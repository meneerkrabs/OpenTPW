using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Chat;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-figures] A shared park's presence (C2d-4; docs/COOP.md, "Co-builders as figures"): the online session owns the room and the chat, places go out only once the
/// shared room is confirmed on the connection and the session runs, the player's figure follows the view, and a co-builder's edit reroutes a walk.
/// </summary>
public partial class ParkPresenceTests
{
	private static readonly string SharedRoom = ChatProtocol.CoopRoom( "0123456789abcdef" );

	/// <summary>A ground whose cells a co-builder blocks and frees while the figures walk, with the version a cell map keeps; it counts the cells it was asked about.</summary>
	private sealed class EditedGround( int width, int height ) : IPresenceGround
	{
		private readonly HashSet<(int, int)> blocked = new();
		public int Width => width;
		public int Height => height;
		public int Version { get; private set; }
		public int Checks { get; private set; }
		public bool IsPassable( int x, int y )
		{
			Checks++;
			return !blocked.Contains( (x, y) );
		}
		public bool Blocked( int x, int y ) => blocked.Contains( (x, y) );
		public void Block( IEnumerable<(int, int)> cells )
		{
			blocked.UnionWith( cells );
			Version++;
		}
		public void Free( (int, int) cell )
		{
			blocked.Remove( cell );
			Version++;
		}
	}

	/// <summary>A shared park's presence on a fake session in the shared room, the view's centre and whether the session runs being the test's.</summary>
	private sealed class SharedPark
	{
		public FakeSession Session { get; } = new()
		{
			InSharedPark = true,
			SharedRoomReady = true,
			CurrentRoom = SharedRoom,
			Connection = new object(),
			ServerProtocol = ChatProtocol.CoopRoomsVersion,
		};
		public double Now { get; private set; }
		public (float X, float Y)? Centre { get; set; } = (10.5f, 5.5f);
		public bool Running { get; set; } = true;
		public bool Riding { get; set; }
		public bool Walking { get; set; }
		public ParkPresence Presence { get; }

		public SharedPark( IPresenceGround? ground = null, Action<FakeSession>? prepare = null, (float X, float Y)? start = null )
		{
			prepare?.Invoke( Session );
			Centre = start ?? Centre;
			Presence = new ParkPresence( Session, SharedRoom, "Ann", ground ?? new Ground( 100, 50 ), start ?? (10.5f, 5.5f), () => Now, sharedPark: true )
			{
				View = () => Centre,
				Running = () => Running,
				Ride = () => Riding ? new ParkPresence.PresenceRide( (30.5f, 5.5f) ) : null,
				FirstPerson = () => Walking,
			};
		}

		public void Frames( int count = 1, float delta = 0.1f )
		{
			for ( var frame = 0; frame < count; frame++ )
			{
				Now += delta;
				Presence.Update( delta );
			}
		}

		public (float X, float Y) Own => (Presence.State.Own.X, Presence.State.Own.Y);

		/// <summary>Whether the own figure stands at <paramref name="place"/> (a walk ends there within float rounding).</summary>
		public bool At( (float X, float Y) place ) => MathF.Abs( Own.X - place.X ) < 1e-3f && MathF.Abs( Own.Y - place.Y ) < 1e-3f;
	}

	[TestMethod]
	public void ASharedParksPresenceLeavesTheRoomAndTheChatToTheOnlineSession()
	{
		var park = new SharedPark( prepare: session => { session.Connection = null; session.CurrentRoom = ChatProtocol.LobbyRoom; session.SharedRoomReady = false; } );
		var session = park.Session;
		Assert.IsTrue( park.Presence.SharedPark );
		Assert.AreEqual( 0, session.Joins.Count, "the session's chat follows the shared park's room itself" );
		Assert.AreEqual( 0, session.ServerInfoRequests );
		park.Frames( 400 );
		Assert.AreEqual( 0, session.Connects, "the session keeps its chat connected" );
		Assert.AreEqual( 0, session.ServerInfoRequests );
		// a click is the park's, never a walk; a goto is the HUD's view, never a teleport
		session.Connection = new object();
		session.CurrentRoom = SharedRoom;
		session.SharedRoomReady = true;
		session.Players.AddRange( new[] { "Ann", "Bob" } );
		session.Positions["Bob"] = (0.5f, 0.5f);
		park.Frames();
		var at = park.Own;
		park.Presence.Click( 30.5f, 5.5f );
		park.Presence.Click( 30.5f, 5.5f );
		park.Presence.GoTo( "Bob" );
		park.Frames( 10 );
		Assert.AreEqual( at, park.Own );
		Assert.IsFalse( park.Presence.State.Walking );
		// leaving the park leaves the room and the chat as they are, whatever the session says of its shared park by then (it may have left it first)
		session.InSharedPark = false;
		park.Presence.Dispose();
		Assert.AreEqual( 0, session.Joins.Count );
		Assert.AreEqual( 0, session.Closes );
		// no sparkle: the park's particle pool is captured with it
		Assert.IsFalse( park.Presence.SparkleAllowed );
		Assert.IsTrue( Create().Presence.SparkleAllowed, "a visit's figures sparkle" );
	}

