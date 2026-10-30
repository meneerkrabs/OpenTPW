using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Actual Level economy synchronization over synthetic CPU runtimes, without GPU or original data.</summary>
[TestClass]
[DoNotParallelize]
public class PrototypeEconomySyncTests
{
	private string root = null!;
	private Level level = null!;
	private ParkEconomy economy = null!;
	private PrototypeRide prototype = null!;
	private OriginalObject ordinary = null!;
	private ParkObjectState prototypeState = null!;
	private ParkObjectState ordinaryState = null!;

	[TestInitialize]
	public void Initialize()
	{
		Log = new Logger();
		root = Path.Combine( Path.GetTempPath(), "opentpw-prototype-economy-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		File.WriteAllBytes( Path.Combine( root, "model.MD2" ), Md2ModelFileTests.CreateGeometry() );
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.END ).Build(
			Enum.GetNames<RideVariables>() ) );
		var catalog = EconomyTestData.Catalog();
		var totem = catalog.Objects.First( item => item.InfoId == 1100 ) with { InfoId = PrototypeRide.InfoId };
		economy = new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( catalog.Objects.Append( totem ) ), ParkGameMode.FullSimulation, 7 );
		level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		SetProperty( level, "Park", ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) );
		var world = new RideScriptWorld();
		ordinary = Wrap<OriginalObject>( Runtime( 1100, world ) );
		prototype = Wrap<PrototypeRide>( Runtime( PrototypeRide.InfoId, world ) );
		SetProperty( level, "PlacedRide", prototype );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( 1100, out var bought ) );
		ordinaryState = bought!;
		Field( typeof( Level ), "economyInstances" ).SetValue( level, new Dictionary<OriginalObject, int> { [ordinary] = ordinaryState.Id } );
		Invoke( "LinkPrototypeToEconomy", prototype );
		prototypeState = economy.Objects.Single( item => item.InfoId == PrototypeRide.InfoId );
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete( root, recursive: true );

	[TestMethod]
	public void PrototypeAndOrdinaryObjectsMirrorTheSameLiveEconomyFields()
	{
		foreach ( var (item, state) in new[] { ((OriginalObject)prototype, prototypeState), (ordinary, ordinaryState) } )
		{
			item.Close();
			Assert.IsTrue( item.Runtime.SetVariable( nameof( RideVariables.VAR_BROKEN ), 1 ) );
			state.Repair = 23.5;
			state.Price = 37;
			state.CostOfGoods = 12;
			state.ChanceOfLosingPercent = 61;
		}
		EconomyTestData.AdvanceToTurn( economy, 300 );
		var balance = economy.Balance;
		var instances = economy.Objects.Select( state => state.Id ).ToArray();
		Invoke( "SyncObjectEconomy" );
		foreach ( var (item, state) in new[] { ((OriginalObject)prototype, prototypeState), (ordinary, ordinaryState) } )
		{
			Assert.IsFalse( state.IsOpen );
			Assert.IsTrue( state.ScriptBroken );
			Assert.AreEqual( RideObjectStates.Idle, ParkEconomy.ObjectState( state ), "the variable alone is not the state word: the ride update has not run" );
			item.Visitors.StateWord.Set( RideObjectStates.BrokenDown ); // the ride update entered state 1 (0x4e14e0) on the shared word
			Assert.AreEqual( RideObjectStates.BrokenDown, ParkEconomy.ObjectState( state ) );
			Assert.AreEqual( 23.5, item.Visitors.WearGauge );
			Assert.AreEqual( new SideshowTerms( 12, 37, 61 ), item.Visitors.Sideshow );
			Assert.AreEqual( economy.ObjectAgeDays( state ), item.Visitors.AgeDays );
			Assert.IsTrue( item.Visitors.AgeDays > 0 );
			item.Open();
			item.Runtime.SetVariable( nameof( RideVariables.VAR_BROKEN ), 0 );
			state.Repair = RideRepair.FullGauge;
		}
		Invoke( "SyncObjectEconomy" );
		foreach ( var (item, state) in new[] { ((OriginalObject)prototype, prototypeState), (ordinary, ordinaryState) } )
		{
			Assert.IsTrue( state.IsOpen );
			Assert.IsFalse( state.ScriptBroken );
			Assert.AreEqual( RideObjectStates.Idle, ParkEconomy.ObjectState( state ) );
			Assert.AreEqual( RideRepair.FullGauge, item.Visitors.WearGauge );
		}
		CollectionAssert.AreEqual( instances, economy.Objects.Select( state => state.Id ).ToArray(), "sync never registers or purchases" );
		Assert.AreEqual( balance, economy.Balance, "sync never charges or sells" );
	}

	[TestMethod]
	public void RealEconomyWearBreakdownAndRepairReachThePrototypeBridge()
	{
		var operations = new EconomyTestData.RideOperations();
		economy.RideOperations = operations;
		operations.Set( prototypeState.Id, new RideOperation( true, 5, 50 ) );
		operations.Set( ordinaryState.Id, new RideOperation( true, 5, 50 ) );
		EconomyTestData.AdvanceToTurn( economy, ParkEconomy.WearInterval );
		Invoke( "SyncObjectEconomy" );
		Assert.IsTrue( prototype.Visitors.WearGauge < 100 );
		Assert.AreEqual( ordinary.Visitors.WearGauge, prototype.Visitors.WearGauge );
		var writes = new List<ParkEvent>();
		economy.EventRaised += item => { if ( item.Kind == ParkEventKind.RideScriptVariable && item.InstanceId == prototypeState.Id ) writes.Add( item ); };
		EconomyTestData.AdvanceToTurn( economy, 26 * ParkEconomy.WearInterval );
		Invoke( "SyncObjectEconomy" );
		// [BIN:TPW-EXE:0x4e0b90 breakdown check] a script-hosted ride only writes VAR_BREAKSTAT = 1; it breaks down when the ride update reads the script's VAR_BROKEN (state 1)
		Assert.IsTrue( writes.Any( item => item.InfoId == (int)RideVariables.VAR_BREAKSTAT && item.Amount == 1 ) );
		Assert.IsFalse( prototypeState.IsBrokenDown, "state 0 until the script reports VAR_BROKEN" );
		Assert.AreEqual( 0d, prototype.Visitors.WearGauge );
		prototype.Runtime.SetVariable( nameof( RideVariables.VAR_BROKEN ), 1 );
		prototype.Visitors.StateWord.Set( RideObjectStates.BrokenDown ); // the ride update entered state 1 (0x4e14e0) on the word it shares with the economy record
		Invoke( "SyncObjectEconomy" );
		Assert.IsTrue( prototypeState.IsBrokenDown, "state 1 breaks the object down and calls a mechanic" );
		EconomyTestData.HireBest( economy, StaffType.Mechanic );
		for ( var turn = 0; turn < 250 && prototypeState.MechanicId == 0; turn++ )
			EconomyTestData.AdvanceToTurn( economy, economy.Turn + 1 );
		for ( var turn = 0; turn < 250 && !writes.Any( item => item.InfoId == (int)RideVariables.VAR_BREAKSTAT && item.Amount == 0 ); turn++ )
			EconomyTestData.AdvanceToTurn( economy, economy.Turn + 1 );
		EconomyTestData.AdvanceToTurn( economy, economy.Turn + 5 );
		Assert.IsTrue( prototypeState.IsBrokenDown, "0x4da830: the repair waits while the script still reports VAR_BROKEN (its fix animation)" );
		prototype.Runtime.SetVariable( nameof( RideVariables.VAR_BROKEN ), 0 );
		Invoke( "SyncObjectEconomy" );
		EconomyTestData.AdvanceToTurn( economy, economy.Turn + 2 );
		Assert.IsFalse( prototypeState.IsBrokenDown, "the existing economy mechanic completes the repair" );
		Assert.IsTrue( writes.Any( item => item.InfoId == (int)RideVariables.VAR_WORN && item.Amount == 0 ), "0x4df8f0 clears VAR_WORN" );
		Invoke( "SyncObjectEconomy" );
		Assert.AreEqual( RideRepair.FullGauge, prototype.Visitors.WearGauge );
	}

	[TestMethod]
	public void RemovingPrototypeSellsOnceAndStopsSynchronization()
	{
		var balance = economy.Balance;
		var scrap = economy.ScrapValue( prototypeState );
		Invoke( "RemovePrototypeFromEconomy", prototype );
		Invoke( "RemovePrototypeFromEconomy", prototype );
		Assert.AreEqual( balance + scrap, economy.Balance );
		Assert.IsFalse( economy.TryGetObject( prototypeState.Id, out _ ) );
		prototype.Visitors.WearGauge = 47;
		prototypeState.Repair = 0;
		Invoke( "SyncObjectEconomy" );
		Assert.AreEqual( 47d, prototype.Visitors.WearGauge, "an unlinked prototype has no economy state to mirror" );
		Assert.AreEqual( 1, economy.Objects.Count );
		Assert.AreEqual( ordinaryState.Id, economy.Objects.Single().Id );
	}

	[TestMethod]
	public void ReadOnlyRemovalLeavesPrototypeAndEconomyUntouched()
	{
		SetProperty( level, "Visit", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		var balance = economy.Balance;
		Assert.IsFalse( level.RemoveAttraction( prototype ) );
		level.RemoveRide();
		Assert.AreSame( prototype, level.PlacedRide );
		Assert.IsFalse( prototype.IsDeleted );
		Assert.AreEqual( balance, economy.Balance );
		Assert.IsTrue( economy.TryGetObject( prototypeState.Id, out var state ) );
		Assert.AreSame( prototypeState, state );
	}

	[TestMethod]
	public void DistinctEconomyRecordsStayWithTheirOwnRuntimeAndBridge()
	{
		prototype.Close();
		prototype.Runtime.SetVariable( nameof( RideVariables.VAR_BROKEN ), 1 );
		ordinary.Open();
		ordinary.Runtime.SetVariable( nameof( RideVariables.VAR_BROKEN ), 0 );
		prototypeState.Repair = 17.25;
		prototypeState.Price = 31;
		prototypeState.CostOfGoods = 11;
		prototypeState.ChanceOfLosingPercent = 51;
		ordinaryState.Repair = 83.75;
		ordinaryState.Price = 42;
		ordinaryState.CostOfGoods = 22;
		ordinaryState.ChanceOfLosingPercent = 62;
		var balance = economy.Balance;
		Invoke( "SyncObjectEconomy" );
		Invoke( "SyncObjectEconomy" );
		Assert.AreEqual( 17.25, prototype.Visitors.WearGauge );
		Assert.AreEqual( 83.75, ordinary.Visitors.WearGauge );
		Assert.AreEqual( new SideshowTerms( 11, 31, 51 ), prototype.Visitors.Sideshow );
		Assert.AreEqual( new SideshowTerms( 22, 42, 62 ), ordinary.Visitors.Sideshow );
		Assert.IsFalse( prototypeState.IsOpen );
		Assert.IsTrue( prototypeState.ScriptBroken );
		Assert.IsTrue( ordinaryState.IsOpen );
		Assert.IsFalse( ordinaryState.ScriptBroken );
		Assert.AreEqual( balance, economy.Balance );
		Assert.AreEqual( 2, economy.Objects.Count );
	}

	[TestMethod]
	public void MissingPrototypeRecordNeverBindsAReplacementOfTheSameInfoId()
	{
		economy.Remove( prototypeState.Id );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.TryBuild( PrototypeRide.InfoId, out var replacement ) );
		Assert.AreNotEqual( prototypeState.Id, replacement!.Id );
		replacement.Repair = 5;
		prototype.Visitors.WearGauge = 79;
		prototype.Close();
		var balance = economy.Balance;
		Invoke( "SyncObjectEconomy" );
		Assert.AreEqual( 79d, prototype.Visitors.WearGauge );
		Assert.IsTrue( replacement.IsOpen, "a stale binding must not follow an Info.Id match" );
		Assert.AreEqual( 5d, replacement.Repair );
		Assert.AreEqual( balance, economy.Balance );
		Assert.AreEqual( 2, economy.Objects.Count );
	}

	[TestMethod]
	public void DeletedAbsentOrUnlinkedPrototypeLeavesItsBridgeAndRecordUntouched()
	{
		prototype.Visitors.WearGauge = 79;
		prototypeState.Repair = 5;
		prototype.Close();
		Field( typeof( OriginalObject ), "deleted" ).SetValue( prototype, true );
		Invoke( "SyncObjectEconomy" );
		Assert.AreEqual( 79d, prototype.Visitors.WearGauge );
		Assert.IsTrue( prototypeState.IsOpen );
		Field( typeof( OriginalObject ), "deleted" ).SetValue( prototype, false );
		SetProperty( level, "PlacedRide", null! );
		Invoke( "SyncObjectEconomy" );
		Assert.IsTrue( prototypeState.IsOpen );
		SetProperty( level, "PlacedRide", prototype );
		Field( typeof( Level ), "prototypeInstance" ).SetValue( level, null );
		Invoke( "SyncObjectEconomy" );
		Assert.AreEqual( 79d, prototype.Visitors.WearGauge );
		Assert.IsTrue( prototypeState.IsOpen );
		Assert.AreEqual( 2, economy.Objects.Count );
	}

	[TestMethod]
	public void LevelSyncUsesEconomicCoasterPredicateAndTracksToolIdentity()
	{
		var info = EconomyTestData.Catalog().Objects.First( item => item.InfoId == 1100 ) with { TrackType = 3 };
		economy = new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( new[] { info, info with { InfoId = PrototypeRide.InfoId } } ), ParkGameMode.FullSimulation, 7 );
		ordinaryState = economy.RegisterExisting( 1100 );
		prototypeState = economy.RegisterExisting( PrototypeRide.InfoId );
		SetProperty( level, "Park", ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) );
		Field( typeof( Level ), "economyInstances" ).SetValue( level, new Dictionary<OriginalObject, int> { [ordinary] = ordinaryState.Id } );
		Field( typeof( Level ), "prototypeInstance" ).SetValue( level, prototypeState.Id );
		var (built, _) = CoasterCollisionTests.Build( CoasterTests.Circuit, 20 );
		var ride = new CoasterRide( built.Definition, new CoasterWorld( 1 ) );
		ride.Track.RestoreSnapshot( CoasterSave.CaptureRide( built, 7 ).Track );
		Field( typeof( OriginalObjectRuntime ), "<Coaster>k__BackingField" ).SetValue( ordinary.Runtime, ride );
		Field( typeof( OriginalObjectRuntime ), "<Coaster>k__BackingField" ).SetValue( prototype.Runtime, built );
		Invoke( "SyncObjectEconomy" );
		Assert.IsTrue( ordinaryState.CoasterAcceptsGuests, "actual level sync credits a complete controller before samples" );
		Assert.IsFalse( ride.AcceptsGuests );
		var bothPoints = economy.ParkPoints();
		Assert.IsTrue( bothPoints > 0 );
		// [EXT:online-coop-commands] the coaster tool's entry and exit set the being-edited mark through their commands (BeginCoasterToolCommand, EndCoasterToolCommand); the test moves it as they do
		void Mark( OriginalObject? editing )
		{
			foreach ( var item in new OriginalObject[] { ordinary, prototype } )
				if ( item.Runtime.Coaster is { } coaster )
					coaster.BeingEdited = ReferenceEquals( item, editing );
		}
		Mark( ordinary );
		Invoke( "SyncObjectEconomy" );
		Assert.IsFalse( ordinaryState.CoasterAcceptsGuests );
		Assert.IsTrue( prototypeState.CoasterAcceptsGuests );
		Assert.AreEqual( bothPoints / 2, economy.ParkPoints() );
		Mark( prototype );
		Invoke( "SyncObjectEconomy" );
		Assert.IsTrue( ordinaryState.CoasterAcceptsGuests );
		Assert.IsFalse( prototypeState.CoasterAcceptsGuests );
		Assert.AreEqual( bothPoints / 2, economy.ParkPoints() );
		Mark( null );
		Invoke( "SyncObjectEconomy" );
		Assert.IsTrue( ordinaryState.CoasterAcceptsGuests && prototypeState.CoasterAcceptsGuests );
		Assert.AreEqual( bothPoints, economy.ParkPoints() );
		Field( typeof( OriginalObjectRuntime ), "<Coaster>k__BackingField" ).SetValue( ordinary.Runtime, null );
		Invoke( "SyncObjectEconomy" );
		Assert.IsFalse( ordinaryState.CoasterAcceptsGuests, "missing controller fails the actual sync" );
		Assert.AreEqual( bothPoints / 2, economy.ParkPoints() );
	}

	private OriginalObjectRuntime Runtime( int infoId, RideScriptWorld world )
	{
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {infoId}\nInfo.Name Synthetic\nInfo.IsChoosable 1\n", "synthetic.sam" ) } );
		var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "synthetic", settings, new ObjectShape( new[] { "*" } ),
			"/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(),
			Array.Empty<string>(), new BaseFileSystem( root ), null );
		return new OriginalObjectRuntime( entry, world, seed: 1, open: true );
	}

	// Renderer shells carry real runtime/bridge state; the reviewed operation needs no GPU resources.
	private static T Wrap<T>( OriginalObjectRuntime runtime ) where T : OriginalObject
	{
		var item = (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
		Field( typeof( OriginalObject ), "<Runtime>k__BackingField" ).SetValue( item, runtime );
		return item;
	}

	private static FieldInfo Field( Type type, string name ) => type.GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!;
	private static void SetProperty( object target, string name, object value ) => Field( typeof( Level ), $"<{name}>k__BackingField" ).SetValue( target, value );
	private void Invoke( string name, params object[] arguments ) => typeof( Level ).GetMethod( name, BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( level, arguments );
}
