using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>Which game's block readers a payload is split with (<see cref="SavePayloadLayout.Parse(ReadOnlySpan{byte}, SavePayloadProfile)"/>).</summary>
public enum SavePayloadProfile
{
	/// <summary>Theme Park World / tp.exe (Mac and PC): the rules in <see cref="SaveBlockWidths"/>.</summary>
	ThemeParkWorld,
	/// <summary>Theme Park Inc / Game.exe: the same 17 delimiters; 45 message lists, a 48-byte camera, an empty UI block and its own World block (<see cref="ThemeParkIncSaveWorld"/>).</summary>
	ThemeParkInc
}

/// <summary>
/// The serialized width of each framed block of a decoded TPWS/TPWI payload, read from tp.exe's own block readers
/// (the loader <c>0x415270</c> runs one reader per block and then compares the next four bytes with the block's tag,
/// <c>0x415371</c> and its eighteen successors, so a block's width is whatever its reader consumes).
/// <list type="bullet">
/// <item>Scripts <c>0x475730</c>: <c>u32 'SCPT', u32 0x118, u32 n, n × u32 slot</c>, then <c>0x118</c> bytes for each nonzero slot 1..n-1; another magic or record size fails the original load ("Restore scripts FAILED") and is rejected here.</item>
/// <item>Particles <c>0x51f7a0</c>: <c>u32 'PCLT', u32 on</c> and, when on, <c>u32 0x9e68 + 0x9e68 bytes, u32 0x8b60 + 0x8b60 bytes, u32 c + c bytes</c> (a different table size fails the original load and is rejected here; the game's save call <c>0x41661B</c> always writes c = 0).</item>
/// <item>Message centre <c>0x40fd60</c>: <c>u32</c>, then 29 lists of <c>u32 count, count × u16</c>.</item>
/// <item>Clock <c>0x402e30</c> 8, vanilla time <c>0x403260</c> 4, game system <c>0x5506e0</c> 36 (nine words).</item>
/// <item>RideSystem <c>0x4647a0</c>: <c>u32 length</c> and that many bytes.</item>
/// <item>Track rides <c>0x543560</c>: a chunk stream whose container header <c>u32 type 1, u32, u32 size</c> carries the whole size; the
/// close <c>0x544290</c> seeks to the chunk start plus that size.</item>
/// <item>Flying rides <c>0x55e020</c>: <c>u32 'FLY_', u32 on</c> and, when on, <c>u32 n, n bytes</c> (the slot array), ten words and, for each
/// nonzero slot 1..98 of the array, as many bytes as the tenth word (the last of the ten) says.</item>
/// <item>RSSE <c>0x5597a0</c>: <c>u32 'RSSE', u32 0x14, 20 bytes</c>, five words, <c>u32 records, u32 0xF4</c>, then per record the fixed
/// struct, eight counted buffers (the seventh in units of 32 bytes), the folder string and an <c>'OBJ '</c> list (<c>u32 count, u32 0x1C, count × 0x1C</c>); see <see cref="SaveRsseBlock"/>.</item>
/// <item>Camera <c>0x42cec0</c> 40; Advisor <c>0x599d00</c>: <c>u32 'ADV_', u32 n, n bytes, u32 m, m bytes</c>; Sound <c>0x51c400</c>:
/// <c>u32 on, u32, u32 n, n bytes</c> and, when on, a chunk of three words, a length word L and L - 4 bytes; cheats <c>0x405570</c> 2;
/// advisor scoring <c>0x59c860</c> 4; UI <c>0x481680</c> (the last block): <c>u32 count, count × 0x21c</c>.</item>
/// </list>
/// A Coasters block with a coaster (its trains and cars depend on the coaster type's train and car counts) has no width here, and the World block is measured
/// by <see cref="TryMeasureWorld"/> (its control manager, macro AI, map cells and thing list are fixed-width or self-closing); <see cref="TryMeasure"/> returns false for both.
/// </summary>
public static class SaveBlockWidths
{
	/// <summary>Theme Park Inc's message centre keeps 45 lists where Theme Park World's keeps 29 (reader 0x437140 against 0x40fd60).</summary>
	// [BIN:TPI-EXE:0x437140 message centre loader] u32, then 45 lists of u32 count and count u16 entries; the prebuilt's block is 476 bytes
	public const int ThemeParkIncMessageLists = 45;
	/// <summary>Theme Park Inc's camera adds gf_SavedRideXRotation and gf_SavedRideYRotation to the 40 bytes.</summary>
	// [BIN:TPI-EXE:0x46a150 camera loader] 40 bytes plus two floats
	public const int ThemeParkIncCameraBytes = 48;

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

