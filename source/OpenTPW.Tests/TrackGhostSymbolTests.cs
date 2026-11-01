using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The track symbol faces of the edit tool's ghost (TRACK-018; tp.exe <c>0x5370E0</c>, <c>0x53C3F0</c>, <c>0x53DF30</c>): one 20 unit face over the tile, its texture coordinates the ±0.4 square turned by the camera yaw.</summary>
[TestClass]
public class TrackGhostSymbolTests
{
	private static ModelHeightfield Field( float[]? heights = null ) => new( 4, 4, 10, 10, 0, 0, Array.Empty<uint>(), heights ?? new float[25], new uint[16] );

	[TestMethod]
	public void ASymbolIsOneTwentyUnitFaceWithTheSquareOfTheRotatingTextureSet()
	{
		var (vertices, indices) = BlueprintMarkers.BuildGeometry( Field(), new[] { (1, 1, BlueprintMarker.Erase) }, null, 0 );
		Assert.AreEqual( 8, vertices.Length, "one face, both sides" );
		Assert.AreEqual( 12, indices.Length );
		Assert.IsTrue( vertices.All( vertex => vertex.TexIndex == 13 ), "m_erase is texture 13" );
		var front = vertices.Take( 4 ).ToArray();
		var uv = front.Select( vertex => vertex.TexCoords ).ToArray();
		// the entries 5 to 8 at yaw 0: (-0.4, -0.4), (0.4, -0.4), (-0.4, 0.4), (0.4, 0.4), each moved by +0.5
		Assert.AreEqual( 0.1f, uv[0].X, 1e-5f );
		Assert.AreEqual( 0.1f, uv[0].Y, 1e-5f );
		Assert.AreEqual( 0.9f, uv[1].X, 1e-5f );
		Assert.AreEqual( 0.9f, uv[3].Y, 1e-5f );
		var span = front.Max( vertex => vertex.Position.X ) - front.Min( vertex => vertex.Position.X );
		Assert.AreEqual( 20 * OriginalParkPlacement.ModelScale, span, 1e-3f, "the face spans the tile's two cells" );
	}

	[TestMethod]
	public void TheCameraYawTurnsTheSymbolWithTheSineTable()
	{
		var (vertices, _) = BlueprintMarkers.BuildGeometry( Field(), new[] { (0, 0, BlueprintMarker.Break) }, null, 0, null, MathF.PI / 2 );
		Assert.AreEqual( 9, vertices[0].TexIndex );
		var uv = vertices.Take( 4 ).Select( vertex => vertex.TexCoords ).ToArray();
		// a quarter turn: x' = cos x - sin y, y' = sin x + cos y with sin 1 and cos 0 at the table's resolution
		Assert.AreEqual( 0.5f + 0.4f, uv[0].X, 1e-4f, "(-0.4, -0.4) goes to (0.4, -0.4)" );
		Assert.AreEqual( 0.5f - 0.4f, uv[0].Y, 1e-4f );
		Assert.AreEqual( 0.5f - 0.4f, uv[3].X, 1e-4f, "(0.4, 0.4) goes to (-0.4, 0.4)" );
		Assert.AreEqual( 0.5f + 0.4f, uv[3].Y, 1e-4f );
	}

	[TestMethod]
	public void TheFourCornersTakeTheTerrainPlusTheLiftAndTheRipple()
	{
		var heights = new float[25];
		for ( var index = 0; index < heights.Length; index++ )
			heights[index] = index * 2f;
		var field = Field( heights );
		var (vertices, _) = BlueprintMarkers.BuildGeometry( field, new[] { (1, 1, BlueprintMarker.Erase) }, null, 0.5f );
		var origin = OriginalParkPlacement.GetOrigin( field );
		foreach ( var (cx, cy) in new[] { (1, 1), (3, 1), (1, 3), (3, 3) } )
		{
			var expected = field.GetCornerHeight( cx, cy ) + BlueprintMarkers.Lift + BlueprintMarkers.Ripple( 0.5f, cx * 10, cy * 10 );
			var engineX = cx * 10 * OriginalParkPlacement.ModelScale - origin.X;
			Assert.IsTrue( vertices.Any( vertex => MathF.Abs( vertex.Position.X - engineX ) < 1e-4f && MathF.Abs( vertex.Position.Z - expected * OriginalParkPlacement.ModelScale ) < 1e-3f ),
				$"corner ({cx}, {cy}) of the 2 x 2 tile" );
		}
	}

	private static IReadOnlyDictionary<(int X, int Y), float?> Raises( params ((int X, int Y) Cell, float Raise)[] raises ) =>
		raises.ToDictionary( item => item.Cell, item => (float?)item.Raise );

