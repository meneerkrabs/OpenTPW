namespace OpenTPW;

/// <summary>
/// Walkable path cells and their cardinal connections, with cached breadth-first flow fields per target.
/// Built from the original save grid (path flag + connection bits, docs/TPWS-PAYLOAD.md) or, without a
/// save, from MAP InitialPath cells (4-neighbour adjacency). Coordinates are game cells (x, y).
/// </summary>
public sealed class GuestPathGrid
{
	/// <summary>Cardinal directions in a fixed order (−Y, +X, +Y, −X) for deterministic tie-breaking.</summary>
	public static readonly (int DX, int DY)[] Directions = { (0, -1), (1, 0), (0, 1), (-1, 0) };

	private readonly bool[] walkable;
	private readonly byte[] links; // bit d = connected towards Directions[d]
	private readonly Dictionary<int, int[]> fields = new();

	public int CountX { get; }
	public int CountY { get; }
	public int Version { get; private set; }
	public int WalkableCount { get; private set; }

	public GuestPathGrid( int countX, int countY )
	{
		if ( countX <= 0 || countY <= 0 || countX > 1024 || countY > 1024 )
			throw new ArgumentOutOfRangeException( nameof( countX ) );
		CountX = countX;
		CountY = countY;
		walkable = new bool[countX * countY];
		links = new byte[countX * countY];
	}

	public bool InBounds( int x, int y ) => (uint)x < (uint)CountX && (uint)y < (uint)CountY;
	public int Index( int x, int y ) => y * CountX + x;
	public bool IsWalkable( int x, int y ) => InBounds( x, y ) && walkable[Index( x, y )];

	/// <summary>Marks a cell walkable; <paramref name="connections"/> limits its links (null: link to every walkable neighbour).</summary>
	public void SetPath( int x, int y, bool isPath, byte? connections = null )
	{
		if ( !InBounds( x, y ) )
			throw new ArgumentOutOfRangeException( nameof( x ) );
		var index = Index( x, y );
		if ( walkable[index] != isPath )
			WalkableCount += isPath ? 1 : -1;
		walkable[index] = isPath;
		links[index] = isPath ? (connections ?? 0x0F) : (byte)0;
		Invalidate();
	}

	public void Invalidate()
	{
		fields.Clear();
		Version++;
	}

	/// <summary>True when both cells are walkable and at least one links towards the other.</summary>
	public bool AreConnected( int x, int y, int direction )
	{
		var (dx, dy) = Directions[direction];
		var nx = x + dx;
		var ny = y + dy;
		if ( !IsWalkable( x, y ) || !IsWalkable( nx, ny ) )
			return false;
		return (links[Index( x, y )] & (1 << direction)) != 0 || (links[Index( nx, ny )] & (1 << ((direction + 2) & 3))) != 0;
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
	public static GuestPathGrid FromOriginal( MapFile map, OriginalParkImport? save )
	{
		var grid = new GuestPathGrid( map.CellCountX, map.CellCountY );
		for ( var y = 0; y < map.CellCountY; y++ )
		{
			for ( var x = 0; x < map.CellCountX; x++ )
			{
				if ( save != null && save.Cells[x, y].IsPath )
					grid.SetPath( x, y, true, ToLinks( save.Cells[x, y].PathConnections ) );
				else if ( map.GetFlagsAt( x, y ).HasFlag( MapCellFlags.InitialPath ) )
					grid.SetPath( x, y, true );
			}
		}
		return grid;
	}

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
