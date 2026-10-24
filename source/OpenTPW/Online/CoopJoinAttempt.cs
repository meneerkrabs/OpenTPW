using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>[EXT:online-coop-session] Where an attempt to join a shared park stands (<see cref="CoopJoinAttempt"/>).</summary>
public enum CoopJoinStage
{
	/// <summary>The link opens.</summary>
	Opening,
	/// <summary>The join went out; the server's answer is awaited.</summary>
	Joining,
	/// <summary>The attempt was cancelled after its join went out: a seat the server gives is left once it answers.</summary>
	Cancelling,
	/// <summary>The server gave this game a seat; the game takes the join in its next step (<see cref="CoopJoinAttempt.Accept"/>) or leaves the seat.</summary>
	Answered,
	/// <summary>The game joined: the session gets the host's park and builds it (<see cref="CoopJoinAttempt.Session"/>).</summary>
	Building,
	/// <summary>Over: joined, cancelled, refused, or its session ended (<see cref="CoopJoinAttempt.Outcome"/>).</summary>
	Done
}

/// <summary>[EXT:online-coop-session] How a join attempt ended (<see cref="CoopJoinAttempt.Outcome"/>).</summary>
public enum CoopJoinOutcome
{
	/// <summary>Not over yet.</summary>
	None,
	/// <summary>The host's park is this game's park now.</summary>
	Joined,
	/// <summary>The player cancelled it.</summary>
	Cancelled,
	/// <summary>The server refused the join (<see cref="CoopJoinAttempt.Refusal"/>), or its answer was lost with the socket (<see cref="CoopReason.Unknown"/>).</summary>
	Refused,
	/// <summary>The link stopped (<see cref="CoopJoinAttempt.Stop"/>).</summary>
	LinkStopped,
	/// <summary>The typed code cannot be a shared park's.</summary>
	NotACode,
	/// <summary>This game cannot join a shared park (<see cref="CoopSession.JoinRefusal"/>: another edition, or a run with a developer option that changes the park).</summary>
	NotAllowed,
	/// <summary>The link is in a shared park already.</summary>
	InSession,
	/// <summary>The game could not take the seat the server gave (a park or a transition came first): the seat was left.</summary>
	NotTaken,
	/// <summary>The session ended before its park was this game's (<see cref="CoopJoinAttempt.EndCause"/>; once the game took it, its end dialog says why).</summary>
	Ended
}

/// <summary>
/// [EXT:online-coop-session] One attempt of this game to join a shared park with a code (C2d-3; docs/COOP.md, "Joining a shared park from the lobby"). The attempt owns its join
/// from the request to the answer, and the game owns the attempt (<see cref="GameFlow"/>'s step after the online pump):
/// <list type="number">
/// <item>it sends the join once the link is open;</item>
/// <item>the server's answer is taken in the game's step, never in the link's callback: the game joins (<see cref="Accept"/>, its session then gets and builds the host's
/// park) or leaves the seat when it cannot take it (<see cref="Reject"/>);</item>
/// <item>it follows the session until its park is the game's, or until it ends.</item>
/// </list>
/// A seat the server gives after a cancel is left at once. A cancel once joined ends the session (its leave goes out, a partial build is abandoned).
/// </summary>
internal sealed class CoopJoinAttempt : IDisposable
{
	/// <summary>The reason the host hears when this game leaves a seat it did not take.</summary>
	internal const string NotJoined = "The player stopped joining the shared park.";

	private readonly CoopLink link;
	private bool joinSent;
	private CoopSessionState? answered;
	private CoopError? refused;
	private CoopEnded? endedEarly;
	/// <summary>Set by a cancel once joined: the session's own end is the cancel's.</summary>
	private bool cancelling;
	private bool disposed;

	/// <summary>
	/// Starts joining the shared park with <paramref name="code"/> (as typed) over <paramref name="link"/>: the link opens now, unless this game could not follow a shared park
	/// (<see cref="CoopJoinOutcome.NotAllowed"/> at once: no socket opens and nothing goes out). <paramref name="takeOver"/> replaces another game of the account that holds
	/// the link (the player's explicit choice after <see cref="CoopStop.InUseElsewhere"/>; C2d-3b).
	/// </summary>
	internal CoopJoinAttempt( CoopLink link, string code, bool takeOver = false )
	{
		ArgumentNullException.ThrowIfNull( link );
		ArgumentNullException.ThrowIfNull( code );
		this.link = link;
		Code = code;
		if ( CoopSession.JoinRefusalKind is var kind and not CoopJoinRefusal.None )
		{
			NotAllowedBecause = kind;
			Finish( CoopJoinOutcome.NotAllowed, CoopSession.JoinRefusal! );
			return;
		}
		link.SessionStarted += OnSessionStarted;
		link.Refused += OnRefused;
		link.SessionEnded += OnSessionEnded;
		Link.Open( takeOver );
	}

	/// <summary>The code as the player typed it.</summary>
	public string Code { get; }

