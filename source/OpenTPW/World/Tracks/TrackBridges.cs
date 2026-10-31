namespace OpenTPW;

/// <summary>
/// The second layer's bridge ops of tp.exe (TRACK-010, docs/TRACKS.md "Bridges"): the track tool's op 0x85 (<see cref="ApplyBase"/>: a path or queue under a tile makes it a bridge), the path and queue
/// tools' op 0x85 (<see cref="PathLaid"/>) and the op 0x86 walks (<see cref="Walk"/>), which go round the ride's track from its station and give each bridged straight the flag word that says which of its
/// neighbours are bridged too (1 alone, 2 bridged ahead, 8 bridged behind, 4 bridged both ways). The piece the tile draws follows from its flag word (<see cref="Reselect"/>, the op 0x81 refresh).
/// </summary>
public static class TrackBridges
{
	/// <summary>What the walks read of a cell's second-layer record: kind (+8), arms (+0xC), flag word (+0xE) and ride word (+0x10).</summary>
	private readonly record struct Record( byte Kind, byte Arms, ushort Flags, int Word );

	/// <summary>The walk's step bound, in map sizes: a walk that circles on a track that never reaches its station ends (the executable never does, it hangs).</summary>
	// [APPROX:TRACK-010] the bound stops a walk where tp.exe would hang
	private const int StepFactor = 2;

	// [BIN:TPW-EXE:0x0053AD20 bridge nibble] the flag word's low four bits: the bridge state of a tile (1 bridge, 2 bridged ahead, 4 bridged both ways, 8 bridged behind)
	private static bool Nibble( in Record record ) => (record.Flags & TrackCell.BridgeMask) != 0;

	// [BIN:TPW-EXE:0x0053AD30 track kinds] 0xB, 0xD, 0x10, 0x12 and 0x19
	private static bool IsTrack( in Record record ) => ParkCellTypes.IsTrackCell( record.Kind );

	// [BIN:TPW-EXE:0x0053AD70 ordinary tile] kinds 0xB and 0x10 only: the stations (0xD, 0x12) and the hoardings are no tile the walks set flags on
	private static bool IsOrdinary( byte kind ) => kind is TrackCellTypes.KartOrigin or TrackCellTypes.WaterOrigin;

	private static bool IsCorner( byte mask ) => TrackHeading.Count( mask ) == 2 && (mask & 0x11) != 0 && (mask & 0x44) != 0;

	// [BIN:TPW-EXE:0x004D8C00 opposite heading] bit 0x01 or 0x04 shifted up by four, 0x10 or 0x40 down, 0 kept
	private static byte Opposite( byte heading ) => heading == 0 ? (byte)0 : heading < 0x10 ? (byte)(heading << 4) : (byte)(heading >> 4);

	/// <summary>
	/// The tile of <paramref name="owner"/>'s station as the walks find it from the ride: the first station tile is the owner word's cell (<see cref="TrackBuilder.OwnerOf"/>) and its flow link the
	/// station's heading (0x524EE9..0x524F19), so the second tile, the exit and the entry follow. Null for owner 0 or a cell that holds no station tile.
	/// </summary>
	internal static TrackStation? StationOf( TrackCellLayer tracks, int owner )
	{
		if ( owner <= 0 || owner > tracks.Width * tracks.Height )
			return null;
		var first = tracks.CellOf( owner - 1 );
		var tile = tracks.Get( first.X, first.Y );
		if ( !tile.IsOrigin || !TrackCellTypes.IsStation( tile.Type ) )
			return null;
		var rotation = tile.Link switch { TrackHeading.PosX => 0, TrackHeading.NegY => 90, TrackHeading.NegX => 180, TrackHeading.PosY => 270, _ => -1 };
		if ( rotation < 0 )
			return null;
		return new TrackStation( rotation, tile.Link, first, (first.X + TrackStation.TileCells * TrackHeading.Dx( tile.Link ), first.Y + TrackStation.TileCells * TrackHeading.Dy( tile.Link )) );
	}

	/// <summary>Re-chooses the piece of the tile at (x, y) from its mask, flow link and flag word (the refresh op 0x81, 0x53AFA0); true when the choice changed.</summary>
	internal static bool Reselect( TrackCellLayer tracks, int x, int y )
	{
		var tile = tracks.Get( x, y );
		if ( !tile.IsOrigin )
			return false;
		var piece = TrackPieceSelector.Select( tile.Type, tile.Mask, tile.Link, tile.Flags );
		if ( tile.Family == piece.Family && tile.Piece == piece.Piece && tile.Rotation == piece.Rotation )
			return false;
		tile.Family = piece.Family;
		tile.Piece = piece.Piece;
		tile.Rotation = piece.Rotation;
		tracks.Set( x, y, tile );
		return true;
	}

