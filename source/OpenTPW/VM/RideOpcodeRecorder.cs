namespace OpenTPW;

/// <summary>
/// Records which opcodes the scripts on the current async flow execute (diagnostics: the M4 gate's opcode inventory
/// and the opcode test map, docs/M4-GATE.md). <see cref="RideVM"/> reports every instruction it is about to execute
/// while a recorder is active; it changes nothing the script does. Recorders nest: the innermost one on the flow
/// receives the instructions, and disposing it makes the outer one current again.
/// </summary>
public sealed class RideOpcodeRecorder : IDisposable
{
	private static readonly AsyncLocal<RideOpcodeRecorder?> current = new();
	private readonly RideOpcodeRecorder? outer;
	private readonly Dictionary<Opcode, long> executed = new();
	private readonly Dictionary<Opcode, long> throughRuntimeEffects = new();
	private readonly List<RideVM> machines = new();
	private readonly HashSet<RideVM> seen = new( ReferenceEqualityComparer.Instance );
	private bool disposed;

	private RideOpcodeRecorder()
	{
		outer = current.Value;
		current.Value = this;
	}

	/// <summary>Starts recording on the current async flow until the returned recorder is disposed.</summary>
	public static RideOpcodeRecorder Start() => new();

	/// <summary>Executed instructions per opcode, children included.</summary>
	public IReadOnlyDictionary<Opcode, long> Executed => executed;

	/// <summary>Executions per opcode by scripts whose effects are a game layer (<see cref="IsRuntimeEffects"/>).</summary>
	public IReadOnlyDictionary<Opcode, long> ExecutedThroughRuntimeEffects => throughRuntimeEffects;

	/// <summary>Every script that executed at least one instruction, in first-execution order.</summary>
	public IReadOnlyList<RideVM> Machines => machines;

	/// <summary>
	/// Hooked opcodes executed through a game layer and never recorded as unimplemented by the script that ran them.
	/// Read after the scripts ran: <see cref="RideVM.UnimplementedEffects"/> counts every call the layer could not perform.
	/// </summary>
	public IReadOnlySet<Opcode> EffectsPerformedByRuntime()
	{
		var performed = new HashSet<Opcode>();
		var failed = new HashSet<Opcode>();
		foreach ( var vm in machines.Where( vm => IsRuntimeEffects( vm.Effects ) ) )
		{
			foreach ( var opcode in vm.UnimplementedEffects.Keys )
				failed.Add( opcode );
		}
		foreach ( var opcode in throughRuntimeEffects.Keys.Where( opcode => RideOpcodes.GetStatus( opcode ) == RideOpcodeStatus.Hooked ) )
		{
			if ( !failed.Contains( opcode ) )
				performed.Add( opcode );
		}
		return performed;
	}

	/// <summary>Effects that route hooked opcodes to OpenTPW's game systems (object animation, the visitor bridge), not to a test double or the default.</summary>
	public static bool IsRuntimeEffects( IRideScriptEffects effects ) => effects is OriginalObjectEffects or VisitorRideScriptEffects;

	/// <summary>The recorder of the current async flow, or null (the common case: one lookup per instruction).</summary>
	internal static RideOpcodeRecorder? Current => current.Value;

	internal void Record( RideVM vm, Instruction instruction )
	{
		if ( disposed )
			return;
		var opcode = (Opcode)instruction.RawOpcode;
		executed[opcode] = executed.GetValueOrDefault( opcode ) + 1;
		if ( IsRuntimeEffects( vm.Effects ) )
			throughRuntimeEffects[opcode] = throughRuntimeEffects.GetValueOrDefault( opcode ) + 1;
		if ( seen.Add( vm ) )
			machines.Add( vm );
	}

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		if ( current.Value == this )
			current.Value = outer;
	}
}
