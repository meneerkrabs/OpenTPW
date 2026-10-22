using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The placement's pending queue start (tp.exe <c>0x8186C0</c>), on the same asset-free level: the line tool a kart or water ride's placement opens hands the close of its circuit
/// to the queue tool whatever the click held (<c>0x526F95</c>) and takes the start (<c>0x527056</c>); with none pending the close is the modifier's, exactly Ctrl keeping the line
/// tool without a start (<c>0x527146</c>). A build click (<c>0x524B53</c>), a path or queue commit (<c>0x527483</c>) and the Move tool's pick-up (<c>0x525E5A</c>) drop it. A line
/// click with a start whose ghost has a refused tile ends the line tool and lays nothing (<c>0x524A63</c>).
/// </summary>
public partial class TrackAddOnLevelTests
{
	/// <summary>The station alone, of a ride with a queue (its back cell (4, 4)), with its track tool opened as the ride's placement opens it.</summary>
	private (Level Level, OriginalObject Ride) PlacedWithQueue()
	{
		var (level, ride, _) = World( hasQueue: true, laid: false );
		AttachTrackEconomy( level, ride );
		WithQueue( ride );
		Assert.IsTrue( level.EnterPlacedTrackTool( ride ) );
		Assert.AreEqual( CellToolMode.KartTrack, level.CellTool.Mode );
		Assert.AreEqual( (14, 10), level.CellTool.Start, "the line starts at the station's exit" );
		return (level, ride);
	}

	private static void WithQueue( OriginalObject ride )
	{
		ride.Visitors.HasCells = true;
		ride.Visitors.QueueFrontCell = (4, 4);
	}

	/// <summary>The long loop of <see cref="World"/> from the station's exit; the line that closes the circuit holds <paramref name="modifiers"/>.</summary>
	private static SegmentResult? LayLoop( Level level, int modifiers )
	{
		level.ClickCellTool( (30, 10) );
		level.ClickCellTool( (30, 14) );
		level.ClickCellTool( (8, 14) );
		return level.ClickCellTool( (8, 10), modifiers );
	}

