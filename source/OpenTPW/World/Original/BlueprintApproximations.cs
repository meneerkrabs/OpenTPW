namespace OpenTPW;

/// <summary>
/// Approximation register of the build-tool blueprint markers (docs/reverse/BLUEPRINT.md). Each entry is tagged
/// <c>[APPROX:BLUEPRINT-NNN]</c> at its code site; the rest of the markers is traced from the Mac binary.
/// </summary>
public static class BlueprintApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("BLUEPRINT-001", "HEIGHT: every marker cell is a flat face at its corner height rounded up to a multiple of 10 (plus 1.5); the PC SetCell 0x532FC0 gives a plain cell height 0 and a cell of map type 4, 7, 9, 10 or 30 or a second-layer (track) cell 0x4D0AF0 of type 11, 12, 16, 17 or 25 the value 10 x ((int)H + 9) / 10 of the cell height H (0x452AE0: the owner object's .hmp, else the terrain), and the face builder 0x53DDD0 adds to every vertex of a texture 0-14 face the terrain under it and sin(phase + x + z) (phase + 0.1 per unpaused frame, 0x53C762), so the original's markers drape over the ground and ripple; OpenTPW does neither"),
		("BLUEPRINT-003", "CELLS: a path or queue line marks every cell up to the refused one, existing cells blue; an object's footprint is blue when the build rule allows it and red otherwise; the PC line preview LayLine 0x536100 draws every cell of the line (the ghost SetCell 0x532FC0 returns 1 for allowed and refused cells) and the validator 0x535670 refuses every later cell once its flag 0x816D50 is set (0x5358A0, reset at 0x527C52); the object ghost 0x528A70 draws footprint row 0 with m_front (texture 6, turned to the build direction) and the other cells blue, each red when refused, the entrance's outside cell m_enter (3; m_inout, 7, when no queue is laid) and the exit's m_exit (4), which OpenTPW does not draw; the validator's result for existing path cells is not read"),
	};
}