	[TestMethod]
	public void PlacesGoOutOnlyInTheConfirmedSharedRoomAndAgainOnEveryNewConnection()
	{
		var park = new SharedPark( prepare: session => session.SharedRoomReady = false );
		var session = park.Session;
		session.Players.AddRange( new[] { "Ann", "Bob" } );
		session.Positions["Bob"] = (0.5f, 0.5f);
		// the roster names the room, but the join on this connection is not confirmed yet: a place now could reach the room the server still has the connection in
		park.Frames( 5 );
		Assert.IsFalse( park.Presence.InRoom );
		Assert.IsFalse( park.Presence.CanChat );
		Assert.AreEqual( 0, session.Moves.Count );
		Assert.AreEqual( 0, park.Presence.State.Others.Count );
		// confirmed: the figure arrives (a teleport, which the server answers with the others' places), and the others are drawn
		session.SharedRoomReady = true;
		park.Frames();
		Assert.IsTrue( park.Presence.InRoom && park.Presence.CanChat );
		CollectionAssert.AreEqual( new[] { ChatMoveMode.Teleport }, session.MoveModes );
		Assert.AreEqual( "Bob", string.Join( ",", park.Presence.State.Others.Select( other => other.Name ) ) );
		// a reconnection: nothing goes out and nobody is drawn until the new connection's join is confirmed, which announces the figure again
		session.Connection = new object();
		session.SharedRoomReady = false;
		park.Frames( 5 );
		Assert.AreEqual( 1, session.Moves.Count );
		Assert.AreEqual( 0, park.Presence.State.Others.Count );
		session.SharedRoomReady = true;
		park.Frames();
		Assert.AreEqual( 2, session.Moves.Count, "the new connection's first place" );
		Assert.AreEqual( 1, park.Presence.State.Others.Count );
		// a server that went back to a version without shared parks' rooms: no figures, no places, though it relays a visit's
		session.ServerProtocol = ChatProtocol.CoopRoomsVersion - 1;
		Assert.IsTrue( session.ServerProtocol >= ParkPresence.MovementProtocol );
		Assert.IsFalse( park.Presence.ServerWalks );
		park.Centre = (14.5f, 5.5f);
		park.Frames( 200 );
		Assert.AreEqual( 2, session.Moves.Count );
		Assert.AreEqual( 0, park.Presence.State.Others.Count );
		Assert.AreEqual( (10.5f, 5.5f), park.Own, "the figure waits for a server that shows it" );
	}

	[TestMethod]
	public void ASessionThatEndedSendsNothingMore()
	{
		var park = new SharedPark();
		var session = park.Session;
		session.Players.AddRange( new[] { "Ann", "Bob" } );
		session.Positions["Bob"] = (0.5f, 0.5f);
		park.Frames();
		Assert.AreEqual( 1, session.Moves.Count );
		// the session ends during the level's update, before the game flow looks again: the figure stops where it is, nobody is drawn, and not even a keyframe goes out
		park.Running = false;
		park.Centre = (14.5f, 5.5f);
		park.Frames( (int)(ParkPresenceState.KeyframeSeconds * 10) + 20 );
		Assert.AreEqual( 1, session.Moves.Count );
		Assert.AreEqual( 0, park.Presence.State.Others.Count );
		Assert.AreEqual( (10.5f, 5.5f), park.Own );
	}

