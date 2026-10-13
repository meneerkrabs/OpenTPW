using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class PostcardShortcutIngressTests
{
	private CameraMode previousCamera = null!;
	private GameEdition previousEdition;
	private static void Set( object target, string field, object value ) => target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	[TestInitialize]
	public void Initialize()
	{
		previousCamera = Camera.Mode;
		previousEdition = GameEditions.Current;
		GameEditions.Select( GameEdition.ThemeParkWorld );
		Camera.Mode = new ParkCameraMode();
		Input.Keyboard = new Input.KeyboardInfo();
		Input.KeysDown.Clear(); Input.LastKeysDown.Clear();
		Input.TextEntryActive = false;
	}

	[TestCleanup]
	public void Cleanup()
	{
		Camera.Mode = previousCamera;
		GameEditions.Select( previousEdition );
		Input.Keyboard = new Input.KeyboardInfo();
		Input.KeysDown.Clear(); Input.LastKeysDown.Clear(); Input.TextEntryActive = false;
	}

	private static UiContext Context()
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1000, 1000 ) };
	}

	[TestMethod]
	public void ActualInjectedChordOpensComposerOnceAndPlainPStillPauses()
	{
		var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
		var calls = 0;
		var host = new HudHost { OpenPostcard = stack => { calls++; stack.Push( new UiScreen( "sendPostcard" ) ); } };
		Set( hud, "host", host );
		var context = Context();
		using ( Input.InjectKeyDown( Key.ControlLeft, Key.P ) )
		{
			Input.UpdateButtons();
			Assert.IsTrue( Input.Pressed( InputButton.SendEmailPostcard ) );
			hud.Update( context, UiInput.Idle( new( -1, -1 ) ), false, false, false );
			Assert.AreEqual( 1, calls, "The registered Ctrl+P edge must reach the composer through the actual HUD frame." );
			Assert.AreEqual( "sendPostcard", hud.Stack.Top!.Name );
			Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed );
			Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.ControlLeft, Key.P } );
			Input.UpdateButtons();
			Assert.IsFalse( Input.Pressed( InputButton.SendEmailPostcard ), "A held chord is not a second press." );
			hud.Update( context, UiInput.Idle( new( -1, -1 ) ), false, false, false );
			Assert.AreEqual( 1, calls );
		}
		Input.Keyboard = new Input.KeyboardInfo();
		Input.UpdateButtons();
		hud.Stack.Pop();
		Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.P } );
		hud.Update( context, new UiInputSource().Poll( 1000, 1000 ), false, false, false );
		Assert.AreEqual( GameSpeed.Paused, hud.Status.Speed );
		Assert.AreEqual( 1, calls );
	}

	[DataTestMethod]
	[DataRow( "success" )]
	[DataRow( "readonly" )]
	[DataRow( "overlay" )]
	[DataRow( "pauseMenu" )]
	[DataRow( "mouse" )]
	[DataRow( "keyboard" )]
	[DataRow( "text" )]
	[DataRow( "textEntry" )]
	[DataRow( "typedText" )]
	[DataRow( "hud" )]
	[DataRow( "building" )]
	[DataRow( "armed" )]
	[DataRow( "removing" )]
	[DataRow( "cell" )]
	[DataRow( "coaster" )]
	[DataRow( "ride" )]
	[DataRow( "rideExit" )]
	[DataRow( "camera" )]
	[DataRow( "tpi" )]
	[DataRow( "noHost" )]
	public void FullHudChordRespectsInputToolsAndCapabilities( string gate )
	{
		var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
		var level = (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
		var calls = 0;
		Set( hud, "host", new HudHost { OpenPostcard = gate == "noHost" ? null : _ => calls++ } );
		if ( gate == "readonly" ) Set( level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		if ( gate is "overlay" or "pauseMenu" ) hud.Stack.Push( new UiScreen( gate == "pauseMenu" ? "pause" : "modal" ) );
		if ( gate == "camera" ) Camera.Mode = new CameraMode();
		if ( gate == "tpi" ) GameEditions.Select( GameEdition.ThemeParkInc );
		Input.TextEntryActive = gate == "textEntry";
		level.IsPlacing = gate == "building"; level.IsRemovingObjects = gate == "removing";
		if ( gate == "armed" ) level.BuildEntry = (ObjectCatalogEntry)RuntimeHelpers.GetUninitializedObject( typeof( ObjectCatalogEntry ) );
		if ( gate == "cell" ) Set( level.CellTool, "<Mode>k__BackingField", CellToolMode.Path );
		if ( gate == "coaster" )
		{
			var station = (OriginalObject)RuntimeHelpers.GetUninitializedObject( typeof( OriginalObject ) );
			Set( station, "<Runtime>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( OriginalObjectRuntime ) ) );
			Set( level, "<CoasterToolStation>k__BackingField", station );
			Set( level, "coasterBuilders", new Dictionary<OriginalObject, CoasterBuilder>() );
		}
		if ( gate is "ride" or "rideExit" ) Set( level, "<RideView>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( RideCameraMode ) ) );
		if ( gate == "hud" ) hud.Screen.Add( new UiButton { Bounds = new UiRect( 0, 0, 100, 100 ), Anchor = UiAnchor.TopLeft } );
		using ( Input.InjectKeyDown( Key.ControlLeft, Key.P ) ) Input.UpdateButtons();
		if ( gate == "rideExit" ) Input.KeysDown.Add( InputButton.RideView );
		var input = gate == "typedText" ? UiInput.Type( "p" ) : UiInput.Idle( gate == "hud" ? new( 20, 20 ) : new( -1, -1 ) );
		hud.Update( Context(), input, gate == "mouse", gate == "keyboard", gate == "text" );
		Assert.AreEqual( gate is "success" or "readonly" ? 1 : 0, calls );
		Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed, "The postcard chord never pauses the simulation." );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ExistingGameFlowComposerOpensAndQueuesLocallyWithoutSendingOrChangingThePark( bool readOnly )
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-postcard-ingress-" ).FullName;
		var previousFiles = OpenTPW.Common.GlobalNamespace.FileSystem;
		try
		{
			OpenTPW.Common.GlobalNamespace.FileSystem = new BaseFileSystem( root );
			var folders = new OnlineFolders( root );
			folders.SaveSettings( new OnlineSettings( null, "sender" ) );
			using var session = new OnlineSession( folders );
			var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
			var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ );
			var level = (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
			Set( level, "<LevelName>k__BackingField", "jungle" );
			if ( readOnly ) Set( level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
			Set( flow, "<Level>k__BackingField", level );
			Set( flow, "<Strings>k__BackingField", OriginalUiTests.FakeStrings() );
			Set( flow, "<Context>k__BackingField", Context() );
			Set( flow, "onlineSession", session );
			Set( hud, "host", new HudHost { OpenPostcard = flow.ShowPostcard } );
			var before = simulation.ComputeStateHash();
			using ( Input.InjectKeyDown( Key.ControlLeft, Key.P ) ) Input.UpdateButtons();
			hud.Update( Context(), UiInput.Idle( new( -1, -1 ) ), false, false, false );
			var composer = hud.Stack.Top!;
			Assert.AreEqual( "sendPostcard", composer.Name );
			Assert.IsNull( session.Client );
			Assert.AreEqual( 0, session.Busy );
			Assert.AreEqual( 0, session.Folders.List( session.Folders.Outbox ).Count );
			((UiTextField)composer.Find( "to" )!).Text = "friend";
			composer.Elements.OfType<UiTextField>().Single( field => field.Id == "title" ).Text = "A park visit";
			((UiTextField)composer.Find( "message" )!).Text = "Synthetic postcard";
			((UiButton)composer.Find( "outbox" )!).Clicked!();
			var cards = session.Folders.List( session.Folders.Outbox );
			Assert.AreEqual( 1, cards.Count );
			Assert.IsNull( cards[0].Error );
			Assert.AreEqual( "Synthetic postcard", cards[0].Card!.Manifest.Text );
			Assert.IsNull( session.Client, "The shortcut and local outbox do not initialize a network client." );
			Assert.AreEqual( before, simulation.ComputeStateHash() );
			((UiButton)composer.Find( "back" )!).Clicked!();
			Assert.AreEqual( "hud", hud.Stack.Top!.Name );
			GameEditions.Select( GameEdition.ThemeParkInc );
			Assert.ThrowsException<NotSupportedException>( () => flow.ShowPostcard( hud.Stack ) );
			Assert.AreEqual( "hud", hud.Stack.Top!.Name );
		}
		finally { OpenTPW.Common.GlobalNamespace.FileSystem = previousFiles; Directory.Delete( root, recursive: true ); }
	}

	[TestMethod]
	public void OpeningComposerConsumesASimultaneousParkClickAndItsRelease()
	{
		var view = Camera.ViewMatrix; var projection = Camera.ProjMatrix;
		try
		{
			Camera.ViewMatrix = Camera.ProjMatrix = System.Numerics.Matrix4x4.Identity;
			var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
			var level = (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
			Set( hud, "host", new HudHost { OpenPostcard = stack => stack.Push( new UiScreen( "sendPostcard" ) ) } );
			using ( Input.InjectKeyDown( Key.ControlLeft, Key.P ) ) Input.UpdateButtons();
			var press = new UiInput( new( 950, 950 ), true, true, false, false, UiKeys.None );
			Assert.IsTrue( hud.Update( Context(), press, false, false, false ) );
			Assert.IsFalse( level.CellTool.IsActive, "A composer opened by the chord owns the same frame's park click." );
			hud.Stack.Pop();
			Input.UpdateButtons();
			hud.Update( Context(), UiInput.Click( press.Mouse ), false, false, false );
			Assert.IsFalse( level.CellTool.IsActive );
			Assert.IsNull( hud.SelectedObject );
		}
		finally { Camera.ViewMatrix = view; Camera.ProjMatrix = projection; }
	}
}
