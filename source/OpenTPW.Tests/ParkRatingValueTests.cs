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
		ride.IsBrokenDown = true;
		ride.LifeGauge = 0.5;
		Assert.AreEqual( 0, park.ParkValue );
		ride.LifeGauge = 60;
		Assert.AreEqual( 1400, park.ParkValue, "broken down but not condemned: it still counts" );
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
