using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>The action-recording block (writer <c>0x403780</c>): <c>u32 mLoadedPublishedPark</c>, <c>u32</c> recording size, the recording bytes.</summary>
public sealed class SaveActionRecordingData
{
	public bool Published { get; set; }
	public byte[] Recording { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// The control manager's table (<c>0x4D3AA0</c>): 150 records of 32 bytes, <c>u32 mNumObjectControls</c>, <c>u16 mPreviousSearchKey</c>. The records stay 32-byte runs: the
/// serializer writes each as one block, and the bytes after the researched flag (+0x11..+0x13) are whatever the structure held (<see cref="SaveObjectControls"/> names the fields).
/// </summary>
public sealed class SaveObjectControlTable
{
	public byte[] Records { get; set; } = new byte[SaveObjectControls.Slots * SaveObjectControl.Bytes];
	public uint Count { get; set; }
	public ushort PreviousSearchKey { get; set; }
}

/// <summary>One thing of the World's chain: its id (the previous thing's <see cref="Next"/>, or the head), the next used id (0 ends the chain), the model number and the body.</summary>
public sealed class SaveThingEntry
{
	public uint Id { get; set; }
	public uint Next { get; set; }
	public SaveThingModel Model { get; set; }
	public byte[] Body { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// The World block in the original writer's order (<c>0x516C80</c>, loader <c>0x5179C0</c>): the 26 labelled variables (64 bytes), the control manager, the macro AI (staff pool
/// <c>0x507850</c>, calendar <c>0x4F7F30</c>, arrivals <c>0x4CF050</c>), the map (<c>0x4D7EA0</c>: 0x4000 cells, a status byte and the records its bits 0, 1 and 2 add) and the
/// thing chain (<c>Used Thing Head</c>, then <c>Used Thing Next</c>, <c>thingmodel</c> and the model's serializer per thing).
/// </summary>
public sealed class OriginalSaveWorldData
{
	public SaveWorldVariables Variables { get; set; } = null!;
	public SaveObjectControlTable Controls { get; set; } = new();
	public SaveStaffPool StaffPool { get; set; } = null!;
	public SaveCalendar Calendar { get; set; }
	/// <summary>The 18 bytes of the arrival state (<c>0x4CF050</c>).</summary>
	public byte[] Arrivals { get; set; } = new byte[SaveWorld.ArrivalBytes];
	/// <summary>The 0x4000 map cells, each its status byte and the records the status bits add (1 + 52 / 31 / 10 bytes).</summary>
	public byte[][] Cells { get; set; } = Array.Empty<byte[]>();
	public List<SaveThingEntry> Things { get; set; } = new();
}

/// <summary>The Scripts block (<c>0x475650</c>): <c>'SCPT'</c>, 0x118, the slot table, then the 0x118-byte record of every nonzero slot from 1 on.</summary>
public sealed class SaveScriptsData
{
	public uint[] SlotWords { get; set; } = Array.Empty<uint>();
	public List<byte[]> Records { get; set; } = new();
}

/// <summary>The Particles block (<c>0x51F680</c>): <c>'PCLT'</c>, the on flag and, when on, the two counted engine tables and the counted tail (always empty in a save the game writes).</summary>
public sealed class SaveParticlesData
{
	public bool On { get; set; }
	public byte[] Globals { get; set; } = Array.Empty<byte>();
	public byte[] Pool { get; set; } = Array.Empty<byte>();
	public byte[] Tail { get; set; } = Array.Empty<byte>();
}

/// <summary>The message centre (<c>0x40FC80</c>): a word and 29 counted lists of <c>u16</c> message ids.</summary>
public sealed class SaveMessageCentreData
{
	public uint Word { get; set; }
	public List<ushort[]> Lists { get; set; } = new();
}

/// <summary>The flying-rides block (<c>0x55DE70</c>): <c>'FLY_'</c>, the on word and, when on, the counted slot array, ten words and one body for each nonzero slot 1 to 98 the last word sizes.</summary>
public sealed class SaveFlyingRidesData
{
	public bool On { get; set; }
	public byte[] Array { get; set; } = System.Array.Empty<byte>();
	public uint[] Words { get; set; } = new uint[10];
	public List<byte[]> Bodies { get; set; } = new();
}

/// <summary>One script of the RSSE block: the 0xF4-byte struct, eight counted buffers (the seventh in 32-byte entries), the folder and the <c>'OBJ '</c> list of 0x1C-byte entries.</summary>
public sealed class SaveRsseRecordData
{
	public byte[] Struct { get; set; } = new byte[SaveRsseBlock.RecordBytes];
	public byte[][] Buffers { get; set; } = new byte[SaveRsseBlock.BufferCount][];
	public byte[] Folder { get; set; } = Array.Empty<byte>();
	public List<byte[]> Objects { get; set; } = new();
}

/// <summary>The RSSE block (<c>0x559350</c>): <c>'RSSE'</c>, <c>u32 0x14</c> and the 20-byte header, five <c>'PAD_'</c> words, the record count and size and the records.</summary>
public sealed class SaveRsseData
{
	public byte[] Header { get; set; } = new byte[SaveRsseBlock.HeaderBytes];
	public uint[] Padding { get; set; } = new uint[SaveRsseBlock.PadWords];
	public uint RecordSize { get; set; } = SaveRsseBlock.RecordBytes;
	public List<SaveRsseRecordData> Records { get; set; } = new();
}

/// <summary>The coasters block (<c>0x437300</c>): the controller count and three globals, then each coaster's chunk (carried verbatim; <see cref="SaveCoasterBlock"/> decodes it).</summary>
public sealed class SaveCoastersData
{
	public uint Count { get; set; }
	public uint[] Globals { get; set; } = new uint[3];
	public byte[] Tail { get; set; } = Array.Empty<byte>();
}

/// <summary>The sound block (<c>0x51C350</c>): the on word, a word, the counted settings and, when on, three words and a chunk the loader skips by its length word.</summary>
public sealed class SaveSoundData
{
	public bool On { get; set; }
	public uint Word { get; set; }
	public byte[] Settings { get; set; } = Array.Empty<byte>();
	public byte[] ChunkWords { get; set; } = Array.Empty<byte>();
	public byte[] Chunk { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// A decoded original save payload (the 19 blocks of docs/TPWS-PAYLOAD.md) that can be written again, byte for byte. Every block model holds exactly the fields its original
/// loader reads, in the loader's order, so <see cref="Write"/> emits what the original writer emits and a read followed by a write returns the input.
/// <para>
/// Fields the OpenTPW readers do not decode stay raw runs (the engine dumps the original copies as one block, the thing bodies, the map cells' records, the RideSystem frame, the
/// track-ride chunk, the coaster chunks); each is documented at its member and in docs/TPWS-PAYLOAD.md. The writer cannot invent such a run: it is carried from a source
/// save or built by <see cref="OriginalSaveDefaults"/>.
/// </para>
/// </summary>
public sealed class OriginalSavePayload
{
	public SaveActionRecordingData ActionRecording { get; set; } = new();
	public OriginalSaveWorldData World { get; set; } = new();
	public SaveScriptsData Scripts { get; set; } = new();
	public SaveParticlesData Particles { get; set; } = new();
	public SaveMessageCentreData MessageCentre { get; set; } = new();
	/// <summary>The clock block (<c>0x402E00</c>): two words (the scaled clock and the advisor clock, in milliseconds).</summary>
	public uint[] Clock { get; set; } = new uint[2];
	/// <summary>The vanilla-time word (<c>0x403220</c>).</summary>
	public uint VanillaTime { get; set; }
	/// <summary>The nine game-system words (<c>0x550520</c>).</summary>
	public uint[] GameSystem { get; set; } = new uint[SaveBlockWidths.GameSystemBytes / 4];
	/// <summary>The RideSystem block's bytes after its length word (<c>0x464140</c>); the scene-record frame is decoded by <see cref="SaveRideSystemBlock"/>.</summary>
	public byte[] RideSystem { get; set; } = Array.Empty<byte>();
	/// <summary>The track-rides chunk (<c>0x5428E0</c>): a container whose first word is its type, the second a word and the third its whole size; the bytes after those 12.</summary>
	public uint TrackRidesType { get; set; } = 1;
	public uint TrackRidesWord { get; set; }
	public byte[] TrackRides { get; set; } = Array.Empty<byte>();
	public SaveFlyingRidesData FlyingRides { get; set; } = new();
	public SaveRsseData Rsse { get; set; } = new();
	/// <summary>The ten camera words (<c>0x42CDC0</c>: zoom, yaw, flags, the two points of interest, the saved yaw).</summary>
	public uint[] Camera { get; set; } = new uint[10];
	public SaveCoastersData Coasters { get; set; } = new();
	/// <summary>The advisor block (<c>0x599C30</c>): <c>'ADV_'</c> and two counted buffers.</summary>
	public byte[] AdvisorFirst { get; set; } = Array.Empty<byte>();
	public byte[] AdvisorSecond { get; set; } = Array.Empty<byte>();
	public SaveSoundData Sound { get; set; } = new();
	/// <summary>The two cheat bytes (<c>0x4053B0</c>).</summary>
	public byte[] Cheat { get; set; } = new byte[SaveBlockWidths.CheatBytes];
	/// <summary>The advisor-scoring word (<c>0x59BEA0</c>).</summary>
	public uint AdvisorScoring { get; set; }
	/// <summary>The UI block's window records (<c>0x4816A0</c>: <c>u32</c> count, then 0x21C bytes each); the bytes after the count.</summary>
	public byte[] Ui { get; set; } = Array.Empty<byte>();

	/// <summary>Reads <paramref name="payload"/> (a Theme Park World payload) into block models; throws <see cref="InvalidDataException"/> when a block does not read as the original's.</summary>
	public static OriginalSavePayload Read( ReadOnlySpan<byte> payload )
	{
		var layout = SavePayloadLayout.Parse( payload );
		var result = new OriginalSavePayload();
		foreach ( var block in layout.Blocks )
		{
			var bytes = payload.Slice( block.Offset, block.Length );
			var reader = new BlockReader( bytes, block.Block.ToString() );
			switch ( block.Block )
			{
				case SaveBlock.ActionRecording:
					result.ActionRecording = new SaveActionRecordingData { Published = reader.U32() != 0, Recording = reader.Counted() };
					break;
				case SaveBlock.World:
					result.World = ReadWorld( bytes );
					reader.Skip( bytes.Length );
					break;
				case SaveBlock.Scripts:
					result.Scripts = ReadScripts( ref reader );
					break;
				case SaveBlock.Particles:
					result.Particles = ReadParticles( ref reader );
					break;
				case SaveBlock.MessageCentre:
					result.MessageCentre = ReadMessageCentre( ref reader );
					break;
				case SaveBlock.Clock:
					result.Clock = reader.Words( 2 );
					break;
				case SaveBlock.VanillaTime:
					result.VanillaTime = reader.U32();
					break;
				case SaveBlock.GameSystem:
					result.GameSystem = reader.Words( SaveBlockWidths.GameSystemBytes / 4 );
					break;
				case SaveBlock.RideSystem:
					result.RideSystem = reader.Counted();
					break;
				case SaveBlock.TrackRides:
					result.TrackRidesType = reader.U32();
					result.TrackRidesWord = reader.U32();
					var size = reader.U32();
					if ( size < SaveBlockWidths.TrackChunkHeaderBytes )
						throw new InvalidDataException( "Save TrackRides chunk is smaller than its header." );
					result.TrackRides = reader.Take( (int)size - SaveBlockWidths.TrackChunkHeaderBytes );
					break;
				case SaveBlock.FlyingRides:
					result.FlyingRides = ReadFlyingRides( ref reader );
					break;
				case SaveBlock.Rsse:
					result.Rsse = ReadRsse( ref reader );
					break;
				case SaveBlock.Camera:
					result.Camera = reader.Words( 10 );
					break;
				case SaveBlock.Coasters:
					result.Coasters = new SaveCoastersData { Count = reader.U32(), Globals = reader.Words( 3 ), Tail = reader.Take( reader.Remaining ) };
					break;
				case SaveBlock.Advisor:
					if ( reader.U32() != SaveBlockWidths.AdvisorMagic )
						throw new InvalidDataException( "Save Advisor block does not start with 'ADV_'." );
					result.AdvisorFirst = reader.Counted();
					result.AdvisorSecond = reader.Counted();
					break;
				case SaveBlock.Sound:
					result.Sound = ReadSound( ref reader );
					break;
				case SaveBlock.Cheat:
					result.Cheat = reader.Take( SaveBlockWidths.CheatBytes );
					break;
				case SaveBlock.AdvisorScoring:
					result.AdvisorScoring = reader.U32();
					break;
				case SaveBlock.UI:
					var records = reader.U32();
					result.Ui = reader.Take( checked((int)(records * (long)SaveBlockWidths.UiRecordBytes)) );
					break;
			}
			if ( reader.Remaining != 0 )
				throw new InvalidDataException( $"Save {block.Block} block holds {reader.Remaining} bytes its loader does not read." );
		}
		return result;
	}

	/// <summary>Writes the payload in the original writer's order (<c>0x4164C0</c>): each block followed by its byte-reversed delimiter, the first and last without one.</summary>
	public byte[] Write()
	{
		var writer = new SaveByteWriter();
		WriteActionRecording( writer );
		WriteWorld( writer );
		writer.Tag( "WRLD" );
		WriteScripts( writer );
		writer.Tag( "SPSC" );
		WriteParticles( writer );
		writer.Tag( "PART" );
		WriteMessageCentre( writer );
		writer.Tag( "MESS" );
		writer.U32( Clock[0] );
		writer.U32( Clock[1] );
		writer.Tag( "CLOK" );
		writer.U32( VanillaTime );
		writer.Tag( "VANT" );
		foreach ( var word in GameSystem )
			writer.U32( word );
		writer.Tag( "GSYS" );
		writer.U32( (uint)RideSystem.Length );
		writer.Bytes( RideSystem );
		writer.Tag( "RSYS" );
		writer.U32( TrackRidesType );
		writer.U32( TrackRidesWord );
		writer.U32( (uint)(SaveBlockWidths.TrackChunkHeaderBytes + TrackRides.Length) );
		writer.Bytes( TrackRides );
		writer.Tag( "TRAK" );
		WriteFlyingRides( writer );
		writer.Tag( "FLYR" );
		WriteRsse( writer );
		writer.Tag( "RSSE" );
		foreach ( var word in Camera )
			writer.U32( word );
		writer.Tag( "KAME" );
		writer.U32( Coasters.Count );
		foreach ( var word in Coasters.Globals )
			writer.U32( word );
		writer.Bytes( Coasters.Tail );
		writer.Tag( "COAS" );
		writer.U32( SaveBlockWidths.AdvisorMagic );
		writer.U32( (uint)AdvisorFirst.Length );
		writer.Bytes( AdvisorFirst );
		writer.U32( (uint)AdvisorSecond.Length );
		writer.Bytes( AdvisorSecond );
		writer.Tag( "ADVS" );
		WriteSound( writer );
		writer.Tag( "SOUN" );
		writer.Bytes( Cheat );
		writer.Tag( "CHTS" );
		writer.U32( AdvisorScoring );
		writer.Tag( "ADSC" );
		writer.U32( (uint)(Ui.Length / SaveBlockWidths.UiRecordBytes) );
		writer.Bytes( Ui );
		return writer.ToArray();
	}

	// [BIN:TPW-EXE:0x403780 action recording writer] u32 mLoadedPublishedPark (4 bytes from this + 8), u32 recording size (virtual size call), the recording bytes
	private void WriteActionRecording( SaveByteWriter writer )
	{
		writer.U32( ActionRecording.Published ? 1u : 0u );
		writer.U32( (uint)ActionRecording.Recording.Length );
		writer.Bytes( ActionRecording.Recording );
	}

	// [BIN:TPW-EXE:0x516C80 World writer] the 26 labelled variables (version .. mFirstObject), then 0x4D3AA0, 0x4D7A70 (0x507850, 0x4F7F30, 0x4CF050), 0x4D7EA0, "Used Thing Head" and per thing "Used Thing Next", "thingmodel" and the model's serializer
	private void WriteWorld( SaveByteWriter writer )
	{
		WriteVariables( writer, World.Variables );
		writer.Bytes( World.Controls.Records );
		writer.U32( World.Controls.Count );
		writer.U16( World.Controls.PreviousSearchKey );
		WriteStaffPool( writer, World.StaffPool );
		writer.I64( World.Calendar.FunnyTimeStart );
		writer.I64( World.Calendar.SessionStart );
		writer.I32( World.Calendar.MonthAtLastUpdate );
		writer.I32( World.Calendar.DayAtLastUpdate );
		writer.I32( World.Calendar.FunnySecsPerRealSec );
		writer.Bytes( World.Arrivals );
		foreach ( var cell in World.Cells )
			writer.Bytes( cell );
		if ( World.Things.Count == 0 )
			throw new InvalidOperationException( "A World holds at least one thing (the loader reads the head and then things until the next id is 0)." );
		writer.U32( World.Things[0].Id );
		for ( var index = 0; index < World.Things.Count; index++ )
		{
			var thing = World.Things[index];
			if ( index + 1 < World.Things.Count ? World.Things[index + 1].Id != thing.Next : thing.Next != 0 )
				throw new InvalidOperationException( $"The thing chain breaks after thing {thing.Id}: its next id {thing.Next} is not the next thing's id." );
			writer.U32( thing.Next );
			writer.U32( (uint)thing.Model );
			writer.Bytes( thing.Body );
		}
	}

	private static void WriteVariables( SaveByteWriter writer, SaveWorldVariables variables )
	{
		// labels at 0x748744 .. 0x76286C: version, mArrivalVehicle_Size1..3, mBankAccount, mCurrentArrivalVehicle, mGameTick, mMechanicHQ, mParkAnalyser, mParkClosed,
		// mNumberOfVisitorsToDate, mParkGates, mTrafficLights, mRandomSeed, mResearchLab, mStaffHQ, mTagSystem, mUIMsgReceiver, mWeather, mWorldState, mFirstHandyman .. mFirstObject
		writer.U32( variables.Version );
		writer.U16( variables.ArrivalVehicleSize1 );
		writer.U16( variables.ArrivalVehicleSize2 );
		writer.U16( variables.ArrivalVehicleSize3 );
		writer.U16( variables.BankAccount );
		writer.U16( variables.CurrentArrivalVehicle );
		writer.U32( variables.GameTick );
		writer.U16( variables.MechanicHq );
		writer.U16( variables.ParkAnalyser );
		writer.U32( variables.ParkClosed );
		writer.U32( variables.NumberOfVisitorsToDate );
		writer.U16( variables.ParkGates );
		writer.U16( variables.TrafficLights );
		writer.U32( variables.RandomSeed );
		writer.U16( variables.ResearchLab );
		writer.U16( variables.StaffHq );
		writer.U16( variables.TagSystem );
		writer.U16( variables.UIMsgReceiver );
		writer.U16( variables.Weather );
		writer.U32( variables.WorldState );
		writer.U16( variables.FirstHandyman );
		writer.U16( variables.FirstMechanic );
		writer.U16( variables.FirstEntertainer );
		writer.U16( variables.FirstGuard );
		writer.U16( variables.FirstResearcher );
		writer.U16( variables.FirstObject );
	}

	// [BIN:TPW-EXE:0x507850 staff pool serializer] 32 × (u32 type, u32 name, u8 grade, u8 sub-type, u8 valid, u8 on pointer, u32 time stamp, u32 timeout), 5 × (u32 mPeopleInCat, u8 mStopProducing), u32 mTimeSig, u8 mOpeningStaffPoolGenerated
	private static void WriteStaffPool( SaveByteWriter writer, SaveStaffPool pool )
	{
		if ( pool.Candidates.Count != SaveStaffPool.Slots || pool.PeopleInCategory.Count != SaveStaffPool.Roles || pool.StopProducing.Count != SaveStaffPool.Roles )
			throw new InvalidOperationException( "A staff pool holds 32 candidates and 5 role counters." );
		foreach ( var candidate in pool.Candidates )
		{
			writer.U32( candidate.Type );
			writer.U32( candidate.Name );
			writer.U8( candidate.Grade );
			writer.U8( candidate.SubType );
			writer.U8( candidate.Valid ? (byte)1 : (byte)0 );
			writer.U8( candidate.OnPointer ? (byte)1 : (byte)0 );
			writer.U32( candidate.TimeSig );
			writer.U32( candidate.Timeout );
		}
		for ( var role = 0; role < SaveStaffPool.Roles; role++ )
		{
			writer.U32( pool.PeopleInCategory[role] );
			writer.U8( pool.StopProducing[role] ? (byte)1 : (byte)0 );
		}
		writer.U32( pool.TimeSig );
		writer.U8( pool.OpeningPoolGenerated ? (byte)1 : (byte)0 );
	}

	// [BIN:TPW-EXE:0x475650 Scripts writer] 'SCPT', 0x118, the slot count, the slot words, then the 0x118-byte record of each nonzero slot from 1 on
	private void WriteScripts( SaveByteWriter writer )
	{
		writer.U32( SaveBlockWidths.ScriptsMagic );
		writer.U32( SaveBlockWidths.ScriptRecordBytes );
		writer.U32( (uint)Scripts.SlotWords.Length );
		foreach ( var word in Scripts.SlotWords )
			writer.U32( word );
		var used = 0;
		for ( var slot = 1; slot < Scripts.SlotWords.Length; slot++ )
		{
			if ( Scripts.SlotWords[slot] == 0 )
				continue;
			if ( used >= Scripts.Records.Count )
				throw new InvalidOperationException( "A nonzero script slot has no record." );
			writer.Bytes( Scripts.Records[used++] );
		}
		if ( used != Scripts.Records.Count )
			throw new InvalidOperationException( "More script records than nonzero slots." );
	}

	// [BIN:TPW-EXE:0x51F680 Particles writer] 'PCLT', the on flag and, when on, the counted globals table (0x9E68), the counted pool table (0x8B60) and the counted tail (the call 0x41661B passes 0)
	private void WriteParticles( SaveByteWriter writer )
	{
		writer.U32( SaveBlockWidths.ParticlesMagic );
		writer.U32( Particles.On ? 1u : 0u );
		if ( !Particles.On )
			return;
		writer.U32( (uint)Particles.Globals.Length );
		writer.Bytes( Particles.Globals );
		writer.U32( (uint)Particles.Pool.Length );
		writer.Bytes( Particles.Pool );
		writer.U32( (uint)Particles.Tail.Length );
		writer.Bytes( Particles.Tail );
	}

	// [BIN:TPW-EXE:0x40FC80 message centre writer] a word and 29 lists (u32 count, count × u16)
	private void WriteMessageCentre( SaveByteWriter writer )
	{
		if ( MessageCentre.Lists.Count != SaveBlockWidths.MessageLists )
			throw new InvalidOperationException( "The message centre holds 29 lists." );
		writer.U32( MessageCentre.Word );
		foreach ( var list in MessageCentre.Lists )
		{
			writer.U32( (uint)list.Length );
			foreach ( var message in list )
				writer.U16( message );
		}
	}

	// [BIN:TPW-EXE:0x55DE70 flying rides writer] 'FLY_', the on word and, when on, the counted slot array, ten words and the last word's byte count for every nonzero slot 1 to 98
	private void WriteFlyingRides( SaveByteWriter writer )
	{
		writer.U32( SaveBlockWidths.FlyingMagic );
		writer.U32( FlyingRides.On ? 1u : 0u );
		if ( !FlyingRides.On )
			return;
		writer.U32( (uint)FlyingRides.Array.Length );
		writer.Bytes( FlyingRides.Array );
		foreach ( var word in FlyingRides.Words )
			writer.U32( word );
		foreach ( var body in FlyingRides.Bodies )
			writer.Bytes( body );
	}

	// [BIN:TPW-EXE:0x559350 RSSE writer] 'RSSE', 0x14 and the header, five padding words, the record count and size, then per record the struct, eight counted buffers, the folder and the 'OBJ ' list
	private void WriteRsse( SaveByteWriter writer )
	{
		writer.U32( SaveRsseBlock.Magic );
		writer.U32( SaveRsseBlock.HeaderBytes );
		writer.Bytes( Rsse.Header );
		foreach ( var word in Rsse.Padding )
			writer.U32( word );
		writer.U32( (uint)Rsse.Records.Count );
		writer.U32( Rsse.RecordSize );
		foreach ( var record in Rsse.Records )
		{
			writer.Bytes( record.Struct );
			for ( var buffer = 0; buffer < SaveRsseBlock.BufferCount; buffer++ )
			{
				var length = buffer == 6 ? record.Buffers[buffer].Length / SaveRsseRecord.WalkEntryBytes : record.Buffers[buffer].Length;
				writer.U32( (uint)length );
				writer.Bytes( record.Buffers[buffer] );
			}
			writer.U32( (uint)record.Folder.Length );
			writer.Bytes( record.Folder );
			writer.U32( SaveRsseBlock.ObjectsMagic );
			writer.U32( (uint)record.Objects.Count );
			writer.U32( SaveRsseObject.Bytes );
			foreach ( var entry in record.Objects )
				writer.Bytes( entry );
		}
	}

	// [BIN:TPW-EXE:0x51C350 sound writer] the on word, a word, the counted settings and, when on, three words and the chunk (its length word first)
	private void WriteSound( SaveByteWriter writer )
	{
		writer.U32( Sound.On ? 1u : 0u );
		writer.U32( Sound.Word );
		writer.U32( (uint)Sound.Settings.Length );
		writer.Bytes( Sound.Settings );
		if ( !Sound.On )
			return;
		writer.Bytes( Sound.ChunkWords );
		writer.U32( (uint)(Sound.Chunk.Length + 4) );
		writer.Bytes( Sound.Chunk );
	}

	private static OriginalSaveWorldData ReadWorld( ReadOnlySpan<byte> bytes )
	{
		var fields = new SaveFieldReader( bytes, 0, "World variables" );
		var variables = SaveWorldVariables.Read( ref fields );
		var world = new OriginalSaveWorldData { Variables = variables };
		var reader = new BlockReader( bytes[fields.Position..], "World" );
		world.Controls = new SaveObjectControlTable
		{
			Records = reader.Take( SaveObjectControls.Slots * SaveObjectControl.Bytes ), Count = reader.U32(), PreviousSearchKey = reader.U16(),
		};
		var pool = SaveStaffPool.TryRead( reader.Rest, 0, SaveStaffPool.Bytes ) ?? throw new InvalidDataException( "Save World staff pool does not read as one." );
		reader.Skip( SaveStaffPool.Bytes );
		world.StaffPool = pool;
		world.Calendar = new SaveCalendar( 0, reader.I64(), reader.I64(), reader.I32(), reader.I32(), reader.I32() );
		world.Arrivals = reader.Take( SaveWorld.ArrivalBytes );
		var cells = new byte[SaveBlockWidths.WorldCells][];
		for ( var cell = 0; cell < cells.Length; cell++ )
		{
			var status = reader.Peek();
			cells[cell] = reader.Take( SaveBlockWidths.CellStatusBytes + ((status & 1) != 0 ? SaveBlockWidths.MapCellBytes : 0)
				+ ((status & 2) != 0 ? SaveBlockWidths.TrackCellBytes : 0) + ((status & 4) != 0 ? SaveBlockWidths.EffectsCellBytes : 0) );
		}
		world.Cells = cells;
		var chainStart = bytes.Length - reader.Remaining;
		var (_, things) = SaveThingChain.Walk( bytes, chainStart, bytes.Length );
		foreach ( var thing in things )
			world.Things.Add( new SaveThingEntry { Id = (uint)thing.Id, Next = (uint)thing.NextId, Model = thing.Model, Body = bytes.Slice( thing.BodyOffset, thing.BodyLength ).ToArray() } );
		reader.Skip( reader.Remaining );
		return world;
	}

	private static SaveScriptsData ReadScripts( ref BlockReader reader )
	{
		if ( reader.U32() != SaveBlockWidths.ScriptsMagic || reader.U32() != SaveBlockWidths.ScriptRecordBytes )
			throw new InvalidDataException( "Save Scripts block does not start with 'SCPT' and 0x118." );
		var data = new SaveScriptsData { SlotWords = reader.Words( checked((int)reader.U32()) ) };
		for ( var slot = 1; slot < data.SlotWords.Length; slot++ )
			if ( data.SlotWords[slot] != 0 )
				data.Records.Add( reader.Take( SaveBlockWidths.ScriptRecordBytes ) );
		return data;
	}

	private static SaveParticlesData ReadParticles( ref BlockReader reader )
	{
		if ( reader.U32() != SaveBlockWidths.ParticlesMagic )
			throw new InvalidDataException( "Save Particles block does not start with 'PCLT'." );
		var data = new SaveParticlesData { On = reader.U32() != 0 };
		if ( !data.On )
			return data;
		data.Globals = reader.Counted();
		data.Pool = reader.Counted();
		data.Tail = reader.Counted();
		return data;
	}

	private static SaveMessageCentreData ReadMessageCentre( ref BlockReader reader )
	{
		var data = new SaveMessageCentreData { Word = reader.U32() };
		for ( var list = 0; list < SaveBlockWidths.MessageLists; list++ )
		{
			var count = reader.U32();
			var messages = new ushort[count];
			var bytes = reader.Take( checked((int)(count * 2L)) );
			for ( var index = 0; index < messages.Length; index++ )
				messages[index] = BinaryPrimitives.ReadUInt16LittleEndian( bytes.AsSpan( index * 2 ) );
			data.Lists.Add( messages );
		}
		return data;
	}

	private static SaveFlyingRidesData ReadFlyingRides( ref BlockReader reader )
	{
		if ( reader.U32() != SaveBlockWidths.FlyingMagic )
			throw new InvalidDataException( "Save FlyingRides block does not start with 'FLY_'." );
		var data = new SaveFlyingRidesData { On = reader.U32() != 0 };
		if ( !data.On )
			return data;
		data.Array = reader.Counted();
		data.Words = reader.Words( 10 );
		var last = data.Words[9];
		for ( var slot = 1; slot <= SaveBlockWidths.FlyingSlots && slot * 4L + 4 <= data.Array.Length; slot++ )
			if ( BinaryPrimitives.ReadUInt32LittleEndian( data.Array.AsSpan( slot * 4 ) ) != 0 )
				data.Bodies.Add( reader.Take( checked((int)last) ) );
		return data;
	}

	private static SaveRsseData ReadRsse( ref BlockReader reader )
	{
		if ( reader.U32() != SaveRsseBlock.Magic )
			throw new InvalidDataException( "Save RSSE block does not start with 'RSSE'." );
		if ( reader.U32() != SaveRsseBlock.HeaderBytes )
			throw new InvalidDataException( "Save RSSE header length is not 0x14." );
		var data = new SaveRsseData { Header = reader.Take( SaveRsseBlock.HeaderBytes ), Padding = reader.Words( SaveRsseBlock.PadWords ) };
		var count = reader.U32();
		data.RecordSize = reader.U32();
		for ( uint index = 0; index < count; index++ )
		{
			var record = new SaveRsseRecordData { Struct = reader.Take( SaveRsseBlock.RecordBytes ) };
			for ( var buffer = 0; buffer < SaveRsseBlock.BufferCount; buffer++ )
			{
				var length = reader.U32();
				record.Buffers[buffer] = reader.Take( checked((int)(length * (buffer == 6 ? (long)SaveRsseRecord.WalkEntryBytes : 1L))) );
			}
			record.Folder = reader.Counted();
			if ( reader.U32() != SaveRsseBlock.ObjectsMagic )
				throw new InvalidDataException( "Save RSSE record has no 'OBJ ' list." );
			var objects = reader.U32();
			var objectBytes = reader.U32();
			if ( objects > 0 && objectBytes != SaveRsseObject.Bytes )
				throw new InvalidDataException( "Save RSSE object entries are not 0x1C bytes." );
			for ( uint entry = 0; entry < objects; entry++ )
				record.Objects.Add( reader.Take( SaveRsseObject.Bytes ) );
			data.Records.Add( record );
		}
		return data;
	}

	private static SaveSoundData ReadSound( ref BlockReader reader )
	{
		var data = new SaveSoundData { On = reader.U32() != 0, Word = reader.U32(), Settings = reader.Counted() };
		if ( !data.On )
			return data;
		data.ChunkWords = reader.Take( 12 );
		var length = reader.U32();
		if ( length < 4 )
			throw new InvalidDataException( "Save Sound chunk length is below its own word." );
		data.Chunk = reader.Take( checked((int)length - 4) );
		return data;
	}

	/// <summary>A cursor over one block's bytes.</summary>
	private ref struct BlockReader
	{
		private readonly ReadOnlySpan<byte> data;
		private readonly string block;
		private int position;

		public BlockReader( ReadOnlySpan<byte> data, string block )
		{
			this.data = data;
			this.block = block;
			position = 0;
		}

		public readonly int Remaining => data.Length - position;
		public readonly ReadOnlySpan<byte> Rest => data[position..];
		public readonly byte Peek() => position < data.Length ? data[position] : throw new InvalidDataException( $"Save {block} block is truncated." );

		public void Skip( int count ) => Take( count );

		public byte[] Take( int count )
		{
			if ( count < 0 || count > data.Length - position )
				throw new InvalidDataException( $"Save {block} block is truncated at offset {position} (needs {count} more bytes, {data.Length - position} remain)." );
			var result = data.Slice( position, count ).ToArray();
			position += count;
			return result;
		}

		public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian( Take( 4 ) );
		public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian( Take( 2 ) );
		public int I32() => BinaryPrimitives.ReadInt32LittleEndian( Take( 4 ) );
		public long I64() => BinaryPrimitives.ReadInt64LittleEndian( Take( 8 ) );

		public uint[] Words( int count )
		{
			var words = new uint[count];
			for ( var index = 0; index < count; index++ )
				words[index] = U32();
			return words;
		}

		/// <summary>A <c>u32</c> byte count and that many bytes.</summary>
		public byte[] Counted() => Take( checked((int)U32()) );
	}
}
