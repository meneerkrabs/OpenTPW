using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The objects' staff claim (tp.exe 0x004E01F0, 0x004E0220: it lapses 100 turns on once its claimant no longer targets the object) and the
/// mechanic HQ's cursor through the ride finder (0x004DAA90).
/// </summary>
[TestClass]
public class StaffClaimTests
{
	private static ParkEconomy Park( out List<ParkObjectState> rides, int count )
	{
		var park = EconomyTestData.Park();
		rides = Enumerable.Range( 0, count ).Select( _ => park.RegisterExisting( 1100 ) ).ToList();
		foreach ( var ride in rides )
			ride.IsBrokenDown = true;
		return park;
	}

	[TestMethod]
	public void TheCursorStartsEachSearchOneObjectOnAndMissesAfterTheLast()
	{
		var park = Park( out var rides, 3 );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		// every ride equally far: the first met wins, and the walk starts at the newest
		(int X, int Y)? Cell( int id ) => (5, 5);
		var found = Enumerable.Range( 0, 6 ).Select( _ => park.FindRideForMechanic( mechanic, (5, 6), Cell )?.Id ?? 0 ).ToArray();
		var ids = rides.Select( ride => ride.Id ).ToArray();
		CollectionAssert.AreEqual( new[] { ids[2], ids[1], ids[0], 0, ids[2], ids[1] }, found,
			"newest first, the start moving one object on per search; after a start at the last object the cursor is 0 and nothing is found" );
	}

	[TestMethod]
	public void TheNearestRideWinsFromAnyStart()
	{
		var park = Park( out var rides, 3 );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var cells = new Dictionary<int, (int X, int Y)> { [rides[0].Id] = (9, 9), [rides[1].Id] = (5, 7), [rides[2].Id] = (1, 1) };
		for ( var search = 0; search < 3; search++ )
			Assert.AreEqual( rides[1].Id, park.FindRideForMechanic( mechanic, (5, 6), id => cells[id] )?.Id );
	}

	[TestMethod]
	public void AClaimLapsesOnlyAfterAHundredTurnsWithoutItsClaimantsTarget()
	{
		var park = Park( out var rides, 1 );
		var ride = rides[0];
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var start = park.Turn;
		park.Claim( ride, mechanic );
		Assert.AreEqual( start, ride.ClaimTurn );
		EconomyTestData.AdvanceToTurn( park, start + ParkEconomy.ClaimLapseTurns );
		Assert.AreEqual( mechanic.Id, park.Claimant( ride ), "the turn must pass the claim turn + 100" );
		EconomyTestData.AdvanceToTurn( park, start + ParkEconomy.ClaimLapseTurns + 1 );
		mechanic.RideId = ride.Id;
		Assert.AreEqual( mechanic.Id, park.Claimant( ride ), "a claimant that still targets the object keeps it" );
		mechanic.RideId = 0;
		Assert.AreEqual( 0, park.Claimant( ride ) );
		Assert.AreEqual( (0, 0L), (ride.MechanicId, ride.ClaimTurn), "the lapse clears the claim" );
	}

	[TestMethod]
	public void AnotherMechanicTakesARideOnlyOnceTheClaimHasLapsed()
	{
		var park = Park( out var rides, 1 );
		var first = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var second = EconomyTestData.HireBest( park, StaffType.Mechanic );
		park.Claim( rides[0], first );
		(int X, int Y)? Cell( int id ) => (5, 5);
		// with one object every other search misses (its start was the last object), so each search here starts afresh
		ParkObjectState? Find( StaffMember member )
		{
			park.MechanicCursor = 0;
			return park.FindRideForMechanic( member, (5, 6), Cell );
		}
		Assert.IsNull( Find( second ), "claimed by the first" );
		Assert.AreEqual( rides[0].Id, Find( first )?.Id, "his own claim is no obstacle" );
		EconomyTestData.AdvanceToTurn( park, park.Turn + ParkEconomy.ClaimLapseTurns + 1 );
		Assert.AreEqual( rides[0].Id, Find( second )?.Id, "the first no longer targets it: lapsed" );
	}

	[TestMethod]
	public void TheCursorAndTheClaimTurnSurviveASave()
	{
		var park = Park( out var rides, 2 );
		var mechanic = EconomyTestData.HireBest( park, StaffType.Mechanic );
		EconomyTestData.AdvanceToTurn( park, 10 );
		park.Claim( rides[0], mechanic );
		park.FindRideForMechanic( mechanic, (0, 0), _ => (1, 1) );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), EconomyTestData.Settings(), EconomyTestData.Catalog() );
		Assert.AreEqual( park.MechanicCursor, restored.MechanicCursor );
		Assert.IsTrue( restored.TryGetObject( rides[0].Id, out var resumed ) );
		Assert.AreEqual( (mechanic.Id, 10L), (resumed.MechanicId, resumed.ClaimTurn) );
		var fresh = ParkSaveFile.Serialize( EconomyTestData.Park() );
		StringAssert.DoesNotMatch( fresh, new System.Text.RegularExpressions.Regex( "MechanicCursor|ClaimTurn" ), "an unused cursor and claims are not written" );
	}
}
