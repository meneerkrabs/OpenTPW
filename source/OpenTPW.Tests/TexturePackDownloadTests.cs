using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
public class TexturePackDownloadTests
{
	private const string Prefix = "content/texture-packs/clarity-enhanced/";
	private const string PngPath = "textures/levels/jungle/grass.wct.png";
	private static readonly byte[] Png = { 10, 20, 30, 40 };
	private static readonly byte[] Manifest = Encoding.UTF8.GetBytes( "{\"Format\":1,\"Textures\":1,\"MixedScale\":true}" );
	private static string Hash( byte[] bytes ) => Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant();
	private static string TemporaryDirectory()
	{
		// macOS /tmp and /var are symlinks; the installer deliberately refuses linked write paths.
		var temporary = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
		var root = Path.Combine( temporary, "opentpw-pack-download-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		return root;
	}

	private static byte[] Zip( params (string Name, byte[] Data, int Attributes)[] entries )
	{
		using var memory = new MemoryStream();
		using ( var zip = new ZipArchive( memory, ZipArchiveMode.Create, leaveOpen: true ) )
		{
			foreach ( var item in entries )
			{
				var entry = zip.CreateEntry( item.Name );
				if ( item.Attributes != 0 ) entry.ExternalAttributes = item.Attributes;
				using var stream = entry.Open();
				stream.Write( item.Data );
			}
		}
		return memory.ToArray();
	}

	private static byte[] ValidZip() => Zip( (Prefix + "pack.json", Manifest, 0), (Prefix + PngPath, Png, 0) );
	private static TexturePackDistribution Distribution( byte[] zip ) => new()
	{
		Format = 1, Name = TexturePack.DefaultName, Version = "test-v1", PackManifest = new() { Textures = 1, MixedScale = true },
		Files = new[] { new TexturePackDownloadFile( "pack.json", Manifest.Length, Hash( Manifest ) ), new TexturePackDownloadFile( PngPath, Png.Length, Hash( Png ) ) },
		Archives = new[] { new TexturePackDownloadArchive( "base.zip", "https://example.test/base.zip", zip.Length, Hash( zip ), "textures" ) }
	};

	private sealed class Handler( Func<HttpRequestMessage, int, HttpResponseMessage> send ) : HttpMessageHandler
	{
		public int Calls;
		protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult( send( request, ++Calls ) );
		}
	}

	private static HttpResponseMessage Response( byte[] bytes, long? offset = null )
	{
		var response = new HttpResponseMessage( offset.HasValue ? HttpStatusCode.PartialContent : HttpStatusCode.OK ) { Content = new ByteArrayContent( bytes.Skip( (int)(offset ?? 0) ).ToArray() ) };
		if ( offset.HasValue ) response.Content.Headers.ContentRange = new ContentRangeHeaderValue( offset.Value, bytes.Length - 1, bytes.Length );
		return response;
	}

	[TestMethod]
	public void DescriptorIsPinnedAndUnsafeOrUnboundedIndicesAreRefused()
	{
		var distribution = Distribution( ValidZip() );
		var json = JsonSerializer.SerializeToUtf8Bytes( distribution );
		Assert.AreEqual( TexturePack.DefaultName, TexturePackDownload.ReadDistribution( json, Hash( json ) ).Name );
		Assert.ThrowsException<InvalidDataException>( () => TexturePackDownload.ReadDistribution( json, new string( '0', 64 ) ) );
		foreach ( var bad in new[] { "../outside.png", "textures/../outside.png", "textures\\outside.png", "textures/CON.png", "textures/a.png.", "textures/a.png:other", "/textures/a.png" } )
		{
			var changed = distribution with { Files = new[] { distribution.Files[0], distribution.Files[1] with { Path = bad } } };
			var bytes = JsonSerializer.SerializeToUtf8Bytes( changed );
			Assert.ThrowsException<InvalidDataException>( () => TexturePackDownload.ReadDistribution( bytes, Hash( bytes ) ), bad );
		}
		var oversized = distribution with { Archives = new[] { distribution.Archives[0] with { Size = 2L * 1024 * 1024 * 1024 + 1 } } };
		var oversizedJson = JsonSerializer.SerializeToUtf8Bytes( oversized );
		Assert.ThrowsException<InvalidDataException>( () => TexturePackDownload.ReadDistribution( oversizedJson, Hash( oversizedJson ) ) );
	}

