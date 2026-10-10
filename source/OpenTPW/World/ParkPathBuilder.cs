namespace OpenTPW;

/// <summary>Why a path or queue cell can or cannot be written (PATH-plan §9.2, in check order).</summary>
public enum CellBuildResult
{
	/// <summary>The cell can be written (and charged).</summary>
	Ok,
	/// <summary>The cell already has this type: the write only bumps its placement counter, free of charge.</summary>
	Existing,
	OutsideTerrain,
	/// <summary>Cell flag 0x40: land the park does not own.</summary>
	NotOwned,
	/// <summary>Refused by <c>CanChangeCellType</c>, or covered by an object, a fixed item or the prototype ride.</summary>
	Occupied,
	/// <summary>Path over a queue cell.</summary>
	QueueCell,
	/// <summary>The object terrain rule refuses the cell (water, blocked, entrance area, hole, the save's occupied cells).</summary>
	Terrain,
	NotEnoughMoney,
	/// <summary>A fixed (MAP InitialPath) path cell that still has neighbours cannot be removed.</summary>
	NoModify,
	/// <summary>Removal of a cell that is not a path cell.</summary>
	NotPath
}

/// <summary>
/// One <c>LayLine</c> run: the end after snapping, the cells newly written (each charged), the cells already of
/// the type (free, counter bumped), the total charged, the first refusal (<see cref="CellBuildResult.Ok"/> when the
/// whole line was laid) and whether the last cell landed on an existing path or queue cell.
/// </summary>
public sealed record SegmentResult( (int X, int Y) SnappedEnd, IReadOnlyList<(int X, int Y)> Built, long Charged, CellBuildResult StoppedBy, bool EndedOnExisting )
{
	/// <summary>The start of the line.</summary>
	public (int X, int Y) Start { get; init; }
	/// <summary>Every cell of the snapped line, start and end included (what the preview shows).</summary>
	public IReadOnlyList<(int X, int Y)> Line { get; init; } = Array.Empty<(int X, int Y)>();
	/// <summary>Line cells that already had the type (placement counter bumped on commit, never charged).</summary>
	public IReadOnlyList<(int X, int Y)> Existing { get; init; } = Array.Empty<(int X, int Y)>();
	/// <summary>The refused cell, when <see cref="StoppedBy"/> is a refusal.</summary>
	public (int X, int Y)? StoppedAt { get; init; }
	/// <summary>True when no cell of the line was refused.</summary>
	public bool Completed => StoppedBy is CellBuildResult.Ok or CellBuildResult.Existing;
}

/// <summary>
/// The headless path builder (PATH-plan §9.5): validates, lays, charges and removes path cells on the shared
/// <see cref="ParkCellMap"/>. CPU-only and without a <see cref="Level"/>: the level's path tool
/// (<see cref="CellBuildTool"/>) and the headless M3 gate both call it. Guests see every write at once, because
/// <see cref="GuestPathGrid"/> derives its walk graph from the same map.
/// </summary>
public sealed class ParkPathBuilder : ICellLineWriter
{
	private readonly Func<ParkEconomy?> economy;
	private readonly Func<int, int, bool>? isOccupied;

	/// <param name="cells">The level's cell map (the source of truth for paths and queues).</param>
	/// <param name="walk">The guests' walk graph over <paramref name="cells"/>, or null.</param>
	/// <param name="economy">The park economy, or null for the generic sandbox (paths are free).</param>
	/// <param name="terrain">The object build grid whose terrain rule paths share.</param>
	/// <param name="isOccupied">Object footprints, fixed items and other host-blocked cells.</param>
	public ParkPathBuilder( ParkCellMap cells, GuestPathGrid? walk, ParkEconomy? economy, IParkGrid terrain, Func<int, int, bool>? isOccupied = null )
		: this( cells, walk, () => economy, terrain, isOccupied )
	{
	}

	/// <param name="economy">Reads the current economy on each call (a level replaces it when a park save loads).</param>
	public ParkPathBuilder( ParkCellMap cells, GuestPathGrid? walk, Func<ParkEconomy?> economy, IParkGrid terrain, Func<int, int, bool>? isOccupied = null )
	{
		Cells = cells ?? throw new ArgumentNullException( nameof( cells ) );
		if ( walk != null && walk.Cells != cells )
			throw new ArgumentException( "The walk graph must read the same cell map.", nameof( walk ) );
		Walk = walk;
		this.economy = economy ?? throw new ArgumentNullException( nameof( economy ) );
		Terrain = terrain ?? throw new ArgumentNullException( nameof( terrain ) );
		this.isOccupied = isOccupied;
	}

