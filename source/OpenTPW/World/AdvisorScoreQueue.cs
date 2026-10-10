namespace OpenTPW;

/// <summary>
/// The controls of one scored advisor message: its descriptor (message ID, first response,
/// response count, group, pending-duplicate limit) joined with its group's Advisor.sam controls.
/// </summary>
internal readonly record struct AdvisorMessageDescriptor(
	int MessageId, int FirstResponseId, int ResponseCount, byte MessageGroup,
	uint MinimumRepeatGroups, bool SayOnlyOnce, uint DiscardAfterSlaps, byte MaximumPendingDuplicates );

/// <summary>Per-message history: saved raw game tick, previous response variant, played flag and slap count.</summary>
internal readonly record struct AdvisorMessageHistory(
	uint SavedGameTick, int PreviousVariant, bool HasBeenPlayed, uint SlapCount )
{
	public static AdvisorMessageHistory Empty => new( 0, -1, false, 0 );
}

/// <summary>One pending advice record (<c>mMessage</c>, <c>mScore</c>, <c>mResponseNo</c>, <c>mOverrideOnlyOnce</c>).</summary>
internal sealed record AdvisorPendingAdvice(
	int MessageId, int Score, int ResponseVariantOverride = -1, bool OverrideOnlyOnce = false );

internal enum AdvisorEligibility
{
	Eligible, TutorialDisabled, RepeatDelay, AlreadyPlayed, TooManySlaps, DuplicateLimit,
}

internal readonly record struct AdvisorQueueAdmission( AdvisorEligibility Eligibility, bool Stored, int Slot )
{
	// [BIN:STP-PPC:0x10008D10 advisor enqueue] an eligible advice is acknowledged even when a full queue keeps it out
	public bool Acknowledged => Eligibility == AdvisorEligibility.Eligible;
}

internal readonly record struct AdvisorResponseSelection( int Slot, AdvisorPendingAdvice Advice, int Variant, int ResponseId );

/// <summary>
/// The original advisor controller's pending-advice queue and per-message history, ported from the
/// reviewed standalone model in the PowerPC advisor lane (docs/reverse/PPC-advisor.md, "Phase four").
/// Eight pending records; admission checks eligibility, fills the first free slot or replaces the
/// earliest weakest record only for a strictly higher score; selection takes the earliest strictly
/// highest cached score strictly above the minimum, waits while the previous action is busy and
/// chooses the shipped cyclic response variant. Scores, raw game ticks, the advisor clock and the
/// playback outcome are inputs; this class neither evaluates score producers nor plays audio.
/// </summary>
internal sealed class AdvisorScoreQueue
{
	// [BIN:STP-PPC:0x1000BC10 advisor record serialization] eight 24-byte pending records from controller +20
	public const int Capacity = 8;
	private readonly Dictionary<int, AdvisorMessageDescriptor> descriptors;
	private readonly Dictionary<int, AdvisorMessageHistory> history;
	private readonly AdvisorPendingAdvice?[] slots = new AdvisorPendingAdvice?[Capacity];
	private AdvisorResponseSelection? playbackAttempt;

	public AdvisorScoreQueue( IEnumerable<AdvisorMessageDescriptor> descriptors,
		IReadOnlyDictionary<int, AdvisorMessageHistory> initialHistory, int minimumScore )
	{
		ArgumentNullException.ThrowIfNull( descriptors );
		ArgumentNullException.ThrowIfNull( initialHistory );
		this.descriptors = descriptors.ToDictionary( item => item.MessageId );
		if ( this.descriptors.Values.Any( item => item.ResponseCount <= 0 ) )
			throw new ArgumentException( "Supply a positive response count for the shipped cyclic mode.", nameof( descriptors ) );
		if ( initialHistory.Keys.Any( id => !this.descriptors.ContainsKey( id ) ) )
			throw new ArgumentException( "History needs an explicitly supplied descriptor.", nameof( initialHistory ) );
		history = initialHistory.ToDictionary( pair => pair.Key, pair => pair.Value );
		MinimumScore = minimumScore;
	}

	/// <summary><c>GeneralAdvisor.MinScoreForConsideration</c>.</summary>
	public int MinimumScore { get; }
	public uint LastActionStarted { get; private set; }
	public uint LastActionDuration { get; private set; }
	public int Count => slots.Count( slot => slot != null );
	public bool IsAttemptOutstanding => playbackAttempt.HasValue;

	public AdvisorPendingAdvice? GetSlot( int index ) => slots[index];
	public AdvisorMessageHistory GetHistory( int messageId ) =>
		history.TryGetValue( messageId, out var value ) ? value : AdvisorMessageHistory.Empty;
	public bool HasDescriptor( int messageId ) => descriptors.ContainsKey( messageId );

