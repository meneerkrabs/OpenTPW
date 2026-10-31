using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenTPW.Tests;

/// <summary>
/// Builds synthetic decoded save payloads in the original writer's layout (docs/reverse/SAVE-plan.md): action
/// recording, World block (variables, opaque macro AI, calendar, arrivals, cell map, thing list), the 17
/// delimiters and the UI block. Thing bodies follow the serializer widths the readers use.
/// </summary>
internal static class SaveTestPayloads
{
	public sealed record AttractionFields
	{
		public ushort PositionX { get; init; } = 51 * 256 + 128;
		public ushort PositionY { get; init; } = 23 * 256 + 128;
		public uint Angle { get; init; } = 90;
		public ushort InfoId { get; init; } = 1100;
		/// <summary>The eight words <c>tv[t]</c>: year, month, day, day of the week (2000-01-01 was a Saturday, 6), hour, minute, second and milliseconds.</summary>
		public uint[] Stamp { get; init; } = { 2000, 1, 1, 6, 15, 37, 30, 0 };
		public uint MeshInstance { get; init; } = 110;
		public string Line1 { get; init; } = "Belly";
		public string Line2 { get; init; } = "Bounce";
		/// <summary><c>mNext</c> (+0xC): the next attraction thing of the object chain, 0 at its end.</summary>
		public ushort Next { get; init; }
		public uint State { get; init; }
		public ushort AssignedStaff { get; init; }
		public uint CanLoad { get; init; } = 1;
		public uint RequestedService { get; init; }
		public uint MarkedTurn { get; init; }
		/// <summary>The running value of every ring (mTemp).</summary>
		public int RingToday { get; init; }
		/// <summary>Every saved slot of every ring.</summary>
		public int RingEntry { get; init; }
		/// <summary>The slot every ring last wrote (mCurrentEntry); -1 is a ring nothing has been written to.</summary>
		public int RingHead { get; init; } = 1;
		public uint RingCount { get; init; } = 30;
		public byte RingFlag { get; init; } = 1;
		public uint Customers { get; init; } = 7;
		public uint WalkAways { get; init; } = 2;
		public byte Capacity { get; init; } = 5;
		public byte Duration { get; init; } = 30;
		public uint Speed { get; init; } = 60;
		public int CostOfGoods { get; init; } = 12;
		public int Quality { get; init; } = 50;
		public int Chance { get; init; } = 100;
		public uint Price { get; init; } = 20;
		public int IngredientAmount { get; init; } = 50;
		public uint QueueCells { get; init; } = 4;
		public float Gauge48 { get; init; } = 100f;
		public float LifeGauge { get; init; } = 97f;
		public float Repair { get; init; } = 64f;
		public uint Costs { get; init; } = 150;
		public uint Takings { get; init; } = 900;
		public byte Level { get; init; } = 1;
	}

