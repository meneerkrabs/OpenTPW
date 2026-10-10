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
/// queue link. <see cref="GuestPathGrid"/> owns one and keeps it in step with its walkable cells; queue
/// cells live only here.
/// </summary>
public sealed class ParkCellMap
{
	private readonly byte[] types;
	private readonly ushort[] flags;
	private readonly short[] placements;
	private readonly byte[] links;
	private readonly byte[] queueLinks;

	public ParkCellMap( int width, int height )
	{
		if ( width <= 0 || height <= 0 || width > 1024 || height > 1024 )
			throw new ArgumentOutOfRangeException( nameof( width ) );
		Width = width;
		Height = height;
		types = new byte[width * height];
		flags = new ushort[width * height];
		placements = new short[width * height];
		links = new byte[width * height];
		queueLinks = new byte[width * height];
	}

	public int Width { get; }
	public int Height { get; }
	/// <summary>Incremented by every write.</summary>
	public int Version { get; private set; }

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

	/// <summary>
	/// <c>CanChangeCellType</c> (PATH-plan §3.1), in the original order: clearing; not 4 over 4; 21 over 21;
	/// path over queue; same type or an empty cell; 4 over path; queue over path only with the last-cell flag.
	/// </summary>
	// [BIN:STP-PPC:0x1008408C CanChangeCellType] the seven rules of PATH-plan §3.1; everything else is refused
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
		return type == 3 && old == 1 && lastCell;
	}
}
