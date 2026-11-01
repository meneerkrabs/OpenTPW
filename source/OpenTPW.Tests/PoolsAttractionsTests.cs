using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using OpenTPW.UI.Original;
using OpenTPW.Hud;
using static OpenTPW.Tests.PoolsSwimSessionTests;

namespace OpenTPW.Tests;

/// <summary>
/// The water attractions of the pools extension (docs/POOLS.md, "Water attractions"): the placement geometry, the wave settings and the waves, the lazy river's rings and current, and the four
/// activities (rope swing, jets, spa, river) in the swim session's rig, with saves in the middle of each.
/// </summary>
[TestClass]
public class PoolsAttractionsTests
{
	// the swim rig's pool is x 5..10, y 3..8; its steps are (5, 3)
	private static readonly PoolAttractionSite Rope = new( 4301, PoolAttractionKind.RopeSwing, new[] { (11, 5) }, (10, 5) );
	private static readonly PoolAttractionSite Jets = new( 4302, PoolAttractionKind.Jets, new[] { (7, 2) }, (7, 3) );
	private static readonly PoolAttractionSite Spa = new( 4303, PoolAttractionKind.Spa, new[] { (4, 6) }, (5, 6) );
	private static readonly PoolAttractionSite BigSpa = new( 4304, PoolAttractionKind.Spa, new[] { (3, 6), (4, 6), (3, 7), (4, 7) }, (5, 6) );

	private static PoolAttractionSites Sites( params PoolAttractionSite[] sites ) => new( () => sites );

	private static (Rig Rig, Guest Guest) Swimmer( IPoolActivity activity )
	{
		var rig = new Rig();
		rig.Simulation.RegisterPoolActivity( activity );
		return (rig, rig.Send());
	}

	private static ulong HashOf( Rig rig )
	{
		var hasher = new StateHasher();
		rig.Simulation.AddCanonicalState( hasher );
		return hasher.Value;
	}

	// ---- Placement ---------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheWaterSideTurnsWithTheObjectAndASpaTakesAnySide()
	{
		var shape = new ObjectShape( new[] { "*" } );
		var north = new[] { (0, -1) };
		// the shape's top row faces -y at rotation 0, then -x, +y, +x as the object turns by 90 degrees
		CollectionAssert.AreEqual( new[] { (10, 9) }, PoolAttractions.AccessCandidates( shape, north, 10, 10, 0 ).ToArray() );
		CollectionAssert.AreEqual( new[] { (9, 10) }, PoolAttractions.AccessCandidates( shape, north, 10, 10, 90 ).ToArray() );
		CollectionAssert.AreEqual( new[] { (10, 11) }, PoolAttractions.AccessCandidates( shape, north, 10, 10, 180 ).ToArray() );
		CollectionAssert.AreEqual( new[] { (11, 10) }, PoolAttractions.AccessCandidates( shape, north, 10, 10, 270 ).ToArray() );
		var any = new[] { (0, -1), (1, 0), (0, 1), (-1, 0) };
		CollectionAssert.AreEquivalent( new[] { (10, 9), (11, 10), (10, 11), (9, 10) }, PoolAttractions.AccessCandidates( shape, any, 10, 10, 0 ).ToArray() );
		// a wide object (the wave machine) has a candidate in front of each of its cells, none inside its own footprint
		var wide = new ObjectShape( new[] { "**" } );
		CollectionAssert.AreEqual( new[] { (10, 9), (11, 9) }, PoolAttractions.AccessCandidates( wide, north, 10, 10, 0 ).ToArray() );
		Assert.AreEqual( 4, PoolAttractions.AccessCandidates( wide, any, 10, 10, 0 ).Count - 2, "the sides inside the footprint are not candidates: 6 outside cells for a 2 x 1 object" );
	}

	[TestMethod]
	public void TheKindsParseAndAnythingElseIsNoWaterAttraction()
	{
		Assert.AreEqual( PoolAttractionKind.RopeSwing, PoolAttractions.ParseKind( "ropeswing" ) );
		Assert.AreEqual( PoolAttractionKind.Jets, PoolAttractions.ParseKind( " Jets " ) );
		Assert.AreEqual( PoolAttractionKind.WaveMachine, PoolAttractions.ParseKind( "WAVEMACHINE" ) );
		Assert.AreEqual( PoolAttractionKind.Spa, PoolAttractions.ParseKind( "spa" ) );
		Assert.AreEqual( PoolAttractionKind.None, PoolAttractions.ParseKind( "slide" ) );
		Assert.AreEqual( PoolAttractionKind.None, PoolAttractions.ParseKind( null ) );
	}

	// ---- The waves ---------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheWaveSettingsRoundTripThroughTheFlagsOfOneCellOfThePool()
	{
		foreach (var on in new[] { true, false })
			for ( var height = 1; height <= 3; height++ )
				for ( var interval = 0; interval <= 2; interval++ )
				{
					var settings = new PoolWaveSettings( on, height, interval );
					Assert.AreEqual( settings, PoolWaveSettings.FromFlags( settings.ToFlags() ), $"{on} {height} {interval}" );
					Assert.AreEqual( 0, (int)(settings.ToFlags() & ~ParkCellFlags.PoolWaveBits), "only the wave bits" );
				}
		Assert.IsNull( PoolWaveSettings.FromFlags( ParkCellFlags.None ), "no height stored: no settings" );
		Assert.IsNull( PoolWaveSettings.FromFlags( ParkCellFlags.PoolRiver ), "the river bit is none of them" );
		var rig = new Rig();
		var access = (6, 5);
		Assert.AreEqual( PoolWaveSettings.Default, PoolWaveSettings.At( rig.Cells, access ), "a cell that holds none gives the default" );
		PoolWaveSettings.SetAt( rig.Cells, access, new PoolWaveSettings( false, 3, 0 ) );
		Assert.AreEqual( new PoolWaveSettings( false, 3, 0 ), PoolWaveSettings.At( rig.Cells, access ) );
		Assert.AreEqual( 1, rig.Region.Cells.Count( cell => PoolWaveSettings.Has( rig.Cells.FlagsAt( cell.X, cell.Y ) ) ), "stored on that cell alone" );
		rig.Cells.SetFlags( 6, 5, rig.Cells.FlagsAt( 6, 5 ) | ParkCellFlags.PoolRiver );
		PoolWaveSettings.SetAt( rig.Cells, access, new PoolWaveSettings( true, 1, 2 ) );
		Assert.IsTrue( rig.Cells.FlagsAt( 6, 5 ).HasFlag( ParkCellFlags.PoolRiver ), "the other flags stay" );
		Assert.AreEqual( new PoolWaveSettings( true, 1, 2 ), PoolWaveSettings.At( rig.Cells, access ) );
	}

	[TestMethod]
	public void ThePoolsWavesAreItsWaveMachinesAndNeverOutliveThem()
	{
		var rig = new Rig();
		var machineA = new PoolAttractionSite( 20, PoolAttractionKind.WaveMachine, new[] { (4, 5) }, (5, 5) );
		var machineB = new PoolAttractionSite( 10, PoolAttractionKind.WaveMachine, new[] { (11, 5) }, (10, 5) );
		var list = new List<PoolAttractionSite>();
		var sites = new PoolAttractionSites( () => list.ToArray() );
		PoolWaveSettings.SetAt( rig.Cells, (5, 5), new PoolWaveSettings( true, 3, 0 ) );    // bits on a cell with no machine
		Assert.IsNull( PoolWaveSettings.ActiveIn( sites, rig.Cells, rig.Region ), "no machine: no waves, whatever the cells hold" );
		list.Add( machineA );
		Assert.AreEqual( new PoolWaveSettings( true, 3, 0 ), PoolWaveSettings.ActiveIn( sites, rig.Cells, rig.Region ) );
		list.Add( machineB );
		Assert.AreEqual( PoolWaveSettings.Default, PoolWaveSettings.ActiveIn( sites, rig.Cells, rig.Region ), "two machines: the lowest object id, here the one with no stored settings (the default)" );
		PoolWaveSettings.SetAt( rig.Cells, (10, 5), new PoolWaveSettings( false, 2, 1 ) );
		Assert.AreEqual( new PoolWaveSettings( true, 3, 0 ), PoolWaveSettings.ActiveIn( sites, rig.Cells, rig.Region ), "the lowest id is off: the next one that is on" );
		list.Clear();
		Assert.IsNull( PoolWaveSettings.ActiveIn( sites, rig.Cells, rig.Region ), "the machines are gone: the waves are gone with them, and a new one starts from its own cell" );
		var far = new PoolAttractionSite( 5, PoolAttractionKind.WaveMachine, new[] { (30, 30) }, (31, 30) );
		list.Add( far );
		Assert.IsNull( PoolWaveSettings.ActiveIn( sites, rig.Cells, rig.Region ), "a machine of another pool gives this one nothing" );
	}

	[TestMethod]
	public void TheWavesRunForARunThenRestAndAreAPureFunctionOfTheTurn()
	{
		foreach ( var interval in new[] { 0, 1, 2 } )
		{
			var settings = new PoolWaveSettings( true, 2, interval );
			var cycle = PoolWaves.CycleTurns( settings );
			Assert.AreEqual( PoolWaves.RunTurns + PoolWaves.RestTurns[interval], cycle );
			Assert.IsTrue( Enumerable.Range( 0, PoolWaves.RunTurns ).All( turn => PoolWaves.IsRunning( settings, turn ) && PoolWaves.IsRunning( settings, turn + cycle ) ), "the run, every cycle" );
			Assert.IsTrue( Enumerable.Range( PoolWaves.RunTurns, PoolWaves.RestTurns[interval] ).All( turn => !PoolWaves.IsRunning( settings, turn ) && PoolWaves.Envelope( settings, turn ) == 0 && PoolWaves.SmoothWave( settings, turn + 0.5, 3f, 4f ) == 0f ), "the rest: flat water" );
			Assert.AreEqual( 0, PoolWaves.Envelope( settings, PoolWaves.RunTurns ) );
			Assert.AreEqual( 64, PoolWaves.Envelope( settings, PoolWaves.RunTurns / 2 ), "full swell in the middle of the run" );
			Assert.IsTrue( PoolWaves.Envelope( settings, 0 ) < 16 && PoolWaves.Envelope( settings, PoolWaves.RunTurns - 1 ) < 16, "swelling in and out" );
		}
		var off = new PoolWaveSettings( false, 3, 0 );
		Assert.IsFalse( PoolWaves.IsRunning( off, 10 ) );
		Assert.AreEqual( 0f, PoolWaves.SmoothBob( off, 10.5, 1f, 1f ) );
		Assert.AreEqual( 0f, PoolWaves.HappinessGain( off, 48 ) );
		var high = new PoolWaveSettings( true, 3, 1 );
		var seen = new HashSet<int>();
		for ( var step = 0; step < PoolWaves.RunTurns * 4; step++ )
		{
			var turn = step / 4.0;
			var wave = PoolWaves.SmoothWave( high, turn, 5f, 5f );
			Assert.AreEqual( wave, PoolWaves.SmoothWave( high, turn, 5f, 5f ), "pure" );
			Assert.IsTrue( wave is >= -1f and <= 1f );
			Assert.IsTrue( Math.Abs( PoolWaves.SmoothBob( high, turn, 5f, 5f ) ) <= PoolWaves.BobHeights[2] + 1e-6f );
			seen.Add( (int)Math.Round( wave * 64 ) );
		}
		Assert.IsTrue( seen.Count > 30 && seen.Min() < -40 && seen.Max() > 40, $"the wave swings both ways ({seen.Count} values)" );
		Assert.AreNotEqual( PoolWaves.SmoothWave( high, 60.0, 5f, 5f ), PoolWaves.SmoothWave( high, 60.0, 8f, 5f ), "the front travels: two points differ in phase" );
	}

	[TestMethod]
	public void TheWaterSurfaceRisesWithTheWaveAndTheSwimmersRideIt()
	{
		// the shader raises the water mesh by kMaxHeave times its strength (the envelope times height / 3); a swimmer bobs by the same height, and a crest stays under the deck
		Assert.IsTrue( PoolWaves.MaxHeave > 0f && PoolWaves.MaxHeave < PoolMeshOptions.Default.WaterDrop, "a crest never tops the coping" );
		for ( var height = 1; height <= 3; height++ )
			Assert.AreEqual( PoolWaves.MaxHeave * height / 3f, PoolWaves.BobHeights[height - 1], 1e-6f, $"height {height}: the bob is the surface's own rise" );
		var shader = WaterShaderText();
		StringAssert.Contains( shader, $"const float kMaxHeave = {PoolWaves.MaxHeave.ToString( System.Globalization.CultureInfo.InvariantCulture )};" );
		StringAssert.Contains( shader, "pos.z += poolWave( pos.xy ) * kMaxHeave;", "the vertex shader heaves the surface" );
		Assert.IsFalse( shader.Contains( "vertex \"" ), "the water has its own vertex shader, not the shared include" );
		// the wave number per engine unit is the phase per cell over a period's 64 steps and the cell's width
		StringAssert.Contains( shader, "const float kWaveNumber = 21.0 / 128.0;" );
		Assert.AreEqual( 21.0 / 128.0, PoolWaves.PhasePerCell / 64.0 / OriginalParkPlacement.CellSize, 1e-9 );
	}

