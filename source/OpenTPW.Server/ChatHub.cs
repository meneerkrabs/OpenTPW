using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Chat;
using OpenTPW.Online.Moderation;
using OpenTPW.Online.Packages;

namespace OpenTPW.Server;

/// <summary>Keys of <see cref="ChatEventKind.System"/> events (clients show their own text for them).</summary>
public static class ChatSystemKeys
{
	public const string ServerMessage = "server-message";
	public const string RateLimited = "rate-limited";
	public const string BuddyList = "buddy-list";
}

/// <summary>
/// Chat rooms over WebSockets: the lobby ("Online World") and one room per published park.
/// Commands follow the original CHAT_COMMANDS.str list; responses are CHAT_COMMANDS.str indices.
/// [APPROX:ONLINE-005] Command semantics are inferred from the command words, response strings and
/// the weachatr.dll export names (Say, Tell, ParkShout, Page, Locate, Goto, AFK, Earmuffs, Ignore,
/// Blackmark, AddBuddy/RemoveBuddy, SetHearingRange) — evidence needed: the original chat server
/// behaviour. Notably: say/emote reach the whole room (no positions, so "hearing" has no effect),
/// shout/eshout reach the room, wshout/ushout every room, "mark" (blackmark) files a moderation
/// report instead of muting automatically.
/// </summary>
public sealed class ChatHub
{
	private readonly ServerStore store;
	private readonly ServerOptions options;
	private readonly WordFilter filter;
	private readonly ConcurrentDictionary<string, ChatSession> sessions = new();

	public ChatHub( ServerStore store, ServerOptions options, WordFilter filter )
	{
		this.store = store;
		this.options = options;
		this.filter = filter;
	}

	public int PlayersIn( string room ) => sessions.Values.Count( session => session.Room == room );

	public int ConnectedCount => sessions.Count;

	private sealed class ChatSession
	{
		public ChatSession( AccountRecord account, ServerOptions options )
		{
			Account = account;
			Limiter = new TokenBucketRateLimiter( new TokenBucketRateLimiterOptions
			{
				TokenLimit = options.ChatBurst,
				TokensPerPeriod = options.ChatMessagesPerSecond,
				ReplenishmentPeriod = TimeSpan.FromSeconds( 1 ),
				QueueLimit = 0,
				AutoReplenishment = true,
			} );
		}

