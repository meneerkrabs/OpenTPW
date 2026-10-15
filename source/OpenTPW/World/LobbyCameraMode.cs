namespace OpenTPW;

/// <summary>
/// Front-end lobby camera, the original's island view (tp.exe 0x5e0490, state 0, and 0x5e1210): the eye orbits
/// the selected island at the lobby's SPINRADIUS and VERTICALOFFSET (lobby.txt) while the angle advances by
/// SPINSPEED per tenth of a second, the eye and the target each move a fixed share of the way to their
/// goals every frame (the glide to a newly selected island), and the field of view is ISLANDFOV.
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

	public LobbyCameraMode()
	{
		target = Target;
		eye = DesiredEye();
		Apply();
	}

	/// <summary>The current orbit angle in radians, in [0, 2π).</summary>
	internal float Angle => angle;
	internal Vector3 Eye => eye;
	internal Vector3 LookTarget => target;

	public override void Update() => Advance( Time.Delta );

	/// <summary>Advances the camera by a frame of <paramref name="seconds"/>.</summary>
	internal void Advance( float seconds )
	{
		var step = Math.Clamp( seconds * 1000f, 0f, MaximumStepMilliseconds ) * StepPerMillisecond;
		// [BIN:TPW-EXE:0x5e0727 lobby spin] the angle advances by SPINSPEED radians per step and wraps into [0, 2π)
		angle += SpinSpeed * step;
		angle %= MathF.Tau;
		if ( angle < 0 )
			angle += MathF.Tau;
		// [BIN:TPW-EXE:0x5e1210 lobby eye] the eye's goal is the island's target plus (sin a × SPINRADIUS, VERTICALOFFSET up, cos a × SPINRADIUS)
		// the 500 ms cap keeps both shares at most the whole way (0.5 and 1)
		eye += (DesiredEye() - eye) * (EyeGlidePerStep * step);
		target += (Target - target) * (TargetGlidePerStep * step);
		Apply();
	}

	private Vector3 DesiredEye() => Target + new Vector3( MathF.Sin( angle ) * Radius, MathF.Cos( angle ) * Radius, VerticalOffset );

	private void Apply()
	{
		// [BIN:TPW-EXE:0x5e13ed island field of view] the island view sets the camera's field of view to ISLANDFOV (lobby +4); the projection 0x578be0
		// and the view gamut 0x56b790 use it as the vertical angle (the gamut's x extent is tan(f/2) divided by height/width, [0x8bcbcc] from 0x582eb0)
		FieldOfView = IslandFieldOfView;
		Position = eye;
		Rotation = Rotation.LookAt( target - eye );
	}
}
