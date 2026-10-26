using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>[EXT:online-coop-session] Where an attempt to share the open park stands (<see cref="CoopShare"/>).</summary>
public enum CoopShareStage
{
	/// <summary>The park is prepared for hosting, a step a frame, while the link opens.</summary>
	Preparing,
	/// <summary>The create went out; the server's answer is awaited.</summary>
	Creating,
	/// <summary>The attempt was cancelled after its create went out: a session the server creates is left once it answers.</summary>
	Cancelling,
	/// <summary>The park is hosted: its session runs.</summary>
	Hosting,
	/// <summary>Over: cancelled, failed, or its session ended (<see cref="CoopShare.Outcome"/>).</summary>
	Done
}

/// <summary>[EXT:online-coop-session] How a share attempt ended (<see cref="CoopShare.Outcome"/>).</summary>
public enum CoopShareOutcome
{
	/// <summary>Not over yet.</summary>
	None,
	/// <summary>The player cancelled it.</summary>
	Cancelled,
	/// <summary>The park cannot be shared (<see cref="CoopShare.Refusal"/>), before the create or at its answer.</summary>
	Refused,
	/// <summary>The link stopped (<see cref="CoopShare.Stop"/>).</summary>
	LinkStopped,
	/// <summary>The server refused the create (<see cref="CoopShare.CreateRefusal"/>), or its answer was lost with the socket (<see cref="CoopReason.Unknown"/>).</summary>
	CreateRefused,
	/// <summary>The park was closed or replaced before it was hosted.</summary>
	ParkClosed,
	/// <summary>Hosting failed at the last moment (<see cref="CoopShare.FailureText"/>).</summary>
	HostFailed,
	/// <summary>The hosted session ended (<see cref="CoopShare.EndedBySelf"/>: the player ended it).</summary>
	Ended
}

/// <summary>What the player chose for a shared park: its title, whether it is on the open list, and how many players it takes (2 to 4).</summary>
public sealed record CoopShareRequest( string Title, bool Listed, int MaximumPlayers );

/// <summary>
/// [EXT:online-coop-session] What the host heard of an invitation (<see cref="CoopShare.LastInvite"/>, <see cref="CoopShare.LateRefusal"/>): the invited player (null for a
/// refusal that names no player this attempt invited) and the server's refusal (null: the invitation went out).
/// </summary>
public sealed record CoopInviteNotice( string? Player, CoopError? Refusal );

/// <summary>
/// [EXT:online-coop-session] One attempt of this game to share its open park (C2d-2; docs/COOP.md, "Sharing the park from the game"). The attempt owns its create from
/// the request to the answer:
/// <list type="number">
/// <item>it prepares the park for hosting (<see cref="CoopSession.PrepareHostSteps"/>, a step a frame) while the link opens;</item>
/// <item>it sends the create once both are done and the park can still be shared;</item>
/// <item>it hosts the park in the step after the server's answer (<see cref="Step"/>, after the online pump and before the level's update; never in a link callback),
/// once the same park is still the game's and can still be shared.</item>
/// </list>
/// A session the server creates after a cancel, a closed park or a refusal at the answer is left at once. The game steps one attempt at a time.
/// </summary>
internal sealed class CoopShare : IDisposable
{
	/// <summary>The reason the others hear when this game leaves a session it created but did not host.</summary>
	internal const string NotHosted = "The host stopped sharing the park.";
	/// <summary>The reason the others hear when the host ends its shared park.</summary>
	internal const string EndedByHost = "The host ended the shared park.";
	/// <summary>
	/// How long an invitation waits for the server's answer, in seconds of the attempt's clock. The server answers an invitation only with a refusal, at once, so the
	/// host invites one player at a time: one without a refusal by then went out, and the line under the invitation speaks of one player at a time.
	/// </summary>
	internal const double InviteAnswerSeconds = 3;
	/// <summary>
	/// How long a player invited once is not invited again (seconds of the attempt's clock): a refusal is matched to its invitation by the player it names, so two
	/// invitations of one player could not be told apart while the first one's refusal may still come.
	/// </summary>
	internal const double RepeatInviteSeconds = 30;

	private readonly CoopLink link;
	private readonly CoopShareRequest request;
	private readonly Func<double>? clock;
	private readonly Func<double> now;
	private readonly IEnumerator<bool> preparation;
	private bool prepared;
	private bool createSent;
	private CoopSessionState? answered;
	private CoopError? createRefused;
	private bool endingSelf;
	private (string Player, double At)? inviting;
	/// <summary>The players this attempt invited and when, newest last: a refusal is the invitation its text names (<see cref="CoopProtocol.InviteRefusalText"/>).</summary>
	private readonly List<(string Player, double At)> invited = new();
	private bool disposed;

