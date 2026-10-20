using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// The park's ring of the last arrival group sizes as the TPW analyser saves it: the five-word ring at park <c>+0x216D8</c>, serialized after the recent-stays ring by the analyser serializer
/// (<c>0x4C5D70</c>, ring serializer <c>0x4CE660</c>): current entry, entry count, wrapped byte, scratch word and the five words. Its offset follows the analyser prefix that
/// <c>SaveParkStatistics.Read</c> validates, so call that first (as <c>OriginalParkImport</c> does); this reader checks only its own fields and bounds.
/// </summary>
public static class SaveArrivalRing
{
	public const int Capacity = 5, Bytes = 13 + Capacity * 4;
	public const int BodyOffset = SaveParkStatistics.RecentStaysOffset + 13 + SaveParkStatistics.RecentStayCapacity * 4;

	/// <summary>
	/// The saved group sizes, the oldest first: slots <c>current + 1</c> to the last and then 0 to <c>current</c> once the ring has wrapped, else slots 0 to <c>current</c> (none for the empty ring, current -1).
	/// Throws for a ring that is not the five-entry shape.
	/// </summary>
	// [BIN:TPW-EXE:0x4CE660 ring serializer] the order of the fields read at +0x18 (mCurrentEntry, 4 bytes, strings 0x759A80), +0x1C (mNumEntries, 4, 0x759A74), +0x20 (mWrappedAround, 1 byte, 0x759A64), the scratch word mTemp (4, 0x759A5C) and the entries mData (4 each, 0x759A50); the push 0x4CF42A steps the index, wraps it to 0 with the flag and stores the size
	public static IReadOnlyList<int> Read( ReadOnlySpan<byte> payload, SaveThing thing )
	{
		if ( thing.Model != SaveThingModel.ParkAnalyser || thing.BodyOffset < 0 || thing.BodyLength < BodyOffset + Bytes
			|| thing.BodyOffset > payload.Length || thing.BodyLength > payload.Length - thing.BodyOffset )
			throw new InvalidDataException( "The referenced TPW park analyser does not contain the arrival ring." );
		var bytes = payload.Slice( thing.BodyOffset + BodyOffset, Bytes ).ToArray();
		var current = BinaryPrimitives.ReadInt32LittleEndian( bytes );
		var wrapped = bytes[8];
		if ( BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( 4 ) ) != Capacity || current < -1 || current >= Capacity || wrapped > 1 || current == -1 && wrapped != 0 )
			throw new InvalidDataException( "The TPW analyser arrival ring has an invalid capacity, head or wrapped flag." );
		int Word( int slot ) => BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( 13 + slot * 4 ) );
		var sizes = new List<int>();
		if ( wrapped != 0 )
			for ( var slot = current + 1; slot < Capacity; slot++ )
				sizes.Add( Word( slot ) );
		for ( var slot = 0; slot <= current; slot++ )
			sizes.Add( Word( slot ) );
		return sizes;
	}
}
