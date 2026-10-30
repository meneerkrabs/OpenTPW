namespace OpenTPW;

/// <summary>Where a tour ride's launch pad is and which way it faces (the original reads model node 99 of the ride and its placement rotation).</summary>
/// <param name="X">Module units on the first ground axis (300 per model unit).</param>
/// <param name="Y">Height in module units.</param>
/// <param name="Z">Module units on the second ground axis.</param>
/// <param name="RotationDegrees">The placement rotation in degrees.</param>
public readonly record struct TourStation( int X, int Y, int Z, int RotationDegrees )
{
	/// <summary>The station of the tour controller's own tests: at the module's flight area centre, unrotated.</summary>
	public static TourStation Default => new( (TourWorld.MinimumX + TourWorld.MaximumX) / 2, 0, (TourWorld.MinimumZ + TourWorld.MaximumZ) / 2, 0 );
}

/// <summary>
/// The tour-ride controller of one original tour ride (Jurassic, Flightmare, Star and Tweety Tours): the module's 0x122C-byte controller with its 20 flying
/// cars. The ride has no track: its cars take off from the station, fly a random walk over the park's lattice for the ride's duration at a height that clears the
/// ground, return, circle the station until the landing slot is free and land, where the script unloads them and loads the next riders. The ride script
/// drives it through the <c>TOUR</c> opcode (<see cref="Command"/>). The controller ticks once per 62 ms: the original's tour tick runs on every second world step of 31 ms.
/// </summary>
/// <remarks>
/// [BIN:TPW-EXE:0x0055ABF0 tour tick] the per-tick state machine over the controllers and their cars, called from 0x0054F5EF only on even step counts (0x0054F5D7 tests the low bit of the step counter [0x00877D34], which 0x0054F4D3 raises once per 31 ms step of 0x0054E360): one tick per 62 ms. The draw interpolation divides by the same 62 ms (the constant at 0x00700F90 is 1/62).
/// [BIN:TPW-EXE:0x005542FE TOUR] the opcode's handler: command number − 1 indexes the jump table 0x5569D0 (18 entries).
/// </remarks>
// [EXT:tour-fixed-step] the controller ticks from OpenTPW's fixed simulation clock, one tick per 62 ms; the original's world step is a catch-up loop on the wall clock (next step += 31 at 0x0054F4C4, a backlog over 2000 ms dropped at 0x0054F493..0x0054F49B) whose phase against the script loop is not reproduced (a determinism decision, docs/DETERMINISM.md)
public sealed class TourController
{
	/// <summary>The cars a controller holds.</summary>
	public const int CarSlots = 20;
	/// <summary>[EXT:cheats-ride-vehicles] The cars a tour ride holds once the cheats window raised its vehicle limit (the user's ceiling, 40; docs/CHEATS.md).</summary>
	public const int RaisedCarSlots = 40;
	/// <summary>The tour tick in seconds: the original's tour tick runs on every second world step of 31 ms.</summary>
	// [BIN:TPW-EXE:0x0054F5EF tour tick] call 0x0055ABF0 behind `test al, 1` on the step counter at 0x0054F5D7: only even steps, so one tick per 62 ms (the draw divides by 62 ms, 0x00700F90)
	public const double TickSeconds = 0.062;
	/// <summary>The largest tick clock a save may carry. Saves from before the 62 ms tick (the 31 ms clock) hold less than 0.031 and restore unchanged: the clock is elapsed seconds, so the first advance runs it off.</summary>
	public const double MaximumSavedRemainder = TickSeconds;

	// the commands of the TOUR opcode (jump-table index + 1)
	public const int CommandCreate = 1;
	public const int CommandDestroy = 2;
	public const int CommandBoard = 3;
	public const int CommandUnboard = 4;
	public const int CommandCars = 5;
	public const int CommandDuration = 8;
	public const int CommandCondition = 9;
	public const int CommandReady = 10;
	public const int CommandDepart = 11;
	public const int CommandApproachMode = 12;
	public const int CommandOpen = 14;
	public const int CommandBrokenAtStation = 15;
	public const int CommandRunning = 16;
	public const int CommandAvoidance = 17;
	public const int CommandBank = 18;

	private readonly TourWorld world;
	private readonly Func<uint> worldRandom;
	private TourCar[] cars = new TourCar[CarSlots];
	private double tickRemainder;

	/// <param name="station">The launch pad.</param>
	/// <param name="worldRandom">The world generator the cars' random choices draw from (the original's 0x516330).</param>
	/// <param name="world">The module state this controller joins.</param>
	/// <param name="small">The creation command's operand (the script passes 0): non-zero halves the approach radius and the landing thrust.</param>
	public TourController( TourStation station, Func<uint> worldRandom, TourWorld world, int small = 0 )
	{
		this.worldRandom = worldRandom ?? throw new ArgumentNullException( nameof( worldRandom ) );
		this.world = world ?? throw new ArgumentNullException( nameof( world ) );
		for ( var index = 0; index < cars.Length; index++ )
			cars[index] = new TourCar();
		// [BIN:TPW-EXE:0x0055A620 controller creation] wanted = requested = 1, duration 300, wait 300, avoidance and bank scales 0x400, speed factor 5, open, nobody aboard; the heading word is 0x400 − the placement angle; the approach radius 2500 with the operand set, 5000 without
		StationX = station.X;
		StationY = station.Y;
		StationZ = station.Z;
		RotationDegrees = station.RotationDegrees;
		Heading = (0x400 - station.RotationDegrees * 4096 / 360) & 0xFFF;
		Small = small != 0;
		ApproachRadius = Small ? 0x9C4 : 5000;
		AvoidanceScale = 0x400;
		BankScale = 0x400;
		Wait = 300;
		Duration = 300;
		SpeedFactor = 5;
		Wanted = 1;
		Requested = 1;
		LandingReserved = true;
		Condition = 1;
		IsOpen = true;
		world.Add( this );
		SpawnCar();
	}

