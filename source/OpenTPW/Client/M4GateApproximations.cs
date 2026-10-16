namespace OpenTPW;

/// <summary>
/// Register of the M4 gate's own assumptions (docs/M4-GATE.md). The gate is OpenTPW's test harness, so its two former
/// entries were closed as design choices: [EXT:m4-gate-use-window] and [EXT:m4-gate-closing-ends-use] in <see cref="M4GateRun"/>.
/// </summary>
public static class M4GateApproximations
{
	// register-empty: both entries were retired (tools/fidelity_register.py accepts an empty register with this marker).
	// GATEM4-001 (the use window W) and GATEM4-002 (closing ends use) are test-harness design choices, not original behaviour.
	public static readonly IReadOnlyList<(string Id, string Assumption, string EvidenceNeeded)> All = Array.Empty<(string Id, string Assumption, string EvidenceNeeded)>();
}
