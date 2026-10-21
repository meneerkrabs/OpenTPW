using System.Buffers.Binary;
using System.Text;

namespace OpenTPW.Online.Coop;

/// <summary>
/// [EXT:online-coop-session] The co-op socket (<c>/api/v1/coop</c>, server protocol 6): binary messages between a game and the server, which relays a
/// shared session's frames and messages without reading them (docs/ONLINE.md, "Shared parks"). A message is one binary WebSocket message: a type byte and
/// its fields, little-endian, strings UTF-8 with a 16-bit length, bytes with a 32-bit length.
/// </summary>
public static class CoopProtocol
{
	public const string Path = "/api/v1/coop";
	/// <summary>The server protocol that has the co-op socket; a game offers co-op on this protocol or later.</summary>
	public const int Version = 6;
	/// <summary>The largest message either side sends or reads; a join snapshot goes over HTTP instead.</summary>
	public const int MaximumMessageBytes = 256 * 1024;
	/// <summary>Players in a session, the host included (decision Q5).</summary>
	public const int MaximumPlayers = 4;
	public const int MaximumTitleLength = 64;
	public const int MaximumBuildLength = 128;
	public const int MaximumReasonLength = 256;
	/// <summary>A name as typed in an invitation; the server answers an unknown or invalid one as not online.</summary>
	public const int MaximumInviteNameLength = 64;
	/// <summary>A code as typed in a join, before <see cref="NormalizeCode"/> drops its spaces and dashes.</summary>
	public const int MaximumTypedCodeLength = 32;
	/// <summary>Simulation-changing mods a hello lists at most: as many as a save records (<c>ParkResumeMods.MaximumRecords</c>).</summary>
	public const int MaximumMods = 256;
	public const int MaximumModIdLength = 64;
	public const int MaximumModVersionLength = 32;
	/// <summary>A session's id: 16 lowercase hexadecimal digits.</summary>
	public const int SessionIdLength = 16;
	/// <summary>A session's code is this many characters of <see cref="CodeAlphabet"/>.</summary>
	public const int CodeLength = 6;
	/// <summary>Upper-case letters and digits without the ones read alike (0 and O, 1, I and L).</summary>
	public const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
	/// <summary>The member id <see cref="CoopSend"/> uses for every other member (a host's send to all).</summary>
	public const uint EveryOtherMember = 0;

	/// <summary>A code as typed: upper case, spaces and dashes dropped; null when it cannot be a code.</summary>
	public static string? NormalizeCode( string? typed )
	{
		if ( typed == null )
			return null;
		var builder = new StringBuilder( CodeLength );
		foreach ( var c in typed )
		{
			if ( c is ' ' or '-' )
				continue;
			var upper = char.ToUpperInvariant( c );
			if ( !CodeAlphabet.Contains( upper ) || builder.Length == CodeLength )
				return null;
			builder.Append( upper );
		}
		return builder.Length == CodeLength ? builder.ToString() : null;
	}

	/// <summary>
	/// <paramref name="text"/> as a field of at most <paramref name="maximumLength"/> characters a reader takes: control characters become spaces and the end
	/// is cut, never inside a surrogate pair. The server's own texts (a refusal that lists mods or builds) go through it.
	/// </summary>
	public static string Clip( string text, int maximumLength = MaximumReasonLength )
	{
		var clean = text.Any( char.IsControl ) ? new string( text.Select( c => char.IsControl( c ) ? ' ' : c ).ToArray() ) : text;
		if ( clean.Length <= maximumLength )
			return clean;
		var cut = char.IsHighSurrogate( clean[maximumLength - 1] ) ? maximumLength - 1 : maximumLength;
		return clean[..cut];
	}