	/// <summary>Restores a controller from a park-resume save: no car is created, the saved words and cars are taken verbatim.</summary>
	internal TourController( TourControllerSaveData saved, Func<uint> worldRandom, TourWorld world )
	{
		this.worldRandom = worldRandom ?? throw new ArgumentNullException( nameof( worldRandom ) );
		this.world = world ?? throw new ArgumentNullException( nameof( world ) );
		// [EXT:cheats-ride-vehicles] a raised controller saved its slot count
		cars = new TourCar[saved.CarSlots ?? CarSlots];
		for ( var index = 0; index < cars.Length; index++ )
			cars[index] = new TourCar();
		StationX = saved.StationX;
		StationY = saved.StationY;
		StationZ = saved.StationZ;
		RotationDegrees = saved.RotationDegrees;
		Heading = (0x400 - saved.RotationDegrees * 4096 / 360) & 0xFFF;
		Small = saved.Small;
		ApproachRadius = Small ? 0x9C4 : 5000;
		Wait = 300;
		SpeedFactor = 5;
		AttractionId = saved.AttractionId;
		Wanted = saved.Wanted;
		Requested = saved.Requested;
		CarCount = saved.CarCount;
		Duration = saved.Duration;
		AvoidanceScale = saved.AvoidanceScale;
		BankScale = saved.BankScale;
		LandingReserved = saved.LandingReserved;
		Condition = saved.Condition;
		IsOpen = saved.IsOpen;
		Ready = saved.Ready;
		ApproachMode = saved.ApproachMode;
		Riders = saved.Riders;
		TickCount = saved.TickCount;
		tickRemainder = saved.TickRemainder;
		Boarded = saved.Boarded;
		Released = saved.Released;
		Departures = saved.Departures;
		Ejected.AddRange( saved.Ejected );
		foreach ( var data in saved.Cars )
		{
			var car = cars[data.Slot];
			car.State = data.State;
			car.X = data.X;
			car.Y = data.Y;
			car.Z = data.Z;
			car.Heading = data.Heading;
			car.Pitch = data.Pitch;
			car.Roll = data.Roll;
			car.PreviousX = data.PreviousX;
			car.PreviousY = data.PreviousY;
			car.PreviousZ = data.PreviousZ;
			car.PreviousHeading = data.PreviousHeading;
			car.PreviousPitch = data.PreviousPitch;
			car.PreviousRoll = data.PreviousRoll;
			car.TargetX = data.TargetX;
			car.TargetY = data.TargetY;
			car.TargetZ = data.TargetZ;
			car.PreviousTargetX = data.PreviousTargetX;
			car.PreviousTargetY = data.PreviousTargetY;
			car.PreviousTargetZ = data.PreviousTargetZ;
			car.VelocityX = data.VelocityX;
			car.VelocityZ = data.VelocityZ;
			car.Turn = data.Turn;
			car.VerticalVelocity = data.VerticalVelocity;
			car.Speed = data.Speed;
			car.Thrust = data.Thrust;
			car.Timer = data.Timer;
			car.Corner = data.Corner;
			car.RiderCount = data.RiderCount;
			for ( var rider = 0; rider < data.Riders.Count; rider++ )
				car.Riders[rider] = data.Riders[rider];
			car.Leaving = data.Leaving;
			car.Broken = data.Broken;
			car.ApproachHeading = data.ApproachHeading;
			car.Random = data.Random;
			car.Animation = data.Animation;
			car.AnimationLoops = data.AnimationLoops;
			car.Emitter = data.Emitter;
		}
		foreach ( var data in saved.FreeSlots ?? new List<TourFreeSlotSaveData>() )
		{
			var car = cars[data.Slot];
			car.Heading = data.Heading;
			car.Corner = data.Corner;
			car.ApproachHeading = data.ApproachHeading;
			for ( var rider = 0; rider < data.Riders.Count; rider++ )
				car.Riders[rider] = data.Riders[rider];
		}
		world.Add( this );
	}

	/// <summary>The attraction id of the ride this controller belongs to (set by the runtime; −1 for a controller of its own).</summary>
	public int AttractionId { get; set; } = -1;
	/// <summary>+0: the number of cars wanted now (the requested number while the ride is open, 1 while it is closed).</summary>
	public int Wanted { get; private set; }
	/// <summary>+4: the number of cars the script asked for (<c>TOUR 5</c>).</summary>
	public int Requested { get; private set; }
	/// <summary>+0xC: the cars that exist.</summary>
	public int CarCount { get; private set; }
	/// <summary>+0x10: the flight duration in ticks (<c>TOUR 8</c>: the operand × 1000 / 64).</summary>
	public int Duration { get; private set; }
	public int StationX { get; }
	public int StationY { get; }
	public int StationZ { get; }
	/// <summary>The placement rotation the controller was created with, in degrees.</summary>
	public int RotationDegrees { get; }
	/// <summary>The part of a world step not yet run.</summary>
	public double TickRemainder => tickRemainder;
	/// <summary>+0x20 (<c>TOUR 17</c>): the car-avoidance radius scale, 1024 is 1.0.</summary>
	public int AvoidanceScale { get; private set; }
	/// <summary>+0x24 (<c>TOUR 18</c>): the pitch and bank scale, 1024 is 1.0.</summary>
	public int BankScale { get; private set; }
	/// <summary>+0x28: the base thrust of a car (300).</summary>
	public int Wait { get; }
	/// <summary>+0x2C: the landing slot is taken: a car on its approach holds it, and a car that has taken off releases it, so the next car may start its approach.</summary>
	public bool LandingReserved { get; private set; }
	/// <summary>+0x30: the approach radius.</summary>
	public int ApproachRadius { get; }
	public bool Small { get; }
	/// <summary>+0x34 (<c>TOUR 9</c>): 1 fine, 2 worn out, 3 broken, 4 broken with riders thrown off.</summary>
	public int Condition { get; private set; }
	/// <summary>+0x38 (<c>TOUR 14</c>): the ride is open.</summary>
	public bool IsOpen { get; private set; }
	/// <summary>+0x3C: a car stands at the station ready to be loaded and sent (<c>TOUR 10</c>); <c>TOUR 11</c> clears it.</summary>
	public bool Ready { get; private set; }
	/// <summary>+0x40: the heading word of the station: 0x400 minus the rotation.</summary>
	public int Heading { get; }

	/// <summary>How many consecutively numbered head dummies the car mesh holds (the original's car +0xDC); set by the ride from its mesh, 0 where none is known (a bare controller).</summary>
	public int HeadNodes { get; init; }
	/// <summary>+0x44 (<c>TOUR 12</c>): cars take off and approach along the station's heading rather than their own.</summary>
	public int ApproachMode { get; private set; }
	/// <summary>+0x48: the riders aboard all cars.</summary>
	public int Riders { get; private set; }
	/// <summary>+0x50: the speed factor of the thrust (5).</summary>
	public int SpeedFactor { get; }
	/// <summary>The riders a severe breakdown (<c>TOUR 9 4</c>) threw off a flying car, handed out by <c>TOUR 4</c> once a car is docked.</summary>
	public List<int> Ejected { get; } = new();
	public IReadOnlyList<TourCar> Cars => cars;

	/// <summary>The car slots: <see cref="CarSlots"/>, or <see cref="RaisedCarSlots"/> once the cheat raised the ride's vehicle limit.</summary>
	public int SlotCount => cars.Length;

