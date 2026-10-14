namespace OpenTPW;

public enum CoasterTrainState
{
	/// <summary>Stopped at the station, taking riders.</summary>
	Boarding,
	Running,
	/// <summary>Waiting before the station while another train uses it.</summary>
	Holding,
	/// <summary>Stopped at the station, letting riders off (one per <c>COAST 3</c>).</summary>
	Unloading
}

/// <summary>One train: its front's position along the track in samples, speed, state and the riders of each car.</summary>
public sealed class CoasterTrain
{
	private readonly List<int>[] cars;

	internal CoasterTrain( int index, int carCount )
	{
		Index = index;
		cars = new List<int>[Math.Max( 1, carCount )];
		for ( var car = 0; car < cars.Length; car++ )
			cars[car] = new List<int>();
	}

	public int Index { get; }
	/// <summary>The front car's position in samples from the station node, [0, sample count) (train <c>+116</c>).</summary>
	public float Position { get; internal set; }
	/// <summary>World units per second.</summary>
	public float Speed { get; internal set; }
	public CoasterTrainState State { get; internal set; }
	/// <summary>Park simulation time (seconds) when the train last stopped to board.</summary>
	public double BoardingSince { get; internal set; }
	public IReadOnlyList<IReadOnlyList<int>> Cars => cars;
	public int Riders => cars.Sum( car => car.Count );
	internal List<int>[] CarLists => cars;
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
	/// <summary>Shortest stop at the station before a train that is not full departs (COAST-023).</summary>
	public const double MinimumBoardingSeconds = 5;
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
	private CoasterSample[] samples = Array.Empty<CoasterSample>();
	private HashSet<int> winchSections = new();
	private double lastDispatch = double.NegativeInfinity;

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
	/// <summary><c>COAST 5</c>: the ride is closed (no boarding, no departures).</summary>
	public bool Closed { get; private set; } = true;
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
	/// <summary>Controller <c>+320</c>: riders on board, unloading riders included.</summary>
	public int RidersOnBoard => trains.Sum( train => train.Riders );
	/// <summary>Riders taken off a removed track or reopened ride, waiting for <c>COAST 3</c>.</summary>
	public int LeavingCount => leaving.Count;
	/// <summary>Park time of the last dispatch, or <c>-∞</c> while no train has ever been dispatched (park-resume capture).</summary>
	internal double LastDispatch => lastDispatch;
	/// <summary>The riders waiting for <c>COAST 3</c>, in departure order (park-resume capture).</summary>
	internal IReadOnlyCollection<int> LeavingRiders => leaving;
	public bool IsOperational => Track.IsRunnable && samples.Length >= 3;
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
		TrackChanged?.Invoke( this );
	}

	/// <summary>Curve, rating pass, winch marks, counted sections and braking start for the current speed cap; returns the train count.</summary>
	private int RateTrack()
	{
		var nodes = Track.Nodes;
		var sampling = Curve.Sample( Track.BuildKnots( Site ), Definition.Curviness );
		samples = sampling.Samples;
		Spacing = sampling.Spacing;
		var (result, winch) = CoasterRating.RatePass( Definition, samples, Spacing, SpeedCap, nodes.Count - 1 );
		winchSections = winch;
		for ( var index = 0; index < nodes.Count; index++ )
		{
			var node = nodes[index];
			node.Flags = winch.Contains( index ) ? node.Flags | CoasterNodeFlags.Winch : node.Flags & ~CoasterNodeFlags.Winch;
		}
		// Controller +264: the sections with both node flags 0x40 and 0x80 (CoasterRating.FinalFor; ORACLE F8).
		var counted = CoasterRating.FinalFor( 0, 0, 0, 0, nodes.Select( node => (uint)node.Flags ), 0, 0, 0, 0 ).Sections;
		Rating = result with { CountedSections = counted };
		BrakingStart = BrakingStartFor( samples.Length, samples[BrakingSpeedSample( samples, nodes.Count - 1 )].Speed, Spacing );
		return result.Trains;
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
	private void PlaceTrains( int count )
	{
		trains.Clear();
		for ( var index = 0; index < count; index++ )
		{
			var train = new CoasterTrain( index, Definition.MaxCars );
			if ( index == 0 )
			{
				train.State = CoasterTrainState.Boarding;
				train.BoardingSince = TimeSeconds;
			}
			else
			{
				train.Position = (float)samples.Length * index / count;
				train.Speed = SampleAt( train.Position ).Speed;
				train.State = CoasterTrainState.Running;
			}
			trains.Add( train );
		}
	}

	/// <summary>Takes the trains off the track; their riders wait for <c>COAST 3</c> to leave.</summary>
	internal void ClearTrains()
	{
		foreach ( var train in trains )
		{
			foreach ( var car in train.CarLists )
			{
				foreach ( var guest in car )
					leaving.Enqueue( guest );
				car.Clear();
			}
		}
		trains.Clear();
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
					BreakdownState = parameter;
				return 0;
			case 5:
				// [BIN:STP-PPC:0x1003DEC8 coaster close] COAST 5 closes (nonzero) or opens the controller
				// [BIN:TPW-EXE:0x43b2f0 coaster close] gated: argument 0 opens (state → 4) only when state & 0xA (closed 8 or incomplete 2), a nonzero argument closes (→ 8) only when state & 4 (open), like the Mac; the bool Closed is state 8/2 versus 4, so each branch changes nothing when already there. Closing (4 → 8, 0x43b390) moves the admission ring (+0xB0) into the leaving ring (+0xD0) that COAST 3 (0x43b220) pops; OpenTPW has no admission ring (a guest is seated at once, see Board), so nothing waits to be released
				if ( parameter == 0 )
				{
					if ( Closed )
						Closed = false;
				}
				else if ( !Closed )
					Closed = true;
				return 0;
			case 6:
				// [BIN:STP-PPC:0x1003DF24 coaster capacity] COAST 6 takes MAX with the global value, then MIN with the definition and train bounds
				// [BIN:TPW-EXE:0x43b330 coaster capacity] min(max(request, 0), definition +0x318 (the fleet's seat socket total), the first train definition's +8); the lower bound is the literal 0 where the Mac reading takes a global (documented difference); a changed value is rebuilt through 0x43ba90 (seat allocation, not modelled)
				// [APPROX:COAST-023] a train seats COAST 6's capacity (at least 0; uiMaxCars × 4 before the script sets it), split evenly over its cars; the two upper bounds (definition +0x318 and the first train definition's +8) are not applied because OpenTPW does not read the seat sockets or that train field; a train departs at speed 0 when the release headway has passed since the last departure and it is full or has boarded for 5 s; an arriving train waits before the station while another train uses it; running trains do not collide — evidence needed: the tick 0x10040650's dispatch branches and the seat socket count per car (definition +0x318, train definition +8)
				SeatsPerTrain = Math.Max( 0, parameter );
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
				foreach ( var car in train.CarLists )
				{
					foreach ( var guest in car )
						leaving.Enqueue( guest );
					car.Clear();
				}
			PlaceTrains( count );
		}
		TrackChanged?.Invoke( this );
	}

	/// <summary>Free seats on the boarding train, 0 while closed, broken, not runnable or with no train at the station.</summary>
	public int Room
	{
		get
		{
			if ( !IsOperational || Closed || IsBroken || BoardingTrain is not { } train )
				return 0;
			return Math.Max( 0, SeatsPerTrain - train.Riders );
		}
	}

	private CoasterTrain? BoardingTrain => trains.FirstOrDefault( train => train.State == CoasterTrainState.Boarding );

	/// <summary>Seats a guest on the boarding train in a random car with room (<c>CoasterRng</c>); a guest without a seat leaves again.</summary>
	// [BIN:STP-PPC:0x1003E2F8 boarding] the pending ring's read cursor (+196) is popped and the visitor is placed in a randomly chosen car (s·214013 + 2531011)
	internal void Board( int guest )
	{
		if ( guest <= 0 )
			return;
		if ( Room == 0 || BoardingTrain is not { } train )
		{
			leaving.Enqueue( guest );
			return;
		}
		var cars = train.CarLists;
		var seatsPerCar = (SeatsPerTrain + cars.Length - 1) / cars.Length;
		var first = World.Random.Next() % cars.Length;
		for ( var offset = 0; offset < cars.Length; offset++ )
		{
			var car = cars[(first + offset) % cars.Length];
			if ( car.Count < seatsPerCar )
			{
				car.Add( guest );
				return;
			}
		}
		cars[first].Add( guest );
	}

	internal int TakeLeavingRider()
	{
		if ( leaving.Count > 0 )
			return leaving.Dequeue();
		foreach ( var train in trains )
		{
			if ( train.State != CoasterTrainState.Unloading )
				continue;
			foreach ( var car in train.CarLists )
			{
				if ( car.Count == 0 )
					continue;
				var guest = car[0];
				car.RemoveAt( 0 );
				return guest;
			}
		}
		return 0;
	}

	// ---- Motion -------------------------------------------------------------------------------------

	/// <summary>
	/// One fixed simulation step: station work (unloading, boarding, dispatch at the release headway), then each running
	/// train's half-sample hops (<c>0x40650</c>) within the step's time budget.
	/// </summary>
	// [APPROX:COAST-022] the hops run on OpenTPW's fixed 60 Hz simulation step instead of the elapsed milliseconds of the cached animation clock per frame (0x100A3E08, controller +288), trains are processed in list order instead of a queue by remaining budget, cars are 7 world units apart (car +56 not read), the front car is the reference car (train +44) and the braking car (train +8), station braking is tested on the front car only (train flag 0x40 is not modelled), the winch floor is never halved (0x100408E4 halves it for trains with flags 0x80 and 0x20; 0x80 is set on leaving mode 64 by 0x10038250, 0x20 after seat allocation at 0x1003E9CC and cleared at 0x1003E7E4, and neither train flag is modelled), every car counts as active (car flag bit 0 is not modelled; the sum skips inactive cars, the average divides by all), the half-sample point wraps to sample 0 instead of reading the duplicated slot n, and a zero-length hop at zero speed counts as partial (the original's NaN hop time takes the full branch) — evidence needed: DET-I2's substep clock, the car offsets from asCarTypes, the train flag 0x20/0x40/0x80 lifecycle in 0x1003E138..0x1003E9E8 and 0x10038250, and car flag bit 0
	public void Advance( double deltaSeconds )
	{
		if ( !(deltaSeconds > 0) )
			return;
		TimeSeconds += deltaSeconds;
		if ( !IsOperational || trains.Count == 0 )
			return;
		foreach ( var train in trains )
		{
			// [BIN:TPW-EXE:0x43b8b0 coaster station] the station step is skipped entirely in mode 64 (breakdown) — and in controller state 2, which OpenTPW does not model; an arriving train unloads open or closed and boarding needs state 4
			if ( IsBroken && train.State != CoasterTrainState.Running )
				continue;
			switch ( train.State )
			{
				case CoasterTrainState.Unloading:
					if ( train.Riders == 0 )
					{
						train.State = CoasterTrainState.Boarding;
						train.BoardingSince = TimeSeconds;
					}
					break;
				case CoasterTrainState.Boarding:
					if ( CanDispatch( train ) )
					{
						train.State = CoasterTrainState.Running;
						train.Speed = 0;
						lastDispatch = TimeSeconds;
					}
					break;
				case CoasterTrainState.Holding:
					if ( !StationBusy( train ) )
						Arrive( train );
					break;
				case CoasterTrainState.Running:
					Move( train, (float)deltaSeconds );
					break;
			}
		}
	}

	// [APPROX:COAST-012] departures stop while the ride is closed or broken (the original tests boarding only in state 4, 0x43b7e0/0x43b600; whether a full or boarded train still departs while closed is the departure gating of the station step, not traced), and the writers of controller states 1 and 2 (callers of the mode setter 0x435730: 0x435570, 0x4368f0, 0x437b60, 0x442c70, 0x446740, 0x447520) are not traced; COAST 4 (0x43b270: 0/1/2 → 16/64/32), the gated COAST 5, mode 32 treated exactly like 16 by the setter (nothing tests it) and the skipped station step in mode 64 are the traced PC rules (COAST 4 argument 1, mode 64, power factor 0 so moving trains coast to a stop)
	private bool CanDispatch( CoasterTrain train )
	{
		if ( !Initialised || Closed || IsBroken || PowerFactor == 0 )
			return false;
		if ( (TimeSeconds - lastDispatch) * 1000 < Definition.TrainReleaseRate )
			return false;
		return train.Riders >= SeatsPerTrain || TimeSeconds - train.BoardingSince >= MinimumBoardingSeconds;
	}

	private bool StationBusy( CoasterTrain except ) =>
		trains.Any( train => train != except && train.State is CoasterTrainState.Boarding or CoasterTrainState.Unloading );

	private void Arrive( CoasterTrain train )
	{
		train.Position = 0;
		train.Speed = 0;
		train.State = train.Riders > 0 ? CoasterTrainState.Unloading : CoasterTrainState.Boarding;
		train.BoardingSince = TimeSeconds;
	}

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
	/// The hops of one frame for a running train: per hop the cars' average candidate speed, station braking (past
	/// <see cref="BrakingStart"/>) or friction, the winch or minimum floor, the cap; the hop takes d/((v + v′)/2) seconds;
	/// the last hop of the budget is partial; after each hop the speed decays below 2.0 and stops below 0.01.
	/// </summary>
	// [BIN:STP-PPC:0x10040650 coaster tick] hops of half a sample: per car the winch test reads the section of the sample at trunc(s + ½) (0x1004098C, 0x10040A0C), the average car speed, braking when a car is past ctl+308 (strictly) or friction on the flag-8 sample of the reference car's +64 position, which is written only after the frame's hops (0x10041094), so it is fixed per frame; max(·, W) when any car is on a winch section else max(·, M), min(·, ctl+52); τ = d/((v + v')/2) from the reference car's distance to its half-sample point (0x1004316C); a full hop if τ < budget, else α = budget/τ, v_end = (1 − α)v + αv', s += ½·((v + v_end)/2·budget/d), v = min(cap, v_end) with no floor: 0x10040D98 stores max(v_end, M), but 0x10040D9C..0x10040DC8 reload the unfloored v_end and overwrite that store; after every hop v ×= 0.95 below 2.0 and v = 0 below 0.01; s wraps by fmod
	private void Move( CoasterTrain train, float budget )
	{
		var count = samples.Length;
		var cars = Math.Max( 1, train.Cars.Count );
		var winchFloor = Definition.WinchSpeed * PowerFactor;
		var minimumFloor = Definition.MinSpeed * PowerFactor;
		var position = train.Position;
		var speed = train.Speed;
		var friction = (SampleAt( position ).Flags & CoasterSampleFlags.Friction) != 0;
		while ( true )
		{
			var sum = 0f;
			var anyWinch = false;
			for ( var car = 0; car < cars; car++ )
			{
				var at = position - car * CarSpacing / Spacing;
				var winch = winchSections.Contains( SampleAt( at + Hop ).Section );
				anyWinch |= winch;
				sum += CarSpeed( Definition, speed, SampleAt( at ).Position.Y, SampleAt( at + Hop ).Position.Y, winch ? winchFloor : minimumFloor );
			}
			var next = sum / cars;
			if ( position > BrakingStart && BrakingStart < count )
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
			if ( position >= count )
			{
				if ( StationBusy( train ) )
				{
					train.Position = count - Hop / 2;
					train.Speed = 0;
					train.State = CoasterTrainState.Holding;
				}
				else
					Arrive( train );
				return;
			}
			if ( done )
				break;
		}
		train.Position = position;
		train.Speed = speed;
	}

	// ---- Canonical state -----------------------------------------------------------------------------

	internal void AddCanonicalState( StateHasher hash )
	{
		Track.AddCanonicalState( hash );
		hash.Add( Initialised );
		hash.Add( Closed );
		hash.Add( BreakdownState );
		hash.Add( SpeedSetting );
		hash.Add( SeatsPerTrain );
		hash.Add( SpeedCap );
		hash.Add( PaidCost );
		hash.Add( TimeSeconds );
		hash.Add( lastDispatch );
		hash.Add( BrakingStart );
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
			hash.Add( (int)train.State );
			hash.Add( train.BoardingSince );
			foreach ( var car in train.Cars )
			{
				hash.Add( car.Count );
				foreach ( var guest in car )
					hash.Add( guest );
			}
		}
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
		Track.RestoreSnapshot( saved.Track );
		Initialised = saved.Initialised;
		Closed = saved.Closed;
		BreakdownState = saved.BreakdownState;
		SpeedSetting = saved.SpeedSetting;
		SeatsPerTrain = saved.SeatsPerTrain;
		SpeedCap = saved.SpeedCap;
		PaidCost = saved.PaidCost;
		TimeSeconds = saved.TimeSeconds;
		lastDispatch = saved.LastDispatch ?? double.NegativeInfinity;
		if ( Track.IsRunnable )
			Rebuild();
		else
		{
			// A track that never completed holds no samples and no trains; its riders (if any) wait to leave.
			if ( trains.Count != 0 )
				ClearTrains();
			RaiseTrackChanged();
		}
		if ( saved.Trains.Count != trains.Count )
			throw new InvalidDataException( $"Coaster '{Name}' holds {saved.Trains.Count} trains in the save, but the restored track places {trains.Count}." );
		for ( var index = 0; index < trains.Count; index++ )
			RestoreTrain( index, saved.Trains[index] );
		leaving.Clear();
		foreach ( var guest in saved.Leaving )
			leaving.Enqueue( guest );
	}

	private void RestoreTrain( int index, CoasterTrainSaveData saved )
	{
		var train = trains[index];
		if ( saved.Cars.Count != train.Cars.Count )
			throw new InvalidDataException( $"Coaster '{Name}' train {index} holds {saved.Cars.Count} cars in the save, the definition gives {train.Cars.Count}." );
		train.Position = saved.Position;
		train.Speed = saved.Speed;
		train.State = (CoasterTrainState)saved.State;
		train.BoardingSince = saved.BoardingSince;
		for ( var car = 0; car < train.CarLists.Length; car++ )
		{
			train.CarLists[car].Clear();
			train.CarLists[car].AddRange( saved.Cars[car] );
		}
	}
}
