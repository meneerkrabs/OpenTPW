using OpenTPW.PpcEvidence.Advisor;

static class DriverTests
{
	private sealed class OriginalRandom
	{
		private readonly Queue<int> values;
		public int Calls { get; private set; }
		public OriginalRandom( params int[] values ) => this.values = new Queue<int>( values );
		public int Next()
		{
			Calls++;
			return values.Count > 0 ? values.Dequeue() : 0;
		}
	}

	public static int Run()
	{
		var tests = new (string Name, Action Test)[]
		{
			(nameof( SignedConversion ), SignedConversion),
			(nameof( InitialTalkingRequiresLoadedLip ), InitialTalkingRequiresLoadedLip),
			(nameof( StrictLipDeadline ), StrictLipDeadline),
			(nameof( MissedFrameConsumesOneMark ), MissedFrameConsumesOneMark),
			(nameof( PauseAndResumeUseSuppliedClock ), PauseAndResumeUseSuppliedClock),
			(nameof( PauseWithExpiredBacklogStillConsumesOneMark ), PauseWithExpiredBacklogStillConsumesOneMark),
			(nameof( ImmediateZeroMarkUsesRecoveredLead ), ImmediateZeroMarkUsesRecoveredLead),
			(nameof( RawNegativeSentinelEndsLip ), RawNegativeSentinelEndsLip),
			(nameof( ConvertedNegativeOneEndsLip ), ConvertedNegativeOneEndsLip),
			(nameof( InitialConvertedNegativeOneIsScheduled ), InitialConvertedNegativeOneIsScheduled),
			(nameof( EarlySentinelStopsWithinBoundedInput ), EarlySentinelStopsWithinBoundedInput),
			(nameof( AllFiveMouthStatesIncludeNormal ), AllFiveMouthStatesIncludeNormal),
			(nameof( StrictMouthDeadlineAndOneRandomDraw ), StrictMouthDeadlineAndOneRandomDraw),
			(nameof( SilentAndTerminalMouthAreNormal ), SilentAndTerminalMouthAreNormal),
			(nameof( DeferredSpeechAcceptsEquality ), DeferredSpeechAcceptsEquality),
			(nameof( DeferredAndLipBoundariesDiffer ), DeferredAndLipBoundariesDiffer),
			(nameof( DeferredMissingLipRequestsSpeechButNormalMouth ), DeferredMissingLipRequestsSpeechButNormalMouth),
			(nameof( PendingZeroDisablesDeferredBranch ), PendingZeroDisablesDeferredBranch),
			(nameof( InputSnapshotAndInvalidInputPreserveState ), InputSnapshotAndInvalidInputPreserveState),
			(nameof( CallerRandomContractIsExplicit ), CallerRandomContractIsExplicit),
			(nameof( MouthCadencePersistsAcrossResponseRequests ), MouthCadencePersistsAcrossResponseRequests),
		};
		foreach ( var (name, test) in tests )
		{
			test();
			Console.WriteLine( $"PASS {name}" );
		}
		return tests.Length;
	}

	private static void Equal<T>( T expected, T actual ) where T : notnull
	{
		if ( !EqualityComparer<T>.Default.Equals( expected, actual ) )
			throw new InvalidOperationException( $"Expected {expected}; actual {actual}." );
	}

	private static void Throws<T>( Action action ) where T : Exception
	{
		try { action(); }
		catch ( T ) { return; }
		throw new InvalidOperationException( $"Expected {typeof( T ).Name}." );
	}

	private static OriginalAdvisorLipDriver Begin( uint[] words, int clock = 1000, bool deferred = false )
	{
		var driver = new OriginalAdvisorLipDriver( () => 0 );
		driver.BeginResponse( words, clock, deferred );
		return driver;
	}

	private static void SignedConversion()
	{
		Equal( 2226, OriginalAdvisorLipDriver.ConvertMarkToClockUnits( 2226893 ) );
		Equal( 0, OriginalAdvisorLipDriver.ConvertMarkToClockUnits( unchecked((uint)-999) ) );
		Equal( -1, OriginalAdvisorLipDriver.ConvertMarkToClockUnits( unchecked((uint)-1999) ) );
		Equal( -2147483, OriginalAdvisorLipDriver.ConvertMarkToClockUnits( 0x80000000 ) );
		Equal( 2147483, OriginalAdvisorLipDriver.ConvertMarkToClockUnits( int.MaxValue ) );
	}

	private static void InitialTalkingRequiresLoadedLip()
	{
		var loaded = Begin( new uint[] { 1000000, uint.MaxValue } );
		Equal( true, loaded.Talking );
		Equal( 1800, loaded.NextLipClock );
		var missing = Begin( Array.Empty<uint>() );
		Equal( false, missing.Talking );
		Equal( false, missing.LipActive );
		Equal( OriginalAdvisorMouth.Normal, missing.Update( 1000 ).DesiredMouth );
	}

