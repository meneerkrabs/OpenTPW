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
	// [APPROX:PATH-009] PATH-FREE: the path tool charges Costs.PathCell through Spend, which does nothing while bank +0x114 is 0 (tp.exe 0x4d01f0); OpenTPW takes 'no park economy' for that state; the free byte (tp.exe 0x8186d4, Mac data:0x7de2d) zeroes the path and queue cost in SetCellType (0x534717, 0x53472c) but is set only in the mode-4 and mode-59 placement branches (0x5235d4, 0x523967, 0x524b4a, 0x5253e9), never by the path tool; open: what bank +0x114 is — still open: what bank +0x114 is
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

	/// <param name="pending">The preview's line cost before this cell; null for a commit, whose money test is <c>SetCellType</c>'s in <see cref="WritePath"/>.</param>
	private CellBuildResult Validate( int x, int y, ParkCellType type, bool lastCell, long? pending )
	{
		// The original's LayLine skips (does not stop at) cells outside its 128 × 128 array (0xd70d8); snapped tool ends are grid cells, so only the headless API can reach this (REVIEW-PATH N1).
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
			// [APPROX:PATH-008] PATH-CODE8: path over any queue cell is refused; the original's validator returns code 8 for a queue end (one link whose neighbour is a queue; tp.exe 0x535670 at 0x535d00), and code 8 is not a refusal: the start check (0x527280) and the ghost accept it, and the commit's SetCellType (0x5346d0; rule 0x535600 allows type 1 over type 3) turns the queue end into a charged path cell; OpenTPW has no queue-to-path conversion — still open: the queue-to-path conversion of a queue end and its ride-queue update are not ported
			return CellBuildResult.QueueCell;
		}
		if ( isOccupied?.Invoke( x, y ) == true )
			return CellBuildResult.Occupied;
		// [APPROX:PATH-006] PATH-SLOPE: no slope or height test, as in the original (tp.exe LayLine 0x536100, SetCell 0x532fc0 reads heights only for the ghost, validator 0x535670 has no float operation, SetCellType 0x5346d0); the object terrain rule OpenTPW applies (water, blocked, entrance area, fixed walkway, holes) stands in for the original's cell types and ownership: map init 0x536490 types only hole cells (2, 0x1e, 7, refused by type) and leaves every other non-InitialPath cell type 0 with the unowned flag 0x40 (PATH-007) — still open: the ownership rule (PATH-007) that the terrain stand-in replaces
		var terrain = Terrain.CheckTerrain( x, y );
		if ( terrain is not (OriginalPlacementResult.Allowed or OriginalPlacementResult.Path) )
			return terrain == OriginalPlacementResult.OutsideTerrain ? CellBuildResult.OutsideTerrain : CellBuildResult.Terrain;
		// [BIN:STP-PPC:0x10084550 placement validator] money test while game+36 == 0: the 32-bit pending line cost (this cell included) is refused when greater than the balance, a zero sum passes; the commit only runs SetCellType's test (WritePath)
		if ( pending is { } before && Economy is { } park && !park.CanPreviewLine( before + CellCost ) )
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
			var check = Validate( x, y, ParkCellType.Path, lastCell: index == line.Count - 1, pending: write ? null : charged );
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
		// [BIN:STP-PPC:0x10082BC8 SetCellType money test] the 32-bit balance − cost test before the store (ParkEconomy.CanSpendCell, the test TrySpendCell repeats)
		if ( park != null && !park.CanSpendCell( CellPurchase.Path ) )
			return false;
		Cells.SetType( x, y, ParkCellType.Path );
		Link( x, y );
		// [BIN:TPW-EXE:0x534879 path Spend] SetCellType spends the path cost through 0x4D01F0, which adds it to the park's total costs (+0x1F5A0); the finance and hire panels show other costs as total costs minus staff costs (0x49BDD0, [0x7CA300]), so path spending is an other cost, as OpenTPW's OtherCosts within MoneyOut minus StaffCosts
		park?.TrySpendCell( CellPurchase.Path );
		return true;
	}

	/// <summary>Links a new path cell and each cardinal path neighbour to each other.</summary>
	// [APPROX:PATH-004] PATH-CONNECT: the path-to-path links are traced and match (tp.exe 0x5348d0 mode 1: each cardinal neighbour of type 1 gets the back bit and the new cell the forward bit, 0x534976..0x534fb4, directions from the vectors set up at 0x4d9a60); not modelled: the OR into existing link bytes (OpenTPW replaces the cell's own), links to type-9/10 neighbours gated by their +0xd link, the diagonal bits 2/8/0x20/0x80 and their clean-up beside type-3/9 links (0x53518b), the type-3 to type-1 conversion at entry (0x534906) and the queue-neighbour refresh with cat_ui 139 (AUDIO-023) — still open: the type-9/10 links, diagonal bits, OR write and type-3 conversion of 0x5348d0 are not ported
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
	// [APPROX:PATH-005] PATH-REMOVE: the forced path removal (a = b = 0, NoModify respected, no refund) is the original's area clear (tool mode 0x3a from the Delete key handler 0x40c5e0: two clicks span a rectangle and 0x52bdd0 calls ClearCell 0x5367a0(0, 0) on every type-1 cell, 0x52c11f), which Remove matches; OpenTPW's remove tool is one click that clears an object, else a queue cell, else a path cell, while the original's rectangle removes only objects wholly inside it and type-1 path cells and leaves queue and type-10 cells — still open: the two-click rectangle of tool 0x3a (0x52bdd0) is not ported
	public CellBuildResult Remove( int x, int y ) => Clear( x, y, forced: true );

	/// <summary>
	/// <c>ClearCell</c>'s path case with a walk step (a, b) other than (0, 0), as the backtrack key and the segment undo
	/// call it: the cell's placement counter drops by one and the cell is removed only when it falls below zero
	/// (<see cref="CellBuildResult.Existing"/> when the cell stays). NoModify as in <see cref="Remove"/>; no refund.
	/// </summary>
	// [BIN:TPW-EXE:0x5367A0 ClearCell] (a, b) other than (0, 0) decrements the signed counter +0x20; the cell stays while it is >= 0, else the counter is reset to 0 and the cell is cleared like the forced case
	public CellBuildResult RemoveOnce( int x, int y ) => Clear( x, y, forced: false );

	private CellBuildResult Clear( int x, int y, bool forced )
	{
		if ( Cells.TypeAt( x, y ) != ParkCellType.Path )
			return CellBuildResult.NotPath;
		if ( Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.NoModify ) )
		{
			if ( HasPathNeighbours( x, y ) )
				return CellBuildResult.NoModify;
			Cells.SetFlags( x, y, Cells.FlagsAt( x, y ) & ~ParkCellFlags.NoModify );
		}
		if ( !forced )
		{
			var count = Cells.PlacementCountAt( x, y ) - 1;
			if ( count >= 0 )
			{
				Cells.SetPlacementCount( x, y, (short)count );
				return CellBuildResult.Existing;
			}
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

	/// <summary>A linked cardinal path or queue neighbour. <c>0x6e110</c> counts every set bit of the link byte instead; the two differ only for links toward non-path cells, such as the 0x0F links of imported InitialPath cells (REVIEW-PATH N3; the link writer is tp.exe 0x5348D0, PATH-004).</summary>
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
	// [BIN:TPW-EXE:0x40BDA0 backtrack key] with a tool object on (vtable 0x6FE9E0, slot +0x24 returns 3) Backspace calls Back 0x52FE50(0, 1) at 0x40BEB3, which pops the vertex stack (count 0x820A8C), switches to mode 0x34 and clears the line from the popped vertex to the new top (0x525F71: LayLine pass 0x32, ClearCell 0x5367A0 with the walk step, so each cell's placement counter drops by one and the cell goes once it falls below 0; the path case spends nothing), then continues from the new top; with only the start left it changes nothing
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
