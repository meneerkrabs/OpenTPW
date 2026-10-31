namespace OpenTPW;

/// <summary>
/// The train flags of the original that matter to the motion and the station step (train <c>+0</c>, tp.exe
/// <c>0x43ce20</c>, <c>0x43b8b0</c>): braking, in the station, bumped and headway not reached.
/// </summary>
[Flags]
public enum CoasterTrainFlags
{
	None = 0,
	/// <summary>Station braking applies (0x10); presentation only here.</summary>
	Braking = 0x10,
	/// <summary>The train is in the station: it was unloaded on arrival and boards while its cars roll through the window (0x20).</summary>
	InStation = 0x20,
	/// <summary>The train was hit from behind (0x40, set on the train ahead by the collision exchange 0x43cd60): no station braking until it is past the window.</summary>
	Bumped = 0x40,
	/// <summary>The headway to the train ahead has not been reached since the power came back (0x80): the winch floor is halved inside the station.</summary>
	Headway = 0x80
}

/// <summary>
/// The controller state (controller <c>+8</c>, low nibble) the ride's tool and its script move between (<c>0x435730</c>): 1 the track is not finished
/// (no trains), 2 built or taken out of service by the coaster tool (trains circulate empty, no station step), 4 open (the station step unloads and
/// boards) and 8 closed (the station step unloads, nobody boards).
/// </summary>
public enum CoasterControllerState
{
	Incomplete = 1,
	Built = 2,
	Open = 4,
	Closed = 8
}

/// <summary>
/// One train (tp.exe train record, COAST-plan §3.3): its reference car's position along the track in samples, its
/// speed, its flags and the riders of each car. The original never stops a train (<c>0x43ce20</c> has no zero-speed
/// branch); it unloads a train that arrives and seats waiting guests in the cars that roll through the station window.
/// Every car has two rider lists (car <c>+0x28/+0x2c</c>, counts <c>+0x30/+0x34</c>): the riders on the lap, and the
/// guests seated at the station, which become the riders when the train leaves (<c>0x43b8b0</c>).
/// </summary>
public sealed class CoasterTrain
{
	private readonly List<int>[] cars;
	private readonly List<int>[] boarding;

	internal CoasterTrain( int index, int carCount, double wheelAnimationStartSeconds )
	{
		Index = index;
		WheelAnimationStartSeconds = wheelAnimationStartSeconds;
		var count = Math.Max( 1, carCount );
		cars = new List<int>[count];
		boarding = new List<int>[count];
		Seats = new int[count];
		OnWinch = new bool[count];
		for ( var car = 0; car < count; car++ )
		{
			cars[car] = new List<int>();
			boarding[car] = new List<int>();
		}
	}

	public int Index { get; }
	/// <summary>
	/// Per car: whether its last hop found it on a winch section (car flag <c>0x2</c>). Inputs of the train loop sound (<see cref="CoasterTrainSounds"/>), not part of the hash or of a save: a restored train
	/// has none until its first hop, as a loaded original train's flags hold what the last tick before the save left.
	/// </summary>
	// [BIN:TPW-EXE:0x43d1ee winch car flag] the hop sets car flag 0x2 when the sample's section has flag 0x800 (0x43d192..0x43d1ee) and clears it otherwise (0x43d23e); the sound pass counts the cars with it (0x43d73d)
	internal bool[] OnWinch { get; }
	/// <summary>Whether the last hop braked the train (train flag <c>0x10</c>): kept apart from <see cref="Flags"/>, which are hashed and saved.</summary>
	// [BIN:TPW-EXE:0x43d2e9 braking flag] the braking branch of the hop sets flag 0x10 (a car past the braking start and flag 0x40 clear, 0x43d28e..0x43d294); the other branch clears it (0x43d345)
	internal bool BrakingNow { get; set; }
	/// <summary>
	/// Controller time when slot setup started the non-bend cars' looping wheel clips. Presentation only: all cars of a
	/// train start together, independent of their visibility or renderer lifetime. A restored train starts a fresh clip.
	/// </summary>
	// [BIN:TPW-EXE:0x43be10 wheel clip start] slot setup plays id 5, sub 0, flags 1, speed 1.0 on every non-bend car (0x4732a0); the state setter 0x435730 and newly activated rating slots (0x43cad6) call setup
	internal double WheelAnimationStartSeconds { get; set; }
	/// <summary>
	/// The reference car's position in samples from the station node (train <c>+0x74</c>): the middle car, <see cref="CoasterDefinition.ReferenceCar"/>;
	/// every other car stands its offset (<see cref="CoasterDefinition.CarOffsets"/>) over the spacing ahead of it or behind it. In [0, sample count)
	/// after a hop; the placement can leave it up to a train's length beyond the count until the first hop wraps it.
	/// </summary>
	public float Position { get; internal set; }
	/// <summary>World units per second.</summary>
	public float Speed { get; internal set; }
	public CoasterTrainFlags Flags { get; internal set; }
	/// <summary>Cars <c>0..LastBoardingCar</c> are the boarding span of the train in the station (train <c>+0x18/+0x1c</c>).</summary>
	public int FirstBoardingCar { get; internal set; }
	public int LastBoardingCar { get; internal set; }
	/// <summary>Seats of each car (car <c>+0x44</c>).</summary>
	public int[] Seats { get; }
	/// <summary>The train's extent in the station frame (train <c>+0x20</c> front, <c>+0x24</c> rear), derived from the position.</summary>
	internal float Front { get; set; }
	internal float Rear { get; set; }
	/// <summary>The riders of each car on the lap (they get off when the train arrives).</summary>
	public IReadOnlyList<IReadOnlyList<int>> Cars => cars;
	/// <summary>The guests seated in each car at the station (they ride when the train leaves).</summary>
	public IReadOnlyList<IReadOnlyList<int>> BoardingCars => boarding;
	public int Riders => cars.Sum( car => car.Count ) + boarding.Sum( car => car.Count );
	internal List<int>[] CarLists => cars;
	internal List<int>[] BoardingLists => boarding;
}

/// <summary>
/// The coaster controller of one station (COAST-plan §3.3, §5, §9.3, §9.7): its track, the samples of the fitted curve,
/// the rating, the trains and the <c>COAST</c> commands of the station script. Boarding goes through the station's
/// script and <see cref="RideVisitorBridge"/>: the admission calls a guest, the script hands it over with
/// <c>COAST 1 VAR_LETMEON</c> while <c>COAST 2</c> reports room, and takes leaving riders back with
/// <c>COAST 3 VAR_LETMEOFF</c>. Trains move by the half-sample hops of the tick <c>0x40650</c> on the park's fixed step.
/// </summary>
public sealed class CoasterRide
{
	/// <summary>Samples per motion hop.</summary>
	public const float Hop = 0.5f;
	/// <summary>Below this speed every hop decays the speed by <see cref="Decay"/>.</summary>
	public const float DecayBelow = 2f;
	public const float Decay = 0.95f;
	/// <summary>Below this speed a train stops.</summary>
	public const float StopBelow = 0.01f;
	/// <summary>The speed setting the controller constructor stores (controller <c>+48</c>).</summary>
	// [BIN:STP-PPC:0x10038B2C controller constructor] the speed setting (controller +48) starts at 60
	public const int DefaultSpeedSetting = 60;

	private readonly List<CoasterTrain> trains = new();
	private readonly Queue<int> leaving = new();
	// [BIN:TPW-EXE:0x43b0e0 coaster admission] COAST 1 pushes the guest on the admission ring (controller +0xb0); a train in the station seats the guests from it (0x43b600)
	private readonly Queue<int> admission = new();
	private CoasterSample[] samples = Array.Empty<CoasterSample>();
	private IReadOnlyList<CoasterKnot> curveKnots = Array.Empty<CoasterKnot>();
	private (System.Numerics.Vector3 Back, System.Numerics.Vector3 Forward)[] curveControls = Array.Empty<(System.Numerics.Vector3, System.Numerics.Vector3)>();
	private CoasterKnot[] curveSecondRail = Array.Empty<CoasterKnot>();
	private (System.Numerics.Vector3 Back, System.Numerics.Vector3 Forward)[] curveSecondControls = Array.Empty<(System.Numerics.Vector3, System.Numerics.Vector3)>();
	private HashSet<int> winchSections = new();
	/// <summary>The loop sound of the trains (<see cref="CoasterTrainSounds"/>), set by the object's runtime; null in a host without one.</summary>
	internal CoasterTrainSounds? TrainSounds { get; set; }
	/// <summary>The state word of each train slot's loop sound (train <c>+0x38</c>), which outlives the train in the slot as the slot's array does.</summary>
	private uint[] trainSoundStates = Array.Empty<uint>();
	/// <summary>The last braking hop's x⁴ of the current tick (the motion tick's local at <c>ebp-0x38</c>), which the sound pass reads for a braking train.</summary>
	internal float BrakeFactor { get; set; }

	internal uint TrainSoundState( int slot ) => slot < trainSoundStates.Length ? trainSoundStates[slot] : 0;

	private bool[] trainScreamFlags = Array.Empty<bool>();

	/// <summary>Train flag <c>0x100</c> of a slot (set while the train holds its scream, <see cref="CoasterTrainSounds"/>), which the slot keeps across a setup like the sound state.</summary>
	internal bool TrainScreamFlag( int slot ) => slot < trainScreamFlags.Length && trainScreamFlags[slot];

	internal void StoreTrainScreamFlag( int slot, bool value )
	{
		if ( slot >= trainScreamFlags.Length )
			Array.Resize( ref trainScreamFlags, slot + 1 );
		trainScreamFlags[slot] = value;
	}

	internal void StoreTrainSoundState( int slot, uint state )
	{
		if ( slot >= trainSoundStates.Length )
			Array.Resize( ref trainSoundStates, slot + 1 );
		trainSoundStates[slot] = state;
	}

	// The hop queue of MoveTrains, kept between ticks so a tick allocates nothing.
	private float[] hopBudgets = Array.Empty<float>();
	private bool[] hopFriction = Array.Empty<bool>();
	private readonly List<int> hopQueue = new();

	public CoasterRide( CoasterDefinition definition, CoasterWorld world, string name = "" )
	{
		Definition = definition ?? throw new ArgumentNullException( nameof( definition ) );
		World = world ?? throw new ArgumentNullException( nameof( world ) );
		Name = name;
		Track = new CoasterTrack( definition );
		// [BIN:TPW-EXE:0x436a61 controller seats] the constructor runs 0x43ba90(ctl, 0, uiMaxCars, 1): a capacity of 0 takes the train definition's head sockets (+8 = definition +0x2f8), stored at ctl+0xf0 and ctl+0xb8
		SeatsPerTrain = definition.HeadsPerTrain;
		// [BIN:TPW-EXE:0x4369c1 ring sizes] the admission ring (ctl+0xb0) holds definition +0x2f8 guests, the leaving ring (ctl+0xd0) 2 × definition +0x318 (0x43b080)
		AdmissionRingSize = definition.HeadsPerTrain;
		LeavingRingSize = 2 * definition.FleetHeads;
		SpeedCap = SpeedCapFor( definition, SpeedSetting );
		world.Add( this );
		CoasterApproximations.LogOnce();
	}

	public CoasterDefinition Definition { get; }
	public CoasterWorld World { get; }
	public string Name { get; }
	public CoasterTrack Track { get; }
	/// <summary>Map bounds, no-ground cells and ground heights; set by the builder.</summary>
	public CoasterSite Site { get; internal set; } = new( 128, 128 );

