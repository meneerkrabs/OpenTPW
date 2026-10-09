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

		[OpcodeHandler( Opcode.JSR, RideOpcodeStatus.Implemented, "docs; corpus: every script using JSR declares stack ≥ 3, used as the return-address limit" )]
		public static void JumpSubRoutine( RideVM vm, Operand location ) => vm.PushReturn( location );

		[OpcodeHandler( Opcode.RETURN, RideOpcodeStatus.Implemented, "docs; LIFO return stack (the old VM used a FIFO queue)" )]
		public static void Return( RideVM vm ) => vm.PopReturn();
	}
}
