using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Chat;
using OpenTPW.Online.Moderation;
using OpenTPW.Online.Packages;

namespace OpenTPW.Server;

/// <summary>
/// [EXT:ONLINE-054] Self-hostable OpenTPW server (ASP.NET Core minimal APIs). See docs/SERVER.md.
/// It stores player-made park packages and postcards only; it never contains original game assets.
/// </summary>
public static class ServerProgram
{
	public const string Version = "1.0";
	public const int ProtocolVersion = 1;
	private const string WebsitePolicy = "website";
	/// <summary>File in the data folder that asks the host to update the server (deploy/opentpw-deploy.path).</summary>
	public const string DeployRequestFile = ".deploy-requested";
	/// <summary>Parks listed on the project website.</summary>
	public const int MaximumWebsiteParks = 10;
	private const int MaximumJsonBodyBytes = 8 * 1024;

	public static async Task<int> Main( string[] args )
	{
		var app = Build( args );
		await app.RunAsync();
		return 0;
	}

	public static WebApplication Build( string[] args, Action<ServerOptions>? configure = null )
	{
		var builder = WebApplication.CreateBuilder( new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory } );
		var options = new ServerOptions();
		builder.Configuration.GetSection( ServerOptions.Section ).Bind( options );
		configure?.Invoke( options );
		options.Validate();

		builder.WebHost.ConfigureKestrel( kestrel =>
		{
			kestrel.AddServerHeader = false;
			kestrel.Limits.MaxRequestBodySize = ParkPackage.MaximumPackageBytes + 4096;
			kestrel.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
			kestrel.Limits.MaxConcurrentConnections = options.MaximumChatConnections + 256;
		} );

		var filter = string.IsNullOrWhiteSpace( options.FilterDirectory ) ? WordFilter.Empty : WordFilter.LoadDirectory( options.FilterDirectory );
		var store = new ServerStore( options );
		var hub = new ChatHub( store, options, filter );
		builder.Services.AddSingleton( options );
		builder.Services.AddSingleton( store );
		builder.Services.AddSingleton( filter );
		builder.Services.AddSingleton( hub );
		// [EXT:ONLINE-057] the project website reads the public news and website park list from the browser
		builder.Services.AddCors( cors => cors.AddPolicy( WebsitePolicy, policy => policy.WithOrigins( options.WebsiteOrigins.ToArray() ).WithMethods( "GET" ) ) );
		builder.Services.AddRateLimiter( limiter =>
		{
			limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>( context => RateLimitPartition.GetFixedWindowLimiter( Address( context ),
				_ => new FixedWindowRateLimiterOptions { PermitLimit = options.RequestsPerMinute, Window = TimeSpan.FromMinutes( 1 ), QueueLimit = 0 } ) );
			limiter.AddPolicy( "auth", context => RateLimitPartition.GetFixedWindowLimiter( Address( context ),
				_ => new FixedWindowRateLimiterOptions { PermitLimit = options.AuthenticationsPerMinute, Window = TimeSpan.FromMinutes( 1 ), QueueLimit = 0 } ) );
			limiter.AddPolicy( "upload", context => RateLimitPartition.GetFixedWindowLimiter( Address( context ),
				_ => new FixedWindowRateLimiterOptions { PermitLimit = options.UploadsPerHour, Window = TimeSpan.FromHours( 1 ), QueueLimit = 0 } ) );
		} );

