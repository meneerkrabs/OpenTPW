using System;
using System.IO;
using System.Numerics;
using V3 = System.Numerics.Vector3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Which object a vehicle's height probe samples (TRACK-001; tp.exe <c>0x452A60</c>, <c>0x450AC0</c>, <c>0x546280</c>), asset-free: a cell with no piece instance (a station tile, an add-on's tile) or no track
/// gives its owner object in that object's own sampler mode, and a cell with no object leaves the probe at the vehicle's previous height.
/// </summary>
[TestClass]
public class TrackObjectSamplingTests
{
		/// <summary>A flat surface 5 units above the object's base over the whole 40 x 40 ground plane.</summary>
	private static TrackSurface Plate() => new( new[]
	{
		new TrackSurface.Triangle( new V3( -100, -100, 5 ), new V3( 100, -100, 5 ), new V3( -100, 100, 5 ) ),
		new TrackSurface.Triangle( new V3( 100, -100, 5 ), new V3( 100, 100, 5 ), new V3( -100, 100, 5 ) )
	} );

	private sealed class Rig : IDisposable
	{
		private readonly string directory = Path.Combine( Path.GetTempPath(), "r9f-sampling-" + Guid.NewGuid().ToString( "N" ) );
		public readonly TrackCellLayer Layer = new( 16, 16 );
		public readonly TrackRenderer Renderer;
		public (int X, int Y)? ObjectAt;
		public float BaseHeight = 2f;

		public Rig( TrackFamily family = TrackFamily.Kart, float ground = 0 )
		{
			Directory.CreateDirectory( Path.Combine( directory, "pieces" ) );
			var kit = TrackPieceKit.Load( new BaseFileSystem( directory ), "pieces", family );
			Renderer = new TrackRenderer( null!, kit, () => Vector2.Zero, ( x, y ) => ground * ObjectPlacement.ModelScale )
			{
				ObjectSurfaceAt = ( x, y ) => ObjectAt == (x, y) || ObjectAt is { } at && Math.Abs( at.X - x ) <= 1 && Math.Abs( at.Y - y ) <= 1
					? (Plate(), Matrix4x4.Identity, BaseHeight)
					: null
			};
		}

		public void Tile( int x, int y, byte type )
		{
			foreach ( var (cx, cy) in TrackStation.TileCellsOf( x, y ) )
			{
				var origin = (cx, cy) == (x, y);
				Layer.Set( cx, cy, new TrackCell { Type = origin ? type : TrackCellTypes.Extension, Parent = origin ? -1 : Layer.IndexOf( x, y ) } );
			}
		}

		public void Dispose()
		{
			Renderer.Dispose();
			try { Directory.Delete( directory, true ); } catch ( IOException ) { }
		}
	}

	[TestMethod]
	public void ACellWithNoTrackAndNoObjectLeavesTheProbeWhereItWas()
	{
		using var rig = new Rig();
		Assert.IsNull( rig.Renderer.SurfaceHeight( rig.Layer, 35f, 35f ), "no object: 0x450AC0 answers 0 and the probe is never sampled" );
	}

	[TestMethod]
	public void ACellWithNoTrackAndAnObjectIsTheObjectsFaces()
	{
		using var rig = new Rig();
		rig.ObjectAt = (3, 3);
		Assert.AreEqual( 7f, rig.Renderer.SurfaceHeight( rig.Layer, 35f, 35f )!.Value, 1e-5f, "the object's base 2 plus its face 5" );
		Assert.IsNull( rig.Renderer.SurfaceHeight( rig.Layer, 95f, 95f ), "a cell beside it still has none" );
	}

	[DataTestMethod]
	[DataRow( TrackFamily.Kart, TrackCellTypes.KartStart )]
	[DataRow( TrackFamily.Water, TrackCellTypes.WaterStart )]
	public void AStationTileIsSampledThroughTheRidesOwnModelNotAPieceFloor( TrackFamily family, byte type )
	{
		using var rig = new Rig( family, ground: 1f );
		rig.Tile( 4, 4, type );
		rig.ObjectAt = (4, 4);
		rig.BaseHeight = 3f;
		foreach ( var (x, y) in new[] { (41f, 41f), (45f, 49f), (58f, 52f) } )
			Assert.AreEqual( 8f, rig.Renderer.SurfaceHeight( rig.Layer, x, y )!.Value, 1e-5f, $"station tile at ({x}, {y}): the ride's face 5 above its base 3 (the Halloween and space karts' station reads faces)" );
		rig.ObjectAt = null;
		Assert.IsNull( rig.Renderer.SurfaceHeight( rig.Layer, 41f, 41f ), "a station tile with no ride object there has no surface" );
	}

	[TestMethod]
	public void AnAddOnsTileIsSampledThroughItsObjectAndAnEmptyOneKeepsTheProbe()
	{
		using var rig = new Rig();
		rig.Tile( 6, 6, TrackCellTypes.KartOrigin );
		var cell = rig.Layer.Get( 6, 6 );
		cell.Flags = TrackCell.FlagAddOn;
		rig.Layer.Set( 6, 6, cell );
		Assert.IsNull( rig.Renderer.SurfaceHeight( rig.Layer, 61f, 61f ), "the add-on tile has no piece instance and no object on it" );
		rig.ObjectAt = (6, 6);
		Assert.AreEqual( 7f, rig.Renderer.SurfaceHeight( rig.Layer, 61f, 61f )!.Value, 1e-5f );
	}

	[TestMethod]
	public void APieceTileWithoutItsMeshFallsBackToTheLandscape()
	{
		using var rig = new Rig( ground: 4f );
		rig.Tile( 8, 8, TrackCellTypes.KartOrigin );
		Assert.AreEqual( 4f, rig.Renderer.SurfaceHeight( rig.Layer, 81f, 81f )!.Value, 1e-5f, "a laid tile keeps the piece path (no object lookup)" );
	}
}
