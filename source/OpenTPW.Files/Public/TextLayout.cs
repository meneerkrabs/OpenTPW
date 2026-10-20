namespace OpenTPW;

/// <summary>
/// A glyph placed by <see cref="TextLayout"/>. <see cref="X"/>/<see cref="Y"/> are the top-left of
/// the glyph bitmap relative to the layout origin (top-left of the first line box).
/// </summary>
public readonly record struct TextLayoutGlyph( char Character, int X, int Y, FontAtlasGlyph Glyph, int Line = 0 );

public sealed record TextLayoutResult( IReadOnlyList<TextLayoutGlyph> Glyphs, int Width, int Height, int LineCount, IReadOnlyList<char> MissingCharacters, IReadOnlyList<int>? LineWidths = null )
{
	/// <summary>Advance width of each line (trailing spaces dropped at a wrap are not counted); <see cref="Width"/> is the widest.</summary>
	public IReadOnlyList<int> LineWidths { get; } = LineWidths ?? new[] { Width };

	/// <summary>
	/// The layout with every line placed inside the block as tp.exe's text library places it (0x6ae550): a line
	/// is moved right by <c>(block width - line width) &gt;&gt; 1</c> when centred and by the whole difference when
	/// right aligned; left aligned lines stay at the block's left edge. The block is the widest line.
	/// </summary>
	public TextLayoutResult AlignLines( TextAlignment alignment )
	{
		if ( alignment == TextAlignment.Left || LineCount <= 1 )
			return this;
		var glyphs = Glyphs.Select( glyph => glyph with { X = glyph.X + TextPlacement.LineOffset( alignment, Width, LineWidths[Math.Min( glyph.Line, LineWidths.Count - 1 )] ) } ).ToList();
		return new TextLayoutResult( glyphs.AsReadOnly(), Width, Height, LineCount, MissingCharacters, LineWidths );
	}

	/// <summary>Left edge of the drawn pixels (glyph bitmaps); 0 when nothing is drawn.</summary>
	public int InkLeft { get; } = Glyphs.Count == 0 ? 0 : Glyphs.Min( glyph => glyph.X );
	/// <summary>Top edge of the drawn pixels; 0 when nothing is drawn.</summary>
	public int InkTop { get; } = Glyphs.Count == 0 ? 0 : Glyphs.Min( glyph => glyph.Y );
	/// <summary>Right edge (exclusive) of the drawn pixels; 0 when nothing is drawn.</summary>
	public int InkRight { get; } = Glyphs.Count == 0 ? 0 : Glyphs.Max( glyph => glyph.X + glyph.Glyph.Width );
	/// <summary>Bottom edge (exclusive) of the drawn pixels; 0 when nothing is drawn.</summary>
	public int InkBottom { get; } = Glyphs.Count == 0 ? 0 : Glyphs.Max( glyph => glyph.Y + glyph.Glyph.Height );
}

