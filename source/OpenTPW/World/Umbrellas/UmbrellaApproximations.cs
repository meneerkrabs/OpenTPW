namespace OpenTPW;

/// <summary>
/// The umbrellas extension's approximation register (docs/UMBRELLAS.md). Each entry is tagged <c>[APPROX:UMB-NNN]</c> at its code site; the words before the colon name the topic. Everything else of the extension is
/// traced from the Theme Park Inc no-CD <c>Game.exe</c> (<c>[BIN:TPI-EXE:0x...]</c> labels) or is OpenTPW's own, with <c>[EXT:umbrellas-*]</c> labels; it is not Theme Park World's behaviour, which has no umbrellas.
/// </summary>
public static class UmbrellaApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new (string Id, string Rule)[]
	{
		// register-empty: UMB-001 to UMB-004 are retired (docs/UMBRELLAS.md); the file stays for a later entry
	};
}
