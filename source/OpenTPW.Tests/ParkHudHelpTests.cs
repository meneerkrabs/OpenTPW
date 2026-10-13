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
public class ParkHudHelpTests
{
	private Input.KeyboardInfo previousKeyboard;
	private List<InputButton> previousDown = null!;
	private List<InputButton> previousLast = null!;
	private bool previousTextEntry;

	[TestInitialize]
	public void Initialize()
	{
		previousKeyboard = Input.Keyboard;
		previousDown = Input.KeysDown;
		previousLast = Input.LastKeysDown;
		previousTextEntry = Input.TextEntryActive;
		Input.Keyboard = new Input.KeyboardInfo();
		Input.KeysDown = new();
		Input.LastKeysDown = new();
		Input.TextEntryActive = false;
	}

	[TestCleanup]
	public void Cleanup()
	{
		Input.Keyboard = previousKeyboard;
		Input.KeysDown = previousDown;
		Input.LastKeysDown = previousLast;
		Input.TextEntryActive = previousTextEntry;
	}

	[TestMethod]
	public void ControlHDoesNotActivateTheStaffWindow()
	{
		var hud = CreateHud();
		var staff = new UiScreen( "staff" ) { Modal = false };
		SetField( hud, typeof( ParkHud ), "staffScreen", staff );
		hud.Stack.Push( staff );
		using var tap = Input.InjectKeyDown( Key.ControlLeft, Key.H );
		Input.UpdateButtons();
		Assert.IsTrue( Input.Pressed( InputButton.ToggleHelpBar ) );
		Assert.IsTrue( Input.Pressed( InputButton.HireStaff ), "the shared physical H reaches both button bindings" );
		Handler( hud )( false );
		Assert.IsTrue( hud.StaffWindowOpen, "Ctrl+H cannot close the unrelated staff window" );
		Input.UpdateButtons();
		Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.H } );
		Input.UpdateButtons();
		Handler( hud )( false );
		Assert.IsFalse( hud.StaffWindowOpen, "plain H still closes its own window" );
	}

	[DataTestMethod]
	[DataRow( Key.ControlLeft )]
	[DataRow( Key.ControlRight )]
	public void EitherControlKeyTogglesHelpOncePerPhysicalChord( Key control )
	{
		var hud = CreateHud();
		var handle = Handler( hud );
		Input.Keyboard = new Input.KeyboardInfo( new List<Key> { control, Key.H } );
		Input.UpdateButtons();
		handle( true );
		Assert.IsFalse( hud.HelpBarVisible );
		handle( true );
		Assert.IsFalse( hud.HelpBarVisible, "a held chord is not a second tap" );
		Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.H } );
		Input.UpdateButtons();
		handle( true );
		Assert.IsFalse( hud.StaffWindowOpen, "releasing Ctrl with H still held does not open staff" );
		Input.Keyboard.KeysDown.Add( control );
		Input.UpdateButtons();
		handle( true );
		Assert.IsTrue( hud.HelpBarVisible, "pressing Ctrl again makes a new chord" );
	}

	[DataTestMethod]
	[DataRow( "typing" )]
	[DataRow( "capture" )]
	[DataRow( "pause" )]
	[DataRow( "dialog" )]
	[DataRow( "window" )]
	[DataRow( "ride" )]
	public void InputOwnersBlockHelpAndAChordHeldThroughTheirRelease( string owner )
	{
		var hud = CreateHud();
		var level = (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!;
		var paused = owner == "pause";
		var overlay = owner is "dialog" or "window";
		Input.TextEntryActive = owner == "typing";
		if ( owner is "pause" or "dialog" or "window" )
			hud.Stack.Push( new UiScreen( owner == "window" ? "buy" : owner ) );
		if ( owner == "ride" )
			SetField( level, typeof( Level ), "<RideView>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( RideCameraMode ) ) );
		Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.ControlLeft, Key.H } );
		Input.UpdateButtons();
		var handle = Handler( hud );
		handle( ParkHud.ParkShortcutsAllowed( paused, overlay, Input.TextEntryActive, owner == "capture" ) );
		Assert.IsTrue( hud.HelpBarVisible, owner );
		Input.TextEntryActive = false;
		SetField( level, typeof( Level ), "<RideView>k__BackingField", null! );
		while ( hud.Stack.Screens.Count > 1 )
			hud.Stack.Pop();
		Input.UpdateButtons();
		handle( true );
		Assert.IsTrue( hud.HelpBarVisible, "leaving an input owner while holding Ctrl+H does not toggle" );
		Input.Keyboard = new Input.KeyboardInfo();
		Input.UpdateButtons();
		handle( true );
		using var tap = Input.InjectKeyDown( Key.ControlLeft, Key.H );
		Input.UpdateButtons();
		handle( true );
		Assert.IsFalse( hud.HelpBarVisible, "a fresh tap resumes normal help control" );
	}

	[TestMethod]
	public void ToggleControlsTheRealHoverHelpDrawWithoutChangingTheSharedOption()
	{
		var hud = CreateHud();
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ),
			new UiModels( name => throw new FileNotFoundException( name ) ) ) { ResolveTexture = _ => null };
		var button = hud.Screen.Add( new UiButton { Help = "AA", Bounds = new UiRect( 100, 100, 100, 100 ) } );
		hud.Stack.Update( context, UiInput.Idle( button.ScreenRect( context.Canvas ).Center ) );
		Assert.AreEqual( "AA", context.HoverHelp );
		hud.Stack.Draw( context, hud.HelpBarVisible );
		Assert.IsTrue( context.Batch.Texts.Any( text => text.Text == "AA" ) );
		using var tap = Input.InjectKeyDown( Key.ControlLeft, Key.H );
		Input.UpdateButtons();
		Handler( hud )( true );
		context.Batch.Clear();
		hud.Stack.Draw( context, hud.HelpBarVisible );
		Assert.AreEqual( 0, context.Batch.Texts.Count, "hidden help emits no text draw" );
		Assert.IsTrue( context.PopupHelp, "the owner gate never overwrites the global popup-help option" );
		Assert.AreSame( font, context.Fonts.Small, "the existing borrowed font remains usable" );
		context.PopupHelp = false;
		hud.Stack.Draw( context, showHelp: true );
		Assert.AreEqual( 0, context.Batch.Texts.Count, "the global option also suppresses help" );
		context.PopupHelp = true;
		hud.Stack.Draw( context );
		Assert.IsTrue( context.Batch.Texts.Any( text => text.Text == "AA" ), "another screen owner keeps its normal help route" );
	}

	[TestMethod]
	public void HeldHelpChordDispatchAllocatesNothing()
	{
		var hud = CreateHud();
		var handle = Handler( hud );
		Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.ControlLeft, Key.H } );
		Input.UpdateButtons();
		handle( true );
		for ( var index = 0; index < 100; index++ )
			handle( true );
		var allocated = GC.GetAllocatedBytesForCurrentThread();
		for ( var index = 0; index < 1000; index++ )
			handle( true );
		Assert.AreEqual( allocated, GC.GetAllocatedBytesForCurrentThread() );
	}

	private static ParkHud CreateHud()
	{
		var hud = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		SetField( hud, typeof( ParkHud ), "level", RuntimeHelpers.GetUninitializedObject( typeof( Level ) ) );
		SetField( hud, typeof( ParkHud ), "<Stack>k__BackingField", new UiScreenStack() );
		var screen = new UiScreen( "hud" ) { Modal = false };
		SetField( hud, typeof( ParkHud ), "hud", screen );
		hud.Stack.Push( screen );
		SetField( hud, typeof( ParkHud ), "<HelpBarVisible>k__BackingField", true );
		return hud;
	}

	private static Action<bool> Handler( ParkHud hud ) => (Action<bool>)typeof( ParkHud )
		.GetMethod( "HandleScreenShortcuts", BindingFlags.Instance | BindingFlags.NonPublic )!.CreateDelegate( typeof( Action<bool> ), hud );

	private static void SetField( object target, Type type, string name, object value ) => type
		.GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );
}
