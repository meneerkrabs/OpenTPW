using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using OpenTPW.Online.Api;
using OpenTPW.Online.Chat;
using OpenTPW.Online.Coop;
using OpenTPW.Online.Packages;

namespace OpenTPW.Online.Client;

public sealed class OnlineException : Exception
{
	public OnlineException( HttpStatusCode status, string message, string? reason = null ) : base( message ) => (Status, Reason) = (status, reason);
	public HttpStatusCode Status { get; }
	/// <summary>The server's <see cref="ApiErrorReasons"/> value, or null.</summary>
	public string? Reason { get; }
}

/// <summary>A login: a session, or the two-step setup the account needs first (<see cref="OnlineClient.ConfirmEnrollmentAsync"/>).</summary>
public sealed record LoginResult( SessionToken? Session, TotpEnrollment? Enrollment );

/// <summary>
/// HTTP + WebSocket client for an OpenTPW server. The server URL is chosen by the user (opt-in);
/// the password is only sent to log in and never stored. All responses are size-bounded.
/// </summary>
public sealed class OnlineClient : IDisposable
{
	private const int MaximumJsonResponseBytes = 1024 * 1024;
	private readonly HttpClient http;
	private readonly bool ownsHttp;

	public OnlineClient( Uri serverUrl, HttpClient? httpClient = null )
	{
		ServerUrl = ValidateServerUrl( serverUrl );
		ownsHttp = httpClient == null;
		http = httpClient ?? new HttpClient( new HttpClientHandler { AllowAutoRedirect = false } ) { Timeout = TimeSpan.FromSeconds( 60 ) };
	}

	public Uri ServerUrl { get; }
	public SessionToken? Session { get; private set; }

	/// <summary>
	/// Whether a socket sends its token as its first message instead of a header: always in a browser, which cannot set WebSocket headers; tests use it on
	/// the desktop to take the browser's way.
	/// </summary>
	public bool AuthenticateInFirstFrame { get; set; } = OperatingSystem.IsBrowser();

	/// <summary>
	/// https, or http for a server on this machine or the local network (passwords and tokens would otherwise cross the
	/// internet unencrypted); no credentials, query or fragment in the URL.
	/// </summary>
	public static Uri ValidateServerUrl( Uri url )
	{
		if ( !url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty( url.UserInfo ) || !string.IsNullOrEmpty( url.Query ) || !string.IsNullOrEmpty( url.Fragment ) )
			throw new ArgumentException( "The server URL must be an http:// or https:// address without credentials, query or fragment." );
		if ( url.Scheme == "http" && !IsLocalHost( url.Host ) )
			throw new ArgumentException( "Use an https:// address: http:// is only for a server on this computer or the local network." );
		var text = url.ToString();
		return new Uri( text.EndsWith( '/' ) ? text : text + "/" );
	}

