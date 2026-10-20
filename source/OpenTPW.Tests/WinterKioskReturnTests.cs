using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:winter-kiosk-return] The winter kiosk does not count as recently used while a guest's slots are not full (docs/WINTER.md, "Returning for the second item"). The game's score scores a thing of the type of the last thing used
/// as 0 and divides it by 5, 4, 3 or 2 for a place in the used history, so a guest who had just bought a hat never scored the kiosk again for its mittens. The rule changes the kiosk's score for a guest who lacks an item, nothing else.
/// </summary>
// [EXT:winter-kiosk-return] user-approved 2026-10-19 ("Laten terugkomen")
[TestClass]
public class WinterKioskReturnTests
{
	private static readonly WinterShopTerms KioskTerms = new( 0, 0, 200, 150, 60, 20, 60 );

	private static (GuestSimulation Simulation, RideVisitorBridge Kiosk, Guest Guest) Park()
	{
		var rig = new GuestSimulation( GuestTests.CreateCross(), Configure(), 5 ) { ArrivalsEnabled = false };
		rig.UmbrellaWeatherState = () => UmbrellaRules.WeatherSnow;
		rig.WinterMonth = () => 12;
		var kiosk = new RideVisitorBridge( 1, "Winter Kiosk", RideVisitorKind.Shop, 1, 60, 30 ) { AppearanceEffect = WinterRules.KioskAppearanceEffect, WinterTerms = KioskTerms, EntranceCell = (10, 9), ExitCell = (10, 9) };
		rig.Register( kiosk );
		return (rig, kiosk, rig.SpawnInPark( kiosk.JoinCell!.Value.X, kiosk.JoinCell!.Value.Y ));
	}

	private static GuestSettings Configure()
	{
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		settings.Types = new() { new GuestType( 60, 300, 1000 ) };
		return settings;
	}

	[TestMethod]
	public void AGuestWhoLacksAnItemScoresTheKioskAsIfItWereNeverUsed()
	{
		var (simulation, kiosk, guest) = Park();
		var fresh = simulation.ScoreAttraction( guest, kiosk );
		Assert.AreEqual( 81u, fresh, "snow in December: 27 x 200 / 100 x 150 / 100" );
		foreach ( var slot in new[] { 0, 1, 2, 3 } )
		{
			System.Array.Clear( guest.UsedHistory );
			guest.UsedHistory[slot] = kiosk.AttractionId;
			Assert.AreEqual( fresh, simulation.ScoreAttraction( guest, kiosk ), $"the kiosk in used history slot {slot} does not count as used (the game divides by 5, 4, 3 and 2, and scores a last-used type as 0)" );
		}
		simulation.SetWarmItemsForTest( guest, WinterRules.Hat );
		guest.UsedHistory[0] = kiosk.AttractionId;
		Assert.AreEqual( fresh, simulation.ScoreAttraction( guest, kiosk ), "with a hat, the guest comes back for the mittens" );
		simulation.SetWarmItemsForTest( guest, WinterRules.Earmuffs );
		Assert.AreEqual( fresh, simulation.ScoreAttraction( guest, kiosk ), "earmuffs, the same" );
	}

	[TestMethod]
	public void AGuestWithFullSlotsStillScoresTheKioskZeroAndTheTemporaryHistoryStillCounts()
	{
		var (simulation, kiosk, guest) = Park();
		guest.UsedHistory[0] = kiosk.AttractionId;
		simulation.SetWarmItemsForTest( guest, WinterRules.Hat | WinterRules.Mittens );
		Assert.AreEqual( 0u, simulation.ScoreAttraction( guest, kiosk ), "full slots: nothing left to buy" );
		simulation.SetWarmItemsForTest( guest, WinterRules.Hat );
		System.Array.Clear( guest.UsedHistory );
		guest.TemporaryHistory[0] = kiosk.AttractionId;
		Assert.AreEqual( 15u, simulation.ScoreAttraction( guest, kiosk ), "a refusal at the kiosk's queue is remembered as before: 27 / 5 = 5, x 200 / 100 x 150 / 100" );
	}

