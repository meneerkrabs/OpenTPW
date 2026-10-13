using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class HudModifierShortcutTests
{
	[TestInitialize]
	public void Initialize()
	{
		Input.Keyboard = new Input.KeyboardInfo();
		Input.KeysDown.Clear(); Input.LastKeysDown.Clear();
		Input.TextEntryActive = false;
		Input.Forward = Input.Right = 0;
	}

	[TestCleanup]
	public void Cleanup() => Initialize();

	[TestMethod]
	public void ControlVActivatesTheLocatorWithoutAlsoOpeningTheVisitorWindow()
	{
		Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.ControlLeft } );
		using var tap = Input.InjectKeyDown( Key.V );
		Input.UpdateButtons();
		Assert.IsTrue( Input.Pressed( InputButton.VisitorLocator ), "actual binding dispatch receives Ctrl+V" );
		Assert.IsTrue( Input.Pressed( InputButton.AllVisitors ), "the raw V binding also matches before HUD arbitration" );
		Assert.IsFalse( ParkHud.VisitorWindowShortcutPressed(), "screen dispatch must reserve Ctrl+V for the locator" );
	}

	[TestMethod]
	public void PlainVRemainsAOneFrameWindowEdge()
	{
		using var tap = Input.InjectKeyDown( Key.V );
		Input.UpdateButtons();
		Assert.IsTrue( ParkHud.VisitorWindowShortcutPressed() );
		Assert.IsFalse( Input.Pressed( InputButton.VisitorLocator ) );
		Input.UpdateButtons();
		Assert.IsFalse( ParkHud.VisitorWindowShortcutPressed(), "a consumed V tap does not toggle again" );
	}

	[TestMethod]
	public void RegisteredControlSAndControlPDoNotBecomePlainHudWindowActions()
	{
		using ( Input.InjectKeyDown( Key.ControlLeft, Key.S ) ) Input.UpdateButtons();
		Assert.IsTrue( Input.Pressed( InputButton.StaffLocator ) );
		Assert.AreEqual( HudScreenShortcut.None, ParkHud.ShortcutFor( InputButton.AllStaff ) );
		Initialize();
		Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.ControlLeft, Key.P } );
		using ( Input.InjectKeyDown( Key.P ) ) Input.UpdateButtons();
		Assert.IsTrue( Input.Pressed( InputButton.SendEmailPostcard ) );
		Assert.IsFalse( new UiInputSource().Poll( 1, 1 ).Has( UiKeys.Pause ) );
	}
	[TestMethod]
	public void FullyInjectedControlVAndRepeatedLocatorTapsStayOutOfTheWindowAction()
	{
		for ( var index = 0; index < 2; index++ )
		{
			using ( Input.InjectKeyDown( Key.ControlLeft, Key.V ) ) Input.UpdateButtons();
			Assert.IsTrue( Input.Pressed( InputButton.VisitorLocator ) );
			Assert.IsFalse( ParkHud.VisitorWindowShortcutPressed() );
			Assert.AreEqual( 0, Input.Keyboard.KeysDown.Count, "synthetic chord keys stay out of physical state" );
			Input.UpdateButtons();
		}
		using ( Input.InjectKeyDown( Key.V ) ) Input.UpdateButtons();
		Assert.IsTrue( ParkHud.VisitorWindowShortcutPressed(), "plain V works after the chord releases" );
	}

}
