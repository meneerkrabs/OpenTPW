using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Theme Park Inc's research lab and researchers (docs/reverse/TPI-STAFF.md §12.3): the prebuilt lab, the tick, the cost, the completion messages, the obstacle project and the researcher's states.</summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncResearchTests : ThemeParkIncScienceTestBase
{
	private sealed class TestRooms : IThemeParkIncRooms
	{
		public ThemeParkIncRoom? Research { get; set; }
		public List<(ushort Thing, int Delta)> Occupancy { get; } = new();
		public ThemeParkIncRoom? First( ThemeParkIncRoomKind kind ) => kind == ThemeParkIncRoomKind.Research ? Research : null;
		public void AddOccupancy( ushort thing, int delta ) => Occupancy.Add( (thing, delta) );
	}

	private static ThemeParkIncSaveWorld Prebuilt() => OriginalPark.Load( "science" ).Save!.ThemeParkIncWorld!;

	/// <summary>The Science economy with the prebuilt's lab and controls seeded, as a level seeds them.</summary>
	private (ParkEconomy Economy, ThemeParkIncResearch Research) Seeded()
	{
		var economy = ScienceEconomy();
		var world = Prebuilt();
		economy.SeedThemeParkIncResearch( world.ResearchLab!, world.ObjectControls );
		return (economy, economy.TpiResearch!);
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

	private static ThemeParkIncStaffMember HireResearcher( ParkEconomy economy, ThemeParkIncStaffWorld world, (int X, int Y) cell, int experience )
	{
		var staff = economy.TpiStaff!;
		var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).First( index => staff.Slots[index]?.Role == ThemeParkIncStaffRole.Researcher );
		Assert.IsTrue( staff.BeginHire( slot ) );
		var member = staff.EndHire( slot, placed: true )!;
		member.Experience = experience;
		world.Construct( economy, member, cell, null );
		return member;
	}

	[TestMethod]
	public void ThePrebuiltLabHoldsFiftyProjectsNoSlotAndControlsForEveryListedObject()
	{
		// [BIN:TPI-EXE:0x61BCE0 lab load] [BIN:TPI-EXE:0x57CB66 researched types]
		var (economy, research) = Seeded();
		Assert.AreEqual( 50, research.Count );
		CollectionAssert.AreEqual( new[] { -4, -4 }, research.SlotProjects.ToArray(), "the lab start's -4, which every reader takes for none" );
		Assert.AreEqual( 85, research.WorkLoad );
		Assert.AreEqual( (0.005f, 1f), (research.SpeedScaleDown, research.CostScaleUp) );
		Assert.AreEqual( new ThemeParkIncLabProject( 3, 0, 0, 2, 1105, 4, 0, 300f, 2, 0f ), research.Projects[1] );
		Assert.IsFalse( research.IsResearched( 1105 ), "an open project's ride is not researched" );
		Assert.IsTrue( research.IsResearched( 1400 ) && research.IsResearched( 1404 ), "the research and training rooms cost nothing to research" );
		var world = Prebuilt();
		Assert.IsFalse( world.Attractions.Any( attraction => attraction.InfoId is 1400 or 1404 ), "no prebuilt research or training room: the rooms' order is the player's building order" );
		Assert.IsTrue( research.Projects.Take( research.Count ).Where( project => project.Level > 0 ).All( project => project.CashPerBrainPoint == 1 ),
			"the level projects took 1 after the dropped rand()" );
		Assert.IsTrue( research.Projects.Take( research.Count ).Where( project => project.Level == 0 ).All( project => project.CashPerBrainPoint is 1 or 2 ), "rand() % 2 + 1" );
	}

	[TestMethod]
	public void EverySixthTickTheListedScientistsInARoomEarnBrainPointsThatTheBankPays()
	{
		// [BIN:TPI-EXE:0x61E980 lab tick] [BIN:TPI-EXE:0x61FF80 research advance]
		var (economy, research) = Seeded();
		var settings = economy.TpiStaff!.Settings;
		var scientist = new ThemeParkIncStaffMember( 900, ThemeParkIncStaffRole.Researcher, 0, settings.MaxExperience / 2, 50, 0, 0 ) { ThingId = 900, State = ThemeParkIncResearch.Researching };
		var idle = new ThemeParkIncStaffMember( 901, ThemeParkIncStaffRole.Researcher, 0, settings.MaxExperience, 50, 0, 0 ) { ThingId = 901, State = 0 };
		ThemeParkIncStaffMember? Lookup( ushort id ) => id == 900 ? scientist : id == 901 ? idle : null;
		research.Member = Lookup;
		// project 27: feature 1405, no consultant, 50 points at 1 cash a point
		research.SetProject( 0, 27 );
		Assert.IsTrue( research.SetTeam( 0, new ushort[] { 900, 901 }.Concat( new ushort[18] ).ToArray() ) );
		Assert.IsTrue( scientist.Listed && idle.Listed );
		var work = settings.WorkDuration( ThemeParkIncStaffRole.Researcher, scientist.Experience, scientist.Motivation, scientist.Salary );
		var points = ThemeParkIncResearch.ScientistPoints( work, 85, 0.005f, 0f );
		var debits = new List<int>();
		var done = new List<(int, int)>();
		research.Tick( 5, Lookup, settings, debits.Add, ( type, category ) => done.Add( (type, category) ) );
		Assert.AreEqual( 0, debits.Count, "only every sixth world tick" );
		research.Tick( 6, Lookup, settings, debits.Add, ( type, category ) => done.Add( (type, category) ) );
		Assert.AreEqual( points, research.Projects[27].Done, "the idle scientist adds nothing" );
		CollectionAssert.AreEqual( new[] { (int)(long)(float)(1.0 * points) }, debits );
		var turn = 6L;
		while ( done.Count == 0 )
			research.Tick( turn += 6, Lookup, settings, debits.Add, ( type, category ) => done.Add( (type, category) ) );
		Assert.AreEqual( 50f, research.Projects[27].Done, "clamped to the cost" );
		CollectionAssert.AreEqual( new[] { (1405, 3) }, done, "message 0x11 with the object and its category" );
		Assert.IsTrue( research.IsResearched( 1405 ) );
		Assert.IsFalse( scientist.Listed, "the team is released" );
		Assert.IsTrue( research.Team( 0 ).All( id => id == 0 ) );
		Assert.AreEqual( 27, research.SlotProjects[0], "the slot keeps the finished project" );
		Assert.IsTrue( debits.Take( debits.Count - 1 ).All( cash => cash == (int)(long)(float)(1.0 * points) ), "each advance pays its points' cash, truncated" );

		// a project with a consultant role and no consultant does not advance
		research.SetProject( 1, 1 );
		research.SetTeam( 1, new ushort[] { 900 }.Concat( new ushort[19] ).ToArray() );
		research.Tick( turn += 6, Lookup, settings, debits.Add, ( _, _ ) => { } );
		Assert.AreEqual( 0f, research.Projects[1].Done, "ride 1105 needs a mechanic consultant" );
	}

	[TestMethod]
	public void TheLabTickAndTheResearchStepRoundEveryX87OperationToASingle()
	{
		// [BIN:TPI-EXE:0x6BF33F control word] the game thread runs at 0x7F after DDSCL_FPUSETUP: fimul, fmuls and fadds each round to a single
		Assert.AreEqual( 1.49f, ThemeParkIncResearch.ScientistPoints( 38, 1, 0.005f, 1.3f ), "the single-precision steps" );
		Assert.AreNotEqual( (float)((double)38 * 0.005f + 1.3f), ThemeParkIncResearch.ScientistPoints( 38, 1, 0.005f, 1.3f ), "one rounding at the end would give another sum" );
		// [BIN:TPI-EXE:0x6289BD research step] fidivl then fsubrl, each rounded
		Assert.AreEqual( 0.99593997f, ThemeParkIncStaffMember.SingleOneMinusRatio( 406, 100000 ) );
		Assert.AreNotEqual( (float)(1.0 - 406.0 / 100000), ThemeParkIncStaffMember.SingleOneMinusRatio( 406, 100000 ), "one rounding at the end would give another step" );
		for ( var numerator = 0u; numerator < 3000; numerator += 7 )
			Assert.AreEqual( (float)numerator / 4999f, ThemeParkIncStaffMember.SingleQuotient( numerator, 4999 ), "exact operands: the single division" );
		// operands beyond 2^24 keep one rounding of the exact quotient: 0xFFFFFFFF / 3 = 1431655765 rounds to 1431655808 (the nearest single)
		Assert.AreEqual( 1431655808f, ThemeParkIncStaffMember.SingleQuotient( uint.MaxValue, 3 ) );
		Assert.AreEqual( 0.5f, ThemeParkIncStaffMember.SingleQuotient( 1, 2 ) );
		Assert.AreEqual( -0.25f, ThemeParkIncStaffMember.SingleQuotient( 1, -4 ) );
	}

	[TestMethod]
	public void TheConsultantPickMatchesTheAbilityOrTheRoleButNeverAResearcher()
	{
		// [BIN:TPI-EXE:0x61D970 consultant pick] [BIN:TPI-EXE:0x576FE0 consultant role]
		Assert.AreEqual( ThemeParkIncResearch.NoRole, ThemeParkIncResearch.RoleOf( 0 ) );
		Assert.AreEqual( (int)ThemeParkIncStaffRole.Mechanic, ThemeParkIncResearch.RoleOf( 3 ) );
		Assert.AreEqual( (int)ThemeParkIncStaffRole.Gardener, ThemeParkIncResearch.RoleOf( 1 ) );
		var guard = new ThemeParkIncStaffMember( 1, ThemeParkIncStaffRole.Guard, 0, 1, 1, 0, 0 ) { ThingId = 11, Ability = 1 };
		var mechanic = new ThemeParkIncStaffMember( 2, ThemeParkIncStaffRole.Mechanic, 0, 1, 1, 0, 0 ) { ThingId = 12 };
		var researcher = new ThemeParkIncStaffMember( 3, ThemeParkIncStaffRole.Researcher, 0, 1, 1, 0, 0 ) { ThingId = 13 };
		var members = new[] { guard, mechanic, researcher };
		Assert.AreEqual( (ushort)12, ThemeParkIncResearch.PickConsultant( new ThemeParkIncLabProject( 3, 0, 0, 0, 1, 0, 0, 1, 1, 0 ), members ) );
		Assert.AreEqual( (ushort)11, ThemeParkIncResearch.PickConsultant( new ThemeParkIncLabProject( 3, 1, 0, 0, 1, 0, 0, 1, 1, 0 ), members ), "an ability picks any role" );
		Assert.AreEqual( (ushort)0, ThemeParkIncResearch.PickConsultant( new ThemeParkIncLabProject( 6, 0, 0, 0, 1, 0, 0, 1, 1, 0 ), members ), "the role table has no researcher" );
		Assert.AreEqual( (ushort)0, ThemeParkIncResearch.PickConsultant( new ThemeParkIncLabProject( 0, 5, 0, 0, 1, 0, 0, 1, 1, 0 ), members ), "no role: no consultant" );
	}

	[TestMethod]
	public void TheLakesParkExpansionResearchPostsTheObstacleResearchedMessage()
	{
		// [BIN:TPI-EXE:0x61CBF0 obstacle project] [BIN:TPI-EXE:0x61DCF9 researched message] [BIN:TPI-EXE:0x61FE70 level project]
		var (economy, research) = Seeded();
		var career = economy.TpiCareer!;
		Assert.IsTrue( career.CompleteMission().Advanced );
		Assert.IsTrue( research.RegisterObstacle( career.HasObstacle, career.Mission.ObstacleType, 29, 1, 1, career.Mission.ObstacleResearchEffort, 0 ) );
		Assert.AreEqual( 51, research.Count );
		var project = research.Projects[50];
		Assert.AreEqual( (1703, 5, 100f, 1, 0, 0), ((int)project.ObjectId, project.Category, project.Cost, project.CashPerBrainPoint, project.StaffRqd, project.StaffSA) );
		Assert.AreEqual( 0xCDCDCDCDu, project.Type, "mType is not written: the record keeps the prebuilt's fill" );
		Assert.IsFalse( research.RegisterObstacle( true, 1703, 29, 1, 1, 100, 0 ), "once" );
		Assert.IsFalse( research.RegisterObstacle( true, 1704, 29, 2, 1, 100, 0 ), "state 2" );
		Assert.IsFalse( research.RegisterObstacle( true, 2000, 29, 1, 1, 100, 0 ), "science types end at 1800" );
		Assert.IsFalse( research.RegisterObstacle( true, 1705, 0, 1, 1, 100, 0 ), "no obstacle thing" );
		Assert.AreEqual( 50, research.ObstacleProject() );

		research.Infos = new[] { new ThemeParkIncResearchInfo( 1703, 0, 0, new[] { 50, 0, 0 }, new int[3] ) };
		var scientist = new ThemeParkIncStaffMember( 900, ThemeParkIncStaffRole.Researcher, 0, economy.TpiStaff!.Settings.MaxExperience, 50, 0, 0 ) { ThingId = 900, State = ThemeParkIncResearch.Researching };
		research.Member = id => id == 900 ? scientist : null;
		research.SetProject( 1, 50 );
		research.SetTeam( 1, new ushort[] { 900 }.Concat( new ushort[19] ).ToArray() );
		var researched = new List<int>();
		for ( var turn = 6L; researched.Count == 0; turn += 6 )
			research.Tick( turn, research.Member, economy.TpiStaff.Settings, _ => { }, ( type, category ) =>
			{
				researched.Add( type );
				if ( category == 5 )
					economy.ThemeParkIncObstacleResearched();
				Assert.IsFalse( research.AppendNextLevel( type ), "the lake has no upgrade level to research" );
			} );
		Assert.IsTrue( economy.TpiMissions!.ObstacleResearchComplete, "message 0x23 reached the controller" );
		Assert.AreEqual( new ThemeParkIncHqSlot( 5, 10, 0 ), economy.TpiStaff.Hq.Slots[0], "and the HQ rebuilt miss01's slot" );
	}

	[TestMethod]
	public void CompletingALevelOneUpgradeAddsTheLevelTwoProjectWithoutADraw()
	{
		// [BIN:TPI-EXE:0x61FE70 level project] every shipped Upgrades[n].CashPerBrainPoint is 0: the original calls rand() and drops it (0x61FF1A), the project costs 1 cash a point, no draw is taken
		var (economy, research) = Seeded();
		research.Infos = new[]
		{
			new ThemeParkIncResearchInfo( 1100, 0, 0, new[] { 0, 100, 200 }, new int[3] ),
			new ThemeParkIncResearchInfo( 1106, 0, 0, new[] { 300, 0, 0 }, new int[3] ),
			new ThemeParkIncResearchInfo( 1102, 0, 0, new[] { 300, 50, 0 }, new[] { 0, 3, 0 } )
		};
		// project 0: Bellows 1100's level-1 upgrade, no consultant
		var scientist = new ThemeParkIncStaffMember( 900, ThemeParkIncStaffRole.Researcher, 0, economy.TpiStaff!.Settings.MaxExperience, 50, 0, 0 ) { ThingId = 900, State = ThemeParkIncResearch.Researching };
		research.Member = id => id == 900 ? scientist : null;
		research.SetProject( 1, 0 );
		research.SetTeam( 1, new ushort[] { 900 }.Concat( new ushort[19] ).ToArray() );
		var count = research.Count;
		var state = economy.World.State;
		var appended = false;
		for ( var turn = 6L; !appended; turn += 6 )
			research.Tick( turn, research.Member, economy.TpiStaff.Settings, _ => { }, ( type, _ ) => appended = research.AppendNextLevel( type ) );
		Assert.AreEqual( count + 1, research.Count );
		var level2 = research.Projects[count];
		Assert.AreEqual( (1100, 4, 2, 200f, 1, 0f), ((int)level2.ObjectId, level2.Category, (int)level2.Level, level2.Cost, level2.CashPerBrainPoint, level2.Done) );
		Assert.AreEqual( 1, research.Controls.First( control => control.Id == 1100 ).Level, "level 1 researched" );
		Assert.AreEqual( state, economy.World.State, "no draw from the world generator" );
		Assert.IsFalse( research.AppendNextLevel( 1106 ), "no further level costs research" );
		Assert.IsTrue( research.AppendNextLevel( 1102 ), "a level with its own cash per point keeps it" );
		Assert.AreEqual( 3, research.Projects[count + 1].CashPerBrainPoint );
	}

	[TestMethod]
	public void AResearcherWalksToTheRoomResearchesUntilTiredAndLeaves()
	{
		// [BIN:TPI-EXE:0x61B820 researcher search] [BIN:TPI-EXE:0x62DCB0 walk to room] [BIN:TPI-EXE:0x6289BD research in room]
		var economy = ScienceEconomy();
		var world = new ThemeParkIncStaffWorld( Row( economy ) );
		var rooms = new TestRooms { Research = new ThemeParkIncRoom( 600, (8, 5) ) };
		world.Rooms = rooms;
		var settings = economy.TpiStaff!.Settings;
		var member = HireResearcher( economy, world, (1, 5), experience: settings.MaxExperience / 2 );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToResearchRoom, 600), (member.State, member.ResearchRoom), "the start's finder sends him to the room" );
		var turn = economy.Turn;
		for ( var step = 0; step < 400 && member.State == ThemeParkIncStaffWorld.ToResearchRoom; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncResearch.Researching, member.State );
		Assert.AreEqual( turn, member.ResearchStamp );
		Assert.IsNull( member.Sprite.Source, "inside the room" );
		CollectionAssert.AreEqual( new[] { ((ushort)600, 1) }, rooms.Occupancy );
		Assert.AreEqual( 0, ThemeParkIncStaffWorld.DisplayCategory( member ), "not listed: idle in the staff panel" );
		member.Listed = true;
		Assert.AreEqual( 7, ThemeParkIncStaffWorld.DisplayCategory( member ), "listed: researching" );
		var researchingTicks = member.DayTicks( ThemeParkIncStaffMember.DisplayState.Researching );
		var energy = member.Energy;
		world.Update( economy, member, ++turn );
		var x = ThemeParkIncStaffMember.SingleOneMinusRatio( (uint)member.Experience, settings.MaxExperience );
		Assert.AreEqual( (float)((float)(x * -0.1f) + energy), member.Energy );
		Assert.AreEqual( researchingTicks + 1, member.DayTicks( ThemeParkIncStaffMember.DisplayState.Researching ) );
		for ( var step = 0; step < 20000 && member.State == ThemeParkIncResearch.Researching; step++ )
			world.Update( economy, member, ++turn );
		var energyByte = (int)member.Energy & 0xFF;
		Assert.IsTrue( energyByte <= settings.RestLevel, "he leaves only once tired" );
		Assert.AreEqual( ((ushort)600, -1), rooms.Occupancy[^1] );
		Assert.IsNotNull( member.Sprite.Source );
		// below RestLevel he is too tired to research and idles; at exactly RestLevel the tired test (<) passes and the finder sends him back
		Assert.AreEqual( energyByte < settings.RestLevel ? (0, 0) : (ThemeParkIncStaffWorld.ToResearchRoom, 600), (member.State, member.ResearchRoom) );
	}

	[TestMethod]
	public void ARemovedResearchRoomSendsItsResearcherOutAndClearsTheTeams()
	{
		// [BIN:TPI-EXE:0x61B570 researcher message] [BIN:TPI-EXE:0x61DB90 room removed]
		var (economy, research) = Seeded();
		var world = new ThemeParkIncStaffWorld( Row( economy ) );
		var rooms = new TestRooms { Research = new ThemeParkIncRoom( 600, (3, 5) ) };
		world.Rooms = rooms;
		var member = HireResearcher( economy, world, (1, 5), experience: 30000 );
		research.SetTeam( 0, new[] { (ushort)member.ThingId }.Concat( new ushort[19] ).ToArray() );
		Assert.IsTrue( member.Listed );
		var turn = economy.Turn;
		for ( var step = 0; step < 200 && member.State != ThemeParkIncResearch.Researching; step++ )
			world.Update( economy, member, ++turn );
		world.ResearchRoomRemoved( economy, 600, turn );
		research.ResearchRoomRemoved();
		Assert.AreEqual( (0, 0), (member.State, member.ResearchRoom) );
		Assert.AreEqual( ((ushort)600, -1), rooms.Occupancy[^1] );
		Assert.IsFalse( member.Listed );
		Assert.IsTrue( research.Team( 0 ).All( id => id == 0 ) );
	}

	[TestMethod]
	public void TheLabAndAResearchersFieldsSurviveTheirSnapshots()
	{
		var (economy, research) = Seeded();
		research.SetProject( 0, 27 );
		research.SetConsultant( 0, 77 );
		var snapshot = JsonSerializer.Deserialize<ThemeParkIncResearchSnapshot>( JsonSerializer.Serialize( economy.CaptureThemeParkIncResearch() ) )!;
		Assert.IsTrue( ThemeParkIncResearch.IsValid( snapshot ) );
		economy.RestoreThemeParkIncResearch( snapshot );
		var restored = economy.TpiResearch!;
		Assert.AreNotSame( research, restored );
		Assert.AreEqual( (50, 27, (ushort)77), (restored.Count, restored.SlotProjects[0], restored.Consultants[0]) );
		CollectionAssert.AreEqual( research.Projects.ToArray(), restored.Projects.ToArray() );
		CollectionAssert.AreEqual( research.Controls.ToArray(), restored.Controls.ToArray() );
		economy.RestoreThemeParkIncResearch( null );
		Assert.IsNull( economy.TpiResearch, "a save without a lab keeps none" );

		var world = new ThemeParkIncStaffWorld( Row( economy ) ) { Rooms = new TestRooms { Research = new ThemeParkIncRoom( 600, (8, 5) ) } };
		var member = HireResearcher( economy, world, (1, 5), experience: 30000 );
		member.Listed = true;
		var staff = JsonSerializer.Deserialize<ThemeParkIncStaffSnapshot>( JsonSerializer.Serialize( economy.TpiStaff!.Capture() ) )!;
		Assert.IsTrue( ThemeParkIncStaff.IsValid( staff ) );
		var saved = staff.Members.Single( other => other.Id == member.Id );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToResearchRoom, true, 600), (saved.World!.State, saved.Listed, saved.ResearchRoom) );
	}

	[TestMethod]
	public void TheChooserOffersOpenProjectsWithinTheTechLevelAndNewProjectsTakeAFreeSlot()
	{
		// [BIN:TPI-EXE:0x574D00 project filter] [BIN:TPI-EXE:0x575F98 new project] [BIN:TPI-EXE:0x620210 percent done]
		var (_, research) = Seeded();
		var all = research.Offered( 0, 7, _ => true ).ToList();
		Assert.AreEqual( 50, all.Count, "nothing finished, nothing in a slot" );
		Assert.IsFalse( research.Offered( 0, 7, _ => false ).Any( index => research.Projects[index].Category == 4 ), "a level project needs its object in the park" );
		Assert.IsTrue( research.Offered( 0, 0, _ => true ).All( index => research.Projects[index].Group == 0 ), "groups above the tech level are hidden" );
		Assert.AreEqual( 0, research.StartNewProject() );
		research.SetProject( 0, 27 );
		Assert.IsFalse( research.Offered( 1, 7, _ => true ).Contains( 27 ), "the other slot's project is hidden" );
		Assert.AreEqual( 1, research.StartNewProject() );
		research.SetProject( 1, 28 );
		Assert.AreEqual( -1, research.StartNewProject(), "Must Delete Project!" );
		Assert.AreEqual( 0, research.Percent( 27 ) );
	}
}