	[TestMethod]
	public void ATilesRaiseIsTheGreatestOfItsFourCellsAndASideWallClimbsFromTheLowerNeighbourInTens()
	{
		// the tile (2, 2) has one cell raised 20; its neighbour above (2, 0) stands at 0: the wall on that side has two faces
		var heights = Raises( ((2, 2), 0f), ((3, 2), 0f), ((2, 3), 20f), ((3, 3), 0f) );
		Assert.AreEqual( 20f, BlueprintMarkers.TileRaise( heights, 2, 2 ) );
		var (vertices, _) = BlueprintMarkers.BuildGeometry( Field(), new[] { (2, 2, BlueprintMarker.Break) }, null, 0, heights );
		// the symbol face (4 vertices x 2 sides) and, per side that stands lower, one wall face per 10 units from the neighbour's raise (0) to 20: two faces on each of the four sides
		Assert.AreEqual( 8 + 4 * 2 * 8, vertices.Length );
		Assert.AreEqual( 8, vertices.Count( vertex => vertex.TexIndex == 9 ), "the symbol face" );
		Assert.AreEqual( 64, vertices.Count( vertex => vertex.TexIndex == 0 ), "blue walls" );
		var lifted = BlueprintMarkers.BuildGeometry( Field(), new[] { (2, 2, BlueprintMarker.Break) }, null, 0, Raises( ((2, 2), 0f) ) ).Vertices;
		Assert.AreEqual( 8, lifted.Length, "no raise, no walls" );
	}

	[TestMethod]
	public void ARefusedTileIsTheRedFaceWithRedWallsAndTheUnitSquare()
	{
		var heights = Raises( ((2, 2), 10f), ((3, 2), 10f), ((2, 3), 10f), ((3, 3), 10f) );
		var (vertices, _) = BlueprintMarkers.BuildGeometry( Field(), new[] { (2, 2, BlueprintMarker.RefusedTile) }, null, 0, heights );
		Assert.IsTrue( vertices.All( vertex => vertex.TexIndex == 1 ), "the face and its walls are red" );
		Assert.AreEqual( 8 + 4 * 8, vertices.Length );
		var face = vertices.Take( 4 ).Select( vertex => vertex.TexCoords ).ToArray();
		Assert.AreEqual( 0f, face[0].X, 1e-6f, "a texture below 8 takes the unit square" );
		Assert.AreEqual( 1f, face[3].X, 1e-6f );
	}

	[TestMethod]
	public void RedFacesOnlyAppearWhileTheBlinkFlagIsSetAndKeepTheirPlaceInTheArraysAsPoints()
	{
		var cells = new[] { (1, 1, BlueprintMarker.Refused), (2, 2, BlueprintMarker.Allowed) };
		var shown = BlueprintMarkers.BuildGeometry( Field(), cells, null, 0, null, 0, showRed: true );
		var hidden = BlueprintMarkers.BuildGeometry( Field(), cells, null, 0, null, 0, showRed: false );
		Assert.AreEqual( shown.Vertices.Length, hidden.Vertices.Length, "a dynamic model keeps its vertex count" );
		Assert.AreEqual( shown.Indices.Length, hidden.Indices.Length );
		var red = hidden.Vertices.Take( 8 ).ToArray();
		Assert.IsTrue( red.All( vertex => vertex.TexIndex == 1 && vertex.Position == red[0].Position ), "the hidden red face is a point" );
		Assert.IsTrue( red.All( vertex => !float.IsNaN( vertex.Normal.X ) ) );
		Assert.IsFalse( shown.Vertices.Take( 4 ).All( vertex => vertex.Position == shown.Vertices[0].Position ) );
	}

	[TestMethod]
	public void TheGlideMovesTheFacesOfATextureBelowThirteenAndNotTheEraseFace()
	{
		var glide = new Vector2( 30, -10 );
		var plain = BlueprintMarkers.BuildGeometry( Field(), new[] { (1, 1, BlueprintMarker.Break), (2, 2, BlueprintMarker.Erase) }, null, 0 ).Vertices;
		var moved = BlueprintMarkers.BuildGeometry( Field(), new[] { (1, 1, BlueprintMarker.Break), (2, 2, BlueprintMarker.Erase) }, null, 0, null, 0, true, glide ).Vertices;
		var scale = OriginalParkPlacement.ModelScale;
		Assert.AreEqual( 30 * scale, moved[0].Position.X - plain[0].Position.X, 1e-3f, "m_break glides (texture 9)" );
		Assert.AreEqual( 0f, moved[8].Position.X - plain[8].Position.X, 1e-6f, "m_erase (texture 13) does not" );
	}

	[TestMethod]
	public void TheBlinkIsHiddenTwoFlushesAndShownSeven()
	{
		var flush = new BlueprintFlush();
		var shown = new List<bool>();
		for ( var frame = 0; frame < 18; frame++ )
		{
			flush.Tick( (0, 0) );
			shown.Add( flush.RedShown );
		}
		// counter 1 (hidden), 2 -> shown; then 1..6 shown, 7 -> hidden (counter 0 again), 1 hidden, 2 -> shown
		CollectionAssert.AreEqual( new[] { false, true, true, true, true, true, true, true, false, false, true, true, true, true, true, true, true, false }, shown );
	}

