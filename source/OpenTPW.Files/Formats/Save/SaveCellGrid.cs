using System.Buffers.Binary;
namespace OpenTPW;

/// <summary>
/// Cardinal path-connection bits of <see cref="SaveCell.PathConnections"/>. Every cardinal
/// neighbour that is itself a path cell has its bit set (78 of 78 Easymode path cells); the other
/// set cardinal bits point at adjacent object/queue cells. Odd (diagonal) bits are not interpreted.
/// </summary>
[Flags]
public enum SavePathConnections : byte
{
	None = 0,
	NegativeY = 0x01,
	PositiveX = 0x04,
	PositiveY = 0x10,
	NegativeX = 0x40,
	Cardinal = NegativeY | PositiveX | PositiveY | NegativeX
}

/// <summary>
/// One per-cell record of the save's World block map (see docs/TPWS-PAYLOAD.md). Only the fields
/// below are interpreted; <see cref="Record"/> keeps every byte, including the optional 10-byte
/// extension, for later work.
/// </summary>
public readonly struct SaveCell
{
	public const int RecordBytes = 84;
	public const int ExtensionBytes = 10;
	public const int ThemeParkIncRecordBytes = 88;
	public const int AttributeOffset = 46;
	public const int PathConnectionsOffset = 8;
	public const int OccupancyOffset = 12;
	public const int PathFlagsOffset = 13;
	public const int PathTextureOffset = 17;
	public const int PathRotationOffset = 21;
	private readonly int baseRecordBytes;

	public SaveCell( ReadOnlyMemory<byte> record ) : this( record, RecordBytes ) { }

	internal SaveCell( ReadOnlyMemory<byte> record, int baseRecordBytes )
	{
		if ( record.Length != baseRecordBytes && record.Length != baseRecordBytes + ExtensionBytes )
			throw new ArgumentException( "A save cell record has an invalid length.", nameof( record ) );
		Record = record;
		this.baseRecordBytes = baseRecordBytes;
	}

	/// <summary>Raw record bytes (84/94 for TPW, or 88/98 for Theme Park Inc).</summary>
	public ReadOnlyMemory<byte> Record { get; }

	/// <summary>Identical to the level MAP cell at the same game coordinate (16,384 of 16,384 in Easymode).</summary>
	public byte Attributes => Record.Span[AttributeOffset];

	/// <summary>Bit 0 of byte 13. All ten MAP <see cref="MapCellFlags.InitialPath"/> cells have it.</summary>
	public bool IsPath => (Record.Span[PathFlagsOffset] & 1) != 0;

	public SavePathConnections PathConnections => (SavePathConnections)Record.Span[PathConnectionsOffset];

	/// <summary>
	/// On path cells, the index into the theme's <c>PathTex</c> list (byte 17); all 78 Easymode path cells
	/// match the shape their neighbour bits call for (docs/PATHS.md, "Path textures").
	/// </summary>
	public byte PathTexture => Record.Span[PathTextureOffset];

	/// <summary>On path cells, the texture's clockwise turn in degrees (u16 at byte 21: 0, 90, 180 or 270).</summary>
	public int PathRotation => BinaryPrimitives.ReadUInt16LittleEndian( Record.Span.Slice( PathRotationOffset, 2 ) );

	/// <summary>Byte 12 is nonzero on every cell of every placed object's footprint (and its queue cells).</summary>
	public bool IsOccupied => Record.Span[OccupancyOffset] != 0;

	/// <summary>Type byte bit 0x04: a 10-byte extension follows the record. Its content is not interpreted.</summary>
	public bool HasExtension => Record.Length == baseRecordBytes + ExtensionBytes;
}

/// <summary>
/// The per-cell grid (map writer 0xd6c90) inside the World block of a decoded TPWI/TPWS payload. Each record is
/// 84 bytes: a type byte (3 or 7 observed), opaque fields, the MAP attribute byte at +46 and the
/// bytes FF FF at +82; type bit 0x04 appends a 10-byte extension. Records are stored X-fastest.
/// The control manager and macro-AI widths before the grid are not pinned, so the grid start is located structurally: exactly
/// one offset in the search range must begin a run of exactly <c>countX * countY</c> valid records.
/// </summary>
public sealed class SaveCellGrid
{
	/// <summary>Search window cap (the Easymode World block is 1,494,283 bytes); bounds the run table to 64 MiB.</summary>
	public const int MaximumSearchBytes = 16 * 1024 * 1024;

	private readonly SaveCell[] cells;

