using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Actual level scheduling, load and teardown seams with synthetic CPU scenes and no original assets or graphics device.</summary>
[TestClass, DoNotParallelize]
public class StaffSpriteLevelIntegrationTests
{
	private Renderer previousRenderer = null!;
	private Logger previousLog = null!;
	[TestInitialize] public void Setup() { previousRenderer = Global.Render; previousLog = Log; Log ??= new(); Global.Render = Empty<Renderer>(); }
	[TestCleanup] public void Cleanup() { Global.Render = previousRenderer; Log = previousLog; }
	private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject( typeof( T ) );
	private static void Set( object owner, string name, object value ) => owner.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( owner, value );

	private sealed class Scene : IDisposable
	{
		internal readonly Level Level = Empty<Level>();
		internal readonly GuestSimulation Guests = new( GuestTests.CreateCross(), GuestTests.CreateSettings(), 1 ) { ArrivalsEnabled = false };
		internal ParkEconomy Economy => Level.Park!.Economy;
		internal readonly StaffMember Member;
		internal StaffSpritePresentation Presentation => Level.StaffSprites!;
		internal readonly EmployeePortraitOwner Window;
		internal Scene( bool pooled = true )
		{
			var economy = EconomyTestData.Park(); var pool = new ThingPool(); pool.AllocateWorldSetup();
			if ( pooled ) Guests.AttachThings( pool ); economy.Staff.AttachThings( pool );
			Set( Level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) );
			Set( Level, "<Guests>k__BackingField", Guests ); Set( Level, "<Objects>k__BackingField", new ParkObjects( null!, new SandboxParkGrid() ) );
			Set( Level, "<Seed>k__BackingField", WorldSeed.Default ); Set( Level, "economyInstances", new Dictionary<OriginalObject, int>() );
			Set( Level, "simulationClock", new FixedStepClock() ); Set( Level, "things", pool );
			Set( Level, "trackRenderers", new Dictionary<TrackFamily, TrackRenderer>() );
			Level.Streams.LinkGenerator();
			Member = EconomyTestData.HireBest( economy, StaffType.Handyman ); Member.CellX = 4; Member.CellY = 5;
			var presentation = new StaffSpritePresentation( StaffOwnerFixture.Catalog(), () => Economy.Staff.Members ); presentation.AttachRoster( economy.Staff );
			Set( Level, "<StaffSprites>k__BackingField", presentation ); Member.BodySprite!.EnsureInitialized();
			Window = new EmployeePortraitOwner( presentation ); Window.FullRefresh( Member, -498f / 1024, 292f / 768, 39f / 40 );
		}
		public void Dispose() { Window.Dispose(); Level.Dispose(); }
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RealPooledAndFallbackLevelSeamsScheduleExactlyAtTourDueSamples( bool pooled )
	{
		using var scene = new Scene( pooled ); var actual = new List<(int Slot, int Sample)>();
		scene.Presentation.Registry.Scheduled += (ticket, sample, _) => actual.Add( (ticket.Slot, sample) );
		var expected = new List<(int Slot, int Sample)>();
		for ( var tick = 0; tick < 120; tick++ )
		{
			if ( SimulationSubstep.ForTick( scene.Economy.Tick, true ).TourDue )
			{
				var sample = GuestSimulation.BodySpriteSampleAtTick( scene.Economy.Tick + 1 );
				expected.Add( (1, sample) ); expected.Add( (2, sample) );
			}
			scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		}
		CollectionAssert.AreEqual( expected, actual ); Assert.AreEqual( 2000, scene.Presentation.LastSample );
	}

