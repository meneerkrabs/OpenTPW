namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	// A replacement identity can name multiple terminal rows. Selection and queued events own the instance instead.
	private sealed class ChallengeTubeRow( ChallengeMessageIdentity identity, int lifetime = 0, long origin = 0 )
	{
		public ChallengeMessageIdentity Identity { get; } = identity;
		public int Lifetime { get; } = lifetime;
		public bool IsTerminal => Lifetime > 0;
		public long Origin { get; set; } = origin;
		public bool Active { get; set; } = lifetime > 0;
		public bool PreExpiryEnabled { get; set; } = lifetime > 0;
		public bool PreExpiryReached { get; set; }
		public bool PreExpiryQueued { get; set; }
		public bool ExpiryQueued { get; set; }
		public long? ReleaseAt { get; set; }
		public bool Visible => ReleaseAt == null;

		public void Read()
		{
			Active = false;
			PreExpiryEnabled = false;
		}
	}

	private enum ChallengeTimerEvent { PreExpiry, Expiry }
	private Queue<(ChallengeTubeRow Row, ChallengeTimerEvent Kind)>? challengeTimerEvents;
	private bool challengeTimersSuspended;
	private bool challengeUserPaused;

	/// <summary>Injected monotonic, unscaled milliseconds. Set before posting rows; null uses the host monotonic clock.</summary>
	internal Func<long>? ChallengeMessageClock { get; set; }
	private long ChallengeUiMilliseconds => ChallengeMessageClock?.Invoke() ?? Environment.TickCount64;
	internal const int ChallengeTerminalReleaseDelay = 500;

	internal bool IsPendingMessageVisible( int index ) => index >= 0 && index < (pendingMessages?.Count ?? 0)
		&& pendingChallengeRows![index]?.Visible != false;

	/// <summary>Diagnostic state only; the pre-expiry flag records the timer, not a rendered particle.</summary>
	internal (bool Active, bool PreExpiryEnabled, bool PreExpiryReached, long? ReleaseAt)? ChallengeTerminalState( int index ) =>
		index >= 0 && index < (pendingChallengeRows?.Count ?? 0) && pendingChallengeRows![index] is { IsTerminal: true } row
			? (row.Active, row.PreExpiryEnabled, row.PreExpiryReached, row.ReleaseAt) : null;

	/// <summary>Inserts a new terminal instance even when its tag/context already exists. Replacement runs before this method.</summary>
	internal bool PostChallengeTerminal( ChallengeMessageRecord record, string text )
	{
		if ( record.Lifetime != 30000 || record.FirstAction != 0 || record.SecondAction != 0
			|| record.Tag == record.ReplacementTag || string.IsNullOrWhiteSpace( text ) || PendingMessages.Count >= MessageTubeCapacity )
			return false;
		if ( !PostGameMessage( text ) )
			return false;
		pendingChallengeRows![^1] = new ChallengeTubeRow( new( record.Tag, 0, 0 ), record.Lifetime, ChallengeUiMilliseconds );
		return true;
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
		var now = ChallengeUiMilliseconds;
		foreach ( var row in pendingChallengeRows )
			if ( row is { Active: true } )
				row.Origin = now;
	}

	/// <summary>
	/// Named managed mapping, not Hud.Paused or the simulation time scale: only the explicit user pause and these traced screens suspend.
	/// Direct load/save and postcard are excluded; load/save under Escape inherit the still-open pause screen.
	/// </summary>
	// [BIN:TPW-EXE:0x48c894 escape suspension] options 0x4a3a7b, map 0x5f0b8a and owned offline message boxes 0x47f27b also suspend.
	// ECON-058/UI-096: the offline simulation focus-loss latch does not own these timers; focus return is not game resume.
	// Other dialogs, online ownership gates and nested host screens are not claimed to reproduce the original's single global flag.
	internal static bool ChallengeMessageSuspensionFor( bool userPaused, IEnumerable<string> screens ) => userPaused
		|| screens.Any( name => name is PauseMenuScreen or MapScreenName or "options" or "confirmDelete" or "confirmDeleteAttraction" or "confirmDismissStaff" or WinningMessageScreen );

	private void SyncChallengeMessageSuspension() =>
		SetChallengeMessageTimersSuspended( ChallengeMessageSuspensionFor( challengeUserPaused, Stack?.Screens.Select( screen => screen.Name ) ?? Array.Empty<string>() ) );

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

	/// <summary>Produces bounded terminal timer events before HUD input; dispatch can observe a read/delete in between.</summary>
	// [BIN:TPW-EXE:0x661fe5 UI timer delivery] timers queue messages; reading stops future timers but queued expiry still needs the active guard.
	// ECON-058: this managed queue coalesces each one-shot terminal event. It does not emulate repeated interval delivery,
	// the whole native UI queue, disabled-timer phase advancement, or signed/unsigned 32-bit clock wrap behavior.
	internal void QueueChallengeTerminalTimers()
	{
		if ( challengeTimersSuspended || pendingChallengeRows == null )
			return;
		var now = ChallengeUiMilliseconds;
		foreach ( var row in pendingChallengeRows )
		{
			if ( row is not { Active: true, Visible: true } )
				continue;
			var elapsed = now - row.Origin;
			if ( row.PreExpiryEnabled && !row.PreExpiryQueued && elapsed >= row.Lifetime - 5000 )
			{
				(challengeTimerEvents ??= new()).Enqueue( (row, ChallengeTimerEvent.PreExpiry) );
				row.PreExpiryQueued = true;
			}
			if ( !row.ExpiryQueued && elapsed >= row.Lifetime )
			{
				(challengeTimerEvents ??= new()).Enqueue( (row, ChallengeTimerEvent.Expiry) );
				row.ExpiryQueued = true;
			}
		}
	}

	/// <summary>Queued events target row instances; reading a terminal before dispatch permanently suppresses its expiry.</summary>
	internal void DispatchChallengeTerminalTimers()
	{
		while ( challengeTimerEvents != null && challengeTimerEvents.TryDequeue( out var entry ) )
		{
			var row = entry.Row;
			if ( !row.Active || !row.Visible || pendingChallengeRows?.Contains( row ) != true )
				continue;
			if ( entry.Kind == ChallengeTimerEvent.PreExpiry )
			{
				row.PreExpiryEnabled = false;
				row.PreExpiryReached = true;
				// PTCL-908: 91 is a retained UI particle, not sound. No UI emitter ownership/rendering interface exists here.
				// Effect 90 on first visibility and effect 91 at this boundary remain omitted; never borrow world emitter slots.
			}
			else
				BeginChallengeTerminalRemoval( row, closePanel: false );
		}
	}

	private void BeginChallengeTerminalRemoval( ChallengeTubeRow row, bool closePanel )
	{
		if ( !row.Visible )
			return;
		row.Read();
		row.ReleaseAt = ChallengeUiMilliseconds + ChallengeTerminalReleaseDelay;
		if ( closePanel && ReferenceEquals( openedChallengeRow, row ) )
			CloseMessagePanel();
	}

	/// <summary>Delayed release uses the unscaled clock independently of lifetime suspension and retains occupancy until delivery.</summary>
	// [BIN:TPW-EXE:0x4a13e8 tube removal] ordinary removal hides before a 500 ms delayed release; cross-tag replacement releases synchronously.
	// [BIN:TPW-EXE:0x669d77 delayed UI queue] the delayed-message pump has no lifetime-timer suspension test.
	internal void ReleaseChallengeTerminalRows()
	{
		var now = ChallengeUiMilliseconds;
		for ( var index = (pendingChallengeRows?.Count ?? 0) - 1; index >= 0; index-- )
			if ( pendingChallengeRows![index]?.ReleaseAt is long due && now >= due )
				RemovePendingMessageAt( index );
	}
}
