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
	/// <summary>
	/// Languages with verified original data: the European CD (English, Danish, French, German, Swedish), the Benelux CD
	/// (Dutch), the Polish CD, the Spanish CD and the Korean EA Slim Pack CD.
	/// </summary>
	public static readonly IReadOnlyList<string> ShippedLanguages = new[] { "English", "Danish", "Dutch", "French", "German", "Korean", "Polish", "Spanish", "Swedish" };

	/// <summary>
	/// Languages verified later from the localisation pack (docs/LANGUAGES.md): US English (tp.exe's <c>American</c>, its folder in the pack's <c>USEnglish</c>),
	/// Hebrew (right to left) and Italian (also on the Benelux CD).
	/// </summary>
	public static readonly IReadOnlyList<string> AdditionalLanguages = new[] { "American", "Hebrew", "Italian" };

	/// <summary>
	/// Fan translations OpenTPW can load when the player points at them, with the language folder each one replaces: the Russian of the
	/// localisation pack ships as <c>Russian/data/language/American</c>. They are not official releases (docs/LANGUAGES.md).
	/// </summary>
	// [EXT:fan-translations] a fan translation is selectable and labelled as such; the original only knew the folder of its table entry (American)
	public static readonly IReadOnlyDictionary<string, string> FanTranslations = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase ) { ["Russian"] = "American" };

	/// <summary>Every language name OpenTPW knows (verified ones first), in their shipped capitalization.</summary>
	public static IEnumerable<string> KnownLanguages => ShippedLanguages.Concat( AdditionalLanguages ).Concat( FanTranslations.Keys );

	/// <summary>Whether <paramref name="name"/> is a fan translation rather than an official release.</summary>
	public static bool IsFanTranslationName( string name ) => FanTranslations.ContainsKey( name );

	/// <summary>
	/// tp.exe's language table, in its order: the name is the <c>Data\Language\&lt;Name&gt;</c> folder and the suffix of the <c>Init</c> pictures, the id the
	/// registry's <c>Language</c> value that selects it (docs/LANGUAGES.md, "The original's language table").
	/// </summary>
	// [BIN:TPW-EXE:0x4193d0 language names] sixteen string objects of 0x14 bytes from 0x78b3d8 (one copy per translation unit): English, French, German,
	// Spanish, Swedish, Italian, Dutch, Polish, Danish, Japanese, TraditionalChinese, SimplifiedChinese, Thai, Korean, Hebrew, American.
	// [BIN:TPW-EXE:0x5c0d00 registry language] HKLM\Software\Bullfrog Productions Ltd\Theme Park World "Language" (default 0x809) is switched to one bit per
	// table entry (0x809 bit 0, 0x40c 1, 7 2, 0xa 3, 0x1d 4, 0x10 5, 0x13 6, 0x15 7, 6 8, 0x11 9, 0x404 10, 0x804 11, 0x41e 12, 0x412 13, 0x40d 14, 0x409 15); any other
	// value stores the word 0x809, whose lowest set bit is English's; the lowest set bit indexes the table and its name is copied to 0xf7f230 (read by 0x41a820)
	public static readonly IReadOnlyList<(string Name, int LanguageId)> OriginalLanguageTable = new (string, int)[]
	{
		("English", 0x809), ("French", 0x40C), ("German", 0x07), ("Spanish", 0x0A), ("Swedish", 0x1D), ("Italian", 0x10), ("Dutch", 0x13), ("Polish", 0x15),
		("Danish", 0x06), ("Japanese", 0x11), ("TraditionalChinese", 0x404), ("SimplifiedChinese", 0x804), ("Thai", 0x41E), ("Korean", 0x412), ("Hebrew", 0x40D),
		("American", 0x409),
	};

	/// <summary>The table name tp.exe selects for the registry value <paramref name="languageId"/>: an unknown value selects English.</summary>
	public static string OriginalLanguageName( int languageId ) =>
		OriginalLanguageTable.FirstOrDefault( entry => entry.LanguageId == languageId ).Name ?? DefaultLanguage;

	/// <summary>The registry value that selects the table entry <paramref name="name"/> (case-insensitive), or null for a folder the table lacks.</summary>
	public static int? OriginalLanguageId( string name ) =>
		OriginalLanguageTable.Where( entry => string.Equals( entry.Name, name, StringComparison.OrdinalIgnoreCase ) ).Select( entry => (int?)entry.LanguageId ).FirstOrDefault();

	/// <summary>The id of the one right-to-left language, Hebrew.</summary>
	public const int HebrewLanguageId = 0x40D;

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
		set
		{
			current = value;
			Version++;
		}
	}

	/// <summary>
	/// Counts language changes. Caches of language data remember the version they loaded and reload when it differs, so a
	/// language chosen in the options applies without a restart (docs/LANGUAGES.md, "Switching languages").
	/// </summary>
	public static int Version { get; private set; }

	/// <summary>
	/// Resolves <paramref name="name"/> from the same data folder and the full language overlay (all CD languages), without
	/// making it current: the caller loads its strings and fonts first and sets <see cref="Current"/> only when that worked.
	/// </summary>
	public static GameLanguage ResolveSwitch( string name ) =>
		Resolve( Current.BaseDataDirectory, name, AvailableOverlay ?? Current.OverlayDataDirectory );

	/// <summary>Whether a language has been selected or resolved.</summary>
	public static bool IsSelected => current != null;

	/// <summary>
	/// The language data the player has available besides the installation (--language-data or the CD
	/// chosen in Game files). Kept even when the selected language comes from the installation, so the
	/// options screen can still offer the other languages.
	/// </summary>
	public static string? AvailableOverlay { get; set; }

	/// <summary>Languages the player can choose: the installation's plus those in <see cref="AvailableOverlay"/>.</summary>
	public static IReadOnlyList<string> Choosable() => Readable( Current.BaseDataDirectory, AvailableOverlay ?? Current.OverlayDataDirectory );

	/// <summary>
	/// The languages of <see cref="FindLanguages"/> whose character table OpenTPW can read; a language whose table
	/// is damaged or in an unknown layout is left out rather than failing when chosen (the single-byte and the two-byte
	/// layouts of the shipped languages are read, see <see cref="BFMUReader"/>).
	/// </summary>
	public static IReadOnlyList<string> Readable( string dataDirectory, string? overlayPath ) =>
		FindLanguages( dataDirectory, overlayPath ).Where( name =>
		{
			try
			{
				_ = Resolve( dataDirectory, name, overlayPath ).CharacterTable;
				return true;
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
			{
				return false;
			}
		} ).ToList();

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
		var folder = Path.GetFileName( Path.TrimEndingDirectorySeparator( directory ) );
		OriginalName = OriginalLanguageTable.Select( entry => entry.Name ).FirstOrDefault( entry => string.Equals( entry, folder, StringComparison.OrdinalIgnoreCase ) ) ?? folder;
		LanguageId = OriginalLanguageId( OriginalName );
	}

	/// <summary>
	/// The name tp.exe gives this language: its <c>Language</c> folder as the original's table spells it (<c>American</c> for US English and for the fan
	/// Russian that replaces it). The <c>Init</c> pictures carry it (<c>Splash_American.tga</c>).
	/// </summary>
	public string OriginalName { get; }

	/// <summary>The registry value that selects this language in tp.exe (0x40D Hebrew, 0x409 American, 0x809 English); null for a folder its table lacks.</summary>
	public int? LanguageId { get; }

	/// <summary>
	/// Whether tp.exe treats the language as right to left: Hebrew. Its interface draws text through two helpers that swap left and right
	/// alignment then (docs/LANGUAGES.md, "Hebrew").
	/// </summary>
	// [BIN:TPW-EXE:0x41a850 is Hebrew] the language word compared with 0x40d; called by the text helpers 0x492750 and 0x4929f0, the alignment helper 0x4925a0,
	// the window procedure's WM_CHAR mapping (0x46b8c5) and the interface library's set-up (0x489ca0 -> 0x6584df, which keeps the flag at 0xfaa56c)
	public bool IsRightToLeft => LanguageId == HebrewLanguageId;

	/// <summary>Whether this language is a fan translation (<see cref="FanTranslations"/>) rather than an official release.</summary>
	public bool IsFanTranslation => IsFanTranslationName( Name );

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

	/// <summary>
	/// The language's banner meshes (<c>bankrupt</c>, <c>congrats</c> and <c>paused</c> in the shipped languages): every <c>.md2</c> file of the language folder, then
	/// those of the CD's <c>&lt;Lang&gt;/Meshes/&lt;Lang&gt;</c> folder an overlay adds, a file name found earlier winning.
	/// </summary>
	// [BIN:TPW-EXE:0x004893C0 language meshes] the UI start-up (0x489CA0) registers every .md2 of the language folder (0x41A820) with the ui.wad models, its textures read from data\ui\textures
	public IReadOnlyList<string> EnumerateMeshes()
	{
		var found = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var directory in searchDirectories )
		{
			if ( !System.IO.Directory.Exists( directory ) )
				continue;
			foreach ( var path in System.IO.Directory.EnumerateFiles( directory ).OrderBy( path => path, StringComparer.OrdinalIgnoreCase ) )
				if ( Path.GetExtension( path ).Equals( ".md2", StringComparison.OrdinalIgnoreCase ) )
					found.TryAdd( Path.GetFileName( path ), path );
		}
		return found.Values.ToList();
	}

	/// <summary>
	/// A string table a mod supplies for a language (docs/MODS.md): called with the language name and the table's file name
	/// (<c>UITEXT.str</c>), it returns the replacement stream (disposed by the caller) or null for the language's own file. Null by default.
	/// </summary>
	// [EXT:mods] OpenTPW's own mod system; the original game had no mods
	public static Func<string, string, Stream?>? StringOverride { get; set; }

	/// <summary>UITEXT entries in the edition OpenTPW numbers its strings by (the <c>UIStrings</c> enum).</summary>
	public const int UiTextEntries = 473;

	/// <summary>
	/// The entry that the 474-entry UITEXT edition (the European CD's languages, including its English, and the
	/// Mac version) has in addition: a "cannot load this park, it uses rides not yet downloaded" message at 207,
	/// which moves every later entry up by one. All other tables agree (CHAT_COMMANDS only appends entries).
	/// </summary>
	public const int UiTextEditionExtraEntry = 207;

	/// <summary>
	/// Loads a string table. UITEXT from the 474-entry edition is renumbered to the 473-entry one (docs/LANGUAGES.md),
	/// so the <c>UIStrings</c> enum indexes every language alike; <paramref name="asStored"/> keeps the file's own numbering.
	/// </summary>
	public StringFile LoadStrings( string fileName, bool asStored = false )
	{
		var strings = LoadModStrings( fileName ) ?? LoadOwnStrings( fileName );
		if ( !asStored && string.Equals( fileName, "UITEXT.str", StringComparison.OrdinalIgnoreCase ) && strings.Entries.Length == UiTextEntries + 1 )
			strings.RemoveEntryAt( UiTextEditionExtraEntry );
		return strings;
	}

	private StringFile LoadOwnStrings( string fileName )
	{
		using var stream = OpenRead( fileName );
		return new StringFile( stream, CharacterTable );
	}

	// [EXT:mods] A mod's table is used only when it parses and has as many entries as the language's own: the interface indexes the
	// tables by position, so a short or broken table would crash it, and a mod is untrusted data. Otherwise the game's table stays.
	private StringFile? LoadModStrings( string fileName )
	{
		if ( StringOverride == null )
			return null;
		try
		{
			using var stream = StringOverride( Name, fileName );
			if ( stream == null )
				return null;
			var replacement = new StringFile( stream, CharacterTable );
			StringFile? own = null;
			try { own = LoadOwnStrings( fileName ); }
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException ) { } // no table of the game's to compare with
			if ( own == null || replacement.Entries.Length >= own.Entries.Length )
				return replacement;
			Log?.Warning( $"Mod string table {Name}/{fileName} is ignored: it has {replacement.Entries.Length} entries, the game's has {own.Entries.Length}." );
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or IOException or ArgumentException or OverflowException or IndexOutOfRangeException )
		{
			Log?.Warning( $"Mod string table {Name}/{fileName} is ignored: {exception.Message}" );
		}
		return null;
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

	/// <summary>The pictures of tp.exe's loading screen (<see cref="InitImagePath"/>).</summary>
	public enum InitImage { Splash, Legal, Welcome, Background }

	/// <summary>The screen widths that have their own <c>Init</c> folder; any other width uses 640's.</summary>
	public static readonly IReadOnlyList<int> InitWidths = new[] { 400, 512, 640, 800, 1024 };

	/// <summary>
	/// The path below <c>data</c> of a loading-screen picture as tp.exe builds it: <c>init/&lt;width&gt;/splash_&lt;Name&gt;.tga</c> (shown first at start-up),
	/// <c>legal_&lt;Name&gt;.tga</c> (the notice on the start-up screen), <c>welcome_&lt;Name&gt;.tga</c> (on every later loading screen) and the
	/// language-independent background <c>welcome.tga</c>.
	/// </summary>
	// [BIN:TPW-EXE:0x587380 loading screen] the start-up routine 0x54dcf0 and the main state machine 0x54e360 call it with the language name of 0x41a820,
	// "data\init" and 500; it appends "\" and "%d\" of the screen width when that is 400, 512, 640, 800 or 1024 (else "640\"), then builds
	// "splash_" + name + ".tga", "legal_" + name + ".tga", "welcome_" + name + ".tga" and "welcome.tga" (0x768dcc, 0x768dc4, 0x768db8, 0x768dac); a null name is "english"
	public static string InitImagePath( InitImage image, int screenWidth, string originalName )
	{
		var folder = $"init/{(InitWidths.Contains( screenWidth ) ? screenWidth : 640)}/";
		return image switch
		{
			InitImage.Splash => $"{folder}splash_{originalName}.tga",
			InitImage.Legal => $"{folder}legal_{originalName}.tga",
			InitImage.Welcome => $"{folder}welcome_{originalName}.tga",
			_ => $"{folder}welcome.tga",
		};
	}

	/// <summary>
	/// The loading-screen picture of this language for a screen <paramref name="screenWidth"/> wide, from the overlay's <c>data</c> first, then the game's;
	/// null when neither has it (tp.exe then shows no splash: the Hebrew pack has only <c>welcome_Hebrew.tga</c>, the Korean CD no <c>Init</c> folder).
	/// </summary>
	public string? ResolveInitImage( InitImage image, int screenWidth ) => ResolveDataFile( InitImagePath( image, screenWidth, OriginalName ) );

	/// <summary>A file of the postcard folder (<c>data/Postcard</c>, e.g. <c>tpwlogo.tga</c>), overlay first; null when missing.</summary>
	// [BIN:TPW-EXE:0x747d18 postcard folder] "data\Postcard" and "data\Postcard.wad" sit in the table of data folders with "data\ui.wad" (0x747c84): fixed paths below
	// the one data folder, so a language's own copy is the one the player's data folder holds
	public string? ResolvePostcardFile( string fileName ) => ResolveDataFile( "postcard/" + fileName );

	/// <summary>
	/// The language's own interface archive (<c>ui.wad</c>) when its overlay has one, else null (the game's own <c>data/ui.wad</c> is used). The US English
	/// archive differs from the others only in the logo texture (<c>tpw_logo.wct</c> in <c>textures</c> and <c>stexture</c>), Sim Theme Park's.
	/// </summary>
	// [BIN:TPW-EXE:0x747c84 ui archive] tp.exe mounts the fixed "data\ui.wad" (with "data\ui", "data\language.wad", "data\Postcard.wad" in the same table) and
	// has no per-language lookup; [EXT:language-settings] a language whose overlay data folder has its own ui.wad uses it, modelling an install the pack's
	// data folder was copied over (or a single-language CD's own Data folder, such as the Korean CD's)
	public string? OwnInterfaceArchive => OverlayDataDirectory != null ? FindEntry( OverlayDataDirectory, "ui.wad", false ) : null;

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
			else if ( CopiedCdLanguages( dataDirectory, baseLanguages ) is { Length: 1 } copied )
				name = copied[0];
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
			return new GameLanguage( name, baseDirectory, dataDirectory, null, CopiedCdMeshes( dataDirectory, name ) );

		var available = FindLanguages( dataDirectory, overlayPath );
		throw new DirectoryNotFoundException( $"Language '{name}' not found. Available: {(available.Count == 0 ? "none" : string.Join( ", ", available ))}. " +
			"Other shipped languages are on the original CD; pass its extracted language data with --language-data (see docs/LANGUAGES.md)." );
	}

	/// <summary>
	/// The English language of the same installation/overlay, used as the fallback for missing
	/// strings; null when no English folder exists. Returns <paramref name="language"/> itself when it is English.
	/// </summary>
	public static GameLanguage? TryResolveEnglish( GameLanguage language )
	{
		if ( string.Equals( language.Name, DefaultLanguage, StringComparison.OrdinalIgnoreCase ) )
			return language;
		try
		{
			return Resolve( language.BaseDataDirectory, DefaultLanguage, null );
		}
		catch ( DirectoryNotFoundException )
		{
			return null;
		}
	}

	/// <summary>Maps a name to the shipped capitalization when it is a shipped language.</summary>
	public static string CanonicalName( string name )
	{
		// The localisation pack's folder and its usual spellings name tp.exe's American entry.
		if ( name.Replace( " ", "" ) is var compact && (string.Equals( compact, "USEnglish", StringComparison.OrdinalIgnoreCase ) || string.Equals( compact, "English(US)", StringComparison.OrdinalIgnoreCase )) )
			return "American";
		return KnownLanguages.FirstOrDefault( language => string.Equals( language, name, StringComparison.OrdinalIgnoreCase ) ) ?? name;
	}

	/// <summary>
	/// The language a folder <paramref name="folderName"/> holding the language folder <paramref name="languageFolder"/> stands for: a fan translation when the
	/// outer folder names one that replaces this language folder (the pack's <c>Russian</c> folder holds <c>language/American</c>), else the language folder's own.
	/// </summary>
	private static string PackLanguageName( string? folderName, string languageFolder )
	{
		if ( folderName != null && FanTranslations.TryGetValue( folderName, out var replaced ) && string.Equals( replaced, languageFolder, StringComparison.OrdinalIgnoreCase ) )
			return CanonicalName( folderName );
		return CanonicalName( languageFolder );
	}

	private static IEnumerable<string> BaseLanguages( string dataDirectory )
	{
		var languageRoot = FindEntry( dataDirectory, "Language", true );
		return languageRoot == null ? Enumerable.Empty<string>() : System.IO.Directory.EnumerateDirectories( languageRoot )
			.Select( directory => CanonicalName( Path.GetFileName( directory ) ) )
			.OrderBy( name => name, StringComparer.OrdinalIgnoreCase );
	}

	/// <summary>
	/// A game folder copied from the CD (rather than installed) keeps the banner meshes in
	/// <c>&lt;Lang&gt;/Meshes/&lt;Lang&gt;</c> beside <c>data</c>; the installer would have copied them into the
	/// language folder.
	/// </summary>
	private static string? CopiedCdMeshes( string dataDirectory, string name )
	{
		var root = Path.GetDirectoryName( Path.TrimEndingDirectorySeparator( dataDirectory ) );
		return root == null ? null : FindEntry( root, Path.Combine( name, "Meshes", name ), true );
	}

	/// <summary>
	/// The language of a game folder copied from a single-language CD whose full <c>Data</c> folder still carries the English folder next
	/// to the CD's own (the Spanish and Korean CDs): the non-English language with a folder of its own beside <c>Data</c> (<c>Spanish</c>,
	/// <c>Korean</c>, which holds the installer and the banner meshes). That language is the CD's speech and text, so it is the default
	/// when no language is chosen.
	/// </summary>
	private static string[] CopiedCdLanguages( string dataDirectory, string[] baseLanguages )
	{
		var root = Path.GetDirectoryName( Path.TrimEndingDirectorySeparator( dataDirectory ) );
		if ( root == null )
			return Array.Empty<string>();
		return baseLanguages.Where( name => !string.Equals( name, DefaultLanguage, StringComparison.OrdinalIgnoreCase ) && FindEntry( root, name, true ) != null ).ToArray();
	}

	private readonly record struct OverlayLanguage( string Name, string Directory, string DataDirectory, string? MeshDirectory );

	private static IEnumerable<OverlayLanguage> OverlayLanguages( string? overlayPath )
	{
		if ( overlayPath == null || !System.IO.Directory.Exists( overlayPath ) )
			yield break;

		// A single CD "<Lang>/data" folder.
		if ( FindEntry( overlayPath, "Language", true ) is { } languageRoot )
		{
			var parent = Path.GetDirectoryName( Path.TrimEndingDirectorySeparator( overlayPath ) );
			var directories = System.IO.Directory.EnumerateDirectories( languageRoot ).OrderBy( path => path, StringComparer.OrdinalIgnoreCase ).ToArray();
			// The full Data folder of the Spanish and Korean CDs: the CD's own language has a folder beside Data, and the English
			// folder that stays in Data is not the CD's English (its speech is the CD's), so only languages with such a folder count.
			var ownFolders = parent == null ? Array.Empty<string>() : directories.Where( directory => FindEntry( parent, CanonicalName( Path.GetFileName( directory ) ), true ) != null ).ToArray();
			var parentName = parent == null ? null : Path.GetFileName( parent );
			foreach ( var directory in ownFolders.Length > 0 ? ownFolders : directories )
			{
				var name = PackLanguageName( parentName, Path.GetFileName( directory ) );
				var meshes = parent == null ? null : FindEntry( parent, Path.Combine( "Meshes", name ), true ) ?? FindEntry( parent, Path.Combine( name, "Meshes", name ), true );
				yield return new OverlayLanguage( name, directory, overlayPath, meshes );
			}
			yield break;
		}

		// One folder of the localisation pack: "<Lang>/data/Language/<Folder>" (Hebrew/data/Language/Hebrew, USEnglish/data/language/american).
		if ( FindEntry( overlayPath, "data", true ) is { } packData && FindEntry( packData, "Language", true ) is { } packLanguages
			&& System.IO.Directory.EnumerateDirectories( overlayPath ).All( entry => string.Equals( Path.GetFileName( entry ), Path.GetFileName( packData ), StringComparison.Ordinal ) ) )
		{
			foreach ( var language in OverlayLanguages( packData ) )
				yield return language;
			yield break;
		}

		// A folder holding "<Lang>/data/Language/<Lang>" trees, as on the CD.
		foreach ( var languageFolder in System.IO.Directory.EnumerateDirectories( overlayPath ).OrderBy( path => path, StringComparer.OrdinalIgnoreCase ) )
		{
			var name = CanonicalName( Path.GetFileName( languageFolder ) );
			var dataDirectory = FindEntry( languageFolder, "data", true );
			var directory = dataDirectory == null ? null : FindEntry( dataDirectory, Path.Combine( "Language", name ), true );
			// The localisation pack names some folders otherwise than their language folder: USEnglish holds language/american, the fan Russian language/American.
			if ( directory == null && dataDirectory != null && FindEntry( dataDirectory, "Language", true ) is { } languages
				&& System.IO.Directory.GetDirectories( languages ) is [var only] )
			{
				name = PackLanguageName( Path.GetFileName( languageFolder ), Path.GetFileName( only ) );
				directory = only;
			}
			if ( directory == null )
				continue;
			yield return new OverlayLanguage( name, directory, dataDirectory!, FindEntry( languageFolder, Path.Combine( "Meshes", name ), true ) );
		}

		// The root of a single-language CD with a complete Data folder: "Data/Language/<Lang>" beside a "<Lang>" folder.
		if ( FindEntry( overlayPath, "Data", true ) is { } cdData && FindEntry( cdData, "Language", true ) is { } cdLanguages )
		{
			foreach ( var languageFolder in System.IO.Directory.EnumerateDirectories( overlayPath ).OrderBy( path => path, StringComparer.OrdinalIgnoreCase ) )
			{
				var name = CanonicalName( Path.GetFileName( languageFolder ) );
				// English is the installed game's own (the CD's Data folder only keeps it next to the CD's language); a language that has
				// its own "<Lang>/data" tree is listed above
				if ( string.Equals( name, "Data", StringComparison.OrdinalIgnoreCase ) || string.Equals( name, DefaultLanguage, StringComparison.OrdinalIgnoreCase )
					|| FindEntry( languageFolder, "data", true ) != null || FindEntry( cdLanguages, name, true ) is not { } directory )
					continue;
				yield return new OverlayLanguage( name, directory, cdData, FindEntry( languageFolder, Path.Combine( "Meshes", name ), true ) );
			}
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
