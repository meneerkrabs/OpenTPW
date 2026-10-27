using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace OpenTPW.Tests;

/// <summary>Park points for the arrival rule (0x100C24C0, docs/reverse/ORACLE.md F10).</summary>
[TestClass]
public class ParkPointsTests
{
	private const int Plain = 1;
	private const int Tracked = 2;
	private const int Unchoosable = 3;
	// 3,750 s per turn: turn 73 is three park days after turn 1 (72 × 3,750 s = 3.125 days).
	private const long ThreeDaysLater = 73;

	private static ParkEconomy Park() => new( EconomyTestData.Settings(), new EconomyObjectCatalog( new[]
	{
		Ride( Plain, trackType: 0 ),
		Ride( Tracked, trackType: 3 ),
		Ride( Unchoosable, trackType: 0, choosable: false )
	} ), ParkGameMode.FullSimulation, 7 );

	// AttractionValue 25, a two-day decay and bonuses 30/20/10.
	private static EconomyObjectInfo Ride( int id, int trackType, bool choosable = true ) => new( id, $"Ride {id}", ParkObjectKind.Ride, ResearchCategory.Ride, 0,
		new[] { new UpgradeLevelInfo( 0, 0, 0, 0, 0, new[] { 0 } ) }, null, null, null, 0, 0, 0, 0, 0, 25, "synthetic.wad",
		NewAttractionDecayTime: 2, NewBonus: new[] { 30, 20, 10 }, TrackType: trackType, IsChoosable: choosable );

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
	public void AnAttractionWithoutRoomInItsQueueDoesNotCount()
	{
		// tp.exe 0x004DD920: no queue cells from the entrance (0x004DE130 gives no back cell) or a queue list of 4 × cells or more fails the counts-test.
		var park = Park();
		var full = park.RegisterExisting( Tracked );
		var waiting = park.RegisterExisting( Tracked );
		Assert.AreEqual( 2 * (30 + 25), park.ParkPoints() );
		full.QueueHasRoom = false;
		Assert.AreEqual( 30 + 25, park.ParkPoints() );
		Assert.IsTrue( park.Catalog.TryGet( Tracked, out var info ) );
		Assert.IsFalse( ParkEconomy.CountsForParkPoints( full, info ) );
		Assert.IsTrue( ParkEconomy.CountsForParkPoints( waiting, info ) );
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

	// The counts-test 0x100DC834 (docs/reverse/ORACLE.md F10): only the conditions with state in OpenTPW are applied.

	[TestMethod]
	public void AClosedRideDoesNotCount()
	{
		var park = Park();
		EconomyTestData.AdvanceToTurn( park, 1 );
		var ride = park.RegisterExisting( Tracked );
		Assert.AreEqual( 30 + 25, park.ParkPoints() );
		// Thing +0x64, the open word, is 0 while the ride is closed (0x100DFF18 reads it as "closed").
		park.SetObjectOpen( ride.Id, false );
		Assert.AreEqual( 0, park.ParkPoints() );
		park.SetObjectOpen( ride.Id, true );
		Assert.AreEqual( 30 + 25, park.ParkPoints() );
	}

	[TestMethod]
	public void ABrokenDownRideDoesNotCount()
	{
		var park = Park();
		EconomyTestData.AdvanceToTurn( park, 1 );
		var ride = park.RegisterExisting( Tracked );
		// Ride state BROKEN_DOWN (1) fails 0x100DFE0C; CONDEMNED (4, a breakdown at life gauge 0) fails 0x100DFE20.
		ride.IsBrokenDown = true;
		Assert.AreEqual( 0, park.ParkPoints() );
		ride.IsBrokenDown = false;
		Assert.AreEqual( 30 + 25, park.ParkPoints() );
	}

	[TestMethod]
	public void ARideWhoseScriptReportsBrokenDoesNotCount()
	{
		var park = Park();
		EconomyTestData.AdvanceToTurn( park, 1 );
		var ride = park.RegisterExisting( Tracked );
		// VAR_BROKEN (script variable 7) makes the original's ride update set state 1 or 4 (0x100E1864); the counts-test reads that state word, not the variable
		ride.ScriptBroken = true;
		Assert.IsFalse( ride.IsBrokenDown );
		Assert.AreEqual( 30 + 25, park.ParkPoints(), "the variable alone is not the state word" );
		EconomyTestData.RideUpdateEntersBrokenState( park, ride );
		Assert.AreEqual( 0, park.ParkPoints() );
		ride.IsBrokenDown = false;
		ride.ScriptBroken = false;
		Assert.AreEqual( 30 + 25, park.ParkPoints() );
	}

	[TestMethod]
	public void ATypeThatGuestsCannotChooseDoesNotCount()
	{
		var park = Park();
		EconomyTestData.AdvanceToTurn( park, 1 );
		park.RegisterExisting( Unchoosable );
		// Info.IsChoosable 0 leaves thing +0x2E bit 0x4 clear, which 0x100DC834 tests first.
		Assert.AreEqual( 0, park.ParkPoints() );
	}

	[TestMethod]
	public void AClosedCopyStillCountsInTheDivisorOfItsType()
	{
		var park = Park();
		EconomyTestData.AdvanceToTurn( park, 1 );
		park.RegisterExisting( Plain );
		var closed = park.RegisterExisting( Plain );
		park.SetObjectOpen( closed.Id, false );
		// The divisor is the park record's copy count (+24, 0x100C5828), which lists the closed copy too: (30 + 25) / 2.
		Assert.AreEqual( (30 + 25) / 2, park.ParkPoints() );
	}

	// tp.exe 0x004DD920 by Bumper.WhichTrackType and 0x004C8240 by Info.WhichUIType (ARRIVAL-003).

	private static ParkEconomy TrackPark() => new( EconomyTestData.Settings(), new EconomyObjectCatalog( new[]
	{
		Ride( Plain, trackType: 0 ),
		Ride( Tracked, trackType: 3 ),
		Ride( 4, trackType: 1 ),
		Ride( 5, trackType: 2 ),
		Ride( 6, trackType: 0 ) with { Kind = ParkObjectKind.Upgrade, WhichUIType = 0 },
		Ride( 7, trackType: 0 ) with { WhichUIType = 4 }
	} ), ParkGameMode.FullSimulation, 7 );

	[TestMethod]
	public void ACoasterCountsOnlyWhileItsControllerAcceptsGuests()
	{
		var park = TrackPark();
		var coaster = park.RegisterExisting( Tracked );
		Assert.AreEqual( 30 + 25, park.ParkPoints() );
		// 0x00441970: no controller, a self-intersection, an incomplete or open track fails the coaster test.
		coaster.CoasterAcceptsGuests = false;
		Assert.AreEqual( 0, park.ParkPoints() );
		// The track word is not read for a coaster (WhichTrackType 3).
		coaster.CoasterAcceptsGuests = true;
		coaster.TrackValid = false;
		Assert.AreEqual( 30 + 25, park.ParkPoints() );
	}

	[TestMethod]
	public void KartsAndWaterRidesNeedTheirTrackWordAndOtherTypesIgnoreIt()
	{
		var park = TrackPark();
		var kart = park.RegisterExisting( 4 );
		var water = park.RegisterExisting( 5 );
		var plain = park.RegisterExisting( Plain );
		Assert.AreEqual( 3 * (30 + 25), park.ParkPoints() );
		kart.TrackValid = false;
		water.TrackValid = false;
		plain.TrackValid = false;
		plain.CoasterAcceptsGuests = false;
		Assert.AreEqual( 30 + 25, park.ParkPoints(), "only the untracked ride counts" );
	}

	[TestMethod]
	public void TheKindFilterReadsInfoWhichUIType()
	{
		var park = TrackPark();
		park.RegisterExisting( 6 );
		// QueueHasRoom defaults to true until the level's first queue sync; in a real level an add-on without an entrance has no queue cells and then stops counting, as in the original.
		Assert.AreEqual( 30 + 25, park.ParkPoints(), "an add-on declaring WhichUIType 0 counts as a ride" );
		park.RegisterExisting( 7 );
		Assert.AreEqual( 30 + 25, park.ParkPoints(), "a ride declaring WhichUIType 4 does not count" );
	}
}
