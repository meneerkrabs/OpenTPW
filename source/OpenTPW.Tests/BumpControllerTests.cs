using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Asset-free tests of the bump-ride module (docs/TRACKS.md): the sine table, the section geometry in the original's fixed-point units, the
/// BUMP command semantics and a golden run of a boat around a closed loop. The golden values freeze this implementation of the traced integer
/// physics (they are not captures of the original game).
/// </summary>
[TestClass]
public class BumpControllerTests
{
	internal sealed class TestGrid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	/// <summary>The eight-tile loop of the circuit tests with a controller on it: station (10,10), (12,10), corners at (14,10), (14,12), (8,12), (8,10).</summary>
	internal sealed class Loop
	{
		public readonly ParkCellMap Cells = new( 40, 40 );
		public readonly TrackBuilder Builder;
		public readonly TrackStation Station = TrackStation.FromPlacement( 10, 9, 0 );
		public readonly BumpController Controller;
		public readonly TrackCircuit Circuit;
		private uint rng = 12345;

		public Loop( int type = -5, BumpWorld? world = null )
		{
			Builder = new TrackBuilder( Cells, TrackFamily.Water, () => null, new TestGrid() );
			Builder.PlaceStation( Station );
			var writer = new TrackLineWriter( Builder );
			writer.Commit( (14, 10), (14, 12) );
			writer.Commit( (14, 12), (8, 12) );
			writer.Commit( (8, 12), (8, 10) );
			Circuit = TrackCircuitWalker.WalkAndRefresh( Builder, Station );
			Controller = new BumpController( BumperTemplates.Get( type ), () => { rng = unchecked( rng * 1664525u + 1013904223u ); return rng >> 8; }, world );
			var centre = Station.Reference;
			Controller.SetStation( centre.X, centre.Y, Station.Heading );
			Controller.Rebuild( Circuit );
		}

		public void Run( int ticks )
		{
			for ( var tick = 0; tick < ticks; tick++ )
				Controller.Tick();
		}
	}

	// ---- The sine table ------------------------------------------------------------------------------

	[TestMethod]
	public void TheSineTableIsTheTruncatedStartUpTable()
	{
		var table = BumpMath.Sine;
		Assert.AreEqual( 512, table.Length );
		CollectionAssert.AreEqual( new[] { 0, 3, 6, 9, 12, 15, 18, 21, 25, 28, 31, 34 }, table.Take( 12 ).ToArray(), "truncation, not rounding: 256 sin(4π/1024) = 12.57" );
		Assert.AreEqual( 255, table[128], "256 · sin(π/2) with the constant 0.00613591796875 just below π/512 stays under 256" );
		Assert.AreEqual( 0, table[256] );
		Assert.AreEqual( -255, table[384] );
		Assert.AreEqual( 255, table.Max() );
		Assert.AreEqual( -255, table.Min() );
		for ( var index = 0; index < 512; index++ )
			Assert.AreEqual( (int)(Math.Sin( index * 2 * 0.00613591796875 ) * 256.0), table[index] );
		// cosine is the sine a quarter turn on
		Assert.AreEqual( 255, BumpMath.Cos( 0 ) );
		Assert.AreEqual( -255, BumpMath.Cos( 256 ) );
		Assert.AreEqual( BumpMath.Sin( 128 ), BumpMath.Cos( 0 ) );
	}

	[TestMethod]
	public void HeadingsRunClockwiseFromPlusYAndDistancesScaleLikeTheOriginal()
	{
		Assert.AreEqual( 0, BumpMath.HeadingOf( 0, 100 ), "+Y is heading 0" );
		// the original's 1/π constant is a hair above the true value, so the truncated heading of +X is 127, not 128
		Assert.AreEqual( 127, BumpMath.HeadingOf( 100, 0 ), "+X is a quarter turn less the truncation" );
		Assert.AreEqual( 256, BumpMath.HeadingOf( 0, -100 ) );
		Assert.AreEqual( 384, BumpMath.HeadingOf( -100, 0 ), "−X is three quarters of a turn" );
		Assert.AreEqual( 5, BumpMath.Distance( 3, 4 ) );
		Assert.AreEqual( 0, BumpMath.Distance( 0, 0 ) );
		Assert.AreEqual( 10, BumpMath.Distance( 6, 8 ), "small vectors are exact" );
		// a vector beyond the 0x8000 range is halved until it fits, then the result is shifted back
		Assert.AreEqual( 0x20000, BumpMath.Distance( 0x20000, 0 ) );
		Assert.AreEqual( 100, BumpMath.Div1024( 102400 ) );
		Assert.AreEqual( -100, BumpMath.Div1024( -102400 ) );
		Assert.AreEqual( 0, BumpMath.Div1024( -1000 ), "truncation toward zero" );
	}

	// ---- Controller values and commands --------------------------------------------------------------

