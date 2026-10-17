using System;
using System.IO;
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

	[DataTestMethod, DataRow( ParkStartKind.InstantAction, true ), DataRow( ParkStartKind.OriginalSaveReference, true ), DataRow( ParkStartKind.FullSimulation, false )]
	public void OnlyParksWithoutAProgressLedgerOfferRestart( ParkStartKind kind, bool offered )
	{
		var level = Live( kind );
		Assert.IsTrue( GameFlow.TakesRestartSnapshot( level ), "every original park takes the snapshot" );
		Assert.AreEqual( offered, GameFlow.RestartEligible( level ) );
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
