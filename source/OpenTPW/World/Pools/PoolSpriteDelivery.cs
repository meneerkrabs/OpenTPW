using System.Security.Cryptography;
using System.Text.Json;

namespace OpenTPW;

/// <summary>
/// How the pools extension's sprite sets (<c>content/pools/esprites</c>, about 8.8 MB) reach the browser (docs/POOLS.md, "Browser delivery"). The desktop finds them next to the program. The browser build does not embed them: it embeds only
/// the pinned manifest <c>content/pools/esprites.manifest.json</c> (path, size and SHA-256 of every file, made by <c>tools/pools/sprite_manifest.py</c>), the site serves the files as static files, and the page fetches them in the background
/// while the extension is on and hands each one to <see cref="Store"/>, which keeps it only when its path, size and SHA-256 are the pinned ones. A set counts as present only when <em>every</em> file of it is (<see cref="IsComplete"/>), so a half-fetched
/// set is never read.
/// <para>
/// The checks apply only where the files are delivered (<see cref="Delivered"/>, set by the browser's start-up): the desktop reads the folder as it is, which keeps a freshly generated sprite set usable before its manifest is pinned.
/// </para>
/// </summary>
// [EXT:pools-render] OpenTPW's own delivery of its own art; the original game has no pools
public static class PoolSpriteDelivery
{
	/// <summary>The manifest's file name below the pools content.</summary>
	public const string ManifestFileName = "esprites.manifest.json";

	/// <summary>The swim sets: needed whenever the pools extension is on (<see cref="PoolSwimSprites"/>).</summary>
	public const string SwimGroup = "Swim";
	/// <summary>The land guest sets: needed while the pools and the pool guest art extensions are on (<see cref="PoolGuestSprites"/>).</summary>
	public const string LandGroup = "Generic";

	/// <summary>One pinned file: its path below <c>esprites</c> (forward slashes), its size and its SHA-256 as lower case hex.</summary>
	public sealed record Entry( string Path, int Size, string Sha256 );

	/// <summary>Whether the sprite files are delivered one by one (the browser), so completeness is checked against the manifest.</summary>
	public static bool Delivered { get; set; }

	private static readonly object gate = new();
	private static HashSet<string> failed = new( StringComparer.Ordinal );
	private static readonly HashSet<(string Group, string Fallback, bool Failed)> loggedLines = new();

	/// <summary>The folder the files are stored in, or null without pools content.</summary>
	public static string? SpriteDirectory => PoolContent.Directory is { } folder ? System.IO.Path.Combine( folder, "esprites" ) : null;

	/// <summary>
	/// The manifest text of the pools content, or null when it is not there. A build that embeds it (the browser) reads the embedded resource itself, never the copy in the in-memory file system, which
	/// other page calls can write to; the desktop reads the file next to the program.
	/// </summary>
	public static string? ManifestText()
	{
		using ( var stream = typeof( PoolSpriteDelivery ).Assembly.GetManifestResourceStream( "content/pools/" + ManifestFileName ) )
			if ( stream != null )
				return new StreamReader( stream ).ReadToEnd();
		var file = PoolContent.Directory is { } folder ? System.IO.Path.Combine( folder, ManifestFileName ) : null;
		return file != null && File.Exists( file ) ? File.ReadAllText( file ) : null;
	}

	/// <summary>Reads the manifest; throws <see cref="InvalidDataException"/> for anything that is not a well-formed, duplicate-free list of safe relative paths.</summary>
	public static IReadOnlyList<Entry> Parse( string json )
	{
		var entries = new List<Entry>();
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		try
		{
			using var document = JsonDocument.Parse( json );
			if ( document.RootElement.GetProperty( "format" ).GetInt32() != 1 )
				throw new InvalidDataException( "Unsupported pools sprite manifest." );
			foreach ( var item in document.RootElement.GetProperty( "files" ).EnumerateArray() )
			{
				var entry = new Entry( item.GetProperty( "path" ).GetString() ?? "", item.GetProperty( "size" ).GetInt32(), item.GetProperty( "sha256" ).GetString() ?? "" );
				if ( !IsSafePath( entry.Path ) || entry.Size <= 0 || entry.Size > MaximumFileBytes || entry.Sha256.Length != 64 || !entry.Sha256.All( Uri.IsHexDigit ) || entry.Sha256 != entry.Sha256.ToLowerInvariant() || !seen.Add( entry.Path ) )
					throw new InvalidDataException( $"Invalid pools sprite manifest entry '{entry.Path}'." );
				entries.Add( entry );
			}
		}
		catch ( Exception exception ) when ( exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException )
		{
			throw new InvalidDataException( "The pools sprite manifest is not valid: " + exception.Message );
		}
		return entries;
	}

	/// <summary>The largest file a manifest may pin.</summary>
	public const int MaximumFileBytes = 4 * 1024 * 1024;

	private static IReadOnlyList<Entry>? manifest;
	private static string? manifestFolder;
	private static readonly HashSet<string> stored = new( StringComparer.Ordinal );

