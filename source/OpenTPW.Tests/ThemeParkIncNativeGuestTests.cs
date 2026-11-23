using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's native guest states, phase P1 (docs/reverse/TPI-GUESTS.md): the pure tables, the setter's entries, the pre-update, the arrival and leaving states, the vehicle dispatch and the
/// save block, over a small rig with the switch <see cref="GuestSimulation.ThemeParkIncNativeGuests"/> on.
/// </summary>
[TestClass]
public sealed class ThemeParkIncNativeGuestTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	private sealed class Payments : IGuestPayments
	{
		public bool Closed;
		public int Fee = 30;
		public int Admitted;
		public readonly List<int> Stays = new();
		public readonly List<(int Category, int Value)> Opinions = new();
		public int AdmissionFee => Fee;
		public bool ParkClosed => Closed;
		public bool TryPayAdmission( out int feePaid ) { feePaid = Fee; Admitted++; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
		public void StayEnded( int days ) => Stays.Add( days );
		public void PriceTested( int category, int value ) => Opinions.Add( (category, value) );
	}

	private sealed class Vehicles : IArrivalVehicleHost
	{
		public readonly bool[] Defined = { true, true, true };
		public readonly bool[] Placed = new bool[3];
		public readonly int[][] Variables = { new int[2], new int[2], new int[2] };
		public readonly List<string> Log = new();
		public bool Exists( int kind ) => Placed[kind];
		public bool Create( int kind )
		{
			Log.Add( $"create {kind}" );
			if ( !Defined[kind] )
				return false;
			Placed[kind] = true;
			Variables[kind] = new int[2];
			return true;
		}
		public void Destroy( int kind )
		{
			Log.Add( $"destroy {kind}" );
			Placed[kind] = false;
		}
		public int GetVariable( int kind, int index ) => Placed[kind] ? Variables[kind][index] : 0;
		public void SetVariable( int kind, int index, int value ) => Variables[kind][index] = value;
	}

	/// <summary>Theme Park Inc's shipped PeepInfo words, booths at (10, 13) and (11, 13), the bus stop A at (10, 5) and the entrances at (10, 17) and (11, 17).</summary>
	internal static ThemeParkIncGuestSettings NativeSettings() => new()
	{
		SmallHappinessChange = 5, MediumHappinessChange = 15, BigHappinessChange = 25, ExcitementToCostDivisor = 6, MinimumEntryFee = 20,
		CheapPriceMultiplier = 0.75f, AveragePriceMultiplier = 1.25f, ExpensivePriceMultiplier = 2f,
		OpinionForMuchTooHigh = 0, OpinionForTooHigh = 20, OpinionForTooLow = 100, OpinionForAboutRight = 50,
		UmbrellaModifier = 1, WeatherModifierFrequency = 4, SlowestWeatherChangeResponse = 3, DiscardBalloonPctModifier = 100,
		ThoughtBubbleHungry = 80, ThoughtBubbleThirsty = 80, ThoughtBubbleNeedTheLoo = 80, ThoughtBubbleSick = 80, ThoughtBubbleMess = 0.3f, ThoughtBubbleHappy = 80, ThoughtBubbleUnhappy = 20,
		HappinessWeather = [new( 1, -1, -1, 1 ), new( 1, -1, -1.5f, -2 ), new( -1, -2, -2, -2 )],
		ThirstWeather = [new( 2, 0, 0, -1 ), new( 2, 0, 0, -1 ), new( 3, 0, 0, -1 )],
		HungerWeather = [new( -1, 0, 0, 1 ), new( -1, 0, 0, 1 ), new( -1, 0, 0, 1 )],
		NumTicketBooths = 2, TicketBooths = [(10, 13, 512), (11, 13, 1536), (0, 0, 0)],
		BusStopA = (10, 5), BusStopB = (16, 5), BusStopC = (18, 5), EntranceA = (10, 17), EntranceB = (11, 17), FirstHourMinutes = 120
	};

	private static (GuestSimulation Simulation, Payments Payments, Vehicles Vehicles) Rig( bool native = true, bool withVehicles = false )
	{
		var grid = new GuestPathGrid( 30, 24 );
		for ( var x = 4; x <= 20; x++ )
			grid.Cells.SetType( x, 5, ParkCellTypes.FixedWalkway );
		foreach ( var x in new[] { 10, 11 } )
			for ( var y = 5; y <= 16; y++ )
				grid.Cells.SetType( x, y, ParkCellTypes.FixedWalkway );
		for ( var x = 5; x <= 25; x++ )
			grid.SetPath( x, 17, true );
		var settings = new GuestSettings
		{
			TpiGuestStart = new( Enumerable.Range( 0, 24 ).Select( i => new ThemeParkIncGuestType( 90 - i, 300 + 20 * i, 40 + i, i / 8 ) ).ToArray(), 400, 30, 15, 70, 20, 10, 10 ),
			TpiNative = NativeSettings(),
			ArrivalTimeBetween = 5
		};
		var payments = new Payments();
		var vehicles = new Vehicles();
		var simulation = new GuestSimulation( grid, settings, 7 )
		{
			ThemeParkIncNativeGuests = native, ArrivalsEnabled = false, Payments = payments, ArrivalVehicles = withVehicles ? vehicles : null
		};
		return (simulation, payments, vehicles);
	}

	private static void Turns( GuestSimulation simulation, int turns )
	{
		var target = simulation.ParkTurn + turns;
		while ( simulation.ParkTurn < target )
			simulation.Tick( Tick );
	}

	/// <summary>Ticks to the start of the next park turn whose low two bits are <paramref name="phase"/>, without running it.</summary>
	private static void BeforePhase( GuestSimulation simulation, int phase )
	{
		while ( ((simulation.ParkTurn + 1) & 3) != phase || !simulation.NextTickStartsTurn )
			simulation.Tick( Tick );
	}

	// ---- The pure tables --------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheNameTableHoldsTheThirtyOneStateNames()
	{
		Assert.AreEqual( ThemeParkIncGuestStates.Count, ThemeParkIncGuestStates.Names.Count );
		Assert.AreEqual( "IDLE", ThemeParkIncGuestStates.Names[ThemeParkIncGuestStates.Idle] );
		Assert.AreEqual( "WAITING TO BE PICKED UP", ThemeParkIncGuestStates.Names[ThemeParkIncGuestStates.WaitingToBePickedUp] );
		Assert.AreEqual( "PLAYING SPOT ANIM", ThemeParkIncGuestStates.Names[8] );
	}

	/// <summary>The catch test 0x614720 over -1..40 (the native oracle tpi_guest_states_oracle.py runs the same range through the executable).</summary>
	[TestMethod]
	public void TheCatchTestCatchesSixToThirteenTwentyTwoAndEveryWordOutsideTheTable()
	{
		var catchable = Enumerable.Range( -1, 42 ).Where( ThemeParkIncGuestStates.IsCatchable ).ToArray();
		CollectionAssert.AreEqual( new[] { -1, 6, 7, 8, 9, 10, 11, 12, 13, 22, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40 }, catchable );
	}

	[TestMethod]
	public void TheQueuePredicateReadsTheSavedStateDuringASpotAnimation()
	{
		Assert.IsTrue( ThemeParkIncGuestStates.InQueueOrLoading( 14, 6 ) );
		Assert.IsTrue( ThemeParkIncGuestStates.InQueueOrLoading( 17, 0 ) );
		Assert.IsFalse( ThemeParkIncGuestStates.InQueueOrLoading( 18, 0 ) );
		Assert.IsTrue( ThemeParkIncGuestStates.InQueueOrLoading( 8, 15 ), "a spot animation in a queue keeps the queue words (0x61AC40)" );
		Assert.IsFalse( ThemeParkIncGuestStates.InQueueOrLoading( 8, 6 ) );
	}

	[TestMethod]
	public void EveryStateProjectsOntoAThemeParkWorldStateOfItsRole()
	{
		for ( var state = 0; state < ThemeParkIncGuestStates.Count; state++ )
			Assert.IsTrue( Enum.IsDefined( ThemeParkIncGuestStates.Projection( state ) ) && ThemeParkIncGuestStates.Projection( state ) != GuestState.Gone, $"state {state}" );
		Assert.AreEqual( GuestState.LeavingPark, ThemeParkIncGuestStates.Projection( 22 ) );
		Assert.AreEqual( GuestState.WaitingToGoHome, ThemeParkIncGuestStates.Projection( 30 ) );
		Assert.AreEqual( GuestState.HeldByGuard, ThemeParkIncGuestStates.Projection( 21 ) );
		Assert.AreEqual( GuestState.Using, ThemeParkIncGuestStates.Projection( 20 ) );
		Assert.AreEqual( GuestState.DoingNothing, ThemeParkIncGuestStates.Projection( 12 + 6 ), "TPI 18 waits for the infodesk; TPW 18 would be the leaving state" );
	}

	[TestMethod]
	public void TheEntryTableWritesTheAnimationModes()
	{
		foreach ( var state in new[] { 0, 2, 5, 7, 9, 13, 15, 16, 22, 29 } )
			Assert.AreEqual( 1, ThemeParkIncGuestStates.EntryMode( state ), $"state {state}" );
		foreach ( var state in new[] { 1, 3, 6, 14, 17, 27, 28, 30 } )
			Assert.AreEqual( 3, ThemeParkIncGuestStates.EntryMode( state ), $"state {state}" );
		foreach ( var state in new[] { 4, 8, 10, 11, 12, 18, 19, 20, 21, 23, 24, 25, 26 } )
			Assert.IsNull( ThemeParkIncGuestStates.EntryMode( state ), $"state {state}" );
	}

	// ---- The switch ---------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void WithTheSwitchOffATpiGuestHasNoNativeRecordAndTheSameDrawsAndHash()
	{
		var (off, _, _) = Rig( native: false );
		var (on, _, _) = Rig( native: true );
		var plain = off.SpawnInPark( 12, 17 );
		var native = on.SpawnInPark( 12, 17 );
		Assert.IsNull( plain.Tpi );
		Assert.IsNotNull( native.Tpi );
		Assert.AreEqual( off.World.State, on.World.State, "a guest built on a park cell draws the constructor's draws only, switch on or off" );
		Assert.AreEqual( (plain.Type, plain.Money, plain.ExitLevel, plain.VisitorNumber), (native.Type, native.Money, native.ExitLevel, native.VisitorNumber) );
		Assert.AreEqual( GuestState.DoingNothing, native.State );
		Assert.AreEqual( ThemeParkIncGuestStates.Idle, native.Tpi!.State );
		var (tpw, _, _) = Rig( native: true );
		tpw.Settings.TpiGuestStart = null;
		Assert.IsNull( tpw.SpawnInPark( 12, 17 ).Tpi, "the switch has no effect on a Theme Park World guest" );
	}

	// ---- The constructor's tail and the arrival --------------------------------------------------------------------------------

	[TestMethod]
	public void AnArrivalOffTheParkDrawsTwiceAfterTheConstructorWalksToItsBoothAndBeams()
	{
		var (simulation, _, _) = Rig();
		var (reference, _, _) = Rig( native: false );
		var beams = 0;
		simulation.Beamed += _ => beams++;
		var guest = simulation.SpawnArrival()!;
		reference.SpawnInPark( 12, 17 );
		var replay = new WorldRng( reference.World.State );
		replay.NextWord();
		replay.NextWord();
		Assert.AreEqual( replay.State, simulation.World.State, "R10 and R11 after the reseed and the sprite draw (0x60CA29, 0x60CA44), both results unused" );
		Assert.AreEqual( GuestNavigation.Centre( (10, 5) ), (guest.Navigation.X, guest.Navigation.Y), "BusStopA's centre with no vehicle at work (0x5D79F0(-1))" );
		Assert.AreEqual( ThemeParkIncGuestStates.WalkingToBooth, guest.Tpi!.State );
		Assert.AreEqual( GuestState.GoingToTicketBooth, guest.State );
		Assert.AreEqual( 1, guest.Tpi.Mode );
		Assert.AreEqual( 1, beams );
		Assert.AreEqual( 0, guest.VisitorNumber, "the visitor number comes at the entrance (state 5)" );
		var booth = guest.Id % 2 == 0 ? (10, 13) : (11, 13);
		Assert.AreEqual( booth, (guest.Navigation.TargetX >> 16, guest.Navigation.TargetY >> 16), "booth id % NumTicketBooths (0x60CA49)" );
		Assert.AreEqual( 0x80, (guest.Navigation.TargetX >> 8) & 0xFF, "the booth's centre (0x60B7F0)" );
	}

	[TestMethod]
	public void AGuestWalksFromTheStopThroughTheBoothAndTheEntranceIntoThePark()
	{
		var (simulation, payments, _) = Rig();
		payments.Fee = 10;
		var guest = simulation.SpawnArrival()!;
		var states = new List<int>();
		for ( var turn = 0; turn < 600 && guest.Tpi!.State != ThemeParkIncGuestStates.Idle; turn++ )
		{
			Turns( simulation, 1 );
			if ( states.Count == 0 || states[^1] != guest.Tpi.State )
				states.Add( guest.Tpi.State );
		}
		CollectionAssert.AreEqual( new[] { 2, 4, 3, 5, 6 }, states, $"the booth, the verdict, the wait for the gate, the entrance, idle: {string.Join( ", ", states )}" );
		Assert.IsTrue( guest.Tpi!.Paid );
		Assert.AreEqual( payments.Fee, guest.Tpi.FeesPaid, "+0x1C4 += the fee (0x615FC7)" );
		Assert.AreEqual( 1, payments.Admitted );
		Assert.AreEqual( 1, guest.VisitorNumber, "the visitor number at the entrance (0x650560)" );
		Assert.AreEqual( 1, simulation.VisitorCount );
		Assert.IsTrue( simulation.IsOnParkCell( guest ) );
	}

	// ---- The setter -----------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheSettersEntriesDrawStampAndCountAsTheTableSays()
	{
		var (simulation, payments, _) = Rig();
		var guest = simulation.SpawnInPark( 12, 17 );
		var tpi = guest.Tpi!;
		var replay = new WorldRng( simulation.World.State );
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WaitingForPark );
		Assert.AreEqual( (int)(replay.NextWord() % 150) + 200, tpi.Wait, "state 3's entry: R % 150 + 200 (0x61A5D2)" );
		Assert.AreEqual( replay.State, simulation.World.State );
		Assert.AreEqual( 3, tpi.Mode );

		tpi.Target = 77;
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.LeavingPark );
		Assert.AreEqual( (0, 1, GuestState.LeavingPark), (tpi.Target, tpi.Mode, guest.State), "state 22's entry clears +0x204 (0x61A952)" );
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WaitingForFirstAid );
		Assert.AreEqual( 100, tpi.FirstAidWait );
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.SpotAnimation );
		Assert.AreEqual( simulation.ParkTurn, tpi.SpotTurn );

		tpi.Byte21B = 7;
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.StandingInQueue );
		Assert.AreEqual( 8, tpi.Word21C, "_ftol(7 × 1.2f) = 8 (0x61A606)" );

		// the stay of a paid guest at the pick-up (0x61A96F): (turn − stamp) >> 2 << 2 as days
		tpi.Paid = true;
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WaitingToBePickedUp );
		CollectionAssert.AreEqual( new[] { ParkStatisticsHistory.StayDays( 0 ) }, payments.Stays );
		Assert.AreEqual( 3, tpi.Mode );
		Assert.ThrowsException<NotSupportedException>( () => simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.UnderGuardControl ),
			"the later phases' entries are not run in P1" );
	}

	[TestMethod]
	public void PayingAtABoothFacesTheBoothsAngle()
	{
		var (simulation, _, _) = Rig();
		var guest = simulation.SpawnArrival()!;
		var tpi = guest.Tpi!;
		simulation.PlaceForTest( guest, 11, 13 );
		tpi.Mode = 1;
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.PayingAtBooth );
		Assert.AreEqual( (1536, 3), (tpi.Heading, tpi.Mode), "booth 1's Angle and mode 3 (0x61A5B1)" );
		simulation.PlaceForTest( guest, 12, 17 );
		tpi.Heading = 9;
		tpi.Mode = 1;
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.PayingAtBooth );
		Assert.AreEqual( (9, 1), (tpi.Heading, tpi.Mode), "no booth's cell: no change" );
	}

	// ---- The pre-update ---------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void ThePreUpdateRunsOnTheGuestsPhaseAndDrawsTheThoughtOnly()
	{
		var (simulation, _, _) = Rig();
		var guest = simulation.SpawnInPark( 12, 17 );
		guest.ExitLevel = 10000;
		var phase = guest.Id & 3;
		BeforePhase( simulation, (phase + 1) & 3 );
		guest.ExitLevel = 400;
		var before = simulation.World.State;
		simulation.Tick( Tick );
		Assert.AreEqual( before, simulation.World.State, "off its phase an idle guest draws nothing (the P1 state 6 stub)" );
		Assert.AreEqual( 400, guest.ExitLevel );
		BeforePhase( simulation, phase );
		var replay = new WorldRng( simulation.World.State );
		simulation.Tick( Tick );
		replay.NextWord();
		Assert.AreEqual( replay.State, simulation.World.State, "the thought draw of 0x619E85 on a park cell" );
		Assert.AreEqual( 399, guest.ExitLevel, "+0x1E0 -= 1 (0x619BA3)" );
	}

	[TestMethod]
	public void AFullToiletCostsTwoAndAFullStomachOne()
	{
		var (simulation, _, _) = Rig();
		var guest = simulation.SpawnInPark( 12, 17 );
		guest.ExitLevel = 400;
		(guest.Happiness, guest.Toilet, guest.Hunger, guest.Thirst, guest.Nausea) = (50, 100, 100, 10, 0);
		BeforePhase( simulation, guest.Id & 3 );
		simulation.Tick( Tick );
		Assert.AreEqual( 50f - 1 - 1 - 1, guest.Happiness, "hunger at 100 (0x619D88), toilet above 80 (0x619DD1) and toilet at 100 (0x619E10)" );
		Assert.AreEqual( GuestSpeed.ToiletBonus, guest.ToiletBonus, "0x60B3F0(1): 25" );
	}

	[TestMethod]
	public void TheHistoryShiftsEveryTwentyTurnsForEveryPhase()
	{
		var (simulation, _, _) = Rig();
		var guest = simulation.SpawnInPark( 12, 17 );
		guest.ExitLevel = 10000;
		guest.TemporaryHistory[0] = 5;
		while ( (simulation.ParkTurn + 1) % 20 != 0 || !simulation.NextTickStartsTurn )
			simulation.Tick( Tick );
		simulation.Tick( Tick );
		CollectionAssert.AreEqual( new[] { 0, 5, 0, 0 }, guest.TemporaryHistory, "0x611D60 every 20 turns" );
	}

	// ---- The weather rule ----------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheWeatherRuleAppliesTheGenerationsRowOnTheFirstDayIncludingTheHeatWave()
	{
		var (simulation, _, _) = Rig();
		var guest = simulation.SpawnInPark( 12, 17 );
		guest.ExitLevel = 400;
		simulation.ArrivalWeatherState = () => UmbrellaRules.WeatherHeatWave;
		var row = guest.Tpi!.Generation;
		var settings = NativeSettings();
		(guest.Happiness, guest.Thirst, guest.Hunger, guest.Toilet, guest.Nausea) = (50, 40, 40, 0, 0);
		BeforePhase( simulation, guest.Id & 3 );
		var growth = ((simulation.ParkTurn + 1) & 0xF) == 0;
		simulation.Tick( Tick );
		Assert.AreEqual( 50 + settings.HappinessWeather[row].HeatWave, guest.Happiness, "the heat wave's row applies (0x6110E6)" );
		Assert.AreEqual( 40 + settings.ThirstWeather[row].HeatWave + (growth ? 2 : 0), guest.Thirst );
		Assert.AreEqual( 40 + settings.HungerWeather[row].HeatWave + (growth ? 2 : 0), guest.Hunger );
		Assert.AreEqual( 0, guest.Tpi.WeatherDay, "+0x2E8 = day 0" );
	}

	[TestMethod]
	public void AnOpenUmbrellaSoftensTheRainAndAClosedOneSchedulesWithAWorldDraw()
	{
		var (simulation, _, _) = Rig();
		var guest = simulation.SpawnInPark( 12, 17 );
		guest.ExitLevel = 400;
		simulation.ArrivalWeatherState = () => UmbrellaRules.WeatherRain;
		simulation.SetUmbrellaStateForTest( guest, UmbrellaRules.StateOpen );
		(guest.Happiness, guest.Thirst, guest.Hunger) = (50, 40, 40);
		var row = guest.Tpi!.Generation;
		BeforePhase( simulation, guest.Id & 3 );
		simulation.Tick( Tick );
		Assert.AreEqual( MeterMath.Add( 50, NativeSettings().HappinessWeather[row].Rain + 1 ), guest.Happiness, "UmbrellaModifier 1 on a negative delta (0x6111BE)" );

		simulation.SetUmbrellaStateForTest( guest, UmbrellaRules.StateClosed );
		BeforePhase( simulation, guest.Id & 3 );
		var replay = new WorldRng( simulation.World.State );
		var delay = replay.Next( 3 );
		simulation.Tick( Tick );
		Assert.AreEqual( UmbrellaRules.StateOpening, guest.UmbrellaState );
		Assert.AreEqual( UmbrellaRules.DueTurn( simulation.ParkTurn, delay ), guest.UmbrellaDueTurn, "rand() % 3 from the world generator ([EXT:tpi-staff-crt-rand])" );
	}

	// ---- The states -------------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void FirstAidWaitsHundredAndOneUpdatesAndAFrozenGuestIsIdleNext()
	{
		var (simulation, _, _) = Rig();
		var guest = simulation.SpawnInPark( 12, 17 );
		guest.ExitLevel = 10000;
		var tpi = guest.Tpi!;
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WaitingForFirstAid );
		Turns( simulation, 101 );
		Assert.AreEqual( ThemeParkIncGuestStates.WaitingForFirstAid, tpi.State );
		Turns( simulation, 1 );
		Assert.AreEqual( ThemeParkIncGuestStates.Idle, tpi.State, "the 102nd update (0x614830)" );
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.Frozen );
		Turns( simulation, 1 );
		Assert.AreEqual( ThemeParkIncGuestStates.Idle, tpi.State, "0x61A307" );
	}

	[TestMethod]
	public void ASpotAnimationRestoresTheSavedStateElevenTurnsLater()
	{
		var (simulation, _, _) = Rig();
		var guest = simulation.SpawnInPark( 12, 17 );
		guest.ExitLevel = 10000;
		var tpi = guest.Tpi!;
		tpi.Saved = ThemeParkIncGuestStates.WaitingForFirstAid;
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.SpotAnimation );
		Turns( simulation, 10 );
		Assert.AreEqual( ThemeParkIncGuestStates.SpotAnimation, tpi.State );
		Turns( simulation, 1 );
		Assert.AreEqual( ThemeParkIncGuestStates.WaitingForFirstAid, tpi.State, "turn > +0x230 + 10 (0x61A382)" );
	}

	[TestMethod]
	public void TheIdleStubSendsAGuestWithoutExitTimeToTheBusStop()
	{
		var (simulation, _, _) = Rig();
		var guest = simulation.SpawnInPark( 12, 17 );
		guest.ExitLevel = 1;
		guest.Happiness = 60;
		Turns( simulation, 8 );
		Assert.AreEqual( ThemeParkIncGuestStates.LeavingPark, guest.Tpi!.State );
		Assert.AreEqual( 60f - 25f, guest.Happiness, 1.5f, "BigHappinessChange (0x614460(2)), give or take the full-need costs" );
		Assert.AreEqual( (10, 5), (guest.Navigation.TargetX >> 16, guest.Navigation.TargetY >> 16), "a point of BusStopA (0x615D50)" );
	}

	[TestMethod]
	public void ALeavingGuestReachesTheStopAndBoardsAtStatusFour()
	{
		var (simulation, payments, vehicles) = Rig( withVehicles: true );
		var guest = simulation.SpawnInPark( 12, 17 );
		guest.ExitLevel = 0;
		for ( var turn = 0; turn < 400 && guest.Tpi!.State != ThemeParkIncGuestStates.WaitingToBePickedUp; turn++ )
			Turns( simulation, 1 );
		Assert.AreEqual( ThemeParkIncGuestStates.WaitingToBePickedUp, guest.Tpi!.State );
		Assert.AreEqual( 0x400, guest.Tpi.Heading, "the bus or none at work (0x6186A6)" );
		Assert.AreEqual( 0, payments.Stays.Count, "an unpaid guest records no stay" );
		simulation.RestoreArrivalVehicleForTest( ArrivalVehicleKind.Bus );
		vehicles.Placed[0] = true;
		vehicles.Variables[0][ArrivalVehicleKind.StatusVariable] = 4;
		Turns( simulation, 1 );
		Assert.IsFalse( simulation.Guests.Contains( guest ), "status 4: destroyed (0x637C40)" );
	}

	[TestMethod]
	public void AWaitingGuestOffTheStopDrawsThreeTimesAndWalksAgain()
	{
		var (simulation, _, vehicles) = Rig( withVehicles: true );
		var guest = simulation.SpawnInPark( 12, 17 );
		guest.ExitLevel = 10000;
		BeforePhase( simulation, (guest.Id + 1) & 3 );
		simulation.SetThemeParkIncState( guest, guest.Tpi!, ThemeParkIncGuestStates.WaitingToBePickedUp );
		simulation.RestoreArrivalVehicleForTest( ArrivalVehicleKind.Bus );
		vehicles.Placed[0] = true;
		vehicles.Variables[0][ArrivalVehicleKind.StatusVariable] = 2;
		var replay = new WorldRng( simulation.World.State );
		simulation.Tick( Tick );
		var pick = replay.NextWord() & 1;
		var bx = replay.NextWord() & 0xFF;
		var by = replay.NextWord() & 0xFF;
		Assert.AreEqual( replay.State, simulation.World.State, "three draws (0x6188AF, 0x6188C7, 0x6188E2)" );
		Assert.AreEqual( ThemeParkIncGuestStates.LeavingPark, guest.Tpi!.State );
		Assert.AreEqual( (pick == 1 ? 11 : 10, 5), (guest.Navigation.TargetX >> 16, guest.Navigation.TargetY >> 16) );
		Assert.AreEqual( ((int)bx, (int)by), ((guest.Navigation.TargetX >> 8) & 0xFF, (guest.Navigation.TargetY >> 8) & 0xFF), "the free sub-cell point (0x60B8E0)" );
	}

	[TestMethod]
	public void AClosedParkHoldsAWaitingGuestUntilItsWaitRunsOut()
	{
		var (simulation, payments, _) = Rig();
		var guest = simulation.SpawnArrival()!;
		var tpi = guest.Tpi!;
		payments.Closed = true;
		simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.WaitingForPark );
		var wait = tpi.Wait;
		Turns( simulation, wait );
		Assert.AreEqual( (ThemeParkIncGuestStates.WaitingForPark, 0), (tpi.State, tpi.Wait) );
		Turns( simulation, 1 );
		Assert.AreEqual( ThemeParkIncGuestStates.LeavingPark, tpi.State, "+0x228 at 0: the bus stop and state 22 (0x615C90)" );
		Assert.AreEqual( 0, guest.ExitLevel );
	}

	[TestMethod]
	public void TheBoothsVerdictsFollowTheirBands()
	{
		foreach ( var (points, fee, expected) in new[] { (0, 10, ThemeParkIncGuestStates.WaitingForPark), (0, 1000, ThemeParkIncGuestStates.LeavingPark) } )
		{
			var (simulation, payments, _) = Rig();
			payments.Fee = fee;
			simulation.ParkPoints = () => points;
			var guest = simulation.SpawnArrival()!;
			var tpi = guest.Tpi!;
			simulation.PlaceForTest( guest, 10, 13 );
			simulation.SetThemeParkIncState( guest, tpi, ThemeParkIncGuestStates.PayingAtBooth );
			Turns( simulation, 1 );
			Assert.AreEqual( expected, tpi.State, $"fee {fee}" );
			if ( fee <= 20 )
				Assert.AreEqual( 0, payments.Opinions.Count, "the early fair exit records nothing (0x615856)" );
			else
				CollectionAssert.AreEqual( new[] { (0, 0) }, payments.Opinions, "far too expensive records OpinionForMuchTooHigh" );
		}
	}

	// ---- The arrival vehicles ---------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheDispatchDestroysEveryCachedVehicleAndBuildsTheDrawnOne()
	{
		var (simulation, _, vehicles) = Rig( withVehicles: true );
		vehicles.Placed[0] = vehicles.Placed[2] = true;
		simulation.ArrivalsEnabled = true;
		simulation.ParkPoints = () => 0;
		var replay = new WorldRng( simulation.World.State );
		while ( simulation.ArrivalVehicle < 0 )
			Turns( simulation, 1 );
		CollectionAssert.AreEqual( new[] { "destroy 0", "destroy 2", "create 0" }, vehicles.Log.ToArray(), "0x64FC47..0x64FCA4, then the group's size 1: the bus" );
		Assert.AreEqual( 1, vehicles.Variables[0][ArrivalVehicleKind.StatusVariable], "VAR 1 = 1 (0x64FF0A)" );
		Assert.AreEqual( replay.State, simulation.World.State, "a sized dispatch draws nothing" );
	}

	[TestMethod]
	public void AMissingVehicleIsRetriedMonoHeliBus()
	{
		var (simulation, _, vehicles) = Rig( withVehicles: true );
		vehicles.Defined[0] = vehicles.Defined[2] = false;
		simulation.ArrivalsEnabled = true;
		simulation.ParkPoints = () => 0;
		while ( simulation.ArrivalVehicle < 0 && simulation.ParkTurn < 2000 )
			Turns( simulation, 1 );
		CollectionAssert.AreEqual( new[] { "create 0", "create 2", "create 1" }, vehicles.Log.Where( entry => entry.StartsWith( "create" ) ).ToArray(), "the untried kinds in the order mono, heli, bus (0x64FDB4)" );
		Assert.AreEqual( 1, simulation.ArrivalVehicle );
	}

	[TestMethod]
	public void AGuestOfTheHeliStepsOffAtBusStopB()
	{
		var (simulation, _, _) = Rig( withVehicles: true );
		simulation.RestoreArrivalVehicleForTest( 1 );
		var guest = simulation.SpawnArrival()!;
		Assert.AreEqual( GuestNavigation.Centre( (16, 5) ), (guest.Navigation.X, guest.Navigation.Y), "0x5D79F0(1): BusStopB" );
	}

	// ---- The guard and the save ----------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheGuardAsksTheNativeCatchTest()
	{
		var (simulation, _, _) = Rig();
		var guest = simulation.SpawnInPark( 12, 17 );
		IGuardGuests guard = simulation;
		Assert.IsTrue( guard.IsCatchable( guest.Id ), "6" );
		simulation.SetThemeParkIncState( guest, guest.Tpi!, ThemeParkIncGuestStates.WaitingToBePickedUp );
		Assert.IsFalse( guard.IsCatchable( guest.Id ), "30" );
		simulation.SetThemeParkIncState( guest, guest.Tpi!, ThemeParkIncGuestStates.LeavingPark );
		Assert.IsTrue( guard.IsCatchable( guest.Id ), "22 is catchable in Theme Park Inc" );
	}

	[TestMethod]
	public void ANativeGuestRoundTripsThroughTheSaveAndAPlainOneWritesNoBlock()
	{
		var (simulation, _, _) = Rig();
		var guest = simulation.SpawnArrival()!;
		guest.ExitLevel = 10000;
		Turns( simulation, 12 );
		var bytes = ParkResumeJson.Serialize( simulation.CaptureSnapshot() );
		var saved = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( bytes )!;
		Assert.AreEqual( GuestSimulationSnapshot.CurrentSchemaVersion, saved.SchemaVersion );
		Assert.IsNotNull( saved.Guests.Single().Tpi );
		var (resumed, _, _) = Rig();
		resumed.RestoreSnapshot( saved );
		Assert.AreEqual( simulation.ComputeStateHash(), resumed.ComputeStateHash() );
		Turns( simulation, 30 );
		Turns( resumed, 30 );
		Assert.AreEqual( simulation.ComputeStateHash(), resumed.ComputeStateHash(), "the resumed park continues the same run" );

		var (plain, _, _) = Rig( native: false );
		plain.SpawnInPark( 12, 17 );
		Assert.IsFalse( System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( plain.CaptureSnapshot() ) ).Contains( "\"Tpi\"" ), "a switch-off save holds no native block" );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => resumed.RestoreSnapshot( saved with { SchemaVersion = 9 } ), "a schema 9 save cannot hold the block" );
	}
}
