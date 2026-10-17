namespace OpenTPW;

/// <summary>Why a pool cell can or cannot be painted or erased (docs/POOLS.md, in check order).</summary>
// [EXT:pools-tool] OpenTPW's own tool; the original game has no pools
public enum PoolBuildResult
{
	/// <summary>The cell can be painted (and charged) or erased (and refunded).</summary>
	Ok,
	/// <summary>The cell is a pool cell already: painting it again changes and charges nothing.</summary>
	Existing,
	/// <summary>The <c>opentpw.pools</c> extension is off.</summary>
	ExtensionOff,
	/// <summary>Outside the map or the terrain's grid.</summary>
	OutsideTerrain,
	/// <summary>Cell flag 0x40: land the park does not own.</summary>
	NotOwned,
	/// <summary>An object, a path, a queue, a track, an entrance or exit or any other type of cell is there already.</summary>
	Occupied,
	/// <summary>The terrain rule refuses the cell (water, a hole, rock, the entrance area, the fixed walkway).</summary>
	Terrain,
	/// <summary>A guest or an employee stands on the cell.</summary>
	Standing,
	/// <summary>The ground under the cell is not level enough (<see cref="PoolBuilder.MaximumCornerSpread"/>).</summary>
	TooSteep,
	NotEnoughMoney,
	/// <summary>Erasing a cell that is not a pool cell.</summary>
	NotPool,
	/// <summary>[EXT:pools-swim] Erasing a pool cell with a swimmer in it: the water stays until the swimmer is out.</summary>
	Swimmers,
	/// <summary>[EXT:pools-changing-room] Erasing a pool cell beside the back of a changing room: the room needs its pool.</summary>
	ChangingRoomAccess,
	/// <summary>[EXT:pools-slides] The cell is the splash cell of a slide with riders on it, or the foot of a tower's stairs with guests on them.</summary>
	SlideRiders,
	/// <summary>[EXT:pools-slides] The slide tool refused the cell; <see cref="PoolTool.LastSlideRefusal"/> says why.</summary>
	SlideRefused
}

/// <summary>One drag of the pool tool: the cells it wrote, the money, and its first refusal.</summary>
/// <param name="Erase">True for a stroke of the erase mode.</param>
/// <param name="Cells">The cells written, in order: painted for a paint stroke, erased for an erase stroke.</param>
/// <param name="Charged">The money charged for painted cells.</param>
/// <param name="Refunded">The money refunded for erased cells.</param>
/// <param name="FirstRefusal">The first cell the stroke could not write, with the reason; null when none was refused.</param>
public sealed record PoolStroke( bool Erase, IReadOnlyList<(int X, int Y)> Cells, long Charged, long Refunded, ((int X, int Y) Cell, PoolBuildResult Reason)? FirstRefusal )
{
	/// <summary>True when no cell of the stroke was refused.</summary>
	public bool Completed => FirstRefusal == null;
}

/// <summary>
/// The headless pool builder of the <c>opentpw.pools</c> extension (docs/POOLS.md): validates, paints, charges and erases pool water cells on the shared
/// <see cref="ParkCellMap"/>. CPU-only, as <see cref="ParkPathBuilder"/> is, so the level's tool and the tests both call it; guests see every write at once because
/// they read the same map (a pool cell is not walkable: <see cref="GuestPathGrid.IsWalkable"/> is true for path cells alone).
/// </summary>
// [EXT:pools-tool] OpenTPW's own tool and rules; the original game has no pools
public sealed class PoolBuilder
{
	/// <summary>
	/// The most the four corner heights of a cell may differ, in engine units, for a pool to be painted on it: 0.25, an eighth of the cell's width of 2 (a slope of about 7 degrees across
	/// the cell). The land cannot be dug, so the pool is a surface on top of it: a cell on a steeper slope would have its water hang over or sink into the ground.
	/// </summary>
	// [EXT:pools-terrain-rule] a threshold chosen from the heights of the four themes' land (docs/POOLS.md, "The ground rule"); the original validates no slope for any tool (0x535670 has no float operation)
	public const float MaximumCornerSpread = 0.25f;

	private readonly Func<ParkEconomy?> economy;
	private readonly Func<int, int, bool>? isOccupied;
	private readonly Func<int, int, bool>? isStoodOn;
	private readonly Func<int, int, PoolBuildResult?>? eraseBlock;

