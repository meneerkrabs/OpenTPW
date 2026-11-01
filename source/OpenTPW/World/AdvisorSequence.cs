using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>
/// The C runtime's <c>rand()</c> (linear congruential generator, seed times 214013 plus 2531011, bits 16..30), the
/// generator the original's sequence builder draws from. The original shares one global stream with every other
/// <c>rand()</c> caller of the process, so its draws depend on everything that ran before; OpenTPW gives the
/// advisor its own stream, seeded with 1 (the C runtime's default seed) per advisor, so a run is deterministic and
/// independent of the simulation's own random numbers. The sequence a given speech gets is therefore not the one
/// the original would have drawn at that moment, only one drawn by the same rule.
/// </summary>
internal sealed class AdvisorRandom
{
	private uint seed;

	public AdvisorRandom( uint seed = 1 ) => this.seed = seed;

	/// <summary>0..32767.</summary>
	public int Next()
	{
		seed = unchecked(seed * 0x343FD + 0x269EC3);
		return (int)((seed >> 16) & 0x7FFF);
	}
}

/// <summary>
/// Lengths in milliseconds of the advisor's animation clips 1–20, as the original's table at 0xF796C8 holds them.
/// </summary>
internal sealed class AdvisorClipDurations
{
	/// <summary>The table has one entry for each of the clips 1–20.</summary>
	public const int Count = 20;
	/// <summary>Clip 14 opens every generated sequence (and the fixed sequences 21–27); its length is forced to 0.</summary>
	public const int StartClip = 14;
	/// <summary>Clip 15 closes every generated sequence and is added once more by the response player.</summary>
	public const int EndClip = 15;

	private readonly uint[] milliseconds = new uint[Count];
	private readonly uint[] played = new uint[Count];

	/// <param name="trailerWord2">Trailer word 2 of clip n at index n − 1 (0 for a clip that does not exist).</param>
	public AdvisorClipDurations( IReadOnlyList<uint> trailerWord2 )
	{
		ArgumentNullException.ThrowIfNull( trailerWord2 );
		// [BIN:TPW-EXE:0x005989C0 advisor clip lengths] the 20 entries are read through 0x474070 from the level advisor's clips when one is loaded, else the global advisor's; afterwards the entry of clip 14 (0xF796FC) is set to 0
		for ( var index = 0; index < Count && index < trailerWord2.Count; index++ )
			milliseconds[index] = FromTrailer( trailerWord2[index] );
		// The animation itself still plays clip 14 for its whole length: the zero only keeps the opening out of the sequence's budget and sum.
		Array.Copy( milliseconds, played, Count );
		milliseconds[StartClip - 1] = 0;
	}

	/// <summary>Trailer word 2 × 1000 / 30 as unsigned 32-bit integer arithmetic (truncating).</summary>
	// [BIN:TPW-EXE:0x00474070 clip length] returns (uint)(trailer word 2 × 1000) / 30, and 0 when the slot has no clip at that index
	public static uint FromTrailer( uint trailerWord2 ) => unchecked(trailerWord2 * 1000u) / 30u;

	/// <summary>Length of clip <paramref name="clip"/> (1–20) in the table the sequence builder reads (clip 14 is 0); 0 outside the table.</summary>
	public uint Of( int clip ) => clip is >= 1 and <= Count ? milliseconds[clip - 1] : 0;

	/// <summary>
	/// How long clip <paramref name="clip"/> plays: its own length, including clip 14's (<see cref="Of"/> forces that to 0 for the
	/// budget); 0 outside the table or for a clip the archive lacks.
	/// </summary>
	// [BIN:TPW-EXE:0x004735D0 animation sampler] a clip runs to its own length before the sampler frees the channel (0x473743-0x47374E); only the table entry of clip 14 (0xF796FC) is zeroed, by 0x5989C0
	public uint PlayOf( int clip ) => clip is >= 1 and <= Count ? played[clip - 1] : 0;

