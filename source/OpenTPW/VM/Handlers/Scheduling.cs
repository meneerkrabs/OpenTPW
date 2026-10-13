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

		[OpcodeHandler( Opcode.WAIT, RideOpcodeStatus.Implemented, "docs; milliseconds (inferred); flags survive it (`WAIT 300; BRANCH_NZ`)" )]
		public static void Wait( RideVM vm, Operand time ) => vm.WaitFor( time.Value );

		// [APPROX:VM-901] The traced facts are STP-PPC 0xb0710–0xb0798: WAITABS stores now + operand as a 32-bit deadline word in the field WAIT uses (no script-speed division), its first encounter always yields, and later encounters wait while unsigned now < deadline. The port models that deadline in its non-wrapping double script clock, so the original's unsigned comparison, its 2^32 wrap and its zero-deadline re-arm sentinel (docs/reverse/PPC-clock.md) are not modelled; a session that never nears 2^31 ms of scheduler clock cannot observe the difference.
		[OpcodeHandler( Opcode.WAITABS, RideOpcodeStatus.Implemented, "STP-PPC 0xb0710: despite the name the first encounter stores now + operand (no WAIT speed division) in the shared deadline field and yields; later encounters resume once the clock passes it (Theme Park Inc corpus: 34 uses, literals 1..10000; VM-901)" )]
		public static void WaitAbsolute( RideVM vm, Operand time ) => vm.WaitForUnscaled( time.Value );

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