	/// <summary>
	/// <c>localhost</c> and <c>.localhost</c> names (which always resolve to this machine) and loopback, private,
	/// link-local or unique-local address literals. Other names, <c>.local</c> included, can resolve to a public address.
	/// </summary>
	public static bool IsLocalHost( string host )
	{
		host = host.Trim( '[', ']' ).TrimEnd( '.' );
		if ( host.Equals( "localhost", StringComparison.OrdinalIgnoreCase ) || host.EndsWith( ".localhost", StringComparison.OrdinalIgnoreCase ) )
			return true;
		if ( !System.Net.IPAddress.TryParse( host, out var address ) )
			return false;
		if ( address.IsIPv4MappedToIPv6 )
			address = address.MapToIPv4();
		if ( System.Net.IPAddress.IsLoopback( address ) )
			return true;
		var bytes = address.GetAddressBytes();
		if ( address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork )
			return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254);
		return address.IsIPv6LinkLocal || (bytes[0] & 0xFE) == 0xFC;
	}

	public Task<ServerInfo> GetServerInfoAsync( CancellationToken cancel = default ) => GetJsonAsync<ServerInfo>( ApiRoutes.Server, cancel );

	/// <summary>The online world's cities, or null from a server without cities (older OpenTPW servers).</summary>
	public async Task<CityList?> GetCitiesAsync( CancellationToken cancel = default )
	{
		try
		{
			return await GetJsonAsync<CityList>( ApiRoutes.Cities, cancel );
		}
		catch ( OnlineException exception ) when ( exception.Status == HttpStatusCode.NotFound )
		{
			return null;
		}
	}

	/// <summary>The online world's chat parks with their player counts, or null from a server without them.</summary>
	public async Task<ChatParkList?> GetChatParksAsync( CancellationToken cancel = default )
	{
		try
		{
			return await GetJsonAsync<ChatParkList>( ApiRoutes.ChatParks, cancel );
		}
		catch ( OnlineException exception ) when ( exception.Status == HttpStatusCode.NotFound )
		{
			return null;
		}
	}

	/// <summary>
	/// The chat parks' scenes: what each satellite's park details show and the park its visit enters (needs a login), or null from a server without them
	/// (an older OpenTPW server, whose satellites are entered in the overhead chat view).
	/// </summary>
	public async Task<ChatParkSceneList?> GetChatParkScenesAsync( CancellationToken cancel = default )
	{
		try
		{
			return await GetJsonAsync<ChatParkSceneList>( ApiRoutes.ChatParkScenes, cancel );
		}
		catch ( OnlineException exception ) when ( exception.Status == HttpStatusCode.NotFound )
		{
			return null;
		}
	}

	/// <summary>
	/// A player's page: their published parks with visitors, votes, publish date, current and highest rank, and their
	/// Platinum Tickets (the original website's "My Theme Park", docs/reverse/ONLINE-SOURCES.md S1). Without a login
	/// only parks the author shows on the website are listed.
	/// </summary>
	public Task<PlayerProfile> GetPlayerProfileAsync( string name, CancellationToken cancel = default ) =>
		GetJsonAsync<PlayerProfile>( ApiRoutes.Players + "/" + Uri.EscapeDataString( name.Trim() ), cancel );

	/// <summary>The public top-parks chart (the original website's Top 10 parks page, S1).</summary>
	public Task<ChartParkList> GetChartParksAsync( CancellationToken cancel = default ) => GetJsonAsync<ChartParkList>( ApiRoutes.ChartParks, cancel );

	/// <summary>The public player charts: Top 10 players by votes, or the richest by Platinum Tickets (S1).</summary>
	public Task<ChartPlayerList> GetChartPlayersAsync( bool richest, CancellationToken cancel = default ) =>
		GetJsonAsync<ChartPlayerList>( richest ? ApiRoutes.ChartRichest : ApiRoutes.ChartPlayers, cancel );

	/// <summary>Searches players by name; the original capped the website's search at 50 results (S1).</summary>
	public Task<PlayerSearchResult> SearchPlayersAsync( string search, CancellationToken cancel = default ) =>
		GetJsonAsync<PlayerSearchResult>( ApiRoutes.Players + "?search=" + Uri.EscapeDataString( search.Trim() ), cancel );

	/// <summary>The server's news, or null from a server without news (older OpenTPW servers).</summary>
	public async Task<NewsInfo?> GetNewsAsync( CancellationToken cancel = default )
	{
		try
		{
			return await GetJsonAsync<NewsInfo>( ApiRoutes.News, cancel );
		}
		catch ( OnlineException exception ) when ( exception.Status == HttpStatusCode.NotFound )
		{
			return null;
		}
	}

	/// <summary>Registers; returns the two-step setup when the server requires one (the account exists only after it), else null.</summary>
	public async Task<TotpEnrollment?> RegisterAsync( string name, string password, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Post, ApiRoutes.Accounts, Json( new Credentials( name, password ) ), cancel, authenticated: false );
		return response.StatusCode == HttpStatusCode.Accepted ? ValidEnrollment( await ReadJsonAsync<TotpEnrollment>( response, cancel ) ) : null;
	}

	/// <summary>Logs in; <paramref name="code"/> is the authenticator code for accounts with two-step login.</summary>
	public async Task<LoginResult> LoginAsync( string name, string password, string? code = null, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Post, ApiRoutes.Sessions, Json( new Credentials( name, password, code ) ), cancel, authenticated: false );
		if ( response.StatusCode == HttpStatusCode.Accepted )
			return new LoginResult( null, ValidEnrollment( await ReadJsonAsync<TotpEnrollment>( response, cancel ) ) );
		Session = await ReadJsonAsync<SessionToken>( response, cancel );
		return new LoginResult( Session, null );
	}

	/// <summary>Finishes a two-step setup with the first code from the authenticator app and logs in.</summary>
	public async Task<SessionToken> ConfirmEnrollmentAsync( TotpEnrollment enrollment, string code, string? emailCode = null, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Post, ApiRoutes.AccountConfirmation,
			Json( new TotpConfirmation( enrollment.Name, enrollment.Ticket, code, string.IsNullOrWhiteSpace( emailCode ) ? null : emailCode.Trim() ) ), cancel, authenticated: false );
		Session = await ReadJsonAsync<SessionToken>( response, cancel );
		return Session;
	}

	/// <summary>Sends a code to the address the new account of <paramref name="enrollment"/> will use for recovery.</summary>
	public async Task SendEnrollmentEmailAsync( TotpEnrollment enrollment, string email, string? language = null, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Post, ApiRoutes.AccountEmail, Json( new EnrollmentEmail( enrollment.Name, enrollment.Ticket, email.Trim(), language ) ), cancel, authenticated: false );
	}

	/// <summary>Asks for a recovery code; the server answers the same whether the name or address exists.</summary>
	public async Task RequestRecoveryAsync( string nameOrEmail, string? language = null, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Post, ApiRoutes.Recovery, Json( new RecoveryRequest( nameOrEmail.Trim(), language ) ), cancel, authenticated: false );
	}

	/// <summary>Sets a new password with the recovery code; <paramref name="resetTwoStep"/> also sets up two-step login again.</summary>
	public async Task ConfirmRecoveryAsync( string nameOrEmail, string code, string password, bool resetTwoStep, string? language = null, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Post, ApiRoutes.RecoveryConfirmation,
			Json( new RecoveryConfirmation( nameOrEmail.Trim(), code.Trim(), password, resetTwoStep, language ) ), cancel, authenticated: false );
	}

	/// <summary>A secret of plausible length in base32 and an otpauth URI that carries it.</summary>
	private static TotpEnrollment ValidEnrollment( TotpEnrollment enrollment )
	{
		byte[] secret;
		try { secret = Totp.FromBase32( enrollment.Secret ?? "" ); }
		catch ( FormatException ) { throw new InvalidDataException( "The server sent an invalid two-step secret." ); }
		if ( secret.Length is < 10 or > 64 || enrollment.Uri?.StartsWith( "otpauth://totp/", StringComparison.Ordinal ) != true || enrollment.Uri.Length > 512
			|| enrollment.Ticket is not { Length: > 0 and <= 128 } )
			throw new InvalidDataException( "The server sent an invalid two-step secret." );
		return enrollment;
	}

	/// <summary>Deletes the logged-in account; the server asks for its name and password again.</summary>
	public async Task DeleteAccountAsync( string name, string password, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Delete, ApiRoutes.Accounts, Json( new Credentials( name, password ) ), cancel );
		Session = null;
	}

	public async Task LogoutAsync( CancellationToken cancel = default )
	{
		if ( Session == null )
			return;
		using var response = await SendAsync( HttpMethod.Delete, ApiRoutes.Sessions, null, cancel );
		Session = null;
	}

	/// <param name="cityId">Only parks published in this city (the city lobby).</param>
	public Task<ParkList> ListParksAsync( string? search = null, string sort = "recent", string? author = null, CancellationToken cancel = default, int? cityId = null )
	{
		var query = $"?sort={Uri.EscapeDataString( sort )}";
		if ( !string.IsNullOrEmpty( search ) )
			query += $"&search={Uri.EscapeDataString( search )}";
		if ( !string.IsNullOrEmpty( author ) )
			query += $"&author={Uri.EscapeDataString( author )}";
		if ( cityId is { } city )
			query += $"&city={city.ToString( System.Globalization.CultureInfo.InvariantCulture )}";
		return RequireListAsync( GetJsonAsync<ParkList>( ApiRoutes.Parks + query, cancel ), list => list.Parks, "park list" );
	}

	/// <param name="showOnWebsite">Lists the park on the project website (opt-in; the server's public website park list).</param>
	/// <param name="cityId">City of the online world to publish in (0: none).</param>
	public async Task<ParkSummary> UploadParkAsync( ParkPackage package, CancellationToken cancel = default, bool showOnWebsite = false, int cityId = 0 )
	{
		var content = new ByteArrayContent( package.ToBytes() );
		content.Headers.ContentType = new MediaTypeHeaderValue( ApiRoutes.PackageMediaType );
		using var response = await SendAsync( HttpMethod.Post, ApiRoutes.Parks + Query( ("website", showOnWebsite ? "true" : null), ("city", cityId > 0 ? cityId.ToString( System.Globalization.CultureInfo.InvariantCulture ) : null) ), content, cancel );
		return await ReadJsonAsync<ParkSummary>( response, cancel );
	}

	public async Task<ParkPackage> DownloadParkAsync( string parkId, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Get, $"{ApiRoutes.Parks}/{Id( parkId )}/package", null, cancel );
		await using var stream = await response.Content.ReadAsStreamAsync( cancel );
		return ParkPackage.Read( await BoundedZip.ReadBoundedAsync( stream, ParkPackage.MaximumPackageBytes, "Park package", cancel ) );
	}

	public async Task<byte[]> DownloadThumbnailAsync( string parkId, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Get, $"{ApiRoutes.Parks}/{Id( parkId )}/thumbnail", null, cancel );
		await using var stream = await response.Content.ReadAsStreamAsync( cancel );
		var png = await BoundedZip.ReadBoundedAsync( stream, ParkPackage.MaximumThumbnailBytes, "Thumbnail", cancel );
		PngImage.Validate( png, ParkPackage.MaximumThumbnailDimension );
		return png;
	}

	public Task<CounterResult> RecordVisitAsync( string parkId, CancellationToken cancel = default ) => PostForJsonAsync<CounterResult>( $"{ApiRoutes.Parks}/{Id( parkId )}/visits", cancel );

	public Task<CounterResult> VoteAsync( string parkId, CancellationToken cancel = default ) => PostForJsonAsync<CounterResult>( $"{ApiRoutes.Parks}/{Id( parkId )}/votes", cancel );

	public async Task UnpublishParkAsync( string parkId, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Delete, $"{ApiRoutes.Parks}/{Id( parkId )}", null, cancel );
	}

	public async Task<PostcardSent> SendPostcardAsync( Postcard card, CancellationToken cancel = default )
	{
		var content = new ByteArrayContent( card.ToBytes() );
		content.Headers.ContentType = new MediaTypeHeaderValue( ApiRoutes.PostcardMediaType );
		using var response = await SendAsync( HttpMethod.Post, ApiRoutes.Postcards, content, cancel );
		return await ReadJsonAsync<PostcardSent>( response, cancel );
	}

	public Task<PostcardInbox> GetInboxAsync( CancellationToken cancel = default ) =>
		RequireListAsync( GetJsonAsync<PostcardInbox>( ApiRoutes.Inbox, cancel ), inbox => inbox.Postcards, "inbox" );

	/// <summary>Most items one list response may hold; servers send at most a few hundred.</summary>
	public const int MaximumListItems = 1000;

	/// <summary>Rejects a response whose list is missing, holds null entries or is implausibly long (a hostile or broken server).</summary>
	private static async Task<T> RequireListAsync<T, TItem>( Task<T> response, Func<T, IReadOnlyList<TItem>?> items, string name ) where TItem : class
	{
		var value = await response;
		var list = items( value );
		if ( list == null || list.Count > MaximumListItems || list.Any( item => item == null ) )
			throw new InvalidDataException( $"The server sent an invalid {name}." );
		return value;
	}

	public async Task<Postcard> DownloadPostcardAsync( string postcardId, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Get, $"{ApiRoutes.Postcards}/{Id( postcardId )}", null, cancel );
		await using var stream = await response.Content.ReadAsStreamAsync( cancel );
		return Postcard.Read( await BoundedZip.ReadBoundedAsync( stream, Postcard.MaximumCardBytes, "Postcard", cancel ) );
	}

	public async Task DeletePostcardAsync( string postcardId, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Delete, $"{ApiRoutes.Postcards}/{Id( postcardId )}", null, cancel );
	}

	public async Task ReportAsync( string kind, string target, string reason, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Post, ApiRoutes.Reports, Json( new ReportRequest( kind, target, reason ) ), cancel );
	}

	/// <summary>Opens the chat WebSocket with the session token.</summary>
	public async Task<ChatConnection> ConnectChatAsync( CancellationToken cancel = default )
	{
		var session = Session ?? throw new InvalidOperationException( "Log in before connecting to chat." );
		return new ChatConnection( await OpenSocketAsync( session, ApiRoutes.Chat, TimeSpan.FromSeconds( 30 ), null, cancel ) );
	}

	/// <summary>
	/// [EXT:online-coop-session] Opens the co-op socket of shared parks with the session token (docs/ONLINE.md, "Shared parks"); the caller's first message is
	/// <see cref="CoopHello"/>. Its keep-alive notices a dead server within 45 seconds.
	/// </summary>
	public async Task<CoopConnection> ConnectCoopAsync( CancellationToken cancel = default )
	{
		var session = Session ?? throw new InvalidOperationException( "Log in before opening a shared park." );
		return new CoopConnection( await OpenSocketAsync( session, ApiRoutes.Coop, TimeSpan.FromSeconds( 15 ), TimeSpan.FromSeconds( 30 ), cancel ) );
	}

	/// <summary>[EXT:online-coop-session] The open list of shared parks (decision Q4): listed sessions that are not full and whose host is connected.</summary>
	public Task<CoopSessionList> ListCoopSessionsAsync( CancellationToken cancel = default ) => GetJsonAsync<CoopSessionList>( ApiRoutes.CoopSessions, cancel );

	/// <summary>
	/// Opens a WebSocket on <paramref name="route"/> with the session token: a header and a keep-alive on the desktop; in a browser, which can set neither,
	/// the token is the first message.
	/// </summary>
	private async Task<ClientWebSocket> OpenSocketAsync( SessionToken session, string route, TimeSpan keepAlive, TimeSpan? keepAliveTimeout, CancellationToken cancel )
	{
		var socket = new ClientWebSocket();
		var firstFrame = AuthenticateInFirstFrame;
		if ( !firstFrame )
			socket.Options.SetRequestHeader( "Authorization", "Bearer " + session.Token );
		var browser = OperatingSystem.IsBrowser();
		if ( !browser )
		{
			socket.Options.KeepAliveInterval = keepAlive;
			if ( keepAliveTimeout is { } timeout )
				socket.Options.KeepAliveTimeout = timeout;
			socket.Options.CollectHttpResponseDetails = true;
		}
		var builder = new UriBuilder( new Uri( ServerUrl, route.TrimStart( '/' ) ) );
		builder.Scheme = builder.Scheme == "https" ? "wss" : "ws";
		try
		{
			await socket.ConnectAsync( builder.Uri, cancel );
			if ( firstFrame )
				await socket.SendAsync( StrictJson.Serialize( ChatAuthentication.For( session.Token ) ), WebSocketMessageType.Text, true, cancel );
		}
		catch ( WebSocketException ) when ( !browser && socket.HttpStatusCode == HttpStatusCode.Unauthorized )
		{
			// a refused login is no network trouble: a reconnect would be refused the same way
			socket.Dispose();
			throw new OnlineException( HttpStatusCode.Unauthorized, "401: The server no longer accepts this login." );
		}
		catch
		{
			socket.Dispose();
			throw;
		}
		return socket;
	}

	private static string Id( string id )
	{
		if ( id.Length is < 1 or > 64 || !id.All( char.IsAsciiLetterOrDigit ) )
			throw new ArgumentException( "Invalid id." );
		return id;
	}

	private static ByteArrayContent Json<T>( T value )
	{
		var content = new ByteArrayContent( StrictJson.Serialize( value ) );
		content.Headers.ContentType = new MediaTypeHeaderValue( "application/json" );
		return content;
	}

	private static string Query( params (string Name, string? Value)[] parameters )
	{
		var present = parameters.Where( parameter => parameter.Value != null ).Select( parameter => $"{parameter.Name}={Uri.EscapeDataString( parameter.Value! )}" ).ToArray();
		return present.Length == 0 ? "" : "?" + string.Join( "&", present );
	}

	private async Task<T> GetJsonAsync<T>( string route, CancellationToken cancel ) where T : class
	{
		using var response = await SendAsync( HttpMethod.Get, route, null, cancel );
		return await ReadJsonAsync<T>( response, cancel );
	}

	private async Task<T> PostForJsonAsync<T>( string route, CancellationToken cancel ) where T : class
	{
		using var response = await SendAsync( HttpMethod.Post, route, null, cancel );
		return await ReadJsonAsync<T>( response, cancel );
	}

	private async Task<HttpResponseMessage> SendAsync( HttpMethod method, string route, HttpContent? content, CancellationToken cancel, bool authenticated = true )
	{
		using var request = new HttpRequestMessage( method, new Uri( ServerUrl, route.TrimStart( '/' ) ) ) { Content = content };
		if ( authenticated && Session != null )
			request.Headers.Authorization = new AuthenticationHeaderValue( "Bearer", Session.Token );
		var response = await http.SendAsync( request, HttpCompletionOption.ResponseHeadersRead, cancel );
		if ( response.IsSuccessStatusCode )
			return response;
		using ( response )
		{
			var message = response.ReasonPhrase ?? "Request failed";
			string? reason = null;
			try
			{
				await using var stream = await response.Content.ReadAsStreamAsync( cancel );
				var body = await BoundedZip.ReadBoundedAsync( stream, 16 * 1024, "Error response", cancel );
				var error = StrictJson.Deserialize<ApiError>( body, "Error response" );
				message = error.Error;
				reason = error.Reason;
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or IOException )
			{
			}
			throw new OnlineException( response.StatusCode, $"{(int)response.StatusCode}: {message}", reason );
		}
	}

	private static async Task<T> ReadJsonAsync<T>( HttpResponseMessage response, CancellationToken cancel ) where T : class
	{
		await using var stream = await response.Content.ReadAsStreamAsync( cancel );
		return StrictJson.Deserialize<T>( await BoundedZip.ReadBoundedAsync( stream, MaximumJsonResponseBytes, "Server response", cancel ), "Server response" );
	}

	public void Dispose()
	{
		if ( ownsHttp )
			http.Dispose();
	}
}

