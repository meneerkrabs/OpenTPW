namespace OpenTPW;

/// <summary>
/// The rider head approximation register (docs/GUESTS.md, "Riders' heads"; docs/RSE-VM.md, "Visitors"). Each entry is
/// tagged <c>[APPROX:HEADS-NNN]</c> at its code site. ADDHEAD and DELHEAD (the slot table, its random draw and the hang
/// and take-down of a head) are traced from the Mac binary (BIN labels at the code sites).
/// </summary>
public static class HeadApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("HEADS-001", "the slot draw of ADDHEAD uses a generator seeded per ride, not the original's global rand; the sequence of draws differs, the uniform choice among free slots does not"),
		("HEADS-002", "a head is drawn at the model node named after its id (headNN, HeadN, Kid_NN, spriteposition NN); the original's link from a head record to its node is not traced, and a head whose id has no such node is not drawn"),
		("HEADS-003", "the head records are the model's dummy attribute records (type flag 0x80, value = head id) and the table length is the run of ids from 1; the Mac lookup state is read from its 20-byte records by value, and the PC executable's sizing rule is used"),
	};
}
