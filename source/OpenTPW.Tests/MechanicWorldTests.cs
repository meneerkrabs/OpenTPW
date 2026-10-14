using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The mechanics' state machine in a world (tp.exe <c>0x004DA490</c>, docs/reverse/WANDER-plan.md §4): the ride finder (<c>0x004DAA90</c>), the walk to the
/// ride (<c>0x004DA740</c>) and the repair or upgrade timed from the arrival (<c>0x004DA830</c>).
/// </summary>
[TestClass]
public class MechanicWorldTests
{
	private sealed class Fixture
	{
		public readonly GuestPathGrid Grid = new( 20, 20 );
		public readonly ParkEconomy Park = EconomyTestData.Park();
		public readonly StaffWorldSimulation World;
		public readonly Dictionary<int, ((int X, int Y) Cell, (int X, int Y) Access)> Rides = new();

		public Fixture()
		{
			for ( var x = 0; x < 16; x++ )
				Grid.SetPath( x, 5, true );
			for ( var y = 0; y < 11; y++ )
				Grid.SetPath( 10, y, true );
			Park.OpenPark();
			World = new StaffWorldSimulation( Grid, () => Park.Staff.Members, id => Rides.TryGetValue( id, out var ride ) ? ride.Access : null, (0, 5) )
			{
				RideCells = id => Rides.TryGetValue( id, out var ride ) ? ride.Cell : null
			};
			Park.StaffWorld = World;
		}

		public ParkObjectState Ride( (int X, int Y) cell, (int X, int Y) access )
		{
			var ride = Park.RegisterExisting( 1100 );
			Rides[ride.Id] = (cell, access);
			return ride;
		}

		public StaffMember Hire()
		{
			var member = EconomyTestData.HireBest( Park, StaffType.Mechanic );
			World.Synchronize();
			return member;
		}

		public void Until( Func<bool> done, int turns = 100 )
		{
			for ( var turn = 0; turn < turns && !done(); turn++ )
				Turns( 1 );
		}

		public void Turns( int turns, Action? each = null )
		{
			for ( var turn = 0; turn < turns; turn++ )
			{
				EconomyTestData.AdvanceToTurn( Park, Park.Turn + 1 );
				each?.Invoke();
			}
		}
	}

	[TestMethod]
	public void AMechanicWalksToABrokenRideAndRepairsItTimedFromHisArrival()
	{
		var f = new Fixture();
		var ride = f.Ride( (12, 6), (12, 5) );
		var mechanic = f.Hire();
		f.Turns( 2 );
		ride.IsBrokenDown = true;
		ride.Repair = 40;
		long arrived = -1, repaired = -1, time = 0;
		f.Turns( 300, () =>
		{
			if ( arrived < 0 && mechanic.Job == StaffJob.Repairing )
				(arrived, time) = (f.Park.Turn, mechanic.JobStamp);
			if ( repaired < 0 && !ride.IsBrokenDown )
				repaired = f.Park.Turn;
		} );
		Assert.IsTrue( arrived > 0, "he reached the ride" );
		Assert.AreEqual( ParkEconomy.RepairTimer( f.Park.Settings[StaffType.Mechanic].WorkDuration[mechanic.Grade], 40, null ), time,
			"(100 − 40) × WorkDuration / 100 (0x004DA370)" );
		Assert.AreEqual( arrived + time + 1, repaired, "the timer counts down to 0, then the repair" );
		Assert.AreEqual( 100.0, ride.Repair, 1e-6 );
		Assert.AreEqual( 0, ride.MechanicId, "the claim is cleared" );
		Assert.AreEqual( 0, mechanic.RideId );
	}

	[TestMethod]
	public void TheNearestRideThatNeedsHimWinsAndAnUpgradeIsInstalled()
	{
		var f = new Fixture();
		var far = f.Ride( (2, 6), (2, 5) );
		var near = f.Ride( (11, 6), (11, 5) );
		var mechanic = f.Hire();
		f.Turns( 2 );
		var navigation = mechanic.Navigation!;
		(navigation.X, navigation.Y) = GuestNavigation.BytePoint( (12, 5), 128, 128 );
		navigation.SetDestination( navigation.X, navigation.Y, [] );
		mechanic.Job = StaffJob.Idle;
		mechanic.IdleStamp = 0;
		far.IsBrokenDown = true;
		near.PendingLevel = near.Level + 1;
		f.Until( () => mechanic.Job == StaffJob.ToRide );
		Assert.AreEqual( (StaffJob.ToRide, near.Id), (mechanic.Job, mechanic.RideId), "11 − 12 beats 2 − 12" );
		Assert.AreEqual( mechanic.Id, near.MechanicId );
		var level = near.Level;
		f.Turns( 600 );
		Assert.AreEqual( level + 1, near.Level, "the upgrade is installed" );
		Assert.IsFalse( far.IsBrokenDown, "then the other ride is repaired" );
	}

