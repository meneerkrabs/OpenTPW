namespace OpenTPW;

/// <summary>
/// The RSE-VM approximation register (docs/RSE-VM.md). Every entry names semantics the VM executes without
/// original-executable proof, or a traced boundary the port's model does not reproduce. IDs VM-900..919 are
/// this lane's reserved range.
/// </summary>
public static class VmApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("VM-901", "WAITABS: the deadline is the 32-bit clock word plus the operand, compared as the original does (tp.exe 0x553828), but the wake runs on this VM's non-wrapping double clock, so a wait that spans the 2^32 ms wrap of the original's clock word is not modelled"),
	};
}
