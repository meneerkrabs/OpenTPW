namespace OpenTPW;

public partial class Level
{
	private FlatRideBounceRiders? rideBodyPoses;

	/// <summary>
	/// Test and smoke seam: called at the end of every fixed tick with the ticks run so far, after the tick's body pose phase and command checkpoint, so a caller that advances
	/// several ticks in one frame still sees the world after each of them (docs/DETERMINISM.md, "Frame batching"). It runs inside the tick's scope (<see cref="ParkCommands.EnterTick"/>):
	/// an edit for a later tick is held with <see cref="ParkCommands.Schedule"/>, not issued.
	/// </summary>
	internal Action<long>? TickCompleted { get; set; }

	/// <summary>
	/// The part of a frame after the world input (<see cref="Update"/>): the path layer, the pools' drawing, the evening, the snow and the scene clock, then the fixed-step simulation for
	/// <paramref name="frameSeconds"/> of real time at the game's time scale, then every entity's update. Everything here outside <see cref="AdvanceSimulation"/> is presentation: it writes
	/// nothing the state hash covers, the simulation reads or a save holds for the simulation, so the park after a tick depends on the ticks and the commands only, never on how the ticks
	/// fall into frames (docs/DETERMINISM.md, "Frame batching"). <paramref name="updateEntities"/> false leaves out the entities' updates (the resume smoke, whose particle pool must stand still).
	/// </summary>
	internal void AdvanceFrame( double frameSeconds, bool updateEntities = true )
	{
		RefreshRenderedCells();
		RefreshPools();
		UpdateEvening( frameSeconds );
		UpdateSnow( frameSeconds );
		SceneClock.Advance( frameSeconds * SimulationTimeScale );
		var simulationStart = FrameStats.Begin();
		AdvanceSimulation( frameSeconds * SimulationTimeScale );
		FrameStats.EndSimulation( simulationStart );
		if ( !updateEntities )
			return;
		foreach ( var entity in Entity.All.ToArray() )
			entity.Update();
	}

	/// <summary>The frame, the replay and the smoke harnesses advance the same fixed-step simulation; every tick ends with the shared body pose phase.</summary>
	internal void AdvanceSimulation( double elapsedSeconds )
	{
		AttachCheatHost();
		simulationClock.Advance( elapsedSeconds, deltaTime =>
		{
			// [EXT:online-coop-commands] the commands held for this tick apply before it (a replayed log; in single player the HUD's commands have run already); no command may be applied while
			// the tick itself runs, since a replay or a peer would apply it at the tick's start (ParkCommands.Strict)
			ApplyDueCommands();
			using var tickScope = Commands.EnterTick();
			// The staff host's bookkeeping, after the commands and before anything of the tick reads a member: a member saved picked up that no hand holds goes back to idle (an imported save),
			// and a member without a position (hired since the last tick, or loaded so) is put on the gate path. Both ran once per frame before the world input; inside the tick they see the
			// same commands at the same tick whether those came from this player's input, a replayed log or (later) a session frame, and however many ticks a frame runs.
			StaffPlacement?.Reconcile();
			StaffWorld?.Synchronize();
			SyncObjectEconomy();
			// the substep this tick stands in (the park's own tick before the economy advances it): none while the park is paused
			var substep = SimulationSubstep.ForTick( Park?.Economy.Tick ?? simulationClock.TickCount, Park == null || Park.Economy.Speed != GameSpeed.Paused );
			// [BIN:TPW-EXE:0x00516380 world update] a lost park (world state 4, ParkEconomy.IsBankrupt) runs no thing updates: the guests and the staff walks stop, the ride scripts (the RSSE tick) go on
			var lost = Park?.Economy.IsBankrupt == true;
			Guests?.SetBodySpriteHostRunning( substep.Running );
			if ( substep.Running )
				Guests?.SetBodySpriteClock( GuestSimulation.BodySpriteSampleAtTick( (Park?.Economy.Tick ?? simulationClock.TickCount) + 1 ) );
			// A park whose guests take their ids from the level's thing pool runs its turns as the original does (0x00516380): the tick that starts a turn runs the substep's ride scripts, then the sweep over the
			// pool newest first, the gate retry, the turn's destroys and the arrival update. Without a park or its guests in the pool, in a lost park or where the economy and the guests count turns apart (a paused
			// economy) the turn is not swept: the things update in the order of the ticks between turns, and the destroys are freed at the end of each tick.
			var pooled = Park != null && Guests is { SharesThings: true };
			// the guests' tick count starts at 0 where the economy of an original save starts at its turn: both cut the ticks into the same turns
			if ( pooled && Park!.Economy.Speed != GameSpeed.Paused )
				Guests!.AlignTurns( Park.Economy.Tick );
			var guestsTurn = pooled && Guests!.NextTickStartsTurn;
			var economyTurn = pooled && Park!.Economy.NextTickStartsTurn;
			// [BIN:TPW-EXE:0x0051643B lost park turn] a lost park (world state 4) still runs the turn: its counter, the gate retry, the destroys and the world tail with the arrival update (0x4D7B20); only the thing walk is skipped
			var sweeping = guestsTurn && economyTurn;
			if ( sweeping )
				SimulateSweptTurn( deltaTime, substep );
			else
			{
				if ( !lost )
					Guests?.Tick( deltaTime );
				Objects.Simulate( deltaTime, substep, PlacedRide );
				// The non-pooled host keeps its existing guest-before-object order; only pooled turn sweeps provide native scheduler-before-guest ordering.
				if ( substep.TourDue )
					Guests?.ScheduleBodySprites();
				Park?.FixedTick();
				if ( !lost )
					AdvanceWeather();
				// The deferred gate-close retry follows the thing sweep (Mac 0x10536c).
				Gate?.Tick();
			}
			if ( !lost )
				StaffWorld?.Tick( deltaTime );
			UpdateTrackRendering( deltaTime );
			UpdateTourRendering( deltaTime );
			// [BIN:TPW-EXE:0x00516641 turn's end] the park turn ends by freeing the things that asked for their destruction, last request first (0x00516310, 0x00516200); a swept turn has done it already, and a park whose
			// things share the level's pool frees them only at a turn: the substeps between turns leave the requests waiting
			if ( !sweeping && (!pooled || guestsTurn || economyTurn) )
				Things.DestroyQueued();
			PrepareRideBodyPoses();
			AfterCommandTick( simulationClock.TickCount + 1 );
			TickCompleted?.Invoke( simulationClock.TickCount + 1 );
		} );
	}

