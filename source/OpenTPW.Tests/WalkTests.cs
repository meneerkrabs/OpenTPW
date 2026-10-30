using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The traced guest walk (docs/reverse/WALK-plan.md §11, WALK-I): speed caps and smoothing, the 16.16 steering step, arrival
/// and the reroute, ported from <c>tools/ppc-analysis/lanes/walk/test_walk_evidence.py</c>, plus the simulation's use of it
/// and the enumeration behind the M3 gate's per-cell walk term.
/// </summary>
[TestClass]
public class WalkTests
{
	private const int One = Fixed16.One;
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	[TestMethod]
	public void SpeedCapsFollowTheSetter()
	{
		Assert.AreEqual( (13107, 26214), GuestSpeed.Caps( 1.0f ) );
		Assert.AreEqual( (655, 655), GuestSpeed.Caps( 0f ), "the floor on both" );
		Assert.AreEqual( (655, 1048), GuestSpeed.Caps( 0.04f ) );
		Assert.AreEqual( GuestSpeed.Caps( 2f ), GuestSpeed.Caps( 5f ), "input capped at 2.0" );
		Assert.AreEqual( (26214, 52428), GuestSpeed.Caps( 2f ) );
		Assert.AreEqual( 7733, GuestSpeed.Caps( 0.59f ).MaxSpeed, "0.118 cell per update at the W2 floor" );
		// PPC oracle (walk-speed-setter, 518 inputs agree): NaN and -inf give the floor, +inf the cap.
		Assert.AreEqual( (655, 655), GuestSpeed.Caps( float.NaN ) );
		Assert.AreEqual( (655, 655), GuestSpeed.Caps( float.NegativeInfinity ) );
		Assert.AreEqual( (655, 655), GuestSpeed.Caps( -3f ) );
		Assert.AreEqual( (26214, 52428), GuestSpeed.Caps( float.PositiveInfinity ) );
	}

	/// <summary>
	/// tp.exe 0x004FA870 rounds the product and the sum separately (x87 at 24 bits), where the Mac's 0xe6b9c is one fused multiply-add (PPC oracle finding F6): the PC walk takes the former, the PPC
	/// oracle bridge the latter; the boost product wraps at 16 bits on both.
	/// </summary>
	[TestMethod]
	public void SmoothingRoundsTheProductAndTheSumSeparatelyAndTheBoostProductWraps()
	{
		var differs = 0;
		for ( var index = 0; index < 2000; index++ )
		{
			var s = index / 997f;
			var quotient = (100 + 25 + index % 200) / 100f;
			var fused = MathF.FusedMultiplyAdd( 3f, s, quotient ) * 0.25f;
			var separate = (quotient + 3f * s) * 0.25f;
			Assert.AreEqual( separate, GuestSpeed.Smooth( s, 100, 25, index % 200 ), $"s {s}" );
			Assert.AreEqual( fused, GuestSpeed.SmoothFused( s, 100, 25, index % 200 ), $"s {s}" );
			differs += fused != separate ? 1 : 0;
		}
		Assert.IsTrue( differs > 0, "some inputs tell the fused form from a separately rounded one" );
		Assert.AreEqual( (ushort)654, GuestSpeed.Decay( 661 ) );
		Assert.AreEqual( (ushort)0, GuestSpeed.Decay( 662 ), "662 x 99 = 65,538 wraps to 2" );
		Assert.AreEqual( (ushort)654, GuestSpeed.Decay( 0xFFFF ) );
		var update = GuestSpeed.Update( 1f, 100, 25, 300 );
		Assert.AreEqual( GuestSpeed.Smooth( 1f, 100, 25, 300 ), update.Speed );
		Assert.AreEqual( (GuestSpeed.Decay( 300 ), GuestSpeed.Caps( update.Speed )), (update.Boost, (update.MaxSpeed, update.MaxForce)) );
	}

