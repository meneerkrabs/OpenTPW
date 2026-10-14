using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>
/// Cell attribute bits whose meaning is supported by cross-format evidence (terrain MD2 geometry
/// and heightfield holes in all four themes, level <c>Standard.sam</c> fixed-item positions and the
/// Jungle <c>Easymode.TPWI</c> cell grid). See docs/MAP.md. Bit 0x04 and the remaining bits are
/// not interpreted; use <see cref="MapFile.GetCellAt"/> for the raw value.
/// </summary>
[Flags]
public enum MapCellFlags : byte
{
	None = 0,
	/// <summary>Edge ring, rock/cliff/volcano features, water and entrance buildings. Never under a saved path or object.</summary>
	Blocked = 0x01,
	/// <summary>River/lake cells (only observed combined with <see cref="Blocked"/>, value 3).</summary>
	Water = 0x02,
	/// <summary>Pre-laid park path starting at FixedItemInfo.EntranceA/B; saved as path cells in Easymode.</summary>
	InitialPath = 0x08,
	/// <summary>
	/// Fixed entrance complex (values 17/144/148: gates, ticket booths, bus stops), a heightfield hole covered by
	/// entrance meshes in all four themes. One isolated value 16 in fantasy has no geometry and is unexplained.
	/// </summary>
	EntranceArea = 0x10,
	/// <summary>Fixed walkways that are not blocked: the bridge deck and the entrance crossings/ticket lane.</summary>
	FixedWalkway = 0x80,
	AllVerified = Blocked | Water | InitialPath | EntranceArea | FixedWalkway
}

/// <summary>
/// Terrain attribute map ("TP2M", "Theme Park 2 Attribute Map File") as stored in level
/// <c>terrain.wad</c> archives. This is not the loose/feature sound-catalog <c>.map</c> format.
/// The file's row index is the game X cell (Standard.sam <c>*PosX</c>, MD2 world X / 10) and its
/// column index the game Y cell (<c>*PosY</c>, MD2 world Z / 10). Only <see cref="MapCellFlags"/>
/// bits are interpreted; other bits stay raw.
/// </summary>
public sealed class MapFile : BaseFormat
{
	public const int MaximumFileBytes = 4 * 1024 * 1024;
	public const int MaximumDimension = 1024;
	public const int MaximumCells = 1024 * 1024;
	public const int TitleBytes = 32;
	public const int OpaqueHeaderValueCount = 5;
	private const int MapChunkHeaderBytes = 8 + OpaqueHeaderValueCount * 4;

	public string Title { get; private set; } = "";

	/// <summary>Copied, read-only 32-byte title slot, including the terminator and uninterpreted padding.</summary>
	public IReadOnlyList<byte> RawTitleSlot { get; private set; } = Array.Empty<byte>();
	public int Width { get; private set; }
	public int Height { get; private set; }

	/// <summary>Five little-endian u32 values after width/height; all observed fixtures contain 8. Meaning unknown.</summary>
	public IReadOnlyList<uint> OpaqueHeaderValues { get; private set; } = Array.Empty<uint>();

	/// <summary>Width * Height raw cell bytes in file order (column index varies fastest).</summary>
	public byte[] Cells { get; private set; } = Array.Empty<byte>();

	public MapFile( string path ) => ReadFromFile( path );
	public MapFile( Stream stream ) => ReadFromStream( stream );

	/// <summary>Encodes the parsed raw fields, preserving title padding, opaque words and every cell bit.</summary>
	// [EXT:tp2m-writer] reader-contract serialization, not native writer or save interoperability parity
	public byte[] Encode() => Encode( Width, Height, RawTitleSlot, OpaqueHeaderValues, Cells );

	/// <summary>Writes validated raw fields at the current position without closing, flushing or truncating output.</summary>
	public void Write( Stream output ) => Write( output, Width, Height, RawTitleSlot, OpaqueHeaderValues, Cells );

