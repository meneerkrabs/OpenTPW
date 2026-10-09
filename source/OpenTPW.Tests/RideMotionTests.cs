using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace OpenTPW.Tests;

[TestClass]
public class RideMotionTests
{
	[TestMethod]
	public void StartsStoppedAtRestAndDoesNotAdvanceUntilStarted()
	{
		var motion = new RideMotion();
		motion.Update( 1 );
		Assert.IsFalse( motion.IsRunning );
		Assert.AreEqual( 0d, motion.ElapsedSeconds );
		Assert.AreEqual( 0d, motion.Phase );
		Assert.AreEqual( 0f, motion.Height );
	}

	[TestMethod]
	public void StartIsIdempotentWithoutResettingAnActiveCycle()
	{
		var motion = new RideMotion();
		motion.Start();
		motion.Update( 2 );
		motion.Start();
		Assert.IsTrue( motion.IsRunning );
		Assert.AreEqual( 2d, motion.ElapsedSeconds );
		Assert.AreEqual( 0.25d, motion.Phase );
		Assert.AreEqual( 3f, motion.Height, 0.0001f );
	}

	[TestMethod]
	public void StopIsIdempotentReturnsToRestAndRestartBeginsANewCycle()
	{
		var motion = new RideMotion();
		motion.Start();
		motion.Update( 4 );
		Assert.AreEqual( 6f, motion.Height, 0.0001f );
		motion.Stop();
		motion.Stop();
		motion.Update( 3 );
		Assert.IsFalse( motion.IsRunning );
		Assert.AreEqual( 0d, motion.ElapsedSeconds );
		Assert.AreEqual( 0d, motion.Phase );
		Assert.AreEqual( 0f, motion.Height );
		motion.Start();
		motion.Update( 2 );
		Assert.AreEqual( 0.25d, motion.Phase );
		Assert.AreEqual( 3f, motion.Height, 0.0001f );
	}

	[TestMethod]
	public void CycleRaisesAndLowersCarriageWithoutNegativeHeight()
	{
		var motion = new RideMotion();
		motion.Start();
		for ( var step = 0; step < 256; ++step )
		{
			motion.Update( 0.125f );
			Assert.IsTrue( motion.Phase >= 0 && motion.Phase < 1 );
			Assert.IsTrue( motion.Height >= 0 && motion.Height <= motion.MaximumHeight );
		}
		Assert.AreEqual( 32d, motion.ElapsedSeconds );
		Assert.AreEqual( 0d, motion.Phase );
		Assert.AreEqual( 0f, motion.Height );
	}

	[TestMethod]
	public void FramePartitioningDoesNotChangeTheMotion()
	{
		var singleStep = new RideMotion();
		var multipleSteps = new RideMotion();
		singleStep.Start();
		multipleSteps.Start();
		singleStep.Update( 5 );
		for ( var step = 0; step < 40; ++step )
			multipleSteps.Update( 0.125f );
		Assert.AreEqual( singleStep.Phase, multipleSteps.Phase, 0.000001d );
		Assert.AreEqual( singleStep.Height, multipleSteps.Height, 0.0001f );
	}

	[TestMethod]
	public void ZeroAndLargeFiniteDeltasAreSafe()
	{
		var motion = new RideMotion();
		motion.Start();
		motion.Update( 0 );
		Assert.AreEqual( 0d, motion.Phase );
		motion.Update( 1000002 );
		Assert.AreEqual( 0.25d, motion.Phase );
		Assert.AreEqual( 3f, motion.Height, 0.0001f );
		motion.Update( float.MaxValue );
		Assert.AreEqual( 0.25d, motion.Phase );
		Assert.IsTrue( double.IsFinite( motion.Phase ) );
		Assert.IsTrue( float.IsFinite( motion.Height ) );
		Assert.IsTrue( motion.Height >= 0 && motion.Height <= motion.MaximumHeight );
	}

	[DataTestMethod]
	[DataRow( -1f )]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	[DataRow( float.NegativeInfinity )]
	public void InvalidDeltasAreRejectedWithoutChangingState( float deltaTime )
	{
		var motion = new RideMotion();
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => motion.Update( deltaTime ) );
		motion.Start();
		motion.Update( 2 );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => motion.Update( deltaTime ) );
		Assert.IsTrue( motion.IsRunning );
		Assert.AreEqual( 2d, motion.ElapsedSeconds );
		Assert.AreEqual( 0.25d, motion.Phase );
		Assert.AreEqual( 3f, motion.Height, 0.0001f );
	}

	[DataTestMethod]
	[DataRow( 0f )]
	[DataRow( -1f )]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	[DataRow( float.NegativeInfinity )]
	public void InvalidMotionConfigurationIsRejected( float value )
	{
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => new RideMotion( maximumHeight: value ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => new RideMotion( cycleDuration: value ) );
	}
}
