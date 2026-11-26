namespace OpenTPW;

/// <summary>
/// Theme Park Inc's advisor sequences: the generated one (<c>GenerateAnimSequence 0x715AB0</c>, the same rule as Theme Park World's <see cref="AdvisorSequences.Generate"/>) and the canned
/// rows (<c>SetCannedSequence 0x715BC0</c>, table <c>0x9DD350</c>), with the clip lengths of Theme Park Inc's 25-entry table (<see cref="AdvisorClipDurations.ThemeParkIncCount"/>).
/// </summary>
internal static class ThemeParkIncAdvisorSequences
{
	/// <summary>The animation of the first canned row; -1 asks for a generated sequence.</summary>
	public const int FirstCanned = 16;

	/// <summary>The rows of the canned table (20 rows of 20 words); the generator's speech budget adds this to the voice length.</summary>
	public const int CannedRows = 20;

	/// <summary>An entry with this bit is a timer pair (the flag, then milliseconds after the clips so far).</summary>
	public const int TimerFlag = 0x400000;

	/// <summary>The accessory bits of a clip entry (<c>entry &amp; 0x7F0000</c>); the clip is the entry without them (<c>&amp; 0xFF80FFFF</c>).</summary>
	public const int AccessoryMask = 0x7F0000;

	// [BIN:TPI-EXE:0x9DD350 canned sequences] 80-byte rows of the executable's data; rows 2-4, 6-8, 13 and 19 are empty. Clips 16 to 26 exist only in the level advisors' archives
	private static readonly int[][] Rows =
	{
		new[] { 0x10 },
		new[] { 0x30012, 0x90013, 0x110014 },
		Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(),
		new[] { 0x15, 0x15, 0x15, 0x15, 0x15, 0x16, 0x10017, 0x10018 },
		Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(),
		new[] { 0x19, 0x19, 0x19, 0x19, 0x1A },
		new[] { 0xE, 5, 0xB, 0xF },
		new[] { 0xE, 0xB, 1, 0xC, 1, 0xF },
		new[] { 0xE, 0xB, 0xB, 5, 0xF },
		Array.Empty<int>(),
		new[] { 0xE, 0xB, 4, 0xF },
		new[] { 0xE, 5, 1, 0xB, 6, 0xF },
		new[] { 0xE, 0xB, 9, 0xF },
		new[] { TimerFlag, 17_000 },
		new[] { 0x20011, 0x20011, 0x20011, 0x20011, 0x40012, 0x90013, TimerFlag, 1_000, 0x110014 },
		Array.Empty<int>(),
	};

	/// <summary>The words of canned row <paramref name="row"/> before its terminating 0 (empty outside the table).</summary>
	public static IReadOnlyList<int> Row( int row ) => row is >= 0 and < CannedRows ? Rows[row] : Array.Empty<int>();

	/// <summary>
	/// The generated sequence for <paramref name="budget"/> milliseconds (<c>0x715AB0</c>): clip 14, then while budget is left and fewer than 19 clips are queued one <c>rand() % 10</c> per
	/// choice (the first one ignored), the drawn clip or the first of clips 1–10 that is at least as long as the budget left (clip 10 when none is), and clip 15. Its sum counts clip 15.
	/// </summary>
	// [BIN:TPI-EXE:0x715AB0 generated sequence] the clip list at 0xBEF134 starts with 0xE; a drawn clip (edx + 1) is taken while the budget is not below its length (0xBEF0D0 + 4·edx), else the first
	// entry of 0xBEF0D0..0xBEF0F8 the budget does not exceed (clip 10 without one); the budget and the sum move by the clip's length; then 0xF, the sum plus 0xBEF108 (clip 15), the terminator and
	// 0xBEF184 = 0. [EXT:tpi-advisor-presentation-rand] (user-approved 2026-11-24) rand() at 0x715AC4 draws from the advisor's presentation stream
	public static AdvisorSequence Generate( int budget, AdvisorClipDurations durations, Func<int> rand ) => AdvisorSequences.Generate( budget, durations, rand );