	[TestMethod]
	public void SmoothingAndItsLowerBound()
	{
		Assert.AreEqual( 0.15f, GuestSpeed.Smooth( 0f, 60, 0, 0 ), 1e-6f );
		Assert.AreEqual( 1.0625f, GuestSpeed.Smooth( 1f, 100, 25, 0 ), 1e-6f );
		CollectionAssert.AreEqual( new ushort[] { 60, 80, 100, 120, 140 }, GuestSpeed.BaseSpeeds.ToArray() );
		var speed = 0f;
		for ( var update = 1; update <= 15; update++ )
		{
			speed = GuestSpeed.Smooth( speed, 60, 0, 0 );
			if ( update == 14 )
				Assert.IsTrue( speed < 0.59f, $"{speed}" );
		}
		Assert.IsTrue( speed > 0.59f, $"s after 15 updates of the slowest base: {speed}" );
		Assert.AreEqual( (ushort)99, GuestSpeed.Decay( 100 ) );
		Assert.AreEqual( (ushort)0, GuestSpeed.Decay( 1 ) );
	}

	[TestMethod]
	public void RadiiAndFixedPointHelpers()
	{
		Assert.AreEqual( Fixed16.Mul( GuestNavigation.ArrivalFactor, GuestNavigation.Radius ), GuestNavigation.ArrivalRadius, "FixMul(1.6, 0.2) = 0.32 cell" );
		Assert.AreEqual( 20971, GuestNavigation.ArrivalRadius );
		Assert.AreEqual( Fixed16.Mul( GuestNavigation.WaypointFactor, GuestNavigation.Radius ), GuestNavigation.WaypointRadius, "FixMul(2.0, 0.2) = 0.4 cell" );
		Assert.AreEqual( 26214, GuestNavigation.WaypointRadius );
		Assert.AreEqual( 104857, GuestNavigation.ArrivalFactor );
		Assert.AreEqual( 7 * One - 3 * One / 2, Fixed16.Octile( 3 * One, 4 * One ) );
		Assert.AreEqual( 5000, Fixed16.Length( 3000, 4000 ) );
		Assert.AreEqual( 5 * One, Fixed16.Length( 3 * One, 4 * One ) );
		Assert.AreEqual( (3 * One, 4 * One), Fixed16.Truncate( 3 * One, 4 * One, 5 * One ) );
		Assert.AreEqual( (3 * One, 4 * One), Fixed16.Truncate( 6 * One, 8 * One, 5 * One ) );
		Assert.AreEqual( int.MaxValue, Fixed16.Div( 1, 0 ) );
	}

	/// <summary>walk_evidence's pinned worst cases on the 7-grid at s = 0.59 (WALK-plan §9), with the C# step.</summary>
	[TestMethod]
	public void WorstWalksAtTheSpeedFloorMatchTheWitness()
	{
		Assert.AreEqual( (10, 0), WalkEnumeration.WorstStripWalk( "w", 0.59f, 7 ) );
		Assert.AreEqual( (14, 0), WalkEnumeration.WorstStripWalk( "w2", 0.59f, 7 ) );
		Assert.AreEqual( (17, 0), WalkEnumeration.WorstStripWalk( "w", 0.59f, 7, reversedAxis: true ) );
	}

	[TestMethod]
	public void SlowGuestsCanTriggerTheReroute()
	{
		// Progress is measured against the cell centre, so a slow move-up to the front edge looks stuck (WALK-plan §7).
		Assert.IsTrue( WalkEnumeration.WorstStripWalk( "w2", 0.2f, 7 ).Failures > 0 );
		Assert.AreEqual( (72, 0), WalkEnumeration.WorstStripWalk( "w", 0f, 7 ), "the stand walk completes even at the floor" );
	}

	[TestMethod]
	public void StraightWalkArrivesWithoutWalls()
	{
		var updates = WalkEnumeration.Walk( WalkEnumeration.SlotPoint( 0, 127 ), (0, 0), WalkEnumeration.StandPoint(),
			[WalkEnumeration.Front, WalkEnumeration.Entrance], 1.0f, WalkEnumeration.InStrip );
		Assert.IsTrue( updates is > 0 and <= 8, $"{updates}" );
	}

