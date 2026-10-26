using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// QUEUE-015: the queue tool's undo (Back, tp.exe 0x52FE50) clears the cells and writes no guest (the cell clear 0x5367A0); the re-evaluation that follows (0x4DE1F0 at 0x52FFEC, 0x501390 per guest)
/// dismisses by place alone: a guest whose place is at least four per cell leaves the queue where it stands (thought 13 for a thing id divisible by three), every other guest keeps its place.
/// Synthetic membership cases isolate the eligibility boundary.
/// </summary>
[TestClass, DoNotParallelize]
public class QueueRemovalCommandTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests fixture = null!;
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private static readonly (int X, int Y)[] Removed = { (2, 4), (2, 3), (2, 2), (2, 1) };

	private sealed record Park( Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State, RideVM Script )
	{
		public GuestSimulation Guests => Level.Guests!;
		public GuestPathGrid Grid => Guests.Grid;
		public RideVisitorBridge Ride => One.Visitors;
	}
	private sealed record Occupants( Guest Early, Guest Target, Guest Nondivisible, Guest[] Retained );

	[TestInitialize]
	public void Initialize()
	{
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
	}

	[TestCleanup]
	public void Cleanup()
	{
		fixture.Cleanup();
		renderer?.Dispose();
	}

	private Park NewPark()
	{
		// The asset-free Level/economy setup used by ParkCommandLevelTests; the edited bridge belongs to the actual mapped object.
		var type = typeof( UpgradeCatalogueDefaultsTests );
		var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { "", 0, false } )!;
		type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } );
		foreach ( var method in new[] { "SetupStaff", "SetupParkGate", "SetupPaths" } )
			typeof( Level ).GetMethod( method, BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		world.Level.ConfigureObjectHooks();
		typeof( Level ).GetField( "<CellTool>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( world.Level, new CellBuildTool() );
		typeof( Level ).GetField( "<PathVariants>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( world.Level, new PathTileVariants() );
		world.Economy.Ledger.Post( LedgerCategory.OtherIncome, 1_000_000 );
		var grid = world.Level.Guests!.Grid;
		grid.SetPath( 1, 1, false );
		grid.SetPath( 3, 3, false );
		for ( var x = 0; x < 16; x++ )
			grid.SetPath( x, 0, true );
		ConfigureQueue( grid, world.One, 2, 5 );
		ConfigureQueue( grid, world.Two, 10, 1 );
		var script = new RideVM( new RideScriptFile( new MemoryStream( new Asm().Label( "loop" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" ).Build( GuestTests.ShopVariables ) ) ),
			new RideVMOptions { Effects = new VisitorRideScriptEffects( world.One.Visitors, UnimplementedRideScriptEffects.Instance ), Seed = 1 } );
		world.One.Visitors.Attach( script, () => true );
		script.Advance( Tick );
		script[RideVariables.VAR_RUNNING] = 1; // hold admission, as in QueueTests.Rig
		// the simulation has seen both queues as they are: only an edit makes it re-evaluate one
		var seen = (Dictionary<int, int>)typeof( GuestSimulation ).GetField( "seenQueueEdits", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( world.Level.Guests! )!;
		foreach ( var item in new[] { world.One, world.Two } )
			seen[item.Visitors.AttractionId] = item.Visitors.QueueEditCount;
		return new Park( world.Level, world.Economy, world.One, world.Two, world.State, script );
	}

	private static void ConfigureQueue( GuestPathGrid grid, OriginalObject item, int x, int count )
	{
		var ride = item.Visitors;
		// Deliberately synthetic access geometry on a real economy-bound object; no object demolition or access-cell removal is exercised here.
		ride.EntranceCell = ride.ExitCell = (x, 0);
		// a placed ride's own exit cell: the guests' queue exits and edits then follow tp.exe and write no position
		ride.ExitAccessCell = (x, 6);
		ride.QueueFrontCell = (x, 5);
		ride.QueueEntranceDirection = 2;
		ride.OwnerWord = ParkCellMap.CellWord( item.Placement.X, item.Placement.Y );
		ride.Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 );
		Assert.AreEqual( count, QueuePaths.TryExtend( grid, ride, Enumerable.Range( 0, count ).Select( index => (x, 5 - index) ) ) );
		QueuePaths.TryConnect( grid, ride );
		grid.Cells.SetFlags( x, 5, grid.Cells.FlagsAt( x, 5 ) | ParkCellFlags.NoModify );
		grid.Cells.SetPlacementCount( x, 5, 1 );
		grid.Cells.SetType( x, 6, ParkCellTypes.Entrance );
		grid.Cells.SetLinks( x, 6, GuestPathGrid.LinkValue( 0 ) );
		grid.Cells.SetQueueLink( x, 6, GuestPathGrid.LinkValue( 0 ) );
	}

	private static void Put( Guest guest, (int X, int Y) cell )
	{
		guest.OnGrid = true;
		guest.CellX = cell.X;
		guest.CellY = cell.Y;
		guest.Navigation.OffGrid = false;
		(guest.Navigation.X, guest.Navigation.Y) = GuestNavigation.BytePoint( cell, 128, 128 );
		guest.Navigation.SetDestination( guest.Navigation.X, guest.Navigation.Y, Array.Empty<(int X, int Y)>() );
		guest.HasWaypoint = false;
	}

	private static Occupants Seed( Park park, RideVisitorBridge? owner = null, (int X, int Y)? removedCell = null )
	{
		owner ??= park.Ride;
		var spawned = Enumerable.Range( 0, 6 ).Select( _ => park.Guests.SpawnInPark( 2, 0 ) ).ToArray();
		var divisible = spawned.Where( guest => guest.Id % 3 == 0 ).ToArray();
		Assert.AreEqual( 2, divisible.Length );
		var other = spawned.Where( guest => guest.Id % 3 != 0 ).ToArray();
		var order = new[] { divisible[0], other[0], other[1], other[2], divisible[1], other[3] };
		for ( var position = 0; position < order.Length; position++ )
		{
			var guest = order[position];
			owner.AppendToQueue( guest.Id );
			guest.AttractionId = owner.AttractionId;
			guest.State = GuestState.Queueing;
			guest.QueuePosition = position;
			guest.QueueJoinTurn = park.Guests.ParkTurn;
			guest.Happiness = 80;
			guest.Thought = GuestThought.Bored;
			Put( guest, position is 0 or >= 4 ? removedCell ?? (2, 4) : owner.QueueFrontCell!.Value );
		}
		Assert.IsTrue( divisible[0].Id < divisible[1].Id, "the in-room forced removal runs before the overflow guest" );
		Assert.AreEqual( 4, owner.GetQueuePosition( divisible[1].Id ) );
		return new Occupants( divisible[0], divisible[1], other[3], other.Take( 3 ).ToArray() );
	}

	private static UndoLineCommand Undo( Park park, bool accepted = true )
	{
		Assert.IsTrue( park.Level.EnterQueueTool( park.Ride ) );
		Assert.AreEqual( 5, park.Ride.QueueSizeInCells );
		Assert.AreEqual( 1, park.Level.CellTool.SegmentCount );
		var count = park.Level.Commands.Log.Count;
		ParkCommandResult? result = null;
		park.Level.Commands.Applied += Capture;
		try { Assert.IsTrue( park.Level.CellTool.Undo() ); }
		finally { park.Level.Commands.Applied -= Capture; }
		void Capture( ParkCommandLogEntry entry, ParkCommandResult value ) => result = value;
		Assert.IsNotNull( result );
		Assert.AreEqual( accepted, result!.Accepted );
		Assert.AreEqual( count + 1, park.Level.Commands.Log.Count );
		var command = park.Level.Commands.Log[^1].Command as UndoLineCommand;
		Assert.IsNotNull( command );
		Assert.AreEqual( CellToolMode.Queue, command!.Mode );
		Assert.AreEqual( park.Ride.AttractionId, command.Ride );
		CollectionAssert.AreEqual( Removed, command.Built.ToArray() );
		return command;
	}

	private static void Sync( Park park ) => typeof( GuestSimulation ).GetMethod( "SyncQueues", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( park.Guests, null );

	private static void AssertDismissedInPlace( Park park, Guest guest, GuestThought thought, Events events, (int X, int Y) cell )
	{
		Assert.AreEqual( thought, guest.Thought, $"guest {guest.Id}'s thought" );
		Assert.AreEqual( GuestState.DoingNothing, guest.State );
		Assert.AreEqual( 0, guest.AttractionId );
		Assert.AreEqual( -1, park.Ride.GetQueuePosition( guest.Id ) );
		Assert.AreEqual( 80f - park.Guests.Settings.MediumHappinessChange, guest.Happiness );
		Assert.AreEqual( cell, guest.Cell, "the dismissal writes no position: the guest stays on the emptied cell" );
		Assert.AreEqual( cell, guest.Navigation.Cell );
		Assert.AreEqual( 1, events.Ends.Count( item => item.Id == guest.Id && item.Reason == QueueEndReason.QueueShortened ) );
	}

	[DataTestMethod]
	[DataRow( GuestState.Queueing, false, true )]
	[DataRow( GuestState.MovingUpQueue, false, true )]
	[DataRow( GuestState.Boarding, false, true )]
	[DataRow( GuestState.WaitingToBoard, false, false )]
	[DataRow( GuestState.Boarding, true, false )]
	public void UndoWritesNoGuestAndTheReevaluationDismissesByPlaceAlone( GuestState state, bool called, bool dismissed )
	{
		var park = NewPark();
		var occupants = Seed( park );
		occupants.Target.State = state;
		if ( called ) // synthetic called slot beyond room, isolating the caller's exclusion
			park.Ride.RestoreSnapshot( park.Ride.CaptureSnapshot() with { CalledGuest = occupants.Target.Id } );
		var events = new Events( park.Ride );
		park.Guests.Observer = events;
		var rng = park.Guests.RandomState;
		var edits = park.Ride.QueueEditCount;
		var counters = (park.Ride.Dismissed, park.Ride.BoardedTotal, park.Ride.ReleasedTotal, park.Guests.VisitorCount);
		var editedEvents = 0;
		park.Ride.QueueEdited += () => editedEvents++;
		var balance = park.Economy.Balance;
		var income = park.Economy.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherIncome );
		var costs = park.Economy.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherCosts );
		var refund = park.Economy.CellCost( CellPurchase.Queue ) * park.Economy.ScrapPercent( park.State ) / 100;
		var frontFlags = park.Grid.Cells.FlagsAt( 2, 5 );
		var frontPlacements = park.Grid.Cells.PlacementCountAt( 2, 5 );
		Undo( park );
		Assert.AreEqual( rng, park.Guests.RandomState, "the preserved thought draws no random word" );
		Assert.AreEqual( edits + 1, park.Ride.QueueEditCount );
		Assert.AreEqual( 1, editedEvents );
		Assert.AreEqual( balance + 4 * refund, park.Economy.Balance );
		Assert.AreEqual( income + 4 * refund, park.Economy.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherIncome ) );
		Assert.AreEqual( costs, park.Economy.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherCosts ) );
		CollectionAssert.AreEqual( new[] { (2, 5) }, park.Ride.QueueCells.ToArray() );
		Assert.AreEqual( frontFlags, park.Grid.Cells.FlagsAt( 2, 5 ) );
		Assert.AreEqual( frontPlacements, park.Grid.Cells.PlacementCountAt( 2, 5 ) );
		Assert.AreEqual( park.Ride.OwnerWord, park.Grid.Cells.OwnerAt( 2, 5 ) );
		Assert.AreEqual( GuestPathGrid.LinkValue( 2 ), park.Grid.Cells.QueueLinkAt( 2, 5 ) );
		Assert.AreEqual( 0, park.Grid.Cells.LinksAt( 2, 5 ) & GuestPathGrid.LinkValue( 0 ) );
		Assert.AreEqual( 0, park.Grid.Cells.LinksAt( 2, 0 ) & GuestPathGrid.LinkValue( 2 ) );
		foreach ( var (x, y) in Removed )
		{
			Assert.AreEqual( ParkCellType.Empty, park.Grid.Cells.TypeAt( x, y ) );
			Assert.AreEqual( 0, (int)park.Grid.Cells.OwnerAt( x, y ) );
			Assert.AreEqual( 0, (int)park.Grid.Cells.LinksAt( x, y ) );
			Assert.AreEqual( 0, (int)park.Grid.Cells.QueueLinkAt( x, y ) );
		}
		// the clear writes no guest: before the re-evaluation every occupant of the emptied cells is where and what it was
		foreach ( var guest in new[] { occupants.Early, occupants.Target, occupants.Nondivisible } )
		{
			Assert.AreEqual( (2, 4), guest.Cell );
			Assert.AreEqual( guest == occupants.Target ? state : GuestState.Queueing, guest.State );
			Assert.AreEqual( park.Ride.AttractionId, guest.AttractionId );
		}
		Sync( park );
		// 0x501390: a place of four per cell or more (here 4 and 5, the room being 4) leaves the queue in place, thought 13 for an id divisible by three; the called guest and a guest waiting to board stay
		AssertDismissedInPlace( park, occupants.Nondivisible, GuestThought.Bored, events, (2, 4) );
		if ( dismissed )
			AssertDismissedInPlace( park, occupants.Target, GuestThought.Angry, events, (2, 4) );
		else
			Assert.AreEqual( state, occupants.Target.State, "an excluded guest keeps its place and its state" );
		Assert.AreEqual( 0, events.Thoughts.Count( item => item.Id == occupants.Early.Id ), "a guest within the room is not dismissed" );
		Assert.AreEqual( GuestState.Queueing, occupants.Early.State );
		Assert.AreEqual( 0, park.Ride.GetQueuePosition( occupants.Early.Id ), "the guest on an emptied cell keeps its place" );
		Assert.AreEqual( rng, park.Guests.RandomState );
		Assert.AreEqual( counters.VisitorCount, park.Guests.VisitorCount );
		foreach ( var guest in occupants.Retained )
		{
			Assert.AreEqual( GuestState.Queueing, guest.State );
			Assert.AreEqual( (2, 5), guest.Cell );
			Assert.AreEqual( 80f, guest.Happiness );
			Assert.AreEqual( GuestThought.Bored, guest.Thought );
		}
		Sync( park );
		Assert.AreEqual( dismissed ? 2 : 1, events.Ends.Count, "a later re-evaluation does not dismiss anyone twice" );
		Assert.AreEqual( balance + 4 * refund, park.Economy.Balance );
	}

	[TestMethod]
	public void NaturallyQueuedOverflowGetsItsThoughtFromTheReevaluationAfterTheToolUndo()
	{
		var park = NewPark();
		var guests = Enumerable.Range( 0, 16 ).Select( _ => park.Guests.SpawnInPark( 2, 0 ) ).ToArray();
		foreach ( var guest in guests )
		{
			guest.Happiness = 80;
			guest.AttractionId = park.Ride.AttractionId;
			guest.State = GuestState.GoingToRide;
		}
		for ( var tick = 0; tick < 20_000 && !guests.All( guest => guest.State == GuestState.Queueing ); tick++ )
		{
			park.Guests.Tick( Tick );
			foreach ( var guest in guests ) guest.Hunger = guest.Thirst = guest.Toilet = 0;
			park.Ride.HostStep();
			park.Script.Advance( Tick );
		}
		Assert.IsTrue( guests.All( guest => guest.State == GuestState.Queueing ), string.Join( ", ", guests.Select( guest => $"{guest.Id}: {guest.State}" ) ) );
		var overflow = guests.Where( guest => park.Ride.GetQueuePosition( guest.Id ) >= 4 ).ToArray();
		Assert.IsTrue( overflow.Any( guest => guest.Id % 3 == 0 ) );
		Assert.IsTrue( overflow.Any( guest => guest.Id % 3 != 0 ) );
		Assert.IsTrue( overflow.All( guest => Removed.Contains( guest.Navigation.Cell ) ) );
		Assert.AreEqual( 0, park.Ride.CalledGuest );
		foreach ( var guest in overflow ) guest.Thought = GuestThought.Bored;
		var cells = overflow.ToDictionary( guest => guest.Id, guest => guest.Navigation.Cell );
		Undo( park );
		Sync( park );
		foreach ( var guest in overflow )
		{
			Assert.AreEqual( guest.Id % 3 == 0 ? GuestThought.Angry : GuestThought.Bored, guest.Thought );
			Assert.AreEqual( GuestState.DoingNothing, guest.State );
			Assert.AreEqual( cells[guest.Id], guest.Navigation.Cell, "dismissed where it stood" );
		}
	}

	[TestMethod]
	public void AGuestDismissedOnAnEmptiedCellDecidesFromThereByTheOriginalsWander()
	{
		// tp.exe 0x4F9490 from a cell without links: the step count is drawn, then the search around the start (0x522810 below 1); the wander is the ported one (GuestWander.OffPath), so the dismissed guests start a walk from the emptied cell
		var park = NewPark();
		var occupants = Seed( park );
		foreach ( var guest in new[] { occupants.Early, occupants.Target, occupants.Nondivisible } ) guest.Hunger = guest.Thirst = guest.Toilet = 0;
		Undo( park );
		Sync( park );
		var dismissed = new[] { occupants.Target, occupants.Nondivisible };
		Assert.IsTrue( dismissed.All( guest => guest.State == GuestState.DoingNothing && guest.Navigation.Cell == (2, 4) ) );
		var walked = new HashSet<int>();
		for ( var tick = 0; tick < 600; tick++ )
		{
			park.Guests.Tick( Tick );
			foreach ( var guest in park.Guests.Guests ) guest.Hunger = guest.Thirst = guest.Toilet = 0;
			park.Ride.HostStep();
			park.Script.Advance( Tick );
			foreach ( var guest in dismissed.Where( guest => guest.State == GuestState.WalkingAround ) ) walked.Add( guest.Id );
		}
		Assert.AreEqual( 2, walked.Count, "both guests started a wander walk from the emptied cell" );
		Assert.IsTrue( dismissed.All( guest => guest.State is GuestState.WalkingAround or GuestState.DoingNothing ) );
	}

	[TestMethod]
	public void AnEditClicksToolTakesItsSegmentsBackThroughTheNativeUndoAndACornerSurvivesItsNextSegment()
	{
		// tp.exe 0x52FE50: Back clicks the popped point and the new top in mode 0x34 and the second click clears the line between them through the cell clear with the walk's step: a cell's counter counts
		// down and a bend goes whatever it holds; the corner walk of the edit click (0x526103, 0x530120) wrote 1 on the corners and 0 elsewhere, and the cells of the segment are cleared from its end, so the
		// corner has lost its second link when its turn comes: it keeps its counter 1 - 1 = 0 and stays
		var park = NewPark();
		var ride = park.Two.Visitors;
		// the ride's anchor cell holds its object, which the native refund reads to find the owner
		park.Grid.Cells.SetType( 3, 3, ParkCellTypes.Object );
		Assert.AreEqual( 4, QueuePaths.TryExtend( park.Grid, ride, new[] { (10, 4), (10, 3), (11, 3), (12, 3) } ) );
		QueuePaths.TryConnect( park.Grid, ride );
		foreach ( var (x, y) in new[] { (10, 4), (10, 3), (11, 3), (12, 3) } ) park.Grid.Cells.SetPlacementCount( x, y, 5 );
		Assert.IsTrue( park.Level.EditQueueAt( 12, 3 ), "the edit click enters the tool" );
		Assert.AreEqual( 2, park.Level.CellTool.SegmentCount );
		Assert.AreEqual( (short)1, park.Grid.Cells.PlacementCountAt( 10, 3 ), "the walk wrote 1 on the corner" );
		Assert.AreEqual( (short)0, park.Grid.Cells.PlacementCountAt( 11, 3 ), "and 0 on a cell that is no corner" );
		var balance = park.Economy.Balance;
		var refund = park.Economy.CellCost( CellPurchase.Queue ) * park.Economy.ScrapPercent( park.State ) / 100;

		Assert.IsTrue( park.Level.CellTool.Undo() );
		var first = park.Level.Commands.Log[^1].Command as UndoQueueSegmentCommand;
		Assert.IsNotNull( first, "the segments of the edit click's tool go through the native undo" );
		Assert.AreEqual( ((10, 3), (12, 3)), ((first!.StartX, first.StartY), (first.EndX, first.EndY)) );
		Assert.AreEqual( ParkCellType.Empty, park.Grid.Cells.TypeAt( 12, 3 ) );
		Assert.AreEqual( ParkCellType.Empty, park.Grid.Cells.TypeAt( 11, 3 ) );
		Assert.AreEqual( ParkCellType.Queue, park.Grid.Cells.TypeAt( 10, 3 ), "the corner stays: its next segment is gone and its counter fell to 0" );
		Assert.AreEqual( (short)0, park.Grid.Cells.PlacementCountAt( 10, 3 ) );
		Assert.AreEqual( balance + 2 * refund, park.Economy.Balance );

		Assert.IsTrue( park.Level.CellTool.Undo() );
		var second = park.Level.Commands.Log[^1].Command as UndoQueueSegmentCommand;
		Assert.IsNotNull( second );
		Assert.AreEqual( ((10, 5), (10, 3)), ((second!.StartX, second.StartY), (second.EndX, second.EndY)) );
		Assert.AreEqual( ParkCellType.Empty, park.Grid.Cells.TypeAt( 10, 3 ) );
		Assert.AreEqual( ParkCellType.Empty, park.Grid.Cells.TypeAt( 10, 4 ) );
		Assert.AreEqual( ParkCellType.Queue, park.Grid.Cells.TypeAt( 10, 5 ), "the NoModify front stays" );
		Assert.AreEqual( balance + 4 * refund, park.Economy.Balance );
		CollectionAssert.AreEqual( new[] { (10, 5) }, ride.QueueCells.ToArray() );
	}

	private static void Settle( Park park ) =>
		typeof( Level ).GetMethod( "SettleDemolishedQueue", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( park.Level, new object[] { park.One } );

	[TestMethod]
	public void ADemolitionDebitsOneCellForAFrontThatHoldsNoQueueCellAndNothingForNoFront()
	{
		// tp.exe 0x527FB4: +0x40 counts the chain from the front whatever its type; Back clears nothing here and the demolition still debits one cell; no front (0x4DE040 writes 0) has a count of 0
		var park = NewPark();
		var ride = park.Ride;
		ride.QueueEntryResolver = () => (2, 6);
		foreach ( var (x, y) in Removed ) park.Grid.ClearQueue( x, y );
		park.Grid.ClearQueue( 2, 5 );
		park.Grid.SetPath( 2, 5, true );
		park.Grid.Cells.SetLinks( 2, 6, GuestPathGrid.LinkValue( 0 ) );
		var debit = park.Economy.CellCost( CellPurchase.Queue ) * park.Economy.ScrapPercent( park.State ) / 100;
		Assert.IsTrue( debit > 0 );
		var balance = park.Economy.Balance;
		Settle( park );
		Assert.AreEqual( balance - debit, park.Economy.Balance, "one cell's refund is debited" );

		park.Grid.Cells.SetLinks( 2, 6, 0 );
		balance = park.Economy.Balance;
		Settle( park );
		Assert.AreEqual( balance, park.Economy.Balance, "an entry without a link bit has no front and no count" );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CalledAndWaitingGuestsOnTheRetainedCellStayThere( bool called )
	{
		var park = NewPark();
		var occupants = Seed( park );
		var guest = occupants.Early; // position zero, divisible id
		guest.State = called ? GuestState.Boarding : GuestState.WaitingToBoard;
		Put( guest, (2, 5) );
		if ( called ) park.Ride.RestoreSnapshot( park.Ride.CaptureSnapshot() with { CalledGuest = guest.Id } );
		Undo( park );
		Sync( park );
		Assert.AreEqual( called ? GuestState.Boarding : GuestState.WaitingToBoard, guest.State );
		Assert.AreEqual( (2, 5), guest.Cell );
		Assert.AreEqual( 0, park.Ride.GetQueuePosition( guest.Id ) );
		Assert.AreEqual( GuestThought.Bored, guest.Thought );
		Assert.AreEqual( 80f, guest.Happiness );
	}

	[TestMethod]
	public void AnotherRidesOverflowOnTheRemovedCellsIsNotTouched()
	{
		var park = NewPark();
		var own = Seed( park );
		var other = Seed( park, park.Two.Visitors );
		Assert.IsTrue( park.Two.Visitors.GetQueuePosition( other.Target.Id ) >= park.Two.Visitors.QueueRoom );
		Undo( park );
		Sync( park );
		Assert.AreEqual( GuestThought.Angry, own.Target.Thought );
		Assert.AreEqual( GuestThought.Bored, other.Target.Thought );
		Assert.AreEqual( GuestState.Queueing, other.Target.State, "the other ride's queue was not edited" );
		Assert.AreEqual( park.Two.Visitors.AttractionId, other.Target.AttractionId );
		Assert.AreEqual( (2, 4), other.Target.Cell );
	}

	[TestMethod]
	public void AStrayCellCommandHasNoEditedRideThoughtOrRefund()
	{
		var park = NewPark();
		park.Grid.SetQueue( 20, 3, -1 );
		park.Grid.SetPath( 20, 2, true );
		var occupants = Seed( park, park.Two.Visitors, (20, 3) );
		Assert.IsNull( Level.QueueRideAt( park.Grid, park.Guests.Attractions, 20, 3 ) );
		var balance = park.Economy.Balance;
		var result = park.Level.Commands.Issue( new RemoveQueueCellCommand( 20, 3 ) );
		Assert.IsTrue( result.Accepted );
		Assert.AreEqual( 1, result.As<int>() );
		Assert.AreEqual( balance, park.Economy.Balance );
		Assert.AreEqual( GuestThought.Bored, occupants.Target.Thought );
		Assert.AreEqual( GuestState.DoingNothing, occupants.Target.State );
		Assert.AreEqual( (20, 2), occupants.Target.Cell );
	}

	[TestMethod]
	public void ThePublicSetDownEntryKeepsItsNoNewThoughtContract()
	{
		var park = NewPark();
		var occupants = Seed( park );
		foreach ( var (x, y) in Removed ) park.Grid.ClearQueue( x, y );
		park.Ride.RecomputeQueue( park.Grid );
		Assert.AreEqual( 3, park.Guests.SetDownOffQueueCells( Removed.ToHashSet() ) );
		Assert.AreEqual( GuestThought.Bored, occupants.Target.Thought );
	}

	[TestMethod]
	public void AnUnlistedRemovedCellOccupantIsSentAwayInPlaceByTheReevaluation()
	{
		var park = NewPark();
		var occupants = Seed( park );
		Assert.IsTrue( park.Ride.LeaveQueue( occupants.Target.Id ) );
		Assert.AreEqual( GuestState.Queueing, occupants.Target.State, "synthetic missing membership, as in an inconsistent imported guest" );
		Undo( park );
		Assert.AreEqual( GuestState.Queueing, occupants.Target.State, "the clear writes no guest" );
		Sync( park );
		Assert.AreEqual( GuestState.DoingNothing, occupants.Target.State );
		Assert.AreEqual( (2, 4), occupants.Target.Cell );
	}

	[TestMethod]
	public void RecordedUndoReplaysTheSameThoughtCellsMoneyAndGuestState()
	{
		var recorded = NewPark();
		var twin = NewPark();
		var own = Seed( recorded );
		var other = Seed( twin );
		var events = new Events();
		twin.Guests.Observer = events;
		Assert.AreEqual( recorded.Level.ComputeStateHash(), twin.Level.ComputeStateHash() );
		Undo( recorded );
		ParkCommandResult? replayResult = null;
		twin.Level.Commands.Applied += (_, result) => replayResult = result;
		ParkCommandReplay.Run( twin.Level.Commands, recorded.Level.Commands.Log, () => twin.Level.Commands.NextTick,
			() => twin.Level.AdvanceSimulation( FixedStepClock.TickDuration ), recorded.Level.Commands.NextTick );
		Assert.IsTrue( replayResult?.Accepted == true );
		Assert.AreEqual( recorded.Level.ComputeStateHash(), twin.Level.ComputeStateHash() );
		Assert.AreEqual( recorded.Economy.Balance, twin.Economy.Balance );
		CollectionAssert.AreEqual( recorded.Ride.Queue.ToArray(), twin.Ride.Queue.ToArray() );
		CollectionAssert.AreEqual( recorded.Level.Commands.Log.ToArray(), twin.Level.Commands.Log.ToArray() );
		for ( var tick = 0; tick < 3; tick++ )
		{
			recorded.Level.AdvanceSimulation( FixedStepClock.TickDuration );
			twin.Level.AdvanceSimulation( FixedStepClock.TickDuration );
			Assert.AreEqual( recorded.Level.ComputeStateHash(), twin.Level.ComputeStateHash(), $"tick {tick + 1}" );
		}
		Assert.AreEqual( 1, events.Ends.Count( item => item.Id == other.Target.Id ) );
		Assert.AreEqual( GuestThought.Angry, other.Target.Thought, "the re-evaluation of the replayed undo gave the same thought" );
		Assert.AreEqual( own.Target.Thought, other.Target.Thought );
	}

	[TestMethod]
	public void RefusedUndoChangesNoGuestCellMembershipOrMoney()
	{
		var park = NewPark();
		var occupants = Seed( park );
		park.Level.Commands.Rights = new NoUndo();
		var before = park.Level.ComputeStateHash();
		var balance = park.Economy.Balance;
		Undo( park, accepted: false );
		Assert.AreEqual( before, park.Level.ComputeStateHash() );
		Assert.AreEqual( balance, park.Economy.Balance );
		Assert.AreEqual( GuestThought.Bored, occupants.Target.Thought );
		Assert.AreEqual( 4, park.Ride.GetQueuePosition( occupants.Target.Id ) );
	}

	private sealed class NoUndo : IParkCommandRights
	{
		public bool Allows( int player, ParkCommandKind kind ) => kind != ParkCommandKind.UndoLine;
	}

	private sealed class Events : IGuestObserver
	{
		private readonly RideVisitorBridge? ride;
		public Events( RideVisitorBridge? ride = null ) => this.ride = ride;
		public readonly List<(int Id, QueueEndReason Reason)> Ends = new();
		public readonly List<(int Id, GuestThought Thought)> Thoughts = new();
		public readonly Dictionary<int, int> PositionsAtExit = new();
		public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns )
		{
			Ends.Add( (guest.Id, reason) );
			if ( ride != null ) PositionsAtExit[guest.Id] = ride.GetQueuePosition( guest.Id );
		}
		public void Thought( Guest guest, GuestThought thought ) => Thoughts.Add( (guest.Id, thought) );
		public void Admitted( Guest guest, int fee ) { }
		public void Spent( Guest guest, int amount, int attractionId ) { }
		public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) { }
		public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
		public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { }
		public void Decided( Guest guest, bool chose ) { }
		public void Leaving( Guest guest, DepartureCause cause ) { }
		public void Departed( Guest guest ) { }
		public void Recalled( Guest guest ) { }
		public void Restored( GuestSimulation simulation ) { }
		public void Ticked( GuestSimulation simulation, float deltaSeconds ) { }
	}
}