	private static string WaterShaderText()
	{
		var directory = new System.IO.DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !System.IO.File.Exists( System.IO.Path.Combine( directory.FullName, "content", "shaders", "pool-water.shader" ) ) )
			directory = directory.Parent;
		Assert.IsNotNull( directory, "Repository shader assets must be available for this test." );
		return System.IO.File.ReadAllText( System.IO.Path.Combine( directory!.FullName, "content", "shaders", "pool-water.shader" ) ).Replace( "\r\n", "\n" );
	}

	[TestMethod]
	public void TheWaveHappinessIsPaidOncePerPeriodWhileTheWavesRun()
	{
		var settings = new PoolWaveSettings( true, 2, 1 );
		var total = 0f;
		var paid = 0;
		for ( var turn = 0; turn < PoolWaves.CycleTurns( settings ) * 2; turn++ )
		{
			var gain = PoolWaves.HappinessGain( settings, turn );
			if ( gain > 0 )
			{
				paid++;
				Assert.IsTrue( PoolWaves.IsRunning( settings, turn ) && turn % PoolWaves.PeriodTurns == 0 );
				Assert.AreEqual( PoolWaves.HappinessPerHeight * 2, gain, 1e-6f );
			}
			total += gain;
		}
		Assert.AreEqual( 10, paid, "five periods of 24 turns in a 120-turn run, two cycles" );
		Assert.IsTrue( total > 0 && total < 20, "a small bonus" );
	}

	// ---- The lazy river ----------------------------------------------------------------------------------------------

	private static ParkCellMap RingMap( int x0, int y0, int x1, int y1, bool river = true )
	{
		var cells = new ParkCellMap( 32, 32 );
		for ( var y = y0; y <= y1; y++ )
			for ( var x = x0; x <= x1; x++ )
			{
				cells.SetType( x, y, ParkCellTypes.PoolWater );
				if ( river && (x == x0 || x == x1 || y == y0 || y == y1) )
					cells.SetFlags( x, y, ParkCellFlags.PoolRiver );
			}
		return cells;
	}

	[TestMethod]
	public void ARingOfRiverCellsIsARiverWithAClockwiseCurrentWhateverItsShape()
	{
		var cells = RingMap( 4, 4, 11, 8 );
		var rivers = PoolRiverMap.Of( cells );
		Assert.AreEqual( 1, rivers.Rings.Count );
		Assert.AreEqual( 2 * (8 + 5) - 4, rivers.Rings[0].Cells.Count, "the perimeter of 8 x 5" );
		Assert.AreEqual( (4, 4), rivers.Rings[0].Cells[0], "starting at the first cell row by row" );
		Assert.AreEqual( (1, 0), rivers.FlowAt( 5, 4 ), "east along the top" );
		Assert.AreEqual( (0, 1), rivers.FlowAt( 11, 6 ), "south on the right" );
		Assert.AreEqual( (-1, 0), rivers.FlowAt( 8, 8 ), "west along the bottom" );
		Assert.AreEqual( (0, -1), rivers.FlowAt( 4, 6 ), "north on the left" );
		Assert.AreEqual( (1, 0), rivers.FlowAt( 4, 4 ), "a corner flows on to the next cell of the ring" );
		Assert.IsNull( rivers.FlowAt( 7, 6 ), "the middle of the pool is no river" );
		Assert.IsFalse( rivers.IsRiver( 7, 6 ) );
		// every cell's flow leads to a neighbour with a flow: the current goes round
		var cell = (X: 4, Y: 4);
		for ( var step = 0; step < rivers.Rings[0].Cells.Count; step++ )
			cell = rivers.NextCell( cell.X, cell.Y )!.Value;
		Assert.AreEqual( (4, 4), cell, "a lap is as long as the ring" );
		// a badly drawn L (one cell touches three others) is no ring; the true L-shaped lanes are in TheFloaterFollowsEveryShapeOfLane
		var l = new ParkCellMap( 32, 32 );
		foreach ( var (x, y) in new[] { (2, 2), (3, 2), (4, 2), (4, 3), (4, 4), (3, 4), (3, 5), (2, 5), (2, 4), (2, 3) } )
		{
			l.SetType( x, y, ParkCellTypes.PoolWater );
			l.SetFlags( x, y, ParkCellFlags.PoolRiver );
		}
		Assert.AreEqual( 0, PoolRiverMap.Build( l ).Rings.Count );
		var u = RingMap( 3, 3, 6, 6 );
		Assert.AreEqual( 1, PoolRiverMap.Build( u ).Rings.Count );
	}

	[TestMethod]
	public void FlaggedCellsThatAreNoRingAreOrdinaryWater()
	{
		var cells = new ParkCellMap( 32, 32 );
		void Flag( int x, int y )
		{
			cells.SetType( x, y, ParkCellTypes.PoolWater );
			cells.SetFlags( x, y, ParkCellFlags.PoolRiver );
		}
		for ( var x = 2; x <= 9; x++ )
			Flag( x, 2 );                                                 // a line: dead ends
		foreach ( var (x, y) in new[] { (12, 12), (13, 12), (14, 12), (13, 13), (13, 11) } )
			Flag( x, y );                                                 // a plus
		for ( var y = 20; y <= 22; y++ )
			for ( var x = 20; x <= 22; x++ )
				Flag( x, y );                                             // a solid 3 x 3 block: the middle has four neighbours
		var rivers = PoolRiverMap.Build( cells );
		Assert.AreEqual( 0, rivers.Rings.Count );
		Assert.IsFalse( rivers.Any );
		// a bare ring of the smallest size is a river (2 x 2); a ring of three cells cannot exist on a grid
		var tiny = RingMap( 5, 5, 6, 6 );
		Assert.AreEqual( 1, PoolRiverMap.Build( tiny ).Rings.Count );
		Assert.AreEqual( 4, PoolRiverMap.Build( tiny ).Rings[0].Cells.Count );
		// the same shape without the river option is plain water
		Assert.AreEqual( 0, PoolRiverMap.Build( RingMap( 4, 4, 11, 8, river: false ) ).Rings.Count );
		// the flag on a cell that is not water counts for nothing
		var dry = new ParkCellMap( 32, 32 );
		foreach ( var (x, y) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) } )
			dry.SetFlags( x, y, ParkCellFlags.PoolRiver );
		Assert.AreEqual( 0, PoolRiverMap.Build( dry ).Rings.Count );
	}

	[TestMethod]
	public void TheDerivedRiversFollowTheCellMapWithoutAStoreOfTheirOwn()
	{
		var cells = RingMap( 4, 4, 9, 8 );
		var before = PoolRiverMap.Of( cells );
		Assert.AreSame( before, PoolRiverMap.Of( cells ), "cached while the pool cells and their flags are unchanged" );
		cells.SetFlags( 6, 4, ParkCellFlags.None );
		var broken = PoolRiverMap.Of( cells );
		Assert.AreNotSame( before, broken, "a flag change counts as a pool write" );
		Assert.AreEqual( 0, broken.Rings.Count, "a gap in the ring: no river" );
		cells.SetFlags( 6, 4, ParkCellFlags.PoolRiver );
		Assert.AreEqual( 1, PoolRiverMap.Of( cells ).Rings.Count );
	}

	/// <summary>A pool that is only a lane one cell wide round the cells of an island of land (the pool cells are the lane and nothing else), every cell flagged.</summary>
	private static ParkCellMap LaneAround( IEnumerable<(int X, int Y)> island )
	{
		var land = island.ToHashSet();
		var cells = new ParkCellMap( 32, 32 );
		foreach ( var (x, y) in land.SelectMany( cell => Enumerable.Range( -1, 3 ).SelectMany( dx => Enumerable.Range( -1, 3 ).Select( dy => (X: cell.X + dx, Y: cell.Y + dy) ) ) ).Distinct().Where( cell => !land.Contains( cell ) ) )
		{
			cells.SetType( x, y, ParkCellTypes.PoolWater );
			cells.SetFlags( x, y, ParkCellFlags.PoolRiver );
		}
		return cells;
	}

	private static IEnumerable<(int X, int Y)> Block( int x0, int y0, int x1, int y1 ) =>
		Enumerable.Range( y0, y1 - y0 + 1 ).SelectMany( y => Enumerable.Range( x0, x1 - x0 + 1 ).Select( x => (X: x, Y: y) ) );

	[TestMethod]
	public void TheFloaterFollowsEveryShapeOfLaneRoundItsBendsWithoutEverLeavingFreeWater()
	{
		var islands = new Dictionary<string, (int X, int Y)[]>
		{
			["a channel round a 3 x 2 island"] = Block( 6, 6, 8, 7 ).ToArray(),
			["a channel round one cell"] = new[] { (6, 6) },
			["a lane round an L-shaped island"] = new[] { (5, 5), (6, 5), (7, 5), (5, 6), (5, 7) },
			["a lane round a T-shaped island"] = Block( 5, 5, 9, 5 ).Concat( new[] { (7, 6), (7, 7) } ).ToArray()
		};
		foreach ( var (name, island) in islands )
		{
			var cells = LaneAround( island );
			var rivers = PoolRiverMap.Of( cells );
			Assert.AreEqual( 1, rivers.Rings.Count, name );
			var ring = rivers.Rings[0].Cells;
			var region = PoolSwimRegion.Build( cells, ring[0] )!;
			Assert.AreEqual( ring.Count, region.Cells.Count, $"{name}: the pool is the lane alone" );
			foreach ( var start in new[] { 0, ring.Count / 3, 2 * ring.Count / 3 } )
			{
				var (x, y) = PoolSwimRegion.Centre( ring[start] );
				var seen = new HashSet<(int X, int Y)>();
				var turns = 0;
				// two laps of the ring at the current's speed: the float must stay in the lane and in free water at every turn
				var limit = 2 * ring.Count * Fixed16.One / PoolRiverSettings.FlowSpeed + 50;
				while ( turns < limit && seen.Count < ring.Count + 1 )
				{
					Assert.IsTrue( rivers.Float( ref x, ref y, PoolRiverSettings.FlowSpeed ), $"{name} start {start} turn {turns}" );
					turns++;
					Assert.IsTrue( rivers.IsRiver( x >> 16, y >> 16 ), $"{name} start {start} turn {turns}: ({x / 65536.0:F2}, {y / 65536.0:F2}) in the lane" );
					Assert.IsTrue( region.IsWater( x, y ), $"{name} start {start} turn {turns}: ({x / 65536.0:F2}, {y / 65536.0:F2}) is free water" );
					seen.Add( (x >> 16, y >> 16) );
				}
				Assert.AreEqual( ring.Count, seen.Count, $"{name} start {start}: every cell of the lane was passed" );
			}
		}
	}

	[TestMethod]
	public void AFloatInAChannelRoundLandRunsItsFullTurnsAndIsPaidOnlyThen()
	{
		foreach ( var island in new[] { Block( 6, 6, 9, 8 ).ToArray(), new (int X, int Y)[] { (5, 5), (6, 5), (7, 5), (5, 6), (5, 7) } } )
		{
			var rig = new Rig( capacity: 12 );
			// the swim rig's pool is replaced by the lane: erase it and lay the channel next to the changing room's access cell
			foreach ( var cell in rig.Region.Cells )
				rig.Cells.SetType( cell.X, cell.Y, (byte)0 );
			var cells = LaneAround( island.Select( cell => (cell.X, cell.Y - 2) ) );
			foreach ( var (x, y) in Block( 0, 0, 19, 9 ).Where( cell => cells.RawTypeAt( cell.X, cell.Y ) == ParkCellTypes.PoolWater ) )
			{
				rig.Cells.SetType( x, y, ParkCellTypes.PoolWater );
				rig.Cells.SetFlags( x, y, ParkCellFlags.PoolRiver );
			}
			// the access cell the room's candidates name must be water: the lane's first cell
			rig.Ride.PoolAccessCandidates = new[] { PoolRiverMap.Of( rig.Cells ).Rings[0].Cells[0] };
			rig.Simulation.RegisterPoolActivity( new RiverActivity() );
			var guest = SendAndWaitForAFloater( rig );
			var planned = guest.PoolActivityTurns;
			var before = guest.Happiness;
			var turns = rig.Until( () => guest.State != GuestState.PoolActivity, 400 );
			Assert.AreEqual( planned, turns, 2, "it floated its drawn turns, not until the first bend" );
			Assert.AreEqual( PoolRiverSettings.HappinessGain, guest.Happiness - before, 0.01f );
		}
	}

	[TestMethod]
	public void AFloaterFollowsTheCurrentRoundTheCornersStayingInTheRingAndLapsTheRing()
	{
		var cells = RingMap( 4, 4, 11, 8 );
		var rivers = PoolRiverMap.Of( cells );
		var region = PoolSwimRegion.Build( cells, (4, 4) )!;
		var (x, y) = (4 * Fixed16.One + Fixed16.One / 2, 4 * Fixed16.One + Fixed16.One / 2);
		var visited = new HashSet<(int X, int Y)>();
		var turns = 0;
		while ( turns < 5000 )
		{
			Assert.IsTrue( rivers.Float( ref x, ref y, PoolRiverSettings.FlowSpeed ), $"turn {turns}" );
			turns++;
			Assert.IsTrue( rivers.IsRiver( x >> 16, y >> 16 ), $"turn {turns}: ({x / 65536.0:F2}, {y / 65536.0:F2}) is in a river cell" );
			Assert.IsTrue( region.IsWater( x, y ), $"turn {turns}: and in free water" );
			visited.Add( (x >> 16, y >> 16) );
			if ( visited.Count == rivers.Rings[0].Cells.Count && (x >> 16, y >> 16) == (4, 4) )
				break;
		}
		Assert.AreEqual( rivers.Rings[0].Cells.Count, visited.Count, "every cell of the ring was passed" );
		var lap = rivers.Rings[0].Cells.Count * Fixed16.One / PoolRiverSettings.FlowSpeed;
		Assert.IsTrue( turns > lap * 0.8 && turns < lap * 1.3, $"a lap takes about {lap} turns at the current's speed ({turns})" );
		// the same walk twice is the same walk
		int ax = 4 * Fixed16.One + 100, ay = 4 * Fixed16.One + 40000, bx = ax, by = ay;
		for ( var i = 0; i < 400; i++ )
		{
			rivers.Float( ref ax, ref ay, PoolRiverSettings.FlowSpeed );
			rivers.Float( ref bx, ref by, PoolRiverSettings.FlowSpeed );
		}
		Assert.AreEqual( (ax, ay), (bx, by) );
		int px = 7 * Fixed16.One, py = 6 * Fixed16.One;
		Assert.IsFalse( rivers.Float( ref px, ref py, PoolRiverSettings.FlowSpeed ), "a point outside the ring is not carried" );
		Assert.AreEqual( (7 * Fixed16.One, 6 * Fixed16.One), (px, py) );
	}

	// ---- The rope swing ----------------------------------------------------------------------------------------------

	/// <summary>Every guest has been in a swim session and none is now.</summary>
	private static bool Done( List<Guest> guests, HashSet<int> joined )
	{
		foreach ( var guest in guests.Where( guest => guest.InSwimSession ) )
			joined.Add( guest.Id );
		return joined.Count == guests.Count && guests.All( guest => !guest.InSwimSession );
	}

	private static void Register( Rig rig, params IPoolActivity[] activities )
	{
		foreach ( var activity in activities )
			rig.Simulation.RegisterPoolActivity( activity );
	}

	[TestMethod]
	public void ASwimmerSwimsToTheRopeSwingClimbsSwingsSplashesInAndSwimsOnHappier()
	{
		var (rig, guest) = Swimmer( new RopeSwingActivity( Sites( Rope ) ) );
		rig.Until( () => guest.State == GuestState.PoolActivity, 2500 );
		Assert.AreEqual( RopeSwingActivity.ActivityId, guest.PoolActivityId );
		Assert.AreEqual( 4301, guest.PoolActivityTarget );
		Assert.AreEqual( (10, 5), (guest.PoolActivityX >> 16, guest.PoolActivityY >> 16), "offered at the water side" );
		var happiness = guest.Happiness;
		var slots = new List<int>();
		var phases = new List<int>();
		var heights = new List<float>();
		var activity = rig.Simulation.PoolActivityOf( guest )!;
		while ( guest.State == GuestState.PoolActivity )
		{
			slots.Add( activity.SpriteSlot( guest ) );
			phases.Add( guest.PoolActivityPhase );
			heights.Add( activity.FeetHeight( rig.Simulation, guest, new PoolFeetHeights( 0f, 0f, 0f ) ) ?? 0f );
			if ( guest.PoolActivityPhase == 1 )
				Assert.AreEqual( (11, 5), guest.Navigation.Cell, "on the platform, on the object's own cell" );
			Assert.IsTrue( phases.Count < 400, "the swing ends" );
			rig.Turns( 1 );
		}
		Assert.AreEqual( GuestState.Swimming, guest.State, "back in the water" );
		Assert.IsTrue( rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ) );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3 }, phases.Distinct().ToArray(), "approach, climb, swing, splash" );
		CollectionAssert.AreEqual( new[] { PoolSwimSlots.Crawl, PoolSwimSlots.LadderClimb, PoolSwimSlots.RopeSwing, PoolSwimSlots.SplashDown }, slots.Distinct().ToArray() );
		Assert.AreEqual( PoolAttractionSettings.SwingClimbTurns, phases.Count( phase => phase == 1 ) );
		Assert.AreEqual( PoolAttractionSettings.SwingTurns, phases.Count( phase => phase == 2 ) );
		Assert.AreEqual( PoolAttractionSettings.SplashTurns, phases.Count( phase => phase == 3 ) );
		Assert.IsTrue( heights.Max() > PoolAttractionSettings.SwingPlatformHeight * 0.9f && heights.Min() >= 0f, "it rises to the platform and comes down" );
		Assert.AreEqual( PoolAttractionSettings.SwingHappiness, guest.Happiness - happiness, 0.01f );
		Assert.IsNull( guest.PoolActivityId );
	}

	[TestMethod]
	public void OnlyOneGuestIsOnARopeSwingAtATimeAndTheOthersSwimOn()
	{
		var rig = new Rig( capacity: 6 );
		rig.Simulation.RegisterPoolActivity( new RopeSwingActivity( Sites( Rope ) ) );
		// six swimmers: the room lets one off per park turn at most (tp.exe 0x004E1410), so enough of them must be in the water together for the swing to be wanted twice
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
		var most = 0;
		var joined = new HashSet<int>();
		var users = new HashSet<int>();
		rig.Until( () =>
		{
			most = Math.Max( most, rig.Simulation.Guests.Count( guest => guest.InPoolActivity && guest.PoolActivityTarget == 4301 ) );
			foreach ( var guest in rig.Simulation.Guests.Where( guest => guest.InPoolActivity ) )
				users.Add( guest.Id );
			return Done( guests, joined );
		}, 6000 );
		Assert.AreEqual( 1, most, "one at a time" );
		Assert.IsTrue( users.Count >= 2, $"and the swing was used by {users.Count} of the swimmers in turn" );
	}

	[TestMethod]
	public void ARopeSwingAbortedWhileTheGuestSwingsSendsItOutOfTheWater()
	{
		var (rig, guest) = Swimmer( new RopeSwingActivity( Sites( Rope ) ) );
		rig.Until( () => guest.State == GuestState.PoolActivity && guest.PoolActivityPhase == 2, 2500 );
		rig.Simulation.AbortPoolActivitiesOf( 4301 );
		rig.Turns( 2 );
		Assert.IsFalse( guest.InPoolActivity );
		rig.Turns( 30 );
		Assert.IsFalse( guest.InSwimSession, "the session ends at once, as when a changing room goes" );
	}

	[TestMethod]
	public void ARopeSwingWhoseObjectIsGoneBringsTheGuestBackIntoTheWater()
	{
		var list = new List<PoolAttractionSite> { Rope };
		var sites = new PoolAttractionSites( () => list.ToArray() );
		var (rig, guest) = Swimmer( new RopeSwingActivity( sites ) );
		rig.Until( () => guest.State == GuestState.PoolActivity && guest.PoolActivityPhase == 1, 2500 );
		list.Clear();
		rig.Turns( 2 );
		Assert.AreEqual( GuestState.Swimming, guest.State );
		Assert.IsTrue( rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ), "the host put it back in free water" );
	}

	[TestMethod]
	public void ASaveInTheMiddleOfEachRopeSwingPhaseRestoresAndContinuesIdentically()
	{
		foreach ( var phase in new[] { 0, 1, 2, 3 } )
		{
			var (rig, guest) = Swimmer( new RopeSwingActivity( Sites( Rope ) ) );
			rig.Until( () => guest.State == GuestState.PoolActivity && guest.PoolActivityPhase == phase && (phase == 0 || guest.PoolActivityTurns >= 2), 2500 );
			var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
			Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid, $"phase {phase}: {string.Join( "; ", rig.Simulation.ValidateSnapshot( snapshot ).Errors )}" );
			var copy = new Rig();
			copy.Simulation.RegisterPoolActivity( new RopeSwingActivity( Sites( Rope ) ) );
			copy.Simulation.RestoreSnapshot( snapshot );
			Assert.AreEqual( HashOf( rig ), HashOf( copy ), $"phase {phase}" );
			var twin = copy.Simulation.Guests.Single();
			for ( var tick = 0; tick < 6000 && (guest.InPoolActivity || twin.InPoolActivity); tick++ )
			{
				rig.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
				copy.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
				Assert.AreEqual( (guest.State, guest.PoolActivityPhase, guest.PoolActivityTurns, guest.Navigation.X, guest.Navigation.Y, guest.SwimRandom),
					(twin.State, twin.PoolActivityPhase, twin.PoolActivityTurns, twin.Navigation.X, twin.Navigation.Y, twin.SwimRandom), $"phase {phase}, tick {tick}" );
			}
			Assert.AreEqual( GuestState.Swimming, twin.State, $"phase {phase}" );
			Assert.AreEqual( guest.Happiness, twin.Happiness, 0.0001f, $"phase {phase}" );
		}
	}

	// ---- The jets ----------------------------------------------------------------------------------------------------

	[TestMethod]
	public void SwimmersStandUnderTheJetsInUpToThreeAndLeaveHappier()
	{
		var rig = new Rig( capacity: 9 );
		rig.Simulation.RegisterPoolActivity( new JetsActivity( Sites( Jets ) ) );
		// nine swimmers: the room lets one off per park turn at most (tp.exe 0x004E1410), so enough of them must be in the water together to meet under the jets
		var guests = Enumerable.Range( 0, 9 ).Select( _ => rig.Send() ).ToList();
		var most = 0;
		var joined = new HashSet<int>();
		var slots = new HashSet<int>();
		var gained = 0f;
		var happiness = new Dictionary<int, float>();
		rig.Until( () =>
		{
			most = Math.Max( most, rig.Simulation.Guests.Count( guest => guest.InPoolActivity && guest.PoolActivityPhase > 0 ) );
			foreach ( var guest in rig.Simulation.Guests.Where( guest => guest.InPoolActivity ) )
			{
				var activity = rig.Simulation.PoolActivityOf( guest )!;
				slots.Add( activity.SpriteSlot( guest ) );
				if ( guest.PoolActivityPhase > 0 )
					Assert.IsTrue( Math.Abs( guest.Navigation.Cell.X - 7 ) <= 1 && guest.Navigation.Cell.Y == 3, $"under the jet: {guest.Navigation.Cell}" );
				happiness.TryAdd( guest.Id, guest.Happiness );
			}
			return Done( guests, joined );
		}, 8000 );
		Assert.IsTrue( most is >= 2 and <= PoolAttractionSettings.JetCapacity, $"{most} under the jets at once" );
		Assert.IsTrue( slots.IsSubsetOf( new[] { PoolSwimSlots.Crawl, PoolSwimSlots.TreadWater, PoolSwimSlots.SittingInWater } ) && slots.Contains( PoolSwimSlots.TreadWater ), string.Join( ",", slots ) );
		gained = happiness.Count;
		Assert.IsTrue( gained >= 3, $"{gained} of the swimmers used the jets" );
	}

	[TestMethod]
	public void AJetStayLastsItsDrawnTurnsPaysPerPeriodAndEnds()
	{
		var (rig, guest) = Swimmer( new JetsActivity( Sites( Jets ) ) );
		rig.Until( () => guest.State == GuestState.PoolActivity && guest.PoolActivityPhase > 0, 3000 );
		var planned = guest.PoolActivityTurns;
		Assert.IsTrue( planned is >= PoolAttractionSettings.JetMinimumTurns and < PoolAttractionSettings.JetMinimumTurns + PoolAttractionSettings.JetExtraTurns );
		var before = guest.Happiness;
		var turns = rig.Until( () => guest.State != GuestState.PoolActivity, 400 );
		Assert.AreEqual( planned, turns, 1, "the stay lasts the drawn turns" );
		Assert.IsTrue( guest.Happiness - before >= PoolAttractionSettings.JetHappiness - 0.01f || guest.Happiness >= 99.99f );
		Assert.AreEqual( GuestState.Swimming, guest.State );
	}

	// ---- The spa -----------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheSeatsOfTheSpaAreDistinctPointsOnItsTile()
	{
		var middle = Spa.CentreFixed;
		Assert.AreEqual( (4 * 65536 + 32768, 6 * 65536 + 32768), middle );
		// the one seat of the tub is its middle (the sitting sprite is as wide as the basin; PoolsSpaGeometryTests)
		Assert.AreEqual( 1, PoolAttractionSettings.SpaSeats );
		var seats = Enumerable.Range( 0, PoolAttractionSettings.SpaSeats ).Select( index => SpaActivity.Seat( middle, index ) ).ToArray();
		Assert.AreEqual( PoolAttractionSettings.SpaSeats, seats.Distinct().Count() );
		Assert.IsTrue( seats.All( seat => seat.X >> 16 == 4 && seat.Y >> 16 == 6 ), "all on the one tile" );
		Assert.AreEqual( (middle.X + PoolAttractionSettings.SpaSeatRadius, middle.Y), seats[0], "seat 0 to the east" );
	}

	[TestMethod]
	public void OneGuestSitsInTheSpaAtATimeAndTheOthersSwimOn()
	{
		var rig = new Rig( capacity: 10 );
		rig.Simulation.RegisterPoolActivity( new SpaActivity( Sites( Spa ) ) );
		var guests = Enumerable.Range( 0, 10 ).Select( _ => rig.Send() ).ToList();
		var most = 0;
		var joined = new HashSet<int>();
		var seatsSeen = new HashSet<(int X, int Y)>();
		rig.Until( () =>
		{
			var sitting = rig.Simulation.Guests.Where( guest => guest.InPoolActivity && guest.PoolActivityPhase == 1 ).ToList();
			most = Math.Max( most, rig.Simulation.Guests.Count( guest => guest.InPoolActivity ) );
			Assert.AreEqual( sitting.Count, sitting.Select( guest => (guest.PoolActivityX, guest.PoolActivityY) ).Distinct().Count(), "nobody shares a seat" );
			foreach ( var guest in sitting )
			{
				seatsSeen.Add( (guest.PoolActivityX, guest.PoolActivityY) );
				Assert.AreEqual( (4, 6), guest.Navigation.Cell, "sitting on the spa's tile, which is dry land" );
				Assert.AreEqual( PoolSwimSlots.SittingInWater, rig.Simulation.PoolActivityOf( guest )!.SpriteSlot( guest ) );
			}
			return Done( guests, joined );
		}, 12000 );
		Assert.IsTrue( most <= PoolAttractionSettings.SpaSeats, $"{most} in the spa at most" );
		Assert.AreEqual( PoolAttractionSettings.SpaSeats, seatsSeen.Count, "the one seat was taken, by one guest after the other" );
	}

	[TestMethod]
	public void FourGuestsSitInTheLargeSpaAtOnceEachInACellOfItsOwnAndTheOthersSwimOn()
	{
		var middle = BigSpa.CentreFixed;
		Assert.AreEqual( (4 * 65536, 7 * 65536), middle, "the middle of the 2 x 2 footprint is its centre corner" );
		Assert.AreEqual( PoolAttractionSettings.BigSpaSeats, SpaActivity.SeatsOf( BigSpa ) );
		Assert.AreEqual( PoolAttractionSettings.SpaSeats, SpaActivity.SeatsOf( Spa ) );
		var seats = Enumerable.Range( 0, PoolAttractionSettings.BigSpaSeats ).Select( index => SpaActivity.Seat( middle, index, PoolAttractionSettings.BigSpaSeats ) ).ToArray();
		CollectionAssert.AreEquivalent( BigSpa.Cells.ToArray(), seats.Select( seat => (seat.X >> 16, seat.Y >> 16) ).ToArray(), "one seat in each footprint cell" );
		var rig = new Rig( capacity: 10 );
		rig.Simulation.RegisterPoolActivity( new SpaActivity( Sites( BigSpa ) ) );
		var guests = Enumerable.Range( 0, 8 ).Select( _ => rig.Send() ).ToList();
		var joined = new HashSet<int>();
		var seatsSeen = new HashSet<(int X, int Y)>();
		var mostSitting = 0;
		rig.Until( () =>
		{
			var sitting = rig.Simulation.Guests.Where( guest => guest.InPoolActivity && guest.PoolActivityPhase == 1 ).ToList();
			mostSitting = Math.Max( mostSitting, sitting.Count );
			Assert.AreEqual( sitting.Count, sitting.Select( guest => (guest.PoolActivityX, guest.PoolActivityY) ).Distinct().Count(), "nobody shares a seat" );
			Assert.IsTrue( rig.Simulation.Guests.Count( guest => guest.InPoolActivity ) <= PoolAttractionSettings.BigSpaSeats, "never more than the seats" );
			foreach ( var guest in sitting )
				seatsSeen.Add( (guest.PoolActivityX, guest.PoolActivityY) );
			return Done( guests, joined );
		}, 20000 );
		Assert.AreEqual( PoolAttractionSettings.BigSpaSeats, mostSitting, "four sat at the same time" );
		Assert.AreEqual( PoolAttractionSettings.BigSpaSeats, seatsSeen.Count, "all four seats were used" );
		CollectionAssert.IsSubsetOf( seatsSeen.ToArray(), seats );
	}

	[TestMethod]
	public void ASpaGuestIsPaidPerPeriodAndStepsBackIntoThePool()
	{
		var (rig, guest) = Swimmer( new SpaActivity( Sites( Spa ) ) );
		rig.Until( () => guest.State == GuestState.PoolActivity && guest.PoolActivityPhase == 1, 3000 );
		var sit = guest.PoolActivityTurns;
		Assert.IsTrue( sit is >= PoolAttractionSettings.SpaMinimumTurns and < PoolAttractionSettings.SpaMinimumTurns + PoolAttractionSettings.SpaExtraTurns );
		var before = guest.Happiness;
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		Assert.IsTrue( guest.Happiness - before >= PoolAttractionSettings.SpaHappiness - 0.01f || guest.Happiness >= 99.99f, $"{before} to {guest.Happiness}" );
		Assert.IsTrue( rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ), "back in the pool" );
		Assert.AreEqual( 0, (guest.PoolActivityX, guest.PoolActivityY).Item1, "the seat is given up" );
	}

	[TestMethod]
	public void ASaveInTheSpaRestoresTheSeatAndTheGuestSitsOnIdentically()
	{
		var (rig, guest) = Swimmer( new SpaActivity( Sites( Spa ) ) );
		rig.Until( () => guest.State == GuestState.PoolActivity && guest.PoolActivityPhase == 1 && guest.PoolActivityTurns < 60, 3000 );
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( snapshot ).Errors ) );
		var copy = new Rig();
		copy.Simulation.RegisterPoolActivity( new SpaActivity( Sites( Spa ) ) );
		copy.Simulation.RestoreSnapshot( snapshot );
		var twin = copy.Simulation.Guests.Single();
		Assert.AreEqual( (guest.PoolActivityX, guest.PoolActivityY), (twin.PoolActivityX, twin.PoolActivityY), "the seat" );
		Assert.AreEqual( HashOf( rig ), HashOf( copy ) );
		for ( var tick = 0; tick < 5000 && (guest.InPoolActivity || twin.InPoolActivity); tick++ )
		{
			rig.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			copy.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			Assert.AreEqual( (guest.State, guest.PoolActivityTurns, guest.Happiness), (twin.State, twin.PoolActivityTurns, twin.Happiness), $"tick {tick}" );
		}
		Assert.AreEqual( HashOf( rig ), HashOf( copy ) );
	}

	// ---- The river ---------------------------------------------------------------------------------------------------

	/// <summary>Sends guests until one floats (the offer cadence is one in 90 turns, so a single swimmer may finish its swim first) and returns it.</summary>
	private static Guest SendAndWaitForAFloater( Rig rig, int turns = 3000, int swimmers = 12 )
	{
		// (a test that follows one floater's lap uses fewer swimmers: with the guests updated in the park turn only, twelve swimmers of one changing room are offered the ring in the same turns, and the others crowd the one followed)
		for ( var i = 0; i < swimmers; i++ )
			rig.Send();
		rig.Until( () => rig.Simulation.Guests.Any( guest => guest.State == GuestState.PoolActivity ), turns );
		return rig.Simulation.Guests.First( guest => guest.State == GuestState.PoolActivity );
	}

	private static Rig RiverRig()
	{
		var rig = new Rig( capacity: 12 );
		// the pool's perimeter is a lazy river; its middle is ordinary water
		for ( var y = 3; y <= 8; y++ )
			for ( var x = 5; x <= 10; x++ )
				if ( x == 5 || x == 10 || y == 3 || y == 8 )
					rig.Cells.SetFlags( x, y, ParkCellFlags.PoolRiver );
		rig.Simulation.RegisterPoolActivity( new RiverActivity() );
		return rig;
	}

	/// <summary>[EXT:pools-waves] The river rig with a wave machine at the pool's north edge (its access cell is a river cell) and the water effects registered.</summary>
	private static Rig RiverRigWithWaves( PoolWaveSettings settings )
	{
		var rig = RiverRig();
		PoolWaveSettings.SetAt( rig.Cells, (6, 3), settings );
		rig.Simulation.RegisterPoolActivity( new PoolWaterEffects( Sites( new PoolAttractionSite( 7001, PoolAttractionKind.WaveMachine, new[] { (6, 2) }, (6, 3) ) ) ) );
		return rig;
	}

	[TestMethod]
	public void TheWavesCarryTheFloatersInTheRiverAlongTheirLaneAndTheFloatsStillEnd()
	{
		var settings = new PoolWaveSettings( true, 3, 0 );
		var calm = RiverRig();
		var wavy = RiverRigWithWaves( settings );
		for ( var i = 0; i < 12; i++ )
		{
			calm.Send();
			wavy.Send();
		}
		var rivers = PoolRiverMap.Of( wavy.Cells );
		var (floatingTurns, ended) = (0, 0);
		var (calmOffset, wavyOffset) = (0, 0);
		var floatingBefore = new HashSet<int>();
		// how far a floater is from the middle line of its lane (across the cell's flow)
		int Offset( Guest guest )
		{
			var cell = (X: guest.Navigation.X >> 16, Y: guest.Navigation.Y >> 16);
			var centre = PoolSwimRegion.Centre( cell );
			return rivers.FlowAt( cell.X, cell.Y ) is { } flow ? Math.Abs( flow.Dx != 0 ? guest.Navigation.Y - centre.Y : guest.Navigation.X - centre.X ) : 0;
		}
		for ( var turn = 0; turn < 3000; turn++ )
		{
			calm.Turns( 1 );
			wavy.Turns( 1 );
			foreach ( var floater in calm.Simulation.Guests.Where( guest => guest.State == GuestState.PoolActivity && guest.PoolActivityId == RiverActivity.ActivityId ) )
				calmOffset = Math.Max( calmOffset, Offset( floater ) );
			foreach ( var floater in wavy.Simulation.Guests.Where( guest => guest.State == GuestState.PoolActivity && guest.PoolActivityId == RiverActivity.ActivityId ) )
			{
				floatingTurns++;
				Assert.IsTrue( rivers.IsRiver( floater.Navigation.X >> 16, floater.Navigation.Y >> 16 ), $"turn {turn}: guest {floater.Id} stays in the river" );
				Assert.IsTrue( wavy.Region.IsWater( floater.Navigation.X, floater.Navigation.Y ), $"turn {turn}: guest {floater.Id} stays in free water" );
				wavyOffset = Math.Max( wavyOffset, Offset( floater ) );
			}
			var floatingNow = wavy.Simulation.Guests.Where( guest => guest.State == GuestState.PoolActivity && guest.PoolActivityId == RiverActivity.ActivityId ).Select( guest => guest.Id ).ToHashSet();
			ended += floatingBefore.Count( id => !floatingNow.Contains( id ) );
			floatingBefore = floatingNow;
		}
		Assert.IsTrue( floatingTurns > 200, $"{floatingTurns} floating turns" );
		Assert.IsTrue( ended > 0, "floats still end" );
		// the push runs along the lane only: the floaters stay as close to the middle line as in a calm river (at a bend, before the middle of the cell, the cell's flow axis is across the way in,
		// so a push there can add a sliver, never the half cell a push across the lane would)
		Assert.IsTrue( wavyOffset <= calmOffset + Fixed16.One / 16, $"off the middle line: {wavyOffset} with waves, {calmOffset} without" );
	}

	[TestMethod]
	public void AFloaterTakesTheWavesPushAlongItsLaneOnlyAndOnlyWithinItsRing()
	{
		var cells = RingMap( 4, 4, 11, 8 );
		var rivers = PoolRiverMap.Of( cells );
		const int a = 3000;
		foreach ( var (cellX, cellY) in new[] { (6, 4), (11, 6), (6, 8), (4, 6) } )
		{
			var flow = rivers.FlowAt( cellX, cellY )!.Value;
			var (x, y) = PoolSwimRegion.Centre( (cellX, cellY) );
			var push = RiverActivity.WavePush( rivers, x, y, (a, a) );
			// the waves run along +x +y: a lane along x keeps the x part, a lane along y the y part, whichever way it flows
			Assert.AreEqual( flow.Dx != 0 ? (a, 0) : (0, a), push, $"cell ({cellX}, {cellY}) flowing ({flow.Dx}, {flow.Dy})" );
		}
		Assert.AreEqual( (0, 0), RiverActivity.WavePush( rivers, PoolSwimRegion.Centre( (6, 4) ).X, PoolSwimRegion.Centre( (6, 4) ).Y, (0, 0) ), "no waves" );
		Assert.AreEqual( (0, 0), RiverActivity.WavePush( rivers, PoolSwimRegion.Centre( (7, 6) ).X, PoolSwimRegion.Centre( (7, 6) ).Y, (a, a) ), "not in a river cell" );
		// a push that would land two cells on, outside the cell's neighbours in its ring, is not taken
		var (edgeX, edgeY) = PoolSwimRegion.Centre( (6, 4) );
		Assert.AreEqual( (0, 0), RiverActivity.WavePush( rivers, edgeX, edgeY, (2 * Fixed16.One, 2 * Fixed16.One) ) );
	}

	[TestMethod]
	public void ARiverWithWavesThatAreOffFloatsExactlyAsAPoolWithoutWaves()
	{
		var calm = RiverRig();
		var off = RiverRigWithWaves( new PoolWaveSettings( false, 3, 0 ) );
		for ( var i = 0; i < 12; i++ )
		{
			calm.Send();
			off.Send();
		}
		for ( var turn = 0; turn < 1500; turn++ )
		{
			calm.Turns( 1 );
			off.Turns( 1 );
			foreach ( var guest in calm.Simulation.Guests )
			{
				var twin = off.Simulation.Guests.Single( other => other.Id == guest.Id );
				Assert.AreEqual( (guest.State, guest.Navigation.X, guest.Navigation.Y), (twin.State, twin.Navigation.X, twin.Navigation.Y), $"turn {turn}: guest {guest.Id}" );
			}
		}
	}

	[TestMethod]
	public void ASaveWhileTheWavesCarryAFloaterRestoresAndContinuesIdentically()
	{
		var settings = new PoolWaveSettings( true, 3, 0 );
		var rig = RiverRigWithWaves( settings );
		var guest = SendAndWaitForAFloater( rig );
		rig.Until( () => guest.State != GuestState.PoolActivity
			|| PoolWaves.Push( settings, rig.Simulation.ParkTurn, guest.Navigation.X, guest.Navigation.Y ).X > 0, 400 );
		Assert.AreEqual( GuestState.PoolActivity, guest.State );
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( snapshot ).Errors ) );
		var copy = RiverRigWithWaves( settings );
		copy.Simulation.RestoreSnapshot( snapshot );
		var twin = copy.Simulation.Guests.Single( other => other.Id == guest.Id );
		Assert.AreEqual( HashOf( rig ), HashOf( copy ) );
		for ( var tick = 0; tick < 4000 && (guest.InPoolActivity || twin.InPoolActivity); tick++ )
		{
			rig.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			copy.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			Assert.AreEqual( (guest.State, guest.PoolActivityTurns, guest.Navigation.X, guest.Navigation.Y), (twin.State, twin.PoolActivityTurns, twin.Navigation.X, twin.Navigation.Y), $"tick {tick}" );
		}
		Assert.AreEqual( HashOf( rig ), HashOf( copy ) );
	}

	[TestMethod]
	public void ASwimmerThatReachesTheRiverFloatsRoundTheRingAtTheCurrentsSpeedThenSwimsOn()
	{
		var rig = RiverRig();
		var rivers = PoolRiverMap.Of( rig.Cells );
		Assert.AreEqual( 1, rivers.Rings.Count );
		var guest = SendAndWaitForAFloater( rig, swimmers: 4 );
		Assert.AreEqual( RiverActivity.ActivityId, guest.PoolActivityId );
		Assert.AreEqual( 0, guest.PoolActivityTarget );
		var planned = guest.PoolActivityTurns;
		Assert.IsTrue( planned is >= PoolRiverSettings.MinimumTurns and < PoolRiverSettings.MinimumTurns + PoolRiverSettings.ExtraTurns );
		var cells = new List<(int X, int Y)>();
		var happiness = guest.Happiness;
		var travelled = 0L;
		var lastX = guest.Navigation.X;
		var lastY = guest.Navigation.Y;
		var turns = rig.Until( () =>
		{
			if ( guest.State != GuestState.PoolActivity )
				return true;
			Assert.IsTrue( rivers.IsRiver( guest.CellX, guest.CellY ), "floating in a river cell" );
			Assert.IsTrue( rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ) );
			Assert.AreEqual( PoolSwimSlots.FloatingInRing, rig.Simulation.PoolActivityOf( guest )!.SpriteSlot( guest ) );
			cells.Add( guest.Cell );
			travelled += Math.Abs( guest.Navigation.X - lastX ) + Math.Abs( guest.Navigation.Y - lastY );
			(lastX, lastY) = (guest.Navigation.X, guest.Navigation.Y);
			return false;
		}, 400 );
		Assert.AreEqual( planned, turns, 2 );
		Assert.AreEqual( GuestState.Swimming, guest.State );
		Assert.IsTrue( cells.Distinct().Count() >= 6, $"it passed {cells.Distinct().Count()} cells" );
		Assert.AreEqual( PoolRiverSettings.FlowSpeed * (double)planned, travelled, PoolRiverSettings.FlowSpeed * planned * 0.2, "it moved at the current's speed, not the swimmer's" );
		Assert.AreEqual( PoolRiverSettings.HappinessGain, guest.Happiness - happiness, 0.01f );
	}

	[TestMethod]
	public void NoRiverNoOfferAndNoDrawFromTheSwimmersStream()
	{
		var plain = new Rig();
		foreach ( var activity in PoolActivityRegistry.Create( Sites() ) )
			plain.Simulation.RegisterPoolActivity( activity );
		var bare = new Rig();
		var a = plain.Send();
		var b = bare.Send();
		for ( var turn = 0; turn < 600; turn++ )
		{
			plain.Turns( 1 );
			bare.Turns( 1 );
			Assert.AreEqual( HashOf( bare ), HashOf( plain ), $"turn {turn}: a river activity in a pool without a river changes no hash" );
			Assert.AreEqual( b.SwimRandom, a.SwimRandom );
		}
	}

	[TestMethod]
	public void ASaveWhileFloatingRestoresAndTheFloaterGoesOnRoundTheSameWay()
	{
		var rig = RiverRig();
		var guest = SendAndWaitForAFloater( rig );
		rig.Until( () => guest.State != GuestState.PoolActivity || guest.PoolActivityTurns < 80, 400 );
		Assert.AreEqual( GuestState.PoolActivity, guest.State );
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( snapshot ).Errors ) );
		var copy = RiverRig();
		copy.Simulation.RestoreSnapshot( snapshot );
		var twin = copy.Simulation.Guests.Single( other => other.Id == guest.Id );
		Assert.AreEqual( HashOf( rig ), HashOf( copy ) );
		for ( var tick = 0; tick < 4000 && (guest.InPoolActivity || twin.InPoolActivity); tick++ )
		{
			rig.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			copy.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			Assert.AreEqual( (guest.State, guest.PoolActivityTurns, guest.Navigation.X, guest.Navigation.Y), (twin.State, twin.PoolActivityTurns, twin.Navigation.X, twin.Navigation.Y), $"tick {tick}" );
		}
		Assert.AreEqual( HashOf( rig ), HashOf( copy ) );
	}

	[TestMethod]
	public void ARiverBrokenUnderAFloaterLetsItGoBackToSwimming()
	{
		var rig = RiverRig();
		var guest = SendAndWaitForAFloater( rig );
		var before = guest.Happiness;
		foreach ( var (x, y) in new[] { (7, 3), (8, 3), (9, 3), (6, 3), (5, 3), (5, 4), (5, 5) } )
			rig.Cells.SetFlags( x, y, ParkCellFlags.None );
		rig.Turns( 3 );
		Assert.AreEqual( GuestState.Swimming, guest.State, "the ring is gone: the current lets go" );
		Assert.AreEqual( before, guest.Happiness, 0.001f, "and a float that did not run its turns is not paid" );
		Assert.IsTrue( rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ) );
	}

	// ---- The registry -------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheRegistryHoldsTheRiverAndTheThreeAttractionsAndStaysInIdOrder()
	{
		var none = PoolActivityRegistry.Create().Select( activity => activity.Id ).OrderBy( id => id, StringComparer.Ordinal ).ToArray();
		CollectionAssert.AreEqual( new[] { PoolWaterEffects.ActivityId, RiverActivity.ActivityId, "extra.board", "extra.ladder", "extra.lounger", "extra.shower", "slide.ride" }, none, "without the sites the river, the ambient effects and the extras" );
		var all = PoolActivityRegistry.Create( Sites( Rope, Jets, Spa ) ).Select( activity => activity.Id ).OrderBy( id => id, StringComparer.Ordinal ).ToArray();
		CollectionAssert.AreEqual( new[] { "ambient.water", "attraction.jets", "attraction.river", "attraction.ropeswing", "attraction.spa", "extra.board", "extra.ladder", "extra.lounger", "extra.shower", "slide.ride", "waves.ring" }, all );
	}

	// ---- The wave pool's ring floaters ------------------------------------------------------------------------------

	/// <summary>[EXT:pools-waves] A rig whose changing room takes twelve, with a wave machine (when <paramref name="waves"/> is given), the water effects and the ring floaters registered, and twelve swimmers sent.</summary>
	private static (Rig Rig, Guest First) RingRig( PoolWaveSettings? waves, int swimmers = 12 )
	{
		// (the bobbing test sends fewer swimmers: in the park turn's sweep all of them reach the ring in the same turns, and the crowd at the rings spots hides the one floater the test measures)
		var rig = new Rig( capacity: 12 );
		var all = new List<PoolAttractionSite>();
		if ( waves is { } settings )
		{
			all.Add( new PoolAttractionSite( 7000, PoolAttractionKind.WaveMachine, new[] { (6, 2) }, (6, 3) ) );
			PoolWaveSettings.SetAt( rig.Cells, (6, 3), settings );
		}
		EffectSites = Sites( all.ToArray() );
		rig.Simulation.RegisterPoolActivity( new PoolWaterEffects( EffectSites ) );
		rig.Simulation.RegisterPoolActivity( new WaveRingActivity( EffectSites ) );
		var first = rig.Send( happiness: 10 );
		for ( var i = 1; i < swimmers; i++ )
			rig.Send( happiness: 10 );
		return (rig, first);
	}

	private static Guest? RingFloater( Rig rig ) => rig.Simulation.Guests.FirstOrDefault( guest => guest.State == GuestState.PoolActivity && guest.PoolActivityId == WaveRingActivity.ActivityId );

	[TestMethod]
	public void ARingIsOfferedOnlyWhileThePoolsWavesRunAwayFromTheStepsAndUpToThePoolsRings()
	{
		var settings = new PoolWaveSettings( true, 3, 2 );
		var (rig, _) = RingRig( settings );
		var ring = rig.Simulation.PoolActivities.OfType<WaveRingActivity>().Single();
		var (offeredTurns, restChecks) = (0, 0);
		for ( var turn = 0; turn < PoolWaves.CycleTurns( settings ) * 2; turn++ )
		{
			rig.Turns( 1 );
			var parkTurn = rig.Simulation.ParkTurn;
			var running = PoolWaves.IsRunning( settings, parkTurn );
			var left = running ? PoolWaves.RunTurns - (int)(parkTurn % PoolWaves.CycleTurns( settings )) : 0;
			var floaters = rig.Simulation.Guests.Count( guest => guest.State == GuestState.PoolActivity && guest.PoolActivityId == WaveRingActivity.ActivityId );
			Assert.IsTrue( floaters <= WaveRingActivity.RingsOf( rig.Region ), $"turn {parkTurn}: {floaters} floaters, the pool has {WaveRingActivity.RingsOf( rig.Region )} rings" );
			foreach ( var swimmer in rig.Simulation.Guests.Where( guest => guest.State == GuestState.Swimming ) )
			{
				var offer = ring.Offer( rig.Simulation, swimmer, rig.Region );
				if ( !running )
					restChecks++;
				if ( offer == null )
					continue;
				offeredTurns++;
				Assert.IsTrue( running && left >= PoolWaveRingSettings.MinimumRunLeft, $"turn {parkTurn}: offered only while at least {PoolWaveRingSettings.MinimumRunLeft} turns of the run are left ({left})" );
				Assert.AreEqual( 7000, offer.Value.TargetId, "the ring is the wave machine's" );
				Assert.IsTrue( Math.Max( Math.Abs( swimmer.CellX - rig.Region.Access.X ), Math.Abs( swimmer.CellY - rig.Region.Access.Y ) ) > 1, "not beside the steps" );
			}
		}
		Assert.IsTrue( offeredTurns > 0 && restChecks > 0, $"offers checked while running ({offeredTurns}) and in the rest ({restChecks})" );
		var (flat, swimmer2) = RingRig( null );
		flat.Until( () => swimmer2.IsSwimming, 400 );
		Assert.IsNull( flat.Simulation.PoolActivities.OfType<WaveRingActivity>().Single().Offer( flat.Simulation, swimmer2, flat.Region ), "a pool without waves" );
		var (off, other) = RingRig( new PoolWaveSettings( false, 3, 0 ) );
		off.Until( () => other.IsSwimming, 400 );
		Assert.IsNull( off.Simulation.PoolActivities.OfType<WaveRingActivity>().Single().Offer( off.Simulation, other, off.Region ), "waves that are off" );
		// a lazy river lane keeps its own floaters: the pool's perimeter becomes a river ring, and a swimmer in or beside it is offered no ring
		var (river, _) = RingRig( settings );
		for ( var y = 3; y <= 8; y++ )
			for ( var x = 5; x <= 10; x++ )
				if ( x == 5 || x == 10 || y == 3 || y == 8 )
					river.Cells.SetFlags( x, y, ParkCellFlags.PoolRiver );
		var rivers = PoolRiverMap.Of( river.Cells );
		var nearLane = 0;
		for ( var turn = 0; turn < 600; turn++ )
		{
			river.Turns( 1 );
			foreach ( var swimmer3 in river.Simulation.Guests.Where( guest => guest.State == GuestState.Swimming ) )
			{
				var beside = Enumerable.Range( -1, 3 ).Any( dy => Enumerable.Range( -1, 3 ).Any( dx => rivers.IsRiver( swimmer3.CellX + dx, swimmer3.CellY + dy ) ) );
				if ( !beside )
					continue;
				nearLane++;
				Assert.IsNull( river.Simulation.PoolActivities.OfType<WaveRingActivity>().Single().Offer( river.Simulation, swimmer3, river.Region ), "not in or beside a river lane" );
			}
		}
		Assert.IsTrue( nearLane > 0, "some swimmer was in or beside the lane" );
	}

	[TestMethod]
	public void ARingFloaterBobsWithTheWavesNearItsSpotWithinTheRunAndIsPaid()
	{
		var settings = new PoolWaveSettings( true, 3, 0 );
		var (rig, _) = RingRig( settings, swimmers: 6 );
		rig.Until( () => RingFloater( rig ) != null, 4000 );
		var floater = RingFloater( rig );
		Assert.IsNotNull( floater, "a swimmer took a ring" );
		Assert.AreEqual( PoolSwimSlots.FloatingInRing, rig.Simulation.PoolActivityOf( floater! )!.SpriteSlot( floater! ) );
		var spot = (floater!.PoolActivityX, floater.PoolActivityY);
		var happiness = floater.Happiness;
		var (moved, farthest, turns) = (0, 0, 0);
		var last = (floater.Navigation.X, floater.Navigation.Y);
		while ( turns < 400 )
		{
			rig.Turns( 1 );
			turns++;
			if ( floater.State != GuestState.PoolActivity )
				break;
			Assert.IsTrue( PoolWaves.IsRunning( settings, rig.Simulation.ParkTurn - 1 ), $"turn {turns}: the float stays within the waves' run" );
			Assert.IsTrue( rig.Region.IsWater( floater.Navigation.X, floater.Navigation.Y ), $"turn {turns}: in free water" );
			if ( (floater.Navigation.X, floater.Navigation.Y) != last )
				moved++;
			last = (floater.Navigation.X, floater.Navigation.Y);
			farthest = Math.Max( farthest, Fixed16.Length( floater.Navigation.X - spot.Item1, floater.Navigation.Y - spot.Item2 ) );
		}
		Assert.IsTrue( turns >= 1 && turns <= PoolWaveRingSettings.MinimumTurns + PoolWaveRingSettings.ExtraTurns + 1, $"floated {turns} turns" );
		Assert.IsTrue( moved > turns / 3, $"the waves rock it ({moved} of {turns} turns it moved)" );
		Assert.IsTrue( farthest < 2 * Fixed16.One, $"it stays near its spot (farthest {farthest / (double)Fixed16.One:F2} cells)" );
		Assert.IsTrue( floater.Happiness >= happiness + PoolWaveRingSettings.HappinessGain - 0.01f, $"paid ({happiness} to {floater.Happiness})" );
	}

	[TestMethod]
	public void WavesSwitchedOffEndARingFloatAtOnceWithoutTheBonus()
	{
		var settings = new PoolWaveSettings( true, 3, 0 );
		var (rig, _) = RingRig( settings );
		rig.Until( () => RingFloater( rig ) is { PoolActivityTurns: > 20 }, 4000 );
		var floater = RingFloater( rig )!;
		Assert.IsNotNull( floater );
		var happiness = floater.Happiness;
		// the machine is switched off: the pool has no waves
		PoolWaveSettings.SetAt( rig.Cells, (6, 3), new PoolWaveSettings( false, 3, 0 ) );
		rig.Turns( 2 );
		Assert.AreNotEqual( WaveRingActivity.ActivityId, floater.PoolActivityId, "back in the water" );
		Assert.IsTrue( floater.Happiness < happiness + PoolWaveRingSettings.HappinessGain - 0.01f, "without the bonus" );
	}

	[TestMethod]
	public void ASaveOnARingRestoresAndContinuesIdentically()
	{
		var settings = new PoolWaveSettings( true, 3, 0 );
		var (rig, _) = RingRig( settings );
		rig.Until( () => RingFloater( rig ) is { PoolActivityTurns: > 30 }, 5000 );
		var floater = RingFloater( rig )!;
		Assert.IsNotNull( floater );
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( snapshot ).Errors ) );
		var (copy, _) = RingRig( settings );
		copy.Simulation.RestoreSnapshot( snapshot );
		var twin = copy.Simulation.Guests.Single( other => other.Id == floater.Id );
		Assert.AreEqual( HashOf( rig ), HashOf( copy ) );
		for ( var tick = 0; tick < 3000 && (floater.InPoolActivity || twin.InPoolActivity); tick++ )
		{
			rig.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			copy.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			Assert.AreEqual( (floater.State, floater.PoolActivityTurns, floater.Navigation.X, floater.Navigation.Y), (twin.State, twin.PoolActivityTurns, twin.Navigation.X, twin.Navigation.Y), $"tick {tick}" );
		}
		Assert.AreEqual( HashOf( rig ), HashOf( copy ) );
	}

	[TestMethod]
	public void TheSwayIsThePushOnTheCrestAndAsMuchBackInTheTrough()
	{
		var settings = new PoolWaveSettings( true, 3, 0 );
		var (x, y) = ((int)(3.5 * Fixed16.One), (int)(4.25 * Fixed16.One));
		var (forward, back) = (0, 0);
		for ( var turn = 0L; turn < PoolWaves.CycleTurns( settings ); turn++ )
		{
			var sway = PoolWaves.Sway( settings, turn, x, y );
			var push = PoolWaves.Push( settings, turn, x, y );
			Assert.AreEqual( sway.X, sway.Y, "along the diagonal" );
			if ( sway.X > 0 )
			{
				forward++;
				Assert.AreEqual( push, sway, $"turn {turn}: on the crest it is the push" );
			}
			else
			{
				Assert.AreEqual( (0, 0), push, $"turn {turn}: no push off the crest" );
				if ( sway.X < 0 )
					back++;
			}
			if ( !PoolWaves.IsRunning( settings, turn ) )
				Assert.AreEqual( (0, 0), sway, "nothing in a rest" );
		}
		Assert.IsTrue( forward > 40 && back > 40 && Math.Abs( forward - back ) <= 6, $"to and fro: {forward} turns forward, {back} back" );
		Assert.AreEqual( (0, 0), PoolWaves.Sway( new PoolWaveSettings( false, 3, 0 ), 60, x, y ), "off" );
	}

	[TestMethod]
	public void TheSitesOfAFixedListAreFoundByRegionAndKind()
	{
		var sites = Sites( Rope, Jets, Spa, new PoolAttractionSite( 9, PoolAttractionKind.Spa, new[] { (30, 30) }, (31, 30) ) );
		var rig = new Rig();
		CollectionAssert.AreEqual( new[] { 4303 }, sites.In( rig.Region, PoolAttractionKind.Spa ).Select( site => site.ObjectId ).ToArray(), "the far spa is not on this pool" );
		Assert.AreEqual( 4301, sites.Find( 4301 )!.ObjectId );
		Assert.IsNull( sites.Find( 1 ) );
	}

	// ---- The pool tool's river option and the flags of a pool cell -------------------------------------------------------

	private static PoolBuilder Builder( ParkCellMap cells, out PoolsTests.FlatGrid grid )
	{
		grid = new PoolsTests.FlatGrid( cells.Width, cells.Height );
		return new PoolBuilder( cells, () => null, grid );
	}

	[TestMethod]
	public void TheRiverOptionPaintsAFlaggedRingMarksExistingWaterForFreeAndUndoesBoth()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var cells = new ParkCellMap( 32, 32 );
		var builder = Builder( cells, out _ );
		var tool = PoolToolRig.Create( builder );
		Assert.IsTrue( tool.Begin( erase: false, river: true ) );
		Assert.IsTrue( tool.IsRiver );
		// a stroke round the perimeter of 6 x 4: press at the corner and drag round it
		var corners = new[] { (4, 4), (9, 4), (9, 7), (4, 7), (4, 5) };
		tool.Press( corners[0].Item1, corners[0].Item2 );
		foreach ( var (x, y) in corners.Skip( 1 ) )
			tool.Drag( x, y );
		var stroke = tool.Release()!;
		Assert.AreEqual( 16, stroke.Cells.Count, "the 6 x 4 ring has 16 cells" );
		Assert.AreEqual( 1, PoolRiverMap.Of( cells ).Rings.Count, "a ring: the corner (4, 5) closed it" );
		Assert.IsTrue( stroke.Cells.All( cell => cells.FlagsAt( cell.X, cell.Y ) == ParkCellFlags.PoolRiver ) );
		// the river option over water that is none already marks it, at no price, and a second pass changes nothing
		Assert.AreEqual( PoolBuildResult.Ok, builder.Paint( 6, 5 ), "plain water inside the ring" );
		Assert.AreEqual( PoolBuildResult.RiverMarked, builder.Paint( 6, 5, river: true ) );
		Assert.AreEqual( 0, builder.LastCharged );
		Assert.AreEqual( PoolBuildResult.Existing, builder.Paint( 6, 5, river: true ), "already a river cell" );
		Assert.AreEqual( 0, PoolRiverMap.Of( cells ).Rings.Count, "the extra cell touches the lane twice: a lane that touches itself is no ring" );
		cells.SetFlags( 6, 5, ParkCellFlags.None );
		Assert.AreEqual( 1, PoolRiverMap.Of( cells ).Rings.Count );
		// painting without the option leaves flags alone
		Assert.AreEqual( PoolBuildResult.Existing, builder.Paint( 4, 4 ) );
		Assert.AreEqual( ParkCellFlags.PoolRiver, cells.FlagsAt( 4, 4 ) );
		// the strokes that only marked cells are undone by clearing the flag, not by erasing the water
		tool.Press( 6, 5 );
		tool.Release();
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( ParkCellType.PoolWater, cells.TypeAt( 6, 5 ), "the water stays" );
		Assert.AreEqual( ParkCellFlags.None, cells.FlagsAt( 6, 5 ) );
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( 1, cells.CountOf( ParkCellType.PoolWater ), "only the plain cell is left once the river stroke is undone" );
		Assert.AreEqual( 0, PoolRiverMap.Of( cells ).Rings.Count );
		tool.Cancel();
		Assert.IsFalse( tool.IsRiver );
	}

	[TestMethod]
	public void ErasingACellTakesItsRiverFlagAndItsWaveSettingsWithIt()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var cells = new ParkCellMap( 32, 32 );
		var builder = Builder( cells, out _ );
		Assert.IsTrue( builder.PaintCells( PoolBuilder.Rectangle( (3, 3), (6, 4) ), river: true ).Completed );
		Assert.AreEqual( ParkCellFlags.PoolRiver, cells.FlagsAt( 3, 3 ) );
		PoolWaveSettings.SetAt( cells, (3, 3), new PoolWaveSettings( true, 2, 0 ) );
		Assert.IsTrue( cells.FlagsAt( 3, 3 ).HasFlag( ParkCellFlags.PoolWavesOn ) && cells.FlagsAt( 3, 3 ).HasFlag( ParkCellFlags.PoolRiver ) );
		Assert.AreEqual( PoolBuildResult.Ok, builder.Erase( 3, 3 ) );
		Assert.AreEqual( ParkCellFlags.None, cells.FlagsAt( 3, 3 ), "an erased cell keeps no flag" );
		// painted again, the cell starts clean
		Assert.AreEqual( PoolBuildResult.Ok, builder.Paint( 3, 3 ) );
		Assert.AreEqual( ParkCellFlags.None, cells.FlagsAt( 3, 3 ) );
	}

	[TestMethod]
	public void ARiverAndWaveFlagsRoundTripThroughTheLayoutSaveAndTheValidatorRefusesForgedOnes()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var cells = RingMap( 4, 4, 9, 7 );
		PoolWaveSettings.SetAt( cells, (4, 4), new PoolWaveSettings( false, 3, 2 ) );
		var layout = ParkLayoutSnapshot.CaptureSnapshot( cells, new ParkObjects( null!, new SandboxParkGrid(), WorldSeed.Default ) );
		var json = ParkResumeJson.Serialize( layout );
		var loaded = ParkResumeJson.Deserialize<ParkLayoutSnapshot.Data>( json );
		var again = ParkLayoutSnapshot.RestoreCellMap( loaded );
		Assert.AreEqual( 1, PoolRiverMap.Of( again ).Rings.Count, "the ring is derived again from the saved flags" );
		Assert.AreEqual( new PoolWaveSettings( false, 3, 2 ), PoolWaveSettings.At( again, (4, 4) ) );
		CollectionAssert.AreEqual( json, ParkResumeJson.Serialize( ParkLayoutSnapshot.CaptureSnapshot( again, new ParkObjects( null!, new SandboxParkGrid(), WorldSeed.Default ) ) ), "byte-stable" );
		ParkLayoutSnapshot.Data Forge( Func<ParkLayoutSnapshot.CellData, ParkLayoutSnapshot.CellData> edit ) =>
			loaded with { Cells = loaded.Cells with { Cells = loaded.Cells.Cells.Select( ( cell, index ) => index == 0 ? edit( cell ) : cell ).ToList() } };
		foreach (var flags in new[] { (ushort)0x0001, (ushort)0x2000, (ushort)0x0000 | 0x2000 | 0x0100 | 0xC000, (ushort)0x4000 })
		{
			var forged = Forge( cell => cell with { Flags = (ushort)flags } );
			Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( forged ), $"flags {flags:X4} on a pool cell" );
		}
	}

	[TestMethod]
	public void AnEmptyWorldWithoutRiversOrWavesHashesAndSavesExactlyAsBefore()
	{
		// the new flags and the river/wave code add nothing to a park without them
		var cells = new ParkCellMap( 32, 32 );
		var layout = ParkLayoutSnapshot.CaptureSnapshot( cells, new ParkObjects( null!, new SandboxParkGrid(), WorldSeed.Default ) );
		StringAssert.DoesNotMatch( Encoding.UTF8.GetString( ParkResumeJson.Serialize( layout ) ), new System.Text.RegularExpressions.Regex( "PoolRiver|Wave" ) );
		Assert.IsFalse( PoolRiverMap.Of( cells ).Any );
	}

	[TestMethod]
	public void AClickOnTheWaveMachineStepsThroughTheFourPresetsAndRoundAgain()
	{
		var seen = new List<PoolWaveSettings>();
		PoolWaveSettings? current = null;
		for ( var click = 0; click < 9; click++ )
		{
			current = PoolWaveSettings.NextPreset( current );
			seen.Add( current.Value );
		}
		CollectionAssert.AreEqual( PoolWaveSettings.Presets.Concat( PoolWaveSettings.Presets ).Concat( PoolWaveSettings.Presets.Take( 1 ) ).ToArray(), seen.ToArray() );
		Assert.AreEqual( PoolWaveSettings.Default, seen[0], "the first click on a pool with no settings gives the default" );
		Assert.IsTrue( PoolWaveSettings.Presets.Any( preset => !preset.On ), "one of them is off" );
		Assert.AreEqual( PoolWaveSettings.Default, PoolWaveSettings.NextPreset( new PoolWaveSettings( true, 3, 1 ) ), "settings that are no preset start the round again" );
	}

	[TestMethod]
	public void TheSprayOfAJetIsAPureFunctionOfTheTimeThatFliesOverTheWaterAndLandsInIt()
	{
		var nozzle = new System.Numerics.Vector3( 10f, 20f, 1.5f );
		var direction = new System.Numerics.Vector2( 0f, 1f );
		var droplets = PoolSpray.At( nozzle, direction, 0.4f, 3.25 ).ToArray();
		Assert.AreEqual( PoolSpray.Droplets, droplets.Length );
		CollectionAssert.AreEqual( droplets, PoolSpray.At( nozzle, direction, 0.4f, 3.25 ).ToArray(), "the same time gives the same droplets" );
		CollectionAssert.AreEqual( droplets, PoolSpray.At( nozzle, direction, 0.4f, 3.25 + PoolSpray.FlightSeconds ).Select( droplet => droplet ).ToArray(), "and a flight later, the same again: it repeats" );
		var started = false;
		foreach ( var seconds in Enumerable.Range( 0, 40 ).Select( step => step * 0.05 ) )
			foreach ( var droplet in PoolSpray.At( nozzle, direction, 0.4f, seconds ) )
			{
				Assert.IsTrue( droplet.Position.Y >= 20f - 1e-3f && droplet.Position.Y <= 20f + PoolSpray.Reach + 1e-3f, $"over the water in front of the jet ({droplet.Position.Y})" );
				Assert.IsTrue( Math.Abs( droplet.Position.X - 10f ) <= PoolSpray.Spread + 1e-3f );
				Assert.IsTrue( droplet.Position.Z >= 0.4f - 1e-3f && droplet.Position.Z <= 1.5f + PoolSpray.Arc + 1e-3f, $"between the water and the top of the arc ({droplet.Position.Z})" );
				Assert.IsTrue( droplet.Alpha is >= 0f and <= 1f && droplet.Size is > 0.04f and < 0.11f );
				started |= droplet.Position.Y > 21f;
			}
		Assert.IsTrue( started, "some droplets reach a metre out" );
		Assert.IsTrue( PoolSpray.At( nozzle, new System.Numerics.Vector2( 1f, 0f ), 0.4f, 1.0 ).All( droplet => droplet.Position.X >= 10f - 1e-3f ), "thrown the other way, the same droplets go the other way" );
	}

	// ---- The ambient effects -------------------------------------------------------------------------------------------

	private static PoolAttractionSites EffectSites = null!;

	private static (Rig Rig, Guest Guest) EffectsRig( PoolWaveSettings? waves, params PoolAttractionSite[] sites )
	{
		var rig = new Rig();
		var all = sites.ToList();
		// the waves are a wave machine's: one stands at the pool's north edge, with the settings in its access cell
		if ( waves is { } settings )
		{
			all.Add( new PoolAttractionSite( 7000, PoolAttractionKind.WaveMachine, new[] { (6, 2) }, (6, 3) ) );
			PoolWaveSettings.SetAt( rig.Cells, (6, 3), settings );
		}
		EffectSites = Sites( all.ToArray() );
		rig.Simulation.RegisterPoolActivity( new PoolWaterEffects( EffectSites ) );
		return (rig, rig.Send( happiness: 10 ));
	}

	[TestMethod]
	public void TheWavesPayAndPushASwimmerAndWavesThatAreOffDoNothing()
	{
		var (waves, swimmerWaves) = EffectsRig( new PoolWaveSettings( true, 3, 0 ) );
		var (flat, swimmerFlat) = EffectsRig( null );
		var (off, swimmerOff) = EffectsRig( new PoolWaveSettings( false, 3, 0 ) );
		var apart = 0;
		for ( var turn = 0; turn < 500; turn++ )
		{
			waves.Turns( 1 );
			flat.Turns( 1 );
			off.Turns( 1 );
			if ( swimmerFlat.IsSwimming )
				Assert.AreEqual( (swimmerFlat.Navigation.X, swimmerFlat.Navigation.Y, swimmerFlat.SwimRandom, swimmerFlat.Happiness),
					(swimmerOff.Navigation.X, swimmerOff.Navigation.Y, swimmerOff.SwimRandom, swimmerOff.Happiness), $"turn {turn}: waves that are off move nobody, draw nothing and pay nothing" );
			if ( swimmerWaves.IsSwimming )
			{
				Assert.IsTrue( waves.Region.IsWater( swimmerWaves.Navigation.X, swimmerWaves.Navigation.Y ), $"turn {turn}: the push keeps the swimmer in the water" );
				if ( (swimmerWaves.Navigation.X, swimmerWaves.Navigation.Y) != (swimmerFlat.Navigation.X, swimmerFlat.Navigation.Y) )
					apart++;
			}
		}
		Assert.IsTrue( apart > 50, $"the waves move the swimmer ({apart} turns apart from the flat pool)" );
		// at most 1.2 per 24 turns while they run (120 of every 160 turns): 500 turns pay at most about 18, and the pushed swimmer's other happiness may differ from the flat pool's
		var paid = swimmerWaves.Happiness - swimmerFlat.Happiness;
		Assert.IsTrue( paid > 0.5f && paid < 20f, $"the waves paid {paid}" );
		Assert.AreEqual( 0, flat.Simulation.PoolActivities.Count( activity => activity.Id != PoolWaterEffects.ActivityId ) );
	}

	[TestMethod]
	public void TheWavesPushAlongTheirWayOnTheCrestByHeightAndEnvelopeAndNotInATroughOrARest()
	{
		var high = new PoolWaveSettings( true, 3, 0 );
		var low = new PoolWaveSettings( true, 1, 0 );
		// a point in the middle of a cell, where the drawn crest is the swimmer's own (SmoothWave takes the point in cells)
		foreach ( var (cellX, cellY) in new[] { (3.5f, 4.25f), (0.1f, 0.9f), (7.75f, 2.5f) } )
		{
			var (x, y) = ((int)(cellX * Fixed16.One), (int)(cellY * Fixed16.One));
			for ( var run = 0; run < 2; run++ )
			{
				var crests = 0;
				for ( var turn = run * (long)PoolWaves.CycleTurns( high ); turn < (run + 1) * (long)PoolWaves.CycleTurns( high ); turn++ )
				{
					var push = PoolWaves.Push( high, turn, x, y );
					Assert.AreEqual( push.X, push.Y, "along the diagonal" );
					Assert.IsTrue( push.X >= 0 && push.X <= PoolWaves.MaxPush, $"turn {turn}: {push.X}" );
					if ( !PoolWaves.IsRunning( high, turn ) )
						Assert.AreEqual( (0, 0), push, "nothing in a rest" );
					if ( push.X > 0 )
					{
						crests++;
						Assert.IsTrue( PoolWaves.SmoothWave( high, turn, cellX, cellY ) > 0f, $"({cellX}, {cellY}) turn {turn}: only while the drawn crest at the point is up" );
						var weak = PoolWaves.Push( low, turn, x, y ).X;
						Assert.IsTrue( weak > 0 && weak < push.X, "lower waves push less" );
					}
				}
				// the crest is up for about half of every period of the 120-turn run (less while it swells in and out)
				Assert.IsTrue( crests >= 45 && crests <= 60, $"({cellX}, {cellY}) run {run}: {crests} turns of push" );
			}
		}
		var middle = PoolWaves.RunTurns / 2;
		var peak = Enumerable.Range( 0, PoolWaves.PeriodTurns ).Max( offset => PoolWaves.Push( high, middle + offset, 3 * Fixed16.One, 4 * Fixed16.One ).X );
		Assert.IsTrue( peak > PoolWaves.MaxPush * 6 / 10, $"a full crest at height 3 pushes about MaxPush times cos 45 ({peak})" );
		Assert.AreEqual( (0, 0), PoolWaves.Push( new PoolWaveSettings( false, 3, 0 ), middle, 3 * Fixed16.One, 4 * Fixed16.One ), "off" );
		// the drift stops at the wall and at another body, and moves where there is room, also away from a body it is already too close to
		var region = new Rig().Region;
		var start = PoolSwimRegion.Centre( region.Access );
		var body = new PoolSwimBody { X = start.X, Y = start.Y };
		Assert.IsTrue( PoolSwimming.Drift( region, ref body, 1000, 1000, ReadOnlySpan<(int X, int Y)>.Empty ) );
		Assert.AreEqual( (start.X + 1000, start.Y + 1000), (body.X, body.Y) );
		var blocked = body;
		Assert.IsFalse( PoolSwimming.Drift( region, ref blocked, 40 * Fixed16.One, 0, ReadOnlySpan<(int X, int Y)>.Empty ), "not through the wall" );
		Assert.AreEqual( (body.X, body.Y), (blocked.X, blocked.Y) );
		var crowd = new[] { (body.X + 2000, body.Y + 2000) };
		Assert.IsFalse( PoolSwimming.Drift( region, ref blocked, 1500, 1500, crowd ), "not into another body" );
		Assert.IsTrue( PoolSwimming.Drift( region, ref blocked, -1500, -1500, crowd ), "but away from it" );
	}

	[TestMethod]
	public void ASaveWhileTheWavesPushASwimmerRestoresAndContinuesIdentically()
	{
		var settings = new PoolWaveSettings( true, 3, 0 );
		var (rig, guest) = EffectsRig( settings );
		// in the middle of a run, while a swimmer is being pushed
		rig.Until( () => guest.IsSwimming && PoolWaves.IsRunning( settings, rig.Simulation.ParkTurn ) && PoolWaves.Envelope( settings, rig.Simulation.ParkTurn ) == 64
			&& PoolWaves.Push( settings, rig.Simulation.ParkTurn, guest.Navigation.X, guest.Navigation.Y ).X > 0, 2000 );
		Assert.IsTrue( guest.IsSwimming );
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
		Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( snapshot ).Errors ) );
		var (copy, _) = EffectsRig( settings );
		copy.Simulation.RestoreSnapshot( snapshot );
		var twin = copy.Simulation.Guests.Single( other => other.Id == guest.Id );
		Assert.AreEqual( HashOf( rig ), HashOf( copy ) );
		for ( var tick = 0; tick < 3000 && (guest.IsSwimming || twin.IsSwimming); tick++ )
		{
			rig.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			copy.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			Assert.AreEqual( (guest.State, guest.Navigation.X, guest.Navigation.Y, guest.SwimRandom), (twin.State, twin.Navigation.X, twin.Navigation.Y, twin.SwimRandom), $"tick {tick}" );
		}
		Assert.AreEqual( HashOf( rig ), HashOf( copy ) );
	}

	[TestMethod]
	public void TheSprayOfAJetPaysSwimmersInsideItAndNoOneElse()
	{
		var near = new PoolAttractionSite( 4302, PoolAttractionKind.Jets, new[] { (7, 2) }, (7, 3) );
		var rig = new Rig();
		var sites = Sites( near );
		Assert.IsTrue( PoolWaterEffects.InSpray( sites, rig.Region, 7, 3 ) );
		Assert.IsTrue( PoolWaterEffects.InSpray( sites, rig.Region, 9, 5 ), "two cells away" );
		Assert.IsFalse( PoolWaterEffects.InSpray( sites, rig.Region, 10, 8 ), "the far corner" );
		Assert.IsFalse( PoolWaterEffects.InSpray( Sites( Rope ), rig.Region, 10, 5 ), "only jets spray" );
		Assert.IsFalse( PoolWaterEffects.InSpray( Sites(), rig.Region, 7, 3 ) );
		var (withJets, a) = EffectsRig( null, near );
		var (without, b) = EffectsRig( null );
		var moved = 0;
		float? atEnd = null, atEndWithout = null;
		for ( var turn = 0; turn < 700 && atEnd == null; turn++ )
		{
			withJets.Turns( 1 );
			without.Turns( 1 );
			if ( a.IsSwimming && PoolWaterEffects.InSpray( Sites( near ), withJets.Region, a.CellX, a.CellY ) )
				moved++;
			if ( a.State == GuestState.ChangingOut )
				(atEnd, atEndWithout) = (a.Happiness, b.Happiness);
		}
		Assert.IsNotNull( atEnd );
		Assert.IsTrue( atEnd >= atEndWithout, "the spray only adds" );
		Assert.IsTrue( moved > 12, $"{moved} turns of the swim were spent in the spray" );
		Assert.IsTrue( atEnd > atEndWithout, $"{moved} turns in the spray: {atEnd} against {atEndWithout}" );
		Assert.AreEqual( (b.Navigation.X, b.Navigation.Y), (a.Navigation.X, a.Navigation.Y), "and moves nobody" );
	}

	[TestMethod]
	public void ASwimmerBobsOnlyOnTheWavesOfItsPoolAndTheBobIsPresentationOnly()
	{
		var (rig, guest) = EffectsRig( new PoolWaveSettings( true, 3, 0 ) );
		rig.Until( () => guest.IsSwimming && PoolWaves.IsRunning( new PoolWaveSettings( true, 3, 0 ), rig.Simulation.ParkTurn ) && rig.Simulation.ParkTurn % 24 == 6, 400 );
		// asking for the bob reads the park turn and the presentation clock and changes nothing the simulation hashes
		var hash = HashOf( rig );
		for ( var i = 0; i < 5; i++ )
			_ = PoolWaterEffects.Bob( rig.Simulation, guest, EffectSites );
		Assert.AreEqual( hash, HashOf( rig ) );
		// where in the wave the swimmer is depends on when it entered the water: sample a whole wave cycle (24 turns), not one window
		var bobs = new List<float>();
		for ( var i = 0; i < 24 * 15; i++ )
		{
			bobs.Add( PoolWaterEffects.Bob( rig.Simulation, guest, EffectSites ) );
			rig.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
		}
		Assert.IsTrue( bobs.Max() > 0.01f || bobs.Min() < -0.01f, $"it bobs ({bobs.Min():F3} to {bobs.Max():F3})" );
		Assert.IsTrue( bobs.All( bob => Math.Abs( bob ) <= PoolWaves.BobHeights[2] + 1e-5f ) );
		var (still, sitter) = EffectsRig( null );
		still.Until( () => sitter.IsSwimming, 400 );
		Assert.AreEqual( 0f, PoolWaterEffects.Bob( still.Simulation, sitter, EffectSites ), "a pool without waves" );
		Assert.AreEqual( 0f, PoolWaterEffects.Bob( rig.Simulation, new Guest( 999, 0 ), EffectSites ), "a guest who is not swimming" );
		// the water level an activity draws its swimmer at rises with the same surface as a free swimmer's bob, and not without a region
		var region = PoolActivityHelpers.RegionOf( rig.Simulation, guest );
		Assert.IsNotNull( region );
		Assert.AreEqual( PoolWaterEffects.Bob( rig.Simulation, guest, EffectSites ), PoolWaterEffects.SurfaceRise( rig.Simulation, guest, region, EffectSites ), 1e-6f );
		Assert.AreEqual( 0f, PoolWaterEffects.SurfaceRise( rig.Simulation, guest, null, EffectSites ) );
	}
}

