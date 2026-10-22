using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// The six "last time" stamps of the TPW park analyser as it saves them (park <c>+0x21C10</c> to <c>+0x21C38</c>), each the park time of a moment: the calendar's <c>FunnyTimeStart</c>
/// plus floor(turn x rate / 4) x 10^7 (<c>0x4F8690</c>), 100 ns since 1601-01-01. They follow the twenty byte rings and the four counters after the arrival ring, and the three
/// extremes <c>mMostPaidForTicket</c>, <c>mLargestBusLoad</c> and <c>mLongestStay</c> end the body. Their offset follows the analyser prefix that <c>SaveParkStatistics.Read</c>
/// and <c>SaveArrivalRing.Read</c> validate, so call those first (as <c>OriginalParkImport</c> does); this reader checks the byte rings it steps over and its own bounds, and takes the
/// stamps as they are, as the original's load does.
/// </summary>
// [BIN:TPW-EXE:0x4C5D70 analyser serializer] after the arrival ring: twenty byte rings from +0x216FC (0x4CEA00: current, entries, wrapped, temp u8 and 50 u8 entries), the 4-byte counters mMisbehavingKids (+0x21BFC), mMisbehavingKidsThatGotAwayWithIt (+0x21C00), mMisbehavingKidsEver (+0x21C04) and mLifetimeVisitors (+0x21C08), then the six 8-byte stamps mLastStrike (+0x21C10), mLastPuke (+0x21C18), mLastPrank (+0x21C20), mParkLastOpened (+0x21C28), mLastObjectBuilt (+0x21C30, written at 0x4C6B9A) and mLastStaffHired (+0x21C38, 0x4C6BC4) through 0x4CEDE0, raw, and the counters mMostPaidForTicket (+0x21C40), mLargestBusLoad (+0x21C44) and mLongestStay (+0x21C48)
// [BIN:TPW-EXE:0x510240 stamp load] the load branch of the serializer reads the six stamps back in the same order, eight raw bytes each (0x4C71EE-0x4C72C0: mLastObjectBuilt at 0x4C7289, mLastStaffHired at 0x4C72B3)
public readonly record struct SaveAnalyserStamps( long LastStrike, long LastPuke, long LastPrank, long ParkLastOpened, long LastObjectBuilt, long LastStaffHired )
{
	public const int Count = 6, Bytes = Count * 8;
	/// <summary>The twenty byte rings of 50 entries between the arrival ring and the counters.</summary>
	public const int ByteRings = 20, ByteRingCapacity = 50, ByteRingBytes = 10 + ByteRingCapacity;
	public const int BodyOffset = SaveArrivalRing.BodyOffset + SaveArrivalRing.Bytes + ByteRings * ByteRingBytes + 4 * 4;

	/// <summary>Reads the six stamps of the analyser thing; throws for a body too short to hold them or a byte ring before them that is not the 50-entry shape.</summary>
	// [BIN:TPW-EXE:0x4CEA00 byte ring serializer] mCurrentEntry (+0x34) and mNumEntries (+0x38), 4 bytes each, mWrappedAround (+0x3C) and mTemp (+0) a byte each, then mNumEntries entries of a byte (from +1): a ring of another size moves every field after it
	public static SaveAnalyserStamps Read( ReadOnlySpan<byte> payload, SaveThing thing )
	{
		if ( thing.Model != SaveThingModel.ParkAnalyser || thing.BodyOffset < 0 || thing.BodyLength < BodyOffset + Bytes
			|| thing.BodyOffset > payload.Length || thing.BodyLength > payload.Length - thing.BodyOffset )
			throw new InvalidDataException( "The referenced TPW park analyser does not contain its six time stamps." );
		for ( var ring = 0; ring < ByteRings; ring++ )
			if ( BinaryPrimitives.ReadInt32LittleEndian( payload[(thing.BodyOffset + SaveArrivalRing.BodyOffset + SaveArrivalRing.Bytes + ring * ByteRingBytes + 4)..] ) != ByteRingCapacity )
				throw new InvalidDataException( $"TPW analyser byte ring {ring} has an unsupported capacity." );
		var bytes = payload.Slice( thing.BodyOffset + BodyOffset, Bytes );
		return new SaveAnalyserStamps( BinaryPrimitives.ReadInt64LittleEndian( bytes ), BinaryPrimitives.ReadInt64LittleEndian( bytes[8..] ),
			BinaryPrimitives.ReadInt64LittleEndian( bytes[16..] ), BinaryPrimitives.ReadInt64LittleEndian( bytes[24..] ),
			BinaryPrimitives.ReadInt64LittleEndian( bytes[32..] ), BinaryPrimitives.ReadInt64LittleEndian( bytes[40..] ) );
	}
}
