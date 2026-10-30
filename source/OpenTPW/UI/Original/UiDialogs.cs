using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>Builders for the window-style screens shared by the front end and the pause menu.</summary>
public static class UiDialogs
{
	/// <summary>Authored rectangle of a centred window of the given canvas size.</summary>
	// [EXT:own-window-sizes] the sizes of OpenTPW's own windows (no original table exists for them); the original's windows come from their layout tables (OriginalLayoutTables)
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
			Shadow = true, Color = UiColors.Title,
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
	// [DATA:video 28:00] the message text sits below the caption and left of the buttons; the table's two text fields belong to the name dialog, so that rectangle is read off the video
	public static UiScreen Message( string name, Func<string> text, params (Func<string> Label, Action Clicked)[] choices ) => Message( name, text, false, choices );

	/// <summary><see cref="Message"/> for an original UITEXT message: its text is cut off at the text box like every original text control (0x48f3d0).</summary>
	public static UiScreen MessageOfOriginalText( string name, Func<string> text, params (Func<string> Label, Action Clicked)[] choices ) => Message( name, text, true, choices );

	private static UiScreen Message( string name, Func<string> text, bool originalText, (Func<string> Label, Action Clicked)[] choices )
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
			Shadow = true, Color = UiColors.Title,
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
			// [EXT:strings] a message that is not an original UITEXT one (OpenTPW's own strings, error texts; [EXT:ONLINE-UI] for the online screens' messages) is not cut off at the text box; an original one is, although the box itself is read off the video
			// ([DATA:video 28:00]), so for a long translation its clip is not the original's
			Unclipped = originalText ? null : () => true,
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
					HelpSource = choice.Label,
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

	/// <summary>The buttons of the original's generic message box: the tick (<c>b_okay</c>) and the cross (<c>b_exit</c>).</summary>
	public enum MessageBoxButton { Okay = 1, Exit = 2 }

