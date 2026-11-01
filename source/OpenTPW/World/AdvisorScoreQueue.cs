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

/// <summary>
/// One pending advice record (<c>mMessage</c>, <c>mScore</c>, <c>mResponseNo</c>, <c>mOverrideOnlyOnce</c>) with the two words the record builder keeps beside them.
/// </summary>
/// <param name="ThingId">The record's word +0x0 (the builder's second argument): the id of the thing the advice is about, 0 for none.</param>
/// <param name="Secondary">The record's dword +0x4 (the third argument): the cell word of the prank lines or the object type id of the ride and research lines, 0 for none.</param>
/// <param name="ScoreOverridden">The record's byte +0x16: set when the record was offered again with a score of its own (the F1 replay of the last tutorial), which is said without being scored again.</param>
// [BIN:TPW-EXE:0x0059B590 advice record] the builder stores its second argument as the word +0x0, its third as the dword +0x4, the variant at +0x8, the message at +0xC, the score at +0x10 with its valid byte +0x14 and flags +0x15/+0x16; the queue copies all six dwords (0x59A940)
// [BIN:TPW-EXE:0x0059B620 advice speech] the record's two words are read when it is said: they ride the category-24 message to the tag system (0x509BF0), whose caption formatter 0x509E10 reads them (docs/LIPS.md, "Captions")
internal sealed record AdvisorPendingAdvice(
	int MessageId, int Score, int ResponseVariantOverride = -1, bool OverrideOnlyOnce = false, ushort ThingId = 0, uint Secondary = 0, bool ScoreOverridden = false );

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
/// What the speech's score check (<c>0x59C0A0</c> as <c>0x59B620</c> calls it) answers: the message's score now and the id and cell words its callback left in the scan object
/// (the record's words are replaced by the nonzero ones).
/// </summary>
internal readonly record struct AdvisorSpeechCheck( int Score, ushort ThingId = 0, uint Secondary = 0 );

/// <summary>What withdrawing a message did: pending records dropped, and whether its speech was cut off.</summary>
internal readonly record struct AdvisorWithdrawal( int PendingRemoved, bool SpeechCancelled );

/// <summary>
/// One pending advice record's own-format state for a park save (a free slot is null). <paramref name="ThingId"/> and <paramref name="Secondary"/> are the record's two words
/// (<see cref="AdvisorPendingAdvice"/>), written only when they are not 0 so that a save without them reads as before.
/// </summary>
internal sealed record AdvisorPendingAdviceSnapshot( int MessageId, int Score, int ResponseVariantOverride, bool OverrideOnlyOnce,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )] int ThingId = 0,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )] uint Secondary = 0,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )] bool ScoreOverridden = false );

/// <summary>One message's history record for a park save (<see cref="AdvisorMessageHistory"/>).</summary>
internal sealed record AdvisorHistorySnapshot( uint SavedGameTick, int PreviousVariant, bool HasBeenPlayed, uint SlapCount );

