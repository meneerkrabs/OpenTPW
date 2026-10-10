namespace OpenTPW;

/// <summary>
/// Register of the M3 gate's own assumptions (docs/M3-GATE.md). Each entry has a matching
/// <c>// [APPROX:GATE-NNN]</c> comment at its code site in <see cref="M3Gate"/>.
/// </summary>
public static class M3GateApproximations
{
	public static readonly IReadOnlyList<(string Id, string Assumption, string EvidenceNeeded)> All = new[]
	{
		("GATE-001", "an M3 in-game minute is 60 s of normal-speed simulation (3,600 fixed 60 Hz ticks), not a park-clock minute", "the original's notion of elapsed play time for the M3 gate"),
		("GATE-003", "the Belly Bounce wait bound is reported with boarding latency tau = 0 s (a lower bound) next to the tau the run implies; it is evidence, not a pass threshold", "the walk speed and the script loop latency between VAR_LETMEON and BOUNCE (QUEUE-plan §9b, §10)"),
	};
}
