namespace OpenTPW;

/// <summary>
/// The game thread's C runtime <c>rand()</c> stream: the MSVC generator (the state times 214013 plus 2531011, bits 16..30), seeded once at start-up from the millisecond
/// tick count, as tp.exe seeds its own. Until <see cref="Seed"/> runs it starts from 1, as the C runtime does without <c>srand</c>. Presentation only: no world, guest or
/// co-op stream reads it, nothing hashes or saves it.
/// </summary>
// [BIN:TPW-EXE:0x0054DDE3 rand seed] the start-up calls srand (0x67B5B0) with GetTickCount (import 0x6FD1D0), once; rand is 0x67B5C0. The C runtime keeps the state per
// thread; the game's draws all come from its main thread, which this one stream stands for
internal static class CrtProcessRandom
{
	private static uint state = 1;
	private static readonly Lock gate = new();

	/// <summary><c>srand(seed)</c>: called once by the game's start-up (<see cref="GameFlow"/>) with the tick count.</summary>
	public static void Seed( uint seed )
	{
		lock ( gate )
			state = seed;
	}

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
