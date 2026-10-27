namespace OpenTPW;

/// <summary>
/// What a link write reports besides the links themselves: the tile refreshes (<c>0x5365D0</c>) it asks for and the
/// queue-neighbour sound. Both are presentation; a null callback skips it.
/// </summary>
/// <param name="Refresh">A cell whose tile the original recomputes after the write (its chooser runs again).</param>
/// <param name="QueueNeighbour">A laid path cell beside a queue cell that links back to it: the original plays <c>cat_ui</c> 139 for it.</param>
/// <param name="EntranceNeighbour">A path cell's clear took its link off an entrance cell (type 9) that carries an owner word: the owner word of that entrance, whose ride the original re-evaluates (<c>0x4DE1F0</c> at <c>0x53694B</c>).</param>
public readonly record struct LinkEffects( Action<int, int>? Refresh = null, Action<int, int>? QueueNeighbour = null, Action<ushort>? EntranceNeighbour = null );

/// <summary>
/// tp.exe's cell link writer <c>0x5348D0</c> and the unlinking loops of the cell clear <c>0x5367A0</c>, over the link byte
/// (map cell <c>+0x0C</c>): the cardinal bits 1 (−Y), 4 (+X), 16 (+Y), 64 (−X) and the diagonal bits 2 (+X−Y), 8 (+X+Y),
/// 0x20 (−X+Y), 0x80 (−X−Y), in <see cref="PathTiles.Offsets"/> order. Mode 1 links a path cell to its neighbours; mode 3
/// links a queue cell (or the path cell a queue line ends on) to the cell before it on the line.
/// </summary>
// [BIN:TPW-EXE:0x004D9A60 link vectors] the static initialisers fill the vector table 0x7CDBA0 with (0, −1), 0x7CDBA8 (0, 1), 0x7CDBB0 (−1, 1), 0x7CDBB8 (1, 1), 0x7CDBC0 (−1, −1), 0x7CDBC8 (1, 0), 0x7CDBD0 (−1, 0), 0x7CDBD8 (1, −1); 0x4D96F0 returns the cell at an offset (0 outside 0..127), 0x4D97E0 the neighbour along a link bit, 0x522700 ORs a bit into +0x0C, 0x522730 clears one, 0x522790 counts the set bits of all eight, 0x522850 reads the queue link +0x0D
public static class PathLinkWriter
{
	/// <summary>The link bit toward the cardinal <see cref="GuestPathGrid.Directions"/> index <paramref name="direction"/> (1, 4, 16, 64).</summary>
	private static byte Cardinal( int direction ) => GuestPathGrid.LinkValue( direction & 3 );

	/// <summary>The diagonal bit between cardinal <paramref name="direction"/> and the next one clockwise (2, 8, 0x20, 0x80).</summary>
	private static byte Diagonal( int direction ) => (byte)(2 << (2 * (direction & 3)));

	/// <summary>The bit of the cell at <paramref name="dx"/>, <paramref name="dy"/> (one of the eight neighbours).</summary>
	private static byte BitOf( int dx, int dy )
	{
		for ( var bit = 0; bit < 8; bit++ )
		{
			if ( PathTiles.Offsets[bit] == (dx, dy) )
				return (byte)(1 << bit);
		}
		throw new ArgumentOutOfRangeException( nameof( dx ) );
	}

	/// <summary>The bit pointing the other way (<c>0x4D8C00</c>).</summary>
	public static byte Opposite( byte bit ) => (byte)((bit << 4) | (bit >> 4));

	private static (int X, int Y) Step( int x, int y, int direction ) =>
		(x + GuestPathGrid.Directions[direction & 3].DX, y + GuestPathGrid.Directions[direction & 3].DY);

	private static void Or( ParkCellMap map, int x, int y, byte bit ) => map.SetLinks( x, y, (byte)(map.LinksAt( x, y ) | bit) );

	private static void Clear( ParkCellMap map, int x, int y, byte bits ) => map.SetLinks( x, y, (byte)(map.LinksAt( x, y ) & ~bits) );

