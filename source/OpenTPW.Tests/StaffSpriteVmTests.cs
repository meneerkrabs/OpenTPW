using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic state/control tests only: no assets, original execution, renderer, save loader or graphics device.</summary>
[TestClass]
public class StaffSpriteVmTests
{
	private const StaffSpriteProgram Synthetic = (StaffSpriteProgram)60000;
	private static StaffSpritePc Pc( StaffSpriteProgram program, int index = 0 ) => new( program, index );
	private static StaffSpriteTickResult Step( StaffSpriteVmState state ) => StaffSpriteVm.Tick( state, unchecked( state.Due + 1 ) );
	private static StaffSpriteProgramSet Code( params StaffSpriteInstruction[] instructions ) => new( new Dictionary<StaffSpriteProgram, StaffSpriteInstruction[]> { [Synthetic] = instructions } );
	private static StaffSpriteVmState SyntheticState()
	{
		var state = StaffSpriteVm.Create( StaffSpriteProgram.Stand, 0 );
		state.CurrentPc = state.RestartPc = Pc( Synthetic );
		return state;
	}
	private static StaffSpriteTickResult Step( StaffSpriteVmState state, StaffSpriteProgramSet code ) => StaffSpriteVm.Tick( state, unchecked( state.Due + 1 ), code );

	// The 70-word manifest covers every field of the native record, using semantic tokens in place of native PCs/table pointers.
	private static uint[] Words( StaffSpriteVmState state ) => new[]
	{
		state.OpaqueWord, state.Handle, state.CurrentPc.Word, state.RestartPc.Word, state.LastPc.Word, state.ProgramTable,
		(uint)state.RunState, unchecked( (uint)state.StackPointer )
	}.Concat( state.Stack ).Concat( new[]
	{
		unchecked( (uint)state.IfDepth ), unchecked( (uint)state.CallDepth ), unchecked( (uint)state.LoopDepth ),
		unchecked( (uint)state.Due ), unchecked( (uint)state.Interval )
	} ).Concat( state.Registers ).Concat( state.DrawCache ).Append( state.OutputValid ).ToArray();

	private static StaffSpriteVmState Sentinel( uint seed )
	{
		var state = new StaffSpriteVmState
		{
			OpaqueWord = seed, Handle = seed + 1, CurrentPc = new( seed + 2 ), RestartPc = new( seed + 3 ),
			LastPc = new( seed + 4 ), ProgramTable = seed + 5, RunState = (StaffSpriteRunState)(seed + 6), StackPointer = (int)seed + 7,
			IfDepth = (int)seed + 28, CallDepth = (int)seed + 29, LoopDepth = (int)seed + 30, Due = (int)seed + 31,
			Interval = (int)seed + 32, OutputValid = seed + 69
		};
		for ( var i = 0; i < state.Stack.Length; i++ ) state.Stack[i] = seed + 8 + (uint)i;
		for ( var i = 0; i < state.Registers.Length; i++ ) state.Registers[i] = seed + 33 + (uint)i;
		for ( var i = 0; i < state.DrawCache.Length; i++ ) state.DrawCache[i] = seed + 52 + (uint)i;
		CollectionAssert.AreEqual( Enumerable.Range( 0, 70 ).Select( i => seed + (uint)i ).ToArray(), Words( state ) );
		return state;
	}

	[TestMethod]
	public void FullCopyPreservesEveryWordExceptTheSixProvenOverridesAndOwnsEveryArray()
	{
		var source = Sentinel( 0x80001000 );
		var before = Words( source );
		var clone = StaffSpriteCloneCopy.Create( source, 17, 101, 102, 103, 104 );
		var expected = (uint[])before.Clone();
		expected[1] = 17;
		expected[34] = 101; expected[35] = 102; expected[36] = 103; expected[41] = 104;
		expected[49] = (before[49] & ~0x200u) | 0x50000u;
		CollectionAssert.AreEqual( expected, Words( clone ) );
		CollectionAssert.AreEqual( before, Words( source ) );
		Assert.AreNotSame( source.Stack, clone.Stack );
		Assert.AreNotSame( source.Registers, clone.Registers );
		Assert.AreNotSame( source.DrawCache, clone.DrawCache );
		for ( var i = 0; i < clone.Stack.Length; i++ ) clone.Stack[i] ^= uint.MaxValue;
		for ( var i = 0; i < clone.Registers.Length; i++ ) clone.Registers[i] ^= uint.MaxValue;
		for ( var i = 0; i < clone.DrawCache.Length; i++ ) clone.DrawCache[i] ^= uint.MaxValue;
		clone.Due++;
		clone.CurrentPc = Pc( StaffSpriteProgram.Run );
		CollectionAssert.AreEqual( before, Words( source ), "Clone mutation must not change its source." );
	}

	[TestMethod]
	public void PartialRefreshChangesExactlyItsAllowlistAndMasksExistingCloneFlags()
	{
		var source = Sentinel( 0x1100 );
		var clone = Sentinel( 0x82002000 );
		source[StaffSpriteRegister.Flags] = 0x12345678;
		clone[StaffSpriteRegister.Flags] = 0xA0800201;
		var originalSource = Words( source );
		var expected = Words( clone );
		var oldFlags = expected[49];
		// Literal record-word indexes are an independent copy oracle, not the implementation's register list.
		foreach ( var index in new[] { 5, 2, 43, 44, 45, 50, 51, 40, 42, 48, 69, 46, 47 } ) expected[index] = originalSource[index];
		expected[49] = (oldFlags & ~0x200u) | 0x50000u;
		StaffSpriteCloneCopy.Refresh( clone, source );
		CollectionAssert.AreEqual( expected, Words( clone ) );
		CollectionAssert.AreEqual( originalSource, Words( source ) );
		Assert.AreNotEqual( (source[StaffSpriteRegister.Flags] & ~0x200u) | 0x50000u, clone[StaffSpriteRegister.Flags] );
		clone.Stack[0]++;
		clone.DrawCache[16]++;
		clone.Registers[18]++;
		CollectionAssert.AreEqual( originalSource, Words( source ) );
	}