	/// <param name="cells">The level's cell map.</param>
	/// <param name="economy">Reads the current economy on each call (a level replaces it when a park save loads); null for a free pool.</param>
	/// <param name="terrain">The object build grid: its terrain rule and its corner heights.</param>
	/// <param name="isOccupied">Objects, fixed items and other host-blocked cells.</param>
	/// <param name="isStoodOn">Whether a guest or an employee stands on a cell (water is not painted under them).</param>
	/// <param name="eraseBlock">[EXT:pools-swim] Why a pool cell may not be erased now (a swimmer in it, a changing room beside it), or null.</param>
	public PoolBuilder( ParkCellMap cells, Func<ParkEconomy?> economy, IParkGrid terrain, Func<int, int, bool>? isOccupied = null, Func<int, int, bool>? isStoodOn = null,
		Func<int, int, PoolBuildResult?>? eraseBlock = null )
	{
		Cells = cells ?? throw new ArgumentNullException( nameof( cells ) );
		this.economy = economy ?? throw new ArgumentNullException( nameof( economy ) );
		Terrain = terrain ?? throw new ArgumentNullException( nameof( terrain ) );
		this.isOccupied = isOccupied;
		this.isStoodOn = isStoodOn;
		this.eraseBlock = eraseBlock;
	}

	public ParkCellMap Cells { get; }
	public IParkGrid Terrain { get; }
	/// <summary>The money the last <see cref="Paint"/> took (positive) or <see cref="Erase"/> gave back (negative), measured on the balance: 0 when nothing moved (no economy, or the bank's withdrawals are off).</summary>
	public long LastCharged { get; private set; }
	public ParkEconomy? Economy => economy();

	/// <summary>Cost of one pool cell (<see cref="ParkEconomy.PoolCellCost"/>), or 0 without an economy.</summary>
	public long CellCost => Economy?.CellCost( CellPurchase.Pool ) ?? 0;

	/// <summary>The spread of the four corner heights of cell (<paramref name="x"/>, <paramref name="y"/>): the highest minus the lowest, in engine units.</summary>
	public static float CornerSpread( IParkGrid terrain, int x, int y )
	{
		ArgumentNullException.ThrowIfNull( terrain );
		var corners = new[] { terrain.GetCornerHeight( x, y ), terrain.GetCornerHeight( x + 1, y ), terrain.GetCornerHeight( x, y + 1 ), terrain.GetCornerHeight( x + 1, y + 1 ) };
		return corners.Max() - corners.Min();
	}

