namespace OpenTPW;

/// <summary>
/// The wire bits of a saved section's flag word, the node flags (node <c>+8</c>) as tp.exe's saver writes them (<c>0x437615..0x4376E0</c>) and its loader
/// restores them (<c>0x437EA6..0x437F58</c>). The names are the node flags the coaster code documents (<c>CoasterNodeFlags</c>, COAST-plan §13).
/// </summary>
[Flags]
public enum SaveCoasterSectionWire : uint
{
	None = 0,
	/// <summary>Node flag 0x1: the yaw was set by hand (restored).</summary>
	ManualYaw = 0x1,
	/// <summary>Node flag 0x2: the incline was set by hand (restored).</summary>
	ManualIncline = 0x2,
	/// <summary>Node flag 0x4: the bank was set by hand, auto-banking skips the node (restored).</summary>
	NoAutoBank = 0x4,
	/// <summary>Node flag 0x80, the first pylon of a loop pair: written, never restored; the loader tests this wire bit (0x437DE9) and runs the loop-mode setup 0x442CD0 (the counter 0x79C664 = 2), so the node constructor 0x434060 derives the flags of the pair again (0xE0 for the first node, 0x60 for the second).</summary>
	LoopFirst = 0x8,
	/// <summary>Node flag 0x40, a loop node: written, never restored; the node constructor sets it, and the loader reads this wire bit only to pick the loop-rotation float of the section's record (0x437E72).</summary>
	Loop = 0x10,
	/// <summary>Node flag 0x100, the node's bank is forced to 0: written, never restored (the stack walk 0x433BD0 sets it).</summary>
	ZeroBank = 0x20,
	/// <summary>Node flag 0x10000, the node before the cut of a reopened circuit: restored, and the loader records the node as the open chain's first node.</summary>
	CutEnd = 0x40,
	/// <summary>Node flag 0x20000, the node after the cut: restored, and the loader records the node as the open chain's last node.</summary>
	CutStart = 0x80,
	/// <summary>Node flag 0x40000, no next link: restored.</summary>
	NoNextLink = 0x100,
	/// <summary>Node flag 0x80000, no previous link: restored.</summary>
	NoPreviousLink = 0x200,
	/// <summary>Node flag 0x200000, marked for the self-intersection pass: restored when set and cleared on the node when not.</summary>
	IntersectionMarked = 0x400,
	/// <summary>Node flag 0x400000, the self-intersection pass's second mark (cleared with 0x200000 by the pass, 0x439AB0): restored when set.</summary>
	IntersectionOverflow = 0x800,
}

/// <summary>
/// The section flag word's two maps: the saver's (memory node flags to wire bits) and the loader's (wire bits back to memory flags). The loader does not
/// restore <see cref="SaveCoasterSectionWire.LoopFirst"/>, <see cref="SaveCoasterSectionWire.Loop"/> and <see cref="SaveCoasterSectionWire.ZeroBank"/>.
/// </summary>
public static class SaveCoasterSectionFlags
{
	/// <summary>The memory node flag behind each restored wire bit, in the loader's order (0x437EA6..0x437F58).</summary>
	public static IReadOnlyList<(SaveCoasterSectionWire Wire, uint Node)> Restored { get; } = Array.AsReadOnly( new[]
	{
		(SaveCoasterSectionWire.ManualYaw, 0x1u), (SaveCoasterSectionWire.ManualIncline, 0x2u), (SaveCoasterSectionWire.NoAutoBank, 0x4u),
		(SaveCoasterSectionWire.NoNextLink, 0x40000u), (SaveCoasterSectionWire.NoPreviousLink, 0x80000u), (SaveCoasterSectionWire.CutEnd, 0x10000u),
		(SaveCoasterSectionWire.CutStart, 0x20000u), (SaveCoasterSectionWire.IntersectionMarked, 0x200000u), (SaveCoasterSectionWire.IntersectionOverflow, 0x400000u),
	} );

