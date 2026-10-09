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
	/// A message box in the original dialog window (w_dialog) with its UITEXT message, which starts
	/// with an upper-case caption line, and one button per choice.
	/// </summary>
	public static UiScreen Message( string name, Func<string> text, params (Func<string> Label, Action Clicked)[] choices )
	{
		var screen = new UiScreen( name );
		var rect = CenteredWindow( 1100, 700 );
		screen.Add( new UiModelImage { Id = "window", Model = "w_dialog", Bounds = rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel
		{
			Id = "message",
			Text = text,
			Font = fonts => fonts.Label,
			Align = UiAlign.Center,
			Wrap = true,
			Bounds = new UiRect( rect.X + 90, rect.Y + 80, rect.Width - 260, rect.Height - 280 ),
			Anchor = UiAnchor.Center
		} );
		var buttonWidth = 380f;
		var gap = 40f;
		var total = choices.Length * buttonWidth + (choices.Length - 1) * gap;
		var x = rect.X + (rect.Width - 80 - total) / 2;
		for ( var index = 0; index < choices.Length; index++ )
		{
			var choice = choices[index];
			screen.Add( new UiButton
			{
				Id = $"choice{index}",
				Text = choice.Label,
				Clicked = choice.Clicked,
				Bounds = new UiRect( x + index * (buttonWidth + gap), rect.Bottom - 190, buttonWidth, 110 ),
				Anchor = UiAnchor.Center
			} );
		}
		if ( choices.Length > 0 )
			screen.Back = choices[^1].Clicked;
		screen.Focus( screen.FocusableElements.FirstOrDefault() );
		return screen;
	}
}