	/// <summary>
	/// Starts sharing <paramref name="level"/> over <paramref name="link"/>: the link opens now. <paramref name="clock"/> is the session's clock and the attempt's own (the
	/// invitation's wait, the end's time), in seconds; a monotonic one when null.
	/// </summary>
	internal CoopShare( Level level, CoopLink link, CoopShareRequest request, Func<double>? clock = null )
	{
		ArgumentNullException.ThrowIfNull( level );
		ArgumentNullException.ThrowIfNull( link );
		ArgumentNullException.ThrowIfNull( request );
		Level = level;
		this.link = link;
		this.request = request;
		this.clock = clock;
		now = clock ?? CoopHostStream.DefaultSeconds;
		preparation = CoopSession.PrepareHostSteps( level ).GetEnumerator();
		link.SessionStarted += OnSessionStarted;
		link.Refused += OnRefused;
		Link.Open();
	}

	/// <summary>The park this attempt shares.</summary>
	public Level Level { get; }

	public CoopShareStage Stage { get; private set; } = CoopShareStage.Preparing;

	public CoopShareOutcome Outcome { get; private set; }

	/// <summary>Why the park could not be shared, for <see cref="CoopShareOutcome.Refused"/>.</summary>
	public CoopShareRefusal Refusal { get; private set; }

	/// <summary>Why the link stopped, for <see cref="CoopShareOutcome.LinkStopped"/>.</summary>
	public CoopStop Stop { get; private set; }

	/// <summary>Why the server refused the create, for <see cref="CoopShareOutcome.CreateRefused"/>.</summary>
	public CoopReason CreateRefusal { get; private set; }

	/// <summary>What went wrong, in English, for the log (and the screen's details); null while nothing did.</summary>
	public string? FailureText { get; private set; }

	/// <summary>The hosted session, once the park is hosted (it stays after its end).</summary>
	public CoopSession? Session { get; private set; }

	/// <summary>Whether the player ended the hosted session (rather than the server, a logout or a failure).</summary>
	public bool EndedBySelf { get; private set; }

	/// <summary>The attempt's clock now, in seconds (<see cref="EndedAt"/> is by it).</summary>
	public double Now => now();

	/// <summary>When the hosted session ended, by the attempt's clock (<see cref="Now"/>); null while it has not.</summary>
	public double? EndedAt { get; private set; }

	/// <summary>The invited player whose invitation waits for the server's answer (<see cref="InviteAnswerSeconds"/>); null when none waits.</summary>
	public string? Inviting => inviting?.Player;

	/// <summary>The latest invitation's outcome: it went out, or the server refused it; null before any.</summary>
	public CoopInviteNotice? LastInvite { get; private set; }

	/// <summary>
	/// A refusal of an older invitation that came after the latest one went out (a slow link), or one that names no player this attempt invited; null since the latest
	/// invitation when none came. The latest invitation's outcome stays its own.
	/// </summary>
	public CoopInviteNotice? LateRefusal { get; private set; }

	/// <summary>The server's refusal of the latest removal; null since it when none came (a removal is answered by its player leaving the list).</summary>
	public CoopError? KickRefusal { get; private set; }

	/// <summary>The session as the link holds it while it runs (its code, its members); null otherwise.</summary>
	public CoopSessionState? State => Stage == CoopShareStage.Hosting ? link.Session : null;

	/// <summary>The link this attempt shares the park over.</summary>
	public CoopLink Link => link;

	/// <summary>
	/// The attempt's step, once a frame right after the online pump and before the co-op session's and the level's update. <paramref name="live"/> is the game's park
	/// now (null while a transition or a park build waits): another park closes the attempt.
	/// </summary>
	public void Step( Level? live )
	{
		switch ( Stage )
		{
			case CoopShareStage.Preparing:
				StepPreparing( live );
				return;
			case CoopShareStage.Creating:
			case CoopShareStage.Cancelling:
				StepCreating( live );
				return;
			case CoopShareStage.Hosting:
				// an invitation without a refusal by now went out
				if ( inviting is { } waiting && now() - waiting.At >= InviteAnswerSeconds )
					inviting = null;
				return;
		}
	}

