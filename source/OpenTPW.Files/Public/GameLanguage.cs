namespace OpenTPW;

/// <summary>
/// The language whose <c>Language/&lt;Name&gt;</c> folder supplies string tables, fonts and banner
/// meshes, optionally taken from a language overlay (see docs/LANGUAGES.md).
/// </summary>
/// <remarks>
/// A retail install only carries the language chosen at install time. The other shipped languages
/// live on the CD as <c>&lt;Lang&gt;/data/...</c> trees that mirror the game's <c>data</c> folder, plus
/// <c>&lt;Lang&gt;/Meshes/&lt;Lang&gt;/</c> banner meshes. An overlay directory may be either one such
/// <c>&lt;Lang&gt;/data</c> folder or a directory containing several <c>&lt;Lang&gt;</c> folders (e.g. a
/// full extraction of the CD). The CD's folder and file name case is inconsistent
/// (<c>danish</c>, <c>speech</c>, <c>lips.WAD</c>, <c>sp_001.lip</c>), so all lookups here are
/// case-insensitive.
/// </remarks>
public sealed class GameLanguage
{
	public const string DefaultLanguage = "English";
	public static readonly IReadOnlyList<string> ShippedLanguages = new[] { "English", "Danish", "French", "German", "Swedish" };

	private static GameLanguage? current;
	private readonly string[] searchDirectories;
	private BFMUReader? characterTable;

	/// <summary>
	/// The active language. Defaults to <see cref="Resolve"/> over the current <c>FileSystem</c>
	/// root when nothing has been selected.
	/// </summary>
	public static GameLanguage Current
	{
		get => current ??= Resolve( FileSystem.GetAbsolutePath( "/" ), null, null );
		set => current = value;
	}

	/// <summary>Whether a language has been selected or resolved.</summary>
	public static bool IsSelected => current != null;

	/// <summary>Language name, using the shipped capitalization (e.g. "Danish").</summary>
	public string Name { get; }

	/// <summary>Absolute path of the <c>Language/&lt;Name&gt;</c> folder in use.</summary>
	public string Directory { get; }

	/// <summary>The game's base <c>data</c> directory.</summary>
	public string BaseDataDirectory { get; }

	/// <summary>
	/// The overlay's <c>&lt;Lang&gt;/data</c> directory when this language comes from an overlay;
	/// otherwise null.
	/// </summary>
	public string? OverlayDataDirectory { get; }

	private GameLanguage( string name, string directory, string baseDataDirectory, string? overlayDataDirectory, string? meshDirectory )
	{
		Name = name;
		Directory = directory;
		BaseDataDirectory = baseDataDirectory;
		OverlayDataDirectory = overlayDataDirectory;
		searchDirectories = meshDirectory == null ? new[] { directory } : new[] { directory, meshDirectory };
	}

	/// <summary>The language's own <c>MBToUni.dat</c> character table.</summary>
	public BFMUReader CharacterTable
	{
		get
		{
			if ( characterTable == null )
			{
				using var stream = OpenRead( StringFile.CharacterTableName );
				characterTable = new BFMUReader( stream );
			}
			return characterTable;
		}
	}

	/// <summary>
	/// Finds a file in the language folder (then, for overlays, the CD's
	/// <c>&lt;Lang&gt;/Meshes/&lt;Lang&gt;</c> banner folder), ignoring case.
	/// </summary>
	public string? FindFile( string fileName )
	{
		foreach ( var directory in searchDirectories )
		{
			var path = FindEntry( directory, fileName, false );
			if ( path != null )
				return path;
		}
		return null;
	}

	public Stream OpenRead( string fileName )
	{
		var path = FindFile( fileName ) ?? throw new FileNotFoundException( $"'{fileName}' is not in the {Name} language folder '{Directory}'.", fileName );
		return File.Open( path, FileMode.Open, FileAccess.Read, FileShare.Read );
	}

	/// <summary>Files directly inside the language folder.</summary>
	public IEnumerable<string> EnumerateFiles() => System.IO.Directory.EnumerateFiles( Directory ).OrderBy( path => path, StringComparer.OrdinalIgnoreCase );

