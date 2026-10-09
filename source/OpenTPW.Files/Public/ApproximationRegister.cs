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
		new( "COMPAT-002", "Sign text", "Lines are centred horizontally and shrunk uniformly to fit the canvas width minus an 8-texel margin.", "binary text placement or captures of long ride names" ),
		new( "COMPAT-003", "Sign text", ".sgn slot parameters 2..4 are read as the RGB text colour (clamped to 0..1).", "binary use of the slot floats or a capture" ),
		new( "COMPAT-004", "Sign text", "The board behind gate text is a flat dark colour; the .sgn per-slot 32-bit pixel blocks and the BILZ extra image are not decoded.", "decoding of the .sgn remainder blocks" ),
		new( "COMPAT-005", "Sign text", "The .sgn field read as HorizontalScalePercent (85..141) is not applied.", "binary use of the field" ),
		new( "COMPAT-006", "Sign text", "No pair kerning on signs (GDI TextOut/DrawText do not kern by default).", "binary call site of the text output function" ),
		new( "COMPAT-007", "Sign text", "The gate shows the theme name from THEMENAMES.str until a save supplies the player's park name.", "save-format park-name field (economy slice) and a capture" ),
		new( "COMPAT-008", "Sign text", "Gate sign faces are lifted 0.05 engine units along their normal to win the depth test.", "none needed once the full gate model and runtime textures are drawn" ),
		new( "COMPAT-009", "Sign text", "TrueType coverage anti-aliasing (16 sub-scanlines, no hinting, linear coverage) stands in for GDI ANTIALIASED_QUALITY output.", "captures of original sign text" ),
		new( "COMPAT-010", "Graphics presets", "TEXTUREFILTERING 0/1/2 (Point/Bilinear/Trilinear, per the .sam legend) map to Veldrid point / linear-with-point-mip / linear-with-linear-mip samplers, and MIPMAP 0 limits sampling to mip 0; the original Direct3D filter states are not known exactly.", "binary render-state setup or captures at each detail level" ),
		new( "COMPAT-011", "Localization", "A missing localized string falls back to the English table, then to the internal identifier.", "original behaviour for missing strings (the game may show blanks)" ),
		new( "COMPAT-012", "Text input", "Characters that UniToMB.dat cannot represent are replaced by '?' (or dropped when '?' is unmappable).", "binary text-input handling" ),
		new( "COMPAT-013", "Graphics presets", "The default detail preset follows the original's memory and processor thresholds, with the processor clock taken as 450 MHz or faster.", "none for any machine that runs OpenTPW (all exceed 450 MHz)" ),
	};

	/// <summary>Logs every approximation once.</summary>
	public static void LogAll( Action<string> warn )
	{
		ArgumentNullException.ThrowIfNull( warn );
		foreach ( var entry in Compatibility )
			warn( $"[APPROX:{entry.Id}] {entry.Area}: {entry.Assumption} Evidence needed: {entry.EvidenceNeeded}." );
	}
}
