using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>
/// The park map's anti-aliased line against tp.exe's own routine 0x5EFB10 run in the x86 oracle (docs/reverse/ORACLE.md): every case of <see cref="MapLineGolden"/> is a segment and a clip rectangle on a
/// synthetic surface, and <see cref="ParkMapLine.Coverage"/> must report the executable's pixel writes in the same order with the same coverage bytes (the zero coverage writes included);
/// every blend row is the executable's own blend of one pixel, which <see cref="ParkMapLine.Blend"/> must reproduce bit for bit.
/// </summary>
[TestClass]
public class MapLineTests
{
	private static string Run( MapLineGolden.Case golden )
	{
		var text = new StringBuilder();
		ParkMapLine.Coverage( golden.X0, golden.Y0, golden.X1, golden.Y1, golden.ClipLeft, golden.ClipTop, golden.ClipRight, golden.ClipBottom,
			( x, y, coverage ) =>
			{
				if ( text.Length > 0 )
					text.Append( ' ' );
				text.Append( x ).Append( ',' ).Append( y ).Append( ',' ).Append( coverage );
			} );
		return text.ToString();
	}

	[TestMethod]
	public void EveryCaseOfTheOraclesMapLineIsReproducedWriteForWrite()
	{
		Assert.IsTrue( MapLineGolden.Cases.Length >= 60 );
		foreach ( var golden in MapLineGolden.Cases )
			Assert.AreEqual( golden.Writes, Run( golden ), $"{golden.Name}: ({golden.X0}, {golden.Y0}) to ({golden.X1}, {golden.Y1}), clip {golden.ClipLeft}, {golden.ClipTop}, {golden.ClipRight}, {golden.ClipBottom}" );
	}

	[TestMethod]
	public void TheGoldenCasesCoverEveryOctantAndTheClipAndZeroLengthEdges()
	{
		var directions = new HashSet<(int, int, bool)>();
		var zeroLength = 0;
		var clipped = 0;
		var empty = 0;
		foreach ( var golden in MapLineGolden.Cases )
		{
			var dx = golden.X1 - golden.X0;
			var dy = golden.Y1 - golden.Y0;
			if ( dx == 0 && dy == 0 )
				zeroLength++;
			else
				directions.Add( (Math.Sign( dx ), Math.Sign( dy ), Math.Abs( dx ) >= Math.Abs( dy )) );
			if ( golden.ClipLeft > 0 || golden.ClipTop > 0 || golden.ClipRight < 128 || golden.ClipBottom < 128 )
				clipped++;
			if ( golden.Writes.Length == 0 )
				empty++;
		}

		// both axes as the major one, all four sign combinations of a diagonal-ish line
		Assert.IsTrue( directions.Contains( (1, 1, true) ) && directions.Contains( (-1, 1, true) ) && directions.Contains( (1, -1, true) ) && directions.Contains( (-1, -1, true) ) );
		Assert.IsTrue( directions.Contains( (1, 1, false) ) && directions.Contains( (-1, 1, false) ) && directions.Contains( (1, -1, false) ) && directions.Contains( (-1, -1, false) ) );
		Assert.IsTrue( zeroLength >= 1 );
		Assert.IsTrue( clipped >= 10 );
		Assert.IsTrue( empty >= 2 );
	}

	[TestMethod]
	public void ZeroLengthAndEmptyClipDrawNothingAndTheEndPointIsNotDrawn()
	{
		var count = 0;
		ParkMapLine.Coverage( 5, 5, 5, 5, 0, 0, 128, 128, ( x, y, c ) => count++ );
		Assert.AreEqual( 0, count );

		// a one pixel step east: the start pixel (255) and its two neighbours at exactly 1.0, nothing at x = 6
		var writes = new List<(int, int, int)>();
		ParkMapLine.Coverage( 5, 5, 6, 5, 0, 0, 128, 128, ( x, y, c ) => writes.Add( (x, y, c) ) );
		CollectionAssert.AreEqual( new[] { (5, 5, 255), (5, 6, 0), (5, 4, 0) }, writes );
	}

	[TestMethod]
	public void EveryBlendOfTheOraclesRoutineIsReproducedBitForBit()
	{
		Assert.IsTrue( MapLineGolden.Blends.Length >= 30 );
		foreach ( var row in MapLineGolden.Blends )
		{
			var actual = ParkMapLine.Blend( row.Source, row.Old, row.Coverage, 0x00FF0000u, 0x0000FF00u, 0x000000FFu, row.AlphaMask );
			Assert.AreEqual( row.New, actual, $"source {row.Source:X8} over {row.Old:X8} with coverage {row.Coverage} and alpha mask {row.AlphaMask:X8}: expected {row.New:X8}, got {actual:X8}" );
		}
	}
}