	/// <summary>
	/// Whether the tile with this connection mask can be a bridge: at most two arms and no corner. The test of both bridge ops and of the walks' flag rule.
	/// </summary>
	// [BIN:TPW-EXE:0x00538AA4 bridge shape] the corner test (0x522810 = 2 with perpendicular arms) skips the tile at 0x538AD8 and an arm count above 2 at 0x538B05; every other tile goes on to the path test
	internal static bool IsBridgeShape( byte mask ) => TrackBuilder.IsBridgeShape( mask );

	/// <summary>
	/// The track tool's op 0x85 on the tile whose origin is (x, y): a tile that can be a bridge takes the flag word 1 when its origin cell or the cells at (0, +1), (+1, 0) and (+1, +1) hold a path or a
	/// queue, and loses the bridge state (the low four bits) when all four hold nothing; the cell types are the map layer's own, which a tile never changes.
	/// </summary>
	// [BIN:TPW-EXE:0x00538A92 bridge flag] 0x538B0B..0x538BDB: the origin cell's type 1 or 3 assigns the whole flag word 1 (0x538B22), so does each of the other three cells in the order (0, +1), (+1, 0), (+1, +1) (0x538B5D, 0x538B91, 0x538BC5; a cell off the map answers none and is skipped), a type 0 counts, and four counted cells clear the low nibble (0x538BDB)
	internal static bool ApplyBase( ParkCellMap cells, int x, int y )
	{
		var tracks = cells.Tracks;
		var tile = tracks.Get( x, y );
		if ( !tile.IsOrigin || !IsBridgeShape( tile.Mask ) )
			return false;
		int flags = tile.Flags;
		var empty = 0;
		foreach ( var (cx, cy) in new[] { (x, y), (x, y + 1), (x + 1, y), (x + 1, y + 1) } )
		{
			if ( !cells.InBounds( cx, cy ) )
				continue;
			var type = cells.PrimaryTypeAt( cx, cy );
			if ( type is (byte)ParkCellType.Path or (byte)ParkCellType.Queue )
				flags = TrackCell.FlagBridge;
			if ( type == 0 )
				empty++;
		}
		if ( empty == 4 )
			flags &= ~TrackCell.BridgeMask;
		if ( flags == tile.Flags )
			return false;
		tile.Flags = (byte)flags;
		tracks.Set( x, y, tile );
		return true;
	}

	/// <summary>
	/// The path and queue tools' op 0x85 on the cell (x, y) a path or queue was just laid on: the tile that holds the cell (an extension cell takes its origin) becomes a bridge when it can be one and is
	/// none yet.
	/// </summary>
	// [BIN:TPW-EXE:0x00534115 path tool bridge op] the record of the cell, an extension followed to its parent (0x53AD90): nothing unless it is of a track kind (0x53AD30), not a hoarding, no corner (0x53AE00) and no junction (0x53AE90) and its low nibble is clear (0x53AD20); then the nibble is cleared and the flag word assigned 1
	internal static bool PathLaid( ParkCellMap cells, int x, int y )
	{
		var tracks = cells.Tracks;
		var tile = tracks.Get( x, y );
		var origin = (X: x, Y: y);
		if ( tile.IsExtension && tile.Parent >= 0 && tile.Parent < tracks.Width * tracks.Height )
		{
			origin = tracks.CellOf( tile.Parent );
			tile = tracks.Get( origin.X, origin.Y );
		}
		if ( !tile.IsOrigin || !IsBridgeShape( tile.Mask ) || (tile.Flags & TrackCell.BridgeMask) != 0 )
			return false;
		tile.Flags = TrackCell.FlagBridge;
		tracks.Set( origin.X, origin.Y, tile );
		return true;
	}

