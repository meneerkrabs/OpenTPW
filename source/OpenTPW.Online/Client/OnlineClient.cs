using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using OpenTPW.Online.Api;
using OpenTPW.Online.Chat;
using OpenTPW.Online.Packages;

namespace OpenTPW.Online.Client;

public sealed class OnlineException : Exception
{
	public OnlineException( HttpStatusCode status, string message ) : base( message ) => Status = status;
	public HttpStatusCode Status { get; }
}

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

	/// <summary>http(s) only, no credentials or query in the URL.</summary>
	public static Uri ValidateServerUrl( Uri url )
	{
		if ( !url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty( url.UserInfo ) || !string.IsNullOrEmpty( url.Query ) || !string.IsNullOrEmpty( url.Fragment ) )
			throw new ArgumentException( "The server URL must be an http:// or https:// address without credentials, query or fragment." );
		var text = url.ToString();
		return new Uri( text.EndsWith( '/' ) ? text : text + "/" );
	}

	public Task<ServerInfo> GetServerInfoAsync( CancellationToken cancel = default ) => GetJsonAsync<ServerInfo>( ApiRoutes.Server, cancel );

	public async Task RegisterAsync( string name, string password, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Post, ApiRoutes.Accounts, Json( new Credentials( name, password ) ), cancel, authenticated: false );
	}

	public async Task<SessionToken> LoginAsync( string name, string password, CancellationToken cancel = default )
	{
		using var response = await SendAsync( HttpMethod.Post, ApiRoutes.Sessions, Json( new Credentials( name, password ) ), cancel, authenticated: false );
		Session = await ReadJsonAsync<SessionToken>( response, cancel );
		return Session;
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

	public Task<ParkList> ListParksAsync( string? search = null, string sort = "recent", string? author = null, CancellationToken cancel = default )
	{
		var query = $"?sort={Uri.EscapeDataString( sort )}";
		if ( !string.IsNullOrEmpty( search ) )
			query += $"&search={Uri.EscapeDataString( search )}";
		if ( !string.IsNullOrEmpty( author ) )
			query += $"&author={Uri.EscapeDataString( author )}";
		return GetJsonAsync<ParkList>( ApiRoutes.Parks + query, cancel );
	}

	public async Task<ParkSummary> UploadParkAsync( ParkPackage package, CancellationToken cancel = default )
	{
		var content = new ByteArrayContent( package.ToBytes() );
		content.Headers.ContentType = new MediaTypeHeaderValue( ApiRoutes.PackageMediaType );
		using var response = await SendAsync( HttpMethod.Post, ApiRoutes.Parks, content, cancel );
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

	public Task<PostcardInbox> GetInboxAsync( CancellationToken cancel = default ) => GetJsonAsync<PostcardInbox>( ApiRoutes.Inbox, cancel );

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
		var socket = new ClientWebSocket();
		// Browsers cannot set WebSocket headers or keep-alive; there the token is the first frame.
		var browser = OperatingSystem.IsBrowser();
		if ( !browser )
		{
			socket.Options.SetRequestHeader( "Authorization", "Bearer " + session.Token );
			socket.Options.KeepAliveInterval = TimeSpan.FromSeconds( 30 );
		}
		var builder = new UriBuilder( new Uri( ServerUrl, ApiRoutes.Chat.TrimStart( '/' ) ) );
		builder.Scheme = builder.Scheme == "https" ? "wss" : "ws";
		try
		{
			await socket.ConnectAsync( builder.Uri, cancel );
			if ( browser )
				await socket.SendAsync( StrictJson.Serialize( ChatAuthentication.For( session.Token ) ), WebSocketMessageType.Text, true, cancel );
		}
		catch
		{
			socket.Dispose();
			throw;
		}
		return new ChatConnection( socket );
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
			try
			{
				await using var stream = await response.Content.ReadAsStreamAsync( cancel );
				var body = await BoundedZip.ReadBoundedAsync( stream, 16 * 1024, "Error response", cancel );
				message = StrictJson.Deserialize<ApiError>( body, "Error response" ).Error;
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or IOException )
			{
			}
			throw new OnlineException( response.StatusCode, $"{(int)response.StatusCode}: {message}" );
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

	public Task SendAsync( ChatCommand command, string argument, CancellationToken cancel = default ) => SendAsync( ChatRequest.ForCommand( command, argument ), cancel );

	public Task JoinAsync( string room, CancellationToken cancel = default ) => SendAsync( ChatRequest.ForJoin( room ), cancel );

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
				return null;
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