	private static readonly Regex ClipName = new( @"^Advisor(?:_[A-Za-z]+)?m(\d+)\.MD2$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant );

	/// <summary>
	/// Lengths from <paramref name="level"/>'s advisor clips (<c>/levels/&lt;level&gt;/advisor</c>) when it has them, else from
	/// the global advisor's (<c>/global/advisor</c>); null when neither archive holds a clip.
	/// </summary>
	public static AdvisorClipDurations? Load( BaseFileSystem fileSystem, string? level )
	{
		foreach ( var archive in level == null ? new[] { Advisor.ArchivePath } : new[] { $"/levels/{level}/advisor", Advisor.ArchivePath } )
			if ( LoadArchive( fileSystem, archive ) is { } durations )
				return durations;
		return null;
	}

	/// <summary>Lengths from the clips of one archive (the model that plays them); null when it holds no clip.</summary>
	public static AdvisorClipDurations? LoadArchive( BaseFileSystem fileSystem, string archive )
	{
		{
			IEnumerable<string> members;
			try
			{
				members = fileSystem.GetFiles( archive ).ToArray();
			}
			catch ( Exception exception ) when ( exception is IOException or DirectoryNotFoundException or UnauthorizedAccessException or InvalidDataException )
			{
				return null;
			}
			var words = new uint[Count];
			var found = false;
			foreach ( var member in members )
			{
				var match = ClipName.Match( Path.GetFileName( member ) );
				if ( !match.Success || !int.TryParse( match.Groups[1].Value, out var number ) || number < 1 || number > Count )
					continue;
				try
				{
					if ( new ModelFile( $"{archive}/{Path.GetFileName( member )}" ).Clip is { } clip )
					{
						words[number - 1] = (uint)clip.Duration;
						found = true;
					}
				}
				catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException )
				{
					Log.Warning( $"Advisor clip {member} could not be read for its length: {exception.Message}" );
				}
			}
			if ( found )
				return new AdvisorClipDurations( words );
		}
		return null;
	}
}

/// <summary>
/// An animation sequence: the clips in play order and the sum of their lengths, which is also when the last clip ends. A
/// generated sequence ends with clip 15; a fixed one with its table row's last clip.
/// </summary>
/// <param name="TimerMilliseconds">
/// The effect timer a fixed row's timer entry sets (the clip sum before the entry plus its milliseconds), on the advisor
/// clock relative to the clip start; null without one. When the clock reaches it, the advisor plays <c>cat_ui</c>
/// <see cref="AdvisorSequences.TimerSound"/> and starts the screen-space effect <see cref="AdvisorSequences.TimerEffect"/>.
/// </param>
/// <param name="AccessoryBits">
/// For a fixed row, the accessory bits (<c>entry &amp; 0x7F0000</c> shifted down by 16) of each clip, parallel to <paramref name="Clips"/>;
/// null for a generated sequence.
/// </param>
internal sealed record AdvisorSequence( IReadOnlyList<int> Clips, int SumMilliseconds, int? TimerMilliseconds = null, IReadOnlyList<int>? AccessoryBits = null );

/// <summary>One entry of a fixed-sequence row: a clip (with its accessory bits) or a timer.</summary>
internal readonly record struct AdvisorSequenceEntry( int Clip, int Bits, int TimerMilliseconds )
{
	public static AdvisorSequenceEntry Play( int clip, int bits = 0 ) => new( clip, bits, 0 );
	public static AdvisorSequenceEntry Timer( int milliseconds ) => new( 0, 0, milliseconds );

	public bool IsTimer => Clip == 0;
}

/// <summary>
/// The response table's fixed sequences: the rows of the original's sequence table that the response rows' sequence field
/// (16 and up; −1 is generated) selects. Ten rows are in use; the others (18–20 and 24) are empty.
/// </summary>
internal static class AdvisorFixedSequences
{
	/// <summary>The sequence field of the first row.</summary>
	public const int FirstSequence = 16;

	// [BIN:TPW-EXE:0x00598BF0 fixed-sequence builder] row (sequence − 16) of the table at 0x76DC18 (0x50 bytes per row), walked until a zero entry: an entry with bit 0x400000 is a timer pair (flag, milliseconds), any other entry is a clip (entry & 0xFF80FFFF; bits 0x7F0000 are stored at 0xF79660 for the accessory selector 0x598CA0)
	// [BIN:TPW-EXE:0x0076DC18 fixed-sequence table] the ten rows in use; the clips 16-20 exist only in the level advisors' archives
	private static readonly IReadOnlyDictionary<int, AdvisorSequenceEntry[]> Rows = new Dictionary<int, AdvisorSequenceEntry[]>
	{
		[16] = new[] { Clip( 16 ) },
		[17] = new[] { Clip( 17, 2 ), Clip( 17, 2 ), Clip( 17, 2 ), Clip( 17, 2 ), Clip( 18, 4 ), Clip( 19, 9 ), Clip( 20, 0x11 ) },
		[21] = new[] { Clip( 14 ), Clip( 5 ), Clip( 11 ), Clip( 15 ) },
		[22] = new[] { Clip( 14 ), Clip( 11 ), Clip( 1 ), Clip( 12 ), Clip( 1 ), Clip( 15 ) },
		[23] = new[] { Clip( 14 ), Clip( 11 ), Clip( 11 ), Clip( 5 ), Clip( 15 ) },
		[25] = new[] { Clip( 14 ), Clip( 11 ), Clip( 4 ), Clip( 15 ) },
		[26] = new[] { Clip( 14 ), Clip( 5 ), Clip( 1 ), Clip( 11 ), Clip( 6 ), Clip( 15 ) },
		[27] = new[] { Clip( 14 ), Clip( 11 ), Clip( 9 ), Clip( 15 ) },
		[28] = new[] { AdvisorSequenceEntry.Timer( 17000 ) },
		[29] = new[] { Clip( 17, 2 ), Clip( 17, 2 ), Clip( 17, 2 ), Clip( 17, 2 ), Clip( 18, 4 ), Clip( 19, 9 ), AdvisorSequenceEntry.Timer( 1000 ), Clip( 20, 0x11 ) },
	};

