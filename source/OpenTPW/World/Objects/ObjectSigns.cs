namespace OpenTPW;

/// <summary>The two sign textures of one object type and name: RGBA halves, plus the GPU textures once requested.</summary>
public sealed class ObjectSignImages
{
	internal ObjectSignImages( string line1, string line2, byte[] left, byte[] right, int textPixels, IReadOnlyList<string> diagnostics )
	{
		Line1 = line1;
		Line2 = line2;
		Left = left;
		Right = right;
		TextPixelCount = textPixels;
		Diagnostics = diagnostics;
	}

	public string Line1 { get; }
	public string Line2 { get; }
	/// <summary>RGBA of the <c>sign1</c> texture (<see cref="SignCanvas.HalfWidth"/> x <see cref="SignCanvas.Height"/>).</summary>
	public byte[] Left { get; }
	/// <summary>RGBA of the <c>sign2</c> texture.</summary>
	public byte[] Right { get; }
	/// <summary>Pixels the text changed against the board colour (0: nothing was drawn).</summary>
	public int TextPixelCount { get; }
	public IReadOnlyList<string> Diagnostics { get; }

	private Texture? leftTexture;
	private Texture? rightTexture;

	/// <summary>GPU texture of the <c>sign1</c> half (created on first use, shared by every object of the type).</summary>
	public Texture LeftTexture => leftTexture ??= new Texture( Left, SignCanvas.HalfWidth, SignCanvas.Height );
	/// <summary>GPU texture of the <c>sign2</c> half.</summary>
	public Texture RightTexture => rightTexture ??= new Texture( Right, SignCanvas.HalfWidth, SignCanvas.Height );

	/// <summary>The texture for a material slot name (<c>sign1</c>/<c>sign2</c>, with or without extension), or null.</summary>
	public Texture? TextureFor( string textureName ) => ObjectSigns.SlotOf( textureName ) switch { 0 => LeftTexture, 1 => RightTexture, _ => null };
}

/// <summary>
/// The text on ride, shop and sideshow signs. Object models that carry the material slots <c>sign1</c>/<c>sign2</c>
/// get their textures from here instead of the magenta "SIGN1"/"SIGN2" placeholders: the object's name is split into
/// two lines and drawn with the fonts and effects of the object's <c>.sgn</c> (docs/COMPATIBILITY.md).
/// </summary>
public static class ObjectSigns
{
	private static readonly Dictionary<string, ObjectSignImages?> cache = new( StringComparer.Ordinal );

	/// <summary>0 for <c>sign1</c>, 1 for <c>sign2</c> (extension and case ignored), else -1.</summary>
	public static int SlotOf( string textureName ) => Path.GetFileNameWithoutExtension( textureName.TrimEnd( '\0' ) ).ToLowerInvariant() switch { "sign1" => 0, "sign2" => 1, _ => -1 };

	/// <summary>True for a material name that the game draws at run time.</summary>
	public static bool IsSignSlot( string textureName ) => SlotOf( textureName ) >= 0;

	/// <summary>
	/// The two lines of an object's sign. The name is the object's display name in the current language (the
	/// OBJECT_NAMES entries joined by a space, as the build list shows it). Bonus objects whose language file gives
	/// <c>SIGNA</c>/<c>SIGNB</c> use those two lines.
	/// </summary>
	// [BIN:STP-PPC:0x1018E254 sign text builder] one name string is split at its middle space (SignCanvas.SplitAtMiddleSpace); a caller may instead pass the two lines (text slot 0 and 1) explicitly
	// [BIN:STP-PPC:0x1011A08C object name builder] the name string (info record +0x70C) is the first OBJECT_NAMES text, a space, then the second ("%ls%ls%ls"); an entry ending in '-' is joined to the next without space or dash
	// [BIN:STP-PPC:0x101598C4 object sign call] for objects built from the catalogue the sign text is that name string of the object's info record (no explicit text passed)
	// [APPROX:COMPAT-016] the display name stands for the info record's name string: the join of the OBJECT_NAMES entries this port binds by English name (RIDES-010) and, for unbound objects, the .sam name — evidence needed: the record field that selects the entries and a capture of a shop or sideshow sign
	public static (string Line1, string Line2) Lines( ObjectCatalogEntry entry )
	{
		if ( entry.IsBonus && BonusNames.ReadSignLines( entry ) is { } explicitLines )
			return explicitLines;
		return SignCanvas.SplitAtMiddleSpace( entry.DisplayName.Trim() );
	}

	/// <summary>Cream board for signs whose text colours are dark, the gate's dark board otherwise.</summary>
	public static readonly (byte R, byte G, byte B, byte A) LightBoard = (214, 190, 140, 255);

	// [APPROX:COMPAT-018] flat board colour chosen so the stored text colours stay readable (light or the gate's dark board, whichever gives the least contrasting line more contrast); the board images (version-101 wavelet, plus two 16x128 fill images) are not decoded or composed — evidence needed: Bitmap::load_wavelet and the board blit (as COMPAT-004) and a capture of an original ride sign
	public static (byte R, byte G, byte B, byte A) Board( SignFile sign )
	{
		static double Luma( byte r, byte g, byte b ) => 0.299 * r + 0.587 * g + 0.114 * b;
		var lumas = Enumerable.Range( 0, sign.Slots.Count ).Select( index => SignCanvas.SlotColor( sign, index ) )
			.Where( colour => colour != null ).Select( colour => Luma( colour!.Value.R, colour.Value.G, colour.Value.B ) ).ToArray();
		if ( lumas.Length == 0 )
			return OriginalGateSign.Background;
		// the board on which the least contrasting line still stands out most (two coloured lines can need different boards)
		double Worst( (byte R, byte G, byte B, byte A) board ) => lumas.Min( luma => Math.Abs( luma - Luma( board.R, board.G, board.B ) ) );
		return Worst( LightBoard ) > Worst( OriginalGateSign.Background ) ? LightBoard : OriginalGateSign.Background;
	}

	/// <summary>
	/// The CPU-composed sign of an object (cached per archive and text), or null when the object has no <c>.sgn</c> or no
	/// text. Drawing problems (missing glyphs or fonts) are logged once, not thrown.
	/// </summary>
	public static ObjectSignImages? Get( ObjectCatalogEntry entry )
	{
		var (line1, line2) = Lines( entry );
		if ( line1.Length == 0 && line2.Length == 0 )
			return null;
		var key = $"{entry.FileSystem.GetAbsolutePath( entry.ArchivePath )}\0{line1}\0{line2}";
		lock ( cache )
		{
			if ( cache.TryGetValue( key, out var cached ) )
				return cached;
			ObjectSignImages? images = null;
			try
			{
				var sign = SignTextRenderer.LoadSignFile( entry.ArchivePath, entry.FileSystem );
				if ( sign != null )
				{
					var composed = SignTextRenderer.Shared.ComposeSign( sign, new[] { line1, line2 }, Board( sign ) );
					foreach ( var diagnostic in composed.Diagnostics.Where( diagnostic => !diagnostic.StartsWith( "COMPAT-003", StringComparison.Ordinal ) ).Distinct() )
						Log?.Warning( $"Sign of {entry.ArchiveName}: {diagnostic}" );
					images = new ObjectSignImages( line1, line2, composed.Left, composed.Right, composed.TextPixelCount, composed.Diagnostics );
				}
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or ArgumentException )
			{
				Log?.Warning( $"Sign of {entry.ArchiveName} unavailable: {exception.Message}" );
			}
			cache[key] = images;
			return images;
		}
	}
}
