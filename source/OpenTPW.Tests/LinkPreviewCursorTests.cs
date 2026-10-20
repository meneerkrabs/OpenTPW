using System.Numerics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class LinkPreviewCursorTests
{
	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 16;
		public int Height => 16;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private static (GuestPathGrid Grid, ParkPathBuilder Builder) PathRig()
	{
		var grid = new GuestPathGrid( 16, 16 );
		grid.Cells.SetType( 4, 4, ParkCellType.Queue );
		grid.Cells.SetType( 4, 5, ParkCellType.Queue );
		grid.Cells.SetLinks( 4, 4, GuestPathGrid.LinkValue( 2 ) );
		return (grid, new ParkPathBuilder( grid.Cells, grid, (ParkEconomy?)null, new FlatGrid() ));
	}

	private static bool Link( CellBuildTool tool ) => ParkHud.LinkPointerShown( (int)tool.Mode, tool.Ghost );

	[TestMethod]
	public void PathPreviewProducesLinkOnlyAtAnAcceptedQueueEndWithoutWriting()
	{
		var (grid, builder) = PathRig();
		var version = grid.Version;
		var ghost = builder.Preview( (2, 4), (4, 4) );
		Assert.IsTrue( ghost.Completed );
		Assert.IsTrue( ghost.LinkPointer );
		Assert.IsTrue( ParkHud.LinkPointerShown( 1, ghost ) );
		Assert.AreEqual( version, grid.Version );
		Assert.IsFalse( builder.Preview( (2, 3), (4, 3) ).LinkPointer );
		grid.Cells.SetLinks( 4, 4, 0 );
		Assert.IsFalse( builder.Preview( (2, 4), (4, 4) ).LinkPointer );
	}

	[TestMethod]
	public void GivesWayNormalizationDoesNotInventAQueueLink()
	{
		var (_, builder) = PathRig();
		builder.ObjectGivesWay = ( x, y ) => x == 4 && y == 4;
		var ghost = builder.Preview( (2, 4), (4, 4) );
		Assert.IsTrue( ghost.Completed );
		Assert.IsFalse( ghost.LinkPointer );
	}

	[TestMethod]
	public void RefusalBeforeOrAfterALinkNeverExposesTheCompletedPreviewPointer()
	{
		var (grid, builder) = PathRig();
		grid.Cells.SetFlags( 3, 4, ParkCellFlags.Unowned );
		var before = builder.Preview( (2, 4), (6, 4) );
		Assert.IsFalse( before.LinkPointer );
		Assert.IsFalse( ParkHud.LinkPointerShown( 1, before ) );
		grid.Cells.SetFlags( 3, 4, 0 );
		grid.Cells.SetFlags( 5, 4, ParkCellFlags.Unowned );
		var after = builder.Preview( (2, 4), (6, 4) );
		Assert.IsTrue( after.LinkPointer, "the earlier accepted cell is recorded" );
		Assert.IsFalse( after.Completed );
		Assert.IsFalse( ParkHud.LinkPointerShown( 1, after ) );
	}

	[TestMethod]
	public void RealToolHoverCommitCancelAndModeSwitchDoNotKeepAStaleLink()
	{
		var (_, builder) = PathRig();
		var tool = new CellBuildTool();
		tool.Enter( builder, (2, 4) );
		tool.Hover( (4, 4) );
		Assert.IsTrue( Link( tool ) );
		tool.Hover( (3, 4) );
		Assert.IsFalse( Link( tool ) );
		tool.Hover( (4, 4) );
		tool.Cancel();
		Assert.IsFalse( Link( tool ) );
		tool.Enter( builder, (2, 4) );
		tool.Hover( (4, 4) );
		tool.Click( (4, 4) );
		Assert.IsFalse( Link( tool ) );
		var (_, freshBuilder) = PathRig();
		tool.Enter( freshBuilder, (2, 4) );
		tool.Hover( (4, 4) );
		Assert.IsTrue( Link( tool ) );
		tool.Enter( QueueWriter( new GuestPathGrid( 16, 16 ) ), (2, 2) );
		Assert.IsFalse( Link( tool ) );
	}

	private static QueueLineWriter QueueWriter( GuestPathGrid grid )
	{
		var ride = new RideVisitorBridge( 1, "Queue", RideVisitorKind.Ride, 5, 55, 30 )
		{
			EntranceCell = (2, 0), ExitCell = (2, 0), QueueFrontCell = (2, 5), QueueEntranceDirection = 2,
			Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 )
		};
		return new QueueLineWriter( grid, ride,
			( x, y, pending ) => QueuePaths.CheckExtend( grid, ride, x, y, pending ),
			( x, y ) => QueuePaths.TryExtend( grid, ride, x, y ),
			( x, y ) => QueuePaths.RemoveFrom( grid, ride, x, y ), () => 75 );
	}

	[TestMethod]
	public void QueueLastPathJoinProducesLinkButOrdinaryQueueDoesNot()
	{
		var grid = new GuestPathGrid( 16, 16 );
		grid.SetPath( 2, 2, true );
		var writer = QueueWriter( grid );
		var version = grid.Version;
		var tool = new CellBuildTool();
		tool.Enter( writer, (2, 5) );
		var ghost = tool.Hover( (2, 2) )!;
		Assert.IsTrue( ghost.Completed );
		Assert.IsTrue( ghost.LinkPointer );
		Assert.IsTrue( Link( tool ) );
		Assert.AreEqual( version, grid.Version );
		tool.Hover( (2, 3) );
		Assert.IsFalse( Link( tool ) );
	}

	[TestMethod]
	public void ActualHudSelectorReadsTheCurrentToolGhostAndKeepsUiAndCashPriority()
	{
		const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
		var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
		var level = (Level)typeof( ParkHud ).GetField( "level", hidden )!.GetValue( hud )!;
		Input.CursorTypes Select() => (Input.CursorTypes)typeof( ParkHud ).GetMethod( "ChooseCursor", hidden )!.Invoke( hud, null )!;
		var (grid, builder) = PathRig();
		level.CellTool.Enter( builder, (2, 4) );
		level.CellTool.Hover( (4, 4) );
		Assert.AreEqual( OriginalPointers.TypeOf( 5 ), Select() );
		typeof( ParkHud ).GetField( "pointerOverUi", hidden )!.SetValue( hud, true );
		Assert.AreEqual( Input.CursorTypes.Normal, Select() );
		typeof( ParkHud ).GetField( "pointerOverUi", hidden )!.SetValue( hud, false );
		level.CellTool.Cancel();
		Assert.AreEqual( OriginalPointers.TypeOf( OriginalPointers.ForTool( 0 ) ?? 2 ), Select() );
		var queueGrid = new GuestPathGrid( 16, 16 );
		queueGrid.SetPath( 2, 2, true );
		level.CellTool.Enter( QueueWriter( queueGrid ), (2, 5) );
		level.CellTool.Hover( (2, 2) );
		Assert.AreEqual( OriginalPointers.TypeOf( 5 ), Select() );
		level.CellTool.Hover( (2, 3) );
		Assert.AreEqual( OriginalPointers.TypeOf( OriginalPointers.ForTool( 3 )!.Value ), Select() );
		var poor = new ParkPathBuilder( grid.Cells, grid, EconomyTestData.Park( initialCash: 20 ), new FlatGrid() );
		level.CellTool.Enter( poor, (4, 4) );
		var refused = level.CellTool.Hover( (6, 4) )!;
		Assert.IsTrue( refused.LinkPointer );
		Assert.AreEqual( CellBuildResult.NotEnoughMoney, refused.StoppedBy );
		Assert.AreEqual( OriginalPointers.TypeOf( OriginalPointers.Cash ), Select() );
	}

	[TestMethod]
	public void ExistingPointerPriorityAndNonLineModesRemainUnchanged()
	{
		var (_, builder) = PathRig();
		var ghost = builder.Preview( (2, 4), (4, 4) );
		var link = ParkHud.LinkPointerShown( 1, ghost );
		Assert.AreEqual( 5, OriginalPointers.ForTool( 1, link: link ) );
		Assert.AreEqual( OriginalPointers.NoPlace, OriginalPointers.ForTool( 1, link: link, noPlace: true ) );
		Assert.AreEqual( OriginalPointers.Cash, OriginalPointers.ForTool( 1, link: link, noPlace: true, cash: true ) );
		Assert.IsFalse( ParkHud.LinkPointerShown( 0, ghost ) );
		Assert.IsFalse( ParkHud.LinkPointerShown( 4, ghost ) );
		Assert.IsFalse( ParkHud.LinkPointerShown( 11, ghost ) );
		Assert.IsFalse( ParkHud.LinkPointerShown( 1, null ) );
	}
}
