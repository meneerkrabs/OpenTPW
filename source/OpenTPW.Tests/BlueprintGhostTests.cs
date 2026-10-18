using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Which cells the build ghosts mark and how the marker faces are built (docs/reverse/BLUEPRINT.md, BLUEPRINT-001 and -003): every cell
/// of a line preview, the footprint's front row and access arrows, the direction bytes, and the draped, rippling vertices.
/// </summary>
[TestClass]
public class BlueprintGhostTests
{
	[TestMethod]
	public void LinePreviewMarksEveryCellAndTurnsRedFromTheRefusedOne()
	{
		var line = new List<(int X, int Y)> { (1, 1), (2, 1), (3, 1), (4, 1), (5, 1) };
		var cells = BlueprintGhosts.LineCells( line, (3, 1) );
		Assert.AreEqual( 5, cells.Count, "the validator keeps refusing after the first refusal, the preview still draws every cell" );
		CollectionAssert.AreEqual(
			new[] { BlueprintMarker.Allowed, BlueprintMarker.Allowed, BlueprintMarker.Refused, BlueprintMarker.Refused, BlueprintMarker.Refused },
			cells.Select( cell => cell.Marker ).ToArray() );
	}

	[TestMethod]
	public void LinePreviewWithoutRefusalIsAllBlue()
	{
		var cells = BlueprintGhosts.LineCells( new List<(int X, int Y)> { (0, 0), (1, 0) }, null );
		Assert.IsTrue( cells.All( cell => cell.Marker == BlueprintMarker.Allowed ) );
	}

	[TestMethod]
	public void BuildDirectionAndRotationFollowTheOriginalHelpers()
	{
		Assert.AreEqual( (byte)1, BlueprintGhosts.BuildDirection( 0 ) );
		Assert.AreEqual( (byte)0x40, BlueprintGhosts.BuildDirection( 90 ) );
		Assert.AreEqual( (byte)0x10, BlueprintGhosts.BuildDirection( 180 ) );
		Assert.AreEqual( (byte)4, BlueprintGhosts.BuildDirection( 270 ) );
		// 0x4D8C20: a direction shifted left by log2 of the build byte, wrapping above 0x80
		Assert.AreEqual( (byte)0x10, BlueprintGhosts.Rotate( 1, 0x10 ) );
		Assert.AreEqual( (byte)0x40, BlueprintGhosts.Rotate( 0x40, 1 ) );
		Assert.AreEqual( (byte)1, BlueprintGhosts.Rotate( 0x40, 4 ) );
		Assert.AreEqual( (byte)0x10, BlueprintGhosts.Rotate( 0x10, 1 ) );
		Assert.AreEqual( (byte)0x40, BlueprintGhosts.Rotate( 4, 0x10 ) );
		// 0x4D8C00: the opposite direction
		Assert.AreEqual( (byte)0x10, BlueprintGhosts.Opposite( 1 ) );
		Assert.AreEqual( (byte)1, BlueprintGhosts.Opposite( 0x10 ) );
		Assert.AreEqual( (byte)0x40, BlueprintGhosts.Opposite( 4 ) );
		Assert.AreEqual( (byte)4, BlueprintGhosts.Opposite( 0x40 ) );
		Assert.AreEqual( (byte)0, BlueprintGhosts.Opposite( 0 ) );
	}

	[TestMethod]
	public void FootprintMarksTheFirstRowWithTheFrontArrowAndTheAccessCellsWithTheirArrows()
	{
		// Row 0 is the last text row: it holds the entrance (symbol 2, a south bit) and the exit (N, a north bit).
		var shape = new ObjectShape( new[] { "***", "*2N" } );
		var (cells, directions) = BlueprintGhosts.Footprint( shape, 5, 5, 0, allowed: true );
		var byCell = cells.ToDictionary( cell => (cell.X, cell.Y), cell => cell.Marker );
		Assert.AreEqual( BlueprintMarker.Front, byCell[(5, 5)], "row 0 is the front row" );
		Assert.AreEqual( BlueprintMarker.Front, byCell[(6, 5)], "the entrance cell is in row 0 as well" );
		Assert.AreEqual( BlueprintMarker.Allowed, byCell[(5, 6)], "later rows are plain blue" );
		Assert.AreEqual( (byte)1, directions[(5, 5)], "the front arrow turns to the build direction byte" );
		// the entrance (symbol 2) opens towards -V and so does the exit N: both outside cells are in row -1
		Assert.AreEqual( BlueprintMarker.Enter, byCell[(6, 4)] );
		Assert.AreEqual( BlueprintMarker.Exit, byCell[(7, 4)] );
		Assert.AreEqual( BlueprintGhosts.Opposite( BlueprintGhosts.Rotate( 1, 0x10 ) ), directions[(6, 4)], "m_enter turns to the opposite of the rotated entrance symbol direction" );
		Assert.AreEqual( BlueprintGhosts.Opposite( BlueprintGhosts.Rotate( 1, 0x01 ) ), directions[(7, 4)], "m_exit likewise for the exit symbol" );
		Assert.AreEqual( (byte)1, directions[(6, 4)] );
		Assert.AreEqual( (byte)0x10, directions[(7, 4)] );
	}

