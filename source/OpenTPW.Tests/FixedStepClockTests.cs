using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class FixedStepClockTests
{
	[TestMethod]
	public void AccumulatesPartialFramesWithoutVariableSimulationSteps()
	{
		var clock = new FixedStepClock();
		var calls = 0;
		Action<float> simulate = delta =>
		{
			Assert.AreEqual( (float)FixedStepClock.TickDuration, delta );
			++calls;
		};
		Assert.AreEqual( 0, clock.Advance( FixedStepClock.TickDuration / 2, simulate ) );
		Assert.AreEqual( 1, clock.Advance( FixedStepClock.TickDuration / 2, simulate ) );
		Assert.AreEqual( 1, calls );
		Assert.AreEqual( 1L, clock.TickCount );
		Assert.AreEqual( 0d, clock.PendingSeconds, 1e-12 );
	}

	[DataTestMethod]
	[DataRow( 30 )]
	[DataRow( 60 )]
	[DataRow( 144 )]
	[DataRow( 240 )]
	public void RenderFrameRateDoesNotChangePrototypeSimulation( int frameRate )
	{
		var clock = new FixedStepClock();
		var motion = new RideMotion();
		motion.Start();
		for ( var frame = 0; frame < frameRate * 6; ++frame )
			clock.Advance( 1d / frameRate, motion.Update );
		var reference = new RideMotion();
		reference.Start();
		for ( var tick = 0; tick < 360; ++tick )
			reference.Update( (float)FixedStepClock.TickDuration );
		Assert.AreEqual( 360L, clock.TickCount );
		Assert.AreEqual( reference.Phase, motion.Phase );
		Assert.AreEqual( reference.Height, motion.Height );
		Assert.AreEqual( 0d, clock.DroppedSeconds );
	}

	[TestMethod]
	public void LongFrameBoundsWorkAndDoesNotLeaveCatchUpBacklog()
	{
		var clock = new FixedStepClock();
		Assert.AreEqual( FixedStepClock.MaximumCatchUpTicks, clock.Advance( 10, _ => { } ) );
		Assert.AreEqual( 10 - FixedStepClock.MaximumCatchUpTicks * FixedStepClock.TickDuration, clock.DroppedSeconds, 1e-12 );
		Assert.IsTrue( clock.PendingSeconds < FixedStepClock.TickDuration );
		Assert.AreEqual( 0, clock.Advance( 0, _ => { } ) );
	}

	[TestMethod]
	public void ExtremeFiniteElapsedTimeKeepsDiagnosticsFinite()
	{
		var clock = new FixedStepClock();
		clock.Advance( double.MaxValue, _ => { } );
		clock.Advance( double.MaxValue, _ => { } );
		Assert.IsTrue( double.IsFinite( clock.DroppedSeconds ) );
		Assert.IsTrue( double.IsFinite( clock.PendingSeconds ) );
	}

	[DataTestMethod]
	[DataRow( -1d )]
	[DataRow( double.NaN )]
	[DataRow( double.PositiveInfinity )]
	[DataRow( double.NegativeInfinity )]
	public void InvalidElapsedTimeDoesNotChangeClock( double elapsed )
	{
		var clock = new FixedStepClock();
		clock.Advance( FixedStepClock.TickDuration / 2, _ => { } );
		var pending = clock.PendingSeconds;
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => clock.Advance( elapsed, _ => { } ) );
		Assert.AreEqual( pending, clock.PendingSeconds );
		Assert.AreEqual( 0L, clock.TickCount );
		Assert.AreEqual( 0d, clock.DroppedSeconds );
	}

	[TestMethod]
	public void ResetClearsTicksRemainderAndDroppedTime()
	{
		var clock = new FixedStepClock();
		clock.Advance( 10, _ => { } );
		clock.Reset();
		Assert.AreEqual( 0L, clock.TickCount );
		Assert.AreEqual( 0d, clock.PendingSeconds );
		Assert.AreEqual( 0d, clock.DroppedSeconds );
		Assert.ThrowsException<ArgumentNullException>( () => clock.Advance( 1, null! ) );
	}
}
