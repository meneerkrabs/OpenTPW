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
		new( "COMPAT-001", "Sign text", "Ride signs use the native two 128x128 destinations and a 512-wide text raster reduced to a 256-wide mask; the lower half of the native 512x512 DIB is not reproduced. Orientation (upright, unmirrored) and half order (sign1 left, sign2 right) match a PC v2.0 gameplay video.", "original-platform sign texture capture showing the lower DIB rows" ),
		new( "COMPAT-002", "Sign text", "The LOGFONT width is applied as a horizontal scale that makes the font's average character width (OS/2 xAvgCharWidth) equal lfWidth, with fractional advances; the width search and centring are traced (0x100A9F9C).", "captures of long ride names (the Mac GDI layer's width rounding)" ),
		new( "COMPAT-003", "Sign text", "Ride signs apply stored paint RGBA, source-image material strips, effect coefficients and native integer layer blending, and each paint mask is offset by the record's offsets (the drop shadow of Wilde Aap, the shadow under the gate text); the mask growth and blur (mask word +4, parameter +8, 0xa9178, the outline of Azteekse Tekens), GDI coverage, source-height resampling and float rounding are approximate. A PC v2.0 video confirms the strips and the existence of shadows and outlines, not their shape.", "the mask shaping of 0xa9178 and a capture on a machine where the sign fonts load" ),
		new( "COMPAT-004", "Sign text", "The gate is composed from its decoded board with the park name as two lines (as the original draws them) and the stored paint (a 60 % black shadow under dark relief stone text, as in the video); gate pixels (board placement, relief strength) are compared with a 720p capture only.", "original-platform gate capture at the native resolution" ),
		new( "COMPAT-006", "Sign text", "No pair kerning on signs: tp.exe has no TextOut/DrawText/ExtTextOut imports, its sign text goes through Uniscribe (0x5EAB70) and no kerning API is called.", "whether usp10's natural widths (ScriptStringGetLogicalWidths) already include kerning" ),
		new( "COMPAT-007", "Sign text", "The theme name from THEMENAMES.str is the default park name; the original stores the park name as two editable lines (the rename dialog's two fields) that the gate draws and saves, and OpenTPW has no rename or saved park name.", "the save field of the two park-name lines" ),
		new( "COMPAT-009", "Sign text", "TrueType coverage anti-aliasing (16 sub-scanlines, no hinting, linear coverage) stands in for GDI ANTIALIASED_QUALITY output.", "captures of original sign text" ),
		new( "COMPAT-016", "Sign text", "A ride sign shows the localized type name of its catalogue entry, not the ride instance's own name (confirmed by a PC v2.0 video, also for a prebuilt ride whose instance name differs); for an id outside the table 0x7488D8 OpenTPW shows the .sam name where the original's getter returns an empty name.", "the original's empty-name result for an id outside the table 0x7488D8" ),
		new( "COMPAT-018", "Sign text", "The native wavelet container and board alpha blit are implemented for ride signs; exact wavelet pixel parity, final GPU row orientation and original-platform board appearance remain unverified.", "independent native bitmap pixels and original ride-sign capture" ),
		new( "COMPAT-032", "Sign text", "Transparent sign faces go through the engine's object material: an alpha test (texels under alpha 0.1 are discarded) and an alpha blend, with depth writes and no depth sorting, so thin letters can erode at low mip levels and a blended edge depends on draw order; the original's render state for sign faces is untraced.", "the original's render state for sign faces (blend, alpha test, draw order) and a distant capture" ),
		new( "COMPAT-010", "Graphics presets", "TEXTUREFILTERING 0/1/2 (Point/Bilinear/Trilinear, per the .sam legend) map to Veldrid point / linear-with-point-mip / linear-with-linear-mip samplers, and MIPMAP 0 limits sampling to mip 0; the original Direct3D filter states are not known exactly.", "binary render-state setup or captures at each detail level" ),
		new( "COMPAT-014", "Theme Park Inc textures", "FSH palette record 0x24 stores no alpha, so its 4,308 images decode fully opaque; no colour key or transparent index is applied (two badge textures, each shipped twice, have 32-bit TGA thumbnails whose alpha is 0 everywhere).", "Theme Park Inc's texture upload code or captures of 0x24 textures in the game" ),
		new( "COMPAT-015", "Theme Park Inc textures", "FSH palette record 0x2D (one file, water snowtrac.wad icewall1.fsh) is read as little-endian A1R5G5B5 with bit 15 as opaque/transparent; every entry has bit 15 set and the mean colour matches the sibling icewall textures.", "Theme Park Inc's palette conversion code or a capture of that texture" ),
		new( "COMPAT-019", "Theme Park Inc lake", "Theme Park Inc's lake is mission obstacle 1703 (Data/Objects.txt: New Science Lake Obstacle; levels/science/obstacles/lake.wad, terrain.wad lake.MD2; mission 1 clears it with a gardener's Water Drainage) with the drained state 1704 (New Science Drained Lake Obstacle; dlake.wad, terrain.wad dlake.MD2, the extent of the heightfield holes base.MD2 leaves open). The viewer always draws dlake.MD2 and ignores the obstacle state, so dlake is the drained lake, not a finished one; the choice is data-driven (the TPI exe holds no lake string).", "Theme Park Inc's obstacle code or a capture of the lake in the game" ),
	};

	/// <summary>Logs every approximation once.</summary>
	public static void LogAll( Action<string> warn )
	{
		ArgumentNullException.ThrowIfNull( warn );
		foreach ( var entry in Compatibility )
			warn( $"[APPROX:{entry.Id}] {entry.Area}: {entry.Assumption} Evidence needed: {entry.EvidenceNeeded}." );
	}
}