	/// <summary>
	/// The canned sequence of row <paramref name="row"/> (<c>0x715BC0</c>), or the generated one for <paramref name="budget"/> when its clips sum to 0 and the budget is not 0. The result also
	/// tells whether the voice starts at once: when the row's first clip is not clip 14.
	/// </summary>
	// [BIN:TPI-EXE:0x715BC0 canned sequence] 0xBEF194 (the timer) = 0; each word is written to the clip list at its count before it is tested: 0 ends; a word with 0x400000 sets the timer to the next
	// word plus the sum so far and skips both; the first clip clears the delayed voice (0xBEF09C = 0) when it is not 0xE; a clip adds its length 0xBEF0CC + 4·clip (clip & 0xFF80FFFF) and counts.
	// A length past the table's 25 entries reads the clip list itself: clip 26 (0xBEF134) is the row's first word, which the list holds by then (row 9: 0x19, 25 ms). A sum of 0 with a budget calls
	// the generator (0x403C8D); otherwise 0xBEF184 = 0 and the sum is returned
	public static (AdvisorSequence Sequence, bool Immediate) Canned( int row, int budget, AdvisorClipDurations durations, Func<int> rand )
	{
		ArgumentNullException.ThrowIfNull( durations );
		var words = Row( row );
		var clips = new List<int>();
		var bits = new List<int>();
		var list = new List<int>();
		var sum = 0;
		int? timer = null;
		var immediate = false;
		for ( var index = 0; index < words.Count; index++ )
		{
			var word = words[index];
			if ( (word & TimerFlag) == TimerFlag )
			{
				timer = unchecked( words[index + 1] + sum );
				index++;
				continue;
			}
			list.Add( word );
			var clip = word & ~AccessoryMask;
			if ( clips.Count == 0 && clip != AdvisorClipDurations.StartClip )
				immediate = true;
			clips.Add( clip );
			bits.Add( (word & AccessoryMask) >> 16 );
			sum = unchecked( sum + Length( clip, durations, list ) );
		}
		if ( budget != 0 && sum == 0 )
			return (AdvisorSequences.Generate( budget, durations, rand ) with { TimerMilliseconds = timer }, immediate);
		return (new AdvisorSequence( clips, sum, timer, bits ), immediate);
	}

	/// <summary>The length the canned builder adds for <paramref name="clip"/>: the table's entry, or past it the clip list's word it reads instead.</summary>
	private static int Length( int clip, AdvisorClipDurations durations, List<int> list )
	{
		if ( clip <= durations.Clips )
			return (int)durations.Of( clip );
		var slot = clip - durations.Clips - 1;
		return slot < list.Count ? list[slot] : throw new NotSupportedException( $"Canned clip {clip} reads a clip list word the row has not written." );
	}

	/// <summary>
	/// The milliseconds the speech returns (<c>0x716230</c>): the sequence for a voice of <paramref name="voice"/> milliseconds plus clip 15 plus 1000. A generated sequence gets the budget
	/// voice + 200 + 300 and replaces the voice + 200 unless it sums to 0; a canned one gets the same budget and its sum replaces it.
	/// </summary>
	// [BIN:TPI-EXE:0x716631 speech length] esi = the sample length + 200; animation -1: 0x715AB0(esi + 300), taken when not 0; else 0x715BC0(animation - 16, esi + 300); the return is
	// 0xBEF108 (clip 15) + esi + 1000 (0x7167CF)
	public static (uint Duration, uint Sequence, AdvisorSequence? Clips, bool Immediate) Speech( int animation, uint voice, AdvisorClipDurations durations, Func<int> rand )
	{
		var length = unchecked( (int)voice + 200 );
		AdvisorSequence? sequence;
		var immediate = false;
		if ( animation == -1 )
		{
			sequence = Generate( length + 300, durations, rand );
			if ( sequence.SumMilliseconds != 0 )
				length = sequence.SumMilliseconds;
		}
		else
		{
			(sequence, immediate) = Canned( animation - FirstCanned, length + 300, durations, rand );
			length = sequence.SumMilliseconds;
		}
		return (unchecked( durations.Of( AdvisorClipDurations.EndClip ) + (uint)length + 1000 ), unchecked( (uint)length ), sequence, immediate);
	}
}
