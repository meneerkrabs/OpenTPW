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

	private readonly CoopLink link;
	private readonly CoopShareRequest request;
	private readonly Func<double>? clock;
	private readonly IEnumerator<bool> preparation;
	private bool prepared;
	private bool createSent;
	private CoopSessionState? answered;
	private CoopError? createRefused;
	private bool endingSelf;
	private bool disposed;

	/// <summary>Starts sharing <paramref name="level"/> over <paramref name="link"/> (its session clock <paramref name="clock"/>, the session's own when null): the link opens now.</summary>
	internal CoopShare( Level level, CoopLink link, CoopShareRequest request, Func<double>? clock = null )
	{
		ArgumentNullException.ThrowIfNull( level );
		ArgumentNullException.ThrowIfNull( link );
		ArgumentNullException.ThrowIfNull( request );
		Level = level;
		this.link = link;
		this.request = request;
		this.clock = clock;
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

	/// <summary>The last invitation or removal the server refused (an invitation is not answered otherwise); null when none.</summary>
	public CoopError? LastRefusal { get; private set; }

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
		}
	}

	private void StepPreparing( Level? live )
	{
		if ( !ReferenceEquals( live, Level ) )
		{
			Finish( CoopShareOutcome.ParkClosed, "The park was closed before it was shared." );
			return;
		}
		// an open on its way does nothing; a closed link opens again (an older server stops it at once, without a state change)
		if ( link.State == CoopLinkState.Closed && link.Stopped == CoopStop.None )
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

	/// <summary>The host removes member <paramref name="memberId"/>; it cannot join this session again.</summary>
	public bool Kick( uint memberId ) => Stage == CoopShareStage.Hosting && link.Kick( memberId );

	/// <summary>The host invites <paramref name="player"/>; the server answers only a refusal (<see cref="LastRefusal"/>).</summary>
	public bool Invite( string player )
	{
		if ( Stage != CoopShareStage.Hosting )
			return false;
		LastRefusal = null;
		return link.Invite( player );
	}

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
		else if ( Stage == CoopShareStage.Hosting && error.Request is CoopMessageType.Invite or CoopMessageType.Kick )
			LastRefusal = error;
	}

	private void OnEnded( string reason )
	{
		EndedBySelf = endingSelf;
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