	private static void StrictLipDeadline()
	{
		var driver = Begin( new uint[] { 1000000, 2000000, 3000000, uint.MaxValue } );
		Equal( false, driver.Update( 1800 ).LipMarkConsumed );
		Equal( true, driver.Talking );
		Equal( true, driver.Update( 1801 ).LipMarkConsumed );
		Equal( false, driver.Talking );
		Equal( 2800, driver.NextLipClock );
	}

	private static void MissedFrameConsumesOneMark()
	{
		var driver = Begin( new uint[] { 1000000, 2000000, 3000000, uint.MaxValue } );
		driver.Update( 10000 );
		Equal( 2, driver.NextWordIndex );
		Equal( false, driver.Talking );
		driver.Update( 10000 );
		Equal( 3, driver.NextWordIndex );
		Equal( true, driver.Talking );
		driver.Update( 10000 );
		Equal( 4, driver.NextWordIndex );
		Equal( false, driver.LipActive );
		Equal( false, driver.Update( 20000 ).LipMarkConsumed );
	}

	private static void PauseAndResumeUseSuppliedClock()
	{
		var random = new OriginalRandom( 1, 2 );
		var driver = new OriginalAdvisorLipDriver( random.Next );
		driver.BeginResponse( new uint[] { 1000000, 2000000, 3000000, uint.MaxValue }, 1000, false );
		driver.Update( 1700 );
		for ( var pausedRenderFrame = 0; pausedRenderFrame < 30; pausedRenderFrame++ )
			Equal( false, driver.Update( 1700 ).LipMarkConsumed );
		Equal( 1, random.Calls );
		Equal( 1, driver.NextWordIndex );
		Equal( false, driver.Update( 1800 ).LipMarkConsumed );
		Equal( true, driver.Update( 1801 ).LipMarkConsumed );
		Equal( OriginalAdvisorMouth.Normal, driver.DesiredMouth );
		// Device/real elapsed time is deliberately absent: the caller supplied frozen time.
	}

	private static void PauseWithExpiredBacklogStillConsumesOneMark()
	{
		var driver = Begin( new uint[] { 1000000, 2000000, 3000000, uint.MaxValue } );
		driver.Update( 10000 );
		Equal( 2, driver.NextWordIndex );
		// A frozen clock does not suppress processing an already-expired backlog.
		Equal( true, driver.Update( 10000 ).LipMarkConsumed );
		Equal( 3, driver.NextWordIndex );
		Equal( true, driver.Talking );
		Equal( true, driver.Update( 10000 ).LipMarkConsumed );
		Equal( false, driver.LipActive );
	}

	private static void ImmediateZeroMarkUsesRecoveredLead()
	{
		var driver = Begin( new uint[] { 0, 1000000, uint.MaxValue } );
		Equal( 800, driver.NextLipClock );
		Equal( true, driver.Update( 1000 ).LipMarkConsumed );
		Equal( false, driver.Talking );
		Equal( 1800, driver.NextLipClock );
	}

	private static void RawNegativeSentinelEndsLip()
	{
		var driver = Begin( new uint[] { 1000000, uint.MaxValue } );
		driver.Update( 1801 );
		Equal( false, driver.Talking );
		Equal( false, driver.LipActive );
		Equal( -1, driver.NextLipClock );
	}

	private static void ConvertedNegativeOneEndsLip()
	{
		var driver = Begin( new uint[] { 1000000, unchecked((uint)-1000), uint.MaxValue } );
		driver.Update( 1801 );
		Equal( false, driver.LipActive );
		Equal( false, driver.Talking );
		Equal( 2, driver.NextWordIndex );
	}

	private static void InitialConvertedNegativeOneIsScheduled()
	{
		var driver = Begin( new uint[] { unchecked((uint)-1000), 1000000, uint.MaxValue } );
		Equal( 799, driver.NextLipClock );
		Equal( true, driver.LipActive );
		Equal( true, driver.Talking );
		Equal( true, driver.Update( 1000 ).LipMarkConsumed );
		Equal( true, driver.LipActive );
		Equal( 1800, driver.NextLipClock );
	}

	private static void EarlySentinelStopsWithinBoundedInput()
	{
		var driver = Begin( new uint[] { 1000000, uint.MaxValue, 2000000, uint.MaxValue } );
		driver.Update( 1801 );
		driver.Update( 30000 );
		Equal( 2, driver.NextWordIndex );
		Equal( false, driver.LipActive );
	}

	private static void AllFiveMouthStatesIncludeNormal()
	{
		var random = new OriginalRandom( 0, 1, 2, 3, 4 );
		var driver = new OriginalAdvisorLipDriver( random.Next );
		driver.BeginResponse( new uint[] { 100000000, uint.MaxValue }, 1000, false );
		for ( var index = 0; index < 5; index++ )
			Equal( (OriginalAdvisorMouth)(index + 1), driver.Update( 1000 + 101 * index ).DesiredMouth );
		Equal( 5, random.Calls );
		Equal( true, driver.Talking );
	}

