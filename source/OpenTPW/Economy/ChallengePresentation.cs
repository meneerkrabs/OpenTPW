namespace OpenTPW;

/// <summary>The four text-bearing challenge transitions, independent of advisor speech.</summary>
public enum ChallengePresentationKind { Offer, Progress, Success, Failure }

/// <summary>An immutable copy of the challenge values at the time its message is posted.</summary>
public readonly record struct ChallengePresentation( ChallengePresentationKind Kind, int Type, int Prize, int TargetTime, int TargetValue,
	int RawValue, int DaysLeft, int TargetObject, int TargetObject2, int TargetStaffType, string OfferText = "" );

/// <summary>Numeric presentation fields of a challenge tag; localized text stays in the installed string table.</summary>
public readonly record struct ChallengeMessageRecord( int Tag, int ReplacementTag, int Icon, int Entry, int Selector,
	int FirstAction, int SecondAction, int Lifetime )
{
	/// <summary>Challenge rows share the category receiver's default context, rather than a definition index.</summary>
	public (int Short, int Value) Context => (0, 0);
}

/// <summary>Challenge type dispatch and the complete offer, progress, success and failure tag metadata.</summary>
public static class ChallengeMessageRecords
{
	public const int NoText = 383;

	// The type dispatch is not in tag order. Type zero has no caption.
	// [BIN:TPW-EXE:0x4d3220 challenge tags] the four dispatch tables select related tags for types 1..33.
	private static readonly int[] progressTags =
	{
		383, 280, 281, 282, 283, 284, 285, 286, 287, 291, 292, 293, 297, 298, 300, 301, 303,
		288, 306, 307, 308, 309, 312, 289, 290, 296, 299, 295, 302, 304, 305, 310, 311, 294
	};

