namespace OpenTPW;

/// <summary>
/// Camera of the online world. The globe stays still and the camera circles it: yaw about the pole, pitch towards the
/// poles, at a distance that eases between GLOBERADIUSOUT and GLOBERADIUSIN (lobby.txt) as the player zooms. Turning
/// adds to a yaw/pitch velocity that dies away, more gently when zoomed out (docs/ONLINE.md).
/// </summary>
// [BIN:TPW-EXE:0x005DC860 globe camera update] position r·(cos yaw·cos pitch, sin pitch, sin yaw·cos pitch) around the globe (pole axis up), looking at its centre; pitch clamped to ±1.5; r eases towards the zoom state's radius at 0.3; velocities are multiplied by the state's damping and integrated into the angles; zoomed out (rate, step, damping) = (0.4, 0.15, 0.5), zoomed in (0.2, 0.075, 0.25); field of view 60 (0x005DCFE0)
public class GlobeCameraMode : CameraMode
{
	// [APPROX:ONLINE-007] the original applies its rates, steps and damping per game tick; OpenTPW assumes 30 ticks per second — evidence needed: the tick length of the online world's update
	public const float TicksPerSecond = 30f;
	public const float PitchLimit = 1.5f;
	private const float RadiusEase = 0.3f;

	/// <summary>Camera distance zoomed out and in (GLOBERADIUSOUT, GLOBERADIUSIN).</summary>
	public static float RadiusOut { get; set; } = 475f;
	public static float RadiusIn { get; set; } = 200f;
	public static bool ZoomedIn { get; private set; }

	// [APPROX:ONLINE-008] the first view faces Europe and the Atlantic from 30° north — evidence needed: the original's starting yaw and pitch
	private static float yaw = 70f * MathF.PI / 180f;
	private static float pitch = 0.5f;
	private static float radius = 475f;
	private static float yawVelocity;
	private static float pitchVelocity;

	public GlobeCameraMode()
	{
		FieldOfView = 60;
		Update();
	}

	private static (float Rate, float Step, float Damping) Preset => ZoomedIn ? (0.2f, 0.075f, 0.25f) : (0.4f, 0.15f, 0.5f);

	/// <summary>Starts the online world zoomed out, at rest.</summary>
	public static void Reset()
	{
		ZoomedIn = false;
		radius = RadiusOut;
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
	// [APPROX:ONLINE-009] edge and right-button steering add rate × offset × step per tick to the velocity; the original's exact edge-depth and right-button scaling are not decoded — evidence needed: the input branches of 0x005DC860
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
		pitch = Math.Clamp( pitch + pitchVelocity * ticks, -PitchLimit, PitchLimit );
		yawVelocity *= damping;
		pitchVelocity *= damping;
		radius += ((ZoomedIn ? RadiusIn : RadiusOut) - radius) * (1 - MathF.Pow( 1 - RadiusEase, ticks ));
		// MD2 (x, y-up, z) is engine (x, z, y-up), so the pole is engine Z.
		Position = GlobeScene.Centre + new Vector3( MathF.Cos( yaw ) * MathF.Cos( pitch ), MathF.Sin( yaw ) * MathF.Cos( pitch ), MathF.Sin( pitch ) ) * radius;
		Rotation = Rotation.LookAt( GlobeScene.Centre - Position );
	}
}