	/// <summary>
	/// The ride bodies' pose phase, at the end of every fixed tick: it asks the guests' body players for templates, creates and deletes bodies and stores the walkers' headings
	/// (<see cref="FlatRideBounceRiders.Prepare"/>), state a park save keeps. A paused park's ticks run none.
	/// </summary>
	// [EXT:fixed-60hz-clock] tp.exe's main loop 0x0054E360 runs the walk update 0x00557AB0 and the bounce pass once per rendered frame, after the frame's substeps (none, one or several); OpenTPW
	// runs them after every fixed tick, so the bodies a save keeps depend on the ticks, never on how they fall into frames (docs/DETERMINISM.md, "Frame batching"). A frame of one tick (60 Hz at
	// normal speed) is unchanged. A frame without a tick runs no pose phase: tp.exe's walk update still runs on a frame without a substep and reads the free-running scaled timer 0x785970
	// (0x00557AFF -> 0x00402D70), but OpenTPW's phase reads the script and body clocks, which only the ticks advance, so on such a frame it only repeated the requests of the frame before, unless
	// the park changed between the two (a load, an edit), when the bodies now follow at the next tick's end instead
	private void PrepareRideBodyPoses()
	{
		if ( Guests != null && (Park == null || Park.Economy.Speed != GameSpeed.Paused) )
			(rideBodyPoses ??= new FlatRideBounceRiders( Objects )).Prepare( Guests );
	}

	/// <summary>The weather's step: the thing sweep's weather update (0x00512880), and on every other tick the first call that starts it.</summary>
	private void AdvanceWeather()
	{
		// [EXT:tpi-viewer] Theme Park Inc's weather is not traced (its states and keys differ from the Theme Park World weather this class ports), so a Theme Park Inc park has none
		if ( Park == null || Guests == null || Park.Economy.Rules.ThemeParkInc )
			return;
		Weather ??= new ParkWeather( Park.Economy.Settings.Standard, Seed.WeatherStream, Guests.Grid.Cells.Width, Guests.Grid.Cells.Height, FeatureGates.WinterWeather, FeatureGates.WinterAlways );
		AttachSnow();
		Weather.AdvanceTo( Park.Economy.Tick );
	}

