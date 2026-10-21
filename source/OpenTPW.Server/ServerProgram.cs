using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using OpenTPW.Online;
using OpenTPW.Online.Api;
using OpenTPW.Online.Chat;
using OpenTPW.Online.Coop;
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
	public const string BuildInfoRoute = "/api/v1/build-info";
	/// <summary>
	/// 2: registration may answer 202 with a two-step setup (docs/ONLINE.md, "Two-step login"). 4: the chat relays movement in park rooms and
	/// answers each room's first movement with where everyone stands, which a visit's online players rely on (docs/ONLINE.md, "Players in a visited park").
	/// 5: movements carry a mode (<see cref="ChatMoveMode"/>, <see cref="ChatProtocol.MoveModesVersion"/>), so a player riding a ride is hidden.
	/// 6: the co-op socket of shared parks (<see cref="CoopProtocol"/>), their open list and join snapshots.
	/// </summary>
	public const int ProtocolVersion = CoopProtocol.Version;
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

	/// <param name="emailSender">Replaces the Resend sender (tests); null uses <see cref="ServerOptions.ResendApiKey"/> when set.</param>
	public static WebApplication Build( string[] args, Action<ServerOptions>? configure = null, IEmailSender? emailSender = null )
	{
		// Options are bound once below, so appsettings.json changes need a restart anyway. Watching the file only adds a
		// file watcher, and on macOS starting one calls sync(2), which never returns while any mounted volume (a stalled
		// SMB share) hangs. Later arguments win, so an operator can still pass --hostBuilder:reloadConfigOnChange=true.
		var builder = WebApplication.CreateBuilder( new WebApplicationOptions { Args = [ "--hostBuilder:reloadConfigOnChange=false", .. args ], ContentRootPath = AppContext.BaseDirectory } );
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
		var chatParks = new ChatParkDirectory( options.DataDirectory );
		var cities = new CityDirectory( options.DataDirectory );
		var hub = new ChatHub( store, options, filter, chatParks, cities );
		var coop = new CoopHub( store, options, filter, new CoopStore( options.DataDirectory ) );
		builder.Services.AddSingleton( options );
		builder.Services.AddSingleton( store );
		builder.Services.AddSingleton( chatParks );
		builder.Services.AddSingleton( cities );
		builder.Services.AddSingleton( filter );
		builder.Services.AddSingleton( hub );
		builder.Services.AddSingleton( coop );
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
		if ( !store.DataDirectoryIsPrivate )
			app.Logger.LogWarning( "The data directory {Directory} may be readable by other users (it could not be restricted or checked); it holds password hashes and authenticator secrets.", store.Root );
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
		// [EXT:online-email] account e-mails go through Resend when a key is set; without one, e-mail is off
		emailSender ??= string.IsNullOrWhiteSpace( options.ResendApiKey ) || string.IsNullOrWhiteSpace( options.EmailFrom )
			? null : new ResendEmailSender( options.ResendApiKey, options.EmailFrom, options.EmailReplyTo );
		Map( app, options, store, hub, coop, chatParks, cities, filter, emailSender );
		app.Lifetime.ApplicationStopping.Register( hub.Restarting );
		app.Lifetime.ApplicationStopping.Register( coop.Restarting );
		// [EXT:online-coop-session] ends the shared parks whose host stayed away, once a minute
		app.Lifetime.ApplicationStarted.Register( () => _ = Task.Run( async () =>
		{
			using var timer = new PeriodicTimer( TimeSpan.FromMinutes( 1 ) );
			try
			{
				while ( await timer.WaitForNextTickAsync( app.Lifetime.ApplicationStopping ) )
					coop.Sweep( DateTimeOffset.UtcNow );
			}
			catch ( OperationCanceledException ) { }
		} ) );
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
	private static async Task<(AccountRecord? User, string? Token)> AuthenticateFirstFrameAsync( System.Net.WebSockets.WebSocket socket, ServerStore store, CancellationToken aborted )
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
					return (null, null);
				length += result.Count;
				if ( result.EndOfMessage )
					break;
				if ( length == buffer.Length )
					return (null, null);
			}
			var token = ChatAuthentication.ParseToken( buffer.AsSpan( 0, length ) );
			return (store.Authenticate( token ), token);
		}
		catch ( Exception exception ) when ( exception is OperationCanceledException or System.Net.WebSockets.WebSocketException or InvalidDataException or System.Text.Json.JsonException )
		{
			return (null, null);
		}
	}

	internal static bool IsId( string id ) => id.Length is >= 1 and <= 64 && id.All( char.IsAsciiLetterOrDigit );

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
			return Results.Json( new ApiError( exception.Message, exception.Reason ), StrictJson.Options, statusCode: exception.Status );
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

	/// <summary>
	/// A player's page: every published park with its visitors, votes, publish date, current rank and highest rank, and
	/// the player's Platinum Tickets (docs/reverse/ONLINE-SOURCES.md S1). A player name is 50 characters at most, like
	/// the server's own name checks.
	/// </summary>
	private static PlayerProfile Profile( ServerStore store, string name, AccountRecord? viewer, CityDirectory cities )
	{
		if ( name.Length > OnlineText.MaximumNameLength )
			throw new StoreException( StatusCodes.Status400BadRequest, "Player name is too long." );
		var (author, platinum, parks) = store.PlayerParks( name, viewer );
		return new PlayerProfile( author, platinum, parks.Select( entry => new PlayerPark(
			entry.Park.Name, entry.Park.Visits, entry.Park.Votes, entry.Park.PublishedUtc, entry.Rank, entry.Park.HighestRank ) ).ToList() );
	}

	private static void Map( WebApplication app, ServerOptions options, ServerStore store, ChatHub hub, CoopHub coop, ChatParkDirectory chatParks, CityDirectory cities, WordFilter filter,
		IEmailSender? emailSender )
	{
		app.MapGet( BuildInfoRoute, () => Ok( ServerBuildInfo.Current ) );
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

		app.MapGet( ApiRoutes.Cities, () =>
		{
			var counts = store.ParksPerCity();
			return Ok( new CityList( cities.Read().Select( city => new CityInfo( city.Id, city.Name, city.Description, city.Latitude, city.Longitude,
				counts.GetValueOrDefault( city.Id ) ) ).ToList() ) );
		} );

		// The globe's five chat satellites: each chat park's room and the players in it (docs/SERVER.md).
		app.MapGet( ApiRoutes.ChatParks, () => Ok( new ChatParkList( chatParks.WithPlayers( hub.PlayersIn ) ) ) );
		// Their park details and the parks their visits enter; a login is needed, as for any park summary.
		app.MapGet( ApiRoutes.ChatParkScenes, Handler( context => Guarded( context, store, true, user =>
			Task.FromResult( Ok( new ChatParkSceneList( chatParks.Scenes( id => IsId( id ) && store.FindPark( id ) is { } park
				? store.Summarize( park, user, hub.PlayersIn( Online.Chat.ChatProtocol.ParkRoom( park.Id ) ) ) : null ) ) ) ) ) ) );

		// The original website's pages (docs/reverse/ONLINE-SOURCES.md S1), as public, privacy-respecting endpoints:
		// only parks whose authors chose to show them on the website appear without a login.
		app.MapGet( ApiRoutes.ChartParks, () => Ok( new ChartParkList( store.TopParks( 10 )
			.Select( entry => new ChartParkEntry( entry.Rank, entry.Name, entry.Player ) ).ToList() ) ) ).RequireCors( WebsitePolicy );
		app.MapGet( ApiRoutes.ChartPlayers, () => Ok( new ChartPlayerList( store.PlayerChart( 10, byPlatinum: false )
			.Select( ( entry, index ) => new ChartPlayerEntry( index + 1, entry.Name, entry.Score ) ).ToList() ) ) ).RequireCors( WebsitePolicy );
		app.MapGet( ApiRoutes.ChartRichest, () => Ok( new ChartPlayerList( store.PlayerChart( 10, byPlatinum: true )
			.Select( ( entry, index ) => new ChartPlayerEntry( index + 1, entry.Name, entry.Score ) ).ToList() ) ) ).RequireCors( WebsitePolicy );
		app.MapGet( ApiRoutes.Players, ( HttpContext context, string? search ) =>
		{
			if ( string.IsNullOrWhiteSpace( search ) || search.Length > OnlineText.MaximumNameLength )
				return Ok( new PlayerSearchResult( Array.Empty<string>(), More: false ) );
			try
			{
				var (names, more) = store.SearchPlayers( search.Trim(), store.Authenticate( Token( context ) ) );
				return Ok( new PlayerSearchResult( names, more ) );
			}
			catch ( StoreException exception )
			{
				return Error( exception.Status, exception.Message );
			}
		} ).RequireCors( WebsitePolicy );
		// The page is public, but a logged-in reader sees every published park of the player, not only the website ones.
		app.MapGet( ApiRoutes.Players + "/{name}", ( HttpContext context, string name ) =>
		{
			try
			{
				var viewer = store.Authenticate( Token( context ) );
				return Task.FromResult( Ok( Profile( store, name, viewer, cities ) ) );
			}
			catch ( StoreException exception )
			{
				return Task.FromResult( Error( exception.Status, exception.Message ) );
			}
		} );

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
			// With two-step login the account exists only after the first code: 202 and the authenticator secret.
			if ( options.RequireTotp )
				return Ok( store.BeginRegistration( credentials.Name ?? "", credentials.Password ?? "" ), StatusCodes.Status202Accepted );
			var account = store.Register( credentials.Name ?? "", credentials.Password ?? "" );
			return Ok( new { name = account.Name }, StatusCodes.Status201Created );
		} ) ) ).RequireRateLimiting( "auth" );

		app.MapPost( ApiRoutes.AccountConfirmation, Handler( context => Guarded( context, store, false, async _ =>
		{
			var confirmation = await ReadJsonAsync<TotpConfirmation>( context.Request );
			return Ok( store.ConfirmEnrollment( confirmation.Name ?? "", confirmation.Ticket ?? "", confirmation.Code ?? "", confirmation.EmailCode ), StatusCodes.Status201Created );
		} ) ) ).RequireRateLimiting( "auth" );

		// [EXT:online-email] the address of a new account, confirmed with a code; then recovery by e-mail.
		// E-mails are sent after the store has committed and outside its lock.
		app.MapPost( ApiRoutes.AccountEmail, Handler( context => Guarded( context, store, false, async _ =>
		{
			if ( emailSender == null )
				return Error( StatusCodes.Status404NotFound, "This server does not send e-mail." );
			var request = await ReadJsonAsync<EnrollmentEmail>( context.Request );
			var message = store.PrepareEnrollmentEmail( request.Name ?? "", request.Ticket ?? "", request.Email ?? "", request.Language );
			try { await emailSender.SendAsync( message, context.RequestAborted ); }
			catch ( EmailException exception )
			{
				app.Logger.LogWarning( "Account e-mail not sent: {Reason}", exception.Message );
				return Error( StatusCodes.Status502BadGateway, "The e-mail could not be sent; try again later." );
			}
			return Results.Accepted();
		} ) ) ).RequireRateLimiting( "auth" );

		app.MapPost( ApiRoutes.Recovery, Handler( context => Guarded( context, store, false, async _ =>
		{
			// Without mail there is no recovery: that tells about the server, not about any account.
			if ( emailSender == null )
				return Error( StatusCodes.Status404NotFound, "This server does not send e-mail; ask its operator." );
			var request = await ReadJsonAsync<RecoveryRequest>( context.Request );
			// The same answer whether the name or address exists, has an address, or hit the e-mail limit.
			// The e-mail goes out in the background, so the answer takes as long either way.
			if ( store.PrepareRecovery( request.NameOrEmail ?? "", request.Language ) is { } message )
			{
				var sending = Task.Run( async () =>
				{
					try { await emailSender.SendAsync( message, CancellationToken.None ); }
					catch ( EmailException exception ) { app.Logger.LogWarning( "Recovery e-mail not sent: {Reason}", exception.Message ); }
				} );
			}
			return Results.Accepted();
		} ) ) ).RequireRateLimiting( "auth" );

		app.MapPost( ApiRoutes.RecoveryConfirmation, Handler( context => Guarded( context, store, false, async _ =>
		{
			var request = await ReadJsonAsync<RecoveryConfirmation>( context.Request );
			var (account, notice) = store.ConfirmRecovery( request.NameOrEmail ?? "", request.Code ?? "", request.Password ?? "", request.ResetTwoStep, request.Language );
			hub.Disconnect( account.Key );
			coop.Disconnect( account.Key );
			if ( emailSender != null )
			{
				try { await emailSender.SendAsync( notice, context.RequestAborted ); }
				catch ( EmailException exception ) { app.Logger.LogWarning( "Password notice not sent: {Reason}", exception.Message ); }
			}
			return Results.NoContent();
		} ) ) ).RequireRateLimiting( "auth" );

		// Deleting an account asks for the password again, not just a session token.
		app.MapDelete( ApiRoutes.Accounts, Handler( context => Guarded( context, store, true, async user =>
		{
			var credentials = await ReadJsonAsync<Credentials>( context.Request );
			if ( !string.Equals( OnlineText.NormalizeName( credentials.Name ?? "" ), user!.Key, StringComparison.Ordinal ) )
				throw new StoreException( 400, "Name the account you are deleting." );
			store.DeleteAccount( user, credentials.Password ?? "" );
			hub.Disconnect( user.Key );
			coop.RemoveAccount( user.Key );
			return Results.NoContent();
		} ) ) ).RequireRateLimiting( "auth" );

		app.MapPost( ApiRoutes.Sessions, Handler( context => Guarded( context, store, false, async _ =>
		{
			var credentials = await ReadJsonAsync<Credentials>( context.Request );
			var outcome = store.Login( credentials.Name ?? "", credentials.Password ?? "", credentials.Code );
			return outcome.Enrollment != null ? Ok( outcome.Enrollment, StatusCodes.Status202Accepted ) : Ok( outcome.Session! );
		} ) ) ).RequireRateLimiting( "auth" );

		app.MapDelete( ApiRoutes.Sessions, Handler( context => Guarded( context, store, true, _ =>
		{
			store.Logout( Token( context )! );
			return Task.FromResult( Results.NoContent() );
		} ) ) );

		app.MapGet( ApiRoutes.Parks, ( HttpContext context, string? search, string? author, string? sort, int? offset, int? limit, int? city ) => Guarded( context, store, true, user =>
		{
			if ( search?.Length > 64 || author?.Length > OnlineText.MaximumNameLength )
				throw new StoreException( StatusCodes.Status400BadRequest, "Search text is too long." );
			var parks = store.ListParks( search, author, sort ?? "recent", city );
			var page = parks.Skip( Math.Clamp( offset ?? 0, 0, int.MaxValue ) ).Take( Math.Clamp( limit ?? 50, 1, 100 ) )
				.Select( park => store.Summarize( park, user, hub.PlayersIn( Online.Chat.ChatProtocol.ParkRoom( park.Id ) ) ) ).ToList();
			return Task.FromResult( Ok( new ParkList( page, parks.Count ) ) );
		} ) );

		app.MapPost( ApiRoutes.Parks, Handler( context => Guarded( context, store, true, async user =>
		{
			var bytes = await ReadBodyAsync( context.Request, ParkPackage.MaximumPackageBytes );
			var package = ParkPackage.Read( bytes );
			// The payload is validated as a park payload too (format, version, sizes, digest over its layout):
			// clients check on visit, but a publish is the last place to refuse a package no client could enter.
			MinimalParkPayload.FromSnapshot( package.ToSnapshot() );
			// [EXT:online-park-name-masking] the original showed swear words in park names as asterisks (FAQ, S2), so the
			// name is masked, not refused; the description keeps the refusal (an OpenTPW moderation choice). OpenTPW masks
			// once on the server at publish, so every client sees the same name. Not reproduced: the original masked at
			// display on the client: the city lobby's park label (tp.exe 0x4B8850), the park details (0x4C0AE0), Find Parks
			// (0x4BFC70) and the other-parks chart (0x4C1E40) pass the name through the filter 0x5D3230 when the filter flag
			// (0x5B09B0, byte +0x25) is set, with the swears.txt and alloweds.txt of Data\Language\<language> loaded by
			// 0x5AFD70 / 0x5D3140; still unknown: what sets the filter flag
			package = package with { Manifest = package.Manifest with { Park = package.Manifest.Park with { Name = filter.Apply( package.Manifest.Park.Name ) } } };
			RequireClean( filter, package.Manifest.Park.Description, "Park description" );
			var city = int.TryParse( context.Request.Query["city"], out var cityId ) ? cityId : 0;
			if ( city != 0 && cities.Find( city ) == null )
				throw new StoreException( StatusCodes.Status400BadRequest, "Unknown city." );
			var park = store.Publish( user!, package, bytes, context.Request.Query["website"] == "true", city );
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
			var sent = await store.SendPostcardAsync( user, card, emailSender, context.RequestAborted );
			return Ok( sent, StatusCodes.Status201Created );
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
				if ( user == null )
					(user, token) = await AuthenticateFirstFrameAsync( socket, store, context.RequestAborted );
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
				// Checked again once the connection is in the hub: a recovery or deletion that ended the session meanwhile
				// found no connection to close.
				await hub.RunAsync( socket, user, context.RequestAborted, () => store.Authenticate( token ) != null );
			}
			finally
			{
				if ( waiting )
					Interlocked.Decrement( ref authenticating );
			}
		} );

		MapCoop( app, options, store, coop );
	}

	/// <summary>
	/// [EXT:online-coop-session] The co-op socket, the open list of shared parks and their join snapshots ([EXT:online-coop-snapshot-join]); docs/ONLINE.md,
	/// "Shared parks".
	/// </summary>
	private static void MapCoop( WebApplication app, ServerOptions options, ServerStore store, CoopHub coop )
	{
		app.Map( ApiRoutes.Coop, async ( HttpContext context ) =>
		{
			if ( !context.WebSockets.IsWebSocketRequest )
			{
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
				return;
			}
			var token = Token( context );
			var user = token == null ? null : store.Authenticate( token );
			if ( token != null && user == null )
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				return;
			}
			// the slot counts a socket from before it authenticates, so unauthenticated sockets are bounded too
			if ( !coop.TryReserveConnection() )
			{
				context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
				return;
			}
			try
			{
				// its own keep-alive: a dead peer is noticed within 45 seconds
				using var socket = await context.WebSockets.AcceptWebSocketAsync( new WebSocketAcceptContext
				{
					KeepAliveInterval = TimeSpan.FromSeconds( 15 ),
					KeepAliveTimeout = TimeSpan.FromSeconds( 30 )
				} );
				if ( user == null )
					(user, token) = await AuthenticateFirstFrameAsync( socket, store, context.RequestAborted );
				if ( user == null || token == null )
				{
					await ChatHub.CloseAsync( socket, System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation, "unauthorized" );
					return;
				}
				await coop.RunAsync( socket, user, token, context.RequestAborted );
			}
			finally
			{
				coop.ReleaseConnection();
			}
		} );

		app.MapGet( ApiRoutes.CoopSessions, Handler( context => Guarded( context, store, true, _ =>
			Task.FromResult( Ok( new CoopSessionList( coop.Listed() ) ) ) ) ) );

		app.MapPut( ApiRoutes.Coop + "/{id}/snapshot", ( HttpContext context, string id ) => Guarded( context, store, true, async user =>
		{
			if ( !CoopProtocol.IsSessionId( id ) )
				throw new StoreException( StatusCodes.Status404NotFound, "No such shared park." );
			if ( context.Request.ContentLength > options.MaximumCoopSnapshotBytes )
				throw new StoreException( StatusCodes.Status413PayloadTooLarge, "The snapshot is too large." );
			// authenticated, the host's and with room: only now may the body be larger than the server's default limit
			coop.BeginUpload( user!, id );
			string? temporary = null;
			string? sha256 = null;
			try
			{
				var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
				if ( feature is { IsReadOnly: false } )
					feature.MaxRequestBodySize = options.MaximumCoopSnapshotBytes + 1;
				long bytes;
				try
				{
					(temporary, sha256, bytes) = await coop.Store.ReceiveAsync( id, context.Request.Body, options.MaximumCoopSnapshotBytes, context.RequestAborted );
				}
				catch ( BadHttpRequestException )
				{
					throw new StoreException( StatusCodes.Status413PayloadTooLarge, "The snapshot is too large." );
				}
				coop.FinishUpload( id, temporary, sha256 );
				temporary = null;
				return Ok( new CoopSnapshotStored( sha256!, bytes ) );
			}
			finally
			{
				if ( temporary != null || sha256 == null )
					coop.FinishUpload( id, null, null );
				if ( temporary != null )
					File.Delete( temporary );
			}
		} ) ).RequireRateLimiting( "upload" );

		app.MapGet( ApiRoutes.Coop + "/{id}/snapshot/{sha256}", ( HttpContext context, string id, string sha256 ) => Guarded( context, store, true, user =>
		{
			if ( !CoopProtocol.IsSessionId( id ) || !CoopProtocol.IsSha256( sha256 ) || coop.FindSnapshot( user!, id, sha256 ) is not { } path )
				return Task.FromResult( Error( StatusCodes.Status404NotFound, "No such snapshot." ) );
			context.Response.Headers["X-Content-SHA256"] = sha256;
			return Task.FromResult( Results.File( path, "application/octet-stream", enableRangeProcessing: true ) );
		} ) );
	}
}
