namespace OpenTPW;

/// <summary>
/// The bump-ride controller of one kart or water ride (original: 0xD0 bytes, up to 64 in a module-wide table): the circuit's sections,
/// collision objects and buoys, the guests waiting to board and to leave, and the vehicles. It runs the module's integer physics at 30
/// ticks per second and answers the ride script's <c>BUMP</c> commands. This slice runs water boats (types −5, −8, −12, −13); the kart
/// tables and templates are shared, the kart grid, race and bumper-car modes are not implemented.
/// </summary>
// [APPROX:TRACK-015] kart rides (Bumper.WhichTrackType 1, controller types −4, −7, −9, −10) have the shared piece tables and templates but no controller: the grid start, race laps, followers, 8-vehicle limit and kart sounds are not implemented, so a kart ride runs its script without a track — evidence needed: the kart branches of 0x005474B0, 0x0054A040 and 0x00545890 exercised on a captured race
// [APPROX:TRACK-019] the controller's collision objects, buoys and boats are its own: the original's pools (2048 objects, 1024 buoys, 256 boats) are module-wide, so boats of two rides could meet and one ride's object search could find another's; separate rides sit on separate cells, so this only differs for overlapping circuits — evidence needed: whether the module-wide object scan crosses controllers

public sealed class BumpController
{
	/// <summary>Physics ticks per second: the controller's duration unit is 30 ticks per second (BUMP 13 stores seconds × 30).</summary>
	// [BIN:TPW-EXE:0x00554980 BUMP 13] BUMP 13 stores parameter × 30 and BUMP 14 −parameter into controller +4 through the duration setter 0x00545100, so durations count 30 ticks per second
	// [APPROX:TRACK-011] the controller ticks at 30 Hz, every second fixed step; the module tick (0x0054E360) and its caller's rate are not traced — evidence needed: the update loop that calls 0x00546C80 and its timestep
	public const int TicksPerSecond = 30;
	public const double TickSeconds = 1.0 / TicksPerSecond;
	public const int MaximumVehicles = 64;
	public const int VehiclePool = 256;
	/// <summary>Bound on the guests that wait in one pending or leaving list (the original's free peep list is shared by all rides).</summary>
	public const int MaximumQueuedGuests = 512;

	// Controller phase (+0x50): 0 closed, 1 loading (kart open), 2 running (water open), 3 race over.
	public const int PhaseClosed = 0;
	public const int PhaseLoading = 1;
	public const int PhaseRunning = 2;
	public const int PhaseOver = 3;
	// Controller state (+0x54): 0 fine, 1 worn out, 2 broken.
	public const int StateFine = 0;
	public const int StateWorn = 1;
	public const int StateBroken = 2;

	private readonly Func<uint> worldRandom;
	private readonly BumpWorld? world;
	private readonly BumpVehicle?[] slots = new BumpVehicle?[VehiclePool];
	private readonly List<BumpVehicle> vehicles = new();
	private List<TrackSection> sections = new();
	private List<CollisionObject> objects = new();
	private List<Buoy> buoys = new();
	private double tickRemainder;

	/// <param name="template">The controller type's template.</param>
	/// <param name="worldRandom">The world generator the aiming jitter draws from (the original's 0x516330).</param>
	/// <param name="world">The module state shared by the park's controllers (cosmetic generator), or null for a controller of its own.</param>
	public BumpController( BumperTemplate template, Func<uint> worldRandom, BumpWorld? world = null )
	{
		Template = template ?? throw new ArgumentNullException( nameof( template ) );
		this.worldRandom = worldRandom ?? throw new ArgumentNullException( nameof( worldRandom ) );
		this.world = world;
		Laps = template.Laps;
		Performance = template.DefaultPerformance;
		ApplyPerformance();
	}

	public BumperTemplate Template { get; }
	public int Type => Template.Type;
	public bool IsWater => Template.IsWater;
	/// <summary>+4: laps to go (negative) or a duration in ticks.</summary>
	public int Laps { get; internal set; }
	/// <summary>+0x1C: the performance pushed from the script's speed bias, 0..100.</summary>
	public int Performance { get; private set; }
	/// <summary>+0x20: thrust per tick at the current performance.</summary>
	public int Accel { get; private set; }
	/// <summary>+0x24: velocity factor per 1024 per tick.</summary>
	public int Damping { get; private set; }
	/// <summary>+0x28: heading change per tick.</summary>
	public int TurnRate { get; private set; }
	/// <summary>+0x2C: bounce per 1024.</summary>
	public int Restitution { get; private set; }
	public int Phase { get; internal set; }
	public int State { get; internal set; }
	public int VehicleCount { get; private set; }
	public int RiderCount { get; private set; }
	/// <summary>The station reference (+0x74/+0x78): where boats launch.</summary>
	public int StationX { get; private set; }
	public int StationY { get; private set; }
	/// <summary>The unload point (+0x90/+0x94).</summary>
	public int UnloadX { get; private set; }
	public int UnloadY { get; private set; }
	/// <summary>The orientation word (+0xB0): bit 1 the axis, bits 2 and 4 the signs.</summary>
	public int Orientation { get; private set; }
	/// <summary>The heading bit the station's flow runs along.</summary>
	public byte StationHeading { get; private set; }
	/// <summary>The attraction's track-valid flag (+0x2C on the PC, <c>mIsTrackRideValid</c>), written by the circuit walk.</summary>
	// [BIN:TPW-EXE:0x00524960 track validity] the flag is the circuit walk's verdict, rewritten after every track edit (label 0x75B538 at 0x4DBE00); BUMP 5 reads it into the branch accumulator
	public bool IsTrackValid { get; private set; }
	/// <summary>The water pieces' animation state set by BUMP 17: true while they run (animation 5), false when stopped (animation 12).</summary>
	public bool WaterRunning { get; private set; } = true;
	/// <summary>The module tick counter (the original's global at 0x877B5C, gating the cosmetic effects every 32nd tick).</summary>
	public long TickCount { get; internal set; }
	public IReadOnlyList<TrackSection> Sections => sections;
	public IReadOnlyList<CollisionObject> Objects => objects;
	public IReadOnlyList<Buoy> Buoys => buoys;
	/// <summary>The buoy after the finish buoy (the first section buoy), where a launched boat aims first (+0xA4).</summary>
	public Buoy? FirstAfterFinish { get; private set; }
	public IReadOnlyList<BumpVehicle> Vehicles => vehicles;
	/// <summary>Guests added by BUMP 1 who ride with the next boat, newest first (+0xC4).</summary>
	public List<int> Pending { get; } = new();
	/// <summary>Guests who left a boat, handed to the script one at a time by BUMP 2, newest first (+0xC8).</summary>
	public List<int> Leaving { get; } = new();
	/// <summary>The cosmetic spray events of all boats (counter only; no particles are spawned).</summary>
	public int Sprays { get; private set; }
	/// <summary>Laps completed by boats that ended their run (statistics).</summary>
	public int CompletedRuns { get; private set; }

