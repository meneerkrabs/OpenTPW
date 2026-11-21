using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public sealed class ThemeParkIncStormTests
{
	// Captured by tpi_storm_timer_oracle.py; the coordinate, drawing and audio callbacks are synthetic.
	[DataTestMethod]
	[DataRow(false, 19, 3, 0, 19, 0, -1, 11, 12, "")]
	[DataRow(false, 19, 3, 1, 19, 0, 0, 11, 12, "T")]
	[DataRow(false, 19, 3, 2, 19, 0, 1, 11, 12, "")]
	[DataRow(true, 11, 3, 2, 1, 3, 1, 11, 12, "")]
	[DataRow(true, 10, 3, 2, 0, 3, 1, 11, 12, "")]
	[DataRow(true, 9, 3, 2, 17, 4, 1, 11, 12, "GSR")]
	[DataRow(true, 9, 3, 0, 29, 4, 7, 22, 32, "GSRR")]
	[DataRow(true, 9, 10, -1, 29, 11, 0, 22, 32, "GSRRT")]
	[DataRow(true, 9, 11, -1, 29, 12, 0, 22, 32, "GSRRT")]
	public void TimersAndThunderPositionMatchTheNativeTurn( bool storm, int lightning, int bolts, int countdown,
		int nextLightning, int nextBolts, int nextCountdown, int x, int z, string events )
	{
		var previousPosition = new Vector3( 11, 0, 12 );
		var strike = new WeatherStrike( new( 22, 300, 32 ), new( 21, 0, 31 ) );
		var draws = new Queue<uint>( new uint[] { 17, 29 } );
		var calls = "";
		var step = ThemeParkIncStorm.Step( new( lightning, bolts, countdown, previousPosition ), storm, 10, 500, 10, 2,
			() => { calls += "G"; return strike; }, () => { calls += "R"; return draws.Dequeue(); } );

		var position = new Vector3( x, x == 22 ? 300 : 0, z );
		Assert.AreEqual( new ThemeParkIncStormState( nextLightning, nextBolts, nextCountdown, position ), step.State );
		Assert.AreEqual<WeatherStrike?>( events.Contains( 'S' ) ? strike : null, step.Strike );
		Assert.AreEqual<Vector3?>( events.Contains( 'T' ) ? position : null, step.Thunder );
		Assert.AreEqual( events.Replace( "S", "" ).Replace( "T", "" ), calls );
	}

	[DataTestMethod]
	[DataRow(true, 128, 128, 0x00000000u, 0x00000000u, 0x00000000u, 0x00000000u, 0x3F800000u, 0x3F800000u, 0xC2480000u, 0xC2480000u)]
	[DataRow(true, 128, 128, 0xFFFFFFFFu, 0x7FFFFFFFu, 0xBFFFFFFFu, 0x3FFFFFFFu, 0x44A00000u, 0x44A00000u, 0x44A64000u, 0x44A64000u)]
	[DataRow(true, 127, 93, 0x3AB42777u, 0x34D52517u, 0x01234567u, 0x23456789u, 0x44919CDEu, 0x443FEE50u, 0x448B95C2u, 0x4441356Cu)]
	[DataRow(true, 128, 128, 0x3FFFFFFFu, 0x00000001u, 0x3FFFFFFFu, 0x00000001u, 0x44A00000u, 0x3F800000u, 0x44A64000u, 0xC2480000u)]
	[DataRow(false, 128, 128, 0x00000000u, 0x00000000u, 0x00000000u, 0x00000000u, 0x3F800000u, 0x3F800000u, 0xC2480000u, 0xC2480000u)]
	[DataRow(false, 128, 128, 0xFFFFFFFFu, 0x7FFFFFFFu, 0xBFFFFFFFu, 0x3FFFFFFFu, 0x44A00000u, 0x44A00000u, 0x44A64000u, 0x44A64000u)]
	[DataRow(false, 127, 93, 0x3AB42777u, 0x34D52517u, 0x01234567u, 0x23456789u, 0x44919CDEu, 0x443FEE51u, 0x448B95C2u, 0x4441356Du)]
	[DataRow(false, 128, 128, 0x3FFFFFFFu, 0x00000001u, 0x3FFFFFFFu, 0x00000001u, 0x44A00000u, 0x3F800000u, 0x44A64000u, 0xC2480000u)]
	public void StrikeGeometryMatchesTheNativeCoordinates( bool singlePrecision, int width, int height,
		uint a, uint b, uint c, uint d, uint bottomX, uint bottomZ, uint topX, uint topZ )
	{
		var draws = new Queue<uint>( new[] { a, b, c, d } );
		var strike = ThemeParkIncStorm.Geometry( width, height, singlePrecision, draws.Dequeue );
		Assert.AreEqual( (bottomX, bottomZ, topX, topZ), (BitConverter.SingleToUInt32Bits( strike.Bottom.X ),
			BitConverter.SingleToUInt32Bits( strike.Bottom.Z ), BitConverter.SingleToUInt32Bits( strike.Top.X ), BitConverter.SingleToUInt32Bits( strike.Top.Z )) );
		Assert.AreEqual( (0f, 300f), (strike.Bottom.Y, strike.Top.Y) );
		Assert.AreEqual( 0, draws.Count );
	}

	[TestMethod]
	public void AStormTurnDrawsFromTheWorldAndItsStateStaysInTheSave()
	{
		var settings = new ThemeParkIncWeatherSettings( new[] { 0, 0, 0, 25, 0, 30, 0, 0, 20, 0, 0, 0, 0, 0, 15, 0 }, new[] { 4, 0, 0 }, 3,
			(40, 95), (95, 100), (0, 20), (0, 40), (0, 15), 20, 600, 10, 500, 2000, 10, 2 );
		var weather = new ThemeParkIncWeatherSpells( settings ) { MapCells = (128, 128) };
		weather.Restore( new ThemeParkIncWeatherSnapshot( new( 3, 3, 10, 1 ), new( 3, 3, 10, 1 ), false, 1, 0 ) );
		var draws = new Queue<uint>( new uint[] { 0u, 0x3FFFFFFFu, 0u, 0x3FFFFFFFu, 17u, 23u } );
		// The lightning timer starts at zero, so the first turn is strictly negative after the step and strikes.
		weather.OnTurn( draws.Dequeue );
		var strike = AssertSingle( weather.TakeStrikes() );
		Assert.AreEqual( 300f, strike.Top.Y );
		Assert.AreEqual( 0, draws.Count, "the turn takes the geometry's four words, the thunder countdown's and the re-arm's" );
		var restored = new ThemeParkIncWeatherSpells( settings );
		restored.Restore( weather.Capture() );
		Assert.AreEqual( 0, restored.TakeStrikes().Count, "strikes are presentation and are not saved" );
		Assert.AreEqual( weather.Capture(), restored.Capture(), "the storm's timers and thunder position survive a save" );
	}

	private static WeatherStrike AssertSingle( IReadOnlyList<WeatherStrike> strikes )
	{
		Assert.AreEqual( 1, strikes.Count );
		return strikes[0];
	}
}
