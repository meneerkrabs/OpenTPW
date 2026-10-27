using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The park rating (<c>0xc7b24</c>) and the park value (<c>0xc1d4c</c>), docs/reverse/ORACLE.md F29 (ECON-026, ECON-027).</summary>
[TestClass]
public class ParkRatingValueTests
{
	[TestMethod]
	public void ObjectsCountInTheRatingByTheTypesUiTypeNotByTheirDirectory()
	{
		// The add-ons (the upgrades directory) declare Info.WhichUIType 0 and count as rides: three of them are 3 × 3 / 2 = 4 points.
		var park = EconomyTestData.Park( initialCash: 1_000_000 );
		Assert.AreEqual( 0, park.ParkRating );
		for ( var count = 0; count < 3; count++ )
			park.RegisterExisting( 1500 );
		Assert.AreEqual( 4, park.ParkRating, "three add-ons count as rides" );
		var fixedItems = EconomyTestData.Park( initialCash: 1_000_000 );
		for ( var count = 0; count < 3; count++ )
			fixedItems.RegisterExisting( 1601 );
		Assert.AreEqual( 0, fixedItems.ParkRating, "fixed items declare the UI type 4 and count for nothing" );
	}

	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 4;
		public int Height => 4;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private sealed class Census( int inPark, int census ) : IParkGuestStatistics
	{
		public int PeopleInPark => inPark;
		public int GuestCensus => census;
		public float AverageHappiness => 0;
		public int CountHappierThan( int happiness ) => 0;
		public int KidsWithBalloonsPercent => 0;
		public int KidsWithCostumesPercent => 0;
	}

	[TestMethod]
	public void TheVisitorTermCountsTheGuestsOnCountedCells()
	{
		// [BIN:TPW-EXE:0x4cceb0 park rating] the guests on a cell of type 0, 1, 3, 9 or 10 (0x4fa990), at most 1000, × 20 / 1000; riders on a ride's body do not count
		var park = EconomyTestData.Park();
		park.GuestStatistics = new Census( inPark: 900, census: 100 );
		Assert.AreEqual( 2, park.ParkRating );
		park.GuestStatistics = new Census( inPark: 900, census: 1500 );
		Assert.AreEqual( 20, park.ParkRating, "capped at 1000" );
	}

