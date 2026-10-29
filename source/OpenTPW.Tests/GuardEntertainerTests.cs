using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The entertainers' and guards' state machines in a world (tp.exe <c>0x004D4810</c>, <c>0x004D6410</c>; docs/reverse/WANDER-plan.md §4): performances
/// for nearby guests, the moving <c>RegionFX</c> rows, and a prank's chase, catch and escort.
/// </summary>
[TestClass]
public class GuardEntertainerTests
{
	private sealed class Fixture
	{
		public readonly GuestPathGrid Grid = new( 20, 20 );
		public readonly ParkEconomy Park = EconomyTestData.Park();
		public readonly GuestSimulation Guests;
		public readonly StaffWorldSimulation World;
		public readonly List<StaffVoice> Sounds = new();

		public Fixture()
		{
			for ( var x = 0; x < 16; x++ )
				Grid.SetPath( x, 5, true );
			for ( var y = 0; y < 11; y++ )
				Grid.SetPath( 10, y, true );
			Park.OpenPark();
			Park.Litter.Cells = Grid.Cells;
			Park.Litter.Environment.Effects = new RegionEffect[RegionEffect.Count];
			Park.Litter.Environment.Effects[RegionEffect.Entertainer] = new RegionEffect( 3, 2, 0, 0, 0, 0 );
			Park.Litter.Environment.Effects[RegionEffect.Guard] = new RegionEffect( 3, 0, 0, 0, 10, 0 );
			var settings = GuestTests.CreateSettings();
			settings.ExitLevelSeconds = 10_000;
			Guests = new GuestSimulation( Grid, settings, 5 ) { ArrivalsEnabled = false, Litter = Park.Litter };
			World = new StaffWorldSimulation( Grid, () => Park.Staff.Members, _ => null, (0, 5) ) { Guests = Guests };
			Guests.IsEscorted = World.IsEscorting;
			Park.StaffWorld = World;
		}

		public StaffMember Hire( StaffType type )
		{
			var member = EconomyTestData.HireBest( Park, type );
			World.Synchronize();
			return member;
		}

		/// <summary>One park turn: the guests' ticks of the turn, then the economy's turn (which runs the staff).</summary>
		public void Turns( int turns, Action? each = null )
		{
			for ( var turn = 0; turn < turns; turn++ )
			{
				var next = Park.Turn + 1;
				while ( ParkCalendar.Turn( Guests.TickCount ) < next )
					Guests.Tick( GuestSnapshotTests.Tick );
				EconomyTestData.AdvanceToTurn( Park, next );
				Sounds.AddRange( World.TakeVoices() );
				each?.Invoke();
			}
		}

		/// <summary>Stands a member on a cell, idle for a long while (so tests choose who moves).</summary>
		public void Stand( StaffMember member, int x, int y )
		{
			Turns( 1 );
			var navigation = member.Navigation!;
			(navigation.X, navigation.Y) = GuestNavigation.BytePoint( (x, y), 128, 128 );
			navigation.SetDestination( navigation.X, navigation.Y, [] );
			(member.CellX, member.CellY) = (x + 0.5f, y + 0.5f);
			member.Job = StaffJob.Idle;
			member.State = StaffState.Idle;
			member.IdleStamp = Park.Turn + 100_000;
			Park.MoveStaffEffect( member, (x, y) );
		}

		public (int X, int Y) Cell( StaffMember member ) => member.Navigation!.Cell;
	}

	[TestMethod]
	public void AnEntertainerPerformsForNearbyGuestsForWorkDurationTurns()
	{
		var f = new Fixture();
		var entertainer = f.Hire( StaffType.Entertainer );
		var guest = f.Guests.SpawnInPark( 2, 5 );
		long started = -1, ended = -1;
		f.Turns( 400, () =>
		{
			if ( started < 0 && entertainer.Job == StaffJob.Performing )
				started = entertainer.JobStamp;
			else if ( started > 0 && ended < 0 && f.Sounds.Any( sound => sound.Event == StaffWorldSimulation.PerformanceEndEvent ) )
				ended = f.Park.Turn;
		} );
		Assert.IsTrue( started > 0, "he performed" );
		var work = f.Park.Settings[StaffType.Entertainer].WorkDuration[entertainer.Grade];
		Assert.AreEqual( started + work + 1, ended, "the performance ends (cat_staff 135) when the turn passes its start + WorkDuration" );
		Assert.IsTrue( f.Sounds.Any( sound => sound.Event == StaffWorldSimulation.PerformanceEndEvent && !sound.Kids ), "cat_staff 135 at its end" );
	}