	/// <summary>
	/// A path or queue cell written over the tile at (x, y) (<see cref="ParkCellMap.SetType(int,int,byte)"/>): the path tool's two bridge ops on the tile that holds the cell, <see cref="PathLaid"/> and then the
	/// walks of the tile's ride, and the piece chosen again. Taking a path or queue cell off a tile runs no op (the cell remover 0x5367A0 calls none).
	/// </summary>
	// [BIN:TPW-EXE:0x005341A2 path tool walks] the path tool's second op takes the tile's ride from the owner word (0x534214: the thing listed on the cell, 0x4D99E0) and runs 0x52C6F0 then 0x52CCB0 as op 0x86 does, with the effect flags set (the flags only add the build effect of the tile's mesh)
	internal static void PathLaidOver( ParkCellMap cells, int x, int y )
	{
		var tracks = cells.Tracks;
		var tile = tracks.Get( x, y );
		var origin = (X: x, Y: y);
		if ( tile.IsExtension && tile.Parent >= 0 && tile.Parent < tracks.Width * tracks.Height )
			origin = tracks.CellOf( tile.Parent );
		PathLaid( cells, x, y );
		var owner = tracks.Get( origin.X, origin.Y ).Owner;
		if ( owner != 0 )
			Walk( cells, owner );
		Reselect( tracks, origin.X, origin.Y );
	}

	/// <summary>
	/// The op 0x86 pair on the ride of <paramref name="owner"/>: the forward walk from the exit tile and, when it answers false, the reverse walk from the entry tile; every tile whose flag word they changed
	/// chooses its piece again. Nothing for a tile with no ride or whose station is not found. Returns the tiles whose flag word changed.
	/// </summary>
	// [BIN:TPW-EXE:0x00538BF0 op 0x86] a tile of kind 0xB, 0xD, 0x10 or 0x12 (not a hoarding) with the land tools' flag 0x81B4CC clear takes its ride (the thing listed on the owner word's cell, 0x4D99E0) and runs 0x52C6F0(thing, 1, 0, 0), then 0x52CCB0(thing, 0, 0, 0) when that answered 0 (0x538CA0..0x538CB7)
	internal static IReadOnlyList<(int X, int Y)> Walk( ParkCellMap cells, int owner )
	{
		var tracks = cells.Tracks;
		if ( StationOf( tracks, owner ) is not { } station )
			return Array.Empty<(int, int)>();
		var walker = new Walker( cells );
		if ( !walker.Forward( station ) )
			walker.Reverse( station );
		foreach ( var (x, y) in walker.Changed )
			Reselect( tracks, x, y );
		return walker.Changed;
	}

	private sealed class Walker( ParkCellMap cells )
	{
		private readonly TrackCellLayer tracks = cells.Tracks;
		private readonly int limit = StepFactor * cells.Tracks.Width * cells.Tracks.Height;
		public List<(int X, int Y)> Changed { get; } = new();

		private Record At( int x, int y )
		{
			if ( !tracks.InBounds( x, y ) )
				return default;
			var tile = tracks.Get( x, y );
			if ( !tile.IsEmpty )
				return new Record( tile.Type, tile.Mask, tile.Flags, tile.IsExtension ? tile.Parent + 1 : tile.Owner );
			if ( cells.Land is { } land )
			{
				// a fence post (kind 0x19) and the cells beside it (0xC) are second-layer records too
				var fence = land.LayerFor( cells.Width, cells.Height );
				var kind = fence.TypeAt( x, y );
				if ( kind != 0 )
					return new Record( kind, 0, fence.FlagsAt( x, y ), fence.OwnerAt( x, y ) );
			}
			return default;
		}

		private Record Next( int x, int y, byte heading ) => At( x + TrackStation.TileCells * TrackHeading.Dx( heading ), y + TrackStation.TileCells * TrackHeading.Dy( heading ) );

		// [BIN:TPW-EXE:0x0053ADB0 station record] a record of kind 0xD or 0x12, or an extension (0xC, 0x11) whose parent word names one
		private bool IsStationAt( int x, int y )
		{
			if ( !tracks.InBounds( x, y ) )
				return false;
			var tile = tracks.Get( x, y );
			if ( tile.IsExtension )
			{
				if ( tile.Parent < 0 || tile.Parent >= tracks.Width * tracks.Height )
					return false;
				var parent = tracks.CellOf( tile.Parent );
				return TrackCellTypes.IsStation( tracks.Get( parent.X, parent.Y ).Type );
			}
			return TrackCellTypes.IsStation( tile.Type );
		}

		// [BIN:TPW-EXE:0x0053A510 flag word write] the word is assigned whole (0x53A510), after the low nibble was cleared (0x53A530 with 0xF): the add-on bit and any other bit go with it
		private void Assign( int x, int y, int flags )
		{
			var tile = tracks.Get( x, y );
			if ( tile.Flags == (byte)flags )
				return;
			tile.Flags = (byte)flags;
			tracks.Set( x, y, tile );
			if ( !Changed.Contains( (x, y) ) )
				Changed.Add( (x, y) );
		}

