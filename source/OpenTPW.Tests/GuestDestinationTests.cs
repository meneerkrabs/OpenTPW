using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Round 9, package G (guest destinations): the target lives until the next major choice, the minor decision's detour keeps a major destination that the exit walk takes up again (tp.exe 0x004FD570, 0x00500900),
/// the minor decision reads the cells the rides stand on and walks to their access cell +0x36 (0x004DB33C), a ride without a front is skipped (0x004DE040), the bin walk ends in the bin's entrance cell, and the
/// camera's mask-2 rule for a type 9 target (0x004D88BC..0x004D8B28).
/// </summary>
public partial class GuestTests
{
	/// <summary>An entrance cell of a footprint (type 9) whose link byte points at its outside cell, as the placement writes it (<c>ParkCellMap.WriteFootprint</c>).</summary>
	private static void Entrance( GuestPathGrid grid, (int X, int Y) cell, byte links )
	{
		grid.Cells.SetType( cell.X, cell.Y, ParkCellTypes.Entrance );
		grid.Cells.SetLinks( cell.X, cell.Y, links );
		grid.Cells.SetQueueLink( cell.X, cell.Y, links );
	}

	private const byte LinkUp = 0x01, LinkRight = 0x04, LinkDown = 0x10, LinkLeft = 0x40;

	private static (GuestSimulation Simulation, ScriptedAttraction Far, ScriptedAttraction Near, ScriptedAttraction Decoy, Guest Guest) DetourPark( bool withGuest = true )
	{
		var grid = CreateCross();
		var simulation = new GuestSimulation( grid, CreateSettings(), 9 ) { ArrivalsEnabled = false };
		// the far ride: its path cell (15, 5) and the entrance cell above it, the anchor further north
		var far = CreateShop( 1, (15, 5) );
		Entrance( grid, (15, 4), LinkDown );
		far.Bridge.AccessCell = (15, 4);
		far.Bridge.PositionCell = (15, 2);
		// the near ride beside the vertical path: its join cell (10, 6), its entrance cell (11, 6) to the east, its anchor in the guest's window
		var near = CreateShop( 2, (10, 6) );
		Entrance( grid, (11, 6), LinkLeft );
		near.Bridge.AccessCell = (11, 6);
		near.Bridge.PositionCell = (11, 7);
		// a ride whose join cell lies in the window but whose anchor does not: the original lists rides on the cell they stand on
		var decoy = CreateShop( 3, (10, 7) );
		Entrance( grid, (9, 7), LinkRight );
		decoy.Bridge.AccessCell = (9, 7);
		decoy.Bridge.PositionCell = (3, 2);
		simulation.Register( far.Bridge );
		simulation.Register( near.Bridge );
		simulation.Register( decoy.Bridge );
		if ( !withGuest )
			return (simulation, far, near, decoy, null!);
		var guest = simulation.SpawnInPark( 10, 8 );
		guest.AttractionId = far.Bridge.AttractionId;
		guest.State = GuestState.GoingToRide;
		return (simulation, far, near, decoy, guest);
	}

	[TestMethod]
	public void TheMinorDecisionListsRidesOnTheCellTheyStandOnAndKeepsTheOldTargetAsTheMajorDestination()
	{
		var (simulation, far, near, decoy, guest) = DetourPark();
		simulation.MinorDecision( guest );
		Assert.AreEqual( near.Bridge.AttractionId, guest.AttractionId, "the ride whose anchor lies in the window and nearer to the target's back cell wins; the decoy's join cell in the window does not count" );
		Assert.AreEqual( far.Bridge.AttractionId, guest.SavedMajorAttractionId, "+0x1DE = the old target (0x004FD92A)" );
		Assert.IsTrue( guest.HasWaypoint, "the route request to the best's access cell was made" );
		Assert.AreNotEqual( decoy.Bridge.AttractionId, guest.AttractionId );
	}

