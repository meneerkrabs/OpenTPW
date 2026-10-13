using Veldrid;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>Whether the HUD shows contextual help; until explicitly toggled it follows the global option.</summary>
	// [APPROX:UI-920] Ctrl+H toggles the existing top-edge contextual popup instead of an untraced original help-bar layout — evidence needed: original help-bar geometry and toggle state lifetime
	public bool HelpBarVisible => helpBarVisibility ?? GameOptions.Current.PopupHelp;

	private bool? helpBarVisibility;

	private bool helpChordWasDown;

	private static bool HelpChordDown => Input.Down( InputButton.ToggleHelpBar )
		|| Input.Keyboard.KeysDown.Contains( Key.H )
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