	/// <summary>The attraction's track word starts at 1 for every ride (tp.exe 0x004DB0DF) and only a circuit walk rewrites it (ARRIVAL-003).</summary>
	[TestMethod]
	public void TheTrackWordStartsAtOneForEveryRide()
	{
		foreach ( var type in new[] { -1, -4, -5 } )
		{
			var controller = new BumpController( BumperTemplates.Get( type ), () => 0 );
			Assert.IsTrue( controller.IsTrackValid, $"type {type}: the constructor's word" );
		}
		var kart = new BumpController( BumperTemplates.Get( -4 ), () => 0 );
		kart.Rebuild( TrackCircuit.Invalid( "no circuit" ) );
		Assert.IsFalse( kart.IsTrackValid, "a walk without a circuit rewrites it" );
	}

	[TestMethod]
	public void PerformanceInterpolatesTheTemplateRangesWithTruncatingDivision()
	{
		var water = new BumpController( BumperTemplates.Get( -5 ), () => 0 );
		Assert.AreEqual( (6, 985, 10, 980), (water.Accel, water.Damping, water.TurnRate, water.Restitution), "the creation performance is 50" );
		water.SetPerformance( 0 );
		Assert.AreEqual( (4, 970, 7, 980), (water.Accel, water.Damping, water.TurnRate, water.Restitution) );
		water.SetPerformance( 100 );
		Assert.AreEqual( (9, 1000, 14, 980), (water.Accel, water.Damping, water.TurnRate, water.Restitution) );
		water.SetPerformance( 33 );
		Assert.AreEqual( (5, 979, 9, 980), (water.Accel, water.Damping, water.TurnRate, water.Restitution), "(9−4)·33/100 = 1, (1000−970)·33/100 = 9, (14−7)·33/100 = 2" );
		water.SetPerformance( 250 );
		Assert.AreEqual( 100, water.Performance, "clamped" );
		water.SetPerformance( -4 );
		Assert.AreEqual( 0, water.Performance );
		var kart = new BumpController( BumperTemplates.Get( -4 ), () => 0 );
		kart.SetPerformance( 50 );
		Assert.AreEqual( (23, 955, 26, 900), (kart.Accel, kart.Damping, kart.TurnRate, kart.Restitution), "kart damping falls with performance: (950−960)·50/100 = −5" );
	}

	[TestMethod]
	public void TheVehicleCreationHangsEachSeatedRidersHeadThroughTheWorldGeneratorOnlyWithoutCostumeHeads()
	{
		// [BIN:TPW-EXE:0x00549CAF seat heads] each rider whose seat has a head dummy goes through 0x44B410, which reseeds the world generator with it when the theme has no costume heads
		var states = new List<ulong>();
		foreach ( var reseeds in new[] { false, true } )
		{
			var scripts = new RideScriptWorld( 1 ) { HeadHangReseeds = reseeds };
			var loop = new Loop( -5, scripts.Bumpers );
			loop.Controller.SeatHeadSource = _ => new[] { true, false };
			loop.Controller.Command( 7, 0 );
			loop.Controller.Command( 1, 7 );
			loop.Controller.Command( 1, 8 );
			loop.Controller.Command( 4, 0 );
			states.Add( scripts.World.State );
			Assert.AreEqual( 2, loop.Controller.RiderCount );
		}
		Assert.AreNotEqual( states[0], states[1], "the head hang took the world generator's state" );
	}

	[TestMethod]
	public void LoadingWaitingGuestsHangsTheirHeadsLikeTheCreationDoes()
	{
		// [BIN:TPW-EXE:0x00549B80 load vehicle] the load calls 0x549C60 for the riders it takes; the last seated rider with a head dummy leaves the generator reseeded with it
		var scripts = new RideScriptWorld( 1 ) { HeadHangReseeds = true };
		var loop = new Loop( -5, scripts.Bumpers );
		loop.Controller.SeatHeadSource = _ => new[] { true, true };
		loop.Controller.Command( 7, 0 );
		loop.Controller.Command( 4, 0 );
		Assert.AreEqual( 0, loop.Controller.RiderCount, "a boat without riders" );
		var untouched = scripts.World.State;
		loop.Controller.Command( 1, 7 );
		loop.Controller.Command( 1, 8 );
		Assert.AreEqual( untouched, scripts.World.State, "waiting guests are not hung" );
		Assert.AreEqual( 1, loop.Controller.Command( 12, 0 ) );
		Assert.AreEqual( 2, loop.Controller.RiderCount );
		// the relaunch of the loaded boat may draw (aiming jitter), so the load's hang is read by replaying it on a fresh run without the hook
		var plain = new RideScriptWorld( 1 );
		var other = new Loop( -5, plain.Bumpers );
		other.Controller.SeatHeadSource = _ => new[] { true, true };
		other.Controller.Command( 7, 0 );
		other.Controller.Command( 4, 0 );
		other.Controller.Command( 1, 7 );
		other.Controller.Command( 1, 8 );
		other.Controller.Command( 12, 0 );
		Assert.AreNotEqual( plain.World.State, scripts.World.State, "the load's hang changed the generator" );
	}