	[TestMethod]
	public void TheDetourWalksIntoTheAccessCellAndThenAsksForTheBackCellAgain()
	{
		var (simulation, far, near, _, guest) = DetourPark();
		simulation.MinorDecision( guest );
		var visited = new HashSet<(int X, int Y)>();
		var states = new List<GuestState>();
		for ( var tick = 0; tick < 40 * 60 && guest.State is GuestState.GoingToRide; tick++ )
		{
			simulation.Tick( Tick );
			far.Step();
			near.Step();
			visited.Add( guest.Navigation.Cell );
		}
		// [BIN:TPW-EXE:0x004FA530 route request] the destination is the entrance cell +0x36 inside the footprint; state 10 then finds the guest off the back cell (0x004FFC40) and asks for the back cell (0x004FFE16)
		Assert.IsTrue( visited.Contains( (11, 6) ), $"the guest stood in the access cell: {string.Join( " ", visited )}" );
		Assert.AreEqual( GuestState.Queueing, guest.State is GuestState.MovingUpQueue ? GuestState.Queueing : guest.State, $"then it joined the queue ({guest.State})" );
		Assert.AreEqual( near.Bridge.AttractionId, guest.AttractionId );
		Assert.AreEqual( far.Bridge.AttractionId, guest.SavedMajorAttractionId, "the detour's major destination stays until the exit walk" );
	}

	[TestMethod]
	public void TheExitWalkTakesTheMajorDestinationUpAgainAndWalksToItsBackCell()
	{
		var (simulation, far, near, _, guest) = DetourPark();
		guest.AttractionId = near.Bridge.AttractionId;
		guest.SavedMajorAttractionId = far.Bridge.AttractionId;
		guest.State = GuestState.ExitingRide;
		guest.HasWaypoint = false;
		simulation.Tick( Tick );
		// [BIN:TPW-EXE:0x00500900 state 15] arrived: +0x1DC = +0x1DE, +0x1DE = 0, a route request to the back cell, "Left minor destination, found old major", state 10
		Assert.AreEqual( GuestState.GoingToRide, guest.State );
		Assert.AreEqual( far.Bridge.AttractionId, guest.AttractionId );
		Assert.AreEqual( 0, guest.SavedMajorAttractionId );
		Assert.IsTrue( guest.HasWaypoint );
	}

	[TestMethod]
	public void WithoutAMajorDestinationTheExitWalkDecidesAgainAndKeepsTheTarget()
	{
		var (simulation, _, near, _, guest) = DetourPark();
		guest.AttractionId = near.Bridge.AttractionId;
		guest.State = GuestState.ExitingRide;
		guest.HasWaypoint = false;
		simulation.Tick( Tick );
		Assert.AreEqual( GuestState.DoingNothing, guest.State, "\"successfully left ride\", state 6" );
		Assert.AreEqual( near.Bridge.AttractionId, guest.AttractionId, "+0x1DC lives until the next major choice (0x004FCB21)" );
	}

	[TestMethod]
	public void AMajorDestinationWithoutAWayInKeepsItAsTheTargetInStateSix()
	{
		var (simulation, far, near, _, guest) = DetourPark();
		// the major destination lost its front: its back cell is 0 (0x004DE110), so the route request is skipped and the guest falls to state 6 with the target set (0x005009F9)
		far.Bridge.QueueEntryResolver = () => (15, 4);
		Entrance( simulation.Grid, (15, 4), 0 );
		far.Bridge.RecomputeQueue( simulation.Grid );
		Assert.IsNull( far.Bridge.BackCell );
		guest.AttractionId = near.Bridge.AttractionId;
		guest.SavedMajorAttractionId = far.Bridge.AttractionId;
		guest.State = GuestState.ExitingRide;
		guest.HasWaypoint = false;
		simulation.Tick( Tick );
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		Assert.AreEqual( far.Bridge.AttractionId, guest.AttractionId );
		Assert.AreEqual( 0, guest.SavedMajorAttractionId );
	}

