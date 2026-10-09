using OpenTPW.PpcEvidence.Advisor;

static class ScoreQueueTests
{
	private const int FirstMessage = 10000;
	private static OriginalAdvisorMessageDescriptor Descriptor( int id, int variants = 3,
		uint repeat = 0, bool once = false, uint slaps = 0, byte duplicates = 255, byte group = 0 ) =>
		new( id, 50000 + id, variants, group, repeat, once, slaps, duplicates );

	private static OriginalAdvisorScoreQueue Queue( IEnumerable<OriginalAdvisorMessageDescriptor>? descriptors = null,
		IReadOnlyDictionary<int, OriginalAdvisorMessageHistory>? history = null ) =>
		new( descriptors ?? Enumerable.Range( FirstMessage, 20 ).Select( id => Descriptor( id ) ),
			history ?? new Dictionary<int, OriginalAdvisorMessageHistory>(), 25 );

	private static void Equal<T>( T expected, T actual ) where T : notnull
	{
		if ( !EqualityComparer<T>.Default.Equals( expected, actual ) )
			throw new InvalidOperationException( $"Expected {expected}; actual {actual}." );
	}

	private static void True( bool value ) => Equal( true, value );
	private static void False( bool value ) => Equal( false, value );
	private static OriginalAdvisorPendingAdvice Advice( int offset, int score,
		int variant = -1, bool overrideOnce = false ) => new( FirstMessage + offset, score, variant, overrideOnce );

	private static OriginalAdvisorResponseSelection Selected( OriginalAdvisorScoreQueue queue, uint clock = 1000 ) =>
		queue.SelectNext( clock ) ?? throw new InvalidOperationException( "Expected a selected record." );

	private static void Complete( OriginalAdvisorScoreQueue queue, OriginalAdvisorResponseSelection selection,
		bool succeeded, uint clock, uint tick, uint span )
	{
		queue.BeginPlaybackAttempt( selection );
		queue.CompletePlaybackAttempt( selection, succeeded, clock, tick, span );
	}

	public static int Run()
	{
		var tests = new (string Name, Action Test)[]
		{
			(nameof( EightSlotsUseFirstFree ), EightSlotsUseFirstFree),
			(nameof( FullQueueRetainsEarliestEqualMinimum ), FullQueueRetainsEarliestEqualMinimum),
			(nameof( FullQueueAcknowledgesWithoutStoringEqualOrLower ), FullQueueAcknowledgesWithoutStoringEqualOrLower),
			(nameof( EarliestEqualMaximumIsSelected ), EarliestEqualMaximumIsSelected),
			(nameof( ZeroAndThresholdScoresCanWaitButCannotPlay ), ZeroAndThresholdScoresCanWaitButCannotPlay),
			(nameof( RecomputedPlaybackEqualityDiffersFromCachedSelection ), RecomputedPlaybackEqualityDiffersFromCachedSelection),
			(nameof( BusyPlayingCannotBePreempted ), BusyPlayingCannotBePreempted),
			(nameof( BusyEndpointUsesUnsignedWrappingSum ), BusyEndpointUsesUnsignedWrappingSum),
			(nameof( PendingIsConsumedBeforeExternalWrapperResult ), PendingIsConsumedBeforeExternalWrapperResult),
			(nameof( FailedPlaybackConsumesWithoutUpdatingHistory ), FailedPlaybackConsumesWithoutUpdatingHistory),
			(nameof( CyclicVariantsStartFirstThenWrap ), CyclicVariantsStartFirstThenWrap),
			(nameof( ExplicitVariantOutsideRangeFallsBackToFirst ), ExplicitVariantOutsideRangeFallsBackToFirst),
			(nameof( VariantSignedOverflowIsNotRepaired ), VariantSignedOverflowIsNotRepaired),
			(nameof( RepeatBoundaryUsesQuarterTickEquality ), RepeatBoundaryUsesQuarterTickEquality),
			(nameof( ZeroSavedQuarterSkipsRepeatGate ), ZeroSavedQuarterSkipsRepeatGate),
			(nameof( CounterWrapRemainsUnsignedShiftedSubtraction ), CounterWrapRemainsUnsignedShiftedSubtraction),
			(nameof( OnceAndSlapsRespectTheirOverrides ), OnceAndSlapsRespectTheirOverrides),
			(nameof( SlapsCompareSignedStoredBits ), SlapsCompareSignedStoredBits),
			(nameof( OverrideDoesNotBypassRepeatOrTutorial ), OverrideDoesNotBypassRepeatOrTutorial),
			(nameof( DuplicateLimitIncludesAlreadyPendingSameMessage ), DuplicateLimitIncludesAlreadyPendingSameMessage),
			(nameof( OnceHistoryDoesNotPurgePreviouslyAdmittedRecords ), OnceHistoryDoesNotPurgePreviouslyAdmittedRecords),
			(nameof( CompletionUsesExplicitPostAttemptClocks ), CompletionUsesExplicitPostAttemptClocks),
			(nameof( WrappingPlaybackReservationIsPreserved ), WrappingPlaybackReservationIsPreserved),
			(nameof( EmptySelectionCannotComplete ), EmptySelectionCannotComplete),
			(nameof( UnknownDescriptorsAndStaleCompletionsFail ), UnknownDescriptorsAndStaleCompletionsFail),
		};
		foreach ( var (name, test) in tests )
		{
			test();
			Console.WriteLine( $"PASS {name}" );
		}
		return tests.Length;
	}