	[TestMethod]
	public void TheBumpCommandsDriveTheLifecycle()
	{
		var loop = new Loop();
		var c = loop.Controller;
		Assert.AreEqual( 1, c.Command( 5, 0 ), "BUMP 5 is the track-valid flag" );
		Assert.AreEqual( 0, c.Phase );
		Assert.AreEqual( 0, c.Command( 1, 7 ), "BUMP 1 is refused while the ride is closed" );
		c.Command( 7, 0 );
		Assert.AreEqual( BumpController.PhaseRunning, c.Phase, "a water ride opens straight into the running phase" );
		Assert.AreEqual( 1, c.Command( 1, 7 ) );
		Assert.AreEqual( 1, c.Command( 1, 8 ) );
		CollectionAssert.AreEqual( new[] { 8, 7 }, c.Pending, "the newest guest is first" );
		Assert.AreEqual( 0, c.Command( 11, 0 ), "no boats yet" );
		Assert.AreEqual( 1, c.Command( 4, 0 ) );
		Assert.AreEqual( 1, c.Command( 11, 0 ) );
		Assert.AreEqual( 0, c.Pending.Count, "the launched boat took the waiting guests" );
		Assert.AreEqual( 2, c.RiderCount );
		Assert.AreEqual( 0, c.Command( 2, 0 ), "nobody has left yet" );
		// closing sends waiting guests to the leaving list behind those already there
		c.Command( 1, 9 );
		c.Command( 6, 0 );
		Assert.AreEqual( 0, c.Phase );
		CollectionAssert.AreEqual( new[] { 9 }, c.Leaving );
		Assert.AreEqual( 9, c.Command( 2, 0 ) );
		Assert.AreEqual( 0, c.Command( 2, 0 ) );
		// BUMP 8: broken (state 2) and fixed; BUMP 9: worn out and fixed; BUMP 13 and 14: duration and laps
		c.Command( 8, 1 );
		Assert.AreEqual( BumpController.StateBroken, c.State );
		c.Command( 8, 0 );
		Assert.AreEqual( BumpController.StateFine, c.State );
		c.Command( 9, 1 );
		Assert.AreEqual( BumpController.StateWorn, c.State );
		c.Command( 9, 0 );
		Assert.AreEqual( BumpController.StateFine, c.State );
		Assert.AreEqual( 5, c.Command( 13, 5 ) );
		Assert.AreEqual( 150, c.Laps, "BUMP 13 stores seconds × 30" );
		Assert.AreEqual( 5, c.Command( 14, 5 ) );
		Assert.AreEqual( -5, c.Laps, "BUMP 14 stores minus the parameter" );
		c.Command( 17, 1 );
		Assert.IsFalse( c.WaterRunning, "a nonzero parameter stops the water (animation 12)" );
		c.Command( 17, 0 );
		Assert.IsTrue( c.WaterRunning, "zero starts it (animation 5)" );
	}

	[TestMethod]
	public void ReopeningLaunchLimitsAndAnInvalidCircuit()
	{
		var loop = new Loop();
		var c = loop.Controller;
		c.Command( 7, 0 );
		for ( var boat = 0; boat < 80; boat++ )
			c.Command( 4, 0 );
		Assert.AreEqual( 64, c.VehicleCount, "at most 64 boats" );
		c.EjectAll();
		Assert.AreEqual( 0, c.VehicleCount );
		Assert.AreEqual( BumpController.PhaseRunning, c.Phase, "a water ride stays open when its last boat leaves" );
		// an invalid circuit: BUMP 5 reads 0 and no sections exist
		var open = TrackCircuit.Invalid( "open" );
		c.Rebuild( open );
		Assert.AreEqual( 0, c.Command( 5, 0 ) );
		Assert.AreEqual( 0, c.Sections.Count );
		Assert.AreEqual( 0, c.Phase, "a re-walk closes the ride" );
		Assert.AreEqual( 1, c.Buoys.Count, "only the station's finish buoy remains" );
	}

	// ---- Section geometry ----------------------------------------------------------------------------

