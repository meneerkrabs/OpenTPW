using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>
/// The Escape pause menu stops the park simulation, as the original's pause menu calls the game pause
/// (Mac 0x10198514 → 0x10110518, which freezes the scaled clock that drives the park turns), while the
/// park's speed setting stays as it was.
/// </summary>
[TestClass]
public class ParkPauseTests
{
	[TestMethod]
	public void PauseMenuStopsTheParkSimulation()
	{
		Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 2f, new[] { "hud", ParkHud.PauseMenuScreen } ), "pause menu on top" );
		Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 1f, new[] { "hud", ParkHud.PauseMenuScreen, "options" } ),
			"a screen opened from the pause menu keeps the park stopped" );
		Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 4f, new[] { "hud", "texturePackDownload" } ), "texture-pack download" );
	}

	[TestMethod]
	public void WithoutThePauseMenuTheSpeedSettingRunsThePark()
	{
		Assert.AreEqual( 1f, ParkHud.TimeScaleFor( 1f, new[] { "hud" } ) );
		Assert.AreEqual( 4f, ParkHud.TimeScaleFor( 4f, new[] { "hud", "buy" } ), "the buy window does not pause" );
		Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 0f, new[] { "hud" } ), "the paused speed setting still pauses" );
	}

	[TestMethod]
	public void ClosingThePauseMenuResumesAtTheSameSpeed()
	{
		var status = new NoEconomyStatus { Speed = GameSpeed.Fast };
		var speedBefore = status.Speed;
		Assert.AreEqual( 0f, ParkHud.TimeScaleFor( status.TimeScale, new[] { "hud", ParkHud.PauseMenuScreen } ) );
		Assert.AreEqual( speedBefore, status.Speed, "opening the pause menu leaves the speed setting alone" );
		Assert.AreEqual( status.TimeScale, ParkHud.TimeScaleFor( status.TimeScale, new[] { "hud" } ) );
		Assert.AreNotEqual( 0f, status.TimeScale );
	}
}