	public static bool IsSessionId( string? id ) => id is { Length: SessionIdLength } && id.All( c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') );

	public static bool IsSha256( string? hex ) => hex is { Length: 64 } && hex.All( c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') );
}

/// <summary>
/// The WebSocket close statuses of the co-op socket and what a game does after each (<see cref="IsResumable"/>): reconnect and resume, or stop.
/// </summary>
public static class CoopCloseStatus
{
	/// <summary>The account's game took this socket over on another device or process; terminal.</summary>
	public const int Replaced = 4001;
	/// <summary>
	/// This socket read too slowly; resumable. The server usually drops such a socket without a close (its queue is stuck behind it), which a game resumes
	/// after as well.
	/// </summary>
	public const int TooSlow = 4002;
	/// <summary>Another game of the account holds the co-op socket; terminal unless the player takes over.</summary>
	public const int InUse = 4003;
	/// <summary>This socket sent faster than its limits; resumable after <see cref="RateRetryDelay"/>.</summary>
	public const int Rate = 4004;
	/// <summary>A malformed or out-of-place message; terminal.</summary>
	public const int ProtocolError = 4005;
	/// <summary>The server is restarting (the chat's 1012); resumable.</summary>
	public const int Restarting = 1012;

	public static readonly TimeSpan RateRetryDelay = TimeSpan.FromSeconds( 10 );

	/// <summary>
	/// Whether a game reconnects and resumes its session after a close with <paramref name="status"/>: a restart, a going-away, an abort (no close frame), an
	/// internal error, a slow reader and the rate close are; a replacement, a refusal, an authentication or protocol close are not.
	/// </summary>
	public static bool IsResumable( int? status ) => status is null or 1001 or 1006 or 1011 or Restarting or TooSlow or Rate;
}

public enum CoopMessageType : byte
{
	// a game to the server
	Hello = 1,
	Create = 2,
	Join = 3,
	Leave = 4,
	Kick = 5,
	Invite = 6,
	HostFrame = 7,
	Send = 8,
	Pong = 9,
	// the server to a game
	Welcome = 101,
	Created = 102,
	Joined = 103,
	MemberChanged = 104,
	Invited = 105,
	Ended = 106,
	Frame = 107,
	Message = 108,
	Error = 109,
	Ping = 110,
	FrameRefused = 111
}

/// <summary>Why the server refused a request or ended a session (<see cref="CoopError"/>, <see cref="CoopEnded"/>).</summary>
public enum CoopReason : ushort
{
	None = 0,
	HelloFirst = 2,
	ProtocolTooOld = 3,
	InUse = 4,
	InSession = 10,
	NotInSession = 11,
	NotHost = 12,
	NotMember = 13,
	NotConnected = 14,
	NotOnline = 15,
	UnknownCode = 20,
	Full = 21,
	Kicked = 22,
	BuildDiffers = 23,
	ModsDiffer = 24,
	TitleRefused = 25,
	ServerFull = 26,
	Rate = 30,
	Left = 40,
	HostLeft = 41,
	Expired = 42,
	AccountDeleted = 43,
	Unknown = 44
}

public enum CoopMemberState : byte
{
	Joined = 1,
	Connected = 2,
	Disconnected = 3,
	Left = 4,
	Kicked = 5
}

/// <summary>A simulation-changing mod of the game (<c>ParkModRecord</c>): its id, version and content digest. A session's mods must match in load order.</summary>
public sealed record CoopMod( string Id, string Version, string ContentSha256 );

/// <summary>A session a game resumes after a reconnect: the session, its own member id and the last frame it holds.</summary>
public sealed record CoopResume( string SessionId, uint MemberId, ulong LastFrameSeq );

/// <summary>A member of a session: its id (never reused in the session), its slot (the game's player index; the host is 0), name and whether it is connected.</summary>
public sealed record CoopMemberInfo( uint Id, byte Slot, string Name, bool Connected );

/// <summary>
/// A session as a member sees it: its id and code, title, whether it is listed, the most players, the receiver's own member id, the host's member id,
/// the last frame the host relayed, whether that number is exact (<paramref name="SeqKnown"/>), and the members. After a server crash the stored number
/// may be behind the frames the guests hold: then the server takes the host's next frame at any later number, and guests drop the frames they already have.
/// </summary>
public sealed record CoopSessionState( string Id, string Code, string Title, bool Listed, byte MaximumPlayers, uint You, uint Host, ulong LastFrameSeq,
	bool SeqKnown, IReadOnlyList<CoopMemberInfo> Members );

/// <summary>A message of the co-op socket. <see cref="Encode"/> writes its bytes; <see cref="Decode"/> reads one or throws <see cref="InvalidDataException"/>.</summary>
public abstract record CoopMessage
{
	public abstract CoopMessageType Type { get; }

	private protected abstract void WriteFields( CoopWriter writer );

	public byte[] Encode()
	{
		var writer = new CoopWriter();
		writer.Byte( (byte)Type );
		WriteFields( writer );
		return writer.ToArray();
	}

	public static CoopMessage Decode( ReadOnlySpan<byte> bytes )
	{
		if ( bytes.Length > CoopProtocol.MaximumMessageBytes )
			throw new InvalidDataException( "The co-op message is too large." );
		var reader = new CoopReader( bytes );
		var type = (CoopMessageType)reader.Byte();
		CoopMessage message = type switch
		{
			CoopMessageType.Hello => CoopHello.Read( ref reader ),
			CoopMessageType.Create => new CoopCreate( reader.Bool(), reader.Text( CoopProtocol.MaximumTitleLength ), reader.Byte() ),
			CoopMessageType.Join => new CoopJoin( reader.Text( CoopProtocol.MaximumTypedCodeLength ) ),
			CoopMessageType.Leave => new CoopLeave( reader.Text( CoopProtocol.MaximumReasonLength ) ),
			CoopMessageType.Kick => new CoopKick( reader.U32() ),
			CoopMessageType.Invite => new CoopInvite( reader.Text( CoopProtocol.MaximumInviteNameLength ) ),
			CoopMessageType.HostFrame => new CoopHostFrame( reader.U64(), reader.Bytes() ),
			CoopMessageType.Send => new CoopSend( reader.U32(), reader.Bytes() ),
			CoopMessageType.Pong => new CoopPong(),
			CoopMessageType.Welcome => CoopWelcome.Read( ref reader ),
			CoopMessageType.Created => new CoopCreated( reader.Session() ),
			CoopMessageType.Joined => new CoopJoined( reader.Session(), reader.Text( CoopProtocol.MaximumBuildLength ), reader.Mods() ),
			CoopMessageType.MemberChanged => new CoopMemberChanged( reader.Member(), (CoopMemberState)reader.Byte() ),
			CoopMessageType.Invited => new CoopInvited( reader.SessionId(), reader.Text( CoopProtocol.CodeLength ), reader.Text( CoopProtocol.MaximumTitleLength ), reader.Text( OnlineText.MaximumNameLength ) ),
			CoopMessageType.Ended => new CoopEnded( reader.SessionId(), (CoopReason)reader.U16(), reader.Text( CoopProtocol.MaximumReasonLength ) ),
			CoopMessageType.Frame => new CoopFrame( reader.U64(), reader.Bytes() ),
			CoopMessageType.Message => new CoopRelayed( reader.U32(), reader.Bytes() ),
			CoopMessageType.Error => new CoopError( (CoopMessageType)reader.Byte(), (CoopReason)reader.U16(), reader.Text( CoopProtocol.MaximumReasonLength ) ),
			CoopMessageType.Ping => new CoopPing(),
			CoopMessageType.FrameRefused => new CoopFrameRefused( reader.U64() ),
			_ => throw new InvalidDataException( $"Unknown co-op message {(byte)type}." )
		};
		reader.End();
		message.Validate();
		return message;
	}

	/// <summary>Checks the decoded fields beyond their byte form (ranges, enum values, identifiers).</summary>
	private protected virtual void Validate() { }

	private protected static void Require( bool condition, string what )
	{
		if ( !condition )
			throw new InvalidDataException( $"The co-op message has an invalid {what}." );
	}
}

/// <summary>
/// The first message of a connection: the game's protocol, its build (the informational version, exactly the same release for everyone in a session: decision
/// Q11), its simulation-changing mods in load order (Q16), the random id of its process (<paramref name="Instance"/>: a reconnect of the same process replaces
/// its old connection), whether it takes the co-op socket over from another game of the account, and the session it resumes, if any.
/// </summary>
public sealed record CoopHello( ushort Protocol, string Build, IReadOnlyList<CoopMod> Mods, Guid Instance, bool TakeOver, CoopResume? Resume ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Hello;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.U16( Protocol );
		writer.Text( Build, CoopProtocol.MaximumBuildLength );
		writer.Mods( Mods );
		writer.Guid( Instance );
		writer.Bool( TakeOver );
		writer.Bool( Resume != null );
		if ( Resume != null )
		{
			writer.Text( Resume.SessionId, CoopProtocol.SessionIdLength );
			writer.U32( Resume.MemberId );
			writer.U64( Resume.LastFrameSeq );
		}
	}

	internal static CoopHello Read( ref CoopReader reader )
	{
		var protocol = reader.U16();
		var build = reader.Text( CoopProtocol.MaximumBuildLength );
		var mods = reader.Mods();
		var instance = reader.Guid();
		var takeOver = reader.Bool();
		var resume = reader.Bool() ? new CoopResume( reader.SessionId(), reader.U32(), reader.U64() ) : null;
		return new CoopHello( protocol, build, mods, instance, takeOver, resume );
	}

	private protected override void Validate()
	{
		Require( Build.Length > 0, "build" );
		Require( Instance != Guid.Empty, "instance" );
	}
}

/// <summary>Creates a session with the sender as its host: listed on the open list or by code and invitation only (decision Q4), its title and its most players.</summary>
public sealed record CoopCreate( bool Listed, string Title, byte MaximumPlayers ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Create;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.Bool( Listed );
		writer.Text( Title, CoopProtocol.MaximumTitleLength );
		writer.Byte( MaximumPlayers );
	}

