using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// The VM-internal semantics read from tp.exe (docs/RSE-VM.md, "Original interpreter internals"): the critical flag and the slice
/// budget (0x551701..0x551736, CRIT_UNLOCK 0x551D59), TURBO (0x5542B9), the per-script heap shared with the call stack (HUSH 0x553D25,
/// HOP 0x553DAA, JSR 0x5539A9, RETURN 0x553A32, PUSH 0x553C1E, POP 0x553CBA), the limbo table (0x554DE5..0x555045), SETLV (0x551E19)
/// and DBGMSG (0x554243). Synthetic scripts only; the original corpus runs live in <see cref="RideVMCorpusTests"/>.
/// </summary>
[TestClass]
public class RideVMInternalsTests
{
	private static readonly string[] Names = { "V0", "V1", "V2", "V3" };
	private static readonly string[] Wide = { "A", "B", "C", "D", "E", "F", "G", "H" };

	private static RideVM Load( Asm asm, RideVMOptions? options = null, string[]? variables = null, params string[] strings )
		=> new( new RideScriptFile( new MemoryStream( asm.Build( variables ?? Names, strings ) ) ), options ?? new RideVMOptions { Seed = 1 } );

	private static void Run( RideVM vm, int ticks )
	{
		for ( var tick = 0; tick < ticks; ++tick )
			vm.Advance( Tick );
	}

	private sealed class CountingEffects : IRideScriptEffects
	{
		public int Calls;

		public int Perform( RideEffectCall call )
		{
			Calls++;
			return 0;
		}
	}

	// ---- critical flag and slice budget ----

	[TestMethod]
	public void InstructionsInsideACriticalSectionAreFreeAndUnlockEndsTheSlice()
	{
		// Budget 4: the two adds count, CRIT_LOCK and everything after it do not (the flag is read after the instruction, 0x551724),
		// and CRIT_UNLOCK zeroes the counter (0x551D5F), so the adds behind it wait for the next slice.
		var vm = Load( new Asm()
			.I( Opcode.ADD, V( 0 ), 1 ).I( Opcode.ADD, V( 0 ), 1 )
			.I( Opcode.CRIT_LOCK )
			.Label( "locked" ).I( Opcode.ADD, V( 1 ), 1 ).I( Opcode.CMP, V( 1 ), 10 ).I( Opcode.BRANCH_NZ, "locked" )
			.I( Opcode.CRIT_UNLOCK )
			.I( Opcode.ADD, V( 2 ), 1 ).I( Opcode.ADD, V( 2 ), 1 ).I( Opcode.ADD, V( 2 ), 1 ).I( Opcode.ADD, V( 2 ), 1 ).I( Opcode.ADD, V( 2 ), 1 ).I( Opcode.ADD, V( 2 ), 1 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ), new RideVMOptions { Seed = 1, SliceBudget = 4 } );
		Run( vm, 1 );
		CollectionAssert.AreEqual( new[] { 2, 10, 0, 0 }, vm.Variables, "the locked loop ran to its end and CRIT_UNLOCK ended the slice" );
		Assert.IsFalse( vm.InCriticalSection );
		Run( vm, 1 );
		Assert.AreEqual( 4, vm.Variables[2], "the next slice runs the budget of four outside a critical section" );
	}

	[TestMethod]
	public void TheCriticalFlagIsClearedAtEverySliceStart()
	{
		// A slice that ends inside the section (ENDSLICE, a wait) leaves the flag set, but the next slice starts with it clear (0x551701).
		var vm = Load( new Asm().I( Opcode.CRIT_LOCK ).I( Opcode.ENDSLICE ).I( Opcode.NOP ).I( Opcode.ENDSLICE ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ) );
		var seen = new List<(Opcode Opcode, bool Locked)>();
		vm.InstructionExecuting = ( machine, instruction ) => seen.Add( ((Opcode)instruction.RawOpcode, machine.InCriticalSection) );
		Run( vm, 1 );
		Assert.IsTrue( vm.InCriticalSection, "the slice ended inside the section" );
		Run( vm, 1 );
		Assert.AreEqual( (Opcode.NOP, false), seen[2], "the next slice's first instruction runs with the flag clear" );
	}

