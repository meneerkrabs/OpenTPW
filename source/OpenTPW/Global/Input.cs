using Veldrid;
using Veldrid.Sdl2;

namespace OpenTPW;

public static partial class Input
{
	public static MouseInfo Mouse { get; internal set; } = new();
	public static KeyboardInfo Keyboard { get; internal set; } = new();
	/// <summary>Characters typed this frame (keyboard layout and IME applied by SDL).</summary>
	public static string Typed { get; private set; } = "";
	/// <summary>Key presses this frame including auto-repeat, in order (for text editing keys).</summary>
	public static IReadOnlyList<Key> KeyPresses { get; private set; } = Array.Empty<Key>();
	/// <summary>Set by the UI while a text field has the focus: the park camera and hotkeys ignore the keyboard.</summary>
	public static bool TextEntryActive { get; set; }

	public static float Forward { get; set; }
	public static float Right { get; set; }

	internal static List<InputButton> LastKeysDown { get; set; } = new();
	internal static List<InputButton> KeysDown { get; set; } = new();

	private static CursorTypes _cursorType;
	public static CursorTypes CursorType
	{
		get => _cursorType;
		set => _cursorType = value;
	}

	public static bool IsSystemCursorVisible
	{
		get => Sdl2Native.SDL_ShowCursor( -1 ) == 1;
		set => Sdl2Native.SDL_ShowCursor( value ? 1 : 0 );
	}

	public static bool Pressed( InputButton button )
	{
		return KeysDown.Contains( button ) && !LastKeysDown.Contains( button );
	}

	public static bool Down( InputButton button )
	{
		return KeysDown.Contains( button );
	}

	public static bool Released( InputButton button )
	{
		return !KeysDown.Contains( button ) && LastKeysDown.Contains( button );
	}

	public struct KeyboardInfo
	{
		public List<Key> KeysDown { get; set; }

		public KeyboardInfo()
		{
			KeysDown = new();
		}

		public KeyboardInfo( List<Key> keysDown )
		{
			KeysDown = keysDown;
		}
	}

	private static Dictionary<InputButton, Key[]> Bindings = new();

	static Input()
	{
		static T? GetAttributeOfType<T>( Enum enumVal ) where T : System.Attribute
		{
			var type = enumVal.GetType();
			var memInfo = type.GetMember( enumVal.ToString() );
			var attributes = memInfo[0].GetCustomAttributes( typeof( T ), false );
			return (attributes.Length > 0) ? (T)attributes[0] : null;
		}

		//
		// Build bindings table based on [DefaultKey] attribute values
		//
		foreach ( var key in Enum.GetValues<InputButton>() )
		{
			var attrib = GetAttributeOfType<DefaultKeyAttribute>( key );

			if ( attrib == null )
				continue;

			Bindings.Add( key, attrib.Keys );
		}
	}

	public static void UpdateFrom( InputSnapshot inputSnapshot )
	{
		IsSystemCursorVisible = true;

		var mousePos = new Vector2( inputSnapshot.MousePosition.X, inputSnapshot.MousePosition.Y );
		var mouseInfo = new MouseInfo
		{
			Delta = Mouse.Position - mousePos,
			Position = mousePos,
			Left = inputSnapshot.IsMouseDown( MouseButton.Left ),
			Right = inputSnapshot.IsMouseDown( MouseButton.Right ),
			Wheel = inputSnapshot.WheelDelta
		};

		Mouse = mouseInfo;
		Typed = new string( inputSnapshot.KeyCharPresses.ToArray() );
		KeyPresses = inputSnapshot.KeyEvents.Where( keyEvent => keyEvent.Down ).Select( keyEvent => keyEvent.Key ).ToArray();

		Right = 0;
		Forward = 0;

		var newKeysDown = inputSnapshot.KeyEvents.Where( x => x.Down ).Select( x => x.Key );
		var newKeysUp = inputSnapshot.KeyEvents.Where( x => !x.Down ).Select( x => x.Key );

		Keyboard = new KeyboardInfo(
			Keyboard.KeysDown.Concat( newKeysDown )
				.Distinct()
				.Where( x => !newKeysUp.Contains( x ) )
				.ToList()
		);

		bool IsKeyPressed( Key k ) => Keyboard.KeysDown.Contains( k );

		if ( IsKeyPressed( Key.A ) && !TextEntryActive )
			Right -= 1;
		if ( IsKeyPressed( Key.D ) && !TextEntryActive )
			Right += 1;
		if ( IsKeyPressed( Key.W ) && !TextEntryActive )
			Forward += 1;
		if ( IsKeyPressed( Key.S ) && !TextEntryActive )
			Forward -= 1;
		
		LastKeysDown = [.. KeysDown];
		KeysDown.Clear();

		foreach ( var (button, vkeys) in Bindings )
		{
			bool isPressed = !TextEntryActive;

			foreach ( var vkey in vkeys )
			{
				if ( !IsKeyPressed( vkey ) )
					isPressed = false;
			}

			if ( isPressed )
			{
				KeysDown.Add( button );
			}
		}
	}
}