	/// <summary>The range of links the open tool marks (the demolish preview, a moved pylon's links); presentation only, not part of the track or its saves.</summary>
	public CoasterRangeMark? RangeMark { get; set; }
	/// <summary>The curve fit (swappable; the traced Hermite/Bézier fit by default).</summary>
	public ICoasterCurve Curve { get; set; } = HermiteCoasterCurve.Instance;
	public IReadOnlyList<CoasterSample> Samples => samples;
	/// <summary>Controller <c>+172</c>: the sample spacing in world units.</summary>
	public float Spacing { get; private set; }
	/// <summary>Controller <c>+268</c>: samples × spacing.</summary>
	public float Length => samples.Length * Spacing;
	public CoasterRatingResult? Rating { get; private set; }
	public IReadOnlyCollection<int> WinchSections => winchSections;
	public IReadOnlyList<CoasterTrain> Trains => trains;
	/// <summary>Controller <c>+308</c>: the sample where station braking starts.</summary>
	public float BrakingStart { get; private set; }
	/// <summary>Set by <c>COAST 8</c>: the station script created its controller.</summary>
	public bool Initialised { get; private set; }
	/// <summary>The controller state: 1 the track is not finished, 2 built or out of service for the coaster tool, 4 open, 8 closed.</summary>
	// [BIN:TPW-EXE:0x435730 state setter] the transitions: 1 → 2 sets the power factor to 1 (outside mode 64), sets every train slot up (0x43be10) and clears the highlight; 1 → 8 and 2 → 8 run the closing move (0x43b390); 2 → 1 removes every train (0x43bf20), clears the shown-trains flag 0x800 and highlights the chain start; 4 → 2 unloads both seat lists of every train (0x43b480 with 0 and 1); 4 → 8 runs the closing move; 8 → 2 unloads both lists and then runs the closing move; every other pair changes nothing
	// [BIN:TPW-EXE:0x447520 coaster tool entry] entering the tool (param 1, no coaster being edited) closes the ride object (0x4df300: "Object %d Closing", the guest being loaded is dropped, event 6) and sets state 2 when the track is complete with its circuit closed (controller +0x3c bit 0 set, bit 1 clear), else state 1; leaving it (param 0) opens the ride object again when the controller's state is 2 (0x4df390; the script's COAST 5 then makes it 4 or 8)
	// [BIN:TPW-EXE:0x435570 completion] completion sets the track's complete flag, appends the end node and sets state 2 (0x435730); close-circuit (0x442c70) and load (0x437b60) set 2 as well, the cut (0x446740) sets 1 from 2
	// [BIN:TPW-EXE:0x43587f admission on 4 → 2] 4 → 2 and 8 → 2 unload both seat lists and then fall through to the closing move 0x43b390 (0x43587f / 0x4358c9 → 0x4358cb), which moves the guests of the admission ring to the leaving ring: no guest waits on a ride that does not run
	// [EXT:coaster-state-clear] the original does nothing on 4 → 1 and 8 → 1 (cases 4 and 8 of 0x435730 have no state 1 branch); the tool always goes through state 2 first, where 2 → 1 removes the trains, but OpenTPW's headless edits (Reopen, Delete, SetStation) also change an open or closed ride to state 1 and then take the trains off with their riders so none is left on a track that is gone; the power factor reset of 1 → 2 is derived from the ride mode here, and the highlight (0x441840) is not drawn
	public CoasterControllerState State { get; private set; } = CoasterControllerState.Incomplete;
	/// <summary>The ride is not open for boarding: <c>COAST 5</c> closed it, the coaster tool took it out of service or its track is not finished (every state but 4).</summary>
	public bool Closed => State != CoasterControllerState.Open;
	/// <summary><c>COAST 4</c> argument (0, 1, 2), mapped to the ride modes 16, 64 and 32 (1 is the breakdown: mode 64, no power).</summary>
	public int BreakdownState { get; private set; }
	// [BIN:STP-PPC:0x1003DDFC coaster state] COAST 4: argument 1 → mode 64 (0x1003DE18 → 0x1003DE50), 2 → mode 32 (0x1003DE44), 0 → mode 16 (0x1003DE38), 3 and above change nothing; each calls 0x10038250
	public int ControllerStateFlag => BreakdownState switch { 0 => 16, 1 => 64, 2 => 32, _ => 0 };
	public bool IsBroken => ControllerStateFlag == 64;
	/// <summary>Controller <c>+44</c>: the power factor P, 0 in mode 64 (both speed floors vanish) and 1 otherwise.</summary>
	// [BIN:STP-PPC:0x10038250 ride mode] changing the mode from 16 or 32 to 64 sets the power factor (+44) to 0; back to 16 or 32 sets it to 1 and flags every train 0x80
	public float PowerFactor => ControllerStateFlag == 64 ? 0f : 1f;
	/// <summary>Controller <c>+48</c>: the speed setting.</summary>
	public int SpeedSetting { get; private set; } = DefaultSpeedSetting;
	/// <summary>Seats of one train (<c>COAST 6</c>).</summary>
	public int SeatsPerTrain { get; private set; }
	/// <summary>Controller <c>+52</c>: the speed cap.</summary>
	public float SpeedCap { get; private set; }
	/// <summary>Net money the track's pylons cost the park so far (charges minus refunds).</summary>
	public long PaidCost { get; internal set; }
	/// <summary>Seconds of fixed simulation steps this controller has run.</summary>
	public double TimeSeconds { get; private set; }
	/// <summary>The guests seated in the cars of the trains (riders on the lap and guests seated at the station).</summary>
	// [BIN:TPW-EXE:0x441970 controller +0x140] the original's controller +0x140 is the self-intersection count, not a rider count (0x439c40, 0x441970), and its pick-up (0x446300) has no rider test: no edit is refused for riders any more
	public int RidersOnBoard => trains.Sum( train => train.Riders );
	/// <summary>Guests handed over by <c>COAST 1</c> and waiting for a train in the station to seat them (the admission ring).</summary>
	public int AdmissionCount => admission.Count;
	/// <summary>Riders taken off a train, a removed track or a closed ride, waiting for <c>COAST 3</c>.</summary>
	public int LeavingCount => leaving.Count;
	/// <summary>The admission ring's size (ctl <c>+0xb0</c>): one train's head sockets (definition <c>+0x2f8</c>).</summary>
	public int AdmissionRingSize { get; }
	/// <summary>The leaving ring's size (ctl <c>+0xd0</c>): twice the fleet's head sockets (2 × definition <c>+0x318</c>).</summary>
	public int LeavingRingSize { get; }
	/// <summary>
	/// The coordinate train 0's last car had when the trains were last taken off (its car <c>+0x40</c>, which the original keeps in the idle train
	/// slot); the next placement starts from it. 0 before any train ran.
	/// </summary>
	public float LeadCarCoordinate { get; private set; }
	/// <summary>Test probe: called after every hop of <see cref="Advance"/> with the train's index, its remaining budget and whether the hop was full.</summary>
	internal Action<int, float, bool>? HopObserved { get; set; }
	/// <summary>The guests waiting for a seat, in arrival order (park-resume capture).</summary>
	internal IReadOnlyCollection<int> AdmissionGuests => admission;
	/// <summary>The riders waiting for <c>COAST 3</c>, in departure order (park-resume capture).</summary>
	internal IReadOnlyCollection<int> LeavingRiders => leaving;
	/// <summary>
	/// The station window in samples, <c>[ctl+0xf8, ctl+0xfc]</c> (<c>0x43b750</c>): the span between the sample coordinates of the station's first
	/// and third node (S0, where the sample list starts, and S2), narrowed toward its middle by fEntranceSizeInterval.
	/// </summary>
	public (float Start, float End) StationWindow { get; private set; }
	/// <summary>The coordinate of every track node's knot on the sample list (<c>node +8</c>): 0 for S0, where the list starts, the window's end for S2, and the end node's for the last.</summary>
	public IReadOnlyList<float> KnotCoordinates { get; private set; } = Array.Empty<float>();
	/// <summary>Self-intersecting places of the track (controller <c>+0x140</c>); the guests refuse the coaster while it is nonzero (0x441970).</summary>
	public int IntersectionCount { get; private set; }
	/// <summary>Per node, the nodes whose links cross the link that starts at it (the original's per-node collision lists, <c>+0xb0</c>).</summary>
	public IReadOnlyList<IReadOnlyList<int>> CollisionLists { get; private set; } = Array.Empty<IReadOnlyList<int>>();
	public bool IsOperational => Track.IsRunnable && samples.Length >= 3;
	/// <summary>The native shared predicate as used by the park-points counts-test, without the host admission sample-safety guard.</summary>
	// [BIN:TPW-EXE:0x441970,0x4dd9ad] existing controller, +0x140 == 0, +0x3c bit 0 set/bit 1 clear, not global 0x790fec; no sample or controller-state test.
	public bool CountsForParkPoints => Track.IsComplete && Track.IsClosed && IntersectionCount == 0 && !BeingEdited;
	/// <summary>
	/// The guest predicate <c>0x441970</c>: the coaster is complete, its track crosses itself nowhere (controller <c>+0x140 == 0</c>) and it is not the
	/// coaster being edited; guests do not join or board a coaster that fails it, and queued guests at an unchanged place leave with thought 13.
	/// </summary>
	// [BIN:TPW-EXE:0x441988 guest predicate] +0x140 must be 0 (the self-intersection count of 0x439c40), +0x3C bit 0 set (complete) and bit 1 clear (circuit not open), and the controller is not the global 0x790fec, the coaster the tool is editing (set by the tool's entry 0x447520, cleared by its exit)
	public bool AcceptsGuests => IsOperational && IntersectionCount == 0 && !BeingEdited;
	/// <summary>The coaster tool is editing this coaster (the original's global <c>0x790fec</c>); set and cleared by the level's tool.</summary>
	public bool BeingEdited { get; internal set; }
	/// <summary>Raised after a track edit that changes the samples (built, reopened, deleted).</summary>
	public event Action<CoasterRide>? TrackChanged;

	/// <summary>The guests' excitement of this ride (<c>0xdff70</c>): 0 without a controller, else trunc(½E + 50).</summary>
	// [BIN:TPW-EXE:0x4e0560 coaster guest rating] COAST-013 retired: the rating getter 0x43e0b0 returns controller +0x100 (sickness) and +0x104 (excitement), the final 0x43df90 writes both, and its only caller 0x4e0560 (the coaster branch of the per-ride guest rating) fetches the sickness and discards it, returning trunc(50 − (−0.5)·E) (constants 0x7005a4/0x7005b4) = Mac 6.1; guest code reaches a controller only through 0x55a4e0, the predicates 0x441910/0x441940/0x441970 and 0x4418f0 (ride-info stats getter 0x43e0e0), so no guest reads the sickness
	public int GuestExcitement => Initialised ? CoasterRating.GuestExcitement( Rating?.Excitement ?? 0 ) : 0;

	/// <summary>The speed cap of a speed setting (<c>0x3fb68</c>): MaxAtMin·(1 − s/100) + MaxAtMax·(s/100).</summary>
	// [BIN:STP-PPC:0x1003FB68 speed cap] controller +52 = fmadds(fMaxSpeedAtMinSetting, 1 − s/100, fMaxSpeedAtMaxSetting × s/100) for the integer setting s (controller +48)
	public static float SpeedCapFor( CoasterDefinition definition, int setting )
	{
		var s = setting / 100f;
		return MathF.FusedMultiplyAdd( definition.MaxSpeedAtMinSetting, 1f - s, definition.MaxSpeedAtMaxSetting * s );
	}

	/// <summary>
	/// A car's pose at a coordinate on the sample list (<c>0x43c0a0</c>): the knot pair whose knot coordinates enclose it, starting from the pair of the
	/// sample below it, and the curve itself at t = (c − c₀)/(c₁ − c₀) on that pair, with the yaw and pitch of the curve's derivative and the slice roll
	/// (the second rail's roll about the tangent plus the tag roll lerp; the sample pose when the curve has not been fitted).
	/// </summary>
	// [BIN:TPW-EXE:0x43c121 car curve point] the car stands on the slice frame 0x43e6b0 of the pair (A, B) with A the knot of sample[ftol(c)] (sample +0x2c) and B its next knot (+0x18), advanced while B's coordinate (+8) is below c, at t = (c − A +8)/(B +8 − A +8): the cubic Bézier point and its tangent's yaw and pitch, not the lerp of two samples
	// [EXT:coaster-closing-pair-car] the closing pair's next knot is the second knot, whose coordinate is taken as one lap on (its own plus the sample count) so the car's parameter runs on; the original's walk compares the second knot's own coordinate
	public CoasterCarPose CarPose( float coordinate )
	{
		var count = curveKnots.Count;
		var coordinates = KnotCoordinates;
		if ( samples.Length == 0 || count < 2 || coordinates.Count < count )
			return CoasterCarRig.PoseAt( samples, coordinate );
		var c = StationCoordinate( coordinate );
		var closing = HermiteCoasterCurve.ClosingKnot( curveKnots );
		int Next( int pair ) => pair == count - 1 ? closing : pair + 1;
		float CoordinateOf( int pair, int next ) => next <= pair ? coordinates[next] + samples.Length : coordinates[next];
		var a = Math.Clamp( samples[Math.Min( (int)c, samples.Length - 1 )].Knot, 0, count - 1 );
		var guard = 0;
		while ( CoordinateOf( a, Next( a ) ) < c && guard++ < count )
			a = Next( a );
		var b = Next( a );
		var span = CoordinateOf( a, b ) - coordinates[a];
		var t = span > 0 ? Math.Clamp( (c - coordinates[a]) / span, 0f, 1f ) : 0f;
		var position = HermiteCoasterCurve.Bezier( curveKnots[a].Position, curveControls[a].Forward, curveControls[b].Back, curveKnots[b].Position, t );
		var tangent = HermiteCoasterCurve.Derivative( curveKnots[a].Position, curveControls[a].Forward, curveControls[b].Back, curveKnots[b].Position, t );
		// [BIN:TPW-EXE:0x43e6b0 car frame] the frame's yaw and pitch are 0x43e590's of the derivative (the PC table, nearest index) and its roll the slice roll: the second rail's roll plus the tag roll lerp, or the swing's sample rolls on a suspended coaster
		var (yaw, pitch) = tangent.LengthSquared() > 0 ? CoasterTrack.PcOrientation( tangent ) : (0f, 0f);
		var second = HermiteCoasterCurve.Bezier( curveSecondRail[a].Position, curveSecondControls[a].Forward, curveSecondControls[b].Back, curveSecondRail[b].Position, t );
		var roll = Definition.IsSuspended
			? HermiteCoasterCurve.FrameRoll( position, second, yaw, pitch, curveKnots[a], curveKnots[b], t, samples, coordinates[a], coordinates[b] )
			: HermiteCoasterCurve.FrameRoll( position, second, yaw, pitch, curveKnots[a], curveKnots[b], t );
		return new CoasterCarPose( position, yaw, pitch, roll );
	}

	/// <summary>
	/// The train the ride view rides: the one with the smallest reference position (controller <c>+0x124</c>), the earliest of equal ones; train 0 without trains.
	/// </summary>
	// [BIN:TPW-EXE:0x43d7dd ride view train] the hop loop (0x43d7dd..0x43d7f3) compares each train's position (+0x74) with the one controller +0x124 names (reset to train 0 at the start of every tick, 0x43ce50) and takes the train when its position is smaller (fcomp, C0 set); 0x436880 returns the camera object of that train's camera car, which the ride camera 0x42a560 takes once, when the ride view opens
	public int RideViewTrain
	{
		get
		{
			var best = 0;
			for ( var index = 1; index < trains.Count; index++ )
			{
				if ( trains[index].Position < trains[best].Position )
					best = index;
			}
			return best;
		}
	}

	/// <summary>
	/// The ride view of a train's camera car (<see cref="CoasterDefinition.RideCameraCar"/>): position from the interpolated sample, forward from the sample direction, roll from the car's frame (<see cref="CarPose"/>).
	/// False without that train.
	/// </summary>
	// [BIN:TPW-EXE:0x00402BF0 options accessor] the vtable 0x6FD630 is the balance-table class of the options (instance 0x785868, built in 0x423BC0; the file reader 0x401650 → 0x4017A0 fills it): its first virtual method returns the entry 0x7442C0 + 0x3C × index (the named table: FIRSTPERSONVIEWDISTANCE is entry 17, COASTERSMOOTHNESS 22), its second the value block at the instance + 8 (0x5B0D60), and the initialiser 0x401030 gives every typed entry a slot of that block in order, so FIRSTPERSONVIEWDISTANCE lives at 0x7858B4 and COASTERSMOOTHNESS at 0x7858C0
	// [BIN:TPW-EXE:0x0044DB60 option readers] the value slots are read through their absolute addresses (the instance pointer is only passed to the loader): 0x7858B4 by the option apply 0x44DB60, which turns it into the fog start and end and the far plane 0x8BD4C0 for every view (UI-107, not a ride-view setting), while no instruction in any section of tp.exe names 0x7858C0 (COASTERSMOOTHNESS) or 0x78596C (Info.FirstPersonSprites, entry 29 of the Info table 0x744B30 at the instance 0x7858F0): the three settings are stored and never read by the ride view
	public bool TryGetRideView( int train, out System.Numerics.Vector3 position, out System.Numerics.Vector3 forward, out float roll )
	{
		position = forward = default;
		roll = 0;
		if ( !IsOperational || train < 0 || train >= trains.Count )
			return false;
		var coordinate = CarCoordinate( trains[train], Definition.RideCameraCar );
		var sample = SampleAt( coordinate );
		// the roll is the car's frame roll (0x43e6b0), the one the car's matrix turns by: the sample roll is the second rail's alone and carries no node bank
		(position, forward, roll) = (sample.Position, sample.Direction, CarPose( coordinate ).Roll);
		return true;
	}

