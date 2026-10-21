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
		IReadOnlyList<int> StringIds, IReadOnlyList<string> StringTexts, string Text, string Line1, string Line2, byte[] Left, byte[] Right );

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
		LanguageStartup.Apply( dataDirectory, language, languageData, cdFolder, false, message => Log?.Warning( message ) );
		if ( !GameLanguage.IsSelected || !string.Equals( GameLanguage.Current.Name, GameLanguage.CanonicalName( language ), StringComparison.OrdinalIgnoreCase ) )
			throw new DirectoryNotFoundException( $"Language '{language}' was not selected; see docs/LANGUAGES.md for where the language data is found." );
		Log?.Trace( $"Language: {GameLanguage.Current}" );
		// nothing is written into the game folder, its data, any language data (the one exported and the overlay offered beside it) or the CD folder;
		// checked before anything else starts
		var protectedDirectories = new[] { dataDirectory, Settings.Default.GamePath, languageData, cdFolder }.OfType<string>().ToArray();
		PrepareOutputDirectory( outputDirectory, protectedDirectories.Concat( LanguageDirectories() ) );
		CompatibilityStartup.Initialize( args, dataDirectory );
		// Only the shipped data: no official bonus objects and no mod content (mods are not started for the export).
		ObjectCatalog.BonusDataRoot = null;
		var files = Run( outputDirectory, protectedDirectories );
		var previews = files.Count( file => file.Half == 0 );
		Console.WriteLine( $"Exported {files.Count - previews} sign textures and {previews} previews ({previews} signs) in {GameLanguage.Current.Name} to {Path.GetFullPath( outputDirectory )}; index in {IndexFileName}." );
	}

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
	public static IReadOnlyList<ExportedFile> Run( string outputDirectory, params string[] protectedDirectories )
	{
		var root = PrepareOutputDirectory( outputDirectory, protectedDirectories.Concat( LanguageDirectories() ) );
		var signs = ComposeAll();
		if ( signs.Count == 0 )
			throw new InvalidDataException( "The game data defines no signs to export." );
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
			yield return new ComposedSign( theme, "gate", gate.ArchiveName, gate.ArchivePath, SignPath( gate )!, id, row, Texts( row, objectNames ), lines.DisplayName, lines.Line1, lines.Line2, left, right );
		}
		else
			Log?.Warning( $"Sign export: {theme} names no gate object (ParkName.GateObjectId); its gate sign is not exported." );
		foreach ( var entry in catalog.Entries.Where( entry => !entry.IsExternal && entry.InfoId != gateId ).OrderBy( entry => entry.ArchivePath, StringComparer.OrdinalIgnoreCase ) )
		{
			if ( SignPath( entry ) is not { } signPath )
				continue;
			var images = ObjectSigns.Get( entry ) ?? throw new InvalidDataException( $"The sign of {entry.ArchivePath} could not be composed (see the log)." );
			var ids = entry.ObjectNameIndex is { } index ? Enumerable.Range( index, entry.ObjectNameLength ).ToArray() : Array.Empty<int>();
			yield return new ComposedSign( theme, entry.Category.ToString().ToLowerInvariant(), entry.ArchiveName, entry.ArchivePath, signPath, entry.InfoId, ids, Texts( ids, objectNames ),
				(entry.SignName ?? entry.DisplayName).Trim(), images.Line1, images.Line2, images.Left, images.Right );
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
				yield return new ComposedSign( LobbyFolder, $"lobby {island.Level}", model, LobbyScene.TerrainDirectory, signPath, gate, row, Texts( row, objectNames ), lines.DisplayName, images.Line1, images.Line2, images.Left, images.Right );
			}
		}
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
				throw new IOException( $"The sign export's output directory '{root}' overlaps the game or language data '{full}'; choose a directory outside it." );
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
