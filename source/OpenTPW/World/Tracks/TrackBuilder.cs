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
	public long TileCost => Economy?.CellCost( Purchase ) ?? 0;

	// ---- Reading -----------------------------------------------------------------------------------

	/// <summary>The origin cell of the tile holding (x, y), or null when the cell holds no track.</summary>
	public (int X, int Y)? OriginOf( int x, int y )
	{
		var cell = Tracks.Get( x, y );
		if ( cell.Type == 0 )
			return null;
		return cell.IsExtension ? Tracks.CellOf( cell.Parent ) : (x, y);
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
		if ( pending is { } before && Economy is { } park && !park.CanPreviewLine( before + TileCost ) )
			return CellBuildResult.NotEnoughMoney;
		return CellBuildResult.Ok;
	}

	/// <summary>Validates a tile for the preview with the line's pending cost.</summary>
	internal CellBuildResult CheckTile( int x, int y, long pending, int owner = 0 ) => Validate( x, y, pending, owner );

	/// <summary>
	/// Lays the tile with its origin at (x, y): clears the path cells under it, writes the origin (type 11/16) and the three extension cells
	/// (type 12, parent = origin), charges <see cref="TileCost"/> and links the tile to neighbours whose arm already points at it.
	/// A tile that exists only bumps its overlap counter, free of charge. Returns the validation result (<see cref="CellBuildResult.Ok"/> when written).
	/// </summary>
	// [BIN:STP-PPC:0x10088CB8 track tile write] a tile over four path or empty cells clears the paths and writes the origin and three type-12 extension cells (the origin recorded in +0x10)
	public CellBuildResult PlaceTile( int x, int y, int owner = 0 )
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
		var park = Economy;
		if ( park != null && !park.CanSpendCell( Purchase ) )
			return CellBuildResult.NotEnoughMoney;
		var overPath = false;
		foreach ( var (cx, cy) in TrackStation.TileCellsOf( x, y ) )
			overPath |= ClearPath( cx, cy );
		var originIndex = Tracks.IndexOf( x, y );
		foreach ( var (cx, cy) in TrackStation.TileCellsOf( x, y ) )
		{
			var origin = (cx, cy) == (x, y);
			Cells.SetType( cx, cy, origin ? OriginType : TrackCellTypes.Extension );
			Tracks.Set( cx, cy, new TrackCell
			{
				Type = origin ? OriginType : TrackCellTypes.Extension,
				Parent = origin ? -1 : originIndex,
				Flags = origin && overPath ? FlagOverPath : (byte)0,
				Owner = owner
			} );
		}
		park?.TrySpendCell( Purchase );
		ConnectDangling( x, y, owner );
		Refresh( x, y );
		return CellBuildResult.Ok;
	}

	/// <summary>Flag bit that remembers a tile replaced path cells, so it draws as a bridge (TRACK-010); kept above the four bridge-state bits.</summary>
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
		}
		Cells.SetType( x, y, ParkCellType.Empty );
		return true;
	}

	/// <summary>Writes the two station tiles of a ride (type 13 or 18 origins and type 12 extensions), each with its flow axis connected both ways.</summary>
	public void PlaceStation( TrackStation station )
	{
		var owner = OwnerOf( station );
		var axis = (byte)(station.Heading | TrackHeading.Opposite( station.Heading ));
		foreach ( var (x, y) in station.Tiles )
		{
			var originIndex = Tracks.IndexOf( x, y );
			foreach ( var (cx, cy) in TrackStation.TileCellsOf( x, y ) )
			{
				ClearPath( cx, cy );
				var origin = (cx, cy) == (x, y);
				Cells.SetType( cx, cy, origin ? StartType : TrackCellTypes.Extension );
				Tracks.Set( cx, cy, new TrackCell { Type = origin ? StartType : TrackCellTypes.Extension, Mask = origin ? axis : (byte)0, Parent = origin ? -1 : originIndex, Owner = owner } );
			}
		}
	}

	/// <summary>Removes the station tiles of a ride (the ride's removal calls <see cref="RemoveCircuit"/> first).</summary>
	public void RemoveStation( TrackStation station )
	{
		foreach ( var (x, y) in station.Tiles )
			ClearTile( x, y );
	}

	private void ClearTile( int x, int y )
	{
		foreach ( var (cx, cy) in TrackStation.TileCellsOf( x, y ) )
		{
			if ( Tracks.Get( cx, cy ).Type != 0 )
			{
				Tracks.Clear( cx, cy );
				Cells.SetType( cx, cy, ParkCellType.Empty );
			}
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
			if ( neighbour.IsOrigin && (neighbour.Mask & TrackHeading.Opposite( heading )) != 0 && Compatible( neighbour.Owner, owner ) )
				AddArm( (x, y), heading );
		}
	}

	// ---- Pieces and bridges ------------------------------------------------------------------------

	/// <summary>
	/// Recomputes the bridge flags and chosen piece of the tile at (x, y) and its four neighbours: a straight tile over water (or over cleared
	/// paths) is a bridge, and the second pass turns it into the ramp or deck variant by which neighbours along the flow are bridges too.
	/// </summary>
	// [BIN:STP-PPC:0x10088590 bridge flag] a straight tile over map type 2 (water) or 7/30 gets flag bit 1; [BIN:STP-PPC:0x1007A354 bridge pass] neither neighbour bridged → H; only ahead → H_U; only behind → H_D; both → H_A
	// [APPROX:TRACK-010] the bridge test is OpenTPW's: terrain water under any of the tile's four cells, or path cells the tile cleared; the original tests map types 2, 7 and 30 whose meaning beyond water is not traced, and the ahead/behind neighbours follow the flow link (the positive axis direction before a walk) — evidence needed: what map types 7 and 30 are and the flag writer 0x10088590
	public void Refresh( int x, int y )
	{
		var region = new List<(int X, int Y)> { (x, y) };
		foreach ( var heading in TrackHeading.All )
			region.Add( (x + TrackStation.TileCells * TrackHeading.Dx( heading ), y + TrackStation.TileCells * TrackHeading.Dy( heading )) );
		foreach ( var (rx, ry) in region )
		{
			if ( IsTile( rx, ry ) )
				RefreshOne( rx, ry );
		}
	}

	private bool IsBridgeBase( int x, int y )
	{
		var tile = Tracks.Get( x, y );
		if ( !tile.IsOrigin || TrackCellTypes.IsStation( tile.Type ) || !IsStraight( tile.Mask ) )
			return false;
		if ( (tile.Flags & FlagOverPath) != 0 )
			return true;
		foreach ( var (cx, cy) in TrackStation.TileCellsOf( x, y ) )
		{
			if ( Terrain.CheckTerrain( cx, cy ) == OriginalPlacementResult.Water )
				return true;
		}
		return false;
	}

	internal static bool IsStraight( byte mask ) => mask is 0x11 or 0x44;

	private void RefreshOne( int x, int y )
	{
		var tile = Tracks.Get( x, y );
		var flags = (byte)(tile.Flags & ~TrackCell.BridgeMask);
		if ( IsBridgeBase( x, y ) )
		{
			var ahead = AheadOf( tile );
			var behind = TrackHeading.Opposite( ahead );
			var aheadBridged = IsBridgeBase( x + TrackStation.TileCells * TrackHeading.Dx( ahead ), y + TrackStation.TileCells * TrackHeading.Dy( ahead ) );
			var behindBridged = IsBridgeBase( x + TrackStation.TileCells * TrackHeading.Dx( behind ), y + TrackStation.TileCells * TrackHeading.Dy( behind ) );
			flags |= aheadBridged ? behindBridged ? TrackCell.FlagBridgeAbove : TrackCell.FlagBridgeUp : behindBridged ? TrackCell.FlagBridgeDown : TrackCell.FlagBridge;
		}
		tile.Flags = flags;
		var piece = TrackPieceSelector.Select( tile.Type, tile.Mask, tile.Link, tile.Flags );
		tile.Family = piece.Family;
		tile.Piece = piece.Piece;
		tile.Rotation = piece.Rotation;
		Tracks.Set( x, y, tile );
	}

	private static byte AheadOf( TrackCell tile ) => tile.Link != 0 && (tile.Link & tile.Mask) != 0 && IsAlong( tile.Link, tile.Mask ) ? tile.Link : (tile.Mask == 0x11 ? TrackHeading.PosY : TrackHeading.PosX);

	private static bool IsAlong( byte link, byte mask ) => (link & mask) == link && (link is TrackHeading.NegY or TrackHeading.PosY) == (mask == 0x11);
}