	private static void StrictMouthDeadlineAndOneRandomDraw()
	{
		var random = new OriginalRandom( 6, int.MaxValue );
		var driver = new OriginalAdvisorLipDriver( random.Next );
		driver.BeginResponse( new uint[] { 100000000, uint.MaxValue }, 1000, false );
		Equal( OriginalAdvisorMouth.Aah, driver.Update( 1000 ).DesiredMouth );
		Equal( false, driver.Update( 1100 ).MouthSelectorChanged );
		Equal( 1, random.Calls );
		driver.Update( 50000 );
		Equal( 2, random.Calls );
		Equal( 50100, driver.NextMouthClock );
		Equal( (OriginalAdvisorMouth)(int.MaxValue % 5 + 1), driver.DesiredMouth );
	}

	private static void SilentAndTerminalMouthAreNormal()
	{
		var random = new OriginalRandom( 4, 2 );
		var driver = new OriginalAdvisorLipDriver( random.Next );
		driver.BeginResponse( new uint[] { 1000000, 2000000, 3000000, uint.MaxValue }, 1000, false );
		Equal( OriginalAdvisorMouth.Sss, driver.Update( 1000 ).DesiredMouth );
		Equal( OriginalAdvisorMouth.Normal, driver.Update( 1801 ).DesiredMouth );
		Equal( OriginalAdvisorMouth.Eee, driver.Update( 2801 ).DesiredMouth );
		Equal( OriginalAdvisorMouth.Normal, driver.Update( 3801 ).DesiredMouth );
		Equal( 2, random.Calls );
	}

	private static void DeferredSpeechAcceptsEquality()
	{
		var driver = Begin( new uint[] { 1000000, uint.MaxValue }, deferred: true );
		Equal( false, driver.Talking );
		Equal( 1800, driver.LipStartClock );
		Equal( false, driver.Update( 1799 ).SpeechStartDue );
		Equal( true, driver.Update( 1800 ).SpeechStartDue );
		Equal( true, driver.Talking );
		Equal( false, driver.SpeechPending );
		Equal( false, driver.Update( 1800 ).SpeechStartDue );
	}

	private static void DeferredAndLipBoundariesDiffer()
	{
		var driver = Begin( new uint[] { 0, 1000000, uint.MaxValue }, deferred: true );
		var equal = driver.Update( 1800 );
		Equal( true, equal.SpeechStartDue );
		Equal( false, equal.LipMarkConsumed );
		Equal( true, driver.Talking );
		Equal( true, driver.Update( 1801 ).LipMarkConsumed );
		Equal( false, driver.Talking );
	}

	private static void DeferredMissingLipRequestsSpeechButNormalMouth()
	{
		var driver = Begin( Array.Empty<uint>(), deferred: true );
		Equal( true, driver.Update( 1800 ).SpeechStartDue );
		Equal( true, driver.Talking );
		Equal( false, driver.LipActive );
		Equal( OriginalAdvisorMouth.Normal, driver.DesiredMouth );
	}

	private static void PendingZeroDisablesDeferredBranch()
	{
		var driver = Begin( new uint[] { 1000000, uint.MaxValue }, clock: -800, deferred: true );
		Equal( false, driver.SpeechPending );
		Equal( true, driver.Talking );
		Equal( -1000, driver.LipStartClock );
	}

	private static void InputSnapshotAndInvalidInputPreserveState()
	{
		var words = new uint[] { 1000000, uint.MaxValue };
		var driver = Begin( words );
		words[1] = 2000000;
		Throws<ArgumentException>( () => driver.BeginResponse( new uint[] { 1000000 }, 2000, false ) );
		Equal( 1800, driver.NextLipClock );
		Throws<ArgumentException>( () => driver.BeginResponse( new uint[] { uint.MaxValue, uint.MaxValue }, 2000, false ) );
		Throws<ArgumentException>( () => driver.BeginResponse( new uint[OriginalAdvisorLipDriver.MaximumLipWords + 1], 2000, false ) );
		driver.Update( 1801 );
		Equal( false, driver.LipActive );
	}

	private static void CallerRandomContractIsExplicit()
	{
		Throws<ArgumentNullException>( () => new OriginalAdvisorLipDriver( null! ) );
		var driver = new OriginalAdvisorLipDriver( () => -1 );
		driver.BeginResponse( new uint[] { 100000000, uint.MaxValue }, 1000, false );
		Throws<InvalidOperationException>( () => driver.Update( 1000 ) );
	}

	private static void MouthCadencePersistsAcrossResponseRequests()
	{
		var random = new OriginalRandom( 4, 2 );
		var driver = new OriginalAdvisorLipDriver( random.Next );
		driver.BeginResponse( new uint[] { 100000000, uint.MaxValue }, 1000, false );
		driver.Update( 1000 );
		driver.BeginResponse( new uint[] { 100000000, uint.MaxValue }, 1050, false );
		Equal( OriginalAdvisorMouth.Sss, driver.Update( 1100 ).DesiredMouth );
		Equal( 1, random.Calls );
		Equal( OriginalAdvisorMouth.Eee, driver.Update( 1101 ).DesiredMouth );
	}
}
