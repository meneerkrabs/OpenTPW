using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Hiring places, pick-up and drop of staff (tp.exe 0x507bf0, 0x505c50, 0x509af0, 0x46c8e0, 0x46cc40; docs/STAFF-PLACEMENT.md): the hire employs
/// nobody until a valid click, a drop puts the member on the cell centre as an idle one without jobs, an invalid cell does nothing.
/// </summary>
[TestClass]
public class StaffPlacementTests
{
	[TestMethod]
	public void ARidesFootprintIsRefusedByItsCellTypesAndItsEntranceTakesADrop()
	{
		// tp.exe 0x46CC40 reads the cell's type (0, 1, 3 or 9) and never an object list: the placement's types 4 and 10 refuse, the entrance's 9 does not (GUEST-013)
		var grid = new GuestPathGrid( 24, 24 );
		var shape = new ObjectShape( new[] { "*2S" } );
		var footprint = ObjectFootprint.GetCells( shape, 4, 8, 0 ).ToList();
		grid.Cells.WriteFootprint( footprint, ParkCellMap.CellWord( 4, 8 ), 0 );
		var types = footprint.Select( cell => grid.Cells.RawTypeAt( cell.X, cell.Y ) ).ToArray();
		CollectionAssert.AreEquivalent( new byte[] { 4, 9, 10 }, types, "body, entrance and exit" );
		foreach ( var (x, y, _) in footprint )
			Assert.AreEqual( grid.Cells.RawTypeAt( x, y ) == 9, StaffPlacementRules.IsValidDropCell( grid.Cells, x, y, null ), $"({x}, {y}) of type {grid.Cells.RawTypeAt( x, y )}" );
	}

	private sealed class Fixture
	{
		public readonly GuestPathGrid Grid = new( 20, 20 );
		public readonly ParkEconomy Park = EconomyTestData.Park();
		public readonly StaffWorldSimulation World;
		public readonly StaffPlacement Placement;
		public readonly HashSet<(int X, int Y)> Occupied = new();

		public Fixture()
		{
			for ( var x = 0; x < 16; x++ )
				Grid.SetPath( x, 5, true );
			Park.OpenPark();
			Park.Litter.Cells = Grid.Cells;
			World = new StaffWorldSimulation( Grid, () => Park.Staff.Members, _ => null, (0, 5) );
			Park.StaffWorld = World;
			Placement = StaffPlacementRig.Create( () => Park, () => Grid.Cells, ( x, y ) => Occupied.Contains( (x, y) ) );
		}

		public StaffMember Hire( StaffType type )
		{
			var member = EconomyTestData.HireBest( Park, type );
			World.Synchronize();
			return member;
		}

		public static (int X, int Y) Cell( StaffMember member ) => ((int)member.CellX!.Value, (int)member.CellY!.Value);
	}

	[TestMethod]
	public void AHireEmploysNobodyAndTheWorldDoesNotPlaceAMemberInHand()
	{
		var f = new Fixture();
		var candidate = f.Park.Staff.Candidates.First( item => item.Type == StaffType.Handyman );
		var employed = f.Park.Staff.Members.Count;
		Assert.IsTrue( f.Placement.BeginHire( candidate.Id ) );
		Assert.AreEqual( StaffToolKind.Place, f.Placement.Kind );
		Assert.AreEqual( employed, f.Park.Staff.Members.Count, "the hire creates no member" );
		Assert.IsTrue( f.Park.Staff.Candidates.Any( item => item.Id == candidate.Id ), "the candidate stays in the pool until the click" );
		Assert.AreEqual( candidate.Id, f.Park.Staff.PendingPlacementId );
		f.World.Synchronize();
		Assert.AreEqual( employed, f.Park.Staff.Members.Count );
		Assert.IsTrue( f.World.Snapshots.All( snapshot => snapshot.Id != candidate.Id ), "no positioned actor" );

		// the economy-side stand-in (a member in hand without a position) is not given the gate spawn either; an ordinary unpositioned one still is
		var held = f.Hire( StaffType.Handyman );
		(held.CellX, held.CellY, held.State) = (null, null, StaffState.PickedUp);
		var ordinary = f.Hire( StaffType.Mechanic );
		(ordinary.CellX, ordinary.CellY) = (null, null);
		f.World.Synchronize();
		Assert.IsNull( held.CellX, "Synchronize skips a member in hand without a position" );
		Assert.IsNotNull( ordinary.CellX, "other unpositioned members still spawn at the gate" );
	}