	[TestMethod]
	public async Task CompleteInstallChecksEveryFileSkipsMoviesAndReplacesOnlyAfterVerification()
	{
		var root = TemporaryDirectory();
		try
		{
			var bytes = ValidZip();
			var distribution = Distribution( bytes );
			distribution = distribution with { Archives = distribution.Archives.Append( new TexturePackDownloadArchive( "movies.zip", "https://example.test/movies.zip", 1, new string( '0', 64 ), "movies" ) ).ToArray() };
			var existing = Path.Combine( root, TexturePack.DefaultName );
			Directory.CreateDirectory( existing );
			File.WriteAllText( Path.Combine( existing, "keep-old-until-complete" ), "old" );
			using var handler = new Handler( ( request, call ) => { Assert.AreEqual( "https://example.test/base.zip", request.RequestUri!.AbsoluteUri ); return Response( bytes ); } );
			using var client = new HttpClient( handler );
			var reported = new List<TexturePackDownloadProgress>();
			await new TexturePackDownload( client ).InstallAsync( distribution, root, progress =>
			{
				if ( progress.Phase != "complete" ) Assert.IsTrue( File.Exists( Path.Combine( existing, "keep-old-until-complete" ) ) );
				reported.Add( progress );
			} );
			Assert.AreEqual( 1, handler.Calls );
			CollectionAssert.AreEqual( Png, File.ReadAllBytes( Path.Combine( existing, PngPath ) ) );
			CollectionAssert.AreEqual( Manifest, File.ReadAllBytes( Path.Combine( existing, "pack.json" ) ) );
			Assert.IsFalse( File.Exists( Path.Combine( existing, "keep-old-until-complete" ) ) );
			Assert.AreEqual( "complete", reported.Last().Phase );
			Assert.IsTrue( TexturePack.IsInstalled( existing ) );
			Assert.AreEqual( 0, Directory.GetFiles( root, "*.zip", SearchOption.AllDirectories ).Length, "successful downloads release their temporary disk space" );
		}
		finally { Directory.Delete( root, recursive: true ); }
	}

	[TestMethod]
	public async Task ResumeUsesRangeAndHandlesServersThatIgnoreIt()
	{
		foreach ( var ignoresRange in new[] { false, true } )
		{
			var root = TemporaryDirectory();
			try
			{
				var bytes = ValidZip();
				var distribution = Distribution( bytes );
				var cache = Path.Combine( root, ".clarity-enhanced-download-test-v1" );
				Directory.CreateDirectory( cache );
				File.WriteAllBytes( Path.Combine( cache, "base.zip.partial" ), bytes.Take( 37 ).ToArray() );
				using var handler = new Handler( ( request, call ) =>
				{
					Assert.AreEqual( 37L, request.Headers.Range!.Ranges.Single().From );
					return Response( bytes, ignoresRange ? null : 37 );
				} );
				using var client = new HttpClient( handler );
				await new TexturePackDownload( client ).InstallAsync( distribution, root );
				Assert.IsTrue( TexturePack.IsInstalled( Path.Combine( root, TexturePack.DefaultName ) ) );
				Assert.AreEqual( 1, handler.Calls );
			}
			finally { Directory.Delete( root, recursive: true ); }
		}
	}

	[TestMethod]
	public async Task CorruptArchiveRetriesAndCannotExposeAnIncompletePack()
	{
		var root = TemporaryDirectory();
		try
		{
			var bytes = ValidZip();
			var corrupt = (byte[])bytes.Clone();
			corrupt[10] ^= 0x01;
			using var handler = new Handler( ( request, call ) => Response( corrupt ) );
			using var client = new HttpClient( handler );
			await Assert.ThrowsExceptionAsync<InvalidDataException>( () => new TexturePackDownload( client ).InstallAsync( Distribution( bytes ), root ) );
			Assert.AreEqual( 3, handler.Calls );
			Assert.IsFalse( Directory.Exists( Path.Combine( root, TexturePack.DefaultName ) ) );
			Assert.AreEqual( 0, Directory.GetFiles( root, "pack.json", SearchOption.AllDirectories ).Length );
		}
		finally { Directory.Delete( root, recursive: true ); }
	}

