using System;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class StaffWorldTests
{
	private static GuestPathGrid Paths()
	{
		var grid = new GuestPathGrid( 5, 3 );
		for ( var x = 0; x < 5; x++ ) grid.SetPath( x, 1, true );
		return grid;
	}

	[TestMethod]
	public void ActorsSpawnAtGateAndFollowConnectedPathWithoutChangingTheirJob()
	{
		var park = EconomyTestData.Park();
		var member = EconomyTestData.HireBest( park, StaffType.Mechanic );
		member.AssignedInstanceId = 100;
		member.State = StaffState.Working;
		member.BusyTicks = 500;
		var world = new StaffWorldSimulation( Paths(), () => park.Staff.Members, id => id == 100 ? (4, 1) : null, (0, 1) );
		Assert.AreEqual( (0.5f, 1.5f), (member.CellX!.Value, member.CellY!.Value) );
		world.Tick( 0 );
		Assert.AreEqual( 0.5f, member.CellX.Value, "pause does not move an actor" );
		world.Tick( 0.5f );
		Assert.AreEqual( 0.5f, member.CellX.Value );
		world.Tick( 0.5f );
		Assert.AreEqual( 1.5f, member.CellX.Value );
		world.Tick( 3 );
		Assert.AreEqual( 4.5f, member.CellX.Value );
		Assert.AreEqual( (100, 500L, StaffState.Working), (member.AssignedInstanceId, member.BusyTicks, member.State) );
		Assert.IsFalse( world.Snapshots.Single().Travelling );
	}

	[TestMethod]
	public void UnreachableJobsAndChangedPathsDoNotTeleportActors()
	{
		var park = EconomyTestData.Park();
		var member = EconomyTestData.HireBest( park, StaffType.Mechanic );
		member.AssignedInstanceId = 100;
		var grid = Paths();
		var world = new StaffWorldSimulation( grid, () => park.Staff.Members, _ => (4, 1), (0, 1) );
		grid.SetPath( 2, 1, false );
		world.Tick( 10 );
		Assert.AreEqual( 0.5f, member.CellX!.Value );
		Assert.IsFalse( world.Snapshots.Single().JobReachable );
		grid.SetPath( 2, 1, true );
		world.Tick( 1 );
		Assert.AreEqual( 1.5f, member.CellX.Value );
		member.CellX = 2.125f;
		member.CellY = 2.25f;
		world.Tick( 10 );
		Assert.AreEqual( (2.125f, 2.25f), (member.CellX.Value, member.CellY!.Value), "native off-path positions remain exact" );
	}

	[TestMethod]
	public void HireFireAndLoadUseActualMemberIdentityAndPersistMovementPhase()
	{
		var park = EconomyTestData.Park();
		var grid = Paths();
		var world = new StaffWorldSimulation( grid, () => park.Staff.Members, _ => null );
		var employee = EconomyTestData.HireBest( park, StaffType.Guard );
		world.Synchronize();
		employee.CellX = 3.125f;
		employee.CellY = 1.25f;
		employee.TravelSeconds = 0.75;
		var bytes = Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) );
		park = ParkSaveFile.Restore( ParkSaveFile.Deserialize( bytes ), park.Settings, park.Catalog );
		world.Synchronize();
		var loaded = park.Staff.Members.Single();
		Assert.AreNotSame( employee, loaded );
		Assert.AreEqual( (employee.Id, 3.125f, 1.25f, 0.75), (loaded.Id, loaded.CellX!.Value, loaded.CellY!.Value, loaded.TravelSeconds) );
		Assert.AreEqual( loaded.Id, world.Snapshots.Single().Id );
		park.Fire( loaded.Id );
		world.Synchronize();
		Assert.AreEqual( 0, world.Snapshots.Count );
		var replacement = EconomyTestData.HireBest( park, StaffType.Guard );
		world.Synchronize();
		Assert.AreNotEqual( loaded.Id, replacement.Id );
		Assert.AreEqual( 0.5f, world.Snapshots.Single().CellX );
	}

	[TestMethod]
	public void NoPathMeansNoActorAndHeadlessStaffHaveNoDefaultSpatialSaveFields()
	{
		var park = EconomyTestData.Park();
		var member = EconomyTestData.HireBest( park, StaffType.Guard );
		Assert.IsFalse( ParkSaveFile.Serialize( park ).Contains( "CellX" ) );
		Assert.IsFalse( ParkSaveFile.Serialize( park ).Contains( "TravelSeconds" ) );
		var grid = new GuestPathGrid( 2, 2 );
		var world = new StaffWorldSimulation( grid, () => park.Staff.Members, _ => null );
		world.Tick( 1 );
		Assert.IsNull( member.CellX );
		Assert.AreEqual( 0, world.Snapshots.Count );
		grid.SetPath( 1, 1, true );
		world.Synchronize();
		Assert.AreEqual( (1.5f, 1.5f), (member.CellX!.Value, member.CellY!.Value) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => world.Tick( float.NaN ) );
	}
}
