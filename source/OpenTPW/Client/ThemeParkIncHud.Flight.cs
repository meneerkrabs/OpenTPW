using OpenTPW.UI.Original;

namespace OpenTPW;

public sealed partial class ThemeParkIncHud
{
	/// <summary>Whether a camera flight of the park's cutscene thing has its full-screen input window up: the HUD is hidden and takes no input but the flight's skip.</summary>
	public bool FlightActive => level.Park?.Economy.TpiCutscene is { OverlayOpen: true };

	/// <summary>
	/// A frame under a camera flight's input window: Escape and the right button's release ask the flight to skip (only a skippable one, the zone arrival, takes it); every other input stays with the
	/// window, so the park's tools and the HUD wait. True while the window is up.
	/// </summary>
	// [BIN:TPI-EXE:0x5D03B0 flight input] the input window 0x84BE91(1, 1, 0xB71B07, 0, 0, 0x7FF, 0x5FF) covers the whole screen; its procedure takes message 0x1000A with key 0x1B (Escape) to 0x5D0290 when skippable and
	// passes the rest to the default procedure 0x84D7F9; the window procedure's WM_RBUTTONUP (0x4D0A38) and the "menu" key action (0x42FCD0) reach 0x5D0290 too
	// [BIN:TPI-EXE:0x50AF20 interface locked] the start locks the interface (0x50AF20, 0x503BA0, 0x7BAE30) and the end shows it again (0x50AC90): the HUD is not drawn under the window
	private bool UpdateFlight( UiInput input )
	{
		if ( level.Park?.Economy.TpiCutscene is not { OverlayOpen: true } cutscene )
			return false;
		if ( input.Has( UiKeys.Back ) || input.RightReleased )
			cutscene.RequestSkip();
		if ( Camera.Mode is ParkCameraMode camera )
		{
			camera.UiCapturesKeyboard = true;
			camera.ModalOpen = true;
		}
		return true;
	}
}
