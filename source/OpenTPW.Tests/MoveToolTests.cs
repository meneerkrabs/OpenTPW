using System;
using System.Collections.Generic;
using System.Linq;
using OpenTPW.UI.Original;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The Move tool (UI-104, interface mode 0x3b of tp.exe): the enabled test of the object windows' b_move (0x52c2b0), the placement's purchase rules (the constructor 0x4db090
/// charges the whole price again; the pick-up's erase credits the scrap value exactly as the erase button does) and the tool's pointer mode.
/// </summary>
[TestClass]
public class MoveToolTests
{
	/// <summary>A track cell's arm count for a hand-made layer: the cells of the dictionary hold the given number of set bits.</summary>
	private static Func<int, int, int> Arms( params ((int X, int Y) Cell, int Count)[] cells )
	{
		var map = cells.ToDictionary( item => item.Cell, item => item.Count );
		return ( x, y ) => map.GetValueOrDefault( (x, y) );
	}

	[TestMethod]
	public void ARideWithoutAKartOrWaterTrackIsAlwaysMovable()
	{
		// [BIN:TPW-EXE:0x52c2b0 move enabled test] a type track word of 0 or 3 (coaster) leaves the answer at 1 without reading a cell
		foreach ( var trackType in new[] { 0, 3 } )
			Assert.IsTrue( Level.MoveEnabled( trackType, 10, 10, 0, ( _, _ ) => throw new InvalidOperationException( "no cell is read" ) ), $"track word {trackType}" );
	}

	[TestMethod]
	public void AKartOrWaterRideNeedsOneArmOnTheTilesBesideItsStation()
	{
		// the exit tile is the thing's cell plus (4, 1), (1, -5), (-5, -2), (-2, 4) and the entry tile plus (-2, 1), (1, 1), (1, -2), (-2, -2) by the angle (0x52c2e8..0x52c405)
		var cases = new (int Rotation, (int X, int Y) Exit, (int X, int Y) Entry)[]
		{
			(0, (14, 11), (8, 11)),
			(90, (11, 5), (11, 11)),
			(180, (5, 8), (11, 8)),
			(270, (8, 14), (8, 8)),
		};
		foreach ( var trackType in new[] { 1, 2 } )
		{
			foreach ( var (rotation, exit, entry) in cases )
			{
				Assert.IsTrue( Level.MoveEnabled( trackType, 10, 10, rotation, Arms( (exit, 1), (entry, 1) ) ), $"type {trackType} at {rotation}: one arm each" );
				Assert.IsFalse( Level.MoveEnabled( trackType, 10, 10, rotation, Arms( (exit, 1) ) ), $"type {trackType} at {rotation}: no entry tile" );
				Assert.IsFalse( Level.MoveEnabled( trackType, 10, 10, rotation, Arms( (entry, 1) ) ), $"type {trackType} at {rotation}: no exit tile" );
				Assert.IsFalse( Level.MoveEnabled( trackType, 10, 10, rotation, Arms( (exit, 2), (entry, 1) ) ), $"type {trackType} at {rotation}: a track leaves the exit tile" );
				Assert.IsFalse( Level.MoveEnabled( trackType, 10, 10, rotation, Arms( (exit, 1), (entry, 2) ) ), $"type {trackType} at {rotation}: a track joins the entry tile" );
				Assert.IsFalse( Level.MoveEnabled( trackType, 10, 10, rotation, Arms() ), $"type {trackType} at {rotation}: no track cell at all" );
			}
		}
	}

	[TestMethod]
	public void TheEnabledTestUsesTheStationsOwnExitAndEntryTiles()
	{
		// the two cells the test reads are the tiles a station's circuit leaves and joins by (TrackStation.Exit and Entry: 0x52a1a0, 0x52a490, 0x52a700)
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var station = TrackStation.FromPlacement( 40, 30, rotation );
			var read = new List<(int X, int Y)>();
			Level.MoveEnabled( 2, 40, 30, rotation, ( x, y ) => { read.Add( (x, y) ); return 1; } );
			CollectionAssert.AreEquivalent( new[] { station.Exit, station.Entry }, read, $"rotation {rotation}" );
		}
	}

	[TestMethod]
	public void APlacementWhileMovingNeedsNeitherResearchNorTheBuyList()
	{
		// [BIN:TPW-EXE:0x4db4a6 constructor charge] the constructor charges the price and nothing else: the buy list's availability and the research live in the buy window
		var park = EconomyTestData.Park( initialCash: 100000 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotResearched, park.CheckBuild( 1180 ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.CheckBuild( 1180, moving: true ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotBuyable, park.CheckBuild( 1601 ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.CheckBuild( 1601, moving: true ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.UnknownObject, park.CheckBuild( 9999, moving: true ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1180, out var built, moving: true ) );
		Assert.IsNotNull( built );
	}

	[TestMethod]
	public void APlacementWhileMovingHasNoBalanceTestButKeepsTheGoldenTicketRule()
	{
		// [BIN:TPW-EXE:0x523940 move hover] the build tool's hover refuses a price above the balance (0x523658), the Move tool's has no such compare and the debit 0x4d01f0 tests nothing;
		// the golden ticket rule stays as the build tool's (a type bought with tickets once is a cash type afterwards; the unreachable ticket-short case is left as it was)
		var poor = EconomyTestData.Park( initialCash: 1000 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotEnoughMoney, poor.CheckBuild( 1303 ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, poor.CheckBuild( 1303, moving: true ) );
		Assert.AreEqual( ParkEconomy.PurchaseResult.NotEnoughGoldenTickets, poor.CheckBuild( 1112, moving: true ) );
	}

	[TestMethod]
	public void AMoveCostsThePriceLessTheScrapCreditOfTheErase()
	{
		// the pick-up's erase credits the scrap value as the erase button does, the placement charges the whole price again (nothing of the object is kept)
		var park = EconomyTestData.Park( initialCash: 5000 );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1100, out var ride ) );
		var price = 5000 - park.Balance;
		park.RecordRideUse( ride!.Id );
		park.RecordUseFinished( ride.Id );
		park.AdvanceDays( 365 );
		var before = park.Balance;
		var credit = park.Sell( ride.Id );
		Assert.AreEqual( before + credit, park.Balance );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1100, out var again, moving: true ) );
		Assert.AreNotEqual( ride.Id, again!.Id, "a new object: no history, counters or settings come with the move" );
		Assert.AreEqual( before - (price - credit), park.Balance, "the move costs the price less the scrap credit" );
		Assert.IsTrue( credit < price, "an aged ride is worth less than its price, so the move costs" );
	}

	[TestMethod]
	public void TheMoveToolDrawsTheBuildToolsPlacingPointerWithoutACashFlag()
	{
		// [BIN:TPW-EXE:0x52f950 pointer switch] mode 0x3b shares the placing pointer of mode 4 (OriginalPointers.ForTool); [BIN:TPW-EXE:0x523940 move hover] its hover sets no cash flag (mode 4's does, 0x523658)
		Assert.AreEqual( OriginalPointers.ForTool( 4 ), OriginalPointers.ForTool( OpenTPW.Hud.ParkHud.MoveToolMode ) );
		Assert.IsTrue( OpenTPW.Hud.ParkHud.CashPointerShown( 4, 2500, 2499, null ) );
		Assert.IsFalse( OpenTPW.Hud.ParkHud.CashPointerShown( OpenTPW.Hud.ParkHud.MoveToolMode, 2500, 2499, null ), "the move hover has no balance compare" );
		Assert.AreEqual( OriginalPointers.NoPlace, OriginalPointers.ForTool( OpenTPW.Hud.ParkHud.MoveToolMode, noPlace: true ) );
	}
}
