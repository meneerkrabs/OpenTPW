using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>What became of a streamed command on the host (<see cref="CoopStreamRecord.Flags"/>); never its reason text, which depends on the runtime and the language.</summary>
[Flags]
public enum CoopRecordFlags : byte
{
	None = 0,
	Accepted = 1,
	/// <summary>The command threw (<see cref="ParkCommandResult.Error"/>): a desync signal, checked by a digest right after it.</summary>
	Errored = 2
}

/// <summary>[EXT:online-coop-lockstep] One command of the host's stream: the tick it applied before, what became of it, and its bytes (<see cref="ParkCommandCodec"/>).</summary>
public readonly record struct CoopStreamRecord( long Boundary, CoopRecordFlags Flags, ReadOnlyMemory<byte> Bytes );

/// <summary>[EXT:online-coop-lockstep] Where a checkpoint's digest was taken (<see cref="CoopCheckpoint"/>).</summary>
public enum CoopCheckpointKind : byte
{
	/// <summary>At the end of the tick that brought the tick count to <see cref="CoopCheckpoint.At"/>, before the records of that boundary.</summary>
	AfterTick = 1,
	/// <summary>Right after stream record <see cref="CoopCheckpoint.At"/> applied (an errored record), in the frame that holds it.</summary>
	AfterRecord = 2
}

/// <summary>[EXT:online-coop-lockstep] The host's session digest at a point of its stream: after a tick count or after a record.</summary>
public readonly record struct CoopCheckpoint( CoopCheckpointKind Kind, long At, ulong Digest );

/// <summary>
/// [EXT:online-coop-lockstep] A frame of the host's command stream (docs/COOP.md, "Sessions"), the payload of a co-op frame: every tick below <see cref="Horizon"/> has run on
/// the host and every record of a boundary below it is in this frame or an earlier one; the records numbered from <see cref="First"/>; the checkpoints since the frame before.
/// </summary>
public sealed record CoopStreamFrame( long Horizon, long First, IReadOnlyList<CoopStreamRecord> Records, IReadOnlyList<CoopCheckpoint> Checkpoints )
{
	public const byte Version = 1;
	/// <summary>The bytes of a frame without records or checkpoints.</summary>
	public const int EmptyBytes = 1 + 8 + 8 + 2 + 1;
	/// <summary>The bytes one record adds besides its own (boundary, flags, length).</summary>
	public const int RecordOverhead = 8 + 1 + 4;
	public const int CheckpointBytes = 1 + 8 + 8;
	public const int MaximumCheckpoints = byte.MaxValue;
	/// <summary>The most bytes a frame holds: what fits a resend's envelope (<see cref="CoopResentFrames"/>), which the host's frame message also takes.</summary>
	public const int MaximumBytes = CoopLink.MaximumMessagePayload - CoopResentFrames.EmptyBytes - CoopResentFrames.FrameOverhead;

	/// <summary>The bytes this frame encodes to.</summary>
	public int Size => EmptyBytes + Records.Sum( record => RecordOverhead + record.Bytes.Length ) + Checkpoints.Count * CheckpointBytes;

	public byte[] Encode()
	{
		var writer = new CoopWriter();
		writer.Byte( Version );
		writer.U64( checked( (ulong)Horizon ) );
		writer.U64( checked( (ulong)First ) );
		writer.U16( checked( (ushort)Records.Count ) );
		foreach ( var record in Records )
		{
			writer.U64( checked( (ulong)record.Boundary ) );
			writer.Byte( (byte)record.Flags );
			writer.Bytes( record.Bytes.Span );
		}
		writer.Byte( checked( (byte)Checkpoints.Count ) );
		foreach ( var checkpoint in Checkpoints )
		{
			writer.Byte( (byte)checkpoint.Kind );
			writer.U64( checked( (ulong)checkpoint.At ) );
			writer.U64( checkpoint.Digest );
		}
		return writer.ToArray();
	}

