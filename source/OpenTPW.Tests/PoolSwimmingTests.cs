using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The pure part of the swim session (docs/POOLS.md, "Swimming"): the water of a pool complex, the integer headings and the steering of a swimmer, with no guests and no simulation.
/// </summary>
[TestClass]
public class PoolSwimmingTests
{
	private static ParkCellMap Pool( int width, int height, params string[] rows )
	{
		var cells = new ParkCellMap( width, height );
		for ( var y = 0; y < rows.Length; y++ )
			for ( var x = 0; x < rows[y].Length; x++ )
				if ( rows[y][x] == '#' )
					cells.SetType( x, y, ParkCellTypes.PoolWater );
		return cells;
	}

	private static PoolSwimRegion Region( ParkCellMap cells, (int X, int Y) access ) => PoolSwimRegion.Build( cells, access ) ?? throw new AssertFailedException( "no region" );

	private static int Fixed( double cells ) => (int)Math.Round( cells * Fixed16.One );

	// ---- Headings ----------------------------------------------------------------------------------

	[TestMethod]
	public void TheSixtyFourHeadingsAreUnitVectorsAroundTheCircle()
	{
		for ( var heading = 0; heading < PoolSwimSettings.Headings; heading++ )
		{
			var (x, y) = PoolSwimming.Direction( heading );
			var length = Math.Sqrt( (double)x * x + (double)y * y ) / Fixed16.One;
			Assert.AreEqual( 1.0, length, 1e-3, $"heading {heading}" );
			var angle = heading * 2 * Math.PI / PoolSwimSettings.Headings;
			Assert.AreEqual( Math.Cos( angle ), x / (double)Fixed16.One, 1e-4 );
			Assert.AreEqual( Math.Sin( angle ), y / (double)Fixed16.One, 1e-4 );
			Assert.AreEqual( heading, PoolSwimming.Closest( x, y ), $"the closest heading of heading {heading}'s own vector" );
		}
		Assert.AreEqual( (Fixed16.One, 0), PoolSwimming.Direction( 0 ), "0 is east" );
		Assert.AreEqual( 16, PoolSwimming.Closest( 0, 5 ), "16 is south, the cell map's growing y" );
		Assert.AreEqual( -3, PoolSwimming.Difference( 1, 62 ) );
		Assert.AreEqual( 3, PoolSwimming.Difference( 62, 1 ) );
	}

	[TestMethod]
	public void TheSwimmersRandomStreamIsAFixedSequenceThatNeverStalls()
	{
		uint a = 12345, b = 12345;
		var first = Enumerable.Range( 0, 5 ).Select( _ => PoolSwimming.Next( ref a ) ).ToArray();
		var again = Enumerable.Range( 0, 5 ).Select( _ => PoolSwimming.Next( ref b ) ).ToArray();
		CollectionAssert.AreEqual( first, again );
		uint zero = 0;
		Assert.AreNotEqual( 0u, PoolSwimming.Next( ref zero ), "a zero state is lifted, never stuck" );
		// pinned values: a change of the generator changes every swimmer, so it is a deliberate act
		uint pinned = 1;
		CollectionAssert.AreEqual( new uint[] { 270369, 67634689, 2647435461 }, new[] { PoolSwimming.Next( ref pinned ), PoolSwimming.Next( ref pinned ), PoolSwimming.Next( ref pinned ) } );
	}

	// ---- The water -----------------------------------------------------------------------------------

	[TestMethod]
	public void TheRegionIsTheFourConnectedPoolCellsOfTheAccessCell()
	{
		var cells = Pool( 12, 8, "............", ".###..##....", ".###..##....", "....#.......", "............" );
		var region = Region( cells, (1, 1) );
		Assert.AreEqual( 6, region.Cells.Count, "the 3 x 2 block; the diagonal cell and the other pool are not connected" );
		Assert.IsTrue( region.Contains( 3, 2 ) );
		Assert.IsFalse( region.Contains( 4, 3 ), "touching at a corner is not connected" );
		Assert.IsFalse( region.Contains( 6, 1 ) );
		CollectionAssert.AreEqual( region.Cells.OrderBy( cell => cell.Y ).ThenBy( cell => cell.X ).ToArray(), region.Cells.ToArray(), "row by row" );
		Assert.IsNull( PoolSwimRegion.Build( cells, (0, 0) ), "an access cell that is no pool cell has no region" );
		Assert.IsNull( PoolSwimRegion.Build( cells, (99, 99) ), "nor one outside the map" );
	}