	[TestMethod]
	public void AStuckExitWalkClearsTheTargetButNotTheMajorDestination()
	{
		var (simulation, far, near, _, guest) = DetourPark();
		guest.AttractionId = near.Bridge.AttractionId;
		guest.SavedMajorAttractionId = far.Bridge.AttractionId;
		guest.State = GuestState.ExitingRide;
		// a walk into the park that cannot be made: the destination is an unreachable cell
		simulation.Grid.Cells.SetType( 0, 0, ParkCellType.Empty );
		var navigation = guest.Navigation;
		navigation.Navigate( simulation.Grid.Cells, GuestNavigation.Centre( (0, 0) ).X, GuestNavigation.Centre( (0, 0) ).Y );
		guest.HasWaypoint = true;
		for ( var tick = 0; tick < 5 * 60 && guest.State == GuestState.ExitingRide; tick++ )
			simulation.Tick( Tick );
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		Assert.AreEqual( 0, guest.AttractionId, "0x00500A3B: a stuck walk clears +0x1DC" );
		Assert.AreEqual( far.Bridge.AttractionId, guest.SavedMajorAttractionId, "and writes no +0x1DE" );
	}

	[TestMethod]
	public void RemovingAnAttractionClearsItFromEveryGuestsMajorDestinationAndHitsAGuestThatJustLeftIt()
	{
		var (simulation, far, near, _, guest) = DetourPark();
		var other = simulation.SpawnInPark( 10, 2 );
		other.SavedMajorAttractionId = far.Bridge.AttractionId;
		other.AttractionId = 0;
		guest.AttractionId = far.Bridge.AttractionId;
		guest.State = GuestState.DoingNothing;
		var happiness = guest.Happiness;
		simulation.Unregister( far.Bridge );
		Assert.AreEqual( 0, other.SavedMajorAttractionId, "0x004FB4A6: +0x1DE is cleared whenever the message names the object" );
		Assert.AreEqual( 0, guest.AttractionId, "the retained target of a guest that has just left the ride is hit (0x004FB444)" );
		Assert.IsTrue( guest.Happiness < happiness, "the small happiness step" );
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		Assert.AreEqual( near.Bridge.AttractionId, simulation.Attractions.Single( attraction => attraction.AttractionId == near.Bridge.AttractionId ).AttractionId );
	}

	[TestMethod]
	public void AGuestWhoseTargetIsAnotherRideIsNotHitByTheRemoval()
	{
		var (simulation, far, near, _, guest) = DetourPark();
		guest.AttractionId = near.Bridge.AttractionId;
		guest.State = GuestState.DoingNothing;
		var happiness = guest.Happiness;
		simulation.Unregister( far.Bridge );
		Assert.AreEqual( near.Bridge.AttractionId, guest.AttractionId );
		Assert.AreEqual( happiness, guest.Happiness );
	}

