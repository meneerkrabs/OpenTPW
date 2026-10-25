using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>[EXT:online-coop-commands] Where a guest's request to the host stands (<see cref="CoopGuestCommands.Request"/>).</summary>
public enum CoopGuestRequestState : byte
{
	/// <summary>Numbered, not sent yet (the guest's park is being resynced).</summary>
	Waiting = 1,
	/// <summary>Sent; neither its record nor a refusal has come.</summary>
	Sent = 2,
	/// <summary>Its record applied in the guest's park: <see cref="CoopGuestCommands.Request.Result"/> is what the command did there, as on the host.</summary>
	Applied = 3,
	/// <summary>Its record is inside a join snapshot the guest's park was rebuilt from: it applied, and its result here is not known.</summary>
	InSnapshot = 4,
	/// <summary>The host took no record of it (<see cref="CoopGuestCommands.Request.Refusal"/>).</summary>
	Refused = 5,
	/// <summary>The session ended before it resolved: whether the host applied it is not known, and no longer matters to a park that is not shared.</summary>
	Ended = 6,
	/// <summary>
	/// The host no longer knows what became of it (its answer is older than the ones the host keeps, <see cref="CoopGuestRefusal.Forgotten"/>): it may have applied, and
	/// the park shows whether it did. The guest's own resends make this rare (<see cref="CoopGuestCommands.ResendSeconds"/>).
	/// </summary>
	Unknown = 7
}

/// <summary>
/// [EXT:online-coop-commands] A guest's requests to the host (docs/COOP.md, "Building by every player"), for the whole session: a resync replaces the guest's park and stream, never its
/// requests. Each request is numbered from 1 and sent once to the host (<see cref="CoopGuestCommand"/>); it resolves when its own record applies in the guest's park (the stream carries the
/// request's origin), as a refusal the host answers without a record, or as applied inside the join snapshot a resync rebuilt the park from. The guest's park changes through the stream only:
/// a request changes nothing by itself. After a lost link resumes, after a resync, and when a request waits longer than <see cref="ResendSeconds"/> for its outcome (a message lost on
/// its way), every open request is sent again in order; the host answers a request it has had already with its record, never applying it twice, and a request that skips one the host
/// has not had with the number it expects, from which the guest sends again. While a resync runs (<see cref="Fenced"/>) requests wait. The session's end resolves the open ones as ended.
/// </summary>
internal sealed class CoopGuestCommands : IDisposable
{
	/// <summary>One request: its number, its command, where it stands, and what became of it.</summary>
	public sealed class Request
	{
		internal Request( uint sequence, ParkCommand command, byte[] bytes )
		{
			Sequence = sequence;
			Command = command;
			Bytes = bytes;
		}

		public uint Sequence { get; }
		public ParkCommand Command { get; }
		internal byte[] Bytes { get; }
		public CoopGuestRequestState State { get; internal set; } = CoopGuestRequestState.Waiting;
		/// <summary>When it last went to the host (the ledger's clock), for its resend.</summary>
		internal double SentAt { get; set; }
		/// <summary>The stream record that holds it, once known (its own record, or the host's answer to a request sent twice).</summary>
		public long Record { get; internal set; }
		/// <summary>Why the host took no record of it (<see cref="CoopGuestRequestState.Refused"/>).</summary>
		public CoopGuestRefusal? Refusal { get; internal set; }
		/// <summary>What its command did in the guest's park (<see cref="CoopGuestRequestState.Applied"/>).</summary>
		public ParkCommandResult? Result { get; internal set; }
		public bool IsOpen => State is CoopGuestRequestState.Waiting or CoopGuestRequestState.Sent;
		/// <summary>Whether its command applied as accepted in the guest's park (its record's result here).</summary>
		public bool Accepted => State == CoopGuestRequestState.Applied && Result is { Accepted: true };
	}

	/// <summary>How long a request waits for its outcome before every open request goes to the host again: a request or its answer lost on its way, which no resume shows.</summary>
	public const double ResendSeconds = 2;

