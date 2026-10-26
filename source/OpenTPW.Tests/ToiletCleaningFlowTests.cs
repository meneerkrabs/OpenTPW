using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The player's toilet flow (GitHub issue 829): the door only closes (tp.exe <c>0x48CCF0</c> → <c>0x4DF300</c>) and the call button closes and calls a handyman
/// (<c>0x4DFE30</c>), a handyman cleans a dirty toilet he passes whatever its door (<c>0x4D7880</c>), and the end of his clean (<c>0x4D73C0</c> state 11:
/// <c>0x4DFD80</c>, <c>0x4DF390</c>) resets the gauge, the claim, the call and the script's VAR_WORN, then opens the toilet.
/// </summary>
[TestClass, DoNotParallelize]
public class ToiletCleaningFlowTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private BaseFileSystem? originalFileSystem;
	private bool dataInUse;

	[TestInitialize]
	public void Initialize()
	{
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( dataInUse )
			FileSystem = originalFileSystem!;
		fixture?.Cleanup();
		renderer?.Dispose();
	}

	// ------------------------------------------------------------------ a level with a placed toilet

	/// <summary>The level of <see cref="ParkSettingCommandLevelTests"/> with its second object turned into a small toilet (a features-window object with the relief flag).</summary>
	private (Level Level, ParkEconomy Economy, OriginalObject Toilet, ParkObjectState State) ToiletLevel()
	{
		var park = ParkSettingCommandLevelTests.NewPark( fixture! );
		var entry = (ObjectCatalogEntry)typeof( UpgradeCatalogueDefaultsTests ).GetMethod( "Entry", Private )!.Invoke( fixture, new object[] { "Info.Id 1402\nInfo.WhichUIType 3\nUsageInfo.ProvidesRelief 1\n", true } )!;
		var objects = park.Level.Objects;
		var toilet = new OriginalObject( entry, new ObjectPlacement( 5, 5, 0, System.Numerics.Vector2.Zero, 0 ), objects.ScriptWorld, seed: 3 ^ objects.ObjectScriptKey );
		( (List<OriginalObject>)objects.Objects ).Add( toilet );
		var state = park.Economy.RegisterExisting( 1402 );
		// the catalogue-tier binding of LinkEconomy is for rides; a feature (the shipped toilets) is only mapped to its record
		( (Dictionary<OriginalObject, int>)typeof( Level ).GetField( "economyInstances", Private )!.GetValue( park.Level )! )[toilet] = state.Id;
		Assert.IsTrue( park.Economy.Catalog.TryGet( state.InfoId, out var info ) && info.ProvidesRelief );
		Assert.AreEqual( 3, toilet.Entry.WhichUIType, "a features-window object, as the shipped toilets are" );
		// the level's tick mirrors this (SyncEconomyObject): the toilet's script is hosted
		state.ScriptHosted = toilet.Runtime.Script != null;
		Assert.IsTrue( state.ScriptHosted );
		return (park.Level, park.Economy, toilet, state);
	}

	private static StaffMember HandymanClaiming( ParkEconomy economy, ParkObjectState toilet )
	{
		var handyman = EconomyTestData.HireBest( economy, StaffType.Handyman );
		handyman.ToiletId = toilet.Id;
		economy.Claim( toilet, handyman );
		return handyman;
	}

	[TestMethod]
	public void TheDoorOnlyClosesAToiletAndCallsNobody()
	{
		var (level, economy, toilet, state) = ToiletLevel();
		EconomyTestData.HireBest( economy, StaffType.Handyman );
		Assert.IsTrue( toilet.IsOpen );
		Assert.IsTrue( level.Commands.Issue( new SetAttractionOpenCommand( toilet.Runtime.AttractionId, false ) ).Accepted );
		Assert.IsFalse( toilet.IsOpen, "0x4DF300 clears the open word" );
		Assert.IsFalse( state.MaintenanceRequested, "no call: 0x4DF300 never touches +0x64" );
		Assert.AreEqual( 0, state.MechanicId, "and sends nobody" );
		Assert.AreEqual( 0, economy.Staff.Members.Count( member => member.Job == StaffJob.ToToilet ) );
	}

	[TestMethod]
	public void TheCallButtonClosesTheToiletAndMarksTheCall()
	{
		var (level, economy, toilet, state) = ToiletLevel();
		EconomyTestData.HireBest( economy, StaffType.Handyman );
		Assert.IsTrue( level.Commands.Issue( new RequestMaintenanceCommand( toilet.Runtime.AttractionId, true ) ).Accepted );
		Assert.IsTrue( state.MaintenanceRequested, "0x4DFE30 sets +0x64" );
		Assert.IsFalse( toilet.IsOpen, "and closes it" );
		Assert.AreEqual( 1, toilet.Runtime.GetVariable( RideVariables.VAR_RIDECLOSED ) );
		var noHandyman = ToiletLevel();
		Assert.IsFalse( noHandyman.Level.Commands.Issue( new RequestMaintenanceCommand( noHandyman.Toilet.Runtime.AttractionId, true ) ).Accepted,
			"no handyman hired: the call is refused (UIHELPTEXT 574)" );
	}

	[TestMethod]
	public void ADoorOfAToiletStaysRefusedWhileItsCallIsPending()
	{
		var (level, economy, toilet, state) = ToiletLevel();
		EconomyTestData.HireBest( economy, StaffType.Handyman );
		Assert.IsTrue( level.Commands.Issue( new RequestMaintenanceCommand( toilet.Runtime.AttractionId, true ) ).Accepted );
		Assert.IsFalse( level.Commands.Issue( new SetAttractionOpenCommand( toilet.Runtime.AttractionId, true ) ).Accepted, "the toilet window greys its door while the guard 0x4DF290 refuses (0x4973A0)" );
		Assert.IsFalse( toilet.IsOpen );
		Assert.IsTrue( state.MaintenanceRequested );
	}

	[TestMethod]
	public void ARidesDoorStillRefusesWhileItsCallIsPending()
	{
		var park = ParkSettingCommandLevelTests.NewPark( fixture! );
		var ride = park.Two.Runtime.AttractionId;
		Assert.IsTrue( park.Level.Commands.Issue( new RequestMaintenanceCommand( ride, true ) ).Accepted );
		Assert.IsFalse( park.Two.IsOpen );
		Assert.IsFalse( park.Level.Commands.Issue( new SetAttractionOpenCommand( ride, true ) ).Accepted, "the ride window greys its door while the guard 0x4DF290 refuses" );
		Assert.IsFalse( park.Two.IsOpen );
	}

	[TestMethod]
	public void ACleanResetsTheScriptsWornVariableAndOpensTheClosedToilet()
	{
		var (level, economy, toilet, state) = ToiletLevel();
		var handyman = HandymanClaiming( economy, state );
		// dirty and closed by the player's door, its script showing the dirty look (the economy writes VAR_WORN = 1 below a gauge byte of 25 on every update)
		state.Repair = 10;
		toilet.Runtime.SetVariable( nameof( RideVariables.VAR_WORN ), 1 );
		state.ScriptWorn = 1;
		Assert.IsTrue( level.Commands.Issue( new SetAttractionOpenCommand( toilet.Runtime.AttractionId, false ) ).Accepted );
		Assert.IsFalse( toilet.IsOpen );
		economy.FinishToiletClean( handyman );
		Assert.AreEqual( 100f, state.Repair, 0.001 );
		Assert.AreEqual( 0, toilet.Runtime.GetVariable( RideVariables.VAR_WORN ), "0x4DFD80 writes script variable 8 = 0: the toilet no longer looks dirty" );
		Assert.AreEqual( 0, state.MechanicId );
		Assert.IsFalse( state.MaintenanceRequested );
		Assert.IsTrue( toilet.IsOpen, "0x4DF390 opens it again after the clean, called or not" );
		Assert.AreEqual( 0, toilet.Runtime.GetVariable( RideVariables.VAR_RIDECLOSED ) );
		Assert.IsTrue( state.IsOpen, "the economy record follows" );
	}

	[TestMethod]
	public void ACleanAnswersTheCallAndClearsIt()
	{
		var (level, economy, toilet, state) = ToiletLevel();
		var handyman = HandymanClaiming( economy, state );
		Assert.IsTrue( level.Commands.Issue( new RequestMaintenanceCommand( toilet.Runtime.AttractionId, true ) ).Accepted );
		toilet.Runtime.SetVariable( nameof( RideVariables.VAR_WORN ), 1 );
		var answered = false;
		economy.EventRaised += item => answered |= item.Kind == ParkEventKind.ToiletCleaned && item.Amount == 1;
		economy.FinishToiletClean( handyman );
		Assert.IsTrue( answered );
		Assert.IsFalse( state.MaintenanceRequested );
		Assert.IsTrue( toilet.IsOpen );
		Assert.AreEqual( 0, toilet.Runtime.GetVariable( RideVariables.VAR_WORN ), "a called clean of a clean toilet writes the variable too (0x4DFE00 is unconditional)" );
	}

	// ------------------------------------------------------------------ the handyman's world

	private sealed class Scene
	{
		public readonly GuestPathGrid Grid = new( 24, 12 );
		public readonly ParkEconomy Park;
		public readonly StaffWorldSimulation World;
		public readonly List<ToiletSite> Toilets = new();
		public readonly StaffMember Handyman;
		public readonly ParkObjectState Toilet;
		public readonly List<(RideVariables Variable, int Value)> ScriptWrites = new();

		public Scene( ParkEconomy park )
		{
			Park = park;
			for ( var x = 0; x < 22; x++ )
				Grid.SetPath( x, 5, true );
			Park.OpenPark();
			Park.Litter.Cells = Grid.Cells;
			World = new StaffWorldSimulation( Grid, () => Park.Staff.Members, _ => null, (0, 5) ) { Toilets = () => Toilets };
			Park.StaffWorld = World;
			Handyman = EconomyTestData.HireBest( Park, StaffType.Handyman );
			World.Synchronize();
			Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, Park.TryBuild( 1402, out var toilet ) );
			Toilet = toilet!;
			Toilet.ScriptHosted = true;
			Park.EventRaised += item =>
			{
				if ( item.Kind == ParkEventKind.RideScriptVariable && item.InstanceId == Toilet.Id )
					ScriptWrites.Add( ((RideVariables)item.InfoId, (int)item.Amount) );
			};
		}

		public void Place( int x ) => Toilets.Add( new ToiletSite( Toilet.Id, (x, 6), (x, 5) ) );

		public void Turns( int turns, Action? each = null )
		{
			for ( var turn = 0; turn < turns; turn++ )
			{
				EconomyTestData.AdvanceToTurn( Park, Park.Turn + 1 );
				each?.Invoke();
			}
		}

		public int Range => Math.Min( 5, Park.Settings.HandymanDetectionRange[Handyman.Grade] );
	}

	private static void Dirty( Scene scene )
	{
		scene.Toilet.Repair = 10;
		// the economy's own update then writes VAR_WORN = 1 (0x4E0D16)
	}

	[TestMethod]
	public void AClosedDirtyToiletInRangeIsCleanedWithoutACallAndItsWornVariableGoesBackToZero()
	{
		var scene = new Scene( EconomyTestData.Park() );
		scene.Place( 2 );
		Dirty( scene );
		scene.Toilet.IsOpen = false;
		Assert.IsFalse( scene.Toilet.MaintenanceRequested, "the player closed it with the door only" );
		var cleaned = false;
		scene.Park.EventRaised += item => cleaned |= item.Kind == ParkEventKind.ToiletCleaned && item.Amount == 0;
		scene.Turns( 600 );
		Assert.IsTrue( cleaned, "a dirty toilet within DetectionRange is cleaned whatever its door says (0x4D7880 never reads the open word)" );
		Assert.AreEqual( 100f, scene.Toilet.Repair, 0.001 );
		Assert.IsTrue( scene.ScriptWrites.Contains( (RideVariables.VAR_WORN, 1) ), "it was reported worn first" );
		Assert.AreEqual( (RideVariables.VAR_WORN, 0), scene.ScriptWrites.Last( write => write.Variable == RideVariables.VAR_WORN ), "and the clean's write comes last" );
		Assert.AreEqual( 0, scene.Toilet.ScriptWorn );
	}

	[TestMethod]
	public void ACleanClosedToiletInRangeSendsNobodyUntilItIsCalled()
	{
		var scene = new Scene( EconomyTestData.Park() );
		scene.Place( 2 );
		scene.Toilet.IsOpen = false;
		scene.Turns( 200 );
		Assert.AreEqual( 0, scene.Toilet.MechanicId, "closing alone dispatches nobody" );
		Assert.AreEqual( 0, scene.ScriptWrites.Count( write => write.Value == 0 ), "and nothing is cleaned" );
		scene.Park.RequestMaintenance( scene.Toilet.Id, true );
		var answered = false;
		scene.Park.EventRaised += item => answered |= item.Kind == ParkEventKind.ToiletCleaned && item.Amount == 1;
		scene.Turns( 600 );
		Assert.IsTrue( answered, "the call button brings the handyman" );
		Assert.AreEqual( (RideVariables.VAR_WORN, 0), scene.ScriptWrites.Last(), "his clean resets VAR_WORN even though the toilet was clean" );
	}

	[TestMethod]
	public void AHandymanLeavesADirtyToiletOutOfHisRangeAloneUntilItIsCalled()
	{
		var scene = new Scene( EconomyTestData.Park() );
		scene.Place( 20 );
		Dirty( scene );
		Assert.IsTrue( 20 > scene.Range + 2, "the toilet is beyond his detection range from the gate" );
		scene.Turns( 80 );
		Assert.AreEqual( 0, scene.Toilet.MechanicId );
		scene.Park.RequestMaintenance( scene.Toilet.Id, true );
		scene.Turns( 800 );
		Assert.AreEqual( 100f, scene.Toilet.Repair, 0.001, "a call reaches him at any distance (0x4D7880)" );
	}

	[TestMethod]
	public void TheHeadlessDispatchResetsTheWornVariableToo()
	{
		var park = EconomyTestData.Park();
		park.OpenPark();
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1402, out var toilet ) );
		toilet!.ScriptHosted = true;
		toilet.Repair = 10;
		var writes = new List<int>();
		park.EventRaised += item => { if ( item.Kind == ParkEventKind.RideScriptVariable && item.InfoId == (int)RideVariables.VAR_WORN ) writes.Add( (int)item.Amount ); };
		EconomyTestData.HireBest( park, StaffType.Handyman );
		EconomyTestData.AdvanceToTurn( park, park.Turn + 400 );
		Assert.AreEqual( 100f, toilet.Repair, 0.001 );
		Assert.IsTrue( writes.Contains( 1 ), "reported worn first" );
		Assert.AreEqual( 0, writes[^1], "the test and tool economy's handyman ends his clean with VAR_WORN = 0" );
	}

	[TestMethod]
	public void ACleanedToiletStaysCleanInTheSave()
	{
		var scene = new Scene( EconomyTestData.Park() );
		scene.Place( 2 );
		Dirty( scene );
		scene.Turns( 600 );
		Assert.AreEqual( 100f, scene.Toilet.Repair, 0.001 );
		var json = ParkSaveFile.Serialize( scene.Park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), scene.Park.Settings, scene.Park.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ), "no new field: the save round-trips exactly" );
		Assert.IsTrue( restored.TryGetObject( scene.Toilet.Id, out var loaded ) );
		Assert.AreEqual( 100f, loaded.Repair, 0.001 );
		Assert.IsFalse( loaded.MaintenanceRequested );
	}

	// ------------------------------------------------------------------ with the original data

	private void UseOriginalData()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		dataInUse = true;
	}

	/// <summary>
	/// The reported flow on the shipped Jungle balance and objects: the player closes a dirty small toilet with its door, a handyman of the shipped grades finds it within his
	/// DetectionRange, cleans it for WorkDuration turns from his arrival, and the toilet reopens with its gauge full and its worn variable reset.
	/// </summary>
	[TestMethod]
	public void WithTheShippedJungleDataAClosedDirtyToiletIsCleanedByAPassingHandymanAndReopens()
	{
		UseOriginalData();
		var settings = BalanceSettings.Load( "jungle", easy: false );
		var catalog = EconomyObjectCatalog.Load( "jungle", easy: false );
		Assert.IsTrue( catalog.TryGet( 1402, out var small ) && small.ProvidesRelief );
		var scene = new Scene( new ParkEconomy( settings, catalog, ParkGameMode.FullSimulation, 7 ) );
		var work = scene.Park.Settings[StaffType.Handyman].WorkDuration[scene.Handyman.Grade];
		scene.Place( 2 );
		Dirty( scene );
		scene.Toilet.IsOpen = false;
		long started = -1, finished = -1;
		scene.Turns( 800, () =>
		{
			if ( started < 0 && scene.Handyman.Job == StaffJob.Cleaning )
				started = scene.Park.Turn;
			if ( finished < 0 && scene.Toilet.Repair == 100 )
				finished = scene.Park.Turn;
		} );
		Assert.IsTrue( started > 0, "the handyman reached the toilet" );
		Assert.AreEqual( started + work + 1, finished, "the clean ends when the turn passes the arrival stamp + WorkDuration (0x4D73C0 state 11)" );
		Assert.AreEqual( (RideVariables.VAR_WORN, 0), scene.ScriptWrites.Last( write => write.Variable == RideVariables.VAR_WORN ) );
		Assert.AreEqual( 0, scene.Toilet.MechanicId );
		Assert.IsFalse( scene.Toilet.MaintenanceRequested );
	}
}
