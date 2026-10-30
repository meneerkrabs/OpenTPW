using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>
/// One entry of a script record's <c>'OBJ '</c> list (0x1C bytes): the object the script drives. The type, runtime handle, key, model node and slot are the words at
/// +8, +0x0C, +0x10, +0x14 and +0x18; the two leading words are kept as read. Handles and keys are runtime values of the saving process.
/// </summary>
public readonly record struct SaveRsseObject( uint Word0, uint Word1, uint Type, uint RuntimeHandle, uint Key, uint ModelNode, uint Slot )
{
	public const int Bytes = 0x1C;
}

/// <summary>One entry of a script's bounce table (16 bytes, BOUNCE 0x555696): the rider (+0), the node number script +0x70 plus the entry's index (+4), the release deadline (+8) and the start (+0xC), both on the game clock. A free entry holds rider 0 and stale words.</summary>
// [BIN:TPW-EXE:0x00555742 BOUNCE entry] the first entry whose rider word is 0 takes the rider (0x00555736), the node (0x00555742..0x0055574F), the deadline clock + operand x 1000 (0x00555753..0x0055576C) and the start clock (0x00555770..0x00555778); script +0x6C counts the entries in use (0x0055577C, UNBOUNCE 0x005558C6 counts it down, BOUNCING 0x00555933 returns it)
public readonly record struct SaveRsseBounceEntry( uint Guest, uint Node, uint Deadline, uint Start )
{
	public bool IsFree => Guest == 0;
}

/// <summary>One entry of a script's walk table (32 bytes, WALKON 0x00556F40): the first and second node of the walk-on (+0, +2), WALKOFF's two nodes (+4, +6), the start (+8) and end (+0xC) on the game clock, the rider (+0x10), the sprite heading (+0x14), the walk action (+0x16), the state (+0x18) and the last operand (+0x1A). State 0 is a free entry.</summary>
// [BIN:TPW-EXE:0x0055706D WALKON entry] the entry is free while its state word (+0x18) is 0 (0x00557047); WALKON stores the nodes at +0/+2/+4/+6, the rider at +0x10, the action at +0x16, the start (0x005570A5) and end (0x005570AF), the last operand at +0x1A, the heading at +0x14 (0x005570FD) and state 1 (0x005570EB); WALKGET frees an entry in state 4 by zeroing the state and the rider (0x0055713F, 0x00557149)
public readonly record struct SaveRsseWalkEntry( ushort Node1, ushort Node2, ushort Node3, ushort Node4, uint Began, uint Ends, uint Guest, ushort Heading, ushort Mode, ushort State, ushort Extra )
{
	public bool IsFree => State == 0;
}

/// <summary>
/// One script record of the RSSE block: the 0xF4-byte script struct (field names from the loader's own log strings and the save/load order, tp.exe
/// <c>0x559350</c> / <c>0x5597A0</c>), its eight length-prefixed buffers, the script folder and the object list. Struct fields that hold process addresses
/// (the buffer pointers, the object-list head) are kept raw; <see cref="Raw"/> keeps every struct byte, including the ones with no name here.
/// </summary>
public sealed record SaveRsseRecord
{
	public const int Bytes = 0xF4;
	public const int WalkEntryBytes = 32, LimboEntryBytes = 8, BounceEntryBytes = 16;

	/// <summary>The 0xF4 bytes of the struct as saved.</summary>
	public required byte[] Raw { get; init; }
	/// <summary>Offset of the record's struct in the decoded payload.</summary>
	public int Offset { get; init; }

