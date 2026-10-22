using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-snapshot-join] The native join smoke's host side (<see cref="CoopJoinSmokeTest"/>): one session of the host and a virtual member, in process. It records every frame the
/// host's stream hands it with the host frame it was sent in, every message to the member, and every snapshot uploaded (answered as stored at the next pump). The member joins when the smoke
/// says (the host then assigns it a capture at once) and asks for a capture again when the smoke says; it reports no progress, so the host neither waits for it nor trims below its cut.
/// </summary>
internal sealed class CoopSmokeHostTransport( Func<long> hostFrame ) : ICoopStreamTransport
{
	public const uint HostId = 1;
	public const uint MemberId = 2;

	private readonly Queue<Action> due = new();
	private static readonly CoopMemberInfo HostInfo = new( HostId, 0, "Host", true );
	private static readonly CoopMemberInfo MemberInfo = new( MemberId, 1, "Joiner", true );
	private bool memberIn;

	public CoopSessionState? Session => new( "smoke", "SMOKEA", "Smoke park", false, 4, HostId, HostId, Frames.Count > 0 ? Frames[^1].Seq : 0, true,
		memberIn ? new[] { HostInfo, MemberInfo } : new[] { HostInfo } );
	public bool IsOpen => true;

	/// <summary>The host's frames in order, with the host frame each was handed over in.</summary>
	public List<(ulong Seq, long HostFrame, byte[] Payload)> Frames { get; } = new();

	/// <summary>The host's messages to the member, with the host frame each was sent in.</summary>
	public List<(long HostFrame, CoopStreamMessage Message)> Messages { get; } = new();

	/// <summary>The snapshots uploaded, by their SHA-256.</summary>
	public Dictionary<string, byte[]> Uploads { get; } = new( StringComparer.Ordinal );

	public string? LeftWith { get; private set; }

	public bool SendFrame( ulong seq, ReadOnlyMemory<byte> payload )
	{
		Frames.Add( (seq, hostFrame(), payload.ToArray()) );
		return true;
	}

	public bool Send( uint to, ReadOnlyMemory<byte> payload )
	{
		if ( to == MemberId )
			Messages.Add( (hostFrame(), CoopStreamMessage.Decode( payload.Span )) );
		return true;
	}

	public bool Leave( string reason )
	{
		LeftWith = reason;
		return true;
	}

#pragma warning disable CS0067 // a session the smoke never resumes, refuses a frame of or ends from outside
	public event Action<CoopSessionState>? Resumed;
	public event Action<CoopFrameRefused>? FrameRefused;
	public event Action<CoopFrame>? FrameReceived;
	public event Action<CoopEnded>? SessionEnded;
#pragma warning restore CS0067
	public event Action<CoopMemberChanged>? MemberChanged;
	public event Action<CoopRelayed>? MessageReceived;

	public void UploadSnapshot( byte[] payload, Action<CoopSnapshotTransfer> done )
	{
		var sha = CoopJoinSmokeTest.Sha256Hex( payload );
		Uploads[sha] = payload;
		due.Enqueue( () => done( CoopSnapshotTransfer.Stored( sha ) ) );
	}

	public void DownloadSnapshot( string sha256, long bytes, Action<CoopSnapshotTransfer> done ) => throw new NotSupportedException( "The host downloads no snapshot." );

	/// <summary>The virtual member joins the running session: the host assigns it a capture at once.</summary>
	public void MemberJoins()
	{
		memberIn = true;
		due.Enqueue( () => MemberChanged?.Invoke( new CoopMemberChanged( MemberInfo, CoopMemberState.Joined ) ) );
	}

	/// <summary>The virtual member asks for a capture.</summary>
	public void MemberAsks( CoopSnapshotRequest request ) => due.Enqueue( () => MessageReceived?.Invoke( new CoopRelayed( MemberId, request.Encode() ) ) );

	/// <summary>Raises what is due: the joins, the requests and the upload answers since the last pump.</summary>
	public void Pump()
	{
		for ( var count = due.Count; count > 0; count-- )
			due.Dequeue()();
	}
}

/// <summary>
/// [EXT:online-coop-snapshot-join] The native join smoke's guest side (<see cref="CoopJoinSmokeTest"/>): it plays the recorded host to a game that joins. The host's frames after the member's join
/// go out at the pace they were recorded in (<see cref="DeliverUpTo"/>); what the game sends is handed to the smoke's script (<see cref="Heard"/>), which answers in the host's place
/// (<see cref="Answer"/>); a resend request is answered from the frames delivered so far; a download is served from the snapshots the script stored (<see cref="Serve"/>) at the next pump.
/// </summary>
internal sealed class CoopSmokeReplayTransport : ICoopStreamTransport
{
	public const uint HostId = 1;
	public const uint JoinerId = 3;

	private readonly IReadOnlyList<(ulong Seq, long HostFrame, byte[] Payload)> frames;
	private readonly ulong joinedAt;
	private readonly Queue<Action> due = new();
	private readonly Dictionary<string, byte[]> served = new( StringComparer.Ordinal );
	private int cursor;

