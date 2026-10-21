using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using OpenTPW.FrontEnd;
using OpenTPW.Online.Packages;

namespace OpenTPW;

/// <summary>
/// <c>--export-signs &lt;language&gt; &lt;output directory&gt;</c>: a debug export of every sign texture the shipped data defines, in one language
/// (docs/RUNNING.md, "Sign textures"). For each of the four themes it writes the park gate's two halves (the gate's OBJECT_NAMES row, as a new
/// park's gate shows it) and the two halves of every object archive with a <c>.sgn</c> (the object's name split at its middle space), plus the
/// lobby's island and gate signs, as <c>&lt;theme&gt;/&lt;object&gt;-1.png</c> (the <c>sign1</c> texture, the left half) and <c>-2.png</c>
/// (<c>sign2</c>, the right half), a preview of the whole sign (<c>&lt;object&gt;.png</c>, the two halves side by side), with <c>index.json</c> listing the source, string ids, text and size of every file. The pixels come from the
/// composers the game itself uses (<see cref="ObjectSigns.Get"/>, <see cref="OriginalGateSign.ComposeHalves"/>, <see cref="LobbySigns.Compose"/>);
/// nothing is drawn on the GPU, no window, audio or park is created, and only the output directory is written.
/// </summary>
public static class SignExport
{
	/// <summary>The folder of the lobby's island and gate signs (the lobby is no theme of its own; each sign shows its island's theme gate lines).</summary>
	public const string LobbyFolder = "lobby";
	public const string IndexFileName = "index.json";

	/// <summary>One composed sign: its source, the text it was drawn with and the RGBA of its two halves.</summary>
	public sealed record ComposedSign( string Theme, string Kind, string Name, string ArchivePath, string SignPath, int? InfoId,
		IReadOnlyList<int> StringIds, IReadOnlyList<string> StringTexts, string Text, string Line1, string Line2, byte[] Left, byte[] Right )
	{
		/// <summary>Where the game takes the two lines from (OBJECT_NAMES ids, or a bonus archive's language file), as <c>index.json</c> records it.</summary>
		public string TextSource { get; init; } = "";
		/// <summary>Whether the sign belongs to an official bonus archive (<c>--bonus-data</c>).</summary>
		public bool IsBonus { get; init; }
		/// <summary>The file system <see cref="SignPath"/> is read from (the game data, or the bonus content for a bonus object).</summary>
		internal BaseFileSystem? SignFileSystem { get; init; }
		/// <summary>The sign as the game would draw it, when <c>--sign-texts</c> replaced its text (null otherwise).</summary>
		public ComposedSign? Original { get; init; }
	}

	/// <summary>One written PNG, as <c>index.json</c> lists it. <see cref="Half"/> is 1 (<c>sign1</c>), 2 (<c>sign2</c>) or 0 (the preview of both).</summary>
	public sealed record ExportedFile( ComposedSign Sign, int Half, string RelativePath, int Width, int Height, string RgbaSha256, string PngSha256 )
	{
		/// <summary>The encoded PNG (not part of the index).</summary>
		internal byte[] Png { get; init; } = Array.Empty<byte>();
	}

	/// <summary>
	/// Parses <c>--export-signs &lt;language&gt; &lt;output directory&gt;</c> and runs the export once the game data is mounted at
	/// <paramref name="dataDirectory"/> (<c>Game.Run</c>). The language data is found as for a normal start (<c>--language-data</c>,
	/// <c>OPENTPW_LANGUAGE_DATA</c>, the saved language data folder, then the CD folder); a language that is not there is an error.
	/// </summary>
	internal static void RunCommand( string[] args, string dataDirectory, string? languageData, string? cdFolder )
	{
		var (language, outputDirectory) = ParseArguments( args );
		// official bonus objects only when asked for: --bonus-data <folder>, or --with-bonus for the folder a normal start uses
		var bonus = BonusRoot( args );
		LanguageStartup.Apply( dataDirectory, language, languageData, cdFolder, false, message => Log?.Warning( message ) );
		if ( !GameLanguage.IsSelected || !string.Equals( GameLanguage.Current.Name, GameLanguage.CanonicalName( language ), StringComparison.OrdinalIgnoreCase ) )
			throw new DirectoryNotFoundException( $"Language '{language}' was not selected; see docs/LANGUAGES.md for where the language data is found." );
		Log?.Trace( $"Language: {GameLanguage.Current}" );
		// nothing is written into the game folder, its data, any language data (the one exported and the overlay offered beside it) or the CD folder;
		// checked before anything else starts
		var protectedDirectories = new[] { dataDirectory, Settings.Default.GamePath, languageData, cdFolder, bonus, bonus == null ? null : ObjectCatalog.FindBonusLevelsParent( bonus ) }.OfType<string>().ToArray();
		PrepareOutputDirectory( outputDirectory, protectedDirectories.Concat( LanguageDirectories() ) );
		CompatibilityStartup.Initialize( args, dataDirectory );
		// Only the shipped data, and the official bonus objects when asked for; never mod content (mods are not started for the export).
		ObjectCatalog.BonusDataRoot = bonus;
		if ( bonus != null )
			Log?.Trace( $"Bonus content: {bonus}." );
		// --sign-texts <file>: replacement texts per sign (read only; parsed before anything is composed)
		var overrides = args.Contains( "--sign-texts" ) ? SignTextOverrides.Load( SignTextsPath( args ) ) : null;
		var files = Run( outputDirectory, overrides, protectedDirectories );
		var previews = files.Count( file => file.Half == 0 );
		Console.WriteLine( $"Exported {files.Count - previews} sign textures and {previews} previews ({previews} signs) in {GameLanguage.Current.Name} to {Path.GetFullPath( outputDirectory )}; index in {IndexFileName}." );
	}

