namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-session] The co-op link's lifetime outside a shared park (C2d-3b; docs/COOP.md, "Invitations and the open list"). While a logged-in player who could follow a
/// shared park is in the lobby, the link is open, so a host's invitation reaches the player on every lobby screen; elsewhere an idle link is closed. The game steps it once a
/// frame (<see cref="GameFlow"/>, after the join's step):
/// <list type="bullet">
/// <item>A link in use is never touched: one that holds a session (a host's or a guest's, also while it reconnects), one whose create or join waits for its answer
/// (<see cref="CoopLink.Waiting"/>: the server would seat the player once it reads it), and one a join attempt or a share is using.</item>
/// <item>Each time the link becomes wanted (the lobby entered, a login there, another login) it opens once (<see cref="CoopLink.OpenInLobby"/>), without a take-over and without
/// leaving a shared park of the account it did not ask for: another game of the account that holds the link keeps it (<see cref="CoopStop.InUseElsewhere"/>), and so does a
/// park of another game whose socket is away for a moment (<see cref="CoopStop.HeldElsewhere"/>). A stop is kept until the next time, or until the player acts (a join or a
/// share opens the link itself); only a link lost for its whole reconnect window opens again by itself, after <see cref="LostRetrySeconds"/> (spread), at most
/// <see cref="MaximumLostRetries"/> times a stay (a link open for <see cref="StableOpenSeconds"/> counts from nought again).</item>
/// <item>While wanted, a link closed by something else (with no stop) opens again.</item>
/// <item>Not wanted, an idle link closes, without a leave: it holds no session. Something to settle at its next welcome (a leave of this game's, a seat a create or join whose
/// answer was lost may have made: <see cref="CoopLink.Settling"/>) keeps it open for up to <see cref="SettleHoldSeconds"/>, so its own reconnect can settle it.</item>
/// </list>
/// </summary>
internal sealed class CoopLobbyLink
{
	/// <summary>How long a lost link waits in the lobby before it opens again, in seconds (spread by <see cref="LostRetrySpread"/>).</summary>
	internal const double LostRetrySeconds = 60;

	/// <summary>The spread of <see cref="LostRetrySeconds"/>: 70 to 130 per cent of it, so the lobbies a server restart lost do not all come back at once.</summary>
	internal const double LostRetrySpread = 0.3;

	/// <summary>How many times a lost link opens again by itself during one stay in the lobby (each open tries for the link's whole reconnect window).</summary>
	internal const int MaximumLostRetries = 10;

	/// <summary>How long the link stays open before its lost opens count from nought again, in seconds: one that drops soon after it opened goes on with the stay's count.</summary>
	internal const double StableOpenSeconds = 60;

	/// <summary>
	/// How long an idle link with something to settle (<see cref="CoopLink.Settling"/>) stays open once the lobby no longer wants it, in seconds: its next welcome settles it;
	/// a link that cannot reconnect meanwhile is closed (a leave of this game's waits for a later open, a lost answer's seat stays on the server).
	/// </summary>
	internal const double SettleHoldSeconds = 60;

	private readonly CoopLink link;
	private readonly Func<double> seconds;
	private readonly Func<double> random;
	private bool wanted;
	private (Uri Server, string Account)? identity;
	private double? lostRetryAt;
	private int lostRetries;
	/// <summary>When the link was first seen open since it last was not.</summary>
	private double? openSince;
	/// <summary>When the lobby stopped wanting the link.</summary>
	private double? unwantedSince;

	/// <param name="link">The online session's link.</param>
	/// <param name="seconds">The clock the lost link's wait runs on.</param>
	/// <param name="random">A number from 0 to 1 that spreads that wait (tests give their own).</param>
	internal CoopLobbyLink( CoopLink link, Func<double> seconds, Func<double>? random = null )
	{
		ArgumentNullException.ThrowIfNull( link );
		ArgumentNullException.ThrowIfNull( seconds );
		this.link = link;
		this.seconds = seconds;
		this.random = random ?? Random.Shared.NextDouble;
	}

	/// <summary>The online session's link this keeps.</summary>
	internal CoopLink Link => link;

	/// <summary>Whether the link was wanted at the last step: what an invitation that arrives meanwhile is kept by.</summary>
	internal bool Wanted => wanted;

	/// <summary>The times a lost link opened again by itself in this stay in the lobby.</summary>
	internal int LostRetries => lostRetries;

	/// <summary>
	/// The step: <paramref name="wanted"/> says whether the game wants the link open (a logged-in player who could follow a shared park, in the lobby), <paramref name="login"/>
	/// who is logged in where (null while logged out), and <paramref name="inUse"/> whether a join attempt, a share or a saved park's share is using the link.
	/// </summary>
	internal void Step( bool wanted, (Uri Server, string Account)? login, bool inUse )
	{
		var rising = wanted && (!this.wanted || login != identity);
		this.wanted = wanted;
		identity = wanted ? login : null;
		if ( rising || !wanted )
		{
			lostRetries = 0;
			lostRetryAt = null;
			openSince = null;
		}
		var now = seconds();
		if ( wanted )
			unwantedSince = null;
		else
			unwantedSince ??= now;
		if ( inUse || link.Session != null || link.Waiting )
			return;
		if ( !wanted )
		{
			// something to settle at the next welcome (a leave of this game's, a seat a lost answer may have made) keeps the link for a while
			if ( link.State != CoopLinkState.Closed && !(link.Settling && now - unwantedSince < SettleHoldSeconds) )
				Link.Close();
			return;
		}
		if ( link.State == CoopLinkState.Open )
		{
			// a link that stayed up starts over; one that drops soon after it opened goes on with the stay's count
			openSince ??= now;
			if ( now - openSince >= StableOpenSeconds )
			{
				lostRetries = 0;
				lostRetryAt = null;
			}
			return;
		}
		openSince = null;
		if ( link.State != CoopLinkState.Closed )
			return;
		if ( rising || link.Stopped == CoopStop.None )
		{
			link.OpenInLobby();
			return;
		}
		if ( link.Stopped != CoopStop.Lost || lostRetries >= MaximumLostRetries )
			return;
		if ( lostRetryAt == null )
		{
			lostRetryAt = now + LostRetrySeconds * (1 - LostRetrySpread + 2 * LostRetrySpread * Math.Clamp( random(), 0, 1 ));
			return;
		}
		if ( now < lostRetryAt )
			return;
		lostRetries++;
		lostRetryAt = null;
		link.OpenInLobby();
	}
}
