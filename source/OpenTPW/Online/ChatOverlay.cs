using Veldrid;

namespace OpenTPW.UI;

/// <summary>
/// Chat lines drawn with the selected language's original BF4 font (GAME8AA) in the overlay pass.
/// [EXT:ONLINE-065] Font choice, position (bottom-left), colours and line count are OpenTPW choices;
/// the original chat panel (ui.wad f_chat.MD2, chatpan2/f_chat1-3 textures) is not reproduced yet.
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
	private readonly TextBatch batch = new();
	private string lastText = "";
	private TextLayoutResult? layout;
	private int lastWidth;

	public ChatOverlay( OnlineSession session ) => this.session = session;

	public void Draw()
	{
		if ( session.ChatLines.Count == 0 )
			return;
		font ??= new FontAtlas( GameLanguage.Current.LoadFont( "GAME8AA.bf4" ) );
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
