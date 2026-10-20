namespace OpenTPW;

public enum StaffSpriteTickStatus { NotDue, Yielded, InstructionLimit, Stopped, Ended, RemovalDue, Invalid }
public enum StaffSpriteVmError { None, UnsupportedProgramTable, UnsupportedPc, InvalidRunState, InvalidStackPointer, InvalidRegister, UnsupportedInstruction, MissingEndIf, UnsupportedStackPc, NonFiniteArithmetic }
[Flags]
public enum StaffSpriteVmDiagnostic { None = 0, IfDepthUnderflow = 1, WhileWithoutDo = 2, FullStack = 4, EmptyStack = 8 }
public readonly record struct StaffSpriteTickResult( StaffSpriteTickStatus Status, int Instructions = 0, StaffSpriteVmError Error = StaffSpriteVmError.None, StaffSpriteVmDiagnostic Diagnostics = StaffSpriteVmDiagnostic.None );

/// <summary>
/// Bounded staff control semantics. Tick accepts one signed clock sample and never decides scheduler ordering.
/// This independently testable core is not yet consumed by gameplay, world rendering, windows or saves.
/// </summary>
public static class StaffSpriteVm
{
	public const int InstructionLimit = 50;

	/// <summary>
	/// Fresh control/clock state. Resource metadata and pose are supplied separately by the future source owner.
	/// Zeroed unused stack, scratch, last-PC and cache words are a host safety choice, not native allocation-byte parity.
	/// </summary>
	public static StaffSpriteVmState Create( StaffSpriteProgram program, int now )
	{
		ValidateEntry( program );
		var state = new StaffSpriteVmState
		{
			CurrentPc = new StaffSpritePc( program, 0 ), RestartPc = new StaffSpritePc( program, 0 ),
			Due = unchecked( now + 62 )
		};
		// [BIN:TPW-EXE:0x004758F0 sprite construction] alpha=255, primary/secondary scale=1; caller supplies pose/resource words
		state[StaffSpriteRegister.Alpha] = 255;
		state[StaffSpriteRegister.PrimaryScale] = BitConverter.SingleToUInt32Bits( 1f );
		state[StaffSpriteRegister.SecondaryScale] = BitConverter.SingleToUInt32Bits( 1f );
		return state;
	}

	/// <summary>Replaces the requested program, retaining output, run state, clock, all register words and inactive stack words.</summary>
	// [BIN:TPW-EXE:0x00475B80 template replacement] reset current/restart PC, stack pointer and three depths only
	public static void ApplyTemplate( StaffSpriteVmState state, StaffSpriteProgram program )
	{
		ArgumentNullException.ThrowIfNull( state );
		ValidateEntry( program );
		state.CurrentPc = state.RestartPc = new StaffSpritePc( program, 0 );
		state.StackPointer = StaffSpriteVmState.StackCapacity;
		state.IfDepth = state.CallDepth = state.LoopDepth = 0;
	}

	/// <summary>Applies already resolved bank metadata; intro and hold remain the existing words, as in template replacement.</summary>
	public static void ApplyPerformance( StaffSpriteVmState state, int program, int slot, int frameCount )
	{
		ArgumentNullException.ThrowIfNull( state );
		if ( program is < 0 or > 3 || slot < 0 || frameCount <= 0 )
			throw new ArgumentOutOfRangeException( nameof( program ), "Performance metadata must identify a supported program, slot and positive frame count." );
		ApplyTemplate( state, (StaffSpriteProgram)((int)StaffSpriteProgram.Performance0 + program) );
		state[StaffSpriteRegister.Slot] = (uint)slot;
		state[StaffSpriteRegister.FrameCount] = (uint)frameCount;
	}

	/// <summary>Wrapper request semantics: zero means no change, and a new interval does not rebase a pending deadline.</summary>
	public static void ApplyInterval( StaffSpriteVmState state, int interval )
	{
		ArgumentNullException.ThrowIfNull( state );
		if ( interval != 0 )
			state.Interval = interval;
	}

	public static StaffSpriteTickResult Tick( StaffSpriteVmState state, int now ) => Tick( state, now, StaffSpritePrograms.Builtin );

