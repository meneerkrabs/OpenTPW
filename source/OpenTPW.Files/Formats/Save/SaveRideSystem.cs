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
/// the save's word order [0], [1], [2], [4], [5], [6], [3], [9], [10], [11], [12] (tp.exe 0x464bef-0x464bf4
/// copies the seventh record word to control word 3). The words are, in save order, Flags, AnimID, SubAnim,
/// StartAnimTime, AnimTime, NoPauseAnimTime, the speed (a float), DeferredAnimID, DeferredSubAnim, DeferredFlags
/// and the deferred speed (a float); the names are the ones the loader's debug dump 0x464580 prints for control words
/// 0, 1, 2, 4, 5, 6, 9, 10 and 11, and the speed is control word 3, which the frame formula multiplies (0x473490).
/// The flags word holds 0x1 loop, 0x2 held on the first frame (AnimID 13), 0x4 ended (AnimID 14; the loader then sets
/// 0x10, sample once, at 0x464bfb), 0x8 an AnimID-0 clip ended, 0x40 the unscaled clock (0x472f60, 0x4735d0). AnimID 12 is
/// "none": 0x464bb6 logs when it is anything else, and the loader 0x464cbe sets the 0x400000 animated-instance flag when it
/// is below 12. The times are milliseconds of the game clock the save's Clock block stores. The loader bounds no word, so
/// they are kept raw; OriginalObjectRuntime.RestoreSavedScene puts them back on the object's channels.
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
	/// <summary>Rotation in degrees, stored as a u32 at header +0x26 (the writer copies a dword of the live model's +0x10
