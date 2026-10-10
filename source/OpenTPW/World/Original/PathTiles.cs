namespace OpenTPW;

/// <summary>A path cell's texture: an index into the theme's <c>PathTex</c> list and a clockwise turn in degrees.</summary>
public readonly record struct PathTile( int TextureIndex, int Rotation );

/// <summary>
/// Chooses the path texture of a cell from its eight neighbours (docs/PATHS.md, "Path textures"). The neighbour
/// mask uses the save's connection-bit order: 0x01 −Y, 0x02 +X−Y, 0x04 +X, 0x08 +X+Y, 0x10 +Y, 0x20 −X+Y, 0x40 −X,
/// 0x80 −X−Y. A diagonal counts only when both sides next to it are set, which leaves the 15 shapes of the
/// theme's <c>PathTex</c> list (entries 0–15; 14 repeats 13) up to rotation.
/// </summary>
public static class PathTiles
{
	public const int StraightVariant = 19;
	public const int EdgeVariant = 20;
	/// <summary>A rotation value that leaves a quad's texture coordinates as they are (ground cells).</summary>
	public const int Identity = -1;

	/// <summary>Cell offset of each mask bit, in bit order.</summary>
	public static readonly (int DX, int DY)[] Offsets = { (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) };

	/// <summary>Each texture's open sides at rotation 0, as a neighbour mask.</summary>
	private static readonly (int Texture, byte Mask)[] Shapes =
	{
		(0, 0x00),  // squ: no neighbour
		(1, 0x10),  // end: +Y
		(2, 0x11),  // str: −Y, +Y
		(3, 0x14),  // cnr2: +X, +Y
		(4, 0x15),  // tju1: −Y, +X, +Y
		(5, 0x55),  // xrd1: all sides
		(6, 0x7D),  // tju2: all sides, +X+Y and −X+Y
		(7, 0x1D),  // tju3: −Y, +X, +Y and +X+Y
		(8, 0xD5),  // xrd2: all sides and −X−Y
		(9, 0x1C),  // cnr1: +X, +Y and +X+Y
		(10, 0x1F), // edg: −Y, +X, +Y and both diagonals between them
		(11, 0x7F), // icn1: all but −X−Y
		(12, 0x77), // icn2: all sides, +X−Y and −X+Y
		(13, 0xFF), // ctr: all
		(15, 0x71), // tju4: −Y, +Y, −X and −X+Y
	};

	private static readonly PathTile[] ByMask = BuildTable();

	/// <summary>Drops each diagonal whose two neighbouring sides are not both set.</summary>
	public static byte Reduce( byte neighbours )
	{
		var mask = neighbours & 0x55;
		for ( var side = 0; side < 4; side++ )
		{
			var diagonal = 2 << (2 * side);
			var next = 1 << (2 * ((side + 1) % 4));
			if ( (neighbours & diagonal) != 0 && (mask & (1 << (2 * side))) != 0 && (mask & next) != 0 )
				mask |= diagonal;
		}
		return (byte)mask;
	}

	/// <summary>Turns a neighbour mask a quarter clockwise (−Y to +X).</summary>
	public static byte Turn( byte mask ) => (byte)((mask << 2) | (mask >> 6));

	/// <summary>
	/// The texture and turn for <paramref name="neighbours"/>. Straights and edges have two textures; the cell
	/// position picks one so a rebuilt surface keeps its look.
	/// </summary>
	public static PathTile Choose( byte neighbours, int x, int y )
	{
		var tile = ByMask[Reduce( neighbours )];
		// [APPROX:PATH-014] str1/str2 and edg1/edg2 are picked by a hash of the cell, not by the original's choice (saved cells keep theirs) — evidence needed: the original routine that writes save cell byte +17
		var second = ((x * 73856093) ^ (y * 19349663)) & 1;
		return tile.TextureIndex switch
		{
			2 when second == 1 => tile with { TextureIndex = StraightVariant },
			10 when second == 1 => tile with { TextureIndex = EdgeVariant },
			_ => tile,
		};
	}

	/// <summary>The eight path neighbours of a cell as a mask; cells outside the <paramref name="countX"/> × <paramref name="countY"/> map are no path.</summary>
	public static byte Neighbours( int x, int y, int countX, int countY, Func<int, int, bool> isPath )
	{
		var mask = 0;
		for ( var bit = 0; bit < 8; bit++ )
		{
			var (nx, ny) = (x + Offsets[bit].DX, y + Offsets[bit].DY);
			if ( (uint)nx < (uint)countX && (uint)ny < (uint)countY && isPath( nx, ny ) )
				mask |= 1 << bit;
		}
		return (byte)mask;
	}

	/// <summary>
	/// The tile of path cell (<paramref name="x"/>, <paramref name="y"/>). A cell that was a path in the original save,
	/// with the same path neighbours, keeps the saved texture and turn: the saved neighbour bits also count ride
	/// entrances, which the cell map does not keep. Other cells follow <see cref="Choose"/>.
	/// </summary>
	public static PathTile ForCell( int x, int y, int countX, int countY, Func<int, int, bool> isPath, SaveCellGrid? saved )
	{
		var neighbours = Neighbours( x, y, countX, countY, isPath );
		if ( saved != null && saved[x, y].IsPath && Neighbours( x, y, countX, countY, ( nx, ny ) => saved[nx, ny].IsPath ) == neighbours
			&& saved[x, y].PathRotation is 0 or 90 or 180 or 270 )
			return new PathTile( saved[x, y].PathTexture, saved[x, y].PathRotation );
		return Choose( neighbours, x, y );
	}

	/// <summary>
	/// Texture coordinates for the cell corner (<paramref name="fx"/>, <paramref name="fy"/>) ∈ [0, 1]² of a path tile
	/// turned <paramref name="rotation"/> degrees clockwise. The textures have +X to the right and +Y at the top row.
	/// </summary>
	public static (float U, float V) TextureCoordinates( float fx, float fy, int rotation )
	{
		var (dx, dy) = (fx - 0.5f, fy - 0.5f);
		for ( var quarter = (rotation / 90 % 4 + 4) % 4; quarter > 0; quarter-- )
			(dx, dy) = (dy, -dx);
		return (dx + 0.5f, 0.5f - dy);
	}

	/// <summary>The shape an index of the theme's <c>PathTex</c> list draws (variants and the duplicate centre map to their base).</summary>
	public static int BaseTexture( int textureIndex ) => textureIndex switch
	{
		StraightVariant => 2,
		EdgeVariant => 10,
		14 => 13,
		_ => textureIndex,
	};

	private static PathTile[] BuildTable()
	{
		var table = new PathTile?[256];
		foreach ( var (texture, mask) in Shapes )
		{
			var turned = mask;
			for ( var quarter = 0; quarter < 4; quarter++ )
			{
				table[turned] ??= new PathTile( texture, quarter * 90 );
				turned = Turn( turned );
			}
		}
		var result = new PathTile[256];
		for ( var neighbours = 0; neighbours < 256; neighbours++ )
			result[neighbours] = table[Reduce( (byte)neighbours )] ?? throw new InvalidOperationException( $"No path shape for mask 0x{neighbours:X2}." );
		return result;
	}
}