	private static bool IsQueueType( byte type ) => type is 3 or 9;

	/// <summary>The number of set bits of the link byte, all eight (<c>0x522790</c>).</summary>
	public static int LinkCount( ParkCellMap map, int x, int y ) => System.Numerics.BitOperations.PopCount( map.LinksAt( x, y ) );

	/// <summary>
	/// Mode 1 for the path cell (<paramref name="x"/>, <paramref name="y"/>): a queue cell becomes a path cell first; then, around
	/// the compass from −Y, each cardinal neighbour is linked both ways when it is a path cell, a type-10 cell whose queue
	/// link points here or a type-9 cell whose queue link points away, and a path neighbour also gets a diagonal bit toward
	/// each of this cell's other path neighbours beside it unless the corner beyond is a queue; after each cardinal the
	/// diagonal neighbour between it and the next is linked both ways when it and both cardinals are path cells. Every bit
	/// is ORed into the bytes. Last, a link from this cell to a queue cell (types 3, 9) clears this cell's two diagonal bits
	/// beside it. A queue neighbour linking back is refreshed and reported (<see cref="LinkEffects.QueueNeighbour"/>);
	/// <paramref name="effects"/> carries the original's refresh flag (its fourth argument).
	/// </summary>
	// [BIN:TPW-EXE:0x005348D0 link writer mode 1] 0x534906: a type-3 cell is typed 1 (stamped, 0x4D8C50/0x4D8C60) and keeps its link bytes; per cardinal (−Y 0x534944, +X 0x534B55, +Y 0x534D74, −X 0x534F83): a type-1 neighbour gets the back bit and this cell the forward bit (0x522700 ORs), the neighbour a diagonal toward this cell's side neighbour when that is type 1 and the neighbour's own cardinal toward it is not type 3 or 9 (e.g. −Y: bit 8 when +X is a path, 0x20 when −X is; 0x534987..0x534A0D), then a tile refresh (0x5365D0) when the flag is set; a type-10 neighbour whose +0x0D has the bit pointing here, or a type-9 neighbour whose +0x0D has the bit pointing away (0x534A18..0x534A74), is linked both ways; a type-3 neighbour whose +0x0C points here is refreshed and, while 0x785A2C is 0, cat_ui 0x8B plays (0x534A76..0x534ACA); after each cardinal the diagonal cell between it and the next one clockwise is linked both ways when it and both cardinals are type 1 (0x534AD2: this cell 2, the +X−Y cell 0x20; 0x534CE4: 8 and 0x80; 0x534EF3: 0x20 and 2; 0x535102: 0x80 and 8)
	// [BIN:TPW-EXE:0x0053518B link clean-up] for the cardinal bits 1, 4, 16, 64 (rotated by 0x4D8C20) set in this cell whose neighbour is type 3 or 9: 1 clears 2 and 0x80, 4 clears 2 and 8, 16 clears 8 and 0x20, 64 clears 0x80 and 0x20, on this cell only (0x5351D8..0x535209)
	public static void LinkPath( ParkCellMap map, int x, int y, LinkEffects? effects = null )
	{
		ArgumentNullException.ThrowIfNull( map );
		if ( map.RawTypeAt( x, y ) == (byte)ParkCellType.Queue )
			map.SetType( x, y, ParkCellType.Path );
		for ( var direction = 0; direction < 4; direction++ )
		{
			LinkCardinal( map, x, y, direction, effects );
			LinkCorner( map, x, y, direction, effects );
		}
		for ( var direction = 0; direction < 4; direction++ )
		{
			if ( (map.LinksAt( x, y ) & Cardinal( direction )) == 0 )
				continue;
			var (nx, ny) = Step( x, y, direction );
			if ( map.InBounds( nx, ny ) && IsQueueType( map.RawTypeAt( nx, ny ) ) )
				Clear( map, x, y, (byte)(Diagonal( direction ) | Diagonal( direction + 3 )) );
		}
	}