/// to it at tp.exe 0x4642f9-0x4642ff; that it is a rotation in degrees is read from the saves, not from the code).</summary>
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
/// presence byte must be exactly 0 or 1. (The original loader is more lenient than this reader: it takes any
/// presence byte other than 1 as an empty slot and never compares its read position with the length. The
/// reader accepts only what the writer produces.)
///
/// What a record never stores is its animation control count T: the original loader takes it from the live
/// instance's control count at +0xE (0x464b98), which 0x4557c0 copies with the instance's 0xfc-byte header from
/// the kind table's type entry (0x463060 looks the entry up by the type index of the record's Info.Id). That
/// entry is built by 0x4629d0, which passes its seventh argument to the model builder 0x461f10 as the count
/// (0x461f10 stores it at entry +0xe) and replaces zero with 1 (0x462ad5-0x462ad9); the callers 0x414175 and
/// 0x4141c7 pass the Info's field at +0x170, which is the object .sam's <c>UsageInfo.NumSimultAnims</c> (read
/// from the saves: Jungle Spray declares 3 and has T=3, the Belly Bounce declares nothing and has T=1). So T is
/// at least 1 and, for an object whose .sam is known, exactly <c>max(1, UsageInfo.NumSimultAnims)</c>.
///
/// The save itself carries neither: this reader therefore takes the control-count table as an optional input
/// (<c>controlCount</c> of <see cref="Parse"/>: the .sam value by Info.Id). A record whose Info.Id the table
/// resolves has exactly that T. Every other record may have any T of at least 1, and the walk then considers
/// every complete parse (an iterative, memoized search over slot, read position and used count, with explicit
/// work limits): it succeeds only when the block has exactly one complete parse and throws when it has none or
/// more than one, so a block that two assignments of control counts both walk is refused instead of being
/// resolved by a guess. Easymode.TPWI and every corpus save have exactly one complete parse.
/// </summary>
public sealed record SaveRideSystemBlock(
	int Offset,
	uint Length,
	uint Used,
	uint Free,
	uint FirstFreeSlot,
	SaveRideSystemRecord[] Records )
{
	/// <summary>Bytes of the record's fixed header (tp.exe 0x464872 and 0x464879 step a present record past 0x26 + 0x10 bytes).</summary>
	public const int RecordHeaderBytes = 0x36;
	/// <summary>Bytes of one saved animation time control (11 u32 words).</summary>
	public const int ControlBytes = 0x2C;
	/// <summary>The most slots a block may list. The records' index words and the instance table are 16-bit sized
	/// (an upper bound for resource use, not a limit read from tp.exe).</summary>
	public const int MaximumSlots = 0xFFFF;
	/// <summary>Search budget: the most candidate transitions the control-count search examines before it refuses the block.</summary>
	public const int MaximumSearchWork = 20_000_000;
	/// <summary>Search budget: the most distinct (slot, position, used) states the search keeps before it refuses the block.</summary>
	public const int MaximumSearchStates = 1_000_000;
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

	/// <summary>How many records took their control count from the caller's table (the rest were resolved by the
	/// single complete parse of the block).</summary>
	public int TableControlCounts { get; internal init; }

	public int SlotCount => (int)(Used + Free);

	/// <summary>The parsed block. Throws <see cref="ArgumentException"/> when <paramref name="layout"/> does not describe
	/// <paramref name="payload"/>, and <see cref="InvalidDataException"/> unless the block has exactly one complete
	/// parse: it walks exactly to its stored length with the stored counts, the stored first free slot, bounded
	/// counts, headers, flag words and node states, and presence bytes of exactly 0 or 1 (see the type summary).
	/// An empty block (no used and no free slot) is exactly the 16 bytes of its three counters with a first free
	/// slot of 0, which is what the writer emits.</summary>
	/// <param name="controlCount">The control count of the object with the given Info.Id, <c>max(1, UsageInfo.NumSimultAnims)</c>
	/// of its .sam, or null when unknown. Null for the whole parameter derives every count from the block alone.</param>
	public static SaveRideSystemBlock Parse( ReadOnlySpan<byte> payload, SavePayloadLayout layout, Func<int, int?>? controlCount = null )
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
		var used = Count( data, 4 );
		var free = Count( data, 8 );
		var firstFreeSlot = Count( data, 12 );
		var total = (ulong)used + free;
		if ( total > MaximumSlots )
			throw new InvalidDataException( $"Save RideSystem block lists {total} slots ({used} used, {free} free), above the {MaximumSlots} bound." );
		if ( total > (ulong)(data.Length - 16) )
			throw new InvalidDataException( $"Save RideSystem block lists {total} slots, more than its {data.Length - 16} bytes hold." );
		if ( total == 0 )
		{
			if ( data.Length != 16 || firstFreeSlot != 0 )
				throw new InvalidDataException( $"Save RideSystem block lists no slots but holds {data.Length - 16} further bytes and a first free slot of {firstFreeSlot}; an empty block is its three zero counters." );
			return new SaveRideSystemBlock( section.Offset, length, used, free, firstFreeSlot, Array.Empty<SaveRideSystemRecord>() );
		}
		if ( free == 0 ? firstFreeSlot != used : firstFreeSlot >= total )
			throw new InvalidDataException( $"Save RideSystem block's first free slot {firstFreeSlot} does not name a free slot of its {total}." );

		var slots = (int)total;
		var (controlCounts, tableCounts) = FindUniqueParse( data, slots, (int)used, (int)free, (int)firstFreeSlot, controlCount, length );

		var records = new SaveRideSystemRecord[used];
		var recordIndex = 0;
		var position = 16;
		for ( var slot = 0; slot < slots; slot++ )
		{
			if ( data[position] == 0 )
			{
				position++;
				continue;
			}
			var record = ReadRecord( data, slot, position, section.Offset, controlCounts[slot] );
			records[recordIndex++] = record;
			position += 1 + RecordHeaderBytes + record.NodeCount * 4 + record.EmbeddedCount * 8 + record.Controls.Length * ControlBytes;
		}
		if ( position != data.Length || recordIndex != records.Length )
			throw new InvalidDataException( $"The RideSystem walk stops {data.Length - position} bytes short of the block's {length}-byte shape." );
		return new SaveRideSystemBlock( section.Offset, length, used, free, firstFreeSlot, records ) { TableControlCounts = tableCounts };
	}

	/// <summary>One state of the search: how many complete-prefix paths reach it (saturated at 2) and its first
	/// predecessor edge.</summary>
	private struct Node
	{
		public int Count;
		public int ParentPosition;
		public int ParentUsed;
		public int Controls;
	}

	/// <summary>
	/// Enumerates every complete parse of the block iteratively: a forward pass over the slots keeps, per slot,
	/// the reachable (read position, used count) states with their saturated path counts and one predecessor edge
	/// (a state fully determines the rest of the walk, so the pass is memoized by construction). The terminal state
	/// (the block's end after all used slots) must have exactly one path; its control counts are then rebuilt from
	/// the predecessor edges. Every record transition uses <see cref="TryMeasure"/>, the same predicate the
	/// decoder applies, and a record resolved by <paramref name="table"/> has exactly its table count.
	/// </summary>
	private static (int[] ControlCounts, int TableCounts) FindUniqueParse( ReadOnlySpan<byte> data, int slots, int used, int free, int firstFreeSlot,
		Func<int, int?>? table, uint length )
	{
		var layers = new Dictionary<(int Position, int Used), Node>[slots + 1];
		layers[0] = new() { [(16, 0)] = new Node { Count = 1, ParentPosition = -1, ParentUsed = -1, Controls = -1 } };
		long work = 0;
		long states = 1;

		void Add( Dictionary<(int Position, int Used), Node> layer, int position, int usedSoFar, in Node from, int fromPosition, int fromUsed, int controls )
		{
			var key = (position, usedSoFar);
			if ( layer.TryGetValue( key, out var existing ) )
			{
				existing.Count = Math.Min( 2, existing.Count + from.Count );
				layer[key] = existing;
				return;
			}
			if ( ++states > MaximumSearchStates )
				throw new InvalidDataException( $"Save RideSystem block's control-count search exceeds {MaximumSearchStates} states; the block is refused." );
			layer[key] = new Node { Count = from.Count, ParentPosition = fromPosition, ParentUsed = fromUsed, Controls = controls };
		}

		for ( var slot = 0; slot < slots; slot++ )
		{
			var next = layers[slot + 1] = new();
			var slotsAfter = slots - slot - 1;
			foreach ( var (key, node) in layers[slot] )
			{
				var (position, usedSoFar) = key;
				var freeSoFar = slot - usedSoFar;
				if ( ++work > MaximumSearchWork )
					throw new InvalidDataException( $"Save RideSystem block's control-count search exceeds {MaximumSearchWork} steps; the block is refused." );
				if ( position >= data.Length )
					continue;
				switch ( data[position] )
				{
					case 0:
						// The stored first free slot is the free list's head: the first absent slot carries its ordinal.
						if ( freeSoFar == 0 && slot != firstFreeSlot )
							continue;
						if ( freeSoFar + 1 > free || !Viable( data, position + 1, slotsAfter, usedSoFar, used ) )
							continue;
						Add( next, position + 1, usedSoFar, node, position, usedSoFar, -1 );
						break;
					case 1:
						if ( usedSoFar + 1 > used || !TryMeasure( data, position, out var recordBytes, out _ ) )
							continue;
						var after = position + 1 + recordBytes;
						work += recordBytes / 4;
						var maximum = (data.Length - after) / ControlBytes;
						var first = 1;
						var last = maximum;
						// Controls of a record the table resolves: exactly the table's count.
						var infoId = BinaryPrimitives.ReadUInt32LittleEndian( data[(position + 1)..] );
						if ( table != null && infoId <= int.MaxValue && table( (int)infoId ) is { } pinned )
							first = last = Math.Max( 1, pinned );
						if ( last > maximum )
							continue;
						for ( var controls = first; controls <= last; controls++ )
						{
							work++;
							if ( work > MaximumSearchWork )
								throw new InvalidDataException( $"Save RideSystem block's control-count search exceeds {MaximumSearchWork} steps; the block is refused." );
							var to = after + controls * ControlBytes;
							if ( to > data.Length )
								break;
							if ( Viable( data, to, slotsAfter, usedSoFar + 1, used ) )
								Add( next, to, usedSoFar + 1, node, position, usedSoFar, controls );
						}
						break;
					default:
						continue;
				}
			}
		}

		if ( !layers[slots].TryGetValue( (data.Length, used), out var terminal ) )
			throw new InvalidDataException( $"Save RideSystem block does not walk from its {slots} slots ({used} used, {free} free, first free {firstFreeSlot}) to its {length}-byte shape." );
		if ( terminal.Count > 1 )
			throw new InvalidDataException( $"Save RideSystem block's {slots} slots ({used} used, {free} free) walk to its {length}-byte shape in more than one way (the animation control counts are not stored); the block is refused as ambiguous." );

		// Exactly one complete parse: rebuild its control counts from the predecessor edges.
		var controlCounts = new int[slots];
		var tableCounts = 0;
		var (cursorPosition, cursorUsed) = (data.Length, used);
		var node2 = terminal;
		for ( var slot = slots; slot > 0; slot-- )
		{
			controlCounts[slot - 1] = node2.Controls;
			(cursorPosition, cursorUsed) = (node2.ParentPosition, node2.ParentUsed);
			node2 = layers[slot - 1][(cursorPosition, cursorUsed)];
		}
		if ( table != null )
		{
			// Count the records the table resolved (their counts equal the table's by construction).
			var position = 16;
			for ( var slot = 0; slot < slots; slot++ )
			{
				if ( data[position] == 0 )
				{
					position++;
					continue;
				}
				TryMeasure( data, position, out var recordBytes, out _ );
				var infoId = BinaryPrimitives.ReadUInt32LittleEndian( data[(position + 1)..] );
				if ( infoId <= int.MaxValue && table( (int)infoId ) != null )
					tableCounts++;
				position += 1 + recordBytes + controlCounts[slot] * ControlBytes;
			}
		}
		return (controlCounts, tableCounts);
	}

	/// <summary>Whether the walk can still end exactly at the block's end from <paramref name="position"/> with
	/// <paramref name="slotsAfter"/> slots to go: each slot needs at least its presence byte, the next presence
	/// byte must be 0 or 1, and the used count must still be reachable.</summary>
	private static bool Viable( ReadOnlySpan<byte> data, int position, int slotsAfter, int usedSoFar, int used ) =>
		position <= data.Length && usedSoFar <= used && used - usedSoFar <= slotsAfter
		&& (slotsAfter > 0
			// every remaining present slot needs its presence byte, header and one control; every other slot one byte
			? (long)data.Length - position >= (long)(used - usedSoFar) * (1 + RecordHeaderBytes + ControlBytes) + (slotsAfter - (used - usedSoFar)) && data[position] <= 1
			: position == data.Length && usedSoFar == used);

	/// <summary>
	/// The one validity predicate of a present slot's fixed part, shared by the search and the decoder: the header
	/// fits, its instance-flag word holds only the writer's bits, its node and embedded-node counts are bounded,
	/// the embedded nodes and node-state words fit, and every node-state word holds only the writer's bits
	/// (bit 4 and bits 12+ are refused). <paramref name="bytes"/> is the record's size without its presence byte
	/// and without its controls.
	/// </summary>
	private static bool TryMeasure( ReadOnlySpan<byte> data, int position, out int bytes, out string? error )
	{
		bytes = 0;
		var header = position + 1;
		if ( header + RecordHeaderBytes > data.Length )
		{
			error = "its header runs past the block";
			return false;
		}
		var flags = BinaryPrimitives.ReadUInt32LittleEndian( data[(header + 0x1C)..] );
		if ( flags > ValidInstanceFlags )
		{
			error = $"its instance-flag word 0x{flags:X8} holds bits the writer never stores (bits 0-8)";
			return false;
		}
		var nodeCount = BinaryPrimitives.ReadUInt16LittleEndian( data[(header + 0x2A)..] );
		var embeddedCount = BinaryPrimitives.ReadUInt16LittleEndian( data[(header + 0x2C)..] );
		// The counts are u16 fields; no bound narrower than that is applied, because a record excluded by a smaller cap could hide a second complete parse.
		bytes = RecordHeaderBytes + embeddedCount * 8 + nodeCount * 4;
		if ( header + bytes > data.Length )
		{
			error = "its embedded nodes and node states run past the block";
			return false;
		}
		var states = header + RecordHeaderBytes + embeddedCount * 8;
		for ( var node = 0; node < nodeCount; node++ )
		{
			var packed = BinaryPrimitives.ReadUInt32LittleEndian( data[(states + node * 4)..] );
			if ( (packed & ~ValidNodeStateBits) != 0 )
			{
				error = $"its node state word 0x{packed:X8} uses bits the writer never stores (bits 0-3 and 5-11)";
				return false;
			}
		}
		error = null;
		return true;
	}

	private static SaveRideSystemRecord ReadRecord( ReadOnlySpan<byte> data, int slot, int position, int blockOffset, int controlCount )
	{
		if ( !TryMeasure( data, position, out var fixedBytes, out var error ) )
			throw new InvalidDataException( $"Save RideSystem slot {slot}: {error}." );
		var header = position + 1;
		static int U32( ReadOnlySpan<byte> data, int offset ) => (int)BinaryPrimitives.ReadUInt32LittleEndian( data[offset..] );
		var flags = BinaryPrimitives.ReadUInt32LittleEndian( data[(header + 0x1C)..] );
		var nodeCount = BinaryPrimitives.ReadUInt16LittleEndian( data[(header + 0x2A)..] );
		var embeddedCount = BinaryPrimitives.ReadUInt16LittleEndian( data[(header + 0x2C)..] );
		var embeddedNodes = new SaveRideSystemEmbeddedNode[embeddedCount];
		var cursor = header + RecordHeaderBytes;
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
		if ( cursor != header + fixedBytes + controlCount * ControlBytes )
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
