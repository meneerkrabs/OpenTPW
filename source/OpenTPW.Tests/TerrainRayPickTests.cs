using System;
using System.Linq;
using System.Numerics;
using NVector3 = System.Numerics.Vector3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The terrain half of the original's cursor pick (tp.exe 0x45bf90, cell 0x45d560).</summary>
[TestClass]
public class TerrainRayPickTests
{
	/// <summary>A heightfield of 10 x 10 cells (10 MD2 units each) with the given corner heights and cell words.</summary>
	private static ModelHeightfield Field( Func<int, int, float> height, Func<int, int, uint>? word = null, int count = 10 )
	{
		var heights = new float[(count + 1) * (count + 1)];
		for ( var z = 0; z <= count; z++ )
			for ( var x = 0; x <= count; x++ )
				heights[z * (count + 1) + x] = height( x, z );
		var words = new uint[count * count];
		for ( var z = 0; z < count; z++ )
			for ( var x = 0; x < count; x++ )
				words[z * count + x] = word?.Invoke( x, z ) ?? 0;
		return new ModelHeightfield( count, count, 10, 10, 0, 0, Array.Empty<uint>(), heights, words );
	}

	/// <summary>The engine position of MD2 (x, height, z).</summary>
	private static NVector3 Engine( ModelHeightfield field, float x, float y, float z ) => OriginalParkPlacement.ToEngine( field, new NVector3( x, y, z ) );

	[TestMethod]
	public void FlatTerrainIsHitAtItsHeightUnderTheRay()
	{
		var field = Field( ( _, _ ) => 20 );
		var origin = Engine( field, 30, 120, 10 );
		var direction = Engine( field, 55, 20, 55 ) - Engine( field, 30, 120, 10 );
		Assert.IsTrue( TerrainRayPick.TryPick( field, origin, direction, out var hit ) );
		var expected = Engine( field, 55, 20, 55 );
		Assert.AreEqual( expected.X, hit.X, 1e-3f );
		Assert.AreEqual( expected.Y, hit.Y, 1e-3f );
		Assert.AreEqual( expected.Z, hit.Z, 1e-3f );
		Assert.IsTrue( OriginalParkPlacement.TryGetCell( field, hit.X, hit.Y, out var x, out var y ) );
		Assert.AreEqual( (5, 5), (x, y) );
	}

	[TestMethod]
	public void ARaisedHillMovesTheHitCellAwayFromTheGroundPlanePick()
	{
		// a plateau of height 30 over cells 4..9, ground 0 elsewhere: the ray reaches the plateau before the plane
		var field = Field( ( x, _ ) => x >= 4 ? 30f : 0f );
		var origin = Engine( field, 0, 90, 50 );
		var target = Engine( field, 60, 30, 50 );
		var direction = target - origin;
		Assert.IsTrue( TerrainRayPick.TryPick( field, origin, direction, out var hit ) );
		Assert.AreEqual( target.Z, hit.Z, 1e-2f, "the hit is on the plateau, not on Z = 0" );
		Assert.IsTrue( OriginalParkPlacement.TryGetCell( field, hit.X, hit.Y, out var x, out _ ) );
		Assert.AreEqual( 6, x );
		Assert.IsTrue( ParkPlacement.TryIntersectGround( origin, direction, out var plane ) );
		Assert.IsTrue( OriginalParkPlacement.TryGetCell( field, plane.X, plane.Y, out var planeX, out _ ) );
		Assert.AreNotEqual( x, planeX, "the plane pick would pick a cell further along the ray" );
	}

	[TestMethod]
	public void ARayThatMissesTheGridOrPointsAwayFindsNothing()
	{
		var field = Field( ( _, _ ) => 0 );
		Assert.IsFalse( TerrainRayPick.TryPick( field, Engine( field, 500, 100, 500 ), Engine( field, 600, 50, 600 ) - Engine( field, 500, 100, 500 ), out _ ) );
		// pointing upwards over the grid: the plane lies behind the camera
		Assert.IsFalse( TerrainRayPick.TryPick( field, Engine( field, 50, 10, 50 ), new NVector3( 0, 0, 1 ), out _ ) );
		Assert.IsFalse( TerrainRayPick.TryPick( field, new NVector3( float.NaN, 0, 0 ), new NVector3( 0, 0, -1 ), out _ ) );
	}

