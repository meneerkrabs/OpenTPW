using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>
/// A screen of elements with mouse and keyboard navigation: hovering focuses an element, a left
/// click (press and release on the same element) activates it, the wheel or arrows on an option
/// row change its value, Up/Down move the focus, Enter activates, Escape goes back.
/// </summary>
public class UiScreen
{
	private readonly List<UiElement> elements = new();
	private readonly Dictionary<UiElement, int> memberships = new();
	internal int ElementGeneration( UiElement? element ) => element != null && memberships.TryGetValue( element, out var generation ) ? generation : 0;
	private int membershipCounter;
	/// <summary>Every addition takes a fresh, screen-wide generation, so an element removed and added again is a new pointer recipient.</summary>
	private void AddMembership( UiElement element ) => memberships[element] = ++membershipCounter;
	private UiElement? pressed;
	private NVector2 pressPoint;
	private bool capturedClick;
	private NVector2 lastMouse = new( -1, -1 );
	private bool seenMouse;

	public UiScreen( string name ) => Name = name;

	public string Name { get; }
	public IReadOnlyList<UiElement> Elements => elements;
	public UiElement? Focused { get; private set; }
	/// <summary>Called for Escape / right click; null means Escape does nothing.</summary>
	public Action? Back { get; set; }
	/// <summary>Extra drawing before the elements (backdrops, 3D-like decorations).</summary>
	public Action<UiContext>? DrawBackground { get; set; }
	/// <summary>Extra drawing after the elements.</summary>
	public Action<UiContext>? DrawOverlay { get; set; }
	/// <summary>Called every frame before input is handled.</summary>
	public Action<UiContext>? Updating { get; set; }
	/// <summary>Releases screen-owned pending work after removal from its stack.</summary>
	public Action? Removed { get; set; }
	/// <summary>Whether a modal screen dims and blocks what is below it.</summary>
	public bool Modal { get; init; } = true;
	/// <summary>A full screen of its own (the online globe): the screens below are not drawn or dimmed.</summary>
	public bool HidesBelow { get; init; }
	/// <summary>True once a stack removed this screen; it then ignores the rest of the frame's input (a click that closed it must not also act as Back on whatever it opened).</summary>
	public bool IsRemoved
	{
		get => isRemoved;
		internal set
		{
			if ( isRemoved != value ) PointerGeneration++;
			isRemoved = value;
			// Retain the opt-in marker until release/new press, so cancelling capture cannot fall through to another widget.
			if ( value ) CancelCapturedPress();
		}
	}
	private bool isRemoved;
	internal int PointerGeneration { get; private set; }
	internal UiElement? PointerDragCapture => pressed is IUiDragTarget ? pressed : null;

	/// <summary>Whether the right click leaves a modal screen: the RMBCancelOn option (+0x39, default on) is set.</summary>
	// [BIN:TPW-EXE:0x488aa1 RMBCancelOn] the original's screens (camcorder 0x488aa1, ride view 0x48a7ab, 0x48aadb) leave on a right click only when the option [0x78d911] is set; the option's other reader is the park view's mouse procedure 0x4881a0 (a quick right click cancels the tool and mode there)
	// [EXT:ui-modal-right-click-back] OpenTPW's other windows read the RMBCancelOn option as well; the original's modal windows and object window do not read it (docs/UI.md)
	public static bool RightCancels( UiInput input ) => input.RightPressed && GameOptions.Current.RmbCancel;

	public T Add<T>( T element ) where T : UiElement
	{
		elements.Add( element );
		AddMembership( element );
		return element;
	}

	/// <summary>Adds <paramref name="element"/> before <paramref name="before"/>, so it draws under it and loses hit tests to it (at the end when it is absent).</summary>
	public T InsertBefore<T>( UiElement? before, T element ) where T : UiElement
	{
		var index = before == null ? -1 : elements.IndexOf( before );
		if ( index < 0 ) elements.Add( element ); else elements.Insert( index, element );
		AddMembership( element );
		return element;
	}

	public void Remove( UiElement element )
	{
		// A removed element has no generation (0), so tokens issued for it no longer match and the map cannot grow without bound.
		if ( elements.Remove( element ) ) memberships.Remove( element );
		if ( pressed == element ) CancelCapturedPress();
		if ( Focused == element )
			Focused = null;
	}

	private void CancelCapturedPress()
	{
		if ( !capturedClick ) return;
		(pressed as IUiCapturedClickTarget)?.CancelCapturedClick();
		pressed = null;
	}

