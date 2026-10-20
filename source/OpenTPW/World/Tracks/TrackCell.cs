namespace OpenTPW;

/// <summary>The track families of the original piece tables: kart tracks (family 3) and water tracks (family 4).</summary>
public enum TrackFamily
{
	None = 0,
	Kart = 3,
	Water = 4,
	/// <summary>The bumper cars: a render family only (the cars drive on the ground inside the ride's arena and no cell carries it).</summary>
	Bumper = 5
}

/// <summary>
/// Map cell types the track tools write (docs/TRACKS.md). A track tile is 2 × 2 map cells: the origin cell carries the
/// tool type (11 kart, 16 water), the other three are type 12 and name their origin.
/// </summary>
public static class TrackCellTypes
{
	public const byte KartOrigin = 11;
	/// <summary>The three non-origin cells of a tile; the origin cell is in the record's parent.</summary>
	public const byte Extension = 12;
	/// <summary>Station tile of a kart ride (presumed from the walk's accepted types; low confidence).</summary>
	public const byte KartStart = 13;
	public const byte WaterOrigin = 16;
	/// <summary>Station tile of a water ride (presumed from the walk's accepted types; low confidence).</summary>
	public const byte WaterStart = 18;
	/// <summary>The water table's second type (selector tables of types 16 and 25); no writer is known.</summary>
	public const byte WaterAlternate = 25;

	/// <summary>Cell types a circuit walk accepts as a track tile.</summary>
	// [BIN:TPW-EXE:0x0052A970 circuit walk] each step requires a cell of type 11, 13, 16, 18 or 25 (the walk of Q3), else it fails
	public static bool IsWalkable( byte type ) => type is KartOrigin or KartStart or WaterOrigin or WaterStart or WaterAlternate;

	public static bool IsStation( byte type ) => type is KartStart or WaterStart;
	public static bool IsOrigin( byte type ) => IsWalkable( type );
	public static TrackFamily FamilyOf( byte type ) => type switch
	{
		KartOrigin or KartStart or 3 => TrackFamily.Kart,
		WaterOrigin or WaterStart or WaterAlternate => TrackFamily.Water,
		_ => TrackFamily.None
	};
}

/// <summary>The four cardinal connection bits of a track cell (map cell +0xC) and their headings.</summary>
public static class TrackHeading
{
	public const byte NegY = 0x01;
	public const byte PosX = 0x04;
	public const byte PosY = 0x10;
	public const byte NegX = 0x40;

	public static readonly byte[] All = { NegY, PosX, PosY, NegX };

	public static int Dx( byte heading ) => heading switch { PosX => 1, NegX => -1, _ => 0 };
	public static int Dy( byte heading ) => heading switch { PosY => 1, NegY => -1, _ => 0 };

	public static byte Opposite( byte heading ) => heading switch
	{
		NegY => PosY,
		PosY => NegY,
		PosX => NegX,
		NegX => PosX,
		_ => throw new ArgumentOutOfRangeException( nameof( heading ) )
	};

	/// <summary>The heading of a station placed at rotation 0, 90, 180 or 270 degrees.</summary>
	// [BIN:STP-PPC:0x10077588 initial heading] the walk's first heading from the station rotation: 0° → +X, 90° → −Y, 180° → −X, 270° → +Y
	public static byte FromRotation( int rotation ) => rotation switch
	{
		0 => PosX,
		90 => NegY,
		180 => NegX,
		270 => PosY,
		_ => throw new ArgumentOutOfRangeException( nameof( rotation ), "Rotations are multiples of 90 degrees." )
	};

	public static int Count( byte mask ) => System.Numerics.BitOperations.PopCount( (uint)(mask & 0x55) );
}