	/// <summary>
	/// [EXT:cheats-ride-vehicles] Gives the controller <see cref="RaisedCarSlots"/> car slots, the new ones free and fresh; false when it has them already. The cars in the old slots keep them, and
	/// the script's <c>TOUR 5</c> then asks for as many cars as its capacity gives (ceil(capacity / 9), / 8 for Star Tours).
	/// </summary>
	internal bool RaiseCarSlots()
	{
		if ( cars.Length >= RaisedCarSlots )
			return false;
		var old = cars.Length;
		Array.Resize( ref cars, RaisedCarSlots );
		for ( var index = old; index < cars.Length; index++ )
			cars[index] = new TourCar();
		return true;
	}

	/// <summary>The slot of <paramref name="car"/> in this controller, −1 when it is not one of its cars.</summary>
	public int SlotOf( TourCar car ) => Array.IndexOf( cars, car );
	/// <summary>Ticks run so far.</summary>
	public long TickCount { get; private set; }
	/// <summary>Riders handed to the cars and handed back (statistics).</summary>
	public int Boarded { get; private set; }
	/// <summary>Boarding commands refused: no car at the station, it was unloading or full (statistics).</summary>
	public int Refused { get; private set; }
	public int Released { get; private set; }
	/// <summary>Times a car left the station (statistics).</summary>
	public int Departures { get; private set; }

	/// <summary>The first car standing at the station (state 1), or null.</summary>
	public TourCar? DockedCar => cars.FirstOrDefault( car => car.State == TourCar.StateDocked );

	/// <summary>Whether a rider can board now: a car stands at the station, is not being unloaded and has a free seat.</summary>
	public bool CanBoard => DockedCar is { RiderCount: >= 0 and < TourCar.MaximumRiders };

	private int Random() => unchecked((int)worldRandom());

	// ---- The TOUR opcode --------------------------------------------------------------------------

	/// <summary>
	/// Runs one TOUR command and returns its result (the flag and output value the script sees). Commands 6, 7, 13 and above 18 are refused by the original
	/// with a log line and do nothing.
	/// </summary>
	// [BIN:TPW-EXE:0x005542FE TOUR] 1 create, 2 destroy, 3 board a rider, 4 hand out a rider, 5 cars, 8 duration, 9 condition, 10 ready, 11 depart, 12 approach mode, 14 open, 15 broken car at the station, 16 running, 17 avoidance scale, 18 bank scale
	public int Command( int command, int argument )
	{
		switch ( command )
		{
			case CommandBoard:
				return Board( argument ) ? 1 : 0;
			case CommandUnboard:
				return Unboard();
			case CommandCars:
				// [BIN:TPW-EXE:0x0055DCF0 TOUR 5] the requested count, which is the wanted count too while the ride is open
				Requested = argument;
				if ( IsOpen )
					Wanted = argument;
				return 0;
			case CommandDuration:
				SetDuration( argument );
				return 0;
			case CommandCondition:
				SetCondition( argument );
				return 0;
			case CommandReady:
				return Ready ? 1 : 0;
			case CommandDepart:
				return Depart() ? 1 : 0;
			case CommandApproachMode:
				ApproachMode = argument;
				return 0;
			case CommandOpen:
				SetOpen( argument != 0 );
				return 0;
			case CommandBrokenAtStation:
				// [BIN:TPW-EXE:0x0055DD40 TOUR 15] true while the first car is broken and standing at the station
				return cars[0].Broken && cars[0].State == TourCar.StateDocked ? 1 : 0;
			case CommandRunning:
				// [BIN:TPW-EXE:0x0055DE30 TOUR 16] running while anybody is aboard, no car waits at the station, or more than one car exists
				return Riders != 0 || !Ready || CarCount != 1 ? 1 : 0;
			case CommandAvoidance:
				AvoidanceScale = argument;
				return 0;
			case CommandBank:
				BankScale = argument;
				return 0;
			default:
				return 0;
		}
	}

	/// <summary>Boards a rider into the car standing at the station; false when none stands there, it is unloading or it is full.</summary>
	// [BIN:TPW-EXE:0x0055D830 TOUR 3] the first car in state 1 whose count is not negative takes the rider into the next slot; the ride's rider total rises. The original has no seat limit (the scripts stop at INCAR); a ninth rider would write over the leaving flag, so a full car refuses
	public bool Board( int guest )
	{
		var car = DockedCar;
		if ( car == null || car.RiderCount < 0 || car.RiderCount >= TourCar.MaximumRiders )
		{
			Refused++;
			return false;
		}
		car.Riders[car.RiderCount] = guest;
		car.RiderCount++;
		Riders++;
		Boarded++;
		// [BIN:TPW-EXE:0x0055D91E tour head hang] a car mesh with head nodes (+0xDC, +0x138 not zero) looks up the rider's node (always found: the nodes are numbered from 1) and hangs the head through 0x44B410
		if ( HeadNodes > 0 )
			world.Scripts?.HeadHung( guest );
		return true;
	}

	/// <summary>Hands out the next rider of the car standing at the station that is being unloaded, newest seat first; 0 when none is.</summary>
	// [BIN:TPW-EXE:0x0055D610 TOUR 4] the first car in state 1; with a negative count the count rises by one, the ride's total falls and the rider at slot −count is returned; else 0
	public int Unboard()
	{
		var car = DockedCar;
		if ( car != null && car.RiderCount < 0 )
		{
			car.RiderCount++;
			Riders--;
			Released++;
			// the seat keeps the id: the original reads it without clearing the slot (0x0055D610), and a stale seat word is saved and hashed like any other (TOUR-009)
			return car.Riders[-car.RiderCount];
		}
		if ( car != null && Ejected.Count > 0 )
		{
			// [EXT:tour-thrown-riders-released] riders a severe breakdown threw off are handed out at the station like unloaded riders (which also lowers VAR_ONRIDE): a deliberate fix of the original's bug, where the loop in 0x0055DA70 discards the id 0x0055D720 returns, so the script never releases them, VAR_ONRIDE keeps counting them and the guest is never released
			var guest = Ejected[^1];
			Ejected.RemoveAt( Ejected.Count - 1 );
			Released++;
			return guest;
		}
		return 0;
	}

	/// <summary>Sends the car at the station away: the ready flag clears, and the car takes off on the next tick. False when the ride has nothing to fly.</summary>
	// [BIN:TPW-EXE:0x0055DD80 TOUR 11] allowed when the ride is open, anybody is aboard or more than one car exists, and a car is ready: the ready flag clears and the result is 1
	public bool Depart()
	{
		if ( (IsOpen || Riders != 0 || CarCount > 1) && Ready )
		{
			Ready = false;
			Departures++;
			return true;
		}
		return false;
	}

	private void SetDuration( int seconds )
	{
		// [BIN:TPW-EXE:0x0055D950 TOUR 8] the operand × 1000 divided by 64 (towards zero) is the flight time in ticks; flights already running are cut to it
		var scaled = unchecked(seconds * 1000);
		Duration = unchecked(scaled + ((scaled >> 31) & 0x3F)) >> 6;
		foreach ( var car in cars )
		{
			if ( Duration < car.Timer )
				car.Timer = Duration;
		}
	}

