using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Veldrid;

namespace OpenTPW.Tests;

/// <summary>
/// The scoped keyboard injection the native smoke tests use to press the HUD's window keys
/// (<see cref="Input.InjectKeyDown"/>): an injected key must reach its bound buttons exactly like a
/// physical tap — pressed for one frame, released the next — without entering the persistent keyboard
/// state, the WASD pan axes or a modifier combination, and a disposed scope must cancel keys that were
/// never consumed. Each test drives the button rebuild the frame loop runs (<see cref="Input.UpdateButtons"/>).
/// </summary>
[TestClass]
public class InputInjectionTests
{
	[TestCleanup]
	public void Cleanup()
	{
		// The injection queue is global: drain whatever a failing test left, then clear the button state.
		Input.Keyboard = new Input.KeyboardInfo();
		Input.TextEntryActive = false;
		Input.UpdateButtons();
		Input.UpdateButtons();
	}

	[TestMethod]
	public void AnInjectedKeyPressesItsButtonForExactlyOneFrame()
	{
		Input.Keyboard = new Input.KeyboardInfo();
		Input.UpdateButtons();
		Assert.IsFalse( Input.Pressed( InputButton.AllVisitors ), "nothing is pressed before the injection" );

		Input.InjectKeyDown( Key.V );
		Input.UpdateButtons();
		Assert.IsTrue( Input.Pressed( InputButton.AllVisitors ), "the injected V presses All Visitors like a physical tap" );
		Assert.IsTrue( Input.Down( InputButton.AllVisitors ) );
		Assert.IsFalse( Input.Pressed( InputButton.VisitorLocator ), "without Ctrl the same key does not press the Ctrl+V locator" );

		Input.UpdateButtons();
		Assert.IsFalse( Input.Down( InputButton.AllVisitors ), "the next frame releases the injected key" );
		Assert.IsTrue( Input.Released( InputButton.AllVisitors ), "the release edge follows, as after a physical tap" );
	}

