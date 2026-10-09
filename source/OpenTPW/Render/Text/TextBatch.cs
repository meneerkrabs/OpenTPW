using Veldrid;

namespace OpenTPW;

/// <summary>
/// A screen-space quad in framebuffer pixels (origin top-left). <see cref="Atlas"/> is null for a
/// solid rectangle; otherwise the quad samples <see cref="Width"/> × <see cref="Height"/> atlas
/// texels at <see cref="AtlasX"/>/<see cref="AtlasY"/> one-to-one.
/// </summary>
internal readonly record struct TextQuad( FontAtlas? Atlas, int X, int Y, int Width, int Height, int AtlasX, int AtlasY, RgbaByte Color );

/// <summary>
/// CPU list of text/rectangle quads in draw order. <see cref="TextRenderer"/> draws it on the GPU;
/// <see cref="Composite"/> is the matching CPU reference used to check GPU readback.
/// </summary>
internal sealed class TextBatch
{
	private readonly List<TextQuad> quads = new();

	public IReadOnlyList<TextQuad> Quads => quads;

	public void Clear() => quads.Clear();

	public void AddRectangle( int x, int y, int width, int height, RgbaByte color )
	{
		if ( width > 0 && height > 0 )
			quads.Add( new TextQuad( null, x, y, width, height, 0, 0, color ) );
	}

	public void AddText( FontAtlas atlas, TextLayoutResult layout, int x, int y, RgbaByte color )
	{
		foreach ( var glyph in layout.Glyphs )
			quads.Add( new TextQuad( atlas, x + glyph.X, y + glyph.Y, glyph.Glyph.Width, glyph.Glyph.Height, glyph.Glyph.X, glyph.Glyph.Y, color ) );
	}

	/// <summary>
	/// Blends all quads over a BGRA8 image with straight alpha, as the GPU pipeline does:
	/// <c>dst = src × a + dst × (1 − a)</c> where <c>a = color.A × atlas alpha</c> (the alpha
	/// channel itself becomes <c>a × a + dst × (1 − a)</c>, Veldrid's single alpha blend).
	/// </summary>
	public void Composite( byte[] bgra, int width, int height )
	{
		ArgumentNullException.ThrowIfNull( bgra );
		if ( bgra.Length != checked(width * height * 4) )
			throw new ArgumentException( "Image size does not match its dimensions.", nameof( bgra ) );
		foreach ( var quad in quads )
		{
			for ( var row = Math.Max( 0, -quad.Y ); row < quad.Height && quad.Y + row < height; row++ )
			{
				for ( var column = Math.Max( 0, -quad.X ); column < quad.Width && quad.X + column < width; column++ )
				{
					var coverage = quad.Atlas == null ? 255 : quad.Atlas.Alpha[(quad.AtlasY + row) * quad.Atlas.Width + quad.AtlasX + column];
					var alpha = quad.Color.A / 255f * (coverage / 255f);
					var pixel = ((quad.Y + row) * width + quad.X + column) * 4;
					bgra[pixel] = Blend( quad.Color.B, bgra[pixel], alpha );
					bgra[pixel + 1] = Blend( quad.Color.G, bgra[pixel + 1], alpha );
					bgra[pixel + 2] = Blend( quad.Color.R, bgra[pixel + 2], alpha );
					bgra[pixel + 3] = (byte)Math.Clamp( MathF.Round( 255 * alpha * alpha + bgra[pixel + 3] * (1 - alpha) ), 0, 255 );
				}
			}
		}
	}

	private static byte Blend( byte source, byte destination, float alpha ) =>
		(byte)Math.Clamp( MathF.Round( source * alpha + destination * (1 - alpha) ), 0, 255 );
}