	// ---- Setup ------------------------------------------------------------------------------------

	/// <summary>Places the station reference at (<paramref name="stationX"/>, <paramref name="stationY"/>) in section units and orients the flow along <paramref name="heading"/>.</summary>
	public void SetStation( int stationX, int stationY, byte heading )
	{
		StationX = stationX;
		StationY = stationY;
		StationHeading = heading;
		Orientation = TrackSections.OrientationOf( heading );
		if ( IsWater )
		{
			var (finish, unloadX, unloadY) = TrackSections.WaterStationBuoys( stationX, stationY, heading );
			UnloadX = unloadX;
			UnloadY = unloadY;
			stationBuoys = new[] { finish };
		}
		RebuildStationOnly();
	}

	private IReadOnlyList<Buoy> stationBuoys = Array.Empty<Buoy>();

	private void RebuildStationOnly()
	{
		var result = TrackSections.Build( Array.Empty<TrackStep>(), Template.Slack, CloneStation() );
		Adopt( result );
	}

	private IReadOnlyList<Buoy> CloneStation() => stationBuoys.Select( buoy => new Buoy { Flags = buoy.Flags, X = buoy.X, Y = buoy.Y } ).ToArray();

	private void Adopt( TrackSections.Result result )
	{
		sections = result.Sections.ToList();
		objects = result.Objects.ToList();
		buoys = result.Buoys.ToList();
		FirstAfterFinish = FindFirstAfterFinish();
	}

	/// <summary>
	/// Replaces the circuit after a walk: the boats leave the ride, the ride closes, the old sections and section buoys go, and the new
	/// circuit's sections, collision objects and buoys are built. The validity flag is the walk's verdict.
	/// </summary>
	// [BIN:TPW-EXE:0x00545450 section reset] a re-walk first unloads every vehicle (0x0054AE50) and closes the ride (0x00544A10), then frees the sections, their collision objects and the buoys that are not station buoys (flags 2 or 4)
	public void Rebuild( TrackCircuit circuit )
	{
		ArgumentNullException.ThrowIfNull( circuit );
		EjectAll();
		Close();
		IsTrackValid = circuit.IsValid;
		var result = TrackSections.Build( circuit.IsValid ? circuit.Steps : Array.Empty<TrackStep>(), Template.Slack, CloneStation() );
		Adopt( result );
		foreach ( var vehicle in vehicles )
			vehicle.ObjectIndex = -1;
	}

	private Buoy? FindFirstAfterFinish()
	{
		var finish = buoys.FirstOrDefault( buoy => buoy.IsFinish );
		if ( finish == null )
			return null;
		return NextBuoy( finish );
	}

	/// <summary>The buoy after <paramref name="buoy"/> in list order, wrapping to the head, skipping grid-slot buoys.</summary>
	public Buoy NextBuoy( Buoy buoy )
	{
		var next = buoy;
		do
		{
			next = next.Index + 1 < buoys.Count ? buoys[next.Index + 1] : buoys[0];
		}
		while ( next.IsSkipped && next != buoy );
		return next;
	}

	/// <summary>Sets the performance (0..100, clamped) and the thrust, damping, turn rate and restitution it interpolates.</summary>
	// [BIN:TPW-EXE:0x00545180 controller performance] p = clamp(0, 100); each of the four values is min + (max − min) × p / 100 with truncating integer division
	public void SetPerformance( int performance )
	{
		Performance = Math.Clamp( performance, 0, 100 );
		ApplyPerformance();
	}

	private void ApplyPerformance()
	{
		var p = Performance;
		var t = Template;
		Accel = (t.Accel.Max - t.Accel.Min) * p / 100 + t.Accel.Min;
		Damping = (t.Damping.Max - t.Damping.Min) * p / 100 + t.Damping.Min;
		TurnRate = (t.Turn.Max - t.Turn.Min) * p / 100 + t.Turn.Min;
		Restitution = (t.Restitution.Max - t.Restitution.Min) * p / 100 + t.Restitution.Min;
	}

	// ---- BUMP commands -----------------------------------------------------------------------------

	/// <summary>
	/// A <c>BUMP</c> command of the ride script (command and parameter already resolved); returns the result the query commands put in the
	/// accumulator. Command numbers are the raw RSE ones: 1 add a guest to the next boat, 2 take a guest who left, 3 halt (karts), 4 launch a boat,
	/// 5 track valid, 6 close, 7 open, 8 broken, 9 worn, 10 close and eject, 11 boats running, 12 load the waiting guests, 13 and 14 duration or laps,
	/// 16 unload one boat, 17 water pieces.
	/// </summary>
	public int Command( int command, int parameter )
	{
		switch ( command )
		{
			case 1:
				return AddGuest( parameter ) ? 1 : 0;
			case 2:
				return TakeLeavingGuest();
			case 3:
				Halt();
				return 0;
			case 4:
				return Launch() ? 1 : 0;
			case 5:
				return IsTrackValid ? 1 : 0;
			case 6:
				Close();
				return 0;
			case 7:
				Open();
				return 0;
			case 8:
				// [BIN:TPW-EXE:0x00554850 BUMP 8] a nonzero parameter breaks the ride (state 2), zero fixes it (state 0)
				if ( parameter != 0 )
					State = StateBroken;
				else
					State = StateFine;
				return IsTrackValid ? 1 : 0;
			case 9:
				// [BIN:TPW-EXE:0x00554895 BUMP 9] a nonzero parameter marks the ride worn out (state 1), zero fixes it (state 0)
				// [APPROX:TRACK-013] BUMP 9 with a nonzero parameter sets the worn state even over a break, as the traced routine does; what the worn state changes for water boats is not traced (the integration reads it for kart sounds only) — evidence needed: the readers of controller +0x54 equal to 1
				State = parameter != 0 ? StateWorn : StateFine;
				return 0;
			case 10:
				EjectAll();
				Close();
				return 0;
			case 11:
				return VehicleCount;
			case 12:
				return LoadWaiting() ? 1 : 0;
			case 13:
				// [BIN:TPW-EXE:0x00554980 BUMP 13] the duration setter 0x00545100 stores parameter × 30 in controller +4 (BUMP 14 stores −parameter)
				Laps = parameter * 30;
				return parameter;
			case 14:
				Laps = -parameter;
				return parameter;
			case 16:
				return UnloadOne() ? 1 : 0;
			case 17:
				SetWater( parameter == 0 );
				return 0;
			default:
				return 0;
		}
	}

