namespace OpenTPW.Hud;

/// <summary>The camcorder's static: the 320 x 240 noise tp.exe's <c>0x53E760</c> fills with black, white and grey 150 pixels.</summary>
internal static class RideStaticNoise
{
	public const int Width = 320, Height = 240;
	public const byte Black = 0, Grey = 1, White = 2;
	/// <summary>The grey pixel's level (0x96, 0x53E7A6).</summary>
	public const int GreyLevel = 150;

	/// <summary>
	/// Fills <paramref name="pixels"/> (<see cref="Width"/> x <see cref="Height"/>, row by row) with the noise: the generator walks the columns, and for each pixel one draw whose low two bits are 0 (a quarter of them)
	/// makes it white or grey by the low bit of a second draw, otherwise black.
	/// </summary>
	public static void Fill( byte[] pixels, uint seed )
	{
		var state = seed;
		for ( var x = 0; x < Width; x++ )
		{
			for ( var y = 0; y < Height; y++ )
			{
				var kind = Black;
				if ( (Next( ref state ) & 3) == 0 )
					kind = (Next( ref state ) & 1) != 0 ? White : Grey;
				pixels[y * Width + x] = kind;
			}
		}
	}

	/// <summary>The CRT's <c>rand</c> (<c>0x67B5C0</c>): the state times 214013 plus 2531011, bits 16 to 30.</summary>
	public static uint Next( ref uint state )
	{
		state = unchecked(state * 214013u + 2531011u);
		return (state >> 16) & 0x7FFF;
	}
}