	/// <summary>An attraction body in the serializer's order (0xdaf04).</summary>
	public static byte[] Attraction( AttractionFields fields )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		writer.Write( fields.PositionX );
		writer.Write( fields.PositionY );
		writer.Write( new byte[4] );
		writer.Write( fields.Angle );
		writer.Write( fields.InfoId );
		foreach ( var value in fields.Stamp )
			writer.Write( value );
		writer.Write( fields.MeshInstance );
		writer.Write( (ushort)0 );
		for ( var index = 0; index < SaveAttraction.NameCharacters; index++ )
		{
			writer.Write( (ushort)(index < fields.Line1.Length ? fields.Line1[index] : 0) );
			writer.Write( (ushort)(index < fields.Line2.Length ? fields.Line2[index] : 0) );
		}
		writer.Write( new byte[8] );
		writer.Write( fields.State );
		writer.Write( new byte[4] );
		writer.Write( fields.Next );
		writer.Write( fields.AssignedStaff );
		writer.Write( (ushort)0 );
		writer.Write( fields.CanLoad );
		writer.Write( new byte[10] );
		void Ring()
		{
			writer.Write( fields.RingHead );
			writer.Write( fields.RingCount );
			writer.Write( fields.RingFlag );
			writer.Write( fields.RingToday );
			for ( var slot = 0; slot < (int)Math.Min( fields.RingCount, 64 ); slot++ )
				writer.Write( fields.RingEntry );
		}
		Ring();
		Ring();
		writer.Write( fields.Customers );
		Ring();
		writer.Write( fields.WalkAways );
		Ring();
		Ring();
		Ring();
		writer.Write( fields.Capacity );
		writer.Write( fields.Duration );
		writer.Write( fields.Speed );
		writer.Write( (ushort)0 );
		writer.Write( fields.CostOfGoods );
		writer.Write( fields.Quality );
		writer.Write( fields.Chance );
		writer.Write( fields.Price );
		writer.Write( fields.IngredientAmount );
		writer.Write( fields.QueueCells );
		writer.Write( fields.Gauge48 );
		writer.Write( fields.LifeGauge );
		writer.Write( fields.Repair );
		writer.Write( fields.RequestedService );
		writer.Write( fields.MarkedTurn );
		writer.Write( fields.Costs );
		writer.Write( fields.Takings );
		writer.Write( 0u );
		writer.Write( fields.Level );
		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>Bank body: base, seven words, eight loan offers (offer i: <paramref name="loans"/> or the easy defaults).</summary>
	public static byte[] Bank( int balance = 87_987, int fee = 25, int profit = -12_013, uint withdrawals = 1, Func<int, int[]>? loans = null )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		writer.Write( new ushort[] { 128, 128, 7, 9 }.SelectMany( BitConverter.GetBytes ).ToArray() );
		writer.Write( fee );
		writer.Write( balance );
		writer.Write( 0 );
		writer.Write( withdrawals );
		writer.Write( balance - 200 );
		writer.Write( 0 );
		writer.Write( profit );
		for ( var index = 0; index < SaveBank.LoanRecords; index++ )
			foreach ( var word in loans?.Invoke( index ) ?? new[] { index == 3 ? 1 : 0, 10_000 * (index + 1), 0, 36, 10_000 * (index + 1) / 36, 0, 0, index } )
				writer.Write( word );
		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>Challenge system body: <paramref name="used"/> leading slots (type, time, value, prize), the rest 0xCD with type 0.</summary>
	public static byte[] ChallengeSystem( IReadOnlyList<(int Type, int Time, int Value, int Prize)> used, bool gap = false )
	{
		var body = new byte[SaveChallengeSystem.Bytes];
		Array.Fill( body, (byte)0xCD, 8, SaveChallengeSystem.Slots * SaveChallengeSystem.SlotBytes );
		for ( var slot = 0; slot < SaveChallengeSystem.Slots; slot++ )
		{
			var offset = 8 + slot * SaveChallengeSystem.SlotBytes;
			var index = gap && slot > 0 ? slot - 1 : slot;
			if ( gap && slot == 0 || index >= used.Count )
			{
				BitConverter.GetBytes( 0 ).CopyTo( body, offset );
				continue;
			}
			var (type, time, value, prize) = used[index];
			Array.Clear( body, offset, SaveChallengeSystem.SlotBytes );
			BitConverter.GetBytes( type ).CopyTo( body, offset );
			BitConverter.GetBytes( time ).CopyTo( body, offset + 4 );
			BitConverter.GetBytes( value ).CopyTo( body, offset + 8 );
			BitConverter.GetBytes( prize ).CopyTo( body, offset + 20 );
			body[offset + 42] = 1;
		}
		BitConverter.GetBytes( -1 ).CopyTo( body, 8 + SaveChallengeSystem.Slots * SaveChallengeSystem.SlotBytes );
		return body;
	}

	public sealed record AdvisorRecordFields( ushort ThingId, uint Secondary, uint Message, int Score, byte Valid, int ResponseNo, byte Override, byte Overridden );

