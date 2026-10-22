using System.Buffers.Binary;
namespace OpenTPW;

/// <summary>Base thing serializer 0xfa808: <c>mX, mY, mMapChild, mMapParent</c>, four u16 fields.</summary>
public readonly record struct SaveThingBase( ushort X, ushort Y, ushort MapChild, ushort MapParent )
{
	public const int Bytes = 8;
}

/// <summary>
/// The park-clock time stamp of an attraction record, its eight words <c>tv[t]</c>: the attraction serializer converts the thing's 64-bit park time
/// with FileTimeToSystemTime and writes the SYSTEMTIME fields year, month, day, day of the week (0 = Sunday), hour, minute, second and milliseconds.
/// The loader rebuilds the time with SystemTimeToFileTime from every word but <see cref="DayOfWeek"/>, which it skips; that word is kept as read.
/// </summary>
public readonly record struct SaveTimestamp( int Year, int Month, int Day, uint DayOfWeek, int Hour, int Minute, int Second, int Millisecond )
{
	public const int Words = 8;

	/// <summary>The time the loader rebuilds from every word but <see cref="DayOfWeek"/>, as a UTC date; throws <see cref="ArgumentOutOfRangeException"/> when the words name no date (such as 30 February).</summary>
	public DateTime ToDateTime() => new( Year, Month, Day, Hour, Minute, Second, Millisecond, DateTimeKind.Utc );

	/// <summary>Reads the eight words; throws when a word the loader converts lies outside year 0–9999, month 1–12, day 1–31, hour 0–23, minute and second 0–59 or milliseconds 0–999.</summary>
	// [BIN:TPW-EXE:0x4DB8EE attraction serializer stamp] tv[0..7] are the out-fields of 0x5FC660 (FileTimeToSystemTime, IAT 0x6FD0BC) on the park time at +0x18: wYear, wMonth, wDay (SYSTEMTIME +6), wDayOfWeek (+4), wHour, wMinute, wSecond, wMilliseconds, written in that order (0x4DB8FB..0x4DB953)
	// [BIN:TPW-EXE:0x4DC8A1 attraction loader stamp] the loader reads the eight words back (0x4DC849..0x4DC874) and passes tv[0], tv[1], tv[2], tv[4], tv[5], tv[6], tv[7] to 0x5FC5C0 (SystemTimeToFileTime, IAT 0x6FD150), which takes each word's low 16 bits; it skips the day of the week and checks nothing: a field SystemTimeToFileTime refuses keeps +0x18 as it was and writes -1 to a status word the loader never reads
	// [BIN:TPI-EXE:0x5DD68E attraction loader stamp] Theme Park Inc's serializer 0x5DC430 is the same: it writes the fields of 0x7D91E0 (FileTimeToSystemTime) on +0x20 (0x5DC56E) and loads them through 0x7D9140 (SystemTimeToFileTime) without tv[3] or a check
	internal static SaveTimestamp Read( ref SaveFieldReader reader, int id )
	{
		Span<uint> tv = stackalloc uint[Words];
		for ( var index = 0; index < Words; index++ )
			tv[index] = reader.U32();
		// Host input policy: a word outside these ranges is refused as a damaged save, where the loader would keep the thing's time as it was
		if ( tv[0] > 9999 || tv[1] is < 1 or > 12 || tv[2] is < 1 or > 31 || tv[4] > 23 || tv[5] > 59 || tv[6] > 59 || tv[7] > 999 )
			throw new InvalidDataException( $"Save attraction thing {id} has an invalid game-time stamp {tv[0]}-{tv[1]}-{tv[2]} {tv[4]}:{tv[5]}:{tv[6]}.{tv[7]}." );
		return new SaveTimestamp( (int)tv[0], (int)tv[1], (int)tv[2], tv[3], (int)tv[4], (int)tv[5], (int)tv[6], (int)tv[7] );
	}
}

