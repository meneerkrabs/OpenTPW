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
	public void TheLocatorChordsActOverTheWindowWhosePersonTheyLocate()
	{
		Assert.IsTrue( ParkHud.ShortcutAllowed( HudScreenShortcut.LocateNextStaff, false, "employee" ), "Ctrl+S locates the open employee window's employee (0x481790)" );
		Assert.IsTrue( ParkHud.ShortcutAllowed( HudScreenShortcut.LocateNextVisitor, false, "visitorInfo" ), "Ctrl+V locates the visitor the open visitor window shows (0x4817c0)" );
		Assert.IsFalse( ParkHud.ShortcutAllowed( HudScreenShortcut.LocateNextStaff, false, "visitors" ) );
		Assert.IsFalse( ParkHud.ShortcutAllowed( HudScreenShortcut.LocateNextVisitor, false, "pause" ), "the pause menu keeps the keys" );
		Assert.IsTrue( ParkHud.ShortcutAllowed( HudScreenShortcut.LocateNextStaff, true, null ), "with no window open the park gate decides" );
		Assert.IsTrue( ParkHud.ShortcutAllowed( HudScreenShortcut.MapWindow, false, "map" ), "a window key still closes its own window" );
		Assert.IsFalse( ParkHud.ShortcutAllowed( HudScreenShortcut.TimeFaster, false, "employee" ), "other keys follow the park gate" );
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
		Assert.AreEqual( 511 / 1024f, ParkHud.GaugeFill( 50f ), "the value is truncated to an integer of 1024 as the original's setter takes it" );
		Assert.AreEqual( 1023 / 1024f, ParkHud.GaugeFill( 100f ), "the original's 0.01f factor is a hair under 0.01, so a mean of 100 sets 1023 of 1024" );
		Assert.AreEqual( 1f, ParkHud.GaugeFill( 140f ), "the fill clamps at a full dish" );
		Assert.AreEqual( 0f, ParkHud.GaugeFill( -20f ), "a negative mean does not fill below empty" );
		Assert.IsNull( ParkHud.GaugeFill( null ), "no visitors in the park leaves the dish empty" );
	}

	[TestMethod]
	public void HappinessGaugeValueIsTheOriginals0To1024Integer()
	{
		// tp.exe 0x4a1d3b-0x4a1d57: ftol( mean x 1024.0 x 0.01f ), then the type-9 gauge's setter 0x66d885 clamps to 0..1024
		Assert.AreEqual( 0, ParkHud.GaugeValue( 0f ) );
		Assert.AreEqual( 511, ParkHud.GaugeValue( 50f ), "50 x 10.24 is 512 less the float error of 0.01f, truncated" );
		Assert.AreEqual( 1023, ParkHud.GaugeValue( 100f ) );
		Assert.AreEqual( 1024, ParkHud.GaugeValue( 120f ), "the setter clamps at 1024" );
		Assert.AreEqual( 0, ParkHud.GaugeValue( -5f ), "the setter clamps at 0" );
	}

	[TestMethod]
	public void HappinessGaugeMeanIsTheOriginalsByteTruncatedMeanAndZeroWhileClosed()
	{
		// tp.exe 0x4c7bb0: 0 while the park-closed word is set, else the mean of the byte-truncated happiness of the in-park guests
		Assert.AreEqual( 74.5f, ParkHud.HappinessMean( new[] { 75.9f, 74.9f }, parkOpen: true ), "(75 + 74) / 2, not the mean of the raw values" );
		Assert.IsNull( ParkHud.HappinessMean( new[] { 80f, 90f }, parkOpen: false ), "a closed park leaves the dish empty even with visitors still in it" );
		Assert.IsNull( ParkHud.HappinessMean( System.Array.Empty<float>(), parkOpen: true ), "nobody in the park leaves the dish empty" );
		Assert.AreEqual( 0f, ParkHud.HappinessMean( new[] { 0.9f, 0f }, parkOpen: true ), "an unhappy park is a mean of zero, not an absent one" );
		Assert.AreEqual( 22f, ParkHud.HappinessMean( new[] { 300f, 0f }, parkOpen: true ), "the truncation keeps the low byte as the original's 'and 0xff' does: 300 is 44" );
		Assert.AreEqual( 2f, ParkHud.HappinessSampleSeconds, "the original's timer (0x4a1cea) re-arms the gauge every 2000 ms" );
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