	[TestMethod]
	public void TheFigureWalksAfterTheViewAndJumpsWithIt()
	{
		var park = new SharedPark();
		var state = park.Presence.State;
		park.Frames();
		Assert.IsFalse( state.Walking, "the view is centred where the figure stands" );
		// the view moves on: the figure walks after it
		park.Centre = (14.5f, 5.5f);
		park.Frames( 1, 0.3f );
		Assert.IsTrue( state.Walking );
		Assert.AreEqual( (14.5f, 5.5f), state.Own.Target );
		// a centre less than a cell from the one followed plans nothing
		park.Centre = (15.2f, 5.5f);
		park.Frames( 5 );
		Assert.AreEqual( (14.5f, 5.5f), state.Own.Target );
		// a centre a cell away is followed, but not sooner than a quarter of a second after the last plan
		park.Centre = (16.5f, 5.5f);
		park.Frames( 1, 0.01f );
		Assert.AreEqual( (16.5f, 5.5f), state.Own.Target );
		park.Centre = (18.5f, 5.5f);
		park.Frames( 1, 0.01f );
		Assert.AreEqual( (16.5f, 5.5f), state.Own.Target, "a panning view is not planned every frame" );
		park.Frames( 30, 0.01f );
		Assert.AreEqual( (18.5f, 5.5f), state.Own.Target );
		park.Frames( 100 );
		Assert.IsFalse( state.Walking );
		Assert.IsTrue( park.At( (18.5f, 5.5f) ), $"the figure walked to the view, at {park.Own}" );
		// the view jumps (a locate, the map): the figure is there at once, and its place goes out as a teleport
		park.Centre = (60.5f, 30.5f);
		park.Frames();
		Assert.AreEqual( (60.5f, 30.5f), park.Own );
		Assert.AreEqual( ChatMoveMode.Teleport, park.Session.MoveModes[^1] );
		// no view (the globe): the figure stays
		park.Centre = null;
		park.Frames( 20 );
		Assert.AreEqual( (60.5f, 30.5f), park.Own );
		// while the player rides, the view does not move the hidden figure
		park.Centre = (60.5f, 30.5f);
		park.Riding = true;
		park.Frames();
		Assert.AreEqual( ChatMoveMode.Hide, park.Session.MoveModes[^1] );
		park.Centre = (64.5f, 30.5f);
		park.Frames( 20 );
		Assert.AreEqual( (60.5f, 30.5f), park.Own );
		// the ride ends at its exit (30.5, 5.5), far from the view: the figure shows again and goes back to the view at once, an appearance for the others
		park.Riding = false;
		park.Frames();
		Assert.AreEqual( (64.5f, 30.5f), park.Own );
		Assert.AreEqual( ChatMoveMode.Unhide, park.Session.MoveModes[^1] );
		Assert.IsFalse( park.Session.MoveModes.Skip( park.Session.MoveModes.LastIndexOf( ChatMoveMode.Hide ) + 1 ).Contains( ChatMoveMode.Teleport ), "no teleport hides the appearance" );
		// a ride whose exit is near the view: the figure walks back to it
		park.Centre = (34.5f, 5.5f);
		park.Frames();
		Assert.AreEqual( (34.5f, 5.5f), park.Own );
		park.Riding = true;
		park.Frames( 1, 0.3f );
		park.Riding = false;
		park.Frames();
		Assert.AreEqual( ChatMoveMode.Unhide, park.Session.MoveModes[^1] );
		Assert.IsTrue( state.Walking, "from the exit back to the view, which did not move" );
		Assert.AreEqual( (34.5f, 5.5f), state.Own.Target );
	}

