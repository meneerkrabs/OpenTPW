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
	public bool Password { get; set; }
	/// <summary>Background model frame; null draws the plain help-box colour.</summary>
	public string? Model { get; set; }
	/// <summary>Called with Enter.</summary>
	public Action? Submitted { get; set; }
	/// <summary>Called whenever the text changes.</summary>
	public Action<string>? Changed { get; set; }
	public override bool Focusable => Visible && Enabled;

	/// <summary>Inserts typed characters, dropping control characters and anything past the limit.</summary>
	public void Type( string typed, int backspaces )
	{
		var text = Text;
		for ( var index = 0; index < backspaces && text.Length > 0; index++ )
			text = text[..^1];
		foreach ( var c in typed )
		{
			if ( char.IsControl( c ) || text.Length >= MaximumLength )
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
		if ( Model == null || !context.DrawModel( Model, 0, rect, focused ? new RgbaByte( 255, 255, 220, 255 ) : null ) )
			context.Batch.AddRectangle( rect, focused ? new RgbaByte( 40, 50, 110, 220 ) : UiColors.HelpBackground );
		var shown = Password ? new string( '*', Text.Length ) : Text;
		// The caret blinks twice a second while the field has the focus.
		if ( focused && Enabled && (int)(context.Time * 2) % 2 == 0 )
			shown += "_";
		var pad = rect.Height * 0.25f;
		context.DrawText( context.Fonts.Label, shown, new UiRect( rect.X + pad, rect.Y, rect.Width - pad * 2, rect.Height ), Enabled ? UiColors.Value : UiColors.Disabled );
	}
}

/// <summary>
/// A list of text rows inside an original list frame (list_findprks, list_outbox, list_msgs …):
/// click or Up/Down selects, Enter or a second click activates, the wheel scrolls.
/// </summary>
public sealed class UiScrollList : UiElement
{
	private int first;

	public Func<IReadOnlyList<string>> Rows { get; set; } = () => Array.Empty<string>();
	public string? Model { get; set; }
	/// <summary>Row height in authored canvas units.</summary>
	public float RowHeight { get; set; } = 44;
	/// <summary>Inner margin between the frame art and the rows, in authored units.</summary>
	public float Margin { get; set; } = 24;
	public int Selected { get; set; } = -1;
	public Action<int>? SelectionChanged { get; set; }
	public Action<int>? RowActivated { get; set; }
	/// <summary>Whether new rows keep the view at the bottom (chat).</summary>
	public bool FollowEnd { get; set; }
	public override bool Focusable => Visible && Enabled;

	public int VisibleRows => Math.Max( 1, (int)((Bounds.Height - Margin * 2) / RowHeight) );

	public void Scroll( int rows )
	{
		var count = Rows().Count;
		first = Math.Clamp( first + rows, 0, Math.Max( 0, count - VisibleRows ) );
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
		if ( Selected >= 0 && Selected < Rows().Count )
			RowActivated?.Invoke( Selected );
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
		if ( Model == null || !context.DrawModel( Model, 0, rect ) )
			context.Batch.AddRectangle( rect, UiColors.HelpBackground );
		var rows = Rows();
		if ( FollowEnd && Selected < 0 )
			first = Math.Max( 0, rows.Count - VisibleRows );
		first = Math.Clamp( first, 0, Math.Max( 0, rows.Count - VisibleRows ) );
		var scale = rect.Height / Bounds.Height;
		var margin = Margin * scale;
		var height = RowHeight * scale;
		for ( var index = 0; index < VisibleRows && first + index < rows.Count; index++ )
		{
			var row = new UiRect( rect.X + margin, rect.Y + margin + index * height, rect.Width - margin * 2, height );
			var selected = first + index == Selected;
			if ( selected )
				context.Batch.AddRectangle( row, new RgbaByte( 60, 90, 200, focused ? (byte)170 : (byte)110 ) );
			context.DrawText( context.Fonts.Small, rows[first + index], row, selected ? UiColors.Highlight : UiColors.Text );
		}
		// A thin position bar when not all rows fit.
		if ( rows.Count > VisibleRows )
		{
			var track = new UiRect( rect.Right - margin * 0.6f, rect.Y + margin, margin * 0.3f, rect.Height - margin * 2 );
			var thumb = track.Height * VisibleRows / rows.Count;
			var offset = (track.Height - thumb) * first / Math.Max( 1, rows.Count - VisibleRows );
			context.Batch.AddRectangle( new UiRect( track.X, track.Y + offset, track.Width, thumb ), UiColors.Highlight );
		}
	}
}