	public ParkCellMap Cells { get; }
	public GuestPathGrid? Walk { get; }
	public IParkGrid Terrain { get; }
	public ParkEconomy? Economy => economy();
	public CellToolMode Mode => CellToolMode.Path;

	/// <summary>Cost of one path cell (<c>Costs.PathCell</c>), or 0 without an economy.</summary>
	// [BIN:STP-PPC:0x10082AC4 SetCellType] type 1 is priced with the path cost global (Costs.PathCell, balance +1472, loaded by 0x1010F29C)
	// [APPROX:PATH-009] the free-build byte data:0x7de2d and the game+36 money gate are taken as "no park economy" (the generic sandbox) — evidence needed: writers of data 0x7de2d and game+36
	public long CellCost => Economy?.CellCost( CellPurchase.Path ) ?? 0;

	// ---- LayLine ----------------------------------------------------------------------------------

	/// <summary>
	/// The snapped end of a line from <paramref name="start"/> toward <paramref name="cursor"/>: the dominant axis
	/// keeps the cursor's coordinate and the other one the start's. A tie keeps X.
	/// </summary>
	// [BIN:STP-PPC:0x1006F380 path preview] |dx| ≥ |dy| keeps X (end.y = start.y), otherwise Y; the commit 0x10070F84 snaps the same way
	public static (int X, int Y) SnapEnd( (int X, int Y) start, (int X, int Y) cursor ) =>
		Math.Abs( cursor.X - start.X ) >= Math.Abs( cursor.Y - start.Y ) ? (cursor.X, start.Y) : (start.X, cursor.Y);

	/// <summary>
	/// The cells <c>LayLine</c> walks from <paramref name="start"/> to <paramref name="end"/>, both included: along
	/// row start.Y when |dx| &gt; |dy|, else along column start.X. The other end coordinate is ignored, so a line is
	/// always straight.
	/// </summary>
	// [BIN:STP-PPC:0x10084EA4 LayLine] |x1 − x0| > |y1 − y0| walks X along row y0, else Y along column x0; the other end coordinate is ignored
	public static IReadOnlyList<(int X, int Y)> LayLine( (int X, int Y) start, (int X, int Y) end )
	{
		var cells = new List<(int X, int Y)>();
		if ( Math.Abs( end.X - start.X ) > Math.Abs( end.Y - start.Y ) )
		{
			var step = Math.Sign( end.X - start.X );
			for ( var x = start.X; ; x += step )
			{
				cells.Add( (x, start.Y) );
				if ( x == end.X )
					break;
			}
		}
		else
		{
			var step = end.Y == start.Y ? 0 : Math.Sign( end.Y - start.Y );
			for ( var y = start.Y; ; y += step )
			{
				cells.Add( (start.X, y) );
				if ( y == end.Y )
					break;
			}
		}
		return cells;
	}

	// ---- Validation -------------------------------------------------------------------------------

	/// <summary>Whether a path can be built on (x, y) right now (no writes): <see cref="Validate"/> for a single path cell.</summary>
	public CellBuildResult CheckCell( int x, int y ) => Validate( x, y, ParkCellType.Path, lastCell: false );

	/// <summary>
	/// The per-cell rules of PATH-plan §9.2 in order, without writing: bounds, owned land, <c>CanChangeCellType</c>,
	/// path over queue, objects, the terrain rule and money (balance − cost ≥ 0).
	/// </summary>
	public CellBuildResult Validate( int x, int y, ParkCellType type, bool lastCell ) => Validate( x, y, type, lastCell, pending: 0 );

