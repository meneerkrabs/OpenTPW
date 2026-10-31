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
/// A track record under a cell refuses an object's footprint over it, though the cell's own type is a path, against a real <see cref="Level"/> (RIDES-018, the placement validator 0x535670 mode 4,
/// 0x535BB3..0x535BD0): the guests' and the object rules' reads of the second layer go through the level's own wiring.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TrackRecordPlacementLevelTests
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

	[TestMethod]
	public void AFootprintCellOverATrackRecordIsRefusedThoughItsOwnTypeIsAPath()
	{
		// the validator's mode 4 reads the cell's structure record (0x53AD30): a path cell lying over a straight tile refuses an object body, a plain path cell does not (RIDES-018)
		Cells.SetType( 21, 21, ParkCellType.Path );
		Cells.SetType( 21, 23, ParkCellType.Path );
		Assert.IsFalse( level.Objects.IsReserved!( 21, 21 ), "a plain path cell takes an object (clearing the path)" );
		Cells.Tracks.Set( 21, 21, new TrackCell { Type = TrackCellTypes.KartOrigin, Parent = -1 } );
		Cells.Tracks.Set( 22, 21, new TrackCell { Type = TrackCellTypes.Extension, Parent = Cells.Tracks.IndexOf( 21, 21 ) } );
		Assert.AreEqual( (byte)ParkCellType.Path, Cells.RawTypeAt( 21, 21 ) );
		Assert.IsTrue( level.Objects.IsReserved!( 21, 21 ), "the origin's record is a kart tile" );
		Assert.IsTrue( level.Objects.IsReserved!( 22, 21 ), "an extension answers through its parent" );
		Assert.IsFalse( level.Objects.IsReserved!( 21, 23 ), "control: another path cell" );
		Cells.Tracks.Clear( 21, 21 );
		Cells.Tracks.Clear( 22, 21 );
		Assert.IsFalse( level.Objects.IsReserved!( 21, 21 ) );
	}
}
