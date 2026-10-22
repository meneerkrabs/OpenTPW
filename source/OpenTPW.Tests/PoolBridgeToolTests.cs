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
		CollectionAssert.AreEqual( Enumerable.Range( 1, 9 ).Select( x => (x, 3) ).ToArray(), tool.HoverBridgeCells().ToArray(), "both outside ends and the seven bridge cells" );
		Assert.IsNull( tool.HoverBridgeResult );
		Assert.IsNull( tool.HoverBridgeBlocker );
		cells.SetType( 9, 3, ParkCellType.Empty );
		tool.Hover( 2, 3 );
		Assert.IsNull( tool.HoverBridgeResult, "an outside end without a path is fine: the player lays it later" );
		cells.SetType( 9, 3, ParkCellType.PoolWater );
		tool.Hover( 2, 3 );
		Assert.AreEqual( PoolBridgeResult.LandIsWater, tool.HoverBridgeResult );
		Assert.AreEqual( (9, 3), tool.HoverBridgeBlocker );
		Assert.IsTrue( tool.HoverBridgeRefuses( (9, 3) ) );
		Assert.IsFalse( tool.HoverBridgeRefuses( (5, 3) ), "only the cell to blame turns red" );
		Assert.IsTrue( tool.ShowsBridgeReason, "the line reaches the pool, so the help names the reason" );
		tool.Hover( 9, 9 );
		Assert.IsNotNull( tool.HoverBridgeResult );
		Assert.IsFalse( tool.ShowsBridgeReason, "away from any pool the help keeps the instructions" );
		tool.Hover( 2, 3 );
		Assert.AreEqual( 0, world.Count, "Hover must not mutate the park" );
		tool.Press( 2, 3 );
		Assert.AreEqual( PoolBridgeResult.LandIsWater, tool.LastBridgeRefusal );
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
		var rows = new[] { ParkHud.PoolBridgeRow, ParkHud.PoolBridgeEraseRow };
		foreach ( var row in rows ) Assert.IsTrue( ParkHud.IsPoolRow( row ) );
		Assert.AreNotEqual( rows[0].InfoId, rows[1].InfoId );
		foreach ( var language in SupplementaryStrings.SupportedLanguages )
			foreach ( var key in new[] { OpenTpwText.PoolBridge, OpenTpwText.PoolBridgeErase, OpenTpwText.PoolBridgeHelp, OpenTpwText.PoolBridgeEraseHelp, OpenTpwText.PoolBridgeNeedsWater } )
				Assert.IsFalse( string.IsNullOrWhiteSpace( SupplementaryStrings.Get( key, language ) ) );
		foreach ( var language in SupplementaryStrings.SupportedLanguages )
			foreach ( var key in new[] { OpenTpwText.PoolBridge, OpenTpwText.PoolBridgeHelp } )
				Assert.IsFalse( SupplementaryStrings.Get( key, language ).Contains( '\u2019' ), "the French fonts lack U+2019: plain apostrophes only" );
	}

	[TestMethod]
	public void OneBridgeEntryTurnsThroughAllFourDirectionsLikeABlueprint()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var (cells, world, tool) = Rig();
		Assert.IsTrue( tool.BeginBridge( 1 ) );
		Assert.AreEqual( 1, tool.BridgeDirection );
		tool.Hover( 5, 5 );
		Assert.AreEqual( (4, 5), tool.HoverBridgeCells().First(), "east: the outside end is west of the click" );
		// comma: one quarter turn clockwise, full stop: three (the keys of an object's blueprint)
		tool.RotateBridge( ParkHud.BlueprintTurnSteps( HudScreenShortcut.TurnBlueprintLeft ) );
		Assert.AreEqual( 2, tool.BridgeDirection );
		Assert.AreEqual( (5, 4), tool.HoverBridgeCells().First(), "south: the blueprint follows the direction" );
		Assert.AreEqual( (5, 12), tool.HoverBridgeCells().Last() );
		tool.RotateBridge( ParkHud.BlueprintTurnSteps( HudScreenShortcut.TurnBlueprintRight ) );
		Assert.AreEqual( 1, tool.BridgeDirection );
		tool.RotateBridge( 2 );
		Assert.AreEqual( 3, tool.BridgeDirection );
		tool.RotateBridge( 1 );
		Assert.AreEqual( 0, tool.BridgeDirection );
		tool.RotateBridge( -1 );
		Assert.AreEqual( 3, tool.BridgeDirection );
		// a bridge built after turning keeps the direction and every direction places
		Crossing( cells, 9, 3, 3 );
		tool.Hover( 9, 3 );
		Assert.IsNull( tool.HoverBridgeResult );
		Assert.AreEqual( PoolBuildResult.Ok, tool.Press( 9, 3 ) );
		Assert.AreEqual( 3, world.At( 9, 3 )!.Direction );
		tool.Release();
		// turning does nothing outside the placing mode
		tool.BeginBridge( 1, erase: true );
		tool.RotateBridge( 1 );
		Assert.AreEqual( 1, tool.BridgeDirection );
		tool.Begin( false );
		tool.RotateBridge( 1 );
		Assert.AreEqual( 1, tool.BridgeDirection );
	}

	[TestMethod]
	public void ABridgeCanBeBuiltWithoutAnyPathAtItsEnds()
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		var (cells, world, tool) = Rig();
		foreach ( var direction in new[] { 0, 1, 2, 3 } )
		{
			var origin = direction switch { 0 => (13, 10), 1 => (2, 3), 2 => (4, 5), _ => (10, 14) };
			var (dx, dy) = GuestPathGrid.Directions[direction];
			for ( var i = 2; i <= 4; i++ )
				cells.SetType( origin.Item1 + i * dx, origin.Item2 + i * dy, ParkCellType.PoolWater );
			Assert.IsTrue( tool.BeginBridge( 1 ) );
			while ( tool.BridgeDirection != direction ) tool.RotateBridge( 1 );
			tool.Hover( origin.Item1, origin.Item2 );
			Assert.IsNull( tool.HoverBridgeResult, $"direction {direction}" );
			Assert.AreEqual( PoolBuildResult.Ok, tool.Press( origin.Item1, origin.Item2 ) );
			tool.Release();
			Assert.IsTrue( world.Covers( origin.Item1, origin.Item2 ) );
			Assert.AreEqual( ParkCellType.Empty, cells.TypeAt( origin.Item1 - dx, origin.Item2 - dy ), "no path was laid or needed" );
			tool.Cancel();
		}
		Assert.AreEqual( 4, world.Count );
	}
}
