namespace OpenTPW;

/// <summary>
/// Which of a pool cell's eight neighbours are pool cells. Cell y grows toward the south, so north is (0, -1).
/// [EXT:pools-render] OpenTPW's own auto-tiling; the original game has no pools (docs/POOLS.md).
/// </summary>
[Flags]
public enum PoolNeighbours : byte
{
	None = 0,
	North = 1,
	NorthEast = 2,
	East = 4,
	SouthEast = 8,
	South = 16,
	SouthWest = 32,
	West = 64,
	NorthWest = 128,
	All = 255
}

/// <summary>
/// The rim of a pool cell from its 8-neighbour mask. A cell is cut into 3 x 3 tiles at the rim width (columns and rows 0 and 2 are the rim strips, 1 the middle); a tile is
/// water or coping. The middle is always water; an edge tile is coping when the neighbour on its side is missing; a corner tile is coping unless both neighbours at its corner and
/// the one diagonally between them are pool cells (so a missing diagonal makes an inner corner, two missing sides an outer corner). Every cell cuts at the same breaks, so
/// neighbouring cells share their tile edges and any shape joins without a seam (docs/POOLS.md).
/// </summary>
// [EXT:pools-render] OpenTPW's own auto-tiling; the original game has no pools
public static class PoolEdges
{
	/// <summary>Tile columns and rows per cell.</summary>
	public const int TilesPerSide = 3;

	/// <summary>The neighbour mask of cell (<paramref name="x"/>, <paramref name="y"/>) in the pool set decided by <paramref name="isPool"/>.</summary>
	public static PoolNeighbours MaskAt( Func<int, int, bool> isPool, int x, int y )
	{
		var mask = PoolNeighbours.None;
		if ( isPool( x, y - 1 ) ) mask |= PoolNeighbours.North;
		if ( isPool( x + 1, y - 1 ) ) mask |= PoolNeighbours.NorthEast;
		if ( isPool( x + 1, y ) ) mask |= PoolNeighbours.East;
		if ( isPool( x + 1, y + 1 ) ) mask |= PoolNeighbours.SouthEast;
		if ( isPool( x, y + 1 ) ) mask |= PoolNeighbours.South;
		if ( isPool( x - 1, y + 1 ) ) mask |= PoolNeighbours.SouthWest;
		if ( isPool( x - 1, y ) ) mask |= PoolNeighbours.West;
		if ( isPool( x - 1, y - 1 ) ) mask |= PoolNeighbours.NorthWest;
		return mask;
	}

	/// <summary>Whether tile (<paramref name="column"/>, <paramref name="row"/>) of a cell with <paramref name="mask"/> is coping (true) or water (false).</summary>
	public static bool IsCoping( PoolNeighbours mask, int column, int row )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( column );
		ArgumentOutOfRangeException.ThrowIfNegative( row );
		ArgumentOutOfRangeException.ThrowIfGreaterThan( column, 2 );
		ArgumentOutOfRangeException.ThrowIfGreaterThan( row, 2 );
		bool Has( PoolNeighbours flag ) => (mask & flag) != 0;
		return (column, row) switch
		{
			(1, 1) => false,
			(1, 0) => !Has( PoolNeighbours.North ),
			(1, 2) => !Has( PoolNeighbours.South ),
			(0, 1) => !Has( PoolNeighbours.West ),
			(2, 1) => !Has( PoolNeighbours.East ),
			(0, 0) => !(Has( PoolNeighbours.North ) && Has( PoolNeighbours.West ) && Has( PoolNeighbours.NorthWest )),
			(2, 0) => !(Has( PoolNeighbours.North ) && Has( PoolNeighbours.East ) && Has( PoolNeighbours.NorthEast )),
			(0, 2) => !(Has( PoolNeighbours.South ) && Has( PoolNeighbours.West ) && Has( PoolNeighbours.SouthWest )),
			_ => !(Has( PoolNeighbours.South ) && Has( PoolNeighbours.East ) && Has( PoolNeighbours.SouthEast ))
		};
	}

	/// <summary>The number of coping tiles of a cell with <paramref name="mask"/> (0 to 8).</summary>
	public static int CopingTileCount( PoolNeighbours mask )
	{
		var count = 0;
		for ( var row = 0; row < TilesPerSide; row++ )
			for ( var column = 0; column < TilesPerSide; column++ )
				count += IsCoping( mask, column, row ) ? 1 : 0;
		return count;
	}
}