	private static AdvisorSequenceEntry Clip( int clip, int bits = 0 ) => AdvisorSequenceEntry.Play( clip, bits );

	/// <summary>The entries of the fixed sequence <paramref name="sequence"/>; empty for −1 (generated), an empty row or a value outside the table.</summary>
	public static IReadOnlyList<AdvisorSequenceEntry> Row( int sequence ) =>
		Rows.TryGetValue( sequence, out var row ) ? row : Array.Empty<AdvisorSequenceEntry>();
}

/// <summary>The advisor's animation sequences: the generator of the original's 550 generated responses.</summary>
internal static class AdvisorSequences
{
	/// <summary>The <c>cat_ui</c> sound a fixed sequence's timer plays (the ticket/key advice, sequences 28 and 29).</summary>
	// [BIN:TPW-EXE:0x005999B2 advisor timer] 0x51BFC0(0, [0x803A2C] = global cat_ui (0x51EBE7), 0xC6, 0, 0, 0): flat, handle not kept
	public const uint TimerSound = 0xC6;
	/// <summary>The screen-space effect a fixed sequence's timer starts: template 87 (Key) at native (90000, 0, 7000), screen (0.8, −0.8).</summary>
	// [BIN:TPW-EXE:0x005999AD advisor timer] 0x521E60(0x57, 0x15F90, 0, 0x1B58) before the sound; the handle is not kept
	public const int TimerEffect = 87;
	public static readonly (int X, int Y, int Z) TimerEffectPosition = (90000, 0, 7000);

	/// <summary>A sequence holds at most this many clips before the closing clip 15.</summary>
	public const int MaximumClips = 19;
	/// <summary>The milliseconds added to the speech length for the generator's budget.</summary>
	public const int BudgetSlackMilliseconds = 200 + 300;

	/// <summary>
	/// Generated sequence for a speech: clip 14; then, while budget is left and fewer than 19 clips are queued, a clip
	/// <c>rand() % 10 + 1</c> — or, when that clip is longer than the remaining budget, the first of the clips 1–10 that is
	/// at least as long as the remaining budget (clip 10 when none is) — and finally clip 15. Each clip takes its length
	/// from the budget. One <c>rand()</c> is drawn for every clip choice including the first, which ignores it.
	/// </summary>
	/// <param name="budgetMilliseconds">Speech length + 200 + 300.</param>
	public static AdvisorSequence Generate( int budgetMilliseconds, AdvisorClipDurations durations, Func<int> rand )
	{
		ArgumentNullException.ThrowIfNull( durations );
		ArgumentNullException.ThrowIfNull( rand );
		// [BIN:TPW-EXE:0x00598B20 sequence generator] clip 14 first; each next clip is rand() % 10 + 1, replaced by the first clip 1–10 whose length is at least the remaining budget (10 when none) if the drawn clip is longer than the budget; stops when the budget is below 1 or 19 clips are queued; then clip 15, whose length is part of the returned sum
		var clips = new List<int>();
		var sum = 0;
		var budget = budgetMilliseconds;
		while ( true )
		{
			if ( budget < 1 || clips.Count > MaximumClips - 1 )
			{
				clips.Add( AdvisorClipDurations.EndClip );
				sum += (int)durations.Of( AdvisorClipDurations.EndClip );
				return new AdvisorSequence( clips, sum );
			}
			var roll = rand();
			int clip;
			if ( clips.Count == 0 )
				clip = AdvisorClipDurations.StartClip;
			else if ( budget < (int)durations.Of( roll % 10 + 1 ) )
			{
				clip = 10;
				for ( var candidate = 1; candidate <= 10; candidate++ )
					if ( budget <= (int)durations.Of( candidate ) )
					{
						clip = candidate;
						break;
					}
			}
			else
				clip = roll % 10 + 1;
			clips.Add( clip );
			sum += (int)durations.Of( clip );
			budget -= (int)durations.Of( clip );
		}
	}

