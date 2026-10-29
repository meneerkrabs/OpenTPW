namespace OpenTPW;

/// <summary>
/// Front-end lobby camera, the original's island view (tp.exe 0x5e0490, state 0, and 0x5e1210): the eye orbits
/// the selected island at the lobby's SPINRADIUS and VERTICALOFFSET (lobby.txt) while the angle advances by
/// SPINSPEED per tenth of a second, the eye and the target each move a fixed share of the way to their
/// goals every frame (the glide to a newly selected island; a per-frame share as in tp.exe, stepped at the 60 Hz reference
/// frame rate, <see cref="ReferenceFrameClock"/>), and the field of view is ISLANDFOV, applied by the camera method 0x5d8cb0 (vertical,
/// inferred from 0x582eb0/0x56b790).
/// </summary>
public class LobbyCameraMode : CameraMode
{
	public static float Radius { get; set; } = 70f;
	public static float VerticalOffset { get; set; } = 20f;
	public static float SpinSpeed { get; set; } = 0.02f;
	/// <summary>Vertical field of view in degrees (lobby.txt ISLANDFOV).</summary>
	public static float IslandFieldOfView { get; set; } = 100f;
	/// <summary>Island the camera orbits; changed by the front end.</summary>
	public static Vector3 Target { get; set; } = new Vector3( 400, 400, 12.5f );

	// [BIN:TPW-EXE:0x5d5c50 lobby time step] the lobby's step is the frame's milliseconds, at most 500 (0x1f4), × 0.01, so one step is a tenth of a second
	internal const float MaximumStepMilliseconds = 500f;
	internal const float StepPerMillisecond = 0.01f;
	// [BIN:TPW-EXE:0x5e1210 lobby glide] each frame the eye moves 0.1 × step and the target 0.2 × step of the way to their goals (0x702c7c, 0x702ca4)
	internal const float EyeGlidePerStep = 0.1f;
	internal const float TargetGlidePerStep = 0.2f;

	private Vector3 eye;
	private Vector3 target;
	private float angle;
	private float radius = Radius;
	private float height = VerticalOffset;

	/// <summary>The enter sequence's phase (tp.exe's camera state 0, 1 and 2 at [this+0x14], 0x5e0490).</summary>
	internal enum EnterPhase { None, Turn, Zoom }
	private EnterPhase phase;
	private float islandAngle;
	private Action? gateOpened;
	private Action? entered;
	private bool enteredRaised;

	/// <summary>The black fade the zoom ends in: the current value eases an eighth of the way to <see cref="FadeTarget"/> a frame (0x5d8cf2), 0..0x10000.</summary>
	internal int FadeCurrent { get; private set; }
	internal int FadeTarget { get; private set; }
	/// <summary>The fade's alpha, the current value shifted right by 8 and clamped to 255 (0x5d8d22-0x5d8d3e).</summary>
	public byte FadeAlpha => (byte)Math.Clamp( FadeCurrent >> 8, 0, 255 );
	internal EnterPhase Phase => phase;
	/// <summary>The camera's current orbit radius and height (shrinking during the zoom).</summary>
	internal float CurrentRadius => radius;
	internal float CurrentHeight => height;

	/// <summary>
	/// Starts the enter sequence on the selected island (tp.exe 0x5e1e30 sets state 1): the camera turns to its angle plus π, then
	/// <paramref name="gateOpened"/> is raised (the gate's M1 clip) and the camera zooms in under a black fade until its radius is
	/// below 8, when <paramref name="entered"/> (the park start, 0x5e1e50) is raised once.
	/// </summary>
	internal void BeginEnter( float islandAngleRadians, Action? gateOpened, Action? entered )
	{
		islandAngle = islandAngleRadians;
		this.gateOpened = gateOpened;
		this.entered = entered;
		enteredRaised = false;
		phase = EnterPhase.Turn;
	}

	/// <summary>Abandons the enter sequence (the park could not start): the orbit resumes and the fade eases back out.</summary>
	internal void CancelEnter()
	{
		phase = EnterPhase.None;
		FadeTarget = 0;
	}

	/// <summary>Whether the enter sequence is running (the lobby's UI is hidden meanwhile).</summary>
	public bool Entering => phase != EnterPhase.None;

	public LobbyCameraMode()
	{
		// [BIN:TPW-EXE:0x5d8cb0 lobby projection] the camera method builds the scene projection with the same builder as the park (0x578be0: near 0.1, far 1000, angle [this+4] = ISLANDFOV), so the view-projection is multiplied by the screen matrix of 0x5781d0 as well: the visible half height at unit depth is 0.5 x tan(ISLANDFOV / 2), not tan(ISLANDFOV / 2)
		UsesOriginalProjection = true;
		target = Target;
		eye = DesiredEye();
		Apply();
	}