	[TestMethod]
	public async Task PerFileMismatchKeepsTheOldPackAndArchiveTraversalAndLinksAreRefused()
	{
		foreach ( var bytes in new[]
		{
			Zip( (Prefix + "pack.json", Manifest, 0), (Prefix + PngPath, new byte[] { 1, 2, 3, 4 }, 0) ),
			Zip( (Prefix + "pack.json", Manifest, 0), (Prefix + PngPath, Png, 0), (Prefix + "../../escape.png", Png, 0) ),
			Zip( (Prefix + "pack.json", Manifest, 0), (Prefix + PngPath, Png, unchecked((int)0xA1FF0000)) ),
			Zip( (Prefix + "pack.json", Manifest, 0), (Prefix + PngPath, Png, 0), (Prefix + PngPath, Png, 0) ),
			Zip( (Prefix + "pack.json", Manifest, 0) ),
		})
		{
			var root = TemporaryDirectory();
			try
			{
				var existing = Path.Combine( root, TexturePack.DefaultName );
				Directory.CreateDirectory( existing );
				File.WriteAllBytes( Path.Combine( existing, "pack.json" ), Manifest );
				File.WriteAllText( Path.Combine( existing, "sentinel" ), "untouched" );
				using var handler = new Handler( ( request, call ) => Response( bytes ) );
				using var client = new HttpClient( handler );
				await Assert.ThrowsExceptionAsync<InvalidDataException>( () => new TexturePackDownload( client ).InstallAsync( Distribution( bytes ), root ) );
				Assert.AreEqual( "untouched", File.ReadAllText( Path.Combine( existing, "sentinel" ) ) );
				CollectionAssert.AreEqual( Manifest, File.ReadAllBytes( Path.Combine( existing, "pack.json" ) ) );
				Assert.IsFalse( File.Exists( Path.Combine( root, "escape.png" ) ) );
				Assert.IsFalse( Directory.Exists( Path.Combine( root, ".clarity-enhanced-download-test-v1", "staging" ) ) );
			}
			finally { Directory.Delete( root, recursive: true ); }
		}
	}