	/// <summary>
	/// The sequence a response plays: its fixed row when the response has one (sequence 16 and up), else a generated one. A
	/// fixed row whose clips sum to 0 (an empty row, a row of only a timer such as 28, or clips missing from the advisor
	/// archives) falls back to the generator with the same budget; its timer stays.
	/// </summary>
	/// <param name="sequence">The response row's sequence field: −1 for a generated sequence, otherwise the row of the fixed table.</param>
	/// <param name="budgetMilliseconds">Speech length + 200 + 300.</param>
	public static AdvisorSequence Build( int sequence, int budgetMilliseconds, AdvisorClipDurations durations, Func<int> rand )
	{
		ArgumentNullException.ThrowIfNull( durations );
		ArgumentNullException.ThrowIfNull( rand );
		var clips = new List<int>();
		var bits = new List<int>();
		var sum = 0;
		int? timer = null;
		// [BIN:TPW-EXE:0x00598BF0 fixed-sequence builder] a timer entry sets the timer to the clip sum so far plus its milliseconds and adds no clip; a clip adds its length from the clip table (clip 14 is 0); a sum of 0 with a nonzero budget returns the generator's sequence (0x598B20) instead
		foreach ( var entry in AdvisorFixedSequences.Row( sequence ) )
		{
			if ( entry.IsTimer )
			{
				// [BIN:TPW-EXE:0x00599880 advisor update] when the advisor clock reaches the timer (0x599991) the update starts the particle effect 0x521E60 (87, 90000, 0, 7000), plays sound 0xC6 (0x51BFC0) and calls 0x4B9340 (Advisor.Update)
				// [BIN:TPW-EXE:0x004B9340 lobby key panel refresh] it returns at once unless the lobby's key panel window exists (0x7CC4B4, created by 0x4B8CA0 from the lobby 0x5E19B8 and cleared by its window procedure 0x4B8B70): in the park it does nothing; the park HUD's key counter is refreshed by the HUD's own message 0x1E handler (0x4A0FB0, ParkStatus.GoldenKeys)
				// [APPROX:ADVISOR-025] The lobby's key panel and the lobby advisor are ported (FrontEndMenu.KeyPanel.cs, FrontEnd/LobbyAdvisor.cs); response 393, which reaches this timer in the lobby (0x4A6B4F, when the first player's dialog closes after 0x5AFC30 added the first key), is not: OpenTPW publishes the starter key only when the lobby is retired for the park, and the other lobby lines belong to dialogs OpenTPW does not have.
				timer = sum + entry.TimerMilliseconds;
				continue;
			}
			clips.Add( entry.Clip );
			bits.Add( entry.Bits );
			sum += (int)durations.Of( entry.Clip );
		}
		if ( sum == 0 && budgetMilliseconds != 0 )
			return Generate( budgetMilliseconds, durations, rand ) with { TimerMilliseconds = timer };
		return new AdvisorSequence( clips, sum, timer, sequence >= AdvisorFixedSequences.FirstSequence && clips.Count > 0 ? bits : null );
	}

	/// <summary>
	/// How long the sequence's clips play from the request to the end of the last one: each clip's own length
	/// (<see cref="AdvisorClipDurations.PlayOf"/>), clip 14's included. The sampler hides the model there.
	/// </summary>
	public static uint PlayMilliseconds( AdvisorSequence sequence, AdvisorClipDurations durations )
	{
		ArgumentNullException.ThrowIfNull( sequence );
		ArgumentNullException.ThrowIfNull( durations );
		var total = 0u;
		foreach ( var clip in sequence.Clips )
			total = checked(total + durations.PlayOf( clip ));
		return total;
	}

	/// <summary>
	/// Span the response player returns: the sequence sum + 1000 + the length of clip 15. A generated sequence already
	/// ends with clip 15, so that clip counts twice.
	/// </summary>
	// [BIN:TPW-EXE:0x005994AC response player return] returns the builder's sum + 1000 + the length of clip 15 (0xF79700)
	public static uint ResponseSpan( AdvisorSequence sequence, AdvisorClipDurations durations ) =>
		checked((uint)(sequence.SumMilliseconds + 1000 + (int)durations.Of( AdvisorClipDurations.EndClip )));

