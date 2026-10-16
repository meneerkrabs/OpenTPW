using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The analyser's cached guest census (tp.exe <c>0x004C7FA0</c>, ECON-014): the crowd sound refreshes it every fourth park turn and the staff manager's month gate reads it with age 5.
/// </summary>
[TestClass]
public class CensusCacheTests
{
	private sealed class Census : IParkGuestStatistics
	{
		public int Guests;
		public int PeopleInPark => Guests;
		public int GuestCensus => Guests;
		public float AverageHappiness => 0;
		public int CountHappierThan( int happiness ) => 0;
		public int KidsWithBalloonsPercent => 0;
		public int KidsWithCostumesPercent => 0;
	}

	private static (ParkEconomy Park, Census Census) NewPark()
	{
		var park = EconomyTestData.Park( initialCash: 5_000_000 );
		var census = new Census();
		park.GuestStatistics = census;
		return (park, census);
	}

	[TestMethod]
	public void TheCrowdReadCountsEveryFourthTurnFromTheTurnTheLoopStartedAt()
	{
		var (park, census) = NewPark();
		census.Guests = 3;
		EconomyTestData.AdvanceToTurn( park, 3 );
		Assert.IsNull( park.CachedCensus, "no read before the 32nd substep" );
		EconomyTestData.AdvanceToTurn( park, 4 );
		Assert.AreEqual( (3, 4L), park.CachedCensus );
		census.Guests = 9;
		EconomyTestData.AdvanceToTurn( park, 7 );
		Assert.AreEqual( (3, 4L), park.CachedCensus, "turns 5 to 7 serve the cached count" );
		EconomyTestData.AdvanceToTurn( park, 8 );
		Assert.AreEqual( (9, 8L), park.CachedCensus );
	}

	[TestMethod]
	public void TheStepCounterStartsAtTheTurnAParkIsLoadedAt()
	{
		var (park, _) = NewPark();
		EconomyTestData.AdvanceToTurn( park, 6 );
		Assert.AreEqual( 0L, park.CrowdPhaseTurn, "a new park's counter starts at turn 0" );
		// a save without the field (written before the cache) starts the count at the turn it loads at
		var (loaded, loadedCensus) = NewPark();
		loaded.Advance( ParkCalendar.TickOfTurn( 6 ) );
		loaded.RestoreCrowdPhase( null );
		loadedCensus.Guests = 5;
		EconomyTestData.AdvanceToTurn( loaded, 9 );
		Assert.AreEqual( 6L, loaded.CrowdPhaseTurn );
		Assert.AreEqual( 4L, loaded.CachedCensus!.Value.Stamp, "with the phase 6 the next read falls on turn 10, not 8" );
		EconomyTestData.AdvanceToTurn( loaded, 10 );
		Assert.AreEqual( (5, 10L), loaded.CachedCensus );
		// OpenTPW's own save keeps the phase and the cache
		var data = ParkSaveFile.Capture( loaded );
		Assert.AreEqual( 6L, data.AnalyserCaches!.CrowdPhaseTurn );
		Assert.AreEqual( 5, data.AnalyserCaches.GuestCensus!.Value );
	}

	[TestMethod]
	public void AReaderSeesTheCachedCountUntilItsAgeIsPassed()
	{
		var (park, census) = NewPark();
		census.Guests = 2;
		Assert.AreEqual( 2, park.CachedGuestCensus( 5 ), "the first read counts" );
		census.Guests = 7;
		EconomyTestData.AdvanceToTurn( park, 3 );
		Assert.AreEqual( 2, park.CachedGuestCensus( 5 ) );
		Assert.AreEqual( 2, park.CachedGuestCensus( 0 ), "(3 >> 2) - (0 >> 2) = 0 is not above age 0" );
		EconomyTestData.AdvanceToTurn( park, 21 );
		Assert.AreEqual( 7, park.CachedGuestCensus( 5 ), "the crowd reads kept it, age 5 would also have counted again at 24" );
	}

	[TestMethod]
	public void TheMonthGateSkipsAMonthWhoseCachedCensusStillHoldsGuests()
	{
		// control: no guests ever; the first consideration warns at turn T
		var (control, controlCensus) = NewPark();
		control.OpenPark();
		for ( var index = 0; index < 4; index++ )
			control.Staff.AddImported( StaffType.Mechanic, 0, 0, 100f, StaffState.Patrolling, 0, 0f );
		var warnings = new List<long>();
		control.EventRaised += item =>
		{
			if ( item.Kind == ParkEventKind.StaffStrikeMessage && (StrikeMessageKind)item.Amount == StrikeMessageKind.Warning )
				warnings.Add( control.Turn );
		};
		while ( warnings.Count == 0 )
			control.AdvanceMonths( 1 );
		var warningTurn = warnings[0];
		Assert.AreEqual( 0, controlCensus.Guests );

		// the last guest leaves right after the last crowd read before the month end: the live count is 0 at the gate, the cached one is not
		var (park, census) = NewPark();
		park.OpenPark();
		for ( var index = 0; index < 4; index++ )
			park.Staff.AddImported( StaffType.Mechanic, 0, 0, 100f, StaffState.Patrolling, 0, 0f );
		var seen = new List<long>();
		park.EventRaised += item =>
		{
			if ( item.Kind == ParkEventKind.StaffStrikeMessage && (StrikeMessageKind)item.Amount == StrikeMessageKind.Warning )
				seen.Add( park.Turn );
		};
		census.Guests = 1;
		var lastRead = (warningTurn - 1) / ParkEconomy.CrowdReadTurns * ParkEconomy.CrowdReadTurns;
		EconomyTestData.AdvanceToTurn( park, lastRead );
		census.Guests = 0;
		EconomyTestData.AdvanceToTurn( park, warningTurn );
		Assert.AreEqual( 0, seen.Count, "the gate read the cached count of the last crowd read, one guest, and skipped the month" );
		census.Guests = 0;
		while ( seen.Count == 0 )
			park.AdvanceMonths( 1 );
		Assert.IsTrue( seen[0] > warningTurn, "the next month end reads the refreshed count and warns" );
	}
}
