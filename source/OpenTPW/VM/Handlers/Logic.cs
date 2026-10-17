namespace OpenTPW;

public static partial class OpcodeHandlers
{
	/// <summary>
	/// Control flow. Branch operands are code-word indices resolved to instructions at load time.
	/// Flags: Zero = last result was 0, Sign = last result was negative.
	/// </summary>
	public static class Logic
	{
		[OpcodeHandler( Opcode.NOP, RideOpcodeStatus.Implemented, "docs" )]
		public static void NoOp( RideVM vm )
		{
		}

		[OpcodeHandler( Opcode.BRANCH, RideOpcodeStatus.Implemented, "docs; all 725 operands are branch words" )]
		public static void Branch( RideVM vm, Operand location ) => vm.BranchTo( location );

		[OpcodeHandler( Opcode.BRANCH_Z, RideOpcodeStatus.Implemented, "docs (zero flag set)" )]
		public static void BranchZero( RideVM vm, Operand location )
		{
			if ( vm.Flags.HasFlag( RideVM.VMFlags.Zero ) )
				vm.BranchTo( location );
		}

		[OpcodeHandler( Opcode.BRANCH_NZ, RideOpcodeStatus.Implemented, "docs (zero flag clear)" )]
		public static void BranchNotZero( RideVM vm, Operand location )
		{
			if ( !vm.Flags.HasFlag( RideVM.VMFlags.Zero ) )
				vm.BranchTo( location );
		}

		[OpcodeHandler( Opcode.BRANCH_NV, RideOpcodeStatus.Implemented, "docs (negative); corpus: `SUB VAR_TEMP VAR_STARTNOW VAR_TEMP; BRANCH_NV` passenger time-outs" )]
		public static void BranchNegativeValue( RideVM vm, Operand location )
		{
			if ( vm.Flags.HasFlag( RideVM.VMFlags.Sign ) )
				vm.BranchTo( location );
		}

		[OpcodeHandler( Opcode.BRANCH_PV, RideOpcodeStatus.Implemented, "docs (positive); corpus: strictly positive, because 15× `BRANCH_PV x; BRANCH_Z x` and `CMP VAR_TEMP 4; BRANCH_PV; BRANCH_NZ` would otherwise be dead code" )]
		public static void BranchPositiveValue( RideVM vm, Operand location )
		{
			if ( !vm.Flags.HasFlag( RideVM.VMFlags.Sign ) && !vm.Flags.HasFlag( RideVM.VMFlags.Zero ) )
				vm.BranchTo( location );
		}

		[OpcodeHandler( Opcode.JSR, RideOpcodeStatus.Implemented, "docs; TPW-EXE 0x5539A9: the return address goes onto the script's shared stack/heap array (header stack size, top-down); a full array still jumps, without pushing; corpus: every script using JSR declares stack ≥ 3" )]
		public static void JumpSubRoutine( RideVM vm, Operand location ) => vm.PushReturn( location );

		[OpcodeHandler( Opcode.RETURN, RideOpcodeStatus.Implemented, "docs; TPW-EXE 0x553A32: pops the shared array LIFO; only an address-tagged word is jumped to; an empty stack kills the script" )]
		public static void Return( RideVM vm ) => vm.PopReturn();

		[OpcodeHandler( Opcode.PUSH, RideOpcodeStatus.Implemented, "TPW-EXE 0x553C1E: pushes the resolved value onto the JSR array (top-down, no collision check) and sets the flags; a full array kills the script; no corpus use" )]
		public static void Push( RideVM vm, Operand value )
		{
			if ( !vm.PushValue( value.Value ) )
				vm.Abort( "PUSH onto a full stack" );
		}

		[OpcodeHandler( Opcode.POP, RideOpcodeStatus.Implemented, "TPW-EXE 0x553CBA: pops the JSR array into the flags and a variable destination; an empty array writes 0 and kills the script; no corpus use" )]
		public static void Pop( RideVM vm, Operand dest )
		{
			var popped = vm.PopValue( out var value );
			vm.SetFlags( value );
			dest.Value = value;
			if ( !popped )
				vm.Abort( "POP from an empty stack" );
		}

		[OpcodeHandler( Opcode.SETLV, RideOpcodeStatus.Implemented, "TPW-EXE 0x551E19: sets the flags from the raw operand word (not resolved: a variable operand counts as its tag word, a literal as its unsigned 16 bits); no corpus use" )]
		public static void SetLastValue( RideVM vm, Operand value ) => vm.SetFlags( (int)value.Kind << 16 | value.Raw );

		[OpcodeHandler( Opcode.DBGMSG, RideOpcodeStatus.Implemented, "TPW-EXE 0x554243: skips its operand and does nothing; no corpus use" )]
		public static void DebugMessage( RideVM vm, Operand message )
		{
		}
	}
}
