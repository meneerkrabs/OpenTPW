namespace OpenTPW.PpcEvidence.Advisor;

/// <summary>Caller-resolved descriptor/group controls; no original table is embedded.</summary>
public readonly record struct OriginalAdvisorMessageDescriptor(
	int MessageId, int FirstResponseId, int ResponseCount, byte MessageGroup,
	uint MinimumRepeatGroups, bool SayOnlyOnce, uint DiscardAfterSlaps, byte MaximumPendingDuplicates );

public readonly record struct OriginalAdvisorMessageHistory(
	uint SavedGameTick, int PreviousVariant, bool HasBeenPlayed, uint SlapCount )
{
	public static OriginalAdvisorMessageHistory Empty => new( 0, -1, false, 0 );
}

/// <summary>Score is supplied explicitly; this helper never evaluates a score producer.</summary>
public sealed record OriginalAdvisorPendingAdvice(
	int MessageId, int Score, int ResponseVariantOverride = -1, bool OverrideOnlyOnce = false );

public enum OriginalAdvisorEligibility
{
	Eligible, TutorialDisabled, RepeatDelay, AlreadyPlayed, TooManySlaps, DuplicateLimit,
}

public readonly record struct OriginalAdvisorQueueAdmission(
	OriginalAdvisorEligibility Eligibility, bool Stored, int Slot )
{
	// App 0x8d10 acknowledges an eligible full-queue item even if it did not replace.
	public bool Acknowledged => Eligibility == OriginalAdvisorEligibility.Eligible;
}

public readonly record struct OriginalAdvisorResponseSelection(
	int Slot, OriginalAdvisorPendingAdvice Advice, int Variant, int ResponseId );

/// <summary>
/// Pure eight-slot queue/history consumer for the identified shipped cyclic mode.
/// Descriptors, group controls, scores, raw game ticks and the unscaled advisor clock
/// remain explicit caller inputs. Playback success includes external original-wrapper
/// revalidation/response resolution; no game-message, score, audio or clock bridge exists.
/// </summary>
public sealed class OriginalAdvisorScoreQueue
{
	public const int Capacity = 8;
	private readonly Dictionary<int, OriginalAdvisorMessageDescriptor> descriptors;
	private readonly Dictionary<int, OriginalAdvisorMessageHistory> history;
	private readonly OriginalAdvisorPendingAdvice?[] slots = new OriginalAdvisorPendingAdvice?[Capacity];
	private OriginalAdvisorResponseSelection? playbackAttempt;

	public int MinimumScore { get; }
	public uint LastActionStarted { get; private set; }
	public uint LastActionDuration { get; private set; }
	public int Count => slots.Count( slot => slot != null );

	public OriginalAdvisorScoreQueue( IEnumerable<OriginalAdvisorMessageDescriptor> descriptors,
		IReadOnlyDictionary<int, OriginalAdvisorMessageHistory> initialHistory, int minimumScore )
	{
		ArgumentNullException.ThrowIfNull( descriptors );
		ArgumentNullException.ThrowIfNull( initialHistory );
		this.descriptors = descriptors.ToDictionary( item => item.MessageId );
		if ( this.descriptors.Values.Any( item => item.ResponseCount <= 0 ) )
			throw new ArgumentException( "Supply a positive response count for the reviewed cyclic mode.", nameof( descriptors ) );
		if ( initialHistory.Keys.Any( id => !this.descriptors.ContainsKey( id ) ) )
			throw new ArgumentException( "History needs an explicitly supplied descriptor.", nameof( initialHistory ) );
		history = initialHistory.ToDictionary( pair => pair.Key, pair => pair.Value );
		MinimumScore = minimumScore;
	}

	public OriginalAdvisorPendingAdvice? GetSlot( int index ) => slots[index];
	public OriginalAdvisorMessageHistory GetHistory( int messageId ) =>
		history.TryGetValue( messageId, out var value ) ? value : OriginalAdvisorMessageHistory.Empty;

	public static uint HistoryElapsed( uint liveGameTick, uint savedGameTick ) =>
		unchecked((liveGameTick >> 2) - (savedGameTick >> 2));

	public static bool IsActionBusy( uint unscaledClock, uint started, uint duration ) =>
		unscaledClock < unchecked(started + duration);

	public bool IsBusy( uint unscaledClock ) => IsActionBusy( unscaledClock, LastActionStarted, LastActionDuration );

	/// <summary>Only the computed-score playback wrapper uses this inclusive revalidation.</summary>
	public bool RevalidatedPlaybackScoreAccepts( int recomputedScore ) => recomputedScore >= MinimumScore;

