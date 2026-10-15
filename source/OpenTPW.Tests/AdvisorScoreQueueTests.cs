using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;


/// <summary>
/// Regression cases of the reviewed standalone queue model (tools/ppc-analysis/lanes/advisor/ScoreQueueTests.cs
/// in the PowerPC advisor lane), run against the runtime port. Message IDs 10000+ and response IDs 50000+
/// are synthetic, outside the original tables.
/// </summary>
[TestClass]
public class AdvisorScoreQueueTests
{
	private const int FirstMessage = 10000;
	private static AdvisorMessageDescriptor Descriptor( int id, int variants = 3,
		uint repeat = 0, bool once = false, uint slaps = 0, byte duplicates = 255, byte group = 0 ) =>
		new( id, 50000 + id, variants, group, repeat, once, slaps, duplicates );

	private static AdvisorScoreQueue Queue( IEnumerable<AdvisorMessageDescriptor>? descriptors = null,
		IReadOnlyDictionary<int, AdvisorMessageHistory>? history = null ) =>
		new( descriptors ?? Enumerable.Range( FirstMessage, 20 ).Select( id => Descriptor( id ) ),
			history ?? new Dictionary<int, AdvisorMessageHistory>(), 25 );

	private static void Equal<T>( T expected, T actual ) where T : notnull => Assert.AreEqual( expected, actual );

	private static void InvalidAttempt( Action action )
	{
		try { action(); }
		catch ( InvalidOperationException ) { return; }
		throw new InvalidOperationException( "Expected a rejected playback attempt." );
	}

	private static void True( bool value ) => Equal( true, value );
	private static void False( bool value ) => Equal( false, value );
	private static AdvisorPendingAdvice Advice( int offset, int score,
		int variant = -1, bool overrideOnce = false ) => new( FirstMessage + offset, score, variant, overrideOnce );

	private static AdvisorResponseSelection Selected( AdvisorScoreQueue queue, uint clock = 1000 ) =>
		queue.SelectNext( clock ) ?? throw new InvalidOperationException( "Expected a selected record." );

	private static void Complete( AdvisorScoreQueue queue, AdvisorResponseSelection selection,
		bool succeeded, uint clock, uint tick, uint span )
	{
		queue.BeginPlaybackAttempt( selection );
		queue.CompletePlaybackAttempt( selection, succeeded, clock, tick, span );
	}

	[TestMethod]
	public void EightSlotsUseFirstFree()
	{
		var queue = Queue();
		for ( var index = 0; index < 8; index++ )
			Equal( index, queue.Enqueue( Advice( index, 30 ), 100, true ).Slot );
		Equal( 8, queue.Count );
		var selection = Selected( queue );
		Complete( queue, selection, false, 1000, 100, 0 );
		Equal( 0, queue.Enqueue( Advice( 9, 0 ), 100, true ).Slot );
	}

	[TestMethod]
	public void FullQueueRetainsEarliestEqualMinimum()
	{
		var queue = Queue();
		for ( var index = 0; index < 8; index++ )
			queue.Enqueue( Advice( index, index < 2 ? 30 : 100 ), 100, true );
		var replacement = queue.Enqueue( Advice( 8, 31 ), 100, true );
		True( replacement.Stored );
		Equal( 0, replacement.Slot );
		Equal( FirstMessage + 1, queue.GetSlot( 1 )!.MessageId );
	}

	[TestMethod]
	public void FullQueueAcknowledgesWithoutStoringEqualOrLower()
	{
		var queue = Queue();
		for ( var index = 0; index < 8; index++ )
			queue.Enqueue( Advice( index, 30 ), 100, true );
		foreach ( var score in new[] { 30, 0, -1 } )
		{
			var result = queue.Enqueue( Advice( 8, score ), 100, true );
			True( result.Acknowledged );
			False( result.Stored );
			Equal( FirstMessage, queue.GetSlot( 0 )!.MessageId );
		}
	}