	[TestMethod]
	public void SectionsCollisionObjectsAndBuoysUseTheOriginalFixedPointUnits()
	{
		var loop = new Loop();
		var c = loop.Controller;
		Assert.AreEqual( 257, BumperTemplates.Get( -5 ).Slack );
		// the first two straights (the station) carry no buoy; the corners and the later straights do
		CollectionAssert.AreEqual( new[] { 9, 9, 7, 6, 9, 9, 5, 8 }, c.Sections.Select( section => section.Kind ).ToArray() );
		CollectionAssert.AreEqual( new[] { true, true, false, false, false, false, false, false }, c.Sections.Select( section => section.NoBuoy ).ToArray() );
		Assert.AreEqual( 0x10009, c.Sections[0].KindWord );
		// the station straight: a box at the tile centre with half extents (0xC20, s)
		var box = c.Sections[0].Objects.Single();
		Assert.AreEqual( (0x3F, 33792, 33792, 0xC20, 257), (box.Flags, box.X, box.Y, box.Extent, box.ExtentY) );
		Assert.IsTrue( box.IsBox && box.IsFull && !box.IsKeepOut );
		// the corner (14, 10) has mask 0x50: pivot (X0, Y0 + 0x1800), keep-in 0xC00 + s over the +X/−Y quadrant, keep-out 0xC00 − s
		var corner = c.Sections[2];
		Assert.AreEqual( (43008, 30720), (corner.X, corner.Y) );
		Assert.AreEqual( 2, corner.Objects.Count );
		var keepIn = corner.Objects[0];
		var keepOut = corner.Objects[1];
		Assert.AreEqual( (0x15, 43008, 36864, 3329), (keepIn.Flags, keepIn.X, keepIn.Y, keepIn.Extent) );
		Assert.AreEqual( (0x55, 43008, 36864, 2815), (keepOut.Flags, keepOut.X, keepOut.Y, keepOut.Extent) );
		Assert.IsTrue( keepOut.IsKeepOut );
		Assert.AreEqual( (45180, 34692), (corner.Buoy!.X, corner.Buoy.Y), "the 45° point of the centreline arc: pivot + (0x87C, −0x87C)" );
		Assert.AreEqual( corner.Objects[0], corner.Buoy.Object );
		// the buoy list: the finish buoy first, then the section buoys in walk order
		var station = loop.Station.Reference;
		Assert.AreEqual( (36864, 33792), station, "the junction between the two station tiles" );
		var finish = c.Buoys[0];
		Assert.AreEqual( (5, 33792, 33792), (finish.Flags, finish.X, finish.Y), "0xC00 behind the station reference: the first tile's centre" );
		Assert.AreEqual( (35840, 33792), (c.UnloadX, c.UnloadY), "a third of that offset from the station" );
		CollectionAssert.AreEqual( new[] { (33792, 33792), (45180, 34692), (45180, 39036), (39936, 39936), (33792, 39936), (28548, 39036), (28548, 34692) },
			c.Buoys.Select( buoy => (buoy.X, buoy.Y) ).ToArray() );
		Assert.AreEqual( c.Buoys[1], c.FirstAfterFinish );
		// the four corner kinds: pivots and buoy offsets in a 0x1800 tile at the origin
		foreach ( var (mask, kind, flags, pivotX, pivotY, buoyX, buoyY) in new[]
		{
			((byte)0x05, 5, 0x0B, 0x1800, 0, 0x1800 - 0x87C, 0x87C),
			((byte)0x41, 6, 0x0D, 0, 0, 0x87C, 0x87C),
			((byte)0x50, 7, 0x15, 0, 0x1800, 0x87C, 0x1800 - 0x87C),
			((byte)0x14, 8, 0x13, 0x1800, 0x1800, 0x1800 - 0x87C, 0x1800 - 0x87C)
		} )
		{
			var step = new TrackStep( 0, 0, TrackHeading.PosX, TrackHeading.PosX, mask, 1, false );
			Assert.AreEqual( kind, step.Kind );
			var built = TrackSections.Build( new[] { step }, 257, Array.Empty<Buoy>() ).Sections.Single();
			Assert.AreEqual( (flags, pivotX, pivotY), (built.Objects[0].Flags, built.Objects[0].X, built.Objects[0].Y), $"kind {kind}" );
			Assert.AreEqual( (buoyX, buoyY), (built.Buoy!.X, built.Buoy.Y), $"kind {kind} buoy" );
		}
	}

	[TestMethod]
	public void CrossingsAndAddOnStraightsBuildTheirObjects()
	{
		var crossing = TrackSections.Build( new[] { new TrackStep( 2, 3, TrackHeading.PosX, TrackHeading.PosX, 0x55, 1, false ) }, 257, Array.Empty<Buoy>() ).Sections.Single();
		Assert.AreEqual( 11, crossing.Kind );
		Assert.AreEqual( 5, crossing.Objects.Count, "one keep-in circle and four keep-out circles" );
		Assert.AreEqual( ((int)Math.Sqrt( 257.0 * 257 + 3072.0 * 3072 ), 2 * 3072 + 3072, 3 * 3072 + 3072), (crossing.Objects[0].Extent, crossing.Objects[0].X, crossing.Objects[0].Y) );
		CollectionAssert.AreEqual( new[] { 2815, 2815, 2815, 2815 }, crossing.Objects.Skip( 1 ).Select( item => item.Extent ).ToArray() );
		CollectionAssert.AreEquivalent( new[] { (6144, 9216), (6144 + 6144, 9216), (6144, 9216 + 6144), (6144 + 6144, 9216 + 6144) },
			crossing.Objects.Skip( 1 ).Select( item => (item.X, item.Y) ).ToArray() );
		var addOn = TrackSections.Build( new[] { new TrackStep( 0, 0, TrackHeading.PosY, TrackHeading.PosY, 0x11, 1, true ) }, 257, Array.Empty<Buoy>() ).Sections.Single();
		Assert.AreEqual( 13, addOn.Kind );
		Assert.AreEqual( CollisionObject.AddOn, addOn.Objects[0].Flags & CollisionObject.AddOn );
		Assert.AreEqual( (257, 0xC20), (addOn.Objects[0].Extent, addOn.Objects[0].ExtentY), "a straight along Y is narrow in X" );
	}

