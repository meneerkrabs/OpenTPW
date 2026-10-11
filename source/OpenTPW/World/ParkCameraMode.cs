namespace OpenTPW;

public class ParkCameraMode : CameraMode
{
	public const float SandboxTargetExtent = 32f;
	public static float TargetExtent { get; set; } = SandboxTargetExtent;
	public static Vector3 InitialTarget { get; set; } = Vector3.Zero;
	// Host interaction tuning, not reconstructed original-game speed constants.
	public const float HostPanSpeed = 24f;
	public const float HostRotationSpeed = 90f;
	public const float HostZoomSpeed = 24f;
	public const float HostEdgeFraction = 0.015f;
	public bool UiCapturesKeyboard { get; set; }
	public bool ModalOpen { get; set; }
	public Vector3 Target { get; private set; }
	public float Height { get; private set; } = 42f;
	public float Yaw { get; private set; } = -135f;
	public ParkCameraView View { get; private set; }
	private bool dragging;

	public ParkCameraMode()
	{
		FieldOfView = 55;
		Focus( InitialTarget );
	}

	/// <summary>Focus on park coordinates: X/Y ground plane, Z up. Targets stay on the ground.</summary>
	public void Focus( Vector3 target )
	{
		var extent = float.IsFinite( TargetExtent ) ? Math.Max( 0, TargetExtent ) : SandboxTargetExtent;
		Target = new Vector3( float.IsFinite( target.X ) ? Math.Clamp( target.X, -extent, extent ) : 0,
			float.IsFinite( target.Y ) ? Math.Clamp( target.Y, -extent, extent ) : 0, 0 );
		UpdateTransform();
	}

	public ParkCameraView CycleView()
	{
		View = (ParkCameraView)(((int)View + 1) % 3);
		Height = View switch { ParkCameraView.Close => 20f, ParkCameraView.Overview => MaximumHeight, _ => 42f };
		UpdateTransform();
		return View;
	}

	private float MaximumHeight => Math.Max( 100, float.IsFinite( TargetExtent ) ? TargetExtent * 2 : 100 );

	public override void Update()
	{
		var io = ImGuiNET.ImGui.GetIO();
		Update( new CameraControlInput
		{
			HasFocus = Window.Current?.SdlWindow.Focused == true,
			LogicalSize = Screen.Size, MousePosition = Input.Mouse.Position, MouseDelta = Input.Mouse.Delta,
			CaptureMouse = io.WantCaptureMouse || Level.Current?.UiCapturesMouse == true,
			CaptureKeyboard = io.WantCaptureKeyboard || UiCapturesKeyboard,
			TextEntry = io.WantTextInput || Input.TextEntryActive, Modal = ModalOpen,
			RightButton = Input.Mouse.Right, Wheel = Input.Mouse.Wheel,
			Forward = Input.Forward, Right = Input.Right + (Input.Down( InputButton.ScrollRight ) ? 1 : 0) - (Input.Down( InputButton.ScrollLeft ) ? 1 : 0),
			RotateLeft = Input.Down( InputButton.RotateLeft ), RotateRight = Input.Down( InputButton.RotateRight ),
			RotateLeftPressed = Input.Pressed( InputButton.RotateLeft ), RotateRightPressed = Input.Pressed( InputButton.RotateRight ),
			Zoom = (Input.Down( InputButton.ZoomIn ) ? 1 : 0) - (Input.Down( InputButton.ZoomOut ) ? 1 : 0),
			Home = Input.Pressed( InputButton.Home ), DeltaSeconds = Time.Delta
		}, GameOptions.Current );
	}

	/// <summary>Apply one frame without SDL, ImGui or renderer dependencies.</summary>
	public void Update( CameraControlInput input, GameOptions options )
	{
		var inside = input.LogicalSize.X > 0 && input.LogicalSize.Y > 0
			&& input.MousePosition.X >= 0 && input.MousePosition.Y >= 0
			&& input.MousePosition.X < input.LogicalSize.X && input.MousePosition.Y < input.LogicalSize.Y;
		if ( !input.HasFocus || input.Modal || !inside ) { dragging = false; return; }
		var dt = float.IsFinite( input.DeltaSeconds ) ? Math.Clamp( input.DeltaSeconds, 0, 0.1f ) : 0;
		var keyboard = !input.CaptureKeyboard && !input.TextEntry;
		if ( keyboard )
		{
			if ( input.Home ) { Yaw = -135; Height = 42; View = ParkCameraView.Standard; Focus( InitialTarget ); }
			var rotation = options.Rotation == RotationMode.Smooth
				? ((input.RotateRight ? 1 : 0) - (input.RotateLeft ? 1 : 0)) * HostRotationSpeed * dt
				: ((input.RotateRightPressed ? 1 : 0) - (input.RotateLeftPressed ? 1 : 0)) * 90;
			Yaw = (Yaw + rotation) % 360;
			Height = Math.Clamp( Height - input.Zoom * HostZoomSpeed * dt, 12, MaximumHeight );
		}
		var radians = Yaw.DegreesToRadians();
		var forward = new Vector3( -MathF.Cos( radians ), -MathF.Sin( radians ), 0 );
		var right = new Vector3( forward.Y, -forward.X, 0 );
		float x = keyboard ? input.Right : 0, y = keyboard ? input.Forward : 0;
		var mouse = !input.CaptureMouse && !input.TextEntry;
		if ( mouse )
		{
			Height = Math.Clamp( Height - input.Wheel * 3, 12, MaximumHeight );
			if ( options.Scroll == ScrollMode.Pushscroll )
			{
				x += Edge( input.MousePosition.X, input.LogicalSize.X );
				y -= Edge( input.MousePosition.Y, input.LogicalSize.Y );
			}
			else if ( input.RightButton && dragging )
				Target += (right * input.MouseDelta.X / input.LogicalSize.X - forward * input.MouseDelta.Y / input.LogicalSize.Y) * Height * 2;
		}
		dragging = mouse && options.Scroll == ScrollMode.RightButton && input.RightButton;
		var length = MathF.Sqrt( x * x + y * y );
		if ( length > 1 ) { x /= length; y /= length; }
		Focus( Target + (right * x + forward * y) * dt * HostPanSpeed );
	}

	private static float Edge( float position, int size ) => position < size * HostEdgeFraction ? -1 : position >= size * (1 - HostEdgeFraction) ? 1 : 0;
	private void UpdateTransform()
	{
		var radians = Yaw.DegreesToRadians();
		Position = Target + new Vector3( MathF.Cos( radians ) * Height, MathF.Sin( radians ) * Height, Height );
		Rotation = Rotation.LookAt( Target - Position );
	}
}
