using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using Veldrid;

namespace OpenTPW.Tests;

[TestClass]
public class MapRepairPaletteTests
{
	// Independent rational/floor reference: no signed shifts or production colour helper.
	private static RgbaByte Reference( int value )
	{
		var v = Math.Clamp( value, 0, 100 );
		var upper = v >= 50;
		var distance = upper ? v - 50 : v;
		var fraction = decimal.Floor( distance * 65536m / 50m ) / 65536m;
		byte Channel( int start, int end ) => (byte)(start + decimal.Floor( (end - start) * fraction ));
		return new RgbaByte( Channel( 248, upper ? 48 : 248 ), Channel( upper ? 200 : 0, upper ? 252 : 200 ), 0, 255 );
	}

	[TestMethod]
	public void EveryIntegerAndClampedBoundaryMatchesIndependentRationalFloor()
	{
		for ( var value = -20; value <= 280; value++ )
			Assert.AreEqual( Reference( value ), ParkMapPalette.RepairRamp( value ), $"repair ramp {value}" );
		Assert.AreEqual( Reference( 0 ), ParkMapPalette.RepairRamp( int.MinValue ) );
		Assert.AreEqual( Reference( 100 ), ParkMapPalette.RepairRamp( int.MaxValue ) );
	}

	[DataTestMethod]
	[DataRow( -1, 248, 0 )]
	[DataRow( 0, 248, 0 )]
	[DataRow( 1, 248, 3 )]
	[DataRow( 25, 248, 100 )]
	[DataRow( 49, 248, 195 )]
	[DataRow( 50, 248, 200 )]
	[DataRow( 51, 244, 201 )]
	[DataRow( 75, 148, 226 )]
	[DataRow( 99, 52, 250 )]
	[DataRow( 100, 48, 252 )]
	[DataRow( 101, 48, 252 )]
	public void TracedRampVectorsKeepFixedPointRoundingAndOpaqueAlpha( int value, int red, int green ) =>
		Assert.AreEqual( new RgbaByte( (byte)red, (byte)green, 0, 255 ), ParkMapPalette.RepairRamp( value ) );

	[DataTestMethod]
	[DataRow( 0.0, 0 )]
	[DataRow( -0.99, 0 )]
	[DataRow( 1.99, 1 )]
	[DataRow( 49.99, 49 )]
	[DataRow( 49.9999999, 50 )]
	[DataRow( 50.9999999, 51 )]
	[DataRow( 99.9999999, 100 )]
	[DataRow( 100.99, 100 )]
	[DataRow( 255.99, 255 )]
	[DataRow( 256.99, 0 )]
	[DataRow( 306.99, 50 )]
	[DataRow( -1.99, 255 )]
	[DataRow( -256.99, 0 )]
	[DataRow( -257.99, 255 )]
	[DataRow( 16777217.0, 0 )]
	[DataRow( 16777219.0, 4 )]
	[DataRow( 2147483520.0, 128 )]
	[DataRow( -2147483648.0, 0 )]
	[DataRow( -2147483649.0, 0 )]
	public void StoredFloatTruncatesBeforeLowByteExtractionAndRampClamp( double repair, int lowByte ) =>
		Assert.AreEqual( (RgbaByte?)Reference( lowByte ), ParkMapPalette.RepairFill( repair ), $"stored float repair {repair:R}" );

	[TestMethod]
	public void NonfiniteAndOutOfRangeStoredFloatsHaveNoRepairColour()
	{
		foreach ( var repair in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, double.MinValue, double.MaxValue,
			(double)float.MinValue, (double)float.MaxValue, 2147483647d, 2147483648d, -2147483904d } )
			Assert.IsNull( ParkMapPalette.RepairFill( repair ), $"malformed repair {repair:R}" );
		Assert.IsNull( ParkMapPalette.RepairFill( (float)int.MaxValue ), "a float comparison against int.MaxValue would round the bound up and allow an overflowing cast" );
	}
}