	[TestMethod]
	public void SectionStatisticsCountCornersRunsAndCrossingsLikeTheOriginal()
	{
		var loop = new Loop();
		// 8 tiles: station straights and two straights, four corners; the longest run wraps over the join from (8, 12) ... (14, 10)
		var (corners, run, crossings, addOns, sections) = loop.Controller.SectionStatistics();
		Assert.AreEqual( (4, 2, 0, 0, 8), (corners, run, crossings, addOns, sections) );
		// the excitement base: 75 × 60 / 100 + (2 per corner + the run) = 45 + 10
		Assert.AreEqual( 55, loop.Controller.ExcitementBase( 75 ) );
		var bare = new BumpController( BumperTemplates.Get( -5 ), () => 0 );
		Assert.AreEqual( 45, bare.ExcitementBase( 75 ), "a ride without a track still has 60 % of its level" );
		Assert.AreEqual( 100, bare.ExcitementBase( 200 ), "clamped to 100" );
	}

	// ---- Golden run ----------------------------------------------------------------------------------

	[TestMethod]
	public void ABoatRunsOneLapAndUnloadsItsGuests()
	{
		var loop = new Loop();
		var c = loop.Controller;
		c.Open();
		c.AddGuest( 7 );
		c.AddGuest( 8 );
		Assert.IsTrue( c.Launch() );
		var boat = c.Vehicles.Single();
		Assert.AreEqual( (36864, 33792, 0, -1), (boat.X, boat.Y, boat.Heading, boat.Laps), "the boat starts at the station reference with heading 0 (+Y, TRACK-012), one lap to go" );
		Assert.AreEqual( c.FirstAfterFinish!.Index, boat.TargetBuoy );
		Assert.AreEqual( BumpVehicle.Live | BumpVehicle.Steerable | BumpVehicle.LapMode | BumpVehicle.Retarget | BumpVehicle.Launched, boat.Flags );
		loop.Run( 1 );
		Assert.AreEqual( (36864, 33792, 0, 0, 10), (boat.X, boat.Y, boat.VX, boat.VY, boat.Heading), "the boat turns toward its aim by the turn rate (10) before it thrusts: it stands still while the angle error is large" );
		loop.Run( 3 );
		Assert.AreEqual( (36864, 0, 40), (boat.X, boat.VX, boat.Heading) );
		loop.Run( 97 );
		Assert.AreEqual( (44011, 33928, 91, 10, 80), (boat.X, boat.Y, boat.VX, boat.VY, boat.Heading), "tick 101: on the straight, aiming at the first corner buoy with its jitter" );
		loop.Run( 200 );
		Assert.AreEqual( (39733, 39855, -101, 0, 382, 4, 6), (boat.X, boat.Y, boat.VX, boat.VY, boat.Heading, boat.TargetBuoy, boat.ObjectIndex), "tick 301: on the far straight" );
		Assert.AreEqual( (190, 134, -424, -3), (boat.JitterX, boat.JitterY, boat.Spin, boat.Roll) );
		loop.Run( 285 );
		Assert.AreEqual( (31659, 33929, 76, 0, 142, 0, -1), (boat.X, boat.Y, boat.VX, boat.VY, boat.Heading, boat.TargetBuoy, boat.Laps), "tick 586: nearing the finish buoy, the lap counter still at -1" );
		loop.Run( 23 );
		Assert.AreEqual( (33682, 33911, 98, -7, 182, 1, 0), (boat.X, boat.Y, boat.VX, boat.VY, boat.Heading, boat.TargetBuoy, boat.Laps), "tick 609: past the finish buoy (the first tile's centre), the lap counter at 0" );
		Assert.AreNotEqual( 0, boat.Flags & BumpVehicle.ToUnload, "the finished boat heads for the unload point" );
		loop.Run( 19 );
		Assert.AreEqual( 1, c.VehicleCount, "tick 628: still on its way" );
		loop.Run( 1 );
		Assert.AreEqual( 0, c.VehicleCount, "tick 629: unloaded" );
		Assert.AreEqual( 1, c.CompletedRuns );
		CollectionAssert.AreEqual( new[] { 7, 8 }, c.Leaving, "the guests wait for BUMP 2 in boarding order" );
		Assert.AreEqual( 0, c.RiderCount );
		Assert.AreEqual( BumpController.PhaseRunning, c.Phase );
	}

