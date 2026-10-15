namespace OpenTPW;

/// <summary>
/// Resizes 8-bit RGBA buffers by averaging the source pixels that fall in each destination cell.
/// Colour is weighted by alpha so transparent texels do not darken the edges of a thumbnail.
/// </summary>
public static class RgbaResampler
{
	public static byte[] Resize( ReadOnlySpan<byte> pixels, int width, int height, int targetWidth, int targetHeight )
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( width );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( height );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( targetWidth );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( targetHeight );
		if ( pixels.Length != checked(width * height * 4) )
			throw new ArgumentException( $"Expected {width * height * 4} bytes for {width}x{height} pixels, got {pixels.Length}.", nameof( pixels ) );

		var result = new byte[checked(targetWidth * targetHeight * 4)];
		for ( var ty = 0; ty < targetHeight; ++ty )
		{
			var (y0, y1) = SourceSpan( ty, height, targetHeight );
			for ( var tx = 0; tx < targetWidth; ++tx )
			{
				var (x0, x1) = SourceSpan( tx, width, targetWidth );
				long r = 0, g = 0, b = 0, a = 0, plainR = 0, plainG = 0, plainB = 0;
				for ( var y = y0; y < y1; ++y )
				{
					for ( var x = x0; x < x1; ++x )
					{
						var i = (y * width + x) * 4;
						var alpha = pixels[i + 3];
						r += pixels[i] * alpha;
						g += pixels[i + 1] * alpha;
						b += pixels[i + 2] * alpha;
						a += alpha;
						plainR += pixels[i];
						plainG += pixels[i + 1];
						plainB += pixels[i + 2];
					}
				}

				var count = (x1 - x0) * (y1 - y0);
				var o = (ty * targetWidth + tx) * 4;
				if ( a > 0 )
				{
					result[o] = (byte)((r + a / 2) / a);
					result[o + 1] = (byte)((g + a / 2) / a);
					result[o + 2] = (byte)((b + a / 2) / a);
				}
				else
				{
					// Fully transparent cell: keep the plain average so the colour is still defined.
					result[o] = (byte)((plainR + count / 2) / count);
					result[o + 1] = (byte)((plainG + count / 2) / count);
					result[o + 2] = (byte)((plainB + count / 2) / count);
				}
				result[o + 3] = (byte)((a + count / 2) / count);
			}
		}
		return result;
	}

	/// <summary>Source range covered by one destination pixel; at least one pixel wide so upscaling repeats.</summary>
	private static (int Start, int End) SourceSpan( int target, int sourceSize, int targetSize )
	{
		var start = (int)((long)target * sourceSize / targetSize);
		var end = (int)((long)(target + 1) * sourceSize / targetSize);
		return (start, Math.Max( end, start + 1 ));
	}
}
