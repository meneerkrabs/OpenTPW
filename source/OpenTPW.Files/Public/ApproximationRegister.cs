namespace OpenTPW;

/// <summary>One value or rule that is not taken from original data or hard evidence.</summary>
public sealed record Approximation( string Id, string Area, string Assumption, string EvidenceNeeded );

/// <summary>
/// Register of the compatibility slice's approximations (tagged <c>[APPROX:COMPAT-NNN]</c> at each
/// site, listed in docs/COMPATIBILITY.md "Approximation register"). Logged once at startup so a
/// session log always states what is not original.
/// </summary>
public static class ApproximationRegister
{
	public static readonly IReadOnlyList<Approximation> Compatibility = new Approximation[]
	{
		new( "COMPAT-001", "Sign text", "Sign text canvas is 512x256 texels (two 256x256 halves for sign1/sign2); the original sign texture size is unknown (shared sign1/sign2.wct placeholders are 128x128).", "binary (CreateDIBSection size) or a capture of a sign texture" ),
		new( "COMPAT-002", "Sign text", "The LOGFONT width is applied as a horizontal scale that makes the font's average character width (OS/2 xAvgCharWidth) equal lfWidth, with fractional advances; the width search and centring are traced (0x100A9F9C).", "captures of long ride names (the Mac GDI layer's width rounding)" ),
		new( "COMPAT-003", "Sign text", "Each line is drawn opaque in its .sgn colour block's RGB (traced); the fourth colour byte, colour modes 1 vs 2, the fill bitmaps and the slot effect words are not applied.", "the Bitmap::colourblt body and the sign effect routines" ),
		new( "COMPAT-004", "Sign text", "The board behind gate text is a flat dark colour; the .sgn board image (a wavelet stream in every shipped sign) is read but not decoded or composed.", "the Bitmap::load_wavelet decoder and the board blit" ),
		new( "COMPAT-005", "Sign text", "The .sgn field read as HorizontalScalePercent (85..141) is not applied.", "binary use of the field" ),
		new( "COMPAT-006", "Sign text", "No pair kerning on signs (GDI TextOut/DrawText do not kern by default).", "binary call site of the text output function" ),
		new( "COMPAT-007", "Sign text", "The gate shows the theme name from THEMENAMES.str until a save supplies the player's park name.", "save-format park-name field (economy slice) and a capture" ),
		new( "COMPAT-008", "Sign text", "Gate sign faces are lifted 0.05 engine units along their normal to win the depth test.", "none needed once the full gate model and runtime textures are drawn" ),
		new( "COMPAT-009", "Sign text", "TrueType coverage anti-aliasing (16 sub-scanlines, no hinting, linear coverage) stands in for GDI ANTIALIASED_QUALITY output.", "captures of original sign text" ),
		new( "COMPAT-016", "Sign text", "A ride, shop or sideshow sign shows the object's display name (OBJECT_NAMES entries bound by English name, RIDES-010, else the .sam name) in place of the info record's name string, split at the middle space as the original does.", "the record field that selects the name entries and a capture of a shop or sideshow sign" ),
		new( "COMPAT-017", "Sign text", "Bonus objects' SIGNA/SIGNB sections are used as sign lines 1 and 2.", "the bonus loader's call of the sign builder" ),
		new( "COMPAT-018", "Sign text", "Ride signs sit on a flat board, cream for dark text colours and the gate's dark colour otherwise; the board and fill images are not composed, and the three signs whose text slots have colour mode 0 show the bare board.", "Bitmap::load_wavelet, the board blit and a capture of an original ride sign" ),
		new( "COMPAT-010", "Graphics presets", "TEXTUREFILTERING 0/1/2 (Point/Bilinear/Trilinear, per the .sam legend) map to Veldrid point / linear-with-point-mip / linear-with-linear-mip samplers, and MIPMAP 0 limits sampling to mip 0; the original Direct3D filter states are not known exactly.", "binary render-state setup or captures at each detail level" ),
		new( "COMPAT-011", "Localization", "A missing localized string falls back to the English table, then to the internal identifier.", "original behaviour for missing strings (the game may show blanks)" ),
		new( "COMPAT-012", "Text input", "Characters that UniToMB.dat cannot represent are replaced by '?' (or dropped when '?' is unmappable).", "binary text-input handling" ),
		new( "COMPAT-013", "Graphics presets", "The default detail preset follows the original's memory and processor thresholds, with the processor clock taken as 450 MHz or faster.", "none for any machine that runs OpenTPW (all exceed 450 MHz)" ),
		new( "COMPAT-014", "Theme Park Inc textures", "FSH palette record 0x24 stores no alpha, so its 4,308 images decode fully opaque; no colour key or transparent index is applied (two badge textures, each shipped twice, have 32-bit TGA thumbnails whose alpha is 0 everywhere).", "Theme Park Inc's texture upload code or captures of 0x24 textures in the game" ),
		new( "COMPAT-015", "Theme Park Inc textures", "FSH palette record 0x2D (one file, water snowtrac.wad icewall1.fsh) is read as little-endian A1R5G5B5 with bit 15 as opaque/transparent; every entry has bit 15 set and the mean colour matches the sibling icewall textures.", "Theme Park Inc's palette conversion code or a capture of that texture" ),
	};

	/// <summary>Logs every approximation once.</summary>
	public static void LogAll( Action<string> warn )
	{
		ArgumentNullException.ThrowIfNull( warn );
		foreach ( var entry in Compatibility )
			warn( $"[APPROX:{entry.Id}] {entry.Area}: {entry.Assumption} Evidence needed: {entry.EvidenceNeeded}." );
	}
}
