using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>One embedded node of a RideSystem record: two u32 words (writer 0x464140 stores the two live values
/// of the instance's embedded-node slot table, one pair per node, after the header's +0x2e/+0x32 block).</summary>
public readonly record struct SaveRideSystemEmbeddedNode( uint First, uint Second );

/// <summary>One stored model node's state: the packed 32-bit save word and the flag constants the loader
/// unpacks it into (each packed bit maps onto the flag list of <see cref="SaveRideSystemBlock.NodeStateFlags"/>).</summary>
public readonly record struct SaveRideSystemNodeState( uint Packed, uint Flags );

/// <summary>
/// One animation time control of a RideSystem record: 11 u32 words of the loader's 14-word control, stored in
/// the save's word order [0], [1], [2], [4], [5], [6], [3], [9], [10], [11], [12] (tp.exe 0x464bb6 moves the
/// seventh record word to control word 3). Word 0 is the flags word (the loader 0x464be2 normalizes its bit 4
/// into flag 0x10); word 1 is the animation set (12 = none; the loader 0x464cbe keeps the 0x400000
/// animated-instance flag off when it is 12 or more, and logs when it is anything else); the remaining words
/// are the animation, the times and a speed float (Easymode's words 9 and 11 hold 1.0 as a float, for instance).
/// The loader bounds no word, so they are kept raw.
/// </summary>
public sealed record SaveRideSystemControl( uint Flags, uint AnimationSet, uint[] Words );

/// <summary>
/// One scene-mesh instance record of the save's RideSystem block (writer 0x464140, loader 0x4647a0;
/// docs/TPWS-PAYLOAD.md "RideSystem placed-object records"): a presence byte, the fixed 54-byte header, then
/// <see cref="EmbeddedCount"/> × 8 bytes of embedded nodes, one packed node-state word per <see cref="NodeCount"/>
/// stored model node, and one 44-byte animation time control per control of the live instance. <see cref="InfoId"/>
/// matches the <c>Info.Id</c> of the object's <c>.sam</c> in the level archives. The header's +0x20 word and
/// +0x22 dword come from the live instance's +0xc/+0xb8 and are kept raw.
/// </summary>
public sealed class SaveRideSystemRecord
{
	public SaveRideSystemRecord( int slot, int offset, int infoId, int x, int y, int width, int height, int kind, int index, int rotation,
		uint instanceFlags, ushort instanceWord, uint instanceDword, int nodeCount, int embeddedCount,
		uint embeddedWord0, uint embeddedWord1, SaveRideSystemEmbeddedNode[] embeddedNodes, SaveRideSystemNodeState[] nodeStates,
		SaveRideSystemControl[] controls )
	{
		Slot = slot;
		Offset = offset;
		InfoId = infoId;
		X = x;
		Y = y;
		Width = width;
		Height = height;
		Kind = kind;
		Index = index;
		Rotation = rotation;
		InstanceFlags = instanceFlags;
		InstanceWord = instanceWord;
		InstanceDword = instanceDword;
		NodeCount = nodeCount;
		EmbeddedCount = embeddedCount;
		EmbeddedWord0 = embeddedWord0;
		EmbeddedWord1 = embeddedWord1;
		EmbeddedNodes = embeddedNodes;
		NodeStates = nodeStates;
		Controls = controls;
	}

	/// <summary>The slot of the block's used + free slots that holds this record.</summary>
	public int Slot { get; }
	/// <summary>Absolute payload offset of the record's presence byte.</summary>
	public int Offset { get; }
	public int InfoId { get; }
	public int X { get; }
	public int Y { get; }
	public int Width { get; }
	public int Height { get; }
	public int Kind { get; }
	public int Index { get; }
	/// <summary>Rotation in degrees, stored as a u32 at header +0x26 (tp.exe 0x46426b writes it into a dword).</summary>
	public int Rotation { get; }
	/// <summary>The packed instance flags of header +0x1c: the writer 0x4641c4 maps the flags 0x20, 0x40, 0x80,
	/// 0x100, 0x200, 0x400, 0x800, 0x40000 and 0x8000000 to bits 0–8 and never stores higher bits.</summary>
	public uint InstanceFlags { get; }
	/// <summary>Header +0x20 (u16): the live instance's +0xc word, kept raw.</summary>
	public ushort InstanceWord { get; }
	/// <summary>Header +0x22 (u32): the live instance's +0xb8 word, kept raw.</summary>
	public uint InstanceDword { get; }
	/// <summary>Header +0x2a (u16): the count of stored node-state words, written from the live model's word at
	/// +0x42 (the loader 0x464ab4 walks that many words, comparing them to the record's own word).</summary>
	public int NodeCount { get; }
	/// <summary>Header +0x2c (u16): the count of embedded nodes, written from the live model's word at +0x48
	/// (the loader compares it before decoding any embedded node).</summary>
	public int EmbeddedCount { get; }
	/// <summary>Header +0x2e: the first word of the embedded block.</summary>
	public uint EmbeddedWord0 { get; }
	/// <summary>Header +0x32: the second word of the embedded block.</summary>
	public uint EmbeddedWord1 { get; }
	public SaveRideSystemEmbeddedNode[] EmbeddedNodes { get; }
	/// <summary>The <see cref="NodeCount"/> node-state words, unpacked (see <see cref="SaveRideSystemNodeState"/>).</summary>
	public SaveRideSystemNodeState[] NodeStates { get; }
	/// <summary>The saved animation time controls; one per control of the live instance (see
	/// <see cref="SaveRideSystemBlock"/> for why this count is not stored).</summary>
	public SaveRideSystemControl[] Controls { get; }

	public bool IsFixedItem => Kind == SaveObject.FixedItemKind;
	/// <summary>Kind 815 marks every buildable ride, shop, sideshow and feature of a themed park.</summary>
	public bool IsPlacedObject => Kind == SaveObject.PlacedObjectKind;

	public SaveObject ToSaveObject() => new( Offset, InfoId, X, Y, Width, Height, Kind, Index, Rotation );
}

/// <summary>
/// Framed walk of the save's RideSystem block (tp.exe writer 0x464140, loader 0x4647a0; docs/TPWS-PAYLOAD.md
/// "RideSystem placed-object records"). The block is a u32 length, then the u32 used count, free-hole count and
/// first free slot, then one slot per one of the used + free slots in slot order: a presence byte, and for a
/// present slot its <see cref="SaveRideSystemRecord"/>. The walk must end exactly at the stored length with the
/// stored counts, the first absent slot must carry the stored first free slot's ordinal (the free hole the
/// instance allocator keeps at +0x7aedf4; with no free hole the count equals the used count), and every
/// presence byte must be exactly 0 or 1.
///
/// What a record never stores is its animation control count T: the original loader takes it from the live
/// instance's control count at +0xe, which 0x4557c0 copies from the object type's template instance (the
/// level's kind table entry carries the template at +0xd0). OpenTPW builds no live instances on import, so the
/// walk derives each T as the smallest value that still walks the rest of the block: with a shorter record the
/// next slot's presence byte stops being 0 or 1 (it falls inside a control word), the slot count stops matching
/// the header, or the walk falls short of the stored length. <see cref="UniquelyWalked"/> reports whether each
/// derived T is also strictly minimal (raising it by one breaks the walk); Easymode.TPWI and every corpus save
/// walk uniquely.
/// </summary>
public sealed record SaveRideSystemBlock(
	int Offset,
	uint Length,
	uint Used,
	uint Free,
	uint FirstFreeSlot,
	SaveRideSystemRecord[] Records )
{
	/// <summary>Bytes of the record's fixed header (tp.exe 0x4647ab steps a present record past 0x26 + 0x10 bytes).</summary>
	public const int RecordHeaderBytes = 0x36;
	/// <summary>Bytes of one saved animation time control (11 u32 words).</summary>
	public const int ControlBytes = 0x2C;
	public const int MaximumNodeCount = 0x400;
	public const int MaximumEmbeddedNodes = 0x400;
	/// <summary>The packed instance-flag word of header +0x1c never holds bits above bit 8 (writer 0x4641c4–0x4642df).</summary>
	public const uint ValidInstanceFlags = 0x1FF;
	/// <summary>The stored node-state words only ever use the writer's mapped bits (0–3 and 5–11): the loader
	/// 0x464af6 tests exactly those bits and the writer 0x464380 sets exactly them, so bit 4 and bits 12+ are
	/// empty in a real save and the reader refuses them.</summary>
	internal static readonly uint[] NodeStateFlags =
	{
		0x10, 0x20, 0x8000, 0x10000, 0, 0x40000, 0x80000, 0x200000, 0x400000, 0x20000000, 0x80000000, 0x100000
	};
	internal const uint ValidNodeStateBits =
		(1u << 0) | (1u << 1) | (1u << 2) | (1u << 3) | (1u << 5) | (1u << 6) | (1u << 7) | (1u << 8) | (1u << 9) | (1u << 10) | (1u << 11);

	/// <summary>True when every record's derived control count is also strictly minimal: raising it by one breaks
	/// the walk, so no other assignment of control counts walks the block (checked per record; a run that fails
	/// the check still parses and decodes, and its per-record values stay the smallest feasible ones).</summary>
	public bool UniquelyWalked { get; internal init; }

	public int SlotCount => (int)(Used + Free);

	/// <summary>The parsed block. Throws <see cref="ArgumentException"/> when <paramref name="layout"/> does not describe
	/// <paramref name="payload"/>, and <see cref="InvalidDataException"/> unless the block walks exactly to its
	/// stored length with the stored counts, the stored first free slot, bounded counts and headers, and
	/// presence bytes of exactly 0 or 1. (The split already equates the length word's read width with the block's
	/// bytes (<see cref="SaveBlockWidths"/>); the check below guards a standalone call with such a layout.)</summary>
	public static SaveRideSystemBlock Parse( ReadOnlySpan<byte> payload, SavePayloadLayout layout )
	{
		ArgumentNullException.ThrowIfNull( layout );
		if ( layout.PayloadLength != payload.Length )
			throw new ArgumentException( "Layout does not describe this payload.", nameof( payload ) );
		var section = layout[SaveBlock.RideSystem];
		// The block is u32 length + that many bytes (tp.exe 0x4647a0 reads the word, then that many bytes).
		if ( section.Length < 4 )
			throw new InvalidDataException( "Save RideSystem block has no length word." );
		var length = BinaryPrimitives.ReadUInt32LittleEndian( payload[section.Offset..] );
		if ( length != (uint)(section.Length - 4) )
			throw new InvalidDataException( $"Save RideSystem block's length word says {length} bytes; the block holds {section.Length - 4}." );
		if ( section.Length < 16 )
			throw new InvalidDataException( "Save RideSystem block has no used, free and first-free counts." );
		var data = payload[section.Offset..section.End];
		var used = (int)Count( data, 4 );
		var free = (int)Count( data, 8 );
		var firstFreeSlot = Count( data, 12 );
		if ( used < 0 || free < 0 )
			throw new InvalidDataException( $"Save RideSystem block lists negative counts: {used} used, {free} free." );
		if ( used + free > data.Length - 16 )
			throw new InvalidDataException( $"Save RideSystem block lists {used + free} slots, more than its {data.Length - 16} bytes hold." );
		if ( used + free == 0 )
			return new SaveRideSystemBlock( section.Offset, length, (uint)used, (uint)free, firstFreeSlot, Array.Empty<SaveRideSystemRecord>() ) { UniquelyWalked = true };
		if ( free == 0 ? firstFreeSlot != (uint)used : firstFreeSlot >= (uint)(used + free) )
			throw new InvalidDataException( $"Save RideSystem block's first free slot {firstFreeSlot} does not name a free slot of its {used + free}." );
		var controlCounts = new int[used + free];
		var entryStates = new (int Position, int Used, int Free, bool FreeSeen)[used + free];
		var failed = new HashSet<(int Slot, int Position, int Used, int Free, bool FreeSeen)>();

		bool Feasible( ReadOnlySpan<byte> data, int slot, int position, int usedSoFar, int freeSoFar, bool freeSeen, bool commit )
		{
			if ( usedSoFar > used || freeSoFar > free || slot > used + free )
				return false;
			if ( position == data.Length )
				return slot == used + free && usedSoFar == used && freeSoFar == free;
			if ( position > data.Length || slot >= used + free )
				return false;
			var state = (Slot: slot, Position: position, Used: usedSoFar, Free: freeSoFar, FreeSeen: freeSeen);
			if ( !commit && !failed.Add( state ) )
				return false;
			switch ( data[position] )
			{
				case 0:
					// The stored first free slot is the free list's head: the first absent slot carries its ordinal.
					if ( !freeSeen && slot != (int)firstFreeSlot )
						return false;
					return Feasible( data, slot + 1, position + 1, usedSoFar, freeSoFar + 1, true, commit );
				case 1:
					if ( position + 1 + RecordHeaderBytes > data.Length )
						return false;
					var nodeCount = BinaryPrimitives.ReadUInt16LittleEndian( data[(position + 1 + RecordHeaderBytes - 12)..] );
					var embeddedCount = BinaryPrimitives.ReadUInt16LittleEndian( data[(position + 1 + RecordHeaderBytes - 10)..] );
					if ( nodeCount > MaximumNodeCount || embeddedCount > MaximumEmbeddedNodes )
						return false;
					var recordBytes = RecordHeaderBytes + nodeCount * 4 + embeddedCount * 8;
					if ( position + 1 + recordBytes > data.Length )
						return false;
					var maximumControls = (data.Length - 1 - position - recordBytes) / ControlBytes;
					for ( var controls = 0; controls <= maximumControls; controls++ )
					{
						if ( Feasible( data, slot + 1, position + 1 + recordBytes + controls * ControlBytes, usedSoFar + 1, freeSoFar, freeSeen, commit ) )
						{
							if ( commit )
							{
								controlCounts[slot] = controls;
								entryStates[slot] = (position, usedSoFar, freeSoFar, freeSeen);
							}
							return true;
						}
					}
					return false;
				default:
					return false;
			}
		}

		if ( !Feasible( data, 0, 16, 0, 0, free == 0, commit: true ) )
			throw new InvalidDataException( $"Save RideSystem block does not walk from its {used + free} slots ({used} used, {free} free, first free {firstFreeSlot}) to its {length}-byte shape." );

		// The committing walk stores the smallest feasible control count per slot; each T was found by probing
		// the values upward, and every larger value was closed (per record, against the whole rest of the block).
		var unique = true;
		var walkRecords = new SaveRideSystemRecord[used];
		var recordIndex = 0;
		var position = 16;
		var freeSeen = false;
		for ( var slot = 0; slot < used + free; slot++ )
		{
			switch ( data[position] )
			{
				case 0:
					if ( !freeSeen && firstFreeSlot != (uint)slot )
						throw new InvalidDataException( $"Save RideSystem block's first absent slot is {slot}, not its stored first free slot {firstFreeSlot}." );
					freeSeen = true;
					position++;
					continue;
				case 1:
					var record = ReadRecord( data, slot, position, section.Offset, controlCounts[slot] );
					walkRecords[recordIndex++] = record;
					position += 1 + RecordHeaderBytes + record.NodeCount * 4 + record.EmbeddedCount * 8 + record.Controls.Length * ControlBytes;
					break;
				default:
					throw new InvalidDataException( $"Save RideSystem slot {slot} has presence byte 0x{data[position]:X2}; slots hold exactly 0 or 1." );
			}
			if ( position > data.Length )
				throw new InvalidDataException( $"Save RideSystem slot {slot}'s record runs past the block's {length}-byte shape." );
		}
		if ( position != data.Length )
			throw new InvalidDataException( $"The RideSystem walk stops {data.Length - position} bytes short of the block's {length}-byte shape." );

		// Strictly minimal control counts: raising any record's T by one must break the walk (same prefix, one
		// record 44 bytes longer, the rest of the block re-walked against the same stored counts and length). A
		// shared memo is sound here: a walk state fully determines the rest of the block. Keeping the memo per
		// record keeps the probes independent and cheap.
		foreach ( var record in walkRecords )
		{
			failed.Clear();
			var entry = entryStates[record.Slot];
			var recordBytes = RecordHeaderBytes + record.NodeCount * 4 + record.EmbeddedCount * 8;
			if ( Feasible( data, record.Slot + 1, entry.Position + 1 + recordBytes + (record.Controls.Length + 1) * ControlBytes,
				entry.Used + 1, entry.Free, entry.FreeSeen, commit: false ) )
			{
				unique = false;
				break;
			}
		}
		return new SaveRideSystemBlock( section.Offset, length, (uint)used, (uint)free, firstFreeSlot, walkRecords ) { UniquelyWalked = unique };
	}

	private static SaveRideSystemRecord ReadRecord( ReadOnlySpan<byte> data, int slot, int position, int blockOffset, int controlCount )
	{
		var header = position + 1;
		static int U32( ReadOnlySpan<byte> data, int offset ) => (int)BinaryPrimitives.ReadUInt32LittleEndian( data[offset..] );
		var flags = BinaryPrimitives.ReadUInt32LittleEndian( data[(header + 0x1C)..] );
		if ( flags > ValidInstanceFlags )
			throw new InvalidDataException( $"Save RideSystem slot {slot}'s instance-flag word 0x{flags:X8} holds bits the writer never stores (bits 0-8)." );
		var nodeCount = BinaryPrimitives.ReadUInt16LittleEndian( data[(header + 0x2A)..] );
		var embeddedCount = BinaryPrimitives.ReadUInt16LittleEndian( data[(header + 0x2C)..] );
		if ( nodeCount > MaximumNodeCount || embeddedCount > MaximumEmbeddedNodes )
			throw new InvalidDataException( $"Save RideSystem slot {slot} lists {nodeCount} nodes and {embeddedCount} embedded nodes, above the {MaximumNodeCount}/{MaximumEmbeddedNodes} bounds." );
		var embeddedNodes = new SaveRideSystemEmbeddedNode[embeddedCount];
		var cursor = header + 0x36;
		for ( var node = 0; node < embeddedCount; node++ )
		{
			embeddedNodes[node] = new SaveRideSystemEmbeddedNode(
				BinaryPrimitives.ReadUInt32LittleEndian( data[cursor..] ),
				BinaryPrimitives.ReadUInt32LittleEndian( data[(cursor + 4)..] ) );
			cursor += 8;
		}
		var nodeStates = new SaveRideSystemNodeState[nodeCount];
		for ( var node = 0; node < nodeCount; node++ )
		{
			var packed = BinaryPrimitives.ReadUInt32LittleEndian( data[cursor..] );
			if ( packed > ValidNodeStateBits )
				throw new InvalidDataException( $"Save RideSystem slot {slot}'s node state word 0x{packed:X8} uses bits the writer never stores (bits 0-3 and 5-11)." );
			var unpacked = 0u;
			for ( var bit = 0; bit < NodeStateFlags.Length; bit++ )
				if ( (packed >> bit & 1) != 0 )
					unpacked |= NodeStateFlags[bit];
			nodeStates[node] = new SaveRideSystemNodeState( packed, unpacked );
			cursor += 4;
		}
		var controls = new SaveRideSystemControl[controlCount];
		for ( var control = 0; control < controlCount; control++ )
		{
			var words = new uint[11];
			for ( var word = 0; word < words.Length; word++ )
				words[word] = BinaryPrimitives.ReadUInt32LittleEndian( data[(cursor + word * 4)..] );
			controls[control] = new SaveRideSystemControl( words[0], words[1], words );
			cursor += ControlBytes;
		}
		if ( cursor != position + 1 + RecordHeaderBytes + nodeCount * 4 + embeddedCount * 8 + controlCount * ControlBytes )
			throw new InvalidDataException( $"Save RideSystem slot {slot}'s record does not span its header, embedded nodes, node states and controls." );
		return new SaveRideSystemRecord( slot, blockOffset + position, U32( data, header ), U32( data, header + 4 ), U32( data, header + 8 ),
			U32( data, header + 0xC ), U32( data, header + 0x10 ), U32( data, header + 0x14 ), U32( data, header + 0x18 ), U32( data, header + 0x26 ),
			flags, BinaryPrimitives.ReadUInt16LittleEndian( data[(header + 0x20)..] ), BinaryPrimitives.ReadUInt32LittleEndian( data[(header + 0x22)..] ),
			nodeCount, embeddedCount,
			BinaryPrimitives.ReadUInt32LittleEndian( data[(header + 0x2E)..] ), BinaryPrimitives.ReadUInt32LittleEndian( data[(header + 0x32)..] ),
			embeddedNodes, nodeStates, controls );
	}

	private static uint Count( ReadOnlySpan<byte> data, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( data[offset..] );
}