	/// <summary>
	/// The bonus content folder of the export, or null for the shipped data alone (the default): <c>--bonus-data &lt;folder&gt;</c>, or with
	/// <c>--with-bonus</c> the folder a normal start would use (<c>OPENTPW_BONUS_DATA</c>, the saved bonus folder, <c>&lt;config&gt;/bonus</c>).
	/// A folder without official bonus archives (no <c>levels</c> folder within two levels) is an error.
	/// </summary>
	internal static string? BonusRoot( string[] args, Func<string?>? implicitRoot = null )
	{
		var index = Array.IndexOf( args, "--bonus-data" );
		string? root = null;
		if ( index >= 0 )
		{
			if ( index + 1 >= args.Length || args[index + 1].StartsWith( "--", StringComparison.Ordinal ) || string.IsNullOrWhiteSpace( args[index + 1] ) )
				throw new ArgumentException( "--bonus-data requires the extracted official bonus content directory." );
			root = Path.GetFullPath( args[index + 1] );
		}
		else if ( args.Contains( "--with-bonus" ) )
		{
			root = (implicitRoot ?? DefaultBonusRoot)() ?? throw new DirectoryNotFoundException( "--with-bonus found no bonus content folder (OPENTPW_BONUS_DATA, the saved bonus folder or <config>/bonus); pass --bonus-data <folder>." );
		}
		if ( root == null )
			return null;
		// the folder the catalog reads (the first levels parent within two levels) must hold bonus archives; a levels tree without them (a __MACOSX copy) would export no bonus sign
		var levelsParent = ObjectCatalog.FindBonusLevelsParent( root )
			?? throw new DirectoryNotFoundException( $"The bonus content folder '{root}' holds no levels folder (within two levels); see docs/OBJECTS.md." );
		if ( BonusContent.Validate( levelsParent ) == 0 )
			throw new DirectoryNotFoundException( $"The bonus content folder '{root}' holds no official bonus archives (_name_N.wad) in '{levelsParent}'; see docs/OBJECTS.md." );
		return root;
	}

	private static string? DefaultBonusRoot() =>
		BonusContent.ResolveRoot( null, Environment.GetEnvironmentVariable( "OPENTPW_BONUS_DATA" ), SetupSettings.Load( SetupSettings.GetDefaultPath() ).BonusPath,
			Path.GetDirectoryName( DisplaySettings.GetDefaultPath() )! )?.Path;

	/// <summary>The language and output directory of <c>--export-signs</c>.</summary>
	internal static (string Language, string OutputDirectory) ParseArguments( string[] args )
	{
		var index = Array.IndexOf( args, "--export-signs" );
		if ( index < 0 || index + 2 >= args.Length || args.Skip( index + 1 ).Take( 2 ).Any( value => value.StartsWith( "--", StringComparison.Ordinal ) || string.IsNullOrWhiteSpace( value ) ) )
			throw new ArgumentException( "--export-signs requires <language> <new or empty output directory> (docs/RUNNING.md)." );
		return (args[index + 1].Trim(), args[index + 2]);
	}

	/// <summary>
	/// Composes every sign in the current language and writes them, with <c>index.json</c>, into <paramref name="outputDirectory"/>, which must
	/// be new or empty and outside <paramref name="protectedDirectories"/> (the game and language data). Everything is composed before the
	/// first file is written.
	/// </summary>
	public static IReadOnlyList<ExportedFile> Run( string outputDirectory, params string[] protectedDirectories ) => Run( outputDirectory, null, protectedDirectories );

	/// <summary>As <see cref="Run(string, string[])"/>, with the texts of <paramref name="overrides"/> (<c>--sign-texts</c>) replacing those of the signs it names.</summary>
	public static IReadOnlyList<ExportedFile> Run( string outputDirectory, SignTextOverrides? overrides, params string[] protectedDirectories )
	{
		var root = PrepareOutputDirectory( outputDirectory, protectedDirectories.Concat( LanguageDirectories() ) );
		var signs = ComposeAll();
		if ( signs.Count == 0 )
			throw new InvalidDataException( "The game data defines no signs to export." );
		if ( overrides != null )
			signs = ApplyOverrides( signs, overrides );
		var files = Plan( signs );
		Directory.CreateDirectory( root );
		foreach ( var file in files )
		{
			var target = ResolveInside( root, file.RelativePath );
			Directory.CreateDirectory( Path.GetDirectoryName( target )! );
			WriteNew( target, file.Png );
		}
		var languageFolder = DescribeLanguageFolder( GameLanguage.Current.Directory, Settings.Default.GamePath );
		WriteNew( ResolveInside( root, IndexFileName ), System.Text.Encoding.UTF8.GetBytes( IndexJson( GameLanguage.Current.Name, languageFolder, files, CompatibilityRuntime.Flags.ToMetadata() ) ) );
		Log?.Trace( $"Sign export: {signs.Count} signs, {files.Count} PNG files (two textures and a preview per sign) in {root}." );
		return files;
	}