	[TestMethod]
	public void ANewDestinationKeepsTheVelocity()
	{
		var navigation = new GuestNavigation { X = One / 2, Y = One / 2, VelocityX = 5000, VelocityY = -3000 };
		navigation.SetDestination( 3 * One / 2, One / 2, [(0, 0), (1, 0)] );
		Assert.AreEqual( (5000, -3000), (navigation.VelocityX, navigation.VelocityY ), "route setup does not reset +16" );
		Assert.IsFalse( navigation.Step( 13107, 26214, ( _, _ ) => true ) );
		Assert.AreNotEqual( 0, navigation.VelocityY, "the old velocity is steered, not dropped" );
	}

	[TestMethod]
	public void ARoutingFailureArrivesAtOnceAndBrakes()
	{
		var navigation = new GuestNavigation { X = One / 2, Y = One / 2, VelocityX = 4000 };
		navigation.SetDestination( 5 * One, 5 * One, [] );
		Assert.IsTrue( navigation.Arrived );
		Assert.IsTrue( navigation.Step( 13107, 26214, ( _, _ ) => true ) );
		Assert.IsTrue( navigation.VelocityX < 4000, "follow_path brakes once arrived" );
	}

	[TestMethod]
	public void ArrivalIsTestedBeforeTheStepMoves()
	{
		var target = (X: One / 2, Y: One / 2);
		var navigation = new GuestNavigation { X = target.X + GuestNavigation.ArrivalRadius - 1, Y = target.Y };
		navigation.SetDestination( target.X, target.Y, [(0, 0)] );
		var x = navigation.X;
		Assert.IsTrue( navigation.Step( 13107, 26214, ( _, _ ) => true ), "octile < 0.32 cell on the last segment" );
		Assert.AreNotEqual( x, navigation.X, "the arrival step still moves" );
		var outside = new GuestNavigation { X = target.X + GuestNavigation.ArrivalRadius, Y = target.Y };
		outside.SetDestination( target.X, target.Y, [(0, 0)] );
		Assert.IsFalse( outside.Step( 13107, 26214, ( _, _ ) => true ) );
	}

	[TestMethod]
	public void ARefusedCellKeepsThePositionAndCountsAsNoProgress()
	{
		// A diagonal step into a closed cell: both edge neighbours are open, so no wall force, but the commit is refused.
		var navigation = new GuestNavigation { X = One - 100, Y = One - 100, VelocityX = 9000, VelocityY = 9000 };
		navigation.SetDestination( 3 * One / 2, 3 * One / 2, [(1, 1)] );
		var position = (navigation.X, navigation.Y);
		navigation.Step( 13107, 26214, ( x, y ) => (x, y) != (1, 1) );
		Assert.AreEqual( position, (navigation.X, navigation.Y), "the new cell is not walkable: the position is not committed" );
		Assert.AreEqual( 0b10, navigation.History, "the refused commit sets a history bit; the first progress sample after route setup has none to compare" );
		navigation.Step( 13107, 26214, ( x, y ) => (x, y) != (1, 1) );
		Assert.AreEqual( position, (navigation.X, navigation.Y) );
		Assert.AreEqual( 0b1011, navigation.History, "refused again, and no progress" );
	}

