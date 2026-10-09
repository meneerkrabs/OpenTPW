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
	// [APPROX:UI-012] hover focus, activate on release, keyboard navigation, right click backs out — evidence needed: binary input handling
	public virtual bool Update( UiContext context, UiInput input )
	{
		Updating?.Invoke( context );
		var canvas = context.Canvas;
		var hit = HitTest( canvas, input.Mouse );
		context.HoverHelp = hit?.Help;
		// A text field keeps the keyboard focus while the mouse merely passes over other elements.
		var typing = Focused is UiTextField { Visible: true, Enabled: true };
		if ( hit != null && (input.LeftPressed || input.LeftReleased || hit != Focused && !typing) && input.Mouse.X >= 0 )
			Focused = hit;
		if ( input.LeftPressed && hit == null && typing && input.Mouse.X >= 0 )
			Focused = null;
		var consumed = hit != null;
		if ( input.LeftPressed )
			pressed = hit;
		if ( input.LeftReleased )
		{
			if ( hit != null && (pressed == null || pressed == hit) )
				Click( canvas, hit, input.Mouse );
			pressed = null;
		}
		if ( hit is UiOptionRow wheelRow && input.Wheel != 0 )
			wheelRow.Adjust( input.Wheel > 0 ? 1 : -1 );
		if ( hit is UiScrollList wheelList && input.Wheel != 0 )
			wheelList.Scroll( input.Wheel > 0 ? -1 : 1 );
		if ( input.Has( UiKeys.Tab ) )
			MoveFocus( 1, element => element is UiTextField );
		if ( Focused is UiTextField field && field.Visible && field.Enabled )
		{
			Input.TextEntryActive = true;
			field.Type( input.Text, input.Backspaces );
			if ( input.Has( UiKeys.Accept ) && !input.Has( UiKeys.Space ) )
				field.Activate();
			if ( input.Has( UiKeys.Back ) || (input.RightPressed && Modal) )
				Back?.Invoke();
			// Typed keys belong to the field, not to navigation or to the park below.
			return true;
		}
		if ( input.Has( UiKeys.Down ) )
			MoveFocus( 1 );
		if ( input.Has( UiKeys.Up ) )
			MoveFocus( -1 );
		if ( Focused != null && Focused.Visible && Focused.Focusable )
		{
			if ( input.Has( UiKeys.Left ) )
				Focused.Adjust( -1 );
			if ( input.Has( UiKeys.Right ) )
				Focused.Adjust( 1 );
			if ( input.Has( UiKeys.Accept ) )
				Focused.Activate();
		}
		if ( input.Has( UiKeys.Back ) || (input.RightPressed && Modal) )
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
		if ( element is UiScrollList list )
		{
			var index = list.RowAt( canvas, point );
			if ( index >= 0 && index == list.Selected )
				list.Activate();
			else if ( index >= 0 )
				list.Select( index );
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

	public void Push( UiScreen screen ) => screens.Add( screen );

	public void Pop()
	{
		if ( screens.Count > 0 )
		{
			var removed = screens[^1];
			screens.RemoveAt( screens.Count - 1 );
			removed.Removed?.Invoke();
		}
	}

	public void Replace( UiScreen screen )
	{
		Pop();
		Push( screen );
	}

	public void Clear()
	{
		var removed = screens.ToArray();
		screens.Clear();
		foreach ( var screen in removed ) screen.Removed?.Invoke();
	}

	public bool Update( UiContext context, UiInput input ) => Top?.Update( context, input ) ?? false;

	public bool Covers( UiCanvas canvas, NVector2 point ) => screens.Any( screen => screen.Covers( canvas, point ) );

	public void Draw( UiContext context )
	{
		for ( var index = 0; index < screens.Count; index++ )
		{
			// [APPROX:UI-011] modal screens dim the screens below — evidence needed: captures of original dialogs
			if ( screens[index].Modal && index > 0 )
				context.Batch.AddRectangle( new UiRect( 0, 0, context.Canvas.Width, context.Canvas.Height ), UiColors.Backdrop );
			screens[index].Draw( context );
		}
		if ( context.PopupHelp && !string.IsNullOrEmpty( context.HoverHelp ) )
			DrawHelp( context, context.HoverHelp! );
	}

	/// <summary>Popup help box along the top edge (placement is an OpenTPW approximation; helpbg art).</summary>
	// [APPROX:UI-010] popup help placement/backdrop — evidence needed: capture of original popup help
	private static void DrawHelp( UiContext context, string text )
	{
		var canvas = context.Canvas;
		var maximum = (int)(canvas.Width * 0.6f);
		var (width, height) = context.Measure( context.Fonts.Small, text, maximum );
		var pad = 4 * canvas.TextScale;
		var rect = new UiRect( (canvas.Width - width) / 2f - pad, pad, width + pad * 2, height + pad * 2 );
		context.Batch.AddRectangle( rect, UiColors.HelpBackground );
		context.DrawText( context.Fonts.Small, text, rect.Inflate( -pad ), UiColors.Text, UiAlign.Center, wrap: true, shadow: false );
	}
}