/// <summary>
/// One model-3 thing (ride, shop, sideshow, feature, gate, traffic lights or bus): tp.exe's attraction serializer <c>0x004DB7D0</c>
/// (docs/TPWS-PAYLOAD.md, "Attraction records"). <see cref="Offset"/> is the body offset, <see cref="Length"/> the body length.
/// <see cref="PositionX"/>/<see cref="PositionY"/> are the base thing's X/Y in 1/256 cell units; for buildable objects the cell equals the
/// RideSystem record cell, fixed items store 128, 128. The loader puts the three floats at thing +0x4C (<see cref="Gauge48"/>, the Mac's +0x48),
/// +0x48 (<see cref="LifeGauge"/>) and +0x44 (<see cref="StateOfRepair"/>); the original saves each as a whole number 0–255.
/// <see cref="TotalCosts"/>/<see cref="TotalTakings"/> are read as signed 32-bit values (+0x184/+0x180; the panel's total profit 0x004E1C60 is their
/// difference). <see cref="State"/> is <c>mState</c> (+0x19C) and the other properties name the remaining fields (<see cref="Timestamp"/> is the park time at +0x18,
/// <see cref="AmountOfSpecialIngredient"/> is <c>mAmountOfSpecialIngredient</c>).
/// </summary>
public readonly record struct SaveAttraction(
	int Offset, int Id, SaveThingBase Base, int InfoId, int Angle, SaveTimestamp Timestamp,
	uint MeshInstanceId, string NameLine1, string NameLine2, int State, int OperatingCapacity, int OperatingDuration,
	uint OperatingSpeed, int CostOfGoods, int QualityOfGoods, int ChanceOfWinning, int PricePerUse, int QueueSizeInCells,
	uint CustomerCount, uint WalkAwayCount, int TotalCosts, int TotalTakings, float Gauge48, float LifeGauge, float StateOfRepair,
	int UpgradeLevel, int Length )
{
	/// <summary><c>mAmountOfSpecialIngredient</c> (ride <c>+0x198</c>), the signed word after <c>mPricePerUse</c>; the original loader takes it unclamped (tp.exe <c>0x4dcdc1</c>).</summary>
	public int AmountOfSpecialIngredient { get; init; }

	public const int NameCharacters = 33;
	/// <summary>History buffers hold at most this many entries (head and count are bounded by it).</summary>
	public const int MaximumRingEntries = 31;
	/// <summary><c>mCurrentEntry</c> of a ring nothing has been written to yet (tp.exe <c>0x51ADE0</c>).</summary>
	public const int EmptyRingHead = -1;
	/// <summary><c>mNumEntries</c> of every ring the constructor makes.</summary>
	public const int InitialRingEntries = 30;
	/// <summary>The original loader clamps <c>mPricePerUse</c> to 0–500.</summary>
	public const int MaximumPricePerUse = 500;
	public const int MaximumUpgradeLevel = 3;
	public const float MaximumGauge = 255f;
	public const int HistoryBuffers = 6;

	public int PositionX => Base.X;
	public int PositionY => Base.Y;
	public int CellX => PositionX >> 8;
	public int CellY => PositionY >> 8;

	/// <summary>Reads the attraction body at <paramref name="offset"/>; throws on truncation or an out-of-range field.</summary>
	public static SaveAttraction Read( ReadOnlySpan<byte> data, int offset, int id )
	{
		var reader = new SaveFieldReader( data, offset, $"attraction thing {id}" );
		var thingBase = reader.Base();
		var angle = reader.U32();
		var infoId = reader.U16();
		if ( infoId == 0 || angle % 90 != 0 || angle >= 360 )
			throw new InvalidDataException( $"Save attraction thing {id} has Info.Id {infoId} and angle {angle}; expected a nonzero Info.Id and 0/90/180/270." );
		var timestamp = SaveTimestamp.Read( ref reader, id );
		var meshInstance = reader.U32();
		var flags = reader.U16();
		var line1 = new char[NameCharacters];
		var line2 = new char[NameCharacters];
		for ( var index = 0; index < NameCharacters; index++ )
			(line1[index], line2[index]) = ((char)reader.U16(), (char)reader.U16());
		var (scriptHandle, trackHandle) = (reader.U32(), reader.U32());
		var state = reader.U32();
		if ( state > ushort.MaxValue )
			throw new InvalidDataException( $"Save attraction thing {id} has state {state}." );
		var (topLeft, entryPos, next, assigned, backOfQueue) = (reader.U16(), reader.U16(), reader.U16(), reader.U16(), reader.U16());
		var canLoad = reader.U32();
		var (exitPos, firstInQueue) = (reader.U16(), reader.U16());
		var trackValid = reader.U32();
		var upgradeParent = reader.U16();
		var histories = new SaveAttractionHistory[HistoryBuffers];
		histories[0] = ReadHistory( ref reader, id );
		histories[1] = ReadHistory( ref reader, id );
		var customers = reader.U32();
		histories[2] = ReadHistory( ref reader, id );
		var walkAways = reader.U32();
		histories[3] = ReadHistory( ref reader, id );
		histories[4] = ReadHistory( ref reader, id );
		histories[5] = ReadHistory( ref reader, id );
		var capacity = reader.U8();
		var duration = reader.U8();
		var speed = reader.U32();
		var personBeingLoaded = reader.U16();
		var costOfGoods = reader.I32();
		var quality = reader.I32();
		var chance = reader.I32();
		var price = reader.U32();
		if ( price > MaximumPricePerUse )
			throw new InvalidDataException( $"Save attraction thing {id} has price {price}; the original loader allows 0–{MaximumPricePerUse}." );
		// [BIN:TPW-EXE:0x4dcdc1 attraction loader] mAmountOfSpecialIngredient (+0x198) follows mPricePerUse (+0x194, clamped to 0..500) as a 4-byte word read without a clamp, then mQueueSizeInCells (+0x40)
		var ingredient = reader.I32();
		var queueCells = reader.U32();
		if ( queueCells > ushort.MaxValue )
			throw new InvalidDataException( $"Save attraction thing {id} has {queueCells} queue cells." );
		var gauge48 = reader.F32();
		var lifeGauge = reader.F32();
		var repair = reader.F32();
		if ( !IsGauge( gauge48 ) || !IsGauge( lifeGauge ) || !IsGauge( repair ) )
			throw new InvalidDataException( $"Save attraction thing {id} has a gauge outside 0–{MaximumGauge}." );
		var (requested, marked) = (reader.U32(), reader.U32());
		var costs = reader.I32();
		var takings = reader.I32();
		var balloonSprite = reader.U32();
		var level = reader.U8();
		if ( level > MaximumUpgradeLevel )
			throw new InvalidDataException( $"Save attraction thing {id} has upgrade level {level}." );
		return new SaveAttraction( offset, id, thingBase, infoId, (int)angle, timestamp, meshInstance, Text( line1 ), Text( line2 ),
			(int)state, capacity, duration, speed, costOfGoods, quality, chance, (int)price, (int)queueCells, customers, walkAways,
			costs, takings, gauge48, lifeGauge, repair, level, reader.Position - offset )
		{
			Flags = flags, RideScriptHandle = scriptHandle, TrackRideHandle = trackHandle, TopLeft = topLeft, EntryPos = entryPos, Next = next,
			AssignedStaffMember = assigned, BackOfQueueCell = backOfQueue, CanLoad = canLoad, ExitPos = exitPos, FirstInQueue = firstInQueue,
			IsTrackRideValid = trackValid, UpgradeParent = upgradeParent, Histories = histories, PersonBeingLoaded = personBeingLoaded,
			AmountOfSpecialIngredient = ingredient, RequestedService = requested, TimeMarkedForMaintenance = marked, UpgradeBalloonSprite = balloonSprite,
		};
	}

	/// <summary><c>mFlags</c> (+0x32): the type's flag word the constructor builds (bit 0 relief, bit 1 rest area, bit 2 choosable).</summary>
	public ushort Flags { get; init; }
	public uint RideScriptHandle { get; init; }
	public uint TrackRideHandle { get; init; }
	/// <summary><c>mTopLeft</c>, <c>mEntryPos</c> and <c>mExitPos</c> (+0x34, +0x36, +0x38): cell ids of the footprint corner, the entrance and the exit.</summary>
	public ushort TopLeft { get; init; }
	public ushort EntryPos { get; init; }
	public ushort ExitPos { get; init; }
	/// <summary><c>mNext</c> (+0xC): the next attraction thing of the object list.</summary>
	public ushort Next { get; init; }
	/// <summary><c>mAssignedStaffMember</c> (+0x5E): the staff thing that claimed the object (0x004E01F0), 0 when none.</summary>
	public ushort AssignedStaffMember { get; init; }
	/// <summary>
	/// <c>mBackOfQueue</c> (+0x3A): the cell id (y × 128 + x + 1) of the cached back-of-queue cell, 0 when the cache is empty; not a guest. The back-of-queue
	/// walk <c>0x4DE130</c> recomputes it (with <see cref="QueueSizeInCells"/>) only when the word is 0, the queue edit <c>0x4DE1F0</c> drops it, and the loader
	/// keeps the saved value.
	/// </summary>
	public ushort BackOfQueueCell { get; init; }
	/// <summary><c>mFirstInQ</c> (+0x3C): the thing id of the queue's head guest. There is no tail word: the join walks the guests' <c>mQNext</c> links to the tail.</summary>
	public ushort FirstInQueue { get; init; }
	/// <summary><c>mCanLoad</c> (+0x68): the open word; SetState (0x004E0E60) clears it for states 1, 2 and 4.</summary>
	public uint CanLoad { get; init; }
	public uint IsTrackRideValid { get; init; }
	public ushort UpgradeParent { get; init; }
	/// <summary>
	/// The six 30-day rings in serializer order: costs (+0xF8), takings (+0x70), customers (+0x1A8), walk-aways (+0x230), visits (+0x2B8) and the
	/// visits' averaged happiness change (+0x340); each ring's current value rolls into the ring on the day message 0xB (0x004DD320).
	/// </summary>
	public IReadOnlyList<SaveAttractionHistory> Histories { get; init; } = Array.Empty<SaveAttractionHistory>();
	/// <summary><c>mPersonBeingLoaded</c> (+0x6C): the guest called forward, 0 when none.</summary>
	public ushort PersonBeingLoaded { get; init; }
	/// <summary><c>mRequestedService</c> (+0x64): the player's maintenance call (0x004DFE30).</summary>
	public uint RequestedService { get; init; }
	/// <summary><c>mTimeMarkedForMaintenance</c> (+0x60): the turn the claim was made (0x004E01F0).</summary>
	public uint TimeMarkedForMaintenance { get; init; }
	public uint UpgradeBalloonSprite { get; init; }

	/// <summary>History ring (0x004CED70 / 0x004CEE50): u32 <c>mCurrentEntry</c>, u32 <c>mNumEntries</c>, u8 <c>mWrappedAround</c>, u32 <c>mTemp</c>, then <c>count</c> u32 <c>mData[i]</c>.</summary>
	private static SaveAttractionHistory ReadHistory( ref SaveFieldReader reader, int id )
	{
		var head = reader.I32();
		var count = reader.U32();
		var flag = reader.U8();
		var current = reader.I32();
		// [BIN:TPW-EXE:0x51ADE0 history constructor] a new ring has mCurrentEntry 0xFFFFFFFF, 30 entries and no wrap; that empty ring is the only head below 0 (the loader 0x4E2C10 reads it unchecked)
		var empty = head == EmptyRingHead && count == InitialRingEntries && flag == 0;
		if ( head is < 0 && !empty || head > MaximumRingEntries || count > MaximumRingEntries || flag > 1 )
			throw new InvalidDataException( $"Save attraction thing {id} has a history buffer with head {head}, count {count}, flag {flag}." );
		var data = new int[count];
		for ( var index = 0; index < data.Length; index++ )
			data[index] = reader.I32();
		return new SaveAttractionHistory( head, flag != 0, current, data );
	}

	private static bool IsGauge( float value ) => float.IsFinite( value ) && value >= 0f && value <= MaximumGauge;

	private static string Text( char[] characters )
	{
		var end = Array.IndexOf( characters, '\0' );
		return new string( characters, 0, end < 0 ? characters.Length : end );
	}
}