	public CoopJoinStage Stage { get; private set; } = CoopJoinStage.Opening;

	public CoopJoinOutcome Outcome { get; private set; }

	/// <summary>Why the server refused the join, for <see cref="CoopJoinOutcome.Refused"/>.</summary>
	public CoopReason Refusal { get; private set; }

	/// <summary>Why the link stopped, for <see cref="CoopJoinOutcome.LinkStopped"/>.</summary>
	public CoopStop Stop { get; private set; }

	/// <summary>Why this game cannot join, for <see cref="CoopJoinOutcome.NotAllowed"/>.</summary>
	public CoopJoinRefusal NotAllowedBecause { get; private set; }

	/// <summary>The seat the server gave, once it answered.</summary>
	public CoopSessionState? Answer => answered;

	/// <summary>The joined session, once the game took the join.</summary>
	public CoopSession? Session { get; private set; }

	/// <summary>
	/// What ended the session for <see cref="CoopJoinOutcome.Ended"/>: the joined session's end (<see cref="CoopSession.EndCause"/>), or, before the game took the join, the
	/// server's end of the seat (<see cref="CoopSessionEnd.Server"/> with <see cref="ServerReason"/>) or a lost link.
	/// </summary>
	public CoopSessionEnd EndCause { get; private set; }

	/// <summary>The server's reason for an end by the server; <see cref="CoopReason.None"/> otherwise.</summary>
	public CoopReason ServerReason { get; private set; }

	/// <summary>Whether the game took the join: from then on the game's own end dialog says why a session ended (<see cref="GameFlow"/>'s join end), never the attempt's screen.</summary>
	public bool Taken => Session != null;

	/// <summary>What went wrong, in English, for the log; null while nothing did.</summary>
	public string? FailureText { get; private set; }

	/// <summary>The link this attempt joins over.</summary>
	public CoopLink Link => link;

	/// <summary>The attempt's step, once a frame right after the online pump and before the co-op session's step.</summary>
	public void Step()
	{
		switch ( Stage )
		{
			case CoopJoinStage.Opening:
				StepOpening();
				return;
			case CoopJoinStage.Joining:
			case CoopJoinStage.Cancelling:
				StepJoining();
				return;
			case CoopJoinStage.Building:
				StepBuilding();
				return;
		}
	}

	private void StepOpening()
	{
		// an open on its way does nothing; a closed link opens again (an older server stops it at once, without a state change)
		if ( link.State == CoopLinkState.Closed && link.Stopped == CoopStop.None )
			Link.Open();
		if ( link.Stopped != CoopStop.None )
		{
			Stop = link.Stopped;
			Finish( CoopJoinOutcome.LinkStopped, $"The link to shared parks stopped: {link.Stopped}." );
			return;
		}
		if ( link.State != CoopLinkState.Open )
			return;
		if ( link.Session != null )
		{
			Finish( CoopJoinOutcome.InSession, "This game is in a shared park already." );
			return;
		}
		// nothing goes out that this game could not follow
		if ( CoopSession.JoinRefusalKind is var kind and not CoopJoinRefusal.None )
		{
			NotAllowedBecause = kind;
			Finish( CoopJoinOutcome.NotAllowed, CoopSession.JoinRefusal! );
			return;
		}
		if ( CoopProtocol.NormalizeCode( Code ) == null )
		{
			Finish( CoopJoinOutcome.NotACode, $"\"{Code}\" is not a shared park's code." );
			return;
		}
		if ( !link.Join( Code ) )
		{
			Finish( CoopJoinOutcome.InSession, "The join could not be sent (a create or join waits)." );
			return;
		}
		joinSent = true;
		Stage = CoopJoinStage.Joining;
	}

	private void StepJoining()
	{
		if ( refused is { } error )
		{
			refused = null;
			if ( Stage == CoopJoinStage.Cancelling )
			{
				Finish( CoopJoinOutcome.Cancelled, "Cancelled." );
				return;
			}
			Refusal = error.Reason;
			Finish( CoopJoinOutcome.Refused, $"The server refused the join ({error.Reason}): {error.Text}" );
			return;
		}
		if ( answered == null )
			return;
		if ( Stage == CoopJoinStage.Cancelling )
		{
			LeaveSeat();
			Finish( CoopJoinOutcome.Cancelled, "Cancelled before the park was joined." );
			return;
		}
		// the game takes it in its own step, after this one (GameFlow.StepCoopJoin)
		Stage = CoopJoinStage.Answered;
	}

	private void StepBuilding()
	{
		if ( Session is not { } session )
			return;
		if ( session.EndedWith != null )
		{
			EndCause = session.EndCause;
			ServerReason = session.ServerReason;
			Finish( session.EndCause == CoopSessionEnd.Left && cancelling ? CoopJoinOutcome.Cancelled : CoopJoinOutcome.Ended, session.EndedWith );
			return;
		}
		if ( session.Level != null )
			Finish( CoopJoinOutcome.Joined, "Joined." );
	}

