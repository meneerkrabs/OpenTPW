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
	/// <summary>
	/// Raw ordered facts for this frame, independent of the legacy snapshot. Null means unavailable;
	/// an empty batch is authoritative and must not replay snapshot edges (including deferred releases).
	/// </summary>
	public static IReadOnlyList<OrderedInputEvent>? OrderedEvents { get; private set; }

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

	/// <summary>Set each frame by a UI that draws the game's own pointer; the next input update then hides the system pointer.</summary>
	public static bool SoftwareCursorRequested { get; set; }

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

	/// <summary>Keys queued by <see cref="InjectKeyDown"/> for the next button rebuild; drained when applied.</summary>
	private static readonly List<InjectedKeyScope> injectedKeys = new();
	// Snapshot before text entry suppresses mapped buttons; rebuilt every frame, including injected modifiers.
	private static readonly HashSet<Key> frameKeys = new();
	internal static bool IsFrameKeyDown( Key key ) => frameKeys.Contains( key );

	/// <summary>
	/// Unit-test seam: pointer fixtures feed the managed frame state a real SDL pump would have produced (UpdateFrom also shows the cursor and
	/// drains injected keys, so it cannot run in a CPU-only test). Returns the action that puts the previous state back.
	/// </summary>
	internal static Action CaptureFrameStateForTests()
	{
		var typed = Typed;
		var presses = KeyPresses;
		var ordered = OrderedEvents;
		var modifiers = pointerModifierMask;
		var held = frameKeys.ToArray();
		var bindings = Bindings;
		return () =>
		{
			Typed = typed;
			KeyPresses = presses;
			OrderedEvents = ordered;
			pointerModifierMask = modifiers;
			frameKeys.Clear();
			frameKeys.UnionWith( held );
			Bindings = bindings;
		};
	}

	/// <summary>Unit-test seam: an empty frame with private copies of the key bindings, so a fixture cannot change another test's.</summary>
	internal static void ResetFrameStateForTests()
	{
		Typed = "";
		KeyPresses = Array.Empty<Key>();
		OrderedEvents = null;
		pointerModifierMask = null;
		frameKeys.Clear();
		Bindings = Bindings.ToDictionary( pair => pair.Key, pair => (Key[])pair.Value.Clone() );
	}

	/// <summary>Unit-test seam: rebinds one button; call <see cref="ResetFrameStateForTests"/> first so the change stays in the fixture's private copy.</summary>
	internal static void RebindForTests( InputButton button, params Key[] keys ) => Bindings[button] = keys;

	/// <summary>Unit-test seam: the physically held keys and the raw ordered batch of one synthetic frame.</summary>
	internal static void SetFrameForTests( IEnumerable<Key> held, IReadOnlyList<OrderedInputEvent>? ordered )
	{
		frameKeys.Clear();
		frameKeys.UnionWith( held );
		OrderedEvents = ordered;
	}

	/// <summary>Modifier mask of a Shift/Ctrl/Alt state as the original builds it: Shift 3, Ctrl 0xc, Alt 0x30.</summary>
	// [BIN:TPW-EXE:0x486aa0 modifier mask] GetAsyncKeyState of VK_SHIFT (0x10), VK_CONTROL (0x11) and VK_MENU (0x12) gives 3, 0xc and 0x30, or-ed together
	internal static int ModifierMask( bool shift, bool control, bool alt ) => (shift ? 3 : 0) | (control ? 0xc : 0) | (alt ? 0x30 : 0);

	/// <summary>The modifier mask of the keys held this frame (either side of each modifier counts).</summary>
	internal static int ModifierMaskDown => pointerModifierMask ?? ModifierMask( IsFrameKeyDown( Key.ShiftLeft ) || IsFrameKeyDown( Key.ShiftRight ),
		IsFrameKeyDown( Key.ControlLeft ) || IsFrameKeyDown( Key.ControlRight ), IsFrameKeyDown( Key.AltLeft ) || IsFrameKeyDown( Key.AltRight ) );

	private static int? pointerModifierMask;
	/// <summary>A queued pointer delivery must not read the final frame's modifier state.</summary>
	internal static IDisposable UsePointerModifiers( int modifiers )
	{
		var previous = pointerModifierMask;
		pointerModifierMask = ModifierMask( ((ModifierKeys)modifiers & ModifierKeys.Shift) != 0,
			((ModifierKeys)modifiers & ModifierKeys.Control) != 0, ((ModifierKeys)modifiers & ModifierKeys.Alt) != 0 );
		return new PointerModifierScope( previous );
	}
	private sealed class PointerModifierScope( int? previous ) : IDisposable
	{
		public void Dispose() => pointerModifierMask = previous;
	}

	/// <summary>
	/// Queues a physical key press for the next frame only (the native smoke tests' keyboard steps): the key
	/// reads as held through its <see cref="InputButton"/> bindings for exactly one frame and is released the
	/// next, like a tap. The injection never enters <see cref="Keyboard"/>'s persistent state or the WASD pan
	/// axes, physical SDL input is unchanged, and the text-entry gate applies to it as to real keys. Disposing
	/// the returned scope removes keys not yet consumed, so a frame that never runs cannot leak them.
	/// </summary>
	internal static IDisposable InjectKeyDown( params Key[] keys )
	{
		var scope = new InjectedKeyScope( keys.ToArray() );
		injectedKeys.Add( scope );
		return scope;
	}

	private sealed class InjectedKeyScope( Key[] keys ) : IDisposable
	{
		internal Key[] Keys { get; } = keys;
		public void Dispose()
		{
			// A consumed scope is absent already; never cancel a newer scope for the same key.
			injectedKeys.Remove( this );
		}
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

	public static void UpdateFrom( InputSnapshot inputSnapshot ) =>
		UpdateFromOrderedSnapshot( inputSnapshot, (inputSnapshot as IOrderedInputSnapshot)?.OrderedEvents );

	internal static void UpdateFromOrderedSnapshot( InputSnapshot inputSnapshot, IReadOnlyList<OrderedInputEvent>? orderedEvents )
	{
		// Copy: browser snapshots and desktop collection buffers are reused by the next pump.
		OrderedEvents = orderedEvents?.ToArray();
		IsSystemCursorVisible = !SoftwareCursorRequested;
		SoftwareCursorRequested = false;

		var mousePos = new Vector2( inputSnapshot.MousePosition.X, inputSnapshot.MousePosition.Y );
		var mouseInfo = new MouseInfo
		{
			Delta = Mouse.Position - mousePos,
			Position = mousePos,
			Left = inputSnapshot.IsMouseDown( MouseButton.Left ),
			Right = inputSnapshot.IsMouseDown( MouseButton.Right ),
			Middle = inputSnapshot.IsMouseDown( MouseButton.Middle ),
			Wheel = inputSnapshot.WheelDelta
		};

		Mouse = mouseInfo;
		Typed = new string( inputSnapshot.KeyCharPresses.ToArray() );
		if ( GameLanguage.IsSelected && GameLanguage.Current.IsRightToLeft )
			Typed = HebrewKeyboard.Map( Typed );
		KeyPresses = inputSnapshot.KeyEvents.Where( keyEvent => keyEvent.Down ).Select( keyEvent => keyEvent.Key ).ToArray();

		Right = 0;
		Forward = 0;

		Keyboard = new KeyboardInfo( ReducePhysicalKeys( Keyboard.KeysDown, inputSnapshot.KeyEvents ) );

		bool IsKeyPressed( Key k ) => Keyboard.KeysDown.Contains( k );

		if ( IsKeyPressed( Key.A ) && !TextEntryActive )
			Right -= 1;
		if ( IsKeyPressed( Key.D ) && !TextEntryActive )
			Right += 1;
		if ( IsKeyPressed( Key.W ) && !TextEntryActive )
			Forward += 1;
		if ( IsKeyPressed( Key.S ) && !TextEntryActive )
			Forward -= 1;

		UpdateButtons();
	}

	/// <summary>Apply the compatibility snapshot's ordered edges; Up then Down must finish held.</summary>
	internal static List<Key> ReducePhysicalKeys( IEnumerable<Key> previous, IReadOnlyList<KeyEvent> events )
	{
		var held = previous.ToHashSet();
		foreach ( var item in events )
		{
			if ( item.Down ) held.Add( item.Key );
			else held.Remove( item.Key );
		}
		return held.ToList();
	}

	/// <summary>
	/// The frame's button state: every <see cref="InputButton"/> whose bound keys the physical keyboard holds,
	/// plus the keys <see cref="InjectKeyDown"/> queued for this frame (a tap). Drains the queue, so an
	/// injection holds for exactly one rebuild and releases the next.
	/// </summary>
	internal static void UpdateButtons()
	{
		RefreshKeyboardStrings();
		var injected = injectedKeys.SelectMany( scope => scope.Keys ).ToArray();
		injectedKeys.Clear();
		frameKeys.Clear();
		frameKeys.UnionWith( Keyboard.KeysDown );
		frameKeys.UnionWith( injected );
		bool IsDown( Key k ) => Keyboard.KeysDown.Contains( k ) || injected.Contains( k );

		LastKeysDown = [.. KeysDown];
		KeysDown.Clear();

		foreach ( var (button, vkeys) in Bindings )
		{
			bool isPressed = !TextEntryActive;

			foreach ( var vkey in vkeys )
			{
				// Help accepts either Ctrl key for physical input and injected one-frame taps.
				if ( !IsDown( vkey ) && !(button == InputButton.ToggleHelpBar && vkey == Key.ControlLeft && IsDown( Key.ControlRight )) )
					isPressed = false;
			}

			if ( isPressed )
			{
				KeysDown.Add( button );
			}
		}
	}
}