	/// <summary>
	/// Every sign of the four themes and the lobby in the current language, in theme order (THEMENAMES.str): per theme the park gate first,
	/// then the other object archives with a <c>.sgn</c> in archive-path order; the lobby's signs last.
	/// </summary>
	public static IReadOnlyList<ComposedSign> ComposeAll()
	{
		if ( GameEditions.Current != GameEdition.ThemeParkWorld )
			throw new NotSupportedException( "--export-signs exports the signs of Theme Park World data." );
		IReadOnlyList<string> objectNames;
		try
		{
			objectNames = GameLanguage.Current.LoadStrings( ObjectNames.FileName ).Entries;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			throw new FileNotFoundException( $"The {GameLanguage.Current.Name} language has no readable {ObjectNames.FileName} ({exception.Message}); the sign texts come from it.", exception );
		}
		var signs = new List<ComposedSign>();
		foreach ( var theme in OriginalGateSign.ThemeLevels )
			signs.AddRange( ComposeTheme( theme, objectNames ) );
		signs.AddRange( ComposeLobby( objectNames ) );
		return signs;
	}

	private static IEnumerable<ComposedSign> ComposeTheme( string theme, IReadOnlyList<string> objectNames )
	{
		var catalog = ObjectCatalog.Load( theme );
		var global = new SettingsFile( $"/levels/{theme}/global.sam" );
		// [DATA:levels/<level>/global.sam:ParkName.GateObjectId] the gate shows the park name, never its type sign (SignSlotPlan.For)
		int? gateId = int.TryParse( global["ParkName.GateObjectId"], out var parsed ) ? parsed : null;
		if ( gateId is { } id && catalog.Find( id ) is { } gate && !gate.IsExternal )
		{
			var sign = SignTextRenderer.LoadSignFile( gate.ArchivePath, gate.FileSystem )
				?? throw new InvalidDataException( $"The {theme} gate {gate.ArchivePath} has no .sgn." );
			var lines = OriginalGateSign.DefaultLines( theme, id );
			var row = ParkNameLines.Default( id, objectNames ) != null && ObjectNameTable.TryGet( id, out var first, out var second ) ? new[] { first, second } : Array.Empty<int>();
			var (left, right, _, _) = OriginalGateSign.ComposeHalves( sign, lines.Line1, lines.Line2 );
			yield return new ComposedSign( theme, "gate", gate.ArchiveName, gate.ArchivePath, SignPath( gate )!, id, row, Texts( row, objectNames ), lines.DisplayName, lines.Line1, lines.Line2, left, right )
				{ TextSource = row.Length > 0 ? $"{ObjectNames.FileName} gate row" : "THEMENAMES.str (no gate row)", SignFileSystem = gate.FileSystem };
		}
		else
			Log?.Warning( $"Sign export: {theme} names no gate object (ParkName.GateObjectId); its gate sign is not exported." );
		// the shipped objects in archive-path order, then the official bonus objects (only with --bonus-data); never a mod's or an extension's objects
		foreach ( var entry in catalog.Entries.Where( entry => (entry.Root.IsData || entry.Root.IsBonus) && entry.InfoId != gateId )
			.OrderBy( entry => entry.Root.IsBonus ).ThenBy( entry => entry.ArchivePath, StringComparer.OrdinalIgnoreCase ) )
		{
			if ( SignPath( entry ) is not { } signPath )
				continue;
			var images = ObjectSigns.Get( entry ) ?? throw new InvalidDataException( $"The sign of {entry.ArchivePath} could not be composed (see the log)." );
			var ids = entry.ObjectNameIndex is { } index ? Enumerable.Range( index, entry.ObjectNameLength ).ToArray() : Array.Empty<int>();
			yield return new ComposedSign( theme, entry.Category.ToString().ToLowerInvariant(), entry.ArchiveName, entry.ArchivePath, signPath, entry.InfoId, ids, Texts( ids, objectNames ),
				(entry.SignName ?? entry.DisplayName).Trim(), images.Line1, images.Line2, images.Left, images.Right ) { TextSource = TextSourceOf( entry ), IsBonus = entry.Root.IsBonus, SignFileSystem = entry.FileSystem };
		}
	}

