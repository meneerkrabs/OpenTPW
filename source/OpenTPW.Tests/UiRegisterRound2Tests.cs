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
	public void TheConfirmationBoxesAndThePostcardPauseThePark()
	{
		// the generic message box 0x47f020 pauses at 0x47f251 (ride erase 0x48cd10, employee dismiss 0x4b5e5d) and the postcard window at 0x4a93d7
		foreach ( var name in new[] { "confirmDelete", "confirmDismissStaff", "sendPostcard" } )
			Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 2f, new[] { "hud", name } ), name );
	}

	[TestMethod]
	public void TheMapFillsPathsQueuesAndUnownedLandWithTheOriginalsRecords()
	{
		// 0x5f2050 classes 10, 11 and 12 (path, queue, unowned) in that order, 0x5f2380 fills them from 0xf86b00, 0xf86b24 and 0xf86b6c
		Assert.AreEqual( new Veldrid.RgbaByte( 31, 166, 31, 255 ), ParkMapPalette.CellFill( ParkCellType.Path, true ) );
		Assert.AreEqual( new Veldrid.RgbaByte( 37, 198, 37, 255 ), ParkMapPalette.CellFill( ParkCellType.Queue, false ) );
		Assert.AreEqual( new Veldrid.RgbaByte( 0, 23, 0, 128 ), ParkMapPalette.CellFill( ParkCellType.Empty, true ) );
		Assert.IsNull( ParkMapPalette.CellFill( ParkCellType.Empty, false ) );
	}

	[TestMethod]
	public void TheMapWindowPausesThePark()
	{
		Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 2f, new[] { "hud", "map" } ), "0x5f0b40 calls the game pause 0x4092a0" );
		Assert.AreEqual( 2f, ParkHud.TimeScaleFor( 2f, new[] { "hud", "buy" } ), "the buy window still does not pause" );
		Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 1f, new[] { "hud", ParkHud.PauseMenuScreen } ) );
	}

	[TestMethod]
	public void ThePostcardAndTheDeleteConfirmationsPauseThePark()
	{
		// [BIN:TPW-EXE:0x4a9380 postcard] pauses at 0x4a93d7; [BIN:TPW-EXE:0x48cd10 delete confirmation] is the message box 0x47f020, which pauses at 0x47f26c
		Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 2f, new[] { "hud", ParkHud.PostcardScreen } ) );
		Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 2f, new[] { "hud", "confirmDelete" } ) );
		Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 2f, new[] { "hud", "attractions", "confirmDeleteAttraction" } ) );
		Assert.AreEqual( 2f, ParkHud.TimeScaleFor( 2f, new[] { "hud", "loanConfirm" } ), "the loan box is not mapped to a 0x47f020 call site" );
		// the pause follows the open screens each frame and holds no state, so closing, Escape, a removed screen or a new park always resume the speed setting
		Assert.AreEqual( 2f, ParkHud.TimeScaleFor( 2f, new[] { "hud" } ) );
		Assert.AreEqual( 0f, ParkHud.TimeScaleFor( 0f, new[] { "hud", ParkHud.PostcardScreen } ), "the paused speed setting stays paused" );
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
	public void AnOriginalClassSliderPagesOnATrackClickAndGrabsTheThumbAtAnOffset()
	{
		var canvas = new UiCanvas( 2048, 1536 );
		var value = 50;
		var slider = new UiSlider
		{
			Bounds = new UiRect( 0, 0, 1067, 67 ), Anchor = UiAnchor.TopLeft, Track = new UiRect( 0, 0, 1067, 67 ), KnobSize = new System.Numerics.Vector2( 67, 67 ),
			Steps = () => 101, Truncates = true, Value = () => value, Changed = v => value = v
		};
		IUiDragTarget target = slider;
		// the thumb of 50 spans 500..567; a click 100 units left of it lowers the value by one page, right of it raises it
		target.DragBegin( canvas, canvas.Map( new UiRect( 400, 0, 1, 1 ), UiAnchor.TopLeft ).Center );
		target.DragTo( canvas, canvas.Map( new UiRect( 400, 0, 1, 1 ), UiAnchor.TopLeft ).Center );
		Assert.AreEqual( 49, value, "left of the thumb: one page down, and the held press does not drag" );
		target.DragBegin( canvas, canvas.Map( new UiRect( 900, 0, 1, 1 ), UiAnchor.TopLeft ).Center );
		Assert.AreEqual( 50, value, "right of the thumb: one page up" );
		// grabbing the thumb 20 units off its centre does not move it; dragging keeps the offset
		var thumb = slider.KnobRect( 50 );
		var grab = thumb.X + thumb.Width / 2 + 20;
		target.DragBegin( canvas, canvas.Map( new UiRect( grab, 0, 1, 1 ), UiAnchor.TopLeft ).Center );
		target.DragTo( canvas, canvas.Map( new UiRect( grab, 0, 1, 1 ), UiAnchor.TopLeft ).Center );
		Assert.AreEqual( 50, value, "a grab alone changes nothing" );
		target.DragTo( canvas, canvas.Map( new UiRect( grab + 100, 0, 1, 1 ), UiAnchor.TopLeft ).Center );
		Assert.AreEqual( 60, value, "100 units of travel are ten values (1000 units for 100)" );
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