	/// <summary>Advisor controller body: pending records (0xCD-filled and invalid past <paramref name="pending"/>), history entries by message.</summary>
	public static byte[] AdvisorController( IReadOnlyList<AdvisorRecordFields> pending, IReadOnlyDictionary<int, (uint Tick, int Variant, byte Played, uint Slaps)> history, AdvisorRecordFields? current = null )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		writer.Write( new ushort[] { 128, 128, 2, 4 }.SelectMany( BitConverter.GetBytes ).ToArray() );
		writer.Write( 0u );
		writer.Write( 0u );
		void Record( AdvisorRecordFields record )
		{
			writer.Write( record.ThingId );
			writer.Write( record.Secondary );
			writer.Write( record.Message );
			writer.Write( record.Score );
			writer.Write( record.Valid );
			writer.Write( record.ResponseNo );
			writer.Write( record.Override );
			writer.Write( record.Overridden );
		}
		for ( var index = 0; index < SaveAdvisorController.PendingRecords; index++ )
			Record( index < pending.Count ? pending[index] : new AdvisorRecordFields( 0xCDCD, 0xCDCDCDCD, 0xCDCDCDCD, unchecked((int)0xCDCDCDCD), 0, unchecked((int)0xCDCDCDCD), 0xCD, 0xCD ) );
		writer.Write( new uint[] { 0, 114_820_087, 10_832 }.SelectMany( BitConverter.GetBytes ).ToArray() );
		for ( var message = 0; message < SaveAdvisorController.HistoryEntries; message++ )
		{
			var (tick, variant, played, slaps) = history.TryGetValue( message, out var entry ) ? entry : (0u, -1, (byte)0, 0u);
			writer.Write( tick );
			writer.Write( variant );
			writer.Write( played );
			writer.Write( new byte[] { 0xCD, 0xCD, 0xCD } );
			writer.Write( slaps );
		}
		Record( current ?? new AdvisorRecordFields( 0, 0, 346, 1000, 0, -1, 0, 0 ) );
		writer.Write( (byte)0xCD );
		writer.Write( 282u );
		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>
	/// Staff body for <paramref name="model"/>: base, person bytes (zero but for the base position and the walk's accurate destination), the staff suffix and a role
	/// suffix (<paramref name="role"/>, zero when null) ending in <paramref name="roleNext"/>.
	/// </summary>
	public static byte[] Staff( SaveThingModel model, int grade = 2, float happiness = 97, string name = "Nicholas Ricks", uint state = 1, byte percentage = 0,
		long hired = 125_935_884_000_000_000, ushort roleNext = 0, ushort patrolLow = 1, ushort patrolHigh = 0x4000, ushort restArea = 0, uint idle = 0, uint jobsDone = 0,
		byte[]? role = null, (ushort X, ushort Y) position = default, (ushort X, ushort Y)? destination = null, int navMode = 0 )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		var person = new byte[SaveThingChain.PersonBytes];
		BinaryPrimitives.WriteUInt16LittleEndian( person, position.X );
		BinaryPrimitives.WriteUInt16LittleEndian( person.AsSpan( 2 ), position.Y );
		var (destX, destY) = destination ?? position;
		BinaryPrimitives.WriteUInt16LittleEndian( person.AsSpan( 20 ), destX );
		BinaryPrimitives.WriteUInt16LittleEndian( person.AsSpan( 22 ), destY );
		BinaryPrimitives.WriteInt32LittleEndian( person.AsSpan( 35 + 48 ), navMode );
		writer.Write( person );
		writer.Write( grade );
		writer.Write( happiness );
		writer.Write( jobsDone );
		for ( var index = 0; index < 33; index++ )
			writer.Write( (ushort)(index < name.Length ? name[index] : 0) );
		writer.Write( patrolLow );
		writer.Write( patrolHigh );
		writer.Write( percentage );
		writer.Write( restArea );
		writer.Write( state );
		writer.Write( idle );
		writer.Write( hired );
		writer.Write( 93f );
		var size = SaveThingChain.FixedBodyBytes[model];
		var suffix = new byte[size - (int)stream.Length - 2];
		role?.CopyTo( suffix, 0 );
		writer.Write( suffix );
		writer.Write( roleNext );
		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>The guest fields of a synthetic guest body (tp.exe guest serializer 0x004FB530 order).</summary>
	public sealed record GuestFields
	{
		public (ushort X, ushort Y) Position { get; init; } = (5 * 256 + 128, 5 * 256 + 128);
		public (ushort X, ushort Y)? Destination { get; init; }
		public ushort BaseSpeed { get; init; } = 100;
		public uint EspSprite { get; init; }
		/// <summary>mSpriteID (person +0x20), the sprite bank number the person serializer reduces by the loaded count.</summary>
		public uint SpriteId { get; init; }
		public uint PreviousSpeed { get; init; } = 0x3F800000;
		/// <summary>The walk mask nav_mode of the navigation state (navigation +48, walk +0xB4, person +0x188).</summary>
		public int NavMode { get; init; }
		/// <summary>mStrandedTime (person +0x198), after mPreviousSpeed, mPreviousX and mPreviousY.</summary>
		public uint StrandedTime { get; init; }
		public uint BalloonScript { get; init; }
		public int Cash { get; init; } = 500;
		public int ExitLevel { get; init; } = 90;
		public float Happiness { get; init; } = 50;
		public float Hunger { get; init; } = 20;
		public float Litter { get; init; } = 5;
		public float Thirst { get; init; } = 30;
		public float Meter1B8 { get; init; }
		public float Toilet { get; init; } = 10;
		public float Illness { get; init; } = 2;
		public ushort MajorDest { get; init; }
		public uint NumRides { get; init; }
		public uint ArrivalDate { get; init; }
		public uint NumShops { get; init; }
		public uint NumSideshows { get; init; }
		public uint NumSideshowsWon { get; init; }
		public byte PersonType { get; init; } = 1;
		public byte PrankeryIndex { get; init; }
		public ushort[] PreviousRides { get; init; } = new ushort[4];
		public ushort[] PreviousTemporaryRides { get; init; } = new ushort[4];
		public uint QueueMoveDelay { get; init; }
		public int BalloonLife { get; init; }
		public uint State { get; init; } = 6;
		public uint TimeOfLastSpotAnim { get; init; }
		public uint TimeStartedIdling { get; init; }
		public uint PaidAdmission { get; init; } = 1;
		public uint ParkOpeningWaitingTime { get; init; }
		public uint ArrivalIndex { get; init; }
		public uint BeenAdmitted { get; init; }
		public uint SavedState { get; init; } = 6;
		public ushort QNext { get; init; }
		public ushort QPrev { get; init; }
		public byte QueuePos { get; init; }
		public uint LastPosX { get; init; }
		public uint LastPosY { get; init; }
		public ushort SavedMajorDest { get; init; }
		/// <summary>The event ring head (person +0xB8) and the ring's slots (type low word, value high word); the thought block ends the person block.</summary>
		public uint ActionHistIndex { get; init; }
		public uint[]? Events { get; init; }
	}

