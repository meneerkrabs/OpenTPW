using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The park's land (tp.exe's hoardings and Buy Land, <see cref="ParkLand"/>): the Hoardings.sam clicks, the fence and its
/// owned cells, the Buy Land validator's refusals, price and merge, the tool's clicks, the save round trip and the
/// fallback for saves without land. Synthetic: a 64 × 64 map with the gate posts A (21, 11) and B (13, 11) and a
/// rectangular park from x 9 to 25 and y 11 to 21 south of them, as the shipped fences keep the park south of the gate.
/// </summary>
[TestClass]
public class ParkLandTests
{
	private static readonly (int X, int Y) PostA = (21, 11), PostB = (13, 11);

	/// <summary>The rectangle park: east from post A, south, west, north and east into post B.</summary>
	private static readonly (int X, int Y)[] RectangleClicks = { (0, 0), (4, 0), (4, 10), (-12, 10), (-12, 0), (-8, 0) };

	private static ParkLandSetup Setup( params (int X, int Y)[] clicks ) => new( PostA, PostB, clicks );

	private static ParkCellMap NewPark( params (int X, int Y)[] clicks )
	{
		var cells = new ParkCellMap( 64, 64 );
		var land = ParkLand.FromClicks( Setup( clicks.Length == 0 ? RectangleClicks : clicks ) );
		land.ApplyToNewPark( cells );
		cells.SetLand( land );
		return cells;
	}