/// <summary>An open chat WebSocket. Not thread-safe for concurrent sends.</summary>
public sealed class ChatConnection : IAsyncDisposable, IDisposable
{
	private readonly ClientWebSocket socket;
	private readonly SemaphoreSlim sendLock = new( 1, 1 );

	internal ChatConnection( ClientWebSocket socket ) => this.socket = socket;

	public bool IsOpen => socket.State == WebSocketState.Open;

	/// <summary>Why the server closed the connection, once it has (<see cref="ChatProtocol.RestartingCloseStatus"/> on a restart).</summary>
	public int? CloseStatus => socket.CloseStatus is { } status ? (int)status : null;

	public Task SendAsync( ChatCommand command, string argument, CancellationToken cancel = default ) => SendAsync( ChatRequest.ForCommand( command, argument ), cancel );

	public Task JoinAsync( string room, CancellationToken cancel = default ) => SendAsync( ChatRequest.ForJoin( room ), cancel );

	/// <summary>Moves the player's avatar in the chat park: a position in the park, both axes 0 to 1.</summary>
	public Task MoveAsync( float x, float y, CancellationToken cancel = default ) => SendAsync( ChatRequest.ForMove( x, y ), cancel );

	/// <summary>Moves the player's avatar with a mode (a server of <see cref="ChatProtocol.MoveModesVersion"/> or later): a walk, a teleport, hidden or shown again.</summary>
	public Task MoveAsync( float x, float y, ChatMoveMode mode, CancellationToken cancel = default ) => SendAsync( ChatRequest.ForMove( x, y, mode ), cancel );