	/// <summary>
	/// Reads a frame; anything malformed throws <see cref="InvalidDataException"/>: another version, a field out of range, records out of tick order or past the
	/// horizon, checkpoints of one kind out of order, a record checkpoint whose record is not in the frame, bytes after the fields.
	/// </summary>
	public static CoopStreamFrame Decode( ReadOnlySpan<byte> bytes )
	{
		var reader = new CoopReader( bytes );
		if ( reader.Byte() != Version )
			throw new InvalidDataException( "The stream frame has an unknown version." );
		var horizon = Signed( reader.U64() );
		var first = Signed( reader.U64() );
		if ( first < 1 )
			throw new InvalidDataException( "The stream frame numbers its records from 0." );
		var records = new CoopStreamRecord[reader.U16()];
		if ( first > long.MaxValue - records.Length )
			throw new InvalidDataException( "The stream frame numbers its records past the end." );
		for ( var index = 0; index < records.Length; index++ )
		{
			var boundary = Signed( reader.U64() );
			var flags = (CoopRecordFlags)reader.Byte();
			if ( (flags & ~(CoopRecordFlags.Accepted | CoopRecordFlags.Errored)) != 0 || flags == (CoopRecordFlags.Accepted | CoopRecordFlags.Errored) )
				throw new InvalidDataException( "The stream record has invalid flags." );
			var payload = reader.Bytes();
			if ( payload.Length == 0 )
				throw new InvalidDataException( "The stream record has no command." );
			// a frame's records run in stream order, so their boundaries never fall, and none is at or past the horizon it promises
			if ( index > 0 && boundary < records[index - 1].Boundary || boundary > horizon )
				throw new InvalidDataException( "The stream record's tick is out of order." );
			records[index] = new CoopStreamRecord( boundary, flags, payload );
		}
		var checkpoints = new CoopCheckpoint[reader.Byte()];
		long lastTick = 0, lastRecord = 0;
		for ( var index = 0; index < checkpoints.Length; index++ )
		{
			var kind = (CoopCheckpointKind)reader.Byte();
			var at = Signed( reader.U64() );
			switch ( kind )
			{
				case CoopCheckpointKind.AfterTick when at > lastTick:
					lastTick = at;
					break;
				// a record's digest travels with its record, so a guest compares it right after applying it
				case CoopCheckpointKind.AfterRecord when at > lastRecord && at >= first && at - first < records.Length:
					lastRecord = at;
					break;
				default:
					throw new InvalidDataException( "The stream checkpoint is of an unknown kind, out of order or names a record the frame does not hold." );
			}
			checkpoints[index] = new CoopCheckpoint( kind, at, reader.U64() );
		}
		reader.End();
		return new CoopStreamFrame( horizon, first, records, checkpoints );
	}

	private static long Signed( ulong value ) => value <= long.MaxValue ? (long)value : throw new InvalidDataException( "The stream frame has a number out of range." );
}

/// <summary>[EXT:online-coop-lockstep] Where a guest stands in the host's stream, as its <see cref="CoopProgress"/> reports it.</summary>
public enum CoopGuestState : byte
{
	/// <summary>Within a few seconds of the host: the host waits for it.</summary>
	Following = 1,
	/// <summary>Further behind (back from a stall, or a join replaying): the host does not wait until it is within reach again.</summary>
	CatchingUp = 2,
	/// <summary>Missing frames it has asked for.</summary>
	Gap = 3,
	/// <summary>Stopped at a desync, waiting for a resync.</summary>
	Desynced = 4,
	/// <summary>Missing frames the host no longer has: waiting for a resync.</summary>
	TooFarBack = 5
}

/// <summary>[EXT:online-coop-lockstep] What a guest found different from the host (<see cref="CoopDesyncReport"/>).</summary>
public enum CoopDesyncKind : byte
{
	/// <summary>The session digest after a checkpoint's tick.</summary>
	Digest = 1,
	/// <summary>A streamed command accepted, refused or errored differently.</summary>
	Flags = 2,
	/// <summary>A stream that breaks its own rules: a falling horizon, a past boundary, a gap in the record numbers, a frame that does not decode.</summary>
	Protocol = 3
}

public enum CoopStreamMessageType : byte
{
	Progress = 1,
	Resend = 2,
	Frames = 3,
	TooFarBack = 4,
	Desync = 5,
	DesyncSeen = 6,
	SnapshotAssigned = 7,
	SnapshotRequest = 8,
	SnapshotReady = 9,
	SnapshotFailed = 10
}

