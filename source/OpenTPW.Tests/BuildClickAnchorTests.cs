using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// RIDES-021: the original placement (tp.exe 0x528A70) turns a build click into the anchor by taking half the shape
/// (truncated) off the clicked cell and then the rotation's offsets inside a 5 × 5 window. For shapes up to five cells, or
/// of odd size, the clicked cell is local (W/2, H/2) in every rotation; a wider even side moves by one cell.
/// </summary>
[TestClass]
public class BuildClickAnchorTests
{
	private static ObjectShape Shape( int width, int height ) => new( Enumerable.Repeat( new string( '*', width ), height ).ToArray() );

	[TestMethod]
	public void TheClickedCellIsLocalHalfWidthHalfHeightForSmallShapes()
	{
		foreach ( var width in Enumerable.Range( 1, 5 ) )
		{
			foreach ( var height in Enumerable.Range( 1, 5 ) )
			{
				var shape = Shape( width, height );
				foreach ( var rotation in new[] { 0, 90, 180, 270 } )
				{
					var anchor = Level.GetCentredAnchor( shape, 40, 30, rotation );
					Assert.AreEqual( (40, 30), ObjectFootprint.ToGrid( anchor.X, anchor.Y, rotation, width / 2, height / 2 ), $"{width} x {height} at {rotation}" );
				}
			}
		}
	}

	[TestMethod]
	public void EvenSidesTakeTheUpperHalfCell()
	{
		// A 4 × 4 ride clicked on (10, 10) is anchored at (8, 8): the click is local (2, 2), not the (1, 1) of a centred bounding box.
		Assert.AreEqual( (8, 8), Level.GetCentredAnchor( Shape( 4, 4 ), 10, 10, 0 ) );
		Assert.AreEqual( (8, 12), Level.GetCentredAnchor( Shape( 4, 4 ), 10, 10, 90 ) );
		Assert.AreEqual( (12, 12), Level.GetCentredAnchor( Shape( 4, 4 ), 10, 10, 180 ) );
		Assert.AreEqual( (12, 8), Level.GetCentredAnchor( Shape( 4, 4 ), 10, 10, 270 ) );
	}

	[TestMethod]
	public void WideEvenShapesFollowTheFiveCellWindow()
	{
		// W = 6: a = (5 − 6) / 2 truncates to 0, b = (5 − 1) / 2 = 2, so 90 adds (b − a, 4 − a − b) = (2, 2) to (x − 3, y).
		Assert.AreEqual( (9, 12), Level.GetCentredAnchor( Shape( 6, 1 ), 10, 10, 90 ) );
		Assert.AreEqual( (7, 10), Level.GetCentredAnchor( Shape( 6, 1 ), 10, 10, 0 ) );
	}
}
