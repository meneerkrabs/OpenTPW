namespace OpenTPW;

/// <summary>
/// One packed BF4 glyph. <see cref="X"/>/<see cref="Y"/> locate its coverage in the atlas;
/// offsets and advance are copied unchanged from the original glyph record.
/// </summary>
public sealed record FontAtlasGlyph( char Character, int SourceIndex, int X, int Y, int Width, int Height, int OffsetX, int OffsetY, int Advance );

/// <summary>
/// Deterministic single-channel atlas built from a decoded BF4 font. Coverage samples 0–15 map
/// linearly to alpha 0–255 (value × 17); this is an OpenTPW choice, not an evidenced original
/// palette or gamma curve. Glyphs are shelf-packed by descending height, descending width and
/// ascending character with one transparent pixel of padding. The first record of each
/// character in table order is used; later duplicate records (for example the 58 identical
/// space slots in the English corpus) are not packed.
/// </summary>
public sealed class FontAtlas
{
	public const int Padding = 1;
	public const int MinimumSize = 64;
	public const int MaximumSize = 4096;

	private readonly Dictionary<char, FontAtlasGlyph> glyphs;

	public int Width { get; }
	public int Height { get; }

	/// <summary>
	/// Row-major alpha, <see cref="Width"/> × <see cref="Height"/> bytes, row 0 at the top.
	/// </summary>
	public byte[] Alpha { get; }

	/// <summary>
	/// Distance between baselines of consecutive lines. Taken from the header height hint: across
	/// the 33 English fonts, the lowest glyph bottom (offset Y + height) is within one pixel of it.
	/// Falls back to the lowest glyph bottom if the hint is zero.
	/// </summary>
	public int LineHeight { get; }

	public IReadOnlyDictionary<char, FontAtlasGlyph> Glyphs => glyphs;

	public FontAtlas( FontFile font )
	{
		ArgumentNullException.ThrowIfNull( font );
		var unique = new List<(FontGlyph Glyph, int Index)>();
		var seen = new HashSet<char>();
		for ( var index = 0; index < font.Glyphs.Count; index++ )
		{
			if ( seen.Add( font.Glyphs[index].Character ) )
				unique.Add( (font.Glyphs[index], index) );
		}

		var packed = unique.Where( entry => entry.Glyph.Width > 0 && entry.Glyph.Height > 0 )
			.OrderByDescending( entry => entry.Glyph.Height )
			.ThenByDescending( entry => entry.Glyph.Width )
			.ThenBy( entry => entry.Glyph.Character )
			.ToArray();

		var placements = Pack( packed.Select( entry => (entry.Glyph.Width, entry.Glyph.Height) ).ToArray(), out var width, out var height );
		Width = width;
		Height = height;
		Alpha = new byte[width * height];
		glyphs = new Dictionary<char, FontAtlasGlyph>();
		for ( var item = 0; item < packed.Length; item++ )
		{
			var (glyph, index) = packed[item];
			var (x, y) = placements[item];
			for ( var row = 0; row < glyph.Height; row++ )
			{
				for ( var column = 0; column < glyph.Width; column++ )
					Alpha[(y + row) * width + x + column] = (byte)(glyph.Coverage[row * glyph.Width + column] * 17);
			}
			glyphs[glyph.Character] = Create( glyph, index, x, y );
		}
		foreach ( var (glyph, index) in unique.Where( entry => entry.Glyph.Width == 0 || entry.Glyph.Height == 0 ) )
			glyphs[glyph.Character] = Create( glyph, index, 0, 0 );

		var lowest = font.Glyphs.Where( glyph => glyph.Height > 0 ).Select( glyph => glyph.OffsetY + glyph.Height ).DefaultIfEmpty( 0 ).Max();
		LineHeight = font.HeaderHeightHint > 0 ? font.HeaderHeightHint : lowest;
	}

	public bool TryGetGlyph( char character, out FontAtlasGlyph glyph ) => glyphs.TryGetValue( character, out glyph! );

	/// <summary>
	/// Expands <see cref="Alpha"/> to white RGBA8 with straight alpha.
	/// </summary>
	public byte[] ToRgba()
	{
		var rgba = new byte[Alpha.Length * 4];
		for ( var pixel = 0; pixel < Alpha.Length; pixel++ )
		{
			rgba[pixel * 4] = 255;
			rgba[pixel * 4 + 1] = 255;
			rgba[pixel * 4 + 2] = 255;
			rgba[pixel * 4 + 3] = Alpha[pixel];
		}
		return rgba;
	}

	private static FontAtlasGlyph Create( FontGlyph glyph, int index, int x, int y ) =>
		new( glyph.Character, index, x, y, glyph.Width, glyph.Height, glyph.OffsetX, glyph.OffsetY, glyph.Advance );

	private static (int X, int Y)[] Pack( (int Width, int Height)[] sizes, out int width, out int height )
	{
		for ( width = MinimumSize; width <= MaximumSize; width *= 2 )
		{
			var placements = new (int X, int Y)[sizes.Length];
			var x = Padding;
			var y = Padding;
			var shelfHeight = 0;
			var fits = true;
			for ( var item = 0; item < sizes.Length && fits; item++ )
			{
				var (glyphWidth, glyphHeight) = sizes[item];
				if ( glyphWidth + 2 * Padding > width )
				{
					fits = false;
					break;
				}
				if ( x + glyphWidth + Padding > width )
				{
					y += shelfHeight + Padding;
					x = Padding;
					shelfHeight = 0;
				}
				placements[item] = (x, y);
				x += glyphWidth + Padding;
				shelfHeight = Math.Max( shelfHeight, glyphHeight );
			}
			var usedHeight = y + shelfHeight + Padding;
			if ( !fits || usedHeight > width )
				continue;
			height = 1;
			while ( height < usedHeight )
				height *= 2;
			return placements;
		}
		throw new InvalidDataException( $"BF4 glyphs do not fit in a {MaximumSize}×{MaximumSize} atlas." );
	}
}