/// <summary>The shipped water attraction objects (content/pools): sixteen archives that load through the game's own pipelines, with the keys the engine reads.</summary>
[TestClass]
[DoNotParallelize]
public class PoolsAttractionsContentTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "space", "fantasy" };
	private static readonly (string Kind, PoolAttractionKind Parsed, string[] Shape, string Side, int Cost, int FirstId)[] Kinds =
	{
		("ropeswing", PoolAttractionKind.RopeSwing, new[] { "*" }, "N", 600, 65200),
		("jets", PoolAttractionKind.Jets, new[] { "*" }, "N", 450, 65204),
		("wavemachine", PoolAttractionKind.WaveMachine, new[] { "**" }, "N", 1500, 65208),
		("spa", PoolAttractionKind.Spa, new[] { "*" }, "*", 800, 65212),
		("bigspa", PoolAttractionKind.Spa, new[] { "**", "**" }, "*", 1800, 65216)
	};
	private string root = null!;
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-pools-attractions-" ).FullName;
		previousFileSystem = FileSystem;
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var candidate = Path.Combine( directory.FullName, "content", "pools" );
			if ( File.Exists( Path.Combine( candidate, "objects.json" ) ) )
			{
				PoolContent.DirectoryOverride = candidate;
				break;
			}
		}
		var totem = Path.Combine( root, "game", "levels", "jungle", "rides", "totem" );
		Directory.CreateDirectory( totem );
		File.WriteAllText( Path.Combine( totem, "totem.sam" ), "Info.Id\t1110\nInfo.Name\t\"Inca Totem\"\nInfo.WhichUIType\t0\nUpgrades[0].CostOfUpgrade\t100\n" );
		File.WriteAllBytes( Path.Combine( totem, "totem.MD2" ), Array.Empty<byte>() );
		FileSystem = new BaseFileSystem( Path.Combine( root, "game" ) );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		PoolContent.DirectoryOverride = null;
		FileSystem = previousFileSystem!;
		Directory.Delete( root, true );
	}

	[TestMethod]
	public void EveryThemeListsItsFourWaterAttractionsWithTheKeysTheEngineReads()
	{
		using var scope = ModContentRoots.Use( PoolsTests.PoolsSet( true ), FileSystem );
		for ( var theme = 0; theme < Themes.Length; theme++ )
		{
			var catalog = ObjectCatalog.Load( Themes[theme] );
			foreach ( var (kind, parsed, shape, side, cost, firstId) in Kinds )
			{
				var entry = catalog.Get( firstId + theme );
				var label = $"{Themes[theme]} {kind}";
				Assert.AreEqual( parsed, PoolAttractions.KindOf( entry ), label );
				Assert.IsFalse( PoolChangingRooms.IsChangingRoom( entry ), label );
				CollectionAssert.AreEqual( shape, entry.Shape.Rows.ToArray(), label );
				Assert.IsFalse( entry.Shape.Cells.Any( cell => cell.Kind is ObjectCellKind.Entrance or ObjectCellKind.Exit ), $"{label}: no entrance or exit: guests do not walk to it" );
				Assert.IsFalse( entry.IsChoosable || entry.HasQueue, label );
				Assert.AreEqual( 3, entry.WhichUIType, label );
				Assert.AreEqual( cost, entry.BuildCost, label );
				Assert.AreEqual( ObjectCategory.Feature, entry.Category, label );
				Assert.AreEqual( "mod:opentpw.pools:", entry.Root.KeyPrefix, label );
				Assert.AreEqual( side == "*" ? 4 : 1, PoolAttractions.WaterSides( entry ).Count, label );
				Assert.IsNotNull( entry.ScriptPath, label );
				Assert.AreEqual( 5, catalog.Entries.Count( item => item.ModId == "opentpw.pools" && PoolAttractions.KindOf( item ) != PoolAttractionKind.None ), $"{Themes[theme]}: five attractions (the large spa is the fifth)" );
			}
		}
	}

	[TestMethod]
	public void EveryAttractionsModelAndTexturesLoadAndStandOnTheirCells()
	{
		using var scope = ModContentRoots.Use( PoolsTests.PoolsSet( true ), FileSystem );
		for ( var theme = 0; theme < Themes.Length; theme++ )
			foreach ( var (kind, _, shape, _, _, firstId) in Kinds )
			{
				var entry = ObjectCatalog.Load( Themes[theme] ).Get( firstId + theme );
				var label = $"{Themes[theme]} {kind}";
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				Assert.AreEqual( ModelFileKind.Geometry, model.Kind, label );
				Assert.IsTrue( model.Meshes.Count >= 1, label );
				var textures = new HashSet<string>();
				foreach ( var mesh in model.Meshes )
				{
					Assert.IsNull( ObjectAssets.GetUnrenderableReason( mesh ), $"{label}: {mesh.Name}" );
					foreach ( var material in mesh.Materials )
						textures.Add( material.Name );
				}
				Assert.IsTrue( textures.Count >= 2, $"{label}: {string.Join( ", ", textures )}" );
				foreach ( var name in textures )
				{
					var texture = ObjectAssets.ResolveTexture( entry, name );
					Assert.IsNotNull( texture, $"{label}: {name}" );
					var (data, width, height) = Texture.DecodeImage( texture!.Value.FileSystem, texture.Value.Path );
					Assert.IsTrue( width is 64 or 128 && height == width && data.Length == width * height * 4, $"{label}: {name} is {width} x {height}" );
				}
				var width10 = shape[0].Length * 10f;
				var height10 = shape.Length * 10f;
				var composed = model.Meshes.SelectMany( mesh => mesh.Vertices.Select( vertex => System.Numerics.Vector3.Transform( vertex.Position.GetSystemVector3(), World( model, mesh.NodeIndex ) ) ) ).ToArray();
				Assert.IsTrue( composed.Min( p => p.X ) >= -0.5f && composed.Max( p => p.X ) <= width10 + 0.5f, $"{label}: within its {width10} units across ({composed.Min( p => p.X ):F1} to {composed.Max( p => p.X ):F1})" );
				Assert.IsTrue( kind != "bigspa" || composed.Min( p => p.Z ) >= -0.5f && composed.Max( p => p.Z ) <= height10 + 0.5f, $"{label}: within its {height10} units along" );
				Assert.IsTrue( composed.Min( p => p.Y ) >= -0.5f, $"{label}: stands on the ground" );
				Assert.IsTrue( composed.Max( p => p.Y ) > 3f, $"{label}: has some height" );
				Assert.AreEqual( 0, RideHeadSpace.Create( model, seed: 1 )?.Count ?? 0, $"{label}: no head records" );
			}
	}

	private static System.Numerics.Matrix4x4 World( ModelFile model, int node )
	{
		var world = System.Numerics.Matrix4x4.Identity;
		for ( var index = node; index >= 0; index = model.Nodes[index].ParentIndex )
			world *= model.Nodes[index].Transform;
		return world;
	}

	[TestMethod]
	public void ThePlacementRuleOfTheShippedObjectsNeedsWaterOnTheirWaterSideForEveryRotation()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		using var catalogScope = ModContentRoots.Use( PoolsTests.PoolsSet( true ), FileSystem );
		var rope = ObjectCatalog.Load( "jungle" ).Get( 65200 );
		var spa = ObjectCatalog.Load( "jungle" ).Get( 65212 );
		var wave = ObjectCatalog.Load( "space" ).Get( 65210 );
		var cabin = ObjectCatalog.Load( "jungle" ).Get( 65500 );
		var cells = new ParkCellMap( 32, 32 );
		Assert.AreEqual( OriginalPlacementResult.NeedsPoolWater, PoolAttractions.PlacementRule( rope, cells, 10, 10, 0 ), "no water: refused" );
		Assert.IsNull( PoolAttractions.PlacementRule( cabin, cells, 10, 10, 0 ), "the changing room has its own rule" );
		// the water side of the unrotated object is north, then west, south and east as it turns
		var sides = new Dictionary<int, (int X, int Y)> { [0] = (10, 9), [90] = (9, 10), [180] = (10, 11), [270] = (11, 10) };
		foreach ( var (rotation, water) in sides )
		{
			var map = new ParkCellMap( 32, 32 );
			map.SetType( water.X, water.Y, ParkCellTypes.PoolWater );
			Assert.IsNull( PoolAttractions.PlacementRule( rope, map, 10, 10, rotation ), $"rotation {rotation}: water on its side" );
			foreach ( var other in sides.Where( pair => pair.Key != rotation ) )
				Assert.AreEqual( OriginalPlacementResult.NeedsPoolWater, PoolAttractions.PlacementRule( rope, WaterAt( other.Value ), 10, 10, rotation ), $"rotation {rotation}: water on another side does not do" );
			Assert.IsNull( PoolAttractions.PlacementRule( spa, map, 10, 10, rotation ), $"a spa takes water on any side ({rotation})" );
		}
		// the 2 x 1 wave machine: water in front of either of its cells
		Assert.IsNull( PoolAttractions.PlacementRule( wave, WaterAt( (11, 9) ), 10, 10, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.NeedsPoolWater, PoolAttractions.PlacementRule( wave, WaterAt( (12, 9) ), 10, 10, 0 ) );
		using ( OpenTpwExtensions.Use( PoolsTests.PoolsSet( false ) ) )
			Assert.IsNull( PoolAttractions.PlacementRule( rope, cells, 10, 10, 0 ), "no opinion with the extension off" );
		Assert.IsNull( PoolAttractions.PlacementRule( rope, null, 10, 10, 0 ), "nor without a cell map" );
	}

	private static ParkCellMap WaterAt( (int X, int Y) cell )
	{
		var map = new ParkCellMap( 32, 32 );
		map.SetType( cell.X, cell.Y, ParkCellTypes.PoolWater );
		return map;
	}
}

