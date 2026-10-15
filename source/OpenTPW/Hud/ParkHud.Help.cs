using Veldrid;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>Whether the HUD shows contextual help: the global Popup help option, which Ctrl+H flips.</summary>
	// [BIN:TPW-EXE:0x40c5d0 system key row 1 'toghelp'] Ctrl+H (0x4817f0) shows or hides the global help object (0x77c48c) and stores the new state in options byte 0x78d90e = options +0x36, the Popup help (TooltipsOn) option the options screen edits; the help box geometry is UI-010
	public bool HelpBarVisible => GameOptions.Current.PopupHelp;

	/// <summary>Flips the global Popup help option (Ctrl+H) and lets the host persist it.</summary>
	internal void ToggleGlobalHelp()
	{
		GameOptions.Current.PopupHelp = !GameOptions.Current.PopupHelp;
		host?.OptionsChanged?.Invoke();
	}

	private bool helpChordWasDown;

	private static bool HelpChordDown => Input.Down( InputButton.ToggleHelpBar )
		|| Input.Keyboard.KeysDown.Contains( Input.ChordKey( InputButton.ToggleHelpBar ) )
			&& (Input.Keyboard.KeysDown.Contains( Key.ControlLeft ) || Input.Keyboard.KeysDown.Contains( Key.ControlRight ));

	private bool SampleHelpShortcutPressed()
	{
		// Track the physical chord even while typing, in a dialog, or riding. Releasing that gate
		// cannot turn a held chord into a new press; injected taps use the existing button binding.
		var down = HelpChordDown;
		var pressed = down && !helpChordWasDown;
		helpChordWasDown = down;
		return pressed;
	}
}