	private void SetOpen( bool open )
	{
		if ( open )
		{
			// [BIN:TPW-EXE:0x0055D9F0 TOUR 14] open: the wanted count is the requested one again
			IsOpen = true;
			Wanted = Requested;
			return;
		}
		// closed (a broken ride ignores it): one car wanted, every flight ends now, a car at the station with riders starts unloading them
		// [BIN:TPW-EXE:0x0055D9F0 TOUR 14] the loop touches the timer, the count and the open word only: the ready flag is left as it is. A departure the script ordered (TOUR 11 cleared ready) that the closing stops before the lift-off leaves the sole car docked with its riders and ready clear (0x0055ABF0 state 1 needs ready clear and, for the first car of a ride with one car, an open ride), so TOUR 10 reads 0 until the ride reopens; it then flies once more with its negative count, lands (ready set) and the script unloads it
		if ( Condition == 3 )
			return;
		Wanted = 1;
		foreach ( var car in cars )
		{
			if ( car.State != TourCar.StateFree )
				car.Timer = 0;
			if ( car.State == TourCar.StateDocked && car.RiderCount > 0 )
				car.RiderCount = -car.RiderCount;
		}
		IsOpen = false;
	}

	private void SetCondition( int condition )
	{
		// [BIN:TPW-EXE:0x0055DA70 TOUR 9] a change of the condition word: 1 mends (the first car's breakdown clears, its thrust is drawn again), 2 worn (smoke), 3 broken (the first car breaks and its flight ends), 4 as 3 and every flying rider is thrown off
		if ( condition == Condition )
			return;
		Condition = condition;
		var first = cars[0];
		switch ( condition )
		{
			case 1:
				Wanted = Requested;
				if ( first.Broken )
				{
					// [BIN:TPW-EXE:0x0055DACB TOUR 9 1] the repair: animation 10 (channel 0, once), then the global cat_rides sound 0xBB at the car (x / 300, height / 300, z / 300; 0x51BFC0)
					SetAnimation( first, 10, false );
					Raise( TourEffectKind.RepairSound, first, TourEffects.SoundRepair );
					first.Broken = false;
				}
				if ( first.Thrust != 0 )
					first.Thrust = TourMath.Abs( Random() ) % 100 + Wait;
				// [BIN:TPW-EXE:0x0055DB5C TOUR 9 1] the first car's emitter is destroyed (0x51FEB0)
				StopEmitter( first );
				break;
			case 2:
				Wanted = Requested;
				// [BIN:TPW-EXE:0x0055DB7D TOUR 9 2] worn: the first car's emitter is replaced by particle 0x27 (FlySmoke) created at the car's raw module coordinates taken as particle units (0x521E60(0x27, x, height, z), not scaled like 3 and 4); the next draw moves it to the car's node
				StartEmitter( first, TourEffects.ParticleFlySmoke, first.X, first.Y, first.Z );
				break;
			case 3:
				first.Broken = true;
				first.Timer = 0;
				// [BIN:TPW-EXE:0x0055DBB5 TOUR 9 3] broken: the emitter is replaced by particle 0x28 (FlyFire) at (x << 10) / 300 etc., the car's place in particle units
				StartEmitter( first, TourEffects.ParticleFlyFire, unchecked(first.X << 10) / 300, unchecked(first.Y << 10) / 300, unchecked(first.Z << 10) / 300 );
				break;
			case 4:
				first.Broken = true;
				first.Timer = 0;
				// [BIN:TPW-EXE:0x0055DC34 TOUR 9 4] as 3, then the flying riders are thrown off
				StartEmitter( first, TourEffects.ParticleFlyFire, unchecked(first.X << 10) / 300, unchecked(first.Y << 10) / 300, unchecked(first.Z << 10) / 300 );
				EjectFlyingRiders();
				Wanted = 1;
				break;
		}
	}

	/// <summary>Throws the riders of the flying cars off (the original's 0x0055D720 loop: each popped id is dropped).</summary>
	private void EjectFlyingRiders()
	{
		foreach ( var car in cars )
		{
			while ( car.RiderCount > 0 )
			{
				var guest = car.Riders[car.RiderCount - 1];
				car.RiderCount--;
				Riders--;
				Ejected.Add( guest );
			}
		}
	}

	// [BIN:TPW-EXE:0x0055AA80 car animation] (animation, channel, loops) is compared with the car's last trigger (+0xCC/+0xD0/+0xD4, all −1 after creation) and only a change triggers the mesh clip through 0x004732A0(instance, animation, channel, loops, 1.0, 0) and logs "FLY: Failed to trigger a flying car anim" on failure; every call site passes channel 0
	private void SetAnimation( TourCar car, int animation, bool loops )
	{
		if ( car.Animation == animation && car.AnimationLoops == loops )
			return;
		car.Animation = animation;
		car.AnimationLoops = loops;
		car.AnimationSerial++;
		Raise( TourEffectKind.Animation, car, animation, loops ? 1 : 0 );
	}

	/// <summary>Raised for every cosmetic effect of a car (engine voice, smoke, repair sound, clip triggers). Presentation only: handlers must not change the controller.</summary>
	public event Action<TourEffect>? Effect;

	private void Raise( TourEffectKind kind, TourCar car, int id = 0, int value = 0 ) =>
		Effect?.Invoke( new TourEffect( kind, SlotOf( car ), id, value, car.X, car.Y, car.Z ) );

	/// <summary>The engine voice's parameter after a state's animation call: code = EventMap variable 10 (the host resolves it), <paramref name="value"/> as traced.</summary>
	// [BIN:TPW-EXE:0x0055ACCE engine parameter] after each animation call the state code reads EventMap variable 10 (0x55A3E0) and, while the car holds a voice (+0xE0), sets it through 0x51BC40: 80 docked (0x55B09F) and landed (0x55BBF5), 15 taking off (0x55B65B), 60 broken or 30 for the cruising states and the descent (0x55ACE4/0x55AD11 and the matching pairs)
	private void EngineParameter( TourCar car, int value ) => Raise( TourEffectKind.EngineParameter, car, 0, value );

	private void CruiseParameter( TourCar car ) => EngineParameter( car, car.Broken ? TourEffects.ParameterBroken : TourEffects.ParameterFlying );

	/// <summary>
	/// Moves a car's emitter to its drawn smoke node: the renderer calls it every draw with the node's engine place (presentation only).
	/// </summary>
	// [BIN:TPW-EXE:0x0055CE60 car draw] for a live car holding an emitter (+0xC4) the draw finds the node class 0x100 number 1 (0x44B220) and moves the emitter to its world place × 1024 (0x51FE30)
	internal void DrawEmitter( int slot, System.Numerics.Vector3 node )
	{
		if ( slot < 0 || slot >= cars.Length || cars[slot] is not { IsLive: true, Emitter: not 0 } car )
			return;
		Effect?.Invoke( new TourEffect( TourEffectKind.EmitterMove, slot, car.Emitter, 0, car.X, car.Y, car.Z, node ) );
	}

