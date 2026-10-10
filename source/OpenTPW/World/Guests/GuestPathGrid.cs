namespace OpenTPW;

/// <summary>
/// Walkable path cells and their cardinal connections, with cached breadth-first flow fields per target.
/// A view over the shared <see cref="ParkCellMap"/> (PATH-plan §9.1), which is the source of truth: a cell is
/// walkable when its type is path (1) and its links are the map's cardinal link bits (1, 4, 16, 64). Built from
/// the original save grid (path flag + connection bits, docs/TPWS-PAYLOAD.md) or, without a save, from MAP
/// InitialPath cells (4-neighbour adjacency). Coordinates are game cells (x, y). Queue cells (type 3,
/// docs/reverse/QUEUE-plan.md §3.3) are not walkable for routing and carry a link byte that points back toward
/// the ride entrance. Writes through this class or straight to the map (<see cref="ParkPathBuilder"/>) both
/// invalidate the cached flow fields, because the cache follows <see cref="ParkCellMap.Version"/>.
/// </summary>
public sealed class GuestPathGrid
{
	/// <summary>Cardinal directions in a fixed order (−Y, +X, +Y, −X) for deterministic tie-breaking.</summary>
	public static readonly (int DX, int DY)[] Directions = { (0, -1), (1, 0), (0, 1), (-1, 0) };

	private readonly Dictionary<int, int[]> fields = new();
	private int fieldsVersion;

	public int CountX { get; }
	public int CountY { get; }
	/// <summary>Changes with every write to <see cref="Cells"/>; guests re-plan their queues when it moves.</summary>
	public int Version => Cells.Version;
	public int WalkableCount => Cells.CountOf( ParkCellType.Path );
	public int QueueCellCount => Cells.CountOf( ParkCellType.Queue );
	/// <summary>The shared cell map of the path and queue tools, which this grid reads.</summary>
	public ParkCellMap Cells { get; }

	/// <summary>Original map cell type of a queue cell (map cell <c>+8</c>; <c>0x851ac</c>/<c>0x851d0</c>).</summary>
	// [BIN:STP-PPC:0x100DDA18 next queue cell] a neighbour that passes 0x851ac (type 3 or 9) and fails 0x851d0 (type 9) is a queue cell
	public const int QueueCellType = 3;

	public GuestPathGrid( int countX, int countY ) : this( new ParkCellMap( countX, countY ) )
	{
	}

	/// <summary>A walk graph over an existing cell map (the level's single map).</summary>
	public GuestPathGrid( ParkCellMap cells )
	{
		Cells = cells ?? throw new ArgumentNullException( nameof( cells ) );
		CountX = cells.Width;
		CountY = cells.Height;
		fieldsVersion = cells.Version;
	}

	public bool InBounds( int x, int y ) => (uint)x < (uint)CountX && (uint)y < (uint)CountY;
	public int Index( int x, int y ) => y * CountX + x;
	public bool IsWalkable( int x, int y ) => Cells.TypeAt( x, y ) == ParkCellType.Path;

	/// <summary>
	/// Marks a cell walkable (a path cell in <see cref="Cells"/>); <paramref name="connections"/> limits its links
	/// in <see cref="Directions"/> bit order (null: link to every walkable neighbour). Clearing a path cell makes it
	/// empty; a queue cell is left as it is.
	/// </summary>
	public void SetPath( int x, int y, bool isPath, byte? connections = null )
	{
		if ( !InBounds( x, y ) )
			throw new ArgumentOutOfRangeException( nameof( x ) );
		if ( isPath )
		{
			Cells.SetType( x, y, ParkCellType.Path );
			Cells.SetLinks( x, y, ToCellLinks( connections ?? 0x0F ) );
		}
		else if ( Cells.TypeAt( x, y ) == ParkCellType.Path )
			Cells.SetType( x, y, ParkCellType.Empty );
		Invalidate();
	}

	public bool IsQueue( int x, int y ) => Cells.TypeAt( x, y ) == ParkCellType.Queue;

	/// <summary>The cell's original queue link value (1, 4, 16 or 64), or 0 when it is not a queue cell.</summary>
	public byte GetQueueLink( int x, int y ) => IsQueue( x, y ) ? Cells.QueueLinkAt( x, y ) : (byte)0;

