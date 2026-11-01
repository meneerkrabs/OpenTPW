using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.PoolsSwimSessionTests;

namespace OpenTPW.Tests;

/// <summary>
/// Riding a water slide (docs/POOLS.md, "Water slides") against a real <see cref="GuestSimulation"/> with a real ride script, the swim session's pool of 6 x 6 cells and a slide built through the slide
/// world: a tower at (3, 5) whose stairs face the pool, a track north, east and south into the pool cell (6, 3).
/// </summary>
[TestClass]
public class PoolSlideActivityTests
{
	/// <summary>The swim session's rig with a slide: tower footprint (3, 5) to (4, 6), pool-side cells (5, 5) and (5, 6), track (3, 4) (3, 3) (3, 2) (4, 2) (5, 2) (6, 2), splash cell (6, 3).</summary>
	internal sealed class SlideRig
	{
		public readonly Rig Rig;
		public readonly PoolSlideTower Tower;
		public readonly PoolSlideWorld World;
		public readonly PoolSlideActivity Activity = new();
		public static readonly (int X, int Y)[] Track = { (3, 4), (3, 3), (3, 2), (4, 2), (5, 2), (6, 2), (6, 3) };

		public SlideRig( int capacity = 4, bool build = true, Rig? rig = null, PoolSlideProfile profile = PoolSlideProfile.Flume )
		{
			Rig = rig ?? new Rig();
			Tower = new PoolSlideTower( 3, 5, new[] { (3, 5), (4, 5), (3, 6), (4, 6) }, new[] { (5, 5), (5, 6) }, profile, capacity, ThingId: 700 );
			var cells = Rig.Cells;
			World = new PoolSlideWorld( () => new PoolSlideSite
			{
				Cells = cells, Ground = ( _, _ ) => 0f, WaterLevel = ( x, y ) => cells.InBounds( x, y ) && cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater ? -0.12f : null
			}, id => id == Tower.Id ? Tower : null, () => null );
			Rig.Simulation.PoolSlides = World;
			Rig.Simulation.RegisterPoolActivity( Activity );
			if ( build )
				foreach ( var (x, y) in Track )
					Assert.AreEqual( PoolSlideResult.Ok, World.AddCell( Tower.X, Tower.Y, x, y ), $"({x}, {y})" );
		}

		public PoolSlide Slide => World.Find( Tower.Id )!;
		public IEnumerable<Guest> OnSlide => Rig.Simulation.Guests.Where( guest => guest.InPoolActivity && guest.PoolActivityTarget == Tower.ThingId );
		public ulong Hash()
		{
			var hasher = new StateHasher();
			Rig.Simulation.AddCanonicalState( hasher );
			return hasher.Value;
		}
	}

