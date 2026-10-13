using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class FrameLimiterTests
{
	/// <summary>How many of <paramref name="frames"/> animation frames at <paramref name="hz"/> (with ± jitter) a cap draws.</summary>
	private static int Drawn( int cap, double hz, int frames, double jitter = 0 )
	{
		var limiter = new FrameLimiter();
		return Enumerable.Range( 0, frames ).Count( index => limiter.Due( 10 + index / hz + (index % 2 == 0 ? jitter : -jitter), cap ) );
	}

	[TestMethod]
	public void WithoutACapEveryFrameIsDrawn()
	{
		Assert.AreEqual( 600, Drawn( 0, 60, 600 ) );
		Assert.AreEqual( 0, new FrameLimiter().WaitSeconds( 10, 0 ) );
	}

	[TestMethod]
	public void ACapDrawsEveryNthDisplayFrameWithoutDrift()
	{
		Assert.AreEqual( 300, Drawn( 30, 60, 600 ), "30 fps on a 60 Hz display: every second frame" );
		Assert.AreEqual( 300, Drawn( 30, 60, 600, jitter: 0.0015 ), "animation-frame jitter keeps the rhythm" );
		Assert.AreEqual( 600, Drawn( 60, 120, 1200 ), "60 fps on a 120 Hz display: every second frame" );
		Assert.AreEqual( 600, Drawn( 60, 60, 600, jitter: 0.0015 ), "a cap at the display rate draws every frame, jitter or not" );
		var drawn144 = Drawn( 60, 144, 1440 );
		Assert.IsTrue( drawn144 is >= 599 and <= 601, $"60 fps on a 144 Hz display averages 60 ({drawn144} in 10 s)" );
	}

	[TestMethod]
	public void ALateFrameRestartsTheGridInsteadOfBursting()
	{
		var limiter = new FrameLimiter();
		Assert.IsTrue( limiter.Due( 10, 30 ) );
		Assert.IsFalse( limiter.Due( 10.02, 30 ) );
		Assert.IsFalse( limiter.Due( 10.024, 30 ), "earlier than a quarter interval before the grid" );
		Assert.IsTrue( limiter.Due( 10.5, 30 ), "a long pause (a busy frame)" );
		Assert.IsFalse( limiter.Due( 10.51, 30 ), "no catch-up burst after the pause" );
		Assert.IsTrue( limiter.Due( 10.5 + 1d / 30, 30 ) );
	}

	[TestMethod]
	public void TheDesktopWaitEndsWhenTheNextFrameIsDue()
	{
		var limiter = new FrameLimiter();
		Assert.IsTrue( limiter.Due( 10, 30 ) );
		var wait = limiter.WaitSeconds( 10.01, 30 );
		Assert.AreEqual( 1d / 30 * (1 - FrameLimiter.SlackFraction) - 0.01, wait, 1e-9 );
		Assert.IsTrue( limiter.Due( 10.01 + wait, 30 ) );
		Assert.IsTrue( new FrameLimiter().Due( 10, 30 ), "turning the cap off and on again starts fresh" );
	}
}