	[TestMethod]
	public void DeepCopyCopiesAllSeventyWordsWithoutAnyPortraitOverrides()
	{
		var source = Sentinel( 1000 );
		var clone = source.DeepCopy();
		CollectionAssert.AreEqual( Words( source ), Words( clone ) );
		source.Stack[19] = 7; source.DrawCache[0] = 8; source.Registers[0] = 9;
		Assert.AreEqual( 1027u, clone.Stack[19] );
		Assert.AreEqual( 1052u, clone.DrawCache[0] );
		Assert.AreEqual( 1033u, clone.Registers[0] );
	}

	[TestMethod]
	public void AllCurrentHostTemplatesAgreeWithIndependentFlattenedOutputOracle()
	{
		foreach ( var id in new[] { 1, 2, 3, 9, 10, 17, 18, 19, 20, 21, 22, 23 } )
		{
			var state = StaffSpriteVm.Create( (StaffSpriteProgram)id, 0 );
			state[StaffSpriteRegister.FrameCount] = 123;
			Assert.IsFalse( state.HasOutput );
			for ( var step = 0; step < 64; step++ )
			{
				var result = Step( state );
				var expected = StaffScripts.At( id, step )!.Value;
				Assert.AreEqual( StaffSpriteTickStatus.Yielded, result.Status, $"template {id}, step {step}" );
				Assert.AreEqual( (uint)expected.Slot, state[StaffSpriteRegister.Slot] );
				Assert.AreEqual( (uint)expected.Frame, state[StaffSpriteRegister.Frame] );
				Assert.AreEqual( 123u, state[StaffSpriteRegister.FrameCount], "SLOT does not resolve frame metadata." );
				Assert.AreEqual( 0x1200u, state[StaffSpriteRegister.Flags] );
				Assert.AreEqual( 1u, state.OutputValid );
				Assert.AreEqual( Pc( (StaffSpriteProgram)id ), state.RestartPc );
				Assert.AreEqual( 20, state.StackPointer );
			}
		}
	}

	[TestMethod]
	public void CatchYieldsItsLastFrameBeforeJumpingToHoldsOwnProgramIdentity()
	{
		var state = StaffSpriteVm.Create( StaffSpriteProgram.Catch, 0 );
		for ( var i = 0; i < 8; i++ ) Step( state );
		Assert.AreEqual( Pc( StaffSpriteProgram.Catch, 10 ), state.CurrentPc );
		Assert.AreEqual( Pc( StaffSpriteProgram.Catch, 9 ), state.LastPc );
		Assert.AreEqual( 7u, state[StaffSpriteRegister.Frame] );
		Assert.AreEqual( 7u, state[StaffSpriteRegister.Slot] );
		Assert.AreEqual( 4, Step( state ).Instructions ); // JMP, SET, SLOT, FRAME
		Assert.AreEqual( Pc( StaffSpriteProgram.Hold, 3 ), state.CurrentPc );
		Assert.AreEqual( Pc( StaffSpriteProgram.Catch ), state.RestartPc );
		Assert.AreEqual( 0u, state[StaffSpriteRegister.Frame] );
		Assert.AreEqual( 8u, state[StaffSpriteRegister.Slot] );
	}

	[TestMethod]
	public void GuardReleaseTemplatePlaysSlotNineThenStandAndTemplate25PreservesAppearance()
	{
		var finite = StaffSpriteVm.Create( StaffSpriteProgram.Template24, 0 );
		for ( var i = 0; i < 8; i++ ) { Step( finite ); Assert.AreEqual( (uint)i, finite[StaffSpriteRegister.Frame] ); }
		Step( finite );
		Assert.AreEqual( 0u, finite[StaffSpriteRegister.Slot] );
		Assert.AreEqual( StaffSpriteProgram.Stand, finite.CurrentPc.Program );
		var noSetup = StaffSpriteVm.Create( StaffSpriteProgram.Template25, 0 );
		noSetup[StaffSpriteRegister.Flags] = 0x50001;
		noSetup[StaffSpriteRegister.Slot] = 99;
		for ( var i = 0; i < 8; i++ ) Step( noSetup );
		Assert.AreEqual( 0x50001u, noSetup[StaffSpriteRegister.Flags] );
		Assert.AreEqual( 99u, noSetup[StaffSpriteRegister.Slot] );
	}

	[TestMethod]
	public void PerformanceZeroMatchesIndependentIntroAndHoldFormulaAcrossSeveralCompleteCycles()
	{
		for ( var intro = 0; intro < 4; intro++ )
		for ( var hold = 0; hold < 4; hold++ )
		{
			var state = Performance( StaffSpriteProgram.Performance0, intro, hold, 5 );
			for ( var step = 0; step < 100; step++ )
			{
				Assert.AreEqual( StaffSpriteTickStatus.Yielded, Step( state ).Status );
				Assert.AreEqual( (uint)StaffScripts.PerformanceFrame( intro, hold, 5, step ), state[StaffSpriteRegister.Frame], $"intro={intro}, hold={hold}, step={step}" );
				Assert.AreEqual( 19, state.StackPointer );
				Assert.AreEqual( 1, state.LoopDepth );
				Assert.IsTrue( state.IfDepth is 0 or 1 );
				Assert.AreEqual( 7u, state[StaffSpriteRegister.Slot] );
			}
		}
	}

	private static StaffSpriteVmState Performance( StaffSpriteProgram program, int intro, int hold, int frames )
	{
		var state = StaffSpriteVm.Create( program, 0 );
		state[StaffSpriteRegister.Intro] = unchecked( (uint)intro );
		state[StaffSpriteRegister.Hold] = unchecked( (uint)hold );
		state[StaffSpriteRegister.FrameCount] = (uint)frames;
		state[StaffSpriteRegister.Slot] = 7;
		return state;
	}