	/// <summary>Maps <see cref="Directions"/>-order link bits to the original 1/4/16/64 link bits.</summary>
	public static byte ToCellLinks( byte directionBits )
	{
		byte value = 0;
		for ( var direction = 0; direction < 4; direction++ )
		{
			if ( (directionBits & (1 << direction)) != 0 )
				value |= LinkValue( direction );
		}
		return value;
	}

	/// <summary>Maps the original 1/4/16/64 link bits to <see cref="Directions"/>-order bits.</summary>
	public static byte FromCellLinks( byte cellLinks )
	{
		byte value = 0;
		for ( var direction = 0; direction < 4; direction++ )
		{
			if ( (cellLinks & LinkValue( direction )) != 0 )
				value |= (byte)(1 << direction);
		}
		return value;
	}

	/// <summary>
	/// Makes (x, y) a queue cell whose link points toward <see cref="Directions"/>[<paramref name="towards"/>]
	/// (its predecessor, i.e. toward the ride entrance). A path cell stops being walkable.
	/// </summary>
	public void SetQueue( int x, int y, int towards )
	{
		if ( !InBounds( x, y ) )
			throw new ArgumentOutOfRangeException( nameof( x ) );
		Cells.SetType( x, y, ParkCellType.Queue );
		Cells.SetLinks( x, y, 0 );
		Cells.SetQueueLink( x, y, LinkValue( towards ) );
		Invalidate();
	}

	/// <summary>Removes a queue cell (the cell becomes plain ground).</summary>
	public void ClearQueue( int x, int y )
	{
		if ( !IsQueue( x, y ) )
			return;
		Cells.SetType( x, y, ParkCellType.Empty );
		Invalidate();
	}

	/// <summary>The original link value (map cell <c>+13</c>) for a <see cref="Directions"/> index.</summary>
	// [BIN:STP-PPC:0x1006E228 queue link read] map cell +13 holds the queue link direction; the values are 1, 4, 16 and 64
	// [APPROX:QUEUE-001] the queue link (+13) uses the compass of the connection bits (+12): Directions order (−Y, +X, +Y, −X) is 1, 4, 16, 64 as in SavePathConnections (78 Easymode path cells); for +13 itself this is assumed — evidence needed: the run-time neighbour offset tables (data 0xec52c..0xec5a4, zero in the file)
	public static byte LinkValue( int direction ) => direction is >= 0 and < 4 ? (byte)(1 << (2 * direction)) : throw new ArgumentOutOfRangeException( nameof( direction ) );

	/// <summary>The <see cref="Directions"/> index a link value points to, or −1.</summary>
	public static int LinkDirection( byte link ) => link switch { 1 => 0, 4 => 1, 16 => 2, 64 => 3, _ => -1 };

	/// <summary>Direction index from (x, y) to the 4-neighbour (nx, ny), or −1 when they are not adjacent.</summary>
	public static int DirectionBetween( int x, int y, int nx, int ny )
	{
		for ( var direction = 0; direction < 4; direction++ )
		{
			if ( x + Directions[direction].DX == nx && y + Directions[direction].DY == ny )
				return direction;
		}
		return -1;
	}

	/// <summary>Marks the grid changed: cached flow fields are dropped and <see cref="Version"/> moves.</summary>
	public void Invalidate() => Cells.Touch();

	/// <summary>True when both cells are walkable and at least one links towards the other.</summary>
	public bool AreConnected( int x, int y, int direction )
	{
		var (dx, dy) = Directions[direction];
		var nx = x + dx;
		var ny = y + dy;
		if ( !IsWalkable( x, y ) || !IsWalkable( nx, ny ) )
			return false;
		return (Cells.LinksAt( x, y ) & LinkValue( direction )) != 0 || (Cells.LinksAt( nx, ny ) & LinkValue( (direction + 2) & 3 )) != 0;
	}

	public IEnumerable<(int X, int Y)> Neighbours( int x, int y )
	{
		for ( var direction = 0; direction < 4; direction++ )
		{
			if ( AreConnected( x, y, direction ) )
				yield return (x + Directions[direction].DX, y + Directions[direction].DY);
		}
	}

	public int NeighbourCount( int x, int y )
	{
		var count = 0;
		for ( var direction = 0; direction < 4; direction++ )
			count += AreConnected( x, y, direction ) ? 1 : 0;
		return count;
	}

