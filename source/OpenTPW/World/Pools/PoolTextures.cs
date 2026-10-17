namespace OpenTPW;

/// <summary>
/// The pool's textures, generated in code (docs/POOLS.md): nothing of the original game is used or shipped. Each is a square RGBA image that tiles seamlessly, so the
/// mesh can repeat it over any number of cells. Deterministic: the same bytes every time.
/// </summary>
// [EXT:pools-render] OpenTPW's own pool art; the original game has no pools
public static class PoolTextures
{
	/// <summary>Pixels per side of every pool texture.</summary>
	public const int Size = 64;

	/// <summary>Pixels per side of one tile of the coping and the wall (a cell shows 4 x 4 of them).</summary>
	private const int TilePixels = 16;

	/// <summary>The coping: pale stone slabs with darker joints.</summary>
	public static byte[] Coping() => Tiles( stone: (226, 222, 212), joint: (176, 180, 182), speckle: 10 );

	/// <summary>The pool wall below the coping: light blue ceramic tiles with pale joints.</summary>
	public static byte[] Wall() => Tiles( stone: (92, 176, 206), joint: (214, 232, 238), speckle: 6 );

	/// <summary>
	/// The water: soft caustic light, brightest in the red channel (the water shader maps that channel between its deep and its shallow blue). Built from sine waves with whole
	/// periods across the image, so its edges meet.
	/// </summary>
	public static byte[] Water()
	{
		var pixels = new byte[Size * Size * 4];
		for ( var y = 0; y < Size; y++ )
		{
			for ( var x = 0; x < Size; x++ )
			{
				double u = x / (double)Size * 2 * Math.PI, v = y / (double)Size * 2 * Math.PI;
				var wave = Math.Sin( 2 * u + Math.Sin( v ) ) * Math.Sin( 3 * v - Math.Sin( u ) ) + Math.Sin( u + v ) * 0.5;
				var light = Math.Clamp( 0.5 + wave * 0.28, 0, 1 );
				// bright thin caustic lines where the waves cross
				light = Math.Clamp( light + Math.Pow( Math.Max( 0, 1 - Math.Abs( wave ) * 2.5 ), 3 ) * 0.25, 0, 1 );
				var value = (byte)Math.Round( light * 255 );
				var offset = (y * Size + x) * 4;
				pixels[offset] = value;
				pixels[offset + 1] = value;
				pixels[offset + 2] = 255;
				pixels[offset + 3] = 255;
			}
		}
		return pixels;
	}

	private static byte[] Tiles( (int R, int G, int B) stone, (int R, int G, int B) joint, int speckle )
	{
		var pixels = new byte[Size * Size * 4];
		for ( var y = 0; y < Size; y++ )
		{
			for ( var x = 0; x < Size; x++ )
			{
				// a one pixel joint on the tile's left and top edges: the joint of the last tile meets the first tile's, so the image tiles
				var onJoint = x % TilePixels == 0 || y % TilePixels == 0;
				var colour = onJoint ? joint : stone;
				// a fixed hash of the pixel gives each tile a slightly different shade (the same shade at (x, y) and at (x + 64, y): the hash reads the position inside the image)
				var tile = (x / TilePixels) * 7 + (y / TilePixels) * 13;
				var shade = onJoint ? 0 : (int)(Hash( (uint)tile ) % (uint)(2 * speckle + 1)) - speckle;
				var offset = (y * Size + x) * 4;
				pixels[offset] = (byte)Math.Clamp( colour.R + shade, 0, 255 );
				pixels[offset + 1] = (byte)Math.Clamp( colour.G + shade, 0, 255 );
				pixels[offset + 2] = (byte)Math.Clamp( colour.B + shade, 0, 255 );
				pixels[offset + 3] = 255;
			}
		}
		return pixels;
	}

	private static uint Hash( uint value )
	{
		value ^= value >> 16;
		value *= 0x7feb352d;
		value ^= value >> 15;
		value *= 0x846ca68b;
		value ^= value >> 16;
		return value;
	}
}
