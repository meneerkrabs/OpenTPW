using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.Mods;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class PoolBridgeToolTests
{
	private static (ParkCellMap Cells, PoolBridgeWorld World, PoolTool Tool) Rig()
	{
		var cells = new ParkCellMap( 16, 16 );
		var world = new PoolBridgeWorld( cells );
		var tool = PoolToolRig.Create( new PoolBuilder( cells, () => null, new PoolsTests.FlatGrid( 16, 16 ) ) );
		tool.Bridges = world;
		return (cells, world, tool);
	}

	private static void Crossing( ParkCellMap cells, int x, int y, int direction )
	{
		var (dx, dy) = GuestPathGrid.Directions[direction];
		cells.SetType( x - dx, y - dy, ParkCellType.Path );
		cells.SetType( x + 7 * dx, y + 7 * dy, ParkCellType.Path );
		for ( var i = 2; i <= 4; i++ )
			cells.SetType( x + i * dx, y + i * dy, ParkCellType.PoolWater );
	}

	[TestMethod]
	public void BridgePlacementIsSingleClickAndNeverEntersWaterUndo()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var (cells, world, tool) = Rig();
		Crossing( cells, 2, 3, 1 );
		Crossing( cells, 2, 7, 1 );
		Assert.IsTrue( tool.BeginBridge( 1 ) );
		tool.Hover( 2, 3 );
		Assert.AreEqual( PoolBuildResult.Ok, tool.HoverResult );
		Assert.AreEqual( 0, world.Count, "Hover must not mutate the park" );
		Assert.AreEqual( PoolBuildResult.Ok, tool.Press( 2, 3 ) );
		tool.Hover( 2, 7 );
		tool.Drag( 2, 7 );
		Assert.AreEqual( 1, tool.Release()!.Cells.Count );
		Assert.AreEqual( 1, world.Count );
		Assert.AreEqual( 0, tool.StrokeCount );
		Assert.IsFalse( tool.Undo() );
		Assert.AreEqual( ParkCellType.PoolWater, cells.TypeAt( 4, 3 ) );
		Assert.IsTrue( tool.Begin( false ) );
		Assert.IsFalse( tool.IsBridge );
		Assert.IsFalse( tool.Undo(), "A bridge must never be erased through pool-water undo" );
		Assert.AreEqual( 1, world.Count );
	}

	[TestMethod]
	public void HoverPreviewsTheWholeBridgeAndBlamesTheRefusingCell()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var (cells, world, tool) = Rig();
		Crossing( cells, 2, 3, 1 );
		Assert.IsTrue( tool.BeginBridge( 1 ) );
		tool.Hover( 2, 3 );
		CollectionAssert.AreEqual( Enumerable.Range( 1, 9 ).Select( x => (x, 3) ).ToArray(), tool.HoverBridgeCells().ToArray(), "both outside path ends and the seven bridge cells" );
		Assert.IsNull( tool.HoverBridgeResult );
		Assert.IsNull( tool.HoverBridgeBlocker );
		cells.SetType( 9, 3, ParkCellType.Empty );
		tool.Hover( 2, 3 );
		Assert.AreEqual( PoolBridgeResult.EndNotPath, tool.HoverBridgeResult );
		Assert.AreEqual( (9, 3), tool.HoverBridgeBlocker );
		Assert.AreEqual( OpenTpwText.PoolBridgeEndNotPath, ParkHud.BridgeRefusalText( PoolBridgeResult.EndNotPath ) );
		Assert.AreEqual( 0, world.Count, "Hover must not mutate the park" );
		tool.Press( 2, 3 );
		Assert.AreEqual( PoolBridgeResult.EndNotPath, tool.LastBridgeRefusal );
		tool.Release();
		Assert.IsTrue( tool.BeginBridge( 1, erase: true ) );
		tool.Hover( 2, 3 );
		Assert.AreEqual( 0, tool.HoverBridgeCells().Count(), "removal marks only the cell under the pointer" );
	}

	[TestMethod]
	public void EveryFixableBridgeRefusalHasItsOwnMessage()
	{
		var refusals = Enum.GetValues<PoolBridgeResult>().Where( result => result is not (PoolBridgeResult.Ok or PoolBridgeResult.ExtensionOff or PoolBridgeResult.OutsideTerrain or PoolBridgeResult.Occupied) ).ToArray();
		var texts = refusals.Select( ParkHud.BridgeRefusalText ).ToArray();
		Assert.IsFalse( texts.Contains( OpenTpwText.CannotBuildHere ), "a refusal the player can fix says what to fix" );
		Assert.AreEqual( texts.Length, texts.Distinct().Count() );
	}

	[TestMethod]
	public void SouthBridgeCanBeRemovedByAnyCoveredCellAndCancellationDoesNotRepeat()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var (cells, world, tool) = Rig();
		Crossing( cells, 4, 2, 2 );
		Assert.IsTrue( tool.BeginBridge( 2 ) );
		Assert.AreEqual( PoolBuildResult.Ok, tool.Press( 4, 2 ) );
		tool.Cancel();
		Assert.IsFalse( tool.IsActive );
		Assert.IsFalse( tool.IsBridge );
		Assert.AreEqual( 1, world.Count );
		Assert.IsTrue( tool.BeginBridge( 2, erase: true ) );
		tool.Hover( 4, 5 );
		Assert.AreEqual( PoolBuildResult.Ok, tool.HoverResult );
		Assert.AreEqual( 1, world.Count );
		Assert.AreEqual( PoolBuildResult.Ok, tool.Press( 4, 5 ) );
		tool.Release();
		Assert.AreEqual( 0, world.Count );
		Assert.AreEqual( ParkCellType.PoolWater, cells.TypeAt( 4, 5 ) );
		tool.Hover( 4, 5 );
		Assert.AreEqual( PoolBridgeResult.NotBridge, tool.HoverBridgeResult );
		tool.Press( 4, 5 );
		Assert.AreEqual( PoolBridgeResult.NotBridge, tool.LastBridgeRefusal );
		Assert.IsNull( tool.Release() );
	}

	[TestMethod]
	public void RemovalRefusalKeepsBridgeAndWaterUndoHistoryIsIsolated()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var (cells, _, tool) = Rig();
		var occupied = false;
		var world = new PoolBridgeWorld( cells, occupied: ( _, _ ) => occupied );
		tool.Bridges = world;
		Crossing( cells, 2, 3, 1 );
		tool.Begin( false );
		tool.Press( 12, 12 );
		tool.Release();
		Assert.AreEqual( 1, tool.StrokeCount );
		tool.BeginBridge( 1 );
		tool.Press( 2, 3 );
		tool.Release();
		tool.BeginBridge( 1, erase: true );
		occupied = true;
		tool.Hover( 4, 3 );
		Assert.AreEqual( PoolBridgeResult.Standing, tool.HoverBridgeResult );
		Assert.AreEqual( PoolBuildResult.Standing, tool.Press( 4, 3 ) );
		Assert.AreEqual( PoolBridgeResult.Standing, tool.LastBridgeRefusal );
		Assert.IsNull( tool.Release() );
		Assert.AreEqual( 1, world.Count );
		Assert.IsFalse( tool.Undo() );
		Assert.AreEqual( 1, tool.StrokeCount );
		tool.Begin( false );
		Assert.IsNull( tool.LastBridgeRefusal );
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( ParkCellType.Empty, cells.TypeAt( 12, 12 ) );
		Assert.AreEqual( 1, world.Count );
	}

	[TestMethod]
	public void MissingWorldDisabledExtensionAndInvalidDirectionCannotStartBridgeMode()
	{
		using ( OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) ) )
		{
			var (_, _, tool) = Rig();
			Assert.IsFalse( tool.BeginBridge( -1 ) );
			Assert.IsFalse( tool.BeginBridge( 4 ) );
			tool.Bridges = null;
			Assert.IsFalse( tool.BeginBridge( 1 ) );
		}
		using ( OpenTpwExtensions.Use( PoolsTests.PoolsSet( false ) ) )
		{
			var (_, _, tool) = Rig();
			Assert.IsFalse( tool.BeginBridge( 1 ) );
		}
	}

	[TestMethod]
	public void BridgeRowsAreDistinctPoolRowsAndHaveLabelsInEveryLanguage()
	{
		var rows = new[] { ParkHud.PoolBridgeEastRow, ParkHud.PoolBridgeSouthRow, ParkHud.PoolBridgeEraseRow };
		foreach ( var row in rows ) Assert.IsTrue( ParkHud.IsPoolRow( row ) );
		Assert.AreNotEqual( rows[0].InfoId, rows[1].InfoId );
		Assert.AreNotEqual( rows[1].InfoId, rows[2].InfoId );
		foreach ( var language in SupplementaryStrings.SupportedLanguages )
			foreach ( var key in new[] { OpenTpwText.PoolBridgeEast, OpenTpwText.PoolBridgeSouth, OpenTpwText.PoolBridgeErase, OpenTpwText.PoolBridgeHelp, OpenTpwText.PoolBridgeEraseHelp } )
				Assert.IsFalse( string.IsNullOrWhiteSpace( SupplementaryStrings.Get( key, language ) ) );
	}
}