	[TestMethod]
	public void EarliestEqualMaximumIsSelected()
	{
		var queue = Queue();
		queue.Enqueue( Advice( 0, 100 ), 100, true );
		queue.Enqueue( Advice( 1, 100 ), 100, true );
		Equal( 0, Selected( queue ).Slot );
	}

	[TestMethod]
	public void ZeroAndThresholdScoresCanWaitButCannotPlay()
	{
		var queue = Queue();
		True( queue.Enqueue( Advice( 0, 0 ), 100, true ).Stored );
		True( queue.Enqueue( Advice( 1, 25 ), 100, true ).Stored );
		False( queue.SelectNext( 1000 ).HasValue );
		Equal( 2, queue.Count );
		queue.Enqueue( Advice( 2, 26 ), 100, true );
		Equal( FirstMessage + 2, Selected( queue ).Advice.MessageId );
	}

	[TestMethod]
	public void RecomputedPlaybackEqualityDiffersFromCachedSelection()
	{
		var queue = Queue();
		True( queue.RevalidatedPlaybackScoreAccepts( 25 ) );
		False( queue.RevalidatedPlaybackScoreAccepts( 24 ) );
		queue.Enqueue( Advice( 0, 25 ), 100, true );
		False( queue.SelectNext( 1000 ).HasValue );
	}

	[TestMethod]
	public void BusyPlayingCannotBePreempted()
	{
		var queue = Queue();
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		Complete( queue, Selected( queue ), true, 1000, 100, 500 );
		queue.Enqueue( Advice( 1, 100000 ), 101, true );
		False( queue.SelectNext( 2499 ).HasValue );
		Equal( FirstMessage + 1, Selected( queue, 2500 ).Advice.MessageId );
	}

	[TestMethod]
	public void BusyEndpointUsesUnsignedWrappingSum()
	{
		True( AdvisorScoreQueue.IsActionBusy( 0x80000000, 0x7FFFFFFF, 2 ) );
		False( AdvisorScoreQueue.IsActionBusy( 10, uint.MaxValue - 99, 100 ) );
		False( AdvisorScoreQueue.IsActionBusy( 100, 90, 10 ) );
	}

	[TestMethod]
	public void PendingIsConsumedBeforeExternalWrapperResult()
	{
		var queue = Queue( new[] { Descriptor( FirstMessage, duplicates: 1 ), Descriptor( FirstMessage + 1 ) } );
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		var selection = Selected( queue );
		queue.BeginPlaybackAttempt( selection );
		Equal( 0, queue.Count );
		Equal( AdvisorMessageHistory.Empty, queue.GetHistory( FirstMessage ) );
		// An external wrapper can observe the freed slot before returning its result.
		True( queue.Enqueue( Advice( 0, 40 ), 100, true ).Stored );
		queue.CompletePlaybackAttempt( selection, false, 1000, 100, 0 );
		Equal( 1, queue.Count );
		Equal( 40, queue.GetSlot( 0 )!.Score );
	}

	[TestMethod]
	public void FailedPlaybackConsumesWithoutUpdatingHistory()
	{
		var queue = Queue();
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		Complete( queue, Selected( queue ), false, 1000, 100, 500 );
		Equal( 0, queue.Count );
		Equal( AdvisorMessageHistory.Empty, queue.GetHistory( FirstMessage ) );
		Equal( 0u, queue.LastActionDuration );
	}

	[TestMethod]
	public void CyclicVariantsStartFirstThenWrap()
	{
		var queue = Queue();
		for ( var index = 0; index < 5; index++ )
		{
			queue.Enqueue( Advice( 0, 30 ), 100, true );
			var selection = Selected( queue, (uint)(1000 + index * 2000) );
			Equal( index % 3, selection.Variant );
			Equal( 60000 + index % 3, selection.ResponseId );
			Complete( queue, selection, true, (uint)(1000 + index * 2000), 100, 0 );
		}
	}