	/// <summary>BUMP 1: adds a guest to the next boat; refused while the ride is closed or the list is full.</summary>
	// [BIN:TPW-EXE:0x0054AA80 peep add] needs a nonzero phase and a free peep node; the guest is put at the front of the pending list (+0xC4) and 1 is returned
	public bool AddGuest( int guest )
	{
		if ( Phase == PhaseClosed || Pending.Count + Leaving.Count >= MaximumQueuedGuests )
			return false;
		Pending.Insert( 0, guest );
		return true;
	}

	/// <summary>BUMP 2: the next guest who left a boat (0 when none).</summary>
	// [BIN:TPW-EXE:0x0054AB40 peep out] pops the head of the leaving list (+0xC8) and returns its guest id, 0 for an empty list
	public int TakeLeavingGuest()
	{
		if ( Leaving.Count == 0 )
			return 0;
		var guest = Leaving[0];
		Leaving.RemoveAt( 0 );
		return guest;
	}

	/// <summary>BUMP 7: opens a closed ride; water boats run at once (phase 2) and aim first at the buoy after the finish buoy.</summary>
	// [BIN:TPW-EXE:0x00544840 open ride] water: only while closed, FirstAfterFinish = the buoy after the finish buoy (skipping flag 2), phase 2, then the performance is pushed; karts use phase 1
	public void Open()
	{
		if ( Phase != PhaseClosed )
			return;
		FirstAfterFinish = FindFirstAfterFinish();
		Phase = IsWater ? PhaseRunning : PhaseLoading;
		ApplyPerformance();
	}

	/// <summary>BUMP 6: closes the ride; the waiting guests go to the leaving list.</summary>
	// [BIN:TPW-EXE:0x00544A10 close ride] phase 0 and the pending list moves behind the leaving list; kart vehicles turn to the unload point at once, water boats finish their run
	public void Close()
	{
		if ( Phase == PhaseClosed )
			return;
		Phase = PhaseClosed;
		Leaving.AddRange( Pending );
		Pending.Clear();
	}

	/// <summary>BUMP 3: the kart start routine; water rides ignore it.</summary>
	// [BIN:TPW-EXE:0x00544F90 start bump ride] not for the water types: karts go from phase 1 to phase 2 and every vehicle is relaunched
	public void Halt()
	{
	}

	private void SetWater( bool running )
	{
		// [BIN:TPW-EXE:0x0052A490 water pieces] BUMP 17 walks the circuit from both station sides and triggers animation 5 (parameter 0) or 12 (nonzero) on channel 7 of every piece; the stored value (script +0xE6) makes a repeated value a no-op
		WaterRunning = running;
	}

	// ---- Vehicles ----------------------------------------------------------------------------------

	private int FreeSlot()
	{
		for ( var index = 0; index < slots.Length; index++ )
		{
			if ( slots[index] == null )
				return index;
		}
		return -1;
	}

	/// <summary>BUMP 4: launches a boat at the station with the waiting guests aboard.</summary>
	// [BIN:TPW-EXE:0x00549DB0 create vehicle] needs a running count below the type's maximum and 64, and a nonzero phase; the boat starts at the station reference with the controller's laps, takes the pending guests and is launched (0x0054A040)
	public bool Launch()
	{
		if ( VehicleCount >= Template.MaxVehicles || VehicleCount >= MaximumVehicles || Phase == PhaseClosed )
			return false;
		var slot = FreeSlot();
		if ( slot < 0 )
			return false;
		var vehicle = new BumpVehicle { Slot = slot, Flags = BumpVehicle.Live, X = StationX, Y = StationY, Radius = Template.VehicleRadius };
		slots[slot] = vehicle;
		InsertSorted( vehicle );
		vehicle.Flags |= BumpVehicle.Steerable;
		VehicleCount++;
		vehicle.Laps = Laps;
		vehicle.ObjectIndex = FirstContaining( StationX, StationY )?.Index ?? -1;
		vehicle.Riders.AddRange( Pending );
		Pending.Clear();
		vehicle.Flags |= BumpVehicle.FirstLaunch;
		vehicle.Variant = VehicleCount % Template.ModelCount + Template.ModelBase;
		// [APPROX:TRACK-012] a boat starts heading along the station's flow; the original's launch leaves the heading at 0 (+Y) for water and the first steering turns it — evidence needed: the heading a launched boat shows before its first turn
		vehicle.Heading = HeadingOfStation();
		RiderCount += vehicle.Riders.Count;
		Relaunch( vehicle );
		vehicle.Flags |= BumpVehicle.Launched;
		return true;
	}

	private void InsertSorted( BumpVehicle vehicle )
	{
		var index = vehicles.FindIndex( other => other.Slot > vehicle.Slot );
		if ( index < 0 )
			vehicles.Add( vehicle );
		else
			vehicles.Insert( index, vehicle );
	}

	private int HeadingOfStation() => StationHeading switch
	{
		TrackHeading.PosY => 0,
		TrackHeading.PosX => 128,
		TrackHeading.NegY => 256,
		_ => 384
	};

	/// <summary>BUMP 12: puts the waiting guests into the first boat that has none, then relaunches it.</summary>
	// [BIN:TPW-EXE:0x00549B80 load vehicle] the first live vehicle of the controller without riders takes the pending guests, gets flags 0x84000 and the controller's laps, and is relaunched
	public bool LoadWaiting()
	{
		if ( Phase == PhaseClosed )
			return false;
		var vehicle = vehicles.FirstOrDefault( item => item.Riders.Count == 0 );
		if ( vehicle == null )
			return false;
		vehicle.Riders.AddRange( Pending );
		RiderCount += Pending.Count;
		Pending.Clear();
		vehicle.Flags |= BumpVehicle.Steerable | BumpVehicle.Slowed;
		vehicle.Laps = Laps;
		Relaunch( vehicle );
		return true;
	}

	/// <summary>BUMP 16: unloads the first boat without guests, else the first boat.</summary>
	// [BIN:TPW-EXE:0x0054AD90 unload vehicle] with the flag set only a vehicle without riders is unloaded; without a match the routine runs again without the flag
	public bool UnloadOne()
	{
		var vehicle = vehicles.FirstOrDefault( item => item.Riders.Count == 0 ) ?? vehicles.FirstOrDefault();
		if ( vehicle == null )
			return false;
		Retire( vehicle );
		return true;
	}

	/// <summary>Every boat leaves the ride; its guests go to the leaving list.</summary>
	public void EjectAll()
	{
		foreach ( var vehicle in vehicles.ToArray() )
			Retire( vehicle );
	}