	[TestMethod]
	public void APerformanceKeepsTheSecondDrawThatChoseItsScript()
	{
		// tp.exe 0x004D47B5: after the draw divisible by 3 and the audience test, a second world draw r picks the script, 13 + (r mod (n - 1)) of the bank's n entries
		var f = new Fixture();
		var entertainer = f.Hire( StaffType.Entertainer );
		f.Guests.SpawnInPark( 2, 5 );
		uint? kept = null;
		for ( var turn = 0; turn < 400 && kept == null; turn++ )
		{
			var mirror = new WorldRng( f.Park.World.State );
			var draws = Enumerable.Range( 0, 400 ).Select( _ => mirror.NextWord() ).ToArray();
			f.Turns( 1 );
			if ( entertainer.Job != StaffJob.Performing )
				continue;
			kept = entertainer.PerformanceDraw;
			Assert.IsTrue( Enumerable.Range( 1, draws.Length - 1 ).Any( i => draws[i - 1] % 3 == 0 && draws[i] == kept ), "the draw right after the one divisible by 3" );
		}
		Assert.IsNotNull( kept, "he performed" );
	}

	[TestMethod]
	public void WithoutGuestsAnEntertainerPatrolsAndNeverPerforms()
	{
		var f = new Fixture();
		var entertainer = f.Hire( StaffType.Entertainer );
		var cells = new HashSet<(int, int)>();
		f.Turns( 300, () =>
		{
			cells.Add( f.Cell( entertainer ) );
			Assert.AreNotEqual( StaffJob.Performing, entertainer.Job );
		} );
		Assert.IsTrue( cells.Count > 3, $"he walked over {cells.Count} cells" );
		Assert.IsTrue( f.Sounds.Any( sound => sound.Event == 0 && sound.Type == StaffType.Entertainer ), "his walking or idle voices" );
	}

