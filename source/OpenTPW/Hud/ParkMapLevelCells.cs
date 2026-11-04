namespace OpenTPW.Hud;

/// <summary>
/// The level's cells, objects and tracks as the map's class writer reads them (<see cref="IParkMapCells"/>): the cell map's primary types and flags, the fence layer's posts, the track layer's kart and
/// water tiles with their rides, and the placed objects by the cell word of their anchor cell.
/// </summary>
internal sealed class LevelMapCells : IParkMapCells
{
	private readonly ParkCellMap? cells;
	private readonly Level level;
	private readonly Dictionary<ushort, List<ParkMapThing>> byAnchor = new();
	private readonly Dictionary<OriginalObject, ParkMapThing> things = new();
	private readonly HashSet<(int X, int Y)>? footprint;

	/// <summary>The map's size in cells and the objects of <paramref name="level"/>; <paramref name="cells"/> is null for a level without a cell map, whose objects then stand on their footprints.</summary>
	public LevelMapCells( Level level, ParkCellMap? cells, int width, int height, Func<OriginalObject, int, ParkMapThing> thingOf )
	{
		this.level = level;
		this.cells = cells;
		Width = width;
		Height = height;
		if ( cells == null )
			footprint = new();
		var number = 0;
		foreach ( var item in level.Objects.Objects )
		{
			if ( item.IsDeleted )
				continue;
			var thing = thingOf( item, ++number );
			things[item] = thing;
			var anchor = Level.OwnerWord( item );
			if ( !byAnchor.TryGetValue( anchor, out var list ) )
				byAnchor[anchor] = list = new();
			list.Add( thing );
			if ( footprint != null )
				foreach ( var cell in item.Cells )
					footprint.Add( (cell.X, cell.Y) );
		}
	}

	public int Width { get; }
	public int Height { get; }
	/// <summary>The object things of the level, by object.</summary>
	public IReadOnlyDictionary<OriginalObject, ParkMapThing> Things => things;

	public byte Type( int x, int y ) => cells != null ? cells.PrimaryTypeAt( x, y ) : footprint!.Contains( (x, y) ) ? ParkCellTypes.Object : (byte)0;
	public bool Unowned( int x, int y ) => cells != null && cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );

	public bool FencePost( int x, int y ) => cells?.Land?.CountsAsStructure( x, y, cells.Width, cells.Height ) == true;

	public bool TrackStructure( int x, int y )
	{
		if ( cells == null || cells.Tracks.Count == 0 )
			return false;
		var tile = cells.Tracks.Get( x, y );
		if ( tile.IsEmpty )
			return false;
		if ( !tile.IsExtension )
			return IsTileType( tile.Type );
		// the extension of a tile counts when it names an owner and its head is a tile
		return tile.Owner != 0 && tile.Parent >= 0 && tile.Parent < cells.Tracks.Width * cells.Tracks.Height
			&& IsTileType( cells.Tracks.Get( cells.Tracks.CellOf( tile.Parent ).X, cells.Tracks.CellOf( tile.Parent ).Y ).Type );
	}

	// [BIN:TPW-EXE:0x53ad30 track cell] the record types 0xb, 0xd, 0x10 and 0x12 are the kart and water tiles and their stations (0x19, the fence post, is its own class)
	private static bool IsTileType( byte type ) => type is TrackCellTypes.KartOrigin or TrackCellTypes.KartStart or TrackCellTypes.WaterOrigin or TrackCellTypes.WaterStart;

	public IEnumerable<ParkMapThing> TrackOwners( int x, int y )
	{
		if ( cells == null )
			yield break;
		var tile = cells.Tracks.Get( x, y );
		(int X, int Y) origin = tile.IsExtension && tile.Parent >= 0 ? cells.Tracks.CellOf( tile.Parent ) : (x, y);
		var head = cells.Tracks.Get( origin.X, origin.Y );
		if ( head.Owner == 0 )
			yield break;
		foreach ( var (ride, station) in level.TrackStations )
		{
			if ( !ride.IsDeleted && level.BuilderOf( ride ) is { } builder && builder.OwnerOf( station ) == head.Owner && things.TryGetValue( ride, out var thing ) )
				yield return thing;
		}
	}

	public IEnumerable<ParkMapThing> ThingsOfOwnerWord( int x, int y )
	{
		var owner = cells != null ? cells.OwnerAt( x, y ) : FootprintOwner( x, y );
		if ( owner != 0 && byAnchor.TryGetValue( owner, out var list ) )
			return list;
		return Array.Empty<ParkMapThing>();
	}

	/// <summary>The anchor word of the object whose footprint holds a cell, for a level without a cell map.</summary>
	private ushort FootprintOwner( int x, int y )
	{
		foreach ( var item in level.Objects.Objects )
			if ( !item.IsDeleted && item.Cells.Any( cell => cell.X == x && cell.Y == y ) )
				return Level.OwnerWord( item );
		return 0;
	}
}