	[TestMethod]
	public void ExplicitVariantOutsideRangeFallsBackToFirst()
	{
		var queue = Queue();
		queue.Enqueue( Advice( 0, 30, variant: 3 ), 100, true );
		var selection = Selected( queue );
		Equal( 3, selection.Variant );
		Equal( 60000, selection.ResponseId );
		Complete( queue, selection, true, 1000, 100, 0 );
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		Equal( 0, Selected( queue, 2000 ).Variant );
	}

	[TestMethod]
	public void VariantSignedOverflowIsNotRepaired()
	{
		var queue = Queue( history: new Dictionary<int, AdvisorMessageHistory>
		{
			[FirstMessage] = new( 0, int.MaxValue, false, 0 ),
		} );
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		Equal( int.MinValue, Selected( queue ).Variant );
		Equal( unchecked(60000 + int.MinValue), Selected( queue ).ResponseId );
	}

	[TestMethod]
	public void RepeatBoundaryUsesQuarterTickEquality()
	{
		var queue = Queue( new[] { Descriptor( FirstMessage, repeat: 120 ) },
			new Dictionary<int, AdvisorMessageHistory> { [FirstMessage] = new( 400, -1, false, 0 ) } );
		Equal( AdvisorEligibility.RepeatDelay, queue.Eligibility( Advice( 0, 30 ), 879, true ) );
		Equal( AdvisorEligibility.Eligible, queue.Eligibility( Advice( 0, 30 ), 880, true ) );
		Equal( 1u, AdvisorScoreQueue.HistoryElapsed( 4, 3 ) );
	}

	[TestMethod]
	public void ZeroSavedQuarterSkipsRepeatGate()
	{
		for ( uint saved = 0; saved < 4; saved++ )
		{
			var queue = Queue( new[] { Descriptor( FirstMessage, repeat: uint.MaxValue ) },
				new Dictionary<int, AdvisorMessageHistory> { [FirstMessage] = new( saved, -1, false, 0 ) } );
			Equal( AdvisorEligibility.Eligible, queue.Eligibility( Advice( 0, 30 ), 4, true ) );
			var once = Queue( new[] { Descriptor( FirstMessage, repeat: uint.MaxValue, once: true ) },
				new Dictionary<int, AdvisorMessageHistory> { [FirstMessage] = new( saved, 0, true, 0 ) } );
			Equal( AdvisorEligibility.AlreadyPlayed, once.Eligibility( Advice( 0, 30 ), 4, true ) );
		}
	}

	[TestMethod]
	public void CounterWrapRemainsUnsignedShiftedSubtraction()
	{
		Equal( 0xC0000001u, AdvisorScoreQueue.HistoryElapsed( 0, 0xFFFFFFFC ) );
		var queue = Queue( new[] { Descriptor( FirstMessage, repeat: 120 ) },
			new Dictionary<int, AdvisorMessageHistory> { [FirstMessage] = new( 0xFFFFFFFC, -1, false, 0 ) } );
		Equal( AdvisorEligibility.Eligible, queue.Eligibility( Advice( 0, 30 ), 0, true ) );
	}

	[TestMethod]
	public void OnceAndSlapsRespectTheirOverrides()
	{
		var once = Queue( new[] { Descriptor( FirstMessage, once: true ) },
			new Dictionary<int, AdvisorMessageHistory> { [FirstMessage] = new( 0, 0, true, 0 ) } );
		Equal( AdvisorEligibility.AlreadyPlayed, once.Eligibility( Advice( 0, 30 ), 100, true ) );
		Equal( AdvisorEligibility.Eligible, once.Eligibility( Advice( 0, 30, overrideOnce: true ), 100, true ) );
		var slaps = Queue( new[] { Descriptor( FirstMessage, slaps: 3 ) },
			new Dictionary<int, AdvisorMessageHistory> { [FirstMessage] = new( 0, 0, false, 3 ) } );
		Equal( AdvisorEligibility.TooManySlaps, slaps.Eligibility( Advice( 0, 30 ), 100, true ) );
		Equal( AdvisorEligibility.Eligible, slaps.Eligibility( Advice( 0, 30, overrideOnce: true ), 100, true ) );
	}