	[TestMethod]
	public void TheFreeWalksOwnFigureIsNotDrawnOnItsOwnScreenButStillWalksForTheOthers()
	{
		var park = new SharedPark();
		park.Session.Players.AddRange( new[] { "Ann", "Bob" } );
		park.Session.Positions["Bob"] = (0.5f, 0.5f);
		park.Frames();
		CollectionAssert.AreEqual( new[] { "Ann", "Bob" }, park.Presence.Drawn.Select( avatar => avatar.Name ).ToArray() );
		// the player walks through the park in the first person: the figure follows the walk's place, not drawn at the player's own eyes
		park.Walking = true;
		park.Centre = (14.5f, 5.5f);
		var moves = park.Session.Moves.Count;
		park.Frames( 20 );
		CollectionAssert.AreEqual( new[] { "Bob" }, park.Presence.Drawn.Select( avatar => avatar.Name ).ToArray() );
		Assert.IsTrue( park.Presence.State.Shown.Contains( park.Presence.State.Own ), "the others still see it" );
		Assert.IsTrue( park.Session.Moves.Count > moves, "its walk still goes out" );
		Assert.IsFalse( park.Session.MoveModes.Contains( ChatMoveMode.Hide ), "a walk hides nothing from the others" );
		park.Walking = false;
		park.Frames();
		Assert.AreEqual( 2, park.Presence.Drawn.Count() );
	}

	[TestMethod]
	public void AViewOverWaterIsWalkedToTheNearestFreeCellOnce()
	{
		var ground = new EditedGround( 40, 20 );
		// a lake from column 20 to 29, across the park
		ground.Block( from x in Enumerable.Range( 20, 10 ) from y in Enumerable.Range( 0, 20 ) select (x, y) );
		// a figure that enters where the view looks stands on the free cell nearest it, and nowhere in the lake's middle (it enters at the park's entrance then)
		Assert.AreEqual( (10.25f, 5.75f), ParkPresenceState.StandingNear( ground, (10.25f, 5.75f) ) );
		Assert.AreEqual( (19.5f, 5.5f), ParkPresenceState.StandingNear( ground, (21.5f, 5.5f) ) );
		Assert.IsNull( ParkPresenceState.StandingNear( ground, (25.5f, 5.5f) ) );
		var park = new SharedPark( ground );
		park.Frames();
		// the view over the lake's shore: the figure walks to the free cell nearest the centre
		park.Centre = (21.5f, 5.5f);
		park.Frames( 1, 0.3f );
		Assert.AreEqual( (19.5f, 5.5f), park.Presence.State.Own.Target );
		var checks = ground.Checks;
		park.Frames( 100 );
		Assert.AreEqual( checks, ground.Checks, "a view that stays over the lake plans nothing more" );
		Assert.IsTrue( park.At( (19.5f, 5.5f) ), $"at {park.Own}" );
		// over the lake's middle, with no free cell near: the figure stays where it stands, also when the view jumps there
		var moves = park.Session.Moves.Count;
		park.Centre = (25.5f, 5.5f);
		park.Frames( 1, 0.3f );
		park.Centre = (25.5f, 18.5f);
		park.Frames( 1, 0.3f );
		checks = ground.Checks;
		park.Frames( 50 );
		Assert.AreEqual( checks, ground.Checks );
		Assert.IsTrue( park.At( (19.5f, 5.5f) ), $"no figure on the water, at {park.Own}" );
		Assert.AreEqual( moves, park.Session.Moves.Count );
		// a jump near the far shore lands the figure on its nearest free cell
		park.Centre = (28.5f, 18.5f);
		park.Frames();
		Assert.AreEqual( (30.5f, 18.5f), park.Own );
		Assert.AreEqual( ChatMoveMode.Teleport, park.Session.MoveModes[^1] );
		// over the lake's middle again: the figure stays; once a co-builder drains that spot, the view that stayed there is followed again
		park.Centre = (25.5f, 10.5f);
		park.Frames( 1, 0.3f );
		Assert.AreEqual( (30.5f, 18.5f), park.Presence.State.Own.Target );
		park.Frames( 1, 0.3f );
		Assert.AreEqual( (30.5f, 18.5f), park.Presence.State.Own.Target );
		ground.Free( (25, 10) );
		park.Frames( 1, 0.3f );
		Assert.AreEqual( (25.5f, 10.5f), park.Presence.State.Own.Target, "the drained spot is followed" );
	}