/// <summary>
/// Pixel layout with original BF4 metrics: the pen advances by each record's advance, bitmaps are
/// placed at pen + offset X and line top + offset Y, and lines are <see cref="FontAtlas.LineHeight"/>
/// apart. No kerning is applied because the BF4 files contain no pair-adjustment data.
/// <c>\n</c>, <c>\r</c> and <c>\r\n</c> start a new line. With a positive maximum width, lines wrap
/// greedily at spaces and tabs and overlong words break between characters, as tp.exe's formatter
/// 0x6aec80 does (its glyph classes, 0x6aeab0: space and tab 0x12 = break opportunity + space, LF 4, CR 0xc, everything else 1). Characters without a glyph render as the
/// fallback glyph (<c>?</c> by default) and are reported in <see cref="TextLayoutResult.MissingCharacters"/>.
/// Width/height describe advance and line boxes; ink can extend past them through negative or
/// large offsets.
/// </summary>
public static class TextLayout
{
	public static TextLayoutResult Create( FontAtlas atlas, string text, int maximumWidth = 0, char fallback = '?' )
	{
		ArgumentNullException.ThrowIfNull( atlas );
		ArgumentNullException.ThrowIfNull( text );
		var missing = new List<char>();
		var lines = new List<List<Placed>>();
		foreach ( var hardLine in text.Replace( "\r\n", "\n" ).Replace( '\r', '\n' ).Split( '\n' ) )
		{
			var glyphs = new List<Placed>( hardLine.Length );
			foreach ( var character in hardLine )
			{
				if ( atlas.TryGetGlyph( character, out var glyph ) )
				{
					glyphs.Add( new Placed( character, glyph ) );
					continue;
				}
				missing.Add( character );
				if ( atlas.TryGetGlyph( fallback, out var replacement ) )
					glyphs.Add( new Placed( character, replacement ) );
			}
			if ( maximumWidth > 0 )
				lines.AddRange( Wrap( glyphs, maximumWidth ) );
			else
				lines.Add( glyphs );
		}
		// [BIN:TPW-EXE:0x6ae4b0 line reorder] each laid-out line that holds Hebrew is drawn in visual order (TextVisualOrder); the line's width is unchanged
		foreach ( var line in lines )
			TextVisualOrder.ReorderLine( line, item => item.Source );

		var placed = new List<TextLayoutGlyph>();
		var lineWidths = new List<int>( lines.Count );
		var width = 0;
		for ( var line = 0; line < lines.Count; line++ )
		{
			var pen = 0;
			foreach ( var (_, glyph) in lines[line] )
			{
				if ( glyph.Width > 0 && glyph.Height > 0 )
					placed.Add( new TextLayoutGlyph( glyph.Character, pen + glyph.OffsetX, line * atlas.LineHeight + glyph.OffsetY, glyph, line ) );
				pen += glyph.Advance;
			}
			lineWidths.Add( pen );
			width = Math.Max( width, pen );
		}
		return new TextLayoutResult( placed.AsReadOnly(), width, lines.Count * atlas.LineHeight, lines.Count, missing.AsReadOnly(), lineWidths.AsReadOnly() );
	}

	// [BIN:TPW-EXE:0x6aeab0 glyph class] the original flags only space and tab (0x12) as break opportunities; a hyphen or any other character does not break
	private static bool IsSpace( char character ) => character is ' ' or '\t';

	/// <summary>A glyph with the character it stands for (the fallback glyph keeps the missing character).</summary>
	private readonly record struct Placed( char Source, FontAtlasGlyph Glyph )
	{
		public char Character => Glyph.Character;
		public int Advance => Glyph.Advance;
	}

	private static IEnumerable<List<Placed>> Wrap( List<Placed> glyphs, int maximumWidth )
	{
		var line = new List<Placed>();
		var lineWidth = 0;
		var index = 0;
		while ( index < glyphs.Count )
		{
			if ( IsSpace( glyphs[index].Source ) )
			{
				var spaceEnd = index;
				var spaceWidth = 0;
				while ( spaceEnd < glyphs.Count && IsSpace( glyphs[spaceEnd].Source ) )
					spaceWidth += glyphs[spaceEnd++].Advance;
				var wordEnd = spaceEnd;
				var wordWidth = 0;
				while ( wordEnd < glyphs.Count && !IsSpace( glyphs[wordEnd].Source ) )
					wordWidth += glyphs[wordEnd++].Advance;
				if ( line.Count > 0 && wordEnd > spaceEnd && lineWidth + spaceWidth + wordWidth > maximumWidth )
				{
					yield return line;
					line = new List<Placed>();
					lineWidth = 0;
					index = spaceEnd;
					continue;
				}
				for ( ; index < spaceEnd; index++ )
				{
					line.Add( glyphs[index] );
					lineWidth += glyphs[index].Advance;
				}
				continue;
			}
			var glyph = glyphs[index];
			if ( line.Count > 0 && lineWidth + glyph.Advance > maximumWidth )
			{
				yield return line;
				line = new List<Placed>();
				lineWidth = 0;
			}
			line.Add( glyph );
			lineWidth += glyph.Advance;
			index++;
		}
		yield return line;
	}
}
