using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using OpenTPW.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class AutomaticCheckpointIngressTests
{
	private string root = null!;
	private BaseFileSystem previousFiles = null!;
	private GameEdition previousEdition;
	private UiContext context = null!;
	private Logger previousLog = null!;
	[TestInitialize] public void Initialize() { previousLog = Log; Log = new(); previousFiles = SaveFileSystem; previousEdition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkWorld ); root = Directory.CreateTempSubdirectory( "opentpw-auto-ingress-" ).FullName; SaveFileSystem = new( root ); context = new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ); }
	[TestCleanup] public void Cleanup() { Log = previousLog; SaveFileSystem = previousFiles; GameEditions.Select( previousEdition ); Directory.Delete( root, true ); }
	private static ParkResumeSnapshot Snapshot() => ParkResumeSaveFileTests.Envelope() with { Gate = new ParkGate.ParkGateSnapshot( 0 ), Staff = new StaffWorldSimulation.StaffWorldStateSnapshot( Array.Empty<StaffWorldSimulation.StaffActorSnapshot>() ) };
	private static LobbyIslandInfo Island() => new( 0, "jungle", "", "", "", "jungle", 0, 0, (0, 0, 0), Array.Empty<string>(), false, 0 );
	private static void Optional( object target, string name, object value ) => target.GetType().GetProperty( name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance )?.SetValue( target, value );
	private void Click( FrontEndMenu menu, string id ) => menu.Stack.Update( context, UiInput.Click( menu.Stack.Top!.Find( id )!.ScreenRect( context.Canvas ).Center ) );
	[TestMethod]
	public void ActualThemeEnterOffersAutomaticOnlyAndActivatesItsExactEntry()
	{
		ParkResumeSaveFile.Save( AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" ), Snapshot() );
		var entry = AutomaticThemeContinuation.Probe( SaveFileSystem, "jungle" )!; var activated = 0;
		var actions = new FrontEndActions(); Optional( actions, "AutomaticEntry", (Func<string, AutomaticParkContinueEntry?>)(_ => entry) ); Optional( actions, "ContinueAutomatic", (Action<AutomaticParkContinueEntry>)(selected => { Assert.AreSame( entry, selected ); activated++; }) );
		var menu = new FrontEndMenu( OriginalUiTests.FakeStrings(), new[] { Island() }, actions );
		Click( menu, "enterPark" ); Assert.AreEqual( "themeStart", menu.Stack.Top!.Name ); Assert.IsNotNull( menu.Stack.Top.Find( "choice0" ) ); Click( menu, "choice0" ); Assert.AreEqual( 1, activated ); Assert.IsFalse( File.Exists( SaveFileSystem.GetAbsolutePath( PlayerTicketFile.FileName ) ) );
	}

	private static void Set( object target, string name, object? value ) => target.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );
	private static ParkHud BareHud( Level level, HudHost host )
	{
		var hud = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		Set( hud, "level", level ); Set( hud, "strings", OriginalUiTests.FakeStrings() ); Set( hud, "host", host ); Set( hud, "<Stack>k__BackingField", new UiScreenStack() ); var screen = new UiScreen( "hud" ) { Modal = false }; Set( hud, "hud", screen ); hud.Stack.Push( screen ); return hud;
	}
	[TestMethod]
	public void ActualRetiredPauseExitCannotActOnReplacementPause()
	{
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ); var exits = 0; var hud = BareHud( level, new HudHost { ExitToLobby = () => exits++ } );
		hud.OpenPauseMenu(); var retired = (UiButton)hud.Stack.Top!.Find( "ExitToLobby" )!; hud.Stack.Pop(); hud.OpenPauseMenu(); retired.Clicked(); Assert.AreEqual( 0, exits ); ((UiButton)hud.Stack.Top!.Find( "ExitToLobby" )!).Clicked(); Assert.AreEqual( 1, exits );
	}

	private GameFlow Flow( Level? level = null, ParkHud? hud = null )
	{
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) ); Set( flow, "<Strings>k__BackingField", OriginalUiTests.FakeStrings() ); Set( flow, "<Level>k__BackingField", level ); Set( flow, "<Hud>k__BackingField", hud ); flow.ProgressCaptureOverride = () => false; return flow;
	}
	private static void Pending( GameFlow flow )
	{
		var field = typeof( GameFlow ).GetField( "pending", BindingFlags.Instance | BindingFlags.NonPublic )!; var action = (Action?)field.GetValue( flow ); field.SetValue( flow, null ); action?.Invoke();
	}
	private Level Live()
	{
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ); Set( level, "<LevelName>k__BackingField", "jungle" ); Set( level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation ) ); Set( level, "<OriginalPark>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( OriginalPark ) ) ); Set( level, "<Guests>k__BackingField", new GuestSimulation( new GuestPathGrid( 32, 32 ), GuestTests.CreateSettings(), 7 ) ); return level;
	}
	private (GameFlow Flow, Level Level, ParkHud Hud) ExitWorld()
	{
		var level = Live(); GameFlow? flow = null; ParkHud? hud = null; hud = BareHud( level, new HudHost { ExitToLobby = () => flow!.RequestAutomaticExit( level, hud! ) } ); flow = Flow( level, hud ); hud.OpenPauseMenu(); return (flow, level, hud);
	}
	private void Exit( (GameFlow Flow, Level Level, ParkHud Hud) w ) => w.Hud.Stack.Update( context, UiInput.Click( w.Hud.Stack.Top!.Find( "ExitToLobby" )!.ScreenRect( context.Canvas ).Center ) );
	private void Choice( (GameFlow Flow, Level Level, ParkHud Hud) w, string id ) => w.Hud.Stack.Update( context, UiInput.Click( w.Hud.Stack.Top!.Find( id )!.ScreenRect( context.Canvas ).Center ) );
	[TestMethod]
	public void ActualExitQueuesLatestCaptureBeforeRetirementAndKeepsManualBytes()
	{
		var w = ExitWorld(); var tick = 0L; var captures = 0; var retired = 0; var manual = SaveFileSystem.GetAbsolutePath( "opentpw-jungle-world.json" ); ParkResumeSaveFile.Save( manual, Snapshot() ); var bytes = File.ReadAllBytes( manual );
		w.Flow.AutomaticCaptureOverride = _ => { captures++; return Snapshot() with { Clock = new FixedStepClockSnapshot( tick, 0, 0 ) }; }; w.Flow.AutomaticRetireOverride = _ => { Assert.IsTrue( File.Exists( AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" ) ) ); retired++; };
		Exit( w ); Assert.AreEqual( 0, captures ); tick = 1; Pending( w.Flow ); Assert.AreEqual( 1, captures ); Assert.AreEqual( 1, retired ); Assert.AreEqual( 1, ParkResumeSaveFile.Load( AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" ) ).Clock.TickCount ); CollectionAssert.AreEqual( bytes, File.ReadAllBytes( manual ) );
	}
	[DataTestMethod, DataRow( "capture" ), DataRow( "move" )]
	public void FailedActualExitRetainsWorldAndRetryCapturesFresh( string failure )
	{
		var w = ExitWorld(); var auto = AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" ); ParkResumeSaveFile.Save( auto, Snapshot() ); var before = File.ReadAllBytes( auto ); var captures = 0; var retired = 0; var fail = true;
		w.Flow.AutomaticCaptureOverride = _ => { captures++; if ( fail && failure == "capture" ) throw new NotSupportedException( "live loop remains unsupported" ); return Snapshot() with { DisplayName = "fresh " + captures }; }; w.Flow.AutomaticRetireOverride = _ => retired++;
		if ( failure == "move" ) { File.Delete( auto ); Directory.CreateDirectory( auto ); }
		Exit( w ); Pending( w.Flow ); Assert.AreSame( w.Level, w.Flow.Level ); Assert.AreEqual( 0, retired ); Assert.AreEqual( "automaticFailed", w.Hud.Stack.Top!.Name ); if ( failure == "capture" ) CollectionAssert.AreEqual( before, File.ReadAllBytes( auto ) ); Assert.AreEqual( 0, Directory.GetFiles( root, "*.tmp*" ).Length );
		fail = false; if ( failure == "move" ) Directory.Delete( auto ); Choice( w, "choice0" ); Pending( w.Flow ); Assert.AreEqual( 2, captures ); Assert.AreEqual( 1, retired ); Assert.AreEqual( "fresh 2", ParkResumeSaveFile.Load( auto ).DisplayName );
	}
	[DataTestMethod, DataRow( "backAccept" ), DataRow( "rightAccept" ), DataRow( "retired" ), DataRow( "capture" )]
	public void FailureCancelAndRetainedRetryCannotWrite( string input )
	{
		var w = ExitWorld(); var captures = 0; w.Flow.AutomaticCaptureOverride = _ => { captures++; throw new NotSupportedException( "refused" ); }; w.Flow.AutomaticRetireOverride = _ => Assert.Fail(); Exit( w ); Pending( w.Flow ); var old = w.Hud.Stack.Top!; var retry = ((UiButton)old.Find( "choice0" )!).Clicked;
		if ( input == "backAccept" ) w.Hud.Stack.Update( context, UiInput.Key( UiKeys.Back | UiKeys.Accept ) );
		if ( input == "rightAccept" ) w.Hud.Stack.Update( context, new( new( -1, -1 ), false, false, false, true, UiKeys.Accept ) );
		if ( input == "retired" ) { w.Hud.Stack.Pop(); w.Hud.Stack.Push( new UiScreen( "fresh" ) ); retry(); }
		if ( input == "capture" ) { w.Flow.ProgressCaptureOverride = () => true; retry(); }
		Pending( w.Flow ); Assert.AreEqual( 1, captures ); Assert.AreSame( w.Level, w.Flow.Level ); Assert.IsFalse( File.Exists( AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" ) ) );
	}
	[DataTestMethod, DataRow( "root" ), DataRow( "hud" ), DataRow( "runtime" ), DataRow( "screen" ), DataRow( "profile" ), DataRow( "capture" ), DataRow( "build" ), DataRow( "modal" )]
	public void QueuedExitRefusesForeignLeaseBeforeCapture( string changed )
	{
		var w = ExitWorld(); var captures = 0; w.Flow.AutomaticCaptureOverride = _ => { captures++; return Snapshot(); }; w.Flow.AutomaticRetireOverride = _ => Assert.Fail(); Exit( w );
		if ( changed == "build" ) Set( w.Flow, "parkBuild", RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ).GetNestedType( "ParkBuild", BindingFlags.NonPublic )! ) ); if ( changed == "modal" ) w.Hud.Stack.Push( new UiScreen( "foreignModal" ) ); if ( changed == "capture" ) w.Flow.ProgressCaptureOverride = () => true; if ( changed == "root" ) SaveFileSystem = new( Path.Combine( root, "other" ) ); if ( changed == "hud" ) Set( w.Flow, "<Hud>k__BackingField", BareHud( w.Level, new() ) ); if ( changed == "runtime" ) Set( w.Level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation ) ); if ( changed == "screen" ) w.Hud.Stack.Pop(); if ( changed == "profile" ) File.WriteAllText( Path.Combine( root, PlayerTicketFile.FileName ), "opaque replacement" );
		Pending( w.Flow ); Assert.AreEqual( 0, captures ); Assert.AreSame( w.Level, w.Flow.Level );
	}
	[TestMethod]
	public void LastClickFrameOwnedAwardIsIncludedWithoutAdoptingForeignReceipt()
	{
		var w = ExitWorld(); var profile = SaveFileSystem.GetAbsolutePath( PlayerTicketFile.FileName ); PlayerTicketFile.Save( profile, new PlayerTicketLedger().Capture() ); w.Flow.AttachProgress( w.Level ); var captures = 0; var retired = 0; w.Flow.AutomaticCaptureOverride = _ => { captures++; return Snapshot(); }; w.Flow.AutomaticRetireOverride = _ => retired++; Exit( w );
		var economy = w.Level.Park!.Economy; economy.Counters.Add( ParkCounters.Admissions, 101 ); economy.Advance( ParkCalendar.TickOfTurn( 100 ) - economy.Tick ); Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( profile )!.EarnedTickets );
		Pending( w.Flow ); Assert.AreEqual( 1, captures ); Assert.AreEqual( 1, retired ); typeof( GameFlow ).GetMethod( "DetachProgress", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( w.Flow, null );
	}
	[TestMethod]
	public void LateRetirementFailureEscapesSaveFailureUiAfterCommittedWrite()
	{
		var w = ExitWorld(); w.Flow.AutomaticCaptureOverride = _ => Snapshot(); w.Flow.AutomaticRetireOverride = _ => throw new IOException( "late retirement" ); Exit( w ); Assert.ThrowsException<IOException>( () => Pending( w.Flow ) ); Assert.AreEqual( "pause", w.Hud.Stack.Top!.Name ); Assert.IsTrue( File.Exists( AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" ) ) );
	}
	[DataTestMethod, DataRow( false ), DataRow( true )]
	public void AutomaticOfferChecksProfileBeforeApplyWithoutNewKeyGateOrCreation( bool instant )
	{
		var flow = Flow(); FrontEndMenu? menu = null; var resumed = 0; flow.AutomaticResumeOverride = s => { Assert.AreEqual( instant ? ParkStartKind.InstantAction : ParkStartKind.FullSimulation, s.StartKind ); resumed++; }; menu = new( OriginalUiTests.FakeStrings(), new[] { Island() }, new FrontEndActions { AutomaticEntry = flow.FindAutomaticContinueEntry, ContinueAutomatic = e => flow.RequestAutomaticContinue( menu!, e ) } ); Set( flow, "<Menu>k__BackingField", menu ); ParkResumeSaveFile.Save( AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" ), Snapshot() );
		if ( instant ) { var snapshot = Snapshot(); typeof( ParkSaveFile.Data ).GetProperty( nameof( ParkSaveFile.Data.Mode ) )!.SetValue( snapshot.Economy, ParkGameMode.InstantAction ); ParkResumeSaveFile.Save( AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" ), snapshot with { StartKind = ParkStartKind.InstantAction } ); }
		Click( menu, "enterPark" ); Click( menu, "choice0" ); Pending( flow ); Assert.AreEqual( 1, resumed ); Assert.IsFalse( File.Exists( SaveFileSystem.GetAbsolutePath( PlayerTicketFile.FileName ) ) );
		menu.Stack.Pop(); Click( menu, "enterPark" ); File.WriteAllText( SaveFileSystem.GetAbsolutePath( PlayerTicketFile.FileName ), "external" ); Click( menu, "choice0" ); Pending( flow ); Assert.AreEqual( 1, resumed ); Assert.AreEqual( "progressFailed", menu.Stack.Top!.Name );
	}
	[DataTestMethod, DataRow( false, false ), DataRow( true, false ), DataRow( false, true ), DataRow( true, true )]
	public void ThemeChoicesRemainMountedBoundedAndCancelFirst( bool manual, bool auto )
	{
		if ( manual ) ParkResumeSaveFile.Save( SaveFileSystem.GetAbsolutePath( "opentpw-jungle-world.json" ), Snapshot() ); if ( auto ) ParkResumeSaveFile.Save( AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" ), Snapshot() ); var activations = 0;
		var menu = new FrontEndMenu( OriginalUiTests.FakeStrings(), new[] { Island() }, new FrontEndActions { ContinueEntry = t => ThemeContinuation.Probe( SaveFileSystem, t ), ContinuePark = _ => activations++, AutomaticEntry = t => AutomaticThemeContinuation.Probe( SaveFileSystem, t ), ContinueAutomatic = _ => activations++ } ); Click( menu, "enterPark" ); if ( !manual && !auto ) { Assert.AreEqual( "gameMode", menu.Stack.Top!.Name ); return; }
		var screen = menu.Stack.Top!; var window = screen.Find( "window" )!.ScreenRect( context.Canvas ); foreach ( var button in screen.Elements.OfType<UiButton>() ) { var r = button.ScreenRect( context.Canvas ); Assert.IsTrue( r.X >= window.X && r.Y >= window.Y && r.Right <= window.Right && r.Bottom <= window.Bottom ); }
		var callback = ((UiButton)screen.Find( "choice0" )!).Clicked; menu.Stack.Update( context, UiInput.Key( UiKeys.Back | UiKeys.Accept ) ); callback(); Assert.AreEqual( 0, activations ); Assert.AreSame( menu.Main, menu.Stack.Top );
	}

	[DataTestMethod, DataRow( "visit" ), DataRow( "sandbox" ), DataRow( "reference" ), DataRow( "TPI" )]
	public void UnsupportedContextsExitWithoutWritingAutomaticData( string reason )
	{
		var w = ExitWorld(); w.Flow.AutomaticCaptureOverride = _ => throw new Exception( "Must not capture" ); var retired = 0; w.Flow.AutomaticRetireOverride = _ => retired++;
		if ( reason == "visit" ) Set( w.Level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) ); if ( reason == "sandbox" ) Set( w.Level, "<OriginalPark>k__BackingField", null ); if ( reason == "reference" ) Set( w.Level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.OriginalSaveReference ) ); if ( reason == "TPI" ) GameEditions.Select( GameEdition.ThemeParkInc );
		Exit( w ); Pending( w.Flow ); Assert.AreEqual( 1, retired ); Assert.IsFalse( File.Exists( AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" ) ) );
	}
	[TestMethod]
	public void CleanOwnedProfileReplacementAndDirtyFailedFlushNeverCapture()
	{
		var w = ExitWorld(); var profile = SaveFileSystem.GetAbsolutePath( PlayerTicketFile.FileName ); PlayerTicketFile.Save( profile, new PlayerTicketLedger().Capture() ); w.Flow.AttachProgress( w.Level ); var captures = 0; var retired = 0; w.Flow.AutomaticCaptureOverride = _ => { captures++; return Snapshot(); }; w.Flow.AutomaticRetireOverride = _ => retired++; var before = File.ReadAllBytes( profile ); File.WriteAllText( profile, "external" ); Exit( w ); Pending( w.Flow ); Assert.AreEqual( 0, captures ); Assert.AreSame( w.Level, w.Flow.Level ); Assert.AreEqual( "automaticFailed", w.Hud.Stack.Top!.Name );
		var economy = w.Level.Park!.Economy; economy.Counters.Add( ParkCounters.Admissions, 101 ); economy.Advance( ParkCalendar.TickOfTurn( 100 ) - economy.Tick ); Choice( w, "choice0" ); Pending( w.Flow ); Assert.AreEqual( 0, captures ); Assert.AreEqual( "external", File.ReadAllText( profile ) ); File.WriteAllBytes( profile, before ); Choice( w, "choice0" ); Pending( w.Flow ); Assert.AreEqual( 1, captures ); Assert.AreEqual( 1, retired ); Assert.AreEqual( 1, PlayerTicketFile.LoadIfPresent( profile )!.EarnedTickets ); typeof( GameFlow ).GetMethod( "DetachProgress", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( w.Flow, null );
	}
	[TestMethod]
	public void ActualHostCaptureAndAutomaticResumeUseRealSyntheticSimulationState()
	{
		using var renderer = new AutomaticCheckpointCpuRendererScope();
		var fixture = new UpgradeCatalogueDefaultsTests(); fixture.Setup(); try
		{
			var type = typeof( UpgradeCatalogueDefaultsTests ); var flags = BindingFlags.Instance | BindingFlags.NonPublic;
			var source = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", flags )!.Invoke( fixture, new object[] { "", 0, false } )!;
			var assets = (Dictionary<string, string>)type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { source } )!;
			typeof( Level ).GetMethod( "SetupStaff", flags )!.Invoke( source.Level, null ); typeof( Level ).GetMethod( "SetupParkGate", flags )!.Invoke( source.Level, null );
			var flow = Flow( source.Level ); var hud = (ParkHud)typeof( GameFlow ).GetMethod( "CreateParkHud", flags )!.Invoke( flow, new object[] { source.Level, EconomyParkStatus.ForLevel( source.Level ) } )!; Set( flow, "<Hud>k__BackingField", hud ); hud.OpenPauseMenu(); var retired = 0; flow.AutomaticCaptureOverride = l => l.CaptureResumeSnapshot( null, "actual", assets ); flow.AutomaticRetireOverride = _ => retired++;
			var clock = (FixedStepClock)typeof( Level ).GetField( "simulationClock", flags )!.GetValue( source.Level )!;
			hud.Stack.Update( context, UiInput.Click( hud.Stack.Top!.Find( "ExitToLobby" )!.ScreenRect( context.Canvas ).Center ) ); clock.Advance( FixedStepClock.TickDuration, _ => { } ); Pending( flow ); Assert.AreEqual( 1, retired );
			var entry = AutomaticThemeContinuation.Probe( SaveFileSystem, "jungle" )!; var saved = AutomaticThemeContinuation.Read( SaveFileSystem, entry ); Assert.AreEqual( 1, saved.Clock.TickCount ); Assert.AreEqual( 2, saved.Runtimes.Count ); Assert.AreEqual( source.Level.Guests!.Guests.Count, saved.Guests.Guests.Length );
			var candidate = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", flags )!.Invoke( fixture, new object[] { "", 0, false } )!; type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { candidate } ); typeof( Level ).GetMethod( "SetupStaff", flags )!.Invoke( candidate.Level, null ); typeof( Level ).GetMethod( "SetupParkGate", flags )!.Invoke( candidate.Level, null );
			Assert.IsTrue( flow.TryAutomaticContinue( entry, null, snapshot => typeof( Level ).GetMethod( "RestoreResumeSimulation", flags )!.Invoke( candidate.Level, new object[] { snapshot } ) ) ); Assert.AreEqual( source.Level.Guests.ComputeStateHash(), candidate.Level.Guests!.ComputeStateHash() ); Assert.AreEqual( System.Text.Json.JsonSerializer.Serialize( source.One.Runtime.CaptureSnapshot() ), System.Text.Json.JsonSerializer.Serialize( candidate.One.Runtime.CaptureSnapshot() ) ); Assert.AreEqual( System.Text.Json.JsonSerializer.Serialize( source.Two.Runtime.CaptureSnapshot() ), System.Text.Json.JsonSerializer.Serialize( candidate.Two.Runtime.CaptureSnapshot() ) );
		}
		finally { fixture.Cleanup(); }
	}

	[TestMethod]
	public void RecoveredOwnedFlushDoesNotLetOldNoticeCancelQueuedExit()
	{
		var w = ExitWorld(); var profile = SaveFileSystem.GetAbsolutePath( PlayerTicketFile.FileName ); PlayerTicketFile.Save( profile, new PlayerTicketLedger().Capture() ); w.Flow.AttachProgress( w.Level ); var before = File.ReadAllBytes( profile ); File.WriteAllText( profile, "external" ); var economy = w.Level.Park!.Economy; economy.Counters.Add( ParkCounters.Admissions, 101 ); economy.Advance( ParkCalendar.TickOfTurn( 100 ) - economy.Tick ); File.WriteAllBytes( profile, before );
		var captures = 0; var retired = 0; w.Flow.AutomaticCaptureOverride = _ => { captures++; return Snapshot(); }; w.Flow.AutomaticRetireOverride = _ => retired++; Exit( w ); typeof( GameFlow ).GetMethod( "PumpProgress", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( w.Flow, null ); Pending( w.Flow ); Assert.AreEqual( 1, captures ); Assert.AreEqual( 1, retired ); typeof( GameFlow ).GetMethod( "DetachProgress", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( w.Flow, null );
	}

	[DataTestMethod, DataRow( "world" ), DataRow( "profile" ), DataRow( "retired" ), DataRow( "root" ), DataRow( "capture" )]
	public void ActualAutomaticApplyRefusesChangedOfferBeforeStagedResume( string change )
	{
		var flow = Flow(); FrontEndMenu? menu = null; var resumed = 0; flow.AutomaticResumeOverride = _ => resumed++; menu = new( OriginalUiTests.FakeStrings(), new[] { Island() }, new FrontEndActions { AutomaticEntry = flow.FindAutomaticContinueEntry, ContinueAutomatic = e => flow.RequestAutomaticContinue( menu!, e ) } ); Set( flow, "<Menu>k__BackingField", menu ); var path = AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" ); ParkResumeSaveFile.Save( path, Snapshot() ); Click( menu, "enterPark" ); var screen = menu.Stack.Top!; var callback = ((UiButton)screen.Find( "choice0" )!).Clicked;
		if ( change == "retired" ) { menu.Stack.Pop(); menu.Stack.Push( new UiScreen( "fresh" ) ); callback(); } else { Click( menu, "choice0" ); if ( change == "world" ) ParkResumeSaveFile.Save( path, Snapshot() with { DisplayName = "changed" } ); if ( change == "profile" ) File.WriteAllText( SaveFileSystem.GetAbsolutePath( PlayerTicketFile.FileName ), "external" ); if ( change == "root" ) SaveFileSystem = new( Path.Combine( root, "other" ) ); if ( change == "capture" ) flow.ProgressCaptureOverride = () => true; }
		Pending( flow ); Assert.AreEqual( 0, resumed ); Assert.IsFalse( File.Exists( Path.Combine( root, PlayerTicketFile.FileName ) ) && change != "profile" );
	}
	[TestMethod]
	public void CurrentExitConsumesOnePendingAttemptAndHeldPointerDoesNotActivate()
	{
		var w = ExitWorld(); var captures = 0; var retired = 0; w.Flow.AutomaticCaptureOverride = _ => { captures++; return Snapshot(); }; w.Flow.AutomaticRetireOverride = _ => retired++; var point = w.Hud.Stack.Top!.Find( "ExitToLobby" )!.ScreenRect( context.Canvas ).Center; w.Hud.Stack.Update( context, new( point, true, false, false, false, UiKeys.None ) ); Pending( w.Flow ); Assert.AreEqual( 0, captures ); Exit( w ); Exit( w ); Pending( w.Flow ); Assert.AreEqual( 1, captures ); Assert.AreEqual( 1, retired );
	}
	[TestMethod]
	public void AutomaticChoiceGeometryFitsMinimumAndWideCanvasesAtEveryTextScale()
	{
		ParkResumeSaveFile.Save( SaveFileSystem.GetAbsolutePath( "opentpw-jungle-world.json" ), Snapshot() ); ParkResumeSaveFile.Save( AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" ), Snapshot() ); var menu = new FrontEndMenu( OriginalUiTests.FakeStrings(), new[] { Island() }, new FrontEndActions { ContinueEntry = t => ThemeContinuation.Probe( SaveFileSystem, t ), ContinuePark = _ => { }, AutomaticEntry = t => AutomaticThemeContinuation.Probe( SaveFileSystem, t ), ContinueAutomatic = _ => { } } ); Click( menu, "enterPark" ); foreach ( var size in new[] { (640, 480), (1280, 720), (2048, 1536) } ) foreach ( var scale in new[] { 1, 2, 3 } ) { var canvas = new UiCanvas( size.Item1, size.Item2, scale ); var window = menu.Stack.Top!.Find( "window" )!.ScreenRect( canvas ); foreach ( var button in menu.Stack.Top.Elements.OfType<UiButton>() ) { var bounds = button.ScreenRect( canvas ); Assert.IsTrue( bounds.X >= window.X && bounds.Right <= window.Right && bounds.Y >= window.Y && bounds.Bottom <= window.Bottom ); } }
	}
}
