using Veldrid;

namespace OpenTPW.UI.Original;

/// <summary>
/// A one-line text entry in the original style: the text sits on a model frame (or a plain box),
/// keeps the keyboard focus until another element is clicked, and limits the length like the
/// original fields (16 characters for the online login name and password, docs/reverse/UI-MAP.md).
/// </summary>
public sealed class UiTextField : UiElement
{
	public string Text { get; set; } = "";
	public int MaximumLength { get; set; } = 64;
	/// <summary>Whether the initial text is selected for replacement by the next edit.</summary>
	public bool AllSelected { get; private set; }
	public void SelectAll() => AllSelected = true;
	public bool Password { get; set; }
	/// <summary>Left inset of the typed text in authored units; the original login fields carry their labels inside (data:0x505f4).</summary>
	public float TextInset { get; set; }
	/// <summary>Background model frame; null draws the plain help-box colour.</summary>
	public string? Model { get; set; }
	/// <summary>Whether a field without a model draws the plain box (off where the field lies on art of its own, the chat frame's entry).</summary>
	public bool Background { get; set; } = true;
	public Func<UiFonts, FontAtlas> Font { get; set; } = fonts => fonts.Label;
	/// <summary>
	/// Whether the field lies over the park view (the chat entry): the pointer resting on it takes the keys from nothing (only a click or Enter gives it
	/// them), neither the arrows nor Tab move the focus onto it, and while it has the keys the screen leaves the pointer to the park outside its controls.
	/// </summary>
	public bool OverView { get; init; }
	/// <summary>Text shown, dimmed, while the field is empty (the original's prompt texts); never part of <see cref="Text"/>.</summary>
	public string? Placeholder { get; set; }
	/// <summary>Called with Enter.</summary>
	public Action? Submitted { get; set; }
	/// <summary>Called whenever the text changes.</summary>
	public Action<string>? Changed { get; set; }
	public override bool Focusable => Visible && Enabled;

	/// <summary>
	/// Inserts typed characters, dropping control characters, unpaired surrogates and anything past the limit (a surrogate pair
	/// is inserted whole or not at all); a backspace removes a whole surrogate pair.
	/// </summary>
	public void Type( string typed, int backspaces )
	{
		var text = Text;
		if ( AllSelected && (backspaces > 0 || MaximumLength >= 1 && typed.Any( c => !char.IsControl( c ) && !char.IsSurrogate( c ) )
			|| MaximumLength >= 2 && Enumerable.Range( 0, Math.Max( 0, typed.Length - 1 ) ).Any( i => char.IsSurrogatePair( typed, i ) )) )
		{
			text = "";
			AllSelected = false;
			backspaces = 0;
		}
		for ( var index = 0; index < backspaces && text.Length > 0; index++ )
			text = text.Length >= 2 && char.IsLowSurrogate( text[^1] ) && char.IsHighSurrogate( text[^2] ) ? text[..^2] : text[..^1];
		for ( var at = 0; at < typed.Length; at++ )
		{
			var c = typed[at];
			if ( char.IsHighSurrogate( c ) )
			{
				if ( at + 1 < typed.Length && char.IsLowSurrogate( typed[at + 1] ) )
				{
					if ( text.Length + 2 <= MaximumLength )
						text += typed.Substring( at, 2 );
					at++;
				}
				continue;
			}
			if ( char.IsLowSurrogate( c ) || char.IsControl( c ) || text.Length >= MaximumLength )
				continue;
			text += c;
		}
		if ( text == Text )
			return;
		Text = text;
		Changed?.Invoke( text );
	}

