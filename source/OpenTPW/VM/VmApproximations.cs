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
		("VM-901", "WAITABS: STP-PPC 0xb0710 proves now + operand stored without speed division, first-encounter yield and unsigned-deadline comparison; the port keeps that deadline in its non-wrapping double script clock, so the original's 2^32 wrap and zero-deadline re-arm sentinel (docs/reverse/PPC-clock.md) are not modelled"),
		("VM-902", "YEAR/MONTH/DAY/HOUR: the TPW object hook samples managed host-local civil time using the Feral C time/localtime field mapping. Provider/current host input is not saved or hashed; VM variable writes/flags remain saved and hashed. Replay depends on the same external samples. Managed timezone, clock changes, invalid/rollover dates and Windows/TPI parity are unqualified; existing destination/literal/flag behavior is not established by the clock-domain witness"),
	};
}
