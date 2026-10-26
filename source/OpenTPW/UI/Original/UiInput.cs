using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>Keyboard navigation commands understood by original-style screens.</summary>
[Flags]
public enum UiKeys
{
	None = 0,
	Up = 1,
	Down = 2,
	Left = 4,
	Right = 8,
	Accept = 16,
	Back = 32,
	Pause = 64,
	/// <summary>Tab: next text field.</summary>
	Tab = 128,
	/// <summary>Set with <see cref="Accept"/> when it came from Space, which a text field types instead.</summary>
	Space = 256
}

/// <summary>
/// One frame of UI input in framebuffer pixels. Built from <see cref="Input"/> at runtime and
/// constructed directly by tests and the smoke test.
/// </summary>
/// <param name="OrderedUnavailable">
/// Set by <see cref="UiInputSource.Poll"/> when the platform could not supply the raw ordered batch for this frame. Hand-built input
/// (the smoke tests and fixtures) has no batch either, but its pointer edges are deliberate: the park routes them as injected edges
/// (<see cref="UiPointerRouter.AdmitInjected"/>), whereas an unavailable batch never starts a gesture.
/// </param>
/// <param name="BackspaceRepeating">Every Backspace of this frame is the auto-repeat of a held key (the pool bridge and water removal acts only on the first press).</param>
/// <param name="RightReleased">The right button was released this frame.</param>
/// <param name="RightClicked">
/// The right release closes a quick click: the press lasted at most <see cref="UiInputSource.RightClickMilliseconds"/> and the pointer never strayed more than
/// <see cref="UiInputSource.RightClickSlop"/> pixels on an axis. The park view cancels its tool and mode on this release, not on the press; a longer or wider press is the camera drag
/// (tp.exe 0x4881A0: the press is held back until the pointer moves or 200 ms pass).
/// </param>
/// <param name="NotClick">
/// Set on a left release that is not a click: the press lasted longer than <see cref="UiInputSource.ClickMilliseconds"/> or the
/// pointer strayed more than <see cref="UiInputSource.ClickSlop"/> pixels on an axis (tp.exe 0x65f6d1 / 0x65fa33). The release still ends drags.
/// </param>
public readonly record struct UiInput( NVector2 Mouse, bool LeftDown, bool LeftPressed, bool LeftReleased, bool RightPressed, UiKeys Pressed, float Wheel = 0, string Text = "", int Backspaces = 0, bool NotClick = false, IReadOnlyList<OrderedInputEvent>? OrderedEvents = null, bool PointerSuppressed = false, float PointerScaleX = 1, float PointerScaleY = 1, bool PointerHostBlocked = false, bool OrderedUnavailable = false, bool BackspaceRepeating = false, bool RightReleased = false, bool RightClicked = false )
{

	public static UiInput Idle( NVector2 mouse ) => new( mouse, false, false, false, false, UiKeys.None );
	public static UiInput Click( NVector2 mouse ) => new( mouse, false, false, true, false, UiKeys.None );
	public static UiInput Key( UiKeys keys ) => new( new NVector2( -1, -1 ), false, false, false, false, keys );
	public static UiInput Type( string text, int backspaces = 0 ) => new( new NVector2( -1, -1 ), false, false, false, false, UiKeys.None, 0, text, backspaces );
	public bool Has( UiKeys key ) => (Pressed & key) != 0;
}

/// <summary>Turns the global <see cref="Input"/> state into <see cref="UiInput"/> edges.</summary>
public sealed class UiInputSource
{
	/// <summary>A release within this many milliseconds of its press is a click.</summary>
	// [BIN:TPW-EXE:0x65f6d1 mouse input] a release (0x10004) posts a click (0x10006) only within 500 ms of its press (constant 0x77c480 = 0x1f4) and while the press is not marked moved
	public const long ClickMilliseconds = 500;
	/// <summary>The pointer may stray this many pixels on each axis from the press before the press is no longer a click.</summary>
	// [BIN:TPW-EXE:0x65fa33 mouse move] a move beyond 6 px on either axis from the press position (0x65fab7/0x65fad8: cmp 6) marks the press moved
	public const float ClickSlop = 6;

