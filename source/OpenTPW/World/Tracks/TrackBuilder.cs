namespace OpenTPW;

/// <summary>
/// The headless kart and water track builder (docs/TRACKS.md): validates, writes, charges and removes 2 × 2 track tiles on the
/// shared <see cref="ParkCellMap"/> and keeps their connection masks, bridge flags and chosen pieces. CPU-only like
/// <see cref="ParkPathBuilder"/>; <see cref="TrackLineWriter"/> drives it from the cell build tool, and the level re-walks the circuit
/// after every edit (<see cref="TrackCircuitWalker"/>).
/// </summary>
public sealed class TrackBuilder
{
	private readonly Func<ParkEconomy?> economy;
	private readonly Func<int, int, bool>? isOccupied;

	/// <param name="cells">The level's cell map; its <see cref="ParkCellMap.Tracks"/> layer holds the tiles.</param>
	/// <param name="family">Kart or water tiles.</param>
	/// <param name="economy">Reads the current park economy on each call, or returns null for free building.</param>
	/// <param name="terrain">The object build grid whose terrain rule tracks share (water is allowed for water tracks).</param>
	/// <param name="isOccupied">Object footprints and other host-blocked cells.</param>
	public TrackBuilder( ParkCellMap cells, TrackFamily family, Func<ParkEconomy?> economy, IParkGrid terrain, Func<int, int, bool>? isOccupied = null )
	{
		Cells = cells ?? throw new ArgumentNullException( nameof( cells ) );
		if ( family is not (TrackFamily.Kart or TrackFamily.Water) )
			throw new ArgumentOutOfRangeException( nameof( family ) );
		Family = family;
		this.economy = economy ?? throw new ArgumentNullException( nameof( economy ) );
		Terrain = terrain ?? throw new ArgumentNullException( nameof( terrain ) );
		this.isOccupied = isOccupied;
	}

	public ParkCellMap Cells { get; }
	public TrackCellLayer Tracks => Cells.Tracks;
	public TrackFamily Family { get; }
	public IParkGrid Terrain { get; }
	public ParkEconomy? Economy => economy();
	public CellPurchase Purchase => Family == TrackFamily.Water ? CellPurchase.WaterTrack : CellPurchase.KartTrack;
	public byte OriginType => Family == TrackFamily.Water ? TrackCellTypes.WaterOrigin : TrackCellTypes.KartOrigin;
	public byte StartType => Family == TrackFamily.Water ? TrackCellTypes.WaterStart : TrackCellTypes.KartStart;

	/// <summary>Cost of one tile (<c>Costs.WaterTrackCell</c> or <c>Costs.KartTrackCell</c>), or 0 without an economy.</summary>
	// [DATA:Standard.sam:Costs.KartTrackCell/WaterTrackCell] one cell cost per tile, charged when the tile is written; the original spends nothing while the free-build byte 0x7DE2D is set (PATH-009)
	public long TileCost => PredictionMoney?.CellCost ?? Economy?.CellCost( Purchase ) ?? 0;

	/// <summary>
	/// [EXT:online-coop-undo] The money a prediction pays with (C5-3, a line predicted on a copy of the park): set, the builder's money tests and charges read and spend this copy of the bank's
	/// balance, by the bank's own arithmetic, in place of the park's economy, so the copy's line stops where the park's would.
	/// </summary>
	internal CellPredictionMoney? PredictionMoney { get; init; }

	// ---- Reading -----------------------------------------------------------------------------------

	/// <summary>The origin cell of the tile holding (x, y), or null when the cell holds no track.</summary>
	public (int X, int Y)? OriginOf( int x, int y )
	{
		var cell = Tracks.Get( x, y );
		if ( cell.Type == 0 )
			return null;
		return cell.IsExtension ? Tracks.CellOf( cell.Parent ) : (x, y);
	}

