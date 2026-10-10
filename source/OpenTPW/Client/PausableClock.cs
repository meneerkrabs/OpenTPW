namespace OpenTPW;

/// <summary>
/// The original's advisor clock: milliseconds of a running clock that stands still while the
/// game is paused and continues from the same value afterwards.
/// </summary>
// [BIN:STP-PPC:0x10117C00 clock read] frozen: snapshot − compensation, else now − compensation; freeze (0x10117AE8) takes the snapshot, resume (0x10117B30) adds now − snapshot to the compensation
// [BIN:STP-PPC:0x10110518 game pause] the pause freezes the clock object at TOC −0x75D8 (0x1010E888), which the advisor controller (0x1000A130) and response player (0x10006B7C) read
internal sealed class PausableClock
{
	private readonly Func<long> source;
	private long snapshot;
	private long compensation;

	public PausableClock( Func<long> source ) => this.source = source;

	public bool Paused { get; private set; }

	public uint Milliseconds => unchecked((uint)((Paused ? snapshot : source()) - compensation));

	public void SetPaused( bool paused )
	{
		if ( paused == Paused )
			return;
		if ( paused )
			snapshot = source();
		else
			compensation += source() - snapshot;
		Paused = paused;
	}
}
