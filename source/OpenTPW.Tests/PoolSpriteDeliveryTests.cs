using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using OpenTPW.Mods;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The browser delivery of the pools extension's sprite sets (docs/POOLS.md, "Browser delivery"): the pinned manifest agrees with the shipped files, the web project embeds the manifest but not the sprites,
/// the game keeps a fetched file only when its path, size and SHA-256 are pinned, and a set is used only when complete.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PoolSpriteDeliveryTests
{
	private string pools = null!;
	private string work = null!;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		pools = ShippedPools();
		work = Directory.CreateTempSubdirectory( "opentpw-pool-delivery-" ).FullName;
		File.Copy( Path.Combine( pools, PoolSpriteDelivery.ManifestFileName ), Path.Combine( work, PoolSpriteDelivery.ManifestFileName ) );
		PoolContent.DirectoryOverride = work;
		PoolSpriteDelivery.Reset();
		PoolSpriteDelivery.Delivered = true;
	}

	[TestCleanup]
	public void Cleanup()
	{
		PoolSpriteDelivery.Delivered = false;
		PoolSpriteDelivery.Reset();
		PoolContent.DirectoryOverride = null;
		Directory.Delete( work, true );
	}

	private static string ShippedPools()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var candidate = Path.Combine( directory.FullName, "content", "pools" );
			if ( File.Exists( Path.Combine( candidate, "objects.json" ) ) && Directory.Exists( Path.Combine( directory.FullName, "tools", "pools" ) ) )
				return candidate;
		}
		throw new FileNotFoundException( "content/pools not found above the test binaries." );
	}

	private IEnumerable<(PoolSpriteDelivery.Entry Entry, byte[] Bytes)> Shipped()
		=> PoolSpriteDelivery.Manifest.Select( entry => (entry, File.ReadAllBytes( Path.Combine( pools, "esprites", entry.Path ) )) );

	[TestMethod]
	public void TheManifestPinsEveryShippedSpriteFileAndNothingElse()
	{
		var folder = Path.Combine( pools, "esprites" );
		var files = Directory.EnumerateFiles( folder, "*", SearchOption.AllDirectories ).Select( file => Path.GetRelativePath( folder, file ).Replace( '\\', '/' ) ).Order( StringComparer.Ordinal ).ToArray();
		var manifest = PoolSpriteDelivery.Parse( File.ReadAllText( Path.Combine( pools, PoolSpriteDelivery.ManifestFileName ) ) );
		CollectionAssert.AreEqual( files, manifest.Select( entry => entry.Path ).ToArray(), "run tools/pools/sprite_manifest.py --write after changing the sprite sets" );
		foreach ( var entry in manifest )
		{
			var bytes = File.ReadAllBytes( Path.Combine( folder, entry.Path ) );
			Assert.AreEqual( entry.Size, bytes.Length, entry.Path );
			Assert.AreEqual( Convert.ToHexStringLower( SHA256.HashData( bytes ) ), entry.Sha256, entry.Path );
		}
		Assert.IsTrue( manifest.Any( entry => entry.Path.StartsWith( "Swim/" ) ) && manifest.Any( entry => entry.Path.StartsWith( "Generic/Kids/" ) ) && manifest.Any( entry => entry.Path.StartsWith( "Generic/Kidsheads/" ) ) );
		Assert.IsTrue( new FileInfo( Path.Combine( pools, PoolSpriteDelivery.ManifestFileName ) ).Length < 16_000, "the embedded manifest is the only part of the delivery in the startup download" );
	}

	[TestMethod]
	public void TheWebProjectEmbedsTheManifestButNotTheSprites()
	{
		var project = System.Xml.Linq.XDocument.Load( Path.Combine( pools, "..", "..", "source", "OpenTPW.Web", "OpenTPW.Web.csproj" ) );
		var embed = project.Descendants( "EmbeddedResource" ).Single( item => ( (string?)item.Attribute( "Include" ) ?? "" ).Contains( "content/pools" ) );
		Assert.AreEqual( "../../content/pools/**/*", (string?)embed.Attribute( "Include" ) );
		Assert.AreEqual( "../../content/pools/esprites/**/*", (string?)embed.Attribute( "Exclude" ) );
		// the manifest sits beside esprites, not in it, so the include above catches it and the exclusion does not
		Assert.IsTrue( File.Exists( Path.Combine( pools, "esprites.manifest.json" ) ) );
		// and the page publishes the sprite files as static files of its own origin
		var assets = project.Descendants( "Content" ).Single( item => ( (string?)item.Attribute( "Include" ) ?? "" ).Contains( "content/pools/esprites" ) );
		Assert.AreEqual( "../../content/pools/esprites/**/*", (string?)assets.Attribute( "Include" ) );
		StringAssert.StartsWith( (string?)assets.Attribute( "Link" ), "wwwroot/pools-sprites/" );
	}

	[TestMethod]
	public void TheServerImageCopiesThePoolsContentIntoTheWebBuild()
	{
		var dockerfile = File.ReadAllText( Path.Combine( pools, "..", "..", "source", "OpenTPW.Server", "Dockerfile" ) );
		Assert.IsTrue( dockerfile.Contains( "COPY content/pools/ content/pools/" ), "the deployed page embeds the objects and publishes the sprite files from content/pools" );
	}

	[TestMethod]
	public void VerifyAcceptsTheCorrectBytesAndRejectsEverythingElse()
	{
		var (entry, bytes) = Shipped().First();
		Assert.IsTrue( PoolSpriteDelivery.Verify( entry.Path, bytes ) );
		Assert.IsFalse( PoolSpriteDelivery.Verify( entry.Path, bytes.AsSpan( 0, bytes.Length - 1 ) ), "wrong size" );
		Assert.IsFalse( PoolSpriteDelivery.Verify( entry.Path, bytes.Concat( new byte[] { 0 } ).ToArray() ), "wrong size" );
		var flipped = (byte[])bytes.Clone();
		flipped[^1] ^= 1;
		Assert.IsFalse( PoolSpriteDelivery.Verify( entry.Path, flipped ), "right size, wrong hash" );
		var other = Shipped().First( item => item.Entry.Path != entry.Path && item.Entry.Size == bytes.Length ).Bytes;
		Assert.IsFalse( PoolSpriteDelivery.Verify( entry.Path, other ), "another pinned file's bytes under this name" );
		Assert.IsFalse( PoolSpriteDelivery.Verify( "Swim/UNKNOWN.ESP", bytes ), "unknown path" );
		Assert.IsFalse( PoolSpriteDelivery.Verify( entry.Path.ToLowerInvariant(), bytes ), "paths are exact" );
		Assert.IsFalse( PoolSpriteDelivery.Verify( "", bytes ) );
	}

	[TestMethod]
	public void StoreRefusesTraversalAbsolutePathsAndUnknownFilesAndWritesNothingOutsideTheFolder()
	{
		var (entry, bytes) = Shipped().First();
		foreach ( var path in new[] { "../" + entry.Path, "Swim/../../" + entry.Path, "/" + entry.Path, "Swim//SPR_BE.ESP", "./" + entry.Path, "..\\" + entry.Path, "Swim/SPR_BE.ESP/", "Swim/SPR_BE.ESP\0", "../../etc/passwd", "esprites.manifest.json", "objects.json" } )
			Assert.IsFalse( PoolSpriteDelivery.Store( path, bytes ), path );
		// a manifest that pins a traversal path is invalid, so its entry can never be accepted
		var evil = $"{{\"format\":1,\"files\":[{{\"path\":\"../x.bin\",\"size\":1,\"sha256\":\"{new string( 'a', 64 )}\"}}]}}";
		Assert.ThrowsException<InvalidDataException>( () => PoolSpriteDelivery.Parse( evil ) );
		Assert.IsFalse( File.Exists( Path.Combine( work, "x.bin" ) ) );
		Assert.IsFalse( Directory.Exists( Path.Combine( work, "esprites" ) ) && Directory.EnumerateFiles( Path.Combine( work, "esprites" ), "*", SearchOption.AllDirectories ).Any(), "nothing was written" );
		Assert.IsTrue( PoolSpriteDelivery.Store( entry.Path, bytes ) );
		Assert.IsTrue( File.Exists( Path.Combine( work, "esprites", entry.Path ) ) );
		Assert.IsFalse( Directory.EnumerateFiles( work, "*.part", SearchOption.AllDirectories ).Any(), "no half file stays behind" );
	}

	[TestMethod]
	public void ManifestParsingRejectsMalformedLists()
	{
		var hash = new string( 'a', 64 );
		string One( string path, string size = "1", string? sha = null ) => $"{{\"format\":1,\"files\":[{{\"path\":\"{path}\",\"size\":{size},\"sha256\":\"{sha ?? hash}\"}}]}}";
		Assert.AreEqual( 1, PoolSpriteDelivery.Parse( One( "Swim/A.ESP" ) ).Count );
		foreach ( var bad in new[] { One( "Swim/A.ESP", "0" ), One( "Swim/A.ESP", "-3" ), One( "Swim/A.ESP", "99999999" ), One( "Swim/A.ESP", sha: "ABC" ), One( "Swim/A.ESP", sha: new string( 'A', 64 ) ), One( "a b" ), One( "/abs" ), One( "a/./b" ), One( "a//b" ),
			"{\"format\":2,\"files\":[]}", "{}", "not json", "{\"format\":1,\"files\":[{\"path\":\"a\",\"size\":1,\"sha256\":\"" + hash + "\"},{\"path\":\"a\",\"size\":1,\"sha256\":\"" + hash + "\"}]}" } )
			Assert.ThrowsException<InvalidDataException>( () => PoolSpriteDelivery.Parse( bad ), bad );
	}

	[TestMethod]
	public void ASetIsUsedOnlyWhenEveryFileOfItIsStored()
	{
		var swim = Shipped().Where( item => item.Entry.Path.StartsWith( "Swim/" ) ).ToArray();
		var land = Shipped().Where( item => item.Entry.Path.StartsWith( "Generic/" ) ).ToArray();
		Assert.IsFalse( PoolSpriteDelivery.IsComplete( PoolSpriteDelivery.SwimGroup ) );
		Assert.IsTrue( PoolSpriteDelivery.IsPending( PoolSpriteDelivery.SwimGroup ) );
		Assert.AreEqual( 0, PoolSwimSprites.Load().Count, "nothing downloaded: no swim sets" );
		foreach ( var (entry, bytes) in swim.Take( swim.Length - 1 ) )
			Assert.IsTrue( PoolSpriteDelivery.Store( entry.Path, bytes ) );
		Assert.IsFalse( PoolSpriteDelivery.IsComplete( PoolSpriteDelivery.SwimGroup ), "one file missing" );
		Assert.AreEqual( 0, PoolSwimSprites.Load().Count, "a half-downloaded set is never read" );
		PoolSpriteDelivery.MarkFailed( PoolSpriteDelivery.SwimGroup );
		Assert.IsFalse( PoolSpriteDelivery.IsPending( PoolSpriteDelivery.SwimGroup ), "a failed download is not waited for" );
		Assert.IsTrue( PoolSpriteDelivery.Store( swim[^1].Entry.Path, swim[^1].Bytes ) );
		Assert.IsTrue( PoolSpriteDelivery.IsComplete( PoolSpriteDelivery.SwimGroup ) );
		Assert.AreEqual( 8, PoolSwimSprites.Load().Count );
		Assert.IsFalse( PoolSpriteDelivery.IsComplete( PoolSpriteDelivery.LandGroup ), "the land sets are a group of their own" );
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var kids = PoolSpriteDelivery.Group( PoolSpriteDelivery.LandGroup ).Count();
		Assert.AreEqual( 48 - swim.Length, kids );
		foreach ( var (entry, bytes) in land )
			Assert.IsTrue( PoolSpriteDelivery.Store( entry.Path, bytes ) );
		Assert.IsTrue( PoolSpriteDelivery.IsComplete( PoolSpriteDelivery.LandGroup ) );
		Assert.IsFalse( PoolSpriteDelivery.IsComplete( "Nope" ), "a group the manifest does not know" );
	}

	[TestMethod]
	public void TheLandGuestsKeepTheGamesArtUntilTheirSetsAreComplete()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var gameKids = PoolsContentTests.GameKids( PoolsContentTests.TpwKidNames );
		Assert.IsNull( PoolGuestSprites.LoadReplacement( gameKids ), "nothing downloaded yet: the game's guests" );
		var land = Shipped().Where( item => item.Entry.Path.StartsWith( "Generic/" ) ).ToArray();
		foreach ( var (entry, bytes) in land.Take( land.Length - 1 ) )
			PoolSpriteDelivery.Store( entry.Path, bytes );
		Assert.IsNull( PoolGuestSprites.LoadReplacement( gameKids ), "one file missing: still the game's guests" );
		PoolSpriteDelivery.Store( land[^1].Entry.Path, land[^1].Bytes );
		Assert.IsNotNull( PoolGuestSprites.LoadReplacement( gameKids ), "complete: the extension's 2x sets" );
	}

	[TestMethod]
	public void WhereTheFilesAreNotDeliveredTheFolderIsReadAsItIs()
	{
		PoolSpriteDelivery.Delivered = false;
		PoolContent.DirectoryOverride = pools;
		Assert.IsTrue( PoolSpriteDelivery.IsComplete( PoolSpriteDelivery.SwimGroup ) );
		Assert.AreEqual( 8, PoolSwimSprites.Load().Count );
	}

	[TestMethod]
	public void TheSwimmerRendererWaitsForTheSetsAndIsMadeTheFrameTheyAreComplete()
	{
		var swim = Shipped().Where( item => item.Entry.Path.StartsWith( "Swim/" ) ).ToArray();
		bool Pending() => PoolSpriteDelivery.IsPending( PoolSpriteDelivery.SwimGroup );
		Assert.AreEqual( Level.SwimSpriteStep.Wait, Level.NextSwimSpriteStep( false, false, true, Pending() ), "nothing downloaded" );
		foreach ( var (entry, bytes) in swim.Take( swim.Length - 1 ) )
			PoolSpriteDelivery.Store( entry.Path, bytes );
		Assert.AreEqual( Level.SwimSpriteStep.Wait, Level.NextSwimSpriteStep( false, false, true, Pending() ), "one file missing" );
		Assert.AreEqual( Level.SwimSpriteStep.Done, Level.NextSwimSpriteStep( false, false, false, Pending() ), "no water yet: nothing to draw" );
		PoolSpriteDelivery.Store( swim[^1].Entry.Path, swim[^1].Bytes );
		Assert.AreEqual( Level.SwimSpriteStep.Create, Level.NextSwimSpriteStep( false, false, true, Pending() ), "complete: made now" );
		Assert.AreEqual( Level.SwimSpriteStep.Done, Level.NextSwimSpriteStep( true, true, true, Pending() ), "once" );
		Assert.AreEqual( 8, PoolSwimSprites.Load().Count );
	}

	[TestMethod]
	public void AFailedDownloadOrAnEmptyManifestIsNotWaitedFor()
	{
		PoolSpriteDelivery.MarkFailed( PoolSpriteDelivery.SwimGroup );
		Assert.AreEqual( Level.SwimSpriteStep.Create, Level.NextSwimSpriteStep( false, false, true, PoolSpriteDelivery.IsPending( PoolSpriteDelivery.SwimGroup ) ), "failed: the one warning, then the kid sprite" );
		PoolSpriteDelivery.Reset();
		File.WriteAllText( Path.Combine( work, PoolSpriteDelivery.ManifestFileName ), "{\"format\":1,\"files\":[]}" );
		Assert.IsFalse( PoolSpriteDelivery.IsPending( PoolSpriteDelivery.SwimGroup ), "no manifest entries: nothing will arrive, so nothing is pending" );
	}

	[TestMethod]
	public void TheServerMapsEveryExtensionOfTheManifestSoItServesTheFiles()
	{
		var extensions = PoolSpriteDelivery.Manifest.Select( entry => Path.GetExtension( entry.Path ).ToLowerInvariant() ).Distinct();
		foreach ( var extension in extensions )
			CollectionAssert.Contains( OpenTPW.Server.WebClient.PoolSpriteExtensions, extension, $"WebClient serves no unknown file types: {extension} needs a mapping" );
		Assert.IsFalse( new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider().TryGetContentType( "x.esp", out _ ), "the default map does not know them" );
	}
}