	private static IDisposable PoolsOn() => OpenTPW.Mods.OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );

	[TestMethod]
	public void ASwimmerSwimsToTheStairsClimbsWaitsRidesTheTrackAndSplashesDownIntoThePool()
	{
		using var scope = PoolsOn();
		var rig = new SlideRig();
		var guest = rig.Rig.Send();
		rig.Rig.Until( () => guest.State == GuestState.PoolActivity, 3000 );
		Assert.AreEqual( PoolSlideActivity.ActivityId, guest.PoolActivityId );
		Assert.AreEqual( rig.Tower.ThingId, guest.PoolActivityTarget, "the target is the tower's thing id" );
		Assert.AreEqual( (5, 5), (guest.PoolActivityX >> 16, guest.PoolActivityY >> 16), "offered at the foot of the stairs" );
		var phases = new List<int> { guest.PoolActivityPhase };
		var slots = new HashSet<int>();
		var rideTurns = 0;
		var slide = rig.Slide;
		var feetClimb = new List<float>();
		var feetRide = new List<float>();
		rig.Rig.Until( () =>
		{
			if ( !guest.InPoolActivity )
				return true;
			if ( phases[^1] != guest.PoolActivityPhase )
				phases.Add( guest.PoolActivityPhase );
			slots.Add( rig.Activity.SpriteSlot( guest ) );
			var feet = rig.Activity.FeetHeight( rig.Rig.Simulation, guest, new PoolFeetHeights( -0.12f, -0.12f, 0f ) );
			switch ( guest.PoolActivityPhase )
			{
				case PoolSlidePhases.Approach:
					Assert.IsTrue( rig.Rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ), "swimming to the stairs" );
					Assert.IsNull( feet );
					break;
				case PoolSlidePhases.Climb:
					feetClimb.Add( feet!.Value );
					break;
				case PoolSlidePhases.Wait:
					Assert.AreEqual( slide.TopHeight / 1000f, feet!.Value, 1e-4, "on the platform" );
					break;
				case PoolSlidePhases.Ride:
				{
					rideTurns++;
					var point = slide.PointAt( slide.PositionAtTurn( guest.PoolActivityTurns ) );
					if ( guest.PoolActivityTurns > 0 )
						Assert.AreEqual( (point.X * 64, point.Y * 64), (guest.Navigation.X, guest.Navigation.Y), $"ride turn {guest.PoolActivityTurns} is on the track" );
					feetRide.Add( feet!.Value );
					break;
				}
			}
			return false;
		}, 800 );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4 }, phases, "approach, climb, wait, ride, splash" );
		CollectionAssert.AreEquivalent( new[] { PoolSwimSlots.Crawl, PoolSwimSlots.LadderClimb, PoolSwimSlots.Walk, PoolSwimSlots.Stand, PoolSwimSlots.Sliding, PoolSwimSlots.SplashDown }, slots.ToArray() );
		Assert.AreEqual( PoolSlideSettings.ClimbTurns, feetClimb.Count, "the climb takes its turns" );
		Assert.IsTrue( feetClimb.Zip( feetClimb.Skip( 1 ) ).All( pair => pair.Second >= pair.First ), "and rises" );
		Assert.AreEqual( slide.TopHeight / 1000f, feetClimb[^1], 1e-4, "up to the deck" );
		Assert.IsTrue( feetRide.Zip( feetRide.Skip( 1 ) ).All( pair => pair.Second < pair.First ), "the ride falls" );
		Assert.AreEqual( slide.RideTurns, rideTurns, "the ride lasts as long as the slide's table says" );
		Assert.AreEqual( GuestState.Swimming, guest.State, "back in the water" );
		Assert.IsTrue( rig.Rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ) );
		Assert.AreEqual( (6, 3), guest.Navigation.Cell, "at the splash cell" );
		Assert.IsNull( guest.PoolActivityId );
		Assert.IsTrue( guest.SwimTurns > 0 );
		rig.Rig.Until( () => !guest.InSwimSession, 1500 );
		Assert.AreEqual( 0, guest.AttractionId, "and the session goes on to its end: changing, then the path" );
	}

	[TestMethod]
	public void NothingIsOfferedWithoutAUsableSlideInTheSwimmersWater()
	{
		using var scope = PoolsOn();
		var empty = new SlideRig( build: false );
		var guest = empty.Rig.Send();
		empty.Rig.Until( () => guest.IsSwimming, 600 );
		var region = empty.Rig.Region;
		Assert.IsNull( empty.Activity.Offer( empty.Rig.Simulation, guest, region ), "no slide" );
		foreach ( var (x, y) in SlideRig.Track.Take( 6 ) )
			empty.World.AddCell( empty.Tower.X, empty.Tower.Y, x, y );
		Assert.IsNull( empty.Activity.Offer( empty.Rig.Simulation, guest, region ), "a track with an open end" );
		empty.World.AddCell( empty.Tower.X, empty.Tower.Y, 6, 3 );
		var offer = empty.Activity.Offer( empty.Rig.Simulation, guest, region );
		Assert.AreEqual( new PoolActivityOffer( empty.Tower.ThingId, 5, 5 ), offer );
		empty.Rig.Cells.SetType( 6, 3, ParkCellType.Empty );
		Assert.IsNull( empty.Activity.Offer( empty.Rig.Simulation, guest, region ), "the splash cell is no longer water" );
		empty.Rig.Cells.SetType( 6, 3, ParkCellTypes.PoolWater );
		Assert.IsNotNull( empty.Activity.Offer( empty.Rig.Simulation, guest, region ) );
		// another tower's pool is another water: a region that does not hold the foot of the stairs gets no offer
		var elsewhere = PoolSwimRegion.Build( empty.Rig.Cells, (10, 8) )!;
		Assert.IsNotNull( elsewhere );
		empty.Rig.Cells.SetType( 5, 5, ParkCellType.Empty );
		empty.Rig.Cells.SetType( 5, 6, ParkCellType.Empty );
		Assert.IsNull( empty.Activity.Offer( empty.Rig.Simulation, guest, empty.Rig.Region ), "no water at the foot of the stairs" );
	}

	[TestMethod]
	public void TheTowerHoldsAtMostItsCapacityAndTheLongestWaitingGuestGoesFirstWithAGapBetweenRidersAndNobodySharesASpot()
	{
		using var scope = PoolsOn();
		var rig = new SlideRig( capacity: 3 );
		var guests = Enumerable.Range( 0, 7 ).Select( _ => rig.Rig.Send() ).ToList();
		var mostOccupied = 0;
		var mostOnTrack = 0;
		var launches = new List<(int Turn, int Id)>();
		var arrived = new Dictionary<int, int>();
		var seen = new HashSet<int>();
		var turn = 0;
		var minimumGap = int.MaxValue;
		var deckSpots = Enumerable.Range( 0, 4 ).Select( rank => rig.Tower.ModelPoint( PoolSlideTowerModel.Spot( rank ).X, PoolSlideTowerModel.Spot( rank ).Y ) ).ToHashSet();
		rig.Rig.Turns( 2400, () =>
		{
			turn++;
			mostOccupied = Math.Max( mostOccupied, PoolSlideActivity.Occupants( rig.Rig.Simulation, rig.Tower.ThingId ) );
			var inTower = rig.OnSlide.Where( guest => guest.PoolActivityPhase == PoolSlidePhases.Wait ).ToList();
			foreach ( var waiting in inTower )
				arrived.TryAdd( waiting.Id, turn );
			var spots = inTower.Select( guest => (guest.Navigation.X, guest.Navigation.Y) ).ToList();
			Assert.AreEqual( spots.Count, spots.Distinct().Count(), $"turn {turn}: two waiting guests share a spot" );
			Assert.IsTrue( spots.All( spot => deckSpots.Contains( spot ) ), $"turn {turn}: a waiting guest is not on one of the deck's spots" );
			var riders = rig.OnSlide.Where( guest => guest.PoolActivityPhase == PoolSlidePhases.Ride ).ToList();
			mostOnTrack = Math.Max( mostOnTrack, riders.Count );
			foreach ( var rider in riders )
				if ( seen.Add( rider.Id ) )
				{
					launches.Add( (turn, rider.Id) );
					// the landing water is free at the launch: nobody within (about) 0.6 cell of the landing point
					var landing = rig.Slide.LandingPoint!.Value;
					foreach ( var other in rig.Rig.Simulation.Guests.Where( other => other != rider && (other.IsSwimming || other.InPoolActivity) && !(other.InPoolActivity && other.PoolActivityPhase is PoolSlidePhases.Climb or PoolSlidePhases.Wait) ) )
					{
						var (dx, dy) = ((other.Navigation.X - landing.X * 64L) / 65536.0, (other.Navigation.Y - landing.Y * 64L) / 65536.0);
						Assert.IsTrue( Math.Sqrt( dx * dx + dy * dy ) >= 0.5, $"turn {turn}: guest {other.Id} is {Math.Sqrt( dx * dx + dy * dy ):F2} cell from where guest {rider.Id} will land" );
					}
				}
			var positions = riders.Select( rider => rig.Slide.PositionAtTurn( rider.PoolActivityTurns ) ).OrderBy( p => p ).ToList();
			for ( var index = 1; index < positions.Count; index++ )
				minimumGap = Math.Min( minimumGap, positions[index] - positions[index - 1] );
		} );
		Assert.AreEqual( 3, mostOccupied, "never more than the capacity, and it filled up" );
		Assert.IsTrue( launches.Count >= 5, $"{launches.Count} guests rode" );
		Assert.IsTrue( minimumGap >= PoolSlideSettings.Gap, $"riders on the track kept {minimumGap} apart, at least {PoolSlideSettings.Gap}" );
		Assert.IsTrue( mostOnTrack >= 1 );
		Assert.IsTrue( launches.Zip( launches.Skip( 1 ) ).All( pair => pair.Second.Turn > pair.First.Turn ), "one launch per turn at most" );
		// the longest waiting goes first: the launch order is the order of arrival on the platform, ties by guest id
		var expected = launches.OrderBy( launch => arrived[launch.Id] ).ThenBy( launch => launch.Id ).Select( launch => launch.Id ).ToList();
		CollectionAssert.AreEqual( expected, launches.Select( launch => launch.Id ).ToList(), "longest waiting first" );
	}

	[TestMethod]
	public void TheClimbFollowsTheStairsOfTheModelAndTheWaitingSpotsAreOnTheDeckForEveryRotation()
	{
		var shape = new ObjectShape( new[] { "**", "**" } );
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var footprint = PoolSlideTowers.Footprint( shape, 10, 10, rotation );
			var tower = new PoolSlideTower( 10, 10, footprint, PoolSlideTowers.PoolSideCandidates( shape, 10, 10, rotation ), PoolSlideProfile.Flume, 4 );
			Assert.AreEqual( rotation, tower.Rotation, "the rotation is read back from the pool side" );
			var centre = tower.ModelPoint( 100, 100 );
			var middle = ((int)footprint.Average( cell => PoolSwimRegion.Centre( cell ).X ), (int)footprint.Average( cell => PoolSwimRegion.Centre( cell ).Y ));
			Assert.AreEqual( middle, centre, $"{rotation}: the model's middle is the footprint's middle" );
			var (fx, fy) = ObjectFootprint.RotateDirection( rotation, 0, -1 );
			var (sx, sy) = ObjectFootprint.RotateDirection( rotation, 1, 0 );
			// the foot of the stairs is on the front edge (a cell from the middle towards the pool) and 0.65 cell to the model's left of the middle: the stairs' column is the footprint's first column
			var foot = tower.ModelPoint( PoolSlideTowerModel.StairsX, PoolSlideTowerModel.StairsFootY );
			Assert.AreEqual( centre.X + fx * 65536 - sx * 42598, foot.X, 3, $"{rotation}: the foot of the stairs (x)" );
			Assert.AreEqual( centre.Y + fy * 65536 - sy * 42598, foot.Y, 3, $"{rotation}: the foot of the stairs (y)" );
			var top = tower.ModelPoint( PoolSlideTowerModel.StairsX, PoolSlideTowerModel.StairsTopY );
			Assert.AreEqual( centre.X - fx * 9830 - sx * 42598, top.X, 3, $"{rotation}: the top of the flight, 0.15 cell short of the middle towards the pool (x)" );
			Assert.AreEqual( centre.Y - fy * 9830 - sy * 42598, top.Y, 3, $"{rotation}: the top of the flight (y)" );
			// every waiting spot is on the deck: over the footprint, clear of the flight's opening, the posts and the rails
			for ( var rank = 0; rank < 4; rank++ )
			{
				var (x, y) = PoolSlideTowerModel.Spot( rank );
				Assert.IsTrue( x is > 20 and < 184 && y is > 15 and < 184, $"spot {rank} ({x}, {y}) inside the rails" );
				Assert.IsFalse( x < 85 && y > 75, $"spot {rank} ({x}, {y}) is over the flight's opening" );
				Assert.IsFalse( x > 175 && y > 170 || x > 175 && y < 20, $"spot {rank} is on a post" );
				var point = tower.ModelPoint( x, y );
				Assert.IsTrue( footprint.Contains( (point.X >> 16, point.Y >> 16) ), $"{rotation}: spot {rank} is over the footprint" );
			}
		}
	}

	[TestMethod]
	public void TheClimberWalksTheFlightFromItsFootToItsTopThenOntoTheDeck()
	{
		using var scope = PoolsOn();
		var rig = new SlideRig();
		var guest = rig.Rig.Send();
		rig.Rig.Until( () => guest.InPoolActivity && guest.PoolActivityPhase == PoolSlidePhases.Climb, 3000 );
		var foot = rig.Tower.ModelPoint( PoolSlideTowerModel.StairsX, PoolSlideTowerModel.StairsFootY );
		var top = rig.Tower.ModelPoint( PoolSlideTowerModel.StairsX, PoolSlideTowerModel.StairsTopY );
		var positions = new Dictionary<int, (int X, int Y)>();
		rig.Rig.Until( () =>
		{
			if ( guest.PoolActivityPhase != PoolSlidePhases.Climb )
				return true;
			positions[guest.PoolActivityTurns] = (guest.Navigation.X, guest.Navigation.Y);
			return false;
		}, 40 );
		Assert.AreEqual( foot, positions[2], "at the foot of the stairs after two turns" );
		Assert.AreEqual( top, positions[10], "at the top of the flight after ten" );
		// the flight is a straight line: every turn between the foot and the top moves an eighth of the way
		for ( var turn = 3; turn <= 9; turn++ )
		{
			Assert.AreEqual( foot.X + (long)(top.X - foot.X) * (turn - 2) / 8, positions[turn].X, 2, $"turn {turn}" );
			Assert.AreEqual( foot.Y + (long)(top.Y - foot.Y) * (turn - 2) / 8, positions[turn].Y, 2, $"turn {turn}" );
		}
	}

	[TestMethod]
	public void ARiderInsideATubeIsNotDrawnAndOneInAFlumeIs()
	{
		using var scope = PoolsOn();
		foreach ( var profile in new[] { PoolSlideProfile.Flume, PoolSlideProfile.Tube } )
		{
			var rig = new SlideRig( profile: profile );
			var guest = rig.Rig.Send();
			var simulation = rig.Rig.Simulation;
			var hiddenIn = new HashSet<int>();
			rig.Rig.Until( () => guest.InPoolActivity, 3000 );
			rig.Rig.Until( () =>
			{
				if ( !guest.InPoolActivity )
					return true;
				if ( PoolSlideActivity.IsInsideTube( simulation, guest ) )
					hiddenIn.Add( guest.PoolActivityPhase );
				return false;
			}, 4000 );
			if ( profile == PoolSlideProfile.Tube )
				CollectionAssert.AreEquivalent( new[] { PoolSlidePhases.Ride }, hiddenIn.ToArray(), "hidden on the track only: seen on the platform before and in the water after" );
			else
				Assert.AreEqual( 0, hiddenIn.Count, "the open flume shows its riders" );
		}
	}

	[TestMethod]
	public void AGuestAtTheFrontOfTheLineWaitsWhileASwimmerIsWithin06CellOfTheLandingPoint()
	{
		using var scope = PoolsOn();
		var rig = new SlideRig();
		var rider = rig.Rig.Send();
		rig.Rig.Until( () => rider.InPoolActivity && rider.PoolActivityPhase == PoolSlidePhases.Wait, 3000 );
		// a swimmer parked at the landing point holds the launch: put the waiter back to the start of its wait with the zone occupied
		var blocker = rig.Rig.Send();
		rig.Rig.Until( () => blocker.IsSwimming, 3000 );
		var landing = rig.Slide.LandingPoint!.Value;
		var waited = rider.PoolActivityTurns;
		Assert.IsTrue( waited >= 0 );
		(rider.PoolActivityPhase, rider.PoolActivityTurns) = (PoolSlidePhases.Wait, 0);
		var launched = false;
		for ( var turn = 0; turn < 12; turn++ )
		{
			rig.Rig.Simulation.PlacePoolGuest( blocker, landing.X * 64, landing.Y * 64 );
			rig.Rig.Turns( 1 );
			launched |= rider.PoolActivityPhase == PoolSlidePhases.Ride;
		}
		Assert.IsFalse( launched, "no launch while a swimmer sits in the landing zone" );
	}

	/// <summary>
	/// [EXT:pools-slides] The tower's launch reads a rider the sweep has already updated in this turn with its counter as it was at the start of the turn (<see cref="PoolSlideActivity"/>'s IsStartClear, as RankInLine does for the
	/// line): a guest at the head of the line launches or waits the same whether the sweep reaches the rider first or last. Both orders of the two guests are run from the same state, for a rider just short of the gap
	/// and for one just past it.
	/// </summary>
	[TestMethod]
	public void TheLaunchDoesNotDependOnTheOrderTheSweepReachesTheGuestsIn()
	{
		using var scope = PoolsOn();
		var rig = new SlideRig();
		var simulation = rig.Rig.Simulation;
		rig.Rig.Send();
		rig.Rig.Send();
		rig.Rig.Until( () => rig.OnSlide.Any( guest => guest.PoolActivityPhase == PoolSlidePhases.Ride ) && rig.OnSlide.Any( guest => guest.PoolActivityPhase == PoolSlidePhases.Wait ), 3000 );
		var rider = rig.OnSlide.First( guest => guest.PoolActivityPhase == PoolSlidePhases.Ride );
		var waiter = rig.OnSlide.First( guest => guest.PoolActivityPhase == PoolSlidePhases.Wait );
		var waited = waiter.PoolActivityTurns;
		// the rider's last counter that is still short of the gap, and the one after it, which is past it
		var short_ = Enumerable.Range( 1, 200 ).First( turns => rig.Slide.PositionAtTurn( turns ) < PoolSlideSettings.Gap && rig.Slide.PositionAtTurn( turns + 1 ) >= PoolSlideSettings.Gap );
		const float tick = 1f / FixedStepClock.TicksPerSecond;
		bool Launches( int riderTurns, bool riderFirst )
		{
			while ( !simulation.NextTickStartsTurn )
				simulation.Tick( tick );
			(rider.PoolActivityPhase, rider.PoolActivityTurns) = (PoolSlidePhases.Ride, riderTurns);
			(waiter.PoolActivityPhase, waiter.PoolActivityTurns) = (PoolSlidePhases.Wait, waited);
			simulation.BeginSweepTick( tick );
			foreach ( var guest in riderFirst ? new[] { rider, waiter } : new[] { waiter, rider } )
				simulation.SweepGuest( guest.Id );
			simulation.FinishSweep();
			simulation.UpdateTurnEndArrivals();
			return waiter.PoolActivityPhase == PoolSlidePhases.Ride;
		}
		foreach ( var riderTurns in new[] { short_, short_ + 1 } )
		{
			var waiterFirst = Launches( riderTurns, riderFirst: false );
			var riderFirst = Launches( riderTurns, riderFirst: true );
			Assert.AreEqual( waiterFirst, riderFirst, $"the rider at counter {riderTurns}: the launch is the same in either order" );
			Assert.AreEqual( rig.Slide.PositionAtTurn( riderTurns ) >= PoolSlideSettings.Gap, waiterFirst, $"and it follows the rider's place at the start of the turn (counter {riderTurns})" );
		}
	}

	[TestMethod]
	public void AGuestRidingAnUnfinishedSlideLeavesTheSessionWithoutACrash()
	{
		using var scope = PoolsOn();
		var rig = new SlideRig( build: false );
		foreach ( var (x, y) in SlideRig.Track.Take( 6 ) )
			Assert.AreEqual( PoolSlideResult.Ok, rig.World.AddCell( rig.Tower.X, rig.Tower.Y, x, y ) );
		foreach ( var phase in new[] { PoolSlidePhases.Ride, PoolSlidePhases.Splash } )
		{
			var guest = rig.Rig.Send();
			rig.Rig.Until( () => guest.IsSwimming, 800 );
			(guest.State, guest.PoolActivityId, guest.PoolActivityTarget, guest.PoolActivityPhase, guest.PoolActivityTurns) = (GuestState.PoolActivity, PoolSlideActivity.ActivityId, rig.Tower.ThingId, phase, 0);
			(guest.PoolActivityX, guest.PoolActivityY) = PoolSwimRegion.Centre( (5, 5) );
			rig.Rig.Turns( 3 );
			Assert.IsFalse( guest.InPoolActivity, $"phase {phase}: out of the activity" );
		}
	}

	[TestMethod]
	public void DemolishingTheTowerEndsTheRidesAndTheQueueOnItAndTheGuestsLeaveTheWater()
	{
		using var scope = PoolsOn();
		foreach ( var phase in new[] { PoolSlidePhases.Approach, PoolSlidePhases.Climb, PoolSlidePhases.Wait, PoolSlidePhases.Ride, PoolSlidePhases.Splash } )
		{
			var rig = new SlideRig();
			var guest = rig.Rig.Send();
			rig.Rig.Until( () => guest.InPoolActivity && guest.PoolActivityPhase == phase, 4000 );
			var wasRiding = guest.PoolActivityPhase == PoolSlidePhases.Ride;
			rig.Rig.Simulation.AbortPoolActivitiesOf( rig.Tower.ThingId );
			Assert.IsTrue( rig.World.Remove( rig.Tower.X, rig.Tower.Y ) );
			rig.Rig.Turns( 2 );
			Assert.IsFalse( guest.InPoolActivity, $"phase {phase}: the activity is over ({wasRiding})" );
			Assert.IsNull( guest.PoolActivityId );
			rig.Rig.Turns( 400 );
			Assert.IsFalse( guest.InSwimSession, $"phase {phase}: the session ended" );
			Assert.AreEqual( 0, guest.AttractionId );
		}
	}

	[TestMethod]
	public void WhenTheParkClosesARiderOnTheTrackIsPutInTheWaterAtTheStepsAndClimbsOut()
	{
		using var scope = PoolsOn();
		foreach ( var phase in new[] { PoolSlidePhases.Wait, PoolSlidePhases.Ride } )
		{
			var rig = new SlideRig();
			var payments = new QueueTests.ClosedPayments();
			rig.Rig.Simulation.Payments = payments;
			var guest = rig.Rig.Send();
			rig.Rig.Until( () => guest.InPoolActivity && guest.PoolActivityPhase == phase && (phase != PoolSlidePhases.Ride || guest.PoolActivityTurns >= 3), 4000 );
			payments.Closed = true;
			rig.Rig.Turns( 2 );
			Assert.IsFalse( guest.InPoolActivity, $"phase {phase}: the activity is let go" );
			Assert.IsNull( guest.PoolActivityId );
			Assert.IsTrue( guest.State is GuestState.ClimbingOut or GuestState.ChangingOut or GuestState.ExitingRide or GuestState.DoingNothing, $"{guest.State}" );
			if ( guest.IsSwimming )
				Assert.IsTrue( rig.Rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ), "in the water, not in the air" );
			rig.Rig.Turns( 400 );
			Assert.IsFalse( guest.InSwimSession, "the session ends" );
		}
	}

	[TestMethod]
	public void ASlideThatStopsBeingUsableLeavesWaitingGuestsInTheWaterAndEndsASessionOnlyWhenThereIsNoWater()
	{
		using var scope = PoolsOn();
		var rig = new SlideRig();
		var guest = rig.Rig.Send();
		rig.Rig.Until( () => guest.InPoolActivity && guest.PoolActivityPhase == PoolSlidePhases.Wait, 4000 );
		rig.Rig.Cells.SetType( 6, 3, ParkCellType.Empty );
		rig.Rig.Turns( 2 );
		Assert.AreEqual( GuestState.Swimming, guest.State, "the unusable slide sends the waiting guest back into the water at the foot of the stairs" );
		Assert.IsTrue( rig.Rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ) );
		Assert.AreEqual( (5, 5), guest.Navigation.Cell );
		rig.Rig.Cells.SetType( 6, 3, ParkCellTypes.PoolWater );
		var other = new SlideRig();
		var second = other.Rig.Send();
		other.Rig.Until( () => second.InPoolActivity && second.PoolActivityPhase == PoolSlidePhases.Climb, 4000 );
		other.Rig.Cells.SetType( 5, 5, ParkCellType.Empty );
		other.Rig.Cells.SetType( 5, 6, ParkCellType.Empty );
		other.Rig.Turns( 3 );
		Assert.IsFalse( second.InPoolActivity, "a climber with no water at the foot of the stairs ends its activity" );
		Assert.IsTrue( other.Rig.Region.IsWater( second.Navigation.X, second.Navigation.Y ) || second.State is GuestState.ChangingOut or GuestState.ExitingRide or GuestState.DoingNothing );
	}

	[TestMethod]
	public void ASaveInTheMiddleOfARideRestoresAndTheRideContinuesWithTheSameHash()
	{
		using var scope = PoolsOn();
		var rig = new SlideRig();
		for ( var more = 0; more < 4; more++ )
			rig.Rig.Send();
		// a rider mid-ride and others on the stairs and the platform
		rig.Rig.Until( () => rig.OnSlide.Any( other => other.PoolActivityPhase == PoolSlidePhases.Ride && other.PoolActivityTurns >= 5 ) && rig.OnSlide.Any( other => other.PoolActivityPhase is PoolSlidePhases.Climb or PoolSlidePhases.Wait ), 6000 );
		var json = Encoding.UTF8.GetString( ParkResumeJson.Serialize( rig.Rig.Simulation.CaptureSnapshot() ) );
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( Encoding.UTF8.GetBytes( json ) );
		var slides = ParkResumeJson.Deserialize<PoolSlidesSaveData>( ParkResumeJson.Serialize( rig.World.CaptureSnapshot() ) );
		var copy = new SlideRig( build: false );
		copy.World.RestoreSnapshot( slides );
		Assert.IsTrue( copy.Rig.Simulation.ValidateSnapshot( snapshot ).IsValid, string.Join( "; ", copy.Rig.Simulation.ValidateSnapshot( snapshot ).Errors ) );
		copy.Rig.Simulation.RestoreSnapshot( snapshot );
		Assert.AreEqual( rig.Hash(), copy.Hash() );
		Assert.IsTrue( copy.Rig.Simulation.Guests.Count >= 2 && copy.OnSlide.Any( other => other.PoolActivityPhase == PoolSlidePhases.Ride ) && copy.OnSlide.Any( other => other.PoolActivityPhase is PoolSlidePhases.Climb or PoolSlidePhases.Wait ) );
		for ( var tick = 0; tick < 60 * 80; tick++ )
		{
			rig.Rig.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			copy.Rig.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			foreach ( var live in rig.Rig.Simulation.Guests )
			{
				var twin = copy.Rig.Simulation.Guests.Single( other => other.Id == live.Id );
				Assert.AreEqual( (live.State, live.PoolActivityPhase, live.PoolActivityTurns, live.Navigation.X, live.Navigation.Y), (twin.State, twin.PoolActivityPhase, twin.PoolActivityTurns, twin.Navigation.X, twin.Navigation.Y), $"tick {tick}, guest {live.Id}" );
			}
		}
		Assert.AreEqual( rig.Hash(), copy.Hash() );
	}

	[TestMethod]
	public void ASlideWorldWithoutSlidesAddsNothingToTheHashAndDrawsNothingFromTheSwimmersStream()
	{
		using var scope = PoolsOn();
		var plain = new Rig();
		var with = new SlideRig( build: false );
		var a = plain.Send();
		var b = with.Rig.Send();
		for ( var turn = 0; turn < 700; turn++ )
		{
			plain.Turns( 1 );
			with.Rig.Turns( 1 );
			var hasher = new StateHasher();
			plain.Simulation.AddCanonicalState( hasher );
			Assert.AreEqual( hasher.Value, with.Hash(), $"turn {turn}" );
			Assert.AreEqual( a.SwimRandom, b.SwimRandom, $"turn {turn}" );
		}
	}

	/// <summary>The fixed run: the slide rig with six guests sent one after the other, 2400 park turns.</summary>
	internal static (ulong Hash, int Rides, int MostWaiting) RunFixedSlides()
	{
		using var scope = PoolsOn();
		var rig = new SlideRig( capacity: 3 );
		var rides = new HashSet<int>();
		var waiting = 0;
		for ( var wave = 0; wave < 6; wave++ )
		{
			rig.Rig.Send();
			rig.Rig.Turns( 60, () =>
			{
				foreach ( var rider in rig.OnSlide.Where( guest => guest.PoolActivityPhase == PoolSlidePhases.Ride ) )
					rides.Add( rider.Id );
				waiting = Math.Max( waiting, PoolSlideActivity.Occupants( rig.Rig.Simulation, rig.Tower.ThingId ) );
			} );
		}
		rig.Rig.Turns( 1800, () =>
		{
			foreach ( var rider in rig.OnSlide.Where( guest => guest.PoolActivityPhase == PoolSlidePhases.Ride ) )
				rides.Add( rider.Id );
		} );
		return (rig.Hash(), rides.Count, waiting);
	}

	/// <summary>[EXT:pools-slides] The pinned hash of <see cref="RunFixedSlides"/> (docs/DETERMINISM.md, "Pools").</summary>
	internal const ulong PoolsSlidesFixedRunHash = 0x8A2B0B14564A3D55UL;

	[TestMethod]
	public void FixedRunWithASlideIsTheSameInEveryProcessAndGuestsRideIt()
	{
		var (hash, rides, waiting) = RunFixedSlides();
		Assert.IsTrue( rides >= 3, $"{rides} guests rode" );
		Assert.IsTrue( waiting >= 2, $"{waiting} at most in the tower" );
		Assert.AreEqual( hash, RunFixedSlides().Hash, "repeatable in the process" );
		Assert.AreEqual( PoolsSlidesFixedRunHash, hash, $"0x{hash:X16} ({rides} rides, {waiting} waiting)" );
	}
}