	/// <summary>The current orbit angle in radians, in [0, 2π).</summary>
	internal float Angle => angle;
	internal Vector3 Eye => eye;
	internal Vector3 LookTarget => target;

	// [EXT:frame-driven-at-60hz] the camera steps once per 60 Hz reference frame, where the original steps once per rendered frame (0x5d5c50 -> 0x5e0470), so the glide and the fade, which are shares per frame, converge at the same speed on every display
	private readonly ReferenceFrameClock frames = new();

	public override void Update()
	{
		for ( var count = frames.Advance( Time.Delta ); count > 0; count-- )
			Advance( ReferenceFrameClock.FrameSeconds );
	}

	/// <summary>Advances the camera by one frame of <paramref name="seconds"/>.</summary>
	internal void Advance( float seconds )
	{
		var step = Math.Clamp( seconds * 1000f, 0f, MaximumStepMilliseconds ) * StepPerMillisecond;
		if ( phase == EnterPhase.None )
		{
			radius = Radius;
			height = VerticalOffset;
			// [BIN:TPW-EXE:0x5e0727 lobby spin] the angle advances by SPINSPEED radians per step and wraps into [0, 2π)
			angle += SpinSpeed * step;
			angle %= MathF.Tau;
			if ( angle < 0 )
				angle += MathF.Tau;
		}
		else
			StepEnter( step );
		// [BIN:TPW-EXE:0x5d8cf2 lobby fade] each frame the current fade moves an eighth of the way (arithmetic shift) to its target
		FadeCurrent += (FadeTarget - FadeCurrent) >> 3;
		// [BIN:TPW-EXE:0x5e1210 lobby eye] the eye's goal is the island's target plus (sin a × SPINRADIUS, VERTICALOFFSET up, cos a × SPINRADIUS)
		// the 500 ms cap keeps both shares at most the whole way (0.5 and 1)
		eye += (DesiredEye() - eye) * (EyeGlidePerStep * step);
		target += (Target - target) * (TargetGlidePerStep * step);
		Apply();
	}

	/// <summary>
	/// One frame of the enter sequence, tp.exe 0x5e0490 states 1 and 2. State 1 re-reads the orbit from the lobby, finds the angle's distance
	/// to the island's angle + π (both in [0, 2π)), and steps it by 0.05 × step toward it the short way round; within half a step the gate opens
	/// and state 2 begins. State 2 holds the angle at the island's angle + π, shrinks the radius by 0.07 × step × radius and the height by
	/// 0.6 × step × height, and fades to black by how far the radius has closed from SPINRADIUS to 8; below 8 the fade is full and the park starts.
	/// </summary>
	private void StepEnter( float step )
	{
		if ( phase == EnterPhase.Turn )
		{
			radius = Radius;
			height = VerticalOffset;
			var goal = Wrap( islandAngle + MathF.PI );
			var distance = Wrap( angle - goal );
			var amount = step * 0.05f;
			if ( distance < amount * 0.5f || MathF.Tau - amount * 0.5f < distance )
			{
				gateOpened?.Invoke();
				phase = EnterPhase.Zoom;
			}
			else if ( distance >= MathF.PI )
				angle = goal + distance + amount;
			else
				angle = goal + (distance - amount);
		}
		else
		{
			angle = islandAngle + MathF.PI;
			radius -= step * radius * 0.07f;
			height -= step * height * 0.6f;
			if ( radius >= 8f )
			{
				var span = Radius - 8f;
				if ( span == 0f )
					span = 1f;
				FadeTarget = (int)((1f - (radius - 8f) / span) * 65536f);
			}
			else
			{
				FadeTarget = 0x10000;
				if ( !enteredRaised )
				{
					enteredRaised = true;
					entered?.Invoke();
				}
			}
		}
	}

	private static float Wrap( float value )
	{
		while ( value >= MathF.Tau )
			value -= MathF.Tau;
		while ( value < 0f )
			value += MathF.Tau;
		return value;
	}

	private Vector3 DesiredEye() => Target + new Vector3( MathF.Sin( angle ) * radius, MathF.Cos( angle ) * radius, height );

	private void Apply()
	{
		// [BIN:TPW-EXE:0x5e13ed island field of view] the island view sets the camera's field of view to ISLANDFOV (lobby +4); the projection 0x578be0
		// and the view gamut 0x56b790 use it as the vertical angle (the gamut's x extent is tan(f/2) divided by height/width, [0x8bcbcc] from 0x582eb0)
		FieldOfView = IslandFieldOfView;
		Position = eye;
		Rotation = Rotation.LookAt( target - eye );
	}
}
