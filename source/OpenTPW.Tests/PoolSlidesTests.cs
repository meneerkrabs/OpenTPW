using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.PoolsSwimSessionTests;

namespace OpenTPW.Tests;

/// <summary>
/// Water slides of the pools extension (docs/POOLS.md, "Water slides"): the track geometry, the open-track rules, the builder, the ride, the activity against a real <see cref="GuestSimulation"/>,
/// the saves and the pin. Every other track type keeps its rules: a slide never touches the track layer (<see cref="TrackCircuitWalker"/>).
/// </summary>
[TestClass]
public class PoolSlidesTests
{
	// ---- Fixtures -----------------------------------------------------------------------------------------

	private static IDisposable PoolsOn() => OpenTPW.Mods.OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );

	private static PoolSlideSite Site( ParkCellMap cells, float ground = 0f, Func<float, float, float>? shape = null, Func<int, int, bool>? reserved = null ) => new()
	{
		Cells = cells,
		Ground = shape ?? ( ( _, _ ) => ground ),
		WaterLevel = ( x, y ) => cells.InBounds( x, y ) && cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater ? ground - 0.12f : null,
		IsReserved = reserved ?? ( ( _, _ ) => false )
	};

	/// <summary>A tower of 2 x 2 cells at (8, 12) whose front faces east: pool-side cells (10, 12) and (10, 13), beside a pool of 10 x 10 cells at x 10 to 19, y 10 to 19.</summary>
	private static (ParkCellMap Cells, PoolSlideTower Tower) Fixture()
	{
		var cells = new ParkCellMap( 40, 40 );
		for ( var y = 10; y < 20; y++ )
			for ( var x = 10; x < 20; x++ )
				cells.SetType( x, y, ParkCellTypes.PoolWater );
		var tower = new PoolSlideTower( 8, 12, new[] { (8, 12), (9, 12), (8, 13), (9, 13) }, new[] { (10, 12), (10, 13) }, PoolSlideProfile.Flume, 4 );
		return (cells, tower);
	}

	/// <summary>The cells of a standard slide from the tower of <see cref="Fixture"/>: north, east, then south into the pool: seven pieces, 6.07 cells long.</summary>
	private static readonly (int X, int Y)[] StandardCells = { (8, 11), (8, 10), (8, 9), (9, 9), (10, 9), (11, 9), (11, 10) };

	private static PoolSlideWorld World( ParkCellMap cells, PoolSlideTower tower, ParkEconomy? economy = null, Func<int, int, bool>? reserved = null ) =>
		new( () => Site( cells, reserved: reserved ), id => id == tower.Id ? tower : null, () => economy );

	private static void Build( PoolSlideWorld world, PoolSlideTower tower, IEnumerable<(int X, int Y)> cells )
	{
		foreach ( var (x, y) in cells )
			Assert.AreEqual( PoolSlideResult.Ok, world.AddCell( tower.X, tower.Y, x, y ), $"cell ({x}, {y})" );
	}

	private static PoolSlide StandardSlide( out ParkCellMap cells, out PoolSlideTower tower, out PoolSlideWorld world )
	{
		(cells, tower) = Fixture();
		world = World( cells, tower );
		Build( world, tower, StandardCells );
		return world.Find( tower.Id )!;
	}

	// ---- Geometry -------------------------------------------------------------------------------------------

	[TestMethod]
	public void ThePiecesFollowTheCellsAndTheLengthsAddUp()
	{
		using var scope = PoolsOn();
		var slide = StandardSlide( out _, out _, out _ );
		CollectionAssert.AreEqual( new[] { PoolSlidePiece.Straight, PoolSlidePiece.Straight, PoolSlidePiece.Right, PoolSlidePiece.Straight, PoolSlidePiece.Straight, PoolSlidePiece.Right, PoolSlidePiece.Splash }, slide.Pieces.ToArray() );
		CollectionAssert.AreEqual( StandardCells, slide.Cells.ToArray() );
		Assert.AreEqual( 4 * PoolSlide.StraightLength + 2 * PoolSlide.CurveLength + PoolSlide.SplashLength, slide.Length );
		Assert.IsTrue( slide.IsComplete );
		Assert.AreEqual( (11, 10), slide.LandingCell );
		Assert.AreEqual( (11 * 1024 + 512, 10 * 1024 + 512), slide.LandingPoint );
		Assert.AreEqual( 3, slide.StartHeading, "north" );
		Assert.AreEqual( 0, slide.HeadingAfter( 2 ), "east after the first turn" );
		Assert.AreEqual( 1, slide.HeadingAfter( 5 ), "south after the second" );
	}

	[TestMethod]
	public void ThePointsRunContinuouslyFromTheTowerEdgeToTheMiddleOfTheSplashCell()
	{
		using var scope = PoolsOn();
		var slide = StandardSlide( out _, out _, out _ );
		var first = slide.PointAt( 0 );
		Assert.AreEqual( (8 * 1024 + 512, 11 * 1024 + 1024), (first.X, first.Y), "starts on the cell edge next to the tower (the tower's north face is y = 12)" );
		var last = slide.PointAt( slide.Length );
		Assert.AreEqual( slide.LandingPoint, (last.X, last.Y), "ends in the middle of the splash cell" );
		var previous = first;
		for ( var position = 1; position <= slide.Length; position++ )
		{
			var point = slide.PointAt( position );
			var step = Math.Sqrt( Math.Pow( point.X - previous.X, 2 ) + Math.Pow( point.Y - previous.Y, 2 ) );
			Assert.IsTrue( step <= 2.0, $"position {position}: a step of {step:F2} units" );
			previous = point;
			// every point lies in its piece's cell
			var cell = slide.Cells[slide.PieceAt( position )];
			Assert.IsTrue( point.X >= cell.X * 1024 - 1 && point.X <= cell.X * 1024 + 1024 + 1 && point.Y >= cell.Y * 1024 - 1 && point.Y <= cell.Y * 1024 + 1024 + 1, $"position {position} at ({point.X}, {point.Y}) lies in cell {cell}" );
		}
	}

	[TestMethod]
	public void ACurveIsAQuarterCircleOfRadiusHalfACellAndTheHeightFallsAtOneSlope()
	{
		using var scope = PoolsOn();
		var slide = StandardSlide( out _, out _, out _ );
		// the first turn: enters the cell (8, 9) at its south edge, leaves at its east edge; the circle is round the south east corner, radius 512
		var start = slide.StartOf( 2 );
		var (cornerX, cornerY) = (8 * 1024 + 1024, 9 * 1024 + 1024);
		for ( var local = 0; local <= PoolSlide.CurveLength; local += 24 )
		{
			var point = slide.PointAt( start + local );
			var radius = Math.Sqrt( Math.Pow( point.X - cornerX, 2 ) + Math.Pow( point.Y - cornerY, 2 ) );
			Assert.AreEqual( 512.0, radius, 4.0, $"local {local}" );
		}
		Assert.AreEqual( slide.TopHeight, slide.HeightAt( 0 ) );
		Assert.AreEqual( slide.EndHeight, slide.HeightAt( slide.Length ) );
		var middle = slide.HeightAt( slide.Length / 2 );
		Assert.AreEqual( (slide.TopHeight + slide.EndHeight) / 2, middle, 2 );
		Assert.IsTrue( slide.SlopePermille is > 380 and < 430, $"{slide.SlopePermille} thousandths" );
	}

	// ---- The rules ----------------------------------------------------------------------------------------------

	private static PoolSlideResult Check( PoolSlide slide, PoolSlideTower? tower, ParkCellMap cells, PoolSlideSite? site = null ) => PoolSlideRules.Validate( slide, tower, site ?? Site( cells ) );

	private static PoolSlide Make( PoolSlideTower tower, ParkCellMap cells, IEnumerable<(int X, int Y)> path, int heading = 3, PoolSlideSite? site = null )
	{
		var list = path.ToList();
		var pieces = new List<PoolSlidePiece>();
		var h = heading;
		for ( var index = 0; index < list.Count; index++ )
		{
			if ( index == list.Count - 1 )
			{
				pieces.Add( cells.RawTypeAt( list[index].X, list[index].Y ) == ParkCellTypes.PoolWater ? PoolSlidePiece.Splash : PoolSlidePiece.Straight );
				break;
			}
			var (dx, dy) = (list[index + 1].X - list[index].X, list[index + 1].Y - list[index].Y);
			var next = Enumerable.Range( 0, 4 ).First( d => PoolSlide.Direction( d ) == (dx, dy) );
			pieces.Add( ((next - h) & 3) switch { 0 => PoolSlidePiece.Straight, 1 => PoolSlidePiece.Right, _ => PoolSlidePiece.Left } );
			h = next;
		}
		site ??= Site( cells );
		var end = list.Count > 0 && cells.RawTypeAt( list[^1].X, list[^1].Y ) == ParkCellTypes.PoolWater ? PoolSlideRules.EndHeightOf( list[^1].X, list[^1].Y, site ) ?? 0 : PoolSlideRules.TopHeightOf( tower, site );
		return new PoolSlide( tower.X, tower.Y, tower.Profile, list[0].X, list[0].Y, heading, pieces, PoolSlideRules.TopHeightOf( tower, site ), end );
	}

	[TestMethod]
	public void AValidSlideEndsInTheWaterOfTheTowersPoolAndEveryRefusalHasItsOwnReason()
	{
		using var scope = PoolsOn();
		var (cells, tower) = Fixture();
		var good = Make( tower, cells, StandardCells );
		Assert.AreEqual( PoolSlideResult.Ok, Check( good, tower, cells ) );

		Assert.AreEqual( PoolSlideResult.NoTower, Check( good, null, cells ) );
		Assert.AreEqual( PoolSlideResult.NoTower, Check( good, tower with { X = 20 }, cells ), "another tower" );
		Assert.AreEqual( PoolSlideResult.NoPoolSide, Check( good, tower with { PoolSide = new[] { (1, 1) } }, cells ) );
		Assert.AreEqual( PoolSlideResult.Empty, Check( new PoolSlide( 8, 12, PoolSlideProfile.Flume, 8, 11, 3, Array.Empty<PoolSlidePiece>(), 5000, 0 ), tower, cells ) );
		Assert.AreEqual( PoolSlideResult.NoSplash, Check( Make( tower, cells, StandardCells.Take( 6 ) ), tower, cells ), "an open end: not complete" );
		Assert.AreEqual( PoolSlideResult.NoSplash, Check( new PoolSlide( 8, 12, PoolSlideProfile.Flume, 8, 11, 3, new[] { PoolSlidePiece.Splash, PoolSlidePiece.Straight }, 5000, 0 ), tower, cells ), "a splash piece in the middle" );
		Assert.AreEqual( PoolSlideResult.TooLong, Check( new PoolSlide( 8, 12, PoolSlideProfile.Flume, 8, 11, 3, Enumerable.Repeat( PoolSlidePiece.Straight, 25 ), 5000, 0 ), tower, cells ) );
		// the track must leave the tower outwards: starting at a cell that touches no footprint cell, or inside the pool side
		Assert.AreEqual( PoolSlideResult.BadStart, Check( Make( tower, cells, StandardCells.Skip( 1 ) ), tower, cells ) );
		Assert.AreEqual( PoolSlideResult.BadStart, Check( Make( tower, cells, new[] { (10, 12), (10, 13) }, heading: 0 ), tower, cells ), "from the stairs' side" );
		Assert.AreEqual( PoolSlideResult.Overlap, Check( Make( tower, cells, new[] { (8, 11), (8, 10), (9, 10), (9, 11), (8, 11), (8, 10), (8, 9), (9, 9), (10, 9), (11, 9), (11, 10) } ), tower, cells ), "the same cell twice" );
		Assert.AreEqual( PoolSlideResult.Occupied, Check( good, tower, cells, Site( cells, reserved: ( x, y ) => (x, y) == (9, 9) ) ), "a cell the host keeps" );
		cells.SetType( 10, 9, ParkCellType.Path );
		Assert.AreEqual( PoolSlideResult.Occupied, Check( good, tower, cells ), "a path under the track" );
		cells.SetType( 10, 9, ParkCellType.Empty );
		Assert.AreEqual( PoolSlideResult.Ok, Check( good, tower, cells ) );
		cells.SetFlags( 8, 10, ParkCellFlags.Unowned );
		Assert.AreEqual( PoolSlideResult.OutsideLand, Check( good, tower, cells ), "land the park does not own" );
		cells.SetFlags( 8, 10, default );

		// the end must be over water, in the water the tower faces
		var island = Fixture();
		island.Cells.SetType( 3, 3, ParkCellTypes.PoolWater );
		var elsewhere = new PoolSlide( 8, 12, PoolSlideProfile.Flume, 8, 11, 3, new[] { PoolSlidePiece.Straight, PoolSlidePiece.Straight, PoolSlidePiece.Left, PoolSlidePiece.Straight, PoolSlidePiece.Left, PoolSlidePiece.Straight, PoolSlidePiece.Splash }, good.TopHeight, good.EndHeight );
		Assert.AreEqual( PoolSlideResult.NotOverPool, Check( elsewhere, island.Tower, island.Cells ), "a splash cell that is not the tower's pool" );
		island.Cells.SetType( 8, 6, ParkCellTypes.PoolWater );
		var other = new PoolSlide( 8, 12, PoolSlideProfile.Flume, 8, 11, 3, new[] { PoolSlidePiece.Straight, PoolSlidePiece.Straight, PoolSlidePiece.Straight, PoolSlidePiece.Straight, PoolSlidePiece.Straight, PoolSlidePiece.Splash }, good.TopHeight, good.EndHeight );
		Assert.AreEqual( PoolSlideResult.NotOverPool, Check( other, island.Tower, island.Cells ), "a separate pool is another water" );
	}

	[TestMethod]
	public void TheSlopeKeepsASlideNeitherTooShortNorTooLongAndTheTrackAboveTheGround()
	{
		using var scope = PoolsOn();
		var (cells, tower) = Fixture();
		// too short: three pieces drop five units over 1.6 cells
		var steep = Make( tower, cells, new[] { (8, 14), (9, 14), (10, 14) }, heading: 1 );
		Assert.AreEqual( PoolSlideResult.TooSteep, Check( steep, tower, cells ), $"{steep.SlopePermille}" );
		// too long: twenty pieces round the park, about 17 cells of track for a drop of five units
		var path = new List<(int X, int Y)>();
		for ( var y = 11; y >= 4; y-- )
			path.Add( (8, y) );
		for ( var x = 9; x <= 14; x++ )
			path.Add( (x, 4) );
		for ( var y = 5; y <= 10; y++ )
			path.Add( (14, y) );
		var snake = Make( tower, cells, path );
		Assert.AreEqual( 20, snake.Pieces.Count );
		Assert.AreEqual( PoolSlideResult.TooGentle, Check( snake, tower, cells ), $"{snake.SlopePermille}" );
		// the ground rises under the track: too low
		var hill = Site( cells, shape: ( x, y ) => x is >= 8 and <= 11 && y is >= 8 and <= 10 ? 3.5f : 0f );
		var rebuilt = Make( tower, cells, StandardCells, site: hill );
		Assert.AreEqual( PoolSlideResult.TooLow, Check( rebuilt, tower, cells, hill ), "the hill is nearly as high as the track" );
		// the recorded heights must be the tower's top and the pool's water
		var good = Make( tower, cells, StandardCells );
		Assert.AreEqual( PoolSlideResult.BadHeights, Check( good.With( good.Pieces, good.TopHeight + 1, good.EndHeight ), tower, cells ) );
		Assert.AreEqual( PoolSlideResult.BadHeights, Check( good.With( good.Pieces, good.TopHeight, good.EndHeight - 500 ), tower, cells ), "an end below the water" );
		Assert.AreEqual( PoolSlideResult.BadHeights, Check( good.With( good.Pieces, good.TopHeight, good.EndHeight + 2000 ), tower, cells ), "an end far above it" );
	}

	[TestMethod]
	public void TheRulesAreOffWithTheExtensionOffAndEveryOtherTrackKeepsItsClosedCircuitRule()
	{
		var (cells, tower) = Fixture();
		var good = Make( tower, cells, StandardCells );
		using ( OpenTPW.Mods.OpenTpwExtensions.Use( PoolsTests.PoolsSet( false ) ) )
			Assert.AreEqual( PoolSlideResult.ExtensionOff, Check( good, tower, cells ) );
		using var scope = PoolsOn();
		Assert.AreEqual( PoolSlideResult.Ok, Check( good, tower, cells ) );
		// a slide writes nothing to the cell map and nothing to the track layer: the closed-circuit rule and the layer are the rides'
		var before = CellDigest( cells );
		var world = World( cells, tower );
		Build( world, tower, StandardCells );
		Assert.AreEqual( before, CellDigest( cells ), "the slide's cells are not written to the cell map" );
		Assert.IsTrue( StandardCells.All( cell => cells.Tracks.Get( cell.X, cell.Y ).Type == 0 ), "and no track tile is laid: the closed-circuit walk of the kart and water tracks never sees a slide" );
	}

	// ---- The builder ------------------------------------------------------------------------------------------

	[TestMethod]
	public void ACellAtATimeBuildsTheSlideTurnsThePreviousPieceAndAPoolCellEndsItWithTheSplash()
	{
		using var scope = PoolsOn();
		var (cells, tower) = Fixture();
		var world = World( cells, tower );
		Assert.AreEqual( PoolSlideResult.BadStart, world.AddCell( 8, 12, 5, 5 ), "the first cell must be one of the tower's start cells" );
		Assert.AreEqual( PoolSlideResult.BadStart, world.AddCell( 8, 12, 10, 12 ), "not on the stairs' side" );
		Assert.AreEqual( PoolSlideResult.Ok, world.AddCell( 8, 12, 8, 11 ) );
		Assert.AreEqual( PoolSlidePiece.Straight, world.Find( tower.Id )!.Pieces[0], "the end is a straight piece until the next cell turns it" );
		Assert.AreEqual( PoolSlideResult.NotNext, world.AddCell( 8, 12, 8, 12 ), "behind the end is the tower" );
		Assert.AreEqual( PoolSlideResult.NotNext, world.AddCell( 8, 12, 9, 10 ), "a diagonal is no neighbour" );
		Assert.AreEqual( PoolSlideResult.NotNext, world.AddCell( 8, 12, 8, 8 ), "nor a cell two away" );
		Assert.AreEqual( PoolSlideResult.Ok, world.AddCell( 8, 12, 9, 11 ), "a turn to the east" );
		Assert.AreEqual( PoolSlidePiece.Right, world.Find( tower.Id )!.Pieces[0], "the first piece became a right turn" );
		Assert.AreEqual( PoolSlideResult.NotNext, world.AddCell( 8, 12, 8, 11 ), "back the way it came" );
		world.RemoveLastPiece( 8, 12 );
		Assert.AreEqual( PoolSlidePiece.Straight, world.Find( tower.Id )!.Pieces[0] );
		world.RemoveLastPiece( 8, 12 );
		Assert.IsNull( world.Find( tower.Id ), "taking back the only piece takes the slide away" );
		Assert.IsFalse( world.RemoveLastPiece( 8, 12 ) );
		Build( world, tower, StandardCells );
		var slide = world.Find( tower.Id )!;
		Assert.IsTrue( slide.IsComplete && world.IsUsable( slide ) );
		Assert.AreEqual( PoolSlideResult.NoSplash, world.AddCell( 8, 12, 12, 10 ), "nothing is added after the splash" );
		Assert.AreEqual( PoolSlideResult.NoTower, world.AddCell( 30, 30, 1, 1 ) );
	}

	[TestMethod]
	public void EveryPieceIsChargedAndTakingItBackOrDemolishingTheTowerRefundsIt()
	{
		using var scope = PoolsOn();
		var (cells, tower) = Fixture();
		var economy = EconomyTestData.Park();
		var world = World( cells, tower, economy );
		var start = economy.Balance;
		Build( world, tower, StandardCells );
		Assert.AreEqual( start - 7 * ParkEconomy.SlidePieceCost, economy.Balance, "seven pieces at 90" );
		world.RemoveLastPiece( 8, 12 );
		Assert.AreEqual( start - 6 * ParkEconomy.SlidePieceCost, economy.Balance, "refunded in full" );
		Build( world, tower, new[] { StandardCells[6] } );
		Assert.IsTrue( world.Remove( 8, 12 ) );
		Assert.AreEqual( start, economy.Balance, "the demolished tower's pieces come back" );
		Assert.AreEqual( 0, world.Count );
		Assert.IsFalse( world.Remove( 8, 12 ) );
	}

	[TestMethod]
	public void ASlideThatTheParkCannotPayForIsRefusedPieceByPiece()
	{
		using var scope = PoolsOn();
		var (cells, tower) = Fixture();
		var economy = EconomyTestData.Park( initialCash: 200 );
		var world = World( cells, tower, economy );
		Assert.AreEqual( PoolSlideResult.Ok, world.AddCell( 8, 12, 8, 11 ) );
		Assert.AreEqual( PoolSlideResult.Ok, world.AddCell( 8, 12, 8, 10 ) );
		Assert.AreEqual( PoolSlideResult.NotEnoughMoney, world.AddCell( 8, 12, 8, 9 ) );
		Assert.AreEqual( 2, world.Find( tower.Id )!.Pieces.Count );
	}

	[TestMethod]
	public void TheTrackKeepsItsLandCellsForThePylonsButNotTheWaterUnderIt()
	{
		using var scope = PoolsOn();
		var slide = StandardSlide( out var cells, out _, out var world );
		foreach ( var (x, y) in slide.Cells )
			Assert.AreEqual( cells.RawTypeAt( x, y ) != ParkCellTypes.PoolWater, world.ReservesLand( x, y, cells ), $"({x}, {y})" );
		Assert.IsFalse( world.ReservesLand( 5, 5, cells ) );
		Assert.AreEqual( slide, world.SlideAt( 8, 9 ) );
		Assert.IsNull( world.SlideAt( 5, 5 ) );
	}

	[TestMethod]
	public void ASlideStopsBeingUsableWhenItsPoolIsErasedAndComesBackWithTheWater()
	{
		using var scope = PoolsOn();
		var slide = StandardSlide( out var cells, out _, out var world );
		Assert.IsTrue( world.IsUsable( slide ) );
		cells.SetType( 11, 10, ParkCellType.Empty );
		Assert.IsFalse( world.IsUsable( slide ), "no water under the splash" );
		cells.SetType( 11, 10, ParkCellTypes.PoolWater );
		Assert.IsTrue( world.IsUsable( slide ) );
		cells.SetType( 10, 12, ParkCellType.Empty );
		cells.SetType( 10, 13, ParkCellType.Empty );
		Assert.IsFalse( world.IsUsable( slide ), "no water at the foot of the stairs" );
	}

	// ---- The ride ------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheRideRunsByGravityAtASpeedFromTheSlopeAndTheTableIsRepeatable()
	{
		using var scope = PoolsOn();
		var slide = StandardSlide( out var cells, out var tower, out _ );
		var previous = -1;
		for ( var turn = 0; turn <= slide.RideTurns + 3; turn++ )
		{
			var position = slide.PositionAtTurn( turn );
			Assert.IsTrue( position >= previous, $"turn {turn}" );
			previous = position;
		}
		Assert.AreEqual( slide.Length, slide.PositionAtTurn( slide.RideTurns ) );
		Assert.AreEqual( 0, slide.PositionAtTurn( 0 ) );
		Assert.IsTrue( slide.RideTurns is > 10 and < 60, $"{slide.RideTurns} turns, about {slide.RideTurns * 0.248:F1} seconds" );
		// the same slide built twice rides the same way
		var again = Make( tower, cells, StandardCells );
		Assert.IsTrue( Enumerable.Range( 0, 80 ).All( turn => slide.PositionAtTurn( turn ) == again.PositionAtTurn( turn ) ) );
		// a steeper slide of the same length is faster, a gentler one slower
		var steeper = slide.With( slide.Pieces, slide.TopHeight + 3000, slide.EndHeight );
		var gentler = slide.With( slide.Pieces, slide.TopHeight - 1500, slide.EndHeight );
		Assert.IsTrue( steeper.RideTurns < slide.RideTurns && slide.RideTurns < gentler.RideTurns, $"{steeper.RideTurns} < {slide.RideTurns} < {gentler.RideTurns}" );
		// the speed settles at about eight times the pull: slope 405 thousandths pull 40, so about 320 (a third of a cell a turn)
		var late = slide.PositionAtTurn( slide.RideTurns - 3 ) - slide.PositionAtTurn( slide.RideTurns - 4 );
		Assert.AreEqual( 8 * slide.SlopePermille * PoolSlideSettings.Acceleration / 1000, late, 40 );
	}

	// ---- Saves and the hash -----------------------------------------------------------------------------------------

	[TestMethod]
	public void TheSlidesRoundTripThroughTheSaveAndTheHashCoversThemOnlyWhenThereAreAny()
	{
		using var scope = PoolsOn();
		var (cells, tower) = Fixture();
		var world = World( cells, tower );
		Assert.IsNull( world.CaptureSnapshot(), "a park without slides saves no part" );
		var empty = new StateHasher();
		world.AddCanonicalState( empty );
		Assert.AreEqual( new StateHasher().Value, empty.Value, "and hashes nothing" );
		Build( world, tower, StandardCells.Take( 4 ) );
		var open = Encoding.UTF8.GetString( ParkResumeJson.Serialize( world.CaptureSnapshot() ) );
		Build( world, tower, StandardCells.Skip( 4 ) );
		var json = Encoding.UTF8.GetString( ParkResumeJson.Serialize( world.CaptureSnapshot() ) );
		Assert.AreNotEqual( open, json );
		var back = ParkResumeJson.Deserialize<PoolSlidesSaveData>( Encoding.UTF8.GetBytes( json ) );
		Assert.AreEqual( json, Encoding.UTF8.GetString( ParkResumeJson.Serialize( back ) ), "the JSON round-trips" );
		var restored = World( cells, tower );
		restored.RestoreSnapshot( back );
		Assert.AreEqual( json, Encoding.UTF8.GetString( ParkResumeJson.Serialize( restored.CaptureSnapshot() ) ) );
		var one = new StateHasher();
		var two = new StateHasher();
		world.AddCanonicalState( one );
		restored.AddCanonicalState( two );
		Assert.AreEqual( one.Value, two.Value );
		Assert.AreNotEqual( new StateHasher().Value, one.Value );
		// an unfinished slide saves and restores too
		var partial = World( cells, tower );
		partial.RestoreSnapshot( ParkResumeJson.Deserialize<PoolSlidesSaveData>( Encoding.UTF8.GetBytes( open ) ) );
		Assert.AreEqual( 4, partial.Find( tower.Id )!.Pieces.Count );
		Assert.IsFalse( partial.Find( tower.Id )!.IsComplete );
	}

	[TestMethod]
	public void ARestoreRefusesASlideTheRulesWouldNotHaveBuilt()
	{
		using var scope = PoolsOn();
		var good = StandardSlide( out var cells, out var tower, out var world );
		var saved = world.CaptureSnapshot()!;
		var item = saved.Slides[0];
		void Refuses( PoolSlidesSaveData data, string why )
		{
			var target = World( cells, tower );
			Assert.ThrowsException<System.IO.InvalidDataException>( () => target.RestoreSnapshot( data ), why );
		}
		Refuses( saved with { Slides = new[] { item with { Pieces = new[] { 0, 9 } } } }, "a piece that does not exist" );
		Refuses( saved with { Slides = new[] { item with { Pieces = Array.Empty<int>() } } }, "no pieces" );
		Refuses( saved with { Slides = new[] { item with { StartHeading = 7 } } }, "a heading" );
		Refuses( saved with { Slides = new[] { item with { Profile = 5 } } }, "a profile" );
		Refuses( saved with { Slides = new[] { item with { TowerX = 3, TowerY = 3 } } }, "a tower that is not there" );
		Refuses( saved with { Slides = new[] { item with { StartX = 5, StartY = 5 } } }, "a start inside the pool" );
		Refuses( saved with { Slides = new[] { item, item } }, "the same slide twice" );
		Refuses( saved with { Slides = new[] { item with { TopHeight = item.TopHeight + 1 } } }, "heights that are not the tower's" );
		cells.SetType( 8, 10, ParkCellType.Path );
		Refuses( saved, "a path under the track" );
		Assert.IsNotNull( good );
	}

	[TestMethod]
	public void TwoSlidesNeverShareACell()
	{
		using var scope = PoolsOn();
		var (cells, tower) = Fixture();
		// a second tower north of the first slide: its pool-side cell is given directly, as the fixture's pool is what the rules need; its start cell (8, 9) is a cell of the first slide
		var second = new PoolSlideTower( 8, 7, new[] { (8, 7), (9, 7), (8, 8), (9, 8) }, new[] { (10, 10) }, PoolSlideProfile.Tube, 4 );
		PoolSlideTower? Lookup( int id ) => id == tower.Id ? tower : id == second.Id ? second : null;
		var world = new PoolSlideWorld( () => Site( cells ), Lookup, () => null );
		Build( world, tower, StandardCells );
		Assert.AreEqual( PoolSlideResult.Occupied, world.AddCell( 8, 7, 8, 9 ), "a cell of the other slide" );
		Assert.AreEqual( PoolSlideResult.Ok, world.AddCell( 8, 7, 7, 8 ), "control: a free start cell" );
		Assert.AreEqual( 2, world.Count );
		// a save that holds two slides over one cell is refused
		var saved = world.CaptureSnapshot()!;
		var forged = saved with { Slides = saved.Slides.Select( item => item.TowerX == 8 && item.TowerY == 7 ? item with { StartX = 8, StartY = 9, StartHeading = 1 } : item ).ToArray() };
		Assert.ThrowsException<System.IO.InvalidDataException>( () => new PoolSlideWorld( () => Site( cells ), Lookup, () => null ).RestoreSnapshot( forged ) );
	}

	[TestMethod]
	public void ASaveLoadsWhateverTheSlidesPoolHasBecomeAndTheSlideIsMerelyUnusableUntilItsWaterIsBack()
	{
		using var scope = PoolsOn();
		var slide = StandardSlide( out var cells, out var tower, out var world );
		var saved = world.CaptureSnapshot()!;
		PoolSlideWorld Fresh( ParkCellMap map, float level = -0.12f ) =>
			new( () => new PoolSlideSite { Cells = map, Ground = ( _, _ ) => 0f, WaterLevel = ( x, y ) => map.RawTypeAt( x, y ) == ParkCellTypes.PoolWater ? level : null }, id => id == tower.Id ? tower : null, () => null );
		// the splash cell drained, the foot of the stairs drained, the pool split, the water level moved out of the window: each loads, none is usable
		foreach ( var (name, edit) in new (string, Action<ParkCellMap>)[]
		{
			("splash drained", map => map.SetType( 11, 10, ParkCellType.Empty )),
			("stairs drained", map => { map.SetType( 10, 12, ParkCellType.Empty ); map.SetType( 10, 13, ParkCellType.Empty ); }),
			("split", map => { for ( var x = 10; x < 20; x++ ) map.SetType( x, 11, ParkCellType.Empty ); })
		} )
		{
			var map = new ParkCellMap( 40, 40 );
			for ( var y = 10; y < 20; y++ )
				for ( var x = 10; x < 20; x++ )
					map.SetType( x, y, ParkCellTypes.PoolWater );
			edit( map );
			var restored = Fresh( map );
			restored.RestoreSnapshot( saved );
			Assert.AreEqual( 1, restored.Count, name );
			Assert.IsFalse( restored.IsUsable( restored.Slides.Single() ), name );
		}
		var lowered = new ParkCellMap( 40, 40 );
		for ( var y = 10; y < 20; y++ )
			for ( var x = 10; x < 20; x++ )
				lowered.SetType( x, y, ParkCellTypes.PoolWater );
		var deep = Fresh( lowered, -3f );
		deep.RestoreSnapshot( saved );
		Assert.IsFalse( deep.IsUsable( deep.Slides.Single() ), "a water level far below the track's end" );
		var level = Fresh( lowered );
		level.RestoreSnapshot( saved );
		Assert.IsTrue( level.IsUsable( level.Slides.Single() ), "control: the park as it was" );
		Assert.IsNotNull( slide );
		// structure still refuses: another profile than the tower's
		Assert.ThrowsException<System.IO.InvalidDataException>( () => Fresh( lowered ).RestoreSnapshot( saved with { Slides = new[] { saved.Slides[0] with { Profile = 1 } } } ) );
	}

	[TestMethod]
	public void AGuestOnASlideIsCheckedAgainstTheRestoredSlidesAndWhatNeedsAFinishedSlideRefusesAnUnfinishedOne()
	{
		using var scope = PoolsOn();
		var (cells, tower) = Fixture();
		var thing = tower with { ThingId = 700 };
		var world = new PoolSlideWorld( () => Site( cells ), id => id == tower.Id ? thing : null, () => null );
		Build( world, tower, StandardCells.Take( 4 ) );
		var guests = new GuestSimulation( new GuestPathGrid( 40, 40 ), GuestTests.CreateSettings(), 1 );
		guests.Grid.SetPath( 1, 1, true );
		var guest = guests.SpawnInPark( 1, 1 );
		void Set( int phase, int target = 700 ) => (guest.State, guest.PoolActivityId, guest.PoolActivityTarget, guest.PoolActivityPhase) = (GuestState.PoolActivity, PoolSlideActivity.ActivityId, target, phase);
		Set( PoolSlidePhases.Wait );
		world.ValidateGuests( guests.Guests );
		Set( PoolSlidePhases.Ride );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => world.ValidateGuests( guests.Guests ), "riding an unfinished slide" );
		Set( PoolSlidePhases.Splash );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => world.ValidateGuests( guests.Guests ) );
		Set( 9 );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => world.ValidateGuests( guests.Guests ), "a phase that does not exist" );
		Set( PoolSlidePhases.Climb, target: 0 );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => world.ValidateGuests( guests.Guests ), "a slide that is not there" );
		Build( world, tower, StandardCells.Skip( 4 ) );
		Set( PoolSlidePhases.Ride );
		world.ValidateGuests( guests.Guests );
		guest.State = GuestState.DoingNothing;
		(guest.PoolActivityId, guest.PoolActivityTarget, guest.PoolActivityPhase) = (null, 0, 0);
		world.ValidateGuests( guests.Guests );
	}

	// ---- The tool -----------------------------------------------------------------------------------------------------

	private static PoolTool ToolFor( ParkCellMap cells, PoolSlideWorld world )
	{
		var tool = PoolToolRig.Create( new PoolBuilder( cells, () => null, new PoolsTests.FlatGrid( cells.Width, cells.Height ) ) );
		tool.Slides = world;
		return tool;
	}

	[TestMethod]
	public void TheSlideModeOfThePoolToolLaysTheTrackByPressingAndDraggingAndEndsWithTheSplash()
	{
		using var scope = PoolsOn();
		var (cells, tower) = Fixture();
		var world = World( cells, tower );
		var tool = ToolFor( cells, world );
		var ended = 0;
		tool.Ended += () => ended++;
		Assert.IsFalse( tool.BeginSlide( 30, 30 ), "no tower there" );
		Assert.IsTrue( tool.BeginSlide( 8, 12 ) );
		Assert.IsTrue( tool.IsActive && tool.IsSlide && !tool.IsErasing );
		tool.Hover( 8, 11 );
		Assert.AreEqual( PoolBuildResult.Ok, tool.HoverResult, "a start cell" );
		tool.Hover( 5, 5 );
		Assert.AreEqual( PoolBuildResult.SlideRefused, tool.HoverResult );
		Assert.AreEqual( PoolSlideResult.BadStart, tool.HoverSlideResult );
		// press on the first cell, drag along the track to the pool: a line of cells, each the next piece
		Assert.AreEqual( PoolBuildResult.Ok, tool.Press( 8, 11 ) );
		tool.Hover( 8, 9 );
		tool.Hover( 11, 9 );
		Assert.AreEqual( 6, world.Find( tower.Id )!.Pieces.Count, "the drag laid the cells it crossed" );
		tool.Hover( 11, 10 );
		var stroke = tool.Release();
		Assert.IsNotNull( stroke );
		Assert.AreEqual( 7, stroke!.Cells.Count );
		Assert.IsTrue( tool.SlideCompleted && !tool.IsActive, "a finished slide ends the slide mode" );
		Assert.AreEqual( 1, ended );
		Assert.IsTrue( world.Find( tower.Id )!.IsComplete );
		Assert.IsFalse( tool.BeginSlide( 8, 12 ), "a finished slide is not laid again" );
	}

	[TestMethod]
	public void TheSlideModeRefusesWithAReasonTakesBackAPieceAtATimeAndCancelsCleanly()
	{
		using var scope = PoolsOn();
		var (cells, tower) = Fixture();
		var world = World( cells, tower );
		var tool = ToolFor( cells, world );
		Assert.IsTrue( tool.BeginSlide( 8, 12 ) );
		tool.Press( 8, 11 );
		tool.Release();
		Assert.AreEqual( PoolBuildResult.SlideRefused, tool.Press( 20, 20 ) );
		Assert.AreEqual( PoolSlideResult.NotNext, tool.LastSlideRefusal, "a cell away from the end" );
		Assert.AreEqual( ((20, 20), PoolBuildResult.SlideRefused), tool.LastRefusal );
		tool.Release();
		// a short run into the pool is too steep: refused with the reason, the track stays as it is
		var steep = ToolFor( cells, World( cells, tower ) );
		Assert.IsTrue( steep.BeginSlide( 8, 12 ) );
		steep.Press( 8, 14 );
		steep.Hover( 9, 14 );
		steep.Hover( 10, 14 );
		steep.Release();
		Assert.AreEqual( PoolSlideResult.TooSteep, steep.LastSlideRefusal );
		Assert.IsTrue( steep.IsActive, "the tool stays on" );
		Assert.IsTrue( steep.Slides!.Find( tower.Id )!.Pieces.Count == 2 );
		Assert.IsTrue( steep.Undo() );
		Assert.AreEqual( 1, steep.Slides.Find( tower.Id )!.Pieces.Count, "Backspace takes back a piece" );
		Assert.IsTrue( steep.Undo() );
		Assert.IsNull( steep.Slides.Find( tower.Id ), "the last piece takes the slide away" );
		Assert.IsFalse( steep.Undo() );
		steep.Cancel();
		Assert.IsFalse( steep.IsActive || steep.IsSlide );
		// the paint and erase modes are untouched
		Assert.IsTrue( steep.Begin( erase: false ) );
		Assert.IsFalse( steep.IsSlide );
	}

	[TestMethod]
	public void DraggingPastTheSplashEndsTheSlideCleanlyWithNoRefusalAndSwitchingModeKeepsThePoolSafe()
	{
		using var scope = PoolsOn();
		var (cells, tower) = Fixture();
		var world = World( cells, tower );
		var tool = ToolFor( cells, world );
		Assert.IsTrue( tool.BeginSlide( 8, 12 ) );
		tool.Press( 8, 11 );
		tool.Hover( 8, 9 );
		tool.Hover( 11, 9 );
		// one drag runs through the pool cell (11, 10) that ends the slide and on through (11, 11) and (11, 12)
		tool.Hover( 11, 13 );
		var stroke = tool.Release();
		Assert.IsNotNull( stroke );
		Assert.IsTrue( tool.SlideCompleted );
		Assert.IsNull( tool.LastRefusal, "the cells past the splash are not a refusal" );
		Assert.IsNull( tool.LastSlideRefusal );
		Assert.AreEqual( 7, world.Find( tower.Id )!.Pieces.Count );
		// an unfinished slide's stroke is not a pool stroke: switching to the paint mode and pressing Backspace takes no water away
		var other = ToolFor( cells, World( cells, tower ) );
		Assert.IsTrue( other.BeginSlide( 8, 12 ) );
		other.Press( 8, 11 );
		other.Hover( 8, 10 );
		other.Release();
		var water = cells.CountOf( ParkCellType.PoolWater );
		Assert.IsTrue( other.Begin( erase: false ) );
		Assert.IsFalse( other.Undo(), "nothing for the pool tool to take back" );
		Assert.AreEqual( water, cells.CountOf( ParkCellType.PoolWater ) );
	}

	[TestMethod]
	public void TheSlideModeNeedsTheExtensionAndMoney()
	{
		var (cells, tower) = Fixture();
		var world = World( cells, tower );
		var tool = ToolFor( cells, world );
		using ( OpenTPW.Mods.OpenTpwExtensions.Use( PoolsTests.PoolsSet( false ) ) )
			Assert.IsFalse( tool.BeginSlide( 8, 12 ), "the extension is off" );
		using var scope = PoolsOn();
		var poor = EconomyTestData.Park( initialCash: 100 );
		var broke = new PoolSlideWorld( () => Site( cells ), id => id == tower.Id ? tower : null, () => poor );
		var second = ToolFor( cells, broke );
		Assert.IsTrue( second.BeginSlide( 8, 12 ) );
		second.Hover( 8, 11 );
		Assert.AreEqual( PoolBuildResult.Ok, second.HoverResult );
		second.Press( 8, 11 );
		second.Release();
		second.Hover( 8, 10 );
		Assert.AreEqual( PoolBuildResult.NotEnoughMoney, second.HoverResult, "10 left, a piece costs 90" );
		Assert.AreEqual( PoolBuildResult.NotEnoughMoney, second.Press( 8, 10 ) );
	}

	[TestMethod]
	public void TheRouteSearchFindsTheShortestValidSlideAndTheWorldBuildsIt()
	{
		var (cells, tower) = Fixture();
		using ( OpenTPW.Mods.OpenTpwExtensions.Use( PoolsTests.PoolsSet( false ) ) )
			Assert.IsNull( PoolSlideRoutes.Find( tower, Site( cells ) ), "the extension is off" );
		using var scope = PoolsOn();
		var route = PoolSlideRoutes.Find( tower, Site( cells ) );
		Assert.IsNotNull( route );
		var world = World( cells, tower );
		Build( world, tower, route! );
		var slide = world.Find( tower.Id )!;
		Assert.IsTrue( slide.IsComplete && world.IsUsable( slide ), $"{route.Count} pieces, slope {slide.SlopePermille}" );
		Assert.IsTrue( route.Count is >= 3 and <= 8, $"{route.Count}" );
		// a tower with no pool beside it has no route; a pool too far has none within the piece limit
		cells.SetType( 10, 12, ParkCellType.Empty );
		cells.SetType( 10, 13, ParkCellType.Empty );
		Assert.IsNull( PoolSlideRoutes.Find( tower, Site( cells ) ) );
	}

	private static ulong CellDigest( ParkCellMap cells )
	{
		var hash = new StateHasher();
		cells.AddCanonicalState( hash );
		return hash.Value;
	}
}
