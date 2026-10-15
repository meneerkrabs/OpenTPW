using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using System.Collections.Generic;
using System.IO;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>
/// The HUD's original keyboard shortcuts (window keys, time keys, blueprint turning) and the happiness gauge:
/// a pressed key must reach the model (the window state, the park clock or the sampled visitors), not just a handler.
/// </summary>
[TestClass]
public class HudScreenShortcutTests
{
	[TestMethod]
	public void OriginalShortcutKeysMapToTheirHudWindowActions()
	{
		Assert.AreEqual( HudScreenShortcut.BuyWindow, ParkHud.ShortcutFor( InputButton.BuyAttractions ), "B" );
		Assert.AreEqual( HudScreenShortcut.ParkInfo, ParkHud.ShortcutFor( InputButton.ParkInfo ), "I" );
		Assert.AreEqual( HudScreenShortcut.FinanceWindow, ParkHud.ShortcutFor( InputButton.FinancialInfo ), "F" );
		Assert.AreEqual( HudScreenShortcut.ResearchWindow, ParkHud.ShortcutFor( InputButton.Research ), "R (help 472 'Click for research (R)')" );
		Assert.AreEqual( HudScreenShortcut.MapWindow, ParkHud.ShortcutFor( InputButton.MapScreen ), "Space (help 473 'view the map (Space)')" );
		Assert.AreEqual( HudScreenShortcut.LoansWindow, ParkHud.ShortcutFor( InputButton.Loans ), "L" );
		Assert.AreEqual( HudScreenShortcut.StaffCosts, ParkHud.ShortcutFor( InputButton.StaffBudgets ), "T" );
		Assert.AreEqual( HudScreenShortcut.EntryPrice, ParkHud.ShortcutFor( InputButton.EntryPrice ), "G" );
		Assert.AreEqual( HudScreenShortcut.StaffApplicants, ParkHud.ShortcutFor( InputButton.HireStaff ), "H" );
		Assert.AreEqual( HudScreenShortcut.VisitorWindow, ParkHud.ShortcutFor( InputButton.AllVisitors ),
			"V opens the All Visitors window (the plain key pans nothing; Ctrl+V stays the locator)" );
		Assert.AreEqual( HudScreenShortcut.None, ParkHud.ShortcutFor( InputButton.AllStaff ),
			"S stays unhandled: the host camera pans with S (Input's WASD axes), and one press cannot be both" );
		Assert.AreEqual( HudScreenShortcut.None, ParkHud.ShortcutFor( InputButton.AllAttractions ),
			"A pans with the camera too; the All Attractions window opens through the buy window instead" );
		Assert.AreEqual( HudScreenShortcut.TimeFaster, ParkHud.ShortcutFor( InputButton.TimeSpeedUp ), "numpad +" );
		Assert.AreEqual( HudScreenShortcut.TimeSlower, ParkHud.ShortcutFor( InputButton.TimeSlowDown ), "numpad -" );
		Assert.AreEqual( HudScreenShortcut.TimeNormal, ParkHud.ShortcutFor( InputButton.TimeReset ), "numpad *" );
		Assert.AreEqual( HudScreenShortcut.TurnBlueprintLeft, ParkHud.ShortcutFor( InputButton.TurnBlueprintLeft ), "comma" );
		Assert.AreEqual( HudScreenShortcut.TurnBlueprintRight, ParkHud.ShortcutFor( InputButton.TurnBlueprintRight ), "full stop" );
		Assert.AreEqual( HudScreenShortcut.LocateNextStaff, ParkHud.ShortcutFor( InputButton.StaffLocator ), "Ctrl+S" );
		Assert.AreEqual( HudScreenShortcut.LocateNextVisitor, ParkHud.ShortcutFor( InputButton.VisitorLocator ), "Ctrl+V" );
		Assert.AreEqual( HudScreenShortcut.ToggleHelpBar, ParkHud.ShortcutFor( InputButton.ToggleHelpBar ), "Ctrl+H toggles contextual help" );
		// Dedicated park actions and keys without implemented screens stay outside window-shortcut dispatch.
		foreach ( var button in new[] { InputButton.CamcorderMode,
			InputButton.SendEmailPostcard, InputButton.Help, InputButton.Menu, InputButton.RideView } )
			Assert.AreEqual( HudScreenShortcut.None, ParkHud.ShortcutFor( button ), button.ToString() );
	}