/// <summary>One 30-day ring of an attraction: the entry last written, whether it has wrapped, the running value (<c>mTemp</c>) and the entries.</summary>
public sealed record SaveAttractionHistory( int CurrentEntry, bool WrappedAround, int Current, IReadOnlyList<int> Entries );

/// <summary>
/// Staff suffix (105 bytes) of models 4–8, in the order of tp.exe's staff serializer <c>0x00504DE0</c>: <c>mCurrentPayGrade</c> (+0x1E4), happiness
/// (+0x1F8, written as its truncated low byte), <c>mJobsDone</c> (+0x204), the inline name <c>mName[i]</c> (+0x1A0, 33 UTF-16 units; the staff carry
/// no name index), <c>mPatrolRegionBL</c> (+0x20A) and <c>mPatrolRegionTR</c> (+0x20C) as cell ids (y × 128 + x + 1, the area test <c>0x00506ED0</c>),
/// <c>mPercentageThroughGrade</c> (+0x1E8), <c>mRestArea</c> (+0x208, a thing id), <c>mState</c> (+0x19C, the member's own state word),
/// <c>mTimeStartedIdling</c> (+0x200, a park turn), <c>mTimeHired</c> (+0x1F0, 100 ns since 1601-01-01 like the calendar) and energy (+0x1FC).
/// </summary>
public readonly record struct SaveStaffRecord( int Grade, float Happiness, uint JobsDone, string Name, ushort PatrolRegionBottomLeft, ushort PatrolRegionTopRight,
	int GradePercentage, ushort RestAreaId, uint State, uint TimeStartedIdling, long HiredTimestamp, float Energy )
{
	/// <summary>The cells of a map 128 cells wide: cell id = y × 128 + x + 1 (tp.exe <c>0x00506ED0</c>, <c>0x00506E90</c>).</summary>
	public const int MapWidth = 128;
	public const int CellIds = MapWidth * MapWidth;

	/// <summary>The cell of a staff cell id (y × 128 + x + 1); null for 0 or an id past the map.</summary>
	public static (int X, int Y)? CellOf( ushort id ) => id is >= 1 and <= CellIds ? ((id - 1) & (MapWidth - 1), (id - 1) / MapWidth) : null;
}

