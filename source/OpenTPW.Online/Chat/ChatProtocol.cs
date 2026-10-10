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
	public const string LobbyRoom = "lobby";
	public const string ParkRoomPrefix = "park:";
	/// <summary>WebSocket close status "Service Restart": the server is being updated; clients reconnect.</summary>
	public const int RestartingCloseStatus = 1012;

	public static string ParkRoom( string parkId ) => ParkRoomPrefix + parkId;

	public static bool IsValidRoom( string? room ) => room == LobbyRoom
		|| room != null && room.StartsWith( ParkRoomPrefix, StringComparison.Ordinal ) && room.Length is > 5 and <= 69 && room[5..].All( char.IsAsciiLetterOrDigit );
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

/// <summary>Client to server: <c>command</c> (canonical command + argument) or <c>join</c> (room).</summary>
public sealed record ChatRequest( string Type, string? Command, string? Argument, string? Room )
{
	public const string CommandType = "command";
	public const string JoinType = "join";

	public static ChatRequest ForCommand( ChatCommand command, string argument ) => new( CommandType, ChatCommands.CanonicalName( command ), argument, null );
	public static ChatRequest ForJoin( string room ) => new( JoinType, null, null, room );

	public static ChatRequest Parse( ReadOnlySpan<byte> utf8 )
	{
		var request = StrictJson.Deserialize<ChatRequest>( utf8, "Chat request" );
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
		else
			throw new InvalidDataException( "Unknown chat request type." );
		return request;
	}
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
}

/// <summary>Server to client event.</summary>
public sealed record ChatEvent(
	ChatEventKind Kind,
	string? From = null,
	string? Room = null,
	string? Text = null,
	string? Command = null,
	int? Notice = null,
	IReadOnlyList<string>? Args = null )
{
	public static ChatEvent ForNotice( ChatNotice notice, params string[] args ) => new( ChatEventKind.Notice, Notice: (int)notice, Args: args );

	public static ChatEvent Parse( ReadOnlySpan<byte> utf8 )
	{
		var value = StrictJson.Deserialize<ChatEvent>( utf8, "Chat event" );
		if ( value.Notice is < 0 or > 1000 || value.Args?.Count > 256 )
			throw new InvalidDataException( "Chat event is out of range." );
		return value;
	}
}