	private protected override void Validate() => Require( MaximumPlayers is >= 2 and <= CoopProtocol.MaximumPlayers, "number of players" );
}

/// <summary>Joins the session with <paramref name="Code"/> (as typed: <see cref="CoopProtocol.NormalizeCode"/>).</summary>
public sealed record CoopJoin( string Code ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Join;
	private protected override void WriteFields( CoopWriter writer ) => writer.Text( Code, CoopProtocol.MaximumTypedCodeLength );
}

/// <summary>Leaves the sender's session; a host's leave ends it, and the server tells the others <paramref name="Reason"/>.</summary>
public sealed record CoopLeave( string Reason ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Leave;
	private protected override void WriteFields( CoopWriter writer ) => writer.Text( Reason, CoopProtocol.MaximumReasonLength );
}

/// <summary>The host removes member <paramref name="MemberId"/> from its session; the account cannot join it again.</summary>
public sealed record CoopKick( uint MemberId ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Kick;
	private protected override void WriteFields( CoopWriter writer ) => writer.U32( MemberId );
}

/// <summary>The host invites <paramref name="Player"/>, who must have a co-op socket open.</summary>
public sealed record CoopInvite( string Player ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Invite;
	private protected override void WriteFields( CoopWriter writer ) => writer.Text( Player, CoopProtocol.MaximumInviteNameLength );
}