	/// <summary>
	/// One tick that starts a park turn, in the order of the original's substep and turn (main loop 0x0054E360 case 10, the world update 0x00516380): the guests' tick counters, the ride scripts and
	/// the bump module of the substep, then the economy's turn, whose thing sweep (<see cref="LevelThingSweep"/>) updates every thing newest first.
	/// </summary>
	// [BIN:TPW-EXE:0x0054F4F2 world substep] each 31 ms substep runs 0x520130, the bump module 0x546C80 (0x54F55F) and the RSSE tick 0x5516B0 (0x54F56B); every eighth substep (0x54F668) then calls the park turn (0x54F7BB)
	private void SimulateSweptTurn( float deltaTime, SimulationSubstep substep )
	{
		Guests!.BeginSweepTick( deltaTime );
		var items = Objects.Objects.Concat( PlacedRide is { IsDeleted: false } prototype ? new OriginalObject[] { prototype } : [] ).ToArray();
		foreach ( var item in items )
			item.Visitors.DefersAdmission = true;
		try
		{
			Objects.Simulate( deltaTime, substep, PlacedRide );
		}
		finally
		{
			foreach ( var item in items )
				item.Visitors.DefersAdmission = false;
		}
		if ( substep.TourDue )
			Guests!.ScheduleBodySprites();
		Park!.Economy.ThingSweep = new LevelThingSweep( this );
		try
		{
			Park.FixedTick();
		}
		finally
		{
			Park.Economy.ThingSweep = null;
		}
	}

	/// <summary>
	/// The original's park turn after its counter has advanced (0x00516380): the sweep over the thing pool newest first (0x0050B360 by thing type: a guest, an attraction, a staff member, the weather, the
	/// challenge system), the gate retry, the turn's deferred destroys and the arrival update (0x004D7B20), before the economy's calendar tail.
	/// </summary>
	private sealed class LevelThingSweep( Level level ) : IThingSweep
	{
		public void Run( ParkEconomy economy, long turn, bool lost ) => level.RunThingSweep( economy, turn, lost );
	}

	private void RunThingSweep( ParkEconomy economy, long turn, bool lost )
	{
		var guests = Guests!;
		if ( !lost )
		{
			var world = economy.StaffWorld;
			var restArea = economy.HasRestArea;
			var members = economy.Staff.Members.Where( member => member.ThingId != 0 ).GroupBy( member => member.ThingId ).ToDictionary( group => group.Key, group => group.First() );
			var swept = new HashSet<StaffMember>();
			var attractions = SweepableObjects();
			var visited = new HashSet<OriginalObject>();
			var visitedStates = new HashSet<int>();
			var weatherDone = false;
			var challengesDone = false;
			world?.BeginStaffTurn( economy, turn );
			// [BIN:TPW-EXE:0x00516380 thing sweep] the walk starts at the used list's head (0x7CF56C), the thing built last, and takes each slot's older link after the update (0x516413..0x516431); the things built during the walk
			// are newer than the position it has reached and are not visited, so a snapshot of the list is the same walk; a thing that asked to be destroyed (byte +3) is skipped (0x5163F2..0x5163F7)
			foreach ( var slot in Things.Capture() )
			{
				if ( Things.IsQueued( slot.Id ) )
					continue;
				if ( guests.SweepGuest( slot.Id ) )
					continue;
				if ( members.TryGetValue( slot.Id, out var member ) )
				{
					if ( swept.Add( member ) )
						SweepStaffMember( economy, world, member, turn, restArea );
					continue;
				}
				if ( attractions.TryGetValue( slot.Id, out var item ) )
				{
					if ( visited.Add( item ) )
						SweepAttraction( economy, item, turn, visitedStates );
					continue;
				}
				switch ( slot.Model )
				{
					// [BIN:TPW-EXE:0x0050B360 thing update] type 0xF is the weather's update 0x00512880, type 0x13 the challenge system's 0x004D1E90
					case SaveThingModel.Weather when !weatherDone:
						weatherDone = true;
						AdvanceWeather();
						break;
					case SaveThingModel.ChallengeSystem when !challengesDone:
						challengesDone = true;
						economy.UpdateChallenges( turn );
						break;
				}
			}
			// what has no slot in the pool (a headless scene's guests, a thing the pool does not hold) is updated after the sweep, in list order
			guests.FinishSweep();
			foreach ( var member in economy.Staff.Members.ToList() )
				if ( swept.Add( member ) )
					SweepStaffMember( economy, world, member, turn, restArea );
			foreach ( var item in SweepableObjects( withoutSlot: true ).Values )
				if ( visited.Add( item ) )
					SweepAttraction( economy, item, turn, visitedStates );
			foreach ( var instance in economy.ObjectInstanceIds().ToList() )
				if ( !visitedStates.Contains( instance ) && economy.TryGetObject( instance, out var state ) )
					economy.UpdateRide( state, turn );
			if ( !weatherDone )
				AdvanceWeather();
			if ( !challengesDone )
				economy.UpdateChallenges( turn );
			if ( world != null )
				world.EndStaffTurn( economy, turn );
			else
				economy.DispatchHandymen();
		}
		// [BIN:TPW-EXE:0x0051643B gate retry] every 30th turn the park's gate thing is asked to close a closed, empty park (the walk above has ended, 0x516442..0x516641)
		Gate?.Tick();
		// [BIN:TPW-EXE:0x00516641 deferred destroy] the turn's destroy requests are carried out last request first once the walk and the gate retry are over (0x516641..0x516691)
		Things.DestroyQueued();
		// [BIN:TPW-EXE:0x00516695 world turn tail] 0x004D7B20 then runs the arrival update (0x004CF3E0), the staff pool (0x005084F0), the calendar (0x004F8260) and every 100th turn the golden ticket step (0x004D4A00)
		// [BIN:TPW-EXE:0x004D7B20 world tail] runs in every world state: the arrival update has no lost-park test of its own (0x4CF3E0 reads the vehicle and the group only), so a lost park still sends its bus and steps guests off
		guests.UpdateTurnEndArrivals();
	}