	/// <summary>A guest body (525 bytes): the person block, then the guest fields in the serializer's order.</summary>
	public static byte[] Guest( GuestFields fields )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		var person = new byte[SaveThingChain.PersonBytes];
		BinaryPrimitives.WriteUInt16LittleEndian( person, fields.Position.X );
		BinaryPrimitives.WriteUInt16LittleEndian( person.AsSpan( 2 ), fields.Position.Y );
		var (destX, destY) = fields.Destination ?? fields.Position;
		BinaryPrimitives.WriteUInt16LittleEndian( person.AsSpan( 20 ), destX );
		BinaryPrimitives.WriteUInt16LittleEndian( person.AsSpan( 22 ), destY );
		BinaryPrimitives.WriteUInt16LittleEndian( person.AsSpan( 26 ), fields.BaseSpeed );
		BinaryPrimitives.WriteUInt32LittleEndian( person.AsSpan( 29 ), fields.EspSprite );
		// the navigation block (177 bytes) follows mLastRecordedMapId at 33; mPreviousSpeed comes after it
		BinaryPrimitives.WriteInt32LittleEndian( person.AsSpan( 35 + 48 ), fields.NavMode );
		BinaryPrimitives.WriteUInt32LittleEndian( person.AsSpan( 35 + 177 ), fields.PreviousSpeed );
		BinaryPrimitives.WriteUInt32LittleEndian( person.AsSpan( 35 + 177 + 12 ), fields.StrandedTime );
		// mPurposeSpeed (u16), mSetDestSuccessfully, mSpriteAngle, then mSpriteID
		BinaryPrimitives.WriteUInt32LittleEndian( person.AsSpan( 35 + 177 + 16 + 2 + 8 ), fields.SpriteId );
		// the thought block (0x50BAD0) closes the person block: head, 32 ring slots, mLastThought, mThoughtScript, mTimeBubbleShown
		var thought = SaveThingChain.PersonBytes - (4 + 4 * 32 + 12);
		BinaryPrimitives.WriteUInt32LittleEndian( person.AsSpan( thought ), fields.ActionHistIndex );
		for ( var slot = 0; slot < (fields.Events?.Length ?? 0); slot++ )
			BinaryPrimitives.WriteUInt32LittleEndian( person.AsSpan( thought + 4 + 4 * slot ), fields.Events![slot] );
		writer.Write( person );
		writer.Write( fields.ArrivalDate ); // mArrivalDate
		writer.Write( fields.ArrivalIndex ); // mArrivalIndex (+0x1D8, the visitor number)
		writer.Write( fields.BalloonScript );
		writer.Write( fields.BeenAdmitted ); // mBeenAdmitted
		writer.Write( fields.Cash );
		writer.Write( fields.ExitLevel );
		writer.Write( fields.Happiness );
		writer.Write( fields.Hunger );
		writer.Write( fields.LastPosX );
		writer.Write( fields.LastPosY );
		writer.Write( fields.Litter );
		writer.Write( fields.MajorDest );
		writer.Write( fields.NumRides );
		writer.Write( fields.NumShops );
		writer.Write( fields.NumSideshows );
		writer.Write( fields.NumSideshowsWon );
		writer.Write( fields.PaidAdmission );
		writer.Write( fields.ParkOpeningWaitingTime ); // mParkOpeningWaitingTime (+0x200)
		writer.Write( fields.PersonType );
		writer.Write( fields.PrankeryIndex );
		for ( var slot = 0; slot < 4; slot++ )
		{
			writer.Write( fields.PreviousRides[slot] );
			writer.Write( fields.PreviousTemporaryRides[slot] );
		}
		writer.Write( fields.QNext );
		writer.Write( fields.QPrev );
		writer.Write( fields.QueueMoveDelay );
		writer.Write( fields.QueuePos );
		writer.Write( fields.BalloonLife );
		writer.Write( fields.SavedMajorDest );
		writer.Write( fields.SavedState ); // mSavedState
		writer.Write( fields.State );
		writer.Write( fields.Thirst );
		writer.Write( fields.TimeOfLastSpotAnim );
		writer.Write( fields.TimeStartedIdling );
		writer.Write( fields.Meter1B8 );
		writer.Write( fields.Toilet );
		writer.Write( fields.Illness );
		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>A zero body of a fixed-width model.</summary>
	public static byte[] Fixed( SaveThingModel model ) => new byte[SaveThingChain.FixedBodyBytes[model]];

