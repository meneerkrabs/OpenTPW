using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The experimental Theme Park Inc HUD under a camera flight's input window (docs/reverse/TPI-MISSIONS.md §5): the skip by Escape and by the right button's release, the pause key that stays reachable,
/// the clock the slice runs at, and the refusal of a flight of thing 0. No game data needed.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncFlightInputTests
{
	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize() { previousEdition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkInc ); }

	[TestCleanup]
	public void Cleanup() => GameEditions.Select( previousEdition );

	private static UiInput RightRelease => new( new Vector2( 100, 100 ), false, false, false, false, UiKeys.None, RightReleased: true );

	private static float Scale( ParkEconomy economy, ThemeParkIncHud hud ) => GameFlow.ThemeParkIncTimeScale( economy.TpiCutscene!.ClockRate, hud.Speed, hud.TimeScale );

	[TestMethod]
	public void AFlightStartedWhilePausedWaitsForThePauseKeyWhichStaysReachable()
	{
		var economy = ThemeParkIncHudChallengeTests.Economy();
		var hud = ThemeParkIncHudChallengeTests.BuildHud( economy, GameSpeed.Paused );
		var context = OriginalUiTests.FakeContext();
		var cutscene = economy.TpiCutscene!;
		Assert.IsTrue( cutscene.Play( 32, 1, ThemeParkIncCutscene.ArrivalMode ) );
		Assert.IsTrue( hud.FlightActive );
		// the flight sets the clock's rate (0x41C000, +0x20) and leaves its pause flag (+0x30, 0x41BBF0): the park stays paused
		Assert.AreEqual( 0f, Scale( economy, hud ) );
		Assert.IsTrue( hud.Update( context, UiInput.Key( UiKeys.Pause ) ), "the input window keeps the frame" );
		Assert.AreEqual( GameSpeed.Normal, hud.Speed, "the binding \"pause\" is dispatched past the input window (0x42F420)" );
		Assert.AreEqual( 1f, Scale( economy, hud ), "resumed, the clock runs at the flight's rate" );
		Assert.IsTrue( cutscene.Flying );
		Assert.IsTrue( hud.Update( context, UiInput.Key( UiKeys.Pause ) ) );
		Assert.AreEqual( GameSpeed.Paused, hud.Speed, "and pauses it again" );
		Assert.AreEqual( 0f, Scale( economy, hud ) );
		Assert.IsTrue( cutscene.Flying, "a pause is no skip" );
	}

	[TestMethod]
	public void TheRightButtonsReleaseSkipsOnlyTheSkippableFlight()
	{
		var economy = ThemeParkIncHudChallengeTests.Economy();
		var hud = ThemeParkIncHudChallengeTests.BuildHud( economy );
		var context = OriginalUiTests.FakeContext();
		var cutscene = economy.TpiCutscene!;
		Assert.IsTrue( cutscene.Play( 29, 2, 0 ) );
		Assert.IsTrue( hud.Update( context, RightRelease ) );
		Assert.IsFalse( cutscene.SkipPending, "an obstacle's flight (mode 0) is not skippable" );
		Assert.IsTrue( cutscene.Flying );
		cutscene.ForceSkip();
		cutscene.Update();
		Assert.IsFalse( cutscene.Flying );

		Assert.IsTrue( cutscene.Play( 32, 1, ThemeParkIncCutscene.ArrivalMode ) );
		// [BIN:TPI-EXE:0x4D0A38 right button] the window procedure's WM_RBUTTONUP reaches the skip 0x5D0290
		Assert.IsTrue( hud.Update( context, RightRelease ) );
		Assert.IsTrue( cutscene.SkipPending );
		cutscene.Update();
		Assert.AreEqual( (false, false, (double?)null), (cutscene.Flying, cutscene.OverlayOpen, cutscene.ClockRate), "the next update ends the arrival" );
		Assert.IsFalse( hud.FlightActive );
	}

	[TestMethod]
	public void AFlightOfThingZeroIsRefusedAndOpensNoWindow()
	{
		var economy = ThemeParkIncHudChallengeTests.Economy();
		var hud = ThemeParkIncHudChallengeTests.BuildHud( economy );
		var cutscene = economy.TpiCutscene!;
		// the thing table's slot 0 is never a thing (0x648DF0); 0x5D0400 would read its null pointer
		Assert.IsFalse( cutscene.Play( 0, 2, 0 ) );
		Assert.AreEqual( (false, false, (double?)null), (cutscene.Flying, cutscene.OverlayOpen, cutscene.ClockRate) );
		Assert.IsFalse( hud.FlightActive );
		// the complete button of an obstacle mission whose obstacle thing is not found posts message 0x24 with thing 0: no flight starts, so no input window is left open
		economy.TpiMissions!.RemoveObstacle( 0 );
		Assert.AreEqual( (ushort)0, economy.TpiCameraRequests[^1].Thing );
		Assert.IsFalse( cutscene.OverlayOpen );
		Assert.IsFalse( hud.FlightActive );
		Assert.IsTrue( cutscene.Play( 32, 1, ThemeParkIncCutscene.ArrivalMode ), "a refused thing 0 leaves the next flight free to start" );
	}
}
