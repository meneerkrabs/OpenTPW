namespace OpenTPW;

/// <summary>
/// The rider head approximation register (docs/GUESTS.md, "Riders' heads"; docs/RSE-VM.md, "Visitors"). Each entry is
/// tagged <c>[APPROX:HEADS-NNN]</c> at its code site. ADDHEAD and DELHEAD (the per-script slot table, its random draw, the record-to-node link and the head drawing of 0x1019B6F8) are traced from the Mac binary (BIN labels at the code sites).
/// </summary>
public static class HeadApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("HEADS-004", "the head's roll is the node matrix's own (atan2(-m10, m11) of the rotation left once the chosen frame's direction is taken out, as tp.exe's 0x46F200 turns the sprite, register 9 in turns), but OpenTPW composes it from the node matrix in world axes where the PC routine 0x44B510 builds the chosen frame's own reference basis from its row and column (0x44B6EF..0x44B96C) and multiplies it into the view through 0x46F150, 0x46FBB0 and 0x470040, and that composition has not been compared instruction by instruction (size and depth are read: the head is a camera-facing quad of scale 0.685 at the node's position, 20 / 128 x sqrt(2) world units a pixel times the scale, no depth offset); OpenTPW guests wear no costumes, so the costumeheads art (visitor +0x24 == 2) is never chosen"),
	};
}
