using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

[TestClass]
public class EyeModeStaticTransitionTests
{
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void EitherObservedBooleanEdgeResetsExactlyThreeHundredMilliseconds( bool initial )
	{
		var timer = new EyeModeStaticTransition( initial );
		timer.Prepare( initial, 100, 1 );
		Assert.IsFalse( timer.ShowStatic );
		timer.Prepare( !initial, 1000, 2 );
		Assert.IsTrue( timer.ShowStatic ); Assert.AreEqual( 300d, timer.RemainingMilliseconds );
		timer.Prepare( !initial, 1100, 3 );
		Assert.AreEqual( 200d, timer.RemainingMilliseconds );
		timer.Prepare( initial, 1150, 4 );
		Assert.AreEqual( 300d, timer.RemainingMilliseconds, "an unfinished edge replaces, rather than adds to, the budget" );
		Assert.AreEqual( 0.3f, ParkHud.RideStaticSeconds );
	}

	[DataTestMethod]
	[DataRow( 299d, 1d )]
	[DataRow( 300d, 0d )]
	[DataRow( 301d, -1d )]
	[DataRow( 5000d, -4700d )]
	public void IncomingPositiveBudgetLatchesBeforeChargingTheInterval( double elapsed, double left )
	{
		var timer = new EyeModeStaticTransition( false );
		timer.Prepare( true, 100, 1 );
		timer.Prepare( true, 100 + elapsed, 2 );
		Assert.IsTrue( timer.ShowStatic, "even a large elapsed interval cannot erase the current static frame" );
		Assert.AreEqual( left, timer.RemainingMilliseconds ); Assert.AreEqual( 2, timer.NoiseFrame );
		timer.Prepare( true, 100 + elapsed, 3 );
		Assert.AreEqual( left > 0, timer.ShowStatic ); Assert.AreEqual( left, timer.RemainingMilliseconds );
	}

	[TestMethod]
	public void UnchangedBooleanDoesNotRestartAndUnobservedIntermediateModesAreNotInvented()
	{
		var timer = new EyeModeStaticTransition( false );
		timer.Prepare( true, 100, 1 );
		// Ride -> walk, or leave and reenter entirely between observations: only the final true is visible.
		timer.Prepare( true, 200, 2 ); Assert.AreEqual( 200d, timer.RemainingMilliseconds );
		timer.Prepare( true, 400, 3 ); Assert.IsTrue( timer.ShowStatic );
		timer.Prepare( true, 500, 4 ); Assert.IsFalse( timer.ShowStatic );
		timer.Prepare( true, 9000, 5 ); Assert.AreEqual( 0d, timer.RemainingMilliseconds );
		timer.Prepare( false, 10000, 6 ); Assert.AreEqual( 300d, timer.RemainingMilliseconds );
	}

	[TestMethod]
	public void SkippedFramesPreserveObservationAndStampUntilEligibleResume()
	{
		var timer = new EyeModeStaticTransition( false );
		timer.Prepare( true, 100, 7 ); timer.SkipFrame(); timer.SkipFrame();
		Assert.IsFalse( timer.ShowStatic ); Assert.AreEqual( 300d, timer.RemainingMilliseconds ); Assert.AreEqual( 7, timer.NoiseFrame );
		timer.Prepare( true, 2100, 8 );
		Assert.IsTrue( timer.ShowStatic ); Assert.AreEqual( -1700d, timer.RemainingMilliseconds );
		timer.Prepare( true, 2200, 9 ); Assert.IsFalse( timer.ShowStatic );
		timer.SkipFrame(); timer.Prepare( false, 6000, 10 );
		Assert.IsTrue( timer.ShowStatic ); Assert.AreEqual( 300d, timer.RemainingMilliseconds, "a newly observed edge starts its own budget" );
	}

	[TestMethod]
	public void HostClockRejectsInvalidOrBackwardSamplesWithoutExtendingTheBudget()
	{
		var timer = new EyeModeStaticTransition( false ); timer.Prepare( true, 100, 0 );
		foreach ( var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d, 90d } )
		{
			timer.Prepare( true, invalid, 1 );
			Assert.AreEqual( 300d, timer.RemainingMilliseconds ); Assert.IsTrue( timer.ShowStatic );
		}
		timer.Prepare( true, 200, 2 ); Assert.AreEqual( 200d, timer.RemainingMilliseconds );
	}

	[TestMethod]
	public void InstancesAndAlreadyActiveInitialStateAreIndependent()
	{
		var first = new EyeModeStaticTransition( false ); var second = new EyeModeStaticTransition( true );
		first.Prepare( true, 100, 1 ); second.Prepare( true, 100, 2 );
		Assert.IsTrue( first.ShowStatic ); Assert.IsFalse( second.ShowStatic );
		second.Prepare( false, 200, 3 ); first.Prepare( true, 410, 4 );
		Assert.AreEqual( -10d, first.RemainingMilliseconds ); Assert.AreEqual( 300d, second.RemainingMilliseconds );
		Assert.AreEqual( 4, first.NoiseFrame ); Assert.AreEqual( 3, second.NoiseFrame );
	}
}