/// <summary>A frame of the host's command stream (C2b): the server relays it to every other member when <paramref name="Seq"/> follows the last one.</summary>
public sealed record CoopHostFrame( ulong Seq, ReadOnlyMemory<byte> Payload ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.HostFrame;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.U64( Seq );
		writer.Bytes( Payload.Span );
	}

	private protected override void Validate() => Require( Seq > 0, "frame number" );
}

/// <summary>
/// A message to member <paramref name="To"/> (<see cref="CoopProtocol.EveryOtherMember"/>: every other member, the host's only); a guest sends to the host.
/// At most once: a message to a member who is not connected is refused, and one in a closed connection's queue is lost.
/// </summary>
public sealed record CoopSend( uint To, ReadOnlyMemory<byte> Payload ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Send;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.U32( To );
		writer.Bytes( Payload.Span );
	}
}

/// <summary>Answers <see cref="CoopPing"/>.</summary>
public sealed record CoopPong : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Pong;
	private protected override void WriteFields( CoopWriter writer ) { }
}

/// <summary>
/// The answer to <see cref="CoopHello"/>: the server's protocol; the session the account belongs to (resumed when the hello asked for it, else still to
/// leave), or why the session the hello resumes ended while the game was away.
/// </summary>
public sealed record CoopWelcome( ushort Protocol, CoopSessionState? Session, CoopEnded? EndedWhileAway ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Welcome;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.U16( Protocol );
		writer.Bool( Session != null );
		if ( Session != null )
			writer.Session( Session );
		writer.Bool( EndedWhileAway != null );
		if ( EndedWhileAway != null )
		{
			writer.Text( EndedWhileAway.SessionId, CoopProtocol.SessionIdLength );
			writer.U16( (ushort)EndedWhileAway.Reason );
			writer.Text( EndedWhileAway.Text, CoopProtocol.MaximumReasonLength );
		}
	}

	internal static CoopWelcome Read( ref CoopReader reader )
	{
		var protocol = reader.U16();
		var session = reader.Bool() ? reader.Session() : null;
		var ended = reader.Bool() ? new CoopEnded( reader.SessionId(), (CoopReason)reader.U16(), reader.Text( CoopProtocol.MaximumReasonLength ) ) : null;
		return new CoopWelcome( protocol, session, ended );
	}
}

