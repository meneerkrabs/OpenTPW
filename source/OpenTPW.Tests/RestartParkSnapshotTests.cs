using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Restart Park reloads the snapshot taken when the park started or was loaded, like the original's state 15 write
/// (0x54f3f7) and state 13/14 reload (0x550359): a restart after building returns to the start state, a restart of a
/// loaded park to the loaded state, an online visit (GameType 1) takes none.
/// </summary>
[TestClass, DoNotParallelize]
public class RestartParkSnapshotTests
{
	private string root = null!;
	private BaseFileSystem previousFiles = null!;
	private GameEdition previousEdition;
	private Logger previousLog = null!;

	[TestInitialize]
	public void Initialize()
	{
		previousLog = Log; Log = new();
		previousFiles = SaveFileSystem; previousEdition = GameEditions.Current;
		GameEditions.Select( GameEdition.ThemeParkWorld );
		root = Directory.CreateTempSubdirectory( "opentpw-restart-" ).FullName;
		SaveFileSystem = new( root );
	}

	[TestCleanup]
	public void Cleanup()
	{
		Log = previousLog; SaveFileSystem = previousFiles; GameEditions.Select( previousEdition );
		Directory.Delete( root, true );
	}

	private static void Set( object target, string name, object? value ) =>
		target.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private static GameFlow Flow( Level level )
	{
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		Set( flow, "<Level>k__BackingField", level );
		flow.ProgressCaptureOverride = () => false;
		return flow;
	}

