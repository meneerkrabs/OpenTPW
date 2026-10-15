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

	internal CoasterTrain( int index, int carCount )
	{
		Index = index;
		var count = Math.Max( 1, carCount );
		cars = new List<int>[count];
		boarding = new List<int>[count];
		Seats = new int[count];
		for ( var car = 0; car < count; car++ )
		{
			cars[car] = new List<int>();
			boarding[car] = new List<int>();
		}
	}

	public int Index { get; }
	/// <summary>The front car's position in samples from the station node, [0, sample count) (train <c>+116</c>).</summary>
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
	/// <summary>Seats per car when the script has not set a capacity yet (COAST-023).</summary>
	public const int DefaultSeatsPerCar = 4;
	/// <summary>Distance between the fronts of two cars in world units (COAST-022).</summary>
	public const float CarSpacing = 7f;
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
	private HashSet<int> winchSections = new();

	public CoasterRide( CoasterDefinition definition, CoasterWorld world, string name = "" )
	{
		Definition = definition ?? throw new ArgumentNullException( nameof( definition ) );
		World = world ?? throw new ArgumentNullException( nameof( world ) );
		Name = name;
		Track = new CoasterTrack( definition );
		SeatsPerTrain = Math.Max( 1, definition.MaxCars ) * DefaultSeatsPerCar;
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
	// [EXT:coaster-tool-admission] the original keeps the guests of the admission ring in it when a running ride goes from 4 to 2 (they wait for the ride to open again); OpenTPW lets them leave with the unloaded riders, so no guest waits on a ride that does not run; the power factor reset of 1 → 2 is derived from the ride mode here, and the highlight (0x441840) and the tool's cut (0x446740) belong to the coaster tool panel (COAST-028)
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
	// [APPROX:COAST-030] the original's controller +0x140 is the self-intersection count, not a rider count (0x439c40, 0x441970), and its pick-up (0x446300) has no rider test; OpenTPW refuses edits while a train carries guests, the Mac reading of the guest predicate
	public int RidersOnBoard => trains.Sum( train => train.Riders );
	/// <summary>Guests handed over by <c>COAST 1</c> and waiting for a train in the station to seat them (the admission ring).</summary>
	public int AdmissionCount => admission.Count;
	/// <summary>Riders taken off a train, a removed track or a closed ride, waiting for <c>COAST 3</c>.</summary>
	public int LeavingCount => leaving.Count;
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
	/// <summary>
	/// The guest predicate <c>0x441970</c>: the coaster is complete and its track crosses itself nowhere (controller <c>+0x140 == 0</c>); guests do not
	/// join or board a coaster that fails it.
	/// </summary>
	// [BIN:TPW-EXE:0x441988 guest predicate] +0x140 must be 0 (the self-intersection count of 0x439c40), +0x3C bit 0 set (complete) and bit 1 clear (circuit not open), and the coaster is not the one being edited (0x790fec: OpenTPW's edits un-complete the track)
	public bool AcceptsGuests => IsOperational && IntersectionCount == 0;
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
	/// The ride view of a train's front car (COAST-plan §7, §18): position from the interpolated sample, forward from the
	/// sample direction, roll from the sample roll. False without that train.
	/// </summary>
	// [APPROX:COAST-011] the ride view is the front car's sample pose; no camera mode uses it yet and Info.FirstPersonSprites, FIRSTPERSONVIEWDISTANCE and COASTERSMOOTHNESS are not read; no camera code calls the coaster module — evidence needed: the camera methods 0x10093D08/0x10094474 for a follow mode fed by car +72..+80, or a capture of the original ride view
	public bool TryGetRideView( int train, out System.Numerics.Vector3 position, out System.Numerics.Vector3 forward, out float roll )
	{
		position = forward = default;
		roll = 0;
		if ( !IsOperational || train < 0 || train >= trains.Count )
			return false;
		var sample = SampleAt( trains[train].Position );
		(position, forward, roll) = (sample.Position, sample.Direction, sample.Bank);
		return true;
	}

	/// <summary>
	/// Fits the curve through the track's knots, runs the rating pass once (marking winch sections), computes the
	/// braking start, the train count and places the trains evenly (train 0 at the station). Called on completion.
	/// </summary>
	// [BIN:STP-PPC:0x100449F8 rebuild] node layout, tangents, angles, auto-bank and knots (0x10033398), knot links, sampling (0x10044FF4), then the rating pass (0x1003FC18)
	internal void Rebuild()
	{
		PlaceTrains( RateTrack() );
		// A finished track puts the controller in state 2 (0x435570 → 0x435730); a track edited while built, open or closed keeps its state.
		if ( State == CoasterControllerState.Incomplete )
			EnterState( CoasterControllerState.Built );
		TrackChanged?.Invoke( this );
	}

	/// <summary>Curve, rating pass, winch marks, counted sections and braking start for the current speed cap; returns the train count.</summary>
	private int RateTrack()
	{
		var nodes = Track.Nodes;
		var knots = Track.BuildKnots( Site );
		var sampling = Curve.Sample( knots, Definition.Curviness );
		samples = sampling.Samples;
		Spacing = sampling.Spacing;
		var tail = nodes.Count - 1;
		var (result, winch) = CoasterRating.RatePass( Definition, samples, Spacing, SpeedCap, tail, sampling.KnotCoordinates.Length > tail ? sampling.KnotCoordinates[tail] : null );
		winchSections = winch;
		for ( var index = 0; index < nodes.Count; index++ )
		{
			var node = nodes[index];
			node.Flags = winch.Contains( index ) ? node.Flags | CoasterNodeFlags.Winch : node.Flags & ~CoasterNodeFlags.Winch;
		}
		// Controller +264: the sections with both node flags 0x40 and 0x80 (CoasterRating.FinalFor; ORACLE F8).
		var counted = CoasterRating.FinalFor( 0, 0, 0, 0, nodes.Select( node => (uint)node.Flags ), 0, 0, 0, 0 ).Sections;
		Rating = result with { CountedSections = counted };
		var collisions = CoasterCollision.Run( NodesForCollision( nodes, samples ), samples );
		IntersectionCount = collisions.Count;
		CollisionLists = collisions.Lists;
		// [BIN:TPW-EXE:0x43c72f braking start] ctl+0x134 = (n − 1) − v_last/spacing with n the sample count; the end node stands on the first station node, so the brake zone is the stretch just before the sample list's end, which is the start of the platform
		BrakingStart = BrakingStartFor( samples.Length, samples[BrakingSpeedSample( samples, tail )].Speed, Spacing );
		// [BIN:TPW-EXE:0x43b750 station window] f8 = (1 − k)·A + k·(A + B)/2 and fc = (1 − k)·B + k·(A + B)/2 with k = definition +0x31c (fEntranceSizeInterval, copied unconditionally) and A, B the sample coordinates of the first and third station node ([[node+0x90]+4]+8): the first one's is 0, the third one's is where the sampler passed S2
		KnotCoordinates = sampling.KnotCoordinates;
		var windowStart = 0f;
		var windowEnd = sampling.KnotCoordinates.Length > CoasterTrack.StationNodeCount - 1 ? sampling.KnotCoordinates[CoasterTrack.StationNodeCount - 1] : samples.Length;
		var k = Definition.EntranceSizeInterval;
		var middle = (windowStart + windowEnd) * 0.5f;
		StationWindow = ((1f - k) * windowStart + k * middle, (1f - k) * windowEnd + k * middle);
		return result.Trains;
	}

	/// <summary>The nodes as the self-intersection pass sees them: the sample index of each knot and the radius of its pylon control.</summary>
	// [BIN:STP-PPC:0x100457A0 node sample index] node +252 = the truncated sampler coordinate when the walk reaches the node: the last sample before its knot, or the sample on the knot when its t is 0 (0x43a060 reads it as the node's first sample)
	private List<CoasterCollision.Node> NodesForCollision( IReadOnlyList<CoasterNode> nodes, IReadOnlyList<CoasterSample> samples )
	{
		var result = new List<CoasterCollision.Node>( nodes.Count );
		for ( var index = 0; index < nodes.Count; index++ )
		{
			var first = -1;
			for ( var sample = 1; sample < samples.Count && index > 0; sample++ )
			{
				if ( samples[sample].Section == index )
				{
					first = sample;
					break;
				}
			}
			var at = index == 0 ? 0 : first < 0 ? (result.Count > 0 ? result[^1].SampleIndex : 0) : samples[first].T == 0 ? first : first - 1;
			var node = nodes[index];
			result.Add( new CoasterCollision.Node( at, node.IsPylonless ? null : Definition.Pylon( node.LoopPylon ).CollisionPossibleRadius, node.IsPylonless ) );
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
	public static float BrakeBlend( float speed, float floor, float front, float start, float total )
	{
		var x = (front - start) / (total - start);
		if ( x < 0 || x > 1 || float.IsNaN( x ) )
			x = 1;
		var x4 = x * x * (x * x);
		return MathF.FusedMultiplyAdd( speed, 1f - x4, floor * x4 );
	}

	// [BIN:STP-PPC:0x1003F930 train spacing] after the rating pass 0x1003E9EC allocates the trains and 0x1003F930 spaces them along the track
	// [BIN:TPW-EXE:0x436dd4 train constructor] a new train has position 0 and speed 0 (train +0x74/+0x78), whatever its place; the tick accelerates it with the floors and gravity like any other
	private void PlaceTrains( int count )
	{
		trains.Clear();
		for ( var index = 0; index < count; index++ )
		{
			var train = new CoasterTrain( index, Definition.MaxCars );
			train.Position = index == 0 ? 0f : (float)samples.Length * index / count;
			RebuildSeats( train );
			UpdateExtents( train );
			trains.Add( train );
		}
	}

	/// <summary>
	/// The seats of each car (<c>0x43ba90</c>): the capacity split over the cars by integer division of what is left by the cars
	/// still to fill, so the front cars get the smaller share.
	/// </summary>
	// [BIN:TPW-EXE:0x43bb7b seat split] car +0x44 = remaining / (seat cars − filled) for the cars whose type is the train's seat type, 0 for the others
	// [APPROX:COAST-023] every car carries seats; the original seats only the cars of the train definition's seat type (+0x10) and caps the capacity by the fleet sockets (definition +0x318)
	private void RebuildSeats( CoasterTrain train )
	{
		var remaining = Math.Max( 0, SeatsPerTrain );
		for ( var car = 0; car < train.Seats.Length; car++ )
		{
			var share = remaining / (train.Seats.Length - car);
			train.Seats[car] = share;
			remaining -= share;
		}
	}

	/// <summary>Takes the trains off the track; their riders and the guests waiting for a seat wait for <c>COAST 3</c> to leave.</summary>
	internal void ClearTrains()
	{
		foreach ( var train in trains )
			UnloadAll( train );
		MoveAdmissionToLeaving();
		trains.Clear();
		IntersectionCount = 0;
		CollisionLists = Array.Empty<IReadOnlyList<int>>();
		samples = Array.Empty<CoasterSample>();
		Spacing = 0;
		Rating = null;
		winchSections = new HashSet<int>();
		TrackChanged?.Invoke( this );
	}

	internal void RaiseTrackChanged() => TrackChanged?.Invoke( this );

	/// <summary>The sample at a position in samples, its position interpolated linearly to the next sample (fused, <c>0x409b0</c>).</summary>
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
		var b = samples[(index + 1) % count];
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
				// [BIN:TPW-EXE:0x43b330 coaster capacity] min(max(request, 0), definition +0x318 (the fleet's seat socket total), the first train definition's +8); the lower bound is the literal 0 where the Mac reading takes a global (documented difference); a changed value is rebuilt through 0x43ba90 (seat allocation, not modelled)
				// [APPROX:COAST-023] a train seats COAST 6's capacity (at least 0; uiMaxCars × 4 before the script sets it) split over its cars by the original's recurrence, all cars carrying seats; the two upper bounds (definition +0x318 fleet sockets and the first train definition's +8) are not applied because OpenTPW reads neither the seat sockets nor that train field, the admission ring has no size limit (definition +0x2f8) and the leaving ring none (2 × definition +0x318) — evidence needed: the seat socket count per car (definition +0x318, train definition +8, car type +0x10) and the ring sizes
				SeatsPerTrain = Math.Max( 0, parameter );
				foreach ( var train in trains )
					RebuildSeats( train );
				return 0;
			default:
				return 0;
		}
	}

	/// <summary>
	/// The ride-script loop's speed setter (<c>0x43b2b0</c>): a changed setting becomes the controller's speed setting and the
	/// speed cap lerp(maxAtMin, maxAtMax, s/100) (<c>0x43c3d0</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x43b2b0 coaster speed setter] the loop 0x5516b0 calls it every pass with the signed 16-bit script bias at script +0xC0 (50 at creation, 0x558c3c); applied via 0x43c3d0 (setting +0x30, cap +0x34 = fmadds-style lerp of definition +0x4C and +0x50 by s·0.01, dirty flags 0x1001, the per-node pass 0x43afa0 on flag 8) when it differs from the setting and money is off (0x785a2c set) or controller +0 bit 0x8000 is clear; bit 0x8000 and the money switch are not modelled (treated as clear), and since the script's bias (RideVM.SpeedBias, the ride's InitSpeed) is written at ride init and when an upgrade completes (RIDES-033) it changes between passes only then
	internal void ApplyScriptSpeed( int bias )
	{
		if ( bias == SpeedSetting )
			return;
		SpeedSetting = bias;
		SpeedCap = SpeedCapFor( Definition, bias );
		// 0x43c3d0 marks the controller dirty (0x1001) and re-runs the per-node pass: a runnable track is re-rated at the
		// new cap, as a park resume would derive it. The trains keep running when their count holds; otherwise they are
		// taken off (riders wait for COAST 3) and placed again.
		if ( !Track.IsRunnable )
			return;
		var count = RateTrack();
		if ( count != trains.Count )
		{
			foreach ( var train in trains )
				UnloadAll( train );
			PlaceTrains( count );
		}
		TrackChanged?.Invoke( this );
	}

	/// <summary>The room COAST 2 reports: the capacity COAST 6 stored minus the guests waiting for a seat on the admission ring.</summary>
	// [BIN:TPW-EXE:0x43b1f0 coaster room] COAST 2 returns max(0, ctl+0xb8 − ctl+0xc0 − ctl+0xbc): the capacity COAST 6 stored (0x43bbd1: ctl+0xf0 → +0xb8), minus the ring's second counter (+0xc0, only ever 0), minus the guests on the admission ring (+0xbc); no state, mode or train enters it, so the room is the capacity of one train whether or not a train is in the station
	public int Room => Math.Max( 0, SeatsPerTrain - admission.Count );

	/// <summary>
	/// COAST 1: the guest the script handed over goes on the admission ring (<c>0x43b0e0</c>); a train in the station seats it
	/// while its cars roll through the window. A guest handed over with no room goes back through <c>COAST 3</c>.
	/// </summary>
	// [BIN:STP-PPC:0x1003E2F8 boarding] the pending ring's read cursor (+196) is popped and the visitor is placed in a randomly chosen car (s·214013 + 2531011)
	// [BIN:TPW-EXE:0x43b0e0 coaster boarding] COAST 1 pushes the guest on the ring at ctl+0xb0 when its count is below the capacity (definition +0x2f8, a queue size OpenTPW does not read; the room above bounds it); nobody is seated here
	internal void Board( int guest )
	{
		if ( guest <= 0 )
			return;
		if ( Room == 0 )
		{
			leaving.Enqueue( guest );
			return;
		}
		admission.Enqueue( guest );
	}

	internal int TakeLeavingRider() => leaving.Count > 0 ? leaving.Dequeue() : 0;

	/// <summary>The admission ring into the leaving ring (<c>0x43b390</c>), when the ride closes and when the track is taken away.</summary>
	// [BIN:TPW-EXE:0x43b390 coaster close] moves min(leaving ring room, admission count) guests from the admission ring (+0xb0) to the leaving ring (+0xd0), oldest first; the leaving ring's size (2 × definition +0x318) is not modelled, so every waiting guest moves
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

	private void UnloadAll( CoasterTrain train )
	{
		Unload( train, boardingList: false );
		Unload( train, boardingList: true );
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
	// [BIN:TPW-EXE:0x43d50b train extents] train +0x20 = front car offset/spacing + position + the car model's front extent; +0x24 = rear car offset/spacing + position − its rear extent; neither is wrapped
	// [APPROX:COAST-022] the cars are 7 world units apart with no model extents and the front car is the reference car (the original's is the middle car, train +0x2c)
	private void UpdateExtents( CoasterTrain train )
	{
		if ( samples.Length == 0 )
		{
			train.Front = train.Rear = 0;
			return;
		}
		var coordinate = StationCoordinate( train.Position );
		train.Front = coordinate;
		train.Rear = coordinate - (train.CarLists.Length - 1) * CarSpacing / Spacing;
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
				// The original reads the first and last car index 0x43b7e0 returns even when no car is inside; OpenTPW's cars have no extents, so a train that overlaps the window can have none, and then nothing is seated this step.
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
	// [BIN:TPW-EXE:0x43b7e0 cars in window] car +0x40 + front extent ≥ ctl+0xf8 and car +0x40 − rear extent ≤ ctl+0xfc; OpenTPW's cars have no extents, so a car is inside when its point is
	private bool CarsInWindow( CoasterTrain train, out int first, out int last )
	{
		var (start, end) = StationWindow;
		first = last = 0;
		var found = false;
		for ( var car = 0; car < train.CarLists.Length; car++ )
		{
			var coordinate = train.Front - car * CarSpacing / Spacing;
			if ( coordinate < 0 )
				coordinate += samples.Length;
			if ( coordinate < start || coordinate > end )
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
	// [APPROX:COAST-022] the hops run on OpenTPW's fixed 60 Hz simulation step instead of the elapsed milliseconds of the cached animation clock per frame (0x4709e0, controller +0x120), trains are processed in list order instead of a queue by remaining budget, cars are 7 world units apart with no model extents (car +0x38 and the model extents are not read), the front car is the reference car (the original's is the middle car, train +0x2c) and the braking car (train +8), every car counts as active (car flag bit 0 is not modelled; the sum skips inactive cars, the average divides by all), the half-sample point wraps to sample 0 instead of reading the duplicated slot n, a zero-length hop at zero speed counts as partial (the original's NaN hop time takes the full branch), a new train starts at its even share of the lap (the original spaces trains by the release time, 0x43c1f0) — evidence needed: DET-I2's substep clock, the car offsets from asCarTypes (0x43bc20), car flag bit 0 and the original's train placement
	public void Advance( double deltaSeconds )
	{
		if ( !(deltaSeconds > 0) )
			return;
		TimeSeconds += deltaSeconds;
		if ( !IsOperational || trains.Count == 0 )
			return;
		foreach ( var train in trains )
			Move( train, (float)deltaSeconds );
		foreach ( var train in trains )
			ClearHeadway( train );
		// [BIN:TPW-EXE:0x43b8b0 coaster station] the station step is skipped entirely in mode 64 (breakdown); an arriving train unloads open or closed and boarding needs state 4; the tick also skips the station step (and 0x439570) for a train whose flag bit 0x2 is clear (0x435ce5 test byte [esi],2; the train flag writers of bit 0x2 are not identified, the stride-0x60 car flags at 0x43d1ee/0x43d23e are another word), here every train counts as having it set
		if ( IsBroken || State is CoasterControllerState.Built )
			return;
		foreach ( var train in trains )
			StationStep( train );
	}

	/// <summary>The ride's state change (<c>0x435730</c>) with what it does to the trains and the rings; see <see cref="State"/>.</summary>
	internal void EnterState( CoasterControllerState next )
	{
		var old = State;
		switch ( (old, next) )
		{
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
	private void Move( CoasterTrain train, float budget )
	{
		var count = samples.Length;
		var cars = Math.Max( 1, train.Cars.Count );
		var minimumFloor = Definition.MinSpeed * PowerFactor;
		var friction = (SampleAt( train.Position ).Flags & CoasterSampleFlags.Friction) != 0;
		while ( true )
		{
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
				var at = position - car * CarSpacing / Spacing;
				var winch = winchSections.Contains( SampleAt( at + Hop ).Section );
				anyWinch |= winch;
				sum += CarSpeed( Definition, speed, SampleAt( at ).Position.Y, SampleAt( at + Hop ).Position.Y, winch ? winchFloor : minimumFloor );
				var wrapped = at < 0 ? at + count : at % count;
				pastBraking |= wrapped > BrakingStart;
			}
			var next = sum / cars;
			if ( pastBraking && (train.Flags & CoasterTrainFlags.Bumped) == 0 )
				next = BrakeBlend( next, winchFloor, position, BrakingStart, count );
			else if ( friction )
				next *= Definition.FrictionMultiplier;
			next = MathF.Max( next, anyWinch ? winchFloor : minimumFloor );
			next = MathF.Min( next, SpeedCap );
			var distance = System.Numerics.Vector3.Distance( SampleAt( position ).Position, SampleAt( position + Hop ).Position );
			var hopTime = speed + next > 0 ? distance / ((speed + next) * 0.5f) : float.PositiveInfinity;
			var done = false;
			if ( hopTime < budget )
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
				position = MathF.FusedMultiplyAdd( Hop, travelled, position );
				speed = SpeedCap < end ? SpeedCap : end;
				done = true;
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
			if ( done )
				break;
		}
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
		hash.Add( PaidCost );
		hash.Add( TimeSeconds );
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
		if ( saved.State is { } checkedState && !Enum.IsDefined( (CoasterControllerState)checkedState ) )
			throw new InvalidDataException( $"Coaster '{Name}' holds the controller state {checkedState}, which is not 1, 2, 4 or 8." );
		var migrated = Track.RestoreSnapshot( saved.Track );
		Initialised = saved.Initialised;
		// A save from before the controller state held only the closed flag: a finished track is open or closed, an unfinished one has no state to be in but 1.
		State = !Track.IsRunnable ? CoasterControllerState.Incomplete
			: saved.State is { } savedState ? (CoasterControllerState)savedState
			: saved.Closed ? CoasterControllerState.Closed : CoasterControllerState.Open;
		BreakdownState = saved.BreakdownState;
		SpeedSetting = saved.SpeedSetting;
		SeatsPerTrain = saved.SeatsPerTrain;
		SpeedCap = saved.SpeedCap;
		PaidCost = saved.PaidCost;
		TimeSeconds = saved.TimeSeconds;
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
		var olderShape = migrated || saved.Trains.Any( train => train.Flags is null );
		var matches = !migrated && saved.Trains.Count == trains.Count
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
		train.Position = saved.Position;
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

	/// <summary>The states of the stop-and-dispatch model saves made before the original's station rule hold: boarding at the station (0) and unloading (3); 1 ran and 2 waited before the station.</summary>
	internal const int LegacyBoardingState = 0;
	internal const int LegacyRunningState = 1;
	internal const int LegacyUnloadingState = 3;
}
