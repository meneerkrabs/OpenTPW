namespace OpenTPW;

/// <summary>Base thing serializer 0xfa808: <c>mX, mY, mMapChild, mMapParent</c>, four u16 fields.</summary>
public readonly record struct SaveThingBase( ushort X, ushort Y, ushort MapChild, ushort MapParent )
{
	public const int Bytes = 8;
}

/// <summary>Game-time stamp stored in each attraction record (year, month, day, hour, minute, second).</summary>
public readonly record struct SaveTimestamp( int Year, int Month, int Day, int Hour, int Minute, int Second );

/// <summary>
/// One model-3 thing (ride, shop, sideshow, feature, gate, traffic lights or bus): serializer 0xdaf04
/// (docs/TPWS-PAYLOAD.md, "Attraction records"). <see cref="Offset"/> is the body offset, <see cref="Length"/>
/// the body length. <see cref="PositionX"/>/<see cref="PositionY"/> are the base thing's X/Y in 1/256 cell units;
/// for buildable objects the cell equals the RideSystem record cell, fixed items store 128, 128.
/// <see cref="StateOfRepair"/>, <see cref="LifeGauge"/> and <see cref="Gauge48"/> are the three floats the original
/// saves as whole numbers 0–255. <see cref="TotalCosts"/>/<see cref="TotalTakings"/> are read as signed 32-bit
/// values; their sign convention is unverified. Kept raw (meaning not traced): the timestamp's two trailing words,
/// <c>mFlags</c>, the script/track handles, the 24 bytes from <c>mTopLeft</c> to <c>mUpgradeParent</c>, the six history
/// buffers, <c>mPersonBeingLoaded</c>, <c>mAmountOfSpecialIngredient</c>, <c>mRequestedService</c>,
/// <c>mTimeMarkedForMaintenance</c> and <c>mUpgradeBalloonSprite</c>; <see cref="State"/> is the raw state enum.
/// </summary>
public readonly record struct SaveAttraction(
	int Offset, int Id, SaveThingBase Base, int InfoId, int Angle, SaveTimestamp Timestamp,
	uint MeshInstanceId, string NameLine1, string NameLine2, int State, int OperatingCapacity, int OperatingDuration,
	uint OperatingSpeed, int CostOfGoods, int QualityOfGoods, int ChanceOfWinning, int PricePerUse, int QueueSizeInCells,
	uint CustomerCount, uint WalkAwayCount, int TotalCosts, int TotalTakings, float Gauge48, float LifeGauge, float StateOfRepair,
	int UpgradeLevel, int Length )
{
	public const int NameCharacters = 33;
	/// <summary>History buffers hold at most this many entries (head and count are bounded by it).</summary>
	public const int MaximumRingEntries = 31;
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
		var stamp = new uint[8];
		for ( var index = 0; index < stamp.Length; index++ )
			stamp[index] = reader.U32();
		if ( stamp[1] is < 1 or > 12 || stamp[2] is < 1 or > 31 || stamp[3] > 23 || stamp[4] > 59 || stamp[5] > 59 || stamp[0] > 9999 )
			throw new InvalidDataException( $"Save attraction thing {id} has an invalid game-time stamp {stamp[0]}-{stamp[1]}-{stamp[2]} {stamp[3]}:{stamp[4]}:{stamp[5]}." );
		var timestamp = new SaveTimestamp( (int)stamp[0], (int)stamp[1], (int)stamp[2], (int)stamp[3], (int)stamp[4], (int)stamp[5] );
		var meshInstance = reader.U32();
		reader.Skip( 2 );
		var line1 = new char[NameCharacters];
		var line2 = new char[NameCharacters];
		for ( var index = 0; index < NameCharacters; index++ )
			(line1[index], line2[index]) = ((char)reader.U16(), (char)reader.U16());
		// mRideScriptHandle, mTrackRideHandle, then mState.
		reader.Skip( 8 );
		var state = reader.U32();
		if ( state > ushort.MaxValue )
			throw new InvalidDataException( $"Save attraction thing {id} has state {state}." );
		reader.Skip( 24 );
		SkipHistory( ref reader, id );
		SkipHistory( ref reader, id );
		var customers = reader.U32();
		SkipHistory( ref reader, id );
		var walkAways = reader.U32();
		SkipHistory( ref reader, id );
		SkipHistory( ref reader, id );
		SkipHistory( ref reader, id );
		var capacity = reader.U8();
		var duration = reader.U8();
		var speed = reader.U32();
		reader.Skip( 2 );
		var costOfGoods = reader.I32();
		var quality = reader.I32();
		var chance = reader.I32();
		var price = reader.U32();
		if ( price > MaximumPricePerUse )
			throw new InvalidDataException( $"Save attraction thing {id} has price {price}; the original loader allows 0–{MaximumPricePerUse}." );
		reader.Skip( 4 );
		var queueCells = reader.U32();
		if ( queueCells > ushort.MaxValue )
			throw new InvalidDataException( $"Save attraction thing {id} has {queueCells} queue cells." );
		var gauge48 = reader.F32();
		var lifeGauge = reader.F32();
		var repair = reader.F32();
		if ( !IsGauge( gauge48 ) || !IsGauge( lifeGauge ) || !IsGauge( repair ) )
			throw new InvalidDataException( $"Save attraction thing {id} has a gauge outside 0–{MaximumGauge}." );
		reader.Skip( 8 );
		var costs = reader.I32();
		var takings = reader.I32();
		reader.Skip( 4 );
		var level = reader.U8();
		if ( level > MaximumUpgradeLevel )
			throw new InvalidDataException( $"Save attraction thing {id} has upgrade level {level}." );
		return new SaveAttraction( offset, id, thingBase, infoId, (int)angle, timestamp, meshInstance, Text( line1 ), Text( line2 ),
			(int)state, capacity, duration, speed, costOfGoods, quality, chance, (int)price, (int)queueCells, customers, walkAways,
			costs, takings, gauge48, lifeGauge, repair, level, reader.Position - offset );
	}

	/// <summary>History buffer: u32 head, u32 count, u8 flag, u32 first, then <c>count</c> u32 entries.</summary>
	private static void SkipHistory( ref SaveFieldReader reader, int id )
	{
		var head = reader.U32();
		var count = reader.U32();
		var flag = reader.U8();
		reader.Skip( 4 );
		if ( head > MaximumRingEntries || count > MaximumRingEntries || flag > 1 )
			throw new InvalidDataException( $"Save attraction thing {id} has a history buffer with head {head}, count {count}, flag {flag}." );
		reader.Skip( (int)count * 4 );
	}

	private static bool IsGauge( float value ) => float.IsFinite( value ) && value >= 0f && value <= MaximumGauge;

	private static string Text( char[] characters )
	{
		var end = Array.IndexOf( characters, '\0' );
		return new string( characters, 0, end < 0 ? characters.Length : end );
	}
}