	[TestMethod]
	public void TheRimAQuarterCellWideAndTheBodyKeepSwimmersOffTheCoping()
	{
		var lone = Region( Pool( 5, 5, ".....", ".....", "..#..", ".....", "....." ), (2, 2) );
		Assert.IsTrue( lone.IsWater( Fixed( 2.5 ), Fixed( 2.5 ) ), "the middle of a lone cell" );
		Assert.IsFalse( lone.IsWater( Fixed( 2.3 ), Fixed( 2.5 ) ), "inside the rim of 0.25 and the body of 0.12 more" );
		Assert.IsTrue( lone.IsWater( Fixed( 2.4 ), Fixed( 2.5 ) ) );
		Assert.IsFalse( lone.IsWater( Fixed( 1.9 ), Fixed( 2.5 ) ), "outside the cell" );
		var pool = Region( Pool( 6, 6, "......", ".##...", ".##...", "......", "......", "......" ), (1, 1) );
		Assert.IsTrue( pool.IsWater( Fixed( 2.0 ), Fixed( 2.0 ) ), "the shared corner of four pool cells has no rim" );
		Assert.IsTrue( pool.IsWater( Fixed( 1.99 ), Fixed( 1.5 ) ), "across a shared edge" );
		Assert.IsFalse( pool.IsWater( Fixed( 1.2 ), Fixed( 1.5 ) ), "the outer edge has its rim" );
		// an inner corner (an L): the corner square of the empty diagonal is coping
		var corner = Region( Pool( 6, 6, "......", ".##...", ".#....", "......", "......", "......" ), (1, 1) );
		Assert.IsFalse( corner.IsWater( Fixed( 1.95 ), Fixed( 1.95 ) ), "the square corner of the inner corner at (2, 2)" );
		Assert.IsTrue( corner.IsWater( Fixed( 1.5 ), Fixed( 1.5 ) ) );
	}

	// ---- Steering --------------------------------------------------------------------------------------

	private static PoolSwimBody Start( PoolSwimRegion region, int index, uint random ) => new()
	{
		X = PoolSwimRegion.Centre( region.Cells[index % region.Cells.Count] ).X, Y = PoolSwimRegion.Centre( region.Cells[index % region.Cells.Count] ).Y, Heading = (index * 11) & 63, Random = random + (uint)index * 7919
	};

	private static IReadOnlyList<PoolSwimRegion> Shapes() => new[]
	{
		Region( Pool( 14, 10, "..............", ".######.......", ".######.......", ".######.......", ".######.......", "..............", "..............", "..............", "..............", ".............." ), (1, 1) ),
		Region( Pool( 14, 10, "..............", ".####.........", ".####.........", ".####.........", ".#############", ".#############", "..............", "..............", "..............", ".............." ), (1, 1) ),
		Region( Pool( 14, 10, "..............", ".#.#.#.#......", ".#########....", "..............", "..............", "..............", "..............", "..............", "..............", ".............." ), (1, 2) ),
		Region( Pool( 14, 10, "..............", "..............", ".#............", "..............", "..............", "..............", "..............", "..............", "..............", ".............." ), (1, 2) )
	};

	[TestMethod]
	public void SwimmersKeepToTheWaterOfAnyShapeForThousandsOfTurns()
	{
		foreach ( var region in Shapes() )
		{
			var body = Start( region, 0, 99 );
			var moved = 0;
			for ( var turn = 0; turn < 4000; turn++ )
			{
				moved += PoolSwimming.Step( region, ref body, ReadOnlySpan<(int X, int Y)>.Empty, 6000 + (turn % 5) * 1000, ignoreOthers: false ) ? 1 : 0;
				Assert.IsTrue( region.IsWater( body.X, body.Y ), $"turn {turn} at ({body.X / 65536.0:F2}, {body.Y / 65536.0:F2}) in a region of {region.Cells.Count} cells" );
			}
			if ( region.Cells.Count > 1 )
				Assert.IsTrue( moved > 3000, $"it swims on ({moved} of 4000 turns moved, {region.Cells.Count} cells)" );
		}
	}

	[TestMethod]
	public void ASwimmerWandersAcrossTheWholeRegion()
	{
		foreach ( var region in Shapes().Take( 2 ) )
		{
			var body = Start( region, 0, 5 );
			var visited = new HashSet<(int X, int Y)>();
			for ( var turn = 0; turn < 8000; turn++ )
			{
				PoolSwimming.Step( region, ref body, ReadOnlySpan<(int X, int Y)>.Empty, 7000, ignoreOthers: false );
				visited.Add( (body.X >> 16, body.Y >> 16) );
				if ( turn == 2499 )
					Assert.IsTrue( visited.Count >= region.Cells.Count * 9 / 10, $"most of the region within 10 minutes ({visited.Count} of {region.Cells.Count} cells)" );
			}
			CollectionAssert.AreEquivalent( region.Cells.ToArray(), visited.ToArray(), $"a single swimmer reaches every cell of the region within 8000 turns (33 minutes); missing {string.Join( ", ", region.Cells.Except( visited ) )}" );
		}
	}

	[TestMethod]
	public void ASwimmerHeadingForTheRimTurnsAndStaysInside()
	{
		var region = Shapes()[0];
		// right at the east rim, heading east
		var body = new PoolSwimBody { X = Fixed( 6.62 ), Y = Fixed( 2.5 ), Heading = 0, Random = 7 };
		Assert.IsTrue( region.IsWater( body.X, body.Y ) );
		Assert.IsFalse( region.IsWater( body.X + 6000, body.Y ), "the next stroke east would leave the water" );
		var headings = new HashSet<int>();
		for ( var turn = 0; turn < 40; turn++ )
		{
			PoolSwimming.Step( region, ref body, ReadOnlySpan<(int X, int Y)>.Empty, 6000, ignoreOthers: false );
			Assert.IsTrue( region.IsWater( body.X, body.Y ), $"turn {turn}" );
			headings.Add( body.Heading );
		}
		Assert.IsTrue( headings.Count > 1, "it turned away from the rim" );
	}