	/// <summary>A right press released within this many milliseconds, without straying, is a quick click (the park view's cancel); later it was the camera drag.</summary>
	// [BIN:TPW-EXE:0x488239 right click time] the park view's mouse procedure 0x4881A0, with RMBCancelOn set (0x4881AE), holds a right press back (0x7C2500, the time 0x7C2504) and turns it into the camera's drag (0x42A760 with 0x10005) on the first message 0x1E that finds it older than 200 ms (cmp 0xC8 at 0x48823F)
	public const long RightClickMilliseconds = 200;
	/// <summary>The pointer may stray this many pixels on each axis from the right press before it is the camera drag.</summary>
	// [BIN:TPW-EXE:0x488265 right click slop] a move (0x10003) more than 8 px from the press position on either axis (cmp 8 at 0x488265 and 0x48827F, the position kept at 0x7B5344/0x7B9774) turns the held-back press into the camera drag
	public const float RightClickSlop = 8;

	private bool wasLeft;
	private bool wasRight;
	private long pressedAt;
	private NVector2 pressedPosition;
	private bool pressMoved;
	private long rightPressedAt;
	private NVector2 rightPressedPosition;
	private bool rightMoved;
	private int rightFrames;
	private int pressFrames;
	/// <summary>The clock the click rule reads, in milliseconds (replaced by tests).</summary>
	internal Func<long> Clock { get; set; } = () => Environment.TickCount64;
	private HashSet<Key> previousKeys = new();

	public UiInput Poll( float framebufferPerWindowX, float framebufferPerWindowY )
	{
		var mouse = Input.Mouse;
		var keys = Input.Keyboard.KeysDown?.ToHashSet() ?? new HashSet<Key>();
		UiKeys pressed = UiKeys.None;
		void Map( Key key, UiKeys value )
		{
			if ( keys.Contains( key ) && !previousKeys.Contains( key ) )
				pressed |= value;
		}
		// [EXT:ui-key-navigation] arrows, Enter, Tab and Space navigate and activate the dialogs (none of the original's six key groups, 0x747f38-0x748378, contains them); Escape is the game group's menu key (0x40c180) and the pause key follows KEYBOARD.str string 20 (default P, system row 0, 0x40bf70)
		Map( Key.Up, UiKeys.Up );
		Map( Key.Down, UiKeys.Down );
		Map( Key.Left, UiKeys.Left );
		Map( Key.Right, UiKeys.Right );
		Map( Key.Enter, UiKeys.Accept );
		Map( Key.KeypadEnter, UiKeys.Accept );
		Map( Key.Space, UiKeys.Accept | UiKeys.Space );
		Map( Key.Tab, UiKeys.Tab );
		Map( Key.Escape, UiKeys.Back );
		// The original key table reserves Ctrl+P for the e-mail postcard (InputButton.SendEmailPostcard);
		// pause is the plain P, so a held modifier keeps the pause key out of the combination's way.
		if ( !keys.Contains( Key.ControlLeft ) && !keys.Contains( Key.ControlRight ) )
			Map( Input.ChordKey( InputButton.Pause ), UiKeys.Pause );
		previousKeys = keys;
		var window = mouse.Position;
		var notClick = false;
		if ( mouse.Left && !wasLeft )
		{
			pressedAt = Clock();
			pressedPosition = window;
			pressMoved = false;
			pressFrames = 0;
		}
		if ( mouse.Left )
			pressFrames++;
		if ( (mouse.Left || wasLeft) && (Math.Abs( window.X - pressedPosition.X ) > ClickSlop || Math.Abs( window.Y - pressedPosition.Y ) > ClickSlop) )
			pressMoved = true;
		if ( !mouse.Left && wasLeft )
			notClick = IsNotClick( pressedAt, Clock(), pressMoved, pressFrames );
		if ( mouse.Right && !wasRight )
		{
			rightPressedAt = Clock();
			rightPressedPosition = window;
			rightMoved = false;
			rightFrames = 0;
		}
		if ( mouse.Right )
			rightFrames++;
		if ( (mouse.Right || wasRight) && (Math.Abs( window.X - rightPressedPosition.X ) > RightClickSlop || Math.Abs( window.Y - rightPressedPosition.Y ) > RightClickSlop) )
			rightMoved = true;
		var rightReleased = !mouse.Right && wasRight;
		var rightClicked = rightReleased && !rightMoved && (Clock() - rightPressedAt <= RightClickMilliseconds || rightFrames <= MaxQuickClickFrames);
		var input = new UiInput( new NVector2( mouse.Position.X * framebufferPerWindowX, mouse.Position.Y * framebufferPerWindowY ),
			mouse.Left, mouse.Left && !wasLeft, !mouse.Left && wasLeft, mouse.Right && !wasRight, pressed, mouse.Wheel,
			Input.Typed, Input.KeyPresses.Count( key => key == Key.BackSpace ), notClick,
			NormalizeOrderedEvents( Input.OrderedEvents, keys, framebufferPerWindowX, framebufferPerWindowY ),
			PointerScaleX: framebufferPerWindowX, PointerScaleY: framebufferPerWindowY, OrderedUnavailable: Input.OrderedEvents == null, BackspaceRepeating: Input.BackspaceRepeating, RightReleased: rightReleased, RightClicked: rightClicked );
		wasLeft = mouse.Left;
		wasRight = mouse.Right;
		return input;
	}

