namespace OpenTPW;

/// <summary>
/// The rider head approximation register (docs/GUESTS.md, "Riders' heads"; docs/RSE-VM.md, "Visitors"). Each entry is
/// tagged <c>[APPROX:HEADS-NNN]</c> at its code site. ADDHEAD and DELHEAD (the per-script slot table, its random draw, the record-to-node link and the head drawing of 0x1019B6F8) are traced from the Mac binary (BIN labels at the code sites).
/// </summary>
public static class HeadApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("HEADS-004", "the head's roll now removes the selected frame reference basis from camera-world times inverse node (tp.exe 0x44B637, 0x44B65C, 0x44B99D, 0x44B9C2 and 0x46F200); native sine-table/x87 arithmetic and rendered reference comparison remain unqualified (size and depth are read: the head is a camera-facing quad of scale 0.685 at the node's position, 20 / 128 x sqrt(2) world units a pixel times the scale, no depth offset); OpenTPW guests wear no costumes, so the costumeheads art (visitor +0x24 == 2) is never chosen"),
		("HEADS-007", "BOUNCE body frame selection now consumes a persistent visitor player with decoded programs 1..8, strict signed 32-bit due times, pending requests and explicit lifetime; remaining parity is bounded by the host fixed-60Hz sample rather than the native independent timer, current guest movement/state/ToiletBonus history, existing bank variation selection and old-save reconstruction and the sprite history of an imported rider, which is posed from the saved bounce and walk entries while its player history is the Scripts block's guest sprite script (SAVE-013; docs/VISITOR-SPRITES.md). Ordinary walking and scripted WALK rendering still use their existing presentation; swimming, generic teleport callbacks and avatars are outside this bounded owner"),
	};
}