/// <summary>
/// A staff thing (models 4 Mechanic, 5 Handyman, 6 Entertainer, 7 Guard, 8 Researcher): base, the 382 person bytes after the base (decoded by
/// <see cref="Person"/>), the staff suffix and the role suffix. <see cref="RoleNextId"/> is the role's linked-list next ID (<c>mNext</c>, +0x210, the
/// suffix's last u16); <see cref="RoleBytes"/> holds the role fields before it (role serializers <c>0x004DA110</c> mechanic, <c>0x004D6D60</c> handyman,
/// <c>0x004D4460</c> entertainer, <c>0x004D6000</c> guard, <c>0x00502760</c> researcher), read by <see cref="RoleTimer"/>, <see cref="RoleTarget"/> and
/// <see cref="RoleToilet"/>.
/// </summary>
public sealed record SaveStaff( int Id, SaveThingModel Model, SaveThingBase Base, byte[] PersonBytes, SaveStaffRecord Staff, byte[] RoleBytes, ushort RoleNextId )
{
	/// <summary>The decoded person block (position, the walk's accurate destination, speeds, thoughts).</summary>
	public SavePerson Person => SavePerson.Read( Base, PersonBytes, $"{Model} thing {Id}" );

	/// <summary>
	/// The role's word at +0x214: a mechanic's <c>mDurationOfRepair</c> (the repair timer), a handyman's <c>mTimeStartedCleaning</c>, an entertainer's
	/// <c>mTimeStartedEntertaining</c>, a guard's <c>mProsecutionTimestamp</c> (the chase timer) and a researcher's <c>mTimeStartedResearching</c>.
	/// </summary>
	public uint RoleTimer => Model switch
	{
		SaveThingModel.Handyman or SaveThingModel.Guard => BinaryPrimitives.ReadUInt32LittleEndian( RoleBytes.AsSpan( 2 ) ),
		_ => BinaryPrimitives.ReadUInt32LittleEndian( RoleBytes ),
	};