	/// <summary>
	/// The original's generic message box (table 0x74F920): the wdialog window at (500,233)-(1516,830), its text white, centred and wrapped in the window's inner
	/// rectangle (610,276)-(1345,753), and up to three round buttons stacked at the right, the first at the bottom; a button closes the box after its action.
	/// </summary>
	/// <param name="close">Removes the box from its stack; every button runs it before its action.</param>
	/// <param name="buttons">The buttons from the bottom slot up, each the tick or the cross and its action (null closes only).</param>
	// [BIN:TPW-EXE:0x0047F020 message box] builds table 0x74F920, gives the text control the colour of its descriptor (+4..+6), centred (+0xD8 = 1, 0x65C428) with wrapping (+0xDA = 1, 0x65C4B9), and fills the
	// three button slots 0x9873C8, 0x9873C9, 0x9873CA from the bottom: a kind 1 slot takes b_okay, 2 b_exit, 0 hides it (0x65D9DD(0)); the handler 0x47EED0 calls the slot's callback and closes the box (message 4) when there is none or it returns nonzero
	// [BIN:TPW-EXE:0x0048F862 text alignment] the text control's +0xD8 picks left, centre or right (0, 1, 2; Hebrew swaps 0 and 2, 0x4925A0) and +0xD9 the vertical placement (1, the default, centres it); +0xDA set wraps at the control's width
	// [EXT:ui-font-tier-by-scale] the text's font is OpenTPW's label font: the descriptor's slot 4 names a bank font OpenTPW has no mode-to-bank rule for
	public static UiScreen OriginalMessageBox( string name, Func<string> text, Action close, params (MessageBoxButton Kind, Func<string> Help, Action? Clicked)[] buttons )
	{
		if ( buttons.Length > 3 ) throw new ArgumentException( "The message box has three button slots.", nameof( buttons ) );
		var screen = new UiScreen( name );
		var table = OriginalLayoutTables.MessageBox;
		var root = table.Root;
		screen.Add( new UiModelImage { Id = "window", Model = root.Model!, Bounds = root.Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel
		{
			Id = "message",
			Text = text,
			Font = fonts => fonts.Label,
			Color = new Veldrid.RgbaByte( 255, 255, 255, 255 ),
			Align = UiAlign.Center,
			Wrap = true,
			Bounds = root.Inner!.Value,
			Anchor = UiAnchor.Center
		} );
		var slots = new[] { 0x9873C8u, 0x9873C9u, 0x9873CAu };
		for ( var index = 0; index < buttons.Length; index++ )
		{
			var (kind, help, clicked) = buttons[index];
			void Click()
			{
				if ( screen.IsRemoved ) return;
				close();
				clicked?.Invoke();
			}
			screen.Add( new UiButton
			{
				Id = $"slot{index}",
				Model = kind == MessageBoxButton.Okay ? "b_okay" : "b_exit",
				HelpSource = help,
				Clicked = Click,
				Bounds = table.Control( slots[index] ).Rect,
				Anchor = UiAnchor.Center
			} );
		}
		// [EXT:ui-modal-right-click-back] Escape and the right click press the bottom button, as OpenTPW's other dialogs leave by their last choice
		if ( buttons.Length > 0 )
			screen.Back = ((UiButton)screen.Find( "slot0" )!).Clicked;
		screen.Focus( screen.FocusableElements.FirstOrDefault() );
		return screen;
	}

	/// <summary>
	/// A frameless menu of large text lines: no window, no title, each line cyan and white while focused, the screen dimmed behind it.
	/// Each line's box is its measured text in the menu font, centred; the default lines are 5 units apart, or contiguous in compact mode.
	/// </summary>
	/// <param name="back">Escape and the right click.</param>
	/// <param name="compact">Capture zero top and gap for the authenticated park menu; other callers retain the standard layout.</param>
	// [DATA:video 27:42-27:47, 27:59-28:03] the pause menus have no window frame and no title
	// [BIN:TPW-EXE:0x48c150 pause menu lines] the builder adds each line through 0x492f60(top, text, id, font slot 0, 128, 128, 128): the first at top 10 and each next at the
	// previous line's bottom + 5 (0x48c240); 0x492f60 measures the text in the slot's font (0x4929f0) and makes the line (text width + 1) x 2048 / screen width wide and
	// (text height + 5) x 1536 / screen height high, centred in its parent, the full-canvas lolight window (0,0)-(2047,1535) of 0x492e80
	// [BIN:TPW-EXE:0x48c36c online pause layout] the park pause menu's top and gap are 0 instead of 10 and 5 while the net-session field +0x60 (0x5b6450) is non-zero (0x48c36c-0x48c4af);
	// that field is set only by the login reply 'Authorization OK' (0x5b6363) and cleared by every other login reply, the login teardown and the other session resets (0x5b6375-0x5b63dd, 0x5b6588, 0x5b595f, 0x5b5e7d, 0x5b7a84, 0x5b7d6a, 0x5b7f0b), so the park menu maps it to
	// the host's authenticated session captured on opening (HudHost.IsLoggedIn)
	// [BIN:TPW-EXE:0x4929f0 menu line measure] the measure takes the font slot of the line (0 for the pause menus: slot 0 and 2 swap while the word at 0x78b538 is 0x40d, 0x41a850), reads the font record of the bank (index 13 x bank + slot, 24-byte records from 0x78b54c, handle at +0x14), lays the text out in a box through 0x6ae1c0 and returns the block box; with the wrap flag clear the box is 0x4000 wide, so nothing wraps, which OpenTPW's text layout box matches
	// [EXT:ui-font-tier-by-scale] the line height is that of OpenTPW's menu font: which shipped font serves slot 0 per size tier is OpenTPW's choice, as the original picks one of four banks by its fixed screen modes
	// [BIN:TPW-EXE:0x48b2a0 pause line ramp] the focused line's colour ramps from grey 0x21 in steps of 0x20 to white over eight frames (UiMenuItem, MenuLineRamp)
	public static UiScreen TextMenu( string name, IReadOnlyList<(string Id, Func<string> Text, Action Clicked, bool Enabled)> items, Action back, string? focus = null, bool compact = false )
	{
		var screen = new TextMenuScreen( name, compact );
		var top = compact ? 0 : TextMenuTop;
		var gap = compact ? 0 : TextMenuGap;
		for ( var index = 0; index < items.Count; index++ )
		{
			var item = items[index];
			screen.Add( new UiMenuItem
			{
				Id = item.Id,
				Text = item.Text,
				Font = fonts => fonts.Menu,
				Clicked = item.Clicked,
				Enabled = item.Enabled,
				Bounds = new UiRect( (UiCanvas.VirtualWidth - TextMenuFallbackWidth) / 2, top + index * (TextMenuFallbackHeight + gap),
					TextMenuFallbackWidth, TextMenuFallbackHeight ),
				Anchor = UiAnchor.Center
			} );
		}
		screen.Back = back;
		screen.Focus( screen.FocusableElements.FirstOrDefault( element => element.Id == focus ) ?? screen.FocusableElements.FirstOrDefault() );
		return screen;
	}

	/// <summary>The top of the first menu line and the gap between lines, in canvas units (0x48c150).</summary>
	public const float TextMenuTop = 10, TextMenuGap = 5;
	// the boxes before the first layout with a context (no font measured yet)
	private const float TextMenuFallbackWidth = 1100, TextMenuFallbackHeight = 169;

	/// <summary>
	/// The canvas boxes of menu lines whose texts measure <paramref name="textPixels"/> font pixels, drawn at <paramref name="textScale"/> screen pixels per
	/// font pixel on a canvas of <paramref name="canvasScale"/> screen pixels per unit: (width + 1) and (height + 5) font pixels, centred, from the top 10 units down, 5 apart (or zero top and gap for a compact menu).
	/// </summary>
	public static UiRect[] TextMenuLines( IReadOnlyList<(int Width, int Height)> textPixels, int textScale, float canvasScale, bool compact = false )
	{
		var lines = new UiRect[textPixels.Count];
		var top = compact ? 0 : TextMenuTop;
		for ( var index = 0; index < lines.Length; index++ )
		{
			var width = (textPixels[index].Width + 1) * textScale / canvasScale;
			var height = (textPixels[index].Height + 5) * textScale / canvasScale;
			lines[index] = new UiRect( (UiCanvas.VirtualWidth - width) / 2, top, width, height );
			top += height + (compact ? 0 : TextMenuGap);
		}
		return lines;
	}

	private sealed class TextMenuScreen( string name, bool compact ) : UiScreen( name )
	{
		internal override void PreparePointerGeometry( UiContext context )
		{
			if ( context.HasFonts )
			{
				var items = Elements.OfType<UiMenuItem>().ToArray();
				var sizes = items.Select( item => context.Layout( item.Font( context.Fonts ), item.Text() ) ).Select( layout => (layout.Width, layout.Height) ).ToArray();
				var lines = TextMenuLines( sizes, context.Canvas.TextScale, context.Canvas.Scale, compact );
				for ( var index = 0; index < items.Length; index++ )
					items[index].Bounds = lines[index];
			}
		}

		public override bool Update( UiContext context, UiInput input )
		{
			PreparePointerGeometry( context );
			if ( !input.PointerSuppressed ) PlayBackdropClick( input, HitTest( context.Canvas, input.Mouse ) );
			return base.Update( context, input );
		}

		internal override bool UpdatePointer( UiContext context, UiInput input, UiElement? recipient )
		{
			if ( IsRemoved ) return true;
			PlayBackdropClick( input, recipient );
			return base.UpdatePointer( context, input, recipient );
		}

		private static void PlayBackdropClick( UiInput input, UiElement? recipient )
		{
			// [BIN:TPW-EXE:0x492d80 pause backdrop click] a click (message 0x10006, either button) on the menu's full-canvas window outside its lines plays cat_ui 0xC1 flat, then sends the activate message 0x100001 to the window's owner; the window is made with parent 0 (0x492ef0), so the owner is probably none and 0x658d9f returns at once (what the pause menu's callback 0x48b6a0 does with 0x100001 for the backdrop is not read, so no OpenTPW rule follows from it); a click on a line goes to the line
			if ( input.Mouse.X >= 0 && (input.LeftReleased && !input.NotClick || input.RightPressed) && recipient == null )
				GameAudio.PlayUi( GameAudio.PauseBackdropEvent );
		}
	}

	/// <summary>Splits a message into its caption (the first line, when more lines follow) and its body.</summary>
	internal static (string Caption, string Body) SplitCaption( string text )
	{
		var newline = text.IndexOf( '\n' );
		return newline < 0 ? ("", text) : (text[..newline].Trim(), text[(newline + 1)..].Trim());
	}
}
