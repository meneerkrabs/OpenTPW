namespace OpenTPW.Hud;

/// <summary>Instance-local presentation state; preparing a frame is separate from observational drawing.</summary>
internal struct EyeModeStaticTransition
{
	// [BIN:TPW-EXE:0x54faf7 static countdown] 0x12c ms on either Boolean edge of flags & 0x16; a nonzero-to-nonzero change does not reset it.
	internal const double DurationMilliseconds = 300;
	private bool previousEyeMode;
	private double previousStamp;
	public double RemainingMilliseconds { get; private set; }
	public bool ShowStatic { get; private set; }
	public int NoiseFrame { get; private set; }

	// [APPROX:UI-094] a new HUD snapshots its current host camera without manufacturing an entry transition. Native session reuse/initialization
	// is not fully recovered. One monotonic host sample per eligible HUD update replaces the two native post-draw GetTickCount samples;
	// backward/nonfinite samples are ignored, without reproducing signed/wrapping native clock arithmetic. Simulation pause is not a timer pause.
	public EyeModeStaticTransition( bool initialEyeMode ) => previousEyeMode = initialEyeMode;

	public void Prepare( bool eyeMode, double milliseconds, int noiseFrame )
	{
		var now = double.IsFinite( milliseconds ) ? Math.Max( previousStamp, milliseconds ) : previousStamp;
		if ( eyeMode != previousEyeMode )
		{
			RemainingMilliseconds = DurationMilliseconds;
			previousStamp = now;
		}
		previousEyeMode = eyeMode;
		// [BIN:TPW-EXE:0x54fb20 positive before draw] the current frame keeps static even when its elapsed interval exhausts the budget.
		ShowStatic = RemainingMilliseconds > 0;
		NoiseFrame = noiseFrame;
		if ( ShowStatic )
		{
			RemainingMilliseconds -= now - previousStamp;
			previousStamp = now;
		}
	}

	/// <summary>The finale skips observation and accounting; eligible resume will charge the elapsed wall interval, unless a new edge resets it.</summary>
	public void SkipFrame() => ShowStatic = false;
}
