using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.Online.Chat;
using OpenTPW.UI;
using OpenTPW.UI.Original;
using Veldrid;
using N = System.Numerics;

namespace OpenTPW.Tests;

[TestClass]
public class VisitChatModeTests
{
	private static void Set( object o, string field, object? value ) => o.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( o, value );

	/// <summary>A visit's other players, as the HUD sees them: their chat is the test's.</summary>
	private sealed class FakePresence : IVisitPresence, IChatPanelSource
	{
		public List<(float X, float Y)> Clicks { get; } = new();
		public List<string> Said { get; } = new();
		public void Click( float cellX, float cellY ) => Clicks.Add( (cellX, cellY) );
		public bool CanChat { get; set; } = true;
		public bool Walks => true;
		public bool Refused { get; set; }
		public IEnumerable<VisitPresenceName> Names => Array.Empty<VisitPresenceName>();
		public IChatPanelSource Chat => this;
		public void GoTo( string name ) { }
		public string? PlayerName => "Ann";
		public bool ChatOpen => true;
		public IReadOnlyList<string> RoomPlayers { get; } = new[] { "Ann", "Bob" };
		public IReadOnlyList<(string Name, bool Online)> Buddies { get; } = Array.Empty<(string, bool)>();
		public IReadOnlyCollection<string> Ignored { get; } = Array.Empty<string>();
		public bool Earmuffs => false;
		public bool CanVote => true;
		public IReadOnlyList<string> ChatLines { get; } = new[] { "Bob: hello" };
		public void SendChatLine( string line ) => Said.Add( line );
		public void SendCommand( ChatCommand command, string argument ) { }
		public void AskBuddies() { }
	}

	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 2048, 1536 ) };

	/// <summary>The HUD of a read-only visit with other players, which it enters in the chat mode.</summary>
	private static ParkHud Visit( FakePresence presence, out Level level )
	{
		var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
		level = (Level)typeof( ParkHud ).GetField( "level", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( hud )!;
		Set( level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		Set( hud, "host", new HudHost { Presence = () => presence } );
		// the fixture skips the constructor, so the field's initial value is set here
		Set( hud, "chatModeWanted", true );
		return hud;
	}

	[TestCleanup]
	public void Cleanup() => Input.TextEntryActive = false;

	/// <summary>One frame as the game flow runs it: the text-entry flag starts cleared and the focused field sets it again.</summary>
	private static void Frame( ParkHud hud, UiInput input )
	{
		Input.TextEntryActive = false;
		hud.Update( Context(), input, false, false, false );
	}

	[TestMethod]
	public void AVisitWithOtherPlayersShowsTheChatOverlayInPlaceOfTheHud()
	{
		var down = Input.KeysDown; var last = Input.LastKeysDown; var keyboard = Input.Keyboard;
		try
		{
			Input.Keyboard = new(); Input.KeysDown = new(); Input.LastKeysDown = new(); Input.UpdateButtons();
			var presence = new FakePresence();
			var hud = Visit( presence, out _ );
			Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
			Assert.IsTrue( hud.ChatMode, "a visit with other players starts in the chat mode" );
			Assert.AreEqual( 1, hud.Stack.Screens.Count );
			Assert.AreEqual( ParkHud.ChatModeScreenName, hud.Stack.Screens[0].Name );
			Assert.IsFalse( hud.Paused, "the chat mode does not pause the park" );
			Assert.IsNotNull( hud.Stack.Screens[0].Find( "chatPeople" ) );
			// F4 switches to the HUD and back; Alt+F4 is the window's
			Input.Keyboard.KeysDown.Add( Key.F4 ); Input.UpdateButtons();
			Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
			Assert.IsFalse( hud.ChatMode );
			Assert.AreSame( hud.Screen, hud.Stack.Screens[0] );
			Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
			Assert.IsFalse( hud.ChatMode, "a held key switches once" );
			Input.Keyboard.KeysDown.Clear(); Input.UpdateButtons();
			Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
			Input.Keyboard.KeysDown.AddRange( new[] { Key.AltLeft, Key.F4 } ); Input.UpdateButtons();
			Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
			Assert.IsFalse( hud.ChatMode, "Alt+F4 is not the chat key" );
			Input.Keyboard.KeysDown.Clear(); Input.UpdateButtons();
			Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
			// Enter over the park returns to the chat mode with the entry taking the keys
			Frame( hud, UiInput.Key( UiKeys.Accept ) );
			Assert.IsTrue( hud.ChatMode && hud.ChatEntryFocused, "Enter over the park opens the chat entry" );
			// the entry keeps the keys, not the pointer: over the park the HUD leaves it to the camera (the game flow's UiCapturesMouse)
			Input.TextEntryActive = false;
			Assert.IsFalse( hud.Update( Context(), UiInput.Idle( new N.Vector2( 1500, 400 ) ), false, false, false ), "the park keeps the pointer while the entry has the keys" );
			Assert.IsTrue( hud.ChatEntryFocused && Input.TextEntryActive );
			// the typed line is said, the entry keeps the keys; Tab leaves it
			Frame( hud, UiInput.Type( "hello park" ) );
			Frame( hud, UiInput.Key( UiKeys.Accept ) );
			CollectionAssert.AreEqual( new[] { "hello park" }, presence.Said );
			Assert.IsTrue( hud.ChatEntryFocused );
			Assert.AreEqual( "", hud.VisitChatPanel!.Entry.Text );
			Frame( hud, UiInput.Key( UiKeys.Tab ) );
			Assert.IsFalse( hud.ChatEntryFocused, "Tab leaves the entry" );
			Input.TextEntryActive = false;
			// Escape opens the pause menu over the chat mode
			Frame( hud, UiInput.Key( UiKeys.Back ) );
			Assert.AreEqual( ParkHud.PauseMenuScreen, hud.Stack.Top?.Name );
			Assert.IsTrue( hud.Paused );
			Assert.AreEqual( ParkHud.ChatModeScreenName, hud.Stack.Screens[0].Name, "the chat mode stays under the menu" );
		}
		finally { Input.TextEntryActive = false; Input.Keyboard = keyboard; Input.KeysDown = down; Input.LastKeysDown = last; Input.UpdateButtons(); }
	}

	[TestMethod]
	public void TheChatModeWaitsForTheParksRoomAndLeavesWhenTheParkIsRefused()
	{
		var presence = new FakePresence { CanChat = false };
		var hud = Visit( presence, out _ );
		Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
		Assert.IsFalse( hud.ChatMode, "no dead overlay before the visit is in its park's room" );
		presence.CanChat = true;
		Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
		Assert.IsTrue( hud.ChatMode );
		// a dropped connection keeps the overlay (its controls wait for the reconnection)
		presence.CanChat = false;
		Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
		Assert.IsTrue( hud.ChatMode );
		// an unpublished park's room is refused: the visit returns to its HUD
		presence.Refused = true;
		Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
		Assert.IsFalse( hud.ChatMode );
		Assert.AreSame( hud.Screen, hud.Stack.Screens[0] );
	}

	[TestMethod]
	public void APlainVisitKeepsItsHud()
	{
		var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
		var level = (Level)typeof( ParkHud ).GetField( "level", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( hud )!;
		Set( level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		Set( hud, "host", new HudHost() );
		Set( hud, "chatModeWanted", true );
		Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
		Assert.IsFalse( hud.ChatMode, "a visit without other players has no chat mode" );
		Assert.AreSame( hud.Screen, hud.Stack.Screens[0] );
	}
}