	private readonly ICoopStreamTransport transport;
	private readonly Func<double> seconds;
	private readonly SortedDictionary<uint, Request> open = new();
	/// <summary>The requests resolved since the last <see cref="TakeCompleted"/>, in order: the tools complete from them outside any command and the stream's application.</summary>
	private readonly Queue<Request> completed = new();
	private uint nextSequence = 1;
	/// <summary>The last stream record the guest's park holds: applied here, or inside the join snapshot it was rebuilt from.</summary>
	private long included;
	/// <summary>The number a gap answer last had every open request sent again from (0 none since the last resume or resync): a burst of gap answers resends once.</summary>
	private uint gapResentFrom;
	private bool fenced;
	private bool disposed;

	/// <param name="seconds">A monotonic clock in seconds (wall time; the tests' virtual one), for the resends.</param>
	public CoopGuestCommands( ICoopStreamTransport transport, Func<double>? seconds = null )
	{
		this.transport = transport ?? throw new ArgumentNullException( nameof( transport ) );
		this.seconds = seconds ?? CoopHostStream.DefaultSeconds;
		transport.MessageReceived += OnMessage;
		transport.Resumed += OnResumed;
	}

	/// <summary>
	/// Raised once for each request that resolves (applied, in a snapshot, refused, unknown, or ended with the session), where it resolves: inside the stream's application of its record, or
	/// in the link's pump. A listener must not act on the park or a tool; the tools take their completions later (<see cref="TakeCompleted"/>).
	/// </summary>
	public event Action<Request>? Resolved;

	/// <summary>
	/// [EXT:online-coop-commands] The requests resolved since the last call, oldest first (C3a-2): the HUD takes them at the start of its update, outside any command, tick and stream
	/// application, and completes the tools that still own them; any other is an orphan, whose outcome is only a message.
	/// </summary>
	public IReadOnlyList<Request> TakeCompleted()
	{
		if ( completed.Count == 0 )
			return Array.Empty<Request>();
		var taken = completed.ToArray();
		completed.Clear();
		return taken;
	}

	/// <summary>The requests not resolved yet, oldest first.</summary>
	public IReadOnlyCollection<Request> Open => open.Values;

	/// <summary>While a resync runs, requests wait; when it ends they go out (<see cref="Snapshot"/> lifts it).</summary>
	public bool Fenced
	{
		get => fenced;
		set
		{
			if ( fenced == value )
				return;
			fenced = value;
			if ( !fenced )
				SendOpen();
		}
	}

	/// <summary>The last stream record the guest's park holds (applied here, or inside the join snapshot it was rebuilt from).</summary>
	public long Included => included;

	/// <summary>Numbers <paramref name="command"/> and sends it to the host (or keeps it while fenced); throws for a command with no stream form, or once the session ended.</summary>
	public Request Submit( ParkCommand command )
	{
		ArgumentNullException.ThrowIfNull( command );
		ObjectDisposedException.ThrowIf( disposed, this );
		var bytes = ParkCommandCodec.Encode( command );
		var request = new Request( nextSequence++, command, bytes );
		open.Add( request.Sequence, request );
		LastSubmitted = request;
		if ( !fenced )
			Send( request );
		return request;
	}

	/// <summary>The request submitted last, resolved or not: what a tool's click sent, which the live acceptance follows to its outcome however fast it resolves.</summary>
	public Request? LastSubmitted { get; private set; }

	/// <summary>
	/// Once a game frame: when an open request has waited <see cref="ResendSeconds"/> for its outcome since it last went out, every open request goes to the host again, in order
	/// (the host answers those it has had). Nothing goes out while fenced.
	/// </summary>
	public void Step()
	{
		if ( disposed || fenced || open.Count == 0 )
			return;
		var now = seconds();
		if ( open.Values.Any( request => request.State == CoopGuestRequestState.Waiting || now - request.SentAt >= ResendSeconds ) )
		{
			gapResentFrom = 0;
			SendOpen();
		}
	}