	[TestMethod]
	public void RealPausedLevelNeverSchedulesWhileLostLevelContinuesBothOwnersWithoutApplyingPending()
	{
		using var scene = new Scene(); var body = scene.Member.BodySprite!;
		for ( var tick = 0; tick < 8; tick++ ) scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		scene.Economy.Speed = GameSpeed.Paused; body.RequestTemplate( 18 );
		var source = StaffOwnerFixture.Words( body.ReadSourceState()! ); var clone = StaffOwnerFixture.Words( scene.Window.ReadCloneState()! );
		var sample = scene.Presentation.LastSample;
		for ( var tick = 0; tick < 60; tick++ ) scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		CollectionAssert.AreEqual( source, StaffOwnerFixture.Words( body.ReadSourceState()! ) ); CollectionAssert.AreEqual( clone, StaffOwnerFixture.Words( scene.Window.ReadCloneState()! ) );
		Assert.AreEqual( sample, scene.Presentation.LastSample );
		scene.Economy.Speed = GameSpeed.Normal; typeof( ParkEconomy ).GetProperty( nameof( ParkEconomy.IsBankrupt ) )!.SetValue( scene.Economy, true );
		for ( var tick = 0; tick < 60; tick++ ) scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
		Assert.AreNotEqual( unchecked( (int)source[31] ), body.ReadSourceState()!.Due ); Assert.AreEqual( 18, body.PendingTemplate );
		Assert.AreNotEqual( unchecked( (int)clone[31] ), scene.Window.ReadCloneState()!.Due );
	}

	[TestMethod]
	public void SameRealSamplesAndOuterBoundariesPreserveAllWordsAcrossSimulationBatchSizes()
	{
		using var a = new Scene(); using var b = new Scene();
		for ( var batch = 0; batch < 15; batch++ )
		{
			for ( var tick = 0; tick < 8; tick++ ) a.Level.AdvanceSimulation( FixedStepClock.TickDuration );
			b.Level.AdvanceSimulation( 8 * FixedStepClock.TickDuration );
			a.Level.PresentStaffSprites( a.Window.PartialRefresh ); b.Level.PresentStaffSprites( b.Window.PartialRefresh );
			CollectionAssert.AreEqual( StaffOwnerFixture.Words( a.Member.BodySprite!.ReadSourceState()!, true ), StaffOwnerFixture.Words( b.Member.BodySprite!.ReadSourceState()!, true ) );
			CollectionAssert.AreEqual( StaffOwnerFixture.Words( a.Window.ReadCloneState()!, true ), StaffOwnerFixture.Words( b.Window.ReadCloneState()!, true ) );
			Assert.AreEqual( a.Level.ComputeStateHash(), b.Level.ComputeStateHash() );
		}
	}

	[TestMethod]
	public void CanonicalGameplayHashExcludesSourceClonePublicationAndWindowHistory()
	{
		using var scene = new Scene(); var hash = scene.Level.ComputeStateHash(); var random = scene.Economy.World.State;
		for ( var index = 0; index < 50; index++ )
		{
			scene.Member.BodySprite!.Moving( 0, 0, index, index * 2, false, false ); scene.Member.BodySprite.ApplyPending();
			scene.Presentation.Registry.Schedule( index * 63 ); scene.Level.PresentStaffSprites( scene.Window.PartialRefresh );
			_ = scene.Member.BodySprite.DrawRecord; _ = scene.Window.DrawRecord;
			if ( index % 5 == 0 ) scene.Window.FullRefresh( scene.Member, -0.5f, 0.4f, 0.975f );
			Assert.AreEqual( hash, scene.Level.ComputeStateHash() ); Assert.AreEqual( random, scene.Economy.World.State );
		}
		scene.Window.Close(); Assert.AreEqual( hash, scene.Level.ComputeStateHash() );
	}

	[TestMethod]
	public void ActualLegacyLoadFailureKeepsOwnersAndSuccessfulLoadStartsFreshFinalClockSession()
	{
		using var scene = new Scene(); var directory = Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( directory );
		try
		{
			var good = Path.Combine( directory, "park.json" ); var bad = Path.Combine( directory, "bad.json" );
			for ( var tick = 0; tick < 90; tick++ ) scene.Level.AdvanceSimulation( FixedStepClock.TickDuration );
			scene.Level.SavePark( good ); File.WriteAllText( bad, "{}" );
			var owner = scene.Presentation; var source = scene.Member.BodySprite!.SourceTicket; var clone = scene.Window.CloneTicket;
			try { scene.Level.LoadPark( bad ); Assert.Fail( "Invalid save must fail before replacing presentation." ); }
			catch ( Exception error ) when ( error is System.Text.Json.JsonException or InvalidDataException ) { }
			Assert.AreSame( owner, scene.Presentation ); Assert.AreEqual( source, scene.Member.BodySprite!.SourceTicket ); Assert.AreEqual( clone, scene.Window.CloneTicket );
			var guestClock = scene.Guests.BodySpriteClockMilliseconds; scene.Level.LoadPark( good );
			Assert.IsTrue( owner.IsDisposed ); Assert.AreEqual( EmployeePortraitState.Closed, scene.Window.State ); Assert.AreNotSame( owner, scene.Presentation );
			var restored = scene.Economy.Staff.Members.Single(); Assert.AreNotSame( scene.Member, restored ); Assert.IsTrue( restored.BodySprite!.Reconstructed );
			Assert.AreEqual( unchecked( GuestSimulation.BodySpriteSampleAtTick( scene.Economy.Tick ) + 62 ), restored.BodySprite.ReadSourceState()!.Due );
			Assert.AreEqual( 0u, restored.BodySprite.ReadSourceState()!.OutputValid ); Assert.AreEqual( guestClock, scene.Guests.BodySpriteClockMilliseconds );
		}
		finally { Directory.Delete( directory, recursive: true ); }
	}