	[TestMethod]
	public void AValidClickHiresAndPlacesOnTheCellCentreWithTheOriginalDefaults()
	{
		var f = new Fixture();
		var candidate = f.Park.Staff.Candidates.First( item => item.Type == StaffType.Researcher );
		var employed = f.Park.Staff.Members.Count;
		f.Placement.BeginHire( candidate.Id );
		var toolVariant = f.Placement.ToolVariantKey;
		f.Placement.Hover( 7, 9 );
		Assert.IsTrue( f.Placement.HoverValid );
		Assert.AreEqual( StaffClickResult.Placed, f.Placement.Click( 7, 9 ) );
		var member = f.Park.Staff.Members.Single( item => item.Id == candidate.Id );
		Assert.AreEqual( employed + 1, f.Park.Staff.Members.Count );
		Assert.AreEqual( (7.5f, 9.5f), (member.CellX, member.CellY) );
		Assert.AreEqual( toolVariant, StaffSpriteRules.KeyOf( member ), "the member keeps the sprite bank the cursor sprite showed" );
		Assert.AreEqual( StaffState.Idle, member.State );
		Assert.AreEqual( (0, 0, ParkLitter.MapSize - 1, ParkLitter.MapSize - 1), member.PatrolArea, "the patrol rectangle is the whole map" );
		Assert.AreEqual( (100f, 100f), (member.Happiness, member.Energy) );
		Assert.IsNull( member.Navigation );
		Assert.IsFalse( f.Placement.IsActive );
		Assert.AreEqual( 0, f.Park.Staff.PendingPlacementId );
		Assert.IsTrue( f.Park.Staff.Candidates.All( item => item.Id != candidate.Id ), "the hired candidate left the pool" );
	}

	[TestMethod]
	public void CancellingThePlaceToolHiresNobody()
	{
		var f = new Fixture();
		var candidate = f.Park.Staff.Candidates.First( item => item.Type == StaffType.Guard );
		var employed = f.Park.Staff.Members.Count;
		var ended = 0;
		f.Placement.Ended += () => ended++;
		f.Placement.BeginHire( candidate.Id );
		f.Placement.Cancel();
		Assert.IsFalse( f.Placement.IsActive );
		Assert.AreEqual( 1, ended );
		Assert.AreEqual( employed, f.Park.Staff.Members.Count );
		Assert.IsTrue( f.Park.Staff.Candidates.Any( item => item.Id == candidate.Id ), "the candidate stays in the pool" );
		Assert.AreEqual( 0, f.Park.Staff.PendingPlacementId );
	}

	[TestMethod]
	public void APendingCandidateDoesNotExpireWhileItIsBeingPlaced()
	{
		var f = new Fixture();
		var candidate = f.Park.Staff.Candidates.First();
		f.Placement.BeginHire( candidate.Id );
		EconomyTestData.AdvanceToTurn( f.Park, f.Park.Turn + 5 );
		f.Park.Staff.UpdatePool( f.Park.World, candidate.ExpiresTick + 1 );
		Assert.IsTrue( f.Park.Staff.Candidates.Any( item => item.Id == candidate.Id ), "the pending candidate survives its expiry" );
		f.Placement.Cancel();
		f.Park.Staff.UpdatePool( f.Park.World, candidate.ExpiresTick + 1 + f.Park.Staff.UpdateInterval );
		Assert.IsTrue( f.Park.Staff.Candidates.All( item => item.Id != candidate.Id ), "after the cancel it expires as any other" );
	}

	[DataTestMethod]
	[DataRow( (byte)0, "grass" )]
	[DataRow( (byte)1, "path" )]
	[DataRow( (byte)3, "queue" )]
	[DataRow( (byte)9, "queue end" )]
	public void ADropOnEachValidCellTypeSetsTheCentreAndClearsTheJobFields( byte type, string name )
	{
		var f = new Fixture();
		var member = f.Hire( StaffType.Handyman );
		member.PatrolArea = (2, 3, 8, 9);
		member.Job = StaffJob.ToLitter;
		member.LitterCell = (4, 5);
		member.ToiletId = 11;
		member.IdleStamp = 77;
		member.Navigation = new GuestNavigation { X = 5 << 16, Y = 5 << 16 };
		f.Park.Litter.Drop( 4, 5, (LitterType)1 );
		f.Park.Litter.Claim( 4, 5, member.Id, 1 );
		f.Grid.Cells.SetType( 12, 8, type );
		Assert.IsTrue( f.Placement.PickUp( member ), name );
		Assert.AreEqual( StaffState.PickedUp, member.State );
		Assert.AreEqual( 0, f.Park.Litter.CollectorAt( 4, 5 ), "the litter claim goes back to the others" );
		Assert.AreEqual( StaffClickResult.Dropped, f.Placement.Click( 12, 8 ), name );
		Assert.AreEqual( (12.5f, 8.5f), (member.CellX, member.CellY), name );
		Assert.AreEqual( StaffState.Idle, member.State );
		Assert.IsNull( member.Navigation );
		Assert.IsNull( member.LitterCell );
		Assert.AreEqual( 0, member.ToiletId );
		Assert.AreEqual( 0L, member.IdleStamp );
		Assert.AreEqual( StaffJob.Idle, member.Job );
		Assert.AreEqual( (2, 3, 8, 9), member.PatrolArea, "a drop never touches the patrol rectangle" );
		Assert.IsFalse( f.Placement.IsActive );
	}

