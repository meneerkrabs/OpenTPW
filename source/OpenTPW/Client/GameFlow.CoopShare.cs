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
		// [EXT:save-extension-switch] the save's extensions are asked about first, as for its load; the share goes with the queued load, so another transition drops it
		ConfirmExtensions( path, change => QueueProgressLevel( park.Level, () => LoadSharedPark( park with { Switch = change }, request ) ) );
		return true;
	}

	/// <summary>The load of a saved park to share (C2d-2b): the load, then the share of the park it made.</summary>
	private void LoadSharedPark( ParkLoadEntry park, CoopShareRequest request )
	{
		var before = Level;
		LoadPark( park, overFrames: true );
		ShareLoadedPark( before, request );
	}

	/// <summary>
	/// Shares the park a load made (C2d-2b): only a park that replaced <paramref name="before"/> (a failed load shares nothing and leaves the lobby as it was). The park's
	/// Build Together screen then shows the attempt, or why it could not start, with the player's choices kept for another try.
	/// </summary>
	internal void ShareLoadedPark( Level? before, CoopShareRequest request )
	{
		if ( Level is not { } loaded || ReferenceEquals( loaded, before ) )
			return;
		// an extension switch may have turned the online extension off: the start checks it again, with the login and the loaded park
		StartSharing( request );
		if ( FeatureGates.Online && Hud is { } hud )
			CreateOnlineScreens( hud.Stack ).ShowBuildTogether( request );
	}

	/// <summary>
	/// The share attempt's step, once a frame right after the online pump and before the co-op session's step and the level's update: it hosts the park only while
	/// that park is still the game's and no transition or park build waits.
	/// </summary>
	internal void StepCoopShare() => coopShare?.Step( parkBuild == null && pending == null ? Level : null );

	/// <summary>The shared park's status line for the HUD (<see cref="Hud.HudHost.SessionStatus"/>), for the game's attempt; see the static overload.</summary>
	internal string? SessionStatusLine( Level level ) => SessionStatusLine( coopShare, level, Strings.Extra );

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
			case CoopShareStage.Done when share is { Outcome: CoopShareOutcome.Ended, EndedBySelf: false, EndedAt: { } at } && Time.Now - at < SharingEndedSeconds:
				return text( OpenTpwText.SharedParkEnded );
			default:
				return null;
		}
	}
}
