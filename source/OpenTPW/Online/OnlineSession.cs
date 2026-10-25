using System.Collections.Concurrent;
using OpenTPW.Online.Api;
using OpenTPW.Online.Chat;
using OpenTPW.Online.Client;
using OpenTPW.Online.Packages;

namespace OpenTPW;

/// <summary>Formats chat events with the selected language's CHAT_COMMANDS.str.</summary>
public static class ChatFormatter
{
	/// <param name="chatStrings">CHAT_COMMANDS.str entries (null: English command words, index fallbacks).</param>
	public static string Format( ChatEvent item, IReadOnlyList<string>? chatStrings, Func<string, string> roomName, Func<OnlineLabel, string> label )
	{
		string Word( string? canonical )
		{
			if ( canonical == null || !ChatCommands.TryParseCanonical( canonical, out var command ) )
				return canonical ?? "";
			return chatStrings != null && chatStrings.Count > (int)command && chatStrings[(int)command].Length > 0 ? chatStrings[(int)command] : canonical;
		}
		string Notice( int index, IEnumerable<string>? args ) =>
			((chatStrings != null && index >= 0 && index < chatStrings.Count && chatStrings[index].Length > 0 ? chatStrings[index] : $"[{index}] ")
			+ string.Join( " ", (args ?? Array.Empty<string>()).Where( arg => arg.Length > 0 ) )).Trim();

		// [EXT:ONLINE-064] Line layouts ("name: text", "name *emote*") are OpenTPW choices; the original chat panel layout is unknown.
		return item.Kind switch
		{
			ChatEventKind.Say when item.Notice != null => $"{item.From}: {Notice( item.Notice.Value, null )}",
			ChatEventKind.Say => $"{item.From}: {item.Text}",
			ChatEventKind.Tell => $"{item.From} [{Word( "tell" )}]: {item.Text}",
			ChatEventKind.Shout => $"{item.From} [{Word( "shout" )}]: {item.Text}",
			ChatEventKind.WorldShout => $"{item.From} [{Word( item.Command ?? "wshout" )}]: {item.Text}",
			ChatEventKind.Emote when item.Text != null => $"{item.From} {item.Text}",
			ChatEventKind.Emote => $"{item.From} *{Word( item.Command )}*",
			ChatEventKind.Page => item.Text is { Length: > 0 } text ? $"{item.From} [{Word( "page" )}]: {text}" : $"{item.From} [{Word( "page" )}]",
			ChatEventKind.Notice when item.Notice == (int)ChatNotice.Commands =>
				Notice( item.Notice.Value, null ) + " " + string.Join( ", ", (item.Args ?? Array.Empty<string>()).Select( Word ) ),
			ChatEventKind.Notice when item.Notice == (int)ChatNotice.ThePark && item.Args is { Count: 2 } args =>
				args[1].Length > 0 ? $"{args[0]}: {Notice( item.Notice.Value, new[] { args[1] } )}" : $"{args[0]}: {roomName( OpenTPW.Online.Chat.ChatProtocol.LobbyRoom )}",
			ChatEventKind.Notice when item.Notice == (int)ChatNotice.YouTold && item.Args is { Count: 2 } told => $"{Notice( item.Notice.Value, new[] { told[0] } )}: {told[1]}",
			ChatEventKind.Notice => Notice( item.Notice ?? 0, item.Args ),
			ChatEventKind.System when item.Text == "rate-limited" => label( OnlineLabel.RateLimited ),
			ChatEventKind.System when item.Text == "buddy-list" => label( OnlineLabel.NoBuddies ),
			// [EXT:online-buddy-enters-text] the wording is OpenTPW's own; the original notification's string is not recovered
			ChatEventKind.System when item.Text == "buddy-enters" && item.Args is { Count: 2 } enters =>
				string.Format( label( OnlineLabel.BuddyEnters ), enters[0], enters[1] ),
			ChatEventKind.System when item.Text == "server-message" => Notice( OnlineStrings.SystemMessage, item.Args ),
			// [EXT:online-coop-session] a located player in a shared park, which the server names to no one
			ChatEventKind.System when item.Text == "in-shared-park" && item.Args is { Count: 1 } shared =>
				string.Format( UI.Original.SupplementaryStrings.Get( UI.Original.OpenTpwText.PlayerInSharedPark, GameLanguage.IsSelected ? GameLanguage.Current.Name : GameLanguage.DefaultLanguage ), shared[0] ),
			// [EXT:online-coop-session] something said for a shared park's room the connection was not in was not sent
			ChatEventKind.System when item.Text == "shared-park-chat-closed" => OnlineSession.SharedParkChatClosedText(),
			ChatEventKind.System => item.Text ?? "",
			ChatEventKind.Room => $"{roomName( item.Room ?? "" )}: {string.Join( ", ", item.Args ?? Array.Empty<string>() )}",
			_ => item.Text ?? "",
		};
	}
}

/// <summary>
/// Client-side online state for the in-game panel: the opt-in server, login, park list, postcards and
/// chat. Network work runs on background tasks; results are applied on the game thread by <see cref="Pump"/>.
/// </summary>
public sealed class OnlineSession : IDisposable, IPresenceSession, IChatPanelSource
{
	public const int MaximumChatLines = 200;
	private readonly ConcurrentQueue<(int Generation, Action Action, Action? Discard)> mainThread = new();
	private readonly AsyncLocal<int?> operationGeneration = new();
	private int generation;
	private bool disposed;
	private CancellationTokenSource? chatCancel;
	/// <summary>The chat requests in the order the game made them (joins, places, lines), sent one after another.</summary>
	private Task chatSends = Task.CompletedTask;
	private int pendingChatSends;
	/// <summary>A chat connection is being opened (the first one or a reconnection).</summary>
	private bool chatConnecting;
	/// <summary>Counts <see cref="CloseChat"/> and <see cref="Disconnect"/>: a connection opened under an older count is not installed.</summary>
	private int chatEpoch;
	/// <summary>The room <see cref="JoinRoom"/> asked for last, and whether the server has not confirmed it yet.</summary>
	private string? requestedRoom;
	private bool joinPending;
	/// <summary>When the pending join was sent (<see cref="Environment.TickCount64"/>), or null while it waits for a connection.</summary>
	private long? joinSentAt;
	/// <summary>A join the server neither confirms nor refuses within this long is sent again (the chat rate limit drops requests over it).</summary>
	internal const int JoinAnswerMilliseconds = 5000;
	/// <summary>The newest place <see cref="MoveTo"/> was given and not sent yet, with the connection it belongs to and the mode the player asked for.</summary>
	private (ChatConnection Chat, float X, float Y, ChatMoveMode Mode)? latestMove;
	private bool moveQueued;
	private readonly object moveLock = new();
	private bool fetchingServerInfo;
	/// <summary>The server-version read in flight. A newer read replaces it, and an older read's answer is ignored.</summary>
	private int serverInfoEpoch;
	/// <summary>
	/// Waits before trying a connection's version again after a failed read, then the last wait is reused. One read at a time, so a dead server is asked
	/// at most once per this interval and modes do not stay off for the rest of the connection.
	/// </summary>
	internal static readonly TimeSpan[] ServerInfoRetryDelays =
	{
		TimeSpan.FromSeconds( 1 ), TimeSpan.FromSeconds( 2 ), TimeSpan.FromSeconds( 4 ), TimeSpan.FromSeconds( 8 ), TimeSpan.FromSeconds( 15 ),
	};
	private TimeSpan[] serverInfoRetryDelays = ServerInfoRetryDelays;

	public OnlineSession( OnlineFolders folders )
	{
		Folders = folders;
		Coop = new CoopLink( this );
		// [EXT:online-coop-session] the chat follows the shared park: its members talk in the session's room, and are back in the lobby once it is over for this game
		Coop.SessionStarted += state => EnterCoopRoom( state.Id );
		Coop.Resumed += state => EnterCoopRoom( state.Id );
		Coop.SessionEnded += ended => LeftCoopSession( ended.SessionId );
		try { Settings = folders.LoadSettings(); }
		catch ( Exception exception ) when ( exception is InvalidDataException or IOException )
		{
			Settings = new OnlineSettings( null, null );
			Status = exception.Message;
		}
	}

	public OnlineFolders Folders { get; }
	public OnlineSettings Settings { get; private set; }
	/// <summary>[EXT:online-coop-session] The link to the co-op socket of shared parks, under this session's login (docs/ONLINE.md, "Shared parks").</summary>
	public CoopLink Coop { get; }

	/// <summary>
	/// Offered in the server field while no server has been chosen: the browser build sets the
	/// address it was loaded from, which serves the API too (docs/WEB.md). Null on the desktop.
	/// </summary>
	public static string? SuggestedServerUrl { get; set; }
	/// <summary>The chat commands of the current language (rebuilt after a language switch).</summary>
	public ChatCommandTable Commands
	{
		get
		{
			if ( commands == null || commandsVersion != GameLanguage.Version )
			{
				commands = new ChatCommandTable( OnlineStrings.ChatCommands );
				commandsVersion = GameLanguage.Version;
			}
			return commands;
		}
	}
	private ChatCommandTable? commands;
	private int commandsVersion = -1;
	public OnlineClient? Client { get; private set; }
	public ChatConnection? Chat { get; private set; }
	public string Status
	{
		get => status;
		private set
		{
			status = value;
			StatusSerial++;
		}
	}
	private string status = "";