	[TestMethod]
	public void PerformanceIntroAndHoldKeepTheActualPcScratchAndLoopReturnAddress()
	{
		var state = Performance( StaffSpriteProgram.Performance0, 1, 1, 3 );
		Step( state );
		Assert.AreEqual( Pc( StaffSpriteProgram.Performance0, 5 ), state.CurrentPc );
		Assert.AreEqual( Pc( StaffSpriteProgram.Performance0, 4 ).Word, state.Stack[19] );
		Assert.AreEqual( 1, state.IfDepth );
		Step( state ); // intro 1
		Step( state ); // main 1
		Assert.AreEqual( Pc( StaffSpriteProgram.Performance0Loop, 3 ), state.CurrentPc );
		Assert.AreEqual( Pc( StaffSpriteProgram.Performance0Loop, 2 ).Word, state.Stack[19] );
		Assert.AreEqual( 0, state.IfDepth );
		Step( state ); // main 2
		Step( state ); // first held 0
		Assert.AreEqual( Pc( StaffSpriteProgram.Performance0Loop, 9 ), state.CurrentPc );
		Assert.AreEqual( Pc( StaffSpriteProgram.Performance0Loop, 8 ).Word, state.Stack[19] );
		Assert.AreEqual( 0u, state[StaffSpriteRegister.Scratch] );
		Assert.AreEqual( 1, state.IfDepth );
		Step( state ); // second held 0
		Assert.AreEqual( 1u, state[StaffSpriteRegister.Scratch] );
		Step( state ); // main 1 again, scratch retains 2
		Assert.AreEqual( 2u, state[StaffSpriteRegister.Scratch] );
		Assert.AreEqual( 1u, state[StaffSpriteRegister.Frame] );
	}

	[TestMethod]
	public void OtherPerformanceGraphsHaveTheirOwnExactInclusiveAndConstantOutputRules()
	{
		var one = Performance( StaffSpriteProgram.Performance1, 99, 99, 3 );
		for ( var step = 0; step < 24; step++ )
		{
			Assert.AreEqual( StaffSpriteTickStatus.Yielded, Step( one ).Status );
			Assert.AreEqual( (uint)(step % 4), one[StaffSpriteRegister.Frame], "Program 1 includes FrameCount; never silently clamp to a plausible atlas frame." );
		}
		foreach ( var program in new[] { StaffSpriteProgram.Performance2, StaffSpriteProgram.Performance3 } )
		{
			var state = Performance( program, 88, 77, 5 );
			for ( var step = 0; step < 12; step++ )
			{
				Assert.AreEqual( StaffSpriteTickStatus.Yielded, Step( state ).Status );
				Assert.AreEqual( 2u, state[StaffSpriteRegister.Frame] );
				Assert.AreEqual( 0, state.LoopDepth );
				Assert.AreEqual( 20, state.StackPointer );
			}
		}
	}

	[TestMethod]
	public void TemplateReplacementRetainsEveryWordExceptTheSixControlFields()
	{
		var state = Sentinel( 1000 );
		var before = Words( state );
		var expected = (uint[])before.Clone();
		expected[2] = expected[3] = Pc( StaffSpriteProgram.Run ).Word;
		expected[7] = 20;
		expected[28] = expected[29] = expected[30] = 0;
		StaffSpriteVm.ApplyTemplate( state, StaffSpriteProgram.Run );
		CollectionAssert.AreEqual( expected, Words( state ) );
	}

	[TestMethod]
	public void PerformanceReplacementChangesOnlyResolvedSlotAndFrameCountBeyondTemplateControls()
	{
		var state = Performance( StaffSpriteProgram.Performance0, 3, 4, 7 );
		Step( state );
		var expected = Words( state );
		expected[2] = expected[3] = Pc( StaffSpriteProgram.Performance3 ).Word;
		expected[7] = 20; expected[28] = expected[29] = expected[30] = 0;
		expected[45] = 9; expected[47] = 12;
		StaffSpriteVm.ApplyPerformance( state, 3, 9, 12 );
		CollectionAssert.AreEqual( expected, Words( state ) );
	}

	[TestMethod]
	public void OutputSurvivesTemplateReplacementUntilANewFrameAndMinusOneClearsTheLatch()
	{
		var state = StaffSpriteVm.Create( StaffSpriteProgram.Walk, 0 );
		Assert.IsFalse( state.HasOutput );
		Step( state ); Step( state );
		var due = state.Due;
		StaffSpriteVm.ApplyTemplate( state, StaffSpriteProgram.Stand );
		Assert.IsTrue( state.HasOutput );
		Assert.AreEqual( 1u, state[StaffSpriteRegister.Frame] );
		Assert.AreEqual( StaffSpriteRunState.Yielded, state.RunState );
		Assert.AreEqual( StaffSpriteTickStatus.NotDue, StaffSpriteVm.Tick( state, due ).Status );
		Step( state );
		Assert.AreEqual( 0u, state[StaffSpriteRegister.Frame] );
		state.CurrentPc = Pc( Synthetic );
		var code = Code( new StaffSpriteInstruction( StaffSpriteOp.Frame, -1 ), new( StaffSpriteOp.FrameRegister, 0 ) );
		Step( state, code );
		Assert.AreEqual( uint.MaxValue, state[StaffSpriteRegister.Frame] );
		Assert.IsFalse( state.HasOutput );
		state[StaffSpriteRegister.Scratch] = uint.MaxValue;
		Step( state, code );
		Assert.IsFalse( state.HasOutput );
	}

	[TestMethod]
	public void PartialRefreshPreservesCrossProgramLoopReturnsInsteadOfRestartingOrReinterpretingThem()
	{
		var clone = Performance( StaffSpriteProgram.Performance0, 0, 0, 8 );
		Step( clone ); // retains Performance0Loop's frame instruction in its stack
		var source = Performance( StaffSpriteProgram.Performance1, 0, 0, 8 );
		Step( source );
		var oldReturn = clone.Stack[19];
		var restart = clone.RestartPc;
		StaffSpriteCloneCopy.Refresh( clone, source );
		Assert.AreEqual( Pc( StaffSpriteProgram.Performance1Loop, 3 ), clone.CurrentPc );
		Assert.AreEqual( oldReturn, clone.Stack[19] );
		Assert.AreEqual( restart, clone.RestartPc );
		Assert.AreEqual( StaffSpriteTickStatus.Yielded, Step( clone ).Status );
		Assert.AreEqual( Pc( StaffSpriteProgram.Performance0Loop, 3 ), clone.CurrentPc );
		Assert.AreEqual( 1u, clone[StaffSpriteRegister.Frame] );
		Assert.AreEqual( oldReturn, clone.Stack[19] );
		Assert.AreEqual( 1, clone.LoopDepth );
		Assert.AreEqual( Pc( StaffSpriteProgram.Performance1Loop, 3 ), source.CurrentPc );
	}

