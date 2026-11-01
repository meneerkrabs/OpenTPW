namespace OpenTPW.Tests;

/// <summary>
/// The guests' thing update runs only in the park turn (tp.exe 0x00516380, DET-014): a test that sets a guest up and expects its state handler to run needs the tick that starts a turn.
/// </summary>
internal static class GuestTurnTicks
{
	/// <summary>Ticks until the next tick would start a park turn, then runs that turn's tick.</summary>
	public static void TickTurn( this GuestSimulation simulation, float seconds )
	{
		while ( !simulation.NextTickStartsTurn )
			simulation.Tick( seconds );
		simulation.Tick( seconds );
	}
}
