using System.Text.Json.Serialization;
using OpenTPW.Online.Packages;

namespace OpenTPW.Online.Chat;

/// <summary>
/// [EXT:ONLINE-031] OpenTPW chat wire protocol: one JSON object per WebSocket text frame. The original
/// used EA's chat server (weachatr.dll, port 7593) whose protocol is unknown and is not reproduced.
/// </summary>
public static class ChatProtocol
{
	public const string Path = "/api/v1/chat";
	public const int MaximumFrameBytes = 4096;
	/// <summary>Most arguments one chat event may carry.</summary>
	public const int MaximumArguments = 256;
	public const string LobbyRoom = "lobby";
	public const string ParkRoomPrefix = "park:";
	/// <summary>Rooms of the online world's chat satellites ("chat parks").</summary>
	public const string SatelliteRoomPrefix = "satellite:";
	/// <summary>[EXT:online-coop-session] The room of a shared park's session: its members' chat and figures (docs/ONLINE.md, "Shared parks").</summary>
	public const string CoopRoomPrefix = "coop:";
	// [BIN:STP-PPC:0x1b8808 chatellite connector] the online world's globe holds five chat satellites (Mac layout
	// table data:0x523f0, controls 90120-90124); each shows the player count of one chat park's room
	public const int SatelliteRoomCount = 5;
	/// <summary>WebSocket close status "Service Restart": the server is being updated; clients reconnect.</summary>
	public const int RestartingCloseStatus = 1012;
	/// <summary>
	/// The server protocol version from which movements carry a <see cref="ChatMoveMode"/>: a client sends a mode only to such a server, and the server
	/// sends one only to clients that sent one, as the strict members of older clients and servers refuse an unknown <c>mode</c>.
	/// </summary>
	public const int MoveModesVersion = 5;
	/// <summary>[EXT:online-coop-session] The server protocol from which a shared park's session has a chat room of its own (<see cref="CoopRoom"/>).</summary>
	public const int CoopRoomsVersion = 8;

	public static string ParkRoom( string parkId ) => ParkRoomPrefix + parkId;

	/// <summary>[EXT:online-coop-session] The room of the shared park's session <paramref name="sessionId"/>.</summary>
	public static string CoopRoom( string sessionId ) => CoopRoomPrefix + sessionId;

	/// <summary>[EXT:online-coop-session] The session a shared park's room belongs to, or null for any other room.</summary>
	public static string? CoopSessionOf( string? room ) =>
		room != null && room.StartsWith( CoopRoomPrefix, StringComparison.Ordinal ) && Coop.CoopProtocol.IsSessionId( room[CoopRoomPrefix.Length..] ) ? room[CoopRoomPrefix.Length..] : null;

	/// <summary>The room of chat satellite <paramref name="index"/> (1-<see cref="SatelliteRoomCount"/>).</summary>
	public static string SatelliteRoom( int index ) =>
		SatelliteRoomPrefix + index.ToString( System.Globalization.CultureInfo.InvariantCulture );

	/// <summary>The satellite a room belongs to (1-<see cref="SatelliteRoomCount"/>), or null.</summary>
	public static int? SatelliteIndexOf( string? room ) =>
		room != null && int.TryParse( room.Length > SatelliteRoomPrefix.Length ? room[SatelliteRoomPrefix.Length..] : null,
			System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index )
		&& index >= 1 && index <= SatelliteRoomCount && room == SatelliteRoom( index ) ? index : null;

	public static bool IsValidRoom( string? room ) => room == LobbyRoom
		|| room != null && room.StartsWith( ParkRoomPrefix, StringComparison.Ordinal ) && room.Length is > 5 and <= 69 && room[5..].All( char.IsAsciiLetterOrDigit )
		|| SatelliteIndexOf( room ) != null
		|| CoopSessionOf( room ) != null;
}

/// <summary>
/// First frame of a chat connection opened without an <c>Authorization</c> header: browsers cannot set
/// WebSocket headers, and a token in the URL would end up in logs. The server reads it before anything else.
/// </summary>
public sealed record ChatAuthentication( string Type, string Token )
{
	public const string AuthenticationType = "auth";
	/// <summary>Largest first frame the server reads; a session token is far smaller.</summary>
	public const int MaximumBytes = 512;
	/// <summary>How long the server waits for the first frame.</summary>
	public static readonly TimeSpan Timeout = TimeSpan.FromSeconds( 10 );

	public static ChatAuthentication For( string token ) => new( AuthenticationType, token );

	public static string ParseToken( ReadOnlySpan<byte> utf8 )
	{
		var authentication = StrictJson.Deserialize<ChatAuthentication>( utf8, "Chat authentication" );
		if ( authentication.Type != AuthenticationType || string.IsNullOrEmpty( authentication.Token ) )
			throw new InvalidDataException( "Expected chat authentication." );
		return authentication.Token;
	}
}

/// <summary>
/// How a movement moves a player's figure: the original's <c>BOSS_CHAT_MOVE</c> modes, the message's z (tp.exe 0x4E7265 in the dispatcher 0x4E6E80).
/// </summary>
// [BIN:TPW-EXE:0x4E7265 BOSS_CHAT_MOVE] z 0 walks the online person to the place (0x4F7DE0), 1 teleports it there (0x4F7E20), 2 hides it (0x4F9ED0) and 3 unhides and teleports it
public enum ChatMoveMode
{
	/// <summary>The figure walks to the place.</summary>
	Walk,
	/// <summary>The figure is at the place at once (a double click, a goto).</summary>
	Teleport,
	/// <summary>The figure is hidden: its player rides a ride.</summary>
	Hide,
	/// <summary>The hidden figure is shown again, at the place: the ride ended.</summary>
	Unhide,
}

