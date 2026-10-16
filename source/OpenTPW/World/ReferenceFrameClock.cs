namespace OpenTPW;

/// <summary>
/// The 60 Hz reference clock for original behaviour that tp.exe steps once per rendered frame instead of once per simulation
/// substep or per elapsed millisecond: it turns the real time of OpenTPW's own frames into whole reference frames of
/// <see cref="FrameSeconds"/>, keeping the remainder, so such a behaviour runs at the same rate on every display.
/// Presentation only: it feeds no simulation state, so it never moves the determinism pin.
/// </summary>
// [EXT:frame-driven-at-60hz] tp.exe's main loop (0x54e360) has no frame limiter: after the 31 ms simulation substeps it draws one frame, and the UI
// sends message 0x1e to every window once per drawn frame (0x48a580 -> 0x658f6c -> 0x658f97), so whatever the original steps from 0x1e, or from the
// per-frame lobby update (0x5d5c50 -> 0x5e0470), ran at the speed of the PC's frame rate; PCGamingWiki's page for Theme Park World reports the
// game "runs too fast and becomes unplayable" at high frame rates and advises limiting the frame rate to 60 FPS, so OpenTPW steps each of them at
// a fixed 60 Hz reference, whatever its own frame rate
public sealed class ReferenceFrameClock
{
	/// <summary>The reference rate.</summary>
	public const int FramesPerSecond = 60;
	/// <summary>One reference frame in seconds.</summary>
	public const float FrameSeconds = 1f / FramesPerSecond;
	/// <summary>The most real time one <see cref="Advance"/> accepts by default; a longer stall (a loading hitch) is dropped rather than replayed.</summary>
	public const float DefaultMaximumSeconds = 0.5f;

	private const double FrameDuration = 1d / FramesPerSecond;
	private readonly double maximumSeconds;
	private double pending;

	/// <summary>A clock that replays at most <paramref name="maximumSeconds"/> of a stall (default half a second, as the lobby's step cap).</summary>
	public ReferenceFrameClock( float maximumSeconds = DefaultMaximumSeconds )
	{
		if ( !float.IsFinite( maximumSeconds ) || maximumSeconds < FrameDuration )
			throw new ArgumentOutOfRangeException( nameof( maximumSeconds ), "the cap must hold at least one reference frame" );
		this.maximumSeconds = maximumSeconds;
	}

	/// <summary>The part of a reference frame already banked, in seconds, always below <see cref="FrameSeconds"/> at rest.</summary>
	public float PendingSeconds => (float)pending;

	/// <summary>
	/// Adds <paramref name="deltaSeconds"/> of real time and returns how many whole reference frames are due. A negative or
	/// non-finite delta adds nothing.
	/// </summary>
	public int Advance( float deltaSeconds )
	{
		if ( !float.IsFinite( deltaSeconds ) || deltaSeconds <= 0 )
			return 0;
		pending = Math.Min( pending + deltaSeconds, maximumSeconds );
		var frames = 0;
		// The tolerance keeps a delta of exactly one reference frame, as a float, from losing the frame to rounding.
		while ( pending + FrameDuration * 1e-6 >= FrameDuration )
		{
			pending = Math.Max( 0, pending - FrameDuration );
			frames++;
		}
		return frames;
	}

	/// <summary>Forgets the banked time (a screen opens or the scene changes).</summary>
	public void Reset() => pending = 0;
}