/// <summary>[EXT:online-coop-snapshot-join] Why a member asks for a join snapshot (<see cref="CoopSnapshotRequest"/>).</summary>
public enum CoopSnapshotReason : byte
{
	/// <summary>It joined the session and holds no park of it yet.</summary>
	Join = 1,
	/// <summary>Its park desynced.</summary>
	Desync = 2,
	/// <summary>The host no longer holds a frame it needs.</summary>
	TooFarBack = 3,
	/// <summary>The download of its snapshot failed.</summary>
	FailedDownload = 4,
	/// <summary>The snapshot it downloaded failed its checks (its numbers, its digest at the cut).</summary>
	FailedCheck = 5,
	/// <summary>No assignment or answer came within its wait, or it resumed after a lost socket.</summary>
	NoAnswer = 6,
	/// <summary>The server no longer keeps the snapshot it was told of.</summary>
	Missing = 7
}

/// <summary>[EXT:online-coop-snapshot-join] Why the host could not serve a capture (<see cref="CoopSnapshotFailed"/>).</summary>
public enum CoopSnapshotFailure : byte
{
	/// <summary>The upload failed for now (a busy server, too many uploads, no room): the member asks again after the delay.</summary>
	Busy = 1,
	/// <summary>A coaster drag held the capture back past its wait.</summary>
	Held = 2,
	/// <summary>The park is too large for a join snapshot.</summary>
	TooLarge = 3,
	/// <summary>The park cannot be captured (an object without its economy binding: a bug).</summary>
	Uncapturable = 4
}

/// <summary>
/// [EXT:online-coop-lockstep] A message between the members of a session about the host's stream (the payload of a co-op send): progress, resends, desyncs. Every message
/// may be lost (at most once): progress is periodic and carries the guest's state, and a guest repeats a resend or a desync until it is answered.
/// </summary>
public abstract record CoopStreamMessage
{
	public abstract CoopStreamMessageType Type { get; }

	/// <summary>
	/// [EXT:online-coop-snapshot-join] The stream generation the message belongs to: the join snapshot its member's stream started from (0 a session's identical start). Each side ignores
	/// a stream message of another generation than the member's current one; the snapshot messages say what it means for them.
	/// </summary>
	public uint Generation { get; init; }

	private protected abstract void WriteFields( CoopWriter writer );

	public byte[] Encode()
	{
		var writer = new CoopWriter();
		writer.Byte( (byte)Type );
		writer.U32( Generation );
		WriteFields( writer );
		return writer.ToArray();
	}

	public static CoopStreamMessage Decode( ReadOnlySpan<byte> bytes )
	{
		var reader = new CoopReader( bytes );
		var type = (CoopStreamMessageType)reader.Byte();
		var generation = reader.U32();
		CoopStreamMessage message = type switch
		{
			CoopStreamMessageType.Progress => new CoopProgress( State( reader.Byte() ), reader.U64(), Signed( reader.U64() ), reader.U32() ),
			CoopStreamMessageType.Resend => new CoopResendRequest( reader.U64(), reader.U64() ),
			CoopStreamMessageType.Frames => CoopResentFrames.Read( ref reader ),
			CoopStreamMessageType.TooFarBack => new CoopTooFarBack( reader.U64() ),
			CoopStreamMessageType.Desync => CoopDesyncReport.Read( ref reader ),
			CoopStreamMessageType.DesyncSeen => new CoopDesyncSeen( reader.U32() ),
			CoopStreamMessageType.SnapshotAssigned => new CoopSnapshotAssigned(),
			CoopStreamMessageType.SnapshotRequest => new CoopSnapshotRequest( reader.U32(), Reason( reader.Byte() ), reader.Bool() ? CoopDesyncReport.Read( ref reader ) : null ),
			CoopStreamMessageType.SnapshotReady => new CoopSnapshotReady( Signed( reader.U64() ), Signed( reader.U64() ), reader.U64(), reader.U64(), reader.U64(), Sha256( reader.Text( 64 ) ),
				Positive( reader.U64() ) ),
			CoopStreamMessageType.SnapshotFailed => new CoopSnapshotFailed( Failure( reader.Byte() ), reader.Bool(), reader.U32() ),
			var other => throw new InvalidDataException( $"Unknown stream message {(byte)other}." )
		};
		reader.End();
		return message with { Generation = generation };
	}