	private static IEnumerable<ComposedSign> ComposeLobby( IReadOnlyList<string> objectNames )
	{
		// the lobby archive's terrain folder holds the .sgn of each island and gate model with sign slots (LobbyScene.QueueSigns)
		var members = FileSystem.GetFiles( LobbyScene.TerrainDirectory ).Where( file => file.EndsWith( ".sgn", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		var done = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var island in LobbyDefinition.Load().Islands )
		{
			foreach ( var model in new[] { island.IslandModel, island.GateModel } )
			{
				var member = members.FirstOrDefault( file => string.Equals( Path.GetFileNameWithoutExtension( file ), model, StringComparison.OrdinalIgnoreCase ) );
				if ( member == null || !done.Add( model ) )
					continue;
				var signPath = $"{LobbyScene.TerrainDirectory}/{Path.GetFileName( member )}";
				var lines = LobbySigns.Lines( island, objectNames );
				var images = LobbySigns.Compose( signPath, lines ) ?? throw new InvalidDataException( $"The lobby sign {signPath} could not be composed (see the log)." );
				var gate = LobbySigns.GateInfoId( island.Level );
				var row = gate is { } id && ParkNameLines.Default( id, objectNames ) != null && ObjectNameTable.TryGet( id, out var first, out var second ) ? new[] { first, second } : Array.Empty<int>();
				yield return new ComposedSign( LobbyFolder, $"lobby {island.Level}", model, LobbyScene.TerrainDirectory, signPath, gate, row, Texts( row, objectNames ), lines.DisplayName, images.Line1, images.Line2, images.Left, images.Right )
					{ TextSource = row.Length > 0 ? $"{ObjectNames.FileName} gate row" : "THEMENAMES.str (no gate row)", SignFileSystem = FileSystem };
			}
		}
	}

	/// <summary>The path after <c>--sign-texts</c>.</summary>
	internal static string SignTextsPath( string[] args )
	{
		var index = Array.IndexOf( args, "--sign-texts" );
		if ( index < 0 || index + 1 >= args.Length || args[index + 1].StartsWith( "--", StringComparison.Ordinal ) || string.IsNullOrWhiteSpace( args[index + 1] ) )
			throw new ArgumentException( "--sign-texts requires a JSON file of replacement sign texts (docs/RUNNING.md)." );
		return args[index + 1];
	}

	/// <summary>
	/// The signs with the texts of <paramref name="overrides"/>: each named sign is composed again from its own <c>.sgn</c> with the replacement lines,
	/// by the composer every sign path of the game ends in (<see cref="SignTextRenderer.ComposeSign"/>); the game's own text is kept in
	/// <see cref="ComposedSign.Original"/>. A key that names no exported sign is an error listing every such key, before anything is written.
	/// </summary>
	public static IReadOnlyList<ComposedSign> ApplyOverrides( IReadOnlyList<ComposedSign> signs, SignTextOverrides overrides )
	{
		var unknown = overrides.Entries.Keys.Where( key => !signs.Any( sign => SignTextOverrides.Matches( key, sign ) ) ).ToList();
		if ( unknown.Count > 0 )
			throw new InvalidDataException( $"{overrides.FileName} names {unknown.Count} sign(s) the export does not have: {string.Join( ", ", unknown.Select( key => $"{key.Theme}/{key.Object}" ) )}. The keys are the theme and object of index.json{(unknown.Any( key => key.Object.StartsWith( '_' ) ) ? "; bonus objects (_name_N) are exported only with --bonus-data" : "")}." );
		return signs.Select( sign =>
		{
			if ( !overrides.TryGet( sign, out var replacement ) )
				return sign;
			var fileSystem = sign.SignFileSystem ?? FileSystem;
			// read as the game reads the sign (SignTextRenderer.LoadSignFile, LobbySigns.Compose): the member's bytes with the enabled data corrections
			var signFile = new SignFile( CompatibilityRuntime.Correct( sign.SignPath, fileSystem.ReadAllBytes( sign.SignPath ) ) );
			var composed = SignTextRenderer.Shared.ComposeSign( signFile, new[] { replacement.Line1, replacement.Line2 }, default );
			foreach ( var diagnostic in composed.Diagnostics.Where( diagnostic => !diagnostic.StartsWith( "COMPAT-003", StringComparison.Ordinal ) ).Distinct() )
				Log?.Warning( $"Sign {sign.Theme}/{sign.Name} ({overrides.FileName}): {diagnostic}" );
			return sign with
			{
				Text = replacement.Text, Line1 = replacement.Line1, Line2 = replacement.Line2, Left = composed.Left, Right = composed.Right,
				TextSource = $"override ({overrides.FileName})", Original = sign
			};
		} ).ToList();
	}

	/// <summary>
	/// Where <see cref="ObjectSigns.Lines"/> takes an object's lines from: a bonus archive's <c>&lt;language&gt;.txt</c> SIGNA/SIGNB when it has that file,
	/// else its display name split at the middle space (its NAME, English as fallback, else the .sam Info.Name); a shipped object's OBJECT_NAMES row.
	/// </summary>
	internal static string TextSourceOf( ObjectCatalogEntry entry )
	{
		if ( !entry.IsBonus )
			return entry.SignName != null ? $"{ObjectNames.FileName} ids" : "display name";
		var language = GameLanguage.IsSelected ? GameLanguage.Current.Name : GameLanguage.DefaultLanguage;
		var file = entry.FileSystem.GetFiles( entry.ArchivePath ).Select( Path.GetFileName )
			.FirstOrDefault( name => name != null && name.EndsWith( ".txt", StringComparison.OrdinalIgnoreCase ) && string.Equals( Path.GetFileNameWithoutExtension( name ), language, StringComparison.OrdinalIgnoreCase ) );
		if ( file == null )
			return $"bonus display name (no {language}.txt): NAME of English.txt, else the .sam Info.Name, split at the middle space";
		return BonusNames.ReadSignLines( entry ) != null
			? $"{file} SIGNA/SIGNB"
			: $"bonus display name ({file} unreadable): NAME of English.txt, else the .sam Info.Name, split at the middle space";
	}

	private static string[] Texts( IReadOnlyList<int> ids, IReadOnlyList<string> objectNames ) =>
		ids.Select( id => id >= 0 && id < objectNames.Count ? objectNames[id] : "" ).ToArray();

	private static string? SignPath( ObjectCatalogEntry entry )
	{
		var member = entry.FileSystem.GetFiles( entry.ArchivePath ).FirstOrDefault( file => file.EndsWith( ".sgn", StringComparison.OrdinalIgnoreCase ) );
		return member == null ? null : $"{entry.ArchivePath.TrimEnd( '/' )}/{Path.GetFileName( member )}";
	}

	/// <summary>
	/// The files of <paramref name="signs"/>: two per sign, <c>&lt;theme&gt;/&lt;name&gt;-1.png</c> (sign1) and <c>-2.png</c> (sign2). A name that is
	/// taken in its folder (two archives of one name in different categories) gets its kind appended, then a number; a name is taken when any of its
	/// three files is (an object <c>foo-1</c> makes <c>foo-1.png</c>, which an object <c>foo</c> would also write).
	/// </summary>
	public static IReadOnlyList<ExportedFile> Plan( IReadOnlyList<ComposedSign> signs )
	{
		var taken = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var files = new List<ExportedFile>();
		foreach ( var sign in signs )
		{
			var folder = SafeSegment( sign.Theme );
			var baseName = SafeSegment( sign.Name );
			var name = baseName;
			bool Free( string candidate ) => new[] { 1, 2, 0 }.All( half => !taken.Contains( FileName( folder, candidate, half ) ) );
			for ( var number = 1; !Free( name ); number++ )
				name = number == 1 ? $"{baseName}_{SafeSegment( sign.Kind )}" : $"{baseName}_{SafeSegment( sign.Kind )}_{number}";
			foreach ( var half in new[] { 1, 2, 0 } )
				taken.Add( FileName( folder, name, half ) );
			foreach ( var half in new[] { 1, 2, 0 } )
			{
				var rgba = half switch { 1 => sign.Left, 2 => sign.Right, _ => SideBySide( sign.Left, sign.Right ) };
				var width = half == 0 ? SignSurfaceComposer.Width : SignSurfaceComposer.HalfWidth;
				var png = PngImage.EncodeRgba( width, SignSurfaceComposer.Height, rgba );
				files.Add( new ExportedFile( sign, half, FileName( folder, name, half ), width, SignSurfaceComposer.Height, Sha256( rgba ), Sha256( png ) ) { Png = png } );
			}
		}
		return files;
	}

	/// <summary><c>&lt;folder&gt;/&lt;name&gt;-&lt;half&gt;.png</c>; half 1 is the <c>sign1</c> texture, 2 the <c>sign2</c> texture, 0 the preview <c>&lt;folder&gt;/&lt;name&gt;.png</c>.</summary>
	public static string FileName( string folder, string name, int half )
	{
		if ( half is not (0 or 1 or 2) )
			throw new ArgumentOutOfRangeException( nameof( half ), "A sign has the halves 1 (sign1) and 2 (sign2), and the preview 0." );
		return half == 0 ? $"{SafeSegment( folder )}/{SafeSegment( name )}.png" : $"{SafeSegment( folder )}/{SafeSegment( name )}-{half}.png";
	}

	/// <summary>The whole sign as the composer drew it: the <c>sign1</c> half left of the <c>sign2</c> half (<see cref="SignSurfaceComposer.Width"/> x <see cref="SignSurfaceComposer.Height"/>).</summary>
	public static byte[] SideBySide( byte[] left, byte[] right )
	{
		const int rowBytes = SignSurfaceComposer.HalfWidth * 4;
		if ( left.Length != rowBytes * SignSurfaceComposer.Height || right.Length != left.Length )
			throw new InvalidDataException( $"Sign halves must be {SignSurfaceComposer.HalfWidth}x{SignSurfaceComposer.Height} RGBA." );
		var whole = new byte[left.Length * 2];
		for ( var row = 0; row < SignSurfaceComposer.Height; row++ )
		{
			Buffer.BlockCopy( left, row * rowBytes, whole, row * rowBytes * 2, rowBytes );
			Buffer.BlockCopy( right, row * rowBytes, whole, row * rowBytes * 2 + rowBytes, rowBytes );
		}
		return whole;
	}

	/// <summary>A file-name segment from data: letters, digits, '-', '_' and '.' kept, anything else '_'; never empty, "." or "..".</summary>
	public static string SafeSegment( string value )
	{
		ArgumentNullException.ThrowIfNull( value );
		var chars = value.Trim().Select( c => char.IsAsciiLetterOrDigit( c ) || c is '-' or '_' or '.' ? c : '_' ).ToArray();
		var segment = new string( chars ).Trim( '.' );
		return segment.Length == 0 ? "_" : segment;
	}

	/// <summary>The full path of <paramref name="relativePath"/> under <paramref name="root"/>; a path that leaves the root is an error.</summary>
	public static string ResolveInside( string root, string relativePath )
	{
		var fullRoot = Path.TrimEndingDirectorySeparator( Path.GetFullPath( root ) );
		if ( Path.IsPathRooted( relativePath ) )
			throw new ArgumentException( $"'{relativePath}' is not relative to the output directory." );
		var full = Path.GetFullPath( Path.Combine( fullRoot, relativePath ) );
		if ( !full.StartsWith( fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal ) )
			throw new ArgumentException( $"'{relativePath}' leaves the output directory." );
		return full;
	}

	/// <summary>
	/// The full output path, after checking that it is new or an empty directory (nothing is overwritten) and that it is not inside one of
	/// <paramref name="protectedDirectories"/> (the game and language data are never written).
	/// </summary>
	public static string PrepareOutputDirectory( string outputDirectory, IEnumerable<string?> protectedDirectories )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( outputDirectory );
		var root = Path.TrimEndingDirectorySeparator( Path.GetFullPath( outputDirectory ) );
		// compared with symbolic links resolved, so a link into the data (or /tmp for /private/tmp) is no way around the check
		var resolvedRoot = ResolveLinks( root );
		foreach ( var directory in protectedDirectories.Where( directory => !string.IsNullOrWhiteSpace( directory ) ) )
		{
			var full = ResolveLinks( directory! );
			if ( string.Equals( resolvedRoot, full, StringComparison.OrdinalIgnoreCase ) || resolvedRoot.StartsWith( full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase )
				|| full.StartsWith( resolvedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) )
				throw new IOException( $"The sign export's output directory '{root}' overlaps the game, language or bonus data '{full}'; choose a directory outside it." );
		}
		if ( File.Exists( root ) )
			throw new IOException( $"The sign export's output '{root}' is a file; choose a new or empty directory." );
		if ( Directory.Exists( root ) && Directory.EnumerateFileSystemEntries( root ).Any() )
			throw new IOException( $"The sign export's output directory '{root}' is not empty; choose a new or empty directory (nothing is overwritten)." );
		return root;
	}

