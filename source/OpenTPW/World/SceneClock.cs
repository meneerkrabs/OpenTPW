namespace OpenTPW;

/// <summary>
/// The scene's rate-scaled clock: what the original's render and animation code read as the scaled clock (the timer 0x785970 through 0x402d70 and the cached copy 0x7b496c).
/// It gains the real time of each frame times the game-speed scale (0 while paused, 0.25..2 by the speed keys; OpenTPW's x4 is an extension) and reads out as whole milliseconds,
/// so the scene's animation (the sky, the coasters' scrolling textures, the weather) runs at the park's speed and stands still while the park is paused.
/// </summary>
public sealed class SceneClock
{
	/// <summary>The most one frame's gap can be for the consumers that cap it (the sky and the weather read the frame time 0x57FF60 stores at +0x9D98).</summary>
	public const uint MaximumFrameMilliseconds = 500;

	private double accumulated;
	private uint milliseconds;
	private uint frameMilliseconds;

	/// <summary>The clock's reading in whole milliseconds (the accumulator truncated, as 0x4033A0's float to integer conversion does).</summary>
	// [BIN:TPW-EXE:0x4033A0 scaled clock] the real gap in milliseconds times the rate (+0x20, a double) is added to the accumulator (+0x18, a double) and the reading is that sum truncated (0x67A830)
	public uint Milliseconds => milliseconds;

	/// <summary>The gap of the last frame in whole milliseconds on this clock.</summary>
	// [BIN:TPW-EXE:0x473440 frame refresh] each frame stores the new reading (0x7B496C) and the gap to the previous one times 0.001 as a float (0x7B497C)
	public uint FrameMilliseconds => frameMilliseconds;

	/// <summary>The gap of the last frame in seconds, capped at <see cref="MaximumFrameMilliseconds"/> as the sky and the weather read it.</summary>
	// [BIN:TPW-EXE:0x57FF60 frame time] the scene's frame time is min(gap, 500) ms times 0.001, taken on the scaled clock; the sky's scroll (0x585F10) reads it from +0x9D98
	public float CappedFrameSeconds => Math.Min( frameMilliseconds, MaximumFrameMilliseconds ) * 0.001f;

	/// <summary>One frame: adds <paramref name="scaledSeconds"/> (the frame's real seconds times the time scale; zero while paused, anything not finite or negative counts as zero).</summary>
	public void Advance( double scaledSeconds )
	{
		var step = double.IsFinite( scaledSeconds ) && scaledSeconds > 0 ? scaledSeconds * 1000.0 : 0.0;
		accumulated += step;
		var next = unchecked( (uint)(long)accumulated );
		frameMilliseconds = next - milliseconds;
		milliseconds = next;
	}
}
