using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Theme Park Inc's staff training (docs/reverse/TPI-STAFF.md §12.2): eligibility, the walk to the training room, the 300 updates, the reward, the HQ messages and the cancel.</summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncTrainingTests : ThemeParkIncScienceTestBase
{
	/// <summary>A room on the path row with its occupancy deltas recorded.</summary>
	private sealed class TestRooms : IThemeParkIncRooms
	{
		public ThemeParkIncRoom? Training { get; set; }
		public ThemeParkIncRoom? Research { get; set; }
		public List<(ushort Thing, int Delta)> Occupancy { get; } = new();
		public ThemeParkIncRoom? First( ThemeParkIncRoomKind kind ) => kind == ThemeParkIncRoomKind.Training ? Training : Research;
		public void AddOccupancy( ushort thing, int delta ) => Occupancy.Add( (thing, delta) );
	}

	private static GuestPathGrid Row( ParkEconomy economy )
	{
		var grid = new GuestPathGrid( 16, 16 );
		for ( var x = 0; x < 12; x++ )
			grid.SetPath( x, 5, true );
		economy.Litter.Cells = grid.Cells;
		economy.Litter.ThemeParkInc = true;
		return grid;
	}

	private static ThemeParkIncStaffMember Hire( ParkEconomy economy, ThemeParkIncStaffWorld world, (int X, int Y) cell, int experience )
	{
		var staff = economy.TpiStaff!;
		var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).First( index => staff.Slots[index]?.Role == ThemeParkIncStaffRole.Handyman );
		Assert.IsTrue( staff.BeginHire( slot ) );
		var member = staff.EndHire( slot, placed: true )!;
		member.Experience = experience;
		world.Construct( economy, member, cell, null );
		return member;
	}

	[TestMethod]
	public void EachRoleLearnsOnlyItsOwnDiscoveredAbilitiesWithTenThousandExperience()
	{
		// [BIN:TPI-EXE:0x62B6C0 ability compatible] [BIN:TPI-EXE:0x639B20 can learn]
		var expected = new Dictionary<ThemeParkIncStaffRole, int[]>
		{
			[ThemeParkIncStaffRole.Mechanic] = new[] { 1, 2, 3, 4, 5, 6 },
			[ThemeParkIncStaffRole.Handyman] = new[] { 7, 8 },
			[ThemeParkIncStaffRole.Guard] = new[] { 9 },
			[ThemeParkIncStaffRole.Gardener] = new[] { 10, 11, 12, 13, 14 },
			[ThemeParkIncStaffRole.Entertainer] = Array.Empty<int>(),
			[ThemeParkIncStaffRole.Researcher] = Array.Empty<int>()
		};
		foreach ( var (role, abilities) in expected )
			CollectionAssert.AreEqual( abilities, Enumerable.Range( 0, 15 ).Where( ability => ThemeParkIncTraining.Compatible( role, ability ) ).ToArray(), role.ToString() );
		var discovered = new byte[15];
		discovered[1] = discovered[7] = discovered[9] = 1;
		var handyman = new ThemeParkIncStaffMember( 1, ThemeParkIncStaffRole.Handyman, 0, 9999, 20, 0, 0 );
		Assert.IsFalse( ThemeParkIncTraining.CanLearn( handyman, 7, discovered ), "below 10000 experience" );
		handyman.Experience = 10000;
		CollectionAssert.AreEqual( new[] { 7 }, ThemeParkIncTraining.Learnable( handyman, discovered ).ToArray(), "8 is not discovered at the career's reset" );
		Assert.AreEqual( 707, ThemeParkIncTraining.NothingToLearnText( ThemeParkIncStaffRole.Handyman ) );
		Assert.AreEqual( 712, ThemeParkIncTraining.NothingToLearnText( ThemeParkIncStaffRole.Researcher ) );
		Assert.AreEqual( 709, ThemeParkIncTraining.NothingToLearnText( ThemeParkIncStaffRole.Gardener ) );
	}

	[TestMethod]
	public void TrainingTakesThreeHundredUpdatesAndRewardsFiveThousandExperienceAndTheAgencyBonus()
	{
		// [BIN:TPI-EXE:0x62AA50 training tick] [BIN:TPI-EXE:0x62AAD4 training reward]
		var settings = ScienceEconomy().TpiStaff!.Settings;
		Assert.AreEqual( (10, 3000, 5000), (settings.SkillIncrement, settings.ProgressUpperLimit, settings.TrainingExtraExperience) );
		var member = new ThemeParkIncStaffMember( 1, ThemeParkIncStaffRole.Handyman, 0, 20000, 20, 0, 0 ) { ChosenAbility = 7, Motivation = 40f };
		Assert.IsTrue( ThemeParkIncTraining.Enter( member ) );
		Assert.AreEqual( (7, 0), (member.LearningAbility, member.TrainingProgress) );
		var ticks = 0;
		while ( !ThemeParkIncTraining.Tick( member, settings ) )
			ticks++;
		Assert.AreEqual( 299, ticks, "the 300th update finishes" );
		Assert.AreEqual( (7, 0, 0), (member.Ability, member.LearningAbility, member.TrainingProgress) );
		ThemeParkIncTraining.Reward( member, settings );
		var modifier = settings.Agencies[0].TrainingExperienceGainModifier;
		Assert.AreEqual( 25000 + (int)((uint)(25000 * modifier) / 100u), member.Experience );
		Assert.AreEqual( 100f, member.Motivation );
		member.Experience = settings.MaxExperience;
		ThemeParkIncTraining.Reward( member, settings );
		Assert.AreEqual( settings.MaxExperience, member.Experience, "capped at MaxExperience" );

		var poor = new ThemeParkIncStaffMember( 2, ThemeParkIncStaffRole.Handyman, 0, 9000, 20, 0, 0 ) { ChosenAbility = 7, LearningAbility = 8, TrainingProgress = 50 };
		Assert.IsTrue( ThemeParkIncTraining.Enter( poor ) );
		Assert.AreEqual( (8, 50), (poor.LearningAbility, poor.TrainingProgress), "below 10000 the old training stays" );
		Assert.IsFalse( ThemeParkIncTraining.Enter( new ThemeParkIncStaffMember( 3, ThemeParkIncStaffRole.Handyman, 0, 20000, 20, 0, 0 ) ), "nothing chosen" );
	}

	[TestMethod]
	public void AHandymanWalksToTheRoomTrainsThenTheHqCountsAndSlotsHim()
	{
		// [BIN:TPI-EXE:0x628492 state 7 entry] [BIN:TPI-EXE:0x62A7C0 walk to training] [BIN:TPI-EXE:0x62AB90 training finish]
		var economy = ScienceEconomy();
		var world = new ThemeParkIncStaffWorld( Row( economy ) );
		var rooms = new TestRooms { Training = new ThemeParkIncRoom( 500, (8, 5) ) };
		world.Rooms = rooms;
		var member = Hire( economy, world, (1, 5), experience: 20000 );
		var staff = economy.TpiStaff!;
		// a slot asking for Firefighting, which the handyman will fill
		var career = economy.TpiCareer!;
		staff.Hq.Import( Enumerable.Range( 0, 20 ).Select( index => index == 0 ? new byte[] { 0, 0, 0, 0, 7, 0, 0, 0, 0, 0, 0, 0 } : new byte[] { 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 } ).ToArray(),
			new uint[15] );
		Assert.IsFalse( staff.Hq.AllFilled );

		Assert.AreEqual( ThemeParkIncTrainingChoice.Started, world.ChooseAbility( economy, member, 7, confirmed: false, economy.Turn ) );
		Assert.AreEqual( (7, 7, 25), (member.State, member.ChosenAbility, member.SpeedBonus) );
		Assert.AreEqual( 1, ThemeParkIncTraining.Trainees( staff.Members ) );
		var turn = economy.Turn;
		for ( var step = 0; step < 400 && member.State == 7; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( 8, member.State, "arrived in the room" );
		Assert.IsNull( member.Sprite.Source, "inside the room the member has no sprite" );
		CollectionAssert.AreEqual( new[] { ((ushort)500, 1) }, rooms.Occupancy );
		Assert.AreEqual( -1, staff.Hq.TrainedStaff[0], "message 0x2A released his ability 0" );
		Assert.AreEqual( (7, 0), (member.LearningAbility, member.TrainingProgress) );
		var training = turn;
		while ( member.State == 8 )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( 300, turn - training, "300 updates in the room" );
		Assert.AreEqual( 7, member.Ability );
		Assert.AreEqual( 1, staff.Hq.TrainedStaff[7], "message 0x2B counted him" );
		Assert.AreEqual( 0, member.HqSlot, "and slotted him" );
		Assert.IsTrue( staff.Hq.AllFilled );
		Assert.AreEqual( ((ushort)500, -1), rooms.Occupancy[^1] );
		Assert.IsNotNull( member.Sprite.Source, "shown again" );
		Assert.IsTrue( world.TrainingNotices.Single() is { Finished: true, Ability: 7 } );
		Assert.AreEqual( ThemeParkIncTrainingChoice.AlreadyTrained, world.ChooseAbility( economy, member, 7, confirmed: false, turn ) );
		Assert.AreEqual( ThemeParkIncTrainingChoice.NeedsConfirmation, world.ChooseAbility( economy, member, 8, confirmed: false, turn ) );
		Assert.IsTrue( career.Abilities[7] == 1 );
	}

	[TestMethod]
	public void ARemovedTrainingRoomCancelsTheTraineeAndAStuckWalkCostsHappiness()
	{
		// [BIN:TPI-EXE:0x626638 room removed] [BIN:TPI-EXE:0x62E7A0 training cancel]
		var economy = ScienceEconomy();
		var grid = Row( economy );
		var world = new ThemeParkIncStaffWorld( grid );
		var rooms = new TestRooms { Training = new ThemeParkIncRoom( 500, (2, 5) ) };
		world.Rooms = rooms;
		var member = Hire( economy, world, (1, 5), experience: 20000 );
		world.ChooseAbility( economy, member, 7, confirmed: false, economy.Turn );
		var turn = economy.Turn;
		for ( var step = 0; step < 200 && member.State == 7; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( 8, member.State );
		world.TrainingRoomRemoved( economy, turn );
		Assert.AreEqual( 0, member.LearningAbility );
		Assert.AreNotEqual( 8, member.State, "set idle and looked for work" );
		Assert.IsTrue( world.TrainingNotices.Single() is { Finished: false, Ability: 7 } );
		Assert.AreEqual( 0, member.Ability, "nothing learnt" );

		// a room no route reaches, even off the paths (its entrance cell of type 2 blocks every walk mask): the staff route and the cell steer 0x60B7F0 both fail, the walk is stuck and each update
		// costs HappyHitCosRoomInaccessible
		var settings = economy.TpiStaff!.Settings;
		grid.Cells.SetType( 8, 9, 2 );
		rooms.Training = new ThemeParkIncRoom( 501, (8, 9) );
		member.Happiness = 100f;
		world.ChooseAbility( economy, member, 7, confirmed: false, turn );
		Assert.AreEqual( 1, member.Navigation!.Mask, "the cell steer's walk mask" );
		world.Update( economy, member, ++turn );
		world.Update( economy, member, ++turn );
		Assert.AreEqual( 7, member.State, "still walking to the room" );
		Assert.AreEqual( 100f - 2 * settings.HappyHitRoomInaccessible, member.Happiness, 0.001f );
	}

	[TestMethod]
	public void ADemolishedRoomCancelsItsTraineeAtTheClickBeforeTheNextTurnsDraws()
	{
		// [BIN:TPI-EXE:0x637C40 thing destroy] the demolish destroys the room at the click (0x4D30F8 -> 0x660D80 -> 0x666DA0 -> 0x6674E1) and the dispatcher 0x436DD0 delivers message 0xA at once:
		// the cancel's look for work draws from the world generator then, before the next world update's draws; only the thing's free waits for the turn's tail
		(ParkEconomy Economy, ThemeParkIncStaffWorld World, ThemeParkIncStaffMember Member, long Turn) Trainee()
		{
			var economy = ScienceEconomy();
			var world = new ThemeParkIncStaffWorld( Row( economy ) ) { Rooms = new TestRooms { Training = new ThemeParkIncRoom( 500, (2, 5) ) } };
			var member = Hire( economy, world, (1, 5), experience: 20000 );
			world.ChooseAbility( economy, member, 7, confirmed: false, economy.Turn );
			var turn = economy.Turn;
			for ( var step = 0; step < 200 && member.State == 7; step++ )
				world.Update( economy, member, ++turn );
			Assert.AreEqual( 8, member.State );
			return (economy, world, member, turn);
		}
		var atClick = Trainee();
		var before = atClick.Economy.World.State;
		atClick.World.RoomDestroyed( atClick.Economy, ThemeParkIncRoomKind.Training, 500, atClick.Turn );
		Assert.AreNotEqual( before, atClick.Economy.World.State, "the cancel drew at the destroy" );
		Assert.AreEqual( 0, atClick.Member.LearningAbility );
		atClick.World.Update( atClick.Economy, atClick.Member, atClick.Turn + 1 );

		// the same park with the cancel held back to the turn's end draws in another order and ends elsewhere
		var deferred = Trainee();
		Assert.AreEqual( before, deferred.Economy.World.State, "the two parks are alike up to the click" );
		deferred.World.Update( deferred.Economy, deferred.Member, deferred.Turn + 1 );
		deferred.World.RoomDestroyed( deferred.Economy, ThemeParkIncRoomKind.Training, 500, deferred.Turn + 1 );
		Assert.AreNotEqual( atClick.Economy.World.State, deferred.Economy.World.State, "the draw order decides the generator's state" );
	}

	[TestMethod]
	public void AFailedRouteToTheTrainingRoomFallsBackToTheCellSteerOffThePaths()
	{
		// [BIN:TPI-EXE:0x628492 state 7 entry] [BIN:TPI-EXE:0x62EB10 staff route] [BIN:TPI-EXE:0x60B7F0 cell steer]
		var economy = ScienceEconomy();
		var grid = Row( economy );
		// a path cell of its own, two cells below the row: the route along the paths (mask 0) cannot reach it, the cell steer's mask 1 walks over the grass
		grid.SetPath( 8, 7, true );
		var world = new ThemeParkIncStaffWorld( grid );
		var rooms = new TestRooms { Training = new ThemeParkIncRoom( 500, (8, 7) ) };
		world.Rooms = rooms;
		var member = Hire( economy, world, (1, 5), experience: 20000 );
		Assert.AreEqual( 0, member.Navigation!.Mask );
		world.ChooseAbility( economy, member, 7, confirmed: false, economy.Turn );
		Assert.AreEqual( (7, 1, false), (member.State, member.Navigation.Mask, member.Navigation.Stuck), "the second search, with mask 1, found the way" );
		var turn = economy.Turn;
		for ( var step = 0; step < 400 && member.State == 7; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( (8, 0), (member.State, member.Navigation.Mask), "arrived and training; the state's entry puts the walk mask back to 0 (0x62854D)" );

		// a path cell beside the row: the staff route reaches it along the paths with mask 0, and no fallback runs
		var second = Hire( economy, world, (2, 5), experience: 20000 );
		rooms.Training = new ThemeParkIncRoom( 502, (10, 5) );
		world.ChooseAbility( economy, second, 7, confirmed: false, turn );
		Assert.AreEqual( (7, 0), (second.State, second.Navigation!.Mask) );
	}

	[TestMethod]
	public void ATraineesStateAndTrainingFieldsSurviveTheStaffSnapshot()
	{
		var economy = ScienceEconomy();
		var world = new ThemeParkIncStaffWorld( Row( economy ) );
		world.Rooms = new TestRooms { Training = new ThemeParkIncRoom( 500, (2, 5) ) };
		var member = Hire( economy, world, (1, 5), experience: 20000 );
		world.ChooseAbility( economy, member, 7, confirmed: false, economy.Turn );
		var turn = economy.Turn;
		for ( var step = 0; step < 200 && member.State == 7; step++ )
			world.Update( economy, member, ++turn );
		for ( var step = 0; step < 10; step++ )
			world.Update( economy, member, ++turn );
		var snapshot = JsonSerializer.Deserialize<ThemeParkIncStaffSnapshot>( JsonSerializer.Serialize( economy.TpiStaff!.Capture() ) )!;
		Assert.IsTrue( ThemeParkIncStaff.IsValid( snapshot ) );
		var restored = snapshot.Members.Single( saved => saved.Id == member.Id );
		Assert.AreEqual( (8, 7, 7, 100), (restored.World!.State, restored.ChosenAbility, restored.LearningAbility, restored.TrainingProgress) );
		economy.TpiStaff.Restore( snapshot );
		var back = economy.TpiStaff.Members.Single( saved => saved.Id == member.Id );
		Assert.AreEqual( (8, 7, 100, -1), (back.State, back.LearningAbility, back.TrainingProgress, back.HqSlot) );
	}
}
