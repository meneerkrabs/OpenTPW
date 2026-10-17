namespace OpenTPW;

/// <summary>
/// The host's part of the original's park turn (tp.exe <c>0x00516380</c>): after the turn counter has advanced, the thing sweep visits every live thing newest first and
/// dispatches it by its type (guests, rides, staff, the weather, the challenge system), then the gate retry, the turn's deferred destroys and the arrival update run
/// before the economy's calendar tail. An economy without a sweep (the headless economy of tests and tools) runs its own thing update in a fixed order.
/// </summary>
public interface IThingSweep
{
	/// <summary>
	/// One park turn's sweep and what follows it up to the world turn's tail (0x00516380 to 0x004D7B20). <paramref name="lost"/> is world state 4 (a lost park): the thing loop
	/// is skipped, the rest runs.
	/// </summary>
	void Run( ParkEconomy economy, long turn, bool lost );
}