/// <summary>The sender's session was created; it is its host.</summary>
public sealed record CoopCreated( CoopSessionState Session ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Created;
	private protected override void WriteFields( CoopWriter writer ) => writer.Session( Session );
}

/// <summary>The sender joined a session, whose host runs <paramref name="HostBuild"/> with <paramref name="HostMods"/>.</summary>
public sealed record CoopJoined( CoopSessionState Session, string HostBuild, IReadOnlyList<CoopMod> HostMods ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Joined;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.Session( Session );
		writer.Text( HostBuild, CoopProtocol.MaximumBuildLength );
		writer.Mods( HostMods );
	}
}

/// <summary>A member of the receiver's session joined, connected, disconnected, left or was kicked.</summary>
public sealed record CoopMemberChanged( CoopMemberInfo Member, CoopMemberState State ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.MemberChanged;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.Member( Member );
		writer.Byte( (byte)State );
	}

	private protected override void Validate() => Require( State is >= CoopMemberState.Joined and <= CoopMemberState.Kicked, "member state" );
}

/// <summary>The receiver is invited to a session by its host.</summary>
public sealed record CoopInvited( string SessionId, string Code, string Title, string Host ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Invited;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.Text( SessionId, CoopProtocol.SessionIdLength );
		writer.Text( Code, CoopProtocol.CodeLength );
		writer.Text( Title, CoopProtocol.MaximumTitleLength );
		writer.Text( Host, OnlineText.MaximumNameLength );
	}
}

/// <summary>The receiver is no longer in session <paramref name="SessionId"/>: it left, was kicked, or the session ended.</summary>
public sealed record CoopEnded( string SessionId, CoopReason Reason, string Text ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Ended;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.Text( SessionId, CoopProtocol.SessionIdLength );
		writer.U16( (ushort)Reason );
		writer.Text( Text, CoopProtocol.MaximumReasonLength );
	}
}

/// <summary>A frame of the host's command stream, relayed.</summary>
public sealed record CoopFrame( ulong Seq, ReadOnlyMemory<byte> Payload ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Frame;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.U64( Seq );
		writer.Bytes( Payload.Span );
	}
}

/// <summary>A message from member <paramref name="From"/> (<see cref="CoopSend"/>).</summary>
public sealed record CoopRelayed( uint From, ReadOnlyMemory<byte> Payload ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Message;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.U32( From );
		writer.Bytes( Payload.Span );
	}
}

/// <summary>The server refused the sender's <paramref name="Request"/> for <paramref name="Reason"/>; <paramref name="Text"/> names the difference where there is one.</summary>
public sealed record CoopError( CoopMessageType Request, CoopReason Reason, string Text ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Error;

	private protected override void WriteFields( CoopWriter writer )
	{
		writer.Byte( (byte)Request );
		writer.U16( (ushort)Reason );
		writer.Text( Text, CoopProtocol.MaximumReasonLength );
	}
}

/// <summary>The server has sent nothing for a while; the game answers with <see cref="CoopPong"/>. A game that hears nothing for 45 seconds reconnects.</summary>
public sealed record CoopPing : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.Ping;
	private protected override void WriteFields( CoopWriter writer ) { }
}

