namespace OpenTPW;

/// <summary>
/// Register of the M4 gate's own assumptions (docs/M4-GATE.md). Each entry has a matching APPROX comment with
/// its id at its code site in <see cref="M4GateRun"/>.
/// </summary>
public static class M4GateApproximations
{
	public static readonly IReadOnlyList<(string Id, string Assumption, string EvidenceNeeded)> All = new[]
	{
		("GATEM4-001", "a working attraction admits a queued guest within W, releases a boarded guest within W, and after closing releases its riders within W; W = 5 simulated minutes", "a traced bound on one ride cycle (admission, ride, unload) per ride type"),
		("GATEM4-002", "closing ends use: no guest boards a closed attraction", "a trace of the host's admission test against VAR_RIDECLOSED (QUEUE-plan) and of what happens to riders and the queue on closing"),
	};
}