	[TestMethod]
	public void TheGlidePointMovesHalfTheWayToTenTimesTheCursorEveryFlush()
	{
		var flush = new BlueprintFlush();
		flush.Tick( (4, 2) );
		Assert.AreEqual( new Vector2( -20, -10 ), flush.Offset( (4, 2) ), "from the origin: half of 40 and of 20 is covered" );
		for ( var frame = 0; frame < 40; frame++ )
			flush.Tick( (4, 2) );
		Assert.AreEqual( 0f, flush.Offset( (4, 2) ).X, 1e-3f );
		flush.Tick( (5, 2) );
		Assert.AreEqual( -10 + 0.5f * 10, flush.Offset( (5, 2) ).X, 1e-3f, "a cell on: the face lags by what is left of the new distance" );
	}

	[TestMethod]
	public void TheBlinkCounterMovesByTheGameSpeedPerFlush()
	{
		var flush = new BlueprintFlush();
		flush.Tick( (0, 0), 2 );
		Assert.IsTrue( flush.RedShown, "at twice the speed the counter passes 1 in one flush" );
		for ( var step = 0; step < 3; step++ )
			flush.Tick( (0, 0), 2 );
		Assert.IsTrue( flush.RedShown, "6 is not above 6" );
		flush.Tick( (0, 0), 2 );
		Assert.IsFalse( flush.RedShown, "8 is" );
	}

	[TestMethod]
	public void TheLineToolsTilesTakeTheTileRaiseAndTheEndTileTheRotatingTexture()
	{
		var raises = new Dictionary<(int X, int Y), float?> { [(2, 2)] = 20f, [(2, 0)] = 0f, [(4, 2)] = null, [(2, 4)] = 20f, [(0, 2)] = 20f };
		var (vertices, _) = BlueprintMarkers.BuildGeometry( Field(), new[] { (2, 2, BlueprintMarker.EndTile) }, null, 0, null, 0, true, default, raises );
		Assert.AreEqual( 11, vertices[0].TexIndex, "m_end over the tile" );
		var flat = BlueprintMarkers.BuildGeometry( Field(), new[] { (2, 2, BlueprintMarker.EndTile) }, null, 0 ).Vertices;
		Assert.AreEqual( 20f * OriginalParkPlacement.ModelScale, vertices[0].Position.Z - flat[0].Position.Z, 1e-3f, "the face stands at the tile raise" );
		// only the side towards -Y has a lower neighbour that exists: two wall faces of 10 units; the +X side is off the map
		Assert.AreEqual( 8 + 2 * 8, vertices.Length );
		var (blue, _) = BlueprintMarkers.BuildGeometry( Field(), new[] { (2, 2, BlueprintMarker.AllowedTile) }, null, 0, null, 0, true, default, raises );
		Assert.AreEqual( 0, blue[0].TexIndex, "a blue tile takes the plain square" );
	}

	[TestMethod]
	public void TheBuyLandGhostWalksTheHoveredPostAloneThenTheFourEdges()
	{
		CollectionAssert.AreEqual( new[] { ((6, 8), (6, 8)) }, TileLineGhost.FencePasses( null, (6, 8) ) );
		CollectionAssert.AreEqual( new[] { ((12, 4), (16, 4)), ((18, 4), (18, 10)), ((12, 10), (16, 10)), ((10, 10), (10, 4)) }, TileLineGhost.FencePasses( (18, 10), (10, 4) ), "the top and bottom edges leave out the corner tiles of the side edges" );
		CollectionAssert.AreEqual( new[] { ((4, 4), (4, 4)), ((4, 4), (4, 10)), ((4, 10), (4, 10)), ((4, 10), (4, 4)) }, TileLineGhost.FencePasses( (4, 4), (4, 10) ), "a rectangle one post wide keeps its edges whole" );
	}

	[TestMethod]
	public void AnExtensionCellAnswersAsItsParentThroughTheMapsOwnCellIndex()
	{
		var cells = new ParkCellMap( 16, 16 );
		// the tile's origin at (6, 4) and its extension at (7, 5), whose parent word is the origin's index in the map
		cells.Tracks.Set( 6, 4, new TrackCell { Type = TrackCellTypes.WaterOrigin, Parent = -1, Owner = 7, Mask = 0x11 } );
		cells.Tracks.Set( 7, 5, new TrackCell { Type = TrackCellTypes.Extension, Parent = cells.Tracks.IndexOf( 6, 4 ), Owner = 7 } );
		var map = new ParkTileGhostMap( cells, ( x, y ) => null, ( x, y ) => 0f );
		var extension = map.NeighbourAt( 7, 5 );
		Assert.IsTrue( extension.Present );
		Assert.AreEqual( TrackCellTypes.WaterOrigin, extension.Type, "the extension's type is its parent's" );
		Assert.AreEqual( 7, extension.Owner, "and so is its owner" );
		Assert.AreEqual( TrackCellTypes.WaterOrigin, map.NeighbourAt( 6, 4 ).Type, "an origin answers for itself" );
		Assert.IsFalse( map.NeighbourAt( 9, 9 ).Present, "a cell without a record is not present" );
	}
}