	private static void LinkCardinal( ParkCellMap map, int x, int y, int direction, LinkEffects? effects )
	{
		var (nx, ny) = Step( x, y, direction );
		if ( !map.InBounds( nx, ny ) )
			return;
		var forward = Cardinal( direction );
		var back = Cardinal( direction + 2 );
		var type = map.RawTypeAt( nx, ny );
		if ( type == (byte)ParkCellType.Path )
		{
			Or( map, x, y, forward );
			Or( map, nx, ny, back );
			// The neighbour's diagonals toward this cell's two side neighbours (clockwise, then counter-clockwise).
			foreach ( var side in new[] { direction + 1, direction + 3 } )
			{
				var (sx, sy) = Step( x, y, side );
				if ( !map.InBounds( sx, sy ) || map.RawTypeAt( sx, sy ) != (byte)ParkCellType.Path )
					continue;
				var (cx, cy) = Step( nx, ny, side );
				if ( IsQueueType( map.RawTypeAt( cx, cy ) ) )
					continue;
				Or( map, nx, ny, BitOf( sx - nx, sy - ny ) );
			}
			effects?.Refresh?.Invoke( nx, ny );
		}
		if ( type == 10 && (map.QueueLinkAt( nx, ny ) & back) != 0 )
		{
			Or( map, x, y, forward );
			Or( map, nx, ny, back );
			effects?.Refresh?.Invoke( nx, ny );
		}
		if ( type == 9 )
		{
			if ( (map.QueueLinkAt( nx, ny ) & forward) != 0 )
			{
				Or( map, x, y, forward );
				Or( map, nx, ny, back );
				effects?.Refresh?.Invoke( nx, ny );
			}
		}
		else if ( type == (byte)ParkCellType.Queue && (map.LinksAt( nx, ny ) & back) != 0 && effects is { } report )
		{
			report.Refresh?.Invoke( nx, ny );
			report.QueueNeighbour?.Invoke( x, y );
		}
	}

	private static void LinkCorner( ParkCellMap map, int x, int y, int direction, LinkEffects? effects )
	{
		var (ax, ay) = Step( x, y, direction );
		var (bx, by) = Step( x, y, direction + 1 );
		var (cx, cy) = (ax + bx - x, ay + by - y);
		if ( !map.InBounds( cx, cy ) || map.RawTypeAt( cx, cy ) != (byte)ParkCellType.Path
			|| map.RawTypeAt( ax, ay ) != (byte)ParkCellType.Path || map.RawTypeAt( bx, by ) != (byte)ParkCellType.Path )
			return;
		Or( map, x, y, Diagonal( direction ) );
		Or( map, cx, cy, Opposite( Diagonal( direction ) ) );
		effects?.Refresh?.Invoke( cx, cy );
	}

	/// <summary>
	/// Mode 3 for cell (<paramref name="x"/>, <paramref name="y"/>) of a line stepping <paramref name="dx"/>, <paramref name="dy"/>:
	/// the cell before it on the line is linked both ways when its type is <paramref name="type"/> (the line's), it has fewer than two
	/// set link bits and <paramref name="sameOwner"/> accepts it; then, when this cell is a path cell (a queue line ending on a
	/// path), its two diagonal bits beside that link are cleared, whether or not the link was written.
	/// </summary>
	// [BIN:TPW-EXE:0x0053538C link writer mode 3] dy 1: the −Y cell (0x535394), dx −1: the +X cell (0x535410), dy −1: the +Y cell (0x53548A), dx 1: the −X cell (0x535504); the cell is linked both ways when its type equals the mode, 0x522790 < 2 and its owner word +0x10 is the tool's ride (0x818698) or 0; this cell, if type 1, then loses 2 and 0x80, 2 and 8, 8 and 0x20, or 0x80 and 0x20 (0x5353F5..0x535580); the step is stored in 0x763AC0/0x763AC4 for the next call
	public static void LinkQueueStep( ParkCellMap map, int x, int y, int dx, int dy, byte type, Func<int, int, bool> sameOwner )
	{
		ArgumentNullException.ThrowIfNull( map );
		ArgumentNullException.ThrowIfNull( sameOwner );
		// The step's predecessor directions: dy 1 is −Y (0), dx −1 is +X (1), dy −1 is +Y (2), dx 1 is −X (3).
		if ( dy == 1 )
			LinkPredecessor( map, x, y, 0, type, sameOwner );
		if ( dx == -1 )
			LinkPredecessor( map, x, y, 1, type, sameOwner );
		if ( dy == -1 )
			LinkPredecessor( map, x, y, 2, type, sameOwner );
		if ( dx == 1 )
			LinkPredecessor( map, x, y, 3, type, sameOwner );
	}

