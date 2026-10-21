using OpenTPW.Online.Coop;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-lockstep] The part of the co-op link a session's stream uses (<see cref="CoopHostStream"/>, <see cref="CoopGuestStream"/>): the session, the host's frames, the
/// members' messages and the link's events, all on the game thread. <see cref="CoopLink"/> is the game's; the tests use an in-memory one with the server's guarantees.
/// </summary>
internal interface ICoopStreamTransport
{
	/// <summary>The session this game is in, as the server last told it; null outside one.</summary>
	CoopSessionState? Session { get; }
	/// <summary>Whether frames and messages can go out now (the link is open); while it is not, they wait for a resume.</summary>
	bool IsOpen { get; }
	/// <summary>Sends the host's frame <paramref name="seq"/>; false when it cannot go out now.</summary>
	bool SendFrame( ulong seq, ReadOnlyMemory<byte> payload );
	/// <summary>Sends a message to member <paramref name="to"/> (at most once); false when it cannot go out now.</summary>
	bool Send( uint to, ReadOnlyMemory<byte> payload );
	/// <summary>Leaves the session with <paramref name="reason"/> (a host's leave ends it for everyone).</summary>
	bool Leave( string reason );
	event Action<CoopSessionState>? Resumed;
	event Action<CoopFrameRefused>? FrameRefused;
	event Action<CoopMemberChanged>? MemberChanged;
	event Action<CoopFrame>? FrameReceived;
	event Action<CoopRelayed>? MessageReceived;
	/// <summary>This game is no longer in its session (kicked, the host left, it ended while away, the link closed).</summary>
	event Action<CoopEnded>? SessionEnded;
	/// <summary>
	/// [EXT:online-coop-snapshot-join] Uploads the host's join snapshot of its session (docs/COOP.md, "Joining a running session"); <paramref name="done"/> runs once on the game thread, from a
	/// later pump, with the stored snapshot's digest or why it failed.
	/// </summary>
	void UploadSnapshot( byte[] payload, Action<CoopSnapshotTransfer> done );
	/// <summary>
	/// [EXT:online-coop-snapshot-join] Downloads join snapshot <paramref name="sha256"/> of the session, <paramref name="bytes"/> long (a stalled or broken transfer resumes with a range);
	/// <paramref name="done"/> runs once on the game thread, from a later pump, with the bytes checked against the digest, or why it failed.
	/// </summary>
	void DownloadSnapshot( string sha256, long bytes, Action<CoopSnapshotTransfer> done );
}

/// <summary>
/// [EXT:online-coop-snapshot-join] What became of a join snapshot's upload or download (<see cref="ICoopStreamTransport.UploadSnapshot"/>, <see cref="ICoopStreamTransport.DownloadSnapshot"/>):
/// the stored snapshot's digest or the downloaded bytes; else why it failed, whether trying again cannot help, when the server asks to be tried again, and whether the server no longer keeps
/// the snapshot (it keeps a session's newest two).
/// </summary>
internal sealed record CoopSnapshotTransfer( string? Sha256, byte[]? Payload, string? Failure, bool Permanent = false, TimeSpan? RetryAfter = null, bool Missing = false,
	bool TooLarge = false )
{
	public bool Succeeded => Failure == null;
	public static CoopSnapshotTransfer Stored( string sha256 ) => new( sha256, null, null );
	public static CoopSnapshotTransfer Downloaded( byte[] payload ) => new( null, payload, null );
	public static CoopSnapshotTransfer Failed( string reason, bool permanent = false, TimeSpan? retryAfter = null, bool missing = false, bool tooLarge = false ) =>
		new( null, null, reason, permanent || tooLarge, retryAfter, missing, tooLarge );
}
