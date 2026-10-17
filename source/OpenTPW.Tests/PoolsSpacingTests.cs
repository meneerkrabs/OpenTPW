using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.PoolsSwimSessionTests;

namespace OpenTPW.Tests;

/// <summary>
/// The body spacing of the pools (docs/POOLS.md, "Spacing"): free swimmers keep <see cref="PoolSwimSettings.MinimumDistance"/> from every body in the water, including the state-25 guests that are in the
/// water (floaters, a jet's guests, landings); river floaters keep a gap along the lane; landings wait for their zone; the pool holds a body per water cell.
/// </summary>
[TestClass]
public class PoolsSpacingTests
{
	/// <summary>A test activity that is a body in the water and does nothing: it holds its guest where it is until <see cref="Release"/>. It is offered to nobody, only started by hand.</summary>
	private sealed class Hold : IPoolActivity
	{
		public bool Release;
		public int Only = -1;
		public string Id => "test.hold";
		public PoolActivityOffer? Offer( GuestSimulation simulation, Guest guest, PoolSwimRegion region ) => guest.Id == Only ? new PoolActivityOffer( 0, guest.CellX, guest.CellY ) : null;
		public bool Start( GuestSimulation simulation, Guest guest, PoolActivityOffer offer, ref uint random ) => true;
		public PoolActivityStep Update( GuestSimulation simulation, Guest guest, ref uint random ) => Release ? PoolActivityStep.BackInWater : PoolActivityStep.Continue;
		public bool InWater( GuestSimulation simulation, Guest guest ) => true;
		public void Abort( GuestSimulation simulation, Guest guest ) { }
	}

	private static int Fixed( double cells ) => (int)Math.Round( cells * Fixed16.One );

	/// <summary>Sends <paramref name="count"/> swimmers into the rig's pool and spreads them on a grid of <paramref name="spacing"/> cells over the free water, with no goal yet.</summary>
	private static List<Guest> Spread( Rig rig, int count, double spacing )
	{
		rig.Simulation.DebugStartSwimmers( rig.Ride, count );
		var swimmers = rig.Simulation.Guests.Where( guest => guest.IsSwimming ).OrderBy( guest => guest.Id ).ToList();
		Assert.AreEqual( count, swimmers.Count );
		var index = 0;
		for ( var y = 3.4; y < 9.0 && index < swimmers.Count; y += spacing )
			for ( var x = 5.4; x < 11.0 && index < swimmers.Count; x += spacing )
			{
				if ( !rig.Region.IsWater( Fixed( x ), Fixed( y ) ) )
					continue;
				rig.Simulation.PlacePoolGuest( swimmers[index], Fixed( x ), Fixed( y ) );
				swimmers[index].SwimHasGoal = false;
				index++;
			}
		Assert.AreEqual( count, index, "every swimmer found a point" );
		return swimmers;
	}

	/// <summary>The closest distance (16.16) between two bodies in the water of the rig's pool, and which two.</summary>
	private static (int Distance, Guest? A, Guest? B) Closest( Rig rig )
	{
		var bodies = rig.Simulation.Guests.Where( guest => rig.Simulation.IsWaterBody( guest, rig.Region ) ).ToList();
		var best = (Distance: int.MaxValue, A: (Guest?)null, B: (Guest?)null);
		for ( var i = 0; i < bodies.Count; i++ )
			for ( var j = i + 1; j < bodies.Count; j++ )
			{
				var distance = Fixed16.Length( bodies[i].Navigation.X - bodies[j].Navigation.X, bodies[i].Navigation.Y - bodies[j].Navigation.Y );
				if ( distance < best.Distance )
					best = (distance, bodies[i], bodies[j]);
			}
		return best;
	}

	// ---- Free swimmers ------------------------------------------------------------------------------------------------