	private static CoopSnapshotReason Reason( byte value ) => value is >= (byte)CoopSnapshotReason.Join and <= (byte)CoopSnapshotReason.Missing
		? (CoopSnapshotReason)value
		: throw new InvalidDataException( "The stream message has an unknown snapshot reason." );

	private static CoopSnapshotFailure Failure( byte value ) => value is >= (byte)CoopSnapshotFailure.Busy and <= (byte)CoopSnapshotFailure.Uncapturable
		? (CoopSnapshotFailure)value
		: throw new InvalidDataException( "The stream message has an unknown snapshot failure." );

	private static string Sha256( string value ) => CoopProtocol.IsSha256( value ) ? value : throw new InvalidDataException( "The stream message has an invalid snapshot digest." );

	private static long Positive( ulong value ) => value is > 0 and <= long.MaxValue ? (long)value : throw new InvalidDataException( "The stream message has a number out of range." );

	private static CoopGuestState State( byte value ) => value is >= (byte)CoopGuestState.Following and <= (byte)CoopGuestState.TooFarBack
		? (CoopGuestState)value
		: throw new InvalidDataException( "The stream message has an unknown guest state." );

	private protected static CoopDesyncKind DesyncKind( byte value ) => value is >= (byte)CoopDesyncKind.Digest and <= (byte)CoopDesyncKind.Protocol
		? (CoopDesyncKind)value
		: throw new InvalidDataException( "The stream message has an unknown desync kind." );

	private protected static long Signed( ulong value ) => value <= long.MaxValue ? (long)value : throw new InvalidDataException( "The stream message has a number out of range." );
}

/// <summary>A guest's state, the last frame it holds without a gap, the ticks it has run, and the desync it reports until the host has seen it (0 none).</summary>
public sealed record CoopProgress( CoopGuestState State, ulong LastFrame, long Ticks, uint DesyncId ) : CoopStreamMessage
{
	public override CoopStreamMessageType Type => CoopStreamMessageType.Progress;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.Byte( (byte)State );
		writer.U64( LastFrame );
		writer.U64( checked( (ulong)Ticks ) );
		writer.U32( DesyncId );
	}
}

/// <summary>A guest asks for the frames from <paramref name="From"/> up to <paramref name="To"/> (exclusive: the first frame it holds after the gap; 0 when it holds none).</summary>
public sealed record CoopResendRequest( ulong From, ulong To ) : CoopStreamMessage
{
	public override CoopStreamMessageType Type => CoopStreamMessageType.Resend;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.U64( From );
		writer.U64( To );
	}
}

/// <summary>Frames the host sends one guest again, in order, each with its number.</summary>
public sealed record CoopResentFrames( IReadOnlyList<(ulong Seq, ReadOnlyMemory<byte> Payload)> Frames ) : CoopStreamMessage
{
	/// <summary>The bytes one resent frame adds besides its payload (number, length).</summary>
	public const int FrameOverhead = 8 + 4;
	/// <summary>The bytes of a message without frames (type, count).</summary>
	public const int EmptyBytes = 1 + 2;

	public override CoopStreamMessageType Type => CoopStreamMessageType.Frames;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.U16( checked( (ushort)Frames.Count ) );
		foreach ( var (seq, payload) in Frames )
		{
			writer.U64( seq );
			writer.Bytes( payload.Span );
		}
	}

	internal static CoopResentFrames Read( ref CoopReader reader )
	{
		var frames = new (ulong, ReadOnlyMemory<byte>)[reader.U16()];
		for ( var index = 0; index < frames.Length; index++ )
		{
			var seq = reader.U64();
			if ( seq == 0 || index > 0 && seq != frames[index - 1].Item1 + 1 )
				throw new InvalidDataException( "The resent frames are out of order." );
			frames[index] = (seq, reader.Bytes());
		}
		return new CoopResentFrames( frames );
	}
}

/// <summary>The host no longer has frame <paramref name="From"/>: the guest needs a resync.</summary>
public sealed record CoopTooFarBack( ulong From ) : CoopStreamMessage
{
	public override CoopStreamMessageType Type => CoopStreamMessageType.TooFarBack;
	private protected override void WriteFields( CoopWriter writer ) => writer.U64( From );
}

