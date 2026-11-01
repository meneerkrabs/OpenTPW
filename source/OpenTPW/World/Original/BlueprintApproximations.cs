namespace OpenTPW;

/// <summary>
/// Approximation register of the build-tool blueprint markers (docs/reverse/BLUEPRINT.md). Each entry is tagged
/// <c>[APPROX:BLUEPRINT-NNN]</c> at its code site; the rest of the markers is traced from the Mac binary.
/// </summary>
public static class BlueprintApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("BLUEPRINT-001", "HEIGHT: ported for a typed TPW world (the ghost SetCell 0x532FC0 raises a cell of map type 4, 7, 9, 10 or 30 or a second-layer cell of record type 0xB, 0xC, 0x10, 0x11 or 0x19 by 10 x ((int)H + 9) / 10 of the cell lookup 0x452AE0, the tile ghost 0x5370E0 by 0x536CA0's last-writer rule, both replayed against tp.exe in the x86 oracle); a world that types no cells (a read-only visit, a harness world) keeps its markers on the terrain ([EXT:untyped-harness-world]), Theme Park Inc's ghost is not read (its executable is not analysed), and the ground of a fence post's own cell has no post mesh to read (TOUR-005)"),
		("BLUEPRINT-003", "CELLS: refused outside access markers follow the original entrance-then-exit bounds order for an ordinary in-bounds HasQueue footprint with exactly one entrance and one exit and a known placement refusal; missing/multiple access points, partially off-grid footprints, special/add-on modes, no-queue mode and unproven global/money refusal cases retain the legacy fallback (the all-red footprint itself follows the original hover's whole-placement preflight and force-red flag, 0x52390E, 0x52391A, 0x5239EE, 0x53319A, so independent per-cell refusal colouring is not a missing feature of that route); the area clear's ghost marks the cells along its rectangle's four edges, all blue, where the original lays the edges with LayLine(0x13a), whose validator answers 0xd and never refuses; m_inout (7, no queue laid) is never chosen because OpenTPW has no auto-laid queue option; a world that types no cells draws the host's own line, Buy Land edge and footprint ([EXT:untyped-harness-world])"),
	};
}
