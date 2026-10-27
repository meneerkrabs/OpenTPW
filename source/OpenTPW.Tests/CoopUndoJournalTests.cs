using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-undo] The host's undo journal on its own (docs/COOP.md, "Conflicts and undo (C5)"), on the stream tests' asset-free park, wired as the host's stream wires it: the map's
/// setters report real changes only; a step's record leaves the cells where its take-back reversed its effects, wherever it stands in their lists; records of no step stay as one
/// permanent marker; a departure keeps its member's records; the bounds forget the oldest steps; and only the building tools' accepted steps are steps.
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
}