	[TestMethod]
	public void RefreshDoesNotCopySourceClockScratchControlDepthOrRunState()
	{
		var source = Performance( StaffSpriteProgram.Performance1, 0, 0, 8 );
		Step( source );
		var clone = Performance( StaffSpriteProgram.Performance0, 0, 0, 8 );
		clone.Due = 100; clone.Interval = 62; clone.IfDepth = -3; clone.LoopDepth = 0; clone.CallDepth = 4;
		clone[StaffSpriteRegister.Scratch] = 1234;
		clone.RunState = StaffSpriteRunState.Stopped;
		StaffSpriteCloneCopy.Refresh( clone, source );
		Assert.AreEqual( StaffSpriteTickStatus.Stopped, StaffSpriteVm.Tick( clone, 101 ).Status );
		Assert.AreEqual( 163, clone.Due );
		Assert.AreEqual( 62, clone.Interval );
		Assert.AreEqual( -3, clone.IfDepth );
		Assert.AreEqual( 0, clone.LoopDepth );
		Assert.AreEqual( 4, clone.CallDepth );
		Assert.AreEqual( 1234u, clone[StaffSpriteRegister.Scratch] );
		Assert.AreEqual( source.CurrentPc, clone.CurrentPc );
	}

	[TestMethod]
	public void ACloneExecutingSetLosesItsScreenFlagsUntilAnExplicitPartialRefresh()
	{
		var source = StaffSpriteVm.Create( StaffSpriteProgram.Walk, 0 );
		source[StaffSpriteRegister.Flags] = 0x80000201;
		var clone = StaffSpriteCloneCopy.Create( source, 2, 0, 0, 0, 0 );
		Assert.AreEqual( 0x80050001u, clone[StaffSpriteRegister.Flags] );
		Step( clone );
		Assert.AreEqual( 0x1200u, clone[StaffSpriteRegister.Flags] );
		Assert.AreEqual( 0x80000201u, source[StaffSpriteRegister.Flags] );
		StaffSpriteCloneCopy.Refresh( clone, source );
		Assert.AreEqual( 0x51000u, clone[StaffSpriteRegister.Flags] );
	}

	[TestMethod]
	public void IndependentClockExampleKeepsCloneIntervalAndDueAcrossSourceRefreshes()
	{
		var source = StaffSpriteVm.Create( StaffSpriteProgram.Walk, 0 );
		source.Due = 100;
		var clone = StaffSpriteCloneCopy.Create( source, 2, 0, 0, 0, 0 );
		StaffSpriteVm.ApplyInterval( source, 20 );
		Assert.AreEqual( StaffSpriteTickStatus.NotDue, StaffSpriteVm.Tick( source, 100 ).Status );
		Assert.AreEqual( StaffSpriteTickStatus.NotDue, StaffSpriteVm.Tick( clone, 100 ).Status );
		StaffSpriteVm.Tick( source, 101 ); StaffSpriteVm.Tick( clone, 101 );
		Assert.AreEqual( 121, source.Due ); Assert.AreEqual( 163, clone.Due );
		StaffSpriteCloneCopy.Refresh( clone, source );
		StaffSpriteVm.Tick( source, 122 );
		Assert.AreEqual( StaffSpriteTickStatus.NotDue, StaffSpriteVm.Tick( clone, 122 ).Status );
		StaffSpriteVm.Tick( clone, 164 ); // no source availability or rendering condition participates in this pure opportunity
		Assert.AreEqual( 226, clone.Due );
		StaffSpriteCloneCopy.Refresh( clone, source );
		Assert.AreEqual( 226, clone.Due ); Assert.AreEqual( 62, clone.Interval );
	}

	[TestMethod]
	public void EqualityLargeLateSamplesAndZeroIntervalsNeverCatchUp()
	{
		var state = StaffSpriteVm.Create( StaffSpriteProgram.Walk, 1000 );
		Assert.AreEqual( 1062, state.Due );
		var before = Words( state );
		Assert.AreEqual( StaffSpriteTickStatus.NotDue, StaffSpriteVm.Tick( state, 1062 ).Status );
		CollectionAssert.AreEqual( before, Words( state ) );
		Assert.AreEqual( 3, StaffSpriteVm.Tick( state, 1000000 ).Instructions );
		Assert.AreEqual( 0u, state[StaffSpriteRegister.Frame] );
		Assert.AreEqual( 1000062, state.Due );
		StaffSpriteVm.ApplyInterval( state, 0 );
		Assert.AreEqual( 62, state.Interval ); // wrapper zero request differs from a raw zero interval
		state.Interval = 0;
		StaffSpriteVm.Tick( state, 1000063 );
		Assert.AreEqual( 1000063, state.Due );
		Assert.AreEqual( StaffSpriteTickStatus.NotDue, StaffSpriteVm.Tick( state, 1000063 ).Status );
		StaffSpriteVm.Tick( state, 1000064 );
		Assert.AreEqual( 2u, state[StaffSpriteRegister.Frame] );
	}