	/// <param name="frames">The host's recorded frames after the frame the member joined at (<paramref name="joinedAt"/>), in order.</param>
	public CoopSmokeReplayTransport( IReadOnlyList<(ulong Seq, long HostFrame, byte[] Payload)> frames, ulong joinedAt )
	{
		this.frames = frames;
		this.joinedAt = joinedAt;
		Session = new CoopSessionState( "smoke", "SMOKEA", "Smoke park", false, 4, JoinerId, HostId, joinedAt, true,
			new[] { new CoopMemberInfo( HostId, 0, "Host", true ), new CoopMemberInfo( JoinerId, 1, "Joiner", true ) } );
	}

	public CoopSessionState? Session { get; private set; }
	public bool IsOpen => true;

	/// <summary>The last frame delivered (the member's join frame before any).</summary>
	public ulong Delivered => cursor == 0 ? joinedAt : frames[cursor - 1].Seq;

	/// <summary>Whether every recorded frame went out.</summary>
	public bool AllDelivered => cursor == frames.Count;

	/// <summary>Everything the game sent, in order.</summary>
	public List<CoopStreamMessage> Sent { get; } = new();

	/// <summary>The downloads the game asked for, by SHA-256, in order.</summary>
	public List<string> Downloads { get; } = new();

	/// <summary>The smoke's script: what the game sent, to answer in the host's place.</summary>
	public Action<CoopStreamMessage>? Heard { get; set; }

	public string? LeftWith { get; private set; }

	public bool SendFrame( ulong seq, ReadOnlyMemory<byte> payload ) => throw new NotSupportedException( "A guest sends no frame." );

	public bool Send( uint to, ReadOnlyMemory<byte> payload )
	{
		if ( to != HostId )
			return false;
		var message = CoopStreamMessage.Decode( payload.Span );
		Sent.Add( message );
		if ( message is CoopResendRequest request )
			Resend( request );
		Heard?.Invoke( message );
		return true;
	}

	public bool Leave( string reason )
	{
		LeftWith = reason;
		return true;
	}

#pragma warning disable CS0067 // a session the smoke never resumes, refuses a frame of, changes the members of or ends from outside
	public event Action<CoopSessionState>? Resumed;
	public event Action<CoopFrameRefused>? FrameRefused;
	public event Action<CoopMemberChanged>? MemberChanged;
	public event Action<CoopEnded>? SessionEnded;
#pragma warning restore CS0067
	public event Action<CoopFrame>? FrameReceived;
	public event Action<CoopRelayed>? MessageReceived;

	public void UploadSnapshot( byte[] payload, Action<CoopSnapshotTransfer> done ) => throw new NotSupportedException( "A guest uploads no snapshot." );

	public void DownloadSnapshot( string sha256, long bytes, Action<CoopSnapshotTransfer> done )
	{
		Downloads.Add( sha256 );
		due.Enqueue( () => done( served.TryGetValue( sha256, out var payload ) && payload.Length == bytes ? CoopSnapshotTransfer.Downloaded( payload )
			: CoopSnapshotTransfer.Failed( "The smoke serves no such snapshot.", missing: true ) ) );
	}

	/// <summary>Stores a snapshot to serve; its SHA-256.</summary>
	public string Serve( byte[] payload )
	{
		var sha = CoopJoinSmokeTest.Sha256Hex( payload );
		served[sha] = payload;
		return sha;
	}

	/// <summary>A message from the host, raised at the next pump.</summary>
	public void Answer( CoopStreamMessage message ) => due.Enqueue( () => MessageReceived?.Invoke( new CoopRelayed( HostId, message.Encode() ) ) );

	/// <summary>Delivers the recorded frames the host had sent by host frame <paramref name="hostFrame"/>, and none after <paramref name="lastSeq"/>.</summary>
	public void DeliverUpTo( long hostFrame, ulong lastSeq = ulong.MaxValue )
	{
		while ( cursor < frames.Count && frames[cursor].HostFrame <= hostFrame && frames[cursor].Seq <= lastSeq )
		{
			var (seq, _, payload) = frames[cursor++];
			FrameReceived?.Invoke( new CoopFrame( seq, payload ) );
		}
	}

	/// <summary>The host's resend of the delivered frames the request names, in its generation, in messages of at most the link's size.</summary>
	private void Resend( CoopResendRequest request )
	{
		var batch = new List<(ulong, ReadOnlyMemory<byte>)>();
		var size = CoopResentFrames.EmptyBytes;
		for ( var index = 0; index < cursor; index++ )
		{
			var (seq, _, payload) = frames[index];
			if ( seq < request.From || (request.To != 0 && seq >= request.To) )
				continue;
			var add = CoopResentFrames.FrameOverhead + payload.Length;
			if ( batch.Count > 0 && size + add > CoopLink.MaximumMessagePayload )
			{
				Answer( new CoopResentFrames( batch ) { Generation = request.Generation } );
				batch = new List<(ulong, ReadOnlyMemory<byte>)>();
				size = CoopResentFrames.EmptyBytes;
			}
			batch.Add( (seq, payload) );
			size += add;
		}
		if ( batch.Count > 0 )
			Answer( new CoopResentFrames( batch ) { Generation = request.Generation } );
	}

	/// <summary>Raises what is due: the answers and the downloads since the last pump.</summary>
	public void Pump()
	{
		for ( var count = due.Count; count > 0; count-- )
			due.Dequeue()();
	}
}