	private static bool Owned( ParkCellMap cells, int x, int y ) => !cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );

	private static ulong Hash( ParkCellMap cells )
	{
		var hash = new StateHasher();
		cells.AddCanonicalState( hash );
		return hash.Value;
	}

	private static ParkEconomy Economy( int cash, int mapCell ) => new(
		new BalanceSettings( new SamSettings( new[] { SamDocument.Parse( EconomyTestData.StandardText( cash ).Replace( "Costs.MapCell 100", $"Costs.MapCell {mapCell}" ), "Standard.sam" ) } ),
			null, null, "jungle", false ),
		EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7 );

	private static LandPurchaseCheck Check( ParkCellMap cells, (int X, int Y) first, (int X, int Y) second, long cost = 100, long? balance = null,
		Func<int, int, bool>? barrier = null ) =>
		cells.Land!.CheckPurchase( cells, first, second, barrier ?? (( _, _ ) => false), cost, balance );

	[TestMethod]
	public void EveryOwnedFencePostAndItsBlockCountFiftyInTheParkValueAndTheBigParkCount()
	{
		// tp.exe 0x5323F0: the fence's posts are structure records of type 25 (0x00524960 in mode 0x19) and the walk 0x52BA50 types the three cells beside each as its extensions; 0x004C7DB0 adds 50 for each such cell
		// on an owned cell (0x005363F0 follows an extension to its parent), 0x004CD7F0 counts it as covered owned or not
		var park = EconomyTestData.Park();
		var cells = NewPark();
		park.Litter.Cells = cells;
		var land = cells.Land!;
		var structure = Enumerable.Range( 0, 128 * 128 ).Select( index => (X: index % 128, Y: index / 128) ).Where( cell => land.CountsAsStructure( cell.X, cell.Y, 128, 128 ) ).ToList();
		var owned = structure.Count( cell => Owned( cells, cell.X, cell.Y ) );
		Assert.IsTrue( owned > 0, "some posts stand on owned cells" );
		Assert.IsTrue( structure.Count > land.Ring.Distinct().Count() * 2, "each post counts its block of up to four cells" );
		var withPosts = park.ParkValue;
		var coveredWithPosts = park.CoveredCells();
		cells.SetLand( null );
		Assert.AreEqual( withPosts - 50 * owned, park.ParkValue, "the park value without the posts' blocks" );
		Assert.AreEqual( coveredWithPosts - structure.Count, park.CoveredCells(), "the big park counts every cell of every block, owned or not" );
	}

	// --- Hoardings.sam ---

	[TestMethod]
	public void HoardingClicksAreReadUpToTheTerminator()
	{
		var text = "#comment\r\nHoardingClicks[0].X\t0\r\nHoardingClicks[0].Y\t0\r\n\r\nHoardingClicks[1].X\t2\r\nHoardingClicks[1].Y\t-4\r\n"
			+ "HoardingClicks[2].X\t512\r\nHoardingClicks[2].Y\t512\r\nHoardingClicks[3].X\t6\r\nHoardingClicks[3].Y\t6\r\n";
		CollectionAssert.AreEqual( new[] { (0, 0), (2, -4) }, ParkLand.ParseHoardingClicks( text ).ToArray() );
		// a missing pair ends the list as the terminator does
		CollectionAssert.AreEqual( new[] { (0, 0) }, ParkLand.ParseHoardingClicks( "HoardingClicks[0].X 0\nHoardingClicks[0].Y 0\nHoardingClicks[2].X 4\nHoardingClicks[2].Y 4\n" ).ToArray() );
		// the table holds 64 pairs
		var long_ = string.Concat( Enumerable.Range( 0, 80 ).Select( index => $"HoardingClicks[{index}].X {index}\nHoardingClicks[{index}].Y 0\n" ) );
		Assert.AreEqual( ParkLand.MaximumClicks, ParkLand.ParseHoardingClicks( long_ ).Count );
		Assert.IsNull( ParkLandSetup.FromLevel( (47, 17), null, text ) );
		Assert.AreEqual( new ParkLandSetup( (51, 17), (43, 17), new[] { (0, 0), (2, -4) } ).PostA, ParkLandSetup.FromLevel( (47, 17), (48, 17), text )!.PostA );
		Assert.AreEqual( (43, 17), ParkLandSetup.FromLevel( (47, 17), (48, 17), text )!.PostB );
	}

	[TestMethod]
	public void TheFenceRunsFromPostAToPostBAlongTheClicks()
	{
		var land = ParkLand.FromClicks( Setup( RectangleClicks ) );
		Assert.AreEqual( PostA, land.Ring[0] );
		Assert.AreEqual( PostB, land.Ring[^1] );
		Assert.AreEqual( 2 + 5 + 8 + 5 + 2 + 1, land.Ring.Count, "every second cell along the clicks" );
		Assert.IsTrue( land.IsPost( 25, 15 ) && land.IsPost( 9, 21 ) && !land.IsPost( 17, 11 ), "the gate between the posts has no fence" );
		Assert.ThrowsException<InvalidDataException>( () => ParkLand.FromClicks( Setup( (2, 0), (4, 0) ) ), "the first click is on post A" );
		Assert.ThrowsException<InvalidDataException>( () => ParkLand.FromClicks( Setup( (0, 0), (4, 4), (-8, 0) ) ), "clicks run straight" );
		Assert.ThrowsException<InvalidDataException>( () => ParkLand.FromClicks( Setup( (0, 0), (4, 0), (4, 10) ) ), "the last click is on post B" );
	}

	// --- a new park's ownership ---

	[TestMethod]
	public void ANewParkOwnsTheFencedLandAndTheStripAtTheGate()
	{
		var cells = new ParkCellMap( 64, 64 );
		cells.SetFlags( 40, 40, ParkCellFlags.NoModify );
		ParkLand.FromClicks( Setup( RectangleClicks ) ).ApplyToNewPark( cells );
		for ( var y = 0; y < 64; y++ )
		{
			for ( var x = 0; x < 64; x++ )
			{
				var inside = x is >= 10 and <= 25 && y is >= 12 and <= 21;
				var strip = x is >= 15 and <= 20 && y is 11 or 12;
				// the fill scans north through the open gate from the posts of the far wall, up to the top of the map; the first such scan (x 19) sets the edge flag 0x5311C0 never clears, so the next ones (x 17, 15) stop
				// after their first block (oracle case "rectangle", checked cell for cell)
				var leak = x is >= 19 and <= 20 && y is >= 1 and <= 10;
				Assert.AreEqual( inside || strip || leak || (x, y) == (40, 40), Owned( cells, x, y ), $"({x}, {y})" );
			}
		}
		Assert.AreEqual( ParkCellFlags.NoModify, cells.FlagsAt( 40, 40 ), "a fixed cell never becomes unowned" );
	}

	[TestMethod]
	public void ACornerTurningAwayFromTheLandOwnsItsFourCells()
	{
		// the rectangle with its south-east quarter cut out: (25, 17) turns away from the land
		var cells = NewPark( (0, 0), (4, 0), (4, 6), (-4, 6), (-4, 10), (-12, 10), (-12, 0), (-8, 0) );
		Assert.IsTrue( Owned( cells, 17, 17 ) && Owned( cells, 18, 17 ) && Owned( cells, 17, 18 ) && Owned( cells, 18, 18 ), "the four cells round (17, 17)" );
		Assert.IsFalse( Owned( cells, 19, 19 ), "the cut-out quarter" );
		Assert.IsFalse( Owned( cells, 18, 19 ), "below the corner's cells" );
		Assert.IsTrue( Owned( cells, 17, 19 ), "west of the cut" );
	}

	// --- the Buy Land validator ---

	[TestMethod]
	public void ARectangleNextToTheParkIsAllowedAndPriced()
	{
		var cells = NewPark();
		var check = Check( cells, (25, 11), (33, 21) );
		Assert.AreEqual( LandPurchaseRefusal.None, check.Refusal );
		// x from 27 to 32, y from 13 to 20: 6 × 8 cells, all unowned
		Assert.AreEqual( 48, check.Cells );
		Assert.AreEqual( 4800, check.Cost );
		Assert.AreEqual( 480, Check( cells, (25, 11), (33, 21), cost: 10 ).Cost, "Costs.MapCell 10 (the jungle's easy level)" );
		Assert.AreEqual( check, Check( cells, (33, 21), (25, 11) ), "the corners' order does not matter" );
	}

	[TestMethod]
	public void ThePriceCountsOnlyUnownedCellsInsideTheNewFence()
	{
		var cells = NewPark();
		// overlapping the park: x from 21 to 32 and y from 15 to 22, owned where x <= 25 and y <= 21
		var check = Check( cells, (19, 13), (33, 23) );
		Assert.AreEqual( LandPurchaseRefusal.None, check.Refusal );
		var expected = 0;
		for ( var y = 15; y < 23; y++ )
			for ( var x = 21; x < 33; x++ )
				expected += Owned( cells, x, y ) ? 0 : 1;
		Assert.AreEqual( expected, check.Cells );
		Assert.AreEqual( 12 * 8 - 5 * 7, check.Cells );
	}

	[TestMethod]
	public void SmallOutsideAndGateRectanglesAreRefused()
	{
		var cells = NewPark();
		Assert.AreEqual( LandPurchaseRefusal.TooSmall, Check( cells, (25, 11), (27, 21) ).Refusal, "two cells wide" );
		Assert.AreEqual( LandPurchaseRefusal.TooSmall, Check( cells, (25, 11), (33, 13) ).Refusal, "two cells high" );
		Assert.AreEqual( LandPurchaseRefusal.OutsideMap, Check( cells, (57, 11), (65, 21) ).Refusal );
		Assert.AreEqual( LandPurchaseRefusal.GatePost, Check( cells, (21, 11), (29, 5) ).Refusal, "a corner on post A" );
		Assert.AreEqual( LandPurchaseRefusal.GatePost, Check( cells, (5, 5), (13, 11) ).Refusal, "a corner on post B" );
		Assert.AreEqual( LandPurchaseRefusal.GatePost, Check( cells, (11, 5), (23, 11) ).Refusal, "an edge through both gate posts" );
		Assert.AreEqual( LandPurchaseRefusal.GatePost, Check( cells, (19, 3), (29, 11) ).Refusal, "an edge through post A" );
		Assert.AreEqual( LandPurchaseRefusal.None, Check( cells, (15, 7), (19, 15) ).Refusal, "land in front of the gate: the original's validator accepts it (oracle case)" );
	}

	[TestMethod]
	public void ARectangleThatTouchesTheParkAtOneCornerIsRefused()
	{
		var cells = NewPark();
		Assert.AreEqual( LandPurchaseRefusal.TouchesAtOnePoint, Check( cells, (25, 21), (33, 29) ).Refusal );
		Assert.AreEqual( LandPurchaseRefusal.TouchesAtOnePoint, Check( cells, (33, 29), (25, 21) ).Refusal );
	}

	[TestMethod]
	public void ARectangleWithoutNewLandIsRefused()
	{
		var cells = NewPark();
		Assert.AreEqual( LandPurchaseRefusal.NoNewLand, Check( cells, (11, 13), (19, 19) ).Refusal, "inside the park" );
		Assert.AreEqual( LandPurchaseRefusal.NoNewLand, Check( cells, (37, 31), (45, 39) ).Refusal, "away from the park" );
	}

	[TestMethod]
	public void ARectangleThatEnclosesUnownedLandIsRefused()
	{
		// the park x 9..25, y 11..21 with a notch x 13..21, y 15..21 open to the south
		var u = NewPark( (0, 0), (4, 0), (4, 10), (0, 10), (0, 4), (-8, 4), (-8, 10), (-12, 10), (-12, 0), (-8, 0) );
		Assert.IsFalse( Owned( u, 17, 19 ), "the notch is not owned" );
		Assert.IsTrue( Owned( u, 17, 13 ) && Owned( u, 11, 19 ) && Owned( u, 23, 19 ) );
		// closing the notch's mouth leaves x 13..21, y 15..19 enclosed and unowned
		Assert.AreEqual( LandPurchaseRefusal.Pocket, Check( u, (11, 19), (23, 25) ).Refusal );
		// filling the whole notch leaves no pocket
		Assert.AreEqual( LandPurchaseRefusal.None, Check( u, (13, 15), (21, 25) ).Refusal );
	}

	[TestMethod]
	public void UnownedLandAgainstTheHighYEdgeIsAPocket()
	{
		// a full-size map whose bottom row (y 127) is fixed, so owned, except for x 26..32 below the purchase; tp.exe's
		// bordered land grid keeps its row 65 (below y 127) a wall over columns 1..63 (0x5321dc), so that gap is enclosed
		var cells = new ParkCellMap( ParkLand.MapSize, ParkLand.MapSize );
		for ( var x = 0; x < ParkLand.MapSize; x++ )
			if ( x is < 26 or > 32 )
				cells.SetFlags( x, ParkLand.MapSize - 1, ParkCellFlags.NoModify );
		var land = ParkLand.FromClicks( Setup( RectangleClicks ) );
		land.ApplyToNewPark( cells );
		cells.SetLand( land );
		Assert.IsFalse( Owned( cells, 29, 127 ), "the gap is not owned" );
		// down to the last post row (y 125): only the gap at y 127 is left, against the map's edge
		Assert.AreEqual( LandPurchaseRefusal.Pocket, Check( cells, (25, 11), (33, 125) ).Refusal );
		// a row short of it the gap still reaches the unowned land round the rectangle
		Assert.AreEqual( LandPurchaseRefusal.None, Check( cells, (25, 11), (33, 123) ).Refusal );
	}

	[TestMethod]
	public void TwoBarrierPostsInARowOnAnEdgeAreRefused()
	{
		var cells = NewPark();
		var water = new HashSet<(int, int)> { (33, 15), (33, 17) };
		Assert.AreEqual( LandPurchaseRefusal.Barrier, Check( cells, (25, 11), (33, 21), barrier: ( x, y ) => water.Contains( (x, y) ) ).Refusal );
		var apart = new HashSet<(int, int)> { (33, 13), (33, 17) };
		Assert.AreEqual( LandPurchaseRefusal.None, Check( cells, (25, 11), (33, 21), barrier: ( x, y ) => apart.Contains( (x, y) ) ).Refusal, "posts apart" );
	}

	[TestMethod]
	public void TheBalanceMustCoverThePriceAndMayEqualIt()
	{
		var cells = NewPark();
		Assert.AreEqual( LandPurchaseRefusal.None, Check( cells, (25, 11), (33, 21), balance: 4800 ).Refusal, "equal is allowed" );
		var refused = Check( cells, (25, 11), (33, 21), balance: 4799 );
		Assert.AreEqual( LandPurchaseRefusal.Money, refused.Refusal );
		Assert.AreEqual( 4800, refused.Cost, "the refused price is still reported (the cash pointer)" );
		Assert.AreEqual( LandPurchaseRefusal.None, Check( cells, (25, 11), (33, 21), cost: 10, balance: 480 ).Refusal );
		Assert.AreEqual( LandPurchaseRefusal.Money, Check( cells, (25, 11), (33, 21), cost: 10, balance: 479 ).Refusal );
	}

	// --- the merge ---

	[TestMethod]
	public void APurchaseMergesTheFenceAndOwnsTheNewLand()
	{
		var cells = NewPark();
		var land = cells.Land!;
		Assert.IsTrue( land.Purchase( cells, (25, 11), (33, 21) ) );
		Assert.AreEqual( PostA, land.Ring[0] );
		Assert.AreEqual( PostB, land.Ring[^1] );
		Assert.IsFalse( land.IsPost( 25, 15 ), "the shared fence is gone" );
		Assert.IsTrue( land.IsPost( 33, 15 ) && land.IsPost( 29, 21 ) && land.IsPost( 29, 11 ) );
		Assert.IsTrue( Owned( cells, 26, 12 ) && Owned( cells, 33, 21 ), "the cells next to the new fence's west and north sides and under its east and south sides" );
		Assert.IsFalse( Owned( cells, 34, 15 ) || Owned( cells, 30, 22 ) );
		// an L-shaped merge: the rectangle's corner (25, 21) lies on the old fence and turns away from the land
		var lCells = NewPark();
		Assert.IsTrue( lCells.Land!.Purchase( lCells, (25, 15), (33, 25) ) );
		Assert.IsTrue( Owned( lCells, 25, 22 ) && Owned( lCells, 26, 22 ), "the corner (25, 21) owns its cells" );
	}

	// --- the tool ---

	/// <summary>The Buy Land tool over <paramref name="cells"/>, its purchases applied straight to the map (the level hands them to its command funnel, <see cref="BuyLandCommand"/>).</summary>
	private static BuyLandTool Tool( ParkCellMap cells, ParkEconomy? economy ) =>
		new( () => cells, () => economy, ( _, _ ) => false,
			command => command is BuyLandCommand buy ? buy.Apply( cells, economy, ( _, _ ) => false ) : ParkCommandResult.Rejected( "Not a land command." ) );

	[TestMethod]
	public void TheToolBuysOneRectanglePerActivation()
	{
		var cells = NewPark();
		var economy = Economy( 50000, 100 );
		var tool = Tool( cells, economy );
		Assert.IsTrue( tool.Begin() );
		Assert.AreEqual( BuyLandClick.CornerStored, tool.Click( 26, 12 ) );
		Assert.AreEqual( (25, 11), tool.Corner, "the click snaps down to the post lattice" );
		tool.Hover( 34, 22 );
		Assert.AreEqual( (33, 21), tool.HoverPost );
		Assert.AreEqual( LandPurchaseRefusal.None, tool.HoverCheck?.Refusal );
		var balance = economy.Balance;
		Assert.AreEqual( BuyLandClick.Bought, tool.Click( 34, 22 ) );
		Assert.IsFalse( tool.IsActive, "one purchase per activation" );
		Assert.AreEqual( balance - 4800, economy.Balance );
		Assert.IsTrue( Owned( cells, 30, 15 ) );
		Assert.IsTrue( cells.Land!.IsPost( 33, 15 ) );
		Assert.AreEqual( BuyLandClick.Ignored, tool.Click( 40, 40 ) );

		// a refused second click ends the tool too, and charges nothing
		Assert.IsTrue( tool.Begin() );
		tool.Click( 11, 13 );
		var before = economy.Balance;
		Assert.AreEqual( BuyLandClick.Refused, tool.Click( 19, 19 ) );
		Assert.IsFalse( tool.IsActive );
		Assert.AreEqual( before, economy.Balance );
		Assert.AreEqual( LandPurchaseRefusal.NoNewLand, tool.LastResult?.Refusal );
	}

	/// <summary>[EXT:online-coop-commands] The purchase as a command (<see cref="BuyLandCommand"/>): its posts are checked against the fence's lattice and the validator runs again with the money.</summary>
	[TestMethod]
	public void TheBuyLandCommandChecksItsPostsAgainAndBuysWhatTheToolWould()
	{
		var cells = NewPark();
		var economy = Economy( 50000, 100 );
		var balance = economy.Balance;
		var before = Hash( cells );
		ParkCommandResult Buy( int cornerX, int cornerY, int postX, int postY ) => new BuyLandCommand( cornerX, cornerY, postX, postY ).Apply( cells, economy, ( _, _ ) => false );
		Assert.IsFalse( Buy( 26, 11, 33, 21 ).Accepted, "a corner off the post lattice is no post the tool could store" );
		Assert.IsFalse( Buy( 25, 11, 33, 22 ).Accepted, "nor is a clicked cell the tool did not snap" );
		Assert.AreEqual( before, Hash( cells ), "a refusal buys nothing" );
		Assert.AreEqual( balance, economy.Balance );
		var bought = Buy( 25, 11, 33, 21 );
		Assert.IsTrue( bought.Accepted );
		Assert.AreEqual( new LandPurchaseCheck( LandPurchaseRefusal.None, 48, 4800 ), bought.Value, "the value is the validator's verdict" );
		Assert.AreEqual( balance - 4800, economy.Balance );
		Assert.IsTrue( Owned( cells, 30, 15 ) );
		var away = Buy( 37, 31, 45, 39 );
		Assert.IsFalse( away.Accepted, "a rectangle away from the park has no land to join" );
		Assert.AreEqual( LandPurchaseRefusal.NoNewLand, ((LandPurchaseCheck)away.Value!).Refusal, "a refusal's value is the validator's verdict too" );
		Assert.AreEqual( balance - 4800, economy.Balance );
		Assert.IsFalse( new BuyLandCommand( 25, 11, 33, 21 ).Apply( new ParkCellMap( 64, 64 ), economy, ( _, _ ) => false ).Accepted, "a map without a fence sells no land" );
	}

	[TestMethod]
	public void TheToolShowsTheCashPointerWhenTheLandIsTooDear()
	{
		var cells = NewPark();
		var economy = Economy( 1000, 100 );
		var tool = Tool( cells, economy );
		tool.Begin();
		tool.Click( 25, 11 );
		tool.Hover( 33, 21 );
		Assert.IsTrue( tool.HoverRefused && tool.HoverTooDear );
		Assert.AreEqual( BuyLandClick.Refused, tool.Click( 33, 21 ) );
		Assert.AreEqual( 1000, economy.Balance );
	}

	[TestMethod]
	public void ABalanceChangeOnlyRepeatsTheHoversMoneyTest()
	{
		var cells = NewPark();
		var economy = Economy( 5000, 100 );
		var tool = Tool( cells, economy );
		tool.Begin();
		tool.Click( 25, 11 );
		tool.Hover( 33, 21 );
		Assert.AreEqual( LandPurchaseRefusal.None, tool.HoverCheck?.Refusal );
		Assert.AreEqual( 1, tool.ValidatorRuns );
		tool.Hover( 33, 21 );
		Assert.AreEqual( 1, tool.ValidatorRuns, "the same rectangle is not validated again" );
		// 4800 against 4700, then 4800 again: the refusal follows the balance without another walk of the fence
		economy.ChargeLand( 300, 0 );
		tool.Hover( 33, 21 );
		Assert.AreEqual( LandPurchaseRefusal.Money, tool.HoverCheck?.Refusal );
		Assert.IsTrue( tool.HoverTooDear );
		Assert.AreEqual( 4800, tool.HoverCheck?.Cost, "the refused price is still reported" );
		economy.CreditCoasterTrack( 100 );
		tool.Hover( 33, 21 );
		Assert.AreEqual( LandPurchaseRefusal.None, tool.HoverCheck?.Refusal, "equal is allowed" );
		Assert.AreEqual( 1, tool.ValidatorRuns, "balance changes only repeat the money test" );
		// another rectangle or a changed map runs the validator again
		tool.Hover( 35, 21 );
		Assert.AreEqual( 2, tool.ValidatorRuns );
		cells.Touch();
		tool.Hover( 35, 21 );
		Assert.AreEqual( 3, tool.ValidatorRuns );
		// a refusal other than money stays put whatever the balance
		tool.Hover( 19, 19 );
		var refusal = tool.HoverCheck?.Refusal;
		Assert.IsTrue( refusal is not (null or LandPurchaseRefusal.None or LandPurchaseRefusal.Money), $"{refusal}" );
		economy.ChargeLand( 4000, 0 );
		tool.Hover( 19, 19 );
		Assert.AreEqual( refusal, tool.HoverCheck?.Refusal );
		Assert.AreEqual( 4, tool.ValidatorRuns );
	}

	[TestMethod]
	public void SnappingClampsToTheMapAndRoundsDownToThePostLattice()
	{
		Assert.AreEqual( (21, 11), BuyLandTool.Snap( 22, 12, PostA, 64, 64 ) );
		Assert.AreEqual( (19, 9), BuyLandTool.Snap( 20, 10, PostA, 64, 64 ), "an odd negative offset rounds down" );
		Assert.AreEqual( (19, 9), BuyLandTool.Snap( 19, 9, PostA, 64, 64 ) );
		Assert.AreEqual( (1, 1), BuyLandTool.Snap( -5, 0, PostA, 64, 64 ), "clamped to 1" );
		Assert.AreEqual( (61, 61), BuyLandTool.Snap( 100, 100, PostA, 64, 64 ), "clamped to width − 2 and height − 3, then down" );
		Assert.AreEqual( (61, 59), BuyLandTool.Snap( 100, 100, PostA, 64, 63 ) );
	}

	// --- the economy ---

	[TestMethod]
	public void LandIsChargedAsOtherCosts()
	{
		var economy = Economy( 50000, 100 );
		var other = economy.Ledger.CurrentMonth( 0, 0 ).Totals.GetValueOrDefault( LedgerCategory.OtherCosts );
		economy.ChargeLand( 4800, 48 );
		Assert.AreEqual( 50000 - 4800, economy.Balance );
		Assert.AreEqual( other + 4800, economy.Ledger.CurrentMonth( 0, 0 ).Totals.GetValueOrDefault( LedgerCategory.OtherCosts ) );
	}

	// --- objects ---

	[TestMethod]
	public void ObjectsAreNotPlacedOnUnownedLand()
	{
		var objects = new ParkObjects( null!, new SandboxParkGrid() ) { IsUnowned = ( x, _ ) => x > 10 };
		var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Feature, "/levels/jungle/features/x", new ObjectSettings( new[]
		{
			new ObjectSettingsFile( "Info.Id\t1400\nInfo.Name\t\"x\"\n", "x.sam" )
		} ), ObjectShape.Single, "/x.MD2", null, null, Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(),
			Array.Empty<string>(), new BaseFileSystem( Path.GetTempPath() ), null );
		Assert.AreEqual( OriginalPlacementResult.Allowed, objects.Check( entry, 5, 5, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.NotOwned, objects.Check( entry, 11, 5, 0 ) );
		objects.IsUnowned = null;
		Assert.AreEqual( OriginalPlacementResult.Allowed, objects.Check( entry, 11, 5, 0 ), "without land every cell is owned" );
	}

	// --- saves ---

	[TestMethod]
	public void TheLandSurvivesASaveRoundTrip()
	{
		var cells = NewPark();
		var tool = Tool( cells, null );
		Assert.IsTrue( tool.Begin() );
		tool.Click( 25, 11 );
		Assert.AreEqual( BuyLandClick.Bought, tool.Click( 33, 21 ) );
		cells.SetType( 15, 15, ParkCellType.Path );
		var snapshot = ParkLayoutSnapshot.CaptureSnapshot( cells, new ParkObjects( null!, new SandboxParkGrid() ) );
		Assert.IsNotNull( snapshot.Cells.Land );
		Assert.IsTrue( snapshot.Cells.Cells.Count < 10, "unowned cells are not recorded one by one" );
		var revived = JsonSerializer.Deserialize<ParkLayoutSnapshot.Data>( JsonSerializer.Serialize( snapshot ) )!;
		var restored = ParkLayoutSnapshot.RestoreCellMap( revived );
		Assert.AreEqual( cells.Version, restored.Version );
		for ( var y = 0; y < 64; y++ )
			for ( var x = 0; x < 64; x++ )
				Assert.AreEqual( (cells.RawTypeAt( x, y ), cells.FlagsAt( x, y )), (restored.RawTypeAt( x, y ), restored.FlagsAt( x, y )), $"({x}, {y})" );
		CollectionAssert.AreEqual( cells.Land!.Ring.ToArray(), restored.Land!.Ring.ToArray() );
		Assert.AreEqual( Hash( cells ), Hash( restored ) );
	}

	[TestMethod]
	public void ASaveWithoutLandGetsTheLevelsLand()
	{
		var old = new ParkCellMap( 64, 64 );
		old.SetType( 15, 15, ParkCellType.Path );
		old.Touch();
		var snapshot = ParkLayoutSnapshot.CaptureSnapshot( old, new ParkObjects( null!, new SandboxParkGrid() ) );
		Assert.IsNull( snapshot.Cells.Land, "a map without land writes no land record" );
		var setup = Setup( RectangleClicks );
		var restored = ParkLayoutSnapshot.RestoreCellMap( snapshot, cells => cells.SetUpLand( setup, null ) );
		Assert.IsNotNull( restored.Land );
		Assert.AreEqual( old.Version, restored.Version, "the saved version is kept" );
		Assert.IsTrue( Owned( restored, 15, 15 ) && !Owned( restored, 40, 40 ) );
		Assert.AreEqual( ParkCellType.Path, restored.TypeAt( 15, 15 ) );
		Assert.IsFalse( ParkLayoutSnapshot.RestoreCellMap( snapshot ).Land != null, "no fallback, no land" );
	}

	[TestMethod]
	public void AnImportedSaveWithoutAWalkableFenceHasNoLand()
	{
		var land = ParkLand.FromClicks( Setup( RectangleClicks ) );
		var links = new Dictionary<(int, int), byte>();
		for ( var index = 0; index + 1 < land.Ring.Count; index++ )
		{
			var (a, b) = (land.Ring[index], land.Ring[index + 1]);
			links[a] = (byte)(links.GetValueOrDefault( a ) | Link( b.X - a.X, b.Y - a.Y ));
			links[b] = (byte)(links.GetValueOrDefault( b ) | Link( a.X - b.X, a.Y - b.Y ));
		}
		static byte Link( int dx, int dy ) => (dx, dy) switch { (0, < 0) => 1, (> 0, 0) => 4, (0, > 0) => 16, _ => 64 };
		// the saved flags: everything west of x 30 owned
		static bool Unowned( int x, int y ) => x >= 30;

		FenceLayer FenceOf( Dictionary<(int, int), byte> posts )
		{
			var layer = new FenceLayer( 64, 64 );
			foreach ( var ((x, y), bits) in posts )
			{
				layer.Type[y * 64 + x] = FenceLayer.PostType;
				layer.Links[y * 64 + x] = bits;
			}
			return layer;
		}
		var walked = new ParkCellMap( 64, 64 );
		walked.ImportLand( Setup( RectangleClicks ), Unowned, FenceOf( links ) );
		Assert.IsNotNull( walked.Land );
		CollectionAssert.AreEqual( land.Ring.ToArray(), walked.Land!.Ring.ToArray() );
		Assert.IsTrue( Owned( walked, 29, 40 ) && !Owned( walked, 30, 5 ), "the saved flags, not the fence's land" );

		// a fence broken half way: no land (Buy Land unavailable), the saved flags kept, not the level's starting ring
		Log ??= new Logger();
		links.Remove( land.Ring[land.Ring.Count / 2] );
		var broken = new ParkCellMap( 64, 64 );
		broken.ImportLand( Setup( RectangleClicks ), Unowned, FenceOf( links ) );
		Assert.IsNull( broken.Land );
		Assert.IsTrue( Owned( broken, 29, 40 ) && !Owned( broken, 30, 5 ) && !Owned( broken, 40, 15 ) );
		Assert.IsFalse( Tool( broken, null ).Begin() );
	}

	[TestMethod]
	public void ACorruptLandRecordIsRejected()
	{
		var cells = NewPark();
		var snapshot = ParkLayoutSnapshot.CaptureSnapshot( cells, new ParkObjects( null!, new SandboxParkGrid() ) );
		ParkLayoutSnapshot.Data With( ParkLayoutSnapshot.LandData land ) => snapshot with { Cells = snapshot.Cells with { Land = land } };
		var land = snapshot.Cells.Land!;
		foreach ( var (data, message) in new (ParkLayoutSnapshot.Data, string)[]
		{
			(With( land with { Unowned = "not base64!" } ), "Base64"),
			(With( land with { Unowned = Convert.ToBase64String( new byte[3] ) } ), "bytes"),
			(With( land with { Ring = land.Ring.Take( 1 ).ToList() } ), "posts"),
			(With( land with { Ring = land.Ring.Append( new[] { 99, 99 } ).ToList() } ), "outside the map"),
			(With( land with { Ring = land.Ring.Select( post => new[] { post[0], post[1] } ).Reverse().ToList() } ), "post A"),
			(With( land with { Ring = land.Ring.Where( ( _, index ) => index != 3 ).ToList() } ), "neighbours"),
		} )
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( data ) ).Message, message );

		// beside a land record a cell record may not carry the unowned flag 0x40; without one it may
		cells.SetType( 15, 15, ParkCellType.Path );
		var withPath = ParkLayoutSnapshot.CaptureSnapshot( cells, new ParkObjects( null!, new SandboxParkGrid() ) );
		var record = withPath.Cells.Cells.Single( cell => (cell.X, cell.Y) == (15, 15) );
		var flagged = withPath with
		{
			Cells = withPath.Cells with { Cells = withPath.Cells.Cells.Select( cell => cell == record ? cell with { Flags = (ushort)(cell.Flags | 0x40) } : cell ).ToList() }
		};
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( flagged ) ).Message, "unowned flag" );
		var landless = flagged with { Cells = flagged.Cells with { Land = null } };
		Assert.IsTrue( ParkLayoutSnapshot.RestoreCellMap( landless ).FlagsAt( 15, 15 ).HasFlag( ParkCellFlags.Unowned ) );
	}

	/// <summary>With bit 0x400 of the level word the set-up clicks six posts instead of reading the hoardings table (tp.exe 0x532489): a small enclosure in front of the gate.</summary>
	[TestMethod]
	public void TheSixClickFenceEnclosesTheSmallYardInFrontOfTheGate()
	{
		var setup = ParkLandSetup.FromLevel( (47, 17), (48, 17), null, sixClickFence: true );
		Assert.IsNotNull( setup );
		Assert.AreEqual( (51, 17), setup!.PostA );
		Assert.AreEqual( (43, 17), setup.PostB );
		CollectionAssert.AreEqual( ParkLandSetup.SixClicks.ToArray(), setup.Clicks.ToArray() );
		Assert.IsNull( ParkLandSetup.FromLevel( (47, 17), (48, 17), null ), "without the bit the table is read" );
		var cells = new ParkCellMap( 128, 128 );
		var land = ParkLand.NewPark( cells, setup );
		Assert.IsNotNull( land );
		cells.SetLand( land );
		// the clicks run from post A east 2, south 4, west 12 (to x - 10), north 4 and east 2 again: a yard 12 wide and 4 high south of the gate, the walk's ring has the posts of that outline
		Assert.IsTrue( land!.IsPost( 51, 17 ) && land.IsPost( 53, 17 ) && land.IsPost( 53, 21 ) && land.IsPost( 41, 21 ) && land.IsPost( 41, 17 ) && land.IsPost( 43, 17 ) );
		var owned = Enumerable.Range( 0, 128 * 128 ).Count( index => !cells.FlagsAt( index % 128, index / 128 ).HasFlag( ParkCellFlags.Unowned ) );
		Assert.IsTrue( owned > 30 && owned < 200, $"{owned} owned cells" );
	}
}
