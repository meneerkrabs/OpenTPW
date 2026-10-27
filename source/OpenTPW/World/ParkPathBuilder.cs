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
	/// <summary>Path over a queue cell that is not a queue end (a queue end is turned into a path cell).</summary>
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
	internal QueueSegmentReceipt? QueueReceipt { get; init; }
	internal bool? QueueFinalEndsTool { get; init; }
	/// <summary>Every cell of the snapped line, start and end included (what the preview shows).</summary>
	public IReadOnlyList<(int X, int Y)> Line { get; init; } = Array.Empty<(int X, int Y)>();
	/// <summary>Line cells that already had the type (placement counter bumped on commit, never charged).</summary>
	public IReadOnlyList<(int X, int Y)> Existing { get; init; } = Array.Empty<(int X, int Y)>();
	/// <summary>The refused cell, when <see cref="StoppedBy"/> is a refusal.</summary>
	public (int X, int Y)? StoppedAt { get; init; }
	/// <summary>True when the refusal is the bound of the original's back-of-queue walk (QueuePaths.MaximumCells), the only queue length limit it has: such a ghost does not end the tool.</summary>
	public bool StoppedByOwnLimit { get; init; }
	/// <summary>True for the ghost a click returns when the line had a refused cell and nothing was committed (tp.exe <c>0x524A63</c>).</summary>
	public bool GhostRefused { get; init; }
	/// <summary>
	/// [EXT:online-coop-commands] True for the line a tool's writer returns when the park refused its command before running it: nothing was laid, as if the click had never reached
	/// the commit. A command that threw while it ran is not refused this way: it keeps what it wrote before the throw (<see cref="ParkCommandResult.Error"/>).
	/// </summary>
	public bool CommandRefused { get; init; }
	/// <summary>
	/// [EXT:online-coop-commands] The line's command went to the host as a guest's request (C3a-2): nothing was laid yet, and the tool waits for the request's outcome
	/// (<see cref="CellBuildTool.CompleteWaiting"/>).
	/// </summary>
	internal WaitingLineCommit? Waiting { get; init; }
	/// <summary>
	/// [EXT:online-coop-undo] The record of the session's stream that laid the segment (C5): the host's own from its stream, a guest's from its request; null in single player, and for a
	/// segment a tool loaded from the park (the queue tool's existing segments). A take-back names it (<see cref="TakeBackCommand"/>).
	/// </summary>
	internal long? Record { get; init; }
	/// <summary>A successful preview visited the path/queue validator's link branch; only completed ghosts expose it in the HUD.</summary>
	public bool LinkPointer { get; init; }
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
	/// <summary>
	/// An object at (x, y) a path may cover (the level: <see cref="ParkObjects.GivesWay"/> with <see cref="ParkObjects.PathOverwritePriority"/>: features of
	/// overwrite priority 0 or 1); the cell then counts as empty and the commit demolishes the object (<see cref="DemolishObject"/>).
	/// </summary>
	public Func<int, int, bool>? ObjectGivesWay { get; set; }
	/// <summary>Demolishes the object at (x, y) before a path cell is laid there (the level: <see cref="ParkObjects.Remove"/>, which sells it for its scrap value).</summary>
	public Action<int, int>? DemolishObject { get; set; }
	public IParkGrid Terrain { get; }
	public ParkEconomy? Economy => economy();
	public CellToolMode Mode => CellToolMode.Path;

	/// <summary>Cost of one path cell (<c>Costs.PathCell</c>), or 0 without an economy.</summary>
	// [BIN:STP-PPC:0x10082AC4 SetCellType] type 1 is priced with the path cost global (Costs.PathCell, balance +1472, loaded by 0x1010F29C)
	// [BIN:TPW-EXE:0x4D01F0 bank debit] the path tool charges Costs.PathCell through the bank debit, which does nothing while mWithdrawalsEnabled (bank +0x114, serialized by 0x4CF920) is 0; the bank constructor 0x4CF7C0 sets it to 1 and only the action recorder's layout replay of a visited online park clears it (0x404140, 'Starting layout replay', called from the front-end loop 0x54E360) until the replay ends (0x4041D0 at 0x4044C4 / 0x40459C), when no tool runs, so a played park always pays; OpenTPW's path is free only for a builder without a park economy, a state no played park has
	// [BIN:TPW-EXE:0x534717 free byte] the free byte (0x8186D4, Mac data:0x7DE2D) zeroes the path and queue cost in SetCellType (0x534717, 0x53472C) but is set only in the mode-4 and mode-59 placement branches (0x5235D4, 0x523967, 0x524B4A, 0x5253E9), never by the path tool
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
	/// The validator's path rule for (x, y) without the line's money test: the rule an object's placement applies to an access cell it lays a
	/// path on (<see cref="ParkObjects.PathRuleAllows"/>), whose cost is the object's.
	/// </summary>
	public CellBuildResult CheckPathRule( int x, int y ) => Validate( x, y, ParkCellType.Path, lastCell: false, pending: null );

	/// <summary>
	/// A queue end (the validator's code 8): a queue cell with exactly one set link bit, whose neighbour along it is a queue cell
	/// too: the back of a queue not connected to a path. A path can be built over it, which turns it into a path cell.
	/// </summary>
	public static bool IsQueueEnd( ParkCellMap cells, int x, int y )
	{
		if ( cells.TypeAt( x, y ) != ParkCellType.Queue || PathLinkWriter.LinkCount( cells, x, y ) != 1 )
			return false;
		var bit = System.Numerics.BitOperations.TrailingZeroCount( cells.LinksAt( x, y ) );
		var (dx, dy) = PathTiles.Offsets[bit];
		return cells.RawTypeAt( x + dx, y + dy ) == (byte)ParkCellType.Queue;
	}

	/// <summary>
	/// The per-cell rules of PATH-plan §9.2 in order, without writing: bounds, owned land, <c>CanChangeCellType</c>,
	/// path over queue, objects, the terrain rule and money (balance − cost ≥ 0).
	/// </summary>
	public CellBuildResult Validate( int x, int y, ParkCellType type, bool lastCell ) => Validate( x, y, type, lastCell, pending: 0 );

	/// <param name="pending">The preview's line cost before this cell; null for a commit, whose money test is <c>SetCellType</c>'s in <see cref="WritePath"/>.</param>
	private CellBuildResult Validate( int x, int y, ParkCellType type, bool lastCell, long? pending ) => Validate( x, y, type, lastCell, pending, out _ );

	private CellBuildResult Validate( int x, int y, ParkCellType type, bool lastCell, long? pending, out bool linkPointer )
	{
		linkPointer = false;
		// The original's LayLine skips (does not stop at) cells outside its 128 × 128 array (0xd70d8); snapped tool ends are grid cells, so only the headless API can reach this (REVIEW-PATH N1).
		if ( !Cells.InBounds( x, y ) || x >= Terrain.Width || y >= Terrain.Height )
			return CellBuildResult.OutsideTerrain;
		// [BIN:STP-PPC:0x1008432C placement validator] a cell with flag 0x40 (not owned; set by map initialisation 0x10085538 on every non-InitialPath cell) is refused
		if ( Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned ) )
			return CellBuildResult.NotOwned;
		// [BIN:TPW-EXE:0x535D66 overwrite priority] a type 4, 9 or 10 cell is tested by its owner's overwrite priority alone: below the tool's 0x818C10 (2 for a path, set by the hover 0x5234D0) it is allowed, otherwise refused
		var givesWay = ObjectGivesWay?.Invoke( x, y ) == true;
		var old = givesWay ? (byte)0 : Cells.RawTypeAt( x, y );
		if ( !ParkCellMap.CanChangeCellType( old, (byte)type, lastCell ) )
			return CellBuildResult.Occupied;
		// [BIN:STP-PPC:0x10084864 placement validator] path over path returns 0 (allowed); SetCellType 0x10082AC4 then only bumps the placement counter
		if ( old == (byte)type )
			return CellBuildResult.Existing;
		// [BIN:STP-PPC:0x100849A4 placement validator] path over a queue cell: code 8 for a queue end (one link, linked neighbour also a queue), else refused
		// [BIN:TPW-EXE:0x00535CE0 path over a queue end] a type-3 cell with one set link bit (0x522790) whose neighbour along it (0x4D97E0) is type 3 returns 8 (0x535D06), which the start check (0x527280) and the ghost accept; any other queue cell is refused (0x535D12: 0x816D50 set, 1); the commit's SetCellType (rule 0x535600 allows type 1 over 3) then types it 1 and charges the path cost (WritePath)
		if ( type == ParkCellType.Path && old == (byte)ParkCellType.Queue && !IsQueueEnd( Cells, x, y ) )
			return CellBuildResult.QueueCell;
		if ( !givesWay && isOccupied?.Invoke( x, y ) == true )
			return CellBuildResult.Occupied;
		// [BIN:TPW-EXE:0x535670 cell validator] no slope or height test (LayLine 0x536100, SetCell 0x532fc0 reads heights only for the ghost, 0x535670 has no float operation, SetCellType 0x5346d0); the terrain rule is the map start's cell types (OriginalParkPlacement: holes 2, 0x1e and 7 refused, every other surface cell type 0 or the initial path), the unowned flag 0x40 is tested above
		var terrain = Terrain.CheckTerrain( x, y );
		if ( terrain is not (OriginalPlacementResult.Allowed or OriginalPlacementResult.Path) )
			return terrain == OriginalPlacementResult.OutsideTerrain ? CellBuildResult.OutsideTerrain : CellBuildResult.Terrain;
		// [BIN:STP-PPC:0x10084550 placement validator] money test while game+36 == 0: the 32-bit pending line cost (this cell included) is refused when greater than the balance, a zero sum passes; the commit only runs SetCellType's test (WritePath)
		if ( pending is { } before && Economy is { } park && !park.CanPreviewLine( before + CellCost ) )
			return CellBuildResult.NotEnoughMoney;
		linkPointer = type == ParkCellType.Path && old == (byte)ParkCellType.Queue;
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

	/// <summary>The path mode's start check: <see cref="CheckCell"/> accepts the cell (a new cell, an existing path or a queue end).</summary>
	// [BIN:TPW-EXE:0x0052727B start check] 0x535670(1, 0) on the clicked cell: only 1 refuses; path over path (0 or 0xB) and a queue end (8) start a line
	bool ICellLineWriter.CanStart( (int X, int Y) cell ) => CheckCell( cell.X, cell.Y ) is CellBuildResult.Ok or CellBuildResult.Existing;

	private SegmentResult Run( (int X, int Y) start, (int X, int Y) end, bool write )
	{
		var line = LayLine( start, end );
		var built = new List<(int X, int Y)>();
		var existing = new List<(int X, int Y)>();
		var written = new List<(int X, int Y)>();
		long charged = 0;
		var stoppedBy = CellBuildResult.Ok;
		var linkPointer = false;
		(int X, int Y)? stoppedAt = null;
		var last = line[^1];
		var endedOnExisting = Cells.TypeAt( last.X, last.Y ) is ParkCellType.Path or ParkCellType.Queue;
		for ( var index = 0; index < line.Count; index++ )
		{
			var (x, y) = line[index];
			// The end cell is written with the last-cell flag (data 0x84b2c) raised.
			var check = Validate( x, y, ParkCellType.Path, lastCell: index == line.Count - 1, pending: write ? null : charged, out var cellLink );
			if ( check == CellBuildResult.Existing )
			{
				existing.Add( (x, y) );
				written.Add( (x, y) );
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
			linkPointer |= !write && cellLink;
			var cost = CellCost;
			// [BIN:TPW-EXE:0x5344E5 cell op 0x87] the commit's first LayLine pass (0x524960: 0x536100(0x87, ...)) demolishes the owner of every type 4, 9 or 10 cell of the line whose priority 0x818C14 is below the tool's 0x818C10 (0x527EE0, crediting its scrap value) before the type pass
			// tp.exe lets a line commit only when its ghost passed the money test, so nothing is demolished for a cell that cannot be paid for
			if ( write && ObjectGivesWay?.Invoke( x, y ) == true )
			{
				if ( Economy is { } park && !park.CanSpendCell( CellPurchase.Path ) )
				{
					stoppedBy = CellBuildResult.NotEnoughMoney;
					stoppedAt = (x, y);
					break;
				}
				DemolishObject?.Invoke( x, y );
			}
			if ( write && !WritePath( x, y ) )
			{
				stoppedBy = CellBuildResult.NotEnoughMoney;
				stoppedAt = (x, y);
				break;
			}
			built.Add( (x, y) );
			written.Add( (x, y) );
			charged += cost;
		}
		// [BIN:TPW-EXE:0x0052744E link pass] the commit runs LayLine once per pass (0x527403..0x527517): the type pass (SetCellType for every cell), then pass 0x80, whose SetCell case calls the link writer 0x5348D0 with the mode of the last type written (0x816D64) and the refresh flag 1 for every cell of the line, so each cell links to neighbours the whole line has already typed; the ghost gate (0x524A63) lets only lines without a refused cell commit, so in the tool the link pass covers every cell, as here the cells the type pass reached
		if ( write )
		{
			foreach ( var (x, y) in written )
				PathLinkWriter.LinkPath( Cells, x, y, Effects );
			// [BIN:TPW-EXE:0x00527517 refresh pass] the last LayLine pass, 0x81, refreshes every cell of the line (SetCell case 0x81: the tile refresh 0x5365D0)
			if ( Effects?.Refresh is { } refresh )
				foreach ( var (x, y) in written )
					refresh( x, y );
		}
		return new SegmentResult( end, built, charged, stoppedBy, endedOnExisting )
		{
			Start = start,
			Line = line,
			Existing = existing,
			StoppedAt = stoppedAt,
			LinkPointer = linkPointer
		};
	}

	/// <summary>Raised with the owner word of a queue cell a path was laid over (the setter's queue edit 0x004DE1F0 of the ride that owns it).</summary>
	public event Action<ushort>? QueueCellPaved;

	/// <summary>The link writer's tile refreshes and queue-neighbour sound (the level's presentation), or none (headless).</summary>
	public LinkEffects? Effects { get; set; }

	// [BIN:STP-PPC:0x10082AC4 SetCellType] the same nonzero type again: the signed placement counter (+32) goes up by one, no charge
	private void BumpPlacement( int x, int y ) => Cells.SetPlacementCount( x, y, (short)Math.Min( short.MaxValue, Cells.PlacementCountAt( x, y ) + 1 ) );

	/// <summary><c>SetCellType(cell, 1)</c> for a validated cell: the type, then the spend; the links follow in the commit's link pass.</summary>
	// [BIN:STP-PPC:0x10082AC4 SetCellType] store the type at +8 and update the map, then Spend (0x100CBFDC) the path cost
	private bool WritePath( int x, int y )
	{
		var park = Economy;
		// [BIN:STP-PPC:0x10082BC8 SetCellType money test] the 32-bit balance − cost test before the store (ParkEconomy.CanSpendCell, the test TrySpendCell repeats)
		if ( park != null && !park.CanSpendCell( CellPurchase.Path ) )
			return false;
		// [BIN:TPW-EXE:0x005346D0 SetCellType] type 1 over a type-3 cell (a queue end, code 8) passes rule 0x535600 and is charged the path cost (0x52FB20) like any path cell; the cell keeps its link byte, and the ride owning it (owner word +0x10, a type-4/9/10 cell) gets its queue edit 0x4DE1F0 at once, which OpenTPW's guests run with their queue recompute on this map edit
		var wasQueue = Cells.TypeAt( x, y ) == ParkCellType.Queue;
		var owner = Cells.OwnerAt( x, y );
		Cells.SetType( x, y, ParkCellType.Path );
		// [BIN:TPW-EXE:0x0053481F queue edit] the setter's queue branch (type 1 over type 3, the owner word's cell of type 4, 9 or 10, 0x005347E0..0x00534858) runs the owning ride's re-evaluation 0x004DE1F0: the queue is recomputed and its back cell stamped
		if ( wasQueue && owner != 0 )
			QueueCellPaved?.Invoke( owner );
		// [BIN:TPW-EXE:0x534879 path Spend] SetCellType spends the path cost through 0x4D01F0, which adds it to the park's total costs (+0x1F5A0); the finance and hire panels show other costs as total costs minus staff costs (0x49BDD0, [0x7CA300]), so path spending is an other cost, as OpenTPW's OtherCosts within MoneyOut minus StaffCosts
		park?.TrySpendCell( CellPurchase.Path );
		return true;
	}

	// ---- Removal ----------------------------------------------------------------------------------

	/// <summary>
	/// <c>ClearCell</c>'s path case with a = b = 0 (forced): clears the cell and both sides of its links. A NoModify
	/// cell is kept unless it has no neighbours (then the flag is dropped first). Nothing is refunded.
	/// </summary>
	// [BIN:STP-PPC:0x100859B4 ClearCell] path case: NOMODIFY keeps the cell unless it has no neighbours ("Removing path cell with no neighbours but NOMODIFY set"); a = b = 0 sets the counter to −1; no Earn (no refund); [BIN:TPW-EXE:0x005367A0 cell clear] every bit of the cell's link byte, diagonals included, is cleared on both sides (PathLinkWriter.Unlink)
	// [BIN:TPW-EXE:0x0052C11F area clear] the area clear (tool 0x3A, AreaClearTool) calls ClearCell 0x5367A0(0, 0) on every type-1 cell of its rectangle: this forced removal
	public CellBuildResult Remove( int x, int y ) => Clear( x, y, forced: true );

	/// <summary>
	/// <see cref="Remove"/> that also takes a NoModify cell with neighbours, the flag dropped first as for a lone one (the level opts in for the cells the placement laid outside
	/// an entrance or exit, EXT:removable-access-cells). Nothing is refunded.
	/// </summary>
	public CellBuildResult RemoveFixed( int x, int y ) => Clear( x, y, forced: true, removeFixed: true );

	/// <summary>
	/// <c>ClearCell</c>'s path case with a walk step (a, b) other than (0, 0), as the backtrack key and the segment undo
	/// call it: the cell's placement counter drops by one and the cell is removed only when it falls below zero
	/// (<see cref="CellBuildResult.Existing"/> when the cell stays). NoModify as in <see cref="Remove"/>; no refund.
	/// </summary>
	// [BIN:TPW-EXE:0x5367A0 ClearCell] (a, b) other than (0, 0) decrements the signed counter +0x20; the cell stays while it is >= 0, else the counter is reset to 0 and the cell is cleared like the forced case
	public CellBuildResult RemoveOnce( int x, int y ) => Clear( x, y, forced: false );

	private CellBuildResult Clear( int x, int y, bool forced, bool removeFixed = false )
	{
		// [EXT:pools-bridges] a bridge's own seven cells are protected; the path outside either end may go, unless someone on the bridge would lose their last way off
		if ( Cells.PoolBridges is { } bridges && bridges.Covers( x, y ) ) return CellBuildResult.Occupied;
		if ( Cells.TypeAt( x, y ) != ParkCellType.Path )
			return CellBuildResult.NotPath;
		// only a removal that really empties the cell can strand someone: a step that just lowers the placement counter leaves the path (and reports Existing below)
		if ( Cells.PoolBridges is { } ends && (forced || Cells.PlacementCountAt( x, y ) - 1 < 0) && ends.PathRemovalStrands( x, y ) ) return CellBuildResult.Occupied;
		if ( Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.NoModify ) )
		{
			if ( !removeFixed && HasPathNeighbours( x, y ) )
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
		PathLinkWriter.Unlink( Cells, x, y, cardinalOnly: false, Effects );
		Cells.SetType( x, y, ParkCellType.Empty );
		return CellBuildResult.Ok;
	}

	/// <summary>A linked cardinal path or queue neighbour. <c>0x6e110</c> counts every set bit of the link byte instead; the two differ only for links toward non-path cells, such as the 0x0F links of imported InitialPath cells (REVIEW-PATH N3; tp.exe 0x522790 counts all eight bits as well).</summary>
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
