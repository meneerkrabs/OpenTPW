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
	private bool middleTurning, middleWasDown;
	private readonly ReferenceFrameClock turnFrames = new();

	public ParkCameraMode()
	{
		// [APPROX:UI-101] the park camera's field of view is 55 degrees where tp.exe's projection has 90 (0x42A990 and 0x54E86F -> 0x578BE0); the pose below (height, yaw, pull-back) is OpenTPW's own and was tuned to 55, so the value is not changed here until the original view is captured
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

	/// <summary>Places the camera at a fixed pose (screenshots): ground target, height and yaw in degrees.</summary>
	public void SetPose( Vector3 target, float height, float yaw )
	{
		Height = float.IsFinite( height ) ? Math.Clamp( height, 12, MaximumHeight ) : Height;
		Yaw = float.IsFinite( yaw ) ? yaw % 360 : Yaw;
		Focus( target );
	}

	/// <summary>
	/// Turns the camera to look along the ground direction <paramref name="direction"/> (engine X/Y), keeping the target and the height: the eye goes behind the
	/// target, <c>Position = Target + (cos yaw, sin yaw) × Height</c>. A zero or non-finite direction changes nothing.
	/// </summary>
	public void LookAlong( System.Numerics.Vector2 direction )
	{
		if ( !float.IsFinite( direction.X ) || !float.IsFinite( direction.Y ) || direction.LengthSquared() < 1e-12f )
			return;
		Yaw = MathF.Atan2( -direction.Y, -direction.X ) * 180f / MathF.PI;
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
			HasFocus = Window.Current?.Focused == true,
			LogicalSize = Screen.Size, MousePosition = Input.Mouse.Position, MouseDelta = Input.Mouse.Delta,
			CaptureMouse = io.WantCaptureMouse || Level.Current?.UiCapturesMouse == true,
			CaptureKeyboard = io.WantCaptureKeyboard || UiCapturesKeyboard,
			TextEntry = io.WantTextInput || Input.TextEntryActive, Modal = ModalOpen,
			RightButton = Input.Mouse.Right, MiddleButton = Input.Mouse.Middle, Wheel = Input.Mouse.Wheel,
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
		if ( !input.HasFocus || input.Modal || !inside ) { dragging = false; middleTurning = false; middleWasDown = input.MiddleButton; turnFrames.Reset(); return; }
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
		Yaw = (Yaw + MiddleTurn( input, dt )) % 360;
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

	/// <summary>
	/// The yaw change, in degrees, of the middle-button turn for this frame: zero unless the middle button went down over the free view and is still held.
	/// </summary>
	// [BIN:TPW-EXE:0x42a760 park camera mouse turn] the button message 0x10005 with button 2 (DirectInput button 2, the middle button; 0 is left, 1 right) sets the mode bit 2 of 0x790aac, 0x10004 clears it, and the leave/focus messages 0x20 and 0x22 clear it too; while it is set every message 0x1e (once per drawn frame) adds (pointer X in whole pixels x 2048 / width - 1024) / 2048 = X / width - 0.5 to the yaw 0x790a38, in radians (the arrow keys turn it by 0.002 x frame milliseconds, 0x42b1c0)
	// [BIN:TPW-EXE:0x42b1c0 yaw sign] the Left arrow (key 6) and numpad 9 (0x12) add to the yaw, the Right arrow (7) and numpad 7 (0x13) subtract, so a pointer right of the centre turns like the Left arrow: OpenTPW's yaw (degrees, rotate left = less) goes down
	// [EXT:frame-driven-at-60hz] the original steps once per drawn frame; OpenTPW takes the elapsed time as that many 60 Hz reference frames
	private float MiddleTurn( CameraControlInput input, float dt )
	{
		if ( !input.MiddleButton )
		{
			middleWasDown = false;
			middleTurning = false;
			turnFrames.Reset();
			return 0;
		}
		var pressed = !middleWasDown;
		middleWasDown = true;
		if ( pressed && !input.CaptureMouse && !input.TextEntry )
			middleTurning = true;
		if ( !middleTurning )
			return 0;
		return -MiddleTurnRadians( input.MousePosition.X, input.LogicalSize.X ) * turnFrames.Advance( dt ) * 180f / MathF.PI;
	}

	/// <summary>The yaw added by one message 0x1e while the middle button is held, in radians: the pointer's whole-pixel X over the width, less one half.</summary>
	public static float MiddleTurnRadians( float pointerX, int width ) => width <= 0 ? 0 : MathF.Floor( pointerX ) / width - 0.5f;

	private static float Edge( float position, int size ) => position < size * HostEdgeFraction ? -1 : position >= size * (1 - HostEdgeFraction) ? 1 : 0;
	private void UpdateTransform()
	{
		var radians = Yaw.DegreesToRadians();
		Position = Target + new Vector3( MathF.Cos( radians ) * Height, MathF.Sin( radians ) * Height, Height );
		Rotation = Rotation.LookAt( Target - Position );
		if ( !EntityConstructionScope.IsStaging ) UpdateAudioListener();
	}

	internal void UpdateAudioListener()
	{
		GameAudio.UpdateListener( GameAudio.ListenerPlace( Position.GetSystemVector3(), Target.GetSystemVector3(), eyeMode: false ),
			(Target - Position).GetSystemVector3(), Level.Current?.OriginalPark?.Heightfield );
	}
}
