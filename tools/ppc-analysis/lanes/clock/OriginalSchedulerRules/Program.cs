using OpenTPW.Evidence.Clock;

var checks = new (string Name, Action Run)[]
{
	( "zero delay preserves state", () =>
	{
		var state = new SchedulerState( 100, 17, 42, 123 );
		var result = SchedulerRules.Advance( state, Input( 100 ) );
		Equal( state, result.State ); Equal( 0, result.Substeps ); True( result.IsDefined );
	} ),
	( "one millisecond ceiling overshoots", () =>
	{
		var result = SchedulerRules.Advance( new( 100, 7, 0, 0 ), Input( 101 ) );
		Equal( 1, result.Substeps ); Equal( 131u, result.State.PreviousScheduledMilliseconds );
		Equal( 8u, result.State.SubstepPhase ); Equal( 1, result.WorldTicks );
	} ),
	( "overshoot produces no premature next step", () =>
	{
		var first = SchedulerRules.Advance( new( 100, 0, 0, 0 ), Input( 101 ) );
		var second = SchedulerRules.Advance( first.State, Input( 102 ) );
		Equal( 0, second.Substeps ); Equal( first.State, second.State ); True( second.IsDefined );
	} ),
	( "exact and just-over step boundaries", () =>
	{
		Equal( 1, SchedulerRules.Advance( default, Input( 31 ) ).Substeps );
		Equal( 2, SchedulerRules.Advance( default, Input( 32 ) ).Substeps );
	} ),
	( "2000 millisecond backlog caps world phases only", () =>
	{
		var result = SchedulerRules.Advance( default, Input( 2000 ) );
		Equal( 65, result.Substeps ); Equal( 65, result.ScriptManagerPasses );
		Equal( 8, result.EligibleWorldPhases ); Equal( 3, result.WorldTicks );
		Equal( 5, result.DroppedWorldPhases ); Equal( 0u, result.DroppedClockMilliseconds );
		Equal( new SchedulerState( 2015, 65, 65, 3 ), result.State );
	} ),
	( "large gap drops excess before stepping", () =>
	{
		var result = SchedulerRules.Advance( default, Input( 5000 ) );
		Equal( 3000u, result.DroppedClockMilliseconds ); Equal( 65, result.Substeps );
		Equal( 5015u, result.State.PreviousScheduledMilliseconds ); Equal( 3, result.WorldTicks );
	} ),
	( "application exclusion advances phase but not scripts", () => CheckGate( true, false ) ),
	( "gameplay exclusion advances phase but not scripts", () => CheckGate( false, true ) ),
	( "both exclusions retain independent script counter", () => CheckGate( true, true ) ),
	( "mode zero consumes eligible phases without world ticks", () =>
	{
		var result = SchedulerRules.Advance( new( 0, 0, 0, 57 ), Input( 2000, 0 ) );
		Equal( 65, result.ScriptManagerPasses ); Equal( 0, result.WorldTicks );
		Equal( 3, result.ModeSuppressedWorldPhases ); Equal( 5, result.DroppedWorldPhases );
		Equal( 57u, result.State.WorldTickCounter );
	} ),
	( "mode one literal wrapper branch admits ticks", () =>
	{
		Equal( 3, SchedulerRules.Advance( default, Input( 2000, 1 ) ).WorldTicks );
	} ),
	( "unknown numeric modes are flagged", () =>
	{
		var state = new SchedulerState( 12, 34, 56, 78 );
		var result = SchedulerRules.Advance( state, Input( 100, 3 ) );
		Equal( UnsupportedBoundary.NumericMode, result.UndefinedBoundary );
		Equal( state, result.State ); Equal( 0, result.Substeps );
	} ),
	( "normal callback resets world cap", () =>
	{
		var first = SchedulerRules.Advance( default, Input( 2000 ) );
		var second = SchedulerRules.Advance( first.State, Input( 2250 ) );
		Equal( 1, second.WorldTicks ); Equal( 4u, second.State.WorldTickCounter );
	} ),
	( "source subtraction wraps unsigned", () =>
	{
		var clock = new OriginalElapsedClock( 0xfffffffe );
		Equal( 3u, clock.Sample( 1 ) ); Equal( 3d, clock.AccumulatedMilliseconds );
	} ),
	( "source output saturates while double accumulator advances", () =>
	{
		var clock = new OriginalElapsedClock( 0, uint.MaxValue );
		Equal( uint.MaxValue, clock.Sample( 1 ) ); Equal( 4294967296d, clock.AccumulatedMilliseconds );
		Equal( uint.MaxValue, clock.Sample( 2 ) ); Equal( 4294967297d, clock.AccumulatedMilliseconds );
	} ),
	( "scaled source preserves fused multiply-add precision", () =>
	{
		var clock = new OriginalElapsedClock( 0, -3435973836d );
		clock.ApplyControl( new( ClockControl.Slower, 0, true, true ) );
		clock.Sample( uint.MaxValue );
		Equal( 858993459d / 4503599627370496d, clock.AccumulatedMilliseconds );
	} ),
	( "finite conversion preserves unsigned upper half", () =>
	{
		Equal( 0x80000001u, OriginalElapsedClock.SaturatingWord( 2147483649.9 ) );
		Equal( uint.MaxValue, OriginalElapsedClock.SaturatingWord( 4294967295.9 ) );
	} ),
	( "finite conversion clamps and truncates", () =>
	{
		Equal( 0u, OriginalElapsedClock.SaturatingWord( -1 ) );
		Equal( 1u, OriginalElapsedClock.SaturatingWord( 1.9 ) );
		Equal( uint.MaxValue, OriginalElapsedClock.SaturatingWord( 4294967296d ) );
	} ),
	( "nonfinite arithmetic stays outside contract", () =>
	{
		foreach ( var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity } )
			Throws<ArgumentOutOfRangeException>( () => OriginalElapsedClock.SaturatingWord( value ) );
	} ),
	( "press does not invoke release speed action", () =>
	{
		var clock = new OriginalElapsedClock( 0 );
		True( !clock.ApplyControl( new( ClockControl.Faster, 10, false, true ) ) );
		Equal( 1d, clock.Scale );
	} ),
	( "release scales next entire unsampled delta", () =>
	{
		var clock = new OriginalElapsedClock( 0 );
		True( clock.ApplyControl( new( ClockControl.Faster, 10, true, true ) ) );
		Equal( 1.25d, clock.Scale ); Equal( 25u, clock.Sample( 20 ) );
	} ),
	( "speed actions clamp to observed endpoints", () =>
	{
		var clock = new OriginalElapsedClock( 0 );
		for ( var index = 0; index < 20; ++index )
			clock.ApplyControl( new( ClockControl.Faster, 0, true, true ) );
		Equal( 2d, clock.Scale );
		for ( var index = 0; index < 40; ++index )
			clock.ApplyControl( new( ClockControl.Slower, 0, true, true ) );
		Equal( 0.25d, clock.Scale );
		clock.ApplyControl( new( ClockControl.Faster, 0, true, true ) );
		Equal( 0.3125d, clock.Scale );
	} ),
	( "pause freezes elapsed reads and excludes paused duration", () =>
	{
		var clock = new OriginalElapsedClock( 0 );
		Equal( 100u, clock.Sample( 100 ) ); clock.Pause( 100 );
		Equal( 100u, clock.Sample( 200 ) ); Equal( 100d, clock.AccumulatedMilliseconds );
		clock.Resume( 300 ); Equal( 100u, clock.Sample( 300 ) );
		Equal( 200u, clock.Sample( 400 ) );
	} ),
	( "pause and resume methods are idempotent", () =>
	{
		var clock = new OriginalElapsedClock( 0 );
		clock.Pause( 100 ); clock.Pause( 200 ); Equal( 100u, clock.Sample( 200 ) );
		clock.Resume( 300 ); clock.Resume( 400 ); Equal( 300d, clock.AccumulatedMilliseconds );
		Equal( 200u, clock.Sample( 400 ) );
	} ),
	( "pause release requires identified host state", () =>
	{
		var clock = new OriginalElapsedClock( 0 );
		True( !clock.ApplyControl( new( ClockControl.TogglePause, 100, true, false ) ) );
		True( !clock.IsPaused ); Equal( 150u, clock.Sample( 150 ) );
	} ),
	( "pause toggles on release only", () =>
	{
		var clock = new OriginalElapsedClock( 0 );
		clock.ApplyControl( new( ClockControl.TogglePause, 100, true, true ) ); True( clock.IsPaused );
		clock.ApplyControl( new( ClockControl.TogglePause, 200, false, true ) ); True( clock.IsPaused );
		clock.ApplyControl( new( ClockControl.TogglePause, 300, true, true ) ); True( !clock.IsPaused );
		Equal( 200u, clock.Sample( 400 ) );
	} ),
	( "fractional source survives integer pause bookkeeping", () =>
	{
		var clock = new OriginalElapsedClock( 0 );
		clock.ApplyControl( new( ClockControl.Faster, 0, true, true ) );
		clock.Pause( 1 ); clock.Resume( 2 );
		Equal( 1u, clock.Sample( 2 ) ); Equal( 4u, clock.Sample( 4 ) );
		Equal( 5d, clock.AccumulatedMilliseconds );
	} ),
	( "frozen selected clock stops subsequent scheduler work", () =>
	{
		var clock = new OriginalElapsedClock( 0 ); clock.Pause( 1000 );
		var state = new SchedulerState( 1000, 0, 0, 0 );
		Equal( 0, SchedulerRules.Advance( state, Input( clock.Sample( 5000 ) ) ).Substeps );
		clock.Resume( 6000 ); Equal( 0, SchedulerRules.Advance( state, Input( clock.Sample( 6000 ) ) ).Substeps );
		Equal( 1, SchedulerRules.Advance( state, Input( clock.Sample( 6001 ) ) ).Substeps );
	} ),
	( "phase and world counters wrap independently", () =>
	{
		var result = SchedulerRules.Advance( new( 0, uint.MaxValue, uint.MaxValue, uint.MaxValue ), Input( 1 ) );
		Equal( 0u, result.State.SubstepPhase ); Equal( 0u, result.State.ScriptPassCounter );
		Equal( 0u, result.State.WorldTickCounter ); Equal( 1, result.WorldTicks );
	} ),
	( "manager phase is independent of excluded scheduler phase", () =>
	{
		var excluded = SchedulerRules.Advance( new( 0, 0, 7, 0 ), new( 31, true, false, 2 ) );
		var resumed = SchedulerRules.Advance( excluded.State, Input( 32 ) );
		Equal( 2u, resumed.State.SubstepPhase ); Equal( 8u, resumed.State.ScriptPassCounter );
		True( SchedulerRules.IsOrdinaryScriptEligible( 0, resumed.State.ScriptPassCounter ) );
		True( !SchedulerRules.IsOrdinaryScriptEligible( 2, resumed.State.ScriptPassCounter ) );
	} ),
	( "signed clock crossing is explicitly unsupported", () =>
	{
		CheckBoundary( new( 0x7fffffff, 1, 2, 3 ), 0x80000000, UnsupportedBoundary.SignedTimestampBoundary );
	} ),
	( "signed overshoot crossing is explicitly unsupported", () =>
	{
		CheckBoundary( new( 0x7ffffffe, 1, 2, 3 ), 0x7fffffff, UnsupportedBoundary.SignedTimestampBoundary );
	} ),
	( "scheduled word wrap is explicitly unsupported", () =>
	{
		CheckBoundary( new( uint.MaxValue - 1, 1, 2, 3 ), uint.MaxValue, UnsupportedBoundary.ScheduledTimestampWrap );
	} )
};