	/// <summary>Host view transition: cancel a gesture without activating or moving its target.</summary>
	internal void CancelPointerGesture()
	{
		(pressed as IUiCapturedClickTarget)?.CancelCapturedClick();
		if ( pressed is UiButton held ) held.HoldState = 0;
		pressed = null;
		capturedClick = false;
	}

	public IEnumerable<UiElement> FocusableElements => elements.Where( element => element.Visible && element.Focusable );

	public UiElement? Find( string id ) => elements.FirstOrDefault( element => element.Id == id );

	public void Focus( UiElement? element ) => Focused = element;

	/// <summary>Topmost visible focusable element under <paramref name="point"/> (framebuffer pixels).</summary>
	public UiElement? HitTest( UiCanvas canvas, NVector2 point ) =>
		elements.LastOrDefault( element => element.Visible && element.Focusable && element.ScreenRect( canvas ).Contains( point ) );

	/// <summary>Whether <paramref name="point"/> lies on any visible element (used to keep clicks off the park).</summary>
	public bool Covers( UiCanvas canvas, NVector2 point ) =>
		Modal || elements.Any( element => element.Visible && element.CoversPoint( canvas, point ) );

	/// <summary>Pure geometry preparation before pointer admission; no timers, callbacks, focus or key dispatch.</summary>
	internal virtual void PreparePointerGeometry( UiContext context ) { }

	/// <summary>Handles one frame of input; returns true when the input was consumed.</summary>
	// [EXT:ui-modal-right-click-back] hover focus follows the pointer, and the right click backs out of a modal screen when the RMBCancelOn option (+0x39, default 1) is set; the option's only direct readers in the original are the park view 0x4881a0, the camcorder 0x488aa1 and the ride view 0x48a7ab/0x48aadb, so this is OpenTPW's uniform back-out
	public virtual bool Update( UiContext context, UiInput input )
	{
		if ( IsRemoved )
			return true;
		Updating?.Invoke( context );
		if ( IsRemoved )
			return true;
		return HandleInput( context, input, null, false, !input.PointerSuppressed, false );
	}

	/// <summary>Dispatches one already-admitted pointer record, without repeating the frame callback or keyboard work.</summary>
	internal virtual bool UpdatePointer( UiContext context, UiInput input, UiElement? recipient ) =>
		IsRemoved ? true : HandleInput( context, input, recipient, true, true, true );