	// The buffers, in the writer's order. Lengths are as stored: bytes, except the walk table, whose stored word is an entry count of 32-byte entries.
	public required byte[] Code { get; init; }
	public required byte[] Stack { get; init; }
	public required byte[] Variables { get; init; }
	public required byte[] Strings { get; init; }
	public required byte[] Limbo { get; init; }
	public required byte[] Bounce { get; init; }
	public required byte[] Walk { get; init; }
	public required byte[] Nodes { get; init; }
	/// <summary>The script folder, for example <c>data\levels\jungle\Rides\bouncy\</c> (without its NUL terminator).</summary>
	public required string Folder { get; init; }
	public required IReadOnlyList<SaveRsseObject> Objects { get; init; }

	/// <summary>The code buffer as 32-bit words (the layout of an RSE member's code section).</summary>
	public uint[] CodeWords
	{
		get
		{
			var words = new uint[Code.Length / 4];
			for ( var index = 0; index < words.Length; index++ )
				words[index] = BinaryPrimitives.ReadUInt32LittleEndian( Code.AsSpan( index * 4 ) );
			return words;
		}
	}

	/// <summary>The bounce table's entries (<see cref="BounceSize"/> of them, 16 bytes each).</summary>
	public IReadOnlyList<SaveRsseBounceEntry> BounceEntries
	{
		get
		{
			var entries = new SaveRsseBounceEntry[Bounce.Length / BounceEntryBytes];
			for ( var index = 0; index < entries.Length; index++ )
			{
				var at = index * BounceEntryBytes;
				entries[index] = new SaveRsseBounceEntry( Word( Bounce, at ), Word( Bounce, at + 4 ), Word( Bounce, at + 8 ), Word( Bounce, at + 12 ) );
			}
			return entries;
		}
	}

	/// <summary>The walk table's entries (<see cref="WalkSize"/> of them, 32 bytes each).</summary>
	public IReadOnlyList<SaveRsseWalkEntry> WalkEntries
	{
		get
		{
			var entries = new SaveRsseWalkEntry[Walk.Length / WalkEntryBytes];
			for ( var index = 0; index < entries.Length; index++ )
			{
				var at = index * WalkEntryBytes;
				entries[index] = new SaveRsseWalkEntry( Half( Walk, at ), Half( Walk, at + 2 ), Half( Walk, at + 4 ), Half( Walk, at + 6 ), Word( Walk, at + 8 ), Word( Walk, at + 12 ),
					Word( Walk, at + 0x10 ), Half( Walk, at + 0x14 ), Half( Walk, at + 0x16 ), Half( Walk, at + 0x18 ), Half( Walk, at + 0x1A ) );
			}
			return entries;
		}
	}

	/// <summary>The head table (script +0x30, <see cref="NodeCount"/> words): the rider on each head id's slot, 0 when free (slot n is head id n + 1). The block's eighth buffer, called the node table before it was read as this.</summary>
	// [BIN:TPW-EXE:0x00554C80 head table] ADDHEAD draws a slot of the table at script +0x30 whose count is +0x4C until it holds 0 and stores the rider there (0x00554CC8..0x00554CD3); DELHEAD zeroes the slots that hold the rider; the loader reads the eighth buffer raw into +0x30 and keeps its byte count >> 2 as +0x4C (0x00559D3D..0x00559D95)
	public IReadOnlyList<uint> HeadSlots
	{
		get
		{
			var slots = new uint[Nodes.Length / 4];
			for ( var index = 0; index < slots.Length; index++ )
				slots[index] = Word( Nodes, index * 4 );
			return slots;
		}
	}

	/// <summary>The limbo table's entries as (rider, expiry on the game clock), <see cref="LimboSize"/> of them.</summary>
	public IReadOnlyList<(uint Guest, uint Expiry)> LimboEntries
	{
		get
		{
			var entries = new (uint, uint)[Limbo.Length / LimboEntryBytes];
			for ( var index = 0; index < entries.Length; index++ )
				entries[index] = (Word( Limbo, index * LimboEntryBytes ), Word( Limbo, index * LimboEntryBytes + 4 ));
			return entries;
		}
	}

