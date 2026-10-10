namespace OpenTPW;

/// <summary>
/// Register of the determinism rules not taken from the original (docs/DETERMINISM.md, numbering from
/// docs/reverse/DET-plan.md §4.6). Each entry has a matching <c>// [APPROX:DET-NNN]</c> comment at its code site.
/// </summary>
public static class DeterminismApproximations
{
	public static readonly IReadOnlyList<(string Id, string Assumption, string EvidenceNeeded)> All = new[]
	{
		("DET-002", "runs take an explicit world seed (default 0x5450574775657374) instead of the original's time(NULL)/timer seeds", "none possible; explicit seeding is an OpenTPW replay policy"),
		("DET-012", "every sound draw stores its successor as the new seed; unmerged advisor phase 10 reads the bundle's choosers as never storing it", "that phase merged and re-pinned on main"),
		("DET-013", "scripts without an explicit seed get a per-VM System.Random seeded from the park's SplitMix64 stream, not draws from the original's one world generator", "DET-I2 (RSE RAND/FINDSCRIPTRAND on the world LCG)"),
		("DET-014", "guests draw from their own SplitMix64 stream seeded from the world seed, not from the original's shared world LCG reseeded to each new guest's id", "DET-I2 port of WorldRng"),
		("DET-015", "the economy draws from its own SplitMix64 stream seeded from the world seed, not from the original's shared world LCG", "DET-I2 port of WorldRng"),
		("DET-016", "the park save persists the economy and every random stream, but not guests, queues or object script state", "an OpenTPW save of the thing and script tables"),
	};
}