	[TestMethod]
	public void InvalidCellsAreRefusedAndTheToolStays()
	{
		var f = new Fixture();
		var member = f.Hire( StaffType.Handyman );
		var start = Fixture.Cell( member );
		f.Grid.Cells.SetType( 3, 2, 4 );
		f.Grid.Cells.SetType( 4, 2, 10 );
		f.Grid.Cells.SetType( 5, 2, 7 );
		f.Grid.Cells.SetFlags( 6, 2, ParkCellFlags.Unowned );
		f.Occupied.Add( (7, 2) );
		Assert.IsTrue( f.Placement.PickUp( member ) );
		foreach ( var (x, y) in new[] { (3, 2), (4, 2), (5, 2), (6, 2), (7, 2), (-1, 0), (0, 20), (25, 3) } )
		{
			f.Placement.Hover( x, y );
			Assert.IsFalse( f.Placement.HoverValid, $"({x}, {y}) is not valid" );
			Assert.AreEqual( StaffClickResult.None, f.Placement.Click( x, y ), $"({x}, {y})" );
			Assert.IsTrue( f.Placement.IsActive, "the tool stays" );
			Assert.AreEqual( StaffState.PickedUp, member.State, "the state is unchanged" );
			Assert.AreEqual( start, Fixture.Cell( member ) );
		}
		// the Place tool refuses the same cells and hires nobody
		f.Placement.Cancel();
		var employed = f.Park.Staff.Members.Count;
		Assert.IsTrue( f.Placement.BeginHire( f.Park.Staff.Candidates.First( item => item.Type == StaffType.Mechanic ).Id ) );
		Assert.AreEqual( StaffClickResult.None, f.Placement.Click( 3, 2 ) );
		Assert.AreEqual( StaffToolKind.Place, f.Placement.Kind );
		Assert.AreEqual( employed, f.Park.Staff.Members.Count );
	}

	[TestMethod]
	public void CancellingTheCarryToolPutsTheMemberBackOnItsOldCell()
	{
		var f = new Fixture();
		var member = f.Hire( StaffType.Entertainer );
		(member.CellX, member.CellY) = (4.2f, 5.9f);
		member.ToiletId = 3;
		member.OffGridLeg = [(1, 1, 0x80, 0x80)];
		Assert.IsTrue( f.Placement.PickUp( member ) );
		Assert.AreEqual( (4, 5), f.Placement.Origin );
		f.Placement.Hover( 9, 9 );
		f.Placement.Cancel();
		Assert.IsFalse( f.Placement.IsActive );
		Assert.AreEqual( (4.5f, 5.5f), (member.CellX, member.CellY) );
		Assert.AreEqual( StaffState.Idle, member.State );
		Assert.AreEqual( 0, member.ToiletId );
		Assert.AreEqual( 0, member.OffGridLeg.Count, "the road leg outside the park is cleared" );
	}

	[TestMethod]
	public void ANewPickUpFirstDropsThePreviousMemberBack()
	{
		var f = new Fixture();
		var first = f.Hire( StaffType.Handyman );
		var second = f.Hire( StaffType.Mechanic );
		(second.CellX, second.CellY) = (9.5f, 5.5f);
		Assert.IsTrue( f.Placement.PickUp( first ) );
		Assert.IsTrue( f.Placement.PickUp( second ) );
		Assert.AreEqual( StaffState.Idle, first.State, "the first member went back" );
		Assert.AreEqual( (0, 5), Fixture.Cell( first ) );
		Assert.AreSame( second, f.Placement.Carried, "only one carry at a time" );
		Assert.AreEqual( StaffState.PickedUp, second.State );
	}