	/// <summary>The wire bits the saver writes and the loader never reads back (the node constructor derives the node flags again).</summary>
	public const SaveCoasterSectionWire NotRestored = SaveCoasterSectionWire.LoopFirst | SaveCoasterSectionWire.Loop | SaveCoasterSectionWire.ZeroBank;

	/// <summary>The wire word the saver writes for a node's memory flags (<c>0x437615..0x4376E0</c>).</summary>
	public static uint ToWire( uint nodeFlags )
	{
		var wire = 0u;
		foreach ( var (bit, node) in Restored )
			if ( (nodeFlags & node) != 0 )
				wire |= (uint)bit;
		if ( (nodeFlags & 0x80) != 0 )
			wire |= (uint)SaveCoasterSectionWire.LoopFirst;
		if ( (nodeFlags & 0x40) != 0 )
			wire |= (uint)SaveCoasterSectionWire.Loop;
		if ( (nodeFlags & 0x100) != 0 )
			wire |= (uint)SaveCoasterSectionWire.ZeroBank;
		return wire;
	}

	/// <summary>The memory node flags the loader sets from a wire word (the bits it does not restore come from the node constructor, not from here).</summary>
	public static uint ToNodeFlags( uint wire )
	{
		var node = 0u;
		foreach ( var (bit, flag) in Restored )
			if ( (wire & (uint)bit) != 0 )
				node |= flag;
		return node;
	}
}

/// <summary>
/// One 34-byte saved track section of the Coasters block (loader 0x437B60, docs/TPWS-PAYLOAD.md "Coasters block"): the wire flags
/// (<see cref="SaveCoasterSectionWire"/>), the node's mesh instance id, the descriptor's cell coordinates, the cell-stack insertion ordinal, five unaligned floats
/// and the narrowed collision count, followed by that many <c>u16</c> collision ordinals (each its own 2-byte read, like the original). Only pylon nodes are saved:
/// the saver 0x437300 skips every chain node whose descriptor (node <c>+0x100</c>) byte <c>+4</c> carries 0x10, which excludes the three station nodes
/// (descriptor 0x12) and the end node (0x13), so section i is chain node 3 + i.
/// <list type="bullet">
/// <item><see cref="MeshInstanceId"/> is node <c>+0x70</c>, the node's pylon entry in the object table <c>0x7A4610</c>: the loader hands it to the node constructor
/// (<c>0x435020</c> → <c>0x434060</c>), which re-attaches that instance through <c>0x463AB0</c> instead of creating one (the same pattern as a car's <c>+0xC</c>).</item>
/// <item><see cref="HeightParameter"/> is node <c>+0xE8</c> (the loft parameter t, setter <c>0x444150</c>), <see cref="Bank"/> <c>+0xD8</c> and
/// <see cref="Incline"/> <c>+0xDC</c> (setter <c>0x444270</c>, kept only for a node that is not a loop node), <see cref="Yaw"/> <c>+0xE0</c> (setter <c>0x4441D0</c>) and
/// <see cref="LoopRotate"/> <c>+0xF8</c> (the same setter given the record's last float when the node is a loop node, which then zeroes bank and incline; any
/// other node clears <c>+0xF8</c> and takes bank and incline from the record).</item>
/// <item><see cref="CollisionOrdinals"/> are the node's self-intersection collision list (count <c>+0xB0</c>, eight slots <c>+0xB4..+0xD4</c>, OpenTPW's
/// <c>CoasterCollision</c>), not track topology: the chain's previous and next links are rebuilt by append order. Each value is the colliding node's chain
/// position (the saver writes the linked node's <c>node+0</c>); after the stack pass the loader resolves them with <c>0x43E520</c>, which walks the head
/// (controller <c>+0x54</c>) that many times along <c>+0x64</c>: S0 = 0, S1 = 1, S2 = 2, the pylons in saved order, then the end node. The loader stores the
/// count without a bound, so more than <see cref="SaveCoasterBlock.CollisionListSlots"/> would overrun the native list; the reader refuses such a record.</item>
/// </list>
/// </summary>
public readonly record struct SaveCoasterSection(
	int Offset, uint Flags, uint MeshInstanceId, int CellX, int CellY, byte StackOrdinal,
	float HeightParameter, float Bank, float Incline, float Yaw, float LoopRotate, ushort[] CollisionOrdinals )
{
	public const int Bytes = 34;

	/// <summary>The flag word as its named wire bits.</summary>
	public SaveCoasterSectionWire Wire => (SaveCoasterSectionWire)Flags;

	/// <summary>Whether the node is a loop node (wire bit 0x10): its record then carries the loop rotation in the last float.</summary>
	public bool IsLoop => Wire.HasFlag( SaveCoasterSectionWire.Loop );
}