	// [BIN:TPW-EXE:0x00475360 sprite opportunity] direct signed now > due, unchecked addition, no catch-up; ended state is removed only when due
	internal static StaffSpriteTickResult Tick( StaffSpriteVmState state, int now, StaffSpriteProgramSet programs )
	{
		ArgumentNullException.ThrowIfNull( state );
		if ( now <= state.Due )
			return new( StaffSpriteTickStatus.NotDue );
		if ( state.RunState == StaffSpriteRunState.Ended )
			return new( StaffSpriteTickStatus.RemovalDue );
		state.Due = unchecked( now + state.Interval );
		if ( state.RunState == StaffSpriteRunState.Stopped )
			return new( StaffSpriteTickStatus.Stopped );
		if ( state.RunState is not (StaffSpriteRunState.Running or StaffSpriteRunState.Yielded) )
			return Invalid( state, 0, StaffSpriteVmError.InvalidRunState, StaffSpriteVmDiagnostic.None );
		if ( state.ProgramTable != StaffSpriteVmState.BuiltinProgramTable )
			return Invalid( state, 0, StaffSpriteVmError.UnsupportedProgramTable, StaffSpriteVmDiagnostic.None );
		if ( state.StackPointer is < 0 or > StaffSpriteVmState.StackCapacity )
			return Invalid( state, 0, StaffSpriteVmError.InvalidStackPointer, StaffSpriteVmDiagnostic.None );

		state.RunState = StaffSpriteRunState.Running;
		var diagnostics = StaffSpriteVmDiagnostic.None;
		// [BIN:TPW-EXE:0x00475010 bounded dispatch] record last PC before each handler; at most 50 handlers in one opportunity
		for ( var count = 0; count < InstructionLimit; count++ )
		{
			if ( !programs.TryRead( state.CurrentPc, out var instruction ) )
				return Invalid( state, count, StaffSpriteVmError.UnsupportedPc, diagnostics );
			state.LastPc = state.CurrentPc;
			state.CurrentPc = state.CurrentPc.Next;
			var error = Execute( state, instruction, programs, ref diagnostics );
			if ( error != StaffSpriteVmError.None )
			{
				// Host safety boundary: keep the offending semantic instruction identifiable and expose no plausible fallback frame.
				state.CurrentPc = state.LastPc;
				return Invalid( state, count + 1, error, diagnostics );
			}
			if ( state.RunState == StaffSpriteRunState.Ended )
				return new( StaffSpriteTickStatus.Ended, count + 1, Diagnostics: diagnostics );
			if ( state.RunState == StaffSpriteRunState.Yielded )
				return new( StaffSpriteTickStatus.Yielded, count + 1, Diagnostics: diagnostics );
		}
		return new( StaffSpriteTickStatus.InstructionLimit, InstructionLimit, Diagnostics: diagnostics );
	}

