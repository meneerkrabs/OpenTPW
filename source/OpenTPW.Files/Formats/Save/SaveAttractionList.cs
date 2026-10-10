using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>Game-time stamp stored in each attraction record (year, month, day, hour, minute, second).</summary>
public readonly record struct SaveTimestamp( int Year, int Month, int Day, int Hour, int Minute, int Second );

/// <summary>
/// One class-3 object (ride, shop, sideshow, feature or fixed item) from the thing list in the save's
/// untagged prefix (see docs/TPWS-PAYLOAD.md, "Attraction records"). The field order and sizes follow
/// the original object serializer; names in the documentation are the original field-name strings.
/// <see cref="PositionX"/>/<see cref="PositionY"/> are in 1/256 cell units; for buildable objects the cell
/// (<c>Position / 256</c>) equals the SYSG record cell, fixed items store 128, 128.
/// <see cref="StateOfRepair"/>, <see cref="LifeGauge"/> and <see cref="Gauge48"/> are the three floats the
/// original saves as whole numbers 0–255. <see cref="TotalCosts"/>/<see cref="TotalTakings"/> are read as signed 32-bit
/// values; their sign convention is unverified. The original ride update lowers <see cref="StateOfRepair"/> by wear
/// and lowers <see cref="LifeGauge"/> by wear and per breakdown (docs/reverse/RIDE-WEAR.md).
/// </summary>
public readonly record struct SaveAttraction(
	int Offset, int Handle, int InfoId, int PositionX, int PositionY, int Angle, SaveTimestamp Timestamp,
	uint MeshInstanceId, string NameLine1, string NameLine2, int State, int OperatingCapacity, int OperatingDuration,
	uint OperatingSpeed, int PricePerUse, int QueueSizeInCells, uint CustomerCount, uint WalkAwayCount,
	int TotalCosts, int TotalTakings, float Gauge48, float LifeGauge, float StateOfRepair, int UpgradeLevel, int Length )
{
	public int CellX => PositionX >> 8;
	public int CellY => PositionY >> 8;
}

/// <summary>
/// Signature scan of the untagged payload prefix for <see cref="SaveAttraction"/> records. The thing list
/// stores each object as <c>u32 handle, u32 class, body</c>; only class 3 is decoded and other classes are
/// skipped by the scan. A candidate must parse completely within the prefix and pass every bound below,
/// so callers get no partial records. One fixture (jungle Easymode.TPWI) supports this layout.
/// </summary>
public static class SaveAttractionList
{
	public const uint AttractionClass = 3;
	public const int EnvelopeBytes = 8;
	public const int NameCharacters = 33;
	public const int MaximumRingEntries = 31;
	public const int MaximumPricePerUse = 500;
	public const int MaximumUpgradeLevel = 3;
	public const float MaximumGauge = 255f;

	public static IReadOnlyList<SaveAttraction> Parse( ReadOnlySpan<byte> payload, SavePayloadLayout layout )
	{
		ArgumentNullException.ThrowIfNull( layout );
		if ( layout.PayloadLength != payload.Length )
			throw new ArgumentException( "Layout does not describe this payload.", nameof( layout ) );
		var prefix = payload[..layout.PrefixLength];
		var result = new List<SaveAttraction>();
		for ( var offset = EnvelopeBytes; offset < prefix.Length; offset++ )
		{
			if ( U32( prefix, offset - 4 ) != AttractionClass )
				continue;
			var handle = U32( prefix, offset - EnvelopeBytes );
			if ( handle == 0 || handle > ushort.MaxValue || !TryParseBody( prefix, offset, (int)handle, out var attraction ) )
				continue;
			result.Add( attraction );
			offset += attraction.Length - 1;
		}
		return result.AsReadOnly();
	}

