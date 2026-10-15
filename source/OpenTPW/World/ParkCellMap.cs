namespace OpenTPW;

/// <summary>
/// Original map cell types that the build tools write (docs/reverse/PATH-plan.md §2). Other original
/// types (objects, tracks, terrain) are kept as raw bytes and named only when a tool needs them.
/// </summary>
public enum ParkCellType : byte
{
	Empty = 0,
	Path = 1,
	Queue = 3
}

/// <summary>Map cell flag bits the build tools read (PATH-plan §5.2, §5.4).</summary>
[Flags]
public enum ParkCellFlags : ushort
{
	None = 0,
	NoModify = 0x20,
	Unowned = 0x40
}

/// <summary>
/// The CPU-only park cell map shared by the path and queue tools (PATH-plan §9.1/§9.6): per cell the
/// original type byte, flags, the same-type placement counter, the cardinal links (1, 4, 16, 64) and the
/// queue link. It is the source of truth for path and queue cells: <see cref="GuestPathGrid"/> is a view
/// that derives the guests' walk graph from it (path cells are walkable, their links are the connections).
/// </summary>
public sealed class ParkCellMap
{
	private readonly byte[] types;
	private readonly ushort[] flags;
	private readonly short[] placements;
	private readonly byte[] links;
	private readonly byte[] queueLinks;
	private readonly int[] typeCounts = new int[256];

	public ParkCellMap( int width, int height )
	{
		if ( width <= 0 || height <= 0 || width > 1024 || height > 1024 )
			throw new ArgumentOutOfRangeException( nameof( width ) );
		Width = width;
		Height = height;
		types = new byte[width * height];
		Tracks = new TrackCellLayer( width, height );
		flags = new ushort[width * height];
		placements = new short[width * height];
		links = new byte[width * height];
		queueLinks = new byte[width * height];
		typeCounts[0] = width * height;
	}

	/// <summary>
	/// The map of an original level (PATH-plan §9.1): Easymode save path cells with their saved links, else MAP
	/// InitialPath cells linked to every cardinal neighbour; MAP InitialPath path cells carry NoModify (§5.4).
	/// </summary>
	// [BIN:STP-PPC:0x10085538 map initialisation] MAP attribute bit 0x08 (InitialPath) makes the cell type 1 with flag 0x20 (NOMODIFY)
	public static ParkCellMap FromOriginal( MapFile map, OriginalParkImport? save )
	{
		var cells = new ParkCellMap( map.CellCountX, map.CellCountY );
		for ( var y = 0; y < map.CellCountY; y++ )
		{
			for ( var x = 0; x < map.CellCountX; x++ )
			{
				var initial = map.GetFlagsAt( x, y ).HasFlag( MapCellFlags.InitialPath );
				if ( save != null && save.Cells[x, y].IsPath )
					cells.SetPath( x, y, GuestPathGrid.ToCellLinks( GuestPathGrid.ToLinks( save.Cells[x, y].PathConnections ) ) );
				else if ( initial )
					cells.SetPath( x, y, GuestPathGrid.ToCellLinks( 0x0F ) );
				else
					continue;
				if ( initial )
					cells.SetFlags( x, y, ParkCellFlags.NoModify );
			}
		}
		return cells;
	}

	/// <summary>
	/// The map of an original level with its land: <see cref="FromOriginal(MapFile, OriginalParkImport?)"/>, then the ownership of
	/// an imported save (its map flags 0x40 and its fence, walked from post A) or, for a new park, the level's own fence
	/// (<paramref name="land"/>) and the land it encloses. Without <paramref name="land"/> every cell stays owned.
	/// </summary>
	public static ParkCellMap FromOriginal( MapFile map, OriginalParkImport? save, ParkLandSetup? land )
	{
		var cells = FromOriginal( map, save );
		if ( land != null )
			cells.SetUpLand( land, save );
		return cells;
	}