	public override void Activate() => Submitted?.Invoke();

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		var rect = ScreenRect( context.Canvas );
		// [EXT:ONLINE-UI] text field art and caret are OpenTPW's; the original field template is not decoded
		if ( Model == null ? Background : !context.DrawModel( Model, 0, rect, focused ? new RgbaByte( 255, 255, 220, 255 ) : null ) )
			context.Batch.AddRectangle( rect, focused ? new RgbaByte( 40, 50, 110, 220 ) : UiColors.HelpBackground );
		var prompt = Text.Length == 0 && Placeholder is { Length: > 0 };
		var shown = prompt ? Placeholder! : Password ? new string( '*', Text.Length ) : Text;
		// The caret blinks twice a second while the field has the focus.
		if ( focused && Enabled && (int)(context.Time * 2) % 2 == 0 )
			shown += "_";
		var scale = rect.Height / Bounds.Height;
		var inset = MathF.Max( rect.Height * 0.25f, TextInset * scale );
		var textRect = new UiRect( rect.X + inset, rect.Y, rect.Width - inset - rect.Height * 0.25f, rect.Height );
		// The selection wash/caret use the host text-field style, not original hilight-art pixel parity.
		if ( focused && AllSelected && Text.Length > 0 )
			context.Batch.AddRectangle( textRect, new RgbaByte( 60, 90, 200, 170 ) );
		context.DrawText( Font( context.Fonts ), shown, textRect, Enabled && !prompt ? UiColors.Value : UiColors.Disabled );
	}
}

/// <summary>A list of selectable rows driven by the screen (click selects, second click activates, wheel scrolls).</summary>
public interface IUiRowList
{
	int Selected { get; }
	/// <summary>Whether Up/Down select rows (otherwise they move the focus).</summary>
	bool ArrowsSelect { get; }
	int RowAt( UiCanvas canvas, System.Numerics.Vector2 point );
	void Select( int index );
	void Scroll( int rows );
	/// <summary>Whether moving the pointer over a row selects it (list attribute 0x80, tp.exe 0x6656a0).</summary>
	bool HoverSelects => false;
	/// <summary>The row under the pointer (-1: none) for lists that highlight on hover. Hovering never changes the selection, so a button beside the list cannot retarget while the pointer travels to it.</summary>
	void Hover( int index ) { }
	/// <summary>As <see cref="Hover(int)"/>, with whether the pointer moved since the last frame; a list that selects on hover selects only on a move (the original's per-move message), not under a resting pointer.</summary>
	void Hover( int index, bool pointerMoved ) => Hover( index );
	/// <summary>
	/// A completed click on row <paramref name="index"/> (<paramref name="left"/>: the left button, otherwise the right one); true when the list
	/// handled it. False leaves the screen's default: a click selects, a second click on the selected row activates, a right click backs out.
	/// </summary>
	bool RowClicked( int index, bool left ) => false;
}

/// <summary>An element that follows the mouse while it is held (slider ball, scroll bar).</summary>
public interface IUiDragTarget
{
	void DragTo( UiCanvas canvas, System.Numerics.Vector2 point );
	/// <summary>The left button went down on this element (before the first <see cref="DragTo"/> of the press).</summary>
	void DragBegin( UiCanvas canvas, System.Numerics.Vector2 point ) { }
	/// <summary>The left button came up after a press that began on this element (called once, after the last <see cref="DragTo"/>).</summary>
	void DragEnd( UiCanvas canvas, System.Numerics.Vector2 point ) { }
}

/// <summary>
/// Opts into a qualified completed click delivered to the original press recipient and point, even if the pointer has left it.
/// The screen calls this after DragEnd instead of its default click/activation path. Disabled, hidden, removed and NotClick targets are excluded.
/// </summary>
public interface IUiCapturedClickTarget
{
	void ClickCaptured( UiCanvas canvas, System.Numerics.Vector2 pressPoint );
	/// <summary>The host lost, removed, hid or disabled this press before its qualified click.</summary>
	void CancelCapturedClick() { }
}

/// <summary>A drag target that wants to know where the press landed, before the first <see cref="IUiDragTarget.DragTo"/> of that press.</summary>
public interface IUiDragStart
{
	void DragStart( UiCanvas canvas, System.Numerics.Vector2 point );
}