	[TestMethod]
	public void AnInjectedKeyBehavesLikeTheSamePhysicalKey()
	{
		Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.V } );
		Input.UpdateButtons();
		var physical = Input.Pressed( InputButton.AllVisitors );
		Input.Keyboard = new Input.KeyboardInfo();
		Input.UpdateButtons();
		Input.InjectKeyDown( Key.V );
		Input.UpdateButtons();
		Assert.AreEqual( physical, Input.Pressed( InputButton.AllVisitors ),
			"the injected V and the physically held V press the same button" );
	}

	[TestMethod]
	public void InjectedKeysNeverEnterTheKeyboardStateOrThePanAxes()
	{
		Input.Keyboard = new Input.KeyboardInfo();
		Input.UpdateButtons();
		Input.InjectKeyDown( Key.V, Key.W, Key.A, Key.S );
		Input.UpdateButtons();
		Assert.IsFalse( Input.Keyboard.KeysDown.Contains( Key.V ) || Input.Keyboard.KeysDown.Contains( Key.W ),
			"the persistent keyboard state stays physical (a release event for it would never come)" );
		Assert.AreEqual( 0f, Input.Forward, "the WASD pan axes stay with the physical keyboard (the camera lane owns them)" );
		Assert.AreEqual( 0f, Input.Right );
	}

	[TestMethod]
	public void TextEntryKeepsTheInjectedKeysFromTheButtons()
	{
		Input.Keyboard = new Input.KeyboardInfo();
		Input.TextEntryActive = true;
		try
		{
			Input.InjectKeyDown( Key.V );
			Input.UpdateButtons();
			Assert.IsFalse( Input.Down( InputButton.AllVisitors ), "a focused text field swallows the injected key like a real one" );
		}
		finally
		{
			Input.TextEntryActive = false;
		}
	}

	[TestMethod]
	public void ADisposedScopeCancelsKeysTheFrameNeverConsumed()
	{
		Input.Keyboard = new Input.KeyboardInfo();
		Input.UpdateButtons();
		using ( Input.InjectKeyDown( Key.V ) )
		{
			// The frame aborts before its button rebuild: disposing must take the queued key back.
		}
		Input.UpdateButtons();
		Assert.IsFalse( Input.Down( InputButton.AllVisitors ), "a cancelled injection presses nothing" );
	}

	[TestMethod]
	public void RepeatInjectionsTwoFramesApartPressTwice()
	{
		Input.Keyboard = new Input.KeyboardInfo();
		Input.UpdateButtons();
		Input.InjectKeyDown( Key.B );
		Input.UpdateButtons();
		Input.UpdateButtons();
		Input.InjectKeyDown( Key.B );
		Input.UpdateButtons();
		Assert.IsTrue( Input.Pressed( InputButton.BuyAttractions ), "a second tap a frame later presses again (toggle keys rely on the edge)" );
	}

	[TestMethod]
	public void DisposingAConsumedScopeCannotCancelANewerScopeForTheSameKey()
	{
		Input.Keyboard = new Input.KeyboardInfo();
		Input.UpdateButtons();
		var consumed = Input.InjectKeyDown( Key.V );
		Input.UpdateButtons();
		Input.UpdateButtons();
		using var pending = Input.InjectKeyDown( Key.V );
		consumed.Dispose();
		Input.UpdateButtons();
		Assert.IsTrue( Input.Pressed( InputButton.AllVisitors ), "disposing an old scope must not cancel a newer tap" );
	}

	[TestMethod]
	public void OverlappingScopesCancelOnlyTheirOwnKeys()
	{
		Input.Keyboard = new Input.KeyboardInfo();
		Input.UpdateButtons();
		using var visitors = Input.InjectKeyDown( Key.V );
		var both = Input.InjectKeyDown( Key.V, Key.B );
		both.Dispose();
		both.Dispose();
		Input.UpdateButtons();
		Assert.IsTrue( Input.Pressed( InputButton.AllVisitors ) );
		Assert.IsFalse( Input.Down( InputButton.BuyAttractions ) );
	}

	[TestMethod]
	public void SyntheticTapCannotReleaseOrRepeatAPhysicallyHeldKey()
	{
		Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.V } );
		Input.UpdateButtons();
		using var tap = Input.InjectKeyDown( Key.V );
		Input.UpdateButtons();
		Assert.IsTrue( Input.Down( InputButton.AllVisitors ) );
		Assert.IsFalse( Input.Pressed( InputButton.AllVisitors ) );
		Input.UpdateButtons();
		Assert.IsTrue( Input.Down( InputButton.AllVisitors ) );
		Assert.IsFalse( Input.Released( InputButton.AllVisitors ) );
		Input.Keyboard = new Input.KeyboardInfo();
		Input.UpdateButtons();
		Assert.IsTrue( Input.Released( InputButton.AllVisitors ) );
	}

	[TestMethod]
	public void PhysicalModifiersCombineWithInjectedKeysWithoutBeingInjectedAsText()
	{
		Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.ControlLeft } );
		Input.UpdateButtons();
		using var tap = Input.InjectKeyDown( Key.V );
		var typed = Input.Typed;
		var keyPresses = Input.KeyPresses;
		Input.UpdateButtons();
		Assert.IsTrue( Input.Pressed( InputButton.VisitorLocator ) );
		Assert.AreEqual( typed, Input.Typed );
		Assert.AreSame( keyPresses, Input.KeyPresses );
		Input.UpdateButtons();
		Assert.IsTrue( Input.Released( InputButton.VisitorLocator ) );
		Assert.IsTrue( Input.Keyboard.KeysDown.Contains( Key.ControlLeft ) );
	}

	[TestMethod]
	public void TextEntryConsumesTheTapWithoutReplayingItAfterFocusLeaves()
	{
		Input.Keyboard = new Input.KeyboardInfo();
		Input.TextEntryActive = true;
		using var tap = Input.InjectKeyDown( Key.V );
		Input.UpdateButtons();
		Input.TextEntryActive = false;
		Input.UpdateButtons();
		Assert.IsFalse( Input.Down( InputButton.AllVisitors ) );
	}
}