	[TestMethod]
	public void FootprintFrontArrowFollowsTheBuildRotation()
	{
		var shape = new ObjectShape( new[] { "**", "**" } );
		foreach ( var (rotation, expected) in new[] { (0, (byte)1), (90, (byte)0x40), (180, (byte)0x10), (270, (byte)4) } )
		{
			var (cells, directions) = BlueprintGhosts.Footprint( shape, 8, 8, rotation, allowed: true );
			var front = cells.Where( cell => cell.Marker == BlueprintMarker.Front ).ToArray();
			Assert.AreEqual( 2, front.Length, $"two front-row cells at {rotation}" );
			Assert.IsTrue( front.All( cell => directions[(cell.X, cell.Y)] == expected ), $"direction byte at {rotation}" );
		}
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 90 )]
	[DataRow( 180 )]
	[DataRow( 270 )]
	public void RefusedFootprintIsAllRedWithoutArrowsAtEveryRotation( int rotation )
	{
		// The ordinary hover's failed check-only pass sets 0x81B604 before its ghost pass (0x52390E..0x5239EE).
		// Even a non-sticky NoModify refusal therefore must not become mixed-colour feedback in this route.
		var shape = new ObjectShape( new[] { "***", "*2N" } );
		var (cells, directions) = BlueprintGhosts.Footprint( shape, 5, 5, rotation, allowed: false );
		Assert.IsTrue( cells.All( cell => cell.Marker == BlueprintMarker.Refused ) );
		Assert.AreEqual( 6, cells.Count );
		Assert.AreEqual( 0, directions.Count );
	}

	/// <summary>The preview may read dimensions only, never terrain, height, ownership or other mutable host rules.</summary>
	private sealed class PreviewGrid( int width = 9, int height = 7 ) : IParkGrid
	{
		public int Width => width;
		public int Height => height;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => throw new InvalidOperationException( "A preview must not revalidate terrain." );
		public float GetCellHeight( int x, int y ) => throw new InvalidOperationException( "A marker selection must not sample height." );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 90 )]
	[DataRow( 180 )]
	[DataRow( 270 )]
	public void RefusedQueuedFootprintIncludesBothRedAccessCellsAtEveryRotation( int rotation )
	{
		var shape = new ObjectShape( new[] { "***", "*2N" } );
		var preview = BlueprintGhosts.Footprint( shape, 4, 3, rotation, false, new PreviewGrid(), true, OriginalPlacementResult.Occupied );
		Assert.AreEqual( 8, preview.Cells.Count );
		Assert.IsTrue( preview.Cells.All( cell => cell.Marker == BlueprintMarker.Refused ), "whole-placement refusal stays all red" );
		Assert.AreEqual( 0, preview.Directions.Count, "refused access cells are not arrows" );
		var points = ObjectFootprint.GetAccessPoints( shape, 4, 3, rotation ).ToArray();
		CollectionAssert.AreEqual( points.Select( point => (point.OutsideX, point.OutsideY) ).ToArray(),
			preview.Cells.Skip( 6 ).Select( cell => (cell.X, cell.Y) ).ToArray() );
	}

	[DataTestMethod]
	[DataRow( 0, 3, 0, false )]
	[DataRow( 90, 0, 4, false )]
	[DataRow( 180, 5, 6, false )]
	[DataRow( 270, 8, 2, false )]
	[DataRow( 0, 3, 4, true )]
	[DataRow( 90, 6, 4, true )]
	[DataRow( 180, 5, 2, true )]
	[DataRow( 270, 2, 2, true )]
	public void RefusedAccessBoundsFollowEntranceThenExitOrder( int rotation, int x, int y, bool entranceInBounds )
	{
		// A non-square host grid proves these checks do not assume the original 128-cell map.
		var shape = new ObjectShape( new[] { "*S*", "***", "*2*" } );
		var preview = BlueprintGhosts.Footprint( shape, x, y, rotation, false, new PreviewGrid(), true, OriginalPlacementResult.OutsideTerrain );
		Assert.AreEqual( entranceInBounds ? 10 : 9, preview.Cells.Count,
			"an off-grid entrance omits both outside markers; an off-grid exit keeps the entrance marker" );
		Assert.IsTrue( preview.Cells.All( cell => cell.Marker == BlueprintMarker.Refused ) );
		Assert.AreEqual( 0, preview.Directions.Count );
		if ( entranceInBounds )
		{
			var entrance = ObjectFootprint.GetAccessPoints( shape, x, y, rotation ).Single( point => point.Kind == ObjectCellKind.Entrance );
			Assert.AreEqual( (entrance.OutsideX, entrance.OutsideY, BlueprintMarker.Refused), preview.Cells[^1] );
		}
	}

	[DataTestMethod]
	[DataRow( 0, -1, 2 )]
	[DataRow( 90, -1, 4 )]
	[DataRow( 180, 1, 4 )]
	[DataRow( 270, 1, 2 )]
	public void PartlyOffGridFootprintKeepsTheLegacyFallback( int rotation, int x, int y )
	{
		var shape = new ObjectShape( new[] { "*S*", "***", "*2*" } );
		var before = BlueprintGhosts.Footprint( shape, x, y, rotation, false );
		var preview = BlueprintGhosts.Footprint( shape, x, y, rotation, false, new PreviewGrid(), true, OriginalPlacementResult.OutsideTerrain );
		CollectionAssert.AreEqual( before.Cells, preview.Cells );
		Assert.AreEqual( 0, preview.Directions.Count );
	}

	[DataTestMethod]
	[DataRow( '@' )]
	[DataRow( 'Q' )]
	[DataRow( '+' )]
	[DataRow( '#' )]
	[DataRow( '<' )]
	[DataRow( '>' )]
	[DataRow( 'D' )]
	public void SpecialSymbolsKeepTheLegacyRefusedFallback( char symbol )
	{
		var shape = new ObjectShape( new[] { "*S*", $"*{symbol}*", "*2*" } );
		var before = BlueprintGhosts.Footprint( shape, 3, 2, 0, false );
		var preview = BlueprintGhosts.Footprint( shape, 3, 2, 0, false, new PreviewGrid(), true, OriginalPlacementResult.Occupied );
		CollectionAssert.AreEqual( before.Cells, preview.Cells );
		Assert.AreEqual( 0, preview.Directions.Count );
	}

	[DataTestMethod]
	[DataRow( "***" )]
	[DataRow( "2**" )]
	[DataRow( "**N" )]
	[DataRow( "22N" )]
	[DataRow( "2NN" )]
	public void MissingOrMultipleAccessPointsKeepTheLegacyRefusedFallback( string row )
	{
		var shape = new ObjectShape( new[] { row } );
		var before = BlueprintGhosts.Footprint( shape, 3, 2, 0, false );
		var preview = BlueprintGhosts.Footprint( shape, 3, 2, 0, false, new PreviewGrid(), true, OriginalPlacementResult.Occupied );
		CollectionAssert.AreEqual( before.Cells, preview.Cells );
		Assert.AreEqual( 0, preview.Directions.Count );
	}

	[TestMethod]
	public void AcceptedNoQueueAndUnprovenRefusalsKeepTheirExistingPresentation()
	{
		var shape = new ObjectShape( new[] { "***", "*2N" } );
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			foreach ( var hasQueue in new[] { false, true } )
			{
				var before = BlueprintGhosts.Footprint( shape, 4, 3, rotation, true );
				var accepted = BlueprintGhosts.Footprint( shape, 4, 3, rotation, true, new PreviewGrid(), hasQueue, OriginalPlacementResult.Allowed );
				CollectionAssert.AreEqual( before.Cells, accepted.Cells );
				CollectionAssert.AreEqual( before.Directions.ToArray(), accepted.Directions.ToArray() );
			}
			var refused = BlueprintGhosts.Footprint( shape, 4, 3, rotation, false );
			var noQueue = BlueprintGhosts.Footprint( shape, 4, 3, rotation, false, new PreviewGrid(), false, OriginalPlacementResult.Occupied );
			CollectionAssert.AreEqual( refused.Cells, noQueue.Cells );
			foreach ( var reason in new[] { OriginalPlacementResult.Allowed, OriginalPlacementResult.Blocked, OriginalPlacementResult.NeedsPool } )
			{
				// Allowed placement plus a refused colour is the money/global-only fallback, not evidence for original money colours.
				var fallback = BlueprintGhosts.Footprint( shape, 4, 3, rotation, false, new PreviewGrid(), true, reason );
				CollectionAssert.AreEqual( refused.Cells, fallback.Cells );
				Assert.AreEqual( 0, fallback.Directions.Count );
			}
		}
	}

	[TestMethod]
	public void RippleReadsTheOriginalSineTable()
	{
		Assert.AreEqual( 0f, BlueprintMarkers.Ripple( 0, 0, 0 ), 1e-6f );
		Assert.AreEqual( 1f, BlueprintMarkers.Ripple( 0, MathF.PI / 2, 0 ), 1e-3f );
		Assert.AreEqual( -1f, BlueprintMarkers.Ripple( 0, 0, 3 * MathF.PI / 2 ), 1e-3f );
		// the argument is phase + x + z, so the phase and the position are interchangeable
		Assert.AreEqual( BlueprintMarkers.Ripple( 0.7f, 2, 3 ), BlueprintMarkers.Ripple( 0, 2.7f, 3 ), 1e-6f );
		// the table has 4096 entries: one full turn repeats
		Assert.AreEqual( BlueprintMarkers.Ripple( 0.3f, 1, 1 ), BlueprintMarkers.Ripple( 0.3f + 2 * MathF.PI, 1, 1 ), 2e-3f );
	}

	[TestMethod]
	public void MarkerVerticesDrapeOverTheTerrainWithTheLiftAndTheRipple()
	{
		var heights = new float[9];
		for ( var index = 0; index < heights.Length; index++ )
			heights[index] = index * 4f; // a ramp: every corner differs
		var field = new ModelHeightfield( 2, 2, 10, 10, 0, 0, Array.Empty<uint>(), heights, new uint[4] );
		var (vertices, indices) = BlueprintMarkers.BuildGeometry( field, new[] { (0, 0, BlueprintMarker.Allowed) }, null, 0.5f );
		Assert.AreEqual( 8, vertices.Length, "one flat face, both sides" );
		Assert.AreEqual( 12, indices.Length );
		var origin = OriginalParkPlacement.GetOrigin( field );
		var corners = new[] { (0, 0), (1, 0), (0, 1), (1, 1) };
		foreach ( var (cx, cy) in corners )
		{
			var expected = field.GetCornerHeight( cx, cy ) + BlueprintMarkers.Lift + BlueprintMarkers.Ripple( 0.5f, cx * 10, cy * 10 );
			var engineX = cx * 10 * OriginalParkPlacement.ModelScale - origin.X;
			Assert.IsTrue( vertices.Any( vertex => MathF.Abs( vertex.Position.X - engineX ) < 1e-4f
				&& MathF.Abs( vertex.Position.Z - expected * OriginalParkPlacement.ModelScale ) < 1e-3f ), $"corner ({cx}, {cy}) at the terrain height plus lift plus ripple" );
		}
	}

	[TestMethod]
	public void CornerOrderTurnsTheTextureByTheDirectionByte()
	{
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3 }, BlueprintMarkers.CornerOrder( 1 ) );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3 }, BlueprintMarkers.CornerOrder( 0 ) );
		CollectionAssert.AreEqual( new[] { 1, 3, 0, 2 }, BlueprintMarkers.CornerOrder( 0x40 ) );
		CollectionAssert.AreEqual( new[] { 3, 2, 1, 0 }, BlueprintMarkers.CornerOrder( 0x10 ) );
		CollectionAssert.AreEqual( new[] { 2, 0, 3, 1 }, BlueprintMarkers.CornerOrder( 4 ) );
	}

	[TestMethod]
	public void ArrowFacesUseTheRotatedTextureCoordinates()
	{
		var field = new ModelHeightfield( 2, 2, 10, 10, 0, 0, Array.Empty<uint>(), new float[9], new uint[4] );
		var directions = new Dictionary<(int X, int Y), byte> { [(0, 0)] = 0x40 };
		var (vertices, _) = BlueprintMarkers.BuildGeometry( field, new[] { (0, 0, BlueprintMarker.Front) }, directions, 0 );
		Assert.AreEqual( 6, vertices[0].TexIndex );
		Assert.AreEqual( new Vector2( 1, 0 ), vertices[0].TexCoords, "vertex 0 takes entry 1 for 0x40" );
		Assert.AreEqual( new Vector2( 1, 1 ), vertices[1].TexCoords, "vertex 1 takes entry 3" );
	}
}