	/// <summary>The reason a cell cannot be painted right now, with no writes. <paramref name="pending"/> is the cost of the cells of the same stroke before this one (for the money test).</summary>
	public PoolBuildResult Check( int x, int y, long pending = 0 )
	{
		if ( !FeatureGates.Pools )
			return PoolBuildResult.ExtensionOff;
		if ( !Cells.InBounds( x, y ) || x >= Terrain.Width || y >= Terrain.Height )
			return PoolBuildResult.OutsideTerrain;
		if ( Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ) )
			return PoolBuildResult.NotOwned;
		var old = Cells.RawTypeAt( x, y );
		if ( old == ParkCellTypes.PoolWater )
			return PoolBuildResult.Existing;
		// anything else on the cell refuses it: paths (fixed ones included), queues, objects' cells, entrances and exits, track cells, the fence
		if ( !ParkCellMap.CanChangeCellType( old, ParkCellTypes.PoolWater, lastCell: false ) )
			return PoolBuildResult.Occupied;
		if ( Cells.Tracks.Get( x, y ) is { Type: not 0 } || isOccupied?.Invoke( x, y ) == true )
			return PoolBuildResult.Occupied;
		// a fence post (the park's boundary) stands on an empty-looking cell of the map: the land model knows it
		if ( Cells.Land?.IsPost( x, y ) == true )
			return PoolBuildResult.Occupied;
		if ( isStoodOn?.Invoke( x, y ) == true )
			return PoolBuildResult.Standing;
		var ground = Terrain.CheckTerrain( x, y );
		if ( ground != OriginalPlacementResult.Allowed )
			return ground == OriginalPlacementResult.OutsideTerrain ? PoolBuildResult.OutsideTerrain : PoolBuildResult.Terrain;
		if ( CornerSpread( Terrain, x, y ) > MaximumCornerSpread )
			return PoolBuildResult.TooSteep;
		if ( Economy is { } park && !park.CanPreviewLine( pending + CellCost ) )
			return PoolBuildResult.NotEnoughMoney;
		return PoolBuildResult.Ok;
	}

	/// <summary>Whether a cell can be erased: it is a pool cell, the extension is on, no swimmer is in it and no changing room needs it.</summary>
	public PoolBuildResult CheckErase( int x, int y )
	{
		if ( !FeatureGates.Pools )
			return PoolBuildResult.ExtensionOff;
		if ( !Cells.InBounds( x, y ) || Cells.RawTypeAt( x, y ) != ParkCellTypes.PoolWater )
			return PoolBuildResult.NotPool;
		return eraseBlock?.Invoke( x, y ) ?? PoolBuildResult.Ok;
	}

	/// <summary>Paints one cell and charges it. Returns <see cref="PoolBuildResult.Ok"/> when it was painted, <see cref="PoolBuildResult.Existing"/> when it already was a pool cell, else the refusal.</summary>
	public PoolBuildResult Paint( int x, int y )
	{
		var check = Check( x, y );
		if ( check != PoolBuildResult.Ok )
			return check;
		var park = Economy;
		if ( park != null && !park.CanSpendCell( CellPurchase.Pool ) )
			return PoolBuildResult.NotEnoughMoney;
		var before = park?.Balance ?? 0;
		Cells.SetType( x, y, ParkCellType.PoolWater );
		park?.TrySpendCell( CellPurchase.Pool );
		LastCharged = before - (park?.Balance ?? 0);
		return PoolBuildResult.Ok;
	}

	/// <summary>Erases one pool cell back to an empty cell and refunds its price.</summary>
	public PoolBuildResult Erase( int x, int y )
	{
		var check = CheckErase( x, y );
		if ( check != PoolBuildResult.Ok )
			return check;
		var before = Economy?.Balance ?? 0;
		Cells.SetType( x, y, ParkCellType.Empty );
		Economy?.RefundPoolCell();
		LastCharged = before - (Economy?.Balance ?? 0);
		return PoolBuildResult.Ok;
	}

	/// <summary>
	/// Paints <paramref name="cells"/> in order. A refused cell is skipped and the first refusal recorded, except for money: a cell that cannot be paid for ends the stroke.
	/// Cells that already are pool cells are passed over.
	/// </summary>
	public PoolStroke PaintCells( IEnumerable<(int X, int Y)> cells )
	{
		ArgumentNullException.ThrowIfNull( cells );
		var painted = new List<(int X, int Y)>();
		((int X, int Y), PoolBuildResult)? refusal = null;
		long charged = 0;
		foreach ( var (x, y) in cells )
		{
			var result = Paint( x, y );
			if ( result == PoolBuildResult.Ok )
			{
				painted.Add( (x, y) );
				charged += LastCharged;
			}
			else if ( result != PoolBuildResult.Existing )
			{
				refusal ??= ((x, y), result);
				if ( result == PoolBuildResult.NotEnoughMoney )
					break;
			}
		}
		return new PoolStroke( false, painted, charged, 0, refusal );
	}

	/// <summary>Erases <paramref name="cells"/> in order, refunding each pool cell; cells that are not pool cells are skipped and the first one recorded as the refusal.</summary>
	public PoolStroke EraseCells( IEnumerable<(int X, int Y)> cells )
	{
		ArgumentNullException.ThrowIfNull( cells );
		var erased = new List<(int X, int Y)>();
		((int X, int Y), PoolBuildResult)? refusal = null;
		long refunded = 0;
		foreach ( var (x, y) in cells )
		{
			var result = Erase( x, y );
			if ( result == PoolBuildResult.Ok )
			{
				erased.Add( (x, y) );
				refunded -= LastCharged;
			}
			else
				refusal ??= ((x, y), result);
		}
		return new PoolStroke( true, erased, 0, refunded, refusal );
	}

	/// <summary>The cells of the rectangle with the two corners, row by row (a fill for big pools).</summary>
	public static IEnumerable<(int X, int Y)> Rectangle( (int X, int Y) a, (int X, int Y) b )
	{
		for ( var y = Math.Min( a.Y, b.Y ); y <= Math.Max( a.Y, b.Y ); y++ )
			for ( var x = Math.Min( a.X, b.X ); x <= Math.Max( a.X, b.X ); x++ )
				yield return (x, y);
	}

	/// <summary>
	/// The cells a drag from <paramref name="from"/> to <paramref name="to"/> covers: a 4-connected walk that moves along the longer axis first, both ends included, so a fast
	/// pointer leaves no hole in the stroke.
	/// </summary>
	public static IReadOnlyList<(int X, int Y)> DragLine( (int X, int Y) from, (int X, int Y) to )
	{
		var cells = new List<(int X, int Y)> { from };
		var (x, y) = from;
		while ( (x, y) != to )
		{
			var (dx, dy) = (to.X - x, to.Y - y);
			if ( Math.Abs( dx ) >= Math.Abs( dy ) )
				x += Math.Sign( dx );
			else
				y += Math.Sign( dy );
			cells.Add( (x, y) );
		}
		return cells;
	}
}