	[TestMethod]
	public void SlapsCompareSignedStoredBits()
	{
		var queue = Queue( new[] { Descriptor( FirstMessage, slaps: 3 ) },
			new Dictionary<int, AdvisorMessageHistory> { [FirstMessage] = new( 0, 0, false, uint.MaxValue ) } );
		Equal( AdvisorEligibility.Eligible, queue.Eligibility( Advice( 0, 30 ), 100, true ) );
	}

	[TestMethod]
	public void OverrideDoesNotBypassRepeatOrTutorial()
	{
		var queue = Queue( new[] { Descriptor( FirstMessage, repeat: 120, once: true, group: 1 ) },
			new Dictionary<int, AdvisorMessageHistory> { [FirstMessage] = new( 400, 0, true, 100 ) } );
		Equal( AdvisorEligibility.TutorialDisabled, queue.Eligibility( Advice( 0, 30, overrideOnce: true ), 500, false ) );
		Equal( AdvisorEligibility.RepeatDelay, queue.Eligibility( Advice( 0, 30, overrideOnce: true ), 500, true ) );
	}

	[TestMethod]
	public void DuplicateLimitIncludesAlreadyPendingSameMessage()
	{
		var queue = Queue( new[] { Descriptor( FirstMessage, duplicates: 1 ), Descriptor( FirstMessage + 1, duplicates: 0 ) } );
		True( queue.Enqueue( Advice( 0, 30 ), 100, true ).Stored );
		Equal( AdvisorEligibility.DuplicateLimit, queue.Enqueue( Advice( 0, 100, overrideOnce: true ), 100, true ).Eligibility );
		Equal( AdvisorEligibility.DuplicateLimit, queue.Enqueue( Advice( 1, 100 ), 100, true ).Eligibility );
	}

	[TestMethod]
	public void OnceHistoryDoesNotPurgePreviouslyAdmittedRecords()
	{
		var queue = Queue( new[] { Descriptor( FirstMessage, once: true, duplicates: 255 ) } );
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		Complete( queue, Selected( queue ), true, 1000, 100, 0 );
		Equal( AdvisorEligibility.AlreadyPlayed, queue.Eligibility( Advice( 0, 30 ), 100, true ) );
		Equal( 1, Selected( queue, 2000 ).Variant );
	}

	[TestMethod]
	public void CompletionUsesExplicitPostAttemptClocks()
	{
		var queue = Queue();
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		Complete( queue, Selected( queue, 1000 ), true, 1100, 444, 100 );
		Equal( 1100u, queue.LastActionStarted );
		Equal( 1100u, queue.LastActionDuration );
		Equal( 444u, queue.GetHistory( FirstMessage ).SavedGameTick );
		False( queue.IsBusy( 2200 ) );
	}

	[TestMethod]
	public void WrappingPlaybackReservationIsPreserved()
	{
		var queue = Queue();
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		Complete( queue, Selected( queue ), true, 1000, 100, uint.MaxValue );
		Equal( 999u, queue.LastActionDuration );
		True( queue.IsBusy( 1998 ) );
		False( queue.IsBusy( 1999 ) );
	}

	private static void RejectMutationAndFinishOriginal(
		Func<AdvisorResponseSelection, AdvisorResponseSelection> mutate )
	{
		var queue = Queue();
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		var selection = Selected( queue );
		Equal( 0, selection.Variant );
		queue.BeginPlaybackAttempt( selection );
		InvalidAttempt( () => queue.CompletePlaybackAttempt( mutate( selection ), true, 9000, 999, 999 ) );
		Equal( AdvisorMessageHistory.Empty, queue.GetHistory( FirstMessage ) );
		Equal( 0u, queue.LastActionStarted );
		Equal( 0u, queue.LastActionDuration );
		// A rejected callback must leave the authentic in-flight tuple completable.
		queue.CompletePlaybackAttempt( selection, true, 1000, 100, 50 );
		Equal( new AdvisorMessageHistory( 100, 0, true, 0 ), queue.GetHistory( FirstMessage ) );
		Equal( 1000u, queue.LastActionStarted );
		Equal( 1050u, queue.LastActionDuration );
	}