	[TestMethod]
	public void PickUpIsRefusedWhileStrikingCarriedEscortingOrOnAnUnsuitableCell()
	{
		var f = new Fixture();
		var member = f.Hire( StaffType.Guard );
		member.State = StaffState.OnStrike;
		Assert.IsFalse( f.Placement.PickUp( member ), "on strike (states 4, 5)" );
		member.State = StaffState.Idle;
		member.Job = StaffJob.Escorting;
		Assert.IsFalse( f.Placement.PickUp( member ), "escorting (state 18)" );
		member.Job = StaffJob.Idle;
		var handyman = f.Hire( StaffType.Handyman );
		handyman.Job = StaffJob.Striking;
		Assert.IsFalse( StaffPlacementRules.CanBePickedUp( handyman, f.Grid.Cells ), "a handyman's own striking state" );
		handyman.Job = StaffJob.ToStrike;
		Assert.IsFalse( StaffPlacementRules.CanBePickedUp( handyman, f.Grid.Cells ), "walking to the strike" );
		handyman.Job = StaffJob.Idle;
		Assert.IsTrue( f.Placement.PickUp( member ) );
		Assert.IsFalse( StaffPlacementRules.CanBePickedUp( member, f.Grid.Cells ), "already in hand (state 7)" );
		f.Placement.Cancel();
		var other = f.Hire( StaffType.Mechanic );
		(other.CellX, other.CellY) = (3.5f, 2.5f);
		foreach ( var (type, allowed) in new[] { ((byte)0, true), ((byte)1, true), ((byte)3, true), ((byte)9, true), ((byte)10, true), ((byte)4, false), ((byte)2, false), ((byte)7, false) } )
		{
			f.Grid.Cells.SetType( 3, 2, type );
			Assert.AreEqual( allowed, StaffPlacementRules.CanBePickedUp( other, f.Grid.Cells ), $"own cell of type {type}" );
		}
		// tp.exe 0x505bd0 refuses state 13, the mechanic's repair (0x4da370(0xd), the timer +0x214)
		f.Grid.Cells.SetType( 3, 2, 1 );
		other.Job = StaffJob.Repairing;
		Assert.IsFalse( StaffPlacementRules.CanBePickedUp( other, f.Grid.Cells ), "repairing (state 13)" );
		other.Job = StaffJob.Idle;
		Assert.IsTrue( StaffPlacementRules.CanBePickedUp( other, f.Grid.Cells ) );
		var unplaced = f.Hire( StaffType.Mechanic );
		(unplaced.CellX, unplaced.CellY) = (null, null);
		Assert.IsFalse( StaffPlacementRules.CanBePickedUp( unplaced, f.Grid.Cells ), "no position, no cell" );
	}

	[TestMethod]
	public void ThePatrolRectangleSurvivesAPickUpAndDropOfEveryRole()
	{
		var f = new Fixture();
		foreach ( StaffType type in Enum.GetValues<StaffType>() )
		{
			var member = f.Hire( type );
			member.PatrolArea = (1, 2, 3, 4);
			Assert.IsTrue( f.Placement.PickUp( member ), type.ToString() );
			Assert.AreEqual( StaffClickResult.Dropped, f.Placement.Click( 10, 10 ) );
			Assert.AreEqual( (1, 2, 3, 4), member.PatrolArea, type.ToString() );
		}
	}