	[TestMethod]
	public void TheRunIsDeterministicAndAdvanceRunsOneTickPer31Milliseconds()
	{
		string Trace( Loop loop )
		{
			loop.Controller.Open();
			loop.Controller.AddGuest( 1 );
			loop.Controller.Launch();
			var boat = loop.Controller.Vehicles.Single();
			var sb = new System.Text.StringBuilder();
			for ( var tick = 0; tick < 400; tick++ )
			{
				loop.Controller.Tick();
				sb.Append( $"{boat.X},{boat.Y},{boat.Heading};" );
			}
			return sb.ToString();
		}
		Assert.AreEqual( Trace( new Loop() ), Trace( new Loop() ) );
		var timed = new Loop();
		timed.Controller.Open();
		timed.Controller.Launch();
		timed.Controller.Advance( 1.0 );
		Assert.AreEqual( 0.031, BumpController.TickSeconds, "the controller ticks once per 31 ms world substep ([BIN:TPW-EXE:0x54F4C4], TRACK-011), not at 30 Hz" );
		Assert.AreEqual( 30, BumpController.TicksPerSecond, "the duration unit of BUMP 13 stays 30" );
		Assert.AreEqual( 32, timed.Controller.TickCount, "one second is 32 ticks of 31 ms (0.992 s) with 8 ms left over" );
		timed.Controller.Advance( 1.0 / 60 );
		Assert.AreEqual( 32, timed.Controller.TickCount, "the carried 8 ms and a 60 Hz step stay below one tick" );
		timed.Controller.Advance( 1.0 / 60 );
		Assert.AreEqual( 33, timed.Controller.TickCount );
	}

	[TestMethod]
	public void ABrokenRideStopsItsBoatsAndTwoLapsTakeTwiceAsLong()
	{
		var broken = new Loop();
		broken.Controller.Open();
		broken.Controller.Launch();
		broken.Run( 60 );
		var speed = broken.Controller.Vehicles.Single().Speed;
		Assert.IsTrue( speed > 50 );
		broken.Controller.Command( 8, 1 );
		broken.Run( 200 );
		Assert.IsTrue( broken.Controller.Vehicles.Single().Speed < 5, "a broken ride damps by 1000 / 1024 and neither steers nor thrusts: the boat drifts to a stop" );
		broken.Controller.Command( 8, 0 );
		broken.Run( 100 );
		Assert.IsTrue( broken.Controller.Vehicles.Single().Speed > 20, "a repaired ride thrusts again" );
		var twice = new Loop();
		twice.Controller.Open();
		twice.Controller.Command( 14, 2 );
		twice.Controller.AddGuest( 5 );
		twice.Controller.Launch();
		twice.Run( 700 );
		Assert.AreEqual( 1, twice.Controller.VehicleCount, "after the first lap of two the boat keeps going" );
		twice.Run( 700 );
		Assert.AreEqual( 0, twice.Controller.VehicleCount );
		CollectionAssert.AreEqual( new[] { 5 }, twice.Controller.Leaving );
	}

	[TestMethod]
	public void BoatsPushEachOtherApartAndAReWalkEjectsThem()
	{
		var loop = new Loop();
		var c = loop.Controller;
		c.Open();
		c.AddGuest( 1 );
		c.Launch();
		loop.Run( 30 );
		c.AddGuest( 2 );
		c.Launch();
		Assert.AreEqual( 2, c.VehicleCount );
		var (first, second) = (c.Vehicles[0], c.Vehicles[1]);
		Assert.IsTrue( BumpMath.Distance( second.X - first.X, second.Y - first.Y ) < 2 * 1024, "the new boat starts inside the first one's radius" );
		loop.Run( 1 );
		Assert.IsTrue( BumpMath.Distance( second.X - first.X, second.Y - first.Y ) >= 2 * 1024 - 8, "one tick of vehicle collisions pushes overlapping boats apart to the sum of their radii" );
		loop.Run( 60 );
		Assert.IsTrue( BumpMath.Distance( second.X - first.X, second.Y - first.Y ) > 1024, "the boats stay apart" );
		// a track edit ejects the boats: their guests go to the leaving list and the ride closes
		c.Rebuild( loop.Circuit );
		Assert.AreEqual( 0, c.VehicleCount );
		Assert.AreEqual( 0, c.Phase );
		CollectionAssert.AreEquivalent( new[] { 1, 2 }, c.Leaving );
		Assert.AreEqual( 0, c.RiderCount );
	}

