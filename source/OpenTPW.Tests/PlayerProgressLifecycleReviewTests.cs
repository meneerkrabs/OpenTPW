using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class PlayerProgressLifecycleReviewTests
{
	private PlayerProgressIngressTests fixture = null!;
	private GameFlow flow = null!;
	private FrontEndMenu menu = null!;
	private UiContext context = null!;
	private string root = null!;
	private List<Entity> oldEntities = null!;
	private CameraMode oldCamera = null!;
	private Level? oldLevel;
	private Renderer oldRenderer = null!;
	private EntityConstructionScope? staged;
	private Level live = null!;
	private ParkEconomyRuntime runtime = null!;
	private OwnedEntity liveEntity = null!;
	private OwnedEntity? candidateEntity;
	private string Profile => Path.Combine( root, PlayerTicketFile.FileName );
	private static T Get<T>( object value, string field ) => (T)value.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( value )!;
	private static void Set( object value, string field, object? data ) => value.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( value, data );
	private static void Call( GameFlow value, string method, params object[] arguments )
	{
		try
		{
			var target = typeof( GameFlow ).GetMethod( method, BindingFlags.Instance | BindingFlags.NonPublic )!;
			target.Invoke( value, arguments.Length == 0 ? target.GetParameters().Select( parameter => parameter.DefaultValue ).ToArray() : arguments );
		}
		catch ( TargetInvocationException error ) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture( error.InnerException! ).Throw(); }
	}
	private sealed class OwnedEntity : Entity
	{
		public int Deleted; public bool Fail;
		protected override void OnDelete() { Deleted++; if ( Fail ) throw new IOException( "Injected old-entity retirement I/O failure." ); }
	}
	private sealed class Steps( bool fail ) : IEnumerator<float>
	{
		public float Current => 0; object System.Collections.IEnumerator.Current => Current;
		public bool MoveNext() => fail ? throw new InvalidDataException( "Injected actual Level.Step construction refusal." ) : false;
		public void Reset() => throw new NotSupportedException(); public void Dispose() { }
	}
	// Uninitialized CPU levels bypass field initializers. Keep the real disposal path with an empty renderer owner set.
	private static Level EmptyLevel()
	{
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		var field = typeof( Level ).GetField( "trackRenderers", BindingFlags.Instance | BindingFlags.NonPublic )!;
		field.SetValue( level, Activator.CreateInstance( field.FieldType ) );
		return level;
	}
	[TestInitialize]
	public void Setup()
	{
		fixture = new(); fixture.Initialize(); flow = Get<GameFlow>( fixture, "flow" ); menu = Get<FrontEndMenu>( fixture, "menu" ); context = Get<UiContext>( fixture, "context" ); root = Get<string>( fixture, "root" );
		oldEntities = Entity.All; oldCamera = Camera.Mode; oldLevel = Level.Current; oldRenderer = Global.Render;
		Entity.All = new(); Camera.Mode = new CameraMode(); Global.Render = (Renderer)RuntimeHelpers.GetUninitializedObject( typeof( Renderer ) );
		live = EmptyLevel(); runtime = ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation );
		Set( live, "<LevelName>k__BackingField", "jungle" ); Set( live, "<Park>k__BackingField", runtime ); Set( flow, "<Level>k__BackingField", live ); Level.Current = live; liveEntity = new();
	}
	[TestCleanup]
	public void Cleanup()
	{
		staged?.Dispose(); Call( flow, "DetachProgress" ); Entity.All = oldEntities; Camera.Mode = oldCamera; Level.Current = oldLevel!; Global.Render = oldRenderer; fixture.Cleanup();
	}
	private void Choose()
	{
		menu.SelectLevel( "jungle" ); menu.ShowGameMode(); Click( "fullSimulation" ); Click( "ok" );
		if ( menu.Stack.Top!.Name == "newProgress" ) Click( "choice0" );
	}
	private void Click( string id ) => menu.Stack.Update( context, UiInput.Click( menu.Stack.Top!.Find( id )!.ScreenRect( context.Canvas ).Center ) );
	private void Pending() { var action = Get<Action?>( flow, "pending" ); Set( flow, "pending", null ); action?.Invoke(); }
	private void Stage( Action commit, bool failStep = false, bool guarded = true )
	{
		staged = new(); var candidate = EmptyLevel();
		Set( candidate, "<LevelName>k__BackingField", "jungle" ); Set( candidate, "construction", new Steps( failStep ) ); Level.Current = candidate; candidateEntity = new(); Camera.Mode = new CameraMode();
		staged.Pause();
		var type = typeof( GameFlow ).GetNestedType( "ParkBuild", BindingFlags.NonPublic )!; var build = RuntimeHelpers.GetUninitializedObject( type );
		foreach ( var pair in new Dictionary<string, object> { ["Scene"] = staged, ["Level"] = candidate, ["LevelName"] = "jungle", ["Original"] = true, ["BeforeEnter"] = (Action<Level>)(_ => commit()), ["FrameMilliseconds"] = new List<double>() } )
			if ( guarded || pair.Key != "BeforeEnter" ) type.GetField( "<" + pair.Key + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( build, pair.Value );
		Set( flow, "parkBuild", build );
	}
	private void Pump()
	{
		for ( var frame = 0; frame < 128 && flow.BuildingPark; frame++ ) Call( flow, "PumpParkBuild" );
		Assert.IsFalse( flow.BuildingPark, "A controlled one-step candidate must finish without assuming one frame meets the budget." );
		staged = null;
	}
	private void KnownOwner()
	{
		PlayerTicketFile.Save( Profile, new PlayerTicketLedger().Capture() with { ExtraKeys = 1 } ); flow.AttachProgress( live );
	}
	private void Award() { runtime.Economy.Counters.Add( ParkCounters.Admissions, 101 ); runtime.Economy.Advance( ParkCalendar.TickOfTurn( 100 ) - runtime.Economy.Tick ); }
	private void AssertRollback( List<Entity> entities, CameraMode camera )
	{
		Assert.AreSame( live, flow.Level ); Assert.AreSame( live, Level.Current ); Assert.AreSame( entities, Entity.All ); Assert.AreSame( camera, Camera.Mode );
		Assert.AreEqual( 0, liveEntity.Deleted ); Assert.AreEqual( 1, candidateEntity!.Deleted ); Assert.IsFalse( flow.BuildingPark ); Assert.IsFalse( EntityConstructionScope.IsStaging );
		Assert.AreEqual( "progressFailed", menu.Stack.Top!.Name );
	}

	[TestMethod]
	public void ActualStagedStepFailureDisposesOnlyCandidateAndNeverCreatesTentativeProfile()
	{
		var entities = Entity.All; var camera = Camera.Mode; var before = ParkSaveFile.Serialize( runtime.Economy );
		flow.ProgressBuildOverride = ( _, _, commit ) => Stage( commit, failStep: true ); Choose(); Pending(); Assert.IsTrue( flow.BuildingPark ); Assert.IsFalse( File.Exists( Profile ) );
		Pump(); AssertRollback( entities, camera ); Assert.AreEqual( before, ParkSaveFile.Serialize( runtime.Economy ) ); Assert.IsFalse( File.Exists( Profile ) );
	}

	[TestMethod]
	public void DirtyAwardDuringStagingRefusesBeforePrecommitAndKeepsLiveOwner()
	{
		KnownOwner(); var source = File.ReadAllBytes( Profile ); var owner = Get<PlayerProgressRuntimeOwner>( flow, "progressOwner" ); var entities = Entity.All; var camera = Camera.Mode;
		var commits = 0; flow.ProgressBuildOverride = ( _, _, commit ) => Stage( () => { commits++; commit(); } ); Choose(); Pending();
		File.WriteAllText( Profile, "{}" ); Award(); var before = ParkSaveFile.Serialize( runtime.Economy ); Assert.IsTrue( owner.Dirty );
		Pump(); AssertRollback( entities, camera ); Assert.AreSame( owner, Get<PlayerProgressRuntimeOwner>( flow, "progressOwner" ) ); Assert.IsTrue( owner.Dirty ); Assert.AreEqual( 0, commits );
		Assert.AreEqual( before, ParkSaveFile.Serialize( runtime.Economy ) ); Assert.AreEqual( "{}", File.ReadAllText( Profile ) );
		File.WriteAllBytes( Profile, source ); Assert.IsTrue( owner.TryFlush() ); Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( Profile )!.EarnedTickets );
	}

	[TestMethod]
	public void ActualPostcommitRetirementFailureKeepsSingleCreatedKeyAndAllowsFreshRetry()
	{
		flow.ProgressBuildOverride = ( _, _, commit ) => Stage( commit ); Choose(); Pending(); liveEntity.Fail = true;
		Pump(); Assert.AreEqual( 1, liveEntity.Deleted ); Assert.AreEqual( 1, candidateEntity!.Deleted ); Assert.IsFalse( flow.BuildingPark );
		Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( Profile )!.ExtraKeys ); Assert.AreEqual( 0, PlayerTicketFile.LoadIfPresent( Profile )!.EarnedTickets );
		var before = File.ReadAllBytes( Profile ); menu.Stack.Clear(); var retried = 0;
		flow.ProgressBuildOverride = ( _, _, commit ) => { retried++; commit(); }; Choose(); Pending(); Assert.AreEqual( 1, retried ); CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) );
	}

	[TestMethod]
	public void DirtyStateAfterQueueRefusesAtExecutionAndRetryDoesNotLoseAward()
	{
		KnownOwner(); var source = File.ReadAllBytes( Profile ); var transitioned = 0;
		typeof( GameFlow ).GetMethod( "QueueProgressTransition", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( flow, new object[] { (Action)(() => transitioned++) } );
		var queued = Get<Action>( flow, "pending" ); File.WriteAllText( Profile, "{}" ); Award(); queued();
		Assert.AreEqual( 0, transitioned ); Assert.AreSame( live, flow.Level ); Assert.AreEqual( "{}", File.ReadAllText( Profile ) ); Assert.IsTrue( Get<PlayerProgressRuntimeOwner>( flow, "progressOwner" ).Dirty );
		File.WriteAllBytes( Profile, source ); queued(); Assert.AreEqual( 1, transitioned ); Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( Profile )!.EarnedTickets );
	}

	private sealed class PreparedModel : IDisposable
	{
		public bool Disposed { get; private set; }
		public void Dispose() => Disposed = true;
	}

	/// <summary>Something an earlier build read for the advisor and nobody took (<see cref="AdvisorEntryPreparation"/>).</summary>
	private static PreparedModel PrepareStale()
	{
		var stale = new PreparedModel();
		AdvisorEntryPreparation.Prepare( "stale", new BaseFileSystem( Path.GetTempPath() ), () => stale );
		Assert.AreEqual( 1, AdvisorEntryPreparation.Count );
		return stale;
	}

	[TestMethod]
	public void AParkBuildWhoseConstructionFailsLeavesNoAdvisorPreparation()
	{
		try
		{
			var stale = PrepareStale();
			flow.ProgressBuildOverride = ( _, _, commit ) => Stage( commit, failStep: true ); Choose(); Pending(); Pump();
			Assert.AreEqual( "progressFailed", menu.Stack.Top!.Name );
			Assert.AreEqual( 0, AdvisorEntryPreparation.Count, "a build whose construction failed" ); Assert.IsTrue( stale.Disposed );
		}
		finally { AdvisorEntryPreparation.Drop(); }
	}

	[TestMethod]
	public void AParkBuildWhoseEnteringFailsLeavesNoAdvisorPreparation()
	{
		try
		{
			// entering refused (unsaved progress), after the build's preparation
			KnownOwner(); var source = File.ReadAllBytes( Profile ); var stale = PrepareStale();
			flow.ProgressBuildOverride = ( _, _, commit ) => Stage( commit ); Choose(); Pending(); File.WriteAllText( Profile, "{}" ); Award(); Pump();
			Assert.AreEqual( "progressFailed", menu.Stack.Top!.Name );
			Assert.AreEqual( 0, AdvisorEntryPreparation.Count, "a build whose entering failed" ); Assert.IsTrue( stale.Disposed );
			File.WriteAllBytes( Profile, source );
		}
		finally { AdvisorEntryPreparation.Drop(); }
	}

	[TestMethod]
	public void ATearDownDropsTheAdvisorPreparation()
	{
		try
		{
			var stale = PrepareStale();
			Call( flow, "TearDown", false, false );
			Assert.AreEqual( 0, AdvisorEntryPreparation.Count ); Assert.IsTrue( stale.Disposed );
		}
		finally { AdvisorEntryPreparation.Drop(); }
	}

	[TestMethod]
	public void DirtyDirectTearDownAndNormalQuitRefuseBeforeNativeCloseOrOwnershipMutation()
	{
		KnownOwner(); var source = File.ReadAllBytes( Profile ); var owner = Get<PlayerProgressRuntimeOwner>( flow, "progressOwner" ); var entities = Entity.All; var camera = Camera.Mode;
		File.WriteAllText( Profile, "{}" ); Award(); var before = ParkSaveFile.Serialize( runtime.Economy ); var screens = menu.Stack.Screens.ToArray();
		Assert.ThrowsException<PlayerProgressSaveException>( () => Call( flow, "TearDown" ) );
		Assert.AreSame( owner, Get<PlayerProgressRuntimeOwner>( flow, "progressOwner" ) ); Assert.AreSame( live, flow.Level ); CollectionAssert.AreEqual( screens, menu.Stack.Screens.ToArray() );
		Call( flow, "RequestProgressQuit" ); // If the guard is missing, the deliberately absent native Window throws.
		Assert.AreSame( live, flow.Level ); Assert.AreSame( entities, Entity.All ); Assert.AreSame( camera, Camera.Mode ); Assert.AreEqual( 0, liveEntity.Deleted ); Assert.AreEqual( before, ParkSaveFile.Serialize( runtime.Economy ) );
		File.WriteAllBytes( Profile, source ); Assert.IsTrue( owner.TryFlush() ); Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( Profile )!.EarnedTickets );
	}

	[TestMethod]
	public void RuntimeLoadThenRealAwardReconcilesWithoutPumpAndRebindingHasOneSubscription()
	{
		KnownOwner(); var loaded = EconomyTestData.Park(); loaded.Objectives.Restore( 0, 0, null, new Dictionary<int, int>(), Array.Empty<int>(), new[] { GoldenTicketKind.WaterLength } );
		var file = Path.Combine( root, "runtime.json" ); ParkSaveFile.Save( file, loaded ); runtime.Load( file ); Award();
		CollectionAssert.AreEquivalent( new[] { GoldenTicketKind.WaterLength, GoldenTicketKind.Visitors }, PlayerTicketFile.LoadIfPresent( Profile )!.Capture().Earned.Select( item => item.Kind ).ToArray() );
		var old = Get<PlayerProgressRuntimeOwner>( flow, "progressOwner" ); flow.AttachProgress( live ); var replacement = Get<PlayerProgressRuntimeOwner>( flow, "progressOwner" ); Assert.AreNotSame( old, replacement );
		var field = typeof( ParkEconomyRuntime ).GetField( "EventRaised", BindingFlags.Instance | BindingFlags.NonPublic )!;
		Assert.AreEqual( 1, ((Delegate?)field.GetValue( runtime ))?.GetInvocationList().Length ?? 0 ); Call( flow, "DetachProgress" ); Assert.AreEqual( 0, ((Delegate?)field.GetValue( runtime ))?.GetInvocationList().Length ?? 0 );
		var source = File.ReadAllBytes( Profile ); runtime.Economy.Counters.Max( ParkCounters.Record( ParkRecordKind.CoasterHeight, 1 ), 106 ); runtime.Economy.Advance( ParkCalendar.TickOfTurn( 200 ) - runtime.Economy.Tick ); CollectionAssert.AreEqual( source, File.ReadAllBytes( Profile ) );
	}
	[TestMethod]
	public void CancelingActualStagedRequestDeletesCandidateBeforeAnyProfilePublication()
	{
		var entities = Entity.All; var camera = Camera.Mode; flow.ProgressBuildOverride = ( _, _, commit ) => Stage( commit ); Choose(); Pending();
		Assert.IsTrue( flow.BuildingPark ); Assert.IsFalse( File.Exists( Profile ) ); var ran = 0;
		flow.Queue( () => ran++ ); staged = null;
		Assert.IsFalse( flow.BuildingPark ); Assert.AreEqual( 1, candidateEntity!.Deleted ); Assert.AreEqual( 0, liveEntity.Deleted );
		Assert.AreSame( entities, Entity.All ); Assert.AreSame( camera, Camera.Mode ); Assert.AreSame( live, Level.Current ); Assert.AreSame( live, flow.Level );
		Assert.IsFalse( File.Exists( Profile ) ); Pending(); Assert.AreEqual( 1, ran ); Assert.IsFalse( File.Exists( Profile ) );
	}

	[TestMethod]
	public void SuccessfulActualTearDownFlushesThenUnsubscribesRetiredRuntime()
	{
		KnownOwner(); Award(); Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( Profile )!.EarnedTickets );
		Call( flow, "TearDown", false, false ); Assert.IsNull( flow.Level ); Assert.IsNull( flow.Menu ); Assert.AreEqual( 1, liveEntity.Deleted );
		var events = typeof( ParkEconomyRuntime ).GetField( "EventRaised", BindingFlags.Instance | BindingFlags.NonPublic )!;
		Assert.AreEqual( 0, ((Delegate?)events.GetValue( runtime ))?.GetInvocationList().Length ?? 0 );
		var source = File.ReadAllBytes( Profile ); runtime.Economy.Counters.Max( ParkCounters.Record( ParkRecordKind.CoasterHeight, 1 ), 106 );
		runtime.Economy.Advance( ParkCalendar.TickOfTurn( 200 ) - runtime.Economy.Tick );
		Assert.IsTrue( runtime.Economy.Objectives.GoldenTickets.Contains( GoldenTicketKind.CoasterHeight ) ); CollectionAssert.AreEqual( source, File.ReadAllBytes( Profile ) );
	}

	[TestMethod]
	public void NormalConstructionFailureWithoutProgressPrecommitStillPropagates()
	{
		var entities = Entity.All; var camera = Camera.Mode; Stage( () => Assert.Fail(), failStep: true, guarded: false );
		Assert.ThrowsException<InvalidDataException>( Pump ); staged = null;
		Assert.IsFalse( flow.BuildingPark ); Assert.AreSame( entities, Entity.All ); Assert.AreSame( camera, Camera.Mode );
		Assert.AreEqual( 0, liveEntity.Deleted ); Assert.AreEqual( 1, candidateEntity!.Deleted ); Assert.IsFalse( File.Exists( Profile ) );
		Assert.AreNotEqual( "progressFailed", menu.Stack.Top!.Name );
	}

}