	private static void EightSlotsUseFirstFree()
	{
		var queue = Queue();
		for ( var index = 0; index < 8; index++ )
			Equal( index, queue.Enqueue( Advice( index, 30 ), 100, true ).Slot );
		Equal( 8, queue.Count );
		var selection = Selected( queue );
		Complete( queue, selection, false, 1000, 100, 0 );
		Equal( 0, queue.Enqueue( Advice( 9, 0 ), 100, true ).Slot );
	}

	private static void FullQueueRetainsEarliestEqualMinimum()
	{
		var queue = Queue();
		for ( var index = 0; index < 8; index++ )
			queue.Enqueue( Advice( index, index < 2 ? 30 : 100 ), 100, true );
		var replacement = queue.Enqueue( Advice( 8, 31 ), 100, true );
		True( replacement.Stored );
		Equal( 0, replacement.Slot );
		Equal( FirstMessage + 1, queue.GetSlot( 1 )!.MessageId );
	}

	private static void FullQueueAcknowledgesWithoutStoringEqualOrLower()
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

	private static void EarliestEqualMaximumIsSelected()
	{
		var queue = Queue();
		queue.Enqueue( Advice( 0, 100 ), 100, true );
		queue.Enqueue( Advice( 1, 100 ), 100, true );
		Equal( 0, Selected( queue ).Slot );
	}

	private static void ZeroAndThresholdScoresCanWaitButCannotPlay()
	{
		var queue = Queue();
		True( queue.Enqueue( Advice( 0, 0 ), 100, true ).Stored );
		True( queue.Enqueue( Advice( 1, 25 ), 100, true ).Stored );
		False( queue.SelectNext( 1000 ).HasValue );
		Equal( 2, queue.Count );
		queue.Enqueue( Advice( 2, 26 ), 100, true );
		Equal( FirstMessage + 2, Selected( queue ).Advice.MessageId );
	}

	private static void RecomputedPlaybackEqualityDiffersFromCachedSelection()
	{
		var queue = Queue();
		True( queue.RevalidatedPlaybackScoreAccepts( 25 ) );
		False( queue.RevalidatedPlaybackScoreAccepts( 24 ) );
		queue.Enqueue( Advice( 0, 25 ), 100, true );
		False( queue.SelectNext( 1000 ).HasValue );
	}

	private static void BusyPlayingCannotBePreempted()
	{
		var queue = Queue();
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		Complete( queue, Selected( queue ), true, 1000, 100, 500 );
		queue.Enqueue( Advice( 1, 100000 ), 101, true );
		False( queue.SelectNext( 2499 ).HasValue );
		Equal( FirstMessage + 1, Selected( queue, 2500 ).Advice.MessageId );
	}

	private static void BusyEndpointUsesUnsignedWrappingSum()
	{
		True( OriginalAdvisorScoreQueue.IsActionBusy( 0x80000000, 0x7FFFFFFF, 2 ) );
		False( OriginalAdvisorScoreQueue.IsActionBusy( 10, uint.MaxValue - 99, 100 ) );
		False( OriginalAdvisorScoreQueue.IsActionBusy( 100, 90, 10 ) );
	}

