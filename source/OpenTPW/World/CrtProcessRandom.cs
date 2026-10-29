namespace OpenTPW;

/// <summary>
/// The C runtime's process-wide <c>rand()</c> stream: one MSVC generator (the state times 214013 plus 2531011, bits 16..30) for the whole process, seeded once at start-up
/// from the millisecond tick count, as tp.exe seeds its own. Presentation only: no world, guest or co-op stream reads it, nothing hashes or saves it.
/// </summary>
// [BIN:TPW-EXE:0x0054DDE3 rand seed] the start-up calls srand (0x67B5B0) with GetTickCount (import 0x6FD1D0), once; rand is 0x67B5C0
internal static class CrtProcessRandom
{
	private static uint state = unchecked((uint)Environment.TickCount);
	private static readonly Lock gate = new();

	/// <summary>The next <c>rand()</c>: 0 to 32767.</summary>
	public static int Next()
	{
		lock ( gate )
		{
			state = unchecked(state * 214013u + 2531011u);
			return (int)((state >> 16) & 0x7FFF);
		}
	}
}
