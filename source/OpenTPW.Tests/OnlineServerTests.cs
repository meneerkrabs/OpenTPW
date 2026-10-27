using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Api;
using OpenTPW.Online.Chat;
using OpenTPW.Online.Client;
using OpenTPW.Online.Moderation;
using OpenTPW.Online.Packages;
using OpenTPW.Server;

namespace OpenTPW.Tests;

/// <summary>The server hosted in-process on an ephemeral loopback port; no external network.</summary>
internal sealed class LoopbackServer : IAsyncDisposable
{
	private readonly WebApplication app;

	private LoopbackServer( WebApplication app, string directory, Uri url )
	{
		this.app = app;
		Directory = directory;
		Url = url;
	}

	public string Directory { get; }
	public Uri Url { get; }
	/// <summary>The server's services (its hubs), for tests that drive them directly.</summary>
	public IServiceProvider Services => app.Services;

	/// <summary>
	/// Building, starting or stopping the server takes well under a second; a stall (once a file watcher blocked in
	/// the OS) fails the test with a clear message instead of hanging the whole suite. Requests are bounded by
	/// <see cref="OnlineClient"/>'s own HttpClient timeout.
	/// </summary>
	public static readonly TimeSpan StartStopTimeout = TimeSpan.FromSeconds( 30 );

	public static async Task<LoopbackServer> StartAsync( Action<ServerOptions>? configure = null, string? directory = null, Uri? url = null, IEmailSender? emailSender = null )
	{
		directory ??= Path.Combine( Path.GetTempPath(), $"opentpw-server-{Guid.NewGuid():N}" );
		var dataDirectory = directory;
		var build = Task.Run( () => ServerProgram.Build( new[] { "--urls", url?.GetLeftPart( UriPartial.Authority ) ?? "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Warning" }, options =>
		{
			options.DataDirectory = dataDirectory;
			options.PasswordIterations = 1000;
			// Most tests are about other features; TwoStepLoginTests turns two-step login on.
			options.RequireTotp = false;
			configure?.Invoke( options );
		}, emailSender ) );
		WebApplication app;
		try
		{
			await Bounded( _ => build, "build" );
			app = await build;
		}
		catch
		{
			// A build that finishes after the timeout still owns a host; dispose it whenever it arrives.
			_ = build.ContinueWith( built => built.Result.DisposeAsync().AsTask(), TaskContinuationOptions.OnlyOnRanToCompletion );
			throw;
		}
		try
		{
			await Bounded( app.StartAsync, "start" );
		}
		catch
		{
			await Bounded( _ => app.DisposeAsync().AsTask(), "shut down" );
			throw;
		}
		var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		return new LoopbackServer( app, directory, new Uri( address ) );
	}

	/// <summary>Runs one lifecycle step on a pool thread, so even synchronous work in it cannot outlast the timeout.</summary>
	private static async Task Bounded( Func<CancellationToken, Task> step, string name )
	{
		using var cancel = new CancellationTokenSource( StartStopTimeout );
		try
		{
			await Task.Run( () => step( cancel.Token ) ).WaitAsync( StartStopTimeout );
		}
		catch ( Exception exception ) when ( exception is TimeoutException || (exception is OperationCanceledException && cancel.IsCancellationRequested) )
		{
			throw new AssertFailedException( $"The loopback server did not {name} within {StartStopTimeout.TotalSeconds} seconds.", exception );
		}
	}

	public async Task<OnlineClient> LoginAsync( string name, string password = "correct horse", bool register = true )
	{
		var client = new OnlineClient( Url );
		if ( register )
			await client.RegisterAsync( name, password );
		await client.LoginAsync( name, password );
		return client;
	}

	public async ValueTask DisposeAsync()
	{
		try
		{
			await Bounded( app.StopAsync, "stop" );
		}
		finally
		{
			await Bounded( _ => app.DisposeAsync().AsTask(), "shut down" );
		}
	}

	public void DeleteData()
	{
		if ( System.IO.Directory.Exists( Directory ) )
			System.IO.Directory.Delete( Directory, true );
	}
}

[TestClass]
public class OnlineServerTests
{
	private static async Task<OnlineException> Fails( Func<Task> action )
	{
		try
		{
			await action();
		}
		catch ( OnlineException exception )
		{
			return exception;
		}
		throw new AssertFailedException( "Expected the server to refuse the request." );
	}