	/// <summary>
	/// Sets up the land of an original level (see <see cref="FromOriginal(MapFile, OriginalParkImport?, ParkLandSetup?)"/>):
	/// an imported save keeps its own ownership; its fence is walked from its hoarding cells, the level's fence standing in when
	/// that walk fails. A new park gets the level's fence and its land (<see cref="ParkLand.ApplyToNewPark"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x4d0b30 cell base serializer] a save keeps each map cell's mFlags, so its 0x40 cells are the saved park's unowned land, and each hoarding's mHoardingNeighbours, its fence
	public void SetUpLand( ParkLandSetup setup, OriginalParkImport? save )
	{
		ArgumentNullException.ThrowIfNull( setup );
		ParkLand? fence = null;
		try
		{
			fence = ParkLand.FromClicks( setup );
		}
		catch ( InvalidDataException exception )
		{
			Log.Warning( $"The level's park fence cannot be laid: {exception.Message} Every cell stays owned." );
		}
		if ( save != null && save.Cells.CountX == Width && save.Cells.CountY == Height )
		{
			var imported = ParkLand.FromFenceLinks( setup.PostA, setup.PostB, ( x, y ) =>
				InBounds( x, y ) && save.Cells[x, y].StructureType == SaveCell.HoardingType ? save.Cells[x, y].HoardingNeighbours : null );
			if ( imported == null )
				Log.Warning( "The imported park's fence does not run from post A to post B; the level's fence stands in for Buy Land." );
			for ( var y = 0; y < Height; y++ )
			{
				for ( var x = 0; x < Width; x++ )
				{
					var unowned = (save.Cells[x, y].MapFlags & (ushort)ParkCellFlags.Unowned) != 0;
					var flags = FlagsAt( x, y );
					if ( unowned != flags.HasFlag( ParkCellFlags.Unowned ) )
						SetFlags( x, y, unowned ? flags | ParkCellFlags.Unowned : flags & ~ParkCellFlags.Unowned );
				}
			}
			SetLand( imported ?? fence );
			return;
		}
		if ( fence == null )
			return;
		fence.ApplyToNewPark( this );
		SetLand( fence );
	}

	private void SetPath( int x, int y, byte cellLinks )
	{
		SetType( x, y, ParkCellType.Path );
		SetLinks( x, y, cellLinks );
	}

	public int Width { get; }
	public int Height { get; }
	/// <summary>The track layer (kart and water track tiles, docs/TRACKS.md); empty until a track is laid.</summary>
	public TrackCellLayer Tracks { get; }
	/// <summary>The park's fence and enclosed land (Buy Land); null for a map without one, where no cell is made unowned.</summary>
	public ParkLand? Land { get; private set; }

	/// <summary>Replaces the land model (a purchase, a load); the cells' ownership flags are written separately.</summary>
	public void SetLand( ParkLand? land )
	{
		Land = land;
		Version++;
	}
	/// <summary>Incremented by every write.</summary>
	public int Version { get; private set; }

	/// <summary>Number of cells whose type byte is <paramref name="type"/>.</summary>
	public int CountOf( ParkCellType type ) => typeCounts[(byte)type];

	public bool InBounds( int x, int y ) => (uint)x < (uint)Width && (uint)y < (uint)Height;
	private int Index( int x, int y ) => InBounds( x, y ) ? y * Width + x : throw new ArgumentOutOfRangeException( nameof( x ) );

	/// <summary>The original type byte (map cell <c>+8</c>); 0 outside the map.</summary>
	public byte RawTypeAt( int x, int y ) => InBounds( x, y ) ? types[y * Width + x] : (byte)0;
	public ParkCellType TypeAt( int x, int y ) => (ParkCellType)RawTypeAt( x, y );
	public ParkCellFlags FlagsAt( int x, int y ) => InBounds( x, y ) ? (ParkCellFlags)flags[y * Width + x] : ParkCellFlags.None;
	/// <summary>Extra placements of the same type (map cell <c>+32</c>).</summary>
	public short PlacementCountAt( int x, int y ) => InBounds( x, y ) ? placements[y * Width + x] : (short)0;
	/// <summary>Cardinal link bits 1 (−Y), 4 (+X), 16 (+Y), 64 (−X) (map cell <c>+12</c>).</summary>
	public byte LinksAt( int x, int y ) => InBounds( x, y ) ? links[y * Width + x] : (byte)0;
	/// <summary>The queue link (map cell <c>+13</c>): one of 1, 4, 16, 64 toward the queue's predecessor, or 0.</summary>
	public byte QueueLinkAt( int x, int y ) => InBounds( x, y ) ? queueLinks[y * Width + x] : (byte)0;

