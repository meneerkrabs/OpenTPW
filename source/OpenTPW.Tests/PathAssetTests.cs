using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The path builder on the original Jungle level (inconclusive without OPENTPW_GAME_PATH).</summary>
[TestClass]
public class PathAssetTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Jungle level and balance files." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFileSystem!;

	/// <summary>
	/// The Easymode park's guest run has the same raw hash as before the cell map became the source of truth
	/// (recorded on 025d410 + the research commit, before the flip; GuestAssetTests.JungleParkRunIsDeterministic's run:
	/// 108 admissions, 0x17B0E01EDEB2D4FB). WALK-I re-pinned it: guests walk the arrival lanes at the traced per-guest
	/// speed from s = 0 (about 0.8 cell/s at the mean base speed instead of 1 cell/s) and draw a base speed from the guest
	/// stream, so fewer reach the ticket booth within 120 s and the hash changes. WALK-F re-pinned the hash again (the traced
	/// separation force and the toilet term's needs phase move the guests); the admissions stay 103. The traced arrival rule
	/// re-pinned it once more: groups of 1.2 × (points + 20) / 6 guests arrive after the last group emptied, here every
	/// 15 timer units instead of the shipped 150, so 24 guests are admitted in 120 s. The traced needs and starting values (ORACLE.md F25) re-pinned it: a guest's
	/// exit counter now runs from the first turn and its needs start drawn, so one guest fewer (23) has been admitted by then. The traced leaving test (F26) changed the hash again (the admissions stay 23). Guests stepping off once per park turn instead of every 0.4 s
	/// (tp.exe 0x004CF3E0, ARRIVAL-001) leave 22 admitted. The original state machine of a guest in the park (state 6 deciding once per
	/// park turn, state 7 walking to a wander destination; docs/reverse/WANDER-plan.md, GUEST-010) changed the hash again and admits 23. The guests' litter meter joined the hash (WANDER-plan §6). The prankery flag joined it next (pranksters in the Jungle's balance, WANDER-plan §7), then the balloon life, balloon and costume (no shop here, so only the hashed fields), then the original's thoughts (the pick of the needs phase; rides and shops set none), then the booth's verdict on the fee (tp.exe 0x004FF5B0), whose draw shifts the guest stream (23 → 24 admitted), and the end of the invented boredom loss, then the guests' meter +0x1B8 (Energy) starting at 0 and moving only by a use's FatigueEffect (GUEST-008).
	/// The guests' draws moved onto the original's one world generator (DET-013 to DET-015): it is reseeded with the guest's id at its creation and a new arrival takes its entrance from the creation draw, so the guests' offsets, lanes and walks move (22 → 21 admissions).
	/// The booth walk's speed term (tp.exe 0x004FF730: 25 for a thing id with its low two bits 0, WALK-010) moved it again to 0x30F213E46933EB8C (21 admissions, as before).
	/// The arrival walk over the cell map (WALK-008, ARRIVAL-001: guests made at the bus stop, state 3's wait and head-of-cell rule, tp.exe 0x004FF520 and the states around it) moved it to 0xBC02D81A5FC6621D (20 admissions).
	/// Round 8 (the guests package: the booth's first verdict is the next turn's update, persistent head-first cell lists, the bin and prize rules) moved it to 0xBE549F10640102B9 and admits 22.
	/// The import of the save's queue cells (tp.exe 0x4D0B30) moved it to 0x08FB6348EBBF4D68 (23 admissions): the walk's edge test (0x4D8750) now meets the Belly Bounce's four queue
	/// cells, which a step enters only along their link, where it met empty ground before (open to an off-path step, mask 1), so the guests' steps and separation round them move.
	/// </summary>
	[TestMethod]
	public void OwnershipFlipKeepsTheJungleGuestRunIdentical()
	{
		var park = OriginalPark.Load( "jungle" );
		if ( park.Save == null )
			Assert.Inconclusive( "The Jungle Easymode.TPWI save is missing." );
		var settings = GuestSettings.Load( "jungle" );
		settings.ArrivalTimeBetween = 15;
		var simulation = new GuestSimulation( GuestPathGrid.FromOriginal( park.Map, park.Save ), settings, 1 );
		for ( var tick = 0; tick < 120 * 60; tick++ )
			simulation.Tick( Tick );
		Assert.AreEqual( 23, simulation.Admissions );
		Assert.AreEqual( 0x08FB6348EBBF4D68UL, simulation.ComputeStateHash(), $"0x{simulation.ComputeStateHash():X16}" );
	}

	[TestMethod]
	public void InitialPathCellsAreFixed()
	{
		var park = OriginalPark.Load( "jungle", readShippedSave: false );
		var cells = ParkCellMap.FromOriginal( park.Map, null );
		var initial = Enumerable.Range( 0, cells.Width * cells.Height ).Select( index => (X: index % cells.Width, Y: index / cells.Width) )
			.Where( cell => park.Map.GetFlagsAt( cell.X, cell.Y ).HasFlag( MapCellFlags.InitialPath ) ).ToArray();
		Assert.IsTrue( initial.Length > 0 );
		Assert.AreEqual( initial.Length, cells.CountOf( ParkCellType.Path ) );
		Assert.IsTrue( initial.All( cell => cells.FlagsAt( cell.X, cell.Y ) == ParkCellFlags.NoModify ), "MAP InitialPath = type 1 + NOMODIFY" );
		var grid = new GuestPathGrid( cells );
		var builder = new ParkPathBuilder( cells, grid, (ParkEconomy?)null, new OriginalParkGrid( park ) );
		var linked = initial.First( cell => grid.NeighbourCount( cell.X, cell.Y ) > 0 );
		Assert.AreEqual( CellBuildResult.NoModify, builder.Remove( linked.X, linked.Y ) );
		Assert.AreEqual( CellBuildResult.Existing, builder.CheckCell( linked.X, linked.Y ) );
	}

	/// <summary>
	/// A fresh Jungle park (MAP InitialPath only, the M3 gate's start): the builder lays a straight path off the
	/// entrance walkway at Costs.PathCell per cell, and guests sent to a shop at its far end walk every built cell.
	/// </summary>
	[TestMethod]
	public void GuestsWalkAPathBuiltFromTheEntrance()
	{
		var park = OriginalPark.Load( "jungle", readShippedSave: false );
		var settings = GuestSettings.Load( "jungle" );
		var economy = ParkEconomy.CreateForTheme( "jungle", easy: false );
		Assert.AreEqual( 20, economy.CellCost( CellPurchase.Path ), "[DATA:Standard.sam:Costs.PathCell]" );
		var grid = GuestPathGrid.FromOriginal( park.Map, null );
		var builder = new ParkPathBuilder( grid.Cells, grid, economy, new OriginalParkGrid( park ) );
		var entrance = settings.ArrivalLaneA![^1];
		Assert.IsTrue( grid.IsWalkable( entrance.X, entrance.Y ), "EntranceA is an InitialPath cell" );

		// The walkway cell nearest the entrance with ten free cells beyond it in a straight line.
		const int Length = 10;
		var field = grid.GetFlowField( entrance.X, entrance.Y );
		var start = Enumerable.Range( 0, grid.CountX * grid.CountY ).Where( index => field[index] >= 0 )
			.OrderBy( index => field[index] ).ThenBy( index => index )
			.SelectMany( index => Enumerable.Range( 0, 4 ).Select( direction => (X: index % grid.CountX, Y: index / grid.CountX, Direction: direction) ) )
			.First( candidate => Enumerable.Range( 1, Length ).All( step =>
				builder.CheckCell( candidate.X + GuestPathGrid.Directions[candidate.Direction].DX * step, candidate.Y + GuestPathGrid.Directions[candidate.Direction].DY * step ) == CellBuildResult.Ok ) );
		var (dx, dy) = GuestPathGrid.Directions[start.Direction];
		var end = (X: start.X + dx * Length, Y: start.Y + dy * Length);

		var balance = economy.Balance;
		var preview = builder.Preview( (start.X, start.Y), end );
		Assert.AreEqual( Length, preview.Built.Count );
		Assert.AreEqual( balance, economy.Balance, "the preview charges nothing" );
		var result = builder.BuildSegment( (start.X, start.Y), end );
		Console.WriteLine( $"Built {result.Built.Count} path cells from {start} to {end} for ${result.Charged}." );
		Assert.IsTrue( result.Completed, result.StoppedBy.ToString() );
		Assert.AreEqual( Length, result.Built.Count );
		CollectionAssert.AreEqual( new[] { (start.X, start.Y) }, result.Existing.ToArray(), "the walkway cell is extended, not bought" );
		Assert.AreEqual( Length * 20L, result.Charged );
		Assert.AreEqual( balance - result.Charged, economy.Balance );
		Assert.AreEqual( result.Charged, economy.Ledger.CurrentTotals[LedgerCategory.OtherCosts] );
		Assert.IsTrue( result.Built.All( cell => grid.Distance( cell.X, cell.Y, entrance.X, entrance.Y ) >= 0 ), "every built cell is linked to the entrance walkway" );

		var shop = new RideVisitorBridge( 1, "Drinks Shop", RideVisitorKind.Shop, 1, 70, 30, GuestNeeds.Thirst ) { EntranceCell = end, ExitCell = end };
		var script = GuestTests.CreateShopScript( new VisitorRideScriptEffects( shop, UnimplementedRideScriptEffects.Instance ) );
		shop.Attach( script, () => true );
		var simulation = new GuestSimulation( grid, settings, 11 ) { ArrivalsEnabled = false };
		simulation.Register( shop );
		GuestTests.LayStandCell( grid, shop );
		for ( var index = 0; index < 6; index++ )
		{
			var guest = simulation.SpawnInPark( entrance.X, entrance.Y );
			guest.AttractionId = shop.AttractionId;
			guest.State = GuestState.GoingToRide;
		}
		var visited = new HashSet<(int, int)>();
		for ( var tick = 0; tick < 90 * 60; tick++ )
		{
			simulation.Tick( Tick );
			shop.HostStep();
			script.Advance( Tick );
			foreach ( var guest in simulation.Guests )
				visited.Add( guest.Cell );
		}
		Assert.IsTrue( result.Built.All( visited.Contains ), $"guests walked every built cell: {string.Join( " ", result.Built.Where( cell => !visited.Contains( cell ) ) )} missed" );
		Assert.IsTrue( shop.BoardedTotal >= 1, $"guests reached the shop at the end of the path ({shop.BoardedTotal} served)" );
	}
}