	[TestMethod]
	public void AnExplicitUiTypeWinsOverTheDirectory()
	{
		var shopAsFeature = new EconomyObjectInfo( 7001, "Odd", ParkObjectKind.Shop, ResearchCategory.Shop, 0, new[] { new UpgradeLevelInfo( 0, 100, 0, 0, 0, new[] { 0 } ) },
			null, null, null, 0, 0, 0, 0, 0, 0, "shops/odd.wad", WhichUIType: 3 );
		Assert.AreEqual( 3, shopAsFeature.RatingType );
		Assert.AreEqual( 1, shopAsFeature with { WhichUIType = -1 } is { } plain ? plain.RatingType : -1, "without the key the directory decides" );
		var park = new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( new[] { shopAsFeature } ), ParkGameMode.FullSimulation, 1 );
		for ( var count = 0; count < 4; count++ )
			park.RegisterExisting( 7001 );
		Assert.AreEqual( 4, park.ParkRating, "four features, 1 each, not four shops at 2 each" );
	}

	[TestMethod]
	public void TheParkValueIsTheCostOfTheLevelsTimesTheStateOfRepair()
	{
		// Belly Bounce (1100): level 0 costs 500, level 1 400, level 2 500. The value counts the levels up to the current one, times the state of repair byte / 100.
		var park = EconomyTestData.Park( initialCash: 1_000_000 );
		var ride = park.RegisterExisting( 1100 );
		Assert.AreEqual( 500, park.ParkValue, "level 0, repair 100" );
		ride.Level = 1;
		Assert.AreEqual( 900, park.ParkValue );
		ride.Repair = 80;
		Assert.AreEqual( 720, park.ParkValue, "900 × 80 / 100" );
		ride.Repair = 79.9;
		Assert.AreEqual( 900 * 79 / 100, park.ParkValue, "the state of repair is a truncated byte" );
		ride.Level = 2;
		ride.Repair = 100;
		Assert.AreEqual( 1400, park.ParkValue );
		// A condemned ride (broken down with an empty life gauge) adds nothing.
		ride.LifeGauge = 0.5;
		ride.IsBrokenDown = true;
		Assert.AreEqual( RideObjectStates.Condemned, ParkEconomy.ObjectState( ride ) );
		Assert.AreEqual( 0, park.ParkValue );
		ride.IsBrokenDown = false;
		ride.LifeGauge = 60;
		ride.IsBrokenDown = true;
		Assert.AreEqual( 1400, park.ParkValue, "broken down but not condemned: it still counts" );
	}

	[TestMethod]
	public void TheOwnedCellsAddTheirPointsToTheParkValue()
	{
		// [BIN:TPW-EXE:0x4c7db0 net worth] 2 per empty cell, 3 per path, 5 per queue, 6 per entrance, 1 per exit and water, nothing for 4, 7 and 30; unowned cells (0x40) count nothing
		var park = EconomyTestData.Park();
		var cells = new ParkCellMap( 4, 2 );
		cells.SetType( 1, 0, ParkCellType.Path );
		cells.SetType( 2, 0, ParkCellType.Queue );
		cells.SetType( 3, 0, ParkCellTypes.Entrance );
		cells.SetType( 0, 1, ParkCellTypes.Exit );
		cells.SetType( 1, 1, ParkCellTypes.Water );
		cells.SetType( 2, 1, ParkCellTypes.Object );
		cells.SetType( 3, 1, ParkCellTypes.NoTerrain );
		park.Litter.Cells = cells;
		Assert.AreEqual( 2 + 3 + 5 + 6 + 1 + 1 + 0 + 0, park.ParkValue );
		cells.SetFlags( 0, 0, ParkCellFlags.Unowned );
		cells.SetFlags( 1, 0, ParkCellFlags.Unowned );
		Assert.AreEqual( 5 + 6 + 1 + 1, park.ParkValue, "unowned cells count nothing" );
		park.RegisterExisting( 1100 );
		Assert.AreEqual( 13 + 500, park.ParkValue, "then the objects" );
		Assert.AreEqual( 2, ParkValueRules.CellPoints( 0, false ) );
		Assert.AreEqual( 52, ParkValueRules.CellPoints( 0, true ), "a track or hoarding tile adds 50" );
	}

	[TestMethod]
	public void TheBigParkCountsObjectEntranceExitAndStructureCellsOwnedOrNot()
	{
		// 0x4cd7f0: the cells of type 4, 9, 10 or 21 or under a counted structure; unlike the net worth it ignores the unowned flag
		var park = EconomyTestData.Park();
		Assert.IsNull( park.CoveredCells(), "no cell map" );
		var cells = new ParkCellMap( 4, 4 );
		park.Litter.Cells = cells;
		Assert.AreEqual( 0, park.CoveredCells() );
		cells.SetType( 0, 0, ParkCellTypes.Object );
		cells.SetType( 1, 0, ParkCellTypes.Entrance );
		cells.SetType( 2, 0, ParkCellTypes.Exit );
		cells.SetType( 3, 0, 21 );
		cells.SetType( 0, 1, ParkCellType.Path );
		cells.SetType( 1, 1, ParkCellTypes.Water );
		cells.SetFlags( 0, 0, ParkCellFlags.Unowned );
		Assert.AreEqual( 4, park.CoveredCells() );
		var builder = new TrackBuilder( cells, TrackFamily.Kart, () => null, new FlatGrid() );
		Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( 2, 2 ) );
		Assert.AreEqual( 4 + 4, park.CoveredCells(), "a track tile and its three extensions" );
	}

	[TestMethod]
	public void ATrackTileCountsFiftyThroughItsOrigin()
	{
		var park = EconomyTestData.Park();
		var cells = new ParkCellMap( 4, 4 );
		park.Litter.Cells = cells;
		var empty = park.ParkValue;
		var builder = new TrackBuilder( cells, TrackFamily.Kart, () => null, new FlatGrid() );
		Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( 0, 0 ) );
		Assert.AreEqual( empty + 4 * 50, park.ParkValue, "the origin (11) and its three extensions (12, naming the origin) add 50 each; their map type counts as an empty cell" );
	}

	[TestMethod]
	public void TheNetWorthIsCountedAgainOnlyWhenTheReadersAgeIsExceeded()
	{
		// [BIN:TPW-EXE:0x4c7db0 net worth] the cached value serves while (turn >> 2) - (stamp >> 2) <= the reader's age
		var park = EconomyTestData.Park();
		Assert.AreEqual( 0, park.NetWorth( 20 ) );
		park.RegisterExisting( 1100 );
		Assert.AreEqual( 0, park.NetWorth( 20 ), "cached in the same turn" );
		Assert.AreEqual( 500, park.ParkValue, "the live value" );
		EconomyTestData.AdvanceToTurn( park, 4 * 20 + 3 );
		Assert.AreEqual( 0, park.NetWorth( 20 ), "20 quarter-turns old: still served" );
		EconomyTestData.AdvanceToTurn( park, 4 * 21 );
		Assert.AreEqual( 500, park.NetWorth( 20 ), "21 quarter-turns: counted again" );
		park.RegisterExisting( 1100 );
		Assert.AreEqual( 500, park.NetWorth( 20 ) );
		EconomyTestData.AdvanceToTurn( park, park.Turn + 4 );
		Assert.AreEqual( 1000, park.NetWorth( 0 ), "age 0 recounts once the quarter-turn changed" );
	}

	[TestMethod]
	public void TheScrapValueIsNoLongerPartOfTheParkValue()
	{
		var park = EconomyTestData.Park( initialCash: 1_000_000 );
		park.RegisterExisting( 1203 );
		park.AdvanceDays( 365 );
		Assert.AreEqual( 650, park.ParkValue, "the cost of the shop, whatever its age: no scrap percentage" );
		Assert.AreNotEqual( park.ScrapValue( park.Objects.Single() ), park.ParkValue );
	}

	[TestMethod]
	public void EveryShippedObjectsUiTypeMatchesItsDirectoryExceptTheAddOnsAndTheMysteryTool()
	{
		// ECON-027: the old rule counted by directory. The shipped data agrees with it except for the add-ons (the upgrades directory: Info.WhichUIType 0, counted as
		// rides) and the Mystery tool (Info.Id 100, a ride-directory object that declares 4).
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the shipped object files." );
		var previous = FileSystem;
		try
		{
			FileSystem = new BaseFileSystem( OriginalParkImportTests.OriginalDataPath() );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			var seen = 0;
			foreach ( var theme in new[] { "jungle", "hallow", "fantasy", "space" } )
			{
				foreach ( var info in EconomyObjectCatalog.Load( theme, easy: false ).Objects )
				{
					var expected = info.Kind switch
					{
						ParkObjectKind.Ride => info.InfoId == 100 ? 4 : 0,
						ParkObjectKind.Shop => 1,
						ParkObjectKind.Sideshow => 2,
						ParkObjectKind.Feature => 3,
						ParkObjectKind.Upgrade => 0,
						_ => 4
					};
					Assert.AreEqual( expected, info.RatingType, $"{theme} {info.InfoId} {info.Name} ({info.Kind}, Info.WhichUIType {info.WhichUIType})" );
					seen++;
				}
			}
			Assert.IsTrue( seen > 200, $"{seen} objects" );
		}
		finally
		{
			FileSystem = previous;
		}
	}
}