	private bool HandleInput( UiContext context, UiInput input, UiElement? recipient, bool routed, bool pointerInput, bool pointerOnly )
	{
		var canvas = context.Canvas;
		var hit = routed ? recipient : HitTest( canvas, input.Mouse );
		var consumed = false;
		var rightHandled = false;
		if ( pointerInput )
		{
			if ( capturedClick && (pressed is { Enabled: false } or { Visible: false } || !input.LeftDown && !input.LeftPressed && !input.LeftReleased) )
				CancelCapturedPress();
			context.HoverHelp = hit?.CurrentHelp;
			// A text field keeps the keyboard focus while the mouse merely passes over other elements.
			var typing = Focused is UiTextField { Visible: true, Enabled: true };
			// a field over the park view (the chat entry) takes the keys only when clicked, and the pointer resting on it leaves nothing else focused, so Enter there
			// does not press the button the pointer crossed before
			if ( hit != null && (input.LeftPressed || input.LeftReleased || hit != Focused && !typing && hit is not UiTextField { OverView: true }) && input.Mouse.X >= 0 )
				Focused = hit;
			else if ( hit is UiTextField { OverView: true } && !typing && input.Mouse.X >= 0 )
				Focused = null;
			if ( input.LeftPressed && hit == null && typing && input.Mouse.X >= 0 )
				Focused = null;
			// [BIN:TPW-EXE:0x6656a0 list hover] a list with attribute 0x80 selects the row under the pointer when the pointer moves; the list decides what a hover does: the buy, loans and hire lists select, the others only highlight (the Load Park list does not, EXT:load-list-select-then-load)
			// [BIN:TPW-EXE:0x658840 pointer move] the original sends the pointer messages (0x10001/0x10003, 0x6588ef) only when the pointer position differs from the last one sent, so a resting pointer selects nothing while keys or the wheel scroll the rows under it; the first frame of a screen has no earlier position and counts as no move
			var pointerMoved = seenMouse && input.Mouse.X >= 0 && input.Mouse != lastMouse;
			foreach ( var element in elements )
			{
				if ( element is IUiRowList { HoverSelects: true } hoverList )
					hoverList.Hover( ReferenceEquals( element, hit ) && input.Mouse.X >= 0 ? hoverList.RowAt( canvas, input.Mouse ) : -1, pointerMoved );
			}
			lastMouse = input.Mouse;
			seenMouse = true;
			consumed = hit != null;
			if ( input.LeftPressed )
			{
				pressed = hit;
				capturedClick = pressed is IUiCapturedClickTarget;
				pressPoint = input.Mouse;
				if ( pressed is { Enabled: true } and IUiDragStart start && input.Mouse.X >= 0 )
					start.DragStart( canvas, input.Mouse );
				if ( hit is UiButton { Repeat: not null, Enabled: true } holdButton && input.Mouse.X >= 0 )
					(holdButton.HoldState, holdButton.HoldTime) = (1, context.Time);
			}
			if ( pressed is UiButton { Repeat: { } repeat, HoldState: not 0 } held && input.LeftDown )
				RunHold( held, repeat, context.Time, ReferenceEquals( hit, held ) );
			if ( input.LeftPressed && pressed is { Enabled: true } and IUiDragTarget begin && input.Mouse.X >= 0 )
				begin.DragBegin( canvas, input.Mouse );
			// A slider follows the mouse while it is held, wherever the pointer has drifted to.
			if ( pressed is { Enabled: true } and IUiDragTarget drag && input.Mouse.X >= 0 && (input.LeftDown || input.LeftPressed || input.LeftReleased) )
				drag.DragTo( canvas, input.Mouse );
			if ( input.LeftReleased )
			{
				if ( pressed is { Enabled: true } and IUiDragTarget ended && (input.Mouse.X >= 0 || capturedClick) )
					ended.DragEnd( canvas, input.Mouse );
				// [BIN:TPW-EXE:0x65f6d1 click rule] a release is a click only within 500 ms of its press and without the pointer straying more than 6 px (UiInputSource)
				// a button whose hold began repeating gives no click when it is let go (0x668eff: the click needs the state not to be 2)
				var repeating = pressed is UiButton { HoldState: 2 };
				// [BIN:TPW-EXE:0x658b84 captured release] release selects the original recipient; 0x65f971 rebuilds the qualified click from its saved press point.
				// Opt-in only: other widgets retain their existing same-element release behavior and the shared UiInput NotClick qualification.
				if ( capturedClick )
				{
					if ( !IsRemoved && pressed is { Enabled: true, Visible: true } and IUiCapturedClickTarget captured && elements.Contains( pressed ) && input.Mouse.X >= 0 && !input.NotClick && !repeating )
						captured.ClickCaptured( canvas, pressPoint );
					else
						CancelCapturedPress();
				}
				else if ( hit != null && (pressed == null || pressed == hit) && !input.NotClick && !repeating )
					Click( canvas, hit, input.Mouse );
				if ( pressed is UiButton { Repeat: not null } released )
					released.HoldState = 0;
				pressed = null;
				capturedClick = false;
				if ( IsRemoved )
					return true;
			}
			// [BIN:TPW-EXE:0x66563d list click] a click of the other button selects the row and posts 0x402 to the list's owner; the list decides what that does and whether the right click also backs out
			if ( input.RightPressed && hit is IUiRowList rightList && input.Mouse.X >= 0 && rightList.RowAt( canvas, input.Mouse ) is >= 0 and var rightRow )
				rightHandled = rightList.RowClicked( rightRow, false );
			if ( IsRemoved )
				return true;
		}
		if ( (hit is UiOptionRow or UiSlider || hit is UiButton { Adjusted: not null, WheelAdjusts: true }) && input.Wheel != 0 )
			hit.Adjust( input.Wheel > 0 ? 1 : -1 );
		if ( hit is IUiRowList wheelList && input.Wheel != 0 )
			wheelList.Scroll( input.Wheel > 0 ? -1 : 1 );
		if ( pointerOnly )
		{
			if ( RightCancels( input ) && Modal && !rightHandled ) Back?.Invoke();
			return consumed;
		}
		if ( input.Has( UiKeys.Tab ) )
			MoveFocus( 1, element => element is UiTextField );
		if ( Focused is UiTextField field && field.Visible && field.Enabled )
		{
			Input.TextEntryActive = true;
			field.Type( input.Text, input.Backspaces );
			if ( input.Has( UiKeys.Accept ) && !input.Has( UiKeys.Space ) )
				field.Activate();
			if ( IsRemoved )
				return true;
			// a right click a list handled (a row's popup) is no back-out, as below
			if ( input.Has( UiKeys.Back ) || (RightCancels( input ) && Modal && !rightHandled) )
				Back?.Invoke();
			// Typed keys belong to the field, not to navigation or to the park below.
			return true;
		}
		// A focused list that takes the arrow keys moves its selection instead of the focus.
		if ( input.Has( UiKeys.Down ) )
		{
			if ( Focused is IUiRowList { ArrowsSelect: true } rows ) rows.Select( rows.Selected + 1 ); else MoveFocus( 1 );
		}
		if ( input.Has( UiKeys.Up ) )
		{
			if ( Focused is IUiRowList { ArrowsSelect: true } rows ) rows.Select( rows.Selected - 1 ); else MoveFocus( -1 );
		}
		if ( Focused != null && Focused.Visible && Focused.Focusable )
		{
			if ( input.Has( UiKeys.Left ) )
				Focused.Adjust( -1 );
			if ( input.Has( UiKeys.Right ) )
				Focused.Adjust( 1 );
			// Enter activates, as for a text field: Space belongs to the park's map key
			// (the original key table; ParkHud.ScreenShortcuts), not to the hovered element.
			// [EXT:ui-key-navigation] the original has no key that activates a focused control (only the eight dialog Enter/Escape paths of docs/AUDIO.md, "List owners"), so a focused button's click on Enter (UiButton.Activate) is OpenTPW's; a focused list row stays silent
			if ( input.Has( UiKeys.Accept ) && !input.Has( UiKeys.Space ) )
				Focused.Activate();
		}
		if ( IsRemoved )
			return true;
		if ( input.Has( UiKeys.Back ) || (RightCancels( input ) && Modal && !rightHandled) )
		{
			Back?.Invoke();
			consumed = true;
		}
		return consumed || input.Pressed != UiKeys.None && Modal;
	}

