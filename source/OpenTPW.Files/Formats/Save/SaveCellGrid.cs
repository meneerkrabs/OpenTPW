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
	/// <summary>The map cell's <c>mDirection</c> (the queue link <c>+0x0D</c>, the first byte of the cell base): on a queue cell the link toward the cell before it, toward the ride entrance.</summary>
	public const int DirectionOffset = 1;
	/// <summary>The map cell's <c>mOverlapCounter</c> (u16, the same-type placement counter <c>+0x20</c>).</summary>
	public const int OverlapCounterOffset = 9;
	public const int OccupancyOffset = 12;
	public const int PathFlagsOffset = 13;
	public const int PathTextureOffset = 17;
	public const int PathRotationOffset = 21;
	/// <summary>The map cell's <c>mFlags</c> (u16): 0x40 unowned land, 0x20 fixed (docs/reverse/PPC-formats.md, cell grid).</summary>
	public const int MapFlagsOffset = 2;
	/// <summary>The map cell's <c>mParentID</c> (u16, the cell's owner word <c>+0x10</c>): for an object's cells the object's anchor cell word, y × 128 + x + 1; for a queue cell the ride's.</summary>
	public const int ParentIdOffset = 11;
	/// <summary>The map cell's <c>mType</c> (u32): 1 path, 2 water, 7 no terrain, 0x1e fixed walkway, ...</summary>
	public const int MapTypeOffset = 25;
	/// <summary>The structure (track) cell's <c>mType</c> (u32): 0x19 for a hoarding (park fence post).</summary>
	public const int StructureTypeOffset = 77;
	/// <summary>The map cell's <c>mLitter</c> (u32, tp.exe cell record +0x2C): the litter type 1 to 8, 0 for none.</summary>
	public const int LitterOffset = 30;
	/// <summary>The map cell's <c>mLitterCollector</c> (u16): the thing id of the handyman who claimed the litter, 0 for none.</summary>
	public const int LitterCollectorOffset = 34;
	/// <summary>The map cell's <c>mLitterScript</c> (u32): the litter sprite's script, a process pointer (the sprite-script slot word of the Scripts block).</summary>
	public const int LitterScriptOffset = 36;
	/// <summary>The map cell's <c>mTimeMarkedForLitterCollection</c> (u32, +0x34): the game turn of the claim.</summary>
	public const int LitterMarkedOffset = 47;
	/// <summary>The structure cell's <c>mHoardingNeighbours</c>: the fence links 1 (−Y), 4 (+X), 16 (+Y), 64 (−X).</summary>
	public const int HoardingNeighboursOffset = 81;
	/// <summary>The structure cell type of a hoarding (park fence post).</summary>
	public const uint HoardingType = 0x19;
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

	/// <summary>The map cell's <c>mDirection</c> (TPW records): the queue link <c>+0x0D</c> of a queue, entrance or exit cell (1, 4, 16 or 64), 0 for none.</summary>
	public byte Direction => Record.Span[DirectionOffset];

	/// <summary>The map cell's <c>mOverlapCounter</c> (TPW records): the cell's same-type placement counter <c>+0x20</c>.</summary>
	public short OverlapCounter => BinaryPrimitives.ReadInt16LittleEndian( Record.Span.Slice( OverlapCounterOffset, 2 ) );

	/// <summary>
	/// On path cells, the index into the theme's <c>PathTex</c> list (byte 17); all 78 Easymode path cells
	/// match the shape their neighbour bits call for (docs/PATHS.md, "Path textures").
	/// </summary>
	public byte PathTexture => Record.Span[PathTextureOffset];

	/// <summary>On path cells, the texture's clockwise turn in degrees (u16 at byte 21: 0, 90, 180 or 270).</summary>
	public int PathRotation => BinaryPrimitives.ReadUInt16LittleEndian( Record.Span.Slice( PathRotationOffset, 2 ) );

	// [BIN:TPW-EXE:0x4d0b30 cell base serializer] each cell record writes mDirection (+0xd), mFlags (+0xe, 2 bytes), mMeshInstance, mNeighbours, mOverlapCounter, mParentID, mTileData, mType (+8, 4 bytes) and mHoardingNeighbours (+0x22) in that order, once for the map cell (record +1) and once for the structure cell (record +53)
	/// <summary>The map cell's flags (TPW records): 0x40 marks land the park does not own.</summary>
	public ushort MapFlags => BinaryPrimitives.ReadUInt16LittleEndian( Record.Span.Slice( MapFlagsOffset, 2 ) );

	/// <summary>The map cell's owner word <c>+0x10</c> (TPW records): 0 for a cell nothing owns.</summary>
	public ushort MapParentId => BinaryPrimitives.ReadUInt16LittleEndian( Record.Span.Slice( ParentIdOffset, 2 ) );

	/// <summary>The map cell's type (TPW records).</summary>
	public uint MapType => BinaryPrimitives.ReadUInt32LittleEndian( Record.Span.Slice( MapTypeOffset, 4 ) );

	// [BIN:TPW-EXE:0x004D8DD0 map cell serializer] the PC serializer writes mLitter (+0x2C, 4 bytes), mLitterCollector (+0x30, 2), mLitterScript (+0x28 and +0x38, 4 each), mPylonIndex (+0x40, 2), mStatusFlags (+0x26, 1), mTimeMarkedForLitterCollection (+0x34, 4) and mWho (+0x24, 2) after the base 0x4D0B30, in the Mac order below (STP-PPC 0x100D6C90): mLitter (4), mLitterCollector (2), mLitterScript (4 twice), mPylonIndex (2), mStatusFlags (1), mTimeMarkedForLitterCollection (4) and mWho (2) follow the 29-byte base (docs/reverse/PPC-formats.md); the status flags byte is the MAP attribute at +46
	/// <summary>The litter in the cell (TPW records): 0 for none, else 1 to 8 (<c>LitterType</c>); another value is kept as read.</summary>
	public uint Litter => BinaryPrimitives.ReadUInt32LittleEndian( Record.Span.Slice( LitterOffset, 4 ) );

	/// <summary>The thing id of the handyman who claimed the cell's litter (TPW records), 0 for none.</summary>
	public ushort LitterCollector => BinaryPrimitives.ReadUInt16LittleEndian( Record.Span.Slice( LitterCollectorOffset, 2 ) );

	/// <summary>The cell's litter sprite script as a process pointer (TPW records): the sprite-script slot word of the Scripts block that holds the litter sprite.</summary>
	public uint LitterScript => BinaryPrimitives.ReadUInt32LittleEndian( Record.Span.Slice( LitterScriptOffset, 4 ) );

	/// <summary>The game turn the litter was claimed for collection (TPW records).</summary>
	public uint LitterMarkedTurn => BinaryPrimitives.ReadUInt32LittleEndian( Record.Span.Slice( LitterMarkedOffset, 4 ) );

	/// <summary>The structure cell's type (TPW records); <see cref="HoardingType"/> for a park fence post.</summary>
	public uint StructureType => BinaryPrimitives.ReadUInt32LittleEndian( Record.Span.Slice( StructureTypeOffset, 4 ) );

	/// <summary>The structure cell's fence links (TPW records).</summary>
	public byte HoardingNeighbours => Record.Span[HoardingNeighboursOffset];

	/// <summary>Byte 12 is nonzero on every cell of every placed object's footprint (and its queue cells).</summary>
	public bool IsOccupied => Record.Span[OccupancyOffset] != 0;

	/// <summary>Type byte bit 0x04: a 10-byte extension follows the record. Its content is not interpreted.</summary>
	public bool HasExtension => Record.Length == baseRecordBytes + ExtensionBytes;
}

