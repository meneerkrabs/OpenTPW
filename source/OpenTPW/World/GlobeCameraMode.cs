namespace OpenTPW;

/// <summary>
/// Camera of the online world. The globe stays still and the camera circles it: yaw about the pole, pitch towards the
/// poles, at a distance that eases between GLOBERADIUSOUT and GLOBERADIUSIN (lobby.txt) as the player zooms. Turning
/// adds to a yaw/pitch velocity that dies away, more gently when zoomed out (docs/ONLINE.md).
/// </summary>
// [BIN:TPW-EXE:0x005DC860 globe camera update] position r·(cos yaw·cos pitch, sin pitch, sin yaw·cos pitch) around the globe (pole axis up), looking at its centre; pitch clamped to ±1.5; r eases towards the zoom state's radius at 0.3; velocities are multiplied by the state's damping and integrated into the angles; zoomed out (rate, step, damping) = (0.4, 0.15, 0.5), zoomed in (0.2, 0.075, 0.25); field of view 60 (0x005DCFE0)
public class GlobeCameraMode : CameraMode
{
	// [EXT:online-globe-tick] OpenTPW turns the globe on a fixed 30 ticks per second: rates, steps, damping and the radius ease
	// apply per tick and the angles move by velocity × ticks, so the turn does not depend on the frame rate. The original's
	// per-frame update is not reproduced: the online front end updates the globe once a frame (tp.exe 0x54E360 state 2
	// → 0x5D5C50 → 0x5DC860) with dt = min(elapsed ms, 500) × 0.01 (0x5D5C50), damps the velocities once a frame and then moves the
	// angles by velocity × dt and eases the radius by 0.3 × dt; still unknown: its frame rate on the online screens (the per-frame
	// damping and steering make its turning depend on it) and how often a held panel arrow sends its step (event
	// 0x10008/0x10009, 0x4C53F0)
	public const float TicksPerSecond = 30f;
	public const float PitchLimit = 1.5f;
	private const float RadiusEase = 0.3f;

	/// <summary>Camera distance zoomed out and in (GLOBERADIUSOUT, GLOBERADIUSIN).</summary>
	public static float RadiusOut { get; set; } = 475f;
	public static float RadiusIn { get; set; } = 200f;
	public static bool ZoomedIn { get; private set; }

	private static float yaw;
	private static float pitch;
	private static float radius = 475f;
	private static float yawVelocity;
	private static float pitchVelocity;

	public GlobeCameraMode()
	{
		FieldOfView = 60;
		// [BIN:TPW-EXE:0x5dd034 globe projection] the globe's camera is the lobby's object (0xf83778): its angle 60.0 goes through the same camera method 0x5d8cb0 into the park's projection builder (near 0.1, far 1000), so the screen matrix applies here too: the visible half height at unit depth is 0.5 x tan(30 degrees), and the sphere of radius 116 seen from 475 fills nearly the whole height
		UsesOriginalProjection = true;
		Update();
	}

	private static (float Rate, float Step, float Damping) Preset => ZoomedIn ? (0.2f, 0.075f, 0.25f) : (0.4f, 0.15f, 0.5f);

	/// <summary>Starts the globe view zoomed out, at rest, facing yaw 0 and pitch 0 (60° W on the equator).</summary>
	// [BIN:TPW-EXE:0x005DCFE0 globe activate] each activation of a globe screen (0x005DD650 online world, 0x005DD910 publish globe, also on the
	// way back from a city lobby, 0x004B86A0 control 0x49D7) sets yaw, pitch and both velocities to 0, the radius to GLOBERADIUSOUT and the
	// zoomed-out preset
	public static void Reset()
	{
		ZoomedIn = false;
		radius = RadiusOut;
		yaw = pitch = 0;
		yawVelocity = pitchVelocity = 0;
	}

	/// <summary>One press of a rotate button or arrow key: adds the zoom state's step to the turn.</summary>
	public static void Turn( int yawSteps, int pitchSteps )
	{
		yawVelocity += Preset.Step * yawSteps;
		pitchVelocity += Preset.Step * pitchSteps;
	}

	/// <summary>
	/// Continuous steering for this frame, -1..1 per axis (cursor at the screen edge, or the right button held off
	/// centre), scaled by the zoom state's rate.
	/// </summary>
	// [EXT:online-globe-steering] edge and right-button steering add rate × offset × step per tick (the fixed tick above) to the
	// velocity. Not reproduced: the original (0x005DC860) adds, once a frame and without the step, rate × 50 × the cursor's depth
	// into the 1% edge band (x = X / 2048 and y = Y / 1536 clamped to 0..1; the depth 0.01 − x below 0.01, x − 0.99 above 0.99, so
	// at most rate / 2) or, with the right button held, rate × the offset from the centre ((X − 1024) / 1024, −(Y − 768) / 768);
	// those increments need the original's per-frame integration and its frame rate on the online screens, which is unknown
	public static void Steer( float yawAmount, float pitchAmount, float delta )
	{
		var ticks = Math.Min( delta, 0.1f ) * TicksPerSecond;
		yawVelocity += Preset.Rate * Preset.Step * yawAmount * ticks;
		pitchVelocity += Preset.Rate * Preset.Step * pitchAmount * ticks;
	}

	public static void ToggleZoom() => ZoomedIn = !ZoomedIn;

	public override void Update()
	{
		var ticks = Math.Min( Time.Delta, 0.1f ) * TicksPerSecond;
		var damping = MathF.Pow( Preset.Damping, ticks );
		yaw += yawVelocity * ticks;
		pitch += pitchVelocity * ticks;
		// [BIN:TPW-EXE:0x005DCD47 globe pitch limit] a pitch past ±1.5 is set to the limit and its velocity to 0
		if ( MathF.Abs( pitch ) > PitchLimit )
		{
			pitch = Math.Clamp( pitch, -PitchLimit, PitchLimit );
			pitchVelocity = 0;
		}
		yawVelocity *= damping;
		pitchVelocity *= damping;
		radius += ((ZoomedIn ? RadiusIn : RadiusOut) - radius) * (1 - MathF.Pow( 1 - RadiusEase, ticks ));
		// MD2 (x, y-up, z) is engine (x, z, y-up), so the pole is engine Z.
		Position = GlobeScene.Centre + new Vector3( MathF.Cos( yaw ) * MathF.Cos( pitch ), MathF.Sin( yaw ) * MathF.Cos( pitch ), MathF.Sin( pitch ) ) * radius;
		Rotation = Rotation.LookAt( GlobeScene.Centre - Position );
	}
}