	/// <summary>
	/// Game event 10 (<c>RESET_FOR_EASY_MODE</c>): every history record resets its variant, played flag
	/// and slap count but keeps its saved game tick, so the repeat delay still runs. Pending records stay.
	/// </summary>
	// [BIN:STP-PPC:0x10009DCC advisor event 10] the loop 0x10009DD8–0x10009E80 over the 351 records stores only +0xE4 (variant −1), +0xE8 (played 0) and +0xEC (slap count 0); the saved tick +0xE0 (written through 0x10121098, read at 0x10009090) is untouched
	public void ClearHistory()
	{
		foreach ( var id in history.Keys.ToArray() )
			history[id] = AdvisorMessageHistory.Empty with { SavedGameTick = history[id].SavedGameTick };
	}

	// [BIN:STP-PPC:0x101210F8 history elapsed] (live mGameTick >> 2) − (saved >> 2), plain 32-bit subtraction
	public static uint HistoryElapsed( uint liveGameTick, uint savedGameTick ) =>
		unchecked((liveGameTick >> 2) - (savedGameTick >> 2));

	// [BIN:STP-PPC:0x1000A154 advisor busy] 32-bit start + duration, unsigned now < end; equality releases
	public static bool IsActionBusy( uint advisorClock, uint started, uint duration ) =>
		advisorClock < unchecked(started + duration);

	public bool IsBusy( uint advisorClock ) => IsActionBusy( advisorClock, LastActionStarted, LastActionDuration );

	/// <summary>The playback wrapper's recomputed-score check accepts a score equal to the minimum, unlike cached selection.</summary>
	// [BIN:STP-PPC:0x1000B858 playback revalidation] recomputed score >= minimum passes
	public bool RevalidatedPlaybackScoreAccepts( int recomputedScore ) => recomputedScore >= MinimumScore;

	public AdvisorEligibility Eligibility( AdvisorPendingAdvice advice, uint liveGameTick, bool tutorialEnabled )
	{
		ArgumentNullException.ThrowIfNull( advice );
		var descriptor = descriptors[advice.MessageId];
		var previous = GetHistory( advice.MessageId );
		// [BIN:STP-PPC:0x10009038 advisor eligibility] tutorial option byte +53 is tested first; with it clear, group-1 (tutorial) messages are refused (reviewed lane model)
		if ( descriptor.MessageGroup == 1 && !tutorialEnabled )
			return AdvisorEligibility.TutorialDisabled;
		// [BIN:STP-PPC:0x100090A4 repeat gate] saved ticks 0–3 (zero saved quarter) skip the interval; strictly fewer groups than the interval refuse
		if ( previous.SavedGameTick >> 2 != 0 &&
			HistoryElapsed( liveGameTick, previous.SavedGameTick ) < descriptor.MinimumRepeatGroups )
			return AdvisorEligibility.RepeatDelay;
		// [BIN:STP-PPC:0x10009100 once override] an override skips the once-only and slap checks only
		if ( !advice.OverrideOnlyOnce )
		{
			if ( descriptor.SayOnlyOnce && previous.HasBeenPlayed )
				return AdvisorEligibility.AlreadyPlayed;
			// [BIN:STP-PPC:0x1000E0FC slap limit] signed comparison of the stored 32-bit count against a nonzero limit
			if ( descriptor.DiscardAfterSlaps != 0 &&
				unchecked((int)previous.SlapCount) >= unchecked((int)descriptor.DiscardAfterSlaps) )
				return AdvisorEligibility.TooManySlaps;
		}
		// [BIN:STP-PPC:0x10009230 duplicate limit] pending copies must stay strictly below the descriptor's low-byte maximum
		var duplicates = slots.Count( item => item?.MessageId == advice.MessageId );
		return duplicates < descriptor.MaximumPendingDuplicates
			? AdvisorEligibility.Eligible : AdvisorEligibility.DuplicateLimit;
	}

	public AdvisorQueueAdmission Enqueue( AdvisorPendingAdvice advice, uint liveGameTick, bool tutorialEnabled )
	{
		var eligibility = Eligibility( advice, liveGameTick, tutorialEnabled );
		if ( eligibility != AdvisorEligibility.Eligible )
			return new( eligibility, false, -1 );
		// [BIN:STP-PPC:0x10008B78 advisor enqueue] no minimum-score gate on admission; first empty slot
		var free = Array.FindIndex( slots, item => item == null );
		if ( free >= 0 )
		{
			slots[free] = advice;
			return new( eligibility, true, free );
		}
		// [BIN:STP-PPC:0x100093D0 minimum scan] only a strictly smaller score moves the weakest slot, so ties keep the earliest
		var weakest = 0;
		for ( var index = 1; index < Capacity; index++ )
			if ( slots[index]!.Score < slots[weakest]!.Score )
				weakest = index;
		// [BIN:STP-PPC:0x10008CD8 replacement] the weakest record is replaced only by a strictly higher score
		if ( advice.Score <= slots[weakest]!.Score )
			return new( eligibility, false, weakest );
		slots[weakest] = advice;
		return new( eligibility, true, weakest );
	}

