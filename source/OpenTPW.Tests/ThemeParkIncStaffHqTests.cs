using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Theme Park Inc's staff HQ obstacle slots (docs/reverse/TPI-STAFF.md §12, ThemeParkIncStaffHeadquarters): the table, assign, release, rebuild and MetRequirements.</summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncStaffHqTests : ThemeParkIncScienceTestBase
{
	private const string TwoRequirements = "Obstacle.Type 1703\nCompletion.EndingTheme 1\n"
		+ "ObstacleStaff[0].StaffType 5\nObstacleStaff[0].Ability 10\nObstacleStaff[0].Number 1\n"
		+ "ObstacleStaff[1].StaffType 0\nObstacleStaff[1].Ability 8\nObstacleStaff[1].Number 2\n";

	private static ThemeParkIncCareer Career( string miss00 )
	{
		var files = new TestCareerFiles()
			.With( "/missions/Miss00.sam", miss00 )
			.With( "/missions/Miss01.sam", "Info.Number 1\n" )
			.With( "/levels/arabian/global.sam", "ParkInfo.AvailableFromStart 0\n" )
			.With( "/levels/science/global.sam", "ParkInfo.AvailableFromStart 1\n" )
			.With( "/levels/water/global.sam", "ParkInfo.AvailableFromStart 0\n" );
		var career = new ThemeParkIncCareer( files, TestCareerFiles.ShippedFiringLines );
		career.ChangeTheme( "science" );
		return career;
	}

	private static ThemeParkIncStaffMember Member( ThemeParkIncStaffRole role, int thing, int ability ) =>
		new( thing, role, 0, 20000, 20, 0, 0 ) { ThingId = thing, Ability = ability };

	[TestMethod]
	public void TheTableAppendsEachRequirementsNumberThenPadsWithType7()
	{
		// [BIN:TPI-EXE:0x634600 slot table]
		var hq = new ThemeParkIncStaffHeadquarters();
		Assert.IsTrue( hq.Slots.All( slot => slot == new ThemeParkIncHqSlot( 7, 0, 0 ) ) && hq.AllFilled, "a new HQ asks for nothing" );
		hq.BuildSlots( Career( TwoRequirements ).Mission );
		CollectionAssert.AreEqual( new[] { new ThemeParkIncHqSlot( 5, 10, 0 ), new ThemeParkIncHqSlot( 0, 8, 0 ), new ThemeParkIncHqSlot( 0, 8, 0 ) }, hq.Slots.Take( 3 ).ToArray() );
		Assert.IsTrue( hq.Slots.Skip( 3 ).All( slot => slot == new ThemeParkIncHqSlot( 7, 0, 0 ) ) );
		Assert.IsFalse( hq.AllFilled );
		var many = string.Concat( Enumerable.Range( 0, 3 ).Select( index => $"ObstacleStaff[{index}].StaffType 1\nObstacleStaff[{index}].Ability 3\nObstacleStaff[{index}].Number 7\n" ) );
		Assert.ThrowsException<InvalidDataException>( () => hq.BuildSlots( Career( many ).Mission ), "21 slots would write past the table" );
	}

	[TestMethod]
	public void AssignMatchesTheAbilityOnlyAndRefusesASlottedMember()
	{
		// [BIN:TPI-EXE:0x634B00 assign]
		var hq = new ThemeParkIncStaffHeadquarters();
		hq.BuildSlots( Career( TwoRequirements ).Mission );
		var guard = Member( ThemeParkIncStaffRole.Guard, 30, 10 );
		Assert.IsTrue( hq.Assign( guard ), "the staff type is not compared: a guard with ability 10 takes the gardener's slot" );
		Assert.AreEqual( 0, guard.HqSlot );
		Assert.AreEqual( (ushort)30, hq.Slots[0].Thing );
		Assert.IsFalse( hq.Assign( guard ), "a slotted member is refused" );
		var handyman = Member( ThemeParkIncStaffRole.Handyman, 31, 8 );
		Assert.IsTrue( hq.Assign( handyman ) );
		Assert.AreEqual( 1, handyman.HqSlot );
		var untrained = Member( ThemeParkIncStaffRole.Handyman, 32, 0 );
		Assert.IsTrue( hq.Assign( untrained ), "an untrained member fills a padding slot (ability 0)" );
		Assert.AreEqual( 3, untrained.HqSlot );
		Assert.IsFalse( hq.AllFilled, "slot 2 still asks for a handyman with ability 8" );
		Assert.IsTrue( hq.Assign( Member( ThemeParkIncStaffRole.Handyman, 33, 8 ) ) && hq.AllFilled );
	}

	[TestMethod]
	public void ReleaseFreesTheSlotAndAssignsAReplacementOfTheSameRoleAndAbility()
	{
		// [BIN:TPI-EXE:0x634730 release] [BIN:TPI-EXE:0x634A40 handyman finder]
		var hq = new ThemeParkIncStaffHeadquarters();
		hq.BuildSlots( Career( TwoRequirements ).Mission );
		var first = Member( ThemeParkIncStaffRole.Handyman, 40, 8 );
		var second = Member( ThemeParkIncStaffRole.Handyman, 41, 8 );
		var spare = Member( ThemeParkIncStaffRole.Handyman, 42, 8 );
		var other = Member( ThemeParkIncStaffRole.Guard, 43, 8 );
		var members = new[] { other, spare, second, first };
		foreach ( var member in new[] { first, second } )
			hq.TrainingFinished( member );
		Assert.AreEqual( 2, hq.TrainedStaff[8] );
		Assert.AreEqual( (1, 2), (first.HqSlot, second.HqSlot) );

		hq.Release( members, first );
		Assert.AreEqual( 1, hq.TrainedStaff[8], "the count falls" );
		Assert.AreEqual( -1, first.HqSlot );
		Assert.AreEqual( 1, spare.HqSlot, "the first handyman with ability 8 in no slot takes the freed slot; the guard is not a handyman" );
		Assert.AreEqual( (ushort)42, hq.Slots[1].Thing );

		var untrained = Member( ThemeParkIncStaffRole.Handyman, 44, 0 );
		hq.Release( members, untrained );
		Assert.AreEqual( -1, hq.TrainedStaff[0], "ability 0's count falls too, then the release stops" );
		var guard = Member( ThemeParkIncStaffRole.Guard, 45, 10 );
		hq.Assign( guard );
		hq.Release( members, guard );
		Assert.AreEqual( (ushort)45, hq.Slots[0].Thing, "a guard's slot is not cleared (only gardeners, handymen and mechanics)" );
	}

	[TestMethod]
	public void RebuildClearsTheSlottedMembersAndFindsTheRequiredStaffByType()
	{
		// [BIN:TPI-EXE:0x634D20 rebuild]
		var career = Career( TwoRequirements );
		var hq = new ThemeParkIncStaffHeadquarters();
		var old = Member( ThemeParkIncStaffRole.Handyman, 50, 0 );
		hq.Assign( old );
		Assert.AreEqual( 0, old.HqSlot );
		var gardener = Member( ThemeParkIncStaffRole.Gardener, 51, 10 );
		var handymen = new[] { Member( ThemeParkIncStaffRole.Handyman, 52, 8 ), Member( ThemeParkIncStaffRole.Handyman, 53, 8 ) };
		var mechanic = Member( ThemeParkIncStaffRole.Mechanic, 54, 10 );
		var members = new[] { mechanic, handymen[0], old, gardener, handymen[1] };
		hq.Rebuild( members, career );
		Assert.AreEqual( -1, old.HqSlot, "the slotted handyman left his slot" );
		Assert.AreEqual( (0, 1, 2, -1), (gardener.HqSlot, handymen[0].HqSlot, handymen[1].HqSlot, mechanic.HqSlot), "type 5 finds the gardener, type 0 the handymen in list order" );
		Assert.IsTrue( hq.AllFilled );

		var noObstacle = Career( "ObstacleStaff[0].StaffType 0\nObstacleStaff[0].Ability 8\nObstacleStaff[0].Number 1\n" );
		hq.Rebuild( members, noObstacle );
		Assert.AreEqual( new ThemeParkIncHqSlot( 0, 8, 0 ), hq.Slots[0], "without an obstacle the table is built but nobody is found" );
		Assert.IsTrue( members.All( member => member.HqSlot == -1 || member == mechanic ) );
	}

	[TestMethod]
	public void TheStaffSnapshotKeepsTheSlotsTheCountsAndTheMembersTrainingFields()
	{
		var hq = new ThemeParkIncStaffHeadquarters();
		hq.BuildSlots( Career( TwoRequirements ).Mission );
		var member = Member( ThemeParkIncStaffRole.Handyman, 60, 8 );
		hq.TrainingFinished( member );
		var restored = new ThemeParkIncStaffHeadquarters();
		var json = JsonSerializer.Serialize( hq.Capture() );
		restored.Restore( JsonSerializer.Deserialize<ThemeParkIncStaffHqSnapshot>( json )! );
		CollectionAssert.AreEqual( hq.Slots.ToArray(), restored.Slots.ToArray() );
		CollectionAssert.AreEqual( hq.TrainedStaff.ToArray(), restored.TrainedStaff.ToArray() );
		Assert.IsFalse( ThemeParkIncStaffHeadquarters.IsValid( new ThemeParkIncStaffHqSnapshot( new int[59], new int[15] ) ) );

		// a member saved before the training fields reads as untrained and in no slot
		var old = JsonSerializer.Deserialize<ThemeParkIncStaffMemberSnapshot>( """{"Id":1,"Role":0,"NameIndex":0,"SpriteDraw":0,"Experience":1,"Salary":1,"Agency":0,"Happiness":1,"Motivation":1,"Energy":1,"WageShortfall":0,"WageDemandDay":0,"DayCounters":[0,0,0,0,0,0,0,0,0]}""" )!;
		Assert.AreEqual( (0, 0, 0, 0, -1), (old.Ability, old.LearningAbility, old.TrainingProgress, old.ChosenAbility, old.HqSlot) );
	}

	[TestMethod]
	public void TheMissionControllerReadsTheHqSlotsAndMessage0x23RebuildsThem()
	{
		// [BIN:TPI-EXE:0x55A620 met requirements] [BIN:TPI-EXE:0x6325B9 HQ rebuild]
		var economy = ScienceEconomy();
		var staff = economy.TpiStaff!;
		var career = economy.TpiCareer!;
		Assert.IsFalse( career.HasObstacle, "the prebuilt starts mission 0, whose gate needs no research" );
		Assert.IsTrue( career.CompleteMission().Advanced );
		Assert.IsTrue( career.HasObstacle, "mission 1's lake needs research" );
		staff.Hq.BuildSlots( career.Mission );
		Assert.AreEqual( new ThemeParkIncHqSlot( 5, 10, 0 ), staff.Hq.Slots[0], "miss01: one gardener trained in Water/Drainage" );
		Assert.IsFalse( staff.Hq.AllFilled );
		career.DiscoverObstacleAbilities();
		Assert.AreEqual( 2, economy.TpiMissions!.ObstacleState() );
		career.AddGoldenTickets( career.Mission.ObstacleGoldenTickets );
		Assert.IsFalse( economy.ThemeParkIncMissionMet, "the gardener's slot is empty" );
		var gardener = new ThemeParkIncStaffMember( 77, ThemeParkIncStaffRole.Gardener, 0, 20000, 20, 0, 0 ) { ThingId = 900, Ability = 10 };
		staff.Hq.TrainingFinished( gardener );
		Assert.IsTrue( staff.Hq.AllFilled );
		Assert.IsTrue( economy.ThemeParkIncMissionMet, "researched, the slots filled and the tickets: the remove button is enabled" );

		economy.ThemeParkIncObstacleResearched();
		Assert.IsTrue( economy.TpiMissions.ObstacleResearchComplete, "the controller hears 0x23 too" );
		Assert.AreEqual( new ThemeParkIncHqSlot( 5, 10, 0 ), staff.Hq.Slots[0], "the rebuild finds no gardener in the park's staff list" );
	}

	[TestMethod]
	public void ProceedOnAMetObstacleMissionSendsMessage0x24ToTheHqAndTheController()
	{
		// [BIN:TPI-EXE:0x55BD79 complete button] [BIN:TPI-EXE:0x6325B9 HQ rebuild] [BIN:TPI-EXE:0x5F0F96 remove obstacle] the mission window's Proceed reaches both listeners of message 0x24
		var economy = ScienceEconomy();
		var staff = economy.TpiStaff!;
		var career = economy.TpiCareer!;
		Assert.IsTrue( career.CompleteMission().Advanced );
		staff.Hq.BuildSlots( career.Mission );
		career.DiscoverObstacleAbilities();
		career.AddGoldenTickets( career.Mission.ObstacleGoldenTickets );
		var window = economy.TpiMissionWindowWorld( "Player" )!;
		Assert.IsFalse( window.PressComplete(), "the gardener's slot is empty: Proceed does nothing" );
		Assert.AreEqual( ThemeParkIncMissionState.Idle, economy.TpiMissions!.State );
		var gardener = new ThemeParkIncStaffMember( 77, ThemeParkIncStaffRole.Gardener, 0, 20000, 20, 0, 0 ) { ThingId = 900, Ability = 10 };
		staff.Hq.TrainingFinished( gardener );
		Assert.AreEqual( (true, 0), (window.ObstacleStaffSlotsFilled, window.ObstacleResearchPercent), "the window reads the HQ; without a lab there is no Park Expansion project" );
		Assert.IsTrue( window.PressComplete() );
		Assert.AreEqual( ThemeParkIncMissionState.ObstacleRemoved, economy.TpiMissions.State, "the controller heard 0x24" );
		Assert.AreEqual( new ThemeParkIncHqSlot( 5, 10, 0 ), staff.Hq.Slots[0], "and the HQ rebuilt its table: the trained gardener is not in the park's staff" );
	}

	[TestMethod]
	public void TheSciencePrebuiltsHqAsksForNothing()
	{
		// [BIN:TPI-EXE:0x6326B0 staff HQ load] the prebuilt saves twenty {7, 0, 0}
		var economy = ScienceEconomy();
		var save = OriginalPark.Load( "science" ).Save!.ThemeParkIncWorld!;
		economy.TpiStaff!.Hq.Import( save.StaffHq!.StaffSlots, save.StaffHq.TrainedStaff );
		Assert.IsTrue( economy.TpiStaff.Hq.Slots.All( slot => slot == new ThemeParkIncHqSlot( 7, 0, 0 ) ) );
		Assert.IsTrue( economy.TpiStaff.Hq.TrainedStaff.All( count => count == 0 ) );
	}
}