	[TestMethod]
	public void ActualLevelLoadThingConflictPreservesEconomyStreamsPoolSourcesAndClone()
	{
		using var scene = new Scene(); var file = Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString( "N" ) + ".json" );
		try
		{
			scene.Level.SavePark( file );
			var save = JsonNode.Parse( File.ReadAllText( file ) )!;
			// A structurally valid save names a live singleton slot of the wrong model. Also vary the saved RNG
			// so an early economy/stream commit cannot accidentally pass by retaining equal numeric state.
			save["Staff"]!["Members"]![0]!["ThingId"] = 1;
			save["WorldGenerator"] = scene.Economy.World.State ^ 0x12345678u;
			File.WriteAllText( file, save.ToJsonString() );
			var economy = scene.Economy; var generator = scene.Economy.World; var random = scene.Level.CaptureRandomState();
			var guestsWorld = scene.Guests.World; var scriptsWorld = scene.Level.Objects.ScriptWorld.World;
			var pool = JsonSerializer.Serialize( scene.Level.Things.CaptureSnapshot() );
			var owner = scene.Presentation; var source = scene.Member.BodySprite!.SourceTicket; var clone = scene.Window.CloneTicket; var epoch = scene.Window.Epoch;
			var sourceWords = StaffOwnerFixture.Words( scene.Member.BodySprite.ReadSourceState()! ); var cloneWords = StaffOwnerFixture.Words( scene.Window.ReadCloneState()! );
			Assert.ThrowsException<InvalidDataException>( () => scene.Level.LoadPark( file ) );
			Assert.AreSame( economy, scene.Economy ); Assert.AreSame( generator, scene.Economy.World );
			Assert.AreSame( guestsWorld, scene.Guests.World ); Assert.AreSame( scriptsWorld, scene.Level.Objects.ScriptWorld.World );
			Assert.AreEqual( random, scene.Level.CaptureRandomState() ); Assert.AreEqual( pool, JsonSerializer.Serialize( scene.Level.Things.CaptureSnapshot() ) );
			Assert.AreSame( owner, scene.Presentation ); Assert.IsFalse( owner.IsDisposed );
			Assert.AreEqual( source, scene.Member.BodySprite.SourceTicket ); Assert.AreEqual( clone, scene.Window.CloneTicket ); Assert.AreEqual( epoch, scene.Window.Epoch );
			CollectionAssert.AreEqual( sourceWords, StaffOwnerFixture.Words( scene.Member.BodySprite.ReadSourceState()! ) );
			CollectionAssert.AreEqual( cloneWords, StaffOwnerFixture.Words( scene.Window.ReadCloneState()! ) );
		}
		finally { File.Delete( file ); }
	}

	[TestMethod]
	public void ActualLevelDisposeImmediatelyRetiresSourcesClonesAndRejectsReusedIdentity()
	{
		using var scene = new Scene(); var source = scene.Member.BodySprite!.SourceTicket!.Value;
		scene.Level.Dispose(); Assert.IsTrue( scene.Presentation.IsDisposed ); Assert.AreEqual( 0, scene.Presentation.Registry.Count );
		Assert.AreEqual( EmployeePortraitState.Closed, scene.Window.State ); Assert.IsNull( scene.Member.BodySprite );
		Assert.IsFalse( scene.Presentation.Registry.TryResolve( source, out _ ) ); scene.Level.Dispose();
	}
}
