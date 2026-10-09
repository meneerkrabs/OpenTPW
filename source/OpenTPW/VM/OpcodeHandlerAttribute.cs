namespace OpenTPW;

/// <summary>
/// VM-level coverage of an opcode.
/// </summary>
public enum RideOpcodeStatus
{
	/// <summary>No handler: semantics unknown. Executing it faults the script.</summary>
	Unknown,

	/// <summary>The VM performs the opcode's whole documented/inferred behaviour itself.</summary>
	Implemented,

	/// <summary>
	/// Operands, results, flags and blocking are handled by the VM; the game effect is routed to
	/// <see cref="IRideScriptEffects"/>, whose default records it as an unimplemented effect.
	/// </summary>
	Hooked
}

/// <summary>
/// Marks a static method <c>(RideVM vm, Operand a, Operand b, ...)</c> as the handler for one opcode.
/// The parameter count after the VM is the operand count every instruction of that opcode must have.
/// </summary>
[AttributeUsage( AttributeTargets.Method, AllowMultiple = false, Inherited = false )]
public sealed class OpcodeHandlerAttribute : Attribute
{
	public Opcode Opcode { get; }
	public RideOpcodeStatus Status { get; }

	/// <summary>Where the semantics come from (upstream docs, corpus pattern, or both).</summary>
	public string Evidence { get; }

	public OpcodeHandlerAttribute( Opcode opcode, RideOpcodeStatus status, string evidence )
	{
		Opcode = opcode;
		Status = status;
		Evidence = evidence;
	}
}