	private CellBuildResult Validate( int x, int y, ParkCellType type, bool lastCell, long pending )
	{
		if ( !Cells.InBounds( x, y ) || x >= Terrain.Width || y >= Terrain.Height )
			return CellBuildResult.OutsideTerrain;
		// [BIN:STP-PPC:0x1008432C placement validator] a cell with flag 0x40 (not owned; set by map initialisation 0x10085538 on every non-InitialPath cell) is refused
		if ( Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ) )
			return CellBuildResult.NotOwned;
		var old = Cells.RawTypeAt( x, y );
		if ( !ParkCellMap.CanChangeCellType( old, (byte)type, lastCell ) )
			return CellBuildResult.Occupied;
		// [BIN:STP-PPC:0x10084864 placement validator] path over path returns 0 (allowed); SetCellType 0x10082AC4 then only bumps the placement counter
		if ( old == (byte)type )
			return CellBuildResult.Existing;
		if ( type == ParkCellType.Path && old == (byte)ParkCellType.Queue )
		{
			// [BIN:STP-PPC:0x100849A4 placement validator] path over a queue cell: code 8 for a queue end (one link, linked neighbour also a queue), else refused
			// [APPROX:PATH-008] path over a queue end (validator code 8) is refused like every other queue cell — evidence needed: the consumer of the validator's return code 8
			return CellBuildResult.QueueCell;
		}
		if ( isOccupied?.Invoke( x, y ) == true )
			return CellBuildResult.Occupied;
		// [APPROX:PATH-006] no slope or height test (none on the traced type-1 route); the object terrain rule applies, and the save's own path cells count as buildable — evidence needed: captures of the original laying paths on hills
		var terrain = Terrain.CheckTerrain( x, y );
		if ( terrain is not (OriginalPlacementResult.Allowed or OriginalPlacementResult.Path) )
			return terrain == OriginalPlacementResult.OutsideTerrain ? CellBuildResult.OutsideTerrain : CellBuildResult.Terrain;
		// [BIN:STP-PPC:0x10082AC4 SetCellType] money test while game+36 == 0: refused when balance − cost < 0 (no bankruptcy test); the validator 0x10084550 tests the pending line cost the same way
		if ( Economy is { } park && park.Balance - (pending + CellCost) < 0 )
			return CellBuildResult.NotEnoughMoney;
		return CellBuildResult.Ok;
	}

	// ---- Preview and build ------------------------------------------------------------------------

	/// <summary>The ghost of a line (no writes): snapped cells, the cells that would be built and their total cost.</summary>
	public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor ) => Run( start, SnapEnd( start, cursor ), write: false );

	/// <summary>
	/// Snaps <paramref name="end"/>, then writes every cell of the line through <c>SetCellType</c>: a new path cell
	/// is linked to its path neighbours and charged <c>Costs.PathCell</c>; an existing path cell only bumps its
	/// placement counter. The first refused cell stops the line; earlier cells stay built and charged.
	/// </summary>
	// [BIN:STP-PPC:0x10084EA4 LayLine] the first refused cell stops the walk; cells already written stay written and charged; LayLine itself never spends
	public SegmentResult BuildSegment( (int X, int Y) start, (int X, int Y) end ) => Run( start, SnapEnd( start, end ), write: true );

	/// <summary>Builds the snapped line from <paramref name="from"/> toward <paramref name="to"/>; true when no cell was refused.</summary>
	public bool TryLayLine( (int X, int Y) from, (int X, int Y) to, out SegmentResult result )
	{
		result = BuildSegment( from, to );
		return result.Completed;
	}

	SegmentResult ICellLineWriter.Commit( (int X, int Y) start, (int X, int Y) end ) => BuildSegment( start, end );

	private SegmentResult Run( (int X, int Y) start, (int X, int Y) end, bool write )
	{
		var line = LayLine( start, end );
		var built = new List<(int X, int Y)>();
		var existing = new List<(int X, int Y)>();
		long charged = 0;
		var stoppedBy = CellBuildResult.Ok;
		(int X, int Y)? stoppedAt = null;
		var last = line[^1];
		var endedOnExisting = Cells.TypeAt( last.X, last.Y ) is ParkCellType.Path or ParkCellType.Queue;
		for ( var index = 0; index < line.Count; index++ )
		{
			var (x, y) = line[index];
			// The end cell is written with the last-cell flag (data 0x84b2c) raised.
			var check = Validate( x, y, ParkCellType.Path, lastCell: index == line.Count - 1, pending: write ? 0 : charged );
			if ( check == CellBuildResult.Existing )
			{
				existing.Add( (x, y) );
				if ( write )
					BumpPlacement( x, y );
				continue;
			}
			if ( check != CellBuildResult.Ok )
			{
				stoppedBy = check;
				stoppedAt = (x, y);
				break;
			}
			var cost = CellCost;
			if ( write && !WritePath( x, y ) )
			{
				stoppedBy = CellBuildResult.NotEnoughMoney;
				stoppedAt = (x, y);
				break;
			}
			built.Add( (x, y) );
			charged += cost;
		}
		return new SegmentResult( end, built, charged, stoppedBy, endedOnExisting )
		{
			Start = start,
			Line = line,
			Existing = existing,
			StoppedAt = stoppedAt
		};
	}

	// [BIN:STP-PPC:0x10082AC4 SetCellType] the same nonzero type again: the signed placement counter (+32) goes up by one, no charge
	private void BumpPlacement( int x, int y ) => Cells.SetPlacementCount( x, y, (short)Math.Min( short.MaxValue, Cells.PlacementCountAt( x, y ) + 1 ) );

	/// <summary><c>SetCellType(cell, 1)</c> for a validated cell: type, links, then the spend.</summary>
	// [BIN:STP-PPC:0x10082AC4 SetCellType] store the type at +8 and update the map, then Spend (0x100CBFDC) the path cost
	private bool WritePath( int x, int y )
	{
		var park = Economy;
		if ( park != null && park.Balance - park.CellCost( CellPurchase.Path ) < 0 )
			return false;
		Cells.SetType( x, y, ParkCellType.Path );
		Link( x, y );
		// [APPROX:PATH-010] path spending is posted to OtherCosts (the original adds Spend to total costs, game+0x1f5a0; its ledger row is not identified) — evidence needed: the finance screen row that shows path spending
		park?.TrySpendCell( CellPurchase.Path );
		return true;
	}

	/// <summary>Links a new path cell and each cardinal path neighbour to each other.</summary>
	// [APPROX:PATH-004] a new path cell links to every cardinal path neighbour on both sides (the link writer 0x82d6c and its neighbour tables are not traced; every Easymode path neighbour is linked, 78 of 78 cells) — evidence needed: 0x10082D6C and the run-time neighbour tables data 0xec52c..
	private void Link( int x, int y )
	{
		byte links = 0;
		for ( var direction = 0; direction < 4; direction++ )
		{
			var (dx, dy) = GuestPathGrid.Directions[direction];
			if ( Cells.TypeAt( x + dx, y + dy ) != ParkCellType.Path )
				continue;
			links |= GuestPathGrid.LinkValue( direction );
			var back = GuestPathGrid.LinkValue( (direction + 2) & 3 );
			Cells.SetLinks( x + dx, y + dy, (byte)(Cells.LinksAt( x + dx, y + dy ) | back) );
		}
		Cells.SetLinks( x, y, links );
	}

	// ---- Removal ----------------------------------------------------------------------------------

	/// <summary>
	/// <c>ClearCell</c>'s path case with a = b = 0 (forced): clears the cell and both sides of its links. A NoModify
	/// cell is kept unless it has no neighbours (then the flag is dropped first). Nothing is refunded.
	/// </summary>
	// [BIN:STP-PPC:0x100859B4 ClearCell] path case: NOMODIFY keeps the cell unless it has no neighbours ("Removing path cell with no neighbours but NOMODIFY set"); a = b = 0 sets the counter to −1; each cardinal link is cleared on both sides; no Earn (no refund)
	// [APPROX:PATH-005] the player's remove route forces removal (a = b = 0) and is the only route besides undo — evidence needed: the bulldozer's call into 0x100859B4 and its a, b arguments
	public CellBuildResult Remove( int x, int y )
	{
		if ( Cells.TypeAt( x, y ) != ParkCellType.Path )
			return CellBuildResult.NotPath;
		if ( Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.NoModify ) )
		{
			if ( HasPathNeighbours( x, y ) )
				return CellBuildResult.NoModify;
			Cells.SetFlags( x, y, Cells.FlagsAt( x, y ) & ~ParkCellFlags.NoModify );
		}
		for ( var direction = 0; direction < 4; direction++ )
		{
			var (dx, dy) = GuestPathGrid.Directions[direction];
			if ( !Cells.InBounds( x + dx, y + dy ) )
				continue;
			var back = GuestPathGrid.LinkValue( (direction + 2) & 3 );
			var neighbour = Cells.LinksAt( x + dx, y + dy );
			if ( (neighbour & back) != 0 )
				Cells.SetLinks( x + dx, y + dy, (byte)(neighbour & ~back) );
		}
		Cells.SetType( x, y, ParkCellType.Empty );
		return CellBuildResult.Ok;
	}

	/// <summary>Neighbours as <c>0x6e110</c> sees them: a linked cardinal path or queue cell.</summary>
	private bool HasPathNeighbours( int x, int y )
	{
		for ( var direction = 0; direction < 4; direction++ )
		{
			var (dx, dy) = GuestPathGrid.Directions[direction];
			if ( (Cells.LinksAt( x, y ) & GuestPathGrid.LinkValue( direction )) != 0 && Cells.TypeAt( x + dx, y + dy ) is ParkCellType.Path or ParkCellType.Queue )
				return true;
		}
		return false;
	}

	/// <summary>Undo of a committed segment: removes the cells it built (newest first) and takes back its counter bumps. No refund.</summary>
	// [APPROX:PATH-002] Backspace undo removes the last segment's new cells and undoes its counter bumps, without a refund (only the help text 443 and the vertex stack are traced) — evidence needed: the BACKSPACE handler of the path tool
	public void Undo( SegmentResult segment )
	{
		for ( var index = segment.Built.Count - 1; index >= 0; index-- )
			Remove( segment.Built[index].X, segment.Built[index].Y );
		foreach ( var (x, y) in segment.Existing )
		{
			if ( Cells.TypeAt( x, y ) == ParkCellType.Path && Cells.PlacementCountAt( x, y ) > 0 )
				Cells.SetPlacementCount( x, y, (short)(Cells.PlacementCountAt( x, y ) - 1) );
		}
	}
}
