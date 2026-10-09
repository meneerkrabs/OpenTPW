namespace OpenTPW.PpcEvidence.Advisor;

/// <summary>The original desired selector, not a claim that a renderer shows that mesh.</summary>
public enum OriginalAdvisorMouth
{
	Unselected = 0,
	Normal = 1,
	Aah = 2,
	Eee = 3,
	Ooh = 4,
	Sss = 5,
}

public readonly record struct OriginalAdvisorLipUpdate(
	bool SpeechStartDue,
	bool LipMarkConsumed,
	bool MouthSelectorChanged,
	OriginalAdvisorMouth DesiredMouth );

/// <summary>
/// Bounded transcription of identified Mac advisor timing consumers (PPC-advisor.md).
/// Every clock argument must be the caller-supplied, pause-aware UNSCALED advisor
/// presentation clock, including its recovered offset/freeze/compensation semantics.
/// No PCM, wall-clock, speed multiplier or original-clock bridge is supplied here.
/// Mouth output requests a selector; animation and node visibility remain external.
/// </summary>
public sealed class OriginalAdvisorLipDriver
{
	public const int MaximumLipWords = 16384;
	public const uint RawTerminator = uint.MaxValue;
	public const int MouthCount = 5;

	private readonly Func<int> nextOriginalRandom;
	private uint[] words = Array.Empty<uint>();

	public bool Talking { get; private set; }
	public bool LipActive { get; private set; }
	public bool SpeechPending { get; private set; }
	public int LipStartClock { get; private set; }
	public int NextLipClock { get; private set; }
	public int PendingSpeechClock { get; private set; }
	public int NextMouthClock { get; private set; }
	public int NextWordIndex { get; private set; }
	public OriginalAdvisorMouth DesiredMouth { get; private set; }

	/// <param name="nextOriginalRandom">
	/// Explicit rand-compatible dependency: each call must return a nonnegative integer.
	/// A .NET random generator is not assumed equivalent to the original CRT generator.
	/// </param>
	public OriginalAdvisorLipDriver( Func<int> nextOriginalRandom )
	{
		ArgumentNullException.ThrowIfNull( nextOriginalRandom );
		this.nextOriginalRandom = nextOriginalRandom;
	}

	/// <summary>Reproduces signed word division, truncating toward zero.</summary>
	public static int ConvertMarkToClockUnits( uint rawWord ) => unchecked((int)rawWord) / 1000;

	/// <summary>
	/// Begin at response-request time. Nonempty input represents a successfully loaded
	/// LIP, including its final raw -1 sentinel. Empty input represents missing LIP.
	/// deferSpeech means the animation sequence retained the original pending deadline.
	/// The clock-zero disabled sentinel and signed 32-bit arithmetic are preserved.
	/// Mouth cadence/desired state persists across requests, as in presentation state.
	/// </summary>
	public void BeginResponse( ReadOnlySpan<uint> terminatedWords, int unscaledPauseAwareClock, bool deferSpeech )
	{
		if ( !terminatedWords.IsEmpty && (terminatedWords.Length is < 2 or > MaximumLipWords ||
			terminatedWords[0] == RawTerminator || terminatedWords[^1] != RawTerminator) )
			throw new ArgumentException( "Supply a bounded LIP with a first mark and final raw -1 sentinel.", nameof( terminatedWords ) );

		// App 0x6cf0/0x6d7c and 0x6f88–0x6fb0: initial base -200;
		// retained nonzero pending deadline adds 1000 to the LIP base and clears talking.
		var pendingClock = unchecked(unscaledPauseAwareClock + 800);
		var pending = deferSpeech && pendingClock != 0;
		words = terminatedWords.ToArray();
		SpeechPending = pending;
		PendingSpeechClock = pending ? pendingClock : 0;
		LipStartClock = unchecked(unscaledPauseAwareClock - 200 + (pending ? 1000 : 0));
		LipActive = words.Length > 0;
		Talking = LipActive && !pending;
		NextWordIndex = LipActive ? 1 : 0;
		NextLipClock = LipActive ? unchecked(LipStartClock + ConvertMarkToClockUnits( words[0] )) : -1;
	}

	/// <summary>
	/// Call once per original presentation update, passing the explicit original clock.
	/// Equal pending-speech time starts speech; equal LIP/mouth deadlines do not advance.
	/// A missed frame consumes at most one expired mark and draws at most one random value.
	/// </summary>
	public OriginalAdvisorLipUpdate Update( int unscaledPauseAwareClock )
	{
		var speechStartDue = false;
		var lipMarkConsumed = false;
		var previousDesiredMouth = DesiredMouth;

		// App 0x7548–0x7590 compares signed deadline-minus-now, accepting equality.
		if ( SpeechPending && unchecked(PendingSpeechClock - unscaledPauseAwareClock) <= 0 )
		{
			SpeechPending = false;
			PendingSpeechClock = 0;
			Talking = true;
			speechStartDue = true;
		}

		// App 0x76c0–0x7754: ONE inversion/word read, with no catch-up loop.
		if ( LipActive && unscaledPauseAwareClock > NextLipClock )
		{
			Talking = !Talking;
			lipMarkConsumed = true;
			var nextWord = words[NextWordIndex++];
			var nextMark = nextWord == RawTerminator ? -1 : ConvertMarkToClockUnits( nextWord );
			// App 0x7734 checks the converted result, too: a negative non-sentinel
			// word dividing to -1 also ends the update path. Initial-mark setup differs.
			if ( nextMark == -1 )
			{
				LipActive = false;
				Talking = false;
				NextLipClock = -1;
			}
			else
				NextLipClock = unchecked(LipStartClock + nextMark);
		}

		// App 0x7770–0x77b0: Normal is a valid random talking selection.
		if ( Talking && LipActive )
		{
			if ( unscaledPauseAwareClock > NextMouthClock )
			{
				var random = nextOriginalRandom();
				if ( random < 0 )
					throw new InvalidOperationException( "The supplied original rand value must be nonnegative." );
				DesiredMouth = (OriginalAdvisorMouth)(random % MouthCount + 1);
				NextMouthClock = unchecked(unscaledPauseAwareClock + 100);
			}
		}
		else
			DesiredMouth = OriginalAdvisorMouth.Normal;

		return new OriginalAdvisorLipUpdate( speechStartDue, lipMarkConsumed,
			DesiredMouth != previousDesiredMouth, DesiredMouth );
	}
}
