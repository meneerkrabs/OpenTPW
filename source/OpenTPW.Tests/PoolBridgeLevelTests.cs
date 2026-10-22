using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// Pool bridges without end paths against a real <see cref="Level"/> (docs/POOLS.md, "Pool bridges"): the cells outside a bridge's ends take paths but no queue or object, a path laid or removed there goes through the
/// level's own wiring, and the park-resume capture of a bridge with bare ends restores.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PoolBridgeLevelTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private Level level = null!;
	private ParkEconomy economy = null!;
	private OriginalObject one = null!;
	private Dictionary<string, string> assets = null!;

	private static object? Call( object target, string name, params object[] args ) => target.GetType().GetMethod( name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public )!.Invoke( target, args );

	[TestInitialize]
	public void Initialize()
	{
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
		var type = typeof( UpgradeCatalogueDefaultsTests );
		var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { "", 0, false } )!;
		assets = (Dictionary<string, string>)type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;
		typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		(level, economy, one) = (world.Level, world.Economy, world.One);
		Call( level, "SetupPaths" );
		level.ConfigureObjectHooks();
		Assert.IsNotNull( level.PoolBuilder, "the level builds its pool tool" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		fixture?.Cleanup();
		renderer?.Dispose();
	}

	private ParkCellMap Cells => level.Guests!.Grid.Cells;

	private ParkResumeSnapshot Capture() => level.CaptureResumeSnapshot( null, "bridges", assets, new DateTimeOffset( 2026, 10, 22, 12, 0, 0, TimeSpan.Zero ) );

	private bool Blocked( string name, int x, int y ) => (bool)Call( level, name, x, y )!;

	/// <summary>A pool 3 cells wide (x 12 to 14) and a bridge across it along y 15: the ends are (9, 15) and (17, 15), both bare.</summary>
	private PoolBridge BareBridge()
	{
		Assert.IsTrue( level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (12, 10), (14, 20) ) ).Completed );
		Assert.AreEqual( PoolBridgeResult.Ok, level.PoolBridges!.TryAdd( 10, 15, 1 ) );
		return level.PoolBridges.At( 10, 15 )!;
	}

	[TestMethod]
	public void TheEndCellsOfABareBridgeTakeAPathButNoQueueAndNoObject()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var bridge = BareBridge();
		var (first, last) = (bridge.Cell( -1 ), bridge.Cell( PoolBridge.Length ));
		Assert.AreEqual( ParkCellType.Empty, Cells.TypeAt( first.X, first.Y ) );
		foreach ( var end in new[] { first, last } )
		{
			Assert.IsTrue( Blocked( "IsQueueBlocked", end.X, end.Y ), "a queue on a bare end would break the bridge's save" );
			Assert.IsFalse( Blocked( "IsPathBuildBlocked", end.X, end.Y ), "a path may be laid there later" );
			Assert.IsTrue( Blocked( "IsPathBlocked", end.X, end.Y ), "water, tracks and the rest still keep off" );
			Assert.IsTrue( level.Objects.IsReserved!( end.X, end.Y ), "no object or entrance on the end" );
		}
		for ( var i = 0; i < PoolBridge.Length; i++ )
		{
			var cell = bridge.Cell( i );
			Assert.IsTrue( Blocked( "IsQueueBlocked", cell.X, cell.Y ) );
			Assert.IsTrue( Blocked( "IsPathBuildBlocked", cell.X, cell.Y ), "the bridge's own cells take no path" );
		}
		Assert.IsFalse( Blocked( "IsQueueBlocked", first.X - 1, first.Y ), "a queue may still join an end path from the next cell" );
	}

	[TestMethod]
	public void APathLaidOnABareEndThroughTheLevelOpensTheBridgeAndItsRemovalGoesThroughTheCommand()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var bridge = BareBridge();
		var first = bridge.Cell( -1 );
		Assert.IsFalse( level.Guests!.Grid.AreConnected( first.X, first.Y, 1 ) );
		Assert.AreEqual( CellBuildResult.Ok, level.Paths!.CheckCell( first.X, first.Y ), "the level's path rule takes the bare end" );
		Cells.SetType( first.X, first.Y, ParkCellType.Path );
		Assert.AreEqual( ParkCellType.Path, Cells.TypeAt( first.X, first.Y ) );
		Assert.IsTrue( level.Guests.Grid.AreConnected( first.X, first.Y, 1 ), "the path laid later makes the bridge walkable" );
		Assert.IsFalse( level.Commands.Issue( new RemovePathCellCommand( bridge.Cell( 0 ).X, bridge.Cell( 0 ).Y ) ).Accepted, "an approach cell takes no removal" );
		Assert.IsTrue( level.Commands.Issue( new RemovePathCellCommand( first.X, first.Y ) ).Accepted, "the end path may go" );
		Assert.AreEqual( ParkCellType.Empty, Cells.TypeAt( first.X, first.Y ) );
		Assert.IsFalse( level.Guests.Grid.AreConnected( first.X, first.Y, 1 ), "and the entrance closes" );
		Assert.AreEqual( 1, level.PoolBridges!.Count );
	}

	[TestMethod]
	public void ABridgeWithBareEndsGoesThroughTheParkResumeCapture()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		BareBridge();
		var snapshot = Capture();
		Assert.IsNotNull( snapshot.PoolBridges );
		var loaded = ParkResumeJson.Deserialize<PoolBridgesSaveData>( ParkResumeJson.Serialize( snapshot.PoolBridges ) );
		level.PoolBridges!.RestoreSnapshot( loaded );
		Assert.AreEqual( 1, level.PoolBridges.Count );
		var bytes = ParkResumeSaveFile.Serialize( snapshot );
		Assert.IsTrue( bytes.Length > 0 );
	}
}