	[TestMethod]
	public void CritUnlockEndsTheSliceEvenWhenTheSectionWasNeverLocked()
	{
		var vm = Load( new Asm().I( Opcode.ADD, V( 0 ), 1 ).I( Opcode.CRIT_UNLOCK ).I( Opcode.ADD, V( 0 ), 1 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ) );
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.Variables[0] );
		Run( vm, 1 );
		Assert.AreEqual( 2, vm.Variables[0] );
	}

	[TestMethod]
	public void ASliceThatNeverUnlocksIsStoppedByTheManagedGuard()
	{
		// [EXT:crit-slice-guard]: the original would hang here.
		var vm = Load( new Asm().I( Opcode.CRIT_LOCK ).Label( "spin" ).I( Opcode.BRANCH, "spin" ) );
		Run( vm, 1 );
		Assert.AreEqual( RideVMState.Faulted, vm.State );
		StringAssert.Contains( vm.FaultMessage, "critical section" );
	}

	// ---- TURBO ----

	private static (int Byte, RideVM.VMFlags Flags, int Effects) TurboOf( Asm asm )
	{
		var effects = new CountingEffects();
		var vm = Load( asm.I( Opcode.ENDSLICE ), new RideVMOptions { Seed = 1, Effects = effects } );
		Run( vm, 1 );
		return (vm.TurboByte, vm.Flags, effects.Calls);
	}

	[TestMethod]
	public void TurboStoresTheLowByteOfTheRawOperandWordAndNothingElse()
	{
		Assert.AreEqual( (1, RideVM.VMFlags.Zero, 0), TurboOf( new Asm().I( Opcode.TEST, V( 3 ) ).I( Opcode.TURBO, 1 ) ), "a literal; the flags of the TEST survive and no effect runs" );
		Assert.AreEqual( 0xFF, TurboOf( new Asm().I( Opcode.TURBO, 0xFFFF ) ).Byte, "0xFFFF is not sign-extended or resolved: its low byte" );
		Assert.AreEqual( 0xA5, TurboOf( new Asm().I( Opcode.TURBO, 0x1A5 ) ).Byte, "only the low byte of the word is kept" );
		var variable = TurboOf( new Asm().I( Opcode.TURBO, V( 2 ) ) );
		Assert.AreEqual( 2, variable.Byte, "a variable operand stores its index byte, not the variable's value" );
		var vm = Load( new Asm().I( Opcode.COPY, V( 2 ), 77 ).I( Opcode.TURBO, V( 2 ) ).I( Opcode.ENDSLICE ) );
		Run( vm, 1 );
		Assert.AreEqual( 2, vm.TurboByte );
		Assert.AreEqual( 0, Load( new Asm().I( Opcode.ENDSLICE ) ).TurboByte, "zero at creation" );
	}

	// ---- HUSH / HOP heap ----

	[TestMethod]
	public void HushAndHopAreAPerScriptLifoHeap()
	{
		var vm = Load( new Asm()
			.I( Opcode.HUSH, 5 ).I( Opcode.HUSH, V( 0 ) ).I( Opcode.HUSH, 0xFFFF )
			.I( Opcode.HOP, V( 1 ) ).I( Opcode.HOP, V( 2 ) ).I( Opcode.HOP, V( 3 ) )
			.I( Opcode.HOP, V( 1 ) )
			.I( Opcode.ENDSLICE ) );
		vm.Variables[0] = 7;
		Run( vm, 1 );
		Assert.AreEqual( -1, vm.Variables[1], "the newest value (HUSH 0xFFFF, -1) came back first and the empty HOP left its destination alone" );
		Assert.AreEqual( 7, vm.Variables[2] );
		Assert.AreEqual( 5, vm.Variables[3] );
		Assert.AreEqual( 0, vm.HeapCount );
		Assert.AreEqual( RideVM.VMFlags.None, vm.Flags, "the empty HOP did not touch the flags the previous HOP set (5)" );
	}

	[TestMethod]
	public void HushSetsTheFlagsAndAFullHeapDoesNothingAtAll()
	{
		var vm = Load( new Asm().WithHeader( 2, 2 )
			.I( Opcode.HUSH, 1 ).I( Opcode.HUSH, 0 ).I( Opcode.HUSH, 9 )
			.I( Opcode.ENDSLICE ) );
		Run( vm, 1 );
		Assert.AreEqual( 2, vm.HeapCount );
		Assert.AreEqual( RideVM.VMFlags.Zero, vm.Flags, "the full HUSH 9 left the flags of HUSH 0 alone" );
		Assert.AreEqual( 1, vm.GetStackSlot( 0 ) );
		Assert.AreEqual( 0, vm.GetStackSlot( 1 ), "the full HUSH 9 stored nothing" );
		Assert.AreEqual( RideVMState.Running, vm.State, "the original's assert is a no-op, the script goes on" );
	}

	[TestMethod]
	public void HopToALiteralOnlySetsTheFlags()
	{
		var vm = Load( new Asm().I( Opcode.HUSH, 0 ).I( Opcode.HUSH, 3 ).I( Opcode.HOP, 0 ).I( Opcode.ENDSLICE ) );
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.HeapCount );
		Assert.AreEqual( RideVM.VMFlags.None, vm.Flags, "3 is neither zero nor negative" );
	}

	[TestMethod]
	public void HushAndTheEffectsHookAreIndependent()
	{
		var effects = new CountingEffects();
		var vm = Load( new Asm().I( Opcode.HUSH, 44 ).I( Opcode.HOP, V( 0 ) ).I( Opcode.ENDSLICE ), new RideVMOptions { Seed = 1, Effects = effects } );
		Run( vm, 1 );
		Assert.AreEqual( 44, vm.Variables[0] );
		Assert.AreEqual( 0, effects.Calls, "HUSH and HOP are not visitor operations: no game effect is requested" );
	}

	[TestMethod]
	public void TheHeapAndTheCallStackShareOneArrayWithoutACollisionCheck()
	{
		// Size 2: HUSH fills index 0, JSR puts its return address at index 1, PUSH inside the routine overwrites index 0 (the HUSH value).
		// The first RETURN then pops that plain value, which is not an address, and the pc stays; the second pops the return address.
		var vm = Load( new Asm().WithHeader( 2, 2 )
			.I( Opcode.HUSH, 5 )
			.I( Opcode.JSR, "routine" )
			.I( Opcode.ADD, V( 0 ), 1 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Label( "routine" ).I( Opcode.PUSH, 9 ).I( Opcode.RETURN ).I( Opcode.ADD, V( 1 ), 1 ).I( Opcode.RETURN ) );
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.Variables[1], "the RETURN that popped the pushed value fell through to the next instruction" );
		Assert.AreEqual( 1, vm.Variables[0], "the second RETURN came back behind the JSR" );
		Assert.AreEqual( 0, vm.CallDepth );
		Assert.AreEqual( 1, vm.HeapCount, "the heap count was not touched" );
		Assert.AreEqual( 9, vm.GetStackSlot( 0 ), "the HUSH value was overwritten" );
	}

	[TestMethod]
	public void AHopThatReachesTheCallRegionReadsTheTaggedReturnAddress()
	{
		// Size 1: HUSH takes index 0, JSR overwrites it with its return address (code word 4 | 0x20000000), HOP then pops that word.
		var vm = Load( new Asm().WithHeader( 1, 2 )
			.I( Opcode.HUSH, 3 )
			.I( Opcode.JSR, "routine" )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Label( "routine" ).I( Opcode.HOP, V( 0 ) ).I( Opcode.HOP, V( 1 ) ).I( Opcode.RETURN ) );
		Run( vm, 1 );
		Assert.AreEqual( 0x20000004, vm.Variables[0], "the stored word is the code-word offset of the next instruction (HUSH 3 and JSR take two words each), tagged" );
		Assert.AreEqual( 0, vm.Variables[1], "the second HOP found the heap empty and left V1" );
		Assert.AreEqual( RideVMState.Running, vm.State );
	}

	// ---- JSR / RETURN / PUSH / POP ----

	[TestMethod]
	public void JsrOnAFullStackStillJumpsWithoutPushing()
	{
		// [BIN:TPW-EXE:0x5539F9]: the assert is a no-op and the jump to the operand runs; the return address is not stored.
		var vm = Load( new Asm().WithHeader( 1, 2 )
			.I( Opcode.JSR, "a" )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Label( "a" ).I( Opcode.JSR, "b" ).I( Opcode.ADD, V( 0 ), 1 ).I( Opcode.RETURN )
			.Label( "b" ).I( Opcode.ADD, V( 1 ), 1 ).I( Opcode.RETURN ) );
		Run( vm, 1 );
		Assert.AreEqual( RideVMState.Running, vm.State );
		Assert.AreEqual( 1, vm.Variables[1], "the second JSR jumped although the stack was full" );
		Assert.AreEqual( 0, vm.Variables[0], "its RETURN popped the first JSR's return address (the second was never stored), so the ADD behind the second JSR never ran" );
	}

	[TestMethod]
	public void PushAndPopUseTheCallStackArrayAndSetTheFlags()
	{
		var vm = Load( new Asm().WithHeader( 2, 2 )
			.I( Opcode.PUSH, 3 ).I( Opcode.PUSH, V( 0 ) )
			.I( Opcode.POP, V( 1 ) )
			.I( Opcode.TEST, V( 3 ) ) // Zero, to tell the POP flags apart
			.I( Opcode.POP, V( 2 ) )
			.I( Opcode.ENDSLICE ) );
		vm.Variables[0] = -4;
		var flags = new List<RideVM.VMFlags>();
		vm.InstructionExecuting = ( machine, _ ) => flags.Add( machine.Flags );
		Run( vm, 1 );
		Assert.AreEqual( -4, vm.Variables[1], "last in, first out" );
		Assert.AreEqual( 3, vm.Variables[2] );
		Assert.AreEqual( RideVM.VMFlags.Sign, flags[2], "PUSH V0 (-4) set the sign flag" );
		Assert.AreEqual( RideVM.VMFlags.Sign, flags[3], "POP V1 (-4) kept it" );
		Assert.AreEqual( RideVM.VMFlags.None, vm.Flags, "POP V2 (3)" );
		Assert.AreEqual( 0, vm.CallDepth );
	}

	[TestMethod]
	public void PushOnAFullStackAndPopFromAnEmptyOneKillTheScript()
	{
		var full = Load( new Asm().WithHeader( 1, 2 ).I( Opcode.PUSH, 1 ).I( Opcode.PUSH, 0 ).I( Opcode.ENDSLICE ) );
		Run( full, 1 );
		Assert.AreEqual( RideVMState.Faulted, full.State );
		Assert.AreEqual( -10000, full.ProgramCounter, "pc = -10000 as in the original" );
		Assert.AreEqual( RideVM.VMFlags.Zero, full.Flags, "the result register still took the pushed 0" );

		var empty = Load( new Asm().I( Opcode.POP, V( 0 ) ).I( Opcode.ENDSLICE ) );
		empty.Variables[0] = 9;
		Run( empty, 1 );
		Assert.AreEqual( RideVMState.Faulted, empty.State );
		Assert.AreEqual( 0, empty.Variables[0], "the shared tail writes the 0 result before the script dies" );
		Assert.AreEqual( RideVM.VMFlags.Zero, empty.Flags );
	}

	// ---- SETLV / DBGMSG ----

	private static RideVM.VMFlags FlagsAfter( Asm asm )
	{
		var vm = Load( asm.I( Opcode.ENDSLICE ) );
		Run( vm, 1 );
		return vm.Flags;
	}

	[TestMethod]
	public void SetLastValueSetsTheFlagsFromTheRawOperandWord()
	{
		Assert.AreEqual( RideVM.VMFlags.Zero, FlagsAfter( new Asm().I( Opcode.SETLV, 0 ) ) );
		Assert.AreEqual( RideVM.VMFlags.None, FlagsAfter( new Asm().I( Opcode.SETLV, 0xFFFF ) ), "0xFFFF is 65535, not -1: the word is not sign-extended" );
		Assert.AreEqual( RideVM.VMFlags.None, FlagsAfter( new Asm().I( Opcode.TEST, V( 3 ) ).I( Opcode.SETLV, V( 3 ) ) ), "a variable operand counts as its tagged word, not as the variable's value 0" );
	}

	[TestMethod]
	public void DebugMessageSkipsItsOperandAndDoesNothing()
	{
		var vm = Load( new Asm().I( Opcode.TEST, V( 3 ) ).I( Opcode.DBGMSG, S( 0 ) ).I( Opcode.ADD, V( 0 ), 1 ).I( Opcode.ENDSLICE ), null, null, "message" );
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.Variables[0] );
		Assert.AreEqual( RideVM.VMFlags.None, vm.Flags, "the ADD ran after the DBGMSG, which touched nothing" );
		Assert.AreEqual( RideVMState.Running, vm.State );
	}

	// ---- limbo ----

	[TestMethod]
	public void LimboFillsTheFirstFreeSlotAndRefusesWhenTheTableIsFull()
	{
		var vm = Load( new Asm()
			.I( Opcode.LIMBO, 11, 5 ).I( Opcode.BRANCH_NZ, "stored" ).I( Opcode.COPY, V( 7 ), 1 )
			.Label( "stored" ).I( Opcode.INLIMBO, V( 0 ) ).I( Opcode.LIMBOSPACE, V( 1 ) )
			.I( Opcode.LIMBO, 12, 5 )
			.I( Opcode.LIMBO, 13, 5 ).I( Opcode.BRANCH_Z, "refused" ).I( Opcode.COPY, V( 6 ), 1 ).I( Opcode.BRANCH, "after" )
			.Label( "refused" ).I( Opcode.COPY, V( 6 ), 2 )
			.Label( "after" ).I( Opcode.INLIMBO, V( 2 ) ).I( Opcode.LIMBOSPACE, V( 3 ) )
			.I( Opcode.ENDSLICE ), null, Wide );
		Run( vm, 1 );
		CollectionAssert.AreEqual( new[] { 1, 1, 2, 0 }, vm.Variables[..4], "occupied and free counts before and after the third LIMBO" );
		Assert.AreEqual( 0, vm.Variables[7], "the first LIMBO's result was 1 (non-zero)" );
		Assert.AreEqual( 2, vm.Variables[6], "the third LIMBO found no free slot: result 0, so the zero flag" );
		Assert.AreEqual( 11, vm.GetLimboSlot( 0 ).Value );
		Assert.AreEqual( 12, vm.GetLimboSlot( 1 ).Value, "slots fill in index order; the refused 13 was not stored" );
		Assert.AreEqual( vm.GetLimboSlot( 0 ).Expiry, vm.GetLimboSlot( 1 ).Expiry, "both holds were taken in the same slice on the same clock reading" );
		Assert.AreEqual( 2, vm.LimboCount );
	}

	[TestMethod]
	public void TheExpiryIsTheClockPlusSecondsTimesAThousandMilliseconds()
	{
		var vm = Load( new Asm().I( Opcode.LIMBO, 11, 5 ).I( Opcode.ENDSLICE ) );
		Run( vm, 1 );
		Assert.AreEqual( (int)vm.ClockMilliseconds32 + 5000, vm.GetLimboSlot( 0 ).Expiry );
		var variable = Load( new Asm().I( Opcode.COPY, V( 0 ), 7 ).I( Opcode.LIMBO, 11, V( 0 ) ).I( Opcode.ENDSLICE ) );
		Run( variable, 1 );
		Assert.AreEqual( (int)variable.ClockMilliseconds32 + 7000, variable.GetLimboSlot( 0 ).Expiry, "the duration is resolved (a variable operand)" );
	}

	[TestMethod]
	public void LimboOfZeroCountsAnOccupantButLeavesTheSlotFree()
	{
		var vm = Load( new Asm()
			.I( Opcode.LIMBO, 0, 5 ).I( Opcode.INLIMBO, V( 0 ) ).I( Opcode.LIMBOSPACE, V( 1 ) )
			.I( Opcode.LIMBO, 7, 5 ).I( Opcode.INLIMBO, V( 2 ) ).I( Opcode.LIMBOSPACE, V( 3 ) )
			.I( Opcode.ENDSLICE ) );
		Run( vm, 1 );
		CollectionAssert.AreEqual( new[] { 1, 1, 2, 0 }, vm.Variables );
		Assert.AreEqual( 7, vm.GetLimboSlot( 0 ).Value, "the value 0 left slot 0 free, so 7 took it" );
		Assert.AreEqual( 0, vm.GetLimboSlot( 1 ).Value );
	}

	[TestMethod]
	public void ALimboSizeOfZeroStoresNothing()
	{
		var vm = Load( new Asm().WithHeader( 4, 0 ).I( Opcode.LIMBO, 11, 5 ).I( Opcode.BRANCH_Z, "none" ).I( Opcode.COPY, V( 0 ), 1 ).Label( "none" ).I( Opcode.LIMBOSPACE, V( 1 ) ).I( Opcode.ENDSLICE ) );
		Run( vm, 1 );
		Assert.AreEqual( 0, vm.Variables[0] );
		Assert.AreEqual( 0, vm.Variables[1] );
		Assert.AreEqual( 0, vm.LimboCapacity );
		Assert.AreEqual( 0, vm.LimboCount );
	}

	[TestMethod]
	public void UnlimboReleasesStrictlyAfterTheExpiryAndInSlotOrder()
	{
		var vm = Load( new Asm()
			.I( Opcode.LIMBO, 11, 5 ).I( Opcode.LIMBO, 12, 1 )
			.Label( "poll" ).I( Opcode.ENDSLICE ).I( Opcode.UNLIMBO, V( 0 ) ).I( Opcode.BRANCH_Z, "poll" )
			.I( Opcode.ADD, V( 1 ), 1 )
			.Label( "done" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "done" ) );
		Run( vm, 1 );
		var (_, expiry) = vm.GetLimboSlot( 1 );
		Assert.AreEqual( (int)vm.ClockMilliseconds32 + 1000, expiry );
		// The clock reads exactly the expiry: the comparison is strict (jl, 0x554F3A), so nothing is released yet.
		vm.Advance( (expiry + 0.5 - vm.SchedulerMilliseconds) / 1000 );
		Assert.AreEqual( expiry, (int)vm.ClockMilliseconds32 );
		Assert.AreEqual( 0, vm.Variables[0], "an expiry equal to the clock is not past" );
		Assert.AreEqual( 2, vm.LimboCount );
		vm.Advance( 0.001 );
		Assert.AreEqual( expiry + 1, (int)vm.ClockMilliseconds32 );
		Assert.AreEqual( 12, vm.Variables[0], "slot 1 expired first; the unexpired slot 0 is skipped" );
		Assert.AreEqual( 1, vm.Variables[1] );
		Assert.AreEqual( 1, vm.LimboCount );
		Assert.AreEqual( (0, expiry), vm.GetLimboSlot( 1 ), "the slot is cleared in place and keeps its old expiry" );
		Assert.AreEqual( 11, vm.GetLimboSlot( 0 ).Value );
	}

	[TestMethod]
	public void ForceUnlimboTakesTheFirstOccupiedSlotWhateverItsExpiry()
	{
		var vm = Load( new Asm()
			.I( Opcode.LIMBO, 11, 100 ).I( Opcode.LIMBO, 12, 100 )
			.I( Opcode.FORCEUNLIMBO, V( 0 ) ).I( Opcode.BRANCH_NZ, "first" ).I( Opcode.COPY, V( 3 ), 99 )
			.Label( "first" ).I( Opcode.FORCEUNLIMBO, V( 1 ) ).I( Opcode.FORCEUNLIMBO, V( 2 ) ).I( Opcode.BRANCH_Z, "empty" ).I( Opcode.COPY, V( 3 ), 98 )
			.Label( "empty" ).I( Opcode.ENDSLICE ) );
		Run( vm, 1 );
		CollectionAssert.AreEqual( new[] { 11, 12, 0, 0 }, vm.Variables, "slot order; the third call finds the table empty and writes 0" );
		Assert.AreEqual( 0, vm.LimboCount );
	}

	[TestMethod]
	public void ForceUnlimboWithALiteralDestinationDoesNothingAtAll()
	{
		var vm = Load( new Asm().I( Opcode.LIMBO, 11, 100 ).I( Opcode.TEST, V( 3 ) ).I( Opcode.FORCEUNLIMBO, 0 ).I( Opcode.ENDSLICE ) );
		Run( vm, 1 );
		Assert.AreEqual( 1, vm.LimboCount, "nothing was released (0x554FA6 returns before the scan)" );
		Assert.AreEqual( RideVM.VMFlags.Zero, vm.Flags, "and the flags of the TEST survived" );
	}

	[TestMethod]
	public void UnlimboWithALiteralDestinationOnlySetsTheFlags()
	{
		var vm = Load( new Asm().I( Opcode.LIMBO, 11, 0 ).I( Opcode.ENDSLICE ).I( Opcode.UNLIMBO, 0 ).I( Opcode.ENDSLICE ) );
		Run( vm, 1 );
		vm.Advance( 0.01 );
		Assert.AreEqual( 0, vm.LimboCount, "the expired (0 s) hold was released" );
		Assert.AreEqual( RideVM.VMFlags.None, vm.Flags, "its value 11 set the flags although a literal destination discards the write" );
	}

	// ---- snapshots and hashes ----

	private static byte[] SnapshotProgram() => new Asm().WithHeader( 4, 2 )
		.I( Opcode.HUSH, 5 ).I( Opcode.HUSH, 6 ).I( Opcode.LIMBO, 11, 3 ).I( Opcode.TURBO, 7 )
		.I( Opcode.JSR, "routine" )
		.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
		.Label( "routine" ).I( Opcode.WAIT, 500 ).I( Opcode.HOP, V( 0 ) ).I( Opcode.RETURN ).Build( Names );

	private static RideVM LoadSnapshotProgram() => new( new RideScriptFile( new MemoryStream( SnapshotProgram() ) ), new RideVMOptions { Seed = 1 } );

	private static ulong Hash( RideVM vm )
	{
		var hash = new StateHasher();
		vm.AddCanonicalState( hash );
		return hash.Value;
	}

	[TestMethod]
	public void HeapLimboAndTurboSurviveTheSnapshotAndTheFileCodec()
	{
		var vm = LoadSnapshotProgram();
		Run( vm, 3 );
		Assert.AreEqual( RideVMState.Waiting, vm.State );
		Assert.AreEqual( 2, vm.HeapCount );
		Assert.AreEqual( 1, vm.CallDepth );
		var snapshot = ParkResumeJson.Deserialize<RideVMSnapshot>( ParkResumeJson.Serialize( vm.CaptureSnapshot() ) );
		snapshot.ValidateSnapshot();

		var restored = LoadSnapshotProgram();
		restored.RestoreSnapshot( snapshot );
		Assert.AreEqual( Hash( vm ), Hash( restored ) );
		Assert.AreEqual( vm.HeapCount, restored.HeapCount );
		Assert.AreEqual( vm.CallDepth, restored.CallDepth );
		Assert.AreEqual( vm.LimboCount, restored.LimboCount );
		Assert.AreEqual( vm.TurboByte, restored.TurboByte );
		for ( var index = 0; index < vm.StackCapacity; index++ )
			Assert.AreEqual( vm.GetStackSlot( index ), restored.GetStackSlot( index ), $"stack slot {index}" );
		Assert.AreEqual( vm.GetLimboSlot( 0 ), restored.GetLimboSlot( 0 ) );

		for ( var tick = 0; tick < 60; tick++ )
		{
			vm.Advance( Tick );
			restored.Advance( Tick );
			Assert.AreEqual( Hash( vm ), Hash( restored ), $"diverged at tick {tick}" );
		}
		Assert.AreEqual( 6, restored.Variables[0], "the HOP after the wait took the newest HUSH value (6)" );
		Assert.AreEqual( 1, restored.HeapCount );
	}

	[TestMethod]
	public void ASnapshotWithoutTheSharedArrayRestoresItsOldCallStack()
	{
		var vm = LoadSnapshotProgram();
		Run( vm, 3 );
		var legacy = vm.CaptureSnapshot() with { Stack = null, StackPointer = 0, HeapCount = 0, LimboValues = null, LimboExpiries = null, LimboCount = 0, CallStack = new[] { 6 } };
		legacy.ValidateSnapshot();
		var restored = LoadSnapshotProgram();
		restored.RestoreSnapshot( legacy );
		Assert.AreEqual( 1, restored.CallDepth );
		Assert.AreEqual( 0, restored.HeapCount );
		Assert.AreEqual( 0, restored.LimboCount );
		Assert.AreEqual( 0x2000000C, restored.GetStackSlot( restored.StackCapacity - 1 ), "the old instruction index 6 went to the top of the array as its code-word offset 12, tagged" );
	}

	[TestMethod]
	public void TheSnapshotValidationRefusesInconsistentHeapLimboAndTurboState()
	{
		var vm = LoadSnapshotProgram();
		Run( vm, 3 );
		var snapshot = vm.CaptureSnapshot();
		Assert.ThrowsException<InvalidDataException>( () => ( snapshot with { StackPointer = 9 } ).ValidateSnapshot() );
		Assert.ThrowsException<InvalidDataException>( () => ( snapshot with { HeapCount = 5 } ).ValidateSnapshot() );
		Assert.ThrowsException<InvalidDataException>( () => ( snapshot with { TurboByte = 300 } ).ValidateSnapshot() );
		Assert.ThrowsException<InvalidDataException>( () => ( snapshot with { LimboExpiries = new[] { 1 } } ).ValidateSnapshot() );
		Assert.ThrowsException<InvalidDataException>( () => ( snapshot with { Stack = null, CallStack = null } ).ValidateSnapshot() );
		var other = new RideVM( new RideScriptFile( new MemoryStream( new Asm().WithHeader( 2, 2 ).I( Opcode.ENDSLICE ).Build( Names ) ) ), new RideVMOptions { Seed = 1 } );
		Assert.ThrowsException<InvalidDataException>( () => other.RestoreSnapshot( snapshot ), "a stack array of another size does not fit" );
	}

	[TestMethod]
	public void ReturnWordsAreCodeWordOffsetsAndMapBackToInstructions()
	{
		// JSR at code words 0-1, ADD V0 at 2-4: the return word is 2 | 0x20000000; a POP reads it and a PUSH of it makes RETURN jump back to the ADD.
		var vm = Load( new Asm()
			.I( Opcode.JSR, "routine" )
			.I( Opcode.ADD, V( 0 ), 1 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Label( "routine" ).I( Opcode.POP, V( 1 ) ).I( Opcode.PUSH, V( 1 ) ).I( Opcode.RETURN ) );
		Run( vm, 1 );
		Assert.AreEqual( 0x20000002, vm.Variables[1] );
		Assert.AreEqual( 1, vm.Variables[0], "RETURN mapped the word back to the ADD instruction" );
		var bad = Load( new Asm().I( Opcode.JSR, "routine" ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Label( "routine" ).I( Opcode.POP, V( 1 ) ).I( Opcode.ADD, V( 1 ), 2 ).I( Opcode.PUSH, V( 1 ) ).I( Opcode.RETURN ) );
		Run( bad, 1 );
		Assert.AreEqual( RideVMState.Faulted, bad.State, "a return word that is not the start of an instruction faults" );
	}

	[TestMethod]
	public void TheHashSeesEveryRawStackWord()
	{
		// PUSH 5 and PUSH 0x20000005 differ only in the return tag bit; they must not hash alike.
		var plain = Load( new Asm().I( Opcode.PUSH, V( 0 ) ).I( Opcode.COPY, V( 0 ), 7 ).I( Opcode.ENDSLICE ) );
		plain.Variables[0] = 5;
		var tagged = Load( new Asm().I( Opcode.PUSH, V( 0 ) ).I( Opcode.COPY, V( 0 ), 7 ).I( Opcode.ENDSLICE ) );
		tagged.Variables[0] = 0x20000005;
		Run( plain, 1 );
		Run( tagged, 1 );
		Assert.AreNotEqual( Hash( plain ), Hash( tagged ) );
	}

	[TestMethod]
	public void AScriptThatUsesNoneOfItKeepsItsEarlierHash()
	{
		// The call region is hashed as before; heap, limbo and TURBO add to the digest only once used.
		var plain = Load( new Asm().I( Opcode.ENDSLICE ) );
		Run( plain, 1 );
		var used = Load( new Asm().I( Opcode.HUSH, 3 ).I( Opcode.ENDSLICE ) );
		Run( used, 1 );
		Assert.AreNotEqual( Hash( plain ), Hash( used ) );
		var limbo = Load( new Asm().I( Opcode.LIMBO, 3, 1 ).I( Opcode.ENDSLICE ) );
		Run( limbo, 1 );
		Assert.AreNotEqual( Hash( plain ), Hash( limbo ) );
		var turbo = Load( new Asm().I( Opcode.TURBO, 1 ).I( Opcode.ENDSLICE ) );
		Run( turbo, 1 );
		Assert.AreNotEqual( Hash( plain ), Hash( turbo ) );
	}

	/// <summary>The float state of WALKST_FLOAT, WALKFLOATSTAT and WALKFLOATSTOP (tp.exe 0x555B72, 0x555BD8, 0x555C11, the frame update's clearing at 0x558051..0x558077).</summary>
	[TestMethod]
	public void TheWalkFloatRunsForTheSecondsOfWalkStFloatAndStopsReportingAfterThem()
	{
		// ZeroG: `WALKST_FLOAT VAR_DURATION 1000 1000`, then a loop over `WALKFLOATSTAT 0; BRANCH_NZ` with a wait in it; V1 counts the passes
		var vm = Load( new Asm()
			.I( Opcode.WALKST_FLOAT, 3, 1000, 1000 )
			.Label( "loop" ).I( Opcode.ADD, V( 1 ), 1 ).I( Opcode.WAIT, 250 ).I( Opcode.WALKFLOATSTAT, 0 ).I( Opcode.BRANCH_NZ, "loop" )
			.I( Opcode.COPY, V( 0 ), 1 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ) );
		Run( vm, 60 );
		Assert.AreEqual( 0, vm.Variables[0], "the float is still running one second in" );
		Run( vm, 60 * 4 );
		Assert.AreEqual( 1, vm.Variables[0], "the loop ended after the three seconds" );
		Assert.IsTrue( vm.Variables[1] is >= 12 and <= 14, $"about 12 passes of 250 ms, got {vm.Variables[1]}" );
		Assert.AreEqual( (0, 0), (vm.WalkFloat.Duration, 0), "the duration word was cleared" );
	}

	[TestMethod]
	public void WalkFloatStatSetsTheFlagsAndAVariableDestinationTakesTheDuration()
	{
		var vm = Load( new Asm().I( Opcode.WALKFLOATSTAT, V( 0 ) ).I( Opcode.WALKST_FLOAT, 2, 5, 6 ).I( Opcode.WALKFLOATSTAT, V( 1 ) ).I( Opcode.WALKFLOATSTAT, 0 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ) );
		Run( vm, 1 );
		Assert.AreEqual( 0, vm.Variables[0], "no float yet" );
		Assert.AreEqual( 2000, vm.Variables[1] );
		Assert.AreEqual( 0, (int)(vm.Flags & RideVM.VMFlags.Zero), "a literal destination only sets the flags (the duration is not zero)" );
		Assert.AreEqual( (0u, 2000, 5 | (6 << 16)), vm.WalkFloat with { Start = 0 }, "the two operands are kept as words" );
	}

	[TestMethod]
	public void WalkFloatStopTurnsTheTimeSpentIntoAThousandthShareAndLeavesOneSecondToSettle()
	{
		// 4 s float, stopped after 1 s: spent = 250 thousandths, so the start moves back 250 ms from now and the duration becomes 1000: the word stays set for the remaining 750 ms
		var vm = Load( new Asm().I( Opcode.WALKST_FLOAT, 4, 0, 0 ).I( Opcode.WAIT, 1000 ).I( Opcode.WALKFLOATSTOP ).I( Opcode.WALKFLOATSTAT, V( 0 ) ).I( Opcode.WAIT, 500 ).I( Opcode.WALKFLOATSTAT, V( 1 ) )
			.I( Opcode.WAIT, 500 ).I( Opcode.WALKFLOATSTAT, V( 2 ) ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ) );
		Run( vm, 60 * 3 );
		Assert.AreEqual( 1000, vm.Variables[0], "after the stop the duration word is 1000" );
		Assert.AreEqual( 1000, vm.Variables[1], "still set 500 ms later" );
		Assert.AreEqual( 0, vm.Variables[2], "gone 1000 ms after the stop minus the share already spent" );
	}

	[TestMethod]
	public void WalkFloatStopWithoutAFloatDoesNothingAndTheStateRoundTripsThroughASnapshot()
	{
		var vm = Load( new Asm().I( Opcode.TEST, V( 0 ) ).I( Opcode.WALKFLOATSTOP ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ) );
		Run( vm, 1 );
		Assert.AreEqual( RideVM.VMFlags.Zero, vm.Flags, "no flags" );
		Assert.AreEqual( (0u, 0, 0), vm.WalkFloat );
		var floating = Load( new Asm().I( Opcode.WALKST_FLOAT, 7, 11, 13 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ) );
		Run( floating, 2 );
		var snapshot = floating.CaptureSnapshot();
		Assert.AreEqual( 7000, snapshot.WalkFloatDuration );
		var other = Load( new Asm().I( Opcode.WALKST_FLOAT, 7, 11, 13 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ) );
		other.RestoreSnapshot( ParkResumeJson.Deserialize<RideVMSnapshot>( ParkResumeJson.Serialize( snapshot ) ) );
		Assert.AreEqual( floating.WalkFloat, other.WalkFloat );
		var (hashA, hashB) = (new StateHasher(), new StateHasher());
		floating.AddCanonicalState( hashA );
		other.AddCanonicalState( hashB );
		Assert.AreEqual( hashA.Value, hashB.Value );
		Assert.IsFalse( System.Text.Encoding.UTF8.GetString( ParkResumeJson.Serialize( vm.CaptureSnapshot() ) ).Contains( "WalkFloat" ), "a script without a float writes no member" );
	}

	[TestMethod]
	public void EndSetsTheProgramCounterToMinusTenThousandAndEndsTheScriptWithoutAFault()
	{
		// tp.exe 0x5567B1: mov [esi + 0x3c], 0xFFFFD8F0 and nothing else
		var vm = Load( new Asm().I( Opcode.COPY, V( 0 ), 1 ).I( Opcode.END ).I( Opcode.COPY, V( 1 ), 1 ) );
		Run( vm, 3 );
		Assert.AreEqual( RideVMState.Halted, vm.State );
		Assert.IsNull( vm.FaultMessage );
		Assert.AreEqual( (1, 0), (vm.Variables[0], vm.Variables[1]), "nothing after END runs" );
		Assert.AreEqual( -10000, vm.ProgramCounter );
	}
}