	private static Level Live( ParkStartKind kind = ParkStartKind.InstantAction, bool visit = false )
	{
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "<LevelName>k__BackingField", "jungle" );
		Set( level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( EconomyTestData.Park( mode: kind == ParkStartKind.InstantAction ? ParkGameMode.InstantAction : ParkGameMode.FullSimulation ), kind ) );
		Set( level, "<OriginalPark>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) ) );
		Set( level, "<Guests>k__BackingField", new GuestSimulation( new GuestPathGrid( 32, 32 ), GuestTests.CreateSettings(), 7 ) );
		if ( visit ) Set( level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		return level;
	}

	private static ParkResumeSnapshot Snapshot( string name, string theme = "jungle" ) =>
		ParkResumeSaveFileTests.Envelope() with
		{
			Theme = theme,
			DisplayName = name,
			Gate = new ParkGate.ParkGateSnapshot( 0 ),
			Staff = new StaffWorldSimulation.StaffWorldStateSnapshot( Array.Empty<StaffWorldSimulation.StaffActorSnapshot>() )
		};

	private string RestartFile => SaveFileSystem.GetAbsolutePath( GameFlow.RestartSnapshotFileName( "jungle" ) );

	[TestMethod]
	public void DeferredRestartCapturesOnceAndRestartFinishesThePendingWrite()
	{
		var level = Live(); var flow = Flow( level ); var live = "at entry"; var captures = 0;
		flow.RestartCaptureOverride = _ => { captures++; return Snapshot( live ); };
		ParkResumeSnapshot? resumed = null; flow.RestartResumeOverride = value => resumed = value;
		flow.TakeRestartSnapshot( level, overFrames: true );
		Assert.IsTrue( flow.RestartSnapshotPending );
		Assert.IsFalse( File.Exists( RestartFile ), "the entry frame does not serialize the snapshot" );
		live = "played on";
		flow.PumpRestartSnapshot( TimeSpan.Zero );
		Assert.IsTrue( flow.RestartSnapshotPending, "a zero-budget slice only runs the initial shape check" );
		flow.RestartCurrentPark( level );
		Assert.IsFalse( flow.RestartSnapshotPending );
		Assert.AreEqual( 1, captures );
		Assert.AreEqual( "at entry", resumed?.DisplayName );
	}

	[TestMethod]
	public void ReplacementCannotCommitAnEarlierPendingCaptureLater()
	{
		var level = Live(); var flow = Flow( level );
		flow.RestartCaptureOverride = _ => Snapshot( "first" );
		flow.TakeRestartSnapshot( level, overFrames: true );
		flow.RestartCaptureOverride = _ => Snapshot( "replacement" );
		flow.TakeRestartSnapshot( level, overFrames: true );
		flow.FlushRestartSnapshot();
		flow.PumpRestartSnapshot();
		Assert.AreEqual( "replacement", RestartSnapshotWrite.Load( RestartFile ).DisplayName );
		Assert.IsFalse( flow.RestartSnapshotPending );
	}

	[TestMethod]
	public void OrderlyShutdownDrainWritesTheDetachedStartState()
	{
		var level = Live(); var flow = Flow( level );
		flow.RestartCaptureOverride = _ => Snapshot( "start" );
		flow.TakeRestartSnapshot( level, overFrames: true );
		flow.FlushRestartSnapshot(); // the same drain called before Dispose and TearDown retire the level
		flow.FlushRestartSnapshot(); // idempotent shutdown
		Assert.AreEqual( "start", RestartSnapshotWrite.Load( RestartFile ).DisplayName );
		Assert.IsFalse( File.Exists( RestartSnapshotWrite.MarkerPath( RestartFile ) ) );
	}

	[TestMethod]
	public void CrashBeforeCommitRetainsButDoesNotReloadThePreviousPark()
	{
		ParkResumeSaveFile.Save( RestartFile, Snapshot( "old park" ) );
		var write = new RestartSnapshotWrite( RestartFile, Snapshot( "new park" ) );
		write.Run( TimeSpan.Zero );
		Assert.AreEqual( "old park", ParkResumeSaveFile.Load( RestartFile ).DisplayName, "the prior recovery file is retained" );
		Assert.ThrowsException<InvalidDataException>( () => RestartSnapshotWrite.Load( RestartFile ) );
		Assert.IsFalse( write.Done );
	}

	[TestMethod]
	public void CrashAfterAtomicReplacementRecognizesTheNewFileBeforeMarkerCleanup()
	{
		var snapshot = Snapshot( "committed" );
		var bytes = ParkResumeSaveFile.Serialize( snapshot );
		var digest = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( bytes ) );
		File.WriteAllText( RestartSnapshotWrite.MarkerPath( RestartFile ), "\"" + digest + "\"" );
		ParkResumeSaveFile.SaveBytes( RestartFile, bytes );
		Assert.AreEqual( "committed", RestartSnapshotWrite.Load( RestartFile ).DisplayName );
		File.WriteAllText( RestartSnapshotWrite.MarkerPath( RestartFile ), "\"" + new string( '0', 64 ) + "\"" );
		Assert.ThrowsException<InvalidDataException>( () => RestartSnapshotWrite.Load( RestartFile ) );
	}

	[TestMethod]
	public void InterruptedOrMalformedMarkerNeverSelectsAnOlderPark()
	{
		ParkResumeSaveFile.Save( RestartFile, Snapshot( "old" ) );
		foreach ( var marker in new[] { "", "\"pending\"", new string( 'x', 66 ), new string( 'x', 1024 ) } )
		{
			File.WriteAllText( RestartSnapshotWrite.MarkerPath( RestartFile ), marker );
			Assert.ThrowsException<InvalidDataException>( () => RestartSnapshotWrite.Load( RestartFile ) );
		}
	}

