using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's challenge system (docs/reverse/TPI-CHALLENGE-SYSTEM.md): the slot loader, the state machine and its clock arithmetic, the offers, the accept, the win and
/// failure tests of the Science level's types, the outcomes in the economy and the save. Synthetic data throughout, plus the retail Science list when
/// <c>OPENTPW_TPI_PATH</c> names the data.
/// </summary>
[TestClass]
public class ThemeParkIncChallengeTests
{
	private sealed class Host : IThemeParkIncChallengeHost
	{
		public bool ParkOpen { get; set; } = true;
		public int MissionCounter { get; set; }
		public int FiringLine { get; set; } = 5;
		public void LowerFiringLine() => FiringLine--;
		public bool FatShop { get; set; }
		public int EntertainerExperience { get; set; }
		public int Staff { get; set; }
		public int LeastHappiness { get; set; } = 100;
		public bool Strike { get; set; }
		public List<int> Offers { get; } = new();
		public List<int> Wins { get; } = new();
		public List<(int Index, bool First)> Losses { get; } = new();
		public bool HasFatShop() => FatShop;
		public int MaxExperience( int kind ) => kind == 6 ? EntertainerExperience : 0;
		public int StaffCount() => Staff;
		public int MinStaffHappiness() => LeastHappiness;
		public bool AnyRoleOnStrike() => Strike;
		public void Offered( int index ) => Offers.Add( index );
		public void Won( int index, ThemeParkIncChallengeSlot slot ) => Wins.Add( index );
		public void Lost( int index, ThemeParkIncChallengeSlot slot, bool firstLoss ) => Losses.Add( (index, firstLoss) );
	}

	private static ThemeParkIncChallengeRow Row( int type, int targetTime = 30, int targetVal = 10, int targetVal2 = 0, int targetObj = 0, int cash = 0, int tickets = 0,
		int checkAtEnd = 0, int min = 0, int max = 0, int delay = 0, int trigger = 0, int grace = 0, int script = 0, int mission = 0, int followup = 0 ) =>
		new( type, followup, targetTime, targetVal, targetVal2, targetObj, 0, 0, cash, tickets, 0, 0, 0, checkAtEnd, min, max, delay, trigger, grace, 0, 0, script, mission, 0 );

	private static ThemeParkIncChallengeSettings Settings( IReadOnlyDictionary<int, ThemeParkIncChallengeRow> rows, int[] level, int daysBetween = 20, int mission = 0 )
	{
		var all = Enumerable.Range( 0, ThemeParkIncChallengeSettings.RowCount ).ToDictionary( index => index, index => rows.TryGetValue( index, out var row ) ? row : Row( 0 ) );
		var list = level.Concat( Enumerable.Repeat( 0, ThemeParkIncChallengeSettings.LevelEntries ) ).Take( ThemeParkIncChallengeSettings.LevelEntries ).ToArray();
		return new ThemeParkIncChallengeSettings( daysBetween, mission, list, all );
	}

	private static ThemeParkIncChallenges Challenges( params ThemeParkIncChallengeRow[] rows ) =>
		new( Settings( rows.Select( ( row, index ) => (row, index) ).ToDictionary( pair => pair.index + 1, pair => pair.row ), rows.Select( ( _, index ) => index + 1 ).ToArray() ) );

	/// <summary>The first park turn whose clock is at least <paramref name="days"/> game days after turn 0 (one day is 23.04 turns).</summary>
	private static long TurnAtDays( double days ) => (long)Math.Ceiling( days * ThemeParkIncChallenges.Day / (15000 / 4 * 10_000_000.0) );

	// ---- loading -------------------------------------------------------------------------------

	[TestMethod]
	public void TheLoaderBuildsTheLevelsRowsIntoTheFirstFreeSlotsInListOrder()
	{
		// [BIN:TPI-EXE:0x5C7630 load slots] the level list holds row numbers; a value of 0 or less is skipped, each row lands in the first slot whose type is 0
		var rows = new Dictionary<int, ThemeParkIncChallengeRow> { [3] = Row( 2 ), [7] = Row( 41 ), [9] = Row( 25, tickets: 0 ), [11] = Row( 0 ), [12] = Row( 17 ) };
		var system = new ThemeParkIncChallenges( Settings( rows, new[] { 7, 0, -4, 3, 11, 9, 12 } ) );
		CollectionAssert.AreEqual( new[] { 41, 2, 25, 17, 0, 0, 0, 0, 0, 0 }, system.Slots.Select( slot => slot.Type ).ToArray(),
			"row 11 has type 0, so its slot stays free and row 9 overwrites it, then row 12 lands after it" );
		Assert.AreEqual( 1, system.Slots[2].GTPrize, "the record constructor raises the golden-ticket prize to 1" );
		Assert.IsTrue( system.Slots.All( slot => slot.Status == ThemeParkIncChallengeStatus.Idle && !slot.OfferTimeSet && slot.Value == 0 ) );
	}

