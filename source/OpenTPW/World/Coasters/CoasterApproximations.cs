namespace OpenTPW;

/// <summary>
/// The roller coaster slice's approximation register (docs/COASTERS.md; docs/reverse/COAST-plan.md §10). Each entry
/// is tagged <c>[APPROX:COAST-NNN]</c> at its code site. The IDs below 015 are the unknowns of the plan's §10 table that
/// COAST-R2 left open (§10.1), COAST-015..019 the new unknowns of §10.1, and COAST-020..029 rules OpenTPW had to choose
/// while implementing the plan. COAST-001, 002, 003, 005, 006, 009 and 010 were resolved by COAST-R2, and COAST-008 by the PPC differential
/// oracle (ORACLE.md F7: the stale term is Fz); they are traced.
/// </summary>
public static class CoasterApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("COAST-004", "COAST-COLLIDE: the track does not collide with terrain or scenery"),
		("COAST-007", "COAST-WAIVER: the distance waiver for flagged nodes is not applied"),
		("COAST-011", "COAST-CAMERA: the ride view is the front car's pose; no camera mode uses it yet"),
		("COAST-012", "COAST-BREAKDOWN: closing or a breakdown stops boarding and departures; moving trains finish to the station"),
		("COAST-013", "COAST-SICKNESS: the sickness rating has no guest consumer"),
		("COAST-014", "COAST-WINDOWS: every coaster rule is Mac-derived; the PC data only matches its inputs"),
		("COAST-015", "COAST-TAGS: one knot per node at the node position, pylon axis = node forward tangent, roll = node bank; mesh heights = fMinHeight/fMaxHeight, stack offset 0, loop rotate 0.5"),
		("COAST-016", "COAST-ACTION129: a click on a station end closes, a further click (or Enter) completes; completion is refused while a node is invalid"),
		("COAST-017", "COAST-ROLL: sample roll interpolates the knot rolls (0 on pylon-less nodes) instead of the second rail"),
		("COAST-018", "COAST-SWING: the suspended swing is roll = lateral force x fForceMultiplier, clamped to +-pi/2"),
		("COAST-019", "COAST-CLOSING: the closing pair is sampled to its end like the other pairs"),
		("COAST-020", "COAST-RATES: loop and trough pylons use their own length and height rates"),
		("COAST-021", "COAST-FORCE: the force term reads p(i-1) + p(i+1) - 2p(i) over the spacing; gravity and fForceMultiplier take no part"),
		("COAST-022", "COAST-STEP: hops run on the 60 Hz fixed step in train order, cars 7 units apart, braking tested on the front car"),
		("COAST-023", "COAST-DISPATCH: seats = COAST 6 capacity; speed setting stays 60; departures at the release headway when full or after 5 s; arriving trains wait before a busy station"),
		("COAST-024", "COAST-INCOMPLETE: guests do not join or board a coaster that is not complete and closed"),
		("COAST-025", "COAST-SAVE: the park save holds no coaster tracks, trains or coaster generator state"),
		("COAST-026", "COAST-RENDER: tracks, pylons and cars are drawn as flat-coloured boxes, not the coaster.sam cross sections and MD2 meshes"),
		("COAST-027", "COAST-RNG: the coaster generator starts from the world seed, not one C-library rand() draw"),
		("COAST-028", "COAST-TOOL: the coaster tool's mouse and key controls and its ghost marker are OpenTPW's"),
		("COAST-029", "COAST-STATION: the station node stands at '>', the end nodes at '<' (and the next cell) at fInitialPylonLoftHeight")
	};

	private static bool logged;

	public static void LogOnce()
	{
		if ( logged )
			return;
		logged = true;
		foreach ( var (id, rule) in Entries )
			Log?.Warning( $"[APPROX:{id}] {rule}" );
	}
}
