using OpenTPW.Evidence.Clock;
using System.Text.Json;

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
	( "application flag8 permits world work", () => CheckGate( true, false ) ),
	( "gameplay exclusion advances phase but not scripts", () => CheckGate( false, true ) ),
	( "application flag8 overrides gameplay exclusion", () => CheckGate( true, true ) ),
	( "mode zero uses the direct world tick branch", () =>
	{
		var result = SchedulerRules.Advance( new( 0, 0, 0, 57 ), Input( 2000, 0 ) );
		Equal( 65, result.ScriptManagerPasses ); Equal( 3, result.WorldTicks );
		Equal( 5, result.DroppedWorldPhases );
		Equal( 60u, result.State.WorldTickCounter );
	} ),
	( "decoded BO BI gate truth table agrees with scheduler", () =>
	{
		using var metadata = ReadNativeBranches();
		foreach ( var application in new[] { false, true } )
			foreach ( var gameplay in new[] { false, true } )
			{
				var nativeAllowed = NativeWorldWorkAllowed( metadata.RootElement, application, gameplay );
				var result = SchedulerRules.Advance( default, new( 2000, application, gameplay, 2 ) );
				Equal( nativeAllowed ? 65 : 0, result.ScriptManagerPasses );
			}
	} ),
	( "decoded mode branch destinations agree with tick policy", () =>
	{
		using var metadata = ReadNativeBranches();
		foreach ( var mode in new[] { 0, 1, 2 } )
		{
			Equal( mode == 1 ? "wrapper" : "direct", NativeModeRoute( metadata.RootElement, mode ) );
			Equal( 3, SchedulerRules.Advance( default, Input( 2000, mode ) ).WorldTicks );
		}
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
		var excluded = SchedulerRules.Advance( new( 0, 0, 7, 0 ), new( 31, false, true, 2 ) );
		var resumed = SchedulerRules.Advance( excluded.State, Input( 32 ) );
		Equal( 2u, resumed.State.SubstepPhase ); Equal( 8u, resumed.State.ScriptPassCounter );
		True( SchedulerRules.IsOrdinaryScriptEligible( 0, resumed.State.ScriptPassCounter ) );
		True( !SchedulerRules.IsOrdinaryScriptEligible( 2, resumed.State.ScriptPassCounter ) );
	} ),
	( "signed clock crossing is explicitly unsupported", () =>
	{
		CheckBoundary( new( 0x7fffffff, 1, 2, 3 ), 0x80000000, UnsupportedBoundary.SignedTimestampBoundary );
	} ),
	( "saved shared epochs align independently without zero origins", () =>
	{
		var saved = new SavedClockEpochs( 114374804, 114876286 );
		var bases = new ClockBaseWords( 1000, 2000 );
		var offsets = SharedClockEpochs.Align( saved, bases );
		Equal( new ClockEpochOffsets( 114373804, 114874286 ), offsets );
		Equal( new SharedClockWords( 114374804, 114876286 ), SharedClockEpochs.Read( offsets, bases ) );
	} ),
	( "restored deadline remains sixty three milliseconds ahead", () =>
	{
		var offsets = SharedClockEpochs.Align( new( 114374804, 114876286 ), new( 1000, 2000 ) );
		var now = SharedClockEpochs.Read( offsets, new( 1005, 2005 ) );
		Equal( 58u, unchecked(114374867u - now.ScaledMilliseconds) );
		Equal( 114876291u, now.UnscaledMilliseconds );
	} ),
	( "epoch alignment and reads wrap word arithmetic", () =>
	{
		var saved = new SavedClockEpochs( 10, 20 );
		var bases = new ClockBaseWords( 0xfffffff0, uint.MaxValue );
		var offsets = SharedClockEpochs.Align( saved, bases );
		Equal( new ClockEpochOffsets( 26, 21 ), offsets );
		Equal( new SharedClockWords( 10, 20 ), SharedClockEpochs.Read( offsets, bases ) );
		Equal( new SharedClockWords( 31, 21 ), SharedClockEpochs.Read( offsets, new( 5, 0 ) ) );
	} ),
	( "outer epoch does not saturate with its underlying source", () =>
	{
		var saturated = OriginalElapsedClock.SaturatingWord( 4294967296d );
		Equal( new SharedClockWords( 1, 4 ), SharedClockEpochs.Read( new( 2, 5 ), new( saturated, saturated ) ) );
	} ),
	( "numeric load hook matches decoded native branch truth table", () =>
	{
		using var stream = typeof( SharedClockEpochs ).Assembly.GetManifestResourceStream( "NativeEpochRules" )
			?? throw new InvalidOperationException( "missing native epoch fixture" );
		using var metadata = JsonDocument.Parse( stream );
		var root = metadata.RootElement;
		Equal( "04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5", root.GetProperty( "identity_sha256" ).GetString() );
		var branch = root.GetProperty( "post_load_skip_branch" );
		Equal( root.GetProperty( "skip_target" ).GetUInt32(), branch.GetProperty( "target" ).GetUInt32() );
		foreach ( var selector in new[] { -1, 0, 1, 2, int.MaxValue } )
		{
			var skips = NativeBranchTaken( branch, selector == root.GetProperty( "post_load_selector_comparison" ).GetInt32() );
			Equal( !skips, SharedClockEpochs.AppliesPostLoadAlignment( selector ) );
		}
	} ),
	( "selector one preserves previous offsets at this hook", () =>
	{
		var prior = new ClockEpochOffsets( 19, 23 );
		Equal( prior, SharedClockEpochs.ApplyPostLoadHook( prior, new( 100, 200 ), new( 10, 20 ), 1 ) );
		Equal( new ClockEpochOffsets( 90, 180 ), SharedClockEpochs.ApplyPostLoadHook( prior, new( 100, 200 ), new( 10, 20 ), 2 ) );
	} ),
	( "saved clock pair requires exact width and preserves unsigned words", () =>
	{
		Span<byte> words = stackalloc byte[8];
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( words, 0x80000001 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( words[4..], uint.MaxValue );
		Equal( new SavedClockEpochs( 0x80000001, uint.MaxValue ), SavedClockEpochs.ReadLittleEndian( words ) );
		foreach ( var size in new[] { 0, 4, 7, 9 } )
			Throws<ArgumentException>( () => SavedClockEpochs.ReadLittleEndian( new byte[size] ) );
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
	using var metadata = ReadNativeBranches();
	var allowed = NativeWorldWorkAllowed( metadata.RootElement, applicationFlag, gameplayFlag );
	Equal( allowed ? 82u : 17u, result.State.ScriptPassCounter );
	Equal( allowed ? 60u : 57u, result.State.WorldTickCounter );
	Equal( allowed ? 65 : 0, result.ScriptManagerPasses );
	Equal( allowed ? 3 : 0, result.WorldTicks );
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

static JsonDocument ReadNativeBranches()
{
	using var stream = typeof( SchedulerRules ).Assembly.GetManifestResourceStream( "NativeBranchRules" )
		?? throw new InvalidOperationException( "missing decoded native branch fixture" );
	var metadata = JsonDocument.Parse( stream );
	Equal( "04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5",
		metadata.RootElement.GetProperty( "identity_sha256" ).GetString() );
	return metadata;
}

static bool NativeBranchTaken( JsonElement branch, bool conditionEqual )
{
	Equal( 2, branch.GetProperty( "bi" ).GetInt32() );
	return branch.GetProperty( "bo" ).GetInt32() switch
	{
		4 => !conditionEqual,
		12 => conditionEqual,
		_ => throw new InvalidOperationException( "unqualified native branch option" )
	};
}

static bool NativeWorldWorkAllowed( JsonElement metadata, bool application, bool gameplay )
{
	var branches = metadata.GetProperty( "branches" );
	var appBranch = branches.GetProperty( "application_flag8" );
	if ( NativeBranchTaken( appBranch, !application ) )
		return appBranch.GetProperty( "target" ).GetUInt32() == metadata.GetProperty( "work_target" ).GetUInt32();
	var gameBranch = branches.GetProperty( "gameplay_flag1" );
	if ( NativeBranchTaken( gameBranch, !gameplay ) )
		return gameBranch.GetProperty( "target" ).GetUInt32() != metadata.GetProperty( "skip_target" ).GetUInt32();
	return true;
}

static string NativeModeRoute( JsonElement metadata, int mode )
{
	var branches = metadata.GetProperty( "branches" );
	var comparisons = metadata.GetProperty( "mode_comparisons" );
	var mode0 = branches.GetProperty( "mode0" );
	if ( NativeBranchTaken( mode0, mode == comparisons.GetProperty( "mode0" ).GetInt32() ) )
		return mode0.GetProperty( "target" ).GetUInt32() == metadata.GetProperty( "direct_target" ).GetUInt32() ? "direct" : "unknown";
	var mode2 = branches.GetProperty( "mode2" );
	if ( !NativeBranchTaken( mode2, mode == comparisons.GetProperty( "mode2" ).GetInt32() ) )
		return "direct";
	Equal( metadata.GetProperty( "mode1_check_target" ).GetUInt32(), mode2.GetProperty( "target" ).GetUInt32() );
	var mode1 = branches.GetProperty( "mode1" );
	if ( NativeBranchTaken( mode1, mode == comparisons.GetProperty( "mode1" ).GetInt32() ) )
		return mode1.GetProperty( "target" ).GetUInt32() == metadata.GetProperty( "after_world_target" ).GetUInt32() ? "none" : "unknown";
	return "wrapper";
}