	/// <summary>The cells of the World's map: the loader's loop runs a fixed 0x4000 times (128 × 128).</summary>
	public const int WorldCells = 0x4000;
	/// <summary>A map cell's status byte and the records its bits 0, 1 and 2 add: the map cell (52 bytes), the track cell (31) and the effects record (10).</summary>
	public const int CellStatusBytes = 1, MapCellBytes = 52, TrackCellBytes = 31, EffectsCellBytes = 10;

	/// <summary>
	/// The width of a World block that starts at <paramref name="offset"/>, from the World loader's own readers; false (nothing consumed) when the bytes do not read as one,
	/// which is where the original would run past the save or fail a sanity check.
	/// </summary>
	/// <remarks>
	/// <c>0x5179C0</c> reads the 64 bytes of variables, then the control manager <c>0x4D3AA0</c> (150 records of 32 bytes, a count and a key: 4806), the macro AI
	/// <c>0x4D7A70</c> (the hiring pool <c>0x507850</c> 670, the calendar <c>0x4F7F30</c> 28 and the arrival state <c>0x4CF050</c> 18) and the map <c>0x4D7EA0</c>: for each of
	/// 0x4000 cells a status byte, then the map cell <c>0x4D8DD0</c> (the 29-byte base <c>0x4D0B30</c> and 23 bytes of litter fields) when bit 0 is set, the track cell
	/// <c>0x50C200</c> (the base and a 2-byte segment number) when bit 1 is set and the effects record <c>0x502490</c> (five words) when bit 2 is set; the thing list follows and closes itself.
	/// </remarks>
	// [BIN:TPW-EXE:0x5179C0 World loader] variables, 0x4D3AA0, 0x4D7A70 (0x507850, 0x4F7F30, 0x4CF050), 0x4D7EA0 (status byte, 0x4D8DD0, 0x50C200, 0x502490 per cell), "Used Thing Head" and the things; a status byte's other bits are not read
	public static bool TryMeasureWorld( ReadOnlySpan<byte> payload, int offset, out int width ) => TryMeasureWorld( payload, offset, out width, out _ );

	/// <summary><see cref="TryMeasureWorld(ReadOnlySpan{byte}, int, out int)"/> that also gives the offset where the thing list (<c>Used Thing Head</c>) starts.</summary>
	public static bool TryMeasureWorld( ReadOnlySpan<byte> payload, int offset, out int width, out int thingsStart )
	{
		width = 0;
		thingsStart = 0;
		try
		{
			var position = checked( offset + SaveWorldVariables.Bytes + SaveObjectControls.Bytes + SaveStaffPool.Bytes + SaveCalendar.Bytes + SaveWorld.ArrivalBytes );
			for ( var cell = 0; cell < WorldCells; cell++ )
			{
				if ( position >= payload.Length )
					return false;
				var status = payload[position];
				position += CellStatusBytes + ((status & 1) != 0 ? MapCellBytes : 0) + ((status & 2) != 0 ? TrackCellBytes : 0) + ((status & 4) != 0 ? EffectsCellBytes : 0);
			}
			if ( position > payload.Length )
				return false;
			width = SaveThingChain.End( payload, position ) - offset;
			thingsStart = position;
			return true;
		}
		catch ( InvalidDataException )
		{
			return false;
		}
		catch ( OverflowException )
		{
			return false;
		}
	}