	/// <summary>The directories of the language data in use: the exported language's folder, its overlay data folder and the overlay offered beside it.</summary>
	private static IEnumerable<string?> LanguageDirectories() => GameLanguage.IsSelected
		? new[] { GameLanguage.Current.Directory, GameLanguage.Current.OverlayDataDirectory, GameLanguage.AvailableOverlay }
		: new[] { GameLanguage.AvailableOverlay };

	/// <summary>
	/// The full path of <paramref name="path"/> with every symbolic link along it resolved (segments that do not exist yet are kept as given), without a
	/// trailing separator.
	/// </summary>
	public static string ResolveLinks( string path ) => ResolveLinks( path, 0 );

	private static string ResolveLinks( string path, int depth )
	{
		var full = Path.TrimEndingDirectorySeparator( Path.GetFullPath( path ) );
		var current = Path.GetPathRoot( full ) ?? "";
		foreach ( var segment in full[current.Length..].Split( Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries ) )
		{
			current = Path.Combine( current, segment );
			FileSystemInfo entry = Directory.Exists( current ) ? new DirectoryInfo( current ) : new FileInfo( current );
			if ( !entry.Exists || entry.LinkTarget == null )
				continue;
			if ( depth >= 40 )
				throw new IOException( $"Too many symbolic links in '{path}'." );
			// a link's target may itself run through links (/var -> /private/var on macOS)
			var target = Path.IsPathRooted( entry.LinkTarget ) ? entry.LinkTarget : Path.Combine( Path.GetDirectoryName( current ) ?? "", entry.LinkTarget );
			current = ResolveLinks( target, depth + 1 );
		}
		return Path.TrimEndingDirectorySeparator( current );
	}