	/// <summary>One frame of a held repeating button (<see cref="UiButtonRepeat"/>): <paramref name="inside"/> is the pointer being over it, the original's pressed bit.</summary>
	internal static void RunHold( UiButton button, UiButtonRepeat repeat, float now, bool inside )
	{
		if ( button.HoldState == 1 && now - button.HoldTime >= repeat.Delay )
		{
			button.HoldState = 2;
			button.HoldTime = now;
			if ( inside )
				repeat.Start();
		}
		else if ( button.HoldState == 2 && now - button.HoldTime > repeat.Interval )
		{
			if ( inside )
				repeat.Tick();
			button.HoldTime = now;
		}
	}

	private static void Click( UiCanvas canvas, UiElement element, NVector2 point )
	{
		// Clicking a text field only gives it the focus; Enter submits it.
		if ( element is UiTextField )
			return;
		if ( element is IUiRowList list )
		{
			var index = list.RowAt( canvas, point );
			if ( index >= 0 && list.RowClicked( index, true ) )
				return;
			if ( index >= 0 && index == list.Selected )
				element.Activate();
			else if ( index >= 0 )
				list.Select( index );
			return;
		}
		if ( element is IUiDragTarget drag )
		{
			drag.DragTo( canvas, point );
			return;
		}
		if ( element is UiOptionRow row )
		{
			var (left, right) = row.ArrowRects( canvas );
			row.Adjust( left.Inflate( 4 ).Contains( point ) ? -1 : 1 );
			return;
		}
		element.Activate();
	}

	public void MoveFocus( int direction, Func<UiElement, bool>? filter = null )
	{
		// a field over the park view takes the keys only from a click or Enter: the arrows (the camera's keys) and Tab pass it by
		var focusable = FocusableElements.Where( element => element is not UiTextField { OverView: true } && (filter == null || filter( element )) ).ToList();
		if ( focusable.Count == 0 )
			return;
		var index = Focused == null ? (direction > 0 ? -1 : 0) : focusable.IndexOf( Focused );
		Focused = focusable[((index + direction) % focusable.Count + focusable.Count) % focusable.Count];
	}

	public virtual void Draw( UiContext context )
	{
		DrawBackground?.Invoke( context );
		foreach ( var element in elements )
		{
			if ( element.Visible )
				element.Draw( context, element == Focused && element.Focusable, element == pressed );
		}
		DrawOverlay?.Invoke( context );
	}
}

/// <summary>A stack of screens: all draw bottom to top, only the top one receives input.</summary>
public sealed class UiScreenStack
{
	private readonly List<UiScreen> screens = new();

	public IReadOnlyList<UiScreen> Screens => screens;
	public UiScreen? Top => screens.Count == 0 ? null : screens[^1];

	public void Push( UiScreen screen )
	{
		screen.IsRemoved = false;
		screens.Add( screen );
	}