	[TestMethod]
	public void ModifiedVariantCannotRewriteHistory() =>
		RejectMutationAndFinishOriginal( selection => selection with { Variant = 2 } );

	[TestMethod]
	public void ModifiedResponseCannotComplete() =>
		RejectMutationAndFinishOriginal( selection => selection with { ResponseId = selection.ResponseId + 2 } );

	[TestMethod]
	public void ModifiedSlotCannotComplete() =>
		RejectMutationAndFinishOriginal( selection => selection with { Slot = selection.Slot + 1 } );

	[TestMethod]
	public void ModifiedAdviceCannotComplete()
	{
		// Identity matters even when a replacement advice record has identical values.
		RejectMutationAndFinishOriginal( selection => selection with { Advice = selection.Advice with { } } );
		RejectMutationAndFinishOriginal( selection => selection with
		{
			Advice = selection.Advice with { MessageId = FirstMessage + 1 },
		} );
		RejectMutationAndFinishOriginal( selection => selection with
		{
			Advice = selection.Advice with { Score = 1000 },
		} );
		RejectMutationAndFinishOriginal( selection => selection with
		{
			Advice = selection.Advice with { ResponseVariantOverride = 2 },
		} );
		RejectMutationAndFinishOriginal( selection => selection with
		{
			Advice = selection.Advice with { OverrideOnlyOnce = true },
		} );
	}

	[TestMethod]
	public void DuplicateCompletionCannotRewriteHistory()
	{
		var queue = Queue();
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		var selection = Selected( queue );
		Complete( queue, selection, true, 1000, 100, 50 );
		InvalidAttempt( () => queue.CompletePlaybackAttempt( selection, true, 9000, 999, 999 ) );
		Equal( new AdvisorMessageHistory( 100, 0, true, 0 ), queue.GetHistory( FirstMessage ) );
		Equal( 1000u, queue.LastActionStarted );
		Equal( 1050u, queue.LastActionDuration );
	}

	[TestMethod]
	public void StaleCompletionCannotConsumeCurrentAttempt()
	{
		var queue = Queue();
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		var previous = Selected( queue );
		Complete( queue, previous, false, 1000, 100, 0 );
		// Reuse the slot and message, but admit a distinct pending advice record.
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		var current = Selected( queue );
		queue.BeginPlaybackAttempt( current );
		InvalidAttempt( () => queue.CompletePlaybackAttempt( previous, true, 9000, 999, 999 ) );
		Equal( AdvisorMessageHistory.Empty, queue.GetHistory( FirstMessage ) );
		queue.CompletePlaybackAttempt( current, true, 1000, 100, 50 );
		Equal( new AdvisorMessageHistory( 100, 0, true, 0 ), queue.GetHistory( FirstMessage ) );
	}

	[TestMethod]
	public void EmptySelectionCannotComplete()
	{
		try { Queue().CompletePlaybackAttempt( default, false, 0, 0, 0 ); }
		catch ( InvalidOperationException ) { return; }
		throw new InvalidOperationException( "Expected an empty-selection failure." );
	}

	[TestMethod]
	public void UnknownDescriptorsAndStaleCompletionsFail()
	{
		var queue = Queue();
		try { queue.Enqueue( new( -1, 30 ), 100, true ); }
		catch ( KeyNotFoundException )
		{
			queue.Enqueue( Advice( 0, 30 ), 100, true );
			var selection = Selected( queue );
			Complete( queue, selection, false, 1000, 100, 0 );
			try { queue.CompletePlaybackAttempt( selection, true, 1000, 100, 0 ); }
			catch ( InvalidOperationException ) { return; }
		}
		throw new InvalidOperationException( "Expected unknown-descriptor and stale-completion failures." );
	}
}
