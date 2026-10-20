using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// The opentpw.pools extension, phase P1 (docs/POOLS.md): the extension and its save rule, the pool cell type, the paint tool and its refusals, prices and
/// refunds, guests keeping off the water, saves, and the pinned digests with the extension on and off.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PoolsTests
{
	/// <summary>A flat, always-allowed park grid of <paramref name="width"/> x <paramref name="height"/> cells, with hooks for the terrain rule and the heights.</summary>
	internal sealed class FlatGrid( int width, int height ) : IParkGrid
	{
		public Func<int, int, OriginalPlacementResult> Terrain { get; set; } = ( _, _ ) => OriginalPlacementResult.Allowed;
		public Func<int, int, float> Corner { get; set; } = ( _, _ ) => 0f;
		public int Width { get; } = width;
		public int Height { get; } = height;
		public System.Numerics.Vector2 Origin => new( -10, -10 );
		public OriginalPlacementResult CheckTerrain( int x, int y ) => Terrain( x, y );
		public float GetCellHeight( int x, int y ) => Corner( x, y );
		public float GetCornerHeight( int x, int y ) => Corner( x, y );
	}

	internal static ModSet PoolsSet( bool enabled ) => ModSet.Resolve( Array.Empty<DiscoveredMod>(),
		new ModSettings { Enabled = new Dictionary<string, bool> { [OfficialExtensions.Pools.Id] = enabled } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );

	private sealed class Rig
	{
		public ParkCellMap Cells { get; } = new( 20, 10 );
		public FlatGrid Grid { get; } = new( 20, 10 );
		public ParkEconomy? Economy { get; set; } = EconomyTestData.Park();
		public Func<int, int, bool>? Occupied { get; set; }
		public PoolBuilder Builder => new( Cells, () => Economy, Grid, ( x, y ) => Occupied?.Invoke( x, y ) == true );
	}

	// ---- The extension ----------------------------------------------------------------------------

	[TestMethod]
	public void ThePoolsUiIsUnavailableInThemeParkIncRunsEvenWithTheExtensionOn()
	{
		var edition = GameEditions.Current;
		try
		{
			using var scope = OpenTpwExtensions.Use( PoolsSet( true ) );
			Assert.IsTrue( FeatureGates.Pools );
			GameEditions.Select( GameEdition.ThemeParkInc );
			Assert.IsFalse( FeatureGates.Pools, "no pool tool, rows, changing rooms or swimmers in Theme Park Inc" );
			Assert.IsNull( PoolContent.CatalogRoot( OpenTpwExtensions.Active ), "no pool objects in a Theme Park Inc catalog" );
		}
		finally
		{
			GameEditions.Select( edition );
		}
	}

	[TestMethod]
	public void TheExtensionIsRegisteredOnByDefaultAndChangesTheSimulation()
	{
		var pools = OfficialExtensions.Pools;
		Assert.AreEqual( "opentpw.pools", pools.Id );
		Assert.IsTrue( pools.DefaultEnabled );
		Assert.IsTrue( pools.AffectsSimulation );
		Assert.AreSame( pools, OfficialExtensions.Find( "opentpw.pools" ) );
		Assert.IsTrue( ModSet.Defaults( OfficialExtensions.All ).IsEnabled( pools.Id ) );
		using ( OpenTpwExtensions.Use( PoolsSet( true ) ) )
			Assert.IsTrue( FeatureGates.Pools );
		using ( OpenTpwExtensions.Use( PoolsSet( false ) ) )
			Assert.IsFalse( FeatureGates.Pools );
		Assert.IsTrue( FeatureGates.Pools, "on in a process that set nothing" );
		Assert.IsTrue( ModSettings.Default.IsEnabled( pools.Id, pools.DefaultEnabled ), "a mods.json without a pools choice (an old file) turns it on" );
		var untouched = ModSettings.Parse( System.Text.Encoding.UTF8.GetBytes( "{\"enabled\":{\"opentpw.online\":false}}" ), new List<string>() );
		Assert.IsTrue( untouched.IsEnabled( pools.Id, pools.DefaultEnabled ), "a player who never touched the pools toggle gets it on" );
		var explicitOff = ModSettings.Parse( System.Text.Encoding.UTF8.GetBytes( "{\"enabled\":{\"opentpw.pools\":false}}" ), new List<string>() );
		Assert.IsFalse( explicitOff.IsEnabled( pools.Id, pools.DefaultEnabled ), "an explicit off stays off" );
		Assert.AreEqual( 75, ParkEconomy.PoolCellCost );
	}

	[TestMethod]
	public void SimulationModsListThePoolsExtensionButASaveRecordsItOnlyWithPoolCells()
	{
		var on = PoolsSet( true );
		CollectionAssert.Contains( on.SimulationMods.Select( mod => mod.Id ).ToArray(), "opentpw.pools" );
		var empty = new ParkCellMap( 20, 10 );
		Assert.IsNull( ParkResumeMods.Capture( on, ParkResumeMods.InUse( empty ) ), "no pool cell, no record" );
		Assert.IsNull( ParkResumeMods.Capture( on ), "and none without saying what is in use" );
		empty.SetType( 3, 3, ParkCellType.PoolWater );
		var record = ParkResumeMods.Capture( on, ParkResumeMods.InUse( empty ) )!.Single();
		Assert.AreEqual( new ParkModRecord( "opentpw.pools", "1.0.0", ModSet.NoFilesSha256 ), record );
		Assert.IsNull( ParkResumeMods.Capture( PoolsSet( false ), ParkResumeMods.InUse( empty ) ), "an extension that is off records nothing, whatever the cells" );
		empty.SetType( 3, 3, ParkCellType.Empty );
		Assert.IsNull( ParkResumeMods.Capture( on, ParkResumeMods.InUse( empty ) ), "the last cell erased: recorded no more" );
	}

	[TestMethod]
	public void LoadingChecksThePoolsRecordOnlyWhenThereIsOne()
	{
		var on = PoolsSet( true );
		var off = PoolsSet( false );
		var saved = new[] { new ParkModRecord( "opentpw.pools", "1.0.0", ModSet.NoFilesSha256 ) };
		ParkResumeMods.Require( saved, on );
		var message = Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( saved, off ) ).Message;
		StringAssert.Contains( message, "needs opentpw.pools 1.0.0" );
		Assert.AreEqual( "opentpw.pools 1.0.0", ParkResumeMods.MissingText( saved, off ) );
		// a save without a record loads with the extension on or off
		ParkResumeMods.Require( null, on );
		ParkResumeMods.Require( null, off );
		// other recorded mods keep their order rule with the pools extension unrecorded in between
		var other = new[] { new ParkModRecord( "opentpw.cross-edition-objects", "1.0.0", ModSet.NoFilesSha256 ) };
		var both = ModSet.Resolve( Array.Empty<DiscoveredMod>(), new ModSettings { Enabled = new Dictionary<string, bool> { ["opentpw.pools"] = true, ["opentpw.cross-edition-objects"] = true } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		ParkResumeMods.Require( other, both );
	}

	// ---- The cell type ----------------------------------------------------------------------------

	[TestMethod]
	public void ThePoolValueCollidesWithNoOriginalCellType()
	{
		var original = new HashSet<byte> { 0, 1, 3, ParkCellTypes.Water, ParkCellTypes.Object, ParkCellTypes.NoTerrain, ParkCellTypes.Entrance, ParkCellTypes.Exit,
			ParkCellTypes.FixedWalkway, ParkCellTypes.TrackConnection, ParkCellTypes.Hoarding, TrackCellTypes.KartOrigin, TrackCellTypes.Extension, TrackCellTypes.KartStart,
			TrackCellTypes.WaterOrigin, TrackCellTypes.WaterStart, TrackCellTypes.WaterAlternate, 0x11, 5, 23 };
		Assert.IsFalse( original.Contains( ParkCellTypes.PoolWater ) );
		Assert.AreEqual( ParkCellTypes.PoolWater, (byte)ParkCellType.PoolWater );
		Assert.AreEqual( (byte)CellToolMode.Pool, ParkCellTypes.PoolWater, "a tool's mode is the cell type it writes" );
		Assert.IsTrue( ParkCellTypes.PoolWater > ParkCellTypes.FixedWalkway, "above every type the original's placement and map init write" );
		Assert.IsFalse( ParkCellTypes.IsTrackCell( ParkCellTypes.PoolWater ) || ParkCellTypes.IsTrackExtension( ParkCellTypes.PoolWater ) || ParkCellTypes.IsObjectCell( ParkCellTypes.PoolWater ) );
		Assert.IsFalse( ParkCellTypes.CarriesQueueLink( ParkCellTypes.PoolWater ) );
		Assert.IsFalse( TrackCellTypes.IsWalkable( ParkCellTypes.PoolWater ) );
	}

	[TestMethod]
	public void PoolWaterGoesOnEmptyCellsOnlyAndErasesBackToEmpty()
	{
		for ( var old = 0; old < 256; old++ )
		{
			var allowed = ParkCellMap.CanChangeCellType( (byte)old, ParkCellTypes.PoolWater, lastCell: false );
			Assert.AreEqual( old is 0 or ParkCellTypes.PoolWater, allowed, $"pool over {old}" );
			Assert.AreEqual( old is 0 or ParkCellTypes.PoolWater, ParkCellMap.CanChangeCellType( (byte)old, ParkCellTypes.PoolWater, lastCell: true ) );
		}
		Assert.IsTrue( ParkCellMap.CanChangeCellType( ParkCellTypes.PoolWater, 0, false ), "clearing a pool cell" );
		foreach ( var type in new byte[] { 1, 3, 4, 9, 10, 11, 16, 21 } )
			Assert.IsFalse( ParkCellMap.CanChangeCellType( ParkCellTypes.PoolWater, type, false ), $"{type} over a pool cell" );
		// the originals' rules are unchanged by the new case
		Assert.IsTrue( ParkCellMap.CanChangeCellType( 1, 4, false ) );
		Assert.IsFalse( ParkCellMap.CanChangeCellType( 4, 4, false ) );
		Assert.IsTrue( ParkCellMap.CanChangeCellType( 3, 1, false ) );
	}

	[TestMethod]
	public void APoolCellIsNotWalkableAndCountsInTheCanonicalHashOnlyWhenPresent()
	{
		var cells = new ParkCellMap( 20, 10 );
		var grid = new GuestPathGrid( cells );
		cells.SetType( 4, 4, ParkCellType.PoolWater );
		cells.SetLinks( 4, 4, 0xFF );
		Assert.IsFalse( grid.IsWalkable( 4, 4 ), "walkability stays == Path, whatever the links" );
		Assert.IsFalse( grid.IsQueue( 4, 4 ) );
		Assert.AreEqual( 1, cells.CountOf( ParkCellType.PoolWater ) );
		var withPool = HashOf( cells );
		var without = HashOf( new ParkCellMap( 20, 10 ) );
		Assert.AreNotEqual( without, withPool );
		cells.SetType( 4, 4, ParkCellType.Empty );
		Assert.AreEqual( 0, cells.CountOf( ParkCellType.PoolWater ) );
	}

	private static ulong HashOf( ParkCellMap cells )
	{
		var hash = new StateHasher();
		cells.AddCanonicalState( hash );
		return hash.Value;
	}

	// ---- Painting, erasing, money ---------------------------------------------------------------------

	[TestMethod]
	public void PaintingChargesSeventyFiveAndErasingRefundsIt()
	{
		using var scope = OpenTpwExtensions.Use( PoolsSet( true ) );
		var rig = new Rig();
		var builder = rig.Builder;
		var start = rig.Economy!.Balance;
		Assert.AreEqual( 75, builder.CellCost );
		var stroke = builder.PaintCells( PoolBuilder.Rectangle( (2, 2), (4, 3) ) );
		Assert.IsTrue( stroke.Completed );
		Assert.AreEqual( 6, stroke.Cells.Count );
		Assert.AreEqual( 6 * 75, stroke.Charged );
		Assert.AreEqual( start - 6 * 75, rig.Economy.Balance );
		Assert.AreEqual( 6, rig.Cells.CountOf( ParkCellType.PoolWater ) );
		// painting again changes and charges nothing
		var again = builder.PaintCells( PoolBuilder.Rectangle( (2, 2), (4, 3) ) );
		Assert.AreEqual( 0, again.Cells.Count );
		Assert.IsTrue( again.Completed );
		Assert.AreEqual( start - 6 * 75, rig.Economy.Balance );
		var erase = builder.EraseCells( PoolBuilder.Rectangle( (2, 2), (3, 3) ) );
		Assert.AreEqual( 4, erase.Cells.Count );
		Assert.AreEqual( 4 * 75, erase.Refunded );
		Assert.AreEqual( start - 2 * 75, rig.Economy.Balance );
		Assert.AreEqual( 2, rig.Cells.CountOf( ParkCellType.PoolWater ) );
		Assert.AreEqual( ParkCellType.Empty, rig.Cells.TypeAt( 2, 2 ) );
		var notPool = builder.EraseCells( new[] { (9, 9) } );
		Assert.AreEqual( PoolBuildResult.NotPool, notPool.FirstRefusal!.Value.Reason );
		Assert.AreEqual( start - 2 * 75, rig.Economy.Balance, "nothing refunded for a cell that was no pool" );
	}

	[TestMethod]
	public void APoolWithoutAnEconomyIsFree()
	{
		using var scope = OpenTpwExtensions.Use( PoolsSet( true ) );
		var rig = new Rig { Economy = null };
		var stroke = rig.Builder.PaintCells( new[] { (1, 1), (2, 1) } );
		Assert.AreEqual( 0, rig.Builder.CellCost );
		Assert.AreEqual( 2, stroke.Cells.Count );
		Assert.AreEqual( 0, rig.Builder.EraseCells( new[] { (1, 1) } ).Refunded );
	}

	[TestMethod]
	public void AStrokeEndsWhereTheMoneyRunsOut()
	{
		using var scope = OpenTpwExtensions.Use( PoolsSet( true ) );
		var rig = new Rig { Economy = EconomyTestData.Park( initialCash: 160 ) };
		var stroke = rig.Builder.PaintCells( PoolBuilder.Rectangle( (0, 0), (5, 0) ) );
		Assert.AreEqual( 2, stroke.Cells.Count );
		Assert.AreEqual( PoolBuildResult.NotEnoughMoney, stroke.FirstRefusal!.Value.Reason );
		Assert.AreEqual( (2, 0), stroke.FirstRefusal.Value.Cell );
		Assert.AreEqual( 10, rig.Economy.Balance );
	}

	[TestMethod]
	public void EveryPlacementRefusalHasItsOwnReason()
	{
		using var scope = OpenTpwExtensions.Use( PoolsSet( true ) );
		var rig = new Rig();
		var builder = rig.Builder;
		PoolBuildResult Check( int x, int y ) => builder.Check( x, y );

		// outside the map and the grid
		Assert.AreEqual( PoolBuildResult.OutsideTerrain, Check( -1, 0 ) );
		Assert.AreEqual( PoolBuildResult.OutsideTerrain, Check( 20, 0 ) );
		Assert.AreEqual( PoolBuildResult.OutsideTerrain, Check( 0, 10 ) );
		// land the park does not own
		rig.Cells.SetFlags( 1, 1, ParkCellFlags.Unowned );
		Assert.AreEqual( PoolBuildResult.NotOwned, Check( 1, 1 ) );
		// paths (a fixed one too), queues, entrances, exits, objects, track cells and the map's water
		rig.Cells.SetType( 2, 1, ParkCellType.Path );
		Assert.AreEqual( PoolBuildResult.Occupied, Check( 2, 1 ), "path" );
		rig.Cells.SetFlags( 2, 1, ParkCellFlags.NoModify );
		Assert.AreEqual( PoolBuildResult.Occupied, Check( 2, 1 ), "fixed path" );
		rig.Cells.SetType( 3, 1, ParkCellType.Queue );
		Assert.AreEqual( PoolBuildResult.Occupied, Check( 3, 1 ), "queue" );
		foreach ( var (type, column) in new (byte, int)[] { (ParkCellTypes.Entrance, 4), (ParkCellTypes.Exit, 5), (ParkCellTypes.Object, 6), (TrackCellTypes.KartOrigin, 7), (TrackCellTypes.Extension, 8), (ParkCellTypes.TrackConnection, 10), (ParkCellTypes.Water, 9) } )
		{
			rig.Cells.SetType( column, 1, type );
			Assert.AreEqual( PoolBuildResult.Occupied, Check( column, 1 ), $"type {type}" );
		}
		rig.Cells.Tracks.Set( 11, 1, new TrackCell { Type = TrackCellTypes.WaterOrigin, Parent = -1 } );
		Assert.AreEqual( PoolBuildResult.Occupied, Check( 11, 1 ), "track layer" );
		rig.Occupied = ( x, y ) => (x, y) == (12, 1);
		Assert.AreEqual( PoolBuildResult.Occupied, Check( 12, 1 ), "a placed object or the host's reservation" );
		// the terrain rule: holes, water, rock, the entrance area
		rig.Grid.Terrain = ( x, y ) => (x, y) == (13, 1) ? OriginalPlacementResult.Water : (x, y) == (14, 1) ? OriginalPlacementResult.NoTerrainSurface : (x, y) == (15, 1) ? OriginalPlacementResult.EntranceArea : OriginalPlacementResult.Allowed;
		Assert.AreEqual( PoolBuildResult.Terrain, Check( 13, 1 ), "water" );
		Assert.AreEqual( PoolBuildResult.Terrain, Check( 14, 1 ), "hole" );
		Assert.AreEqual( PoolBuildResult.Terrain, Check( 15, 1 ), "entrance area" );
		// the ground rule
		rig.Grid.Corner = ( x, y ) => x == 17 && y == 1 ? PoolBuilder.MaximumCornerSpread + 0.01f : x == 18 && y == 1 ? PoolBuilder.MaximumCornerSpread : 0f;
		Assert.AreEqual( PoolBuildResult.TooSteep, Check( 16, 1 ), "the corner at (17, 1) is just too high for cell (16, 1) and (17, 1)" );
		Assert.AreEqual( PoolBuildResult.TooSteep, Check( 17, 1 ) );
		Assert.AreEqual( PoolBuildResult.Ok, Check( 18, 2 ), "a flat cell elsewhere" );
		rig.Grid.Corner = ( x, y ) => x == 5 && y == 5 ? PoolBuilder.MaximumCornerSpread : 0f;
		Assert.AreEqual( PoolBuildResult.Ok, Check( 5, 5 ), "a spread of exactly the threshold is level enough" );
		// money
		rig.Economy = EconomyTestData.Park( initialCash: 50 );
		Assert.AreEqual( PoolBuildResult.NotEnoughMoney, Check( 6, 6 ) );
		// a pool cell that is there
		rig.Economy = EconomyTestData.Park();
		Assert.AreEqual( PoolBuildResult.Ok, builder.Paint( 6, 6 ) );
		Assert.AreEqual( PoolBuildResult.Existing, Check( 6, 6 ) );
		// every refusal writes nothing
		var before = rig.Cells.CountOf( ParkCellType.PoolWater );
		foreach ( var (x, y) in new[] { (-1, 0), (1, 1), (2, 1), (3, 1), (12, 1), (13, 1) } )
			Assert.AreNotEqual( PoolBuildResult.Ok, builder.Paint( x, y ) );
		Assert.AreEqual( before, rig.Cells.CountOf( ParkCellType.PoolWater ) );
	}

	[TestMethod]
	public void EverythingIsRefusedWhileTheExtensionIsOff()
	{
		using var scope = OpenTpwExtensions.Use( PoolsSet( false ) );
		var rig = new Rig();
		Assert.AreEqual( PoolBuildResult.ExtensionOff, rig.Builder.Check( 3, 3 ) );
		Assert.AreEqual( PoolBuildResult.ExtensionOff, rig.Builder.Paint( 3, 3 ) );
		Assert.AreEqual( PoolBuildResult.ExtensionOff, rig.Builder.CheckErase( 3, 3 ) );
		Assert.AreEqual( 0, rig.Cells.CountOf( ParkCellType.PoolWater ) );
		Assert.IsFalse( PoolToolRig.Create( rig.Builder ).Begin( erase: false ), "the tool will not start" );
	}

	[TestMethod]
	public void TheGroundRuleUsesTheCornerSpreadOfTheCell()
	{
		var grid = new FlatGrid( 4, 4 ) { Corner = ( x, y ) => x + y == 2 ? 0.5f : 0f };
		Assert.AreEqual( 0.5f, PoolBuilder.CornerSpread( grid, 1, 1 ), 1e-6f );
		Assert.AreEqual( 0.5f, PoolBuilder.CornerSpread( grid, 0, 1 ), 1e-6f );
		Assert.AreEqual( 0f, PoolBuilder.CornerSpread( grid, 3, 3 ), 1e-6f );
	}

	// ---- The tool -----------------------------------------------------------------------------------

	[TestMethod]
	public void DraggingPaintsEveryCellTheStrokeCrossesAndUndoTakesItBack()
	{
		using var scope = OpenTpwExtensions.Use( PoolsSet( true ) );
		var rig = new Rig();
		var start = rig.Economy!.Balance;
		var tool = PoolToolRig.Create( rig.Builder );
		Assert.IsTrue( tool.Begin( erase: false ) );
		Assert.AreEqual( CellToolMode.Pool, tool.Mode );
		tool.Hover( 2, 2 );
		Assert.AreEqual( PoolBuildResult.Ok, tool.HoverResult );
		Assert.AreEqual( PoolBuildResult.Ok, tool.Press( 2, 2 ) );
		tool.Hover( 6, 5 );   // a jump: the cells between are painted too
		var stroke = tool.Release()!;
		Assert.IsFalse( stroke.Erase );
		Assert.AreEqual( PoolBuilder.DragLine( (2, 2), (6, 5) ).Count, stroke.Cells.Count );
		CollectionAssert.AreEqual( PoolBuilder.DragLine( (2, 2), (6, 5) ).ToArray(), stroke.Cells.ToArray() );
		Assert.AreEqual( stroke.Cells.Count * 75, stroke.Charged );
		Assert.AreEqual( start - stroke.Charged, rig.Economy.Balance );
		Assert.IsTrue( tool.IsActive, "the tool stays on for another stroke" );
		// a second stroke over cells that are already water and one new cell
		tool.Press( 6, 5 );
		tool.Hover( 7, 5 );
		var second = tool.Release()!;
		Assert.AreEqual( 1, second.Cells.Count );
		Assert.AreEqual( 2, tool.StrokeCount );
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( ParkCellType.Empty, rig.Cells.TypeAt( 7, 5 ) );
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( 0, rig.Cells.CountOf( ParkCellType.PoolWater ) );
		Assert.AreEqual( start, rig.Economy.Balance, "an undone stroke is refunded" );
		Assert.IsFalse( tool.Undo() );
		tool.Cancel();
		Assert.IsFalse( tool.IsActive );
		Assert.AreEqual( CellToolMode.None, tool.Mode );
	}

	/// <summary>
	/// [EXT:online-coop-commands] A stroke longer than a command's cell list (<see cref="ParkCommandCodec.MaximumCells"/>) is taken back in chunks that each fit a command and write in the order one
	/// write did, so a winding drag's Backspace still undoes it and a twin following the commands reaches the same map; a repaint that runs out of money stops the rest of the chunks.
	/// </summary>
	[TestMethod]
	public void AStrokeLongerThanACommandIsUndoneInChunksThatATwinFollows()
	{
		using var scope = OpenTpwExtensions.Use( PoolsSet( true ) );
		const int width = 128, height = 128, rows = 40;
		ParkEconomy? economy = EconomyTestData.Park( initialCash: 500_000 );
		var cells = new ParkCellMap( width, height );
		var builder = new PoolBuilder( cells, () => economy, new FlatGrid( width, height ) );
		var log = new List<ParkCommand>();
		PoolTool tool = null!;
		tool = new PoolTool( builder, command =>
		{
			log.Add( command );
			return PoolToolCommand.ApplyTo( command, builder, tool.Slides, tool.Bridges );
		} );
		// the twin writes each stroke and its undo as one write of the builder, as the tool did before the commands
		ParkEconomy? twinEconomy = EconomyTestData.Park( initialCash: 500_000 );
		var twinCells = new ParkCellMap( width, height );
		var twin = new PoolBuilder( twinCells, () => twinEconomy, new FlatGrid( width, height ) );
		void Winding()
		{
			// row by row, back and forth: every cell of the first rows
			for ( var y = 0; y < rows; y++ )
			{
				tool.Hover( y % 2 == 0 ? width - 1 : 0, y );
				if ( y + 1 < rows )
					tool.Hover( y % 2 == 0 ? width - 1 : 0, y + 1 );
			}
		}
		void AssertTwin( string message )
		{
			var hash = new StateHasher();
			cells.AddCanonicalState( hash );
			var twinHash = new StateHasher();
			twinCells.AddCanonicalState( twinHash );
			Assert.AreEqual( twinHash.Value, hash.Value, message );
			Assert.AreEqual( twinEconomy!.Balance, economy!.Balance, message );
		}

		Assert.IsTrue( tool.Begin( erase: false ) );
		Assert.AreEqual( PoolBuildResult.Ok, tool.Press( 0, 0 ) );
		Winding();
		var paint = tool.Release()!;
		Assert.AreEqual( width * rows, paint.Cells.Count, "the drag painted every cell of its rows" );
		Assert.IsTrue( paint.Cells.Count > ParkCommandCodec.MaximumCells, "more cells than one command takes" );
		log.Clear();
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( 0, cells.CountOf( ParkCellType.PoolWater ), "Backspace took the whole stroke back" );
		var chunks = log.OfType<UndoPoolStrokeCommand>().ToArray();
		Assert.AreEqual( 2, chunks.Length, "two chunks" );
		foreach ( var chunk in chunks )
			Assert.IsTrue( chunk.IsWellFormed && ParkCommandCodec.Decode( ParkCommandCodec.Encode( chunk ) ).Equals( chunk ), "each chunk is a well-formed command" );
		CollectionAssert.AreEqual( paint.Cells.Reverse().ToArray(), chunks.SelectMany( chunk => chunk.Cells.Reverse() ).ToArray(), "the cells go last first, as the one write erased them" );
		twin.PaintCells( paint.Cells );
		twin.EraseCells( paint.Cells.Reverse() );
		AssertTwin( "the chunks erased what the one write erases" );

		// an erase stroke's undo paints again in order; with too little money it stops inside the first chunk and issues no second one
		var block = PoolBuilder.Rectangle( (0, 0), (width - 1, rows - 1) ).ToArray();
		builder.PaintCells( block );
		twin.PaintCells( block );
		Assert.IsTrue( tool.Begin( erase: true ) );
		tool.Press( 0, 0 );
		Winding();
		var erase = tool.Release()!;
		Assert.AreEqual( width * rows, erase.Cells.Count );
		twin.EraseCells( erase.Cells );
		AssertTwin( "the erase stroke" );
		// spend all but the money for a thousand cells on the free rows below
		var keep = 1000 * builder.CellCost;
		var below = PoolBuilder.Rectangle( (0, rows), (width - 1, height - 1) ).ToArray();
		builder.PaintCells( below.TakeWhile( _ => economy!.Balance - builder.CellCost >= keep ) );
		twin.PaintCells( below.TakeWhile( _ => twinEconomy!.Balance - twin.CellCost >= keep ) );
		log.Clear();
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( 1, log.Count, "the repaint ran out of money in the first chunk, so no second chunk followed" );
		Assert.IsTrue( erase.Cells.Skip( ParkCommandCodec.MaximumCells ).All( cell => cells.TypeAt( cell.X, cell.Y ) == ParkCellType.Empty ), "nothing past the first chunk was painted" );
		Assert.IsTrue( twin.PaintCells( erase.Cells ).RanOutOfMoney );
		AssertTwin( "the chunks painted what the one write paints" );
	}

	[TestMethod]
	public void TheEraseModeTakesPoolCellsBackAndRefundsThem()
	{
		using var scope = OpenTpwExtensions.Use( PoolsSet( true ) );
		var rig = new Rig();
		rig.Builder.PaintCells( PoolBuilder.Rectangle( (1, 1), (5, 3) ) );
		var painted = rig.Economy!.Balance;
		var tool = PoolToolRig.Create( rig.Builder );
		tool.Begin( erase: true );
		Assert.IsTrue( tool.IsErasing );
		tool.Press( 1, 1 );
		tool.Hover( 5, 1 );
		var stroke = tool.Release()!;
		Assert.IsTrue( stroke.Erase );
		Assert.AreEqual( 5, stroke.Cells.Count );
		Assert.AreEqual( 5 * 75, stroke.Refunded );
		Assert.AreEqual( painted + 5 * 75, rig.Economy.Balance );
		Assert.AreEqual( 10, rig.Cells.CountOf( ParkCellType.PoolWater ) );
		// undoing an erase paints the cells again
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( 15, rig.Cells.CountOf( ParkCellType.PoolWater ) );
		Assert.AreEqual( painted, rig.Economy.Balance );
		// a stroke over a path in the way is refused, cell by cell, and reports the first
		rig.Cells.SetType( 9, 5, ParkCellType.Path );
		tool.Begin( erase: false );
		tool.Press( 8, 5 );
		tool.Hover( 10, 5 );
		var mixed = tool.Release()!;
		CollectionAssert.AreEqual( new[] { (8, 5), (10, 5) }, mixed.Cells.ToArray(), "the free cells either side are painted, the path is skipped" );
		Assert.AreEqual( PoolBuildResult.Occupied, mixed.FirstRefusal!.Value.Reason );
		Assert.AreEqual( (9, 5), mixed.FirstRefusal.Value.Cell );
		Assert.AreEqual( ParkCellType.Path, rig.Cells.TypeAt( 9, 5 ) );
	}

	[TestMethod]
	public void DragLineIsFourConnectedAndIncludesBothEnds()
	{
		var line = PoolBuilder.DragLine( (1, 1), (4, 3) );
		Assert.AreEqual( (1, 1), line[0] );
		Assert.AreEqual( (4, 3), line[^1] );
		for ( var index = 1; index < line.Count; index++ )
			Assert.AreEqual( 1, Math.Abs( line[index].X - line[index - 1].X ) + Math.Abs( line[index].Y - line[index - 1].Y ) );
		Assert.AreEqual( 1, PoolBuilder.DragLine( (3, 3), (3, 3) ).Count );
		Assert.AreEqual( 12, PoolBuilder.Rectangle( (4, 4), (1, 2) ).Count() );
	}

	[TestMethod]
	public void PathsAndQueuesRefuseToCoverPoolWater()
	{
		using var scope = OpenTpwExtensions.Use( PoolsSet( true ) );
		var rig = new Rig();
		rig.Builder.Paint( 5, 5 );
		var paths = new ParkPathBuilder( rig.Cells, null, rig.Economy, rig.Grid );
		Assert.AreEqual( CellBuildResult.Occupied, paths.Validate( 5, 5, ParkCellType.Path, lastCell: false ), "a path over water" );
		Assert.AreEqual( CellBuildResult.Occupied, paths.Validate( 5, 5, ParkCellType.Path, lastCell: true ) );
		Assert.AreEqual( CellBuildResult.Ok, paths.Validate( 6, 5, ParkCellType.Path, lastCell: false ), "the cell beside it is free" );
		var line = paths.BuildSegment( (3, 5), (8, 5) );
		Assert.AreEqual( CellBuildResult.Occupied, line.StoppedBy );
		Assert.AreEqual( (5, 5), line.StoppedAt );
		Assert.AreEqual( ParkCellType.PoolWater, rig.Cells.TypeAt( 5, 5 ) );
		Assert.IsFalse( ParkCellMap.CanChangeCellType( ParkCellTypes.PoolWater, (byte)ParkCellType.Queue, lastCell: false ), "a queue cell over water" );
		Assert.AreEqual( CellBuildResult.NotPath, paths.Remove( 5, 5 ), "the path remover leaves water alone" );
		Assert.AreEqual( ParkCellType.PoolWater, rig.Cells.TypeAt( 5, 5 ) );
	}

	// ---- Guests ----------------------------------------------------------------------------------------

	[TestMethod]
	public void GuestsRouteAroundPoolCellsAndNeverStandOnThem()
	{
		using var scope = OpenTpwExtensions.Use( PoolsSet( true ) );
		var grid = GuestTests.CreateCross();
		var builder = new PoolBuilder( grid.Cells, () => null, new FlatGrid( 20, 10 ) );
		// water on both sides of the paths, touching them; the path cells themselves are refused
		var stroke = builder.PaintCells( PoolBuilder.Rectangle( (11, 3), (16, 4) ).Concat( PoolBuilder.Rectangle( (3, 6), (8, 8) ) ).Concat( new[] { (10, 5), (4, 5) } ) );
		Assert.AreEqual( 12 + 18, stroke.Cells.Count );
		Assert.AreEqual( PoolBuildResult.Occupied, stroke.FirstRefusal!.Value.Reason, "painting over the path is refused" );
		Assert.AreEqual( 29, grid.WalkableCount, "the paths are untouched" );
		var pools = Enumerable.Range( 0, 200 ).Select( index => (X: index % 20, Y: index / 20) ).Where( cell => grid.Cells.RawTypeAt( cell.X, cell.Y ) == ParkCellTypes.PoolWater ).ToHashSet();
		Assert.AreEqual( 30, pools.Count );
		Assert.IsTrue( pools.All( cell => !grid.IsWalkable( cell.X, cell.Y ) ) );
		Assert.AreEqual( 14, grid.Distance( 0, 5, 10, 9 ), "the shortest path is the same with water beside it" );
		var simulation = new GuestSimulation( grid, GuestTests.CreateSettings(), 5 );
		var seen = 0;
		for ( var tick = 0; tick < 120 * 60; tick++ )
		{
			simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			if ( tick % 15 != 0 )
				continue;
			foreach ( var guest in simulation.Guests )
			{
				seen++;
				Assert.IsFalse( pools.Contains( ((int)MathF.Floor( guest.X ), (int)MathF.Floor( guest.Y )) ), $"guest {guest.Id} at ({guest.X:0.00}, {guest.Y:0.00}) stands in the water" );
			}
		}
		Assert.IsTrue( seen > 0, "guests were in the park" );
	}

	// ---- Saves -------------------------------------------------------------------------------------------

	private static ParkLayoutSnapshot.CellData Cell( int x, int y, byte links = 0, ushort owner = 0, short placements = 0 ) =>
		new() { X = x, Y = y, Type = ParkCellTypes.PoolWater, Flags = 0, Placements = placements, Links = links, QueueLink = 0, Owner = owner };

	private static ParkResumeSnapshot WithPoolCells( ParkResumeSnapshot snapshot, params (int X, int Y)[] cells )
	{
		var records = snapshot.Layout.Cells.Cells.Concat( cells.Select( cell => Cell( cell.X, cell.Y ) ) ).ToList();
		return snapshot with { Layout = snapshot.Layout with { Cells = snapshot.Layout.Cells with { Cells = records } } };
	}

	[TestMethod]
	public void ASaveWithPoolCellsRecordsTheExtensionAndRoundTrips()
	{
		var on = PoolsSet( true );
		var snapshot = WithPoolCells( ParkResumeSaveFileTests.Envelope(), (12, 8), (13, 8), (14, 8), (14, 7) );
		using ( OpenTpwExtensions.Use( on ) )
		{
			var cells = ParkLayoutSnapshot.RestoreCellMap( snapshot.Layout );
			var recorded = snapshot with { Mods = ParkResumeMods.Capture( on, ParkResumeMods.InUse( cells ) ) };
			CollectionAssert.AreEqual( new[] { "opentpw.pools" }, recorded.Mods!.Select( mod => mod.Id ).ToArray() );
			Assert.AreEqual( 4, cells.CountOf( ParkCellType.PoolWater ) );
			var bytes = ParkResumeSaveFile.Serialize( recorded );
			StringAssert.Contains( Encoding.UTF8.GetString( bytes ), "opentpw.pools" );
			var loaded = ParkResumeSaveFile.Deserialize( bytes );
			Assert.IsTrue( ParkResumeSaveFile.Serialize( loaded ).SequenceEqual( bytes ), "byte-stable" );
			var again = ParkLayoutSnapshot.RestoreCellMap( loaded.Layout );
			Assert.AreEqual( 4, again.CountOf( ParkCellType.PoolWater ) );
			foreach ( var (x, y) in new[] { (12, 8), (13, 8), (14, 8), (14, 7) } )
			{
				Assert.AreEqual( ParkCellType.PoolWater, again.TypeAt( x, y ) );
				Assert.AreEqual( cells.OwnerAt( x, y ), again.OwnerAt( x, y ) );
			}
			var recaptured = ParkLayoutSnapshot.CaptureSnapshot( again, new ParkObjects( null!, new SandboxParkGrid(), WorldSeed.Default ) );
			Assert.AreEqual( 4, recaptured.Cells.Cells.Count( cell => cell.Type == ParkCellTypes.PoolWater ) );
		}
	}

	[TestMethod]
	public void ASaveWithPoolCellsRefusesToLoadWithTheExtensionOff()
	{
		var on = PoolsSet( true );
		var off = PoolsSet( false );
		var snapshot = WithPoolCells( ParkResumeSaveFileTests.Envelope(), (12, 8) );
		var recorded = snapshot with { Mods = ParkResumeMods.Capture( on, new HashSet<string> { "opentpw.pools" } ) };
		// the mod check names the extension, before anything else is judged
		var message = Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( recorded.Mods, off ) ).Message;
		StringAssert.Contains( message, "needs opentpw.pools 1.0.0" );
		// and even a save that hides the record cannot put water into a park with the extension off
		using ( OpenTpwExtensions.Use( off ) )
		{
			var layout = Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( snapshot.Layout ) ).Message;
			StringAssert.Contains( layout, "opentpw.pools" );
		}
		using ( OpenTpwExtensions.Use( on ) )
			ParkLayoutSnapshot.RestoreCellMap( snapshot.Layout );
		// a save with pool cells must record the extension
		var shape = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( snapshot ) ).Message;
		StringAssert.Contains( shape, "pool cells but the save does not record opentpw.pools" );
		ParkResumeSaveFile.ValidateShape( recorded );
		// a pool cell carries no links, owner, queue link or placements
		foreach ( var bad in new[] { Cell( 1, 1, links: 4 ), Cell( 1, 1, owner: 3 ), Cell( 1, 1, placements: 1 ) } )
		{
			var forged = snapshot with { Layout = snapshot.Layout with { Cells = snapshot.Layout.Cells with { Cells = new List<ParkLayoutSnapshot.CellData> { bad } } } };
			using var scope = OpenTpwExtensions.Use( on );
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( forged.Layout ) ).Message, "pool cell" );
		}
	}

	[TestMethod]
	public void TheEnvelopeWithoutPoolCellsKeepsItsBytesWhateverTheCaptureSays()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		var plain = ParkResumeSaveFile.Serialize( snapshot );
		var cells = ParkLayoutSnapshot.RestoreCellMap( snapshot.Layout );   // no pool cell, so no extension is needed to read it
		foreach ( var enabled in new[] { true, false } )
		{
			var set = PoolsSet( enabled );
			using var scope = OpenTpwExtensions.Use( set );
			var mods = ParkResumeMods.Capture( set, ParkResumeMods.InUse( cells ) );
			Assert.IsNull( mods, $"extension {enabled}: no record without pool cells" );
			var bytes = ParkResumeSaveFile.Serialize( snapshot with { Mods = mods } );
			Assert.IsTrue( bytes.SequenceEqual( plain ), $"extension {enabled}: identical bytes" );
			Assert.IsFalse( Encoding.UTF8.GetString( bytes ).Contains( "opentpw.pools", StringComparison.Ordinal ) );
			ParkResumeMods.Require( ParkResumeSaveFile.Deserialize( bytes ).Mods, set );
		}
		// a park that had pools and erased them all saves like one that never had any
		using ( OpenTpwExtensions.Use( PoolsSet( true ) ) )
		{
			var erased = new ParkCellMap( 20, 10 );
			erased.SetType( 5, 5, ParkCellType.PoolWater );
			erased.SetType( 5, 5, ParkCellType.Empty );
			Assert.IsNull( ParkResumeMods.Capture( OpenTpwExtensions.Active, ParkResumeMods.InUse( erased ) ) );
		}
	}

	// ---- Pinned digests ---------------------------------------------------------------------------------------

	[TestMethod]
	public void TogglingThePoolsExtensionKeepsEveryExistingPinWithoutPoolCells()
	{
		ulong? pathing = null;
		foreach ( var enabled in new[] { true, false } )
		{
			var set = PoolsSet( enabled );
			using var scope = OpenTpwExtensions.Use( set );
			Assert.AreEqual( enabled, FeatureGates.Pools, "the run really has the extension on or off" );
			Assert.AreEqual( enabled, set.IsEnabled( OfficialExtensions.Pools.Id ) );
			Assert.AreEqual( "", set.Digest );
			Assert.AreEqual( DeterminismTests.FixedRunHash, DeterminismTests.RunFixedPark(), $"fixed run, pools {enabled}" );
			var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 5 );
			for ( var tick = 0; tick < 120 * 60; tick++ )
				simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
			pathing ??= simulation.ComputeStateHash();
			Assert.AreEqual( pathing, simulation.ComputeStateHash() );
		}
	}

	[TestMethod]
	public void TheFixedRunWithPoolCellsHasItsOwnPinnedHash()
	{
		var hash = DeterminismTests.RunFixedParkWithPools();
		Assert.AreEqual( hash, DeterminismTests.RunFixedParkWithPools(), "repeatable in the process" );
		Assert.AreNotEqual( DeterminismTests.FixedRunHash, hash, "pool cells are part of the digest" );
		Assert.AreEqual( DeterminismTests.PoolsFixedRunHash, hash, $"0x{hash:X16}" );
	}

	// ---- Walkers that leave the paths ---------------------------------------------------------------------------------

	[TestMethod]
	public void EveryWalkMaskIsBlockedFromSteppingIntoWater()
	{
		var map = new ParkCellMap( 20, 10 );
		map.SetType( 3, 3, ParkCellType.Path );
		map.SetType( 4, 3, ParkCellType.PoolWater );
		map.SetType( 5, 5, ParkCellType.Empty );
		map.SetType( 6, 5, ParkCellType.PoolWater );
		for ( var mask = 0; mask <= 3; mask++ )
		{
			Assert.IsTrue( WalkEdges.Blocked( map, 3, 3, 1, mask ), $"mask {mask}: from the path into the water" );
			Assert.IsTrue( WalkEdges.Blocked( map, 5, 5, 1, mask ), $"mask {mask}: from open ground into the water" );
			Assert.IsFalse( WalkEdges.Commit( map, (3, 3), (4, 3), mask ), $"mask {mask}: the step commit refuses it too" );
		}
		// controls: the same steps onto open ground are what they were (walkers set down off the paths cross grass)
		Assert.IsFalse( WalkEdges.Blocked( map, 3, 3, 2, 1 ), "mask 1 steps from a path onto grass" );
		Assert.IsFalse( WalkEdges.Blocked( map, 5, 5, 0, 1 ), "and across grass" );
		Assert.IsTrue( WalkEdges.Commit( map, (5, 5), (5, 4), 1 ) );
	}

	[TestMethod]
	public void OffPathTargetsKeepOutOfTheWater()
	{
		var grid = new GuestPathGrid( 20, 10 );
		(int X, int Y)? Pick( bool water )
		{
			var cells = grid.Cells;
			cells.SetType( 6, 5, water ? ParkCellType.PoolWater : ParkCellType.Empty );
			var calls = 0;
			(int X, int Y)? routed = null;
			// the draws ask for the cell (6, 5) from the start (5, 5): x = 5 + 6 % 11 - 5, y = 5 + 5 % 11 - 5
			GuestWander.OffPath( grid, (5, 5), () => calls++ % 2 == 0 ? 6u : 5u, cell => { routed ??= cell; return true; } );
			return routed;
		}
		Assert.AreEqual( (6, 5), Pick( water: false ), "control: the draw lands on open ground and is routed" );
		Assert.IsNull( Pick( water: true ), "the same draws land in the water and are skipped (the draws themselves are unchanged)" );
	}

	// ---- The tool, further ---------------------------------------------------------------------------------------------------

	[TestMethod]
	public void SwitchingBetweenPaintAndEraseKeepsTheStrokesToUndo()
	{
		using var scope = OpenTpwExtensions.Use( PoolsSet( true ) );
		var rig = new Rig();
		var tool = PoolToolRig.Create( rig.Builder );
		tool.Begin( erase: false );
		tool.Press( 2, 2 );
		tool.Hover( 4, 2 );
		tool.Release();
		Assert.AreEqual( 1, tool.StrokeCount );
		Assert.IsTrue( tool.Begin( erase: true ), "the other row" );
		Assert.IsTrue( tool.IsErasing );
		Assert.AreEqual( 1, tool.StrokeCount, "the undo stack survives the switch" );
		Assert.IsTrue( tool.Undo() );
		Assert.AreEqual( 0, rig.Cells.CountOf( ParkCellType.PoolWater ) );
	}

	[TestMethod]
	public void ADragThatLeavesTheViewDoesNotPaintTheCellsBetween()
	{
		using var scope = OpenTpwExtensions.Use( PoolsSet( true ) );
		var rig = new Rig();
		var tool = PoolToolRig.Create( rig.Builder );
		tool.Begin( erase: false );
		tool.Press( 1, 1 );
		tool.Hover( 2, 1 );
		tool.HoverNone();   // the pointer left the park view with the button held
		Assert.IsFalse( tool.IsDragging );
		Assert.AreEqual( 1, tool.StrokeCount, "the stroke so far is kept" );
		tool.Hover( 9, 1 );  // and came back far away
		tool.Hover( 9, 2 );
		CollectionAssert.AreEquivalent( new[] { (1, 1), (2, 1) }, Enumerable.Range( 0, 200 ).Select( index => (X: index % 20, Y: index / 20) ).Where( cell => rig.Cells.TypeAt( cell.X, cell.Y ) == ParkCellType.PoolWater ).ToArray(),
			"only the cells the pointer crossed" );
	}

	// ---- Money and value ---------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void NothingIsRefundedThatWasNotCharged()
	{
		using var scope = OpenTpwExtensions.Use( PoolsSet( true ) );
		var rig = new Rig();
		rig.Economy!.RestoreBankWords( withdrawalsEnabled: false, lastBalance: rig.Economy.Balance, turnEnteredRed: 0 );
		var start = rig.Economy.Balance;
		var painted = rig.Builder.PaintCells( PoolBuilder.Rectangle( (1, 1), (3, 1) ) );
		Assert.AreEqual( 3, painted.Cells.Count );
		Assert.AreEqual( 0, painted.Charged, "the bank charged nothing" );
		Assert.AreEqual( start, rig.Economy.Balance );
		var erased = rig.Builder.EraseCells( PoolBuilder.Rectangle( (1, 1), (3, 1) ) );
		Assert.AreEqual( 3, erased.Cells.Count );
		Assert.AreEqual( 0, erased.Refunded, "so nothing is given back" );
		Assert.AreEqual( start, rig.Economy.Balance, "paint and erase make no money" );
		// with withdrawals on, the stroke records what really moved
		rig.Economy.RestoreBankWords( withdrawalsEnabled: true, lastBalance: rig.Economy.Balance, turnEnteredRed: 0 );
		var charged = rig.Builder.PaintCells( new[] { (5, 5) } );
		Assert.AreEqual( 75, charged.Charged );
		Assert.AreEqual( 75, rig.Builder.EraseCells( new[] { (5, 5) } ).Refunded );
		Assert.AreEqual( start, rig.Economy.Balance );
	}

	[TestMethod]
	public void APoolCellIsWorthWhatTheLandWasInTheParkValue()
	{
		Assert.AreEqual( ParkValueRules.CellPoints( 0, false ), ParkValueRules.CellPoints( ParkCellTypes.PoolWater, false ) );
		Assert.AreEqual( 2, ParkValueRules.CellPoints( ParkCellTypes.PoolWater, false ) );
		using var scope = OpenTpwExtensions.Use( PoolsSet( true ) );
		var rig = new Rig();
		rig.Economy!.Litter.Cells = rig.Cells;
		var before = rig.Economy.ParkValue;
		rig.Builder.PaintCells( PoolBuilder.Rectangle( (2, 2), (5, 4) ) );
		Assert.AreEqual( before, rig.Economy.ParkValue, "painting water neither adds nor takes away park value" );
	}

	[TestMethod]
	public void PoolStrokesDoNotCountAsPathChangesForTheDrawing()
	{
		var cells = new ParkCellMap( 20, 10 );
		cells.SetType( 3, 3, ParkCellType.Path );
		var other = cells.Version - cells.PoolWrites;
		cells.SetType( 5, 5, ParkCellType.PoolWater );
		cells.SetType( 5, 5, ParkCellType.Empty );
		cells.SetType( 6, 6, ParkCellType.PoolWater );
		Assert.AreEqual( other, cells.Version - cells.PoolWrites, "pool writes move the version but not the path version" );
		cells.SetType( 7, 7, ParkCellType.Queue );
		cells.SetFlags( 7, 7, ParkCellFlags.NoModify );
		Assert.AreEqual( other + 2, cells.Version - cells.PoolWrites, "any other write does" );
	}

	// ---- The ground mesh's holes ------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheGroundMeshLeavesOutRenderHolesAndTheHeightsStayWhatTheyWere()
	{
		var heights = new float[4 * 4];
		for ( var index = 0; index < heights.Length; index++ )
			heights[index] = index * 0.1f;
		var words = Enumerable.Range( 0, 9 ).Select( index => index == 4 ? ModelHeightfield.HoleCellWord : 0x00010000u ).ToArray();
		var field = new ModelHeightfield( 3, 3, 10, 10, 0, 0, Array.Empty<uint>(), heights, words );
		var all = OriginalTerrain.HeightfieldCells( field, null ).ToArray();
		Assert.AreEqual( 8, all.Length, "the original hole (the middle cell) never has a surface" );
		var cornersBefore = Enumerable.Range( 0, 16 ).Select( index => field.GetCornerHeight( index % 4, index / 4 ) ).ToArray();
		var sampledBefore = LandscapeSampler.Sample( 3, 3, 10, 10, field.GetCornerHeight, field.SplitsAlongAntiDiagonal, 5f, 5f );
		var holed = OriginalTerrain.HeightfieldCells( field, ( x, y ) => (x, y) is (0, 0) or (2, 1) ).ToArray();
		CollectionAssert.AreEqual( all.Where( cell => (cell.X, cell.Y) is not ((0, 0) or (2, 1)) ).ToArray(), holed, "exactly the render-hole cells are skipped" );
		// the render holes change the drawn mesh and nothing else: heights, the sampler and the cell words are as before
		CollectionAssert.AreEqual( cornersBefore, Enumerable.Range( 0, 16 ).Select( index => field.GetCornerHeight( index % 4, index / 4 ) ).ToArray() );
		Assert.AreEqual( sampledBefore, LandscapeSampler.Sample( 3, 3, 10, 10, field.GetCornerHeight, field.SplitsAlongAntiDiagonal, 5f, 5f ) );
		Assert.IsFalse( field.IsHole( 0, 0 ), "the heightfield's own hole bit is not touched" );
		Assert.IsTrue( field.IsHole( 1, 1 ) );
		// the rule that feeds it
		var cells = new ParkCellMap( 3, 3 );
		cells.SetType( 0, 0, ParkCellType.PoolWater );
		Assert.IsTrue( PoolRenderHoles.IsHole( cells, 0, 0, extensionOn: true ) );
		Assert.IsFalse( PoolRenderHoles.IsHole( cells, 0, 0, extensionOn: false ), "off: the ground is drawn whole" );
		Assert.IsFalse( PoolRenderHoles.IsHole( cells, 1, 0, extensionOn: true ) );
		CollectionAssert.AreEqual( all.Where( cell => (cell.X, cell.Y) != (0, 0) ).ToArray(), OriginalTerrain.HeightfieldCells( field, ( x, y ) => PoolRenderHoles.IsHole( cells, x, y, true ) ).ToArray() );
		CollectionAssert.AreEqual( all, OriginalTerrain.HeightfieldCells( field, ( x, y ) => PoolRenderHoles.IsHole( cells, x, y, false ) ).ToArray() );
	}
}
