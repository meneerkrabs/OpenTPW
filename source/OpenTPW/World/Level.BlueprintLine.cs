namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// The path or queue tool's preview line as the original's ghost marks it (<see cref="BlueprintLineGhost"/>): each cell of the hover's line takes the validator's answer, with the money of the park, the ride under
	/// the queue tool and the heights of the typed map. Null for the other tools and for a world that types no cells, whose ghosts are the host's own line (a harness world; a Theme Park Inc world takes the same branch, which is BLUEPRINT-001's open residual, not an extension).
	/// </summary>
	// [EXT:untyped-harness-world] an OpenTPW harness world does not type its cells and has nothing for the validator to read: its ghost is the host's preview line (cells before its first refusal blue, the rest red); a Theme Park Inc world takes the same branch without being an extension (BLUEPRINT-001)
	internal LineGhostResult? PathLineGhost( SegmentResult ghost )
	{
		if ( !TypesFootprints || Guests?.Grid.Cells is not { } cells || ghost.Line.Count == 0 )
			return null;
		var queue = ParkCommandLineWriter.Unwrap( CellTool.Writer ) as QueueLineWriter;
		var mode = CellTool.Mode switch { CellToolMode.Path => 1, CellToolMode.Queue when queue != null => 3, _ => 0 };
		if ( mode == 0 )
			return null;
		var economy = Park?.Economy;
		var price = economy == null ? 0 : (int)economy.CellCost( mode == 1 ? CellPurchase.Path : CellPurchase.Queue );
		// [BIN:TPW-EXE:0x00524724 hover flag] the hover sets 0x818688 when the line has a start and the snapped end is that cell
		var map = new ParkCellGhostMap( cells, mode, OwnerPriorityAt, BlueprintHeightOffset );
		return BlueprintLineGhost.Run( map, ghost.Line, new LineGhostTool( mode, queue?.Ride.OwnerWord ?? 0, CellTool.Start == ghost.SnappedEnd, price, economy == null ? null : unchecked( (int)economy.Balance ) ) );
	}

	/// <summary>The kart and water line tool's preview as the original's tile ghost marks it (<see cref="TileLineGhost"/>); null for the other tools and for a world that types no cells.</summary>
	/// <remarks>Computed once for a hover: the markers and the pointer share the result until the line, the tool, the cells, the tracks or the money change.</remarks>
	// [EXT:untyped-harness-world] a world that does not type its cells has no records for the tile chooser to read: its ghost is the host's own line (a Theme Park Inc world takes the same branch, BLUEPRINT-001's open residual)
	internal TileLineGhostResult? TileLineGhostFor( SegmentResult ghost )
	{
		if ( !TypesFootprints || Guests?.Grid.Cells is not { } cells || OriginalPark?.Heightfield is not { } field || ghost.Symbols.Count > 0 )
			return null;
		if ( ParkCommandLineWriter.Unwrap( CellTool.Writer ) is not TrackLineWriter writer )
			return null;
		var economy = Park?.Economy;
		var key = new TileGhostKey( ghost, ghost.Start, ghost.SnappedEnd, CellTool.Start, writer, cells, cells.Version, GroundStamp( cells, field ), economy == null ? null : unchecked( (int)economy.Balance ) );
		if ( tileLineMemo is { } known && known.Key == key )
			return known.Result;
		var mode = writer.Mode == CellToolMode.WaterTrack ? 0x10 : 0xB;
		var price = economy == null ? 0 : (int)economy.CellCost( mode == 0xB ? CellPurchase.KartTrack : CellPurchase.WaterTrack );
		// [BIN:TPW-EXE:0x00524724 hover flag] the hover sets 0x818688 when the line has a start and the snapped end is that cell
		var tool = new LineGhostTileTool( unchecked( (ushort)writer.Owner ), TileLineGhost.ToolPriority, price, CellTool.Start == ghost.SnappedEnd, (0, 0), (0, 0) );
		var latches = new TileLatches { Bank = key.Balance };
		var result = TileLineGhost.Run( TileMap( cells, field ), mode, ghost.Start, ghost.SnappedEnd, tool, latches );
		tileLineMemo = (key, result);
		return result;
	}

	private readonly record struct TileGhostKey( object Ghost, (int X, int Y) Start, (int X, int Y) End, (int X, int Y)? ToolStart, object Writer, ParkCellMap Cells, int CellsVersion, GroundStampKey Ground, int? Balance );

	/// <summary>Everything besides the cells that the tile ghost's reads depend on: the terrain (a heightfield is never written, so its identity stands for it), the placed objects (their owners' priorities and height maps) and the track layers (the pieces' own ground).</summary>
	internal readonly record struct GroundStampKey( ModelHeightfield Field, int ObjectsVersion, int TracksVersion, int PieceTracksVersion );

	internal static GroundStampKey GroundStampOf( ParkCellMap cells, ModelHeightfield field, ParkObjects objects, TrackCellLayer? pieceTracks ) =>
		new( field, objects.Version, cells.Tracks.Version, pieceTracks?.Version ?? -1 );

	private GroundStampKey GroundStamp( ParkCellMap cells, ModelHeightfield field ) => GroundStampOf( cells, field, Objects, waterTracks?.Tracks );

	private (TileGhostKey Key, TileLineGhostResult Result)? tileLineMemo;
	private ParkTileGhostMap? tileMap;
	private ParkCellMap? tileMapCells;

	/// <summary>The tile ghost's view of the typed map, built once for a park's cells.</summary>
	private ParkTileGhostMap TileMap( ParkCellMap cells, ModelHeightfield field )
	{
		if ( tileMap == null || !ReferenceEquals( tileMapCells, cells ) )
		{
			tileMapCells = cells;
			tileMap = new ParkTileGhostMap( cells, OwnerPriorityAt, ( x, y ) => BlueprintGround( cells, field, x, y ) );
		}
		return tileMap;
	}

	/// <summary>The raise of a tile's marker, <see cref="TileLineGhost.TileRaise"/> over the typed map; null in a world that types no cells.</summary>
	private float? BlueprintTileRaise( int x, int y )
	{
		if ( !TypesFootprints || Guests?.Grid.Cells is not { } cells || OriginalPark?.Heightfield is not { } field )
			return null;
		return TileLineGhost.TileRaise( TileMap( cells, field ), x, y );
	}

	/// <summary>
	/// The Buy Land tool's fence ghost as the original draws it (<c>0x53AAC0</c> with the tool mode 0x19): the tile chooser's tiles along the four edges of the rectangle from the stored corner to the hovered post (the hovered
	/// post's tile alone before the first click), all red while the land validator refuses the rectangle. Null in a world that types no cells.
	/// </summary>
	// [BIN:TPW-EXE:0x00523A50 fence passes] the hover runs the walker four times (the top edge from the left post two cells in to the right post two cells in, the right edge, the bottom edge, the left edge back up), the refused rectangle having set the validator's sticky word 0x816D50 first (0x531A50)
	internal List<((int X, int Y) Origin, int Answer)>? BuyLandGhost( BuyLandTool tool )
	{
		if ( !TypesFootprints || Guests?.Grid.Cells is not { Land: { } land } cells || OriginalPark?.Heightfield is not { } field || tool.HoverPost is not { } post )
			return null;
		var key = (tool.Corner, post, tool.HoverRefused, cells, cells.Version, GroundStamp( cells, field ));
		if ( buyLandMemo is { } known && known.Key == key )
			return known.Tiles;
		var map = TileMap( cells, field );
		var gate = new LineGhostTileTool( 0, TileLineGhost.ToolPriority, 0, false, land.PostA, land.PostB );
		var latches = new TileLatches { Refuse = tool.HoverRefused };
		var tiles = new List<((int X, int Y) Origin, int Answer)>();
		foreach ( var (from, to) in TileLineGhost.FencePasses( tool.Corner, post ) )
		{
			var result = TileLineGhost.Run( map, 0x19, from, to, gate, latches );
			latches = result.Latches;
			tiles.AddRange( result.Tiles );
		}
		buyLandMemo = (key, tiles);
		return tiles;
	}

	private (((int X, int Y)? Corner, (int X, int Y) Post, bool Refused, ParkCellMap Cells, int CellsVersion, GroundStampKey Ground) Key, List<((int X, int Y) Origin, int Answer)> Tiles)? buyLandMemo;
}