	private void StartEmitter( TourCar car, int particle, int x, int y, int z )
	{
		StopEmitter( car );
		car.Emitter = particle;
		Effect?.Invoke( new TourEffect( TourEffectKind.EmitterStart, SlotOf( car ), particle, 0, x, y, z ) );
	}

	private void StopEmitter( TourCar car )
	{
		if ( car.Emitter == 0 )
			return;
		car.Emitter = 0;
		Raise( TourEffectKind.EmitterStop, car );
	}

	// ---- The tick -----------------------------------------------------------------------------------

	/// <summary>Runs the world steps that fit into <paramref name="deltaSeconds"/>.</summary>
	public void Advance( double deltaSeconds )
	{
		tickRemainder += deltaSeconds;
		while ( tickRemainder >= TickSeconds )
		{
			tickRemainder -= TickSeconds;
			Tick();
		}
	}

	/// <summary>The time since the last tour step, for the renderer's interpolation (<see cref="TickSeconds"/> is the most); the substep scheduler (<see cref="SimulationSubstep"/>) runs the steps themselves.</summary>
	internal void PresentAt( double secondsSinceStep ) => tickRemainder = Math.Clamp( secondsSinceStep, 0, TickSeconds - 1e-6 );

	/// <summary>One world step: a car is created when fewer exist than wanted, then every live car runs its state.</summary>
	public void Tick()
	{
		TickCount++;
		if ( CarCount < Wanted )
			SpawnCar();
		for ( var index = 0; index < cars.Length; index++ )
		{
			var car = cars[index];
			if ( car.State == TourCar.StateFree )
				continue;
			car.PreviousX = car.X;
			car.PreviousY = car.Y;
			car.PreviousZ = car.Z;
			car.PreviousHeading = car.Heading;
			car.PreviousPitch = car.Pitch;
			car.PreviousRoll = car.Roll;
			if ( index > 0 && Wanted < CarCount && 1 < CarCount )
				car.Leaving = true;
			RunState( index, car );
			Move( index, car );
			// [BIN:TPW-EXE:0x0055C1B5 engine move] at the end of every car's tick a held voice (+0xE0) moves to (x / 300, height / 300, z / 300) (0x51C270); a removed car's voice is gone
			if ( car.IsLive )
				Raise( TourEffectKind.EngineMove, car );
		}
	}

	/// <summary>Creates a car in the first free slot and, for the ride's first car, parks it at the station.</summary>
	// [BIN:TPW-EXE:0x0055A720 car creation] state 4, target height 14000, thrust = |draw| % 100 + 300; the car appears outside the flight area (an edge chosen by two draws, 45000 beyond the bound) at height 14000; the first car of a ride stands at the station facing its heading with the ready flag set
	private void SpawnCar()
	{
		var slot = Array.FindIndex( cars, car => car.State == TourCar.StateFree );
		if ( slot < 0 )
			return;
		var car = cars[slot];
		car.State = TourCar.StateReturn;
		// [BIN:TPW-EXE:0x0055A720 car creation] the slot's stale words stay: the heading (+0x10, only the ride's first car takes the station's), the patrol corner (+0x88), the seat ids (+0x90..) and the approach heading (+0xBC) are not written here, so a new car in a reused slot starts from the last car's values. They are saved, hashed and restored for free slots too
		car.Pitch = car.Roll = 0;
		car.VerticalVelocity = 0;
		car.Turn = 0;
		car.Speed = 0;
		car.VelocityX = car.VelocityZ = 0;
		car.RiderCount = 0;
		car.Broken = false;
		car.Leaving = false;
		// [BIN:TPW-EXE:0x0055A8D9 car creation] the last-trigger triple is set to −1, so the creation's animation 0 always triggers
		car.Animation = -1;
		car.AnimationLoops = false;
		car.TargetY = 14000;
		car.Thrust = TourMath.Abs( Random() ) % 100 + Wait;
		if ( (TourMath.Abs( Random() ) & 1) == 0 )
		{
			car.TargetZ = unchecked(TourMath.Abs( Random() ) * 100) % (TourWorld.MaximumZ - TourWorld.MinimumZ) + TourWorld.MinimumZ;
			car.TargetX = (TourMath.Abs( Random() ) & 1) == 0 ? TourWorld.MinimumX - 45000 : TourWorld.MaximumX + 45000;
		}
		else
		{
			car.TargetX = unchecked(TourMath.Abs( Random() ) * 100) % (TourWorld.MaximumX - TourWorld.MinimumX) + TourWorld.MinimumX;
			car.TargetZ = (TourMath.Abs( Random() ) & 1) == 0 ? TourWorld.MinimumZ - 45000 : TourWorld.MaximumZ + 45000;
		}
		car.X = car.TargetX;
		car.Y = car.TargetY;
		car.Z = car.TargetZ;
		car.Timer = 0;
		car.PreviousTargetX = car.TargetX - 20000;
		car.PreviousTargetY = car.TargetY;
		car.PreviousTargetZ = car.TargetZ;
		CarCount++;
		// [BIN:TPW-EXE:0x0055A944 engine start] the car's engine voice: EventMap variable 0 submitted to the level cat_rides at (x / 300, height / 300, z / 300) (0x51EEB0), its handle kept in +0xE0
		Raise( TourEffectKind.EngineStart, car );
		car.Random = TourMath.Abs( Random() );
		car.Generation++;
		if ( CarCount == 1 )
		{
			car.State = TourCar.StateDocked;
			car.X = StationX;
			car.Y = StationY;
			car.Z = StationZ;
			car.Thrust = 0;
			car.Heading = Heading;
			Ready = true;
		}
		SetAnimation( car, 0, false );
		car.PreviousX = car.X;
		car.PreviousY = car.Y;
		car.PreviousZ = car.Z;
		car.PreviousHeading = car.Heading;
		car.PreviousPitch = car.Pitch;
		car.PreviousRoll = car.Roll;
	}