	// Normalized by tag here; the source table's physical row number is not its tag.
	// [BIN:TPW-EXE:0x50af40 tag lookup] lookup searches for the matching tag after the indexed probe.
	private static readonly ChallengeMessageRecord[] records =
	{
		new( 247, 383, 4, 192, 9, 26, 27, 90000 ),
		new( 248, 383, 4, 196, 9, 26, 27, 90000 ),
		new( 249, 383, 4, 200, 9, 26, 27, 90000 ),
		new( 250, 383, 4, 204, 9, 26, 27, 90000 ),
		new( 251, 383, 4, 208, 9, 26, 27, 90000 ),
		new( 252, 383, 4, 212, 9, 26, 27, 90000 ),
		new( 253, 383, 4, 216, 9, 26, 27, 90000 ),
		new( 254, 383, 4, 220, 9, 26, 27, 90000 ),
		new( 255, 383, 4, 224, 10, 26, 27, 90000 ),
		new( 256, 383, 4, 228, 11, 26, 27, 90000 ),
		new( 257, 383, 4, 232, 11, 26, 27, 90000 ),
		new( 258, 383, 4, 236, 9, 26, 27, 90000 ),
		new( 259, 383, 4, 240, 9, 26, 27, 90000 ),
		new( 260, 383, 4, 244, 9, 26, 27, 90000 ),
		new( 261, 383, 4, 248, 15, 26, 27, 90000 ),
		new( 262, 383, 4, 252, 12, 26, 27, 90000 ),
		new( 263, 383, 4, 256, 11, 26, 27, 90000 ),
		new( 264, 383, 4, 260, 9, 26, 27, 90000 ),
		new( 265, 383, 4, 264, 9, 26, 27, 90000 ),
		new( 266, 383, 4, 268, 11, 26, 27, 90000 ),
		new( 267, 383, 4, 276, 9, 26, 27, 90000 ),
		new( 268, 383, 4, 280, 9, 26, 27, 90000 ),
		new( 269, 383, 4, 284, 10, 26, 27, 90000 ),
		new( 270, 383, 4, 288, 9, 26, 27, 90000 ),
		new( 271, 383, 4, 292, 13, 26, 27, 90000 ),
		new( 272, 383, 4, 296, 14, 26, 27, 90000 ),
		new( 273, 383, 4, 304, 10, 26, 27, 90000 ),
		new( 274, 306, 4, 308, 10, 26, 27, 90000 ),
		new( 275, 307, 4, 312, 10, 26, 27, 90000 ),
		new( 276, 383, 4, 316, 10, 26, 27, 90000 ),
		new( 277, 309, 4, 320, 14, 26, 27, 90000 ),
		new( 278, 383, 4, 324, 14, 26, 27, 90000 ),
		new( 279, 312, 4, 328, 10, 26, 27, 90000 ),
		new( 280, 280, 4, 193, 16, 0, 0, 0 ),
		new( 281, 281, 4, 197, 16, 0, 0, 0 ),
		new( 282, 282, 4, 201, 16, 0, 0, 0 ),
		new( 283, 283, 4, 205, 16, 0, 0, 0 ),
		new( 284, 284, 4, 209, 16, 0, 0, 0 ),
		new( 285, 285, 4, 213, 16, 0, 0, 0 ),
		new( 286, 286, 4, 217, 16, 0, 0, 0 ),
		new( 287, 287, 4, 221, 16, 0, 0, 0 ),
		new( 288, 288, 4, 225, 17, 0, 0, 0 ),
		new( 289, 289, 4, 229, 18, 0, 0, 0 ),
		new( 290, 290, 4, 233, 18, 0, 0, 0 ),
		new( 291, 291, 4, 237, 16, 0, 0, 0 ),
		new( 292, 292, 4, 241, 16, 0, 0, 0 ),
		new( 293, 293, 4, 245, 16, 0, 0, 0 ),
		new( 294, 294, 4, 249, 22, 0, 0, 0 ),
		new( 295, 295, 4, 253, 19, 0, 0, 0 ),
		new( 296, 296, 4, 257, 18, 0, 0, 0 ),
		new( 297, 297, 4, 261, 16, 0, 0, 0 ),
		new( 298, 298, 4, 265, 16, 0, 0, 0 ),
		new( 299, 299, 4, 269, 18, 0, 0, 0 ),
		new( 300, 300, 4, 277, 16, 0, 0, 0 ),
		new( 301, 301, 4, 281, 16, 0, 0, 0 ),
		new( 302, 302, 4, 285, 17, 0, 0, 0 ),
		new( 303, 303, 4, 289, 16, 0, 0, 0 ),
		new( 304, 304, 4, 293, 20, 0, 0, 0 ),
		new( 305, 305, 4, 297, 21, 0, 0, 0 ),
		new( 306, 306, 4, 305, 17, 0, 0, 0 ),
		new( 307, 307, 4, 309, 17, 0, 0, 0 ),
		new( 308, 308, 4, 313, 17, 0, 0, 0 ),
		new( 309, 309, 4, 317, 17, 0, 0, 0 ),
		new( 310, 310, 4, 321, 21, 0, 0, 0 ),
		new( 311, 311, 4, 325, 21, 0, 0, 0 ),
		new( 312, 312, 4, 329, 17, 0, 0, 0 ),
		new( 313, 280, 4, 194, 23, 0, 0, 30000 ),
		new( 314, 281, 4, 198, 23, 0, 0, 30000 ),
		new( 315, 282, 4, 202, 23, 0, 0, 30000 ),
		new( 316, 283, 4, 206, 23, 0, 0, 30000 ),
		new( 317, 284, 4, 210, 23, 0, 0, 30000 ),
		new( 318, 285, 4, 214, 23, 0, 0, 30000 ),
		new( 319, 286, 4, 218, 23, 0, 0, 30000 ),
		new( 320, 287, 4, 222, 23, 0, 0, 30000 ),
		new( 321, 288, 4, 226, 24, 0, 0, 30000 ),
		new( 322, 289, 4, 230, 25, 0, 0, 30000 ),
		new( 323, 290, 4, 234, 25, 0, 0, 30000 ),
		new( 324, 291, 4, 238, 23, 0, 0, 30000 ),
		new( 325, 292, 4, 242, 23, 0, 0, 30000 ),
		new( 326, 293, 4, 246, 23, 0, 0, 30000 ),
		new( 327, 294, 4, 250, 29, 0, 0, 30000 ),
		new( 328, 295, 4, 254, 26, 0, 0, 30000 ),
		new( 329, 296, 4, 258, 25, 0, 0, 30000 ),
		new( 330, 297, 4, 262, 23, 0, 0, 30000 ),
		new( 331, 298, 4, 266, 23, 0, 0, 30000 ),
		new( 332, 299, 4, 270, 25, 0, 0, 30000 ),
		new( 333, 300, 4, 278, 23, 0, 0, 30000 ),
		new( 334, 301, 4, 282, 23, 0, 0, 30000 ),
		new( 335, 302, 4, 286, 24, 0, 0, 30000 ),
		new( 336, 303, 4, 290, 23, 0, 0, 30000 ),
		new( 337, 304, 4, 294, 27, 0, 0, 30000 ),
		new( 338, 305, 4, 298, 28, 0, 0, 30000 ),
		new( 339, 306, 4, 306, 24, 0, 0, 30000 ),
		new( 340, 307, 4, 310, 24, 0, 0, 30000 ),
		new( 341, 308, 4, 314, 24, 0, 0, 30000 ),
		new( 342, 309, 4, 318, 24, 0, 0, 30000 ),
		new( 343, 310, 4, 322, 28, 0, 0, 30000 ),
		new( 344, 311, 4, 326, 28, 0, 0, 30000 ),
		new( 345, 312, 4, 330, 24, 0, 0, 30000 ),
		new( 346, 280, 4, 195, 30, 0, 0, 30000 ),
		new( 347, 281, 4, 199, 30, 0, 0, 30000 ),
		new( 348, 282, 4, 203, 30, 0, 0, 30000 ),
		new( 349, 283, 4, 207, 30, 0, 0, 30000 ),
		new( 350, 284, 4, 211, 30, 0, 0, 30000 ),
		new( 351, 285, 4, 215, 30, 0, 0, 30000 ),
		new( 352, 286, 4, 219, 30, 0, 0, 30000 ),
		new( 353, 287, 4, 223, 30, 0, 0, 30000 ),
		new( 354, 288, 4, 227, 31, 0, 0, 30000 ),
		new( 355, 289, 4, 231, 32, 0, 0, 30000 ),
		new( 356, 290, 4, 235, 32, 0, 0, 30000 ),
		new( 357, 291, 4, 239, 30, 0, 0, 30000 ),
		new( 358, 292, 4, 243, 30, 0, 0, 30000 ),
		new( 359, 293, 4, 247, 30, 0, 0, 30000 ),
		new( 360, 294, 4, 251, 36, 0, 0, 30000 ),
		new( 361, 295, 4, 255, 33, 0, 0, 30000 ),
		new( 362, 296, 4, 259, 32, 0, 0, 30000 ),
		new( 363, 297, 4, 263, 30, 0, 0, 30000 ),
		new( 364, 298, 4, 267, 30, 0, 0, 30000 ),
		new( 365, 299, 4, 271, 32, 0, 0, 30000 ),
		new( 366, 300, 4, 279, 30, 0, 0, 30000 ),
		new( 367, 301, 4, 283, 30, 0, 0, 30000 ),
		new( 368, 302, 4, 287, 31, 0, 0, 30000 ),
		new( 369, 303, 4, 291, 30, 0, 0, 30000 ),
		new( 370, 304, 4, 295, 34, 0, 0, 30000 ),
		new( 371, 305, 4, 299, 35, 0, 0, 30000 ),
		new( 372, 306, 4, 307, 31, 0, 0, 30000 ),
		new( 373, 307, 4, 311, 31, 0, 0, 30000 ),
		new( 374, 308, 4, 315, 31, 0, 0, 30000 ),
		new( 375, 309, 4, 319, 31, 0, 0, 30000 ),
		new( 376, 310, 4, 323, 35, 0, 0, 30000 ),
		new( 377, 311, 4, 327, 35, 0, 0, 30000 ),
		new( 378, 312, 4, 331, 31, 0, 0, 30000 ),
	};

	public static bool TryGet( int type, ChallengePresentationKind kind, out ChallengeMessageRecord record )
	{
		record = default;
		if ( type <= 0 || type >= progressTags.Length || (int)kind < 0 || (int)kind > (int)ChallengePresentationKind.Failure )
			return false;
		return TryGetByTag( progressTags[type] + ((int)kind - 1) * 33, out record );
	}

	public static bool TryGetByTag( int tag, out ChallengeMessageRecord record )
	{
		record = default;
		if ( tag is < 247 or > 378 )
			return false;
		record = records[tag - 247];
		return true;
	}
}
