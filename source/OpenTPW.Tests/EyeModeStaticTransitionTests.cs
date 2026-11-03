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
		timer.Prepare( initial, 100 );
		Assert.IsFalse( timer.ShowStatic );
		timer.Prepare( !initial, 1000 );
		Assert.IsTrue( timer.ShowStatic ); Assert.AreEqual( 300d, timer.RemainingMilliseconds );
		timer.Prepare( !initial, 1100 );
		Assert.AreEqual( 200d, timer.RemainingMilliseconds );
		timer.Prepare( initial, 1150 );
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
		timer.Prepare( true, 100 );
		timer.Prepare( true, 100 + elapsed );
		Assert.IsTrue( timer.ShowStatic, "even a large elapsed interval cannot erase the current static frame" );
		Assert.AreEqual( left, timer.RemainingMilliseconds );
		timer.Prepare( true, 100 + elapsed );
		Assert.AreEqual( left > 0, timer.ShowStatic ); Assert.AreEqual( left, timer.RemainingMilliseconds );
	}

	[TestMethod]
	public void UnchangedBooleanDoesNotRestartAndUnobservedIntermediateModesAreNotInvented()
	{
		var timer = new EyeModeStaticTransition( false );
		timer.Prepare( true, 100 );
		// Ride -> walk, or leave and reenter entirely between observations: only the final true is visible.
		timer.Prepare( true, 200 ); Assert.AreEqual( 200d, timer.RemainingMilliseconds );
		timer.Prepare( true, 400 ); Assert.IsTrue( timer.ShowStatic );
		timer.Prepare( true, 500 ); Assert.IsFalse( timer.ShowStatic );
		timer.Prepare( true, 9000 ); Assert.AreEqual( 0d, timer.RemainingMilliseconds );
		timer.Prepare( false, 10000 ); Assert.AreEqual( 300d, timer.RemainingMilliseconds );
	}

	[TestMethod]
	public void SkippedFramesPreserveObservationAndStampUntilEligibleResume()
	{
		var timer = new EyeModeStaticTransition( false );
		timer.Prepare( true, 100 ); timer.SkipFrame(); timer.SkipFrame();
		Assert.IsFalse( timer.ShowStatic ); Assert.AreEqual( 300d, timer.RemainingMilliseconds );
		timer.Prepare( true, 2100 );
		Assert.IsTrue( timer.ShowStatic ); Assert.AreEqual( -1700d, timer.RemainingMilliseconds );
		timer.Prepare( true, 2200 ); Assert.IsFalse( timer.ShowStatic );
		timer.SkipFrame(); timer.Prepare( false, 6000 );
		Assert.IsTrue( timer.ShowStatic ); Assert.AreEqual( 300d, timer.RemainingMilliseconds, "a newly observed edge starts its own budget" );
	}

	[TestMethod]
	public void HostClockRejectsInvalidOrBackwardSamplesWithoutExtendingTheBudget()
	{
		var timer = new EyeModeStaticTransition( false ); timer.Prepare( true, 100 );
		foreach ( var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d, 90d } )
		{
			timer.Prepare( true, invalid );
			Assert.AreEqual( 300d, timer.RemainingMilliseconds ); Assert.IsTrue( timer.ShowStatic );
		}
		timer.Prepare( true, 200 ); Assert.AreEqual( 200d, timer.RemainingMilliseconds );
	}

	[TestMethod]
	public void InstancesAndAlreadyActiveInitialStateAreIndependent()
	{
		var first = new EyeModeStaticTransition( false ); var second = new EyeModeStaticTransition( true );
		first.Prepare( true, 100 ); second.Prepare( true, 100 );
		Assert.IsTrue( first.ShowStatic ); Assert.IsFalse( second.ShowStatic );
		second.Prepare( false, 200 ); first.Prepare( true, 410 );
		Assert.AreEqual( -10d, first.RemainingMilliseconds ); Assert.AreEqual( 300d, second.RemainingMilliseconds );
	}
}
