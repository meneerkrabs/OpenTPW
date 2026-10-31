namespace OpenTPW;

/// <summary>
/// The rider head approximation register (docs/GUESTS.md, "Riders' heads"; docs/RSE-VM.md, "Visitors"). Each entry is
/// tagged <c>[APPROX:HEADS-NNN]</c> at its code site. ADDHEAD and DELHEAD (the per-script slot table, its random draw, the record-to-node link and the head drawing of 0x1019B6F8) are traced from the Mac binary (BIN labels at the code sites).
/// </summary>
public static class HeadApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("HEADS-007", "BOUNCE body frame selection consumes a persistent visitor player with decoded programs 1..8, strict signed 32-bit due times, pending requests and explicit lifetime, stepped by the sprite scheduler on even substeps before the world update (0x54F5D7, 0x54F5FB) on the host's fixed-60Hz sample ([EXT:fixed-60hz-clock]); an imported rider's player is the saved script record (SAVE-013); remaining parity is bounded by the current guest movement/state/ToiletBonus history, the existing bank variation selection and the reconstruction of a save without body history (an OpenTPW save older than the sprite state: a fresh body in the host's load epoch, no emitted frame; docs/VISITOR-SPRITES.md). Ordinary walking and scripted WALK rendering still use their existing presentation; swimming, generic teleport callbacks and avatars are outside this bounded owner"),
	};
}
