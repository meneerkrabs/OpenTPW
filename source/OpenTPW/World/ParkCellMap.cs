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
	private readonly int[] blockStamps;
	/// <summary>Cells per side of a stamp block (the original's 16 × 16-cell blocks).</summary>
	public const int StampBlock = 16;

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
		BlocksX = (width + StampBlock - 1) / StampBlock;
		BlocksY = (height + StampBlock - 1) / StampBlock;
		blockStamps = new int[BlocksX * BlocksY];
	}

	/// <summary>Stamp blocks across and down the map.</summary>
	public int BlocksX { get; }
	public int BlocksY { get; }

	/// <summary>
	/// The stamp counter (tp.exe <c>0x7CDB98</c>): raised by every write that changes a cell type, which stamps the cell's block with it.
	/// A walk keeps the counter of its set-up (<see cref="GuestNavigation.Stamp"/>). The original also raises it at every walk set-up and
	/// route test (<c>0x4D8C50</c>), which changes no comparison of a block stamp with a walk's, so OpenTPW counts the edits only.
	/// </summary>
	public int StampCounter { get; private set; }

	/// <summary>
	/// The stamp of the 16 × 16-cell block (<paramref name="blockX"/>, <paramref name="blockY"/>): the <see cref="StampCounter"/> of the last
	/// write that changed a cell type in it (or cleared one), 0 when none did (or outside the map). A walk set up at a lower counter searches again when it
	/// stands in the block (tp.exe <c>0x4D8CD0</c>, follow_path <c>0x50ED6C</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x004D8C60 block stamp] a cell type change (0x5346D0, 0x5348D0, 0x538FC0; the queue length change 0x4DE1F0 stamps its back cell) writes the counter 0x7CDB98, raised by one (0x4D8C50), into the block (y >> 4) × 33 + (x >> 4); every walk set-up takes the counter raised by one too (0x50F8E0), so a later edit always stamps above the walk; an unchanged non-zero type (0x5346D0: the same-type return at 0x5346F5, only the placement count rises) and a link write (0x522700) stamp nothing, but clearing stamps even an empty cell (0x5346EC skips that return for type 0, the stamp at 0x5347AF)
	public int BlockStamp( int blockX, int blockY ) =>
		(uint)blockX < (uint)BlocksX && (uint)blockY < (uint)BlocksY ? blockStamps[blockY * BlocksX + blockX] : 0;

	/// <summary>Restores the stamp counter and the block stamps a snapshot recorded (row by row); null leaves every block unstamped.</summary>
	internal void RestoreBlockStamps( int counter, IReadOnlyList<int>? stamps )
	{
		Array.Clear( blockStamps );
		StampCounter = counter >= 0 ? counter : throw new InvalidDataException( "the stamp counter is negative" );
		if ( stamps == null )
			return;
		if ( stamps.Count != blockStamps.Length )
			throw new InvalidDataException( $"the cell map has {blockStamps.Length} stamp blocks, the snapshot {stamps.Count}" );
		for ( var index = 0; index < blockStamps.Length; index++ )
			blockStamps[index] = stamps[index] >= 0 ? stamps[index] : throw new InvalidDataException( $"stamp block {index} is negative" );
	}

	/// <summary>The block stamps row by row, or null while no block is stamped.</summary>
	internal int[]? CaptureBlockStamps() => Array.Exists( blockStamps, stamp => stamp != 0 ) ? (int[])blockStamps.Clone() : null;

	/// <summary>
	/// The map of an original level (PATH-plan §9.1): Easymode save path cells with their saved links, else MAP
	/// InitialPath cells linked to every cardinal neighbour; MAP InitialPath path cells carry NoModify (§5.4).
	/// </summary>
	// [BIN:STP-PPC:0x10085538 map initialisation] MAP attribute bit 0x08 (InitialPath) makes the cell type 1 with flag 0x20 (NOMODIFY)
	// [APPROX:PATH-007] PATH-LAND: no cell gets the unowned flag 0x40 (every in-bounds cell counts as owned); the original sets 0x40 on every non-InitialPath cell at level load (tp.exe 0x536490 from 0x52f050), clears it inside the park fence that 0x5323f0 lays from the level's outline table 0x784e3c anchored on the gate (fill 0x531040/0x5311c0 plus a strip at the gate), refuses 0x40 cells for paths (0x535670) and saves the flags as mFlags (0x4d0b30); OpenTPW decodes neither the outline nor the saved flags, and Buy Land (tool 0x39, 0x5327e0) is not modelled — still open: the level outline table 0x784e3c (Scape data) and the saved mFlags are not imported
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
		// The level's own cells are written without stamps (map init 0x536490 sets the types directly).
		cells.RestoreBlockStamps( 0, null );
		return cells;
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
		// tp.exe's cell setter 0x5346D0 returns early for the same non-zero type only: clearing always stamps.
		var changed = types[index] != type || type == 0;
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
		if ( changed )
			blockStamps[y / StampBlock * BlocksX + x / StampBlock] = ++StampCounter;
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
		// The block stamps (WALK-007) join it once a block is stamped.
		if ( Array.Exists( blockStamps, stamp => stamp != 0 ) )
		{
			hash.Add( StampCounter );
			hash.Add( blockStamps.Length );
			foreach ( var stamp in blockStamps )
				hash.Add( stamp );
		}
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