	/// <summary>
	/// The role's target: a mechanic's <c>mObjectToRepair</c> (+0x218, a thing id), a handyman's <c>mTargetLitterCell</c> (+0x218, a cell id) and a
	/// guard's <c>mPerp</c> (+0x212, a guest thing id); 0 for the entertainer and the researcher.
	/// </summary>
	public ushort RoleTarget => Model switch
	{
		SaveThingModel.Mechanic => BinaryPrimitives.ReadUInt16LittleEndian( RoleBytes.AsSpan( 4 ) ),
		SaveThingModel.Handyman or SaveThingModel.Guard => BinaryPrimitives.ReadUInt16LittleEndian( RoleBytes ),
		_ => 0,
	};

	/// <summary>A handyman's <c>mToiletToClean</c> (+0x21A, a thing id); 0 for the other roles.</summary>
	public ushort RoleToilet => Model == SaveThingModel.Handyman ? BinaryPrimitives.ReadUInt16LittleEndian( RoleBytes.AsSpan( 6 ) ) : (ushort)0;

	public static SaveStaff Read( ReadOnlySpan<byte> payload, SaveThing thing )
	{
		if ( thing.Model is < SaveThingModel.Mechanic or > SaveThingModel.Researcher )
			throw new ArgumentException( $"Thing {thing.Id} is a {thing.Model}, not staff.", nameof( thing ) );
		var reader = new SaveFieldReader( payload[..thing.End], thing.BodyOffset, $"{thing.Model} thing {thing.Id}" );
		var thingBase = reader.Base();
		var person = reader.Bytes( SaveThingChain.PersonBytes - SaveThingBase.Bytes );
		var staff = new SaveStaffRecord( reader.I32(), reader.F32(), reader.U32(), reader.Utf16( 33 ), reader.U16(), reader.U16(), reader.U8(),
			reader.U16(), reader.U32(), reader.U32(), reader.I64(), reader.F32() );
		if ( staff.Grade is < 0 or > 4 || !float.IsFinite( staff.Happiness ) || !float.IsFinite( staff.Energy ) || staff.GradePercentage is < 0 or > 100 )
			throw new InvalidDataException( $"Save {thing.Model} thing {thing.Id} has grade {staff.Grade}, percentage {staff.GradePercentage}, happiness {staff.Happiness}, energy {staff.Energy}." );
		var role = reader.Bytes( thing.End - reader.Position - 2 );
		return new SaveStaff( thing.Id, thing.Model, thingBase, person, staff, role, reader.U16() );
	}
}

/// <summary>
/// Model 9 (0xf7c34): force strike 4, picked up 2, five 12-byte strike levels (raw), five 4-byte training budgets and
/// a trained byte. The budgets' role order is not traced.
/// </summary>
public sealed record SaveStaffHq( int Id, SaveThingBase Base, uint ForceStrike, ushort PickedUp, byte[] StrikeLevels, IReadOnlyList<int> TrainingBudgets, byte Trained )
{
	public const int Roles = 5;

	public static SaveStaffHq Read( ReadOnlySpan<byte> payload, SaveThing thing )
	{
		var reader = Body( payload, thing, SaveThingModel.StaffHq );
		var thingBase = reader.Base();
		var force = reader.U32();
		var picked = reader.U16();
		var strike = reader.Bytes( Roles * 12 );
		var budgets = new int[Roles];
		for ( var index = 0; index < Roles; index++ )
			budgets[index] = reader.I32();
		return new SaveStaffHq( thing.Id, thingBase, force, picked, strike, budgets, reader.U8() );
	}

	internal static SaveFieldReader Body( ReadOnlySpan<byte> payload, SaveThing thing, SaveThingModel model )
	{
		if ( thing.Model != model )
			throw new ArgumentException( $"Thing {thing.Id} is a {thing.Model}, not a {model}.", nameof( thing ) );
		return new SaveFieldReader( payload[..thing.End], thing.BodyOffset, $"{model} thing {thing.Id}" );
	}
}

/// <summary>
/// One advisor controller record (record serializer 0xbc10, 21 bytes): <c>mThingId 2, mSecondary 4, mMessage 4,
/// mScore 4, mValid 1, mResponseNo 4, mOverrideOnlyOnce 1, mScoreOverridden 1</c>. Records with <see cref="Valid"/>
/// clear are free slots; their other bytes may be uninitialized (0xCD) memory and carry no meaning.
/// </summary>
public readonly record struct SaveAdvisorRecord( ushort ThingId, uint Secondary, uint MessageId, int Score, bool Valid, int ResponseNo, byte OverrideOnlyOnce, byte ScoreOverridden )
{
	public const int Bytes = 21;

	internal static SaveAdvisorRecord Read( ref SaveFieldReader reader, string field )
	{
		var record = new SaveAdvisorRecord( reader.U16(), reader.U32(), reader.U32(), reader.I32(), reader.Flag( field + ".mValid" ), reader.I32(), reader.U8(), reader.U8() );
		if ( record.Valid && (record.OverrideOnlyOnce > 1 || record.ScoreOverridden > 1) )
			throw new InvalidDataException( $"Save advisor {field} is valid but has override flags {record.OverrideOnlyOnce}/{record.ScoreOverridden}." );
		return record;
	}
}

