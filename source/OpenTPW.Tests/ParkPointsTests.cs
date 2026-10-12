using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace OpenTPW.Tests;

/// <summary>Park points for the arrival rule (0x100C24C0, docs/reverse/ORACLE.md F10).</summary>
[TestClass]
public class ParkPointsTests
{
	private const int Plain = 1;
	private const int Tracked = 2;
	// 3,750 s per turn: turn 73 is three park days after turn 1 (72 × 3,750 s = 3.125 days).
	private const long ThreeDaysLater = 73;

	private static ParkEconomy Park() => new( EconomyTestData.Settings(), new EconomyObjectCatalog( new[]
	{
		Ride( Plain, trackType: 0 ),
		Ride( Tracked, trackType: 3 )
	} ), ParkGameMode.FullSimulation, 7 );

	// AttractionValue 25, a two-day decay and bonuses 30/20/10.
	private static EconomyObjectInfo Ride( int id, int trackType ) => new( id, $"Ride {id}", ParkObjectKind.Ride, ResearchCategory.Ride, 0,
		new[] { new UpgradeLevelInfo( 0, 0, 0, 0, 0, new[] { 0 } ) }, null, null, null, 0, 0, 0, 0, 0, 25, "synthetic.wad",
		NewAttractionDecayTime: 2, NewBonus: new[] { 30, 20, 10 }, TrackType: trackType );

	[TestMethod]
	public void CopiesOfAnUntrackedTypeShareTheAgeOfItsFirstPurchase()
	{
		var park = Park();
		EconomyTestData.AdvanceToTurn( park, 1 );
		park.RegisterExisting( Plain );
		EconomyTestData.AdvanceToTurn( park, ThreeDaysLater );
		park.RegisterExisting( Plain );
		// Both are three days old (bracket 1): (20 + 25) / 2 each. Aging the second copy as new would give 22 + 27.
		Assert.AreEqual( 44, park.ParkPoints() );
	}

	[TestMethod]
	public void APurchaseAtTurnZeroLeavesTheFirstPurchaseUnset()
	{
		var park = Park();
		park.RegisterExisting( Plain );
		EconomyTestData.AdvanceToTurn( park, ThreeDaysLater );
		park.RegisterExisting( Plain );
		// 0x100DA874 writes +28 only while it is 0, so turn 73 becomes the first purchase: both copies are new.
		Assert.AreEqual( ThreeDaysLater, park.Counters[ParkCounters.FirstPurchaseTurn( Plain )] );
		Assert.AreEqual( 54, park.ParkPoints() );
	}

	[TestMethod]
	public void EveryCopyOfATrackedTypeCountsInFullAtItsOwnAge()
	{
		var park = Park();
		EconomyTestData.AdvanceToTurn( park, 1 );
		park.RegisterExisting( Tracked );
		EconomyTestData.AdvanceToTurn( park, ThreeDaysLater );
		park.RegisterExisting( Tracked );
		// Bumper.WhichTrackType 3: divisor 1, own ages 3 and 0 days.
		Assert.AreEqual( (20 + 25) + (30 + 25), park.ParkPoints() );
	}

	[TestMethod]
	public void SellingDoesNotClearTheFirstPurchase()
	{
		var park = Park();
		EconomyTestData.AdvanceToTurn( park, 1 );
		var sold = park.RegisterExisting( Plain );
		park.Sell( sold.Id );
		EconomyTestData.AdvanceToTurn( park, ThreeDaysLater );
		park.RegisterExisting( Plain );
		Assert.AreEqual( 1, park.Counters[ParkCounters.FirstPurchaseTurn( Plain )] );
		Assert.AreEqual( 20 + 25, park.ParkPoints() );
	}

	[TestMethod]
	public void ASaveKeepsTheFirstPurchase()
	{
		var park = Park();
		EconomyTestData.AdvanceToTurn( park, 1 );
		park.RegisterExisting( Plain );
		EconomyTestData.AdvanceToTurn( park, ThreeDaysLater );
		park.RegisterExisting( Plain );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), park.Settings, park.Catalog );
		Assert.AreEqual( 44, restored.ParkPoints() );
	}

	[TestMethod]
	public void AnOldSaveWithoutTheCounterAgesFromTurnZero()
	{
		var park = Park();
		EconomyTestData.AdvanceToTurn( park, ThreeDaysLater );
		park.RegisterExisting( Plain );
		Assert.AreEqual( 30 + 25, park.ParkPoints() );
		var data = ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) );
		data.Counters.Remove( ParkCounters.FirstPurchaseTurn( Plain ) );
		// An unset park record +28 counts from turn 0, as in the original: 73 turns are three days (bracket 1).
		Assert.AreEqual( 20 + 25, ParkSaveFile.Restore( data, park.Settings, park.Catalog ).ParkPoints() );
	}
}
