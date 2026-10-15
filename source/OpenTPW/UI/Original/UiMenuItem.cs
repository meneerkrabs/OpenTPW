namespace OpenTPW.UI.Original;

/// <summary>
/// One line of a frameless text menu (the pause menu): large cyan text that turns white while it is focused or pressed, and grey when disabled.
/// </summary>
// [DATA:video 27:42-27:47 pause menu] a column of large cyan text items (about (0,173,218)); the focused item is white (about (255,250,230))
// [APPROX:UI-006] the cyan and the white are read off the 720p video (chroma +-15); the original's font palette is not traced
public sealed class UiMenuItem : UiElement
{
	public Func<string> Text { get; set; } = () => "";
	public Action? Clicked { get; set; }
	public Func<UiFonts, FontAtlas> Font { get; set; } = fonts => fonts.Title;
	public override bool Focusable => Visible && Enabled;

	public override void Activate()
	{
		if ( !Enabled )
			return;
		// the UI message hook plays cat_ui 0x1F for every activated control (see UiButton)
		GameAudio.PlayUi( GameAudio.ClickEvent );
		Clicked?.Invoke();
	}

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		var color = !Enabled ? UiColors.Disabled : focused || pressed ? UiColors.MenuItemFocused : UiColors.MenuItem;
		context.DrawFittedText( Font( context.Fonts ), Text(), ScreenRect( context.Canvas ), color, UiAlign.Center );
	}
}