	/// <summary>
	/// Counts every message <see cref="Status"/> was given, so a screen can show only what came after it opened or came back to the top: a status is the outcome of what the player just did,
	/// and a message from a search on a screen that is gone says nothing about the menu opened afterwards.
	/// </summary>
	public long StatusSerial { get; private set; }
	public int Busy { get; private set; }
	/// <summary>
	/// A chat connection is being opened or chat requests are still on their way. Chat traffic does not take the operation slot (<see cref="Busy"/>):
	/// a visit joins its park's room while the download that started it still holds the slot, and a walking player sends a place several times
	/// a second, which must not hold up a vote or a download.
	/// </summary>
	public bool ChatBusy => chatConnecting || Volatile.Read( ref pendingChatSends ) > 0;
	/// <summary>A chat connection is being opened (the first one or a reconnection).</summary>
	public bool ChatConnecting => chatConnecting;
	public IReadOnlyList<ParkSummary> Parks { get; private set; } = Array.Empty<ParkSummary>();
	public IReadOnlyList<PostcardSummary> Inbox { get; private set; } = Array.Empty<PostcardSummary>();
	/// <summary>Cities of the current server's online world; empty before fetching or when the server has none.</summary>
	public IReadOnlyList<CityInfo> Cities { get; private set; } = Array.Empty<CityInfo>();
	/// <summary>
	/// The chat parks of the current server's online world (the globe's five satellites), each with the players
	/// currently in its room; empty before fetching or when the server has none.
	/// </summary>
	public IReadOnlyList<ChatParkInfo> ChatParks { get; private set; } = Array.Empty<ChatParkInfo>();

	/// <summary>News fetched from the current server; null before fetching or when the server has none.</summary>
	public NewsInfo? News { get; private set; }
	public List<string> ChatLines { get; } = new();
	public string CurrentRoom { get; private set; } = ChatProtocol.LobbyRoom;
	/// <summary>The players in the current room, from the room's roster events ("who" in the original chat).</summary>
	public IReadOnlyList<string> RoomPlayers { get; private set; } = Array.Empty<string>();
	/// <summary>
	/// Where each player in the current chat park stands (both axes 0-1 within the park), from the server's relayed
	/// movement; the player's own position is set by <see cref="MoveTo"/>. A relayed hide removes that player's place
	/// until their next movement. Empty in a room nobody moved in.
	/// </summary>
	public IReadOnlyDictionary<string, (float X, float Y)> RoomPositions => positions;
	private readonly Dictionary<string, (float X, float Y)> positions = new( StringComparer.OrdinalIgnoreCase );
	/// <summary>
	/// How often each player's figure in the current room jumped (a teleport, or the appearance after a ride, <see cref="ChatMoveMode"/>): a figure whose
	/// count changed is shown at its place at once.
	/// </summary>
	public IReadOnlyDictionary<string, int> RoomJumps => jumps;
	private readonly Dictionary<string, int> jumps = new( StringComparer.OrdinalIgnoreCase );
	/// <summary>
	/// The logged-in player's own page (published parks, ranks, Platinum Tickets) or null before
	/// <see cref="FetchProfile"/> ran.
	/// </summary>
	public PlayerProfile? Profile { get; private set; }
	/// <summary>
	/// The buddies of the last <c>/buddy</c> answer and whether each was online then (the manual's buddy list: "Lists
	/// your park buddies who are currently online"); asked when the chat connection opens, empty until the first answer comes, and kept while a new ask
	/// is out.
	/// </summary>
	public IReadOnlyList<(string Name, bool Online)> Buddies => buddies;
	private readonly List<(string Name, bool Online)> buddies = new();
	/// <summary>The buddy answers now coming are the overlay's own asks (<see cref="AskBuddies"/>), kept off the chat lines; a typed <c>/buddy</c> shows them.</summary>
	private bool quietBuddyAnswers;
	/// <summary>An ask for the buddy list is out: its first answer replaces <see cref="Buddies"/>, which stands as it was until then.</summary>
	private bool buddyAnswerPending;
	/// <summary>When the last ask for the buddy list went out on this connection, typed or the overlay's (<see cref="Environment.TickCount64"/>).</summary>
	private long? buddyAskedAt;
	/// <summary>
	/// The overlay asks for the buddy list at most once in this many milliseconds, a typed <c>/buddy</c> included: each ask spends the connection's shared
	/// request allowance, and an ask of the overlay right after a typed one would keep the typed one's answer off the chat lines. An ask that got no
	/// answer in that time is taken as dropped.
	/// </summary>
	internal int BuddyAskIntervalMilliseconds { get; set; } = 5000;
	/// <summary>The players this chat connection ignores, from the server's answers (it keeps the list per connection).</summary>
	public IReadOnlyCollection<string> Ignored => ignored;
	private readonly HashSet<string> ignored = new( StringComparer.OrdinalIgnoreCase );
	/// <summary>Whether the player's earmuffs are on, from the server's last answer (off on a new connection).</summary>
	public bool Earmuffs { get; private set; }
	/// <summary>The park rooms the server took a vote for from this login, or refused one for (the player's own park, a vote already given today).</summary>
	private readonly HashSet<string> votedRooms = new( StringComparer.Ordinal );
	/// <summary>The server refused a vote for the day's limit, which counts the votes for every park.</summary>
	private bool voteLimitReached;
	/// <summary>The chat connection is open.</summary>
	public bool ChatOpen => Chat != null;
	/// <summary>The player is in a published park's room that the server has not taken a vote for from this login: the chat overlay's vote button works.</summary>
	public bool CanVote => Chat != null && CurrentRoom.StartsWith( ChatProtocol.ParkRoomPrefix, StringComparison.Ordinal ) && !votedRooms.Contains( CurrentRoom ) && !voteLimitReached;
	public bool IsLoggedIn => Client?.Session != null;

	/// <summary>
	/// [EXT:online-coop-session] Who is logged in where: the server and the account, null while logged out. The lobby's invitations and the open list of shared parks belong to
	/// it (C2d-3b): another login has neither.
	/// </summary>
	internal (Uri Server, string Account)? LoginIdentity => Client is { Session: { } session } client ? (client.ServerUrl, session.Name) : null;
	string? IChatPanelSource.PlayerName => Settings.PlayerName;
	object? IPresenceSession.Connection => Chat;
	IReadOnlyList<string> IChatPanelSource.ChatLines => ChatLines;
	/// <summary>A two-step setup waiting for its first code (after registering, or a login after an operator reset).</summary>
	public TotpEnrollment? Enrollment { get; internal set; }
	/// <summary>The last login asked for an authenticator code (missing or wrong).</summary>
	public bool CodeRequested { get; private set; }