	/// <summary>
	/// Fits the curve through the track's knots, runs the rating pass once (marking winch sections), computes the
	/// braking start, the train count and places the trains evenly (train 0 at the station). Called on completion.
	/// </summary>
	// [BIN:STP-PPC:0x100449F8 rebuild] node layout, tangents, angles, auto-bank and knots (0x10033398), knot links, sampling (0x10044FF4), then the rating pass (0x1003FC18)
	internal void Rebuild()
	{
		var lead = LeadCoordinate();
		PlaceTrains( RateTrack(), lead );
		// A finished track puts the controller in state 2 (0x435570 → 0x435730); a track edited while built, open or closed keeps its state.
		if ( State == CoasterControllerState.Incomplete )
			EnterState( CoasterControllerState.Built );
		TrackChanged?.Invoke( this );
	}

	/// <summary>Curve, rating pass, winch marks, counted sections and braking start for the current speed cap; returns the train count.</summary>
	private int RateTrack()
	{
		var nodes = Track.Nodes;
		var pass = Track.BuildKnotPass( Site );
		var knots = pass.Knots;
		curveKnots = knots;
		curveControls = HermiteCoasterCurve.Controls( knots, Definition.Curviness, closed: true );
		curveSecondRail = HermiteCoasterCurve.SecondRail( knots );
		curveSecondControls = HermiteCoasterCurve.Controls( curveSecondRail, Definition.Curviness, closed: true );
		var sampling = Curve.Sample( knots, Definition.Curviness );
		samples = sampling.Samples;
		Spacing = sampling.Spacing;
		var tail = nodes.Count - 1;
		// The tail's coordinate is the end node's knot's, the last knot (ctl +0xa8, 0x441659).
		var (result, winch) = CoasterRating.RatePass( Definition, samples, Spacing, SpeedCap, tail, sampling.KnotCoordinates.Length == knots.Length && knots.Length > 0 ? sampling.KnotCoordinates[^1] : null );
		winchSections = winch;
		for ( var index = 0; index < nodes.Count; index++ )
		{
			var node = nodes[index];
			// [BIN:TPW-EXE:0x432b50 winch clear] the rebuild clears 0x800 on every node without 0x1000 before the rating pass sets it
			node.Flags = winch.Contains( index ) || (node.Flags & CoasterNodeFlags.WinchKept) != 0 ? node.Flags | CoasterNodeFlags.Winch : node.Flags & ~CoasterNodeFlags.Winch;
		}
		// Controller +264: the sections with both node flags 0x40 and 0x80 (CoasterRating.FinalFor; ORACLE F8).
		var counted = CoasterRating.FinalFor( 0, 0, 0, 0, nodes.Select( node => (uint)node.Flags ), 0, 0, 0, 0 ).Sections;
		Rating = result with { CountedSections = counted };
		var collisions = CoasterCollision.Run( NodesForCollision( nodes, pass, sampling.KnotCoordinates, Definition ), samples );
		IntersectionCount = collisions.Count;
		CollisionLists = collisions.Lists;
		// [BIN:TPW-EXE:0x43c72f braking start] ctl+0x134 = (n − 1) − v_last/spacing with n the sample count; the end node stands on the first station node, so the brake zone is the stretch just before the sample list's end, which is the start of the platform
		BrakingStart = BrakingStartFor( samples.Length, samples[BrakingSpeedSample( samples, tail )].Speed, Spacing );
		// [BIN:TPW-EXE:0x43b750 station window] f8 = (1 − k)·A + k·(A + B)/2 and fc = (1 − k)·B + k·(A + B)/2 with k = definition +0x31c (fEntranceSizeInterval, copied unconditionally) and A, B the sample coordinates of the first and third station node ([[node+0x90]+4]+8): the first one's is 0, the third one's is where the sampler passed S2
		KnotCoordinates = sampling.KnotCoordinates;
		var windowStart = 0f;
		var s2 = CoasterTrack.StationNodeCount - 1;
		var windowEnd = nodes.Count > s2 && sampling.KnotCoordinates.Length > pass.FirstKnot[s2] ? sampling.KnotCoordinates[pass.FirstKnot[s2]] : samples.Length;
		var k = Definition.EntranceSizeInterval;
		var middle = (windowStart + windowEnd) * 0.5f;
		StationWindow = ((1f - k) * windowStart + k * middle, (1f - k) * windowEnd + k * middle);
		return result.Trains;
	}

	/// <summary>
	/// The nodes of <paramref name="chain"/> whose sections the rating pass runs at the winch speed (flag 0x800), plus the nodes that keep the mark (0x1000, the station's first two), over the sample
	/// list of a chain the editor draws; none before the chain has a sample list.
	/// </summary>
	// [BIN:TPW-EXE:0x43c4f0 winch mark] the rebuild 0x43eb70 runs the rating pass 0x43c450 on every chain it draws and on every frame of an edit: a sample whose speed falls back to the floor (fWinchSpeed) marks its node 0x800 (0x43c4f0..0x43c505); the pass has no finished-track gate (it reads the controller's sample list, +0xa4)
	internal static IReadOnlyCollection<int> ChainWinchSections( CoasterDefinition definition, float speedCap, IReadOnlyList<CoasterNode> chain, CoasterSampling? sampling )
	{
		if ( sampling == null || sampling.Samples.Length < 3 )
			return Array.Empty<int>();
		var (_, winch) = CoasterRating.RatePass( definition, sampling.Samples, sampling.Spacing, speedCap, chain.Count - 1, sampling.KnotCoordinates.Length > 0 ? sampling.KnotCoordinates[^1] : null );
		for ( var index = 0; index < chain.Count; index++ )
		{
			if ( (chain[index].Flags & CoasterNodeFlags.WinchKept) != 0 )
				winch.Add( index );
		}
		return winch;
	}

	/// <summary>
	/// The crossing lists of a track still being built. The original's rebuild 0x43eb70 runs the knot pass, the sampler 0x440c10 and the
	/// self-intersection pass 0x439ab0 on every track it draws, so a link of an unfinished chain (the cursor's included) that crosses another is
	/// drawn with the crossing marker while it is built.
	/// </summary>
	// [BIN:TPW-EXE:0x43eb70 rebuild order] the only caller of 0x430ed0 (knot pass), 0x440c10 (sampler) and 0x439ab0 (pass), called from 0x435a30, 0x447d30 and 0x447d60; the sampler treats the chain's last node as the end node whatever the chain is
	// [APPROX:COAST-004] a chain cut open by the demolish button is not sampled here (the original's sampler across the cut ends is not traced) and shows no build-time crossings
	internal static IReadOnlyList<IReadOnlyList<int>> ChainCrossings( ICoasterCurve curve, IReadOnlyList<CoasterNode> chain, CoasterKnotPass pass, CoasterDefinition definition, CoasterSampling? sampling = null )
	{
		if ( chain.Count < 3 || pass.Knots.Length < 3 || chain.Any( node => (node.Flags & (CoasterNodeFlags.CutEnd | CoasterNodeFlags.CutStart)) != 0 ) )
			return Array.Empty<IReadOnlyList<int>>();
		sampling ??= curve.Sample( pass.Knots, definition.Curviness );
		// one sweep, as the original runs per frame: the live marker must stay cheap on a tangled chain (the repeats cost up to 16x)
		return CoasterCollision.Run( NodesForCollision( chain, pass, sampling.KnotCoordinates, definition ), sampling.Samples, maxPasses: 1 ).Lists;
	}

	/// <summary>The nodes as the self-intersection pass sees them: the sample index of each node and the radius of its pylon control.</summary>
	// [BIN:STP-PPC:0x100457A0 node sample index] node +252 = the truncated sampler coordinate when the walk reaches the node: the last sample before its knot, or the sample on the knot when its t is 0 (0x43a060 reads it as the node's first sample)
	// [BIN:TPW-EXE:0x44167f node sample index] the walk stores ftol of the reached knot's coordinate (knot +8) in node +0xfc when that knot is the node's table centre ([node +0x74] +0), or the node has no pylon table or carries 0x2000; the walk starts on the first node's first knot, so a first node whose centre is that knot keeps 0
	internal static List<CoasterCollision.Node> NodesForCollision( IReadOnlyList<CoasterNode> nodes, CoasterKnotPass pass, IReadOnlyList<float> coordinates, CoasterDefinition definition )
	{
		var result = new List<CoasterCollision.Node>( nodes.Count );
		for ( var index = 0; index < nodes.Count; index++ )
		{
			var knot = pass.CentreKnot[index];
			var at = knot == 0 || knot >= coordinates.Count ? 0 : (int)coordinates[knot];
			var node = nodes[index];
			result.Add( new CoasterCollision.Node( at, node.IsPylonless ? null : definition.Pylon( node.LoopPylon ).CollisionPossibleRadius, node.IsPylonless, (node.Flags & CoasterNodeFlags.CutEnd) != 0 ) );
		}
		return result;
	}

	/// <summary>The first sample of the closing pair (the pair that starts at the end node, which carries one sample): the sample count when there is none.</summary>
	internal static int PlatformStartSample( IReadOnlyList<CoasterSample> samples, int tailSection )
	{
		for ( var index = 1; index < samples.Count; index++ )
		{
			if ( samples[index].Section == tailSection )
				return index;
		}
		return samples.Count;
	}

	/// <summary>
	/// The sample whose rated speed sets the braking start: the tail node's sample coordinate (node <c>+252</c>, the
	/// index of the last sample before or on its knot) minus one.
	/// </summary>
	// [BIN:STP-PPC:0x10040070 braking sample] v_last = sample[(tail node +252) − 1].speed with the tail = controller +88 (the station end node); node +252 = trunc of the sampler's coordinate (last sample index + distance since it / spacing) when the walk reaches the node (0x10045790, 0x100457A0), i.e. the last sample before the node's knot
	public static int BrakingSpeedSample( IReadOnlyList<CoasterSample> samples, int tailSection )
	{
		var first = -1;
		for ( var index = 0; index < samples.Count; index++ )
		{
			if ( samples[index].Section == tailSection )
			{
				first = index;
				break;
			}
		}
		if ( first < 0 )
			return samples.Count - 1;
		// The first sample of the tail's pair normally lies past its knot, so the coordinate truncates to the sample before
		// it; a sample exactly on the knot (t = 0) is itself the truncated coordinate (COAST-V2 CV2-4).
		return Math.Max( samples[first].T == 0 ? first - 1 : first - 2, 0 );
	}

	/// <summary>Controller <c>+308</c> = max((n − 1) − v/spacing, 0) in samples, v the rated speed of <see cref="BrakingSpeedSample"/>.</summary>
	// [BIN:STP-PPC:0x10040070 braking start] controller +308 = max((n − 1) − v_last / 3.0, 0) samples
	public static float BrakingStartFor( int sampleCount, float speedBeforeEnd, float spacing ) =>
		MathF.Max( (sampleCount - 1) - speedBeforeEnd / spacing, 0f );

	/// <summary>Station braking (<c>0x40bb8</c>): x = (front − start)/(n − start), 1 outside [0, 1]; v(1 − x⁴) + floor·x⁴.</summary>
	// [BIN:STP-PPC:0x10040BB8 station braking] x = (front − ctl+308)/(n − ctl+308), x = 1 outside [0, 1]; v = fmadds(v, 1 − x⁴, W·x⁴)
	public static float BrakeBlend( float speed, float floor, float front, float start, float total ) => BrakeBlend( speed, floor, front, start, total, out _ );

	/// <summary><see cref="BrakeBlend(float, float, float, float, float)"/> that also gives x⁴, which the motion tick keeps for the train loop sound.</summary>
	// [BIN:TPW-EXE:0x43d2f2 braking factor] the braking branch stores x⁴ (x squared, then squared again) in the tick's local at ebp-0x38 (0x43d2e5..0x43d2f2), where the sound pass reads it (0x43d76a)
	public static float BrakeBlend( float speed, float floor, float front, float start, float total, out float x4 )
	{
		var x = (front - start) / (total - start);
		if ( x < 0 || x > 1 || float.IsNaN( x ) )
			x = 1;
		x4 = x * x * (x * x);
		return MathF.FusedMultiplyAdd( speed, 1f - x4, floor * x4 );
	}

	/// <summary>
	/// The coordinate the placement starts from (<c>0x43c1f0</c>, train <c>+0x30</c>): train 0's last car, on the sample list of the track it last ran
	/// on, or <see cref="LeadCarCoordinate"/> when no train runs.
	/// </summary>
	private float LeadCoordinate() => trains.Count > 0 && samples.Length > 0 ? CarCoordinate( trains[0], trains[0].CarLists.Length - 1 ) : LeadCarCoordinate;