	/// <summary>
	/// The language folder as <c>index.json</c> names it, without the user's own path (the file is meant to be shared): relative to the game folder
	/// when it lies inside it (<c>Data/Language/English</c>), else from its <c>Language</c> folder on (<c>Language/spanish</c>), else its name.
	/// </summary>
	public static string DescribeLanguageFolder( string languageDirectory, string? gameFolder )
	{
		var full = Path.TrimEndingDirectorySeparator( Path.GetFullPath( languageDirectory ) );
		if ( !string.IsNullOrWhiteSpace( gameFolder ) )
		{
			var game = Path.TrimEndingDirectorySeparator( Path.GetFullPath( gameFolder ) );
			if ( full.StartsWith( game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) )
				return full[(game.Length + 1)..].Replace( Path.DirectorySeparatorChar, '/' );
		}
		var segments = full.Split( Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries );
		var language = Array.FindLastIndex( segments, segment => string.Equals( segment, "Language", StringComparison.OrdinalIgnoreCase ) );
		return language >= 0 ? string.Join( '/', segments[language..] ) : segments.LastOrDefault() ?? "";
	}

	private static void WriteNew( string path, byte[] bytes )
	{
		using var stream = new FileStream( path, FileMode.CreateNew, FileAccess.Write, FileShare.None );
		stream.Write( bytes );
	}

	private static string Sha256( byte[] bytes ) => Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant();

