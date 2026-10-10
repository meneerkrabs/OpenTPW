namespace OpenTPW;

/// <summary>
/// The guest walk's approximation register (docs/GUESTS.md, "Walking"; docs/reverse/WALK-plan.md §11, §13). Each entry is
/// tagged <c>[APPROX:WALK-NNN]</c> at its code site. The speed model, the steering step, follow_path's arrival and
/// waypoint rules, the stuck reroute rule, the edge branch of avoid_walls and the stand point are traced from the Mac
/// binary (BIN labels at the code sites).
/// </summary>
public static class WalkApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("WALK-001", "nothing raises the speed boost +196; only its traced decay runs"),
		("WALK-002", "avoid_walls models only its edge branch, per axis; the diagonal-corner branches are left out"),
		("WALK-003", "the separation force is zero; guests keep OpenTPW's random sub-cell destination offsets"),
		("WALK-004", "routes are OpenTPW's cell sequences, refilled five waypoints at a time; the stuck reroute prepends the current cell to the rest")
	};
}