	/// <summary>The track button's line tool of the open circuit, and the close of the loop with exactly Ctrl held: with no queue start pending the line tool stays without a start.</summary>
	private static void AssertTheTrackButtonsCloseKeepsTheLineTool( Level level, OriginalObject ride )
	{
		Assert.IsTrue( level.EnterTrackTool( ride ), "the track button" );
		Assert.IsTrue( LayLoop( level, 0xc )?.Completed == true );
		Assert.IsTrue( ride.Runtime.Bump!.IsTrackValid, "the last line closes the circuit" );
		Assert.AreEqual( CellToolMode.KartTrack, level.CellTool.Mode, "no queue start is pending: exactly Ctrl keeps the line tool (0x527146)" );
		Assert.IsNull( level.CellTool.Start );
		Assert.AreSame( ride, level.TrackToolStation );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 0xc )]
	[DataRow( 0xf )]
	public void APlacedRidesCircuitHandsOverToItsQueueWhateverTheCloseHeld( int modifiers )
	{
		var (level, ride) = PlacedWithQueue();
		Assert.IsTrue( LayLoop( level, modifiers )?.Completed == true );
		Assert.IsTrue( ride.Runtime.Bump!.IsTrackValid, "the last line closes the circuit" );
		Assert.AreEqual( CellToolMode.Queue, level.CellTool.Mode, "the pending queue start takes the queue tool before the modifier word is read (0x526F95)" );
		Assert.AreEqual( (4, 4), level.CellTool.Start );
		Assert.IsNull( level.TrackToolStation );
	}

	[TestMethod]
	public void TheHandoverTakesThePendingQueueStart()
	{
		var (level, ride) = PlacedWithQueue();
		LayLoop( level, 0 );
		Assert.AreEqual( CellToolMode.Queue, level.CellTool.Mode );
		level.CellTool.Cancel();
		CutForLiveMend( level, ride );
		Assert.IsTrue( level.ClickCellTool( (20, 14), 0xc )?.Completed == true );
		Assert.AreEqual( CellToolMode.KartTrack, level.CellTool.Mode, "the first close took the start (0x527056): the mend's close keeps the line tool" );
		Assert.IsNull( level.CellTool.Start );
		Assert.AreSame( ride, level.TrackToolStation );
	}

	[DataTestMethod]
	[DataRow( 0xc, true )]
	[DataRow( 0, false )]
	[DataRow( 0x3c, false )]
	public void WithNoQueueStartPendingTheModifierDecidesForARideWithAQueue( int modifiers, bool stays )
	{
		var (level, ride, _) = World( hasQueue: true );
		AttachTrackEconomy( level, ride );
		WithQueue( ride );
		CutForLiveMend( level, ride );
		Assert.IsTrue( level.ClickCellTool( (20, 14), modifiers )?.Completed == true );
		Assert.IsTrue( ride.Runtime.Bump!.IsTrackValid );
		Assert.AreEqual( stays, level.CellTool.IsActive, "the track button opened no placement's queue start: no queue tool follows (0x526FB3)" );
		if ( stays )
		{
			Assert.AreEqual( CellToolMode.KartTrack, level.CellTool.Mode );
			Assert.IsNull( level.CellTool.Start );
		}
		Assert.AreEqual( stays ? ride : null, level.TrackToolStation );
	}

	[TestMethod]
	public void ABuildClickDropsThePendingQueueStart()
	{
		var (level, ride) = PlacedWithQueue();
		level.CellTool.Cancel();
		// the build tool's click on the ride's own cell: the placement is refused, and the click drops the start all the same (0x524B53)
		level.BuildEntry = ride.Entry;
		var objects = level.Objects.Objects.Count;
		level.ClickBuildTool( 10, 9 );
		Assert.AreEqual( objects, level.Objects.Objects.Count, "nothing was placed" );
		level.BuildEntry = null;
		AssertTheTrackButtonsCloseKeepsTheLineTool( level, ride );
	}

	[TestMethod]
	public void AQueueCommitDropsThePendingQueueStart()
	{
		var (level, ride) = PlacedWithQueue();
		// a line of the queue tool, which the ride's window opens without the handover: its commit drops the start (0x527483)
		level.CellTool.Enter( new AcceptingLineWriter( CellToolMode.Queue ), (4, 4) );
		Assert.IsNotNull( level.ClickCellTool( (4, 8) ) );
		level.CellTool.Cancel();
		AssertTheTrackButtonsCloseKeepsTheLineTool( level, ride );
	}

	[TestMethod]
	public void ARefusedQueueClickKeepsThePendingQueueStart()
	{
		var (level, ride) = PlacedWithQueue();
		// a queue click whose ghost is refused ends that tool before the commit (0x524A63), so the start stays pending
		level.CellTool.Enter( new AcceptingLineWriter( CellToolMode.Queue, refuses: true ), (4, 4) );
		Assert.IsTrue( level.ClickCellTool( (4, 8) ) is { GhostRefused: true } );
		Assert.IsTrue( level.EnterTrackTool( ride ), "the track button" );
		Assert.IsTrue( LayLoop( level, 0xc )?.Completed == true );
		Assert.AreEqual( CellToolMode.Queue, level.CellTool.Mode, "the start is still pending: the close hands over to the queue tool" );
	}

	[TestMethod]
	public void TheMoveToolsPickUpDropsThePendingQueueStart()
	{
		var (level, ride) = PlacedWithQueue();
		level.CellTool.Cancel();
		var bench = level.Objects.TryPlace( Entry( ObjectCategory.Feature, "Info.Id 1700\nInfo.Name SyntheticBench\nInfo.WhichUIType 3\n", "*" ), 20, 20, 0, out _ );
		Assert.IsNotNull( bench );
		// the pick-up's click in mode 0x33 drops the start (0x525E5A)
		Assert.IsTrue( level.BeginMove( bench! ) );
		Assert.IsTrue( bench!.IsDeleted && level.IsMoving );
		level.BuildEntry = null;
		AssertTheTrackButtonsCloseKeepsTheLineTool( level, ride );
	}

	[TestMethod]
	public void ARefusedTileOnTheLineEndsTheLineToolAndLaysNothing()
	{
		var (level, ride) = PlacedWithQueue();
		// (20, 10) is a queue cell: the tile with its origin there is refused (QueueCell); the line's first tile is the station's exit tile, which the placement laid (0x5253E9),
		// and the two after it are free
		level.Guests!.Grid.Cells.SetType( 20, 10, ParkCellType.Queue );
		Assert.AreEqual( CellBuildResult.QueueCell, level.KartTracks!.CheckTile( 20, 10 ) );
		Assert.IsTrue( level.KartTracks.IsTile( 14, 10 ) );
		var balance = level.Park!.Economy.Balance;
		var result = level.ClickCellTool( (30, 10) );
		Assert.IsTrue( result is { GhostRefused: true, StoppedBy: CellBuildResult.QueueCell } );
		Assert.AreEqual( (20, 10), result!.StoppedAt );
		Assert.IsFalse( level.CellTool.IsActive, "the refused ghost ends the line tool (0x5266A3)" );
		foreach ( var x in new[] { 16, 18 } )
			Assert.IsFalse( level.KartTracks.IsTile( x, 10 ), $"({x}, 10) is not laid: the click lays none of the line" );
		Assert.AreEqual( balance, level.Park.Economy.Balance, "nothing is charged" );
		Assert.IsFalse( level.Commands.Log.Any( entry => entry.Command.Kind == ParkCommandKind.LayTrackLine ), "no line command is issued" );
	}

	[TestMethod]
	public void ALineTheParkCannotPayForEndsTheLineToolAndLaysNothing()
	{
		var (level, ride) = PlacedWithQueue();
		var economy = level.Park!.Economy;
		// the money for three of the line's eight new tiles (its first is the station's exit tile): the preview refuses the fourth (the chooser's money test, 0x539F95)
		economy.Ledger.Post( LedgerCategory.OtherCosts, economy.Balance - 3 * level.KartTracks!.TileCost );
		Assert.AreEqual( 3 * level.KartTracks.TileCost, economy.Balance );
		var result = level.ClickCellTool( (30, 10) );
		Assert.IsTrue( result is { GhostRefused: true, StoppedBy: CellBuildResult.NotEnoughMoney } );
		Assert.AreEqual( (22, 10), result!.StoppedAt );
		Assert.IsFalse( level.CellTool.IsActive );
		Assert.IsFalse( level.KartTracks.IsTile( 16, 10 ), "the affordable tiles are not laid either" );
		Assert.AreEqual( 3 * level.KartTracks.TileCost, economy.Balance );
	}

	/// <summary>A line writer of <paramref name="mode"/> that lays nothing and accepts every line, or refuses every ghost when <paramref name="refuses"/>.</summary>
	private sealed class AcceptingLineWriter( CellToolMode mode, bool refuses = false ) : ICellLineWriter
	{
		public CellToolMode Mode => mode;
		public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor ) =>
			new( cursor, System.Array.Empty<(int X, int Y)>(), 0, refuses ? CellBuildResult.Occupied : CellBuildResult.Ok, false )
			{ Start = start, Line = ParkPathBuilder.LayLine( start, cursor ), StoppedAt = refuses ? cursor : null };
		public SegmentResult Commit( (int X, int Y) start, (int X, int Y) end ) => Preview( start, end );
		public void Undo( SegmentResult segment ) { }
	}
}
