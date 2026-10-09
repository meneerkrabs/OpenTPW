namespace OpenTPW;

/// <summary>Why a read-only data root is mounted.</summary>
public enum DataOverlayRole
{
	/// <summary>Extracted original CD/ISO (<c>--cd-data</c>): fills in files a minimal install lacks (movies, music).</summary>
	CdFallback,
	/// <summary>Bonus content (<c>--bonus-data</c>, rides slice): extra levels/objects.</summary>
	Bonus,
}

/// <summary>A read-only directory that mirrors the game's <c>Data</c> folder.</summary>
public sealed record DataOverlayRoot( string Name, string DataDirectory, DataOverlayRole Role );

/// <summary>
/// The base <c>Data</c> folder plus ordered read-only overlays. Lookups prefer the base install and
/// fall back to the overlays in order, matching every path segment case-insensitively (CD images
/// mix <c>Data</c>/<c>data</c>, <c>Music</c>/<c>music</c>). Archive members are resolved through
/// their archive (<c>levels/jungle/Music/MusicHD.sdt/level4c.mp2</c> resolves the <c>.sdt</c>).
/// Nothing is ever copied into the game folder. Shared by <c>--cd-data</c> and <c>--bonus-data</c>.
/// </summary>
public sealed class DataRoots
{
	private static readonly string[] ArchiveExtensions = { ".wad", ".sdt" };
	private readonly List<DataOverlayRoot> overlays = new();

	public DataRoots( string baseDataDirectory )
	{
		BaseDataDirectory = Path.GetFullPath( baseDataDirectory );
	}

	public string BaseDataDirectory { get; }
	public IReadOnlyList<DataOverlayRoot> Overlays => overlays;

	/// <summary>
	/// Accepts an extracted CD root (containing <c>Data</c>, any case) or a <c>Data</c> folder itself.
	/// Returns the data directory or null when neither shape is present.
	/// </summary>
	public static string? FindDataDirectory( string root )
	{
		if ( !Directory.Exists( root ) )
			return null;
		if ( GameLanguage.FindEntry( root, "Data", true ) is { } data )
			return data;
		// A Data folder has at least one of these well-known children.
		return new[] { "levels", "Movies", "global", "Language" }.Any( child => GameLanguage.FindEntry( root, child, true ) != null ) ? Path.GetFullPath( root ) : null;
	}

	/// <summary>Adds an overlay; throws when the directory does not look like a CD root or Data folder.</summary>
	public DataOverlayRoot Add( string name, string root, DataOverlayRole role )
	{
		var data = FindDataDirectory( Path.GetFullPath( root ) )
			?? throw new DirectoryNotFoundException( $"{name} '{root}' is neither an extracted CD root (with a Data folder) nor a Data folder." );
		var overlay = new DataOverlayRoot( name, data, role );
		overlays.Add( overlay );
		return overlay;
	}

	/// <summary>
	/// Resolves a data-relative path to (root data directory, on-disk relative path) — base first, then
	/// overlays. The relative path keeps archive members after the archive segment.
	/// </summary>
	public (string DataDirectory, string RelativePath, DataOverlayRoot? Overlay)? Resolve( string relativePath )
	{
		if ( ResolveIn( BaseDataDirectory, relativePath ) is { } inBase )
			return (BaseDataDirectory, inBase, null);
		foreach ( var overlay in overlays )
		{
			if ( ResolveIn( overlay.DataDirectory, relativePath ) is { } inOverlay )
				return (overlay.DataDirectory, inOverlay, overlay);
		}
		return null;
	}

	/// <summary>Absolute path of a loose file or directory (base first, then overlays); null when absent.</summary>
	public string? ResolveLoose( string relativePath, bool directory = false )
	{
		if ( GameLanguage.FindEntry( BaseDataDirectory, relativePath, directory ) is { } found )
			return found;
		foreach ( var overlay in overlays )
		{
			if ( GameLanguage.FindEntry( overlay.DataDirectory, relativePath, directory ) is { } inOverlay )
				return inOverlay;
		}
		return null;
	}

