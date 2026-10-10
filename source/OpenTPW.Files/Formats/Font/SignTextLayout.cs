namespace OpenTPW;

/// <summary>Size of a laid-out sign string in pixels; <see cref="MissingCodePoints"/> were drawn with .notdef.</summary>
public sealed record SignTextMetrics( float Width, float Ascent, float Descent, int GlyphCount, IReadOnlyList<int> MissingCodePoints );

/// <summary>Where text sits inside a sign canvas.</summary>
public enum SignTextAlignment
{
	Center,
	Left,
	Right
}

/// <summary>
/// CPU layout and rasterization of one line of sign text with a <see cref="TrueTypeFont"/>, following
/// GDI conventions: a negative LOGFONT height is the em height,
/// advance widths come from <c>hmtx</c>; pair kerning from <c>kern</c> is opt-in (GDI TextOut does not kern;
/// [APPROX:COMPAT-006]). Characters without a glyph
/// use glyph 0 (.notdef), as GDI substitutes the default character, and are reported.
/// </summary>
public static class SignTextLayout
{
	public static IReadOnlyList<int> CodePoints( string text )
	{
		var result = new List<int>( text.Length );
		for ( var i = 0; i < text.Length; i++ )
		{
			if ( char.IsHighSurrogate( text[i] ) && i + 1 < text.Length && char.IsLowSurrogate( text[i + 1] ) )
			{
				result.Add( char.ConvertToUtf32( text[i], text[i + 1] ) );
				i++;
			}
			else
				result.Add( text[i] );
		}
		return result;
	}

	/// <summary>Code points of <paramref name="text"/> that the font has no glyph for (spaces and controls excluded).</summary>
	public static IReadOnlyList<int> FindMissing( TrueTypeFont font, string text ) =>
		CodePoints( text ).Where( code => code > ' ' && !char.IsControl( (char)Math.Min( code, 0xFFFF ) ) && !font.HasGlyph( code ) ).Distinct().ToArray();

	private static List<(int Glyph, float PenX)> Place( TrueTypeFont font, string text, bool kerning, out float advance, out List<int> missing )
	{
		var placed = new List<(int Glyph, float PenX)>();
		missing = new List<int>();
		var pen = 0f;
		var previous = -1;
		foreach ( var code in CodePoints( text ) )
		{
			if ( code is '\r' or '\n' )
				continue;
			var glyph = font.GetGlyphIndex( code );
			if ( glyph == 0 && code > ' ' && !missing.Contains( code ) )
				missing.Add( code );
			if ( kerning && previous >= 0 )
				pen += font.GetKerning( previous, glyph );
			placed.Add( (glyph, pen) );
			pen += font.GetMetrics( glyph ).AdvanceWidth;
			previous = glyph;
		}
		advance = pen;
		return placed;
	}

	public static SignTextMetrics Measure( TrueTypeFont font, string text, float emPixels, float horizontalScale = 1, bool kerning = false )
	{
		var placed = Place( font, text, kerning, out var advance, out var missing );
		var scale = font.ScaleForEmHeight( emPixels );
		return new SignTextMetrics( advance * scale * horizontalScale, font.WindowsAscent * scale, font.WindowsDescent * scale, placed.Count, missing );
	}

	/// <summary>
	/// Rasterizes one line. <paramref name="emPixels"/> is the em height (GDI negative LOGFONT
	/// height); <paramref name="horizontalScale"/> condenses or expands glyphs.
	/// </summary>
	/// <summary>
	/// The horizontal scale a LOGFONT width selects: GDI scales a TrueType font so that its average
	/// character width (OS/2 xAvgCharWidth) equals <paramref name="logFontWidth"/>; 0 keeps the design aspect.
	/// </summary>
	// [APPROX:COMPAT-002] lfWidth is applied with the Win32 rule (average character width = lfWidth) and fractional advances; the Mac GDI layer's rounding is not verified — evidence needed: long-name captures
	public static float LogFontHorizontalScale( TrueTypeFont font, float emPixels, int logFontWidth ) =>
		logFontWidth <= 0 || font.AverageCharWidth <= 0 ? 1 : logFontWidth / (font.AverageCharWidth * font.ScaleForEmHeight( emPixels ));

