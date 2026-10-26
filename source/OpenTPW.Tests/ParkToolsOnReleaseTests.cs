using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// The park view's tools act on the left button's RELEASE, as tp.exe's do: the mode object's press slot (+4, 0x524790) only sets a latch, its release slot (+8, 0x524960) is the tool's click, at the
/// cell under the pointer then; a release goes to the window that took its press; the right button's quick click (200 ms, 8 px) cancels on its release. Every case runs the real HUD path with
/// ordered pointer events: no SDL pump, renderer, game data or executable is used.
/// </summary>
[TestClass, DoNotParallelize]
public class ParkToolsOnReleaseTests
{
	private static readonly N.Vector2 PointA = new( 500, 400 );
	private N.Vector2 pointB;
	private N.Vector2 PointB => pointB;
	private ParkHud hud = null!;
	private Level level = null!;
	private UiContext context = null!;
	private UiInputSource source = null!;
	private Action restore = () => { };
	private readonly List<ParkCommand> commands = new();
	private readonly List<uint> sounds = new();
	private long sequence;
	private long milliseconds = 1000;

	private sealed class EmptyCatalog : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => Array.Empty<BuildItem>();
	}

	/// <summary>A kart line writer whose ghost refuses one tile and accepts the rest.</summary>
	private sealed class RefusingTrackWriter( (int X, int Y)? refused ) : ICellLineWriter
	{
		public CellToolMode Mode => CellToolMode.KartTrack;
		public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor )
		{
			var bad = refused is { } cell && (start == cell || cursor == cell);
			return new( cursor, Array.Empty<(int X, int Y)>(), 0, bad ? CellBuildResult.Occupied : CellBuildResult.Ok, false )
				{ Start = start, Line = ParkPathBuilder.LayLine( start, cursor ), StoppedAt = bad ? refused : null };
		}
		public SegmentResult Commit( (int X, int Y) start, (int X, int Y) end ) => Preview( start, end );
		public void Undo( SegmentResult segment ) { }
	}

	[TestInitialize]
	public void Initialize()
	{
		commands.Clear(); sounds.Clear(); sequence = 0; milliseconds = 1000;
		var oldMouse = Input.Mouse;
		var oldKeyboard = Input.Keyboard;
		var oldDown = Input.KeysDown;
		var oldLast = Input.LastKeysDown;
		var oldText = Input.TextEntryActive;
		var oldCursor = Input.CursorType;
		var oldSoftwareCursor = Input.SoftwareCursorRequested;
		var restoreFrame = Input.CaptureFrameStateForTests();
		var oldMode = Camera.Mode;
		var oldView = Camera.ViewMatrix;
		var oldProjection = Camera.ProjMatrix;
		var oldEdition = GameEditions.Current;
		var oldOptions = GameOptions.Current;
		var oldRidePointer = RideCameraMode.PointerOverride;
		var oldLog = Log;
		restore = () =>
		{
			Input.Mouse = oldMouse;
			Input.Keyboard = oldKeyboard;
			Input.KeysDown = oldDown;
			Input.LastKeysDown = oldLast;
			Input.TextEntryActive = oldText;
			Input.CursorType = oldCursor;
			Input.SoftwareCursorRequested = oldSoftwareCursor;
			restoreFrame();
			Camera.Mode = oldMode; Camera.ViewMatrix = oldView; Camera.ProjMatrix = oldProjection;
			GameEditions.Select( oldEdition );
			GameOptions.Current = oldOptions; RideCameraMode.PointerOverride = oldRidePointer;
			GameAudio.UiSoundObserver = null;
			Log = oldLog;
		};
		Input.Mouse = new(); Input.Keyboard = new(); Input.KeysDown = new(); Input.LastKeysDown = new();
		Input.TextEntryActive = false;
		Input.ResetFrameStateForTests();
		GameEditions.Select( GameEdition.ThemeParkWorld );
		GameOptions.Current = new GameOptions();
		Camera.Mode = new ParkCameraMode();
		// a camera south of the park's middle and above it, looking down at it: neighbouring screen points lie on different cells
		var centre = new N.Vector3( ParkPlacement.MinimumCoordinate + 16.5f * ObjectPlacement.CellSize, ParkPlacement.MinimumCoordinate + 16.5f * ObjectPlacement.CellSize, 0 );
		Camera.ViewMatrix = N.Matrix4x4.CreateLookAt( centre + new N.Vector3( 0, -20, 30 ), centre + new N.Vector3( 0, 0, 12 ), N.Vector3.UnitZ );
		Camera.ProjMatrix = N.Matrix4x4.CreatePerspectiveFieldOfView( 0.8f, 1, 0.5f, 500 );
		var fixture = VisitorWorldSelectionTests.Fixture( out _, out _ );
		level = fixture.LevelForTests;
		level.UseCommandsForTests( new ParkCommands( () => 0, command => { commands.Add( command ); return ParkCommandResult.Ok(); }, () => false ) );
		hud = new ParkHud( level, OriginalUiTests.FakeStrings(), new NoEconomyStatus(), new EmptyCatalog(), new HudHost() );
		foreach ( var element in hud.Screen.Elements.ToArray() ) hud.Screen.Remove( element );
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( 1000, 1000 ), PopupHelp = false, ResolveTexture = _ => null };
		source = new UiInputSource { Clock = () => milliseconds };
		GameAudio.UiSoundObserver = sounds.Add;
		Assert.IsTrue( level.TryGetGridCell( PointA, new N.Vector2( 1000 ), out var ax, out var ay ), "The camera must hit the sandbox." );
		// the second point is the first one found on another cell, to the right of the first
		pointB = default;
		for ( var x = (int)PointA.X + 20; x <= 990 && pointB == default; x += 10 )
			if ( level.TryGetGridCell( new N.Vector2( x, PointA.Y ), new N.Vector2( 1000 ), out var bx, out var by ) && (bx, by) != (ax, ay) )
				pointB = new N.Vector2( x, PointA.Y );
		Assert.AreNotEqual( default, pointB, "a second cell can be found" );
	}

	[TestCleanup]
	public void Cleanup() => restore();

	private OrderedInputEvent Event( OrderedInputEventKind kind, N.Vector2 point, MouseButton button = MouseButton.Left, long advance = 1 )
	{
		milliseconds += advance;
		return new OrderedInputEvent( ++sequence, kind, point.X, point.Y,
			kind is OrderedInputEventKind.ButtonDown or OrderedInputEventKind.ButtonUp ? (int)button : -1,
			0, TimestampMilliseconds: milliseconds, PositionIsExact: true );
	}

	private OrderedInputEvent Down( N.Vector2 point, MouseButton button = MouseButton.Left, long advance = 1 ) => Event( OrderedInputEventKind.ButtonDown, point, button, advance );
	private OrderedInputEvent Up( N.Vector2 point, MouseButton button = MouseButton.Left, long advance = 1 ) => Event( OrderedInputEventKind.ButtonUp, point, button, advance );
	private OrderedInputEvent Move( N.Vector2 point, long advance = 1 ) => Event( OrderedInputEventKind.Motion, point, advance: advance );

	/// <summary>One frame of the real path: the input source polls the batch, the HUD updates and the level takes its own world input.</summary>
	private void Frame( IReadOnlyList<OrderedInputEvent> events, N.Vector2? mouse = null, bool left = false, bool right = false )
	{
		var position = mouse ?? PointA;
		Input.Mouse = new Input.MouseInfo { Position = new( position.X, position.Y ), Left = left, Right = right };
		Input.Keyboard = new Input.KeyboardInfo( new List<Key>() );
		Input.SetFrameForTests( Input.Keyboard.KeysDown, events );
		var input = source.Poll( 1, 1 );
		var captured = hud.Update( context, input, false, false, false );
		level.UpdateWorldInput( Input.Mouse.Left, captured, input.Mouse, new N.Vector2( context.Canvas.Width, context.Canvas.Height ), Input.ModifierMaskDown );
	}

	private (int X, int Y) CellAt( N.Vector2 point )
	{
		Assert.IsTrue( level.TryGetGridCell( point, new N.Vector2( context.Canvas.Width, context.Canvas.Height ), out var x, out var y ), $"the synthetic camera hits the park at {point}" );
		return (x, y);
	}

	/// <summary>A button on the interface whose centre lies over a cell of the park (not the first point's), so a release there has a cell to act on.</summary>
	private N.Vector2 AddButtonOverPark( Action clicked )
	{
		var (ax, ay) = CellAt( PointA );
		for ( var dx = 100f; dx <= 900; dx += 50 )
			for ( var dy = -400f; dy <= 400; dy += 50 )
			{
				var candidate = new UiButton { Bounds = new UiRect( dx, dy, 60, 60 ), Anchor = UiAnchor.Center };
				var centre = candidate.ScreenRect( context.Canvas ).Center;
				if ( (centre - PointA).Length() > 150 && level.TryGetGridCell( centre, new N.Vector2( 1000 ), out var x, out var y ) && (x, y) != (ax, ay) )
				{
					candidate.Clicked = clicked;
					hud.Screen.Add( candidate );
					return centre;
				}
			}
		Assert.Fail( "no button position over the park" );
		return default;
	}

	[TestMethod]
	public void ThePathToolIsEnteredByThePressAndItsFirstClickIsTheRelease()
	{
		// [BIN:TPW-EXE:0x4879D0 park click] the press enters the path tool (0x46C580(1), SetMode(1, 1)): no start, nothing laid; [BIN:TPW-EXE:0x524960] the release is its first click
		var cell = CellAt( PointA );
		Frame( new[] { Down( PointA ) }, left: true );
		Assert.IsTrue( level.CellTool.IsActive, "the press enters the tool" );
		Assert.AreEqual( CellToolMode.Path, level.CellTool.Mode );
		Assert.IsNull( level.CellTool.Start, "and stores no start" );
		Frame( Array.Empty<OrderedInputEvent>(), left: true );
		Assert.IsNull( level.CellTool.Start, "holding the button does not either" );
		Frame( new[] { Up( PointA ) } );
		Assert.AreEqual( cell, level.CellTool.Start, "the release is the first click" );
		Assert.AreEqual( 0, commands.Count, "a first click lays nothing" );
	}

	[TestMethod]
	public void APressDraggedToAnotherCellClicksTheCellUnderThePointerAtTheRelease()
	{
		// the tool object reads the hover cell (0x7B05CC) at the release, not a cell kept from the press
		var (ax, ay) = CellAt( PointA );
		var (bx, by) = CellAt( PointB );
		Assert.AreNotEqual( (ax, ay), (bx, by), "the two points lie on different cells" );
		Frame( new[] { Down( PointA ), Move( PointB ) }, mouse: PointB, left: true );
		Assert.IsTrue( level.CellTool.IsActive );
		Assert.IsNull( level.CellTool.Start );
		Frame( new[] { Up( PointB ) }, mouse: PointB );
		Assert.AreEqual( (bx, by), level.CellTool.Start, "the first click is at the cell of the release" );
	}

	[TestMethod]
	public void ALineCommitsOnTheReleaseOfItsSecondClickNotOnItsPress()
	{
		level.CellTool.Enter( new RefusingTrackWriter( null ), CellAt( PointA ) );
		Frame( new[] { Down( PointB ) }, mouse: PointB, left: true );
		Assert.IsTrue( level.CellTool.IsActive );
		Assert.AreEqual( 0, level.CellTool.SegmentCount, "a press commits no segment" );
		Frame( new[] { Up( PointB ) }, mouse: PointB );
		Assert.AreEqual( 1, level.CellTool.SegmentCount, "the release lays the line" );
	}

	[TestMethod]
	public void ARefusedClickSoundsTheRefusalCueAtTheReleaseOnlyAndEndsTheTool()
	{
		// [BIN:TPW-EXE:0x524A63 refused ghost] [BIN:TPW-EXE:0x524AA7] the release with the refused-ghost latch set plays cat_ui 0xAF behind the press latch and ends the line tool; the press plays nothing
		var cell = CellAt( PointA );
		level.CellTool.Enter( new RefusingTrackWriter( cell ), null );
		Frame( new[] { Down( PointA ) }, left: true );
		Assert.IsTrue( level.CellTool.IsActive, "the press ends nothing" );
		CollectionAssert.AreEqual( Array.Empty<uint>(), sounds, "and is silent" );
		Frame( new[] { Up( PointA ) } );
		Assert.IsFalse( level.CellTool.IsActive, "the refused ghost ends the line tool at the release" );
		CollectionAssert.AreEqual( new[] { GameAudio.PathRefusedEvent }, sounds, "the release plays the refusal cue and no track click 0xCE" );
	}

	[TestMethod]
	public void ARefusedReleaseWithoutItsPressPlaysNothing()
	{
		// the cue is behind the press latch 0x8186D8: a release the tool object never saw the press of is silent (and cannot reach the park: its press was somewhere else)
		var cell = CellAt( PointA );
		level.CellTool.Enter( new RefusingTrackWriter( cell ), null );
		Frame( new[] { Up( PointA ) } );
		Assert.IsTrue( level.CellTool.IsActive, "a lone release is nobody's click" );
		CollectionAssert.AreEqual( Array.Empty<uint>(), sounds );
	}

	[TestMethod]
	public void AReleaseOverTheInterfaceBelongsToTheParkThatTookThePress()
	{
		// [BIN:TPW-EXE:0x658b7f release recipient] an up goes to the window of its down: pressed on the park and let go over a window, the click is the park's, at the cell under the pointer
		var clicks = 0;
		var over = AddButtonOverPark( () => clicks++ );
		var overCell = CellAt( over );
		level.CellTool.Enter( new RefusingTrackWriter( null ), null );
		Frame( new[] { Down( PointA ), Move( over ) }, mouse: over, left: true );
		Frame( new[] { Up( over ) }, mouse: over );
		Assert.AreEqual( 0, clicks, "the window did not take the press, so it takes no click" );
		Assert.AreEqual( overCell, level.CellTool.Start, "the tool's click is at the cell under the pointer at the release" );
	}

	[TestMethod]
	public void AReleaseOverTheParkAfterAPressOnTheInterfaceDoesNothingInThePark()
	{
		var clicks = 0;
		var over = AddButtonOverPark( () => clicks++ );
		level.CellTool.Enter( new RefusingTrackWriter( null ), null );
		Frame( new[] { Down( over ), Move( PointA ) }, mouse: PointA, left: true );
		Frame( new[] { Up( PointA ) }, mouse: PointA );
		Assert.AreEqual( 0, clicks, "the button's press is not finished on the button" );
		Assert.IsNull( level.CellTool.Start, "and the park saw neither the press nor the release" );
		Assert.AreEqual( 0, commands.Count );
		CollectionAssert.AreEqual( Array.Empty<uint>(), sounds );
		Frame( new[] { Move( over ), Down( over ), Up( over ) }, mouse: over );
		Assert.AreEqual( 1, clicks, "the button is a real target: pressed and released on it, it clicks" );
	}

	[TestMethod]
	public void TheRemoveToolActsOnTheReleaseAtTheCellUnderThePointerThere()
	{
		var (bx, by) = CellAt( PointB );
		level.IsRemovingObjects = true;
		Frame( new[] { Down( PointA ), Move( PointB ) }, mouse: PointB, left: true );
		Assert.AreEqual( 0, commands.Count, "the press removes nothing, and neither does the drag" );
		Frame( new[] { Up( PointB ) }, mouse: PointB );
		var removed = commands.OfType<RemoveAtCommand>().Single();
		Assert.AreEqual( (bx, by), (removed.X, removed.Y), "the release removes at the cell it ends on" );
	}

	[TestMethod]
	public void TheAreaClearToolStoresItsCornersOnTheReleases()
	{
		var tool = new AreaClearTool( ( _, _ ) => ParkCommandResult.Ok() );
		typeof( Level ).GetField( "<AreaClearTool>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( level, tool );
		tool.Begin();
		var (bx, by) = CellAt( PointB );
		Frame( new[] { Down( PointA ), Move( PointB ) }, mouse: PointB, left: true );
		Assert.IsNull( tool.Corner, "the press stores no corner" );
		Frame( new[] { Up( PointB ) }, mouse: PointB );
		Assert.AreEqual( (bx, by), tool.Corner, "the release stores the cell it ends on" );
	}

	[TestMethod]
	public void ARightPressDoesNothingAndAQuickReleaseCancelsTheTool()
	{
		// [BIN:TPW-EXE:0x488434] with RMBCancelOn a right press let go within 200 ms and 8 px ends the mode (0x52F200(0, 1)) at the release; the press itself does nothing
		level.CellTool.Enter( new RefusingTrackWriter( null ), null );
		Frame( new[] { Down( PointA, MouseButton.Right ) }, right: true );
		Assert.IsTrue( level.CellTool.IsActive, "the press cancels nothing" );
		Frame( new[] { Up( PointA, MouseButton.Right, advance: 150 ) } );
		Assert.IsFalse( level.CellTool.IsActive, "the quick release does" );
	}

	[TestMethod]
	public void ASlowOrWanderingRightPressIsTheCameraDragAndCancelsNothing()
	{
		level.CellTool.Enter( new RefusingTrackWriter( null ), null );
		Frame( new[] { Down( PointA, MouseButton.Right ) }, right: true );
		Frame( new[] { Up( PointA, MouseButton.Right, advance: 201 ) } );
		Assert.IsTrue( level.CellTool.IsActive, "after 200 ms the press was the camera's drag" );
		Frame( new[] { Down( PointA, MouseButton.Right ), Move( PointA + new N.Vector2( 8, 8 ) ), Up( PointA + new N.Vector2( 8, 8 ), MouseButton.Right ) } );
		Assert.IsFalse( level.CellTool.IsActive, "8 px on each axis is still a click" );
		level.CellTool.Enter( new RefusingTrackWriter( null ), null );
		Frame( new[] { Down( PointA, MouseButton.Right ), Move( PointA + new N.Vector2( 9, 0 ) ), Up( PointA, MouseButton.Right ) } );
		Assert.IsTrue( level.CellTool.IsActive, "a move of 9 px made it the drag, though it came back" );
	}

	[TestMethod]
	public void TheCancelOptionOffLeavesTheRightClickToTheCameraAlone()
	{
		GameOptions.Current = new GameOptions { RmbCancel = false };
		level.CellTool.Enter( new RefusingTrackWriter( null ), null );
		Frame( new[] { Down( PointA, MouseButton.Right ), Up( PointA, MouseButton.Right ) } );
		Assert.IsTrue( level.CellTool.IsActive );
	}

	[TestMethod]
	public void LegacyFramesATapOnTheInterfaceLetGoOverTheParkClicksNothing()
	{
		// the hand-fed path (co-op, a read-only visit, frames with no ordered events): the release acts only when the park view took its press
		var over = AddButtonOverPark( () => { } );
		level.IsRemovingObjects = true;
		Frame( null, mouse: over, left: true );
		Frame( null, mouse: PointA );
		Assert.AreEqual( 0, commands.Count, "a press on the interface is not the park's: its release over the park removes nothing" );
	}

	[TestMethod]
	public void LegacyFramesAPressOnTheParkLetGoOverTheInterfaceClicksAtTheCellThere()
	{
		var over = AddButtonOverPark( () => { } );
		var (ox, oy) = CellAt( over );
		level.IsRemovingObjects = true;
		Frame( null, mouse: PointA, left: true );
		Assert.AreEqual( 0, commands.Count );
		Frame( null, mouse: over );
		var removed = commands.OfType<RemoveAtCommand>().Single();
		Assert.AreEqual( (ox, oy), (removed.X, removed.Y), "the park's click, at the cell under the pointer at the release" );
	}

	[TestMethod]
	public void ABlurReleaseEndsThePressWithNoClickAndNoCue()
	{
		// a browser blur makes up a release with the last position only: the gesture ends, the tools never click
		var cell = CellAt( PointA );
		level.CellTool.Enter( new RefusingTrackWriter( cell ), null );
		Frame( new[] { Down( PointA ) }, left: true );
		Frame( new[] { Up( PointA ) with { Source = OrderedInputEventSource.Browser, PositionIsExact = false } } );
		Assert.IsTrue( level.CellTool.IsActive, "no click: the refused ghost did not end the tool" );
		CollectionAssert.AreEqual( Array.Empty<uint>(), sounds, "and no refusal cue" );
		level.CellTool.Cancel();
		level.IsRemovingObjects = true;
		Frame( new[] { Down( PointA ) }, left: true );
		Frame( new[] { Up( PointA ) with { Source = OrderedInputEventSource.Browser, PositionIsExact = false } } );
		Assert.AreEqual( 0, commands.Count, "the removal is not clicked either" );
		level.IsRemovingObjects = false;
		Frame( new[] { Down( PointA ) }, left: true );
		Frame( new[] { Up( PointA ) with { Source = OrderedInputEventSource.Browser, PositionIsExact = false } } );
		Assert.IsTrue( level.CellTool.IsActive, "the tool the press entered stays, with no start" );
		Assert.IsNull( level.CellTool.Start );
		Frame( new[] { Up( PointA ) } );
		Assert.IsNull( level.CellTool.Start );
	}

	[TestMethod]
	public void AToolArmedBetweenAPressAndItsReleaseTakesNoClickFromThatRelease()
	{
		// a co-op guest's Move pick-up coming back (CompleteMove) arms the build tool while the button is down: the press was the idle mode's, so its release places nothing
		// with no path builder the press enters nothing: it is the idle mode's alone
		typeof( Level ).GetField( "<Paths>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( level, null );
		Frame( new[] { Down( PointA ) }, left: true );
		Assert.IsFalse( level.CellTool.IsActive );
		level.IsRemovingObjects = true;
		Frame( new[] { Up( PointA ) } );
		Assert.AreEqual( 0, commands.Count, "the release of a press that saw no tool is no click of the tool armed since" );
		Frame( new[] { Down( PointA ), Up( PointA ) } );
		Assert.AreEqual( 1, commands.Count, "a fresh click of the armed tool acts" );
	}
}