	/// <summary>The 64 bytes of World variables, version <paramref name="version"/>; IDs default to none.</summary>
	public static byte[] Variables( uint version = 2, ushort bank = 0, uint gameTick = 755, uint parkClosed = 0, ushort researchLab = 0, ushort staffHq = 0,
		ushort firstResearcher = 0, ushort firstObject = 0, uint randomSeed = 0x3F416BBA, uint worldState = 0 )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		writer.Write( version );
		writer.Write( new ushort[] { 15, 0, 0, bank, 0 }.SelectMany( BitConverter.GetBytes ).ToArray() );
		writer.Write( gameTick );
		writer.Write( (ushort)0 );
		writer.Write( (ushort)0 );
		writer.Write( parkClosed );
		writer.Write( 0u );
		writer.Write( (ushort)0 );
		writer.Write( (ushort)0 );
		writer.Write( randomSeed );
		writer.Write( new ushort[] { researchLab, staffHq, 0, 0, 0 }.SelectMany( BitConverter.GetBytes ).ToArray() );
		writer.Write( worldState );
		writer.Write( new ushort[] { 0, 0, 0, 0, firstResearcher, firstObject }.SelectMany( BitConverter.GetBytes ).ToArray() );
		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>Calendar (FunnyTimeStart 2000-01-01, rate 15000, month cache 1, day 2) and 18 arrival bytes.</summary>
	public static byte[] CalendarAndArrivals( int rate = 15_000, int month = 1, int day = 2, long funnyTimeStart = 125_911_584_000_000_000L )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		writer.Write( funnyTimeStart );
		writer.Write( 125_850_128_932_900_000L );
		writer.Write( month );
		writer.Write( day );
		writer.Write( rate );
		writer.Write( new byte[SaveWorld.ArrivalBytes] );
		writer.Flush();
		return stream.ToArray();
	}

	/// <summary>Cell records X-fastest with attributes from <paramref name="map"/>.</summary>
	public static byte[] Grid( MapFile map, Func<int, int, byte[]>? cell = null )
	{
		using var output = new MemoryStream();
		for ( var y = 0; y < map.CellCountY; y++ )
			for ( var x = 0; x < map.CellCountX; x++ )
				output.Write( cell?.Invoke( x, y ) ?? OriginalParkImportTests.CreateCellRecord( map.GetCellAt( x, y ) ) );
		return output.ToArray();
	}

	/// <summary>Used Thing Head, then each thing as (next ID, model, body).</summary>
	public static byte[] Things( IReadOnlyList<(uint Id, uint Model, byte[] Body)> things )
	{
		using var output = new MemoryStream();
		output.Write( BitConverter.GetBytes( things.Count == 0 ? 0u : things[0].Id ) );
		for ( var index = 0; index < things.Count; index++ )
		{
			output.Write( BitConverter.GetBytes( index + 1 < things.Count ? things[index + 1].Id : 0u ) );
			output.Write( BitConverter.GetBytes( things[index].Model ) );
			output.Write( things[index].Body );
		}
		return output.ToArray();
	}

	/// <summary>World block bytes (without its delimiter).</summary>
	public static byte[] World( byte[] variables, byte[] grid, byte[] things, int opaqueBytes = 37, byte[]? calendar = null, byte[]? opaque = null ) =>
		variables.Concat( opaque ?? Enumerable.Repeat( (byte)0xCD, opaqueBytes ) ).Concat( calendar ?? CalendarAndArrivals() ).Concat( grid ).Concat( things ).ToArray();

	/// <summary>The stored (byte-reversed) delimiter of <paramref name="tag"/>.</summary>
	public static byte[] Stored( string tag ) => Encoding.ASCII.GetBytes( tag ).Reverse().ToArray();

	/// <summary>The framed RideSystem block of a park without placed records: a length word naming the 12 counting bytes after it, then used, free and first-free counts of 0 (the writer 0x464140 of an empty instance list).</summary>
	public static readonly byte[] EmptyRideSystem = new byte[] { 12, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

	/// <summary>The smallest valid body of a traced block (the readers of SaveBlockWidths): an empty script table, no particles, empty lists.</summary>
	public static byte[] MinimalBlock( SaveBlock block )
	{
		static byte[] Words( params uint[] words ) => words.SelectMany( BitConverter.GetBytes ).ToArray();
		return block switch
		{
			SaveBlock.Scripts => Words( SaveBlockWidths.ScriptsMagic, SaveBlockWidths.ScriptRecordBytes, 1, 0 ),
			SaveBlock.Particles => Words( SaveBlockWidths.ParticlesMagic, 0 ),
			SaveBlock.MessageCentre => new byte[4 + SaveBlockWidths.MessageLists * 4],
			SaveBlock.TrackRides => Words( 1, 12, 12 ),
			SaveBlock.FlyingRides => Words( SaveBlockWidths.FlyingMagic, 0 ),
			SaveBlock.Rsse => Words( SaveBlockWidths.RsseMagic, 0x14, 1, 0, 0x10, 0, 0, 0x5F444150, 0x5F444150, 0x5F444150, 0x5F444150, 0x5F444150, 0, SaveRsseBlock.RecordBytes ),
			SaveBlock.Advisor => Words( SaveBlockWidths.AdvisorMagic, 0, 0 ),
			SaveBlock.Sound => new byte[12],
			_ => throw new ArgumentOutOfRangeException( nameof( block ), block, "No minimal body." ),
		};
	}

	/// <summary>
	/// A full payload: a 3-byte action recording, <paramref name="world"/>, then every other block (fixed-width blocks
	/// at their widths, an empty Coasters block, <paramref name="rideSystem"/>; <paramref name="blocks"/> overrides), with
	/// their delimiters (<paramref name="tags"/> overrides the delimiter order) and a 4-byte UI block.
	/// <paramref name="rideSystem"/> is the complete framed RideSystem block including its length word
	/// (<see cref="EmptyRideSystem"/> when null).
	/// </summary>
	public static byte[] Payload( byte[] world, byte[]? rideSystem = null, IReadOnlyDictionary<SaveBlock, byte[]>? blocks = null,
		IEnumerable<(SaveBlock Block, string Tag)>? tags = null, uint published = 0 )
	{
		using var output = new MemoryStream();
		output.Write( BitConverter.GetBytes( published ) );
		output.Write( BitConverter.GetBytes( 3u ) );
		output.Write( new byte[] { 1, 2, 3 } );
		foreach ( var (block, tag) in tags ?? SavePayloadLayout.Delimiters )
		{
			var content = blocks != null && blocks.TryGetValue( block, out var replaced ) ? replaced : block switch
			{
				SaveBlock.World => world,
				SaveBlock.RideSystem => rideSystem ?? EmptyRideSystem,
				SaveBlock.Coasters => new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0 },
				_ when SavePayloadLayout.FixedBlockBytes.TryGetValue( block, out var size ) => new byte[size],
				_ => MinimalBlock( block ),
			};
			output.Write( content );
			output.Write( Stored( tag ) );
		}
		output.Write( new byte[4] );
		return output.ToArray();
	}

	/// <summary>A framed RideSystem block in the writer's shape (writer 0x464140, loader 0x4647a0): u32 length (= 12 + the slots' bytes),
	/// u32 used, free-hole and first-free-slot counts, then <paramref name="slots"/>' bytes. Each slot's first byte is its presence
	/// byte (<see cref="RideSystemRecord"/> writes 1, <see cref="RideSystemGap"/> writes 0); the counts, the first free slot (the blocks'
	/// first absent-slot ordinal; with no absent slot the used count, as the corpus's free-less blocks hold) and the length follow from
	/// the slots themselves.</summary>
	public static byte[] RideSystem( IReadOnlyList<byte[]> slots )
	{
		var slotBytes = slots.SelectMany( slot => slot ).ToArray();
		var used = slots.Count( slot => slot[0] == 1 );
		var free = slots.Count - used;
		var firstFree = used;
		for ( var slot = 0; slot < slots.Count; slot++ )
		{
			if ( slots[slot][0] == 0 )
			{
				firstFree = slot;
				break;
			}
		}
		using var output = new MemoryStream();
		using var writer = new BinaryWriter( output );
		writer.Write( (uint)(12 + slotBytes.Length) );
		writer.Write( (uint)used );
		writer.Write( (uint)free );
		writer.Write( (uint)firstFree );
		writer.Write( slotBytes );
		writer.Flush();
		return output.ToArray();
	}

	/// <summary>An absent slot of a RideSystem block: its presence byte.</summary>
	public static byte[] RideSystemGap() => new byte[] { 0 };

	/// <summary>The control a <see cref="RideSystemRecord"/> without an explicit list stores: no flags, the "none" animation set 12.</summary>
	public static readonly uint[] DefaultRideSystemControl = new uint[] { 0, 12, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

	/// <summary>An animation time control of a RideSystem record: the record's 11 stored words in the save's word order
	/// (see <see cref="OpenTPW.SaveRideSystemControl"/>).</summary>
	public static uint[] RideSystemControl( params uint[] words ) => words.Length == 11 ? words
		: throw new ArgumentException( "A saved animation time control stores 11 words.", nameof( words ) );

	/// <summary>
	/// A present RideSystem slot: presence byte 1, the 54-byte header (Info.Id, X, Y, width, height, kind, index; the
	/// packed instance flags, the +0x20 word and the +0x22 dword; the rotation, the node and embedded-node counts and
	/// the two +0x2e/+0x32 embedded words), then <paramref name="embeddedNodes"/>' 8-byte pairs, <paramref name="nodeStates"/>' words
	/// and <paramref name="controls"/>' 11-word animation time controls. (The header's two embedded words are separate
	/// from the pairs: writer 0x464140 stores both, like the loader 0x4647a0 reads them.)
	/// </summary>
	public static byte[] RideSystemRecord( int infoId, int x, int y, int width, int height, int kind, int index, int rotation = 0,
		uint instanceFlags = 0, ushort instanceWord = 0, uint instanceDword = 0, uint embeddedWord0 = 0, uint embeddedWord1 = 0,
		(uint First, uint Second)[]? embeddedNodes = null, uint[]? nodeStates = null, IReadOnlyList<uint[]>? controls = null )
	{
		var nodeCount = nodeStates?.Length ?? 0;
		var embeddedCount = embeddedNodes?.Length ?? 0;
		if ( controls != null && controls.Any( control => control.Length != 11 ) )
			throw new ArgumentException( "A saved animation time control stores 11 words.", nameof( controls ) );
		using var output = new MemoryStream();
		using var writer = new BinaryWriter( output );
		writer.Write( (byte)1 );
		foreach ( var field in new[] { infoId, x, y, width, height, kind, index } )
			writer.Write( field );
		writer.Write( instanceFlags );
		// The +0x20 word's position pins the header: presence byte 1, seven u32 fields, then the packed flags.
		if ( (int)output.Length != 1 + 0x1C + 4 )
			throw new InvalidOperationException( "Test header layout drifted." );
		writer.Write( instanceWord );
		writer.Write( instanceDword );
		writer.Write( rotation );
		writer.Write( (ushort)nodeCount );
		writer.Write( (ushort)embeddedCount );
		writer.Write( embeddedWord0 );
		writer.Write( embeddedWord1 );
		foreach ( var (first, second) in embeddedNodes ?? Array.Empty<(uint, uint)>() )
		{
			writer.Write( first );
			writer.Write( second );
		}
		foreach ( var state in nodeStates ?? Array.Empty<uint>() )
			writer.Write( state );
		// A record always has at least one control (tp.exe 0x4629d0 replaces a count of 0 with 1), so an omitted list writes one.
		foreach ( var control in controls ?? new[] { DefaultRideSystemControl } )
			foreach ( var word in control )
				writer.Write( word );
		writer.Flush();
		return output.ToArray();
	}

	/// <summary>
	/// A small but complete park on <paramref name="map"/>: bank 8, challenge system 10, advisor 3, staff HQ 1, research
	/// lab 6, researcher 30, one attraction 12 (Info.Id 1100 at cell 1, 1) and the other singleton things.
	/// </summary>
	public static List<(uint Id, uint Model, byte[] Body)> ParkThings() => new()
	{
		(30, (uint)SaveThingModel.Researcher, Staff( SaveThingModel.Researcher )),
		(12, (uint)SaveThingModel.Attraction, Attraction( new AttractionFields { PositionX = 1 * 256 + 128, PositionY = 1 * 256 + 128, Angle = 0, Price = 30, Level = 0, Repair = 80, LifeGauge = 90, IngredientAmount = 80, Quality = 100, Chance = 30 } )),
		(10, (uint)SaveThingModel.ChallengeSystem, ChallengeSystem( new[] { (3, 60, 30, 5000), (9, 60, 15, 3000) } )),
		(9, (uint)SaveThingModel.UIMsgReceiver, Fixed( SaveThingModel.UIMsgReceiver )),
		(8, (uint)SaveThingModel.Bank, Bank()),
		(7, (uint)SaveThingModel.Weather, Fixed( SaveThingModel.Weather )),
		(6, (uint)SaveThingModel.ResearchLab, Fixed( SaveThingModel.ResearchLab )),
		(5, (uint)SaveThingModel.ParkAnalyser, Fixed( SaveThingModel.ParkAnalyser )),
		(4, (uint)SaveThingModel.TagSystem, Fixed( SaveThingModel.TagSystem )),
		(3, (uint)SaveThingModel.AdvisorController, AdvisorController( new[] { new AdvisorRecordFields( 0, 0, 128, 20, 1, -1, 0, 0 ) }, new Dictionary<int, (uint, int, byte, uint)> { [0] = (1, 0, 1, 0) } )),
		(2, (uint)SaveThingModel.MechanicHq, Fixed( SaveThingModel.MechanicHq )),
		(1, (uint)SaveThingModel.StaffHq, Fixed( SaveThingModel.StaffHq )),
	};

	public static byte[] ParkVariables( uint gameTick = 755, uint parkClosed = 0, uint worldState = 0 ) =>
		Variables( bank: 8, gameTick: gameTick, parkClosed: parkClosed, researchLab: 6, staffHq: 1, firstResearcher: 30, firstObject: 12, worldState: worldState );
}
