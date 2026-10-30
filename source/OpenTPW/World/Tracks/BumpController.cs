namespace OpenTPW;

/// <summary>
/// The bump-ride controller of one kart or water ride (original: 0xD0 bytes, up to 64 in a module-wide table): the circuit's sections,
/// collision objects and buoys, the guests waiting to board and to leave, and the vehicles. It runs the module's integer physics once per 31 ms
/// world substep (about 32.26 per second) and answers the ride script's <c>BUMP</c> commands. It runs water boats (types −5, −8, −12, −13), go-karts (types −4,
/// −7, −9, −10: the grid start, the race, the finish and the kart effects) and bumper cars (types −1, −6, −11, −14: cars that wander an arena
/// and hand their riders over when the session's countdown ends), with the add-ons' vehicle hook (the Space Racers Teleport hides a vehicle between its two tiles, <see cref="PassAddOn"/>).
/// </summary>
// [EXT:bump-pre-shared-save-order] a park-resume save from before the shared pools restores their order from the restore (controllers by attraction id), where the original's pools only ever fill in build order; kept so older saves load; [EXT:bump-foreign-object-forget] a vehicle whose foreign object is rebuilt loses it (BumpWorld.ForgetObjectsOf)

public sealed partial class BumpController
{
	/// <summary>The controller's duration unit: BUMP 13 stores seconds × 30. It is not the tick rate (<see cref="TickSeconds"/>).</summary>
	// [BIN:TPW-EXE:0x00554980 BUMP 13] BUMP 13 stores parameter × 30 and BUMP 14 −parameter into controller +4 through the duration setter 0x00545100; the unit 30 is the original's constant
	public const int TicksPerSecond = 30;
	/// <summary>The controller's tick: one per 31 ms world substep, so a duration of 30 units lasts 0.93 s.</summary>
	// [BIN:TPW-EXE:0x0054F4C4 world substep] the game loop 0x54F4BF..0x54F8DA runs fixed substeps (next += 0x1F, a backlog over 2000 ms is dropped at 0x54F493) and every substep calls the bump module tick 0x546C80 (call at 0x54F55F), 0x520130 and the RSSE tick 0x5516B0; the substep is skipped while paused. The shared-substep catch-up rules are ECON-001's
	public const double TickSeconds = 0.031;
	/// <summary>The largest tick clock a save may carry: saves from before the 31 ms tick (1/30 s) hold up to this and run it off on the next advance.</summary>
	public const double MaximumSavedRemainder = 1.0 / 30;
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
	/// <summary>The module this controller ticks in: its world, or one of its own for a controller made without a world.</summary>
	private readonly BumpWorld module;
	private readonly List<BumpVehicle> vehicles = new();
	private List<TrackSection> sections = new();
	private List<CollisionObject> objects = new();
	private List<Buoy> buoys = new();

	/// <param name="template">The controller type's template.</param>
	/// <param name="worldRandom">The world generator the aiming jitter draws from (the original's 0x516330).</param>
	/// <param name="world">The module state shared by the park's controllers (cosmetic generator), or null for a controller of its own.</param>
	public BumpController( BumperTemplate template, Func<uint> worldRandom, BumpWorld? world = null )
	{
		Template = template ?? throw new ArgumentNullException( nameof( template ) );
		VehicleLimit = template.MaxVehicles;
		this.worldRandom = worldRandom ?? throw new ArgumentNullException( nameof( worldRandom ) );
		this.world = world;
		// the controller ticks in its module from its creation (a ride's runtime adds it to the park's module, a test's controller to its own world)
		module = world ?? new BumpWorld();
		module.Add( this );
		Laps = template.Laps;
		Performance = template.DefaultPerformance;
		ApplyPerformance();
		// [BIN:TPW-EXE:0x004DB0DF attraction constructor] the attraction's track word +0x2C is 1 from the constructor on, for every ride; only a track edit's circuit walk (0x52A970 at 0x524D1B, 0x52694F, 0x526BE1, 0x526F13) or its clear (0x5251B8, 0x5257FB) rewrites it
		IsTrackValid = true;
		if ( template.IsBumper )
		{
			// a bumper ride has no track to walk, and the arena exists from the start (a placed ride moves it with SetStation)
			SetStation( 0, 0, TrackHeading.PosY );
		}
	}

	/// <summary>Whether the ride lays a track (kart and water rides): false for the bumper cars, which drive in an arena.</summary>
	public bool HasTrack => !Template.IsBumper;

	public BumperTemplate Template { get; }

	/// <summary>
	/// [EXT:cheats-ride-vehicles] How many vehicles the controller launches: the template's <see cref="BumperTemplate.MaxVehicles"/>, or the raised limit of the cheats window's ride vehicle cheat
	/// (bumper cars and karts, <see cref="RaisedVehicleLimit"/>; the water rides' 64 boats are already the module's own limit). docs/CHEATS.md.
	/// </summary>
	public int VehicleLimit { get; private set; }

	/// <summary>[EXT:cheats-ride-vehicles] The vehicle limit of a bumper or kart ride the cheat raised (the user's ceiling, 32).</summary>
	public const int RaisedVehicleLimit = 32;

	/// <summary>Whether the cheat can raise this controller's vehicle limit: bumper cars and karts, not the water rides (their boats are already limited only by the module's 64).</summary>
	public bool CanRaiseVehicleLimit => Template.IsBumper || Template.IsKart;

	/// <summary>[EXT:cheats-ride-vehicles] Raises the vehicle limit to <see cref="RaisedVehicleLimit"/>; false for a water ride or one already raised.</summary>
	internal bool RaiseVehicleLimit()
	{
		if ( !CanRaiseVehicleLimit || VehicleLimit >= RaisedVehicleLimit )
			return false;
		VehicleLimit = RaisedVehicleLimit;
		return true;
	}
	/// <summary>The attraction id of the ride this controller belongs to (set by the runtime; −1 for a controller of its own): the riders' heads draw for it.</summary>
	public int AttractionId { get; set; } = -1;
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
	/// <summary>Whether this ride was restored from a park saved before the station followed the original's line (<see cref="TrackStation.FromPlacementLegacy"/>); saved with the controller.</summary>
	public bool LegacyStationLayout { get; set; }
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
	/// <summary>
	/// Whether a track edit has written the track word since the constructor stored 1: the ride's placement walks nothing in tp.exe (<c>0x529E10</c> lays the station through <c>0x52A1A0</c>), so a fresh
	/// ride reads 1 until the first track-tool click clears it (<c>0x5251B8</c>, <c>0x5257FB</c>) or a walk writes its verdict. Saved, so a load keeps a fresh ride's 1.
	/// </summary>
	public bool TrackWordWritten { get; private set; }
	/// <summary>The water pieces' animation state set by BUMP 17: true while they run (animation 5), false when stopped (animation 12).</summary>
	public bool WaterRunning { get; private set; } = true;
	/// <summary>
	/// The last BUMP 17 parameter (the script's word +0xE6), −1 when none was given since the ride was placed or the park was loaded. A BUMP 17 with the same value
	/// does nothing. Presentation only: not saved and not hashed (the original's load resets the word too).
	/// </summary>
	// [BIN:TPW-EXE:0x00558C71 script attach] the word +0xE6 starts at −1 (or ecx, -1 at 0x558C22) when the ride's script is attached, and the savegame load's script restore 0x5597A0 (from 0x415270) sets it to 0xFFFF again (0x559F29)
	public int WaterCommand { get; private set; } = -1;
	/// <summary>
	/// Raised when BUMP 17 changes <see cref="WaterCommand"/>: true starts the pieces' water animation (animation 5), false stops it (12). The level walks the
	/// circuit from both station sides and triggers every piece it passes (<see cref="TrackPieceWalk"/>).
	/// </summary>
	public event Action<bool>? WaterPiecesTriggered;
	/// <summary>The module ticks this controller has run (statistics; the 32-tick gates read the module's frame counter, <see cref="BumpWorld.FrameCount"/>).</summary>
	public long TickCount { get; internal set; }

	/// <summary>The module the controller ticks in (its world, or its own).</summary>
	public BumpWorld Module => module;
	public IReadOnlyList<TrackSection> Sections => sections;
	public IReadOnlyList<CollisionObject> Objects => objects;
	public IReadOnlyList<Buoy> Buoys => buoys;
	/// <summary>The buoy after the finish buoy (the first section buoy), where a launched boat aims first (+0xA4).</summary>
	public Buoy? FirstAfterFinish { get; private set; }
	public IReadOnlyList<BumpVehicle> Vehicles => vehicles;
	/// <summary>Whether the kart mesh has the sparks dummy (class 0x100, number 1): the exhaust puff and the breakdown smoke need it. The runtime sets it from the model; true by default.</summary>
	public bool HasSparksNode { get; set; } = true;
	/// <summary>Raised while a kart ticks for every cosmetic effect (sounds, particles, the engine voice, animation changes). Presentation only: handlers must not change the controller.</summary>
	public event Action<BumpEffect>? Effect;
	/// <summary>Whether the race has been started and not yet ended, for the kart rides (+0x50 is 2).</summary>
	public bool IsRacing => Phase == PhaseRunning;
	/// <summary>The kart grid buoys (flag 2), in slot order.</summary>
	public IEnumerable<Buoy> GridBuoys => buoys.Where( buoy => buoy.IsSkipped );

	private void Raise( BumpEffectKind kind, BumpVehicle vehicle, int id = 0, int value = 0 ) => Effect?.Invoke( new BumpEffect( kind, vehicle.Slot, id, value, vehicle.X, vehicle.Y, vehicle.Height, vehicle.SparksNode ) );
	/// <summary>Guests added by BUMP 1 who ride with the next boat, newest first (+0xC4).</summary>
	public List<int> Pending { get; } = new();
	/// <summary>Guests who left a boat, handed to the script one at a time by BUMP 2, newest first (+0xC8).</summary>
	public List<int> Leaving { get; } = new();
	/// <summary>The cosmetic spray events of all boats (counter only; no particles are spawned).</summary>
	public int Sprays { get; private set; }
	/// <summary>Laps completed by boats that ended their run (statistics).</summary>
	public int CompletedRuns { get; private set; }

	// ---- Setup ------------------------------------------------------------------------------------

