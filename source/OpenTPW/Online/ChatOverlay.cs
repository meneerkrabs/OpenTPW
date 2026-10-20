using Veldrid;

namespace OpenTPW.UI;

/// <summary>
/// Chat lines drawn with the selected language's original BF4 font (GAME8AA) in the overlay pass, while the player is in the park's HUD or the
/// lobby with a chat connection open: the chat parks and a visit's chat mode show the original chat overlay instead (<see cref="ChatPanel"/>).
/// [EXT:ONLINE-065] Font choice, position (bottom-left), colours and line count are OpenTPW choices.
/// </summary>
internal sealed class ChatOverlay : IDisposable
{
	public const int VisibleLines = 8;
	private const int Margin = 16;
	private const int Padding = 6;
	private static readonly RgbaByte BackgroundColor = new( 16, 24, 48, 200 );
	private static readonly RgbaByte TextColor = new( 255, 255, 255, 255 );

	private readonly OnlineSession session;
	private TextRenderer? renderer;
	private FontAtlas? font;
	private int fontVersion = -1;
	private readonly TextBatch batch = new();
	private string lastText = "";
	private TextLayoutResult? layout;
	private int lastWidth;

	public ChatOverlay( OnlineSession session ) => this.session = session;

	/// <summary>True while another view shows the lines (a visit's chat mode shows them in the original chat overlay).</summary>
	public Func<bool>? Hidden { get; init; }

	public void Draw()
	{
		if ( session.ChatLines.Count == 0 || Hidden?.Invoke() == true )
			return;
		if ( font == null || fontVersion != GameLanguage.Version )
		{
			font = new FontAtlas( GameLanguage.Current.LoadFont( "GAME8AA.bf4" ) );
			fontVersion = GameLanguage.Version;
			layout = null;
		}
		renderer ??= new TextRenderer( Render.OutputDescription );
		var text = string.Join( "\n", session.ChatLines.Skip( Math.Max( 0, session.ChatLines.Count - VisibleLines ) ) );
		var framebuffer = Render.OverlayFramebuffer;
		var scale = Screen.UiScale;
		var layoutWidth = Math.Max( 1, (int)framebuffer.Width / (2 * scale) );
		if ( text != lastText || layout == null || layoutWidth != lastWidth )
		{
			layout = TextLayout.Create( font, text, layoutWidth );
			lastText = text;
			lastWidth = layoutWidth;
		}
		var width = (layout.Width + Padding * 2) * scale;
		var height = (layout.Height + Padding * 2) * scale;
		var y = (int)framebuffer.Height - height - Margin * scale;
		batch.Clear();
		batch.AddRectangle( Margin * scale, Math.Max( 0, y ), width, height, BackgroundColor );
		batch.AddText( font, layout, (Margin + Padding) * scale, Math.Max( 0, y ) + Padding * scale, TextColor, scale );
		renderer.Draw( Render.CommandList, batch, framebuffer.Width, framebuffer.Height );
	}

	public void Dispose() => renderer?.Dispose();
}