	private static void PendingIsConsumedBeforeExternalWrapperResult()
	{
		var queue = Queue( new[] { Descriptor( FirstMessage, duplicates: 1 ), Descriptor( FirstMessage + 1 ) } );
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		var selection = Selected( queue );
		queue.BeginPlaybackAttempt( selection );
		Equal( 0, queue.Count );
		Equal( OriginalAdvisorMessageHistory.Empty, queue.GetHistory( FirstMessage ) );
		// An external wrapper can observe the freed slot before returning its result.
		True( queue.Enqueue( Advice( 0, 40 ), 100, true ).Stored );
		queue.CompletePlaybackAttempt( selection, false, 1000, 100, 0 );
		Equal( 1, queue.Count );
		Equal( 40, queue.GetSlot( 0 )!.Score );
	}

	private static void FailedPlaybackConsumesWithoutUpdatingHistory()
	{
		var queue = Queue();
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		Complete( queue, Selected( queue ), false, 1000, 100, 500 );
		Equal( 0, queue.Count );
		Equal( OriginalAdvisorMessageHistory.Empty, queue.GetHistory( FirstMessage ) );
		Equal( 0u, queue.LastActionDuration );
	}

	private static void CyclicVariantsStartFirstThenWrap()
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

	private static void ExplicitVariantOutsideRangeFallsBackToFirst()
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

	private static void VariantSignedOverflowIsNotRepaired()
	{
		var queue = Queue( history: new Dictionary<int, OriginalAdvisorMessageHistory>
		{
			[FirstMessage] = new( 0, int.MaxValue, false, 0 ),
		} );
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		Equal( int.MinValue, Selected( queue ).Variant );
		Equal( unchecked(60000 + int.MinValue), Selected( queue ).ResponseId );
	}

	private static void RepeatBoundaryUsesQuarterTickEquality()
	{
		var queue = Queue( new[] { Descriptor( FirstMessage, repeat: 120 ) },
			new Dictionary<int, OriginalAdvisorMessageHistory> { [FirstMessage] = new( 400, -1, false, 0 ) } );
		Equal( OriginalAdvisorEligibility.RepeatDelay, queue.Eligibility( Advice( 0, 30 ), 879, true ) );
		Equal( OriginalAdvisorEligibility.Eligible, queue.Eligibility( Advice( 0, 30 ), 880, true ) );
		Equal( 1u, OriginalAdvisorScoreQueue.HistoryElapsed( 4, 3 ) );
	}

	private static void ZeroSavedQuarterSkipsRepeatGate()
	{
		for ( uint saved = 0; saved < 4; saved++ )
		{
			var queue = Queue( new[] { Descriptor( FirstMessage, repeat: uint.MaxValue ) },
				new Dictionary<int, OriginalAdvisorMessageHistory> { [FirstMessage] = new( saved, -1, false, 0 ) } );
			Equal( OriginalAdvisorEligibility.Eligible, queue.Eligibility( Advice( 0, 30 ), 4, true ) );
			var once = Queue( new[] { Descriptor( FirstMessage, repeat: uint.MaxValue, once: true ) },
				new Dictionary<int, OriginalAdvisorMessageHistory> { [FirstMessage] = new( saved, 0, true, 0 ) } );
			Equal( OriginalAdvisorEligibility.AlreadyPlayed, once.Eligibility( Advice( 0, 30 ), 4, true ) );
		}
	}

	private static void CounterWrapRemainsUnsignedShiftedSubtraction()
	{
		Equal( 0xC0000001u, OriginalAdvisorScoreQueue.HistoryElapsed( 0, 0xFFFFFFFC ) );
		var queue = Queue( new[] { Descriptor( FirstMessage, repeat: 120 ) },
			new Dictionary<int, OriginalAdvisorMessageHistory> { [FirstMessage] = new( 0xFFFFFFFC, -1, false, 0 ) } );
		Equal( OriginalAdvisorEligibility.Eligible, queue.Eligibility( Advice( 0, 30 ), 0, true ) );
	}