	/// <summary>Reconstruct event-local modifiers from this batch and its final physical key snapshot.
	/// Do not seed from a prior frame: focus changes and missing raw batches may invalidate that state.</summary>
	internal static IReadOnlyList<OrderedInputEvent>? NormalizeOrderedEvents( IReadOnlyList<OrderedInputEvent>? batch, HashSet<Key> finalKeys, float scaleX, float scaleY )
	{
		if ( batch == null ) return null;
		var held = new HashSet<Key>( finalKeys );
		for ( var i = batch.Count - 1; i >= 0; i-- )
		{
			var item = batch[i];
			if ( item.Kind == OrderedInputEventKind.KeyDown && !item.Repeat ) held.Remove( (Key)item.Key );
			else if ( item.Kind == OrderedInputEventKind.KeyUp ) held.Add( (Key)item.Key );
		}
		var normalized = new OrderedInputEvent[batch.Count];
		int? observedModifiers = null;
		for ( var i = 0; i < batch.Count; i++ )
		{
			var item = batch[i];
			if ( item.Kind == OrderedInputEventKind.KeyDown ) held.Add( (Key)item.Key );
			else if ( item.Kind == OrderedInputEventKind.KeyUp ) held.Remove( (Key)item.Key );
			var modifiers = (held.Contains( Key.ShiftLeft ) || held.Contains( Key.ShiftRight ) ? ModifierKeys.Shift : 0)
				| (held.Contains( Key.ControlLeft ) || held.Contains( Key.ControlRight ) ? ModifierKeys.Control : 0)
				| (held.Contains( Key.AltLeft ) || held.Contains( Key.AltRight ) ? ModifierKeys.Alt : 0);
			// A real key carries an authoritative mask even when a modifier's original down was
			// outside this window. It also synchronizes later pointer masks in this pump batch.
			if ( item.Source != OrderedInputEventSource.Synthetic && item.Kind is OrderedInputEventKind.KeyDown or OrderedInputEventKind.KeyUp )
				observedModifiers = item.Modifiers;
			var eventModifiers = observedModifiers ?? (int)modifiers;
			normalized[i] = item with { X = item.X * scaleX, Y = item.Y * scaleY, Modifiers = eventModifiers,
				ModifiersAreExact = observedModifiers.HasValue };
		}
		return normalized;
	}

	/// <summary>Whether a release <paramref name="releasedAt"/> after a press at <paramref name="pressedAt"/> (milliseconds), with the press marked moved or not, is no click.</summary>
	/// <remarks>The input layer has no event timestamps, so the time is measured per frame; a press held for at most <see cref="MaxQuickClickFrames"/> frames (press and release in consecutive frames) always counts, so one long frame (a browser hitch) cannot drop a normal click.</remarks>
	public static bool IsNotClick( long pressedAt, long releasedAt, bool moved, int heldFrames = 0 ) => moved || (releasedAt - pressedAt > ClickMilliseconds && heldFrames > MaxQuickClickFrames);

	/// <summary>Frames a press may last and still be a click whatever the clock says.</summary>
	public const int MaxQuickClickFrames = 2;
}
