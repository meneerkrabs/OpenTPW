namespace OpenTPW;

/// <summary>
/// A glyph placed by <see cref="TextLayout"/>. <see cref="X"/>/<see cref="Y"/> are the top-left of
/// the glyph bitmap relative to the layout origin (top-left of the first line box).
/// </summary>
public readonly record struct TextLayoutGlyph( char Character, int X, int Y, FontAtlasGlyph Glyph );

public sealed record TextLayoutResult( IReadOnlyList<TextLayoutGlyph> Glyphs, int Width, int Height, int LineCount, IReadOnlyList<char> MissingCharacters )
{
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
/// greedily at spaces and overlong words break between characters; this wrapping policy is
/// OpenTPW's, not verified against the original UI. Characters without a glyph render as the
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
		var lines = new List<List<FontAtlasGlyph>>();
		foreach ( var hardLine in text.Replace( "\r\n", "\n" ).Replace( '\r', '\n' ).Split( '\n' ) )
		{
			var glyphs = new List<FontAtlasGlyph>( hardLine.Length );
			foreach ( var character in hardLine )
			{
				if ( atlas.TryGetGlyph( character, out var glyph ) )
				{
					glyphs.Add( glyph );
					continue;
				}
				missing.Add( character );
				if ( atlas.TryGetGlyph( fallback, out var replacement ) )
					glyphs.Add( replacement );
			}
			if ( maximumWidth > 0 )
				lines.AddRange( Wrap( glyphs, maximumWidth ) );
			else
				lines.Add( glyphs );
		}

		var placed = new List<TextLayoutGlyph>();
		var width = 0;
		for ( var line = 0; line < lines.Count; line++ )
		{
			var pen = 0;
			foreach ( var glyph in lines[line] )
			{
				if ( glyph.Width > 0 && glyph.Height > 0 )
					placed.Add( new TextLayoutGlyph( glyph.Character, pen + glyph.OffsetX, line * atlas.LineHeight + glyph.OffsetY, glyph ) );
				pen += glyph.Advance;
			}
			width = Math.Max( width, pen );
		}
		return new TextLayoutResult( placed.AsReadOnly(), width, lines.Count * atlas.LineHeight, lines.Count, missing.AsReadOnly() );
	}

	private static IEnumerable<List<FontAtlasGlyph>> Wrap( List<FontAtlasGlyph> glyphs, int maximumWidth )
	{
		var line = new List<FontAtlasGlyph>();
		var lineWidth = 0;
		var index = 0;
		while ( index < glyphs.Count )
		{
			if ( glyphs[index].Character == ' ' )
			{
				var spaceEnd = index;
				var spaceWidth = 0;
				while ( spaceEnd < glyphs.Count && glyphs[spaceEnd].Character == ' ' )
					spaceWidth += glyphs[spaceEnd++].Advance;
				var wordEnd = spaceEnd;
				var wordWidth = 0;
				while ( wordEnd < glyphs.Count && glyphs[wordEnd].Character != ' ' )
					wordWidth += glyphs[wordEnd++].Advance;
				if ( line.Count > 0 && wordEnd > spaceEnd && lineWidth + spaceWidth + wordWidth > maximumWidth )
				{
					yield return line;
					line = new List<FontAtlasGlyph>();
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
				line = new List<FontAtlasGlyph>();
				lineWidth = 0;
			}
			line.Add( glyph );
			lineWidth += glyph.Advance;
			index++;
		}
		yield return line;
	}
}
