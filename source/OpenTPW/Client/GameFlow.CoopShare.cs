using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>[EXT:online-coop-session] What the online screens ask of the game to share its open park (C2d-2; <see cref="UI.OnlineScreens.ShowBuildTogether"/>).</summary>
internal interface ICoopHosting
{
	/// <summary>Why the open park cannot be shared now (<see cref="CoopShareRefusal.None"/>: it can), or null when no park is open.</summary>
	CoopShareRefusal? ShareRefusal { get; }

	/// <summary>The attempt in progress, or the last one with its outcome; null when there is none.</summary>
	CoopShare? Sharing { get; }

	/// <summary>Starts sharing the open park; false, with nothing started, when it may not (no park, a transition, logged out, another attempt, a refusal).</summary>
	bool StartSharing( CoopShareRequest request );

	/// <summary>Forgets a finished attempt (the screen goes back to the start).</summary>
	void DismissSharing();

	/// <summary>The player's own saved parks that the lobby can share (C2d-2b: not the sandbox, not a shipped park), read now; none while a park is open.</summary>
	IReadOnlyList<ParkLoadEntry> SavedParks();

	/// <summary>
	/// Loads the player's own saved park <paramref name="park"/> (its extensions asked about first) and shares it with <paramref name="request"/> once it is the
	/// game's park (C2d-2b); false, with nothing started, when it may not (a park is open, a transition, logged out, another attempt, not an own save).
	/// </summary>
	bool ShareSavedPark( ParkLoadEntry park, CoopShareRequest request );

	/// <summary>Gives up the share of a saved park that waits for its load (the lobby screen's Back), with that load; nothing when none waits.</summary>
	void CancelSavedParkShare();
}

/// <summary>
/// [EXT:online-coop-session] The game's sharing of its open park (C2d-2; docs/COOP.md, "Sharing the park from the game"): the pause menu's Build Together starts an
/// attempt (<see cref="CoopShare"/>), which the flow steps once a frame right after the online pump, and the HUD shows the shared park's status line.
/// </summary>
internal sealed partial class GameFlow : ICoopHosting
{
	/// <summary>How long the status line says that the shared park ended (when the player did not end it).</summary>
	internal const double SharingEndedSeconds = 10;

	/// <summary>The attempt to share the open park, until a new one replaces it or the player dismisses its outcome.</summary>
	private CoopShare? coopShare;

	/// <summary>A dismissed attempt whose session ended without the player ending it: the status line says so for its time all the same.</summary>
	private CoopShare? endedNotice;

	/// <summary>
	/// [EXT:online-coop-session] The share that goes with a saved park's load (C2d-2b): the player's choices, the server and the account that asked for it, and the
	/// queued load once the save's extensions were asked about.
	/// </summary>
	private sealed class SavedParkShare( CoopShareRequest request, (Uri? Server, string? Account) login )
	{
		public CoopShareRequest Request { get; } = request;
		public (Uri? Server, string? Account) Login { get; } = login;
		public Action? Load { get; set; }
	}

	/// <summary>The share of a saved park that waits for its load; null when none waits.</summary>
	private SavedParkShare? savedParkShare;

	/// <summary>The server and the account the online session is logged in on now.</summary>
	private (Uri? Server, string? Account) OnlineLogin => (Online.Client?.ServerUrl, Online.Client?.Session?.Name);

	/// <summary>The attempt the status line speaks of: the current one, or a dismissed one whose end it still shows.</summary>
	internal CoopShare? StatusShare => coopShare ?? endedNotice;

	/// <inheritdoc/>
	public CoopShare? Sharing => coopShare;

	/// <inheritdoc/>
	public CoopShareRefusal? ShareRefusal => Level is { } level ? CoopSession.ShareRefusal( level ) : null;

	/// <inheritdoc/>
	public bool StartSharing( CoopShareRequest request )
	{
		ArgumentNullException.ThrowIfNull( request );
		if ( !FeatureGates.Online || Level is not { } level || coopJoin != null || parkBuild != null || pending != null )
			return false;
		if ( coopShare is { Stage: not CoopShareStage.Done } || !Online.IsLoggedIn || CoopSession.ShareRefusal( level ) != CoopShareRefusal.None )
			return false;
		coopShare?.Dispose();
		endedNotice = null;
		coopShare = new CoopShare( level, Online.Coop, request, CoopSessionClock );
		return true;
	}

	/// <inheritdoc/>
	public void DismissSharing()
	{
		if ( coopShare is not { Stage: CoopShareStage.Done } done )
			return;
		done.Dispose();
		coopShare = null;
		if ( done is { Outcome: CoopShareOutcome.Ended, EndedBySelf: false } )
			endedNotice = done;
	}

	/// <inheritdoc/>
	public IReadOnlyList<ParkLoadEntry> SavedParks() => Level == null ? FindLoadEntries().Where( entry => entry.OwnSavePath != null ).ToArray() : Array.Empty<ParkLoadEntry>();

