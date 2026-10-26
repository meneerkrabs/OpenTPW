namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	private bool? previousApplicationFocus;
	private bool applicationPaused;

	/// <summary>The visible pause selection includes the focus-loss latch without changing the chosen running speed.</summary>
	private GameSpeed DisplayedGameSpeed => applicationPaused ? GameSpeed.Paused : Status.Speed;

	/// <summary>
	/// A visible park that loses observed focus stops until an explicit resume, as tp.exe's deactivation does (0x46B74C -> 0x4092A0(1), which builds the P window; a logged-in player's own park included).
	/// [EXT:offline-focus-pause] what differs from the original is host plumbing only: focus is sampled once per frame (a loss and a return between two samples is not seen), an initial lack of focus and
	/// offscreen automation are not deactivation, and the stopped state is shown through the speed buttons with the chosen speed stored. [EXT:online-coop-lockstep] a shared park and a read-only visit
	/// are the host's clock and take no local pause.
	/// </summary>
	internal void ObserveApplicationFocus( bool focused, bool offscreen )
	{
		if ( offscreen || level.IsReadOnlyVisit || level.CoopRole != CoopRole.None )
		{
			previousApplicationFocus = null;
			ClearApplicationPause();
			return;
		}
		// Existing named modal pauses keep their own lifetime. Do not turn their close into a new focus-latch obligation.
		if ( previousApplicationFocus == true && !focused && TimeScaleFor( 1f, Stack.Screens.Select( screen => screen.Name ) ) > 0 )
		{
			applicationPaused = true;
			// [BIN:TPW-EXE:0x46b74c focus loss] WM_ACTIVATEAPP deactivation of the window procedure 0x46b600 (it tests [0xF82884], which is 0 in every park, so a logged-in player's own park pauses too), calls 0x4092a0(1, 1) on the pause object 0x786b68:
			// it latches the pause and, for the constant 1, creates the P window (0x481440 -> 0x48a6e0 -> 0x4a2790, only when [0x7cb2cc] is still 0), so losing focus offline brings up the same backdrop as P.
			CreatePausePointerOverlay();
		}
		previousApplicationFocus = focused;
	}

	/// <summary>
	/// Ends the focus-loss latch together with the window it opened. [BIN:TPW-EXE:0x409300 resume] every resume (about 26 callers) runs 0x481450 -> 0x48a700 -> 0x4a27c0,
	/// which destroys [0x7cb2cc]; the speed choices of the HUD reach it the same way.
	/// </summary>
	private void ClearApplicationPause()
	{
		if ( !applicationPaused )
			return;
		applicationPaused = false;
		RetirePausePointerOverlay();
	}

	private void ToggleUserPause()
	{
		if ( applicationPaused && Status.Speed != GameSpeed.Paused )
			ClearApplicationPause(); // Resume the stored speed; do not convert focus pause into explicit user pause.
		else
		{
			// [BIN:TPW-EXE:0x409350 P toggle] the key row (0x40bf77) and the P window's own procedure (0x4a2706) call 0x409350, which first toggles the window [0x7cb2cc]
			// (jump thunk 0x481460 -> 0x48a720 -> 0x4a27f0) and then pauses or resumes. Besides P, only the focus-loss pause creates it (ObserveApplicationFocus, 0x4092a0(1));
			// a park loaded already paused, the speed buttons and the numpad never do.
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
		ClearApplicationPause();
		ClosePauseMenu();
	}
}