	public void Pop()
	{
		if ( screens.Count > 0 )
		{
			var removed = screens[^1];
			screens.RemoveAt( screens.Count - 1 );
			removed.IsRemoved = true;
			removed.Removed?.Invoke();
		}
	}

	public void Replace( UiScreen screen )
	{
		Pop();
		Push( screen );
	}

	/// <summary>Removes every screen <paramref name="match"/> accepts, top-down: re-opening a details dialog replaces it.</summary>
	public void RemoveWhere( Predicate<UiScreen> match )
	{
		for ( var index = screens.Count - 1; index >= 0; index-- )
		{
			if ( !match( screens[index] ) )
				continue;
			var removed = screens[index];
			screens.RemoveAt( index );
			removed.IsRemoved = true;
			removed.Removed?.Invoke();
		}
	}

	public void Clear()
	{
		var removed = screens.ToArray();
		screens.Clear();
		foreach ( var screen in removed )
		{
			screen.IsRemoved = true;
			screen.Removed?.Invoke();
		}
	}

	public bool Update( UiContext context, UiInput input ) => Top?.Update( context, input ) ?? false;

	public bool Covers( UiCanvas canvas, NVector2 point ) => screens.Any( screen => screen.Covers( canvas, point ) );

	public void Draw( UiContext context ) => Draw( context, showHelp: null );

	/// <summary>Draws with an optional owner-local help override; null follows the context option without changing it.</summary>
	public void Draw( UiContext context, bool? showHelp )
	{
		var first = 0;
		for ( var index = screens.Count - 1; index > 0; index-- )
		{
			if ( screens[index].HidesBelow )
			{
				first = index;
				break;
			}
		}
		for ( var index = first; index < screens.Count; index++ )
		{
			// [DATA:video 10:00, 28:00] the pause overlay and the new-player dialog darken the scene behind them, as every modal screen does here
			if ( screens[index].Modal && index > first )
				context.Batch.AddRectangle( new UiRect( 0, 0, context.Canvas.Width, context.Canvas.Height ), UiColors.Backdrop );
			screens[index].Draw( context );
		}
		if ( (showHelp ?? context.PopupHelp) && !string.IsNullOrEmpty( context.HoverHelp ) )
			DrawHelp( context, context.HoverHelp! );
	}

	/// <summary>
	/// The popup help box: a fixed rectangle at the bottom, 574 to 1474 wide and ending at y 1520 of the virtual canvas, drawn with the
	/// f_helpbg art; its height is the small font's line height scaled to the canvas, plus 10 units. The text is centred and wrapped inside.
	/// </summary>
	// [BIN:TPW-EXE:0x492180 help box] the help control is built with left 0x23e, right 0x5c2, bottom 0x5f0 and top 0x5f0 - h, h = (fontHeight(font 7) * 3 * 1024) / [0xfaa5c0] + 10; its art is f_helpbg and its text font slot 7, colour (255,255,255,255)
	// [DATA:video 3:00, 3:20 help box] the box measures 569..1475 by 1436..1515 for one line and for two, grass shows through it at about 67 % black (the art's own alpha)
	// [BIN:TPW-EXE:0x492180 help height] the divisor [0xfaa5c0] is taken as the screen height in pixels and font slot 7 as the small UI font (medium confidence); a text that wraps past the box overflows it on both sides
	internal static UiRect HelpBoxRect( UiCanvas canvas, float lineHeightPixels )
	{
		var height = lineHeightPixels * 3072f / Math.Max( 1, canvas.Height ) + 10;
		return canvas.Map( new UiRect( 574, 1520 - height, 900, height ), UiAnchor.Bottom );
	}

	private static void DrawHelp( UiContext context, string text )
	{
		var rect = HelpBoxRect( context.Canvas, context.Fonts.Small.LineHeight * context.Canvas.TextScale );
		if ( !context.DrawModel( "f_helpbg", 0, rect ) )
			context.Batch.AddRectangle( rect, UiColors.HelpBox );
		var pad = 4 * context.Canvas.TextScale;
		var inner = rect.Inflate( -pad );
		var height = context.Measure( context.Fonts.Small, text, (int)inner.Width ).Height;
		// a text taller than the box is centred on it, not hung from its top edge
		var area = height > inner.Height ? new UiRect( inner.X, inner.Y - (height - inner.Height) / 2, inner.Width, height ) : inner;
		context.DrawText( context.Fonts.Small, text, area, UiColors.Text, UiAlign.Center, wrap: true, shadow: false );
	}
}
