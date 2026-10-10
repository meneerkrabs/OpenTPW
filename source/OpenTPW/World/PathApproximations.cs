namespace OpenTPW;

/// <summary>
/// The path builder's approximation register (docs/PATHS.md; docs/reverse/PATH-plan.md §10). Each entry is
/// tagged <c>[APPROX:PATH-NNN]</c> at its code site and names the plan's unknown in its first word. Everything
/// else in the path builder is traced from the Mac binary (BIN labels at the code sites).
/// </summary>
public static class PathApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("PATH-001", "PATH-ENTER: the path tool starts from a park-view click on an empty owned cell or a path cell (no menu button); ghosts are flat cell markers"),
		("PATH-002", "PATH-UNDO: Backspace pops the last vertex and removes the cells that segment built, without a refund"),
		("PATH-003", "PATH-ENDFLAG: a segment whose last cell lands on an existing path or queue cell ends the tool"),
		("PATH-004", "PATH-CONNECT: a new path cell links to every cardinal path neighbour, on both sides"),
		("PATH-005", "PATH-REMOVE: the remove tool clears a path cell by forced removal (a = b = 0), respecting NoModify"),
		("PATH-006", "PATH-SLOPE: no slope or height limit; the object terrain rule (water, blocked, entrance area, holes) applies"),
		("PATH-007", "PATH-LAND: every in-bounds cell counts as owned land until the save and MAP ownership bits are decoded"),
		("PATH-008", "PATH-CODE8: path over a queue cell is refused (the original's code 8 for a queue end is not traced)"),
		("PATH-009", "PATH-FREE: paths are free only without a park economy (the free byte data:0x7de2d is a park-view flag set in tool modes 4 and 59, not a sandbox switch)"),
		("PATH-010", "PATH-LEDGER: path spending is posted as other costs"),
		("PATH-011", "PATH-CANCEL: Escape or Back inside the path or queue tool ends the tool without writing; the next Escape opens the pause menu"),
		("PATH-012", "PATH-ENDREFUSE: a line refused part-way ends the tool (the original's end flag reads the ghost-clear LayLine, not the commit)"),
		("PATH-013", "PATH-PAUSE: the pause menu blocks path and queue tool clicks; the economy's speed pause does not")
	};
}