	/// <summary>
	/// Case-corrects <paramref name="relativePath"/> below <paramref name="dataDirectory"/>, stopping at
	/// an archive (<c>name.wad</c>/<c>name.sdt</c> standing for segment <c>name</c> or <c>name.sdt</c>).
	/// Returns the corrected relative path ('/' separators) or null when nothing matches.
	/// </summary>
	public static string? ResolveIn( string dataDirectory, string relativePath )
	{
		var segments = relativePath.Replace( '\\', '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries );
		if ( segments.Length == 0 || segments.Any( segment => segment == ".." ) )
			return null;
		var current = dataDirectory;
		var corrected = new List<string>();
		for ( var i = 0; i < segments.Length; i++ )
		{
			if ( !Directory.Exists( current ) )
				return null;
			var segment = segments[i];
			var last = i == segments.Length - 1;
			var directory = Directory.EnumerateDirectories( current ).FirstOrDefault( entry => string.Equals( Path.GetFileName( entry ), segment, StringComparison.OrdinalIgnoreCase ) );
			if ( directory != null && !last )
			{
				corrected.Add( Path.GetFileName( directory ) );
				current = directory;
				continue;
			}
			var file = Directory.EnumerateFiles( current ).FirstOrDefault( entry => string.Equals( Path.GetFileName( entry ), segment, StringComparison.OrdinalIgnoreCase ) );
			if ( file != null && (last || ArchiveExtensions.Contains( Path.GetExtension( file ), StringComparer.OrdinalIgnoreCase )) )
			{
				// A loose file, or an archive addressed with its extension followed by member segments.
				corrected.Add( Path.GetFileName( file ) );
				corrected.AddRange( segments.Skip( i + 1 ) );
				return string.Join( "/", corrected );
			}
			if ( last && directory != null )
			{
				corrected.Add( Path.GetFileName( directory ) );
				return string.Join( "/", corrected );
			}
			// An archive addressed without its extension (the file system's convention: levels/jungle/rides/totem/...).
			var archive = Directory.EnumerateFiles( current ).FirstOrDefault( entry =>
				ArchiveExtensions.Contains( Path.GetExtension( entry ), StringComparer.OrdinalIgnoreCase ) && string.Equals( Path.GetFileNameWithoutExtension( entry ), segment, StringComparison.OrdinalIgnoreCase ) );
			if ( archive == null )
				return null;
			corrected.Add( Path.GetFileNameWithoutExtension( archive ) );
			corrected.AddRange( segments.Skip( i + 1 ) );
			return string.Join( "/", corrected );
		}
		return null;
	}

	public override string ToString() => overlays.Count == 0 ? BaseDataDirectory : $"{BaseDataDirectory} + {string.Join( ", ", overlays.Select( overlay => $"{overlay.Name} {overlay.DataDirectory}" ) )}";
}

/// <summary>Optional media a minimal install may omit, and where each one was found.</summary>
public static class OptionalMedia
{
	/// <summary>[DATA:TPWORLD.ISO and retail install:Data/Movies] the nine shipped movies.</summary>
	public static readonly IReadOnlyList<string> Movies = new[] { "bf", "buc", "bub", "grav", "jug", "mir", "plan", "roc", "roll" }.Select( name => $"Movies/{name}.tgq" ).ToArray();

	/// <summary>[DATA:TPWORLD.ISO and retail install] global music and the four theme music banks.</summary>
	public static readonly IReadOnlyList<string> Music = new[] { "global/sound/MusicHD.sdt" }
		.Concat( new[] { "jungle", "hallow", "space", "fantasy" }.Select( level => $"levels/{level}/Music/MusicHD.sdt" ) ).ToArray();

	public sealed record MediaStatus( string RelativePath, string? FoundIn );

	/// <summary>Where each optional media file resolves (null = missing everywhere).</summary>
	public static IReadOnlyList<MediaStatus> Check( DataRoots roots ) =>
		Movies.Concat( Music ).Select( path =>
		{
			var resolved = roots.Resolve( path );
			return new MediaStatus( path, resolved == null ? null : resolved.Value.Overlay?.Name ?? "install" );
		} ).ToArray();

	/// <summary>Human-readable diagnostics: one line per media file not in the install.</summary>
	public static IReadOnlyList<string> Describe( IReadOnlyList<MediaStatus> statuses, bool overlayMounted = false ) =>
		statuses.Where( status => status.FoundIn != "install" ).Select( status => status.FoundIn == null
			? $"Optional media missing: {status.RelativePath} (not in the install{(overlayMounted ? " or the overlays" : "")}; {(status.RelativePath.StartsWith( "Movies/", StringComparison.Ordinal ) ? "the movie will be skipped" : "the music will be silent")}).{(overlayMounted ? "" : " Pass the extracted CD with --cd-data.")}"
			: $"Optional media {status.RelativePath} read from {status.FoundIn}." ).ToArray();
}
