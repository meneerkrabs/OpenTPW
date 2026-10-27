using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// The swim session of the pools extension against a real <see cref="GuestSimulation"/> and a real ride script (docs/POOLS.md, "Swimming"): guests queue for a changing room, change, swim inside the
/// connected pool cells only, avoid each other, climb out, change back and return to the path; the edge cases of the session; and the save roundtrip in the middle of it.
/// </summary>
[TestClass]
public class PoolsSwimSessionTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	/// <summary>
	/// The queue tests' rig with a changing room: a facility whose script holds a guest for a second, its pool-side cell (5, 5) in a pool of 6 x 6 cells at x 5 to 10, y 3 to 8. The path row is y = 0, the
	/// queue column x = 2 down to the entrance (2, 6); the room's exit is the path cell (2, 0). The pool lies beside the queue, clear of every path cell.
	/// </summary>
	internal sealed class Rig
	{
		public readonly QueueTests.Rig Park;
		public GuestSimulation Simulation => Park.Simulation;
		public RideVisitorBridge Ride => Park.Ride;

		public Rig( int capacity = 2, Action<GuestSettings>? configure = null, int gridSize = 12 )
		{
			Park = new QueueTests.Rig( queueCells: 5, takesGuests: true, capacity: capacity, kind: RideVisitorKind.Facility, configure: configure, gridSize: gridSize ) { KeepNeedsEmpty = true };
			for ( var y = 3; y <= 8; y++ )
				for ( var x = 5; x <= 10; x++ )
					Park.Grid.Cells.SetType( x, y, ParkCellTypes.PoolWater );
			Ride.IsChangingRoom = true;
			Ride.PoolAccessCandidates = new[] { (5, 2), (5, 3) };
		}

		public Guest Send( float happiness = 60 ) => Park.Send( happiness );

		/// <summary>One park turn at a time, calling <paramref name="each"/> after every turn.</summary>
		public void Turns( int turns, Action? each = null )
		{
			for ( var turn = 0; turn < turns; turn++ )
			{
				var target = Simulation.ParkTurn + 1;
				while ( Simulation.ParkTurn < target )
					Park.Step();
				each?.Invoke();
			}
		}

		/// <summary>Runs until <paramref name="done"/>, at most <paramref name="limit"/> turns.</summary>
		public int Until( Func<bool> done, int limit = 600 )
		{
			for ( var turn = 0; turn < limit; turn++ )
			{
				if ( done() )
					return turn;
				Turns( 1 );
			}
			Assert.IsTrue( done(), $"not reached within {limit} turns" );
			return limit;
		}

		public PoolSwimRegion Region => Simulation.SwimRegionOf( Ride )!;
		public ParkCellMap Cells => Park.Grid.Cells;
	}

	private static bool InWater( Rig rig, Guest guest ) => rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y );

	// ---- The pool side of the room -------------------------------------------------------------------

	[TestMethod]
	public void TheAccessCellIsTheFirstBackCellThatHoldsWaterAndTheRegionIsItsConnectedPool()
	{
		var rig = new Rig();
		Assert.AreEqual( (5, 3), rig.Simulation.PoolAccessOf( rig.Ride ), "(5, 2) is no water, so the next candidate is taken" );
		Assert.AreEqual( 36, rig.Region.Cells.Count );
		Assert.AreEqual( (5, 3), rig.Region.Access );
		rig.Cells.SetType( 5, 2, ParkCellTypes.PoolWater );
		Assert.AreEqual( (5, 2), rig.Simulation.PoolAccessOf( rig.Ride ), "the first candidate wins as soon as it holds water" );
		Assert.AreEqual( 37, rig.Region.Cells.Count, "and the region is rebuilt for the new water" );
		var other = new QueueTests.Rig( queueCells: 5 );
		Assert.IsFalse( other.Ride.IsChangingRoom );
		Assert.IsNull( other.Simulation.SwimRegionOf( other.Ride ), "an ordinary attraction has no pool" );
		rig.Ride.IsChangingRoom = false;
		Assert.IsNull( rig.Simulation.SwimRegionOf( rig.Ride ), "nor does a changing room flag turned off" );
	}

	// ---- The session ------------------------------------------------------------------------------------

	[TestMethod]
	public void AGuestQueuesChangesSwimsInsideTheWaterClimbsOutChangesBackAndReturnsToThePath()
	{
		var rig = new Rig();
		var guest = rig.Send();
		var seen = new List<GuestState>();
		var states = new HashSet<GuestState>();
		var swimTurns = 0;
		var happinessBefore = 0f;
		var positions = new HashSet<(int X, int Y)>();
		rig.Until( () => rig.Simulation.IsChangingIn( guest ), 400 );
		Assert.IsFalse( guest.IsVisible, "changing in the room, out of sight like a rider" );
		Assert.AreEqual( GuestState.Using, guest.State );
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		Assert.AreEqual( rig.Ride.AttractionId, guest.AttractionId, "the swimmer names its changing room" );
		Assert.IsTrue( guest.IsVisible && guest.IsSwimming );
		var start = (guest.Navigation.X, guest.Navigation.Y);
		Assert.AreEqual( rig.Region.Access, guest.Navigation.Cell, "it steps into the water at the pool-side cell" );
		Assert.IsFalse( rig.Simulation.IsOnParkCell( guest ), "a swimmer is on no path cell" );
		happinessBefore = guest.Happiness;
		Assert.IsTrue( guest.SwimTurns is >= PoolSwimSettings.MinimumTurns and < PoolSwimSettings.MinimumTurns + PoolSwimSettings.ExtraTurns );
		var plannedTurns = guest.SwimTurns;
		float happinessAtChanging = 0;
		rig.Until( () =>
		{
			states.Add( guest.State );
			if ( guest.State is GuestState.Swimming )
			{
				swimTurns++;
				Assert.IsTrue( InWater( rig, guest ), $"turn {swimTurns}: ({guest.X:F2}, {guest.Y:F2}) is in the water" );
				Assert.IsTrue( rig.Cells.RawTypeAt( guest.CellX, guest.CellY ) == ParkCellTypes.PoolWater, "and its cell is a pool cell" );
				positions.Add( (guest.CellX, guest.CellY) );
			}
			if ( guest.State is GuestState.ClimbingOut )
				Assert.IsTrue( InWater( rig, guest ), "climbing out is swimming to the steps" );
			if ( guest.State == GuestState.ChangingOut && happinessAtChanging == 0 )
				happinessAtChanging = guest.Happiness;
			return guest.State is GuestState.ExitingRide or GuestState.DoingNothing;
		}, 900 );
		CollectionAssert.IsSubsetOf( new[] { GuestState.Swimming, GuestState.ClimbingOut, GuestState.ChangingOut }, states.ToArray() );
		Assert.IsTrue( swimTurns >= plannedTurns - 2 && swimTurns <= plannedTurns + 2, $"the swim lasted its {plannedTurns} planned turns ({swimTurns})" );
		Assert.IsTrue( positions.Count > 3, $"it swam through {positions.Count} cells" );
		Assert.IsTrue( happinessAtChanging >= Math.Min( 100f, happinessBefore ) - 0.5f + 0f && happinessAtChanging > happinessBefore - 0.01f, $"happiness rose ({happinessBefore} to {happinessAtChanging})" );
		Assert.AreEqual( 0, guest.AttractionId, "back on the path it names no attraction" );
		Assert.AreEqual( rig.Ride.ExitCell, guest.Cell, "on the room's path-side exit" );
		Assert.IsTrue( rig.Simulation.Grid.IsWalkable( guest.CellX, guest.CellY ) );
		Assert.AreEqual( 0, guest.SwimTurns );
		Assert.IsFalse( guest.SwimHasGoal );
		Assert.AreEqual( 0u, guest.SwimRandom );
		Assert.AreEqual( rig.Ride.AttractionId, guest.LastAttractionId );
		// the guest decides again and walks on
		rig.Turns( 40 );
		Assert.IsFalse( guest.InSwimSession );
		Assert.AreNotEqual( start, (guest.Navigation.X, guest.Navigation.Y) );
	}

	[TestMethod]
	public void ASwimGivesTheHappinessItPromises()
	{
		var rig = new Rig();
		var guest = rig.Send( happiness: 40 );
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		var before = guest.Happiness;
		rig.Until( () => guest.State == GuestState.ChangingOut, 900 );
		Assert.AreEqual( before + PoolSwimSettings.HappinessGain, guest.Happiness, 0.5f, "12 happiness, with the needs held at zero" );
		var full = rig.Send( happiness: 95 );
		rig.Until( () => full.State == GuestState.ChangingOut, 1200 );
		Assert.IsTrue( full.Happiness <= 100f, "the meter stays within 0..100" );
	}

	[TestMethod]
	public void SeveralSwimmersAvoidEachOtherAndEveryOneGetsOutAgain()
	{
		var rig = new Rig( capacity: 4 );
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
		var minimum = double.MaxValue;
		var together = 0;
		rig.Until( () =>
		{
			var swimmers = guests.Where( guest => guest.IsSwimming ).ToList();
			foreach ( var guest in swimmers )
				Assert.IsTrue( InWater( rig, guest ), $"guest {guest.Id}" );
			together = Math.Max( together, swimmers.Count );
			if ( swimmers.Count >= 2 && rig.Simulation.ParkTurn % 2 == 0 )
				for ( var a = 0; a < swimmers.Count; a++ )
					for ( var b = a + 1; b < swimmers.Count; b++ )
						if ( swimmers[a].SwimTurns < 100 + 0 && swimmers[b].SwimTurns < 100 )
							minimum = Math.Min( minimum, Math.Sqrt( Math.Pow( swimmers[a].X - swimmers[b].X, 2 ) + Math.Pow( swimmers[a].Y - swimmers[b].Y, 2 ) ) );
			return guests.All( guest => !guest.InSwimSession && guest.AttractionId == 0 && guest.State != GuestState.Using && guest.LastAttractionId == rig.Ride.AttractionId );
		}, 2400 );
		Assert.IsTrue( together >= 4, $"{together} swam at once" );
		Assert.IsTrue( minimum >= 0.2, $"swimmers settled apart ({minimum:F2} cells at the closest, after the first stroke)" );
		Assert.IsTrue( guests.All( guest => rig.Simulation.Grid.IsWalkable( guest.CellX, guest.CellY ) ), "everyone is back on the path" );
	}

	[TestMethod]
	public void ASwimmerLeavesNoTraceInTheWalkersCellListsAndIsNotCatchable()
	{
		var rig = new Rig();
		var guest = rig.Send();
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		Assert.IsFalse( ((IGuardGuests)rig.Simulation).IsCatchable( guest.Id ), "a guard cannot wade in" );
		Assert.AreEqual( 0, ((IGuardGuests)rig.Simulation).WalkersOn( guest.CellX, guest.CellY ).Count(), "no walker separates from a swimmer" );
		var stats = rig.Simulation.GetStatistics();
		Assert.AreEqual( 1, stats.Walking, "a swimmer counts with the walkers" );
		rig.Until( () => guest.State == GuestState.ChangingOut, 900 );
		Assert.AreEqual( 1, rig.Simulation.GetStatistics().OnRides, "a guest changing back is indoors" );
		Assert.IsFalse( guest.IsVisible );
		Assert.IsFalse( ((IGuardGuests)rig.Simulation).AnyGuestWithin( rig.Region.Access, 3 ), "nobody is in sight while the only guest is inside" );
	}

	// ---- Edge cases ------------------------------------------------------------------------------------------

	[TestMethod]
	public void DemolishingTheChangingRoomGetsTheSwimmersOutOnItsExit()
	{
		foreach (var (name, stage) in new (string, Func<Guest, bool>)[]
		{
			("aboard changing in", guest => guest.State == GuestState.Using),
			("swimming", guest => guest.State == GuestState.Swimming),
			("climbing out", guest => guest.State == GuestState.ClimbingOut),
			("changing back", guest => guest.State == GuestState.ChangingOut)
		} )
		{
			var rig = new Rig();
			var guest = rig.Send();
			rig.Until( () => guest.State == GuestState.Swimming || stage( guest ) && guest.State == GuestState.Using, 400 );
			if ( !stage( guest ) )
			{
				if ( name is "climbing out" or "changing back" )
					guest.SwimTurns = 1;
				rig.Until( () => stage( guest ), 400 );
			}
			Assert.IsTrue( stage( guest ), name );
			rig.Simulation.Unregister( rig.Ride );
			Assert.AreEqual( GuestState.DoingNothing, guest.State, name );
			Assert.AreEqual( (2, 0), guest.Cell, $"{name}: on the room's exit cell" );
			Assert.AreEqual( 0, guest.AttractionId, name );
			Assert.IsFalse( guest.InSwimSession, name );
			Assert.AreEqual( 0u, guest.SwimRandom, name );
			Assert.IsTrue( rig.Simulation.Grid.IsWalkable( guest.CellX, guest.CellY ), name );
			rig.Turns( 30 );
			Assert.IsTrue( rig.Simulation.Guests.All( other => !other.InSwimSession ), name );
		}
	}

	[TestMethod]
	public void DemolishingTheRoomInTheOrderOfTheRealPathStartsNoSwimForTheGuestsChangingInside()
	{
		var rig = new Rig( capacity: 4 );
		var swimmer = rig.Send();
		var changing = rig.Send();
		var third = rig.Send();
		// one guest is in the water while another is still changing inside the room
		rig.Until( () => swimmer.State == GuestState.Swimming && rig.Simulation.Guests.Any( guest => guest.State == GuestState.Using ), 800 );
		var aboard = rig.Simulation.Guests.Where( guest => guest.State == GuestState.Using ).ToList();
		var randomBefore = rig.Simulation.CaptureSnapshot().RandomState;
		var swimmersBefore = rig.Simulation.Guests.Count( guest => guest.InSwimSession );
		// OriginalObject.OnDelete -> OriginalObjectRuntime.Stop (marks the visitors stopped, then releases everyone aboard); then ParkObjects.ObjectRemoved -> GuestSimulation.Unregister
		var happiness = aboard.Select( guest => guest.Happiness ).ToArray();
		rig.Ride.IsStopped = true;
		rig.Ride.ReleaseAll();
		Assert.AreEqual( randomBefore, rig.Simulation.CaptureSnapshot().RandomState, "no swim was seeded: the world generator was not drawn" );
		for ( var index = 0; index < aboard.Count; index++ )
		{
			// tp.exe's removal (0x004FB360) runs no ExitRide and no use routine for a rider: the small happiness step, no attraction named, state 6 on the exit cell (QUEUE-010)
			Assert.AreNotEqual( GuestState.Swimming, aboard[index].State, "released by the demolition: no swim starts" );
			Assert.AreEqual( GuestState.DoingNothing, aboard[index].State );
			Assert.AreEqual( 0, aboard[index].AttractionId );
			Assert.AreEqual( (2, 0), aboard[index].Cell );
			Assert.AreEqual( MeterMath.Add( happiness[index], -(float)(rig.Simulation.Settings.SmallHappinessChange & 0xFF) ), aboard[index].Happiness, "the removal's small step" );
		}
		var after = aboard.Select( guest => guest.Happiness ).ToArray();
		rig.Simulation.Unregister( rig.Ride );
		CollectionAssert.AreEqual( after, aboard.Select( guest => guest.Happiness ).ToArray(), "no second treatment by the unregistering" );
		Assert.IsTrue( swimmersBefore >= 1 );
		Assert.IsTrue( rig.Simulation.Guests.All( guest => !guest.InSwimSession ), "the swimmers who were in the water are out as well" );
		Assert.IsFalse( rig.Simulation.IsChangingIn( changing ) || rig.Simulation.IsChangingIn( third ) );
	}

	[TestMethod]
	public void ARoomThatHasStoppedReleasesItsGuestsOnFootAndAFullPoolDoesNotTakeMoreSwimmers()
	{
		var rig = new Rig( capacity: 8 );
		// the water shrinks to two cells: the pool holds the minimum capacity of two swimmers
		foreach ( var (x, y) in rig.Region.Cells.ToArray() )
			if ( (x, y) is not ((5, 3) or (5, 4)) )
				rig.Cells.SetType( x, y, ParkCellType.Empty );
		Assert.AreEqual( 2, rig.Region.Cells.Count );
		Assert.AreEqual( PoolSwimSettings.MinimumCapacity, rig.Region.Capacity, "two cells hold the minimum, not 2 x 1 swimmers" );
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
		var peak = 0;
		rig.Until( () =>
		{
			peak = Math.Max( peak, rig.Simulation.Guests.Count( guest => guest.IsSwimming ) );
			return guests.All( guest => guest.LastAttractionId == rig.Ride.AttractionId && guest.State != GuestState.Using );
		}, 600 );
		Assert.AreEqual( PoolSwimSettings.MinimumCapacity, peak, "the pool filled to its capacity and no further" );
		Assert.AreEqual( 6 - PoolSwimSettings.MinimumCapacity, guests.Count( guest => !guest.InSwimSession ), "the guests who found it full left the room the normal way" );
		Assert.IsTrue( guests.Where( guest => !guest.InSwimSession ).All( guest => guest.State is GuestState.ExitingRide or GuestState.DoingNothing or GuestState.WalkingAround && guest.AttractionId == rig.Ride.AttractionId ), "on foot at the room's exit, the target kept (GUEST-004)" );
		// a big pool scales with its cells: one body per water cell
		var big = new Rig();
		Assert.AreEqual( 36, big.Region.Capacity );
		var single = new Rig();
		foreach ( var (x, y) in single.Region.Cells.ToArray() )
			if ( (x, y) != (5, 3) )
				single.Cells.SetType( x, y, ParkCellType.Empty );
		Assert.AreEqual( PoolSwimSettings.MinimumCapacity, single.Region.Capacity );
	}

	[TestMethod]
	public void SwimmersCountInTheParkCensusAndAreNotAskedToBeOnAParkCell()
	{
		var rig = new Rig();
		var guest = rig.Send();
		var walker = rig.Simulation.SpawnInPark( 2, 0 );
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		Assert.IsFalse( rig.Simulation.IsOnParkCell( guest ), "the water is no counted cell type" );
		Assert.IsTrue( rig.Simulation.IsCountedInPark( guest ), "but a swimmer is in the park" );
		var census = ParkGate.CountCensus( rig.Simulation );
		Assert.AreEqual( rig.Simulation.Guests.Count( other => other.State != GuestState.Gone && ( other.InSwimSession || rig.Simulation.IsOnParkCell( other ) ) ), census );
		Assert.IsTrue( census >= 2, $"the swimmer and the walker ({walker.Id}) both count ({census})" );
		// the gate cannot close under a swimmer climbing out, nor a guest changing back
		guest.SwimTurns = 1;
		rig.Until( () => guest.State == GuestState.ChangingOut, 400 );
		Assert.IsTrue( ParkGate.CountCensus( rig.Simulation ) >= 2 );
		rig.Until( () => !guest.InSwimSession, 200 );
		// with no swim session nothing differs from a cell test: the same count a park without a room gets
		Assert.AreEqual( rig.Simulation.Guests.Count( other => other.State != GuestState.Gone && rig.Simulation.IsOnParkCell( other ) ), ParkGate.CountCensus( rig.Simulation ) );
	}

	[TestMethod]
	public void AClosedParkSendsTheSwimmersOutOfTheWater()
	{
		var payments = new QueueTests.ClosedPayments();
		var rig = new Rig();
		rig.Simulation.Payments = payments;
		var guest = rig.Send();
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		rig.Turns( 5 );
		Assert.AreEqual( GuestState.Swimming, guest.State, "an open park lets it swim" );
		payments.Closed = true;
		rig.Turns( 2 );
		Assert.AreEqual( GuestState.ClimbingOut, guest.State, "the park closed: it swims to the steps" );
		rig.Until( () => !guest.InSwimSession, 400 );
		Assert.AreEqual( 0, guest.AttractionId );
		// a guest released while the park is closed does not swim at all
		var late = rig.Send();
		rig.Until( () => late.State is GuestState.ExitingRide or GuestState.DoingNothing || late.State == GuestState.Swimming, 600 );
		Assert.AreNotEqual( GuestState.Swimming, late.State, "a closed park sends a guest straight on" );
	}

	[TestMethod]
	public void ARoomWhosePoolWasErasedLetsItsGuestsWalkOnAndDropsASwimmerWhoseWaterWentAway()
	{
		var rig = new Rig();
		// a room with no water: the guest changes and walks on
		foreach ( var (x, y) in rig.Region.Cells.ToArray() )
			rig.Cells.SetType( x, y, ParkCellType.Empty );
		var dry = rig.Send();
		// ExitRide (0x005014E0) walks it out of the room to the exit cell (2, 0) in state 15, then it decides again
		rig.Until( () => dry.State is GuestState.DoingNothing or GuestState.WalkingAround || dry.State == GuestState.Swimming, 900 );
		Assert.AreNotEqual( GuestState.Swimming, dry.State );
		Assert.AreEqual( (2, 0), dry.Cell );
		Assert.AreEqual( rig.Ride.AttractionId, dry.AttractionId, "the exit walk keeps the target until the next choice (GUEST-004)" );
		// a swimmer whose pool goes while it swims is put out at once
		var rig2 = new Rig();
		var wet = rig2.Send();
		rig2.Until( () => wet.State == GuestState.Swimming, 400 );
		foreach ( var (x, y) in rig2.Region.Cells.ToArray() )
			rig2.Cells.SetType( x, y, ParkCellType.Empty );
		rig2.Turns( 2 );
		Assert.IsFalse( wet.InSwimSession );
		Assert.AreEqual( 0, wet.AttractionId );
		Assert.IsTrue( rig2.Simulation.Grid.IsWalkable( wet.CellX, wet.CellY ) );
	}

	[TestMethod]
	public void ASwimmerWhoseWaterShrinksIsPutBackInTheWater()
	{
		var rig = new Rig();
		var guest = rig.Send();
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		rig.Turns( 20 );
		// the pool loses its far half: nobody swims there any more; whoever was there is back in the water within a turn
		for ( var y = 3; y <= 8; y++ )
			for ( var x = 8; x <= 10; x++ )
				rig.Cells.SetType( x, y, ParkCellType.Empty );
		for ( var turn = 0; turn < 300 && guest.IsSwimming; turn++ )
		{
			rig.Turns( 1 );
			if ( guest.IsSwimming )
				Assert.IsTrue( InWater( rig, guest ), $"turn {turn}" );
		}
	}

	[TestMethod]
	public void AGuestWhoWantsToLeaveFinishesItsSwimFirstAndThenGoesHome()
	{
		var rig = new Rig();
		var guest = rig.Send();
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		// the exit counter and the happiness are spent: a guest on the path would decide to go home at once
		guest.ExitLevel = -3;
		guest.Happiness = 0;
		var swimTurns = 0;
		rig.Until( () =>
		{
			Assert.IsTrue( guest.InSwimSession || guest.State == GuestState.ExitingRide || guest.State == GuestState.DoingNothing || guest.State == GuestState.LeavingPark, $"{guest.State}" );
			swimTurns += guest.IsSwimming ? 1 : 0;
			return !guest.InSwimSession;
		}, 900 );
		Assert.IsTrue( swimTurns > 60, $"it swam ({swimTurns} turns) instead of leaving the water at once" );
		rig.Until( () => guest.State is GuestState.LeavingPark or GuestState.Gone or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome, 400 );
	}

	[TestMethod]
	public void StaffAreNeverPutDownInThePool()
	{
		var rig = new Rig();
		Assert.IsFalse( StaffPlacementRules.IsValidDropCell( rig.Cells, 7, 5, null ), "a pool cell is none of the drop types" );
		Assert.IsFalse( StaffPlacementRules.IsPickUpCellType( ParkCellTypes.PoolWater ) );
		Assert.IsTrue( StaffPlacementRules.IsValidDropCell( rig.Cells, 1, 1, null ), "control: open ground" );
	}

	[TestMethod]
	public void ASwimmersBalloonIsPutAwayForTheSwimAndComesBackWhenItStepsOut()
	{
		var rig = new Rig();
		var guest = rig.Send();
		(guest.HasBalloon, guest.BalloonLife) = (true, GuestAppearance.MaximumBalloonLife);
		var seenWithBalloon = false;
		rig.Until( () =>
		{
			seenWithBalloon |= guest.InSwimSession && guest.HasBalloon;
			return !guest.InSwimSession && guest.State != GuestState.Using && guest.LastAttractionId == rig.Ride.AttractionId;
		}, 1200 );
		Assert.IsFalse( seenWithBalloon, "no balloon while changing, swimming or changing back" );
		Assert.IsTrue( guest.HasBalloon && guest.BalloonLife > 0, "back on the path it has its balloon again" );
	}

	[TestMethod]
	public void SwimmersOfTwoRoomsInOnePoolAvoidEachOther()
	{
		var rig = new Rig( capacity: 3 );
		// a second changing room whose pool-side cell lies in the same pool
		var second = new RideVisitorBridge( 2, "Second room", RideVisitorKind.Facility, 3, 55, 30, GuestNeeds.None ) { EntranceCell = (2, 0), ExitCell = (2, 0), IsChangingRoom = true, PoolAccessCandidates = new[] { (10, 3) } };
		var scriptEffects = new VisitorRideScriptEffects( second, UnimplementedRideScriptEffects.Instance );
		var script = GuestTests.CreateShopScript( scriptEffects );
		second.Attach( script, () => true );
		rig.Simulation.Register( second );
		Assert.AreEqual( 36, rig.Simulation.SwimRegionOf( second )!.Cells.Count, "the same 36 cells of water" );
		var a = rig.Send();
		var b = rig.Send();
		rig.Until( () => a.State == GuestState.Swimming && b.State == GuestState.Swimming, 500 );
		b.AttractionId = second.AttractionId;   // b swims out of the second room
		var closest = double.MaxValue;
		rig.Turns( 150, () => closest = Math.Min( closest, Math.Sqrt( Math.Pow( a.X - b.X, 2 ) + Math.Pow( a.Y - b.Y, 2 ) ) ) );
		Assert.IsTrue( closest >= 0.15, $"the two swimmers of different rooms kept apart ({closest:F2})" );
	}

	// ---- Saves ----------------------------------------------------------------------------------------------------

	private static Rig Restored( GuestSimulationSnapshot snapshot )
	{
		var rig = new Rig();
		rig.Simulation.RestoreSnapshot( snapshot );
		return rig;
	}

	[TestMethod]
	public void ASaveInTheMiddleOfASwimRestoresAndContinuesWithTheSameHash()
	{
		foreach ( var (name, stopAt) in new (string, Func<List<Guest>, bool>)[]
		{
			("everyone swimming", guests => guests.All( guest => guest.State == GuestState.Swimming && guest.SwimTurns < 120 )),
			("one climbing out", guests => guests.Any( guest => guest.State == GuestState.ClimbingOut )),
			("one changing back", guests => guests.Any( guest => guest.State == GuestState.ChangingOut ))
		} )
		{
			var rig = new Rig( capacity: 4 );
			var guests = Enumerable.Range( 0, 4 ).Select( _ => rig.Send() ).ToList();
			// the rooms' scripts are not in the guest snapshot, so the session is saved once nobody is aboard or queueing: every guest is in the water or on the path
			rig.Until( () => guests.All( guest => guest.InSwimSession ), 1500 );
			rig.Until( () => stopAt( guests ), 1500 );
			var json = System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
			var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( System.Text.Encoding.UTF8.GetBytes( json ) );
			Assert.AreEqual( json, System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( snapshot ) ), $"{name}: the snapshot round-trips as JSON" );
			var copy = Restored( snapshot );
			Assert.AreEqual( rig.Simulation.ComputeStateHash(), copy.Simulation.ComputeStateHash(), $"{name}: the restored simulation hashes as the original" );
			Assert.IsTrue( stopAt( copy.Simulation.Guests.ToList() ), $"{name}: the same moment of the session" );
			// both run on tick for tick, the original uninterrupted: the hash agrees at the end of every park turn, through the avoidance, the climb out, the change and the walk away
			var turns = 0;
			for ( var tick = 0; tick < 400 * 15 + 30; tick++ )
			{
				rig.Simulation.Tick( Tick );
				copy.Simulation.Tick( Tick );
				if ( rig.Simulation.ParkTurn == turns )
					continue;
				turns = (int)rig.Simulation.ParkTurn;
				Assert.AreEqual( rig.Simulation.ComputeStateHash(), copy.Simulation.ComputeStateHash(), $"{name}: park turn {turns} after the restore" );
			}
			Assert.IsTrue( turns >= 390, $"{name}: ran {turns} turns" );
		}
	}

	[TestMethod]
	public void ARestoredSwimmerSwimsTheSameStrokesAsTheOriginal()
	{
		var rig = new Rig();
		var guest = rig.Send();
		rig.Until( () => guest.State == GuestState.Swimming && guest.SwimTurns < 100, 800 );
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
		var copy = Restored( snapshot );
		var twin = copy.Simulation.Guests.Single();
		var tickAfter = rig.Simulation.TickCount;
		Assert.AreEqual( tickAfter, copy.Simulation.TickCount );
		// only the swimmers' own steps: no arrival, no decision, so the two simulations step alike tick for tick
		for ( var tick = 0; tick < 4 * 60 && guest.IsSwimming; tick++ )
		{
			rig.Simulation.Tick( Tick );
			copy.Simulation.Tick( Tick );
			Assert.AreEqual( (guest.State, guest.Navigation.X, guest.Navigation.Y, guest.SwimHeading, guest.SwimTurns), (twin.State, twin.Navigation.X, twin.Navigation.Y, twin.SwimHeading, twin.SwimTurns), $"tick {tick}" );
		}
	}

	[TestMethod]
	public void AnOlderSnapshotWithoutSwimFieldsRestoresWithNoSwimmers()
	{
		var rig = new Rig();
		rig.Send();
		rig.Turns( 5 );
		var json = System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
		foreach ( var field in new[] { "SwimHeading", "SwimTurns", "SwimStroke", "SwimGoalX", "SwimGoalY", "SwimHasGoal", "SwimRandom" } )
			Assert.IsFalse( json.Contains( field, StringComparison.Ordinal ), $"{field} is not written outside a swim, so an older build's JSON equals this one's" );
		var copy = Restored( ParkResumeJson.Deserialize<GuestSimulationSnapshot>( System.Text.Encoding.UTF8.GetBytes( json ) ) );
		Assert.IsTrue( copy.Simulation.Guests.All( guest => !guest.InSwimSession && guest.SwimTurns == 0 ) );
	}

	[TestMethod]
	public void TheSnapshotValidatorRefusesASwimSessionThatCannotBeRestored()
	{
		var rig = new Rig();
		var guest = rig.Send();
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		var good = rig.Simulation.CaptureSnapshot();
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( good ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( good ).Errors ) );
		GuestSimulationSnapshot With( Func<GuestSnapshot, GuestSnapshot> edit ) => good with { Guests = good.Guests.Select( edit ).ToArray() };
		Assert.IsFalse( rig.Simulation.ValidateSnapshot( With( item => item with { AttractionId = 0 } ) ).IsValid, "a swimmer without a changing room" );
		Assert.IsFalse( rig.Simulation.ValidateSnapshot( With( item => item with { SwimHeading = 64 } ) ).IsValid, "a heading out of range" );
		Assert.IsFalse( rig.Simulation.ValidateSnapshot( With( item => item with { SwimStroke = 3 } ) ).IsValid, "a stroke out of range" );
		Assert.IsFalse( rig.Simulation.ValidateSnapshot( With( item => item with { SwimTurns = -1 } ) ).IsValid, "negative turns" );
		Assert.IsFalse( rig.Simulation.ValidateSnapshot( With( item => item with { State = GuestState.DoingNothing } ) ).IsValid, "swim fields outside a swim session" );
	}

	[TestMethod]
	public void TheSnapshotValidatorBoundsTheSwimTurnsOfEachStateByWhatTheSessionCanSet()
	{
		var rig = new Rig();
		var guest = rig.Send();
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		var good = rig.Simulation.CaptureSnapshot();
		bool Valid( GuestState state, int turns ) => rig.Simulation.ValidateSnapshot( good with { Guests = good.Guests.Select( item => item with { State = state, SwimTurns = turns } ).ToArray() } ).IsValid;
		var longest = PoolSwimSettings.MinimumTurns + PoolSwimSettings.ExtraTurns - 1;
		Assert.IsTrue( Valid( GuestState.Swimming, longest ) );
		Assert.IsFalse( Valid( GuestState.Swimming, longest + 1 ), "more than the longest swim" );
		Assert.IsFalse( Valid( GuestState.Swimming, 0 ), "a swim with no turn left has already become the climb out" );
		Assert.IsTrue( Valid( GuestState.ClimbingOut, GuestSimulation.ClimbOutLimitTurns ) );
		Assert.IsFalse( Valid( GuestState.ClimbingOut, GuestSimulation.ClimbOutLimitTurns + 1 ), "more than the climb-out limit" );
		Assert.IsTrue( Valid( GuestState.ChangingOut, PoolSwimSettings.ChangeOutTurns ) );
		Assert.IsFalse( Valid( GuestState.ChangingOut, PoolSwimSettings.ChangeOutTurns + 1 ), "more than the change duration" );
		Assert.IsFalse( Valid( GuestState.ChangingOut, int.MaxValue ), "an absurd count would keep the guest hidden for years" );
	}

	[TestMethod]
	public void TheSnapshotValidatorRefusesASwimmerBoundToAnAttractionThatIsNoChangingRoom()
	{
		var rig = new Rig();
		var guest = rig.Send();
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		var good = rig.Simulation.CaptureSnapshot();
		rig.Ride.IsChangingRoom = false;
		var errors = rig.Simulation.ValidateSnapshot( good ).Errors;
		Assert.IsTrue( errors.Any( error => error.Contains( "not a changing room", StringComparison.Ordinal ) ), string.Join( "; ", errors ) );
	}

	// ---- The hash ------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheSwimSessionJoinsTheStateHashOnlyWhileAGuestIsInOne()
	{
		var rig = new Rig();
		var guest = rig.Send();
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		var before = guest.SwimHeading;
		var hashSwimming = HashOf( rig );
		guest.SwimHeading = (guest.SwimHeading + 1) & 63;
		Assert.AreNotEqual( hashSwimming, HashOf( rig ), "the heading of a swimmer is part of the digest" );
		guest.SwimHeading = before;
		Assert.AreEqual( hashSwimming, HashOf( rig ) );
		rig.Until( () => !guest.InSwimSession, 900 );
		guest.SwimHeading = 9;
		var walking = HashOf( rig );
		guest.SwimHeading = 11;
		Assert.AreEqual( walking, HashOf( rig ), "outside a session the swim fields are not hashed (every park without a changing room keeps its pins)" );
	}

	private static ulong HashOf( Rig rig )
	{
		var hasher = new StateHasher();
		rig.Simulation.AddCanonicalState( hasher );
		return hasher.Value;
	}

	// ---- The choice --------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void ACheerfulGuestChoosesTheChangingRoomLikeAnyAttractionAndSwims()
	{
		// no forced target: a spawned guest decides on its own; the room is the only attraction in the park
		var rig = new Rig( configure: settings => settings.Types = new() { new GuestType( 55, 300, 1000 ) } );
		var guest = rig.Simulation.SpawnInPark( 2, 0 );
		var reached = false;
		rig.Until( () =>
		{
			reached |= guest.AttractionId == rig.Ride.AttractionId && guest.State is GuestState.GoingToRide or GuestState.MovingUpQueue or GuestState.Queueing or GuestState.Boarding or GuestState.WaitingToBoard or GuestState.Using;
			return guest.State == GuestState.Swimming;
		}, 2500 );
		Assert.IsTrue( reached, "the guest went to the changing room" );
	}
}
