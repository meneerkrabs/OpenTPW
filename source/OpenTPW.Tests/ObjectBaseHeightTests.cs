using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>An object's base height is the midpoint of the lowest and highest terrain vertex under its footprint (tp.exe 0x00466D70; the oracle pins it: <c>ObjectSkirtGoldenTests.TheBaseHeightIsWhatTheOriginalReturned</c>).</summary>
[TestClass]
public class ObjectBaseHeightTests
{
	/// <summary>A grid whose vertex (x, y) stands at <c>height(x, y)</c>.</summary>
	private sealed class VertexGrid : IParkGrid
	{
		private readonly Func<int, int, float> height;
		public VertexGrid( Func<int, int, float> height ) => this.height = height;
		public int Width => 64;
		public int Height => 64;
		public System.Numerics.Vector2 Origin => System.Numerics.Vector2.Zero;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => (height( x, y ) + height( x + 1, y ) + height( x, y + 1 ) + height( x + 1, y + 1 )) / 4;
		public float GetCornerHeight( int x, int y ) => height( x, y );
	}

	[TestMethod]
	public void ItIsTheMidpointOfTheLowestAndHighestVertex()
	{
		var shape = new ObjectShape( new[] { "**", "**" } );
		var grid = new VertexGrid( ( x, y ) => x == 10 && y == 20 ? 8 : x == 12 && y == 22 ? -2 : 1 );
		Assert.AreEqual( 3f, ParkObjects.GetBaseHeight( shape, grid, 10, 20, 0 ), "the corners of the 2 x 2 cells span the vertices (10..12, 20..22): (8 + -2) / 2" );
		Assert.AreEqual( 1f, ParkObjects.GetBaseHeight( shape, grid, 30, 30, 0 ), "a flat patch gives its height" );
	}

	[TestMethod]
	public void OnlyShapeCellsWithANonZeroTypeNibbleCount()
	{
		// '#' is type 16 (low nibble 0) and '.' is free: neither samples a vertex; '*' (4) and '+' (11) do
		var grid = new VertexGrid( ( x, y ) => x == 5 && y == 5 ? 40 : x == 7 && y == 5 ? 100 : 0 );
		Assert.AreEqual( 20f, ParkObjects.GetBaseHeight( new ObjectShape( new[] { "*#" } ), grid, 5, 5, 0 ), "'#' adds nothing: the '*' cell spans vertices 5..6 and holds 40 and 0" );
		Assert.AreEqual( 50f, ParkObjects.GetBaseHeight( new ObjectShape( new[] { "*+" } ), grid, 5, 5, 0 ), "a '+' cell counts: its corner (7, 5) is 100" );
	}

	[TestMethod]
	public void TheRotationTurnsTheFootprintAboutTheAnchorCorner()
	{
		var shape = new ObjectShape( new[] { "***" } );
		for ( var rotation = 0; rotation < 360; rotation += 90 )
		{
			var cells = ObjectFootprint.GetCells( shape, 20, 20, rotation ).ToList();
			var expected = new HashSet<(int, int)>( cells.SelectMany( cell => new[] { (cell.X, cell.Y), (cell.X + 1, cell.Y), (cell.X, cell.Y + 1), (cell.X + 1, cell.Y + 1) } ) );
			var seen = new HashSet<(int, int)>();
			var grid = new VertexGrid( ( x, y ) =>
			{
				seen.Add( (x, y) );
				return 0;
			} );
			ParkObjects.GetBaseHeight( shape, grid, 20, 20, rotation );
			CollectionAssert.AreEquivalent( expected.ToArray(), seen.ToArray(), $"rotation {rotation}: the vertices are the corners of the footprint cells" );
		}
	}