	private static void OnceAndSlapsRespectTheirOverrides()
	{
		var once = Queue( new[] { Descriptor( FirstMessage, once: true ) },
			new Dictionary<int, OriginalAdvisorMessageHistory> { [FirstMessage] = new( 0, 0, true, 0 ) } );
		Equal( OriginalAdvisorEligibility.AlreadyPlayed, once.Eligibility( Advice( 0, 30 ), 100, true ) );
		Equal( OriginalAdvisorEligibility.Eligible, once.Eligibility( Advice( 0, 30, overrideOnce: true ), 100, true ) );
		var slaps = Queue( new[] { Descriptor( FirstMessage, slaps: 3 ) },
			new Dictionary<int, OriginalAdvisorMessageHistory> { [FirstMessage] = new( 0, 0, false, 3 ) } );
		Equal( OriginalAdvisorEligibility.TooManySlaps, slaps.Eligibility( Advice( 0, 30 ), 100, true ) );
		Equal( OriginalAdvisorEligibility.Eligible, slaps.Eligibility( Advice( 0, 30, overrideOnce: true ), 100, true ) );
	}

	private static void SlapsCompareSignedStoredBits()
	{
		var queue = Queue( new[] { Descriptor( FirstMessage, slaps: 3 ) },
			new Dictionary<int, OriginalAdvisorMessageHistory> { [FirstMessage] = new( 0, 0, false, uint.MaxValue ) } );
		Equal( OriginalAdvisorEligibility.Eligible, queue.Eligibility( Advice( 0, 30 ), 100, true ) );
	}

	private static void OverrideDoesNotBypassRepeatOrTutorial()
	{
		var queue = Queue( new[] { Descriptor( FirstMessage, repeat: 120, once: true, group: 1 ) },
			new Dictionary<int, OriginalAdvisorMessageHistory> { [FirstMessage] = new( 400, 0, true, 100 ) } );
		Equal( OriginalAdvisorEligibility.TutorialDisabled, queue.Eligibility( Advice( 0, 30, overrideOnce: true ), 500, false ) );
		Equal( OriginalAdvisorEligibility.RepeatDelay, queue.Eligibility( Advice( 0, 30, overrideOnce: true ), 500, true ) );
	}

	private static void DuplicateLimitIncludesAlreadyPendingSameMessage()
	{
		var queue = Queue( new[] { Descriptor( FirstMessage, duplicates: 1 ), Descriptor( FirstMessage + 1, duplicates: 0 ) } );
		True( queue.Enqueue( Advice( 0, 30 ), 100, true ).Stored );
		Equal( OriginalAdvisorEligibility.DuplicateLimit, queue.Enqueue( Advice( 0, 100, overrideOnce: true ), 100, true ).Eligibility );
		Equal( OriginalAdvisorEligibility.DuplicateLimit, queue.Enqueue( Advice( 1, 100 ), 100, true ).Eligibility );
	}

	private static void OnceHistoryDoesNotPurgePreviouslyAdmittedRecords()
	{
		var queue = Queue( new[] { Descriptor( FirstMessage, once: true, duplicates: 255 ) } );
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		Complete( queue, Selected( queue ), true, 1000, 100, 0 );
		Equal( OriginalAdvisorEligibility.AlreadyPlayed, queue.Eligibility( Advice( 0, 30 ), 100, true ) );
		Equal( 1, Selected( queue, 2000 ).Variant );
	}

	private static void CompletionUsesExplicitPostAttemptClocks()
	{
		var queue = Queue();
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		Complete( queue, Selected( queue, 1000 ), true, 1100, 444, 100 );
		Equal( 1100u, queue.LastActionStarted );
		Equal( 1100u, queue.LastActionDuration );
		Equal( 444u, queue.GetHistory( FirstMessage ).SavedGameTick );
		False( queue.IsBusy( 2200 ) );
	}

	private static void WrappingPlaybackReservationIsPreserved()
	{
		var queue = Queue();
		queue.Enqueue( Advice( 0, 30 ), 100, true );
		Complete( queue, Selected( queue ), true, 1000, 100, uint.MaxValue );
		Equal( 999u, queue.LastActionDuration );
		True( queue.IsBusy( 1998 ) );
		False( queue.IsBusy( 1999 ) );
	}

	private static void EmptySelectionCannotComplete()
	{
		try { Queue().CompletePlaybackAttempt( default, false, 0, 0, 0 ); }
		catch ( InvalidOperationException ) { return; }
		throw new InvalidOperationException( "Expected an empty-selection failure." );
	}

	private static void UnknownDescriptorsAndStaleCompletionsFail()
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