	[TestMethod]
	public void CollisionTestsFollowTheQuadrantBitsAndAdjacencyRules()
	{
		var full = new CollisionObject { Flags = 0x1F, X = 0, Y = 0, Extent = 100, ExtentY = 100 };
		Assert.IsTrue( BumpController.Contains( 0, 0, full ), "the centre is inside" );
		Assert.IsTrue( BumpController.Contains( 60, -60, full ) );
		Assert.IsTrue( BumpController.Contains( 70, -70, full ), "98.99 is within the radius" );
		Assert.IsFalse( BumpController.Contains( 80, 80, full ) );
		// a clipped corner object holds only its quadrant: x ≤ centre and y > centre
		var quarter = new CollisionObject { Flags = 0x01 | 0x02 | 0x08, X = 1000, Y = 1000, Extent = 500, ExtentY = 500 };
		Assert.IsTrue( BumpController.Contains( 900, 1100, quarter ) );
		Assert.IsFalse( BumpController.Contains( 1100, 1100, quarter ), "the other side of the pivot is outside" );
		Assert.IsFalse( BumpController.Contains( 900, 900, quarter ) );
		var boxed = new CollisionObject { Flags = 0x3F, X = 0, Y = 0, Extent = 300, ExtentY = 50 };
		Assert.IsTrue( BumpController.Contains( -290, 40, boxed ) );
		Assert.IsFalse( BumpController.Contains( 0, 60, boxed ) );
		// adjacency: an object is adjacent to itself, two full objects when their centres are nearer than the sum of their sizes
		Assert.IsTrue( BumpController.Adjacent( full, full ) );
		var near = new CollisionObject { Flags = 0x1F, X = 150, Y = 0, Extent = 100, ExtentY = 100 };
		var far = new CollisionObject { Flags = 0x1F, X = 250, Y = 0, Extent = 100, ExtentY = 100 };
		Assert.IsTrue( BumpController.Adjacent( full, near ) );
		Assert.IsFalse( BumpController.Adjacent( full, far ) );
	}

	[TestMethod]
	public void TheStateSavesAndRestoresExactly()
	{
		var loop = new Loop();
		var c = loop.Controller;
		c.Open();
		c.AddGuest( 3 );
		c.Launch();
		c.AddGuest( 4 );
		loop.Run( 250 );
		var saved = TrackSave.CaptureController( c, 17 );
		var json = System.Text.Json.JsonSerializer.Serialize( saved, new System.Text.Json.JsonSerializerOptions { IncludeFields = true } );
		var back = System.Text.Json.JsonSerializer.Deserialize<BumpControllerSaveData>( json, new System.Text.Json.JsonSerializerOptions { IncludeFields = true } )!;
		var other = new Loop();
		other.Controller.Restore( back );
		var (original, restored) = (c.Vehicles.Single(), other.Controller.Vehicles.Single());
		Assert.AreEqual( (original.X, original.Y, original.VX, original.VY, original.Heading, original.TargetBuoy, original.Spin), (restored.X, restored.Y, restored.VX, restored.VY, restored.Heading, restored.TargetBuoy, restored.Spin) );
		CollectionAssert.AreEqual( c.Pending, other.Controller.Pending );
		// the two controllers now run the same trajectory (the world draws are the loop's own generator, which restarts: align it)
		var (hashA, hashB) = (new StateHasher(), new StateHasher());
		c.AddCanonicalState( hashA );
		other.Controller.AddCanonicalState( hashB );
		Assert.AreEqual( hashA.Value, hashB.Value );
	}

	// ---- Review fixes ------------------------------------------------------------------------------

	[TestMethod]
	public void ACircuitBeyondThePoolsIsInvalidAndBuildsNoGeometry()
	{
		var loop = new Loop();
		Assert.IsTrue( loop.Controller.IsTrackValid );
		var baseline = loop.Controller.Buoys.Count;
		Assert.IsTrue( baseline > 1 );
		// 1100 distinct straights overflow the 1024-section pool
		var steps = Enumerable.Range( 0, 1100 ).Select( index => new TrackStep( 2 * index, 0, TrackHeading.PosX, TrackHeading.PosX, 0x44, index + 1, false ) ).ToList();
		var result = TrackSections.Build( steps, loop.Controller.Template.Slack, Array.Empty<Buoy>() );
		Assert.IsTrue( result.Truncated );
		loop.Controller.Rebuild( new TrackCircuit( true, steps, null ) );
		Assert.IsFalse( loop.Controller.IsTrackValid, "the walk said valid, the pools said no" );
		Assert.AreEqual( 0, loop.Controller.Sections.Count );
		Assert.AreEqual( 0, loop.Controller.Objects.Count );
		Assert.AreEqual( 0, loop.Controller.VehicleCount );
		// an ordinary circuit rebuilds as valid again
		loop.Controller.Rebuild( loop.Circuit );
		Assert.IsTrue( loop.Controller.IsTrackValid );
		Assert.AreEqual( baseline, loop.Controller.Buoys.Count );
	}