	/// <summary>One staff member's thing update of the sweep: its research (a researcher's pre-update, every 20th turn), its vitals and job timer, and the world's state machine.</summary>
	private static void SweepStaffMember( ParkEconomy economy, IStaffWorld? world, StaffMember member, long turn, bool restArea )
	{
		// [BIN:TPW-EXE:0x00502960 researcher update] the research comes first in the researcher's update, on every 20th turn (0x14)
		if ( member.Type == StaffType.Researcher && turn % ParkResearch.TurnsPerResearch == 0 )
			economy.ResearchBy( member );
		economy.UpdateStaffVitals( member, restArea );
		economy.CountDownJob( member );
		world?.UpdateStaffMember( economy, member, turn );
	}

	/// <summary>One object's thing update of the sweep: the economy's wear and breakdown step (0x004E0B90), then the ride update's admission (0x004E0E00).</summary>
	private void SweepAttraction( ParkEconomy economy, OriginalObject item, long turn, HashSet<int> visitedStates )
	{
		if ( ObjectInstanceOf( item ) is { } instance && economy.TryGetObject( instance, out var state ) )
		{
			visitedStates.Add( instance );
			economy.UpdateRide( state, turn );
		}
		item.Visitors.UpdateAdmission();
	}

	/// <summary>The park's objects by thing id (those that hold a slot of the pool), or the ones without a slot; a deleted object is not swept.</summary>
	private Dictionary<int, OriginalObject> SweepableObjects( bool withoutSlot = false )
	{
		var result = new Dictionary<int, OriginalObject>();
		var counter = 0;
		foreach ( var item in Objects.Objects.Concat( PlacedRide is { IsDeleted: false } prototype ? new OriginalObject[] { prototype } : [] ) )
		{
			if ( item.IsDeleted )
				continue;
			var slotted = item.ThingId != 0 && Things.IsUsed( item.ThingId );
			if ( slotted == withoutSlot )
				continue;
			result[withoutSlot ? --counter : item.ThingId] = item;
		}
		return result;
	}

	/// <summary>The economy instance an object is linked to, the prototype's included; null for an object the economy does not hold.</summary>
	private int? ObjectInstanceOf( OriginalObject item ) =>
		economyInstances.TryGetValue( item, out var instance ) ? instance : ReferenceEquals( item, PlacedRide ) ? prototypeInstance : null;
}
