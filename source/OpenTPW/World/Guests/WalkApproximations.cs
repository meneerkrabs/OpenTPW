namespace OpenTPW;

/// <summary>
/// The guest walk's approximation register (docs/GUESTS.md, "Walking"; docs/reverse/WALK-plan.md §11, §13). Each entry is
/// tagged <c>[APPROX:WALK-NNN]</c> at its code site. The speed model, the steering step, follow_path's arrival and
/// waypoint rules, the stuck reroute rule, the edge branch of avoid_walls and the stand point are traced from the Mac
/// binary, and so is the separation force (BIN labels at the code sites).
/// </summary>
public static class WalkApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("WALK-001", "nothing raises the speed boost +196; only its traced decay runs"),
		("WALK-002", "avoid_walls models only its edge branch (the first pushing edge of +x, -x, +y, -y); the diagonal-corner branches tried when no edge pushes are left out"),
		("WALK-003", "separation's neighbours are the guests outside rides and shops at their navigation position, in OpenTPW's list order within a cell, not the original's thing positions +4/+6 and per-cell thing lists"),
		("WALK-004", "routes are OpenTPW's cell sequences, refilled five waypoints at a time; the stuck reroute prepends the current cell to the rest"),
		("WALK-005", "a cell is walkable when it is on the current walk (route and extra open cells); the original's edge flags with the +180 mask and its commit callback are not modelled"),
		("WALK-006", "a queue walk may also enter the queue cell ahead of its destination and the one behind its start, and no other cell off its route"),
		("WALK-007", "no map-edit reroute: the block stamp check against +72 and the failure flag +184 are left out"),
		("WALK-008", "guests on the arrival lanes walk off the path grid, with no wall force and every position committed"),
		("WALK-009", "the progress history compares raw remaining distances and skips the first step after route setup, not progress = ONE - FixDiv(remaining, +160) against +172"),
		("WALK-010", "the needs phase that gates the toilet speed term compares the park turn with OpenTPW's guest id, not the original thing id")
	};
}