	[TestMethod]
	public void SellingTheRideHeWalksToSendsHimBackToSearching()
	{
		var f = new Fixture();
		var ride = f.Ride( (12, 6), (12, 5) );
		var mechanic = f.Hire();
		f.Turns( 2 );
		ride.IsBrokenDown = true;
		f.Until( () => mechanic.Job == StaffJob.ToRide );
		Assert.AreEqual( StaffJob.ToRide, mechanic.Job );
		f.Park.Sell( ride.Id );
		f.Rides.Remove( ride.Id );
		Assert.AreEqual( (0, StaffJob.Idle), (mechanic.RideId, mechanic.Job) );
		ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( f.Park ) ) ), f.Park.Settings, f.Park.Catalog );
		f.Turns( 20 );
	}

	[TestMethod]
	public void ACalledMechanicServicesAWorkingRideAndTheCallClosesItUntilThen()
	{
		var f = new Fixture();
		var ride = f.Ride( (12, 6), (12, 5) );
		var mechanic = f.Hire();
		var events = new List<ParkEventKind>();
		f.Park.EventRaised += item => events.Add( item.Kind );
		ride.Repair = 70;
		Assert.IsTrue( f.Park.RequestMaintenance( ride.Id, true ) );
		Assert.IsTrue( ride.MaintenanceRequested );
		CollectionAssert.Contains( events, ParkEventKind.MaintenanceRequested, "the level closes the ride" );
		f.Until( () => mechanic.Job == StaffJob.Repairing, 300 );
		Assert.AreEqual( ride.Id, mechanic.RideId, "a working ride with the call is a job (0x004E03D0)" );
		f.Until( () => !ride.MaintenanceRequested, 300 );
		Assert.AreEqual( 100.0, ride.Repair, 1e-6, "repaired fully" );
		CollectionAssert.Contains( events, ParkEventKind.RideRepaired, "and reopened" );
	}

	[TestMethod]
	public void CancellingTheCallReopensAndAsksNobody()
	{
		var f = new Fixture();
		var ride = f.Ride( (12, 6), (12, 5) );
		var events = new List<ParkEventKind>();
		f.Park.EventRaised += item => events.Add( item.Kind );
		f.Park.RequestMaintenance( ride.Id, true );
		f.Park.RequestMaintenance( ride.Id, true );
		f.Park.RequestMaintenance( ride.Id, false );
		CollectionAssert.AreEqual( new[] { ParkEventKind.MaintenanceRequested, ParkEventKind.MaintenanceCancelled }, events, "a second call changes nothing" );
		Assert.IsFalse( ride.MaintenanceRequested );
		f.Park.RequestMaintenance( ride.Id, true );
		var json = ParkSaveFile.Serialize( f.Park );
		Assert.IsTrue( ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), f.Park.Settings, f.Park.Catalog )
			.TryGetObject( ride.Id, out var restored ) && restored.MaintenanceRequested, "the call is saved" );
	}

	[TestMethod]
	public void DismissingAMechanicFreesHisRide()
	{
		var f = new Fixture();
		var ride = f.Ride( (12, 6), (12, 5) );
		var mechanic = f.Hire();
		f.Turns( 2 );
		ride.IsBrokenDown = true;
		f.Until( () => mechanic.Job == StaffJob.ToRide );
		Assert.AreEqual( mechanic.Id, ride.MechanicId );
		f.Park.Fire( mechanic.Id );
		Assert.AreEqual( 0, ride.MechanicId );
	}

	[TestMethod]
	public void AWalkingMechanicResumesFromASaveExactly()
	{
		var f = new Fixture();
		var ride = f.Ride( (12, 6), (12, 5) );
		var mechanic = f.Hire();
		f.Turns( 2 );
		ride.IsBrokenDown = true;
		f.Until( () => mechanic.Job == StaffJob.ToRide );
		var json = ParkSaveFile.Serialize( f.Park );
		Assert.IsTrue( json.Contains( "\"RideId\"" ), "the mechanic's ride is saved" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), f.Park.Settings, f.Park.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );
		restored.StaffWorld = new StaffWorldSimulation( f.Grid, () => restored.Staff.Members, id => f.Rides.TryGetValue( id, out var r ) ? r.Access : null, (0, 5) )
		{
			RideCells = id => f.Rides.TryGetValue( id, out var r ) ? r.Cell : null
		};
		for ( var turn = 0; turn < 120; turn++ )
		{
			f.Turns( 1 );
			EconomyTestData.AdvanceToTurn( restored, restored.Turn + 1 );
		}
		Assert.AreEqual( ParkSaveFile.Serialize( f.Park ), ParkSaveFile.Serialize( restored ) );
	}
}
