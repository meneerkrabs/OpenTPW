using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Chat;

namespace OpenTPW.Tests;

[TestClass]
public class ParkPresenceTests
{
	private sealed class Ground( int width, int height ) : IPresenceGround
	{
		public int Width => width;
		public int Height => height;
		public bool IsPassable( int x, int y ) => true;
	}

	/// <summary>A session that records what the presence asks of it; the test plays the server.</summary>
	private sealed class FakeSession : IPresenceSession
	{
		public bool IsLoggedIn { get; set; } = true;
		public string? PlayerName { get; set; } = "Ann";
		public object? Connection { get; set; }
		public bool ChatBusy { get; set; }
		public bool ChatConnecting { get; set; }
		public string CurrentRoom { get; set; } = ChatProtocol.LobbyRoom;
		public string? Requested;
		public string WantedRoom => Requested ?? CurrentRoom;
		public string? RefusedRoom { get; set; }
		public int? ServerProtocol { get; set; } = ParkPresence.MovementProtocol;
		public int ServerInfoRequests;
		public int Closes;
		public Dictionary<string, (float X, float Y)> Positions { get; } = new( StringComparer.OrdinalIgnoreCase );
		public IReadOnlyDictionary<string, (float X, float Y)> RoomPositions => Positions;
		public Dictionary<string, int> Jumps { get; } = new( StringComparer.OrdinalIgnoreCase );
		public IReadOnlyDictionary<string, int> RoomJumps => Jumps;
		public List<string> Players { get; } = new();
		public IReadOnlyList<string> RoomPlayers => Players;
		public IReadOnlyList<string> ChatLines => Lines;
		public int Connects;
		public readonly List<string> Joins = new();
		public readonly List<(float X, float Y)> Moves = new();
		public readonly List<ChatMoveMode> MoveModes = new();
		public readonly List<string> Lines = new();
		public void ConnectChat() => Connects++;
		public void CloseChat() { Closes++; Connection = null; CurrentRoom = ChatProtocol.LobbyRoom; Requested = null; }
		public void FetchServerInfo() => ServerInfoRequests++;
		public void JoinRoom( string room ) { Joins.Add( room ); Requested = room; }
		public void MoveTo( float x, float y, ChatMoveMode mode = ChatMoveMode.Walk )
		{
			Moves.Add( (x, y) );
			MoveModes.Add( mode );
		}
		public void SendChatLine( string line ) => Lines.Add( line );
		public bool ChatOpen => Connection != null;
		public IReadOnlyList<(string Name, bool Online)> Buddies { get; set; } = Array.Empty<(string, bool)>();
		public HashSet<string> IgnoredNames { get; } = new( StringComparer.OrdinalIgnoreCase );
		public IReadOnlyCollection<string> Ignored => IgnoredNames;
		public bool Earmuffs { get; set; }
		public bool CanVote { get; set; } = true;
		public readonly List<(ChatCommand Command, string Argument)> Commands = new();
		public int BuddyRequests;
		public void SendCommand( ChatCommand command, string argument ) => Commands.Add( (command, argument) );
		public void AskBuddies() => BuddyRequests++;

		/// <summary>The server confirms the requested room on a connection (a reconnection returns to the room the session was in).</summary>
		public void Enter( object connection )
		{
			Connection = connection;
			CurrentRoom = Requested ?? CurrentRoom;
			Requested = null;
		}
	}

	private static (ParkPresence Presence, FakeSession Session, Func<double> Advance) Create( double step = 0.1, object? connection = null )
	{
		var session = new FakeSession { Connection = connection };
		var now = 0.0;
		var presence = new ParkPresence( session, ChatProtocol.ParkRoom( "park1" ), "Ann", new Ground( 100, 50 ), (10.5f, 5.5f), () => now );
		return (presence, session, () => now += step);
	}

	[TestMethod]
	public void AVisitJoinsThePublishedParksRoom()
	{
		var (presence, session, _) = Create();
		Assert.AreEqual( ChatProtocol.ParkRoom( "park1" ), presence.Room );
		CollectionAssert.AreEqual( new[] { "park:park1" }, session.Joins );
		Assert.IsFalse( presence.InRoom );
		Assert.IsFalse( presence.CanChat );
	}

	[TestMethod]
	public void TheConnectionIsOpenedAndRetriedWithGrowingWaits()
	{
		var (presence, session, advance) = Create();
		presence.Update( 0.1f );
		Assert.AreEqual( 1, session.Connects, "a visit without a connection opens one" );
		// the attempt failed (no connection, nothing in flight): the next one waits 2 s, then 4 s
		for ( var frame = 0; frame < 19; frame++ ) { advance(); presence.Update( 0.1f ); }
		Assert.AreEqual( 1, session.Connects );
		advance(); advance(); presence.Update( 0.1f );
		Assert.AreEqual( 2, session.Connects );
		for ( var frame = 0; frame < 38; frame++ ) { advance(); presence.Update( 0.1f ); }
		Assert.AreEqual( 2, session.Connects, "the second wait is twice as long" );
		for ( var frame = 0; frame < 3; frame++ ) { advance(); presence.Update( 0.1f ); }
		Assert.AreEqual( 3, session.Connects );
		// an attempt in flight (or a reconnection) is not doubled
		session.ChatBusy = true;
		for ( var frame = 0; frame < 200; frame++ ) { advance(); presence.Update( 0.1f ); }
		Assert.AreEqual( 3, session.Connects );
	}