/// <summary>
/// One track cell record, the managed twin of the original's 40-byte map cell (docs/TRACKS.md): type, connection mask,
/// flow link, flags, parent, the chosen family/piece/rotation, the overlap counter, the alternate mask and the circuit's
/// segment number. The cell index (+0) is the cell's own position and the mesh instance (+4) belongs to the renderer.
/// </summary>
public struct TrackCell
{
	/// <summary>Cell type (+8); 0 when the cell holds no track.</summary>
	public byte Type;
	/// <summary>Map type preserved beneath a station marker; zero for legacy cells and ordinary track tiles.</summary>
	public byte BaseType;
	/// <summary>Connection mask (+0xC): 0x01 −Y, 0x04 +X, 0x10 +Y, 0x40 −X.</summary>
	public byte Mask;
	/// <summary>Flow link (+0xD): the heading bit the last circuit walk moved along here.</summary>
	public byte Link;
	/// <summary>Flags (+0xE): bits 0–3 the bridge state, 0x10 an add-on.</summary>
	public byte Flags;
	/// <summary>The origin cell's index (<c>y × width + x</c>) in an extension cell (+0x10); −1 in an origin cell.</summary>
	public int Parent;
	/// <summary>Chosen piece family (+0x14): 3 kart, 4 water, 0 none.</summary>
	public int Family;
	/// <summary>Chosen piece (+0x18).</summary>
	public int Piece;
	/// <summary>Chosen rotation in degrees (+0x1C).</summary>
	public int Rotation;
	/// <summary>Overlap counter (+0x20): how often the same tile type was written again.</summary>
	public short Overlap;
	/// <summary>Alternate mask while previewing (+0x22).</summary>
	public short AltMask;
	/// <summary>Segment number the circuit walk wrote (+0x24); 0 outside a valid walk.</summary>
	public int Segment;
	/// <summary>
	/// The ride the tile belongs to: the origin-cell index of its station's first tile plus one, 0 for a tile laid without a ride (OpenTPW's own field, TRACK-021).
	/// Extension cells carry their origin's owner. Two tiles of different rides never connect and a demolition removes exactly its ride's tiles.
	/// </summary>
	public int Owner;

	public readonly bool IsEmpty => Type == 0;
	public readonly bool IsExtension => Type == TrackCellTypes.Extension;
	public readonly bool IsOrigin => TrackCellTypes.IsOrigin( Type );

	public const byte FlagBridge = 0x01;
	public const byte FlagBridgeUp = 0x02;
	public const byte FlagBridgeAbove = 0x04;
	public const byte FlagBridgeDown = 0x08;
	public const byte BridgeMask = 0x0F;
	public const byte FlagAddOn = 0x10;
}

/// <summary>
/// The track layer of the park cell map: one <see cref="TrackCell"/> per map cell (original: the 40-byte records at
/// game +0x1102D8). The map's own type byte stays in sync (<see cref="ParkCellMap"/>), so guests and the object rules see
/// a track tile as an occupied cell. Empty until a track is laid.
/// </summary>
public sealed class TrackCellLayer
{
	private readonly TrackCell[] cells;

	public TrackCellLayer( int width, int height )
	{
		Width = width;
		Height = height;
		cells = new TrackCell[width * height];
	}

	public int Width { get; }
	public int Height { get; }
	/// <summary>Cells whose type is not 0.</summary>
	public int Count { get; private set; }
	/// <summary>Incremented by every write; render caches rebuild on it.</summary>
	public int Version { get; private set; }

	public bool InBounds( int x, int y ) => (uint)x < (uint)Width && (uint)y < (uint)Height;
	public int IndexOf( int x, int y ) => y * Width + x;
	public (int X, int Y) CellOf( int index ) => (index % Width, index / Width);

	public TrackCell Get( int x, int y ) => InBounds( x, y ) ? cells[y * Width + x] : default;

	public void Set( int x, int y, TrackCell cell )
	{
		if ( !InBounds( x, y ) )
			throw new ArgumentOutOfRangeException( nameof( x ) );
		ref var slot = ref cells[y * Width + x];
		Count += (cell.Type != 0 ? 1 : 0) - (slot.Type != 0 ? 1 : 0);
		slot = cell;
		Version++;
	}

	public void Clear( int x, int y ) => Set( x, y, default );

	/// <summary>Every non-empty cell in row order.</summary>
	public IEnumerable<(int X, int Y, TrackCell Cell)> Cells()
	{
		for ( var index = 0; index < cells.Length; index++ )
		{
			if ( cells[index].Type != 0 )
				yield return (index % Width, index / Width, cells[index]);
		}
	}

	/// <summary>Track part of the cell-map digest, called only while a track exists so a trackless park's pins hold.</summary>
	internal void AddCanonicalState( StateHasher hash )
	{
		hash.Add( Count );
		foreach ( var (x, y, cell) in Cells() )
		{
			hash.Add( y * Width + x );
			// XOR preserves the old stream for zero metadata, including its sign extension for high flag bytes.
			hash.Add( (cell.Type | cell.Mask << 8 | cell.Link << 16 | cell.Flags << 24 | (long)(byte)cell.Family << 32 | (long)(byte)cell.Piece << 40) ^ (long)cell.BaseType << 48 );
			hash.Add( cell.Parent );
			hash.Add( cell.Rotation );
			hash.Add( cell.Overlap | cell.AltMask << 16 );
			hash.Add( cell.Segment );
			hash.Add( cell.Owner );
		}
	}
}