	public StringFile LoadStrings( string fileName )
	{
		using var stream = OpenRead( fileName );
		return new StringFile( stream, CharacterTable );
	}

	public FontFile LoadFont( string fileName )
	{
		using var stream = OpenRead( fileName );
		return new FontFile( stream );
	}

	/// <summary>
	/// Resolves a loose file below <c>data</c> (e.g. <c>levels/jungle/speech/lips/sp_001.lip</c>),
	/// preferring the language overlay over the base data and ignoring case. Returns null when the
	/// file is in neither (files inside WAD/SDT archives are not searched).
	/// </summary>
	public string? ResolveDataFile( string relativePath )
	{
		if ( OverlayDataDirectory != null && FindEntry( OverlayDataDirectory, relativePath, false ) is { } overlay )
			return overlay;
		return FindEntry( BaseDataDirectory, relativePath, false );
	}

	/// <summary>
	/// Languages available in <paramref name="dataDirectory"/> and the optional overlay, using shipped
	/// capitalization, without duplicates.
	/// </summary>
	public static IReadOnlyList<string> FindLanguages( string dataDirectory, string? overlayPath ) =>
		BaseLanguages( dataDirectory ).Concat( OverlayLanguages( overlayPath ).Select( entry => entry.Name ) )
			.Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();

	/// <summary>
	/// Selects a language.
	/// </summary>
	/// <param name="dataDirectory">The game's <c>data</c> directory.</param>
	/// <param name="requested">Language name (any case); null or empty picks the default.</param>
	/// <param name="overlayPath">Optional overlay: a CD <c>&lt;Lang&gt;/data</c> folder or a folder of <c>&lt;Lang&gt;</c> folders.</param>
	/// <remarks>
	/// Without a request: an overlay holding exactly one language selects it; otherwise English
	/// if the base data has it, otherwise the alphabetically first language found.
	/// </remarks>
	public static GameLanguage Resolve( string dataDirectory, string? requested, string? overlayPath )
	{
		dataDirectory = Path.GetFullPath( dataDirectory );
		if ( !string.IsNullOrWhiteSpace( overlayPath ) )
		{
			overlayPath = Path.GetFullPath( overlayPath );
			if ( !System.IO.Directory.Exists( overlayPath ) )
				throw new DirectoryNotFoundException( $"Language data overlay '{overlayPath}' does not exist." );
		}
		else
			overlayPath = null;

		var overlay = OverlayLanguages( overlayPath ).ToArray();
		var baseLanguages = BaseLanguages( dataDirectory ).ToArray();
		var name = string.IsNullOrWhiteSpace( requested ) ? null : CanonicalName( requested.Trim() );
		if ( name == null )
		{
			if ( overlay.Length == 1 )
				name = overlay[0].Name;
			else
			{
				var candidates = baseLanguages.Length > 0 ? baseLanguages : overlay.Select( entry => entry.Name ).ToArray();
				name = candidates.FirstOrDefault( language => language == DefaultLanguage ) ?? candidates.OrderBy( language => language, StringComparer.OrdinalIgnoreCase ).FirstOrDefault();
			}
			if ( name == null )
				throw new DirectoryNotFoundException( $"No language folders found in '{Path.Combine( dataDirectory, "Language" )}'." );
		}

		var fromOverlay = overlay.FirstOrDefault( entry => string.Equals( entry.Name, name, StringComparison.OrdinalIgnoreCase ) );
		if ( fromOverlay.Directory != null )
			return new GameLanguage( name, fromOverlay.Directory, dataDirectory, fromOverlay.DataDirectory, fromOverlay.MeshDirectory );

		var baseDirectory = FindEntry( dataDirectory, Path.Combine( "Language", name ), true );
		if ( baseDirectory != null )
			return new GameLanguage( name, baseDirectory, dataDirectory, null, null );

		var available = FindLanguages( dataDirectory, overlayPath );
		throw new DirectoryNotFoundException( $"Language '{name}' not found. Available: {(available.Count == 0 ? "none" : string.Join( ", ", available ))}. " +
			"Other shipped languages are on the original CD; pass its extracted language data with --language-data (see docs/LANGUAGES.md)." );
	}

