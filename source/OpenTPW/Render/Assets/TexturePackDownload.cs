using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenTPW;

/// <summary>The immutable release index shipped with the game; textures themselves live in release assets.</summary>
// [EXT:texture-pack] Clarity is optional replacement art; downloads never change the original installation.
public sealed record TexturePackDistribution
{
	public int Format { get; init; }
	public string Name { get; init; } = "";
	public string Version { get; init; } = "";
	public TexturePackManifest PackManifest { get; init; } = new();
	public TexturePackDownloadFile[] Files { get; init; } = Array.Empty<TexturePackDownloadFile>();
	public TexturePackDownloadArchive[] Archives { get; init; } = Array.Empty<TexturePackDownloadArchive>();
	public long DownloadSize => Archives.Where( archive => archive.Kind == "textures" ).Sum( archive => archive.Size );
}

public sealed record TexturePackDownloadFile( string Path, long Size, string Sha256 );
public sealed record TexturePackDownloadArchive( string Name, string Url, long Size, string Sha256, string Kind );
public sealed record TexturePackDownloadProgress( string Phase, string File, long Done, long Total );

/// <summary>
/// Downloads a pinned set of independently verifiable archives, retaining partial downloads for HTTP Range resume.
/// Extracts only indexed files into a hidden sibling directory; the completed pack replaces the old directory only
/// after every file has passed its size and SHA-256 check. Cancellation leaves the old pack usable and downloads reusable.
/// </summary>
public sealed class TexturePackDownload
{
	public const string DistributionFileName = "distribution.json";
	public const string DistributionChecksumFileName = "distribution.sha256";
	private const int MaximumIndexSize = 16 * 1024 * 1024;
	private const long MaximumArchiveSize = 2L * 1024 * 1024 * 1024;
	private const long MaximumFileSize = 512L * 1024 * 1024;
	private const long MaximumInstalledSize = 16L * 1024 * 1024 * 1024;
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
	private readonly HttpClient client;

	public TexturePackDownload( HttpClient client ) => this.client = client;

	/// <summary>The shipped checksum pins the local descriptor; no mutable remote manifest or installation script is run.</summary>
	public static TexturePackDistribution LoadBundled( string? directory = null )
	{
		directory ??= TexturePack.PackDirectory( TexturePack.DefaultName, TexturePack.BundledPacksDirectory() );
		var index = Path.Combine( directory, DistributionFileName );
		if ( new FileInfo( index ).Length > MaximumIndexSize )
			throw new InvalidDataException( "The texture pack index is too large." );
		var pin = File.ReadAllText( Path.Combine( directory, DistributionChecksumFileName ) ).Trim().Split( ' ', '\t', '\r', '\n' )[0];
		return ReadDistribution( File.ReadAllBytes( index ), pin );
	}

	public static TexturePackDistribution ReadDistribution( byte[] index, string sha256 )
	{
		if ( index.Length > MaximumIndexSize || !IsHash( sha256 ) || !HashMatches( index, sha256 ) )
			throw new InvalidDataException( "The texture pack index failed its checksum." );
		var distribution = JsonSerializer.Deserialize<TexturePackDistribution>( index, JsonOptions )
			?? throw new InvalidDataException( "The texture pack index is empty." );
		Validate( distribution );
		return distribution;
	}

	private static bool IsHash( string? hash ) => hash is { Length: 64 } && hash.All( char.IsAsciiHexDigit );
	private static bool HashMatches( byte[] bytes, string hash ) => Convert.ToHexString( SHA256.HashData( bytes ) ).Equals( hash, StringComparison.OrdinalIgnoreCase );

