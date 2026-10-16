using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using Veldrid;

namespace OpenTPW.Tests;

/// <summary>The buy window's footprint thumbnail (tp.exe 0x4ab1b0, the classes 0x52c5b0; UI-026).</summary>
[TestClass]
public class BuyFootprintTests
{
	[TestMethod]
	public void CellTypesGetTheOriginalClassColours()
	{
		var orange = new RgbaByte( 255, 170, 30, 128 );
		Assert.AreEqual( orange, ParkHud.BuyFootprintColor( 4 ), "'*' occupied" );
		Assert.AreEqual( orange, ParkHud.BuyFootprintColor( 1 ), "'@'" );
		Assert.AreEqual( orange, ParkHud.BuyFootprintColor( 23 ), "track ends" );
		Assert.AreEqual( new RgbaByte( 50, 220, 15, 128 ), ParkHud.BuyFootprintColor( 9 ), "entrances are green" );
		Assert.AreEqual( new RgbaByte( 15, 100, 220, 128 ), ParkHud.BuyFootprintColor( 10 ), "exits are blue" );
		foreach ( var other in new[] { 0, 3, 11, 16 } )
			Assert.IsNull( ParkHud.BuyFootprintColor( other ), $"type {other} draws nothing" );
	}

	[TestMethod]
	public void TheShapeIsDrawnFromTheBottomRowUpAndCutOffAtEightCells()
	{
		var cells = ParkHud.BuyFootprintCells( new ObjectShape( new[] { "*.N", "*2*" } ) );
		var byCell = cells.ToDictionary( cell => (cell.Column, cell.Row), cell => cell.Color );
		Assert.AreEqual( 5, byCell.Count, "the free cell draws nothing" );
		Assert.AreEqual( new RgbaByte( 50, 220, 15, 128 ), byCell[(1, 0)], "the entrance sits in the last text row, row 0 at the bottom" );
		Assert.AreEqual( new RgbaByte( 15, 100, 220, 128 ), byCell[(2, 1)] );
		Assert.AreEqual( 8, ParkHud.BuyFootprintCells( new ObjectShape( new[] { "**********" } ) ).Count, "ten columns are cut off at the bitmap's eight" );
		Assert.AreEqual( 0, ParkHud.BuyFootprintCells( null ).Count );
	}
}
