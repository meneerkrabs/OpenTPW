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
	Pause = 64
}

/// <summary>
/// One frame of UI input in framebuffer pixels. Built from <see cref="Input"/> at runtime and
/// constructed directly by tests and the smoke test.
/// </summary>
public readonly record struct UiInput( NVector2 Mouse, bool LeftDown, bool LeftPressed, bool LeftReleased, bool RightPressed, UiKeys Pressed, float Wheel = 0 )
{
	public static UiInput Idle( NVector2 mouse ) => new( mouse, false, false, false, false, UiKeys.None );
	public static UiInput Click( NVector2 mouse ) => new( mouse, false, false, true, false, UiKeys.None );
	public static UiInput Key( UiKeys keys ) => new( new NVector2( -1, -1 ), false, false, false, false, keys );
	public bool Has( UiKeys key ) => (Pressed & key) != 0;
}

/// <summary>Turns the global <see cref="Input"/> state into <see cref="UiInput"/> edges.</summary>
public sealed class UiInputSource
{
	private bool wasLeft;
	private bool wasRight;
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
		Map( Key.Up, UiKeys.Up );
		Map( Key.Down, UiKeys.Down );
		Map( Key.Left, UiKeys.Left );
		Map( Key.Right, UiKeys.Right );
		Map( Key.Enter, UiKeys.Accept );
		Map( Key.KeypadEnter, UiKeys.Accept );
		Map( Key.Space, UiKeys.Accept );
		Map( Key.Escape, UiKeys.Back );
		Map( Key.P, UiKeys.Pause );
		previousKeys = keys;
		var input = new UiInput( new NVector2( mouse.Position.X * framebufferPerWindowX, mouse.Position.Y * framebufferPerWindowY ),
			mouse.Left, mouse.Left && !wasLeft, !mouse.Left && wasLeft, mouse.Right && !wasRight, pressed, mouse.Wheel );
		wasLeft = mouse.Left;
		wasRight = mouse.Right;
		return input;
	}
}