/// <summary>
/// The queue's own-format state for a park save: the eight pending records in slot order, the per-message
/// history of the bound messages, the action reservation the current speech holds and the last tutorial
/// record (absent in saves from before it was kept: none). The controller has no other mutable state; an
/// outstanding playback attempt cannot be captured (it exists only inside one <see cref="AdvisorController.Update"/> call).
/// </summary>
internal sealed record AdvisorQueueSnapshot(
	IReadOnlyList<AdvisorPendingAdviceSnapshot?> Slots, IReadOnlyDictionary<int, AdvisorHistorySnapshot> History,
	uint LastActionStarted, uint LastActionDuration, int? CurrentMessageId = null, AdvisorPendingAdviceSnapshot? LastTutorial = null );

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
	/// <summary>The message whose response started last (what the original keeps at controller +0x16EC); null before the first one and after a restore.</summary>
	public int? CurrentMessageId { get; private set; }
	public int Count => slots.Count( slot => slot != null );
	/// <summary>
	/// The last tutorial (group 1) advice offered to the queue, whether or not it was admitted (the original's record at controller
	/// +0x16D0, its valid byte at +0x16E4); F1 in a HUD window offers it again (<see cref="ReplayLastTutorial"/>). Null when none is kept.
	/// </summary>
	public AdvisorPendingAdvice? LastTutorial { get; private set; }
	public bool IsAttemptOutstanding => playbackAttempt.HasValue;

	public AdvisorPendingAdvice? GetSlot( int index ) => slots[index];
	public AdvisorMessageHistory GetHistory( int messageId ) =>
		history.TryGetValue( messageId, out var value ) ? value : AdvisorMessageHistory.Empty;
	public bool HasDescriptor( int messageId ) => descriptors.ContainsKey( messageId );
	/// <summary>Whether the message is bound and of group 1 (tutorial), the group 0x59C500 reports as 1.</summary>
	public bool IsTutorial( int messageId ) => descriptors.TryGetValue( messageId, out var descriptor ) && descriptor.MessageGroup == 1;

	/// <summary>
	/// Replaces the pending records and history with a loaded controller's. <paramref name="pending"/> has one entry
	/// per slot (null = free); every message must have a descriptor.
	/// </summary>
	public void Restore( IReadOnlyList<AdvisorPendingAdvice?> pending, IReadOnlyDictionary<int, AdvisorMessageHistory> restoredHistory )
	{
		ArgumentNullException.ThrowIfNull( pending );
		ArgumentNullException.ThrowIfNull( restoredHistory );
		if ( pending.Count != Capacity )
			throw new ArgumentException( $"Supply all {Capacity} pending slots.", nameof( pending ) );
		if ( pending.Any( item => item != null && !descriptors.ContainsKey( item.MessageId ) ) || restoredHistory.Keys.Any( id => !descriptors.ContainsKey( id ) ) )
			throw new ArgumentException( "Restored advice and history need an explicitly supplied descriptor." );
		if ( playbackAttempt.HasValue )
			throw new InvalidOperationException( "Finish the outstanding playback attempt first." );
		for ( var index = 0; index < Capacity; index++ )
			slots[index] = pending[index];
		history.Clear();
		foreach ( var (id, value) in restoredHistory )
			history[id] = value;
	}

	/// <summary>
	/// The queue's state for a park save, deep-copied. Fails explicitly while a playback attempt is
	/// outstanding — between frames it never is, since <see cref="AdvisorController.Update"/> completes the
	/// attempt within the call.
	/// </summary>
	public AdvisorQueueSnapshot CaptureSnapshot()
	{
		if ( playbackAttempt.HasValue )
			throw new InvalidOperationException( "Cannot capture the advisor queue while a playback attempt is outstanding." );
		return new AdvisorQueueSnapshot(
			slots.Select( item => item is { } advice
				? new AdvisorPendingAdviceSnapshot( advice.MessageId, advice.Score, advice.ResponseVariantOverride, advice.OverrideOnlyOnce, advice.ThingId, advice.Secondary, advice.ScoreOverridden )
				: null ).ToArray(),
			history.ToDictionary( pair => pair.Key, pair => new AdvisorHistorySnapshot( pair.Value.SavedGameTick, pair.Value.PreviousVariant, pair.Value.HasBeenPlayed, pair.Value.SlapCount ) ),
			LastActionStarted, LastActionDuration, CurrentMessageId,
			LastTutorial is { } tutorial ? new AdvisorPendingAdviceSnapshot( tutorial.MessageId, tutorial.Score, tutorial.ResponseVariantOverride, tutorial.OverrideOnlyOnce, tutorial.ThingId, tutorial.Secondary, tutorial.ScoreOverridden ) : null );
	}

	/// <summary>
	/// Checks a snapshot against this queue's descriptors without touching it: all eight slots, bound
	/// message IDs, variant overrides of −1 (none) or a request, history records present and only for
	/// bound messages, and
	/// non-negative stored variants.
	/// </summary>
	public void ValidateSnapshot( AdvisorQueueSnapshot snapshot )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		if ( snapshot.Slots is null || snapshot.History is null )
			throw new InvalidDataException( "Advisor queue snapshot is inconsistent: missing slots or history." );
		void Require( bool condition, string message )
		{
			if ( !condition )
				throw new InvalidDataException( $"Advisor queue snapshot is inconsistent: {message}." );
		}
		Require( snapshot.Slots.Count == Capacity, $"supply all {Capacity} pending slots" );
		Require( snapshot.CurrentMessageId is not { } current || descriptors.ContainsKey( current ), "the current message has no bound descriptor" );
		if ( snapshot.LastTutorial is { } tutorial )
		{
			Require( descriptors.TryGetValue( tutorial.MessageId, out var tutorialDescriptor ), $"the last tutorial message {tutorial.MessageId} has no bound descriptor" );
			Require( tutorialDescriptor.MessageGroup == 1, $"the last tutorial message {tutorial.MessageId} is not of the tutorial group" );
			Require( tutorial.ResponseVariantOverride >= -1, $"the last tutorial message {tutorial.MessageId}'s variant override is below none" );
			Require( tutorial.ThingId is >= 0 and <= ushort.MaxValue, $"the last tutorial message {tutorial.MessageId}'s thing id is not a word" );
		}
		foreach ( var item in snapshot.Slots )
			if ( item != null )
			{
				Require( descriptors.ContainsKey( item.MessageId ), $"message {item.MessageId} has no bound descriptor" );
				Require( item.ResponseVariantOverride >= -1, $"message {item.MessageId}'s variant override is below none" );
				Require( item.ThingId is >= 0 and <= ushort.MaxValue, $"message {item.MessageId}'s thing id is not a word" );
			}
		foreach ( var (id, entry) in snapshot.History )
		{
			Require( entry is not null, $"history message {id}'s record is missing" );
			Require( descriptors.ContainsKey( id ), $"history message {id} has no bound descriptor" );
			Require( entry.PreviousVariant >= -1, $"history message {id}'s previous variant is below none" );
		}
	}

	/// <summary>
	/// Continues this queue (a fresh controller's, with the same balance) from a validated snapshot: the
	/// pending records, the history and the current speech's action reservation all resume.
	/// </summary>
	public void RestoreSnapshot( AdvisorQueueSnapshot snapshot )
	{
		ValidateSnapshot( snapshot );
		Restore( snapshot.Slots!.Select( item => item is { } advice
				? new AdvisorPendingAdvice( advice.MessageId, advice.Score, advice.ResponseVariantOverride, advice.OverrideOnlyOnce, (ushort)advice.ThingId, advice.Secondary, advice.ScoreOverridden )
				: null ).ToArray(),
			snapshot.History!.ToDictionary( pair => pair.Key, pair => new AdvisorMessageHistory( pair.Value.SavedGameTick, pair.Value.PreviousVariant, pair.Value.HasBeenPlayed, pair.Value.SlapCount ) ) );
		LastActionStarted = snapshot.LastActionStarted;
		LastActionDuration = snapshot.LastActionDuration;
		// The reservation resumes with the message it belongs to (absent in saves from before help withdrawal: none).
		CurrentMessageId = snapshot.CurrentMessageId;
		LastTutorial = snapshot.LastTutorial is { } tutorial
			? new AdvisorPendingAdvice( tutorial.MessageId, tutorial.Score, tutorial.ResponseVariantOverride, tutorial.OverrideOnlyOnce, (ushort)tutorial.ThingId, tutorial.Secondary, tutorial.ScoreOverridden ) : null;
	}

	/// <summary>Replaces the last tutorial record with a loaded controller's (an original save's record at +0x16D0); it must be a bound tutorial message.</summary>
	public void RestoreLastTutorial( AdvisorPendingAdvice? tutorial )
	{
		if ( tutorial != null && !IsTutorial( tutorial.MessageId ) )
			throw new ArgumentException( "The last tutorial record needs a bound tutorial-group descriptor.", nameof( tutorial ) );
		LastTutorial = tutorial;
	}

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
		ArgumentNullException.ThrowIfNull( advice );
		// [BIN:TPW-EXE:0x0059A940 advisor enqueue] before the eligibility check 0x59ABC0, a record whose message is of group 1 (0x59C500 == 1) is copied whole (rep movsd, 6 dwords) to controller +0x16D0, admitted or not
		if ( IsTutorial( advice.MessageId ) )
			LastTutorial = advice;
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
			// [BIN:TPW-EXE:0x0059A796 sequential pick] the Windows update switches on the descriptor's word +0x2C (0x59C490): all 351 rows hold mode 2, the previous response + 1 (0x59A796, checked by tools/ppc-analysis/lanes/advisor/tpw_exe_tables.py); the mode-3 branch (a world generator draw, 0x59A7AC) is never taken, so the pick draws nothing
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
		// [BIN:TPW-EXE:0x0059A550 advisor update] the played message is stored at controller +0x16EC after a successful playback
		CurrentMessageId = pending.Advice.MessageId;
	}

	/// <summary>
	/// [EXT:advisor-sliced-decode] The response just played could not be said after all (its decode over frames failed after the line started): the reservation becomes the one
	/// a playback that returned no span makes, 0 + 1000 from the same start, as when the decode fails before the line (<see cref="CompletePlaybackAttempt"/>). A reservation
	/// already ended (a withdrawal) stays ended.
	/// </summary>
	public void ReserveAsUnsaid() => LastActionDuration = Math.Min( LastActionDuration, 1000u );

	/// <summary>
	/// Withdraws <paramref name="messageId"/> (the original's "unadvise", 0x486B40 → 0x59AA70): every pending record of that message is
	/// dropped; and when it is the message being spoken (the action is busy and it is the current message) the speech is cut off: the
	/// message does not count as played any more, its slap count goes up by one (so a help message that is cut off three times is discarded
	/// for good, its group's DiscardAfterSlaps) and the action reservation ends, which frees the controller for the next advice at once.
	/// </summary>
	// [BIN:TPW-EXE:0x0059AA70 unadvise] drops the pending records with the message (0x59AA94-0x59AAB8); if the busy test (now < +0xD8 + +0xDC) holds and +0x16EC is the message, it stops the speech (0x5994E0), clears the played byte (+0xE8), adds one to the slap count (+0xEC) and zeroes +0xDC
	// [BIN:TPW-EXE:0x0059AA7B unadvise] first, while the last tutorial record is valid (+0x16E4), a message of group 1 (0x59C500 == 1) that is the record's message (+0x16DC) clears its valid byte (0x59AAC8)
	public AdvisorWithdrawal Withdraw( int messageId, uint advisorClock )
	{
		if ( LastTutorial?.MessageId == messageId && IsTutorial( messageId ) )
			LastTutorial = null;
		var removed = 0;
		for ( var index = 0; index < Capacity; index++ )
		{
			if ( slots[index]?.MessageId != messageId )
				continue;
			slots[index] = null;
			removed++;
		}
		var cancelled = false;
		if ( descriptors.ContainsKey( messageId ) && CurrentMessageId == messageId && IsBusy( advisorClock ) )
		{
			cancelled = true;
			var old = GetHistory( messageId );
			history[messageId] = old with { HasBeenPlayed = false, SlapCount = unchecked(old.SlapCount + 1) };
			LastActionDuration = 0;
		}
		return new AdvisorWithdrawal( removed, cancelled );
	}

	/// <summary>
	/// F1 in a HUD window: the last tutorial record, when one is kept, is offered to the queue again at <paramref name="contextHelpScore"/>
	/// with its once-only and slap checks overridden (the record itself keeps those changes, admitted or not). Null when none is kept.
	/// </summary>
	// [BIN:TPW-EXE:0x0059AB50 replay last tutorial] with +0x16E4 set: the record's score (+0x16E0) becomes the balance field +0x32C (HelpMessage.ContextHelpScore), its score-overridden byte (+0x16E6) and once-only override (+0x16E5) become 1, and it goes through the enqueue 0x59A940 again
	public AdvisorQueueAdmission? ReplayLastTutorial( int contextHelpScore, uint liveGameTick, bool tutorialEnabled )
	{
		if ( LastTutorial is not { } tutorial )
			return null;
		LastTutorial = tutorial with { Score = contextHelpScore, OverrideOnlyOnce = true, ScoreOverridden = true };
		return Enqueue( LastTutorial, liveGameTick, tutorialEnabled );
	}
}
