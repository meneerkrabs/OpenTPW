using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's entertainer and guard (docs/reverse/TPI-STAFF.md, "Entertainer" and "Guard"): the pre-updates' cell effects (0x5D1960, 0x5D3B40 with the writer 0x5D7770), the entertainer's search
/// 0x5D1A30 and setter 0x5D1D30, the performing state's end, the guard's decision 0x5D3C10, the hire and the save. The tests run the retail Science economy and need OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncPerformerTests
{
	private BaseFileSystem? previous;
	private Logger? previousLog;

	[TestInitialize]
	public void Initialize() => previous = FileSystem;

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous!;
		if ( previousLog != null )
			Log = previousLog;
		previousLog = null;
		GameEditions.Select( GameEdition.ThemeParkWorld );
	}

	private void UseRetailData()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		previousLog ??= Log;
		Log = new();
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
	}

	private ParkEconomy ScienceEconomy()
	{
		UseRetailData();
		return ParkEconomyRuntime.ForThemeParkIncLevel( OriginalPark.Load( "science" ) ).Economy;
	}

	/// <summary>A path row along y = 5 from x = 0 to 11.</summary>
	private static ThemeParkIncStaffWorld World( ParkEconomy economy )
	{
		var grid = new GuestPathGrid( 16, 16 );
		for ( var x = 0; x < 12; x++ )
			grid.SetPath( x, 5, true );
		economy.Litter.Cells = grid.Cells;
		economy.Litter.ThemeParkInc = true;
		return new( grid );
	}

	/// <summary>Hires the first pooled member of <paramref name="role"/> and drops him on <paramref name="cell"/>.</summary>
	/// <summary>The world generator's state when the last <see cref="Hire"/> began its drop.</summary>
	private static uint stateBeforeDrop;

	private static ThemeParkIncStaffMember Hire( ParkEconomy economy, ThemeParkIncStaffWorld world, ThemeParkIncStaffRole role, (int X, int Y) cell, int? experience = null )
	{
		var staff = economy.TpiStaff!;
		var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).FirstOrDefault( index => staff.Slots[index]?.Role == role, -1 );
		if ( slot < 0 )
			Assert.Inconclusive( $"The Science pool holds no {role}." );
		Assert.IsTrue( staff.BeginHire( slot ) );
		var member = staff.EndHire( slot, placed: true )!;
		if ( experience is { } value )
			member.Experience = value;
		stateBeforeDrop = economy.World.State;
		world.Construct( economy, member, cell, null );
		return member;
	}

	private static short[] Words( ParkEconomy economy ) =>
		Enumerable.Range( 0, 16 * 16 ).SelectMany( index => { var (h, i, g, s, a) = economy.Litter.Environment.At( index % 16, index / 16 ); return new[] { h, i, g, s, a }; } ).ToArray();

	/// <summary>The words the grid holds on the cells of a 16 × 16 map after a source of <paramref name="row"/> went down at (x, y) with <paramref name="radius"/>, from an empty grid.</summary>
	private static short[] Expected( ParkEconomy economy, int row, (int X, int Y) cell, int radius )
	{
		var environment = new ParkEnvironment { Effects = economy.Litter.Environment.Effects };
		environment.ApplyRadius( row, cell.X, cell.Y, add: true, radius );
		return Enumerable.Range( 0, 16 * 16 ).SelectMany( index => { var (h, i, g, s, a) = environment.At( index % 16, index / 16 ); return new[] { h, i, g, s, a }; } ).ToArray();
	}

	// ---- Data ---------------------------------------------------------------------------------------

	[TestMethod]
	public void TheEntertainersBankLoadsAndTheirSettingsAreTheShippedOnes()
	{
		// [BIN:TPI-EXE:0x5D13E7 entertainer sprite] category 8: esprites.wad's Science\Entertainers (Spr_bu, Spr_sp); the guard's category 0xB is Generic\Guards (Spr_gu)
		UseRetailData();
		var sprites = StaffSpriteSet.Load( "science" );
		Assert.AreEqual( 2, sprites.BankCount( StaffType.Entertainer ), "Spr_bu and Spr_sp" );
		Assert.IsTrue( sprites.BankCount( StaffType.Guard ) >= 1 );
		Assert.IsTrue( sprites.AnimationsOf( StaffType.Entertainer, 0 )!.PerformanceCount >= 1 );
		// Theme Park World's levels read the three entertainers of their own themes, and the Theme Park Inc folders are not Theme Park World's
		GameEditions.Select( GameEdition.ThemeParkWorld );
		Assert.AreEqual( 0, StaffSpriteSet.Load( "science" ).BankCount( StaffType.Entertainer ) );
		GameEditions.Select( GameEdition.ThemeParkInc );
		var settings = ThemeParkIncStaffSettings.Read( BalanceSettings.LoadThemeParkInc( "science" ).Standard )!;
		var entertainer = settings.Roles[ThemeParkIncStaffRole.Entertainer];
		var guard = settings.Roles[ThemeParkIncStaffRole.Guard];
		// data\levels\Standard.sam: Entertainer.MinWorkDuration 10, MaxWorkDuration 75, MinActiveRange 2, MaxActiveRange 5 (0xA022E4..0xA022F0); Guard 10, 75, MinDetectionRange 5, MaxDetectionRange 10 (0xA0230C..0xA02318)
		Assert.AreEqual( (10, 75, 2, 5), (entertainer.MinWorkDuration, entertainer.MaxWorkDuration, entertainer.MinRange, entertainer.MaxRange) );
		Assert.AreEqual( (10, 75, 5, 10), (guard.MinWorkDuration, guard.MaxWorkDuration, guard.MinRange, guard.MaxRange) );
		var rows = ScienceEconomy().Litter.Environment.Effects;
		Assert.AreEqual( 2, rows[RegionEffect.Entertainer].Happiness, "RegionFX[0] cheers the cells" );
		Assert.AreEqual( 10, rows[RegionEffect.Guard].Security, "RegionFX[3] is the security" );
	}

	[TestMethod]
	public void TheThemeIndexIsThePositionInTheThemeTable()
	{
		// [BIN:TPI-EXE:0x9D53B4 theme names] "arabian", "science", "water", "wIntro", "Unknown"
		Assert.AreEqual( 0, ThemeParkIncStaffWorld.ThemeIndexOf( "arabian" ) );
		Assert.AreEqual( 1, ThemeParkIncStaffWorld.ThemeIndexOf( "Science" ) );
		Assert.AreEqual( 2, ThemeParkIncStaffWorld.ThemeIndexOf( "water" ) );
		Assert.AreEqual( 3, ThemeParkIncStaffWorld.ThemeIndexOf( "wintro" ) );
		Assert.AreEqual( ThemeParkIncStaffWorld.UnknownTheme, ThemeParkIncStaffWorld.ThemeIndexOf( "jungle" ) );
	}

	[TestMethod]
	public void ThePerformanceTemplateFollowsTheDrawTheThemeAndTheVariant()
	{
		// [BIN:TPI-EXE:0x5D1D83 performance template] R % n == 0: 0x13 unless the theme index and the variant are both 0 (0x14); else 0x15 for theme 2 with variant 1, 0x16 for the rest
		Assert.AreEqual( 0x14, ThemeParkIncStaffWorld.PerformanceTemplateOf( 0, 0, 0 ) );
		Assert.AreEqual( 0x13, ThemeParkIncStaffWorld.PerformanceTemplateOf( 0, 1, 0 ) );
		Assert.AreEqual( 0x13, ThemeParkIncStaffWorld.PerformanceTemplateOf( 0, 0, 1 ) );
		Assert.AreEqual( 0x15, ThemeParkIncStaffWorld.PerformanceTemplateOf( 1, 2, 1 ) );
		Assert.AreEqual( 0x16, ThemeParkIncStaffWorld.PerformanceTemplateOf( 1, 2, 0 ) );
		Assert.AreEqual( 0x16, ThemeParkIncStaffWorld.PerformanceTemplateOf( 3, 1, 1 ) );
		// the four templates are the programs of the table 0x9CDA94: slots 4 and 0xA
		foreach ( var template in new[] { 0x13, 0x14, 0x15, 0x16 } )
			Assert.IsTrue( StaffSpritePrograms.IsEntry( (StaffSpriteProgram)ThemeParkIncStaffWorld.ProgramOf( template ) ) );
	}

	// ---- Hire and the cell effect -------------------------------------------------------------------

	[TestMethod]
	public void AnEntertainerAndAGuardAreHiredAndPutTheirRowsDownOnTheirCell()
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		var entertainer = Hire( economy, world, ThemeParkIncStaffRole.Entertainer, (3, 5), experience: 0 );
		Assert.IsTrue( entertainer.Placed );
		Assert.AreEqual( ThemeParkIncStaffWorld.CellWord( 3, 5 ), entertainer.EffectCell );
		// Entertainer.MinActiveRange 2 at no experience (0x62C900 of 0x5D1404)
		Assert.AreEqual( 2, entertainer.EffectRadius );
		CollectionAssert.AreEqual( Expected( economy, RegionEffect.Entertainer, (3, 5), 2 ), Words( economy ) );
		var guard = Hire( economy, world, ThemeParkIncStaffRole.Guard, (8, 5), experience: 0 );
		Assert.AreEqual( 5, guard.EffectRadius, "Guard.MinDetectionRange" );
		Assert.AreEqual( 10, economy.Litter.Environment.At( 8, 5 ).Security, "the security row, 10 on the guard's own cell" );
		Assert.AreEqual( 5, economy.Litter.Environment.At( 8, 6 ).Security, "10 / (1 + 1) a cell away" );
		Assert.AreEqual( 2, economy.Litter.Environment.At( 3, 5 ).Happiness, "the entertainer's row 0 has Happiness 2 at its centre" );
		Assert.AreEqual( ThemeParkIncStaffWorld.Performing, 0x17 );
	}

	[TestMethod]
	public void TheEffectMovesWithTheCellAndKeepsWhatAChangedRadiusLeaves()
	{
		// the guard's row 3 (Security 10) reaches nine cells out at the largest radius (10 / (d + 1) is 1 up to d = 9); the entertainer's row 0 (Happiness 2) is 0 beyond one cell
		var economy = ScienceEconomy();
		var world = World( economy );
		var member = Hire( economy, world, ThemeParkIncStaffRole.Guard, (3, 5), experience: 0 );
		var turn = economy.Turn;
		// he stands on the next cell when the pre-update runs: the row comes off (3, 5) and goes down on (4, 5), with the radius of the moment
		member.Navigation!.X += 1 << 16;
		world.Update( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.CellWord( 4, 5 ), member.EffectCell );
		CollectionAssert.AreEqual( Expected( economy, RegionEffect.Guard, (4, 5), 5 ), Words( economy ), "no residue while the radius stays" );
		Assert.AreEqual( 0, economy.Litter.Environment.ResidueCells().Count() );
		// his experience grew: the old cell's row comes off with the larger radius, which the grid keeps as the difference
		member.Experience = economy.TpiStaff!.Settings.MaxExperience / 2;
		member.Navigation!.X += 1 << 16;
		world.Update( economy, member, ++turn );
		var radius = ThemeParkIncStaffSettings.Lerp( member.Experience, 0, economy.TpiStaff.Settings.MaxExperience, 5, 10 );
		Assert.IsTrue( radius > 5 );
		Assert.AreEqual( radius, member.EffectRadius );
		var residue = economy.Litter.Environment.ResidueCells().ToList();
		Assert.IsTrue( residue.Count > 0, "the removal's larger radius left its difference" );
		// the original's grid: the removal ran with the new radius over the old cell's smaller add
		var original = new ParkEnvironment { Effects = economy.Litter.Environment.Effects };
		original.ApplyRadius( RegionEffect.Guard, 3, 5, add: true, 5 );
		original.ApplyRadius( RegionEffect.Guard, 4, 5, add: true, 5 );
		original.ApplyRadius( RegionEffect.Guard, 3, 5, add: false, 5 );
		original.ApplyRadius( RegionEffect.Guard, 4, 5, add: false, radius );
		original.ApplyRadius( RegionEffect.Guard, 5, 5, add: true, radius );
		// (the second move's add and removal at the same radius: the cell 4 add had the radius 5)
		for ( var y = 0; y < 16; y++ )
			for ( var x = 0; x < 16; x++ )
				Assert.AreEqual( original.At( x, y ), economy.Litter.Environment.At( x, y ), $"cell ({x}, {y}) as the original's writer leaves it" );
		// a rebuild of the source from its cell and radius, plus the residue a save keeps, reads as the live grid
		var rebuilt = new ParkEnvironment { Effects = economy.Litter.Environment.Effects };
		rebuilt.ApplyRadius( RegionEffect.Guard, 5, 5, add: true, radius );
		rebuilt.RestoreResidue( residue );
		for ( var y = 0; y < 16; y++ )
			for ( var x = 0; x < 16; x++ )
				Assert.AreEqual( economy.Litter.Environment.At( x, y ), rebuilt.At( x, y ), $"cell ({x}, {y})" );
	}

	// ---- The state machines -------------------------------------------------------------------------

	[TestMethod]
	public void TheSearchDrawsOnceForTheStateAndStartsAPerformanceWithAGuestInRange()
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		world.GuestFinder = ( _, _ ) => true;
		var member = Hire( economy, world, ThemeParkIncStaffRole.Entertainer, (3, 5), experience: 0 );
		var turn = economy.Turn;
		var performed = false;
		for ( var step = 0; step < 4000 && !performed; step++ )
		{
			world.Update( economy, member, ++turn );
			performed = member.State == ThemeParkIncStaffWorld.Performing;
			if ( !performed )
				member.IdleStamp = 0;
		}
		Assert.IsTrue( performed, "a draw divisible by 3 with a guest in range starts a performance" );
		Assert.IsTrue( member.Template is >= 0x13 and <= 0x16 );
		Assert.AreEqual( turn, member.WorkStamp, "+0x5D4 = the world turn" );
		// the performance runs the work duration, then the sound 0x87 plays and the search runs
		var duration = economy.TpiStaff!.Settings.WorkDuration( ThemeParkIncStaffRole.Entertainer, member.Experience, member.Motivation, member.Salary );
		var started = turn;
		world.Voices.Clear();
		for ( var step = 0; step < duration; step++ )
		{
			world.Update( economy, member, ++turn );
			Assert.AreEqual( ThemeParkIncStaffWorld.Performing, member.State, $"performing at {turn - started} of {duration}" );
		}
		Assert.IsFalse( world.Voices.Any( voice => voice.Event == ThemeParkIncStaffWorld.PerformanceEndSound ) );
		for ( var step = 0; step < 6 && member.State == ThemeParkIncStaffWorld.Performing && !world.Voices.Any( voice => voice.Event == ThemeParkIncStaffWorld.PerformanceEndSound ); step++ )
			world.Update( economy, member, ++turn );
		Assert.IsTrue( world.Voices.Any( voice => voice.Event == ThemeParkIncStaffWorld.PerformanceEndSound ), "the performance's end plays sound 0x87" );
	}

	[TestMethod]
	public void WithoutAGuestInRangeTheSearchIdlesEveryFourthTurnAndPatrolsOtherwise()
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		world.GuestFinder = ( _, _ ) => false;
		var member = Hire( economy, world, ThemeParkIncStaffRole.Entertainer, (3, 5), experience: 0 );
		// the constructor searched at the turn 0: (turn & 3) == 0 sets idle
		Assert.AreEqual( ThemeParkIncStaffWorld.Idle, member.State );
		// a happiness at most 97 takes no draw in the gate (0x62BDxx draws only above 97)
		member.Happiness = 50;
		var before = economy.World.State;
		var probe = new WorldRng( before );
		// the setter draws once whatever the state: a search at a turn divisible by 4 takes the draw of the % 3 test and the setter's
		world.Search( economy, member, 8 );
		Assert.AreEqual( ThemeParkIncStaffWorld.Idle, member.State );
		_ = probe.NextWord();
		_ = probe.NextWord();
		Assert.AreEqual( probe.State, economy.World.State, "the % 3 draw and the setter's draw, and nothing else" );
		// another turn walks when a destination is found
		var walked = false;
		for ( var turn = 9; turn < 40 && !walked; turn++ )
		{
			if ( (turn & 3) == 0 )
				continue;
			world.Search( economy, member, turn );
			walked = member.State == ThemeParkIncStaffWorld.Walking;
		}
		Assert.IsTrue( walked );
	}

	[TestMethod]
	public void ATiredEntertainerDoesNotPerformAndTakesNoDrawForIt()
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		world.GuestFinder = ( _, _ ) => true;
		var member = Hire( economy, world, ThemeParkIncStaffRole.Entertainer, (3, 5), experience: 0 );
		member.Energy = 0;
		member.Happiness = 100;
		var probe = new WorldRng( economy.World.State );
		// the gate (0x62BBA0) of a tired member draws for the rest branch (a draw whose low nibble 0 costs happiness) and sets him idle; the search returns, so the % 3 test never draws
		member.State = ThemeParkIncStaffWorld.Performing;
		world.Search( economy, member, 7 );
		_ = probe.NextWord();
		Assert.AreEqual( probe.State, economy.World.State, "one draw, the gate's, and none for the % 3 test" );
		Assert.AreNotEqual( ThemeParkIncStaffWorld.Performing, member.State );
	}

	[TestMethod]
	public void AGuardDropDrawsTheWorldForHisVariantAndAnEntertainerDoesNot()
	{
		// [BIN:TPI-EXE:0x5D3253 guard variant] 0x6902F0(0xB) draws the world once after the person start's base-speed draw; the entertainer's constructor keeps the candidate's byte
		var economy = ScienceEconomy();
		var world = World( economy );
		var guard = Hire( economy, world, ThemeParkIncStaffRole.Guard, (3, 5), experience: 0 );
		var probe = new WorldRng( stateBeforeDrop );
		_ = probe.NextWord();
		var variantDraw = probe.NextWord();
		Assert.AreEqual( variantDraw, guard.SpriteDraw, "the guard's variant comes from his own draw" );
		_ = probe.NextWord();
		// base speed, variant, the gate's draw (happiness above 97); the decision at turn 0 idles without a patrol draw
		Assert.AreEqual( probe.State, economy.World.State, "three draws in a guard's drop" );

		var entertainer = Hire( economy, world, ThemeParkIncStaffRole.Entertainer, (6, 5), experience: 0 );
		var candidateDraw = entertainer.SpriteDraw;
		var after = new WorldRng( stateBeforeDrop );
		for ( var draw = 0; draw < 4; draw++ )
			_ = after.NextWord();
		// base speed, the gate's draw, the % 3 draw and the setter's draw (no guests: the finder is not asked)
		Assert.AreEqual( candidateDraw, entertainer.SpriteDraw );
		Assert.AreEqual( after.State, economy.World.State, "four draws in an entertainer's drop, none for his variant" );
	}

	[TestMethod]
	public void AGuardPatrolsOnThreeTurnsInFourAndIdlesOnTheFourth()
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		var member = Hire( economy, world, ThemeParkIncStaffRole.Guard, (3, 5), experience: 0 );
		// the constructor's decision at turn 0 ((turn & 3) == 0): idle
		Assert.AreEqual( ThemeParkIncStaffWorld.Idle, member.State );
		world.Search( economy, member, 8 );
		Assert.AreEqual( ThemeParkIncStaffWorld.Idle, member.State );
		var walked = false;
		for ( var turn = 9; turn < 60 && !walked; turn++ )
		{
			if ( (turn & 3) == 0 )
				continue;
			world.Search( economy, member, turn );
			walked = member.State == ThemeParkIncStaffWorld.Walking;
		}
		Assert.IsTrue( walked, "a turn whose low bits are not 0 and a patrol destination set the patrol walk" );
		Assert.AreEqual( 0, member.SpeedBonus, "the guard's setter clears the speed bonus (0x60B3F0(0))" );
		// arrived or stuck decides again; the walk step costs energy and happiness each tick while walking
		var energy = member.Energy;
		var turns = economy.Turn;
		for ( var step = 0; step < 6 && member.State == ThemeParkIncStaffWorld.Walking; step++ )
			world.Update( economy, member, ++turns );
		Assert.IsTrue( member.Energy <= energy );
	}

	[TestMethod]
	public void ARestingOrChasingStateIsNotAPortedGuardState()
	{
		// the chase, carry, after-release and first-aid states need the guests' Theme Park Inc states (docs/reverse/TPI-STAFF.md): a save holding one is refused
		foreach ( var state in new[] { 2, 3, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E } )
			Assert.IsFalse( ThemeParkIncStaffMember.IsPortedState( ThemeParkIncStaffRole.Guard, state ), $"guard state {state:X}" );
		foreach ( var state in new[] { 0, 1, ThemeParkIncTraining.ToTrainingRoom, ThemeParkIncTraining.Training } )
			Assert.IsTrue( ThemeParkIncStaffMember.IsPortedState( ThemeParkIncStaffRole.Guard, state ) );
		Assert.IsTrue( ThemeParkIncStaffMember.IsPortedState( ThemeParkIncStaffRole.Entertainer, ThemeParkIncStaffWorld.Performing ) );
		Assert.IsFalse( ThemeParkIncStaffMember.IsPortedState( ThemeParkIncStaffRole.Entertainer, ThemeParkIncTraining.Training ), "an entertainer learns nothing" );
	}

	// ---- The save -----------------------------------------------------------------------------------

	[TestMethod]
	public void APerformingEntertainerAndAGuardSurviveASaveAndAnOlderSaveIsRefused()
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		world.GuestFinder = ( _, _ ) => true;
		var entertainer = Hire( economy, world, ThemeParkIncStaffRole.Entertainer, (3, 5), experience: 0 );
		var guard = Hire( economy, world, ThemeParkIncStaffRole.Guard, (8, 5), experience: 0 );
		var turn = economy.Turn;
		for ( var step = 0; step < 4000 && entertainer.State != ThemeParkIncStaffWorld.Performing; step++ )
		{
			world.Update( economy, entertainer, ++turn );
			world.Update( economy, guard, turn );
		}
		Assert.AreEqual( ThemeParkIncStaffWorld.Performing, entertainer.State );
		// the guard's experience grew since his effect went down and he moved, so the grid holds a residue the save must keep
		guard.Experience = economy.TpiStaff!.Settings.MaxExperience / 2;
		for ( var step = 0; step < 200 && economy.Litter.Environment.ResidueCells().Count() == 0; step++ )
		{
			guard.Navigation!.X += 1 << 16;
			if ( guard.Navigation.Cell.X > 14 )
				guard.Navigation.X = (1 << 16) + 0x8000;
			world.Update( economy, guard, ++turn );
		}
		Assert.IsTrue( economy.Litter.Environment.ResidueCells().Any(), "a guard whose radius grew left a residue" );
		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"TpiPerformers\"" );
		StringAssert.Contains( json, "\"EffectCell\"" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ), "the save round-trips exactly" );
		var restoredMember = restored.TpiStaff!.Members.Single( item => item.Role == ThemeParkIncStaffRole.Entertainer );
		Assert.AreEqual( (entertainer.State, entertainer.WorkStamp, entertainer.EffectCell, entertainer.EffectRadius), (restoredMember.State, restoredMember.WorkStamp, restoredMember.EffectCell, restoredMember.EffectRadius) );
		CollectionAssert.AreEqual( Words( economy ), Words( restored ), "the grid after the load is the grid that was saved, residue included" );
		var restoredWorld = World( restored );
		restoredWorld.GuestFinder = ( _, _ ) => true;
		var restoredGuard = restored.TpiStaff.Members.Single( item => item.Role == ThemeParkIncStaffRole.Guard );
		for ( var step = 0; step < 80; step++ )
		{
			turn++;
			world.Update( economy, entertainer, turn );
			world.Update( economy, guard, turn );
			restoredWorld.Update( restored, restoredMember, turn );
			restoredWorld.Update( restored, restoredGuard, turn );
		}
		Assert.AreEqual( ParkSaveFile.Serialize( economy ), ParkSaveFile.Serialize( restored ), "both go on alike" );

		// a Science save from before the entertainers and the guards is refused
		var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		node.Remove( "TpiPerformers" );
		var older = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( node.ToJsonString() ) ), economy.Settings, economy.Catalog ) );
		Assert.AreEqual( ParkSaveFile.ThemeParkIncPerformersMissing, older.Message );
		var saved = economy.TpiStaff.Capture();
		Assert.IsFalse( ThemeParkIncStaff.IsValid( saved with { Members = saved.Members.Select( item => item.Role == ThemeParkIncStaffRole.Guard ? item with { World = item.World! with { State = 0x1A } } : item ).ToArray() } ),
			"a guard in the chase state is refused (not ported)" );
		Assert.IsFalse( ThemeParkIncStaff.IsValid( saved with { Members = saved.Members.Select( item => item.Role == ThemeParkIncStaffRole.Guard ? item with { World = item.World! with { EffectCell = 0 } } : item ).ToArray() } ),
			"a cell effect names a cell of the map" );
		Assert.IsFalse( ThemeParkIncStaff.IsValid( saved with { Members = saved.Members.Select( item => item.Role == ThemeParkIncStaffRole.Entertainer ? item with { World = item.World! with { EffectRadius = null } } : item ).ToArray() } ),
			"an entertainer keeps the radius of his effect" );
		Assert.IsFalse( ThemeParkIncStaff.IsValid( saved with { Members = saved.Members.Select( item => item.Role == ThemeParkIncStaffRole.Guard ? item with { World = item.World! with { PatrolArea = new[] { 1, 1, 9, 9 } } } : item ).ToArray() } ),
			"a guard's patrol area other than the whole map is refused (0x6256A0 is not ported)" );
		Assert.IsFalse( ThemeParkIncStaff.IsValid( saved with { Members = saved.Members.Select( item => item.Role == ThemeParkIncStaffRole.Guard ? item with { World = item.World! with { PlantId = 5 } } : item ).ToArray() } ),
			"a guard holds no plant" );
	}

	[TestMethod]
	public void AWorldSaveFromBeforeTheEntertainersIsRefusedBeforeItsAssetsAreLookedAt()
	{
		var envelope = ParkResumeSaveFileTests.Envelope();
		var economy = ScienceEconomy();
		var current = envelope with { Theme = "science", Economy = ParkSaveFile.Capture( economy ) };
		var node = System.Text.Json.Nodes.JsonNode.Parse( ParkResumeJson.Serialize( current ) )!.AsObject();
		node["Economy"]!.AsObject().Remove( "TpiPerformers" );
		node["AssetHashes"] = new System.Text.Json.Nodes.JsonObject();
		var refusal = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( node.ToJsonString() ) ) );
		Assert.AreEqual( ParkSaveFile.ThemeParkIncPerformersMissing, refusal.Message );
	}
}
