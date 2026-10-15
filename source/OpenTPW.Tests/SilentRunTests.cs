using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SilentRunTests
{
	[TestMethod]
	public void AutomatedRunsAreSilentUnlessTheyAskForSound()
	{
		Assert.IsTrue( Game.IsSilentRun( new[] { "--smoke-test" }, null ) );
		Assert.IsTrue( Game.IsSilentRun( new[] { "--front-end", "--smoke-test" }, null ) );
		Assert.IsTrue( Game.IsSilentRun( new[] { "--capture-world", "a.png" }, null ) );
		Assert.IsTrue( Game.IsSilentRun( new[] { "--park-resume-smoke", "capture" }, null ) );
		Assert.IsFalse( Game.IsSilentRun( new[] { "--smoke-test", "--smoke-audio" }, null ) );
	}

	[TestMethod]
	public void NormalPlayKeepsItsSoundUnlessSilenced()
	{
		Assert.IsFalse( Game.IsSilentRun( new[] { "--game-path", "x" }, null ) );
		Assert.IsFalse( Game.IsSilentRun( new[] { "--load-original-level", "jungle" }, "0" ) );
		Assert.IsTrue( Game.IsSilentRun( new[] { "--load-original-level", "jungle" }, "1" ) );
		Assert.IsTrue( Game.IsSilentRun( new[] { "--silent" }, null ) );
	}
}
