namespace OpenTPW.UI.Original;

/// <summary>
/// Coverage of a BF4 atlas magnified for text drawn at an integer scale above 1 (HiDPI and large
/// outputs). Nearest-neighbour doubling turns the fonts' anti-aliased edges into staircases, so each
/// glyph is enlarged on its own (Catmull-Rom, nothing outside its rectangle) and the edge is
/// tightened again with a contrast curve. The result keeps the atlas layout times the factor, so
/// glyph texture coordinates stay valid. At scale 1 the original coverage is used unchanged.
/// </summary>
public static class FontSmoothing
{
	/// <summary>Largest smoothed atlas side; larger requests use a smaller factor (sampled linearly).</summary>
	public const int MaximumSize = 4096;

	// [APPROX:UI-043] text drawn above 1× is magnified per glyph (Catmull-Rom, contrast min(2, 0.8 × factor)) instead of doubling pixels — OpenTPW choice; the original only drew its fonts at 1×
	public static float Contrast( int factor ) => Math.Min( 2f, 0.8f * factor );

	/// <summary>The factor the atlas is enlarged by for text drawn at <paramref name="scale"/> (1 = unchanged).</summary>
	public static int Factor( FontAtlas atlas, int scale )
	{
		var factor = Math.Max( 1, scale );
		while ( factor > 1 && Math.Max( atlas.Width, atlas.Height ) * factor > MaximumSize )
			factor--;
		return factor;
	}

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FontAtlas, Dictionary<int, byte[]>> cache = new();

	/// <summary>
	/// Row-major alpha of (<see cref="FontAtlas.Width"/> × factor) × (<see cref="FontAtlas.Height"/> × factor)
	/// for <paramref name="factor"/> from <see cref="Factor"/>; cached per atlas.
	/// </summary>
	public static byte[] Coverage( FontAtlas atlas, int factor )
	{
		if ( factor <= 1 )
			return atlas.Alpha;
		var byFactor = cache.GetOrCreateValue( atlas );
		lock ( byFactor )
		{
			if ( !byFactor.TryGetValue( factor, out var coverage ) )
				byFactor[factor] = coverage = Build( atlas, factor );
			return coverage;
		}
	}

	private static byte[] Build( FontAtlas atlas, int factor )
	{
		var width = atlas.Width * factor;
		var result = new byte[width * atlas.Height * factor];
		var contrast = Contrast( factor );
		foreach ( var glyph in atlas.Glyphs.Values )
		{
			if ( glyph.Width <= 0 || glyph.Height <= 0 )
				continue;
			float Source( int x, int y ) => x < 0 || y < 0 || x >= glyph.Width || y >= glyph.Height
				? 0f
				: atlas.Alpha[(glyph.Y + y) * atlas.Width + glyph.X + x] / 255f;

			// Horizontal pass into glyph.Width × factor columns, then vertical.
			var outWidth = glyph.Width * factor;
			var outHeight = glyph.Height * factor;
			var rows = new float[outWidth * glyph.Height];
			for ( var y = 0; y < glyph.Height; y++ )
			{
				for ( var x = 0; x < outWidth; x++ )
				{
					var (first, w0, w1, w2, w3) = Taps( x, factor );
					rows[y * outWidth + x] = Source( first, y ) * w0 + Source( first + 1, y ) * w1 + Source( first + 2, y ) * w2 + Source( first + 3, y ) * w3;
				}
			}
			float Row( int x, int y ) => y < 0 || y >= glyph.Height ? 0f : rows[y * outWidth + x];
			for ( var y = 0; y < outHeight; y++ )
			{
				var (first, w0, w1, w2, w3) = Taps( y, factor );
				for ( var x = 0; x < outWidth; x++ )
				{
					var value = Row( x, first ) * w0 + Row( x, first + 1 ) * w1 + Row( x, first + 2 ) * w2 + Row( x, first + 3 ) * w3;
					value = (value - 0.5f) * contrast + 0.5f;
					result[(glyph.Y * factor + y) * width + glyph.X * factor + x] = (byte)Math.Clamp( MathF.Round( value * 255f ), 0f, 255f );
				}
			}
		}
		return result;
	}

	/// <summary>First source index and Catmull-Rom weights for output pixel <paramref name="index"/> (pixel centres).</summary>
	private static (int First, float W0, float W1, float W2, float W3) Taps( int index, int factor )
	{
		var position = (index + 0.5f) / factor - 0.5f;
		var floor = (int)MathF.Floor( position );
		var t = position - floor;
		var t2 = t * t;
		var t3 = t2 * t;
		return (floor - 1,
			-0.5f * t3 + t2 - 0.5f * t,
			1.5f * t3 - 2.5f * t2 + 1f,
			-1.5f * t3 + 2f * t2 + 0.5f * t,
			0.5f * t3 - 0.5f * t2);
	}
}
