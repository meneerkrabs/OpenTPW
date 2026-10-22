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
		Assert.IsTrue( Game.IsSilentRun( new[] { "--coop-join-smoke", "host" }, null ) );
		Assert.IsFalse( Game.IsSilentRun( new[] { "--smoke-test", "--smoke-audio" }, null ) );
	}

	[TestMethod]
	public void EveryAutomatedModeIsSilentUnlessAudible()
	{
		foreach ( var flag in new[] { "--tpi-smoke-test", "--perf-hash-tick", "--smoke-presence", "--offscreen", "--headless", "--m3-gate", "--guest-report" } )
		{
			Assert.IsTrue( Game.IsSilentRun( new[] { flag }, null ), flag );
			Assert.IsFalse( Game.IsSilentRun( new[] { flag, "--audible" }, null ), flag );
		}
		Assert.IsTrue( Game.IsSilentRun( new[] { "--perf-run", "600" }, null ) );
		Assert.IsTrue( Game.IsSilentRun( new[] { "--load-original-level", "jungle", "--debug-sim-minutes", "3" }, null ) );
		Assert.IsFalse( Game.IsSilentRun( new[] { "--perf-run", "600", "--audible" }, null ) );
		Assert.IsTrue( Game.IsSilentRun( new[] { "--load-original-level", "jungle", "--debug-weather", "100" }, null ), "a debug capture option without a capture" );
		Assert.IsFalse( Game.IsSilentRun( new[] { "--load-original-level", "jungle", "--debug-weather", "100", "--audible" }, null ) );
		Assert.IsTrue( Game.IsSilentRun( new[] { "--game-path", "x" }, null, "1" ) );
		Assert.IsFalse( Game.IsSilentRun( new[] { "--game-path", "x", "--no-offscreen" }, null, "1" ) );
		Assert.IsFalse( Game.IsSilentRun( new[] { "--game-path", "x", "--visible" }, null, "1" ) );
		Assert.IsTrue( Game.IsSilentRun( new[] { "--smoke-test", "--audible" }, "1" ) );
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