	/// <summary>
	/// A record of the stream applied in the guest's park as on the host (<see cref="CoopGuestStream.RecordApplied"/>): when it holds one of this guest's requests, the request resolves with
	/// the command's result here, the host's by the stream's determinism.
	/// </summary>
	public void RecordApplied( long record, CoopRecordOrigin? origin, ParkCommandResult result )
	{
		if ( disposed )
			return;
		included = Math.Max( included, record );
		if ( origin is not { } from || transport.Session is not { } session || from.Member != session.You || !open.Remove( from.Sequence, out var request ) )
			return;
		request.Record = record;
		request.Result = result;
		Resolve( request, CoopGuestRequestState.Applied );
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] The guest's park was rebuilt from a join snapshot whose cut holds the records up to <paramref name="records"/>: a request whose record is known and inside
	/// it resolves as applied there; the rest go out again (the host answers those it has had), and the fence lifts.
	/// </summary>
	public void Snapshot( long records )
	{
		if ( disposed )
			return;
		included = records;
		foreach ( var request in open.Values.Where( request => request.Record > 0 && request.Record <= records ).ToArray() )
		{
			open.Remove( request.Sequence );
			Resolve( request, CoopGuestRequestState.InSnapshot );
		}
		fenced = false;
		gapResentFrom = 0;
		SendOpen();
	}

	/// <summary>The session ended (or this game left it): every open request resolves as ended, and nothing more is sent or taken.</summary>
	public void End()
	{
		if ( disposed )
			return;
		Dispose();
		foreach ( var request in open.Values.ToArray() )
			Resolve( request, CoopGuestRequestState.Ended );
		open.Clear();
	}

	private void OnMessage( CoopRelayed relayed )
	{
		if ( disposed || transport.Session is not { } session || relayed.From != session.Host )
			return;
		CoopStreamMessage message;
		try
		{
			message = CoopStreamMessage.Decode( relayed.Payload.Span );
		}
		catch ( InvalidDataException )
		{
			// the guest's stream logs a message it cannot read
			return;
		}
		if ( message is CoopGuestCommandAnswer answer )
			Answered( answer );
	}

	/// <summary>
	/// The host's answer to a request it took no record of, or had already: a refusal resolves it; a gap sends the open requests again from the number the host expects (once for a burst
	/// of gap answers); a record the host names resolves the request as inside the snapshot when the guest's park holds that record already without having seen it apply (a resync's
	/// snapshot), and is otherwise remembered until it applies here.
	/// </summary>
	private void Answered( CoopGuestCommandAnswer answer )
	{
		if ( answer.Refusal == CoopGuestRefusal.Gap )
		{
			if ( !fenced && answer.Next != gapResentFrom )
			{
				gapResentFrom = answer.Next;
				SendOpen( answer.Next );
			}
			return;
		}
		if ( !open.TryGetValue( answer.Sequence, out var request ) )
			return;
		if ( answer.Refusal is { } refusal )
		{
			open.Remove( answer.Sequence );
			request.Refusal = refusal;
			Resolve( request, refusal == CoopGuestRefusal.Forgotten ? CoopGuestRequestState.Unknown : CoopGuestRequestState.Refused );
			return;
		}
		request.Record = answer.Record;
		if ( answer.Record <= included )
		{
			open.Remove( answer.Sequence );
			Resolve( request, CoopGuestRequestState.InSnapshot );
		}
	}

	/// <summary>After the guest's own resume: every open request again, in order, since its record or answer may have been lost with the socket.</summary>
	private void OnResumed( CoopSessionState session )
	{
		if ( disposed || fenced )
			return;
		gapResentFrom = 0;
		SendOpen();
	}

	private void SendOpen( uint from = 1 )
	{
		if ( disposed )
			return;
		foreach ( var request in open.Values.Where( request => request.Sequence >= from ).ToArray() )
			Send( request );
	}

	private void Send( Request request )
	{
		if ( transport.Session is not { } session || !transport.IsOpen )
			return;
		if ( transport.Send( session.Host, new CoopGuestCommand( request.Sequence, request.Bytes ).Encode() ) )
		{
			request.State = CoopGuestRequestState.Sent;
			request.SentAt = seconds();
		}
	}

	private void Resolve( Request request, CoopGuestRequestState state )
	{
		request.State = state;
		completed.Enqueue( request );
		// a listener that throws keeps neither the other listeners nor the stream that applied the record from going on
		foreach ( var listener in Resolved?.GetInvocationList() ?? Array.Empty<Delegate>() )
		{
			try { ((Action<Request>)listener)( request ); }
			catch ( Exception error ) when ( error is not OutOfMemoryException ) { Log?.Warning( $"A shared park request's listener failed: {error}" ); }
		}
	}

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		transport.MessageReceived -= OnMessage;
		transport.Resumed -= OnResumed;
	}
}
