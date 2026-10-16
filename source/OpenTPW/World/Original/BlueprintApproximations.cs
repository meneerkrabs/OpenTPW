namespace OpenTPW;

/// <summary>
/// Approximation register of the build-tool blueprint markers (docs/reverse/BLUEPRINT.md). Each entry is tagged
/// <c>[APPROX:BLUEPRINT-NNN]</c> at its code site; the rest of the markers is traced from the Mac binary.
/// </summary>
public static class BlueprintApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("BLUEPRINT-001", "HEIGHT: marker faces drape over the terrain 1.5 above it and ripple with sin(phase + x + z) as the PC face builder 0x53DDD0 does for a plain cell (SetCell 0x532FC0 passes height 0), but a cell that already holds an object or track is not raised: the original gives a cell of map type 4, 7, 9, 10 or 30 or a second-layer cell 0x4D0AF0 of type 11, 12, 16, 17 or 25 the height 10 x ((int)H + 9) / 10 of the cell height H (0x452AE0: the owner object's .hmp, else the terrain sampler 0x4527F0) and OpenTPW's cell map does not type those cells; the ripple phase steps 0.1 per rendered frame in the original (0x53C762, its frame rate is not known) and per 1/30 s of real time here"),
		("BLUEPRINT-003", "CELLS: a refused placement is one verdict for the whole footprint (all red, no arrows) where the original validates each cell and each access outside cell on its own and turns only the refused ones red; the footprint cells of types 1, 11, 16 and 23 ('@', '+', '#' and track ends), which 0x528A70 stamps through their own branches, are drawn blue; the track symbols (m_direct, m_link, m_end, m_break, m_erase) and the arrow models garrow.MD2 and rarrow.MD2 are not drawn; the validator's answer (0x535670 after 0x5358B5) for a line cell that already holds the line's type is not read, such cells before the refusal stay blue; the Buy Land ghost marks the cells along the rectangle's edges where the original draws a ghost fence model along them (0x53AAC0 with the tool mode | 0x100); m_inout (7, no queue laid) is never chosen because OpenTPW has no auto-laid queue option"),
	};
}