	/// <summary>avoid_walls (review WV-7): the edges are tried +x, −x, +y, −y and the first that pushes is the whole force.</summary>
	[TestMethod]
	public void AvoidWallsStopsAtTheFirstPushingEdge()
	{
		// Heading into the corner of a closed +x and a closed +y neighbour: only the x edge pushes.
		var navigation = new GuestNavigation { X = One - 4000, Y = One - 4000, VelocityX = 3000, VelocityY = 3000 };
		navigation.SetDestination( One / 2, One / 2, [(0, 0)] );
		navigation.Arrived = true; // follow_path only brakes
		navigation.Step( 13107, 26214, ( x, y ) => (x, y) == (0, 0) );
		var cornered = (navigation.VelocityX, navigation.VelocityY);
		// The same step with the +x neighbour open: now the y edge pushes.
		var open = new GuestNavigation { X = One - 4000, Y = One - 4000, VelocityX = 3000, VelocityY = 3000 };
		open.SetDestination( One / 2, One / 2, [(0, 0)] );
		open.Arrived = true;
		open.Step( 13107, 26214, ( x, y ) => (x, y) is (0, 0) or (1, 0) );
		Assert.IsTrue( cornered.VelocityX < open.VelocityX, $"{cornered} vs ({open.VelocityX}, {open.VelocityY}): the x wall pushed" );
		Assert.IsTrue( cornered.VelocityY > open.VelocityY, $"{cornered} vs ({open.VelocityX}, {open.VelocityY}): no y push while the x edge pushes" );
	}

	/// <summary>
	/// separation's neighbour query and callback (<c>0x10087c</c>, <c>0xfd5f4</c>; review WV-5): neighbours within 2 × radius
	/// in 1/256 cells, not the guest itself, not at the same 1/256 point; each pushes FixDiv(offset, octile); at most five.
	/// </summary>
	[TestMethod]
	public void SeparationPushesAwayFromAtMostFiveNeighbours()
	{
		GuestNavigation At( int x, int y ) => new() { X = x, Y = y };
		var self = At( 5 * One, 5 * One );
		Assert.AreEqual( (0, 0), GuestNavigation.Separation( self, [self] ), "the guest itself" );
		Assert.AreEqual( (0, 0), GuestNavigation.Separation( self, [At( 5 * One + 255, 5 * One )] ), "the same 1/256 point" );
		Assert.AreEqual( (0, 0), GuestNavigation.Separation( self, [At( 5 * One + 102 * 256, 5 * One )] ), "(2 x 0.2 cell >> 8)^2 = 102^2 is outside" );
		Assert.AreEqual( (-One, 0), GuestNavigation.Separation( self, [At( 5 * One + 101 * 256, 5 * One )] ), "a unit push away" );
		var diagonal = GuestNavigation.Separation( self, [At( 5 * One - 6000, 5 * One - 6000 )] );
		Assert.AreEqual( (Fixed16.Div( 6000, 9000 ), Fixed16.Div( 6000, 9000 )), diagonal, "FixDiv by the octile distance 6000 + 6000 - 3000" );
		var crowd = Enumerable.Range( 1, 7 ).Select( index => At( 5 * One + index * 1000, 5 * One ) ).ToList();
		Assert.AreEqual( (-5 * One, 0), GuestNavigation.Separation( self, crowd ), "the query stops after five neighbours" );
		// Skipped neighbours do not count towards the five.
		crowd.Insert( 0, At( 5 * One + 30000, 5 * One ) );
		crowd.Insert( 0, self );
		Assert.AreEqual( (-5 * One, 0), GuestNavigation.Separation( self, crowd ) );
	}

	/// <summary>Separation is off on the last segment within ONE + radius of its waypoint (<c>0xfd778</c>).</summary>
	[TestMethod]
	public void SeparationIsOffNearTheLastWaypoint()
	{
		var calls = 0;
		(int X, int Y) Push( GuestNavigation self )
		{
			calls++;
			return (0, 0);
		}
		var near = new GuestNavigation { X = One / 2, Y = One / 2 };
		near.SetDestination( 5 * One / 2, One / 2, [(0, 0), (1, 0), (2, 0)] );
		near.Step( 13107, 26214, ( _, _ ) => true, Push );
		Assert.AreEqual( 1, calls, "not on the last segment: on" );
		var last = new GuestNavigation { X = One / 2, Y = One / 2 };
		last.SetDestination( One / 2 + 1000, One / 2, [(0, 0)] );
		last.Step( 13107, 26214, ( _, _ ) => true, Push );
		Assert.AreEqual( 1, calls, "last segment, within ONE + radius: off" );
		var far = new GuestNavigation { X = One / 2, Y = One / 2 };
		far.SetDestination( 3 * One, One / 2, [(3, 0)] );
		far.Step( 13107, 26214, ( _, _ ) => true, Push );
		Assert.AreEqual( 2, calls, "last segment, farther than ONE + radius: on" );
	}