foreach ( var check in checks )
{
	try { check.Run(); }
	catch ( Exception error )
	{
		Console.Error.WriteLine( $"FAIL: {check.Name}: {error.Message}" );
		return 1;
	}
}
Console.WriteLine( $"PASS: {checks.Length} standalone OriginalSchedulerRules checks" );
return 0;

static SchedulerInput Input( uint clock, int mode = 2 ) => new( clock, false, false, mode );

static void CheckGate( bool applicationFlag, bool gameplayFlag )
{
	var result = SchedulerRules.Advance( new( 0, 0, 17, 57 ), new( 2000, applicationFlag, gameplayFlag, 2 ) );
	Equal( 65, result.Substeps ); Equal( 65u, result.State.SubstepPhase );
	Equal( 17u, result.State.ScriptPassCounter ); Equal( 57u, result.State.WorldTickCounter );
	Equal( 0, result.ScriptManagerPasses ); Equal( 0, result.WorldTicks );
}

static void CheckBoundary( SchedulerState state, uint now, UnsupportedBoundary expected )
{
	var result = SchedulerRules.Advance( state, Input( now ) );
	Equal( expected, result.UndefinedBoundary ); Equal( state, result.State );
	Equal( 0, result.Substeps ); True( !result.IsDefined );
}

static void Equal<T>( T expected, T actual )
{
	if ( !EqualityComparer<T>.Default.Equals( expected, actual ) )
		throw new InvalidOperationException( $"expected {expected}, received {actual}" );
}

static void True( bool actual ) => Equal( true, actual );

static void Throws<T>( Action action ) where T : Exception
{
	try { action(); }
	catch ( T ) { return; }
	throw new InvalidOperationException( $"expected {typeof( T ).Name}" );
}
