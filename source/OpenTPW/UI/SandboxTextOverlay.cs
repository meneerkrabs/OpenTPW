using Veldrid;

namespace OpenTPW.UI;

/// <summary>
/// Sandbox panel drawn with the selected language's original BF4 fonts and string-table entries: the
/// Totem ride name in SESHMED and ride-panel labels in GAME8AA. Placement, colors and the backing
/// rectangle are OpenTPW choices, not a reproduction of an original UI screen.
/// </summary>
internal sealed class SandboxTextOverlay : IDisposable
{
	public const int TotemObjectName = 29;
	private const int Margin = 16;
	private const int Padding = 8;
	private static readonly RgbaByte BackgroundColor = new( 24, 32, 64, 255 );
	private static readonly RgbaByte HeadingColor = new( 255, 214, 64, 255 );
	private static readonly RgbaByte LabelColor = new( 255, 255, 255, 255 );

	private readonly TextRenderer renderer;
	private readonly FontAtlas headingFont;
	private readonly FontAtlas labelFont;
	private readonly TextLayoutResult heading;
	private readonly TextLayoutResult labels;
	private readonly TextLayoutResult note;

	public TextBatch Batch { get; } = new();
	public (int X, int Y, int Width, int Height) Bounds { get; private set; }

	public SandboxTextOverlay()
	{
		var language = GameLanguage.Current;
		var uiText = language.LoadStrings( "UITEXT.str" );
		var objectNames = language.LoadStrings( "OBJECT_NAMES.str" );
		headingFont = new FontAtlas( language.LoadFont( "SESHMED.bf4" ) );
		labelFont = new FontAtlas( language.LoadFont( "GAME8AA.bf4" ) );
		heading = TextLayout.Create( headingFont, objectNames[TotemObjectName] );
		labels = TextLayout.Create( labelFont, string.Join( "\n",
			uiText[(int)UIStrings.Excitement], uiText[(int)UIStrings.Reliability], uiText[(int)UIStrings.StateOfRepair], uiText[(int)UIStrings.RemainingLife] ) );
		note = TextLayout.Create( labelFont, uiText[(int)UIStrings.NoUpgrades] );
		renderer = new TextRenderer( Render.OutputDescription );
		var missing = heading.MissingCharacters.Concat( labels.MissingCharacters ).Concat( note.MissingCharacters ).ToArray();
		if ( missing.Length > 0 )
			Log.Warning( $"BF4 text overlay is missing {missing.Length} glyph(s); fallback glyphs are drawn." );
	}

	/// <summary>
	/// Rebuilds the batch for an output framebuffer width, anchored to the top-right corner. Margins,
	/// padding and glyphs are multiplied by the integer UI <paramref name="scale"/>.
	/// </summary>
	public void Build( int framebufferWidth, int scale = 1 )
	{
		var contentWidth = Math.Max( heading.Width, Math.Max( labels.Width, note.Width ) );
		var width = (contentWidth + Padding * 2) * scale;
		var height = (heading.Height + labels.Height + labelFont.LineHeight + note.Height + Padding * 2) * scale;
		var x = Math.Max( 0, framebufferWidth - width - Margin * scale );
		var y = Margin * scale;
		var padding = Padding * scale;
		Bounds = (x, y, width, height);
		Scale = scale;
		Batch.Clear();
		Batch.AddRectangle( x, y, width, height, BackgroundColor );
		Batch.AddText( headingFont, heading, x + padding, y + padding, HeadingColor, scale );
		Batch.AddText( labelFont, labels, x + padding, y + padding + heading.Height * scale, LabelColor, scale );
		Batch.AddText( labelFont, note, x + padding, y + padding + (heading.Height + labels.Height + labelFont.LineHeight) * scale, LabelColor, scale );
	}

	public int Scale { get; private set; } = 1;

	/// <summary>Draws into the renderer's overlay pass (output size, after the world blit).</summary>
	public void Draw()
	{
		var framebuffer = Render.OverlayFramebuffer;
		Build( (int)framebuffer.Width, Screen.UiScale );
		renderer.Draw( Render.CommandList, Batch, framebuffer.Width, framebuffer.Height );
	}

	public void Dispose() => renderer.Dispose();
}
