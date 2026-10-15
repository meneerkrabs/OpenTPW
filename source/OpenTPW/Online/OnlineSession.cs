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
			// [APPROX:ONLINE-024] the original notification's string is not recovered; the wording is OpenTPW's own
			ChatEventKind.System when item.Text == "buddy-enters" && item.Args is { Count: 2 } enters =>
				string.Format( label( OnlineLabel.BuddyEnters ), enters[0], enters[1] ),
			ChatEventKind.System when item.Text == "server-message" => Notice( OnlineStrings.SystemMessage, item.Args ),
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
public sealed class OnlineSession : IDisposable
{
	public const int MaximumChatLines = 200;
	private readonly ConcurrentQueue<(int Generation, Action Action, Action? Discard)> mainThread = new();
	private readonly AsyncLocal<int?> operationGeneration = new();
	private int generation;
	private bool disposed;
	private CancellationTokenSource? chatCancel;

	public OnlineSession( OnlineFolders folders )
	{
		Folders = folders;
		try { Settings = folders.LoadSettings(); }
		catch ( Exception exception ) when ( exception is InvalidDataException or IOException )
		{
			Settings = new OnlineSettings( null, null );
			Status = exception.Message;
		}
	}

	public OnlineFolders Folders { get; }
	public OnlineSettings Settings { get; private set; }

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
	public string Status { get; private set; } = "";
	public int Busy { get; private set; }
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
	/// movement; the player's own position is set by <see cref="MoveTo"/>. Empty in a room nobody moved in.
	/// </summary>
	public IReadOnlyDictionary<string, (float X, float Y)> RoomPositions => positions;
	private readonly Dictionary<string, (float X, float Y)> positions = new( StringComparer.OrdinalIgnoreCase );
	/// <summary>
	/// The logged-in player's own page (published parks, ranks, Platinum Tickets) or null before
	/// <see cref="FetchProfile"/> ran.
	/// </summary>
	public PlayerProfile? Profile { get; private set; }
	/// <summary>
	/// The buddies of the last <c>/buddy</c> answer and whether each was online then (the manual's buddy list: "Lists
	/// your park buddies who are currently online"); empty before the first answer.
	/// </summary>
	public IReadOnlyList<(string Name, bool Online)> Buddies => buddies;
	private readonly List<(string Name, bool Online)> buddies = new();
	public bool IsLoggedIn => Client?.Session != null;
	/// <summary>A two-step setup waiting for its first code (after registering, or a login after an operator reset).</summary>
	public TotpEnrollment? Enrollment { get; internal set; }
	/// <summary>The last login asked for an authenticator code (missing or wrong).</summary>
	public bool CodeRequested { get; private set; }

	public void Pump()
	{
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

	private void Post( Action action, Action? discard = null )
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
			catch ( HttpRequestException exception ) { message = OnlineStrings.Error( OnlineStrings.ErrorConnect, "Error: Couldn't connect to server" ) + $" ({exception.Message})"; }
			catch ( Exception exception ) when ( exception is InvalidDataException or IOException or TaskCanceledException or ArgumentException or InvalidOperationException or System.Net.WebSockets.WebSocketException or ObjectDisposedException )
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

	/// <summary>Opts in to a server (validated http/https URL) and remembers it.</summary>
	public void UseServer( string url, string? playerName )
	{
		if ( disposed )
			throw new ObjectDisposedException( nameof( OnlineSession ) );
		var server = OnlineClient.ValidateServerUrl( new Uri( url.Trim(), UriKind.Absolute ) );
		Disconnect();
		Client = new OnlineClient( server );
		Settings = new OnlineSettings( server.ToString(), playerName ?? Settings.PlayerName );
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
		Client?.Dispose();
		Client = null;
		Parks = Array.Empty<ParkSummary>();
		Inbox = Array.Empty<PostcardSummary>();
		News = null;
		Cities = Array.Empty<CityInfo>();
		ChatParks = Array.Empty<ChatParkInfo>();
		CityParks = null;
		CurrentRoom = ChatProtocol.LobbyRoom;
		RoomPlayers = Array.Empty<string>();
		positions.Clear();
		Profile = null;
		ChatLines.Clear();
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

	public void Publish( ParkPackage package, bool showOnWebsite = false, int cityId = 0 ) => WithClient( async client =>
	{
		Post( () => Status = OnlineStrings.Ui( OnlineStrings.PublishingPark, "Publishing Park" ) );
		await client.UploadParkAsync( package, showOnWebsite: showOnWebsite, cityId: cityId );
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
			await client.SendPostcardAsync( card );
			File.Move( file, Path.Combine( Folders.Sent, Path.GetFileName( file ) ), true );
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

	public void ConnectChat() => WithClient( async client =>
	{
		if ( Chat != null )
			return null;
		var chat = await client.ConnectChatAsync();
		if ( disposed || operationGeneration.Value != generation )
		{
			await chat.DisposeAsync();
			return null;
		}
		var cancel = new CancellationTokenSource();
		Post( () =>
		{
			Chat = chat;
			chatCancel = cancel;
		}, () => { cancel.Cancel(); cancel.Dispose(); chat.Dispose(); } );
		_ = Task.Run( () => ReceiveLoopAsync( chat, cancel.Token ) );
		return null;
	} );

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
					if ( item.Kind == ChatEventKind.Room && item.Room != null )
					{
						CurrentRoom = item.Room;
						RoomPlayers = item.Args ?? Array.Empty<string>();
						// The new room's places arrive with the player's first movement there; the old room's
						// places mean nothing in it.
						positions.Clear();
					}
					else if ( item.Kind == ChatEventKind.Move && item.From != null )
						positions[item.From] = (item.X ?? 0, item.Y ?? 0);
					else if ( item.Kind == ChatEventKind.Notice && item.Notice is (int)ChatNotice.YourBuddyOnline or (int)ChatNotice.YourBuddyOffline
						&& item.Args is { Count: >= 1 } args )
					{
						// A /buddy answer: remember it for the chat overlay's buddy tab.
						buddies.RemoveAll( entry => entry.Name.Equals( args[0], StringComparison.OrdinalIgnoreCase ) );
						buddies.Add( (args[0], item.Notice == (int)ChatNotice.YourBuddyOnline) );
					}
					if ( item.Kind != ChatEventKind.Move )
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
				if ( lost && IsLoggedIn && Client is { } client )
					ReconnectChat( client, CurrentRoom );
			}
		} );
	}

