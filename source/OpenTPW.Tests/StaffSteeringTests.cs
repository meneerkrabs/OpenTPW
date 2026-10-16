using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Staff walk the guests' walk (tp.exe 0x004FA2A0 → 0x0050F3B0): the original's route search and steering step with avoid_walls, follow_path and
/// separation over the guests and staff around, and the walk mask +0x188 the strike and the guard's second booth route raise.
/// </summary>
[TestClass]
public class StaffSteeringTests
{
	private sealed class Fixture
	{
		public readonly GuestPathGrid Grid = new( 20, 20 );
		public readonly ParkEconomy Park = EconomyTestData.Park();
		public readonly GuestSimulation Guests;
		public readonly StaffWorldSimulation World;

		public Fixture()
		{
			for ( var x = 0; x < 16; x++ )
				Grid.SetPath( x, 5, true );
			for ( var y = 0; y < 11; y++ )
				Grid.SetPath( 10, y, true );
			Park.OpenPark();
			Park.Litter.Cells = Grid.Cells;
			Guests = new GuestSimulation( Grid, GuestTests.CreateSettings(), 5 ) { ArrivalsEnabled = false, Litter = Park.Litter };
			World = new StaffWorldSimulation( Grid, () => Park.Staff.Members, _ => null, (0, 5) ) { Guests = Guests };
			Park.StaffWorld = World;
		}

		public StaffMember Hire( StaffType type )
		{
			var member = EconomyTestData.HireBest( Park, type );
			World.Synchronize();
			return member;
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
	public void APatrollingHandymanWalksTheOriginalWalkOnThePaths()
	{
		var f = new Fixture();
		var handyman = f.Hire( StaffType.Handyman );
		var walked = false;
		var cells = new HashSet<(int, int)>();
		f.Turns( 400, () =>
		{
			walked |= handyman.Job == StaffJob.Patrol && handyman.Navigation!.Original;
			cells.Add( handyman.Navigation!.Cell );
		} );
		Assert.IsTrue( walked, "a patrol is a walk of the original kind (Navigate)" );
		Assert.AreEqual( 0, handyman.Navigation!.Mask );
		Assert.IsTrue( cells.Count > 4 && cells.All( cell => f.Grid.IsWalkable( cell.Item1, cell.Item2 ) ), "the edge test keeps mask 0 on the paths" );
	}

	[TestMethod]
	public void AGuestInTheWayPushesAWalkingMemberAside()
	{
		List<(int, int)> Run( bool withGuest )
		{
			var f = new Fixture();
			var handyman = f.Hire( StaffType.Handyman );
			if ( withGuest )
			{
				for ( var x = 1; x < 15; x++ )
					f.Guests.SpawnInPark( x, 5 );
			}
			var positions = new List<(int, int)>();
			f.Turns( 200, () => positions.Add( (handyman.Navigation!.X, handyman.Navigation.Y) ) );
			return positions;
		}
		CollectionAssert.AreNotEqual( Run( false ), Run( true ), "separation (0x0050E0E0) reads the guests around him" );
		CollectionAssert.AreEqual( Run( true ), Run( true ) );
	}

	[TestMethod]
	public void TheStrikeWalkRaisesTheWalkMaskAndTheStrikeClearsIt()
	{
		var f = new Fixture();
		var handyman = f.Hire( StaffType.Handyman );
		f.World.GatesOpen = () => true;
		f.Turns( 2 );
		f.Park.Staff.Strikes.Restore( new int[5], Enumerable.Range( 0, 5 ).Select( index => index == (int)StaffType.Handyman ).ToArray(), new uint[5] );
		var masks = new List<(StaffJob, int)>();
		f.Turns( 600, () =>
		{
			if ( masks.Count == 0 || masks[^1] != (handyman.Job, handyman.Navigation!.Mask) )
				masks.Add( (handyman.Job, handyman.Navigation!.Mask) );
		} );
		CollectionAssert.Contains( masks, (StaffJob.ToStrike, 1), string.Join( ", ", masks ) );
		CollectionAssert.Contains( masks, (StaffJob.Striking, 0), "0x005056E0: arrived, +0x188 = 0" );
	}

	[TestMethod]
	public void AMemberWithAFailedSearchStandsStuck()
	{
		var f = new Fixture();
		var handyman = f.Hire( StaffType.Handyman );
		f.Turns( 2 );
		var navigation = handyman.Navigation!;
		// no route reaches a cell outside the paths: SetDest leaves the walk arrived and stuck (0x0050F8E0)
		Assert.IsFalse( navigation.Navigate( f.Grid.Cells, GuestNavigation.Centre( (3, 15) ).X, GuestNavigation.Centre( (3, 15) ).Y ) );
		Assert.IsTrue( navigation.Original && navigation.Stuck && navigation.Arrived );
	}
}
