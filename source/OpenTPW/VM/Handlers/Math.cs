namespace OpenTPW;

public static partial class OpcodeHandlers
{
	/// <summary>
	/// Arithmetic. The corpus shows the destination first for SUB/DIV/MOD (`SUB VAR_TEMP VAR_TEMP 3000; WAIT VAR_TEMP`),
	/// unlike the upstream signature, and a literal destination (usually 0) when only flags are wanted.
	/// </summary>
	public static class Math
	{
		[OpcodeHandler( Opcode.COPY, RideOpcodeStatus.Implemented, "Mac-static: variable destination receives the resolved source and sets flags; non-variable destination aborts with PC -10000" )]
		public static void Copy( RideVM vm, Operand dest, Operand source )
		{
			if ( !dest.IsVariable )
			{
				vm.Abort( $"COPY requires a variable destination, found {dest.Kind}" );
				return;
			}
			var result = source.Value;
			dest.Value = result;
			vm.SetFlags( result );
		}

		[OpcodeHandler( Opcode.ADD, RideOpcodeStatus.Implemented, "docs (dest += value, sets flags); corpus: 115 branches consume ADD flags, e.g. `ADD VAR_SPACELEFT 0xFFFF; BRANCH_Z`" )]
		public static void Add( RideVM vm, Operand dest, Operand value )
		{
			var result = unchecked(dest.Value + value.Value);
			dest.Value = result;
			vm.SetFlags( result );
		}

		[OpcodeHandler( Opcode.SUB, RideOpcodeStatus.Implemented, "docs (sets flags); corpus: operand order is `SUB dest a b` → dest = a − b" )]
		public static void Sub( RideVM vm, Operand dest, Operand valueA, Operand valueB )
		{
			var result = unchecked(valueA.Value - valueB.Value);
			dest.Value = result;
			vm.SetFlags( result );
		}

		[OpcodeHandler( Opcode.DIV, RideOpcodeStatus.Implemented, "corpus: `DIV dest a b`; Mac-static: signed truncation, zero divisor returns 0; signed overflow retains VM wrap policy" )]
		public static void Div( RideVM vm, Operand dest, Operand valueA, Operand valueB )
		{
			var divisor = valueB.Value;
			var result = divisor == 0 ? 0 : divisor == -1 ? unchecked(-valueA.Value) : valueA.Value / divisor;
			dest.Value = result;
			vm.SetFlags( result );
		}

		[OpcodeHandler( Opcode.MOD, RideOpcodeStatus.Implemented, "corpus: `MOD dest a b`; Mac-static: signed remainder, zero divisor returns 0; signed overflow retains VM zero policy" )]
		public static void Mod( RideVM vm, Operand dest, Operand valueA, Operand valueB )
		{
			var divisor = valueB.Value;
			var result = divisor is 0 or -1 ? 0 : valueA.Value % divisor;
			dest.Value = result;
			vm.SetFlags( result );
		}

		[OpcodeHandler( Opcode.TEST, RideOpcodeStatus.Implemented, "docs" )]
		public static void Test( RideVM vm, Operand value ) => vm.SetFlags( value.Value );

		[OpcodeHandler( Opcode.CMP, RideOpcodeStatus.Implemented, "docs say bitwise AND; corpus needs subtraction (a − b): `RAND VAR_TEMP 10; CMP VAR_TEMP 4; BRANCH_PV; BRANCH_NZ`, `CMP VAR_CAPACITY VAR_CARS; BRANCH_Z`" )]
		public static void Compare( RideVM vm, Operand valueA, Operand valueB ) => vm.SetFlags( unchecked(valueA.Value - valueB.Value) );

		[OpcodeHandler( Opcode.RAND, RideOpcodeStatus.Implemented, "Mac-static: signed raw bound word, inclusive nonnegative maximum, sets flags; host PRNG remains unqualified" )]
		public static void Random( RideVM vm, Operand dest, Operand maxValue )
		{
			var result = vm.NextRandom( (short)maxValue.Raw );
			dest.Value = result;
			vm.SetFlags( result );
		}
	}
}
