using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class AutomaticCheckpointIngressIndependentTests
{
	private string root = null!;
	private BaseFileSystem oldSaves = null!, oldFiles = null!;
	private Logger oldLog = null!;
	private GameEdition oldEdition;
	private Vector3 oldListenerPosition, oldListenerForward;
	private UiContext context = null!;
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	[TestInitialize]
	public void Setup()
	{
		oldSaves = SaveFileSystem; oldFiles = FileSystem; oldLog = Log; oldEdition = GameEditions.Current; oldListenerPosition = AudioListener.Position; oldListenerForward = AudioListener.Forward;
		root = Directory.CreateTempSubdirectory( "opentpw-auto-independent-" ).FullName;
		SaveFileSystem = new( root ); FileSystem = new( Path.Combine( root, "empty-assets" ) ); Log = new(); GameEditions.Select( GameEdition.ThemeParkWorld );
		context = new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) );
	}
	[TestCleanup]
	public void Cleanup() { SaveFileSystem = oldSaves; FileSystem = oldFiles; Log = oldLog; GameEditions.Select( oldEdition ); typeof( AudioListener ).GetMethod( "RestoreCaptured", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { oldListenerPosition, oldListenerForward } ); Directory.Delete( root, true ); }
	private static void Set( object target, string name, object? value ) => target.GetType().GetField( name, Private )!.SetValue( target, value );
	private static GameFlow Flow( Level? level = null )
	{
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) ); Set( flow, "<Strings>k__BackingField", OriginalUiTests.FakeStrings() ); Set( flow, "<Level>k__BackingField", level ); flow.ProgressCaptureOverride = () => false; return flow;
	}
	private static ParkHud BareHud( Level level )
	{
		var hud = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) ); Set( hud, "level", level ); Set( hud, "strings", OriginalUiTests.FakeStrings() ); Set( hud, "host", new HudHost() ); Set( hud, "<Stack>k__BackingField", new UiScreenStack() ); var screen = new UiScreen( "hud" ) { Modal = false }; Set( hud, "hud", screen ); hud.Stack.Push( screen ); return hud;
	}
	private static void Pending( GameFlow flow )
	{
		var field = typeof( GameFlow ).GetField( "pending", Private )!; var action = (Action?)field.GetValue( flow ); field.SetValue( flow, null ); action?.Invoke();
	}
	private string Auto => AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" );
	private string Manual => SaveFileSystem.GetAbsolutePath( "opentpw-jungle-world.json" );
	private string Profile => SaveFileSystem.GetAbsolutePath( PlayerTicketFile.FileName );
	private static ParkResumeSnapshot Snapshot( bool instant = false )
	{
		var snapshot = ParkResumeSaveFileTests.Envelope() with { Gate = new ParkGate.ParkGateSnapshot( 0 ), Staff = new StaffWorldSimulation.StaffWorldStateSnapshot( Array.Empty<StaffWorldSimulation.StaffActorSnapshot>() ) };
		if ( !instant ) return snapshot;
		var json = JsonNode.Parse( Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( snapshot ) ) )!;
		json["StartKind"] = (int)ParkStartKind.InstantAction; json["Economy"]!["Mode"] = (int)ParkGameMode.InstantAction;
		return ParkResumeSaveFile.Deserialize( Encoding.UTF8.GetBytes( json.ToJsonString() ) );
	}
	private FrontEndMenu Menu( GameFlow flow )
	{
		FrontEndMenu? menu = null;
		menu = new( OriginalUiTests.FakeStrings(), new[] { new LobbyIslandInfo( 0, "jungle", "", "", "", "jungle", 0, 0, (0, 0, 0), Array.Empty<string>(), false, 0 ) }, new FrontEndActions { AutomaticEntry = flow.FindAutomaticContinueEntry, ContinueAutomatic = entry => flow.RequestAutomaticContinue( menu!, entry ) } );
		Set( flow, "<Menu>k__BackingField", menu ); return menu;
	}
	private void Click( UiScreenStack stack, string id ) => stack.Update( context, UiInput.Click( stack.Top!.Find( id )!.ScreenRect( context.Canvas ).Center ) );

	[DataTestMethod, DataRow( false ), DataRow( true )]
	public void ActualMenuKeepsSavedModeAndDistinguishesSemanticWorldFromRawProfileBinding( bool instant )
	{
		PlayerTicketFile.Save( Profile, new PlayerTicketLedger( ParkGameMode.InstantAction ).Capture() );
		ParkResumeSaveFile.Save( Manual, Snapshot() ); ParkResumeSaveFile.Save( Auto, Snapshot( instant ) );
		var profile = File.ReadAllBytes( Profile ); var manual = File.ReadAllBytes( Manual ); var flow = Flow(); var menu = Menu( flow ); var resumed = 0;
		flow.AutomaticResumeOverride = snapshot => { Assert.AreEqual( instant ? ParkStartKind.InstantAction : ParkStartKind.FullSimulation, snapshot.StartKind ); Assert.AreEqual( instant ? ParkGameMode.InstantAction : ParkGameMode.FullSimulation, snapshot.Economy.Mode ); resumed++; };
		Click( menu.Stack, "enterPark" ); File.WriteAllText( Auto, "\n" + File.ReadAllText( Auto ) + "\n" ); Click( menu.Stack, "choice0" ); Pending( flow );
		Assert.AreEqual( 1, resumed ); CollectionAssert.AreEqual( profile, File.ReadAllBytes( Profile ) ); CollectionAssert.AreEqual( manual, File.ReadAllBytes( Manual ) ); Assert.AreEqual( 0, PlayerTicketFile.LoadIfPresent( Profile )!.ExtraKeys );
		menu.Stack.Pop(); Click( menu.Stack, "enterPark" ); File.AppendAllText( Profile, " " ); var replacement = File.ReadAllBytes( Profile ); Click( menu.Stack, "choice0" ); Pending( flow );
		Assert.AreEqual( 1, resumed ); Assert.AreEqual( "progressFailed", menu.Stack.Top!.Name ); CollectionAssert.AreEqual( replacement, File.ReadAllBytes( Profile ) ); CollectionAssert.AreEqual( manual, File.ReadAllBytes( Manual ) );
	}

	[TestMethod]
	public void ActualMenuHidesOversizedProfileAutomaticOfferWithoutHidingManualContinue()
	{
		ParkResumeSaveFile.Save( Manual, Snapshot() ); ParkResumeSaveFile.Save( Auto, Snapshot() );
		using ( var file = File.Create( Profile ) ) file.SetLength( PlayerTicketFile.MaximumFileSize + 1L );
		var manual = File.ReadAllBytes( Manual ); var auto = File.ReadAllBytes( Auto ); var flow = Flow(); FrontEndMenu? menu = null; var manualCalls = 0; var autoCalls = 0;
		menu = new( OriginalUiTests.FakeStrings(), new[] { new LobbyIslandInfo( 0, "jungle", "", "", "", "jungle", 0, 0, (0, 0, 0), Array.Empty<string>(), false, 0 ) }, new FrontEndActions
		{
			ContinueEntry = theme => ThemeContinuation.Probe( SaveFileSystem, theme ),
			ContinuePark = _ => manualCalls++,
			AutomaticEntry = flow.FindAutomaticContinueEntry,
			ContinueAutomatic = _ => autoCalls++
		} );
		Set( flow, "<Menu>k__BackingField", menu ); Click( menu.Stack, "enterPark" );
		Assert.AreEqual( "themeStart", menu.Stack.Top!.Name ); Assert.AreEqual( 3, menu.Stack.Top.Elements.OfType<UiButton>().Count() );
		Assert.IsFalse( menu.Stack.Top.Elements.OfType<UiButton>().Any( button => button.Text?.Invoke()?.Contains( SupplementaryStrings.Get( OpenTpwText.ContinueAutomaticPark, "English" ), StringComparison.Ordinal ) == true ) );
		Click( menu.Stack, "choice0" ); Assert.AreEqual( 1, manualCalls ); Assert.AreEqual( 0, autoCalls ); Assert.AreEqual( PlayerTicketFile.MaximumFileSize + 1L, new FileInfo( Profile ).Length );
		CollectionAssert.AreEqual( manual, File.ReadAllBytes( Manual ) ); CollectionAssert.AreEqual( auto, File.ReadAllBytes( Auto ) );
	}

	[TestMethod]
	public void ActualAutomaticReadAndResumeAssetPreflightRetainTheLiveWorldAndSlots()
	{
		ParkResumeSaveFile.Save( Auto, Snapshot() ); ParkResumeSaveFile.Save( Manual, Snapshot() ); var auto = File.ReadAllBytes( Auto ); var manual = File.ReadAllBytes( Manual );
		var live = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ); var flow = Flow( live ); var hud = BareHud( live ); Set( flow, "<Hud>k__BackingField", hud );
		var entry = AutomaticThemeContinuation.Probe( SaveFileSystem, "jungle" )!;
		Assert.IsFalse( flow.TryAutomaticContinue( entry, null, snapshot => flow.ResumePark( snapshot ) ) );
		Assert.AreSame( live, flow.Level ); Assert.AreSame( hud, flow.Hud ); Assert.AreEqual( "progressFailed", hud.Stack.Top!.Name );
		CollectionAssert.AreEqual( auto, File.ReadAllBytes( Auto ) ); CollectionAssert.AreEqual( manual, File.ReadAllBytes( Manual ) ); Assert.IsFalse( File.Exists( Profile ) );
	}

	[TestMethod]
	public void ActualOwnedLoopRefusesExitWithoutStoppingOtherVoicesAndFreshRetryUsesRealCapture()
	{
		var worldFixture = new UpgradeCatalogueDefaultsTests(); worldFixture.Setup();
		var audioFixture = new PlacedEventAudioOriginTests(); var audioReady = false;
		try
		{
			var type = typeof( UpgradeCatalogueDefaultsTests );
			var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", Private )!.Invoke( worldFixture, new object[] { "", 0, false } )!;
			var assets = (Dictionary<string, string>)type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;
			typeof( Level ).GetMethod( "SetupStaff", Private )!.Invoke( world.Level, null ); typeof( Level ).GetMethod( "SetupParkGate", Private )!.Invoke( world.Level, null );
			ParkResumeSaveFile.Save( Auto, world.Level.CaptureResumeSnapshot( null, "previous", assets ) ); ParkResumeSaveFile.Save( Manual, world.Level.CaptureResumeSnapshot( null, "manual", assets ) ); var auto = File.ReadAllBytes( Auto ); var manual = File.ReadAllBytes( Manual );
			audioFixture.Setup(); audioReady = true;
			var audioType = typeof( PlacedEventAudioOriginTests ); var emit = audioType.GetMethod( "Event", Private )!;
			emit.Invoke( audioFixture, new object[] { world.One.Runtime, 3, 44 } );
			var audioEntry = (ObjectCatalogEntry)audioType.GetField( "entry", Private )!.GetValue( audioFixture )!;
			var foreign = new OriginalObject( audioEntry, new ObjectPlacement( 10, 10, 0, System.Numerics.Vector2.Zero, 0 ) ); emit.Invoke( audioFixture, new object[] { foreign.Runtime, 3, 44 } );
			var mixer = (AudioMixer)audioType.GetField( "mixer", Private )!.GetValue( audioFixture )!; var ownedVoice = mixer.Voices.First(); var otherVoice = mixer.Voices.Last(); Assert.IsTrue( ownedVoice.Loops && otherVoice.Loops ); AudioListener.Update( ownedVoice.Position!.Value, new Vector3( 0, 0, 1 ) ); Assert.IsTrue( mixer.Mix( 1 ).Any( sample => sample != 0 ) );
			var flow = Flow( world.Level ); var hud = (ParkHud)typeof( GameFlow ).GetMethod( "CreateParkHud", Private )!.Invoke( flow, new object[] { world.Level, EconomyParkStatus.ForLevel( world.Level ) } )!; Set( flow, "<Hud>k__BackingField", hud ); hud.OpenPauseMenu(); var retired = 0;
			flow.AutomaticCaptureOverride = level => level.CaptureResumeSnapshot( null, "fresh", assets ); flow.AutomaticRetireOverride = _ => retired++; var hash = world.Level.ComputeStateHash();
			Click( hud.Stack, "ExitToLobby" ); Pending( flow );
			Assert.AreEqual( 0, retired ); Assert.AreSame( world.Level, flow.Level ); Assert.AreEqual( hash, world.Level.ComputeStateHash() ); Assert.IsFalse( ownedVoice.IsFinished || otherVoice.IsFinished ); StringAssert.Contains( ((UiLabel)hud.Stack.Top!.Find( "message" )!).Text(), "looping EVENT" ); CollectionAssert.AreEqual( auto, File.ReadAllBytes( Auto ) ); CollectionAssert.AreEqual( manual, File.ReadAllBytes( Manual ) );
			world.One.Runtime.Stop(); Click( hud.Stack, "choice0" ); Pending( flow );
			Assert.AreEqual( 1, retired ); Assert.IsTrue( ownedVoice.IsFinished ); Assert.IsFalse( otherVoice.IsFinished ); Assert.AreEqual( "fresh", ParkResumeSaveFile.Load( Auto ).DisplayName ); CollectionAssert.AreEqual( manual, File.ReadAllBytes( Manual ) ); Assert.AreEqual( 0, Directory.GetFiles( root, "*.tmp*" ).Length );
			foreign.Runtime.Stop();
		}
		finally { if ( audioReady ) audioFixture.Cleanup(); worldFixture.Cleanup(); }
	}
}