/// <summary>The water attractions against a real <see cref="Level"/>: placement through the level's hooks, the sites derived from the placed objects, the erase guard and the removal.</summary>
[TestClass]
[DoNotParallelize]
public class PoolsAttractionsLevelTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private Level level = null!;
	private OriginalObject one = null!;

	[TestInitialize]
	public void Initialize()
	{
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
		var type = typeof( UpgradeCatalogueDefaultsTests );
		var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.Invoke( fixture, new object[] { "", 0, false } )!;
		type.GetMethod( "PrepareCapture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic )!.Invoke( null, new object[] { world } );
		typeof( Level ).GetMethod( "SetupStaff", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.Invoke( world.Level, null );
		typeof( Level ).GetMethod( "SetupParkGate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.Invoke( world.Level, null );
		(level, one) = (world.Level, world.One);
		level.Objects.Things ??= new ThingPool();
		typeof( Level ).GetMethod( "SetupPaths", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.Invoke( level, null );
		level.ConfigureObjectHooks();
		typeof( Level ).GetMethod( "ConnectObjectsToGuests", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.Invoke( level, null );
	}

	[TestCleanup]
	public void Cleanup()
	{
		fixture?.Cleanup();
		renderer?.Dispose();
	}

	private ParkCellMap Cells => level.Guests!.Grid.Cells;

	private static T With<T>( Func<T> action )
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		return action();
	}

	private ObjectCatalogEntry Entry( int id, string kind, string side, params string[] shape )
	{
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {id}\nInfo.Name Synthetic{id}\nInfo.IsChoosable 0\nInfo.OverwritePriority 5\nPools.Attraction {kind}\nPools.WaterSide {side}\n", $"synthetic{id}.sam" ) } );
		return new ObjectCatalogEntry( "jungle", ObjectCategory.Feature, "/synthetic", settings, new ObjectShape( shape ), "/model.MD2", null, "/script.RSE",
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), one.Entry.FileSystem, null );
	}

	[TestMethod]
	public void AWaterAttractionNeedsWaterOnItsSideThroughTheLevelsHooksAndBecomesASiteOnceItStandsThere()
	{
		var swing = Entry( 1301, "ropeswing", "N", "*" );
		With( () =>
		{
			Assert.AreEqual( PoolAttractionKind.RopeSwing, PoolAttractions.KindOf( swing ) );
			Assert.IsNotNull( level.Objects.PlacementRule );
			Assert.IsNotNull( level.Guests );
			Assert.AreEqual( OriginalPlacementResult.NeedsPoolWater, PoolAttractions.PlacementRule( swing, level.Guests!.Grid.Cells, 20, 20, 0 ) );
			Assert.AreEqual( OriginalPlacementResult.NeedsPoolWater, level.Objects.PlacementRule!( swing, 20, 20, 0 ) );
			Assert.AreEqual( OriginalPlacementResult.NeedsPoolWater, level.Objects.Check( swing, 20, 20, 0 ), "no water beside it" );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder!.Paint( 20, 19 ) );
			Assert.AreEqual( OriginalPlacementResult.Allowed, level.Objects.Check( swing, 20, 20, 0 ), "water to its north" );
			Assert.AreEqual( OriginalPlacementResult.NeedsPoolWater, level.Objects.Check( swing, 20, 20, 180 ), "turned round, its side faces dry land" );
			var attractionsBefore = level.Guests!.Attractions.Count;
			var placed = level.Objects.TryPlace( swing, 20, 20, 0, out _ );
			Assert.IsNotNull( placed );
			Assert.AreEqual( attractionsBefore, level.Guests.Attractions.Count, "guests never choose it from the park: it is no attraction of theirs" );
			var site = level.PoolSites.All.Single();
			Assert.AreEqual( (PoolAttractionKind.RopeSwing, (20, 19)), (site.Kind, site.Access) );
			Assert.AreEqual( placed!.ThingId, site.ObjectId );
			CollectionAssert.AreEqual( new[] { (20, 20) }, site.Cells.ToArray() );
			return 0;
		} );
		Assert.AreEqual( 0, With( () => level.PoolSites.All.Count ) - 1, "one site" );
		using ( OpenTpwExtensions.Use( PoolsTests.PoolsSet( false ) ) )
			Assert.AreEqual( 0, level.PoolSites.All.Count, "no site while the extension is off" );
	}

	[TestMethod]
	public void TheLastWaterCellOfAnAttractionsSideCannotBeErasedWhileAnotherCan()
	{
		var wave = Entry( 1302, "wavemachine", "N", "**" );
		With( () =>
		{
			level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (22, 18), (24, 19) ) );
			Assert.IsNotNull( level.Objects.TryPlace( wave, 22, 20, 0, out _ ) );
			// the side cells (22, 19) and (23, 19) are both water: one may go, the last may not
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.CheckErase( 22, 19 ) );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.Erase( 22, 19 ) );
			Assert.AreEqual( PoolBuildResult.AttractionAccess, level.PoolBuilder.CheckErase( 23, 19 ), "the last one" );
			Assert.AreEqual( PoolBuildResult.AttractionAccess, level.PoolBuilder.Erase( 23, 19 ) );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.CheckErase( 24, 19 ), "a cell that is not on its side" );
			return 0;
		} );
	}

	[TestMethod]
	public void AWaveMachineRunsTheDefaultWavesUntilTheClickAndTheWavesGoWithTheMachine()
	{
		var wave = Entry( 1303, "wavemachine", "N", "**" );
		With( () =>
		{
			level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (4, 22), (9, 24) ) );
			var region = PoolSwimRegion.Build( Cells, (4, 22) )!;
			Assert.IsNull( PoolWaveSettings.ActiveIn( level.PoolSites, Cells, region ), "no machine, no waves" );
			var placed = level.Objects.TryPlace( wave, 5, 25, 0, out _ );
			Assert.IsNotNull( placed );
			Assert.AreEqual( PoolWaveSettings.Default, PoolWaveSettings.ActiveIn( level.PoolSites, Cells, region ), "a new machine runs the default at once" );
			Assert.AreEqual( 0, region.Cells.Count( cell => PoolWaveSettings.Has( Cells.FlagsAt( cell.X, cell.Y ) ) ), "and wrote nothing to the cells" );
			level.CyclePoolWaves( placed! );
			Assert.AreEqual( new PoolWaveSettings( true, 3, 0 ), PoolWaveSettings.ActiveIn( level.PoolSites, Cells, region ) );
			typeof( Level ).GetField( "demolishedQueues", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( level, new HashSet<OriginalObject>() );
			Assert.IsTrue( level.Objects.Remove( placed! ) );
			Assert.IsNull( PoolWaveSettings.ActiveIn( level.PoolSites, Cells, region ), "the machine is gone: the pool has no waves" );
			var again = level.Objects.TryPlace( wave, 5, 25, 0, out _ );
			Assert.AreEqual( new PoolWaveSettings( true, 3, 0 ), PoolWaveSettings.ActiveIn( level.PoolSites, Cells, region ), "the settings of the access cell are the cell's, and a machine standing there again takes them up; erasing the cell is what clears them" );
			Assert.IsNotNull( again );
			return 0;
		} );
	}

	[TestMethod]
	public void AClickOnAWaveMachineStepsTheWavesOfItsPoolAndOnlyOfAWaveMachine()
	{
		var wave = Entry( 1306, "wavemachine", "N", "**" );
		var swing = Entry( 1307, "ropeswing", "N", "*" );
		With( () =>
		{
			level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (4, 12), (9, 14) ) );
			var machine = level.Objects.TryPlace( wave, 5, 15, 0, out _ )!;
			var rope = level.Objects.TryPlace( swing, 8, 15, 0, out _ )!;
			var region = PoolSwimRegion.Build( Cells, (4, 12) )!;
			Assert.AreEqual( PoolWaveSettings.Default, PoolWaveSettings.ActiveIn( level.PoolSites, Cells, region ) );
			Assert.AreEqual( new PoolWaveSettings( true, 3, 0 ), level.CyclePoolWaves( machine ) );
			Assert.AreEqual( new PoolWaveSettings( true, 1, 2 ), level.CyclePoolWaves( machine ) );
			Assert.AreEqual( new PoolWaveSettings( false, 2, 1 ), level.CyclePoolWaves( machine ) );
			Assert.IsNull( PoolWaveSettings.ActiveIn( level.PoolSites, Cells, region ), "off: no waves" );
			Assert.AreEqual( PoolWaveSettings.Default, level.CyclePoolWaves( machine ) );
			Assert.IsNull( level.CyclePoolWaves( rope ), "a rope swing has no waves" );
			return 0;
		} );
		Assert.IsNull( level.CyclePoolWaves( one ), "and an ordinary object neither" );
	}

	[TestMethod]
	public void TheWaveMachineWindowSetsOnOffHeightAndIntervalAndCancelLeavesThemAsTheyWere()
	{
		var wave = Entry( 1309, "wavemachine", "N", "**" );
		var swing = Entry( 1310, "ropeswing", "N", "*" );
		using var gates = WindowGates();
		With( () =>
		{
			level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (4, 12), (9, 14) ) );
			var machine = level.Objects.TryPlace( wave, 5, 15, 0, out _ )!;
			var rope = level.Objects.TryPlace( swing, 8, 15, 0, out _ )!;
			var region = PoolSwimRegion.Build( Cells, (4, 12) )!;
			var hud = new ParkHud( level, OriginalUiTests.FakeStrings(), new EconomyParkStatus( () => null ), new UiTestCatalog(), new HudHost() );
			Assert.AreEqual( PoolWaveSettings.Default, level.PoolWavesOf( machine ) );

			// the window opens on the machine's settings, the choices show them, and the tick writes the edited copy to the machine
			hud.SelectObject( machine );
			hud.OpenWaveSettings( machine );
			var screen = hud.Stack.Top!;
			Assert.AreEqual( "waveSettings", screen.Name );
			Assert.IsTrue( Selected( screen, "wavesOn" ) && Selected( screen, "height2" ) && Selected( screen, "interval1" ) );
			Assert.IsFalse( Selected( screen, "wavesOff" ) || Selected( screen, "height3" ) || Selected( screen, "interval0" ) );
			screen.Find( "height3" )!.Activate();
			screen.Find( "interval0" )!.Activate();
			Assert.AreEqual( PoolWaveSettings.Default, level.PoolWavesOf( machine ), "nothing is written before the tick" );
			screen.Find( "apply" )!.Activate();
			Assert.AreNotSame( screen, hud.Stack.Top, "the tick closes the window" );
			Assert.AreEqual( new PoolWaveSettings( true, 3, 0 ), level.PoolWavesOf( machine ) );
			Assert.AreEqual( new PoolWaveSettings( true, 3, 0 ), PoolWaveSettings.ActiveIn( level.PoolSites, Cells, region ), "the pool runs them" );

			// an unchanged tick writes nothing
			var flags = Cells.FlagsAt( level.PoolSites.Find( machine.ThingId )!.Access.X, level.PoolSites.Find( machine.ThingId )!.Access.Y );
			hud.OpenWaveSettings( machine );
			hud.Stack.Top!.Find( "apply" )!.Activate();
			Assert.AreEqual( flags, Cells.FlagsAt( level.PoolSites.Find( machine.ThingId )!.Access.X, level.PoolSites.Find( machine.ThingId )!.Access.Y ) );

			// off and a long rest, then the cross: nothing changes
			hud.OpenWaveSettings( machine );
			screen = hud.Stack.Top!;
			screen.Find( "wavesOff" )!.Activate();
			screen.Find( "interval2" )!.Activate();
			screen.Find( "cancel" )!.Activate();
			Assert.AreEqual( new PoolWaveSettings( true, 3, 0 ), level.PoolWavesOf( machine ), "the cross leaves them as they were" );
			hud.OpenWaveSettings( machine );
			screen = hud.Stack.Top!;
			screen.Find( "wavesOff" )!.Activate();
			screen.Find( "height1" )!.Activate();
			screen.Find( "interval2" )!.Activate();
			screen.Find( "apply" )!.Activate();
			Assert.AreEqual( new PoolWaveSettings( false, 1, 2 ), level.PoolWavesOf( machine ) );
			Assert.IsNull( PoolWaveSettings.ActiveIn( level.PoolSites, Cells, region ), "off: no waves" );

			// only a wave machine has the window
			var screens = hud.Stack.Screens.Count;
			hud.OpenWaveSettings( rope );
			hud.OpenWaveSettings( one );
			Assert.AreEqual( screens, hud.Stack.Screens.Count );
			Assert.IsNull( level.PoolWavesOf( rope ) );
			Assert.IsFalse( level.SetPoolWaves( rope, PoolWaveSettings.Default ) );
			return 0;
		} );
	}

	[TestMethod]
	public void TheWaveMachineWindowsChoicesFitItAndAreNamedInEveryLanguage()
	{
		var wave = Entry( 1311, "wavemachine", "N", "**" );
		using var gates = WindowGates();
		With( () =>
		{
			level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (4, 12), (9, 14) ) );
			var machine = level.Objects.TryPlace( wave, 5, 15, 0, out _ )!;
			var hud = new ParkHud( level, OriginalUiTests.FakeStrings(), new EconomyParkStatus( () => null ), new UiTestCatalog(), new HudHost() );
			hud.OpenWaveSettings( machine );
			var screen = hud.Stack.Top!;
			var canvas = new UiCanvas( 2048, 1536 );
			var window = screen.Find( "window" )!.ScreenRect( canvas );
			foreach ( var element in screen.Elements.Where( element => element.Id != "window" ) )
			{
				var rect = element.ScreenRect( canvas );
				Assert.IsTrue( rect.X >= window.X && rect.Y >= window.Y && rect.Right <= window.Right && rect.Bottom <= window.Bottom, $"{element.Id} lies inside the window" );
			}
			return 0;
		} );
		var keys = new[] { OpenTpwText.PoolWavesSettings, OpenTpwText.PoolWavesLow, OpenTpwText.PoolWavesMedium, OpenTpwText.PoolWavesHigh,
			OpenTpwText.PoolWavesShort, OpenTpwText.PoolWavesNormal, OpenTpwText.PoolWavesLong };
		foreach ( var language in SupplementaryStrings.SupportedLanguages )
			foreach ( var key in keys )
			{
				var text = SupplementaryStrings.Get( key, language );
				Assert.IsFalse( string.IsNullOrWhiteSpace( text ), $"{language} {key}" );
				Assert.IsTrue( text.Length <= 24, $"{language} {key}: '{text}' fits a toggle" );
			}
	}

	/// <summary>
	/// The shared input state the settings window's gate wants, restored on dispose: the park camera (it does not open while the camera rides or walks) and a keyboard no text field holds (it does
	/// not open while text is typed, and a field an earlier test focused leaves <see cref="Input.TextEntryActive"/> set, which only the game's frame clears).
	/// </summary>
	private IDisposable WindowGates()
	{
		// the fixture's level is made without its constructor; the window's input gate asks the cell tool whether it is armed
		if ( level.CellTool == null )
			typeof( Level ).GetField( "<CellTool>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( level, new CellBuildTool() );
		var previous = (Camera.Mode, Input.TextEntryActive);
		Camera.Mode = new ParkCameraMode();
		Input.TextEntryActive = false;
		return new Restore( () => (Camera.Mode, Input.TextEntryActive) = previous );
	}

	private sealed class Restore( Action action ) : IDisposable { public void Dispose() => action(); }

	private static bool Selected( UiScreen screen, string id ) => ((UiButton)screen.Find( id )!).Selected!();

	private sealed class UiTestCatalog : IBuildCatalog { public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => Array.Empty<BuildItem>(); }

	[TestMethod]
	public void TheSpaCellIsKeptWhileUsedAndRemovingTheSpaSendsItsGuestOut()
	{
		var spa = Entry( 1308, "spa", "*", "*" );
		With( () =>
		{
			level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (12, 12), (15, 14) ).Where( cell => cell != (15, 13) ) );
			var placed = level.Objects.TryPlace( spa, 15, 13, 0, out _ )!;
			var site = level.PoolSites.All.Single();
			Assert.AreEqual( (15, 12), site.Access, "the first side with water: north" );
			// a guest sitting in it (the host's own fields; the activity is the real one)
			level.Guests!.Grid.SetPath( 6, 6, true );
			var guest = level.Guests.SpawnInPark( 6, 6 );
			guest.State = GuestState.PoolActivity;
			guest.PoolActivityId = SpaActivity.ActivityId;
			guest.PoolActivityTarget = site.ObjectId;
			Assert.AreEqual( PoolBuildResult.Swimmers, level.PoolBuilder.CheckErase( 15, 12 ), "the access cell is kept while somebody is using the spa" );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.CheckErase( 12, 12 ), "any other cell goes" );
			// removing the spa ends the session of whoever is in it at once
			typeof( Level ).GetField( "demolishedQueues", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( level, new HashSet<OriginalObject>() );
			Assert.IsTrue( level.Objects.Remove( placed ) );
			Assert.AreNotEqual( GuestState.PoolActivity, guest.State, "the guest left the activity with its object" );
			Assert.IsNull( guest.PoolActivityId );
			return 0;
		} );
	}

	[TestMethod]
	public void APoolIsNotPaintedOnACellThatCarriesAFlagOfTheOriginalAndForgedPoolFlagsOnDryCellsAreRefused()
	{
		With( () =>
		{
			Cells.SetFlags( 30, 30, ParkCellFlags.EntranceArea );
			Assert.AreEqual( PoolBuildResult.Occupied, level.PoolBuilder!.Check( 30, 30 ), "the entrance complex's flag is not silently cleared" );
			Cells.SetFlags( 31, 30, ParkCellFlags.NoModify );
			Assert.AreEqual( PoolBuildResult.Occupied, level.PoolBuilder.Check( 31, 30 ) );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.Check( 28, 30 ), "control: a plain cell" );
			var layout = ParkLayoutSnapshot.CaptureSnapshot( Cells, level.Objects );
			var dry = new ParkLayoutSnapshot.CellData { X = 28, Y = 28, Type = ParkCellTypes.Object, Flags = (ushort)ParkCellFlags.PoolRiver, Placements = 0, Links = 0, QueueLink = 0 };
			var forged = layout with { Cells = layout.Cells with { Cells = layout.Cells.Cells.Append( dry ).ToList() } };
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( forged ) ).Message, "no pool cell" );
			var wavy = dry with { Flags = (ushort)(ParkCellFlags.PoolWavesOn | (ParkCellFlags)0x0100) };
			var forged2 = layout with { Cells = layout.Cells with { Cells = layout.Cells.Cells.Append( wavy ).ToList() } };
			Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( forged2 ) );
			return 0;
		} );
	}

	[TestMethod]
	public void RemovingAnAttractionForgetsItsSite()
	{
		var spa = Entry( 1305, "spa", "*", "*" );
		With( () =>
		{
			level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (12, 22), (14, 24) ) );
			var placed = level.Objects.TryPlace( spa, 15, 23, 0, out _ );
			Assert.IsNotNull( placed );
			Assert.AreEqual( 1, level.PoolSites.All.Count );
			// the fixture's level is built without its constructor, so this field has no initializer run
			typeof( Level ).GetField( "demolishedQueues", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( level, new HashSet<OriginalObject>() );
			Assert.IsTrue( level.Objects.Remove( placed! ) );
			Assert.AreEqual( 0, level.PoolSites.All.Count );
			return 0;
		} );
	}
}
