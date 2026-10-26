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

	/// <summary>
	/// The runtime slot flag 0x800 (not a flag of the file): the skirt reader sets it on the texture slots of a ride's edge floor art, which then draw opaque (<see cref="WorldTranslucency.OpaqueSlotFlag"/>).
	/// A slot's <see cref="ModelFile.MaterialData.Flags"/> carries it after <see cref="ObjectFloor"/> applied the type's skirt.
	/// </summary>
	public const uint OpaqueSlotBit = 0x800;

	/// <summary>
	/// OpenTPW's vertex flag for a corner of a self-lit material (material word +0xC bit 0x1, <see cref="ModelBuilder.SelfLitMaterialFlag"/>): tp.exe gives the corners of such a material the constant
	/// colour of the draw (0xFFFFFFFF) instead of the vertex lighting colour, so the texture is drawn full-bright, and <c>test.shader</c> shades the corner with 1. Bit 26 is free like bits 27 to 31.
	/// </summary>
	// [BIN:TPW-EXE:0x580C51 self-lit material] the vertex fill of the mesh draw (0x580530) tests the material record's word +0xC (the file's fourth dword of the 16-byte material record, the one the model reader calls Pad) for bit 0x1: with it clear the corner's diffuse colour is the lit colour (0x574660: the sum of 0x5741B0 clamped to bytes, alpha 0xFF, ANDed with the draw's mask [[0x87A248]+0x2C]) and with it set it is the mask itself (0xFFFFFFFF; 0xFFFF0000 for the one draw that tints), so the texture is not lit at all; the stage state 0x567620 modulates the texture with the diffuse colour
	public const uint SelfLitFlag = 0x0400_0000;

	/// <summary>The vertex flags for a corner of a material: those of its slot (<see cref="VertexFlags(uint)"/>) and <see cref="SelfLitFlag"/> when the material is self-lit.</summary>
	public static uint VertexFlags( ModelFile.MaterialData material ) =>
		VertexFlags( material.Flags ) | ((material.Pad & ModelBuilder.SelfLitMaterialFlag) != 0 ? SelfLitFlag : 0u);

	/// <summary>Whether tp.exe wraps a slot with these flags (else it clamps).</summary>
	public static bool Wraps( uint slotFlags ) => (slotFlags & WrapFlags) != 0;

	/// <summary>The vertex flags for a corner of a slot: the slot's own flags, with <see cref="ClampFlag"/> when the slot does not wrap.</summary>
	public static uint VertexFlags( uint slotFlags )
	{
		// the runtime opaque bit becomes its own vertex flag
		var opaque = (slotFlags & OpaqueSlotBit) != 0 ? WorldTranslucency.OpaqueSlotFlag : 0u;
		slotFlags &= ~OpaqueSlotBit;
		return opaque | (Wraps( slotFlags ) ? slotFlags & ~ClampFlag : slotFlags | ClampFlag);
	}
}
