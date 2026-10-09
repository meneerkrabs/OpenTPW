using Veldrid;

namespace OpenTPW.UI;

/// <summary>
/// Sandbox panel drawn with original BF4 fonts and original English string-table entries: the
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
		var uiText = new StringFile( "Language/English/UITEXT.str" );
		var objectNames = new StringFile( "Language/English/OBJECT_NAMES.str" );
		headingFont = new FontAtlas( new FontFile( "Language/English/SESHMED.bf4" ) );
		labelFont = new FontAtlas( new FontFile( "Language/English/GAME8AA.bf4" ) );
		heading = TextLayout.Create( headingFont, objectNames[TotemObjectName] );
		labels = TextLayout.Create( labelFont, string.Join( "\n",
			uiText[(int)UIStrings.Excitement], uiText[(int)UIStrings.Reliability], uiText[(int)UIStrings.StateOfRepair], uiText[(int)UIStrings.RemainingLife] ) );
		note = TextLayout.Create( labelFont, uiText[(int)UIStrings.NoUpgrades] );
		renderer = new TextRenderer( Render.MultisampledFramebuffer.OutputDescription );
		var missing = heading.MissingCharacters.Concat( labels.MissingCharacters ).Concat( note.MissingCharacters ).ToArray();
		if ( missing.Length > 0 )
			Log.Warning( $"BF4 text overlay is missing {missing.Length} glyph(s); fallback glyphs are drawn." );
	}

	/// <summary>
	/// Rebuilds the batch for a framebuffer width, anchored to the top-right corner.
	/// </summary>
	public void Build( int framebufferWidth )
	{
		var contentWidth = Math.Max( heading.Width, Math.Max( labels.Width, note.Width ) );
		var width = contentWidth + Padding * 2;
		var height = heading.Height + labels.Height + labelFont.LineHeight + note.Height + Padding * 2;
		var x = Math.Max( 0, framebufferWidth - width - Margin );
		var y = Margin;
		Bounds = (x, y, width, height);
		Batch.Clear();
		Batch.AddRectangle( x, y, width, height, BackgroundColor );
		Batch.AddText( headingFont, heading, x + Padding, y + Padding, HeadingColor );
		Batch.AddText( labelFont, labels, x + Padding, y + Padding + heading.Height, LabelColor );
		Batch.AddText( labelFont, note, x + Padding, y + Padding + heading.Height + labels.Height + labelFont.LineHeight, LabelColor );
	}

	public void Draw()
	{
		var framebuffer = Render.MultisampledFramebuffer;
		Build( (int)framebuffer.Width );
		renderer.Draw( Render.CommandList, Batch, framebuffer.Width, framebuffer.Height );
	}

	public void Dispose() => renderer.Dispose();
}
