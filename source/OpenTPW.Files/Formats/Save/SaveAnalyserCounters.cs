using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// The park analyser's three misbehaving-kids words as the original saves them (<c>mMisbehavingKids</c>, <c>mMisbehavingKidsThatGotAwayWithIt</c> and <c>mMisbehavingKidsEver</c>, park <c>+0x21BFC</c>,
/// <c>+0x21C00</c> and <c>+0x21C04</c>): the guard chases of the current month, the abandoned ones of it and all of them. They are the three 4-byte words that precede <c>mLifetimeVisitors</c> and the six stamps in the
/// serializer's order, so the same prefix checks as <see cref="SaveAnalyserStamps"/> apply.
/// </summary>
// [BIN:TPW-EXE:0x004C5D70 analyser serializer] the counters mMisbehavingKids, mMisbehavingKidsThatGotAwayWithIt, mMisbehavingKidsEver and mLifetimeVisitors are four raw words between the byte rings and the stamps (read back at 0x004C6A4F, 0x004C6A74, 0x004C6A9E in the load direction and 0x004C7139, 0x004C7163, 0x004C718D in the save direction); only a guard's chase writes them (0x004C74F0)
public readonly record struct SaveAnalyserCounters( int MisbehavingKids, int GotAway, int Ever )
{
	private const int Words = 4;

	/// <summary>Reads the three words of the analyser thing; throws as <see cref="SaveAnalyserStamps.Read"/> does for a body that lacks the stamps.</summary>
	public static SaveAnalyserCounters Read( ReadOnlySpan<byte> payload, SaveThing thing )
	{
		// the stamps' checks cover the byte rings and the body's bounds
		_ = SaveAnalyserStamps.Read( payload, thing );
		var start = thing.BodyOffset + SaveAnalyserStamps.BodyOffset - Words * 4;
		return new SaveAnalyserCounters( BinaryPrimitives.ReadInt32LittleEndian( payload[start..] ), BinaryPrimitives.ReadInt32LittleEndian( payload[(start + 4)..] ),
			BinaryPrimitives.ReadInt32LittleEndian( payload[(start + 8)..] ) );
	}
}