	[DataTestMethod]
	[DataRow( 2 )]
	[DataRow( 3 )]
	public void FailedMarkerOrReplacementWriteRetainsTheOldFileWithoutSelectingIt( int failingWrite )
	{
		ParkResumeSaveFile.Save( RestartFile, Snapshot( "old" ) );
		var writes = 0;
		var write = new RestartSnapshotWrite( RestartFile, Snapshot( "new" ), (path, bytes) =>
		{
			if ( ++writes == failingWrite ) throw new IOException( "simulated write failure" );
			ParkResumeSaveFile.SaveBytes( path, bytes );
		} );
		Assert.ThrowsException<IOException>( () => write.Run( TimeSpan.MaxValue ) );
		Assert.AreEqual( "old", ParkResumeSaveFile.Load( RestartFile ).DisplayName );
		Assert.ThrowsException<InvalidDataException>( () => RestartSnapshotWrite.Load( RestartFile ) );
		Assert.IsFalse( write.Done );
		Assert.AreEqual( 0, Directory.GetFiles( root, "*.tmp" ).Length );
	}

	[TestMethod]
	public void DeferredValidationRunsInSlicesOfItsOwnAndTheBytesAreUnchanged()
	{
		var snapshot = Snapshot( "validated later" );
		var steps = 0;
		IEnumerable<bool> Validation()
		{
			for ( var step = 0; step < 3; step++ )
			{
				steps++;
				yield return true;
			}
			steps++;
		}
		var write = new RestartSnapshotWrite( RestartFile, snapshot, cooperative: true, validation: Validation() );
		Assert.AreEqual( 0, steps, "the entering frame does not validate" );
		// a zero budget runs one step per slice; the serialization waits until the last one
		for ( var slice = 1; slice <= 3; slice++ )
		{
			Assert.IsFalse( write.Run( TimeSpan.Zero ) );
			Assert.AreEqual( slice, steps );
		}
		Assert.IsFalse( write.Run( TimeSpan.Zero ), "the validation's end is a slice of its own" );
		Assert.AreEqual( 4, steps );
		Assert.IsFalse( File.Exists( RestartFile ) );
		while ( !write.Run( TimeSpan.Zero ) ) { }
		Assert.AreEqual( 4, steps, "the validation runs once" );
		CollectionAssert.AreEqual( ParkResumeSaveFile.Serialize( snapshot ), File.ReadAllBytes( RestartFile ) );
		// an unlimited budget (restart, shutdown) validates and writes in one call
		File.Delete( RestartFile );
		steps = 0;
		var flushed = new RestartSnapshotWrite( RestartFile, snapshot, cooperative: true, validation: Validation() );
		Assert.IsTrue( flushed.Run( TimeSpan.MaxValue ) );
		Assert.AreEqual( 4, steps );
		Assert.AreEqual( "validated later", RestartSnapshotWrite.Load( RestartFile ).DisplayName );
	}

	[TestMethod]
	public void ARefusedDeferredValidationLeavesNoRestartPointAsARefusedCaptureDid()
	{
		ParkResumeSaveFile.Save( RestartFile, Snapshot( "old park" ) );
		static IEnumerable<bool> Refusal()
		{
			yield return true;
			throw new InvalidDataException( "simulated refusal" );
		}
		var write = new RestartSnapshotWrite( RestartFile, Snapshot( "invalid" ), cooperative: true, validation: Refusal() );
		Assert.IsFalse( write.Run( TimeSpan.Zero ) );
		Assert.IsTrue( File.Exists( RestartFile ), "the earlier snapshot stays until the validation has decided" );
		Assert.ThrowsException<InvalidDataException>( () => write.Run( TimeSpan.Zero ) );
		Assert.IsTrue( write.Done, "a refused write is over" );
		Assert.IsFalse( File.Exists( RestartFile ), "the earlier park's snapshot is not left to be restarted into" );
		Assert.IsFalse( File.Exists( RestartSnapshotWrite.MarkerPath( RestartFile ) ) );
	}

