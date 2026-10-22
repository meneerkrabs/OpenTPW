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
/// <param name="BackspaceRepeating">Every Backspace of this frame is the auto-repeat of a held key (the pool bridge and water removal acts only on the first press).</param>
/// <param name="NotClick">
/// Set on a left release that is not a click: the press lasted longer than <see cref="UiInputSource.ClickMilliseconds"/> or the
/// pointer strayed more than <see cref="UiInputSource.ClickSlop"/> pixels on an axis (tp.exe 0x65f6d1 / 0x65fa33). The release still ends drags.
/// </param>
public readonly record struct UiInput( NVector2 Mouse, bool LeftDown, bool LeftPressed, bool LeftReleased, bool RightPressed, UiKeys Pressed, float Wheel = 0, string Text = "", int Backspaces = 0, bool NotClick = false, bool BackspaceRepeating = false )
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

	private bool wasLeft;
	private bool wasRight;
	private long pressedAt;
	private NVector2 pressedPosition;
	private bool pressMoved;
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
		var input = new UiInput( new NVector2( mouse.Position.X * framebufferPerWindowX, mouse.Position.Y * framebufferPerWindowY ),
			mouse.Left, mouse.Left && !wasLeft, !mouse.Left && wasLeft, mouse.Right && !wasRight, pressed, mouse.Wheel,
			Input.Typed, Input.KeyPresses.Count( key => key == Key.BackSpace ), notClick, Input.BackspaceRepeating );
		wasLeft = mouse.Left;
		wasRight = mouse.Right;
		return input;
	}

	/// <summary>Whether a release <paramref name="releasedAt"/> after a press at <paramref name="pressedAt"/> (milliseconds), with the press marked moved or not, is no click.</summary>
	/// <remarks>The input layer has no event timestamps, so the time is measured per frame; a press held for at most <see cref="MaxQuickClickFrames"/> frames (press and release in consecutive frames) always counts, so one long frame (a browser hitch) cannot drop a normal click.</remarks>
	public static bool IsNotClick( long pressedAt, long releasedAt, bool moved, int heldFrames = 0 ) => moved || (releasedAt - pressedAt > ClickMilliseconds && heldFrames > MaxQuickClickFrames);

	/// <summary>Frames a press may last and still be a click whatever the clock says.</summary>
	public const int MaxQuickClickFrames = 2;
}
