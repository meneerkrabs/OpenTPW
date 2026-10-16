using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The optional park-save fields of the economy second pass (Rings, NextOfferSeconds, UpgradeOrder, ActualThing, CarriedThings): a hand-edited or damaged save is
/// refused with an InvalidDataException, never an unrelated exception, and a signed happiness-change ring survives a save.
/// </summary>
[TestClass]
public class SaveSecondPassFieldsTests
{
	private static JsonObject SavedParkWithRings()
	{
		var economy = EconomyTestData.Park();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1203, out var shop ) );
		economy.SetPrices( shop!.Id, 10, 0, 0 );
		shop.IsOpen = true;
		Assert.IsTrue( economy.TryBuy( shop.Id, 100, out _ ) );
		economy.AdvanceDays( 1 );
		return JsonNode.Parse( ParkSaveFile.Serialize( economy ) )!.AsObject();
	}

	private static void AssertRefused( JsonObject root, string what )
	{
		var bytes = Encoding.UTF8.GetBytes( root.ToJsonString() );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( bytes ), what );
	}

	private static JsonObject ObjectWithRings( JsonObject root ) => root["Objects"]!.AsArray().Select( node => node!.AsObject() ).First( node => node["Rings"] != null );

	[TestMethod]
	public void ASavedParkWithRingsIsAccepted()
	{
		var root = SavedParkWithRings();
		Assert.AreEqual( 5, ObjectWithRings( root )["Rings"]!.AsArray().Count );
		ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( root.ToJsonString() ) );
	}

	[TestMethod]
	public void RingsWithTheWrongCountANullRingOrANegativeCountingValueAreRefused()
	{
		var root = SavedParkWithRings();
		var rings = ObjectWithRings( root )["Rings"]!.AsArray();
		var kept = rings.Select( ring => ring!.DeepClone() ).ToList();
		void Reset() { rings.Clear(); foreach ( var ring in kept ) rings.Add( ring.DeepClone() ); }

		rings.RemoveAt( 4 );
		AssertRefused( root, "four rings" );
		Reset();
		rings[2] = null;
		AssertRefused( root, "a null ring" );
		Reset();
		rings[1]!["Today"] = -1;
		AssertRefused( root, "a negative counting ring value" );
		Reset();
		rings[0]!["Head"] = 30;
		AssertRefused( root, "a head past the ring" );
		Reset();
		rings[0]!["Wrapped"] = true;
		rings[0]!["Head"] = -1;
		AssertRefused( root, "a wrapped ring without a head" );
	}

	[TestMethod]
	public void ASignedHappinessChangeRingSurvivesASave()
	{
		var economy = EconomyTestData.Park();
		var shop = economy.RegisterExisting( 1203 );
		shop.SatisfactionRing.Today = -5;
		shop.SatisfactionRing.Roll();
		shop.SatisfactionRing.Today = -2;
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( economy ) ) ), economy.Settings, economy.Catalog ).Objects.Single();
		Assert.AreEqual( (-5L, -2), (restored.SatisfactionRing.SignedTotal, restored.SatisfactionRing.Today) );
	}

	[TestMethod]
	public void ChallengeTimesOutsideTheRangeOrApartFromTheOfferDayAreRefused()
	{
		var root = SavedParkWithRings();
		var objectives = root["Objectives"]!.AsObject();
		var day = objectives["NextOfferDay"]!.GetValue<long>();
		objectives["NextOfferSeconds"] = day * ParkCalendar.SecondsPerDay + 5;
		ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( root.ToJsonString() ) );
		objectives["NextOfferSeconds"] = -5L;
		AssertRefused( root, "a negative offer second" );
		objectives["NextOfferSeconds"] = ParkSaveFile.MaximumParkSeconds + 1;
		AssertRefused( root, "an offer second past the limit" );
		objectives["NextOfferSeconds"] = (day + 3) * ParkCalendar.SecondsPerDay;
		AssertRefused( root, "an offer second on another day than NextOfferDay" );
	}

	[TestMethod]
	public void AnUpgradeOrderEntryThatIsNotAPairIsRefused()
	{
		var root = SavedParkWithRings();
		root["Research"]!.AsObject()["UpgradeOrder"] = new JsonArray( new JsonArray( 1, 2, 3 ) );
		AssertRefused( root, "an order entry of three numbers" );
		root["Research"]!.AsObject()["UpgradeOrder"] = new JsonArray( new JsonArray( 1 ) );
		AssertRefused( root, "an order entry of one number" );
		root["Research"]!.AsObject()["UpgradeOrder"] = new JsonArray( new JsonArray( 999999, 0 ) );
		var data = ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( root.ToJsonString() ) );
		var economy = EconomyTestData.Park();
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( data, economy.Settings, economy.Catalog ), "an order entry for no project" );
	}

	[TestMethod]
	public void ANegativeCarriedObjectIsRefused()
	{
		var root = SavedParkWithRings();
		root["Objectives"]!.AsObject()["ActualThing"] = -1;
		AssertRefused( root, "a negative carried object" );
		root["Objectives"]!.AsObject()["ActualThing"] = 0;
		root["Objectives"]!.AsObject()["CarriedThings"] = new JsonObject { ["3"] = 0 };
		AssertRefused( root, "a carried slot of object 0" );
	}

	[TestMethod]
	public void ADailyRingFromTheOriginalsSlotsRejectsAWrappedRingWithoutAHead()
	{
		Assert.ThrowsException<InvalidDataException>( () => DailyRing.FromSlots( -1, true, 0, new int[DailyRing.Size] ) );
		Assert.AreEqual( 0, DailyRing.FromSlots( -1, false, 0, new int[DailyRing.Size] ).Count );
	}
}
