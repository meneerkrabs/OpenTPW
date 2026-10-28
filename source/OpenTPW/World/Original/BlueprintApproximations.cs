namespace OpenTPW;

/// <summary>
/// Approximation register of the build-tool blueprint markers (docs/reverse/BLUEPRINT.md). Each entry is tagged
/// <c>[APPROX:BLUEPRINT-NNN]</c> at its code site; the rest of the markers is traced from the Mac binary.
/// </summary>
public static class BlueprintApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("BLUEPRINT-001", "HEIGHT: marker faces drape over the terrain 1.5 above it and ripple with sin(phase + x + z) as the PC face builder 0x53DDD0 does for a plain cell (SetCell 0x532FC0 passes height 0), but a cell that already holds an object or track is not raised: the original gives a cell of map type 4, 7, 9, 10 or 30 or a second-layer cell 0x4D0AF0 of type 11, 12, 16, 17 or 25 the height 10 x ((int)H + 9) / 10 of the cell height H (0x452AE0: the owner object's .hmp, else the terrain sampler 0x4527F0) and OpenTPW's cell map does not type those cells"),
		("BLUEPRINT-003", "CELLS: refused outside access markers now follow the original entrance-then-exit bounds order for an ordinary in-bounds HasQueue footprint with exactly one entrance and one exit and a known placement refusal; missing/multiple access points, partially off-grid footprints, special/add-on modes, no-queue mode and unproven global/money refusal cases retain the legacy fallback. The all-red footprint itself follows the original hover's whole-placement preflight and force-red flag (0x52390E, 0x52391A, 0x5239EE, 0x53319A), so independent per-cell refusal colouring is not a missing feature of that route; footprint cells of types 1, 3, 11, 16 and 23 ('@', 'Q', '+', '#' and track ends), which 0x528A70 stamps through their own branches, keep simplified markers; the track symbols m_direct, m_link and m_end (m_break and m_erase are drawn over the track edit's pick and cut tiles, with the tile raise, walls, glide and the red faces' blink of 0x5370E0 and 0x53C3F0), and the arrow models garrow.MD2 and rarrow.MD2 are not drawn; the validator's answer (0x535670 after 0x5358B5) for a line cell that already holds the line's type is not read, such cells before the refusal stay blue; the Buy Land ghost marks the cells along the rectangle's edges where the original draws a ghost fence model along them (0x53AAC0 with the tool mode | 0x100); m_inout (7, no queue laid) is never chosen because OpenTPW has no auto-laid queue option"),
	};
}
