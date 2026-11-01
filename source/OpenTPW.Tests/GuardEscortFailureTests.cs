using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic guard admission failures through the real state machine; not native route-parity evidence.</summary>
[TestClass]
public class GuardEscortFailureTests
{
	private const int GuestId = 77;
	private static readonly (int X, int Y) CatchCell = (3, 3);

	// Only the guest-facing boundary is recorded. Routing, walking, job changes, sounds and RNG are production code.
	private sealed class RecordingGuests : IGuardGuests
	{
		public StaffMember Guard = null!;
		public ParkEconomy Park = null!;
		public (int X, int Y)[]? RouteLane;
		public readonly List<string> Calls = new();
		public readonly List<int> RouteMasks = new();
		public readonly List<uint> RouteSeeds = new();
		public readonly List<(int Id, (int X, int Y) Cell, int? Side, uint Seed)> Releases = new();
		public bool Held;
		public (int X, int Y)? CellOf( int id ) => id == GuestId ? CatchCell : null;
		public bool IsCatchable( int id ) => id == GuestId && !Held;
		public void Hold( int id )
		{
			Assert.AreEqual( GuestId, id );
			Assert.IsFalse( Held, "a catch must not hold an already held guest" );
			Calls.Add( "hold" );
			Held = true;
		}
		public bool Release( int id, (int X, int Y) cell, int? side = null )
		{
			Assert.IsTrue( Held, "release must follow hold" );
			Calls.Add( "release" );
			Releases.Add( (id, cell, side, Park.World.State) );
			Held = false;
			return true;
		}
		public bool AnyGuestWithin( (int X, int Y) cell, int radius ) => false;
		public (int X, int Y)[]? Lane( int side )
		{
			Assert.IsTrue( Held, "gate routing starts only after the catch" );
			RouteMasks.Add( Guard.Navigation!.Mask );
			RouteSeeds.Add( Park.World.State );
			return RouteLane;
		}
	}

	private sealed class Fixture
	{
		public readonly GuestPathGrid Grid = new( 12, 12 );
		public readonly ParkEconomy Park = EconomyTestData.Park();
		public readonly StaffWorldSimulation World;
		public readonly StaffMember Guard;
		public readonly RecordingGuests Guests = new();

		public Fixture( uint seed )
		{
			for ( var x = 1; x <= 7; x++ ) Grid.SetPath( x, 3, true );
			Park.OpenPark();
			Park.Litter.Cells = Grid.Cells;
			Guard = EconomyTestData.HireBest( Park, StaffType.Guard );
			World = new StaffWorldSimulation( Grid, () => Park.Staff.Members, _ => null, CatchCell ) { Guests = Guests };
			Park.StaffWorld = World;
			Guests.Guard = Guard;
			Guests.Park = Park;
			(Guard.CellX, Guard.CellY) = (3.5f, 3.5f);
			Guard.Navigation = new GuestNavigation();
			(Guard.Navigation.X, Guard.Navigation.Y) = GuestNavigation.BytePoint( CatchCell, 128, 128 );
			Guard.Navigation.SetDestination( Guard.Navigation.X, Guard.Navigation.Y, [] );
			Guard.Job = StaffJob.Chasing;
			Guard.State = StaffState.Working;
			Guard.TargetGuestId = GuestId;
			Guard.JobStamp = 20;
			Guard.SpeedBonus = GuestSpeed.ToiletBonus;
			Park.World.Restore( seed );
		}

		public void Update() => World.UpdateStaffMember( Park, Guard, 1 );
		public static (int X, int Y)[] LaneTo( (int X, int Y) destination ) =>
			[(0, 3), (1, 3), (2, 3), destination, destination];
	}

	[DataTestMethod]
	[DataRow( 0, 0 )]
	[DataRow( 0, 1 )]
	[DataRow( 1, 0 )]
	[DataRow( 1, 1 )]
	[DataRow( 2, 0 )]
	[DataRow( 2, 1 )]
	public void FailedEscortAdmissionReleasesExactlyOnceAndAbandonsTheChase( int failure, int seed )
	{
		var f = new Fixture( (uint)seed );
		f.Guests.RouteLane = failure switch
		{
			0 => null,
			1 => [(0, 3), (1, 3), (2, 3), (3, 3)],
			_ => Fixture.LaneTo( (8, 8) )
		};
		if ( failure == 2 )
		{
			// A path island surrounded by blocked type-2 cells: neither ordinary nor off-path routing can reach it.
			f.Grid.SetPath( 8, 8, true );
			foreach ( var (x, y) in new[] { (7, 8), (9, 8), (8, 7), (8, 9) } )
				f.Grid.Cells.SetType( x, y, (ParkCellType)2 );
			foreach ( var mask in new[] { 0, 1 } )
			{
				var probe = new GuestNavigation { X = f.Guard.Navigation!.X, Y = f.Guard.Navigation.Y, Mask = mask };
				var target = GuestNavigation.Centre( (8, 8) );
				Assert.IsFalse( probe.Navigate( f.Grid.Cells, target.X, target.Y ), "fixture must really reject both route searches" );
			}
		}
		var expected = new WorldRng( (uint)seed );
		var side = (int)(expected.NextWord() & 1);
		f.Update();

		CollectionAssert.AreEqual( new[] { "hold", "release" }, f.Guests.Calls );
		CollectionAssert.AreEqual( new[] { 0, 1 }, f.Guests.RouteMasks.Distinct().ToArray(), "the failed first attempt must be retried with mask 1" );
		Assert.IsTrue( f.Guests.RouteSeeds.All( value => value == (uint)seed ), "no draw before the set-down" );
		Assert.AreEqual( 1, f.Guests.Releases.Count );
		Assert.AreEqual( (GuestId, CatchCell, (int?)side, expected.State), f.Guests.Releases.Single() );
		Assert.AreEqual( expected.State, f.Park.World.State, "exactly one draw, including through both failed attempts" );
		Assert.IsFalse( f.Guests.Held );
		Assert.AreEqual( StaffJob.Idle, f.Guard.Job );
		Assert.AreEqual( StaffState.Idle, f.Guard.State );
		Assert.AreEqual( 0, f.Guard.TargetGuestId );
		Assert.AreEqual( 0, f.Guard.SpeedBonus );
		Assert.IsFalse( f.World.IsEscorting( GuestId ) );
		// [BIN:TPW-EXE:0x004D691B caught kid counted] the catch counted the kid (month and lifetime words), the set-down failure then abandoned the chase (0x004D6A1F: month, lifetime and got-away words)
		Assert.AreEqual( (2, 1, 2), (f.Park.MisbehavingKids( false ), f.Park.MisbehavingKids( true ), f.Park.MisbehavingKidsEver) );
		CollectionAssert.AreEqual( new[] { (StaffWorldSimulation.CatchEvent, false), (StaffWorldSimulation.SetDownEvent, true) },
			f.World.TakeVoices().Select( voice => (voice.Event, voice.Kids) ).ToArray() );
	}

