namespace OpenTPW;

/// <summary>
/// The TrueType fonts the original game ships in <c>Data/fonts.wad</c> (17 files such as
/// <c>YOUNIA__.TTF</c> "Young Itch AOE"), read in memory straight from the WAD. The original game
/// installs them with <c>AddFontResourceA</c> and draws park-gate and ride/shop sign text with
/// GDI (<c>CreateFontIndirectA</c>, LOGFONT records in each object's <c>.sgn</c>); OpenTPW never
/// extracts or installs them.
/// </summary>
public sealed class SignFontLibrary
{
	public const string WadPath = "/fonts.wad";
	public const int ShippedFontCount = 17;

	private readonly Dictionary<string, TrueTypeFont> byFileName = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, TrueTypeFont> byFamily = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, byte[]> rawFiles = new( StringComparer.OrdinalIgnoreCase );

	private SignFontLibrary() { }

	/// <summary>File names in WAD order.</summary>
	public IReadOnlyList<string> FileNames { get; private set; } = Array.Empty<string>();
	public IEnumerable<TrueTypeFont> Fonts => FileNames.Select( name => byFileName[name] );
	/// <summary>Fonts that failed to parse (file name and reason); the rest stay usable.</summary>
	public IReadOnlyList<string> Diagnostics { get; private set; } = Array.Empty<string>();

	/// <summary>Reads every <c>.ttf</c> member of a <c>fonts.wad</c> stream.</summary>
	public static SignFontLibrary Load( Stream wadStream )
	{
		var archive = new WadArchive( wadStream );
		var library = new SignFontLibrary();
		var names = new List<string>();
		var diagnostics = new List<string>();
		foreach ( var name in archive.GetFiles( "" ).Where( name => name.EndsWith( ".ttf", StringComparison.OrdinalIgnoreCase ) ) )
		{
			var bytes = archive.GetFile( name ).GetData();
			try
			{
				library.Add( name, bytes );
				names.Add( name );
			}
			catch ( InvalidDataException exception )
			{
				diagnostics.Add( $"{name}: {exception.Message}" );
			}
		}
		library.FileNames = names;
		library.Diagnostics = diagnostics;
		return library;
	}

	/// <summary>Loads <c>fonts.wad</c> from the game file system.</summary>
	public static SignFontLibrary LoadFromGameData()
	{
		using var stream = File.OpenRead( FileSystem.GetAbsolutePath( WadPath ) );
		return Load( stream );
	}

	/// <summary>Builds a library from loose font files (tests, tools).</summary>
	public static SignFontLibrary FromFiles( IEnumerable<(string Name, byte[] Data)> files )
	{
		var library = new SignFontLibrary();
		var names = new List<string>();
		foreach ( var (name, bytes) in files )
		{
			library.Add( name, bytes );
			names.Add( name );
		}
		library.FileNames = names;
		return library;
	}

	private void Add( string name, byte[] bytes )
	{
		var font = TrueTypeFont.Parse( bytes, name );
		byFileName[name] = font;
		rawFiles[name] = bytes;
		if ( font.FamilyName.Length > 0 )
			byFamily.TryAdd( font.FamilyName, font );
		if ( font.FullName.Length > 0 )
			byFamily.TryAdd( font.FullName, font );
	}

	/// <summary>The decompressed font file bytes (for hash verification).</summary>
	public byte[] GetFileData( string fileName ) => rawFiles[fileName];

	/// <summary>Finds a font by file name (<c>YOUNIA__.TTF</c>) or by family/full name (<c>Young Itch AOE</c>), ignoring case.</summary>
	public TrueTypeFont? Find( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return null;
		name = name.Trim();
		if ( byFileName.TryGetValue( name, out var font ) || byFamily.TryGetValue( name, out font ) )
			return font;
		return byFileName.TryGetValue( name + ".TTF", out font ) ? font : null;
	}

	public TrueTypeFont Get( string name ) => Find( name ) ?? throw new KeyNotFoundException( $"Sign font '{name}' is not in fonts.wad. Available: {string.Join( ", ", FileNames )}." );
}
