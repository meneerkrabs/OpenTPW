namespace OpenTPW;

/// <summary>
/// The RSE-VM approximation register (docs/RSE-VM.md). Every entry names semantics the VM executes without
/// original-executable proof, or a traced boundary the port's model does not reproduce. IDs VM-900..919 are
/// this lane's reserved range.
/// </summary>
public static class VmApproximations
{
	// register-empty: the last entry was retired (tools/fidelity_register.py accepts an empty register with this marker).
	// VM-901 (WAITABS across the 2^32 ms wrap of the clock word) is retired: the wake is the unsigned difference of deadline and clock word, so a wait no longer ends at once after the wrap.
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = Array.Empty<(string Id, string Rule)>();
}
