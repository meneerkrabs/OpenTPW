using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The terrain drape of the kart and water track pieces ([BIN:TPW-EXE:0x00469A80]): the drape sampler against the landscape's triangle split, the tiles' shared edges, and the
/// vehicles' surface heights (data-free tests first, then the shipped pieces on the original PC data, inconclusive without OPENTPW_GAME_PATH).
/// </summary>
[TestClass]
public class TrackDrapeTests
{
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Init() => previousFileSystem = FileSystem;

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFileSystem!;

	private static float[,] Corners( int cellsX, int cellsZ ) => new float[cellsX + 1, cellsZ + 1];

	[TestMethod]
	public void TheDrapeSamplerIsTheCellTriangleSplitByTheCellWordWithoutThePlanarShortcut()
	{
		var random = new Random( 829 );
		var corners = Corners( 8, 8 );
		for ( var x = 0; x <= 8; x++ )
			for ( var z = 0; z <= 8; z++ )
				corners[x, z] = (float)(random.NextDouble() * 6);
		// a flat cell: the drape is the plane, and so is the planar shortcut
		for ( var x = 2; x <= 3; x++ )
			for ( var z = 2; z <= 3; z++ )
				corners[x, z] = 2f;
		bool Anti( int x, int z ) => (x * 7 + z * 3) % 2 == 0;
		float Corner( int x, int z ) => corners[x, z];
		for ( var step = 0; step < 400; step++ )
		{
			var (x, z) = ((float)(random.NextDouble() * 79.9), (float)(random.NextDouble() * 79.9));
			var (ix, iz) = ((int)(x / 10), (int)(z / 10));
			var (fx, fz) = (x / 10 - ix, z / 10 - iz);
			var expected = LandscapeSampler.InCell( Corner( ix, iz ), Corner( ix + 1, iz ), Corner( ix, iz + 1 ), Corner( ix + 1, iz + 1 ), fx, fz, true, Anti( ix, iz ) );
			Assert.AreEqual( expected, LandscapeSampler.SampleDrape( 8, 8, 10, 10, Corner, Anti, x, z ), 1e-4f, $"({x}, {z})" );
		}
	}

	[TestMethod]
	public void TheDrapeSamplerReadsTheFarEdgeAndNegativePointsWithoutALookup()
	{
		var corners = Corners( 4, 4 );
		for ( var x = 0; x <= 4; x++ )
			for ( var z = 0; z <= 4; z++ )
				corners[x, z] = x * 3 + z * 5 + ((x + z) % 2) * 2;
		float Corner( int x, int z ) => corners[x, z];
		foreach ( var anti in new[] { false, true } )
		{
			// the far edge: the far corner row weighs 1, the same as the corner itself
			Assert.AreEqual( Corner( 4, 2 ), LandscapeSampler.SampleDrape( 4, 4, 10, 10, Corner, ( _, _ ) => anti, 40, 20 ), 1e-4f );
			Assert.AreEqual( Corner( 2, 4 ), LandscapeSampler.SampleDrape( 4, 4, 10, 10, Corner, ( _, _ ) => anti, 20, 40 ), 1e-4f );
			Assert.AreEqual( Corner( 4, 4 ), LandscapeSampler.SampleDrape( 4, 4, 10, 10, Corner, ( _, _ ) => anti, 40, 40 ), 1e-4f );
			// a negative coordinate reads cell 0
			Assert.AreEqual( Corner( 0, 0 ), LandscapeSampler.SampleDrape( 4, 4, 10, 10, Corner, ( _, _ ) => anti, -3, -3 ), 1e-4f );
		}
	}

