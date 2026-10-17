namespace OpenTPW;

/// <summary>
/// One model-3 thing of a Theme Park Inc World thing list: the attraction serializer the model-3 case of the thing loader (0x64cc9d) calls (0x5dc430), in its load order.
/// The body is 312 bytes, plus the 19 history rings (5,263 bytes) when <see cref="HasHistory"/> is set. The fields shared with the Theme Park World attraction
/// (<see cref="SaveAttraction"/>) keep its names; Theme Park Inc adds <see cref="RunningCost"/>, <see cref="PortionSize"/>, <see cref="GoneFirstPersonFlag"/>, the photo
/// booth words, the prebuilt-track flags, the promotion and survey fields and the weather closure, and writes the costs, takings and customer counts as history rings
/// instead of words (the ring names are not traced, so they stay in serializer order in <see cref="Histories"/>).
/// <see cref="Offset"/> is the body offset (after the 16-byte thing header), <see cref="Length"/> the body length.
/// </summary>
// [BIN:TPI-EXE:0x5dc430 attraction serializer] mAngle 4, mId 2, 8 x tv[t] 4, MeshInstanceID 4, mFlags 2, 33 x (mNameA[i] 2, mNameB[i] 2), mRideScriptHandle 4, mTrackRideHandle 4, mState 4, mTopLeft 2, mEntryPos 2, mNext 2, mAssignedStaffMember 2, mBackOfQueue 2, mCanLoad 4, mExitPos 2, mFirstInQ 2, mIsTrackRideValid 4, mUpgradeParent 2, m_runningCost 4, mNumWalkAways 4, mOperatingCapacity 1, mOperatingDuration 1, mOperatingSpeed 4, mPersonBeingLoaded 2, mCostOfGoods 4, mQualityOfGoods 4, mChanceOfWinning 4, mPricePerUse 4, mAmountOfSpecialIngredient 4, mPortionSize 4, mGoneFirstPersonFlg 1, mQueueSizeInCells 4, three unnamed 4-byte gauges, mRequestedService 4, mTimeMarkedForMaintenance 4, mUpgradeBalloonSprite 4, mUpgradeLevel 1, mPhotoBooth 2, mBooby 2, mPrebuiltTrackOpen 1, mBeenAroundAtLeastAMonth 1, mPrebuiltTrackEdited 1, mPromotedAttraction 2, mTimeOfLastSurveyReset 8, mBeenActivated 1, mHasBeenOpenedAtSomePoint 1, m_bFlashUnconnectedExit 1, mClosedDueToWeather 1, HasHistory 1, then the history object 0x5ea020 when HasHistory
// [APPROX:SAVE-940] the three unnamed 4-byte words after mQueueSizeInCells are read as the gauges of the Theme Park World serializer (Gauge48, LifeGauge, StateOfRepair), and the 19 history rings are kept in serializer order without their meanings: the loader's log names none of them (one inline ring, then eleven through 0x5ea720 and six through 0x5eac40), and the prebuilt's rings are all empty (current entry 0 or -1, wrapped 0) — evidence needed: a Theme Park Inc save whose attractions have run, or the names of the history fields
public sealed record ThemeParkIncAttraction(
	int Offset, int Id, SaveThingBase Base, int InfoId, int Angle, SaveTimestamp Timestamp, uint MeshInstanceId, ushort Flags,
	string NameLine1, string NameLine2, uint RideScriptHandle, uint TrackRideHandle, uint State, ushort TopLeft, ushort EntryPos, ushort Next,
	ushort AssignedStaffMember, ushort BackOfQueue, uint CanLoad, ushort ExitPos, ushort FirstInQueue, uint IsTrackRideValid, ushort UpgradeParent,
	int RunningCost, uint WalkAwayCount, byte OperatingCapacity, byte OperatingDuration, uint OperatingSpeed, ushort PersonBeingLoaded,
	int CostOfGoods, int QualityOfGoods, int ChanceOfWinning, uint PricePerUse, int AmountOfSpecialIngredient, int PortionSize,
	bool GoneFirstPersonFlag, uint QueueSizeInCells, float Gauge48, float LifeGauge, float StateOfRepair, uint RequestedService,
	uint TimeMarkedForMaintenance, uint UpgradeBalloonSprite, byte UpgradeLevel, ushort PhotoBooth, ushort Booby, bool PrebuiltTrackOpen,
	bool BeenAroundAtLeastAMonth, bool PrebuiltTrackEdited, ushort PromotedAttraction, long TimeOfLastSurveyReset, bool BeenActivated,
	bool HasBeenOpenedAtSomePoint, bool FlashUnconnectedExit, bool ClosedDueToWeather, bool HasHistory,
	IReadOnlyList<SaveAttractionHistory> Histories, int Length )
{
	/// <summary>The body without history: base to <c>HasHistory</c>.</summary>
	public const int BodyBytes = 312;
	public const int NameCharacters = 33;
	/// <summary>The history object's rings: thirteen of 30 entries, then six of 144.</summary>
	public const int ShortRings = 13, LongRings = 6, ShortRingEntries = 30, LongRingEntries = 144;
	/// <summary>A ring: u32 current entry, u32 entry count, u8 wrapped, i32 running value, then the entries.</summary>
	public const int RingHeaderBytes = 13;
	public const int HistoryBytes = ShortRings * (RingHeaderBytes + 4 * ShortRingEntries) + LongRings * (RingHeaderBytes + 4 * LongRingEntries);
	public const int MaximumUpgradeLevel = 3;
	public const float MaximumGauge = 255f;

	public int PositionX => Base.X;
	public int PositionY => Base.Y;
	public int CellX => PositionX >> 8;
	public int CellY => PositionY >> 8;

	/// <summary>The body width at <paramref name="offset"/>: 312 bytes, plus the history when its flag byte is 1; throws on any other flag value or truncation.</summary>
	public static int MeasureBody( ReadOnlySpan<byte> data, int offset, int id )
	{
		if ( offset < 0 || data.Length - offset < BodyBytes )
			throw new InvalidDataException( $"Save attraction thing {id} is truncated at offset {offset} (needs {BodyBytes} more bytes, {Math.Max( 0, data.Length - offset )} remain)." );
		var flag = data[offset + BodyBytes - 1];
		if ( flag > 1 )
			throw new InvalidDataException( $"Save attraction thing {id} has HasHistory {flag}, not a 0/1 flag." );
		var length = BodyBytes + (flag != 0 ? HistoryBytes : 0);
		if ( data.Length - offset < length )
			throw new InvalidDataException( $"Save attraction thing {id} history crosses the end of the data (needs {length} bytes, {data.Length - offset} remain)." );
		return length;
	}

	/// <summary>Reads the attraction body at <paramref name="offset"/>; throws on truncation, an out-of-range field or a history ring that does not hold all its entries.</summary>
	public static ThemeParkIncAttraction Read( ReadOnlySpan<byte> data, int offset, int id )
	{
		var length = MeasureBody( data, offset, id );
		var reader = new SaveFieldReader( data[..(offset + length)], offset, $"Theme Park Inc attraction thing {id}" );
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
		var meshInstance = reader.U32();
		var flags = reader.U16();
		var line1 = new char[NameCharacters];
		var line2 = new char[NameCharacters];
		for ( var index = 0; index < NameCharacters; index++ )
			(line1[index], line2[index]) = ((char)reader.U16(), (char)reader.U16());
		var (scriptHandle, trackHandle, state) = (reader.U32(), reader.U32(), reader.U32());
		var (topLeft, entryPos, next, assigned, backOfQueue) = (reader.U16(), reader.U16(), reader.U16(), reader.U16(), reader.U16());
		var canLoad = reader.U32();
		var (exitPos, firstInQueue) = (reader.U16(), reader.U16());
		var trackValid = reader.U32();
		var upgradeParent = reader.U16();
		var runningCost = reader.I32();
		var walkAways = reader.U32();
		var (capacity, duration) = (reader.U8(), reader.U8());
		var speed = reader.U32();
		var personBeingLoaded = reader.U16();
		var (costOfGoods, quality, chance) = (reader.I32(), reader.I32(), reader.I32());
		var price = reader.U32();
		var ingredient = reader.I32();
		var portion = reader.I32();
		var goneFirst = reader.Flag( "mGoneFirstPersonFlg" );
		var queueCells = reader.U32();
		if ( queueCells > ushort.MaxValue )
			throw new InvalidDataException( $"Save attraction thing {id} has {queueCells} queue cells." );
		var (gauge48, lifeGauge, repair) = (reader.F32(), reader.F32(), reader.F32());
		if ( !IsGauge( gauge48 ) || !IsGauge( lifeGauge ) || !IsGauge( repair ) )
			throw new InvalidDataException( $"Save attraction thing {id} has a gauge outside 0–{MaximumGauge}." );
		var (requested, marked, balloon) = (reader.U32(), reader.U32(), reader.U32());
		var level = reader.U8();
		if ( level > MaximumUpgradeLevel )
			throw new InvalidDataException( $"Save attraction thing {id} has upgrade level {level}." );
		var (photoBooth, booby) = (reader.U16(), reader.U16());
		var prebuiltOpen = reader.Flag( "mPrebuiltTrackOpen" );
		var aroundMonth = reader.Flag( "mBeenAroundAtLeastAMonth" );
		var prebuiltEdited = reader.Flag( "mPrebuiltTrackEdited" );
		var promoted = reader.U16();
		var surveyReset = reader.I64();
		var activated = reader.Flag( "mBeenActivated" );
		var opened = reader.Flag( "mHasBeenOpenedAtSomePoint" );
		var flash = reader.Flag( "m_bFlashUnconnectedExit" );
		var closedWeather = reader.Flag( "mClosedDueToWeather" );
		var hasHistory = reader.Flag( "HasHistory" );
		var histories = new List<SaveAttractionHistory>();
		if ( hasHistory )
		{
			for ( var ring = 0; ring < ShortRings + LongRings; ring++ )
				histories.Add( ReadRing( ref reader, id, ring < ShortRings ? ShortRingEntries : LongRingEntries ) );
		}
		if ( reader.Position - offset != length )
			throw new InvalidDataException( $"Save attraction thing {id} read {reader.Position - offset} bytes, not the {length} its flags promise." );
		return new ThemeParkIncAttraction( offset, id, thingBase, infoId, (int)angle,
			new SaveTimestamp( (int)stamp[0], (int)stamp[1], (int)stamp[2], (int)stamp[3], (int)stamp[4], (int)stamp[5] ), meshInstance, flags,
			Text( line1 ), Text( line2 ), scriptHandle, trackHandle, state, topLeft, entryPos, next, assigned, backOfQueue, canLoad, exitPos, firstInQueue,
			trackValid, upgradeParent, runningCost, walkAways, capacity, duration, speed, personBeingLoaded, costOfGoods, quality, chance, price,
			ingredient, portion, goneFirst, queueCells, gauge48, lifeGauge, repair, requested, marked, balloon, level, photoBooth, booby, prebuiltOpen,
			aroundMonth, prebuiltEdited, promoted, surveyReset, activated, opened, flash, closedWeather, hasHistory, histories.AsReadOnly(), length );
	}

	private static SaveAttractionHistory ReadRing( ref SaveFieldReader reader, int id, int capacity )
	{
		var head = reader.U32();
		var count = reader.U32();
		var flag = reader.U8();
		var current = reader.I32();
		// The prebuilt's rings all store their full capacity as the entry count; a different count would need the loader's loop (by count or by capacity), which is not traced.
		if ( count != capacity || flag > 1 || head != uint.MaxValue && head >= capacity )
			throw new InvalidDataException( $"Save attraction thing {id} has a history ring with current entry {head}, count {count}, wrapped {flag}; expected {capacity} entries." );
		var data = new int[count];
		for ( var index = 0; index < data.Length; index++ )
			data[index] = reader.I32();
		return new SaveAttractionHistory( (int)head, flag != 0, current, data );
	}

	private static bool IsGauge( float value ) => float.IsFinite( value ) && value >= 0f && value <= MaximumGauge;

	private static string Text( char[] characters )
	{
		var end = Array.IndexOf( characters, '\0' );
		return new string( characters, 0, end < 0 ? characters.Length : end );
	}
}