	private static void LinkPredecessor( ParkCellMap map, int x, int y, int direction, byte type, Func<int, int, bool> sameOwner )
	{
		var (px, py) = Step( x, y, direction );
		if ( !map.InBounds( px, py ) || map.RawTypeAt( px, py ) != type )
			return;
		if ( LinkCount( map, px, py ) < 2 && sameOwner( px, py ) )
		{
			Or( map, x, y, Cardinal( direction ) );
			Or( map, px, py, Cardinal( direction + 2 ) );
		}
		if ( map.RawTypeAt( x, y ) == (byte)ParkCellType.Path )
			Clear( map, x, y, (byte)(Diagonal( direction ) | Diagonal( direction + 3 )) );
	}

	/// <summary>
	/// The unlinking loop of the cell clear for (<paramref name="x"/>, <paramref name="y"/>) before it is emptied: for every bit set
	/// in its link byte (all eight for a path cell, the four cardinals for a queue cell) the bit is cleared and the neighbour it points
	/// to loses the opposite bit and is refreshed.
	/// </summary>
	// [BIN:TPW-EXE:0x005367A0 cell clear] the path case (types 1, 10) walks the bits 1, 2, 4, .. 0x80 (0x4D8C20 by one), the queue case (types 3, 9) the bits 1, 4, 16, 64 (by two); for each bit set in +0x0C: 0x522730 clears it, and the neighbour along it (0x4D97E0) loses the opposite bit (0x4D8C00) and is refreshed (0x5365D0); then the byte, the queue link +0x0D and the flags are cleared and the cell typed 0 (0x5346D0)
	public static void Unlink( ParkCellMap map, int x, int y, bool cardinalOnly, LinkEffects? effects = null )
	{
		ArgumentNullException.ThrowIfNull( map );
		for ( var bit = 0; bit < 8; bit += cardinalOnly ? 2 : 1 )
		{
			var value = (byte)(1 << bit);
			if ( (map.LinksAt( x, y ) & value) == 0 )
				continue;
			Clear( map, x, y, value );
			var (nx, ny) = (x + PathTiles.Offsets[bit].DX, y + PathTiles.Offsets[bit].DY);
			if ( !map.InBounds( nx, ny ) )
				continue;
			Clear( map, nx, ny, Opposite( value ) );
			effects?.Refresh?.Invoke( nx, ny );
			// [BIN:TPW-EXE:0x0053694B path clear beside an entrance] the path case (type 1) of the clear, on a cardinal bit whose neighbour is a type-9 cell with an owner word (+0x10) whose thing is an attraction (thing type 3), runs that ride's re-evaluation 0x004DE1F0 once the neighbour is unlinked and refreshed (0x5368A1..0x53694B), before the next bit
			if ( !cardinalOnly && (bit & 1) == 0 && map.RawTypeAt( x, y ) == (byte)ParkCellType.Path && map.RawTypeAt( nx, ny ) == ParkCellTypes.Entrance && map.OwnerAt( nx, ny ) != 0 )
				effects?.EntranceNeighbour?.Invoke( map.OwnerAt( nx, ny ) );
		}
	}
}
