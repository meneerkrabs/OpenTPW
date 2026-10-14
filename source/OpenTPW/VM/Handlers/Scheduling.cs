namespace OpenTPW;

public static partial class OpcodeHandlers
{
	/// <summary>
	/// Slicing, waiting and script clocks. Units are milliseconds (WAIT 500…5300 around ride animations,
	/// `GETTIME; ADD VAR_STARTNOW 10000` passenger time-outs); not verified against the original engine.
	/// </summary>
	public static class Scheduling
	{
		[OpcodeHandler( Opcode.CRIT_LOCK, RideOpcodeStatus.Implemented, "docs (locks the ride); VM: the slice budget cannot preempt inside CRIT_LOCK…CRIT_UNLOCK, hosts must not change visitor variables while `InCriticalSection`" )]
		public static void CritLock( RideVM vm ) => vm.InCriticalSection = true;

		[OpcodeHandler( Opcode.CRIT_UNLOCK, RideOpcodeStatus.Implemented, "undocumented upstream; corpus: closes every CRIT_LOCK section (240 uses, 0 operands)" )]
		public static void CritUnlock( RideVM vm ) => vm.InCriticalSection = false;

		[OpcodeHandler( Opcode.ENDSLICE, RideOpcodeStatus.Implemented, "name; corpus: sits in idle loops (`ENDSLICE; TEST VAR_RIDECLOSED; BRANCH_NZ`), so it yields until the next slice; flags survive it" )]
		public static void EndSlice( RideVM vm ) => vm.EndSlice();

		// [BIN:STP-PPC:0xb0658 WAIT] the first encounter stores now + trunc(operand / script speed) as the deadline (0xb06f8) and yields; later slices resume once now >= deadline (docs/reverse/BOARD-plan.md). The division is taken in single precision like the triggers' (TriggerDelay)
		[OpcodeHandler( Opcode.WAIT, RideOpcodeStatus.Implemented, "STP-PPC 0xb0658: milliseconds divided by the script speed (0.5 + SPEED/100), truncated; flags survive it (`WAIT 300; BRANCH_NZ`)" )]
		public static void Wait( RideVM vm, Operand time ) => vm.WaitFor( RideAnimationTiming.TriggerDelay( time.Value, vm.ScriptSpeed ) );

		// [BIN:TPW-EXE:0x00553828 WAITABS] with the deadline word (+0xA0) at 0 the handler stores clock (0x402D70 → 0x4031E0) + the operand (a variable's value, or the literal sign-extended from 16 bits) as a 32-bit word, with no script-speed division (0x5538A4–0x5538CB), rewinds the program counter by 2 and yields; with a deadline set it waits while now is unsigned-below it (jb at 0x55386D), else clears the word to 0 and continues. A deadline that wraps to exactly 0 reads as not armed and arms again
		// [BIN:STP-PPC:0xb0710 WAITABS] the Mac handler stores now + operand in the same word and compares unsigned
		[OpcodeHandler( Opcode.WAITABS, RideOpcodeStatus.Implemented, "tp.exe 0x553828 and STP-PPC 0xb0710: despite the name the first encounter stores the 32-bit clock + operand (no WAIT speed division) in the shared deadline word and yields; later encounters wait while the clock is unsigned-below it; a wrapped deadline is already past and a deadline of exactly 0 arms again (Theme Park Inc corpus: 34 uses, literals 1..10000)" )]
		public static void WaitAbsolute( RideVM vm, Operand time )
		{
			var wait = unchecked((uint)time.Value);
			var now = vm.ClockMilliseconds32;
			var deadline = unchecked(now + wait);
			if ( deadline == 0 )
			{
				// The word reads as "not armed": the next execution stores again.
				vm.RepeatInstruction();
				vm.EndSlice();
				return;
			}
			// The first encounter always yields; a deadline that is not ahead of the clock (a wrapped sum, or an operand of 0) is passed at the next servicing pass.
			if ( deadline <= now )
				vm.WaitUntil( vm.TimeMilliseconds );
			else
			{
				// The original resumes once the integer clock word reaches the deadline, so the wake is when the scheduler clock reaches it (not the arming time's fraction plus the wait).
				// [APPROX:VM-901] the clock is this VM's non-wrapping double, so a wait that spans the 2^32 ms wrap of the original's clock word (about 49.7 days of scheduler clock) is not modelled — evidence needed: none (the rule is traced, tp.exe 0x553828); the port waits for the wrap-free case
				vm.WaitUntil( vm.TimeMilliseconds + ( deadline - vm.SchedulerMilliseconds ) );
			}
		}

		[OpcodeHandler( Opcode.GETTIME, RideOpcodeStatus.Implemented, "STP-PPC 0xaf6d8: the shared scheduler clock's 32-bit millisecond word, stored unchanged (no creation timestamp subtracted)" )]
		public static void GetTime( RideVM vm, Operand dest ) => dest.Value = unchecked((int)vm.ClockMilliseconds32);

		[OpcodeHandler( Opcode.SETTIMER, RideOpcodeStatus.Implemented, "corpus: `SETTIMER 10000 … GETTIMER x; BRANCH_Z start` replaces the GETTIME time-out idiom; countdown in ms" )]
		public static void SetTimer( RideVM vm, Operand time ) => vm.SetTimer( time.Value );

		[OpcodeHandler( Opcode.GETTIMER, RideOpcodeStatus.Implemented, "corpus: `GETTIMER 0; BRANCH_NZ skip` staggers unloading; dest = remaining ms (≥ 0), sets flags" )]
		public static void GetTimer( RideVM vm, Operand dest )
		{
			var remaining = vm.TimerRemaining;
			dest.Value = remaining;
			vm.SetFlags( remaining );
		}

		[OpcodeHandler( Opcode.NAME, RideOpcodeStatus.Implemented, "docs; the name is what FINDSCRIPTRAND searches for" )]
		public static void Name( RideVM vm, Operand newName ) => vm.ScriptName = newName.Text;
	}
}
