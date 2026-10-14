namespace OpenTPW;

/// <summary>
/// Register of the determinism rules not taken from the original (docs/DETERMINISM.md, numbering from
/// docs/reverse/DET-plan.md §4.6). Each entry has a matching <c>// [APPROX:DET-NNN]</c> comment at its code site.
/// </summary>
public static class DeterminismApproximations
{
	public static readonly IReadOnlyList<(string Id, string Assumption, string EvidenceNeeded)> All = new[]
	{
		("DET-013", "scripts without an explicit seed get a per-VM System.Random-compatible stream seeded from the park's SplitMix64 stream, and the bump rides draw from the park's seeded stream, not from the original's one world generator (tp.exe 0x516330: s = rotr(s * 1664525 + 1013904223, 13), the rotated value is the new state, the result is abs(s) with int.MinValue staying negative)", "port requirements (static evidence, nothing missing): one WorldRng per level holding the u32 at world+0x1DA708; reseed with the zero-extended u16 thing id (0x516370) at the 8 sites 0x4D71CA (litter sweep spot), 0x4D94E2 (litter drop), 0x4F7B7E, 0x4FB19E (guest creation: 8 draws, reseed, 2 draws at 0x4FB201 and 0x4FB21C), 0x4FCAFD, 0x4FE6A1 and 0x4FE6FC (balloon) and 0x50205F, all taking the id through 0x50B350; seed the state from time() at world setup (0x67D3F0 -> 0x515879) and save it as mRandomSeed next to mGameTick (save 0x51710F/0x517123, load 0x517DE8/0x517DFC); every consumer on the one stream with its own projection (RAND labs(labs(r >>> 1) % (int16 operand + 1)) at 0x55398A, ADDHEAD 0x554CB5 and FINDSCRIPTRAND 0x5560CB (labs(r >>> 1) % n) + 1, tour and flying car 0x55A770 and 0x55DB41 labs(r) % 100, kart and water 0x54A082, economy and staff around 0x4D47xx and 0x4FFxxx, guests around 0x4F9xxx-0x502xxx: 139 direct calls and 4 inline copies at 0x51674B, 0x51680B, 0x516879 and 0x51A34B); the original call order (substeps and the RSE pass schedule, things newest first, economy month-end), without which the distribution is right but not the sequence; the OpenTPW save stores the one u32 and an original import reads mRandomSeed"),
		("DET-014", "guests draw from their own SplitMix64 stream seeded from the world seed, not from the original's shared world generator reseeded to each new guest's id", "port of the world generator (see DET-013): guest creation 0x4FB19E draws 8 times, reseeds with the guest's id and draws twice more; the guest sites around 0x4F9xxx-0x502xxx use the same stream in the original update order"),
		("DET-015", "the economy draws from its own SplitMix64 stream seeded from the world seed, not from the original's shared world generator", "port of the world generator (see DET-013): the economy and staff sites around 0x4D47xx and 0x4FFxxx draw from the one stream in the original update order (economy month-end after the things)"),
	};
}