/// <summary>
/// One saved coaster of the Coasters block: its packed 32-byte header, the auxiliary block (the collision list of the third station node S2, which the
/// saver writes where its chain walk meets the node with <c>node+0 == 2</c>, since station nodes are never saved as sections), the section records with
/// their collision ordinals, and the train payload that follows the topology (saver 0x437300, loader 0x437B60, docs/TPWS-PAYLOAD.md "Coasters block").
/// The header's controller words keep their native controller offsets (decimal) as names; the named accessors give their meaning:
/// <list type="bullet">
/// <item>header +4 → <see cref="Controller8"/>, controller <c>+8</c>, the state word (the low nibble is <c>CoasterControllerState</c>: 1 incomplete, 2 built,
/// 4 open, 8 closed; the mode bits 0x10/0x20/0x40, 0x40 the breakdown; the constructor's value is 0x11), restored;</item>
/// <item>header +8 → <see cref="Controller12"/>, controller <c>+0xC</c>, the coaster type id (<see cref="TypeId"/>); not restored, the constructor
/// 0x4368F0 already stored it;</item>
/// <item>header +0xA → <see cref="Controller16"/>, controller <c>+0x10</c>, the station's mesh instance id (<see cref="StationMeshInstanceId"/>), the key that
/// binds the block to the live controller (0x437CBF);</item>
/// <item>header +0xC → <see cref="Controller20"/>, controller <c>+0x14</c>, the coaster's slot in the table <c>0x790BD0</c> (<see cref="Slot"/>, the scripts'
/// coaster handle): the only header word the loader hands to the slot allocator <c>0x468760</c>, which stores a slot other than -1 as given (no allocation or fallback);</item>
/// <item>header +0xE/+0x10 → <see cref="Controller28"/>/<see cref="Controller32"/>, controller <c>+0x1C</c>/<c>+0x20</c>, the two values the placement
/// routine <c>0x466F90</c> hands the constructor; not restored (the loader passes them to <c>0x436AC0</c>, which never reads them);</item>
/// <item>header +0x12 → <see cref="PowerFactor"/>, controller <c>+0x2C</c> (0 or 1), restored;</item>
/// <item>header +0x18 → <see cref="Controller244"/>, controller <c>+0xF4</c>, the trains in service (<see cref="TrainsInService"/>), restored;</item>
/// <item>header +0x1A → <see cref="Controller240"/>, controller <c>+0xF0</c>, the seats per train (<see cref="SeatsPerTrain"/>), restored and copied by
/// <c>0x43BA90</c> to the room capacity <c>+0xB8</c>;</item>
/// <item>header +0x1C → <see cref="IntersectionCount"/>, controller <c>+0x140</c>: the track's self-intersection count (PC writers <c>0x439C40</c>, <c>0x43AD50</c>;
/// docs/COASTERS.md), not a rider count, restored.</item>
/// </list>
/// <see cref="Trains"/> are the train payload's bytes (exactly the payload when <see cref="TrainSlots"/> were decoded, else everything after the sections) and
/// <see cref="TrainSlots"/> its decoded slots.
/// </summary>
// [APPROX:SAVE-921] the header's flag word is kept raw: the saver (0x437300) writes controller +0x3C bits 0 and 1 as wire 0x1 and 0x2 (track complete, circuit cut open) and the track flags (+0) 0x20 to 0x800 as wire 0x4 to 0x100 (0x43743C..0x4374D0), the loader maps them back (0x4380D4..0x438335), and the meaning of the track flags 0x20, 0x80, 0x100, 0x200 and 0x400 is not traced (0x40 is the outside mode and 0x800 is cleared when the trains are removed, COAST-plan §22) — evidence needed: what sets and reads those track flags
// [BIN:TPW-EXE:0x4368F0 placement cell words] the header words +28 and +32 (controller +0x1C and +0x20) are the station's placement cell: the constructor stores the two values the placement routine 0x466F90 computes (the object's cell moved by the rotated footprint, per yaw 0, 90, 180, 270) and the loader hands them to the station-node builder 0x436AC0, whose body reads only its fifth argument (0x436BFA); the saver reads them back (0x4374F7, 0x437500), so they are dead data in the file and need no import
public sealed record SaveCoaster(
	int Offset, uint Flags, uint Controller8, ushort Controller12, ushort Controller16, ushort Controller20,
	ushort Controller28, ushort Controller32, float PowerFactor, ushort SectionCount, ushort Controller244,
	ushort Controller240, uint IntersectionCount, ushort[] AuxiliaryOrdinals, IReadOnlyList<SaveCoasterSection> Sections,
	byte[] Trains, int TrainsOffset, IReadOnlyList<SaveCoasterTrain>? TrainSlots = null )
{
	public const int HeaderBytes = 32;

	/// <summary>The coaster type id (controller <c>+0xC</c>); the loader does not restore it.</summary>
	public ushort TypeId => Controller12;

	/// <summary>The station's mesh instance id (controller <c>+0x10</c>), the loader's bind key.</summary>
	public ushort StationMeshInstanceId => Controller16;

	/// <summary>The coaster's slot in the table <c>0x790BD0</c> (controller <c>+0x14</c>), restored as given by the slot allocator <c>0x468760</c>.</summary>
	public ushort Slot => Controller20;

	/// <summary>The trains in service (controller <c>+0xF4</c>): the loop bound of the train passes (<c>0x435784</c>, <c>0x43BA35</c>, <c>0x43CD36</c>).</summary>
	public ushort TrainsInService => Controller244;

	/// <summary>The seats per train (controller <c>+0xF0</c>).</summary>
	public ushort SeatsPerTrain => Controller240;

	/// <summary>The controller state (<c>+8</c>, low nibble): 1 incomplete, 2 built, 4 open, 8 closed.</summary>
	public uint State => Controller8 & 0xF;

	/// <summary>Controller <c>+0x3C</c> bit 0 (wire 0x1): the track is complete.</summary>
	public bool TrackComplete => (Flags & 0x1) != 0;

	/// <summary>Controller <c>+0x3C</c> bit 1 (wire 0x2): the circuit is cut open.</summary>
	public bool CircuitOpen => (Flags & 0x2) != 0;
}

