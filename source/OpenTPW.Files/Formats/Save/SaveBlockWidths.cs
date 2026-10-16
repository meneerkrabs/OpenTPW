using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// The serialized width of each framed block of a decoded TPWS/TPWI payload, read from tp.exe's own block readers
/// (the loader <c>0x415270</c> runs one reader per block and then compares the next four bytes with the block's tag,
/// <c>0x415371</c> and its eighteen successors, so a block's width is whatever its reader consumes).
/// <list type="bullet">
/// <item>Scripts <c>0x475730</c>: <c>u32 'SCPT', u32 0x118, u32 n, n × u32 slot</c>, then <c>0x118</c> bytes for each nonzero slot 1..n-1.</item>
/// <item>Particles <c>0x51f7a0</c>: <c>u32 'PCLT', u32 on</c> and, when on, <c>u32 0x9e68 + 0x9e68 bytes, u32 0x8b60 + 0x8b60 bytes, u32 c + c bytes</c>.</item>
/// <item>Message centre <c>0x40fd60</c>: <c>u32</c>, then 29 lists of <c>u32 count, count × u16</c>.</item>
/// <item>Clock <c>0x402e30</c> 8, vanilla time <c>0x403260</c> 4, game system <c>0x5506e0</c> 36 (nine words).</item>
/// <item>RideSystem <c>0x4647a0</c>: <c>u32 length</c> and that many bytes.</item>
/// <item>Track rides <c>0x543560</c>: a chunk stream whose container header <c>u32 type 1, u32, u32 size</c> carries the whole size; the
/// close <c>0x544290</c> seeks to the chunk start plus that size.</item>
/// <item>Flying rides <c>0x55e020</c>: <c>u32 'FLY_', u32 on</c> and, when on, <c>u32 n, n bytes</c> (the slot array), ten words and, for each
/// nonzero slot 1..98 of the array, as many bytes as the tenth word (the last of the ten) says.</item>
/// <item>RSSE <c>0x5597a0</c>: <c>u32 'RSSE', u32 n, n bytes</c>, five words, <c>u32 records, u32 record size</c>, then per record the fixed
/// struct, nine counted buffers (the seventh in units of 32 bytes) and an <c>'OBJ '</c> list (<c>u32 count, u32 size, count × size</c>).</item>
/// <item>Camera <c>0x42cec0</c> 40; Advisor <c>0x599d00</c>: <c>u32 'ADV_', u32 n, n bytes, u32 m, m bytes</c>; Sound <c>0x51c400</c>:
/// <c>u32 on, u32, u32 n, n bytes</c> and, when on, a chunk of three words, a length word L and L - 4 bytes; cheats <c>0x405570</c> 2;
/// advisor scoring <c>0x59c860</c> 4; UI <c>0x481680</c> (the last block): <c>u32 count, count × 0x21c</c>.</item>
/// </list>
/// The World block (its control manager and macro AI widths are not traced) and a Coasters block with a coaster (its trains
/// and cars depend on the coaster type, SAVE-920) have no width here; <see cref="TryMeasure"/> returns false for them.
/// </summary>
public static class SaveBlockWidths
{
	public const uint ScriptsMagic = 0x53435054;
	public const int ScriptRecordBytes = 0x118;
	public const uint ParticlesMagic = 0x5054434c;
	public const int ParticleHeaderBytes = 0x9e68;
	public const int ParticleFieldBytes = 0x8b60;
	public const int MessageLists = 29;
	public const int GameSystemBytes = 36;
	public const int CheatBytes = 2;
	public const int AdvisorScoringBytes = 4;
	public const uint FlyingMagic = 0x5f594c46;
	public const int FlyingSlots = 99;
	public const uint RsseMagic = 0x45535352;
	public const uint RsseObjectsMagic = 0x204a424f;
	public const uint AdvisorMagic = 0x5f564441;
	public const int UiRecordBytes = 0x21c;
	public const int TrackChunkHeaderBytes = 12;

	/// <summary>Whether the block's width follows from its bytes alone (World and a Coasters block with a coaster do not).</summary>
	public static bool IsTraced( SaveBlock block ) => block is not (SaveBlock.World or SaveBlock.Coasters or SaveBlock.ActionRecording);

