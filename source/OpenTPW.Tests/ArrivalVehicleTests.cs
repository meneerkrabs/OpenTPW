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
		public uint Draw;

		public Park()
		{
			var settings = GuestTests.CreateSettings();
			settings.ArrivalTimeBetween = 2;
			Simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 1 )
			{
				ArrivalClock = () => Turn,
				ParkPoints = () => Points,
				ArrivalVehicles = Vehicles,
				WorldDraw = () => Draw
			};
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
		var park = new Park { Draw = 0x7FFF_FFF4 }; // 2147483636 mod 3 = 2: the Ferry
		park.Simulation.ArrivalsEnabled = true;
		var first = park.Simulation.SpawnArrival()!;
		var second = park.Simulation.SpawnArrival()!;
		var third = park.Simulation.SpawnArrival()!;
		foreach ( var guest in new[] { first, second, third } )
			guest.State = GuestState.WaitingToGoHome;
		park.Turns( 1 );
		CollectionAssert.AreEqual( new[] { ArrivalVehicleKind.Ferry }, park.Vehicles.Created, "a waiting guest and no vehicle at work: size 0, a world draw mod 3" );
		park.Turns( 30 );
		Assert.AreEqual( 3, park.Simulation.Guests.Count( guest => guest.State == GuestState.WaitingToGoHome ), "they wait for the vehicle, not a timer" );

		park.Vehicles.SetStatus( ArrivalVehicleKind.Ferry, 4 );
		park.Turns( 1 );
		// Lanes alternate A, B, A: one guest boards per stop cell and turn.
		Assert.AreEqual( 1, park.Simulation.Guests.Count, "status 4: one guest per stop cell boards" );
		Assert.AreEqual( 0, park.Vehicles.Trigger( ArrivalVehicleKind.Ferry ), "a guest still waits: the vehicle is held" );
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
			// bus.RSE keeps the drive-in trigger's result less 3000 in VAR_TEMP (variable 3): the Busm1 clip's time to its end.
			var driveIn = triggered + 3000;
			Assert.IsTrue( driveIn > 3000 && driveIn <= clipLength, $"the drive-in trigger reports {driveIn} ms of the {clipLength} ms clip" );
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