/// <summary>
/// The Coasters block of a decoded save payload — the body between the stored <c>EMAK</c> (Camera) and
/// <c>SAOC</c> (Coasters) delimiters, per the traced original coaster save/load contract (docs/reverse/PPC-rides.md,
/// "Original TPW coaster save/load contract", and tp.exe's saver 0x437300 and loader 0x437B60, docs/TPWS-PAYLOAD.md "Coasters block"):
/// a linked-list coaster count, three globals (<see cref="Global1"/> <c>0x790B04</c> the used slots, <see cref="Global2"/> <c>0x790B00</c> the free holes,
/// <see cref="Global3"/> <c>0x791CE0</c> the next-slot cursor: the state of the coaster slot allocator <c>0x468760</c>, which the loader reads straight into
/// those globals; an empty park holds 0, 0, 1), then per coaster a 32-byte header, the auxiliary block (station node S2's collision list), the section
/// records with their collision ordinals and the train payload. Read-only: no original coaster chunk is ever written. The only original Theme Park World
/// fixtures hold the empty boundary (count 0, globals 0, 0, 1), so the nonempty shape is cross-checked on the sibling Theme Park Inc prebuilt save's bytes,
/// whose coaster body follows this contract exactly (docs/TPWS-PAYLOAD.md).
/// <para>
/// The loader does not create a coaster: the RideSystem loader <c>0x4647A0</c> already recreated the station's mesh instance through <c>0x463060</c> with
/// the load flag, which built an empty controller (<c>0x4368F0</c> with load flag 1: no slot, no trains, no station nodes). Per saved coaster the loader
/// walks the controller list <c>0x790FE0</c> for the one whose <c>+0x10</c> equals the header's station mesh id (<c>0x437CBF</c>); when none matches it
/// dereferences the null pointer (<c>0x437CD5</c>), so the original crashes rather than skipping the block.
/// </para>
/// </summary>
public sealed record SaveCoasterBlock( uint Count, uint Global1, uint Global2, uint Global3, IReadOnlyList<SaveCoaster> Coasters )
{
	/// <summary>Count and the three globals; an empty block is exactly this (SavePayloadLayout enforces it on the split).</summary>
	public const int EmptyBlockBytes = 16;
	/// <summary>More coasters than the 128×128 map can hold even at the smallest station footprint; bounds the parse.</summary>
	public const int MaximumCoasters = 1024;
	/// <summary>Largest representable section count in the serialized u16 field.</summary>
	public const int MaximumSections = ushort.MaxValue;
	/// <summary>
	/// The slots of a node's native collision list (count <c>+0xB0</c>, entries <c>+0xB4..+0xD4</c>), which both the auxiliary block and each section's
	/// collision ordinals fill. The loader stores the saved count without a bound, so a larger count would overrun the list; the reader refuses it.
	/// </summary>
	public const int CollisionListSlots = 8;

