using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>Builders for the window-style screens shared by the front end and the pause menu.</summary>
public static class UiDialogs
{
	/// <summary>Authored rectangle of a centred window of the given canvas size.</summary>
	// [APPROX:UI-013] dialog window sizes and inner layout — evidence needed: captures of original dialogs
	public static UiRect CenteredWindow( float width, float height ) =>
		UiRect.FromCenter( new NVector2( UiCanvas.VirtualWidth / 2, UiCanvas.VirtualHeight / 2 ), width, height );

	/// <summary>Adds the original window frame (w_med/w_dialog) and a title.</summary>
	public static void AddWindow( UiScreen screen, UiRect rect, string model, Func<string> title )
	{
		screen.Add( new UiModelImage { Id = "window", Model = model, Bounds = rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel
		{
			Id = "title",
			Text = title,
			Font = fonts => fonts.Title,
			Color = UiColors.Title,
			Align = UiAlign.Center,
			Bounds = new UiRect( rect.X + rect.Width * 0.06f, rect.Y + rect.Height * 0.04f, rect.Width * 0.8f, 110 ),
			Anchor = UiAnchor.Center
		} );
	}

	/// <summary>
	/// A message box in the original confirm dialog (table 0x7583e8): the wdialogw window at (500,233)-(1516,830) in the upper centre of the screen, its UITEXT
	/// message (an upper-case caption line, a blank line and the question) at the left, and the round tick and X buttons stacked at the right: the first choice is
	/// the tick, the second the X (their labels are the buttons' tooltips). A message with more than two choices keeps text buttons in a row at the window's foot.
	/// </summary>
	// [BIN:TPW-EXE:0x7583e8 confirm dialog] window id 0x48 (500,233,1516,830) with b_okay (1376,524,1460,608) and b_exit (1387,613,1470,696), a caption box (606,304,1333,384) and two text fields; the second table 0x7585a8 is the same stream; the name dialog 0x753f50 has the same window and buttons
	// [DATA:video 28:00, 28:04, 28:40] the dialogs measure (527..1491, 247..854) in a 954x720 game area, with the tick and X at the right and no text buttons
	// [APPROX:UI-013] the message text's own rectangle (below the caption, left of the buttons) is read off the video, not from the table, whose two text fields belong to the name dialog; the load dialog is not in the capture
	public static UiScreen Message( string name, Func<string> text, params (Func<string> Label, Action Clicked)[] choices )
	{
		var screen = new UiScreen( name );
		var table = OriginalLayoutTables.ConfirmDialog;
		var rect = table.Root.Rect;
		screen.Add( new UiModelImage { Id = "window", Model = table.Root.Model!, Bounds = rect, Anchor = UiAnchor.Center } );
		var caption = table.Control( 0x4d ).Rect;
		screen.Add( new UiLabel
		{
			Id = "caption",
			Text = () => SplitCaption( text() ).Caption,
			Font = fonts => fonts.Label,
			Color = UiColors.Title,
			Align = UiAlign.Center,
			Fit = true,
			Bounds = caption,
			Anchor = UiAnchor.Center
		} );
		var textRight = table.Model( "b_okay" ).Rect.X - 30;
		screen.Add( new UiLabel
		{
			Id = "message",
			Text = () => SplitCaption( text() ).Body,
			Font = fonts => fonts.Label,
			Align = UiAlign.Center,
			Wrap = true,
			Bounds = new UiRect( rect.X + 56, caption.Bottom + 20, textRight - rect.X - 56, rect.Bottom - caption.Bottom - 60 ),
			Anchor = UiAnchor.Center
		} );
		if ( choices.Length <= 2 )
		{
			var buttons = new[] { table.Model( "b_okay" ), table.Model( "b_exit" ) };
			for ( var index = 0; index < choices.Length; index++ )
			{
				var choice = choices[index];
				screen.Add( new UiButton
				{
					Id = $"choice{index}",
					Model = buttons[index].Model,
					Help = choice.Label(),
					Clicked = choice.Clicked,
					Bounds = buttons[index].Rect,
					Anchor = UiAnchor.Center
				} );
			}
		}
		else
		{
			var gap = 30f;
			var buttonWidth = (rect.Width - 120 - (choices.Length - 1) * gap) / choices.Length;
			for ( var index = 0; index < choices.Length; index++ )
			{
				var choice = choices[index];
				screen.Add( new UiButton
				{
					Id = $"choice{index}",
					Text = choice.Label,
					Clicked = choice.Clicked,
					Bounds = new UiRect( rect.X + 60 + index * (buttonWidth + gap), rect.Bottom - 150, buttonWidth, 100 ),
					Anchor = UiAnchor.Center
				} );
			}
		}
		if ( choices.Length > 0 )
			screen.Back = choices[^1].Clicked;
		screen.Focus( screen.FocusableElements.FirstOrDefault() );
		return screen;
	}

	/// <summary>
	/// A frameless menu of large text lines in a centred column: no window, no title, each line cyan and white while focused, the screen dimmed behind it.
	/// </summary>
	/// <param name="back">Escape and the right click.</param>
	// [DATA:video 27:42-27:47, 27:59-28:03] the pause menus have no window frame and no title; their lines are spaced at about 11 % of the screen height
	// [APPROX:UI-013] the lines' exact rectangles (a 169-unit pitch, a 1000-unit width) are read off the video, not from a table
	public static UiScreen TextMenu( string name, IReadOnlyList<(string Id, Func<string> Text, Action Clicked, bool Enabled)> items, Action back, string? focus = null )
	{
		var screen = new UiScreen( name );
		const float pitch = 169f, width = 1100f;
		var top = (UiCanvas.VirtualHeight - items.Count * pitch) / 2;
		for ( var index = 0; index < items.Count; index++ )
		{
			var item = items[index];
			screen.Add( new UiMenuItem
			{
				Id = item.Id,
				Text = item.Text,
				Clicked = item.Clicked,
				Enabled = item.Enabled,
				Bounds = new UiRect( (UiCanvas.VirtualWidth - width) / 2, top + index * pitch + 8, width, pitch - 16 ),
				Anchor = UiAnchor.Center
			} );
		}
		screen.Back = back;
		screen.Focus( screen.FocusableElements.FirstOrDefault( element => element.Id == focus ) ?? screen.FocusableElements.FirstOrDefault() );
		return screen;
	}

	/// <summary>Splits a message into its caption (the first line, when more lines follow) and its body.</summary>
	internal static (string Caption, string Body) SplitCaption( string text )
	{
		var newline = text.IndexOf( '\n' );
		return newline < 0 ? ("", text) : (text[..newline].Trim(), text[(newline + 1)..].Trim());
	}
}
