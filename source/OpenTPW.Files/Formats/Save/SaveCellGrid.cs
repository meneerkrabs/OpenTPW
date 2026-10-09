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
/// One per-cell record of the save's untagged prefix (see docs/TPWS-PAYLOAD.md). Only the fields
/// below are interpreted; <see cref="Record"/> keeps every byte, including the optional 10-byte
/// extension, for later work.
/// </summary>
public readonly struct SaveCell
{
	public const int RecordBytes = 84;
	public const int ExtensionBytes = 10;
	public const int AttributeOffset = 46;
	public const int PathConnectionsOffset = 8;
	public const int OccupancyOffset = 12;
	public const int PathFlagsOffset = 13;

	public SaveCell( ReadOnlyMemory<byte> record )
	{
		if ( record.Length != RecordBytes && record.Length != RecordBytes + ExtensionBytes )
			throw new ArgumentException( "A save cell record is 84 or 94 bytes.", nameof( record ) );
		Record = record;
	}

	/// <summary>Raw record bytes (84, or 94 when <see cref="HasExtension"/>).</summary>
	public ReadOnlyMemory<byte> Record { get; }

	/// <summary>Identical to the level MAP cell at the same game coordinate (16,384 of 16,384 in Easymode).</summary>
	public byte Attributes => Record.Span[AttributeOffset];

	/// <summary>Bit 0 of byte 13. All ten MAP <see cref="MapCellFlags.InitialPath"/> cells have it.</summary>
	public bool IsPath => (Record.Span[PathFlagsOffset] & 1) != 0;

	public SavePathConnections PathConnections => (SavePathConnections)Record.Span[PathConnectionsOffset];

	/// <summary>Byte 12 is nonzero on every cell of every placed object's footprint (and its queue cells).</summary>
	public bool IsOccupied => Record.Span[OccupancyOffset] != 0;

	/// <summary>Type byte bit 0x04: a 10-byte extension follows the record. Its content is not interpreted.</summary>
	public bool HasExtension => Record.Length == RecordBytes + ExtensionBytes;
}

/// <summary>
/// The per-cell grid inside the untagged prefix of a decoded TPWI/TPWS payload. Each record is
/// 84 bytes: a type byte (3 or 7 observed), opaque fields, the MAP attribute byte at +46 and the
/// bytes FF FF at +82; type bit 0x04 appends a 10-byte extension. Records are stored X-fastest.
/// The header before the grid is not decoded, so the grid start is located structurally: exactly
/// one offset in the search range must begin a run of exactly <c>countX * countY</c> valid records.
/// </summary>
public sealed class SaveCellGrid
{
	/// <summary>Search window cap (the Easymode prefix is 1,495,462 bytes); bounds the run table to 64 MiB.</summary>
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

	/// <summary>
	/// Parses the grid from <paramref name="payload"/> within its first <paramref name="searchLength"/>
	/// bytes (normally <see cref="SavePayloadLayout.PrefixLength"/>). Throws when no run or more
	/// than one run of exactly the required record count exists.
	/// </summary>
	public static SaveCellGrid Parse( ReadOnlyMemory<byte> payload, int searchLength, int countX, int countY )
	{
		if ( countX <= 0 || countY <= 0 || countX > MapFile.MaximumDimension || countY > MapFile.MaximumDimension || (long)countX * countY > MapFile.MaximumCells )
			throw new ArgumentOutOfRangeException( nameof( countX ), "Grid dimensions must match a valid MAP." );
		if ( searchLength < 0 || searchLength > payload.Length )
			throw new ArgumentOutOfRangeException( nameof( searchLength ) );
		if ( searchLength > MaximumSearchBytes )
			throw new InvalidDataException( $"Save cell grid search exceeds the {MaximumSearchBytes}-byte limit." );
		var data = payload.Span[..searchLength];
		var required = countX * countY;

		// run[s] = number of consecutive valid records starting at s (capped at required + 1).
		var run = new int[data.Length + 1];
		for ( var offset = data.Length - 1; offset >= 0; offset-- )
		{
			var length = RecordLength( data, offset );
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
		var position = start;
		for ( var index = 0; index < required; index++ )
		{
			var length = RecordLength( data, position );
			result[index] = new SaveCell( payload.Slice( position, length ) );
			position += length;
		}
		return new SaveCellGrid( countX, countY, start, position, result );
	}

	private static int RecordLength( ReadOnlySpan<byte> data, int offset )
	{
		if ( data.Length - offset < SaveCell.RecordBytes )
			return 0;
		var type = data[offset];
		if ( type != 3 && type != 7 )
			return 0;
		if ( data[offset + 82] != 0xFF || data[offset + 83] != 0xFF )
			return 0;
		var length = (type & 4) != 0 ? SaveCell.RecordBytes + SaveCell.ExtensionBytes : SaveCell.RecordBytes;
		return length <= data.Length - offset ? length : 0;
	}
}