	/// <summary>
	/// The game takes the answered join: <paramref name="session"/> is its joined session (<see cref="GameFlow.JoinSharedPark(CoopLink)"/>), which gets and builds the host's park.
	/// Only in <see cref="CoopJoinStage.Answered"/>.
	/// </summary>
	public void Accept( CoopSession session )
	{
		ArgumentNullException.ThrowIfNull( session );
		if ( Stage != CoopJoinStage.Answered )
			throw new InvalidOperationException( "Only an answered join can be taken." );
		Session = session;
		Stage = CoopJoinStage.Building;
	}

	/// <summary>
	/// The game cannot take the answered join (a park, a transition, another attempt came first): the seat is left. A session the server ended in the same pump was the server's
	/// end (<see cref="CoopJoinOutcome.Ended"/>). Only in <see cref="CoopJoinStage.Answered"/>.
	/// </summary>
	public void Reject( string? failure = null )
	{
		if ( Stage != CoopJoinStage.Answered )
			return;
		if ( endedEarly is { } ended )
		{
			EndCause = ended.Reason is CoopReason.Unknown ? CoopSessionEnd.LinkLost : ended.Reason is CoopReason.Left ? CoopSessionEnd.Left : CoopSessionEnd.Server;
			ServerReason = EndCause == CoopSessionEnd.Server ? ended.Reason : CoopReason.None;
			Finish( CoopJoinOutcome.Ended, $"The shared park ended before it was joined: {ended.Text}" );
			return;
		}
		LeaveSeat();
		Finish( CoopJoinOutcome.NotTaken, failure ?? "The game could not take the join." );
	}

	/// <summary>
	/// Stops the attempt: at once while the link opens; once the join went out, when the server answers (a seat it gives is left then); once joined, by ending the session
	/// (its leave goes out and a partial build is abandoned: <paramref name="endSession"/>, the game's own end of a join that shows no failure).
	/// </summary>
	public void Cancel( Action<CoopSession>? endSession = null )
	{
		switch ( Stage )
		{
			case CoopJoinStage.Opening:
				Finish( CoopJoinOutcome.Cancelled, "Cancelled." );
				return;
			case CoopJoinStage.Joining:
				Stage = CoopJoinStage.Cancelling;
				return;
			case CoopJoinStage.Answered:
				LeaveSeat();
				Finish( CoopJoinOutcome.Cancelled, "Cancelled before the park was joined." );
				return;
			case CoopJoinStage.Building when Session is { EndedWith: null } session:
				cancelling = true;
				if ( endSession != null )
					endSession( session );
				else
					session.End( GameFlow.LeftSharedPark );
				StepBuilding();
				return;
		}
	}

	/// <summary>Leaves the seat the server gave this attempt, while the link still holds it.</summary>
	private void LeaveSeat()
	{
		if ( answered is { } seat && link.Session is { } held && held.Id == seat.Id )
			link.Leave( NotJoined );
	}

	/// <summary>Whether the link still holds the seat the server gave (a seat the server ended in the same pump is gone).</summary>
	public bool HoldsSeat => answered is { } seat && endedEarly == null && link.Session is { } held && held.Id == seat.Id && held.You != held.Host;

	private void OnSessionStarted( CoopSessionState state )
	{
		// the answer to this attempt's join (only it joins while it waits)
		if ( joinSent && answered == null && Stage is CoopJoinStage.Joining or CoopJoinStage.Cancelling && state.You != state.Host )
			answered = state;
	}

	private void OnRefused( CoopError error )
	{
		if ( error.Request == CoopMessageType.Join && joinSent && answered == null && Stage is CoopJoinStage.Joining or CoopJoinStage.Cancelling )
			refused = error;
	}

	private void OnSessionEnded( CoopEnded ended )
	{
		// the seat ended before the game took it (in the same pump as its answer, or before the step): the step reports the server's end
		if ( answered is { } seat && ended.SessionId == seat.Id && Session == null )
			endedEarly = ended;
	}

	private void Finish( CoopJoinOutcome outcome, string text )
	{
		if ( Stage == CoopJoinStage.Done )
			return;
		Stage = CoopJoinStage.Done;
		Outcome = outcome;
		FailureText = outcome is CoopJoinOutcome.Joined or CoopJoinOutcome.Cancelled ? null : text;
		if ( FailureText != null )
			Log.Warning( $"Joining the shared park failed: {FailureText}" );
		Unsubscribe();
	}

	private void Unsubscribe()
	{
		link.SessionStarted -= OnSessionStarted;
		link.Refused -= OnRefused;
		link.SessionEnded -= OnSessionEnded;
	}

	/// <summary>
	/// Forgets the attempt (a new one replaces it, or the game ends). One waiting for its join's answer is left behind: the login's close leaves the seat a waiting join takes
	/// (<see cref="CoopLink.Close"/>), and a later welcome leaves any seat the link no longer holds. A joined session is the game's.
	/// </summary>
	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		Unsubscribe();
	}
}