	/// <summary>
	/// Whether a response with fixed sequence <paramref name="sequence"/> (−1 for a generated one) starts its audio when the
	/// response is requested: when its row's first clip is not clip 14, which is the sequences 16, 17 and 29. Every other
	/// sequence opens with clip 14 (a generated one always does) and starts its audio
	/// <see cref="AdvisorSpeechStart.DelayMilliseconds"/> later.
	/// </summary>
	// [BIN:TPW-EXE:0x00598BF0 fixed-sequence builder] the builder clears the immediate-start word 0xF79694 when the row's first clip is not clip 14 (0x598C47–0x598C56); a row without a clip never clears it
	public static bool StartsImmediately( int sequence ) =>
		AdvisorFixedSequences.Row( sequence ).FirstOrDefault( entry => !entry.IsTimer ) is { Clip: not 0 and not AdvisorClipDurations.StartClip };
}

/// <summary>
/// When a response's audio starts and where its LIP clock begins, on the advisor clock (milliseconds that stand still
/// while the game is paused).
/// </summary>
internal readonly record struct AdvisorSpeechStart( long RequestMilliseconds, bool Delayed )
{
	/// <summary>A delayed start restarts the audio this long after the request.</summary>
	public const int DelayMilliseconds = 800;
	/// <summary>The LIP clock of an immediate start begins this long before the request.</summary>
	public const int LipLeadMilliseconds = 200;

	public static AdvisorSpeechStart For( long requestMilliseconds, int sequence ) => new( requestMilliseconds, !AdvisorSequences.StartsImmediately( sequence ) );

	/// <summary>
	/// The advisor-clock time the audio starts: the request itself, or for a sequence opening with clip 14 the time the
	/// audio started at the request is stopped and restarted (equality counts).
	/// </summary>
	// [BIN:TPW-EXE:0x00599050 response player] audio starts at the request with the LIP clock base at request − 200 (0x5990FA); a sequence that opens with clip 14 stops it at once (0x51C2C0) and moves the base by 1000 to request + 800
	// [BIN:TPW-EXE:0x00599880 advisor update] the audio restarts once the clock reaches the moved base (equality counts) and the talking flag is set again (0x599987)
	public long AudioStartMilliseconds => Delayed ? RequestMilliseconds + DelayMilliseconds : RequestMilliseconds;

	/// <summary>The LIP clock reads the advisor clock minus this base.</summary>
	public long LipBaseMilliseconds => Delayed ? RequestMilliseconds + DelayMilliseconds : RequestMilliseconds - LipLeadMilliseconds;

	/// <summary>How far the mouth leads the audio that started at <see cref="AudioStartMilliseconds"/>.</summary>
	public long LipLeadOverAudioMilliseconds => AudioStartMilliseconds - LipBaseMilliseconds;

	/// <summary>
	/// Whether the audio is due. The advisor clock is a wrapping 32-bit millisecond count, so the elapsed time since the
	/// request is taken modulo 2^32 and a request just before the wrap still reaches its deadline.
	/// </summary>
	public bool AudioDue( long nowMilliseconds ) => unchecked((uint)(nowMilliseconds - RequestMilliseconds)) >= (Delayed ? (uint)DelayMilliseconds : 0u);

	/// <summary>Milliseconds the LIP clock reads at advisor-clock time <paramref name="nowMilliseconds"/> (wrap-safe): the clock minus <see cref="LipBaseMilliseconds"/>.</summary>
	public long LipMilliseconds( long nowMilliseconds ) => unchecked((uint)(nowMilliseconds - LipBaseMilliseconds));

	/// <summary>
	/// Whether a sequence timer <paramref name="timerMilliseconds"/> after the request is due: the deadline minus the clock, as a signed
	/// 32-bit difference, is not positive.
	/// </summary>
	// [BIN:TPW-EXE:0x005994A5 response player] a set timer is moved to the request's clock value (+ esi) when the response starts
	// [BIN:TPW-EXE:0x00599991 advisor update] it fires when timer - now (sub, signed test) is not above 0, then clears (0x5999D1)
	public bool TimerDue( long nowMilliseconds, int timerMilliseconds ) =>
		unchecked((int)((uint)(RequestMilliseconds + timerMilliseconds) - (uint)nowMilliseconds)) <= 0;

	/// <summary>Whether <paramref name="spanMilliseconds"/> have not yet passed since the request (wrap-safe).</summary>
	public bool Within( long nowMilliseconds, uint spanMilliseconds ) => unchecked((uint)(nowMilliseconds - RequestMilliseconds)) < spanMilliseconds;
}