/// <summary>
/// A list of text rows inside an original list frame (list_findprks, list_outbox, list_msgs …):
/// click or Up/Down selects, Enter or a second click activates, the wheel scrolls.
/// </summary>
public sealed class UiScrollList : UiElement, IUiRowList
{
	private int first;
	private bool following = true;

	public Func<IReadOnlyList<string>> Rows { get; set; } = () => Array.Empty<string>();
	public string? Model { get; set; }
	/// <summary>Row height in authored canvas units.</summary>
	public float RowHeight { get; set; } = 44;
	/// <summary>Inner margin between the frame art and the rows, in authored units.</summary>
	public float Margin { get; set; } = 24;
	/// <summary>
	/// Column headers of a multi-column list, drawn above the rows: each entry is (offset from the list's left
	/// edge in authored units, header text). Rows then split on tabs and each piece lands at its column's offset.
	/// </summary>
	public IReadOnlyList<(float Offset, Func<string> Text)>? Columns { get; set; }
	/// <summary>
	/// A right-aligned second cell per row (the original's load list row has two cells, the park name left and the save date right,
	/// 0x49f170); an empty text draws nothing. The first cell is narrowed by it.
	/// </summary>
	public Func<int, string>? Trailing { get; set; }
	public int Selected { get; set; } = -1;
	public Action<int>? SelectionChanged { get; set; }
	public Action<int>? RowActivated { get; set; }
	/// <summary>
	/// Whether an activation plays the control click before <see cref="RowActivated"/>. Off by default: no original list row clicks (see <see cref="Activate"/>);
	/// only OpenTPW's own screens without an original counterpart opt in.
	/// </summary>
	public bool ClicksOnActivate { get; set; }
	/// <summary>Whether new rows keep the view at the bottom (chat).</summary>
	public bool FollowEnd { get; set; }
	/// <summary>Whether the list draws its own background (off when an original frame model is drawn behind it).</summary>
	public bool Background { get; set; } = true;
	/// <summary>Whether the list draws its thin position bar (off when an original scroller stands beside it).</summary>
	public bool PositionBar { get; set; } = true;
	/// <summary>The first visible row.</summary>
	public int FirstRow => Math.Clamp( first, 0, Math.Max( 0, Rows().Count - VisibleRows ) );
	public override bool Focusable => Visible && Enabled;
	/// <summary>Up/Down keep moving the focus between elements; Left/Right change the selection.</summary>
	public bool ArrowsSelect => false;

	public int VisibleRows => Math.Max( 1, (int)((Bounds.Height - Margin * 2) / RowHeight) );

	public void Scroll( int rows )
	{
		var count = Rows().Count;
		first = Math.Clamp( first + rows, 0, Math.Max( 0, count - VisibleRows ) );
		following = first >= count - VisibleRows;
	}

	public void Select( int index )
	{
		var count = Rows().Count;
		if ( count == 0 )
			return;
		index = Math.Clamp( index, 0, count - 1 );
		if ( index < first )
			first = index;
		else if ( index >= first + VisibleRows )
			first = index - VisibleRows + 1;
		if ( index == Selected )
			return;
		Selected = index;
		SelectionChanged?.Invoke( index );
	}

	public override void Adjust( int direction ) => Select( Selected < 0 ? 0 : Selected + direction );

	public override void Activate()
	{
		if ( Selected < 0 || Selected >= Rows().Count || RowActivated == null )
			return;
		// [BIN:TPW-EXE:0x665c35 type-7 procedure] row helpers notify the owner (activation 0x400, selection 0x401, other button 0x402), never the hook's button message 0x100, and the hook 0x485780 plays 0x1F/0xBD only for 0x100 (0x4857cb). The type-7 constructor 0x662562 is reached only through the factory 0x65dd7b from layout records (0x65fe40; the text-layout path 0x6607ca has no caller), and the 19 type-7 lists of the 18 layout tables belong to owners whose row handlers send no 0x100: the only 0x100 senders in tp.exe are the button release 0x6691eb and eight window keyboard paths (docs/AUDIO.md, "List owners"). So no original list row clicks; OpenTPW's own screens opt in.
		if ( ClicksOnActivate )
			GameAudio.PlayUi( GameAudio.ClickEvent );
		RowActivated( Selected );
	}

