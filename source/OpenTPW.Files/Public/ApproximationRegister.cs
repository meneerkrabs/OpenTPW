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
		new( "COMPAT-001", "Sign text", "The lower half of the native 512x512 DIB is not reproduced. Orientation (upright, unmirrored) and half order (sign1 left, sign2 right) match a PC v2.0 gameplay video.", "original-platform sign texture capture showing the lower DIB rows" ),
		new( "COMPAT-002", "Sign text", "The LOGFONT width is applied as a horizontal scale that makes the font's average character width (OS/2 xAvgCharWidth) equal lfWidth; the width search and centring are traced (0x100A9F9C). The Mac GDI layer's width rounding is not reproduced.", "captures of long ride names (the Mac GDI layer's width rounding)" ),
		new( "COMPAT-003", "Sign text", "Ride signs apply stored paint RGBA, source-image material strips, effect coefficients and native integer layer blending, and each paint mask is shaped as tp.exe 0x5EBF10 shapes it (offset copy 0x5EA3F0, erosion 0x5EA6D0 or dilation 0x5EA510 by the mask word +4, separable Gaussian 0x5EA8A0 by the parameter +8: Wilde Aap's drop shadow, the gate's shadow, Azteekse Tekens' outline). The upsampling of a relief strip taller than its source (nearest rows) remains approximate.", "the native upsampling branch of the strip resampler and a capture on a machine where the sign fonts load" ),
		new( "COMPAT-004", "Sign text", "The gate is composed from its decoded board with the park name as two lines (as the original draws them) and the stored paint (a 60 % black shadow under dark relief stone text, as in the video); gate pixels (board placement, relief strength) are compared with a 720p capture only.", "original-platform gate capture at the native resolution" ),
		new( "COMPAT-006", "Sign text", "No pair kerning on signs: tp.exe has no TextOut/DrawText/ExtTextOut imports, its sign text goes through Uniscribe (0x5EAB70) and no kerning API is called.", "whether usp10's natural widths (ScriptStringGetLogicalWidths) already include kerning" ),
		new( "COMPAT-007", "Sign text", "The park name is the gate's two sign lines: they start as the gate's OBJECT_NAMES row (0x4DB6F9), the entry price window's rename dialog (0x4990F0, OK 0x4993E0) stores them and redraws the gate, and OpenTPW's world save keeps them. Gate lines of original saves are not imported (their save field is not decoded), and the OK handler's other effects are not built: the copies to the online park record (0x409410, then 0x5B0940/0x5B0970) and the secret name compared with 0x751A34/0x751A38 (\"Kevin the\" / \"Cosmic Cow\", which calls 0x4053C0).", "the gate-line field of original saves, and what 0x5B0940/0x5B0970 and 0x4053C0 drive" ),
		new( "COMPAT-018", "Sign text", "The native wavelet container and board alpha blit are implemented for ride signs; exact wavelet pixel parity, final GPU row orientation and original-platform board appearance remain unverified.", "independent native bitmap pixels and original ride-sign capture" ),
		new( "COMPAT-032", "Sign text", "In the original a textured face's state word follows its texture's alpha type (0x581790 -> 0x590C40, texture flags +8 & 0x300: 0x100 alpha blend with an alpha test at 0xF0/255 (0x2756), 0x200 alpha blend with an alpha test at 0x10/255 (0x556), 0x300 source-alpha-over-one (0x200552), else opaque (0x1256)), all with depth writes (decoder 0x567620), and the batcher 0x581DB0 orders by state and texture; OpenTPW draws the faces with its own alpha test and blend (an extension); which alpha type the generated sign texture carries, and so which state word the original would use, is not read.", "the alpha type of the generated sign texture and a distant capture" ),
		new( "COMPAT-010", "Graphics presets", "tp.exe's option apply 0x44D8F0 sets engine flag 0x20000 for option [0x785898] = 0 and 0x40000 for 1 (neither for 2 and 3), and its state decoder 0x567620 sets stage 0 from the state word: 0x20000 -> MAGFILTER point (else linear), 0x40000 -> MIPFILTER none (else the device's mode 1-3 chosen by caps at 0x566840), 0x400000 -> wrap addressing with a linear MINFILTER (else clamp with a point MINFILTER); that [0x785898] is TEXTUREFILTERING and how the engine flags reach a primitive's state word are not read.", "the option that [0x785898] stores and the path from the engine flags 0x8BCBC8 into the state word" ),
		new( "COMPAT-015", "Theme Park Inc textures", "FSH palette record 0x2D (one file, water snowtrac.wad icewall1.fsh) is read as little-endian A1R5G5B5 with bit 15 as opaque/transparent; every entry has bit 15 set and the mean colour matches the sibling icewall textures. The Theme Park Inc executable's palette converter (Game.exe 0x6B7000) handles only the codes 0x24 and 0x2A (0x6B70FB, 0x6B7108) and returns at 0x6B72D6 with its table unwritten for any other code, so there is no original reading of a 0x2D palette to match.", "whether any data path loads that file and what it then shows" ),
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