	/// <summary>
	/// Whether the '+' or '#' cell of an add-on may stand on (x, y), as the validator's add-on mode accepts it: the cell lies on a tile (its head for an extension cell) that is
	/// not a corner and has no junction, is not flagged as carrying an add-on already (the map cell's flag 0x10), and belongs to <paramref name="rideOwner"/>, the ride the add-on is
	/// for (<see cref="OwnerOf"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x00535953 add-on validation] with the upgrade selection set (0x8187F8) a cell asked for as type 0xB or 0x10 is refused when its flag word has 0x10 (0x535961), when the head's cell is no track cell (0x53AD30, 0x53596D), a corner (0x53AE00) or a junction (0x53AE90) (0x53597C..0x535990), when the head's owner word +0x10 is not the selected ride's (0x818698, 0x535998) and when the cell is a queue (0x5359A9); otherwise it passes (0x5359B3); without the selection a track under the cell refuses it (0x535831)
	public bool CanStandAddOn( int x, int y, int rideOwner )
	{
		if ( OriginOf( x, y ) is not { } origin )
			return false;
		var head = Tracks.Get( origin.X, origin.Y );
		if ( !head.IsOrigin || Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.AddOn ) || (Tracks.Get( x, y ).Flags & TrackCell.FlagAddOn) != 0 )
			return false;
		var arms = TrackHeading.Count( head.Mask );
		var corner = arms == 2 && (head.Mask & 0x11) != 0 && (head.Mask & 0x44) != 0;
		return !corner && arms <= 2 && head.Owner == rideOwner && Cells.RawTypeAt( x, y ) != (byte)ParkCellType.Queue;
	}

	/// <summary>Whether (x, y) is the origin cell of a track tile (station tiles included).</summary>
	public bool IsTile( int x, int y ) => Tracks.Get( x, y ).IsOrigin;

	/// <summary>Every tile origin in row order.</summary>
	public IEnumerable<(int X, int Y)> Tiles() => Tracks.Cells().Where( item => item.Cell.IsOrigin ).Select( item => (item.X, item.Y) );

	/// <summary>The owner id of the ride whose station is <paramref name="station"/> (TRACK-021): its first tile's origin-cell index plus one.</summary>
	// [EXT:track-tile-owner] OpenTPW tags every tile with its ride so rides side by side stay separate and a demolition removes the ride's own tiles; the original's 40-byte cell has no owner field and its walk and demolition follow the masks (its vehicles can cross into a neighbour ride's adjacent objects, see TRACK-019)
	public int OwnerOf( TrackStation station ) => Tracks.IndexOf( station.First.X, station.First.Y ) + 1;

	/// <summary>Whether tiles of the two owners may connect: the same ride, or a tile laid without a ride (owner 0).</summary>
	private static bool Compatible( int a, int b ) => a == b || a == 0 || b == 0;

	// ---- Validation and writing --------------------------------------------------------------------

	/// <summary>Whether a tile can be laid with its origin at (x, y) right now (no writes).</summary>
	public CellBuildResult CheckTile( int x, int y, int owner = 0 ) => Validate( x, y, pending: 0, owner );

	/// <param name="pending">The preview's line cost before this tile; null for a commit, whose money test is the spend's.</param>
	private CellBuildResult Validate( int x, int y, long? pending, int owner )
	{
		var existing = 0;
		foreach ( var (cx, cy) in TrackStation.TileCellsOf( x, y ) )
		{
			if ( !Cells.InBounds( cx, cy ) || cx >= Terrain.Width || cy >= Terrain.Height )
				return CellBuildResult.OutsideTerrain;
			var track = Tracks.Get( cx, cy );
			if ( track.Type != 0 )
			{
				(int X, int Y) origin = track.IsExtension ? Tracks.CellOf( track.Parent ) : (cx, cy);
				var originCell = Tracks.Get( origin.X, origin.Y );
				if ( origin != (x, y) || originCell.Type != OriginType && originCell.Type != StartType || !Compatible( originCell.Owner, owner ) )
					return CellBuildResult.Occupied;
				existing++;
				continue;
			}
			// [BIN:STP-PPC:0x1008432C placement validator] a cell with flag 0x40 (not owned) is refused, as for paths
			if ( Cells.FlagsAt( cx, cy ).HasFlag( ParkCellFlags.Unowned ) )
				return CellBuildResult.NotOwned;
			var type = Cells.RawTypeAt( cx, cy );
			if ( type == (byte)ParkCellType.Path )
			{
				if ( Cells.FlagsAt( cx, cy ).HasFlag( ParkCellFlags.NoModify ) )
					return CellBuildResult.NoModify;
			}
			else if ( type != 0 )
			{
				return type == (byte)ParkCellType.Queue ? CellBuildResult.QueueCell : CellBuildResult.Occupied;
			}
			if ( isOccupied?.Invoke( cx, cy ) == true )
				return CellBuildResult.Occupied;
			var ground = Terrain.CheckTerrain( cx, cy );
			if ( ground is OriginalPlacementResult.OutsideTerrain )
				return CellBuildResult.OutsideTerrain;
			var allowed = ground is OriginalPlacementResult.Allowed or OriginalPlacementResult.Path || ground == OriginalPlacementResult.Water && Family == TrackFamily.Water;
			if ( !allowed )
				return CellBuildResult.Terrain;
		}
		if ( existing == 4 )
			return CellBuildResult.Existing;
		if ( existing != 0 )
			return CellBuildResult.Occupied;
		// [BIN:STP-PPC:0x10084550 placement validator] the preview refuses a line whose pending cost exceeds the balance (the path tool's test)
		if ( pending is { } before && (PredictionMoney is { } money ? !money.CanPreviewLine( before + TileCost ) : Economy is { } park && !park.CanPreviewLine( before + TileCost )) )
			return CellBuildResult.NotEnoughMoney;
		return CellBuildResult.Ok;
	}

	/// <summary>Validates a tile for the preview with the line's pending cost.</summary>
	internal CellBuildResult CheckTile( int x, int y, long pending, int owner = 0 ) => Validate( x, y, pending, owner );

	/// <summary>
	/// Lays the tile with its origin at (x, y) as a line of one tile: writes the origin's record (type 11/16) and the three extension records (type 12, parent = origin), charges <see cref="TileCost"/>, links the
	/// tile to neighbours whose arm already points at it and runs the bridge ops on it (<see cref="RunBridgeOps"/>). A tile that exists only bumps its overlap counter, free of charge. Returns the validation
	/// result (<see cref="CellBuildResult.Ok"/> when written). The cells' own types stay as they are: a path under the tile stays, except that the last tile of a line removes the paths under it when its four
	/// cells hold nothing but paths and empty ground (<see cref="PlaceTileInLine"/>).
	/// </summary>
	// [BIN:STP-PPC:0x10088CB8 track tile write] a tile over four path or empty cells clears the paths and writes the origin and three type-12 extension cells (the origin recorded in +0x10) (the Mac's; the PC clears only on a line's last step, 0x5390C0)
	public CellBuildResult PlaceTile( int x, int y, int owner = 0 )
	{
		var result = PlaceTileInLine( x, y, owner, last: true );
		if ( result is CellBuildResult.Ok or CellBuildResult.Existing )
			RunBridgeOps( new[] { (x, y) } );
		return result;
	}

	/// <summary>
	/// One tile of a line: the type op without the later passes. <paramref name="last"/> is the line walker's last step (0x820AD0): only the last tile's type op removes the paths under its tile
	/// (<see cref="TrackLineWriter"/> runs the passes 0x85, 0x86 and 0x81 over the whole line afterwards, <see cref="RunBridgeOps"/>).
	/// </summary>
	internal CellBuildResult PlaceTileInLine( int x, int y, int owner, bool last ) => PlaceTileCore( x, y, owner, free: false, last );

	private CellBuildResult PlaceTileCore( int x, int y, int owner, bool free, bool last )
	{
		var check = Validate( x, y, pending: null, owner );
		if ( check == CellBuildResult.Existing )
		{
			var tile = Tracks.Get( x, y );
			tile.Overlap = (short)Math.Min( short.MaxValue, tile.Overlap + 1 );
			Tracks.Set( x, y, tile );
			return check;
		}
		if ( check != CellBuildResult.Ok )
			return check;
		// [EXT:online-coop-undo] a prediction pays with its copy of the balance (C5-3)
		var money = free ? null : PredictionMoney;
		var park = free || money != null ? null : Economy;
		if ( money != null && !money.CanSpendCell() )
			return CellBuildResult.NotEnoughMoney;
		if ( park != null && !park.CanSpendCell( Purchase ) )
			return CellBuildResult.NotEnoughMoney;
		// [BIN:TPW-EXE:0x00538FC0 type op credit test] a free tile (the placement's) is still refused while the player's cash is below zero (the test is cash - cost >= 0 with a cost of 0)
		if ( free && (PredictionMoney is { } shadow ? shadow.Balance < 0 : Economy is { Balance: < 0 }) )
			return CellBuildResult.NotEnoughMoney;
		// [BIN:TPW-EXE:0x005390C0 paths under the last tile] the type op of a kart or water tile on the line's last step (0x820AD0, 0x5390CE) removes the paths of the four cells with the cell remover 0x5367A0 when each of them is of type 0 or 1 (0x53915A..0x539188); every other tile of a line leaves its cells as they are, so the paths stay under it and it bridges them
		var cellsOfTile = TrackStation.TileCellsOf( x, y ).ToArray();
		if ( last && cellsOfTile.All( cell => Cells.PrimaryTypeAt( cell.X, cell.Y ) is 0 or (byte)ParkCellType.Path ) )
		{
			foreach ( var (cx, cy) in cellsOfTile )
				ClearPath( cx, cy );
		}
		var originIndex = Tracks.IndexOf( x, y );
		foreach ( var (cx, cy) in cellsOfTile )
		{
			var origin = (cx, cy) == (x, y);
			var type = origin ? OriginType : TrackCellTypes.Extension;
			// [BIN:TPW-EXE:0x00539017 type op writes the record] the record's type is written, never the cell's; OpenTPW's type byte shows the tile on a cell that holds nothing else (ParkCellMap.PrimaryTypeAt) and keeps the path or queue a cell holds
			Tracks.Set( cx, cy, new TrackCell { Type = type, Parent = origin ? -1 : originIndex, Flags = 0, Owner = owner } );
			if ( Cells.RawTypeAt( cx, cy ) == 0 )
				Cells.SetType( cx, cy, type );
			else
				Cells.StampBlockAt( cx, cy );
		}
		money?.SpendCell();
		park?.TrySpendCell( Purchase );
		ConnectDangling( x, y, owner );
		TrackBridges.Reselect( Tracks, x, y );
		return CellBuildResult.Ok;
	}

	/// <summary>
	/// A stray bit from saves made before round 11, when OpenTPW remembered here the paths it had cleared under a tile: no longer written, and the bridge ops ignore it (the fence keeps its own use of the bit,
	/// <see cref="ParkFence"/>). The next bridge op on such a tile assigns its whole flag word.
	/// </summary>
	public const byte FlagOverPath = 0x20;

	private bool ClearPath( int x, int y )
	{
		if ( Cells.TypeAt( x, y ) != ParkCellType.Path )
			return false;
		for ( var direction = 0; direction < 4; direction++ )
		{
			var (dx, dy) = GuestPathGrid.Directions[direction];
			if ( !Cells.InBounds( x + dx, y + dy ) )
				continue;
			var back = GuestPathGrid.LinkValue( (direction + 2) & 3 );
			var neighbour = Cells.LinksAt( x + dx, y + dy );
			if ( (neighbour & back) != 0 )
				Cells.SetLinks( x + dx, y + dy, (byte)(neighbour & ~back) );
			// [BIN:TPW-EXE:0x0053694B path clear beside an entrance] the clear every track routine reaches (0x538FC0 at 0x539190, the routine around 0x538DC2) re-evaluates the ride of a type-9 neighbour that carries an owner word
			if ( (Cells.LinksAt( x, y ) & GuestPathGrid.LinkValue( direction )) != 0 && Cells.RawTypeAt( x + dx, y + dy ) == ParkCellTypes.Entrance && Cells.OwnerAt( x + dx, y + dy ) != 0 )
				Cells.EntranceReevaluation?.Invoke( Cells.OwnerAt( x + dx, y + dy ) );
		}
		Cells.SetType( x, y, ParkCellType.Empty );
		return true;
	}

	/// <summary>Writes the two station tiles of a ride (type 13 or 18 origins and type 12 extensions), each with its flow axis connected both ways.</summary>
	public void PlaceStation( TrackStation station ) => PlaceStation( station, lineOnly: false );

	/// <summary>
	/// The station tiles with their flow axis (<paramref name="lineOnly"/> false) or with the arms of the station line alone: the first tile the arm toward the second and the second the arm back, the
	/// line op's arms (<c>0x539220</c>, each cell after the first gets the arm toward the previous cell and the previous cell the arm back). The exit and entry tiles' ops give the others.
	/// </summary>
	private void PlaceStation( TrackStation station, bool lineOnly )
	{
		var owner = OwnerOf( station );
		var axis = (byte)(station.Heading | TrackHeading.Opposite( station.Heading ));
		foreach ( var (x, y) in station.Tiles )
		{
			var tileAxis = !lineOnly ? axis : (x, y) == station.First ? station.Heading : TrackHeading.Opposite( station.Heading );
			var originIndex = Tracks.IndexOf( x, y );
			foreach ( var (cx, cy) in TrackStation.TileCellsOf( x, y ) )
			{
				ClearPath( cx, cy );
				var previous = Tracks.Get( cx, cy );
				var rawType = Cells.RawTypeAt( cx, cy );
				var baseType = !previous.IsEmpty && rawType == previous.Type ? previous.BaseType : rawType;
				var origin = (cx, cy) == (x, y);
				Cells.SetType( cx, cy, origin ? StartType : TrackCellTypes.Extension );
				// [BIN:TPW-EXE:0x00524F1E station link] the station tiles' flow link is the heading of the station's rotation (0x524EE9..0x524F19: 0 → 4, 90 → 1, 180 → 0x40, 270 → 0x10)
				Tracks.Set( cx, cy, new TrackCell { Type = origin ? StartType : TrackCellTypes.Extension, Mask = origin ? tileAxis : (byte)0, Link = origin ? station.Heading : (byte)0, Parent = origin ? -1 : originIndex, Owner = owner, BaseType = baseType } );
			}
		}
	}

	/// <summary>
	/// Fresh placement only: the station line, then the exit tile and the entry tile laid as the original's placement lays them, free of the tile price. Loading or migrating a station uses
	/// <see cref="PlaceStation(TrackStation)"/> instead. <paramref name="giveWay"/> demolishes what stands on a cell of the two tiles (the priority demolition of op 0x87), called with each of their cells.
	/// </summary>
	// [BIN:TPW-EXE:0x005253E9 initial track] placement sets free byte 0x8186D4; secondary writer 0x538FC0 skips the tile price and debit, not validation or existing-tile overlap.
	// [BIN:TPW-EXE:0x0052A1A0 station line] the station line first: the type op, 0x80 (the first tile no arm, the second the arm toward the first and the first the arm back), 0x82 and 0x83 over the two tiles, whose end is the exit cell
	// [BIN:TPW-EXE:0x005254C5 exit ops] on the exit cell, in this order through 0x53AAC0: 0x87, the tool mode's type op (0x52F840), 0x80, 0x82, 0x83, 0x85, 0x86 and 0x81, then the exit becomes the tool's first vertex (0x52FBF0); the arm op 0x80 gives the exit the arm toward the second station tile and that tile the arm back only when the type op laid the tile (0x539220 reads the type op's answer in 0x816D64, which a refused type op leaves cleared), the flow link op 0x82 has the singleton line's zero step and writes nothing
	// [BIN:TPW-EXE:0x005255F0 entry ops] the same ops on the entry cell (0x52A420) with the heading's step in the local, then by hand and whatever the type op answered: the arm toward the station (0x522700) and the flow link set to the heading (0x5227E0) on the entry cell, and the arm back (0x4D8C00, 0x522700) on the cell two cells on (0x50C3E0), the first station tile; the arm op 0x80 of the entry writes nothing, its previous step being the exit's zero step
	// [BIN:TPW-EXE:0x00538FC0 type op free] with the free byte set the tile costs nothing, but the op's credit test (0x538FF5..0x539050) still refuses a tile when the player's cash is below zero; a tile of the same type only counts its overlap word up (0x538FFD)
	public void PlaceInitialStation( TrackStation station, Action<int, int>? giveWay = null )
	{
		PlaceStation( station, lineOnly: true );
		var owner = OwnerOf( station );
		var heading = station.Heading;
		foreach ( var at in new[] { station.Exit, station.Entry } )
		{
			if ( giveWay != null )
				GiveWay( at, giveWay );
			// A refused adjacent tile remains untouched; placement is not an atomic four-origin transaction.
			var result = PlaceTileCore( at.X, at.Y, owner, free: true, last: true );
			var laid = result is CellBuildResult.Ok or CellBuildResult.Existing;
			if ( at == station.Exit )
			{
				if ( !laid )
					continue;
				AddArm( at, TrackHeading.Opposite( heading ) );
				AddArm( station.Second, heading );
				RunBridgeOps( new[] { at } );
				Refresh( station.Second.X, station.Second.Y );
				continue;
			}
			// the entry's own arm and link are written whatever the type op answered; a cell that holds no tile has no arm byte anyone reads
			if ( laid )
				ApplyLineLink( at, heading );
			if ( laid )
				AddArm( at, heading );
			AddArm( station.First, TrackHeading.Opposite( heading ) );
			Refresh( station.First.X, station.First.Y );
			if ( laid )
				RunBridgeOps( new[] { at } );
		}
	}

	/// <summary>
	/// Op 0x87 on a tile: unless the tile's origin cell already holds a track type (and no add-on), each of its four cells that is not a station cell is handed to <paramref name="giveWay"/> (the demolition of the
	/// object owning it) and its second-layer record is cleared.
	/// </summary>
	// [BIN:TPW-EXE:0x00538CF0 cell op 0x87] in 0x5370E0 (jump table 0x538EEC): when 0x818C14 < 0x818C10 (signed) a tile whose origin cell's type is 0xB, 0xD, 0x10, 0x12 or 0x19 does nothing (0x538D42..0x538D6C); any other tile calls 0x527EE0(x, y, 0) on (x, y), (x+1, y), (x, y+1) and (x+1, y+1) (0x538D76..0x538D9E), each returning at once unless the cell is of type 4, 9 or 10 (0x527D60), then, the cell being the line's only cell (0x820AD0 = 1), clears the four second-layer records (0x4D0B10, 0x5367A0(0, 0)) and restores the tool mode and 0x81EDE4/E8 (0x52F580); the cells of a kart or water station (types 13 and 18, extension 12 and 17 there) are no type 4, 9 or 10 cell, so no station is demolished, while an add-on's cells are type 4
	// [BIN:TPW-EXE:0x005367A0 second-layer clear] the record of a cell that is no station cell is cleared (its type and arms), here the tile record of the cell without the rest of its tile
	private void GiveWay( (int X, int Y) at, Action<int, int> giveWay )
	{
		foreach ( var (cx, cy) in GiveWayCells( at ) )
		{
			giveWay( cx, cy );
			Cells.ClearTrackRecord( cx, cy );
		}
	}

	/// <summary>
	/// The adjacent cells whose objects and second-layer records a fresh station may demolish before its tile validation. The rights preflight uses the same gates as the writer:
	/// an existing origin without an add-on skips the whole tile, and another station's origin and extension cells are spared. Enumerate before changing the park for a read-only plan.
	/// </summary>
	internal IEnumerable<(int X, int Y)> InitialStationGiveWayCells( TrackStation station ) => new[] { station.Exit, station.Entry }.SelectMany( GiveWayCells );

	private IEnumerable<(int X, int Y)> GiveWayCells( (int X, int Y) at )
	{
		var origin = Tracks.Get( at.X, at.Y );
		if ( origin.IsOrigin && (origin.Flags & TrackCell.FlagAddOn) == 0 )
			yield break;
		foreach ( var (cx, cy) in TrackStation.TileCellsOf( at.X, at.Y ) )
		{
			var cell = Tracks.Get( cx, cy );
			if ( TrackCellTypes.IsStation( cell.Type ) || cell.IsExtension && TrackCellTypes.IsStation( Tracks.Get( Tracks.CellOf( cell.Parent ).X, Tracks.CellOf( cell.Parent ).Y ).Type ) )
				continue;
			yield return (cx, cy);
		}
	}

	/// <summary>Removes the station tiles of a ride (the ride's removal calls <see cref="RemoveCircuit"/> first).</summary>
	public void RemoveStation( TrackStation station )
	{
		foreach ( var (x, y) in station.Tiles )
			ClearTile( x, y );
	}

	/// <summary>Erases the four cells of the tile whose origin is (x, y) from the layer and the cell map without touching its neighbours' arms (the track editor's clear does those).</summary>
	internal void EraseTile( int x, int y ) => ClearTile( x, y );

	private void ClearTile( int x, int y )
	{
		foreach ( var (cx, cy) in TrackStation.TileCellsOf( x, y ) )
		{
			Cells.ClearTrackRecord( cx, cy );
		}
	}

	/// <summary>
	/// Removes a laid (non-station) tile and the arms of its neighbours that pointed at it. Nothing is refunded.
	/// </summary>
	public bool RemoveTile( int x, int y )
	{
		var tile = Tracks.Get( x, y );
		if ( !tile.IsOrigin || TrackCellTypes.IsStation( tile.Type ) )
			return false;
		ClearTile( x, y );
		foreach ( var heading in TrackHeading.All )
		{
			var (nx, ny) = (x + TrackStation.TileCells * TrackHeading.Dx( heading ), y + TrackStation.TileCells * TrackHeading.Dy( heading ));
			var neighbour = Tracks.Get( nx, ny );
			if ( !neighbour.IsOrigin || TrackCellTypes.IsStation( neighbour.Type ) || !Compatible( neighbour.Owner, tile.Owner ) )
				continue;
			neighbour.Mask &= (byte)~TrackHeading.Opposite( heading );
			Tracks.Set( nx, ny, neighbour );
			Refresh( nx, ny );
		}
		return true;
	}

	/// <summary>How many laid (non-station) tiles belong to <paramref name="owner"/>.</summary>
	public int OwnedTileCount( int owner ) => owner == 0 ? 0 : Tracks.Cells().Count( item => item.Cell.IsOrigin && !TrackCellTypes.IsStation( item.Cell.Type ) && item.Cell.Owner == owner );

	/// <summary>Removes every laid (non-station) tile that belongs to <paramref name="owner"/>, whatever it is connected to; returns how many went.</summary>
	public int RemoveOwned( int owner )
	{
		if ( owner == 0 )
			return 0;
		var tiles = Tracks.Cells().Where( item => item.Cell.IsOrigin && !TrackCellTypes.IsStation( item.Cell.Type ) && item.Cell.Owner == owner ).Select( item => (item.X, item.Y) ).ToList();
		return RemoveCircuit( tiles );
	}

	/// <summary>Removes every laid (non-station) tile of the layer's connected circuit pieces that <paramref name="tiles"/> names.</summary>
	public int RemoveCircuit( IEnumerable<(int X, int Y)> tiles )
	{
		var removed = 0;
		foreach ( var (x, y) in tiles.ToArray() )
		{
			if ( RemoveTile( x, y ) )
				removed++;
		}
		return removed;
	}

	// ---- Connections -------------------------------------------------------------------------------

	/// <summary>Connects two tiles two cells apart: each gets the arm toward the other.</summary>
	public bool Link( (int X, int Y) a, (int X, int Y) b )
	{
		var heading = HeadingBetween( a, b );
		if ( heading == 0 || !IsTile( a.X, a.Y ) || !IsTile( b.X, b.Y ) || !Compatible( Tracks.Get( a.X, a.Y ).Owner, Tracks.Get( b.X, b.Y ).Owner ) )
			return false;
		AddArm( a, heading );
		AddArm( b, TrackHeading.Opposite( heading ) );
		Refresh( a.X, a.Y );
		Refresh( b.X, b.Y );
		return true;
	}

	/// <summary>
	/// The link op of a line (op 0x82 of the line tool, <c>0x539670</c>) on one tile of a line that runs in <paramref name="direction"/>: a tile with at most two arms takes the line's
	/// direction as its flow link, a tile with more arms (a tee or crossing another line made) adds it to the bits it has.
	/// </summary>
	// [BIN:TPW-EXE:0x00539670 line link op] the line op 0x53AAC0 calls the cell op handler once per tile of the line with the step (±2, 0) or (0, ±2) (0x538146, op 0x82); the handler sets +0xD (0x5227E0) to the direction bit (−X 0x40, +X 0x04, −Y 0x01, +Y 0x10) when the cell has two arms or fewer (0x522810), else to the old link ORed with it, which is how a crossing of two lines gets a two-bit link (0x14, 0x41, 0x50); the click handler runs op 0x80 (the arms) before it (0x525521, 0x525542)
	public void ApplyLineLink( (int X, int Y) tile, byte direction )
	{
		if ( direction == 0 || !IsTile( tile.X, tile.Y ) )
			return;
		var cell = Tracks.Get( tile.X, tile.Y );
		cell.Link = TrackHeading.Count( cell.Mask ) > 2 ? (byte)(cell.Link | direction) : direction;
		Tracks.Set( tile.X, tile.Y, cell );
		Refresh( tile.X, tile.Y );
	}

	/// <summary>The heading from tile <paramref name="a"/> to a tile two cells away, or 0.</summary>
	public static byte HeadingBetween( (int X, int Y) a, (int X, int Y) b )
	{
		var (dx, dy) = (b.X - a.X, b.Y - a.Y);
		return (dx, dy) switch
		{
			(TrackStation.TileCells, 0) => TrackHeading.PosX,
			(-TrackStation.TileCells, 0) => TrackHeading.NegX,
			(0, TrackStation.TileCells) => TrackHeading.PosY,
			(0, -TrackStation.TileCells) => TrackHeading.NegY,
			_ => 0
		};
	}

	private void AddArm( (int X, int Y) tile, byte heading )
	{
		var cell = Tracks.Get( tile.X, tile.Y );
		cell.Mask |= heading;
		Tracks.Set( tile.X, tile.Y, cell );
	}

	/// <summary>A new tile takes the arm toward every neighbour whose arm already points at it (the station's open ends, the end of an earlier line).</summary>
	private void ConnectDangling( int x, int y, int owner )
	{
		foreach ( var heading in TrackHeading.All )
		{
			var (nx, ny) = (x + TrackStation.TileCells * TrackHeading.Dx( heading ), y + TrackStation.TileCells * TrackHeading.Dy( heading ));
			var neighbour = Tracks.Get( nx, ny );
			if ( !neighbour.IsOrigin || !Compatible( neighbour.Owner, owner ) )
				continue;
			// a station tile also takes the tile on its axis when its own arm toward it was taken away (an edit cut the track there, TrackEditor.ClearCell)
			var axis = TrackCellTypes.IsStation( neighbour.Type ) && neighbour.Link != 0 ? (byte)(neighbour.Link | TrackHeading.Opposite( neighbour.Link )) : (byte)0;
			if ( (neighbour.Mask & TrackHeading.Opposite( heading )) != 0 || (axis & TrackHeading.Opposite( heading )) != 0 )
			{
				AddArm( (x, y), heading );
				if ( (neighbour.Mask & TrackHeading.Opposite( heading )) == 0 )
				{
					AddArm( (nx, ny), TrackHeading.Opposite( heading ) );
					Refresh( nx, ny );
				}
			}
		}
	}

	// ---- Pieces and bridges ------------------------------------------------------------------------

	/// <summary>
	/// Chooses the piece again of the tile at (x, y) and of its four neighbours (the refresh op 0x81, 0x53AFA0, which reads the tile's arms, flow link and flag word): the flag words themselves only change in the bridge
	/// ops (<see cref="RunBridgeOps"/>, <see cref="TrackBridges"/>), never here.
	/// </summary>
	// [BIN:STP-PPC:0x1007A354 bridge pass] neither neighbour bridged → H; only ahead → H_U; only behind → H_D; both → H_A (the Mac's own pass; the PC's flag words follow the walks of TrackBridges)
	public void Refresh( int x, int y )
	{
		TrackBridges.Reselect( Tracks, x, y );
		foreach ( var heading in TrackHeading.All )
			TrackBridges.Reselect( Tracks, x + TrackStation.TileCells * TrackHeading.Dx( heading ), y + TrackStation.TileCells * TrackHeading.Dy( heading ) );
	}

	/// <summary>
	/// The bridge passes of a track line, after the arms and flow links are written: op 0x85 on every tile in line order (<see cref="TrackBridges.ApplyBase"/>), op 0x86 on every tile (the forward and reverse walks
	/// round the tile's ride, <see cref="TrackBridges.Walk"/>) and op 0x81 on every tile (the piece).
	/// </summary>
	// [BIN:TPW-EXE:0x005254C5 exit ops] the click handler's op list runs through the line walker 0x53AAC0, each op a full pass over the line's tiles: 0x87, the type op, 0x80, 0x82, 0x83, 0x85, 0x86, 0x81 (0x820AD0 is the last step)
	// [BIN:TPW-EXE:0x00538BF0 op 0x86] the ride a tile belongs to is the thing listed on its owner word's cell (0x4D99E0): its walk pair runs once per tile of the line, so a long line walks the ride's whole track once for each tile
	internal void RunBridgeOps( IReadOnlyList<(int X, int Y)> tiles )
	{
		foreach ( var (x, y) in tiles )
			TrackBridges.ApplyBase( Cells, x, y );
		foreach ( var (x, y) in tiles )
		{
			var tile = Tracks.Get( x, y );
			if ( tile.IsOrigin && tile.Owner != 0 )
				TrackBridges.Walk( Cells, tile.Owner );
		}
		foreach ( var (x, y) in tiles )
			Refresh( x, y );
	}

	/// <summary>
	/// Chooses the piece again of every tile of <paramref name="tracks"/> (a restore): a save keeps each tile's flag word and piece, which only the bridge ops change, so this only makes the stored piece follow
	/// the stored arms, link and flags.
	/// </summary>
	public static void RefreshAll( TrackCellLayer tracks, ParkCellMap? cells = null )
	{
		ArgumentNullException.ThrowIfNull( tracks );
		foreach ( var (x, y) in tracks.Cells().Where( item => item.Cell.IsOrigin ).Select( item => (item.X, item.Y) ).ToList() )
			TrackBridges.Reselect( tracks, x, y );
	}

	internal static bool IsStraight( byte mask ) => mask is 0x11 or 0x44;

	/// <summary>A tile the bridge test may take: at most two arms and not a corner (a straight, a single-arm end or an armless tile).</summary>
	// [BIN:TPW-EXE:0x00538AA4 bridge shape] the corner test (0x522810 = 2 with perpendicular arms) skips the tile at 0x538AD8 and an arm count above 2 at 0x538B05; every other kart or water tile goes on to the path test
	internal static bool IsBridgeShape( byte mask ) => TrackHeading.Count( mask ) <= 2 && !(TrackHeading.Count( mask ) == 2 && (mask & 0x11) != 0 && (mask & 0x44) != 0);
}