	/// <summary>
	/// Measures <paramref name="block"/> starting at <paramref name="offset"/>. Returns false for the blocks without a traced width.
	/// Throws <see cref="InvalidDataException"/> when the bytes do not read as the block (the original loader would report the
	/// SAD_ error, or run past the end of the save).
	/// </summary>
	public static bool TryMeasure( ReadOnlySpan<byte> payload, SaveBlock block, int offset, out int width )
	{
		width = 0;
		if ( offset < 0 || offset > payload.Length )
			throw new InvalidDataException( $"Save {block} block starts outside the payload." );
		var reader = new Cursor( payload, offset, block );
		switch ( block )
		{
			case SaveBlock.Scripts:
				var scriptMagic = reader.U32();
				var scriptSize = reader.U32();
				if ( scriptMagic != ScriptsMagic || scriptSize != ScriptRecordBytes )
				{
					// The loader reads the two words, finds no script table and keeps its defaults.
					width = reader.Taken;
					return true;
				}
				var scripts = reader.U32();
				var slots = reader.Take( checked((long)scripts * 4) );
				var present = 0;
				for ( var slot = 1; slot < scripts; slot++ )
					if ( BinaryPrimitives.ReadUInt32LittleEndian( payload[(slots + slot * 4)..] ) != 0 )
						present++;
				reader.Take( (long)present * ScriptRecordBytes );
				break;
			case SaveBlock.Particles:
				if ( reader.U32() == ParticlesMagic && reader.U32() != 0 )
				{
					if ( reader.U32() != ParticleHeaderBytes )
						break;
					reader.Take( ParticleHeaderBytes );
					if ( reader.U32() != ParticleFieldBytes )
						break;
					reader.Take( ParticleFieldBytes );
					reader.Take( reader.U32() );
				}
				break;
			case SaveBlock.MessageCentre:
				reader.U32();
				for ( var list = 0; list < MessageLists; list++ )
					reader.Take( reader.U32() * 2L );
				break;
			case SaveBlock.Clock:
				reader.Take( 8 );
				break;
			case SaveBlock.VanillaTime:
				reader.Take( 4 );
				break;
			case SaveBlock.GameSystem:
				reader.Take( GameSystemBytes );
				break;
			case SaveBlock.RideSystem:
				reader.Take( reader.U32() );
				break;
			case SaveBlock.TrackRides:
			{
				var type = reader.U32();
				reader.U32();
				var size = reader.U32();
				if ( size < TrackChunkHeaderBytes )
					throw new InvalidDataException( $"Save {block} container chunk claims {size} bytes; its header alone takes {TrackChunkHeaderBytes}." );
				if ( type == 1 )
				{
					// The loader consumes sub-chunks until the container's bytes are used up, so their sizes must add up exactly.
					var position = (long)TrackChunkHeaderBytes;
					while ( position < size )
					{
						var sub = new Cursor( payload, checked(offset + (int)position), block );
						sub.U32();
						sub.U32();
						var length = sub.U32();
						if ( length < TrackChunkHeaderBytes )
							throw new InvalidDataException( $"Save {block} sub-chunk at {position} claims {length} bytes; its header alone takes {TrackChunkHeaderBytes}." );
						position += length;
					}
					if ( position != size )
						throw new InvalidDataException( $"Save {block} sub-chunks end at {position}, not at the container's {size} bytes." );
				}
				reader.Take( size - TrackChunkHeaderBytes );
				break;
			}
			case SaveBlock.FlyingRides:
				if ( reader.U32() != FlyingMagic )
					break;
				if ( reader.U32() != 0 )
				{
					var arrayBytes = reader.U32();
					var array = reader.Take( arrayBytes );
					uint last = 0;
					for ( var word = 0; word < 10; word++ )
						last = reader.U32();
					for ( var slot = 1; slot <= FlyingSlots && slot * 4L + 4 <= arrayBytes; slot++ )
						if ( BinaryPrimitives.ReadUInt32LittleEndian( payload[(array + slot * 4)..] ) != 0 )
							reader.Take( last );
				}
				break;
			case SaveBlock.Rsse:
				if ( reader.U32() != RsseMagic )
					break;
				reader.Take( reader.U32() );
				for ( var word = 0; word < 5; word++ )
					reader.U32();
				var records = reader.U32();
				var recordBytes = reader.U32();
				for ( uint record = 0; record < records; record++ )
				{
					reader.Take( recordBytes );
					for ( var buffer = 0; buffer < 9; buffer++ )
						reader.Take( reader.U32() * (buffer == 6 ? 32L : 1L) );
					if ( reader.U32() != RsseObjectsMagic )
						throw new InvalidDataException( $"Save {block} record {record} has no 'OBJ ' object list." );
					var objects = reader.U32();
					var objectBytes = reader.U32();
					reader.Take( objects * (long)objectBytes );
				}
				break;
			case SaveBlock.Camera:
				reader.Take( 40 );
				break;
			case SaveBlock.Advisor:
				if ( reader.U32() == AdvisorMagic )
				{
					reader.Take( reader.U32() );
					reader.Take( reader.U32() );
				}
				break;
			case SaveBlock.Sound:
			{
				var on = reader.U32();
				reader.U32();
				reader.Take( reader.U32() );
				if ( on != 0 )
				{
					// With no sound system the loader reads three words and a length L, then seeks L - 4 bytes on.
					reader.Take( 12 );
					var length = reader.U32();
					if ( length < 4 )
						throw new InvalidDataException( $"Save {block} chunk length {length} is below the 4 bytes of its own word." );
					reader.Take( length - 4L );
				}
				break;
			}
			case SaveBlock.Cheat:
				reader.Take( CheatBytes );
				break;
			case SaveBlock.AdvisorScoring:
				reader.Take( AdvisorScoringBytes );
				break;
			case SaveBlock.UI:
				reader.Take( reader.U32() * (long)UiRecordBytes );
				break;
			default:
				return false;
		}
		width = reader.Taken;
		return true;
	}

	private ref struct Cursor
	{
		private readonly ReadOnlySpan<byte> payload;
		private readonly int start;
		private readonly SaveBlock block;
		private int position;

		public Cursor( ReadOnlySpan<byte> payload, int offset, SaveBlock block )
		{
			this.payload = payload;
			this.block = block;
			start = position = offset;
		}

		public readonly int Taken => position - start;

		public uint U32()
		{
			var at = Take( 4 );
			return BinaryPrimitives.ReadUInt32LittleEndian( payload[at..] );
		}

		/// <summary>Consumes <paramref name="count"/> bytes and returns where they start.</summary>
		public int Take( long count )
		{
			if ( count < 0 || count > payload.Length - position )
				throw new InvalidDataException( $"Save {block} block is truncated at offset {position} (needs {count} more bytes, {payload.Length - position} remain)." );
			var at = position;
			position += (int)count;
			return at;
		}
	}
}
