using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// One sprite-script instance of the Scripts block: the 0x118-byte record of the sprite-script virtual machine (tp.exe <c>0x4758F0</c> fills it, <c>0x475010</c> runs it).
/// A script is a program in the shared word table at <c>0x74DAB8</c> (the guests' balloon, burst and entertainer-performance steps, docs/GUESTS.md) with its own program
/// counter, 19 parameter words and a 20-word area between the capacity and the depth counter that is read here as the stack. The named fields are the offsets the VM and its setters read and write; every other byte stays in <see cref="Raw"/>
/// and is reachable by offset through <see cref="Word(int)"/>. Pointer-like words (the program base, the handle at +0xBC, the words from +0xF0) are values of the saving process.
/// </summary>
public sealed record SaveScriptRecord
{
	public const int Bytes = SaveBlockWidths.ScriptRecordBytes;
	/// <summary>The program table the loader writes into +0x14 of every record it reads (<c>0x475849</c>): the saved value is overwritten.</summary>
	public const uint ProgramTable = 0x74DAB8;
	public const int StackWords = 20;
	public const int ParameterWords = 19;

	/// <summary>The slot (1 to n - 1) in the script table this record belongs to.</summary>
	public required int Slot { get; init; }
	/// <summary>Offset of the record in the decoded payload.</summary>
	public required int Offset { get; init; }
	/// <summary>The 0x118 bytes as saved.</summary>
	public required byte[] Raw { get; init; }

	/// <summary>The word at <paramref name="offset"/> (a multiple of 4 below 0x118).</summary>
	public uint Word( int offset )
	{
		if ( offset < 0 || offset > Bytes - 4 || offset % 4 != 0 )
			throw new ArgumentOutOfRangeException( nameof( offset ) );
		return BinaryPrimitives.ReadUInt32LittleEndian( Raw.AsSpan( offset ) );
	}

	/// <summary>+0x00: written by no traced code (0 in every corpus record; the object's first word).</summary>
	public uint Word0 => Word( 0 );
	/// <summary>+0x04: the record's own slot number (<c>0x475B6F</c> stores it when the script is registered).</summary>
	public uint StoredSlot => Word( 4 );
	/// <summary>+0x08: the program counter, in words from <see cref="ProgramTable"/>.</summary>
	public uint ProgramCounter => Word( 8 );
	/// <summary>+0x0C: the entry point the script started at (the initializer <c>0x4758F0</c> sets it equal to the counter).</summary>
	public uint EntryPoint => Word( 0x0C );
	/// <summary>+0x10: the counter of the last instruction the VM ran (<c>0x475057</c>).</summary>
	public uint LastCounter => Word( 0x10 );
	/// <summary>+0x14: the program table base; saved as <see cref="ProgramTable"/> and rewritten by the loader.</summary>
	public uint Program => Word( 0x14 );
	/// <summary>+0x18: the run state: the initializer writes 1, the VM 1 while it steps and 4 when the program ends, 3 skips the step; 2 is what every corpus record holds.</summary>
	public uint State => Word( 0x18 );
	/// <summary>+0x1C: 0x14, the stack capacity in words (<c>0x475950</c>).</summary>
	public uint StackCapacity => Word( 0x1C );
	/// <summary>+0x20 to +0x6F: the 20 words between the capacity word and the depth counter, read as the stack by position (all zero in the corpus).</summary>
	public uint[] Stack
	{
		get
		{
			var words = new uint[StackWords];
			for ( var index = 0; index < words.Length; index++ )
				words[index] = Word( 0x20 + index * 4 );
			return words;
		}
	}
	/// <summary>+0x70: the nesting counter of the block-skipping scans <c>0x4750C0</c> and <c>0x475130</c> (<c>0x4750F2</c>); 0 between runs.</summary>
	public uint Depth => Word( 0x70 );
	/// <summary>+0x7C: the game tick at which the script runs next (the updater <c>0x475360</c> adds <see cref="Period"/> after each run).</summary>
	public uint WakeTick => Word( 0x7C );
	/// <summary>+0x80: the ticks between two runs.</summary>
	public uint Period => Word( 0x80 );
	/// <summary>+0x84 to +0xCF: the 19 parameter words the script's instructions read (<c>0x475590</c> sets one, <c>0x4755D0</c> sets one as a float).</summary>
	public uint[] Parameters
	{
		get
		{
			var words = new uint[ParameterWords];
			for ( var index = 0; index < words.Length; index++ )
				words[index] = Word( 0x84 + index * 4 );
			return words;
		}
	}
	/// <summary>+0x88, +0x8C and +0x90: the script's position (<c>0x475480</c> stores three floats there and a fourth at +0xC0).</summary>
	public (float X, float Y, float Z) Position => (Float( 0x88 ), Float( 0x8C ), Float( 0x90 ));
	/// <summary>+0xC0: the fourth word of the position setter (0x1200 in the corpus).</summary>
	public uint PositionFlags => Word( 0xC0 );
	/// <summary>+0xBC: the sprite handle the creator <c>0x540C60</c> returned for the script (a value of the saving process).</summary>
	public uint SpriteHandle => Word( 0xBC );

	private float Float( int offset ) => BitConverter.UInt32BitsToSingle( Word( offset ) );
}

