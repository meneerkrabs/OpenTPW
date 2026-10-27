using System;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The tile instances' placement and height (the instancers 0x522B90/0x522D50, 0x463060, 0x466F90, 0x466D70) and the landscape sampler 0x4527F0, asset-free
/// (docs/TRACKS.md; TRACK-001 and TRACK-003 retired).
/// </summary>
[TestClass]
public class TrackInstanceHeightTests
{
	private static TrackRenderer Renderer( Func<int, int, float>? corner = null ) =>
		new( null!, null, () => Vector2.Zero, ( x, y ) => 0, corner );

	[TestMethod]
	public void TheTileTurnsAboutItsCentreAsTheOriginalsCornerPivotWithTwoCellsOfOffset()
	{
		var renderer = Renderer();
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			// the original: engine rotation 360 - rotation (0x522B90), one cell added to the origin cell by it there and once more by 0x466F90
			var engine = (360 - rotation) % 360;
			var (offsetX, offsetY) = engine switch { 90 => (0, 1), 180 => (1, 1), 270 => (1, 0), _ => (0, 0) };
			var (x, y) = (7, 4);
			var instance = new Vector2( (x + 2 * offsetX) * 10, (y + 2 * offsetY) * 10 );
			var matrix = renderer.TileTransform( x, y, rotation );
			var turn = engine * MathF.PI / 180;
			foreach ( var (px, py) in new[] { (0f, 0f), (20f, 0f), (0f, 20f), (20f, 20f), (6f, 13f) } )
			{
				// the engine's rotation is clockwise seen from above
				var original = instance + new Vector2( px * MathF.Cos( turn ) + py * MathF.Sin( turn ), -px * MathF.Sin( turn ) + py * MathF.Cos( turn ) );
				var ours = System.Numerics.Vector3.Transform( new System.Numerics.Vector3( px, py, 0 ), matrix );
				Assert.AreEqual( original.X * ObjectPlacement.ModelScale, ours.X, 1e-3f, $"x of ({px}, {py}) at rotation {rotation}" );
				Assert.AreEqual( original.Y * ObjectPlacement.ModelScale, ours.Y, 1e-3f, $"y of ({px}, {py}) at rotation {rotation}" );
			}
		}
	}

	[TestMethod]
	public void TheMidrangeTakesTheHighestAndTheLowestOfTheTilesNineCorners()
	{
		float Corner( int x, int y ) => (x, y) switch { (3, 3) => 30f, (4, 5) => -10f, (6, 6) => 99f, _ => 4f };
		var renderer = Renderer( Corner );
		Assert.AreEqual( 10f, renderer.TileHeight( 3, 3 ), 1e-4f, "(6, 6) is outside the tile's vertices 3..5: highest 30, lowest -10" );
		Assert.AreEqual( 4f, Renderer( ( x, y ) => 4f ).TileHeight( 0, 0 ), 1e-6f );
	}

	[TestMethod]
	public void ACellWithoutTheNonPlanarFlagIsOnePlane()
	{
		// flat and sloped planar cells: the plane through a, b, c at every fraction
		Assert.AreEqual( 5f, LandscapeSampler.InCell( 5, 5, 5, 5, 0.3f, 0.8f, false, false ), 1e-5f );
		// a: 0, b: 10 (rises 10 along x), c: 20 (rises 20 along z): plane h = 10 fx + 20 fz
		Assert.AreEqual( 10 * 0.25f + 20 * 0.5f, LandscapeSampler.InCell( 0, 10, 20, 30, 0.25f, 0.5f, false, false ), 1e-4f );
	}

	[TestMethod]
	public void ANonPlanarCellTakesTheTriangleOfItsDiagonal()
	{
		// a 0, b 0, c 0, d 10: the main diagonal (x, z)-(x+1, z+1): the lower-right triangle (a, b, d) when fz <= fx, else (a, c, d)
		Assert.AreEqual( 10 * 0.5f, LandscapeSampler.InCell( 0, 0, 0, 10, 0.75f, 0.5f, true, false ), 1e-4f, "plane through a, b, d: h = 10 fz" );
		Assert.AreEqual( 10 * 0.5f, LandscapeSampler.InCell( 0, 0, 0, 10, 0.5f, 0.75f, true, false ), 1e-4f, "plane through a, c, d: h = 10 fx" );
		// the other diagonal (bit 0x4): corners b-c; triangle (b, c, d) when 1 - fz <= fx, else (a, b, c)
		Assert.AreEqual( 10 * 0.5f, LandscapeSampler.InCell( 0, 0, 0, 10, 0.75f, 0.75f, true, true ), 1e-4f, "plane through b, c, d (b = c = 0, d = 10): h = 10 (fx + fz - 1)" );
	}

	[TestMethod]
	public void TheLoadMarksACellNonPlanarWhenItsCornerNormalsDisagree()
	{
		Assert.IsFalse( LandscapeSampler.IsNonPlanar( 3, 3, 3, 3 ), "a flat cell is planar" );
		Assert.IsTrue( LandscapeSampler.IsNonPlanar( 0, 0, 0, 10 ), "one raised corner" );
	}

	[TestMethod]
	public void TheSamplerAnswersZeroOffTheMapAndInterpolatesInside()
	{
		float Corner( int x, int z ) => 2 * x + 3 * z;
		bool Anti( int x, int z ) => false;
		Assert.AreEqual( 0f, LandscapeSampler.Sample( 4, 4, 10, 10, Corner, Anti, -0.1f, 5 ) );
		Assert.AreEqual( 0f, LandscapeSampler.Sample( 4, 4, 10, 10, Corner, Anti, 40, 5 ), "x at the grid's far edge is off the map" );
		Assert.AreEqual( 0f, LandscapeSampler.Sample( 4, 4, 10, 10, Corner, Anti, 5, 40 ) );
		// a tilted plane: height 2 x + 3 z at cell coordinates, 25 / 10 = 2.5, 12 / 10 = 1.2
		Assert.AreEqual( 2 * 2.5f + 3 * 1.2f, LandscapeSampler.Sample( 4, 4, 10, 10, Corner, Anti, 25, 12 ), 1e-3f );
	}
}