	[TestMethod]
	public void WindowKeysActOverTheHudWindowsOnly()
	{
		Assert.IsTrue( ParkHud.WindowShortcutsAllowed( true, null ), "with no window open the park gate decides" );
		Assert.IsTrue( ParkHud.WindowShortcutsAllowed( false, "buy" ), "a window key may close its own window" );
		Assert.IsTrue( ParkHud.WindowShortcutsAllowed( false, "map" ) );
		Assert.IsFalse( ParkHud.WindowShortcutsAllowed( false, "pause" ), "the pause menu keeps the keys" );
		Assert.IsFalse( ParkHud.WindowShortcutsAllowed( false, "confirmQuit" ), "a dialog keeps the keys" );
		Assert.IsFalse( ParkHud.WindowShortcutsAllowed( false, null ), "an overlay without a window name keeps the keys" );
		Assert.IsFalse( ParkHud.WindowShortcutsAllowed( false, "lobby" ), "the front end is not a HUD window" );
		foreach ( var shortcut in new[] { HudScreenShortcut.TimeFaster, HudScreenShortcut.TurnBlueprintLeft, HudScreenShortcut.LocateNextStaff } )
			Assert.IsFalse( ParkHud.OpensWindow( shortcut ), $"{shortcut} follows the plain park gate" );
		foreach ( var shortcut in new[] { HudScreenShortcut.BuyWindow, HudScreenShortcut.MapWindow, HudScreenShortcut.StaffApplicants } )
			Assert.IsTrue( ParkHud.OpensWindow( shortcut ), $"{shortcut} is a window key" );
	}

	[TestMethod]
	public void TimeKeysStepTheActualParkClockScale()
	{
		// The keys reach the park status the HUD is bound to (the same object the speed buttons use).
		var status = new StubParkStatus( 1000 );
		status.Speed = ParkHud.SteppedSpeed( status.Speed, 1 );
		Assert.AreEqual( (GameSpeed.Fast, 2f), (status.Speed, status.TimeScale), "numpad + twice from normal reaches the x2 clock scale" );
		status.Speed = ParkHud.SteppedSpeed( status.Speed, 1 );
		Assert.AreEqual( GameSpeed.Fastest, status.Speed );
		status.Speed = ParkHud.SteppedSpeed( status.Speed, 1 );
		Assert.AreEqual( GameSpeed.Fastest, status.Speed, "stepping up clamps at fastest" );
		Assert.AreEqual( GameSpeed.Fast, ParkHud.SteppedSpeed( GameSpeed.Fastest, -1 ), "one step down from fastest is the x2 scale" );
		status.Speed = ParkHud.SteppedSpeed( status.Speed, -3 );
		Assert.AreEqual( (GameSpeed.Paused, 0f), (status.Speed, status.TimeScale), "stepping down three times from fastest reaches the pause scale" );
		status.Speed = ParkHud.SteppedSpeed( status.Speed, -1 );
		Assert.AreEqual( GameSpeed.Paused, status.Speed, "stepping down clamps at paused" );
		Assert.AreEqual( GameSpeed.Paused, ParkHud.SteppedSpeed( GameSpeed.Fastest, -9 ), "a long hold down clamps at paused" );
	}

