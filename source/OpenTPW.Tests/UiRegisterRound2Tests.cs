using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The GPU-free rules of the second UI register round, each cited to tp.exe in the source.</summary>
[TestClass]
public class UiRegisterRound2Tests
{
	[TestMethod]
	public void CommaTurnsOneRotationStepAndFullStopThree()
	{
		// tp.exe 0x40c120 adds 90 degrees, 0x40c150 adds 270 (mod 360); Level.RotateBuild is one +90 step.
		Assert.AreEqual( 1, ParkHud.BlueprintTurnSteps( HudScreenShortcut.TurnBlueprintLeft ), "comma = +90" );
		Assert.AreEqual( 3, ParkHud.BlueprintTurnSteps( HudScreenShortcut.TurnBlueprintRight ), "full stop = +270" );
		Assert.AreEqual( 0, ParkHud.BlueprintTurnSteps( HudScreenShortcut.BuyWindow ) );
		foreach ( var (steps, expected) in new[] { (1, 90), (3, 270) } )
		{
			var rotation = 0;
			for ( var step = 0; step < steps; step++ )
				rotation = (rotation + 90) % 360;
			Assert.AreEqual( expected, rotation, "the steps add up to the original's angle" );
		}
	}

	[TestMethod]
	public void ExactlyCtrlAtThePlacingClickKeepsTheBuildTool()
	{
		Assert.IsTrue( Level.KeepsBuildTool( Input.ModifierMask( shift: false, control: true, alt: false ) ), "mask 0xc" );
		Assert.IsFalse( Level.KeepsBuildTool( 0 ) );
		Assert.IsFalse( Level.KeepsBuildTool( Input.ModifierMask( true, true, false ) ), "Ctrl+Shift is not exactly Ctrl" );
		Assert.IsFalse( Level.KeepsBuildTool( Input.ModifierMask( false, true, true ) ), "Ctrl+Alt is not exactly Ctrl" );
		Assert.IsFalse( Level.KeepsBuildTool( Input.ModifierMask( true, false, false ) ) );
		Assert.AreEqual( 3, Input.ModifierMask( true, false, false ) );
		Assert.AreEqual( 0xc, Input.ModifierMask( false, true, false ) );
		Assert.AreEqual( 0x30, Input.ModifierMask( false, false, true ) );
	}

	[TestMethod]
	public void LeavingTheBuildToolResetsTheAngleToZero()
	{
		var level = (Level)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		level.RotateBuild();
		level.RotateBuild();
		Assert.AreEqual( 180, level.BuildRotation );
		level.ResetBuildRotation();
		Assert.AreEqual( 0, level.BuildRotation );
	}

	[TestMethod]
	public void TheMapWindowPausesThePark()
	{
		Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 2f, new[] { "hud", "map" } ), "0x5f0b40 calls the game pause 0x4092a0" );
		Assert.AreEqual( 2f, ParkHud.TimeScaleFor( 2f, new[] { "hud", "buy" } ), "the buy window still does not pause" );
		Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 1f, new[] { "hud", ParkHud.PauseMenuScreen } ) );
	}

	[TestMethod]
	public void TheResearchEffortSliderTruncatesButOtherSlidersRound()
	{
		var effort = new UiSlider { Track = new UiRect( 0, 0, 1067, 67 ), KnobSize = new System.Numerics.Vector2( 67, 67 ), Steps = () => 101, Truncates = true };
		// travel 1000 units: 10 units per value; the ball centre is half a ball (33.5) inside the track.
		Assert.AreEqual( 0, effort.ValueAt( 33.5f + 9.9f ), "9.9 units: truncated to 0" );
		Assert.AreEqual( 1, effort.ValueAt( 33.5f + 10.1f ) );
		Assert.AreEqual( 1, effort.ValueAt( 33.5f + 19.9f ), "1.99 truncates to 1" );
		Assert.AreEqual( 50, effort.ValueAt( 33.5f + 509f ) );
		Assert.AreEqual( 100, effort.ValueAt( 33.5f + 1000f ), "the right end is exactly 100" );
		Assert.AreEqual( 100, effort.ValueAt( 5000 ) );
		Assert.AreEqual( 0, effort.ValueAt( -50 ) );
		var rounded = new UiSlider { Track = effort.Track, KnobSize = effort.KnobSize, Steps = () => 101 };
		Assert.AreEqual( 1, rounded.ValueAt( 33.5f + 9.9f ), "the default still takes the nearest step" );
	}

	[TestMethod]
	public void TheEnterParkSoundSkipsInstantAction()
	{
		Assert.IsFalse( FrontEndMenu.EnterParkSoundPlays( instantAction: true ), "game type 2 skips cat_globallobbysfx 4" );
		Assert.IsTrue( FrontEndMenu.EnterParkSoundPlays( instantAction: false ) );
	}

	[TestMethod]
	public void AllTheOptionsTogglesFlipTheirOwnByte()
	{
		var previous = GameOptions.Current;
		try
		{
			GameOptions.Current = new GameOptions();
			var changed = 0;
			var hud = (ParkHud)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
			typeof( ParkHud ).GetField( "host", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!
				.SetValue( hud, new HudHost { OptionsChanged = () => changed++ } );
			Assert.IsTrue( hud.HelpBarVisible );
			hud.ToggleGlobalHelp();
			Assert.IsFalse( GameOptions.Current.PopupHelp, "Ctrl+H flips Popup help (options +0x36)" );
			Assert.IsTrue( GameOptions.Current.Confirmations && GameOptions.Current.Advisor && GameOptions.Current.RmbCancel, "the neighbouring options are untouched" );
			Assert.AreEqual( 1, changed, "the host is told so it can persist the options" );
			hud.ToggleGlobalHelp();
			Assert.IsTrue( GameOptions.Current.PopupHelp );
			Assert.AreEqual( 2, changed );
		}
		finally
		{
			GameOptions.Current = previous;
		}
	}
}