	/// <summary>Parses the Coasters block of <paramref name="payload"/> as split by <paramref name="layout"/>.</summary>
	public static SaveCoasterBlock Parse( ReadOnlySpan<byte> payload, SavePayloadLayout layout, int countX, int countY,
		Func<ushort, SaveCoasterTrainLayout?>? trainLayouts = null )
	{
		ArgumentNullException.ThrowIfNull( layout );
		if ( layout.PayloadLength != payload.Length )
			throw new ArgumentException( "Layout does not describe this payload.", nameof( layout ) );
		var block = layout[SaveBlock.Coasters];
		return ParseBlock( payload.Slice( block.Offset, block.Length ), countX, countY, block.Offset, trainLayouts );
	}

	/// <summary>
	/// Parses the Coasters block's own bytes (offset 0 of <paramref name="data"/>; <paramref name="baseOffset"/> is
	/// added to every reported offset). Throws <see cref="InvalidDataException"/> on truncation, an out-of-range
	/// count or float, a section cell outside the <paramref name="countX"/>×<paramref name="countY"/> grid, or a
	/// block that lists more than one coaster.
	/// </summary>
	public static SaveCoasterBlock ParseBlock( ReadOnlySpan<byte> data, int countX, int countY, int baseOffset = 0,
		Func<ushort, SaveCoasterTrainLayout?>? trainLayouts = null ) =>
		ParseCore( data, countX, countY, baseOffset, trainLayouts, measure: false ).Block;

	/// <summary>
	/// How many bytes the Coasters block takes at the start of <paramref name="data"/>, read as the loader reads it (header, auxiliary block, sections and the type's train slots and
	/// cars of every coaster in turn) with no delimiter searched; false when a coaster's type is unknown to <paramref name="trainLayouts"/> or the bytes do not read as the block.
	/// The coaster cells are not bounded here (the map is not known); <see cref="ParseBlock"/> checks them.
	/// </summary>
	// [BIN:TPW-EXE:0x415270 block loader] each block's own reader runs and the next four bytes are compared with the block's tag (0x415371): the Coasters reader 0x437B60 consumes its bytes by the types' train and car counts and nothing searches for the tag
	public static bool TryMeasure( ReadOnlySpan<byte> data, Func<ushort, SaveCoasterTrainLayout?> trainLayouts, out int width )
	{
		ArgumentNullException.ThrowIfNull( trainLayouts );
		width = 0;
		try
		{
			var (_, consumed) = ParseCore( data, int.MaxValue, int.MaxValue, 0, trainLayouts, measure: true );
			width = consumed;
			return true;
		}
		catch ( InvalidDataException )
		{
			return false;
		}
	}

