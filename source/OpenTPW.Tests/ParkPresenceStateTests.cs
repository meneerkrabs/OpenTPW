using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Chat;

namespace OpenTPW.Tests;

[TestClass]
public class ParkPresenceStateTests
{
	/// <summary>A test ground: every cell passable but the ones listed.</summary>
	private sealed class Ground : IPresenceGround
	{
		private readonly HashSet<(int, int)> blocked;
		public Ground( int width, int height, IEnumerable<(int, int)>? blocked = null )
		{
			Width = width;
			Height = height;
			this.blocked = new HashSet<(int, int)>( blocked ?? Array.Empty<(int, int)>() );
		}
		public int Width { get; }
		public int Height { get; }
		public bool IsPassable( int x, int y ) => !blocked.Contains( (x, y) );
	}

	/// <summary>A wall down column 5 of a 10 x 10 park with one gap at row <paramref name="gap"/> (null: no gap).</summary>
	private static Ground Wall( int? gap ) =>
		new( 10, 10, Enumerable.Range( 0, 10 ).Where( y => y != gap ).Select( y => (5, y) ) );

	[TestMethod]
	public void RelayedPlacesAreFractionsOfTheParkMap()
	{
		var state = new ParkPresenceState( new Ground( 128, 64 ), "Ann", (1, 1) );
		Assert.AreEqual( (64f, 16f), state.FromWire( (0.5f, 0.25f) ) );
		Assert.AreEqual( (0.5f, 0.25f), state.ToWire( 64, 16 ) );
		// out-of-range values are clamped to the park on both ways
		Assert.AreEqual( (128f, 0f), state.FromWire( (2f, -1f) ) );
		Assert.AreEqual( (1f, 0f), state.ToWire( 500, -3 ) );
	}

	[TestMethod]
	public void TheRouteGoesThroughTheGapWithoutCuttingCorners()
	{
		var ground = Wall( gap: 7 );
		var route = ParkPresenceState.Route( ground, (2, 2), (8, 2) );
		Assert.IsNotNull( route );
		Assert.AreEqual( (2, 2), route![0] );
		Assert.AreEqual( (8, 2), route[^1] );
		Assert.IsTrue( route.Contains( (5, 7) ), "the route crosses the wall at its gap: " + string.Join( " ", route ) );
		Assert.IsTrue( route.All( cell => ground.IsPassable( cell.X, cell.Y ) ), "every cell of the route is passable" );
		for ( var index = 1; index < route.Count; index++ )
		{
			var (px, py) = route[index - 1];
			var (cx, cy) = route[index];
			Assert.IsTrue( Math.Abs( cx - px ) <= 1 && Math.Abs( cy - py ) <= 1, "neighbouring cells" );
			if ( cx != px && cy != py )
				Assert.IsTrue( ground.IsPassable( cx, py ) && ground.IsPassable( px, cy ), "a diagonal step has both side cells free" );
		}
		Assert.IsNull( ParkPresenceState.Route( Wall( gap: null ), (2, 2), (8, 2) ), "no gap, no route" );
		Assert.IsNull( ParkPresenceState.Route( ground, (2, 2), (5, 2) ), "a blocked goal has no route" );
	}

	/// <summary>A pocket of free cells walled in on all sides round <paramref name="centre"/> (radius 1 inside, the wall at radius 2).</summary>
	private static IEnumerable<(int, int)> Pocket( (int X, int Y) centre ) =>
		from y in Enumerable.Range( centre.Y - 2, 5 )
		from x in Enumerable.Range( centre.X - 2, 5 )
		where Math.Max( Math.Abs( x - centre.X ), Math.Abs( y - centre.Y ) ) == 2
		select (x, y);

