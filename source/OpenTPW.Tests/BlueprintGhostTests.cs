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

	[TestMethod]
	public void RefusedFootprintIsAllRedWithoutArrows()
	{
		var shape = new ObjectShape( new[] { "***", "*2N" } );
		var (cells, directions) = BlueprintGhosts.Footprint( shape, 5, 5, 0, allowed: false );
		Assert.IsTrue( cells.All( cell => cell.Marker == BlueprintMarker.Refused ) );
		Assert.AreEqual( 6, cells.Count );
		Assert.AreEqual( 0, directions.Count );
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