	/// <summary>The index: the language and one record per file (theme, kind, object, archive, .sgn, info id, string ids and texts, lines, file, size, hashes).</summary>
	public static string IndexJson( string language, string languageDirectory, IReadOnlyList<ExportedFile> files, string? compatibility = null )
	{
		using var buffer = new MemoryStream();
		using ( var writer = new Utf8JsonWriter( buffer, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping } ) )
		{
			writer.WriteStartObject();
			writer.WriteString( "tool", "OpenTPW --export-signs (debug export, docs/RUNNING.md)" );
			writer.WriteString( "language", language );
			writer.WriteString( "languageFolder", languageDirectory );
			writer.WriteString( "stringTable", ObjectNames.FileName );
			// the compatibility profile decides one data correction of the sign files (sign-font-substitution, docs/COMPATIBILITY.md)
			if ( compatibility != null )
				writer.WriteString( "compatibility", compatibility );
			writer.WriteNumber( "count", files.Count );
			writer.WriteStartArray( "files" );
			foreach ( var file in files )
			{
				var sign = file.Sign;
				writer.WriteStartObject();
				writer.WriteString( "theme", sign.Theme );
				writer.WriteString( "kind", sign.Kind );
				writer.WriteString( "object", sign.Name );
				writer.WriteString( "archive", sign.ArchivePath );
				writer.WriteString( "sgn", sign.SignPath );
				if ( sign.InfoId is { } infoId )
					writer.WriteNumber( "infoId", infoId );
				else
					writer.WriteNull( "infoId" );
				writer.WriteStartArray( "stringIds" );
				foreach ( var id in sign.StringIds )
					writer.WriteNumberValue( id );
				writer.WriteEndArray();
				writer.WriteStartArray( "strings" );
				foreach ( var text in sign.StringTexts )
					writer.WriteStringValue( text );
				writer.WriteEndArray();
				writer.WriteString( "text", sign.Text );
				writer.WriteString( "textSource", sign.TextSource );
				if ( sign.Original is { } original )
				{
					// the text the game itself draws on this sign (stringIds and strings above are that text's OBJECT_NAMES entries)
					writer.WriteString( "originalText", original.Text );
					writer.WriteString( "originalLine1", original.Line1 );
					writer.WriteString( "originalLine2", original.Line2 );
					writer.WriteString( "originalTextSource", original.TextSource );
				}
				if ( sign.IsBonus )
					writer.WriteBoolean( "bonus", true );
				writer.WriteString( "line1", sign.Line1 );
				writer.WriteString( "line2", sign.Line2 );
				writer.WriteString( "texture", file.Half switch { 1 => "sign1", 2 => "sign2", _ => "preview" } );
				writer.WriteString( "file", file.RelativePath );
				writer.WriteNumber( "width", file.Width );
				writer.WriteNumber( "height", file.Height );
				writer.WriteString( "rgbaSha256", file.RgbaSha256 );
				writer.WriteString( "pngSha256", file.PngSha256 );
				writer.WriteEndObject();
			}
			writer.WriteEndArray();
			writer.WriteEndObject();
		}
		return System.Text.Encoding.UTF8.GetString( buffer.ToArray() ) + "\n";
	}
}

/// <summary>
/// The replacement sign texts of <c>--sign-texts &lt;file&gt;</c> (docs/RUNNING.md): a UTF-8 JSON object with a <c>signs</c> array whose entries name one
/// sign by <c>theme</c> and <c>object</c> (as <c>index.json</c> names it; the gate is the object <c>gates</c> of its theme, the lobby's signs are theme
/// <c>lobby</c>) and give either <c>lines</c> (exactly two strings, drawn as they are) or <c>name</c> (one string, split into two lines with the rule the
/// game uses for a display name, <see cref="SignCanvas.SplitAtMiddleSpace"/>). Keys are matched without regard to case; a duplicate key, an unknown
/// member, an entry with both or neither of <c>lines</c> and <c>name</c>, or a line with a control character is an error. The file is only read.
/// </summary>
public sealed class SignTextOverrides
{
	/// <summary>The largest file accepted.</summary>
	public const int MaximumFileBytes = 4 * 1024 * 1024;

	public readonly record struct Key( string Theme, string Object );

	/// <summary>One replacement: the two lines that are drawn, and the text as given (the name, or the two lines joined by a space).</summary>
	public sealed record Replacement( string Line1, string Line2, string Text );

	private SignTextOverrides( string fileName, IReadOnlyDictionary<Key, Replacement> entries )
	{
		FileName = fileName;
		Entries = entries;
	}

	/// <summary>The override file's name (without its folder: index.json is meant to be shared).</summary>
	public string FileName { get; }
	public IReadOnlyDictionary<Key, Replacement> Entries { get; }

	public static SignTextOverrides Load( string path )
	{
		var info = new FileInfo( path );
		if ( !info.Exists )
			throw new FileNotFoundException( $"The sign texts file '{path}' does not exist.", path );
		if ( info.Length > MaximumFileBytes )
			throw new InvalidDataException( $"The sign texts file '{path}' is larger than {MaximumFileBytes} bytes." );
		return Parse( File.ReadAllBytes( path ), info.Name );
	}