	[TestMethod]
	public void AnEntertainersRegionRowFollowsHimAndGoesWithHim()
	{
		var f = new Fixture();
		var entertainer = f.Hire( StaffType.Entertainer );
		f.Turns( 60 );
		var cell = f.Cell( entertainer );
		Assert.AreEqual( cell, entertainer.EffectCell );
		Assert.AreEqual( 2, f.Park.Litter.Environment.At( cell.X, cell.Y ).Happiness, "row 0 on his cell" );
		Assert.AreEqual( 1, f.Park.Litter.Environment.At( cell.X + 1, cell.Y ).Happiness, "2 / 2 beside him" );
		Assert.AreEqual( 0, f.Park.Litter.Environment.At( cell.X + 2, cell.Y ).Happiness, "2 / 3 truncates to 0" );

		var json = ParkSaveFile.Serialize( f.Park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), f.Park.Settings, f.Park.Catalog );
		restored.Litter.Environment.Effects = f.Park.Litter.Environment.Effects;
		restored.ApplyStaffEffects();
		Assert.AreEqual( f.Park.Litter.Environment.At( cell.X, cell.Y ), restored.Litter.Environment.At( cell.X, cell.Y ), "a load puts the row back" );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );

		f.Park.Fire( entertainer.Id );
		Assert.AreEqual( default, f.Park.Litter.Environment.At( cell.X, cell.Y ), "dismissed, the row goes" );
	}

	[TestMethod]
	public void APrankOnASecureCellSendsTheFreeGuardNearestTheStaffHq()
	{
		var f = new Fixture();
		var near = f.Hire( StaffType.Guard );
		var far = f.Hire( StaffType.Guard );
		f.Stand( far, 14, 5 );
		f.Stand( near, 10, 5 );
		var unwatched = f.Guests.SpawnInPark( 10, 0 );
		f.World.ReportPrank( f.Park, unwatched.Id );
		Assert.AreEqual( 0, f.Park.Litter.Environment.At( 10, 0 ).Security, "five cells from both guards: no security" );
		Assert.AreEqual( (StaffJob.Idle, StaffJob.Idle), (near.Job, far.Job), "no security, no chase" );

		var prankster = f.Guests.SpawnInPark( 12, 5 );
		Assert.AreEqual( 10 / 3 + 10 / 3, f.Park.Litter.Environment.At( 12, 5 ).Security, "both guards' rows" );
		f.World.ReportPrank( f.Park, prankster.Id );
		Assert.AreEqual( StaffJob.Chasing, near.Job, "10² + 5² beats 14² + 5², however far from the prankster" );
		Assert.AreEqual( prankster.Id, near.TargetGuestId );
		Assert.AreEqual( StaffJob.Idle, far.Job );
		Assert.AreEqual( f.Park.Settings[StaffType.Guard].WorkDuration[near.Grade], near.JobStamp, "the chase timer" );
		Assert.IsTrue( f.World.TakeVoices().Any( sound => sound.Event == StaffWorldSimulation.ChaseEvent ), "cat_staff 136" );
	}

	[TestMethod]
	public void TheNewestGuardWinsATieBecauseTheGuardListIsHeadInserted()
	{
		// [BIN:TPW-EXE:0x004D5DE0 guard list] a hired guard goes to the head of the guard list (0x00519AA0), which the choice 0x005099C0 walks from the head, the first of equal distances winning
		var f = new Fixture();
		var first = f.Hire( StaffType.Guard );
		var second = f.Hire( StaffType.Guard );
		f.Stand( first, 10, 5 );
		f.Stand( second, 10, 5 );
		var prankster = f.Guests.SpawnInPark( 12, 5 );
		f.World.ReportPrank( f.Park, prankster.Id );
		Assert.AreEqual( StaffJob.Chasing, second.Job, "hired last, first in the list" );
		Assert.AreEqual( StaffJob.Idle, first.Job );
	}

	[TestMethod]
	public void AMechanicSettingOffForABrokenRideAsksTheAdvisorForAdvice70()
	{
		// [BIN:TPW-EXE:0x004DA6A5 mechanic on the way] the advice is queued as the mechanic claims a ride nobody called him to and that is not being upgraded
		var f = new Fixture();
		var ride = f.Park.RegisterExisting( 1100 );
		ride.Repair = 0;
		ride.IsBrokenDown = true;
		var world = new StaffWorldSimulation( f.Grid, () => f.Park.Staff.Members, id => id == ride.Id ? (14, 5) : null, (0, 5) ) { Guests = f.Guests };
		f.Park.StaffWorld = world;
		var called = new List<(int Ride, int Mechanic)>();
		world.MechanicOnTheWay += ( rideId, mechanicId ) => called.Add( (rideId, mechanicId) );
		var mechanic = EconomyTestData.HireBest( f.Park, StaffType.Mechanic );
		world.Synchronize();
		f.Turns( 100 );
		Assert.IsTrue( called.Count > 0, "the mechanic set off for the broken ride" );
		CollectionAssert.AreEqual( new[] { (ride.Id, mechanic.Id) }, called.Take( 1 ).ToArray(), "the first claim asks for the advice" );
	}

	[TestMethod]
	public void AMechanicCalledToARideByThePlayerBringsNoAdviceBecauseTheCallWasTheAdvice()
	{
		// [BIN:TPW-EXE:0x004DA696 mechanic on the way] mRequestedService (0x004E03D0) set skips the advice
		var f = new Fixture();
		var ride = f.Park.RegisterExisting( 1100 );
		ride.IsBrokenDown = true;
		ride.MaintenanceRequested = true;
		var world = new StaffWorldSimulation( f.Grid, () => f.Park.Staff.Members, id => id == ride.Id ? (14, 5) : null, (0, 5) ) { Guests = f.Guests };
		f.Park.StaffWorld = world;
		var called = 0;
		world.MechanicOnTheWay += ( _, _ ) => called++;
		EconomyTestData.HireBest( f.Park, StaffType.Mechanic );
		world.Synchronize();
		f.Turns( 300 );
		Assert.AreEqual( 0, called );
		Assert.IsFalse( ride.IsBrokenDown, "he did go to the ride and repair it" );
	}

	[TestMethod]
	public void AGuardCatchesThePranksterCarriesItAndSetsItDownToLeave()
	{
		var f = new Fixture();
		var guard = f.Hire( StaffType.Guard );
		f.Stand( guard, 10, 5 );
		var prankster = f.Guests.SpawnInPark( 12, 5 );
		f.World.ReportPrank( f.Park, prankster.Id );
		var seen = new List<StaffJob>();
		var held = false;
		f.Turns( 200, () =>
		{
			if ( seen.Count == 0 || seen[^1] != guard.Job )
				seen.Add( guard.Job );
			held |= prankster.State == GuestState.HeldByGuard && !prankster.IsVisible;
			// a guest whose counter still holds changes its mind on its first turn in state 18 (the recall 0x00500A50, GuestTests); this one has none left once caught, so the set-down sends it home
			if ( prankster.State == GuestState.HeldByGuard )
				prankster.ExitLevel = 0;
		} );
		Assert.IsTrue( held, "caught and carried out of sight" );
		CollectionAssert.IsSubsetOf( new[] { StaffJob.Chasing, StaffJob.Escorting, StaffJob.ReturningFromEscort }, seen, string.Join( ", ", seen ) );
		Assert.IsTrue( seen.IndexOf( StaffJob.Chasing ) < seen.IndexOf( StaffJob.Escorting ) && seen.IndexOf( StaffJob.Escorting ) < seen.IndexOf( StaffJob.ReturningFromEscort ) );
		Assert.IsTrue( prankster.State is GuestState.LeavingPark or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome || f.Guests.Find( prankster.Id ) == null,
			$"set down, it leaves ({prankster.State})" );
		Assert.IsTrue( f.Sounds.Any( sound => sound.Event == StaffWorldSimulation.CatchEvent && !sound.Kids ), "cat_staff 137" );
		Assert.IsTrue( f.Sounds.Any( sound => sound.Event == StaffWorldSimulation.SetDownEvent && sound.Kids ), "cat_kids 128" );
	}

	[TestMethod]
	public void AHeldGuestWithoutItsGuardLeavesThePark()
	{
		var f = new Fixture();
		var guard = f.Hire( StaffType.Guard );
		f.Stand( guard, 10, 5 );
		var prankster = f.Guests.SpawnInPark( 11, 5 );
		f.World.ReportPrank( f.Park, prankster.Id );
		// a prankster that slips past the guard ends the chase; the next prank report sends him again
		for ( var turn = 0; turn < 400 && guard.Job != StaffJob.Escorting; turn++ )
		{
			f.Turns( 1 );
			if ( guard.Job is StaffJob.Idle or StaffJob.Patrol && prankster.State != GuestState.HeldByGuard )
				f.World.ReportPrank( f.Park, prankster.Id );
		}
		Assert.AreEqual( StaffJob.Escorting, guard.Job );
		Assert.AreEqual( GuestState.HeldByGuard, prankster.State );
		f.Park.Fire( guard.Id );
		f.Turns( 1 );
		Assert.IsNull( f.Guests.Find( prankster.Id ), "no guard escorts it: the original destroys it (0x0050B780)" );
	}

	[TestMethod]
	public void AGuardPickedUpWhileEscortingSetsTheGuestDown()
	{
		var f = new Fixture();
		var guard = f.Hire( StaffType.Guard );
		f.Stand( guard, 10, 5 );
		var prankster = f.Guests.SpawnInPark( 11, 5 );
		f.World.ReportPrank( f.Park, prankster.Id );
		for ( var turn = 0; turn < 100 && guard.Job != StaffJob.Escorting; turn++ )
			f.Turns( 1 );
		guard.State = StaffState.PickedUp;
		f.Turns( 1 );
		Assert.AreEqual( StaffJob.PickedUp, guard.Job );
		Assert.AreEqual( 0, guard.TargetGuestId );
		Assert.AreNotEqual( GuestState.HeldByGuard, prankster.State, "set down on his cell" );
	}

	[TestMethod]
	public void TheGuardChoiceTakesTheFreeGuardNearestTheOriginFirstOnATie()
	{
		var guards = new (StaffJob, (int, int))[] { (StaffJob.Chasing, (0, 1)), (StaffJob.Patrol, (3, 4)), (StaffJob.Idle, (4, 3)), (StaffJob.Idle, (0, 6)) };
		Assert.AreEqual( 1, StaffWorldSimulation.ChooseGuard( guards, (0, 0) ), "the chaser is not free; 3² + 4² ties 4² + 3², the first wins" );
		Assert.AreEqual( 3, StaffWorldSimulation.ChooseGuard( guards, (0, 7) ) );
		Assert.IsNull( StaffWorldSimulation.ChooseGuard( guards.Take( 1 ), (0, 0) ) );
	}

	[TestMethod]
	public void ANewGuardStandsWithHisRowDownBeforeHisFirstTurn()
	{
		var f = new Fixture();
		var guard = f.Hire( StaffType.Guard );
		var prankster = f.Guests.SpawnInPark( 1, 5 );
		f.World.ReportPrank( f.Park, prankster.Id );
		Assert.AreEqual( StaffJob.Chasing, guard.Job, "the constructor puts the security row down" );
	}

	[TestMethod]
	public void AGuestCaughtInItsActivityLeavesIt()
	{
		var f = new Fixture();
		var guest = f.Guests.SpawnInPark( 3, 5 );
		guest.Activity = GuestActivity.Cheering;
		((IGuardGuests)f.Guests).Hold( guest.Id );
		Assert.AreEqual( (GuestState.HeldByGuard, GuestActivity.None), (guest.State, guest.Activity) );
		var validation = f.Guests.ValidateSnapshot( f.Guests.CaptureSnapshot() );
		Assert.IsTrue( validation.IsValid, string.Join( "; ", validation.Errors ) );
	}

	[TestMethod]
	public void AGuardsWorldStateSurvivesASave()
	{
		var f = new Fixture();
		var guard = f.Hire( StaffType.Guard );
		var entertainer = f.Hire( StaffType.Entertainer );
		f.Turns( 40 );
		var json = ParkSaveFile.Serialize( f.Park );
		StringAssert.Contains( json, "\"WorldState\"" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), f.Park.Settings, f.Park.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );
		var legacy = json.Replace( "\"WorldState\"", "\"Handyman\"" );
		Assert.AreEqual( json, ParkSaveFile.Serialize( ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( legacy ) ), f.Park.Settings, f.Park.Catalog ) ),
			"the record's earlier name still loads" );
	}
}