/// <summary>
/// The host's frame was not the next one: the server dropped it (no guest sees it) and takes frame <paramref name="Expected"/> next, or any later one while
/// the session's frame number is not exact (<see cref="CoopSessionState.SeqKnown"/>). The host resends from there.
/// </summary>
public sealed record CoopFrameRefused( ulong Expected ) : CoopMessage
{
	public override CoopMessageType Type => CoopMessageType.FrameRefused;
	private protected override void WriteFields( CoopWriter writer ) => writer.U64( Expected );
	private protected override void Validate() => Require( Expected > 0, "frame number" );
}

/// <summary>Writes the fields of a co-op message.</summary>
public sealed class CoopWriter
{
	private byte[] buffer = new byte[64];
	private int length;

	public byte[] ToArray() => buffer.AsSpan( 0, length ).ToArray();

	private Span<byte> Take( int count )
	{
		if ( length + count > buffer.Length )
			Array.Resize( ref buffer, Math.Max( buffer.Length * 2, length + count ) );
		var span = buffer.AsSpan( length, count );
		length += count;
		return span;
	}

	public void Byte( byte value ) => Take( 1 )[0] = value;
	public void Bool( bool value ) => Byte( value ? (byte)1 : (byte)0 );
	public void U16( ushort value ) => BinaryPrimitives.WriteUInt16LittleEndian( Take( 2 ), value );
	public void U32( uint value ) => BinaryPrimitives.WriteUInt32LittleEndian( Take( 4 ), value );
	public void U64( ulong value ) => BinaryPrimitives.WriteUInt64LittleEndian( Take( 8 ), value );
	public void Guid( Guid value ) => value.TryWriteBytes( Take( 16 ) );

	/// <summary>
	/// A field of at most <paramref name="maximumLength"/> characters without control characters, the same limits <see cref="CoopReader.Text"/> keeps, so
	/// nothing is written that a reader refuses (<see cref="CoopProtocol.Clip"/> fits a text the server makes).
	/// </summary>
	public void Text( string value, int maximumLength )
	{
		if ( value.Length > maximumLength || value.Any( char.IsControl ) )
			throw new ArgumentException( $"The text is over {maximumLength} characters or holds a control character.", nameof( value ) );
		var count = Encoding.UTF8.GetByteCount( value );
		U16( checked( (ushort)count ) );
		Encoding.UTF8.GetBytes( value, Take( count ) );
	}

	public void Bytes( ReadOnlySpan<byte> value )
	{
		U32( (uint)value.Length );
		value.CopyTo( Take( value.Length ) );
	}

	public void Mods( IReadOnlyList<CoopMod> mods )
	{
		if ( mods.Count > CoopProtocol.MaximumMods )
			throw new ArgumentException( $"A co-op message lists at most {CoopProtocol.MaximumMods} mods.", nameof( mods ) );
		U16( (ushort)mods.Count );
		foreach ( var mod in mods )
		{
			Text( mod.Id, CoopProtocol.MaximumModIdLength );
			Text( mod.Version, CoopProtocol.MaximumModVersionLength );
			Text( mod.ContentSha256, 64 );
		}
	}

	public void Member( CoopMemberInfo member )
	{
		U32( member.Id );
		Byte( member.Slot );
		Text( member.Name, OnlineText.MaximumNameLength );
		Bool( member.Connected );
	}

	public void Session( CoopSessionState session )
	{
		Text( session.Id, CoopProtocol.SessionIdLength );
		Text( session.Code, CoopProtocol.CodeLength );
		Text( session.Title, CoopProtocol.MaximumTitleLength );
		Bool( session.Listed );
		Byte( session.MaximumPlayers );
		U32( session.You );
		U32( session.Host );
		U64( session.LastFrameSeq );
		Bool( session.SeqKnown );
		Byte( checked( (byte)session.Members.Count ) );
		foreach ( var member in session.Members )
			Member( member );
	}
}

