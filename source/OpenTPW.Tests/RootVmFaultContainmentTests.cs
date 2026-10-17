using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class RootVmFaultContainmentTests
{
	private GameEdition previousEdition;
	private static readonly string[] Variables = Enum.GetNames<RideVariables>();
	private static Asm FaultScript() => new Asm().I( Opcode.COPY, 0, V( 0 ) );
	private static Asm Loop() => new Asm().Label( "again" ).I( Opcode.ADD, V( 0 ), 1 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "again" );
	private static RideScriptFile Script( Asm asm ) => new( new MemoryStream( asm.Build( Variables ) ) );

	[TestInitialize]
	public void Initialize()
	{
		previousEdition = GameEditions.Current;
		GameEditions.Select( GameEdition.ThemeParkWorld );
		Log ??= new();
	}

	[TestCleanup]
	public void Cleanup() => GameEditions.Select( previousEdition );

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void FaultedParentCannotAdvanceEitherOwnedChildOrItsOwnClock( bool alreadyFaulted )
	{
		var world = new RideScriptWorld();
		var root = new RideVM( Script( FaultScript() ), new RideVMOptions { World = world, ResolveScript = _ => Script( Loop() ) } );
		root.SpawnChild( "child", false );
		root.SpawnChild( "sound", true );
		var child = root.Child!;
		var sound = root.SoundChild!;
		if ( alreadyFaulted ) root.Abort( "external diagnostic" );
		var before = root.TimeMilliseconds;
		root.Advance( Tick );
		Assert.AreEqual( 0L, child.ExecutedInstructions );
		Assert.AreEqual( 0L, sound.ExecutedInstructions );
		Assert.IsNull( root.Child );
		Assert.IsNull( root.SoundChild );
		Assert.AreEqual( RideVMState.Halted, child.State );
		Assert.AreEqual( RideVMState.Halted, sound.State );
		Assert.AreEqual( 1, world.Scripts.Count, "The direct VM root remains registered for diagnostics until host teardown." );
		Assert.AreEqual( RideVMState.Faulted, root.State );
		Assert.AreEqual( alreadyFaulted ? "external diagnostic" : "COPY requires a variable destination, found Literal", root.FaultMessage );
		Assert.AreEqual( alreadyFaulted ? before : before + Tick * 1000, root.TimeMilliseconds );
		var faultTime = root.TimeMilliseconds;
		root.Advance( Tick );
		Assert.AreEqual( faultTime, root.TimeMilliseconds );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => root.Advance( double.NaN ) );
		root.Stop();
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DescendantCallbackFaultStopsRemainingOwnedScheduling( bool fromSound )
	{
		var world = new RideScriptWorld();
		var root = new RideVM( Script( Loop() ), new RideVMOptions { World = world, ResolveScript = _ => Script( Loop() ) } );
		root.SpawnChild( "child", false );
		root.SpawnChild( "sound", true );
		var child = root.Child!;
		var sound = root.SoundChild!;
		(fromSound ? sound : child).InstructionExecuting = ( _, _ ) => root.Abort( "descendant callback" );
		root.Advance( Tick );
		Assert.AreEqual( RideVMState.Faulted, root.State );
		Assert.AreEqual( "descendant callback", root.FaultMessage );
		if ( !fromSound ) Assert.AreEqual( 0L, sound.ExecutedInstructions, "The next owned slot cannot run after its root faults." );
		Assert.IsNull( root.Child );
		Assert.IsNull( root.SoundChild );
		Assert.AreEqual( RideVMState.Halted, child.State );
		Assert.AreEqual( RideVMState.Halted, sound.State );
		Assert.AreEqual( 1, world.Scripts.Count );
		root.Stop();
	}

	[TestMethod]
	public void NormalCompletionKeepsItsOwnedChildScheduling()
	{
		var root = new RideVM( Script( new Asm().I( Opcode.COPY, V( 0 ), 0 ) ), new RideVMOptions { ResolveScript = _ => Script( Loop() ) } );
		root.SpawnChild( "child", false );
		root.Advance( Tick );
		root.Advance( Tick );
		Assert.AreEqual( RideVMState.Halted, root.State );
		Assert.AreEqual( 2, root.Child!.Variables[0] );
		root.Stop();
	}

	[TestMethod]
	public void ChildOnlyFaultDoesNotStopItsHealthyParentOrSibling()
	{
		var root = new RideVM( Script( Loop() ), new RideVMOptions { ResolveScript = name => Script( name == "bad" ? FaultScript() : Loop() ) } );
		root.SpawnChild( "bad", false );
		root.SpawnChild( "healthy", true );
		root.Advance( Tick );
		root.Advance( Tick );
		Assert.AreEqual( RideVMState.Faulted, root.Child!.State );
		Assert.AreEqual( RideVMState.Running, root.State );
		Assert.AreEqual( 2, root.Variables[0] );
		Assert.AreEqual( 2, root.SoundChild!.Variables[0] );
		root.Stop();
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RuntimeFaultStopsPresentationAndRoundTripsTheStoppedDiagnostic( bool alreadyFaulted )
	{
		using var fixture = new Fixture( FaultScript() );
		var world = new RideScriptWorld();
		var runtime = new OriginalObjectRuntime( fixture.Entry, world, seed: 1 );
		try
		{
			runtime.Animator.Play( 0, new ModelFile( new MemoryStream( Md2AnimationTests.CreateAnimation() ) ).Clip!, "synthetic", loop: true );
			Assert.AreEqual( 1, runtime.Animator.ActiveChannels );
			if ( alreadyFaulted ) runtime.Script!.Abort( "external diagnostic" );
			var before = runtime.Animator.CaptureSnapshot().ClockMilliseconds;
			runtime.Simulate( Tick );
			var snapshot = runtime.CaptureSnapshot();
			Assert.IsTrue( snapshot.Stopped );
			Assert.AreEqual( 0, runtime.Animator.ActiveChannels );
			Assert.AreEqual( before, snapshot.Animator.ClockMilliseconds );
			Assert.AreEqual( 0, world.Scripts.Count );
			Assert.AreEqual( runtime.Script!.FaultMessage, snapshot.StoppedScriptFault );
			runtime.Simulate( Tick );
			Assert.AreEqual( before, runtime.Animator.CaptureSnapshot().ClockMilliseconds );
			var candidateWorld = new RideScriptWorld();
			var candidate = OriginalObjectRuntime.CreateForRestore( fixture.Entry, candidateWorld, 1, false, snapshot.AttractionId );
			try
			{
				Assert.AreEqual( 0, candidate.RestoreSnapshot( snapshot ).Count );
				Assert.AreEqual( RideVMState.Faulted, candidate.Script!.State );
				Assert.AreEqual( snapshot.StoppedScriptFault, candidate.Script.FaultMessage );
				candidate.Simulate( Tick );
				Assert.AreEqual( before, candidate.Animator.CaptureSnapshot().ClockMilliseconds );
				Assert.AreEqual( 0, candidateWorld.Scripts.Count );
			}
			finally { candidate.Stop(); }
		}
		finally { runtime.Stop(); }
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void NoRootAndNormallyFinishedHeadlessRuntimeKeepPresentationProgress( bool hasRoot )
	{
		using var fixture = new Fixture( hasRoot ? new Asm().I( Opcode.COPY, V( 0 ), 0 ) : null );
		var runtime = new OriginalObjectRuntime( fixture.Entry, new RideScriptWorld(), seed: 1 );
		try
		{
			var clock = new FixedStepClock();
			clock.Advance( 0, delta => runtime.Simulate( delta ) );
			Assert.AreEqual( 0d, runtime.Animator.CaptureSnapshot().ClockMilliseconds );
			runtime.Simulate( Tick );
			runtime.Simulate( Tick );
			Assert.IsFalse( runtime.CaptureSnapshot().Stopped );
			Assert.AreEqual( Tick * 2000, runtime.Animator.CaptureSnapshot().ClockMilliseconds );
		}
		finally { runtime.Stop(); }
	}

	[TestMethod]
	public void RootFaultRetiresItsActualCoasterAndVisitorRidersButKeepsOtherOwners()
	{
		var settings = "Bumper.WhichTrackType 3\n" + string.Join( "\n", CoasterTests.PcSettings().Select( pair => pair.Key + " " + pair.Value ) );
		using var fixture = new Fixture( FaultScript(), settings );
		var world = new RideScriptWorld();
		var runtime = new OriginalObjectRuntime( fixture.Entry, world );
		var other = new OriginalObjectRuntime( fixture.Entry, world );
		try
		{
			var coaster = runtime.Coaster!;
			var builder = new CoasterBuilder( coaster, new CoasterSite( 64, 64 ) );
			CoasterTests.PlaceStation( builder, (20, 20) ); builder.Height = 40;
			foreach ( var (x, y) in CoasterTests.Circuit ) Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 20 + x, 20 + y ) );
			Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
			coaster.Command( 8, 0 ); coaster.Command( 5, 0 ); coaster.Command( 6, 6 ); coaster.Board( 101 );
			runtime.Script!.Effect( Opcode.WALKON, new[] { 101, 0, 0, 0, 0, 0, 0 }.Select( value => new Operand( runtime.Script, new RideScriptOperand( RideScriptOperandKind.Literal, (ushort)value ), -1 ) ).ToArray() );
			Assert.AreEqual( 1, coaster.AdmissionCount ); Assert.AreEqual( 1, runtime.Visitors.Riders.Count );
			Assert.IsTrue( world.Coasters.Rides.Contains( coaster ) );
			runtime.Simulate( Tick );
			Assert.AreEqual( RideVMState.Faulted, runtime.Script.State );
			Assert.AreEqual( 0, runtime.Visitors.Riders.Count ); Assert.AreEqual( 1, runtime.Visitors.ReleasedTotal );
			Assert.AreEqual( 0, coaster.Trains.Count ); Assert.IsFalse( world.Coasters.Rides.Contains( coaster ) );
			Assert.IsTrue( world.Coasters.Rides.Contains( other.Coaster! ) ); Assert.IsTrue( world.Scripts.Contains( other.Script! ) );
			runtime.Simulate( Tick ); runtime.Stop(); Assert.AreEqual( 1, runtime.Visitors.ReleasedTotal, "Teardown is idempotent." );
		}
		finally { runtime.Stop(); other.Stop(); }
	}

	[TestMethod]
	public void FaultStopsItsRealScreamChainAndRetainsOtherOwnerAndCapturePolicy()
	{
		using var fixture = new Fixture( FaultScript() );
		using var mixer = new AudioMixer( null );
		var system = new SoundEventSystem( mixer, 7 );
		var category = (SoundCategory)RuntimeHelpers.GetUninitializedObject( typeof( SoundCategory ) );
		var catalog = (SoundCatalog)RuntimeHelpers.GetUninitializedObject( typeof( SoundCatalog ) );
		var sample = new SoundSample( 1, 65535, 0, 1, 0 );
		var sound = new SoundEntry( 100, 100, 0, 0, 0, 0, 1, 100, new[] { sample }, Array.Empty<SoundLink>(), Array.Empty<byte>() );
		typeof( SoundCatalog ).GetField( "<Events>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( catalog, new[] { new SoundEvent( 72, 0, 0, 0, new[] { sound } ) } );
		typeof( SoundCategory ).GetField( "<Catalog>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( category, catalog );
		((System.Collections.Generic.Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem ).GetField( "decoded", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( system )!)[(category, 1, 1)] = new Mp2Audio( AudioMixer.OutputRate, 1, Enumerable.Repeat( (short)1000, 256 ).ToArray(), 1, 0 );
		var events = typeof( GameAudio ).GetField( "events", BindingFlags.Static | BindingFlags.NonPublic )!;
		var kids = typeof( GameAudio ).GetField( "<Kids>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic )!;
		var previousEvents = events.GetValue( null ); var previousKids = kids.GetValue( null );
		var runtime = new OriginalObjectRuntime( fixture.Entry ); var other = new OriginalObjectRuntime( fixture.Entry );
		try
		{
			events.SetValue( null, system ); kids.SetValue( null, category );
			SoundChain Scream( OriginalObjectRuntime owner )
			{
				owner.Script!.Effect( Opcode.STARTSCREAM, new[] { new Operand( owner.Script, new RideScriptOperand( RideScriptOperandKind.Literal, 2 ), -1 ), new Operand( owner.Script, new RideScriptOperand( RideScriptOperandKind.Literal, 50 ), -1 ) } );
				return (SoundChain)typeof( OriginalObjectEffects ).GetField( "screamChain", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( owner.Script.Effects )!;
			}
			var held = Scream( runtime ); var otherHeld = Scream( other );
			system.Pump();
			Assert.IsNotNull( held ); Assert.IsNotNull( otherHeld ); Assert.IsTrue( mixer.Mix( 1 ).Any( value => value != 0 ) );
			var live = runtime.CaptureSnapshot();
			Assert.IsTrue( live.HeldScream, "a held scream is captured with its operands" );
			Assert.AreEqual( (2, 50), (live.HeldScreamBand, live.HeldScreamLevel) );
			runtime.Script!.Abort( "review fault" ); runtime.Simulate( Tick );
			Assert.IsTrue( held.IsStopped ); Assert.IsFalse( otherHeld.IsStopped );
			var stopped = runtime.CaptureSnapshot(); Assert.IsFalse( stopped.HeldScream ); Assert.AreEqual( 0, stopped.OwnedScreamCount );
			Assert.AreEqual( "review fault", stopped.StoppedScriptFault );
			Assert.IsTrue( mixer.Mix( 1 ).Any( value => value != 0 ), "The healthy owner remains audible." );
			runtime.Stop(); Assert.IsFalse( otherHeld.IsStopped );
			// STOPSCREAM ends the held chain; the stopped chain no longer counts, so the park can be saved again.
			other.Script!.Effect( Opcode.STOPSCREAM, System.Array.Empty<Operand>() );
			Assert.IsTrue( otherHeld.IsStopped );
			var afterStop = other.CaptureSnapshot();
			Assert.IsFalse( afterStop.HeldScream ); Assert.AreEqual( 0, afterStop.OwnedScreamCount );
		}
		finally { runtime.Stop(); other.Stop(); events.SetValue( null, previousEvents ); kids.SetValue( null, previousKids ); }
	}

	[TestMethod]
	public void FaultStopsItsRealEventLoopAndRetainsOtherOwnerAndCaptureEligibility()
	{
		using var fixture = new Fixture( FaultScript() );
		using var mixer = new AudioMixer( null );
		var system = new SoundEventSystem( mixer, 7 );
		var category = (SoundCategory)RuntimeHelpers.GetUninitializedObject( typeof( SoundCategory ) );
		var catalog = (SoundCatalog)RuntimeHelpers.GetUninitializedObject( typeof( SoundCatalog ) );
		var sample = new SoundSample( 1, 65535, 0, 1, 0 );
		var sound = new SoundEntry( 100, 100, 0, 0, 0, 0, 1, 100, new[] { sample }, Array.Empty<SoundLink>(), Array.Empty<byte>() );
		typeof( SoundCatalog ).GetField( "<Events>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( catalog, new[] { new SoundEvent( 44, 0, 8, 0, new[] { sound } ) } );
		typeof( SoundCategory ).GetField( "<Catalog>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( category, catalog );
		((System.Collections.Generic.Dictionary<(SoundCategory, int, uint), Mp2Audio?>)typeof( SoundEventSystem ).GetField( "decoded", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( system )!)[(category, 1, 1)] = new Mp2Audio( AudioMixer.OutputRate, 1, Enumerable.Repeat( (short)1000, 256 ).ToArray(), 1, 0 );
		var events = typeof( GameAudio ).GetField( "events", BindingFlags.Static | BindingFlags.NonPublic )!;
		var kids = typeof( GameAudio ).GetField( "<LevelRides>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic )!;
		var previousEvents = events.GetValue( null ); var previousKids = kids.GetValue( null );
		var runtime = new OriginalObjectRuntime( fixture.Entry ); var other = new OriginalObjectRuntime( fixture.Entry );
		try
		{
			events.SetValue( null, system ); kids.SetValue( null, category );
			AudioVoice EventLoop( OriginalObjectRuntime owner )
			{
				var listenerPosition = AudioListener.Position;
				owner.SetEventOrigin( () => listenerPosition );
				using var stream = new MemoryStream( new Asm().I( Opcode.EVENT, 3, -1, 44 ).I( Opcode.ENDSLICE ).Build( Array.Empty<string>() ) );
				var vm = new RideVM( stream, new RideVMOptions { Effects = owner.Script!.Effects } );
				vm.Advance( Tick ); Assert.AreNotEqual( RideVMState.Faulted, vm.State ); vm.Stop();
				return ((System.Collections.Generic.List<AudioVoice>)typeof( OriginalObjectEffects ).GetField( "ownedEventLoops", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( owner.Script.Effects )!).Single();
			}
			var held = EventLoop( runtime ); var otherHeld = EventLoop( other );
			Assert.IsTrue( held.Loops ); Assert.IsTrue( otherHeld.Loops ); Assert.IsTrue( mixer.Mix( 1 ).Any( value => value != 0 ) );
			StringAssert.Contains( Assert.ThrowsException<NotSupportedException>( () => runtime.CaptureSnapshot() ).Message, "looping EVENT" );
			runtime.Script!.Abort( "review fault" ); runtime.Simulate( Tick );
			Assert.IsTrue( held.IsFinished ); Assert.IsFalse( otherHeld.IsFinished );
			var stopped = runtime.CaptureSnapshot(); Assert.IsFalse( stopped.HeldScream ); Assert.AreEqual( 0, stopped.OwnedScreamCount );
			Assert.AreEqual( "review fault", stopped.StoppedScriptFault );
			Assert.IsTrue( mixer.Mix( 1 ).Any( value => value != 0 ), "The healthy owner remains audible." );
			runtime.Stop(); Assert.IsFalse( otherHeld.IsFinished );
		}
		finally { runtime.Stop(); other.Stop(); events.SetValue( null, previousEvents ); kids.SetValue( null, previousKids ); }
	}

	[TestMethod]
	public void UnservicedRootFaultKeepsExistingLiveSnapshotUntilContainmentTick()
	{
		using var fixture = new Fixture( FaultScript() ); var runtime = new OriginalObjectRuntime( fixture.Entry );
		try
		{
			runtime.Script!.Abort( "pending diagnostic" );
			var live = runtime.CaptureSnapshot(); Assert.IsFalse( live.Stopped ); Assert.IsNull( live.StoppedScriptFault );
			Assert.AreEqual( "pending diagnostic", runtime.Script.CaptureSnapshot().FaultMessage );
			live.ValidateSnapshot();
			runtime.Simulate( Tick ); Assert.AreEqual( "pending diagnostic", runtime.CaptureSnapshot().StoppedScriptFault );
		}
		finally { runtime.Stop(); }
	}

	private sealed class Fixture : IDisposable
	{
		private readonly string directory = Path.Combine( Path.GetTempPath(), "opentpw-root-fault-" + Guid.NewGuid().ToString( "N" ) );
		public ObjectCatalogEntry Entry { get; }
		public Fixture( Asm? script, string extraSettings = "" )
		{
			Directory.CreateDirectory( directory );
			File.WriteAllBytes( Path.Combine( directory, "model.md2" ), Md2ModelFileTests.CreateGeometry() );
			if ( script != null ) File.WriteAllBytes( Path.Combine( directory, "root.rse" ), script.Build( Variables ) );
			var settings = new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1000\nInfo.Name Synthetic\nInfo.FixedItem 1\n" + extraSettings ) } );
			Entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Feature, "/synthetic.wad", settings, new ObjectShape( new[] { "*" } ),
				"/model.md2", null, script == null ? null : "/root.rse", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(),
				Array.Empty<string>(), new[] { "/model.md2" }, new BaseFileSystem( directory ), null );
		}
		public void Dispose() => Directory.Delete( directory, recursive: true );
	}
}
