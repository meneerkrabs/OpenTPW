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
/// Implemented by effects that own animation channels: the raw AnimID of a channel as the query 0xa7dcc reads it
/// (12 = none, 14 = ended), for TRIGWAITANIM's resume check. Effects without it have no channels to read, so
/// TRIGWAITANIM resumes as for a script without an object (the query is skipped and the wait never ends).
/// </summary>
public interface IRideAnimationQuery
{
	int GetAnimationId( RideVM vm, int channel );
}

/// <summary>Implemented by effects whose state later script steps read back; hashed with the root script.</summary>
public interface IRideCanonicalEffects
{
	void AddCanonicalState( StateHasher hash );
}

/// <summary>
/// Default effects: nothing in OpenTPW implements these game systems yet. Each call is counted in
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
