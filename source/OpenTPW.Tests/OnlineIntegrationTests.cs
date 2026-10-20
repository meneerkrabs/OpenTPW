using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Api;
using OpenTPW.Online.Chat;
using OpenTPW.Online.Client;
using OpenTPW.Online.Packages;
using OpenTPW.Server;

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
		return new ParkPresence( session, ChatProtocol.ParkRoom( parkId ), session.Settings.PlayerName!, new OpenGround(), start, () => clock.Elapsed.TotalSeconds );
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
			Assert.AreEqual( ServerProgram.ProtocolVersion, ann.ServerProtocol );
			var annSeen = bobVisit.State.Others.Single( other => other.Name == "Ann" );
			Assert.AreEqual( 10.5f, annSeen.X, 0.01f, "Bob sees Ann where she stands" );
			// Ann walks; Bob's view of her follows
			annVisit.Click( 15.5f, 10.5f );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => !annVisit.State.Walking && MathF.Abs( annSeen.X - 15.5f ) < 0.05f ),
				$"Bob's view of Ann did not follow her walk ({annSeen.X}, {annVisit.State.Own.X})" );
			// a line said in the park reaches the other visitor
			annVisit.Say( "hello park" );
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => bob.ChatLines.Any( line => line.Contains( "hello park" ) ) ) );
			// Ann rides a ride: her figure is gone from Bob's park until the ride ends, and is back at the ride's exit
			var riding = true;
			annVisit.Ride = () => riding ? new ParkPresence.PresenceRide( (25.5f, 12.5f) ) : null;
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => bobVisit.State.Others.Count == 0 ), "Bob still sees the riding Ann" );
			Assert.IsTrue( bob.RoomPlayers.Contains( "Ann" ), "she is still in the park" );
			riding = false;
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => bobVisit.State.Others.Any( other => other.Name == "Ann" ) ), "Ann never came back" );
			var annBack = bobVisit.State.Others.Single( other => other.Name == "Ann" );
			Assert.AreEqual( (25.5f, 12.5f), (MathF.Round( annBack.X, 2 ), MathF.Round( annBack.Y, 2 )), "at the ride's exit, at once" );
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
			// the buddy tab asks afresh, quietly too; a typed /buddy lists them in the chat lines, also when the buddy tab opens right after it (no
			// overlay ask within five seconds of the last one, which would keep the typed one's answer off the lines)
			ann.BuddyAskIntervalMilliseconds = 0;
			chat.AskBuddies();
			Assert.IsTrue( await Until( ann, annVisit, bob, bobVisit, () => chat.Buddies.Any( buddy => buddy.Name == "Bob" && buddy.Online ) ) );
			Assert.IsFalse( ann.ChatLines.Contains( BuddyLine( "Bob", true ) ) );
			ann.BuddyAskIntervalMilliseconds = 5000;
			chat.SendChatLine( "/buddy" );
			chat.AskBuddies();
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
			// the connection asked as it opened: let the ask out at once
			ann.BuddyAskIntervalMilliseconds = 0;
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

	/// <summary>Pumps <paramref name="session"/> and, when given, its visit, until <paramref name="condition"/> holds.</summary>
	private static async Task<bool> Until( OnlineSession session, Func<bool> condition, ParkPresence? visit = null )
	{
		var until = DateTime.UtcNow.AddSeconds( 15 );
		while ( DateTime.UtcNow < until )
		{
			session.Pump();
			visit?.Update( 0.25f );
			if ( condition() )
				return true;
			await Task.Delay( 20 );
		}
		session.Pump();
		visit?.Update( 0.25f );
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

	[TestMethod]
	public async Task AWalkKeepsAQueuedHideTeleportOrAppearance()
	{
		Assert.AreEqual( ChatMoveMode.Hide, OnlineSession.KeepPlaceMode( ChatMoveMode.Walk, ChatMoveMode.Hide ) );
		Assert.AreEqual( ChatMoveMode.Teleport, OnlineSession.KeepPlaceMode( ChatMoveMode.Walk, ChatMoveMode.Teleport ) );
		Assert.AreEqual( ChatMoveMode.Unhide, OnlineSession.KeepPlaceMode( ChatMoveMode.Walk, ChatMoveMode.Unhide ) );
		Assert.AreEqual( ChatMoveMode.Walk, OnlineSession.KeepPlaceMode( ChatMoveMode.Walk, ChatMoveMode.Walk ) );
		// a later mode replaces a hide: the ride ending is an appearance, not a walk
		Assert.AreEqual( ChatMoveMode.Unhide, OnlineSession.KeepPlaceMode( ChatMoveMode.Unhide, ChatMoveMode.Hide ) );
		Assert.AreEqual( ChatMoveMode.Teleport, OnlineSession.KeepPlaceMode( ChatMoveMode.Teleport, ChatMoveMode.Hide ) );

		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		var release = new TaskCompletionSource<int>( TaskCreationOptions.RunContinuationsAsynchronously );
		var (ann, http) = GatedSession( server.Url, "Ann", respond: async () => ServerInfoResponse( await release.Task ) );
		using var cancel = new CancellationTokenSource();
		var seen = new List<ChatEvent>();
		try
		{
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			ann.ConnectChat();
			await PumpUntil( ann, () => ann.Chat != null );
			Assert.IsFalse( ann.ProtocolCurrent, "the version read is still in flight" );

			using var bobClient = await server.LoginAsync( "Bob" );
			await using var bob = await bobClient.ConnectChatAsync();
			var reading = Collect( bob, seen, cancel.Token );
			var room = ChatProtocol.SatelliteRoom( 1 );
			ann.JoinRoom( room );
			await bob.JoinAsync( room );
			await bob.MoveAsync( 0.5f, 0.5f, ChatMoveMode.Walk );
			await PumpUntil( ann, () => ann.CurrentRoom == room );
			// the hide stays queued while this connection's version is unknown, and the walk keeps that mode at the new place
			ann.MoveTo( 0.25f, 0.75f, ChatMoveMode.Hide );
			ann.MoveTo( 0.5f, 0.25f );
			ann.JoinRoom( "invalid" ); // invalid rooms and the room already wanted must keep its waiting hide
			ann.JoinRoom( room );
			Assert.AreEqual( (0.5f, 0.25f), ann.RoomPositions["Ann"] );
			var held = DateTime.UtcNow.AddMilliseconds( 300 );
			while ( DateTime.UtcNow < held ) { ann.Pump(); await Task.Delay( 20 ); }
			Assert.AreEqual( 0, MovesOf( seen, "Ann" ).Count, "a walk replaced the queued hide with a place" );

			release.SetResult( 5 );
			await PumpUntil( ann, () => ann.ProtocolCurrent && ann.ServerProtocol == 5 );
			Assert.IsTrue( await Until( ann, () => MovesOf( seen, "Ann" ).Any( item => item.Mode == ChatMoveMode.Hide && item.X == 0.5f && item.Y == 0.25f ) ),
				"the queued hide was not sent at the walked place: " + string.Join( " | ", MovesOf( seen, "Ann" ).Select( item => $"{item.Mode} {item.X},{item.Y}" ) ) );
			cancel.Cancel();
			await reading;
		}
		finally
		{
			cancel.Cancel();
			release.TrySetResult( 5 );
			ann.Dispose();
			http.Dispose();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AHiddenRiderStaysHiddenAcrossAReconnect()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		var gate = new VersionGate();
		var (ann, http) = GatedSession( server.Url, "Ann", gate );
		using var cancel = new CancellationTokenSource();
		var daveSeen = new List<ChatEvent>();
		var carolSeen = new List<ChatEvent>();
		try
		{
			using var owner = await server.LoginAsync( "Owner" );
			var park = await owner.UploadParkAsync( OnlineFormatTests.Package() );
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			var visit = Visit( ann, park.Id, (10.5f, 10.5f) );
			using var daveClient = await server.LoginAsync( "Dave" );
			using var carolClient = await server.LoginAsync( "Carol" );
			await using var dave = await daveClient.ConnectChatAsync();
			await using var carol = await carolClient.ConnectChatAsync();
			var daveRead = Collect( dave, daveSeen, cancel.Token );
			var carolRead = Collect( carol, carolSeen, cancel.Token );
			await dave.JoinAsync( visit.Room );
			await carol.JoinAsync( visit.Room );
			// Dave reads modes; Carol is an older client and never sends one
			await dave.MoveAsync( 0.4f, 0.4f, ChatMoveMode.Walk );
			await carol.MoveAsync( 0.6f, 0.4f );
			Assert.IsTrue( await Until( ann, () => visit.InRoom && ann.ProtocolCurrent && MovesOf( daveSeen, "Ann" ).Count > 0, visit ),
				$"Ann never announced herself ({ann.Status}, room {ann.CurrentRoom}, protocol {ann.ServerProtocol}, current {ann.ProtocolCurrent})" );

			var riding = true;
			visit.Ride = () => riding ? new ParkPresence.PresenceRide( (40.5f, 20.5f) ) : null;
			Assert.IsTrue( await Until( ann, () => MovesOf( daveSeen, "Ann" ).Any( item => item.Mode == ChatMoveMode.Hide ), visit ),
				"Dave was not told that Ann is riding" );
			Assert.IsFalse( MovesOf( carolSeen, "Ann" ).Any( item => item.Mode == ChatMoveMode.Hide ), "an older client is not sent a mode" );

			lock ( daveSeen ) daveSeen.Clear();
			lock ( carolSeen ) carolSeen.Clear();
			// the reconnect's version read stays in flight, which is when a mode-less announce used to show the rider
			gate.HoldNext();
			AbortChat( ann );
			var sawUnknown = false;
			var until = DateTime.UtcNow.AddSeconds( 8 );
			while ( DateTime.UtcNow < until )
			{
				ann.Pump();
				visit.Update( 0.25f );
				if ( ann.Chat != null && !ann.ProtocolCurrent )
					sawUnknown = true;
				await Task.Delay( 20 );
			}
			Assert.IsTrue( sawUnknown, "the reconnected chat never sat with an unknown version" );
			Assert.AreEqual( 0, MovesOf( daveSeen, "Ann" ).Count, "a hide was sent before the version was known" );
			AssertNoRider( daveSeen, carolSeen, "while the version read was in flight" );

			gate.Release( 5 );
			Assert.IsTrue( await Until( ann, () => ann.ProtocolCurrent && MovesOf( daveSeen, "Ann" ).Any( item => item.Mode == ChatMoveMode.Hide ), visit ),
				$"the hide was not sent again once the version was known ({ann.ServerProtocol}, current {ann.ProtocolCurrent})" );
			AssertNoRider( daveSeen, carolSeen, "after the version was known" );

			riding = false;
			Assert.IsTrue( await Until( ann, () => MovesOf( daveSeen, "Ann" ).Any( item => item.Mode == ChatMoveMode.Unhide ), visit ), "Dave never saw Ann leave the ride" );
			Assert.IsTrue( await Until( ann, () => MovesOf( carolSeen, "Ann" ).Any( item => item.Mode == null ), visit ), "Carol never saw Ann reappear" );
			visit.Dispose();
			ann.Dispose();
			cancel.Cancel();
			await Task.WhenAll( daveRead, carolRead );
		}
		finally
		{
			cancel.Cancel();
			gate.Release( 5 );
			ann.Dispose();
			http.Dispose();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AReconnectingRidersWaitingHideDoesNotFollowTheSharedConnectionToAChatPark()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		var gate = new VersionGate();
		var (ann, http) = GatedSession( server.Url, "Ann", gate );
		using var cancel = new CancellationTokenSource();
		var seen = new List<ChatEvent>();
		try
		{
			using var owner = await server.LoginAsync( "Owner" );
			var park = await owner.UploadParkAsync( OnlineFormatTests.Package() );
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			// The online screens own this connection, so leaving the visit must keep it open.
			ann.ConnectChat();
			await PumpUntil( ann, () => ann.Chat != null && ann.ProtocolCurrent );
			using var visit = Visit( ann, park.Id, (10.5f, 10.5f) );
			visit.Ride = () => new ParkPresence.PresenceRide( (40.5f, 20.5f) );
			Assert.IsTrue( await Until( ann, () => visit.InRoom && ann.RoomPositions.ContainsKey( "Ann" ), visit ) );
			await PumpUntilIdle( ann );

			var previous = ann.Chat;
			gate.HoldNext();
			AbortChat( ann );
			Assert.IsTrue( await Until( ann, () => ann.Chat != null && ann.Chat != previous && !ann.ProtocolCurrent
				&& visit.InRoom && ann.RoomPositions.ContainsKey( "Ann" ), visit ), "the reconnect did not queue the rider's hide" );
			var shared = ann.Chat;
			visit.Dispose();
			Assert.AreSame( shared, ann.Chat, "leaving a visit keeps the online screens' connection" );
			Assert.AreEqual( ChatProtocol.LobbyRoom, ann.WantedRoom );

			using var bobClient = await server.LoginAsync( "Bob" );
			await using var bob = await bobClient.ConnectChatAsync();
			var reading = Collect( bob, seen, cancel.Token );
			var room = ChatProtocol.SatelliteRoom( 2 );
			await bob.JoinAsync( room );
			await bob.MoveAsync( 0.25f, 0.75f, ChatMoveMode.Walk );
			await bob.SendAsync( ChatCommand.Say, "observer ready" );
			await WaitUntil( () => HasLine( seen, "Bob", "observer ready" ) );
			ann.JoinRoom( room );
			await PumpUntil( ann, () => ann.CurrentRoom == room );
			// The chat park announces once, then stays stationary; there is no visit keyframe to repair a leaked hide.
			var view = new OpenTPW.UI.UiChatParkView( ann );
			view.Walk( OpenTPW.UI.UiChatParkView.SendInterval );
			Assert.IsTrue( await Until( ann, () => MovesOf( seen, "Ann" ).Count > 0 ), "the new room's walk inherited the old visit's waiting hide" );
			Assert.IsFalse( ann.ProtocolCurrent, "the version response must still be held" );
			gate.Release( 5 );
			await PumpUntil( ann, () => ann.ProtocolCurrent );
			await PumpUntilIdle( ann );
			ann.SendChatLine( "still standing" );
			await PumpUntil( ann, () => HasLine( seen, "Ann", "still standing" ) );
			var moves = MovesOf( seen, "Ann" );
			Assert.AreEqual( 1, moves.Count, "releasing the version must not send the previous room's hide" );
			Assert.AreEqual( (room, (float?)0.5f, (float?)0.5f, (ChatMoveMode?)null), (moves[0].Room, moves[0].X, moves[0].Y, moves[0].Mode) );
			Assert.AreSame( shared, ann.Chat );
			cancel.Cancel();
			await reading;
		}
		finally
		{
			cancel.Cancel();
			gate.Release( 5 );
			ann.Dispose();
			http.Dispose();
			server.DeleteData();
		}
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task ACoalescedPlaceDoesNotFollowALaterJoin( bool announceWhileJoining )
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		var (ann, http) = GatedSession( server.Url, "Ann", new VersionGate() );
		var release = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );
		using var cancel = new CancellationTokenSource();
		var seen = new List<ChatEvent>();
		var beforeJoin = new List<ChatEvent>();
		try
		{
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			ann.ConnectChat();
			await PumpUntil( ann, () => ann.Chat != null && ann.ProtocolCurrent );
			ann.JoinRoom( ChatProtocol.SatelliteRoom( 1 ) );
			await PumpUntil( ann, () => ann.CurrentRoom == ChatProtocol.SatelliteRoom( 1 ) );
			await PumpUntilIdle( ann );
			using var bobClient = await server.LoginAsync( "Bob" );
			await using var bob = await bobClient.ConnectChatAsync();
			var reading = Collect( bob, seen, cancel.Token );
			var room = ChatProtocol.SatelliteRoom( 2 );
			await bob.JoinAsync( room );
			await bob.MoveAsync( 0.5f, 0.5f, ChatMoveMode.Walk );
			await bob.SendAsync( ChatCommand.Say, "observer ready" );
			await WaitUntil( () => HasLine( seen, "Bob", "observer ready" ) );

			using var daveClient = await server.LoginAsync( "Dave" );
			await using var dave = await daveClient.ConnectChatAsync();
			var beforeRead = Collect( dave, beforeJoin, cancel.Token );
			await dave.JoinAsync( ChatProtocol.SatelliteRoom( 1 ) );
			await dave.MoveAsync( 0.5f, 0.5f, ChatMoveMode.Walk );
			await dave.SendAsync( ChatCommand.Say, "observer ready" );
			await WaitUntil( () => HasLine( beforeJoin, "Dave", "observer ready" ) );

			// Hold the send chain itself: A owns a slot, B is coalesced, and the join is queued after A.
			var sends = typeof( OnlineSession ).GetField( "chatSends", BindingFlags.Instance | BindingFlags.NonPublic )!;
			sends.SetValue( ann, release.Task );
			ann.MoveTo( 0.2f, 0.3f );
			ann.MoveTo( 0.3f, 0.4f, ChatMoveMode.Teleport );
			ann.JoinRoom( room );
			if ( announceWhileJoining )
			{
				// This belongs to the new room even before its roster confirms the join, and keeps its own mode.
				ann.MoveTo( 0.7f, 0.8f );
				ann.JoinRoom( room ); // retrying the same requested room must not discard its initial place
			}
			var joined = (Task)sends.GetValue( ann )!;
			release.SetResult( true );
			await joined.WaitAsync( TimeSpan.FromSeconds( 10 ) );
			// A's callback posted FlushMove; run it only after the join was sent, deterministically exposing the old ordering bug.
			ann.Pump();
			await PumpUntilIdle( ann );
			ann.SendChatLine( "after join" );
			await PumpUntil( ann, () => HasLine( seen, "Ann", "after join" ) );
			await WaitUntil( () => MovesOf( beforeJoin, "Ann" ).Count > 0 );
			var original = MovesOf( beforeJoin, "Ann" ).Single();
			Assert.AreEqual( (ChatProtocol.SatelliteRoom( 1 ), (float?)0.2f, (float?)0.3f), (original.Room, original.X, original.Y), "the frozen snapshot belongs before the join" );
			var moves = MovesOf( seen, "Ann" );
			Assert.AreEqual( announceWhileJoining ? 1 : 0, moves.Count, "a coalesced place from the previous room followed the join" );
			if ( announceWhileJoining )
				Assert.AreEqual( (room, (float?)0.7f, (float?)0.8f, (ChatMoveMode?)ChatMoveMode.Walk), (moves[0].Room, moves[0].X, moves[0].Y, moves[0].Mode) );
			cancel.Cancel();
			await Task.WhenAll( reading, beforeRead );
		}
		finally
		{
			release.TrySetResult( true );
			cancel.Cancel();
			ann.Dispose();
			http.Dispose();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ARefusedRoomsWaitingHideDoesNotApplyToTheRoomThatStayedOpen()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		var gate = new VersionGate();
		gate.HoldNext();
		var (ann, http) = GatedSession( server.Url, "Ann", gate );
		using var cancel = new CancellationTokenSource();
		var seen = new List<ChatEvent>();
		try
		{
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			ann.ConnectChat();
			await PumpUntil( ann, () => ann.Chat != null );
			using var bobClient = await server.LoginAsync( "Bob" );
			await using var bob = await bobClient.ConnectChatAsync();
			var reading = Collect( bob, seen, cancel.Token );
			var room = ChatProtocol.SatelliteRoom( 1 );
			await bob.JoinAsync( room );
			await bob.MoveAsync( 0.5f, 0.5f, ChatMoveMode.Walk );
			await bob.SendAsync( ChatCommand.Say, "observer ready" );
			await WaitUntil( () => HasLine( seen, "Bob", "observer ready" ) );
			ann.JoinRoom( room );
			await PumpUntil( ann, () => ann.CurrentRoom == room );
			var refused = ChatProtocol.ParkRoom( "missing" );
			ann.JoinRoom( refused );
			ann.MoveTo( 0.2f, 0.3f, ChatMoveMode.Hide );
			await PumpUntil( ann, () => ann.RefusedRoom == refused );
			Assert.AreEqual( room, ann.WantedRoom );
			Assert.IsFalse( ann.ProtocolCurrent );
			gate.Release( 5 );
			await PumpUntil( ann, () => ann.ProtocolCurrent );
			await PumpUntilIdle( ann );
			ann.SendChatLine( "still here" );
			await PumpUntil( ann, () => HasLine( seen, "Ann", "still here" ) );
			Assert.AreEqual( 0, MovesOf( seen, "Ann" ).Count, "the refused room's hide reached the original room" );
			ann.MoveTo( 0.6f, 0.7f );
			await PumpUntil( ann, () => MovesOf( seen, "Ann" ).Count > 0 );
			Assert.AreEqual( ChatMoveMode.Walk, MovesOf( seen, "Ann" ).Single().Mode );
			cancel.Cancel();
			await reading;
		}
		finally
		{
			cancel.Cancel();
			gate.Release( 5 );
			ann.Dispose();
			http.Dispose();
			server.DeleteData();
		}
	}

	private static bool HasLine( List<ChatEvent> seen, string name, string text )
	{
		lock ( seen )
			return seen.Any( item => item.Kind == ChatEventKind.Say && item.From == name && item.Text == text );
	}

	[TestMethod]
	public async Task AStaleServerVersionCannotReplaceTheOpenConnections()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		var calls = 0;
		var first = new TaskCompletionSource<int>( TaskCreationOptions.RunContinuationsAsynchronously );
		var second = new TaskCompletionSource<int>( TaskCreationOptions.RunContinuationsAsynchronously );
		var (ann, http) = GatedSession( server.Url, "Ann", respond: async () =>
		{
			var index = Interlocked.Increment( ref calls );
			var version = index == 1 ? await first.Task : await second.Task;
			return ServerInfoResponse( version );
		} );
		using var cancel = new CancellationTokenSource();
		var seen = new List<ChatEvent>();
		try
		{
			Assert.IsNull( ann.ServerProtocol );
			Assert.IsFalse( ann.ProtocolCurrent );
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			ann.FetchServerInfo();
			await WaitUntil( () => Volatile.Read( ref calls ) >= 1 );
			ann.ConnectChat();
			await PumpUntil( ann, () => ann.Chat != null && Volatile.Read( ref calls ) >= 2 );
			Assert.IsFalse( ann.ProtocolCurrent, "a connection's version is unknown until its own read returns" );
			Assert.IsNull( ann.ServerProtocol, "the read that started first is still in flight" );

			using var bobClient = await server.LoginAsync( "Bob" );
			await using var bob = await bobClient.ConnectChatAsync();
			var reading = Collect( bob, seen, cancel.Token );
			var room = ChatProtocol.SatelliteRoom( 1 );
			ann.JoinRoom( room );
			await bob.JoinAsync( room );
			await bob.MoveAsync( 0.5f, 0.5f, ChatMoveMode.Walk );
			await PumpUntil( ann, () => ann.CurrentRoom == room );
			// a walk still goes out while the version is unknown; a hide does not
			ann.MoveTo( 0.2f, 0.3f, ChatMoveMode.Walk );
			Assert.IsTrue( await Until( ann, () => MovesOf( seen, "Ann" ).Any( item => item.Mode == null ) ), "Bob never saw Ann's walk" );
			lock ( seen ) seen.Clear();
			ann.MoveTo( 0.2f, 0.3f, ChatMoveMode.Hide );
			var held = DateTime.UtcNow.AddMilliseconds( 300 );
			while ( DateTime.UtcNow < held ) { ann.Pump(); await Task.Delay( 20 ); }
			Assert.AreEqual( 0, MovesOf( seen, "Ann" ).Count, "a hide was sent before the version was known" );

			// the connection's own read says 4; the older read, which says 5, must not win when it arrives afterwards
			second.SetResult( 4 );
			await PumpUntil( ann, () => ann.ProtocolCurrent && ann.ServerProtocol == 4 );
			first.SetResult( 5 );
			var quiet = DateTime.UtcNow.AddMilliseconds( 400 );
			while ( DateTime.UtcNow < quiet ) { ann.Pump(); await Task.Delay( 20 ); }
			Assert.AreEqual( 4, ann.ServerProtocol, "a stale version replaced the open connection's" );
			Assert.IsTrue( ann.ProtocolCurrent );
			// known to be older than modes: the held hide goes out as a plain place
			Assert.IsTrue( await Until( ann, () => MovesOf( seen, "Ann" ).Count > 0 ), "the held hide was never sent" );
			Assert.IsNull( MovesOf( seen, "Ann" ).Single().Mode, "a server read as version 4 was sent a mode" );
			ann.Disconnect();
			Assert.IsNull( ann.ServerProtocol );
			Assert.IsFalse( ann.ProtocolCurrent, "disconnect forgets the version" );
			cancel.Cancel();
			await reading;
		}
		finally
		{
			cancel.Cancel();
			first.TrySetResult( 4 );
			second.TrySetResult( 4 );
			ann.Dispose();
			http.Dispose();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AFailedServerVersionReadIsRetriedAndThenHides()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		var calls = 0;
		var release = new TaskCompletionSource<int>( TaskCreationOptions.RunContinuationsAsynchronously );
		var (ann, http) = GatedSession( server.Url, "Ann", respond: async () =>
		{
			var index = Interlocked.Increment( ref calls );
			if ( index == 1 )
				return new HttpResponseMessage( HttpStatusCode.ServiceUnavailable );
			return ServerInfoResponse( await release.Task );
		} );
		using var cancel = new CancellationTokenSource();
		var seen = new List<ChatEvent>();
		try
		{
			ann.SetServerInfoRetryDelays( new[] { TimeSpan.FromMilliseconds( 40 ) } );
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			ann.ConnectChat();
			await PumpUntil( ann, () => ann.Chat != null && Volatile.Read( ref calls ) >= 1 );
			Assert.IsFalse( ann.ProtocolCurrent, "a failed read leaves modes off" );
			await PumpUntil( ann, () => Volatile.Read( ref calls ) >= 2 );
			Assert.IsFalse( ann.ProtocolCurrent, "the retry has not answered yet" );

			using var bobClient = await server.LoginAsync( "Bob" );
			await using var bob = await bobClient.ConnectChatAsync();
			var reading = Collect( bob, seen, cancel.Token );
			var room = ChatProtocol.SatelliteRoom( 1 );
			ann.JoinRoom( room );
			await bob.JoinAsync( room );
			await bob.MoveAsync( 0.5f, 0.5f, ChatMoveMode.Walk );
			await PumpUntil( ann, () => ann.CurrentRoom == room );
			ann.MoveTo( 0.25f, 0.75f, ChatMoveMode.Hide );
			var held = DateTime.UtcNow.AddMilliseconds( 250 );
			while ( DateTime.UtcNow < held ) { ann.Pump(); await Task.Delay( 20 ); }
			Assert.AreEqual( 0, MovesOf( seen, "Ann" ).Count, "a hide was sent while the version read was still failing" );

			release.SetResult( 5 );
			await PumpUntil( ann, () => ann.ProtocolCurrent && ann.ServerProtocol == 5 );
			Assert.IsTrue( await Until( ann, () => MovesOf( seen, "Ann" ).Any( item => item.Mode == ChatMoveMode.Hide ) ), "the hide was not sent once version 5 was known" );
			var settled = Volatile.Read( ref calls );
			await Task.Delay( 150 );
			ann.Pump();
			Assert.AreEqual( settled, Volatile.Read( ref calls ), "a version that arrived is not read again" );
			cancel.Cancel();
			await reading;
		}
		finally
		{
			cancel.Cancel();
			release.TrySetResult( 5 );
			ann.Dispose();
			http.Dispose();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task AVersionRetryDoesNotReplaceAReadStartedDuringTheWait()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		var calls = 0;
		var started = new TaskCompletionSource<int>( TaskCreationOptions.RunContinuationsAsynchronously );
		var (ann, http) = GatedSession( server.Url, "Ann", respond: async () =>
		{
			var index = Interlocked.Increment( ref calls );
			if ( index == 1 )
				return new HttpResponseMessage( HttpStatusCode.ServiceUnavailable );
			return ServerInfoResponse( await started.Task );
		} );
		try
		{
			ann.SetServerInfoRetryDelays( new[] { TimeSpan.FromMilliseconds( 1000 ) } );
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			ann.ConnectChat();
			var sawFailure = DateTime.UtcNow;
			await PumpUntil( ann, () => ann.Chat != null && Volatile.Read( ref calls ) >= 1 );
			sawFailure = DateTime.UtcNow;
			// the failed read clears its in-flight flag, then waits; a read started in that wait must stay the one that answers
			var secondBy = DateTime.UtcNow.AddMilliseconds( 700 );
			while ( Volatile.Read( ref calls ) < 2 && DateTime.UtcNow < secondBy )
			{
				ann.Pump();
				ann.FetchServerInfo();
				await Task.Delay( 15 );
			}
			Assert.AreEqual( 2, Volatile.Read( ref calls ), "the read during the backoff never started" );
			while ( DateTime.UtcNow < sawFailure.AddMilliseconds( 1600 ) )
			{
				ann.Pump();
				await Task.Delay( 20 );
			}
			ann.Pump();
			Assert.AreEqual( 2, Volatile.Read( ref calls ), "the backoff replaced a read that started during the wait" );
			started.SetResult( 5 );
			await PumpUntil( ann, () => ann.ProtocolCurrent && ann.ServerProtocol == 5 );
			Assert.AreEqual( 2, Volatile.Read( ref calls ), "the answered read was asked again" );
		}
		finally
		{
			started.TrySetResult( 5 );
			ann.Dispose();
			http.Dispose();
			server.DeleteData();
		}
	}

	[TestMethod]
	public async Task ClosingTheChatIgnoresTheVersionReadInFlight()
	{
		await using var server = await LoopbackServer.StartAsync( options => options.ChatBurst = 50 );
		var gates = new List<TaskCompletionSource<int>>();
		var (ann, http) = GatedSession( server.Url, "Ann", respond: async () =>
		{
			var gate = new TaskCompletionSource<int>( TaskCreationOptions.RunContinuationsAsynchronously );
			lock ( gates )
				gates.Add( gate );
			return ServerInfoResponse( await gate.Task );
		} );
		int Reads() { lock ( gates ) return gates.Count; }
		TaskCompletionSource<int> Gate( int index ) { lock ( gates ) return gates[index]; }
		try
		{
			ann.Register( "Ann", "correct horse" );
			await PumpUntilIdle( ann );
			ann.ConnectChat();
			await PumpUntil( ann, () => ann.Chat != null && Reads() >= 1 );
			ann.CloseChat();
			ann.Pump();
			Assert.IsNull( ann.Chat );
			Assert.IsFalse( ann.ProtocolCurrent );
			Assert.IsNull( ann.ServerProtocol );
			// the closed connection's read must not answer, and a new one can start (the in-flight flag was cleared)
			ann.FetchServerInfo();
			await WaitUntil( () => Reads() >= 2 );
			Gate( 0 ).SetResult( 4 );
			var quiet = DateTime.UtcNow.AddMilliseconds( 300 );
			while ( DateTime.UtcNow < quiet ) { ann.Pump(); await Task.Delay( 20 ); }
			Assert.IsNull( ann.ServerProtocol, "a version read for the closed chat answered the next one" );
			Assert.IsFalse( ann.ProtocolCurrent );
			Gate( 1 ).SetResult( 5 );
			await PumpUntil( ann, () => ann.ServerProtocol == 5 );
			Assert.IsFalse( ann.ProtocolCurrent, "a read with no open chat is not that connection's version" );

			ann.ConnectChat();
			await PumpUntil( ann, () => ann.Chat != null && Reads() >= 3 );
			Gate( 2 ).SetResult( 5 );
			await PumpUntil( ann, () => ann.ProtocolCurrent );
			ann.CloseChat();
			ann.Pump();
			Assert.IsFalse( ann.ProtocolCurrent, "a closed chat is not a connection whose version is known" );
			Assert.AreEqual( 5, ann.ServerProtocol, "closing the chat stays logged in" );
		}
		finally
		{
			lock ( gates )
				foreach ( var gate in gates )
					gate.TrySetResult( 5 );
			ann.Dispose();
			http.Dispose();
			server.DeleteData();
		}
	}

	private static async Task WaitUntil( Func<bool> done, int seconds = 10 )
	{
		var until = DateTime.UtcNow.AddSeconds( seconds );
		while ( !done() && DateTime.UtcNow < until )
			await Task.Delay( 20 );
		Assert.IsTrue( done() );
	}

	private static List<ChatEvent> MovesOf( List<ChatEvent> seen, string name )
	{
		lock ( seen )
			return seen.Where( item => item.Kind == ChatEventKind.Move && item.From == name ).ToList();
	}

	private static void AssertNoRider( List<ChatEvent> modes, List<ChatEvent> plain, string when )
	{
		var shown = MovesOf( modes, "Ann" ).Where( item => item.Mode != ChatMoveMode.Hide ).Select( item => item.Mode?.ToString() ?? "plain" ).ToArray();
		Assert.AreEqual( 0, shown.Length, $"a client that reads modes was shown the rider {when}: {string.Join( ", ", shown )}" );
		Assert.AreEqual( 0, MovesOf( plain, "Ann" ).Count, $"an older client was shown the rider {when}" );
	}

	private static void AbortChat( OnlineSession session )
	{
		var chat = session.Chat ?? throw new AssertFailedException( "No chat to drop." );
		var socket = (ClientWebSocket)typeof( ChatConnection ).GetField( "socket", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( chat )!;
		socket.Abort();
	}

	private static Task Collect( ChatConnection chat, List<ChatEvent> into, CancellationToken cancel ) => Task.Run( async () =>
	{
		try
		{
			while ( !cancel.IsCancellationRequested )
			{
				var item = await chat.ReceiveAsync( cancel );
				if ( item == null )
					break;
				lock ( into )
					into.Add( item );
			}
		}
		catch ( Exception exception ) when ( exception is OperationCanceledException or WebSocketException or ObjectDisposedException or InvalidDataException )
		{
		}
	}, cancel );

	private static (OnlineSession Session, HttpClient Http) GatedSession( Uri server, string name, VersionGate? gate = null, Func<Task<HttpResponseMessage>>? respond = null )
	{
		var handler = new ServerInfoHandler( new HttpClientHandler { AllowAutoRedirect = false }, respond ?? (gate ?? throw new ArgumentNullException( nameof( gate ) )).Next );
		var http = new HttpClient( handler ) { Timeout = TimeSpan.FromSeconds( 30 ) };
		var session = new OnlineSession( Folders() );
		session.UseClient( new OnlineClient( server, http ), name );
		return (session, http);
	}

	/// <summary>Answers <c>GET /api/v1/server</c> at once, or holds the next reads until <see cref="Release"/>.</summary>
	private sealed class VersionGate
	{
		private TaskCompletionSource<int>? hold;

		public void HoldNext() => Volatile.Write( ref hold, new TaskCompletionSource<int>( TaskCreationOptions.RunContinuationsAsynchronously ) );

		public void Release( int version ) => Volatile.Read( ref hold )?.TrySetResult( version );

		public async Task<HttpResponseMessage> Next()
		{
			var pending = Volatile.Read( ref hold );
			return ServerInfoResponse( pending == null ? 5 : await pending.Task );
		}
	}

	private sealed class ServerInfoHandler( HttpMessageHandler inner, Func<Task<HttpResponseMessage>> serverInfo ) : DelegatingHandler( inner )
	{
		protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken ) =>
			request.RequestUri?.AbsolutePath == ApiRoutes.Server ? serverInfo() : base.SendAsync( request, cancellationToken );
	}

	private static HttpResponseMessage ServerInfoResponse( int protocol )
	{
		var body = StrictJson.Serialize( new ServerInfo( "OpenTPW", "test", protocol, null, true, false, 1024, 1024, 8, 5 ) );
		var content = new ByteArrayContent( body );
		content.Headers.ContentType = new MediaTypeHeaderValue( "application/json" );
		return new HttpResponseMessage( HttpStatusCode.OK ) { Content = content };
	}
}