	private static (SaveCoasterBlock Block, int Consumed) ParseCore( ReadOnlySpan<byte> data, int countX, int countY, int baseOffset,
		Func<ushort, SaveCoasterTrainLayout?>? trainLayouts, bool measure )
	{
		if ( countX <= 0 || countY <= 0 )
			throw new ArgumentOutOfRangeException( countX <= 0 ? nameof( countX ) : nameof( countY ) );
		if ( baseOffset < 0 || baseOffset > int.MaxValue - data.Length )
			throw new ArgumentOutOfRangeException( nameof( baseOffset ) );
		var reader = new SaveFieldReader( data, 0, "coasters block" );
		var count = reader.U32();
		if ( count > MaximumCoasters )
			throw new InvalidDataException( $"Save coasters block lists {count} coasters; more than the bound of {MaximumCoasters}." );
		var global1 = reader.U32();
		var global2 = reader.U32();
		var global3 = reader.U32();
		if ( count == 0 )
		{
			if ( !measure && data.Length != EmptyBlockBytes )
				throw new InvalidDataException( $"Save coasters block has no coaster but {data.Length} bytes; an empty block is {EmptyBlockBytes}." );
			return (new SaveCoasterBlock( 0, global1, global2, global3, Array.Empty<SaveCoaster>() ), EmptyBlockBytes);
		}

		var coasters = new SaveCoaster[(int)count];
		for ( var index = 0; index < count; index++ )
		{
			var what = $"coaster {index}";
			var header = new SaveFieldReader( data, reader.Position, $"coasters block {what} header" );
			var flags = header.U32();
			var controller8 = header.U32();
			var controller12 = header.U16();
			var controller16 = header.U16();
			var controller20 = header.U16();
			var controller28 = header.U16();
			var controller32 = header.U16();
			var power = header.F32();
			var sectionCount = header.U16();
			var controller244 = header.U16();
			var controller240 = header.U16();
			var intersections = header.U32();
			if ( !float.IsFinite( power ) )
				throw new InvalidDataException( $"Save {what} has a non-finite power factor {power}." );
			var coasterOffset = reader.Position;
			reader.Skip( SaveCoaster.HeaderBytes );

			// The loader reads one auxiliary block (a u32 count plus u16 ordinals into the collision list of station node S2, the chain's third node)
			// before any ordinary section record; the saver writes it where its chain walk meets the node with node+0 == 2, since station nodes are never sections.
			var auxiliary = new SaveFieldReader( data, reader.Position, $"coasters block {what} auxiliary collision list" );
			var auxiliaryCount = auxiliary.U32();
			if ( auxiliaryCount > CollisionListSlots )
				throw new InvalidDataException( $"Save {what} has {auxiliaryCount} auxiliary collision ordinals; more than the bound of {CollisionListSlots} (the native collision list's slots)." );
			var ordinals = new ushort[(int)auxiliaryCount];
			for ( var link = 0; link < auxiliaryCount; link++ )
				ordinals[link] = auxiliary.U16();
			reader.Skip( 4 + (int)auxiliaryCount * 2 );

			var sections = new SaveCoasterSection[sectionCount];
			for ( var section = 0; section < sectionCount; section++ )
			{
				var record = new SaveFieldReader( data, reader.Position, $"coasters block {what} section {section}" );
				var sectionOffset = reader.Position;
				var sectionFlags = record.U32();
				var meshInstance = record.U32();
				var cellX = record.U16();
				var cellY = record.U16();
				var stack = record.U8();
				var height = record.F32();
				var bank = record.F32();
				var incline = record.F32();
				var yaw = record.F32();
				var loopRotate = record.F32();
				var collisions = record.U8();
				if ( cellX >= countX || cellY >= countY )
					throw new InvalidDataException( $"Save {what} section {section} stands at cell ({cellX}, {cellY}); outside the {countX}x{countY} grid." );
				if ( !float.IsFinite( height ) || !float.IsFinite( bank ) || !float.IsFinite( incline ) || !float.IsFinite( yaw ) || !float.IsFinite( loopRotate ) )
					throw new InvalidDataException( $"Save {what} section {section} has a non-finite height, bank, incline, yaw or loop rotation." );
				if ( collisions > CollisionListSlots )
					throw new InvalidDataException( $"Save {what} section {section} has {collisions} collision ordinals; more than the bound of {CollisionListSlots} (the native collision list's slots)." );
				var collisionOrdinals = new ushort[collisions];
				for ( var collision = 0; collision < collisions; collision++ )
					collisionOrdinals[collision] = record.U16();
				sections[section] = new SaveCoasterSection( baseOffset + sectionOffset, sectionFlags, meshInstance, cellX, cellY, stack,
					height, bank, incline, yaw, loopRotate, collisionOrdinals );
				reader.Skip( SaveCoasterSection.Bytes + 2 * collisions );
			}

			// [BIN:TPW-EXE:0x437300 coaster saver] per train slot a 27-byte record (0x43783B) and per car an 11-byte record (0x4378A5) with the car's two lists counted by its bytes 4 and 5, which the loader reads back in the same order (0x43838C..)
			// With the coaster type's counts the train payload is decoded exactly like the loader 0x437B60 reads it and the next coaster's header follows it.
			// Without them it is whatever remains of the block, which only the last coaster can claim.
			var trainsOffset = reader.Position;
			var layout = trainLayouts?.Invoke( controller16 );
			if ( measure && layout == null )
				throw new InvalidDataException( $"Save {what}'s type is unknown, so its train payload cannot be measured." );
			IReadOnlyList<SaveCoasterTrain>? slots = null;
			byte[] trainBytes;
			if ( layout is { } known )
			{
				var position = trainsOffset;
				slots = SaveCoasterTrain.ReadAll( data, ref position, known, baseOffset, $"coasters block {what}" );
				trainBytes = data[trainsOffset..position].ToArray();
				reader = new SaveFieldReader( data, position, "coasters block" );
			}
			else
			{
				trainBytes = data[trainsOffset..].ToArray();
			}
			coasters[index] = new SaveCoaster( baseOffset + coasterOffset, flags, controller8, controller12, controller16,
				controller20, controller28, controller32, power, sectionCount, controller244, controller240, intersections,
				ordinals, sections, trainBytes, baseOffset + trainsOffset, slots );
			if ( index + 1 >= count )
			{
				if ( measure )
					return (new SaveCoasterBlock( count, global1, global2, global3, coasters ), reader.Position);
				if ( slots != null && reader.Position != data.Length )
					throw new InvalidDataException( $"Save coasters block's last train payload ends at {reader.Position}, but the block has {data.Length} bytes; the coaster type's train and car counts do not match the save." );
				continue;
			}
			// [BIN:TPW-EXE:0x437B60 coaster loader] the loader reads every coaster in turn: header, auxiliary block, sections, then each of the type's train slots (the type's +0x2E8, 0x438386) with the cars of its train (the type's +0x2F4, 0x436D10), so the next header starts where the last car's lists end and a block of several coasters needs each type's counts
			if ( slots == null )
				throw new InvalidDataException( reader.Position == data.Length
					? $"Save coasters block lists {count} coasters but ends after {what}; coaster {index + 1}'s header is missing."
					: $"Save coasters block lists {count} coasters; the train payload that follows {what}'s sections needs the coaster type's train and car counts, so coaster {index + 1}'s header cannot be located." );
		}
		return (new SaveCoasterBlock( count, global1, global2, global3, coasters ), reader.Position);
	}
}
