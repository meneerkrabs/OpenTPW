using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's plants (docs/reverse/TPI-STAFF.md, "Gardener and plants"): the shared script data\levels\vegetation.rse (0x5DE61A), the state-3 update 0x5E4CB0 (the wither rate, the gauge and the
/// condition's RegionFX row), the constructor's and the destructor's rows, the dry and on-fire tests and the watering's end (0x5E31B0). The synthetic tests run without data; the retail ones need
/// OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncPlantTests
{
	private BaseFileSystem? previous;
	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize()
	{
		previous = FileSystem;
		previousEdition = GameEditions.Current;
		Log ??= new();
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous!;
		GameEditions.Select( previousEdition );
	}

	private const string Standard =
		"BankAccountInfo.InitialCash\t50000\nBankAccountInfo.InitialAdmissionFee\t25\n"
		+ "Costs.QueueCell 25\nCosts.PathCell 10\nCosts.KartTrackCell 40\nCosts.WaterTrackCell 50\nCosts.MapCell 75\n"
		+ "PeepInfo.ExcitementToCostDivisor 6\nPeepInfo.MinimumEntryFee 20\nPeepInfo.CheapPriceMultiplier 0.75\nPeepInfo.AveragePriceMultiplier 1.25\nPeepInfo.ExpensivePriceMultiplier 2.0\n"
		+ "RegionFX[10].Radius 1\nRegionFX[10].Happiness 3\nRegionFX[10].Illness -5\n"
		+ "RegionFX[11].Radius 1\nRegionFX[11].Happiness 2\nRegionFX[11].Illness -2\n"
		+ "RegionFX[12].Radius 1\n"
		+ "RegionFX[13].Radius 1\nRegionFX[13].Happiness -1\nRegionFX[13].Illness 1\n"
		+ "Vegetation.RainWither 2.0\nVegetation.HeatwaveWither 0.5\nVegetation.SnowWither 1.0\nVegetation.StormWither 1.5\n";

	private static readonly EconomyObjectInfo Plant = new( 1, "Bush", ParkObjectKind.Feature, ResearchCategory.Feature, 0,
		new List<UpgradeLevelInfo> { new( 0, 100, 10, 0, 0, new[] { 50, 30, 20, 10 } ) }, null, null, null, 0, 0, 0, 0, 0, 0, "/x", IsChoosable: false, RequiresWatering: true );

	/// <summary>A synthetic Theme Park Inc economy with one plant whose script variables are <paramref name="variables"/> (vegetation.rse's 15).</summary>
	private static (ParkEconomy Economy, ParkObjectState Plant) Economy( int[] variables )
	{
		var economy = new ParkEconomy( BalanceSettings.ForThemeParkInc( new SamSettings( new[] { SamDocument.Parse( Standard, "Standard.sam" ) } ), "science" ),
			new EconomyObjectCatalog( new[] { Plant } ), ParkGameMode.FullSimulation, 7, EconomyRules.ThemeParkIncMoney );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1, out var plant ) );
		economy.ThemeParkIncScriptOf = instance => instance == plant!.Id ? variables : null;
		return (economy, plant!);
	}

	private static int[] Variables( int condition = 0, int old = 0, int scale = 1000, int dead = 0, int onFire = 0 )
	{
		var variables = new int[15];
		variables[ParkEconomy.PlantCondition] = condition;
		variables[ParkEconomy.PlantOldState] = old;
		variables[ParkEconomy.PlantScale] = scale;
		variables[ParkEconomy.PlantDead] = dead;
		variables[ParkEconomy.PlantOnFire] = onFire;
		return variables;
	}

	[TestMethod]
	public void TheRegionTableHasFifteenRowsInThemeParkIncAndEightInThemeParkWorld()
	{
		// [BIN:TPI-EXE:0x996684 Standard schema] "3,RegionFX,0,0,15"
		var (economy, _) = Economy( Variables() );
		Assert.AreEqual( RegionEffect.ThemeParkIncCount, economy.Litter.Environment.Effects.Length );
		Assert.AreEqual( new RegionEffect( 1, 3, -5, 0, 0, 0 ), economy.Litter.Environment.Effects[10] );
		var tpw = new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( Array.Empty<EconomyObjectInfo>() ), ParkGameMode.FullSimulation, 7 );
		Assert.AreEqual( RegionEffect.Count, tpw.Litter.Environment.Effects.Length );
	}

	[TestMethod]
	public void ANewPlantPutsRowTenOnItsCellAndItsDestructionTakesOffTheRowOfItsLiveCondition()
	{
		// [BIN:TPI-EXE:0x5DB63F plant row] row 10 at construction; [BIN:TPI-EXE:0x5DE9D3 plant row off] the row of VAR_CONDITION at destruction
		var variables = Variables();
		var (economy, plant) = Economy( variables );
		economy.PlaceObjectEffects( plant.Id, (20, 20) );
		Assert.AreEqual( ((short)3, (short)-5), (economy.Litter.Environment.At( 20, 20 ).Happiness, economy.Litter.Environment.At( 20, 20 ).Illness) );
		Assert.AreEqual( ((short)1, (short)-2), (economy.Litter.Environment.At( 21, 20 ).Happiness, economy.Litter.Environment.At( 21, 20 ).Illness), "the coefficient over the distance + 1, toward zero" );
		// the script moved to condition 1 since the plant's last update: the destructor takes off row 11, so row 10 minus row 11 stays behind, as in the original
		variables[ParkEconomy.PlantCondition] = 1;
		economy.Remove( plant.Id );
		Assert.AreEqual( ((short)(3 - 2), (short)(-5 + 2)), (economy.Litter.Environment.At( 20, 20 ).Happiness, economy.Litter.Environment.At( 20, 20 ).Illness) );
	}

	[TestMethod]
	public void TheUpdateMovesTheRowWhenTheConditionChangesAndWritesTheGaugeEveryTime()
	{
		// [BIN:TPI-EXE:0x5E4CB0 object state dispatch] c != o: VAR_OLDSTATE = c, row 10 + o off, row 10 + c on; +0x54 = (3 - c) × 33 on every update
		var variables = Variables();
		var (economy, plant) = Economy( variables );
		economy.PlaceObjectEffects( plant.Id, (20, 20) );
		economy.UpdateThemeParkIncPlant( plant );
		Assert.AreEqual( 99.0, plant.Repair );
		Assert.AreEqual( (short)3, economy.Litter.Environment.At( 20, 20 ).Happiness, "condition unchanged: row 10 stays" );
		variables[ParkEconomy.PlantCondition] = 2;
		economy.UpdateThemeParkIncPlant( plant );
		Assert.AreEqual( (2, 33.0), (variables[ParkEconomy.PlantOldState], plant.Repair) );
		Assert.AreEqual( ((short)0, (short)0), (economy.Litter.Environment.At( 20, 20 ).Happiness, economy.Litter.Environment.At( 20, 20 ).Illness), "row 12 is all zero" );
		variables[ParkEconomy.PlantCondition] = 3;
		economy.UpdateThemeParkIncPlant( plant );
		Assert.AreEqual( ((short)-1, (short)1, 0.0), (economy.Litter.Environment.At( 20, 20 ).Happiness, economy.Litter.Environment.At( 20, 20 ).Illness, plant.Repair) );
		// a condition outside 0..3 moves no row in (the tables 0x5E5044 and 0x5E5054 end at 3) but is still noted
		variables[ParkEconomy.PlantCondition] = 7;
		economy.UpdateThemeParkIncPlant( plant );
		Assert.AreEqual( (7, (short)0), (variables[ParkEconomy.PlantOldState], economy.Litter.Environment.At( 20, 20 ).Happiness) );
		Assert.AreEqual( (float)(-4 * 33), (float)plant.Repair );
	}

	[TestMethod]
	public void TheScaleTakesTheWeathersWitherOnlyWhenItsThousandthsDiffer()
	{
		// [BIN:TPI-EXE:0x5E4D82 wither scale] s = VAR_SCALE / 1000 toward zero; a wither unequal to s writes ftol(wither × 1000)
		var withers = new ThemeParkIncWithers( 0.5f, 2.0f, 1.5f, 1.0f );
		Assert.AreEqual( 1.0f, ParkEconomy.PlantWither( 0, withers ), "no spell" );
		CollectionAssert.AreEqual( new[] { 0.5f, 2.0f, 1.5f, 1.0f, 1.0f }, Enumerable.Range( 1, 5 ).Select( state => ParkEconomy.PlantWither( state, withers ) ).ToArray() );
		var variables = Variables( scale: 1999 );
		var (economy, plant) = Economy( variables );
		Assert.AreEqual( withers, economy.Settings.ThemeParkIncWithers, "Standard.sam's Vegetation.*Wither, heatwave first as at 0xA029A4" );
		economy.UpdateThemeParkIncPlant( plant );
		Assert.AreEqual( 1999, variables[ParkEconomy.PlantScale], "1999 / 1000 is 1: equal to the clear sky's 1.0, so no write" );
		variables[ParkEconomy.PlantScale] = -999;
		economy.UpdateThemeParkIncPlant( plant );
		Assert.AreEqual( 1000, variables[ParkEconomy.PlantScale], "-999 / 1000 is 0 toward zero: written" );
	}

	[TestMethod]
	public void TheDryAndOnFireTestsReadTheConditionTheDeathAndTheFire()
	{
		// [BIN:TPI-EXE:0x5E3990 dry test] condition above 0 and (alive or burning); [BIN:TPI-EXE:0x5E3B20 on fire] VAR_ONFIRE
		var variables = Variables();
		var (economy, plant) = Economy( variables );
		Assert.IsFalse( economy.ThemeParkIncPlantDry( plant ) );
		variables[ParkEconomy.PlantCondition] = 1;
		Assert.IsTrue( economy.ThemeParkIncPlantDry( plant ) );
		variables[ParkEconomy.PlantDead] = 1;
		Assert.IsFalse( economy.ThemeParkIncPlantDry( plant ), "a dead plant is not watered" );
		variables[ParkEconomy.PlantOnFire] = 1;
		Assert.IsTrue( economy.ThemeParkIncPlantDry( plant ), "unless it burns" );
		Assert.AreEqual( 1, economy.ThemeParkIncPlantOnFire( plant ) );
		variables[ParkEconomy.PlantCondition] = -1;
		Assert.IsFalse( economy.ThemeParkIncPlantDry( plant ), "a condition at most 0 (signed) is never dry" );
		economy.ThemeParkIncScriptOf = null;
		Assert.IsFalse( economy.ThemeParkIncPlantDry( plant ), "no script reads 0" );
	}

	[TestMethod]
	public void TheWateringsEndWritesTheWaterFillsTheGaugeAndClearsTheClaimAndTheRequest()
	{
		// [BIN:TPI-EXE:0x5E31B0 maintenance finish] VAR_WATER = 1, +0x54 = 100, +0x72 = 0, +0x78 = 0
		var variables = Variables( condition: 2 );
		var (economy, plant) = Economy( variables );
		plant.MechanicId = 9;
		plant.ClaimTurn = 40;
		plant.MaintenanceRequested = true;
		plant.Repair = 33;
		economy.FinishThemeParkIncWatering( plant );
		Assert.AreEqual( (1, 100.0, 0, false), (variables[ParkEconomy.PlantWater], plant.Repair, plant.MechanicId, plant.MaintenanceRequested) );
		Assert.AreEqual( 40, plant.ClaimTurn, "the claim turn +0x74 is not written" );
	}

	[TestMethod]
	public void ThePlantsScriptRunsInTheVmThroughItsConditionsAndItsRowFollowsAtTheTurnRate()
	{
		// data\levels\vegetation.rse in the script VM beside the engine's plant update at the turn rate (248 ms): the timer withers the plant 0 -> 1 -> 2, VAR_WATER brings 2 back through 1 to 0,
		// and the timer withers it again to 3 (dead), where water no longer helps. Each change copies VAR_CONDITION into VAR_OLDSTATE first and is followed by a WAITANIM of at least 300 script ms,
		// longer than a turn, so the engine's update runs between two changes: at every tick (a save point) the row VAR_OLDSTATE names is the row on the cells (ObjectEffectRow, which a load puts back)
		var corpus = Retail();
		var script = new RideScriptFile( new MemoryStream( corpus ) );
		CollectionAssert.AreEqual( new[] { "VAR_CONDITION", "VAR_WATER", "VAR_TIMEOUT", "VAR_TIMER", "VAR_DEAD", "VAR_TEST", "VAR_RIDECLOSED", "VAR_BROKEN", "VAR_WORN", "VAR_RUNNING",
			"VAR_OLDSTATE", "VAR_SETFIRE", "VAR_ONFIRE", "VAR_PRANKED", "VAR_SCALE" }, script.VariableNames.ToArray(), "the indexes the engine reads and writes" );
		var plantEntry = ObjectCatalog.Load( "science" ).Entries.First( entry => entry.Settings.GetBool( "UsageInfo.RequiresWatering" ) );
		GameEditions.Select( GameEdition.ThemeParkInc );
		var runtime = new OriginalObjectRuntime( plantEntry );
		var variables = runtime.Script!.Variables;
		// the construction words (0x5DB850): VAR_TIMEOUT, VAR_TEST, VAR_SCALE; a short timeout keeps the run short (the timer is VAR_TIMEOUT x VAR_SCALE)
		variables[2] = 2;
		variables[5] = 0;
		variables[ParkEconomy.PlantScale] = 1000;
		var (economy, plant) = Economy( variables );
		economy.PlaceObjectEffects( plant.Id, (20, 20) );
		var engineRow = ParkEconomy.PlantHealthyRow;
		var seen = new List<int> { 0 };
		var elapsed = 0.0;
		var nextTurn = ParkCalendar.TurnMilliseconds / 1000.0;
		bool watered = false, waterAfterDeath = false;
		for ( var tick = 0; tick < 200 * FixedStepClock.TicksPerSecond; tick++ )
		{
			runtime.Simulate( FixedStepClock.TickDuration );
			elapsed += FixedStepClock.TickDuration;
			Assert.AreNotEqual( RideVMState.Faulted, runtime.Script.State, runtime.Script.FaultMessage );
			Assert.AreEqual( engineRow, economy.ObjectEffectRow( plant ), $"a save at {elapsed:F3} s restores the live row" );
			if ( elapsed < nextTurn )
				continue;
			nextTurn += ParkCalendar.TurnMilliseconds / 1000.0;
			// the scale stays 1000: no weather spell, so the wither is 1.0
			economy.UpdateThemeParkIncPlant( plant );
			var condition = variables[ParkEconomy.PlantCondition];
			engineRow = ParkEconomy.PlantRow( condition );
			if ( condition != seen[^1] )
				seen.Add( condition );
			if ( condition == 2 && !watered )
			{
				watered = true;
				variables[ParkEconomy.PlantWater] = 1;
			}
			if ( condition == 3 && !waterAfterDeath )
			{
				waterAfterDeath = true;
				variables[ParkEconomy.PlantWater] = 1;
			}
			if ( waterAfterDeath && variables[ParkEconomy.PlantWater] == 0 && elapsed > 5 && seen.Count >= 8 )
				break;
		}
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 1, 0, 1, 2, 3 }, seen, string.Join( ",", seen ) );
		Assert.AreEqual( (3, 1, 0), (variables[ParkEconomy.PlantCondition], variables[ParkEconomy.PlantDead], variables[ParkEconomy.PlantWater]), "a dead plant takes the water and stays dead" );
		Assert.AreEqual( ((short)-1, (short)1), (economy.Litter.Environment.At( 20, 20 ).Happiness, economy.Litter.Environment.At( 20, 20 ).Illness), "only the dead plant's row 13 is on the cells" );
	}

	[TestMethod]
	public void APlantSoldThroughTheLevelTakesOffTheRowOfItsLiveCondition()
	{
		// [BIN:TPI-EXE:0x5DE9D3 plant row off] the remove tool's path: ParkObjects.Remove -> the level's ObjectRemoved -> the sale -> RemoveObjectEffects, which reads VAR_CONDITION from the plant's script
		// through the level (ScriptVariablesOfInstance); the level unlinks the object only after the sale, so a dead plant takes off row 13, not row 10 (the read of an unlinked script answers 0)
		Retail();
		GameEditions.Select( GameEdition.ThemeParkInc );
		var retail = ObjectCatalog.Load( "science" ).Entries.First( candidate => candidate.Settings.GetBool( "UsageInfo.RequiresWatering" ) );
		// the retail plant's settings and shape over a textureless one-frame model (no GPU in the tests); its script is the shared vegetation.rse of the retail data (OriginalObjectRuntime.ScriptOf)
		var root = Directory.CreateTempSubdirectory( "opentpw-tpi-plant-sale-" ).FullName;
		var previousEntities = Entity.All;
		Entity.All = new();
		try
		{
			var bytes = Md2ModelFileTests.CreateGeometry();
			System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x42 ), 1 ); System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x44 ), 0 );
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x268 ), 0 ); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x78 ), 0x264 );
			foreach ( var at in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( at ), 0 );
			File.WriteAllBytes( Path.Combine( root, "model.MD2" ), bytes );
			var entry = new ObjectCatalogEntry( "science", retail.Category, retail.ArchivePath, retail.Settings, retail.Shape, "/model.MD2", null, null, Array.Empty<string>(),
				Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
			SellThroughTheLevel( entry );
		}
		finally
		{
			Entity.All = previousEntities;
			Directory.Delete( root, true );
		}
	}

	private static void SellThroughTheLevel( ObjectCatalogEntry entry )
	{
		var economy = new ParkEconomy( BalanceSettings.LoadThemeParkInc( "science" ), EconomyObjectCatalog.Load( "science", easy: false ), ParkGameMode.FullSimulation, 1,
			EconomyRules.ThemeParkIncMoney );
		var level = (Level)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
		void Set( string field, object? value ) => typeof( Level ).GetField( field, Private )!.SetValue( level, value );
		Set( "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) );
		var objects = new ParkObjects( ObjectCatalog.Load( "science" ), new SandboxParkGrid() );
		Set( "<Objects>k__BackingField", objects );
		Set( "economyInstances", new Dictionary<OriginalObject, int>() );
		var plant = objects.Add( entry, 40, 40, 0 );
		Assert.IsNotNull( plant.Runtime.Script, "the plant runs vegetation.rse" );
		typeof( Level ).GetMethod( "AttachAdvisorProbes", Private )!.Invoke( level, null );
		typeof( Level ).GetMethod( "ConnectObjectsToEconomy", Private )!.Invoke( level, null );
		var instance = level.GetEconomyInstance( plant )!.Value;
		Assert.IsTrue( economy.TryGetObject( instance, out var state ) );
		var cell = state.EffectCell!.Value;
		var healthy = economy.Litter.Environment.At( cell.X, cell.Y );
		Assert.AreEqual( ((short)3, (short)-5), (healthy.Happiness, healthy.Illness), "the constructor's row 10" );
		// the script reached the dead condition and the update moved the row: 13 is on the cells
		var variables = plant.Runtime.Script!.Variables;
		variables[ParkEconomy.PlantCondition] = 3;
		variables[ParkEconomy.PlantDead] = 1;
		economy.UpdateThemeParkIncPlant( state );
		var dead = economy.Litter.Environment.At( cell.X, cell.Y );
		Assert.AreEqual( ((short)-1, (short)1), (dead.Happiness, dead.Illness), "row 13" );
		Assert.IsTrue( objects.Remove( plant ) );
		Assert.IsFalse( economy.TryGetObject( instance, out _ ), "the remove tool sold the record" );
		Assert.IsNull( level.GetEconomyInstance( plant ), "and the level unlinked the object after the sale" );
		for ( var dx = -3; dx <= 3; dx++ )
			for ( var dy = -3; dy <= 3; dy++ )
			{
				var left = economy.Litter.Environment.At( cell.X + dx, cell.Y + dy );
				Assert.AreEqual( ((short)0, (short)0), (left.Happiness, left.Illness), $"nothing stays at ({cell.X + dx}, {cell.Y + dy})" );
			}
	}

	[TestMethod]
	public void InThemeParkIncAPlantRunsTheSharedVegetationScriptWithItsConstructionWords()
	{
		// [BIN:TPI-EXE:0x5DE61A plant script] data\levels\vegetation.rse for a thing with flag 0x1000; [BIN:TPI-EXE:0x5DB7D5 construction variables] 2, 5 and 14
		Retail();
		var catalog = ObjectCatalog.Load( "science" );
		var plants = catalog.Entries.Where( entry => entry.Settings.GetBool( "UsageInfo.RequiresWatering" ) ).ToList();
		Assert.IsTrue( plants.Count >= 5, $"{plants.Count} plants" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		var runtime = new OriginalObjectRuntime( plants[0] );
		Assert.IsNotNull( runtime.Script, "the plant runs data\\levels\\vegetation.rse" );
		StringAssert.EndsWith( runtime.Script!.Options.SourceName!.ToLowerInvariant(), "vegetation.rse" );
		var standard = ThemeParkIncStandard.Load( ParkEconomy.GameCareerFiles.Instance.Read, "science" )!;
		ThemeParkIncStandard.ConstructionWords( standard ).Apply( plants[0].Settings, runtime.Script );
		Assert.AreEqual( (400, 0, 1000), (runtime.Script.Variables[2], runtime.Script.Variables[5], runtime.Script.Variables[14]), "VAR_TIMEOUT, VAR_TEST and VAR_SCALE now land on a script" );
		// Theme Park World's loader has no watering flag: a plant there keeps its archive's script (none)
		GameEditions.Select( GameEdition.ThemeParkWorld );
		Assert.AreEqual( plants[0].ScriptPath, OriginalObjectRuntime.ScriptOf( plants[0] ).Path );
	}

	[TestMethod]
	public void EveryThemeParkIncPlantIsAFeatureNobodyChoosesThatIsNoReliefObjectAndHoldsNoLitter()
	{
		// the dry test's and the finish's first two branches (relief +0x42 bit 1, a bin bit 0x40) are not reached for a plant: no shipped plant has those flags, and none is choosable (state 3)
		Retail();
		foreach ( var world in new[] { "arabian", "science", "water" } )
		{
			var plants = ObjectCatalog.Load( world ).Entries.Where( entry => entry.Settings.GetBool( "UsageInfo.RequiresWatering" ) ).ToList();
			Assert.IsTrue( plants.Count >= 5, $"{world}: {plants.Count} plants" );
			foreach ( var plant in plants )
			{
				Assert.IsFalse( plant.Settings.GetBool( "UsageInfo.ProvidesRelief" ), $"{world}: {plant}" );
				Assert.IsFalse( plant.Settings.GetBool( "UsageInfo.HoldsLitter" ), $"{world}: {plant}" );
				Assert.IsFalse( plant.Settings.GetBool( "Info.IsChoosable" ), $"{world}: {plant}" );
			}
		}
	}

	[TestMethod]
	public void NoPlantHasARepairLineSoADeadPlantIsNoMechanicsJob()
	{
		// [BIN:TPI-EXE:0x5E28A0 repair line] the mechanics' finder takes an object whose repair byte is below the type's RideBreakDown.Imminent (+0x3C8); a dead plant's +0x54 is 0 (0x5E4DD4), so a plant
		// type with a line above 0 would be a mechanic's job: no shipped plant has one, and none wears (WearRate 0)
		Retail();
		foreach ( var world in new[] { "arabian", "science", "water" } )
			foreach ( var info in EconomyObjectCatalog.Load( world, easy: false ).Objects.Where( info => info.RequiresWatering ) )
			{
				Assert.AreEqual( 0f, info.BreakdownImminent, $"{world} {info.Name}" );
				Assert.IsTrue( info.Upgrades.All( level => level.WearRate == 0 ), $"{world} {info.Name}" );
			}
	}

	[TestMethod]
	public void TheShippedWithersAreTheVegetationKeys()
	{
		Retail();
		var settings = BalanceSettings.LoadThemeParkInc( "science" );
		// data\levels\Standard.sam: Vegetation.RainWither 2.0, HeatwaveWither 0.5, SnowWither 1.0, StormWither 1.5
		Assert.AreEqual( new ThemeParkIncWithers( 0.5f, 2.0f, 1.5f, 1.0f ), settings.ThemeParkIncWithers );
		Assert.AreEqual( RegionEffect.ThemeParkIncCount, RegionEffect.Load( settings.Standard, RegionEffect.ThemeParkIncCount ).Length );
		var table = RegionEffect.Load( settings.Standard, RegionEffect.ThemeParkIncCount );
		Assert.AreEqual( (new RegionEffect( 3, 3, -5, 0, 0, 0 ), new RegionEffect( 3, -1, 1, 0, 0, 0 )), (table[10], table[13]), "the healthy and the dead plant's rows" );
	}

	/// <summary>Points the file system at the Theme Park Inc data (Inconclusive without OPENTPW_TPI_PATH) and returns vegetation.rse's bytes.</summary>
	private static byte[] Retail()
	{
		ThemeParkIncObstacleTests.UseTpiData();
		var path = FileSystem.GetFiles( "/levels" ).First( file => string.Equals( Path.GetFileName( file ), OriginalObjectRuntime.VegetationScript, StringComparison.OrdinalIgnoreCase ) );
		return FileSystem.ReadAllBytes( path );
	}
}