	/// <summary>
	/// The words of an original save's ride chunk the controller holds (loader <c>0x543638</c>: the laps or duration at +4, the performance at +0x1C, the phase at +0x50). The performance goes
	/// through the setter that derives thrust, damping, turn and bounce (<see cref="ApplyPerformance"/>). The loader restores the saved vehicles with the phase; a controller restored without them keeps a phase it can leave: a kart or bumper ride
	/// saved racing (2) or over (3) loads in loading (1), a water ride keeps its running phase (its boats retire to it, 0x54AE50).
	/// </summary>
	// [BIN:TPW-EXE:0x54AE50 retire vehicle] water rides keep their phase when the last boat retires, karts reset to loading: the phases a controller without vehicles can hold
	internal int RestoreOriginalWords( int laps, int performance, int phase )
	{
		Laps = laps;
		Performance = Math.Clamp( performance, 0, 100 );
		ApplyPerformance();
		var kept = IsWater && phase is >= PhaseClosed and <= PhaseOver || phase is PhaseClosed or PhaseLoading ? phase : PhaseLoading;
		Phase = kept;
		return kept;
	}

	/// <summary>Places the station reference at (<paramref name="stationX"/>, <paramref name="stationY"/>) in section units and orients the flow along <paramref name="heading"/>.</summary>
	public void SetStation( int stationX, int stationY, byte heading )
	{
		StationX = stationX;
		StationY = stationY;
		StationHeading = heading;
		Orientation = TrackSections.OrientationOf( heading );
		if ( Template.IsBumper )
		{
			// bumper cars: the arena is centred on the station reference, cars drop in and out at the same point
			UnloadX = stationX;
			UnloadY = stationY;
			BuildArena();
			return;
		}
		if ( IsWater )
		{
			var (finish, unloadX, unloadY) = TrackSections.WaterStationBuoys( stationX, stationY, heading );
			UnloadX = unloadX;
			UnloadY = unloadY;
			stationBuoys = new[] { finish };
		}
		else if ( Template.IsKart )
		{
			// karts spawn and unload at the station reference itself; the grid and the finish buoy lie around it
			UnloadX = stationX;
			UnloadY = stationY;
			stationBuoys = TrackSections.KartStationBuoys( stationX, stationY, Orientation, Template.MaxVehicles );
		}
		RebuildStationOnly();
	}

	private IReadOnlyList<Buoy> stationBuoys = Array.Empty<Buoy>();

	/// <summary>
	/// The bumper arena (original: the setup of 0x00545890 for types −1, −6, −11 and −14): one full keep-in circle at the station centre, no sections,
	/// and eight buoys on a ring around the centre that the cars wander between. The ring starts at (0, R) and turns by 45° per step with the
	/// original's 8-bit sine factors and truncating shifts.
	/// </summary>
	// [BIN:TPW-EXE:0x00545890 bumper arena] collision object flags 0x1F at (X, Y) with radius 0x1200 (−1), 0x1100 (−11) or 0x1600; eight buoys (flag 1) from (0, R) rotated by x' = (S·x + C·y) / 256, y' = (S·y − C·x) / 256 with C = sine[64] and S = sine[192], each shifted to the centre
	private void BuildArena()
	{
		var circle = new CollisionObject { Flags = CollisionObject.Used | CollisionObject.Sides, X = StationX, Y = StationY, Extent = Template.ArenaRadius, ExtentY = Template.ArenaRadius, Index = 0 };
		var ring = new List<Buoy>();
		var x = 0;
		var y = Template.RingRadius;
		var c = BumpMath.Sine[64];
		var s = BumpMath.Sine[192];
		for ( var index = 0; index < 8; index++ )
		{
			ring.Add( new Buoy { Flags = Buoy.Used, X = x + StationX, Y = y + StationY, Index = index } );
			var nx = s * x + c * y;
			var ny = s * y - c * x;
			x = (nx + ((nx >> 31) & 0xFF)) >> 8;
			y = (ny + ((ny >> 31) & 0xFF)) >> 8;
		}
		// [BIN:TPW-EXE:0x00545890 bumper arena] the circle and the ring come from the module's object and buoy pools
		if ( !PlaceInModule( new List<CollisionObject> { circle }, ring.Count ) )
		{
			Log?.Warning( "The bumper arena does not fit the module's object or buoy pool." );
			ring.Clear();
		}
		sections = new List<TrackSection>();
		buoys = ring;
		FirstAfterFinish = null;
		foreach ( var vehicle in vehicles )
		{
			vehicle.ObjectIndex = objects.Count > 0 ? 0 : -1;
			vehicle.ObjectOwner = null;
		}
	}

	/// <summary>
	/// Puts this controller's new collision objects into the module's pool (the old ones freed first) when they and <paramref name="buoyCount"/> buoys fit the shared
	/// pools; else the controller keeps no objects and the call answers false. Vehicles of other rides that sat in the old objects lose their object.
	/// </summary>
	// [BIN:TPW-EXE:0x0054B1E0 object pool] 2048 records for every ride of the park; [BIN:TPW-EXE:0x0054B170 buoy pool] 1024 ("Out of Buoys")
	private bool PlaceInModule( List<CollisionObject> fresh, int buoyCount )
	{
		module.ForgetObjectsOf( this );
		if ( module.SharedSaveRestore )
		{
			// a restore of a shared-module save: the objects wait unplaced for their saved pool slots (BumpWorld.FinishRestore), no pool check
			module.PlaceObjects( this, Array.Empty<CollisionObject>() );
			foreach ( var item in fresh )
			{
				item.Owner = this;
				item.ModuleSlot = -1;
			}
			objects = fresh;
			return true;
		}
		var placed = module.BuoyCount - buoys.Count + buoyCount <= BumpWorld.BuoyPool && module.PlaceObjects( this, fresh );
		if ( !placed )
		{
			module.PlaceObjects( this, Array.Empty<CollisionObject>() );
			objects = new List<CollisionObject>();
			return false;
		}
		objects = fresh;
		return true;
	}

	private void RebuildStationOnly()
	{
		var result = TrackSections.Build( Array.Empty<TrackStep>(), Template.Slack, CloneStation(), IsWater );
		if ( !Adopt( result ) )
			Log?.Warning( "The track station does not fit the module's object or buoy pool." );
	}

	private IReadOnlyList<Buoy> CloneStation() => stationBuoys.Select( buoy => new Buoy { Flags = buoy.Flags, X = buoy.X, Y = buoy.Y } ).ToArray();

	/// <summary>Takes a built circuit; false when its objects or buoys overflow the module's shared pools (the controller then holds no objects and no buoys).</summary>
	private bool Adopt( TrackSections.Result result )
	{
		var fits = PlaceInModule( result.Objects.ToList(), result.Buoys.Count );
		sections = result.Sections.ToList();
		buoys = fits ? result.Buoys.ToList() : new List<Buoy>();
		FirstAfterFinish = FindFirstAfterFinish();
		return fits;
	}

	/// <summary>
	/// Replaces the circuit after a walk: the boats leave the ride, the ride closes, the old sections and section buoys go, and the new
	/// circuit's sections, collision objects and buoys are built. The validity flag is the walk's verdict.
	/// </summary>
	// [BIN:TPW-EXE:0x00545450 section reset] a re-walk first unloads every vehicle (0x0054AE50) and closes the ride (0x00544A10), then frees the sections, their collision objects and the buoys that are not station buoys (flags 2 or 4)
	public void Rebuild( TrackCircuit circuit, bool writesWord = true )
	{
		ArgumentNullException.ThrowIfNull( circuit );
		EjectAll();
		Close();
		var result = TrackSections.Build( circuit.IsValid ? circuit.Steps : Array.Empty<TrackStep>(), Template.Slack, CloneStation(), IsWater );
		// [EXT:track-pool-overflow-refused] a circuit that overflows the section, collision-object or buoy pool is not a ride: the walk's verdict is overridden to invalid and the geometry is dropped, so no boat runs on a partial track; the original skips sections silently when the section pool is full, crashes on a collision-object overflow (it logs "Out of Collision Objects" and writes the flags through a null pointer, [BIN:TPW-EXE:0x0054B1E0]) and drops buoys ("Out of Buoys")
		// the collision-object and buoy pools are the module's: a circuit that does not fit beside the other rides' is refused the same way
		if ( result.Truncated || !Adopt( result ) )
		{
			Log?.Warning( result.Truncated
				? $"A track circuit of {circuit.Steps.Count} tiles overflows the section pool; the ride is invalid."
				: $"A track circuit of {circuit.Steps.Count} tiles does not fit the module's shared collision-object or buoy pool beside the other rides; the ride is invalid." );
			if ( writesWord )
				IsTrackValid = false;
			Adopt( TrackSections.Build( Array.Empty<TrackStep>(), Template.Slack, CloneStation(), IsWater ) );
		}
		else if ( writesWord )
			IsTrackValid = circuit.IsValid;
		if ( writesWord )
			TrackWordWritten = true;
		foreach ( var vehicle in vehicles )
		{
			vehicle.ObjectIndex = -1;
			vehicle.ObjectOwner = null;
		}
	}

	/// <summary>
	/// The re-walk of a park-resume restore of a shared-module save (<see cref="BumpWorld.SharedSaveRestore"/>): the saved verdict is taken instead of replaying the
	/// shared pools' checks. A ride saved invalid gets the station-only geometry (a walk that is valid now was refused by the pools when it was built); a ride saved
	/// valid needs a valid walk whose sections fit its own pool.
	/// </summary>
	// [EXT:track-pool-overflow-refused] the shared pools' overflow verdict depends on the order the rides were built in, which a restore does not replay (it walks the rides by attraction id); the restore keeps the saved verdicts and object slots, so a park near the limits loads as it was saved
	internal void RestoreCircuit( TrackCircuit circuit, bool savedValid, int savedObjectCount, int? savedBuoyCount = null )
	{
		ArgumentNullException.ThrowIfNull( circuit );
		EjectAll();
		Close();
		var valid = savedValid && circuit.IsValid;
		var result = TrackSections.Build( valid ? circuit.Steps : Array.Empty<TrackStep>(), Template.Slack, CloneStation(), IsWater );
		if ( result.Truncated )
			throw new InvalidDataException( $"A saved valid circuit of {circuit.Steps.Count} tiles overflows the section pool." );
		if ( savedObjectCount == 0 && (savedBuoyCount ?? 0) == 0 && (result.Objects.Count > 0 || savedBuoyCount != null && result.Buoys.Count > 0) )
		{
			// the save holds a ride whose station did not fit the shared pools either: its sections without objects or buoys, as Adopt leaves a refused build
			if ( valid )
				throw new InvalidDataException( "A saved valid circuit places no collision objects." );
			module.ForgetObjectsOf( this );
			module.PlaceObjects( this, Array.Empty<CollisionObject>() );
			objects = new List<CollisionObject>();
			buoys = new List<Buoy>();
			sections = result.Sections.ToList();
			FirstAfterFinish = FindFirstAfterFinish();
		}
		else
			Adopt( result );
		if ( savedBuoyCount is { } count && count != buoys.Count )
			throw new InvalidDataException( $"A saved controller holds {count} buoys, but its rebuilt circuit has {buoys.Count}." );
		IsTrackValid = valid;
		TrackWordWritten = true;
		foreach ( var vehicle in vehicles )
		{
			vehicle.ObjectIndex = -1;
			vehicle.ObjectOwner = null;
		}
	}