	[TestMethod]
	public void AVerticalRayHitsTheCellBelowIt()
	{
		var field = Field( ( x, z ) => x + z );
		var origin = Engine( field, 73, 200, 22 );
		Assert.IsTrue( TerrainRayPick.TryPick( field, origin, new NVector3( 0, 0, -1 ), out var hit ) );
		Assert.AreEqual( origin.X, hit.X, 1e-3f );
		Assert.AreEqual( origin.Y, hit.Y, 1e-3f );
		Assert.IsTrue( OriginalParkPlacement.TryGetCell( field, hit.X, hit.Y, out var x, out var y ) );
		Assert.AreEqual( (7, 2), (x, y) );
	}

	[TestMethod]
	public void TheCellWordPicksWhichDiagonalTheTrianglesMeetOn()
	{
		// corners (0,0) and (1,1) raised: a ridge along the main diagonal when bit 0x2 is set (that split's triangles contain both raised corners)
		Func<int, int, float> ridge = ( x, z ) => (x == 0 && z == 0) || (x == 1 && z == 1) ? 10f : 0f;
		var diagonal = Field( ridge, ( _, _ ) => 2 );
		var anti = Field( ridge, ( _, _ ) => 4 );
		// straight down over MD2 (9, _, 1): inside the triangle of the corner (1, 0), which is low
		var down = new NVector3( 0, 0, -1 );
		Assert.IsTrue( TerrainRayPick.TryPick( diagonal, Engine( diagonal, 9, 100, 1 ), down, out var onDiagonal ) );
		Assert.IsTrue( TerrainRayPick.TryPick( anti, Engine( anti, 9, 100, 1 ), down, out var onAnti ) );
		Assert.AreNotEqual( onDiagonal.Z, onAnti.Z, "the two splits are different surfaces over the same point" );
	}

	[TestMethod]
	public void ThePlaneOfEitherTriangleCountsInsideTheCellSquare()
	{
		// the original tests each triangle's plane against the cell square, not the triangle: over a ridge cell the higher plane is taken even where the other triangle is the surface
		var field = Field( ( x, z ) => (x == 0 && z == 0) || (x == 1 && z == 1) ? 10f : 0f, ( _, _ ) => 2 );
		Assert.IsTrue( TerrainRayPick.TryPick( field, Engine( field, 9, 100, 1 ), new NVector3( 0, 0, -1 ), out var hit ) );
		var surface = field.GetHeight( 0.9f, 0.1f ) * OriginalParkPlacement.ModelScale;
		Assert.IsTrue( hit.Z > surface + 0.5f, $"hit {hit.Z} is above the triangle surface {surface}" );
	}

	[TestMethod]
	public void HolesAreNotSkipped()
	{
		var field = Field( ( _, _ ) => 6, ( _, _ ) => ModelHeightfield.HoleCellWord );
		Assert.IsTrue( TerrainRayPick.TryPick( field, Engine( field, 45, 100, 45 ), new NVector3( 0, 0, -1 ), out var hit ) );
		Assert.AreEqual( 6 * OriginalParkPlacement.ModelScale, hit.Z, 1e-3f );
	}

	[TestMethod]
	public void TheMouseRayMatchesTheGroundPlanePick()
	{
		var view = Matrix4x4.CreateLookAt( new NVector3( 0, -20, 30 ), NVector3.Zero, NVector3.UnitZ );
		var projection = Matrix4x4.CreatePerspectiveFieldOfView( 0.9f, 4f / 3f, 0.1f, 500 );
		var viewport = new System.Numerics.Vector2( 800, 600 );
		foreach ( var mouse in new[] { new System.Numerics.Vector2( 400, 300 ), new System.Numerics.Vector2( 120, 410 ), new System.Numerics.Vector2( 700, 90 ) } )
		{
			Assert.IsTrue( ParkPlacement.TryGetMouseRay( mouse, viewport, view, projection, out var origin, out var direction ) );
			Assert.IsTrue( ParkPlacement.TryGetGroundPoint( mouse, viewport, view, projection, out var plane ) );
			Assert.IsTrue( ParkPlacement.TryIntersectGround( origin, direction, out var again ) );
			Assert.AreEqual( plane.X, again.X, 1e-3f );
			Assert.AreEqual( plane.Y, again.Y, 1e-3f );
		}
	}
}