/// <summary>
/// Staff suffix 0xf2c28 (105 bytes) of models 4–8. <see cref="Happiness"/>/<see cref="Energy"/> are the saved
/// floats (the writer truncates the runtime value to its low byte first). <see cref="GradePercentage"/> is the
/// percentage through the grade. <see cref="Name"/> is stored inline (33 UTF-16 units); there is no name index.
/// <see cref="HiredTimestamp"/> counts 100 ns since 1601-01-01 like the calendar. Raw (meaning not traced):
/// <see cref="Jobs"/>, <see cref="PatrolRegion"/>, <see cref="RestAreaId"/>, the <see cref="State"/> enum and
/// <see cref="IdleTick"/>.
/// </summary>
public readonly record struct SaveStaffRecord( int Grade, float Happiness, uint Jobs, string Name, uint PatrolRegion, int GradePercentage,
	ushort RestAreaId, uint State, uint IdleTick, long HiredTimestamp, float Energy );

/// <summary>
/// A staff thing (models 4 Mechanic, 5 Handyman, 6 Entertainer, 7 Guard, 8 Researcher): base, the 382 opaque
/// person bytes after the base (sprite, scalars, navigation, thoughts), the staff suffix and the role suffix.
/// <see cref="RoleNextId"/> is the role's linked-list next ID (the role suffix's last u16, after <c>mFirst&lt;Role&gt;</c>);
/// the other role-suffix bytes are kept in <see cref="RoleBytes"/> (mechanic: repair 4, object 2; handyman: litter
/// cell 2, start 4, toilet 2; entertainer and researcher: start tick 4; guard: perpetrator 2, prosecution 4).
/// </summary>
public sealed record SaveStaff( int Id, SaveThingModel Model, SaveThingBase Base, byte[] PersonBytes, SaveStaffRecord Staff, byte[] RoleBytes, ushort RoleNextId )
{
	public static SaveStaff Read( ReadOnlySpan<byte> payload, SaveThing thing )
	{
		if ( thing.Model is < SaveThingModel.Mechanic or > SaveThingModel.Researcher )
			throw new ArgumentException( $"Thing {thing.Id} is a {thing.Model}, not staff.", nameof( thing ) );
		var reader = new SaveFieldReader( payload[..thing.End], thing.BodyOffset, $"{thing.Model} thing {thing.Id}" );
		var thingBase = reader.Base();
		var person = reader.Bytes( SaveThingChain.PersonBytes - SaveThingBase.Bytes );
		var staff = new SaveStaffRecord( reader.I32(), reader.F32(), reader.U32(), reader.Utf16( 33 ), reader.U32(), reader.U8(),
			reader.U16(), reader.U32(), reader.U32(), reader.I64(), reader.F32() );
		if ( staff.Grade is < 0 or > 4 || !float.IsFinite( staff.Happiness ) || !float.IsFinite( staff.Energy ) || staff.GradePercentage > 100 )
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

/// <summary>
/// Model 14, the research lab (0xf0930): base, 256 project records of 16 bytes, five group records of 13 bytes and
/// 4 + 4 + 4 + 1 trailing bytes. The record fields (<c>mObjId</c>, <c>mCat</c>, group, upgrade level, cost, done;
/// group effort, output, current project, highest group) have no pinned widths, so the records stay raw.
/// </summary>
public sealed record SaveResearchLab( int Id, SaveThingBase Base, IReadOnlyList<byte[]> Projects, IReadOnlyList<byte[]> Groups, byte[] Tail )
{
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
}

/// <summary>
/// One 45-byte challenge slot (field order differs from <c>Challenges.sam</c>): <c>i32 type, time, value, u16 object,
/// u16 object2, i32 staff type, prize, follow-up type, u8 check at end only</c>, 13 raw bytes, <c>u8 independent</c>,
/// two raw bytes.
/// </summary>
public readonly record struct SaveChallengeRecord( int Offset, int Type, int TargetTime, int TargetValue, int TargetObject, int TargetObject2,
	int TargetStaffType, int Prize, int FollowupType, bool CheckAtEndOnly, bool Independent );

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
			reader.Skip( 13 );
			var independent = reader.U8();
			reader.Skip( 2 );
			if ( type == 0 )
			{
				ended = true;
				continue;
			}
			if ( ended )
				throw new InvalidDataException( $"Save challenge slot {index} is used after an unused slot." );
			if ( payload[start + 28] > 1 || independent > 1 )
				throw new InvalidDataException( $"Save challenge slot {index} has non-boolean flags {payload[start + 28]}/{independent}." );
			used.Add( record with { Independent = independent != 0 } );
		}
		return new SaveChallengeSystem( thing.Id, thingBase, used, reader.I32(), reader.I64(), reader.U8(), reader.U8(), reader.U8(), reader.I32(), reader.U16() );
	}
}
