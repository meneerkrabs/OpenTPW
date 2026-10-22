namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	private bool? previousApplicationFocus;
	private bool applicationPaused;

	/// <summary>The visible pause selection includes the focus-loss latch without changing the chosen running speed.</summary>
	private GameSpeed DisplayedGameSpeed => applicationPaused ? GameSpeed.Paused : Status.Speed;

	/// <summary>
	/// [EXT:offline-focus-pause] A visible offline park that loses observed focus stops until an explicit resume.
	/// Initial lack of focus and offscreen automation are not deactivation. Native modal/audio/device parity is not claimed.
	/// </summary>
	internal void ObserveApplicationFocus( bool focused, bool offscreen, bool loggedIn )
	{
		// No local focus-driven clock ownership in online visits/chat sessions. Future co-op requires host-issued commands.
		if ( offscreen || loggedIn || level.IsReadOnlyVisit )
		{
			previousApplicationFocus = null;
			applicationPaused = false;
			return;
		}
		// Existing named modal pauses keep their own lifetime. Do not turn their close into a new focus-latch obligation.
		if ( previousApplicationFocus == true && !focused && TimeScaleFor( 1f, Stack.Screens.Select( screen => screen.Name ) ) > 0 )
			applicationPaused = true;
		previousApplicationFocus = focused;
	}

	private void ToggleUserPause()
	{
		if ( applicationPaused && Status.Speed != GameSpeed.Paused )
			applicationPaused = false; // Resume the stored speed; do not convert focus pause into explicit user pause.
		else
		{
			// [BIN:TPW-EXE:0x4a27f0 P window toggle] the P key (thunk 0x48a720, referenced from the key table at 0x481460) is the only caller that
			// creates or destroys the window [0x7cb2cc]; a park loaded already paused, the speed buttons and the numpad never create it.
			var pausing = Status.Speed != GameSpeed.Paused;
			if ( SetUserGameSpeed( pausing ? GameSpeed.Paused : GameSpeed.Normal ) )
			{
				if ( pausing ) CreatePausePointerOverlay();
				else RetirePausePointerOverlay();
			}
		}
	}

	private void ResumeGameAfterApplicationPause()
	{
		applicationPaused = false;
		ClosePauseMenu();
	}
}