	[TestMethod]
	public void SubCellBytesAreOver256AndTheStandPointRotatesWithTheObject()
	{
		Assert.AreEqual( (2 * One + (127 << 8), 3 * One), GuestNavigation.BytePoint( (2, 3), 127, 0 ) );
		Assert.AreEqual( (127, 127), GuestSimulation.StandBytes( (0.5f, 0.5f), 0 ) );
		foreach ( var rotation in new[] { 90, 180, 270 } )
			Assert.AreEqual( (127, 127), GuestSimulation.StandBytes( (0.5f, 0.5f), rotation ), "the default is the entrance cell's centre less 1/256" );
		// 0xde1d8: 90 -> (y, 1 - x), 180 -> (1 - x, 1 - y), 270 -> (1 - y, x); x 255, truncated.
		Assert.AreEqual( (25, 229), GuestSimulation.StandBytes( (0.1f, 0.9f), 0 ) );
		Assert.AreEqual( (229, 229), GuestSimulation.StandBytes( (0.1f, 0.9f), 90 ) );
		Assert.AreEqual( (229, 25), GuestSimulation.StandBytes( (0.1f, 0.9f), 180 ) );
		Assert.AreEqual( (25, 25), GuestSimulation.StandBytes( (0.1f, 0.9f), 270 ) );
	}

	// ---- The simulation -----------------------------------------------------------------------------------------------

	private static GuestSimulation Corridor( ulong seed = 3 )
	{
		var grid = new GuestPathGrid( 30, 3 );
		for ( var x = 0; x < 30; x++ )
			grid.SetPath( x, 1, true );
		var settings = GuestTests.CreateSettings();
		return new GuestSimulation( grid, settings, seed ) { ArrivalsEnabled = false };
	}

	[TestMethod]
	public void GuestsDrawTheirBaseSpeedAndStartFromZero()
	{
		var simulation = Corridor();
		var guests = Enumerable.Range( 0, 40 ).Select( _ => simulation.SpawnInPark( 1, 1 ) ).ToList();
		Assert.IsTrue( guests.All( guest => GuestSpeed.BaseSpeeds.Contains( (ushort)guest.BaseSpeed ) ) );
		Assert.IsTrue( guests.Select( guest => guest.BaseSpeed ).Distinct().Count() > 1, "drawn from the guest stream" );
		Assert.IsTrue( guests.All( guest => guest.SmoothedSpeed == 0 && guest.MaxSpeed == GuestSpeed.Floor ) );
		Assert.IsTrue( guests.All( guest => guest.ToiletBonus == GuestSpeed.ToiletBonus ), "the person constructor stores the speed term 25 (0x004FB1C1)" );
		var guest = guests[0];
		guest.ToiletBonus = 0;   // the speed term the needs phase writes for a guest without need (this test follows the smoothing alone)
		var expected = 0f;
		for ( var turn = 0; turn < 20; turn++ )
		{
			expected = GuestSpeed.Smooth( expected, guest.BaseSpeed, 0, 0 );
			var start = simulation.ParkTurn;
			while ( simulation.ParkTurn == start )
				simulation.Tick( Tick );
		}
		Assert.AreEqual( expected, guest.SmoothedSpeed, "one speed update per park turn" );
		Assert.AreEqual( GuestSpeed.Caps( expected ), (guest.MaxSpeed, guest.MaxForce) );
	}