	[TestMethod]
	public void TheFigureAnnouncesItselfInTheRoomAndOnEveryNewConnection()
	{
		var (presence, session, advance) = Create();
		presence.Update( 0.1f );
		Assert.AreEqual( 0, session.Moves.Count, "nothing is sent before the room is joined" );
		session.Enter( new object() );
		advance(); presence.Update( 0.1f );
		Assert.IsTrue( presence.InRoom && presence.CanChat );
		CollectionAssert.AreEqual( new[] { (0.105f, 0.11f) }, session.Moves, "the start place, as fractions of the park" );
		advance(); presence.Update( 0.1f );
		Assert.AreEqual( 1, session.Moves.Count, "a figure standing still sends nothing more" );
		// a reconnection lands in the room again on a new connection: the server forgot the place
		session.Enter( new object() );
		advance(); presence.Update( 0.1f );
		Assert.AreEqual( 2, session.Moves.Count );
		// so does leaving the room and coming back on the same connection
		session.CurrentRoom = ChatProtocol.LobbyRoom;
		advance(); presence.Update( 0.1f );
		session.CurrentRoom = presence.Room;
		advance(); presence.Update( 0.1f );
		Assert.AreEqual( 3, session.Moves.Count );
		// a click walks: places go out while walking
		presence.Click( 30.5f, 5.5f );
		for ( var frame = 0; frame < 30; frame++ ) { advance(); presence.Update( 0.1f ); }
		Assert.IsTrue( session.Moves.Count > 10, $"walking sends places ({session.Moves.Count})" );
	}

	[TestMethod]
	public void RidingARideHidesTheFigureFromTheRoomUntilTheRideEnds()
	{
		var (presence, session, advance) = Create();
		var riding = false;
		presence.Ride = () => riding ? new ParkPresence.PresenceRide( (30.5f, 5.5f) ) : null;
		session.Enter( new object() );
		advance(); presence.Update( 0.1f );
		CollectionAssert.AreEqual( new[] { ChatMoveMode.Teleport }, session.MoveModes, "the arrival puts the figure in the park (tp.exe 0x4E5EF0: mode 1)" );
		riding = true;
		advance(); presence.Update( 0.1f );
		Assert.AreEqual( ChatMoveMode.Hide, session.MoveModes[^1], "boarding hides the figure at once" );
		Assert.AreEqual( session.Moves[0], session.Moves[^1], "where it stood" );
		Assert.IsFalse( presence.State.Shown.Contains( presence.State.Own ) );
		presence.Click( 30.5f, 5.5f );
		for ( var frame = 0; frame < 20; frame++ ) { advance(); presence.Update( 0.1f ); }
		Assert.AreEqual( 2, session.Moves.Count, "a click does not walk the hidden figure" );
		riding = false;
		advance(); presence.Update( 0.1f );
		Assert.AreEqual( ChatMoveMode.Unhide, session.MoveModes[^1], "the ride's end shows it again" );
		Assert.AreEqual( (0.305f, 0.11f), session.Moves[^1], "at the ride's exit" );
		Assert.AreEqual( (30.5f, 5.5f), (presence.State.Own.X, presence.State.Own.Y) );
		Assert.IsTrue( presence.State.Shown.Contains( presence.State.Own ) );
	}

	[TestMethod]
	public void OthersAreDrawnWhereTheRoomPutsThemAndLinesAreSaidOnlyInTheRoom()
	{
		var (presence, session, advance) = Create();
		presence.Say( "too early" );
		session.Enter( new object() );
		session.Players.AddRange( new[] { "Ann", "Bob" } );
		session.Positions["Bob"] = (0.5f, 0.5f);
		session.Positions["Ann"] = (0.105f, 0.11f);
		advance(); presence.Update( 0.1f );
		Assert.AreEqual( "Bob", string.Join( ",", presence.State.Others.Select( other => other.Name ) ) );
		Assert.AreEqual( (50f, 25f), (presence.State.Others[0].X, presence.State.Others[0].Y) );
		presence.Say( "hello" );
		CollectionAssert.AreEqual( new[] { "hello" }, session.Lines );
		// outside the room the others are gone
		session.CurrentRoom = ChatProtocol.LobbyRoom;
		advance(); presence.Update( 0.1f );
		Assert.AreEqual( 0, presence.State.Others.Count );
	}

