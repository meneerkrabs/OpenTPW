namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	private enum ChallengeTimerEvent { PreExpiry, Expiry }
	private Queue<(TubeRow Row, ChallengeTimerEvent Kind)>? challengeTimerEvents;
	private bool challengeTimersSuspended;
	private bool challengeUserPaused;

	/// <summary>Injected monotonic, unscaled milliseconds. Set before posting rows; null uses the host monotonic clock.</summary>
	internal Func<long>? ChallengeMessageClock { get; set; }
	private long ChallengeUiMilliseconds => ChallengeMessageClock?.Invoke() ?? Environment.TickCount64;

	/// <summary>The UI clock as the original's timers read it: a 32-bit millisecond counter whose differences wrap (0x65968E).</summary>
	private int ChallengeUiTicks => unchecked( (int)ChallengeUiMilliseconds );
	internal const int ChallengeTerminalReleaseDelay = MessageTubeSaveData.MaximumOfferReleaseDelay;

	/// <summary>An offer's tag lifetime: 90 s (the tag records of the offers, 0x4d3220), its warning timer 5 s earlier.</summary>
	internal const int ChallengeOfferLifetime = 90000;

	/// <summary>The row can be clicked: its tag window is shown and its hit-test attribute is still set (an answered or removed row is not).</summary>
	internal bool IsPendingMessageVisible( int index ) => index >= 0 && index < (pendingMessages?.Count ?? 0)
		&& pendingChallengeRows![index].Visible;

	/// <summary>The row's icon is drawn: it is not hidden (an answered row stays drawn until its removal).</summary>
	internal bool IsPendingMessageDrawn( int index ) => index >= 0 && index < (pendingMessages?.Count ?? 0)
		&& pendingChallengeRows![index].Drawn;

	/// <summary>Diagnostic state only: whether the expiry is armed, the warning timer enabled and fired, and the removal clock; the warning's particle is the row's own.</summary>
	internal (bool Active, bool PreExpiryEnabled, bool PreExpiryReached, long? ReleaseAt)? ChallengeTerminalState( int index ) =>
		index >= 0 && index < (pendingChallengeRows?.Count ?? 0) && pendingChallengeRows![index] is { IsTerminal: true } row
			? (row.ExpiryArmed, row.Warning?.Enabled == true, row.WarningFired, row.ReleaseAt) : null;

	/// <summary>Inserts a new terminal instance even when its tag/context already exists. Replacement runs before this method.</summary>
	internal bool PostChallengeTerminal( ChallengeMessageRecord record, string text )
	{
		if ( record.Lifetime != 30000 || record.FirstAction != 0 || record.SecondAction != 0
			|| record.Tag == record.ReplacementTag || string.IsNullOrWhiteSpace( text ) || PendingMessages.Count >= MessageTubeCapacity )
			return false;
		return AddTubeMessage( record.Tag, record.ReplacementTag, text, 0, 0, record.Lifetime, false, record.Icon, Array.Empty<GameMessageAction>() ) != null;
	}

	/// <summary>One suspend flag: only a suspended-to-resumed transition restarts active timer origins.</summary>
	// [BIN:TPW-EXE:0x662411 timer suspension] resume 0x662420 resets every timer's origin, without re-enabling stopped timers.
	internal void SetChallengeMessageTimersSuspended( bool suspended )
	{
		if ( challengeTimersSuspended == suspended )
			return;
		challengeTimersSuspended = suspended;
		if ( suspended || pendingChallengeRows == null )
			return;
		var now = ChallengeUiTicks;
		foreach ( var row in pendingChallengeRows )
		{
			row.Expiry?.Reset( now );
			row.Warning?.Reset( now );
		}
	}

	/// <summary>
	/// Named managed mapping, not Hud.Paused or the simulation time scale: only the explicit user pause and these traced screens suspend.
	/// Direct load/save and postcard are excluded; load/save under Escape inherit the still-open pause screen.
	/// </summary>
	// [BIN:TPW-EXE:0x48c894 escape suspension] options 0x4a3a7b, map 0x5f0b8a and owned offline message boxes 0x47f27b also suspend.
	// [BIN:TPW-EXE:0x48A6E0 pause suspends timers] the pause 0x4092A0(1) (the P key and the focus loss, 0x46B74C) runs 0x481440 -> 0x48A6E0, which suspends the UI timers (0x662411) and builds the P window; the resume 0x409300 -> 0x481450 -> 0x48A700 resumes them (0x662420) and destroys it. The focus-loss latch therefore owns the timers until an explicit resume.
	// The other pauses (0x4092A0(0, 0): the Escape menu, the box, options, map) suspend directly (0x662411 at 0x48C894, 0x47F27B, 0x4A3A7B, 0x5F0B8A); loans windows and the postcard pause the game clock only.
	// Other dialogs, online ownership gates and nested host screens are not claimed to reproduce the original's single global flag.
	internal static bool ChallengeMessageSuspensionFor( bool userPaused, IEnumerable<string> screens, bool focusPaused = false ) => userPaused || focusPaused
		|| screens.Any( name => name is PauseMenuScreen or MapScreenName or "options" or "confirmDelete" or "confirmDeleteAttraction" or "confirmDismissStaff" or WinningMessageScreen );

	private void SyncChallengeMessageSuspension() =>
		SetChallengeMessageTimersSuspended( ChallengeMessageSuspensionFor( challengeUserPaused, Stack?.Screens.Select( screen => screen.Name ) ?? Array.Empty<string>(), applicationPaused ) );

	private void FinishChallengeMessageFrame()
	{
		SyncChallengeMessageSuspension();
		DispatchChallengeTerminalTimers();
		ReleaseChallengeTerminalRows();
	}

	/// <summary>The explicit pause key and host speed controls, rather than an arbitrary zero simulation scale, own user suspension.</summary>
	internal bool SetUserGameSpeed( GameSpeed speed )
	{
		// an explicit speed choice also ends the focus-loss pause (and the window it opened)
		ClearApplicationPause();
		// a park's speed is part of its state and goes through its commands; a park without an economy keeps its speed in the HUD's status
		if ( level?.Park != null )
		{
			if ( !level.Commands.Issue( new SetGameSpeedCommand( speed ) ).Accepted )
				return false;
		}
		else
			Status.Speed = speed;
		// [BIN:TPW-EXE:0x409300 resume] a resume destroys the P window (0x481450 -> 0x48a700 -> 0x4a27c0; about 26 callers), so any speed other than Paused closes it.
		// The speed itself never creates it: only P and the focus-loss pause do (ParkHud.ApplicationPause).
		if ( speed != GameSpeed.Paused )
			RetirePausePointerOverlay();
		challengeUserPaused = speed == GameSpeed.Paused;
		SyncChallengeMessageSuspension();
		return true;
	}

	/// <summary>
	/// [EXT:online-coop-speed] Whether the park's speed is another machine's: a guest's park follows the host's stream, and only the host pauses or changes speed (docs/COOP.md, "Money and
	/// rights (C4)"; the user's decision Q8). The speed buttons draw subdued for it; each speed input, the pause key and the numpad's included, issues its command, which the session's
	/// rights refuse here with the host-only note. A session is online, where no focus-loss pause latches (<see cref="ObserveApplicationFocus"/>).
	/// </summary>
	private bool SpeedIsTheHosts => level?.Park != null && level.Commands.FollowsStream;

	/// <summary>Produces bounded timer events before HUD input; dispatch can observe a read/delete in between.</summary>
	// [BIN:TPW-EXE:0x661fe5 UI timer delivery] the pump (0x6622D2) ticks every timer in creation order; a timer whose interval has passed posts its message to the window's queue (0x658ccc), which the frame's pump delivers after the whole tick
	// [BIN:TPW-EXE:0x66203A tick guards] the message is posted only for an enabled timer of a window that exists while the UI is active ([0x77c488]: set by the window's activation 0x659333, cleared by the deactivation, which is a game pause here: UI timers are suspended then); the elapsed time is a signed 32-bit difference and the origin keeps the remainder
	// A tick reports at most one message per timer: the window's guards (the expiry's byte +0x134, the warning's property 9) make every later message of the same tick a no-op.
	internal void QueueChallengeTerminalTimers()
	{
		if ( challengeTimersSuspended || pendingChallengeRows == null )
			return;
		var now = ChallengeUiTicks;
		foreach ( var row in pendingChallengeRows )
		{
			if ( row.Expiry is { } expiry && expiry.Tick( now ) > 0 )
				(challengeTimerEvents ??= new()).Enqueue( (row, ChallengeTimerEvent.Expiry) );
			if ( row.Warning is { } warning && warning.Tick( now ) > 0 )
				(challengeTimerEvents ??= new()).Enqueue( (row, ChallengeTimerEvent.PreExpiry) );
		}
	}

	/// <summary>Queued events target row instances; a message that finds its row gone is dropped.</summary>
	internal void DispatchChallengeTerminalTimers()
	{
		while ( challengeTimerEvents != null && challengeTimerEvents.TryDequeue( out var entry ) )
		{
			var row = entry.Row;
			if ( pendingChallengeRows?.Contains( row ) != true )
				continue;
			if ( entry.Kind == ChallengeTimerEvent.PreExpiry )
				WarnTubeRow( row );
			else if ( row.ExpiryArmed )
			{
				// [BIN:TPW-EXE:0x48dd53 tag expiry] timer 0x80080 acts only while the byte +0x134 is set: it hides the tag, clears the byte and posts the removal 0x100002 (0x48dd5d-0x48dd7b); the removal puts the panel away when the tag is the selected one at its end (0x48dd19-0x48dd28)
				row.ExpiryArmed = false;
				BeginTubeRemoval( row, hide: true, closePanelNow: false, disarm: false );
			}
		}
	}

	/// <summary>
	/// Starts a row's removal: its hit-test attribute is cleared (and the row hidden when <paramref name="hide"/>) and the destruction follows 500 ms later; a row whose attribute is already cleared is left alone.
	/// </summary>
	// [BIN:TPW-EXE:0x4a13e8 tube removal] 0x100002 acts only on a window whose attribute bit 1 is set (0x4a13ee-0x4a13f5), clears it (0x65d9dd with 0) and posts message 5 with 500 ms delay (0x4a1408-0x4a1419); with the flag 1 it sends message 5 at once (0x4a1421-0x4a142d)
	// [BIN:TPW-EXE:0x48e08a right click] a right click and the bomb hide the window first (0x48e081-0x48e087) and clear the armed byte (a right click only); the action result 2 and the expiry's hide differ as the callers say
	private void BeginTubeRemoval( TubeRow row, bool hide, bool closePanelNow, bool disarm )
	{
		if ( row.ReleaseAt != null )
			return;
		// Bind before hiding: cleanup of an answered/stale offer must never decline a newer follow-up.
		if ( row.IsOffer )
			row.PendingOfferIdentity = SameUnansweredOffer( row.OfferIdentity, CurrentUnansweredOffer ) ? row.OfferIdentity : null;
		if ( disarm )
			row.ExpiryArmed = false;
		if ( hide )
			HideTubeRow( row );
		row.ReleaseAt = ChallengeUiMilliseconds + ChallengeTerminalReleaseDelay;
		if ( closePanelNow && ReferenceEquals( openedChallengeRow, row ) )
			CloseMessagePanel();
	}

	/// <summary>Delayed release uses the unscaled clock independently of lifetime suspension and retains occupancy until delivery.</summary>
	// [BIN:TPW-EXE:0x4a13e8 tube removal] ordinary removal hides before a 500 ms delayed release; cross-tag replacement releases synchronously.
	// [BIN:TPW-EXE:0x669d77 delayed UI queue] the delayed-message pump has no lifetime-timer suspension test.
	internal void ReleaseChallengeTerminalRows()
	{
		var now = ChallengeUiMilliseconds;
		for ( var index = (pendingChallengeRows?.Count ?? 0) - 1; index >= 0; index-- )
		{
			var row = pendingChallengeRows![index];
			if ( row.ReleaseAt is long due && now >= due || row.ClearAt is long clear && now >= clear )
				RemovePendingMessageAt( index );
		}
	}
}