	[TestMethod]
	public void TheRouteSearchOfTheLargestParkAnswersUnreachableSpotsAtOnceAndFindsLongDetours()
	{
		// the largest park map: 256 x 256 cells
		var open = new Ground( 256, 256 );
		var search = new ParkPresenceState.RouteSearch( 256, 256 );
		var across = search.Find( open, (0, 0), (255, 255) );
		Assert.AreEqual( 256, across!.Count, "the diagonal across the whole park" );
		Assert.IsTrue( search.Expanded < 2000, $"the heuristic leads straight there ({search.Expanded} cells)" );
		// a spot walled in, near or far: the ground's areas answer at once, without flooding the park
		var walled = new Ground( 256, 256, Pocket( (12, 10) ).Concat( Pocket( (250, 250) ) ) );
		var pockets = new ParkPresenceState.RouteSearch( 256, 256 );
		Assert.IsNull( pockets.Find( walled, (5, 10), (12, 10) ) );
		Assert.AreEqual( 0, pockets.Expanded, "a near spot that cannot be reached costs no search" );
		Assert.IsNull( pockets.Find( walled, (1, 1), (250, 250) ) );
		Assert.AreEqual( 0, pockets.Expanded, "nor does a far one" );
		// the same arrays serve the next search: nothing of the others is left in them
		var again = pockets.Find( walled, (1, 1), (240, 200) );
		Assert.AreEqual( (1, 1), again![0] );
		Assert.AreEqual( (240, 200), again[^1] );
		Assert.AreEqual( 240, again.Count, "the octile route: 199 diagonal steps and 40 straight ones" );
		// a short distance behind a long wall: the route goes round its far end instead of straight through it
		var wall = new Ground( 256, 256, Enumerable.Range( 0, 250 ).Select( y => (128, y) ) );
		var detour = new ParkPresenceState.RouteSearch( 256, 256 ).Find( wall, (124, 10), (132, 10) );
		Assert.IsNotNull( detour, "the long way round is found" );
		Assert.IsTrue( detour!.Any( cell => cell.Y >= 250 ), "round the wall's end" );
		Assert.IsTrue( detour.All( cell => wall.IsPassable( cell.X, cell.Y ) ) );
	}

	[TestMethod]
	public void AFigureOnABlockedCellLeavesItByAnyFreeStep()
	{
		// the figure stands on a blocked cell beside the walled area's way out
		var ground = new Ground( 20, 20, new[] { (5, 5) }.Concat( Enumerable.Range( 0, 20 ).Select( y => (10, y) ) ) );
		var route = ParkPresenceState.Route( ground, (5, 5), (2, 2) );
		Assert.AreEqual( (5, 5), route![0] );
		Assert.AreEqual( (2, 2), route[^1] );
		Assert.IsNull( ParkPresenceState.Route( ground, (5, 5), (15, 5) ), "across the wall there is no route" );
	}

	[TestMethod]
	public void AWalkFollowsTheRouteAndEndsAtTheClickedPoint()
	{
		var state = new ParkPresenceState( Wall( gap: 7 ), "Ann", (2.5f, 2.5f) );
		state.WalkTo( 8.25f, 2.75f );
		Assert.IsTrue( state.Walking );
		var crossed = false;
		for ( var frame = 0; frame < 2000 && state.Walking; frame++ )
		{
			state.Advance( 0.05f, false, false );
			if ( state.Own.X is > 5f and < 6f )
				crossed |= state.Own.Y is > 7f and < 8f;
			Assert.IsTrue( (int)state.Own.X != 5 || (int)state.Own.Y == 7, $"the figure stays off the wall ({state.Own.X}, {state.Own.Y})" );
		}
		Assert.IsFalse( state.Walking );
		Assert.IsTrue( crossed, "the figure went through the gap" );
		Assert.AreEqual( 8.25f, state.Own.X, 1e-4f );
		Assert.AreEqual( 2.75f, state.Own.Y, 1e-4f );
	}

