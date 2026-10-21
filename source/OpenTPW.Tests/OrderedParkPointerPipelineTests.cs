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
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// Synthetic CPU-only end-to-end input tests. They run the real source, HUD, screen and level
/// fallback, not the router in isolation. No SDL pump, renderer, game data or executable is used.
/// </summary>
[TestClass, DoNotParallelize]
public class OrderedParkPointerPipelineTests
{
	private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
	private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
	private static readonly N.Vector2 ParkPoint = new( 500, 400 );
	private ParkHud hud = null!;
	private Level level = null!;
	private UiContext context = null!;
	private UiInputSource source = null!;
	private Action restore = () => { };
	private readonly List<ParkCommand> commands = new();
	private readonly List<int> commandModifiers = new();
	private long sequence;
	private long milliseconds = 1000;
	private int updating;

	private sealed class EmptyCatalog : IBuildCatalog
	{
		public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) => Array.Empty<BuildItem>();
	}

	private static FieldInfo InputField( string name ) => typeof( Input ).GetField( name, StaticPrivate )
		?? throw new AssertFailedException( "Input fixture field not found: " + name );
	private static void Set( object target, string name, object? value ) =>
		(typeof( Level ).IsInstanceOfType( target ) ? typeof( Level ) : target.GetType()).GetField( name, InstancePrivate )!.SetValue( target, value );

	[TestInitialize]
	public void Initialize()
	{
		commands.Clear(); commandModifiers.Clear(); sequence = 0; milliseconds = 1000; updating = 0;
		// UpdateFrom invokes SDL_ShowCursor, and UpdateButtons drains injected keys/localizes bindings.
		// Save and inject only the managed state consumed by Poll and the HUD. Preserve old collection
		// identities and the private frame-key contents so unrelated fixtures cannot inherit our state.
		var oldMouse = Input.Mouse;
		var oldKeyboard = Input.Keyboard;
		var oldDown = Input.KeysDown;
		var oldLast = Input.LastKeysDown;
		var oldText = Input.TextEntryActive;
		var oldCursor = Input.CursorType;
		var oldSoftwareCursor = Input.SoftwareCursorRequested;
		var oldTyped = Input.Typed;
		var oldPresses = Input.KeyPresses;
		var oldOrdered = Input.OrderedEvents;
		var frameKeys = (HashSet<Key>)InputField( "frameKeys" ).GetValue( null )!;
		var oldFrameKeys = frameKeys.ToArray();
		var bindings = InputField( "Bindings" );
		var oldBindings = (Dictionary<InputButton, Key[]>)bindings.GetValue( null )!;
		var oldPointerModifiers = InputField( "pointerModifierMask" ).GetValue( null );
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
			InputField( "<Typed>k__BackingField" ).SetValue( null, oldTyped );
			InputField( "<KeyPresses>k__BackingField" ).SetValue( null, oldPresses );
			InputField( "<OrderedEvents>k__BackingField" ).SetValue( null, oldOrdered );
			InputField( "pointerModifierMask" ).SetValue( null, oldPointerModifiers );
			frameKeys.Clear(); frameKeys.UnionWith( oldFrameKeys );
			bindings.SetValue( null, oldBindings );
			Camera.Mode = oldMode; Camera.ViewMatrix = oldView; Camera.ProjMatrix = oldProjection;
			GameEditions.Select( oldEdition );
			GameOptions.Current = oldOptions; RideCameraMode.PointerOverride = oldRidePointer;
			Log = oldLog;
		};
		Input.Mouse = new(); Input.Keyboard = new(); Input.KeysDown = new(); Input.LastKeysDown = new();
		Input.TextEntryActive = false;
		InputField( "<Typed>k__BackingField" ).SetValue( null, "" );
		InputField( "<KeyPresses>k__BackingField" ).SetValue( null, Array.Empty<Key>() );
		InputField( "<OrderedEvents>k__BackingField" ).SetValue( null, null );
		InputField( "pointerModifierMask" ).SetValue( null, null );
		frameKeys.Clear();
		bindings.SetValue( null, oldBindings.ToDictionary( pair => pair.Key, pair => (Key[])pair.Value.Clone() ) );
		GameEditions.Select( GameEdition.ThemeParkWorld );
		GameOptions.Current = new GameOptions();
		Camera.Mode = new ParkCameraMode(); Camera.ViewMatrix = Camera.ProjMatrix = N.Matrix4x4.Identity;
		var fixture = VisitorWorldSelectionTests.Fixture( out _, out _ );
		level = (Level)typeof( ParkHud ).GetField( "level", InstancePrivate )!.GetValue( fixture )!;
		// Record calls at the real Level fallback boundary without loading or mutating a rendered park.
		Set( level, "commands", new ParkCommands( () => 0, command =>
		{
			commands.Add( command ); commandModifiers.Add( Input.ModifierMaskDown );
			return ParkCommandResult.Ok();
		}, () => false ) );
		level.IsRemovingObjects = true;
		hud = new ParkHud( level, OriginalUiTests.FakeStrings(), new NoEconomyStatus(), new EmptyCatalog(), new HudHost() );
		foreach ( var element in hud.Screen.Elements.ToArray() ) hud.Screen.Remove( element );
		hud.Screen.Updating = _ => updating++;
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) )
			{ Canvas = new UiCanvas( 1000, 1000 ), PopupHelp = false, ResolveTexture = _ => null };
		source = new UiInputSource { Clock = () => milliseconds };
		Assert.IsTrue( level.TryGetGridCell( ParkPoint, new N.Vector2( 1000 ), out _, out _ ), "The synthetic camera must hit the sandbox." );
	}

	[TestCleanup]
	public void Cleanup() => restore();

	private OrderedInputEvent Event( OrderedInputEventKind kind, N.Vector2? point = null, Key key = Key.Unknown, bool repeat = false )
	{
		var mouse = point ?? ParkPoint;
		return new OrderedInputEvent( ++sequence, kind, mouse.X, mouse.Y,
			kind is OrderedInputEventKind.ButtonDown or OrderedInputEventKind.ButtonUp ? (int)MouseButton.Left : -1,
			(int)key, Repeat: repeat, TimestampMilliseconds: ++milliseconds, PositionIsExact: true );
	}

	private OrderedInputEvent Down( N.Vector2? point = null ) => Event( OrderedInputEventKind.ButtonDown, point );
	private OrderedInputEvent Up( N.Vector2? point = null ) => Event( OrderedInputEventKind.ButtonUp, point );
	private OrderedInputEvent KeyDown( Key key, bool repeat = false ) => Event( OrderedInputEventKind.KeyDown, key: key, repeat: repeat );
	private OrderedInputEvent KeyUp( Key key ) => Event( OrderedInputEventKind.KeyUp, key: key );

	private UiInput Poll( IReadOnlyList<OrderedInputEvent>? events, bool left = false, N.Vector2? mouse = null,
		Key[]? keys = null, float scaleX = 1, float scaleY = 1 )
	{
		var position = mouse ?? ParkPoint;
		Input.Mouse = new Input.MouseInfo { Position = new( position.X, position.Y ), Left = left };
		Input.Keyboard = new Input.KeyboardInfo( (keys ?? Array.Empty<Key>()).ToList() );
		var frameKeys = (HashSet<Key>)InputField( "frameKeys" ).GetValue( null )!;
		frameKeys.Clear(); frameKeys.UnionWith( Input.Keyboard.KeysDown );
		InputField( "<OrderedEvents>k__BackingField" ).SetValue( null, events );
		return source.Poll( scaleX, scaleY );
	}

	private UiInput Frame( IReadOnlyList<OrderedInputEvent>? events, bool left = false, N.Vector2? mouse = null,
		Key[]? keys = null, float scaleX = 1, float scaleY = 1, bool hostBlocked = false, bool mouseCaptured = false )
	{
		var input = Poll( events, left, mouse, keys, scaleX, scaleY ) with { PointerHostBlocked = hostBlocked };
		var captured = hud.Update( context, input, mouseCaptured, false, false );
		// This is the actual next-stage API used by Level.Update, including snapshot-only fallback.
		level.UpdateWorldInput( Input.Mouse.Left, captured, input.Mouse,
			new N.Vector2( context.Canvas.Width, context.Canvas.Height ), Input.ModifierMaskDown );
		return input;
	}

	private N.Vector2 AddButton( Action click, UiScreen? screen = null )
	{
		var button = (screen ?? hud.Screen).Add( new UiButton
			{ Bounds = new UiRect( 980, 530, 100, 100 ), Anchor = UiAnchor.Center, Clicked = click } );
		return button.ScreenRect( context.Canvas ).Center;
	}

	private void TapPause() => Frame( new[] { KeyDown( Input.ChordKey( InputButton.Pause ) ), KeyUp( Input.ChordKey( InputButton.Pause ) ) } );

	[TestMethod]
	public void PollScalesEveryRecordCopiesTheBatchAndRetainsEventLocalModifiers()
	{
		var raw = new[] { KeyDown( Key.ControlLeft ), Down(), Up(), KeyUp( Key.ControlLeft ), Down(), Up() };
		var input = Poll( raw, scaleX: 2, scaleY: 3 );
		Assert.AreEqual( new N.Vector2( 1000, 1200 ), input.Mouse );
		Assert.AreNotSame( raw, input.OrderedEvents );
		CollectionAssert.AreEqual( raw.Select( item => item.Sequence ).ToArray(), input.OrderedEvents!.Select( item => item.Sequence ).ToArray() );
		Assert.AreEqual( 1000f, input.OrderedEvents[1].X ); Assert.AreEqual( 1200f, input.OrderedEvents[1].Y );
		Assert.AreEqual( (int)ModifierKeys.Control, input.OrderedEvents[1].Modifiers );
		Assert.AreEqual( 0, input.OrderedEvents[4].Modifiers );
		Assert.AreEqual( ParkPoint.X, raw[1].X, "Poll must not rewrite the producer's reusable batch." );
	}

	[TestMethod]
	public void MultipleShortUiClicksAreDeliveredOnceAndUpdatingRunsOnlyOnce()
	{
		var clicks = 0; var point = AddButton( () => clicks++ );
		var input = Frame( new[] { Down( point ), Up( point ), Down( point ), Up( point ), Down( point ), Up( point ) }, mouse: point );
		Assert.IsFalse( input.LeftPressed ); Assert.IsFalse( input.LeftReleased );
		Assert.AreEqual( 3, clicks ); Assert.AreEqual( 1, updating ); Assert.AreEqual( 0, commands.Count );
		Frame( Array.Empty<OrderedInputEvent>(), mouse: point );
		Assert.AreEqual( 3, clicks, "The resting delivery cannot replay the last release." );
		Assert.AreEqual( 2, updating );
	}

	[TestMethod]
	public void MultipleShortParkClicksReachLevelExactlyOnceEvenWhenFinalSnapshotIsUp()
	{
		var input = Frame( new[] { Down(), Up(), Down(), Up() } );
		Assert.IsFalse( input.LeftDown ); Assert.IsFalse( input.LeftPressed );
		Assert.AreEqual( 2, commands.Count ); Assert.IsTrue( commands.All( command => command is RemoveAtCommand ) );
		Assert.IsTrue( level.WorldPointerRouted ); Assert.AreEqual( 1, updating );
		Frame( Array.Empty<OrderedInputEvent>() );
		Assert.AreEqual( 2, commands.Count );
	}

	[TestMethod]
	public void HeldSnapshotCannotRepeatTheAlreadyDispatchedLevelPress()
	{
		Frame( new[] { Down() }, left: true );
		Assert.AreEqual( 1, commands.Count, "Routed down plus Level.UpdateWorldInput is one command, not two." );
		Frame( Array.Empty<OrderedInputEvent>(), left: true );
		Frame( new[] { Up() } );
		Assert.AreEqual( 1, commands.Count );
		Frame( new[] { Down() }, left: true );
		Assert.AreEqual( 2, commands.Count );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void PauseDoesNotRetargetAnyParkClickAlreadyAdmittedInItsBatch( bool keyFirst )
	{
		var pause = Input.ChordKey( InputButton.Pause );
		var events = keyFirst ? new[] { KeyDown( pause ), KeyUp( pause ), Down(), Up() }
			: new[] { Down(), Up(), KeyDown( pause ), KeyUp( pause ) };
		Frame( events );
		Assert.AreEqual( GameSpeed.Paused, hud.Status.Speed ); Assert.AreEqual( 1, commands.Count );
		Frame( new[] { Down(), Up() } );
		Assert.AreEqual( 1, commands.Count, "A new batch is owned by the pause overlay." );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void ResumeDoesNotReleaseOverlayOwnedClicksFromTheSameBatch( bool keyFirst )
	{
		TapPause(); Assert.AreEqual( GameSpeed.Paused, hud.Status.Speed );
		var pause = Input.ChordKey( InputButton.Pause );
		Frame( keyFirst ? new[] { KeyDown( pause ), KeyUp( pause ), Down(), Up() }
			: new[] { Down(), Up(), KeyDown( pause ), KeyUp( pause ) } );
		Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed ); Assert.AreEqual( 0, commands.Count );
		Frame( new[] { Down(), Up() } );
		Assert.AreEqual( 1, commands.Count, "The following fresh gesture is admitted to the park." );
	}

	[TestMethod]
	public void UiPressBeforePauseKeepsItsReleaseOwner()
	{
		var clicks = 0; var point = AddButton( () => clicks++ );
		Frame( new[] { Down( point ) }, left: true, mouse: point );
		var pause = Input.ChordKey( InputButton.Pause );
		Frame( new[] { KeyDown( pause ), KeyUp( pause ) }, left: true, mouse: point );
		Frame( new[] { Up( point ) }, mouse: point );
		Assert.AreEqual( GameSpeed.Paused, hud.Status.Speed );
		Assert.AreEqual( 1, clicks, "Pause blocks new presses, not release to an already-held live UI owner." );
		Assert.AreEqual( 0, commands.Count );
	}

	[TestMethod]
	public void OverlayPressHeldAcrossResumeCannotClickTheNewlyExposedButton()
	{
		var clicks = 0; var point = AddButton( () => clicks++ );
		TapPause();
		Frame( new[] { Down( point ) }, left: true, mouse: point );
		var pause = Input.ChordKey( InputButton.Pause );
		Frame( new[] { KeyDown( pause ), KeyUp( pause ) }, left: true, mouse: point );
		Frame( new[] { Up( point ) }, mouse: point );
		Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed ); Assert.AreEqual( 0, clicks ); Assert.AreEqual( 0, commands.Count );
		Frame( new[] { Down( point ), Up( point ) }, mouse: point );
		Assert.AreEqual( 1, clicks );
	}

	[TestMethod]
	public void AuthoritativeEmptyBatchCannotInventOrReplayADeferredSnapshotRelease()
	{
		var clicks = 0; var point = AddButton( () => clicks++ );
		Frame( new[] { Down( point ) }, left: true, mouse: point );
		var empty = Frame( Array.Empty<OrderedInputEvent>(), mouse: point );
		Assert.IsTrue( empty.LeftReleased, "The legacy snapshot sees a release, but it is not authoritative here." );
		Assert.AreEqual( 0, clicks );
		Frame( new[] { Up( point ) }, mouse: point );
		Assert.AreEqual( 1, clicks );
		Frame( Array.Empty<OrderedInputEvent>(), mouse: point );
		Assert.AreEqual( 1, clicks );
	}

	[TestMethod]
	public void MissingBatchQuarantinesANewPressUntilAFreshOrderedGesture()
	{
		Frame( Array.Empty<OrderedInputEvent>() );
		Frame( null, left: true ); Frame( null );
		Assert.AreEqual( 0, commands.Count, "Unavailable ordering must not guess a new park press." );
		Frame( new[] { Down(), Up() } );
		Assert.AreEqual( 1, commands.Count );
	}

	[TestMethod]
	public void RemovedScreenCannotForwardItsRemainingAdmittedClicksToThePark()
	{
		var modal = new UiScreen( "pipeline-modal" );
		var updates = 0; var clicks = 0;
		modal.Updating = _ => updates++;
		var point = AddButton( () => { clicks++; hud.Stack.Pop(); }, modal );
		hud.Stack.Push( modal );
		Frame( new[] { Down( point ), Up( point ), Down( point ), Up( point ) }, mouse: point );
		Assert.AreEqual( 1, clicks ); Assert.AreEqual( 1, updates ); Assert.IsTrue( modal.IsRemoved );
		Assert.AreEqual( 0, commands.Count );
		Frame( new[] { Down(), Up() } );
		Assert.AreEqual( 1, commands.Count );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void RealLevelFallbackUsesEachClicksModifierStateNotTheFinalKeyboardSnapshot( bool controlFirst )
	{
		if ( controlFirst )
			Frame( new[] { KeyDown( Key.ControlLeft ), Down(), Up(), KeyUp( Key.ControlLeft ), Down(), Up() } );
		else
			Frame( new[] { Down(), Up(), KeyDown( Key.ControlRight ), Down(), Up() }, keys: new[] { Key.ControlRight } );
		CollectionAssert.AreEqual( controlFirst ? new[] { 0xc, 0 } : new[] { 0, 0xc }, commandModifiers.ToArray() );
		Assert.AreEqual( controlFirst ? 0 : 0xc, Input.ModifierMaskDown, "Event scope restores the final physical state after dispatch." );
	}

	[TestMethod]
	public void FocusClearedKeysAndMissingRawBatchDoNotLeaveControlStuckOnTheNextClick()
	{
		Frame( new[] { KeyDown( Key.ControlLeft ) }, keys: new[] { Key.ControlLeft } );
		Assert.AreEqual( 0xc, Input.ModifierMaskDown );
		// A focus transition can clear the final snapshot without an observed Ctrl-up event.
		Frame( null );
		Frame( new[] { Down(), Up() } );
		CollectionAssert.AreEqual( new[] { 0 }, commandModifiers.ToArray() );
		Assert.AreEqual( 0, Input.ModifierMaskDown );
	}

	[TestMethod]
	public void ActualPollAndHudHonorRemappedPauseAndIgnoreRepeatAndControlChord()
	{
		var bindings = (Dictionary<InputButton, Key[]>)InputField( "Bindings" ).GetValue( null )!;
		bindings[InputButton.Pause] = new[] { Key.F8 };
		Frame( new[] { KeyDown( Key.P ), KeyUp( Key.P ) } );
		Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed );
		var input = Frame( new[] { KeyDown( Key.F8 ) }, keys: new[] { Key.F8 } );
		Assert.IsTrue( input.Has( UiKeys.Pause ) ); Assert.AreEqual( GameSpeed.Paused, hud.Status.Speed );
		Frame( new[] { KeyDown( Key.F8, repeat: true ) }, keys: new[] { Key.F8 } );
		Assert.AreEqual( GameSpeed.Paused, hud.Status.Speed );
		Frame( new[] { KeyUp( Key.F8 ), KeyDown( Key.ControlLeft ), KeyDown( Key.F8 ), KeyUp( Key.F8 ), KeyUp( Key.ControlLeft ) } );
		Assert.AreEqual( GameSpeed.Paused, hud.Status.Speed, "Ctrl plus the rebound key is not plain pause." );
		Frame( new[] { KeyDown( Key.F8 ), KeyUp( Key.F8 ) } );
		Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed );
	}

	[DataTestMethod]
	[DataRow( 5f, true )]
	[DataRow( 7f, false )]
	public void OrderedClickSlopUsesLogicalWindowPixelsUnderNonUniformFramebufferScaling( float drift, bool clicks )
	{
		var count = 0; var framebufferPoint = AddButton( () => count++ );
		var windowPoint = new N.Vector2( framebufferPoint.X / 2, framebufferPoint.Y / 3 );
		var release = windowPoint + new N.Vector2( drift, 0 );
		Frame( new[] { Down( windowPoint ), Event( OrderedInputEventKind.Motion, release ), Up( release ) },
			mouse: release, scaleX: 2, scaleY: 3 );
		Assert.AreEqual( clicks ? 1 : 0, count ); Assert.AreEqual( 1, updating ); Assert.AreEqual( 0, commands.Count );
	}

	[TestMethod]
	public void OrderedTextFieldPressOwnsFollowingPauseKeyUntilAnOrderedClickOutside()
	{
		var field = hud.Screen.Add( new UiTextField { Bounds = new UiRect( 500, 500, 200, 100 ), Anchor = UiAnchor.Center } );
		var point = field.ScreenRect( context.Canvas ).Center;
		var pause = Input.ChordKey( InputButton.Pause );
		Frame( new[] { Down( point ), Up( point ), KeyDown( pause ), KeyUp( pause ) }, mouse: point );
		Assert.AreSame( field, hud.Screen.Focused ); Assert.IsTrue( Input.TextEntryActive );
		Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed ); Assert.AreEqual( 0, commands.Count );
		Frame( new[] { Event( OrderedInputEventKind.Motion ), Down(), Up(), KeyDown( pause ), KeyUp( pause ) } );
		Assert.IsNull( hud.Screen.Focused ); Assert.IsFalse( Input.TextEntryActive );
		Assert.AreEqual( GameSpeed.Paused, hud.Status.Speed, "The click-out clears text ownership before the following P is drained." );
		Assert.AreEqual( 1, commands.Count );
	}

	[TestMethod]
	public void OrderedRightClickClosesInfoArmWithoutFallingThroughToPark()
	{
		// Inject only the information-arm input latch after the asset-dependent selection/layout pass.
		// This is deliberately not a rendered object-window fixture; the full HUD frame still dispatches RMB.
		hud.Screen.Updating = _ => { updating++; Set( hud, "<InfoArmOpen>k__BackingField", true ); };
		var rightDown = Down() with { Button = (int)MouseButton.Right };
		var rightUp = Up() with { Button = (int)MouseButton.Right };
		Frame( new[] { rightDown, rightUp } );
		Assert.IsFalse( hud.InfoArmOpen ); Assert.AreEqual( 0, commands.Count ); Assert.AreEqual( 1, updating );
	}

	[TestMethod]
	public void PhysicalKeyReductionRetainsUpThenDownAndTheNextRawClickKeepsControl()
	{
		var physical = Input.ReducePhysicalKeys( new[] { Key.ControlLeft }, new[]
		{
			new KeyEvent( Key.ControlLeft, false, ModifierKeys.None ),
			new KeyEvent( Key.ControlLeft, true, ModifierKeys.Control ),
			new KeyEvent( Key.ControlLeft, true, ModifierKeys.Control, true )
		} );
		CollectionAssert.AreEqual( new[] { Key.ControlLeft }, physical.ToArray() );
		Frame( new[] { KeyUp( Key.ControlLeft ), KeyDown( Key.ControlLeft ), KeyDown( Key.ControlLeft, repeat: true ) }, keys: physical.ToArray() );
		Frame( new[] { Down(), Up() }, keys: physical.ToArray() );
		CollectionAssert.AreEqual( new[] { 0xc }, commandModifiers.ToArray() );
		var released = Input.ReducePhysicalKeys( physical, new[] { new KeyEvent( Key.ControlLeft, false, ModifierKeys.None ) } );
		Assert.AreEqual( 0, released.Count );
		var tapped = Input.ReducePhysicalKeys( Array.Empty<Key>(), new[]
		{
			new KeyEvent( Key.ControlLeft, true, ModifierKeys.Control ),
			new KeyEvent( Key.ControlLeft, true, ModifierKeys.Control, true ),
			new KeyEvent( Key.ControlLeft, false, ModifierKeys.None )
		} );
		Assert.AreEqual( 0, tapped.Count );
	}

	[TestMethod]
	public void ParkBuildHostBlockSuppressesRawUiWorldAndPauseCallbacks()
	{
		var clicks = 0; var point = AddButton( () => clicks++ );
		var pause = Input.ChordKey( InputButton.Pause );
		Frame( new[] { Down( point ), Up( point ), Event( OrderedInputEventKind.Motion, new N.Vector2( 700, 600 ) ),
			Down( new N.Vector2( 700, 600 ) ), Up( new N.Vector2( 700, 600 ) ), KeyDown( pause ), KeyUp( pause ) }, hostBlocked: true );
		Assert.AreEqual( 0, clicks ); Assert.AreEqual( 0, commands.Count );
		Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed ); Assert.AreEqual( 1, updating );
		Frame( new[] { Event( OrderedInputEventKind.Motion, point ), Down( point ), Up( point ) }, mouse: point );
		Assert.AreEqual( 1, clicks, "Unblocked fresh gestures resume normally." );
	}

	[DataTestMethod]
	[DataRow( "camcorder" )]
	[DataRow( "ride" )]
	[DataRow( "walk" )]
	public void EnteringAnUnsupportedViewInsideAUiCallbackSuspendsTheRestOfTheBatch( string mode )
	{
		var clicks = 0;
		var point = AddButton( () =>
		{
			clicks++;
			// Enter only the mode marker; do not create a renderer, open a ride or execute a camera.
			if ( mode == "camcorder" ) Set( hud, "<CamcorderArmed>k__BackingField", true );
			else if ( mode == "ride" ) Set( level, "<RideView>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( RideCameraMode ) ) );
			else Set( level, "<FreeWalk>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( FreeWalkCameraMode ) ) );
		} );
		Frame( new[] { Down( point ), Up( point ), Down( point ) }, left: true, mouse: point );
		Assert.AreEqual( 1, clicks ); Assert.AreEqual( 0, commands.Count );
		Set( hud, "<CamcorderArmed>k__BackingField", false );
		Set( level, "<RideView>k__BackingField", null ); Set( level, "<FreeWalk>k__BackingField", null );
		Frame( new[] { Up( point ) }, mouse: point );
		Assert.AreEqual( 1, clicks, "Returning cannot revive a release from the suspended view's press." );
		Frame( new[] { Down( point ), Up( point ) }, mouse: point );
		Assert.AreEqual( 2, clicks );
	}

	[TestMethod]
	public void RemovingAndReaddingTheSameElementCannotReviveItsOldPress()
	{
		var clicks = 0;
		var button = hud.Screen.Add( new UiButton { Bounds = new UiRect( 980, 530, 100, 100 ), Anchor = UiAnchor.Center, Clicked = () => clicks++ } );
		var point = button.ScreenRect( context.Canvas ).Center;
		Frame( new[] { Down( point ) }, left: true, mouse: point );
		hud.Screen.Remove( button ); hud.Screen.Add( button );
		Frame( new[] { Up( point ) }, mouse: point );
		Assert.AreEqual( 0, clicks );
		Frame( new[] { Down( point ), Up( point ) }, mouse: point );
		Assert.AreEqual( 1, clicks ); Assert.AreEqual( 0, commands.Count );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void HostBlockedPressCannotActivateAButtonWhenItsReleaseArrivesUnblocked( bool buildBlocked )
	{
		var clicks = 0; var point = AddButton( () => clicks++ );
		Frame( new[] { Down( point ) }, left: true, mouse: point, hostBlocked: buildBlocked, mouseCaptured: !buildBlocked );
		Frame( new[] { Up( point ) }, mouse: point );
		Assert.AreEqual( 0, clicks ); Assert.AreEqual( 0, commands.Count );
		Frame( new[] { Down( point ), Up( point ) }, mouse: point );
		Assert.AreEqual( 1, clicks );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void HostSuspensionClearsARealSlidersCaptureWithoutDraggingOrReleasingIt( bool buildBlocked )
	{
		var value = 10; var grabs = 0; var releases = 0;
		var slider = hud.Screen.Add( new UiSlider
		{
			Bounds = new UiRect( 980, 530, 400, 100 ), Track = new UiRect( 980, 530, 400, 100 ),
			Anchor = UiAnchor.Center, KnobTop = 530, KnobSize = new N.Vector2( 50, 100 ), Truncates = true,
			Steps = () => 101, Value = () => value, Changed = next => value = next,
			Grabbed = _ => grabs++, Released = () => releases++
		} );
		var point = context.Canvas.Map( slider.KnobRect( value ), slider.Anchor ).Center;
		Frame( new[] { Down( point ) }, left: true, mouse: point );
		Assert.AreSame( slider, hud.Screen.PointerDragCapture ); Assert.AreEqual( 1, grabs );
		var moved = point + new N.Vector2( 40, 0 );
		Frame( new[] { Event( OrderedInputEventKind.Motion, moved ) }, left: true, mouse: moved );
		Assert.IsTrue( value > 10, "The real slider handled a captured motion before suspension." );
		var before = value; var blocked = moved + new N.Vector2( 80, 0 );
		Frame( new[] { Event( OrderedInputEventKind.Motion, blocked ) }, left: true, mouse: blocked,
			hostBlocked: buildBlocked, mouseCaptured: !buildBlocked );
		Assert.IsNull( hud.Screen.PointerDragCapture ); Assert.AreEqual( before, value ); Assert.AreEqual( 0, releases );
		Frame( new[] { Up( blocked ) }, mouse: blocked );
		Assert.AreEqual( before, value ); Assert.AreEqual( 0, releases, "The cancelled gesture's stale up is not a slider release callback." );
		var fresh = context.Canvas.Map( slider.KnobRect( value ), slider.Anchor ).Center;
		Frame( new[] { Event( OrderedInputEventKind.Motion, fresh ), Down( fresh ), Up( fresh ) }, mouse: fresh );
		Assert.AreEqual( 2, grabs ); Assert.AreEqual( 1, releases ); Assert.AreEqual( 0, commands.Count );
	}

	[TestMethod]
	public void HostSuspensionSettlesARealExistingCoasterHeightDragExactlyOnce()
	{
		var ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Synthetic" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, (20, 20) ); builder.Height = 40;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		var edits = new CoasterEdits(); var applied = new List<ParkCommand>();
		var panel = new CoasterToolPanel( builder, null, 0, command =>
		{
			applied.Add( command );
			return CoasterPanelCommand.ApplyTo( command, builder, edits );
		} );
		Assert.IsTrue( panel.Select( CoasterToolButton.Height ) );
		Assert.IsTrue( panel.Press( CoasterTrack.StationNodeCount, new N.Vector2( 100, 100 ), 24, 20 ) );
		panel.Drag( new N.Vector2( 100, 80 ), 0, null );
		Assert.IsTrue( panel.IsDragging ); Assert.IsNotNull( panel.DragCost );
		// Seed the already-started real tool, not its private drag flag. The following Poll/HUD frame
		// owns host cancellation; synthetic geometry is sufficient and no pylon rendering is needed.
		Set( level, "<CoasterPanel>k__BackingField", panel );
		Frame( new[] { Event( OrderedInputEventKind.Motion ) }, left: true, hostBlocked: true );
		Assert.IsFalse( panel.IsDragging ); Assert.IsNull( panel.DragCost );
		Assert.AreEqual( 1, applied.Count( command => command is EndCoasterNodeEditCommand ) );
		var afterSettlement = applied.Count;
		Frame( Array.Empty<OrderedInputEvent>(), hostBlocked: true );
		Frame( new[] { Up() } );
		Assert.AreEqual( afterSettlement, applied.Count, "Neither another blocked frame nor the eventual up settles or edits again." );
		Assert.AreEqual( 0, commands.Count );
	}

	[TestMethod]
	public void InexactBrowserReleaseEndsItsOwnersHoldWithoutInventingAClick()
	{
		var clicks = 0;
		var button = hud.Screen.Add( new UiButton
		{
			Bounds = new UiRect( 980, 530, 100, 100 ), Anchor = UiAnchor.Center, Clicked = () => clicks++,
			Repeat = new UiButtonRepeat( 1, 0.1f, () => { }, () => { } )
		} );
		var point = button.ScreenRect( context.Canvas ).Center;
		Frame( new[] { Down( point ) with { Source = OrderedInputEventSource.Browser } }, left: true, mouse: point );
		Assert.AreEqual( 1, button.HoldState );
		Frame( new[] { Up( point ) with { Source = OrderedInputEventSource.Browser, PositionIsExact = false } }, mouse: point );
		Assert.AreEqual( 0, button.HoldState, "The uncertain release must still reach and release its original owner." );
		Assert.AreEqual( 0, clicks, "A matching cached coordinate is not evidence of a qualified browser click." );
		Frame( new[] { Down( point ) with { Source = OrderedInputEventSource.Browser },
			Up( point ) with { Source = OrderedInputEventSource.Browser } }, mouse: point );
		Assert.AreEqual( 1, clicks ); Assert.AreEqual( 0, commands.Count );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void CoasterRoleTransitionKeepsEditAndSuspensionSettlementInsideTheCurrentCommandRights( bool guest )
	{
		// Establish routing before the role transition so the next complete HUD frame must suspend it.
		Frame( Array.Empty<OrderedInputEvent>() );
		var ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), new CoasterWorld( 1 ), "Synthetic" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, (20, 20) ); builder.Height = 40;
		Assert.AreEqual( CoasterPlaceResult.Ok, builder.Place( 24, 20 ) );
		var edits = new CoasterEdits();
		var executed = new List<ParkCommand>();
		Set( level, "commands", new ParkCommands( () => 0, command =>
		{
			executed.Add( command );
			return CoasterPanelCommand.ApplyTo( command, builder, edits );
		}, () => false ) );
		var attempts = new List<(ParkCommand Command, CoopRole Role, int Player, ParkCommandResult Result)>();
		var panel = new CoasterToolPanel( builder, null, 0, command =>
		{
			var role = level.CoopRole; var player = level.Commands.Player;
			var result = level.Commands.Issue( command );
			attempts.Add( (command, role, player, result) );
			return result;
		} );
		Assert.IsTrue( panel.Select( CoasterToolButton.Height ) );
		Assert.IsTrue( panel.Press( CoasterTrack.StationNodeCount, new N.Vector2( 100, 100 ), 24, 20 ) );
		Set( level, "<CoasterPanel>k__BackingField", panel );
		Assert.IsNotNull( edits.Of( ParkCommands.LocalPlayer ) );
		ulong State()
		{
			var hash = new StateHasher();
			ride.AddCanonicalState( hash ); hash.Add( ride.PaidCost ); edits.AddSessionState( hash );
			return hash.Value;
		}
		var before = State(); var executedBefore = executed.Count;
		var roleAfter = guest ? CoopRole.Guest : CoopRole.Host;
		// Mirror only the stream's command-rights setup and the session's role marker. No session
		// constructor, stream, Attach, transport, simulation or network operation is invoked.
		var session = (CoopSession)RuntimeHelpers.GetUninitializedObject( typeof( CoopSession ) );
		Set( session, "<Role>k__BackingField", roleAfter );
		level.CoopSession = session;
		level.Commands.SessionRules = true;
		level.Commands.Rights = CoopHostOnlyRights.Instance;
		level.Commands.Player = guest ? 1 : ParkCommands.LocalPlayer;
		try
		{
			panel.Drag( new N.Vector2( 100, 80 ), 0, null );
			Assert.IsTrue( attempts[^1].Command is EditCoasterNodeCommand );
			Assert.AreEqual( !guest, attempts[^1].Result.Accepted );
			Assert.AreEqual( roleAfter, attempts[^1].Role );
			if ( guest ) Assert.AreEqual( before, State(), "A rejected guest edit must not mutate track, cost or held edits." );
			Frame( Array.Empty<OrderedInputEvent>() );
			Assert.IsFalse( panel.IsDragging, "The local gesture is stopped even when current rights refuse its settlement." );
			Assert.IsTrue( attempts[^1].Command is EndCoasterNodeEditCommand );
			Assert.AreEqual( !guest, attempts[^1].Result.Accepted );
			Assert.AreEqual( roleAfter, attempts[^1].Role );
			Assert.AreEqual( guest ? 1 : ParkCommands.LocalPlayer, attempts[^1].Player );
			Assert.AreEqual( executedBefore + (guest ? 0 : 2), executed.Count,
				"Both the edit and the release must pass through Level.Commands rather than applying the builder directly." );
			if ( guest )
			{
				Assert.AreEqual( before, State(), "Guest suspension cannot bypass rights to settle money or erase the authoritative held edit." );
				Assert.IsNotNull( edits.Of( ParkCommands.LocalPlayer ) );
			}
			else
			{
				Assert.AreNotEqual( before, State() );
				Assert.IsNull( edits.Of( ParkCommands.LocalPlayer ), "The allowed host end settles the existing edit." );
			}
			var attemptCount = attempts.Count;
			Frame( Array.Empty<OrderedInputEvent>() );
			Assert.AreEqual( attemptCount, attempts.Count, "The stopped gesture cannot retry settlement on every excluded-view frame." );
			if ( !guest )
			{
				Assert.IsTrue( panel.Press( CoasterTrack.StationNodeCount, new N.Vector2( 100, 100 ), 24, 20 ) );
				var afterNewCoopPress = attempts.Count;
				Frame( Array.Empty<OrderedInputEvent>() );
				Assert.IsTrue( panel.IsDragging, "Once offline routing is inactive, it cannot cancel a newly started co-op drag on every frame." );
				Assert.AreEqual( afterNewCoopPress, attempts.Count );
			}
		}
		finally { level.CoopSession = null; }
	}
	[TestMethod]
	public void PlatformKeyMaskSurvivesUnobservedFocusModifierAndSynchronizesLaterClick()
	{
		var pause = Input.ChordKey( InputButton.Pause );
		var input = Frame( new[] {
			KeyDown( pause ) with { Source = OrderedInputEventSource.Browser, Modifiers = (int)ModifierKeys.Control },
			Down(), Up() }, keys: new[] { pause } );
		Assert.AreEqual( GameSpeed.Normal, hud.Status.Speed, "The platform reports Ctrl+pause, not plain pause." );
		Assert.IsTrue( input.OrderedEvents!.All( item => item.ModifiersAreExact ) );
		Assert.IsTrue( input.OrderedEvents.All( item => item.Modifiers == (int)ModifierKeys.Control ) );
		CollectionAssert.AreEqual( new[] { 0xc }, commandModifiers );
	}

	[TestMethod]
	public void InitialStationaryAvailableFrameUsesKnownPointerWithoutInventingAnEdge()
	{
		var clicks = 0;
		var point = AddButton( () => clicks++ );
		Frame( Array.Empty<OrderedInputEvent>(), mouse: point );
		Assert.IsNotNull( hud.Screen.Focused, "Initial hover is at the known pointer, not (0,0)." );
		Assert.AreEqual( 0, clicks );
		Assert.AreEqual( 0, commands.Count );
		Assert.AreEqual( 1, updating );
	}

	[TestMethod]
	public void StationaryReentryAfterExcludedViewUsesItsCurrentPosition()
	{
		var clicks = 0; var point = AddButton( () => clicks++ );
		Frame( new[] { Event( OrderedInputEventKind.Motion, ParkPoint ) }, mouse: ParkPoint );
		Set( hud, "<CamcorderArmed>k__BackingField", true );
		Frame( Array.Empty<OrderedInputEvent>(), mouse: point );
		Set( hud, "<CamcorderArmed>k__BackingField", false );
		Frame( Array.Empty<OrderedInputEvent>(), mouse: point );
		Assert.IsNotNull( hud.Screen.Focused );
		Frame( new[] { Down( point ), Up( point ) }, mouse: point );
		Assert.AreEqual( 1, clicks );
		Assert.AreEqual( 0, commands.Count );
	}

}
