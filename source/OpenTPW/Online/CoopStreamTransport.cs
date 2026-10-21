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
}
