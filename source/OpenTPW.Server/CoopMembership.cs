namespace OpenTPW.Server;

/// <summary>
/// [EXT:online-coop-session] What the chat asks of the shared parks (docs/SERVER.md, "Shared parks"): whether an account is a member of a session, which lets it
/// into that session's chat room, and which accounts stop being members, whom the chat then takes out of that room.
/// </summary>
public interface ICoopMembership
{
	/// <summary>Whether <paramref name="accountKey"/> holds a seat in the running session <paramref name="sessionId"/>; it waits on no lock of the shared parks.</summary>
	bool IsMember( string accountKey, string sessionId );

	/// <summary>
	/// Raised with an account and the session it stopped being a member of (it left or was kicked, or the session ended), once <see cref="IsMember"/> says so. Raised
	/// under the shared parks' locks, before the change is announced: a handler waits on none of them and does not call back into the shared parks; one that throws
	/// is reported, not thrown into the change.
	/// </summary>
	event Action<string, string>? MemberGone;
}