	[TestMethod]
	public void HappinessGaugeFillFollowsTheSampledVisitors()
	{
		Assert.AreEqual( 0f, ParkHud.GaugeFill( 0f ) );
		Assert.AreEqual( 0.5f, ParkHud.GaugeFill( 50f ) );
		Assert.AreEqual( 1f, ParkHud.GaugeFill( 100f ) );
		Assert.AreEqual( 1f, ParkHud.GaugeFill( 140f ), "the fill clamps at a full dish" );
		Assert.AreEqual( 0f, ParkHud.GaugeFill( -20f ), "a negative mean does not fill below empty" );
		Assert.IsNull( ParkHud.GaugeFill( null ), "no visitors in the park leaves the dish empty" );
		var empty = ParkHud.GaugeColour( 0 );
		var pleased = ParkHud.GaugeColour( 100 );
		Assert.IsTrue( empty.R > pleased.R && empty.G < pleased.G, "unhappy visitors fill red, happy visitors green" );
		Assert.AreEqual( 210, empty.A );
		Assert.AreEqual( (empty.R, empty.G, empty.B), (ParkHud.GaugeColour( 0 ).R, ParkHud.GaugeColour( 0 ).G, ParkHud.GaugeColour( 0 ).B), "the colour is deterministic for tests and drawing" );
	}

	[TestMethod]
	public void PauseMenuEntriesDisableWithoutTheirHostActions()
	{
		Assert.IsTrue( ParkHud.PauseMenuEntryEnabled( readOnlyVisit: false, UIStrings.Save, canLoad: false, canOptions: false ), "save works in a writable park" );
		Assert.IsFalse( ParkHud.PauseMenuEntryEnabled( readOnlyVisit: true, UIStrings.Save, canLoad: true, canOptions: true ), "a read-only visit cannot save" );
		Assert.IsFalse( ParkHud.PauseMenuEntryEnabled( false, UIStrings.Load, canLoad: false, canOptions: true ), "load without a host load screen is disabled" );
		Assert.IsFalse( ParkHud.PauseMenuEntryEnabled( false, UIStrings.Options, canLoad: true, canOptions: false ), "options without a host options screen is disabled" );
		Assert.IsTrue( ParkHud.PauseMenuEntryEnabled( true, UIStrings.Load, canLoad: true, canOptions: true ), "a read-only visit may still load another park" );
		Assert.IsTrue( ParkHud.PauseMenuEntryEnabled( false, UIStrings.QuitGame, canLoad: false, canOptions: false ), "quit never depends on the host" );
	}

	[TestMethod]
	public void SpaceBelongsToTheMapKeyNotTheHoveredButton()
	{
		// Hovering focuses a HUD button, but Space is the original map key (ShortcutFor(MapScreen)):
		// it must not also click whatever the pointer happens to rest on; Enter activates, as for a text field.
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!,
			new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1024, 768 ) };
		var screen = new UiScreen( "park" );
		var clicked = 0;
		var button = screen.Add( new UiButton { Id = "buy", Clicked = () => clicked++, Bounds = new UiRect( 100, 100, 300, 180 ) } );
		var centre = button.ScreenRect( context.Canvas ).Center;
		screen.Update( context, new UiInput( centre, false, false, false, false, UiKeys.None ) );
		Assert.AreSame( button, screen.Focused, "hovering the button focuses it" );
		screen.Update( context, new UiInput( centre, false, false, false, false, UiKeys.Accept | UiKeys.Space ) );
		Assert.AreEqual( 0, clicked, "Space does not activate the focused button" );
		screen.Update( context, UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( 1, clicked, "Enter still activates the focused button" );
	}

	[TestMethod]
	public void PauseIsThePlainPNotThePostcardsCtrlP()
	{
		// The original key table reserves Ctrl+P for the e-mail postcard (InputButton.SendEmailPostcard);
		// the pause key must not fire as part of that combination. Each case uses a fresh source so the
		// key is a new edge in both.
		try
		{
			Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.ControlLeft, Key.P } );
			Assert.IsFalse( new UiInputSource().Poll( 1, 1 ).Has( UiKeys.Pause ), "Ctrl+P is not pause" );
			Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.P } );
			Assert.IsTrue( new UiInputSource().Poll( 1, 1 ).Has( UiKeys.Pause ), "the plain P pauses" );
		}
		finally
		{
			Input.Keyboard = new Input.KeyboardInfo();
		}
	}
}