	[TestMethod]
	public void AdvanceIgnoresNegativeAndNonFiniteTimeAndKeepsTheRemainderBelowOneTick()
	{
		var loop = new Loop();
		var start = loop.Controller.TickCount;
		loop.Controller.Advance( double.NaN );
		loop.Controller.Advance( double.PositiveInfinity );
		loop.Controller.Advance( -5 );
		Assert.AreEqual( start, loop.Controller.TickCount );
		Assert.AreEqual( 0, loop.Controller.TickRemainder );
		loop.Controller.Advance( 0.1 );
		Assert.AreEqual( start + 3, loop.Controller.TickCount );
		Assert.IsTrue( loop.Controller.TickRemainder >= 0 && loop.Controller.TickRemainder < BumpController.TickSeconds );
	}

	[TestMethod]
	public void BoatsThatReachTheUnloadPointRetireTogetherWithoutSkippingNeighbours()
	{
		var loop = new Loop();
		var bump = loop.Controller;
		bump.Open();
		for ( var count = 0; count < 3; count++ )
		{
			Assert.IsTrue( bump.AddGuest( 100 + count ) );
			Assert.IsTrue( bump.Launch() );
		}
		Assert.AreEqual( 3, bump.VehicleCount );
		foreach ( var vehicle in bump.Vehicles )
		{
			vehicle.X = bump.UnloadX;
			vehicle.Y = bump.UnloadY;
			vehicle.Flags = BumpVehicle.Live | BumpVehicle.ToUnload | BumpVehicle.Retarget | BumpVehicle.Steerable;
		}
		bump.Tick();
		Assert.AreEqual( 0, bump.VehicleCount, "each boat retired in its own step; none was skipped by the list shrinking" );
		Assert.AreEqual( 3, bump.CompletedRuns );
		Assert.AreEqual( 0, bump.Vehicles.Count );
		CollectionAssert.AreEquivalent( new[] { 100, 101, 102 }, bump.Leaving.ToArray() );
	}

	[TestMethod]
	public void TheCanonicalStateCoversEveryRestoredBoatField()
	{
		var loop = new Loop();
		var bump = loop.Controller;
		bump.Open();
		bump.AddGuest( 21 );
		bump.Launch();
		loop.Run( 40 );
		var boat = bump.Vehicles.First();
		ulong Hash() { var hash = new StateHasher(); bump.AddCanonicalState( hash ); return hash.Value; }
		var reference = Hash();
		var changes = new Dictionary<string, Action>
		{
			["Variant"] = () => boat.Variant++, ["VX0"] = () => boat.VX0++, ["VY0"] = () => boat.VY0++, ["Speed"] = () => boat.Speed++, ["Roll"] = () => boat.Roll++,
			["Radius"] = () => boat.Radius++, ["AccelBonus"] = () => boat.AccelBonus++, ["TargetX"] = () => boat.TargetX++, ["TargetY"] = () => boat.TargetY++,
			["JitterX"] = () => boat.JitterX++, ["JitterY"] = () => boat.JitterY++, ["BuoyCountdown"] = () => boat.BuoyCountdown++, ["Age"] = () => boat.Age++,
			["ObjectIndex"] = () => boat.ObjectIndex++, ["WallHits"] = () => boat.WallHits++, ["WallWindow"] = () => boat.WallWindow++, ["Sprays"] = () => boat.Sprays++,
			["Rider"] = () => boat.Riders[0]++
		};
		foreach ( var (name, change) in changes )
		{
			change();
			var changed = Hash();
			Assert.AreNotEqual( reference, changed, $"{name} is part of the digest" );
			reference = changed;
		}
	}

	[TestMethod]
	public void RestoreRefusesBoatsThatAimAtBuoysOrObjectsTheWalkDoesNotHave()
	{
		var loop = new Loop();
		var bump = loop.Controller;
		bump.Open();
		bump.AddGuest( 21 );
		bump.Launch();
		loop.Run( 10 );
		var saved = TrackSave.CaptureController( bump, 1 );
		bump.Restore( saved );
		var badBuoy = saved with { Vehicles = saved.Vehicles.Select( vehicle => vehicle with { TargetBuoy = bump.Buoys.Count } ).ToList() };
		Assert.ThrowsException<System.IO.InvalidDataException>( () => bump.Restore( badBuoy ) );
		var badObject = saved with { Vehicles = saved.Vehicles.Select( vehicle => vehicle with { ObjectIndex = bump.Objects.Count } ).ToList() };
		Assert.ThrowsException<System.IO.InvalidDataException>( () => bump.Restore( badObject ) );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => bump.Restore( saved with { TickRemainder = BumpController.MaximumSavedRemainder } ) );
		Assert.AreEqual( 1, bump.VehicleCount, "a refused restore left the boats alone" );
	}
}
