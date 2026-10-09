namespace OpenTPW;

/// <summary>
/// Front-end lobby camera: orbits the selected island at the lobby's SPINRADIUS and VERTICALOFFSET
/// (lobby.txt) and glides to a newly selected island. SPINSPEED (0.02) is read as radians per tenth
/// of a second and the field of view is an OpenTPW choice: neither unit is evidenced (docs/UI.md).
/// </summary>
public class LobbyCameraMode : CameraMode
{
	public static float Radius { get; set; } = 70f;
	public static float VerticalOffset { get; set; } = 20f;
	public static float SpinSpeed { get; set; } = 0.02f;
	/// <summary>Island the camera orbits; changed by the front end.</summary>
	public static Vector3 Target { get; set; } = new Vector3( 400, 400, 12.5f );

	private Vector3 current = Target;
	private float angle;

	public LobbyCameraMode()
	{
		FieldOfView = 60;
		Update();
	}

	// [APPROX:UI-017] SPINSPEED as radians per 0.1 s, FOV 60, 3/s glide — evidence needed: binary or capture of the lobby camera
	public override void Update()
	{
		angle += SpinSpeed * 10 * Math.Min( Time.Delta, 0.1f );
		var blend = 1 - MathF.Exp( -Math.Min( Time.Delta, 0.1f ) * 3 );
		current = current + (Target - current) * blend;
		var offset = new Vector3( MathF.Sin( angle ) * Radius, MathF.Cos( angle ) * Radius, VerticalOffset );
		Position = current + offset;
		Rotation = Rotation.LookAt( current - Position );
	}
}
