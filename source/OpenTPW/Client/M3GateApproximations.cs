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
		("GATE-004", "the walk terms w (to the stand point) and w2 (new head to slot 0) of the BOUNCE boarding bound use OpenTPW's walk model (WalkSpeedCellsPerSecond x 0.7, the QUEUE-006 slot points and the QUEUE-007 stand point); the original's steering step has a speed cap but no traced floor", "the steering step and velocity floor (0xfec9c, the +28 cap at 0xfed98) and the 0xde1d8 / 0xdde74 geometry"),
	};
}
