using System.Runtime.CompilerServices;

namespace OpenTPW;

/// <summary>The numbers of a lazy river (docs/POOLS.md, "Lazy river"). All of them are OpenTPW's own: the original game has no pools.</summary>
// [EXT:pools-river] OpenTPW's own lazy river; the original game has no pools
public static class PoolRiverSettings
{
	/// <summary>The smallest ring: four cells (a 2 x 2 block, every cell with two river neighbours).</summary>
	public const int MinimumRingCells = 4;
	/// <summary>The current's speed, in 16.16 cells per park turn: 0.07 cells a turn, 0.28 cells a second (a swimmer makes 0.09 to 0.21 cells a turn).</summary>
	public const int FlowSpeed = Fixed16.One / 14;
	/// <summary>How much of its offset from the middle of the lane a floater loses each turn: 1/8, so a floater that enters a corner is turned in about a cell.</summary>
	public const int CentringShift = 3;
	/// <summary>The shortest float, in park turns: 120 turns, about 30 seconds.</summary>
	public const int MinimumTurns = 120;
	/// <summary>A float lasts <see cref="MinimumTurns"/> plus a draw below this: up to 120 more turns.</summary>
	public const int ExtraTurns = 120;
	/// <summary>The happiness a float adds (meter 0 to 100), when it ends: floating in a lazy river is relaxing.</summary>
	public const float HappinessGain = 5f;
}

/// <summary>One ring of a lazy river: its cells in the order of the current (clockwise as the map is drawn: along the ring's top row to the east, then down).</summary>
/// <param name="Cells">The cells of the ring in flow order, starting at its first cell in row-by-row order.</param>
// [EXT:pools-river] OpenTPW's own lazy river; the original game has no pools
public sealed record PoolRiverRing( IReadOnlyList<(int X, int Y)> Cells );

/// <summary>
/// The lazy rivers of a park, derived from the pool cells that carry <see cref="ParkCellFlags.PoolRiver"/> (the river option of the pool tool). A river is a <b>ring</b>: a 4-connected set of
/// flagged cells in which every cell has exactly two flagged neighbours and that has at least <see cref="PoolRiverSettings.MinimumRingCells"/> cells. Flagged cells that do not form such a ring (a
/// dead end, a branch, a lane that touches itself, a block wider than one lane) are plain water: the swimmers do not float there. The current of a ring runs clockwise as the map is drawn
/// (east along its top row, then down), and each cell's flow direction is the step to the next cell of the ring, so the extension point is the same whatever the ring's shape.
/// Not saved: the flags on the pool cells are the truth, so a save round-trips the rivers for free (<see cref="Of"/> derives them again).
/// </summary>
// [EXT:pools-river] OpenTPW's own lazy river; the original game has no pools
public sealed class PoolRiverMap
{
	private static readonly ConditionalWeakTable<ParkCellMap, Cache> caches = new();
	private static readonly (int Dx, int Dy)[] Neighbours = { (0, -1), (1, 0), (0, 1), (-1, 0) };

	private sealed class Cache
	{
		public int Writes = -1;
		public PoolRiverMap? Map;
	}

	private readonly Dictionary<(int X, int Y), (int Ring, int Index)> positions = new();
	private readonly List<PoolRiverRing> rings = new();

	private PoolRiverMap()
	{
	}

	/// <summary>The rings, ordered by their first cell in row-by-row order.</summary>
	public IReadOnlyList<PoolRiverRing> Rings => rings;
	/// <summary>True when the park has at least one ring.</summary>
	public bool Any => rings.Count > 0;

	/// <summary>The rivers of a cell map, derived once per change of its pool cells (<see cref="ParkCellMap.PoolWrites"/>).</summary>
	public static PoolRiverMap Of( ParkCellMap cells )
	{
		ArgumentNullException.ThrowIfNull( cells );
		var cache = caches.GetOrCreateValue( cells );
		if ( cache.Map != null && cache.Writes == cells.PoolWrites )
			return cache.Map;
		cache.Map = Build( cells );
		cache.Writes = cells.PoolWrites;
		return cache.Map;
	}