		var app = builder.Build();
		app.Logger.LogInformation( "OpenTPW server data in {Directory}; word filter: {Swears} entries, {Alloweds} exceptions.", store.Root, filter.SwearCount, filter.AllowedCount );
		app.Use( async ( context, next ) =>
		{
			context.Response.Headers.XContentTypeOptions = "nosniff";
			context.Response.Headers["Referrer-Policy"] = "no-referrer";
			context.Response.Headers.CacheControl = "no-store";
			await next();
		} );
		// Before the rate limiter: one page load of the browser game fetches over a hundred files.
		if ( !string.IsNullOrWhiteSpace( options.WebClientDirectory ) )
			app.UseWebClient( options.WebClientDirectory );
		app.UseWebSockets( new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds( 30 ) } );
		app.UseCors();
		app.UseRateLimiter();
		Map( app, options, store, hub, filter );
		app.Lifetime.ApplicationStopping.Register( hub.Restarting );
		return app;
	}

	/// <summary>Typed handler so minimal APIs write the returned result (a bare HttpContext lambda would bind as RequestDelegate).</summary>
	private static Delegate Handler( Func<HttpContext, Task<IResult>> handler ) => handler;

	private static string Address( HttpContext context ) => RateLimitKey( context.Connection.RemoteIpAddress );

	/// <summary>
	/// The rate-limit partition of a client address: IPv4 as is, IPv6 by its /64, because one host usually has a whole
	/// /64 and could otherwise use a fresh address, and so a fresh limit, for every request.
	/// </summary>
	public static string RateLimitKey( System.Net.IPAddress? address )
	{
		if ( address == null )
			return "unknown";
		if ( address.IsIPv4MappedToIPv6 )
			address = address.MapToIPv4();
		if ( address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6 )
			return address.ToString();
		var bytes = address.GetAddressBytes();
		Array.Clear( bytes, 8, 8 );
		return new System.Net.IPAddress( bytes ) + "/64";
	}

	private static IResult Error( int status, string message ) => Results.Json( new ApiError( message ), StrictJson.Options, statusCode: status );

	private static IResult Ok<T>( T value, int status = StatusCodes.Status200OK ) => Results.Json( value, StrictJson.Options, statusCode: status );

	private static string? Token( HttpContext context )
	{
		var header = context.Request.Headers.Authorization.ToString();
		return header.StartsWith( "Bearer ", StringComparison.Ordinal ) ? header[7..].Trim() : null;
	}

	/// <summary>Reads a <see cref="ChatAuthentication"/> first frame; null when it is missing, late, too large or wrong.</summary>
	private static async Task<AccountRecord?> AuthenticateFirstFrameAsync( System.Net.WebSockets.WebSocket socket, ServerStore store, CancellationToken aborted )
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource( aborted );
		timeout.CancelAfter( ChatAuthentication.Timeout );
		var buffer = new byte[ChatAuthentication.MaximumBytes];
		var length = 0;
		try
		{
			while ( true )
			{
				var result = await socket.ReceiveAsync( buffer.AsMemory( length ), timeout.Token );
				if ( result.MessageType != System.Net.WebSockets.WebSocketMessageType.Text )
					return null;
				length += result.Count;
				if ( result.EndOfMessage )
					break;
				if ( length == buffer.Length )
					return null;
			}
			return store.Authenticate( ChatAuthentication.ParseToken( buffer.AsSpan( 0, length ) ) );
		}
		catch ( Exception exception ) when ( exception is OperationCanceledException or System.Net.WebSockets.WebSocketException or InvalidDataException or System.Text.Json.JsonException )
		{
			return null;
		}
	}

	private static bool IsId( string id ) => id.Length is >= 1 and <= 64 && id.All( char.IsAsciiLetterOrDigit );

	private static async Task<byte[]> ReadBodyAsync( HttpRequest request, int maximumBytes )
	{
		if ( request.ContentLength > maximumBytes )
			throw new StoreException( StatusCodes.Status413PayloadTooLarge, "File is too large." );
		var feature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
		if ( feature is { IsReadOnly: false } )
			feature.MaxRequestBodySize = maximumBytes + 1L;
		using var output = new MemoryStream();
		var buffer = new byte[81920];
		while ( true )
		{
			int count;
			try
			{
				count = await request.Body.ReadAsync( buffer, request.HttpContext.RequestAborted );
			}
			catch ( BadHttpRequestException )
			{
				throw new StoreException( StatusCodes.Status413PayloadTooLarge, "File is too large." );
			}
			if ( count == 0 )
				break;
			if ( output.Length + count > maximumBytes )
				throw new StoreException( StatusCodes.Status413PayloadTooLarge, "File is too large." );
			output.Write( buffer, 0, count );
		}
		return output.ToArray();
	}

	private static async Task<T> ReadJsonAsync<T>( HttpRequest request ) where T : class =>
		StrictJson.Deserialize<T>( await ReadBodyAsync( request, MaximumJsonBodyBytes ), "Request" );

	/// <summary>Runs a handler with the caller's account; maps store and format errors to JSON errors.</summary>
	private static async Task<IResult> Guarded( HttpContext context, ServerStore store, bool requireUser, Func<AccountRecord?, Task<IResult>> handler )
	{
		try
		{
			AccountRecord? user = null;
			if ( requireUser )
			{
				user = store.Authenticate( Token( context ) );
				if ( user == null )
					return Error( StatusCodes.Status401Unauthorized, "Authorisation failed." );
			}
			return await handler( user );
		}
		catch ( StoreException exception )
		{
			return Error( exception.Status, exception.Message );
		}
		catch ( InvalidDataException exception )
		{
			return Error( StatusCodes.Status400BadRequest, exception.Message );
		}
	}

	private static void RequireClean( WordFilter filter, string text, string what )
	{
		filter.Apply( text, out var changed );
		if ( changed )
			throw new StoreException( StatusCodes.Status400BadRequest, $"{what} contains words blocked by this server's filter." );
	}

	private static void Map( WebApplication app, ServerOptions options, ServerStore store, ChatHub hub, WordFilter filter )
	{
		app.MapGet( ApiRoutes.Server, () => Ok( new ServerInfo( options.ServerName, Version, ProtocolVersion, options.Message, options.AllowRegistration, !filter.IsEmpty,
			ParkPackage.MaximumPackageBytes, Postcard.MaximumCardBytes, options.MaximumParksPerPlayer, options.MaximumVotesPerDay ) ) );

		// Public like the server info: news is readable before logging in.
		// [EXT:ONLINE-058] a release asks the host to update: the server only leaves a file in its data folder, which the
		// host's opentpw-deploy.path unit turns into the regular update (deploy/). Without a DeployToken the route is absent.
		if ( !string.IsNullOrEmpty( options.DeployToken ) )
		{
			var expected = System.Text.Encoding.UTF8.GetBytes( options.DeployToken );
			app.MapPost( ApiRoutes.Deploy, ( HttpContext context ) =>
			{
				var token = Token( context );
				if ( token == null || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals( System.Text.Encoding.UTF8.GetBytes( token ), expected ) )
					return Error( StatusCodes.Status401Unauthorized, "Authorisation failed." );
				AtomicFile.Write( Path.Combine( store.Root, DeployRequestFile ), System.Text.Encoding.UTF8.GetBytes( DateTimeOffset.UtcNow.ToString( "O" ) ) );
				return Results.Accepted();
			} ).RequireRateLimiting( "auth" );
		}

		var news = new NewsFeed( options.DataDirectory );
		app.MapGet( ApiRoutes.News, () => Ok( news.Read() ) ).RequireCors( WebsitePolicy );

		// [EXT:ONLINE-057] public, for the project website: only parks whose authors opted in, at most ten, cached a minute.
		var websiteParks = new TimedCache<WebsiteParkList>( TimeSpan.FromMinutes( 1 ), () => new WebsiteParkList( store.WebsiteParks( MaximumWebsiteParks )
			.Select( park => new WebsitePark( park.Id, park.Name, park.Description, park.Author, park.Level, park.Votes, park.Visits, park.PublishedUtc, park.HasThumbnail ) ).ToList() ) );
		app.MapGet( ApiRoutes.WebsiteParks, () => Ok( websiteParks.Get() ) ).RequireCors( WebsitePolicy );
		app.MapGet( ApiRoutes.WebsiteParks + "/{id}/thumbnail", ( HttpContext context, string id ) =>
		{
			var park = IsId( id ) ? store.FindPark( id ) : null;
			if ( park is not { ShowOnWebsite: true, HasThumbnail: true } )
				return Error( StatusCodes.Status404NotFound, "No such park picture." );
			context.Response.Headers.CacheControl = "public, max-age=3600";
			return Results.File( store.ParkFile( park.Id, ".png" ), "image/png" );
		} );

		app.MapPost( ApiRoutes.Accounts, Handler( context => Guarded( context, store, false, async _ =>
		{
			var credentials = await ReadJsonAsync<Credentials>( context.Request );
			RequireClean( filter, credentials.Name ?? "", "Name" );
			var account = store.Register( credentials.Name ?? "", credentials.Password ?? "" );
			return Ok( new { name = account.Name }, StatusCodes.Status201Created );
		} ) ) ).RequireRateLimiting( "auth" );

		// Deleting an account asks for the password again, not just a session token.
		app.MapDelete( ApiRoutes.Accounts, Handler( context => Guarded( context, store, true, async user =>
		{
			var credentials = await ReadJsonAsync<Credentials>( context.Request );
			if ( !string.Equals( OnlineText.NormalizeName( credentials.Name ?? "" ), user!.Key, StringComparison.Ordinal ) )
				throw new StoreException( 400, "Name the account you are deleting." );
			store.DeleteAccount( user, credentials.Password ?? "" );
			hub.Disconnect( user.Key );
			return Results.NoContent();
		} ) ) ).RequireRateLimiting( "auth" );

		app.MapPost( ApiRoutes.Sessions, Handler( context => Guarded( context, store, false, async _ =>
		{
			var credentials = await ReadJsonAsync<Credentials>( context.Request );
			return Ok( store.Login( credentials.Name ?? "", credentials.Password ?? "" ) );
		} ) ) ).RequireRateLimiting( "auth" );

		app.MapDelete( ApiRoutes.Sessions, Handler( context => Guarded( context, store, true, _ =>
		{
			store.Logout( Token( context )! );
			return Task.FromResult( Results.NoContent() );
		} ) ) );

		app.MapGet( ApiRoutes.Parks, ( HttpContext context, string? search, string? author, string? sort, int? offset, int? limit ) => Guarded( context, store, true, user =>
		{
			if ( search?.Length > 64 || author?.Length > OnlineText.MaximumNameLength )
				throw new StoreException( StatusCodes.Status400BadRequest, "Search text is too long." );
			var parks = store.ListParks( search, author, sort ?? "recent" );
			var page = parks.Skip( Math.Clamp( offset ?? 0, 0, int.MaxValue ) ).Take( Math.Clamp( limit ?? 50, 1, 100 ) )
				.Select( park => store.Summarize( park, user, hub.PlayersIn( Online.Chat.ChatProtocol.ParkRoom( park.Id ) ) ) ).ToList();
			return Task.FromResult( Ok( new ParkList( page, parks.Count ) ) );
		} ) );

		app.MapPost( ApiRoutes.Parks, Handler( context => Guarded( context, store, true, async user =>
		{
			var bytes = await ReadBodyAsync( context.Request, ParkPackage.MaximumPackageBytes );
			var package = ParkPackage.Read( bytes );
			RequireClean( filter, package.Manifest.Park.Name, "Park name" );
			RequireClean( filter, package.Manifest.Park.Description, "Park description" );
			var park = store.Publish( user!, package, bytes, context.Request.Query["website"] == "true" );
			return Ok( store.Summarize( park, user, 0 ), StatusCodes.Status201Created );
		} ) ) ).RequireRateLimiting( "upload" );

		app.MapGet( ApiRoutes.Parks + "/{id}", ( HttpContext context, string id ) => Guarded( context, store, true, user =>
		{
			var park = (IsId( id ) ? store.FindPark( id ) : null) ?? throw new StoreException( StatusCodes.Status404NotFound, "Park ID is invalid." );
			return Task.FromResult( Ok( store.Summarize( park, user, hub.PlayersIn( Online.Chat.ChatProtocol.ParkRoom( park.Id ) ) ) ) );
		} ) );

		app.MapGet( ApiRoutes.Parks + "/{id}/package", ( HttpContext context, string id ) => Guarded( context, store, true, _ =>
		{
			var park = (IsId( id ) ? store.FindPark( id ) : null) ?? throw new StoreException( StatusCodes.Status404NotFound, "Park ID is invalid." );
			return Task.FromResult( Results.File( store.ParkFile( park.Id, ParkPackage.FileExtension ), ApiRoutes.PackageMediaType, park.Id + ParkPackage.FileExtension ) );
		} ) );

		app.MapGet( ApiRoutes.Parks + "/{id}/thumbnail", ( HttpContext context, string id ) => Guarded( context, store, true, _ =>
		{
			var park = (IsId( id ) ? store.FindPark( id ) : null) ?? throw new StoreException( StatusCodes.Status404NotFound, "Park ID is invalid." );
			if ( !park.HasThumbnail )
				throw new StoreException( StatusCodes.Status404NotFound, "This park has no thumbnail." );
			return Task.FromResult( Results.File( store.ParkFile( park.Id, ".png" ), "image/png" ) );
		} ) );

		app.MapPost( ApiRoutes.Parks + "/{id}/visits", ( HttpContext context, string id ) => Guarded( context, store, true, user =>
			Task.FromResult( IsId( id ) ? Ok( store.Visit( user!, id ) ) : Error( StatusCodes.Status404NotFound, "Park ID is invalid." ) ) ) );

		app.MapPost( ApiRoutes.Parks + "/{id}/votes", ( HttpContext context, string id ) => Guarded( context, store, true, user =>
			Task.FromResult( IsId( id ) ? Ok( store.Vote( user!, id ) ) : Error( StatusCodes.Status404NotFound, "Park ID is invalid." ) ) ) );

		app.MapDelete( ApiRoutes.Parks + "/{id}", ( HttpContext context, string id ) => Guarded( context, store, true, user =>
		{
			if ( !IsId( id ) )
				throw new StoreException( StatusCodes.Status404NotFound, "Park ID is invalid." );
			store.Unpublish( user!, id );
			return Task.FromResult( Results.NoContent() );
		} ) );

		app.MapPost( ApiRoutes.Postcards, Handler( context => Guarded( context, store, true, async user =>
		{
			var bytes = await ReadBodyAsync( context.Request, Postcard.MaximumCardBytes );
			var card = Postcard.Read( bytes );
			if ( OnlineText.NormalizeName( card.Manifest.From ) != user!.Key )
				throw new StoreException( StatusCodes.Status400BadRequest, "The postcard sender must be the logged-in player." );
			RequireClean( filter, card.Manifest.Title, "Postcard title" );
			RequireClean( filter, card.Manifest.Text, "Postcard text" );
			var (id, delivered) = store.SendPostcard( user, card, bytes );
			return Ok( new PostcardSent( id, delivered ), StatusCodes.Status201Created );
		} ) ) ).RequireRateLimiting( "upload" );

		app.MapGet( ApiRoutes.Inbox, Handler( context => Guarded( context, store, true, user =>
			Task.FromResult( Ok( new PostcardInbox( store.Inbox( user! ) ) ) ) ) ) );

		app.MapGet( ApiRoutes.Postcards + "/{id}", ( HttpContext context, string id ) => Guarded( context, store, true, user =>
		{
			if ( !IsId( id ) )
				throw new StoreException( StatusCodes.Status404NotFound, "Postcard not found." );
			return Task.FromResult( Results.File( store.OwnPostcardFile( user!, id ), ApiRoutes.PostcardMediaType ) );
		} ) );

		app.MapDelete( ApiRoutes.Postcards + "/{id}", ( HttpContext context, string id ) => Guarded( context, store, true, user =>
		{
			if ( !IsId( id ) )
				throw new StoreException( StatusCodes.Status404NotFound, "Postcard not found." );
			store.DeletePostcard( user!, id );
			return Task.FromResult( Results.NoContent() );
		} ) );

		app.MapPost( ApiRoutes.Reports, Handler( context => Guarded( context, store, true, async user =>
		{
			var report = await ReadJsonAsync<ReportRequest>( context.Request );
			if ( !ReportKinds.IsKnown( report.Kind ) )
				throw new StoreException( StatusCodes.Status400BadRequest, "Unknown report kind." );
			OnlineText.RequireText( report.Target, 64, false, false, "Report target" );
			OnlineText.RequireText( report.Reason, 500, true, true, "Report reason" );
			store.Report( user!, report.Kind, report.Target, report.Reason );
			return Results.Accepted();
		} ) ) ).RequireRateLimiting( "upload" );

		var authenticating = 0;
		app.Map( ApiRoutes.Chat, async ( HttpContext context ) =>
		{
			if ( !context.WebSockets.IsWebSocketRequest )
			{
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
				return;
			}
			// Desktop clients send the token as a header; browsers cannot, and send it as the first frame.
			var token = Token( context );
			var user = token == null ? null : store.Authenticate( token );
			if ( token != null && user == null )
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				return;
			}
			// Unauthenticated sockets are bounded like chat connections before they are accepted.
			var waiting = user == null;
			if ( waiting && Interlocked.Increment( ref authenticating ) + hub.ConnectedCount > options.MaximumChatConnections )
			{
				Interlocked.Decrement( ref authenticating );
				context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
				return;
			}
			try
			{
				using var socket = await context.WebSockets.AcceptWebSocketAsync();
				user ??= await AuthenticateFirstFrameAsync( socket, store, context.RequestAborted );
				if ( waiting )
				{
					waiting = false;
					Interlocked.Decrement( ref authenticating );
				}
				if ( user == null )
				{
					await ChatHub.CloseAsync( socket, System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation, "unauthorized" );
					return;
				}
				await hub.RunAsync( socket, user, context.RequestAborted );
			}
			finally
			{
				if ( waiting )
					Interlocked.Decrement( ref authenticating );
			}
		} );
	}
}