	[TestMethod]
	public void ACrowdedPoolNeverHasTwoSwimmersCloserThanTheMinimumDistance()
	{
		var rig = new Rig( capacity: 40 );
		var swimmers = Spread( rig, 30, 0.9 );
		var start = swimmers.Select( guest => (guest.Navigation.X, guest.Navigation.Y) ).ToList();
		var worst = int.MaxValue;
		var closeTurns = 0;
		rig.Turns( 140, () =>
		{
			Assert.IsTrue( swimmers.All( guest => guest.State == GuestState.Swimming ), "nobody leaves within the run" );
			var (distance, _, _) = Closest( rig );
			worst = Math.Min( worst, distance );
			if ( distance < PoolSwimSettings.MinimumDistance + 2000 )
				closeTurns++;
		} );
		Assert.IsTrue( worst >= PoolSwimSettings.MinimumDistance - 8, $"the closest two came within {worst / (double)Fixed16.One:F3} cells; the minimum is {PoolSwimSettings.MinimumDistance / (double)Fixed16.One:F2}" );
		var moved = swimmers.Where( ( guest, index ) => Fixed16.Length( guest.Navigation.X - start[index].X, guest.Navigation.Y - start[index].Y ) > Fixed16.One ).Count();
		Assert.IsTrue( moved >= 20, $"the crowd still swims: {moved} of 30 got more than a cell from where they started" );
		Assert.IsTrue( swimmers.All( guest => rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ) ), "and stays in the water" );
	}

	[TestMethod]
	public void FreeSwimmersKeepAwayFromAJetStanderAndAnyBodyInTheWater()
	{
		var rig = new Rig( capacity: 20 );
		var hold = new Hold();
		rig.Simulation.RegisterPoolActivity( hold );
		var swimmers = Spread( rig, 12, 1.1 );
		var stander = swimmers[0];
		hold.Only = stander.Id;
		Assert.IsTrue( rig.Simulation.DebugStartActivity( stander, hold.Id ) );
		Assert.AreEqual( GuestState.PoolActivity, stander.State );
		var (sx, sy) = (stander.Navigation.X, stander.Navigation.Y);
		var near = 0;
		rig.Turns( 120, () =>
		{
			Assert.AreEqual( (sx, sy), (stander.Navigation.X, stander.Navigation.Y), "the stander stays" );
			foreach ( var guest in swimmers.Skip( 1 ).Where( guest => guest.State == GuestState.Swimming ) )
			{
				var distance = Fixed16.Length( guest.Navigation.X - sx, guest.Navigation.Y - sy );
				Assert.IsTrue( distance >= PoolSwimSettings.MinimumDistance - 8, $"swimmer {guest.Id} came within {distance / (double)Fixed16.One:F3} of the guest in an activity" );
				if ( distance < PoolSwimSettings.AvoidRadius )
					near++;
			}
		} );
		Assert.IsTrue( swimmers.Skip( 1 ).All( guest => guest.State == GuestState.Swimming ) );
		Assert.IsTrue( rig.Simulation.IsWaterBody( stander, rig.Region ), "the host counts the guest of the activity as a body in the water" );
		hold.Release = true;
		rig.Turns( 2 );
		Assert.AreEqual( GuestState.Swimming, stander.State );
	}

	[TestMethod]
	public void FreeSwimmersAvoidRiverFloatersAndAJetsGuests()
	{
		var rig = RingRig( out _ );
		var jets = new PoolAttractionSite( 4302, PoolAttractionKind.Jets, new[] { (7, 2) }, (7, 3) );
		var sites = new PoolAttractionSites( () => new[] { jets } );
		rig.Simulation.RegisterPoolActivity( new JetsActivity( sites ) );
		var swimmers = Spread( rig, 10, 1.1 );
		// two guests in the jet's water-side cell (they approach, then stand under it)
		Assert.IsTrue( rig.Simulation.DebugStartActivity( swimmers[0], JetsActivity.ActivityId ) );
		Assert.IsTrue( rig.Simulation.DebugStartActivity( swimmers[1], JetsActivity.ActivityId ) );
		var free = swimmers.Skip( 2 ).ToList();
		var worst = int.MaxValue;
		rig.Turns( 40, () =>
		{
			var jetGuests = swimmers.Take( 2 ).Where( guest => guest.State == GuestState.PoolActivity ).ToList();
			foreach ( var guest in free.Where( guest => guest.State == GuestState.Swimming ) )
				foreach ( var other in jetGuests )
					worst = Math.Min( worst, Fixed16.Length( guest.Navigation.X - other.Navigation.X, guest.Navigation.Y - other.Navigation.Y ) );
		} );
		Assert.IsTrue( worst >= PoolSwimSettings.MinimumDistance - 8, $"free swimmers came within {worst / (double)Fixed16.One:F3} cells of a jet's guest" );
		// a floater: put a swimmer in the river row and let it float, while the others swim on
		// the floater is put in the river cell farthest from every swimmer
		var floater = free.First( guest => guest.State == GuestState.Swimming );
		var others = free.Where( guest => guest != floater && guest.State == GuestState.Swimming ).ToList();
		var spot = PoolRiverMap.Of( rig.Cells ).Rings[0].Cells.Select( PoolSwimRegion.Centre )
			.OrderByDescending( point => others.Min( guest => Fixed16.Length( guest.Navigation.X - point.X, guest.Navigation.Y - point.Y ) ) ).First();
		rig.Simulation.PlacePoolGuest( floater, spot.X, spot.Y );
		Assert.IsTrue( rig.Simulation.DebugStartActivity( floater, RiverActivity.ActivityId ), "a swimmer in a river cell floats" );
		var worstFloat = int.MaxValue;
		rig.Turns( 100, () =>
		{
			if ( floater.State != GuestState.PoolActivity )
				return;
			foreach ( var guest in others.Where( guest => guest.State == GuestState.Swimming ) )
				worstFloat = Math.Min( worstFloat, Fixed16.Length( guest.Navigation.X - floater.Navigation.X, guest.Navigation.Y - floater.Navigation.Y ) );
		} );
		// the current moves the floater by up to its own speed after a swimmer's check, which is the tolerance
		Assert.IsTrue( worstFloat >= PoolSwimSettings.MinimumDistance - PoolRiverSettings.FlowSpeed - 8, $"free swimmers came within {worstFloat / (double)Fixed16.One:F3} cells of a floater" );
	}

	// ---- The river ----------------------------------------------------------------------------------------------------

	private static Rig RingRig( out IReadOnlyList<(int X, int Y)> ring )
	{
		var rig = new Rig( capacity: 60 );
		for ( var y = 3; y <= 8; y++ )
			for ( var x = 5; x <= 10; x++ )
				if ( x == 5 || x == 10 || y == 3 || y == 8 )
					rig.Cells.SetFlags( x, y, ParkCellFlags.PoolRiver );
		rig.Simulation.RegisterPoolActivity( new RiverActivity() );
		ring = PoolRiverMap.Of( rig.Cells ).Rings[0].Cells;
		return rig;
	}

	/// <summary>Floaters laid evenly along the ring's lane, <paramref name="spacing"/> cells apart (the lane is one cell a cell long, straight and round the bends).</summary>
	private static List<Guest> Float( Rig rig, IReadOnlyList<(int X, int Y)> ring, int count, double spacing )
	{
		rig.Simulation.DebugStartSwimmers( rig.Ride, count );
		var guests = rig.Simulation.Guests.Where( guest => guest.IsSwimming ).OrderBy( guest => guest.Id ).ToList();
		for ( var index = 0; index < count; index++ )
		{
			var along = index * spacing;
			var cellIndex = (int)Math.Floor( along );
			var share = along - cellIndex;
			var (cx, cy) = PoolSwimRegion.Centre( ring[cellIndex % ring.Count] );
			var (nx, ny) = PoolSwimRegion.Centre( ring[(cellIndex + 1) % ring.Count] );
			rig.Simulation.PlacePoolGuest( guests[index], cx + (int)((nx - cx) * share), cy + (int)((ny - cy) * share) );
			Assert.IsTrue( rig.Simulation.DebugStartActivity( guests[index], RiverActivity.ActivityId ), $"floater {index}" );
		}
		return guests;
	}

	[TestMethod]
	public void RiverFloatersKeepTheirGapAlongTheLaneAndTheRingKeepsFlowing()
	{
		var rig = RingRig( out var ring );
		// the ring has 20 cells; 28 floaters 0.7 cell apart are packed tighter than the floaters' gap (0.8) and looser than their minimum (0.6)
		var guests = Float( rig, ring, 28, spacing: 0.7 );
		var travelled = guests.ToDictionary( guest => guest.Id, _ => 0L );
		var last = guests.ToDictionary( guest => guest.Id, guest => (guest.Navigation.X, guest.Navigation.Y) );
		var worst = int.MaxValue;
		var most = 0;
		var turn = 0;
		rig.Turns( 90, () =>
		{
			turn++;
			foreach ( var guest in guests.Where( guest => guest.State == GuestState.PoolActivity ) )
			{
				travelled[guest.Id] += Fixed16.Length( guest.Navigation.X - last[guest.Id].X, guest.Navigation.Y - last[guest.Id].Y );
				last[guest.Id] = (guest.Navigation.X, guest.Navigation.Y);
			}
			var floaters = guests.Where( guest => guest.State == GuestState.PoolActivity ).ToList();
			most = Math.Max( most, floaters.Count );
			// the laying out cuts the corners of the lane, so floaters round a bend start closer than the gap: the first turns open it
			for ( var i = 0; i < floaters.Count && turn > 20; i++ )
				for ( var j = i + 1; j < floaters.Count; j++ )
					worst = Math.Min( worst, Fixed16.Length( floaters[i].Navigation.X - floaters[j].Navigation.X, floaters[i].Navigation.Y - floaters[j].Navigation.Y ) );
		} );
		Assert.AreEqual( 28, most, "all floated" );
		Assert.IsTrue( worst >= PoolRiverSettings.FloatMinimumGap - 2 * PoolRiverSettings.DriftSpeed, $"floaters came within {worst / (double)Fixed16.One:F3} cells" );
		// nobody is stuck: every floater moved at least the drift all the way
		Assert.IsTrue( guests.All( guest => travelled[guest.Id] >= 60L * PoolRiverSettings.DriftSpeed ), $"the slowest floater went {travelled.Values.Min() / (double)Fixed16.One:F2} cells in 90 turns" );
		// and every float ends: nobody floats for ever (a float lasts at most 240 turns, packed or not)
		var finished = new HashSet<int>();
		rig.Turns( 260, () =>
		{
			foreach ( var guest in guests.Where( guest => guest.State != GuestState.PoolActivity ) )
				finished.Add( guest.Id );
		} );
		Assert.AreEqual( guests.Count, finished.Count, "every floater's float ran out" );
	}

	[TestMethod]
	public void AFloaterWithAFreeLaneAheadFloatsAtTheCurrentsSpeed()
	{
		var rig = RingRig( out var ring );
		var guests = Float( rig, ring, 4, spacing: 0.1 );
		var leader = guests[3];
		var start = (leader.Navigation.X, leader.Navigation.Y);
		var followerStart = (guests[0].Navigation.X, guests[0].Navigation.Y);
		rig.Turns( 1 );
		var moved = Fixed16.Length( leader.Navigation.X - start.X, leader.Navigation.Y - start.Y );
		Assert.AreEqual( PoolRiverSettings.FlowSpeed, moved, 200, "the leader moves at the current's speed" );
		Assert.IsTrue( Fixed16.Length( guests[0].Navigation.X - followerStart.X, guests[0].Navigation.Y - followerStart.Y ) < PoolRiverSettings.FlowSpeed / 2, "a floater packed behind the others only drifts, or stands" );
	}

	// ---- Landings -----------------------------------------------------------------------------------------------------

	private static (int X, int Y) Landing( PoolSwimRegion region, PoolAttractionSite site )
	{
		var steps = PoolSwimRegion.Centre( site.Access );
		var platform = site.CentreFixed;
		var beyond = (X: steps.X + Math.Sign( steps.X - platform.X ) * PoolAttractionSettings.SwingReach, Y: steps.Y + Math.Sign( steps.Y - platform.Y ) * PoolAttractionSettings.SwingReach);
		return region.IsWater( beyond.X, beyond.Y ) ? beyond : steps;
	}

	[TestMethod]
	public void ARopeSwingWaitsAtTheTopWhileSomeoneIsInTheWaterAtItsLandingThenSwingsAndNeverWaitsForEver()
	{
		var rope = new PoolAttractionSite( 4301, PoolAttractionKind.RopeSwing, new[] { (11, 5) }, (10, 5) );
		foreach ( var release in new[] { true, false } )
		{
			var rig = new Rig( capacity: 4 );
			var hold = new Hold();
			rig.Simulation.RegisterPoolActivity( hold );
			rig.Simulation.RegisterPoolActivity( new RopeSwingActivity( new PoolAttractionSites( () => new[] { rope } ) ) );
			rig.Simulation.DebugStartSwimmers( rig.Ride, 2 );
			var guests = rig.Simulation.Guests.Where( guest => guest.IsSwimming ).OrderBy( guest => guest.Id ).ToList();
			var landing = Landing( rig.Region, rope );
			rig.Simulation.PlacePoolGuest( guests[1], landing.X + Fixed( 0.2 ), landing.Y );
			hold.Only = guests[1].Id;
			Assert.IsTrue( rig.Simulation.DebugStartActivity( guests[1], hold.Id ) );
			rig.Simulation.PlacePoolGuest( guests[0], Fixed( 9.6 ), Fixed( 5.5 ) );
			Assert.IsTrue( rig.Simulation.DebugStartActivity( guests[0], RopeSwingActivity.ActivityId ) );
			var climbing = rig.Until( () => guests[0].PoolActivityPhase == 1, 200 );
			rig.Turns( PoolAttractionSettings.SwingClimbTurns + 20 );
			Assert.AreEqual( 1, guests[0].PoolActivityPhase, "it stands at the top while the landing is occupied" );
			if ( release )
			{
				hold.Release = true;
				// the other guest is a swimmer again, and leaves the 0.6 cell zone within a few turns; then the swing goes
				rig.Turns( 25 );
				Assert.IsTrue( guests[0].PoolActivityPhase >= 2 || guests[0].State != GuestState.PoolActivity, "the landing is free: it swings" );
			}
			else
			{
				rig.Turns( PoolAttractionSettings.LandingWaitTurns );
				Assert.IsTrue( guests[0].PoolActivityPhase >= 2 || guests[0].State != GuestState.PoolActivity, $"after {PoolAttractionSettings.LandingWaitTurns} turns it goes anyway" );
			}
			Assert.IsTrue( climbing >= 0 );
		}
	}

	[TestMethod]
	public void ADiveWaitsAtTheEndOfTheBoardForAFreeLandingZone()
	{
		var rig = new Rig( capacity: 4 );
		var hold = new Hold();
		rig.Simulation.RegisterPoolActivity( hold );
		foreach ( var activity in PoolActivityRegistry.Create() )
			rig.Simulation.RegisterPoolActivity( activity );
		var sites = new[] { new PoolExtraSite( 9002, PoolExtraKind.DivingBoard, 0, new[] { (7, 2) }) };
		rig.Simulation.PoolExtraSites = () => sites;
		rig.Simulation.DebugStartSwimmers( rig.Ride, 2 );
		var guests = rig.Simulation.Guests.Where( guest => guest.IsSwimming ).OrderBy( guest => guest.Id ).ToList();
		var landing = (X: Fixed( 7.5 ), Y: Fixed( 4.5 ));
		Assert.IsTrue( rig.Region.IsWater( landing.X, landing.Y ) );
		rig.Simulation.PlacePoolGuest( guests[1], landing.X, landing.Y + Fixed( 0.3 ) );
		hold.Only = guests[1].Id;
		Assert.IsTrue( rig.Simulation.DebugStartActivity( guests[1], hold.Id ) );
		rig.Simulation.PlacePoolGuest( guests[0], Fixed( 7.5 ), Fixed( 3.6 ) );
		Assert.IsTrue( rig.Simulation.DebugStartActivity( guests[0], "extra.board" ) );
		rig.Until( () => (guests[0].PoolActivityPhase & 15) == 3, 300 );
		rig.Turns( PoolExtraSettings.BoardPauseTurns + 10 );
		Assert.AreEqual( 3, guests[0].PoolActivityPhase & 15, "the diver stands at the end of the board while the water below is taken" );
		hold.Release = true;
		// the other guest swims on and leaves the zone within a few turns; then the dive goes (well before the wait runs out)
		var waited = rig.Until( () => (guests[0].PoolActivityPhase & 15) >= 4 || guests[0].State != GuestState.PoolActivity, PoolAttractionSettings.LandingWaitTurns );
		Assert.IsTrue( waited < PoolAttractionSettings.LandingWaitTurns - 5, $"the water is free after {waited} turns: it dives" );
	}

	[TestMethod]
	public void JetsGuestsStandOnDistinctSpots()
	{
		var rig = new Rig( capacity: 8 );
		var jets = new PoolAttractionSite( 4302, PoolAttractionKind.Jets, new[] { (7, 2) }, (7, 3) );
		rig.Simulation.RegisterPoolActivity( new JetsActivity( new PoolAttractionSites( () => new[] { jets } ) ) );
		rig.Simulation.DebugStartSwimmers( rig.Ride, 5 );
		var guests = rig.Simulation.Guests.Where( guest => guest.IsSwimming ).OrderBy( guest => guest.Id ).ToList();
		var started = guests.Count( guest => rig.Simulation.DebugStartActivity( guest, JetsActivity.ActivityId ) );
		Assert.AreEqual( PoolAttractionSettings.JetCapacity, started, "the jet takes three" );
		var standers = guests.Where( guest => guest.State == GuestState.PoolActivity ).ToList();
		for ( var i = 0; i < standers.Count; i++ )
		{
			Assert.IsTrue( rig.Region.IsWater( standers[i].PoolActivityX, standers[i].PoolActivityY ), "a spot is free water" );
			for ( var j = i + 1; j < standers.Count; j++ )
				Assert.IsTrue( Fixed16.Length( standers[i].PoolActivityX - standers[j].PoolActivityX, standers[i].PoolActivityY - standers[j].PoolActivityY ) >= PoolSwimSettings.MinimumDistance, "spots are a minimum distance apart" );
		}
		rig.Turns( 100 );
		var stood = guests.Where( guest => guest.State == GuestState.PoolActivity ).ToList();
		for ( var i = 0; i < stood.Count; i++ )
			for ( var j = i + 1; j < stood.Count; j++ )
				Assert.IsTrue( Fixed16.Length( stood[i].Navigation.X - stood[j].Navigation.X, stood[i].Navigation.Y - stood[j].Navigation.Y ) >= PoolSwimSettings.MinimumDistance - 8, "the guests stand apart" );
	}

	// ---- Capacity, lookup and speed ------------------------------------------------------------------------------------

	[TestMethod]
	public void ThePoolTakesABodyPerWaterCellAndAtLeastTwo()
	{
		Assert.AreEqual( 1, PoolSwimSettings.SwimmersPerWaterCell );
		var rig = new Rig();
		Assert.AreEqual( 36, rig.Region.Capacity );
		Assert.AreEqual( 2, PoolSwimSettings.MinimumCapacity );
		// a strip of two cells has 1.26 cells of free length: two bodies fit at the minimum distance
		Assert.IsTrue( PoolSwimSettings.MinimumDistance <= Fixed16.One * 2 - 2 * PoolSwimSettings.Margin );
	}

	[TestMethod]
	public void TheCellLookupFindsTheSameBodiesAsAScanOfEveryGuestAndAManySwimmerPoolRunsFast()
	{
		var rig = new Rig( capacity: 600, gridSize: 48 );
		for ( var y = 3; y < 44; y++ )
			for ( var x = 5; x < 44; x++ )
				rig.Cells.SetType( x, y, ParkCellTypes.PoolWater );
		Assert.AreEqual( 39 * 41, rig.Region.Cells.Count );
		Assert.AreEqual( 39 * 41, rig.Region.Capacity );
		var count = 500;
		rig.Simulation.DebugStartSwimmers( rig.Ride, count );
		var swimmers = rig.Simulation.Guests.Where( guest => guest.IsSwimming ).ToList();
		Assert.AreEqual( count, swimmers.Count, "a big pool takes them all" );
		var clock = Stopwatch.StartNew();
		rig.Turns( 60 );
		clock.Stop();
		Assert.IsTrue( clock.Elapsed < TimeSpan.FromSeconds( 60 ), $"500 swimmers, 60 turns took {clock.Elapsed.TotalSeconds:F1} s" );
		// the lookup agrees with the brute-force scan for every swimmer
		var list = new List<Guest>();
		foreach ( var guest in swimmers.Where( guest => guest.IsSwimming ) )
		{
			rig.Simulation.GatherWaterBodies( rig.Region, guest, guest.Navigation.X, guest.Navigation.Y, PoolSwimSettings.AvoidRadius, list );
			var scan = rig.Simulation.Guests.Where( other => other != guest && rig.Simulation.IsWaterBody( other, rig.Region ) && Math.Abs( other.Navigation.X - guest.Navigation.X ) < PoolSwimSettings.AvoidRadius && Math.Abs( other.Navigation.Y - guest.Navigation.Y ) < PoolSwimSettings.AvoidRadius );
			CollectionAssert.AreEquivalent( scan.Select( other => other.Id ).ToList(), list.Select( other => other.Id ).ToList(), $"guest {guest.Id}" );
		}
		Assert.IsTrue( Closest( rig ).Distance >= 0 );
	}
}
