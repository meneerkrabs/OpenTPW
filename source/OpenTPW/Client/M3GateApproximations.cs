namespace OpenTPW;

/// <summary>
/// Register of the M3 gate's own assumptions (docs/M3-GATE.md). The gate is OpenTPW's test harness, so its former
/// entries were closed as design choices: [EXT:m3-gate-minute] (GATE-001) and [EXT:m3-gate-walk-bound] (GATE-006) in <see cref="M3Gate"/>.
/// </summary>
public static class M3GateApproximations
{
	// register-empty: the last entry was retired (tools/fidelity_register.py accepts an empty register with this marker).
	// GATE-006 (the queue walk bound from an enumeration of the walk as ported) is a test-harness design choice, not original behaviour.
	public static readonly IReadOnlyList<(string Id, string Assumption, string EvidenceNeeded)> All = Array.Empty<(string Id, string Assumption, string EvidenceNeeded)>();
}
