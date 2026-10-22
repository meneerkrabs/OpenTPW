using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The placement's pending queue start (tp.exe <c>0x8186C0</c>), on the same asset-free level: the line tool a kart or water ride's placement opens hands the close of its circuit
/// to the queue tool whatever the click held (<c>0x526F95</c>) and takes the start (<c>0x527056</c>); with none pending the close is the modifier's, exactly Ctrl keeping the line
/// tool without a start (<c>0x527146</c>). A build click on an accepted ghost (<c>0x524B53</c>), a path or queue commit (<c>0x527483</c>) and the Move tool's pick-up (<c>0x525E5A</c>)
/// drop it; a click on a refused ghost returns before them (<c>0x524A63</c>). A line click with a start whose ghost has a refused tile ends the line tool and lays nothing.
/// </summary>
public partial class TrackAddOnLevelTests
{
	/// <summary>The station alone, of a ride with a queue (its back cell (4, 4)), with its track tool opened as the ride's placement opens it.</summary>
	private (Level Level, OriginalObject Ride) PlacedWithQueue( ObjectCatalogEntry? extra = null )
	{
		var (level, ride, _) = World( hasQueue: true, laid: false, extra );
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
		level.CellTool.Cancel();
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

	/// <summary>The track button's line tool of the open circuit, and the close of the loop with exactly Ctrl held: the start is still pending, so the close hands over to the queue tool.</summary>
	private static void AssertTheTrackButtonsCloseHandsOverToTheQueue( Level level, OriginalObject ride )
	{
		level.CellTool.Cancel();
		Assert.IsTrue( level.EnterTrackTool( ride ), "the track button" );
		Assert.IsTrue( LayLoop( level, 0xc )?.Completed == true );
		Assert.AreEqual( CellToolMode.Queue, level.CellTool.Mode, "the start is still pending: the close hands over to the queue tool" );
		Assert.AreEqual( (4, 4), level.CellTool.Start );
	}

	[TestMethod]
	public void AnAcceptedBuildClickDropsThePendingQueueStart()
	{
		// its own archive path, so the placing command's entry key names it and not the ride (ParkCommandCodec.EntryKey)
		var toilet = new ObjectCatalogEntry( "jungle", ObjectCategory.Feature, "/synthetic-toilet",
			new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1402\nInfo.Name SyntheticToilet\nInfo.WhichUIType 3\n", "synthetic.sam" ) } ), new ObjectShape( new[] { "*" } ), "/synthetic/model.MD2", null,
			"/synthetic/script.RSE", System.Array.Empty<string>(), System.Array.Empty<ObjectAnimationFile>(), System.Array.Empty<string>(), System.Array.Empty<string>(), FileSystem, null );
		var (level, ride) = PlacedWithQueue( toilet );
		level.CellTool.Cancel();
		level.BuildEntry = toilet;
		Assert.IsTrue( level.BuildGhostAllowed( toilet, level.CheckBuildTarget( toilet, 20, 20, 0 ) ), "the ghost on free ground is accepted" );
		// the click zeroes the start before it places (0x524B53)
		level.ClickBuildTool( 20, 20 );
		Assert.IsTrue( level.Objects.Objects.Any( item => !item.IsDeleted && item.Entry == toilet ), $"the click places the object ({level.LastActionMessage})" );
		level.BuildEntry = null;
		AssertTheTrackButtonsCloseKeepsTheLineTool( level, ride );
	}

	[TestMethod]
	public void ABuildClickOnARefusedGhostKeepsThePendingQueueStart()
	{
		var (level, ride) = PlacedWithQueue();
		level.CellTool.Cancel();
		// the ghost on the ride's own cell is refused: the click returns at the latch before the mode dispatch (0x524A63, modes 4 and 0x3B to 0x52765B)
		level.BuildEntry = ride.Entry;
		Assert.IsFalse( level.BuildGhostAllowed( ride.Entry, level.CheckBuildTarget( ride.Entry, 10, 9, 0 ) ) );
		var objects = level.Objects.Objects.Count;
		level.ClickBuildTool( 10, 9 );
		Assert.AreEqual( objects, level.Objects.Objects.Count, "nothing was placed" );
		level.BuildEntry = null;
		AssertTheTrackButtonsCloseHandsOverToTheQueue( level, ride );
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
		AssertTheTrackButtonsCloseHandsOverToTheQueue( level, ride );
	}

	[TestMethod]
	public void AQueueLineWhoseCommandIsRefusedKeepsThePendingQueueStart()
	{
		var (level, ride) = PlacedWithQueue();
		// the real command writer: the park refuses the line of a ride it does not have, so nothing is laid and the start stays pending
		level.CellTool.Enter( new ParkCommandLineWriter( new AcceptingLineWriter( CellToolMode.Queue ), level.Commands, 9999 ), (4, 4) );
		Assert.IsTrue( level.ClickCellTool( (4, 8) ) is { GhostRefused: false, CommandRefused: true } );
		AssertTheTrackButtonsCloseHandsOverToTheQueue( level, ride );
	}

	[TestMethod]
	public void AQueueLineWhoseCommandThrewAfterAWriteDropsThePendingQueueStart()
	{
		var (level, ride) = PlacedWithQueue();
		var strict = ParkCommands.Strict;
		ParkCommands.Strict = false;
		try
		{
			// a command that throws while it runs keeps what it wrote (ParkCommands): the line counts as reached, so the start is dropped as after the original's LayLine (0x527483)
			level.CommandFault = command =>
			{
				if ( command is LayLineCommand )
				{
					level.Guests!.Grid.Cells.SetType( 4, 5, ParkCellType.Queue );
					throw new System.InvalidOperationException( "after a write" );
				}
			};
			level.CellTool.Enter( new ParkCommandLineWriter( new AcceptingLineWriter( CellToolMode.Queue ), level.Commands, 9999 ), (4, 4) );
			Assert.IsTrue( level.ClickCellTool( (4, 8) ) is { GhostRefused: false, CommandRefused: false } );
			Assert.AreEqual( ParkCellType.Queue, level.Guests!.Grid.Cells.TypeAt( 4, 5 ), "the write before the throw stays" );
		}
		finally
		{
			level.CommandFault = null;
			ParkCommands.Strict = strict;
		}
		AssertTheTrackButtonsCloseKeepsTheLineTool( level, ride );
	}

	[TestMethod]
	public void TheHudsPlacementOfAKartRideLeavesItsQueueStartPending()
	{
		var (level, first, _) = World( hasQueue: true, laid: false );
		// the queue cells the guest link gives a placed ride with an entrance (the synthetic shape has none)
		level.Objects.ObjectPlaced += item =>
		{
			if ( item != first && item.Runtime.Bump != null )
				WithQueue( item );
		};
		var hud = new Hud.ParkHud( level, OriginalUiTests.FakeStrings(), Hud.EconomyParkStatus.ForLevel( level ), new NoBuildItems(), new Hud.HudHost() );
		// the buy list armed the ride (the HUD's pending item) and the build click places it through its command; the HUD ends the placement once the command returned (AfterIssue)
		typeof( Hud.ParkHud ).GetField( "pendingItem", Private )!.SetValue( hud, new Hud.BuildItem( "ride:1150", 1150, Hud.BuildCategory.Rides, -1, 0, null, System.Array.Empty<string>(), Entry: first.Entry ) );
		level.BuildEntry = first.Entry;
		level.ClickBuildTool( 10, 20 );
		var placed = level.Objects.Objects.Single( item => !item.IsDeleted && item != first );
		Assert.AreSame( placed, level.TrackToolStation, "the placement opens the new ride's track tool" );
		Assert.AreEqual( CellToolMode.KartTrack, level.CellTool.Mode );
		Assert.AreEqual( level.TrackStations[placed].Exit, level.CellTool.Start );
		Assert.AreEqual( (14, 21), level.CellTool.Start );
		// the long loop of World, eleven cells further down, closed with exactly Ctrl held
		level.ClickCellTool( (30, 21) );
		level.ClickCellTool( (30, 25) );
		level.ClickCellTool( (8, 25) );
		Assert.IsTrue( level.ClickCellTool( (8, 21), 0xc )?.Completed == true );
		Assert.IsTrue( placed.Runtime.Bump!.IsTrackValid, "the last line closes the new ride's circuit" );
		Assert.AreEqual( CellToolMode.Queue, level.CellTool.Mode, "the placement left the ride's queue start pending" );
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