		/// <summary>The forward walk 0x52C6F0 from the station's exit tile along the ride's heading: true when it reaches a station tile of the ride that is joined back.</summary>
		// [BIN:TPW-EXE:0x0052C6F0 forward bridge walk] start = anchor + (4, 1), (1, -5), (-5, -2) or (-2, 4) by rotation 0, 90, 180, 270 with the heading +X, -Y, -X, +Y (0x52C73C..0x52C77B); the start cell not being a track kind answers 1 at once (0x52C7BD); a tile of kind 0xB or 0x10 (0x53AD70) turns at a corner to mask ^ opposite (0x52C80B..0x52C83E), is given its flag word when bridged (0x52C843..0x52CA47), ends the walk when it has no arm along the heading (0x52CA91..0x52CAB5) and answers 1 when the tile ahead is a station tile of its ride with an arm back (0x52CAE8..0x52CB37); any other cell is passed over along the heading
		public bool Forward( TrackStation station )
		{
			var (px, py) = station.Exit;
			var heading = station.Heading;
			var done = false;
			var result = false;
			for ( var step = 0; ; step++ )
			{
				if ( !Locate( px, py, out var x, out var y ) )
					return false;
				var tile = At( x, y );
				if ( step == 0 && !IsTrack( tile ) )
					result = done = true;
				if ( IsOrdinary( tile.Kind ) )
				{
					var corner = IsCorner( tile.Arms );
					if ( corner )
					{
						var back = Opposite( heading );
						if ( (back & tile.Arms) == 0 )
							back = heading;
						heading = (byte)(tile.Arms ^ back);
					}
					else if ( Nibble( tile ) )
						ForwardFlags( x, y, tile, heading );
					if ( !corner && (tile.Arms & heading) == 0 )
						done = true;
					var (ax, ay) = (x + TrackStation.TileCells * TrackHeading.Dx( heading ), y + TrackStation.TileCells * TrackHeading.Dy( heading ));
					var ahead = At( ax, ay );
					if ( IsStationAt( ax, ay ) && ahead.Word == tile.Word && (ahead.Arms & Opposite( heading )) != 0 )
						result = done = true;
				}
				if ( done )
					return result;
				px += TrackStation.TileCells * TrackHeading.Dx( heading );
				py += TrackStation.TileCells * TrackHeading.Dy( heading );
				if ( step > limit )
					return false;
			}
		}

		/// <summary>
		/// The record at the walk's position: the executable keeps x and y as plain integers and finds the record by <c>y × 128 + x + 1</c> (0x52C7C7..0x52C7F2), so a walk that leaves a row at its end enters the
		/// next one; the cell it decodes the record's neighbours from is the record's own. False when the record lies outside the structure array (the executable reads other game memory there, undefined).
		/// </summary>
		// [BIN:TPW-EXE:0x0052C7C7 walk position] the id (y << 7) + x + 1 is truncated to 16 bits and the record read from the array of 128 x 128 records behind it (0x52C7D2..0x52C7F2); the neighbours of 0x50C3E0 come from the id the record stands at
		// [APPROX:TRACK-010] a walk that runs past the structure array (a track end with an arm into empty ground reads on until the id wraps) ends false here; the executable reads whatever lies after the array
		private bool Locate( int px, int py, out int x, out int y )
		{
			var index = py * tracks.Width + px;
			(x, y) = (-1, -1);
			if ( index < 0 || index >= tracks.Width * tracks.Height )
				return false;
			(x, y) = (index % tracks.Width, index / tracks.Width);
			return true;
		}

		private void ForwardFlags( int x, int y, Record tile, byte heading )
		{
			var flags = (int)tile.Flags;
			var ahead = Next( x, y, heading );
			var behind = Next( x, y, Opposite( heading ) );
			var arms = System.Numerics.BitOperations.PopCount( (uint)tile.Arms );
			// 0x52C87C..0x52C90F: the bridge state from which of the two neighbours are bridges
			if ( arms == 2 )
			{
				var a = Nibble( ahead );
				var b = Nibble( behind );
				flags = a && b ? 4 : !a && !b ? 1 : a ? 2 : 8;
			}
			// 0x52C916..0x52C988: a behind tile that is no track or another ride's, or a stub that ends against a track ahead
			if ( !(IsTrack( behind ) && behind.Word == tile.Word) || (IsTrack( ahead ) && arms == 1 && (tile.Arms & heading) != 0) )
			{
				if ( (flags & TrackCell.BridgeMask) != 0 )
					flags = IsTrack( ahead ) ? 8 : 2;
				if ( Nibble( ahead ) )
					flags = 4;
			}
			// 0x52C9A5..0x52CA42: the same from the other side
			if ( !(IsTrack( ahead ) && ahead.Word == tile.Word) || (IsTrack( behind ) && arms == 1 && (tile.Arms & Opposite( heading )) != 0) )
			{
				if ( (flags & TrackCell.BridgeMask) != 0 )
					flags = IsTrack( ahead ) ? 8 : 2;
				if ( Nibble( behind ) )
					flags = 4;
			}
			Assign( x, y, flags );
		}

