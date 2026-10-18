using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Chat;
using OpenTPW.Online.Client;
using OpenTPW.Online.Packages;

namespace OpenTPW.Tests;

[TestClass]
public class OnlineIntegrationTests
{
	internal static OnlineFolders Folders() => new( Path.Combine( Path.GetTempPath(), "opentpw-online-" + Guid.NewGuid().ToString( "N" ) ) );

	internal static async Task PumpUntilIdle( OnlineSession session )
	{
		var until = DateTime.UtcNow.AddSeconds( 10 );
		// chat traffic runs beside the operation slot (OnlineSession.ChatBusy), so both must settle
		while ( (session.Busy > 0 || session.ChatBusy) && DateTime.UtcNow < until )
		{
			session.Pump();
			await Task.Delay( 10 );
		}
		session.Pump();
		Assert.AreEqual( 0, session.Busy, session.Status );
		Assert.IsFalse( session.ChatBusy, session.Status );
	}

	[TestMethod]
	public async Task SessionWalksAChatParkAndReportsBuddyEntries()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		try
		{
			// Two full sessions against one server: the movement of one player reaches the other, and a buddy
			// is told which park their buddy entered.
			var ann = new OnlineSession( Folders() );
			var bob = new OnlineSession( Folders() );
			ann.UseServer( server.Url.ToString(), "Ann" );
			bob.UseServer( server.Url.ToString(), "Bob" );
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			bob.Register( "Bob", "correct horse" );
			await PumpUntilIdle( bob );
			ann.ConnectChat();
			await PumpUntilIdle( ann );
			bob.ConnectChat();
			await PumpUntilIdle( bob );
			Assert.IsTrue( ann.IsLoggedIn && bob.IsLoggedIn, $"{ann.Status} / {bob.Status}" );

			var satellite = ChatProtocol.SatelliteRoom( 1 );
			ann.JoinRoom( satellite );
			bob.JoinRoom( satellite );
			await PumpUntilIdle( ann );
			await PumpUntilIdle( bob );
			// A player's first movement asks the park where everyone stands; afterwards every step is relayed.
			bob.MoveTo( 0.5f, 0.5f );
			await PumpUntilIdle( bob );
			ann.MoveTo( 0.3f, 0.7f );
			await PumpUntilIdle( ann );
			Assert.IsTrue( await Until( bob, () => bob.RoomPositions.TryGetValue( "Ann", out var place ) && place == (0.3f, 0.7f) ),
				"Bob never saw Ann's relayed position." );
			// The roster of the chat park names both players.
			Assert.IsTrue( await Until( bob, () => bob.RoomPlayers.Contains( "Ann" ) && bob.RoomPlayers.Contains( "Bob" ) ) );

			ann.SendChatLine( "/buddy Bob" );
			await PumpUntilIdle( ann );
			// the server's answer, not the sent line, shows it keeps Bob as Ann's buddy: each connection's lines are handled on their own, so Bob's join
			// could otherwise come first
			Assert.IsTrue( await Until( ann, () => ann.Buddies.Any( buddy => buddy.Name == "Bob" ) ), string.Join( " | ", ann.ChatLines ) );
			bob.JoinRoom( ChatProtocol.SatelliteRoom( 2 ) );
			await PumpUntilIdle( bob );
			Assert.IsTrue( await Until( ann, () => ann.ChatLines.Any( line => line.Contains( "has entered" ) ) ),
				"Ann was not told that her buddy entered a park: " + string.Join( " | ", ann.ChatLines ) );

			ann.Dispose();
			bob.Dispose();
		}
		finally
		{
			server.DeleteData();
		}
	}

	/// <summary>Every cell passable (the presence's rules are tested in ParkPresenceStateTests).</summary>
	private sealed class OpenGround : IPresenceGround
	{
		public int Width => 128;
		public int Height => 128;
		public bool IsPassable( int x, int y ) => true;
	}

	private static ParkPresence Visit( OnlineSession session, string parkId, (float X, float Y) start )
	{
		var clock = System.Diagnostics.Stopwatch.StartNew();
		return new ParkPresence( session, parkId, session.Settings.PlayerName!, new OpenGround(), start, () => clock.Elapsed.TotalSeconds );
	}

	/// <summary>
	/// Pumps both sessions and updates both visits until <paramref name="condition"/> holds, or twenty seconds pass. Each round advances the visits by a
	/// quarter of a second of walking, so a walk does not take its real time on a slow machine (the network still runs in real time).
	/// </summary>
	private static async Task<bool> Until( OnlineSession first, ParkPresence firstVisit, OnlineSession second, ParkPresence secondVisit, Func<bool> condition )
	{
		var until = DateTime.UtcNow.AddSeconds( 20 );
		while ( DateTime.UtcNow < until )
		{
			first.Pump();
			second.Pump();
			firstVisit.Update( 0.25f );
			secondVisit.Update( 0.25f );
			if ( condition() )
				return true;
			await Task.Delay( 20 );
		}
		return condition();
	}

	[TestMethod]
	public async Task VisitorsOfAPublishedParkSeeEachOtherWalk()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		try
		{
			using var owner = await server.LoginAsync( "Owner" );
			var park = await owner.UploadParkAsync( OnlineFormatTests.Package() );
			var ann = new OnlineSession( Folders() );
			var bob = new OnlineSession( Folders() );
			ann.UseServer( server.Url.ToString(), "Ann" );
			bob.UseServer( server.Url.ToString(), "Bob" );
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			bob.Register( "Bob", "correct horse" );
			await PumpUntilIdle( bob );
			// both visits start right after a download, without a chat connection: each opens one and joins the park's room
			var annVisit = Visit( ann, park.Id, (10.5f, 10.5f) );
			var bobVisit = Visit( bob, park.Id, (20.5f, 10.5f) );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => annVisit.State.Others.Any( other => other.Name == "Bob" ) && bobVisit.State.Others.Any( other => other.Name == "Ann" ) ),
				$"the visitors never saw each other: {ann.Status} / {bob.Status} / rooms {ann.CurrentRoom}, {bob.CurrentRoom}" );
			Assert.AreEqual( ParkPresence.MovementProtocol, ann.ServerProtocol );
			var annSeen = bobVisit.State.Others.Single( other => other.Name == "Ann" );
			Assert.AreEqual( 10.5f, annSeen.X, 0.01f, "Bob sees Ann where she stands" );
			// Ann walks; Bob's view of her follows
			annVisit.Click( 15.5f, 10.5f );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => !annVisit.State.Walking && MathF.Abs( annSeen.X - 15.5f ) < 0.05f ),
				$"Bob's view of Ann did not follow her walk ({annSeen.X}, {annVisit.State.Own.X})" );
			// a line said in the park reaches the other visitor
			annVisit.Say( "hello park" );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => bob.ChatLines.Any( line => line.Contains( "hello park" ) ) ) );
			// Ann leaves: her visit closes the connection it opened, and she is gone from Bob's park
			annVisit.Dispose();
			Assert.IsNull( ann.Chat );
			Assert.AreEqual( 0, ann.ChatLines.Count, "the visit's lines do not follow her home" );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => bobVisit.State.Others.Count == 0 ), "Ann left Bob's park" );
			bobVisit.Dispose();
			ann.Dispose();
			bob.Dispose();
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task RosterUpdatesKeepTheRoomsPlacesAndAJoinDroppedByTheRateLimitIsSentAgain()
	{
		// one chat request a second: a join right after a line is dropped by the server's limit
		await using var server = await LoopbackServer.StartAsync( options => { options.ChatBurst = 1; options.ChatMessagesPerSecond = 1; } );
		try
		{
			var ann = new OnlineSession( Folders() );
			ann.UseServer( server.Url.ToString(), "Ann" );
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			ann.ConnectChat();
			await PumpUntilIdle( ann );
			ann.SendChatLine( "hello" );
			var satellite = ChatProtocol.SatelliteRoom( 1 );
			ann.JoinRoom( satellite );
			Assert.AreEqual( satellite, ann.WantedRoom );
			Assert.IsTrue( await Until( ann, () => ann.CurrentRoom == satellite ), "the dropped join was sent again" );
			ann.MoveTo( 0.25f, 0.5f );
			await PumpUntilIdle( ann );
			// someone else enters: the roster changes, the places stay
			using var bobClient = await server.LoginAsync( "Bob" );
			await using var bob = await bobClient.ConnectChatAsync();
			await Task.Delay( 1100 );
			await bob.JoinAsync( satellite );
			Assert.IsTrue( await Until( ann, () => ann.RoomPlayers.Contains( "Bob" ) ) );
			Assert.IsTrue( ann.RoomPositions.ContainsKey( "Ann" ), "a roster update of the same room keeps the places" );
			// a park that does not exist is refused, and remembered as refused
			await Task.Delay( 1100 );
			ann.JoinRoom( ChatProtocol.ParkRoom( "missing" ) );
			Assert.IsTrue( await Until( ann, () => ann.RefusedRoom == ChatProtocol.ParkRoom( "missing" ) ) );
			Assert.AreEqual( satellite, ann.WantedRoom, "the player stays where they were" );
			ann.Disconnect();
			Assert.IsNull( ann.RefusedRoom );
			Assert.AreEqual( ChatProtocol.LobbyRoom, ann.WantedRoom );
			ann.Dispose();
		}
		finally
		{
			server.DeleteData();
		}
	}

	/// <summary>Pumps <paramref name="session"/> until <paramref name="condition"/> holds, or ten seconds pass.</summary>
	[TestMethod]
	public async Task AVisitsChatOverlayFollowsTheServersAnswers()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		try
		{
			using var owner = await server.LoginAsync( "Owner" );
			var park = await owner.UploadParkAsync( OnlineFormatTests.Package() );
			var ann = new OnlineSession( Folders() );
			var bob = new OnlineSession( Folders() );
			ann.UseServer( server.Url.ToString(), "Ann" );
			bob.UseServer( server.Url.ToString(), "Bob" );
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			bob.Register( "Bob", "correct horse" );
			await PumpUntilIdle( bob );
			var annVisit = Visit( ann, park.Id, (10.5f, 10.5f) );
			var bobVisit = Visit( bob, park.Id, (20.5f, 10.5f) );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => annVisit.State.Others.Any( other => other.Name == "Bob" ) ) );
			var chat = annVisit.Chat;
			Assert.IsTrue( chat.ChatOpen );
			CollectionAssert.AreEquivalent( new[] { "Ann", "Bob" }, ChatPanelModel.People( chat, buddies: false ).Select( person => person.Name ).ToArray() );
			// the connection asked for the buddy list as it opened, keeping the answer (none) off the chat lines
			Assert.IsFalse( ann.ChatLines.Contains( OnlineStrings.Get( OnlineLabel.NoBuddies ) ), string.Join( " | ", ann.ChatLines ) );
			string BuddyLine( string name, bool online ) =>
				ChatFormatter.Format( ChatEvent.ForNotice( online ? ChatNotice.YourBuddyOnline : ChatNotice.YourBuddyOffline, name ), OnlineStrings.ChatCommands, _ => "", OnlineStrings.Get );
			// the earmuffs, the ignore list and the buddy list follow the server's answers
			chat.SendCommand( ChatCommand.Earmuffs, "" );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => chat.Earmuffs ) );
			chat.SendCommand( ChatCommand.Ignore, "Bob" );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => ChatPanelModel.Person( chat, "Bob" ).Flags == ChatPersonFlags.Ignored ) );
			chat.SendCommand( ChatCommand.Ignore, "Bob" );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => chat.Ignored.Count == 0 ) );
			chat.SendCommand( ChatCommand.Buddy, "Bob" );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => ChatPanelModel.Person( chat, "Bob" ).Flags == (ChatPersonFlags.Buddy | ChatPersonFlags.Online | ChatPersonFlags.InPark) ),
				"a buddy in the park is green" );
			// a new connection knows the buddy at once, without the buddy tab, and keeps the answer off the chat lines
			annVisit.Dispose();
			annVisit = Visit( ann, park.Id, (10.5f, 10.5f) );
			chat = annVisit.Chat;
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => ChatPanelModel.Person( chat, "Bob" ).Flags == (ChatPersonFlags.Buddy | ChatPersonFlags.Online | ChatPersonFlags.InPark) ),
				"the buddy is green before the buddy tab was opened" );
			Assert.IsFalse( ann.ChatLines.Contains( BuddyLine( "Bob", true ) ), string.Join( " | ", ann.ChatLines ) );
			// the buddy tab asks afresh, quietly too; a typed /buddy lists them in the chat lines
			chat.AskBuddies();
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => chat.Buddies.Any( buddy => buddy.Name == "Bob" && buddy.Online ) ) );
			Assert.IsFalse( ann.ChatLines.Contains( BuddyLine( "Bob", true ) ) );
			chat.SendChatLine( "/buddy" );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => ann.ChatLines.Contains( BuddyLine( "Bob", true ) ) ), string.Join( " | ", ann.ChatLines ) );
			chat.SendCommand( ChatCommand.Buddy, "Bob" );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => chat.Buddies.Count == 0 ), "a removed buddy leaves the list" );
			// the vote goes once per park
			Assert.IsTrue( chat.CanVote );
			chat.SendCommand( ChatCommand.Vote, "" );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => !chat.CanVote ) );
			// a goto to a player in the park takes the figure to them
			var bobSeen = annVisit.State.Others.Single( other => other.Name == "Bob" );
			annVisit.GoTo( "Bob" );
			Assert.AreEqual( (bobSeen.X, bobSeen.Y), (annVisit.State.Own.X, annVisit.State.Own.Y) );
			// a new connection starts without the earmuffs and the ignore list, as the server's does
			annVisit.Dispose();
			Assert.IsFalse( ann.Earmuffs );
			bobVisit.Dispose();
			ann.Dispose();
			bob.Dispose();
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ADroppedBuddyAskKeepsTheBuddyList()
	{
		// the default burst of chat requests, which a run of lines uses up
		await using var server = await LoopbackServer.StartAsync();
		try
		{
			using var owner = await server.LoginAsync( "Owner" );
			var park = await owner.UploadParkAsync( OnlineFormatTests.Package() );
			var ann = new OnlineSession( Folders() );
			var bob = new OnlineSession( Folders() );
			ann.UseServer( server.Url.ToString(), "Ann" );
			bob.UseServer( server.Url.ToString(), "Bob" );
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			bob.Register( "Bob", "correct horse" );
			await PumpUntilIdle( bob );
			var annVisit = Visit( ann, park.Id, (10.5f, 10.5f) );
			var bobVisit = Visit( bob, park.Id, (20.5f, 10.5f) );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => annVisit.State.Others.Any( other => other.Name == "Bob" ) ) );
			var chat = annVisit.Chat;
			chat.SendCommand( ChatCommand.Buddy, "Bob" );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => ChatPanelModel.Person( chat, "Bob" ).Flags.HasFlag( ChatPersonFlags.Buddy ) ) );
			// the lines use up the burst, so the server drops the ask after them: Bob stays a buddy, and his popup stays the buddy's
			for ( var index = 0; index < 12; index++ )
				chat.SendChatLine( $"line {index}" );
			chat.AskBuddies();
			Assert.IsTrue( ChatPanelModel.Person( chat, "Bob" ).Flags.HasFlag( ChatPersonFlags.Buddy ), "the list stands while the ask is out" );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => ann.ChatLines.Contains( OnlineStrings.Get( OnlineLabel.RateLimited ) ) ), string.Join( " | ", ann.ChatLines ) );
			for ( var round = 0; round < 10; round++ )
			{
				ann.Pump();
				await Task.Delay( 20 );
			}
			Assert.IsTrue( ChatPanelModel.Person( chat, "Bob" ).Flags.HasFlag( ChatPersonFlags.Buddy ), "a dropped ask leaves the list as it was" );
			annVisit.Dispose();
			bobVisit.Dispose();
			ann.Dispose();
			bob.Dispose();
		}
		finally
		{
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AVisitsVoteButtonKeepsTheServersRefusals()
	{
		// no votes left today: the day's limit refuses a vote for any park
		await using var server = await LoopbackServer.StartAsync( options =>
		{
			options.ChatBurst = 50;
			options.MaximumVotesPerDay = 0;
		} );
		try
		{
			using var owner = await server.LoginAsync( "Owner" );
			var first = await owner.UploadParkAsync( OnlineFormatTests.Package() );
			var second = await owner.UploadParkAsync( OnlineFormatTests.Package( "Second" ) );
			var ann = new OnlineSession( Folders() );
			var own = new OnlineSession( Folders() );
			ann.UseServer( server.Url.ToString(), "Ann" );
			own.UseServer( server.Url.ToString(), "Owner" );
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			own.Login( "Owner", "correct horse" );
			await PumpUntilIdle( own );
			var ownVisit = Visit( own, first.Id, (10.5f, 10.5f) );
			var annVisit = Visit( ann, first.Id, (20.5f, 10.5f) );
			Assert.IsTrue( await Until( ann, annVisit, own, ownVisit, () => ownVisit.Chat.CanVote && annVisit.Chat.CanVote ) );
			// the owner's vote for their own park is refused, and the vote button stays off
			ownVisit.Chat.SendCommand( ChatCommand.Vote, "" );
			Assert.IsTrue( await Until( ann, annVisit, own, ownVisit, () => !ownVisit.Chat.CanVote ) );
			Assert.IsTrue( own.ChatLines.Contains( OnlineStrings.ChatNotice( (int)ChatNotice.VoteFailed, null ) ), string.Join( " | ", own.ChatLines ) );
			// Ann's vote meets the day's limit, which holds in every park
			annVisit.Chat.SendCommand( ChatCommand.Vote, "" );
			Assert.IsTrue( await Until( ann, annVisit, own, ownVisit, () => !annVisit.Chat.CanVote ) );
			annVisit.Dispose();
			annVisit = Visit( ann, second.Id, (20.5f, 10.5f) );
			Assert.IsTrue( await Until( ann, annVisit, own, ownVisit, () => annVisit.CanChat ) );
			Assert.IsFalse( annVisit.Chat.CanVote, "the day's limit holds in another park" );
			annVisit.Dispose();
			ownVisit.Dispose();
			ann.Dispose();
			own.Dispose();
		}
		finally
		{
			server.DeleteData();
		}
	}

	private static async Task<bool> Until( OnlineSession session, Func<bool> condition )
	{
		var until = DateTime.UtcNow.AddSeconds( 10 );
		while ( DateTime.UtcNow < until )
		{
			session.Pump();
			if ( condition() )
				return true;
			await Task.Delay( 10 );
		}
		return condition();
	}

	[TestMethod]
	public void ChatLinesFormatBuddyEntriesAndPositions()
	{
		// A buddy entry (EXT:online-buddy-enters-text) reads as one line naming the buddy and the park.
		var entered = ChatFormatter.Format( new ChatEvent( ChatEventKind.System, Text: "buddy-enters", Args: new[] { "Ann", "The Fairground" } ),
			null, _ => "Online World", OnlineStrings.Get );
		Assert.AreEqual( "Your buddy Ann has entered The Fairground", entered );
	}

	[TestMethod]
	public void LocalFoldersKeepPostcardsAndReportUnreadableFiles()
	{
		var folders = Folders();
		try
		{
			Assert.IsNull( folders.LoadSettings().ServerUrl );
			folders.SaveSettings( new OnlineSettings( "http://localhost:5000/", "Alice" ) );
			Assert.AreEqual( "Alice", folders.LoadSettings().PlayerName );
			var card = Postcard.Create( "Alice", new[] { "Bobby" }, "Hello", "Visit my park", "English" );
			var file = folders.Store( folders.Outbox, card );
			Assert.AreEqual( file, folders.Store( folders.Outbox, card ) );
			File.WriteAllText( Path.Combine( folders.Outbox, "bad" + Postcard.FileExtension ), "broken" );
			var list = folders.List( folders.Outbox );
			Assert.AreEqual( 2, list.Count );
			Assert.AreEqual( 1, list.Count( entry => entry.Card != null ) );
			Assert.AreEqual( 1, list.Count( entry => entry.Error != null ) );
			File.WriteAllText( file, "damaged existing card" );
			Assert.ThrowsException<InvalidDataException>( () => folders.Store( folders.Outbox, card ) );
		}
		finally { Directory.Delete( folders.Root, true ); }
	}

	[TestMethod]
	public async Task SessionIgnoresResultsFromDisconnectedOperations()
	{
		var folders = Folders();
		using var session = new OnlineSession( folders );
		var release = new TaskCompletionSource<string?>( TaskCreationOptions.RunContinuationsAsynchronously );
		var started = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );
		var completed = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );
		session.Run( async () => { started.SetResult( true ); var result = await release.Task; completed.SetResult( true ); return result; } );
		session.Run( () => throw new InvalidOperationException( "Busy work must not start" ) );
		Assert.AreEqual( 1, session.Busy );
		await started.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
		session.Disconnect();
		session.Run( () => Task.FromResult<string?>( "new session" ) );
		await PumpUntilIdle( session );
		release.SetResult( "stale session" );
		await completed.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
		await Task.Delay( 30 );
		session.Pump();
		Assert.AreEqual( "new session", session.Status );
		Assert.AreEqual( 0, session.Busy );
	}

	[TestMethod]
	public async Task SessionUsesLoopbackServerForParksAndPostcardFolders()
	{
		await using var server = await LoopbackServer.StartAsync();
		var folders = Folders();
		try
		{
			using var recipient = await server.LoginAsync( "Bobby" );
			using var session = new OnlineSession( folders );
			session.UseServer( server.Url.ToString(), "Alice" );
			session.Register( "Alice", "correct horse" );
			await PumpUntilIdle( session );
			Assert.IsTrue( session.IsLoggedIn, session.Status );
			session.Publish( OnlineFormatTests.Package() );
			await PumpUntilIdle( session );
			session.RefreshParks( null );
			await PumpUntilIdle( session );
			Assert.AreEqual( 1, session.Parks.Count, session.Status );
			string? downloaded = null;
			session.Download( session.Parks[0], path => downloaded = path );
			await PumpUntilIdle( session );
			Assert.IsNotNull( downloaded, session.Status );
			Assert.AreEqual( "My Jungle", ParkPackage.Load( downloaded! ).Manifest.Park.Name );
			var card = Postcard.Create( "Alice", new[] { "Bobby" }, "Hello", "Postcard", "English" );
			folders.Store( folders.Outbox, card );
			session.SendOutbox();
			await PumpUntilIdle( session );
			Assert.AreEqual( 0, folders.List( folders.Outbox ).Count );
			Assert.AreEqual( 1, folders.List( folders.Sent ).Count );
			Assert.AreEqual( 1, (await recipient.GetInboxAsync()).Postcards.Count );
			await recipient.SendPostcardAsync( Postcard.Create( "Bobby", new[] { "Alice" }, "Reply", "Hello", "English" ) );
			// Two cards that reuse one card id (a sender's choice) must both arrive instead of blocking the inbox.
			var reused = Guid.NewGuid();
			await recipient.SendPostcardAsync( Postcard.Create( "Bobby", new[] { "Alice" }, "One", "first", "English", cardId: reused ) );
			await recipient.SendPostcardAsync( Postcard.Create( "Bobby", new[] { "Alice" }, "Two", "second", "English", cardId: reused ) );
			session.FetchInbox();
			await PumpUntilIdle( session );
			Assert.AreEqual( 3, folders.List( folders.Inbox ).Count, session.Status );
			Assert.AreEqual( 0, (await session.Client!.GetInboxAsync()).Postcards.Count );
			Assert.IsFalse( File.ReadAllText( folders.SettingsFile ).Contains( "correct horse" ) );
			session.ConnectChat();
			await PumpUntilIdle( session );
			Assert.IsNotNull( session.Chat, session.Status );
			session.SendChatLine( "hello from the game session" );
			await PumpUntilIdle( session );
			var until = DateTime.UtcNow.AddSeconds( 5 );
			while ( !session.ChatLines.Any( line => line.Contains( "hello from the game session" ) ) && DateTime.UtcNow < until )
			{
				session.Pump();
				await Task.Delay( 10 );
			}
			Assert.IsTrue( session.ChatLines.Any( line => line.Contains( "hello from the game session" ) ) );
		}
		finally { Directory.Delete( folders.Root, true ); server.DeleteData(); }
	}

	[TestMethod]
	public async Task ChatReconnectsToItsRoomAfterTheServerRestarts()
	{
		var server = await LoopbackServer.StartAsync();
		var directory = server.Directory;
		var url = server.Url;
		var folders = Folders();
		try
		{
			using var owner = await server.LoginAsync( "Bobby" );
			var park = await owner.UploadParkAsync( OnlineFormatTests.Package() );
			using var session = new OnlineSession( folders );
			session.UseServer( url.ToString(), "Alice" );
			session.Register( "Alice", "correct horse" );
			await PumpUntilIdle( session );
			session.RefreshParks( null );
			await PumpUntilIdle( session );
			session.ConnectChat();
			await PumpUntilIdle( session );
			var room = ChatProtocol.ParkRoom( park.Id );
			session.JoinRoom( room );
			await PumpUntil( session, () => session.CurrentRoom == room );

			await server.DisposeAsync();
			await using var restarted = await LoopbackServer.StartAsync( directory: directory, url: url );
			await PumpUntil( session, () => session.ChatLines.Any( line => line == OnlineStrings.Get( OnlineLabel.ChatReconnected ) ), seconds: 20 );
			Assert.IsTrue( session.IsLoggedIn, "the session survived the restart" );
			await PumpUntil( session, () => session.Chat != null && session.CurrentRoom == room );
		}
		finally
		{
			Directory.Delete( folders.Root, true );
			if ( Directory.Exists( directory ) )
				Directory.Delete( directory, true );
		}
	}

	private static async Task PumpUntil( OnlineSession session, Func<bool> done, int seconds = 10 )
	{
		var until = DateTime.UtcNow.AddSeconds( seconds );
		while ( !done() && DateTime.UtcNow < until )
		{
			session.Pump();
			await Task.Delay( 20 );
		}
		Assert.IsTrue( done(), session.Status );
	}

	[TestMethod]
	public void OriginalParkExportPreparesAReadOnlyVisitWithoutEconomy()
	{
		var data = OriginalParkImportTests.OriginalDataPath();
		var previous = OpenTPW.Common.GlobalNamespace.FileSystem;
		var fileSystem = new BaseFileSystem( data );
		fileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		OpenTPW.Common.GlobalNamespace.FileSystem = fileSystem;
		try
		{
			var park = OriginalPark.Load( "jungle" );
			var snapshot = ParkSnapshotBuilder.FromOriginal( park, null );
			var package = ParkSharing.CreatePackage( snapshot, "Original Jungle", "Read-only", "Alice", park.Map );
			var visit = ParkSharing.PrepareVisit( ParkPackage.Read( package.ToBytes() ) );
			Assert.IsTrue( visit.MatchesOriginalImport );
			Assert.IsFalse( visit.IsSandbox );
			Assert.IsNull( visit.Payload.Economy );
			Assert.AreEqual( park.Save!.PathCells.Count, visit.Payload.PathCells.Count );
			Assert.AreEqual( 0, visit.MissingContent.Count );
			CollectionAssert.Contains( package.Manifest.Flags.ToArray(), OpenTPW.Online.Packages.CompatibilityFlags.NoEconomy );
			CollectionAssert.Contains( package.Manifest.Flags.ToArray(), OpenTPW.Online.Packages.CompatibilityFlags.ReadOnlyVisit );
		}
		finally { OpenTPW.Common.GlobalNamespace.FileSystem = previous; }
	}

	[TestMethod]
	public void ChatHistoryAndSupplementaryTranslationsAreComplete()
	{
		using var session = new OnlineSession( Folders() );
		for ( var index = 0; index < OnlineSession.MaximumChatLines + 5; index++ )
			session.AddChatLine( index.ToString() );
		Assert.AreEqual( OnlineSession.MaximumChatLines, session.ChatLines.Count );
		Assert.AreEqual( "5", session.ChatLines[0] );
		foreach ( var language in GameLanguage.ShippedLanguages )
			foreach ( var label in Enum.GetValues<OnlineLabel>() )
				Assert.IsFalse( string.IsNullOrWhiteSpace( OnlineStrings.Supplementary[language][label] ) );
		session.Disconnect();
		Assert.AreEqual( 0, session.ChatLines.Count );
		Assert.AreEqual( ChatProtocol.LobbyRoom, session.CurrentRoom );
	}

	[TestMethod]
	public void ServerUrlsRejectCredentialsAndAmbiguousAddresses()
	{
		foreach ( var url in new[] { "file:///tmp/server", "https://user:pass@example.test", "https://example.test/?token=a", "https://example.test/#other" } )
			Assert.ThrowsException<ArgumentException>( () => OnlineClient.ValidateServerUrl( new Uri( url ) ) );
		Assert.AreEqual( "https://example.test/sub/", OnlineClient.ValidateServerUrl( new Uri( "https://example.test/sub" ) ).ToString() );
	}

	[TestMethod]
	public void PlainHttpIsOnlyForThisComputerAndTheLocalNetwork()
	{
		foreach ( var url in new[] { "http://localhost:8080", "http://127.0.0.1:5000", "http://[::1]:8080", "http://dev.localhost", "http://192.168.1.20",
			"http://10.0.0.5", "http://172.20.1.1", "http://[fd00::5]", "http://[fe80::1]" } )
			Assert.AreEqual( "http", OnlineClient.ValidateServerUrl( new Uri( url ) ).Scheme, url );
		foreach ( var url in new[] { "http://play.opentpw.io", "http://example.test", "http://8.8.8.8", "http://172.32.0.1", "http://[2001:db8::1]", "http://localhost.evil.test", "http://nas.local" } )
			Assert.ThrowsException<ArgumentException>( () => OnlineClient.ValidateServerUrl( new Uri( url ) ), url );
	}
}