	// [APPROX:COAST-032] the rating pass 0x43c450 with its step and final (0x43dc50, 0x43df90, 0x43dbf0), the suspended swing 0x43c76c and the ride motion are built in single steps from the instruction sequences but no isolated tp.exe run has compared them bit-exactly
	/// <summary>
	/// The trains after a rating pass (<c>0x43c450</c>): the train count changes (<c>0x43ba20</c>: a train past the new count gives up its riders and
	/// its slot), new trains are set up, then every train is placed again: train 0 keeps its last car where it was and each next train's last car stands
	/// where a train leaving the previous one's would be after <c>uiTrainReleaseRate</c> at the rated sample speeds (<c>0x43c1f0</c>); the cars are laid
	/// out from the last car forward, the train's position is its reference car's and its speed the mean rated speed under its cars.
	/// </summary>
	// [BIN:STP-PPC:0x1003F930 train spacing] after the rating pass 0x1003E9EC allocates the trains and 0x1003F930 spaces them along the track
	// [BIN:TPW-EXE:0x43cad6 train slots] trains below the count without bit 0 get bits 0 and 2 and the slot setup 0x43be10 (train flag 0x2, every car flag bit 0, both rider lists empty); trains at or past it lose bits 0 and 0x2 and their cars bit 0 (0x43cb08..0x43cb97), after 0x43ba20 moved their riders, boarding list first (0x43b480 with 0, then 1), to the leaving ring when the state is 4 or 8
	// [BIN:TPW-EXE:0x43cbda train layout] per train: the last car's +0x40 = train +0x30 and each car before it adds (its front extent + the next car's rear extent) / spacing; train +0x74 = the reference car's +0x40, train +0x20/+0x24 from car 0's and the last car's offsets and extents (as 0x43bdb0), train +0x78 = the mean over the cars of the lerp of sample +0x1c at the car's +0x40 (modf, the sample and the next one)
	private void PlaceTrains( int count, float lead )
	{
		for ( var index = count; index < trains.Count; index++ )
		{
			// The original unloads them in the open and closed states only; in state 2 the trains are empty (4 → 2 and 8 → 2 unload them, the tool's entry), and a
			// caller that edits outside the tool never leaves a rider on a train that goes, whatever the state.
			Unload( trains[index], boardingList: true );
			Unload( trains[index], boardingList: false );
		}
		for ( var index = count; index < trains.Count; index++ )
			TrainSounds?.EndLoops( trains[index] );
		if ( trains.Count > count )
			trains.RemoveRange( count, trains.Count - count );
		while ( trains.Count < count )
		{
			var train = new CoasterTrain( trains.Count, Definition.CarCount, TimeSeconds );
			RebuildSeats( train );
			trains.Add( train );
			// [BIN:TPW-EXE:0x43be10 slot setup] a train that comes below the count gets the slot setup, which starts its loop sound (0x4392A0)
			TrainSounds?.SetUp( train );
		}
		if ( count == 0 || samples.Length == 0 )
			return;
		var lastCars = ReleaseSpacing( count, lead );
		for ( var index = 0; index < count; index++ )
			LayOut( trains[index], lastCars[index] );
	}

	/// <summary>
	/// The last car's coordinate of each train (<c>0x43c1f0</c>): train 0's from <paramref name="lead"/> (plus the sample count when negative), every next
	/// one from the previous by the release time: from the previous last car's sample the walk adds spacing / sample speed per sample until it reaches
	/// <c>uiTrainReleaseRate</c>/1000 seconds and takes i − 1 + f with f = (t − R)/(t − t′) over the sample i where it crossed, wrapped into the list.
	/// </summary>
	// [BIN:TPW-EXE:0x43c1f0 release spacing] R = fild(definition +0x2ec as 64-bit) × 0.001 (0x6fe030) stored single; per train after the first: modf of the previous train's +0x30 gives i and f₀, t = (1 − f₀)·spacing / sample[i].+0x1c, t′ = 0; while t < R: i = (i + 1) mod n (ctl+0xa4), t′ = t, t += spacing / sample[i].+0x1c; then f = (t − R)/(t − t′) and train +0x30 = fmod((i − 1)(1 − f) + i·f + n, n) (0x43c343..0x43c39f); train 0 keeps its +0x30
	internal float[] ReleaseSpacing( int count, float lead )
	{
		var n = samples.Length;
		var total = (float)n;
		var spacing = Spacing;
		var release = (float)(Definition.TrainReleaseRate * (double)0.001f);
		var result = new float[count];
		// The original takes a stale lead coordinate as it is; one that lies past this track's samples (a shorter track since) is wrapped into it here.
		var first = lead < 0 ? lead + total : lead;
		result[0] = first >= total || first < 0 ? (float)(((double)first % total + total) % total) : first;
		for ( var index = 1; index < count; index++ )
		{
			var previous = (double)result[index - 1];
			var whole = Math.Truncate( previous );
			var fraction = previous - whole;
			var sample = (int)whole % n;
			var time = (1.0 - fraction) * spacing / samples[sample].Speed;
			var before = 0.0;
			// A lap of samples that adds no time (every rated speed infinite) would never reach the release time: the walk stops after one such lap.
			var stalled = 0;
			while ( time < release && stalled < n )
			{
				sample = (sample + 1) % n;
				before = time;
				time += spacing / (double)samples[sample].Speed;
				stalled = time > before ? 0 : stalled + 1;
			}
			var f = (time - release) / (time - before);
			// A sample rated at speed 0 takes forever (t = ∞, f = NaN in the original's x87 sum); the train then stands on that sample, f's limit.
			if ( !double.IsFinite( f ) || time < release )
				f = 1.0;
			var position = (float)((sample - 1) * (1.0 - f) + sample * f);
			result[index] = (float)(((double)position + total) % total);
		}
		return result;
	}

	/// <summary>Lays a placed train out from its last car's coordinate (<c>0x43cbda..0x43cd47</c>): car coordinates, position, extents and speed.</summary>
	private void LayOut( CoasterTrain train, float lastCar )
	{
		var cars = train.CarLists.Length;
		var coordinates = new float[cars];
		// The sum stays on the x87 stack (0x43cbf1..0x43cc19), each car's coordinate is its single-precision store.
		double coordinate = lastCar;
		for ( var car = cars - 1; car >= 0; car-- )
		{
			coordinates[car] = (float)coordinate;
			if ( car > 0 )
				coordinate = ((double)Definition.CarMetrics( car ).FrontExtent + Definition.CarMetrics( car - 1 ).RearExtent) / Spacing + coordinate;
		}
		train.Position = coordinates[Definition.ReferenceCar];
		UpdateExtents( train );
		var sum = 0f;
		foreach ( var at in coordinates )
		{
			var whole = MathF.Truncate( at );
			var fraction = at - whole;
			var index = (int)whole;
			// A coordinate past slot n reads beyond the original's list; OpenTPW wraps it.
			sum = Slot( index + 1 ).Speed * fraction + (1f - fraction) * Slot( index ).Speed + sum;
		}
		train.Speed = sum / cars;
	}

	/// <summary>Car <paramref name="car"/>'s coordinate on the sample list (car <c>+0x40</c>): the train's position plus the car's offset over the spacing, wrapped.</summary>
	// [BIN:TPW-EXE:0x43d5e0 car coordinates] at the end of the tick car +0x40 = car +0x38 / spacing + train +0x74, fmod by the sample count when not negative, else plus the count
	public float CarCoordinate( CoasterTrain train, int car )
	{
		ArgumentNullException.ThrowIfNull( train );
		return Spacing > 0 ? StationCoordinate( train.Position + Definition.CarOffsets[car] / Spacing ) : 0;
	}

	/// <summary>
	/// The seats of each car (<c>0x43ba90</c>): the capacity split over the seat cars (<see cref="CoasterDefinition.IsSeatCar"/>, the cars of the centre
	/// type) by integer division of what is left by the seat cars still to fill, so the front ones get the smaller share; the other cars get none.
	/// </summary>
	// [BIN:TPW-EXE:0x43bb7b seat split] car +0x44 = remaining / (seat cars − filled) for the cars whose type is the train's seat type, 0 for the others
	// [BIN:TPW-EXE:0x43bb1b seat type] the seat type is the train definition's +0x10, the centre car type (compared with car +4 at 0x43bb6c); the capacity is the argument, or the train definition's +8 (the head sockets of one train) when it is 0 (0x43bb1b..0x43bb29)
	private void RebuildSeats( CoasterTrain train )
	{
		var remaining = (uint)Math.Max( 0, SeatsPerTrain );
		var seatCars = Definition.SeatCarCount;
		var filled = 0;
		for ( var car = 0; car < train.Seats.Length; car++ )
		{
			if ( !Definition.IsSeatCar( car ) || seatCars - filled <= 0 )
			{
				train.Seats[car] = 0;
				continue;
			}
			var share = remaining / (uint)(seatCars - filled);
			train.Seats[car] = (int)share;
			remaining -= share;
			filled++;
		}
	}

	/// <summary>Takes the trains off the track; their riders and the guests waiting for a seat wait for <c>COAST 3</c> to leave.</summary>
	internal void ClearTrains()
	{
		// The idle train slot keeps its cars' coordinates (0x43bf20 clears only the flags), so the next placement starts where train 0's last car stood.
		LeadCarCoordinate = LeadCoordinate();
		foreach ( var train in trains )
		{
			UnloadAll( train );
			TrainSounds?.EndLoops( train );
		}
		MoveAdmissionToLeaving();
		trains.Clear();
		IntersectionCount = 0;
		CollisionLists = Array.Empty<IReadOnlyList<int>>();
		samples = Array.Empty<CoasterSample>();
		Spacing = 0;
		// The original keeps the stale braking start (ctl+0x134) and station window (ctl+0xf8/+0xfc) until the next rating pass rewrites them, before any
		// train runs again; they go with the samples here, so a resumed park (whose save does not hold them) hashes as the one that kept running.
		BrakingStart = 0;
		StationWindow = default;
		Rating = null;
		winchSections = new HashSet<int>();
		TrackChanged?.Invoke( this );
	}

	internal void RaiseTrackChanged() => TrackChanged?.Invoke( this );

	/// <summary>
	/// The sample in slot <paramref name="index"/> of the original's list: slot n holds a copy of the last sample (<c>0x441783</c>), so the point after
	/// the last sample is the last sample itself.
	/// </summary>
	// [BIN:TPW-EXE:0x441783 slot n copy] the sampler copies the last sample into slot n, which the car point of the last sample reads as its next sample (index + 1 at 0x43cf6c for the current point, 0x43d10d for the half-sample point)
	private CoasterSample Slot( int index ) => index < samples.Length ? samples[index] : index == samples.Length ? samples[^1] : samples[index % samples.Length];

	/// <summary>The sample at a position in samples, its position interpolated linearly to the next sample (fused, <c>0x409b0</c>); past the last sample the next one is its slot-n copy.</summary>
	// [BIN:STP-PPC:0x100409B0 car point] a car point between two samples is fmadds(cur, 1 − f, next·f) per component (0x100409B0..0x100409E4)
	public CoasterSample SampleAt( float position )
	{
		if ( samples.Length == 0 )
			return default;
		var count = samples.Length;
		var wrapped = position % count;
		if ( wrapped < 0 )
			wrapped += count;
		var index = Math.Min( (int)wrapped, count - 1 );
		var fraction = wrapped - index;
		var a = samples[index];
		var b = Slot( index + 1 );
		var result = a;
		var rest = 1f - fraction;
		result.Position = new System.Numerics.Vector3( MathF.FusedMultiplyAdd( a.Position.X, rest, b.Position.X * fraction ),
			MathF.FusedMultiplyAdd( a.Position.Y, rest, b.Position.Y * fraction ), MathF.FusedMultiplyAdd( a.Position.Z, rest, b.Position.Z * fraction ) );
		return result;
	}

	// ---- COAST commands ------------------------------------------------------------------------------

	/// <summary>Performs a <c>COAST</c> command of the station script (PPC-rides command table, COAST-plan §5.3).</summary>
	public int Perform( RideEffectCall call ) => Command( call.Argument( 0 ), call.OperandCount > 1 ? call.Argument( 1 ) : 0, call.VM.SpeedBias );

	/// <summary>One <c>COAST</c> command with its resolved parameter; returns the command's result.</summary>
	public int Command( int command, int parameter, int speedBias = RideVM.DefaultSpeedBias )
	{
		switch ( command )
		{
			case 8:
				// [BIN:STP-PPC:0x100B6DF4 COAST bridge] COAST 8 creates or looks up the controller (0x1003DAFC), stores it at script +224 and applies the script speed (0x1003DE68); the parameter is ignored
				Initialised = true;
				ApplyScriptSpeed( speedBias );
				return 0;
			case 1:
				// [BIN:STP-PPC:0x1003DBC0 coaster boarding] COAST 1 resolves the visitor and hands it to the controller's passenger slots
				Board( parameter );
				return 0;
			case 2:
				// [BIN:STP-PPC:0x1003DD14 coaster room] COAST 2 returns the remaining admission room
				return Room;
			case 3:
				// [BIN:STP-PPC:0x1003DD5C coaster departure] COAST 3 extracts one departing visitor (0 when none)
				return TakeLeavingRider();
			case 4:
				// [BIN:STP-PPC:0x1003DDFC coaster state] COAST 4 maps states 0, 1, 2 to the ride modes 16, 64, 32; other values change nothing
				if ( parameter is >= 0 and <= 2 )
					SetBreakdownState( parameter );
				return 0;
			case 5:
				// [BIN:STP-PPC:0x1003DEC8 coaster close] COAST 5 closes (nonzero) or opens the controller
				// [BIN:TPW-EXE:0x43b2f0 coaster close] gated: argument 0 opens (state → 4) only when state & 0xA (built 2 or closed 8), a nonzero argument closes (→ 8) only when state & 4 (open), like the Mac; a ride whose track is not finished (state 1) takes neither. Closing (4 → 8, 0x43b390) moves the admission ring (+0xB0) into the leaving ring (+0xD0) that COAST 3 (0x43b220) pops
				if ( parameter == 0 )
				{
					if ( State is CoasterControllerState.Built or CoasterControllerState.Closed )
						EnterState( CoasterControllerState.Open );
				}
				else if ( State == CoasterControllerState.Open )
					EnterState( CoasterControllerState.Closed );
				return 0;
			case 6:
				// [BIN:STP-PPC:0x1003DF24 coaster capacity] COAST 6 takes MAX with the global value, then MIN with the definition and train bounds
				// [BIN:TPW-EXE:0x43b330 coaster capacity] min(max(request, 0), definition +0x318 (the fleet's head sockets), the first train definition's +8 (one train's head sockets, definition +0x2f8)); the lower bound is the literal 0 where the Mac reading takes a global (documented difference); a value that differs from ctl+0xf0 is rebuilt through 0x43ba90, where 0 stands for the train's head sockets
				SetCapacity( parameter );
				return 0;
			default:
				return 0;
		}
	}