	/// <summary>Row index under <paramref name="point"/> (framebuffer pixels), or -1.</summary>
	public int RowAt( UiCanvas canvas, System.Numerics.Vector2 point )
	{
		var rect = ScreenRect( canvas );
		var scale = rect.Height / Bounds.Height;
		var top = rect.Y + Margin * scale;
		var index = (int)MathF.Floor( (point.Y - top) / (RowHeight * scale) );
		return index < 0 || index >= VisibleRows || first + index >= Rows().Count ? -1 : first + index;
	}

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		var rect = ScreenRect( context.Canvas );
		if ( Background && (Model == null || !context.DrawModel( Model, 0, rect )) )
			context.Batch.AddRectangle( rect, UiColors.HelpBackground );
		var rows = Rows();
		if ( FollowEnd && following && Selected < 0 )
			first = Math.Max( 0, rows.Count - VisibleRows );
		first = Math.Clamp( first, 0, Math.Max( 0, rows.Count - VisibleRows ) );
		var scale = rect.Height / Bounds.Height;
		var margin = Margin * scale;
		var height = RowHeight * scale;
		var top = rect.Y + margin;
		if ( Columns != null )
		{
			foreach ( var column in Columns )
			{
				var width = Bounds.Width - column.Offset - Margin;
				var header = new UiRect( rect.X + column.Offset * scale, top, width * scale, height );
				context.DrawText( context.Fonts.Small, column.Text(), header, UiColors.Value );
			}
			top += height;
		}
		for ( var index = 0; index < VisibleRows && first + index < rows.Count; index++ )
		{
			var row = new UiRect( rect.X + margin, top + index * height, rect.Width - margin * 2, height );
			var selected = first + index == Selected;
			if ( selected )
				context.Batch.AddRectangle( row, new RgbaByte( 60, 90, 200, focused ? (byte)170 : (byte)110 ) );
			var text = rows[first + index];
			if ( Columns == null )
			{
				var trailing = Trailing?.Invoke( first + index ) ?? "";
				if ( trailing.Length > 0 )
				{
					var trailingWidth = context.Measure( context.Fonts.Small, trailing ).Width + 2 * context.Canvas.TextScale;
					context.DrawText( context.Fonts.Small, trailing, row, selected ? UiColors.Highlight : UiColors.Text, UiAlign.Right );
					row = new UiRect( row.X, row.Y, Math.Max( 1, row.Width - trailingWidth ), row.Height );
				}
				context.DrawText( context.Fonts.Small, text, row, selected ? UiColors.Highlight : UiColors.Text );
			}
			else
			{
				// A tab-separated row: each piece is drawn at its column's offset.
				var pieces = text.Split( '\t' );
				for ( var column = 0; column < Columns.Count && column < pieces.Length; column++ )
				{
					var cell = new UiRect( rect.X + Columns[column].Offset * scale, top + index * height,
						(Bounds.Width - Columns[column].Offset - Margin) * scale, height );
					context.DrawText( context.Fonts.Small, pieces[column], cell, selected ? UiColors.Highlight : UiColors.Text );
				}
			}
		}
		// A thin position bar when not all rows fit.
		if ( PositionBar && rows.Count > VisibleRows )
		{
			var track = new UiRect( rect.Right - margin * 0.6f, rect.Y + margin, margin * 0.3f, rect.Height - margin * 2 );
			var thumb = track.Height * VisibleRows / rows.Count;
			var offset = (track.Height - thumb) * first / Math.Max( 1, rows.Count - VisibleRows );
			context.Batch.AddRectangle( new UiRect( track.X, track.Y + offset, track.Width, thumb ), UiColors.Highlight );
		}
	}
}