	/// <summary>Whether the block's width follows from its bytes alone (World and a Coasters block with a coaster do not).</summary>
	public static bool IsTraced( SaveBlock block ) => block is not (SaveBlock.World or SaveBlock.Coasters or SaveBlock.ActionRecording);

	/// <summary>
	/// Measures <paramref name="block"/> starting at <paramref name="offset"/>. Returns false for the blocks without a traced width.
	/// Throws <see cref="InvalidDataException"/> when the bytes do not read as the block (the original loader would report the
	/// SAD_ error, or run past the end of the save).
	/// </summary>
	public static bool TryMeasure( ReadOnlySpan<byte> payload, SaveBlock block, int offset, out int width ) =>
		TryMeasure( payload, block, offset, out width, SavePayloadProfile.ThemeParkWorld );

	/// <summary>
	/// <see cref="TryMeasure(ReadOnlySpan{byte}, SaveBlock, int, out int)"/> under one game's reader rules: the Theme Park Inc profile has 45 message-centre lists, a 48-byte camera and an
	/// empty UI block (its writer 0x4f8630 is a bare <c>ret</c> and its loader reads nothing); the other blocks read as in tp.exe.
	/// </summary>
	public static bool TryMeasure( ReadOnlySpan<byte> payload, SaveBlock block, int offset, out int width, SavePayloadProfile profile )
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
				// [BIN:TPW-EXE:0x475730 Scripts loader] without 'SCPT' and the 0x118 record size the original reports "Restore scripts FAILED" and the whole load fails
				if ( scriptMagic != ScriptsMagic || scriptSize != ScriptRecordBytes )
					throw new InvalidDataException( $"Save Scripts block does not start with 'SCPT' and a record size of {ScriptRecordBytes} (magic 0x{scriptMagic:X8}, size {scriptSize}); the original fails with \"Restore scripts FAILED\"." );
				var scripts = reader.U32();
				var slots = reader.Take( checked((long)scripts * 4) );
				var present = 0;
				for ( var slot = 1; slot < scripts; slot++ )
					if ( BinaryPrimitives.ReadUInt32LittleEndian( payload[(slots + slot * 4)..] ) != 0 )
						present++;
				reader.Take( (long)present * ScriptRecordBytes );
				break;
			case SaveBlock.Particles:
				// [BIN:TPW-EXE:0x51F7A0 Particles loader] a table whose stored size is not 0x9E68 / 0x8B60 fails the original load; the pool length c is 0 in every save the game writes (call 0x41661B)
				if ( reader.U32() == ParticlesMagic && reader.U32() != 0 )
				{
					var headerSize = reader.U32();
					if ( headerSize != ParticleHeaderBytes )
						throw new InvalidDataException( $"Save Particles block stores a {headerSize}-byte globals table; the original loader requires {ParticleHeaderBytes}." );
					reader.Take( ParticleHeaderBytes );
					var fieldSize = reader.U32();
					if ( fieldSize != ParticleFieldBytes )
						throw new InvalidDataException( $"Save Particles block stores a {fieldSize}-byte table; the original loader requires {ParticleFieldBytes}." );
					reader.Take( ParticleFieldBytes );
					reader.Take( reader.U32() );
				}
				break;
			case SaveBlock.MessageCentre:
				reader.U32();
				for ( var list = 0; list < (profile == SavePayloadProfile.ThemeParkInc ? ThemeParkIncMessageLists : MessageLists); list++ )
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
						if ( position > payload.Length - offset )
							throw new InvalidDataException( $"Save {block} sub-chunk at {position} lies beyond the end of the payload (truncated)." );
						var sub = new Cursor( payload, offset + (int)position, block );
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
				// A block without the magic is not read further (width of the tag alone, unverified against the loader); a block with it must read as <see cref="SaveRsseBlock"/>.
				if ( reader.U32() != RsseMagic )
					break;
				width = SaveRsseBlock.Parse( payload, offset ).Length;
				return true;
			case SaveBlock.Camera:
				reader.Take( profile == SavePayloadProfile.ThemeParkInc ? ThemeParkIncCameraBytes : 40 );
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
				if ( profile != SavePayloadProfile.ThemeParkInc )
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