	[TestMethod]
	public void SignedClockUsesDirectComparisonAndUncheckedAdditionAtBothBoundaries()
	{
		var state = StaffSpriteVm.Create( StaffSpriteProgram.Walk, 0 );
		state.Due = int.MaxValue - 1;
		Assert.AreEqual( StaffSpriteTickStatus.Yielded, StaffSpriteVm.Tick( state, int.MaxValue ).Status );
		Assert.AreEqual( unchecked( int.MaxValue + 62 ), state.Due );
		Assert.AreEqual( StaffSpriteTickStatus.NotDue, StaffSpriteVm.Tick( state, int.MinValue ).Status );
		// The same large positive sample is greater than the wrapped negative due: deliberately not wrap-safe subtraction.
		Assert.AreEqual( StaffSpriteTickStatus.Yielded, StaffSpriteVm.Tick( state, int.MaxValue ).Status );
		state.Due = int.MinValue;
		StaffSpriteVm.ApplyInterval( state, -62 );
		Assert.AreEqual( int.MinValue, state.Due );
		StaffSpriteVm.Tick( state, int.MinValue + 1 );
		Assert.AreEqual( unchecked( int.MinValue + 1 - 62 ), state.Due );
		Assert.AreEqual( StaffSpriteTickStatus.NotDue, StaffSpriteVm.Tick( state, int.MinValue + 2 ).Status );
	}

	[TestMethod]
	public void StoppedStateDoesNotExecuteButStillRebasesDueAndTemplateReplacementDoesNotResumeIt()
	{
		var state = StaffSpriteVm.Create( StaffSpriteProgram.Walk, 0 );
		Step( state );
		state.RunState = StaffSpriteRunState.Stopped;
		StaffSpriteVm.ApplyTemplate( state, StaffSpriteProgram.Run );
		var before = Words( state );
		var result = Step( state );
		Assert.AreEqual( StaffSpriteTickStatus.Stopped, result.Status );
		Assert.AreEqual( 0, result.Instructions );
		before[31] = unchecked( before[31] + 63 );
		CollectionAssert.AreEqual( before, Words( state ) );
	}

	[TestMethod]
	public void SyntheticEndClearsOutputButRemovalIsOnlyReportedAtTheNextDueOpportunity()
	{
		var state = SyntheticState();
		state.OutputValid = 1;
		var code = Code( new StaffSpriteInstruction( StaffSpriteOp.End ) );
		Assert.AreEqual( StaffSpriteTickStatus.Ended, Step( state, code ).Status );
		Assert.AreEqual( StaffSpriteRunState.Ended, state.RunState );
		Assert.IsFalse( state.HasOutput );
		var before = Words( state );
		Assert.AreEqual( StaffSpriteTickStatus.NotDue, StaffSpriteVm.Tick( state, state.Due, code ).Status );
		Assert.AreEqual( StaffSpriteTickStatus.RemovalDue, Step( state, code ).Status );
		CollectionAssert.AreEqual( before, Words( state ) );
		StaffSpriteVm.ApplyTemplate( state, StaffSpriteProgram.Stand );
		Assert.AreEqual( StaffSpriteTickStatus.RemovalDue, Step( state ).Status, "A new template cannot resurrect ended state." );
	}

	[TestMethod]
	public void FiftyInstructionCapPreservesRunningStateAndOutputWithoutInventingAYield()
	{
		var state = SyntheticState();
		state.OutputValid = 1;
		state[StaffSpriteRegister.Frame] = 17;
		var code = Code( new StaffSpriteInstruction( StaffSpriteOp.Add, 0, 1 ), new( StaffSpriteOp.Jump, Target: Pc( Synthetic ) ) );
		var result = Step( state, code );
		Assert.AreEqual( StaffSpriteTickStatus.InstructionLimit, result.Status );
		Assert.AreEqual( 50, result.Instructions );
		Assert.AreEqual( 25u, state[StaffSpriteRegister.Scratch] );
		Assert.AreEqual( Pc( Synthetic ), state.CurrentPc );
		Assert.AreEqual( Pc( Synthetic, 1 ), state.LastPc );
		Assert.AreEqual( StaffSpriteRunState.Running, state.RunState );
		Assert.AreEqual( 17u, state[StaffSpriteRegister.Frame] );
		Assert.IsTrue( state.HasOutput );
		Step( state, code );
		Assert.AreEqual( 50u, state[StaffSpriteRegister.Scratch] );
	}

	[TestMethod]
	public void NestedFalseIfSkipsInstructionsButStillDispatchesItsOwnEndIf()
	{
		var state = SyntheticState();
		var code = Code( new StaffSpriteInstruction( StaffSpriteOp.IfGreaterLiteral, 0, 0 ), new( StaffSpriteOp.IfGreaterLiteral, 0, -1 ),
			new( StaffSpriteOp.Set, 18, 77 ), new( StaffSpriteOp.EndIf ), new( StaffSpriteOp.EndIf ), new( StaffSpriteOp.Frame, 4 ) );
		var result = Step( state, code );
		Assert.AreEqual( 3, result.Instructions );
		Assert.AreEqual( 0, state.IfDepth );
		Assert.AreEqual( 0u, state[StaffSpriteRegister.Hold] );
		Assert.AreEqual( 4u, state[StaffSpriteRegister.Frame] );
	}

	[TestMethod]
	public void EndIfUnderflowRemainsNegativeAndLaterInstructionsAreNotNormalizedOrRejected()
	{
		var state = SyntheticState();
		var code = Code( new StaffSpriteInstruction( StaffSpriteOp.EndIf ), new( StaffSpriteOp.Frame, 5 ), new( StaffSpriteOp.Frame, 6 ) );
		var result = Step( state, code );
		Assert.AreEqual( StaffSpriteTickStatus.Yielded, result.Status );
		Assert.AreEqual( StaffSpriteVmDiagnostic.IfDepthUnderflow, result.Diagnostics );
		Assert.AreEqual( -1, state.IfDepth );
		Assert.AreEqual( StaffSpriteTickStatus.Yielded, Step( state, code ).Status );
		Assert.AreEqual( -1, state.IfDepth );
		Assert.AreEqual( 6u, state[StaffSpriteRegister.Frame] );
	}