	/// <summary>
	/// The LOGFONT width a sign line is drawn with: unchanged while the text is narrower than
	/// <paramref name="canvasWidth"/>, otherwise the result of the original's bisection over the width
	/// (the height never changes).
	/// </summary>
	// [BIN:STP-PPC:0x100A9F9C sign line] fit when the extent is below the 2x canvas width; else lo = 1, hi = lfWidth (or -lfHeight when 0), mid = (hi - 1) / 2 + 1; a fitting trial sets lo = mid, mid += (hi - mid) / 2, a wide one sets hi = mid, mid = lo + (mid - lo) / 2; stop on the first fitting trial with hi - lo < 3
	public static int FitLogFontWidth( TrueTypeFont font, string text, float emPixels, int logFontWidth, int canvasWidth, bool kerning = false )
	{
		float Extent( int width ) => Measure( font, text, emPixels, LogFontHorizontalScale( font, emPixels, width ), kerning ).Width;
		if ( Extent( logFontWidth ) < canvasWidth )
			return logFontWidth;
		var low = 1;
		var high = logFontWidth != 0 ? logFontWidth : (int)MathF.Round( emPixels );
		var middle = ((high - 1) >> 1) + 1;
		// The original loops forever when even width 1 does not fit; stop there instead.
		for ( var step = 0; step < 64; step++ )
		{
			var fits = Extent( middle ) < canvasWidth;
			if ( fits && high - low < 3 )
				return middle;
			if ( fits )
			{
				low = middle;
				middle += (high - middle) >> 1;
			}
			else
			{
				high = middle;
				middle = low + ((middle - low) >> 1);
			}
		}
		return middle;
	}

	public static CoverageBitmap Rasterize( TrueTypeFont font, string text, float emPixels, float horizontalScale = 1, bool kerning = false )
	{
		if ( !(emPixels > 0) || !float.IsFinite( emPixels ) )
			throw new ArgumentOutOfRangeException( nameof( emPixels ), "The em height must be positive." );
		var placed = Place( font, text, kerning, out _, out _ );
		var scale = font.ScaleForEmHeight( emPixels );
		return TrueTypeRasterizer.Rasterize( placed.Select( item => (font.GetOutline( item.Glyph ), item.PenX) ), scale * horizontalScale, scale );
	}

	/// <summary>
	/// Renders one line into a tight RGBA image (straight alpha, rows top to bottom) of
	/// <paramref name="color"/> with coverage as alpha, plus <paramref name="padding"/> transparent
	/// pixels on every side. <c>BaselineY</c> is the baseline row inside the image.
	/// </summary>
	public static (byte[] Rgba, int Width, int Height, int BaselineY) RenderRgba( TrueTypeFont font, string text, float emPixels, (byte R, byte G, byte B) color, float horizontalScale = 1, int padding = 2, bool kerning = false )
	{
		padding = Math.Max( 0, padding );
		var bitmap = Rasterize( font, text, emPixels, horizontalScale, kerning );
		var width = Math.Max( 1, bitmap.Width + padding * 2 );
		var height = Math.Max( 1, bitmap.Height + padding * 2 );
		var rgba = new byte[width * height * 4];
		for ( var y = 0; y < bitmap.Height; y++ )
		{
			for ( var x = 0; x < bitmap.Width; x++ )
			{
				var index = ((y + padding) * width + x + padding) * 4;
				rgba[index] = color.R;
				rgba[index + 1] = color.G;
				rgba[index + 2] = color.B;
				rgba[index + 3] = bitmap[x, y];
			}
		}
		return (rgba, width, height, bitmap.OriginY + padding);
	}