	/// <summary>Removes a boat: its guests go to the front of the leaving list one by one and the boat count falls.</summary>
	// [BIN:TPW-EXE:0x0054AE50 retire vehicle] the riders move to the leaving list (0x0054AC70), the vehicle is freed and the count falls; water rides keep their phase when it reaches 0 (karts reset to loading)
	public void Retire( BumpVehicle vehicle )
	{
		foreach ( var guest in vehicle.Riders )
		{
			Leaving.Insert( 0, guest );
			RiderCount--;
		}
		vehicle.Riders.Clear();
		vehicle.Flags &= ~BumpVehicle.Live;
		slots[vehicle.Slot] = null;
		vehicles.Remove( vehicle );
		VehicleCount--;
		if ( VehicleCount == 0 && !IsWater )
			Phase = PhaseLoading;
	}

	/// <summary>The water relaunch: the boat aims at the first section buoy again in lap mode.</summary>
	// [BIN:TPW-EXE:0x0054A040 relaunch] water (types −5, −8, −12, −13): buoy countdown 0, flags keep only bit 0, 7 and 14, then lap mode with a new aim (0x10008); a first launch aims at controller +0xA4 and clears the accel bonus
	private void Relaunch( BumpVehicle vehicle )
	{
		vehicle.BuoyCountdown = 0;
		vehicle.Flags &= unchecked((int)0xFFFBFF81);
		if ( (vehicle.Flags & BumpVehicle.FirstLaunch) != 0 )
			vehicle.TargetBuoy = FirstAfterFinish?.Index ?? -1;
		vehicle.Flags |= BumpVehicle.Retarget | BumpVehicle.LapMode;
		if ( vehicle.TargetBuoy < 0 || vehicle.TargetBuoy >= buoys.Count )
			vehicle.TargetBuoy = FirstAfterFinish?.Index ?? -1;
		vehicle.BuoyCountdown = 0;
		vehicle.AccelBonus = 0;
		vehicle.Flags &= ~BumpVehicle.FirstLaunch;
	}

	// ---- Statistics --------------------------------------------------------------------------------

	/// <summary>The piece counts of the circuit the ride's excitement reads: corners, the longest run of straights and crossings, crossings, add-on straights and sections.</summary>
	// [BIN:TPW-EXE:0x00545310 section counts] two passes over the section list (so a run wraps from the end to the start): kinds 9–13 and 11 extend the run, a corner (kinds 5–8) ends it and counts; crossings count separately; add-on straights without a run counter count apart; every count is halved for the doubled pass except the longest run
	public (int Corners, int LongestRun, int Crossings, int AddOns, int Sections) SectionStatistics()
	{
		int run = 0, longest = 0, corners = 0, crossings = 0, addOns = 0, total = 0;
		for ( var pass = 0; pass < 2; pass++ )
		{
			foreach ( var section in sections )
			{
				switch ( section.Kind )
				{
					case 11:
						run++;
						crossings++;
						break;
					case 12:
					case 13:
						addOns++;
						run++;
						break;
					case 9:
					case 10:
						run++;
						break;
					default:
						longest = Math.Max( longest, run );
						run = 0;
						corners++;
						break;
				}
				total++;
			}
		}
		return (corners / 2, longest, crossings / 2, addOns, total / 2);
	}

	/// <summary>
	/// The excitement base of a ride with a controller: 60 % of the data level plus 2 per corner, 1 per straight in the longest run and 3 per crossing
	/// (that sum capped at 40), all clamped to 0..100; the speed and duration ratios scale it afterwards.
	/// </summary>
	// [BIN:TPW-EXE:0x004E0560 live excitement] for a ride whose thing +0x28 holds a controller: base = level × 60 / 100 + clamp(3 × crossings + longest run + 2 × corners, 0, 40), clamped to 0..100, then × the clamped speed and duration ratios
	public int ExcitementBase( int level )
	{
		var (corners, longestRun, crossings, _, _) = SectionStatistics();
		var pieces = Math.Clamp( crossings * 3 + longestRun + corners * 2, 0, 0x28 );
		return Math.Clamp( level * 0x3C / 100 + pieces, 0, 100 );
	}

	// ---- Time --------------------------------------------------------------------------------------

	/// <summary>Advances the controller by <paramref name="deltaSeconds"/>: whole 1/30 s ticks run, the rest carries over.</summary>
	public void Advance( double deltaSeconds )
	{
		tickRemainder += deltaSeconds;
		while ( tickRemainder >= TickSeconds - 1e-9 )
		{
			tickRemainder -= TickSeconds;
			Tick();
		}
	}

	/// <summary>The fractional tick time left over (saved with the controller).</summary>
	public double TickRemainder => tickRemainder;

	internal void RestoreTickRemainder( double remainder ) => tickRemainder = remainder;

	/// <summary>One physics tick (the original's per-vehicle update 0x00546C80): steering and integration, then collisions.</summary>
	public void Tick()
	{
		TickCount++;
		foreach ( var vehicle in vehicles.ToArray() )
		{
			vehicle.Flags &= ~BumpVehicle.Crunched;
			Step( vehicle );
			if ( vehicle.IsLive )
				Integrate( vehicle );
		}
		foreach ( var vehicle in vehicles.ToArray() )
		{
			if ( !vehicle.IsLive )
				continue;
			if ( ((ObjectOf( vehicle )?.Flags ?? 0) & CollisionObject.Crossing) == 0 && (vehicle.Flags & 0x200) == 0 )
			{
				foreach ( var other in vehicles.ToArray() )
					Collide( vehicle, other );
			}
			StuckCheck( vehicle );
			KeepIn( vehicle );
			KeepOut( vehicle );
		}
	}

	private CollisionObject? ObjectOf( BumpVehicle vehicle ) => vehicle.ObjectIndex >= 0 && vehicle.ObjectIndex < objects.Count ? objects[vehicle.ObjectIndex] : null;

	// ---- Collision tests ---------------------------------------------------------------------------

	/// <summary>Whether the point (<paramref name="px"/>, <paramref name="py"/>) lies inside <paramref name="item"/>: in one of its active quadrants and within its radius or half extents.</summary>
	// [BIN:TPW-EXE:0x005493F0 point in object] the point's side bits (x ≤ / &gt; the centre, y &gt; / ≤ the centre) must all be active in the object's flags; the centre itself is inside; a circle compares the scaled distance with the radius, a box each axis with its half extent
	public static bool Contains( int px, int py, CollisionObject? item )
	{
		if ( item == null )
			return false;
		var dx = item.X - px;
		var dy = item.Y - py;
		var bits = (dx >= 0 ? CollisionObject.XAtMost : CollisionObject.XAbove) | (dy < 0 ? CollisionObject.YAbove : CollisionObject.YAtMost);
		if ( dx == 0 && dy == 0 )
			return true;
		if ( (bits & item.Flags) != bits )
			return false;
		if ( !item.IsBox )
			return BumpMath.Distance( dx, dy ) <= item.Extent;
		return Math.Abs( dx ) <= item.Extent && Math.Abs( dy ) <= item.ExtentY;
	}