	[TestMethod]
	public void TheFollowUpFilterEmptiesAFollowUpWithoutItsChallenge()
	{
		// [BIN:TPI-EXE:0x5C75C0 follow-up filter] a slot with a follow-up type stays only while another occupied slot has that type, slot by slot in order
		var rows = new Dictionary<int, ThemeParkIncChallengeRow> { [1] = Row( 2, followup: 41 ), [2] = Row( 41 ), [3] = Row( 25, followup: 17 ), [4] = Row( 17, followup: 25 ) };
		var system = new ThemeParkIncChallenges( Settings( rows, new[] { 1, 2, 3 } ) );
		CollectionAssert.AreEqual( new[] { 2, 41, 0 }, system.Slots.Take( 3 ).Select( slot => slot.Type ).ToArray(), "the type-25 slot's follow-up 17 is in no slot" );
		var paired = new ThemeParkIncChallenges( Settings( rows, new[] { 3, 4 } ) );
		CollectionAssert.AreEqual( new[] { 25, 17 }, paired.Slots.Take( 2 ).Select( slot => slot.Type ).ToArray() );
	}

	[TestMethod]
	public void ARowNumberPastTheParsedRowsIsRefused()
	{
		var standard = new SamSettings( new[] { SamDocument.Parse( "Challenges.DaysBetweenOffers 20\nChallengesInThisLevel[0].ChallengeType 60\n", "Standard.sam" ) } );
		var challenges = new SamSettings( new[] { SamDocument.Parse( "Challenges[1].Type 2\n", "Challenges.sam" ) } );
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncChallengeSettings.Read( standard, challenges ) );
		Assert.IsNull( ThemeParkIncChallengeSettings.Read( standard, null ), "no Challenges.sam, no challenge system" );
	}

	[TestMethod]
	public void TheRetailScienceLevelLoadsItsTenRowsInListOrder()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		string Find( string directory, string name ) => Directory.EnumerateFiles( directory ).First( file => string.Equals( Path.GetFileName( file ), name, StringComparison.OrdinalIgnoreCase ) );
		var levels = Directory.EnumerateDirectories( data ).First( directory => string.Equals( Path.GetFileName( directory ), "levels", StringComparison.OrdinalIgnoreCase ) );
		var science = Directory.EnumerateDirectories( levels ).First( directory => string.Equals( Path.GetFileName( directory ), "science", StringComparison.OrdinalIgnoreCase ) );
		var standard = new SamSettings( new[] { Find( levels, "Standard.sam" ), Find( science, "Standard.sam" ) }.Select( file => SamDocument.Parse( File.ReadAllText( file ), file ) ) );
		var challengesFile = Find( data, "Challenges.sam" );
		var challenges = new SamSettings( new[] { SamDocument.Parse( File.ReadAllText( challengesFile ), challengesFile ) } );
		var settings = ThemeParkIncChallengeSettings.Read( standard, challenges )!;
		Assert.AreEqual( 20, settings.DaysBetweenOffers );
		Assert.AreEqual( 0, settings.MissionNumberForFirstChallenge );
		CollectionAssert.AreEqual( new[] { 2, 4, 5, 14, 13, 18, 21, 8, 20, 22 }, settings.LevelRows.ToArray() );
		var system = new ThemeParkIncChallenges( settings );
		CollectionAssert.AreEqual( new[] { 2, 35, 25, 46, 36, 41, 56, 17, 48, 17 }, system.Slots.Select( slot => slot.Type ).ToArray(), "the slots follow the list" );
		CollectionAssert.AreEqual( new[] { 2, 4, 5, 14, 13, 18, 21, 8, 20, 22 }, system.Slots.Select( slot => slot.ScriptIndex ).ToArray() );
		CollectionAssert.AreEqual( new[] { 2, 2, 2, 2, 2, 2, 4, 0, 3, 0 }, system.Slots.Select( slot => slot.MissionNeeded ).ToArray(),
			"with a new career's mission counter of 0 only the two ride rows pass the mission gate" );
		Assert.AreEqual( (ushort)1160, system.Slots[7].TargetObj );
		Assert.IsTrue( system.Slots.Count( slot => ThemeParkIncChallenges.IsPorted( slot.Type ) ) == 3, "types 2, 25 and 41 are measured" );
	}

	// ---- switch, availability and offers -------------------------------------------------------

	[TestMethod]
	public void TheSystemSwitchesOnOnceTheParkIsOpenAndTheMissionGateIsMet()
	{
		var system = Challenges( Row( 2, delay: 0 ) );
		var host = new Host { ParkOpen = false, FatShop = true };
		system.Update( 1, host );
		Assert.IsFalse( system.SystemSwitchedOn, "a closed park keeps the system off" );
		Assert.IsFalse( system.Slots[0].OfferTimeSet, "and nothing else runs" );
		host.ParkOpen = true;
		system.Update( 2, host );
		Assert.IsTrue( system.SystemSwitchedOn );
		host.ParkOpen = false;
		system.Update( 3, host );
		Assert.IsTrue( system.SystemSwitchedOn, "the switch is never cleared" );
		var gated = new ThemeParkIncChallenges( Settings( new Dictionary<int, ThemeParkIncChallengeRow> { [1] = Row( 2 ) }, new[] { 1 }, mission: 1 ) );
		gated.Update( 1, new Host { FatShop = true } );
		Assert.IsFalse( gated.SystemSwitchedOn, "a mission counter below MissionNumberForFirstChallenge keeps it off (signed)" );
	}

	[TestMethod]
	public void AvailabilityLatchesOnceAfterTheTriggerAndTheMinimumDays()
	{
		// [BIN:TPI-EXE:0x5C36D0 availability] (trigger && days > Min) || (Max != 0 && days > Max), then Mission <= counter, all unsigned; offer time = now + DaysDelay days
		var slot = ThemeParkIncChallengeSlot.FromRow( Row( 2, min: 2, delay: 3 ) );
		var host = new Host { FatShop = true };
		ThemeParkIncChallenges.CalcAvailTime( slot, TurnAtDays( 2 ), host );
		Assert.IsFalse( slot.OfferTimeSet, "day 2 is not past MinDaysTilTrigger 2" );
		var turn = TurnAtDays( 3 );
		ThemeParkIncChallenges.CalcAvailTime( slot, turn, host );
		Assert.IsTrue( slot.OfferTimeSet );
		Assert.AreEqual( ThemeParkIncChallenges.Now( turn ) + 3 * ThemeParkIncChallenges.Day, slot.OfferTime );
		host.FatShop = false;
		ThemeParkIncChallenges.CalcAvailTime( slot, turn + 100, host );
		Assert.AreEqual( ThemeParkIncChallenges.Now( turn ) + 3 * ThemeParkIncChallenges.Day, slot.OfferTime, "the latch is never cleared" );

		var late = ThemeParkIncChallengeSlot.FromRow( Row( 2, max: 4 ) );
		ThemeParkIncChallenges.CalcAvailTime( late, TurnAtDays( 4 ), new Host() );
		Assert.IsFalse( late.OfferTimeSet, "without the trigger only days past MaxDaysTilTrigger count" );
		ThemeParkIncChallenges.CalcAvailTime( late, TurnAtDays( 5 ), new Host() );
		Assert.IsTrue( late.OfferTimeSet );

		var negative = ThemeParkIncChallengeSlot.FromRow( Row( 2, min: -1 ) );
		ThemeParkIncChallenges.CalcAvailTime( negative, TurnAtDays( 100 ), new Host { FatShop = true } );
		Assert.IsFalse( negative.OfferTimeSet, "a negative MinDaysTilTrigger compares as a huge unsigned number" );

		var mission = ThemeParkIncChallengeSlot.FromRow( Row( 2, mission: 2 ) );
		ThemeParkIncChallenges.CalcAvailTime( mission, TurnAtDays( 1 ), new Host { FatShop = true } );
		Assert.IsFalse( mission.OfferTimeSet, "MissionNeededToActivate 2 is above a new career's 0" );
		ThemeParkIncChallenges.CalcAvailTime( mission, TurnAtDays( 1 ), new Host { FatShop = true, MissionCounter = 2 } );
		Assert.IsTrue( mission.OfferTimeSet );
	}

	[TestMethod]
	public void ATypeOpenTpwCannotMeasureIsNeverMadeAvailable()
	{
		// [EXT:tpi-capability-gates] type 17's trigger reads the target ride's activation flags, which OpenTPW does not model: even MaxDaysTilTrigger does not make it available
		var slot = ThemeParkIncChallengeSlot.FromRow( Row( 17, max: 1, targetObj: 1160 ) );
		ThemeParkIncChallenges.CalcAvailTime( slot, TurnAtDays( 500 ), new Host { FatShop = true, Staff = 99, EntertainerExperience = 99 } );
		Assert.IsFalse( slot.OfferTimeSet );
		foreach ( var type in new[] { 35, 46, 36, 56, 17, 48 } )
			Assert.IsFalse( ThemeParkIncChallenges.IsPorted( type ), $"type {type}" );
		foreach ( var type in new[] { 2, 25, 41 } )
			Assert.IsTrue( ThemeParkIncChallenges.IsPorted( type ), $"type {type}" );
	}

	[TestMethod]
	public void OffersComeLowestSlotFirstEveryDaysBetweenOffersAndDoNotWaitForAnAnswer()
	{
		var system = Challenges( Row( 2 ), Row( 41, trigger: 3 ), Row( 25, trigger: 3 ) );
		var host = new Host { FatShop = true, Staff = 3, EntertainerExperience = 3 };
		system.Update( TurnAtDays( 1 ) - 1, host );
		Assert.IsFalse( system.Slots.Any( slot => slot.OfferTimeSet ), "day 0 is not past a MinDaysTilTrigger of 0" );
		var first = TurnAtDays( 1 );
		system.Update( first, host );
		Assert.IsTrue( system.Slots.Take( 3 ).All( slot => slot.OfferTimeSet ), "all three trigger at once on day 1: " + string.Join( ",", system.Slots.Select( slot => slot.OfferTimeSet ) ) );
		CollectionAssert.AreEqual( Array.Empty<int>(), host.Offers.ToArray(), "the offer time must be strictly passed" );
		system.Update( first + 1, host );
		CollectionAssert.AreEqual( new[] { 0 }, host.Offers.ToArray(), "the lowest due slot first" );
		Assert.AreEqual( ThemeParkIncChallenges.Now( first + 1 ) + 20 * ThemeParkIncChallenges.Day, system.NextChallengeDelayTime );
		var second = first + 1;
		while ( ThemeParkIncChallenges.Now( second ) <= system.NextChallengeDelayTime )
			second++;
		system.Update( second - 1, host );
		Assert.AreEqual( 1, host.Offers.Count, "nothing before DaysBetweenOffers have passed" );
		system.Update( second, host );
		CollectionAssert.AreEqual( new[] { 0, 1 }, host.Offers.ToArray(), "slot 0 still unanswered does not block slot 1" );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Offered, system.Slots[0].Status );
		system.Accept( 1, second + 1 );
		system.Update( second + TurnAtDays( 25 ), host );
		Assert.AreEqual( 2, host.Offers.Count, "an accepted slot blocks the offers" );
	}

	// ---- accept and the phases -----------------------------------------------------------------

	[TestMethod]
	public void AcceptStartsTheGracePeriodOrTheTimedPhaseAndResetsTheValues()
	{
		var system = Challenges( Row( 2, targetTime: 60 ), Row( 25, targetTime: 30, grace: 10 ) );
		system.Slots[0].Value = 7;
		system.Slots[0].Value2 = 3;
		system.Accept( 0, 100 );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Running, system.Slots[0].Status, "no grace period: the timed phase" );
		Assert.AreEqual( (0, 0), (system.Slots[0].Value, system.Slots[0].Value2) );
		Assert.AreEqual( ThemeParkIncChallenges.Now( 100 ) + 60 * ThemeParkIncChallenges.Day, system.Slots[0].FinishTime );
		system.Accept( 1, 100 );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Grace, system.Slots[1].Status );
		Assert.AreEqual( ThemeParkIncChallenges.Now( 100 ) + 10 * ThemeParkIncChallenges.Day, system.Slots[1].FinishTime );
	}

	[TestMethod]
	public void TheGracePeriodRunsNoTestsAndHandsItsCountsToTheTimedPhase()
	{
		var system = Challenges( Row( 2, targetTime: 30, targetVal: 5, grace: 2 ) );
		var host = new Host();
		system.Update( 0, host );
		system.Accept( 0, 0 );
		system.AddEvent( 2, 9, 0, false );
		system.Update( 1, host );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Grace, system.Slots[0].Status, "no win test during grace although V >= TV" );
		var lastDay = TurnAtDays( 1 ) + 1;
		system.Update( lastDay, host );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Running, system.Slots[0].Status, "the last day of grace starts the timed phase" );
		Assert.AreEqual( 9, system.Slots[0].Value, "the count carries over" );
		Assert.AreEqual( ThemeParkIncChallenges.Now( lastDay ) + 30 * ThemeParkIncChallenges.Day, system.Slots[0].FinishTime );
		system.Update( lastDay + 1, host );
		CollectionAssert.AreEqual( new[] { 0 }, host.Wins.ToArray(), "the next update wins it early" );
	}

	[TestMethod]
	public void AClosedParkLosesAGraceOrRunningChallenge()
	{
		var system = Challenges( Row( 2, grace: 5 ) );
		var host = new Host();
		system.Update( 0, host );
		system.Accept( 0, 0 );
		host.ParkOpen = false;
		system.Update( 1, host );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Offered, system.Slots[0].Status, "a lost challenge returns to state 1" );
		CollectionAssert.AreEqual( new[] { (0, true) }, host.Losses.ToArray() );
		Assert.AreEqual( 4, host.FiringLine, "its first loss takes one off the firing line" );
		host.ParkOpen = true;
		system.Accept( 0, 2 );
		host.ParkOpen = false;
		system.Update( 3, host );
		Assert.AreEqual( (0, false), host.Losses[1], "a second loss of the same slot is not a first" );
		Assert.AreEqual( 4, host.FiringLine );
		Assert.AreEqual( 2, system.Slots[0].ChallengeLost );
	}

	[TestMethod]
	public void TheFinalDayDecidesWithoutTheFailureTestAndEndOnlyTypesWinThere()
	{
		// type 25 is end-only: it fails while V < TV with days left, and on the final day DidThisWinIt(1) answers true with no failure test
		var system = Challenges( Row( 25, targetTime: 2, targetVal: 30, checkAtEnd: 1 ), Row( 2, targetTime: 2, targetVal: 30 ) );
		var host = new Host { LeastHappiness = 50 };
		system.Update( 0, host );
		system.Accept( 0, 0 );
		system.Update( 1, host );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Running, system.Slots[0].Status, "an end-only type does not win early" );
		Assert.AreEqual( 50, system.Slots[0].Value, "type 25 polls the lowest staff happiness" );
		host.LeastHappiness = 10;
		var final = TurnAtDays( 1 ) + 1;
		Assert.AreEqual( 0, ThemeParkIncChallenges.DaysLeft( system.Slots[0], final ) );
		system.Update( final, host );
		CollectionAssert.AreEqual( new[] { 0 }, host.Wins.ToArray(), "on the final day the failure test is not run and type 25 wins" );

		system.Accept( 1, final );
		system.AddEvent( 2, 29, 0, false );
		system.Update( final + TurnAtDays( 1 ) + 1, host );
		CollectionAssert.AreEqual( new[] { (1, true) }, host.Losses.ToArray(), "29 hot dogs of 30 by the final day lose" );
	}

	[TestMethod]
	public void AFailureWithDaysLeftLosesAndAnEarlyWinNeedsAnOpenPark()
	{
		var system = Challenges( Row( 25, targetTime: 30, targetVal: 30, checkAtEnd: 1 ), Row( 41, targetTime: 90, checkAtEnd: 1 ) );
		var host = new Host { LeastHappiness = 29 };
		system.Update( 0, host );
		system.Accept( 0, 0 );
		system.Update( 1, host );
		CollectionAssert.AreEqual( new[] { (0, true) }, host.Losses.ToArray(), "a staff member below 30 fails type 25" );
		system.Accept( 1, 2 );
		host.Strike = true;
		system.Update( 3, host );
		Assert.AreEqual( (1, true), host.Losses[1], "a strike fails type 41" );
	}

	[TestMethod]
	public void APassThatJumpsADayPastTheFinishNeverTimesOut()
	{
		// [BIN:TPI-EXE:0x5C54E0 days left] the signed quotient stays negative and nonzero, so the final-day branch is never taken again
		var system = Challenges( Row( 2, targetTime: 1, targetVal: 1000 ) );
		var host = new Host();
		system.Update( 0, host );
		system.Accept( 0, 0 );
		var beyond = TurnAtDays( 2 ) + 1;
		Assert.IsTrue( ThemeParkIncChallenges.DaysLeft( system.Slots[0], beyond ) < 0 );
		for ( var turn = beyond; turn < beyond + 1000; turn += 37 )
			system.Update( turn, host );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Running, system.Slots[0].Status, "type 2 never fails, and the time-out never comes" );
		Assert.AreEqual( 0, host.Wins.Count + host.Losses.Count );
	}

	[TestMethod]
	public void EventsReachTheAcceptedSlotsOfTheirTypeAndTarget()
	{
		var system = Challenges( Row( 17, targetObj: 1160 ), Row( 17, targetObj: 1170 ), Row( 2 ) );
		system.Slots[0].Status = ThemeParkIncChallengeStatus.Running;
		system.Slots[1].Status = ThemeParkIncChallengeStatus.Grace;
		system.AddEvent( 17, 1, 1160, false );
		system.AddEvent( 17, 2, 0, false );
		system.AddEvent( 17, 4, 1170, true );
		system.AddEvent( 2, 8, 0, false );
		Assert.AreEqual( (3, 0), (system.Slots[0].Value, system.Slots[0].Value2) );
		Assert.AreEqual( (2, 4), (system.Slots[1].Value, system.Slots[1].Value2) );
		Assert.AreEqual( 0, system.Slots[2].Value, "an idle slot hears nothing" );
	}

	// ---- the Science types' tests --------------------------------------------------------------

	[DataTestMethod]
	// type, CheckAtEndOnly, TV, TV2, V, V2, strike, won early, won at the end, failed
	[DataRow( 2, 0, 30, 0, 30, 0, false, true, true, false )]
	[DataRow( 2, 0, 30, 0, 29, 0, false, false, false, false )]
	[DataRow( 35, 1, 40, 0, 39, 0, false, false, true, true )]
	[DataRow( 35, 1, 40, 0, 40, 0, false, false, true, false )]
	[DataRow( 25, 1, 30, 0, 29, 0, false, false, true, true )]
	[DataRow( 46, 1, 10, 0, 9, 0, false, false, false, true )]
	[DataRow( 46, 1, 10, 0, 10, 0, false, false, true, false )]
	[DataRow( 36, 1, 60, 0, 59, 0, false, false, true, true )]
	[DataRow( 41, 1, 0, 0, 0, 0, false, false, true, false )]
	[DataRow( 41, 1, 0, 0, 1, 0, true, false, false, true )]
	[DataRow( 56, 1, 60, 50, 60, 50, false, false, true, false )]
	[DataRow( 56, 1, 60, 50, 60, 49, false, false, false, true )]
	[DataRow( 17, 0, 20, 0, 20, 0, false, true, true, false )]
	[DataRow( 17, 0, 20, 0, 19, 0, false, false, false, false )]
	[DataRow( 48, 1, 20, 0, 20, 0, false, false, true, false )]
	[DataRow( 48, 1, 20, 0, 25, 1, false, false, false, true )]
	public void TheScienceTypesWinAndFailAsTheExecutableTests( int type, int atEndOnly, int targetVal, int targetVal2, int value, int value2, bool strike, bool earlyWin, bool endWin, bool failed )
	{
		var slot = ThemeParkIncChallengeSlot.FromRow( Row( type, targetVal: targetVal, targetVal2: targetVal2, checkAtEnd: atEndOnly ) );
		slot.Value = value;
		slot.Value2 = value2;
		Assert.AreEqual( earlyWin, slot.DidThisWinIt( atEnd: false, strike ), "DidThisWinIt(0)" );
		Assert.AreEqual( endWin, slot.DidThisWinIt( atEnd: true, strike ), "DidThisWinIt(1)" );
		Assert.AreEqual( failed, slot.HasFailed(), "HasFailed" );
	}

	// ---- the economy's side --------------------------------------------------------------------

	private const string Standard =
		"BankAccountInfo.InitialCash\t50000\nBankAccountInfo.InitialAdmissionFee\t25\n"
		+ "LoanInfo[0].LoanAmount\t10000\nLoanInfo[0].APRInPercent\t20\nLoanInfo[0].RepaymentPeriodInMonths\t36\nLoanInfo[0].Lendername\t0\n"
		+ "Costs.QueueCell 25\nCosts.PathCell 10\nCosts.KartTrackCell 40\nCosts.WaterTrackCell 50\nCosts.MapCell 75\n"
		+ "PeepInfo.ExcitementToCostDivisor 6\nPeepInfo.MinimumEntryFee 20\nPeepInfo.CheapPriceMultiplier 0.75\nPeepInfo.AveragePriceMultiplier 1.25\nPeepInfo.ExpensivePriceMultiplier 2.0\n"
		+ "Challenges.DaysBetweenOffers 20\nChallenges.MissionNumberForFirstChallenge 0\n"
		+ "ChallengesInThisLevel[0].ChallengeType 2\nChallengesInThisLevel[1].ChallengeType 5\nChallengesInThisLevel[2].ChallengeType 8\n";

	private const string ChallengesSam =
		"Challenges[2].Type 2\nChallenges[2].TargetTime 60\nChallenges[2].TargetVal 3\nChallenges[2].CashPrize 5000\nChallenges[2].GTPrize 2\nChallenges[2].ScriptIndex 2\n"
		+ "Challenges[5].Type 25\nChallenges[5].TriggerData 3\nChallenges[5].TargetTime 30\nChallenges[5].TargetVal 30\nChallenges[5].CheckAtEndOnly 1\nChallenges[5].GracePeriodLimit 10\nChallenges[5].ScriptIndex 5\n"
		+ "Challenges[8].Type 17\nChallenges[8].TargetObj 1160\nChallenges[8].TargetVal 20\nChallenges[8].ScriptIndex 8\n";

	private static readonly EconomyObjectInfo HotDogs = new( 1207, "Hot dogs", ParkObjectKind.Shop, ResearchCategory.Shop, 0,
		new[] { new UpgradeLevelInfo( 0, 500, 0, 0, 0, new[] { 50, 30, 20, 10 } ) }, 10, 2, null, 2, 1, 0, 0, 0, 0, "/x" );

	private static ParkEconomy TpwEconomy() => new( EconomyTestData.Settings(), new EconomyObjectCatalog( Array.Empty<EconomyObjectInfo>() ), ParkGameMode.FullSimulation, 7 );

	internal static ParkEconomy Economy()
	{
		var settings = BalanceSettings.ForThemeParkInc( new SamSettings( new[] { SamDocument.Parse( Standard, "Standard.sam" ) } ), "science",
			new SamSettings( new[] { SamDocument.Parse( ChallengesSam, "Challenges.sam" ) } ) );
		return new ParkEconomy( settings, new EconomyObjectCatalog( new[] { HotDogs } ), ParkGameMode.FullSimulation, 7, EconomyRules.ThemeParkIncMoney );
	}

	[TestMethod]
	public void AThemeParkIncParkRunsItsOwnChallengeSystemAndThemeParkWorldDoesNot()
	{
		var economy = Economy();
		Assert.IsNotNull( economy.TpiChallenges );
		Assert.IsFalse( economy.Features.Challenges || economy.Features.GoldenTickets, "Theme Park World's objectives stay off" );
		CollectionAssert.AreEqual( new[] { 2, 25, 17 }, economy.TpiChallenges!.Slots.Take( 3 ).Select( slot => slot.Type ).ToArray() );
		Assert.IsNull( TpwEconomy().TpiChallenges, "a Theme Park World park has none" );
		var noChallenges = new ParkEconomy( economy.Settings, economy.Catalog, ParkGameMode.FullSimulation, 7, new EconomyRules( false, false, false, true ) );
		Assert.IsNull( noChallenges.TpiChallenges, "rules without challenges build none" );
	}

	[TestMethod]
	public void AHotDogShopTriggersTheOfferItsSalesCountAndTheWinPaysTheBankAndTheCareer()
	{
		var economy = Economy();
		economy.OpenPark();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1207, out var shop ) );
		economy.Advance( ParkCalendar.TickOfTurn( TurnAtDays( 1 ) + 1 ) - economy.Tick );
		var challenges = economy.TpiChallenges!;
		Assert.IsTrue( challenges.SystemSwitchedOn );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Offered, challenges.Slots[0].Status, "the fat shop's trigger offers the hot-dog challenge" );
		Assert.AreEqual( new ThemeParkIncChallengeNotice( 0, ThemeParkIncChallengeNoticeKind.Offered ), economy.TpiChallengeNotices.Single() );
		Assert.IsFalse( challenges.Slots[1].OfferTimeSet, "type 25 waits for an entertainer with experience" );
		Assert.IsFalse( economy.CanAcceptThemeParkIncChallenge( 1 ), "an idle slot cannot be accepted" );
		Assert.IsTrue( economy.CanAcceptThemeParkIncChallenge( 0 ) );
		economy.ClosePark();
		Assert.IsFalse( economy.CanAcceptThemeParkIncChallenge( 0 ), "the accept control needs an open park" );
		Assert.ThrowsException<InvalidOperationException>( () => economy.AcceptThemeParkIncChallenge( 0 ) );
		economy.OpenPark();
		economy.AcceptThemeParkIncChallenge( 0 );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Running, challenges.Slots[0].Status );
		var balance = economy.Balance;
		for ( var sale = 0; sale < 3; sale++ )
			economy.SettleVisit( shop.Id, 10, null );
		Assert.AreEqual( 3, challenges.Slots[0].Value, "each paid hot-dog sale counts one" );
		economy.Advance( ParkCalendar.TickOfTurn( economy.Turn + 1 ) - economy.Tick );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Won, challenges.Slots[0].Status );
		Assert.AreEqual( balance + 30 + 5000, economy.Balance, "the takings and the cash prize" );
		Assert.AreEqual( 2, economy.TpiCareer!.GoldenTickets, "the golden tickets go to the career" );
		Assert.AreEqual( ThemeParkIncChallengeNoticeKind.Won, economy.TpiChallengeNotices.Last().Kind );
	}

	[TestMethod]
	public void LossesShortenTheFiringLineOnceEachAndAnEmptyLinePostsTheLetterEveryTime()
	{
		var economy = Economy();
		economy.OpenPark();
		var challenges = economy.TpiChallenges!;
		var career = economy.TpiCareer!;
		career.FiringLine = 2;
		economy.Advance( ParkCalendar.TickOfTurn( 1 ) - economy.Tick );
		Assert.AreEqual( 0, economy.DebugOfferThemeParkIncChallenge( 0 ) );
		economy.AcceptThemeParkIncChallenge( 0 );
		economy.ClosePark();
		economy.Advance( ParkCalendar.TickOfTurn( economy.Turn + 1 ) - economy.Tick );
		Assert.AreEqual( 1, career.FiringLine );
		Assert.IsNull( economy.TakeTpiMail(), "a positive line posts the lost mail, not the letter" );
		Assert.AreEqual( ThemeParkIncChallengeNoticeKind.Lost, economy.TpiChallengeNotices.Last().Kind );
		economy.OpenPark();
		economy.AcceptThemeParkIncChallenge( 0 );
		economy.ClosePark();
		economy.Advance( ParkCalendar.TickOfTurn( economy.Turn + 1 ) - economy.Tick );
		Assert.AreEqual( 1, career.FiringLine, "the same challenge lost again takes nothing off" );
		Assert.IsNull( economy.TakeTpiMail() );
		economy.OpenPark();
		challenges.Slots[1].Status = ThemeParkIncChallengeStatus.Offered;
		economy.AcceptThemeParkIncChallenge( 1 );
		economy.ClosePark();
		economy.Advance( ParkCalendar.TickOfTurn( economy.Turn + 1 ) - economy.Tick );
		Assert.AreEqual( 0, career.FiringLine );
		Assert.AreEqual( ThemeParkIncAdviceMail.ChallengeLost, economy.TakeTpiMail(), "the loss that empties the line posts the firing letter" );
		economy.OpenPark();
		economy.AcceptThemeParkIncChallenge( 0 );
		economy.ClosePark();
		economy.Advance( ParkCalendar.TickOfTurn( economy.Turn + 1 ) - economy.Tick );
		Assert.AreEqual( ThemeParkIncAdviceMail.ChallengeLost, economy.TakeTpiMail(), "and every loss after it" );
	}

	[TestMethod]
	public void TheChallengeSystemSurvivesTheSaveAndAThemeParkWorldSaveMayNotCarryIt()
	{
		var economy = Economy();
		economy.OpenPark();
		economy.TryBuild( 1207, out _ );
		economy.Advance( ParkCalendar.TickOfTurn( TurnAtDays( 1 ) + 2 ) - economy.Tick );
		economy.AcceptThemeParkIncChallenge( 0 );
		economy.TpiChallenges!.AddEvent( 2, 2, 0, false );
		economy.TpiCareer!.FiringLine = 3;
		economy.TpiCareer.AddGoldenTickets( 4 );
		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"TpiChallenges\"" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ), "the reloaded park saves the same" );
		var slot = restored.TpiChallenges!.Slots[0];
		Assert.AreEqual( (ThemeParkIncChallengeStatus.Running, 2), (slot.Status, slot.Value) );
		Assert.AreEqual( (3, 4, true), (restored.TpiCareer!.FiringLine, restored.TpiCareer.GoldenTickets, restored.TpiChallenges.SystemSwitchedOn) );
		Assert.IsFalse( ParkSaveFile.Serialize( TpwEconomy() ).Contains( "TpiChallenges", StringComparison.Ordinal ), "a Theme Park World save writes no Theme Park Inc challenges" );
		var saved = economy.TpiChallenges.Capture();
		Assert.IsTrue( ThemeParkIncChallenges.IsValid( saved ) );
		var unmeasured = saved with { Slots = saved.Slots.Select( ( item, index ) => index == 0 ? item with { Type = 17 } : item ).ToArray() };
		Assert.IsFalse( ThemeParkIncChallenges.IsValid( unmeasured ), "a running challenge of a type OpenTPW cannot measure is refused" );
		Assert.IsFalse( ThemeParkIncChallenges.IsValid( saved with { Slots = saved.Slots.Take( 9 ).ToArray() } ), "ten slots" );
		var latched = saved with { Slots = saved.Slots.Select( ( item, index ) => index == 1 ? item with { Type = 17, Status = ThemeParkIncChallengeStatus.Idle, OfferTimeSet = true } : item ).ToArray() };
		Assert.IsFalse( ThemeParkIncChallenges.IsValid( latched ), "an idle slot of an unmeasured type latched as due would be offered" );
		Assert.IsTrue( ThemeParkIncChallenges.IsValid( latched with { Slots = latched.Slots.Select( ( item, index ) => index == 1 ? item with { OfferTimeSet = false } : item ).ToArray() } ),
			"unlatched, it waits" );

		var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		var system = node["TpiChallenges"]!.DeepClone();
		node.Remove( "TpiChallenges" );
		var older = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( node.ToJsonString() ) ), economy.Settings, economy.Catalog ) );
		StringAssert.Contains( older.Message, "before OpenTPW kept the challenges", "a Theme Park Inc save from before the challenge system is refused, not continued with a new one" );
		var carrying = System.Text.Json.Nodes.JsonNode.Parse( ParkSaveFile.Serialize( TpwEconomy() ) )!.AsObject();
		carrying["TpiChallenges"] = system;
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( carrying.ToJsonString() ) ), "a Theme Park World save may not carry the challenges" );
	}
}
