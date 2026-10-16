namespace OpenTPW;

/// <summary>
/// One 34-byte saved track section of the Coasters block (loader 0x39f98, docs/TPWS-PAYLOAD.md
/// "Coasters block"): <c>u32</c> remapped section+8 flags, <c>u32</c> section+112, the descriptor's
/// cell coordinates, the cell-stack insertion ordinal, five unaligned floats and the narrowed link
/// count, followed by that many <c>u16</c> link ordinals (each its own 2-byte read, like the original).
/// <see cref="HeightParameter"/> is section+232, the clamped height parameter <c>t</c> the builder stores
/// (COAST-plan §4.1); <see cref="Bank"/> is section+216, the auto-bank the knots pass keeps on the node
/// (COAST-plan §13.8). Link ordinals are node ordinals into the section list, resolved through the rebuilt
/// topology (0x41dcc); they are not validated against the section count because filtered nodes
/// (descriptor flag 0x10) stay in the list. Every other field is kept raw with its native offset as name.
/// </summary>
public readonly record struct SaveCoasterSection(
	int Offset, uint Flags, uint Field112, int CellX, int CellY, byte StackOrdinal,
	float HeightParameter, float Bank, float Field220, float Field224, float Field248, ushort[] Links )
{
	public const int Bytes = 34;

	// [APPROX:SAVE-921] the section's flags are the remapped section+8 node flags; they are kept raw and no bit is interpreted. tp.exe's saver writes memory 0x80/0x40/0x100 as wire 0x8/0x10/0x20 and 0x10000..0x400000 as wire 0x40..0x800 (0x437615..0x4376E0); its loader restores 0x1/0x2/0x4 and 0x10000..0x400000 but not 0x80/0x40/0x100 (0x437EA6..0x437F58) — evidence needed: the meaning of the remapped bits and of wire 0x8/0x10/0x20 on load
	// [APPROX:SAVE-922] the word section+112 and the floats section+220/+224/+248 are saved but their meaning is not traced (+248 matches the loop-rotate slider's node +248 by offset only); they are reported raw — evidence needed: their readers in the original coaster loader 0x437B60
}

/// <summary>
/// One saved coaster of the Coasters block: its packed 32-byte header (the remapped controller flags, seven
/// controller words, the power factor as <c>f32</c> at +18, the section count at +22 and the riders-on-board
/// count at +28), the auxiliary link block the loader reads before any section record, the section records with
/// their links, and the train/car/passenger payload that follows the topology (serializer 0x38da4). The header's
/// controller words keep their native offsets as names. <see cref="PowerFactor"/> is controller+44 (0 or 1,
/// COAST-plan §17.4) and <see cref="RidersOnBoard"/> is controller+320; both are the only header values the
/// traced evidence names (the save contract, docs/reverse/PPC-rides.md).
/// </summary>
public sealed record SaveCoaster(
	int Offset, uint Flags, uint Controller8, ushort Controller12, ushort Controller16, ushort Controller20,
	ushort Controller28, ushort Controller32, float PowerFactor, ushort SectionCount, ushort Controller244,
	ushort Controller240, uint RidersOnBoard, ushort[] AuxiliaryOrdinals, IReadOnlyList<SaveCoasterSection> Sections,
	byte[] Trains, int TrainsOffset )
{
	public const int HeaderBytes = 32;
}

