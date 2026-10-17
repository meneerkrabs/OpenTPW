namespace OpenTPW.Mods;

/// <summary>
/// The mod inputs of the browser build (docs/WEB.md, "Mods in the browser"). A desktop mod input is a folder the player
/// chooses; the browser has no folders, so the page copies the player's own game files into the runtime's memory, one tree per
/// original edition below <see cref="RootDirectory"/> (<c>/inputs/theme-park-inc/Data/...</c>), beside the Theme Park World
/// files in <c>/game</c>. An <c>original-data</c> input of a mod that names an edition is satisfied by that edition's tree, so
/// one imported Theme Park Inc folder serves every mod that asks for it. The page decides which files are copied
/// (<c>opentpw-inputs.js</c>: the object archives, the shared textures and the sound banks of the three worlds, about 77 MB of the install's 400 MB)
/// and keeps them in the browser's storage between visits; this class validates what arrives and wires the tree into the mod settings.
/// Nothing is written to a mod's own folder and nothing of the original games is shipped.
/// </summary>
// [EXT:mods] OpenTPW's own mod system; the original game had no mods
public static class BrowserModInputs
{
	/// <summary>The edition token of Theme Park Inc inputs (<c>"edition": "theme-park-inc"</c> in a manifest).</summary>
	public const string ThemeParkInc = "theme-park-inc";

	public const int MaximumFiles = 20000;
	public const long MaximumBytes = 256L * 1024 * 1024;
	public const int MaximumPathLength = 260;

	private static readonly object gate = new();
	private static readonly Dictionary<string, (int Files, long Bytes)> totals = new( StringComparer.Ordinal );
	private static int revision;
	private static string? message;
	private static bool locked;

	/// <summary>Where the trees live (the browser's <c>/inputs</c>); tests point it at a temporary folder.</summary>
	public static string RootDirectory { get; set; } = "/inputs";

	/// <summary>Asks the page to show the folder dialog for an edition's tree (set by the browser's startup; null elsewhere).</summary>
	public static Action<string>? Choose { get; set; }

	/// <summary>Counts every change the page reports (an import finished or failed); the Extensions and mods screen reads it to refresh.</summary>
	public static int Revision => Volatile.Read( ref revision );

	/// <summary>The page's last message about an import (why it failed, or empty).</summary>
	public static string? Message { get { lock ( gate ) return message; } }

	/// <summary>The editions the browser can hold a tree for.</summary>
	public static bool IsSupported( string? edition ) => edition == ThemeParkInc;

	/// <summary>The marker a finished import leaves in the live tree: a tree without it is never read.</summary>
	public const string CompleteMarker = ".opentpw-complete";

	/// <summary>The live tree of an edition (what mods read), or null for an edition the browser cannot hold. It exists only after <see cref="Publish"/>.</summary>
	public static string? FolderFor( string? edition ) => IsSupported( edition ) ? Path.Combine( RootDirectory, edition! ) : null;

	/// <summary>Where an import copies to until it is complete and recognised; never read by a mod.</summary>
	private static string StagingFor( string edition ) => Path.Combine( RootDirectory, ".staging", edition );

	/// <summary>Whether an edition's live tree is a finished import (its marker is there).</summary>
	public static bool IsComplete( string edition ) => FolderFor( edition ) is { } folder && File.Exists( Path.Combine( folder, CompleteMarker ) );

	/// <summary>The tree that satisfies an input: an <c>original-data</c> input that names a supported edition.</summary>
	public static string? FolderFor( ModInput input ) => input.Kind == "original-data" ? FolderFor( input.Edition ) : null;

	/// <summary>
	/// The settings with every input that has no saved folder pointed at its edition's tree when that tree exists. The choice stays
	/// the player's: a saved folder is never replaced, and nothing is written to disk.
	/// </summary>
	public static ModSettings WithDefaults( ModSettings settings, IEnumerable<DiscoveredMod> discovered )
	{
		foreach ( var mod in discovered.Where( mod => mod.IsValid ) )
			foreach ( var input in mod.Manifest!.Inputs )
				if ( FolderFor( input ) is { } folder && settings.InputPath( mod.Id, input.Id ) == null && Directory.Exists( folder ) && File.Exists( Path.Combine( folder, CompleteMarker ) ) )
					settings = settings.WithInput( mod.Id, input.Id, folder );
		return settings;
	}