	[TestMethod]
	public void TheValidationInStepsIsTheWholeValidation()
	{
		// the steps hold every check of the whole validation: a snapshot the whole validation refuses is refused by its steps with the same message, and one it accepts passes them
		var snapshot = Snapshot( "steps" );
		var steps = ParkResumeSaveFile.ValidateShapeSteps( snapshot ).Count();
		Assert.IsTrue( steps > 5, $"the shape check is spread over {steps} steps" );
		var broken = snapshot with { Clock = null };
		var whole = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( broken ) ).Message;
		var stepped = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShapeSteps( broken ).Count() ).Message;
		Assert.AreEqual( whole, stepped );
	}

	[TestMethod]
	public void SlicedRestartBytesEqualTheSynchronousFormat()
	{
		var snapshot = Snapshot( "same bytes" );
		var write = new RestartSnapshotWrite( RestartFile, snapshot, cooperative: true ); var slices = 0;
		while ( !write.Run( TimeSpan.Zero ) ) Assert.IsTrue( ++slices < 10000 );
		CollectionAssert.AreEqual( ParkResumeSaveFile.Serialize( snapshot ), File.ReadAllBytes( RestartFile ) );
		Assert.IsTrue( slices > 1 );
		Assert.IsTrue( write.Run( TimeSpan.Zero ), "completed writes are idempotent" );
	}

	[TestMethod]
	public void BrowserCompletionCopiesAndHashesOnlyOneBoundedChunkPerZeroBudgetSlice()
	{
		var snapshot = Snapshot( "chunked" );
		var assets = new System.Collections.Generic.Dictionary<string, string>( snapshot.AssetHashes );
		for ( var i = 0; i < 2000; i++ ) assets.Add( $"/synthetic-{i}.sam", new string( 'a', 64 ) );
		snapshot = snapshot with { AssetHashes = assets };
		var expected = ParkResumeSaveFile.Serialize( snapshot );
		Assert.IsTrue( expected.Length > RestartSnapshotWrite.CopyChunkBytes * 2 );
		var write = new RestartSnapshotWrite( RestartFile, snapshot, cooperative: true );
		var slices = 0; var copySlices = 0;
		while ( !write.Done )
		{
			var before = write.CopiedBytes;
			write.Run( TimeSpan.Zero );
			var copied = write.CopiedBytes - before;
			Assert.IsTrue( copied >= 0 && copied <= RestartSnapshotWrite.CopyChunkBytes );
			if ( copied > 0 ) copySlices++;
			Assert.IsTrue( ++slices < 10000 );
		}
		Assert.IsTrue( copySlices >= 3 );
		CollectionAssert.AreEqual( expected, File.ReadAllBytes( RestartFile ) );
	}

	[TestMethod]
	public void DesktopCompletionRemainsPendingUntilItsDetachedWorkerFinishes()
	{
		Action? queued = null;
		var finished = new System.Threading.Tasks.TaskCompletionSource( System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously );
		var snapshot = Snapshot( "worker" );
		var write = new RestartSnapshotWrite( RestartFile, snapshot, cooperative: false,
			background: work => { queued = work; return finished.Task; } );
		for ( var slices = 0; queued == null; slices++ )
		{
			Assert.IsFalse( write.Run( TimeSpan.Zero ) );
			Assert.IsTrue( slices < 10000 );
		}
		Assert.IsFalse( write.Done ); Assert.IsFalse( File.Exists( RestartFile ) );
		Assert.IsFalse( write.Run( TimeSpan.Zero ), "ordinary frames never wait for the worker" );
		queued!(); finished.SetResult();
		Assert.IsTrue( write.Run( TimeSpan.MaxValue ), "the lifecycle drain observes the completed worker" );
		CollectionAssert.AreEqual( ParkResumeSaveFile.Serialize( snapshot ), File.ReadAllBytes( RestartFile ) );
	}

	[TestMethod]
	public async System.Threading.Tasks.Task UnlimitedDrainWaitsForTheDesktopWorker()
	{
		var queued = new System.Threading.Tasks.TaskCompletionSource<Action>( System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously );
		var finished = new System.Threading.Tasks.TaskCompletionSource( System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously );
		var write = new RestartSnapshotWrite( RestartFile, Snapshot( "drain" ), cooperative: false,
			background: work => { queued.SetResult( work ); return finished.Task; } );
		var drain = System.Threading.Tasks.Task.Run( () => write.Run( TimeSpan.MaxValue ) );
		await System.Threading.Tasks.Task.WhenAny( queued.Task, drain );
		if ( !queued.Task.IsCompleted ) { await drain; Assert.Fail( "The drain ended without scheduling its worker." ); }
		var complete = await queued.Task;
		try
		{
			Assert.IsFalse( drain.IsCompleted, "restart/shutdown must not return while the write is pending" );
			complete();
		}
		finally { finished.SetResult(); }
		Assert.IsTrue( await drain ); Assert.IsTrue( write.Done );
	}

	[TestMethod]
	public void DesktopWorkerFailureReachesTheDrainAndCannotExposeThePreviousPark()
	{
		ParkResumeSaveFile.Save( RestartFile, Snapshot( "old" ) );
		var write = new RestartSnapshotWrite( RestartFile, Snapshot( "new" ), cooperative: false,
			background: _ => System.Threading.Tasks.Task.FromException( new IOException( "worker failed" ) ) );
		Assert.ThrowsException<IOException>( () => write.Run( TimeSpan.MaxValue ) );
		Assert.IsFalse( write.Done );
		Assert.AreEqual( "old", ParkResumeSaveFile.Load( RestartFile ).DisplayName );
		Assert.ThrowsException<InvalidDataException>( () => RestartSnapshotWrite.Load( RestartFile ) );
	}

	[TestMethod]
	public void RealWorldEntrySnapshotStaysUnchangedWhileTheParkTicksBetweenSlices()
	{
		using var renderer = new AutomaticCheckpointCpuRendererScope();
		var fixture = new UpgradeCatalogueDefaultsTests(); fixture.Setup();
		try
		{
			var type = typeof( UpgradeCatalogueDefaultsTests );
			var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { "", 0, false } )!;
			var assets = (System.Collections.Generic.Dictionary<string, string>)type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;
			typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
			typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
			var expected = ParkResumeSaveFile.Serialize( world.Level.CaptureResumeSnapshot( null, "jungle", assets ) );
			var before = world.Level.ComputeStateHash();
			var flow = Flow( world.Level );
			flow.TakeRestartSnapshot( world.Level, assets, overFrames: true );
			Assert.IsTrue( flow.RestartSnapshotPending );
			Assert.AreEqual( before, world.Level.ComputeStateHash(), "capture does not advance the live park" );
			for ( var tick = 0; tick < 600; tick++ )
			{
				world.Level.AdvanceSimulation( FixedStepClock.TickDuration );
				flow.PumpRestartSnapshot( TimeSpan.Zero );
			}
			flow.FlushRestartSnapshot();
			Assert.AreNotEqual( before, world.Level.ComputeStateHash(), "the live world really advanced" );
			CollectionAssert.AreEqual( expected, File.ReadAllBytes( RestartFile ) );
		}
		finally { fixture.Cleanup(); }
	}

	[TestMethod]
	public void TheCaptureWarmUpOfAParkBuildChangesNeitherTheParkNorItsSnapshot()
	{
		using var renderer = new AutomaticCheckpointCpuRendererScope();
		var fixture = new UpgradeCatalogueDefaultsTests(); fixture.Setup();
		try
		{
			var type = typeof( UpgradeCatalogueDefaultsTests );
			var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { "", 0, false } )!;
			var assets = (System.Collections.Generic.Dictionary<string, string>)type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;
			typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
			typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
			var before = world.Level.ComputeStateHash();
			var expected = ParkResumeSaveFile.Serialize( world.Level.CaptureResumeSnapshot( null, "jungle", assets ) );
			var steps = world.Level.WarmResumeCaptureSteps().Count();
			Assert.IsTrue( steps >= 4, $"the warm-up runs its parts in {steps} steps" );
			Assert.AreEqual( before, world.Level.ComputeStateHash(), "the warm-up does not change the park" );
			CollectionAssert.AreEqual( expected, ParkResumeSaveFile.Serialize( world.Level.CaptureResumeSnapshot( null, "jungle", assets ) ), "nor its snapshot" );
		}
		finally { fixture.Cleanup(); }
	}

	[TestMethod]
	public void RestartAfterBuildingReturnsToTheStartState()
	{
		var level = Live(); var flow = Flow( level ); var live = "start"; var captures = 0;
		flow.RestartCaptureOverride = _ => { captures++; return Snapshot( live ); };
		ParkResumeSnapshot? resumed = null; flow.RestartResumeOverride = snapshot => resumed = snapshot;
		flow.TakeRestartSnapshot( level );
		live = "after building"; // the park moves on; nothing retakes the snapshot
		flow.RestartCurrentPark( level );
		Assert.AreEqual( 1, captures, "one snapshot per park start" );
		Assert.AreEqual( "start", resumed?.DisplayName, "Restart Park reloads the snapshot of the start, not the current state" );
	}

	[TestMethod]
	public void RestartOfALoadedParkReturnsToTheLoadedState()
	{
		var level = Live( ParkStartKind.OriginalSaveReference ); var flow = Flow( level ); var live = "level start";
		flow.RestartCaptureOverride = _ => Snapshot( live );
		ParkResumeSnapshot? resumed = null; flow.RestartResumeOverride = snapshot => resumed = snapshot;
		flow.TakeRestartSnapshot( level );
		live = "loaded park"; // a save load runs the same state-15 write again, so the snapshot is the loaded state
		flow.TakeRestartSnapshot( level );
		live = "built after the load";
		flow.RestartCurrentPark( level );
		Assert.AreEqual( "loaded park", resumed?.DisplayName );
	}

	[TestMethod]
	public void AnOnlineVisitTakesNoSnapshotAndCannotRestart()
	{
		var level = Live( visit: true ); var flow = Flow( level ); var captures = 0;
		flow.RestartCaptureOverride = _ => { captures++; return Snapshot( "visit" ); };
		var resumes = 0; flow.RestartResumeOverride = _ => resumes++;
		flow.TakeRestartSnapshot( level ); flow.RestartCurrentPark( level );
		Assert.AreEqual( 0, captures ); Assert.AreEqual( 0, resumes ); Assert.IsFalse( File.Exists( RestartFile ) );
		Assert.IsFalse( GameFlow.TakesRestartSnapshot( level ) ); Assert.IsFalse( GameFlow.RestartEligible( level ) );
	}

	[TestMethod]
	public void TheSnapshotIsAFileSoARelaunchedGameRestartsFromIt()
	{
		var level = Live();
		var first = Flow( level ); first.RestartCaptureOverride = _ => Snapshot( "before quitting" ); first.TakeRestartSnapshot( level );
		Assert.IsTrue( File.Exists( RestartFile ) );
		var relaunched = Flow( level ); ParkResumeSnapshot? resumed = null; relaunched.RestartResumeOverride = snapshot => resumed = snapshot;
		relaunched.RestartCurrentPark( level );
		Assert.AreEqual( "before quitting", resumed?.DisplayName );
	}

	[TestMethod]
	public void AFailedCaptureLeavesNoEarlierParksSnapshotToRestartInto()
	{
		var level = Live( ParkStartKind.OriginalSaveReference ); var flow = Flow( level );
		flow.RestartCaptureOverride = _ => Snapshot( "first park" ); flow.TakeRestartSnapshot( level ); Assert.IsTrue( File.Exists( RestartFile ) );
		flow.RestartCaptureOverride = _ => throw new InvalidOperationException( "not resumable" ); flow.TakeRestartSnapshot( level );
		Assert.IsFalse( File.Exists( RestartFile ) );
	}

	[TestMethod]
	public void ASnapshotOfAnotherThemeIsNotReloaded()
	{
		var level = Live( ParkStartKind.OriginalSaveReference ); var flow = Flow( level );
		ParkResumeSaveFile.Save( RestartFile, Snapshot( "jungle snapshot" ) );
		File.Copy( RestartFile, SaveFileSystem.GetAbsolutePath( GameFlow.RestartSnapshotFileName( "fantasy" ) ) ); // a misplaced file: jungle's park under fantasy's name
		Set( level, "<LevelName>k__BackingField", "fantasy" );
		var resumes = 0; flow.RestartResumeOverride = _ => resumes++;
		flow.RestartCurrentPark( level );
		Assert.AreEqual( 0, resumes );
	}

	// the original's pause menu builder 0x48c150 lists Restart Park for every game type but the online visit
	[DataTestMethod, DataRow( ParkStartKind.InstantAction ), DataRow( ParkStartKind.OriginalSaveReference ), DataRow( ParkStartKind.FullSimulation )]
	public void EveryOriginalParkOffersRestart( ParkStartKind kind )
	{
		var level = Live( kind );
		Assert.IsTrue( GameFlow.TakesRestartSnapshot( level ), "every original park takes the snapshot" );
		Assert.IsTrue( GameFlow.RestartEligible( level ) );
		Set( level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		Assert.IsFalse( GameFlow.RestartEligible( level ), "the online visit has no Restart Park line" );
	}

	[TestMethod]
	public void AFullSimulationRestartKeepsThePlayerWideTicketLedger()
	{
		var ledgerPath = SaveFileSystem.GetAbsolutePath( PlayerTicketFile.FileName );
		var ledger = new PlayerTicketLedger();
		PlayerTicketFile.Save( ledgerPath, ledger.Capture() with { ExtraKeys = 1 } );
		var level = Live( ParkStartKind.FullSimulation ); var flow = Flow( level );
		flow.RestartCaptureOverride = _ => Snapshot( "start" ); flow.TakeRestartSnapshot( level );
		// the park earns a ticket after its start; the ledger file records it
		var earned = PlayerTicketFile.ReadIfPresent( ledgerPath )!.Ledger;
		Assert.IsTrue( earned.RecordEarned( "jungle", GoldenTicketKind.Visitors ) );
		PlayerTicketFile.Save( ledgerPath, earned.Capture() );
		var before = File.ReadAllBytes( ledgerPath );
		ParkResumeSnapshot? resumed = null; flow.RestartResumeOverride = snapshot => resumed = snapshot;
		flow.RestartCurrentPark( level );
		Assert.AreEqual( "start", resumed?.DisplayName, "a Full Simulation park restarts to its snapshot" );
		CollectionAssert.AreEqual( before, File.ReadAllBytes( ledgerPath ), "the restart neither reads nor rewrites the ledger" );
		// the restarted park starts without the ticket again; attaching the ledger to it keeps what the player earned
		using var owner = new PlayerProgressRuntimeOwner( ledgerPath, "jungle", PlayerTicketFile.ReadIfPresent( ledgerPath )!,
			ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation ), () => true, _ => Assert.Fail() );
		Assert.IsTrue( owner.TryFlush() );
		var kept = PlayerTicketFile.LoadIfPresent( ledgerPath )!;
		Assert.AreEqual( 1, kept.EarnedTickets ); Assert.AreEqual( 1, kept.ExtraKeys );
	}

	[TestMethod]
	public void ARestartRefusesAStaleLevel()
	{
		var level = Live(); var flow = Flow( level ); var resumes = 0; flow.RestartResumeOverride = _ => resumes++;
		flow.RestartCaptureOverride = _ => Snapshot( "start" ); flow.TakeRestartSnapshot( level );
		flow.RestartCurrentPark( Live() );
		Assert.AreEqual( 0, resumes );
	}
}