	private static StaffSpriteVmError Execute( StaffSpriteVmState state, StaffSpriteInstruction instruction, StaffSpriteProgramSet programs, ref StaffSpriteVmDiagnostic diagnostics )
	{
		var a = instruction.A;
		var b = instruction.B;
		switch ( instruction.Op )
		{
			case StaffSpriteOp.Set:
			case StaffSpriteOp.Add:
				if ( !IsRegister( a ) )
					return StaffSpriteVmError.InvalidRegister;
				// [BIN:TPW-EXE:0x004766D0 SET and ADD] integer operand converts to float for registers 1..6; other words wrap
				if ( IsFloatRegister( a ) )
				{
					var previous = BitConverter.UInt32BitsToSingle( state.Registers[a] );
					if ( instruction.Op == StaffSpriteOp.Add && !float.IsFinite( previous ) )
						return StaffSpriteVmError.NonFiniteArithmetic;
					var value = instruction.Op == StaffSpriteOp.Set ? (float)b : (float)((double)previous + b);
					state.Registers[a] = BitConverter.SingleToUInt32Bits( value );
				}
				else
					state.Registers[a] = instruction.Op == StaffSpriteOp.Set ? unchecked( (uint)b ) : unchecked( state.Registers[a] + (uint)b );
				break;
			case StaffSpriteOp.Copy:
				if ( !IsRegister( a ) || !IsRegister( b ) )
					return StaffSpriteVmError.InvalidRegister;
				state.Registers[a] = state.Registers[b];
				break;
			case StaffSpriteOp.Slot:
				state[StaffSpriteRegister.Slot] = unchecked( (uint)a ); // SLOT does not recompute FrameCount.
				break;
			case StaffSpriteOp.Frame:
				Emit( state, unchecked( (uint)a ) );
				break;
			case StaffSpriteOp.FrameRegister:
				if ( !IsRegister( a ) )
					return StaffSpriteVmError.InvalidRegister;
				Emit( state, state.Registers[a] );
				break;
			case StaffSpriteOp.Jump:
				if ( !programs.Contains( instruction.Target ) )
					return StaffSpriteVmError.UnsupportedPc;
				state.CurrentPc = instruction.Target;
				break;
			case StaffSpriteOp.IfGreaterLiteral:
				if ( !IsRegister( a ) )
					return StaffSpriteVmError.InvalidRegister;
				var left = IsFloatRegister( a ) ? (double)BitConverter.UInt32BitsToSingle( state.Registers[a] ) : unchecked( (int)state.Registers[a] );
				if ( !double.IsFinite( left ) )
					return StaffSpriteVmError.NonFiniteArithmetic;
				var end = default( StaffSpritePc );
				if ( left <= b && !programs.TryFindEndIf( state.CurrentPc, out end ) )
					return StaffSpriteVmError.MissingEndIf;
				state.IfDepth = unchecked( state.IfDepth + 1 );
				if ( left <= b )
					state.CurrentPc = end; // ENDIF still dispatches and decrements the depth, including on the false path.
				break;
			case StaffSpriteOp.EndIf:
				// The native diagnostic is a no-op: negative depth from a copied-PC hybrid is not repaired or rejected.
				state.IfDepth = unchecked( state.IfDepth - 1 );
				if ( state.IfDepth < 0 )
					diagnostics |= StaffSpriteVmDiagnostic.IfDepthUnderflow;
				break;
			case StaffSpriteOp.Do:
				Push( state, state.CurrentPc.Word, ref diagnostics );
				state.LoopDepth = unchecked( state.LoopDepth + 1 );
				break;
			case StaffSpriteOp.WhileNotEqualRegister:
			case StaffSpriteOp.WhileLessEqualRegister:
				// [BIN:TPW-EXE:0x004763D0 WHILE] depth <= 0 consumes operands but does not pop, compare, jump or repair depths.
				if ( state.LoopDepth <= 0 )
				{
					diagnostics |= StaffSpriteVmDiagnostic.WhileWithoutDo;
					break;
				}
				if ( !IsRegister( a ) || !IsRegister( b ) )
					return StaffSpriteVmError.InvalidRegister;
				var lhs = IsFloatRegister( a ) ? (double)BitConverter.UInt32BitsToSingle( state.Registers[a] ) : unchecked( (int)state.Registers[a] );
				var rhs = IsFloatRegister( a ) ? (double)BitConverter.UInt32BitsToSingle( state.Registers[b] ) : unchecked( (int)state.Registers[b] );
				if ( !double.IsFinite( lhs ) || !double.IsFinite( rhs ) )
					return StaffSpriteVmError.NonFiniteArithmetic;
				var repeat = instruction.Op == StaffSpriteOp.WhileNotEqualRegister ? lhs != rhs : lhs <= rhs;
				if ( repeat )
				{
					// Peek before a taken jump: an empty native pop returns table offset zero, outside this staff subset.
					var target = state.StackPointer == StaffSpriteVmState.StackCapacity ? default : new StaffSpritePc( state.Stack[state.StackPointer] );
					if ( state.StackPointer == StaffSpriteVmState.StackCapacity )
						diagnostics |= StaffSpriteVmDiagnostic.EmptyStack;
					if ( !programs.Contains( target ) )
						return StaffSpriteVmError.UnsupportedStackPc;
					var word = Pop( state, ref diagnostics );
					Push( state, word, ref diagnostics );
					state.CurrentPc = target;
				}
				else
				{
					Pop( state, ref diagnostics );
					state.LoopDepth = unchecked( state.LoopDepth - 1 );
				}
				break;
			case StaffSpriteOp.End:
				// The decoded staff catalog has no END. This terminal rule is covered by a synthetic program only.
				state.RunState = StaffSpriteRunState.Ended;
				state.OutputValid = 0;
				break;
			default:
				return StaffSpriteVmError.UnsupportedInstruction;
		}
		return StaffSpriteVmError.None;
	}

	private static void Emit( StaffSpriteVmState state, uint frame )
	{
		state[StaffSpriteRegister.Frame] = frame;
		state.OutputValid = frame == uint.MaxValue ? 0u : 1u;
		state.RunState = StaffSpriteRunState.Yielded;
	}

	private static void Push( StaffSpriteVmState state, uint word, ref StaffSpriteVmDiagnostic diagnostics )
	{
		if ( state.StackPointer == 0 )
			diagnostics |= StaffSpriteVmDiagnostic.FullStack;
		else
			state.Stack[--state.StackPointer] = word;
	}

	private static uint Pop( StaffSpriteVmState state, ref StaffSpriteVmDiagnostic diagnostics )
	{
		if ( state.StackPointer == StaffSpriteVmState.StackCapacity )
		{
			diagnostics |= StaffSpriteVmDiagnostic.EmptyStack;
			return 0;
		}
		return state.Stack[state.StackPointer++];
	}

	private static StaffSpriteTickResult Invalid( StaffSpriteVmState state, int count, StaffSpriteVmError error, StaffSpriteVmDiagnostic diagnostics )
	{
		state.OutputValid = 0;
		return new( StaffSpriteTickStatus.Invalid, count, error, diagnostics );
	}

	private static bool IsRegister( int register ) => (uint)register < StaffSpriteVmState.RegisterCount;
	private static bool IsFloatRegister( int register ) => register is >= 1 and <= 6;
	private static void ValidateEntry( StaffSpriteProgram program )
	{
		if ( !StaffSpritePrograms.IsEntry( program ) )
			throw new ArgumentOutOfRangeException( nameof( program ), "Only decoded staff template and performance entry points are supported." );
	}
}