	private static uint Word( byte[] bytes, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( offset ) );
	private static ushort Half( byte[] bytes, int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( bytes.AsSpan( offset ) );

	private uint U32( int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( Raw.AsSpan( offset ) );
	private ushort U16( int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( Raw.AsSpan( offset ) );
	private short I16( int offset ) => BinaryPrimitives.ReadInt16LittleEndian( Raw.AsSpan( offset ) );

	public uint ScriptId => U32( 0x08 );
	public uint ChildScriptId => U32( 0x0C );
	public uint ParentScriptId => U32( 0x10 );
	public uint SoundScriptId => U32( 0x14 );
	/// <summary>The nine buffer pointers at +0x18..+0x38 (process addresses, raw).</summary>
	public uint[] BufferPointers => Enumerable.Range( 0, 9 ).Select( index => U32( 0x18 + index * 4 ) ).ToArray();
	public uint ProgramCounter => U32( 0x3C );
	public uint StackPointer => U32( 0x40 );
	/// <summary>The result/flag register (+0x48).</summary>
	public uint ResultRegister => U32( 0x48 );
	public uint NodeCount => U32( 0x4C );
	public uint CodeWordCount => U32( 0x50 );
	public uint StackSize => U32( 0x54 );
	public uint LimboSize => U32( 0x58 );
	public uint LimboInUse => U32( 0x60 );
	public uint BounceSize => U32( 0x64 );
	public short BounceCount => I16( 0x6C );
	public short BounceBase => I16( 0x6E );
	public uint BounceNode => U32( 0x70 );
	/// <summary>The NAME word (+0x74).</summary>
	public uint Name => U32( 0x74 );
	public uint WalkSize => U32( 0x7C );
	public uint WalkFloat0 => U32( 0x80 );
	public uint WalkFloat1 => U32( 0x84 );
	public uint VariableCount => U32( 0x8C );
	public uint StringBytes => U32( 0x90 );
	public uint TimeSlice => U32( 0x94 );
	public uint YieldWord => U32( 0x98 );
	/// <summary>The WAIT deadline (+0xA0), a game-clock value.</summary>
	public uint WaitDeadline => U32( 0xA0 );
	public uint AnimationWait0 => U32( 0xA4 );
	public uint AnimationWait1 => U32( 0xA8 );
	public uint AnimationWait2 => U32( 0xBC );
	/// <summary>The attraction thing the script belongs to (+0xAC).</summary>
	public ushort ThingId => U16( 0xAC );
	public uint ObjectListHead => U32( 0xB0 );
	public byte Turbo => Raw[0xB8];
	public byte DipMusic => Raw[0xB9];
	public short Speed => I16( 0xC0 );
	public uint Timer => U32( 0xC4 );
	/// <summary>The mesh instance the script animates (+0xC8); an attraction's <see cref="SaveAttraction.MeshInstanceId"/>.</summary>
	public uint MeshInstance => U32( 0xC8 );
	/// <summary>The repair effect's emitter handle (+0xCC): REPAIREFFECT keeps it, the script's destruction ends it (0x0055853E, 0x00558553); 0 when none.</summary>
	public uint RepairEffect => U32( 0xCC );
	public uint Scream => U32( 0xD0 );
	public uint SoundEmitter => U32( 0xD4 );
	public uint Reverb => U32( 0xD8 );
	public ushort SparkA => U16( 0xDC );
	public ushort SparkB => U16( 0xDE );
	public uint Coast => U32( 0xE0 );
	public ushort AnimationSpeed => U16( 0xE4 );
	public short Bump => I16( 0xE6 );
}

/// <summary>
/// The RSSE block (writer tp.exe <c>0x559350</c>, loader <c>0x5597A0</c>): <c>'RSSE'</c>, <c>u32 0x14</c> and the 20-byte header (the script system's globals at
/// <c>0x8791A0</c>: initialised flag, tick counter, next script id, record count and the list-head pointer, raw), five <c>'PAD_'</c> words the loader discards,
/// <c>u32</c> record count, <c>u32 0xF4</c> record size, then per record the struct (<see cref="SaveRsseRecord"/>), eight length-prefixed buffers (code, stack, variables,
/// string blob, limbo table, bounce table, walk table, node table; the walk table's stored word counts 32-byte entries, the others are byte lengths), the script folder
/// (<c>u32</c> length, NUL-terminated text) and the object list (<c>'OBJ '</c>, <c>u32</c> count, <c>u32 0x1C</c>, entries).
/// </summary>
// [BIN:TPW-EXE:0x559350 RSSE writer] [BIN:TPW-EXE:0x5597A0 RSSE loader] header at 0x8791A0, 0xF4-byte script struct, eight counted buffers, folder string, 'OBJ ' list of 0x1C-byte entries
public sealed record SaveRsseBlock( uint Initialised, uint TickCounter, uint NextScriptId, uint HeaderRecordCount, uint ListHead, IReadOnlyList<SaveRsseRecord> Records, int Length )
{
	public const uint Magic = 0x45535352;
	public const uint ObjectsMagic = 0x204A424F;
	public const int HeaderBytes = 0x14;
	public const int PadWords = 5;
	public const int RecordBytes = SaveRsseRecord.Bytes;
	public const int BufferCount = 8;
	/// <summary>Bound on a script's folder text (a path).</summary>
	public const int MaximumFolderBytes = 4096;

	/// <summary>Reads the RSSE block of <paramref name="layout"/> from <paramref name="payload"/>; the block must be consumed exactly.</summary>
	public static SaveRsseBlock Read( ReadOnlySpan<byte> payload, SavePayloadLayout layout )
	{
		ArgumentNullException.ThrowIfNull( layout );
		if ( layout.PayloadLength != payload.Length )
			throw new ArgumentException( "Layout does not describe this payload.", nameof( layout ) );
		var block = layout[SaveBlock.Rsse];
		var read = Parse( payload.Slice( 0, block.End ), block.Offset );
		if ( read.Length != block.Length )
			throw new InvalidDataException( $"Save RSSE block reads {read.Length} bytes but the split gives it {block.Length}." );
		return read;
	}

	/// <summary>Reads the block starting at <paramref name="offset"/>; throws <see cref="InvalidDataException"/> when the bytes do not read as an RSSE block.</summary>
	public static SaveRsseBlock Parse( ReadOnlySpan<byte> payload, int offset )
	{
		if ( offset < 0 || offset > payload.Length )
			throw new InvalidDataException( "Save RSSE block starts outside the payload." );
		var reader = new Walker( payload, offset );
		if ( reader.U32( "magic" ) != Magic )
			throw new InvalidDataException( "Save RSSE block does not start with 'RSSE'." );
		var headerBytes = reader.U32( "header length" );
		if ( headerBytes != HeaderBytes )
			throw new InvalidDataException( $"Save RSSE header length is {headerBytes}; expected {HeaderBytes}." );
		var (initialised, ticks, nextId, headerCount, head) = (reader.U32( "header" ), reader.U32( "header" ), reader.U32( "header" ), reader.U32( "header" ), reader.U32( "header" ));
		for ( var word = 0; word < PadWords; word++ )
			reader.U32( "padding" );
		var count = reader.U32( "record count" );
		var size = reader.U32( "record size" );
		if ( count > 0 && size != RecordBytes )
			throw new InvalidDataException( $"Save RSSE record size is {size}; expected {RecordBytes}." );
		// Every record takes at least its struct, so the count is bounded by the bytes left.
		if ( count > 0 && (long)count * RecordBytes > payload.Length - reader.Position )
			throw new InvalidDataException( $"Save RSSE block claims {count} records but is truncated." );
		var records = new List<SaveRsseRecord>( (int)Math.Min( count, 1024 ) );
		for ( uint index = 0; index < count; index++ )
		{
			var recordOffset = reader.Position;
			var raw = reader.Take( RecordBytes, $"record {index} struct" );
			var buffers = new byte[BufferCount][];
			for ( var buffer = 0; buffer < BufferCount; buffer++ )
			{
				var length = reader.U32( $"record {index} buffer {buffer} length" );
				buffers[buffer] = reader.Take( length * (buffer == 6 ? (long)SaveRsseRecord.WalkEntryBytes : 1L), $"record {index} buffer {buffer}" );
				if ( buffer == 0 && buffers[0].Length % 4 != 0 )
					throw new InvalidDataException( $"Save RSSE record {index} code buffer holds {buffers[0].Length} bytes, not whole words." );
			}
			var folderLength = reader.U32( $"record {index} folder length" );
			if ( folderLength == 0 || folderLength > MaximumFolderBytes )
				throw new InvalidDataException( $"Save RSSE record {index} folder length {folderLength} is out of range." );
			var folder = reader.Take( folderLength, $"record {index} folder" );
			if ( folder[^1] != 0 || folder.AsSpan( 0, folder.Length - 1 ).IndexOf( (byte)0 ) >= 0 )
				throw new InvalidDataException( $"Save RSSE record {index} folder is not one NUL-terminated string." );
			if ( reader.U32( $"record {index} object list tag" ) != ObjectsMagic )
				throw new InvalidDataException( $"Save RSSE record {index} has no 'OBJ ' object list." );
			var objects = reader.U32( $"record {index} object count" );
			var objectBytes = reader.U32( $"record {index} object size" );
			if ( objects > 0 && objectBytes != SaveRsseObject.Bytes )
				throw new InvalidDataException( $"Save RSSE record {index} object size is {objectBytes}; expected {SaveRsseObject.Bytes}." );
			var entries = new List<SaveRsseObject>( (int)Math.Min( objects, 1024 ) );
			for ( uint entry = 0; entry < objects; entry++ )
			{
				var bytes = reader.Take( SaveRsseObject.Bytes, $"record {index} object {entry}" );
				uint Word( int at ) => BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( at ) );
				entries.Add( new SaveRsseObject( Word( 0 ), Word( 4 ), Word( 8 ), Word( 0x0C ), Word( 0x10 ), Word( 0x14 ), Word( 0x18 ) ) );
			}
			records.Add( new SaveRsseRecord
			{
				Raw = raw, Offset = recordOffset, Code = buffers[0], Stack = buffers[1], Variables = buffers[2], Strings = buffers[3], Limbo = buffers[4],
				Bounce = buffers[5], Walk = buffers[6], Nodes = buffers[7], Folder = Encoding.Latin1.GetString( folder, 0, folder.Length - 1 ), Objects = entries.AsReadOnly(),
			} );
		}
		return new SaveRsseBlock( initialised, ticks, nextId, headerCount, head, records.AsReadOnly(), reader.Position - offset );
	}

	private ref struct Walker
	{
		private readonly ReadOnlySpan<byte> payload;

		public Walker( ReadOnlySpan<byte> payload, int offset )
		{
			this.payload = payload;
			Position = offset;
		}

		public int Position { get; private set; }

		public uint U32( string what )
		{
			if ( payload.Length - Position < 4 )
				throw new InvalidDataException( $"Save RSSE block is truncated at offset {Position} (reading {what})." );
			var value = BinaryPrimitives.ReadUInt32LittleEndian( payload[Position..] );
			Position += 4;
			return value;
		}

		public byte[] Take( long count, string what )
		{
			if ( count < 0 || count > payload.Length - Position )
				throw new InvalidDataException( $"Save RSSE block is truncated at offset {Position} ({what} needs {count} more bytes, {payload.Length - Position} remain)." );
			var bytes = payload.Slice( Position, (int)count ).ToArray();
			Position += (int)count;
			return bytes;
		}
	}
}
