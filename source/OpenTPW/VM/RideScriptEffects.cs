namespace OpenTPW;

/// <summary>
/// A game effect requested by a hooked opcode. Operand values are already resolved;
/// <see cref="SetOutput"/> lets an implementation write a result into a variable operand when the
/// opcode's command decides the direction (BUMP/COAST/TOUR).
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
/// opcodes, the animation duration in milliseconds.
/// </summary>
public interface IRideScriptEffects
{
	int Perform( RideEffectCall call );
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