	private CollisionObject? FirstContaining( int x, int y )
	{
		foreach ( var item in objects )
		{
			if ( (item.Flags & CollisionObject.Used) != 0 && !item.IsKeepOut && Contains( x, y, item ) )
				return item;
		}
		return null;
	}

	/// <summary>Whether two keep-in objects are close enough for a vehicle to pass from one to the other.</summary>
	// [BIN:TPW-EXE:0x005491E0 objects adjacent] the same object, two full objects whose centres are closer than the sum of their sizes, a full object and a clipped one whose quadrants face it, or two clipped objects sharing a centre
	public static bool Adjacent( CollisionObject a, CollisionObject b )
	{
		if ( a == b )
			return true;
		var fullA = (a.Flags & CollisionObject.Sides) == CollisionObject.Sides;
		var fullB = (b.Flags & CollisionObject.Sides) == CollisionObject.Sides;
		if ( fullA && fullB )
			return BumpMath.Distance( a.X - b.X, a.Y - b.Y ) < Math.Max( a.Extent, a.ExtentY ) + Math.Max( b.Extent, b.ExtentY );
		if ( ((a.Flags | b.Flags) & CollisionObject.Sides) == CollisionObject.Sides )
		{
			var partial = fullA ? b : a;
			var full = fullA ? a : b;
			var flags = partial.Flags;
			if ( (flags & CollisionObject.XAtMost) != 0 && full.X < partial.X || (flags & CollisionObject.XAbove) != 0 && partial.X < full.X
				|| (flags & CollisionObject.YAbove) != 0 && partial.Y < full.Y || (flags & CollisionObject.YAtMost) != 0 && full.Y < partial.Y )
			{
				if ( fullA || fullB )
					return BumpMath.Distance( partial.X - full.X, partial.Y - full.Y ) < Math.Max( partial.Extent, partial.ExtentY ) + Math.Max( full.Extent, full.ExtentY );
				return Math.Abs( partial.X - full.X ) + Math.Abs( partial.Y - full.Y ) == 0x1800;
			}
			return false;
		}
		return a.X == b.X && a.Y == b.Y;
	}

	/// <summary>
	/// The keep-in object a vehicle in <paramref name="current"/> should aim through toward (<paramref name="x"/>, <paramref name="y"/>): the nearest
	/// adjacent object whose waypoint lies outside every keep-out circle.
	/// </summary>
	// [BIN:TPW-EXE:0x00549070 next object] each keep-in object's waypoint is its centre moved 0x87C toward the open quadrant; candidates adjacent to the current object whose waypoint is not inside a keep-out circle compete by the distance from their centre to the target
	private CollisionObject? NextObject( CollisionObject current, int x, int y, bool includeSelf )
	{
		CollisionObject? best = null;
		var bestDistance = int.MaxValue;
		foreach ( var item in objects )
		{
			if ( (item.Flags & CollisionObject.Used) == 0 || item.IsKeepOut || !includeSelf && current == item || !Adjacent( current, item ) )
				continue;
			var (wx, wy) = Waypoint( item );
			if ( objects.Any( other => (other.Flags & CollisionObject.Used) != 0 && other.IsKeepOut && Contains( wx, wy, other ) ) )
				continue;
			var distance = BumpMath.Distance( item.X - x, item.Y - y );
			if ( distance < bestDistance )
			{
				bestDistance = distance;
				best = item;
			}
		}
		return best;
	}

	private static (int X, int Y) Waypoint( CollisionObject item )
	{
		var wx = item.X;
		var wy = item.Y;
		if ( (item.Flags & CollisionObject.YAbove) == 0 )
			wy -= TrackSections.ArcOffset;
		else if ( (item.Flags & CollisionObject.YAtMost) == 0 )
			wy += TrackSections.ArcOffset;
		if ( (item.Flags & CollisionObject.XAtMost) == 0 )
			wx += TrackSections.ArcOffset;
		else if ( (item.Flags & CollisionObject.XAbove) == 0 )
			wx -= TrackSections.ArcOffset;
		return (wx, wy);
	}

	// ---- Steering ----------------------------------------------------------------------------------

	/// <summary>
	/// Turns and thrusts <paramref name="vehicle"/> toward (<paramref name="tx"/>, <paramref name="ty"/>) and returns the distance to its aim (−1 when
	/// the target lies in no object). The heading turns by the full turn rate while the angle error exceeds half of it; the vehicle thrusts when the error
	/// is below the distance divided by 512 clamped to 22..48 (a quarter less inside clipped corner objects).
	/// </summary>
	// [BIN:TPW-EXE:0x00547C60 steer] retarget through the object graph while flag 0x10000 is set; error = (−heading − (atan2(dx, −dy)·256/π − 256)) mod 512; turn when error &gt; rate/2 and state ≠ 2; thrust (accel + bonus, ×¾ in a clipped object) along (sin h, cos h) with the 256-scaled table ×4/1024
	private int Steer( BumpVehicle vehicle, int tx, int ty )
	{
		var current = ObjectOf( vehicle );
		if ( (vehicle.Flags & BumpVehicle.Retarget) != 0 && current != null )
		{
			var target = FirstContaining( tx, ty );
			if ( target == null )
			{
				vehicle.TargetX = tx;
				vehicle.TargetY = ty;
				return -1;
			}
			if ( !Adjacent( current, target ) )
			{
				var route = NextObject( current, tx, ty, includeSelf: false );
				if ( route == null )
				{
					vehicle.VX = 0;
					vehicle.VY = 0;
					return -1;
				}
				var (wx, wy) = Waypoint( route );
				vehicle.TargetX = wx;
				vehicle.TargetY = wy;
				vehicle.Flags = (vehicle.Flags & ~BumpVehicle.Retarget) | BumpVehicle.Waypoint;
			}
			else
			{
				vehicle.TargetY = vehicle.JitterY + ty;
				vehicle.TargetX = vehicle.JitterX + tx;
				vehicle.Flags &= ~BumpVehicle.Retarget;
			}
		}
		var dx = vehicle.TargetX - vehicle.X;
		var dy = vehicle.TargetY - vehicle.Y;
		var angle = BumpMath.SteerAngle( dx, dy );
		var error = (-vehicle.Heading - angle) & 0x1FF;
		var distance = BumpMath.Distance( dx, dy );
		var rate = TurnRate;
		var signed = rate;
		if ( error > 0x100 )
		{
			signed = -rate;
			error = 0x200 - error;
		}
		if ( error > rate / 2 && State != StateBroken && (vehicle.Flags & BumpVehicle.Steerable) != 0 )
			vehicle.Heading = (vehicle.Heading + signed) & 0x1FF;
		var limit = Math.Clamp( distance / 512, 0x16, 0x30 );
		if ( error < limit && (vehicle.Flags & (BumpVehicle.Slowed | BumpVehicle.Steerable)) == BumpVehicle.Steerable && State != StateBroken )
		{
			var thrust = Accel + vehicle.AccelBonus;
			if ( current != null && (current.Flags & CollisionObject.Sides) != CollisionObject.Sides )
				thrust = (thrust * 3 + ((thrust * 3 >> 31) & 3)) >> 2;
			vehicle.VY += BumpMath.Div1024( BumpMath.Cos( vehicle.Heading ) * thrust * 4 );
			vehicle.VX += BumpMath.Div1024( BumpMath.Sin( vehicle.Heading ) * thrust * 4 );
		}
		return distance;
	}