	public void Pump()
	{
		// a join the server dropped (over the chat rate limit) is asked again
		if ( joinPending && Chat is { } chat && joinSentAt is { } sent && Environment.TickCount64 - sent >= JoinAnswerMilliseconds && requestedRoom is { } room )
			SendJoin( chat, room );
		while ( mainThread.TryDequeue( out var item ) )
		{
			if ( disposed || item.Generation != generation )
			{
				item.Discard?.Invoke();
				continue;
			}
			try { item.Action(); }
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or ArgumentException or InvalidOperationException )
			{ Status = exception.Message; }
		}
	}

	/// <summary>
	/// Runs <paramref name="action"/> on the game thread at the next <see cref="Pump"/>, unless the session went offline or changed server since the background
	/// work that posts it began; then <paramref name="discard"/> runs instead.
	/// </summary>
	internal void Post( Action action, Action? discard = null )
	{
		lock ( mainThread )
		{
			var origin = operationGeneration.Value ?? generation;
			if ( !disposed && origin == generation )
			{
				mainThread.Enqueue( (origin, action, discard) );
				return;
			}
		}
		discard?.Invoke();
	}

	/// <summary>Runs <paramref name="work"/> on a background task whose <see cref="Post"/>s belong to the current server and login; it does not take the operation slot.</summary>
	internal void Background( Func<Task> work )
	{
		if ( disposed )
			return;
		var startedGeneration = generation;
		_ = Task.Run( async () =>
		{
			operationGeneration.Value = startedGeneration;
			try
			{
				await work();
			}
			catch ( Exception exception )
			{
				Log.Warning( $"Online background work failed: {exception}" );
			}
		} );
	}

	/// <summary>
	/// The original's connection error in the game's language; the .NET reason (English, whatever the language) only goes to the log.
	/// A request that timed out (<see cref="TaskCanceledException"/> from the HTTP client) or whose response broke off (<see cref="HttpIOException"/>)
	/// is a failed connection too.
	/// </summary>
	private static string ConnectError( Exception exception )
	{
		Log?.Info( $"Online connection failed: {exception.Message}" );
		return OnlineStrings.Error( OnlineStrings.ErrorConnect, "Error: Couldn't connect to server" );
	}

	/// <summary>The original's chat connection error ([DATA:Language/English/ERRORMSG.str:69]) in the game's language; the .NET reason only goes to the log.</summary>
	private static string ChatConnectError( Exception exception )
	{
		Log?.Info( $"Chat connection failed: {exception.Message}" );
		return OnlineStrings.Error( OnlineStrings.ErrorChatConnect, "Error: Couldn't connect to chat server" );
	}

	/// <summary>Runs network work in the background and reports the outcome in <see cref="Status"/>.</summary>
	public void Run( Func<Task<string?>> work )
	{
		if ( disposed || Busy > 0 )
			return;
		Busy++;
		var startedGeneration = generation;
		Task.Run( async () =>
		{
			if ( disposed || startedGeneration != generation )
				return;
			operationGeneration.Value = startedGeneration;
			string? message;
			try { message = await work(); }
			catch ( OnlineException exception ) { message = exception.Message; }
			catch ( Exception exception ) when ( exception is HttpRequestException or HttpIOException or TaskCanceledException ) { message = ConnectError( exception ); }
			catch ( Exception exception ) when ( exception is InvalidDataException or IOException or ArgumentException or InvalidOperationException or System.Net.WebSockets.WebSocketException or ObjectDisposedException )
			{ message = exception.Message; }
			// Anything else must still end the operation, or every online button stays disabled.
			catch ( Exception exception )
			{
				Log.Warning( $"Online operation failed: {exception}" );
				message = exception.Message;
			}
			Post( () =>
			{
				Busy--;
				if ( message != null )
					Status = message;
			} );
		} );
	}

	/// <summary>Opts in to a server (validated http/https URL, see <see cref="ParseServerUrl"/>) and remembers it.</summary>
	public void UseServer( string url, string? playerName )
	{
		if ( disposed )
			throw new ObjectDisposedException( nameof( OnlineSession ) );
		var server = ParseServerUrl( url );
		Disconnect();
		Client = new OnlineClient( server );
		Settings = new OnlineSettings( server.ToString(), playerName ?? Settings.PlayerName );
		Folders.SaveSettings( Settings );
	}

	/// <summary>
	/// The server of a typed address. A missing, malformed or refused address throws an <see cref="ArgumentException"/> whose message is the
	/// reason in the game's language, for the status line: the .NET messages ("Invalid URI: The URI is empty.") are English whatever the
	/// language. An empty address is never replaced by a default here: the player may have cleared it on purpose, and logging in sends a password.
	/// </summary>
	public static Uri ParseServerUrl( string url )
	{
		var text = url.Trim();
		if ( text.Length == 0 )
			throw new ArgumentException( OnlineStrings.Get( OnlineLabel.EnterServerAddress ) );
		if ( !Uri.TryCreate( text, UriKind.Absolute, out var uri ) )
			throw new ArgumentException( OnlineStrings.Get( OnlineLabel.InvalidServerAddress ) );
		try { return OnlineClient.ValidateServerUrl( uri ); }
		catch ( ArgumentException ) { throw new ArgumentException( OnlineStrings.Get( OnlineLabel.InvalidServerAddress ) ); }
	}

	/// <summary>Tests: the session uses <paramref name="client"/> (a stub transport) instead of opening one from a URL.</summary>
	internal void UseClient( OnlineClient client, string? playerName )
	{
		if ( disposed )
			throw new ObjectDisposedException( nameof( OnlineSession ) );
		Disconnect();
		Client = client;
		Settings = new OnlineSettings( client.ServerUrl.ToString(), playerName ?? Settings.PlayerName );
		Folders.SaveSettings( Settings );
	}

	/// <summary>Registers; on a server with two-step login this leaves an <see cref="Enrollment"/> to confirm with a code.</summary>
	public void Register( string name, string password ) => WithClient( async client =>
	{
		var enrollment = await client.RegisterAsync( name, password );
		if ( enrollment != null )
			return Enroll( enrollment );
		return await LoginCore( client, name, password, null );
	} );

	public void Login( string name, string password, string? code = null ) => WithClient( client => LoginCore( client, name, password, code ) );

	/// <summary>Finishes <see cref="Enrollment"/> with the first code from the authenticator app.</summary>
	public void ConfirmEnrollment( string code, string? emailCode = null ) => WithClient( async client =>
	{
		var enrollment = Enrollment ?? throw new InvalidOperationException( "No two-step setup is waiting." );
		try
		{
			var session = await client.ConfirmEnrollmentAsync( enrollment, code, emailCode );
			Post( () => Enrollment = null );
			return LoggedIn( session );
		}
		catch ( OnlineException exception ) when ( exception.Reason == null && exception.Status == System.Net.HttpStatusCode.BadRequest )
		{
			// The setup expired or was replaced: start again from the login dialog.
			Post( () => Enrollment = null );
			throw;
		}
	} );

	public void CancelEnrollment() => Enrollment = null;

	/// <summary>The game language, sent with requests whose answer is an e-mail.</summary>
	private static string? MailLanguage => GameLanguage.IsSelected ? GameLanguage.Current.Name : null;

	/// <summary>[EXT:online-email] Sends a code to the address of the waiting setup's new account.</summary>
	public void SendEnrollmentEmail( string email ) => WithClient( async client =>
	{
		var enrollment = Enrollment ?? throw new InvalidOperationException( "No two-step setup is waiting." );
		await client.SendEnrollmentEmailAsync( enrollment, email, MailLanguage );
		return OnlineStrings.Get( OnlineLabel.EmailCodeSent );
	} );

	/// <summary>[EXT:online-email] Asks the server to send a recovery code for a player name or e-mail address.</summary>
	public void RequestRecovery( string nameOrEmail ) => WithClient( async client =>
	{
		await client.RequestRecoveryAsync( nameOrEmail, MailLanguage );
		return OnlineStrings.Get( OnlineLabel.RecoveryCodeSent );
	} );

	/// <summary>[EXT:online-email] Sets a new password with the recovery code; then log in as usual.</summary>
	public void ConfirmRecovery( string nameOrEmail, string code, string password, bool resetTwoStep ) => WithClient( async client =>
	{
		await client.ConfirmRecoveryAsync( nameOrEmail, code, password, resetTwoStep, MailLanguage );
		Post( () => PasswordReset = true );
		return OnlineStrings.Get( OnlineLabel.PasswordChanged );
	} );

	/// <summary>A recovery finished: the screen returns to the login dialog.</summary>
	public bool PasswordReset { get; internal set; }

	private string? Enroll( TotpEnrollment enrollment )
	{
		Post( () => { Enrollment = enrollment; CodeRequested = false; } );
		return OnlineStrings.Get( OnlineLabel.TwoStepLogin );
	}

	private async Task<string?> LoginCore( OnlineClient client, string name, string password, string? code )
	{
		LoginResult result;
		try { result = await client.LoginAsync( name, password, code ); }
		catch ( OnlineException exception ) when ( exception.Reason is ApiErrorReasons.TotpRequired or ApiErrorReasons.TotpInvalid )
		{
			Post( () => CodeRequested = true );
			throw;
		}
		if ( result.Enrollment != null )
			return Enroll( result.Enrollment );
		return LoggedIn( result.Session! );
	}

	private string LoggedIn( SessionToken session )
	{
		Post( () =>
		{
			CodeRequested = false;
			Settings = Settings with { PlayerName = session.Name };
			Folders.SaveSettings( Settings );
		} );
		return $"{session.Name} OK";
	}

	public void Disconnect()
	{
		var discarded = new List<Action>();
		lock ( mainThread )
		{
			generation++;
			while ( mainThread.TryDequeue( out var pending ) )
				if ( pending.Discard != null )
					discarded.Add( pending.Discard );
		}
		foreach ( var cleanup in discarded )
			cleanup();
		Busy = 0;
		// A setup or a code request belongs to the server it came from.
		Enrollment = null;
		CodeRequested = false;
		chatCancel?.Cancel();
		chatCancel?.Dispose();
		chatCancel = null;
		Chat?.Dispose();
		Chat = null;
		chatConnecting = false;
		Interlocked.Increment( ref chatEpoch );
		requestedRoom = null;
		joinPending = false;
		joinSentAt = null;
		RefusedRoom = null;
		// a send already queued finds no place and clears its own flag
		lock ( moveLock )
			latestMove = null;
		ServerProtocol = null;
		protocolCurrent = false;
		fetchingServerInfo = false;
		coopRoom = null;
		coopJoinWaits = false;
		// a version read for the server just left must not answer the next one
		serverInfoEpoch++;
		Client?.Dispose();
		Client = null;
		// [EXT:online-coop-session] the co-op link belongs to the login (closed once the login is gone, so a handler of its close cannot open it on the old one); a
		// shared park it holds is left first, on its socket, which outlives the login's HTTP client (C2d-2: a host's logout or server switch ends the park for its
		// guests at once, not after the away limit), and one it cannot leave now is left at the next open
		Coop.Close( LeftOnLogout );
		Parks = Array.Empty<ParkSummary>();
		Inbox = Array.Empty<PostcardSummary>();
		News = null;
		Cities = Array.Empty<CityInfo>();
		ChatParks = Array.Empty<ChatParkInfo>();
		CityParks = null;
		CurrentRoom = ChatProtocol.LobbyRoom;
		RoomPlayers = Array.Empty<string>();
		positions.Clear();
		jumps.Clear();
		Profile = null;
		ChatLines.Clear();
		buddies.Clear();
		quietBuddyAnswers = false;
		buddyAnswerPending = false;
		buddyAskedAt = null;
		ignored.Clear();
		Earmuffs = false;
		votedRooms.Clear();
		voteLimitReached = false;
	}

	/// <summary>
	/// Deletes the logged-in account and everything the server keeps about it (docs/SERVER.md), then
	/// goes offline as Log out does. The server asks for the name and password again.
	/// </summary>
	public void DeleteAccount( string name, string password ) => WithClient( async client =>
	{
		await client.DeleteAccountAsync( name, password );
		Post( () =>
		{
			Disconnect();
			Settings = Settings with { PlayerName = null };
			Folders.SaveSettings( Settings );
			Status = OnlineStrings.Get( OnlineLabel.AccountDeleted );
		} );
		return null;
	} );

	private OnlineClient Require() => Client ?? throw new InvalidOperationException( OnlineStrings.Get( OnlineLabel.OnlineOff ) );

	private void WithClient( Func<OnlineClient, Task<string?>> work )
	{
		var client = Require();
		Run( () => work( client ) );
	}

	/// <summary>Parks of the city whose lobby is open, most votes first; null while they are being fetched.</summary>
	public IReadOnlyList<ParkSummary>? CityParks { get; private set; }

	/// <summary>Fetches a city's parks for its lobby (the top ten become islands, the rest are listed).</summary>
	public void FetchCityParks( int cityId ) => WithClient( async client =>
	{
		// [DATA:Language/English/UITEXT.str:249] the original showed "Getting Top 10 Park List" while this ran
		Post( () => { CityParks = null; Status = OnlineStrings.Ui( OnlineStrings.GettingTop10ParkList, "Getting Top 10 Park List" ); } );
		var list = await client.ListParksAsync( sort: "votes", cityId: cityId );
		Post( () => CityParks = list.Parks );
		return list.Parks.Count == 0 ? OnlineStrings.Ui( OnlineStrings.NoSearchResults, "No search results" ) : null;
	} );

	/// <summary>Fetches the cities for the globe; no login needed. Older servers have none.</summary>
	public void FetchCities() => WithClient( async client =>
	{
		// [DATA:Language/English/UITEXT.str:248] the original showed "Getting City List" while this ran
		Post( () => Status = OnlineStrings.Ui( OnlineStrings.GettingCityList, "Getting City List" ) );
		var cities = await client.GetCitiesAsync();
		Post( () => Cities = cities?.Cities ?? Array.Empty<CityInfo>() );
		return "";
	} );

	/// <summary>
	/// Fetches the chat parks with their player counts for the globe's satellites (the original's "Chat Satellite
	/// List", UITEXT 252); no login needed. Older servers have none.
	/// </summary>
	public void FetchChatParks() => WithClient( async client =>
	{
		Post( () => Status = OnlineStrings.Ui( OnlineStrings.GettingChatSatelliteList, "Getting Chat Satellite List" ) );
		var parks = await client.GetChatParksAsync();
		Post( () => ChatParks = parks?.ChatParks ?? Array.Empty<ChatParkInfo>() );
		return "";
	} );

	/// <summary>
	/// Fetches the chat parks' scenes for a satellite's park details (needs a login): <paramref name="onFetched"/> gets them on the game's thread, or null
	/// from a server without them (an older OpenTPW server).
	/// </summary>
	public void FetchChatParkScenes( Action<IReadOnlyList<ChatParkScene>?> onFetched ) => WithClient( async client =>
	{
		Post( () => Status = OnlineStrings.Ui( OnlineStrings.GettingChatSatelliteList, "Getting Chat Satellite List" ) );
		var scenes = await client.GetChatParkScenesAsync();
		Post( () => onFetched( scenes?.Scenes ) );
		return "";
	} );

	/// <summary>Fetches Game News and System News; no login needed.</summary>
	public void FetchNews() => WithClient( async client =>
	{
		// [DATA:Language/English/UITEXT.str:245/246] "Getting Game News" / "Getting System News"
		Post( () => Status = OnlineStrings.Ui( OnlineStrings.GettingGameNews, "Getting Game News" ) );
		var news = await client.GetNewsAsync();
		Post( () => News = news );
		return news == null || (news.Game.Length == 0 && news.System.Length == 0) ? OnlineStrings.Get( OnlineLabel.NoNews ) : null;
	} );

	public void RefreshParks( string? search, string sort = "recent", int? cityId = null, string? author = null ) => WithClient( async client =>
	{
		// [DATA:Language/English/UITEXT.str:250/251] "Getting All Park List" / "Getting Player's Park List"
		Post( () => Status = OnlineStrings.Ui( search == null && author == null ? OnlineStrings.GettingAllParkList : OnlineStrings.GettingPlayersParkList,
			search == null && author == null ? "Getting All Park List" : "Getting Player's Park List" ) );
		var list = await client.ListParksAsync( search, sort, author, cityId: cityId );
		Post( () => Parks = list.Parks );
		return list.Parks.Count == 0 ? OnlineStrings.Ui( OnlineStrings.NoSearchResults, "No search results" ) : null;
	} );

	/// <summary>Unpublishes one of the player's own parks (the owner action of the original park details).</summary>
	public void Unpublish( ParkSummary park ) => WithClient( async client =>
	{
		await client.UnpublishParkAsync( park.Id );
		Post( () => Parks = Parks.Where( item => item.Id != park.Id ).ToList() );
		return null;
	} );

	/// <summary>Publishes <paramref name="package"/>; <paramref name="onPublished"/> runs on the game's thread once the server has the park (the Publish Park form closes then).</summary>
	public void Publish( ParkPackage package, bool showOnWebsite = false, int cityId = 0, Action? onPublished = null ) => WithClient( async client =>
	{
		Post( () => Status = OnlineStrings.Ui( OnlineStrings.PublishingPark, "Publishing Park" ) );
		await client.UploadParkAsync( package, showOnWebsite: showOnWebsite, cityId: cityId );
		if ( onPublished != null )
			Post( onPublished );
		return OnlineStrings.Ui( OnlineStrings.ParkPublished, "PARK PUBLISHED" ).Replace( "\n\n", " " );
	} );

	/// <summary>Downloads a park into the visited folder and records the visit; returns via status the file to visit.</summary>
	public void Download( ParkSummary park, Action<string> onDownloaded ) => WithClient( async client =>
	{
		Post( () => Status = OnlineStrings.Ui( OnlineStrings.DownloadingPark, "Downloading Park" ) );
		var package = await client.DownloadParkAsync( park.Id );
		var path = Path.Combine( Folders.Visited, park.Id + ParkPackage.FileExtension );
		package.Save( path );
		await client.RecordVisitAsync( park.Id );
		Post( () => onDownloaded( path ) );
		return OnlineStrings.Get( OnlineLabel.SavedTo ) + path;
	} );

	public void Vote( ParkSummary park ) => WithClient( async client =>
	{
		var result = await client.VoteAsync( park.Id );
		return $"{OnlineStrings.Ui( OnlineStrings.NumberOfVotes, "Number of Votes" )}: {result.Votes}";
	} );

	public void Report( string kind, string target, string reason ) => WithClient( async client =>
	{
		await client.ReportAsync( kind, target, reason );
		return OnlineStrings.Get( OnlineLabel.Report ) + " OK";
	} );

	/// <summary>
	/// Sends the outbox postcards ("Sending mails from outbox", UITEXT 313) and moves sent cards to the sent folder.
	/// The original's outbox sent the cards marked for sending (0x101862ac); with <paramref name="files"/> empty every
	/// card goes out, which is what a player without marks expects.
	/// </summary>
	public void SendOutbox( IReadOnlyList<string>? files = null ) => WithClient( async client =>
	{
		var sent = 0;
		foreach ( var (file, card, error) in Folders.List( Folders.Outbox ) )
		{
			if ( card == null || (files != null && files.Count > 0 && !files.Contains( file )) )
				continue;
			var result = await client.SendPostcardAsync( card );
			var delivered = result.DeliveredTo.Select( PostcardRecipient.Key ).ToHashSet( StringComparer.Ordinal );
			var remaining = card.Manifest.To.Where( recipient => !delivered.Contains( PostcardRecipient.Key( recipient ) ) ).ToArray();
			if ( remaining.Length == 0 )
				File.Move( file, Path.Combine( Folders.Sent, Path.GetFileName( file ) ), true );
			else
			{
				var confirmed = card.Manifest.To.Where( recipient => delivered.Contains( PostcardRecipient.Key( recipient ) ) ).ToArray();
				if ( confirmed.Length > 0 )
				{
					// Stable subset names make repeating a local save safe; never mark a failed recipient as sent.
					var suffix = ParkPackage.Hash( StrictJson.Serialize( card.Manifest with { To = confirmed } ) )[..16];
					Folders.Store( Folders.Sent, card with { Manifest = card.Manifest with { To = confirmed } }, $"sent-{card.Id:N}-{suffix}" );
					(card with { Manifest = card.Manifest with { To = remaining } }).Save( file );
				}
				throw new InvalidDataException( result.Error ?? "Some postcard recipients could not be reached; they remain in the outbox." );
			}
			sent++;
		}
		return $"{OnlineStrings.Ui( OnlineStrings.SendingOutbox, "Sending mails from outbox" )} {sent}";
	} );

	/// <summary>Downloads server postcards into the local inbox folder and removes them from the server.</summary>
	public void FetchInbox() => WithClient( async client =>
	{
		var inbox = await client.GetInboxAsync();
		var failed = 0;
		foreach ( var summary in inbox.Postcards )
		{
			// Each card is stored under its server delivery id, not the sender-chosen card id, so two cards
			// with one card id cannot block each other; a card that fails stays on the server and the rest still arrive.
			try
			{
				var card = await client.DownloadPostcardAsync( summary.Id );
				Folders.Store( Folders.Inbox, card, $"inbox-{summary.Id}" );
				await client.DeletePostcardAsync( summary.Id );
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or OnlineException )
			{
				failed++;
			}
		}
		Post( () => Inbox = inbox.Postcards );
		return $"{OnlineStrings.Get( OnlineLabel.Inbox )}: {inbox.Postcards.Count}" + (failed > 0 ? $" (!{failed})" : "");
	} );

	/// <summary>
	/// Opens the chat connection, once: while it opens or is open a second call does nothing. When it is open the room <see cref="JoinRoom"/>
	/// asked for meanwhile is joined. It does not take the operation slot (<see cref="ChatBusy"/>).
	/// </summary>
	public void ConnectChat()
	{
		var client = Require();
		if ( disposed || Chat != null || chatConnecting )
			return;
		chatConnecting = true;
		var startedGeneration = generation;
		var epoch = chatEpoch;
		_ = Task.Run( async () =>
		{
			operationGeneration.Value = startedGeneration;
			ChatConnection chat;
			try
			{
				chat = await ChatConnector( client );
			}
			catch ( Exception exception )
			{
				var message = exception is HttpRequestException or System.Net.WebSockets.WebSocketException or IOException or TaskCanceledException
					? ChatConnectError( exception ) : exception.Message;
				if ( exception is not (OnlineException or HttpRequestException or System.Net.WebSockets.WebSocketException or IOException
					or InvalidDataException or TaskCanceledException or InvalidOperationException or ObjectDisposedException) )
					Log.Warning( $"Chat connection failed: {exception}" );
				Post( () =>
				{
					if ( epoch != chatEpoch )
						return;
					chatConnecting = false;
					Status = message;
					// [EXT:online-coop-session] the shared park's chat is tried again while the session owns it
					RetryCoopChat();
				} );
				return;
			}
			var cancel = new CancellationTokenSource();
			Post( () =>
			{
				// the chat was closed while this connection opened: it is not used
				if ( epoch != chatEpoch )
				{
					cancel.Dispose();
					chat.Dispose();
					return;
				}
				chatConnecting = false;
				Install( chat, cancel );
			}, () => { cancel.Dispose(); chat.Dispose(); } );
		} );
	}

	/// <summary>
	/// Makes <paramref name="chat"/> the session's connection and starts reading it; a new connection starts in the Online World, so a room asked for
	/// and not yet confirmed is joined now. Runs on the game thread.
	/// </summary>
	private void Install( ChatConnection chat, CancellationTokenSource cancel )
	{
		if ( Chat != null )
		{
			cancel.Dispose();
			chat.Dispose();
			return;
		}
		Chat = chat;
		chatCancel = cancel;
		coopChatFailures = 0;
		// the server keeps the ignore list and the earmuffs per connection: a new one starts without them
		ignored.Clear();
		Earmuffs = false;
		// an ask on the old connection gets no answer on this one
		buddyAskedAt = null;
		// the server behind a new connection may be another version (a restart): movements carry no mode until its version is read again, and a hide waits for that read
		protocolCurrent = false;
		BeginServerInfo( refresh: true, attempt: 0 );
		_ = Task.Run( () => ReceiveLoopAsync( chat, cancel.Token ) );
		if ( joinPending && requestedRoom is { } room )
		{
			if ( room == ChatProtocol.LobbyRoom )
				joinPending = false;
			// [EXT:online-coop-session] a shared park's room waits for this connection's server version (FollowCoopRoom)
			else if ( ChatProtocol.CoopSessionOf( room ) != null )
				coopJoinWaits = true;
			else
				SendJoin( chat, room );
		}
		// [EXT:online-chat-person-flags] the overlay colours the buddies and offers the right name popup from the start, so the list is asked at once
		AskBuddies();
	}

	private void SendJoin( ChatConnection chat, string room )
	{
		joinSentAt = Environment.TickCount64;
		SendOnChat( chat, ChatRequest.ForJoin( room ) );
	}

	/// <summary>
	/// Sends <paramref name="request"/> after every chat request made before it, without taking the operation slot. A failed send is dropped: a lost
	/// connection ends the receive loop, which reconnects and rejoins the room.
	/// </summary>
	private void SendOnChat( ChatConnection chat, ChatRequest request ) => Chain( () => chat.SendAsync( request ) );

	/// <summary>Runs <paramref name="send"/> after every chat send queued before it (one sender at a time: the connection's send lock has no timeout).</summary>
	private void Chain( Func<Task> send )
	{
		Interlocked.Increment( ref pendingChatSends );
		chatSends = chatSends.ContinueWith( async _ =>
		{
			try
			{
				await send();
			}
			catch ( Exception exception ) when ( exception is System.Net.WebSockets.WebSocketException or IOException or ObjectDisposedException
				or OperationCanceledException or InvalidOperationException )
			{
			}
			finally
			{
				Interlocked.Decrement( ref pendingChatSends );
			}
		}, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default ).Unwrap();
	}

	private async Task ReceiveLoopAsync( ChatConnection chat, CancellationToken cancel )
	{
		try
		{
			while ( !cancel.IsCancellationRequested )
			{
				var item = await chat.ReceiveAsync( cancel );
				if ( item == null )
					break;
				var line = ChatFormatter.Format( item, OnlineStrings.ChatCommands, RoomName, OnlineStrings.Get );
				Post( () =>
				{
					if ( Chat != chat )
						return;
					if ( buddyAnswerPending && IsBuddyAnswer( item ) )
					{
						// the first answer to an ask replaces the list, so a buddy removed elsewhere drops out
						buddies.Clear();
						buddyAnswerPending = false;
					}
					else if ( buddyAnswerPending && item.Kind == ChatEventKind.System && item.Text == "rate-limited" )
						// the server dropped a request, perhaps the ask: the list stands as it was
						buddyAnswerPending = false;
					if ( item.Kind == ChatEventKind.Room && item.Room != null )
					{
						if ( item.Room != CurrentRoom )
						{
							// The new room's places arrive with the player's first movement there; the old room's
							// places mean nothing in it.
							positions.Clear();
							jumps.Clear();
						}
						else if ( item.Args is { } roster && !(roster.Count > 0 && roster[^1].StartsWith( "+", StringComparison.Ordinal )) )
						{
							// A roster of the room the player is already in (someone came or went) keeps the places of those who
							// stay; a roster cut short with "+N" (ChatHub.RosterEvent) does not tell who left.
							foreach ( var name in positions.Keys.Concat( jumps.Keys ).Where( name => !roster.Contains( name, StringComparer.OrdinalIgnoreCase ) ).ToArray() )
							{
								positions.Remove( name );
								jumps.Remove( name );
							}
						}
						CurrentRoom = item.Room;
						RoomPlayers = item.Args ?? Array.Empty<string>();
						if ( joinPending && item.Room == requestedRoom )
						{
							joinPending = false;
							joinSentAt = null;
						}
					}
					// a NoPark answer to another command (a vote outside a park) is not the join's
					else if ( item.Kind == ChatEventKind.Notice && item.Notice == (int)ChatNotice.NoPark && joinPending && joinSentAt is { } asked
						&& Environment.TickCount64 - asked < JoinAnswerMilliseconds )
					{
						// The park's room was refused (it was unpublished): the player stays where they are.
						// A place waiting for the refused room (for example a hide awaiting its version) is not this room's.
						lock ( moveLock )
							latestMove = null;
						RefusedRoom = requestedRoom;
						joinPending = false;
						joinSentAt = null;
						requestedRoom = CurrentRoom;
					}
					else if ( item.Kind == ChatEventKind.Move && item.From != null )
						Moved( item.From, (item.X ?? 0, item.Y ?? 0), item.Mode );
					else if ( item.Kind == ChatEventKind.Notice )
						Remember( (ChatNotice)(item.Notice ?? 0), item.Args );
					if ( item.Kind != ChatEventKind.Move && !(quietBuddyAnswers && IsBuddyAnswer( item )) )
						AddChatLine( line );
				} );
			}
		}
		catch ( Exception exception ) when ( exception is System.Net.WebSockets.WebSocketException or OperationCanceledException or InvalidDataException or ObjectDisposedException )
		{
		}
		// A server restart (a deploy) or a dropped connection is not the player leaving: reconnect to the same room.
		var lost = !cancel.IsCancellationRequested && chat.CloseStatus is null or ChatProtocol.RestartingCloseStatus or (int)System.Net.WebSockets.WebSocketCloseStatus.EndpointUnavailable;
		Post( () =>
		{
			chat.Dispose();
			if ( Chat == chat )
			{
				Chat = null;
				chatCancel?.Dispose();
				chatCancel = null;
				// a place held for the connection that just died is not this one's
				lock ( moveLock )
					latestMove = null;
				if ( lost && IsLoggedIn && Client is { } client )
				{
					// The new connection returns to the room the player was in, or to the one they were on their way to.
					if ( !joinPending )
					{
						requestedRoom = CurrentRoom;
						joinPending = CurrentRoom != ChatProtocol.LobbyRoom;
					}
					// the new connection sends the join itself (Install)
					joinSentAt = null;
					ReconnectChat( client );
				}
			}
		} );
	}

	/// <summary>Keeps what the chat overlay shows of the server's answers: the buddy list, the ignored players, the earmuffs and the votes.</summary>
	private void Remember( ChatNotice notice, IReadOnlyList<string>? args )
	{
		var name = args is { Count: >= 1 } ? args[0] : null;
		switch ( notice )
		{
			case ChatNotice.YourBuddyOnline or ChatNotice.YourBuddyOffline or ChatNotice.YouMakeBuddy when name != null:
				// a /buddy answer, or a buddy just added: the overlay's buddy tab lists them
				var online = notice == ChatNotice.YourBuddyOnline || (notice == ChatNotice.YouMakeBuddy && RoomPlayers.Contains( name, StringComparer.OrdinalIgnoreCase ));
				buddies.RemoveAll( entry => entry.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) );
				buddies.Add( (name, online) );
				break;
			case ChatNotice.YourBuddyRemoved when name != null:
				buddies.RemoveAll( entry => entry.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) );
				break;
			case ChatNotice.YouIgnore when name != null:
				ignored.Add( name );
				break;
			case ChatNotice.YouNoLongerIgnore when name != null:
				ignored.Remove( name );
				break;
			case ChatNotice.EarmuffsOn or ChatNotice.EarmuffsOff:
				Earmuffs = notice == ChatNotice.EarmuffsOn;
				break;
			case ChatNotice.VotedForPark or ChatNotice.VoteFailed when CurrentRoom.StartsWith( ChatProtocol.ParkRoomPrefix, StringComparison.Ordinal ):
				// a refusal (the player's own park, a vote already given today) would be refused again
				votedRooms.Add( CurrentRoom );
				break;
			case ChatNotice.VoteLimitReached:
				voteLimitReached = true;
				break;
		}
	}

	/// <summary>A relayed movement of <paramref name="name"/>: their place, none while their figure is hidden (they ride a ride), and a jump for a teleport or an appearance.</summary>
	// [EXT:online-ride-hiding] a hidden player's figure leaves the room's places until their next movement, which shows it again
	private void Moved( string name, (float X, float Y) place, ChatMoveMode? mode )
	{
		if ( mode == ChatMoveMode.Hide )
		{
			positions.Remove( name );
			return;
		}
		positions[name] = place;
		if ( mode is ChatMoveMode.Teleport or ChatMoveMode.Unhide )
			jumps[name] = jumps.GetValueOrDefault( name ) + 1;
	}

	/// <summary>An answer to <c>/buddy</c> without a name: a buddy online or offline, or that there are none.</summary>
	private static bool IsBuddyAnswer( ChatEvent item ) =>
		item.Kind == ChatEventKind.Notice && item.Notice is (int)ChatNotice.YourBuddyOnline or (int)ChatNotice.YourBuddyOffline
		|| item.Kind == ChatEventKind.System && item.Text == "buddy-list";

	/// <summary>Waits between attempts to reconnect the chat after a server restart.</summary>
	internal static readonly TimeSpan[] ReconnectDelays = { TimeSpan.FromSeconds( 1 ), TimeSpan.FromSeconds( 2 ), TimeSpan.FromSeconds( 4 ), TimeSpan.FromSeconds( 8 ), TimeSpan.FromSeconds( 15 ), TimeSpan.FromSeconds( 30 ) };

	private void ReconnectChat( OnlineClient client )
	{
		var startedGeneration = generation;
		var epoch = chatEpoch;
		chatConnecting = true;
		_ = Task.Run( async () =>
		{
			operationGeneration.Value = startedGeneration;
			foreach ( var delay in ReconnectDelays )
			{
				await Task.Delay( delay );
				if ( disposed || startedGeneration != generation || Client != client || !IsLoggedIn || epoch != Volatile.Read( ref chatEpoch ) )
					break;
				ChatConnection chat;
				try
				{
					chat = await client.ConnectChatAsync();
				}
				catch ( Exception exception )
				{
					if ( exception is not (OnlineException or HttpRequestException or System.Net.WebSockets.WebSocketException or IOException or InvalidDataException
						or TaskCanceledException or InvalidOperationException or ObjectDisposedException) )
						Log.Warning( $"Chat reconnection failed: {exception}" );
					continue;
				}
				var cancel = new CancellationTokenSource();
				Post( () =>
				{
					// the chat was closed during the wait (the player left the visit that opened it): it stays closed
					if ( epoch != chatEpoch )
					{
						cancel.Dispose();
						chat.Dispose();
						return;
					}
					chatConnecting = false;
					var installed = Chat == null;
					// Install joins the room the player is to return to.
					Install( chat, cancel );
					if ( installed )
						AddChatLine( OnlineStrings.Get( OnlineLabel.ChatReconnected ) );
				}, () => { cancel.Dispose(); chat.Dispose(); } );
				return;
			}
			Post( () =>
			{
				if ( epoch == chatEpoch )
					chatConnecting = false;
			} );
		} );
	}

	/// <summary>The name of the room the player is in: the Online World, a chat park or a published park.</summary>
	public string CurrentRoomName => RoomName( CurrentRoom );

	private string RoomName( string room ) => room == ChatProtocol.LobbyRoom || room.Length == 0
		? OnlineStrings.Ui( OnlineStrings.OnlineWorld, "Online World" )
		// [EXT:online-coop-session] a shared park's room: its session's title, which its members know
		: ChatProtocol.CoopSessionOf( room ) is { } sessionId
			? Coop.Session is { } shared && shared.Id == sessionId ? shared.Title
				: UI.Original.SupplementaryStrings.Get( UI.Original.OpenTpwText.SharedPark, GameLanguage.IsSelected ? GameLanguage.Current.Name : GameLanguage.DefaultLanguage )
		: ChatParks.FirstOrDefault( park => ChatProtocol.SatelliteRoom( park.Id ) == room )?.Name
			?? Parks.FirstOrDefault( park => ChatProtocol.ParkRoom( park.Id ) == room )?.Name ?? room;

	public void AddChatLine( string line )
	{
		ChatLines.Add( line );
		if ( ChatLines.Count > MaximumChatLines )
			ChatLines.RemoveRange( 0, ChatLines.Count - MaximumChatLines );
	}

	/// <summary>Parses a typed line with the language's command words and sends it.</summary>
	public void SendChatLine( string line )
	{
		var chat = Chat;
		if ( chat == null || string.IsNullOrWhiteSpace( line ) )
			return;
		if ( line.Length > ChatCommands.MaximumLineLength )
			line = line[..ChatCommands.MaximumLineLength];
		if ( !Commands.TryParse( line, out var command, out var argument ) )
		{
			AddChatLine( OnlineStrings.ChatNotice( (int)ChatNotice.NoSuchCommand, null ) );
			return;
		}
		// [EXT:online-coop-session] in a shared park, what is said to a room goes to its room only (CommandRequest)
		if ( CommandRequest( command, argument ) is not { } request )
			return;
		if ( command is ChatCommand.Buddy or ChatCommand.Friend && ChatCommands.SplitName( argument ).Name == null )
		{
			// a typed /buddy lists the buddies in the chat lines, and its answer replaces the list as an ask's does
			buddyAnswerPending = true;
			quietBuddyAnswers = false;
			buddyAskedAt = Environment.TickCount64;
		}
		SendOnChat( chat, request );
	}

	/// <summary>
	/// Goes to a room: the Online World, a chat park or a published park. Asked before the chat connection is open (a visit asks as it starts),
	/// the room is joined once it is; a reconnection returns to it too.
	/// </summary>
	public void JoinRoom( string room )
	{
		if ( !ChatProtocol.IsValidRoom( room ) )
			return;
		// [EXT:online-coop-session] the chat of a game in a shared park is the session's: no other room takes it
		if ( coopRoom != null && room != coopRoom )
			return;
		if ( room == RefusedRoom )
			RefusedRoom = null;
		if ( Chat is { } chat )
		{
			// already there, or on the way
			if ( room == WantedRoom )
				return;
			// A coalesced place has no slot in the send chain yet: it must not follow this join into another room.
			// Snapshots already in the chain precede the join and still belong to the room they were queued for.
			lock ( moveLock )
				latestMove = null;
			requestedRoom = room;
			joinPending = true;
			SendJoin( chat, room );
			return;
		}
		// a new connection starts in the Online World and joins this room once it is open (Install)
		requestedRoom = room;
		joinPending = true;
		joinSentAt = null;
	}

	/// <summary>
	/// [EXT:online-coop-session] The chat room of the shared park this game is in (<see cref="FollowCoopRoom"/>); null outside one. While it is set the chat is the shared
	/// park's: no other room is joined, and nothing is said in a room until the chat is on its way to this one (<see cref="SendChatLine"/>).
	/// </summary>
	private string? coopRoom;

	/// <summary>[EXT:online-coop-session] The reason a shared park hears when this game logs out or changes server (<see cref="Disconnect"/>).</summary>
	internal const string LeftOnLogout = "The player went offline.";

	/// <summary>[EXT:online-coop-session] The shared park's room is to be joined on the open connection once its server version is read (a new connection's rejoin waits for it).</summary>
	private bool coopJoinWaits;

	/// <summary>[EXT:online-coop-session] The shared park's chat connections that failed in a row (their waits grow, <see cref="ReconnectDelays"/>).</summary>
	private int coopChatFailures;

	/// <summary>Tests: opens the chat connection (the client's own by default), so a test can fail an attempt.</summary>
	internal Func<OnlineClient, Task<ChatConnection>> ChatConnector { get; set; } = client => client.ConnectChatAsync();

	/// <summary>
	/// [EXT:online-coop-session] A chat connection opened for the shared park failed: it is tried again after a growing wait, while the session still owns the chat
	/// and no other connection was made meanwhile (a logout or another server ends it).
	/// </summary>
	private void RetryCoopChat()
	{
		if ( coopRoom is not { } room || disposed || !IsLoggedIn )
			return;
		var delay = ReconnectDelays[Math.Min( coopChatFailures++, ReconnectDelays.Length - 1 )];
		var generationAtFail = generation;
		_ = Task.Run( async () =>
		{
			await Task.Delay( delay );
			Post( () =>
			{
				if ( disposed || generation != generationAtFail || coopRoom != room || Chat != null || chatConnecting )
					return;
				FollowCoopRoom();
			} );
		} );
	}

	/// <summary>[EXT:online-coop-session] Whether this game is in a shared park, whose room its chat follows.</summary>
	public bool InSharedPark => coopRoom != null;

	/// <summary>[EXT:online-coop-presence] The chat room of the shared park this game is in (<c>coop:</c> and the session's id), or null outside one (C2d-4).</summary>
	public string? SharedParkRoom => coopRoom;

	/// <summary>
	/// [EXT:online-coop-session] The shared park's room is this connection's, confirmed (C2d-4): a chat connection whose own server version is read and has shared parks'
	/// rooms, no join on its way or waiting, and the server's roster names the room. A movement sent now goes to the shared park's room; after a reconnection it waits for
	/// the new connection's confirmed join.
	/// </summary>
	public bool SharedRoomReady => coopRoom is { } room && Chat != null && SharedParkRoomsKnown && !joinPending && !coopJoinWaits && CurrentRoom == room;

	/// <summary>
	/// [EXT:online-coop-session] Whether something said to a room now would not reach the shared park's room: this connection's server is not known to have such rooms,
	/// or the chat is not on its way to the session's room yet. (On its way, the server takes what is said for the room only once the connection is there.)
	/// </summary>
	private bool SharedParkChatClosed => coopRoom != null && (!SharedParkRoomsKnown || WantedRoom != coopRoom || coopJoinWaits);

	/// <summary>[EXT:online-coop-session] The open connection's server is known to have shared parks' rooms (<see cref="ChatProtocol.CoopRoomsVersion"/>).</summary>
	private bool SharedParkRoomsKnown => protocolCurrent && ServerProtocol >= ChatProtocol.CoopRoomsVersion;

	/// <summary>[EXT:online-coop-session] The chat's line for something said in a shared park that was not sent.</summary>
	internal static string SharedParkChatClosedText() =>
		UI.Original.SupplementaryStrings.Get( UI.Original.OpenTpwText.SharedParkChatClosed, GameLanguage.IsSelected ? GameLanguage.Current.Name : GameLanguage.DefaultLanguage );

	/// <summary>
	/// [EXT:online-coop-session] The command as it goes out, or null when it may not: in a shared park, what is said to a room names the session's room (the server drops
	/// it when the connection is not there) and is not sent while the chat is not on its way there; a goto, which would take the chat elsewhere, is not sent at all.
	/// The chat says why.
	/// </summary>
	private ChatRequest? CommandRequest( ChatCommand command, string argument )
	{
		if ( coopRoom is not { } room )
			return ChatRequest.ForCommand( command, argument );
		if ( command == ChatCommand.Goto )
		{
			AddChatLine( OnlineStrings.ChatNotice( (int)ChatNotice.LeaveParkToGoTo, new[] { ChatCommands.SplitName( argument ).Name ?? "" } ) );
			return null;
		}
		if ( !ChatCommands.SaysToRoom( command ) )
			return ChatRequest.ForCommand( command, argument );
		if ( SharedParkChatClosed )
		{
			AddChatLine( SharedParkChatClosedText() );
			return null;
		}
		return ChatRequest.ForCommand( command, argument, room );
	}

	/// <summary>
	/// [EXT:online-coop-session] This game hosts, joined or resumed the shared park's session <paramref name="sessionId"/>: its chat is the session's, and goes to the
	/// session's room once the server is known to have such rooms (<see cref="FollowCoopRoom"/>).
	/// </summary>
	private void EnterCoopRoom( string sessionId )
	{
		coopRoom = ChatProtocol.CoopRoom( sessionId );
		coopChatFailures = 0;
		// the server's version decides first; it needs no chat connection
		if ( ServerProtocol == null )
			FetchServerInfo();
		FollowCoopRoom();
	}

	/// <summary>
	/// [EXT:online-coop-session] Takes the chat to the shared park's room once the server is known to have such rooms (<see cref="ChatProtocol.CoopRoomsVersion"/>):
	/// it opens the chat when it is closed, and joins once the open connection's own version is read (<see cref="protocolCurrent"/>). An older server's chat would
	/// refuse the room: it is neither opened for the shared park nor moved, and lines said in the shared park are not sent there.
	/// </summary>
	private void FollowCoopRoom()
	{
		if ( coopRoom is not { } room || !IsLoggedIn || disposed || ServerProtocol is not { } protocol )
			return;
		if ( protocol < ChatProtocol.CoopRoomsVersion )
		{
			// a rejoin held for this connection's version (a server that went back to an older version) is dropped: the chat stays in the lobby
			if ( coopJoinWaits )
			{
				coopJoinWaits = false;
				joinPending = false;
				requestedRoom = null;
			}
			return;
		}
		if ( Chat is not { } chat )
		{
			if ( !chatConnecting )
				ConnectChat();
			return;
		}
		if ( !protocolCurrent )
			return;
		if ( coopJoinWaits )
		{
			coopJoinWaits = false;
			SendJoin( chat, room );
		}
		else
			JoinRoom( room );
	}

	/// <summary>
	/// [EXT:online-coop-session] The shared park's session <paramref name="sessionId"/> is over for this game (it ended, or this game left it): a chat in its room, or
	/// on its way there, goes back to the lobby. Nothing once logged out, which leaves every room.
	/// </summary>
	internal void LeftCoopSession( string sessionId )
	{
		var room = ChatProtocol.CoopRoom( sessionId );
		if ( coopRoom != room )
			return;
		coopRoom = null;
		coopJoinWaits = false;
		// wherever the chat is: a refused join left it in the room it was in before
		if ( IsLoggedIn )
			JoinRoom( ChatProtocol.LobbyRoom );
	}

	/// <summary>The room the session is in or on its way to (<see cref="JoinRoom"/>'s last room until the server confirms or refuses it).</summary>
	public string WantedRoom => joinPending && requestedRoom != null ? requestedRoom : CurrentRoom;

	/// <summary>The last room the server refused to let the player into ("NO SUCH PARK": the park was unpublished), until it is asked for again.</summary>
	public string? RefusedRoom { get; private set; }

	/// <summary>
	/// Closes the chat connection and forgets its lines, staying logged in (a visit closes the connection it opened when the park is left, so the chat
	/// overlay does not follow the player into their own park). Nothing reconnects it; the online screens open it again.
	/// </summary>
	public void CloseChat()
	{
		// a connection or a reconnection on its way is not installed
		Interlocked.Increment( ref chatEpoch );
		chatConnecting = false;
		ChatLines.Clear();
		chatCancel?.Cancel();
		chatCancel?.Dispose();
		chatCancel = null;
		Chat?.Dispose();
		Chat = null;
		CurrentRoom = ChatProtocol.LobbyRoom;
		RoomPlayers = Array.Empty<string>();
		positions.Clear();
		jumps.Clear();
		// the server forgets the connection's ignore list and earmuffs with it
		ignored.Clear();
		Earmuffs = false;
		requestedRoom = null;
		joinPending = false;
		joinSentAt = null;
		// [EXT:online-coop-session] a shared park's rejoin was this connection's; the shared park itself goes on, and its room is asked for again on the next connection
		coopJoinWaits = false;
		// a send already queued finds no place and clears its own flag
		lock ( moveLock )
			latestMove = null;
		// still logged in, so a version already known stays; a read for the connection just closed must not answer the next one
		protocolCurrent = false;
		fetchingServerInfo = false;
		serverInfoEpoch++;
	}

	/// <summary>The current server's protocol version (<see cref="FetchServerInfo"/>); null before it is known.</summary>
	public int? ServerProtocol { get; private set; }
	/// <summary>
	/// <see cref="ServerProtocol"/> was read after the open chat connection was made: only then do movements carry their mode, as a server that restarted on an
	/// older version (a deploy taken back) would refuse the member. A hide is not sent at all until this is set, so a reconnect cannot show a riding player.
	/// </summary>
	private bool protocolCurrent;
	/// <summary>Whether <see cref="protocolCurrent"/> is set (tests).</summary>
	internal bool ProtocolCurrent => protocolCurrent;

	/// <summary>Asks the server for its version once (no login needed), beside the operation slot; <see cref="ServerProtocol"/> holds the answer.</summary>
	public void FetchServerInfo() => BeginServerInfo( refresh: false, attempt: 0 );

	/// <summary>Tests: how long a failed version read waits before the next try (<see cref="ServerInfoRetryDelays"/>).</summary>
	internal void SetServerInfoRetryDelays( TimeSpan[] delays ) => serverInfoRetryDelays = delays;

	/// <param name="refresh">Asks again whatever is known, for the chat connection just made (<see cref="Install"/>) or a failed read of it.</param>
	/// <param name="attempt">How many reads of this connection have already failed; chooses the wait before the next one.</param>
	private void BeginServerInfo( bool refresh, int attempt )
	{
		if ( disposed || Client is not { } client )
			return;
		if ( !refresh && (fetchingServerInfo || ServerProtocol != null) )
			return;
		// a refresh replaces a read already in flight: that read's answer is then stale
		fetchingServerInfo = true;
		var epoch = ++serverInfoEpoch;
		var startedGeneration = generation;
		var connection = Chat;
		_ = Task.Run( async () =>
		{
			operationGeneration.Value = startedGeneration;
			try
			{
				var info = await client.GetServerInfoAsync();
				Post( () => ApplyServerInfo( epoch, client, connection, info.ProtocolVersion ) );
			}
			catch ( Exception exception )
			{
				if ( exception is not (OnlineException or HttpRequestException or IOException or InvalidDataException or TaskCanceledException or InvalidOperationException or ObjectDisposedException) )
					Log.Warning( $"Server version request failed: {exception}" );
				Post( () => FailServerInfo( epoch, client, connection, attempt ) );
			}
		} );
	}

	/// <summary>The answer of version read <paramref name="epoch"/>, ignored when a newer read or another server has taken its place.</summary>
	private void ApplyServerInfo( int epoch, OnlineClient client, ChatConnection? connection, int protocol )
	{
		if ( epoch != serverInfoEpoch || Client != client )
			return;
		fetchingServerInfo = false;
		ServerProtocol = protocol;
		// the answer speaks for the connection that was open when it was asked, and still is
		if ( connection != null && Chat == connection )
			protocolCurrent = true;
		FlushMove();
		FollowCoopRoom();
	}

	/// <summary>A failed version read. The open connection's read is tried again, so modes do not stay off after one lost answer.</summary>
	private void FailServerInfo( int epoch, OnlineClient client, ChatConnection? connection, int attempt )
	{
		if ( epoch != serverInfoEpoch || Client != client )
			return;
		fetchingServerInfo = false;
		// [EXT:online-coop-session] a shared park waits for the server's version without a chat connection (EnterCoopRoom): that read is tried again too
		var forSharedPark = connection == null && Chat == null && coopRoom != null && ServerProtocol == null;
		if ( (connection == null || Chat != connection || protocolCurrent) && !forSharedPark || disposed )
			return;
		var delay = serverInfoRetryDelays[Math.Min( attempt, serverInfoRetryDelays.Length - 1 )];
		var next = attempt + 1;
		var generationAtFail = generation;
		_ = Task.Run( async () =>
		{
			await Task.Delay( delay );
			Post( () =>
			{
				// a read started during the wait already owns the answer; replacing it would drop that answer
				if ( disposed || generation != generationAtFail || Client != client || Chat != connection || protocolCurrent
					|| fetchingServerInfo || epoch != serverInfoEpoch )
					return;
				if ( forSharedPark && (coopRoom == null || ServerProtocol != null) )
					return;
				BeginServerInfo( refresh: !forSharedPark, attempt: next );
			} );
		} );
	}

	/// <summary>
	/// Walks the player's avatar to a place in the chat park (manual p. 68: "Left click to walk to destination;
	/// Double click to teleport"): the server relays the position to everyone in the park. The player's own place is
	/// recorded at once, so the view moves even before the server answers. A server of <see cref="ChatProtocol.MoveModesVersion"/> or later is told how
	/// the figure moved (<paramref name="mode"/>); an older one, which refuses the mode, gets the place alone. A hide waits until this connection's version
	/// is known: sending it without the mode would show the rider to every older client for the whole ride.
	/// </summary>
	public void MoveTo( float x, float y, ChatMoveMode mode = ChatMoveMode.Walk )
	{
		var chat = Chat;
		if ( chat == null )
			return;
		var name = Settings.PlayerName;
		if ( name != null )
			positions[name] = (x, y);
		lock ( moveLock )
		{
			// a walk that replaces a waiting teleport, appearance or hide keeps that mode (the place is the new one)
			if ( latestMove is { Chat: var pendingChat, Mode: var pending } && pendingChat == chat )
				mode = KeepPlaceMode( mode, pending );
			latestMove = (chat, x, y, mode);
		}
		FlushMove();
	}

	/// <summary>The mode a newer walk leaves in place: a teleport or an appearance keeps its jump, and a hide must not be walked into view before it is sent.</summary>
	internal static ChatMoveMode KeepPlaceMode( ChatMoveMode incoming, ChatMoveMode pending ) =>
		incoming == ChatMoveMode.Walk && pending is ChatMoveMode.Teleport or ChatMoveMode.Unhide or ChatMoveMode.Hide ? pending : incoming;

	/// <summary>
	/// Sends the newest place, or keeps a hide until <see cref="protocolCurrent"/> says this connection's version is known. One send waits in the chain at a
	/// time: a connection that cannot keep up skips the places in between, never the last one.
	/// </summary>
	private void FlushMove()
	{
		(ChatConnection Chat, float X, float Y, ChatMoveMode? Wire) snapshot;
		lock ( moveLock )
		{
			if ( moveQueued || latestMove is not { } held )
				return;
			if ( held.Chat != Chat )
			{
				latestMove = null;
				return;
			}
			// a hide with its mode stripped is a plain place, and an older client keeps that place for the whole ride
			if ( held.Mode == ChatMoveMode.Hide && !protocolCurrent )
				return;
			var wire = protocolCurrent && ServerProtocol >= ChatProtocol.MoveModesVersion ? held.Mode : (ChatMoveMode?)null;
			latestMove = null;
			moveQueued = true;
			snapshot = (held.Chat, held.X, held.Y, wire);
		}
		Chain( () =>
		{
			lock ( moveLock )
				moveQueued = false;
			// a place that arrived while this one was on the wire, or a hide released by the version answer, goes next
			Post( FlushMove );
			return snapshot.Chat.SendAsync( ChatRequest.ForMove( snapshot.X, snapshot.Y, snapshot.Wire ) );
		} );
	}

	/// <summary>
	/// Asks for the buddy list afresh, for the overlay: the answers (each buddy online or offline) rebuild <see cref="Buddies"/>, so a buddy removed elsewhere
	/// drops out, and stay off the chat lines. The old list stands until the first answer, so a dropped ask leaves no buddy looking like a stranger. Nothing
	/// is asked within <see cref="BuddyAskIntervalMilliseconds"/> of the last ask (<see cref="BuddyAskDue"/>).
	/// </summary>
	// [EXT:online-chat-person-flags] the overlay keeps its buddy list from asks of its own, one in five seconds at most; the original kept the flags from the server's buddy messages (0x4EE5C0)
	public void AskBuddies()
	{
		if ( Chat == null || !BuddyAskDue( buddyAskedAt, Environment.TickCount64, BuddyAskIntervalMilliseconds ) )
			return;
		buddyAnswerPending = true;
		quietBuddyAnswers = true;
		buddyAskedAt = Environment.TickCount64;
		SendCommand( ChatCommand.Buddy, "" );
	}

	/// <summary>Whether a new ask for the buddy list may go out at <paramref name="now"/>: none was sent yet, or the last at least <paramref name="interval"/> milliseconds ago.</summary>
	internal static bool BuddyAskDue( long? askedAt, long now, int interval ) => askedAt is not { } asked || now - asked >= interval;

	/// <summary>Sends one chat command (the chat overlay's buttons and popups), whatever the language's command words.</summary>
	public void SendCommand( ChatCommand command, string argument )
	{
		// [EXT:online-coop-session] in a shared park, as a typed line goes (CommandRequest)
		if ( Chat is { } chat && CommandRequest( command, argument ) is { } request )
			SendOnChat( chat, request );
	}

	/// <summary>Fetches the logged-in player's own page: their parks, their ranks and their Platinum Tickets.</summary>
	public void FetchProfile() => WithClient( async client =>
	{
		var name = Settings.PlayerName;
		if ( name == null )
			return null;
		var profile = await client.GetPlayerProfileAsync( name );
		Post( () => Profile = profile );
		return null;
	} );

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		Disconnect();
	}
}
