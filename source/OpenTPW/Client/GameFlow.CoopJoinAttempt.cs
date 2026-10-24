using OpenTPW.Online.Coop;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>[EXT:online-coop-session] What the online screens ask of the game to join a shared park with a code (C2d-3; <see cref="UI.OnlineScreens.ShowJoinSharedPark"/>).</summary>
internal interface ICoopJoining
{
	/// <summary>The attempt in progress, or the last one with its outcome; null when there is none.</summary>
	CoopJoinAttempt? Joining { get; }

	/// <summary>Starts joining with <paramref name="code"/>; false, with nothing started, when it may not (a park is open, a transition, a share, logged out, another attempt).</summary>
	bool StartJoining( string code );

	/// <summary>Stops the attempt in progress (once joined, its session ends and is left).</summary>
	void CancelJoining();

	/// <summary>Forgets a finished attempt (the screen goes back to the start).</summary>
	void DismissJoining();
}

/// <summary>
/// [EXT:online-coop-session] The game's joining of a shared park from the lobby (C2d-3; docs/COOP.md, "Joining a shared park from the lobby"): the join screen starts an attempt
/// (<see cref="CoopJoinAttempt"/>), which the flow steps once a frame after the online pump and the share's step, and takes the server's answer there.
/// </summary>
internal sealed partial class GameFlow : ICoopJoining
{
	/// <summary>The attempt to join a shared park, until a new one replaces it or the player dismisses its outcome.</summary>
	private CoopJoinAttempt? coopJoinAttempt;

	/// <inheritdoc/>
	public CoopJoinAttempt? Joining => coopJoinAttempt;

	/// <inheritdoc/>
	public bool StartJoining( string code )
	{
		ArgumentNullException.ThrowIfNull( code );
		if ( !FeatureGates.Online || Level != null || coopJoin != null || parkBuild != null || pending != null || savedParkShare != null )
			return false;
		if ( coopJoinAttempt is { Stage: not CoopJoinStage.Done } || coopShare is { Stage: not CoopShareStage.Done } || !Online.IsLoggedIn )
			return false;
		coopJoinAttempt?.Dispose();
		coopJoinAttempt = new CoopJoinAttempt( Online.Coop, code );
		return true;
	}

	/// <inheritdoc/>
	public void CancelJoining() => coopJoinAttempt?.Cancel( session => EndJoin( session, LeftSharedPark ) );

	/// <inheritdoc/>
	public void DismissJoining()
	{
		if ( coopJoinAttempt is not { Stage: CoopJoinStage.Done } done )
			return;
		done.Dispose();
		coopJoinAttempt = null;
	}

	/// <summary>
	/// The join attempt's step, once a frame right after the online pump and the share's step and before the co-op session's: the server's answer is taken here, never in the link's
	/// callback, and only while the link still holds that seat and the game is free to join (no park, no park build, no transition, no saved park's share, no share attempt);
	/// otherwise the seat is left.
	/// </summary>
	internal void StepCoopJoin()
	{
		if ( coopJoinAttempt is not { } attempt )
			return;
		attempt.Step();
		if ( attempt.Stage != CoopJoinStage.Answered )
			return;
		if ( !attempt.HoldsSeat || Level != null || coopJoin != null || parkBuild != null || pending != null || savedParkShare != null || coopShare is { Stage: not CoopShareStage.Done } )
		{
			attempt.Reject();
			return;
		}
		CoopSession session;
		try
		{
			session = JoinSharedPark( attempt.Link );
		}
		catch ( InvalidOperationException failure )
		{
			// the game cannot follow it after all: the seat is left, never held until a teardown
			attempt.Reject( failure.Message );
			return;
		}
		attempt.Accept( session );
	}

	/// <summary>
	/// The words for a shared park's end on a guest's game (C2d-3), by what ended it and the server's reason, never the server's English; null when this game left it (nothing to
	/// say).
	/// </summary>
	internal static OpenTpwText? JoinEndText( CoopSessionEnd cause, CoopReason serverReason ) => cause switch
	{
		CoopSessionEnd.Server => serverReason switch
		{
			CoopReason.Kicked => OpenTpwText.JoinKicked,
			CoopReason.Expired => OpenTpwText.JoinHostGone,
			CoopReason.BuildDiffers => OpenTpwText.JoinBuildDiffers,
			CoopReason.ModsDiffer => OpenTpwText.JoinModsDiffer,
			_ => OpenTpwText.JoinHostLeft,
		},
		CoopSessionEnd.LinkLost => OpenTpwText.SharedParkLinkLost,
		CoopSessionEnd.JoinFailed => OpenTpwText.JoinFailed,
		CoopSessionEnd.StreamFailed => OpenTpwText.SharedParkFailed,
		_ => null,
	};

	/// <summary>The words for the server's refusal of a join (C2d-3), by its reason.</summary>
	internal static OpenTpwText JoinRefusalText( CoopReason reason ) => reason switch
	{
		CoopReason.UnknownCode => OpenTpwText.JoinUnknownCode,
		CoopReason.Full => OpenTpwText.JoinFull,
		CoopReason.Kicked => OpenTpwText.JoinKicked,
		CoopReason.BuildDiffers => OpenTpwText.JoinBuildDiffers,
		CoopReason.ModsDiffer => OpenTpwText.JoinModsDiffer,
		CoopReason.Rate => OpenTpwText.SharedParkTryLater,
		CoopReason.InSession => OpenTpwText.JoinInSession,
		CoopReason.Unknown => OpenTpwText.SharedParkLinkLost,
		_ => OpenTpwText.JoinRefused,
	};

	/// <summary>
	/// The guest's status line for its shared park (C2d-3; the HUD's line, as the host's): reconnecting, then waiting for the host (away), then catching up (a resync), else the park's
	/// title, its host and the connected players; null once the session ended.
	/// </summary>
	internal static string? GuestStatusLine( CoopSession session, CoopLink link, Func<OpenTpwText, string> text )
	{
		if ( session.EndedWith != null || link.Session is not { } state )
			return null;
		if ( link.State == CoopLinkState.Reconnecting )
			return text( OpenTpwText.GuestReconnecting );
		var host = state.Members.FirstOrDefault( member => member.Id == state.Host );
		if ( host is { Connected: false } )
			return text( OpenTpwText.GuestHostAway );
		if ( session.Phase == CoopGuestPhase.Resyncing )
			return text( OpenTpwText.GuestCatchingUp );
		return string.Format( text( OpenTpwText.GuestStatus ), state.Title, host?.Name ?? "", state.Members.Count( member => member.Connected ), state.MaximumPlayers );
	}
}