	private SaveCellGrid( int countX, int countY, int startOffset, int endOffset, SaveCell[] cells )
	{
		CountX = countX;
		CountY = countY;
		StartOffset = startOffset;
		EndOffset = endOffset;
		this.cells = cells;
	}

	public int CountX { get; }
	public int CountY { get; }
	public int StartOffset { get; }
	public int EndOffset { get; }

	public SaveCell this[int x, int y]
	{
		get
		{
			if ( (uint)x >= (uint)CountX || (uint)y >= (uint)CountY )
				throw new ArgumentOutOfRangeException( (uint)x >= (uint)CountX ? nameof( x ) : nameof( y ) );
			return cells[y * CountX + x];
		}
	}

	/// <summary>Parses the grid within the first <paramref name="searchLength"/> bytes of <paramref name="payload"/>.</summary>
	public static SaveCellGrid Parse( ReadOnlyMemory<byte> payload, int searchLength, int countX, int countY ) =>
		Parse( payload, 0, searchLength, countX, countY );

	/// <summary>
	/// Parses the grid from <paramref name="payload"/> within [<paramref name="searchStart"/>, <paramref name="searchEnd"/>)
	/// (for a save, the World block after its variables). Throws when no run or more than one run of exactly
	/// the required record count exists. Offsets stay payload offsets.
	/// </summary>
	public static SaveCellGrid Parse( ReadOnlyMemory<byte> payload, int searchStart, int searchEnd, int countX, int countY )
		=> Parse( payload, searchStart, searchEnd, countX, countY, SaveCell.RecordBytes );

	/// <summary>Parses the observed Theme Park Inc profile: 88-byte cells, a ten-byte type-7 extension, and FF FF at +86.</summary>
	public static SaveCellGrid ParseThemeParkInc( ReadOnlyMemory<byte> payload, int searchStart, int searchEnd, int countX, int countY )
		=> Parse( payload, searchStart, searchEnd, countX, countY, SaveCell.ThemeParkIncRecordBytes );

	private static SaveCellGrid Parse( ReadOnlyMemory<byte> payload, int searchStart, int searchEnd, int countX, int countY, int recordBytes )
	{
		if ( countX <= 0 || countY <= 0 || countX > MapFile.MaximumDimension || countY > MapFile.MaximumDimension || (long)countX * countY > MapFile.MaximumCells )
			throw new ArgumentOutOfRangeException( nameof( countX ), "Grid dimensions must match a valid MAP." );
		if ( searchStart < 0 || searchEnd < searchStart || searchEnd > payload.Length )
			throw new ArgumentOutOfRangeException( nameof( searchEnd ) );
		if ( searchEnd - searchStart > MaximumSearchBytes )
			throw new InvalidDataException( $"Save cell grid search exceeds the {MaximumSearchBytes}-byte limit." );
		var data = payload.Span[searchStart..searchEnd];
		var required = countX * countY;

		// run[s] = number of consecutive valid records starting at s (capped at required + 1).
		var run = new int[data.Length + 1];
		for ( var offset = data.Length - 1; offset >= 0; offset-- )
		{
			var length = RecordLength( data, offset, recordBytes );
			if ( length > 0 )
				run[offset] = Math.Min( required + 1, 1 + run[offset + length] );
		}

		var start = -1;
		for ( var offset = 0; offset < data.Length; offset++ )
		{
			if ( run[offset] < required )
				continue;
			if ( run[offset] > required || start >= 0 )
				throw new InvalidDataException( "Save cell grid is ambiguous: more than one record run matches the map size." );
			start = offset;
		}
		if ( start < 0 )
			throw new InvalidDataException( "Save payload contains no cell grid matching the map size." );

		var result = new SaveCell[required];
		start += searchStart;
		var position = start;
		for ( var index = 0; index < required; index++ )
		{
			var length = RecordLength( data, position - searchStart, recordBytes );
			result[index] = new SaveCell( payload.Slice( position, length ), recordBytes );
			position += length;
		}
		return new SaveCellGrid( countX, countY, start, position, result );
	}

	private static int RecordLength( ReadOnlySpan<byte> data, int offset, int recordBytes )
	{
		if ( data.Length - offset < recordBytes )
			return 0;
		var type = data[offset];
		if ( type != 3 && type != 7 )
			return 0;
		if ( data[offset + recordBytes - 2] != 0xFF || data[offset + recordBytes - 1] != 0xFF )
			return 0;
		var length = (type & 4) != 0 ? recordBytes + SaveCell.ExtensionBytes : recordBytes;
		return length <= data.Length - offset ? length : 0;
	}
}