	/// <summary>Encodes explicitly supplied raw fields under the existing reader limits; no title or terrain conversion.</summary>
	public static byte[] Encode( int width, int height, IReadOnlyList<byte> rawTitleSlot, IReadOnlyList<uint> opaqueHeaderValues, IReadOnlyList<byte> cells )
	{
		ArgumentNullException.ThrowIfNull( rawTitleSlot );
		ArgumentNullException.ThrowIfNull( opaqueHeaderValues );
		ArgumentNullException.ThrowIfNull( cells );
		var cellCount = (long)width * height;
		var byteCount = 4L + TitleBytes + 8 + MapChunkHeaderBytes + cellCount + 8;
		if ( width < 1 || height < 1 || width > MaximumDimension || height > MaximumDimension || cellCount > MaximumCells || byteCount > MaximumFileBytes )
			throw new ArgumentException( "MAP dimensions are zero or exceed the existing reader limits." );
		if ( rawTitleSlot.Count != TitleBytes || opaqueHeaderValues.Count != OpaqueHeaderValueCount || cells.Count != cellCount )
			throw new ArgumentException( "MAP raw title, header or cell count does not match its shape." );
		Span<byte> title = stackalloc byte[TitleBytes];
		for ( var index = 0; index < title.Length; index++ ) title[index] = rawTitleSlot[index];
		if ( title.IndexOf( (byte)0 ) < 0 )
			throw new ArgumentException( "MAP raw title slot is not NUL-terminated.", nameof( rawTitleSlot ) );
		var data = new byte[(int)byteCount];
		"TP2M"u8.CopyTo( data );
		title.CopyTo( data.AsSpan( 4 ) );
		"MAP "u8.CopyTo( data.AsSpan( 4 + TitleBytes ) );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 8 + TitleBytes ), (uint)(MapChunkHeaderBytes + cellCount) );
		var payload = data.AsSpan( 12 + TitleBytes );
		BinaryPrimitives.WriteUInt32LittleEndian( payload, (uint)width );
		BinaryPrimitives.WriteUInt32LittleEndian( payload[4..], (uint)height );
		for ( var index = 0; index < OpaqueHeaderValueCount; index++ )
			BinaryPrimitives.WriteUInt32LittleEndian( payload[(8 + index * 4)..], opaqueHeaderValues[index] );
		for ( var index = 0; index < cellCount; index++ ) payload[MapChunkHeaderBytes + index] = cells[index];
		"END "u8.CopyTo( data.AsSpan( data.Length - 8 ) );
		return data;
	}

	/// <summary>Validates and encodes all raw fields before the first write; I/O failure may leave a partial caller-owned output.</summary>
	public static void Write( Stream output, int width, int height, IReadOnlyList<byte> rawTitleSlot, IReadOnlyList<uint> opaqueHeaderValues, IReadOnlyList<byte> cells )
	{
		ArgumentNullException.ThrowIfNull( output );
		if ( !output.CanWrite ) throw new NotSupportedException( "MAP output stream is not writable." );
		var data = Encode( width, height, rawTitleSlot, opaqueHeaderValues, cells );
		output.Write( data );
	}

	public byte GetCell( int column, int row )
	{
		if ( (uint)column >= (uint)Width || (uint)row >= (uint)Height )
			throw new ArgumentOutOfRangeException( column >= 0 && column < Width ? nameof( row ) : nameof( column ) );
		return Cells[row * Width + column];
	}

	/// <summary>Number of game X cells (file rows).</summary>
	public int CellCountX => Height;
	/// <summary>Number of game Y cells (file columns).</summary>
	public int CellCountY => Width;

	/// <summary>Raw cell at game cell (x, y): file row x, column y.</summary>
	public byte GetCellAt( int x, int y )
	{
		if ( (uint)x >= (uint)Height || (uint)y >= (uint)Width )
			throw new ArgumentOutOfRangeException( (uint)x >= (uint)Height ? nameof( x ) : nameof( y ) );
		return Cells[x * Width + y];
	}

	/// <summary>Verified attribute bits at game cell (x, y); unverified bits are masked out.</summary>
	public MapCellFlags GetFlagsAt( int x, int y ) => (MapCellFlags)GetCellAt( x, y ) & MapCellFlags.AllVerified;

	/// <summary>Game cells whose raw value has every bit of <paramref name="mask"/> set, in x-major order. Unmasked, for inspecting unverified bits.</summary>
	public IEnumerable<(int X, int Y, byte Raw)> EnumerateCellsWithRawBits( byte mask )
	{
		for ( var x = 0; x < Height; x++ )
			for ( var y = 0; y < Width; y++ )
			{
				var raw = Cells[x * Width + y];
				if ( (raw & mask) == mask )
					yield return (x, y, raw);
			}
	}

	protected override void ReadFromStream( Stream stream )
	{
		ArgumentNullException.ThrowIfNull( stream );
		var data = ReadBounded( stream );
		if ( data.Length < 4 + TitleBytes || !data.AsSpan( 0, 4 ).SequenceEqual( "TP2M"u8 ) )
			throw new InvalidDataException( "MAP is not a TP2M attribute map or its header is truncated." );
		var title = data.AsSpan( 4, TitleBytes );
		var terminator = title.IndexOf( (byte)0 );
		if ( terminator < 0 )
			throw new InvalidDataException( "MAP title is not NUL-terminated." );
		var position = 4 + TitleBytes;
		var sawMap = false;
		while ( true )
		{
			if ( data.Length - position < 8 )
				throw new InvalidDataException( "MAP chunk header is truncated or the END chunk is missing." );
			var tag = data.AsSpan( position, 4 );
			var size = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( position + 4 ) );
			position += 8;
			if ( size > (uint)(data.Length - position) )
				throw new InvalidDataException( "MAP chunk size exceeds the input." );
			if ( tag.SequenceEqual( "END "u8 ) )
			{
				if ( size != 0 || position != data.Length )
					throw new InvalidDataException( "MAP END chunk must be empty and final." );
				break;
			}
			if ( !tag.SequenceEqual( "MAP "u8 ) )
				throw new NotSupportedException( $"Unsupported MAP chunk tag '{Encoding.ASCII.GetString( tag )}'." );
			if ( sawMap )
				throw new InvalidDataException( "MAP contains more than one MAP chunk." );
			ReadMapChunk( data.AsSpan( position, (int)size ) );
			sawMap = true;
			position += (int)size;
		}
		if ( !sawMap )
			throw new InvalidDataException( "MAP chunk is missing." );
		Title = Encoding.ASCII.GetString( title[..terminator] );
		RawTitleSlot = Array.AsReadOnly( title.ToArray() );
	}

	private void ReadMapChunk( ReadOnlySpan<byte> chunk )
	{
		if ( chunk.Length < MapChunkHeaderBytes )
			throw new InvalidDataException( "MAP chunk header is truncated." );
		var width = BinaryPrimitives.ReadUInt32LittleEndian( chunk );
		var height = BinaryPrimitives.ReadUInt32LittleEndian( chunk[4..] );
		if ( width == 0 || height == 0 || width > MaximumDimension || height > MaximumDimension || width * height > MaximumCells )
			throw new InvalidDataException( "MAP dimensions are zero or exceed the grid limit." );
		var cells = (int)(width * height);
		if ( chunk.Length != MapChunkHeaderBytes + cells )
			throw new InvalidDataException( "MAP chunk size does not match its grid dimensions." );
		var opaque = new uint[OpaqueHeaderValueCount];
		for ( var index = 0; index < opaque.Length; index++ )
			opaque[index] = BinaryPrimitives.ReadUInt32LittleEndian( chunk[(8 + index * 4)..] );
		Width = (int)width;
		Height = (int)height;
		OpaqueHeaderValues = Array.AsReadOnly( opaque );
		Cells = chunk.Slice( MapChunkHeaderBytes, cells ).ToArray();
	}

	private static byte[] ReadBounded( Stream stream )
	{
		using var input = new MemoryStream();
		var readBuffer = new byte[8192];
		while ( true )
		{
			var count = stream.Read( readBuffer, 0, (int)Math.Min( readBuffer.Length, MaximumFileBytes - input.Length + 1 ) );
			if ( count == 0 )
				break;
			if ( count > MaximumFileBytes - input.Length )
				throw new InvalidDataException( "MAP exceeds the input byte limit." );
			input.Write( readBuffer, 0, count );
		}
		return input.ToArray();
	}
}