	[TestMethod]
	public void LeavingTheParkLeavesItsRoom()
	{
		// a connection the player had before the visit stays, back in the Online World
		var (presence, session, _) = Create( connection: new object() );
		session.Enter( session.Connection! );
		presence.Dispose();
		Assert.AreEqual( ChatProtocol.LobbyRoom, session.Joins[^1], "the other visitors see the parting" );
		Assert.AreEqual( 0, session.Closes );
		presence.Dispose();
		Assert.AreEqual( 2, session.Joins.Count, "once" );
		presence.Click( 1, 1 );
		presence.Say( "after" );
		Assert.AreEqual( 0, session.Lines.Count );
		// a session that is elsewhere already is left there
		var (other, otherSession, _) = Create( connection: new object() );
		otherSession.Enter( otherSession.Connection! );
		otherSession.CurrentRoom = ChatProtocol.SatelliteRoom( 1 );
		other.Dispose();
		Assert.AreEqual( "park:park1", otherSession.Joins[^1] );
		// a connection the visit opened is closed again, so the Online World's lines do not follow the player home
		var (opened, openedSession, _) = Create();
		openedSession.Enter( new object() );
		opened.Dispose();
		Assert.AreEqual( 1, openedSession.Closes );
		Assert.IsNull( openedSession.Connection );
		// a connection the online screens were still opening when the visit began is theirs: it stays open
		var openingSession = new FakeSession { ChatConnecting = true };
		var opening = new ParkPresence( openingSession, ChatProtocol.ParkRoom( "park1" ), "Ann", new Ground( 100, 50 ), (10.5f, 5.5f), () => 0 );
		openingSession.ChatConnecting = false;
		openingSession.Enter( new object() );
		opening.Dispose();
		Assert.AreEqual( 0, openingSession.Closes );
		Assert.IsNotNull( openingSession.Connection );
		// [EXT:online-coop-session] a connection the visit opened that a shared park's session took over since stays open in the session's room
		var (taken, takenSession, _) = Create();
		takenSession.Enter( new object() );
		var coop = ChatProtocol.CoopRoom( "0123456789abcdef" );
		takenSession.JoinRoom( coop );
		taken.Dispose();
		Assert.AreEqual( 0, takenSession.Closes );
		Assert.AreEqual( coop, takenSession.Joins[^1] );
		Assert.IsNotNull( takenSession.Connection );
	}

	[TestMethod]
	public void AServerWithoutParkMovementShowsAndSendsNoFigures()
	{
		var (presence, session, advance) = Create();
		session.ServerProtocol = null;
		Assert.AreEqual( 1, session.ServerInfoRequests, "the visit asks the server's version" );
		session.Enter( new object() );
		session.Players.AddRange( new[] { "Ann", "Bob" } );
		session.Positions["Bob"] = (0.5f, 0.5f);
		for ( var frame = 0; frame < 5; frame++ ) { advance(); presence.Update( 0.1f ); }
		Assert.AreEqual( 0, session.Moves.Count, "an unknown server gets no places" );
		Assert.AreEqual( 0, presence.State.Others.Count );
		Assert.IsTrue( presence.CanChat, "chat works on any server" );
		session.ServerProtocol = 3;
		for ( var frame = 0; frame < 5; frame++ ) { advance(); presence.Update( 0.1f ); }
		Assert.AreEqual( 0, session.Moves.Count, "a server from before park movement gets no places" );
		session.ServerProtocol = ParkPresence.MovementProtocol;
		advance(); presence.Update( 0.1f );
		Assert.AreEqual( 1, session.Moves.Count );
		Assert.AreEqual( 1, presence.State.Others.Count );
	}

	[TestMethod]
	public void ARefusedParkIsNotAskedForAgain()
	{
		var (presence, session, advance) = Create();
		session.Enter( new object() );
		// the park was unpublished: the server refused its room
		session.CurrentRoom = ChatProtocol.LobbyRoom;
		session.Requested = null;
		session.RefusedRoom = presence.Room;
		session.Joins.Clear();
		for ( var frame = 0; frame < 300; frame++ ) { advance(); presence.Update( 0.1f ); }
		Assert.IsTrue( presence.Refused );
		Assert.AreEqual( 0, session.Joins.Count );
	}

	[TestMethod]
	public void AStandingFigureSendsItsPlaceNowAndThen()
	{
		// a lost arrival, or a server that answered no first movement, is repaired within a keyframe
		var (presence, session, advance) = Create();
		session.Enter( new object() );
		advance(); presence.Update( 0.1f );
		Assert.AreEqual( 1, session.Moves.Count );
		for ( var frame = 0; frame < 95; frame++ ) { advance(); presence.Update( 0.1f ); }
		Assert.AreEqual( 1, session.Moves.Count );
		for ( var frame = 0; frame < 10; frame++ ) { advance(); presence.Update( 0.1f ); }
		Assert.AreEqual( 2, session.Moves.Count, $"one keyframe after {ParkPresenceState.KeyframeSeconds} s" );
	}
}