	/// <summary>
	/// Marks the trees the resolved mods read as in use: they cannot be replaced while the game runs (a catalog reads its archives lazily),
	/// the page asks for a reload instead.
	/// </summary>
	public static void LockInUse( ModSet set )
	{
		lock ( gate )
			locked = set.Mods.Any( mod => mod.InputDirectories.Values.Any( directory => IsInside( directory ) ) );
	}

	private static bool IsInside( string directory )
	{
		var root = Path.GetFullPath( RootDirectory ).TrimEnd( '/', '\\' ) + Path.DirectorySeparatorChar;
		return Path.GetFullPath( directory ).StartsWith( root, StringComparison.Ordinal );
	}

	/// <summary>Whether an edition's tree is read by an active mod, and so cannot be replaced until the page is reloaded.</summary>
	public static bool IsLocked { get { lock ( gate ) return locked; } }

	/// <summary>
	/// Starts an import: removes the live tree and any earlier staging (a replaced install must never mix with the old files, and a half-done import
	/// must never be read), then opens an empty staging tree. Refused while an active mod reads the live tree.
	/// </summary>
	public static void Begin( string edition )
	{
		var folder = FolderFor( edition ) ?? throw new ArgumentException( $"The browser cannot hold '{edition}' data for mods." );
		lock ( gate )
		{
			if ( locked )
				throw new InvalidOperationException( "The Theme Park Inc files are in use by a mod of this visit. Reload the page to choose another folder." );
			DeleteTrees( edition );
			totals[edition] = (0, 0);
			Directory.CreateDirectory( StagingFor( edition ) );
		}
	}

	private static void DeleteTrees( string edition )
	{
		foreach ( var tree in new[] { FolderFor( edition )!, StagingFor( edition ) } )
			if ( Directory.Exists( tree ) )
				Directory.Delete( tree, true );
		var staging = Path.Combine( RootDirectory, ".staging" );
		if ( Directory.Exists( staging ) && !Directory.EnumerateFileSystemEntries( staging ).Any() )
			Directory.Delete( staging );
	}

	/// <summary>Gives up an import: the staging and the live tree are removed (nothing is left half-copied). A tree an active mod reads is left alone.</summary>
	public static void Abort( string edition )
	{
		if ( !IsSupported( edition ) )
			return;
		lock ( gate )
		{
			if ( locked )
				return;
			DeleteTrees( edition );
			totals[edition] = (0, 0);
		}
	}

	/// <summary>
	/// Stores one file below an edition's staging tree, as <c>Data/...</c>. The path must stay inside the tree (no empty, '.' or '..' components, no
	/// backslashes), and the tree is bounded in files and bytes.
	/// </summary>
	public static void AddFile( string edition, string relativePath, byte[] contents )
	{
		_ = FolderFor( edition ) ?? throw new ArgumentException( $"The browser cannot hold '{edition}' data for mods." );
		var path = ValidatePath( relativePath );
		lock ( gate )
		{
			if ( locked )
				throw new InvalidOperationException( "The Theme Park Inc files are in use by a mod of this visit." );
			var staging = StagingFor( edition );
			if ( !Directory.Exists( staging ) )
				throw new InvalidOperationException( "No import is open: the page must begin one first." );
			var (files, bytes) = totals.GetValueOrDefault( edition );
			if ( files + 1 > MaximumFiles || bytes + contents.Length > MaximumBytes )
				throw new InvalidDataException( $"Too many files for a mod input (at most {MaximumFiles} files and {MaximumBytes / (1024 * 1024)} MB)." );
			var target = Path.Combine( staging, path );
			Directory.CreateDirectory( Path.GetDirectoryName( target )! );
			File.WriteAllBytes( target, contents );
			totals[edition] = (files + 1, bytes + contents.Length);
		}
	}