	[TestMethod]
	public void ReachableBoothKeepsTheGuestHeldWithoutDrawingTheReleaseSide()
	{
		var f = new Fixture( 1 );
		f.Guests.RouteLane = Fixture.LaneTo( (7, 3) );
		f.Update();
		CollectionAssert.AreEqual( new[] { "hold" }, f.Guests.Calls );
		CollectionAssert.AreEqual( new[] { 0 }, f.Guests.RouteMasks );
		Assert.AreEqual( 0, f.Guests.Releases.Count );
		Assert.IsTrue( f.Guests.Held );
		Assert.AreEqual( StaffJob.Escorting, f.Guard.Job );
		Assert.AreEqual( GuestId, f.Guard.TargetGuestId );
		Assert.IsTrue( f.World.IsEscorting( GuestId ) );
		Assert.AreEqual( 1u, f.Park.World.State );
		Assert.IsTrue( f.Guard.Navigation!.Original && !f.Guard.Navigation.Stuck );
		Assert.AreEqual( (7, 3), (f.Guard.Navigation.TargetX >> 16, f.Guard.Navigation.TargetY >> 16) );
		Assert.AreEqual( (1, 0, 1), (f.Park.MisbehavingKids( false ), f.Park.MisbehavingKids( true ), f.Park.MisbehavingKidsEver), "a caught kid adds to the month and lifetime words, not to the got-away word" );
		CollectionAssert.AreEqual( new[] { (StaffWorldSimulation.CatchEvent, false) },
			f.World.TakeVoices().Select( voice => (voice.Event, voice.Kids) ).ToArray() );
	}

	/// <summary>
	/// [BIN:TPW-EXE:0x004D6954] the catch routes to the booth cell of the turn's side and nowhere else: a booth that is no path cell fails the first request (the guard's mask 0), and the second request
	/// (+0x188 = 1, which may leave the paths) walks the guard onto it. The entrance end of the lane is not a stand-in for the booth.
	/// </summary>
	[TestMethod]
	public void ABoothThatIsNoPathCellIsReachedWithTheSecondRequestAndNotReplacedByTheEntrance()
	{
		var f = new Fixture( 1 );
		// the booth (8, 3) is open land beside the path's end (7, 3); the entrance (9, 3) is a path cell beyond it that no path reaches
		f.Grid.SetPath( 9, 3, true );
		f.Guests.RouteLane = [(0, 3), (1, 3), (2, 3), (8, 3), (9, 3)];
		f.Update();
		CollectionAssert.AreEqual( new[] { "hold" }, f.Guests.Calls );
		CollectionAssert.AreEqual( new[] { 0, 1 }, f.Guests.RouteMasks, "the booth was asked for with each mask in turn" );
		Assert.AreEqual( StaffJob.Escorting, f.Guard.Job );
		Assert.AreEqual( (8, 3), (f.Guard.Navigation!.TargetX >> 16, f.Guard.Navigation.TargetY >> 16), "the walk is to the booth cell" );
		Assert.AreEqual( 1, f.Guard.Navigation.Mask );
		Assert.AreEqual( 0, f.Guests.Releases.Count );
	}

	[TestMethod]
	public void AnAbandonedChaseCountsAsTheKidThatGotAway()
	{
		var f = new Fixture( 1 );
		// [BIN:TPW-EXE:0x004D67AC chase timer] a timer of 0 gives the chase up before the guard moves
		f.Guard.JobStamp = 0;
		f.Update();
		Assert.AreEqual( StaffJob.Idle, f.Guard.Job );
		Assert.AreEqual( (1, 1, 1), (f.Park.MisbehavingKids( false ), f.Park.MisbehavingKids( true ), f.Park.MisbehavingKidsEver) );
		Assert.AreEqual( 0, f.Guests.Calls.Count, "the kid was never caught" );
	}
}