	/// <summary>
	/// Chooses from cached scores. Eligibility was applied on admission and is not re-applied to
	/// already-pending records. Does not start or consume playback.
	/// </summary>
	public AdvisorResponseSelection? SelectNext( uint advisorClock )
	{
		// [BIN:STP-PPC:0x100087C8 controller update] a busy action exits before queued advice is picked
		if ( IsBusy( advisorClock ) )
			return null;
		// [BIN:STP-PPC:0x10009494 maximum scan] only a strictly greater score moves the choice, so ties keep the earliest valid slot
		var best = -1;
		for ( var index = 0; index < Capacity; index++ )
			if ( slots[index] is { } item && (best < 0 || item.Score > slots[best]!.Score) )
				best = index;
		// [BIN:STP-PPC:0x10008850 minimum score] the candidate must be strictly above MinScoreForConsideration
		if ( best < 0 || slots[best]!.Score <= MinimumScore )
			return null;
		var advice = slots[best]!;
		var descriptor = descriptors[advice.MessageId];
		var variant = advice.ResponseVariantOverride;
		if ( variant < 0 )
		{
			// [BIN:STP-PPC:0x10008974 cyclic variant] previous variant + 1 (wrapping signed add), reset to 0 unless below the count
			variant = unchecked(GetHistory( advice.MessageId ).PreviousVariant + 1);
			if ( variant >= descriptor.ResponseCount )
				variant = 0;
		}
		// [BIN:STP-PPC:0x1000B8C8 response wrapper] an out-of-range requested variant plays the first response; history keeps the request (0x10008A3C)
		var playbackVariant = variant >= descriptor.ResponseCount ? 0 : variant;
		return new( best, advice, variant, unchecked(descriptor.FirstResponseId + playbackVariant) );
	}

	/// <summary>
	/// Consumes the selected record before the playback wrapper runs. Begin, playback and
	/// <see cref="CompletePlaybackAttempt"/> must be serialized with the unchanged selection.
	/// </summary>
	// [BIN:STP-PPC:0x1000B7D8 playback wrapper] the pending record is invalidated before playback is attempted
	public void BeginPlaybackAttempt( AdvisorResponseSelection selection )
	{
		if ( playbackAttempt.HasValue || (uint)selection.Slot >= Capacity || slots[selection.Slot] == null ||
			!ReferenceEquals( slots[selection.Slot], selection.Advice ) )
			throw new InvalidOperationException( "Supply a current pending selection and finish the preceding attempt first." );
		slots[selection.Slot] = null;
		playbackAttempt = selection;
	}

	/// <summary>
	/// Completes the consumed attempt. A rejected completion keeps the outstanding attempt. Only
	/// success saves the raw game tick, requested variant and played flag, and reserves the returned
	/// playback span plus 1000 advisor clock units; failure does not restore the record.
	/// </summary>
	public void CompletePlaybackAttempt( AdvisorResponseSelection selection,
		bool playbackSucceeded, uint advisorClockAfterAttempt, uint liveGameTickAfterAttempt, uint returnedPlaybackSpan )
	{
		if ( playbackAttempt is not { } pending || pending.Slot != selection.Slot ||
			pending.Variant != selection.Variant || pending.ResponseId != selection.ResponseId ||
			!ReferenceEquals( pending.Advice, selection.Advice ) )
			throw new InvalidOperationException( "Supply the unchanged selection tuple for the outstanding playback attempt." );
		playbackAttempt = null;
		if ( !playbackSucceeded )
			return;
		// [BIN:STP-PPC:0x10008A28 history update] raw game tick, retained variant and played flag after wrapper success
		var old = GetHistory( pending.Advice.MessageId );
		history[pending.Advice.MessageId] = old with
		{
			SavedGameTick = liveGameTickAfterAttempt,
			PreviousVariant = pending.Variant,
			HasBeenPlayed = true,
		};
		// [BIN:STP-PPC:0x10008A10 action reservation] returned playback span plus 1000
		LastActionStarted = advisorClockAfterAttempt;
		LastActionDuration = unchecked(returnedPlaybackSpan + 1000);
	}
}
