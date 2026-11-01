using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The arrival update with the park's vehicles (tp.exe 0x004CF3E0, the dispatch 0x0051A2F0 and the status read 0x0051A690), on a
/// synthetic park whose vehicles are plain variable pairs the test moves by hand.
/// </summary>
[TestClass]
public class ArrivalVehicleTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	/// <summary>Two script variables (0 trigger, 1 status) per vehicle kind; a missing kind cannot be created.</summary>
	private sealed class FakeVehicles : IArrivalVehicleHost
	{
		public readonly bool[] Defined = { true, true, true };
		public readonly bool[] Placed = new bool[3];
		public readonly int[][] Variables = { new int[2], new int[2], new int[2] };
		public readonly List<int> Created = new();

		public bool Exists( int kind ) => Placed[kind];

		/// <summary>The thing ids the saved handles name in the tests (the bus 20, the seaplane 21, the Ferry 22).</summary>
		public int ThingId( int kind ) => Placed[kind] ? 20 + kind : 0;

		public bool Create( int kind )
		{
			if ( !Defined[kind] )
				return false;
			Placed[kind] = true;
			Created.Add( kind );
			return true;
		}

		public int GetVariable( int kind, int index ) => Placed[kind] ? Variables[kind][index] : 0;
		public void SetVariable( int kind, int index, int value ) => Variables[kind][index] = value;
		public int Trigger( int kind ) => Variables[kind][ArrivalVehicleKind.TriggerVariable];
		public int Status( int kind ) => Variables[kind][ArrivalVehicleKind.StatusVariable];
		public void SetStatus( int kind, int value ) => Variables[kind][ArrivalVehicleKind.StatusVariable] = value;
		public void ClearTrigger( int kind ) => Variables[kind][ArrivalVehicleKind.TriggerVariable] = 0;
	}

	/// <summary>The shipped vehicles as bare object runtimes (their scripts and animation channels, no models drawn).</summary>
	private sealed class RuntimeVehicles : IArrivalVehicleHost
	{
		private readonly ObjectCatalog catalog;
		private readonly RideScriptWorld world = new();
		private readonly OriginalObjectRuntime?[] placed = new OriginalObjectRuntime?[3];

		public RuntimeVehicles( ObjectCatalog catalog ) => this.catalog = catalog;

		public bool Exists( int kind ) => placed[kind] != null;

		public bool Create( int kind )
		{
			if ( ArrivalVehicles.Definition( catalog, ArrivalVehicleKind.Names[kind] ) is not { } entry )
				return false;
			placed[kind] = new OriginalObjectRuntime( entry, world, seed: kind + 1 );
			return true;
		}

		public int GetVariable( int kind, int index ) => placed[kind]?.Script?.Variables[index] ?? 0;

		public void SetVariable( int kind, int index, int value )
		{
			if ( placed[kind]?.Script is { } script )
				script.Variables[index] = value;
		}

		public void Simulate( double seconds )
		{
			foreach ( var runtime in placed )
				runtime?.Simulate( seconds );
		}
	}

	private sealed class Park
	{
		public long Turn;
		public readonly FakeVehicles Vehicles = new();
		public readonly GuestSimulation Simulation;
		public int Points;

		public Park()
		{
			var settings = GuestTests.CreateSettings();
			settings.ArrivalTimeBetween = 2;
			Simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 1 )
			{
				ArrivalClock = () => Turn,
				ParkPoints = () => Points,
				ArrivalVehicles = Vehicles
			};
		}

		/// <summary>Sets the world generator so that its next draw modulo 3 is <paramref name="kind"/> (the pick of a vehicle without a group, tp.exe 0x0051A34B).</summary>
		public void NextVehicleIs( int kind )
		{
			for ( var state = 1u; ; state++ )
				if ( new WorldRng( state ).NextWord() % 3 == (uint)kind )
				{
					Simulation.World.Restore( state );
					return;
				}
		}

		/// <summary>Runs whole park turns (15 ticks each, the turn starting on the first).</summary>
		public void Turns( int count )
		{
			for ( var index = 0; index < count; index++ )
			{
				Turn++;
				for ( var tick = 0; tick < 15; tick++ )
					Simulation.Tick( Tick );
			}
		}

		public int Arrived => Simulation.Guests.Count;
	}

	/// <summary>
	/// A read-only visit has no economy: its arrivals read the turn of the save it is built on plus its guests' own turns (<see cref="Level.VisitArrivalClock"/>), so the visit of a park
	/// built on the shipped Jungle save (turn 755) sends its first group at once, as an own park started from that save does; counted from turn 0, the timer of 150 units of four turns
	/// would hold the first group back until turn 604, and no guest would arrive for the first two and a half minutes.
	/// </summary>
	[TestMethod]
	public void AVisitBuiltOnASaveSendsItsFirstGroupAtOnce()
	{
		(GuestSimulation Simulation, FakeVehicles Vehicles) Visit( long? savedTurn )
		{
			var settings = GuestTests.CreateSettings();
			settings.ArrivalTimeBetween = 150; // [DATA:Standard.sam:Arrival.TimeBetweenArrivals]
			var vehicles = new FakeVehicles();
			var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 1 ) { OnlineVisit = true, ArrivalVehicles = vehicles };
			simulation.ArrivalClock = Level.VisitArrivalClock( simulation, savedTurn );
			return (simulation, vehicles);
		}
		void Turns( GuestSimulation simulation, int count )
		{
			for ( var tick = 0; tick < count * 15; tick++ )
				simulation.Tick( Tick );
		}

		var saved = Visit( 755 );
		Turns( saved.Simulation, 2 );
		CollectionAssert.AreEqual( new[] { ArrivalVehicleKind.Bus }, saved.Vehicles.Created, "turn 755 is past the timer (755 >> 2 = 188 > 150): the first turn sends the bus" );

		var unsaved = Visit( null );
		Assert.IsNull( unsaved.Simulation.ArrivalClock, "without a save the guests' own turns count, as in a park started without one" );
		Turns( unsaved.Simulation, 590 );
		Assert.AreEqual( 0, unsaved.Vehicles.Created.Count, "from turn 0 no group is due before turn 604 (604 >> 2 = 151 > 150)" );
		Turns( unsaved.Simulation, 20 );
		CollectionAssert.AreEqual( new[] { ArrivalVehicleKind.Bus }, unsaved.Vehicles.Created );
	}

	/// <summary>[EXT:cheats-arrival] While an arrival cheat waits (docs/CHEATS.md) no natural group starts; once it has applied the natural timer goes on.</summary>
	[TestMethod]
	public void AWaitingArrivalCheatHoldsTheNaturalGroup()
	{
		foreach ( var withVehicles in new[] { true, false } )
		{
			var park = new Park();
			if ( !withVehicles ) park.Simulation.ArrivalVehicles = null;
			var held = true;
			park.Simulation.ArrivalHeldForCheat = () => held;
			var groups = new List<int>();
			park.Simulation.ArrivalGroupStarted += groups.Add;
			park.Turns( 40 );
			Assert.AreEqual( 0, groups.Count, "the timer is long past, but the waiting cheat holds the group" );
			Assert.AreEqual( 0, park.Vehicles.Created.Count, "and calls no vehicle" );
			held = false;
			park.Turns( 1 );
			Assert.AreEqual( 1, groups.Count, "released: the natural group starts at the next turn" );
		}
	}

	[DataTestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void BothArrivalUpdatesReportSignedGroupsAboveTheirParkLimit( bool online, bool withVehicles )
	{
		var park = new Park();
		park.Simulation.OnlineVisit = online;
		if ( !withVehicles ) park.Simulation.ArrivalVehicles = null;
		var limit = online ? GuestSimulation.OnlineArrivalParkLimit : GuestSimulation.ArrivalParkLimit;
		for ( var index = 0; index < limit + 3; index++ )
			park.Simulation.SpawnInPark( 5, 5 );
		var groups = new List<int>();
		park.Simulation.ArrivalGroupStarted += groups.Add;
		park.Turn = 100;
		park.Turns( 1 );
		CollectionAssert.AreEqual( new[] { -3 }, groups, "the actual update emits the signed remainder before handling an empty group" );
		Assert.AreEqual( limit + 3, park.Arrived );
		if ( withVehicles )
		{
			CollectionAssert.AreEqual( new[] { ArrivalVehicleKind.Bus }, park.Vehicles.Created );
			park.Vehicles.SetStatus( ArrivalVehicleKind.Bus, 2 );
		}
		park.Turns( 1 );
		Assert.AreEqual( limit + 3, park.Arrived, "neither update creates a guest for a negative group" );
		Assert.IsFalse( park.Simulation.CaptureSnapshot().DeliveringArrivals );
		CollectionAssert.AreEqual( new[] { -3 }, groups, "finishing the empty group does not append a clamped zero" );
	}

	[TestMethod]
	public void NoGuestStepsOffBeforeTheVehicleReportsItsStop()
	{
		var park = new Park();
		park.Turns( 12 );
		CollectionAssert.AreEqual( new[] { ArrivalVehicleKind.Bus }, park.Vehicles.Created, "a group of four calls the bus (size 1: up to 35 guests)" );
		Assert.AreEqual( 1, park.Vehicles.Status( ArrivalVehicleKind.Bus ), "a new vehicle is placed with status 1 (0x0051A59B)" );
		Assert.AreEqual( ArrivalVehicleKind.Bus, park.Simulation.ArrivalVehicle );
		park.Turns( 40 );
		Assert.AreEqual( 0, park.Arrived, "status 1: the vehicle is still driving to its stop" );

		park.Vehicles.SetStatus( ArrivalVehicleKind.Bus, 2 );
		park.Turns( 1 );
		Assert.AreEqual( 1, park.Arrived, "status 2: one guest steps off per park turn" );
		park.Turns( 3 );
		Assert.AreEqual( 4, park.Arrived );
		Assert.AreEqual( 0, park.Vehicles.Trigger( ArrivalVehicleKind.Bus ), "the group is not yet found empty" );
		park.Turns( 1 );
		Assert.AreEqual( 4, park.Arrived );
		Assert.AreEqual( 1, park.Vehicles.Trigger( ArrivalVehicleKind.Bus ), "the turn that finds the group empty sends the vehicle on" );
		Assert.AreEqual( 0, park.Simulation.CaptureSnapshot().PendingArrivals );
		Assert.IsFalse( park.Simulation.CaptureSnapshot().DeliveringArrivals, "and restarts the timer" );
	}

	[TestMethod]
	public void TheGroupSizePicksTheVehicleAndAMissingOneFallsBackFerrySeaplaneBus()
	{
		Park Started( int points, Action<FakeVehicles>? setup = null )
		{
			var park = new Park { Points = points };
			setup?.Invoke( park.Vehicles );
			park.Turns( 12 );
			return park;
		}
		// (int)(1.2 × (points + NewParkBonus 20)) / PointsPerVisitor 6
		CollectionAssert.AreEqual( new[] { ArrivalVehicleKind.Bus }, Started( 155 ).Vehicles.Created, "35 guests: the bus" );
		CollectionAssert.AreEqual( new[] { ArrivalVehicleKind.Seaplane }, Started( 160 ).Vehicles.Created, "36 guests: the seaplane" );
		CollectionAssert.AreEqual( new[] { ArrivalVehicleKind.Seaplane }, Started( 280 ).Vehicles.Created, "60 guests: the seaplane" );
		CollectionAssert.AreEqual( new[] { ArrivalVehicleKind.Ferry }, Started( 285 ).Vehicles.Created, "61 guests: the Ferry" );
		CollectionAssert.AreEqual( new[] { ArrivalVehicleKind.Ferry }, Started( 0, vehicles => vehicles.Defined[0] = false ).Vehicles.Created,
			"no bus: the Ferry is tried first (0x0051A442)" );
		CollectionAssert.AreEqual( new[] { ArrivalVehicleKind.Seaplane }, Started( 0, vehicles => vehicles.Defined[0] = vehicles.Defined[2] = false ).Vehicles.Created );
		var none = Started( 0, vehicles => vehicles.Defined[0] = vehicles.Defined[1] = vehicles.Defined[2] = false );
		Assert.AreEqual( -1, none.Simulation.ArrivalVehicle, "no vehicle at all: none is at work" );
	}

	[TestMethod]
	public void AVehicleThatReportsSixIsReleasedAndAParkedOneIsTriggeredNotPlacedAgain()
	{
		var park = new Park();
		park.Turns( 12 );
		park.Vehicles.SetStatus( ArrivalVehicleKind.Bus, 2 );
		park.Turns( 5 );
		Assert.AreEqual( 4, park.Arrived );
		park.Vehicles.ClearTrigger( ArrivalVehicleKind.Bus );
		park.Vehicles.SetStatus( ArrivalVehicleKind.Bus, 6 );
		park.Turns( 1 );
		Assert.AreEqual( -1, park.Simulation.ArrivalVehicle, "status 6: the vehicle is gone (0x0051A690)" );
		Assert.AreEqual( 0, park.Vehicles.Status( ArrivalVehicleKind.Bus ), "its status is set back to 0" );
		park.Turns( 12 );
		CollectionAssert.AreEqual( new[] { ArrivalVehicleKind.Bus }, park.Vehicles.Created, "the next group sends the parked bus again" );
		Assert.AreEqual( 1, park.Vehicles.Trigger( ArrivalVehicleKind.Bus ), "through its trigger (variable 0)" );
		Assert.AreEqual( ArrivalVehicleKind.Bus, park.Simulation.ArrivalVehicle );
		Assert.AreEqual( 4, park.Arrived, "no guest before the stop" );
	}

	[TestMethod]
	public void LeavingGuestsCallAVehicleByAWorldDrawAndBoardAtStatusFour()
	{
		var park = new Park();
		park.Simulation.ArrivalsEnabled = true;
		// three guests stand on the bus's two stop cells of lane A's bus stop (the cell (1, 2) and its +X neighbour), two on the first
		var stops = new[] { (1, 2), (1, 2), (2, 2) };
		var waiting = new List<Guest>();
		foreach ( var cell in stops )
		{
			var guest = park.Simulation.SpawnInPark( 10, 0 );
			(guest.Navigation.X, guest.Navigation.Y) = GuestNavigation.BytePoint( cell, 100, 100 );
			(guest.CellX, guest.CellY) = cell;
			guest.State = GuestState.WaitingToGoHome;
			guest.HasWaypoint = false;
			waiting.Add( guest );
		}
		var (first, second, third) = (waiting[0], waiting[1], waiting[2]);
		park.Turns( 1 );   // the cells' guest lists are rebuilt at the start of the turn, after the arrival update has read the old ones
		park.NextVehicleIs( ArrivalVehicleKind.Ferry );
		park.Turns( 1 );
		CollectionAssert.AreEqual( new[] { ArrivalVehicleKind.Ferry }, park.Vehicles.Created, "a waiting guest and no vehicle at work: size 0, a world draw mod 3" );
		park.Turns( 30 );
		Assert.AreEqual( 3, park.Simulation.Guests.Count( guest => guest.State == GuestState.WaitingToGoHome ), "they wait for the vehicle, not a timer" );

		park.Vehicles.SetStatus( ArrivalVehicleKind.Ferry, 4 );
		park.Turns( 1 );
		// 0x00500BD0: the guest that heads its cell's list boards, so one per stop cell and turn
		var cellsInUse = stops.Distinct().Count();
		Assert.AreEqual( 3 - cellsInUse, park.Simulation.Guests.Count, "status 4: one guest per stop cell boards" );
		if ( park.Simulation.Guests.Count > 0 )
			Assert.AreEqual( 0, park.Vehicles.Trigger( ArrivalVehicleKind.Ferry ), "a guest still waits: the vehicle is held" );
		for ( var turn = 0; turn < 3 && park.Simulation.Guests.Count > 0; turn++ )
			park.Turns( 1 );
		Assert.AreEqual( 0, park.Simulation.Guests.Count );
		park.Turns( 1 );
		Assert.AreEqual( 1, park.Vehicles.Trigger( ArrivalVehicleKind.Ferry ), "status 4 with nobody waiting sends the vehicle on" );
	}

	[TestMethod]
	public void TheVehicleAtWorkIsSavedAndValidated()
	{
		var park = new Park();
		park.Turns( 12 );
		var snapshot = park.Simulation.CaptureSnapshot();
		Assert.AreEqual( ArrivalVehicleKind.Bus, snapshot.ArrivalVehicle );
		var restored = new Park();
		restored.Simulation.RestoreSnapshot( snapshot );
		Assert.AreEqual( ArrivalVehicleKind.Bus, restored.Simulation.ArrivalVehicle );
		Assert.AreEqual( -1, new Park().Simulation.ArrivalVehicle );

		var json = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( snapshot ) );
		Assert.AreEqual( ArrivalVehicleKind.Bus, json.ArrivalVehicle, "the field survives the save file" );
		var old = System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( snapshot with { ArrivalVehicle = null } ) );
		Assert.IsNull( ParkResumeJson.Deserialize<GuestSimulationSnapshot>( System.Text.Encoding.UTF8.GetBytes( old.Replace( "\"ArrivalVehicle\":null,", "" ) ) ).ArrivalVehicle,
			"a save without the field loads with none at work" );
		restored.Simulation.RestoreSnapshot( snapshot with { ArrivalVehicle = null } );
		Assert.AreEqual( -1, restored.Simulation.ArrivalVehicle, "a save from before the vehicles has none at work" );
		var errors = restored.Simulation.ValidateSnapshot( snapshot with { ArrivalVehicle = 3 } ).Errors;
		Assert.IsTrue( errors.Any( error => error.Contains( "ArrivalVehicle 3" ) ), string.Join( "; ", errors ) );
	}

	[TestMethod]
	public void AVehicleAtWorkThatIsNotInTheParkIsDroppedInsteadOfHoldingTheGroup()
	{
		var park = new Park();
		park.Turns( 12 );
		var snapshot = park.Simulation.CaptureSnapshot();
		Assert.AreEqual( ArrivalVehicleKind.Bus, snapshot.ArrivalVehicle );
		var restored = new Park();
		restored.Simulation.RestoreSnapshot( snapshot );
		Assert.IsFalse( restored.Vehicles.Placed[ArrivalVehicleKind.Bus], "the new park holds no bus object" );
		restored.Turns( 1 );
		Assert.IsTrue( restored.Vehicles.Created.Contains( ArrivalVehicleKind.Bus ), "the group places its vehicle again" );
	}

	private static SaveWorld SavedArrivals( uint turn = 109, uint timer = 103, int group = 7, byte active = 0, int handle = -1, int[]? handles = null )
	{
		var map = OriginalParkImportTests.CreateMap( 3, 2, ( _, _ ) => 0 );
		var variables = SaveTestPayloads.ParkVariables( gameTick: turn );
		// the cached bus, seaplane and Ferry handles and the vehicle at work (world +0x1DA72C/E/30 and +0x1DA72A)
		foreach ( var (offset, slot) in new[] { (4, 0), (6, 1), (8, 2), (12, 3) } )
			System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( variables.AsSpan( offset ), (ushort)(offset == handle ? 15 : handles?[slot] ?? 0) );
		var calendar = SaveTestPayloads.CalendarAndArrivals();
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( calendar.AsSpan( 28 ), 91 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( calendar.AsSpan( 32 ), timer );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( calendar.AsSpan( 36 ), 73 );
		System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( calendar.AsSpan( 40 ), group );
		calendar[44] = active;
		calendar[45] = 255;
		var payload = SaveTestPayloads.Payload( SaveTestPayloads.World( variables, SaveTestPayloads.Grid( map ),
			SaveTestPayloads.Things( SaveTestPayloads.ParkThings() ), calendar: calendar ) );
		return SaveWorld.Parse( payload, SavePayloadLayout.Parse( payload ), map.CellCountX, map.CellCountY );
	}

	[TestMethod]
	public void OriginalArrivalFieldsDecodeWithoutLosingSignedGroupsOrNoncanonicalFlags()
	{
		var saved = SavedArrivals( group: -7, active: 128 ).ArrivalState;
		Assert.AreEqual( new SaveArrivals( 91, 103, 73, -7, true, true ), saved );
		Assert.ThrowsException<ArgumentException>( () => SaveArrivals.Parse( new byte[17] ) );
		Assert.ThrowsException<ArgumentException>( () => SaveArrivals.Parse( new byte[19] ) );
	}

	[TestMethod]
	public void ImportedInactiveSchedulerUsesTheSavedTurnBoundaryWithoutDispatchingAtAttach()
	{
		var park = new Park { Turn = 109 };
		var groups = new List<int>();
		park.Simulation.ArrivalGroupStarted += groups.Add;
		var random = park.Simulation.RandomState;
		var report = park.Simulation.ImportOriginalArrivals( SavedArrivals() );
		StringAssert.Contains( report, "scheduler imported" );
		Assert.AreEqual( random, park.Simulation.RandomState );
		Assert.AreEqual( 0, groups.Count );
		Assert.AreEqual( 0, park.Vehicles.Created.Count );
		Assert.AreEqual( 103L, park.Simulation.CaptureSnapshot().ArrivalTimerStart );
		Assert.AreEqual( 7, park.Simulation.CaptureSnapshot().PendingArrivals );
		park.Turns( 2 );
		Assert.AreEqual( 0, groups.Count, "the separately shifted timer and current turn differ by exactly the interval" );
		park.Turns( 1 );
		Assert.AreEqual( 1, groups.Count );
		CollectionAssert.AreEqual( new[] { ArrivalVehicleKind.Bus }, park.Vehicles.Created );
	}

	[DataTestMethod]
	[DataRow( 50, ArrivalVehicleKind.Seaplane )]
	[DataRow( 0, ArrivalVehicleKind.Bus )]
	[DataRow( -3, ArrivalVehicleKind.Bus )]
	public void ImportedActiveGroupContinuesOnTheNextNormalUpdateAndOwnSave( int group, int kind )
	{
		var park = new Park { Turn = 109 };
		var groups = new List<int>();
		park.Simulation.ArrivalGroupStarted += groups.Add;
		var random = park.Simulation.RandomState;
		StringAssert.Contains( park.Simulation.ImportOriginalArrivals( SavedArrivals( group: group, active: 1 ) ), "scheduler imported" );
		Assert.AreEqual( random, park.Simulation.RandomState );
		Assert.AreEqual( 0, park.Vehicles.Created.Count );
		Assert.AreEqual( 0, groups.Count );
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( park.Simulation.CaptureSnapshot() ) );
		var resumed = new Park { Turn = park.Turn };
		resumed.Simulation.RestoreSnapshot( snapshot );
		park.Turns( 1 );
		resumed.Turns( 1 );
		CollectionAssert.AreEqual( new[] { kind }, park.Vehicles.Created );
		Assert.AreEqual( random, park.Simulation.RandomState, "dispatch by saved group size consumes no random draw" );
		Assert.AreEqual( group, park.Simulation.CaptureSnapshot().PendingArrivals );
		Assert.AreEqual( 0, groups.Count, "an imported active group does not emit a second group-history event" );
		park.Vehicles.SetStatus( kind, 2 );
		resumed.Vehicles.SetStatus( kind, 2 );
		park.Turns( 1 );
		resumed.Turns( 1 );
		Assert.AreEqual( group > 0 ? group - 1 : group, park.Simulation.CaptureSnapshot().PendingArrivals );
		Assert.AreEqual( group > 0, park.Simulation.CaptureSnapshot().DeliveringArrivals );
		Assert.AreEqual( group > 0 ? 1 : 0, park.Arrived );
		Assert.AreEqual( group > 0 ? 103L : park.Turn, park.Simulation.CaptureSnapshot().ArrivalTimerStart );
		CollectionAssert.AreEqual( ParkResumeJson.Serialize( park.Simulation.CaptureSnapshot() ), ParkResumeJson.Serialize( resumed.Simulation.CaptureSnapshot() ) );
		Assert.AreEqual( 0, groups.Count );
	}

	[DataTestMethod]
	[DataRow( "cache1", 4 )]
	[DataRow( "cache2", 6 )]
	[DataRow( "cache3", 8 )]
	[DataRow( "current", 12 )]
	[DataRow( "present", -1 )]
	[DataRow( "future", -1 )]
	[DataRow( "clock", -1 )]
	[DataRow( "noclock", -1 )]
	[DataRow( "nohost", -1 )]
	[DataRow( "online", -1 )]
	public void UnsupportedOriginalSchedulersLeaveTheCompleteStateUnchanged( string reason, int handle )
	{
		var park = new Park { Turn = 109 };
		StringAssert.Contains( park.Simulation.ImportOriginalArrivals( SavedArrivals( timer: 97, group: 52, active: 1 ) ), "scheduler imported" );
		if ( reason == "present" ) park.Vehicles.Placed[ArrivalVehicleKind.Ferry] = true;
		if ( reason == "clock" ) park.Turn++;
		if ( reason == "noclock" ) park.Simulation.ArrivalClock = null;
		if ( reason == "nohost" ) park.Simulation.ArrivalVehicles = null;
		if ( reason == "online" ) park.Simulation.OnlineVisit = true;
		var groups = new List<int>();
		park.Simulation.ArrivalGroupStarted += groups.Add;
		var before = ParkResumeJson.Serialize( park.Simulation.CaptureSnapshot() );
		var variables = park.Vehicles.Variables.SelectMany( values => values ).ToArray();
		StringAssert.Contains( park.Simulation.ImportOriginalArrivals( SavedArrivals( timer: reason == "future" ? 110u : 103u, group: -7, active: 1, handle: handle ) ), "scheduler unchanged" );
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( park.Simulation.CaptureSnapshot() ), "including all scheduler fields and the RNG" );
		CollectionAssert.AreEqual( variables, park.Vehicles.Variables.SelectMany( values => values ).ToArray() );
		Assert.AreEqual( 0, groups.Count );
		Assert.AreEqual( 0, park.Vehicles.Created.Count );
	}

	private static Park ParkWithVehicles()
	{
		var park = new Park { Turn = 109 };
		park.Vehicles.Placed[0] = park.Vehicles.Placed[1] = park.Vehicles.Placed[2] = true;
		return park;
	}

	[DataTestMethod]
	[DataRow( 20, ArrivalVehicleKind.Bus, 2 )]
	[DataRow( 21, ArrivalVehicleKind.Seaplane, 4 )]
	[DataRow( 22, ArrivalVehicleKind.Ferry, 3 )]
	public void ASavedVehicleAtWorkIsTheCachedOneItsHandleNamesAtItsSavedStatus( int current, int kind, int status )
	{
		var park = ParkWithVehicles();
		park.Vehicles.SetStatus( kind, status );
		var random = park.Simulation.RandomState;
		var report = park.Simulation.ImportOriginalArrivals( SavedArrivals( group: 3, active: 1, handles: new[] { 20, 21, 22, current } ) );
		StringAssert.Contains( report, $"the {ArrivalVehicleKind.Names[kind]} (thing {current}) at work" );
		Assert.AreEqual( kind, park.Simulation.ArrivalVehicle );
		Assert.AreEqual( random, park.Simulation.RandomState );
		Assert.AreEqual( 0, park.Vehicles.Created.Count );
		var snapshot = park.Simulation.CaptureSnapshot();
		Assert.AreEqual( kind, snapshot.ArrivalVehicle );
		Assert.AreEqual( 3, snapshot.PendingArrivals );
		// the next update reads the vehicle's own status as the original does: at 2 a guest steps off, at 4 the group waits
		park.Turns( 1 );
		Assert.AreEqual( status == 2 ? 1 : 0, park.Arrived );
		Assert.AreEqual( kind, park.Simulation.ArrivalVehicle );
	}

	[TestMethod]
	public void CachedHandlesWithNoVehicleAtWorkImportTheSchedulerAndKeepTheParksVehicles()
	{
		var park = ParkWithVehicles();
		StringAssert.Contains( park.Simulation.ImportOriginalArrivals( SavedArrivals( group: 7, handles: new[] { 20, 21, 22, 0 } ) ), "no vehicle at work" );
		Assert.AreEqual( -1, park.Simulation.ArrivalVehicle );
		Assert.AreEqual( 0, park.Vehicles.Created.Count );
	}

	[DataTestMethod]
	[DataRow( "wrong-bus", 21, 21, 22, 0 )]
	[DataRow( "missing-ferry-handle", 20, 21, 0, 0 )]
	[DataRow( "stray-current", 20, 21, 22, 15 )]
	[DataRow( "current-without-cache", 20, 21, 22, 99 )]
	public void SavedVehicleHandlesThatDoNotNameTheParksVehiclesLeaveTheSchedulerUnchanged( string name, int bus, int plane, int ferry, int current )
	{
		var park = ParkWithVehicles();
		var before = ParkResumeJson.Serialize( park.Simulation.CaptureSnapshot() );
		StringAssert.Contains( park.Simulation.ImportOriginalArrivals( SavedArrivals( group: 9, active: 1, handles: new[] { bus, plane, ferry, current } ) ), "scheduler unchanged", name );
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( park.Simulation.CaptureSnapshot() ), name );
	}

	/// <summary>
	/// The shipped jungle bus (inconclusive without OPENTPW_GAME_PATH): placed on the first group, its bus.RSE waits 2 s, starts its
	/// drive-in clip (Busm1), waits for it to end and 1 s more, and only then reports status 2; the first guest steps off at the next
	/// park turn. Sent on, it reports 3, 4 and, as nobody waits, leaves (5, then 6).
	/// </summary>
	[TestMethod]
	public void TheShippedBusReachesItsStopAfterItsDriveInClip()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the shipped vehicle scripts." );
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			foreach ( var theme in ObjectCatalog.Themes )
				foreach ( var name in ArrivalVehicleKind.Names )
					Assert.IsNotNull( ArrivalVehicles.Definition( ObjectCatalog.Load( theme ), name ), $"{theme} defines {name} (by archive name; hallow's seaplane is called Plane)" );
			var catalog = ObjectCatalog.Load( "jungle" );
			var vehicles = new RuntimeVehicles( catalog );
			var bus = catalog.Entries.Single( entry => entry.ArchiveName == "bus" );
			var clip = ObjectAssets.LoadModel( bus.FileSystem, bus.ResolveAnimation( 5, 0 )!.Path ).Clip!;
			var clipLength = new OriginalObjectRuntime( bus ).Animator.StartLengthMilliseconds( clip );

			var settings = GuestTests.CreateSettings();
			settings.ArrivalTimeBetween = 2;
			var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 1 ) { ArrivalVehicles = vehicles };
			double? placed = null, atStop = null, firstGuest = null, sentOn = null;
			var statuses = new List<int>();
			var seconds = 0.0;
			var triggered = 0;
			for ( var tick = 0; tick < 120 * FixedStepClock.TicksPerSecond && statuses.LastOrDefault() != 6; tick++ )
			{
				simulation.Tick( Tick );
				vehicles.Simulate( Tick );
				seconds += Tick;
				if ( placed == null && vehicles.Exists( ArrivalVehicleKind.Bus ) )
					placed = seconds;
				var status = vehicles.GetVariable( ArrivalVehicleKind.Bus, ArrivalVehicleKind.StatusVariable );
				if ( placed != null && (statuses.Count == 0 || statuses[^1] != status) )
					statuses.Add( status );
				if ( status == 1 )
					triggered = Math.Max( triggered, vehicles.GetVariable( ArrivalVehicleKind.Bus, 3 ) );
				if ( status == 2 )
					atStop ??= seconds;
				if ( simulation.Guests.Count > 0 )
					firstGuest ??= seconds;
				if ( sentOn == null && vehicles.GetVariable( ArrivalVehicleKind.Bus, ArrivalVehicleKind.TriggerVariable ) == 1 )
					sentOn = seconds;
			}
			Assert.IsNotNull( placed, "the first group places the bus" );
			Assert.IsNotNull( atStop );
			var drive = (atStop!.Value - placed!.Value) * 1000;
			// bus.RSE keeps the drive-in trigger's result less 3000 in VAR_TEMP (variable 3); TRIGANIM reports the clip length less 300 ms (Mac 0xafa94).
			var driveIn = triggered + 3000;
			Assert.AreEqual( clipLength - 300, driveIn, $"the drive-in trigger reports {driveIn} ms for the {clipLength} ms clip" );
			Assert.AreEqual( 2000 + driveIn + 1000, drive, 1000.0 / FixedStepClock.TicksPerSecond + 1, $"status 2 after the 2 s wait, the {driveIn} ms clip and 1 s" );
			Assert.IsTrue( firstGuest >= atStop && firstGuest - atStop < 0.3, "the first guest steps off at the next park turn" );
			Assert.AreEqual( 4, simulation.Guests.Count );
			Assert.IsNotNull( sentOn, "the empty group sends the bus on" );
			CollectionAssert.AreEqual( new[] { 1, 2, 3, 4, 0, 5, 6 }, statuses.ToArray() );
		}
		finally
		{
			FileSystem = previous;
		}
	}
}
