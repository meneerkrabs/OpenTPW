namespace OpenTPW;

public static partial class OpcodeHandlers
{
	/// <summary>
	/// Arithmetic. The corpus shows the destination first for SUB/DIV/MOD (`SUB VAR_TEMP VAR_TEMP 3000; WAIT VAR_TEMP`),
	/// unlike the upstream signature, and a literal destination (usually 0) when only flags are wanted.
	/// </summary>
	public static class Math
	{
		[OpcodeHandler( Opcode.COPY, RideOpcodeStatus.Implemented, "docs; no flags (no branch in the corpus consumes flags after COPY)" )]
		public static void Copy( RideVM vm, Operand dest, Operand source ) => dest.Value = source.Value;

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

		[OpcodeHandler( Opcode.DIV, RideOpcodeStatus.Implemented, "docs; corpus: `DIV VAR_TEMP VAR_CAPACITY 9` + `MOD 0 VAR_CAPACITY 9` round-up idiom gives `DIV dest a b`; truncating, ÷0 faults" )]
		public static void Div( RideVM vm, Operand dest, Operand valueA, Operand valueB )
		{
			var divisor = valueB.Value;
			if ( divisor == 0 )
				throw new RideScriptException( "division by zero" );
			var result = divisor == -1 ? unchecked(-valueA.Value) : valueA.Value / divisor;
			dest.Value = result;
			vm.SetFlags( result );
		}

		[OpcodeHandler( Opcode.MOD, RideOpcodeStatus.Implemented, "docs; corpus: `MOD 0 VAR_CAPACITY 9; BRANCH_Z` and clock `MOD VAR_TEMP VAR_TEMP 12; BRANCH_NZ`; ÷0 faults" )]
		public static void Mod( RideVM vm, Operand dest, Operand valueA, Operand valueB )
		{
			var divisor = valueB.Value;
			if ( divisor == 0 )
				throw new RideScriptException( "modulo by zero" );
			var result = divisor == -1 ? 0 : valueA.Value % divisor;
			dest.Value = result;
			vm.SetFlags( result );
		}

		[OpcodeHandler( Opcode.TEST, RideOpcodeStatus.Implemented, "docs" )]
		public static void Test( RideVM vm, Operand value ) => vm.SetFlags( value.Value );

		[OpcodeHandler( Opcode.CMP, RideOpcodeStatus.Implemented, "docs say bitwise AND; corpus needs subtraction (a − b): `RAND VAR_TEMP 10; CMP VAR_TEMP 4; BRANCH_PV; BRANCH_NZ`, `CMP VAR_CAPACITY VAR_CARS; BRANCH_Z`" )]
		public static void Compare( RideVM vm, Operand valueA, Operand valueB ) => vm.SetFlags( unchecked(valueA.Value - valueB.Value) );

		[OpcodeHandler( Opcode.RAND, RideOpcodeStatus.Implemented, "docs (`RAND dest max`); corpus: inclusive 0..max (Totem `RAND VAR_TEMP 2` selects one of three animation sets; `RAND 0 1; BRANCH_Z` coin flips) and sets flags" )]
		public static void Random( RideVM vm, Operand dest, Operand maxValue )
		{
			var result = vm.NextRandom( maxValue.Value );
			dest.Value = result;
			vm.SetFlags( result );
		}
	}
}