	[TestMethod]
	public void WithoutARouteTheFigureWalksStraight()
	{
		// the original moves straight to the point when its route search fails (0x50F8E0)
		var state = new ParkPresenceState( Wall( gap: null ), "Ann", (2.5f, 2.5f) );
		state.WalkTo( 8.5f, 2.5f );
		var seconds = 0f;
		while ( state.Walking && seconds < 60 )
		{
			state.Advance( 0.1f, false, false );
			seconds += 0.1f;
		}
		Assert.AreEqual( (8.5f, 2.5f), (state.Own.X, state.Own.Y) );
		Assert.AreEqual( 6f / ParkPresenceState.WalkSpeed, seconds, 0.11f, "six cells at walking speed" );
	}

	[TestMethod]
	public void AClickOnABlockedCellWalksToTheNearestFreeOne()
	{
		var water = Enumerable.Range( 0, 10 ).SelectMany( y => Enumerable.Range( 6, 4 ).Select( x => (x, y) ) );
		var state = new ParkPresenceState( new Ground( 10, 10, water ), "Ann", (1.5f, 4.5f) );
		state.WalkTo( 7.5f, 4.5f );
		while ( state.Walking )
			state.Advance( 0.1f, false, false );
		Assert.AreEqual( (5.5f, 4.5f), (state.Own.X, state.Own.Y), "the free cell next to the water" );
		// a click deep in a large blocked area finds nothing near and does not walk
		var lake = new ParkPresenceState( new Ground( 20, 20, Enumerable.Range( 0, 20 ).SelectMany( y => Enumerable.Range( 8, 12 ).Select( x => (x, y) ) ) ), "Ann", (1.5f, 1.5f) );
		lake.WalkTo( 15.5f, 5.5f );
		Assert.IsFalse( lake.Walking );
	}

	[TestMethod]
	public void ADoubleClickTeleports()
	{
		var state = new ParkPresenceState( new Ground( 20, 20 ), "Ann", (1.5f, 1.5f) );
		Assert.IsFalse( state.Click( 10f, 10f, 1.0 ) );
		Assert.IsTrue( state.Walking );
		Assert.IsTrue( state.Click( 10.5f, 10.2f, 1.3 ), "a second click near the first within 0.4 s" );
		Assert.IsFalse( state.Walking );
		Assert.AreEqual( (10.5f, 10.2f), (state.Own.X, state.Own.Y) );
		// a third click starts a new pair: it walks
		Assert.IsFalse( state.Click( 2f, 2f, 1.4 ) );
		Assert.IsFalse( state.Click( 18f, 18f, 1.5 ), "a click far from the previous one walks" );
		Assert.IsFalse( state.Click( 18f, 18f, 2.5 ), "a click long after the previous one walks" );
	}

	[TestMethod]
	public void PlacesAreSentOnArrivalAnnouncementAndEveryIntervalWhileWalking()
	{
		var state = new ParkPresenceState( new Ground( 100, 100 ), "Ann", (10.5f, 10.5f) );
		Assert.IsNull( state.Advance( 0.016f, false, true ), "nothing goes out while not in the room" );
		Assert.AreEqual( (0.105f, 0.105f), state.Advance( 0.016f, true, false )!.Value, "the first place announces the figure" );
		Assert.IsNull( state.Advance( 0.5f, true, false ), "a figure that stands still sends nothing" );
		Assert.IsNotNull( state.Advance( 0.016f, true, true ), "a new connection announces again" );
		state.WalkTo( 20.5f, 10.5f );
		var sent = new List<float>();
		var time = 0f;
		while ( state.Walking || time < 0.2f )
		{
			time += 0.016f;
			if ( state.Advance( 0.016f, true, false ) is { } place )
				sent.Add( time );
		}
		Assert.IsTrue( sent.Count >= 60, $"about one place every 0.12 s for 8.3 s of walking ({sent.Count})" );
		for ( var index = 1; index < sent.Count - 1; index++ )
			Assert.IsTrue( sent[index] - sent[index - 1] >= ParkPresenceState.SendInterval - 1e-4f, "no faster than the interval" );
		Assert.AreEqual( (0.205f, 0.105f), state.ToWire( state.Own.X, state.Own.Y ), "the last place sent is the end of the walk" );
		state.TeleportTo( 50f, 50f );
		Assert.AreEqual( (0.5f, 0.5f), state.Advance( 0.001f, true, false )!.Value, "a teleport goes out at once" );
	}