	private static void Validate( TexturePackDistribution distribution )
	{
		if ( distribution.Format != 1 || distribution.Name != TexturePack.DefaultName || !TexturePack.IsValidName( distribution.Version ) || distribution.Version.Length > 80 || distribution.PackManifest == null || distribution.PackManifest.Format != TexturePack.FormatVersion )
			throw new InvalidDataException( "Unsupported texture pack distribution." );
		if ( distribution.Files == null || distribution.Archives == null || distribution.Files.Length is < 2 or > 100000 || distribution.Archives.Length is < 1 or > 20 )
			throw new InvalidDataException( "Invalid texture pack file count." );
		var files = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		long total = 0;
		foreach ( var file in distribution.Files )
		{
			if ( file == null || !SafeRelativePath( file.Path ) || !(file.Path == TexturePack.ManifestFileName || file.Path.StartsWith( "textures/", StringComparison.Ordinal ) && file.Path.EndsWith( ".png", StringComparison.Ordinal ))
				|| file.Size is < 1 or > MaximumFileSize || !IsHash( file.Sha256 ) || !files.Add( file.Path ) )
				throw new InvalidDataException( "Invalid or duplicate texture pack file." );
			total += file.Size;
			if ( total > MaximumInstalledSize )
				throw new InvalidDataException( "The installed texture pack is too large." );
		}
		if ( !files.Contains( TexturePack.ManifestFileName ) )
			throw new InvalidDataException( "The texture pack manifest is missing." );
		var archives = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var archive in distribution.Archives )
		{
			if ( archive == null || !TexturePack.IsValidName( archive.Name ) || !SafeRelativePath( archive.Name ) || !archive.Name.EndsWith( ".zip", StringComparison.Ordinal ) || archive.Size is < 1 or > MaximumArchiveSize
				|| !IsHash( archive.Sha256 ) || !archives.Add( archive.Name ) || archive.Kind is not ("textures" or "movies")
				|| !Uri.TryCreate( archive.Url, UriKind.Absolute, out var url ) || url.Scheme != "https" || url.UserInfo.Length > 0 || url.Fragment.Length > 0 )
				throw new InvalidDataException( "Invalid texture pack archive." );
		}
		if ( distribution.DownloadSize <= 0 )
			throw new InvalidDataException( "No texture archives were provided." );
	}

	private static bool SafeRelativePath( string? path ) => !string.IsNullOrEmpty( path ) && path.Length <= 500 && !path.Contains( '\\' ) && !path.Contains( ':' )
		&& path.Split( '/' ).All( part => part.Length > 0 && part is not ("." or "..") && !part.EndsWith( '.' ) && !part.EndsWith( ' ' ) && !part.Any( character => character < ' ' || character is '<' or '>' or '"' or '|' or '?' or '*' )
			&& !IsReservedWindowsName( part ) );

	private static bool IsReservedWindowsName( string part )
	{
		var name = part.Split( '.' )[0].ToUpperInvariant();
		return name is "CON" or "PRN" or "AUX" or "NUL" || name.Length == 4 && (name.StartsWith( "COM" ) || name.StartsWith( "LPT" )) && name[3] is >= '1' and <= '9';
	}

	/// <summary>The same whitelist validation is used for extraction and for read-only release layout checks.</summary>
	internal static TexturePackDownloadFile? ValidateEntry( ZipArchiveEntry entry, IReadOnlyDictionary<string, TexturePackDownloadFile> expected, ISet<string> extracted )
	{
		const string prefix = "content/texture-packs/clarity-enhanced/";
		var mode = (entry.ExternalAttributes >> 16) & 0xF000;
		if ( mode != 0 && mode is not (0x8000 or 0x4000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 )
			throw new InvalidDataException( "The texture archive contains a link or special file." );
		var name = entry.FullName;
		if ( name.EndsWith( '/' ) )
		{
			if ( !SafeRelativePath( name.TrimEnd( '/' ) ) || entry.Length != 0 )
				throw new InvalidDataException( "Invalid archive directory." );
			return null;
		}
		if ( !name.StartsWith( prefix, StringComparison.Ordinal ) || !SafeRelativePath( name ) || !expected.TryGetValue( name[prefix.Length..], out var file ) || !extracted.Add( file.Path ) || entry.Length != file.Size )
			throw new InvalidDataException( "The archive contains an unexpected, duplicate or incorrectly sized file." );
		return file;
	}

	/// <summary>Installs textures only; optional replacement movies remain a separate manual download.</summary>
	public async Task InstallAsync( TexturePackDistribution distribution, string packsDirectory, Action<TexturePackDownloadProgress>? progress = null, CancellationToken cancellation = default )
	{
		Validate( distribution );
		packsDirectory = Path.GetFullPath( packsDirectory );
		EnsureSafeDirectory( packsDirectory );
		var cache = Path.Combine( packsDirectory, $".{distribution.Name}-download-{distribution.Version}" );
		EnsureSafeDirectory( cache );
		// One installer may write this cache at a time; a second process cannot disturb partial downloads.
		var lockPath = Path.Combine( cache, "install.lock" );
		RejectLinks( lockPath );
		using var held = new FileStream( lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None );
		var staging = Path.Combine( cache, "staging" );
		var backup = Path.Combine( cache, "previous" );
		var destination = Path.Combine( packsDirectory, distribution.Name );
		RejectLinks( destination );
		RejectLinks( backup );
		// Recover a process interrupted between the two directory moves before attempting another install.
		if ( !System.IO.Directory.Exists( destination ) && System.IO.Directory.Exists( backup ) )
			System.IO.Directory.Move( backup, destination );
		DeleteSafeTree( staging );
		EnsureSafeDirectory( staging );
		try
		{
			var archives = distribution.Archives.Where( archive => archive.Kind == "textures" ).ToArray();
			long completed = 0;
			foreach ( var archive in archives )
			{
				cancellation.ThrowIfCancellationRequested();
				await DownloadAsync( archive, cache, done => progress?.Invoke( new( "download", archive.Name, completed + done, distribution.DownloadSize ) ), cancellation );
				completed += archive.Size;
			}
			var expected = distribution.Files.ToDictionary( file => file.Path, StringComparer.Ordinal );
			var extracted = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			byte[]? manifest = null;
			foreach ( var archive in archives )
			{
				using var zip = ZipFile.OpenRead( Path.Combine( cache, archive.Name ) );
				if ( zip.Entries.Count > 100100 )
					throw new InvalidDataException( "Too many entries in the texture archive." );
				foreach ( var entry in zip.Entries )
				{
					cancellation.ThrowIfCancellationRequested();
					var file = ValidateEntry( entry, expected, extracted );
					if ( file == null ) continue;
					progress?.Invoke( new( "verify", file.Path, extracted.Count - 1, expected.Count ) );
					using var input = entry.Open();
					if ( file.Path == TexturePack.ManifestFileName )
					{
						if ( file.Size > 1024 * 1024 )
							throw new InvalidDataException( "The pack manifest is too large." );
						using var memory = new MemoryStream();
						await CopyVerifiedAsync( input, memory, file.Size, file.Sha256, null, cancellation );
						manifest = memory.ToArray();
						if ( JsonSerializer.Deserialize<TexturePackManifest>( manifest, JsonOptions )?.Format != TexturePack.FormatVersion )
							throw new InvalidDataException( "Unsupported pack manifest." );
					}
					else
					{
						var target = Path.Combine( staging, file.Path.Replace( '/', Path.DirectorySeparatorChar ) );
						EnsureSafeDirectory( Path.GetDirectoryName( target )! );
						RejectLinks( target );
						using var output = new FileStream( target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true );
						await CopyVerifiedAsync( input, output, file.Size, file.Sha256, null, cancellation );
					}
				}
			}
			if ( extracted.Count != expected.Count || manifest == null )
				throw new InvalidDataException( "The texture archives do not contain the complete indexed pack." );
			cancellation.ThrowIfCancellationRequested();
			await File.WriteAllBytesAsync( Path.Combine( staging, TexturePack.ManifestFileName ), manifest, cancellation );
			cancellation.ThrowIfCancellationRequested();
			DeleteSafeTree( backup );
			RejectLinks( destination );
			if ( System.IO.Directory.Exists( destination ) )
				System.IO.Directory.Move( destination, backup );
			try { System.IO.Directory.Move( staging, destination ); }
			catch
			{
				if ( System.IO.Directory.Exists( backup ) && !System.IO.Directory.Exists( destination ) )
					System.IO.Directory.Move( backup, destination );
				throw;
			}
			DeleteSafeTree( backup );
			progress?.Invoke( new( "complete", "", expected.Count, expected.Count ) );
			// Successful archives no longer need disk space; cancellation/failure keeps them for retry.
			foreach ( var archive in archives ) File.Delete( Path.Combine( cache, archive.Name ) );
		}
		finally { DeleteSafeTree( staging ); }
	}

	private async Task DownloadAsync( TexturePackDownloadArchive archive, string cache, Action<long> progress, CancellationToken cancellation )
	{
		var complete = Path.Combine( cache, archive.Name );
		var partial = complete + ".partial";
		RejectLinks( complete );
		RejectLinks( partial );
		if ( File.Exists( complete ) && await FileMatchesAsync( complete, archive, cancellation ) )
		{
			progress( archive.Size );
			return;
		}
		File.Delete( complete );
		for ( var attempt = 0; ; attempt++ )
		{
			try
			{
				var offset = File.Exists( partial ) ? new FileInfo( partial ).Length : 0;
				if ( offset > archive.Size ) { File.Delete( partial ); offset = 0; }
				if ( offset < archive.Size )
				{
					using var request = new HttpRequestMessage( HttpMethod.Get, archive.Url );
					request.Headers.AcceptEncoding.Add( new StringWithQualityHeaderValue( "identity" ) );
					if ( offset > 0 ) request.Headers.Range = new RangeHeaderValue( offset, null );
					using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource( cancellation );
					headerTimeout.CancelAfter( TimeSpan.FromMinutes( 2 ) );
					using var response = await client.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token );
					headerTimeout.CancelAfter( Timeout.InfiniteTimeSpan );
					response.EnsureSuccessStatusCode();
					if ( response.RequestMessage?.RequestUri is { Scheme: not "https" } )
						throw new InvalidDataException( "The texture download was redirected to an insecure address." );
					if ( response.StatusCode == HttpStatusCode.PartialContent )
					{
						var range = response.Content.Headers.ContentRange;
						if ( range?.From != offset || range.To != archive.Size - 1 || range.Length != archive.Size || range.Unit != "bytes" )
							throw new InvalidDataException( "Invalid texture archive resume response." );
					}
					else if ( response.StatusCode == HttpStatusCode.OK ) offset = 0;
					else throw new InvalidDataException( "Unexpected texture archive response." );
					if ( response.Content.Headers.ContentLength is { } length && length != archive.Size - offset )
						throw new IOException( "The texture archive response has an incorrect length." );
					using var stream = await response.Content.ReadAsStreamAsync( cancellation );
					using var output = new FileStream( partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true );
					progress( offset );
					await CopyBoundedAsync( stream, output, archive.Size - offset, done => progress( offset + done ), cancellation, idleTimeout: true );
				}
				if ( !await FileMatchesAsync( partial, archive, cancellation ) )
				{
					File.Delete( partial );
					throw new InvalidDataException( "The texture archive failed its checksum." );
				}
				File.Move( partial, complete );
				return;
			}
			catch ( Exception exception ) when ( attempt < 2 && !cancellation.IsCancellationRequested && exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException )
			{
				await Task.Delay( TimeSpan.FromMilliseconds( 250 * (attempt + 1) ), cancellation );
			}
		}
	}

	private static async Task<bool> FileMatchesAsync( string path, TexturePackDownloadArchive archive, CancellationToken cancellation )
	{
		if ( new FileInfo( path ).Length != archive.Size ) return false;
		using var file = File.OpenRead( path );
		return Convert.ToHexString( await SHA256.HashDataAsync( file, cancellation ) ).Equals( archive.Sha256, StringComparison.OrdinalIgnoreCase );
	}

	private static async Task CopyVerifiedAsync( Stream input, Stream output, long size, string hash, Action<long>? progress, CancellationToken cancellation )
	{
		using var digest = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
		await CopyBoundedAsync( input, output, size, progress, cancellation, hash: digest );
		if ( !Convert.ToHexString( digest.GetHashAndReset() ).Equals( hash, StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidDataException( "A texture pack file failed its checksum." );
	}

	private static async Task CopyBoundedAsync( Stream input, Stream output, long size, Action<long>? progress, CancellationToken cancellation, bool idleTimeout = false, IncrementalHash? hash = null )
	{
		var buffer = new byte[81920];
		long done = 0;
		while ( true )
		{
			using var readTimeout = idleTimeout ? CancellationTokenSource.CreateLinkedTokenSource( cancellation ) : null;
			readTimeout?.CancelAfter( TimeSpan.FromSeconds( 60 ) );
			var read = await input.ReadAsync( buffer, readTimeout?.Token ?? cancellation );
			if ( read == 0 ) break;
			if ( done + read > size ) throw new InvalidDataException( "The texture download exceeds its indexed size." );
			hash?.AppendData( buffer, 0, read );
			await output.WriteAsync( buffer.AsMemory( 0, read ), cancellation );
			done += read;
			progress?.Invoke( done );
		}
		if ( done != size ) throw new IOException( "The texture download ended before its indexed size." );
	}

	private static void EnsureSafeDirectory( string path )
	{
		RejectLinks( path );
		System.IO.Directory.CreateDirectory( path );
	}

	private static void RejectLinks( string path )
	{
		for ( string? current = Path.GetFullPath( path ); current != null; current = Path.GetDirectoryName( current ) )
		{
			// LinkTarget also detects dangling links, for which File.Exists/Directory.Exists are false.
			var info = new FileInfo( current );
			if ( info.LinkTarget != null || info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0 )
				throw new IOException( "A texture pack installation path contains a link." );
		}
	}

	private static void DeleteSafeTree( string path )
	{
		RejectLinks( path );
		if ( !System.IO.Directory.Exists( path ) ) return;
		foreach ( var entry in System.IO.Directory.EnumerateFileSystemEntries( path ) )
		{
			RejectLinks( entry );
			if ( System.IO.Directory.Exists( entry ) ) DeleteSafeTree( entry ); else File.Delete( entry );
		}
		System.IO.Directory.Delete( path );
	}
}
