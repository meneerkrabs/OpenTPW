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
	/// <summary>A buddy of the receiver entered a park; arguments: the buddy's name, the park (with its city).</summary>
	public const string BuddyEnters = "buddy-enters";
	/// <summary>[EXT:online-coop-session] The located player is in a shared park, whose room is its members' alone; argument: the player's name.</summary>
	public const string InSharedPark = "in-shared-park";
}

/// <summary>
/// Chat rooms over WebSockets: the lobby ("Online World") and one room per published park.
/// Commands follow the original CHAT_COMMANDS.str list; responses are CHAT_COMMANDS.str indices.
/// [EXT:online-chat-command-semantics] OpenTPW's chat server gives the commands meanings that fit their
/// command words, response strings and the weachatr.dll export names (Say, Tell, ParkShout, Page, Locate,
/// Goto, AFK, Earmuffs, Ignore, Blackmark, AddBuddy/RemoveBuddy, SetHearingRange): say/emote reach the
/// whole room (no positions, so "hearing" has no effect), shout/eshout reach the room, wshout/ushout every
/// room, and "mark" (blackmark) files a moderation report instead of muting automatically. The original
/// chat server is not among the available files, so its behaviour is not reproduced.
/// </summary>
public sealed class ChatHub
{
	private readonly ServerStore store;
	private readonly ServerOptions options;
	private readonly WordFilter filter;
	private readonly ChatParkDirectory chatParks;
	private readonly CityDirectory cities;
	private readonly ConcurrentDictionary<string, ChatSession> sessions = new();
	/// <summary>
	/// Held while a player enters, leaves or connects and while a roster is taken and queued. Each connection's
	/// requests run on their own thread, so without it two players entering a room together could each take a roster
	/// before the other's arrival and queue it after the other's newer one: a player would end with a roster that
	/// leaves out someone in the room. Under the lock every outbox receives a room's rosters in the order its
	/// membership changed.
	/// </summary>
	private readonly object rooms = new();
	private volatile bool restarting;
	/// <summary>[EXT:online-coop-session] Who may enter a shared park's room (<see cref="UseCoopMembership"/>); none while unset.</summary>
	private ICoopMembership? coopMembership;

	public ChatHub( ServerStore store, ServerOptions options, WordFilter filter, ChatParkDirectory chatParks, CityDirectory cities )
	{
		this.store = store;
		this.options = options;
		this.filter = filter;
		this.chatParks = chatParks;
		this.cities = cities;
	}

	public int PlayersIn( string room ) => sessions.Values.Count( session => session.Room == room );

	/// <summary>
	/// [EXT:online-coop-session] Lets the members of a shared park's session into its room (<see cref="ChatProtocol.CoopRoom"/>), by a join or a goto, and takes each
	/// one back to the lobby once it stops being a member (it left or was kicked, or the session ended). Without it, no session's room admits anyone.
	/// </summary>
	public void UseCoopMembership( ICoopMembership membership )
	{
		ArgumentNullException.ThrowIfNull( membership );
		coopMembership = membership;
		// raised under the shared parks' locks: the move runs on its own
		membership.MemberGone += ( accountKey, sessionId ) => ThreadPool.QueueUserWorkItem( _ => LeaveCoopRoom( accountKey, sessionId ) );
	}

	/// <summary>Takes the account's connections that are in the session's room back to the lobby.</summary>
	private void LeaveCoopRoom( string accountKey, string sessionId )
	{
		var room = ChatProtocol.CoopRoom( sessionId );
		foreach ( var session in sessions.Values.Where( session => session.Key == accountKey ) )
			EnterRoom( session, ChatProtocol.LobbyRoom, onlyFrom: room );
	}