	/// <summary>The per-tick vehicle step: lap and unload logic, steering toward the aimed buoy or the unload point.</summary>
	// [BIN:TPW-EXE:0x005474B0 vehicle step] lap mode (flag 8) steers at the buoy list and on capture (within the buoy radius) advances to the next buoy with a fresh jitter, ending the run when the finish buoy's lap counter reaches 0; flag 0x20 steers at the unload point and retires the boat within the radius
	private void Step( BumpVehicle vehicle )
	{
		vehicle.Age++;
		if ( (vehicle.Flags & 4) != 0 )
			return;
		if ( (vehicle.Flags & BumpVehicle.ToUnload) != 0 )
		{
			var distance = Steer( vehicle, UnloadX, UnloadY );
			if ( (vehicle.Flags & BumpVehicle.Waypoint) != 0 && distance <= Template.BuoyRadius )
			{
				vehicle.Flags = (vehicle.Flags & ~BumpVehicle.Waypoint) | BumpVehicle.Retarget;
				return;
			}
			if ( distance < Template.BuoyRadius )
			{
				CompletedRuns++;
				Retire( vehicle );
			}
			return;
		}
		if ( (vehicle.Flags & (BumpVehicle.LapMode | BumpVehicle.Wander | 0x40)) == 0 || buoys.Count == 0 || vehicle.TargetBuoy < 0 || vehicle.TargetBuoy >= buoys.Count )
			return;
		var buoy = buoys[vehicle.TargetBuoy];
		var captured = Steer( vehicle, buoy.X, buoy.Y );
		if ( (vehicle.Flags & BumpVehicle.Waypoint) != 0 && captured <= Template.BuoyRadius )
		{
			vehicle.Flags = (vehicle.Flags & ~BumpVehicle.Waypoint) | BumpVehicle.Retarget;
			return;
		}
		if ( (vehicle.Flags & 0x48000) == 0x48000 )
		{
			Relaunch( vehicle );
			vehicle.Flags |= BumpVehicle.Retarget;
			return;
		}
		if ( captured > Template.BuoyRadius )
			return;
		vehicle.BuoyCountdown--;
		if ( vehicle.BuoyCountdown == 0 )
		{
			Relaunch( vehicle );
			vehicle.Flags |= BumpVehicle.Retarget;
			return;
		}
		if ( (vehicle.Flags & BumpVehicle.LapMode) != 0 && (vehicle.Flags & 0x40) == 0 )
		{
			if ( buoy.IsFinish )
			{
				// laps run from a negative count up to 0; a closed ride ends the run at once
				vehicle.Laps++;
				if ( Phase == PhaseOver || Phase == PhaseClosed || vehicle.Laps == 0 )
				{
					vehicle.JitterX = 0;
					vehicle.JitterY = 0;
					vehicle.Flags = (vehicle.Flags & unchecked((int)0xFFFFFFA1)) | BumpVehicle.Retarget | BumpVehicle.ToUnload;
				}
				else
				{
					Relaunch( vehicle );
				}
			}
			if ( buoy.Object != null && !Contains( vehicle.X, vehicle.Y, buoy.Object ) )
			{
				vehicle.JitterX = 0;
				vehicle.JitterY = 0;
				vehicle.Flags |= BumpVehicle.Retarget;
				return;
			}
			if ( buoy.Object != null )
				vehicle.ObjectIndex = buoy.Object.Index;
			AimAtNext( vehicle, NextBuoy( buoy ) );
		}
		vehicle.Flags |= BumpVehicle.Retarget;
	}

	/// <summary>Aims at <paramref name="next"/> with a fresh jitter: the new value on X, the previous X on Y.</summary>
	// [BIN:TPW-EXE:0x00516330 world rng] the jitter is the buoy radius / 2 minus a world-generator draw modulo the radius; the new draw goes to X and the previous X to Y
	// [APPROX:DET-013] the draw comes from the park's seeded SplitMix64 stream instead of the original's world generator
	private void AimAtNext( BumpVehicle vehicle, Buoy next )
	{
		vehicle.TargetBuoy = next.Index;
		vehicle.JitterY = vehicle.JitterX;
		var radius = (uint)Template.BuoyRadius;
		vehicle.JitterX = (int)(radius / 2) - (int)(worldRandom() % radius);
		vehicle.Flags |= BumpVehicle.Retarget;
	}

	/// <summary>Damping and movement of one tick, the water boat's roll and its occasional spray.</summary>
	// [BIN:TPW-EXE:0x00547F50 integrate] v = v × d / 1024 (d = the damping; 1000 while the ride is broken, ×7/10 under flag 0x80000), the post-damping velocity is kept, position += v; water: the wall spin decays by 0.998 above 0x30, the roll is spin / 128 clamped to ±16, and every 32nd tick a boat has a 1 in 8 chance of spray
	private void Integrate( BumpVehicle vehicle )
	{
		var damping = Damping;
		if ( (vehicle.Flags & BumpVehicle.Slowed) == 0 )
		{
			if ( State == StateBroken )
				damping = 1000;
		}
		else
		{
			damping = damping * 7 / 10;
		}
		vehicle.VX = BumpMath.Div1024( damping * vehicle.VX );
		vehicle.VY = BumpMath.Div1024( damping * vehicle.VY );
		vehicle.VX0 = vehicle.VX;
		vehicle.VY0 = vehicle.VY;
		vehicle.Speed = BumpMath.Length( vehicle.VX, vehicle.VY );
		vehicle.Y += vehicle.VY;
		vehicle.X += vehicle.VX;
		if ( !IsWater )
			return;
		if ( Math.Abs( vehicle.Spin ) > 0x30 )
			vehicle.Spin = (int)(vehicle.Spin * 99.80000305175781 * 0.009999999776482582);
		var roll = (vehicle.Spin + ((vehicle.Spin >> 31) & 0x7F)) >> 7;
		vehicle.Roll = Math.Clamp( roll, -16, 16 );
		// [APPROX:TRACK-016] the boats' spray, the smoke, horn and crash cues and the boat model variants draw nothing: the cosmetic generator and a spray counter keep the original's cadence, no particle, sound or mesh is made — evidence needed: the particle and sound events of 0x00547F50 and 0x00547170
		if ( Phase == PhaseRunning && (TickCount & 0x1F) == 0 && (world?.NextCosmetic() ?? 0) is var draw && ((draw >> 16) & 7) == 2 )
		{
			vehicle.Sprays++;
			Sprays++;
		}
	}