	private void RunState( int index, TourCar car )
	{
		switch ( car.State )
		{
			case TourCar.StateDocked:
				car.Pitch = TourMath.Div2( car.Pitch );
				EngineParameter( car, TourEffects.ParameterDocked );
				if ( !Ready && !car.Broken && (IsOpen || index != 0 || CarCount != 1) )
					TakeOff( car );
				break;
			case TourCar.StateTakeoff:
				SetAnimation( car, 4, true );
				EngineParameter( car, TourEffects.ParameterTakeoff );
				if ( car.TargetY <= car.Y )
				{
					LandingReserved = false;
					if ( !car.Leaving )
					{
						car.State = TourCar.StateTour;
						car.PreviousTargetX = TourMath.Lattice( car.TargetX ) - 20000;
						car.PreviousTargetY = car.TargetY;
						car.PreviousTargetZ = TourMath.Lattice( car.TargetZ );
					}
					else
						car.State = TourCar.StateReturn;
				}
				break;
			case TourCar.StateTour:
				RunTour( car );
				break;
			case TourCar.StateReturn:
				RunReturn( car );
				break;
			case TourCar.StateStaging:
				RunCruiseAnimation( car );
				if ( HorizontalDistance( car ) < 5000 )
				{
					car.State = TourCar.StateApproach;
					car.TargetX = StationX - TourMath.Div1024( TourWorld.Cos( car.ApproachHeading ) * ApproachRadius );
					car.TargetY = StationY + 5000;
					car.TargetZ = StationZ - TourMath.Div1024( TourWorld.Sin( car.ApproachHeading ) * ApproachRadius );
				}
				if ( car.Leaving && car.RiderCount == 0 )
				{
					car.State = TourCar.StateReturn;
					car.TargetY = 14000;
					LandingReserved = false;
				}
				break;
			case TourCar.StateApproach:
				RunCruiseAnimation( car );
				if ( HorizontalDistance( car ) < 5000 )
				{
					car.State = TourCar.StateLanding;
					car.Thrust = Small ? Wait / 2 : Wait;
					car.TargetX = StationX;
					car.TargetY = StationY;
					car.TargetZ = StationZ;
				}
				break;
			case TourCar.StateLanding:
				RunLanding( car );
				break;
			case TourCar.StatePatrol:
				RunPatrol( car );
				break;
			case TourCar.StateLeaving:
				RunCruiseAnimation( car );
				if ( HorizontalDistance( car ) < 5000 )
					RemoveCar( car );
				break;
		}
	}

	private int HorizontalDistance( TourCar car ) => TourMath.Distance32( car.X - car.TargetX, car.Z - car.TargetZ ) * 32;

	private void RunCruiseAnimation( TourCar car )
	{
		if ( !car.Broken )
			SetAnimation( car, 5, true );
		else
			SetAnimation( car, 9, true );
		CruiseParameter( car );
	}

	/// <summary>The car at the station takes off: thrust, flight timer and the first target 20000 units ahead along the heading, at a height clearing the ground.</summary>
	// [BIN:TPW-EXE:0x0055ABF0 state 1] a ready flag of 0 and no breakdown send the car: state 2, thrust |draw| % 100 + 300, timer = the duration, target = the station + 20000 along the car's heading (the station's when TOUR 12 is set), height = max(14000, max(station height, ground at the target) + 5000)
	private void TakeOff( TourCar car )
	{
		car.State = TourCar.StateTakeoff;
		car.Thrust = TourMath.Abs( Random() ) % 100 + Wait;
		car.Timer = Duration;
		var heading = ApproachMode == 0 ? car.Heading : Heading;
		car.TargetX = TourMath.Div1024( TourWorld.Cos( heading ) * 20000 ) + StationX;
		car.TargetZ = TourMath.Div1024( TourWorld.Sin( heading ) * 20000 ) + StationZ;
		var ground = world.GroundClamped( car.TargetX, car.TargetZ );
		car.TargetY = Math.Max( 14000, Math.Max( StationY, ground ) + 5000 );
	}

	private void RunTour( TourCar car )
	{
		RunCruiseAnimation( car );
		if ( car.Broken )
		{
			if ( Condition == 3 )
				car.Timer = 0;
			car.Thrust = Wait / 2;
		}
		if ( car.Timer < 1 || car.Leaving )
		{
			car.State = TourCar.StateReturn;
			NextTarget( car );
			car.TargetY = 14000;
		}
		else if ( HorizontalDistance( car ) < 5000 )
			NextTarget( car );
	}

	private void RunReturn( TourCar car )
	{
		RunCruiseAnimation( car );
		if ( HorizontalDistance( car ) >= 5000 )
			return;
		if ( !car.Leaving || car.RiderCount != 0 )
		{
			car.State = TourCar.StatePatrol;
			PatrolCorner( car );
			return;
		}
		// an extra car nobody rides: off the edge of the flight area
		car.State = TourCar.StateLeaving;
		car.TargetY = 14000;
		if ( (TourMath.Abs( Random() ) & 1) == 0 )
		{
			car.TargetZ = unchecked(TourMath.Abs( Random() ) * 100) % (TourWorld.MaximumZ - TourWorld.MinimumZ) + TourWorld.MinimumZ;
			car.TargetX = (TourMath.Abs( Random() ) & 1) == 0 ? TourWorld.MinimumX - 45000 : TourWorld.MaximumX + 45000;
		}
		else
		{
			car.TargetX = unchecked(TourMath.Abs( Random() ) * 100) % (TourWorld.MaximumX - TourWorld.MinimumX) + TourWorld.MinimumX;
			car.TargetZ = (TourMath.Abs( Random() ) & 1) == 0 ? TourWorld.MinimumZ - 45000 : TourWorld.MaximumZ + 45000;
		}
		car.Thrust = Wait << 1;
	}

	private void RunLanding( TourCar car )
	{
		if ( StationY < car.Y )
		{
			SetAnimation( car, 6, false );
			CruiseParameter( car );
			return;
		}
		// down: the car stands at the station, ready, and the riders it carries start leaving
		car.Y = StationY;
		car.State = TourCar.StateDocked;
		Ready = true;
		car.Pitch = 0;
		car.Thrust = 0;
		car.VerticalVelocity = 0;
		if ( car.RiderCount > 0 )
			car.RiderCount = -car.RiderCount;
		SetAnimation( car, 3, false );
		EngineParameter( car, TourEffects.ParameterDocked );
	}

	private void RunPatrol( TourCar car )
	{
		RunCruiseAnimation( car );
		if ( car.RiderCount == 0 && car.Leaving )
		{
			car.State = TourCar.StateReturn;
			PatrolCorner( car );
			car.TargetY = 14000;
		}
		if ( HorizontalDistance( car ) < 5000 )
			PatrolCorner( car );
		if ( LandingReserved )
			return;
		LandingReserved = true;
		if ( ApproachMode == 0 )
		{
			// aim at the station along the line from the car
			// [EXT:deterministic-math] the x87's fpatan in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
			car.ApproachHeading = (int)(DetMath.Atan2( StationZ - car.Z, StationX - car.X ) * 652.0);
			car.State = TourCar.StateApproach;
			car.TargetX = StationX - TourMath.Div1024( TourWorld.Cos( car.ApproachHeading ) * ApproachRadius );
			car.TargetY = Math.Max( StationY + 5000, 14000 );
			car.TargetZ = StationZ - TourMath.Div1024( TourWorld.Sin( car.ApproachHeading ) * ApproachRadius );
		}
		else
		{
			// along the station's own heading, from six radii out
			car.ApproachHeading = Heading;
			car.State = TourCar.StateStaging;
			car.TargetX = StationX - TourMath.Div1024( TourWorld.Cos( car.ApproachHeading ) * ApproachRadius * 6 );
			car.TargetY = Math.Max( StationY + 5000, 14000 );
			car.TargetZ = StationZ - TourMath.Div1024( TourWorld.Sin( car.ApproachHeading ) * ApproachRadius * 6 );
		}
	}