	[TestMethod]
	public async Task AccountsUseHashedPasswordsAndTokens()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			using var client = new OnlineClient( server.Url );
			var info = await client.GetServerInfoAsync();
			Assert.AreEqual( 8, info.ProtocolVersion, "8: a shared park's session has a chat room of its own" );
			Assert.IsFalse( info.WordFilterLoaded );
			await client.RegisterAsync( "Alice", "correct horse" );
			Assert.AreEqual( HttpStatusCode.Conflict, (await Fails( () => client.RegisterAsync( "ALICE", "another one" ) )).Status );
			Assert.AreEqual( HttpStatusCode.BadRequest, (await Fails( () => client.RegisterAsync( "x", "correct horse" ) )).Status );
			Assert.AreEqual( HttpStatusCode.BadRequest, (await Fails( () => client.RegisterAsync( "Shorty", "short" ) )).Status );
			Assert.AreEqual( HttpStatusCode.Unauthorized, (await Fails( () => client.LoginAsync( "Alice", "wrong password" ) )).Status );
			Assert.AreEqual( HttpStatusCode.Unauthorized, (await Fails( () => client.LoginAsync( "Nobody", "correct horse" ) )).Status );
			Assert.AreEqual( HttpStatusCode.Unauthorized, (await Fails( () => client.ListParksAsync() )).Status );
			var session = (await client.LoginAsync( "alice", "correct horse" )).Session!;
			Assert.AreEqual( "Alice", session.Name );
			Assert.AreEqual( 0, (await client.ListParksAsync()).Total );
			var accounts = File.ReadAllText( Path.Combine( server.Directory, "accounts.json" ) );
			Assert.IsFalse( accounts.Contains( "correct horse" ) );
			Assert.IsFalse( accounts.Contains( session.Token ) );
			StringAssert.Contains( accounts, "\"hash\"" );
			await client.LogoutAsync();
			using var stale = new HttpClient();
			stale.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", session.Token );
			Assert.AreEqual( HttpStatusCode.Unauthorized, (await stale.GetAsync( new Uri( server.Url, "api/v1/parks" ) )).StatusCode );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ServerAcceptsBothParkPackageVersions()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			using var owner = await server.LoginAsync( "Owner" );
			// A version 2 package (the built layout) publishes, lists and downloads as itself…
			var v2 = await owner.UploadParkAsync( SharedParkTests.LayoutPackage() );
			var downloaded = await owner.DownloadParkAsync( v2.Id );
			Assert.AreEqual( 2, downloaded.Manifest.FormatVersion );
			Assert.IsTrue( MinimalParkPayload.FromSnapshot( downloaded.ToSnapshot() ).HasBuiltLayout );
			// …and a version 1 package from an OpenTPW 0.7 client is still accepted and served unchanged.
			var v1 = await owner.UploadParkAsync( SharedParkTests.V1Package() );
			Assert.AreEqual( 1, (await owner.DownloadParkAsync( v1.Id )).Manifest.FormatVersion );
			Assert.AreEqual( 2, (await owner.ListParksAsync()).Total );
			// A container-valid package whose payload is not a park payload is refused at publish: clients
			// check on visit, but no client could enter this park.
			var garbage = Encoding.UTF8.GetBytes( "{\"level\":\"jungle\",\"source\":\"other\"}" );
			var manifest = SharedParkTests.LayoutPackage().Manifest with
			{
				Payload = SharedParkTests.LayoutPackage().Manifest.Payload with { Length = garbage.Length, Sha256 = ParkPackage.Hash( garbage ) }
			};
			var hostile = ParkPackage.Read( OnlineFormatTests.Zip(
				(ParkPackage.ManifestEntry, StrictJson.Serialize( manifest )),
				(ParkPackage.PayloadEntry, garbage) ) );
			Assert.AreEqual( HttpStatusCode.BadRequest, (await Fails( () => owner.UploadParkAsync( hostile ) )).Status );
			Assert.AreEqual( 2, (await owner.ListParksAsync()).Total );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ParksUploadListDownloadVisitAndVote()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.MaximumParksPerPlayer = 2 );
		try
		{
			using var owner = await server.LoginAsync( "Owner" );
			using var visitor = await server.LoginAsync( "Visitor" );
			var package = OnlineFormatTests.Package();
			var published = await owner.UploadParkAsync( package );
			Assert.AreEqual( "My Jungle", published.Name );
			Assert.AreEqual( "Owner", published.Author );
			await owner.UploadParkAsync( OnlineFormatTests.Package( "Second" ) );
			Assert.AreEqual( HttpStatusCode.Conflict, (await Fails( () => owner.UploadParkAsync( OnlineFormatTests.Package( "Third" ) ) )).Status );

			var list = await visitor.ListParksAsync( search: "jungle" );
			Assert.AreEqual( 1, list.Total );
			Assert.AreEqual( published.Id, list.Parks[0].Id );
			Assert.AreEqual( 2, (await visitor.ListParksAsync( author: "owner" )).Total );
			var downloaded = await visitor.DownloadParkAsync( published.Id );
			CollectionAssert.AreEqual( package.ToBytes(), downloaded.ToBytes() );
			CollectionAssert.AreEqual( package.Thumbnail, await visitor.DownloadThumbnailAsync( published.Id ) );

			Assert.AreEqual( 1, (await visitor.RecordVisitAsync( published.Id )).Visits );
			// Every visit earned the owner Platinum Tickets in the original, but re-entering the same day and visiting
			// one's own park do not count again (EXT:online-visit-count-guard), so tickets cannot be farmed.
			Assert.AreEqual( 1, (await visitor.RecordVisitAsync( published.Id )).Visits );
			Assert.AreEqual( 1, (await owner.RecordVisitAsync( published.Id )).Visits );
			var vote = await visitor.VoteAsync( published.Id );
			Assert.AreEqual( (1, 9), (vote.Votes, vote.VotesLeftToday) );
			Assert.AreEqual( HttpStatusCode.Conflict, (await Fails( () => visitor.VoteAsync( published.Id ) )).Status );
			Assert.AreEqual( HttpStatusCode.Forbidden, (await Fails( () => owner.VoteAsync( published.Id ) )).Status );
			var summary = (await visitor.ListParksAsync( sort: "votes" )).Parks[0];
			Assert.IsTrue( summary.VisitedBefore && summary.VotedFor );
			Assert.AreEqual( published.Id, summary.Id );

			Assert.AreEqual( HttpStatusCode.Forbidden, (await Fails( () => visitor.UnpublishParkAsync( published.Id ) )).Status );
			await owner.UnpublishParkAsync( published.Id );
			Assert.AreEqual( HttpStatusCode.NotFound, (await Fails( () => visitor.DownloadParkAsync( published.Id ) )).Status );
			Assert.IsFalse( File.Exists( Path.Combine( server.Directory, "parks", published.Id + ".tpwpark" ) ) );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task UploadsAreValidatedAndSizeLimited()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			using var client = await server.LoginAsync( "Uploader" );
			using var http = new HttpClient();
			http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", client.Session!.Token );
			var parks = new Uri( server.Url, "api/v1/parks" );
			var garbage = await http.PostAsync( parks, new ByteArrayContent( Encoding.ASCII.GetBytes( "PK\u0003\u0004 not really" ) ) );
			Assert.AreEqual( HttpStatusCode.BadRequest, garbage.StatusCode );
			var huge = await http.PostAsync( parks, new ByteArrayContent( new byte[ParkPackage.MaximumPackageBytes + 10] ) );
			Assert.AreEqual( HttpStatusCode.RequestEntityTooLarge, huge.StatusCode );
			var card = await http.PostAsync( new Uri( server.Url, "api/v1/postcards" ), new ByteArrayContent( OnlineFormatTests.Package().ToBytes() ) );
			Assert.AreEqual( HttpStatusCode.BadRequest, card.StatusCode );
			var json = await http.PostAsync( new Uri( server.Url, "api/v1/reports" ), new StringContent( "{\"kind\":\"park\",\"target\":\"x\",\"reason\":\"y\",\"extra\":1}" ) );
			Assert.AreEqual( HttpStatusCode.BadRequest, json.StatusCode );
			var traversal = await http.GetAsync( new Uri( server.Url, "api/v1/parks/..%2F..%2Faccounts.json/package" ) );
			Assert.IsTrue( traversal.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest );
			await client.ReportAsync( ReportKinds.Park, "abc", "Rude park name" );
			StringAssert.Contains( File.ReadAllText( Path.Combine( server.Directory, "reports.jsonl" ) ), "Rude park name" );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task PlayersCanDeleteTheirAccountAndWhatTheServerKeepsAboutThem()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			using var ann = await server.LoginAsync( "Ann" );
			using var bob = await server.LoginAsync( "Bob" );
			var park = await ann.UploadParkAsync( OnlineFormatTests.Package() );
			await bob.SendPostcardAsync( Postcard.Create( "Bob", new[] { "Ann" }, "Hi", "Nice park", "English" ) );
			await ann.ReportAsync( ReportKinds.Park, "abc", "Rude park name" );
			Assert.IsTrue( File.Exists( Path.Combine( server.Directory, "parks", park.Id + ParkPackage.FileExtension ) ) );

			// The password is asked again, and for the logged-in account only.
			Assert.AreEqual( HttpStatusCode.Unauthorized, (await Fails( () => ann.DeleteAccountAsync( "Ann", "wrong password" ) )).Status );
			Assert.AreEqual( HttpStatusCode.BadRequest, (await Fails( () => ann.DeleteAccountAsync( "Bob", "correct horse" ) )).Status );
			Assert.AreEqual( 1, (await bob.ListParksAsync()).Total, "A refused deletion removes nothing." );

			await ann.DeleteAccountAsync( "Ann", "correct horse" );
			Assert.AreEqual( 0, (await bob.ListParksAsync()).Total );
			Assert.IsFalse( File.Exists( Path.Combine( server.Directory, "parks", park.Id + ParkPackage.FileExtension ) ) );
			Assert.IsFalse( File.ReadAllText( Path.Combine( server.Directory, "postcards.json" ) ).Contains( "\"ann\"" ), "Her inbox is gone." );
			var reports = File.ReadAllText( Path.Combine( server.Directory, "reports.jsonl" ) );
			Assert.IsFalse( reports.Contains( "Ann" ) );
			StringAssert.Contains( reports, "(deleted player)" );
			using ( var oldSession = new OnlineClient( server.Url ) )
				Assert.AreEqual( HttpStatusCode.Unauthorized, (await Fails( () => oldSession.LoginAsync( "Ann", "correct horse" ) )).Status );

			// The name is free again.
			using var newAnn = await server.LoginAsync( "Ann", "another password" );
			Assert.AreEqual( 0, (await newAnn.GetInboxAsync()).Postcards.Count );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task PostcardsAreDeliveredToInboxes()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			using var sender = await server.LoginAsync( "Sender" );
			using var reader = await server.LoginAsync( "Reader" );
			var card = Postcard.Create( "Sender", new[] { "Reader", "Ghost" }, "Greetings", "See my park", "English" );
			var sent = await sender.SendPostcardAsync( card );
			CollectionAssert.AreEqual( new[] { "Reader" }, sent.DeliveredTo.ToArray() );
			var inbox = await reader.GetInboxAsync();
			Assert.AreEqual( 1, inbox.Postcards.Count );
			Assert.AreEqual( ("Sender", "Greetings"), (inbox.Postcards[0].From, inbox.Postcards[0].Title) );
			var downloaded = await reader.DownloadPostcardAsync( inbox.Postcards[0].Id );
			Assert.AreEqual( card.Id, downloaded.Id );
			Assert.AreEqual( HttpStatusCode.NotFound, (await Fails( () => sender.DownloadPostcardAsync( inbox.Postcards[0].Id ) )).Status );
			var forged = Postcard.Create( "Reader", new[] { "Sender" }, "Forged", "x", "English" );
			Assert.AreEqual( HttpStatusCode.BadRequest, (await Fails( () => sender.SendPostcardAsync( forged ) )).Status );
			var nobody = Postcard.Create( "Sender", new[] { "Nobody" }, "Lost", "x", "English" );
			Assert.AreEqual( HttpStatusCode.NotFound, (await Fails( () => sender.SendPostcardAsync( nobody ) )).Status );
			await reader.DeletePostcardAsync( inbox.Postcards[0].Id );
			Assert.AreEqual( 0, (await reader.GetInboxAsync()).Postcards.Count );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task NewsIsPublicAndFollowsTheOperatorsFiles()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			using var client = new OnlineClient( server.Url );
			var empty = await client.GetNewsAsync();
			Assert.IsNotNull( empty, "served without a session" );
			Assert.AreEqual( ("", "", (DateTimeOffset?)null), (empty!.Game, empty.System, empty.UpdatedUtc) );

			var folder = Path.Combine( server.Directory, "news" );
			Directory.CreateDirectory( folder );
			File.WriteAllText( Path.Combine( folder, "game.txt" ), "Version 0.5 is out.\r\n" );
			File.WriteAllText( Path.Combine( folder, "system.txt" ), new string( 'x', NewsFeed.MaximumCharacters + 10 ) );
			var news = await client.GetNewsAsync();
			Assert.AreEqual( "Version 0.5 is out.", news!.Game );
			Assert.AreEqual( NewsFeed.MaximumCharacters, news.System.Length );
			Assert.IsNotNull( news.UpdatedUtc );

			File.WriteAllText( Path.Combine( folder, "game.txt" ), "Maintenance tonight." );
			File.SetLastWriteTimeUtc( Path.Combine( folder, "game.txt" ), DateTime.UtcNow.AddMinutes( 1 ) );
			Assert.AreEqual( "Maintenance tonight.", (await client.GetNewsAsync())!.Game );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ServersWithoutNewsGiveNoNews()
	{
		// No appsettings.json watcher: on macOS starting one calls sync(2), which a stalled mounted volume blocks (see ServerProgram.Build).
		var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder( new[] { "--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Warning", "--hostBuilder:reloadConfigOnChange=false" } );
		await using var app = builder.Build();
		await app.StartAsync();
		var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		using var client = new OnlineClient( new Uri( address ) );
		Assert.IsNull( await client.GetNewsAsync() );
		await app.StopAsync();
	}

	[TestMethod]
	public async Task OnlyParksWhoseAuthorsOptInAreListedForTheWebsite()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.WebsiteOrigins.Add( "https://opentpw.io" ) );
		try
		{
			using var ann = await server.LoginAsync( "Ann" );
			using var bob = await server.LoginAsync( "Bob" );
			var shown = await ann.UploadParkAsync( OnlineFormatTests.Package( "Shown" ), showOnWebsite: true );
			var hidden = await ann.UploadParkAsync( OnlineFormatTests.Package( "Private" ) );
			var popular = await bob.UploadParkAsync( OnlineFormatTests.Package( "Popular" ), showOnWebsite: true );
			await ann.VoteAsync( popular.Id );

			using var http = new HttpClient { BaseAddress = server.Url };
			var list = StrictJson.Deserialize<WebsiteParkList>( await http.GetByteArrayAsync( ApiRoutes.WebsiteParks.TrimStart( '/' ) ), "test" );
			CollectionAssert.AreEqual( new[] { "Popular", "Shown" }, list.Parks.Select( park => park.Name ).ToArray(), "opted-in parks only, most votes first, without a session" );
			Assert.AreEqual( "Bob", list.Parks[0].Author );

			Assert.AreEqual( HttpStatusCode.NotFound, (await http.GetAsync( $"{ApiRoutes.WebsiteParks.TrimStart( '/' )}/{hidden.Id}/thumbnail" )).StatusCode, "no public picture of a park not on the website" );
			var picture = await http.GetAsync( $"{ApiRoutes.WebsiteParks.TrimStart( '/' )}/{shown.Id}/thumbnail" );
			Assert.AreEqual( HttpStatusCode.OK, picture.StatusCode );
			Assert.AreEqual( "image/png", picture.Content.Headers.ContentType?.MediaType );
			Assert.IsTrue( picture.Headers.CacheControl?.Public == true, "website pictures may be cached" );

			using var allowed = new HttpRequestMessage( HttpMethod.Get, ApiRoutes.WebsiteParks.TrimStart( '/' ) );
			allowed.Headers.Add( "Origin", "https://opentpw.io" );
			Assert.AreEqual( "https://opentpw.io", (await http.SendAsync( allowed )).Headers.GetValues( "Access-Control-Allow-Origin" ).Single() );
			using var other = new HttpRequestMessage( HttpMethod.Get, ApiRoutes.News.TrimStart( '/' ) );
			other.Headers.Add( "Origin", "https://example.com" );
			Assert.IsFalse( (await http.SendAsync( other )).Headers.Contains( "Access-Control-Allow-Origin" ) );
			using var parks = new HttpRequestMessage( HttpMethod.Get, ApiRoutes.Parks.TrimStart( '/' ) );
			parks.Headers.Add( "Origin", "https://opentpw.io" );
			var guarded = await http.SendAsync( parks );
			Assert.AreEqual( HttpStatusCode.Unauthorized, guarded.StatusCode, "the full park list still needs a session" );
			Assert.IsFalse( guarded.Headers.Contains( "Access-Control-Allow-Origin" ) );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task DeployRequestNeedsTheTokenAndOnlyLeavesAFile()
	{
		await using ( var closed = await LoopbackServer.StartAsync() )
		{
			try
			{
				using var http = new HttpClient { BaseAddress = closed.Url };
				Assert.AreEqual( HttpStatusCode.NotFound, (await http.PostAsync( ApiRoutes.Deploy.TrimStart( '/' ), null )).StatusCode, "no route without a token" );
			}
			finally
			{
				closed.DeleteData();
			}
		}
		await using var server = await LoopbackServer.StartAsync( options => options.DeployToken = "release-secret" );
		try
		{
			var request = Path.Combine( server.Directory, ServerProgram.DeployRequestFile );
			using var http = new HttpClient { BaseAddress = server.Url };
			async Task<HttpStatusCode> Post( string? token )
			{
				using var message = new HttpRequestMessage( HttpMethod.Post, ApiRoutes.Deploy.TrimStart( '/' ) );
				if ( token != null )
					message.Headers.Authorization = new AuthenticationHeaderValue( "Bearer", token );
				return (await http.SendAsync( message )).StatusCode;
			}
			Assert.AreEqual( HttpStatusCode.Unauthorized, await Post( null ) );
			Assert.AreEqual( HttpStatusCode.Unauthorized, await Post( "release-secreT" ) );
			Assert.IsFalse( File.Exists( request ) );
			Assert.AreEqual( HttpStatusCode.Accepted, await Post( "release-secret" ) );
			Assert.IsTrue( File.Exists( request ) );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public void AVisitorCountsOncePerDayAndOwnersNever()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-visits-{Guid.NewGuid():N}" );
		try
		{
			var now = DateTimeOffset.UtcNow;
			var store = new ServerStore( new ServerOptions { DataDirectory = directory, PasswordIterations = 1000 }, () => now );
			var owner = store.Register( "Owner", "correct horse" );
			var visitor = store.Register( "Visitor", "correct horse" );
			var package = OnlineFormatTests.Package( "Counted" );
			var park = store.Publish( owner, package, package.ToBytes() );
			Assert.AreEqual( 0, store.Visit( owner, park.Id ).Visits, "an owner's own visit does not count" );
			Assert.AreEqual( 1, store.Visit( visitor, park.Id ).Visits );
			Assert.AreEqual( 1, store.Visit( visitor, park.Id ).Visits, "the same visitor counts once per day" );
			now = now.AddDays( 1 );
			Assert.AreEqual( 2, store.Visit( visitor, park.Id ).Visits, "the next day counts again" );
		}
		finally
		{
			if ( Directory.Exists( directory ) )
				Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public void SessionsSurviveARestartAsHashesOnly()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-sessions-{Guid.NewGuid():N}" );
		try
		{
			var now = DateTimeOffset.UtcNow;
			var options = new ServerOptions { DataDirectory = directory, PasswordIterations = 1000 };
			var first = new ServerStore( options, () => now );
			first.Register( "Ann", "correct horse" );
			var kept = first.Login( "Ann", "correct horse" ).Session!;
			var loggedOut = first.Login( "Ann", "correct horse" ).Session!;
			first.Logout( loggedOut.Token );
			Assert.IsFalse( File.ReadAllText( Path.Combine( directory, "sessions.json" ) ).Contains( kept.Token ), "only the token hash is stored" );

			var restarted = new ServerStore( options, () => now.AddMinutes( 1 ) );
			Assert.AreEqual( "Ann", restarted.Authenticate( kept.Token )?.Name, "a restart keeps the session" );
			Assert.IsNull( restarted.Authenticate( loggedOut.Token ) );
			Assert.IsNull( new ServerStore( options, () => now + options.SessionLifetime + TimeSpan.FromMinutes( 1 ) ).Authenticate( kept.Token ), "expired sessions are not loaded" );
		}
		finally
		{
			if ( Directory.Exists( directory ) )
				Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public async Task StoppingTheServerTellsChatClientsItRestarts()
	{
		var server = await LoopbackServer.StartAsync();
		try
		{
			using var ann = await server.LoginAsync( "Ann" );
			await using var chat = await ann.ConnectChatAsync();
			await NextAsync( chat, IsNotice( ChatNotice.WelcomeThemeParkWorld ) );
			var stopping = server.DisposeAsync().AsTask();
			using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 15 ) );
			while ( await chat.ReceiveAsync( timeout.Token ) != null )
			{
			}
			Assert.AreEqual( ChatProtocol.RestartingCloseStatus, chat.CloseStatus );
			await stopping;
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task CitiesArePublicCountParksAndCanBeReplacedByTheOperator()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			using var anonymous = new OnlineClient( server.Url );
			var defaults = (await anonymous.GetCitiesAsync())!.Cities;
			Assert.AreEqual( "North Pole", defaults[0].Name, "the Bullfrog city comes first" );
			CollectionAssert.IsSubsetOf( new[] { "Hong Kong", "Ulaanbaatar", "Timbuktu", "San Francisco" }, defaults.Select( city => city.Name ).ToArray() );
			Assert.IsTrue( defaults.All( city => city.Name.Length <= CityDirectory.MaximumNameLength && city.Parks == 0 ) );

			using var ann = await server.LoginAsync( "Ann" );
			var timbuktu = defaults.Single( city => city.Name == "Timbuktu" ).Id;
			await ann.UploadParkAsync( OnlineFormatTests.Package( "Sand Castle" ), cityId: timbuktu );
			Assert.AreEqual( 1, (await anonymous.GetCitiesAsync())!.Cities.Single( city => city.Id == timbuktu ).Parks );
			await ann.UploadParkAsync( OnlineFormatTests.Package( "Elsewhere" ) );
			var inCity = await ann.ListParksAsync( sort: "votes", cityId: timbuktu );
			CollectionAssert.AreEqual( new[] { "Sand Castle" }, inCity.Parks.Select( park => park.Name ).ToArray(), "the city lobby lists only that city's parks" );
			Assert.AreEqual( timbuktu, inCity.Parks[0].CityId );
			Assert.AreEqual( HttpStatusCode.BadRequest, (await Fails( () => ann.UploadParkAsync( OnlineFormatTests.Package( "Nowhere" ), cityId: 999 ) )).Status );

			File.WriteAllText( Path.Combine( server.Directory, "cities.json" ),
				"[{\"id\":7,\"name\":\"A very long city name indeed\",\"description\":\"x\",\"latitude\":10,\"longitude\":20},{\"id\":8,\"name\":\"Bad\",\"latitude\":95,\"longitude\":0}]" );
			var replaced = (await anonymous.GetCitiesAsync())!.Cities;
			Assert.AreEqual( 1, replaced.Count, "invalid cities are left out" );
			Assert.AreEqual( CityDirectory.MaximumNameLength, replaced[0].Name.Length );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AuthenticationIsRateLimited()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.AuthenticationsPerMinute = 3 );
		try
		{
			using var client = new OnlineClient( server.Url );
			for ( var attempt = 0; attempt < 3; attempt++ )
				await Fails( () => client.LoginAsync( "Nobody", "wrong password" ) );
			var refused = await Fails( () => client.LoginAsync( "Nobody", "wrong password" ) );
			Assert.AreEqual( HttpStatusCode.TooManyRequests, refused.Status );
			Assert.IsTrue( refused.RetryAfter is { } wait && wait > TimeSpan.Zero && wait <= TimeSpan.FromMinutes( 1 ), "the client reads when to try again" );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task BannedPlayersCannotLogIn()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.BannedPlayers.Add( "troll" ) );
		try
		{
			using var client = new OnlineClient( server.Url );
			await client.RegisterAsync( "Troll", "correct horse" );
			Assert.AreEqual( HttpStatusCode.Forbidden, (await Fails( () => client.LoginAsync( "Troll", "correct horse" ) )).Status );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task StatePersistsAcrossRestarts()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"opentpw-server-{Guid.NewGuid():N}" );
		try
		{
			string id;
			await using ( var first = await LoopbackServer.StartAsync( directory: directory ) )
			{
				using var owner = await first.LoginAsync( "Owner" );
				id = (await owner.UploadParkAsync( OnlineFormatTests.Package() )).Id;
			}
			await using var second = await LoopbackServer.StartAsync( directory: directory );
			using var again = await second.LoginAsync( "Owner", register: false );
			Assert.AreEqual( id, (await again.ListParksAsync()).Parks.Single().Id );
		}
		finally
		{
			if ( Directory.Exists( directory ) )
				Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public async Task PlayersJoiningARoomTogetherAllEndWithTheFullRoster()
	{
		// Each connection's requests are handled on their own thread, so joins of one room run side by side. A roster
		// written after a newer one would leave a player believing others are absent (the flake of
		// OnlineIntegrationTests.SessionWalksAChatParkAndReportsBuddyEntries): while players only arrive, each player's
		// rosters of the room must grow, and the last one must name everyone.
		await using var server = await LoopbackServer.StartAsync( options =>
		{
			options.AuthenticationsPerMinute = 100;
			options.ChatBurst = 1000;
			options.ChatMessagesPerSecond = 1000;
		} );
		var clients = new List<OnlineClient>();
		var chats = new List<ChatConnection>();
		try
		{
			const int players = 12;
			for ( var index = 0; index < players; index++ )
			{
				var client = await server.LoginAsync( $"Player{index}" );
				clients.Add( client );
				var chat = await client.ConnectChatAsync();
				chats.Add( chat );
				await NextAsync( chat, item => item.Kind == ChatEventKind.Room );
			}
			var names = Enumerable.Range( 0, players ).Select( index => $"Player{index}" ).ToArray();
			// the lobby between the parks, so each round starts from an empty room
			var rooms = Enumerable.Range( 1, ChatProtocol.SatelliteRoomCount ).SelectMany( index => new[] { ChatProtocol.SatelliteRoom( index ), ChatProtocol.LobbyRoom } ).ToArray();
			for ( var round = 0; round < 4 * rooms.Length; round++ )
			{
				var room = rooms[round % rooms.Length];
				var rosters = chats.Select( _ => new List<IReadOnlyList<string>>() ).ToArray();
				// A request handled after a join on the same connection is answered after everything that join wrote: the
				// first marker, once every player has it, means every join of the round is done; the second, sent after
				// that, comes after every roster the round wrote to that player.
				async Task ReadUntilMarker( int index, string range )
				{
					while ( true )
					{
						var item = await NextAsync( chats[index] );
						if ( item.Kind == ChatEventKind.Room && item.Room == room )
							rosters[index].Add( item.Args! );
						else if ( item.Kind == ChatEventKind.Notice && item.Notice == (int)ChatNotice.HearingRangeSet && item.Args?[0] == range )
							return;
					}
				}
				await Task.WhenAll( chats.Select( async ( chat, index ) =>
				{
					await chat.JoinAsync( room );
					await chat.SendAsync( ChatCommand.Hearing, "1" );
					await ReadUntilMarker( index, "1" );
				} ) );
				await Task.WhenAll( chats.Select( async ( chat, index ) =>
				{
					await chat.SendAsync( ChatCommand.Hearing, "2" );
					await ReadUntilMarker( index, "2" );
				} ) );
				for ( var index = 0; index < players; index++ )
				{
					var seen = rosters[index];
					var what = $"round {round}, {names[index]} in {room}: " + string.Join( " | ", seen.Select( roster => string.Join( ",", roster ) ) );
					Assert.IsTrue( seen.Count > 0, what );
					for ( var next = 1; next < seen.Count; next++ )
						Assert.IsTrue( seen[next - 1].All( seen[next].Contains ), "a roster lost a player while players only arrived: " + what );
					CollectionAssert.AreEquivalent( names, seen[^1].ToArray(), "the last roster does not name everyone: " + what );
				}
			}
		}
		finally
		{
			foreach ( var chat in chats )
				await chat.DisposeAsync();
			foreach ( var client in clients )
				client.Dispose();
			server.DeleteData();
		}
	}

	private static async Task<ChatEvent> NextAsync( ChatConnection chat, Func<ChatEvent, bool>? match = null )
	{
		using var timeout = new CancellationTokenSource( OnlineIntegrationTests.Deadline );
		while ( true )
		{
			var item = await chat.ReceiveAsync( timeout.Token ) ?? throw new AssertFailedException( "Chat closed." );
			if ( match == null || match( item ) )
				return item;
		}
	}

	private static Func<ChatEvent, bool> IsNotice( ChatNotice notice ) => item => item.Kind == ChatEventKind.Notice && item.Notice == (int)notice;

	/// <summary>Opens the chat socket the way a browser must: no Authorization header, the token as the first frame.</summary>
	private static async Task<System.Net.WebSockets.ClientWebSocket> ConnectWithoutHeaderAsync( LoopbackServer server, byte[] firstFrame )
	{
		var socket = new System.Net.WebSockets.ClientWebSocket();
		var url = new UriBuilder( new Uri( server.Url, ApiRoutes.Chat.TrimStart( '/' ) ) ) { Scheme = "ws" }.Uri;
		await socket.ConnectAsync( url, CancellationToken.None );
		await socket.SendAsync( firstFrame, System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None );
		return socket;
	}

	private static async Task<(System.Net.WebSockets.WebSocketMessageType Type, string Text)> ReceiveAsync( System.Net.WebSockets.ClientWebSocket socket )
	{
		using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 15 ) );
		var buffer = new byte[ChatProtocol.MaximumFrameBytes];
		var result = await socket.ReceiveAsync( buffer, timeout.Token );
		return (result.MessageType, Encoding.UTF8.GetString( buffer, 0, result.Count ));
	}

	[TestMethod]
	public async Task ChatAcceptsTheTokenAsTheFirstFrameForBrowsers()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			using var client = await server.LoginAsync( "Ann" );
			using ( var socket = await ConnectWithoutHeaderAsync( server, StrictJson.Serialize( ChatAuthentication.For( client.Session!.Token ) ) ) )
			{
				var (type, text) = await ReceiveAsync( socket );
				Assert.AreEqual( System.Net.WebSockets.WebSocketMessageType.Text, type );
				Assert.AreEqual( (int)ChatNotice.WelcomeThemeParkWorld, StrictJson.Deserialize<ChatEvent>( Encoding.UTF8.GetBytes( text ), "event" ).Notice );
			}

			// A wrong token or any other first frame closes the socket without a word.
			foreach ( var frame in new[] {
				StrictJson.Serialize( ChatAuthentication.For( "not-a-session" ) ),
				StrictJson.Serialize( ChatRequest.ForJoin( ChatProtocol.LobbyRoom ) ),
				Encoding.UTF8.GetBytes( new string( 'x', ChatAuthentication.MaximumBytes + 10 ) ) } )
			{
				using var socket = await ConnectWithoutHeaderAsync( server, frame );
				var (type, _) = await ReceiveAsync( socket );
				Assert.AreEqual( System.Net.WebSockets.WebSocketMessageType.Close, type );
				Assert.AreEqual( System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation, socket.CloseStatus );
			}
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ServesTheBrowserGameBesideTheApi()
	{
		var web = Path.Combine( Path.GetTempPath(), $"opentpw-web-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( web, "_framework" ) );
		File.WriteAllText( Path.Combine( web, "index.html" ), "<!doctype html><title>OpenTPW</title>" );
		File.WriteAllBytes( Path.Combine( web, "index.html.br" ), [0x0b, 0x01, 0x80] );
		File.WriteAllText( Path.Combine( web, "_framework", "dotnet.js" ), "// boot" );
		File.WriteAllBytes( Path.Combine( web, "_framework", "dotnet.native.abcde12345.wasm" ), [0, 0x61, 0x73, 0x6d] );
		await using var server = await LoopbackServer.StartAsync( options =>
		{
			options.WebClientDirectory = web;
			options.RequestsPerMinute = 3;
		} );
		try
		{
			using var http = new HttpClient( new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None } ) { BaseAddress = server.Url };
			using var page = await http.GetAsync( "/" );
			Assert.AreEqual( HttpStatusCode.OK, page.StatusCode );
			Assert.AreEqual( "text/html", page.Content.Headers.ContentType!.MediaType );
			StringAssert.Contains( page.Headers.CacheControl!.ToString(), "no-cache" );
			StringAssert.Contains( string.Join( ";", page.Headers.GetValues( "Content-Security-Policy" ) ), "'wasm-unsafe-eval'" );
			// the game data source: the player's chosen https address may be fetched; scripts stay the page's own
			var policy = string.Join( ";", page.Headers.GetValues( "Content-Security-Policy" ) );
			StringAssert.Contains( policy, "connect-src 'self'" );
			StringAssert.Contains( policy, " https:;" );
			StringAssert.Contains( policy, "script-src 'self' 'wasm-unsafe-eval';" );
			Assert.IsFalse( policy.Contains( "connect-src *" ) || policy.Contains( "script-src 'self' https:" ), policy );

			using var request = new HttpRequestMessage( HttpMethod.Get, "/index.html" );
			request.Headers.AcceptEncoding.ParseAdd( "gzip, br" );
			using var compressed = await http.SendAsync( request );
			Assert.AreEqual( "br", compressed.Content.Headers.ContentEncoding.Single() );
			Assert.AreEqual( "text/html", compressed.Content.Headers.ContentType!.MediaType );
			CollectionAssert.AreEqual( new byte[] { 0x0b, 0x01, 0x80 }, await compressed.Content.ReadAsByteArrayAsync() );

			using var runtime = await http.GetAsync( "/_framework/dotnet.native.abcde12345.wasm" );
			Assert.AreEqual( "application/wasm", runtime.Content.Headers.ContentType!.MediaType );
			StringAssert.Contains( runtime.Headers.CacheControl!.ToString(), "immutable" );
			using var boot = await http.GetAsync( "/_framework/dotnet.js" );
			StringAssert.Contains( boot.Headers.CacheControl!.ToString(), "no-cache" );

			// A page load fetches many files; the API's per-address limit does not count them.
			for ( var index = 0; index < 10; index++ )
				using ( var again = await http.GetAsync( "/_framework/dotnet.js" ) )
					Assert.AreEqual( HttpStatusCode.OK, again.StatusCode );
			using var info = await http.GetAsync( ApiRoutes.Server );
			Assert.AreEqual( HttpStatusCode.OK, info.StatusCode );
		}
		finally
		{
			server.DeleteData();
			Directory.Delete( web, true );
		}
	}

	[TestMethod]
	public async Task ChatRunsTheOriginalCommands()
	{
		var filterDirectory = Path.Combine( Path.GetTempPath(), $"opentpw-filter-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( filterDirectory );
		File.WriteAllBytes( Path.Combine( filterDirectory, WordFilter.SwearsFileName ), WordFilter.EncodeList( new[] { "darn" } ) );
		File.WriteAllBytes( Path.Combine( filterDirectory, WordFilter.AllowedsFileName ), WordFilter.EncodeList( new[] { "darning" } ) );
		await using var server = await LoopbackServer.StartAsync( options =>
		{
			options.FilterDirectory = filterDirectory;
			options.Message = "Hello operators";
			options.MutedPlayers.Add( "Mute" );
			options.ChatBurst = 50;
		} );
		try
		{
			using var annClient = await server.LoginAsync( "Ann" );
			using var bobClient = await server.LoginAsync( "Bob" );
			using var muteClient = await server.LoginAsync( "Mute" );
			await using var ann = await annClient.ConnectChatAsync();
			Assert.AreEqual( (int)ChatNotice.WelcomeThemeParkWorld, (await NextAsync( ann )).Notice );
			Assert.AreEqual( "Hello operators", (await NextAsync( ann )).Args![0] );
			Assert.AreEqual( ChatProtocol.LobbyRoom, (await NextAsync( ann, item => item.Kind == ChatEventKind.Room )).Room );
			await using var bob = await bobClient.ConnectChatAsync();
			await NextAsync( bob, item => item.Kind == ChatEventKind.Room );

			// A second connection for the same player is refused with the original response.
			await using ( var duplicate = await bobClient.ConnectChatAsync() )
				Assert.AreEqual( (int)ChatNotice.NameAlreadyOnline, (await NextAsync( duplicate )).Notice );

			await ann.SendAsync( ChatCommand.Say, "what a darn fine darning park" );
			var heard = await NextAsync( bob, item => item.Kind == ChatEventKind.Say );
			Assert.AreEqual( ("Ann", "what a **** fine darning park"), (heard.From, heard.Text) );
			await bob.SendAsync( ChatCommand.Filter, "" );
			await NextAsync( bob, IsNotice( ChatNotice.FilterOff ) );
			await ann.SendAsync( ChatCommand.Say, "darn" );
			Assert.AreEqual( "darn", (await NextAsync( bob, item => item.Kind == ChatEventKind.Say )).Text );

			await ann.SendAsync( ChatCommand.Tell, "bob secret" );
			var told = await NextAsync( bob, item => item.Kind == ChatEventKind.Tell );
			Assert.AreEqual( ("Ann", "secret"), (told.From, told.Text) );
			var echo = await NextAsync( ann, IsNotice( ChatNotice.YouTold ) );
			Assert.AreEqual( "Bob", echo.Args![0] );
			await bob.SendAsync( ChatCommand.Reply, "got it" );
			Assert.AreEqual( "got it", (await NextAsync( ann, item => item.Kind == ChatEventKind.Tell )).Text );
			await ann.SendAsync( ChatCommand.Tell, "Nobody hi" );
			Assert.AreEqual( "Nobody", (await NextAsync( ann, IsNotice( ChatNotice.NoSuchPlayer ) )).Args![0] );

			await bob.SendAsync( ChatCommand.Cheer, "" );
			await NextAsync( bob, IsNotice( ChatNotice.YouCheer ) );
			Assert.AreEqual( "cheer", (await NextAsync( ann, item => item.Kind == ChatEventKind.Emote )).Command );

			await ann.SendAsync( ChatCommand.Earmuffs, "" );
			await NextAsync( ann, IsNotice( ChatNotice.EarmuffsOn ) );
			await ann.SendAsync( ChatCommand.Shout, "hey" );
			await NextAsync( ann, IsNotice( ChatNotice.CannotShoutWithEarmuffs ) );

			await ann.SendAsync( ChatCommand.Ignore, "Bob" );
			await NextAsync( ann, IsNotice( ChatNotice.YouIgnore ) );
			await bob.SendAsync( ChatCommand.Say, "ignored line" );
			await NextAsync( bob, item => item.Kind == ChatEventKind.Say );
			await ann.SendAsync( ChatCommand.Who, "" );
			var who = await NextAsync( ann, item => item.Kind is ChatEventKind.Say or ChatEventKind.Room );
			Assert.AreEqual( ChatEventKind.Room, who.Kind, "Ignored speech must not be delivered." );
			CollectionAssert.AreEqual( new[] { "Ann", "Bob" }, who.Args!.ToArray() );

			await bob.SendAsync( ChatCommand.Friend, "Ann" );
			await NextAsync( bob, IsNotice( ChatNotice.YouMakeBuddy ) );
			await bob.SendAsync( ChatCommand.Buddy, "" );
			Assert.AreEqual( "Ann", (await NextAsync( bob, IsNotice( ChatNotice.YourBuddyOnline ) )).Args![0] );
			await bob.SendAsync( ChatCommand.Mark, "Ann spamming" );
			await NextAsync( bob, IsNotice( ChatNotice.YouBlackmark ) );
			StringAssert.Contains( File.ReadAllText( Path.Combine( server.Directory, "reports.jsonl" ) ), "spamming" );
			await bob.SendAsync( ChatCommand.Vote, "" );
			await NextAsync( bob, IsNotice( ChatNotice.NoPark ) );
			await bob.SendAsync( ChatCommand.Goto, "Bob" );
			await NextAsync( bob, IsNotice( ChatNotice.CannotGotoSelf ) );

			await using var mute = await muteClient.ConnectChatAsync();
			await mute.SendAsync( ChatCommand.Say, "let me talk" );
			await NextAsync( mute, IsNotice( ChatNotice.Muted ) );

			await bob.SendAsync( new ChatRequest( "command", "rm -rf", "", null ) );
			await NextAsync( bob, IsNotice( ChatNotice.NoSuchCommand ) );
		}
		finally
		{
			server.DeleteData();
			Directory.Delete( filterDirectory, true );
		}
	}

	[TestMethod]
	public async Task ChatJoinsParkRoomsAndVotes()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		try
		{
			using var ownerClient = await server.LoginAsync( "Owner" );
			using var guestClient = await server.LoginAsync( "Guest" );
			var park = await ownerClient.UploadParkAsync( OnlineFormatTests.Package() );
			await using var guest = await guestClient.ConnectChatAsync();
			await guest.JoinAsync( ChatProtocol.ParkRoom( park.Id ) );
			Assert.AreEqual( ChatProtocol.ParkRoom( park.Id ), (await NextAsync( guest, item => item.Kind == ChatEventKind.Room && item.Room != ChatProtocol.LobbyRoom )).Room );
			Assert.AreEqual( 1, (await guestClient.ListParksAsync()).Parks.Single().PlayersAtPark );
			await guest.SendAsync( ChatCommand.Vote, "" );
			await NextAsync( guest, IsNotice( ChatNotice.VotedForPark ) );
			await guest.JoinAsync( ChatProtocol.ParkRoom( "doesnotexist" ) );
			await NextAsync( guest, IsNotice( ChatNotice.NoPark ) );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ChatIsRateLimitedPerConnection()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 2 );
		try
		{
			using var client = await server.LoginAsync( "Spammer" );
			await using var chat = await client.ConnectChatAsync();
			for ( var index = 0; index < 4; index++ )
				await chat.SendAsync( ChatCommand.Say, $"line {index}" );
			var limited = await NextAsync( chat, item => item.Kind == ChatEventKind.System && item.Text == ChatSystemKeys.RateLimited );
			Assert.IsNotNull( limited );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task TenParksAreTheDefaultPublishLimit()
	{
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			using var owner = await server.LoginAsync( "Owner" );
			// The original allowed ten parks on show at once (online FAQ, docs/reverse/ONLINE-SOURCES.md S2).
			Assert.AreEqual( 10, (await owner.GetServerInfoAsync()).MaximumParksPerPlayer );
			for ( var index = 1; index <= 10; index++ )
				await owner.UploadParkAsync( OnlineFormatTests.Package( $"Park {index}" ) );
			var refused = await Fails( () => owner.UploadParkAsync( OnlineFormatTests.Package( "Eleventh" ) ) );
			Assert.AreEqual( HttpStatusCode.Conflict, refused.Status );
			// The original's message (UITEXT 221): the personal limit is reached, un-publish first.
			StringAssert.Contains( refused.Message, "personal limit" );
			var first = (await owner.ListParksAsync( sort: "name" )).Parks.Single( park => park.Name == "Park 1" );
			await owner.UnpublishParkAsync( first.Id );
			await owner.UploadParkAsync( OnlineFormatTests.Package( "Eleventh" ) );
			Assert.AreEqual( 10, (await owner.ListParksAsync()).Total );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ParkNamesAreMaskedWhereDescriptionsAreRefused()
	{
		var filterDirectory = Path.Combine( Path.GetTempPath(), $"opentpw-filter-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( filterDirectory );
		File.WriteAllBytes( Path.Combine( filterDirectory, WordFilter.SwearsFileName ), WordFilter.EncodeList( new[] { "darn" } ) );
		File.WriteAllBytes( Path.Combine( filterDirectory, WordFilter.AllowedsFileName ), WordFilter.EncodeList( new[] { "darning" } ) );
		await using var server = await LoopbackServer.StartAsync( options => options.FilterDirectory = filterDirectory );
		try
		{
			using var owner = await server.LoginAsync( "Owner" );
			// The original showed swear words in park names as asterisks (FAQ, S2): the name is masked, not refused.
			var masked = await owner.UploadParkAsync( OnlineFormatTests.Package( "Darn Park" ) );
			Assert.AreEqual( "**** Park", masked.Name );
			Assert.AreEqual( "**** Park", (await owner.ListParksAsync()).Parks.Single().Name );
			// An allowed word inside the name survives.
			Assert.AreEqual( "Darning Park", (await owner.UploadParkAsync( OnlineFormatTests.Package( "Darning Park" ) )).Name );
			// Descriptions keep the refusal: an OpenTPW moderation choice, not the original's.
			var swearing = OnlineFormatTests.Package( "Fine Park" );
			Assert.AreEqual( HttpStatusCode.BadRequest, (await Fails( () => owner.UploadParkAsync( swearing with { Manifest = swearing.Manifest with { Park = swearing.Manifest.Park with { Description = "what a darn park" } } } ) )).Status );
		}
		finally
		{
			server.DeleteData();
			Directory.Delete( filterDirectory, true );
		}
	}

	[TestMethod]
	public async Task WebsiteChartsAndPlayerPagesMirrorTheOriginalLists()
	{
		// The search part registers fifty-one players from one address, above the usual authentication limit.
		await using var server = await LoopbackServer.StartAsync( options =>
		{
			options.AuthenticationsPerMinute = 200;
			options.RequestsPerMinute = 600;
		} );
		try
		{
			using var annClient = await server.LoginAsync( "Ann" );
			using var bobClient = await server.LoginAsync( "Bob" );
			using var carolClient = await server.LoginAsync( "Carol" );
			var shown = await annClient.UploadParkAsync( OnlineFormatTests.Package( "Shown" ), showOnWebsite: true );
			await annClient.UploadParkAsync( OnlineFormatTests.Package( "Private" ) );
			var popular = await bobClient.UploadParkAsync( OnlineFormatTests.Package( "Popular" ), showOnWebsite: true );
			// A visit earns the park's owner Platinum Tickets whether or not the visitor votes (S2).
			await carolClient.RecordVisitAsync( shown.Id );
			await carolClient.VoteAsync( popular.Id );

			using var publicClient = new OnlineClient( server.Url );
			// Top 10 parks (S1: rank, park name, player) of the parks their authors show on the website.
			var parks = await publicClient.GetChartParksAsync();
			Assert.AreEqual( 2, parks.Parks.Count );
			Assert.AreEqual( (1, "Popular", "Bob"), (parks.Parks[0].Rank, parks.Parks[0].Name, parks.Parks[0].Player) );
			Assert.AreEqual( (2, "Shown", "Ann"), (parks.Parks[1].Rank, parks.Parks[1].Name, parks.Parks[1].Player) );
			// Top 10 players and the richest players (S1); only players with website parks appear.
			var players = await publicClient.GetChartPlayersAsync( richest: false );
			Assert.AreEqual( ("Bob", "Ann"), (players.Players[0].Player, players.Players[1].Player) );
			var richest = await publicClient.GetChartPlayersAsync( richest: true );
			Assert.AreEqual( ("Ann", 1), (richest.Players[0].Player, richest.Players[0].Score) );
			Assert.AreEqual( "Bob", richest.Players[1].Player );
			// The player page (S1: park name, visitors, votes, publish date, rank, highest rank) is public for the
			// website parks only; the Platinum Tickets of a public page count only those parks.
			var publicPage = await publicClient.GetPlayerProfileAsync( "Ann" );
			Assert.AreEqual( ("Ann", 1), (publicPage.Name, publicPage.PlatinumTickets) );
			Assert.AreEqual( 1, publicPage.Parks.Count );
			Assert.AreEqual( ("Shown", 1, 0, 2, 2),
				(publicPage.Parks[0].Name, publicPage.Parks[0].Visits, publicPage.Parks[0].Votes, publicPage.Parks[0].Rank, publicPage.Parks[0].HighestRank) );
			// Logged in, the page lists every published park of the player, with all their Platinum Tickets.
			var ownPage = await annClient.GetPlayerProfileAsync( "Ann" );
			Assert.AreEqual( 2, ownPage.Parks.Count );
			Assert.AreEqual( 1, ownPage.PlatinumTickets );
			// A vote re-orders the chart and the ranks follow; each park remembers its best place (Popular held
			// rank 1 until this vote passed it).
			await carolClient.VoteAsync( shown.Id );
			var afterVote = await publicClient.GetPlayerProfileAsync( "Ann" );
			Assert.AreEqual( 1, afterVote.Parks[0].Rank );
			Assert.AreEqual( 1, afterVote.Parks[0].HighestRank );
			var ownAfterVote = await bobClient.GetPlayerProfileAsync( "Bob" );
			Assert.AreEqual( 2, ownAfterVote.Parks[0].Rank );
			Assert.AreEqual( 1, ownAfterVote.Parks[0].HighestRank );
			// Taking the leader out lifts the rest and their best places follow.
			await bobClient.UnpublishParkAsync( popular.Id );
			Assert.AreEqual( 1, (await publicClient.GetPlayerProfileAsync( "Ann" )).Parks[0].Rank );

			// Player search by name (S1): at most fifty results, more asks for detail. Without a login only players
			// who show a park on the website are found, so the public search cannot list every account.
			for ( var index = 0; index < 51; index++ )
				await publicClient.RegisterAsync( $"Crowd {index:D2}", "crowd password" );
			var anonymous = await new OnlineClient( server.Url ).SearchPlayersAsync( "Crowd" );
			Assert.AreEqual( 0, anonymous.Names.Count );
			CollectionAssert.AreEqual( new[] { "Ann" }, (await new OnlineClient( server.Url ).SearchPlayersAsync( "Ann" )).Names.ToArray() );
			var search = await annClient.SearchPlayersAsync( "Crowd" );
			Assert.AreEqual( 50, search.Names.Count );
			Assert.IsTrue( search.More );
			var narrow = await annClient.SearchPlayersAsync( "Crowd 49" );
			Assert.AreEqual( 1, narrow.Names.Count );
			Assert.IsFalse( narrow.More );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task MovementIsNotRelayedInTheLobby()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		using var annClient = await server.LoginAsync( "Ann" );
		using var bobClient = await server.LoginAsync( "Bob" );
		await using var ann = await annClient.ConnectChatAsync();
		await NextAsync( ann, item => item.Kind == ChatEventKind.Room );
		await using var bob = await bobClient.ConnectChatAsync();
		await NextAsync( bob, item => item.Kind == ChatEventKind.Room );
		await bob.MoveAsync( 0.5f, 0.5f );
		await ann.MoveAsync( 0.25f, 0.75f );
		// A chat line sent after the moves arrives with no movement before it.
		await ann.SendAsync( ChatCommand.Say, "after" );
		var next = await NextAsync( bob, item => item.Kind is ChatEventKind.Move or ChatEventKind.Say );
		Assert.AreEqual( (ChatEventKind.Say, "after"), (next.Kind, next.Text) );
	}

	[TestMethod]
	public async Task EachRoomsFirstMovementAnswersWhereEveryoneStands()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		try
		{
			using var annClient = await server.LoginAsync( "Ann" );
			using var bobClient = await server.LoginAsync( "Bob" );
			await using var ann = await annClient.ConnectChatAsync();
			await NextAsync( ann, item => item.Kind == ChatEventKind.Room );
			await using var bob = await bobClient.ConnectChatAsync();
			await NextAsync( bob, item => item.Kind == ChatEventKind.Room );
			// Bob walks in one park first, so his connection already draws movement
			var first = ChatProtocol.SatelliteRoom( 1 );
			await bob.JoinAsync( first );
			await NextAsync( bob, item => item.Kind == ChatEventKind.Room && item.Room == first );
			await bob.MoveAsync( 0.5f, 0.5f );
			// Ann stands in a second park
			var second = ChatProtocol.SatelliteRoom( 2 );
			await ann.JoinAsync( second );
			await NextAsync( ann, item => item.Kind == ChatEventKind.Room && item.Room == second );
			await ann.MoveAsync( 0.25f, 0.75f );
			// Bob's first movement in the second park is answered with where Ann stands, though his connection walked before
			await bob.JoinAsync( second );
			await NextAsync( bob, item => item.Kind == ChatEventKind.Room && item.Room == second );
			await bob.MoveAsync( 0.6f, 0.4f );
			var annStands = await NextAsync( bob, item => item.Kind == ChatEventKind.Move && item.From == "Ann" );
			Assert.AreEqual( (second, 0.25f, 0.75f), (annStands.Room, annStands.X, annStands.Y) );
		}
		finally { server.DeleteData(); }
	}

	[TestMethod]
	public async Task JoiningTheRoomAgainKeepsThePlaceAndTellsNobody()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		try
		{
			using var annClient = await server.LoginAsync( "Ann" );
			using var bobClient = await server.LoginAsync( "Bob" );
			await using var ann = await annClient.ConnectChatAsync();
			await NextAsync( ann, item => item.Kind == ChatEventKind.Room );
			await using var bob = await bobClient.ConnectChatAsync();
			await NextAsync( bob, item => item.Kind == ChatEventKind.Room );
			var room = ChatProtocol.SatelliteRoom( 1 );
			await ann.JoinAsync( room );
			await NextAsync( ann, item => item.Kind == ChatEventKind.Room && item.Room == room );
			await ann.MoveAsync( 0.25f, 0.75f );
			await bob.JoinAsync( room );
			await NextAsync( bob, item => item.Kind == ChatEventKind.Room && item.Room == room );
			await NextAsync( ann, item => item.Kind == ChatEventKind.Room && item.Room == room );
			await bob.MoveAsync( 0.5f, 0.5f );
			await NextAsync( bob, item => item.Kind == ChatEventKind.Move && item.From == "Ann" );
			await NextAsync( ann, item => item.Kind == ChatEventKind.Move && item.From == "Bob" );
			// a retried join of the room Bob is in answers him the roster and nothing else
			await bob.JoinAsync( room );
			Assert.AreEqual( "Ann,Bob", string.Join( ",", (await NextAsync( bob, item => item.Kind == ChatEventKind.Room )).Args! ) );
			await bob.MoveAsync( 0.6f, 0.4f );
			await bob.SendAsync( ChatCommand.Say, "after" );
			// Bob keeps his place: his next movement is no first one, so Ann's place is not sent again
			var bobSees = new List<ChatEvent>();
			for ( var item = await NextAsync( bob ); item.Kind != ChatEventKind.Say; item = await NextAsync( bob ) )
				bobSees.Add( item );
			Assert.IsFalse( bobSees.Any( item => item.Kind == ChatEventKind.Move ), string.Join( " | ", bobSees.Select( item => item.Kind ) ) );
			// and Ann is told nothing of the join: no roster before Bob's next step
			var annSees = new List<ChatEvent>();
			for ( var item = await NextAsync( ann ); item.Kind != ChatEventKind.Move; item = await NextAsync( ann ) )
				annSees.Add( item );
			Assert.AreEqual( 0, annSees.Count, string.Join( " | ", annSees.Select( item => item.Kind ) ) );
			var annHears = await NextAsync( ann );
			Assert.AreEqual( (ChatEventKind.Say, "Bob"), (annHears.Kind, annHears.From), "Ann hears Bob's line next" );
			// a goto to a player in the same room is the same: Bob is told, keeps his place, and Ann hears nothing of it
			await bob.SendAsync( ChatCommand.Goto, "Ann" );
			await NextAsync( bob, IsNotice( ChatNotice.YouGoTo ) );
			await NextAsync( bob, item => item.Kind == ChatEventKind.Room );
			await bob.MoveAsync( 0.7f, 0.3f );
			var annAfterGoto = await NextAsync( ann );
			Assert.AreEqual( (ChatEventKind.Move, "Bob"), (annAfterGoto.Kind, annAfterGoto.From), "no roster went to Ann" );
		}
		finally { server.DeleteData(); }
	}

	[TestMethod]
	public async Task ARidingPlayerIsHiddenFromThoseWhoReadModesAndKeptForTheOthers()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		try
		{
			using var ownerClient = await server.LoginAsync( "Owner" );
			var room = ChatProtocol.ParkRoom( (await ownerClient.UploadParkAsync( OnlineFormatTests.Package() )).Id );
			var clients = new List<IDisposable>();
			async Task<ChatConnection> Enter( string name )
			{
				var client = await server.LoginAsync( name );
				clients.Add( client );
				var chat = await client.ConnectChatAsync();
				await NextAsync( chat, item => item.Kind == ChatEventKind.Room );
				await chat.JoinAsync( room );
				await NextAsync( chat, item => item.Kind == ChatEventKind.Room && item.Room == room );
				return chat;
			}
			// Ann and Bob send modes (protocol 5); Carol is an older client that sends places alone
			await using var ann = await Enter( "Ann" );
			await using var bob = await Enter( "Bob" );
			await using var carol = await Enter( "Carol" );
			await bob.MoveAsync( 0.5f, 0.5f, ChatMoveMode.Walk );
			await carol.MoveAsync( 0.6f, 0.5f );
			await ann.MoveAsync( 0.2f, 0.7f, ChatMoveMode.Walk );
			// once Ann knows where both stand (her first movement's answer, or their own movements), the server has handled their movements
			var known = new HashSet<string>();
			while ( known.Count < 2 )
				known.Add( (await NextAsync( ann, item => item.Kind == ChatEventKind.Move )).From! );
			await ann.MoveAsync( 0.25f, 0.75f, ChatMoveMode.Walk );
			bool AnnAt( ChatEvent item, float x ) => item.Kind == ChatEventKind.Move && item.From == "Ann" && item.X == x;
			Assert.AreEqual( ChatMoveMode.Walk, (await NextAsync( bob, item => AnnAt( item, 0.25f ) )).Mode );
			Assert.IsNull( (await NextAsync( carol, item => AnnAt( item, 0.25f ) )).Mode, "an older client gets no mode" );

			// Ann boards a ride: Bob is told her figure is hidden, Carol is not told anything and keeps it where it stood
			await ann.MoveAsync( 0.25f, 0.75f, ChatMoveMode.Hide );
			var hidden = await NextAsync( bob, item => item.Kind == ChatEventKind.Move && item.From == "Ann" );
			Assert.AreEqual( ChatMoveMode.Hide, hidden.Mode, hidden.ToString() );
			await ann.SendAsync( ChatCommand.Say, "on the ride" );
			Assert.AreEqual( ChatEventKind.Say, (await NextAsync( carol, item => item.From == "Ann" )).Kind, "no movement of Ann reached Carol before her line" );

			// a joiner is told where the others stand, not where the riding player stood
			await using var dave = await Enter( "Dave" );
			await dave.MoveAsync( 0.1f, 0.1f, ChatMoveMode.Walk );
			await dave.SendAsync( ChatCommand.Say, "hello" );
			var seen = new List<string>();
			for ( var item = await NextAsync( dave, item => item.Kind is ChatEventKind.Move or ChatEventKind.Say ); item.Kind == ChatEventKind.Move;
				item = await NextAsync( dave, item => item.Kind is ChatEventKind.Move or ChatEventKind.Say ) )
				seen.Add( item.From! );
			CollectionAssert.AreEquivalent( new[] { "Bob", "Carol" }, seen );

			// the ride ends: Ann appears at her place for everyone, as a plain place for Carol
			await ann.MoveAsync( 0.25f, 0.75f, ChatMoveMode.Unhide );
			Assert.AreEqual( ChatMoveMode.Unhide, (await NextAsync( bob, item => item.Kind == ChatEventKind.Move && item.From == "Ann" )).Mode );
			Assert.AreEqual( ChatMoveMode.Unhide, (await NextAsync( dave, item => item.Kind == ChatEventKind.Move && item.From == "Ann" )).Mode );
			var shown = await NextAsync( carol, item => item.Kind == ChatEventKind.Move && item.From == "Ann" );
			Assert.AreEqual( (0.25f, 0.75f, (ChatMoveMode?)null), (shown.X, shown.Y, shown.Mode) );
			clients.ForEach( client => client.Dispose() );
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AHiddenRiderLeavingOrDisconnectingIsNotShown()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		try
		{
			using var ownerClient = await server.LoginAsync( "Owner" );
			var room = ChatProtocol.ParkRoom( (await ownerClient.UploadParkAsync( OnlineFormatTests.Package() )).Id );
			var clients = new List<IDisposable>();
			async Task<ChatConnection> Enter( string name )
			{
				var client = await server.LoginAsync( name );
				clients.Add( client );
				var chat = await client.ConnectChatAsync();
				await NextAsync( chat, item => item.Kind == ChatEventKind.Room );
				await chat.JoinAsync( room );
				await NextAsync( chat, item => item.Kind == ChatEventKind.Room && item.Room == room );
				return chat;
			}
			await using var ann = await Enter( "Ann" );
			await using var bob = await Enter( "Bob" );
			await using var carol = await Enter( "Carol" );
			await bob.MoveAsync( 0.5f, 0.5f, ChatMoveMode.Walk );
			await carol.MoveAsync( 0.6f, 0.4f );
			await ann.MoveAsync( 0.2f, 0.3f, ChatMoveMode.Walk );
			// once Ann knows where both stand (her first movement's answer, or their own movements), the server has handled their movements
			var known = new HashSet<string>();
			while ( known.Count < 2 )
				known.Add( (await NextAsync( ann, item => item.Kind == ChatEventKind.Move )).From! );
			// Bob's first place for Ann can be the modeless snapshot of that first movement (ReceivesMovement is set before ReceivesModes, and the
			// snapshot carries no mode). A later place, matched by its coordinates, is the one that has to arrive as a hide.
			await ann.MoveAsync( 0.25f, 0.35f, ChatMoveMode.Walk );
			bool AnnAt( ChatEvent item, float x ) => item.Kind == ChatEventKind.Move && item.From == "Ann" && item.X == x;
			Assert.AreEqual( ChatMoveMode.Walk, (await NextAsync( bob, item => AnnAt( item, 0.25f ) )).Mode );
			Assert.IsNull( (await NextAsync( carol, item => AnnAt( item, 0.25f ) )).Mode, "an older client gets no mode" );

			await ann.MoveAsync( 0.25f, 0.35f, ChatMoveMode.Hide );
			var hidden = await NextAsync( bob, item => item.Kind == ChatEventKind.Move && item.From == "Ann" );
			Assert.AreEqual( ChatMoveMode.Hide, hidden.Mode, hidden.ToString() );
			await ann.SendAsync( ChatCommand.Say, "riding" );
			Assert.AreEqual( ChatEventKind.Say, (await NextAsync( carol, item => item.From == "Ann" )).Kind, "no movement of Ann reached Carol before her line" );

			// leaving the park while hidden updates the roster and does not place her back in view
			await ann.JoinAsync( ChatProtocol.LobbyRoom );
			var bobLeft = await UntilRoom( bob, room, names => !names.Contains( "Ann" ) );
			var carolLeft = await UntilRoom( carol, room, names => !names.Contains( "Ann" ) );
			Assert.IsFalse( bobLeft.Any( item => item.Kind == ChatEventKind.Move && item.From == "Ann" ), "leaving showed Ann to a client that reads modes" );
			Assert.IsFalse( carolLeft.Any( item => item.Kind == ChatEventKind.Move && item.From == "Ann" ), "leaving showed Ann to an older client" );

			await ann.JoinAsync( room );
			await NextAsync( ann, item => item.Kind == ChatEventKind.Room && item.Room == room );
			await NextAsync( bob, item => item.Kind == ChatEventKind.Room && item.Room == room && item.Args!.Contains( "Ann" ) );
			await NextAsync( carol, item => item.Kind == ChatEventKind.Room && item.Room == room && item.Args!.Contains( "Ann" ) );
			await ann.MoveAsync( 0.2f, 0.3f, ChatMoveMode.Hide );
			Assert.AreEqual( ChatMoveMode.Hide, (await NextAsync( bob, item => item.Kind == ChatEventKind.Move && item.From == "Ann" )).Mode );

			// disconnecting while hidden is the same: the roster drops her and no place follows
			await ann.DisposeAsync();
			var bobGone = await UntilRoom( bob, room, names => !names.Contains( "Ann" ) );
			var carolGone = await UntilRoom( carol, room, names => !names.Contains( "Ann" ) );
			Assert.IsFalse( bobGone.Any( item => item.Kind == ChatEventKind.Move && item.From == "Ann" ), "disconnecting showed Ann to a client that reads modes" );
			Assert.IsFalse( carolGone.Any( item => item.Kind == ChatEventKind.Move && item.From == "Ann" ), "disconnecting showed Ann to an older client" );
			clients.ForEach( client => client.Dispose() );
		}
		finally
		{
			server.DeleteData();
		}
	}

	/// <summary>Events up to and including the room roster that matches <paramref name="roster"/>.</summary>
	private static async Task<List<ChatEvent>> UntilRoom( ChatConnection chat, string room, Func<IReadOnlyList<string>, bool> roster )
	{
		var seen = new List<ChatEvent>();
		while ( true )
		{
			var item = await NextAsync( chat );
			seen.Add( item );
			if ( item.Kind == ChatEventKind.Room && item.Room == room && item.Args is { } names && roster( names ) )
				return seen;
		}
	}

	[TestMethod]
	public async Task ChatParksRelayMovementPresenceAndBuddyEntries()
	{
		await using var server = await LoopbackServer.StartAsync( options =>
		{
			options.ChatBurst = 50;
			options.MovementBurst = 3;
			options.MovementsPerSecond = 2;
		} );
		try
		{
			using var annClient = await server.LoginAsync( "Ann" );
			using var bobClient = await server.LoginAsync( "Bob" );
			using var ownerClient = await server.LoginAsync( "Owner" );
			await using var ann = await annClient.ConnectChatAsync();
			await NextAsync( ann, item => item.Kind == ChatEventKind.Room );
			await using var bob = await bobClient.ConnectChatAsync();
			await NextAsync( bob, item => item.Kind == ChatEventKind.Room );

			// A chat park of the globe's satellites: the joiner sees everyone, everyone sees the joiner.
			var satellite = ChatProtocol.SatelliteRoom( 1 );
			await ann.JoinAsync( satellite );
			Assert.AreEqual( "Ann", string.Join( ",", (await NextAsync( ann, item => item.Kind == ChatEventKind.Room && item.Room == satellite )).Args! ) );
			await bob.JoinAsync( satellite );
			Assert.AreEqual( "Ann,Bob", string.Join( ",", (await NextAsync( bob, item => item.Kind == ChatEventKind.Room && item.Room == satellite )).Args! ) );
			Assert.AreEqual( "Ann,Bob", string.Join( ",", (await NextAsync( ann, item => item.Kind == ChatEventKind.Room && item.Room == satellite )).Args! ) );
			// The player count of the satellite list includes them.
			using var counter = new OnlineClient( server.Url );
			Assert.AreEqual( 2, (await counter.GetChatParksAsync())!.ChatParks.Single( park => park.Id == 1 ).Players );

			// Walking is relayed to everyone in the park; a client's first movement also asks for the places of
			// everyone already there, without ever sending movement to an older client that never walked.
			await bob.MoveAsync( 0.5f, 0.5f );
			await ann.MoveAsync( 0.25f, 0.75f );
			var annMoved = await NextAsync( bob, item => item.Kind == ChatEventKind.Move && item.From == "Ann" );
			Assert.AreEqual( ("Ann", satellite, 0.25f, 0.75f), (annMoved.From, annMoved.Room, annMoved.X, annMoved.Y) );
			using var carolClient = await server.LoginAsync( "Carol" );
			await using var carol = await carolClient.ConnectChatAsync();
			await NextAsync( carol, item => item.Kind == ChatEventKind.Room );
			await carol.JoinAsync( satellite );
			await NextAsync( carol, item => item.Kind == ChatEventKind.Room && item.Room == satellite );
			await carol.MoveAsync( 0.75f, 0.25f );
			var carolSeesAnn = await NextAsync( carol, item => item.Kind == ChatEventKind.Move && item.From == "Ann" );
			Assert.AreEqual( (0.25f, 0.75f), (carolSeesAnn.X, carolSeesAnn.Y) );
			// Carol's first movement reached the park's other players as well.
			var carolMoved = await NextAsync( bob, item => item.Kind == ChatEventKind.Move && item.From == "Carol" );
			Assert.AreEqual( (0.75f, 0.25f), (carolMoved.X, carolMoved.Y) );

			// A flood of positions is dropped, not answered and not punished: the connection stays and chat flows.
			for ( var index = 0; index < 8; index++ )
				await bob.MoveAsync( index / 8f, 0.5f );
			await Task.Delay( 200 );
			await bob.SendAsync( ChatCommand.Say, "still here" );
			Assert.AreEqual( "still here", (await NextAsync( ann, item => item.Kind == ChatEventKind.Say )).Text );

			// A parting player leaves the roster of the park for everyone who stays.
			await carol.DisposeAsync();
			Assert.AreEqual( "Ann,Bob", string.Join( ",", (await NextAsync( bob, item => item.Kind == ChatEventKind.Room && item.Room == satellite && item.Args is { Count: 2 } )).Args! ) );

			// Buddies are told which park their buddy entered (S5).
			await bob.SendAsync( ChatCommand.Buddy, "Ann" );
			await NextAsync( bob, IsNotice( ChatNotice.YouMakeBuddy ) );
			await ann.JoinAsync( ChatProtocol.SatelliteRoom( 2 ) );
			await NextAsync( ann, item => item.Kind == ChatEventKind.Room && item.Room == ChatProtocol.SatelliteRoom( 2 ) );
			// The park Ann walked out of gets its roster without her.
			Assert.AreEqual( "Bob", string.Join( ",", (await NextAsync( bob, item => item.Kind == ChatEventKind.Room && item.Room == satellite && item.Args is { Count: 1 } )).Args! ) );
			var entered = await NextAsync( bob, item => item.Kind == ChatEventKind.System && item.Text == ChatSystemKeys.BuddyEnters );
			Assert.AreEqual( "Ann", entered.Args![0] );
			// The chat parks' default names (docs/SERVER.md): the notification names the park itself.
			StringAssert.Contains( entered.Args[1], "Fairground", $"satellite 2 name was {entered.Args[1]}" );

			// A page carries its message to any park: "Dan pages you: Hello" (manual p. 70, S5).
			await bob.SendAsync( ChatCommand.Page, "Ann Hello" );
			var paged = await NextAsync( ann, item => item.Kind == ChatEventKind.Page );
			Assert.AreEqual( ("Bob", "Hello"), (paged.From, paged.Text) );
			var youPage = await NextAsync( bob, IsNotice( ChatNotice.YouPage ) );
			Assert.AreEqual( ("Ann", "Hello"), (youPage.Args![0], youPage.Args[1]) );

			// Locate names the park and its city: "Halloween World in San Francisco" (manual p. 70).
			var park = await ownerClient.UploadParkAsync( OnlineFormatTests.Package( "Halloween World" ), cityId: 5 );
			await using var owner = await ownerClient.ConnectChatAsync();
			await NextAsync( owner, item => item.Kind == ChatEventKind.Room );
			await owner.JoinAsync( ChatProtocol.ParkRoom( park.Id ) );
			await NextAsync( owner, item => item.Kind == ChatEventKind.Room && item.Room == ChatProtocol.ParkRoom( park.Id ) );
			await bob.SendAsync( ChatCommand.Locate, "Owner" );
			var located = await NextAsync( bob, IsNotice( ChatNotice.ThePark ) );
			Assert.AreEqual( "Halloween World in San Francisco", located.Args![1] );
		}
		finally
		{
			server.DeleteData();
		}
	}

}