	[TestMethod]
	public void EnergyDoesNotSlowTheWalk()
	{
		// Two runs from the same seed; one guest's meter +0x1B8 is full in the second. It does not enter the speed (WALK-plan §4; tp.exe reads it only in the save).
		(float X, float Y) Run( bool tired )
		{
			var simulation = Corridor( 9 );
			var guest = simulation.SpawnInPark( 1, 1 );
			guest.Hunger = guest.Thirst = guest.Toilet = 0;
			for ( var tick = 0; tick < 20 * 60; tick++ )
			{
				if ( tired )
					guest.Energy = 100;
				simulation.Tick( Tick );
			}
			return (guest.X, guest.Y);
		}
		Assert.AreEqual( Run( false ), Run( true ) );
	}

	[TestMethod]
	public void TheToiletTermAddsSpeedOnTheGuestsNeedsPhase()
	{
		// +194 is written in the needs block, which runs only when park turn & 3 == the guest's id & 3 (0xeed40, review WV-8).
		var simulation = Corridor();
		var guest = simulation.SpawnInPark( 1, 1 );
		void RunUntilPhase( int phase )
		{
			do
				simulation.Tick( Tick );
			while ( (simulation.ParkTurn & 3) != phase || ParkCalendar.Turn( simulation.TickCount - 1 ) == simulation.ParkTurn );
		}
		guest.Toilet = 0;
		RunUntilPhase( guest.Id & 3 );
		Assert.AreEqual( 0, guest.ToiletBonus );
		guest.Toilet = 81;
		RunUntilPhase( (guest.Id + 1) & 3 );
		Assert.AreEqual( 0, guest.ToiletBonus, "off phase: unchanged" );
		RunUntilPhase( guest.Id & 3 );
		Assert.AreEqual( 25, guest.ToiletBonus, "+194 = 25 while toilet > 80" );
		guest.Toilet = 80;
		RunUntilPhase( (guest.Id + 2) & 3 );
		Assert.AreEqual( 25, guest.ToiletBonus, "kept between its phases" );
		RunUntilPhase( guest.Id & 3 );
		Assert.AreEqual( 0, guest.ToiletBonus );
		// The compare reads the truncated toilet's low byte unsigned (0xef0cc): 80.9 truncates to 80.
		guest.Toilet = 80.9f;
		RunUntilPhase( guest.Id & 3 );
		Assert.AreEqual( 0, guest.ToiletBonus );
	}

	[TestMethod]
	public void GuestsStepOncePerParkTurnAndTheDrawnPositionInterpolates()
	{
		var simulation = Corridor();
		var guest = simulation.SpawnInPark( 1, 1 );
		for ( var tick = 0; tick < 10 * 60; tick++ )
			simulation.Tick( Tick );
		var steps = 0;
		var moved = 0;
		var interpolated = false;
		for ( var tick = 0; tick < 5 * 60; tick++ )
		{
			var before = (guest.X, guest.Y);
			var turn = simulation.ParkTurn;
			simulation.Tick( Tick );
			if ( (guest.X, guest.Y) != before )
			{
				moved++;
				Assert.AreNotEqual( turn, simulation.ParkTurn, "positions change only on the tick that starts a park turn" );
			}
			if ( simulation.ParkTurn != turn )
				steps++;
			interpolated |= guest.DrawX != guest.X && guest.DrawX != before.X;
		}
		Assert.IsTrue( moved > 0 && moved <= steps, $"{moved} moves in {steps} turns" );
		Assert.IsTrue( interpolated, "the drawn position moves between steps" );
	}

	// ---- The M3 gate's walk terms -------------------------------------------------------------------------------------

	private static readonly int[] Depths = { 0, 63, 127, 191 };
	private static readonly int[] Laterals = { 114, 127, 141 };

	private static IEnumerable<int[]> LongShapes( int crossings ) => new[]
	{
		new int[crossings - 1],
		Enumerable.Range( 0, crossings - 1 ).Select( i => i % 2 == 0 ? 1 : -1 ).ToArray(),
		Enumerable.Range( 0, crossings - 1 ).Select( i => i % 3 == 2 ? (i / 3 % 2 == 0 ? 1 : -1) : 0 ).ToArray(),
		Enumerable.Range( 0, crossings - 1 ).Select( i => i % 2 == 1 ? (i / 2 % 2 == 0 ? 1 : -1) : 0 ).ToArray(),
	};