	/// <summary>Maps a name to the shipped capitalization when it is a shipped language.</summary>
	public static string CanonicalName( string name ) =>
		ShippedLanguages.FirstOrDefault( language => string.Equals( language, name, StringComparison.OrdinalIgnoreCase ) ) ?? name;

	private static IEnumerable<string> BaseLanguages( string dataDirectory )
	{
		var languageRoot = FindEntry( dataDirectory, "Language", true );
		return languageRoot == null ? Enumerable.Empty<string>() : System.IO.Directory.EnumerateDirectories( languageRoot )
			.Select( directory => CanonicalName( Path.GetFileName( directory ) ) )
			.OrderBy( name => name, StringComparer.OrdinalIgnoreCase );
	}

	private readonly record struct OverlayLanguage( string Name, string Directory, string DataDirectory, string? MeshDirectory );

	private static IEnumerable<OverlayLanguage> OverlayLanguages( string? overlayPath )
	{
		if ( overlayPath == null || !System.IO.Directory.Exists( overlayPath ) )
			yield break;

		// A single CD "<Lang>/data" folder.
		if ( FindEntry( overlayPath, "Language", true ) is { } languageRoot )
		{
			foreach ( var directory in System.IO.Directory.EnumerateDirectories( languageRoot ).OrderBy( path => path, StringComparer.OrdinalIgnoreCase ) )
			{
				var name = CanonicalName( Path.GetFileName( directory ) );
				var parent = Path.GetDirectoryName( Path.TrimEndingDirectorySeparator( overlayPath ) );
				var meshes = parent == null ? null : FindEntry( parent, Path.Combine( "Meshes", name ), true );
				yield return new OverlayLanguage( name, directory, overlayPath, meshes );
			}
			yield break;
		}

		// A folder holding "<Lang>/data/Language/<Lang>" trees, as on the CD.
		foreach ( var languageFolder in System.IO.Directory.EnumerateDirectories( overlayPath ).OrderBy( path => path, StringComparer.OrdinalIgnoreCase ) )
		{
			var name = CanonicalName( Path.GetFileName( languageFolder ) );
			var dataDirectory = FindEntry( languageFolder, "data", true );
			var directory = dataDirectory == null ? null : FindEntry( dataDirectory, Path.Combine( "Language", name ), true );
			if ( directory == null )
				continue;
			yield return new OverlayLanguage( name, directory, dataDirectory!, FindEntry( languageFolder, Path.Combine( "Meshes", name ), true ) );
		}
	}

	/// <summary>
	/// Resolves <paramref name="relativePath"/> below <paramref name="root"/>, matching each segment
	/// exactly first and then ignoring case, and returns the on-disk spelling. Returns null when missing; rejects paths that leave
	/// the root.
	/// </summary>
	public static string? FindEntry( string root, string relativePath, bool directory )
	{
		var current = Path.GetFullPath( root );
		if ( !System.IO.Directory.Exists( current ) )
			return null;
		var segments = relativePath.Split( new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries );
		for ( var i = 0; i < segments.Length; i++ )
		{
			var segment = segments[i];
			if ( segment == "." )
				continue;
			if ( segment == ".." )
				throw new ArgumentException( "Path must remain inside the data directory.", nameof( relativePath ) );
			var last = i == segments.Length - 1;
			var wantDirectory = !last || directory;
			// Enumerate rather than probe so the on-disk spelling is returned on case-insensitive
			// file systems too.
			var entries = wantDirectory ? System.IO.Directory.EnumerateDirectories( current ) : System.IO.Directory.EnumerateFiles( current );
			var matches = entries.Where( entry => string.Equals( Path.GetFileName( entry ), segment, StringComparison.OrdinalIgnoreCase ) ).ToArray();
			var match = matches.FirstOrDefault( entry => Path.GetFileName( entry ) == segment ) ?? matches.OrderBy( entry => entry, StringComparer.Ordinal ).FirstOrDefault();
			if ( match == null )
				return null;
			current = match;
		}
		return current;
	}

	public override string ToString() => OverlayDataDirectory == null ? $"{Name} ({Directory})" : $"{Name} ({Directory}, language overlay)";
}