/// <summary>
/// One 16-byte per-message history entry, written as one block from controller +224 (0xcccc): saved raw game tick
/// (+0xE0), previous response variant (+0xE4), played flag (low byte of +0xE8; the other three bytes are not
/// written by the game and stay raw in <see cref="Padding"/>), slap count (+0xEC). Index = message ID 0–350.
/// </summary>
public readonly record struct SaveAdvisorHistory( uint SavedGameTick, int PreviousVariant, bool HasBeenPlayed, uint Padding, uint SlapCount )
{
	public const int Bytes = 16;
	public bool IsInitial => SavedGameTick == 0 && PreviousVariant == -1 && !HasBeenPlayed && SlapCount == 0;
}

/// <summary>
/// Model 11, the advisor controller (0x82e0, 5,838 bytes): base 8, time signature 4, sound 4, eight pending
/// records from controller +20, three raw words, 351 history entries from +224, the current record at +5840,
/// <c>mRequestedLastTutorial</c> 1 and <c>mMessagePlaying</c> 4.
/// </summary>
public sealed record SaveAdvisorController( int Id, SaveThingBase Base, uint TimeSignature, uint Sound, IReadOnlyList<SaveAdvisorRecord> Pending,
	IReadOnlyList<uint> RawWords, IReadOnlyList<SaveAdvisorHistory> History, SaveAdvisorRecord Current, byte RequestedLastTutorial, uint MessagePlaying )
{
	public const int PendingRecords = 8;
	public const int HistoryEntries = 351;
	public const int Bytes = SaveThingBase.Bytes + 4 + 4 + PendingRecords * SaveAdvisorRecord.Bytes + 3 * 4 + HistoryEntries * SaveAdvisorHistory.Bytes + SaveAdvisorRecord.Bytes + 1 + 4;

	public static SaveAdvisorController Read( ReadOnlySpan<byte> payload, SaveThing thing )
	{
		var reader = SaveStaffHq.Body( payload, thing, SaveThingModel.AdvisorController );
		var thingBase = reader.Base();
		var signature = reader.U32();
		var sound = reader.U32();
		var pending = new SaveAdvisorRecord[PendingRecords];
		for ( var index = 0; index < pending.Length; index++ )
			pending[index] = SaveAdvisorRecord.Read( ref reader, $"pending record {index}" );
		var words = new[] { reader.U32(), reader.U32(), reader.U32() };
		var history = new SaveAdvisorHistory[HistoryEntries];
		for ( var index = 0; index < history.Length; index++ )
		{
			var tick = reader.U32();
			var variant = reader.I32();
			var played = reader.U32();
			if ( (played & 0xFF) > 1 )
				throw new InvalidDataException( $"Save advisor history {index} has played byte {played & 0xFF}." );
			history[index] = new SaveAdvisorHistory( tick, variant, (played & 0xFF) != 0, played >> 8, reader.U32() );
		}
		var current = SaveAdvisorRecord.Read( ref reader, "current record" );
		return new SaveAdvisorController( thing.Id, thingBase, signature, sound, pending, words, history, current, reader.U8(), reader.U32() );
	}
}

/// <summary>One research project (16 bytes, tp.exe <c>0x504210</c>): object id, category, group, upgrade level, the points it costs and the points done.</summary>
public readonly record struct SaveResearchProject( ushort ObjectId, uint Category, byte Group, byte UpgradeLevel, float Cost, float Done );

/// <summary>One research category (13 bytes, tp.exe <c>0x504760</c>): effort, output (not read by the game), current project index (−1: none) and highest open group.</summary>
public readonly record struct SaveResearchGroup( int Effort, int Output, int CurrentProject, byte HighestGroup );

/// <summary>
/// Model 14, the research lab (tp.exe <c>0x502dc0</c>; Mac 0xf0930): base, 256 project records of 16 bytes, five group records of 13 bytes and
/// the tail mNumProjects 4, mWorkLoad 4, mMostExpensiveProject 4, mLabActive 1.
/// </summary>
// [BIN:TPW-EXE:0x504210 research project serializer] u16 mObjId, u32 mCat, u8 mResearchGroup, u8 mUpgradeLevel, f32 mCost, f32 mResearchDone (0x504630 compares and adds them as floats)
// [BIN:TPW-EXE:0x504760 research group serializer] u32 effort, u32 output, i32 currentProject, u8 highestGroupAvailable
public sealed record SaveResearchLab( int Id, SaveThingBase Base, IReadOnlyList<byte[]> Projects, IReadOnlyList<byte[]> Groups, byte[] Tail )
{
	public SaveResearchProject Project( int index )
	{
		var raw = Projects[index];
		return new SaveResearchProject( BinaryPrimitives.ReadUInt16LittleEndian( raw ), BinaryPrimitives.ReadUInt32LittleEndian( raw.AsSpan( 2 ) ), raw[6], raw[7],
			BinaryPrimitives.ReadSingleLittleEndian( raw.AsSpan( 8 ) ), BinaryPrimitives.ReadSingleLittleEndian( raw.AsSpan( 12 ) ) );
	}

