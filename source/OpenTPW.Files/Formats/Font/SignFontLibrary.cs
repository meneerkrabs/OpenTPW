namespace OpenTPW;

/// <summary>
/// The TrueType fonts the original game ships in <c>Data/fonts.wad</c> (17 files such as
/// <c>YOUNIA__.TTF</c> "Young Itch AOE"), read in memory straight from the WAD. The original game
/// installs them with <c>AddFontResourceA</c> and draws park-gate and ride/shop sign text with
/// GDI (<c>CreateFontIndirectA</c>, LOGFONT records in each object's <c>.sgn</c>); OpenTPW never
/// extracts or installs them. The Korean CD's <c>fonts.wad</c> adds the TrueType collection <c>gulim.ttc</c> (Gulim, GulimChe,
/// Dotum, DotumChe), which every sign of that CD names; its LOGFONT face names are the Korean family names in the ANSI code page 949
/// ("굴림" is the bytes B1 BC B8 B2), which this library matches to the collection's members (<see cref="Find(string, string)"/>).
/// </summary>
public sealed class SignFontLibrary
{
	public const string WadPath = "/fonts.wad";
	public const int ShippedFontCount = 17;

	private readonly Dictionary<string, TrueTypeFont> byFileName = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, TrueTypeFont> byFamily = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, byte[]> rawFiles = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, IReadOnlyList<TrueTypeFont>> collections = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The code page 949 bytes (as a Latin-1 string, how <see cref="SignFile"/> reads a LOGFONT face name) of the Korean family names
	/// of the fonts in <c>gulim.ttc</c> and the other Korean system fonts a sign may name.
	/// </summary>
	private static readonly Dictionary<string, string> Cp949FaceNames = new()
	{
		["굴림"] = "\u00B1\u00BC\u00B8\u00B2",
		["굴림체"] = "\u00B1\u00BC\u00B8\u00B2\u00C3\u00BC",
		["돋움"] = "\u00B5\u00B8\u00BF\u00F2",
		["돋움체"] = "\u00B5\u00B8\u00BF\u00F2\u00C3\u00BC",
		["바탕"] = "\u00B9\u00D9\u00C5\u00C1",
		["궁서"] = "\u00B1\u00C3\u00BC\u00AD",
	};

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
		foreach ( var name in archive.GetFiles( "" ).Where( name => name.EndsWith( ".ttf", StringComparison.OrdinalIgnoreCase ) || name.EndsWith( ".ttc", StringComparison.OrdinalIgnoreCase ) ) )
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
		var members = TrueTypeFont.IsCollection( bytes ) ? TrueTypeFont.ParseCollection( bytes, name ) : new[] { TrueTypeFont.Parse( bytes, name ) };
		byFileName[name] = members[0];
		rawFiles[name] = bytes;
		if ( members.Count > 1 || TrueTypeFont.IsCollection( bytes ) )
			collections[name] = members;
		foreach ( var font in members )
		{
			foreach ( var family in font.FamilyNames )
				byFamily.TryAdd( family, font );
			if ( font.FamilyName.Length > 0 )
				byFamily.TryAdd( font.FamilyName, font );
			if ( font.FullName.Length > 0 )
				byFamily.TryAdd( font.FullName, font );
		}
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

	/// <summary>
	/// The font a sign slot names: a file of the library by its file name (the first font of a collection), else a font by face name. In a
	/// collection such as <c>gulim.ttc</c> the face name picks the member: the English or Korean family name, or the Korean name as
	/// the code page 949 bytes a LOGFONT stores ("굴림" Gulim, "굴림체" GulimChe); an unknown face keeps the first member.
	/// </summary>
	public TrueTypeFont? Find( string fileName, string faceName )
	{
		if ( collections.TryGetValue( fileName?.Trim() ?? "", out var members ) )
		{
			var face = faceName?.Trim() ?? "";
			return members.FirstOrDefault( member => member.FamilyNames.Any( family => string.Equals( family, face, StringComparison.OrdinalIgnoreCase )
				|| Cp949FaceNames.TryGetValue( family, out var ansi ) && string.Equals( ansi, face, StringComparison.Ordinal ) ) ) ?? members[0];
		}
		return Find( fileName ?? "" ) ?? Find( faceName ?? "" );
	}

	/// <summary>The fonts of a collection file in its directory order (empty for a plain font file).</summary>
	public IReadOnlyList<TrueTypeFont> Collection( string fileName ) => collections.TryGetValue( fileName, out var members ) ? members : Array.Empty<TrueTypeFont>();

	public TrueTypeFont Get( string name ) => Find( name ) ?? throw new KeyNotFoundException( $"Sign font '{name}' is not in fonts.wad. Available: {string.Join( ", ", FileNames )}." );
}