/// <summary>
/// The Scripts block (writer <c>0x475650</c>, loader <c>0x475730</c>): <c>u32 'SCPT'</c> (the file holds <c>TPCS</c>), <c>u32 0x118</c> (the record size), <c>u32 n</c> (the script table length,
/// 100 in every corpus save), <c>n × u32</c> slot words (pointers of the saving process; only zero or not zero matters, and the loader zeroes slot 0), then the 0x118-byte record of every
/// slot 1 to n - 1 whose word is not zero, in slot order. Another magic or record size makes the loader report "Restore scripts FAILED" and the whole load fail
/// (<see cref="SaveBlockWidths"/> rejects it); a block with no sprite scripts is 12 + 4n bytes.
/// </summary>
public sealed class SaveScriptsBlock
{
	public const int MaximumSlots = 1 << 16;

	private readonly uint[] slotWords;
	private readonly SaveScriptRecord[] records;

	private SaveScriptsBlock( int offset, int length, uint[] slotWords, SaveScriptRecord[] records )
	{
		Offset = offset;
		Length = length;
		this.slotWords = slotWords;
		this.records = records;
	}

	/// <summary>Offset of the block in the decoded payload.</summary>
	public int Offset { get; }
	/// <summary>Bytes the loader consumes.</summary>
	public int Length { get; }
	/// <summary>The script table length n.</summary>
	public int SlotCount => slotWords.Length;
	/// <summary>The saved slot words (process pointers).</summary>
	public IReadOnlyList<uint> SlotWords => slotWords;
	/// <summary>The records, in slot order.</summary>
	public IReadOnlyList<SaveScriptRecord> Records => records;

	/// <summary>Reads the Scripts block of <paramref name="layout"/>; it must end exactly where its loader stops.</summary>
	public static SaveScriptsBlock Parse( ReadOnlySpan<byte> payload, SavePayloadLayout layout )
	{
		ArgumentNullException.ThrowIfNull( layout );
		if ( layout.PayloadLength != payload.Length )
			throw new ArgumentException( "Layout does not describe this payload.", nameof( layout ) );
		var block = layout[SaveBlock.Scripts];
		var parsed = Parse( payload, block.Offset );
		if ( parsed.Length != block.Length )
			throw new InvalidDataException( $"The Scripts block reads {parsed.Length} bytes but the layout holds {block.Length}." );
		return parsed;
	}

	/// <summary>Reads a Scripts block that starts at <paramref name="offset"/> (the loader stops where this returns; the delimiter is not checked here).</summary>
	public static SaveScriptsBlock Parse( ReadOnlySpan<byte> payload, int offset )
	{
		if ( offset < 0 || offset > payload.Length )
			throw new InvalidDataException( "The Scripts block starts outside the payload." );
		var cursor = new SaveBlockCursor( payload, offset, "Scripts" );
		var magic = cursor.U32();
		var size = cursor.U32();
		// [BIN:TPW-EXE:0x4757D0 Scripts loader] 'SCPT' and 0x118, else "Restore scripts FAILED" (branch 0x4758B4)
		if ( magic != SaveBlockWidths.ScriptsMagic || size != SaveBlockWidths.ScriptRecordBytes )
			throw new InvalidDataException( $"The Scripts block does not start with 'SCPT' and a record size of {SaveBlockWidths.ScriptRecordBytes} (magic 0x{magic:X8}, size {size}); the original fails with \"Restore scripts FAILED\"." );
		var count = cursor.U32();
		if ( count > MaximumSlots || (long)count * 4 > payload.Length - cursor.Position )
			throw new InvalidDataException( $"The Scripts block claims {count} slots, which do not fit the payload." );
		var words = new uint[count];
		for ( var slot = 0; slot < words.Length; slot++ )
			words[slot] = cursor.U32();
		var found = new List<SaveScriptRecord>();
		// slot 0 is zeroed by the loader (0x475842) and is never read
		for ( var slot = 1; slot < words.Length; slot++ )
		{
			if ( words[slot] == 0 )
				continue;
			var at = cursor.Take( SaveScriptRecord.Bytes );
			found.Add( new SaveScriptRecord { Slot = slot, Offset = at, Raw = payload.Slice( at, SaveScriptRecord.Bytes ).ToArray() } );
		}
		return new SaveScriptsBlock( offset, cursor.Position - offset, words, found.ToArray() );
	}
}

/// <summary>A bounds-checked reader over a block of a decoded payload; every refusal is an <see cref="InvalidDataException"/> naming the block.</summary>
internal ref struct SaveBlockCursor
{
	private readonly ReadOnlySpan<byte> payload;
	private readonly string block;

	public SaveBlockCursor( ReadOnlySpan<byte> payload, int offset, string block )
	{
		this.payload = payload;
		this.block = block;
		Position = offset;
	}

	public int Position { get; private set; }

	/// <summary>Consumes <paramref name="count"/> bytes and returns where they start.</summary>
	public int Take( long count )
	{
		if ( count < 0 || count > payload.Length - Position )
			throw new InvalidDataException( $"The {block} block is truncated at offset {Position} (needs {count} more bytes, {payload.Length - Position} remain)." );
		var at = Position;
		Position += (int)count;
		return at;
	}

	public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian( payload[Take( 4 )..] );
}