	public void SetType( int x, int y, byte type )
	{
		var index = Index( x, y );
		typeCounts[types[index]]--;
		typeCounts[type]++;
		types[index] = type;
		if ( type != (byte)ParkCellType.Queue )
			queueLinks[index] = 0;
		if ( type == 0 )
		{
			links[index] = 0;
			placements[index] = 0;
		}
		Version++;
	}

	public void SetType( int x, int y, ParkCellType type ) => SetType( x, y, (byte)type );

	public void SetLinks( int x, int y, byte value )
	{
		links[Index( x, y )] = value;
		Version++;
	}

	public void SetQueueLink( int x, int y, byte value )
	{
		queueLinks[Index( x, y )] = value;
		Version++;
	}

	public void SetFlags( int x, int y, ParkCellFlags value )
	{
		flags[Index( x, y )] = (ushort)value;
		Version++;
	}

	public void SetPlacementCount( int x, int y, short value )
	{
		placements[Index( x, y )] = value;
		Version++;
	}

	/// <summary>Marks the map changed without a cell write (cached walk fields are rebuilt).</summary>
	public void Touch() => Version++;

	/// <summary>
	/// Sets the change counter a <see cref="ParkLayoutSnapshot"/> recorded: restoring writes every cell with the
	/// normal setters (each bumping <see cref="Version"/>), then aligns the counter with the saved park so cached
	/// state derived from it (the guests' walk graph) keeps the saved epoch.
	/// </summary>
	internal void RestoreVersion( int version ) => Version = version >= 0 ? version : throw new ArgumentOutOfRangeException( nameof( version ) );

	/// <summary>
	/// Cell-map part of <see cref="WorldStateHash"/>: every non-empty cell in row order with its type, flags,
	/// placement counter, links and queue link (the cells guests walk and queue on, and the build tools read).
	/// </summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( Width );
		hash.Add( Height );
		var count = 0;
		for ( var index = 0; index < types.Length; index++ )
			count += types[index] != 0 || flags[index] != 0 || placements[index] != 0 || links[index] != 0 || queueLinks[index] != 0 ? 1 : 0;
		hash.Add( count );
		for ( var index = 0; index < types.Length; index++ )
		{
			if ( types[index] == 0 && flags[index] == 0 && placements[index] == 0 && links[index] == 0 && queueLinks[index] == 0 )
				continue;
			hash.Add( index );
			hash.Add( types[index] | flags[index] << 8 | (long)links[index] << 24 | (long)queueLinks[index] << 32 | (long)(ushort)placements[index] << 40 );
		}
		// The track layer joins the digest only while a track exists, so every trackless park keeps its earlier pins.
		if ( Tracks.Count > 0 )
			Tracks.AddCanonicalState( hash );
		// The land joins it only on a map that has one (an original level), for the same reason.
		Land?.AddCanonicalState( hash );
	}

	/// <summary>
	/// <c>CanChangeCellType</c> (PATH-plan §3.1), in the original order: clearing; not 4 over 4; 21 over 21;
	/// path over queue; same type or an empty cell; 4 over path; queue over path except on a line's last cell.
	/// </summary>
	// [BIN:STP-PPC:0x1008408C CanChangeCellType] the seven rules of PATH-plan §3.1; everything else is refused. Rule 7 allows queue over path while data:0x84b2c is 0; the line tool 0x10084EA4 sets that word to 1 only for a line's last cell
	public static bool CanChangeCellType( byte old, byte type, bool lastCell )
	{
		if ( type == 0 )
			return true;
		if ( type == 4 && old == 4 )
			return false;
		if ( type == 21 && old == 21 )
			return true;
		if ( type == 1 && old == 3 )
			return true;
		if ( type == old || old == 0 )
			return true;
		if ( type == 4 && old == 1 )
			return true;
		return type == 3 && old == 1 && !lastCell;
	}
}