		/// <summary>The reverse walk 0x52CCB0 from the station's entry tile against the heading.</summary>
		// [BIN:TPW-EXE:0x0052CCB0 reverse bridge walk] start = anchor + (-2, 1), (1, 1), (1, -2) or (-2, -2) by rotation with the opposite heading (0x52CCFA..0x52CD33); the same turn and end rules as the forward walk, its own flag rules (0x52CE95..0x52D0E3: ahead only gives 8 and behind only 2), and no answer: it ends when the tile ahead is a station tile of its ride with an arm back (0x52D144..0x52D187)
		public void Reverse( TrackStation station )
		{
			var (px, py) = station.Entry;
			var heading = Opposite( station.Heading );
			var done = false;
			for ( var step = 0; ; step++ )
			{
				if ( !Locate( px, py, out var x, out var y ) )
					return;
				var tile = At( x, y );
				if ( step == 0 && !IsTrack( tile ) )
					done = true;
				if ( IsOrdinary( tile.Kind ) )
				{
					var corner = IsCorner( tile.Arms );
					if ( corner )
					{
						var back = Opposite( heading );
						if ( (back & tile.Arms) == 0 )
							back = heading;
						heading = (byte)(tile.Arms ^ back);
					}
					else if ( Nibble( tile ) )
						ReverseFlags( x, y, tile, heading );
					if ( !corner && (tile.Arms & heading) == 0 )
						done = true;
					var (ax, ay) = (x + TrackStation.TileCells * TrackHeading.Dx( heading ), y + TrackStation.TileCells * TrackHeading.Dy( heading ));
					var ahead = At( ax, ay );
					if ( IsStationAt( ax, ay ) && ahead.Word == tile.Word && (ahead.Arms & Opposite( heading )) != 0 )
						done = true;
				}
				if ( done )
					return;
				px += TrackStation.TileCells * TrackHeading.Dx( heading );
				py += TrackStation.TileCells * TrackHeading.Dy( heading );
				if ( step > limit )
					return;
			}
		}

		private void ReverseFlags( int x, int y, Record tile, byte heading )
		{
			var flags = (int)tile.Flags;
			var ahead = Next( x, y, heading );
			var behind = Next( x, y, Opposite( heading ) );
			var arms = System.Numerics.BitOperations.PopCount( (uint)tile.Arms );
			if ( arms == 2 )
			{
				var a = Nibble( ahead );
				var b = Nibble( behind );
				flags = a && b ? 4 : !a && !b ? 1 : a ? 8 : 2;
			}
			if ( !(IsTrack( behind ) && behind.Word == tile.Word) )
			{
				if ( (flags & TrackCell.BridgeMask) != 0 )
					flags = IsTrack( ahead ) ? 8 : 2;
				if ( Nibble( ahead ) )
					flags = 4;
			}
			if ( IsTrack( behind ) && arms == 1 && (tile.Arms & heading) != 0 )
			{
				if ( (flags & TrackCell.BridgeMask) != 0 )
					flags = IsTrack( ahead ) ? 8 : 2;
				if ( Nibble( ahead ) )
					flags = 4;
			}
			if ( !(IsTrack( ahead ) && ahead.Word == tile.Word) )
			{
				if ( (flags & TrackCell.BridgeMask) != 0 )
					flags = IsTrack( ahead ) ? 8 : 2;
				if ( Nibble( behind ) )
					flags = 4;
			}
			if ( IsTrack( ahead ) && arms == 1 && (tile.Arms & Opposite( heading )) != 0 )
			{
				if ( (flags & TrackCell.BridgeMask) != 0 )
					flags = IsTrack( ahead ) ? 2 : 8;
				if ( Nibble( behind ) )
					flags = 4;
			}
			Assign( x, y, flags );
		}
	}
}