	/// <summary>
	/// The ride-script loop's speed setter (<c>0x43b2b0</c>): a changed setting becomes the controller's speed setting and the
	/// speed cap lerp(maxAtMin, maxAtMax, s/100) (<c>0x43c3d0</c>), unless a layout replay set the speed (<see cref="SpeedSetByReplay"/>) and no
	/// layout replay runs now.
	/// </summary>
	// [BIN:TPW-EXE:0x43b2b0 coaster speed setter] COAST 8 (0x554ab4, after 0x43b050 stored the slot at script +0xE0) and the loop 0x5516b0 after every slice its cadence selects (0x5517ea; OriginalObjectEffects.AfterSlice) call it with the signed 16-bit script bias at script +0xC0 (50 at creation, 0x558c3c); applied via 0x43c3d0 (setting +0x30, cap +0x34 = fmadds-style lerp of definition +0x4C and +0x50 by s·0.01, dirty flags 0x1001, the per-node pass 0x43afa0 on flag 8) when it differs from the setting (0x43b2c6) and either a layout replay runs (0x785a2c, the replay object 0x785a08 +0x24, set by 0x404140 and cleared at the replay's end in 0x4041d0) or controller +0 bit 0x8000 is clear (0x43b2d6); since the script's bias (RideVM.SpeedBias, the ride's InitSpeed) is written at ride init and when an upgrade completes (RIDES-033) it changes between passes only then, and the setter takes the new bias after the script's next slice
	internal void ApplyScriptSpeed( int bias ) => ApplySpeed( bias, LayoutReplayRuns );

	/// <summary>
	/// Whether the original's layout replay (the action replayer <c>0x785a08</c>, which rebuilds a downloaded online park from its recorded actions) is running.
	/// OpenTPW has no layout replay, so it never runs.
	/// </summary>
	internal const bool LayoutReplayRuns = false;

	/// <summary>
	/// Controller bit <c>0x8000</c>: a layout replay set this coaster's speed (<see cref="ApplyReplayedSpeed"/>); outside a replay the script's speed word
	/// no longer changes it. Never set in OpenTPW, which runs no layout replay.
	/// </summary>
	// [BIN:TPW-EXE:0x447e30 replayed coaster speed] the only writer of controller +0 bit 0x8000: the replayer 0x4045b0 (one recorded action per call, from 0x4041d0) calls it for action 0x45 (69) with the slot and the speed; it calls 0x43b2b0 and then ORs 0x8000 into the controller word (0x790bd0[slot]); no code of the coaster module (0x42f000..0x449000) clears it, and the saver 0x437300 does not write it (ctl+0 bits 0x20..0x800 only), so a loaded controller has it clear
	public bool SpeedSetByReplay { get; private set; }

	/// <summary>The replayed coaster speed action (<c>0x447e30</c>): the speed setter (<see cref="ApplySpeed"/>), then bit <c>0x8000</c>.</summary>
	internal void ApplyReplayedSpeed( int speed, bool layoutReplayRuns )
	{
		ApplySpeed( speed, layoutReplayRuns );
		SpeedSetByReplay = true;
	}

	/// <summary>The speed setter <c>0x43b2b0</c> with the layout replay's state.</summary>
	internal void ApplySpeed( int bias, bool layoutReplayRuns )
	{
		if ( bias == SpeedSetting )
			return;
		if ( !layoutReplayRuns && SpeedSetByReplay )
			return;
		SpeedSetting = bias;
		SpeedCap = SpeedCapFor( Definition, bias );
		// 0x43c3d0 marks the controller dirty (0x1001) and re-runs the per-node pass: a runnable track is re-rated at the
		// new cap, as a park resume would derive it, and the rating pass places the trains again (0x43c450): the trains
		// below the new count keep their riders, the ones past it give theirs up (they wait for COAST 3).
		if ( !Track.IsRunnable )
			return;
		var lead = LeadCoordinate();
		PlaceTrains( RateTrack(), lead );
		TrackChanged?.Invoke( this );
	}

	/// <summary>
	/// COAST 6's capacity (<c>0x43b330</c>): the request bounded below by 0 and above by the fleet's and one train's head sockets; a changed value
	/// re-splits the seats of every train (<c>0x43ba90</c>), and 0 stands for one train's head sockets there.
	/// </summary>
	private void SetCapacity( int request )
	{
		var capacity = Math.Min( Math.Min( Math.Max( request, 0 ), Definition.FleetHeads ), Definition.HeadsPerTrain );
		if ( capacity == SeatsPerTrain )
			return;
		SeatsPerTrain = capacity == 0 ? Definition.HeadsPerTrain : capacity;
		foreach ( var train in trains )
			RebuildSeats( train );
	}

	/// <summary>The capacity <see cref="SetCapacity"/> would store for a request.</summary>
	private int BoundedCapacity( int request )
	{
		var capacity = Math.Min( Math.Min( Math.Max( request, 0 ), Definition.FleetHeads ), Definition.HeadsPerTrain );
		return capacity == 0 ? Definition.HeadsPerTrain : capacity;
	}

	/// <summary>The room COAST 2 reports: the capacity COAST 6 stored minus the guests waiting for a seat on the admission ring.</summary>
	// [BIN:TPW-EXE:0x43b1f0 coaster room] COAST 2 returns max(0, ctl+0xb8 − ctl+0xc0 − ctl+0xbc): the capacity COAST 6 stored (0x43bbd1: ctl+0xf0 → +0xb8), minus the ring's second counter (+0xc0, only ever 0), minus the guests on the admission ring (+0xbc); no state, mode or train enters it, so the room is the capacity of one train whether or not a train is in the station
	public int Room => Math.Max( 0, SeatsPerTrain - admission.Count );

	/// <summary>
	/// COAST 1: the guest the script handed over goes on the admission ring (<c>0x43b0e0</c>); a train in the station seats it
	/// while its cars roll through the window. A guest handed over to a full ring goes back through <c>COAST 3</c>.
	/// </summary>
	// [BIN:STP-PPC:0x1003E2F8 boarding] the pending ring's read cursor (+196) is popped and the visitor is placed in a randomly chosen car (s·214013 + 2531011)
	// [BIN:TPW-EXE:0x43b0e0 coaster boarding] COAST 1 pushes the guest on the ring at ctl+0xb0 when the ring's two counters (+0xc0, +0xbc) are below its size (+0xb0 = definition +0x2f8); nobody is seated here, and the room COAST 2 reports is not tested
	// [EXT:coaster-full-ring] the original drops a guest handed over to a full ring (it stays on the ride with nobody to let it off); OpenTPW hands it back through COAST 3 so no guest is stranded
	internal void Board( int guest )
	{
		if ( guest <= 0 )
			return;
		if ( admission.Count >= AdmissionRingSize )
		{
			leaving.Enqueue( guest );
			return;
		}
		admission.Enqueue( guest );
	}

	internal int TakeLeavingRider() => leaving.Count > 0 ? leaving.Dequeue() : 0;

	/// <summary>The admission ring into the leaving ring (<c>0x43b390</c>), when the ride closes and when the track is taken away.</summary>
	// [BIN:TPW-EXE:0x43b390 coaster close] moves min(leaving ring room, admission count) guests from the admission ring (+0xb0) to the leaving ring (+0xd0, 2 × definition +0x318 guests), oldest first
	// [EXT:coaster-full-ring] the guests that do not fit stay on the admission ring in the original; OpenTPW moves them all (the ring only fills past one train's guests when a whole fleet unloads at once, which the original's own paths never do)
	private void MoveAdmissionToLeaving()
	{
		while ( admission.Count > 0 )
			leaving.Enqueue( admission.Dequeue() );
	}

	/// <summary>COAST 4: the ride mode changes; leaving mode 64 (breakdown) flags every train for the headway rule and clears its bumped flag (<c>0x435730</c>).</summary>
	// [BIN:TPW-EXE:0x435944 mode setter] from mode 64 to 16 or 32 the power factor becomes 1 and every train gets flag 0x80 and loses 0x40 (0x435947..0x435950); entering mode 64 sets the power factor to 0 (0x4359d5)
	private void SetBreakdownState( int state )
	{
		var wasBroken = BreakdownState == 1;
		BreakdownState = state;
		if ( !wasBroken || state == 1 )
			return;
		foreach ( var train in trains )
			train.Flags = (train.Flags & ~CoasterTrainFlags.Bumped) | CoasterTrainFlags.Headway;
	}

	// ---- Station -------------------------------------------------------------------------------------

	/// <summary>Moves the riders of one list of every car to the leaving ring, car by car (<c>0x43b480</c>).</summary>
	private void Unload( CoasterTrain train, bool boardingList )
	{
		foreach ( var car in boardingList ? train.BoardingLists : train.CarLists )
		{
			foreach ( var guest in car )
				leaving.Enqueue( guest );
			car.Clear();
		}
	}

	// [BIN:TPW-EXE:0x43585a unload order] 4 → 2 and 8 → 2 unload each train's boarding list (0x43b480 with 0: car +0x2c) before its lap list (with 1: car +0x28)
	private void UnloadAll( CoasterTrain train )
	{
		Unload( train, boardingList: true );
		Unload( train, boardingList: false );
		train.Flags &= ~CoasterTrainFlags.InStation;
		train.FirstBoardingCar = train.LastBoardingCar = 0;
	}

	/// <summary>A position's coordinate on the sample list, in [0, count): the sample list starts at the platform's first node.</summary>
	private float StationCoordinate( float position )
	{
		var count = samples.Length;
		if ( count == 0 )
			return 0;
		var coordinate = position % count;
		return coordinate < 0 ? coordinate + count : coordinate;
	}

	/// <summary>The train's front and rear in the station frame (train <c>+0x20</c>, <c>+0x24</c>).</summary>
	// [BIN:TPW-EXE:0x43d50b train extents] train +0x20 = (car 0's offset / spacing + position) + its front extent / spacing; +0x24 = (the last car's offset / spacing + position) − its rear extent / spacing (car 0 is train +8, which 0x43ba90 sets to 0; 0x43bdb0 and the placement 0x43cc45 compute the same); neither is wrapped, and the hop computes them before it wraps the position (0x43d54f..0x43d572)
	private void UpdateExtents( CoasterTrain train )
	{
		if ( samples.Length == 0 || Spacing <= 0 )
		{
			train.Front = train.Rear = 0;
			return;
		}
		var last = train.CarLists.Length - 1;
		double spacing = Spacing;
		train.Front = (float)(Definition.CarOffsets[0] / spacing + train.Position + Definition.CarMetrics( 0 ).FrontExtent / spacing);
		train.Rear = (float)(Definition.CarOffsets[last] / spacing + train.Position - Definition.CarMetrics( last ).RearExtent / spacing);
	}

	/// <summary>
	/// The station step of one train (<c>0x43b8b0</c>), run after the motion in the open and closed states outside mode 64. A train
	/// whose span overlaps the window unloads its riders when it arrives (any state) and, while the ride is open, seats the
	/// waiting guests in the cars that are inside the window; when the span leaves the window the remaining seats are filled, the
	/// two rider lists swap and the flag clears.
	/// </summary>
	// [BIN:TPW-EXE:0x43b8b0 coaster station] in the station when train +0x20 ≥ ctl+0xf8 and train +0x24 ≤ ctl+0xfc; arrival (flag 0x20 clear): 0x43b480(…, 1) unloads the lap's riders; state 4: 0x43b7e0 finds the cars inside the window, the span grows from car 0 to the last car seen, 0x43b600 seats the ring's guests; leaving (flag 0x20 set, out of the window): state 4 seats the free seats of every car from train +0x18, the two lists swap, +0x18/+0x1c = 0, flag 0x20 clears
	private void StationStep( CoasterTrain train )
	{
		var (start, end) = StationWindow;
		var inStation = train.Front >= start && train.Rear <= end;
		var open = !Closed;
		var cars = train.CarLists.Length;
		if ( inStation )
		{
			if ( (train.Flags & CoasterTrainFlags.InStation) == 0 )
				Unload( train, boardingList: false );
			if ( open )
			{
				var first = train.FirstBoardingCar;
				var last = train.LastBoardingCar;
				// The original reads the first and last car index 0x43b7e0 returns even when no car is inside (a train whose span overlaps a window that falls
				// between two of its cars; the index is then garbage); OpenTPW seats nobody in that step.
				if ( CarsInWindow( train, out var newFirst, out var newLast ) )
				{
					first = Math.Min( newFirst, first );
					last = Math.Max( newLast, last );
					// [BIN:TPW-EXE:0x43b9fe seating span] the seating call gets the span grown from the stored one (min/max, 0x43b9ae..0x43b9cb), but +0x18 and +0x1c keep the raw first and last of 0x43b7e0 (0x43ba09, 0x43ba0c)
					SeatGuests( train, first, last - first + 1 );
					train.FirstBoardingCar = newFirst;
					train.LastBoardingCar = newLast;
				}
			}
			train.Flags |= CoasterTrainFlags.InStation;
		}
		else if ( (train.Flags & CoasterTrainFlags.InStation) != 0 )
		{
			if ( open )
				SeatGuests( train, train.FirstBoardingCar, cars - train.FirstBoardingCar );
			for ( var car = 0; car < cars; car++ )
			{
				// A rider still on the lap list would be lost by the pointer swap; a train is unloaded on arrival, so this only guards a restored train.
				foreach ( var guest in train.CarLists[car] )
					leaving.Enqueue( guest );
				train.CarLists[car].Clear();
				train.CarLists[car].AddRange( train.BoardingLists[car] );
				train.BoardingLists[car].Clear();
			}
			train.FirstBoardingCar = train.LastBoardingCar = 0;
			train.Flags &= ~CoasterTrainFlags.InStation;
		}
	}

	/// <summary>The first and last car whose position is inside the station window (<c>0x43b7e0</c>); false when no car is.</summary>
	// [BIN:TPW-EXE:0x43b7e0 cars in window] from car train +8 (0): front extent / spacing + car +0x40 ≥ ctl+0xf8 and car +0x40 − rear extent / spacing ≤ ctl+0xfc; the first and the last such car
	private bool CarsInWindow( CoasterTrain train, out int first, out int last )
	{
		var (start, end) = StationWindow;
		first = last = 0;
		var found = false;
		for ( var car = 0; car < train.CarLists.Length; car++ )
		{
			var coordinate = CarCoordinate( train, car );
			var metrics = Definition.CarMetrics( car );
			if ( !(start <= metrics.FrontExtent / Spacing + coordinate) || !(coordinate - metrics.RearExtent / Spacing <= end) )
				continue;
			if ( !found )
				first = car;
			last = car;
			found = true;
		}
		return found;
	}