		public AccountRecord Account { get; }
		public string Name => Account.Name;
		public string Key => Account.Key;
		public volatile string Room = ChatProtocol.LobbyRoom;
		public string? LastToldTo;
		public string? LastToldBy;
		public bool Earmuffs;
		public bool Afk;
		public bool Filter = true;
		public readonly ConcurrentDictionary<string, byte> Ignored = new();
		public readonly Channel<ChatEvent> Outbox = Channel.CreateBounded<ChatEvent>( new BoundedChannelOptions( 256 ) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true } );
		public TokenBucketRateLimiter Limiter { get; }
		/// <summary>Cancelled to end the connection from the server side (account deleted).</summary>
		public readonly CancellationTokenSource Closed = new();
	}

	/// <summary>Ends a player's chat connection, if any (their account was deleted).</summary>
	public void Disconnect( string accountKey )
	{
		if ( sessions.TryGetValue( accountKey, out var session ) )
			session.Closed.Cancel();
	}

	public async Task RunAsync( WebSocket socket, AccountRecord account, CancellationToken cancel )
	{
		var session = new ChatSession( account, options );
		if ( sessions.Count >= options.MaximumChatConnections || !sessions.TryAdd( account.Key, session ) )
		{
			var notice = sessions.ContainsKey( account.Key ) ? ChatNotice.NameAlreadyOnline : ChatNotice.ParkFull;
			await socket.SendAsync( StrictJson.Serialize( ChatEvent.ForNotice( notice, account.Name ) ), WebSocketMessageType.Text, true, cancel );
			await socket.CloseAsync( WebSocketCloseStatus.PolicyViolation, "already connected", cancel );
			return;
		}
		using var linked = CancellationTokenSource.CreateLinkedTokenSource( cancel, session.Closed.Token );
		cancel = linked.Token;
		var writer = Task.Run( () => WriteLoopAsync( socket, session, cancel ), cancel );
		try
		{
			Send( session, ChatEvent.ForNotice( ChatNotice.WelcomeThemeParkWorld ) );
			if ( !string.IsNullOrWhiteSpace( options.Message ) )
				Send( session, new ChatEvent( ChatEventKind.System, Text: ChatSystemKeys.ServerMessage, Args: new[] { options.Message } ) );
			Send( session, RoomEvent( session.Room ) );
			await ReadLoopAsync( socket, session, cancel );
		}
		catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException or InvalidDataException )
		{
		}
		finally
		{
			sessions.TryRemove( new KeyValuePair<string, ChatSession>( account.Key, session ) );
			session.Outbox.Writer.TryComplete();
			try { await writer; } catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException ) { }
			session.Limiter.Dispose();
			session.Closed.Dispose();
			if ( socket.State == WebSocketState.Open )
			{
				try { await socket.CloseAsync( WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None ); }
				catch ( WebSocketException ) { }
			}
		}
	}

	private static async Task WriteLoopAsync( WebSocket socket, ChatSession session, CancellationToken cancel )
	{
		await foreach ( var item in session.Outbox.Reader.ReadAllAsync( cancel ) )
		{
			if ( socket.State != WebSocketState.Open )
				return;
			await socket.SendAsync( StrictJson.Serialize( item ), WebSocketMessageType.Text, true, cancel );
		}
	}

	private async Task ReadLoopAsync( WebSocket socket, ChatSession session, CancellationToken cancel )
	{
		var buffer = new byte[ChatProtocol.MaximumFrameBytes];
		var invalid = 0;
		while ( socket.State == WebSocketState.Open && !cancel.IsCancellationRequested )
		{
			var length = 0;
			bool endOfMessage;
			do
			{
				if ( length == buffer.Length )
				{
					await socket.CloseAsync( WebSocketCloseStatus.MessageTooBig, "frame too large", cancel );
					return;
				}
				var result = await socket.ReceiveAsync( buffer.AsMemory( length ), cancel );
				if ( result.MessageType == WebSocketMessageType.Close )
					return;
				if ( result.MessageType != WebSocketMessageType.Text )
				{
					await socket.CloseAsync( WebSocketCloseStatus.InvalidMessageType, "text only", cancel );
					return;
				}
				length += result.Count;
				endOfMessage = result.EndOfMessage;
			} while ( !endOfMessage );

			ChatRequest request;
			try
			{
				request = ChatRequest.Parse( buffer.AsSpan( 0, length ) );
			}
			catch ( InvalidDataException )
			{
				Send( session, ChatEvent.ForNotice( ChatNotice.NoSuchCommand ) );
				if ( ++invalid >= 20 )
				{
					await socket.CloseAsync( WebSocketCloseStatus.PolicyViolation, "too many invalid requests", cancel );
					return;
				}
				continue;
			}
			using var lease = session.Limiter.AttemptAcquire();
			if ( !lease.IsAcquired )
			{
				Send( session, new ChatEvent( ChatEventKind.System, Text: ChatSystemKeys.RateLimited ) );
				continue;
			}
			Handle( session, request );
		}
	}

	private void Handle( ChatSession session, ChatRequest request )
	{
		if ( request.Type == ChatRequest.JoinType )
		{
			var room = request.Room!;
			if ( room != ChatProtocol.LobbyRoom && store.FindPark( room[ChatProtocol.ParkRoomPrefix.Length..] ) == null )
			{
				Send( session, ChatEvent.ForNotice( ChatNotice.NoPark ) );
				return;
			}
			session.Room = room;
			Send( session, RoomEvent( room ) );
			return;
		}
		ChatCommands.TryParseCanonical( request.Command!, out var command );
		Execute( session, command, request.Argument ?? "" );
	}

	private ChatEvent RoomEvent( string room ) => new( ChatEventKind.Room, Room: room,
		Args: sessions.Values.Where( other => other.Room == room ).Select( other => other.Name ).OrderBy( name => name, StringComparer.OrdinalIgnoreCase ).ToArray() );

	private void Execute( ChatSession session, ChatCommand command, string argument )
	{
		var speaks = command is ChatCommand.Say or ChatCommand.Tell or ChatCommand.Reply or ChatCommand.Shout or ChatCommand.Emote or ChatCommand.ETell
			or ChatCommand.EShout or ChatCommand.WShout or ChatCommand.UShout or ChatCommand.Page || ChatCommands.IsMoodEmote( command );
		if ( speaks && store.IsMuted( session.Name ) )
		{
			Send( session, ChatEvent.ForNotice( ChatNotice.Muted ) );
			return;
		}
		var from = session.Name;
		switch ( command )
		{
			case ChatCommand.Say:
				if ( argument.Length > 0 )
					Broadcast( session.Room, new ChatEvent( ChatEventKind.Say, from, session.Room, argument ) );
				break;
			case ChatCommand.Emote:
				if ( argument.Length > 0 )
					Broadcast( session.Room, new ChatEvent( ChatEventKind.Emote, from, session.Room, argument ) );
				break;
			case ChatCommand.Shout:
			case ChatCommand.EShout:
			case ChatCommand.WShout:
			case ChatCommand.UShout:
				if ( session.Earmuffs )
				{
					Send( session, ChatEvent.ForNotice( ChatNotice.CannotShoutWithEarmuffs ) );
					break;
				}
				if ( argument.Length == 0 )
					break;
				var world = command is ChatCommand.WShout or ChatCommand.UShout;
				var shout = new ChatEvent( command == ChatCommand.EShout ? ChatEventKind.Emote : world ? ChatEventKind.WorldShout : ChatEventKind.Shout, from, session.Room, argument,
					Command: ChatCommands.CanonicalName( command ) );
				foreach ( var other in sessions.Values.Where( other => (world || other.Room == session.Room) && !other.Earmuffs ) )
					Deliver( other, shout );
				break;
			case ChatCommand.Tell:
			case ChatCommand.ETell:
			{
				var (name, text) = ChatCommands.SplitName( argument );
				if ( name == null )
				{
					Send( session, ChatEvent.ForNotice( ChatNotice.NeedName ) );
					break;
				}
				Tell( session, name, text, command == ChatCommand.ETell );
				break;
			}
			case ChatCommand.Reply:
				if ( session.LastToldBy == null )
					Send( session, ChatEvent.ForNotice( ChatNotice.NeedName ) );
				else
					Tell( session, session.LastToldBy, argument, false );
				break;
			case ChatCommand.Last:
				Send( session, ChatEvent.ForNotice( ChatNotice.LastToldTo, session.LastToldTo ?? "" ) );
				Send( session, ChatEvent.ForNotice( ChatNotice.LastToldBy, session.LastToldBy ?? "" ) );
				break;
			case ChatCommand.Hearing:
				// [EXT:ONLINE-053] No avatar positions exist, so the range is acknowledged but has no effect.
				if ( !int.TryParse( argument, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var range ) || range > 1000 )
					Send( session, ChatEvent.ForNotice( ChatNotice.BadParameter ) );
				else
					Send( session, ChatEvent.ForNotice( ChatNotice.HearingRangeSet, range.ToString( System.Globalization.CultureInfo.InvariantCulture ) ) );
				break;
			case ChatCommand.Help:
				Send( session, ChatEvent.ForNotice( ChatNotice.Commands, ChatCommands.CanonicalNames.ToArray() ) );
				break;
			case ChatCommand.Earmuffs:
				session.Earmuffs = !session.Earmuffs;
				Send( session, ChatEvent.ForNotice( session.Earmuffs ? ChatNotice.EarmuffsOn : ChatNotice.EarmuffsOff ) );
				break;
			case ChatCommand.Ignore:
			{
				var (name, _) = ChatCommands.SplitName( argument );
				if ( name == null )
				{
					Send( session, ChatEvent.ForNotice( ChatNotice.WhoToIgnore ) );
					break;
				}
				var target = store.FindAccount( name );
				if ( target == null )
					Send( session, ChatEvent.ForNotice( ChatNotice.NoSuchPlayer, name ) );
				else if ( target.Key == session.Key )
					Send( session, ChatEvent.ForNotice( ChatNotice.CouldNotAddIgnore, target.Name ) );
				else if ( session.Ignored.TryRemove( target.Key, out _ ) )
					Send( session, ChatEvent.ForNotice( ChatNotice.YouNoLongerIgnore, target.Name ) );
				else
				{
					session.Ignored[target.Key] = 0;
					Send( session, ChatEvent.ForNotice( ChatNotice.YouIgnore, target.Name ) );
				}
				break;
			}
			case ChatCommand.Mark:
			{
				var (name, reason) = ChatCommands.SplitName( argument );
				if ( name == null )
				{
					Send( session, ChatEvent.ForNotice( ChatNotice.WhoToBlackmark ) );
					break;
				}
				var target = store.FindAccount( name );
				if ( target == null || target.Key == session.Key )
				{
					Send( session, ChatEvent.ForNotice( ChatNotice.NoSuchPlayer, name ) );
					break;
				}
				store.Report( session.Account, ReportKinds.Chat, target.Name, reason.Length > 0 ? reason : "blackmark" );
				Send( session, ChatEvent.ForNotice( ChatNotice.YouBlackmark, target.Name ) );
				break;
			}
			case ChatCommand.Friend:
			case ChatCommand.Buddy:
			{
				var (name, _) = ChatCommands.SplitName( argument );
				if ( name == null )
				{
					var buddies = store.BuddyNames( session.Account );
					foreach ( var buddy in buddies )
						Send( session, ChatEvent.ForNotice( IsOnline( buddy ) ? ChatNotice.YourBuddyOnline : ChatNotice.YourBuddyOffline, buddy ) );
					if ( buddies.Count == 0 )
						Send( session, new ChatEvent( ChatEventKind.System, Text: ChatSystemKeys.BuddyList, Args: Array.Empty<string>() ) );
					break;
				}
				var target = store.FindAccount( name );
				if ( target == null )
					Send( session, ChatEvent.ForNotice( ChatNotice.NoSuchPlayer, name ) );
				else if ( target.Key == session.Key )
					Send( session, ChatEvent.ForNotice( ChatNotice.CannotBuddySelf ) );
				else
				{
					try
					{
						var added = store.ToggleBuddy( session.Account, target );
						Send( session, ChatEvent.ForNotice( added ? ChatNotice.YouMakeBuddy : ChatNotice.YourBuddyRemoved, target.Name ) );
					}
					catch ( StoreException )
					{
						Send( session, ChatEvent.ForNotice( ChatNotice.CouldNotAddBuddy, target.Name ) );
					}
				}
				break;
			}
			case ChatCommand.Who:
				Send( session, RoomEvent( session.Room ) );
				break;
			case ChatCommand.Page:
			{
				var (name, _) = ChatCommands.SplitName( argument );
				if ( name == null )
				{
					Send( session, ChatEvent.ForNotice( ChatNotice.NeedName ) );
					break;
				}
				var target = Online( name );
				if ( target == null )
				{
					Send( session, ChatEvent.ForNotice( ChatNotice.NoSuchPlayer, name ) );
					break;
				}
				Deliver( target, new ChatEvent( ChatEventKind.Page, from, session.Room ) );
				Send( session, ChatEvent.ForNotice( ChatNotice.YouPage, target.Name ) );
				break;
			}
			case ChatCommand.Locate:
			{
				var (name, _) = ChatCommands.SplitName( argument );
				var target = name == null ? null : Online( name );
				if ( target == null )
					Send( session, name == null ? ChatEvent.ForNotice( ChatNotice.NeedName ) : ChatEvent.ForNotice( ChatNotice.NoSuchPlayer, name ) );
				else
					Send( session, new ChatEvent( ChatEventKind.Notice, target.Name, target.Room, Notice: (int)ChatNotice.ThePark, Args: new[] { target.Name, RoomName( target.Room ) } ) );
				break;
			}
			case ChatCommand.Afk:
				session.Afk = !session.Afk;
				Send( session, ChatEvent.ForNotice( session.Afk ? ChatNotice.AfkOn : ChatNotice.AfkOff ) );
				foreach ( var other in sessions.Values.Where( other => other.Room == session.Room && other != session ) )
					Deliver( other, new ChatEvent( ChatEventKind.Say, from, session.Room, Notice: (int)(session.Afk ? ChatNotice.AfkAnnounceOn : ChatNotice.AfkAnnounceOff) ) );
				break;
			case ChatCommand.Goto:
			{
				var (name, _) = ChatCommands.SplitName( argument );
				var target = name == null ? null : Online( name );
				if ( name == null )
					Send( session, ChatEvent.ForNotice( ChatNotice.NeedName ) );
				else if ( target == session )
					Send( session, ChatEvent.ForNotice( ChatNotice.CannotGotoSelf ) );
				else if ( target == null )
					Send( session, ChatEvent.ForNotice( ChatNotice.NoSuchPlayer, name ) );
				else if ( session.Room != ChatProtocol.LobbyRoom && target.Room != session.Room )
					// [APPROX:ONLINE-006] A visitor inside a park must leave it first (CHAT_COMMANDS 127) — evidence needed.
					Send( session, ChatEvent.ForNotice( ChatNotice.LeaveParkToGoTo, target.Name ) );
				else
				{
					session.Room = target.Room;
					Send( session, ChatEvent.ForNotice( ChatNotice.YouGoTo, target.Name ) );
					Send( session, RoomEvent( session.Room ) );
				}
				break;
			}
			case ChatCommand.Vote:
				if ( !session.Room.StartsWith( ChatProtocol.ParkRoomPrefix, StringComparison.Ordinal ) )
				{
					Send( session, ChatEvent.ForNotice( ChatNotice.NoPark ) );
					break;
				}
				try
				{
					store.Vote( session.Account, session.Room[ChatProtocol.ParkRoomPrefix.Length..] );
					Send( session, ChatEvent.ForNotice( ChatNotice.VotedForPark ) );
				}
				catch ( StoreException exception )
				{
					Send( session, ChatEvent.ForNotice( exception.Status == 429 ? ChatNotice.VoteLimitReached : ChatNotice.VoteFailed ) );
				}
				break;
			case ChatCommand.Filter:
				session.Filter = !session.Filter;
				Send( session, ChatEvent.ForNotice( session.Filter ? ChatNotice.FilterOn : ChatNotice.FilterOff ) );
				break;
			default:
			{
				// Mood emotes (cheer … confused): the room sees the emote, the sender gets the original "You …" line.
				var mood = new ChatEvent( ChatEventKind.Emote, from, session.Room, command == ChatCommand.Think ? argument : null, ChatCommands.CanonicalName( command ) );
				foreach ( var other in sessions.Values.Where( other => other.Room == session.Room && other != session ) )
					Deliver( other, mood );
				var notice = ChatCommands.MoodNotice( command );
				Send( session, notice != null ? ChatEvent.ForNotice( notice.Value ) : mood );
				break;
			}
		}
	}

	private string RoomName( string room )
	{
		if ( room == ChatProtocol.LobbyRoom )
			return "";
		return store.FindPark( room[ChatProtocol.ParkRoomPrefix.Length..] )?.Name ?? "";
	}

	private void Tell( ChatSession session, string name, string text, bool emote )
	{
		var target = Online( name );
		if ( target == null )
		{
			Send( session, ChatEvent.ForNotice( ChatNotice.NoSuchPlayer, name ) );
			return;
		}
		if ( text.Length == 0 )
			return;
		Deliver( target, new ChatEvent( emote ? ChatEventKind.Emote : ChatEventKind.Tell, session.Name, Text: text, Command: emote ? ChatCommands.CanonicalName( ChatCommand.ETell ) : null ) );
		target.LastToldBy = session.Name;
		session.LastToldTo = target.Name;
		Send( session, ChatEvent.ForNotice( emote ? ChatNotice.YouEmotedTo : ChatNotice.YouTold, target.Name, Filtered( session, text ) ) );
	}

	private ChatSession? Online( string name ) => sessions.GetValueOrDefault( OnlineText.NormalizeName( name ) );

	private bool IsOnline( string name ) => Online( name ) != null;

	private void Broadcast( string room, ChatEvent item )
	{
		foreach ( var other in sessions.Values.Where( other => other.Room == room ) )
			Deliver( other, item );
	}

	/// <summary>Applies the recipient's ignore list and word filter.</summary>
	private void Deliver( ChatSession to, ChatEvent item )
	{
		if ( item.From != null && to.Ignored.ContainsKey( OnlineText.NormalizeName( item.From ) ) )
			return;
		Send( to, item.Text == null ? item : item with { Text = Filtered( to, item.Text ) } );
	}

	private string Filtered( ChatSession to, string text ) => to.Filter ? filter.Apply( text ) : text;

	private static void Send( ChatSession to, ChatEvent item ) => to.Outbox.Writer.TryWrite( item );
}