/// <summary>
/// The per-cell grid (map writer 0xd6c90) inside the World block of a decoded TPWI/TPWS payload. Each record is
/// 84 bytes: a type byte (3 or 7 observed), opaque fields, the MAP attribute byte at +46 and the
/// bytes FF FF at +82; type bit 0x04 appends a 10-byte extension. Records are stored X-fastest.
/// The grid starts where the World loader's readers put it (<see cref="SaveBlockWidths.TryMeasureWorld"/>: the variables, the control manager and the macro AI have
/// fixed widths); a World block that does not read that way (synthetic payloads) has its start located structurally: exactly
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

	/// <summary>
	/// Parses the grid that starts exactly at <paramref name="start"/> (the offset the World loader's readers put it at, <see cref="SaveBlockWidths.TryMeasureWorld"/>): the
	/// <c>countX * countY</c> records must follow one another from there. Throws when they do not.
	/// </summary>
	public static SaveCellGrid ParseAt( ReadOnlyMemory<byte> payload, int start, int end, int countX, int countY )
	{
		if ( countX <= 0 || countY <= 0 || countX > MapFile.MaximumDimension || countY > MapFile.MaximumDimension || (long)countX * countY > MapFile.MaximumCells )
			throw new ArgumentOutOfRangeException( nameof( countX ), "Grid dimensions must match a valid MAP." );
		if ( end < 0 || end > payload.Length )
			throw new ArgumentOutOfRangeException( nameof( end ) );
		if ( start < 0 || start > end )
			throw new InvalidDataException( $"Save cell grid would start at offset {start}, outside the World block that ends at {end}." );
		var data = payload.Span[..end];
		var required = countX * countY;
		var result = new SaveCell[required];
		var position = start;
		for ( var index = 0; index < required; index++ )
		{
			var length = RecordLength( data, position, SaveCell.RecordBytes );
			if ( length == 0 )
				throw new InvalidDataException( $"Save cell grid record {index} at offset {position} is not a cell record." );
			result[index] = new SaveCell( payload.Slice( position, length ), SaveCell.RecordBytes );
			position += length;
		}
		return new SaveCellGrid( countX, countY, start, position, result );
	}

	/// <summary>The bytes one Theme Park Inc map cell takes after its status byte: the 56-byte map cell (bit 0), the 31-byte track cell (bit 1) and the 10-byte effects record (bit 2); the status byte's other bits are not read.</summary>
	public static int ThemeParkIncCellBytes( byte status ) =>
		SaveBlockWidths.CellStatusBytes + ((status & 1) != 0 ? ThemeParkIncSaveWorld.MapCellBytes : 0) + ((status & 2) != 0 ? SaveBlockWidths.TrackCellBytes : 0) + ((status & 4) != 0 ? SaveBlockWidths.EffectsCellBytes : 0);

	/// <summary>
	/// Parses the Theme Park Inc map that starts exactly at <paramref name="start"/> (the map loader 0x5d6d40: for each cell a status byte, then the 56-byte map cell when bit 0 is set,
	/// the 31-byte track cell when bit 1 is set and the 10-byte effects record when bit 2 is set) and must end exactly at <paramref name="end"/>. A cell with both bits 0 and 1 (every
	/// cell of the prebuilt) exposes its bytes as saved; a cell without one of the two records gets a copy with that record zero-filled, so every <see cref="SaveCell.Record"/> has the
	/// 88-byte shape (98 with the effects record) the accessors read.
	/// </summary>
	// [BIN:TPI-EXE:0x5d6d40 map loader] per cell u8 status, 56-byte map cell if bit 0, 31-byte track cell if bit 1, 10-byte effects record if bit 2
	public static SaveCellGrid ParseThemeParkIncAt( ReadOnlyMemory<byte> payload, int start, int end, int countX, int countY )
	{
		if ( countX <= 0 || countY <= 0 || countX > MapFile.MaximumDimension || countY > MapFile.MaximumDimension || (long)countX * countY > MapFile.MaximumCells )
			throw new ArgumentOutOfRangeException( nameof( countX ), "Grid dimensions must match a valid MAP." );
		if ( end < 0 || end > payload.Length )
			throw new ArgumentOutOfRangeException( nameof( end ) );
		if ( start < 0 || start > end )
			throw new InvalidDataException( $"Save cell grid would start at offset {start}, outside the World block that ends at {end}." );
		var data = payload.Span[..end];
		var required = countX * countY;
		var result = new SaveCell[required];
		var position = start;
		for ( var index = 0; index < required; index++ )
		{
			if ( position >= end )
				throw new InvalidDataException( $"Save Theme Park Inc cell grid is truncated at cell {index}." );
			var status = data[position];
			var length = ThemeParkIncCellBytes( status );
			if ( end - position < length )
				throw new InvalidDataException( $"Save Theme Park Inc cell {index} at offset {position} crosses the end of the grid." );
			result[index] = new SaveCell( (status & 3) == 3 ? payload.Slice( position, length ) : Normalize( data.Slice( position, length ), status ), SaveCell.ThemeParkIncRecordBytes );
			position += length;
		}
		if ( position != end )
			throw new InvalidDataException( $"Save Theme Park Inc cell grid ends at offset {position}, not at {end}." );
		return new SaveCellGrid( countX, countY, start, position, result );
	}

	private static byte[] Normalize( ReadOnlySpan<byte> saved, byte status )
	{
		var record = new byte[SaveCell.ThemeParkIncRecordBytes + ((status & 4) != 0 ? SaveCell.ExtensionBytes : 0)];
		record[0] = status;
		var source = 1;
		if ( (status & 1) != 0 )
		{
			saved.Slice( source, ThemeParkIncSaveWorld.MapCellBytes ).CopyTo( record.AsSpan( 1 ) );
			source += ThemeParkIncSaveWorld.MapCellBytes;
		}
		if ( (status & 2) != 0 )
		{
			saved.Slice( source, SaveBlockWidths.TrackCellBytes ).CopyTo( record.AsSpan( 1 + ThemeParkIncSaveWorld.MapCellBytes ) );
			source += SaveBlockWidths.TrackCellBytes;
		}
		if ( (status & 4) != 0 )
			saved.Slice( source, SaveCell.ExtensionBytes ).CopyTo( record.AsSpan( SaveCell.ThemeParkIncRecordBytes ) );
		return record;
	}

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
