using System;
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

	public static async Task<LoopbackServer> StartAsync( Action<ServerOptions>? configure = null, string? directory = null )
	{
		directory ??= Path.Combine( Path.GetTempPath(), $"opentpw-server-{Guid.NewGuid():N}" );
		var app = ServerProgram.Build( new[] { "--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Warning" }, options =>
		{
			options.DataDirectory = directory;
			options.PasswordIterations = 1000;
			configure?.Invoke( options );
		} );
		await app.StartAsync();
		var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		return new LoopbackServer( app, directory, new Uri( address ) );
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
		await app.StopAsync();
		await app.DisposeAsync();
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
			Assert.AreEqual( 1, info.ProtocolVersion );
			Assert.IsFalse( info.WordFilterLoaded );
			await client.RegisterAsync( "Sander", "correct horse" );
			Assert.AreEqual( HttpStatusCode.Conflict, (await Fails( () => client.RegisterAsync( "SANDER", "another one" ) )).Status );
			Assert.AreEqual( HttpStatusCode.BadRequest, (await Fails( () => client.RegisterAsync( "x", "correct horse" ) )).Status );
			Assert.AreEqual( HttpStatusCode.BadRequest, (await Fails( () => client.RegisterAsync( "Shorty", "short" ) )).Status );
			Assert.AreEqual( HttpStatusCode.Unauthorized, (await Fails( () => client.LoginAsync( "Sander", "wrong password" ) )).Status );
			Assert.AreEqual( HttpStatusCode.Unauthorized, (await Fails( () => client.LoginAsync( "Nobody", "correct horse" ) )).Status );
			Assert.AreEqual( HttpStatusCode.Unauthorized, (await Fails( () => client.ListParksAsync() )).Status );
			var session = await client.LoginAsync( "sander", "correct horse" );
			Assert.AreEqual( "Sander", session.Name );
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
			Assert.AreEqual( 1, (await visitor.RecordVisitAsync( published.Id )).Visits );
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
	public async Task AuthenticationIsRateLimited()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.AuthenticationsPerMinute = 3 );
		try
		{
			using var client = new OnlineClient( server.Url );
			for ( var attempt = 0; attempt < 3; attempt++ )
				await Fails( () => client.LoginAsync( "Nobody", "wrong password" ) );
			Assert.AreEqual( HttpStatusCode.TooManyRequests, (await Fails( () => client.LoginAsync( "Nobody", "wrong password" ) )).Status );
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

	private static async Task<ChatEvent> NextAsync( ChatConnection chat, Func<ChatEvent, bool>? match = null )
	{
		using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
		while ( true )
		{
			var item = await chat.ReceiveAsync( timeout.Token ) ?? throw new AssertFailedException( "Chat closed." );
			if ( match == null || match( item ) )
				return item;
		}
	}

	private static Func<ChatEvent, bool> IsNotice( ChatNotice notice ) => item => item.Kind == ChatEventKind.Notice && item.Notice == (int)notice;

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
}
