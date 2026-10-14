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

	/// <param name="trailerWord2">Trailer word 2 of clip n at index n − 1 (0 for a clip that does not exist).</param>
	public AdvisorClipDurations( IReadOnlyList<uint> trailerWord2 )
	{
		ArgumentNullException.ThrowIfNull( trailerWord2 );
		// [BIN:TPW-EXE:0x005989C0 advisor clip lengths] the 20 entries are read through 0x474070 from the level advisor's clips when one is loaded, else the global advisor's; afterwards the entry of clip 14 (0xF796FC) is set to 0
		for ( var index = 0; index < Count && index < trailerWord2.Count; index++ )
			milliseconds[index] = FromTrailer( trailerWord2[index] );
		milliseconds[StartClip - 1] = 0;
	}

	/// <summary>Trailer word 2 × 1000 / 30 as unsigned 32-bit integer arithmetic (truncating).</summary>
	// [BIN:TPW-EXE:0x00474070 clip length] returns (uint)(trailer word 2 × 1000) / 30, and 0 when the slot has no clip at that index
	public static uint FromTrailer( uint trailerWord2 ) => unchecked(trailerWord2 * 1000u) / 30u;

	/// <summary>Length of clip <paramref name="clip"/> (1–20); 0 outside the table.</summary>
	public uint Of( int clip ) => clip is >= 1 and <= Count ? milliseconds[clip - 1] : 0;

	private static readonly Regex ClipName = new( @"^Advisor(?:_[A-Za-z]+)?m(\d+)\.MD2$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant );

	/// <summary>
	/// Lengths from <paramref name="level"/>'s advisor clips (<c>/levels/&lt;level&gt;/advisor</c>) when it has them, else from
	/// the global advisor's (<c>/global/advisor</c>); null when neither archive holds a clip.
	/// </summary>
	public static AdvisorClipDurations? Load( BaseFileSystem fileSystem, string? level )
	{
		foreach ( var archive in level == null ? new[] { Advisor.ArchivePath } : new[] { $"/levels/{level}/advisor", Advisor.ArchivePath } )
		{
			IEnumerable<string> members;
			try
			{
				members = fileSystem.GetFiles( archive ).ToArray();
			}
			catch ( Exception exception ) when ( exception is IOException or DirectoryNotFoundException or UnauthorizedAccessException or InvalidDataException )
			{
				continue;
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

/// <summary>A generated animation sequence: the clips in play order (ending with clip 15) and the sum of their lengths.</summary>
internal sealed record AdvisorSequence( IReadOnlyList<int> Clips, int SumMilliseconds );

/// <summary>The advisor's animation sequences: the generator of the original's 550 generated responses.</summary>
internal static class AdvisorSequences
{
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
	/// Span the response player returns: the sequence sum + 1000 + the length of clip 15. A generated sequence already
	/// ends with clip 15, so that clip counts twice.
	/// </summary>
	// [BIN:TPW-EXE:0x005994AC response player return] returns the builder's sum + 1000 + the length of clip 15 (0xF79700)
	public static uint ResponseSpan( AdvisorSequence sequence, AdvisorClipDurations durations ) =>
		checked((uint)(sequence.SumMilliseconds + 1000 + (int)durations.Of( AdvisorClipDurations.EndClip )));

	/// <summary>
	/// Whether a response with fixed sequence <paramref name="sequence"/> (−1 for a generated one) starts its audio when the
	/// response is requested: only the sequences 16, 17 and 29. Every other sequence opens with clip 14 and starts its audio
	/// <see cref="AdvisorSpeechStart.DelayMilliseconds"/> later.
	/// </summary>
	public static bool StartsImmediately( int sequence ) => sequence is 16 or 17 or 29;
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

	/// <summary>Whether <paramref name="spanMilliseconds"/> have not yet passed since the request (wrap-safe).</summary>
	public bool Within( long nowMilliseconds, uint spanMilliseconds ) => unchecked((uint)(nowMilliseconds - RequestMilliseconds)) < spanMilliseconds;
}
