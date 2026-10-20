using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class ImportedBrokenRideStateTests
{
	private GameEdition edition;
	[TestInitialize]
	public void Setup() { edition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkWorld ); Log ??= new(); }
	[TestCleanup]
	public void Cleanup() => GameEditions.Select( edition );

	private static (SaveAttraction Saved, ParkEconomy Economy, ParkObjectState State) Import( int state, int? lifeGauge = null )
	{
		var settings = OriginalSaveImportTests.Settings();
		var fields = new SaveTestPayloads.AttractionFields { PositionX = 384, PositionY = 384, Price = 30, Repair = 80, LifeGauge = lifeGauge ?? (state == 4 ? 0 : 90), State = checked((uint)state), CanLoad = 0 };
		var save = OriginalSaveImportTests.Park( settings, bellyBounce: SaveTestPayloads.Attraction( fields ) );
		var economy = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		var import = OriginalEconomyImport.Apply( economy, save );
		var saved = save.Attractions.Single( item => item.InfoId == 1100 );
		Assert.IsTrue( economy.TryGetObject( import.AttractionInstances[saved.Id], out var linked ) );
		return (saved, economy, linked);
	}

	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( 4 )]
	public void DecodedRetainedStateAttachesWithoutReplayingTheBreakdown( int state )
	{
		var w = Import( state );
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		var bridge = runtime.Visitors;
		Assert.AreEqual( 0, runtime.GetVariable( RideVariables.VAR_BROKEN ) );
		Assert.IsFalse( bridge.InBrokenState, "The freshly constructed bridge has no original retained state." );
		var script = ParkResumeJson.Serialize( runtime.Script!.CaptureSnapshot() );
		w.Economy.ObserveScriptState( w.State, bridge.InBrokenState );
		var events = 0;
		w.Economy.EventRaised += _ => events++;
		Assert.IsNull( Level.RestoreOriginalRetainedRideState( w.Saved, w.State, runtime, w.Saved.Id ) );
		Assert.IsTrue( bridge.InBrokenState && bridge.IsBrokenDown );
		Assert.AreEqual( state == 4, bridge.IsCondemned );
		Assert.AreEqual( state == 1, bridge.HoldsQueuedGuests );
		Assert.AreEqual( w.State.LifeGauge, bridge.LifeGauge );
		w.Economy.ObserveScriptState( w.State, bridge.InBrokenState );
		Assert.AreEqual( 0, events );
		CollectionAssert.AreEqual( script, ParkResumeJson.Serialize( runtime.Script.CaptureSnapshot() ) );

		// Original queued guests are currently omitted by original import. These are newly added synthetic
		// heads, testing retained-state drainage independently of any claim to restored original queue links.
		Assert.IsTrue( bridge.TryJoinQueue( 100 ) );
		Assert.IsTrue( bridge.TryJoinQueue( 101 ) );
		Assert.IsNull( Level.RestoreOriginalRetainedRideState( w.Saved, w.State, runtime, w.Saved.Id ) );
		bridge.UpdateAdmission();
		Assert.AreEqual( 1, bridge.Queue.Count() );
		Assert.IsTrue( bridge.InBrokenState );
		runtime.Open();
		bridge.UpdateAdmission();
		Assert.IsFalse( bridge.InBrokenState || bridge.IsCondemned );
		w.Economy.ObserveScriptState( w.State, bridge.InBrokenState );
		Assert.AreEqual( 0, events, "Reopening does not replay a breakdown." );
	}

	[DataTestMethod]
	[DataRow( "thing" )]
	[DataRow( "info" )]
	[DataRow( "open-word" )]
	[DataRow( "script-open" )]
	[DataRow( "idle" )]
	[DataRow( "inert" )]
	[DataRow( "tpi" )]
	public void UnsupportedAttachmentLeavesAnExistingRetainedStateUntouched( string failure )
	{
		var w = Import( 4 );
		using var fixture = new RuntimeFixture();
		var runtime = fixture.Runtime;
		Assert.IsNull( Level.RestoreOriginalRetainedRideState( w.Saved, w.State, runtime, w.Saved.Id ) );
		var saved = w.Saved;
		var thingId = saved.Id;
		if ( failure == "thing" ) thingId++;
		if ( failure == "info" ) saved = saved with { InfoId = saved.InfoId + 1 };
		if ( failure == "open-word" ) saved = saved with { CanLoad = 1 };
		if ( failure == "script-open" ) runtime.Open();
		if ( failure == "idle" ) saved = saved with { State = 0 };
		if ( failure == "inert" ) saved = saved with { State = 3 };
		if ( failure == "tpi" ) GameEditions.Select( GameEdition.ThemeParkInc );
		var before = ParkResumeJson.Serialize( runtime.CaptureSnapshot() );
		var economy = ParkResumeJson.Serialize( ParkSaveFile.Capture( w.Economy ) );
		Assert.IsNotNull( Level.RestoreOriginalRetainedRideState( saved, w.State, runtime, thingId ) );
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( runtime.CaptureSnapshot() ) );
		CollectionAssert.AreEqual( economy, ParkResumeJson.Serialize( ParkSaveFile.Capture( w.Economy ) ) );
	}

	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( 4 )]
	public void AttachedStateSurvivesJsonResumeWithoutReattachment( int state )
	{
		var w = Import( state );
		using var fixture = new RuntimeFixture();
		Assert.IsNull( Level.RestoreOriginalRetainedRideState( w.Saved, w.State, fixture.Runtime, w.Saved.Id ) );
		// Production registration computes queue geometry before a park save can be captured.
		fixture.Runtime.Visitors.RecomputeQueue( new GuestPathGrid( 3, 3 ) );
		Assert.AreEqual( 0, RideVisitorBridge.ValidateSnapshot( fixture.Runtime.Visitors.CaptureSnapshot() ).Count );
		var snapshot = ParkResumeJson.Deserialize<OriginalObjectRuntimeSnapshot>( ParkResumeJson.Serialize( fixture.Runtime.CaptureSnapshot() ) );
		// Production saves retain visitor bridges in the guest slice, separate from the object/script slice.
		var bridgeSnapshot = ParkResumeJson.Deserialize<RideVisitorBridgeSnapshot>( ParkResumeJson.Serialize( fixture.Runtime.Visitors.CaptureSnapshot() ) );
		using var restored = new RuntimeFixture();
		Assert.AreEqual( 0, restored.Runtime.RestoreSnapshot( snapshot ).Count );
		restored.Runtime.Visitors.RestoreSnapshot( bridgeSnapshot );
		var park = ParkSaveFile.Restore( ParkResumeJson.Deserialize<ParkSaveFile.Data>( ParkResumeJson.Serialize( ParkSaveFile.Capture( w.Economy ) ) ), w.Economy.Settings, w.Economy.Catalog );
		var loaded = park.Objects.Single( item => item.Id == w.State.Id );
		var events = 0;
		park.EventRaised += _ => events++;
		park.ObserveScriptState( loaded, restored.Runtime.Visitors.InBrokenState );
		Assert.AreEqual( state == 4, restored.Runtime.Visitors.IsCondemned );
		Assert.AreEqual( state == 1, restored.Runtime.Visitors.HoldsQueuedGuests );
		Assert.AreEqual( 0, events );
		CollectionAssert.AreEqual( ParkResumeJson.Serialize( fixture.Runtime.Visitors.CaptureSnapshot() ), ParkResumeJson.Serialize( restored.Runtime.Visitors.CaptureSnapshot() ) );
		CollectionAssert.AreEqual( ParkResumeJson.Serialize( fixture.Runtime.CaptureSnapshot() ), ParkResumeJson.Serialize( restored.Runtime.CaptureSnapshot() ) );
	}

	[TestMethod]
	public void MissingScriptIsDiagnosedWithoutChangingTheBridgeOrEconomy()
	{
		var w = Import( 1 );
		using var fixture = new RuntimeFixture( hasScript: false );
		var before = ParkResumeJson.Serialize( fixture.Runtime.CaptureSnapshot() );
		Assert.IsNotNull( Level.RestoreOriginalRetainedRideState( w.Saved, w.State, fixture.Runtime, w.Saved.Id ) );
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( fixture.Runtime.CaptureSnapshot() ) );
		Assert.IsNull( w.State.ScriptStateBroken );
	}

	[TestMethod]
	public void ExistingGaugeNormalizationAlsoControlsTheBridgeClassification()
	{
		var w = Import( 4, lifeGauge: 90 );
		using var fixture = new RuntimeFixture();
		// The existing importer retains the gauge-based state on inconsistent original records.
		Assert.IsNull( Level.RestoreOriginalRetainedRideState( w.Saved, w.State, fixture.Runtime, w.Saved.Id ) );
		Assert.IsTrue( fixture.Runtime.Visitors.HoldsQueuedGuests );
		Assert.IsFalse( fixture.Runtime.Visitors.IsCondemned );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	public void ProductionOwnerSelectionRequiresExactlyOneMappedRuntime( int matches )
	{
		var w = Import( 1 );
		using var fixture = new RuntimeFixture();
		using var unrelated = new RuntimeFixture();
		var existing = Import( 4 );
		Assert.IsNull( Level.RestoreOriginalRetainedRideState( existing.Saved, existing.State, fixture.Runtime, existing.Saved.Id ) );
		var before = ParkResumeJson.Serialize( fixture.Runtime.CaptureSnapshot() );
		var otherBefore = ParkResumeJson.Serialize( unrelated.Runtime.CaptureSnapshot() );
		var candidates = Enumerable.Repeat( (fixture.Runtime, w.Saved.Id, w.State.Id), matches )
			.Append( (unrelated.Runtime, w.Saved.Id, w.State.Id + 1) );
		var reason = Level.RestoreOriginalRetainedRideOwner( w.Saved, w.State, candidates );
		Assert.AreEqual( matches == 1, reason == null );
		Assert.IsTrue( fixture.Runtime.Visitors.InBrokenState );
		Assert.AreEqual( matches != 1, fixture.Runtime.Visitors.IsCondemned );
		if ( matches != 1 )
		{
			CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( fixture.Runtime.CaptureSnapshot() ) );
			Assert.IsNull( w.State.ScriptStateBroken );
		}
		CollectionAssert.AreEqual( otherBefore, ParkResumeJson.Serialize( unrelated.Runtime.CaptureSnapshot() ) );
	}

	[TestMethod]
	public void ACorrectSavedAndEconomyIdentityCannotAttachToADifferentRuntimeType()
	{
		var w = Import( 1 );
		using var fixture = new RuntimeFixture( infoId: 1110 );
		var before = ParkResumeJson.Serialize( fixture.Runtime.CaptureSnapshot() );
		Assert.IsNotNull( Level.RestoreOriginalRetainedRideState( w.Saved, w.State, fixture.Runtime, w.Saved.Id ) );
		CollectionAssert.AreEqual( before, ParkResumeJson.Serialize( fixture.Runtime.CaptureSnapshot() ) );
		Assert.IsNull( w.State.ScriptStateBroken );
	}

	private sealed class RuntimeFixture : IDisposable
	{
		private readonly string root = Path.Combine( Path.GetTempPath(), "opentpw-imported-broken-" + Guid.NewGuid().ToString( "N" ) );
		public OriginalObjectRuntime Runtime { get; }
		public RuntimeFixture( bool hasScript = true, int infoId = 1100 )
		{
			Directory.CreateDirectory( root );
			var bytes = Md2ModelFileTests.CreateGeometry();
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x42 ), 1 ); BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x44 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x268 ), 0 ); BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x78 ), 0x264 );
			foreach ( var at in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } ) BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( at ), 0 );
			File.WriteAllBytes( Path.Combine( root, "model.md2" ), bytes );
			File.WriteAllBytes( Path.Combine( root, "script.rse" ), new RideVMTests.Asm().Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( Enum.GetNames<RideVariables>() ) );
			var settings = new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {infoId}\nInfo.Name Broken\nInfo.IsChoosable 1\nInfo.HasQueue 1\nUpgrades[0].InitCapacity 2" ) } );
			var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/broken.wad", settings, new ObjectShape( new[] { "*" } ), "/model.md2", null, hasScript ? "/script.rse" : null, Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
			Runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 1, open: false );
			Runtime.Script?.Advance( FixedStepClock.TickDuration );
		}
		public void Dispose() { Runtime.Stop(); Directory.Delete( root, true ); }
	}
}
