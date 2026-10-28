using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-undo] The host's undo journal on its own (docs/COOP.md, "Conflicts and undo (C5)"), on the stream tests' asset-free park, wired as the host's stream wires it: the map's
/// setters report real changes only; a step's record leaves the cells where its take-back reversed its effects, wherever it stands in their lists, and stays as its member's residue
/// where an effect stayed, which only that member's earlier step clearing the cell whole passes; records of no step stay as one permanent marker; a departure keeps its member's records;
/// the bounds forget the oldest steps; and only the building tools' accepted steps are steps.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CoopUndoJournalTests
{
	/// <summary>The host's own member id in these tests.</summary>
	private const uint Host = 1000;

	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private List<Entity>? entities;
	private Level? current;
	private readonly List<List<Entity>> built = new();

	[TestInitialize]
	public void Initialize()
	{
		entities = Entity.All;
		current = Level.Current;
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var list in built )
		{
			Entity.All = list;
			foreach ( var entity in list.ToArray() )
				entity.Delete();
		}
		Entity.All = new List<Entity>();
		fixture?.Cleanup();
		renderer?.Dispose();
		if ( entities != null )
			Entity.All = entities;
		Level.Current = current!;
	}

	/// <summary>The stream tests' park, entered, with the host's journal wired to its commands and cells as the host's stream wires it, each record numbered as the stream numbers it.</summary>
	private sealed class Journaled
	{
		public required Level Level { get; init; }
		public required CoopUndoJournal Journal { get; init; }
		public OriginalObject One { get; init; } = null!;
		private uint? member;
		private long next = 1;
		public long LastRecord { get; private set; }

		public static Journaled On( Level level, OriginalObject one )
		{
			var journal = new CoopUndoJournal( level, Host );
			var journaled = new Journaled { Level = level, Journal = journal, One = one };
			level.Commands.Prepare = command => journal.Decide( command, journaled.member );
			level.Commands.Starting = journal.Begin;
			level.Commands.Applied += ( entry, result ) =>
			{
				journaled.LastRecord = journaled.next++;
				journal.Applied( journaled.LastRecord, journaled.member, entry.Command, result );
			};
			level.Guests!.Grid.Cells.Changed = journal.Changed;
			return journaled;
		}

		/// <summary><paramref name="command"/> as member <paramref name="id"/>'s (null for the host's), of seat <paramref name="id"/> (0 for the host): its result and its record.</summary>
		public (ParkCommandResult Result, long Record) Run( uint? id, ParkCommand command )
		{
			member = id;
			try
			{
				var result = Level.Commands.Apply( command with { Player = (int)(id ?? 0) % 64 } );
				return (result, LastRecord);
			}
			finally
			{
				member = null;
			}
		}

		/// <summary>Member <paramref name="id"/>'s take-back of the step of <paramref name="record"/>: the verdict the host decided.</summary>
		public TakeBackVerdict TakeBack( uint? id, long record )
		{
			Run( id, new TakeBackCommand( false, record, -1, -1, -1 ) );
			return Level.Commands.Log.Select( entry => entry.Command ).OfType<TakeBackCommand>().Last().Verdict;
		}

		public long[] History( int x, int y ) => Journal.HistoryAt( x, y )?.ToArray() ?? Array.Empty<long>();

		/// <summary>[EXT:online-coop-undo] Member <paramref name="id"/>'s take-back of its pool stroke named <paramref name="stroke"/> (C5-2): the verdict the host decided.</summary>
		public TakeBackVerdict TakeBackStroke( uint? id, long stroke )
		{
			Run( id, new TakeBackCommand( true, stroke, -1, -1, -1 ) );
			return LastTakeBack.Verdict;
		}

		/// <summary>The take-back the host decided last, as its record holds it.</summary>
		public TakeBackCommand LastTakeBack => Level.Commands.Log.Select( entry => entry.Command ).OfType<TakeBackCommand>().Last();

		public ParkEconomy Economy => Level.Park!.Economy;

		public ParkCellMap Cells => Level.Guests!.Grid.Cells;

		public bool IsWater( int x, int y ) => Cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater;
	}

	private Journaled NewJournaled()
	{
		var park = CoopStreamTests.BuildPark( fixture! );
		built.Add( park.Entities );
		Entity.All = park.Entities;
		Level.Current = park.Level;
		return Journaled.On( park.Level, park.One );
	}

	/// <summary>The queue tests' synthetic queue of five cells for the park's ride, from (10, 6) up to its back cell (10, 2) (as the session tests build it).</summary>
	private static void QueueOf( Journaled journaled )
	{
		var grid = journaled.Level.Guests!.Grid;
		var ride = journaled.One.Visitors;
		ride.EntranceCell = ride.ExitCell = (10, 1);
		ride.QueueFrontCell = (10, 6);
		ride.QueueEntranceDirection = 2;
		ride.OwnerWord = ParkCellMap.CellWord( journaled.One.Placement.X, journaled.One.Placement.Y );
		ride.Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 );
		journaled.Level.Guests.Grid.Cells.Changed = null;
		Assert.AreEqual( 5, QueuePaths.TryExtend( grid, ride, Enumerable.Range( 0, 5 ).Select( index => (10, 6 - index) ) ) );
		QueuePaths.TryConnect( grid, ride );
		grid.Cells.SetFlags( 10, 6, grid.Cells.FlagsAt( 10, 6 ) | ParkCellFlags.NoModify );
		grid.Cells.SetPlacementCount( 10, 6, 1 );
		grid.Cells.SetType( 10, 7, ParkCellTypes.Entrance );
		grid.Cells.SetLinks( 10, 7, GuestPathGrid.LinkValue( 0 ) );
		grid.Cells.SetQueueLink( 10, 7, GuestPathGrid.LinkValue( 0 ) );
		grid.Cells.Changed = journaled.Journal.Changed;
	}

	private static QueueSegmentCommand Segment( Journaled journaled, int startX, int startY, int endX, int endY ) =>
		new( journaled.One.Visitors.AttractionId, startX, startY, endX, endY );

	private static LayLineCommand Line( int startX, int startY, int endX, int endY ) => new( CellToolMode.Path, -1, startX, startY, endX, endY );

	[TestMethod]
	public void TheMapsSettersReportOnlyARealChange()
	{
		var journaled = NewJournaled();
		var cells = journaled.Level.Guests!.Grid.Cells;
		var seen = new List<(int X, int Y, CellFields Before)>();
		cells.Changed = ( x, y, before ) => seen.Add( (x, y, before) );
		cells.SetType( 30, 30, ParkCellType.Path );
		cells.SetType( 30, 30, ParkCellType.Path );
		cells.SetLinks( 30, 30, cells.LinksAt( 30, 30 ) );
		cells.SetFlags( 30, 30, cells.FlagsAt( 30, 30 ) );
		cells.SetPlacementCount( 30, 30, cells.PlacementCountAt( 30, 30 ) );
		cells.SetOwner( 30, 30, cells.OwnerAt( 30, 30 ) );
		cells.SetQueueLink( 30, 30, cells.QueueLinkAt( 30, 30 ) );
		Assert.AreEqual( 1, seen.Count, "a value written again unchanged is no change" );
		Assert.AreEqual( 0, seen[0].Before.Type, "with the fields from before" );
		cells.SetLinks( 30, 30, 4 );
		Assert.AreEqual( 2, seen.Count );
		Assert.AreEqual( (byte)ParkCellType.Path, seen[1].Before.Type );
		Assert.AreEqual( 0, seen[1].Before.Links );
		cells.Changed = null;
	}

	[TestMethod]
	public void AStepsRecordLeavesItsCellsListWhereverItStandsWhenItsTakeBackReversesIt()
	{
		var journaled = NewJournaled();
		var a = journaled.Run( 1, Line( 5, 20, 12, 20 ) ).Record;
		// two later lines link into (8, 20) from above and from below
		var b = journaled.Run( 2, Line( 8, 19, 8, 14 ) ).Record;
		var c = journaled.Run( 3, Line( 8, 21, 8, 26 ) ).Record;
		CollectionAssert.AreEqual( new[] { a, b, c }, journaled.History( 8, 20 ) );
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBack( 1, a ) );
		// the earlier of them taken back first: its record leaves the list from the middle
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 2, b ) );
		CollectionAssert.AreEqual( new[] { a, c }, journaled.History( 8, 20 ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 3, c ) );
		CollectionAssert.AreEqual( new[] { a }, journaled.History( 8, 20 ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, a ) );
		Assert.IsNull( journaled.Journal.HistoryAt( 8, 20 ), "no step tracks the cell any more" );
		Assert.AreEqual( 0, journaled.Journal.StepsOf( 1 ) + journaled.Journal.StepsOf( 2 ) + journaled.Journal.StepsOf( 3 ) );
	}

	[TestMethod]
	public void RecordsOfNoStepStayAsOnePermanentMarkerAndAListStaysShortUnderRepeatedEdits()
	{
		var journaled = NewJournaled();
		var a = journaled.Run( 1, Line( 5, 20, 12, 20 ) ).Record;
		// another member's line linking into (10, 20), laid and taken back again and again: the list stays the step's alone
		for ( var round = 0; round < 20; round++ )
		{
			var line = journaled.Run( 3, Line( 10, 21, 10, 24 ) ).Record;
			Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 3, line ), $"round {round}" );
		}
		CollectionAssert.AreEqual( new[] { a }, journaled.History( 10, 20 ) );
		// two lines into (8, 20), then the host's Backspace with no tool takes both cells beside it off: two records of no step, kept as one marker
		var above = journaled.Run( 2, Line( 8, 19, 8, 14 ) ).Record;
		var below = journaled.Run( 2, Line( 8, 21, 8, 26 ) ).Record;
		Assert.IsTrue( journaled.Run( null, new BacktrackCommand( 8, 19 ) ).Result.Accepted );
		Assert.IsTrue( journaled.Run( null, new BacktrackCommand( 8, 21 ) ).Result.Accepted );
		CollectionAssert.AreEqual( new[] { a, above, below, -1L }, journaled.History( 8, 20 ) );
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBack( 1, a ), "a permanent marker after it: never" );
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBack( 2, below ), "the host's removal stands on its cell too" );
	}

	[TestMethod]
	public void ADepartureDropsItsMembersStepsAndKeepsTheirRecordsForGood()
	{
		var journaled = NewJournaled();
		var a = journaled.Run( 1, Line( 5, 20, 12, 20 ) ).Record;
		var b = journaled.Run( 2, Line( 8, 19, 8, 14 ) ).Record;
		journaled.Journal.Depart( 2 );
		Assert.AreEqual( 0, journaled.Journal.StepsOf( 2 ) );
		CollectionAssert.AreEqual( new[] { a, -1L }, journaled.History( 8, 20 ), "its record turned permanent" );
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBack( 1, a ) );
		// a new member in the departed one's seat owns none of its steps
		Assert.AreEqual( TakeBackVerdict.NotOwn, journaled.TakeBack( 66, b ), "seat 2's new member" );
		Assert.IsTrue( journaled.Level.Guests!.Grid.IsWalkable( 8, 16 ), "nothing taken" );
		// the session's end empties the journal
		journaled.Journal.Clear();
		Assert.AreEqual( 0, journaled.Journal.StepsOf( 1 ) );
		Assert.IsNull( journaled.Journal.HistoryAt( 8, 20 ) );
	}

	[TestMethod]
	public void TheBoundsForgetAMembersOldestStepsWhoseTakeBackThenSaysSo()
	{
		var journaled = NewJournaled();
		journaled.Journal.StepBound = 2;
		var first = journaled.Run( 1, Line( 5, 20, 12, 20 ) ).Record;
		var second = journaled.Run( 1, Line( 5, 24, 12, 24 ) ).Record;
		var third = journaled.Run( 1, Line( 5, 28, 12, 28 ) ).Record;
		Assert.AreEqual( 2, journaled.Journal.StepsOf( 1 ) );
		Assert.AreEqual( TakeBackVerdict.Forgotten, journaled.TakeBack( 1, first ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, third ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, second ) );
		Assert.IsTrue( journaled.Level.Guests!.Grid.IsWalkable( 8, 20 ), "the forgotten line stays" );
		// a step heavier than the cell bound is forgotten at once
		journaled.Journal.CellBound = 4;
		var heavy = journaled.Run( 2, Line( 5, 16, 12, 16 ) );
		Assert.IsTrue( heavy.Result.Accepted );
		Assert.AreEqual( 0, journaled.Journal.StepsOf( 2 ) );
		Assert.AreEqual( TakeBackVerdict.Forgotten, journaled.TakeBack( 2, heavy.Record ) );
	}

	[TestMethod]
	public void AnOwnTurningChainGoesBackStepByStepThoughTheBendsClearingUnlinksTheEarlierSegment()
	{
		var journaled = NewJournaled();
		QueueOf( journaled );
		var first = journaled.Run( 1, Segment( journaled, 10, 2, 8, 2 ) );
		var second = journaled.Run( 1, Segment( journaled, 8, 2, 8, 4 ) );
		Assert.IsTrue( first.Result.Accepted && second.Result.Accepted );
		// the second's undo clears the bend corner (8, 2) the two share and unlinks the first's cell (9, 2) beside it: that own chain's write adds no marker
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, second.Record ) );
		CollectionAssert.AreEqual( new[] { first.Record }, journaled.History( 9, 2 ), "no marker on the earlier segment's cell" );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, first.Record ), "the earlier one goes back too" );
		Assert.AreEqual( 0, journaled.Journal.StepsOf( 1 ) );
	}

	[TestMethod]
	public void AnotherPlayersSegmentFromTheBendCornerSinceTheEarlierSegmentPutsTheBendsClearingInTheWay()
	{
		var journaled = NewJournaled();
		QueueOf( journaled );
		var cells = journaled.Level.Guests!.Grid.Cells;
		var first = journaled.Run( 1, Segment( journaled, 10, 2, 8, 2 ) ).Record;
		// another member's segment up from the first's end (8, 2), then the first's member's segment down from it
		var up = journaled.Run( 2, Segment( journaled, 8, 2, 8, 0 ) );
		Assert.IsTrue( up.Result.Accepted, up.Result.Reason );
		var second = journaled.Run( 1, Segment( journaled, 8, 2, 8, 4 ) );
		Assert.IsTrue( second.Result.Accepted, second.Result.Reason );
		var fields = (cells.FieldsAt( 8, 2 ), cells.FieldsAt( 8, 3 ), cells.FieldsAt( 8, 4 ), cells.FieldsAt( 8, 1 ));
		// the second's undo walks up to (8, 2) and finds it a bend (toward (9, 2) and the other member's (8, 1)), which it clears whatever its counter: the other member's segment would lose
		// its corner, so the bend's clearing is in the way, nothing changed
		Assert.AreEqual( TakeBackVerdict.InTheWay, journaled.TakeBack( 1, second.Record ) );
		Assert.AreEqual( fields, (cells.FieldsAt( 8, 2 ), cells.FieldsAt( 8, 3 ), cells.FieldsAt( 8, 4 ), cells.FieldsAt( 8, 1 )) );
		// nor does the other member's segment go: the second counted the corner up since, and its own undo would clear that corner too (a bend toward (9, 2) and (8, 3)); each waits on
		// the other, as no take-back undoes over another player's work, and the removal under the pointer stays for them
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBack( 2, up.Record ) );
		Assert.AreEqual( fields, (cells.FieldsAt( 8, 2 ), cells.FieldsAt( 8, 3 ), cells.FieldsAt( 8, 4 ), cells.FieldsAt( 8, 1 )) );
		Assert.AreEqual( TakeBackVerdict.NotLast, journaled.TakeBack( 1, first ) );
	}

	[TestMethod]
	public void AnotherPlayersSegmentOnTheEarlierCellBesideTheBendStillRefusesItUntilTakenBack()
	{
		var journaled = NewJournaled();
		QueueOf( journaled );
		var first = journaled.Run( 1, Segment( journaled, 10, 2, 8, 2 ) ).Record;
		var second = journaled.Run( 1, Segment( journaled, 8, 2, 8, 4 ) ).Record;
		// another member's segment down from the first's cell (9, 2): that cell counted up and linked
		var other = journaled.Run( 2, Segment( journaled, 9, 2, 9, 4 ) );
		Assert.IsTrue( other.Result.Accepted, other.Result.Reason );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, second ), "its own cells are untouched" );
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBack( 1, first ), "the other member's segment still stands on (9, 2)" );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 2, other.Record ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, first ), "then taken" );
	}

	[TestMethod]
	public void ALineThatEndsItsToolIsNoStepAndTheLineItCrossedStaysChangedForGood()
	{
		var journaled = NewJournaled();
		var first = journaled.Run( 1, Line( 5, 20, 12, 20 ) ).Record;
		// another member's line over the same cells builds nothing and raises every crossing; it ends on a path, so it ends its tool, as in the original: no take-back reaches it
		var over = journaled.Run( 2, Line( 5, 20, 12, 20 ) );
		Assert.IsTrue( over.Result.Accepted && over.Result.Value is SegmentResult { Built.Count: 0, EndedOnExisting: true }, "it built nothing" );
		Assert.AreEqual( 0, journaled.Journal.StepsOf( 2 ), "no step" );
		Assert.AreEqual( TakeBackVerdict.NotOwn, journaled.TakeBack( 2, over.Record ), "not even by a request made by hand" );
		CollectionAssert.AreEqual( new[] { first, -1L }, journaled.History( 8, 20 ), "its record stays for good" );
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBack( 1, first ) );
		Assert.IsTrue( journaled.Level.Guests!.Grid.IsWalkable( 8, 20 ) );
		// a line that ends on a cell of the first one ends its tool too, whatever it built
		var joining = journaled.Run( 3, Line( 9, 16, 9, 20 ) );
		Assert.IsTrue( joining.Result.Value is SegmentResult { Completed: true, EndedOnExisting: true, Built.Count: 4 } );
		Assert.AreEqual( 0, journaled.Journal.StepsOf( 3 ) );
	}

	[TestMethod]
	public void AGuestsSegmentItsRecordCutShortEndsItsToolAndIsNoStep()
	{
		var journaled = NewJournaled();
		QueueOf( journaled );
		var grid = journaled.Level.Guests!.Grid;
		grid.Cells.Changed = null;
		// a queue cell of the ride's on the line, past where the money will stop it
		grid.SetQueue( 7, 2, towards: GuestPathGrid.DirectionBetween( 7, 2, 6, 2 ) );
		grid.Cells.Changed = journaled.Journal.Changed;
		var economy = journaled.Level.Park!.Economy;
		economy.Ledger.Post( LedgerCategory.OtherCosts, economy.Balance - (2 * economy.CellCost( CellPurchase.Queue ) - 1) );
		var counter = grid.Cells.PlacementCountAt( 7, 2 );
		var segment = journaled.Run( 1, Segment( journaled, 10, 2, 6, 2 ) );
		Assert.IsTrue( segment.Result.Value is SegmentResult { StoppedBy: CellBuildResult.NotEnoughMoney, Built.Count: 1 }, "one cell built, stopped before (7, 2)" );
		Assert.AreEqual( counter, grid.Cells.PlacementCountAt( 7, 2 ), "the commit's type pass never reached it (its link passes did)" );
		var queue = grid.Cells.FieldsAt( 7, 2 );
		economy.Ledger.Post( LedgerCategory.OtherIncome, 2_000_000 );
		// a guest's line its record cut short ends its tool (CellBuildTool.CompleteWaiting), as the click would have on the refused ghost: no step, no take-back
		Assert.AreEqual( 0, journaled.Journal.StepsOf( 1 ) );
		Assert.AreEqual( TakeBackVerdict.NotOwn, journaled.TakeBack( 1, segment.Record ) );
		Assert.AreEqual( queue, grid.Cells.FieldsAt( 7, 2 ) );
		Assert.AreEqual( (byte)ParkCellType.Queue, grid.Cells.RawTypeAt( 9, 2 ), "its own cell stays" );
	}

	[TestMethod]
	public void AStepWhoseCellAnotherCommandEmptiedIsRefusedEvenAfterThatOneIsTakenBack()
	{
		var journaled = NewJournaled();
		QueueOf( journaled );
		// a path line down across row 2, then another member's native segment along the row paving its cell (8, 2), then taken back: the cell is left empty
		var path = journaled.Run( 1, Line( 8, 0, 8, 4 ) ).Record;
		var segment = journaled.Run( 2, Segment( journaled, 10, 2, 6, 2 ) );
		Assert.IsTrue( segment.Result.Accepted, segment.Result.Reason );
		Assert.AreEqual( (byte)ParkCellType.Queue, journaled.Level.Guests!.Grid.Cells.RawTypeAt( 8, 2 ), "paved" );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 2, segment.Record ) );
		Assert.AreEqual( 0, journaled.Level.Guests.Grid.Cells.RawTypeAt( 8, 2 ), "the native undo left the paved cell empty" );
		// the segment's take-back did not reverse its effect there (the cell was a path before it, it is empty now), so its record stays: someone changed that since
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBack( 1, path ), "the path line's cell is gone" );
		Assert.IsTrue( journaled.Level.Guests.Grid.IsWalkable( 8, 0 ), "nothing taken" );
	}

	[TestMethod]
	public void AFaultTurnsTheJournalOffAndNoTakeBackIsDecidedOnIncompleteHistories()
	{
		var journaled = NewJournaled();
		var line = journaled.Run( 1, Line( 5, 20, 12, 20 ) ).Record;
		journaled.Journal.Fault = () => throw new InvalidOperationException( "a test's fault" );
		var later = journaled.Run( 1, Line( 5, 24, 12, 24 ) );
		Assert.IsTrue( later.Result.Accepted, "the command it observed is not failed" );
		Assert.IsTrue( journaled.Journal.Disabled );
		journaled.Journal.Fault = null;
		Assert.AreEqual( TakeBackVerdict.Forgotten, journaled.TakeBack( 1, line ), "off for the session: nothing is taken back" );
		Assert.AreEqual( TakeBackVerdict.Forgotten, journaled.TakeBack( 1, later.Record ) );
		Assert.IsTrue( journaled.Level.Guests!.Grid.IsWalkable( 8, 20 ) && journaled.Level.Guests.Grid.IsWalkable( 8, 24 ) );
		Assert.AreEqual( 0, journaled.Journal.StepsOf( 1 ), "and records nothing more" );
		Assert.IsTrue( journaled.Run( 1, Line( 5, 28, 12, 28 ) ).Result.Accepted, "the park goes on" );
		Assert.AreEqual( 0, journaled.Journal.StepsOf( 1 ) );
		// a fault while it decides turns it off the same way
		var fresh = NewJournaled();
		var step = fresh.Run( 1, Line( 5, 20, 12, 20 ) ).Record;
		fresh.Journal.Fault = () => throw new InvalidOperationException( "a test's fault" );
		Assert.AreEqual( TakeBackVerdict.Forgotten, fresh.TakeBack( 1, step ) );
		Assert.IsTrue( fresh.Journal.Disabled );
		Assert.IsTrue( fresh.Level.Guests!.Grid.IsWalkable( 8, 20 ) );
	}

	/// <summary>
	/// Member 1's column down from (9, 21), then its row on y 20 ending at (8, 20), diagonal to the column's top, then a column down from (8, 21), beside the first and under the row's end: the
	/// last one's link pass gives the row's (8, 20) a diagonal bit toward the first column's (9, 21) and that cell one back (the original's neighbour diagonals,
	/// <see cref="PathLinkWriter.LinkPath"/>), between two cells its take-back leaves standing. Returns the three records.
	/// </summary>
	private static (long Column, long Row, long Beside) ColumnRowAndBeside( Journaled journaled, uint besideBy )
	{
		var column = journaled.Run( 1, Line( 9, 21, 9, 24 ) ).Record;
		var row = journaled.Run( 1, Line( 5, 20, 8, 20 ) ).Record;
		Assert.AreEqual( 0, journaled.Level.Guests!.Grid.Cells.LinksAt( 8, 20 ) & 8, "no diagonal toward (9, 21) yet" );
		var beside = journaled.Run( besideBy, Line( 8, 21, 8, 24 ) );
		Assert.IsTrue( beside.Result.Accepted, beside.Result.Reason );
		Assert.AreNotEqual( 0, journaled.Level.Guests.Grid.Cells.LinksAt( 8, 20 ) & 8, "its link pass set it" );
		return (column, row, beside.Record);
	}

	[TestMethod]
	public void AnOwnLinesLeftoverDiagonalIsItsResidueWhichItsEarlierLinesPassOver()
	{
		var journaled = NewJournaled();
		var (column, row, beside) = ColumnRowAndBeside( journaled, 1 );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, beside ) );
		var cells = journaled.Level.Guests!.Grid.Cells;
		Assert.AreNotEqual( 0, cells.LinksAt( 8, 20 ) & 8, "the clear unlinks the removed cells alone: the diagonal stays, as in single player" );
		CollectionAssert.AreEqual( new[] { row, -3L }, journaled.History( 8, 20 ), "the row's record, then member 1's residue" );
		CollectionAssert.AreEqual( new[] { column, -3L }, journaled.History( 9, 21 ), "the bit back, the same on the column's cell" );
		// the row's take-back clears (8, 20) whole, the diagonal with it: its member's own residue does not stop it; its clear unlinks the column's (9, 21) too, a write of its member's own
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, row ) );
		CollectionAssert.AreEqual( new[] { column, -3L }, journaled.History( 9, 21 ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, column ) );
		Assert.IsFalse( journaled.Level.Guests.Grid.IsWalkable( 8, 20 ) || journaled.Level.Guests.Grid.IsWalkable( 9, 22 ) );
		Assert.AreEqual( 0, journaled.Journal.StepsOf( 1 ) );
	}

	[TestMethod]
	public void AnOwnResidueBesideAnotherPlayersRecordStillRefusesTheEarlierLine()
	{
		var journaled = NewJournaled();
		var (_, row, beside) = ColumnRowAndBeside( journaled, 1 );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, beside ) );
		// another member's line up from the row's (8, 20) links into it
		var other = journaled.Run( 2, Line( 8, 19, 8, 16 ) );
		Assert.IsTrue( other.Result.Accepted, other.Result.Reason );
		CollectionAssert.AreEqual( new[] { row, -3L, other.Record }, journaled.History( 8, 20 ) );
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBack( 1, row ), "the other member's line still joins it" );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 2, other.Record ) );
		CollectionAssert.AreEqual( new[] { row, -3L }, journaled.History( 8, 20 ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, row ), "its own residue alone is passed" );
	}

	[TestMethod]
	public void AnotherMembersResidueIsAChangeAndStaysOneAfterItsDeparture()
	{
		var journaled = NewJournaled();
		var (_, row, beside) = ColumnRowAndBeside( journaled, 2 );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 2, beside ) );
		CollectionAssert.AreEqual( new[] { row, -4L }, journaled.History( 8, 20 ), "member 2's residue" );
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBack( 1, row ), "no take-back undoes over another player's write" );
		// its member gone, it stays for good as the permanent marker
		journaled.Journal.Depart( 2 );
		CollectionAssert.AreEqual( new[] { row, -1L }, journaled.History( 8, 20 ) );
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBack( 1, row ) );
	}

	[TestMethod]
	public void ACellsListKeepsEachRunOfMarkersAsTheSetTheRuleReads()
	{
		// records 5, 7 and 9; residue entries −3 and −6 of member 1, −4 of member 2, −5 of member 3; the permanent marker (−1)
		uint MemberOf( long entry ) => entry == -6 ? 1u : (uint)(-2 - entry);
		var merged = new List<(long Kept, long Merged)>();
		var list = new List<long> { 5, -3, -4, -6, 7, -3, -1, -4, 9, -1, -1, -5 };
		CoopUndoJournal.Normalize( list, MemberOf, ( kept, gone ) => merged.Add( (kept, gone) ) );
		CollectionAssert.AreEqual( new long[] { 5, -3, -4, 7, -1, 9, -1 }, list, "each member's residue once in a run, the later merged into it; a run with the permanent marker that marker alone" );
		CollectionAssert.AreEqual( new[] { (-3L, -6L), (-1L, -3L), (-1L, -4L), (-1L, -5L) }, merged,
			"within a run only: the residue after the record 7 stays apart; a permanent marker takes in what every marker of its run left" );
		var permanentFirst = new List<long> { -1, -3, -1, 4 };
		CoopUndoJournal.Normalize( permanentFirst, MemberOf );
		CollectionAssert.AreEqual( new long[] { -1, 4 }, permanentFirst );
	}

	[TestMethod]
	public void ALinkIntoACellALineOnlyCrossedFromALineThatEndedItsToolIsNoDependency()
	{
		var journaled = NewJournaled();
		var cells = journaled.Level.Guests!.Grid.Cells;
		// member 3's column ends at (8, 20), and its path along y = 24 waits below; member 1's row crosses the column there and builds on both sides
		var column = journaled.Run( 3, Line( 8, 17, 8, 20 ) ).Record;
		Assert.IsTrue( journaled.Run( 3, Line( 5, 24, 12, 24 ) ).Result.Accepted );
		var counter = cells.PlacementCountAt( 8, 20 );
		var row = journaled.Run( 1, Line( 5, 20, 12, 20 ) );
		Assert.IsTrue( row.Result.Value is SegmentResult { Completed: true, EndedOnExisting: false } && cells.PlacementCountAt( 8, 20 ) == counter + 1, "a crossing" );
		// member 2's line down from (8, 21) links into (8, 20) from below and ends its tool on the path at (8, 24): no step, its record a permanent marker there, which left a link alone
		var below = journaled.Run( 2, Line( 8, 21, 8, 24 ) );
		Assert.IsTrue( below.Result.Value is SegmentResult { Completed: true, EndedOnExisting: true }, "it joined the path below" );
		CollectionAssert.AreEqual( new[] { column, row.Record, -1L }, journaled.History( 8, 20 ) );
		Assert.AreEqual( false, journaled.Journal.MarkerFieldsAt( 8, 20, 2 ), "a link bit alone" );
		// the row's undo counts (8, 20) down and unlinks its own cells there alone, which leaves that link as it is: no dependency, as for a line still a step
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, row.Record ) );
		Assert.AreEqual( counter, cells.PlacementCountAt( 8, 20 ) );
		Assert.IsTrue( journaled.Level.Guests.Grid.IsWalkable( 8, 22 ) && !journaled.Level.Guests.Grid.IsWalkable( 9, 20 ) );
		Assert.AreNotEqual( 0, cells.LinksAt( 8, 20 ) & GuestPathGrid.LinkValue( GuestPathGrid.DirectionBetween( 8, 20, 8, 21 ) ), "the link from below stays" );
		CollectionAssert.AreEqual( new[] { column, -1L }, journaled.History( 8, 20 ), "its record left, the marker stays" );
		// a line that ends its tool across a cell a line only crossed counts it up: a dependency for good, as decision 7 says
		var again = journaled.Run( 1, Line( 5, 20, 12, 20 ) );
		Assert.IsTrue( again.Result.Value is SegmentResult { Completed: true } );
		var through = journaled.Run( 2, Line( 8, 18, 8, 22 ) );
		Assert.IsTrue( through.Result.Value is SegmentResult { EndedOnExisting: true }, "it ends on the path it laid below" );
		Assert.AreEqual( true, journaled.Journal.MarkerFieldsAt( 8, 20, journaled.History( 8, 20 ).Length - 1 ), "its counter raised" );
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBack( 1, again.Record ) );
	}

	[TestMethod]
	public void AnotherLinkIntoACellALineOnlyCrossedIsNoDependency()
	{
		var journaled = NewJournaled();
		var cells = journaled.Level.Guests!.Grid.Cells;
		// member 3's column ends at (8, 20); member 1's row crosses it there and builds on both sides
		var column = journaled.Run( 3, Line( 8, 17, 8, 20 ) ).Record;
		var counter = cells.PlacementCountAt( 8, 20 );
		var row = journaled.Run( 1, Line( 5, 20, 12, 20 ) );
		Assert.IsTrue( row.Result.Value is SegmentResult { Completed: true, EndedOnExisting: false } && cells.PlacementCountAt( 8, 20 ) == counter + 1, "a crossing" );
		// member 2's line down from (8, 21) links into (8, 20) from below, and into nothing of the row's
		var below = journaled.Run( 2, Line( 8, 21, 8, 24 ) ).Record;
		CollectionAssert.AreEqual( new[] { column, row.Record, below }, journaled.History( 8, 20 ) );
		// the row's undo counts (8, 20) down and unlinks its own cells there alone: the line below does not depend on it
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, row.Record ) );
		Assert.AreEqual( counter, cells.PlacementCountAt( 8, 20 ) );
		Assert.IsTrue( journaled.Level.Guests.Grid.IsWalkable( 8, 22 ) && !journaled.Level.Guests.Grid.IsWalkable( 9, 20 ) );
		CollectionAssert.AreEqual( new[] { column, below }, journaled.History( 8, 20 ), "its record left" );
		// a line through one of the row's own cells is a dependency, as before
		var again = journaled.Run( 1, Line( 5, 20, 12, 20 ) );
		Assert.IsTrue( again.Result.Value is SegmentResult { Completed: true } );
		var across = journaled.Run( 2, Line( 6, 17, 6, 23 ) );
		Assert.IsTrue( across.Result.Accepted );
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBack( 1, again.Record ), "the line across its built (6, 20)" );
	}

	[TestMethod]
	public void TwoCrossingsOfOneCellGoBackNewestFirstAndLeaveNothingBehind()
	{
		var journaled = NewJournaled();
		var cells = journaled.Level.Guests!.Grid.Cells;
		// member 3's column ends at (8, 20); member 1's row crosses it there; member 2's line up from (8, 25) to (8, 15) crosses it again
		var column = journaled.Run( 3, Line( 8, 17, 8, 20 ) ).Record;
		var counter = cells.PlacementCountAt( 8, 20 );
		var row = journaled.Run( 1, Line( 5, 20, 12, 20 ) ).Record;
		var up = journaled.Run( 2, Line( 8, 25, 8, 15 ) );
		Assert.IsTrue( up.Result.Value is SegmentResult { Completed: true, EndedOnExisting: false } && cells.PlacementCountAt( 8, 20 ) == counter + 2, "a second crossing" );
		// the earlier crossing waits for the later: its counter is raised again
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBack( 1, row ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 2, up.Record ) );
		Assert.AreEqual( counter + 1, cells.PlacementCountAt( 8, 20 ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, row ) );
		Assert.AreEqual( counter, cells.PlacementCountAt( 8, 20 ) );
		CollectionAssert.AreEqual( new[] { column }, journaled.History( 8, 20 ), "both records left: each found the counter as its step left it" );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 3, column ), "then the column" );
		Assert.IsFalse( journaled.Level.Guests.Grid.IsWalkable( 8, 20 ) || journaled.Level.Guests.Grid.IsWalkable( 8, 18 ) );
	}

	[TestMethod]
	public void AnotherMembersLinkOnlyResidueOnACellALineOnlyCrossedIsNoDependency()
	{
		var journaled = NewJournaled();
		var cells = journaled.Level.Guests!.Grid.Cells;
		// member 3's row; member 1's longer row over it, crossing all of it and building two cells past its end
		journaled.Run( 3, Line( 5, 20, 12, 20 ) );
		var over = journaled.Run( 1, Line( 5, 20, 14, 20 ) );
		Assert.IsTrue( over.Result.Value is SegmentResult { Built.Count: 2, EndedOnExisting: false } );
		// member 4's column down from (9, 21) links into the crossed (9, 20); member 2's column down from (8, 21) gives the crossed (8, 20) a diagonal toward (9, 21)
		journaled.Run( 4, Line( 9, 21, 9, 24 ) );
		Assert.AreEqual( 0, cells.LinksAt( 8, 20 ) & 8 );
		var beside = journaled.Run( 2, Line( 8, 21, 8, 24 ) ).Record;
		Assert.AreNotEqual( 0, cells.LinksAt( 8, 20 ) & 8 );
		// taken back, it leaves that diagonal behind: its member's residue, of a link alone
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 2, beside ) );
		Assert.AreNotEqual( 0, cells.LinksAt( 8, 20 ) & 8 );
		CollectionAssert.Contains( journaled.History( 8, 20 ), -4L );
		// the longer row's undo only counts (8, 20) down and leaves that diagonal as it is: taken
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, over.Record ) );
		Assert.IsFalse( journaled.Level.Guests.Grid.IsWalkable( 13, 20 ) );
		Assert.IsTrue( journaled.Level.Guests.Grid.IsWalkable( 8, 20 ) && (cells.LinksAt( 8, 20 ) & 8) != 0 );
	}

	[TestMethod]
	public void TwoResiduesOfOneMemberOnEitherSideOfAnotherStepKeepWhatEachLeft()
	{
		var journaled = NewJournaled();
		var cells = journaled.Level.Guests!.Grid.Cells;
		// member 3's row; member 1's longer row over it, crossing (8, 20); member 4's column down from (9, 21)
		var row = journaled.Run( 3, Line( 5, 20, 12, 20 ) ).Record;
		var over = journaled.Run( 1, Line( 5, 20, 14, 20 ) ).Record;
		journaled.Run( 4, Line( 9, 21, 9, 24 ) );
		// member 2's column down from (8, 21), taken back: it leaves (8, 20) a diagonal toward (9, 21)
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 2, journaled.Run( 2, Line( 8, 21, 8, 24 ) ).Record ) );
		// member 5's line up from (8, 19) links into (8, 20); member 6's column down from (7, 21)
		var up = journaled.Run( 5, Line( 8, 19, 8, 16 ) ).Record;
		journaled.Run( 6, Line( 7, 21, 7, 24 ) );
		// member 2's column again, taken back again: now it leaves (8, 20) a diagonal toward (7, 21)
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 2, journaled.Run( 2, Line( 8, 21, 8, 24 ) ).Record ) );
		CollectionAssert.AreEqual( new[] { row, over, -4L, up, -4L }, journaled.History( 8, 20 ), "two residues of member 2, one on each side of member 5's line" );
		Assert.AreEqual( (byte)8, journaled.Journal.ResidueLinksAt( 8, 20, 2 ), "the first left its diagonal toward (9, 21)" );
		Assert.AreEqual( (byte)0x20, journaled.Journal.ResidueLinksAt( 8, 20, 4 ), "the second its diagonal toward (7, 21) alone" );
		// the longer row's count-down leaves both diagonals and member 5's link: taken
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, over ) );
		Assert.AreEqual( 8 | 0x20, cells.LinksAt( 8, 20 ) & (8 | 0x20) );
	}

	[TestMethod]
	public void AClearedSharedBendTakesItsWeightFromTheEarlierSegment()
	{
		var journaled = NewJournaled();
		QueueOf( journaled );
		var cells = journaled.Level.Guests!.Grid.Cells;
		// an own chain: along the row to (8, 2), up from it, then down from it; the last one's undo finds (8, 2) a bend (toward (9, 2) and (8, 1)) and clears it
		journaled.Run( 1, Segment( journaled, 10, 2, 8, 2 ) );
		journaled.Run( 1, Segment( journaled, 8, 2, 8, 0 ) );
		var down = journaled.Run( 1, Segment( journaled, 8, 2, 8, 4 ) ).Record;
		Assert.IsTrue( journaled.Journal.WeightAgrees( 1 ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, down ) );
		Assert.AreEqual( 0, cells.RawTypeAt( 8, 2 ), "the bend cleared, as the original's undo clears it" );
		Assert.IsTrue( journaled.Journal.WeightAgrees( 1 ), "the earlier segment's weight shrank with its footprint" );
		journaled.Journal.Depart( 1 );
		Assert.AreEqual( 0, journaled.Journal.StepsOf( 1 ) );
	}

	[TestMethod]
	public void OnlyTheBuildingToolsAcceptedStepsAreSteps()
	{
		var journaled = NewJournaled();
		var one = journaled.Level.Objects.Objects.First();
		// a refused line, a placement and a take-back are no steps
		Assert.IsFalse( journaled.Run( 1, Line( -1, 20, 3, 20 ) ).Result.Accepted );
		Assert.IsTrue( journaled.Run( 1, journaled.Level.BuildCommand( one.Entry, 20, 12, 90 ) ).Result.Accepted );
		Assert.AreEqual( 0, journaled.Journal.StepsOf( 1 ) );
		var placed = journaled.LastRecord;
		Assert.AreEqual( TakeBackVerdict.NotOwn, journaled.TakeBack( 1, placed ), "an object has no take-back: its sale removes it" );
		var line = journaled.Run( 1, Line( 5, 20, 12, 20 ) ).Record;
		Assert.AreEqual( 1, journaled.Journal.StepsOf( 1 ) );
		// the host's own step is the host member's, and a take-back of another member's step is not this member's
		var hostLine = journaled.Run( null, Line( 5, 24, 12, 24 ) ).Record;
		Assert.AreEqual( 1, journaled.Journal.StepsOf( Host ) );
		Assert.AreEqual( TakeBackVerdict.NotOwn, journaled.TakeBack( 1, hostLine ) );
		Assert.AreEqual( TakeBackVerdict.NotOwn, journaled.TakeBack( null, line ) );
		// a take-back the host refused carries its verdict in the record, so every peer refuses it alike, and its reason names it
		var refused = journaled.Level.Commands.Log.Select( entry => entry.Command ).OfType<TakeBackCommand>().Last();
		Assert.IsNull( refused.Inverse );
		var result = refused.Execute( journaled.Level );
		Assert.AreEqual( TakeBackVerdict.NotOwn, TakeBackCommand.VerdictOf( result.Reason ) );
	}

	// ---- [EXT:online-coop-undo] pool strokes (C5-2) ----------------------------------------------------------------------------------

	/// <summary>A guest's batch of a pool stroke (<see cref="PoolCellsCommand"/>), the stroke's first when <paramref name="first"/>.</summary>
	private static PoolCellsCommand Batch( bool first, bool erase, bool river, params (int X, int Y)[] cells ) => new( erase, river, cells ) { First = first };

	/// <summary>The host's single cell of a pool stroke, the stroke's first when <paramref name="first"/>.</summary>
	private static PaintPoolCellCommand HostPaint( bool first, int x, int y ) => new( x, y, false ) { First = first };

	/// <summary>A synthetic two-cell wave machine whose water side is north (as the pools' attraction tests build one), with a thing slot so its pool site is found.</summary>
	private static ObjectCatalogEntry WaveMachine( Journaled journaled, int id )
	{
		journaled.Level.Objects.Things ??= new ThingPool();
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {id}\nInfo.Name Synthetic{id}\nInfo.IsChoosable 0\nInfo.OverwritePriority 5\nPools.Attraction wavemachine\nPools.WaterSide N\n", $"synthetic{id}.sam" ) } );
		return new ObjectCatalogEntry( "jungle", ObjectCategory.Feature, "/synthetic", settings, new ObjectShape( new[] { "**" } ), "/model.MD2", null, "/script.RSE",
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), journaled.One.Entry.FileSystem, null );
	}

	/// <summary>Sets the bank's balance to <paramref name="balance"/> by a posting of the difference.</summary>
	private static void BalanceAt( Journaled journaled, long balance )
	{
		var economy = journaled.Economy;
		if ( balance >= economy.Balance )
			economy.Ledger.Post( LedgerCategory.OtherIncome, balance - economy.Balance );
		else
			economy.Ledger.Post( LedgerCategory.OtherCosts, economy.Balance - balance );
		Assert.AreEqual( balance, economy.Balance );
	}

	[TestMethod]
	public void AStrokesCommandsFromItsFirstAreOneStepSetWhichItsTakeBackUndoesWholeWithTheirRefunds()
	{
		var journaled = NewJournaled();
		var economy = journaled.Economy;
		var (paid, cost) = (economy.Balance, economy.CellCost( CellPurchase.Pool ));
		Assert.IsTrue( cost > 0 );
		var stroke = journaled.Run( 1, Batch( true, false, false, (14, 26), (15, 26) ) ).Record;
		// a later batch of the drag, its last cell sent twice: the second write finds water
		Assert.IsTrue( journaled.Run( 1, Batch( false, false, false, (16, 26), (16, 26) ) ).Result.Accepted );
		Assert.AreEqual( paid - 3 * cost, economy.Balance );
		Assert.AreEqual( 2, journaled.Journal.StepsOf( 1 ), "an entry per batch" );
		// the stroke is named by its first batch's record alone
		Assert.AreEqual( TakeBackVerdict.NotOwn, journaled.TakeBackStroke( 1, stroke + 1 ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, stroke ) );
		Assert.IsTrue( journaled.LastTakeBack.Inverse is UndoPoolStrokeCommand { Erase: false } inverse
			&& inverse.Cells.SequenceEqual( new[] { (14, 26), (15, 26), (16, 26) } ) && inverse.RiverMarked.Count == 0, "one inverse over the stroke's cells" );
		Assert.IsFalse( journaled.IsWater( 14, 26 ) || journaled.IsWater( 15, 26 ) || journaled.IsWater( 16, 26 ) );
		Assert.AreEqual( paid, economy.Balance, "each erased cell refunded in full, as the tool's own undo refunds it" );
		Assert.AreEqual( 0, journaled.Journal.StepsOf( 1 ) );
		Assert.IsNull( journaled.Journal.HistoryAt( 14, 26 ), "no step tracks its cells any more" );
		Assert.IsTrue( journaled.Journal.WeightAgrees( 1 ) );
	}

	[TestMethod]
	public void ABatchThatOnlyMarksWaterIsAStrokesStepAndItsTakeBackTakesTheMarksOffAlone()
	{
		var journaled = NewJournaled();
		var cells = journaled.Cells;
		bool River( int x, int y ) => cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.PoolRiver );
		// the host's pool, a stroke of its own single cells
		var pool = journaled.Run( null, HostPaint( true, 14, 26 ) ).Record;
		journaled.Run( null, HostPaint( false, 15, 26 ) );
		journaled.Run( null, HostPaint( false, 16, 26 ) );
		Assert.AreEqual( 3, journaled.Journal.StepsOf( Host ) );
		// a guest's river stroke marks the water and paints one cell beyond it
		var river = journaled.Run( 1, Batch( true, false, true, (14, 26), (15, 26) ) ).Record;
		Assert.IsTrue( journaled.Run( 1, Batch( false, false, true, (16, 26), (17, 26) ) ).Result.Accepted );
		Assert.IsTrue( River( 14, 26 ) && River( 15, 26 ) && River( 16, 26 ) && River( 17, 26 ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, river ) );
		Assert.IsTrue( journaled.LastTakeBack.Inverse is UndoPoolStrokeCommand { Erase: false } inverse && inverse.Cells.SequenceEqual( new[] { (17, 26) } )
			&& inverse.RiverMarked.SequenceEqual( new[] { (14, 26), (15, 26), (16, 26) } ) );
		Assert.IsTrue( journaled.IsWater( 14, 26 ) && journaled.IsWater( 15, 26 ) && journaled.IsWater( 16, 26 ), "the water stayed" );
		Assert.IsFalse( River( 14, 26 ) || River( 15, 26 ) || River( 16, 26 ), "its marks went" );
		Assert.IsFalse( journaled.IsWater( 17, 26 ), "and the cell it painted" );
		// a stroke of marks alone is a step too
		var marks = journaled.Run( 1, Batch( true, false, true, (15, 26) ) ).Record;
		Assert.IsTrue( River( 15, 26 ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, marks ) );
		Assert.IsFalse( River( 15, 26 ) );
		// nothing of the guest's stands on the host's pool any more: the host's stroke goes back whole
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( null, pool ) );
		Assert.IsFalse( journaled.IsWater( 14, 26 ) || journaled.IsWater( 15, 26 ) || journaled.IsWater( 16, 26 ) );
	}

	[TestMethod]
	public void AnotherPlayersWriteOverAStrokesCellRefusesItsTakeBackUntilItIsTakenBackItself()
	{
		var journaled = NewJournaled();
		var stroke = journaled.Run( 1, Batch( true, false, false, (14, 26), (15, 26), (16, 26) ) ).Record;
		var erase = journaled.Run( null, new ErasePoolCellCommand( 15, 26 ) { First = true } ).Record;
		var balance = journaled.Economy.Balance;
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBackStroke( 1, stroke ) );
		Assert.IsTrue( journaled.IsWater( 14, 26 ) && journaled.IsWater( 16, 26 ) && !journaled.IsWater( 15, 26 ), "nothing changed" );
		Assert.AreEqual( balance, journaled.Economy.Balance );
		Assert.AreEqual( 1, journaled.Journal.StepsOf( 1 ), "the stroke kept" );
		// the host's erase taken back: nothing of it stands on the stroke's cells, which go back whole
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( null, erase ) );
		Assert.IsTrue( journaled.IsWater( 15, 26 ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, stroke ) );
		Assert.IsFalse( journaled.IsWater( 14, 26 ) || journaled.IsWater( 15, 26 ) || journaled.IsWater( 16, 26 ) );
		// a stroke that painted a cell, another player erased it, and the stroke's later batch painted it again: refused, nothing changed
		var again = journaled.Run( 1, Batch( true, false, false, (20, 26) ) ).Record;
		journaled.Run( null, new ErasePoolCellCommand( 20, 26 ) { First = true } );
		Assert.IsTrue( journaled.Run( 1, Batch( false, false, false, (20, 26), (21, 26) ) ).Result.Accepted );
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBackStroke( 1, again ) );
		Assert.IsTrue( journaled.IsWater( 20, 26 ) && journaled.IsWater( 21, 26 ) );
	}

	[TestMethod]
	public void EachStrokeIsItsOwnStepSetTakenBackNewestFirstWhateverItsMode()
	{
		var journaled = NewJournaled();
		var economy = journaled.Economy;
		var cost = economy.CellCost( CellPurchase.Pool );
		var paint = journaled.Run( 1, Batch( true, false, false, (14, 26), (15, 26), (16, 26), (17, 26) ) ).Record;
		var erase = journaled.Run( 1, Batch( true, true, false, (16, 26), (17, 26) ) ).Record;
		var more = journaled.Run( 1, Batch( true, false, false, (20, 26), (21, 26) ) ).Record;
		Assert.AreEqual( TakeBackVerdict.NotLast, journaled.TakeBackStroke( 1, paint ) );
		Assert.AreEqual( TakeBackVerdict.NotLast, journaled.TakeBackStroke( 1, erase ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, more ), "two strokes of one mode in a row are two step sets" );
		Assert.IsTrue( journaled.IsWater( 14, 26 ) && !journaled.IsWater( 20, 26 ) );
		var balance = economy.Balance;
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, erase ) );
		Assert.IsTrue( journaled.IsWater( 16, 26 ) && journaled.IsWater( 17, 26 ), "the erased cells painted again" );
		Assert.AreEqual( balance - 2 * cost, economy.Balance, "and paid for" );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, paint ) );
		Assert.IsFalse( Enumerable.Range( 14, 4 ).Any( x => journaled.IsWater( x, 26 ) ) );
	}

	[TestMethod]
	public void ABatchWithoutTheFirstMarkerInAnotherModeOrAfterAnotherStepOpensAStrokeOfItsOwn()
	{
		var journaled = NewJournaled();
		// a forged erase batch that claims to go on with a paint stroke: a stroke of its own, taken back alone
		var paint = journaled.Run( 1, Batch( true, false, false, (14, 26), (15, 26) ) ).Record;
		var forged = journaled.Run( 1, Batch( false, true, false, (15, 26) ) ).Record;
		Assert.AreEqual( TakeBackVerdict.NotLast, journaled.TakeBackStroke( 1, paint ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, forged ) );
		Assert.IsTrue( journaled.IsWater( 14, 26 ) && journaled.IsWater( 15, 26 ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, paint ) );
		// a batch without the marker after another tool's step of its member: a stroke of its own too
		var before = journaled.Run( 1, Batch( true, false, false, (20, 26) ) ).Record;
		var line = journaled.Run( 1, Line( 5, 20, 12, 20 ) ).Record;
		Assert.AreEqual( 2, journaled.Journal.StepsOf( 1 ), "the line is a step" );
		var after = journaled.Run( 1, Batch( false, false, false, (21, 26) ) ).Record;
		Assert.AreEqual( TakeBackVerdict.NotLast, journaled.TakeBackStroke( 1, before ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, after ) );
		Assert.IsTrue( journaled.IsWater( 20, 26 ) && !journaled.IsWater( 21, 26 ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, before ), "the line is another tool's step" );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBack( 1, line ) );
	}

	[TestMethod]
	public void ADragWhoseFirstBatchWasRefusedIsOneStrokeNamedByThatBatch()
	{
		var journaled = NewJournaled();
		journaled.Journal.StepBound = 2;
		// older steps of the member, which the bound forgets, and a stroke before the drag
		journaled.Run( 1, Line( 5, 20, 12, 20 ) );
		journaled.Run( 1, Line( 5, 22, 12, 22 ) );
		var before = journaled.Run( 1, Batch( true, false, false, (20, 26) ) ).Record;
		// the drag's first batch meets a path cell: refused, it changed nothing, and still opens the stroke its later batch goes on with
		var first = journaled.Run( 1, Batch( true, false, false, (5, 20) ) );
		Assert.IsFalse( first.Result.Accepted );
		Assert.IsTrue( journaled.Run( 1, Batch( false, false, false, (14, 26), (15, 26) ) ).Result.Accepted );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, first.Record ) );
		Assert.IsFalse( journaled.IsWater( 14, 26 ) || journaled.IsWater( 15, 26 ) );
		Assert.IsTrue( journaled.IsWater( 20, 26 ), "the stroke before it untouched" );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, before ) );
		// the same as a member's first stroke of the session
		var opening = journaled.Run( 2, Batch( true, false, false, (5, 22) ) );
		Assert.IsFalse( opening.Result.Accepted );
		Assert.IsTrue( journaled.Run( 2, Batch( false, false, false, (24, 26) ) ).Result.Accepted );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 2, opening.Record ) );
		Assert.IsFalse( journaled.IsWater( 24, 26 ) );
	}

	[TestMethod]
	public void AStrokeThatLostAnEntryToTheBoundsIsForgottenWhole()
	{
		var journaled = NewJournaled();
		journaled.Journal.StepBound = 2;
		var older = journaled.Run( 1, Batch( true, false, false, (20, 26) ) ).Record;
		var stroke = journaled.Run( 1, Batch( true, false, false, (14, 26) ) ).Record;
		journaled.Run( 1, Batch( false, false, false, (15, 26) ) );
		journaled.Run( 1, Batch( false, false, false, (16, 26) ) );
		Assert.AreEqual( 2, journaled.Journal.StepsOf( 1 ) );
		Assert.AreEqual( TakeBackVerdict.Forgotten, journaled.TakeBackStroke( 1, stroke ), "its first entry went: too long ago as a whole" );
		Assert.IsTrue( journaled.IsWater( 14, 26 ) && journaled.IsWater( 15, 26 ) && journaled.IsWater( 16, 26 ), "nothing changed" );
		Assert.AreEqual( 2, journaled.Journal.StepsOf( 1 ) );
		Assert.AreEqual( TakeBackVerdict.Forgotten, journaled.TakeBackStroke( 1, older ) );
		Assert.AreEqual( TakeBackVerdict.NotOwn, journaled.TakeBackStroke( 1, 999_999 ), "no stroke of the member's" );
		Assert.AreEqual( 1, journaled.Journal.ClippedOf( 1 ), "only the stroke that keeps an entry is kept as clipped" );
		// its last entries go too: the stroke leaves the clipped set, and the bounds' cutoff still says it is too long ago
		journaled.Run( 1, Batch( true, false, false, (21, 26) ) );
		journaled.Run( 1, Batch( true, false, false, (22, 26) ) );
		Assert.AreEqual( 0, journaled.Journal.ClippedOf( 1 ) );
		Assert.AreEqual( TakeBackVerdict.Forgotten, journaled.TakeBackStroke( 1, stroke ) );
		Assert.IsTrue( journaled.Journal.WeightAgrees( 1 ) );
	}

	[TestMethod]
	public void AStrokeTooLongForOneInverseIsKeptAndSaysSo()
	{
		var journaled = NewJournaled();
		// a forged drag past the tool's split (the asset-free park is too small for a stroke of more than a command's cells: a seam lowers the limit)
		journaled.Journal.StrokeCellLimit = 3;
		var stroke = journaled.Run( 1, Batch( true, false, false, (14, 26), (15, 26) ) ).Record;
		Assert.IsTrue( journaled.Run( 1, Batch( false, false, false, (16, 26), (17, 26) ) ).Result.Accepted );
		Assert.AreEqual( TakeBackVerdict.TooLarge, journaled.TakeBackStroke( 1, stroke ) );
		Assert.IsTrue( Enumerable.Range( 14, 4 ).All( x => journaled.IsWater( x, 26 ) ), "nothing changed" );
		Assert.AreEqual( 2, journaled.Journal.StepsOf( 1 ), "the stroke kept" );
		// within the limit it goes
		journaled.Journal.StrokeCellLimit = 4;
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, stroke ) );
	}

	[TestMethod]
	public void ARepaintTheBankOrAStandingEmployeeAllowsOnlyInPartIsInTheWayWhole()
	{
		var journaled = NewJournaled();
		var economy = journaled.Economy;
		var cost = economy.CellCost( CellPurchase.Pool );
		journaled.Run( 1, Batch( true, false, false, (14, 26), (15, 26) ) );
		var erase = journaled.Run( 1, Batch( true, true, false, (14, 26), (15, 26) ) ).Record;
		void Refused( long balance, string why )
		{
			BalanceAt( journaled, balance );
			Assert.AreEqual( TakeBackVerdict.InTheWay, journaled.TakeBackStroke( 1, erase ), why );
			Assert.AreEqual( balance, economy.Balance, $"{why}: nothing paid" );
			Assert.IsFalse( journaled.IsWater( 14, 26 ) || journaled.IsWater( 15, 26 ), $"{why}: nothing painted" );
			Assert.AreEqual( 2, journaled.Journal.StepsOf( 1 ), $"{why}: the stroke kept" );
		}
		// money for one of the two cells
		Refused( cost, "money for one cell" );
		// at the 32-bit edge the bank's two tests disagree: 2^31 passes the commit's wrapping difference and fails the preview's comparison, which a repaint meets first
		Assert.IsTrue( ParkEconomy.Affords( 1L << 31, cost ) && !ParkEconomy.PreviewsLine( cost, 1L << 31 ) );
		Refused( 1L << 31, "the preview's test" );
		// 2^32 and one cell: each cell alone passes against the bank's balance, but the first one's charge leaves the second none in 32 bits
		Refused( (1L << 32) + cost, "the balance the first cell left" );
		// an employee standing on a cell the repaint would paint
		BalanceAt( journaled, 10 * cost );
		var member = economy.Staff.Members.First();
		(member.CellX, member.CellY) = (15.5f, 26.5f);
		Assert.AreEqual( TakeBackVerdict.InTheWay, journaled.TakeBackStroke( 1, erase ), "someone stands there" );
		Assert.IsFalse( journaled.IsWater( 14, 26 ) || journaled.IsWater( 15, 26 ) );
		(member.CellX, member.CellY) = (40.5f, 40.5f);
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, erase ) );
		Assert.AreEqual( 8 * cost, economy.Balance, "both cells paid for" );
		Assert.IsTrue( journaled.IsWater( 14, 26 ) && journaled.IsWater( 15, 26 ) );
	}

	[TestMethod]
	public void ASwimmerInACellAPaintStrokesUndoWouldEraseIsInTheWayWhole()
	{
		var journaled = NewJournaled();
		var stroke = journaled.Run( 1, Batch( true, false, false, (14, 26), (15, 26) ) ).Record;
		var balance = journaled.Economy.Balance;
		// a guest swimming in the cell the undo erases last (its swim set by hand: the asset-free park has no changing room), so the first cell alone could go
		var swimmer = journaled.Level.Guests!.SpawnInPark( 4, 1 );
		var (state, x, y) = (swimmer.State, swimmer.CellX, swimmer.CellY);
		(swimmer.State, swimmer.CellX, swimmer.CellY) = (GuestState.Swimming, 14, 26);
		Assert.IsTrue( journaled.Level.Guests.IsSwimmerIn( 14, 26 ) );
		Assert.AreEqual( TakeBackVerdict.InTheWay, journaled.TakeBackStroke( 1, stroke ) );
		Assert.IsTrue( journaled.IsWater( 14, 26 ) && journaled.IsWater( 15, 26 ), "both cells kept" );
		Assert.AreEqual( balance, journaled.Economy.Balance, "nothing refunded" );
		Assert.AreEqual( 1, journaled.Journal.StepsOf( 1 ), "the stroke kept" );
		// out of the water: taken whole
		(swimmer.State, swimmer.CellX, swimmer.CellY) = (state, x, y);
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, stroke ) );
		Assert.IsFalse( journaled.IsWater( 14, 26 ) || journaled.IsWater( 15, 26 ) );
	}

	[TestMethod]
	public void AStrokeWhoseUndoWouldEraseBothWaterSideCellsOfAWaveMachineIsInTheWayWhole()
	{
		var journaled = NewJournaled();
		var stroke = journaled.Run( 1, Batch( true, false, false, (14, 26), (15, 26), (16, 26) ) ).Record;
		// a wave machine along the stroke: its two water-side cells are the stroke's; one of them is all it needs
		var machine = journaled.Level.Objects.TryPlace( WaveMachine( journaled, 9301 ), 14, 27, 0, out _ );
		Assert.IsNotNull( machine, "the machine stands with water to its north" );
		Assert.IsNull( journaled.Level.PoolEraseBlock( 15, 26 ), "either water-side cell alone may go" );
		var balance = journaled.Economy.Balance;
		// the undo erases the stroke from its end: the first water-side cell may go, the second is then the machine's last
		Assert.AreEqual( TakeBackVerdict.InTheWay, journaled.TakeBackStroke( 1, stroke ) );
		Assert.IsTrue( journaled.IsWater( 14, 26 ) && journaled.IsWater( 15, 26 ) && journaled.IsWater( 16, 26 ), "nothing erased" );
		Assert.AreEqual( balance, journaled.Economy.Balance );
		Assert.AreEqual( 1, journaled.Journal.StepsOf( 1 ), "the stroke kept" );
	}

	[TestMethod]
	public void AWaveSettingOverARiverMarkLetsItsTakeBackPassButOverAPaintedCellStaysAChange()
	{
		var journaled = NewJournaled();
		var cells = journaled.Cells;
		// the host's pool and a wave machine at its south edge
		journaled.Run( null, HostPaint( true, 14, 26 ) );
		journaled.Run( null, HostPaint( false, 15, 26 ) );
		var machine = journaled.Level.Objects.TryPlace( WaveMachine( journaled, 9302 ), 14, 27, 0, out _ );
		Assert.IsNotNull( machine );
		// a guest's river stroke over the machine's access cell, then another player's wave settings there: the take-back takes the marks off and leaves the settings
		var river = journaled.Run( 1, Batch( true, false, true, (14, 26), (15, 26) ) ).Record;
		Assert.IsTrue( journaled.Run( null, new SetPoolWavesCommand( machine!.Runtime.AttractionId, true, 3, 0 ) ).Result.Accepted );
		Assert.AreEqual( new PoolWaveSettings( true, 3, 0 ), journaled.Level.PoolWavesOf( machine ) );
		Assert.AreEqual( TakeBackVerdict.Taken, journaled.TakeBackStroke( 1, river ) );
		Assert.IsFalse( cells.FlagsAt( 14, 26 ).HasFlag( ParkCellFlags.PoolRiver ) || cells.FlagsAt( 15, 26 ).HasFlag( ParkCellFlags.PoolRiver ) );
		Assert.AreEqual( new PoolWaveSettings( true, 3, 0 ), journaled.Level.PoolWavesOf( machine ), "the settings intact" );
		// a guest's paint stroke under a second machine, another player's wave settings in its access cell, the machine removed: the paint's take-back would erase the settings
		var paint = journaled.Run( 1, Batch( true, false, false, (20, 26), (21, 26) ) ).Record;
		var second = journaled.Level.Objects.TryPlace( WaveMachine( journaled, 9303 ), 20, 27, 0, out _ );
		Assert.IsNotNull( second );
		Assert.IsTrue( journaled.Run( null, new SetPoolWavesCommand( second!.Runtime.AttractionId, false, 1, 2 ) ).Result.Accepted );
		var flags = (cells.FlagsAt( 20, 26 ), cells.FlagsAt( 21, 26 ));
		Assert.AreNotEqual( (ParkCellFlags.None, ParkCellFlags.None), flags, "the settings are in a cell's flags" );
		// the fixture's park is built without its field initializers: the removal's table of demolished queues
		var demolished = typeof( Level ).GetField( "demolishedQueues", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!;
		if ( demolished.GetValue( journaled.Level ) == null )
			demolished.SetValue( journaled.Level, new HashSet<OriginalObject>() );
		Assert.IsTrue( journaled.Level.Objects.Remove( second ) );
		Assert.AreEqual( TakeBackVerdict.Changed, journaled.TakeBackStroke( 1, paint ) );
		Assert.IsTrue( journaled.IsWater( 20, 26 ) && journaled.IsWater( 21, 26 ) );
		Assert.AreEqual( flags, (cells.FlagsAt( 20, 26 ), cells.FlagsAt( 21, 26 )), "the settings intact" );
	}
}