	/// <summary>Parses the JSON text of an override file named <paramref name="fileName"/>.</summary>
	public static SignTextOverrides Parse( byte[] json, string fileName )
	{
		JsonDocument document;
		try
		{
			document = JsonDocument.Parse( json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, AllowDuplicateProperties = false } );
		}
		catch ( JsonException exception )
		{
			throw new InvalidDataException( $"{fileName} is not valid JSON: {exception.Message}", exception );
		}
		using ( document )
		{
			var root = document.RootElement;
			if ( root.ValueKind != JsonValueKind.Object )
				throw new InvalidDataException( $"{fileName}: the top level must be an object with a \"signs\" array." );
			foreach ( var property in root.EnumerateObject() )
				if ( property.Name is not ("signs" or "description") )
					throw new InvalidDataException( $"{fileName}: unknown top-level member \"{property.Name}\" (allowed: signs, description)." );
			if ( root.TryGetProperty( "description", out var description ) && description.ValueKind != JsonValueKind.String )
				throw new InvalidDataException( $"{fileName}: \"description\" must be a string." );
			if ( !root.TryGetProperty( "signs", out var signs ) || signs.ValueKind != JsonValueKind.Array )
				throw new InvalidDataException( $"{fileName}: a \"signs\" array is required." );
			var entries = new Dictionary<Key, Replacement>( KeyComparer.Instance );
			var position = 0;
			foreach ( var sign in signs.EnumerateArray() )
			{
				var where = $"{fileName}: signs[{position++}]";
				if ( sign.ValueKind != JsonValueKind.Object )
					throw new InvalidDataException( $"{where} must be an object." );
				foreach ( var property in sign.EnumerateObject() )
					if ( property.Name is not ("theme" or "object" or "lines" or "name") )
						throw new InvalidDataException( $"{where}: unknown member \"{property.Name}\" (allowed: theme, object, lines, name)." );
				var theme = RequiredString( sign, "theme", where );
				var name = RequiredString( sign, "object", where );
				var hasLines = sign.TryGetProperty( "lines", out var lines );
				var hasName = sign.TryGetProperty( "name", out var text );
				if ( hasLines == hasName )
					throw new InvalidDataException( $"{where} ({theme}/{name}) needs exactly one of \"lines\" (two strings) and \"name\" (one string)." );
				Replacement replacement;
				if ( hasLines )
				{
					if ( lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() != 2 || lines.EnumerateArray().Any( line => line.ValueKind != JsonValueKind.String ) )
						throw new InvalidDataException( $"{where} ({theme}/{name}): \"lines\" must be an array of exactly two strings." );
					var line1 = CheckedText( StringOf( lines[0], where ), where );
					var line2 = CheckedText( StringOf( lines[1], where ), where );
					replacement = new Replacement( line1, line2, line2.Length == 0 ? line1 : line1.Length == 0 ? line2 : $"{line1} {line2}" );
				}
				else
				{
					if ( text.ValueKind != JsonValueKind.String )
						throw new InvalidDataException( $"{where} ({theme}/{name}): \"name\" must be a string." );
					var full = CheckedText( StringOf( text, where ), where );
					// the game's rule for a sign that has one name (ObjectSigns.Lines: the display name, trimmed, split at the middle space)
					var (line1, line2) = SplitName( full );
					replacement = new Replacement( line1, line2, full.Trim() );
				}
				if ( !entries.TryAdd( new Key( theme, name ), replacement ) )
					throw new InvalidDataException( $"{where}: {theme}/{name} is listed twice." );
			}
			return new SignTextOverrides( fileName, entries );
		}
	}

	/// <summary>The two lines the game draws for a sign with one name: the name trimmed and split at the space nearest its middle (<see cref="ObjectSigns.Lines"/>).</summary>
	public static (string Line1, string Line2) SplitName( string name ) => SignCanvas.SplitAtMiddleSpace( name.Trim() );

	internal static bool Matches( Key key, SignExport.ComposedSign sign ) => KeyComparer.Instance.Equals( key, new Key( sign.Theme, sign.Name ) );

	public bool TryGet( SignExport.ComposedSign sign, out Replacement replacement ) => Entries.TryGetValue( new Key( sign.Theme, sign.Name ), out replacement! );

	private static string RequiredString( JsonElement element, string member, string where )
	{
		var text = element.TryGetProperty( member, out var value ) && value.ValueKind == JsonValueKind.String ? StringOf( value, where ) : null;
		return string.IsNullOrWhiteSpace( text ) ? throw new InvalidDataException( $"{where}: \"{member}\" must be a non-empty string." ) : text.Trim();
	}

	// a line or paragraph separator (U+2028/U+2029) is no glyph in the sign fonts and draws a .notdef box, so it is refused like a control character
	private static string CheckedText( string text, string where ) =>
		text.Any( c => char.IsControl( c ) || char.GetUnicodeCategory( c ) is System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator )
			? throw new InvalidDataException( $"{where}: a sign text must not contain control characters or line/paragraph separators (line breaks are given as two lines)." )
			: text;

	/// <summary>A JSON string's value; an escaped lone surrogate ("\ud800"), which is no text, is an error of the file.</summary>
	private static string StringOf( JsonElement element, string where )
	{
		try
		{
			return element.GetString()!;
		}
		catch ( InvalidOperationException exception )
		{
			throw new InvalidDataException( $"{where}: a string is not valid text ({exception.Message}).", exception );
		}
	}

	private sealed class KeyComparer : IEqualityComparer<Key>
	{
		public static readonly KeyComparer Instance = new();
		public bool Equals( Key x, Key y ) => string.Equals( x.Theme, y.Theme, StringComparison.OrdinalIgnoreCase ) && string.Equals( x.Object, y.Object, StringComparison.OrdinalIgnoreCase );
		public int GetHashCode( Key key ) => HashCode.Combine( StringComparer.OrdinalIgnoreCase.GetHashCode( key.Theme ), StringComparer.OrdinalIgnoreCase.GetHashCode( key.Object ) );
	}
}