	/// <inheritdoc/>
	public bool ShareSavedPark( ParkLoadEntry park, CoopShareRequest request )
	{
		ArgumentNullException.ThrowIfNull( park );
		ArgumentNullException.ThrowIfNull( request );
		if ( !FeatureGates.Online || Level != null || park.OwnSavePath is not { } path || coopJoin != null || parkBuild != null || pending != null )
			return false;
		if ( coopShare is { Stage: not CoopShareStage.Done } || !Online.IsLoggedIn )
			return false;
		var share = savedParkShare = new SavedParkShare( request, OnlineLogin );
		// [EXT:save-extension-switch] the save's extensions are asked about first, as for its load; the share goes with the queued load, so another transition drops it
		ConfirmExtensions( path, change =>
		{
			// given up while the extensions were asked about
			if ( !ReferenceEquals( savedParkShare, share ) )
				return;
			QueueProgressLevel( park.Level, () => LoadSharedPark( park with { Switch = change }, share ) );
			share.Load = pending;
		} );
		return true;
	}

	/// <inheritdoc/>
	public void CancelSavedParkShare()
	{
		if ( savedParkShare is not { } share )
			return;
		savedParkShare = null;
		// Back means never mind: the load that waits for the level's files is not run either
		if ( share.Load != null && ReferenceEquals( pending, share.Load ) )
		{
			pending = null;
			pendingLevel = null;
		}
	}

	/// <summary>The load of a saved park to share (C2d-2b): the load, then the share of the park it made, unless the share was given up meanwhile.</summary>
	private void LoadSharedPark( ParkLoadEntry park, SavedParkShare share )
	{
		var wanted = ReferenceEquals( savedParkShare, share );
		savedParkShare = null;
		var before = Level;
		LoadPark( park, overFrames: true );
		if ( wanted )
			ShareLoadedPark( before, share.Request, share.Login );
	}

	/// <summary>
	/// Shares the park a load made (C2d-2b): only a park that replaced <paramref name="before"/> (a failed load shares nothing and leaves the lobby as it was), and only
	/// on the server and account that asked (<paramref name="login"/>). The park pauses under the pause menu, and its Build Together screen shows the attempt, or why
	/// it could not start, with the player's choices kept for another try (also on another login, where the player decides). With online play turned off by the
	/// load's extension choices, a message says why nothing was shared.
	/// </summary>
	internal void ShareLoadedPark( Level? before, CoopShareRequest request, (Uri? Server, string? Account) login )
	{
		if ( Level is not { } loaded || ReferenceEquals( loaded, before ) )
			return;
		// the unshared park pauses under the screens, as when the pause menu opens them; a shared park runs on under them
		Hud?.OpenPauseMenu();
		if ( !FeatureGates.Online )
		{
			if ( Hud is { } paused )
				paused.Stack.Push( UiDialogs.Message( ShareOnlineOffScreenName, () => Strings.Extra( OpenTpwText.ShareOnlineOff ),
					(() => Strings.Extra( OpenTpwText.Back ), () => paused.Stack.Pop()) ) );
			return;
		}
		if ( OnlineLogin == login )
			StartSharing( request );
		if ( Hud is { } hud )
			CreateOnlineScreens( hud.Stack ).ShowBuildTogether( request );
	}

	/// <summary>The message's screen name when a loaded saved park was not shared because online play is off.</summary>
	internal const string ShareOnlineOffScreenName = "shareOnlineOff";

	/// <summary>
	/// The share attempt's step, once a frame right after the online pump and before the co-op session's step and the level's update: it hosts the park only while
	/// that park is still the game's and no transition or park build waits.
	/// </summary>
	internal void StepCoopShare()
	{
		coopShare?.Step( parkBuild == null && pending == null ? Level : null );
		// a dismissed end's notice goes once its time is over or its park is no longer the game's (it holds that park)
		if ( endedNotice is { EndedAt: { } at } notice && (notice.Now - at >= SharingEndedSeconds || !ReferenceEquals( notice.Level, Level )) )
			endedNotice = null;
	}

	/// <summary>The shared park's status line for the HUD (<see cref="Hud.HudHost.SessionStatus"/>), for the game's attempt; see the static overload.</summary>
	internal string? SessionStatusLine( Level level ) => level.CoopSession is { Role: CoopRole.Guest } guest && FeatureGates.Online
		? GuestStatusLine( guest, Online.Coop, Strings.Extra ) : SessionStatusLine( StatusShare, level, Strings.Extra );

	/// <summary>
	/// The shared park's status line for <paramref name="level"/> (in the player's language, <paramref name="text"/>): while it is being shared, while it is hosted
	/// (its code and the connected players, or that the link reconnects), and <see cref="SharingEndedSeconds"/> after a session the player did not end (counted from
	/// when it ended, which is often the online pump's frame); null otherwise, and for another park than the attempt's.
	/// </summary>
	internal static string? SessionStatusLine( CoopShare? share, Level level, Func<OpenTpwText, string> text )
	{
		if ( share is null || !ReferenceEquals( share.Level, level ) )
			return null;
		switch ( share.Stage )
		{
			case CoopShareStage.Preparing:
			case CoopShareStage.Creating:
				return text( OpenTpwText.SharingThePark );
			case CoopShareStage.Hosting when share.State is { } state:
				if ( share.Link.State == CoopLinkState.Reconnecting )
					return string.Format( text( OpenTpwText.SharedParkReconnecting ), state.Code );
				return string.Format( text( OpenTpwText.SharedParkStatus ), state.Code, state.Members.Count( member => member.Connected ), state.MaximumPlayers );
			case CoopShareStage.Done when share is { Outcome: CoopShareOutcome.Ended, EndedBySelf: false, EndedAt: { } at } && share.Now - at < SharingEndedSeconds:
				return text( OpenTpwText.SharedParkEnded );
			default:
				return null;
		}
	}
}