	[TestMethod]
	public void ARidingPlayersFigureIsHiddenWhereItStoodAndAppearsThereAgain()
	{
		var state = new ParkPresenceState( new Ground( 100, 100 ), "Ann", (10.5f, 10.5f) );
		Assert.IsNotNull( state.Advance( 0.016f, true, false ) );
		Assert.AreEqual( ChatMoveMode.Teleport, state.SentMode, "the figure enters the park as a teleport (tp.exe 0x4E5EF0)" );
		state.WalkTo( 20.5f, 10.5f );
		state.Advance( 1f, true, false );
		var stood = (state.Own.X, state.Own.Y);
		// boarding a ride gives up the walk and hides the figure where it stood, at once
		state.SetRiding( true );
		Assert.IsFalse( state.Walking );
		Assert.AreEqual( state.ToWire( stood.X, stood.Y ), state.Advance( 0.016f, true, false )!.Value );
		Assert.AreEqual( ChatMoveMode.Hide, state.SentMode );
		CollectionAssert.DoesNotContain( state.Shown.ToList(), state.Own, "the own figure is not drawn while riding" );
		// clicks do not move it meanwhile, and its keyframes and a new connection's announcement keep it hidden
		Assert.IsFalse( state.Click( 30f, 30f, 5.0 ) );
		state.TeleportTo( 40f, 40f );
		Assert.IsNull( state.Advance( 1f, true, false ) );
		Assert.AreEqual( stood, (state.Own.X, state.Own.Y) );
		Assert.IsNotNull( state.Advance( ParkPresenceState.KeyframeSeconds, true, false ) );
		Assert.AreEqual( ChatMoveMode.Hide, state.SentMode );
		Assert.IsNotNull( state.Advance( 0.016f, true, true ) );
		Assert.AreEqual( ChatMoveMode.Hide, state.SentMode );
		// the ride's end shows it where it stood, once; then it walks as before
		state.SetRiding( false );
		Assert.AreEqual( state.ToWire( stood.X, stood.Y ), state.Advance( 0.016f, true, false )!.Value );
		Assert.AreEqual( ChatMoveMode.Unhide, state.SentMode );
		CollectionAssert.Contains( state.Shown.ToList(), state.Own );
		state.WalkTo( 20.5f, 20.5f );
		Assert.IsNotNull( state.Advance( 0.5f, true, false ) );
		Assert.AreEqual( ChatMoveMode.Walk, state.SentMode );
		// a teleport says so
		state.TeleportTo( 50f, 50f );
		Assert.IsNotNull( state.Advance( 0.001f, true, false ) );
		Assert.AreEqual( ChatMoveMode.Teleport, state.SentMode );
		// a ride with an exit lets the figure out there
		state.SetRiding( true, (70.5f, 30.5f) );
		Assert.IsNotNull( state.Advance( 0.016f, true, false ) );
		Assert.AreEqual( (50f, 50f), (state.Own.X, state.Own.Y), "hidden where it stood" );
		state.SetRiding( false );
		Assert.AreEqual( state.ToWire( 70.5f, 30.5f ), state.Advance( 0.016f, true, false )!.Value );
		Assert.AreEqual( ChatMoveMode.Unhide, state.SentMode );
		Assert.AreEqual( (70.5f, 30.5f), (state.Own.X, state.Own.Y) );
	}