	// ---- Collisions --------------------------------------------------------------------------------

	/// <summary>Pushes <paramref name="vehicle"/> and <paramref name="other"/> apart when their circles overlap, exchanging momentum when they close in.</summary>
	// [BIN:TPW-EXE:0x00546C80 vehicle collisions] closing (relative velocity within a quarter turn of the line between them): the normal part of the relative speed × the other's restitution goes to the other vehicle, the rest back to this one; otherwise they are pushed apart by the overlap, the other by its restitution share
	private void Collide( BumpVehicle vehicle, BumpVehicle other )
	{
		if ( other == vehicle || !other.IsLive || ((ObjectOf( other )?.Flags ?? 0) & CollisionObject.Crossing) != 0 || (other.Flags & 0x200) != 0 )
			return;
		var dx = other.X - vehicle.X;
		var dy = other.Y - vehicle.Y;
		var reach = other.Radius + vehicle.Radius;
		var distance = BumpMath.Distance( dx, dy );
		if ( distance == 0 || distance >= reach )
			return;
		var direction = BumpMath.HeadingOf( dx, dy );
		var rvx = vehicle.VX0 - other.VX0;
		var rvy = vehicle.VY0 - other.VY0;
		var relative = BumpMath.SteerAngle( rvx, rvy );
		var angle = (-direction - relative) & 0x1FF;
		if ( angle < 0x80 || angle > 0x180 )
		{
			var speed = BumpMath.Length( rvx, rvy );
			var normal = BumpMath.Div1024( speed * BumpMath.Cos( angle ) * 4 );
			var share = BumpMath.Div1024( Restitution * normal );
			other.VX += BumpMath.Div1024( share * BumpMath.Sin( direction ) * 4 );
			other.VY += BumpMath.Div1024( share * BumpMath.Cos( direction ) * 4 );
			other.Flags |= BumpVehicle.Bumped | BumpVehicle.Retarget;
			var rest = normal - share;
			vehicle.VX -= BumpMath.Div1024( rest * BumpMath.Sin( direction ) * 4 );
			vehicle.VY -= BumpMath.Div1024( rest * BumpMath.Cos( direction ) * 4 );
		}
		else
		{
			var ox = dx - dx * reach / distance;
			var oy = dy - dy * reach / distance;
			var px = BumpMath.Div1024( ox * Restitution );
			var py = BumpMath.Div1024( oy * Restitution );
			other.X -= px;
			other.Y -= py;
			vehicle.X += ox - px;
			vehicle.Y += oy - py;
		}
	}

	/// <summary>
	/// Counts wall contacts: a boat that touches walls on more than 16 ticks of a 32-tick window is stuck and aims at the buoy after the one of its
	/// object (or after the finish buoy) with a fresh jitter.
	/// </summary>
	// [BIN:TPW-EXE:0x00546C80 stuck retarget] once the window counter passes 31, a wall-contact count above 16 moves the aim to the next buoy; both counters reset
	private void StuckCheck( BumpVehicle vehicle )
	{
		if ( vehicle.WallHits <= 0 )
			return;
		var window = vehicle.WallWindow++;
		if ( window <= 0x1F )
			return;
		if ( vehicle.WallHits > 0x10 )
		{
			var buoy = ObjectOf( vehicle )?.Buoy ?? buoys.FirstOrDefault( item => item.IsFinish );
			if ( buoy != null )
				AimAtNext( vehicle, NextBuoy( buoy ) );
			vehicle.Flags |= BumpVehicle.Retarget;
		}
		vehicle.WallHits = 0;
		vehicle.WallWindow = 0;
	}

	/// <summary>
	/// Keeps a boat inside the keep-in objects: when it leaves its object it first looks for another one that holds it, otherwise it is reflected off
	/// the wall (a circle turns the velocity about the radial direction and moves the boat back onto the arc, a box flips the offending component)
	/// and the wall hit spins the boat.
	/// </summary>
	// [BIN:TPW-EXE:0x005497B0 keep-in] outside the current object: another object containing the point becomes current and the aim is recomputed; else the wall counter rises, the velocity is reflected (× restitution) and the position projected back; water adds spin ∓0x30 × penetration
	private void KeepIn( BumpVehicle vehicle )
	{
		var current = ObjectOf( vehicle );
		if ( Contains( vehicle.X, vehicle.Y, current ) )
			return;
		var found = FirstContaining( vehicle.X, vehicle.Y );
		if ( found != null )
		{
			vehicle.ObjectIndex = found.Index;
			vehicle.Flags |= BumpVehicle.Retarget;
			return;
		}
		if ( current == null )
			return;
		if ( vehicle.WallHits >= 0 )
			vehicle.WallHits++;
		var dyo = vehicle.Y - current.Y;
		var dxo = vehicle.X - current.X;
		var velocityHeading = BumpMath.HeadingOf( vehicle.VX0, vehicle.VY0 );
		int penetration;
		if ( !current.IsBox )
		{
			var radial = BumpMath.ReflectAngle( dxo, dyo );
			var a = (-128 - radial) & 0x1FF;
			var b = (velocityHeading - 0x100) & 0x1FF;
			var u = (a * 2 - 0x100 - b - velocityHeading) & 0x1FF;
			var vy = BumpMath.Div1024( Restitution * vehicle.VY );
			var vx = BumpMath.Div1024( Restitution * vehicle.VX );
			vehicle.VX = BumpMath.Div1024( (vy * BumpMath.Sin( u ) + vx * BumpMath.Cos( u )) * 4 );
			vehicle.VY = BumpMath.Div1024( (vy * BumpMath.Cos( u ) - vx * BumpMath.Sin( u )) * 4 );
			var distance = BumpMath.Distance( dxo, dyo );
			penetration = Math.Abs( current.Extent - distance );
			if ( distance != 0 )
			{
				vehicle.X = dxo * current.Extent / distance + current.X;
				vehicle.Y = dyo * current.Extent / distance + current.Y;
			}
		}
		else
		{
			penetration = Math.Abs( dxo );
			if ( current.Extent < Math.Abs( dxo ) )
			{
				vehicle.VX = -BumpMath.Div1024( Restitution * vehicle.VX );
				vehicle.X = current.X + (dxo < 0 ? -current.Extent : current.Extent);
				penetration = Math.Abs( current.Extent - Math.Abs( dxo ) );
			}
			if ( current.ExtentY < Math.Abs( dyo ) )
			{
				vehicle.VY = -BumpMath.Div1024( Restitution * vehicle.VY );
				vehicle.Y = current.Y + (dyo < 0 ? -current.ExtentY : current.ExtentY);
				penetration = Math.Abs( current.ExtentY - Math.Abs( dyo ) );
			}
		}
		if ( IsWater )
		{
			var heading = BumpMath.SteerAngle( vehicle.VX, vehicle.VY );
			Spin( vehicle, penetration, ((-velocityHeading - heading) & 0x1FF) < 0x100 ? -1 : 1, heading );
		}
	}