	private static void AssertWithinQueueWalkTerm( WalkEnumeration.QueueWalkResult result )
	{
		Assert.AreEqual( 0, result.Failures, $"{result}" );
		Assert.IsTrue( result.Worst <= M3GateRun.QueueWalkTurns( result.Crossings ), $"{result} > {M3GateRun.QueueWalkTurns( result.Crossings )}" );
	}

	/// <summary>
	/// The per-cell term C = 27 with w₂ = 15 (<see cref="M3GateRun.QueueCellWalkTurns"/>): every enumerated queue walk that
	/// crosses k cells arrives within k × 27 + 15 updates. Reduced grid; the full run is <see cref="QueueWalkTermFullEnumeration"/>.
	/// </summary>
	[TestMethod]
	public void QueueWalkTermIsDerivedByEnumeration()
	{
		var speeds = new[] { 0.59f, 1.0f, 2.0f };
		for ( var crossings = 0; crossings <= 3; crossings++ )
			AssertWithinQueueWalkTerm( WalkEnumeration.WorstQueueWalk( crossings, WalkEnumeration.TurnSequences( crossings ), speeds, 3, 2, Depths, Laterals ) );
		AssertWithinQueueWalkTerm( WalkEnumeration.WorstQueueWalk( 12, LongShapes( 12 ), new[] { 0.59f }, 3, 4, new[] { 0, 191 }, new[] { 114, 141 } ) );
	}

