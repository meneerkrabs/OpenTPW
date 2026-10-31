namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	private bool soundPauseOwned;
	private bool soundPauseApplication;

	/// <summary>
	/// Whether an owner of the original's game pause holds the park now: the pause menu and the screens opened from it, the map, the postcard, a pausing message box, the P window and
	/// the deactivation latch. The speed buttons' pause and the texture-pack download's wait are not owners (they never call 0x4092A0 / 0x409350), and a shared park or a read-only visit
	/// takes no local pause at all ([EXT:online-coop-lockstep]).
	/// </summary>
	// [BIN:TPW-EXE:0x4092a0 game pause] the callers are the pause menu 0x48c87e, the map 0x5f0b7f, the postcard 0x4a93d7, the message boxes 0x47f26c, the three screens 0x49efba, 0x49f29f, 0x4a3a6c and the deactivation 0x46b74c; the only other caller of the pause object is the P key (0x409350, from 0x40bf77 and the P window 0x4a2706)
	private bool SoundPauseOwnerHolds => !OnlineSession
		&& (applicationPaused || pausePointerOverlay != null
			|| Stack.Screens.Any( screen => screen.Name is PauseMenuScreen or MapScreenName or PostcardScreen || IsPausingMessageBox( screen.Name ) ));

	/// <summary>
	/// Hands the sound side of the game pause the frame's change of owners (<see cref="GamePauseSound"/>): the deactivation latch is the pause of the application (the mixer), any other owner the
	/// game pause (the advisor's speech and the listener's height), and the last owner's close is the resume, which undoes whatever the last pause did.
	/// </summary>
	/// <remarks>The original calls the pause at each owner's open and the resume at its close; a menu's open clears the pause object's gate (+0x3C), so the owners opened from it call nothing, and the deactivation finds the gate shut while a menu holds it (<see cref="ObserveApplicationFocus"/>). The change of the set is that sequence.</remarks>
	internal void ObservePauseSound()
	{
		var owned = SoundPauseOwnerHolds;
		var application = applicationPaused && !OnlineSession;
		if ( application && !soundPauseApplication )
			GamePauseSound.Pause( byDeactivation: true );
		else if ( owned && !soundPauseOwned && !application )
			GamePauseSound.Pause( byDeactivation: false );
		else if ( !owned && soundPauseOwned )
			GamePauseSound.Resume();
		soundPauseOwned = owned;
		soundPauseApplication = application;
	}
}
