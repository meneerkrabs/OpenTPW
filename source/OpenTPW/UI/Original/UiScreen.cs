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
	private UiElement? pressed;
	private NVector2 lastMouse = new( -1, -1 );

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
	public bool IsRemoved { get; internal set; }

	public T Add<T>( T element ) where T : UiElement
	{
		elements.Add( element );
		return element;
	}

	public void Remove( UiElement element )
	{
		elements.Remove( element );
		if ( Focused == element )
			Focused = null;
	}

	public IEnumerable<UiElement> FocusableElements => elements.Where( element => element.Visible && element.Focusable );

	public UiElement? Find( string id ) => elements.FirstOrDefault( element => element.Id == id );

	public void Focus( UiElement? element ) => Focused = element;

	/// <summary>Topmost visible focusable element under <paramref name="point"/> (framebuffer pixels).</summary>
	public UiElement? HitTest( UiCanvas canvas, NVector2 point ) =>
		elements.LastOrDefault( element => element.Visible && element.Focusable && element.ScreenRect( canvas ).Contains( point ) );

	/// <summary>Whether <paramref name="point"/> lies on any visible element (used to keep clicks off the park).</summary>
	public bool Covers( UiCanvas canvas, NVector2 point ) =>
		Modal || elements.Any( element => element.Visible && element.ScreenRect( canvas ).Contains( point ) );

	/// <summary>Handles one frame of input; returns true when the input was consumed.</summary>
	// [APPROX:UI-012] hover focus follows the pointer, and the right click backs out of a modal screen whatever the RMBCancelOn option (+0x39, default 1; readers 0x4881ae, 0x488aa1, 0x48a7ab, 0x48aadb) says — evidence needed: what those readers do
	public virtual bool Update( UiContext context, UiInput input )
	{
		if ( IsRemoved )
			return true;
		Updating?.Invoke( context );
		if ( IsRemoved )
			return true;
		var canvas = context.Canvas;
		var hit = HitTest( canvas, input.Mouse );
		context.HoverHelp = hit?.Help;
		// A text field keeps the keyboard focus while the mouse merely passes over other elements.
		var typing = Focused is UiTextField { Visible: true, Enabled: true };
		if ( hit != null && (input.LeftPressed || input.LeftReleased || hit != Focused && !typing) && input.Mouse.X >= 0 )
			Focused = hit;
		if ( input.LeftPressed && hit == null && typing && input.Mouse.X >= 0 )
			Focused = null;
		// [BIN:TPW-EXE:0x6656a0 list hover] a list with attribute 0x80 selects the row under the pointer; OpenTPW only highlights it, because the buttons beside the list act on the selection
		// The list decides what a hover does: most only highlight (the click selects, so buttons beside the list keep their target), the hire list selects a row when the pointer moves onto it.
		foreach ( var element in elements )
		{
			if ( element is IUiRowList { HoverSelects: true } hoverList )
				hoverList.Hover( ReferenceEquals( element, hit ) && input.Mouse.X >= 0 ? hoverList.RowAt( canvas, input.Mouse ) : -1 );
		}
		lastMouse = input.Mouse;
		var consumed = hit != null;
		if ( input.LeftPressed )
			pressed = hit;
		// A slider follows the mouse while it is held, wherever the pointer has drifted to.
		if ( pressed is { Enabled: true } and IUiDragTarget drag && input.Mouse.X >= 0 && (input.LeftDown || input.LeftPressed || input.LeftReleased) )
			drag.DragTo( canvas, input.Mouse );
		if ( input.LeftReleased )
		{
			// [BIN:TPW-EXE:0x65f6d1 click rule] a release is a click only within 500 ms of its press and without the pointer straying more than 6 px (UiInputSource)
			if ( hit != null && (pressed == null || pressed == hit) && !input.NotClick )
				Click( canvas, hit, input.Mouse );
			pressed = null;
			if ( IsRemoved )
				return true;
		}
		// [BIN:TPW-EXE:0x66563d list click] a click of the other button selects the row and posts 0x402 to the list's owner; the list decides what that does and whether the right click also backs out
		var rightHandled = false;
		if ( input.RightPressed && hit is IUiRowList rightList && input.Mouse.X >= 0 && rightList.RowAt( canvas, input.Mouse ) is >= 0 and var rightRow )
			rightHandled = rightList.RowClicked( rightRow, false );
		if ( IsRemoved )
			return true;
		if ( (hit is UiOptionRow or UiSlider || hit is UiButton { Adjusted: not null, WheelAdjusts: true }) && input.Wheel != 0 )
			hit.Adjust( input.Wheel > 0 ? 1 : -1 );
		if ( hit is IUiRowList wheelList && input.Wheel != 0 )
			wheelList.Scroll( input.Wheel > 0 ? -1 : 1 );
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
			if ( input.Has( UiKeys.Back ) || (input.RightPressed && Modal) )
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
			if ( input.Has( UiKeys.Accept ) && !input.Has( UiKeys.Space ) )
				Focused.Activate();
		}
		if ( IsRemoved )
			return true;
		if ( input.Has( UiKeys.Back ) || (input.RightPressed && Modal && !rightHandled) )
		{
			Back?.Invoke();
			consumed = true;
		}
		return consumed || input.Pressed != UiKeys.None && Modal;
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
		var focusable = FocusableElements.Where( element => filter == null || filter( element ) ).ToList();
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
