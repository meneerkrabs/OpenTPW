using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>The CPU side of the slides' rendering (docs/POOLS.md, "Water slides"): the swept track shell, the pylons and the generated textures. The GPU side is exercised only on screen.</summary>
[TestClass]
public class PoolSlideMeshTests
{
	private static readonly SVector2 Origin = new( -10f, 6f );
	private const float Cell = 2f;

	private static IDisposable PoolsOn() => OpenTPW.Mods.OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );

	private static PoolSlide Slide( PoolSlideProfile profile, out ParkCellMap cells )
	{
		(cells, var tower) = (new ParkCellMap( 40, 40 ), new PoolSlideTower( 8, 12, new[] { (8, 12), (9, 12), (8, 13), (9, 13) }, new[] { (10, 12), (10, 13) }, profile, 4 ));
		for ( var y = 10; y < 20; y++ )
			for ( var x = 10; x < 20; x++ )
				cells.SetType( x, y, ParkCellTypes.PoolWater );
		var map = cells;
		var world = new PoolSlideWorld( () => new PoolSlideSite { Cells = map, Ground = ( _, _ ) => 0f, WaterLevel = ( x, y ) => map.RawTypeAt( x, y ) == ParkCellTypes.PoolWater ? -0.12f : null }, id => id == tower.Id ? tower : null, () => null );
		foreach ( var (x, y) in new[] { (8, 11), (8, 10), (8, 9), (9, 9), (10, 9), (11, 9), (11, 10) } )
			Assert.AreEqual( PoolSlideResult.Ok, world.AddCell( 8, 12, x, y ) );
		return world.Find( tower.Id )!;
	}

	private static PoolSlideMesh Mesh( PoolSlide slide, ParkCellMap cells ) =>
		PoolSlideMeshBuilder.Build( new[] { slide }, ( _, _ ) => 0f, ( x, y ) => cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater ? -0.12f : null, Origin, Cell );

	[TestMethod]
	public void TheShellFollowsTheTrackFromTheTowerToTheSplashAndStaysInItsBounds()
	{
		using var scope = PoolsOn();
		foreach ( var profile in new[] { PoolSlideProfile.Flume, PoolSlideProfile.Tube } )
		{
			var slide = Slide( profile, out var cells );
			var mesh = Mesh( slide, cells );
			var track = mesh.Track;
			Assert.IsTrue( track.Vertices.Count > 400 && track.TriangleCount > 400, $"{profile}: {track.TriangleCount} triangles" );
			Assert.IsTrue( track.Vertices.All( v => float.IsFinite( v.Position.X + v.Position.Y + v.Position.Z ) && float.IsFinite( v.Uv.X + v.Uv.Y ) ) );
			Assert.IsTrue( track.Vertices.All( v => MathF.Abs( v.Normal.Length() - 1f ) < 1e-3f ), "unit normals" );
			Assert.IsTrue( track.Indices.All( index => index >= 0 && index < track.Vertices.Count ) );
			// the ring at the start is centred on the tower's face, the end on the splash cell's middle
			var start = PoolSlideMeshBuilder.ToEngine( slide.PointAt( 0 ), Origin, Cell );
			var end = PoolSlideMeshBuilder.ToEngine( slide.PointAt( slide.Length ), Origin, Cell );
			Assert.AreEqual( Origin.X + 8.5f * Cell, start.X, 1e-3 );
			Assert.AreEqual( Origin.Y + 12f * Cell, start.Y, 1e-3, "the tower's north face" );
			Assert.AreEqual( slide.TopHeight / 1000f, start.Z, 1e-3 );
			Assert.AreEqual( (Origin.X + 11.5f * Cell, Origin.Y + 10.5f * Cell), (end.X, end.Y) );
			var lowest = track.Vertices.Min( v => v.Position.Z );
			var highest = track.Vertices.Max( v => v.Position.Z );
			Assert.IsTrue( lowest >= slide.EndHeight / 1000f - 0.01f, $"{profile}: nothing below the end ({lowest})" );
			Assert.IsTrue( highest <= slide.TopHeight / 1000f + PoolSlideMeshBuilder.TubeRadius * 2 + 0.01f, $"{profile}: nothing far above the start ({highest})" );
			// every vertex is within a section radius and a lip of some piece's cell
			var reach = PoolSlideMeshBuilder.TubeRadius + PoolSlideMeshBuilder.Lip + 0.05f;
			Assert.IsTrue( track.Vertices.All( v => slide.Cells.Any( cell =>
			{
				var (x0, y0) = (Origin.X + cell.X * Cell, Origin.Y + cell.Y * Cell);
				return v.Position.X >= x0 - reach && v.Position.X <= x0 + Cell + reach && v.Position.Y >= y0 - reach && v.Position.Y <= y0 + Cell + reach;
			} ) ), $"{profile}: inside the pieces' cells" );
		}
	}

	[TestMethod]
	public void TheFlumeIsAHalfPipeWithLipsAndTheTubeARingWithASlitOnTop()
	{
		var flume = PoolSlideMeshBuilder.Section( PoolSlideProfile.Flume );
		var tube = PoolSlideMeshBuilder.Section( PoolSlideProfile.Tube );
		Assert.AreEqual( 15, flume.Count );
		Assert.AreEqual( 21, tube.Count );
		// the flume's lowest point is the centreline's own point and its rims are the half pipe's radius up
		Assert.AreEqual( 0f, flume.Min( p => p.Up ), 1e-4 );
		Assert.AreEqual( PoolSlideMeshBuilder.FlumeRadius, flume.Max( p => p.Up ), 1e-4 );
		Assert.AreEqual( PoolSlideMeshBuilder.FlumeRadius + PoolSlideMeshBuilder.Lip, flume.Max( p => p.Lat ), 1e-4 );
		Assert.IsTrue( flume.All( p => MathF.Abs( p.NormalLat * p.NormalLat + p.NormalUp * p.NormalUp - 1f ) < 1e-4 ) );
		// the tube: a ring of its radius, lowest point at the centreline, no point in the 70 degree slit on top
		Assert.AreEqual( 0f, tube.Min( p => p.Up ), 1e-4 );
		Assert.IsTrue( tube.All( p => MathF.Abs( MathF.Sqrt( p.Lat * p.Lat + (p.Up - PoolSlideMeshBuilder.TubeRadius) * (p.Up - PoolSlideMeshBuilder.TubeRadius) ) - PoolSlideMeshBuilder.TubeRadius ) < 1e-4 ) );
		Assert.IsTrue( tube.All( p => p.NormalUp <= MathF.Sin( 55f * MathF.PI / 180 ) + 1e-3f ), "the slit is open" );
		Assert.AreEqual( tube.Count, tube.Select( p => (MathF.Round( p.Lat, 4 ), MathF.Round( p.Up, 4 )) ).Distinct().Count(), "no repeated point" );
	}

	[TestMethod]
	public void TheFlumeBedIsLitFromAboveAndTheTubeFromOutside()
	{
		using var scope = PoolsOn();
		var flume = Slide( PoolSlideProfile.Flume, out var cells );
		var bed = Mesh( flume, cells ).Track;
		var middle = bed.Vertices.Where( v => MathF.Abs( v.Position.X - (Origin.X + 8.5f * Cell) ) < 0.05f && v.Position.Z > 4f ).ToList();
		Assert.IsTrue( middle.Count > 0 );
		// the lowest vertices of the first straight are the bed: its normal faces up, into the pipe
		var lowest = middle.OrderBy( v => v.Position.Z ).First();
		Assert.IsTrue( lowest.Normal.Z > 0.5f, $"the bed's normal {lowest.Normal}" );
		var tube = Slide( PoolSlideProfile.Tube, out var tubeCells );
		var shell = Mesh( tube, tubeCells ).Track;
		var low = shell.Vertices.Where( v => MathF.Abs( v.Position.X - (Origin.X + 8.5f * Cell) ) < 0.05f && v.Position.Z > 4f ).OrderBy( v => v.Position.Z ).First();
		Assert.IsTrue( low.Normal.Z < -0.5f, $"the tube's underside faces down: {low.Normal}" );
	}

	[TestMethod]
	public void PylonsStandOnTheGroundAndOnThePoolFloorUnderTheSplashAndTheMeshIsRepeatable()
	{
		using var scope = PoolsOn();
		var slide = Slide( PoolSlideProfile.Flume, out var cells );
		var mesh = Mesh( slide, cells );
		var posts = mesh.Pylons;
		Assert.AreEqual( 7 * 4 * 2, posts.TriangleCount, "four sides of two triangles for each of the seven pylons" );
		Assert.AreEqual( 0f, posts.Vertices.Where( v => v.Position.Z > -0.5f ).Min( v => v.Position.Z ), 1e-3, "the pylons of the land pieces stand on the ground" );
		var floor = -0.12f - 1.75f;
		Assert.IsTrue( posts.Vertices.Any( v => MathF.Abs( v.Position.Z - floor ) < 1e-3 ), "the splash chute's pylon stands on the pool floor" );
		Assert.IsTrue( posts.Vertices.All( v => v.Position.Z <= slide.TopHeight / 1000f ), "no pylon above the track" );
		var again = Mesh( slide, cells );
		CollectionAssert.AreEqual( mesh.Track.Vertices.Select( v => v.Position ).ToArray(), again.Track.Vertices.Select( v => v.Position ).ToArray(), "the same mesh every time" );
		CollectionAssert.AreEqual( mesh.Track.Indices.ToArray(), again.Track.Indices.ToArray() );
		Assert.IsTrue( PoolSlideMesh.Empty.IsEmpty );
		Assert.IsTrue( PoolSlideMeshBuilder.Build( Array.Empty<PoolSlide>(), ( _, _ ) => 0f, ( _, _ ) => null, Origin, Cell ).IsEmpty );
	}

	[TestMethod]
	public void TheSlideTexturesAreOpaqueGeneratedAndDifferByStyleAndProfile()
	{
		var seen = new HashSet<string>();
		foreach ( var style in PoolStyles.All )
			foreach ( var profile in new[] { PoolSlideProfile.Flume, PoolSlideProfile.Tube } )
			{
				var pixels = PoolSlideTextures.Surface( style, profile );
				Assert.AreEqual( PoolSlideTextures.Size * PoolSlideTextures.Size * 4, pixels.Length );
				Assert.IsTrue( Enumerable.Range( 0, pixels.Length / 4 ).All( i => pixels[i * 4 + 3] == 255 ), "opaque" );
				CollectionAssert.AreEqual( pixels, PoolSlideTextures.Surface( style, profile ), "deterministic" );
				Assert.IsTrue( seen.Add( Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( pixels ) ) ), $"{style.Id} {profile} differs from every other" );
				Assert.IsTrue( pixels.Distinct().Count() > 20, "not a flat colour" );
			}
	}
}