	[TestMethod]
	public void SwimmersSteerAwayFromEachOtherAndNeverCloseIn()
	{
		var region = Shapes()[0];
		var bodies = Enumerable.Range( 0, 8 ).Select( index => Start( region, index * 5, 31 ) ).ToArray();
		var minimum = double.MaxValue;
		var closePairs = 0;
		for ( var turn = 0; turn < 3000; turn++ )
		{
			for ( var index = 0; index < bodies.Length; index++ )
			{
				var others = bodies.Where( ( _, other ) => other != index ).Select( other => (other.X, other.Y) ).ToArray();
				PoolSwimming.Step( region, ref bodies[index], others, 6500, ignoreOthers: false );
				Assert.IsTrue( region.IsWater( bodies[index].X, bodies[index].Y ) );
			}
			if ( turn < 100 )
				continue;
			for ( var a = 0; a < bodies.Length; a++ )
				for ( var b = a + 1; b < bodies.Length; b++ )
				{
					var distance = Fixed16.Length( bodies[a].X - bodies[b].X, bodies[a].Y - bodies[b].Y ) / (double)Fixed16.One;
					minimum = Math.Min( minimum, distance );
					closePairs += distance < PoolSwimSettings.AvoidRadius / (double)Fixed16.One ? 1 : 0;
				}
		}
		Assert.IsTrue( minimum >= 0.2, $"the closest two swimmers came ({minimum:F2}) never nearer than the minimum distance of 0.25 less a stroke" );
		Assert.IsTrue( closePairs < 8 * 7 / 2 * 2900 / 3, $"swimmers share the water without crowding ({closePairs} close pair-turns of {8 * 7 / 2 * 2900})" );
	}

	[TestMethod]
	public void TwoSwimmersOnOnePointSwimApart()
	{
		var region = Shapes()[0];
		var a = Start( region, 12, 1 );
		var b = a;
		b.Random = 99;
		for ( var turn = 0; turn < 60; turn++ )
		{
			PoolSwimming.Step( region, ref a, new[] { (b.X, b.Y) }, 6000, ignoreOthers: false );
			PoolSwimming.Step( region, ref b, new[] { (a.X, a.Y) }, 6000, ignoreOthers: false );
		}
		Assert.IsTrue( Fixed16.Length( a.X - b.X, a.Y - b.Y ) > PoolSwimSettings.MinimumDistance, "they separated" );
	}

	[TestMethod]
	public void TheSameSeedSwimsTheSamePath()
	{
		var region = Shapes()[1];
		ulong Path( uint seed )
		{
			var body = Start( region, 3, seed );
			var hash = 14695981039346656037UL;
			for ( var turn = 0; turn < 1500; turn++ )
			{
				PoolSwimming.Step( region, ref body, ReadOnlySpan<(int X, int Y)>.Empty, 7000, ignoreOthers: false );
				hash = (hash ^ (uint)body.X) * 1099511628211UL;
				hash = (hash ^ (uint)body.Y) * 1099511628211UL;
			}
			return hash;
		}
		Assert.AreEqual( Path( 4 ), Path( 4 ) );
		Assert.AreNotEqual( Path( 4 ), Path( 5 ), "another stream, another path" );
	}

	[TestMethod]
	public void ASwimmerBoxedInStandsStillAndLooksForAnotherGoal()
	{
		// a lone cell: the water is a 0.26-cell square; at the corner every heading but inward is blocked
		var region = Region( Pool( 5, 5, ".....", ".....", "..#..", ".....", "....." ), (2, 2) );
		var body = new PoolSwimBody { X = Fixed( 2.37 ), Y = Fixed( 2.37 ), Heading = 48, Random = 3 };
		Assert.IsTrue( region.IsWater( body.X, body.Y ) );
		for ( var turn = 0; turn < 200; turn++ )
		{
			PoolSwimming.Step( region, ref body, ReadOnlySpan<(int X, int Y)>.Empty, 9000, ignoreOthers: false );
			Assert.IsTrue( region.IsWater( body.X, body.Y ) );
		}
	}

	[TestMethod]
	public void TheStrokeNamesTheSwimSpriteSlots()
	{
		Assert.AreEqual( 8, PoolSwimming.SpriteSlot( 0 ) );
		Assert.AreEqual( 9, PoolSwimming.SpriteSlot( 1 ) );
		Assert.AreEqual( 10, PoolSwimming.SpriteSlot( 2 ) );
		Assert.AreEqual( 10, PoolSwimming.SpriteSlot( 9 ), "out of range is held to treading water" );
	}
}