	/// <summary>Step count from every cell to the target (−1 unreachable). Cached until the grid changes.</summary>
	public int[] GetFlowField( int targetX, int targetY )
	{
		if ( fieldsVersion != Cells.Version )
		{
			fields.Clear();
			fieldsVersion = Cells.Version;
		}
		var key = Index( targetX, targetY );
		if ( fields.TryGetValue( key, out var field ) )
			return field;
		field = new int[CountX * CountY];
		Array.Fill( field, -1 );
		if ( IsWalkable( targetX, targetY ) )
		{
			var queue = new Queue<int>();
			field[key] = 0;
			queue.Enqueue( key );
			while ( queue.Count > 0 )
			{
				var current = queue.Dequeue();
				var x = current % CountX;
				var y = current / CountX;
				for ( var direction = 0; direction < 4; direction++ )
				{
					if ( !AreConnected( x, y, direction ) )
						continue;
					var next = Index( x + Directions[direction].DX, y + Directions[direction].DY );
					if ( field[next] >= 0 )
						continue;
					field[next] = field[current] + 1;
					queue.Enqueue( next );
				}
			}
		}
		fields[key] = field;
		return field;
	}

	public int Distance( int fromX, int fromY, int targetX, int targetY ) =>
		IsWalkable( fromX, fromY ) ? GetFlowField( targetX, targetY )[Index( fromX, fromY )] : -1;

	/// <summary>Next cell towards the target along the flow field; false at the target or when unreachable.</summary>
	public bool TryStep( int x, int y, int targetX, int targetY, out int nextX, out int nextY )
	{
		nextX = x;
		nextY = y;
		var field = GetFlowField( targetX, targetY );
		if ( !IsWalkable( x, y ) )
			return false;
		var here = field[Index( x, y )];
		if ( here <= 0 )
			return false;
		for ( var direction = 0; direction < 4; direction++ )
		{
			if ( !AreConnected( x, y, direction ) )
				continue;
			var nx = x + Directions[direction].DX;
			var ny = y + Directions[direction].DY;
			if ( field[Index( nx, ny )] == here - 1 )
			{
				nextX = nx;
				nextY = ny;
				return true;
			}
		}
		return false;
	}

	/// <summary>Full cell path from start to target (inclusive), or an empty list when unreachable.</summary>
	public List<(int X, int Y)> FindPath( int startX, int startY, int targetX, int targetY )
	{
		var path = new List<(int X, int Y)>();
		if ( Distance( startX, startY, targetX, targetY ) < 0 )
			return path;
		var (x, y) = (startX, startY);
		path.Add( (x, y) );
		while ( TryStep( x, y, targetX, targetY, out var nx, out var ny ) )
		{
			(x, y) = (nx, ny);
			path.Add( (x, y) );
		}
		return path;
	}

	/// <summary>The walkable cell nearest to (x, y) by Manhattan distance (ties: lowest y, then x), or null.</summary>
	public (int X, int Y)? FindNearestWalkable( int x, int y, int maximumDistance = 64 )
	{
		for ( var distance = 0; distance <= maximumDistance; distance++ )
		{
			for ( var dy = -distance; dy <= distance; dy++ )
			{
				var rest = distance - Math.Abs( dy );
				foreach ( var dx in rest == 0 ? new[] { 0 } : new[] { -rest, rest } )
				{
					if ( IsWalkable( x + dx, y + dy ) )
						return (x + dx, y + dy);
				}
			}
		}
		return null;
	}

	/// <summary>Builds the grid from an original level: save path cells with their connection bits, plus MAP InitialPath cells.</summary>
	public static GuestPathGrid FromOriginal( MapFile map, OriginalParkImport? save ) => new( ParkCellMap.FromOriginal( map, save ) );

	/// <summary>Maps the save's cardinal connection bits to <see cref="Directions"/> order.</summary>
	public static byte ToLinks( SavePathConnections connections )
	{
		byte links = 0;
		if ( connections.HasFlag( SavePathConnections.NegativeY ) ) links |= 1;
		if ( connections.HasFlag( SavePathConnections.PositiveX ) ) links |= 2;
		if ( connections.HasFlag( SavePathConnections.PositiveY ) ) links |= 4;
		if ( connections.HasFlag( SavePathConnections.NegativeX ) ) links |= 8;
		return links;
	}
}
