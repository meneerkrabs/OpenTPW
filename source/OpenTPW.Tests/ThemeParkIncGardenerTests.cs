using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's gardener (docs/reverse/TPI-STAFF.md, "Gardener and plants"): kind 0x14's search 0x5D28C0 and chooser 0x5D2DC0, the walk to the plant 0x5D2C70, the watering 0x5D2ACE and its end
/// 0x5E31B0, the claims, the removal message 0x5D2480, the training of the gardener's abilities 10 to 14 into the staff HQ's slots, and the save. The chooser's reading is run natively by
/// tools/ppc-analysis/oracle_x86/tpi_gardener_oracle.py. The tests run the retail Science economy and need OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncGardenerTests
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
	private static GuestPathGrid Row( ParkEconomy economy )
	{
		var grid = new GuestPathGrid( 16, 16 );
		for ( var x = 0; x < 12; x++ )
			grid.SetPath( x, 5, true );
		economy.Litter.Cells = grid.Cells;
		economy.Litter.ThemeParkInc = true;
		return grid;
	}

	/// <summary>The plants a test builds: their thing cells beside the row (y = 6) and their script variables (vegetation.rse's fifteen).</summary>
	private sealed class Garden
	{
		public Dictionary<int, (int X, int Y)> Cells { get; } = new();
		public Dictionary<int, int[]> Scripts { get; } = new();
	}

	/// <summary>A world over the row whose plants stand where <paramref name="garden"/> puts them (1 × 1, angle 0), their scripts the garden's.</summary>
	private static ThemeParkIncStaffWorld World( ParkEconomy economy, Garden garden )
	{
		economy.ThemeParkIncScriptOf = instance => garden.Scripts.TryGetValue( instance, out var variables ) ? variables : null;
		return new( Row( economy ) )
		{
			ObjectThings = instance => garden.Cells.TryGetValue( instance, out var cell ) ? new ThemeParkIncStaffWorld.DropObject( 3, cell, 0, ObjectShape.Single, 0, 0 ) : null,
			ObjectCells = instance => garden.Cells.TryGetValue( instance, out var cell ) ? (cell, null) : null
		};
	}

	/// <summary>The first plant type (UsageInfo.RequiresWatering) of the Science catalogue.</summary>
	private static int PlantType( ParkEconomy economy ) =>
		economy.Catalog.Objects.First( info => info.RequiresWatering && economy.CheckBuild( info.InfoId ) == ParkEconomy.PurchaseResult.Ok ).InfoId;

	/// <summary>Builds a plant on (<paramref name="x"/>, 6) in condition <paramref name="condition"/>.</summary>
	private static ParkObjectState Plant( ParkEconomy economy, Garden garden, int x, int condition = 0, int dead = 0, int onFire = 0 )
	{
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( PlantType( economy ), out var plant ) );
		garden.Cells[plant!.Id] = (x, 6);
		var variables = new int[15];
		variables[ParkEconomy.PlantCondition] = condition;
		variables[ParkEconomy.PlantOldState] = condition;
		variables[ParkEconomy.PlantDead] = dead;
		variables[ParkEconomy.PlantOnFire] = onFire;
		variables[ParkEconomy.PlantScale] = 1000;
		variables[2] = 400;
		garden.Scripts[plant.Id] = variables;
		return plant;
	}

	/// <summary>Hires the first pooled gardener and drops him on <paramref name="cell"/>.</summary>
	private static ThemeParkIncStaffMember HireGardener( ParkEconomy economy, ThemeParkIncStaffWorld world, (int X, int Y) cell, int? experience = null )
	{
		var staff = economy.TpiStaff!;
		var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).FirstOrDefault( index => staff.Slots[index]?.Role == ThemeParkIncStaffRole.Gardener, -1 );
		if ( slot < 0 )
			Assert.Inconclusive( "The Science pool holds no gardener." );
		Assert.IsTrue( staff.BeginHire( slot ) );
		var member = staff.EndHire( slot, placed: true )!;
		if ( experience is { } value )
			member.Experience = value;
		world.Construct( economy, member, cell, null );
		return member;
	}

	// ---- Data -------------------------------------------------------------------------------------

	[TestMethod]
	public void TheGardenersBankLoadsAndTheirSettingsAreTheShippedOnes()
	{
		// [BIN:TPI-EXE:0x5D228C gardener sprite] category 0x12: esprites.wad's Generic\Gardeners (Spr_gd)
		UseRetailData();
		var sprites = StaffSpriteSet.Load( "science" );
		Assert.AreEqual( 1, sprites.BankCount( StaffSpriteRules.ThemeParkIncGardener ) );
		Assert.AreEqual( 1, StaffSpriteSet.Load( "science", bankLimit: 1 ).BankCount( StaffSpriteRules.ThemeParkIncGardener ), "the folder's one bank is whole under any limit" );
		Assert.IsFalse( StaffSpriteRules.IsBankLimited( StaffSpriteRules.ThemeParkIncGardener ) );
		Assert.IsTrue( sprites.AnimationsOf( StaffSpriteRules.ThemeParkIncGardener, 0 )!.Animations.Count > 4, "the walk, stand and work slots" );
		// Theme Park World's levels do not probe for the gardeners' folder
		GameEditions.Select( GameEdition.ThemeParkWorld );
		Assert.AreEqual( 0, StaffSpriteSet.Load( "science" ).BankCount( StaffSpriteRules.ThemeParkIncGardener ) );
		GameEditions.Select( GameEdition.ThemeParkInc );
		var settings = ThemeParkIncStaffSettings.Read( BalanceSettings.LoadThemeParkInc( "science" ).Standard )!;
		var gardener = settings.Roles[ThemeParkIncStaffRole.Gardener];
		// data\levels\Standard.sam: Gardener.MinWorkDuration 30, MaxWorkDuration 5, MinDetectionRange 3, MaxDetectionRange 5 (0xA022D0..0xA022DC)
		Assert.AreEqual( (30, 5, 3, 5), (gardener.MinWorkDuration, gardener.MaxWorkDuration, gardener.MinRange, gardener.MaxRange) );
	}

	// ---- Separation ---------------------------------------------------------------------------------

	[TestMethod]
	public void AGardenerIsASeparationNeighbourOfTheGuestsAndTheStaff()
	{
		// [BIN:TPI-EXE:0x63F1E0 separation neighbours] Theme Park Inc's query takes the kinds 1, 4, 5, 6, 8, 7, 0x14 and 0x12 (0x63F2AD..0x63F2CB): the gardener (0x14) is a neighbour, which Theme Park
		// World's 0x5102F0 has no kind for. OpenTPW files every placed member on the staff chain and the query reads it without a kind test (docs/reverse/TPI-NAVIGATOR.md)
		var economy = ScienceEconomy();
		var grid = Row( economy );
		var guests = new GuestSimulation( grid, GuestTests.CreateSettings(), 3 ) { ArrivalsEnabled = false };
		var world = new ThemeParkIncStaffWorld( grid ) { Guests = guests };
		var gardener = HireGardener( economy, world, (4, 5) );
		Assert.AreEqual( ThemeParkIncStaffRole.Gardener, gardener.Role );
		Assert.IsNotNull( guests.StaffChain.Of( gardener.ThingId ), "the dropped gardener is filed on his cell's chain" );
		var guest = guests.SpawnInPark( 4, 5 );
		// a step off the gardener's centre, inside separation's range (2 x radius)
		guest.Navigation.X = gardener.Navigation!.X + 0x2000;
		guest.Navigation.Y = gardener.Navigation.Y + 0x1000;
		var chain = new List<GuestNavigation>();
		guests.AppendCellChain( 4, 5, chain );
		CollectionAssert.Contains( chain, gardener.Navigation, "the cell's chain holds the gardener beside the guest" );
		var push = GuestNavigation.Separation( guest.Navigation, chain );
		Assert.AreNotEqual( (0, 0), push, "the guest is pushed away from the gardener" );
		Assert.IsTrue( push.X > 0 && push.Y > 0, "away from him: the guest stands right of and below him" );
		Assert.AreEqual( push, GuestNavigation.Separation( guest.Navigation, [gardener.Navigation] ), "he is the guest's only neighbour" );
		// and the gardener's own separation sees the guest
		var back = new List<GuestNavigation>();
		guests.AppendCellChain( 4, 5, back );
		var away = GuestNavigation.Separation( gardener.Navigation, back );
		Assert.IsTrue( away.X < 0 && away.Y < 0, "the gardener is pushed the other way" );
	}

	// ---- Hire, search and watering --------------------------------------------------------------

	[TestMethod]
	public void AGardenerIsHiredDroppedAndWatersTheDryPlantBesideHim()
	{
		// [BIN:TPI-EXE:0x5D2230 gardener constructor] the search runs at the drop; [BIN:TPI-EXE:0x5D28C0 gardener search] the chooser's plant asks for maintenance and he sets off (0x1F)
		var economy = ScienceEconomy();
		var garden = new Garden();
		var world = World( economy, garden );
		var plant = Plant( economy, garden, 9, condition: 1 );
		var member = HireGardener( economy, world, (8, 5), experience: 0 );
		Assert.AreEqual( (ThemeParkIncStaffWorld.ToPlant, plant.Id, member.ThingId, true), (member.State, member.PlantId, plant.MechanicId, plant.MaintenanceRequested) );
		Assert.AreEqual( (25, ThemeParkIncStaffWorld.WalkTemplate), (member.SpeedBonus, member.Template), "0x60B3F0(1) and template 0xF" );
		Assert.AreEqual( economy.Turn, plant.ClaimTurn );
		// the drop's fallback put the path cell (9, 5) beside the plant on its queue link: the step from (9, 6) to it is 0x10
		Assert.AreEqual( (byte)0x10, economy.Litter.Cells!.QueueLinkAt( 9, 5 ) );
		var turn = economy.Turn;
		for ( var step = 0; step < 400 && member.State == ThemeParkIncStaffWorld.ToPlant; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( (ThemeParkIncStaffWorld.Watering, turn, ThemeParkIncStaffWorld.WorkTemplate), (member.State, member.WorkStamp, member.Template), "arrived, he waters (0x20)" );
		var duration = economy.TpiStaff!.Settings.WorkDuration( ThemeParkIncStaffRole.Gardener, member.Experience, member.Motivation, member.Salary );
		var started = turn;
		while ( member.State == ThemeParkIncStaffWorld.Watering )
		{
			world.Update( economy, member, ++turn );
			// [BIN:TPI-EXE:0x609E60 face the cell's link] each watering update turns him by the queue link of his cell: 0x10 faces 0x3E8
			Assert.AreEqual( 0x3E8, member.SpriteAngle, "the watering faces the cell's link" );
		}
		Assert.AreEqual( started + duration + 1, turn, "the watering ends past +0x5D4 + the work duration" );
		Assert.AreEqual( (1, 100.0, 0, false), (garden.Scripts[plant.Id][ParkEconomy.PlantWater], plant.Repair, plant.MechanicId, plant.MaintenanceRequested), "0x5E31B0: VAR_WATER, +0x54, the claim and the request" );
		Assert.AreEqual( ThemeParkIncStaffWorld.Idle, member.State );
	}

	/// <summary>The sprite turn <c>0x609E60</c>: the cell's queue-link byte picks the heading word; any other byte, none included, faces 0x7D0.</summary>
	[TestMethod]
	public void TheWateringFacingFollowsTheExecutablesTable()
	{
		// [BIN:TPI-EXE:0x609E60 face the cell's link] 1: 0x7D0, 4: 0x5DC, 0x10: 0x3E8, 0x40: 0x1F4, otherwise 0x7D0
		var expected = new Dictionary<byte, int> { [0x01] = 0x7D0, [0x04] = 0x5DC, [0x10] = 0x3E8, [0x40] = 0x1F4 };
		for ( var link = 0; link < 256; link++ )
			Assert.AreEqual( expected.GetValueOrDefault( (byte)link, 0x7D0 ), ThemeParkIncStaffWorld.Facing( (byte)link ), $"link {link:X2}" );
	}

	[TestMethod]
	public void TheChooserSkipsAPlantOutOfRangeHealthyDeadOrBurningAndTakesOneThatAskedForMaintenance()
	{
		// [BIN:TPI-EXE:0x5D2DC0 plant chooser] a dry plant within the detection range (3 at no experience: d below 9); a requested one at any distance nearer than the best, unless it burns
		var economy = ScienceEconomy();
		var garden = new Garden();
		var world = World( economy, garden );
		var far = Plant( economy, garden, 9, condition: 2 );
		var healthy = Plant( economy, garden, 2, condition: 0 );
		var dead = Plant( economy, garden, 3, condition: 3, dead: 1 );
		var member = HireGardener( economy, world, (2, 5), experience: 0 );
		Assert.AreEqual( 0, member.PlantId, "the dry plant is 7 cells away, the near ones are healthy or dead" );
		Assert.AreEqual( ThemeParkIncStaffWorld.Walking, member.State, "a patrol instead" );
		garden.Scripts[dead.Id][ParkEconomy.PlantOnFire] = 1;
		Assert.IsTrue( economy.ThemeParkIncPlantDry( dead ), "a dead plant that burns is dry (0x5E3990)" );
		Assert.AreEqual( 0, world.FindPlant( economy, member, economy.Turn ), "but it burns, so the chooser's own test 0x5E3B20 refuses it" );
		garden.Scripts[dead.Id][ParkEconomy.PlantOnFire] = 0;
		far.MaintenanceRequested = true;
		Assert.AreEqual( far.Id, world.FindPlant( economy, member, economy.Turn ), "a plant that asked for maintenance is taken at any distance" );
		Assert.AreEqual( member.ThingId, far.MechanicId );
		far.MechanicId = 0;
		garden.Scripts[far.Id][ParkEconomy.PlantOnFire] = 1;
		Assert.AreEqual( 0, world.FindPlant( economy, member, economy.Turn ), "unless it burns" );
		_ = healthy;
	}

	[TestMethod]
	public void TheLastDryPlantInTheListWinsWhateverItsDistance()
	{
		// [BIN:TPI-EXE:0x5D2F16 dry branch] the dry branch compares the distance with R × R only, not with the best: a later plant in the list (an older one) replaces a nearer earlier one
		var economy = ScienceEconomy();
		var garden = new Garden();
		var world = World( economy, garden );
		var older = Plant( economy, garden, 6, condition: 1 );
		var newer = Plant( economy, garden, 4, condition: 1 );
		Assert.IsTrue( economy.ThemeParkIncObjectList.ToList().IndexOf( newer.Id ) < economy.ThemeParkIncObjectList.ToList().IndexOf( older.Id ), "the list is newest first" );
		var member = HireGardener( economy, world, (4, 5), experience: 0 );
		Assert.AreEqual( older.Id, member.PlantId, "the newer plant is nearer (d = 1) but the older one (d = 5) comes later" );
		Assert.AreEqual( (0, member.ThingId), (newer.MechanicId, older.MechanicId), "only the chosen plant is claimed" );
	}

	[TestMethod]
	public void AnotherGardenersFreshClaimKeepsThePlantAndAStaleOneLapses()
	{
		// [BIN:TPI-EXE:0x5E37F0 claimant] a claim within 100 turns holds; past it the claimant's 0x62B280 (a gardener's +0x5D8 against the plant) decides, and a lapsed claimant goes idle
		var economy = ScienceEconomy();
		var garden = new Garden();
		var world = World( economy, garden );
		var plant = Plant( economy, garden, 9, condition: 1 );
		var first = HireGardener( economy, world, (8, 5), experience: 0 );
		Assert.AreEqual( plant.Id, first.PlantId );
		var second = HireGardener( economy, world, (10, 5), experience: 0 );
		Assert.AreEqual( 0, second.PlantId, "the first gardener's claim is fresh" );
		Assert.AreEqual( first.ThingId, world.Claimant( economy, plant, plant.ClaimTurn + 500 ), "a claimant still on his way keeps it" );
		first.PlantId = 0;
		first.State = ThemeParkIncStaffWorld.Walking;
		Assert.AreEqual( plant.Id, world.FindPlant( economy, second, plant.ClaimTurn + 101 ), "past 100 turns the first gardener no longer targets it: the claim lapses" );
		Assert.AreEqual( (ThemeParkIncStaffWorld.Idle, second.ThingId), (first.State, plant.MechanicId), "the lapsed claimant goes idle; the second claims it" );
	}

	[TestMethod]
	public void ATiredGardenerDoesNotLookForPlants()
	{
		// [BIN:TPI-EXE:0x62B3B0 tired] "Too tired to water plants": the chooser does not run and +0x5D8 is kept
		var economy = ScienceEconomy();
		var garden = new Garden();
		var world = World( economy, garden );
		var member = HireGardener( economy, world, (8, 5), experience: 0 );
		var plant = Plant( economy, garden, 9, condition: 1 );
		member.Energy = economy.TpiStaff!.Settings.RestLevel - 0.5f;
		member.State = ThemeParkIncStaffWorld.Idle;
		world.Search( economy, member, economy.Turn );
		Assert.AreEqual( (0, 0), (member.PlantId, plant.MechanicId) );
		member.Energy = 100f;
		world.Search( economy, member, economy.Turn );
		Assert.AreEqual( plant.Id, member.PlantId );
	}

	[TestMethod]
	public void ThePlantBeingWateredIsSoldAndTheGardenerGoesIdle()
	{
		// [BIN:TPI-EXE:0x5D2480 gardener message] message 10 for his +0x5D8: "plant I was watering was deleted", +0x5D8 = 0, state 0
		var economy = ScienceEconomy();
		var garden = new Garden();
		var world = World( economy, garden );
		var plant = Plant( economy, garden, 9, condition: 1 );
		var member = HireGardener( economy, world, (8, 5), experience: 0 );
		Assert.AreEqual( ThemeParkIncStaffWorld.ToPlant, member.State );
		economy.Sell( plant.Id );
		Assert.AreEqual( (0, ThemeParkIncStaffWorld.Idle, ThemeParkIncStaffWorld.IdleTemplate), (member.PlantId, member.State, member.Template) );
	}

	[TestMethod]
	public void TheIdleGardenerSearchesEverySearchPeriodWithoutAFinderFirst()
	{
		// [BIN:TPI-EXE:0x5D2820 gardener pre-update] p = trunc((1 - exp / MaxExperience) × 50): the search runs from state 0 on turns that p divides
		var economy = ScienceEconomy();
		var garden = new Garden();
		var world = World( economy, garden );
		var member = HireGardener( economy, world, (8, 5), experience: 0 );
		member.State = ThemeParkIncStaffWorld.Idle;
		member.IdleStamp = 1_000_000;
		var plant = Plant( economy, garden, 9, condition: 1 );
		Assert.AreEqual( 50, ThemeParkIncStaffWorld.SearchPeriod( 0, economy.TpiStaff!.Settings.MaxExperience ) );
		world.Update( economy, member, 1001 );
		Assert.AreEqual( 0, member.PlantId, "1001 is no multiple of 50" );
		world.Update( economy, member, 1050 );
		Assert.AreEqual( (plant.Id, ThemeParkIncStaffWorld.ToPlant), (member.PlantId, member.State) );
	}

	// ---- Training and the staff HQ --------------------------------------------------------------

	[TestMethod]
	public void AGardenerLearnsAGardeningAbilityAndFillsTheHqSlotThatAsksForIt()
	{
		// [BIN:TPI-EXE:0x62B6C0 ability compatibility] a gardener learns 10 to 14; [BIN:TPI-EXE:0x6348C0 gardener finder] the rebuild's staff type 5 finds him; [BIN:TPI-EXE:0x63256F training finished]
		var economy = ScienceEconomy();
		var garden = new Garden();
		var world = World( economy, garden );
		var rooms = new TrainingRooms { Training = new ThemeParkIncRoom( 500, (2, 5) ) };
		world.Rooms = rooms;
		var career = economy.TpiCareer!;
		var state = career.Capture();
		state.Abilities[10] = 1;
		career.Restore( state );
		var member = HireGardener( economy, world, (1, 5), experience: 20000 );
		var staff = economy.TpiStaff!;
		// a slot asking for ability 10
		staff.Hq.Import( Enumerable.Range( 0, 20 ).Select( index => index == 0 ? new byte[] { 5, 0, 0, 0, 10, 0, 0, 0, 0, 0, 0, 0 } : new byte[] { 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 } ).ToArray(),
			new uint[15] );
		Assert.IsFalse( staff.Hq.AllFilled );
		Assert.AreEqual( ThemeParkIncTrainingChoice.Started, world.ChooseAbility( economy, member, 10, confirmed: false, economy.Turn ) );
		Assert.AreEqual( ThemeParkIncTraining.ToTrainingRoom, member.State );
		var turn = economy.Turn;
		for ( var step = 0; step < 400 && member.State == ThemeParkIncTraining.ToTrainingRoom; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncTraining.Training, member.State, "the gardener's dispatch sends state 7 to the common handler" );
		while ( member.State == ThemeParkIncTraining.Training )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( (10, 0), (member.Ability, member.HqSlot), "trained, he fills the slot" );
		Assert.IsTrue( staff.Hq.AllFilled );
		Assert.IsFalse( ThemeParkIncTraining.CanLearn( member, 7, career.Abilities ), "a handyman's ability is not his" );
		CollectionAssert.AreEqual( new[] { 10 }, Enumerable.Range( 0, 15 ).Where( ability => ThemeParkIncTraining.CanLearn( member, ability, career.Abilities ) ).ToArray(),
			"of the discovered abilities he may learn the gardener's" );
	}

	private sealed class TrainingRooms : IThemeParkIncRooms
	{
		public ThemeParkIncRoom? Training { get; set; }
		public ThemeParkIncRoom? First( ThemeParkIncRoomKind kind ) => kind == ThemeParkIncRoomKind.Training ? Training : null;
		public void AddOccupancy( ushort thing, int delta ) { }
	}

	// ---- The save -------------------------------------------------------------------------------

	[TestMethod]
	public void AWateringGardenerSurvivesASaveAndAnOlderSaveIsRefused()
	{
		var economy = ScienceEconomy();
		var garden = new Garden();
		var world = World( economy, garden );
		var plant = Plant( economy, garden, 9, condition: 1 );
		var member = HireGardener( economy, world, (8, 5), experience: 0 );
		var turn = economy.Turn;
		for ( var step = 0; step < 400 && member.State == ThemeParkIncStaffWorld.ToPlant; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.Watering, member.State );
		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"TpiGardens\"" );
		StringAssert.Contains( json, "\"PlantId\"" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ), "the save round-trips exactly" );
		var restoredMember = restored.TpiStaff!.Members.Single();
		Assert.AreEqual( (member.PlantId, member.WorkStamp, member.State), (restoredMember.PlantId, restoredMember.WorkStamp, restoredMember.State) );
		var restoredGarden = new Garden();
		foreach ( var (id, cell) in garden.Cells )
		{
			restoredGarden.Cells[id] = cell;
			restoredGarden.Scripts[id] = (int[])garden.Scripts[id].Clone();
		}
		var restoredWorld = World( restored, restoredGarden );
		for ( var step = 0; step < 60; step++ )
		{
			turn++;
			world.Update( economy, member, turn );
			restoredWorld.Update( restored, restoredMember, turn );
		}
		Assert.AreEqual( ParkSaveFile.Serialize( economy ), ParkSaveFile.Serialize( restored ), "both go on alike" );
		Assert.AreEqual( 1, restoredGarden.Scripts[plant.Id][ParkEconomy.PlantWater], "and the watering ended" );

		// a Science save from before the gardeners and the plants' watering is refused
		var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		node.Remove( "TpiGardens" );
		var older = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( node.ToJsonString() ) ), economy.Settings, economy.Catalog ) );
		Assert.AreEqual( ParkSaveFile.ThemeParkIncGardensMissing, older.Message );
		var saved = economy.TpiStaff!.Capture();
		Assert.IsFalse( ThemeParkIncStaff.IsValid( saved with { Members = saved.Members.Select( item => item with { World = item.World! with { PatrolArea = new[] { 1, 1, 9, 9 } } } ).ToArray() } ),
			"a gardener's patrol area other than the whole map is refused (0x6256A0 is not ported)" );
		Assert.IsFalse( ThemeParkIncStaff.IsValid( saved with { Members = saved.Members.Select( item => item with { World = item.World! with { PlantId = null } } ).ToArray() } ),
			"a gardener's thing keeps his plant word" );
		Assert.IsFalse( ThemeParkIncStaff.IsValid( saved with { Members = saved.Members.Select( item => item with { World = item.World! with { State = ThemeParkIncStaffWorld.Repairing } } ).ToArray() } ),
			"a mechanic's state is not a gardener's" );

		// a gardener's plant and his claims are plants (his chooser reads only flag-0x1000 things), and no mechanic claims a plant
		var ride = economy.RegisterExisting( economy.Catalog.Objects.First( info => !info.RequiresWatering ).InfoId );
		// the loop above stepped the gardener's turns without the park's clock, so the objects' claim turns (the only stamps checked against the save's tick) are brought back inside it
		var stamped = System.Text.Json.Nodes.JsonNode.Parse( ParkSaveFile.Serialize( economy ) )!.AsObject();
		foreach ( var objectNode in stamped["Objects"]!.AsArray() )
			objectNode!.AsObject()["ClaimTurn"] = 0;
		var withRide = stamped.ToJsonString();
		ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( withRide ) ), economy.Settings, economy.Catalog );
		string Edit( Action<System.Text.Json.Nodes.JsonObject> edit )
		{
			var root = System.Text.Json.Nodes.JsonNode.Parse( withRide )!.AsObject();
			edit( root );
			return root.ToJsonString();
		}
		System.Text.Json.Nodes.JsonObject ObjectNode( System.Text.Json.Nodes.JsonObject root, int id ) =>
			root["Objects"]!.AsArray().Select( item => item!.AsObject() ).Single( item => (int)item["Id"]! == id );
		// the job-kind check's own message, so a refusal from another validator does not pass for it
		void Refused( string text, string expected, string because ) =>
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( text ) ), economy.Settings, economy.Catalog ), because ).Message,
				expected, because );
		Refused( Edit( root =>
		{
			root["TpiStaff"]!["Members"]![0]!["World"]!["PlantId"] = ride.Id;
			ObjectNode( root, plant.Id )["MechanicId"] = 0;
			ObjectNode( root, ride.Id )["MechanicId"] = member.ThingId;
		} ), "is no plant", "a gardener watering a ride" );
		Refused( Edit( root =>
		{
			ObjectNode( root, ride.Id )["MechanicId"] = member.ThingId;
		} ), "whose search does not take it", "a gardener's claim on a ride" );
	}

	[TestMethod]
	public void AWorldSaveFromBeforeTheGardenersIsRefusedBeforeItsAssetsAreLookedAt()
	{
		// the world save's embedded economy is validated (ParkSaveFile.Validate) before the continuation's own state and the asset manifest
		var envelope = ParkResumeSaveFileTests.Envelope();
		var economy = ScienceEconomy();
		var current = envelope with { Theme = "science", Economy = ParkSaveFile.Capture( economy ) };
		System.Text.Json.Nodes.JsonObject Save( bool gardens )
		{
			var node = System.Text.Json.Nodes.JsonNode.Parse( ParkResumeJson.Serialize( current ) )!.AsObject();
			if ( !gardens )
				node["Economy"]!.AsObject().Remove( "TpiGardens" );
			node["AssetHashes"] = new System.Text.Json.Nodes.JsonObject();
			return node;
		}
		var refusal = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( Save( gardens: false ).ToJsonString() ) ) );
		Assert.AreEqual( ParkSaveFile.ThemeParkIncGardensMissing, refusal.Message );
		Assert.AreNotEqual( ParkSaveFile.ThemeParkIncGardensMissing,
			Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( Save( gardens: true ).ToJsonString() ) ) ).Message );
	}

	// ---- The plants' thing pass --------------------------------------------------------------

	[TestMethod]
	public void APlantsUpdateRunsInTheThemeParkIncObjectUpdate()
	{
		// [BIN:TPI-EXE:0x63760B attraction update] kind 3: 0x5E4790, then 0x5E4CB0's plant block
		var economy = ScienceEconomy();
		var garden = new Garden();
		_ = World( economy, garden );
		var plant = Plant( economy, garden, 9, condition: 0 );
		economy.PlaceObjectEffects( plant.Id, (9, 6) );
		Assert.AreEqual( RideObjectStates.NotAnAttraction, ParkEconomy.ObjectState( plant ), "a plant is not choosable: state 3" );
		garden.Scripts[plant.Id][ParkEconomy.PlantCondition] = 2;
		economy.UpdateRide( plant, economy.Turn );
		Assert.AreEqual( (2, 33.0), (garden.Scripts[plant.Id][ParkEconomy.PlantOldState], plant.Repair) );
	}
}