	[TestMethod]
	public void TheMajorDestinationIsSavedValidatedAndRestored()
	{
		var (simulation, far, near, _, guest) = DetourPark();
		simulation.SpawnInPark( 10, 2 );
		guest.AttractionId = near.Bridge.AttractionId;
		guest.SavedMajorAttractionId = far.Bridge.AttractionId;
		guest.State = GuestState.DoingNothing;
		var snapshot = simulation.CaptureSnapshot();
		Assert.AreEqual( far.Bridge.AttractionId, snapshot.Guests.Single( item => item.Id == guest.Id ).SavedMajorAttractionId );
		Assert.IsTrue( simulation.ValidateSnapshot( snapshot ).IsValid );
		var json = System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( snapshot.Guests.Single( item => item.Id == guest.Id ) ) );
		StringAssert.Contains( json, "SavedMajorAttractionId" );
		var withoutNone = System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( snapshot.Guests.Single( item => item.Id != guest.Id ) ) );
		Assert.IsFalse( withoutNone.Contains( "SavedMajorAttractionId" ), "written only while set: an old reader and an old save are unchanged" );
		// a major destination that names no attraction is rejected
		var corrupt = snapshot with { Guests = snapshot.Guests.Select( item => item.Id == guest.Id ? item with { SavedMajorAttractionId = 99 } : item ).ToArray() };
		Assert.IsFalse( simulation.ValidateSnapshot( corrupt ).IsValid );
		var before = simulation.ComputeStateHash();
		simulation.RestoreSnapshot( snapshot );
		Assert.AreEqual( far.Bridge.AttractionId, simulation.Find( guest.Id )!.SavedMajorAttractionId );
		Assert.AreEqual( before, simulation.ComputeStateHash() );
		simulation.Find( guest.Id )!.SavedMajorAttractionId = 0;
		Assert.AreNotEqual( before, simulation.ComputeStateHash(), "the major destination is in the digest while set" );
	}

	// ---- QUEUE-003, the guests' side of a zero front ---------------------------------------------

	private static ScriptedAttraction TypedShop( GuestPathGrid grid, int id, (int X, int Y) join, (int X, int Y) entrance, byte links )
	{
		var shop = CreateShop( id, join );
		Entrance( grid, entrance, links );
		shop.Bridge.QueueEntryResolver = () => entrance;
		shop.Bridge.QueueFrontCell = join;
		shop.Bridge.QueueEntranceDirection = 2;
		shop.Bridge.AccessCell = entrance;
		shop.Bridge.PositionCell = entrance;
		shop.Bridge.RecomputeQueue( grid );
		return shop;
	}

	[TestMethod]
	public void AGuestNeitherScoresNorChoosesNorJoinsARideWithoutAFront()
	{
		var grid = CreateCross();
		var simulation = new GuestSimulation( grid, CreateSettings(), 9 ) { ArrivalsEnabled = false };
		var shop = TypedShop( grid, 1, (15, 5), (15, 4), 0 );
		simulation.Register( shop.Bridge );
		var guest = simulation.SpawnInPark( 10, 5 );
		Assert.IsTrue( shop.Bridge.IsZeroFront );
		Assert.IsNull( shop.Bridge.BackCell );
		Assert.AreEqual( 0, shop.Bridge.QueueSizeInCells );
		Assert.AreEqual( 0u, simulation.ScoreAttraction( guest, shop.Bridge ), "back cell 0: the score is 0" );
		Assert.IsNull( simulation.PeekChoice( guest ), "0x004FCB84 skips a ride whose back cell is 0" );
		Assert.AreEqual( QueueJoinResult.NoRoom, shop.Bridge.JoinQueue( guest.Id ), "count < 4 x 0 fails (0x004DDA20)" );
		// the same ride with its link set has a front and is chosen
		Entrance( grid, (15, 4), LinkDown );
		shop.Bridge.RecomputeQueue( grid );
		Assert.IsFalse( shop.Bridge.IsZeroFront );
		Assert.AreEqual( ((int, int)?)(15, 5), shop.Bridge.BackCell );
		Assert.AreEqual( shop.Bridge.AttractionId, simulation.PeekChoice( guest )?.AttractionId );
	}

	[TestMethod]
	public void AGuestWalkingToARideThatLostItsFrontFallsToStateSixAndKeepsTheTarget()
	{
		var grid = CreateCross();
		var simulation = new GuestSimulation( grid, CreateSettings(), 9 ) { ArrivalsEnabled = false };
		var shop = TypedShop( grid, 1, (15, 5), (15, 4), LinkDown );
		simulation.Register( shop.Bridge );
		var guest = simulation.SpawnInPark( 14, 5 );
		guest.AttractionId = shop.Bridge.AttractionId;
		guest.State = GuestState.GoingToRide;
		guest.HasWaypoint = false;
		Entrance( grid, (15, 4), 0 );
		shop.Bridge.RecomputeQueue( grid );
		simulation.Tick( Tick );
		// [BIN:TPW-EXE:0x004FFE16 back of the queue has moved] the back cell is 0: event 0x16, state 6, no +0x1DC write
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		Assert.AreEqual( shop.Bridge.AttractionId, guest.AttractionId );
	}

	[TestMethod]
	public void ARideThatLosesItsFrontDismissesItsQueuedGuestsButTheCalledOne()
	{
		var grid = CreateCross();
		var simulation = new GuestSimulation( grid, CreateSettings(), 9 ) { ArrivalsEnabled = false };
		var shop = TypedShop( grid, 1, (15, 5), (15, 4), LinkDown );
		simulation.Register( shop.Bridge );
		var guest = simulation.SpawnInPark( 15, 5 );
		guest.AttractionId = shop.Bridge.AttractionId;
		Assert.AreEqual( QueueJoinResult.Joined, shop.Bridge.JoinQueue( guest.Id ) );
		guest.State = GuestState.Queueing;
		Entrance( grid, (15, 4), 0 );
		simulation.Tick( Tick );
		Assert.IsTrue( shop.Bridge.IsZeroFront );
		for ( var tick = 0; tick < 5 && shop.Bridge.GetQueuePosition( guest.Id ) >= 0; tick++ )
			simulation.Tick( Tick );
		Assert.AreEqual( -1, shop.Bridge.GetQueuePosition( guest.Id ), "the room is 4 x +0x40 = 0: the re-evaluation dismisses the queued guest (0x00501496)" );
	}

	[TestMethod]
	public void AZeroFrontIsSavedAndASaveFromBeforeLoadsWithItsOldOneCellQueue()
	{
		var grid = CreateCross();
		var shop = TypedShop( grid, 1, (15, 5), (15, 4), 0 );
		var saved = shop.Bridge.CaptureSnapshot();
		Assert.AreEqual( true, saved.ZeroFront );
		Assert.IsNull( saved.JoinCell );
		Assert.IsTrue( RideVisitorBridge.ValidateSnapshot( saved ).Count == 0, string.Join( " ", RideVisitorBridge.ValidateSnapshot( saved ) ) );
		Assert.IsTrue( shop.Bridge.MatchesSnapshotQueueGeometry( grid, saved ) );
		// a save from before: no ZeroFront word and the host's one-cell queue with its join cell
		var legacy = saved with { ZeroFront = null, JoinCell = (15, 5) };
		Assert.IsTrue( RideVisitorBridge.ValidateSnapshot( legacy ).Count == 0 );
		Assert.IsTrue( shop.Bridge.MatchesSnapshotQueueGeometry( grid, legacy ), "an old save still loads" );
		// a ride with a front must not match a save that says it has none
		var framed = TypedShop( grid, 2, (3, 5), (3, 4), LinkDown );
		Assert.IsFalse( framed.Bridge.MatchesSnapshotQueueGeometry( grid, framed.Bridge.CaptureSnapshot() with { ZeroFront = true } ) );
		var restored = TypedShop( grid, 3, (6, 5), (6, 4), 0 );
		restored.Bridge.RestoreSnapshot( saved with { AttractionId = 3 } );
		Assert.IsTrue( restored.Bridge.IsZeroFront );
		Assert.IsNull( restored.Bridge.BackCell );
	}

	// ---- GUEST-005, the bin's access cell --------------------------------------------------------

	[TestMethod]
	public void ABinsAccessCellIsItsEntranceCellInsideTheFootprintAndTheGuestWalksIntoIt()
	{
		var grid = new GuestPathGrid( 20, 10 );
		for ( var x = 0; x < 20; x++ )
			grid.SetPath( x, 5, true );
		Entrance( grid, (4, 4), LinkDown );
		var litter = new ParkLitter();
		var bins = new[] { new LitterBin( 9, (4, 4), (4, 4) ) };
		var simulation = new GuestSimulation( grid, CreateSettings(), 5 ) { ArrivalsEnabled = false, Litter = litter, Bins = () => bins };
		var binned = new List<int>();
		simulation.LitterBinned += ( _, bin ) => binned.Add( bin );
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Litter = 95;
		var visited = new HashSet<(int X, int Y)>();
		for ( var tick = 0; tick < 60 * 60 && guest.Litter > 0; tick++ )
		{
			simulation.Tick( Tick );
			visited.Add( guest.Navigation.Cell );
		}
		// [BIN:TPW-EXE:0x00500DC0 bin walk] the route request goes to +0x36, the type 9 cell the shape's entrance symbol gave the thing; the edge test opens it from the path cell its link points at
		CollectionAssert.AreEqual( new[] { 9 }, binned );
		Assert.IsTrue( visited.Contains( (4, 4) ), "the guest stood in the bin's own cell" );
		Assert.AreEqual( 0, litter.Count );
	}

	// ---- WALK-005, the camera's mask-2 rule ------------------------------------------------------

	[TestMethod]
	public void AMaskTwoStepIntoAnEntranceIsDecidedByTheRideOnItsOwnerCell()
	{
		var map = new ParkCellMap( 8, 8 );
		map.SetType( 2, 3, ParkCellType.Path );
		map.SetLinks( 2, 3, LinkRight );
		map.SetType( 3, 3, ParkCellTypes.Entrance );
		map.SetLinks( 3, 3, LinkLeft );
		const int East = 1;
		Assert.IsFalse( WalkEdges.Blocked( map, 2, 3, East, 2 ), "no ride thing on the owner cell: the test goes on and the link back opens it" );
		map.EntranceRideOpens = ( _, _ ) => false;
		Assert.IsTrue( WalkEdges.Blocked( map, 2, 3, East, 2 ), "a ride that cannot be ridden (0x42A440) or whose type cannot be ridden (+0x118) blocks a mask-2 step" );
		Assert.IsFalse( WalkEdges.Blocked( map, 2, 3, East, 0 ), "every other mask ignores the ride" );
		Assert.IsFalse( WalkEdges.Blocked( map, 2, 3, East, 1 ) );
		map.EntranceRideOpens = ( _, _ ) => true;
		Assert.IsFalse( WalkEdges.Blocked( map, 2, 3, East, 2 ) );
		map.EntranceRideOpens = ( _, _ ) => null;
		Assert.IsFalse( WalkEdges.Blocked( map, 2, 3, East, 2 ), "a cell without an object on its owner cell is not decided by one" );
		// the target's flag 0x400 opens it before the ride is read
		map.EntranceRideOpens = ( _, _ ) => false;
		map.SetFlags( 3, 3, (ParkCellFlags)0x400 );
		Assert.IsFalse( WalkEdges.Blocked( map, 2, 3, East, 2 ) );
	}

	private static Guest ImportSaved( GuestSimulation simulation, int id, uint state, ushort major, ushort savedMajor, Func<int, int> attraction )
	{
		var body = SaveTestPayloads.Guest( new SaveTestPayloads.GuestFields
		{
			Position = (10 * 256 + 128, 8 * 256 + 128), State = state, MajorDest = major, SavedMajorDest = savedMajor, PaidAdmission = 0
		} );
		var payload = new byte[8 + body.Length];
		body.CopyTo( payload, 8 );
		var saved = SaveGuest.Read( payload, new SaveThing( id, SaveThingModel.Guest, 0, 0, 8, body.Length ) );
		var report = simulation.ImportOriginal( new[] { saved }, 755, attraction, _ => 0 );
		Assert.IsTrue( report.Any( line => line.StartsWith( "Guest destinations imported", StringComparison.Ordinal ) ), "the import reports its destinations" );
		return simulation.Find( id )!;
	}

	[DataTestMethod]
	[DataRow( 6u )]
	[DataRow( 7u )]
	[DataRow( 8u )]
	public void AnOriginalSaveKeepsTheTargetOfAGuestDecidingOrWanderingAndItsSavedMajorDestination( uint state )
	{
		// tp.exe saves +0x1DC (mMajorDest) and +0x1DE (mSavedMajorDest) with every person and its loader (0x004FB530) leaves both as read; the state-6/7/8 handlers keep the target (0x004FCB21)
		var (simulation, far, near, _, _) = DetourPark( withGuest: false );
		var guest = ImportSaved( simulation, 90, state, 4001, 4002, id => id == 4001 ? far.Bridge.AttractionId : id == 4002 ? near.Bridge.AttractionId : 0 );
		Assert.AreEqual( far.Bridge.AttractionId, guest.AttractionId, "+0x1DC" );
		Assert.AreEqual( near.Bridge.AttractionId, guest.SavedMajorAttractionId, "+0x1DE" );
	}

	[TestMethod]
	public void AnOriginalSaveNamingNoAttractionLeavesBothDestinationsClear()
	{
		var (simulation, _, _, _, _) = DetourPark( withGuest: false );
		var guest = ImportSaved( simulation, 91, 7, 4001, 4002, _ => 0 );
		Assert.AreEqual( 0, guest.AttractionId );
		Assert.AreEqual( 0, guest.SavedMajorAttractionId );
	}
}