	/// <summary>The bumper arena of a park-resume restore of a shared-module save: present when the save placed its object, else dropped (it did not fit the pools when built).</summary>
	internal void RestoreArena( bool present, int? savedBuoyCount = null )
	{
		if ( !Template.IsBumper )
			return;
		if ( present )
			BuildArena();
		else
		{
			module.ForgetObjectsOf( this );
			module.PlaceObjects( this, Array.Empty<CollisionObject>() );
			objects = new List<CollisionObject>();
			buoys = new List<Buoy>();
			foreach ( var vehicle in vehicles )
			{
				vehicle.ObjectIndex = -1;
				vehicle.ObjectOwner = null;
			}
		}
		if ( savedBuoyCount is { } count && count != buoys.Count )
			throw new InvalidDataException( $"A saved bumper ride holds {count} buoys, but its rebuilt arena has {buoys.Count}." );
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
				Start();
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
				{
					State = StateBroken;
					StartSmoke();
				}
				else
					Fix();
				// the routine has no return value, and the script's accumulator stays what it was (Visitors.cs: PreserveInput)
				return 0;
			case 9:
				// [BIN:TPW-EXE:0x00554895 BUMP 9] a nonzero parameter marks the ride worn out (state 1), zero fixes it (state 0)
				// [BIN:TPW-EXE:0x00544C61 BUMP 9] the only writer of controller +0x54 = 1 is unconditional (it also overwrites a break); the only reader of +0x54 == 1 is the kart exhaust branch of 0x547F50 (0x5486FC), so the worn state changes nothing for boats or bumper cars (+0x54 == 2 is read at 0x547DC4, 0x547E2C and 0x547F8E)
				// [BIN:TPW-EXE:0x005548C3 BUMP 9 fix] BUMP 9 with 0 calls the same fix routine as BUMP 8 with 0 (0x544E50): the state is cleared, but the emitters die only when the old state was broken
				if ( parameter != 0 )
					State = StateWorn;
				else
					Fix();
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
				SetWater( parameter );
				return 0;
			default:
				return 0;
		}
	}

	/// <summary>
	/// The break smoke: BUMP 8 gives every car that carries guests (flag 0x4000), has a sparks node and no emitter yet a particle-2 emitter that follows it
	/// (cars created later get none); the fix and the unload remove it.
	/// </summary>
	// [BIN:TPW-EXE:0x00544C80 BUMP 8 break] for every vehicle of the controller with flag 0x4000, a sparks node (+0x24 not −1) and no emitter (+0x2C) the routine creates 0x521E60(2, …) (Smoke) and stores it in +0x2C; 0x547F50 moves it to the node every tick (0x51FE30); the fix 0x544E50 and the unload 0x54AE50 destroy it (0x51FEB0); water boats have no sparks node
	private void StartSmoke()
	{
		if ( IsWater || !HasSparksNode )
			return;
		foreach ( var vehicle in vehicles )
		{
			if ( !vehicle.IsLive || vehicle.Smoke || (vehicle.Flags & BumpVehicle.Steerable) == 0 )
				continue;
			vehicle.Smoke = true;
			Raise( BumpEffectKind.EmitterStart, vehicle, BumpEffects.ParticleSmoke );
			// [BIN:TPW-EXE:0x00544DE9 jungle break] for the jungle type −1 the new emitter is followed by animation 12 (the presentation object stops)
			if ( Type == -1 )
				SetAnimation( vehicle, 12 );
		}
	}

	/// <summary>The fix routine of BUMP 8 and BUMP 9 with 0: state 0 always, the smoke emitters (and the jungle cars' animation 5) only when the ride was broken.</summary>
	// [BIN:TPW-EXE:0x00544E50 fix] +0x54 is cleared; only when it was 2 does the routine walk the cars: for types −14..−3 and −1 each car with an emitter destroys it (0x51FEB0) and, for the jungle type −1 with flag 0x4000, plays animation 5 (0x544F2E..0x544F76)
	private void Fix()
	{
		var old = State;
		State = StateFine;
		if ( old == StateBroken )
			StopAllSmoke();
	}

	private void StopAllSmoke()
	{
		foreach ( var vehicle in vehicles )
		{
			var had = vehicle.Smoke;
			StopSmoke( vehicle );
			if ( had && Type == -1 && (vehicle.Flags & BumpVehicle.Steerable) != 0 )
				SetAnimation( vehicle, 5 );
		}
	}

	private void SetAnimation( BumpVehicle vehicle, int animation )
	{
		vehicle.Animation = animation;
		Raise( BumpEffectKind.Animation, vehicle, animation );
	}

	private void StopSmoke( BumpVehicle vehicle )
	{
		if ( !vehicle.Smoke )
			return;
		vehicle.Smoke = false;
		Raise( BumpEffectKind.EmitterStop, vehicle, BumpEffects.ParticleSmoke );
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
		// [BIN:TPW-EXE:0x00544840 open ride] the bumper types fall into the default case: the phase becomes 1 whatever it was, then the performance is pushed
		if ( Template.IsBumper )
		{
			Phase = PhaseLoading;
			ApplyPerformance();
			return;
		}
		if ( Phase != PhaseClosed )
			return;
		FirstAfterFinish = FindFirstAfterFinish();
		Phase = IsWater ? PhaseRunning : PhaseLoading;
		ApplyPerformance();
	}

	/// <summary>BUMP 6: closes the ride; the waiting guests go to the leaving list.</summary>
	// [BIN:TPW-EXE:0x00544A10 close ride] karts parked on the grid stay aboard when the ride closes during loading (the routine touches only the lists and the phase); the script recovers them with BUMP 10 (unload every kart, close) and a reopen (BUMP 7) leaves them parked for BUMP 3. Phase 0 and the pending list moves behind the leaving list; only the bumper cars turn to the unload point at once, karts and water boats finish their run (a vehicle ends it at its next finish crossing with the phase 0)
	public void Close()
	{
		if ( Phase == PhaseClosed )
			return;
		Phase = PhaseClosed;
		Leaving.AddRange( Pending );
		Pending.Clear();
		if ( !Template.IsBumper )
			return;
		// [BIN:TPW-EXE:0x00544A10 close ride] every bumper car of the controller gets laps 0 and flags (flags & 0xFFFFBFA1) | 0x10020: it stops carrying, heads for the end of the session and hands its rider over at its next step
		foreach ( var vehicle in vehicles )
		{
			vehicle.Laps = 0;
			vehicle.Flags = (vehicle.Flags & unchecked((int)0xFFFFBFA1)) | BumpVehicle.Retarget | BumpVehicle.ToUnload;
		}
	}

	/// <summary>BUMP 3: the start of a kart race; water rides ignore it.</summary>
	// [BIN:TPW-EXE:0x00544F90 start bump ride] not for the water types: the phase goes from 1 to 2 and every vehicle of the controller clears flags 0x80000 and 0x40 (parked, grid mode), takes the controller's laps (+4) as its lap counter and is relaunched (0x0054A040); "Start Bump Ride" is logged
	public void Start()
	{
		if ( IsWater || Phase != PhaseLoading )
			return;
		Phase = PhaseRunning;
		foreach ( var vehicle in vehicles.ToArray() )
		{
			vehicle.Flags &= unchecked((int)0xFFF7FFBF);
			vehicle.Laps = Laps;
			Relaunch( vehicle );
			// [BIN:TPW-EXE:0x00544F90 start bump ride] the jungle bumper (type −1) switches every car that carries a guest (flag 0x4000) to animation 5
			if ( Type == -1 && (vehicle.Flags & BumpVehicle.Steerable) != 0 )
				vehicle.Animation = 5;
		}
	}

	// [BIN:TPW-EXE:0x005549ED BUMP 17] the parameter is compared with the sign-extended word +0xE6 and an equal value does nothing (0x554A09); otherwise the word takes it and
	// 0x0052A490 and 0x0052A700 walk the circuit from the station's two sides with flag 1 for a zero parameter (0x554A39) and 0 for any other (0x554A1A)
	private void SetWater( int parameter )
	{
		if ( (short)WaterCommand == parameter )
			return;
		WaterCommand = (short)parameter;
		WaterRunning = parameter == 0;
		WaterPiecesTriggered?.Invoke( parameter == 0 );
	}

	// ---- Vehicles ----------------------------------------------------------------------------------

	/// <summary>BUMP 4: launches a boat at the station with the waiting guests aboard.</summary>
	// [BIN:TPW-EXE:0x00549DB0 create vehicle] needs a running count below the type's maximum and 64, and a nonzero phase; the boat starts at the station reference with the controller's laps, takes the pending guests and is launched (0x0054A040)
	public bool Launch()
	{
		// [EXT:cheats-ride-vehicles] the template's limit, or the cheat's raised one
		if ( VehicleCount >= VehicleLimit || VehicleCount >= MaximumVehicles || Phase == PhaseClosed )
			return false;
		// [BIN:TPW-EXE:0x0054A040 relaunch kart] the original accepts BUMP 4 in phase 3 but its relaunch gives such a kart no movement flags, so it would stand on the station for ever (no shipped script creates a kart after the start)
		// [EXT:late-kart-guard] a kart is refused while the race is over, where the original creates a kart that never moves and keeps the controller's count above 0 (its race never resets: a deadlock no shipped script reaches); OpenTPW does not reproduce the frozen kart
		if ( Template.IsKart && Phase == PhaseOver )
			return false;
		// [BIN:TPW-EXE:0x00549DB0 create vehicle] the record comes from the module's pool of 256, shared by every ride: a full pool refuses the vehicle
		var vehicle = new BumpVehicle { Flags = BumpVehicle.Live, X = StationX, Y = StationY, Radius = Template.VehicleRadius, Controller = this };
		var slot = module.AllocateVehicle( vehicle );
		if ( slot < 0 )
			return false;
		vehicle.Slot = slot;
		InsertSorted( vehicle );
		vehicle.Flags |= BumpVehicle.Steerable;
		VehicleCount++;
		vehicle.Laps = Laps;
		SetObject( vehicle, FirstContaining( StationX, StationY ) );
		vehicle.Riders.AddRange( Pending );
		Pending.Clear();
		vehicle.Flags |= BumpVehicle.FirstLaunch;
		vehicle.Variant = VehicleCount % ModelCount + ModelBase;
		// [BIN:TPW-EXE:0x00549DB0 create vehicle] the 0xAC-byte record is zeroed (rep stosd, 0x2B dwords) and neither 0x549DB0, 0x549C60 nor the water and kart cases of 0x54A040 write the steering heading +0x50 or the drawn heading +0x54: a boat or kart starts at heading 0 (+Y) and its first steering turns it; only a bumper car takes a random heading at its first launch (RelaunchBumper)
		vehicle.Heading = 0;
		vehicle.VisualHeading = 0;
		if ( IsWater )
		{
			// [BIN:TPW-EXE:0x0054A040 water first launch] the first launch (0x54A909) starts an engine voice from the ride's EventMap slot 0, kept until the boat unloads (0x51C300 stops it silently)
			vehicle.Engine = true;
			Raise( BumpEffectKind.EngineStart, vehicle, BumpEffects.SlotEngine );
		}
		else
		{
			// [BIN:TPW-EXE:0x00549DB0 create vehicle] a kart or bumper car plays animation 12 from its creation; the controller's first vehicle carries flag 0x400000 until it unloads
			vehicle.Animation = 12;
			if ( !vehicles.Any( other => (other.Flags & BumpVehicle.First) != 0 ) )
				vehicle.Flags |= BumpVehicle.First;
			if ( Template.IsKart )
				Raise( BumpEffectKind.EngineStart, vehicle, BumpEffects.SlotEngine );
		}
		// [BIN:TPW-EXE:0x00549FF5 creation heads] the creation hangs its riders' heads before the family's launch set-up (0x0054A040)
		HangRiderHeads( vehicle );
		RiderCount += vehicle.Riders.Count;
		Relaunch( vehicle );
		vehicle.Flags |= BumpVehicle.Launched;
		if ( Template.IsKart )
			Raise( BumpEffectKind.Particle, vehicle, BumpEffects.ParticleBeamUpCar );
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
		vehicle.SeatNumbers = null;
		RiderCount += Pending.Count;
		Pending.Clear();
		// [BIN:TPW-EXE:0x00549B80 load vehicle] the loading hangs the riders' heads with the beam flag (0x549C60(vehicle, 1)): each rider whose seat has a head dummy gets particle 0x13 at the vehicle's drawn place (its root matrix translation times 1024; OpenTPW uses the simulation position, PTCL-908); the creation (0x549DB0) hangs with 0, no beam; the load is shared by every vehicle family, so boats and karts beam too
		HangRiderHeads( vehicle );
		RaiseRiderBeams( vehicle, vehicle.Riders.Count );
		vehicle.Flags |= BumpVehicle.Steerable | BumpVehicle.Slowed;
		vehicle.Laps = Laps;
		Relaunch( vehicle );
		return true;
	}

	/// <summary>
	/// Which seats of a vehicle variant's mesh have a head dummy (seat k - 1 holds rider k), set by the host from the model so the loading beams do not wait for a draw;
	/// null where no host resolves meshes (a host that never loads the model, such as a bare simulation).
	/// </summary>
	public Func<int, bool[]?>? SeatHeadSource { get; set; }

	/// <summary>
	/// Hangs the heads of a vehicle's riders (0x00549C60): rider k takes head dummy k, and each rider whose seat has one goes through the head-hang routine (<see cref="RideScriptWorld.HeadHung"/>), in the list's order.
	/// </summary>
	// [BIN:TPW-EXE:0x00549CAF seat heads] the walk numbers the nodes 1 to n, looks up head dummy n (0x44B220) and calls 0x44B410 with the rider only when one is found; a vehicle whose mesh the host cannot read hangs none here (the seat flags are unknown)
	private void HangRiderHeads( BumpVehicle vehicle )
	{
		if ( module.Scripts is not { HeadHangReseeds: true } scripts )
			return;
		var seats = vehicle.SeatHeads ?? SeatHeadSource?.Invoke( vehicle.Variant );
		if ( seats == null )
			return;
		for ( var seat = 0; seat < vehicle.Riders.Count && seat < seats.Length; seat++ )
			if ( seats[seat] )
				scripts.HeadHung( vehicle.Riders[seat] );
	}

	/// <summary>One beam (particle 19) for each of the first <paramref name="count"/> riders of <paramref name="vehicle"/> whose seat has a head dummy.</summary>
	// [BIN:TPW-EXE:0x00549C60 beam flag] the beam is raised only for a rider whose head dummy handle (0x44B220 on the loaded model) is not -1; OpenTPW's own
	// fallback: where no host resolves the model's dummies, no rider is taken as seated and no beam is raised
	private void RaiseRiderBeams( BumpVehicle vehicle, int count )
	{
		// presentation only: without a listener (a headless run) the mesh is not even read
		if ( Effect == null )
			return;
		var seats = vehicle.SeatHeads ?? SeatHeadSource?.Invoke( vehicle.Variant );
		if ( seats == null )
			return;
		for ( var seat = 0; seat < count; seat++ )
		{
			if ( seat >= seats.Length || !seats[seat] )
				continue;
			Raise( BumpEffectKind.Particle, vehicle, BumpEffects.ParticleBeamUp );
		}
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
		// forward order: each retired boat's guests go to the front of the leaving list in turn
		while ( vehicles.Count > 0 )
			Retire( vehicles[0] );
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
		vehicle.SeatNumbers = null;
		StopSmoke( vehicle );
		if ( IsWater )
		{
			// [BIN:TPW-EXE:0x0054AE50 retire vehicle] the beam-up particle 15 plays for every type, and a non-kart voice is stopped silently (0x51C300): the boat's engine voice ends without a stop sound
			Raise( BumpEffectKind.Particle, vehicle, BumpEffects.ParticleBeamUpCar );
			StopEngine( vehicle );
		}
		else if ( Template.IsKart )
		{
			// [BIN:TPW-EXE:0x0054AE50 retire vehicle] the beam-up particle 15 (not in state 4), then the engine voice is replaced by EventMap slot 1
			Raise( BumpEffectKind.Particle, vehicle, BumpEffects.ParticleBeamUpCar );
			Raise( BumpEffectKind.EngineStop, vehicle, BumpEffects.SlotStop );
		}
		if ( Template.IsBumper )
		{
			// [BIN:TPW-EXE:0x0054AE50 retire vehicle] a bumper car also gets the beam-up particle, but its engine voice (the first car's) only stops: the EventMap stop sound is the kart types' case
			Raise( BumpEffectKind.Particle, vehicle, BumpEffects.ParticleBeamUpCar );
			StopEngine( vehicle );
		}
		vehicle.Flags &= ~(BumpVehicle.Live | BumpVehicle.First);
		module.FreeVehicle( vehicle );
		vehicles.Remove( vehicle );
		VehicleCount--;
		if ( VehicleCount == 0 && !IsWater )
			Phase = PhaseLoading;
	}

	/// <summary>The water relaunch: the boat aims at the first section buoy again in lap mode.</summary>
	// [BIN:TPW-EXE:0x0054A040 relaunch] water (types −5, −8, −12, −13): buoy countdown 0, flags keep only bit 0, 7 and 14, then lap mode with a new aim (0x10008); a first launch aims at controller +0xA4 and clears the accel bonus
	private void Relaunch( BumpVehicle vehicle )
	{
		if ( Template.IsKart )
		{
			RelaunchKart( vehicle );
			return;
		}
		if ( Template.IsBumper )
		{
			RelaunchBumper( vehicle );
			return;
		}
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

	/// <summary>
	/// The kart relaunch: while the ride loads (phase 1) the kart aims at the grid slot of its number (the vehicle count-th buoy with flag 2) in grid mode;
	/// during the race (phase 2) it aims on in lap mode and draws a new thrust bias from the world generator, (3 − (draw &amp; 7)) × accel / 9, the only
	/// difference between the karts.
	/// </summary>
	// [BIN:TPW-EXE:0x0054A040 relaunch kart] the first launch of a hovering kart (types −9, −10) sets flags 0x3C00; flags &= 0xFFFBFF81 and the buoy countdown is 0; phase 1: +0x7C = the count-th buoy with flag 2, flags |= 0x10048 (retarget, grid, lap-mode bit); phase 2: flags |= 0x10008, a target outside the buoy list becomes +0xA4, +0x68 = (3 − (rand &amp; 7)) × (accel / 9); the first-launch flag 0x100000 is cleared in every case
	// [BIN:TPW-EXE:0x0054A79E kart thrust draw] the draw is the world generator's (0x00516330): (3 - (draw & 7)) times accel / 9: the park's one stream, shared with the guests, the staff and the scripts
	private void RelaunchKart( BumpVehicle vehicle )
	{
		if ( (vehicle.Flags & BumpVehicle.FirstLaunch) != 0 && Type is -9 or -10 )
			vehicle.Flags |= BumpVehicle.HoverKartFlags;
		vehicle.BuoyCountdown = 0;
		vehicle.Flags &= unchecked((int)0xFFFBFF81);
		if ( Phase == PhaseLoading )
		{
			var slot = 0;
			var target = -1;
			foreach ( var buoy in buoys )
			{
				if ( buoy.IsSkipped && ++slot == VehicleCount )
				{
					target = buoy.Index;
					break;
				}
			}
			if ( target >= 0 )
				vehicle.TargetBuoy = target;
			vehicle.Flags |= BumpVehicle.Retarget | BumpVehicle.GridMode | BumpVehicle.LapMode;
		}
		else if ( Phase == PhaseRunning )
		{
			vehicle.Flags |= BumpVehicle.Retarget | BumpVehicle.LapMode;
			if ( vehicle.TargetBuoy < 0 || vehicle.TargetBuoy >= buoys.Count )
				vehicle.TargetBuoy = FirstAfterFinish?.Index ?? -1;
			vehicle.BuoyCountdown = 0;
			vehicle.AccelBonus = (3 - (int)(worldRandom() & 7)) * (Accel / 9);
		}
		vehicle.Flags &= ~BumpVehicle.FirstLaunch;
	}

	/// <summary>
	/// The bumper-car relaunch. A car's first launch drops it at a random free spot of the arena with a random heading and no passenger flag (0x4000); every
	/// launch then picks the next target: a random buoy of the ring, or, one time in four when the car is not parked or already wandering, the buoy after
	/// its current one. The buoy countdown is 3, so a car changes its mind after three captures.
	/// </summary>
	// [BIN:TPW-EXE:0x0054A040 drop-in overlap] the overlap test walks the module's whole vehicle pool (0x877B68, 0xAC-byte records, live flag 1, 0x54A0D8..0x54A18F), not only the ride's own cars; the chase candidates (0x54A495) need the same controller (+0x9C compared with +0x9C) and are the ride's own
	// [BIN:TPW-EXE:0x0054A040 relaunch bumper] first launch (flag 0x100000): up to 100 draws of (radius = rand % arena radius, angle = rand &amp; 0x1FF) around the station until no other vehicle overlaps, flag 0x4000 cleared, heading +0x50 = +0x54 = rand &amp; 0x1FF, +0xA4 = −1 (no wall-stuck retarget); the jungle bumper also gets flags 0x3000000 and a presentation object. Target: flag 8 set and rand &amp; 0xF ≤ 3 → the buoy after the current one, else buoy number rand % 8 counted from the head; flags (flags &amp; 0xFFFBFF89) | 0x10008 | 0x8000, countdown +0x8C = 3, flag 0x100000 cleared
	private void RelaunchBumper( BumpVehicle vehicle )
	{
		if ( (vehicle.Flags & BumpVehicle.FirstLaunch) != 0 )
		{
			var arena = Template.ArenaRadius;
			for ( var attempt = 100; attempt > 0; attempt-- )
			{
				var radius = (int)(worldRandom() % (uint)arena);
				var angle = (int)(worldRandom() & 0x1FF);
				var along = BumpMath.Sine[(angle + 0x80) & 0x1FF] * radius;
				var across = radius * BumpMath.Sine[angle];
				vehicle.X = ((along + ((along >> 31) & 0xFF)) >> 8) + StationX;
				vehicle.Y = StationY - ((across + ((across >> 31) & 0xFF)) >> 8);
				if ( attempt == 1 || !module.LiveVehicles.Any( other => other != vehicle && BumpMath.Distance( other.X - vehicle.X, other.Y - vehicle.Y ) < other.Radius + vehicle.Radius ) )
					break;
			}
			vehicle.Flags &= ~BumpVehicle.Steerable;
			// the jungle bumper car also gets flags 0x3000000: the wake object that hangs under it (mesh 0)
			if ( Type == -1 )
				vehicle.Flags |= 0x3000000;
			var heading = (int)(worldRandom() & 0x1FF);
			vehicle.Heading = heading;
			vehicle.VisualHeading = heading;
			vehicle.WallHits = -1;
		}
		// [BIN:TPW-EXE:0x0054A040 relaunch bumper] the first carrying car (flags 0x404000) of a running ride that has no voice yet starts the engine
		if ( !vehicle.Engine && (vehicle.Flags & (BumpVehicle.First | BumpVehicle.Steerable)) == (BumpVehicle.First | BumpVehicle.Steerable) && Phase == PhaseRunning && (vehicle.Flags & BumpVehicle.ToUnload) == 0 )
		{
			vehicle.Engine = true;
			Raise( BumpEffectKind.EngineStart, vehicle, BumpEffects.SlotEngine );
		}
		if ( TryStartChase( vehicle ) )
			return;
		var next = vehicle.TargetBuoy;
		int steps;
		if ( (vehicle.Flags & BumpVehicle.LapMode) == 0 || (worldRandom() & 0xF) > 3 || next < 0 || next >= buoys.Count )
		{
			steps = buoys.Count == 0 ? 0 : (int)(worldRandom() % (uint)buoys.Count);
			next = 0;
		}
		else
			steps = 1;
		vehicle.Flags = (vehicle.Flags & unchecked((int)0xFFFBFF89)) | BumpVehicle.Retarget | BumpVehicle.LapMode;
		if ( buoys.Count > 0 )
			vehicle.TargetBuoy = (next + steps) % buoys.Count;
		vehicle.Flags |= 0x8000;
		vehicle.BuoyCountdown = 3;
		vehicle.Flags &= ~BumpVehicle.FirstLaunch;
	}

	/// <summary>
	/// The chase choice of a relaunch: a car that is neither chasing nor bumped draws one in 16 to chase (3 of 16 chances in the original's draw: rand &amp; 0xF ≤ 2); it then picks a
	/// random other car of its arena that is not heading for the end of the session and locks on for 90 ticks.
	/// </summary>
	// [BIN:TPW-EXE:0x0054A040 chase choice] only when flags 0x40004 are clear and rand &amp; 0xF ≤ 2 and the controller has at least two cars: every other live car without flag 0x20 whose collision object is adjacent to this car's is a candidate, rand % count picks one (the list is built newest first), +0x80 = its slot, +0x8C = 0x5A, flags (flags &amp; 0xFFEBFF85) | 0x8004
	private bool TryStartChase( BumpVehicle vehicle )
	{
		if ( (vehicle.Flags & (BumpVehicle.Bumped | BumpVehicle.Chasing)) != 0 || (worldRandom() & 0xF) > 2 || VehicleCount < 2 )
			return false;
		var candidates = new List<BumpVehicle>();
		var current = ObjectOf( vehicle );
		foreach ( var other in vehicles )
		{
			if ( other == vehicle || !other.IsLive || (other.Flags & BumpVehicle.ToUnload) != 0 )
				continue;
			var theirs = ObjectOf( other );
			if ( current != null && theirs != null && Adjacent( current, theirs ) )
				candidates.Insert( 0, other );
		}
		if ( candidates.Count == 0 )
			return false;
		var target = candidates[(int)(worldRandom() % (uint)candidates.Count)];
		vehicle.ChaseTarget = target.Slot;
		vehicle.BuoyCountdown = 0x5A;
		vehicle.Flags = (vehicle.Flags & unchecked((int)0xFFEBFF85)) | BumpVehicle.Chasing | 0x8000;
		return true;
	}

	/// <summary>
	/// One step of a chasing car: it aims at where the target will be (its position plus its velocity times the ticks to reach it, at most 24), pulled back into the arena
	/// when that point lies outside it, and steers there; the chase ends after 90 ticks or when the target is bumped or goes home.
	/// </summary>
	// [BIN:TPW-EXE:0x005474B0 chase step] the countdown +0x8C falls each step; at 0, or when the target's flags &amp; 0x40021 are not exactly 1, the car relaunches; else lead = min(distance / own speed, 24) (own speed at least 1), aim = target position + velocity × lead; an aim outside the target's object and in no other keep-in object is moved onto the nearest object's rim (offset / 8 scaled to the radius over the offset's length + 0x20); the steering routine runs with that aim and clears flag 0x20000 within the buoy radius
	// the target is named by slot, as in the original (+0x80 is an index into the vehicle pool): a retired target ends the chase (its live flag is gone), but a car created into the freed slot within the 90 ticks is followed like the original follows it
	private void ChaseStep( BumpVehicle vehicle )
	{
		var target = module.VehicleAt( vehicle.ChaseTarget );
		vehicle.BuoyCountdown--;
		if ( vehicle.BuoyCountdown < 1 || target == null || (target.Flags & 0x40021) != 1 )
		{
			Relaunch( vehicle );
			return;
		}
		vehicle.Flags |= BumpVehicle.Retarget;
		var speed = Math.Max( BumpMath.Distance( vehicle.VX, vehicle.VY ), 1 );
		var distance = BumpMath.Distance( vehicle.X - target.X, vehicle.Y - target.Y );
		var lead = distance / speed < 0x19 ? distance / speed : 0x18;
		var aimX = target.VX * lead + target.X;
		var aimY = target.VY * lead + target.Y;
		var home = ObjectOf( target );
		if ( !Contains( aimX, aimY, home ) && FirstContaining( aimX, aimY ) == null && home != null && NextObject( home, aimX, aimY, includeSelf: true ) is { } near )
		{
			var ox = aimX - near.X;
			var oy = aimY - near.Y;
			ox = (ox + ((ox >> 31) & 7)) >> 3;
			oy = (oy + ((oy >> 31) & 7)) >> 3;
			var length = BumpMath.Distance( ox, oy ) + 0x20;
			aimX = ox * near.Extent / length + near.X;
			aimY = oy * near.Extent / length + near.Y;
		}
		var reached = Steer( vehicle, aimX, aimY );
		if ( (vehicle.Flags & BumpVehicle.Waypoint) != 0 && reached <= Template.BuoyRadius )
			vehicle.Flags &= ~BumpVehicle.Waypoint;
	}

	/// <summary>Stops the engine voice of a bumper car (the original only stops the voice, without the stop sound).</summary>
	private void StopEngine( BumpVehicle vehicle )
	{
		if ( !vehicle.Engine )
			return;
		vehicle.Engine = false;
		Raise( BumpEffectKind.EngineHalt, vehicle, BumpEffects.SlotEngine );
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

	/// <summary>Advances the controller by <paramref name="deltaSeconds"/>: whole 31 ms ticks run, the rest carries over.</summary>
	public void Advance( double deltaSeconds )
	{
		module.Advance( this, deltaSeconds );
	}

	/// <summary>The fractional tick time left over (saved with the controller).</summary>
	public double TickRemainder => module.TickRemainder;

	internal void RestoreTickRemainder( double remainder ) => module.RestoreTickRemainder( remainder );

	/// <summary>One module tick (the original's 0x00546C80): every ride's vehicles of this controller's module are stepped, integrated and collided in one pass (<see cref="BumpWorld.Tick"/>).</summary>
	public void Tick() => module.Tick();

	/// <summary>The first half of a vehicle's module tick: its step (0x5474B0); <see cref="IntegrateVehicle"/> follows while it lives.</summary>
	internal void StepVehicle( BumpVehicle vehicle ) => Step( vehicle );

	internal void IntegrateVehicle( BumpVehicle vehicle )
	{
		Integrate( vehicle );
		PassAddOn( vehicle );
	}

	/// <summary>
	/// The tail of every vehicle's integration: the add-on hook. Only the Space Racers Teleport (<see cref="TrackAddOns.TeleportTypeId"/>) has one: in its first tile a vehicle
	/// within <see cref="TrackAddOns.TeleportReach"/> of the centre along the tile's axis vanishes, remembering the side it came from, and the teleport queues its pass animation
	/// and then its idle loop; past that reach on the far side of the second tile the vehicle shows again, as it does wherever it sits in no add-on's object.
	/// </summary>
	// [BIN:TPW-EXE:0x00548C81 add-on hook] after the drawn heading (0x00548C70, the tail every vehicle family reaches) the vehicle's object +0x98 is tested: none ends the hook; without flag 0x100 a hidden vehicle (0x200) shows (0x00548E12); with it the section +0x18 must have the type id 0xDAC at +0x10 (0x00548CA5); d = x − the object's centre x when the kind word +4 is 0xC, else y − centre y; on the section's first tile (+8 = 0) a vehicle that is not hidden and within (−0x4B0, 0x4B0) takes flag 0x800000 for d &lt; 0 (clears it otherwise) and hides (0x00548E80(v, 1)); if the instance's channel 0 does not play animation 5 (0x00473FB0), its queued clip is dropped (0x00473270), animation 5 is triggered with flags 0 (0x004732A0: it waits behind a clip that still runs) and the section takes flag 2; on any later tile a hidden vehicle shows once d &gt; 0x4B0 with flag 0x800000 or d &lt; −0x4B0 without it; while the section has flag 2 and channel 0 has nothing queued (+0x24 = 0xC, 0x00473FF0), animation 2 is triggered with flags 1 (the loop) and flag 2 clears (0x00548DFC)
	private static void PassAddOn( BumpVehicle vehicle )
	{
		if ( ObjectOf( vehicle ) is not { } item )
			return;
		if ( (item.Flags & CollisionObject.AddOn) == 0 )
		{
			if ( (vehicle.Flags & BumpVehicle.Hidden) != 0 )
				Show( vehicle, false );
			return;
		}
		if ( item.Section is not { AddOn: { TypeId: TrackAddOns.TeleportTypeId } addOn } section )
			return;
		// the kind word is compared whole, so an add-on among the first two straights (no-buoy bit 0x10000) measures along y whatever its axis
		var offset = section.KindWord == 12 ? vehicle.X - item.X : vehicle.Y - item.Y;
		if ( section.AddOnRun == 0 )
		{
			if ( (vehicle.Flags & BumpVehicle.Hidden) == 0 && offset is < TrackAddOns.TeleportReach and > -TrackAddOns.TeleportReach )
			{
				vehicle.Flags = offset < 0 ? vehicle.Flags | BumpVehicle.FromNegative : vehicle.Flags & ~BumpVehicle.FromNegative;
				Show( vehicle, true );
				if ( addOn.Animation != TrackAddOns.PassAnimation )
				{
					addOn.FlushDeferred();
					addOn.Queue( TrackAddOns.PassAnimation, loop: false );
					section.AddOnPassing = true;
				}
			}
		}
		else if ( (vehicle.Flags & BumpVehicle.Hidden) != 0
			&& ((vehicle.Flags & BumpVehicle.FromNegative) != 0 ? offset > TrackAddOns.TeleportReach : offset < -TrackAddOns.TeleportReach) )
			Show( vehicle, false );
		if ( section.AddOnPassing && addOn.DeferredAnimation == ObjectAnimator.NoAnimation )
		{
			addOn.Queue( TrackAddOns.IdleAnimation, loop: true );
			section.AddOnPassing = false;
		}
	}

	/// <summary>Hides or shows a vehicle (0x00548E80): the flag the renderer, the riders' heads and the vehicle collisions read.</summary>
	// [BIN:TPW-EXE:0x00548E80 vehicle hide] hiding sets flag 0x200 and hides the vehicle's instance, its shadow instance (flags 0x2002000 with +0xC) and its riders' heads (0x0044B4C0 for every rider of the list +0x30); showing clears the flag, shows both instances and hangs the heads again (0x0044B410); either does nothing when the flag already says so
	private static void Show( BumpVehicle vehicle, bool hide ) => vehicle.Flags = hide ? vehicle.Flags | BumpVehicle.Hidden : vehicle.Flags & ~BumpVehicle.Hidden;

	/// <summary>The rest of a vehicle's module tick after its collisions: the stuck check, the walls, the keep-out circles and the effect player.</summary>
	internal void FinishVehicle( BumpVehicle vehicle )
	{
		StuckCheck( vehicle );
		KeepIn( vehicle );
		KeepOut( vehicle );
		if ( !IsWater )
			PlayEffect( vehicle );
	}

	/// <summary>The collision object a vehicle sits in: one of its own ride's, or another ride's (the searches are module-wide), or null.</summary>
	internal static CollisionObject? ObjectOf( BumpVehicle vehicle )
	{
		var owner = vehicle.ObjectOwner ?? vehicle.Controller;
		return owner != null && vehicle.ObjectIndex >= 0 && vehicle.ObjectIndex < owner.objects.Count ? owner.objects[vehicle.ObjectIndex] : null;
	}

	/// <summary>Records the object a vehicle sits in (null: none); an object of another ride is kept with its owner.</summary>
	private static void SetObject( BumpVehicle vehicle, CollisionObject? item )
	{
		vehicle.ObjectIndex = item?.Index ?? -1;
		vehicle.ObjectOwner = item?.Owner is { } owner && owner != vehicle.Controller ? owner : null;
	}

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

	// [BIN:TPW-EXE:0x005476FE module object search] every object search walks the module pool 0x877B70 (2048 records), whichever ride owns the objects
	private CollisionObject? FirstContaining( int x, int y )
	{
		foreach ( var item in module.Objects )
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
		foreach ( var item in module.Objects )
		{
			if ( (item.Flags & CollisionObject.Used) == 0 || item.IsKeepOut || !includeSelf && current == item || !Adjacent( current, item ) )
				continue;
			var (wx, wy) = Waypoint( item );
			if ( module.Objects.Any( other => (other.Flags & CollisionObject.Used) != 0 && other.IsKeepOut && Contains( wx, wy, other ) ) )
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
		// [BIN:TPW-EXE:0x005474B0 duration countdown] a lap counter at or above 0 is a duration: while the ride runs (phase 2) and is not broken it counts down once per tick, and at 0 the vehicle goes to the unload point (flags & 0xFFFFFFA1 | 0x10020); no kart or water script sets it (BUMP 14 stores a negative lap count); the water boats skip it here, so a boat that ended its run keeps its lap counter at 0 as phase 1 pinned
		// [BIN:TPW-EXE:0x005474B0 session countdown] the bumper cars' duration is the same counter: BUMP 13 stores seconds × 30 and BUMP 3 hands it to every car; at 0 the bumper types also drop the carrying flag 0x4000 before they head for the end of the session
		if ( !IsWater && vehicle.Laps >= 0 && Phase == PhaseRunning && State != StateBroken )
		{
			vehicle.Laps--;
			if ( vehicle.Laps == 0 )
			{
				if ( Template.IsBumper )
					vehicle.Flags &= ~BumpVehicle.Steerable;
				vehicle.Flags = (vehicle.Flags & unchecked((int)0xFFFFFFA1)) | BumpVehicle.Retarget | BumpVehicle.ToUnload;
			}
		}
		if ( (vehicle.Flags & BumpVehicle.Chasing) != 0 )
		{
			if ( Template.IsBumper )
				ChaseStep( vehicle );
			return;
		}
		if ( (vehicle.Flags & BumpVehicle.ToUnload) != 0 )
		{
			if ( Template.IsBumper )
			{
				EndBumperSession( vehicle );
				return;
			}
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
		if ( (vehicle.Flags & BumpVehicle.LapMode) != 0 && (vehicle.Flags & BumpVehicle.GridMode) != 0 )
		{
			// [BIN:TPW-EXE:0x005474B0 grid slot] within a quarter of the buoy radius of its slot a kart parks: flag 0x80000 (damping × 0.7, no thrust) and its target becomes +0xA4, the buoy after the finish, where it waits for BUMP 3
			if ( captured <= Template.BuoyRadius / 4 )
			{
				vehicle.Flags |= BumpVehicle.Slowed | BumpVehicle.Retarget;
				vehicle.TargetBuoy = FirstAfterFinish?.Index ?? vehicle.TargetBuoy;
				return;
			}
		}
		else if ( (vehicle.Flags & BumpVehicle.LapMode) != 0 && (vehicle.Flags & BumpVehicle.GridMode) == 0 )
		{
			if ( buoy.IsFinish )
			{
				// laps run from a negative count up to 0; a closed ride or a finished race ends the run at once, without counting the crossing
				// [BIN:TPW-EXE:0x005474B0 finish buoy] phase 3 or 0 ends the run; otherwise ++laps (+0x88) ends it at 0; for the kart types a run ending in phase 2 sets phase 3 ("GoKart Race Over"), so the first kart home ends the race and the others go home at their next crossing
				var ended = Phase == PhaseOver || Phase == PhaseClosed;
				if ( !ended )
				{
					vehicle.Laps++;
					ended = vehicle.Laps == 0;
				}
				if ( ended )
				{
					if ( Template.IsKart && Phase == PhaseRunning )
						Phase = PhaseOver;
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
				SetObject( vehicle, buoy.Object );
			AimAtNext( vehicle, NextBuoy( buoy ) );
		}
		vehicle.Flags |= BumpVehicle.Retarget;
	}

	/// <summary>
	/// A bumper car at the end of its session: it hands every rider to the leaving list at once (the ride's script collects them with BUMP 2), and when
	/// that leaves nobody on any car of the controller, the ride goes back to loading and the car stops.
	/// </summary>
	// [BIN:TPW-EXE:0x005474B0 bumper unload] the 0x20 branch for types −1, −6, −11, −14 calls 0x0054AC70 (riders to the front of the leaving list one by one, the rider count +0x60 falls, the head sprite goes); when the controller has no rider left its phase becomes 1 ("loading"), the car's flags &amp;= 0xFFFFFF81 (no more wandering or unloading) and its animation 12; otherwise the car repeats the call at its next step, so the last car's step ends the session for every car
	private void EndBumperSession( BumpVehicle vehicle )
	{
		// [BIN:TPW-EXE:0x005474B0 bumper unload] 0x54AC70(vehicle, 1) takes the riders one by one and, for each one whose head was hung, raises particle 0x13 at the vehicle's drawn place (0x54acdb-0x54ad24; only while the head is still hung, [rider+8] != -1, where OpenTPW checks the seat's head dummy; OpenTPW uses the simulation position, PTCL-908); the retire (0x54AE50) passes 0 and raises none
		RaiseRiderBeams( vehicle, vehicle.Riders.Count );
		foreach ( var guest in vehicle.Riders )
		{
			Leaving.Insert( 0, guest );
			RiderCount--;
		}
		vehicle.Riders.Clear();
		StopEngine( vehicle );
		if ( RiderCount > 0 )
			return;
		Phase = PhaseLoading;
		vehicle.Flags &= unchecked((int)0xFFFFFF81);
		if ( vehicle.Animation != 12 )
		{
			vehicle.Animation = 12;
			Raise( BumpEffectKind.Animation, vehicle, 12 );
		}
	}

	/// <summary>Aims at <paramref name="next"/> with a fresh jitter: the new value on X, the previous X on Y.</summary>
	// [BIN:TPW-EXE:0x00516330 world rng] the jitter is the buoy radius / 2 minus a world-generator draw modulo the radius; the new draw goes to X and the previous X to Y
	// [BIN:TPW-EXE:0x00547B9A jitter draw] the draw is the world generator's (0x00516330; the same step is at 0x005470D3): radius / 2 - draw % radius: the park's one stream, shared with the guests, the staff and the scripts
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
		if ( Template.IsKart )
		{
			IntegrateKart( vehicle );
			return;
		}
		if ( Template.IsBumper )
		{
			IntegrateBumper( vehicle );
			return;
		}
		if ( !IsWater )
			return;
		if ( Math.Abs( vehicle.Spin ) > 0x30 )
			vehicle.Spin = (int)(vehicle.Spin * 99.80000305175781 * 0.009999999776482582);
		var roll = (vehicle.Spin + ((vehicle.Spin >> 31) & 0x7F)) >> 7;
		vehicle.Roll = Math.Clamp( roll, -16, 16 );
		// [BIN:TPW-EXE:0x005480B7 boat spray] the water branch of 0x547F50: in phase 2 on every 32nd module tick the cosmetic LCG gives (s >> 16) &amp; 7 == 2 and 0x521E60(0x60, …) raises particle 96 (P_EFFECT_SmallSplash) at the boat model's world position (0x5480B7..0x5480FC); the water types have no crash cue (absent from 0x546C80's switch), no horn and no smoke (node +0x24 stays −1), and their templates hold one model (base 0, count 1)
		if ( Phase == PhaseRunning && (module.FrameCount & 0x1F) == 0 && (world?.NextCosmetic() ?? 0) is var draw && ((draw >> 16) & 7) == 2 )
		{
			vehicle.Sprays++;
			Sprays++;
			Raise( BumpEffectKind.Particle, vehicle, BumpEffects.ParticleSmallSplash );
		}
		// [BIN:TPW-EXE:0x00548104 boat engine voice] a boat holding a voice (+0x20) moves it and sets its parameter to speed / 3 every tick, as the karts do
		Raise( BumpEffectKind.EngineParameter, vehicle, BumpEffects.SlotEngine, vehicle.Speed / 3 );
	}

	/// <summary>The break smoke's emitter follows its car every tick (0x547F50 moves it to the sparks node, 0x51FE30).</summary>
	private void FollowSmoke( BumpVehicle vehicle )
	{
		if ( vehicle.Smoke )
			Raise( BumpEffectKind.EmitterMove, vehicle, BumpEffects.ParticleSmoke );
	}

	/// <summary>
	/// The tail of a kart's integration: the drawn heading follows the velocity, the horn and exhaust requests of the 32nd tick, and the animation switch at
	/// the speed threshold; the engine voice gets its pitch every tick.
	/// </summary>
	// [BIN:TPW-EXE:0x00547F50 integrate kart] the velocity angle a (512ths) moves the drawn heading +0x54 by ((a − h) mod 512, wrapped to ±256) × speed / 1000 per tick (+0x5C, with its change +0x60); racing (phase 2) on every 32nd tick above speed 230 with no pending effect asks for effect 2 (horn); worn (+0x54 = 1) above speed 80 with a sparks node a draw r = (lcg >> 16) &amp; 7 gives sparks (r &lt; 2) or an exhaust puff and effect 1 (r = 2); speed ≤ 40 sets animation 12 (not for the hovering karts, flag 0x400), above sets 5; the engine voice's parameter 10 is speed / 3
	private void IntegrateKart( BumpVehicle vehicle )
	{
		var speed = vehicle.Speed;
		if ( Phase == PhaseRunning && (module.FrameCount & 0x1F) == 0 && speed > BumpEffects.TootSpeed && vehicle.EffectRequest == 0 )
			vehicle.EffectRequest = BumpEffects.RequestToot;
		if ( speed <= BumpEffects.AnimationSpeed )
		{
			if ( vehicle.Animation == 5 && (vehicle.Flags & BumpVehicle.Hover) == 0 )
			{
				vehicle.Animation = 12;
				Raise( BumpEffectKind.Animation, vehicle, 12 );
			}
		}
		else if ( vehicle.Animation == 12 )
		{
			vehicle.Animation = 5;
			Raise( BumpEffectKind.Animation, vehicle, 5 );
		}
		if ( Phase == PhaseRunning && State == StateWorn && (module.FrameCount & 0x1F) == 0 && speed > BumpEffects.ExhaustSpeed && HasSparksNode )
		{
			var draw = ((world?.NextCosmetic() ?? 0) >> 16) & 7;
			if ( draw < 2 )
				Raise( BumpEffectKind.Particle, vehicle, BumpEffects.ParticleSparks );
			else if ( draw == 2 && vehicle.EffectRequest == 0 )
			{
				Raise( BumpEffectKind.Particle, vehicle, BumpEffects.ParticleExhaustPuff );
				vehicle.EffectRequest = BumpEffects.RequestExhaust;
			}
		}
		Raise( BumpEffectKind.EngineParameter, vehicle, BumpEffects.SlotEngine, speed / 3 );
		FollowSmoke( vehicle );
		var heading = BumpMath.HeadingOf( vehicle.VX, vehicle.VY );
		var difference = (heading - vehicle.VisualHeading) & 0x1FF;
		if ( difference > 0x100 )
			difference -= 0x200;
		var previous = vehicle.YawRate;
		vehicle.YawRate = difference * speed / 1000;
		vehicle.YawDelta = vehicle.YawRate - previous;
		vehicle.VisualHeading = (vehicle.VisualHeading + vehicle.YawRate) & 0x1FF;
	}

	/// <summary>
	/// The tail of a bumper car's integration: the drawn heading follows the aimed heading (the steering heading for the jungle cars, the direction of the velocity for the others)
	/// by (angle difference × speed / 1000) per tick while the ride runs; carrying cars of the non-jungle types throw sparks now and then; the engine voice gets its pitch.
	/// </summary>
	// [BIN:TPW-EXE:0x00547F50 integrate bumper] phase 2: +0x58 = the steering heading +0x50 (type −1) or the velocity angle, +0x5C = ((+0x58 − +0x54) mod 512, wrapped to ±256) × speed / 1000, else 0; +0x54 += +0x5C at the end of every step; phase 2, carrying (0x4000), every 32nd tick, a sparks node, a cosmetic draw with (lcg &gt;&gt; 16) &amp; 3 = 2, not type −1 and no pending effect: particle 1 and effect 1 (cat_rides 24); the engine voice's parameter 10 is speed / 3
	private void IntegrateBumper( BumpVehicle vehicle )
	{
		var speed = vehicle.Speed;
		if ( Phase == PhaseRunning )
		{
			var aimed = Type == -1 ? vehicle.Heading : BumpMath.HeadingOf( vehicle.VX, vehicle.VY );
			var difference = (aimed - vehicle.VisualHeading) & 0x1FF;
			if ( difference > 0x100 )
				difference -= 0x200;
			vehicle.YawRate = difference * speed / 1000;
		}
		else
			vehicle.YawRate = 0;
		if ( Phase == PhaseRunning && (vehicle.Flags & BumpVehicle.Steerable) != 0 && (module.FrameCount & 0x1F) == 0 && HasSparksNode
			&& (((world?.NextCosmetic() ?? 0) >> 16) & 3) == 2 && Type != -1 && vehicle.EffectRequest == 0 )
		{
			Raise( BumpEffectKind.Particle, vehicle, BumpEffects.ParticleSparks );
			vehicle.EffectRequest = BumpEffects.RequestExhaust;
		}
		if ( vehicle.Engine )
			Raise( BumpEffectKind.EngineParameter, vehicle, BumpEffects.SlotEngine, speed / 3 );
		FollowSmoke( vehicle );
		vehicle.VisualHeading = (vehicle.VisualHeading + vehicle.YawRate) & 0x1FF;
	}

	/// <summary>The effect player of a vehicle (the end of its tick): the request is latched at the higher code, a latched effect plays once.</summary>
	// [BIN:TPW-EXE:0x00547170 effect player] request (+0x14) is taken into the latch (+0x18) when higher and cleared; 1 plays global cat_rides 24, 2 draws the module LCG and plays EventMap slot 3 (TOOT) when (lcg &amp; 0x70000) == 0, else stays latched for the next tick, 3 plays global cat_rides 12 (CRUNCH); a played effect clears the latch
	// [BIN:TPW-EXE:0x00547170 effect player] code 1 plays cat_rides 0x18 (24), 3 and 5 play 0x0C (12), 2 EventMap slot 3 (TOOT), 4 slot 4; DAT_00803A28 is the cat_rides category (0x51EBC4) and cat_ridesSFX.map holds events 12 and 24 (one-shots, one sound each); the global route resolves the same category by id
	private void PlayEffect( BumpVehicle vehicle )
	{
		var request = vehicle.EffectRequest;
		if ( request != 0 )
		{
			if ( vehicle.EffectLatch < request )
				vehicle.EffectLatch = request;
			vehicle.EffectRequest = 0;
		}
		switch ( vehicle.EffectLatch )
		{
			case BumpEffects.RequestExhaust:
				Raise( BumpEffectKind.GlobalSound, vehicle, BumpEffects.SoundExhaust );
				vehicle.EffectLatch = 0;
				break;
			case BumpEffects.RequestToot:
				if ( (((world?.NextCosmetic() ?? 0) & 0x70000) == 0) )
				{
					Raise( BumpEffectKind.EventSound, vehicle, BumpEffects.SlotToot );
					vehicle.EffectLatch = 0;
				}
				break;
			case BumpEffects.RequestCrash:
			case BumpEffects.RequestBumperCrash:
				Raise( BumpEffectKind.GlobalSound, vehicle, BumpEffects.SoundCrunch );
				vehicle.EffectLatch = 0;
				break;
			default:
				vehicle.EffectLatch = 0;
				break;
		}
	}

	// ---- Collisions --------------------------------------------------------------------------------

	/// <summary>Pushes <paramref name="vehicle"/> and <paramref name="other"/> apart when their circles overlap, exchanging momentum when they close in.</summary>
	// [BIN:TPW-EXE:0x00546C80 vehicle collisions] closing (relative velocity within a quarter turn of the line between them): the normal part of the relative speed × the other's restitution goes to the other vehicle, the rest back to this one; otherwise they are pushed apart by the overlap, the other by its restitution share
	// [BIN:TPW-EXE:0x00546E52 shared pass] the pair loop runs over the whole module pool: the restitution is the other vehicle's controller's (+0x9C → +0x2C) and the crash code this vehicle's controller type, so vehicles of neighbouring rides push each other
	internal void Collide( BumpVehicle vehicle, BumpVehicle other )
	{
		if ( other == vehicle || !other.IsLive || ((ObjectOf( other )?.Flags ?? 0) & CollisionObject.Crossing) != 0 || (other.Flags & BumpVehicle.Hidden) != 0 )
			return;
		var restitution = other.Controller?.Restitution ?? Restitution;
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
			var share = BumpMath.Div1024( restitution * normal );
			other.VX += BumpMath.Div1024( share * BumpMath.Sin( direction ) * 4 );
			other.VY += BumpMath.Div1024( share * BumpMath.Cos( direction ) * 4 );
			other.Flags |= BumpVehicle.Bumped | BumpVehicle.Retarget;
			var rest = normal - share;
			vehicle.VX -= BumpMath.Div1024( rest * BumpMath.Sin( direction ) * 4 );
			vehicle.VY -= BumpMath.Div1024( rest * BumpMath.Cos( direction ) * 4 );
			// [BIN:TPW-EXE:0x00546C80 crash] a kart pair whose impulse (the share handed to the other) exceeds 0x82 asks for the CRUNCH effect (code 3) once per tick: both get flag 0x200000 (the bumper cars ask for code 5, the water boats make no sound)
			// [BIN:TPW-EXE:0x00546C80 crash] the switch on the controller type: −14, −11, −6 and −3 ask for code 5, the kart types for code 3; the jungle bumper (−1) has no case and makes no sound
			if ( (Template.IsKart || Template.IsBumper && Type != -1) && share > BumpEffects.CrashImpulse && ((vehicle.Flags | other.Flags) & BumpVehicle.Crunched) == 0 )
			{
				vehicle.Flags |= BumpVehicle.Crunched;
				other.Flags |= BumpVehicle.Crunched;
				vehicle.EffectRequest = Template.IsKart ? BumpEffects.RequestCrash : BumpEffects.RequestBumperCrash;
			}
		}
		else
		{
			var ox = dx - dx * reach / distance;
			var oy = dy - dy * reach / distance;
			var px = BumpMath.Div1024( ox * restitution );
			var py = BumpMath.Div1024( oy * restitution );
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
			SetObject( vehicle, found );
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
		foreach ( var circle in module.Objects )
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
		hash.Add( module.TickRemainder );
		hash.Add( StationX );
		hash.Add( StationY );
		hash.Add( sections.Count );
		foreach ( var section in sections )
		{
			hash.Add( section.KindWord );
			hash.Add( section.X );
			hash.Add( section.Y );
			if ( section.AddOn != null )
				hash.Add( section.AddOnPassing );
		}
		hash.Add( Pending.Count );
		foreach ( var guest in Pending )
			hash.Add( guest );
		hash.Add( Leaving.Count );
		foreach ( var guest in Leaving )
			hash.Add( guest );
		// [EXT:cheats-ride-vehicles] a raised limit joins the hash; every other controller keeps its pins
		if ( VehicleLimit != Template.MaxVehicles )
			hash.Add( VehicleLimit );
		// the model words (+0x14, +0x18) join the hash only where a save gave a ride words other than its template's
		if ( !HasTemplateModelWords )
		{
			hash.Add( ModelBase );
			hash.Add( ModelCount );
		}
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
			hash.Add( vehicle.Variant );
			hash.Add( vehicle.VX0 );
			hash.Add( vehicle.VY0 );
			hash.Add( vehicle.Speed );
			hash.Add( vehicle.Roll );
			hash.Add( vehicle.Radius );
			hash.Add( vehicle.AccelBonus );
			hash.Add( vehicle.TargetX );
			hash.Add( vehicle.TargetY );
			hash.Add( vehicle.JitterX );
			hash.Add( vehicle.JitterY );
			hash.Add( vehicle.BuoyCountdown );
			hash.Add( vehicle.Age );
			hash.Add( vehicle.ObjectIndex );
			// an object of another ride (the searches are module-wide) is named by its owner
			if ( vehicle.ObjectOwner is { } foreign )
				hash.Add( foreign.AttractionId );
			hash.Add( vehicle.WallHits );
			hash.Add( vehicle.WallWindow );
			hash.Add( vehicle.Sprays );
			if ( Template.IsKart )
			{
				// the kart's drawn heading, yaw, animation and effect latch are simulation state (the effects draw from the cosmetic generator), so they join the digest of kart controllers only
				hash.Add( vehicle.VisualHeading );
				hash.Add( vehicle.YawRate );
				hash.Add( vehicle.YawDelta );
				hash.Add( vehicle.Animation );
				hash.Add( vehicle.EffectRequest );
				hash.Add( vehicle.EffectLatch );
			}
			if ( Template.IsBumper )
			{
				// the chase target and the drawn heading are simulation state of the bumper cars (they steer the next relaunch and the drawn pose)
				hash.Add( vehicle.ChaseTarget );
				hash.Add( vehicle.VisualHeading );
				hash.Add( vehicle.YawRate );
			}
			hash.Add( vehicle.Riders.Count );
			foreach ( var rider in vehicle.Riders )
				hash.Add( rider );
		}
	}

	internal void Restore( BumpControllerSaveData data )
	{
		// the walked circuit decides how many buoys and collision objects exist, so the boats' indexes are checked against it before anything changes
		foreach ( var saved in data.Vehicles )
		{
			// an object of another ride (ObjectAttraction) is checked once every ride is back (BumpWorld.ResolveRestoredObjects)
			if ( saved.TargetBuoy >= buoys.Count || saved.ObjectAttraction < 0 && saved.ObjectIndex >= objects.Count || saved.Slot is < 0 or >= VehiclePool )
				throw new InvalidDataException( $"A saved boat aims at buoy {saved.TargetBuoy} or sits in object {saved.ObjectIndex}, but the walked circuit has {buoys.Count} buoys and {objects.Count} objects." );
		}
		if ( !double.IsFinite( data.TickRemainder ) || data.TickRemainder < 0 || data.TickRemainder >= MaximumSavedRemainder )
			throw new InvalidDataException( "A saved controller's tick remainder is outside one tick." );
		if ( data.VehicleLimit is { } limit && (!CanRaiseVehicleLimit || limit != RaisedVehicleLimit) )
			throw new InvalidDataException( $"A saved controller of type {Type} has the vehicle limit {limit}, which only a raised bumper or kart ride ({RaisedVehicleLimit}) has." );
		VehicleLimit = data.VehicleLimit ?? Template.MaxVehicles;
		if ( data.ModelBase.HasValue != data.ModelCount.HasValue )
			throw new InvalidDataException( "A saved controller holds only one of its two model words." );
		foreach ( var saved in data.Vehicles )
		{
			if ( saved.SeatNumbers is { } seats && (seats.Count != saved.Riders.Count || seats.Any( seat => seat is < 1 or > 255 ) || seats.Distinct().Count() != seats.Count) )
				throw new InvalidDataException( "A saved vehicle's seat numbers do not match its riders." );
		}
		RestoreModelWords( data.ModelBase, data.ModelCount );
		// a ride no edit has walked keeps the constructor's word (an older save holds no such field: its word was written)
		if ( data.TrackWordWritten == false )
		{
			IsTrackValid = data.IsTrackValid;
			TrackWordWritten = false;
		}
		Laps = data.Laps;
		SetPerformance( data.Performance );
		Phase = data.Phase;
		State = data.State;
		TickCount = data.TickCount;
		// the module clock: a save from before the shared module carries it on its controllers, the module's first controller drives it (the park restore then sets the saved module clock)
		if ( module.Controllers.Count == 0 || module.Controllers[0] == this )
		{
			module.RestoreTickRemainder( data.TickRemainder );
			module.FrameCount = data.TickCount;
		}
		WaterRunning = data.WaterRunning;
		// the load's script restore (0x005597A0) sets the BUMP 17 word to −1, so the next BUMP 17 always triggers the pieces
		WaterCommand = -1;
		Pending.Clear();
		Pending.AddRange( data.Pending );
		Leaving.Clear();
		Leaving.AddRange( data.Leaving );
		foreach ( var old in vehicles )
			module.FreeVehicle( old );
		vehicles.Clear();
		var moved = new Dictionary<int, int>();
		VehicleCount = 0;
		RiderCount = 0;
		foreach ( var saved in data.Vehicles )
		{
			var vehicle = new BumpVehicle
			{
				Slot = saved.Slot, Flags = saved.Flags, Variant = saved.Variant, X = saved.X, Y = saved.Y, VX = saved.VX, VY = saved.VY, VX0 = saved.VX0, VY0 = saved.VY0,
				Speed = saved.Speed, Heading = saved.Heading, Spin = saved.Spin, Roll = saved.Roll, Radius = saved.Radius, AccelBonus = saved.AccelBonus,
				TargetX = saved.TargetX, TargetY = saved.TargetY, JitterX = saved.JitterX, JitterY = saved.JitterY, TargetBuoy = saved.TargetBuoy, Laps = saved.Laps,
				BuoyCountdown = saved.BuoyCountdown, Age = saved.Age, ObjectIndex = saved.ObjectIndex, WallHits = saved.WallHits, WallWindow = saved.WallWindow, Sprays = saved.Sprays,
				VisualHeading = saved.VisualHeading, YawRate = saved.YawRate, YawDelta = saved.YawDelta, Animation = saved.Animation, EffectRequest = saved.EffectRequest,
				EffectLatch = saved.EffectLatch, Height = float.IsFinite( saved.Height ) ? saved.Height : 0f, ChaseTarget = saved.ChaseTarget, Engine = saved.Engine || Template.IsWater, Smoke = saved.Smoke,
				Controller = this, RestoredObjectAttraction = saved.ObjectAttraction
			};
			vehicle.Riders.AddRange( saved.Riders );
			vehicle.SeatNumbers = saved.SeatNumbers?.ToArray();
			// the saved pool slot; a save from before the shared pool may name a slot another ride's vehicle took, which then gets the first free one
			vehicle.Slot = module.PlaceVehicle( vehicle, saved.Slot );
			if ( vehicle.Slot < 0 )
				throw new InvalidDataException( "The module's vehicle pool cannot hold the saved vehicles." );
			moved[saved.Slot] = vehicle.Slot;
			InsertSorted( vehicle );
			VehicleCount++;
			RiderCount += vehicle.Riders.Count;
		}
		// a chase names a pool slot: a car of this ride that moved to another slot (a save from before the shared pool) is still the one chased
		foreach ( var vehicle in vehicles )
		{
			if ( vehicle.ChaseTarget >= 0 && moved.TryGetValue( vehicle.ChaseTarget, out var slot ) )
				vehicle.ChaseTarget = slot;
		}
	}
}
