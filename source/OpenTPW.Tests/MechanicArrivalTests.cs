using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class MechanicArrivalTests
{
	private static (ParkEconomy Park, ParkObjectState Ride, StaffMember Member, StaffWorldSimulation World, GuestPathGrid Grid) Fixture( bool connected = true, bool live = true )
	{
		var extra = string.Concat( Enumerable.Range( 0, 5 ).Select( grade => $"MechanicConstsPerGrade[{grade}].WorkDuration 1\n" ) );
		var park = new ParkEconomy( EconomyTestData.Settings( extra: extra ), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		var ride = park.RegisterExisting( 1100 ); ride.Repair = 0; ride.IsBrokenDown = true;
		var grid = new GuestPathGrid( 6, 3 );
		for ( var x = 0; x < 6; x++ ) grid.SetPath( x, 1, true );
		if ( !connected ) grid.SetPath( 2, 1, false );
		var world = new StaffWorldSimulation( grid, () => park.Staff.Members, id => id == ride.Id ? (5, 1) : null, (0, 1) );
		if ( live ) park.MechanicJobWorld = world;
		var member = EconomyTestData.HireBest( park, StaffType.Mechanic ); world.Synchronize(); EconomyTestData.AdvanceToTurn( park, 1 ); Assert.AreEqual( member.Id, ride.MechanicId ); Assert.AreEqual( 2L, member.BusyTicks );
		return (park, ride, member, world, grid);
	}
	private static void Tick( ParkEconomy park, StaffWorldSimulation world, int frames )
	{
		for ( var i = 0; i < frames; i++ ) { park.AdvanceFixedTick(); world.Tick( 1f / 60 ); }
	}
	[TestMethod]
	public void FiveCellDistantMechanicCannotFinishOrCountDownBeforeArrival()
	{
		var f = Fixture(); Tick( f.Park, f.World, 60 );
		Assert.IsTrue( f.Ride.IsBrokenDown, "Old remote repair completed before its actor travelled five cells." );
		Assert.AreEqual( 2L, f.Member.BusyTicks ); Assert.IsTrue( f.Member.CellX < 5 );
	}
	[TestMethod]
	public void DisconnectedMechanicCannotRepairFromAcrossMissingPath()
	{
		var f = Fixture( connected: false ); Tick( f.Park, f.World, 600 );
		Assert.IsTrue( f.Ride.IsBrokenDown, "No route must not silently become an immediate remote repair." );
		Assert.AreEqual( 2L, f.Member.BusyTicks ); Assert.AreEqual( 0.5f, f.Member.CellX ); Assert.IsFalse( f.World.Snapshots.Single().JobReachable );
	}
	[TestMethod]
	public void NewHeadlessJobRetainsImmediateExistingCountdown()
	{
		var f = Fixture( live: false ); EconomyTestData.AdvanceToTurn( f.Park, 3 ); Assert.IsFalse( f.Ride.IsBrokenDown ); Assert.AreEqual( 0, f.Member.AssignedInstanceId );
	}
	[TestMethod]
	public void ArrivalObservationStartsExactReservedTimerAndReconnectResumesTravel()
	{
		var f = Fixture( connected: false ); var events = 0; f.Park.EventRaised += item => { if ( item.Kind == ParkEventKind.RideRepaired ) events++; };
		Tick( f.Park, f.World, 120 ); Assert.IsTrue( f.Member.AwaitingJobArrival ); Assert.AreEqual( 2L, f.Member.BusyTicks );
		f.Grid.SetPath( 2, 1, true ); f.World.Tick( 5 ); Assert.IsTrue( f.World.HasArrived( f.Member ) ); Assert.IsTrue( f.Member.AwaitingJobArrival );
		EconomyTestData.AdvanceToTurn( f.Park, f.Park.Turn + 1 ); Assert.IsFalse( f.Member.AwaitingJobArrival ); Assert.AreEqual( 2L, f.Member.BusyTicks ); Assert.AreEqual( 0, events );
		EconomyTestData.AdvanceToTurn( f.Park, f.Park.Turn + 1 ); Assert.AreEqual( 1L, f.Member.BusyTicks ); Assert.IsTrue( f.Ride.IsBrokenDown );
		EconomyTestData.AdvanceToTurn( f.Park, f.Park.Turn + 1 ); Assert.AreEqual( 1, events ); Assert.IsFalse( f.Ride.IsBrokenDown ); Assert.AreEqual( 0, f.Member.AssignedInstanceId );
		EconomyTestData.AdvanceToTurn( f.Park, f.Park.Turn + 4 ); Assert.AreEqual( 1, events );
	}
	[TestMethod]
	public void ArrivalPredicateRejectsForeignAndOffPathActorsWithMatchingIdentity()
	{
		var f = Fixture(); f.World.Tick( 5 ); Assert.IsTrue( f.World.HasArrived( f.Member ) );
		var foreign = new StaffMember { Id = f.Member.Id, Type = StaffType.Mechanic, Grade = f.Member.Grade, NameIndex = f.Member.NameIndex, AssignedInstanceId = f.Ride.Id, CellX = f.Member.CellX, CellY = f.Member.CellY };
		Assert.IsFalse( f.World.HasArrived( foreign ) ); f.Member.CellY = 2.5f; Assert.IsFalse( f.World.HasArrived( f.Member ) );
	}
	[TestMethod]
	public void TravelAndWorkingPhasesResumeWithActualEconomyJsonAndMovementFraction()
	{
		var f = Fixture(); f.World.Tick( 0.75f );
		var saved = ParkSaveFile.Serialize( f.Park ); var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( saved ) ), f.Park.Settings, f.Park.Catalog );
		var member = restored.Staff.Members.Single(); Assert.IsTrue( member.AwaitingJobArrival ); Assert.AreEqual( 0.75, member.TravelSeconds ); Assert.AreEqual( f.Member.CellX, member.CellX );
		Assert.IsFalse( f.World.HasArrived( member ) ); EconomyTestData.AdvanceToTurn( restored, restored.Turn + 2 ); Assert.AreEqual( 2L, member.BusyTicks, "Restored waiting does not become headless immediate work." );
		// Restart from same snapshot to compare identical fixed-step progression.
		restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( saved ) ), f.Park.Settings, f.Park.Catalog );
		var resumed = new StaffWorldSimulation( f.Grid, () => restored.Staff.Members, id => id == f.Ride.Id ? (5, 1) : null, (0, 1) ); restored.MechanicJobWorld = resumed;
		Tick( f.Park, f.World, 300 ); Tick( restored, resumed, 300 ); Assert.AreEqual( ParkSaveFile.Serialize( f.Park ), ParkSaveFile.Serialize( restored ) );
		var working = Fixture(); working.World.Tick( 5 ); EconomyTestData.AdvanceToTurn( working.Park, working.Park.Turn + 1 ); Assert.IsFalse( working.Member.AwaitingJobArrival );
		var loaded = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( working.Park ) ) ), working.Park.Settings, working.Park.Catalog );
		EconomyTestData.AdvanceToTurn( loaded, loaded.Turn + 2 ); Assert.IsFalse( loaded.Objects.Single().IsBrokenDown );
	}
	[TestMethod]
	public void MissingPhaseLegacyWorkingJobKeepsImmediateCountdownAndPhaseIsGameplayHashed()
	{
		var f = Fixture(); var waiting = WorldStateHash.Compute( new WorldStateSources { Economy = f.Park } );
		var node = JsonNode.Parse( ParkSaveFile.Serialize( f.Park ) )!; node["Staff"]!["Members"]![0]!.AsObject().Remove( "AwaitingJobArrival" );
		var legacy = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ), f.Park.Settings, f.Park.Catalog ); Assert.IsFalse( legacy.Staff.Members.Single().AwaitingJobArrival );
		Assert.IsFalse( ParkSaveFile.Serialize( legacy ).Contains( "AwaitingJobArrival" ) ); Assert.AreNotEqual( waiting, WorldStateHash.Compute( new WorldStateSources { Economy = legacy } ) );
		var loaded = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( f.Park ) ) ), f.Park.Settings, f.Park.Catalog ); Assert.AreEqual( waiting, WorldStateHash.Compute( new WorldStateSources { Economy = loaded } ) );
		legacy.MechanicJobWorld = new StaffWorldSimulation( f.Grid, () => legacy.Staff.Members, _ => (5, 1), (0, 1) ); EconomyTestData.AdvanceToTurn( legacy, legacy.Turn + 2 ); Assert.IsFalse( legacy.Objects.Single().IsBrokenDown );
	}
	[DataTestMethod]
	[DataRow( "remove" )]
	[DataRow( "sell" )]
	[DataRow( "fire" )]
	public void ExistingCancellationClearsOnlyTheMatchingWaitingPhase( string action )
	{
		var f = Fixture(); var other = f.Park.RegisterExisting( 1100 ); var otherMember = EconomyTestData.HireBest( f.Park, StaffType.Mechanic ); other.IsBrokenDown = true; other.Repair = 0; EconomyTestData.AdvanceToTurn( f.Park, f.Park.Turn + 1 );
		Assert.AreEqual( otherMember.Id, other.MechanicId ); Assert.IsTrue( otherMember.AwaitingJobArrival );
		if ( action == "remove" ) f.Park.Remove( f.Ride.Id ); else if ( action == "sell" ) f.Park.Sell( f.Ride.Id ); else f.Park.Fire( f.Member.Id );
		Assert.IsFalse( f.Member.AwaitingJobArrival ); Assert.AreEqual( otherMember.Id, other.MechanicId ); Assert.IsTrue( otherMember.AwaitingJobArrival ); Assert.AreEqual( other.Id, otherMember.AssignedInstanceId );
	}
	[TestMethod]
	public void NewPhaseIntegrityRefusesMalformedDtoAndInvalidCaptureBeforeOwnedSaveOverwrite()
	{
		var f = Fixture(); var saved = ParkSaveFile.Serialize( f.Park ); var root = Directory.CreateTempSubdirectory( "opentpw-arrival-save-" ).FullName;
		try
		{
			var path = Path.Combine( root, "park.json" ); ParkSaveFile.Save( path, f.Park ); var before = File.ReadAllBytes( path ); f.Ride.MechanicId = f.Member.Id + 100;
			Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Save( path, f.Park ) ); CollectionAssert.AreEqual( before, File.ReadAllBytes( path ) ); Assert.AreEqual( 1, Directory.GetFiles( root ).Length );
		}
		finally { Directory.Delete( root, true ); }
		foreach ( var field in new[] { "AssignedInstanceId", "BusyTurns", "Type" } )
		{
			var node = JsonNode.Parse( saved )!; var staff = node["Staff"]!["Members"]![0]!; staff[field] = field == "Type" ? (int)StaffType.Guard : 0;
			Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ) );
		}
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void WornAndActuallyPurchasedUpgradeJobsWaitThenUseExistingCompletion( bool upgrade )
	{
		var f = Fixture(); f.Park.Remove( f.Ride.Id ); var ride = f.Park.RegisterExisting( 1100 );
		if ( upgrade ) { f.Park.Research.AddResearcherPoints( 1000000 ); Assert.IsTrue( f.Park.Research.IsAvailable( 1100, 1 ) ); var balance = f.Park.Balance; Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, f.Park.TryBuyUpgrade( ride.Id ) ); Assert.AreEqual( balance, f.Park.Balance, "the request debits nothing (0x4af480)" ); Assert.IsTrue( ride.UpgradeCostDue ); }
		else ride.Repair = 20;
		var world = new StaffWorldSimulation( f.Grid, () => f.Park.Staff.Members, id => id == ride.Id ? (5, 1) : null, (0, 1) ); f.Park.MechanicJobWorld = world;
		EconomyTestData.AdvanceToTurn( f.Park, f.Park.Turn + 1 ); Assert.IsTrue( f.Member.AwaitingJobArrival ); Assert.AreEqual( ride.Id, f.Member.AssignedInstanceId );
		var remaining = f.Member.BusyTicks; EconomyTestData.AdvanceToTurn( f.Park, f.Park.Turn + 3 ); Assert.AreEqual( remaining, f.Member.BusyTicks );
		world.Tick( 5 ); EconomyTestData.AdvanceToTurn( f.Park, f.Park.Turn + 1 ); Assert.IsFalse( f.Member.AwaitingJobArrival ); Assert.AreEqual( remaining, f.Member.BusyTicks );
		var completions = 0; f.Park.EventRaised += item => { if ( item.InstanceId == ride.Id && item.Kind == (upgrade ? ParkEventKind.UpgradeCompleted : ParkEventKind.RideRepaired) ) completions++; };
		var spent = ride.TotalSpent;
		EconomyTestData.AdvanceToTurn( f.Park, f.Park.Turn + remaining ); Assert.AreEqual( 1, completions ); if ( upgrade ) Assert.AreEqual( 1, ride.Level ); else Assert.AreEqual( 100, ride.Repair );
		// The installation debits the level's cost (0x4df8f0 -> 0x4d01f0)
		Assert.AreEqual( spent + (upgrade ? 400 : 0), ride.TotalSpent ); Assert.IsFalse( ride.UpgradeCostDue );
	}
	[TestMethod]
	public void PauseAndSpeedScaleAdvanceTheSameActualClockAndActorWork()
	{
		var normal = Fixture(); var fast = Fixture(); var clockA = new FixedStepClock(); var clockB = new FixedStepClock();
		void Frame( FixedStepClock clock, ParkEconomy park, StaffWorldSimulation world, GameSpeed speed ) { park.Speed = speed; clock.Advance( GameSpeedScale.Of( speed ) / 60d, dt => { park.AdvanceFixedTick(); world.Tick( dt ); } ); }
		var initialTick = normal.Park.Tick; for ( var i = 0; i < 120; i++ ) Frame( clockA, normal.Park, normal.World, GameSpeed.Paused );
		Assert.AreEqual( initialTick, normal.Park.Tick ); Assert.AreEqual( 0.5f, normal.Member.CellX ); Assert.AreEqual( 0d, normal.Member.TravelSeconds ); Assert.AreEqual( 2L, normal.Member.BusyTicks );
		for ( var i = 0; i < 120; i++ ) Frame( clockA, normal.Park, normal.World, GameSpeed.Normal ); for ( var i = 0; i < 60; i++ ) Frame( clockB, fast.Park, fast.World, GameSpeed.Fast );
		Assert.AreEqual( normal.Park.Tick, fast.Park.Tick ); Assert.AreEqual( normal.Member.CellX, fast.Member.CellX ); Assert.AreEqual( normal.Member.TravelSeconds, fast.Member.TravelSeconds ); Assert.AreEqual( normal.Member.BusyTicks, fast.Member.BusyTicks ); Assert.IsTrue( normal.Member.AwaitingJobArrival );
	}

}
