namespace OpenTPW;

public class ParkCameraMode : CameraMode
{
	public const float SandboxTargetExtent = 32f;
	/// <summary>Half-size of the square the camera target may pan over; set by the level before the mode is created.</summary>
	public static float TargetExtent { get; set; } = SandboxTargetExtent;
	/// <summary>Camera target when the mode is created.</summary>
	public static Vector3 InitialTarget { get; set; } = Vector3.Zero;

	private Vector3 target = Vector3.Zero;
	private float height = 42f;
	private float yaw = -135f;

	public ParkCameraMode()
	{
		target = InitialTarget;
		FieldOfView = 55;
		UpdateTransform();
	}

	public override void Update()
	{
		var io = ImGuiNET.ImGui.GetIO();
		var radians = yaw.DegreesToRadians();
		var forward = new Vector3( -MathF.Cos( radians ), -MathF.Sin( radians ), 0 );
		var right = new Vector3( forward.Y, -forward.X, 0 );
		if ( !io.WantCaptureKeyboard )
		{
			target += (forward * Input.Forward + right * Input.Right) * Math.Min( Time.Delta, 0.1f ) * 24f;
			if ( Input.Pressed( InputButton.RotateLeft ) )
				yaw -= 45;
			if ( Input.Pressed( InputButton.RotateRight ) )
				yaw += 45;
		}
		if ( !io.WantCaptureMouse )
		{
			height = Math.Clamp( height - Input.Mouse.Wheel * 3, 12, Math.Max( 100, TargetExtent * 2 ) );
			if ( Input.Mouse.Right )
				target += (right * Input.Mouse.Delta.X - forward * Input.Mouse.Delta.Y) * height / 500f;
		}
		target = new Vector3( Math.Clamp( target.X, -TargetExtent, TargetExtent ), Math.Clamp( target.Y, -TargetExtent, TargetExtent ), 0 );
		UpdateTransform();
	}

	private void UpdateTransform()
	{
		var radians = yaw.DegreesToRadians();
		Position = target + new Vector3( MathF.Cos( radians ) * height, MathF.Sin( radians ) * height, height );
		Rotation = Rotation.LookAt( target - Position );
	}
}