	public async Task SendAsync( ChatRequest request, CancellationToken cancel = default )
	{
		var bytes = StrictJson.Serialize( request );
		await sendLock.WaitAsync( cancel );
		try
		{
			await socket.SendAsync( bytes, WebSocketMessageType.Text, true, cancel );
		}
		finally
		{
			sendLock.Release();
		}
	}

	/// <summary>Next event, or null when the server closed the connection.</summary>
	public async Task<ChatEvent?> ReceiveAsync( CancellationToken cancel = default )
	{
		var buffer = new byte[ChatProtocol.MaximumFrameBytes];
		var length = 0;
		while ( true )
		{
			if ( length == buffer.Length )
				throw new InvalidDataException( "Chat frame exceeds the size limit." );
			var result = await socket.ReceiveAsync( buffer.AsMemory( length ), cancel );
			if ( result.MessageType == WebSocketMessageType.Close )
			{
				// Complete the close handshake at once, so a restarting server need not wait for it.
				try
				{
					using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 2 ) );
					await socket.CloseOutputAsync( WebSocketCloseStatus.NormalClosure, "bye", timeout.Token );
				}
				catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException or ObjectDisposedException )
				{
				}
				return null;
			}
			if ( result.MessageType != WebSocketMessageType.Text )
				throw new InvalidDataException( "Chat frames must be text." );
			length += result.Count;
			if ( result.EndOfMessage )
				return ChatEvent.Parse( buffer.AsSpan( 0, length ) );
		}
	}

	public async ValueTask DisposeAsync()
	{
		try
		{
			if ( socket.State == WebSocketState.Open )
			{
				using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 2 ) );
				await socket.CloseAsync( WebSocketCloseStatus.NormalClosure, "bye", timeout.Token );
			}
		}
		catch ( Exception exception ) when ( exception is WebSocketException or OperationCanceledException )
		{
		}
		socket.Dispose();
		sendLock.Dispose();
	}

	public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