	// [BIN:TPW-EXE:0x0055CDA0 remove car] the car's emitter (+0xC4) is destroyed (0x51FEB0), its mesh instance freed and its engine voice stopped (0x51C2C0)
	private void RemoveCar( TourCar car )
	{
		car.State = TourCar.StateFree;
		CarCount--;
		StopEmitter( car );
		Raise( TourEffectKind.EngineStop, car );
	}

	/// <summary>The next of the four patrol corners round the station, 25000 units out on each diagonal, at least 14000 up.</summary>
	// [BIN:TPW-EXE:0x0055C270 patrol corner] the previous target is kept, the height is max(14000, station height + 5000), the corner index rises (mod 4): (+,+), (+,−), (−,−), (−,+) on the station's two ground axes
	private void PatrolCorner( TourCar car )
	{
		car.PreviousTargetX = car.TargetX;
		car.PreviousTargetY = car.TargetY;
		car.PreviousTargetZ = car.TargetZ;
		car.TargetY = Math.Max( StationY + 5000, 14000 );
		car.Corner = (car.Corner + 1) & 3;
		switch ( car.Corner )
		{
			case 0:
				car.TargetX = StationX + 25000;
				car.TargetZ = StationZ + 25000;
				break;
			case 1:
				car.TargetX = StationX + 25000;
				car.TargetZ = StationZ - 25000;
				break;
			case 2:
				car.TargetX = StationX - 25000;
				car.TargetZ = StationZ - 25000;
				break;
			default:
				car.TargetX = StationX - 25000;
				car.TargetZ = StationZ + 25000;
				break;
		}
	}

	/// <summary>The next target of the tour: one 20000-unit lattice step on, turning aside where the ground ahead is too high.</summary>
	// [BIN:TPW-EXE:0x0055C350 next target] the step continues the direction from the previous target to the target (along the longer axis); three draws in four it goes straight on when the ground ahead is below the car's height + 5000, else it tries the two sides, straight on and back, raising the height bar by 5000 per round; the new target snaps to the lattice centre (10000 into the cell); its height is max(12000, higher ground of the car and the target + 5000)
	private void NextTarget( TourCar car )
	{
		var dx = car.TargetX - car.PreviousTargetX;
		var dz = car.TargetZ - car.PreviousTargetZ;
		int stepX, stepZ;
		if ( Math.Abs( dz ) < Math.Abs( dx ) )
		{
			stepX = dx == 0 ? 0 : TourMath.Sign( dx ) * 20000;
			stepZ = 0;
		}
		else
		{
			stepX = 0;
			stepZ = dz == 0 ? 0 : TourMath.Sign( dz ) * 20000;
		}
		car.PreviousTargetX = car.TargetX;
		car.PreviousTargetY = car.TargetY;
		car.PreviousTargetZ = car.TargetZ;
		var bar = car.Y + 5000;
		int newX, newZ;
		var draw = TourMath.Abs( Random() );
		if ( (draw & 3) == 0 || world.Clearance( stepX + car.TargetX, stepZ + car.TargetZ ) >= bar )
		{
			while ( true )
			{
				if ( world.Clearance( stepZ + car.TargetX, stepX + car.TargetZ ) < bar )
				{
					newX = car.TargetX + stepZ;
					newZ = car.TargetZ + stepX;
					break;
				}
				if ( world.Clearance( car.TargetX - stepZ, car.TargetZ - stepX ) < bar )
				{
					newX = car.TargetX - stepZ;
					newZ = car.TargetZ - stepX;
					break;
				}
				if ( world.Clearance( stepX + car.TargetX, stepZ + car.TargetZ ) < bar )
				{
					newX = car.TargetX + stepX;
					newZ = car.TargetZ + stepZ;
					break;
				}
				if ( world.Clearance( car.TargetX - stepX, car.TargetZ - stepZ ) < bar )
				{
					newX = car.TargetX - stepX;
					newZ = car.TargetZ - stepZ;
					break;
				}
				bar += 5000;
			}
			newX = TourMath.Lattice( newX ) + 10000;
			newZ = TourMath.Lattice( newZ ) + 10000;
		}
		else
		{
			newX = TourMath.Lattice( car.TargetX ) + 10000 + stepX;
			newZ = TourMath.Lattice( car.TargetZ ) + 10000 + stepZ;
		}
		car.TargetX = newX;
		car.TargetZ = newZ;
		var here = world.GroundClamped( car.X, car.Z );
		var there = world.GroundClamped( newX, newZ );
		var height = Math.Max( here, there ) + 5000;
		car.TargetY = height < 0x2EE1 ? 12000 : height;
	}

	// ---- Motion ---------------------------------------------------------------------------------------

	/// <summary>The motion every car runs after its state: speed and heading dynamics, the avoidance between cars, the steering, the climb and the roll.</summary>
	// [BIN:TPW-EXE:0x0055ABF0 motion] speed decays by 225/1024 (a docked car 125/1024 per tick) and gains (factor × thrust / 10 + thrust) / 10; the position advances by speed × (cos, sin) of the heading; the heading turns by a tenth of a smoothed error to the target bearing; the height follows the target through a damped vertical velocity; the roll follows the turn
	private void Move( int index, TourCar car )
	{
		var speed = car.Speed;
		var damped = car.State == TourCar.StateDocked ? speed * 100 : speed * 200;
		car.Speed = TourMath.Div1024( (speed * 0x19 + damped) * 4 );
		car.Turn = TourMath.Div32( car.Turn * 0x1F );
		car.Timer--;
		car.Speed += ((SpeedFactor * car.Thrust) / 10 + car.Thrust) / 10;
		car.VelocityX = TourMath.Div1024( TourWorld.Cos( car.Heading ) * car.Speed );
		car.VelocityZ = TourMath.Div1024( TourWorld.Sin( car.Heading ) * car.Speed );
		car.X += car.VelocityX;
		car.Z += car.VelocityZ;
		if ( car.State == TourCar.StateDocked )
		{
			if ( car.Pitch > 0 )
				car.Pitch -= 0x32;
		}
		else
		{
			Avoid( index, car );
			Steer( car );
			Climb( car );
			Bank( car );
		}
	}

