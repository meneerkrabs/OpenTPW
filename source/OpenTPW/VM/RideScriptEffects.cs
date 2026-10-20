namespace OpenTPW;

/// <summary>
/// A game effect requested by a hooked opcode. Operand values are already resolved;
/// <see cref="SetOutput"/> lets an implementation write a result into a variable operand when the
/// opcode's command decides the direction. Reviewed BUMP/COAST/TOUR query handlers
/// write their returned result to the applicable parameter themselves.
/// </summary>
public readonly struct RideEffectCall
{
	private readonly Operand[] operands;

	public RideVM VM { get; }
	public Opcode Opcode { get; }
	public int OperandCount => operands.Length;

	internal RideEffectCall( RideVM vm, Opcode opcode, Operand[] operands )
	{
		VM = vm;
		Opcode = opcode;
		this.operands = operands;
	}

	public int Argument( int index ) => operands[index].Value;
	public bool IsVariable( int index ) => operands[index].IsVariable;
	/// <summary>The operand's code word as the original stores a raw operand: the low 16 bits, and 0x40000000 on top for a variable (an operand the handler does not resolve).</summary>
	public int RawWord( int index ) => operands[index].IsVariable ? 0x4000_0000 | operands[index].Raw : operands[index].Raw;
	public void SetOutput( int index, int value ) => operands[index].Value = value;

	public override string ToString() => $"{Opcode} {string.Join( " ", operands.Select( x => x.Value ) )}".TrimEnd();
}

/// <summary>
/// Receives every game effect of a hooked opcode. The return value is the opcode's result
/// (written to its destination and/or used for flags, as each handler documents) or, for animation
/// opcodes, the animation duration in milliseconds. Controller mutators may ignore the
/// result. Implementations must record unavailable calls with <see cref="RideVM.RecordUnimplementedEffect"/>;
/// controller queries then preserve prior flags/output rather than using a fabricated zero.
/// </summary>
public interface IRideScriptEffects
{
	int Perform( RideEffectCall call );
}

/// <summary>
/// Implemented by effects whose object takes the script loop's controller update after each slice of its script (the tail of tp.exe <c>0x5516B0</c>'s loop body, which
/// runs on the passes the slice cadence selects whether or not the script had instructions to run).
/// </summary>
public interface IRideSliceTail
{
	void AfterSlice( RideVM vm );
}

/// <summary>
/// Implemented by effects that own animation channels: the raw AnimID of a channel as the query 0xa7dcc reads it
/// (12 = none, 14 = ended), for TRIGWAITANIM's resume check. Effects without it have no channels to read, so
/// TRIGWAITANIM resumes as for a script without an object (the query is skipped and the wait never ends).
/// </summary>
public interface IRideAnimationQuery
{
	int GetAnimationId( RideVM vm, int channel );

	/// <summary>The AnimID, or false when this host has no object to ask (a wrapper around effects without channels).</summary>
	bool TryGetAnimationId( RideVM vm, int channel, out int animationId )
	{
		animationId = GetAnimationId( vm, channel );
		return true;
	}
}

/// <summary>
/// Implemented by effects that keep per-script records (the object list of tp.exe's scripts, +0xB0): told when a script is destroyed, so its records end with it.
/// </summary>
// [BIN:TPW-EXE:0x558703 script destructor] 0x558500 ends every record the script still holds (types 1-2 through 0x51FF70(handle, -2), 3-10 through 0x51C2C0) and frees it; the pass destroys a script whose program counter has gone negative (END, 0x551ABC -> 0x559060) and a removed or stopped script is destroyed the same way
public interface IRideScriptLifetime
{
	/// <summary>The script ended or was stopped: its records end now. Called again for a script already destroyed, which does nothing.</summary>
	void ScriptDestroyed( RideVM vm );
}

/// <summary>Implemented by effects whose state later script steps read back; hashed with the root script.</summary>
public interface IRideCanonicalEffects
{
	void AddCanonicalState( StateHasher hash );
}

/// <summary>
/// Default effects: nothing is implemented behind them (a bare VM, or an opcode no host game system serves). Each call is counted in
/// <see cref="RideVM.UnimplementedEffects"/>, logged once per VM and opcode, and returns 0
/// (no visitor, no animation time, nothing queued).
/// </summary>
public sealed class UnimplementedRideScriptEffects : IRideScriptEffects
{
	public static UnimplementedRideScriptEffects Instance { get; } = new();

	public int Perform( RideEffectCall call )
	{
		if ( call.VM.RecordUnimplementedEffect( call.Opcode ) )
			Log?.Trace( $"RSE '{call.VM.ScriptName}': unimplemented effect {call}" );
		return 0;
	}
}
