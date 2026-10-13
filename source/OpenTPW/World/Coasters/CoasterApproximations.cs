namespace OpenTPW;

/// <summary>
/// The roller coaster slice's approximation register (docs/COASTERS.md; docs/reverse/COAST-plan.md §10). Each entry
/// is tagged <c>[APPROX:COAST-NNN]</c> at its code site. The IDs below 015 are the unknowns of the plan's §10 table that
/// COAST-R2 left open (§10.1), COAST-015..019 the new unknowns of §10.1, and COAST-022..031 rules OpenTPW had to choose
/// while implementing the plan, and COAST-032 the arithmetic precision COAST-V2 found unported. COAST-001, 002, 003, 005, 006, 009 and 010 were resolved by COAST-R2, COAST-008 by the PPC differential
/// oracle (ORACLE.md F7: the stale term is Fz), and COAST-020 (cost rates) and COAST-021 (force term) by COAST-V; they are traced.
/// </summary>
public static class CoasterApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("COAST-004", "COAST-COLLIDE: the track does not collide with terrain or scenery"),
		("COAST-007", "COAST-WAIVER: the distance waiver for flagged nodes is not applied"),
		("COAST-011", "COAST-CAMERA: the ride view is the front car's pose; no camera mode uses it yet"),
		("COAST-012", "COAST-BREAKDOWN: closing stops boarding and departures and moving trains finish to the station; COAST 5 always takes effect; mode 32 has no effect"),
		("COAST-013", "COAST-SICKNESS: the sickness rating has no guest consumer"),
		("COAST-014", "COAST-WINDOWS: every coaster rule is Mac-derived; the PC data only matches its inputs"),
		("COAST-015", "COAST-TAGS: one knot per node at the node position, pylon axis = node forward tangent, roll = node bank; mesh heights = fMinHeight/fMaxHeight, stack offset 0, loop rotate 0.5; the station node does not bank"),
		("COAST-016", "COAST-ACTION129: a click on a station end closes, a further click (or Enter) completes; completion is refused while a node is invalid or the circuit is open, which 0x37ef0 itself does not test"),
		("COAST-017", "COAST-ROLL: sample roll interpolates the knot rolls (0 on pylon-less nodes) instead of the second rail"),
		("COAST-018", "COAST-SWING: the suspended swing is roll = lateral force x fForceMultiplier, clamped to +-pi/2"),
		("COAST-019", "COAST-CLOSING: the closing pair is sampled to its end like the other pairs; a last sample within half the spacing of the first merges into the wrap"),
		("COAST-022", "COAST-STEP: hops run on the 60 Hz fixed step in train order, cars 7 units apart, the front car is the reference and braking car, no winch-floor halving, every car active"),
		("COAST-023", "COAST-DISPATCH: seats = COAST 6 capacity; speed setting stays 60; departures at the release headway when full or after 5 s; arriving trains wait before a busy station"),
		("COAST-024", "COAST-INCOMPLETE: guests do not join or board a coaster that is not complete and closed"),
		("COAST-025", "COAST-SAVE: the own-format park save holds the tracks, trains, riders and generator state; the original's coaster chunks between EMAK and SAOC are neither read nor written"),
		("COAST-026", "COAST-RENDER: tracks, pylons and cars are drawn as flat-coloured boxes, not the coaster.sam cross sections and MD2 meshes"),
		("COAST-027", "COAST-RNG: the coaster generator starts from the world seed, not one C-library rand() draw"),
		("COAST-028", "COAST-TOOL: the coaster tool's mouse and key controls and its ghost marker are OpenTPW's; the tool refuses no pylon for money"),
		("COAST-029", "COAST-STATION: the station node stands at '>', the end node at '<' at fInitialPylonLoftHeight"),
		("COAST-030", "COAST-RULES: closing needs two pylons and validates the closing link and arrival turn; edits wait for an empty train; the station node's predecessor, flags 0x10000/0x20000 and object flags 0x600 are not modelled"),
		("COAST-031", "COAST-LEDGER: track money posts as other costs and income and joins the station's total spent; the original debit's own statistics and its bank +276 gate are not modelled"),
		("COAST-032", "COAST-PRECISION: the knot chord, the node tangent's unit vectors, lengths and dot product, and the knot-control norms round every step in single precision instead of the original's fused and double-precision steps")
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