	[TestMethod]
	public void WhileWithoutDoConsumesItsOperandsWithoutTouchingStackOrDepth()
	{
		foreach ( var depth in new[] { 0, -3 } )
		{
			var state = SyntheticState();
			state.LoopDepth = depth;
			state.Stack[19] = 0x12345678;
			var code = Code( new StaffSpriteInstruction( StaffSpriteOp.WhileLessEqualRegister, 0, 0 ), new( StaffSpriteOp.Frame, 5 ) );
			var result = Step( state, code );
			Assert.AreEqual( StaffSpriteTickStatus.Yielded, result.Status );
			Assert.AreEqual( StaffSpriteVmDiagnostic.WhileWithoutDo, result.Diagnostics );
			Assert.AreEqual( depth, state.LoopDepth );
			Assert.AreEqual( 20, state.StackPointer );
			Assert.AreEqual( 0x12345678u, state.Stack[19] );
		}
	}

	[TestMethod]
	public void FullStackDoDoesNotWriteButStillIncrementsLoopDepthAndContinuesNextOpportunity()
	{
		var state = SyntheticState();
		state.StackPointer = 0;
		state.LoopDepth = 20;
		for ( var i = 0; i < 20; i++ ) state.Stack[i] = (uint)i + 100;
		var before = (uint[])state.Stack.Clone();
		var code = Code( new StaffSpriteInstruction( StaffSpriteOp.Do ), new( StaffSpriteOp.Frame, 1 ), new( StaffSpriteOp.Frame, 2 ) );
		var result = Step( state, code );
		Assert.AreEqual( StaffSpriteVmDiagnostic.FullStack, result.Diagnostics );
		Assert.AreEqual( 21, state.LoopDepth );
		CollectionAssert.AreEqual( before, state.Stack );
		Assert.AreEqual( StaffSpriteTickStatus.Yielded, Step( state, code ).Status );
		Assert.AreEqual( 21, state.LoopDepth );
	}

	[TestMethod]
	public void FalseWhileMayDiscardAnUnsupportedOrEmptyReturnWordWithoutExecutingIt()
	{
		foreach ( var sp in new[] { 19, 20 } )
		{
			var state = SyntheticState();
			state.StackPointer = sp;
			state.Stack[19] = 0xDEADBEEF;
			state.LoopDepth = 1;
			var code = Code( new StaffSpriteInstruction( StaffSpriteOp.WhileNotEqualRegister, 0, 0 ), new( StaffSpriteOp.Frame, 8 ) );
			var result = Step( state, code );
			Assert.AreEqual( StaffSpriteTickStatus.Yielded, result.Status );
			Assert.AreEqual( 0, state.LoopDepth );
			Assert.AreEqual( 20, state.StackPointer );
			Assert.AreEqual( 0xDEADBEEFu, state.Stack[19] );
			Assert.AreEqual( sp == 20 ? StaffSpriteVmDiagnostic.EmptyStack : StaffSpriteVmDiagnostic.None, result.Diagnostics );
		}
	}

	[TestMethod]
	public void TakenWhileWithUnknownOrEmptyReturnFailsExplicitlyInsteadOfGuessingAFrame()
	{
		foreach ( var sp in new[] { 19, 20 } )
		{
			var state = SyntheticState();
			state.StackPointer = sp; state.LoopDepth = 1; state.Stack[19] = 0xDEADBEEF; state.OutputValid = 1;
			var result = Step( state, Code( new StaffSpriteInstruction( StaffSpriteOp.WhileLessEqualRegister, 0, 0 ), new( StaffSpriteOp.Frame, 9 ) ) );
			Assert.AreEqual( StaffSpriteTickStatus.Invalid, result.Status );
			Assert.AreEqual( StaffSpriteVmError.UnsupportedStackPc, result.Error );
			Assert.IsFalse( state.HasOutput );
			Assert.AreEqual( Pc( Synthetic ), state.CurrentPc );
			Assert.AreEqual( sp, state.StackPointer );
			Assert.AreEqual( 1, state.LoopDepth );
		}
	}

	[TestMethod]
	public void SetAndAddUseFloatArithmeticOnlyForRegistersOneThroughSixAndCopyIsAlwaysRaw()
	{
		var state = SyntheticState();
		var instructions = new List<StaffSpriteInstruction>();
		for ( var register = 0; register < 19; register++ )
		{
			instructions.Add( new( StaffSpriteOp.Set, register, -3 ) );
			instructions.Add( new( StaffSpriteOp.Add, register, 5 ) );
		}
		instructions.Add( new( StaffSpriteOp.Frame, 7 ) );
		Assert.AreEqual( StaffSpriteTickStatus.Yielded, Step( state, Code( instructions.ToArray() ) ).Status );
		for ( var register = 0; register < 19; register++ )
			Assert.AreEqual( register == 13 ? 7u : register is >= 1 and <= 6 ? BitConverter.SingleToUInt32Bits( 2f ) : 2u, state.Registers[register] );
		state.CurrentPc = Pc( Synthetic );
		state.Registers[1] = 0x7FC12345; // arbitrary NaN bits are copied rather than converted or normalized
		Step( state, Code( new StaffSpriteInstruction( StaffSpriteOp.Copy, 0, 1 ), new( StaffSpriteOp.Copy, 2, 0 ), new( StaffSpriteOp.Frame, 1 ) ) );
		Assert.AreEqual( 0x7FC12345u, state.Registers[0] );
		Assert.AreEqual( 0x7FC12345u, state.Registers[2] );
	}

	[TestMethod]
	public void IntegerAdditionWrapsAndFloatAdditionDoesNotRoundTheIntegerBeforeAdding()
	{
		var state = SyntheticState();
		state.Registers[0] = uint.MaxValue;
		state.Registers[1] = BitConverter.SingleToUInt32Bits( -16777216f );
		Step( state, Code( new StaffSpriteInstruction( StaffSpriteOp.Add, 0, 2 ), new( StaffSpriteOp.Add, 1, 16777217 ), new( StaffSpriteOp.Frame, 0 ) ) );
		Assert.AreEqual( 1u, state.Registers[0] );
		Assert.AreEqual( BitConverter.SingleToUInt32Bits( 1f ), state.Registers[1] );
	}

