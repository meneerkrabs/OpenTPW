using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.Online.Chat;
using OpenTPW.UI;
using OpenTPW.UI.Original;
using Veldrid;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-presence] A shared park's HUD with its co-builders as figures (C2d-4; docs/COOP.md, "Co-builders as figures"): the HUD comes first, the chat mode is F4's or
/// Enter's and builds nothing, and a goto moves the view to the co-builder's figure instead of asking the server.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SharedParkChatModeTests
{
	private static void Set( object o, string field, object? value ) => o.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( o, value );

	/// <summary>A shared park's players, as the HUD sees them: their figures and their chat are the test's.</summary>
	private sealed class SharedPresence : IVisitPresence, IChatPanelSource
	{
		public List<(float X, float Y)> Clicks { get; } = new();
		public List<string> WentTo { get; } = new();
		public List<VisitPresenceName> Figures { get; } = new();
		public void Click( float cellX, float cellY ) => Clicks.Add( (cellX, cellY) );
		public bool CanChat { get; set; } = true;
		public bool Walks => true;
		public bool Refused => false;
		public IEnumerable<VisitPresenceName> Names => Figures;
		public IChatPanelSource Chat => this;
		public void GoTo( string name ) => WentTo.Add( name );
		public bool SharedPark => true;
		public string? PlayerName => "Ann";
		public bool ChatOpen => true;
		public IReadOnlyList<string> RoomPlayers { get; } = new[] { "Ann", "Bob" };
		public IReadOnlyList<(string Name, bool Online)> Buddies { get; } = new[] { ("Eve", true) };
		public IReadOnlyCollection<string> Ignored { get; } = Array.Empty<string>();
		public bool Earmuffs => false;
		public bool CanVote => false;
		public IReadOnlyList<string> ChatLines { get; } = new[] { "Bob: shall I lay the path?" };
		public List<(ChatCommand Command, string Argument)> Commands { get; } = new();
		public void SendChatLine( string line ) { }
		public void SendCommand( ChatCommand command, string argument ) => Commands.Add( (command, argument) );
		public void AskBuddies() { }
	}

	/// <summary>A path tool's line writer that lays nothing: the tool only has to be armed.</summary>
	private sealed class IdleWriter : ICellLineWriter
	{
		public CellToolMode Mode => CellToolMode.Path;
		public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor ) => throw new NotSupportedException();
		public SegmentResult Commit( (int X, int Y) start, (int X, int Y) end ) => throw new NotSupportedException();
		public void Undo( SegmentResult segment ) { }
	}

	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 2048, 1536 ) };

	/// <summary>The HUD of a shared park (no visit) whose co-builders the presence shows.</summary>
	private static ParkHud SharedParkHud( SharedPresence presence, out Level level )
	{
		var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
		level = (Level)typeof( ParkHud ).GetField( "level", BindingFlags.NonPublic | BindingFlags.Instance )!.GetValue( hud )!;
		Set( hud, "host", new HudHost { Presence = () => presence } );
		return hud;
	}

	private (List<InputButton> Down, List<InputButton> Last, Input.KeyboardInfo Keyboard) keys;
	private CameraMode? camera;
	private (Vector3 Position, Vector3 Forward, Vector3 Front, Vector3 Up) listener;

	[TestInitialize]
	public void Initialize()
	{
		camera = Camera.Mode;
		listener = (AudioListener.Position, AudioListener.Forward, AudioListener.Front, AudioListener.Up);
		keys = (Input.KeysDown, Input.LastKeysDown, Input.Keyboard);
		Input.Keyboard = new();
		Input.KeysDown = new();
		Input.LastKeysDown = new();
		Input.UpdateButtons();
	}

	[TestCleanup]
	public void Cleanup()
	{
		Input.TextEntryActive = false;
		(Input.KeysDown, Input.LastKeysDown, Input.Keyboard) = keys;
		Input.UpdateButtons();
		if ( camera != null )
			Camera.Mode = camera;
		AudioListener.RestoreCaptured( listener.Position, listener.Forward );
		AudioListener.RestoreCapturedAxes( listener.Front, listener.Up );
	}

	/// <summary>One frame as the game flow runs it: the text-entry flag starts cleared and the focused field sets it again.</summary>
	private static void Frame( ParkHud hud, UiInput input )
	{
		Input.TextEntryActive = false;
		hud.Update( Context(), input, false, false, false );
	}

	private static void PressF4( ParkHud hud )
	{
		Input.Keyboard.KeysDown.Add( Key.F4 );
		Input.UpdateButtons();
		Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
		Input.Keyboard.KeysDown.Clear();
		Input.UpdateButtons();
		Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
	}

	[TestMethod]
	public void ASharedParkKeepsItsHudAndOffersTheChatModeOnF4AndEnter()
	{
		var presence = new SharedPresence();
		var hud = SharedParkHud( presence, out _ );
		Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
		Assert.IsFalse( hud.ChatMode, "a shared park starts in its HUD, where its host builds" );
		Assert.AreSame( hud.Screen, hud.Stack.Screens[0] );
		PressF4( hud );
		Assert.IsTrue( hud.ChatMode, "F4 opens the chat mode" );
		Assert.AreEqual( ParkHud.ChatModeScreenName, hud.Stack.Screens[0].Name );
		PressF4( hud );
		Assert.IsFalse( hud.ChatMode, "and back to the HUD" );
		// Enter over the park opens it with the entry taking the keys
		Frame( hud, UiInput.Key( UiKeys.Accept ) );
		Assert.IsTrue( hud.ChatMode && hud.ChatEntryFocused );
	}

	[TestMethod]
	public void TheChatModeRetiresTheArmedToolsAndKeepsItsClicksFromTheLevel()
	{
		var presence = new SharedPresence();
		var hud = SharedParkHud( presence, out var level );
		Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
		// a path tool, the remove tool and a placement are armed in the HUD
		level.CellTool.Enter( new IdleWriter(), null );
		level.IsRemovingObjects = true;
		level.IsPlacing = true;
		Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
		Assert.IsTrue( level.CellTool.IsActive && level.IsRemovingObjects && level.IsPlacing );
		Assert.IsFalse( level.WorldPointerRouted, "the HUD's park clicks are the level's" );
		PressF4( hud );
		Assert.IsTrue( hud.ChatMode );
		Assert.IsFalse( level.CellTool.IsActive, "the chat mode builds nothing" );
		Assert.IsFalse( level.IsRemovingObjects );
		Assert.IsFalse( level.IsPlacing );
		Assert.IsTrue( level.WorldPointerRouted, "the chat mode's park clicks never reach the level's tools" );
		// a click on an empty cell of the park starts no path, whose tool the HUD would start there
		HudToolClick.Click( hud, (5, 5) );
		Assert.IsFalse( level.CellTool.IsActive, "the chat mode starts no path" );
		Assert.AreEqual( 0, presence.Clicks.Count, "nor walks a figure" );
		PressF4( hud );
		Assert.IsFalse( hud.ChatMode );
		Assert.IsFalse( level.WorldPointerRouted );
	}

	[TestMethod]
	public void AGotoMovesTheViewToTheCoBuildersFigureAndAsksTheServerForNoOneElse()
	{
		var presence = new SharedPresence();
		presence.Figures.Add( new VisitPresenceName( "Ann", true, new N.Vector3( 1, 2, 0 ) ) );
		presence.Figures.Add( new VisitPresenceName( "Bob", false, new N.Vector3( 12, -7, 3 ) ) );
		var hud = SharedParkHud( presence, out var level );
		var view = new ParkCameraMode();
		Camera.Mode = view;
		Frame( hud, UiInput.Idle( new N.Vector2( -1, -1 ) ) );
		PressF4( hud );
		var panel = hud.VisitChatPanel!;
		Assert.IsTrue( panel.TakeTo!( "bob" ), "the co-builder's figure is drawn here" );
		Assert.AreEqual( (12f, -7f, 0f), (view.Target.X, view.Target.Y, view.Target.Z), "the view centred on it" );
		Assert.IsFalse( panel.TakeTo( "Eve" ), "a buddy elsewhere has no figure here: the goto goes to the server" );
		Assert.IsFalse( panel.TakeTo( "Ann" ), "the player's own figure is no goto's" );
		// from the free walk: the walk ends, and the overview looks at the figure
		var field = new ModelHeightfield( 10, 10, 10, 10, 0, 0, Array.Empty<uint>(), Enumerable.Repeat( 2.5f, 121 ).ToArray(), new uint[100] );
		var place = GuestRenderer.CellToEngine( field, 5f, 5f );
		Assert.IsTrue( FreeWalkCameraMode.TryGround( field, place.X, place.Y, out var ground ) );
		var walk = new FreeWalkCameraMode( field, ground, Vector3.Forward );
		level.UseFreeWalkForTests( walk );
		Camera.Mode = walk;
		Assert.IsTrue( panel.TakeTo( "Bob" ) );
		Assert.IsNull( level.FreeWalk, "the walk ended" );
		var overview = Camera.Mode as ParkCameraMode;
		Assert.IsNotNull( overview );
		Assert.AreEqual( (12f, -7f), (overview.Target.X, overview.Target.Y) );
		// with no park view (the globe over the park), nothing moves and the server is asked
		Camera.Mode = new CameraMode();
		Assert.IsFalse( panel.TakeTo( "Bob" ) );
		Assert.AreEqual( 0, presence.Commands.Count );
		Assert.AreEqual( 0, presence.WentTo.Count );
	}
}