	/// <summary>
	/// Seats guests from the admission ring (<c>0x43b600</c>): while the span's cars have a free seat and the ring a guest, the guest
	/// goes to car <c>(s &gt;&gt; 16) % count + first</c> of the generator, or the first car after it with room.
	/// </summary>
	// [BIN:TPW-EXE:0x43b600 seat guests] train +0x14 = the free seats of the span's cars (car +0x44 − car +0x34), counted by the caller; each guest popped from the ring (+0xc4) draws s = s·0x343fd + 0x269ec3 (no 0x7fff mask) and takes (s >> 16) % count + first, then the first car from there, wrapping inside the span, whose count (+0x34) is below its seats (+0x44)
	private void SeatGuests( CoasterTrain train, int first, int count )
	{
		if ( count <= 0 )
			return;
		var free = 0;
		for ( var car = first; car < first + count; car++ )
			free += Math.Max( 0, train.Seats[car] - train.BoardingLists[car].Count );
		while ( free > 0 && admission.Count > 0 )
		{
			var guest = admission.Dequeue();
			var car = first + World.Random.Next() % count;
			var seated = false;
			for ( var step = 0; step < count && !seated; step++ )
			{
				var list = train.BoardingLists[car];
				if ( list.Count < train.Seats[car] )
				{
					list.Add( guest );
					seated = true;
					// [BIN:TPW-EXE:0x0043B70D seat head] the seated guest's head is hung on the car's node (0x44B410, no node test)
					World.Scripts?.HeadHung( guest );
				}
				else if ( ++car >= first + count )
					car = first;
			}
			if ( !seated )
				leaving.Enqueue( guest );
			free--;
		}
	}

	// ---- Motion -------------------------------------------------------------------------------------

	/// <summary>
	/// One fixed simulation step (<c>0x435a30</c>): every train's half-sample hops (<c>0x43ce20</c>) within the step's time
	/// budget, the headway test, then the station step of each train in the open and closed states outside mode 64. Trains
	/// never stop: they circulate whether or not the ride is open.
	/// </summary>
	// [BIN:TPW-EXE:0x435ac9 controller tick] the motion tick runs when state & 0xE (2 built, 4 open, 8 closed); the station step (0x43b8b0, then 0x439570) only when state & 0xC and mode is not 0x40 (0x435cd1, 0x435cd5); the motion tick has no zero-speed branch, so a train in the station keeps the winch floor's speed
	// [BIN:TPW-EXE:0x43ce40 frame time] the frame's time is the elapsed milliseconds of the cached animation clock (0x4709e0) since ctl+0x120 × 0.001, written to every train's +0x7c
	// [EXT:coaster-fixed-step] the motion tick's frame time is the scaled game clock since the last motion tick, as in the original, with the clock counted in fixed steps (MotionClockLagSeconds: a stretch in state 1 is caught up in one tick, as 0x43ce20 does); what stays OpenTPW's own is only the frame: the original runs one controller tick per rendered frame with that frame's whole milliseconds, OpenTPW one per 60 Hz simulation tick of 1/60 s (docs/DETERMINISM.md: no simulation reads the wall clock), so a frame longer or shorter than a tick hops in other slices, and while the game clock stands still (the game pause 0x4092a0, which freezes 0x785970) the original still runs a zero-time motion tick and the station step on every frame (the call 0x54fa8f to 0x435a30 is gated only by the test at 0x54f8e0: 0x7a1a14 bit 3, which the main window procedure 0x46b600 sets and clears with the application's activation, or 0x7a1a8c bit 0 clear; a zero-time tick caps every train's speed, decays it ×0.95 below 2.0 and stops it below 0.01), where OpenTPW, whose paused park runs no tick, runs none; a sample read past the original's slot n (a stale lead coordinate beyond a shorter track's samples, a car laid out past the end of the list) wraps instead of reading past the list, and the hop time 0/0 of a hop of no length at no speed (impossible with samples 3 units apart), which the original's x87 compare takes as a full hop, is a partial one; the release walk of the train placement (0x43c1f0) over samples rated at speed 0 or infinity, where the original's sum turns NaN or never reaches the release time, stops after a lap that adds no time and takes the crossing sample itself
	public void Advance( double deltaSeconds )
	{
		if ( !(deltaSeconds > 0) )
			return;
		TimeSeconds += deltaSeconds;
		MotionClockLagSeconds += deltaSeconds;
		Update();
	}

	/// <summary>
	/// The coaster update of the original's first frame after a load (<c>0x435a30</c>): the save loader restarts the main loop's substep clock (<c>0x415140</c>), so that
	/// frame runs no substep and no script pass before it; its rebuild keeps the saved trains (<see cref="ImportOriginal"/>) and its motion tick is the controller's
	/// first, which takes no time (<see cref="MotionClockStarted"/>). The park import runs it once per imported coaster, before the station script's first pass.
	/// </summary>
	internal void UpdateFirstFrameAfterLoad() => Update();

	/// <summary>Whether the controller's motion tick has run (controller flag <c>0x10</c>); the first one after the controller was made, built or loaded takes no time.</summary>
	// [BIN:TPW-EXE:0x43ce85 motion clock] the motion tick 0x43ce20 takes as its frame time the animation clock's milliseconds since ctl+0x120 (0x43cea7) and stores the clock there at its end (0x43d8d3); when ctl+0 bit 0x10 is clear it first sets the bit and ctl+0x120 = now (0x43ce91..0x43ce9c), so that tick's time is 0: every train still makes one partial hop of no length, which takes its speed v_end = v to min(v, cap) (0x43d3a8..0x43d407, the floor store overwritten), decays it ×0.95 below 2.0 and stops it below 0.01 (0x43d4bc), runs the bump exchange 0x43cd60, wraps the position and the bumped-flag test; the constructor 0x4368f0 allocates the controller zeroed (0x45b9d0, GMEM_ZEROINIT), the Coasters loader 0x437b60 only ORs bits into ctl+0, and nothing in tp.exe clears bit 0x10 again, so the first tick of a new or a loaded controller (the first in state 2, 4 or 8, 0x435ac9) is the one
	public bool MotionClockStarted { get; private set; }

	/// <summary>
	/// The simulation time since the controller's motion clock word (<c>ctl+0x120</c>) was last written: the fixed steps since the last motion tick. A tick
	/// takes all of it as its frame time, so the first tick after a stretch in state 1 (a cut or reopened track runs no motion tick) catches the whole stretch up.
	/// </summary>
	// [BIN:TPW-EXE:0x43ce91 motion clock catch-up] 0x43ce20 takes as its frame time the unsigned (now − ctl+0x120) milliseconds (0x43cea7, loaded as a zero-extended qword) × 0.001 (0x6fe030) of the scaled game clock 0x7b496c (0x4709e0(0), refreshed once per frame by 0x473440 from the clock object 0x785970, the clock 0x4031e0 that also drives the substep clock 0x878c74), with no clamp, and writes every train's +0x7c with it once (0x43cedb); the trains then hop from the queue until each has spent all of it (0x43ced8), one tick, no substeps; ctl+0x120 is written only at the tick's end (0x43d8d3) and by the first tick (0x43ce9c); the controller tick 0x435a30 calls 0x43ce20 only in state 2, 4 or 8 (0x435ac9, state & 0xE), so in state 1 (the cut 0x446740, a reopen) the word stands while the clock runs, and the next tick after the track is completed again takes all the time since the last tick; the game pause (0x4092a0: the pause menu, the map, the postcard, message boxes, the pause key through 0x409350) freezes the clock, so a paused stretch adds nothing; OpenTPW's fixed steps are that scaled clock (they stop while paused and multiply with the game speed, as the substep scheduler maps 0x878c74), so the lag is the sum of the fixed steps since the last motion tick
	public double MotionClockLagSeconds { get; private set; }

	/// <summary>Test probe: the frame time the last motion tick gave every train (train <c>+0x7c</c> at the tick's start); derived, not saved or hashed.</summary>
	internal float LastMotionFrameSeconds { get; private set; }

	private void Update()
	{
		// state 1: no motion tick (0x435ac9), so the clock word stands and the lag keeps growing
		if ( !IsOperational )
			return;
		var budget = MotionClockStarted ? (float)MotionClockLagSeconds : 0f;
		MotionClockStarted = true;
		MotionClockLagSeconds = 0;
		LastMotionFrameSeconds = budget;
		if ( trains.Count == 0 )
			return;
		MoveTrains( budget );
		foreach ( var train in trains )
			ClearHeadway( train );
		TrainSounds?.StateTick();
		// [BIN:TPW-EXE:0x43b8b0 coaster station] the station step is skipped entirely in mode 64 (breakdown); an arriving train unloads open or closed and boarding needs state 4; the tick also skips the station step (and 0x439570) for a train whose flag bit 0x2 is clear (0x435ce5 test byte [esi],2); the slot setup 0x43be10 sets it (0x43be45) on each train 0x43cad6 brings below the count, and 0x43cb08..0x43cb97 clear it (0x43cb95, under controller flag 0x800) on the trains at or past it, which leave the list here, so every train in the list counts as having it set (the stride-0x60 car flags at 0x43d1ee/0x43d23e are another word)
		if ( !(IsBroken || State is CoasterControllerState.Built) )
		{
			foreach ( var train in trains )
				StationStep( train );
		}
		// [BIN:TPW-EXE:0x435cf6 train sounds] every train with flag 0x2 gets 0x439570 after its station step (open and closed outside mode 64) or without one (0x435d1c)
		TrainSounds?.PositionTick();
	}

	/// <summary>The ride's state change (<c>0x435730</c>) with what it does to the trains and the rings; see <see cref="State"/>.</summary>
	internal void EnterState( CoasterControllerState next )
	{
		var old = State;
		switch ( (old, next) )
		{
			case (CoasterControllerState.Incomplete, CoasterControllerState.Built):
				// 0x435730 runs slot setup for every train on 1 → 2; other state changes do not restart the clips.
				foreach ( var train in trains )
				{
					train.WheelAnimationStartSeconds = TimeSeconds;
					TrainSounds?.SetUp( train );
				}
				break;
			case (CoasterControllerState.Built, CoasterControllerState.Incomplete):
			case (CoasterControllerState.Open, CoasterControllerState.Incomplete):
			case (CoasterControllerState.Closed, CoasterControllerState.Incomplete):
				// The trains are removed with their riders, and the guests waiting for a seat leave with them.
				ClearTrains();
				break;
			case (CoasterControllerState.Open, CoasterControllerState.Built):
			case (CoasterControllerState.Closed, CoasterControllerState.Built):
				foreach ( var train in trains )
					UnloadAll( train );
				MoveAdmissionToLeaving();
				break;
			case (CoasterControllerState.Incomplete, CoasterControllerState.Closed):
			case (CoasterControllerState.Built, CoasterControllerState.Closed):
			case (CoasterControllerState.Open, CoasterControllerState.Closed):
				MoveAdmissionToLeaving();
				break;
		}
		State = next;
	}

	/// <summary>
	/// The coaster tool opens on this controller (<c>0x447520</c>): a finished track goes to state 2 (its riders and the guests waiting for a seat leave, the trains
	/// circulate empty without a station step), an unfinished one to state 1 (no trains).
	/// </summary>
	internal void EnterTool() => EnterState( Track.IsRunnable ? CoasterControllerState.Built : CoasterControllerState.Incomplete );

	/// <summary>
	/// One car's candidate speed for a hop (<c>0x40650</c>): max(mod·(PerHeight·(y(s) − y(s + ½))) + v, floor), the uphill
	/// modifier when the point half a sample ahead is higher.
	/// </summary>
	// [BIN:STP-PPC:0x10040A20 coaster hop speed] per car: v_c = max(fmadds(mod, PerHeight × (y(s) − y(s + ½)), v), floor) with fUphillAccelModifier when y(s + ½) > y(s), else fDownhillAccelModifier; the floor is fWinchSpeed·P on winch (0x800) sections, else fMinSpeed·P
	public static float CarSpeed( CoasterDefinition definition, float speed, float y, float yHalf, float floor )
	{
		var modifier = yHalf > y ? definition.UphillAccelModifier : definition.DownhillAccelModifier;
		return MathF.Max( MathF.FusedMultiplyAdd( modifier, definition.AccelerationPerHeight * (y - yHalf), speed ), floor );
	}

	/// <summary>
	/// The hops of one frame for a train: per hop the cars' average candidate speed, station braking (a car past
	/// <see cref="BrakingStart"/>, no bumped flag) or friction, the winch or minimum floor (the winch floor halved for a train
	/// with the headway and in-station flags), the cap; the hop takes d/((v + v′)/2) seconds; the last hop of the budget is
	/// partial; after each hop the speed decays below 2.0 and stops below 0.01, the extents update, a train that caught up with
	/// the train ahead exchanges speed with it and the position wraps.
	/// </summary>
	// [BIN:STP-PPC:0x10040650 coaster tick] hops of half a sample: per car the winch test reads the section of the sample at trunc(s + ½) (0x1004098C, 0x10040A0C), the average car speed, braking when a car is past ctl+308 (strictly) or friction on the flag-8 sample of the reference car's +64 position, which is written only after the frame's hops (0x10041094), so it is fixed per frame; max(·, W) when any car is on a winch section else max(·, M), min(·, ctl+52); τ = d/((v + v')/2) from the reference car's distance to its half-sample point (0x1004316C); a full hop if τ < budget, else α = budget/τ, v_end = (1 − α)v + αv', s += ½·((v + v_end)/2·budget/d), v = min(cap, v_end) with no floor: 0x10040D98 stores max(v_end, M), but 0x10040D9C..0x10040DC8 reload the unfloored v_end and overwrite that store; after every hop v ×= 0.95 below 2.0 and v = 0 below 0.01; s wraps by fmod
	// [BIN:TPW-EXE:0x43d054 hop floors] the winch floor [ebp-0xc] = W·P·0.5 (0x6fe064) when train flags 0x80 and 0x20 are both set (0x43d041..0x43d05d), else W·P; braking needs a car past ctl+0x134 and flag 0x40 clear (0x43d161, 0x43d294)
	// [BIN:TPW-EXE:0x43ced8 hop queue] every train starts the frame with the frame's time at +0x7c in the queue at ctl+0x12c (train order); the train at the head hops; after a full hop it goes back in before the first train whose remaining time is not above its own (0x43d48b..0x43d4b9, or at the end), after a partial hop it leaves the queue; the frame ends when the queue is empty
	private void MoveTrains( float budget )
	{
		var count = trains.Count;
		if ( hopBudgets.Length != count )
		{
			hopBudgets = new float[count];
			hopFriction = new bool[count];
		}
		hopQueue.Clear();
		// the tick's local starts as a pointer read as a float (0x43cea4), which rounds to 0
		BrakeFactor = 0;
		for ( var index = 0; index < count; index++ )
		{
			hopBudgets[index] = budget;
			// [BIN:TPW-EXE:0x43d30d friction sample] the friction flag (8) is read on sample ftol(reference car +0x40), which is written only at the end of the tick, so it is the frame's starting sample
			hopFriction[index] = (samples[(int)CarCoordinate( trains[index], Definition.ReferenceCar ) % samples.Length].Flags & CoasterSampleFlags.Friction) != 0;
			hopQueue.Add( index );
		}
		while ( hopQueue.Count > 0 )
		{
			var index = hopQueue[0];
			var full = HopTrain( trains[index], ref hopBudgets[index], hopFriction[index] );
			HopObserved?.Invoke( index, hopBudgets[index], full );
			hopQueue.RemoveAt( 0 );
			if ( full )
				Requeue( hopQueue, hopBudgets, index );
		}
	}

