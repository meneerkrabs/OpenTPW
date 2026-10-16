using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;
using N = System.Numerics;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class PostcardShortcutReviewTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static void Set( object target, string name, object value ) => target.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );
	private static UiContext Context()
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1000, 1000 ) };
	}

	[DataTestMethod]
	[DataRow( "overlay" )]
	[DataRow( "mouse" )]
	[DataRow( "keyboard" )]
	[DataRow( "text" )]
	public void ABlockedPhysicalChordWaitsForReleaseBeforeItCanOpenTheComposer( string gate )
	{
		var previousCamera = Camera.Mode;
		var previousEdition = GameEditions.Current;
		var previousKeyboard = Input.Keyboard;
		var previousDown = Input.KeysDown;
		var previousLast = Input.LastKeysDown;
		var previousText = Input.TextEntryActive;
		try
		{
			GameEditions.Select( GameEdition.ThemeParkWorld );
			Camera.Mode = new ParkCameraMode();
			Input.KeysDown = new(); Input.LastKeysDown = new(); Input.TextEntryActive = false;
			Input.Keyboard = new Input.KeyboardInfo( new List<Key> { Key.ControlLeft, Key.P } );
			var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
			var calls = 0;
			Set( hud, "host", new HudHost { OpenPostcard = stack => { calls++; stack.Push( new UiScreen( "sendPostcard" ) ); } } );
			if ( gate == "overlay" ) hud.Stack.Push( new UiScreen( "modal" ) );
			Input.UpdateButtons();
			hud.Update( Context(), UiInput.Idle( new( -1, -1 ) ), gate == "mouse", gate == "keyboard", gate == "text" );
			Assert.AreEqual( 0, calls );
			if ( gate == "overlay" ) hud.Stack.Pop();
			Input.UpdateButtons();
			hud.Update( Context(), UiInput.Idle( new( -1, -1 ) ), false, false, false );
			Assert.AreEqual( 0, calls, "Removing a capture owner cannot replay its held postcard press." );
			Input.Keyboard = new Input.KeyboardInfo();
			Input.UpdateButtons();
			hud.Update( Context(), UiInput.Idle( new( -1, -1 ) ), false, false, false );
			using ( Input.InjectKeyDown( Key.ControlLeft, Key.P ) ) Input.UpdateButtons();
			hud.Update( Context(), UiInput.Idle( new( -1, -1 ) ), false, false, false );
			Assert.AreEqual( 1, calls );
			Assert.AreEqual( "sendPostcard", hud.Stack.Top!.Name );
			Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed );
		}
		finally
		{
			Camera.Mode = previousCamera; GameEditions.Select( previousEdition );
			Input.Keyboard = previousKeyboard; Input.KeysDown = previousDown; Input.LastKeysDown = previousLast; Input.TextEntryActive = previousText;
		}
	}

	[TestMethod]
	public void APostcardPressOverAPickableVisitorOwnsThatClickAndItsRelease()
	{
		var previousCamera = Camera.Mode;
		var previousView = Camera.ViewMatrix; var previousProjection = Camera.ProjMatrix;
		var previousEdition = GameEditions.Current;
		var previousKeyboard = Input.Keyboard;
		var previousDown = Input.KeysDown; var previousLast = Input.LastKeysDown;
		var previousText = Input.TextEntryActive;
		try
		{
			GameEditions.Select( GameEdition.ThemeParkWorld );
			Camera.Mode = new ParkCameraMode(); Camera.ViewMatrix = Camera.ProjMatrix = N.Matrix4x4.Identity;
			Input.Keyboard = new Input.KeyboardInfo(); Input.KeysDown = new(); Input.LastKeysDown = new(); Input.TextEntryActive = false;
			var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out var renderer );
			var guest = new Guest( 8, 0 ) { State = GuestState.WalkingAround, DrawX = 5, DrawY = 5 };
			Set( simulation, "guests", new List<Guest> { guest } );
			Set( hud, "host", new HudHost { OpenPostcard = stack => stack.Push( new UiScreen( "sendPostcard" ) ) } );
			var mouse = new N.Vector2( 500, 400 );
			Assert.AreSame( guest, renderer.PickVisitor( mouse, new N.Vector2( 1000 ), Camera.ViewMatrix, Camera.ProjMatrix ) );
			using ( Input.InjectKeyDown( Key.ControlLeft, Key.P ) ) Input.UpdateButtons();
			var press = new UiInput( mouse, true, true, false, false, UiKeys.None );
			hud.Update( Context(), press, false, false, false );
			Assert.AreEqual( "sendPostcard", hud.Stack.Top!.Name );
			Assert.IsTrue( hud.Paused, "The composer retains the existing modal pause without toggling Status.Speed." );
			Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed );
			Assert.IsFalse( hud.VisitorWindowOpen );
			hud.Stack.Pop();
			Assert.IsFalse( hud.Paused );
			Input.UpdateButtons();
			hud.Update( Context(), UiInput.Click( mouse ), false, false, false );
			Assert.IsFalse( hud.VisitorWindowOpen );
			hud.Update( Context(), press, false, false, false );
			Assert.IsTrue( hud.VisitorWindowOpen, "A new ordinary visitor click must work after the consumed release." );
			Assert.AreEqual( guest.Id, hud.SelectedVisitorId );
		}
		finally
		{
			Camera.Mode = previousCamera; Camera.ViewMatrix = previousView; Camera.ProjMatrix = previousProjection;
			GameEditions.Select( previousEdition ); Input.Keyboard = previousKeyboard;
			Input.KeysDown = previousDown; Input.LastKeysDown = previousLast; Input.TextEntryActive = previousText;
		}
	}
}
