namespace OpenTPW;

/// <summary>
/// The rider head approximation register (docs/GUESTS.md, "Riders' heads"; docs/RSE-VM.md, "Visitors"). Each entry is
/// tagged <c>[APPROX:HEADS-NNN]</c> at its code site. ADDHEAD and DELHEAD (the per-script slot table, its random draw, the record-to-node link and the head drawing of 0x1019B6F8) are traced from the Mac binary (BIN labels at the code sites).
/// </summary>
public static class HeadApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("HEADS-001", "the slot draw of ADDHEAD uses the original's formula, labs(worldRand() >> 1) % length (repeated until a free slot), on a generator seeded per ride instead of the shared world stream 0x10105328 (s * 1664525 + 1013904223), which is not ported (DET-015); so only the stream differs"),
		("HEADS-004", "the head's size and depth are untraced: how sprite scale 1.0 maps to world units (the head uses the body's pixel scale times the traced scale property 0.685), the depth handling of the quad, the azimuth origin and sense of column 0 and the reference frame of the roll (the node matrix in world axes); OpenTPW guests wear no costumes, so the costumeheads art (visitor +0x24 == 2) is never chosen"),
		("HEADS-005", "whether a child or sound script takes its parent's thing id (+0xAC) for its head table is untraced; each script has its own table as in the script init 0x100B2BA0, a child's table is dropped when its script stops or is replaced, joins the canonical state while it exists, and the park-resume snapshot refuses while one holds a head (no shipped child script uses a head opcode)"),
	};
}