	private void Avoid( int index, TourCar car )
	{
		for ( var other = index; other < cars.Length; other++ )
		{
			var partner = cars[other];
			if ( other == index || partner.State == TourCar.StateFree || partner.State == TourCar.StateDocked )
				continue;
			var dx = car.X - partner.X;
			var dy = car.Y - partner.Y;
			var dz = car.Z - partner.Z;
			// [BIN:TPW-EXE:0x0055ABF0 avoidance] within 15000 × the scale on the ground and 3200 × the scale in height the slower car loses a sixteenth of its speed and the lower one climbs; within 3000 × the scale in all three axes the cars are pushed 500 apart in height and roll away from each other
			if ( TourMath.Distance32( dx, dz ) * 32 < TourMath.Div1024( AvoidanceScale * 15000 ) && Math.Abs( partner.Y - car.Y ) < TourMath.Div1024( AvoidanceScale * 3200 ) )
			{
				if ( car.State == TourCar.StateLanding )
					partner.VerticalVelocity += 0x28;
				else if ( partner.State == TourCar.StateLanding )
					car.VerticalVelocity += 0x28;
				else
				{
					if ( car.Speed > partner.Speed )
						partner.Speed -= TourMath.Div16( partner.Speed );
					else
						car.Speed -= TourMath.Div16( car.Speed );
					if ( car.Y > partner.Y )
						car.VerticalVelocity += 0x28;
					else if ( index < other )
						partner.VerticalVelocity += 0x28;
				}
			}
			if ( TourMath.Distance32( dx, dy, dz ) * 32 < TourMath.Div1024( AvoidanceScale * 3000 ) )
			{
				if ( partner.Y < car.Y )
					car.Y += 500;
				else
					partner.Y += 500;
				car.Roll += 1000;
				partner.Roll -= 1000;
			}
		}
	}

	private void Steer( TourCar car )
	{
		// the bearing to the target in 4096ths of a turn (the original multiplies the arc tangent by −652, a hair under 4096 / 2π, and subtracts)
		// [EXT:deterministic-math] the x87's fpatan in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
		var bearing = (int)(DetMath.Atan2( car.TargetZ - car.Z, car.TargetX - car.X ) * -652.0);
		var error = (-car.Heading - bearing) & 0xFFF;
		if ( error > 0x7FF )
			error -= 0xFFF;
		car.Turn = TourMath.Div2( error + car.Turn );
		car.Heading = (car.Turn / 10 + car.Heading) & 0xFFF;
	}

	private void Climb( TourCar car )
	{
		if ( car.State == TourCar.StateLanding )
		{
			if ( car.Pitch < 0x3C )
				car.Pitch += 2;
			var distance = TourMath.Distance32( car.X - car.TargetX, car.Z - car.TargetZ );
			var along = Small ? distance << 6 : distance << 5;
			car.Y = TourMath.Div8( along + StationY - 3000 + car.Y * 7 );
			return;
		}
		var difference = car.TargetY - car.Y;
		var step = Math.Clamp( difference / 20, -0x10, 0x10 );
		var gain = Math.Abs( difference ) < 0x2711 ? 1 : 3;
		car.VerticalVelocity = Math.Clamp( car.VerticalVelocity + gain * step, -0x200, 0x200 );
		car.VerticalVelocity = TourMath.Div8( car.VerticalVelocity * 7 );
		car.Y += car.VerticalVelocity * 3;
		car.Pitch = TourMath.Div1024( BankScale * car.VerticalVelocity );
	}

	private void Bank( TourCar car )
	{
		var turn = car.Turn;
		var banked = (-TourMath.Div2( turn ) - TourMath.Div4( turn )) * BankScale;
		var target = TourMath.Div1024( banked ) & 0xFFF;
		var difference = target - (car.Roll & 0xFFF);
		if ( difference > 0x800 )
			target -= 0x1000;
		else if ( difference < -0x800 )
			target += 0x1000;
		car.Roll = TourMath.Div8( (car.Roll & 0xFFF) * 7 + target ) & 0xFFF;
	}

	// ---- State ------------------------------------------------------------------------------------------

	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( AttractionId );
		hash.Add( TickCount );
		hash.Add( tickRemainder );
		hash.Add( Wanted );
		hash.Add( Requested );
		hash.Add( CarCount );
		hash.Add( Duration );
		hash.Add( StationX );
		hash.Add( StationY );
		hash.Add( StationZ );
		hash.Add( Heading );
		hash.Add( AvoidanceScale );
		hash.Add( BankScale );
		hash.Add( LandingReserved );
		hash.Add( Condition );
		hash.Add( IsOpen );
		hash.Add( Ready );
		hash.Add( ApproachMode );
		hash.Add( Riders );
		hash.Add( Ejected.Count );
		foreach ( var guest in Ejected )
			hash.Add( guest );
		foreach ( var car in cars )
		{
			hash.Add( car.State );
			if ( car.State == TourCar.StateFree )
			{
				// the stale words a new car in this slot inherits (TOUR-009)
				hash.Add( car.Heading );
				hash.Add( car.Corner );
				hash.Add( car.ApproachHeading );
				for ( var rider = 0; rider < TourCar.MaximumRiders; rider++ )
					hash.Add( car.Riders[rider] );
				continue;
			}
			hash.Add( car.X );
			hash.Add( car.Y );
			hash.Add( car.Z );
			hash.Add( car.Heading );
			hash.Add( car.Pitch );
			hash.Add( car.Roll );
			hash.Add( car.TargetX );
			hash.Add( car.TargetY );
			hash.Add( car.TargetZ );
			hash.Add( car.PreviousTargetX );
			hash.Add( car.PreviousTargetY );
			hash.Add( car.PreviousTargetZ );
			hash.Add( car.Turn );
			hash.Add( car.VerticalVelocity );
			hash.Add( car.Speed );
			hash.Add( car.Thrust );
			hash.Add( car.Timer );
			hash.Add( car.Corner );
			hash.Add( car.RiderCount );
			for ( var rider = 0; rider < TourCar.MaximumRiders; rider++ )
				hash.Add( car.Riders[rider] );
			hash.Add( car.Leaving );
			hash.Add( car.Broken );
			hash.Add( car.ApproachHeading );
		}
	}

	/// <summary>Takes the controller off the park's module list (a removed ride).</summary>
	internal void Release() => world.Remove( this );

	/// <summary>The riders aboard every car and the ejected ones, for a removed ride to hand back.</summary>
	public IEnumerable<int> AllRiders()
	{
		foreach ( var car in cars )
		{
			for ( var rider = 0; rider < Math.Abs( car.RiderCount ) && rider < TourCar.MaximumRiders; rider++ )
				yield return car.Riders[rider];
		}
		foreach ( var guest in Ejected )
			yield return guest;
	}
}