	/// <summary>
	/// Puts a train that made a full hop back in the hop queue (<c>0x43d48b..0x43d4b9</c>): before the first train whose remaining budget is not above
	/// its own, or at the end when every train left in the queue has more time left than it.
	/// </summary>
	// [BIN:TPW-EXE:0x43d497 hop requeue] fld other +0x7c; fcomp own +0x7c; test ah, 0x41 (C0 or C3: other ≤ own) jumps to 0x43d8e0, which stores the train in that slot; a train with more time left moves one slot toward the head (0x43d4ab)
	internal static void Requeue( List<int> queue, IReadOnlyList<float> budgets, int index )
	{
		var own = budgets[index];
		var at = 0;
		while ( at < queue.Count && budgets[queue[at]] > own )
			at++;
		queue.Insert( at, index );
	}

	/// <summary>One hop of a train (see <see cref="MoveTrains"/>); true for a full hop, false for the partial last hop of its frame.</summary>
	// [BIN:TPW-EXE:0x43d085 car coordinates in the hop] each active car's coordinate is car +0x38 / spacing + train +0x74, plus the sample count when negative but not wrapped above (the braking test compares it so with ctl+0x134); the hop distance is the reference car's (train +0x2c) from its point to its half-sample point (0x470940 on car +0x48 and +0x54); the braking blend's x takes car train +8's (0) coordinate; a partial hop starts from min(position, sample count)
	private bool HopTrain( CoasterTrain train, ref float budget, bool friction )
	{
		var count = samples.Length;
		var cars = train.CarLists.Length;
		var minimumFloor = Definition.MinSpeed * PowerFactor;
		var position = train.Position;
		var speed = train.Speed;
		var winchFloor = Definition.WinchSpeed * PowerFactor;
		if ( (train.Flags & (CoasterTrainFlags.Headway | CoasterTrainFlags.InStation)) == (CoasterTrainFlags.Headway | CoasterTrainFlags.InStation) )
			winchFloor *= 0.5f;
		var sum = 0f;
		var anyWinch = false;
		var pastBraking = false;
		for ( var car = 0; car < cars; car++ )
		{
			var at = Definition.CarOffsets[car] / Spacing + position;
			if ( at < 0 )
				at += count;
			var winch = winchSections.Contains( SampleAt( at + Hop ).Section );
			anyWinch |= winch;
			train.OnWinch[car] = winch;
			sum += CarSpeed( Definition, speed, SampleAt( at ).Position.Y, SampleAt( at + Hop ).Position.Y, winch ? winchFloor : minimumFloor );
			pastBraking |= BrakingStart < at;
		}
		var next = sum / cars;
		if ( pastBraking && (train.Flags & CoasterTrainFlags.Bumped) == 0 )
		{
			next = BrakeBlend( next, winchFloor, Definition.CarOffsets[0] / Spacing + position, BrakingStart, count, out var factor );
			train.BrakingNow = true;
			BrakeFactor = factor;
		}
		else
		{
			train.BrakingNow = false;
			if ( friction )
				next *= Definition.FrictionMultiplier;
		}
		next = MathF.Max( next, anyWinch ? winchFloor : minimumFloor );
		next = MathF.Min( next, SpeedCap );
		var distance = System.Numerics.Vector3.Distance( SampleAt( position ).Position, SampleAt( position + Hop ).Position );
		var hopTime = speed + next > 0 ? distance / ((next + speed) * 0.5f) : float.PositiveInfinity;
		var full = hopTime < budget;
		if ( full )
		{
			budget -= hopTime;
			speed = next;
			position += Hop;
		}
		else
		{
			var fraction = float.IsFinite( hopTime ) && hopTime > 0 ? budget / hopTime : 0f;
			var end = MathF.FusedMultiplyAdd( 1f - fraction, speed, fraction * next );
			var travelled = distance > 0 ? (speed + end) * 0.5f * budget / distance : 0f;
			position = MathF.FusedMultiplyAdd( Hop, travelled, MathF.Min( position, count ) );
			speed = SpeedCap < end ? SpeedCap : end;
		}
		if ( speed < DecayBelow )
			speed *= Decay;
		if ( speed < StopBelow )
			speed = 0;
		train.Position = position;
		train.Speed = speed;
		UpdateExtents( train );
		Collide( train );
		if ( train.Position >= count )
			train.Position %= count;
		// [BIN:TPW-EXE:0x43d575 bumped flag] flag 0x40 clears while the train is past the window and before the braking start (0x43d578..0x43d599)
		var coordinate = StationCoordinate( train.Position );
		if ( coordinate > StationWindow.End && coordinate < StationCoordinate( BrakingStart ) )
			train.Flags &= ~CoasterTrainFlags.Bumped;
		return full;
	}

	/// <summary>
	/// The collision exchange (<c>0x43cd60</c>, run after every hop): when the rear of the train ahead (the next index) is behind this
	/// train's front and it is slower, both take a third of their own speed plus two thirds of the other's, and the train ahead is
	/// flagged bumped.
	/// </summary>
	// [BIN:TPW-EXE:0x43cd60 train collision] T.speed = (T + 2·O)/3, O.speed = (O + 2·T)/3 and O.flags |= 0x40 when O +0x24 (+ the sample count when O +0x74 < T +0x74) < T +0x20 and O +0x78 < T +0x78; when |T' − O'| > 1 it calls the crash effect 0x439840 (sound and particles only)
	private void Collide( CoasterTrain train )
	{
		if ( trains.Count < 2 )
			return;
		var other = trains[(train.Index + 1) % trains.Count];
		var rear = other.Rear;
		if ( other.Front < train.Front )
			rear += samples.Length;
		if ( !(rear < train.Front) || !(other.Speed < train.Speed) )
			return;
		const float Third = 0.33333334f;
		var own = train.Speed;
		var ahead = other.Speed;
		train.Speed = (own - -2f * ahead) * Third;
		other.Speed = (ahead - -2f * own) * Third;
		other.Flags |= CoasterTrainFlags.Bumped;
		// [BIN:TPW-EXE:0x43ce00 crash threshold] the crash effect 0x439840 runs when the new speeds differ by more than 1.0 (0x6fe020; a NaN difference skips it)
		if ( MathF.Abs( other.Speed - train.Speed ) > 1f && Crashed is { } crashed && CrashPlace( train ) is { } place )
			crashed( place );
	}

	/// <summary>Raised when two trains bump hard: the whole-unit place of the bump, for the crash sound (<see cref="GameAudio.CoasterCrashEvent"/>).</summary>
	public event Action<System.Numerics.Vector3>? Crashed;

	/// <summary>
	/// The place the crash effect plays at: the bumping train's front on the track samples, <c>fmod(front, samples)</c> split into a sample and a fraction
	/// (<c>0x43c000</c> interpolates the two samples' positions), each coordinate truncated toward zero (<c>__ftol</c>, <c>0x439842</c>-<c>0x4398c2</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x439840 crash place] fmod (0x67b24a) of train +0x20 by the sample count (ctl +0xa8), modf (0x67b280) into sample and fraction, 0x43c000(ctl, sample, fraction, out) = lerp of the +4/+8/+0xC positions of that sample and the next (stride 0x38), then three truncations; the next sample past the last is read beyond the array by the original, the first sample here (a closed circuit)
	private System.Numerics.Vector3? CrashPlace( CoasterTrain train )
	{
		var count = samples.Length;
		if ( count == 0 || !float.IsFinite( train.Front ) )
			return null;
		var wrapped = train.Front % count;
		var index = (int)wrapped;
		if ( index < 0 || index >= count )
			return null;
		var fraction = wrapped - index;
		var from = samples[index].Position;
		var to = samples[(index + 1) % count].Position;
		var place = from * (1f - fraction) + to * fraction;
		return new System.Numerics.Vector3( (int)place.X, (int)place.Y, (int)place.Z );
	}

	/// <summary>
	/// The headway test at the end of the tick (<c>0x43d806..0x43d8b3</c>): a train with flag 0x80 loses it once the time to the train
	/// ahead reaches <c>uiTrainReleaseRate</c>. The original multiplies the sample count between the two trains by spacing over the
	/// speed of the sample whose index is the train's own index (a loop counter reused as a sample index); OpenTPW keeps that.
	/// </summary>
	// [BIN:TPW-EXE:0x43d85f headway time] t = (s − c)·(spacing / sample[i mod n].speed) with c = trunc(position), s = trunc(next train's position) (+ n − 1 when below the train's position); flag 0x80 clears when release ≤ 1000·t (0x43d8a5..0x43d8b1); the sample index is the train's own index i (0x43d85f), where a walk over the samples between the trains would use the walking index
	private void ClearHeadway( CoasterTrain train )
	{
		if ( (train.Flags & CoasterTrainFlags.Headway) == 0 || samples.Length == 0 )
			return;
		var other = trains[(train.Index + 1) % trains.Count];
		var count = samples.Length;
		var stop = (int)other.Position;
		if ( stop < train.Position )
			stop += count - 1;
		var from = (int)train.Position;
		var seconds = 0f;
		if ( from < stop )
		{
			var each = Spacing / samples[train.Index % count].Speed;
			for ( var step = stop - from; step > 0; step-- )
				seconds += each;
		}
		if ( Definition.TrainReleaseRate <= seconds * 1000f )
			train.Flags &= ~CoasterTrainFlags.Headway;
	}

	// ---- Canonical state -----------------------------------------------------------------------------

	internal void AddCanonicalState( StateHasher hash )
	{
		Track.AddCanonicalState( hash );
		hash.Add( Initialised );
		hash.Add( (int)State );
		hash.Add( BreakdownState );
		hash.Add( SpeedSetting );
		hash.Add( SeatsPerTrain );
		hash.Add( SpeedCap );
		hash.Add( LeadCarCoordinate );
		hash.Add( PaidCost );
		hash.Add( TimeSeconds );
		hash.Add( MotionClockStarted );
		hash.Add( MotionClockLagSeconds );
		hash.Add( SpeedSetByReplay );
		hash.Add( BrakingStart );
		hash.Add( StationWindow.Start );
		hash.Add( StationWindow.End );
		hash.Add( IntersectionCount );
		hash.Add( samples.Length );
		hash.Add( Spacing );
		foreach ( var sample in samples )
		{
			hash.Add( (long)sample.Flags );
			hash.Add( sample.Position.Y );
			hash.Add( sample.Bank );
			hash.Add( sample.Speed );
		}
		hash.Add( Rating.HasValue );
		if ( Rating is { } result )
		{
			hash.Add( result.Sickness );
			hash.Add( result.Excitement );
			hash.Add( result.LapTime );
			hash.Add( result.Trains );
		}
		hash.Add( winchSections.Count );
		foreach ( var section in winchSections.Order() )
			hash.Add( section );
		hash.Add( trains.Count );
		foreach ( var train in trains )
		{
			hash.Add( train.Position );
			hash.Add( train.Speed );
			hash.Add( (int)train.Flags );
			hash.Add( train.FirstBoardingCar );
			hash.Add( train.LastBoardingCar );
			foreach ( var car in train.Cars )
			{
				hash.Add( car.Count );
				foreach ( var guest in car )
					hash.Add( guest );
			}
			foreach ( var car in train.BoardingCars )
			{
				hash.Add( car.Count );
				foreach ( var guest in car )
					hash.Add( guest );
			}
		}
		hash.Add( admission.Count );
		foreach ( var guest in admission )
			hash.Add( guest );
		hash.Add( leaving.Count );
		foreach ( var guest in leaving )
			hash.Add( guest );
	}

	// ---- Original save import -------------------------------------------------------------------------

	/// <summary>One saved train slot of an original Coasters block as the import restores it: the reference car's position (train <c>+0x74</c>), the speed (<c>+0x78</c>) and the flags (<c>+0</c>).</summary>
	internal readonly record struct OriginalTrain( float Position, float Speed, CoasterTrainFlags Flags, uint SoundState = 0, bool ScreamHeld = false,
		IReadOnlyList<IReadOnlyList<int>>? Lap = null, IReadOnlyList<IReadOnlyList<int>>? Boarding = null );