	/// <summary>Derives the rings of the map's river cells (no cache).</summary>
	public static PoolRiverMap Build( ParkCellMap cells )
	{
		ArgumentNullException.ThrowIfNull( cells );
		var flagged = new HashSet<(int X, int Y)>();
		for ( var y = 0; y < cells.Height; y++ )
			for ( var x = 0; x < cells.Width; x++ )
				if ( cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater && cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.PoolRiver ) )
					flagged.Add( (x, y) );
		var map = new PoolRiverMap();
		var visited = new HashSet<(int X, int Y)>();
		// row-by-row order: the first cell of a component is its minimum, whose only flagged neighbours (of a ring) are east and south
		foreach ( var start in flagged.OrderBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			if ( !visited.Add( start ) )
				continue;
			var component = new List<(int X, int Y)> { start };
			var pending = new Stack<(int X, int Y)>();
			pending.Push( start );
			while ( pending.Count > 0 )
			{
				var (x, y) = pending.Pop();
				foreach ( var (dx, dy) in Neighbours )
					if ( flagged.Contains( (x + dx, y + dy) ) && visited.Add( (x + dx, y + dy) ) )
					{
						component.Add( (x + dx, y + dy) );
						pending.Push( (x + dx, y + dy) );
					}
			}
			if ( component.Count >= PoolRiverSettings.MinimumRingCells && component.All( cell => RiverNeighbours( flagged, cell ).Count == 2 ) )
				map.AddRing( flagged, start, component.Count );
		}
		return map;
	}

	private static List<(int X, int Y)> RiverNeighbours( HashSet<(int X, int Y)> flagged, (int X, int Y) cell ) =>
		Neighbours.Select( step => (X: cell.X + step.Dx, Y: cell.Y + step.Dy) ).Where( flagged.Contains ).ToList();

	private void AddRing( HashSet<(int X, int Y)> flagged, (int X, int Y) start, int count )
	{
		// the first cell is the component's minimum in row-by-row order, so its two neighbours are east and south: the walk leaves east and the ring runs clockwise on the map
		var cells = new List<(int X, int Y)> { start };
		var previous = start;
		var current = (X: start.X + 1, Y: start.Y);
		while ( current != start && cells.Count <= count )
		{
			cells.Add( current );
			var next = RiverNeighbours( flagged, current ).First( cell => cell != previous );
			previous = current;
			current = next;
		}
		if ( cells.Count != count )
			return;
		var ring = rings.Count;
		for ( var i = 0; i < cells.Count; i++ )
			positions[cells[i]] = (ring, i);
		rings.Add( new PoolRiverRing( cells ) );
	}

	/// <summary>Whether the cell is part of a ring.</summary>
	public bool IsRiver( int x, int y ) => positions.ContainsKey( (x, y) );

	/// <summary>The index of the ring a cell belongs to, or -1.</summary>
	public int RingAt( int x, int y ) => positions.TryGetValue( (x, y), out var position ) ? position.Ring : -1;

	/// <summary>The cell the current carries a swimmer in (x, y) into: the next cell of its ring, or null when the cell is no river cell.</summary>
	public (int X, int Y)? NextCell( int x, int y )
	{
		if ( !positions.TryGetValue( (x, y), out var position ) )
			return null;
		var cells = rings[position.Ring].Cells;
		return cells[(position.Index + 1) % cells.Count];
	}

	/// <summary>The flow direction of a river cell as a unit step ((1, 0) east, (0, 1) south, ...), or null when the cell is no river cell.</summary>
	public (int Dx, int Dy)? FlowAt( int x, int y ) => NextCell( x, y ) is { } next ? (next.X - x, next.Y - y) : null;

	/// <summary>The cell the current brings a swimmer in (x, y) from: the previous cell of its ring, or null when the cell is no river cell.</summary>
	public (int X, int Y)? PreviousCell( int x, int y )
	{
		if ( !positions.TryGetValue( (x, y), out var position ) )
			return null;
		var cells = rings[position.Ring].Cells;
		return cells[(position.Index + cells.Count - 1) % cells.Count];
	}

	/// <summary>
	/// One park turn of a floater at (<paramref name="x"/>, <paramref name="y"/>) (16.16 cells) in a river cell. It keeps the direction it came in by until it reaches the middle of the cell, then turns into
	/// the cell's own flow, so at a bend it follows the middle lines of the lane (which are free water in every cell of a ring, inner corners included) instead of cutting the corner; across the direction
	/// of travel it loses an eighth of its offset from the middle of the lane each turn. Returns false (and leaves the point) when the point is in no river cell. Integers only.
	/// </summary>
	public bool Float( ref int x, ref int y, int speed )
	{
		var cell = (X: x >> 16, Y: y >> 16);
		if ( FlowAt( cell.X, cell.Y ) is not { } flow || PreviousCell( cell.X, cell.Y ) is not { } previous )
			return false;
		var incoming = (Dx: cell.X - previous.X, Dy: cell.Y - previous.Y);
		var (centreX, centreY) = PoolSwimRegion.Centre( cell );
		// how far before the middle of the cell, along the way in, the floater still is (positive: not there yet)
		var before = -((x - centreX) * incoming.Dx + (y - centreY) * incoming.Dy);
		var (dx, dy) = incoming != flow && before > 0 ? incoming : flow;
		var step = dx == incoming.Dx && dy == incoming.Dy && incoming != flow ? Math.Min( speed, before ) : speed;
		var newX = x + dx * step + (dx == 0 ? (centreX - x) >> PoolRiverSettings.CentringShift : 0);
		var newY = y + dy * step + (dy == 0 ? (centreY - y) >> PoolRiverSettings.CentringShift : 0);
		(x, y) = (newX, newY);
		return true;
	}
}