	/// <summary>
	/// Finishes an import: the staging must hold exactly <paramref name="expectedFiles"/> files, be recognised as Theme Park Inc data by the game's own
	/// detection (<see cref="GameEditions.Detect"/>) and hold the minimum a mod reads (per world a ride archive and the shared textures, and the global
	/// sounds). Only then is the tree marked complete and made live; otherwise it throws and the page aborts.
	/// </summary>
	public static void Publish( string edition, int expectedFiles )
	{
		var live = FolderFor( edition ) ?? throw new ArgumentException( $"The browser cannot hold '{edition}' data for mods." );
		lock ( gate )
		{
			if ( locked )
				throw new InvalidOperationException( "The Theme Park Inc files are in use by a mod of this visit." );
			var staging = StagingFor( edition );
			var (files, _) = totals.GetValueOrDefault( edition );
			if ( !Directory.Exists( staging ) || files != expectedFiles || files == 0 )
				throw new InvalidDataException( $"The copy is incomplete ({files} of {expectedFiles} files)." );
			var data = Path.Combine( staging, "Data" );
			if ( !Directory.Exists( data ) || GameEditions.Detect( data ).Edition != GameEdition.ThemeParkInc )
				throw new InvalidDataException( "The files copied are not recognised as Theme Park Inc data." );
			if ( MissingMinimum( data ) is { } missing )
				throw new InvalidDataException( $"The Theme Park Inc files are incomplete: {missing} is missing." );
			File.WriteAllText( Path.Combine( staging, CompleteMarker ), files.ToString() );
			if ( Directory.Exists( live ) )
				Directory.Delete( live, true );
			Directory.Move( staging, live );
		}
	}

	/// <summary>The first file group a Theme Park Inc rides mod needs that the tree lacks, or null.</summary>
	internal static string? MissingMinimum( string data )
	{
		var paths = Directory.EnumerateFiles( data, "*", SearchOption.AllDirectories )
			.Select( file => Path.GetRelativePath( data, file ).Replace( '\\', '/' ).ToLowerInvariant() ).ToList();
		foreach ( var world in GameEditions.ThemeParkIncWorlds )
		{
			if ( !paths.Any( path => path.StartsWith( $"levels/{world}/rides/", StringComparison.Ordinal ) ) )
				return $"the {world} ride archives";
			if ( !paths.Contains( $"levels/{world}/sharetex.wad" ) )
				return $"{world}'s shared textures";
		}
		return paths.Any( path => path.StartsWith( "global/sound/", StringComparison.Ordinal ) ) ? null : "the global sounds";
	}

	/// <summary>The path as stored: '/' components below the tree, starting at <c>Data</c>.</summary>
	public static string ValidatePath( string relativePath )
	{
		if ( string.IsNullOrEmpty( relativePath ) || relativePath.Length > MaximumPathLength || relativePath.Contains( '\\' ) || relativePath.Any( char.IsControl ) )
			throw new ArgumentException( $"'{relativePath}' is not a usable file path." );
		var parts = relativePath.Split( '/' );
		if ( parts.Length < 2 || parts.Any( part => part.Length == 0 || part is "." or ".." || part.Contains( ':' ) ) || !parts[0].Equals( "Data", StringComparison.Ordinal ) )
			throw new ArgumentException( $"'{relativePath}' is not a file below the Data folder." );
		return string.Join( '/', parts );
	}

	/// <summary>The page reports that an import (or a restore from the browser's storage) finished; an empty <paramref name="problem"/> means it worked.</summary>
	public static void Changed( string? problem )
	{
		lock ( gate )
			message = string.IsNullOrEmpty( problem ) ? null : problem;
		Interlocked.Increment( ref revision );
	}

	/// <summary>Test hook: forgets the counters and the lock.</summary>
	internal static void Reset()
	{
		lock ( gate )
		{
			totals.Clear();
			locked = false;
			message = null;
		}
	}

	/// <summary>The files an edition's tree holds now, for the page's status line.</summary>
	public static (int Files, long Bytes) Count( string edition )
	{
		lock ( gate )
			return totals.GetValueOrDefault( edition );
	}

	/// <summary>A one-line summary for the log.</summary>
	public static string Describe( string edition ) => FolderFor( edition ) is { } folder && Directory.Exists( folder )
		? $"{Count( edition ).Files} files in {folder}"
		: "none";
}