	[TestMethod]
	public void AWalkStopsInFrontOfACellBuiltOnUntilItIsPlannedAgain()
	{
		var ground = new EditedGround( 30, 12 );
		var park = new SharedPark( ground, start: (2.5f, 5.5f) );
		park.Frames( 1, 0.3f );
		park.Centre = (12.5f, 5.5f);
		park.Frames( 1, 0.3f );
		Assert.IsTrue( park.Presence.State.Walking );
		while ( park.Own.X < 5.8f )
			park.Frames( 1, 0.05f );
		// an edit elsewhere: the walk is planned again at once (its last plan was long ago), and the next plan waits a quarter of a second
		ground.Free( (29, 0) );
		park.Frames( 1, 0.05f );
		// within that quarter second a co-builder builds on the route's next cell: the figure stops in front of it, never on it
		ground.Block( new[] { (6, 5) } );
		for ( var frame = 0; frame < 3; frame++ )
		{
			park.Frames( 1, 0.05f );
			Assert.IsFalse( ground.Blocked( (int)MathF.Floor( park.Own.X ), (int)MathF.Floor( park.Own.Y ) ), $"the figure stands on the new cell at {park.Own}" );
		}
		Assert.IsFalse( park.Presence.State.Walking, "it waits in front of the new cell" );
		Assert.IsTrue( park.Own.X < 6f );
		// then its walk is planned again, round the cell
		for ( var frame = 0; frame < 400 && (park.Presence.State.Walking || !park.At( (12.5f, 5.5f) )); frame++ )
		{
			park.Frames();
			Assert.IsFalse( ground.Blocked( (int)MathF.Floor( park.Own.X ), (int)MathF.Floor( park.Own.Y ) ), $"the figure stands on the new cell at {park.Own}" );
		}
		Assert.IsTrue( park.At( (12.5f, 5.5f) ), $"at {park.Own}" );
	}

	[TestMethod]
	public void AWalkGoesRoundWhatACoBuilderBuildsAndThroughWhatTheyOpen()
	{
		var ground = new EditedGround( 30, 12 );
		// a fence across the park (column 7): the view's centre beyond it cannot be reached, so the figure walks straight at it
		ground.Block( Enumerable.Range( 0, 12 ).Select( y => (7, y) ) );
		var park = new SharedPark( ground, start: (2.5f, 5.5f) );
		park.Frames( 1, 0.3f );
		park.Centre = (12.5f, 5.5f);
		park.Frames( 1, 0.3f );
		Assert.IsTrue( park.Presence.State.Walking );
		void WalkOn()
		{
			for ( var frame = 0; frame < 400 && park.Presence.State.Walking; frame++ )
			{
				park.Frames();
				Assert.IsFalse( ground.Blocked( (int)MathF.Floor( park.Own.X ), (int)MathF.Floor( park.Own.Y ) ), $"the figure stands on the fence at {park.Own}" );
			}
		}
		// a co-builder opens a gate in the fence: the walk is planned again on the new ground (its areas labelled again) and goes through the gate
		ground.Free( (7, 11) );
		WalkOn();
		Assert.IsTrue( park.At( (12.5f, 5.5f) ), $"at {park.Own}" );
		// on the way back the co-builder closes that gate and opens another: the walk goes round again
		park.Centre = (3.5f, 5.5f);
		park.Frames( 1, 0.3f );
		Assert.IsTrue( park.Presence.State.Walking );
		ground.Block( new[] { (7, 11) } );
		ground.Free( (7, 0) );
		WalkOn();
		Assert.IsTrue( park.At( (3.5f, 5.5f) ), $"at {park.Own}" );
		// a co-builder who lays a path cell by cell changes the ground every frame: the walk is planned again at most every quarter of a second
		park.Centre = (12.5f, 5.5f);
		park.Frames( 1, 0.3f );
		park.Frames( 1, 0.3f );
		// the first edit is gone round at once
		ground.Free( (29, 0) );
		park.Frames( 1, 0.01f );
		var checks = ground.Checks;
		for ( var frame = 1; frame < 20; frame++ )
		{
			ground.Free( (29, frame % 12) );
			park.Frames( 1, 0.01f );
		}
		Assert.IsTrue( park.Presence.State.Walking );
		Assert.AreEqual( checks, ground.Checks, "no plan within a fifth of a second of the last" );
		ground.Free( (29, 0) );
		park.Frames( 1, 0.1f );
		Assert.IsTrue( ground.Checks > checks, "the walk is planned again once a quarter of a second passed" );
	}
}
