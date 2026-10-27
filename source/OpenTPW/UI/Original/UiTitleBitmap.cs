namespace OpenTPW.UI.Original;

/// <summary>
/// The bitmap of a title control (see <see cref="UiBatch.AddTitleText"/>), composed on the CPU as the original builds it: the string is written into a cleared bitmap twice, first the grey
/// copy (128,128,128) with the colour alpha <c>copyAlpha</c> on the rectangle moved by (-2, +2) pixels, then the string in white, whose texels replace the copy's where they overlap; the
/// quad then blends the whole bitmap once, modulated by the control colour.
/// </summary>
// [BIN:TPW-EXE:0x6b24b0 text texel] a glyph sample of coverage c writes (colour RGB, alpha 17 c) for the colour alpha 0xff (0x6b25df-0x6b25fb); for a lower colour alpha it writes alpha 17 x floor(c x alpha / 255) and nothing when that level is 0 (0x6b26d0-0x6b2702); the texels are written, not blended (the bitmap was cleared, 0x48fb64)
// [BIN:TPI-EXE:0x899070 text texel] Theme Park Inc's blitter has the same formula; its title copy has the colour alpha 0xc8 (0x5162d2-0x5162f4)
// [EXT:hidpi-glyphs] above scale 1 the glyph coverage is the smoothed atlas (<see cref="FontSmoothing"/>): the copy's alpha is then the smoothed coverage times the colour alpha / 255 (no stepped levels), the same replace rule
public sealed class UiTitleBitmap
{
	/// <summary>Most bitmaps kept; the least recently used is dropped past it (the renderer then deletes its GPU copy).</summary>
	public const int Capacity = 192;

	/// <summary>Pixels of the bitmap: <see cref="Width"/> x <see cref="Height"/>, straight RGBA, row 0 on top.</summary>
	public byte[] Rgba { get; }
	public int Width { get; }
	public int Height { get; }
	/// <summary>Bitmap pixels per text pixel (the smoothing factor of the atlas; 1 = the original's own pixels).</summary>
	public int Factor { get; }
	/// <summary>Left and top of the bitmap relative to the layout origin, in text pixels (the copy reaches two pixels left of the string's ink and two below it).</summary>
	public int Left { get; }
	public int Top { get; }
	/// <summary>Width and height in text pixels.</summary>
	public int TextWidth => Width / Factor;
	public int TextHeight => Height / Factor;
	/// <summary>Set when the cache dropped this bitmap; the renderer then deletes its GPU copy.</summary>
	public bool Evicted { get; private set; }

	private UiTitleBitmap( byte[] rgba, int width, int height, int factor, int left, int top )
	{
		Rgba = rgba;
		Width = width;
		Height = height;
		Factor = factor;
		Left = left;
		Top = top;
	}

	private readonly record struct Key( FontAtlas Atlas, int Scale, byte CopyAlpha, int Count, int Width, int Height, ulong Hash );

	private static readonly Dictionary<Key, (UiTitleBitmap Bitmap, LinkedListNode<Key> Node)> cache = new();
	private static readonly LinkedList<Key> recent = new();
	private static readonly object gate = new();

	/// <summary>The composed bitmap of <paramref name="layout"/> at text scale <paramref name="scale"/>, cached by atlas, string layout, copy alpha and scale; null when the layout draws nothing.</summary>
	public static UiTitleBitmap? Get( FontAtlas atlas, TextLayoutResult layout, byte copyAlpha, int scale )
	{
		var glyphs = layout.Glyphs;
		if ( glyphs.Count == 0 || copyAlpha == 0 )
			return null;
		scale = Math.Max( 1, scale );
		var hash = 14695981039346656037UL;
		for ( var index = 0; index < glyphs.Count; index++ )
		{
			var glyph = glyphs[index];
			hash = (hash ^ glyph.Character) * 1099511628211UL;
			hash = (hash ^ (uint)glyph.X) * 1099511628211UL;
			hash = (hash ^ (uint)glyph.Y) * 1099511628211UL;
		}
		var key = new Key( atlas, scale, copyAlpha, glyphs.Count, layout.Width, layout.Height, hash );
		lock ( gate )
		{
			if ( cache.TryGetValue( key, out var entry ) )
			{
				recent.Remove( entry.Node );
				recent.AddFirst( entry.Node );
				return entry.Bitmap;
			}
			var bitmap = Compose( atlas, layout, copyAlpha, FontSmoothing.Factor( atlas, scale ) );
			cache[key] = (bitmap, recent.AddFirst( key ));
			while ( cache.Count > Capacity )
			{
				var oldest = recent.Last!;
				recent.RemoveLast();
				cache[oldest.Value].Bitmap.Evicted = true;
				cache.Remove( oldest.Value );
			}
			return bitmap;
		}
	}

	/// <summary>Composes the bitmap of a layout at <paramref name="factor"/> bitmap pixels per text pixel (see <see cref="UiTitleBitmap"/>).</summary>
	public static UiTitleBitmap Compose( FontAtlas atlas, TextLayoutResult layout, byte copyAlpha, int factor )
	{
		factor = Math.Max( 1, factor );
		// [APPROX:UI-007] the original clips both passes to the title control's bitmap ([control+0xa8] x [control+0xaa]); this bitmap is the string's ink plus the copy, unclipped — evidence needed: the control box each title builder passes
		var offset = UiBatch.TitleShadowOffset;
		var left = layout.InkLeft - offset;
		var top = layout.InkTop;
		var width = (layout.InkRight - left) * factor;
		var height = (layout.InkBottom + offset - top) * factor;
		var rgba = new byte[width * height * 4];
		var coverage = FontSmoothing.Coverage( atlas, factor );
		// pass 1: the grey copy (128,128,128, colour alpha copyAlpha) on the string's rectangle moved by (-2, +2); pass 2: the white string; both only write texels that have a level above 0, and the second replaces the first
		for ( var pass = 0; pass < 2; pass++ )
		{
			for ( var index = 0; index < layout.Glyphs.Count; index++ )
			{
				var placed = layout.Glyphs[index];
				var glyph = placed.Glyph;
				var originX = (placed.X - left + (pass == 0 ? -offset : 0)) * factor;
				var originY = (placed.Y - top + (pass == 0 ? offset : 0)) * factor;
				for ( var row = 0; row < glyph.Height * factor; row++ )
				{
					var y = originY + row;
					if ( y < 0 || y >= height )
						continue;
					for ( var column = 0; column < glyph.Width * factor; column++ )
					{
						var x = originX + column;
						if ( x < 0 || x >= width )
							continue;
						var sample = coverage[(glyph.Y * factor + row) * atlas.Width * factor + glyph.X * factor + column];
						int alpha;
						if ( pass == 1 )
							alpha = sample;
						else if ( factor == 1 )
							alpha = OriginalTextTexel.Alpha( sample / 17, copyAlpha );
						else
							alpha = sample * copyAlpha / 255;
						if ( alpha <= 0 )
							continue;
						var at = (y * width + x) * 4;
						var grey = (byte)(pass == 0 ? 128 : 255);
						rgba[at] = grey;
						rgba[at + 1] = grey;
						rgba[at + 2] = grey;
						rgba[at + 3] = (byte)alpha;
					}
				}
			}
		}
		return new UiTitleBitmap( rgba, width, height, factor, left, top );
	}
}
