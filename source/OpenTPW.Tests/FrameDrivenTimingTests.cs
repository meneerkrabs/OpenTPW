using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>
/// Original behaviour that tp.exe steps once per rendered frame runs at a fixed 60 Hz reference rate here ([EXT:frame-driven-at-60hz]):
/// the same number of steps per second at 30, 60 and 144 frames a second.
/// </summary>
[TestClass, DoNotParallelize]
public class FrameDrivenTimingTests
{
	private static int FramesIn( ReferenceFrameClock clock, float seconds, int framesPerSecond )
	{
		var total = 0;
		var delta = 1f / framesPerSecond;
		for ( var frame = 0; frame < (int)MathF.Round( seconds * framesPerSecond ); frame++ )
			total += clock.Advance( delta );
		return total;
	}

	[TestMethod]
	public void AClockYieldsSixtyReferenceFramesASecondWhateverTheDisplayRate()
	{
		foreach ( var rate in new[] { 24, 30, 50, 60, 75, 120, 144, 240 } )
		{
			var frames = FramesIn( new ReferenceFrameClock(), 10f, rate );
			Assert.IsTrue( Math.Abs( frames - 600 ) <= 1, $"{rate} fps gave {frames} reference frames in ten seconds" );
		}
	}

	[TestMethod]
	public void ADeltaOfExactlyOneReferenceFrameIsOneFrameEveryTime()
	{
		var clock = new ReferenceFrameClock();
		for ( var index = 0; index < 1000; index++ )
			Assert.AreEqual( 1, clock.Advance( ReferenceFrameClock.FrameSeconds ) );
		Assert.AreEqual( 0f, clock.PendingSeconds, 1e-6f );
	}

	[TestMethod]
	public void AShortDeltaBanksItsTimeAndABadDeltaAddsNothing()
	{
		var clock = new ReferenceFrameClock();
		Assert.AreEqual( 0, clock.Advance( 0.01f ) );
		Assert.AreEqual( 0.01f, clock.PendingSeconds, 1e-6f );
		Assert.AreEqual( 1, clock.Advance( 0.01f ), "0.02 s is one frame and a remainder" );
		Assert.AreEqual( 0.02f - ReferenceFrameClock.FrameSeconds, clock.PendingSeconds, 1e-6f );
		foreach ( var bad in new[] { -1f, 0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity } )
			Assert.AreEqual( 0, clock.Advance( bad ) );
		Assert.AreEqual( 0.02f - ReferenceFrameClock.FrameSeconds, clock.PendingSeconds, 1e-6f, "a bad delta changed nothing" );
		clock.Reset();
		Assert.AreEqual( 0f, clock.PendingSeconds );
	}

	[TestMethod]
	public void AStallIsCappedInsteadOfReplayed()
	{
		Assert.AreEqual( 30, new ReferenceFrameClock().Advance( 60f ), "half a second at most by default" );
		Assert.AreEqual( 15, new ReferenceFrameClock( 0.25f ).Advance( 60f ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => new ReferenceFrameClock( 0.001f ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => new ReferenceFrameClock( float.NaN ) );
	}

	[TestMethod]
	public void TheResearchFluidsStepSixtyTimesASecondAtAnyDisplayRate()
	{
		// tp.exe 0x4a9fc0 steps a panel's fluid on every message 0x1e, once per rendered frame (0x48a580 -> 0x658f6c -> 0x658f97)
		var positions = new List<int>();
		foreach ( var rate in new[] { 30, 60, 144 } )
		{
			var fluid = new ResearchFluid();
			fluid.Apply( 30 );
			var clock = new ReferenceFrameClock( 0.25f );
			var steps = 0;
			for ( var frame = 0; frame < rate * 2; frame++ )
				steps += ResearchFluid.StepFrames( clock, new[] { fluid }, 1f / rate );
			Assert.IsTrue( Math.Abs( steps - 120 ) <= 1, $"{rate} fps stepped {steps} times in two seconds" );
			positions.Add( steps );
		}
		// rate 30: one frame per step, 16 filling frames (0x30..0x3f), then the 32 bubbling frames loop: 120 steps end on the same frame whatever the display
		var reference = new ResearchFluid();
		reference.Apply( 30 );
		for ( var step = 0; step < 120; step++ )
			reference.Tick();
		var sixty = new ResearchFluid();
		sixty.Apply( 30 );
		var sixtyClock = new ReferenceFrameClock( 0.25f );
		for ( var frame = 0; frame < 120; frame++ )
			ResearchFluid.StepFrames( sixtyClock, new[] { sixty }, ReferenceFrameClock.FrameSeconds );
		Assert.AreEqual( (reference.State, reference.Frame, reference.Position), (sixty.State, sixty.Frame, sixty.Position) );
	}

	[TestMethod]
	public void EveryPanelsFluidStepsOnEveryReferenceFrame()
	{
		var fluids = Enumerable.Range( 0, 5 ).Select( _ => new ResearchFluid() ).ToArray();
		foreach ( var fluid in fluids )
			fluid.Apply( 30 );
		var clock = new ReferenceFrameClock( 0.25f );
		Assert.AreEqual( 3, ResearchFluid.StepFrames( clock, fluids, ReferenceFrameClock.FrameSeconds * 3 ) );
		Assert.IsTrue( fluids.All( fluid => fluid.Frame == 0x33 ), "three frames on from the fill's first frame 0x30" );
	}
}
