using System;
using System.IO;
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

	private sealed class Fixture : IDisposable
	{
		private readonly string directory = Path.Combine( Path.GetTempPath(), "opentpw-root-fault-" + Guid.NewGuid().ToString( "N" ) );
		public ObjectCatalogEntry Entry { get; }
		public Fixture( Asm? script )
		{
			Directory.CreateDirectory( directory );
			File.WriteAllBytes( Path.Combine( directory, "model.md2" ), Md2ModelFileTests.CreateGeometry() );
			if ( script != null ) File.WriteAllBytes( Path.Combine( directory, "root.rse" ), script.Build( Variables ) );
			var settings = new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1000\nInfo.Name Synthetic\nInfo.FixedItem 1" ) } );
			Entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Feature, "/synthetic.wad", settings, new ObjectShape( new[] { "*" } ),
				"/model.md2", null, script == null ? null : "/root.rse", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(),
				Array.Empty<string>(), new[] { "/model.md2" }, new BaseFileSystem( directory ), null );
		}
		public void Dispose() => Directory.Delete( directory, recursive: true );
	}
}