	[TestMethod]
	public void AMechanicDroppedMidJobLosesItsRideAndTheDispatchRunsAgain()
	{
		var extra = string.Concat( Enumerable.Range( 0, 5 ).Select( grade => $"MechanicConstsPerGrade[{grade}].WorkDuration 40\n" ) );
		var park = new ParkEconomy( EconomyTestData.Settings( extra: extra ), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
		var ride = park.RegisterExisting( 1100 );
		ride.Repair = 0;
		ride.IsBrokenDown = true;
		var grid = new GuestPathGrid( 8, 3 );
		for ( var x = 0; x < 8; x++ )
			grid.SetPath( x, 1, true );
		var world = new StaffWorldSimulation( grid, () => park.Staff.Members, id => id == ride.Id ? (7, 1) : null, (0, 1) );
		park.MechanicJobWorld = world;
		var placement = StaffPlacementRig.Create( () => park, () => grid.Cells, ( _, _ ) => false );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		world.Synchronize();
		EconomyTestData.AdvanceToTurn( park, 1 );
		Assert.AreEqual( mechanic.Id, ride.MechanicId );
		Assert.IsTrue( mechanic.BusyTicks > 0 );
		Assert.IsTrue( placement.PickUp( mechanic ) );
		Assert.AreEqual( StaffClickResult.Dropped, placement.Click( 3, 1 ) );
		Assert.AreEqual( 0, ride.MechanicId, "the ride is free again" );
		Assert.AreEqual( (0, 0L, false), (mechanic.AssignedInstanceId, mechanic.BusyTicks, mechanic.AwaitingJobArrival) );
		Assert.AreEqual( (3.5f, 1.5f), (mechanic.CellX, mechanic.CellY) );
		EconomyTestData.AdvanceToTurn( park, park.Turn + 1 );
		Assert.AreEqual( mechanic.Id, ride.MechanicId, "the dispatch gave it the job again" );
	}

	[TestMethod]
	public void ACarriedMemberIsSavedDroppedOnItsOldCellAndAPendingHireIsNotSaved()
	{
		var f = new Fixture();
		var member = f.Hire( StaffType.Handyman );
		f.Park.Litter.Drop( 2, 5, (LitterType)1 );
		for ( var turn = 0; turn < 3; turn++ )
			EconomyTestData.AdvanceToTurn( f.Park, f.Park.Turn + 1 );
		var origin = Fixture.Cell( member );
		Assert.IsTrue( f.Placement.PickUp( member ) );
		f.Placement.PrepareForSave();
		Assert.IsFalse( f.Placement.IsActive );
		var json = ParkSaveFile.Serialize( f.Park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), f.Park.Settings, f.Park.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );
		Assert.IsTrue( restored.Staff.Members.All( item => item.State != StaffState.PickedUp ), "nobody is saved in hand" );
		Assert.AreEqual( StaffState.Idle, member.State );
		var actors = f.World.CaptureSnapshot().Actors.Single( actor => actor.Id == member.Id );
		Assert.AreEqual( (origin.X + 0.5f, origin.Y + 0.5f), (actors.CellX, actors.CellY ) );

		var candidate = f.Park.Staff.Candidates.First( item => item.Type == StaffType.Mechanic );
		f.Placement.BeginHire( candidate.Id );
		f.Placement.PrepareForSave();
		var afterHire = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( f.Park ) ) ), f.Park.Settings, f.Park.Catalog );
		Assert.IsTrue( afterHire.Staff.Candidates.Any( item => item.Id == candidate.Id ), "the candidate stays in the pool" );
		Assert.IsTrue( afterHire.Staff.Members.All( item => item.Id != candidate.Id ) );
	}

	[TestMethod]
	public void AMemberLeftInHandByAnImportedSaveIsPutBackToIdle()
	{
		var f = new Fixture();
		var member = f.Hire( StaffType.Mechanic );
		member.State = StaffState.PickedUp;
		f.Placement.Reconcile();
		Assert.AreEqual( StaffState.Idle, member.State );
		Assert.IsTrue( f.Placement.PickUp( member ) );
		f.Placement.Reconcile();
		Assert.AreEqual( StaffState.PickedUp, member.State, "the member the tool carries stays in hand" );
	}

	[TestMethod]
	public void AMechanicDroppedOffThePathNetworkIsNotDispatchedAndHoldsNoRide()
	{
		var extra = string.Concat( Enumerable.Range( 0, 5 ).Select( grade => $"MechanicConstsPerGrade[{grade}].WorkDuration 40\n" ) );
		foreach ( var (cellType, onPath) in new[] { ((byte)0, false), ((byte)3, false), ((byte)1, true) } )
		{
			var park = new ParkEconomy( EconomyTestData.Settings( extra: extra ), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );
			var grid = new GuestPathGrid( 8, 4 );
			for ( var x = 0; x < 8; x++ )
				grid.SetPath( x, 1, true );
			grid.Cells.SetType( 3, 3, cellType );
			var ride = park.RegisterExisting( 1100 );
			var world = new StaffWorldSimulation( grid, () => park.Staff.Members, id => id == ride.Id ? (7, 1) : null, (0, 1) );
			park.MechanicJobWorld = world;
			var placement = StaffPlacementRig.Create( () => park, () => grid.Cells, ( _, _ ) => false );
			var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
			world.Synchronize();
			Assert.IsTrue( placement.PickUp( mechanic ) );
			Assert.AreEqual( StaffClickResult.Dropped, placement.Click( 3, 3 ), $"type {cellType} is a valid drop cell" );
			ride.Repair = 0;
			ride.IsBrokenDown = true;
			EconomyTestData.AdvanceToTurn( park, park.Turn + 2 );
			Assert.AreEqual( onPath ? mechanic.Id : 0, ride.MechanicId, onPath ? "a path cell dispatches" : $"type {cellType}: no reservation that could never arrive" );
			Assert.AreEqual( onPath ? ride.Id : 0, mechanic.AssignedInstanceId );
		}
	}
}
