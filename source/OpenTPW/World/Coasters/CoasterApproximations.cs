namespace OpenTPW;

/// <summary>
/// The roller coaster slice's approximation register (docs/COASTERS.md; docs/reverse/COAST-plan.md §10). Each entry
/// is tagged <c>[APPROX:COAST-NNN]</c> at its code site. The IDs below 015 are the unknowns of the plan's §10 table that
/// COAST-R2 left open (§10.1), COAST-015..019 the new unknowns of §10.1, and COAST-022..031 rules OpenTPW had to choose
/// while implementing the plan, COAST-032 the arithmetic precision COAST-V2 found unported, and COAST-026 and COAST-033..035 what the
/// presentation (track mesh, pylons, cars, build cursor) still approximates after it was traced from tp.exe. COAST-001, 002, 003, 005, 006, 009 and 010 were resolved by COAST-R2, COAST-008 by the PPC differential
/// oracle (ORACLE.md F7: the stale term is Fz), COAST-013 (the sickness has no guest consumer: tp.exe 0x4e0560 discards it) by the COAST register round of October 14, COAST-020 (cost rates) and COAST-021 (force term) by COAST-V, and COAST-012 (the controller states and the tool's entry and exit) and COAST-019 (the closing pair of the sampler) by the three-node station round of October 15; they are traced. COAST-004 and COAST-029 were narrowed by it, COAST-014, 015, 024 and 030 lost their station clauses.
/// </summary>
public static class CoasterApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("COAST-004", "COAST-COLLIDE: the self-intersection pass (CoasterCollision: 0x439ab0 → 0x439c40 → 0x43a2b0) runs once over the finished track instead of incrementally per edit, carries the x87 intermediates in double, takes only the default asTrackCollisionPoints (no PC .sam names them; the key layout is not decoded) and does not re-test a node marked by another node's overflowed list; there is no terrain or scenery collision in the original and fCollisionPossibleRadius is the pass's box margin; the consumers of the nodes' collision lists (+0xb0/+0xb4, flag 0x400000 at 0x4376cc) are not traced"),
		("COAST-011", "COAST-CAMERA: the ride view is the front car's pose; no camera mode uses it yet"),
		("COAST-014", "COAST-WINDOWS: the PC twins agree with the Mac readings for the rating step and final, COAST 2-6, the mode and speed setters, the guest predicates, the debit, the validator, the sampler and the hop; differences: COAST 6's 0 floor, the round-to-nearest sine-table indices of the PC (the swing follows them, the Mac-derived tables truncate) and x87 arithmetic; the cost 0x447830 is not compared"),
		("COAST-015", "COAST-TAGS: one knot per node at the node position (S0 and S2 at their StdPylons' knots, S1 on its dummy, the end node on S0's knot), pylon axis = node forward tangent, roll = node bank; the pylon mesh track tags and the incline and bank clips are not read; S0 and the end node are not banked (no previous neighbour; the original's angle for them is untraced) while S2 banks from S1 and the first pylon; h0/h1, the raised fMinHeight, the stack offset and the loop pair are the original's (a pylon model whose track dummy is missing or does not move with the loft clip, the loop pylon of coaster 3180, keeps the settings' heights where the original would stand every node at h0)"),
		("COAST-017", "COAST-ROLL: sample roll interpolates the knot rolls instead of the second rail; at pylon-less knots the PC offset (0, 1, 0) gives roll 0 as here, between knots the second rail needs the pylon tag frames"),
		("COAST-022", "COAST-STEP: hops run on the 60 Hz fixed step in train order (no budget queue), cars 7 units apart with no model extents (car offsets 0x43bc20 not read), the front car is the reference and braking car, every car active, every train has its flag bit 0x2 set (the tick skips the station step of a train without it, 0x435ce5; its writers are not identified), a new train starts at its even share of the lap; the train flags, the winch-floor halving, the station extents, the collision exchange and the headway test are the original's"),
		("COAST-023", "COAST-DISPATCH: seats are split over all cars by the original's recurrence (the original seats only the cars of the train definition's seat type), COAST 6's two upper bounds (definition +0x318 fleet sockets, first train definition +8) and the sizes of the admission and leaving rings are not applied; boarding, unloading, the admission ring and the closing move are the original's"),
		("COAST-024", "COAST-INCOMPLETE: guests do not join or board a coaster that is not complete (the station counts as closed to them) or crosses itself (controller +0x140 == 0, CoasterRide.AcceptsGuests); the not-the-coaster-being-edited test of 0x441970 is not modelled itself (its effect is the coaster tool's: the tool closes the ride and puts the controller in state 2, COAST-012), nor the queue state 11 thought-13 exit"),
		("COAST-026", "COAST-RENDER: the track mesh is the traced extrusion of the coaster.sam cross sections with the track textures, but it draws both faces, emits per-segment vertices, takes cross-section changes per segment (no dry/wet blend sections), shows an invalid segment as flat red and lights self-illuminated textures; scrolling textures shift V by the power factor × elapsed time as the original does, another reader of the stored scroll rate is not excluded; the coaster MD2 models' Add-mode vertex and UV animation (the TrackMaster loader 0x42fad0 keeps header flag 4, add paths of 0x471860) is not modelled (formerly the remainder of RIDES-043)"),
		("COAST-028", "COAST-TOOL: the original drag, the height, rotate and bank sliders and the sub-modes 0x79c624 are not mapped to controls (the tool's own controls are an extension, [EXT:coaster-tool-controls]); the coaster click refuses no pylon for money"),
		("COAST-029", "COAST-STATION: the station is the original's three nodes at the model's dummies (a StdPylon in the cell beyond '<', a pylon-less node on the middle dummy, a StdPylon beyond '>', the end node on the first one's knot); what is not traced: a station whose dummies lie below the StdPylon's h0 (Ghosta Coasta 2.04 against 4, Scatty Batty 3.5 against 5, Escape Velocity 12.04 against 30, Candy Coaster 7.09 against 10) stands on pylons raised to h0 by the loft clamp, so S0 and S2 sit above the middle knot, which the original's pylon pose and terrain height may not do; the middle knot takes fTrackHeightAdjust (the Big Dripper's -1.5) like the pylon knots and the rating pass re-derives the platform's winch marks"),
		("COAST-030", "COAST-RULES: closing needs two pylons and validates the closing link and arrival turn by OpenTPW's rule (the original re-validates the open start of a reopened circuit and closes a fresh one by completion; a click on the station's own cells closes too, an OpenTPW convenience); edits wait for an empty train (OpenTPW's guard); the cut-end flags 0x10000/0x20000 have no case in OpenTPW's tracks"),
		("COAST-032", "COAST-PRECISION: the arithmetic precision differs, the PC twins (chord 0x432840, knot norms and node tangents 0x430ed0/0x431660, the swing 0x43c450) run in x87 with extended intermediates and single or double rounded copies; C# rounds every step to single (the swing and the prism test carry double and round at the original's stores); the FPU control word at run time is not captured"),
		("COAST-033", "COAST-PYLON: pylons are their MD2 posed with the loft and rotate clips (to the node height and knot yaw); the incline and bank clips, the ground tile under the pylon and the rotate clip setup of 0x42eb70 are not reproduced, and a node outside the loft range scales or sinks the pylon"),
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
