using OpenTPW.Online.Coop;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-session] The lobby's side of shared parks (C2d-3b; docs/COOP.md, "Invitations and the open list"): the lobby-wide link (<see cref="CoopLobbyLink"/>) and the
/// invitations it brings, one dialog at a time over whatever lobby screen is open. An invitation is the login's that got it: leaving the lobby, a logout or another login forgets
/// every invitation, also the one on screen. The protocol has no answer to an invitation: Not now only puts it aside, and Join is a join with its code.
/// </summary>
internal sealed partial class GameFlow
{
	/// <summary>How long an invitation waits to be shown (while a join runs, or another invitation is on screen), in seconds.</summary>
	internal const double InvitationKeepSeconds = 60;

	/// <summary>The invitations that wait at most, one per shared park: a new one puts the oldest aside.</summary>
	internal const int MaximumWaitingInvitations = 4;

	/// <summary>The invitation dialog's name.</summary>
	internal const string InvitationScreenName = "coopInvitation";

	private CoopLobbyLink? coopLobbyLink;
	/// <summary>The invitations not shown yet, oldest first, with the time they came (made on first use: a flow the tests make has no field initializers run).</summary>
	private List<(CoopInvited Invitation, double At)>? waitingInvitations;
	private List<(CoopInvited Invitation, double At)> Waiting => waitingInvitations ??= new();
	/// <summary>The invitation on screen, until the player answers it (a rebuild of the lobby shows it again).</summary>
	private CoopInvited? shownInvitation;
	private UiScreen? invitationScreen;
	/// <summary>The login the invitations belong to.</summary>
	private (Uri Server, string Account)? invitationsLogin;

	/// <summary>The invitation on screen (tests).</summary>
	internal CoopInvited? ShownInvitation => shownInvitation;

	/// <summary>The invitations that wait to be shown, oldest first (tests).</summary>
	internal IReadOnlyList<CoopInvited> WaitingInvitations => Waiting.Select( entry => entry.Invitation ).ToArray();

	/// <summary>The lobby's link (tests).</summary>
	internal CoopLobbyLink? CoopLobbyLink => coopLobbyLink;

	private double LobbySeconds() => (CoopSessionClock ?? CoopHostStream.DefaultSeconds)();

	/// <summary>The lobby proper: no park, no park on its way (a queued transition, a build, a joined park's build), no Theme Park Inc front end.</summary>
	private bool InCoopLobby => Level == null && IncFrontEnd == null && parkBuild == null && pending == null && coopJoin == null;

	/// <summary>The link is someone else's: a join attempt or a share runs, or a saved park's share waits for its park.</summary>
	private bool CoopLinkInUse => coopJoinAttempt is { Stage: not CoopJoinStage.Done } || coopShare is { Stage: not CoopShareStage.Done } || savedParkShare != null;

	/// <summary>
	/// The lobby's step, once a frame after the join's: the lobby-wide link (open for a logged-in player in the lobby who could follow a shared park, closed elsewhere while
	/// idle), then the invitations: forgotten once the lobby is left or the login changes, else the next one shown while the game is free to join.
	/// </summary>
	internal void StepCoopLobby()
	{
		if ( !FeatureGates.Online || onlineSession is not { } online )
			return;
		if ( coopLobbyLink == null )
		{
			coopLobbyLink = new CoopLobbyLink( online.Coop, LobbySeconds );
			online.Coop.Invited += OnCoopInvited;
		}
		var login = online.LoginIdentity;
		var wanted = login != null && InCoopLobby && CoopSession.JoinRefusalKind == CoopJoinRefusal.None;
		coopLobbyLink.Step( wanted, login, CoopLinkInUse );
		if ( !wanted || login != invitationsLogin )
			ForgetInvitations();
		invitationsLogin = wanted ? login : null;
		if ( wanted )
			ShowInvitation();
	}

	/// <summary>An invitation from the link: kept only while the lobby wants the link (it came for this login); a repeat of one already kept or shown takes its place.</summary>
	private void OnCoopInvited( CoopInvited invited )
	{
		if ( coopLobbyLink is not { Wanted: true } )
			return;
		if ( shownInvitation is { } shown && shown.SessionId == invited.SessionId )
		{
			shownInvitation = invited;
			return;
		}
		Waiting.RemoveAll( entry => entry.Invitation.SessionId == invited.SessionId );
		Waiting.Add( (invited, LobbySeconds()) );
		while ( Waiting.Count > MaximumWaitingInvitations )
			Waiting.RemoveAt( 0 );
	}

	/// <summary>Forgets every invitation and closes the one on screen.</summary>
	private void ForgetInvitations()
	{
		waitingInvitations?.Clear();
		shownInvitation = null;
		RemoveInvitationScreen();
	}

	private void RemoveInvitationScreen()
	{
		if ( invitationScreen is { } screen )
			Menu?.Stack.RemoveWhere( candidate => ReferenceEquals( candidate, screen ) );
		invitationScreen = null;
	}

	/// <summary>
	/// Shows the next invitation, or the one on screen again after a rebuild of the lobby dropped its dialog, while the game is free to join: nothing is under way that the
	/// link or a park is busy with (a join, a share, a park on its way, the lobby's enter sequence). Waiting invitations older than <see cref="InvitationKeepSeconds"/> are dropped.
	/// </summary>
	private void ShowInvitation()
	{
		var now = LobbySeconds();
		Waiting.RemoveAll( entry => now - entry.At > InvitationKeepSeconds );
		if ( Menu is not { } menu || LobbyEntering || CoopLinkInUse )
			return;
		if ( shownInvitation == null )
		{
			if ( Waiting.Count == 0 )
				return;
			shownInvitation = Waiting[0].Invitation;
			Waiting.RemoveAt( 0 );
		}
		else if ( invitationScreen != null && menu.Stack.Screens.Contains( invitationScreen ) )
			return;
		invitationScreen = CreateOnlineScreens( menu.Stack ).ShowInvitation( () => shownInvitation, AcceptInvitation, PutInvitationAside );
	}

	/// <summary>
	/// Join: a join with the invitation's code, if the invitation is still this login's and the game may join (<see cref="StartJoining"/>); the join screen shows it, as a join with a
	/// typed code, and keeps the code for another try.
	/// </summary>
	private void AcceptInvitation()
	{
		var invitation = shownInvitation;
		shownInvitation = null;
		RemoveInvitationScreen();
		if ( invitation == null || coopLobbyLink is not { Wanted: true } || onlineSession?.LoginIdentity is not { } login || login != invitationsLogin || Menu is not { } menu )
			return;
		if ( !StartJoining( invitation.Code ) )
			return;
		// the join screen shows it with the invitation's code (kept for another try after a refusal): an idle one on top makes way
		if ( menu.Stack.Top?.Name == UI.OnlineScreens.JoinSharedParkScreenName )
			menu.Stack.Pop();
		CreateOnlineScreens( menu.Stack ).ShowJoinSharedPark( invitation.Code );
	}

	/// <summary>Not now: the invitation is put aside (the server hears nothing).</summary>
	private void PutInvitationAside()
	{
		shownInvitation = null;
		RemoveInvitationScreen();
	}
}