/// <summary>A guest's desync: its id, what differed, the tick, both digests (a digest desync) and the records it had applied.</summary>
public sealed record CoopDesyncReport( uint Id, CoopDesyncKind Kind, long Tick, ulong HostDigest, ulong GuestDigest, long FirstStream, long LastStream ) : CoopStreamMessage
{
	public override CoopStreamMessageType Type => CoopStreamMessageType.Desync;

	internal static CoopDesyncReport Read( ref CoopReader reader ) =>
		new( reader.U32(), DesyncKind( reader.Byte() ), Signed( reader.U64() ), reader.U64(), reader.U64(), Signed( reader.U64() ), Signed( reader.U64() ) );

	/// <summary>The report's fields, without a message's type and generation (a snapshot request carries the report it repeats).</summary>
	internal void WriteReport( CoopWriter writer ) => WriteFields( writer );

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.U32( Id );
		writer.Byte( (byte)Kind );
		writer.U64( checked( (ulong)Tick ) );
		writer.U64( HostDigest );
		writer.U64( GuestDigest );
		writer.U64( checked( (ulong)FirstStream ) );
		writer.U64( checked( (ulong)LastStream ) );
	}
}

/// <summary>The host has seen desync <paramref name="Id"/>: the guest stops repeating it.</summary>
public sealed record CoopDesyncSeen( uint Id ) : CoopStreamMessage
{
	public override CoopStreamMessageType Type => CoopStreamMessageType.DesyncSeen;
	private protected override void WriteFields( CoopWriter writer ) => writer.U32( Id );
}

/// <summary>
/// [EXT:online-coop-snapshot-join] The host assigned the member capture <see cref="CoopStreamMessage.Generation"/> (docs/COOP.md, "Joining a running session"): the member's old stream goes
/// silent until the capture's <see cref="CoopSnapshotReady"/> or <see cref="CoopSnapshotFailed"/>.
/// </summary>
public sealed record CoopSnapshotAssigned : CoopStreamMessage
{
	public override CoopStreamMessageType Type => CoopStreamMessageType.SnapshotAssigned;
	private protected override void WriteFields( CoopWriter writer ) { }
}

/// <summary>
/// [EXT:online-coop-snapshot-join] A member asks for a join snapshot: <paramref name="Known"/> is the newest generation whose answer it received (0 none), with why, and the desync it
/// reports with it (the host may not have seen the report itself).
/// </summary>
public sealed record CoopSnapshotRequest( uint Known, CoopSnapshotReason Reason, CoopDesyncReport? Report ) : CoopStreamMessage
{
	public override CoopStreamMessageType Type => CoopStreamMessageType.SnapshotRequest;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.U32( Known );
		writer.Byte( (byte)Reason );
		writer.Bool( Report != null );
		Report?.WriteReport( writer );
	}
}

/// <summary>
/// [EXT:online-coop-snapshot-join] The host's capture <see cref="CoopStreamMessage.Generation"/> is stored on the server: its cut (records up to <paramref name="Records"/>, tick
/// <paramref name="Ticks"/> next, frame <paramref name="Frame"/> the last), the session digest and the world hash at it, and the stored payload's digest and length.
/// </summary>
public sealed record CoopSnapshotReady( long Records, long Ticks, ulong Frame, ulong Digest, ulong WorldHash, string Sha256, long Bytes ) : CoopStreamMessage
{
	public override CoopStreamMessageType Type => CoopStreamMessageType.SnapshotReady;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.U64( checked( (ulong)Records ) );
		writer.U64( checked( (ulong)Ticks ) );
		writer.U64( Frame );
		writer.U64( Digest );
		writer.U64( WorldHash );
		writer.Text( Sha256, 64 );
		writer.U64( checked( (ulong)Bytes ) );
	}
}

/// <summary>
/// [EXT:online-coop-snapshot-join] The host's capture <see cref="CoopStreamMessage.Generation"/> failed: why, whether trying again cannot help (the member leaves then), and how many seconds the
/// member waits before it asks again.
/// </summary>
public sealed record CoopSnapshotFailed( CoopSnapshotFailure Reason, bool Permanent, uint RetryAfterSeconds ) : CoopStreamMessage
{
	public override CoopStreamMessageType Type => CoopStreamMessageType.SnapshotFailed;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.Byte( (byte)Reason );
		writer.Bool( Permanent );
		writer.U32( RetryAfterSeconds );
	}
}