	public SaveResearchGroup Group( int index )
	{
		var raw = Groups[index];
		return new SaveResearchGroup( BinaryPrimitives.ReadInt32LittleEndian( raw ), BinaryPrimitives.ReadInt32LittleEndian( raw.AsSpan( 4 ) ),
			BinaryPrimitives.ReadInt32LittleEndian( raw.AsSpan( 8 ) ), raw[12] );
	}

	/// <summary>The lab's work load percentage (tail <c>mWorkLoad</c>).</summary>
	public int WorkLoad => BinaryPrimitives.ReadInt32LittleEndian( Tail.AsSpan( 4 ) );

	public const int ProjectRecords = 256;
	public const int ProjectBytes = 16;
	public const int GroupRecords = 5;
	public const int GroupBytes = 13;
	public const int TailBytes = 4 + 4 + 4 + 1;
	public const int Bytes = SaveThingBase.Bytes + ProjectRecords * ProjectBytes + GroupRecords * GroupBytes + TailBytes;

	public static SaveResearchLab Read( ReadOnlySpan<byte> payload, SaveThing thing )
	{
		var reader = SaveStaffHq.Body( payload, thing, SaveThingModel.ResearchLab );
		var thingBase = reader.Base();
		var projects = new byte[ProjectRecords][];
		for ( var index = 0; index < projects.Length; index++ )
			projects[index] = reader.Bytes( ProjectBytes );
		var groups = new byte[GroupRecords][];
		for ( var index = 0; index < groups.Length; index++ )
			groups[index] = reader.Bytes( GroupBytes );
		return new SaveResearchLab( thing.Id, thingBase, projects, groups, reader.Bytes( TailBytes ) );
	}
}

/// <summary>One 32-byte loan-offer record of the bank (serializer 0xcba54).</summary>
public readonly record struct SaveLoanRecord( int Offset, int Index, bool Available, int Amount, int AprPercent, int Months,
	int MonthlyRepayment, bool Bought, int MonthsRepaid, int LenderNameIndex );

/// <summary>The seven 32-bit bank words before the loan records (serializer 0xcba54).</summary>
public readonly record struct SaveBankRecord( int Offset, int AdmissionFee, int Balance, int BatchBalance, bool WithdrawalsEnabled,
	int LastBalance, uint TurnEnteredRed, int ProfitThisYear );

/// <summary>Model 16, the bank account: base, seven bank words and eight loan offers.</summary>
public sealed record SaveBank( int Id, SaveThingBase Base, SaveBankRecord Account, IReadOnlyList<SaveLoanRecord> Loans )
{
	public const int LoanRecords = 8;
	public const int LoanBytes = 32;
	public const int AccountBytes = 28;
	public const int Bytes = SaveThingBase.Bytes + AccountBytes + LoanRecords * LoanBytes;

	public static SaveBank Read( ReadOnlySpan<byte> payload, SaveThing thing )
	{
		var reader = SaveStaffHq.Body( payload, thing, SaveThingModel.Bank );
		var thingBase = reader.Base();
		var offset = reader.Position;
		var fee = reader.I32();
		var balance = reader.I32();
		var batch = reader.I32();
		var withdrawals = reader.U32();
		if ( withdrawals > 1 )
			throw new InvalidDataException( $"Save bank thing {thing.Id} has a non-boolean withdrawals-enabled word {withdrawals}." );
		var account = new SaveBankRecord( offset, fee, balance, batch, withdrawals != 0, reader.I32(), reader.U32(), reader.I32() );
		var loans = new SaveLoanRecord[LoanRecords];
		for ( var index = 0; index < loans.Length; index++ )
		{
			var start = reader.Position;
			var available = reader.U32();
			var amount = reader.I32();
			var apr = reader.I32();
			var months = reader.I32();
			var monthly = reader.I32();
			var bought = reader.U32();
			var repaid = reader.I32();
			var lender = reader.I32();
			if ( available > 1 || bought > 1 || amount < 0 || apr < 0 || months <= 0 || repaid < 0 || repaid > months || lender < 0 )
				throw new InvalidDataException( $"Save bank loan offer {index} is out of range (available {available}, amount {amount}, APR {apr}, months {months}, bought {bought}, repaid {repaid}, lender {lender})." );
			loans[index] = new SaveLoanRecord( start, index, available != 0, amount, apr, months, monthly, bought != 0, repaid, lender );
		}
		return new SaveBank( thing.Id, thingBase, account, loans );
	}

