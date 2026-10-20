namespace OpenTPW;

/// <summary>
/// How an original MD2 texture slot addresses texture coordinates outside 0..1: tp.exe wraps a slot with flag 0x10 or
/// 0x20 and clamps every other slot. The object shaders (test.shader, advisor.shader) read <see cref="ClampFlag"/> from
/// <see cref="Vertex.MatFlags"/>; a vertex without it keeps the material's sampler, so non-MD2 geometry is unaffected.
/// </summary>
// [BIN:TPW-EXE:0x00575E9C texture setup] slot flags 0x10/0x20 become texture bits 0x10000/0x20000; the Direct3D key builders turn either bit into render-key bit 0x400000 (0x581847: test 0x30000, or 0x400000; 0x590C40), and the stage decoder 0x567620 sets D3DTSS_ADDRESS to WRAP for that bit and CLAMP without it (0x5678CF through 0x567A70: state 12, values 1 and 3); the address state covers both axes
// [EXT:md2-clamp-in-shader] a clamped slot samples the material's wrapping sampler at coordinates held half a texel of the coarsest selectable mip inside the edges, with the unclamped derivatives: WebGL allows 16 texture units, so a second sampler per texture is not available, and this is OpenTPW's emulation of CLAMP (exact at the finest mip, the edge texel a little wider in coarser mips)
public static class TextureSlotAddressing
{
	/// <summary>The slot flag bits that make tp.exe wrap the slot's texture (either bit wraps both axes).</summary>
	public const uint WrapFlags = 0x30;

	/// <summary>
	/// OpenTPW's vertex flag for a corner of a clamped slot. The original slot flags never set bit 31 (the corpus has
	/// 0x1..0x73; 0x80 is the procedural-texture bit).
	/// </summary>
	public const uint ClampFlag = 0x8000_0000;

	/// <summary>
	/// OpenTPW's vertex flag for a corner of a path-like material (the entrance plaza and its kerbs, path slabs of objects): under snow test.shader draws it as slush, like the path layer. Bit 30 is free like bit 31
	/// (the corpus flags are 0x1..0x73); set by <see cref="IsPathLikeTexture"/>.
	/// </summary>
	// [EXT:winter-snow-paths] OpenTPW's presentation only
	public const uint SlushFlag = 0x4000_0000;

	/// <summary>Whether a material's texture name marks a walking surface: the shipped names contain path or pav (js_path, ss_PATH, grd_pav4) or are the entrance's centre and kerb strips (grd_ctr1, grd_krb1).</summary>
	public static bool IsPathLikeTexture( string? name ) =>
		name != null && ( name.Contains( "path", StringComparison.OrdinalIgnoreCase ) || name.Contains( "pav", StringComparison.OrdinalIgnoreCase ) || name.StartsWith( "grd_ctr", StringComparison.OrdinalIgnoreCase ) || name.StartsWith( "grd_krb", StringComparison.OrdinalIgnoreCase ) );

	/// <summary>Whether tp.exe wraps a slot with these flags (else it clamps).</summary>
	public static bool Wraps( uint slotFlags ) => (slotFlags & WrapFlags) != 0;

	/// <summary>The vertex flags for a corner of a slot: the slot's own flags, with <see cref="ClampFlag"/> when the slot does not wrap.</summary>
	public static uint VertexFlags( uint slotFlags ) => Wraps( slotFlags ) ? slotFlags & ~ClampFlag : slotFlags | ClampFlag;
}
