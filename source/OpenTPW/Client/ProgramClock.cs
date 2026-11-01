using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace OpenTPW;

/// <summary>
/// Milliseconds since the program started, never paused and never reset: the base of the original's high-resolution timer object (0xFA71E0), which its static initializer
/// starts (the function 0x5F5F80 sits in the C runtime's initializer table at 0x72DF14, and 0x5F5E80 stores the performance counter's start) and the lobby queue reads (0x5F5FA0).
/// </summary>
internal static class ProgramClock
{
	private static long start = Stopwatch.GetTimestamp();

	// the module initializer runs when the assembly loads, before any game code, so the base is the program's start and not the first lobby entry
	[ModuleInitializer]
	internal static void Start() => start = Stopwatch.GetTimestamp();

	/// <summary>Whole milliseconds since the program started.</summary>
	public static long Milliseconds => (long)Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
}