	/// <summary>
	/// Restores a coaster block of an original save into this fresh ride (<see cref="OriginalCoasterImport"/>): its station was placed, which built the three station
	/// nodes, as the loader's station-node builder does. The track goes in as mapped, the controller words as saved, the speed setting stays the constructor's 60, and a
	/// runnable track is rated at that cap while the saved trains keep their places, as the first rebuild after a load leaves them.
	/// </summary>
	// [BIN:TPW-EXE:0x437B60 coaster loader] the loader restores the state word, power factor, trains in service, seats and intersection count (0x438340..0x43837D) and every train slot's flags (|= 4), car count, position +0x74 and speed +0x78 (0x438386..), and sets controller flags 0x1001 (dirty) and 0x4000 (loaded); the speed setting +0x30 is not in the block and stays the constructor's 60 (0x4368F0 → 0x43C3D0 with its load argument); the admission and leaving rings are the constructor's fresh ones and the trains' boarding spans (+0x18/+0x1C) are not saved
	// [BIN:TPW-EXE:0x43EB70 rebuild after load] the next frame's coaster update (0x435A30, run after the substep loop of 0x54E360) rebuilds every dirty controller: the knot pass, the winch clear 0x432B50, the sampler 0x440C10, the rating pass 0x43C450 and the self-intersection pass 0x439AB0, then it clears 0x4000 (0x43F01E); the rating pass is called only from here and, while 0x4000 is set, rates the track (0x43DF90) but skips the train count 0x43BA20, the slot set-up and the layout 0x43C1F0 (0x43CA6B), so the saved trains in service stay where they were, at their saved speeds
	// [BIN:TPW-EXE:0x415140 load restarts the clock] the save loader 0x414D40 calls 0x415140 after the payload (0x415270), which sets 0x879070; the running case of the main loop 0x54E360 then restarts the substep clock 0x878C74 at the frame's own time, so the first frame after a load runs its substep loop (and the script pass 0x5516B0) only when the clock advanced a millisecond between those two reads, and its coaster update rebuilds at the constructor's speed setting before any script pass
	internal void ImportOriginal( CoasterTrackSaveData track, CoasterControllerState state, int breakdownState, int seatsPerTrain, IReadOnlyList<OriginalTrain> savedTrains )
	{
		ArgumentNullException.ThrowIfNull( track );
		ArgumentNullException.ThrowIfNull( savedTrains );
		if ( !Enum.IsDefined( state ) )
			throw new InvalidDataException( $"Coaster '{Name}' holds the controller state {(int)state}, which is not 1, 2, 4 or 8." );
		Track.RestoreSnapshot( track );
		ClearTrains();
		leaving.Clear();
		admission.Clear();
		Initialised = false;
		State = state;
		BreakdownState = breakdownState;
		SpeedSetting = DefaultSpeedSetting;
		SpeedCap = SpeedCapFor( Definition, SpeedSetting );
		SeatsPerTrain = BoundedCapacity( seatsPerTrain );
		// [EXT:coaster-ledger] the money a track cost is OpenTPW's own ledger word (the original keeps none): an imported track was paid for in the saved park, so this session has charged nothing for it
		PaidCost = 0;
		TimeSeconds = 0;
		LeadCarCoordinate = 0;
		// the loaded controller's bit 0x10 is clear (the loader only ORs bits into ctl+0): its first motion tick takes no time
		MotionClockStarted = false;
		MotionClockLagSeconds = 0;
		// bit 0x8000 is not in the saved wire map (0x437300 writes ctl+0 bits 0x20..0x800), so a loaded controller has it clear
		SpeedSetByReplay = false;
		if ( !Track.IsRunnable )
		{
			// the next frame's rebuild of the loaded controller (0x435A30 → 0x43EB70) runs the knot pass on an unfinished chain too, which stores the auto-banks and half turns in its nodes
			if ( Track.Nodes.Count >= CoasterTrack.StationNodeCount )
				Track.BuildKnotPass( Site, writeBanks: true );
			RaiseTrackChanged();
			return;
		}
		RateTrack();
		foreach ( var saved in savedTrains )
		{
			var train = new CoasterTrain( trains.Count, Definition.CarCount, TimeSeconds );
			RebuildSeats( train );
			train.Position = saved.Position;
			train.Speed = saved.Speed;
			train.Flags = saved.Flags;
			// [BIN:TPW-EXE:0x4383FE sound state load] the loader restores the slot's sound state (+0x38) raw, and no slot setup follows for a slot that is set up (flag 0x1): the loops stay silent until the next setup, which then compares its state with this one
			StoreTrainSoundState( train.Index, saved.SoundState );
			// [BIN:TPW-EXE:0x4383DA train flags load] the flags word is restored raw, so a saved train that held its scream (flag 0x100, set by 0x4393E0 and saved by 0x4377F4) goes on holding it; the handle +0x70 is not saved (only +0x38, +0x64 and +0x68, 0x43780A..0x437821)
			StoreTrainScreamFlag( train.Index, saved.ScreamHeld );
			// [BIN:TPW-EXE:0x437B60 train rider lists] the loader fills each car's two lists as saved (list +0x28 the riders on the lap, +0x2C the guests seated at the station) without hanging a head (0x43853E..0x43859F; 0x44B410 is not called from the loader, and the controllers are built afresh with sockets nobody hung: the system reset 0x437150 destroys every controller and unhangs its cars' sockets through 0x43BD20); the seats are the list indices and the heads come back with the sprite scripts of the Scripts block (SAVE-013)
			for ( var car = 0; car < train.CarLists.Length; car++ )
			{
				if ( saved.Lap is { } lap && car < lap.Count )
					train.CarLists[car].AddRange( lap[car] );
				if ( saved.Boarding is { } boarding && car < boarding.Count )
					train.BoardingLists[car].AddRange( boarding[car] );
			}
			UpdateExtents( train );
			trains.Add( train );
		}
		TrackChanged?.Invoke( this );
	}

	/// <summary>
	/// The station script of an imported coaster holds its slot (script <c>+0xE0</c>, stored by <c>COAST 8</c>): the controller counts as created by its script. The script
	/// loop's speed setter then applies the script's speed word after the script's first slice after the load (<see cref="OriginalObjectEffects.AfterSlice"/>), which
	/// re-rates the track and lays the trains out again when it differs from 60.
	/// </summary>
	// [BIN:TPW-EXE:0x5516B0 script speed] after each slice the cadence selects (one pass in eight by script id unless its TURBO byte is set, 0x5516E1; the call at 0x5517EA), a script whose +0xE0 holds a coaster slot calls 0x43B2B0(slot, the signed word +0xC0), so after a load the speed word applies after the station script's first slice; 0x43B2B0 looks the controller up in 0x790BD0 and, when the word differs from the setting +0x30, calls 0x43C3D0, which stores the setting and the cap and marks the controller dirty (0x4346C0); the rebuild that follows has 0x4000 clear (the first rebuild after the load cleared it), so its rating pass counts the trains again and lays them out from train 0's last car (0x43C1F0), whose +0x40 the first frame's motion tick 0x43CE20 rewrote from the saved +0x74 without moving the train (that tick takes no time, 0x43CE85; UpdateFirstFrameAfterLoad)
	internal void BindImportedScript() => Initialised = true;

	// ---- Park-resume snapshot ----------------------------------------------------------------------------

	/// <summary>
	/// Restores a saved controller into this fresh ride (its station was placed, which reset the track to the station
	/// node): the track verbatim, then the controller scalars — before the rebuild, whose rating pass reads the speed
	/// cap — then the rebuild itself for a runnable track (the curve, rating, winch marks, braking point and train
	/// count are re-derived by the same deterministic pass completion ran, never copied from the save), then the
	/// trains and riders the save holds over the placed ones. A count mismatch throws
	/// <see cref="InvalidDataException"/> — the restore is a park-resume candidate's step, and the failed candidate is
	/// discarded whole, so a half-restored controller never simulates.
	/// </summary>
	internal void RestoreSnapshot( CoasterRideSaveData saved )
	{
		ArgumentNullException.ThrowIfNull( saved );
		if ( saved.KnotPass is { } knotPass && knotPass != KnotPassVersion )
			throw new InvalidDataException( $"Coaster '{Name}' was saved on knot pass {knotPass}; this version knows {KnotPassVersion}." );
		if ( saved.State is { } checkedState && !Enum.IsDefined( (CoasterControllerState)checkedState ) )
			throw new InvalidDataException( $"Coaster '{Name}' holds the controller state {checkedState}, which is not 1, 2, 4 or 8." );
		if ( saved.MotionClockLagSeconds is { } lag && !(double.IsFinite( lag ) && lag >= 0) )
			throw new InvalidDataException( $"Coaster '{Name}' holds the motion clock lag {lag}, which is not a finite time of 0 or more." );
		var migrated = Track.RestoreSnapshot( saved.Track );
		Initialised = saved.Initialised;
		// A save from before the controller state held only the closed flag: a finished track is open or closed, an unfinished one has no state to be in but 1.
		// [EXT:online-coop-snapshot-join] kept for a join: a live coaster is never runnable-less outside state 1 (the tool's exit gives Built only when runnable, every runnability change goes through it)
		State = !Track.IsRunnable ? CoasterControllerState.Incomplete
			: saved.State is { } savedState ? (CoasterControllerState)savedState
			: saved.Closed ? CoasterControllerState.Closed : CoasterControllerState.Open;
		BreakdownState = saved.BreakdownState;
		SpeedSetting = saved.SpeedSetting;
		// A save from before the head sockets bounded the capacity can hold more than one train's sockets, or 0 (which now stands for them): it takes COAST 6's bounds.
		SeatsPerTrain = BoundedCapacity( saved.SeatsPerTrain );
		SpeedCap = saved.SpeedCap;
		PaidCost = saved.PaidCost;
		TimeSeconds = saved.TimeSeconds;
		LeadCarCoordinate = saved.LeadCarCoordinate ?? 0f;
		// a park resume continues the controller (its motion clock too); a save from before the motion clock counts it started on a track that runs
		MotionClockStarted = saved.MotionClockStarted ?? Track.IsRunnable;
		// a save from before the catch-up holds no lag: the controller continues as if its last motion tick had just run
		MotionClockLagSeconds = saved.MotionClockLagSeconds ?? 0;
		SpeedSetByReplay = saved.SpeedSetByReplay ?? false;
		if ( Track.IsRunnable )
			Rebuild();
		else
		{
			// A track that never completed holds no samples and no trains; its riders (if any) wait to leave.
			if ( trains.Count != 0 )
				ClearTrains();
			RaiseTrackChanged();
		}
		// A save of the older shape (no train flags, or the station of one node) was rated with the earlier pylon heights, minimum heights, end node flags
		// and station, so the re-derived rating can place another number of trains or cars and the samples start elsewhere. Its trains are not kept: the new
		// placement stands and every saved rider waits to leave (they are not lost). A save of the current shape must match exactly, or it is refused.
		// A save from before the pylon tag knots stood its trains on a curve with one knot per node: where a pylon model gives the knots, the curve and the samples moved.
		var olderKnots = saved.KnotPass is null && (Definition.StandardPylon.TagModel != null || (Definition.HasLoopPylon && Definition.Pylon( true ).TagModel != null));
		var olderShape = migrated || olderKnots || saved.Trains.Any( train => train.Flags is null );
		var matches = !migrated && !olderKnots && saved.Trains.Count == trains.Count
			&& saved.Trains.Select( ( train, index ) => train.Cars.Count == trains[index].Cars.Count ).All( equal => equal );
		if ( !matches && !olderShape )
		{
			if ( saved.Trains.Count != trains.Count )
				throw new InvalidDataException( $"Coaster '{Name}' holds {saved.Trains.Count} trains in the save, but the restored track places {trains.Count}." );
			throw new InvalidDataException( $"Coaster '{Name}' holds trains with another number of cars in the save than the definition gives ({trains[0].Cars.Count} cars per train)." );
		}
		leaving.Clear();
		foreach ( var guest in saved.Leaving )
			leaving.Enqueue( guest );
		admission.Clear();
		foreach ( var guest in saved.Admission ?? new List<int>() )
			admission.Enqueue( guest );
		if ( !matches )
		{
			foreach ( var train in saved.Trains )
			{
				foreach ( var car in train.Cars )
					foreach ( var guest in car )
						leaving.Enqueue( guest );
				foreach ( var car in train.BoardingCars ?? new List<List<int>>() )
					foreach ( var guest in car )
						leaving.Enqueue( guest );
			}
			return;
		}
		for ( var index = 0; index < trains.Count; index++ )
			RestoreTrain( index, saved.Trains[index] );
	}

	/// <summary>
	/// Restores one train. A save made before the original's station rule (no flags) held the old stop-and-dispatch states: a
	/// train that was boarding keeps its guests in the boarding lists and counts as in the station, so they ride when it leaves;
	/// one that was unloading hands its riders to the leaving ring; waiting and running trains keep their riders on the lap.
	/// </summary>
	private void RestoreTrain( int index, CoasterTrainSaveData saved )
	{
		var train = trains[index];
		if ( saved.Cars.Count != train.Cars.Count )
			throw new InvalidDataException( $"Coaster '{Name}' train {index} holds {saved.Cars.Count} cars in the save, the definition gives {train.Cars.Count}." );
		if ( saved.BoardingCars != null && saved.BoardingCars.Count != train.Cars.Count )
			throw new InvalidDataException( $"Coaster '{Name}' train {index} holds {saved.BoardingCars.Count} boarding lists in the save, the definition gives {train.Cars.Count}." );
		if ( saved.ReferenceCar is { } reference && reference != Definition.ReferenceCar )
			throw new InvalidDataException( $"Coaster '{Name}' train {index} is placed by car {reference} in the save, the definition's reference car is {Definition.ReferenceCar}." );
		// A save from before the car layout holds the front car's coordinate; the reference car stands its offset behind it.
		train.Position = saved.ReferenceCar is null ? StationCoordinate( saved.Position - Definition.CarOffsets[0] / Spacing ) : saved.Position;
		train.Speed = saved.Speed;
		UpdateExtents( train );
		var legacyBoarding = saved.Flags is null && saved.State == LegacyBoardingState;
		var legacyUnloading = saved.Flags is null && saved.State == LegacyUnloadingState;
		for ( var car = 0; car < train.CarLists.Length; car++ )
		{
			train.CarLists[car].Clear();
			train.BoardingLists[car].Clear();
			if ( legacyUnloading )
			{
				foreach ( var guest in saved.Cars[car] )
					leaving.Enqueue( guest );
			}
			else
				( legacyBoarding ? train.BoardingLists : train.CarLists )[car].AddRange( saved.Cars[car] );
			if ( saved.BoardingCars != null )
				train.BoardingLists[car].AddRange( saved.BoardingCars[car] );
		}
		if ( saved.Flags is { } flags )
		{
			train.Flags = (CoasterTrainFlags)flags;
			train.FirstBoardingCar = saved.FirstBoardingCar;
			train.LastBoardingCar = saved.LastBoardingCar;
		}
		else
		{
			train.Flags = legacyBoarding || legacyUnloading ? CoasterTrainFlags.InStation : CoasterTrainFlags.None;
			train.FirstBoardingCar = 0;
			train.LastBoardingCar = legacyBoarding ? train.CarLists.Length - 1 : 0;
		}
	}

	/// <summary>The knot pass this version builds the curve on (2: a pylon with its model gives the knots of its posed track tags, <c>0x430ed0</c>); saves record it.</summary>
	internal const int KnotPassVersion = 2;

	/// <summary>The states of the stop-and-dispatch model saves made before the original's station rule hold: boarding at the station (0) and unloading (3); 1 ran and 2 waited before the station.</summary>
	internal const int LegacyBoardingState = 0;
	internal const int LegacyRunningState = 1;
	internal const int LegacyUnloadingState = 3;
}