	/// <summary>
	/// The body bytes the bank's serializer writes (the twin of <see cref="Read"/>): the base thing, the seven bank words and the eight 32-byte loan records, each field in its stored width.
	/// </summary>
	// [BIN:TPW-EXE:0x4cf920 bank serializer] base (0xfa808), then the account words (admission fee, balance, batch balance, withdrawals enabled, last balance, turn entered red, profit this year), then the eight loan records (available, amount, APR, months, monthly repayment, bought, months repaid, lender)
	public byte[] ToBody()
	{
		if ( Loans.Count != LoanRecords )
			throw new InvalidOperationException( $"A bank holds {LoanRecords} loan offers." );
		var writer = new SaveByteWriter();
		writer.U16( Base.X );
		writer.U16( Base.Y );
		writer.U16( Base.MapChild );
		writer.U16( Base.MapParent );
		writer.I32( Account.AdmissionFee );
		writer.I32( Account.Balance );
		writer.I32( Account.BatchBalance );
		writer.U32( Account.WithdrawalsEnabled ? 1u : 0u );
		writer.I32( Account.LastBalance );
		writer.U32( Account.TurnEnteredRed );
		writer.I32( Account.ProfitThisYear );
		foreach ( var loan in Loans )
		{
			writer.U32( loan.Available ? 1u : 0u );
			writer.I32( loan.Amount );
			writer.I32( loan.AprPercent );
			writer.I32( loan.Months );
			writer.I32( loan.MonthlyRepayment );
			writer.U32( loan.Bought ? 1u : 0u );
			writer.I32( loan.MonthsRepaid );
			writer.I32( loan.LenderNameIndex );
		}
		return writer.ToArray();
	}
}

/// <summary>
/// One 45-byte challenge slot (field order differs from <c>Challenges.sam</c>): <c>i32 type, time, value, u16 object,
/// u16 object2, i32 staff type, prize, follow-up type, u8 check at end only, i32 offered, lost, declined, u8 won, u8 independent,
/// u16 follow-up thing</c>.
/// </summary>
// [BIN:TPW-EXE:0x4d0f90 challenge slot serializer] ... mCheckAtEndOnly (1), mChallengeOffered (4, +0x20), mChallengeLost (4, +0x24), mChallengeDeclined (4, +0x28), mHaveWon (1, +0x2c), mIndependent (1, +0x1d), mThingIdForFollowup (2, +0x2e)
public readonly record struct SaveChallengeRecord( int Offset, int Type, int TargetTime, int TargetValue, int TargetObject, int TargetObject2,
	int TargetStaffType, int Prize, int FollowupType, bool CheckAtEndOnly, bool Independent )
{
	public int Offered { get; init; }
	public int Lost { get; init; }
	public int Declined { get; init; }
	public bool Won { get; init; }
	public ushort FollowupThing { get; init; }
}

/// <summary>
/// Model 19, the challenge system (0xcf42c): base, 20 slots of 45 bytes, current challenge 4, next event time 8,
/// three switch bytes, value 4, actual thing 2. Used slots are the leading slots with a nonzero type; the rest are
/// unused (uninitialized 0xCD in Easymode) and are not interpreted.
/// </summary>
public sealed record SaveChallengeSystem( int Id, SaveThingBase Base, IReadOnlyList<SaveChallengeRecord> Challenges, int CurrentChallenge,
	long NextEventTime, byte Switch1, byte Switch2, byte Switch3, int Value, ushort ActualThing )
{
	public const int Slots = 20;
	public const int SlotBytes = 45;
	public const int Bytes = SaveThingBase.Bytes + Slots * SlotBytes + 4 + 8 + 1 + 1 + 1 + 4 + 2;

	public static SaveChallengeSystem Read( ReadOnlySpan<byte> payload, SaveThing thing )
	{
		var reader = SaveStaffHq.Body( payload, thing, SaveThingModel.ChallengeSystem );
		var thingBase = reader.Base();
		var used = new List<SaveChallengeRecord>();
		var ended = false;
		for ( var index = 0; index < Slots; index++ )
		{
			var start = reader.Position;
			var type = reader.I32();
			var record = new SaveChallengeRecord( start, type, reader.I32(), reader.I32(), reader.U16(), reader.U16(), reader.I32(), reader.I32(), reader.I32(),
				reader.U8() != 0, false );
			var offered = reader.I32();
			var lost = reader.I32();
			var declined = reader.I32();
			var won = reader.U8();
			var independent = reader.U8();
			var followupThing = reader.U16();
			if ( type == 0 )
			{
				ended = true;
				continue;
			}
			if ( ended )
				throw new InvalidDataException( $"Save challenge slot {index} is used after an unused slot." );
			if ( payload[start + 28] > 1 || independent > 1 || won > 1 )
				throw new InvalidDataException( $"Save challenge slot {index} has non-boolean flags {payload[start + 28]}/{independent}/{won}." );
			used.Add( record with { Independent = independent != 0, Offered = offered, Lost = lost, Declined = declined, Won = won != 0, FollowupThing = followupThing } );
		}
		return new SaveChallengeSystem( thing.Id, thingBase, used, reader.I32(), reader.I64(), reader.U8(), reader.U8(), reader.U8(), reader.I32(), reader.U16() );
	}
}