	public OriginalAdvisorEligibility Eligibility( OriginalAdvisorPendingAdvice advice,
		uint liveGameTick, bool tutorialEnabled )
	{
		ArgumentNullException.ThrowIfNull( advice );
		var descriptor = descriptors[advice.MessageId];
		var previous = GetHistory( advice.MessageId );
		if ( descriptor.MessageGroup == 1 && !tutorialEnabled )
			return OriginalAdvisorEligibility.TutorialDisabled;
		// App 0x90a4: ticks 0..3 are the zero saved-quarter sentinel.
		if ( previous.SavedGameTick >> 2 != 0 &&
			HistoryElapsed( liveGameTick, previous.SavedGameTick ) < descriptor.MinimumRepeatGroups )
			return OriginalAdvisorEligibility.RepeatDelay;
		if ( !advice.OverrideOnlyOnce )
		{
			if ( descriptor.SayOnlyOnce && previous.HasBeenPlayed )
				return OriginalAdvisorEligibility.AlreadyPlayed;
			// App 0xe0fc compares signed bit patterns; retain uint storage/wrap.
			if ( descriptor.DiscardAfterSlaps != 0 &&
				unchecked((int)previous.SlapCount) >= unchecked((int)descriptor.DiscardAfterSlaps) )
				return OriginalAdvisorEligibility.TooManySlaps;
		}
		var duplicates = slots.Count( item => item?.MessageId == advice.MessageId );
		return duplicates < descriptor.MaximumPendingDuplicates
			? OriginalAdvisorEligibility.Eligible : OriginalAdvisorEligibility.DuplicateLimit;
	}

	public OriginalAdvisorQueueAdmission Enqueue( OriginalAdvisorPendingAdvice advice,
		uint liveGameTick, bool tutorialEnabled )
	{
		var eligibility = Eligibility( advice, liveGameTick, tutorialEnabled );
		if ( eligibility != OriginalAdvisorEligibility.Eligible )
			return new( eligibility, false, -1 );
		var free = Array.FindIndex( slots, item => item == null );
		if ( free >= 0 )
		{
			slots[free] = advice;
			return new( eligibility, true, free );
		}
		var weakest = 0;
		for ( var index = 1; index < Capacity; index++ )
			if ( slots[index]!.Score < slots[weakest]!.Score )
				weakest = index;
		if ( advice.Score <= slots[weakest]!.Score )
			return new( eligibility, false, weakest );
		slots[weakest] = advice;
		return new( eligibility, true, weakest );
	}

	/// <summary>
	/// Choose using cached scores. Eligibility was applied on admission, not reapplied to
	/// already-accepted pending records. This operation does not start or consume playback.
	/// </summary>
	public OriginalAdvisorResponseSelection? SelectNext( uint unscaledClock )
	{
		if ( IsBusy( unscaledClock ) )
			return null;
		var best = -1;
		for ( var index = 0; index < Capacity; index++ )
			if ( slots[index] is { } item && (best < 0 || item.Score > slots[best]!.Score) )
				best = index;
		if ( best < 0 || slots[best]!.Score <= MinimumScore )
			return null;
		var advice = slots[best]!;
		var descriptor = descriptors[advice.MessageId];
		var variant = advice.ResponseVariantOverride;
		if ( variant < 0 )
		{
			variant = unchecked(GetHistory( advice.MessageId ).PreviousVariant + 1);
			if ( variant >= descriptor.ResponseCount )
				variant = 0;
		}
		// The wrapper falls back to first response, but history retains an explicit
		// out-of-range requested variant (0xb8c8/0xb904 versus 0x8a3c).
		var playbackVariant = variant >= descriptor.ResponseCount ? 0 : variant;
		return new( best, advice, variant, unchecked(descriptor.FirstResponseId + playbackVariant) );
	}

	/// <summary>
	/// Consume before invoking the external original score/playback wrapper. Callers
	/// serialize begin, wrapper callback and completion, retaining the selected tuple.
	/// The one-outstanding-attempt guard is a helper contract, not a recovered native
	/// reentrancy or threading rule; this value API does not mint attempt identities.
	/// </summary>
	public void BeginPlaybackAttempt( OriginalAdvisorResponseSelection selection )
	{
		if ( playbackAttempt.HasValue || (uint)selection.Slot >= Capacity || slots[selection.Slot] == null ||
			!ReferenceEquals( slots[selection.Slot], selection.Advice ) )
			throw new InvalidOperationException( "Supply a current pending selection and finish the preceding attempt first." );
		slots[selection.Slot] = null;
		playbackAttempt = selection;
	}

	/// <summary>
	/// Complete the already-consumed attempt with its unchanged selection tuple.
	/// Rejected completion preserves the outstanding attempt. Only successful playback saves
	/// current game tick/variant and reserves returned span plus 1000 clock units.
	/// playbackSucceeded includes caller response lookup, appropriate score-wrapper
	/// revalidation and dispatcher result; no producers are fabricated here.
	/// </summary>
	public void CompletePlaybackAttempt( OriginalAdvisorResponseSelection selection,
		bool playbackSucceeded, uint unscaledClockAfterAttempt, uint liveGameTickAfterAttempt,
		uint returnedPlaybackSpan )
	{
		if ( playbackAttempt is not { } pending || pending.Slot != selection.Slot ||
			pending.Variant != selection.Variant || pending.ResponseId != selection.ResponseId ||
			!ReferenceEquals( pending.Advice, selection.Advice ) )
			throw new InvalidOperationException( "Supply the unchanged selection tuple for the outstanding playback attempt." );
		playbackAttempt = null;
		if ( !playbackSucceeded )
			return;
		var old = GetHistory( pending.Advice.MessageId );
		history[pending.Advice.MessageId] = old with
		{
			SavedGameTick = liveGameTickAfterAttempt,
			// App 0x8a3c retains the chosen r29 across the external wrapper.
			PreviousVariant = pending.Variant,
			HasBeenPlayed = true,
		};
		LastActionStarted = unscaledClockAfterAttempt;
		LastActionDuration = unchecked(returnedPlaybackSpan + 1000);
	}
}