/// <summary>Client to server: <c>command</c> (canonical command + argument), <c>join</c> (room) or <c>move</c> (position, and from protocol 5 its mode).</summary>
public sealed record ChatRequest( string Type, string? Command, string? Argument, string? Room,
	float? X = null, float? Y = null,
	// written only when set: a server before protocol 5 refuses the member
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] ChatMoveMode? Mode = null )
{
	public const string CommandType = "command";
	public const string JoinType = "join";
	/// <summary>Moving the player's avatar in a chat park: a position in the park, both axes 0 to 1.</summary>
	public const string MoveType = "move";

	public static ChatRequest ForCommand( ChatCommand command, string argument ) => new( CommandType, ChatCommands.CanonicalName( command ), argument, null );
	public static ChatRequest ForJoin( string room ) => new( JoinType, null, null, room );
	/// <summary>A movement; <paramref name="mode"/> only for a server of <see cref="ChatProtocol.MoveModesVersion"/> or later.</summary>
	public static ChatRequest ForMove( float x, float y, ChatMoveMode? mode = null ) => new( MoveType, null, null, null, x, y, mode );

	public static ChatRequest Parse( ReadOnlySpan<byte> utf8 )
	{
		var request = StrictJson.Deserialize<ChatRequest>( utf8, "Chat request" );
		if ( request.Mode != null && request.Type != MoveType )
			throw new InvalidDataException( "Invalid chat movement mode." );
		if ( request.Type == CommandType )
		{
			if ( request.Command == null || !ChatCommands.TryParseCanonical( request.Command, out _ ) || request.Room != null )
				throw new InvalidDataException( "Unknown chat command." );
			OnlineText.RequireText( request.Argument ?? "", ChatCommands.MaximumLineLength, false, true, "Chat text" );
		}
		else if ( request.Type == JoinType )
		{
			if ( !ChatProtocol.IsValidRoom( request.Room ) || request.Command != null || request.Argument != null )
				throw new InvalidDataException( "Invalid chat room." );
		}
		else if ( request.Type == MoveType )
		{
			if ( !IsPosition( request.X ) || !IsPosition( request.Y ) || request.Command != null || request.Argument != null || request.Room != null )
				throw new InvalidDataException( "Invalid chat position." );
		}
		else
			throw new InvalidDataException( "Unknown chat request type." );
		return request;
	}

	/// <summary>A finite position on one axis of a chat park (0 to 1).</summary>
	internal static bool IsPosition( float? value ) => value is { } position && float.IsFinite( position ) && position is >= 0f and <= 1f;
}

public enum ChatEventKind
{
	/// <summary>Spoken in the current room ("say").</summary>
	Say,
	/// <summary>Private message ("tell"/"reply").</summary>
	Tell,
	/// <summary>Shout to the current room ("shout").</summary>
	Shout,
	/// <summary>Shout to every room ("wshout"/"ushout").</summary>
	WorldShout,
	/// <summary>Emote text or a mood emote (<see cref="ChatEvent.Command"/> set).</summary>
	Emote,
	/// <summary>Page from another player.</summary>
	Page,
	/// <summary>Original response string (<see cref="ChatEvent.Notice"/> + <see cref="ChatEvent.Args"/>).</summary>
	Notice,
	/// <summary>Server text without an original string (lists, operator notices).</summary>
	System,
	/// <summary>Room membership: <see cref="ChatEvent.Room"/> joined, <see cref="ChatEvent.Args"/> players.</summary>
	Room,
	/// <summary>A player moved in a chat park: <see cref="ChatEvent.From"/>, <see cref="ChatEvent.Room"/>, X and Y, and for a client that sends modes the <see cref="ChatEvent.Mode"/>.</summary>
	Move,
}

/// <summary>Server to client event.</summary>
public sealed record ChatEvent(
	ChatEventKind Kind,
	string? From = null,
	string? Room = null,
	string? Text = null,
	string? Command = null,
	int? Notice = null,
	IReadOnlyList<string>? Args = null,
	float? X = null,
	float? Y = null,
	// written only when set, and set only for a client that sent modes itself: an older client refuses the member
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] ChatMoveMode? Mode = null )
{
	public static ChatEvent ForNotice( ChatNotice notice, params string[] args ) => new( ChatEventKind.Notice, Notice: (int)notice, Args: args );

	public static ChatEvent Parse( ReadOnlySpan<byte> utf8 )
	{
		var value = StrictJson.Deserialize<ChatEvent>( utf8, "Chat event" );
		if ( value.Notice is < 0 or > 1000 || value.Args?.Count > ChatProtocol.MaximumArguments || value.Args?.Any( arg => arg == null ) == true
			|| (value.Kind == ChatEventKind.Move && (!ChatRequest.IsPosition( value.X ) || !ChatRequest.IsPosition( value.Y ) || value.From == null))
			|| (value.Mode != null && value.Kind != ChatEventKind.Move) )
			throw new InvalidDataException( "Chat event is out of range." );
		return value;
	}
}
