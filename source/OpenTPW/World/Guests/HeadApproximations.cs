namespace OpenTPW;

/// <summary>
/// The rider head approximation register (docs/GUESTS.md, "Riders' heads"; docs/RSE-VM.md, "Visitors"). Each entry is
/// tagged <c>[APPROX:HEADS-NNN]</c> at its code site. ADDHEAD and DELHEAD (the per-script slot table, its random draw, the record-to-node link and the head drawing of 0x1019B6F8) are traced from the Mac binary (BIN labels at the code sites).
/// </summary>
public static class HeadApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("HEADS-001", "the slot draw uses the original's formula, labs(worldRand() >>> 1) % length (tp.exe 0x554CB5, repeated until a free slot), on a stream seeded per ride instead of the shared world generator 0x516330 (s * 1664525 + 1013904223, rotated right by 13, absolute value), which is not ported (DET-013)"),
		("HEADS-004", "the head's size, depth and roll are partly untraced: how sprite scale 1.0 maps to world units (the head uses the body's pixel scale times the traced scale property 0.685), the depth handling of the quad, and the roll (the PC draw routine 0x44b510 turns the sprite by the angle between the chosen frame's pre-rendered up direction and the view, via 0x46f150, 0x46fbb0 and 0x46f200; OpenTPW uses the node matrix in world axes); OpenTPW guests wear no costumes, so the costumeheads art (visitor +0x24 == 2) is never chosen"),
	};
}
