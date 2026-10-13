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
		("VM-900", "MULT: dest = a × b with the SUB/DIV/MOD dest-first operand order, 32-bit wrap and flags; inferred from the upstream three-operand signature and Theme Park Inc's sole `MULT VAR_TIMER VAR_TIMEOUT VAR_SCALE` (followed by SETTIMER). No original dispatch of interpreter case 48 is traced, so the native accumulation and flags are unqualified"),
		("VM-901", "WAITABS: STP-PPC 0xb0710 proves now + operand stored without speed division, first-encounter yield and unsigned-deadline comparison; the port keeps that deadline in its non-wrapping double script clock, so the original's 2^32 wrap and zero-deadline re-arm sentinel (docs/reverse/PPC-clock.md) are not modelled"),
	};
}