	/// <summary>
	/// Draws one line into an RGBA canvas (rows top to bottom, straight alpha), shrinking the em
	/// height when the text would not fit the canvas width minus <paramref name="margin"/>. The line
	/// is centred vertically on <paramref name="centerY"/> (canvas centre when null) using the font's
	/// Windows ascent/descent, or, when <paramref name="cellTop"/> is given, placed with the top of its
	/// character cell at that row (GDI <c>TA_TOP</c>, the TextOut default). Returns the em height used.
	/// </summary>
	public static float DrawLine( byte[] rgba, int width, int height, TrueTypeFont font, string text, float emPixels, (byte R, byte G, byte B) color,
		SignTextAlignment alignment = SignTextAlignment.Center, float horizontalScale = 1, float? centerY = null, int margin = 8, bool kerning = false, float? cellTop = null )
	{
		ArgumentNullException.ThrowIfNull( rgba );
		if ( rgba.Length != width * height * 4 )
			throw new ArgumentException( "Canvas size does not match its dimensions.", nameof( rgba ) );
		if ( string.IsNullOrEmpty( text ) )
			return emPixels;
		var metrics = Measure( font, text, emPixels, horizontalScale, kerning );
		var available = Math.Max( 1, width - 2 * margin );
		if ( metrics.Width > available )
			emPixels *= available / metrics.Width;
		var lineHeight = (font.WindowsAscent + font.WindowsDescent) * font.ScaleForEmHeight( emPixels );
		if ( cellTop == null && lineHeight > height - 2 * margin )
			emPixels *= Math.Max( 1, height - 2 * margin ) / lineHeight;
		var bitmap = Rasterize( font, text, emPixels, horizontalScale, kerning );
		if ( bitmap.Width == 0 )
			return emPixels;
		var scale = font.ScaleForEmHeight( emPixels );
		var ascent = font.WindowsAscent * scale;
		var descent = font.WindowsDescent * scale;
		var baseline = cellTop is { } top0 ? top0 + ascent : (centerY ?? height / 2f) + (ascent - descent) / 2;
		var advance = Measure( font, text, emPixels, horizontalScale, kerning ).Width;
		var penX = alignment switch
		{
			SignTextAlignment.Left => margin,
			SignTextAlignment.Right => width - margin - advance,
			_ => (width - advance) / 2
		};
		var left = (int)MathF.Round( penX ) - bitmap.OriginX;
		var top = (int)MathF.Round( baseline ) - bitmap.OriginY;
		for ( var y = 0; y < bitmap.Height; y++ )
		{
			var canvasY = top + y;
			if ( canvasY < 0 || canvasY >= height )
				continue;
			for ( var x = 0; x < bitmap.Width; x++ )
			{
				var canvasX = left + x;
				var alpha = bitmap[x, y];
				if ( alpha == 0 || canvasX < 0 || canvasX >= width )
					continue;
				var index = (canvasY * width + canvasX) * 4;
				// Source-over with straight alpha.
				var source = alpha / 255f;
				var destinationAlpha = rgba[index + 3] / 255f;
				var outAlpha = source + destinationAlpha * (1 - source);
				rgba[index] = Blend( color.R, rgba[index], source, destinationAlpha, outAlpha );
				rgba[index + 1] = Blend( color.G, rgba[index + 1], source, destinationAlpha, outAlpha );
				rgba[index + 2] = Blend( color.B, rgba[index + 2], source, destinationAlpha, outAlpha );
				rgba[index + 3] = (byte)MathF.Round( outAlpha * 255 );
			}
		}
		return emPixels;
	}

	private static byte Blend( byte source, byte destination, float sourceAlpha, float destinationAlpha, float outAlpha ) =>
		outAlpha <= 0 ? (byte)0 : (byte)Math.Clamp( MathF.Round( (source * sourceAlpha + destination * destinationAlpha * (1 - sourceAlpha)) / outAlpha ), 0, 255 );
}