	private void StepPreparing( Level? live )
	{
		if ( !ReferenceEquals( live, Level ) )
		{
			Finish( CoopShareOutcome.ParkClosed, "The park was closed before it was shared." );
			return;
		}
		// an open on its way does nothing; a closed link opens again (an older server stops it at once, without a state change), also one the lobby opened that stopped
		// at a park of the account it did not ask for: the share is the player's own act, whose open leaves that park (C2d-3b)
		if ( link.State == CoopLinkState.Closed && link.Stopped is CoopStop.None or CoopStop.HeldElsewhere )
			Link.Open();
		if ( link.Stopped != CoopStop.None )
		{
			Stop = link.Stopped;
			Finish( CoopShareOutcome.LinkStopped, $"The link to shared parks stopped: {link.Stopped}." );
			return;
		}
		if ( !prepared )
		{
			// one step a frame; the last one is the frame that finds nothing left
			if ( preparation.MoveNext() )
				return;
			prepared = true;
		}
		if ( link.State != CoopLinkState.Open )
			return;
		if ( CoopSession.ShareRefusal( Level ) is var refusal and not CoopShareRefusal.None )
		{
			Refusal = refusal;
			Finish( CoopShareOutcome.Refused, $"The park cannot be shared: {refusal}." );
			return;
		}
		if ( link.Session != null || !link.Create( request.Listed, request.Title, request.MaximumPlayers ) )
		{
			CreateRefusal = link.Session != null ? CoopReason.InSession : CoopReason.None;
			Finish( CoopShareOutcome.CreateRefused, link.Session != null ? "This game is in a shared park already." : "The create could not be sent." );
			return;
		}
		createSent = true;
		Stage = CoopShareStage.Creating;
	}

	private void StepCreating( Level? live )
	{
		// a closed park cancels: the create's answer is still awaited, and a session it made is left
		if ( Stage == CoopShareStage.Creating && !ReferenceEquals( live, Level ) )
			Stage = CoopShareStage.Cancelling;
		if ( createRefused is { } error )
		{
			createRefused = null;
			if ( Stage == CoopShareStage.Cancelling )
			{
				Finish( Outcome == CoopShareOutcome.None && ReferenceEquals( live, Level ) ? CoopShareOutcome.Cancelled : CoopShareOutcome.ParkClosed, "Cancelled." );
				return;
			}
			CreateRefusal = error.Reason;
			Finish( CoopShareOutcome.CreateRefused, $"The server refused the shared park ({error.Reason}): {error.Text}" );
			return;
		}
		if ( answered is not { } state )
			return;
		answered = null;
		if ( Stage == CoopShareStage.Cancelling )
		{
			link.Leave( NotHosted );
			Finish( ReferenceEquals( live, Level ) ? CoopShareOutcome.Cancelled : CoopShareOutcome.ParkClosed, "Cancelled before the park was hosted." );
			return;
		}
		if ( CoopSession.ShareRefusal( Level ) is var refusal and not CoopShareRefusal.None )
		{
			link.Leave( NotHosted );
			Refusal = refusal;
			Finish( CoopShareOutcome.Refused, $"The park cannot be shared: {refusal}." );
			return;
		}
		try
		{
			Session = CoopSession.Host( Level, link, clock );
		}
		catch ( InvalidOperationException failure )
		{
			link.Leave( NotHosted );
			Finish( CoopShareOutcome.HostFailed, failure.Message );
			return;
		}
		Log.Info( $"Sharing the park as {state.Code} ({request.MaximumPlayers} players, {(request.Listed ? "listed" : "unlisted")})." );
		Stage = CoopShareStage.Hosting;
		Session.Ended += OnEnded;
	}

	/// <summary>
	/// Stops the attempt before its park is hosted: at once while it prepares; once the create went out, when the server answers (a session it made is left then).
	/// A hosted park is ended with <see cref="End"/>.
	/// </summary>
	public void Cancel()
	{
		if ( Stage == CoopShareStage.Preparing )
			Finish( CoopShareOutcome.Cancelled, "Cancelled." );
		else if ( Stage == CoopShareStage.Creating )
			Stage = CoopShareStage.Cancelling;
	}

	/// <summary>Ends the hosted session for everyone; the park stays this game's.</summary>
	public void End()
	{
		if ( Stage != CoopShareStage.Hosting || Session is not { } session )
			return;
		endingSelf = true;
		try { session.End( EndedByHost ); }
		finally { endingSelf = false; }
	}

	/// <summary>[EXT:online-coop-permissions] Whether the guest in seat <paramref name="slot"/> may build, as the shared park's applied grant says (C3a-2); false while not hosting.</summary>
	public bool Builds( int slot ) => Stage == CoopShareStage.Hosting && Session?.Level is { } park && park.HasBuildGrant( slot );