	/// <summary>Waits between attempts to reconnect the chat after a server restart.</summary>
	internal static readonly TimeSpan[] ReconnectDelays = { TimeSpan.FromSeconds( 1 ), TimeSpan.FromSeconds( 2 ), TimeSpan.FromSeconds( 4 ), TimeSpan.FromSeconds( 8 ), TimeSpan.FromSeconds( 15 ), TimeSpan.FromSeconds( 30 ) };

	private void ReconnectChat( OnlineClient client, string room )
	{
		var startedGeneration = generation;
		_ = Task.Run( async () =>
		{
			operationGeneration.Value = startedGeneration;
			foreach ( var delay in ReconnectDelays )
			{
				await Task.Delay( delay );
				if ( disposed || startedGeneration != generation || Client != client || !IsLoggedIn )
					return;
				ChatConnection chat;
				try
				{
					chat = await client.ConnectChatAsync();
				}
				catch ( Exception exception ) when ( exception is OnlineException or HttpRequestException or System.Net.WebSockets.WebSocketException or IOException or InvalidDataException or TaskCanceledException )
				{
					continue;
				}
				if ( room != ChatProtocol.LobbyRoom )
				{
					try { await chat.JoinAsync( room ); }
					catch ( Exception exception ) when ( exception is System.Net.WebSockets.WebSocketException or IOException ) { }
				}
				var cancel = new CancellationTokenSource();
				Post( () =>
				{
					if ( Chat != null )
					{
						cancel.Dispose();
						chat.Dispose();
						return;
					}
					Chat = chat;
					chatCancel = cancel;
					AddChatLine( OnlineStrings.Get( OnlineLabel.ChatReconnected ) );
					_ = Task.Run( () => ReceiveLoopAsync( chat, cancel.Token ) );
				}, () => { cancel.Dispose(); chat.Dispose(); } );
				return;
			}
		} );
	}

	/// <summary>The name of the room the player is in: the Online World, a chat park or a published park.</summary>
	public string CurrentRoomName => RoomName( CurrentRoom );

	private string RoomName( string room ) => room == ChatProtocol.LobbyRoom || room.Length == 0
		? OnlineStrings.Ui( OnlineStrings.OnlineWorld, "Online World" )
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
		Run( async () =>
		{
			await chat.SendAsync( command, argument );
			return null;
		} );
	}

	public void JoinRoom( string room )
	{
		var chat = Chat;
		if ( chat != null )
			Run( async () =>
			{
				await chat.JoinAsync( room );
				return null;
			} );
	}

	/// <summary>
	/// Walks the player's avatar to a place in the chat park (manual p. 68: "Left click to walk to destination;
	/// Double click to teleport"): the server relays the position to everyone in the park. The player's own place is
	/// recorded at once, so the view moves even before the server answers.
	/// </summary>
	public void MoveTo( float x, float y )
	{
		var chat = Chat;
		if ( chat == null )
			return;
		var name = Settings.PlayerName;
		if ( name != null )
			positions[name] = (x, y);
		Run( async () =>
		{
			await chat.MoveAsync( x, y );
			return null;
		} );
	}

	/// <summary>Asks the server for the buddy list (the original's <c>/buddy</c> answer lists each buddy as online or offline).</summary>
	public void AskBuddies() => SendChatLine( "/" + ChatCommands.CanonicalName( ChatCommand.Buddy ) );

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