	/// <summary>Keeps a boat out of the keep-out circles: moving in, its normal velocity is cancelled and it is pushed back to the rim.</summary>
	// [BIN:TPW-EXE:0x005494D0 keep-out] inside a keep-out circle and moving toward its centre: velocity += normal speed × (restitution + 1024) / 1024 along the radial direction, position projected to the rim; water adds the wall spin
	private void KeepOut( BumpVehicle vehicle )
	{
		foreach ( var circle in objects )
		{
			if ( (circle.Flags & CollisionObject.Used) == 0 || !circle.IsKeepOut || !Contains( vehicle.X, vehicle.Y, circle ) )
				continue;
			var dx = vehicle.X - circle.X;
			var dy = vehicle.Y - circle.Y;
			var reversed = BumpMath.HeadingOf( -vehicle.VX0, -vehicle.VY0 );
			var radial = BumpMath.HeadingOf( dx, dy );
			var angle = (reversed - radial) & 0x1FF;
			if ( angle >= 0x80 && angle <= 0x180 )
				continue;
			var speed = BumpMath.Length( vehicle.VX0, vehicle.VY0 );
			var normal = BumpMath.Div1024( BumpMath.Div1024( speed * BumpMath.Cos( angle ) * 4 ) * (Restitution + 0x400) );
			vehicle.VX += BumpMath.Div1024( BumpMath.Sin( radial ) * normal * 4 );
			vehicle.VY += BumpMath.Div1024( BumpMath.Cos( radial ) * normal * 4 );
			vehicle.Flags |= BumpVehicle.Bumped | BumpVehicle.Retarget;
			var distance = BumpMath.Distance( dx, dy );
			if ( distance == 0 )
				continue;
			vehicle.X = dx * circle.Extent / distance + circle.X;
			vehicle.Y = dy * circle.Extent / distance + circle.Y;
			if ( IsWater )
			{
				var heading = BumpMath.SteerAngle( -vehicle.VX, -vehicle.VY );
				Spin( vehicle, Math.Abs( distance - circle.Extent ), ((-heading - reversed) & 0x1FF) < 0x100 ? -1 : 1, heading );
			}
		}
	}

	private static void Spin( BumpVehicle vehicle, int penetration, int sign, int heading )
	{
		// the wall hit's spin impulse is ∓0x30 × penetration; it replaces the spin when the old spin points the other way
		var impulse = penetration * 0x30 * sign;
		if ( sign < 0 )
			vehicle.Spin = impulse <= vehicle.Spin + impulse ? vehicle.Spin + impulse : impulse;
		else
			vehicle.Spin = vehicle.Spin + impulse <= impulse ? vehicle.Spin + impulse : impulse;
	}

	// ---- Save support ------------------------------------------------------------------------------

	/// <summary>The hash part of a controller (called only for parks with a controller).</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( Type );
		hash.Add( Laps );
		hash.Add( Performance );
		hash.Add( Phase );
		hash.Add( State );
		hash.Add( IsTrackValid );
		hash.Add( WaterRunning );
		hash.Add( TickCount );
		hash.Add( tickRemainder );
		hash.Add( StationX );
		hash.Add( StationY );
		hash.Add( sections.Count );
		foreach ( var section in sections )
		{
			hash.Add( section.KindWord );
			hash.Add( section.X );
			hash.Add( section.Y );
		}
		hash.Add( Pending.Count );
		foreach ( var guest in Pending )
			hash.Add( guest );
		hash.Add( Leaving.Count );
		foreach ( var guest in Leaving )
			hash.Add( guest );
		hash.Add( vehicles.Count );
		foreach ( var vehicle in vehicles )
		{
			hash.Add( vehicle.Slot );
			hash.Add( vehicle.Flags );
			hash.Add( vehicle.X );
			hash.Add( vehicle.Y );
			hash.Add( vehicle.VX );
			hash.Add( vehicle.VY );
			hash.Add( vehicle.Heading );
			hash.Add( vehicle.Spin );
			hash.Add( vehicle.TargetBuoy );
			hash.Add( vehicle.Laps );
			hash.Add( vehicle.Riders.Count );
		}
	}

	internal void Restore( BumpControllerSaveData data )
	{
		Laps = data.Laps;
		SetPerformance( data.Performance );
		Phase = data.Phase;
		State = data.State;
		TickCount = data.TickCount;
		tickRemainder = data.TickRemainder;
		WaterRunning = data.WaterRunning;
		Pending.Clear();
		Pending.AddRange( data.Pending );
		Leaving.Clear();
		Leaving.AddRange( data.Leaving );
		Array.Clear( slots );
		vehicles.Clear();
		VehicleCount = 0;
		RiderCount = 0;
		foreach ( var saved in data.Vehicles )
		{
			var vehicle = new BumpVehicle
			{
				Slot = saved.Slot, Flags = saved.Flags, Variant = saved.Variant, X = saved.X, Y = saved.Y, VX = saved.VX, VY = saved.VY, VX0 = saved.VX0, VY0 = saved.VY0,
				Speed = saved.Speed, Heading = saved.Heading, Spin = saved.Spin, Roll = saved.Roll, Radius = saved.Radius, AccelBonus = saved.AccelBonus,
				TargetX = saved.TargetX, TargetY = saved.TargetY, JitterX = saved.JitterX, JitterY = saved.JitterY, TargetBuoy = saved.TargetBuoy, Laps = saved.Laps,
				BuoyCountdown = saved.BuoyCountdown, Age = saved.Age, ObjectIndex = saved.ObjectIndex, WallHits = saved.WallHits, WallWindow = saved.WallWindow, Sprays = saved.Sprays
			};
			vehicle.Riders.AddRange( saved.Riders );
			slots[vehicle.Slot] = vehicle;
			InsertSorted( vehicle );
			VehicleCount++;
			RiderCount += vehicle.Riders.Count;
		}
	}
}