	[TestMethod]
	public void AFigureWhoseMovementJumpedIsAtItsPlaceAtOnce()
	{
		var state = new ParkPresenceState( new Ground( 100, 100 ), "Ann", (1, 1) );
		var positions = new Dictionary<string, (float X, float Y)>( StringComparer.OrdinalIgnoreCase ) { ["Bob"] = (0.5f, 0.5f) };
		var jumps = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		var players = new[] { "Ann", "Bob" };
		state.Sync( positions, players, 0.016f, jumps );
		var bob = state.Others.Single();
		// a short step is walked to catch up with...
		positions["Bob"] = (0.52f, 0.5f);
		state.Sync( positions, players, 0.016f, jumps );
		Assert.IsTrue( bob.X < 51f, $"walked toward the place ({bob.X})" );
		// ...unless the movement was a teleport, also within the teleport distance
		positions["Bob"] = (0.525f, 0.5f);
		jumps["Bob"] = 1;
		Assert.IsTrue( 52.5f - bob.X < ParkPresenceState.TeleportDistance );
		state.Sync( positions, players, 0.016f, jumps );
		Assert.AreEqual( 52.5f, bob.X, 1e-3f, "at the place at once" );
		Assert.AreEqual( 50f, bob.Y, 1e-3f );
		// a player who rides has no place: the figure goes, and comes back at its place when the ride ends
		positions.Remove( "Bob" );
		state.Sync( positions, players, 0.016f, jumps );
		Assert.AreEqual( 0, state.Others.Count );
		positions["Bob"] = (0.3f, 0.3f);
		jumps["Bob"] = 2;
		state.Sync( positions, players, 0.016f, jumps );
		Assert.AreEqual( 30f, state.Others.Single().X, 1e-3f );
		Assert.AreEqual( 30f, state.Others.Single().Y, 1e-3f );
	}

	[TestMethod]
	public void OtherPlayersFollowTheirRelayedPlacesAndLeaveWithTheRoster()
	{
		var state = new ParkPresenceState( new Ground( 100, 100 ), "Ann", (1, 1) );
		var positions = new Dictionary<string, (float X, float Y)>( StringComparer.OrdinalIgnoreCase )
		{
			["ann"] = (0.9f, 0.9f), ["Bob"] = (0.5f, 0.5f), ["Gone"] = (0.2f, 0.2f),
		};
		var players = new[] { "Ann", "BOB" };
		state.Sync( positions, players, 0.016f );
		Assert.AreEqual( "Bob", string.Join( ",", state.Others.Select( other => other.Name ) ), "the own name and players not in the room are left out" );
		var bob = state.Others[0];
		Assert.AreEqual( (50f, 50f), (bob.X, bob.Y), "a new player appears at its place" );
		positions["Bob"] = (0.51f, 0.5f);
		state.Sync( positions, players, 0.05f );
		Assert.IsTrue( bob.X > 50f && bob.X < 51f, $"a short move is walked, not jumped ({bob.X})" );
		Assert.IsTrue( bob.Moving && bob.HeadingX > 0.99f );
		for ( var frame = 0; frame < 20; frame++ )
			state.Sync( positions, players, 0.05f );
		Assert.AreEqual( 51f, bob.X, 1e-4f );
		Assert.IsFalse( bob.Moving, "it stands once it has caught up" );
		positions["Bob"] = (0.8f, 0.5f);
		state.Sync( positions, players, 0.016f );
		Assert.AreEqual( (80f, 50f), (bob.X, bob.Y), "a jump of more than three cells is a teleport" );
		// a busy room's roster cut short with "+N" (ChatHub.RosterEvent) says nobody is absent: every relayed place is drawn
		state.Sync( positions, new[] { "Ann", "+2" }, 0.016f );
		Assert.AreEqual( "Bob,Gone", string.Join( ",", state.Others.Select( other => other.Name ) ) );
		state.Sync( positions, new[] { "Ann" }, 0.016f );
		Assert.AreEqual( 0, state.Others.Count, "a player who left the room is no longer drawn" );
	}

	[TestMethod]
	public void EveryClientChoosesTheSameLookForAName()
	{
		Assert.AreEqual( 355060760, ParkPresenceState.LookOf( "Ann" ) );
		Assert.AreEqual( ParkPresenceState.LookOf( "Ann" ), ParkPresenceState.LookOf( "ANN" ) );
		Assert.AreEqual( 113680596, ParkPresenceState.LookOf( "bob" ) );
		Assert.AreEqual( 1125401212, ParkPresenceState.LookOf( "Zoë" ) );
	}
}
