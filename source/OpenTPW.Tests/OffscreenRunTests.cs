using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class OffscreenRunTests
{
	[TestMethod]
	public void SmokesAreOffscreenByDefault()
	{
		Assert.IsTrue( Game.IsOffscreenRun( new[] { "--smoke-test" }, null ) );
		Assert.IsTrue( Game.IsOffscreenRun( new[] { "--front-end", "--smoke-test" }, null ) );
		Assert.IsTrue( Game.IsOffscreenRun( new[] { "--sandbox", "--smoke-test" }, null ) );
		Assert.IsTrue( Game.IsOffscreenRun( new[] { "--load-original-level", "0", "--smoke-test" }, null ) );
		Assert.IsTrue( Game.IsOffscreenRun( new[] { "--park-resume-smoke", "capture" }, null ) );
	}

	[TestMethod]
	public void NormalRunsStayVisibleUnlessAskedOtherwise()
	{
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--sandbox" }, null ) );
		Assert.IsFalse( Game.IsOffscreenRun( System.Array.Empty<string>(), null ) );
		Assert.IsTrue( Game.IsOffscreenRun( new[] { "--offscreen" }, null ) );
		Assert.IsTrue( Game.IsOffscreenRun( System.Array.Empty<string>(), "1" ) );
	}

	[TestMethod]
	public void OptingOutKeepsTheWindowVisible()
	{
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--smoke-test" }, "0" ) );
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--smoke-test", "--no-offscreen" }, null ) );
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--offscreen", "--no-offscreen" }, null ) );
	}
}