	/// <summary>
	/// The full enumeration recorded in docs/reverse/WALK-plan.md §15 (several minutes; run with OPENTPW_WALK_DERIVE=1):
	/// s = 0.59 and 0.60..2.00 in steps of 0.05, a 5 × 5 start grid, 17 stale velocities, 4 depths × 3 laterals on both
	/// depth axes, separation off and opposing; every turn sequence up to 4 crossings and four long shapes up to 25.
	/// </summary>
	[TestMethod]
	public void QueueWalkTermFullEnumeration()
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_WALK_DERIVE" ) != "1" )
			Assert.Inconclusive( "Set OPENTPW_WALK_DERIVE=1 to run the full enumeration." );
		var speeds = new[] { 0.59f }.Concat( Enumerable.Range( 0, 29 ).Select( index => 0.6f + 0.05f * index ) ).ToArray();
		var results = new List<WalkEnumeration.QueueWalkResult>();
		for ( var crossings = 0; crossings <= 4; crossings++ )
			results.Add( WalkEnumeration.WorstQueueWalk( crossings, WalkEnumeration.TurnSequences( crossings ), speeds, 5, 1, Depths, Laterals ) );
		foreach ( var crossings in new[] { 5, 8, 12, 16, 20, 25 } )
			results.Add( WalkEnumeration.WorstQueueWalk( crossings, LongShapes( crossings ), speeds, 3, 1, new[] { 0, 191 }, new[] { 114, 141 } ) );
		foreach ( var result in results )
			Console.WriteLine( $"{result} bound {M3GateRun.QueueWalkTurns( result.Crossings )}" );
		foreach ( var result in results )
			AssertWithinQueueWalkTerm( result );
	}

	/// <summary>
	/// The same term over tp.exe's own walk (<see cref="WalkEnumeration.WalkOriginal"/>: whole-walk destination 0x510100, search 0x511420,
	/// edge test 0x4D8750, corner turns): C = 27 since that walk reroutes around neighbours' cells (the full run: 3 crossings 93, 4 crossings
	/// 118, long shapes 13.7 per cell). Reduced grid; the full run is <see cref="QueueWalkTermFullEnumerationOriginal"/>.
	/// </summary>
	[TestMethod]
	public void QueueWalkTermHoldsOverTheOriginalWalk()
	{
		var speeds = new[] { 0.59f, 1.0f, 2.0f };
		for ( var crossings = 0; crossings <= 3; crossings++ )
			AssertWithinQueueWalkTerm( WalkEnumeration.WorstQueueWalkOriginal( crossings, WalkEnumeration.TurnSequences( crossings ), speeds, 3, 2, Depths, Laterals ) );
		AssertWithinQueueWalkTerm( WalkEnumeration.WorstQueueWalkOriginal( 12, LongShapes( 12 ), new[] { 0.59f }, 3, 4, new[] { 0, 191 }, new[] { 114, 141 } ) );
	}

	/// <summary>The move-up from slots 1 to 3 to slot 0 over the original walk on a typed strip stays within w2 = 15 (the full run: 12 at s = 0.59).</summary>
	[TestMethod]
	public void MoveUpWalkTermHoldsOverTheOriginalWalk()
	{
		foreach ( var speed in new[] { 0.59f, 1.0f, 2.0f } )
		{
			var (worst, failures, _) = WalkEnumeration.WorstStripMoveUpOriginal( speed, 3 );
			Assert.AreEqual( 0, failures );
			Assert.IsTrue( worst <= M3GateRun.MoveUpWalkTurns, $"s {speed}: {worst} > {M3GateRun.MoveUpWalkTurns}" );
		}
	}

	/// <summary>The full original-walk enumeration (about 20 minutes; OPENTPW_WALK_DERIVE=1): the grid of <see cref="QueueWalkTermFullEnumeration"/> over tp.exe's walk.</summary>
	[TestMethod]
	public void QueueWalkTermFullEnumerationOriginal()
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_WALK_DERIVE" ) != "1" )
			Assert.Inconclusive( "Set OPENTPW_WALK_DERIVE=1 to run the full enumeration." );
		var speeds = new[] { 0.59f }.Concat( Enumerable.Range( 0, 29 ).Select( index => 0.6f + 0.05f * index ) ).ToArray();
		for ( var crossings = 0; crossings <= 4; crossings++ )
			AssertWithinQueueWalkTerm( WalkEnumeration.WorstQueueWalkOriginal( crossings, WalkEnumeration.TurnSequences( crossings ), speeds, 5, 1, Depths, Laterals ) );
		foreach ( var crossings in new[] { 5, 8, 12, 16, 20, 25 } )
			AssertWithinQueueWalkTerm( WalkEnumeration.WorstQueueWalkOriginal( crossings, LongShapes( crossings ), speeds, 3, 1, new[] { 0, 191 }, new[] { 114, 141 } ) );
	}

	[TestMethod]
	public void EveryWalkApproximationHasOneCodeTagAndDocumentation()
	{
		var ids = WalkApproximations.Entries.Select( entry => entry.Id ).ToList();
		Assert.AreEqual( ids.Count, ids.Distinct().Count() );
		var root = new DirectoryInfo( AppContext.BaseDirectory );
		while ( root != null && !File.Exists( Path.Combine( root.FullName, "docs", "GUESTS.md" ) ) )
			root = root.Parent;
		if ( root == null )
			Assert.Inconclusive( "Repository sources are not next to the test binaries." );
		var tags = Directory.EnumerateFiles( Path.Combine( root!.FullName, "source", "OpenTPW" ), "*.cs", SearchOption.AllDirectories )
			.Where( path => !path.Contains( $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}" ) )
			.SelectMany( path => Regex.Matches( File.ReadAllText( path ), @"\[APPROX:(WALK-\d+)\]" ).Select( match => match.Groups[1].Value ) ).ToList();
		CollectionAssert.AreEquivalent( ids, tags, "one code tag per registered approximation" );
		var docs = File.ReadAllText( Path.Combine( root.FullName, "docs", "GUESTS.md" ) );
		foreach ( var id in ids )
			Assert.IsTrue( docs.Contains( $"| {id} |" ), $"{id} is documented" );
	}
}