	[TestMethod]
	public void SignedConditionsDoNotTreatNegativeWordsAsLargeUnsignedValues()
	{
		var state = SyntheticState();
		state[StaffSpriteRegister.Intro] = uint.MaxValue;
		var code = Code( new StaffSpriteInstruction( StaffSpriteOp.IfGreaterLiteral, 17, 0 ), new( StaffSpriteOp.Set, 0, 99 ),
			new( StaffSpriteOp.EndIf ), new( StaffSpriteOp.Frame, 2 ) );
		Step( state, code );
		Assert.AreEqual( 0u, state[StaffSpriteRegister.Scratch] );
		state.CurrentPc = Pc( Synthetic ); state.StackPointer = 19; state.Stack[19] = Pc( Synthetic, 2 ).Word; state.LoopDepth = 1;
		state.Registers[0] = uint.MaxValue; state.Registers[18] = 0;
		Step( state, Code( new StaffSpriteInstruction( StaffSpriteOp.WhileLessEqualRegister, 0, 18 ), new( StaffSpriteOp.Frame, 8 ), new( StaffSpriteOp.Frame, 9 ) ) );
		Assert.AreEqual( 9u, state[StaffSpriteRegister.Frame] );
	}

	[TestMethod]
	public void InvalidPcTableRunStateAndStackBoundsFailClosedWithSpecificReasons()
	{
		var mutations = new (Action<StaffSpriteVmState> Change, StaffSpriteVmError Error)[]
		{
			(state => state.CurrentPc = Pc( StaffSpriteProgram.Walk, 1000 ), StaffSpriteVmError.UnsupportedPc),
			(state => state.CurrentPc = new( 0 ), StaffSpriteVmError.UnsupportedPc),
			(state => state.ProgramTable = 17, StaffSpriteVmError.UnsupportedProgramTable),
			(state => state.RunState = (StaffSpriteRunState)0, StaffSpriteVmError.InvalidRunState),
			(state => state.StackPointer = -1, StaffSpriteVmError.InvalidStackPointer),
			(state => state.StackPointer = 21, StaffSpriteVmError.InvalidStackPointer)
		};
		foreach ( var (change, error) in mutations )
		{
			var state = StaffSpriteVm.Create( StaffSpriteProgram.Walk, 0 );
			state.OutputValid = 1; change( state );
			var result = Step( state );
			Assert.AreEqual( StaffSpriteTickStatus.Invalid, result.Status );
			Assert.AreEqual( error, result.Error );
			Assert.IsFalse( state.HasOutput );
		}
	}

	[TestMethod]
	public void InvalidInstructionOperandsAndMissingBlocksNeverReadOutsideStateOrChooseFallbackFrames()
	{
		var cases = new (StaffSpriteInstruction Instruction, StaffSpriteVmError Error)[]
		{
			(new( StaffSpriteOp.Set, -1, 0 ), StaffSpriteVmError.InvalidRegister),
			(new( StaffSpriteOp.Add, 19, 0 ), StaffSpriteVmError.InvalidRegister),
			(new( StaffSpriteOp.Copy, 0, 19 ), StaffSpriteVmError.InvalidRegister),
			(new( StaffSpriteOp.FrameRegister, 19 ), StaffSpriteVmError.InvalidRegister),
			(new( StaffSpriteOp.IfGreaterLiteral, 0, 0 ), StaffSpriteVmError.MissingEndIf),
			(new( StaffSpriteOp.Jump, Target: Pc( StaffSpriteProgram.Run ) ), StaffSpriteVmError.UnsupportedPc),
			(new( (StaffSpriteOp)999 ), StaffSpriteVmError.UnsupportedInstruction)
		};
		foreach ( var (instruction, error) in cases )
		{
			var state = SyntheticState(); state.OutputValid = 1;
			var result = Step( state, Code( instruction, new( StaffSpriteOp.Frame, 99 ) ) );
			Assert.AreEqual( StaffSpriteTickStatus.Invalid, result.Status );
			Assert.AreEqual( error, result.Error );
			Assert.IsFalse( state.HasOutput );
			Assert.AreEqual( Pc( Synthetic ), state.CurrentPc );
		}
	}

