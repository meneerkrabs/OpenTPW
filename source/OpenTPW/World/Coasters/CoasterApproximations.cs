namespace OpenTPW;

/// <summary>
/// The roller coaster slice's approximation register (docs/COASTERS.md; docs/reverse/COAST-plan.md §10). Each entry
/// is tagged <c>[APPROX:COAST-NNN]</c> at its code site. The IDs below 015 are the unknowns of the plan's §10 table that
/// COAST-R2 left open (§10.1), COAST-015..019 the new unknowns of §10.1, and COAST-022..031 rules OpenTPW had to choose
/// while implementing the plan, COAST-032 the arithmetic precision COAST-V2 found unported, and COAST-026 and COAST-033..035 what the
/// presentation (track mesh, pylons, cars, build cursor) still approximates after it was traced from tp.exe. COAST-001, 002, 003, 005, 006, 009 and 010 were resolved by COAST-R2, COAST-008 by the PPC differential
/// oracle (ORACLE.md F7: the stale term is Fz), COAST-013 (the sickness has no guest consumer: tp.exe 0x4e0560 discards it) by the COAST register round of October 14, and COAST-020 (cost rates) and COAST-021 (force term) by COAST-V; they are traced.
/// </summary>
public static class CoasterApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("COAST-004", "COAST-COLLIDE: the track does not collide with terrain or scenery"),
		("COAST-007", "COAST-WAIVER: the distance waiver for flagged nodes is not applied"),
		("COAST-011", "COAST-CAMERA: the ride view is the front car's pose; no camera mode uses it yet"),
		("COAST-012", "COAST-BREAKDOWN: departure gating while closed and the writers of controller states 1 and 2 (callers of 0x435730) are not traced"),
		("COAST-014", "COAST-WINDOWS: the PC twins match the Mac readings for the rating step and final (X11/X12, the step's term differs), COAST 2-6, the mode and speed setters, the guest predicates, excitement to guests and the debit; the validator, cost, curve sampler and hop are not compared; differences are COAST 6's 0 floor and the x87 table trigonometry of the swing"),
		("COAST-015", "COAST-TAGS: one knot per node at the node position, pylon axis = node forward tangent, roll = node bank; mesh heights = fMinHeight/fMaxHeight, stack offset 0, loop rotate 0.5; the station node does not bank"),
		("COAST-016", "COAST-ACTION129: a click on a station end closes, a further click (or Enter) completes; completion is refused while a node is invalid or the circuit is open, which 0x37ef0 itself does not test"),
		("COAST-017", "COAST-ROLL: sample roll interpolates the knot rolls (0 on pylon-less nodes) instead of the second rail"),
		("COAST-018", "COAST-SWING: the suspended swing follows the PC restatement (0x43c76c..0x43ca45) with libm sine and cosine instead of the runtime table, and x87 intermediates rounded to single or double"),
		("COAST-019", "COAST-CLOSING: the closing pair is sampled to its end like the other pairs; a last sample within half the spacing of the first merges into the wrap"),
		("COAST-022", "COAST-STEP: hops run on the 60 Hz fixed step in train order, cars 7 units apart, the front car is the reference and braking car, no winch-floor halving, every car active"),
		("COAST-023", "COAST-DISPATCH: COAST 6's two upper bounds (definition +0x318 fleet sockets, first train definition +8) are not applied; seats are split evenly over the cars; departures at the release headway when full or after 5 s; arriving trains wait before a busy station"),
		("COAST-024", "COAST-INCOMPLETE: guests do not join or board a coaster that is not complete and closed; the guest predicate 0x441970's +0x140 == 0 and not-the-coaster-being-edited tests are not modelled, nor the queue state 11 thought-13 exit"),
		("COAST-025", "COAST-SAVE: the own-format park save holds the tracks, trains, riders and generator state; the original's coaster chunks between EMAK and SAOC are read structurally (SaveCoasterBlock: header, auxiliary links, sections, links; train payload raw, cell/Info.Id joins only; nonempty TPW runtime import refused) and never written"),
		("COAST-026", "COAST-RENDER: the track mesh is the traced extrusion of the coaster.sam cross sections with the track textures, but it draws both faces, emits per-segment vertices, takes cross-section changes per segment (no dry/wet blend sections), shows an invalid segment as flat red, scrolls textures by fScrollRate and lights self-illuminated textures"),
		("COAST-027", "COAST-RNG: the coaster generator starts from the world seed, not one C-library rand() draw"),
		("COAST-028", "COAST-TOOL: the coaster tool's mouse and key controls and its ghost marker are OpenTPW's; the tool refuses no pylon for money"),
		("COAST-029", "COAST-STATION: the station node stands at '>', the end node at '<' at fInitialPylonLoftHeight"),
		("COAST-030", "COAST-RULES: closing needs two pylons and validates the closing link and arrival turn; edits wait for an empty train; the station node's predecessor, flags 0x10000/0x20000 and object flags 0x600 are not modelled"),
		("COAST-031", "COAST-LEDGER: track money posts as other costs and income and joins the station's total spent (OpenTPW's own); the debit's withdrawals gate, last balance and turn-entered-red stamp come from ParkEconomy's posting"),
		("COAST-032", "COAST-PRECISION: the knot chord, the node tangent's unit vectors, lengths and dot product, and the knot-control norms round every step in single precision instead of the original's fused and double-precision steps"),
		("COAST-033", "COAST-PYLON: pylons are their MD2 posed with the loft and rotate clips (to the node height and knot yaw); the incline and bank clips, the ground tile under the pylon and the pylon mesh heights h0/h1 behind the node heights (COAST-015) are not reproduced, and a node outside the loft range scales or sinks the pylon"),
		("COAST-034", "COAST-CAR: cars are the static rest mesh at the interpolated sample point with the sample direction and bank, spaced by their model extents plus the car type's distance adds; the bend cars, wheel and head clips and the model's +Z forward axis are not traced"),
		("COAST-035", "COAST-GHOST: a track under construction is the curve through the placed nodes and a ghost pylon at the build cursor (tool height and loop mode), drawn like a placed one with a flat red link for an invalid cell; the original's cursor node and invalid presentation are not decoded")
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