	/// <summary>The manifest of the pools content in this run (read once per folder), empty when it is missing or broken (a warning says so).</summary>
	public static IReadOnlyList<Entry> Manifest
	{
		get
		{
			var folder = PoolContent.Directory;
			lock ( gate )
			{
				if ( manifest != null && manifestFolder == folder )
					return manifest;
				manifestFolder = folder;
				try
				{
					manifest = ManifestText() is { } text ? Parse( text ) : Array.Empty<Entry>();
				}
				catch ( InvalidDataException exception )
				{
					Log?.Warning( exception.Message );
					manifest = Array.Empty<Entry>();
				}
				return manifest;
			}
		}
	}

	/// <summary>Whether <paramref name="path"/> is a plain relative path of safe characters with no empty, <c>.</c> or <c>..</c> part (so no traversal and no absolute path).</summary>
	public static bool IsSafePath( string path )
		=> path.Length is > 0 and <= 256 && path.All( character => char.IsAsciiLetterOrDigit( character ) || character is '/' or '-' or '_' or '.' )
			&& path.Split( '/' ).All( part => part.Length > 0 && part is not ("." or "..") );

	/// <summary>The manifest entries of one group (<see cref="SwimGroup"/> or <see cref="LandGroup"/>).</summary>
	public static IEnumerable<Entry> Group( string group ) => Manifest.Where( entry => entry.Path.StartsWith( group + "/", StringComparison.Ordinal ) );

	/// <summary>Whether the path, size and SHA-256 of <paramref name="contents"/> are exactly those of a manifest entry.</summary>
	public static bool Verify( string relativePath, ReadOnlySpan<byte> contents )
		=> Verify( Manifest, relativePath, contents );

	internal static bool Verify( IReadOnlyList<Entry> entries, string relativePath, ReadOnlySpan<byte> contents )
	{
		if ( !IsSafePath( relativePath ) )
			return false;
		var entry = entries.FirstOrDefault( item => string.Equals( item.Path, relativePath, StringComparison.Ordinal ) );
		return entry != null && entry.Size == contents.Length
			&& string.Equals( Convert.ToHexStringLower( SHA256.HashData( contents ) ), entry.Sha256, StringComparison.Ordinal );
	}

	/// <summary>
	/// Keeps a fetched file below <c>esprites</c> when <see cref="Verify(string, ReadOnlySpan{byte})"/> accepts it, and returns whether it did. The file appears under its name only once it is complete (written next to it, then moved),
	/// so a reader never sees half a file.
	/// </summary>
	public static bool Store( string relativePath, byte[] contents )
	{
		if ( SpriteDirectory is not { } folder || !Verify( relativePath, contents ) )
			return false;
		var target = System.IO.Path.Combine( folder, relativePath );
		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( target )! );
		var partial = target + ".part";
		File.WriteAllBytes( partial, contents );
		File.Move( partial, target, true );
		lock ( gate )
			stored.Add( relativePath );
		return true;
	}

	/// <summary>
	/// Whether every file of a group has been stored (<see cref="Store"/> verified each against the manifest). Always true where the files are not delivered (<see cref="Delivered"/> is false: the desktop reads the folder as it is).
	/// False for a group the manifest does not know. Cheap enough for a per-frame check.
	/// </summary>
	public static bool IsComplete( string group )
	{
		if ( !Delivered )
			return true;
		lock ( gate )
		{
			var entries = Group( group ).ToArray();
			return entries.Length > 0 && entries.All( entry => stored.Contains( entry.Path ) );
		}
	}

	/// <summary>The page could not fetch the files (offline, not found, or a wrong file): the sets stay absent and the fallbacks are used.</summary>
	public static void MarkFailed( string group )
	{
		lock ( gate )
			failed.Add( group );
	}

	/// <summary>[EXT:extensions-apply-at-load] Forgets that the page gave up, before it fetches the sets again (the extension was switched on after the start, whose fetch skipped them).</summary>
	public static void Retry()
	{
		lock ( gate )
			failed.Clear();
	}

	/// <summary>Whether a pinned file has been stored (<see cref="Store"/>).</summary>
	public static bool IsStored( string relativePath )
	{
		lock ( gate )
			return stored.Contains( relativePath );
	}

	/// <summary>Whether a group is not complete yet but may still arrive: files are delivered, the fetch has not given up and it is not finished.</summary>
	public static bool IsPending( string group )
	{
		if ( !Delivered )
			return false;
		lock ( gate )
			return !failed.Contains( group ) && Group( group ).Any() && !IsComplete( group );
	}

	/// <summary>Forgets the stored files, the failures and the logged notes (a new start of the runtime, tests).</summary>
	internal static void Reset()
	{
		lock ( gate )
		{
			failed = new HashSet<string>( StringComparer.Ordinal );
			loggedLines.Clear();
			stored.Clear();
			manifest = null;
		}
	}

	/// <summary>
	/// Whether a group can be used now; when it cannot, one line says so (each distinct line once) and the caller keeps its fallback.
	/// </summary>
	public static bool Ready( string group, string fallback )
	{
		if ( IsComplete( group ) )
			return true;
		lock ( gate )
		{
			// the line is built only the first time (this runs every frame while swimmers wait for their sets)
			var didFail = failed.Contains( group );
			if ( loggedLines.Add( (group, fallback, didFail) ) )
				Log?.Trace( $"Pools: the {group} sprite sets are not downloaded yet ({(didFail ? "the download failed" : "still downloading")}); {fallback}." );
		}
		return false;
	}
}
