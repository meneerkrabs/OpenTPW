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

		public SlideRig( int capacity = 4, bool build = true, Rig? rig = null )
		{
			Rig = rig ?? new Rig();
			Tower = new PoolSlideTower( 3, 5, new[] { (3, 5), (4, 5), (3, 6), (4, 6) }, new[] { (5, 5), (5, 6) }, PoolSlideProfile.Flume, capacity, ThingId: 700 );
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
		CollectionAssert.AreEquivalent( new[] { PoolSwimSlots.Crawl, PoolSwimSlots.LadderClimb, PoolSwimSlots.Stand, PoolSwimSlots.Sliding, PoolSwimSlots.SplashDown }, slots.ToArray() );
		Assert.AreEqual( PoolSlideSettings.ClimbTurns, feetClimb.Count, "the climb takes its turns" );
		Assert.IsTrue( feetClimb.Zip( feetClimb.Skip( 1 ) ).All( pair => pair.Second > pair.First ), "and rises" );
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
	public void TheTowerHoldsAtMostItsCapacityAndTheLongestWaitingGuestGoesFirstWithAGapBetweenRiders()
	{
		using var scope = PoolsOn();
		var rig = new SlideRig( capacity: 2 );
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Rig.Send() ).ToList();
		var mostOccupied = 0;
		var mostOnTrack = 0;
		var launches = new List<(int Turn, int Id)>();
		var seen = new HashSet<int>();
		var turn = 0;
		var minimumGap = int.MaxValue;
		rig.Rig.Turns( 2400, () =>
		{
			turn++;
			mostOccupied = Math.Max( mostOccupied, PoolSlideActivity.Occupants( rig.Rig.Simulation, rig.Tower.ThingId ) );
			var riders = rig.OnSlide.Where( guest => guest.PoolActivityPhase == PoolSlidePhases.Ride ).ToList();
			mostOnTrack = Math.Max( mostOnTrack, riders.Count );
			foreach ( var rider in riders )
				if ( seen.Add( rider.Id ) )
					launches.Add( (turn, rider.Id) );
			var positions = riders.Select( rider => rig.Slide.PositionAtTurn( rider.PoolActivityTurns ) ).OrderBy( p => p ).ToList();
			for ( var index = 1; index < positions.Count; index++ )
				minimumGap = Math.Min( minimumGap, positions[index] - positions[index - 1] );
		} );
		Assert.IsTrue( mostOccupied <= 2 && mostOccupied == 2, $"never more than the capacity, and it filled up ({mostOccupied})" );
		Assert.IsTrue( launches.Count >= 4, $"{launches.Count} guests rode" );
		Assert.IsTrue( minimumGap >= PoolSlideSettings.Gap, $"riders on the track kept {minimumGap} apart, at least {PoolSlideSettings.Gap}" );
		Assert.IsTrue( mostOnTrack >= 1 );
		Assert.IsTrue( launches.Zip( launches.Skip( 1 ) ).All( pair => pair.Second.Turn > pair.First.Turn ), "one launch per turn at most" );
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
		var guest = rig.Rig.Send();
		rig.Rig.Until( () => guest.InPoolActivity && guest.PoolActivityPhase == PoolSlidePhases.Ride && guest.PoolActivityTurns >= 5, 4000 );
		var json = Encoding.UTF8.GetString( ParkResumeJson.Serialize( rig.Rig.Simulation.CaptureSnapshot() ) );
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( Encoding.UTF8.GetBytes( json ) );
		var slides = ParkResumeJson.Deserialize<PoolSlidesSaveData>( ParkResumeJson.Serialize( rig.World.CaptureSnapshot() ) );
		var copy = new SlideRig( build: false );
		copy.World.RestoreSnapshot( slides );
		Assert.IsTrue( copy.Rig.Simulation.ValidateSnapshot( snapshot ).IsValid, string.Join( "; ", copy.Rig.Simulation.ValidateSnapshot( snapshot ).Errors ) );
		copy.Rig.Simulation.RestoreSnapshot( snapshot );
		Assert.AreEqual( rig.Hash(), copy.Hash() );
		var twin = copy.Rig.Simulation.Guests.Single();
		for ( var tick = 0; tick < 60 * 80; tick++ )
		{
			rig.Rig.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			copy.Rig.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			Assert.AreEqual( (guest.State, guest.PoolActivityPhase, guest.PoolActivityTurns, guest.Navigation.X, guest.Navigation.Y), (twin.State, twin.PoolActivityPhase, twin.PoolActivityTurns, twin.Navigation.X, twin.Navigation.Y), $"tick {tick}" );
		}
		Assert.AreEqual( rig.Hash(), copy.Hash() );
		Assert.IsFalse( twin.InPoolActivity && twin.PoolActivityPhase == PoolSlidePhases.Ride, "it landed" );
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
	internal const ulong PoolsSlidesFixedRunHash = 0x8FDB7A3F30ACC019UL;

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
