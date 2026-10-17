namespace OpenTPW;

/// <summary>
/// The pool's textures, generated in code (docs/POOLS.md): nothing of the original game is used or shipped. Each is a square RGBA image that tiles seamlessly, so the mesh can repeat it
/// over any area. The coping and the wall are 4 x 4 slabs of 16 pixels with a one pixel joint and a slight bevel (a light edge on two sides, a shadow on the other two), drawn
/// from a <see cref="PoolStyle"/>. Deterministic: the same bytes every time.
/// </summary>
// [EXT:pools-render] OpenTPW's own pool art; the original game has no pools
public static class PoolTextures
{
	/// <summary>Pixels per side of every pool texture.</summary>
	public const int Size = 64;
	/// <summary>Pixels per side of one slab.</summary>
	public const int SlabPixels = 16;
	/// <summary>Slabs per side of a texture.</summary>
	public const int SlabsPerSide = Size / SlabPixels;

	/// <summary>The coping of <paramref name="style"/>: its pattern, joints and bevels.</summary>
	public static byte[] Coping( PoolStyle style ) => Slabs( ( slabX, slabY, x, y ) => style.Coping switch
	{
		PoolCopingPattern.Planks => Plank( style, slabX, slabY, x, y ),
		PoolCopingPattern.MossyStone => Mossy( style, slabX, slabY, x, y ),
		PoolCopingPattern.RivetedPanels => Riveted( style, slabX, slabY, x, y ),
		PoolCopingPattern.CandyTiles => Candy( style, slabX, slabY ),
		_ => Speckled( style.CopingBase, slabX, slabY, x, y, 10 )
	}, style.CopingJoint, bevel: true );

	/// <summary>The tiles of the pool wall and floor of <paramref name="style"/>.</summary>
	public static byte[] Wall( PoolStyle style ) => Slabs( ( slabX, slabY, x, y ) => Speckled( style.Wall, slabX, slabY, x, y, 7 ), style.WallJoint, bevel: true );

	/// <summary>
	/// The water: soft caustic light in the red channel (the water shader mixes its deep and shallow colours by it), the same for every style. Built from sine waves with whole
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

	private static byte[] Slabs( Func<int, int, int, int, PoolColour> body, PoolColour joint, bool bevel )
	{
		var pixels = new byte[Size * Size * 4];
		for ( var py = 0; py < Size; py++ )
		{
			for ( var px = 0; px < Size; px++ )
			{
				int slabX = px / SlabPixels, slabY = py / SlabPixels, x = px % SlabPixels, y = py % SlabPixels;
				PoolColour colour;
				if ( x == 0 || y == 0 )
					colour = joint;
				else
				{
					colour = body( slabX, slabY, x, y );
					if ( bevel )
					{
						// a slight bevel: light along the top and left edges inside the joint, shadow along the bottom and right
						if ( x == 1 || y == 1 )
							colour = colour.Mixed( new PoolColour( 255, 255, 255 ), 0.22f );
						else if ( x == SlabPixels - 1 || y == SlabPixels - 1 )
							colour = colour.Scaled( 0.72f );
						else if ( x == SlabPixels - 2 || y == SlabPixels - 2 )
							colour = colour.Scaled( 0.88f );
					}
				}
				var offset = (py * Size + px) * 4;
				pixels[offset] = (byte)colour.R;
				pixels[offset + 1] = (byte)colour.G;
				pixels[offset + 2] = (byte)colour.B;
				pixels[offset + 3] = 255;
			}
		}
		return pixels;
	}

	/// <summary>A slab shade: a fixed hash of the slab and the pixel gives each slab its own tone and the surface a little grain.</summary>
	private static PoolColour Speckled( PoolColour colour, int slabX, int slabY, int x, int y, int amount )
	{
		var slab = (int)(Hash( (uint)(slabX * 7 + slabY * 13 + 1) ) % (uint)(2 * amount + 1)) - amount;
		var grain = (int)(Hash( (uint)(((slabX * SlabPixels + x) * 73856093) ^ ((slabY * SlabPixels + y) * 19349663)) ) % 5u) - 2;
		return Shift( colour, slab + grain );
	}

	private static PoolColour Plank( PoolStyle style, int slabX, int slabY, int x, int y )
	{
		// the grain runs along the slab; a plank is a whole slab with its own tone, every other row turned lighter
		var tone = (int)(Hash( (uint)(slabX * 31 + slabY * 17 + 5) ) % 17u) - 8;
		var line = Math.Sin( y * 1.7 + Hash( (uint)(slabX + slabY * 4 + 9) ) % 7 + x * 0.12 );
		var colour = (slabY & 1) == 0 ? style.CopingBase : style.CopingAccent.Mixed( style.CopingBase, 0.35f );
		return Shift( colour, tone + (int)(line * 7) );
	}

	private static PoolColour Mossy( PoolStyle style, int slabX, int slabY, int x, int y )
	{
		var stone = Speckled( style.CopingBase, slabX, slabY, x, y, 8 );
		// moss collects in patches that follow a smooth field repeating across the image
		double u = (slabX * SlabPixels + x) / (double)Size * 2 * Math.PI, v = (slabY * SlabPixels + y) / (double)Size * 2 * Math.PI;
		var field = Math.Sin( 3 * u + Math.Sin( 2 * v ) ) * Math.Sin( 2 * v + 1 ) + 0.5 * Math.Sin( 5 * u - 4 * v );
		var moss = (float)Math.Clamp( (field - 0.15) * 1.6, 0, 0.85 );
		return stone.Mixed( Shift( style.CopingAccent, (int)(field * 12) ), moss );
	}

	private static PoolColour Riveted( PoolStyle style, int slabX, int slabY, int x, int y )
	{
		var panel = Shift( style.CopingBase, (int)(Hash( (uint)(slabX * 5 + slabY * 11) ) % 9u) - 4 + (y % 2 == 0 ? 2 : 0) );
		foreach ( var (cx, cy) in new[] { (4, 4), (11, 4), (4, 11), (11, 11) } )
		{
			var distance = Math.Sqrt( (x - cx) * (x - cx) + (y - cy) * (y - cy) );
			if ( distance <= 1.5 )
				return x + y <= cx + cy ? style.CopingAccent : style.CopingAccent.Scaled( 0.7f );
			if ( distance <= 2.4 )
				return panel.Scaled( 0.6f );
		}
		return panel;
	}

	private static PoolColour Candy( PoolStyle style, int slabX, int slabY )
	{
		var white = new PoolColour( 255, 250, 252 );
		return ((slabX + slabY) % 3) switch
		{
			0 => style.CopingBase,
			1 => style.CopingAccent,
			_ => style.CopingBase.Mixed( white, 0.55f )
		};
	}

	private static PoolColour Shift( PoolColour colour, int amount ) =>
		new( Math.Clamp( colour.R + amount, 0, 255 ), Math.Clamp( colour.G + amount, 0, 255 ), Math.Clamp( colour.B + amount, 0, 255 ) );

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