	private static bool TryParseBody( ReadOnlySpan<byte> data, int start, int handle, out SaveAttraction attraction )
	{
		attraction = default;
		var reader = new FieldReader( data, start );
		// Base object: position X, position Y and two further 16-bit fields.
		if ( !reader.U16( out var positionX ) || !reader.U16( out var positionY ) || !reader.Skip( 4 ) )
			return false;
		if ( !reader.U32( out var angle ) || !reader.U16( out var infoId ) || infoId == 0 || angle % 90 != 0 || angle >= 360 )
			return false;
		// Year, month, day, hour, minute, second, then two opaque words that are not bounded.
		var stamp = new uint[8];
		for ( var index = 0; index < stamp.Length; index++ )
			if ( !reader.U32( out stamp[index] ) )
				return false;
		if ( stamp[0] is < 1990 or > 2100 || stamp[1] is < 1 or > 12 || stamp[2] is < 1 or > 31
			|| stamp[3] > 23 || stamp[4] > 59 || stamp[5] > 59 )
			return false;
		var timestamp = new SaveTimestamp( (int)stamp[0], (int)stamp[1], (int)stamp[2], (int)stamp[3], (int)stamp[4], (int)stamp[5] );
		if ( !reader.U32( out var meshInstance ) || !reader.Skip( 2 ) )
			return false;
		var line1 = new char[NameCharacters];
		var line2 = new char[NameCharacters];
		for ( var index = 0; index < NameCharacters; index++ )
			if ( !reader.U16( out var first ) || !reader.U16( out var second ) )
				return false;
			else
				(line1[index], line2[index]) = ((char)first, (char)second);
		// Script and track handles, then mState.
		if ( !reader.Skip( 8 ) || !reader.U32( out var state ) || state > ushort.MaxValue )
			return false;
		// mTopLeft, mEntryPos, mNext, mAssignedStaffMember, mBackOfQueue, mCanLoad, mExitPos, mFirstInQ,
		// mIsTrackRideValid, mUpgradeParent.
		if ( !reader.Skip( 10 + 4 + 4 + 4 + 2 ) )
			return false;
		if ( !SkipRing( ref reader ) || !SkipRing( ref reader ) || !reader.U32( out var customers )
			|| !SkipRing( ref reader ) || !reader.U32( out var walkAways )
			|| !SkipRing( ref reader ) || !SkipRing( ref reader ) || !SkipRing( ref reader ) )
			return false;
		if ( !reader.U8( out var capacity ) || !reader.U8( out var duration ) || !reader.U32( out var speed ) || !reader.Skip( 2 ) )
			return false;
		// mCostOfGoods, mQualityOfGoods, mChanceOfWinning, then mPricePerUse (clamped to 0..500 by the original loader).
		if ( !reader.Skip( 12 ) || !reader.U32( out var price ) || price > MaximumPricePerUse
			|| !reader.Skip( 4 ) || !reader.U32( out var queueCells ) || queueCells > ushort.MaxValue )
			return false;
		if ( !reader.F32( out var gauge48 ) || !reader.F32( out var lifeGauge ) || !reader.F32( out var repair )
			|| !IsGauge( gauge48 ) || !IsGauge( lifeGauge ) || !IsGauge( repair ) )
			return false;
		// mRequestedService, mTimeMarkedForMaintenance, then totals, mUpgradeBalloonSprite and mUpgradeLevel.
		if ( !reader.Skip( 8 ) || !reader.U32( out var costs ) || !reader.U32( out var takings ) || !reader.Skip( 4 )
			|| !reader.U8( out var level ) || level > MaximumUpgradeLevel )
			return false;
		attraction = new SaveAttraction( start, handle, infoId, positionX, positionY, (int)angle, timestamp,
			meshInstance, Text( line1 ), Text( line2 ), (int)state, capacity, duration, speed, (int)price, (int)queueCells,
			customers, walkAways, unchecked((int)costs), unchecked((int)takings), gauge48, lifeGauge, repair, level,
			reader.Position - start );
		return true;
	}

	/// <summary>History buffer: u32 head, u32 count, u8 flag, u32 first, then <c>count</c> u32 entries.</summary>
	private static bool SkipRing( ref FieldReader reader )
	{
		if ( !reader.U32( out var head ) || !reader.U32( out var count ) || !reader.U8( out var flag ) || !reader.Skip( 4 ) )
			return false;
		return head <= MaximumRingEntries && count <= MaximumRingEntries && flag <= 1 && reader.Skip( (int)count * 4 );
	}

	private static bool IsGauge( float value ) => float.IsFinite( value ) && value >= 0f && value <= MaximumGauge;

	private static string Text( char[] characters )
	{
		var end = Array.IndexOf( characters, '\0' );
		return new string( characters, 0, end < 0 ? characters.Length : end );
	}

	private static uint U32( ReadOnlySpan<byte> data, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( data[offset..] );

	private ref struct FieldReader
	{
		private readonly ReadOnlySpan<byte> data;

		public FieldReader( ReadOnlySpan<byte> data, int position )
		{
			this.data = data;
			Position = position;
		}

		public int Position { get; private set; }

		public bool Skip( int count )
		{
			if ( count < 0 || data.Length - Position < count )
				return false;
			Position += count;
			return true;
		}

		public bool U8( out byte value )
		{
			value = 0;
			if ( data.Length - Position < 1 )
				return false;
			value = data[Position++];
			return true;
		}

		public bool U16( out ushort value )
		{
			value = 0;
			if ( data.Length - Position < 2 )
				return false;
			value = BinaryPrimitives.ReadUInt16LittleEndian( data[Position..] );
			Position += 2;
			return true;
		}

		public bool U32( out uint value )
		{
			value = 0;
			if ( data.Length - Position < 4 )
				return false;
			value = BinaryPrimitives.ReadUInt32LittleEndian( data[Position..] );
			Position += 4;
			return true;
		}

		public bool F32( out float value )
		{
			value = 0;
			if ( data.Length - Position < 4 )
				return false;
			value = BinaryPrimitives.ReadSingleLittleEndian( data[Position..] );
			Position += 4;
			return true;
		}
	}
}
