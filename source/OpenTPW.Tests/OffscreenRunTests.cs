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
		Assert.IsTrue( Game.IsOffscreenRun( new[] { "--play-movie", "bf", "--smoke-test" }, null ), "the movie smoke" );
	}

	[TestMethod]
	public void EveryAutomatedModeIsOffscreenUnlessVisible()
	{
		var modes = new[]
		{
			new[] { "--tpi-smoke-test" },
			new[] { "--smoke-presence" },
			new[] { "--load-original-level", "jungle", "--capture-world", "a.png" },
			new[] { "--load-original-level", "jungle", "--capture-interface", "--capture-world", "a.png" },
			new[] { "--load-original-level", "jungle", "--capture-camera", "10,-5,30,-135" },
			new[] { "--load-original-level", "jungle", "--perf-run", "600" },
			new[] { "--load-original-level", "jungle", "--perf-hash-tick", "100" },
			new[] { "--load-original-level", "jungle", "--debug-sim-minutes", "3" },
			new[] { "--load-original-level", "jungle", "--debug-weather", "100" },
			new[] { "--headless" },
			new[] { "--m3-gate" },
			new[] { "--m4-gate" },
			new[] { "--guest-report" },
			new[] { "--guest-compare" },
			new[] { "--validate-assets" },
		};
		foreach ( var mode in modes )
		{
			var name = string.Join( ' ', mode );
			Assert.IsTrue( Game.IsAutomatedRun( mode ), name );
			Assert.IsTrue( Game.IsOffscreenRun( mode, null ), name );
			Assert.IsFalse( Game.IsOffscreenRun( [.. mode, "--no-offscreen"], null ), name );
			Assert.IsFalse( Game.IsOffscreenRun( [.. mode, "--visible"], null ), name );
			Assert.IsFalse( Game.IsOffscreenRun( mode, "0" ), name );
		}
	}

	[TestMethod]
	public void SilentAndOffscreenShareOneListOfAutomatedModes()
	{
		var samples = new[]
		{
			new[] { "--smoke-test" }, new[] { "--capture-world", "a.png" }, new[] { "--perf-run", "600" }, new[] { "--debug-staff" },
			new[] { "--headless" }, new[] { "--m4-gate" }, new[] { "--offscreen" },
			new[] { "--sandbox" }, new[] { "--front-end" }, new[] { "--load-original-level", "jungle" }, new[] { "--play-movie", "bf" },
			new[] { "--setup" }, System.Array.Empty<string>(),
		};
		foreach ( var args in samples )
			Assert.AreEqual( Game.IsSilentRun( args, null ), Game.IsOffscreenRun( args, null ), string.Join( ' ', args ) );
	}

	[TestMethod]
	public void SoundAndWindowAreSeparateChoices()
	{
		// --audible brings the sound back but keeps the window offscreen; --visible shows the window but keeps the run silent
		Assert.IsTrue( Game.IsOffscreenRun( new[] { "--perf-run", "600", "--audible" }, null ) );
		Assert.IsFalse( Game.IsSilentRun( new[] { "--perf-run", "600", "--audible" }, null ) );
		Assert.IsTrue( Game.IsOffscreenRun( new[] { "--smoke-test", "--smoke-audio" }, null ) );
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--perf-run", "600", "--visible" }, null ) );
		Assert.IsTrue( Game.IsSilentRun( new[] { "--perf-run", "600", "--visible" }, null ) );
		Assert.IsTrue( Game.IsSilentRun( new[] { "--capture-world", "a.png", "--no-offscreen" }, null ) );
	}

	[TestMethod]
	public void InteractiveModesStayVisible()
	{
		// viewing and playing need a real presented window; --play-movie is only offscreen as the movie smoke (above)
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--play-movie", "bf" }, null ) );
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--front-end" }, null ) );
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--load-original-level", "jungle" }, null ) );
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--setup" }, null ) );
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--visit-park", "a.tpwpark" }, null ) );
		Assert.IsFalse( Game.IsAutomatedRun( new[] { "--play-movie", "bf", "--mute", "--no-vsync", "--frame-stats", "--audible", "--visible" } ) );
	}

	[TestMethod]
	public void NormalRunsStayVisibleUnlessAskedOtherwise()
	{
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--sandbox" }, null ) );
		Assert.IsFalse( Game.IsOffscreenRun( System.Array.Empty<string>(), null ) );
		Assert.IsTrue( Game.IsOffscreenRun( new[] { "--offscreen" }, null ) );
		Assert.IsTrue( Game.IsOffscreenRun( System.Array.Empty<string>(), "1" ) );
		Assert.IsFalse( Game.IsOffscreenRun( System.Array.Empty<string>(), "0" ) );
	}

	[TestMethod]
	public void OptingOutKeepsTheWindowVisible()
	{
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--smoke-test" }, "0" ) );
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--smoke-test", "--no-offscreen" }, null ) );
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--offscreen", "--no-offscreen" }, null ) );
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--offscreen", "--visible" }, null ) );
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--capture-world", "a.png" }, "0" ) );
		Assert.IsFalse( Game.IsOffscreenRun( new[] { "--capture-world", "a.png", "--visible" }, "1" ), "the flag wins over the environment" );
	}
}