/// <summary>
/// The Coasters block of a decoded save payload — the body between the stored <c>EMAK</c> (Camera) and
/// <c>SAOC</c> (Coasters) delimiters, per the traced original coaster save/load contract (docs/reverse/PPC-rides.md,
/// "Original TPW coaster save/load contract"): a linked-list coaster count, three globals whose semantics are
/// unqualified, then per coaster a 32-byte header, one auxiliary link block, the section records with their links
/// and the train payload. Read-only: no original coaster chunk is ever written. The layout is traced static
/// evidence from the Mac PowerPC build; the only original Theme Park World fixture (jungle Easymode) holds the
/// empty boundary (count 0, globals 0, 0, 1), so the nonempty shape is cross-checked on the sibling Theme Park
/// Inc prebuilt save's bytes, whose coaster body follows this contract exactly (docs/TPWS-PAYLOAD.md).
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
	/// Bound on the auxiliary link block. The auxiliary ordinals land in an existing node's word-array slots whose
	/// native storage size is not traced; beyond this bound the block is refused rather than guessed at.
	/// </summary>
	public const int MaximumAuxiliaryLinks = 4096;

	/// <summary>Parses the Coasters block of <paramref name="payload"/> as split by <paramref name="layout"/>.</summary>
	public static SaveCoasterBlock Parse( ReadOnlySpan<byte> payload, SavePayloadLayout layout, int countX, int countY )
	{
		ArgumentNullException.ThrowIfNull( layout );
		if ( layout.PayloadLength != payload.Length )
			throw new ArgumentException( "Layout does not describe this payload.", nameof( layout ) );
		var block = layout[SaveBlock.Coasters];
		return ParseBlock( payload.Slice( block.Offset, block.Length ), countX, countY, block.Offset );
	}

	/// <summary>
	/// Parses the Coasters block's own bytes (offset 0 of <paramref name="data"/>; <paramref name="baseOffset"/> is
	/// added to every reported offset). Throws <see cref="InvalidDataException"/> on truncation, an out-of-range
	/// count or float, a section cell outside the <paramref name="countX"/>×<paramref name="countY"/> grid, or a
	/// block that lists more than one coaster.
	/// </summary>
	public static SaveCoasterBlock ParseBlock( ReadOnlySpan<byte> data, int countX, int countY, int baseOffset = 0 )
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
			if ( data.Length != EmptyBlockBytes )
				throw new InvalidDataException( $"Save coasters block has no coaster but {data.Length} bytes; an empty block is {EmptyBlockBytes}." );
			return new SaveCoasterBlock( 0, global1, global2, global3, Array.Empty<SaveCoaster>() );
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
			var riders = header.U32();
			if ( !float.IsFinite( power ) )
				throw new InvalidDataException( $"Save {what} has a non-finite power factor {power}." );
						var coasterOffset = reader.Position;
			reader.Skip( SaveCoaster.HeaderBytes );

			// The loader reads one auxiliary block (a u32 count plus u16 ordinals into an existing node's link slots)
			// before any ordinary section record; the saver writes it where the node walk meets ordinal 2.
			var auxiliary = new SaveFieldReader( data, reader.Position, $"coasters block {what} auxiliary links" );
			var auxiliaryCount = auxiliary.U32();
			if ( auxiliaryCount > MaximumAuxiliaryLinks )
				throw new InvalidDataException( $"Save {what} has {auxiliaryCount} auxiliary links; more than the bound of {MaximumAuxiliaryLinks}." );
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
				var field112 = record.U32();
				var cellX = record.U16();
				var cellY = record.U16();
				var stack = record.U8();
				var height = record.F32();
				var bank = record.F32();
				var field220 = record.F32();
				var field224 = record.F32();
				var field248 = record.F32();
				var links = record.U8();
				if ( cellX >= countX || cellY >= countY )
					throw new InvalidDataException( $"Save {what} section {section} stands at cell ({cellX}, {cellY}); outside the {countX}x{countY} grid." );
				if ( !float.IsFinite( height ) || !float.IsFinite( bank ) || !float.IsFinite( field220 ) || !float.IsFinite( field224 ) || !float.IsFinite( field248 ) )
					throw new InvalidDataException( $"Save {what} section {section} has a non-finite height, bank or unnamed float." );
				var linkValues = new ushort[links];
				for ( var link = 0; link < links; link++ )
					linkValues[link] = record.U16();
				sections[section] = new SaveCoasterSection( baseOffset + sectionOffset, sectionFlags, field112, cellX, cellY, stack,
					height, bank, field220, field224, field248, linkValues );
				reader.Skip( SaveCoasterSection.Bytes + 2 * links );
			}

			// The last coaster's train payload is whatever remains of the block.
			// [APPROX:SAVE-923] the train/car/passenger payload that follows each coaster's sections is not decoded; it is preserved as raw bytes and never interpreted. tp.exe writes one 27-byte record per train slot of the coaster type (0x43783B), then per car an 11-byte record (0x4378A5) with two counts and the two counted u32 lists — evidence needed: the meanings of the train and car words
			coasters[index] = new SaveCoaster( baseOffset + coasterOffset, flags, controller8, controller12, controller16,
				controller20, controller28, controller32, power, sectionCount, controller244, controller240, riders,
				ordinals, sections, data[reader.Position..].ToArray(), baseOffset + reader.Position );
			if ( index + 1 >= count )
				continue;
			// [APPROX:SAVE-920] a Coasters block with more than one coaster is refused: tp.exe's loader 0x437B60 reads every coaster, but each coaster's train payload spans its type's train slots (type +0x2E8, 0x438386) and car counts, which this reader does not have, so the next header cannot be located — evidence needed: the coaster type's train slot and car counts at this layer
			throw new InvalidDataException( reader.Position == data.Length
				? $"Save coasters block lists {count} coasters but ends after {what}; coaster {index + 1}'s header is missing."
				: $"Save coasters block lists {count} coasters; the train payload that follows {what}'s sections is not decoded, so coaster {index + 1}'s header cannot be located." );
		}
		return new SaveCoasterBlock( count, global1, global2, global3, coasters );
	}
}