	[TestMethod]
	public void UnsupportedTemplateAndInvalidPerformanceMetadataFailBeforeChangingState()
	{
		var state = Sentinel( 1000 ); var before = Words( state );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => StaffSpriteVm.ApplyTemplate( state, (StaffSpriteProgram)999 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => StaffSpriteVm.ApplyTemplate( state, StaffSpriteProgram.Performance0Loop ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => StaffSpriteVm.ApplyPerformance( state, 4, 7, 8 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => StaffSpriteVm.ApplyPerformance( state, 0, -1, 8 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => StaffSpriteVm.ApplyPerformance( state, 0, 7, -1 ) );
		CollectionAssert.AreEqual( before, Words( state ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => new StaffSpritePc( StaffSpriteProgram.Run, -1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => new StaffSpritePc( StaffSpriteProgram.Run, 65536 ) );
	}

	[TestMethod]
	public void NonFiniteFloatArithmeticIsExplicitlyUnqualifiedButRawCopyIsStillAllowed()
	{
		var state = SyntheticState(); state.Registers[1] = BitConverter.SingleToUInt32Bits( float.NaN );
		var result = Step( state, Code( new StaffSpriteInstruction( StaffSpriteOp.Add, 1, 1 ) ) );
		Assert.AreEqual( StaffSpriteTickStatus.Invalid, result.Status );
		Assert.AreEqual( StaffSpriteVmError.NonFiniteArithmetic, result.Error );
	}

	[TestMethod]
	public void ProgramDescriptionsAreCopiedAndCannotBeMutatedThroughTheirConstructionInput()
	{
		var instructions = new[] { new StaffSpriteInstruction( StaffSpriteOp.Frame, 3 ) };
		var code = Code( instructions );
		instructions[0] = new( StaffSpriteOp.Frame, 99 );
		var state = SyntheticState(); Step( state, code );
		Assert.AreEqual( 3u, state[StaffSpriteRegister.Frame] );
	}
	[TestMethod]
	public void AYieldOnInstructionFiftyWinsOverTheDispatchCap()
	{
		var instructions = Enumerable.Range( 0, 49 ).Select( _ => new StaffSpriteInstruction( StaffSpriteOp.Add, 0, 1 ) ).ToList();
		instructions.Add( new( StaffSpriteOp.Frame, 6 ) );
		var state = SyntheticState();
		var result = Step( state, Code( instructions.ToArray() ) );
		Assert.AreEqual( StaffSpriteTickStatus.Yielded, result.Status );
		Assert.AreEqual( 50, result.Instructions );
		Assert.AreEqual( 49u, state[StaffSpriteRegister.Scratch] );
		Assert.AreEqual( 6u, state[StaffSpriteRegister.Frame] );
	}

	[TestMethod]
	public void PartialRefreshIntoEndedStateCopiesOutputButCannotResurrectIt()
	{
		var source = StaffSpriteVm.Create( StaffSpriteProgram.Run, 0 );
		Step( source );
		var clone = StaffSpriteVm.Create( StaffSpriteProgram.Stand, 0 );
		clone.RunState = StaffSpriteRunState.Ended;
		StaffSpriteCloneCopy.Refresh( clone, source );
		Assert.AreEqual( StaffSpriteRunState.Ended, clone.RunState );
		Assert.IsTrue( clone.HasOutput );
		Assert.AreEqual( StaffSpriteTickStatus.RemovalDue, Step( clone ).Status );
	}

	[TestMethod]
	public void DifferentRefreshBoundariesMayLegitimatelyProduceDifferentCloneHistories()
	{
		var source = StaffSpriteVm.Create( StaffSpriteProgram.Walk, 0 );
		var afterEveryTick = StaffSpriteCloneCopy.Create( source, 2, 0, 0, 0, 0 );
		var afterBatch = StaffSpriteCloneCopy.Create( source, 3, 0, 0, 0, 0 );
		source.Interval = 20;
		foreach ( var now in new[] { 63, 84, 105 } )
		{
			StaffSpriteVm.Tick( source, now );
			StaffSpriteVm.Tick( afterEveryTick, now );
			StaffSpriteVm.Tick( afterBatch, now );
			StaffSpriteCloneCopy.Refresh( afterEveryTick, source );
		}
		// A refresh before the clone's next due sample changes its cursor; a deferred refresh does not yet do so.
		StaffSpriteVm.Tick( afterEveryTick, 126 );
		StaffSpriteVm.Tick( afterBatch, 126 );
		Assert.AreEqual( 3u, afterEveryTick[StaffSpriteRegister.Frame] );
		Assert.AreEqual( 1u, afterBatch[StaffSpriteRegister.Frame] );
		Assert.AreEqual( afterEveryTick.Due, afterBatch.Due );
	}

	[TestMethod]
	public void MaximumEncodedInstructionAndOversizedProgramAreRejectedWithoutOverflowingFetch()
	{
		var state = StaffSpriteVm.Create( StaffSpriteProgram.Walk, 0 );
		state.CurrentPc = Pc( StaffSpriteProgram.Walk, ushort.MaxValue );
		Assert.AreEqual( StaffSpriteVmError.UnsupportedPc, Step( state ).Error );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Code( new StaffSpriteInstruction[65536] ) );
	}

	[TestMethod]
	public void ARealCopiedHoldPcWithAnEmptyCloneStackPreservesNativeDiagnosticContinuation()
	{
		var source = Performance( StaffSpriteProgram.Performance0, 0, 1, 1 );
		Step( source ); // main frame 0
		Step( source ); // first hold frame 0
		Assert.AreEqual( Pc( StaffSpriteProgram.Performance0Loop, 9 ), source.CurrentPc );
		var clone = StaffSpriteVm.Create( StaffSpriteProgram.Stand, 0 );
		clone[StaffSpriteRegister.Scratch] = 10;
		StaffSpriteCloneCopy.Refresh( clone, source );
		Assert.AreEqual( 0, clone.LoopDepth );
		Assert.AreEqual( 0, clone.IfDepth );
		var result = Step( clone );
		Assert.AreEqual( StaffSpriteTickStatus.Yielded, result.Status );
		Assert.AreEqual( StaffSpriteVmDiagnostic.WhileWithoutDo | StaffSpriteVmDiagnostic.IfDepthUnderflow, result.Diagnostics );
		Assert.AreEqual( -1, clone.IfDepth );
		Assert.AreEqual( 11u, clone[StaffSpriteRegister.Scratch] );
		Assert.AreEqual( 19, clone.StackPointer );
		Assert.AreEqual( 1, clone.LoopDepth );
		Assert.AreEqual( Pc( StaffSpriteProgram.Performance0Loop, 3 ), clone.CurrentPc );
		Assert.AreEqual( 1, source.IfDepth );
		Assert.AreEqual( 0u, source[StaffSpriteRegister.Scratch] );
		Assert.AreEqual( StaffSpriteTickStatus.Yielded, Step( clone ).Status );
	}

	[TestMethod]
	public void RawPerformanceBoundaryRegistersAreExecutedWithoutClampingAnInventedFrame()
	{
		var negativeIntro = Performance( StaffSpriteProgram.Performance0, -1, 0, 3 );
		Assert.AreEqual( StaffSpriteTickStatus.Yielded, Step( negativeIntro ).Status );
		Assert.AreEqual( uint.MaxValue, negativeIntro[StaffSpriteRegister.Frame] );
		Assert.IsFalse( negativeIntro.HasOutput );
		Step( negativeIntro );
		Assert.AreEqual( 0u, negativeIntro[StaffSpriteRegister.Frame] );
		Assert.IsTrue( negativeIntro.HasOutput );
		var beyondRange = Performance( StaffSpriteProgram.Performance0, 0, 0, 0 );
		for ( var step = 0; step < 4; step++ )
		{
			Assert.AreEqual( StaffSpriteTickStatus.Yielded, Step( beyondRange ).Status );
			Assert.AreEqual( (uint)step, beyondRange[StaffSpriteRegister.Frame] );
		}
		// This is raw VM behavior only. A future renderer must reject unavailable resource frames.
	}

}
