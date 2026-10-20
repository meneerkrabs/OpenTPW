using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class MechanicArrivalIndependentReviewTests
{
	private sealed record Scene( ParkEconomy Park, ParkObjectState Ride, StaffMember Member, GuestPathGrid Grid, StaffWorldSimulation World );

	private static Scene Create()
	{
		var settings = EconomyTestData.Settings( extra: string.Concat( Enumerable.Range( 0, 5 )
			.Select( grade => $"MechanicConstsPerGrade[{grade}].WorkDuration 2\n" ) ) );
		var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 11 );
		var ride = park.RegisterExisting( 1100 );
		ride.Repair = 0;
		ride.IsBrokenDown = true;
		var member = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var grid = new GuestPathGrid( 7, 2 );
		for ( var x = 0; x < 7; x++ ) grid.SetPath( x, 0, true );
		var world = new StaffWorldSimulation( grid, () => park.Staff.Members, id => id == ride.Id ? (6, 0) : null, (0, 0) );
		park.MechanicJobWorld = world;
		EconomyTestData.AdvanceToTurn( park, 1 );
		Assert.AreEqual( member.Id, ride.MechanicId );
		Assert.IsTrue( member.AwaitingJobArrival );
		Assert.AreEqual( 3L, member.BusyTicks );
		return new( park, ride, member, grid, world );
	}

	private static ParkEconomy Restore( Scene scene, string json ) => ParkSaveFile.Restore(
		ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), scene.Park.Settings, scene.Park.Catalog );

	private static void Turns( ParkEconomy park, long count ) => EconomyTestData.AdvanceToTurn( park, park.Turn + count );

	[TestMethod]
	public void AnotherWorldWithMatchingIdsAndArrivedCoordinatesCannotStartWork()
	{
		var one = Create(); var two = Create();
		Assert.AreEqual( one.Member.Id, two.Member.Id );
		Assert.AreEqual( one.Ride.Id, two.Ride.Id );
		one.Member.CellX = two.Member.CellX = 6.5f;
		Assert.IsTrue( one.World.HasArrived( one.Member ) );
		Assert.IsFalse( one.World.HasArrived( two.Member ) );
		two.Park.MechanicJobWorld = one.World;
		var otherBefore = ParkSaveFile.Serialize( one.Park );
		Turns( two.Park, 4 );
		Assert.IsTrue( two.Member.AwaitingJobArrival );
		Assert.AreEqual( 3L, two.Member.BusyTicks );
		Assert.IsTrue( two.Ride.IsBrokenDown );
		Assert.AreEqual( otherBefore, ParkSaveFile.Serialize( one.Park ) );
		two.Park.MechanicJobWorld = two.World;
		Turns( two.Park, 1 );
		Assert.IsFalse( two.Member.AwaitingJobArrival );
		Assert.AreEqual( 3L, two.Member.BusyTicks );
		Turns( two.Park, 3 );
		Assert.IsFalse( two.Ride.IsBrokenDown );
	}

	[TestMethod]
	public void RestoredEmployeeNeedsNewWorldEvenWhenSavedAtAccessCell()
	{
		var scene = Create(); scene.Member.CellX = 6.5f;
		var loaded = Restore( scene, ParkSaveFile.Serialize( scene.Park ) );
		var member = loaded.Staff.Members.Single();
		Assert.AreNotSame( scene.Member, member );
		loaded.MechanicJobWorld = scene.World;
		Turns( loaded, 2 );
		Assert.IsTrue( member.AwaitingJobArrival );
		Assert.AreEqual( 3L, member.BusyTicks );
		loaded.MechanicJobWorld = new StaffWorldSimulation( scene.Grid, () => loaded.Staff.Members, _ => (6, 0) );
		Turns( loaded, 1 );
		Assert.IsFalse( member.AwaitingJobArrival );
		Assert.AreEqual( 3L, member.BusyTicks );
	}

	[TestMethod]
	public void OlderBusyTicksUnitsRemainStartedWhenNewWorldIsAttached()
	{
		var scene = Create();
		var node = JsonNode.Parse( ParkSaveFile.Serialize( scene.Park ) )!;
		var staff = node["Staff"]!["Members"]![0]!.AsObject();
		staff.Remove( "AwaitingJobArrival" ); staff.Remove( "BusyTurns" ); staff["BusyTicks"] = 25;
		var loaded = Restore( scene, node.ToJsonString() );
		var member = loaded.Staff.Members.Single();
		Assert.AreEqual( 3L, member.BusyTicks, "The existing tenths-of-hour migration rounds 25 to three park turns." );
		Assert.IsFalse( member.AwaitingJobArrival );
		loaded.MechanicJobWorld = new StaffWorldSimulation( scene.Grid, () => loaded.Staff.Members, _ => (6, 0) );
		Assert.IsFalse( loaded.MechanicJobWorld.HasArrived( member ) );
		Turns( loaded, 3 );
		Assert.IsFalse( loaded.Objects.Single().IsBrokenDown );
		Assert.AreEqual( 0.5f, member.CellX, "Migration does not invent travel before old started work can finish." );
		Assert.IsFalse( ParkSaveFile.Serialize( loaded ).Contains( "AwaitingJobArrival" ) );
	}

	[TestMethod]
	public void FractionalLifeGaugeUsesExistingFloatByteEligibilityOnCaptureAndLoad()
	{
		var scene = Create(); scene.Ride.LifeGauge = 0.99999999;
		Assert.AreEqual( 1, RideBreakdownRules.GaugeByte( (float)scene.Ride.LifeGauge ) );
		var json = ParkSaveFile.Serialize( scene.Park );
		var loaded = Restore( scene, json );
		Assert.IsTrue( loaded.Staff.Members.Single().AwaitingJobArrival );
		Assert.AreEqual( scene.Ride.LifeGauge, loaded.Objects.Single().LifeGauge );
		Assert.AreEqual( WorldStateHash.Compute( new WorldStateSources { Economy = scene.Park } ),
			WorldStateHash.Compute( new WorldStateSources { Economy = loaded } ) );
	}

	[DataTestMethod]
	[DataRow( StaffState.PickedUp, true, StaffState.PickedUp )]
	[DataRow( StaffState.OnStrike, false, StaffState.Patrolling )]
	[DataRow( StaffState.Resting, false, StaffState.Patrolling )]
	public void ExistingStaffStateTransitionsControlArrivalObservationWithoutChangingWork( StaffState state, bool awaiting, StaffState after )
	{
		var scene = Create(); scene.Member.CellX = 6.5f; scene.Member.State = state;
		Assert.IsFalse( scene.World.HasArrived( scene.Member ) );
		Turns( scene.Park, 1 );
		// The existing vitals update clears an inactive strike and rest without a room before arrival is observed.
		Assert.AreEqual( after, scene.Member.State );
		Assert.AreEqual( awaiting, scene.Member.AwaitingJobArrival );
		Assert.AreEqual( 3L, scene.Member.BusyTicks );
		Assert.AreEqual( scene.Member.Id, scene.Ride.MechanicId );
		Assert.AreEqual( awaiting, Restore( scene, ParkSaveFile.Serialize( scene.Park ) ).Staff.Members.Single().AwaitingJobArrival );
	}

	[TestMethod]
	public void AlreadyStartedJobDoesNotAcquireANewTravelGateWhenPathDisappears()
	{
		var scene = Create(); scene.World.Tick( 6 ); Turns( scene.Park, 1 );
		Assert.IsFalse( scene.Member.AwaitingJobArrival );
		scene.Grid.SetPath( 6, 0, false );
		Assert.IsFalse( scene.World.HasArrived( scene.Member ) );
		Turns( scene.Park, 3 );
		Assert.IsFalse( scene.Ride.IsBrokenDown );
		Assert.AreEqual( 0L, scene.Member.BusyTicks );
	}

	[TestMethod]
	public void InvalidPendingStateRefusesBeforePreviousFileAndLiveOwnershipChange()
	{
		var scene = Create();
		var directory = Directory.CreateTempSubdirectory( "opentpw-arrival-independent-" ).FullName;
		try
		{
			var path = Path.Combine( directory, "park.json" ); ParkSaveFile.Save( path, scene.Park );
			var previous = File.ReadAllBytes( path ); var balance = scene.Park.Balance;
			scene.Member.State = (StaffState)int.MaxValue;
			Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Save( path, scene.Park ) );
			CollectionAssert.AreEqual( previous, File.ReadAllBytes( path ) );
			Assert.AreEqual( 1, Directory.GetFiles( directory ).Length );
			Assert.AreEqual( balance, scene.Park.Balance );
			Assert.AreEqual( scene.Member.Id, scene.Ride.MechanicId );
			Assert.AreEqual( 3L, scene.Member.BusyTicks );
			Assert.IsTrue( scene.Member.AwaitingJobArrival );
		}
		finally { Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public void NewPhaseRoleRefusalUsesValidEnumDataRatherThanAParseFailure()
	{
		var scene = Create(); var node = JsonNode.Parse( ParkSaveFile.Serialize( scene.Park ) )!;
		var staff = node["Staff"]!["Members"]![0]!.AsObject(); staff["Type"] = "Guard";
		// Keep presentation membership valid so the refusal below specifically exercises arrival job ownership.
		var roleOrder = node["Staff"]!["RoleOrder"]!.AsArray();
		roleOrder[(int)StaffType.Mechanic]!.AsArray().Clear();
		roleOrder[(int)StaffType.Guard]!.AsArray().Add( scene.Member.Id );
		var error = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ) );
		StringAssert.Contains( error.Message, "awaiting arrival job ownership" );
		// The additional rule belongs to the new true phase; generic legacy staff parsing is unchanged.
		staff.Remove( "AwaitingJobArrival" );
		Assert.AreEqual( StaffType.Guard, ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ).Staff.Members.Single().Type );
	}

	[TestMethod]
	public void RealLevelStaffBindingAndFixedStepKeepMissingObjectAccessPending()
	{
		var scene = Create(); scene.Park.MechanicJobWorld = null; scene.Member.AwaitingJobArrival = false;
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		void Set( string field, object value ) => typeof( Level ).GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( level, value );
		Set( "<Park>k__BackingField", ParkEconomyRuntime.ForResume( scene.Park, ParkStartKind.FullSimulation ) );
		Set( "<Guests>k__BackingField", new GuestSimulation( scene.Grid, new GuestSettings(), 1 ) );
		Set( "<Objects>k__BackingField", new ParkObjects( null!, new SandboxParkGrid() ) );
		Set( "economyInstances", new Dictionary<OriginalObject, int>() );
		Set( "simulationClock", new FixedStepClock() );
		typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, null );
		Assert.AreSame( level.StaffWorld, scene.Park.MechanicJobWorld );
		// The synthetic level intentionally has no placed object/access mapping. Dispatch a fresh job through its real fixed-step route.
		scene.Park.Remove( scene.Ride.Id ); var ride = scene.Park.RegisterExisting( 1100 ); ride.IsBrokenDown = true; ride.Repair = 0;
		for ( var frame = 0; frame < 120; frame++ ) level.AdvanceSimulation( FixedStepClock.TickDuration );
		// In a level the mechanics run their own state machine (tp.exe 0x004DA490): without an access cell no route reaches the ride, so nobody
		// claims it and the job stays pending (the ride stays broken).
		Assert.AreSame( level.StaffWorld, scene.Park.StaffWorld );
		Assert.AreEqual( 0, ride.MechanicId );
		Assert.AreEqual( 0, scene.Member.RideId );
		Assert.AreEqual( 0L, scene.Member.BusyTicks );
		Assert.IsTrue( ride.IsBrokenDown );
	}
}