	/// <summary>Whether <paramref name="session"/>'s player is a member of the shared park whose room is <paramref name="room"/>.</summary>
	private bool IsCoopMember( ChatSession session, string room ) =>
		ChatProtocol.CoopSessionOf( room ) is { } sessionId && coopMembership?.IsMember( session.Key, sessionId ) == true;

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
			MovementLimiter = new TokenBucketRateLimiter( new TokenBucketRateLimiterOptions
			{
				// [EXT:ONLINE-072] walking sends a position whenever the player moves, so the movement bucket is
				// sized for a walk, not a message: a burst of turns, then a position every few frames.
				TokenLimit = options.MovementBurst,
				TokensPerPeriod = options.MovementsPerSecond,
				ReplenishmentPeriod = TimeSpan.FromSeconds( 1 ),
				QueueLimit = 0,
				AutoReplenishment = true,
			} );
		}

		public AccountRecord Account { get; }
		public string Name => Account.Name;
		public string Key => Account.Key;
		public volatile string Room = ChatProtocol.LobbyRoom;
		/// <summary>The avatar's position in a chat park, both axes 0-1; null until it has moved.</summary>
		public (float X, float Y)? Position;
		/// <summary>
		/// Set with the first movement this connection sends: only clients that walk receive movement, so older
		/// game versions (v0.7.x, which have no chat-park view) never get frames they would draw as empty lines.
		/// </summary>
		public bool ReceivesMovement;
		/// <summary>Set by the first movement that carries a mode (protocol 5): only such clients are sent modes, which older ones refuse.</summary>
		public bool ReceivesModes;
		/// <summary>The player's figure is hidden in the room (they ride a ride, <see cref="ChatMoveMode.Hide"/>) until their next other movement.</summary>
		public bool Hidden;
		public string? LastToldTo;
		public string? LastToldBy;
		public bool Earmuffs;
		public bool Afk;
		public bool Filter = true;
		public readonly ConcurrentDictionary<string, byte> Ignored = new();
		public readonly Channel<ChatEvent> Outbox = Channel.CreateBounded<ChatEvent>( new BoundedChannelOptions( 256 ) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true } );
		public TokenBucketRateLimiter Limiter { get; }
		public TokenBucketRateLimiter MovementLimiter { get; }
		/// <summary>Cancelled to end the connection from the server side (account deleted).</summary>
		public readonly CancellationTokenSource Closed = new();
	}

	/// <summary>
	/// The server is stopping (a deploy): ends every chat connection with <see cref="ChatProtocol.RestartingCloseStatus"/>,
	/// so the games reconnect to the new server instead of waiting for a dead connection.
	/// </summary>
	public void Restarting()
	{
		restarting = true;
		// The write loop sends what is queued, then the close; the client answers it and the read loop ends. Cancelling
		// the pending receive instead would abort the socket without a close frame.
		foreach ( var session in sessions.Values )
		{
			session.Outbox.Writer.TryComplete();
			// A client that does not answer the close is dropped after a moment.
			try { session.Closed.CancelAfter( TimeSpan.FromSeconds( 3 ) ); }
			catch ( ObjectDisposedException ) { }
		}
	}

	/// <summary>Ends a player's chat connection, if any (their account was deleted).</summary>
	public void Disconnect( string accountKey )
	{
		if ( sessions.TryGetValue( accountKey, out var session ) )
			session.Closed.Cancel();
	}

	public async Task RunAsync( WebSocket socket, AccountRecord account, CancellationToken cancel, Func<bool>? stillAuthenticated = null )
	{
		var session = new ChatSession( account, options );
		bool added;
		lock ( rooms )
		{
			added = sessions.Count < options.MaximumChatConnections && sessions.TryAdd( account.Key, session );
			if ( added )
			{
				Send( session, ChatEvent.ForNotice( ChatNotice.WelcomeThemeParkWorld ) );
				if ( !string.IsNullOrWhiteSpace( options.Message ) )
					Send( session, new ChatEvent( ChatEventKind.System, Text: ChatSystemKeys.ServerMessage, Args: new[] { options.Message } ) );
				Send( session, RoomEvent( session.Room ) );
			}
		}
		if ( !added )
		{
			session.Limiter.Dispose();
			session.MovementLimiter.Dispose();
			session.Closed.Dispose();
			var notice = sessions.ContainsKey( account.Key ) ? ChatNotice.NameAlreadyOnline : ChatNotice.ParkFull;
			await socket.SendAsync( StrictJson.Serialize( ChatEvent.ForNotice( notice, account.Name ) ), WebSocketMessageType.Text, true, cancel );
			await CloseAsync( socket, WebSocketCloseStatus.PolicyViolation, "already connected" );
			return;
		}
		if ( stillAuthenticated != null && !stillAuthenticated() )
			session.Closed.Cancel();
		using var linked = CancellationTokenSource.CreateLinkedTokenSource( cancel, session.Closed.Token );
		cancel = linked.Token;
		var writer = Task.Run( () => WriteLoopAsync( socket, session, cancel ), cancel );
		try
		{
			await ReadLoopAsync( socket, session, cancel );
		}
		catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException or InvalidDataException )
		{
		}
		finally
		{
			lock ( rooms )
			{
				sessions.TryRemove( new KeyValuePair<string, ChatSession>( account.Key, session ) );
				// The park the player left gets its roster without them, so the people list shows the parting.
				var parted = RoomEvent( session.Room );
				foreach ( var other in sessions.Values.Where( other => other.Room == session.Room ) )
					Send( other, parted );
			}
			session.Outbox.Writer.TryComplete();
			try { await writer; } catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException ) { }
			session.Limiter.Dispose();
			session.MovementLimiter.Dispose();
			session.Closed.Dispose();
			if ( socket.State == WebSocketState.Open )
			{
				await CloseAsync( socket, WebSocketCloseStatus.NormalClosure, "bye" );
			}
		}
	}

	private async Task WriteLoopAsync( WebSocket socket, ChatSession session, CancellationToken cancel )
	{
		await foreach ( var item in session.Outbox.Reader.ReadAllAsync( cancel ) )
		{
			if ( socket.State != WebSocketState.Open )
				return;
			await socket.SendAsync( StrictJson.Serialize( item ), WebSocketMessageType.Text, true, cancel );
		}
		if ( restarting && socket.State == WebSocketState.Open )
			await socket.CloseOutputAsync( (WebSocketCloseStatus)ChatProtocol.RestartingCloseStatus, "server restarting", cancel );
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
					await CloseAsync( socket, WebSocketCloseStatus.MessageTooBig, "frame too large" );
					return;
				}
				var result = await socket.ReceiveAsync( buffer.AsMemory( length ), cancel );
				if ( result.MessageType == WebSocketMessageType.Close )
					return;
				if ( result.MessageType != WebSocketMessageType.Text )
				{
					await CloseAsync( socket, WebSocketCloseStatus.InvalidMessageType, "text only" );
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
					await CloseAsync( socket, WebSocketCloseStatus.PolicyViolation, "too many invalid requests" );
					return;
				}
				continue;
			}
			using var lease = (request.Type == ChatRequest.MoveType ? session.MovementLimiter : session.Limiter).AttemptAcquire();
			if ( !lease.IsAcquired )
			{
				// Walking positions are dropped silently: the next one carries the player's place, and a reply
				// for every dropped step would outshout the chat itself.
				if ( request.Type != ChatRequest.MoveType )
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
			if ( ChatProtocol.SatelliteIndexOf( room ) is { } satellite )
			{
				// A chat park of the online world's globe (docs/SERVER.md): a standing room, not a published park.
				if ( chatParks.Find( satellite ) == null )
				{
					Send( session, ChatEvent.ForNotice( ChatNotice.NoPark ) );
					return;
				}
			}
			// [EXT:online-coop-session] a shared park's room is no published park: EnterRoom admits its members alone
			else if ( room != ChatProtocol.LobbyRoom && ChatProtocol.CoopSessionOf( room ) == null && store.FindPark( room[ChatProtocol.ParkRoomPrefix.Length..] ) == null )
			{
				Send( session, ChatEvent.ForNotice( ChatNotice.NoPark ) );
				return;
			}
			EnterRoom( session, room );
			return;
		}
		if ( request.Type == ChatRequest.MoveType )
		{
			// Walking exists only inside a park (a satellite chat park or a published park's room). The lobby holds
			// every connected player, so relaying movement there would multiply each step by the whole server.
			if ( session.Room == ChatProtocol.LobbyRoom )
				return;
			// The server relays the player's movement to everyone in the chat park (manual p. 68: players walk
			// and teleport around a park they are in together). The first movement also marks this connection as
			// one that draws movement, and the answer to the first movement in each room carries where everyone
			// else already stands — so a joiner sees the park's players at their places, also in the second park
			// a connection walks in, and an older client (v0.7.x) never receives a frame it would draw as an
			// empty line.
			var position = (request.X!.Value, request.Y!.Value);
			// entering a room forgets the player's place there (EnterRoom), so no place yet is the first movement in this room
			var first = session.Position == null;
			session.Position = position;
			session.ReceivesMovement = true;
			// [EXT:online-ride-hiding] a movement's mode (protocol 5) goes only to clients that send modes themselves; a hidden player's figure (riding a
			// ride) is not relayed to the others, who keep it where it stood and see it again at its next place
			if ( request.Mode != null )
				session.ReceivesModes = true;
			session.Hidden = request.Mode == ChatMoveMode.Hide;
			var moved = new ChatEvent( ChatEventKind.Move, session.Name, session.Room, X: position.Item1, Y: position.Item2, Mode: request.Mode );
			foreach ( var other in sessions.Values.Where( other => other.Room == session.Room && other != session && other.ReceivesMovement ) )
			{
				if ( other.ReceivesModes )
					Deliver( other, moved );
				else if ( !session.Hidden )
					Deliver( other, moved with { Mode = null } );
			}
			// a joiner is told where the others stand, not where a hidden player stood
			if ( first )
				foreach ( var other in sessions.Values.Where( other => other.Room == session.Room && other != session && other.Position != null && !other.Hidden ) )
					Send( session, new ChatEvent( ChatEventKind.Move, other.Name, session.Room, X: other.Position!.Value.X, Y: other.Position!.Value.Y ) );
			return;
		}
		ChatCommands.TryParseCanonical( request.Command!, out var command );
		Execute( session, command, request.Argument ?? "" );
	}

	/// <summary>
	/// Places <paramref name="session"/> in <paramref name="room"/>: the joiner sees the room's roster and where
	/// everyone stands, everyone else sees the new roster (the original's "see players join and the list of players
	/// in the session"), and the joiner's online buddies are told which park they entered. A join of the room the
	/// player is already in (a retried join whose answer came late) only answers the roster: the player keeps their
	/// place and nobody is told again.
	/// </summary>
	private void EnterRoom( ChatSession session, string room, string? onlyFrom = null )
	{
		lock ( rooms )
		{
			var left = session.Room;
			// a move out of one room only (a shared park's member that stopped being one): a player who has moved on since stays where they are
			if ( onlyFrom != null && left != onlyFrom )
				return;
			// [EXT:online-coop-session] a shared park's room is its members' alone, asked while the rooms are held: the move out of a member that stops being
			// one waits for them, so it comes after this entry and no one who is not a member stays in the room
			if ( ChatProtocol.CoopSessionOf( room ) != null && !IsCoopMember( session, room ) )
			{
				Send( session, ChatEvent.ForNotice( ChatNotice.NoPark ) );
				return;
			}
			if ( left == room )
			{
				Send( session, RoomEvent( room ) );
				return;
			}
			session.Room = room;
			session.Position = null;
			session.Hidden = false;
			// everyone in the room, the joiner included, gets the same roster, built once while the lock is held
			var entered = RoomEvent( room );
			foreach ( var other in sessions.Values.Where( other => other.Room == room ) )
				Send( other, entered );
			// The park the player walked out of gets its roster without them, as when they disconnect.
			var parted = RoomEvent( left );
			foreach ( var other in sessions.Values.Where( other => other.Room == left ) )
				Send( other, parted );
		}
		NotifyBuddiesOfEntry( session );
	}

	/// <summary>
	/// Tells the player's online buddies that they entered a park (docs/reverse/ONLINE-SOURCES.md S5), wherever those
	/// buddies are — "another park or the park you are in" in the original's words.
	/// </summary>
	// [EXT:online-buddy-enters-text] the client shows OpenTPW's own sentence for this system key: the original
	// notification's string is not recovered (CHAT_COMMANDS 111-113, "Your buddy ", have empty English suffixes and
	// no enter-the-park variant)
	private void NotifyBuddiesOfEntry( ChatSession session )
	{
		var room = RoomName( session.Room );
		if ( room.Length == 0 )
			return;
		foreach ( var other in sessions.Values.Where( other => other != session && store.BuddyNames( other.Account ).Contains( session.Name ) ) )
			Send( other, new ChatEvent( ChatEventKind.System, Text: ChatSystemKeys.BuddyEnters, Args: new[] { session.Name, room } ) );
	}

	private ChatEvent RoomEvent( string room ) => RosterEvent( room,
		sessions.Values.Where( other => other.Room == room ).Select( other => other.Name ).OrderBy( name => name, StringComparer.OrdinalIgnoreCase ).ToArray() );

	/// <summary>
	/// The room event with as many names as fit one chat frame, and "+N" for the rest: clients reject frames over
	/// <see cref="ChatProtocol.MaximumFrameBytes"/>, so a full roster of a busy lobby would cut every client off.
	/// </summary>
	public static ChatEvent RosterEvent( string room, IReadOnlyList<string> names )
	{
		ChatEvent With( int count ) => new( ChatEventKind.Room, Room: room,
			Args: count == names.Count ? names.ToArray() : names.Take( count ).Append( $"+{names.Count - count}" ).ToArray() );
		bool Fits( int count ) => (count == names.Count ? count : count + 1) <= ChatProtocol.MaximumArguments
			&& StrictJson.Serialize( With( count ) ).Length <= ChatProtocol.MaximumFrameBytes - 256;
		if ( Fits( names.Count ) )
			return With( names.Count );
		var (low, high) = (0, names.Count - 1);
		while ( low < high )
		{
			var middle = (low + high + 1) / 2;
			if ( Fits( middle ) )
				low = middle;
			else
				high = middle - 1;
		}
		return With( low );
	}

	/// <summary>
	/// Sends a close frame and waits at most two seconds for the peer's reply, then drops the connection: a peer that
	/// never answers the close handshake cannot hold the socket open.
	/// </summary>
	internal static async Task CloseAsync( WebSocket socket, WebSocketCloseStatus status, string reason )
	{
		using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 2 ) );
		try
		{
			if ( socket.State is WebSocketState.Open or WebSocketState.CloseReceived )
				await socket.CloseOutputAsync( status, reason, timeout.Token );
			var discard = new byte[256];
			while ( socket.State == WebSocketState.CloseSent )
			{
				if ( (await socket.ReceiveAsync( discard, timeout.Token )).MessageType == WebSocketMessageType.Close )
					break;
			}
		}
		catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException ) { }
		if ( socket.State != WebSocketState.Closed )
			socket.Abort();
	}

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
				lock ( rooms )
					Send( session, RoomEvent( session.Room ) );
				break;
			case ChatCommand.Page:
			{
				// A page carries its message: "Dan pages you: Hello" (manual p. 70, S5).
				var (name, text) = ChatCommands.SplitName( argument );
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
				Deliver( target, new ChatEvent( ChatEventKind.Page, from, session.Room, Text: text ) );
				Send( session, ChatEvent.ForNotice( ChatNotice.YouPage, target.Name, Filtered( session, text ) ) );
				break;
			}
			case ChatCommand.Locate:
			{
				var (name, _) = ChatCommands.SplitName( argument );
				var target = name == null ? null : Online( name );
				if ( target == null )
					Send( session, name == null ? ChatEvent.ForNotice( ChatNotice.NeedName ) : ChatEvent.ForNotice( ChatNotice.NoSuchPlayer, name ) );
				else if ( ChatProtocol.CoopSessionOf( target.Room ) != null )
					// [EXT:online-coop-session] a shared park is named to no one: the player is in one
					Send( session, new ChatEvent( ChatEventKind.System, Text: ChatSystemKeys.InSharedPark, Args: new[] { target.Name } ) );
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
				// the target's room, read once: the target may move on while this runs
				var room = target?.Room;
				if ( name == null )
					Send( session, ChatEvent.ForNotice( ChatNotice.NeedName ) );
				else if ( target == session )
					Send( session, ChatEvent.ForNotice( ChatNotice.CannotGotoSelf ) );
				else if ( target == null || room == null )
					Send( session, ChatEvent.ForNotice( ChatNotice.NoSuchPlayer, name ) );
				else if ( session.Room != ChatProtocol.LobbyRoom && room != session.Room )
					// [EXT:online-goto-leave-park] the server refuses a goto into another park from inside a park with
					// CHAT_COMMANDS 127, whose text the original has; where the original enforced it is not reproduced.
					Send( session, ChatEvent.ForNotice( ChatNotice.LeaveParkToGoTo, target.Name ) );
				else if ( ChatProtocol.CoopSessionOf( room ) != null && !IsCoopMember( session, room ) )
					// [EXT:online-coop-session] only a shared park's members go to someone in its room (EnterRoom asks again as it enters)
					Send( session, ChatEvent.ForNotice( ChatNotice.NoPark ) );
				else
				{
					Send( session, ChatEvent.ForNotice( ChatNotice.YouGoTo, target.Name ) );
					EnterRoom( session, room );
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
		if ( ChatProtocol.SatelliteIndexOf( room ) is { } satellite )
			return chatParks.Find( satellite )?.Name ?? "";
		// [EXT:online-coop-session] a shared park is named to no one (so no buddy hears of it)
		if ( ChatProtocol.CoopSessionOf( room ) != null )
			return "";
		var park = store.FindPark( room[ChatProtocol.ParkRoomPrefix.Length..] );
		if ( park == null )
			return "";
		// "Dan is currently in Halloween World in San Francisco" (manual p. 70): the park and its city.
		var city = park.CityId != 0 ? cities.Find( park.CityId )?.Name : null;
		return city == null ? park.Name : $"{park.Name} in {city}";
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
