using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class PlayerProgressIngressTests
{
	private string root = null!;
	private BaseFileSystem previousFiles = null!, previousSaves = null!;
	private Logger previousLog = null!;
	private GameEdition previousEdition;
	private Func<string, bool>? previousReady;
	private GameFlow flow = null!;
	private FrontEndMenu menu = null!;
	private UiContext context = null!;
	private int built;
	private string Profile => Path.Combine( root, PlayerTicketFile.FileName );
	[TestInitialize]
	public void Initialize()
	{
		previousFiles = FileSystem; previousSaves = SaveFileSystem; previousLog = Log; previousEdition = GameEditions.Current; previousReady = GameFlow.LevelDataReady;
		Log = new(); GameEditions.Select( GameEdition.ThemeParkWorld ); GameFlow.LevelDataReady = null;
		root = Directory.CreateTempSubdirectory( "opentpw-progress-entry-" ).FullName;
		SaveFileSystem = new( root ); FileSystem = new( Path.Combine( root, "data" ) );
		WriteCost( "jungle", "1" ); WriteCost( "fantasy", "3" );
		flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		Set( flow, "<Strings>k__BackingField", OriginalUiTests.FakeStrings() );
		flow.ProgressCaptureOverride = () => false;
		flow.ProgressBuildOverride = ( _, _, commit ) => { built++; commit(); };
		FrontEndMenu? owner = null;
		owner = new( OriginalUiTests.FakeStrings(), new[] { Island( "jungle" ), Island( "fantasy" ) }, new FrontEndActions
		{
			StartPark = ( island, mode ) => flow.RequestParkStart( island.Level, mode, owner )
		} );
		menu = owner; Set( flow, "<Menu>k__BackingField", menu );
		context = new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) );
	}
	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previousFiles; SaveFileSystem = previousSaves; Log = previousLog; GameEditions.Select( previousEdition ); GameFlow.LevelDataReady = previousReady;
		Directory.Delete( root, true );
	}
	private static LobbyIslandInfo Island( string theme ) => new( 0, theme, "", "", "", theme, 0, 0, (0, 0, 0), Array.Empty<string>(), false, 0 );
	private static void Set( object target, string name, object? value ) => target.GetType().GetField( name, BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( target, value );
	private void WriteCost( string theme, string value ) { var path = Path.Combine( root, "data", "levels", theme, "global.sam" ); Directory.CreateDirectory( Path.GetDirectoryName( path )! ); File.WriteAllText( path, "Keys.CostToEnter " + value + "\n" ); }
	private void Click( string id ) => menu.Stack.Update( context, UiInput.Click( menu.Stack.Top!.Find( id )!.ScreenRect( context.Canvas ).Center ) );
	private void Choose( string mode, string theme = "jungle" ) { menu.SelectLevel( theme ); menu.ShowGameMode(); Click( mode ); }
	private void Pending()
	{
		var field = typeof( GameFlow ).GetField( "pending", BindingFlags.NonPublic | BindingFlags.Instance )!;
		var action = (Action?)field.GetValue( flow ); field.SetValue( flow, null ); action?.Invoke();
	}
	private void Known( int keys, ParkGameMode mode = ParkGameMode.FullSimulation ) => PlayerTicketFile.Save( Profile, new PlayerTicketLedger( mode ).Capture() with { ExtraKeys = keys } );

	[DataTestMethod]
	[DataRow( 0, "jungle", 0 )]
	[DataRow( 2, "fantasy", 0 )]
	[DataRow( 3, "fantasy", 1 )]
	public void ActualFullMenuUsesKnownKeysBeforeCandidateConstruction( int keys, string theme, int expected )
	{
		Known( keys ); var before = File.ReadAllBytes( Profile );
		Choose( "fullSimulation", theme ); Pending(); Assert.AreEqual( expected, built );
		CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) );
	}
	[TestMethod]
	public void MissingHistoryRequiresConsentThenPublishesOneKeyBeforeActivation()
	{
		Choose( "fullSimulation" ); Assert.AreEqual( "newProgress", menu.Stack.Top!.Name ); Assert.IsFalse( File.Exists( Profile ) );
		Click( "choice0" ); Pending(); Assert.AreEqual( 1, built );
		Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( Profile )!.ExtraKeys );
		flow.RequestParkStart( "jungle", GameMode.FullSimulation ); Pending(); Assert.AreEqual( 2, built );
		Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( Profile )!.ExtraKeys );
	}
	[DataTestMethod]
	[DataRow( "back" )]
	[DataRow( "backAccept" )]
	[DataRow( "right" )]
	[DataRow( "captured" )]
	[DataRow( "retired" )]
	public void ConsentCancellationCaptureAndRetiredCallbacksCannotCreateProgress( string gate )
	{
		Choose( "fullSimulation" ); var consent = menu.Stack.Top!; var activate = ((UiButton)consent.Find( "choice0" )!).Clicked;
		if ( gate == "back" ) Click( "choice1" );
		if ( gate == "backAccept" ) menu.Stack.Update( context, UiInput.Key( UiKeys.Back | UiKeys.Accept ) );
		if ( gate == "right" ) menu.Stack.Update( context, new( new( -1, -1 ), false, false, false, true, UiKeys.Accept ) );
		if ( gate == "captured" ) { flow.ProgressCaptureOverride = () => true; activate(); }
		if ( gate == "retired" ) { menu.Stack.Pop(); menu.Stack.Push( new UiScreen( "fresh" ) ); activate(); }
		Pending(); Assert.AreEqual( 0, built ); Assert.IsFalse( File.Exists( Profile ) );
		if ( gate == "retired" ) Assert.AreEqual( "fresh", menu.Stack.Top!.Name );
	}
	[TestMethod]
	public void RefusedFantasyCannotManufactureStarterProfileAndKnownInstantModeIsNotReset()
	{
		// Fantasy costs 3 keys: a new history could only start with one, so the refusal comes before any consent
		Choose( "fullSimulation", "fantasy" ); Assert.AreNotEqual( "newProgress", menu.Stack.Top?.Name ); Pending(); Assert.AreEqual( 0, built ); Assert.IsFalse( File.Exists( Profile ) );
		menu.Stack.Clear(); Known( 9, ParkGameMode.InstantAction ); var before = File.ReadAllBytes( Profile );
		Choose( "fullSimulation" ); Pending(); Assert.AreEqual( 0, built ); CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) );
	}
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void InstantActionBypassesKeysAndNeverCreatesOrResetsProfile( bool existing )
	{
		if ( existing ) Known( 0 ); var before = existing ? File.ReadAllBytes( Profile ) : null;
		Choose( "instantAction", "fantasy" ); Pending(); Assert.AreEqual( 1, built );
		if ( before == null ) Assert.IsFalse( File.Exists( Profile ) ); else CollectionAssert.AreEqual( before, File.ReadAllBytes( Profile ) );
	}
	[TestMethod]
	public void FileOrCostChangedAfterConsentOrQueueRefusesBeforeCandidate()
	{
		Choose( "fullSimulation" ); Known( 7 ); var changed = File.ReadAllBytes( Profile ); Click( "choice0" ); Pending();
		Assert.AreEqual( 0, built ); CollectionAssert.AreEqual( changed, File.ReadAllBytes( Profile ) );
		menu.Stack.Clear(); Known( 1 ); Choose( "fullSimulation" ); WriteCost( "jungle", "0" ); Pending(); Assert.AreEqual( 0, built );
	}
	[DataTestMethod]
	[DataRow( "missing" )]
	[DataRow( "-1" )]
	[DataRow( "1.5" )]
	[DataRow( "2147483648" )]
	public void RequiredTrustedCostNeverFallsBackToZero( string value )
	{
		Known( 9 ); if ( value == "missing" ) File.Delete( Path.Combine( root, "data", "levels", "jungle", "global.sam" ) ); else WriteCost( "jungle", value );
		Choose( "fullSimulation" ); Pending(); Assert.AreEqual( 0, built );
	}
	[TestMethod]
	public void QueueLeaseRetirementAndReplacedMenuCannotStartFromOldCallbacks()
	{
		Known( 1 ); Choose( "fullSimulation" ); menu.Stack.Pop(); menu.Stack.Push( new UiScreen( "fresh" ) ); Pending(); Assert.AreEqual( 0, built );
		Assert.AreEqual( "fresh", menu.Stack.Top!.Name );
		menu.Stack.Clear(); menu.ShowGameMode(); var action = ((UiButton)menu.Stack.Top!.Find( "fullSimulation" )!).Clicked;
		Set( flow, "<Menu>k__BackingField", new FrontEndMenu( OriginalUiTests.FakeStrings(), new[] { Island( "jungle" ) }, new FrontEndActions() ) );
		action(); Pending(); Assert.AreEqual( 0, built );
	}
	[TestMethod]
	public void ProfileWriteFailureRetainsLiveLevelAndLaterActivationFailureKeepsOneKey()
	{
		var live = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ); Set( flow, "<Level>k__BackingField", live );
		Choose( "fullSimulation" ); Click( "choice0" );
		// A directory at the expected file is an actual source/IO refusal before publication.
		Directory.CreateDirectory( Profile ); Pending(); Assert.AreEqual( 0, built ); Assert.AreSame( live, flow.Level );
		Directory.Delete( Profile ); menu.Stack.Clear();
		flow.ProgressBuildOverride = ( _, _, commit ) => { built++; commit(); throw new IOException( "Synthetic activation failure after precommit." ); };
		Choose( "fullSimulation" ); Click( "choice0" ); Pending(); Assert.AreEqual( 1, built ); Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( Profile )!.ExtraKeys );
		Assert.AreSame( live, flow.Level );
	}
	[TestMethod]
	public void ActualManualContinueUsesSavedModeWithoutAnyOwnProfileOrKeyGate()
	{
		var saved = ParkResumeSaveFileTests.Envelope(); var path = SaveFileSystem.GetAbsolutePath( "opentpw-jungle-world.json" ); ParkResumeSaveFile.Save( path, saved );
		var entry = flow.FindContinueEntry( "jungle" )!; var resumed = 0;
		Assert.IsTrue( flow.TryContinuePark( entry, snapshot => { resumed++; Assert.AreEqual( saved.Economy.Mode, snapshot.Economy.Mode ); } ) );
		Assert.AreEqual( 1, resumed ); Assert.IsFalse( File.Exists( Profile ) );
	}
	[TestMethod]
	public void ProfilePrecommitRejectsChangedSourceAndCostWithoutPublishingOrDoubleCommit()
	{
		var active = true; var cost = 1;
		var admission = new PlayerProgressAdmission( Profile, "jungle", null, 1, () => cost, () => active, () => { } );
		admission.Validate(); cost = 0; Assert.ThrowsException<InvalidDataException>( admission.BeforeRetirement ); Assert.IsFalse( File.Exists( Profile ) );
		cost = 1; active = false; Assert.ThrowsException<InvalidOperationException>( admission.BeforeRetirement ); Assert.IsFalse( File.Exists( Profile ) );
		active = true; admission.BeforeRetirement(); Assert.ThrowsException<InvalidOperationException>( admission.BeforeRetirement ); Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( Profile )!.ExtraKeys );
	}
	[TestMethod]
	public void DirectFullStartAndCapturedMenuRefuseBeforeFactoryWhileContinueRemainsAvailable()
	{
		Known( 0 ); flow.StartPark( "jungle", GameMode.FullSimulation ); Pending(); Assert.AreEqual( 0, built );
		menu.Stack.Clear(); Known( 3 ); flow.ProgressCaptureOverride = () => true; Choose( "fullSimulation" ); Pending(); Assert.AreEqual( 0, built );
		Assert.AreEqual( 3, PlayerTicketFile.LoadIfPresent( Profile )!.ExtraKeys );
	}

	[TestMethod]
	public void CombinedConsentInputAndSeparateFramesPublishOnlyOncePerAdmittedRequest()
	{
		Choose( "fullSimulation" ); var consent = menu.Stack.Top!; var button = consent.Find( "choice0" )!;
		menu.Stack.Update( context, UiInput.Click( button.ScreenRect( context.Canvas ).Center ) with { Pressed = UiKeys.Accept } );
		Pending(); Assert.AreEqual( 1, built ); Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( Profile )!.ExtraKeys );
		menu.Stack.Pop(); menu.ShowGameMode(); Click( "fullSimulation" ); Pending(); Assert.AreEqual( 2, built );
	}

	[TestMethod]
	public void CandidatePrecommitRealWriteFailureLeavesLiveWorldAndNoProfileThenRetrySucceeds()
	{
		if ( OperatingSystem.IsWindows() ) { Assert.Inconclusive( "Unix directory permission control; portable rename controls are separate." ); return; }
		var live = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ); Set( flow, "<Level>k__BackingField", live );
		var mode = File.GetUnixFileMode( root );
		flow.ProgressBuildOverride = ( _, _, commit ) =>
		{
			if ( OperatingSystem.IsWindows() ) throw new InvalidOperationException( "Unix permission control." );
			built++;
			try
			{
				File.SetUnixFileMode( root, mode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite) );
				var probe = Path.Combine( root, "probe.tmp" );
				try { using var stream = File.Create( probe ); File.Delete( probe ); Assert.Inconclusive( "Current identity bypasses directory permissions." ); } catch ( UnauthorizedAccessException ) { }
				commit();
			}
			finally { File.SetUnixFileMode( root, mode ); }
		};
		Choose( "fullSimulation" ); Click( "choice0" ); Pending(); Assert.AreEqual( 1, built ); Assert.AreSame( live, flow.Level ); Assert.IsFalse( File.Exists( Profile ) );
		Assert.AreEqual( 0, Directory.GetFiles( root, "*.tmp" ).Length );
		menu.Stack.Clear(); flow.ProgressBuildOverride = ( _, _, commit ) => { built++; commit(); };
		Choose( "fullSimulation" ); Click( "choice0" ); Pending(); Assert.AreEqual( 2, built ); Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( Profile )!.ExtraKeys );
	}

	[TestMethod]
	public void LateAssetReadinessStillChecksTrustedCostBeforeConstructingCandidate()
	{
		Known( 0 ); File.Delete( Path.Combine( root, "data", "levels", "jungle", "global.sam" ) );
		Choose( "fullSimulation" ); Assert.AreEqual( 0, built );
		WriteCost( "jungle", "1" ); Pending(); Assert.AreEqual( 0, built );
	}

}