	private sealed class ChunkedStream( byte[] bytes ) : MemoryStream( bytes )
	{
		public override ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default ) => base.ReadAsync( buffer[..Math.Min( buffer.Length, 17 )], cancellationToken );
	}

	[TestMethod]
	public async Task CancellationRetainsOnlyPartialDownloadAndTheNextAttemptResumes()
	{
		var root = TemporaryDirectory();
		try
		{
			var bytes = ValidZip();
			var distribution = Distribution( bytes );
			using var cancel = new CancellationTokenSource();
			using var handler = new Handler( ( request, call ) => call == 1
				? new HttpResponseMessage( HttpStatusCode.OK ) { Content = new StreamContent( new ChunkedStream( bytes ) ) }
				: Response( bytes, request.Headers.Range!.Ranges.Single().From ) );
			using var client = new HttpClient( handler );
			try
			{
				await new TexturePackDownload( client ).InstallAsync( distribution, root, progress => { if ( progress.Phase == "download" && progress.Done >= 17 ) cancel.Cancel(); }, cancel.Token );
				Assert.Fail( "Cancellation must end the install." );
			}
			catch ( OperationCanceledException ) { Assert.IsTrue( cancel.IsCancellationRequested ); }
			var partial = Path.Combine( root, ".clarity-enhanced-download-test-v1", "base.zip.partial" );
			Assert.AreEqual( 17L, new FileInfo( partial ).Length );
			Assert.IsFalse( Directory.Exists( Path.Combine( root, TexturePack.DefaultName ) ) );
			await new TexturePackDownload( client ).InstallAsync( distribution, root );
			Assert.AreEqual( 2, handler.Calls );
			Assert.IsTrue( TexturePack.IsInstalled( Path.Combine( root, TexturePack.DefaultName ) ) );
		}
		finally { Directory.Delete( root, recursive: true ); }
	}

	[TestMethod]
	public async Task ALinkedConfigOrCacheCannotRedirectInstallation()
	{
		if ( OperatingSystem.IsWindows() ) Assert.Inconclusive( "Creating a Windows symlink needs account privileges; archive-link refusal is tested on every platform." );
		var root = TemporaryDirectory();
		try
		{
			var outside = Path.Combine( root, "outside" );
			Directory.CreateDirectory( outside );
			Directory.CreateSymbolicLink( Path.Combine( root, "linked" ), outside );
			using var handler = new Handler( ( request, call ) => throw new AssertFailedException( "A linked path must fail before HTTP." ) );
			using var client = new HttpClient( handler );
			await Assert.ThrowsExceptionAsync<IOException>( () => new TexturePackDownload( client ).InstallAsync( Distribution( ValidZip() ), Path.Combine( root, "linked" ) ) );
			Assert.AreEqual( 0, Directory.GetFiles( outside, "*", SearchOption.AllDirectories ).Length );
			Assert.AreEqual( 0, handler.Calls );
		}
		finally { Directory.Delete( root, recursive: true ); }
	}

	[TestMethod]
	public void ExplicitOriginalLegacyCustomAndLocalPacksNeverDownload()
	{
		Assert.IsTrue( TexturePackDownloadScreen.NeedsDownload( TexturePack.DefaultName, false, null ) );
		foreach ( var choice in new[] { "", "enhanced", "detailed", "my-pack" } )
			Assert.IsFalse( TexturePackDownloadScreen.NeedsDownload( choice, false, null ) );
		Assert.IsFalse( TexturePackDownloadScreen.NeedsDownload( TexturePack.DefaultName, true, null ) );
		Assert.IsFalse( TexturePackDownloadScreen.NeedsDownload( TexturePack.DefaultName, false, "/local/override" ) );
	}

	private static UiStringTable Strings() => new( "English", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>() );
	private static async Task PumpUntil( UiScreen screen, Func<bool> condition )
	{
		for ( var count = 0; count < 400 && !condition(); count++ )
		{
			screen.Updating!( null! );
			await Task.Delay( 5 );
		}
		Assert.IsTrue( condition(), "The download screen did not reach the expected state." );
	}

	[TestMethod]
	public async Task DownloadScreenCanCancelImmediatelyAndSelectOfflineTextures()
	{
		foreach ( var (button, expected) in new[] { ("original", ""), ("hd", TexturePack.EnhancedName) } )
		{
			var stack = new UiScreenStack();
			var started = new TaskCompletionSource<CancellationToken>( TaskCreationOptions.RunContinuationsAsynchronously );
			string? selected = null;
			var screen = TexturePackDownloadScreen.Create( stack, Strings(), async ( progress, token ) =>
			{
				started.SetResult( token );
				await Task.Delay( Timeout.Infinite, token );
			}, choice => selected = choice );
			stack.Push( screen );
			var cancellation = await started.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
			screen.Find( button )!.Activate();
			Assert.AreEqual( expected, selected );
			Assert.IsTrue( cancellation.IsCancellationRequested );
			Assert.IsNull( stack.Top );
		}
	}

	[TestMethod]
	public async Task FailedDownloadOffersRetryAndOnlyCompletedInstallActivatesClarity()
	{
		var stack = new UiScreenStack();
		var finished = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
		var calls = 0;
		string? selected = null;
		var screen = TexturePackDownloadScreen.Create( stack, Strings(), ( progress, token ) =>
		{
			if ( Interlocked.Increment( ref calls ) == 1 ) throw new IOException( "offline" );
			return finished.Task;
		}, choice => selected = choice );
		stack.Push( screen );
		await PumpUntil( screen, () => screen.Find( "retry" )!.Visible );
		Assert.IsNull( selected );
		screen.Find( "retry" )!.Activate();
		await PumpUntil( screen, () => Volatile.Read( ref calls ) == 2 );
		Assert.IsFalse( screen.Find( "retry" )!.Visible );
		Assert.IsNull( selected );
		finished.SetResult();
		await PumpUntil( screen, () => selected != null );
		Assert.AreEqual( TexturePack.DefaultName, selected );
		Assert.IsNull( stack.Top );
	}

	[TestMethod]
	public async Task RemovingTheScreenCancelsWorkWithoutChangingTheSavedChoice()
	{
		var stack = new UiScreenStack();
		var started = new TaskCompletionSource<CancellationToken>( TaskCreationOptions.RunContinuationsAsynchronously );
		var screen = TexturePackDownloadScreen.Create( stack, Strings(), async ( progress, token ) =>
		{
			started.SetResult( token );
			await Task.Delay( Timeout.Infinite, token );
		}, choice => Assert.Fail( "Closing the game cannot implicitly choose a texture profile." ) );
		stack.Push( screen );
		var cancellation = await started.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
		stack.Clear();
		Assert.IsTrue( cancellation.IsCancellationRequested );
	}

	[TestMethod]
	public async Task StartupActivationAndUntouchedOptionsMakeNoRequestsButExplicitSelectionStartsDownload()
	{
		var root = TemporaryDirectory();
		var previousDirectory = TexturePack.Directory;
		try
		{
			foreach ( var initialChoice in new[] { "", TexturePack.DefaultName } )
			{
				using var handler = new Handler( ( request, call ) => Response( Png ) );
				using var client = new HttpClient( handler );
				var graphics = new GraphicsSettingsService( _ => null, new GraphicsSettings { TexturePackName = initialChoice }, null, () => CompatibilityFlags.Original );
				// The actual startup activation only resolves local/bundled art; it has no downloader callback.
				TexturePack.Activate( graphics.Current.TexturePackName, new List<string>(), root, root );
				Assert.AreEqual( 0, handler.Calls );
				Assert.AreEqual( initialChoice, graphics.Current.TexturePackName, "startup does not rewrite an existing choice" );
				var stack = new UiScreenStack();
				OptionsServices Services() => new()
				{
					Display = new StubDisplaySettings( DisplaySettings.Default ), Options = new GameOptions(), Languages = new[] { "English" }, CurrentLanguage = "English", Graphics = graphics,
					BeginTexturePackSwitch = name =>
					{
						Assert.AreEqual( TexturePack.DefaultName, name );
						stack.Push( TexturePackDownloadScreen.Create( stack, Strings(), async ( progress, token ) =>
						{
							using var response = await client.GetAsync( "https://example.test/explicit-only", token );
							response.EnsureSuccessStatusCode();
						}, _ => { } ) );
						return null;
					}
				};
				var screen = OptionsScreen.Create( stack, Strings(), Services(), () => { } );
				stack.Push( screen );
				screen.Find( "ok" )!.Activate();
				Assert.AreEqual( 0, handler.Calls, "accepting untouched options does not opt into a network download" );
				Assert.IsNull( stack.Top );
				screen = OptionsScreen.Create( stack, Strings(), Services(), () => { } );
				stack.Push( screen );
				screen.Find( "openTpw" )!.Activate();
				var row = (UiButton)stack.Top!.Find( "enhancedTextures" )!;
				// A missing saved Clarity can be explicitly re-selected even when its final value is unchanged.
				for ( var step = 0; step < (initialChoice == TexturePack.DefaultName ? 3 : 1); step++ ) row.Activate();
				Assert.AreEqual( 0, handler.Calls, "changing the pending row does not start a download before OK" );
				stack.Pop();
				screen.Find( "ok" )!.Activate();
				Assert.AreEqual( "texturePackDownload", stack.Top?.Name );
				await PumpUntil( stack.Top!, () => handler.Calls == 1 );
				Assert.AreEqual( TexturePack.DefaultName, graphics.Current.TexturePackName );
				stack.Clear();
			}
		}
		finally { TexturePack.Directory = previousDirectory; Directory.Delete( root, recursive: true ); }
	}

	[TestMethod]
	public void RealReleaseDescriptorAndEveryArchiveEntryUseTheSameInstallerWhitelist()
	{
		var index = Environment.GetEnvironmentVariable( "OPENTPW_CLARITY_DISTRIBUTION_PATH" );
		if ( string.IsNullOrEmpty( index ) ) Assert.Inconclusive( "Set OPENTPW_CLARITY_DISTRIBUTION_PATH to a locally staged distribution.json; no network is used." );
		var directory = Path.GetDirectoryName( index )!;
		var distribution = TexturePackDownload.LoadBundled( directory );
		var files = distribution.Files.ToDictionary( file => file.Path, StringComparer.Ordinal );
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var archive in distribution.Archives.Where( archive => archive.Kind == "textures" ) )
		{
			using var zip = ZipFile.OpenRead( Path.Combine( directory, archive.Name ) );
			foreach ( var entry in zip.Entries ) TexturePackDownload.ValidateEntry( entry, files, seen );
		}
		CollectionAssert.AreEquivalent( files.Keys.ToArray(), seen.ToArray() );
		Assert.IsTrue( files.ContainsKey( "textures/ui/textures/b_rideit!.wct.png" ) );
		Assert.IsTrue( files.ContainsKey( "textures/ui/textures/b_rideit!d.wct.png" ) );
	}
}
