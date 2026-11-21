using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's guests' littering and its handymen in the park (docs/reverse/TPI-STAFF.md §11, ThemeParkIncStaffWorld): the guests' litter step 0x614B71, the litter's sprite offset, and the
/// handyman's states 0, 1, 0x0F and 0x10 with his search, meters, day counters and payday. The handyman tests run the retail Science economy and need OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncHandymanTests
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

	// ---- The guests' litter step (synthetic) ----------------------------------------------

	private static GuestSimulation LitterRow( ParkLitter litter )
	{
		var grid = new GuestPathGrid( 16, 16 );
		for ( var x = 0; x < 12; x++ )
			grid.SetPath( x, 5, true );
		var settings = GuestTests.CreateSettings();
		settings.TpiGuestStart = new( Enumerable.Range( 0, 24 ).Select( i => new ThemeParkIncGuestType( 90 - i, 300 + 20 * i, 40 + i, i / 8 ) ).ToArray(), 400, 30, 15, 70, 20, 10, 100 );
		litter.Cells = grid.Cells;
		litter.ThemeParkInc = true;
		return new GuestSimulation( grid, settings, 3 ) { ArrivalsEnabled = false, Litter = litter };
	}

	private static bool LitterStep( GuestSimulation simulation, Guest guest ) =>
		(bool)typeof( GuestSimulation ).GetMethod( "ThemeParkIncLitterStep", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( simulation, new object[] { guest } )!;

	/// <summary>A generator state whose first draw's low two bits are (or are not) zero.</summary>
	private static uint StateWhoseFirstDraw( bool passes )
	{
		for ( var seed = 1UL; ; seed++ )
		{
			var state = WorldRng.StateFromSeed( seed );
			if ( ((new WorldRng( state ).NextWord() & 3) == 0) == passes )
				return state;
		}
	}

	[TestMethod]
	public void TheLitterStepLetsOnlyOneDrawInFourOnAndKeepsTheUrgeOtherwise()
	{
		// [BIN:TPI-EXE:0x614B84 litter gate] (B & 3) != 0 leaves the block: no litter, no bin search, the urge kept, one draw taken
		var litter = new ParkLitter();
		var simulation = LitterRow( litter );
		simulation.Bins = () => throw new AssertFailedException( "the bin search runs only past the gate" );
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Litter = 95;
		var state = StateWhoseFirstDraw( passes: false );
		simulation.AttachWorld( new WorldRng( state ) );
		var mirror = new WorldRng( state );
		mirror.NextWord();
		Assert.IsFalse( LitterStep( simulation, guest ) );
		Assert.AreEqual( (0, 95f), (litter.Count, guest.Litter) );
		Assert.AreEqual( mirror.State, simulation.World.State, "one draw" );
	}

	[TestMethod]
	public void PastTheGateTheTypeIsDrawnBeforeTheCellIsTestedAndAnOccupiedCellKeepsTheUrge()
	{
		// [BIN:TPI-EXE:0x614C03 ordinary litter] draw C, type C % 5 + 1, then the cell test: a free cell takes the drop (reseed and two draws) and the urge is 0
		var litter = new ParkLitter();
		var simulation = LitterRow( litter );
		simulation.Bins = () => Array.Empty<LitterBin>();
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Litter = 95;
		var state = StateWhoseFirstDraw( passes: true );
		simulation.AttachWorld( new WorldRng( state ) );
		var mirror = new WorldRng( state );
		mirror.NextWord();
		var type = (LitterType)(1 + mirror.NextWord() % ParkLitter.OrdinaryKinds);
		Assert.IsFalse( LitterStep( simulation, guest ), "no bin: the tests go on" );
		Assert.AreEqual( type, litter.TypeAt( 2, 5 ) );
		Assert.AreEqual( 0f, guest.Litter );

		// an occupied cell: draw C is taken, the drop and the urge's reset are skipped (0x614C15..0x614C1B)
		guest.Litter = 95;
		simulation.AttachWorld( new WorldRng( state ) );
		mirror = new WorldRng( state );
		mirror.NextWord();
		mirror.NextWord();
		Assert.IsFalse( LitterStep( simulation, guest ) );
		Assert.AreEqual( (type, 95f), (litter.TypeAt( 2, 5 ), guest.Litter ) );
		Assert.AreEqual( mirror.State, simulation.World.State, "the gate and the type draw, no drop draws" );
		Assert.AreEqual( 1, litter.Count );
	}

	[TestMethod]
	public void ABinInReachTakesTheLitterPastTheGate()
	{
		// [BIN:TPI-EXE:0x614B97 bin search] radius 3: the guest walks to the bin (dispatch 9), the urge kept until it gets there
		var litter = new ParkLitter();
		var simulation = LitterRow( litter );
		simulation.Bins = () => new[] { new LitterBin( 8, (3, 4), (3, 5) ) };
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Litter = 95;
		simulation.AttachWorld( new WorldRng( StateWhoseFirstDraw( passes: true ) ) );
		Assert.IsTrue( LitterStep( simulation, guest ) );
		Assert.AreEqual( (8, 0, 95f), (guest.BinId, litter.Count, guest.Litter) );
	}

	[TestMethod]
	public void ThemeParkIncLitterLiesThreeToSixTenthsIntoTheCellAfterTheSameDraws()
	{
		// [BIN:TPI-EXE:0x5D8E14 litter sprite offset] ((d & 3) - 2) * 0.1 + 0.5 of the cell; the reseed and the two draws are Theme Park World's
		for ( var x = 0; x < 30; x++ )
		{
			var tpi = new WorldRng( 7 );
			var tpw = new WorldRng( 7 );
			var (dx, dz) = ParkLitter.ThemeParkIncSpriteOffset( tpi, x, 2 * x );
			ParkLitter.SpriteOffset( tpw, x, 2 * x );
			Assert.IsTrue( dx is >= 3 and <= 6 && dz is >= 3 and <= 6 );
			Assert.AreEqual( tpw.State, tpi.State, "the same draws" );
		}
		var litter = new ParkLitter { ThemeParkInc = true };
		Assert.AreEqual( ParkLitter.ThemeParkIncSpriteOffset( new WorldRng( 0 ), 4, 9 ), litter.SpriteOffsetOf( 4, 9 ) );
		Assert.AreEqual( (40 + litter.SpriteOffsetOf( 4, 9 ).X, 90 + litter.SpriteOffsetOf( 4, 9 ).Z), LitterRenderer.Anchor( 4, 9, themeParkInc: true ) );
	}

	// ---- The handyman (retail Science economy) ---------------------------------------------

	private ParkEconomy ScienceEconomy()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		previousLog = Log;
		Log = new();
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		return ParkEconomyRuntime.ForThemeParkIncLevel( OriginalPark.Load( "science" ) ).Economy;
	}

	/// <summary>A path row along y = 5 from x = 0 to 11, the economy's litter on its cells.</summary>
	private static GuestPathGrid Row( ParkEconomy economy )
	{
		var grid = new GuestPathGrid( 16, 16 );
		for ( var x = 0; x < 12; x++ )
			grid.SetPath( x, 5, true );
		economy.Litter.Cells = grid.Cells;
		economy.Litter.ThemeParkInc = true;
		return grid;
	}

	/// <summary>Hires the first pooled handyman and drops him on <paramref name="cell"/>.</summary>
	private static ThemeParkIncStaffMember Hire( ParkEconomy economy, ThemeParkIncStaffWorld world, (int X, int Y) cell, int? experience = null )
	{
		var staff = economy.TpiStaff!;
		var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).First( index => staff.Slots[index]?.Role == ThemeParkIncStaffRole.Handyman );
		Assert.IsTrue( staff.BeginHire( slot ) );
		var member = staff.EndHire( slot, placed: true )!;
		if ( experience is int value )
			member.Experience = value;
		world.Construct( economy, member, cell, null );
		return member;
	}

	private static void PlaceLitter( ParkEconomy economy, params (int X, int Y)[] cells ) =>
		economy.Litter.Restore( economy.Litter.Snapshot().Concat( cells.Select( cell => new LitterCell( cell.X, cell.Y, (LitterType)1, 0, 0 ) ) ).ToArray() );

	[TestMethod]
	public void ADroppedHandymanTakesItsThingStartsFullAndSearchesAtOnce()
	{
		var economy = ScienceEconomy();
		var world = new ThemeParkIncStaffWorld( Row( economy ) );
		var settings = economy.TpiStaff!.Settings;
		var member = Hire( economy, world, (2, 5) );
		Assert.AreEqual( 1, member.ThingId, "the world's first thing id without a pool" );
		Assert.AreEqual( ((2 << 16) + 0x8000, (5 << 16) + 0x8000), (member.Navigation!.X, member.Navigation.Y), "the drop puts him in the cell's centre" );
		Assert.AreEqual( (100f, 100f, 100f), (member.Happiness, member.Motivation, member.Energy) );
		Assert.AreEqual( (0, 0, 0x7F, 0x7F), member.PatrolArea );
		// [BIN:TPI-EXE:0x62BD8D comfortable branch] the first search's gate sets the base speed by the full energy: lerp(exp, 0, MaxExperience, 140, MaxSpeedDifference + 140)
		Assert.AreEqual( ThemeParkIncStaffSettings.Lerp( member.Experience, 0, settings.MaxExperience, 140, settings.MaxSpeedDifference + 140 ), member.BaseSpeed );
		Assert.IsTrue( member.State is ThemeParkIncStaffWorld.Walking or ThemeParkIncStaffWorld.Idle, "no litter: a patrol or idle" );
		Assert.AreEqual( member.State == ThemeParkIncStaffWorld.Walking ? ThemeParkIncStaffWorld.WalkTemplate : ThemeParkIncStaffWorld.IdleTemplate, member.Template );
	}

	[TestMethod]
	public void AHandymanClaimsWalksToSweepsAndRemovesLitterThenSearchesAgain()
	{
		var economy = ScienceEconomy();
		var world = new ThemeParkIncStaffWorld( Row( economy ) );
		var settings = economy.TpiStaff!.Settings;
		PlaceLitter( economy, (6, 5) );
		var member = Hire( economy, world, (2, 5), experience: settings.MaxExperience / 2 );
		// [BIN:TPI-EXE:0x5D52A0 handyman search] the litter four cells away is in reach (radius lerp 3..5 at half the experience gives 4): claimed with his thing id, state 0x0F, the bonus 25
		Assert.AreEqual( ThemeParkIncStaffWorld.ToLitter, member.State );
		Assert.AreEqual( (member.ThingId, 25, 5 * 128 + 6 + 1), (economy.Litter.CollectorAt( 6, 5 ), member.SpeedBonus, member.LitterCell) );
		var turn = economy.Turn;
		var energy = member.Energy;
		for ( var step = 0; step < 400 && member.State == ThemeParkIncStaffWorld.ToLitter; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.Sweeping, member.State, "arrived on the litter" );
		Assert.AreEqual( (6, 5), member.Navigation!.Cell );
		Assert.AreEqual( turn, member.WorkStamp, "the sweep's time starts on arrival" );
		Assert.AreEqual( energy, member.Energy, "the walk to litter costs no step (only state 1 and the work do)" );
		var duration = settings.WorkDuration( ThemeParkIncStaffRole.Handyman, member.Experience, member.Motivation, member.Salary );
		for ( var step = 0; step < duration; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.Sweeping, member.State, "still sweeping at exactly the stamp plus the duration" );
		Assert.AreEqual( (LitterType)1, economy.Litter.TypeAt( 6, 5 ) );
		Assert.IsTrue( member.Energy < energy, "each sweeping update costs a working step" );
		world.Update( economy, member, ++turn );
		Assert.AreEqual( LitterType.None, economy.Litter.TypeAt( 6, 5 ), "the litter goes" );
		Assert.AreEqual( 0, member.LitterCell );
		Assert.AreNotEqual( ThemeParkIncStaffWorld.Sweeping, member.State );
		Assert.IsTrue( member.DayTicks( ThemeParkIncStaffMember.DisplayState.Working ) > 0, "the working updates are counted" );
	}

	[TestMethod]
	public void LitterClaimedAwayEndsTheWalkAndAnotherSearchRuns()
	{
		// [BIN:TPI-EXE:0x5D59D0 to litter] "didn't get to litter in time, someone else cleaning": the claim word is no longer his
		var economy = ScienceEconomy();
		var world = new ThemeParkIncStaffWorld( Row( economy ) );
		PlaceLitter( economy, (6, 5) );
		var member = Hire( economy, world, (2, 5), experience: economy.TpiStaff!.Settings.MaxExperience - 1 );
		Assert.AreEqual( ThemeParkIncStaffWorld.ToLitter, member.State );
		economy.Litter.Claim( 6, 5, member.ThingId + 50, economy.Turn );
		world.Update( economy, member, economy.Turn + 1 );
		Assert.AreNotEqual( ThemeParkIncStaffWorld.ToLitter, member.State, "another member's claim is not his to take" );
		Assert.AreEqual( member.ThingId + 50, economy.Litter.CollectorAt( 6, 5 ) );
	}

	[TestMethod]
	public void TheSearchPeriodAndTheIdleWaitShrinkWithExperience()
	{
		// [BIN:TPI-EXE:0x5D5000 handyman pre-update] trunc((1 - exp / MaxExperience) * 50), [BIN:TPI-EXE:0x62CA10 idle end] the same times -50
		Assert.AreEqual( (50, -50), (ThemeParkIncStaffWorld.SearchPeriod( 0, 100000 ), ThemeParkIncStaffWorld.IdleTerm( 0, 100000 )) );
		Assert.AreEqual( (25, -25), (ThemeParkIncStaffWorld.SearchPeriod( 50000, 100000 ), ThemeParkIncStaffWorld.IdleTerm( 50000, 100000 )) );
		Assert.AreEqual( (0, 0), (ThemeParkIncStaffWorld.SearchPeriod( 99999, 100000 ), ThemeParkIncStaffWorld.IdleTerm( 99999, 100000 )) );
	}

	[TestMethod]
	public void TheDisplayCategoriesFollowTheExecutablesTable()
	{
		// [BIN:TPI-EXE:0x62B010 display state]
		var expected = new Dictionary<int, int> { [0] = 0, [1] = 1, [2] = 3, [3] = 3, [4] = 4, [5] = 4, [6] = 2, [7] = 8, [8] = 8, [9] = 2, [0xA] = 5, [0xB] = 6, [0xC] = 6,
			[0xF] = 2, [0x10] = 2, [0x18] = 7, [0x19] = 7, [0x21] = 7, [0x25] = 8 };
		foreach ( var (state, category) in expected )
			Assert.AreEqual( category, ThemeParkIncStaffWorld.Category( state ), $"state {state}" );
	}

	[TestMethod]
	public void ATiredMemberLosesHappinessOnTheGatesDrawAndStopsWorking()
	{
		// [BIN:TPI-EXE:0x62BC88 rest branch] energy byte at most RestLevel: no rest area, a draw whose low nibble 0 costs HappyHitCosNoRestArea, and a working member is set to 0
		var economy = ScienceEconomy();
		var world = new ThemeParkIncStaffWorld( Row( economy ) );
		var settings = economy.TpiStaff!.Settings;
		PlaceLitter( economy, (6, 5) );
		var member = Hire( economy, world, (2, 5), experience: settings.MaxExperience - 1 );
		Assert.AreEqual( ThemeParkIncStaffWorld.ToLitter, member.State );
		member.Energy = settings.RestLevel;
		member.Happiness = 50f;
		var state = economy.World.State;
		var mirror = new WorldRng( state );
		var hit = (mirror.NextWord() & 0xF) == 0;
		world.Search( economy, member, economy.Turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.Idle, member.State );
		Assert.AreEqual( hit ? 50f - settings.HappyHitNoRestArea : 50f, member.Happiness );
		Assert.AreEqual( mirror.State, economy.World.State, "one draw" );
	}

	[TestMethod]
	public void TheBaseSpeedReadsPastTheFiveSpeedWordsByTheEnergysTens()
	{
		// [BIN:TPI-EXE:0x62BDB4 speed by energy] the energy byte 75 reads index 7 of 0x9CFB28: the high word of the float 2.5 that follows the table, 0x4020
		var economy = ScienceEconomy();
		var world = new ThemeParkIncStaffWorld( Row( economy ) );
		var settings = economy.TpiStaff!.Settings;
		var member = Hire( economy, world, (2, 5) );
		member.Energy = 75.9f;
		world.Search( economy, member, economy.Turn );
		Assert.AreEqual( ThemeParkIncStaffSettings.Lerp( member.Experience, 0, settings.MaxExperience, 0x4020, settings.MaxSpeedDifference + 0x4020 ) & 0xFFFF, member.BaseSpeed );
		member.Energy = 55f;
		world.Search( economy, member, economy.Turn );
		Assert.AreEqual( ThemeParkIncStaffSettings.Lerp( member.Experience, 0, settings.MaxExperience, 0, settings.MaxSpeedDifference ), member.BaseSpeed );
	}

	[TestMethod]
	public void TheDayGainsExperienceAndClearsTheCountersAndThePaydayDebitsTheSalary()
	{
		// [BIN:TPI-EXE:0x626624 staff day] [BIN:TPI-EXE:0x6265DE staff payday]
		var economy = ScienceEconomy();
		var world = new ThemeParkIncStaffWorld( Row( economy ) );
		var settings = economy.TpiStaff!.Settings;
		var member = Hire( economy, world, (2, 5), experience: 1000 );
		member.CountTick( ThemeParkIncStaffMember.DisplayState.Working );
		economy.AdvanceDays( 1 );
		Assert.AreEqual( 1000 + settings.Roles[ThemeParkIncStaffRole.Handyman].ExperiencePerMonth + (settings.Agencies[member.Agency].ExperienceGainModifier is var modifier and not 0 ? modifier : 0),
			member.Experience, "a day spent working gains the role's ExperiencePerMonth" );
		Assert.AreEqual( 0u, member.DayTicks( ThemeParkIncStaffMember.DisplayState.Working ), "and the counters start again" );
		var balance = economy.Balance;
		var salary = member.Salary;
		economy.AdvanceMonths( 1 );
		Assert.AreEqual( balance - salary, economy.Balance, "the month's only debit is the member's salary" );
	}

	[TestMethod]
	public void APlacedHandymanSurvivesASaveMidWalkAndGoesOnExactly()
	{
		var economy = ScienceEconomy();
		var world = new ThemeParkIncStaffWorld( Row( economy ) );
		PlaceLitter( economy, (6, 5) );
		var member = Hire( economy, world, (2, 5), experience: economy.TpiStaff!.Settings.MaxExperience / 2 );
		var turn = economy.Turn;
		for ( var step = 0; step < 2; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.ToLitter, member.State, "mid-walk" );
		Assert.IsFalse( member.Navigation!.Arrived );
		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"World\"", "the member's thing is in the save" );
		var directory = Directory.CreateTempSubdirectory( "opentpw-tpi-handyman-" );
		ParkEconomy restored;
		try
		{
			var path = Path.Combine( directory.FullName, "science.json" );
			ParkSaveFile.Save( path, economy );
			restored = ParkSaveFile.Load( path );
		}
		finally
		{
			directory.Delete( recursive: true );
		}
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ), "the save round-trips exactly" );
		var restoredWorld = new ThemeParkIncStaffWorld( Row( restored ) );
		var restoredMember = restored.TpiStaff!.Members.Single();
		for ( var step = 0; step < 60; step++ )
		{
			turn++;
			world.Update( economy, member, turn );
			restoredWorld.Update( restored, restoredMember, turn );
		}
		Assert.AreEqual( ParkSaveFile.Serialize( economy ), ParkSaveFile.Serialize( restored ), "both go on alike" );
		Assert.AreEqual( LitterType.None, restored.Litter.TypeAt( 6, 5 ), "and the walk ended in the sweep" );

		var saved = economy.TpiStaff!.Capture();
		Assert.IsFalse( ThemeParkIncStaff.IsValid( saved with { Members = saved.Members.Select( item => item with { World = item.World! with { State = 0x11 } } ).ToArray() } ),
			"a state this model does not run is refused" );
	}
}