	/// <summary>The vertex of a shape corner as 0x454050 computes it (float arithmetic, ftol(lerp + 0.1)), for a box turned by the angle 0x466F90 gives.</summary>
	private static (int X, int Y) OriginalVertex( int angle, int x, int y, int width, int height, float a, float b )
	{
		// the box origin of 0x466F90 and its size (width and height swap for 90 and 270)
		int x0 = x, y0 = y;
		switch ( angle )
		{
			case 90: y0 = y + 1 - width; break;
			case 180: x0 = x + 1 - width; y0 = y + 1 - height; break;
			case 270: x0 = x + 1 - height; break;
		}
		var (sizeX, sizeY) = angle is 0 or 180 ? (width, height) : (height, width);
		float fx0 = x0, fy0 = y0, fx1 = x0 + sizeX, fy1 = y0 + sizeY;
		float Lerp( float t, float from, float to ) => (1f - t) * from + t * to;
		var (ox, oy) = angle switch
		{
			0 => (Lerp( a, fx0, fx1 ), Lerp( b, fy0, fy1 )),
			90 => (Lerp( b, fx0, fx1 ), Lerp( a, fy1, fy0 )),
			180 => (Lerp( a, fx1, fx0 ), Lerp( b, fy1, fy0 )),
			_ => (Lerp( b, fx1, fx0 ), Lerp( a, fy0, fy1 ))
		};
		return ((int)(ox + 0.1f), (int)(oy + 0.1f));
	}

	[TestMethod]
	public void TheVerticesEqualTheOriginalsCornerFormulaAtEveryRotation()
	{
		foreach ( var rows in new[] { new[] { "*8*", "***" }, new[] { "***", "*.*", "N**" }, new[] { "*" }, new[] { "****", "**+*" } } )
		{
			var shape = new ObjectShape( rows );
			for ( var rotation = 0; rotation < 360; rotation += 90 )
			{
				foreach ( var cell in shape.Cells )
				{
					for ( var corner = 0; corner < 4; corner++ )
					{
						var point = ObjectFootprint.ToGrid( 17, 23, rotation, new System.Numerics.Vector2( cell.U + (corner & 1), cell.V + (corner >> 1) ) );
						var original = OriginalVertex( rotation, 17, 23, shape.Width, shape.Height, (cell.U + (corner & 1)) / (float)shape.Width, (cell.V + (corner >> 1)) / (float)shape.Height );
						Assert.AreEqual( original, ((int)MathF.Round( point.X ), (int)MathF.Round( point.Y )), $"{string.Join( "/", rows )} rotation {rotation} cell ({cell.U}, {cell.V}) corner {corner}" );
					}
				}
			}
		}
	}

	[TestMethod]
	public void AnObjectRestoredFromAnOlderSaveKeepsTheCellMeanAndIsSavedWithoutTheRule()
	{
		var entry = new ObjectCatalogEntry( "test", ObjectCategory.Ride, "test", new ObjectSettings( Array.Empty<ObjectSettingsFile>() ),
			new ObjectShape( new[] { "**", "**" } ), "model", null, null, Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(),
			Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( System.IO.Path.GetTempPath() ), null );
		var objects = new ParkObjects( null!, new VertexGrid( ( x, y ) => x == 10 && y == 20 ? 8 : 1 ) );
		var current = objects.ComputePlacement( entry, 10, 20, 0 );
		Assert.AreEqual( (4.5f, 1, false), (current.BaseHeight, ParkLayoutSnapshot.BaseHeightRuleOf( current ), current.LegacyBaseHeight), "the vertex rule: (8 + 1) / 2, saved with rule 1" );
		var legacy = objects.ComputePlacement( entry, 10, 20, 0, legacyBaseHeight: true );
		Assert.AreEqual( objects.GetLegacyBaseHeight( entry, 10, 20, 0 ), legacy.BaseHeight, "a save without the rule restores the cell mean" );
		Assert.AreNotEqual( current.BaseHeight, legacy.BaseHeight, "the two rules differ on a slope, so the old height must be kept to restore at all" );
		Assert.IsNull( ParkLayoutSnapshot.BaseHeightRuleOf( legacy ), "the next save of that object writes no rule, so it restores with the cell mean again instead of failing on the new rule's height" );
	}
}
