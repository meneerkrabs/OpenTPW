namespace OpenTPW;

/// <summary>
/// Approximation register of the build-tool blueprint markers (docs/reverse/BLUEPRINT.md). Each entry is tagged
/// <c>[APPROX:BLUEPRINT-NNN]</c> at its code site; the rest of the markers is traced from the Mac binary.
/// </summary>
public static class BlueprintApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("BLUEPRINT-001", "HEIGHT: ported for a typed TPW world (the ghost SetCell 0x532FC0 raises a cell of map type 4, 7, 9, 10 or 30 or a second-layer cell of record type 0xB, 0xC, 0x10, 0x11 or 0x19 by 10 x ((int)H + 9) / 10 of the cell lookup 0x452AE0, the tile ghost 0x5370E0 by 0x536CA0's last-writer rule, both replayed against tp.exe in the x86 oracle); an OpenTPW harness world keeps its markers on the terrain ([EXT:untyped-harness-world]); Theme Park Inc's ghost is not read and its worlds keep the terrain too (its executable is not analysed); and the ground of a fence post's own cell has no post mesh to read (TOUR-005)"),
		("BLUEPRINT-003", "CELLS: refused outside access markers follow the original entrance-then-exit bounds order for an ordinary in-bounds HasQueue footprint with exactly one entrance and one exit and a known placement refusal; missing/multiple access points, partially off-grid footprints, special/add-on modes, no-queue mode and unproven global/money refusal cases retain the legacy fallback (the all-red footprint itself follows the original hover's whole-placement preflight and force-red flag, 0x52390E, 0x52391A, 0x5239EE, 0x53319A, so independent per-cell refusal colouring is not a missing feature of that route); the area clear's ghost marks the cells along its rectangle's four edges, all blue, where the original lays the edges with LayLine(0x13a), whose validator answers 0xd and never refuses; the track-end footprint kind 23 (0x529526): its commit branch 0x5295F7..0x52965C (walker ops 0x87, 0x15, 0x83, 0x84 and 0x81 on the cell beyond) and the link to that cell (0x4D8C20, 0x4D97E0) are carried by ParkObjects' beyond-cell refusal (0x5295A8) and ParkCellMap.WriteFootprint's beyond-cell write but were not replayed against the executable, and its zero-direction branch 0x529581 (shape 'D': no own face, no cell beyond, the sticky word 0x816D50) shows only on a refused ghost because the check refuses 'D'; the terrain flag of the face adder 0x53DDD0 ([EBP+0x1C], passed per caller 0x53E083..0x53E580) is not matched per texture for the m_direct face; m_inout (7, no queue laid) is never chosen because OpenTPW has no auto-laid queue option; an OpenTPW harness world that types no cells draws the host's own line, Buy Land edge and footprint ([EXT:untyped-harness-world]), and a Theme Park Inc world does the same because its cells are not typed"),
	};
}
