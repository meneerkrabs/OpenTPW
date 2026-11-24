using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's special rides and the first share-prize challenge types (docs/reverse/TPI-CHALLENGE-SYSTEM.md §11): the special-ride table and its staff slots, the activation, the
/// special-ride trigger with the research lab's fallback, the values and tests of types 17, 46, 52 and 58, the admission events, the shares a win brings and the save. The economy tests use
/// the retail Science park (<c>OPENTPW_TPI_PATH</c>); the table and the trigger are synthetic.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncShareChallengeTests : ThemeParkIncScienceTestBase
{
	/// <summary>The Water Ride (Info.Id 1160): Info.IsSpecial 1, UsageInfo.GoldenTicketCost 1, SkillSet[9].Num 1 (a gardener in Water/Drainage).</summary>
	private const int WaterRide = 1160;
	/// <summary>The Tour Ride (Info.Id 1170), a special ride.</summary>
	private const int TourRide = 1170;
	/// <summary>The Log Mill (Info.Id 1180), a special ride.</summary>
	private const int LogMill = 1180;
	/// <summary>The Giant Robot Swing (Info.Id 1101), an ordinary ride.</summary>
	private const int RobotSwing = 1101;

	private sealed class Host : IThemeParkIncChallengeHost
	{
		public bool ParkOpen { get; set; } = true;
		public int MissionCounter { get; set; }
		public void LowerFiringLine() { }
		public bool HasFatShop() => false;
		public int MaxExperience( int kind ) => 0;
		public int StaffCount() => 0;
		public int MinStaffHappiness() => 100;
		public bool AnyRoleOnStrike() => false;
		public void Offered( int index ) { }
		public void Won( int index, ThemeParkIncChallengeSlot slot ) { }
		public void Lost( int index, ThemeParkIncChallengeSlot slot, bool firstLoss ) { }
		public Dictionary<ushort, ThemeParkIncChallengeTarget> Targets { get; } = new();
		public HashSet<ushort> Researching { get; } = new();
		public ThemeParkIncChallengeTarget FindTarget( ushort type ) => Targets.TryGetValue( type, out var target ) ? target : ThemeParkIncChallengeTarget.None;
		public bool LabResearching( ushort type ) => Researching.Contains( type );
		public int BreakdownsEver { get; set; }
		public int LeastRepair { get; set; } = 100;
		public int LeastRideRepair() => LeastRepair;
		public int GuestsInPark { get; set; }
	}

	private static ThemeParkIncChallengeSlot Slot( int type, int targetObj = 0, int targetVal = 0, int targetVal2 = 0, int checkAtEnd = 0, int trigger = 0 ) =>
		ThemeParkIncChallengeSlot.FromRow( new ThemeParkIncChallengeRow( type, 0, 30, targetVal, targetVal2, targetObj, 0, 0, 0, 1, 0, 4, 2, checkAtEnd, 0, 0, 0, trigger, 0, 0, 0, 0, 0, 0 ) );

	private static ThemeParkIncStaffMember Member( int thing, ThemeParkIncStaffRole role, int ability ) =>
		new( thing, role, 0, 20000, 20, 0, 0 ) { ThingId = thing, Ability = ability };

	private static int[] SkillSet( params (int Set, int Number)[] sets )
	{
		var result = new int[ThemeParkIncSpecialRides.SkillSets];
		foreach ( var (set, number) in sets )
			result[set] = number;
		return result;
	}

	// ---- the table -----------------------------------------------------------------------------

	[TestMethod]
	public void TheRecordAsksForEachSkillSetsStaffTypeAndAbilityAndPadsWithStaffTypeSeven()
	{
		// [BIN:TPI-EXE:0x62B640 ability's staff type] 1..6 the mechanic, 7 and 8 the handyman, 9 the guard, 10..14 the gardener, others 7
		CollectionAssert.AreEqual( new[] { 7, 1, 1, 1, 1, 1, 1, 0, 0, 3, 5, 5, 5, 5, 5, 7, 7 }, Enumerable.Range( 0, 17 ).Select( ThemeParkIncSpecialRides.StaffTypeOf ).ToArray() );
		// [BIN:TPI-EXE:0x623DC0 record builder] the Log Mill's SkillSet[4].Num 1 and SkillSet[13].Num 1: a mechanic in Tunnelling (5) and a gardener in Logging (14)
		var rides = new ThemeParkIncSpecialRides();
		rides.Add( 3, SkillSet( (13, 1), (4, 1) ) );
		var record = rides.Records.Single();
		Assert.AreEqual( 3, record.Instance );
		CollectionAssert.AreEqual( new[] { new ThemeParkIncSpecialRideStaffSlot( 1, 5, 0 ), new ThemeParkIncSpecialRideStaffSlot( 5, 14, 0 ) }, record.Slots.Take( 2 ).ToArray() );
		Assert.IsTrue( record.Slots.Skip( 2 ).All( slot => slot == new ThemeParkIncSpecialRideStaffSlot( 7, 0, 0 ) ) );
		Assert.IsFalse( rides.IsFree( 3 ) );
		Assert.IsTrue( rides.IsFree( 4 ), "a ride without a record is free to use" );
		for ( var instance = 4; instance < 7; instance++ )
			rides.Add( instance, SkillSet() );
		Assert.ThrowsException<InvalidOperationException>( () => rides.Add( 9, SkillSet() ), "the table holds four records before its own count" );
		Assert.ThrowsException<InvalidDataException>( () => new ThemeParkIncSpecialRides().Add( 1, SkillSet( (0, 21) ) ), "more than 20 staff would write past the record" );
	}

	[TestMethod]
	public void ATrainedMemberTakesTheFirstMatchingSlotOnOwnedLandAndLeavesItForTraining()
	{
		var rides = new ThemeParkIncSpecialRides();
		rides.Add( 10, SkillSet( (9, 1) ) );
		rides.Add( 11, SkillSet( (9, 1), (4, 1) ) );
		var unowned = new HashSet<int> { 10 };
		// [BIN:TPI-EXE:0x623FB0 assign] the first record on owned land, the staff type and the ability compared
		var mechanic = Member( 201, ThemeParkIncStaffRole.Mechanic, 9 );
		Assert.IsNull( rides.Assign( mechanic, unowned.Contains ), "ability 9 is the guard's slot type 3, not the mechanic's 1" );
		var gardener = Member( 202, ThemeParkIncStaffRole.Gardener, 10 );
		Assert.AreEqual( 11, rides.Assign( gardener, unowned.Contains ), "the record on unowned land is skipped" );
		Assert.AreEqual( (ushort)202, rides.Find( 11 )!.Slots[1].Thing, "the slots go by ability: Tunnelling (set 4) before Water/Drainage (set 9)" );
		Assert.AreEqual( (ushort)0, rides.Find( 10 )!.Slots[0].Thing );
		var tunneller = Member( 203, ThemeParkIncStaffRole.Mechanic, 5 );
		Assert.AreEqual( 11, rides.Assign( tunneller, _ => false ) );
		Assert.AreEqual( (ushort)203, rides.Find( 11 )!.Slots[0].Thing );
		var second = Member( 204, ThemeParkIncStaffRole.Gardener, 10 );
		Assert.AreEqual( 10, rides.Assign( second, _ => false ), "with the land owned the first record's slot is free" );
		// [BIN:TPI-EXE:0x6241B0 unassign] the first slot holding him in a record on owned land
		Assert.IsTrue( rides.Release( gardener, _ => false ) );
		Assert.AreEqual( (ushort)0, rides.Find( 11 )!.Slots[1].Thing );
		Assert.IsFalse( rides.Release( gardener, _ => false ), "he holds no slot any more" );
		Assert.IsFalse( rides.Release( tunneller, instance => instance == 11 ), "a record on unowned land is not looked at" );
		Assert.IsTrue( rides.Release( tunneller, _ => false ) );
	}

	[TestMethod]
	public void TheActivateButtonNeedsTheRecordTheTicketsAndEveryUsedSlotAndTheActivationEmptiesTheRecord()
	{
		var rides = new ThemeParkIncSpecialRides();
		rides.Add( 5, SkillSet( (9, 1), (11, 2) ) );
		// [BIN:TPI-EXE:0x6243C0 can activate]
		Assert.IsFalse( rides.CanActivate( 6, 9, 1 ), "no record" );
		Assert.IsFalse( rides.CanActivate( 5, 9, 1 ), "three empty slots" );
		rides.Assign( Member( 1, ThemeParkIncStaffRole.Gardener, 10 ), _ => false );
		rides.Assign( Member( 2, ThemeParkIncStaffRole.Gardener, 12 ), _ => false );
		Assert.IsFalse( rides.CanActivate( 5, 9, 1 ), "one landscaping slot is still empty" );
		rides.Assign( Member( 3, ThemeParkIncStaffRole.Gardener, 12 ), _ => false );
		Assert.IsTrue( rides.CanActivate( 5, 1, 1 ) );
		Assert.IsFalse( rides.CanActivate( 5, 0, 1 ), "the career holds fewer golden tickets than the cost" );
		// [BIN:TPI-EXE:0x624330 abilities known]
		var discovered = new byte[ThemeParkIncCareer.AbilityCount];
		discovered[10] = 1;
		Assert.IsFalse( rides.AbilitiesDiscovered( 5, discovered ) );
		discovered[12] = 1;
		Assert.IsTrue( rides.AbilitiesDiscovered( 5, discovered ) );
		Assert.IsTrue( rides.AbilitiesDiscovered( 99, new byte[ThemeParkIncCareer.AbilityCount] ), "a ride without a record answers true" );
		// [BIN:TPI-EXE:0x5E8910 activated flags] [BIN:TPI-EXE:0x624280 record emptied]
		rides.Activate( 5 );
		Assert.IsTrue( rides.IsActivated( 5 ) && rides.IsFree( 5 ) );
		Assert.AreEqual( 0, rides.Records.Single().Instance, "the record stays in the table with no ride" );
		Assert.AreEqual( (ushort)1, rides.Records.Single().Slots[0].Thing, "its slots keep their members" );
		Assert.IsNull( rides.Assign( Member( 9, ThemeParkIncStaffRole.Gardener, 12 ), _ => false ), "an emptied record takes no one" );
		Assert.IsFalse( rides.Release( Member( 1, ThemeParkIncStaffRole.Gardener, 10 ), _ => false ), "nor lets anyone go" );
	}

	// ---- the trigger, the values and the tests -------------------------------------------------

	[TestMethod]
	public void TheSpecialRideTriggerReadsTheActivatedFlagOrTheLabsProject()
	{
		// [BIN:TPI-EXE:0x5C3828 special ride trigger]
		foreach ( var type in new[] { 17, 48, 50, 51, 52, 53, 55, 56, 57, 58 } )
		{
			var slot = Slot( type, targetObj: WaterRide );
			var host = new Host();
			Assert.IsFalse( ThemeParkIncChallenges.Trigger( slot, host ), $"type {type}: no ride and no project" );
			host.Researching.Add( WaterRide );
			Assert.IsTrue( ThemeParkIncChallenges.Trigger( slot, host ), $"type {type}: the lab researches the target" );
			host.Targets[WaterRide] = new ThemeParkIncChallengeTarget( true, true, false, 100 );
			Assert.IsFalse( ThemeParkIncChallenges.Trigger( slot, host ), $"type {type}: a placed ride decides, not the lab" );
			host.Targets[WaterRide] = new ThemeParkIncChallengeTarget( true, true, true, 100 );
			Assert.IsTrue( ThemeParkIncChallenges.Trigger( slot, host ), $"type {type}: the activated ride" );
			host.Targets[WaterRide] = new ThemeParkIncChallengeTarget( true, false, true, 100 );
			Assert.IsFalse( ThemeParkIncChallenges.Trigger( slot, host ), $"type {type}: a target that is not special reads +0x12D, which is gated" );
		}
		// [BIN:TPI-EXE:0x5C3927 breakdown trigger] type 46 compares the analyser's breakdown count with TriggerData (signed)
		var review = Slot( 46, trigger: 0 );
		Assert.IsTrue( ThemeParkIncChallenges.Trigger( review, new Host() ) );
		Assert.IsFalse( ThemeParkIncChallenges.Trigger( Slot( 46, trigger: 1 ), new Host() ) );
		Assert.IsTrue( ThemeParkIncChallenges.Trigger( Slot( 46, trigger: -1 ), new Host { BreakdownsEver = -1 } ) );
	}

	[TestMethod]
	public void TheValuesOfTheWorstRepairTheTargetsRepairAndTheGuestsInThePark()
	{
		var host = new Host { LeastRepair = 7, GuestsInPark = 40 };
		host.Targets[WaterRide] = new ThemeParkIncChallengeTarget( true, true, true, 63 );
		// [BIN:TPI-EXE:0x5C50EC worst repair value]
		var review = Slot( 46, targetVal: 10 );
		ThemeParkIncChallenges.UpdateCurrentValue( review, host );
		Assert.AreEqual( (7, 0), (review.Value, review.Value2) );
		// [BIN:TPI-EXE:0x5C5176 target repair value] the second value only; the riders count by events
		var bell = Slot( 52, targetObj: WaterRide, targetVal: 25, targetVal2: 10 );
		bell.Value = 3;
		ThemeParkIncChallenges.UpdateCurrentValue( bell, host );
		Assert.AreEqual( (3, 63), (bell.Value, bell.Value2) );
		var missing = Slot( 52, targetObj: TourRide );
		missing.Value2 = 11;
		ThemeParkIncChallenges.UpdateCurrentValue( missing, host );
		Assert.AreEqual( 11, missing.Value2, "no target thing leaves the values alone" );
		// [BIN:TPI-EXE:0x5C51D3 guests value]
		var swing = Slot( 58, targetObj: WaterRide, targetVal: 10 );
		swing.Value = 5;
		ThemeParkIncChallenges.UpdateCurrentValue( swing, host );
		Assert.AreEqual( (5, 40), (swing.Value, swing.Value2) );
		// [BIN:TPI-EXE:0x5C52C3 no poll] type 17 polls nothing
		var riders = Slot( 17, targetObj: WaterRide, targetVal: 20 );
		riders.Value = 2;
		ThemeParkIncChallenges.UpdateCurrentValue( riders, host );
		Assert.AreEqual( (2, 0), (riders.Value, riders.Value2) );
	}

	[TestMethod]
	[DataRow( 17, 20, 0, 0, 19, 0, false, false, false, DisplayName = "17: 19 riders of 20" )]
	[DataRow( 17, 20, 0, 0, 20, 0, true, true, false, DisplayName = "17: 20 riders win at once" )]
	[DataRow( 46, 10, 0, 1, 10, 0, false, true, false, DisplayName = "46: repair 10 wins at the end only" )]
	[DataRow( 46, 10, 0, 1, 9, 0, false, false, true, DisplayName = "46: repair 9 fails" )]
	[DataRow( 52, 25, 10, 1, 25, 10, false, true, false, DisplayName = "52: 25 riders and repair 10 win at the end" )]
	[DataRow( 52, 25, 10, 1, 24, 10, false, false, false, DisplayName = "52: 24 riders lose at the end without failing" )]
	[DataRow( 52, 25, 10, 1, 30, 9, false, false, true, DisplayName = "52: repair 9 fails" )]
	[DataRow( 58, 10, 0, 0, 4, 40, true, true, false, DisplayName = "58: 4 riders of 40 guests is 10 %" )]
	[DataRow( 58, 10, 0, 0, 3, 40, false, false, false, DisplayName = "58: 3 of 40 is 7 % (idiv)" )]
	[DataRow( 58, 10, 0, 0, 9, 0, false, false, false, DisplayName = "58: no guests compares 0" )]
	public void TheWinAndFailureTestsOfThePortedTypes( int type, int targetVal, int targetVal2, int atEnd, int value, int value2, bool earlyWin, bool endWin, bool failed )
	{
		// [BIN:TPI-EXE:0x5C3F60 win test] [BIN:TPI-EXE:0x5C3DB0 failure test]
		var slot = Slot( type, targetObj: WaterRide, targetVal: targetVal, targetVal2: targetVal2, checkAtEnd: atEnd );
		slot.Value = value;
		slot.Value2 = value2;
		Assert.AreEqual( earlyWin, slot.DidThisWinIt( atEnd: false, false ), "DidThisWinIt(0)" );
		Assert.AreEqual( endWin, slot.DidThisWinIt( atEnd: true, false ), "DidThisWinIt(1)" );
		Assert.AreEqual( failed, slot.HasFailed(), "HasFailed" );
	}

	// ---- the economy ---------------------------------------------------------------------------

	/// <summary>A Water Ride built in the Science economy (a headless economy builds any type; the catalogue hides the special rides from the player).</summary>
	private static ParkObjectState Build( ParkEconomy economy, int type )
	{
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( type, out var built ) );
		return built!;
	}

	[TestMethod]
	public void ASpecialRideAwaitsItsActivationWhichSpendsItsGoldenTicketAndNeedsItsStaff()
	{
		var economy = ScienceEconomy();
		var rides = economy.TpiSpecialRides!;
		var career = economy.TpiCareer!;
		var swing = Build( economy, RobotSwing );
		var water = Build( economy, WaterRide );
		Assert.IsFalse( economy.IsThemeParkIncSpecialRidePending( swing.Id ), "an ordinary ride has no record" );
		Assert.IsTrue( economy.IsThemeParkIncSpecialRidePending( water.Id ) );
		CollectionAssert.AreEqual( new[] { new ThemeParkIncSpecialRideStaffSlot( 5, 10, 0 ) }, rides.Find( water.Id )!.Slots.Where( slot => slot.StaffType != 7 ).ToArray(),
			"SkillSet[9].Num 1: a gardener in Water/Drainage" );
		Assert.AreEqual( 1, economy.ThemeParkIncActivationCost( water.Id ), "UsageInfo.GoldenTicketCost 1" );
		Assert.IsFalse( economy.CanActivateThemeParkIncSpecialRide( water.Id ), "no staff and no golden ticket" );
		Assert.ThrowsException<InvalidOperationException>( () => economy.ActivateThemeParkIncSpecialRide( water.Id ) );
		Assert.IsFalse( economy.ThemeParkIncActivationAbilitiesDiscovered( water.Id ), "a new career has discovered abilities 1, 7 and 9 only" );
		rides.Assign( Member( 300, ThemeParkIncStaffRole.Gardener, 10 ), _ => false );
		Assert.IsFalse( economy.CanActivateThemeParkIncSpecialRide( water.Id ), "the staff are there, the ticket is not" );
		career.AddGoldenTickets( 2 );
		Assert.IsTrue( economy.CanActivateThemeParkIncSpecialRide( water.Id ) );
		economy.ActivateThemeParkIncSpecialRide( water.Id );
		Assert.AreEqual( 1, career.GoldenTickets, "the activation spends the ride's one ticket" );
		Assert.IsTrue( rides.IsActivated( water.Id ) );
		Assert.IsFalse( economy.IsThemeParkIncSpecialRidePending( water.Id ) );
		Assert.IsFalse( economy.CanActivateThemeParkIncSpecialRide( water.Id ), "an activated ride has no record" );
		Assert.IsNull( new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( Array.Empty<EconomyObjectInfo>() ), ParkGameMode.FullSimulation, 7 ).TpiSpecialRides,
			"a Theme Park World park has no special rides" );
	}

	[TestMethod]
	public void TheTriggerOfTheScienceRidersChallengeFollowsTheLabUntilTheRideStandsAndThenItsActivation()
	{
		var economy = ScienceEconomy();
		var park = OriginalPark.Load( "science" );
		economy.SeedThemeParkIncResearch( park.Save!.ThemeParkIncWorld!.ResearchLab!, park.Save.ThemeParkIncWorld.ObjectControls );
		var research = economy.TpiResearch!;
		void Projects( int slot0, int slot1 )
		{
			var state = research.Capture();
			var projects = (ThemeParkIncLabProject[])state.Projects.Clone();
			projects[0] = projects[0] with { ObjectId = WaterRide };
			projects[1] = projects[1] with { ObjectId = TourRide };
			research.Restore( state with { Projects = projects, Count = Math.Max( 2, state.Count ), SlotProjects = new[] { slot0, slot1 } } );
		}
		var challenges = economy.TpiChallenges!;
		var riders = challenges.Slots[7];
		Assert.AreEqual( (17, (ushort)WaterRide), (riders.Type, riders.TargetObj), "the Science level's row 8" );
		var turn = ThemeParkIncTestTurns.AtDays( 2 );
		// [BIN:TPI-EXE:0x61E6A0 lab project] slot 0 decides while it holds a project
		Projects( -1, -1 );
		ThemeParkIncChallenges.CalcAvailTime( riders, turn, EconomyHost( economy ) );
		Assert.IsFalse( riders.OfferTimeSet, "no project" );
		Projects( 1, 0 );
		ThemeParkIncChallenges.CalcAvailTime( riders, turn, EconomyHost( economy ) );
		Assert.IsFalse( riders.OfferTimeSet, "slot 0 researches the Tour Ride, so slot 1 is not asked" );
		Projects( -1, 0 );
		ThemeParkIncChallenges.CalcAvailTime( riders, turn, EconomyHost( economy ) );
		Assert.IsTrue( riders.OfferTimeSet, "an empty slot 0 lets slot 1's Water Ride project answer" );
		// the ride placed: its activation decides, the lab no longer does
		var built = ScienceEconomy();
		built.SeedThemeParkIncResearch( park.Save.ThemeParkIncWorld.ResearchLab!, park.Save.ThemeParkIncWorld.ObjectControls );
		var slot = built.TpiChallenges!.Slots[7];
		var water = Build( built, WaterRide );
		Assert.IsFalse( ThemeParkIncChallenges.Trigger( slot, EconomyHost( built ) ) );
		built.TpiSpecialRides!.Activate( water.Id );
		Assert.IsTrue( ThemeParkIncChallenges.Trigger( slot, EconomyHost( built ) ) );
	}

	/// <summary>The economy's own challenge host (the private class behind <see cref="ParkEconomy.TpiChallenges"/>'s updates).</summary>
	private static IThemeParkIncChallengeHost EconomyHost( ParkEconomy economy ) =>
		(IThemeParkIncChallengeHost)typeof( ParkEconomy ).GetField( "tpiChallengeHost", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.GetValue( economy )!;

	[TestMethod]
	public void TheWorstRepairReadsTheRidesOnly()
	{
		var economy = ScienceEconomy();
		var host = EconomyHost( economy );
		Assert.AreEqual( 100, host.LeastRideRepair(), "no ride" );
		var first = Build( economy, RobotSwing );
		var second = Build( economy, RobotSwing );
		var toilet = Build( economy, 3417 );
		first.Repair = 81.9;
		second.Repair = 37.99;
		toilet.Repair = 2;
		// [BIN:TPI-EXE:0x5B7D30 worst repair] Info.WhichUIType 0 only, the byte truncated
		Assert.AreEqual( 37, host.LeastRideRepair() );
		Assert.AreEqual( 0, host.BreakdownsEver );
	}

	[TestMethod]
	public void AThemeParkIncRideAdmissionCountsForTypes17_52_58AndTheFirstValueOf57()
	{
		var economy = ScienceEconomy();
		var water = Build( economy, WaterRide );
		var challenges = economy.TpiChallenges!;
		void Run( int index, int type, int target )
		{
			var slot = challenges.Slots[index];
			slot.Type = type;
			slot.TargetObj = (ushort)target;
			slot.Status = ThemeParkIncChallengeStatus.Running;
			slot.Value = slot.Value2 = 0;
		}
		Run( 0, 17, WaterRide );
		Run( 1, 52, WaterRide );
		Run( 2, 58, WaterRide );
		Run( 3, 57, WaterRide );
		Run( 4, 17, TourRide );
		// [BIN:TPI-EXE:0x61A867 admission events]
		economy.RecordRideUse( water.Id );
		economy.RecordRideUse( water.Id );
		CollectionAssert.AreEqual( new[] { 2, 2, 2, 2, 0 }, challenges.Slots.Take( 5 ).Select( slot => slot.Value ).ToArray() );
		Assert.IsTrue( challenges.Slots.Take( 5 ).All( slot => slot.Value2 == 0 ), "the booth's second value is not reached" );
	}

	[TestMethod]
	public void TheActivatedWaterRideOffersItsRidersChallengeAndTheWinBringsItsShares()
	{
		var economy = ScienceEconomy();
		var career = economy.TpiCareer!;
		var challenges = economy.TpiChallenges!;
		economy.OpenPark();
		var water = Build( economy, WaterRide );
		economy.TpiSpecialRides!.Assign( Member( 300, ThemeParkIncStaffRole.Gardener, 10 ), _ => false );
		career.AddGoldenTickets( 1 );
		economy.ActivateThemeParkIncSpecialRide( water.Id );
		// row 8: MinDaysTilTrigger 0 (the day count must pass it), DaysDelay 2, TargetVal 20, TargetTime 30, SharePrize 600, SharePriceEffectWon 4
		economy.Advance( ParkCalendar.TickOfTurn( ThemeParkIncTestTurns.AtDays( 4 ) ) - economy.Tick );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Offered, challenges.Slots[7].Status );
		economy.AcceptThemeParkIncChallenge( 7 );
		var owned = career.Shares.Owned;
		var price = career.Shares.Price;
		for ( var rider = 0; rider < 20; rider++ )
			economy.RecordRideUse( water.Id );
		economy.Advance( ParkCalendar.TickOfTurn( economy.Turn + 1 ) - economy.Tick );
		Assert.AreEqual( ThemeParkIncChallengeStatus.Won, challenges.Slots[7].Status );
		// [BIN:TPI-EXE:0x5C4344 share prize] [BIN:TPI-EXE:0x5C4352 won price]
		Assert.AreEqual( owned + 600, career.Shares.Owned, "the SharePrize reaches the shares game" );
		Assert.AreEqual( price + price * 4 / 100, career.Shares.Price );
	}

	// ---- the save ------------------------------------------------------------------------------

	[TestMethod]
	public void TheSpecialRidesSurviveTheSaveAndAnOlderOrAThemeParkWorldSaveIsRefused()
	{
		var economy = ScienceEconomy();
		var water = Build( economy, WaterRide );
		var mill = Build( economy, LogMill );
		var rides = economy.TpiSpecialRides!;
		rides.Assign( Member( 300, ThemeParkIncStaffRole.Gardener, 10 ), _ => false );
		rides.Activate( water.Id );
		rides.Assign( Member( 301, ThemeParkIncStaffRole.Mechanic, 5 ), _ => false );
		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"TpiSpecialRides\"" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ), "the reloaded park saves the same" );
		Assert.IsTrue( restored.TpiSpecialRides!.IsActivated( water.Id ) && !restored.IsThemeParkIncSpecialRidePending( water.Id ) );
		Assert.AreEqual( (ushort)301, restored.TpiSpecialRides.Find( mill.Id )!.Slots[0].Thing );
		// a Theme Park Inc save from before the special rides is refused with the start-again message
		var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		node.Remove( "TpiSpecialRides" );
		var older = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( node.ToJsonString() ) ), economy.Settings, economy.Catalog ) );
		Assert.AreEqual( ParkSaveFile.SpecialRidesMissing, older.Message );
		Assert.IsFalse( ParkSaveFile.Serialize( new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( Array.Empty<EconomyObjectInfo>() ), ParkGameMode.FullSimulation, 7 ) )
			.Contains( "TpiSpecialRides", StringComparison.Ordinal ), "a Theme Park World save writes none" );
		var saved = rides.Capture();
		Assert.IsTrue( ThemeParkIncSpecialRides.IsValid( saved ) );
		Assert.IsFalse( ThemeParkIncSpecialRides.IsValid( saved with { Records = saved.Records.Concat( saved.Records ).Concat( saved.Records ).ToArray() } ), "more than four records" );
		Assert.IsFalse( ThemeParkIncSpecialRides.IsValid( saved with { Activated = new[] { water.Id, water.Id } } ) );
		Assert.IsFalse( ThemeParkIncSpecialRides.IsValid( saved with { Records = new[] { saved.Records[1], saved.Records[1] } } ), "a ride named twice" );
	}

	[TestMethod]
	public void AThemeParkWorldSaveCarryingSpecialRidesIsRefused()
	{
		var economy = new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( Array.Empty<EconomyObjectInfo>() ), ParkGameMode.FullSimulation, 7 );
		var node = System.Text.Json.Nodes.JsonNode.Parse( ParkSaveFile.Serialize( economy ) )!.AsObject();
		node["TpiSpecialRides"] = System.Text.Json.Nodes.JsonNode.Parse( "{\"Records\":[],\"Activated\":[]}" );
		var refused = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( node.ToJsonString() ) ), economy.Settings, economy.Catalog ) );
		StringAssert.Contains( refused.Message, "Theme Park Inc state" );
	}

	// ---- the prebuilt Science park ---------------------------------------------------------------

	[TestMethod]
	public void ThePrebuiltSciencePark4SpecialRidesAwaitTheirActivationWithTheirSavedSlots()
	{
		var economy = ScienceEconomy();
		var park = OriginalPark.Load( "science" );
		var world = park.Save!.ThemeParkIncWorld!;
		Assert.AreEqual( 0u, world.ParkAnalyser!.NumBreakdownsEver, "the analyser's mNumBreakdownsEver, never raised in play" );
		var catalog = ObjectCatalog.Load( "science" );
		var instances = new Dictionary<int, int>();
		foreach ( var record in park.Save.ObjectsInRecordOrder )
			if ( catalog.Find( record.InfoId ) is { IsTool: false } && economy.Catalog.TryGet( record.InfoId, out _ ) )
				instances[park.Save.FindThemeParkIncAttraction( record )!.Id] = economy.RegisterExisting( record.InfoId ).Id;
		Level.ImportThemeParkIncSpecialRides( economy, world, instances );
		var rides = economy.TpiSpecialRides!;
		int TypeOf( int instance ) => economy.TryGetObject( instance, out var item ) ? item.InfoId : 0;
		CollectionAssert.AreEqual( new[] { LogMill, WaterRide, TourRide, 1110 }, rides.Records.Select( record => TypeOf( record.Instance ) ).ToArray() );
		Assert.AreEqual( 0, rides.Activated.Count, "no special ride of the prebuilt park is activated" );
		// the saved slots are the records the builder makes from each type's SkillSet (the files' last values)
		foreach ( var record in rides.Records )
		{
			var fresh = new ThemeParkIncSpecialRides();
			fresh.Add( record.Instance, ((EconomyObjectCatalog)economy.Catalog)[TypeOf( record.Instance )].SkillSet! );
			CollectionAssert.AreEqual( fresh.Records[0].Slots, record.Slots, $"type {TypeOf( record.Instance )}" );
			Assert.AreEqual( 1, economy.ThemeParkIncActivationCost( record.Instance ) );
		}
	}
}

/// <summary>Turns of the Theme Park Inc clock.</summary>
internal static class ThemeParkIncTestTurns
{
	/// <summary>The first park turn whose clock is at least <paramref name="days"/> game days after turn 0 (one day is 23.04 turns).</summary>
	public static long AtDays( double days ) => (long)Math.Ceiling( days * ThemeParkIncChallenges.Day / (15000 / 4 * 10_000_000.0) );
}