	[TestMethod]
	public void TheLiftOfTwoTilesAgreesOnTheirSharedEdge()
	{
		// a sloped, bumpy landscape: engine height as a function of the grid point
		static float Terrain( float x, float y ) => 0.5f * x + 0.3f * y + 4 * MathF.Sin( x / 7 ) * MathF.Cos( y / 5 );
		static (float X, float Y) LocalOf( int x, int y, int rotation, float gridX, float gridY )
		{
			var angle = -rotation * MathF.PI / 180;
			var (dx, dy) = (gridX - (10 + x * 10), gridY - (10 + y * 10));
			return (10 + dx * MathF.Cos( angle ) - dy * MathF.Sin( angle ), 10 + dx * MathF.Sin( angle ) + dy * MathF.Cos( angle ));
		}
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			// tiles (30, 40) and (32, 40) span 300..320 and 320..340 in x and share the edge x = 320, whatever the rotation of either
			for ( var along = 0f; along <= 20; along += 2.5f )
			{
				var edge = (X: 320f, Y: 400f + along);
				var leftLocal = LocalOf( 30, 40, rotation, edge.X, edge.Y );
				var rightLocal = LocalOf( 32, 40, rotation, edge.X, edge.Y );
				var leftTile = TrackDrape.GridPosition( 30, 40, rotation, leftLocal.X, leftLocal.Y );
				var rightTile = TrackDrape.GridPosition( 32, 40, rotation, rightLocal.X, rightLocal.Y );
				Assert.AreEqual( edge.X, leftTile.X, 1e-3f );
				Assert.AreEqual( edge.Y, leftTile.Y, 1e-3f );
				Assert.AreEqual( edge.X, rightTile.X, 1e-3f );
				Assert.AreEqual( edge.Y, rightTile.Y, 1e-3f );
				// two different instance heights: the drawn height (model z + instance height + lift) is the landscape plus the model's own z, whatever the tile
				var (leftHeight, rightHeight) = (11.5f, -3.25f);
				var left = leftHeight + TrackDrape.Lift( 30, 40, rotation, leftHeight, Terrain )( leftLocal.X, leftLocal.Y ) * ObjectPlacement.ModelScale;
				var right = rightHeight + TrackDrape.Lift( 32, 40, rotation, rightHeight, Terrain )( rightLocal.X, rightLocal.Y ) * ObjectPlacement.ModelScale;
				Assert.AreEqual( left, right, 1e-3f );
				Assert.AreEqual( Terrain( edge.X, edge.Y ), left, 1e-3f, "a vertex of model height 0 lies on the landscape" );
			}
		}
	}

	[TestMethod]
	public void ApplyLeavesAFlatLandscapeAtTheInstanceHeightAlone()
	{
		var vertices = new[] { new Vertex( new Vector3( 1, 2, 3 ), new Vector3( 0, 0, 1 ), new Vector2( 0, 0 ) ), new Vertex( new Vector3( 4, 5, 6 ), new Vector3( 0, 0, 1 ), new Vector2( 1, 1 ) ) };
		Assert.IsNull( TrackDrape.Apply( vertices, TrackDrape.Lift( 0, 0, 0, 7.5f, ( _, _ ) => 7.5f ) ) );
		var moved = TrackDrape.Apply( vertices, TrackDrape.Lift( 0, 0, 0, 7.5f, ( x, y ) => 7.5f + x * ObjectPlacement.ModelScale ) )!;
		Assert.AreEqual( 3 + 1f, moved[0].Position.Z, 1e-4f );
		Assert.AreEqual( 6 + 4f, moved[1].Position.Z, 1e-4f );
		Assert.AreEqual( 4f, moved[1].Position.X, 1e-6f, "the drape moves heights only" );
		Assert.AreEqual( vertices[1].TexCoords, moved[1].TexCoords );
	}

	// ---- the shipped pieces ------------------------------------------------------------------------

	private static ObjectCatalogEntry JungleKarts()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original ride archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		return ObjectCatalog.Themes.SelectMany( theme => ObjectCatalog.Load( theme ).Entries )
			.First( entry => !entry.IsBonus && entry.Settings.GetInt( "Bumper.WhichTrackType" ) == 1 && entry.Category == ObjectCategory.Ride && entry.Theme.Equals( "jungle", StringComparison.OrdinalIgnoreCase ) );
	}

	// engine heights of a sloped landscape: 0.5 per model unit along x, 0.25 along y
	private static float SlopeAt( float x, float y ) => 0.5f * x + 0.25f * y;
	private static float SlopeCorner( int x, int y ) => SlopeAt( x * 10, y * 10 );

	private static TrackRenderer SlopedRenderer( ObjectCatalogEntry entry, TrackPieceKit kit ) =>
		new( entry, kit, () => System.Numerics.Vector2.Zero, ( x, y ) => SlopeAt( x * 10 + 5, y * 10 + 5 ), SlopeCorner, SlopeAt, SlopeAt );

	private static TrackCellLayer LayerWith( int x, int y, int piece, int rotation )
	{
		var layer = new TrackCellLayer( 128, 128 );
		layer.Set( x, y, new TrackCell { Type = TrackCellTypes.KartOrigin, Parent = -1, Family = (int)TrackFamily.Kart, Piece = piece, Rotation = rotation } );
		foreach ( var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1) } )
			layer.Set( x + dx, y + dy, new TrackCell { Type = TrackCellTypes.Extension, Parent = layer.IndexOf( x, y ) } );
		return layer;
	}

	[TestMethod]
	public void NeighbouringStraightTilesMeetOnTheSlopeWhereTheirInstanceHeightsDiffer()
	{
		var entry = JungleKarts();
		var kit = TrackPieceKit.Load( entry.FileSystem, entry.ArchivePath, TrackFamily.Kart );
		var straight = TrackPieceSelector.PieceStraight;
		var sloped = SlopedRenderer( entry, kit );
		// the same tiles on flat ground at height 0: nothing is lifted, so these are the pieces' own vertices
		var flat = new TrackRenderer( entry, kit, () => System.Numerics.Vector2.Zero, ( x, y ) => 0f, ( x, y ) => 0f, ( x, y ) => 0f, ( x, y ) => 0f );
		var layer = LayerWith( 30, 40, straight, 0 );
		// the straight piece at rotation 0 runs along y: the tile above (30, 42) shares the edge y = 420
		layer.Set( 30, 42, new TrackCell { Type = TrackCellTypes.KartOrigin, Parent = -1, Family = (int)TrackFamily.Kart, Piece = straight, Rotation = 0 } );
		Assert.AreNotEqual( sloped.TileHeight( 30, 40 ), sloped.TileHeight( 30, 42 ), "the slope gives the two tiles different instance heights" );
		System.Numerics.Vector3 World( TrackRenderer renderer, int x, int y, Vertex vertex ) => System.Numerics.Vector3.Transform( vertex.Position.GetSystemVector3(), renderer.TileTransform( x, y, 0 ) );
		List<System.Numerics.Vector3> Vertices( TrackRenderer renderer, int x, int y ) => renderer.DrapedVertices( layer, x, y ).SelectMany( part => part.Select( vertex => World( renderer, x, y, vertex ) ) ).ToList();
		var (flatLow, flatHigh) = (Vertices( flat, 30, 40 ), Vertices( flat, 30, 42 ));
		var (slopedLow, slopedHigh) = (Vertices( sloped, 30, 40 ), Vertices( sloped, 30, 42 ));
		Assert.AreEqual( flatLow.Count, slopedLow.Count );
		Assert.AreEqual( flatHigh.Count, slopedHigh.Count );
		var (considered, moved) = (0, 0);
		for ( var low = 0; low < flatLow.Count; low++ )
		{
			if ( MathF.Abs( flatLow[low].Y - 420 * ObjectPlacement.ModelScale ) > 1e-3f )
				continue;
			// a vertex of the upper tile at the same place and the same model height: the same point of the joint seen from the other tile
			for ( var high = 0; high < flatHigh.Count; high++ )
			{
				if ( MathF.Abs( flatHigh[high].X - flatLow[low].X ) > 2e-5f || MathF.Abs( flatHigh[high].Y - flatLow[low].Y ) > 2e-5f || MathF.Abs( flatHigh[high].Z - flatLow[low].Z ) > 1e-4f )
					continue;
				considered++;
				Assert.AreEqual( slopedLow[low].Z, slopedHigh[high].Z, 2e-3f, $"joint vertex at x {flatLow[low].X / ObjectPlacement.ModelScale}: the two tiles draw it at the same height" );
				if ( MathF.Abs( slopedLow[low].Z - flatLow[low].Z ) > 1e-3f )
					moved++;
			}
		}
		Assert.IsTrue( considered > 4, $"{considered} joint vertices are shared by the two tiles" );
		Assert.IsTrue( moved > 0, "the slope lifted them" );
	}

	[TestMethod]
	public void AFlatPieceTakesTheLandscapeUnderTheProbePlusItsHeaderOffsetNotTheInstanceHeight()
	{
		var entry = JungleKarts();
		var kit = TrackPieceKit.Load( entry.FileSystem, entry.ArchivePath, TrackFamily.Kart );
		var renderer = SlopedRenderer( entry, kit );
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var layer = LayerWith( 30, 40, TrackPieceSelector.PieceStraight, rotation );
			foreach ( var (x, y) in new[] { (301f, 401f), (305f, 409f), (318.5f, 412f), (319.9f, 419.9f) } )
			{
				var expected = SlopeAt( x, y ) / ObjectPlacement.ModelScale + 3.1f;
				Assert.AreEqual( expected, renderer.SurfaceHeight( layer, x, y ) ?? float.NaN, 1e-3f, $"rotation {rotation} at ({x}, {y}): mode 1 reads the landscape under the probe + header +0xA0 (3.1)" );
			}
		}
	}

	[TestMethod]
	public void ABridgePieceIsSampledOnItsDrapedSurfaceRecordsMode2()
	{
		var entry = JungleKarts();
		var kit = TrackPieceKit.Load( entry.FileSystem, entry.ArchivePath, TrackFamily.Kart );
		var bridge = kit.Entries.First( item => item.Name == "H" ).Index;
		var model = kit.GetModel( bridge )!;
		var faces = TrackSurface.FromObject( model, drapeLoaded: true );
		Assert.AreEqual( TrackSamplerMode.SurfaceRecords, faces.Mode, "H has a surface record: mode 2 (0x44A870 sets the block's bit 8)" );
		var renderer = SlopedRenderer( entry, kit );
		var layer = LayerWith( 30, 40, bridge, 0 );
		var tileHeight = renderer.TileHeight( 30, 40 ) / ObjectPlacement.ModelScale;
		var hits = 0;
		for ( var dx = 1f; dx < 20; dx += 3 )
		{
			for ( var dy = 1f; dy < 20; dy += 3 )
			{
				if ( faces.HeightAt( dx, dy ) is not { } floor )
					continue;
				var (gx, gy) = (300 + dx, 400 + dy);
				var expected = SlopeAt( gx, gy ) / ObjectPlacement.ModelScale + floor;
				Assert.AreEqual( expected, renderer.SurfaceHeight( layer, gx, gy ) ?? float.NaN, 2e-3f, $"({dx}, {dy}) on the bridge: the draped face under the probe, which stands on the landscape under it plus the face's own height (tile height {tileHeight})" );
				hits++;
			}
		}
		Assert.IsTrue( hits > 10, $"{hits} probes hit the bridge's faces" );
	}
}