/// <summary>Reads the fields of a co-op message; every shortfall, overrun, invalid string or out-of-range count throws <see cref="InvalidDataException"/>.</summary>
public ref struct CoopReader
{
	private static readonly UTF8Encoding StrictUtf8 = new( false, true );
	private readonly ReadOnlySpan<byte> bytes;
	private int position;

	public CoopReader( ReadOnlySpan<byte> bytes )
	{
		this.bytes = bytes;
		position = 0;
	}

	private ReadOnlySpan<byte> Take( int count )
	{
		if ( count < 0 || position + count > bytes.Length )
			throw new InvalidDataException( "The co-op message ends early." );
		var span = bytes.Slice( position, count );
		position += count;
		return span;
	}

	public byte Byte() => Take( 1 )[0];

	public bool Bool() => Byte() switch
	{
		0 => false,
		1 => true,
		_ => throw new InvalidDataException( "The co-op message has an invalid flag." )
	};

	public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian( Take( 2 ) );
	public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian( Take( 4 ) );
	public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian( Take( 8 ) );
	public Guid Guid() => new( Take( 16 ) );

	/// <summary>A UTF-8 string of at most <paramref name="maximumLength"/> characters, without control characters.</summary>
	public string Text( int maximumLength )
	{
		var count = U16();
		string text;
		try
		{
			text = StrictUtf8.GetString( Take( count ) );
		}
		catch ( DecoderFallbackException )
		{
			throw new InvalidDataException( "The co-op message has invalid text." );
		}
		if ( text.Length > maximumLength || text.Any( char.IsControl ) )
			throw new InvalidDataException( "The co-op message has invalid text." );
		return text;
	}

	public byte[] Bytes()
	{
		var count = U32();
		if ( count > CoopProtocol.MaximumMessageBytes )
			throw new InvalidDataException( "The co-op message's payload is too large." );
		return Take( (int)count ).ToArray();
	}

	public string SessionId()
	{
		var id = Text( CoopProtocol.SessionIdLength );
		if ( !CoopProtocol.IsSessionId( id ) )
			throw new InvalidDataException( "The co-op message has an invalid session id." );
		return id;
	}

	public IReadOnlyList<CoopMod> Mods()
	{
		var count = U16();
		if ( count > CoopProtocol.MaximumMods )
			throw new InvalidDataException( "The co-op message lists too many mods." );
		var mods = new CoopMod[count];
		for ( var index = 0; index < count; index++ )
		{
			var id = Text( CoopProtocol.MaximumModIdLength );
			var version = Text( CoopProtocol.MaximumModVersionLength );
			var sha = Text( 64 );
			if ( id.Length == 0 || !CoopProtocol.IsSha256( sha ) )
				throw new InvalidDataException( "The co-op message has an invalid mod." );
			mods[index] = new CoopMod( id, version, sha );
		}
		return mods;
	}

	public CoopMemberInfo Member()
	{
		var id = U32();
		var slot = Byte();
		var name = Text( OnlineText.MaximumNameLength );
		var connected = Bool();
		if ( id == 0 || slot >= CoopProtocol.MaximumPlayers )
			throw new InvalidDataException( "The co-op message has an invalid member." );
		return new CoopMemberInfo( id, slot, name, connected );
	}

	public CoopSessionState Session()
	{
		var id = SessionId();
		var code = Text( CoopProtocol.CodeLength );
		var title = Text( CoopProtocol.MaximumTitleLength );
		var listed = Bool();
		var maximum = Byte();
		var you = U32();
		var host = U32();
		var seq = U64();
		var known = Bool();
		var count = Byte();
		if ( CoopProtocol.NormalizeCode( code ) != code || maximum is < 2 or > CoopProtocol.MaximumPlayers || count > maximum )
			throw new InvalidDataException( "The co-op message has an invalid session." );
		var members = new CoopMemberInfo[count];
		for ( var index = 0; index < count; index++ )
			members[index] = Member();
		return new CoopSessionState( id, code, title, listed, maximum, you, host, seq, known, members );
	}

	/// <summary>Requires the whole message to have been read.</summary>
	public void End()
	{
		if ( position != bytes.Length )
			throw new InvalidDataException( "The co-op message has bytes after its fields." );
	}
}
