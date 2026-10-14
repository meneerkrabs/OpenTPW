namespace OpenTPW;

public static partial class Mp2Decoder
{
	// ISO/IEC11172-3 §2.4.2.3 and AnnexB.2a-d, numeric standard facts only.
	private static readonly int[] Mpeg1LayerTwoBitrates = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 };
	private static readonly int[] HighFirst = { 0, 1, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };
	private static readonly int[] HighMiddle = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 17 };
	private static readonly int[] HighThree = { 0, 1, 2, 3, 4, 5, 6, 17 };
	private static readonly int[] HighTwo = { 0, 1, 2, 17 };
	private static readonly int[] LowFour = { 0, 1, 2, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
	private static readonly int[] LowThree = { 0, 1, 2, 4, 5, 6, 7, 8 };
	private static readonly (int Count, int Bits, int[] Classes)[] Mpeg1TableA =
		{ (3,4,HighFirst), (8,4,HighMiddle), (12,3,HighThree), (4,2,HighTwo) };
	private static readonly (int Count, int Bits, int[] Classes)[] Mpeg1TableB =
		{ (3,4,HighFirst), (8,4,HighMiddle), (12,3,HighThree), (7,2,HighTwo) };
	private static readonly (int Count, int Bits, int[] Classes)[] Mpeg1TableC =
		{ (2,4,LowFour), (6,3,LowThree) };
	private static readonly (int Count, int Bits, int[] Classes)[] Mpeg1TableD =
		{ (2,4,LowFour), (10,3,LowThree) };

	private static (int Count, int Bits, int[] Classes)[] LayerTwoAllocation( Header header )
	{
		if ( header.Version == 2 ) return AllocationTable;
		var perChannel = header.Bitrate / header.Channels;
		if ( perChannel <= 48 ) return header.SampleRate == 32000 ? Mpeg1TableD : Mpeg1TableC;
		return header.SampleRate == 48000 || perChannel <= 80 ? Mpeg1TableA : Mpeg1TableB;
	}
}
