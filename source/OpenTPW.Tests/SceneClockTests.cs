using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SceneClockTests
{
	[TestMethod]
	public void TheClockGainsScaledTimeAndReadsWholeMilliseconds()
	{
		var clock = new SceneClock();
		clock.Advance( 0.0105 );
		Assert.AreEqual( 10u, clock.Milliseconds, "the accumulator is truncated" );
		clock.Advance( 0.0105 );
		Assert.AreEqual( 21u, clock.Milliseconds, "the fraction carries over in the accumulator" );
		Assert.AreEqual( 11u, clock.FrameMilliseconds );
	}

	[TestMethod]
	public void DoubleSpeedDoublesTheClockAndPauseFreezesIt()
	{
		SceneClock Run( float scale )
		{
			var clock = new SceneClock();
			for ( var frame = 0; frame < 60; frame++ )
				clock.Advance( 1.0 / 60 * scale );
			return clock;
		}
		Assert.AreEqual( 1000u, Run( 1 ).Milliseconds, 1u );
		Assert.AreEqual( 2000u, Run( 2 ).Milliseconds, 2u );
		var paused = Run( 1 );
		var frozen = paused.Milliseconds;
		paused.Advance( 0 );
		Assert.AreEqual( (frozen, 0u), (paused.Milliseconds, paused.FrameMilliseconds) );
		paused.Advance( double.NaN );
		paused.Advance( -1 );
		Assert.AreEqual( frozen, paused.Milliseconds );
	}

	[TestMethod]
	public void TheCappedFrameTimeStopsAtHalfASecond()
	{
		var clock = new SceneClock();
		clock.Advance( 2.0 );
		Assert.AreEqual( 0.5f, clock.CappedFrameSeconds );
		clock.Advance( 0.016 );
		Assert.AreEqual( 0.016f, clock.CappedFrameSeconds, 1e-6f );
	}
}