	[TestMethod]
	public void AGuestWhoCannotPayTheKioskKeepsTheGamesHistoryRules()
	{
		// the shipped kiosk's price terms (docs/WINTER.md, "Price"): price 25, cost of goods 10, happiness effect 15, RipOffOK 100
		var (simulation, _, guest) = Park();
		var kiosk = new RideVisitorBridge( 3, "Winter Kiosk", RideVisitorKind.Shop, 1, 60, 30, price: 25 )
		{
			AppearanceEffect = WinterRules.KioskAppearanceEffect, WinterTerms = KioskTerms, EntranceCell = (10, 9), ExitCell = (10, 9), InitCostOfGoods = 10, HappinessEffect = 15, RipOffOk = 100
		};
		simulation.Register( kiosk );
		simulation.SetWarmItemsForTest( guest, WinterRules.Hat );
		var fresh = simulation.ScoreAttraction( guest, kiosk );
		Assert.IsTrue( fresh > 0 );
		guest.UsedHistory[0] = kiosk.AttractionId;
		guest.Money = 1000;
		Assert.IsFalse( QueueJoinRules.TooExpensive( GuestSimulation.PriceTermsOf( kiosk ), guest.Happiness, guest.Thirst, guest.Hunger, guest.Nausea, guest.Money ) );
		Assert.AreEqual( fresh, simulation.ScoreAttraction( guest, kiosk ), "a guest who can pay comes back for the mittens" );
		guest.Money = 24;
		Assert.AreEqual( 0u, simulation.ScoreAttraction( guest, kiosk ), "a guest whose purse is below the price keeps the game's rule: the kiosk it used last scores 0, so it is not drawn back to be refused" );
		guest.UsedHistory[0] = 0;
		guest.UsedHistory[2] = kiosk.AttractionId;
		Assert.AreEqual( fresh / 3, simulation.ScoreAttraction( guest, kiosk ), "and the used-history division (slot 2 divides by 3)" );
		guest.Money = 25;
		Assert.AreEqual( fresh, simulation.ScoreAttraction( guest, kiosk ), "exactly the price is enough" );
		// the willingness counts as the purse does: a guest at happiness 0 is willing to pay 24, below the price
		guest.Money = 1000;
		guest.Happiness = 0;
		var unhappy = simulation.ScoreAttraction( guest, kiosk );
		guest.UsedHistory[2] = 0;
		Assert.AreEqual( simulation.ScoreAttraction( guest, kiosk ) / 3, unhappy, "an unwilling guest keeps the division too" );
	}

	[TestMethod]
	public void EveryOtherAttractionKeepsItsHistoryRules()
	{
		var (simulation, kiosk, guest) = Park();
		var shop = new RideVisitorBridge( 2, "Shop", RideVisitorKind.Shop, 1, 60, 30 ) { EntranceCell = (10, 9), ExitCell = (10, 9) };
		simulation.Register( shop );
		var fresh = simulation.ScoreAttraction( guest, shop );
		Assert.IsTrue( fresh > 0 );
		guest.UsedHistory[0] = shop.AttractionId;
		Assert.AreEqual( 0u, simulation.ScoreAttraction( guest, shop ), "the shop used last scores 0 (same type as the last used)" );
		guest.UsedHistory[0] = 0;
		guest.UsedHistory[1] = shop.AttractionId;
		Assert.AreEqual( fresh / 4, simulation.ScoreAttraction( guest, shop ), "the shop in slot 1 is divided by 4" );
		// the kiosk in the history does not touch another shop's score
		guest.UsedHistory[1] = 0;
		guest.UsedHistory[0] = kiosk.AttractionId;
		Assert.AreEqual( fresh, simulation.ScoreAttraction( guest, shop ) );
	}
}
