namespace OpenTPW;

/// <summary>
/// The frame cap of <see cref="DisplaySettings.FrameCap"/> ([EXT:frame-cap], docs/BROWSER-PERFORMANCE.md): decides whether a frame is
/// due. The browser asks at every animation frame and skips the early ones; the desktop sleeps for <see cref="WaitSeconds"/> first.
/// Frames are scheduled on a grid of 1/cap seconds, so a 30 fps cap on a 60 Hz display draws every second frame instead of drifting;
/// a frame more than one interval late restarts the grid. Only drawing is capped: the simulation's fixed clock catches up the time.
/// </summary>
public sealed class FrameLimiter
{
	/// <summary>
	/// How early a frame may come and still count, as a share of the interval: the jitter of animation-frame and vsync timing. A quarter
	/// keeps a cap at the display rate from dropping frames and still skips every other frame of a display twice as fast; the grid keeps
	/// the average at the cap.
	/// </summary>
	public const double SlackFraction = 0.25;

	private double next;

	private static double Slack( int cap ) => SlackFraction / cap;

	/// <summary>Seconds to wait before the next frame is due under <paramref name="cap"/>; 0 when it is due now or there is no cap.</summary>
	public double WaitSeconds( double nowSeconds, int cap ) => cap <= 0 ? 0 : Math.Max( 0, next - Slack( cap ) - nowSeconds );

	/// <summary>True when a frame is due at <paramref name="nowSeconds"/> under <paramref name="cap"/> (0: every frame); a due frame books the next.</summary>
	public bool Due( double nowSeconds, int cap )
	{
		if ( cap <= 0 )
		{
			next = 0;
			return true;
		}
		if ( nowSeconds + Slack( cap ) < next )
			return false;
		var interval = 1d / cap;
		next = nowSeconds - next > interval ? nowSeconds + interval : next + interval;
		return true;
	}
}