	/// <summary>
	/// [EXT:online-coop-permissions] The host lets the guest in seat <paramref name="slot"/> build, or takes it back (C3a-2): a command of the shared park, which every peer applies at its
	/// record, so a request of the guest's is decided by where it stands in the stream. False when it was not applied (not hosting, the host's own seat).
	/// </summary>
	public bool SetBuilds( int slot, bool builds ) => Stage == CoopShareStage.Hosting && slot > 0 && Session?.Level is { } park
		&& park.Commands.Issue( new SetBuildGrantCommand( slot, builds ) ).Accepted;

	/// <summary>The host removes member <paramref name="memberId"/>; it cannot join this session again.</summary>
	public bool Kick( uint memberId )
	{
		if ( Stage != CoopShareStage.Hosting || !link.Kick( memberId ) )
			return false;
		KickRefusal = null;
		return true;
	}

	/// <summary>
	/// The host invites <paramref name="player"/>: false while another invitation waits for its answer (<see cref="Inviting"/>), for a player invited in the last
	/// <see cref="RepeatInviteSeconds"/>, and when it cannot be sent. The server answers only a refusal (<see cref="LastInvite"/>).
	/// </summary>
	public bool Invite( string player )
	{
		if ( Stage != CoopShareStage.Hosting || inviting != null || InvitedLately( player ) || !link.Invite( player ) )
			return false;
		inviting = (player, now());
		// every invitation of the repeat window stays (the server's invitation quota bounds them); an older one's refusal, should it ever come, names no one
		invited.RemoveAll( entry => inviting.Value.At - entry.At >= RepeatInviteSeconds );
		invited.Add( (player, inviting.Value.At) );
		LastInvite = new CoopInviteNotice( player, null );
		LateRefusal = null;
		return true;
	}

	/// <summary>Whether <paramref name="player"/>, exactly as typed (the server's refusal echoes the name so), was invited in the last <see cref="RepeatInviteSeconds"/>.</summary>
	public bool InvitedLately( string player ) => invited.Any( entry => entry.Player == player && now() - entry.At < RepeatInviteSeconds );

	private void OnSessionStarted( CoopSessionState state )
	{
		// the answer to this attempt's create (only it creates while it waits); the step hosts it
		if ( createSent && answered == null && (Stage == CoopShareStage.Creating || Stage == CoopShareStage.Cancelling) && state.You == state.Host )
			answered = state;
	}

	private void OnRefused( CoopError error )
	{
		if ( error.Request == CoopMessageType.Create && createSent && (Stage == CoopShareStage.Creating || Stage == CoopShareStage.Cancelling) )
			createRefused = error;
		else if ( Stage == CoopShareStage.Hosting && error.Request == CoopMessageType.Invite )
		{
			// every refusal names its player, so it is the invitation its text names, whenever it comes: the latest invitation's is its outcome (and ends its wait); an
			// older one's, or one that names no player this attempt invited, is a late result of its own and leaves the latest invitation be
			var named = invited.LastOrDefault( entry => error.Text == CoopProtocol.InviteRefusalText( error.Reason, entry.Player ) ).Player;
			if ( named != null && named == LastInvite?.Player )
			{
				LastInvite = new CoopInviteNotice( named, error );
				if ( inviting?.Player == named )
					inviting = null;
			}
			else
				LateRefusal = new CoopInviteNotice( named, error );
		}
		else if ( Stage == CoopShareStage.Hosting && error.Request == CoopMessageType.Kick )
			KickRefusal = error;
	}

	private void OnEnded( string reason )
	{
		EndedBySelf = endingSelf;
		// the end comes when it comes (often in the online pump, before the flow's step): its time is what the status line counts from
		EndedAt = now();
		Finish( CoopShareOutcome.Ended, reason );
	}

	private void Finish( CoopShareOutcome outcome, string text )
	{
		if ( Stage == CoopShareStage.Done )
			return;
		Stage = CoopShareStage.Done;
		Outcome = outcome;
		FailureText = outcome is CoopShareOutcome.Cancelled or CoopShareOutcome.Ended ? null : text;
		if ( FailureText != null )
			Log.Warning( $"Sharing the park failed: {FailureText}" );
		Unsubscribe();
	}

	private void Unsubscribe()
	{
		link.SessionStarted -= OnSessionStarted;
		link.Refused -= OnRefused;
		if ( Session != null )
			Session.Ended -= OnEnded;
	}

	/// <summary>
	/// Forgets the attempt (a new one replaces it, or the game ends): one still waiting for its create's answer is left behind, and the link leaves a session the
	/// server names later (its next welcome). A hosted session is the level's; it ends with its park.
	/// </summary>
	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		Unsubscribe();
		preparation.Dispose();
	}
}
