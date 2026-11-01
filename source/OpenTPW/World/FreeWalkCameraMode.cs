using Veldrid;

namespace OpenTPW;

/// <summary>
/// The camcorder's walk (the original's camera mode 2): the eye stands 5 original units above the ground and walks over the park's cells as the camera update <c>0x42B1C0</c> moves it
/// (<see cref="CamcorderWalk"/>); an entrance cell of a ride it reaches takes the walk into that ride (<see cref="PendingRide"/>).
/// </summary>
// [BIN:TPW-EXE:0x42AE70 camcorder walk] the walk enters from the camcorder tool's click (0x46D0D0): the camera mode 2 (0x42AB20) at the hover point (0x7A1C70, 0x7A1C78) with the park camera's yaw (0x790A38) and zoom value
// [BIN:TPW-EXE:0x42B1C0 camcorder keys] the Left key or numpad 9 turn left, the Right key or numpad 7 right, the Up key or numpad 1 walk forward and the Down key or numpad 3 back; the right mouse button held walks forward; the
// pointer steers (outside 0.4 across it turns, 0.2 up and -0.4 down it pitches, all in half sizes of the view); the Home key and numpad 5 put the walker back at the park's entrance
public sealed class FreeWalkCameraMode : CameraMode
{
	private readonly ModelHeightfield field;
	private readonly CamcorderWalk walk = new();
	private readonly CamcorderWalkWorld world;
	private bool keypadHomeDown;
	/// <summary>The park camera's zoom value the walk's speed follows (0x7909EC); the Home key sets it to 110.</summary>
	public float Zoom { get; private set; }
	public bool UiCapturesMouse { get; set; }
	public bool UiCapturesKeyboard { get; set; }
	public bool ModalOpen { get; set; }

	/// <summary>The ride whose entrance cell the walk reached, to be boarded by the level; cleared by <see cref="TakePendingRide"/>.</summary>
	public OriginalObject? PendingRide { get; private set; }

	/// <param name="field">The park's heightfield.</param>
	/// <param name="ground">The engine point the walk starts at.</param>
	/// <param name="heading">The direction the view looked along (engine axes): the walk's yaw is its ground direction.</param>
	/// <param name="blocked">The walk edge test with mask 2 (<c>0x4D8750</c>), none for a park without cells.</param>
	/// <param name="rideAt">The ride whose entrance lies in a cell (<c>0x42A340</c>).</param>
	/// <param name="zoom">The park camera's zoom value.</param>
	internal FreeWalkCameraMode( ModelHeightfield field, Vector3 ground, Vector3 heading, Func<int, int, int, bool>? blocked = null, Func<int, int, OriginalObject?>? rideAt = null, float zoom = ParkCameraMode.HomeHeight )
	{
		this.field = field;
		world = new CamcorderWalkWorld( field, blocked ?? ( ( _, _, _ ) => false ), rideAt ?? ( ( _, _ ) => null ) );
		Zoom = zoom;
		var (cellsX, cellsY) = OriginalParkPlacement.ToCells( field, ground.X, ground.Y );
		walk.X = cellsX * field.CellSizeX;
		walk.Z = cellsY * field.CellSizeZ;
		walk.Yaw = float.IsFinite( heading.X ) && float.IsFinite( heading.Y ) && heading.X * heading.X + heading.Y * heading.Y > 0 ? MathF.Atan2( heading.X, heading.Y ) : 0f;
		// [BIN:TPW-EXE:0x42A990 walk projection] the walk view is the camcorder mode of the same camera, so it shares the one projection
		FieldOfView = OriginalProjection.FieldOfView;
		UsesOriginalProjection = true;
		Place();
	}

	/// <summary>The walker's position and yaw in original units and radians (0x7908F0, 0x7908F8, 0x790A38).</summary>
	internal CamcorderWalk Walk => walk;

	/// <summary>The yaw in radians; forward on the ground is (sin, cos) in the original's x and z.</summary>
	public float Yaw => walk.Yaw;

	/// <summary>The look pitch in radians, positive looking down.</summary>
	public float Pitch => walk.Pitch;

	/// <summary>The cell the walker stands in (the original's cell of 0x7908F0 and 0x7908F8: truncated tenths).</summary>
	internal (int X, int Y) Cell => ((int)(walk.X * 0.1f), (int)(walk.Z * 0.1f));

	internal static bool TryGround( ModelHeightfield field, float x, float y, out Vector3 ground )
	{
		ground = default;
		if ( !OriginalParkPlacement.TryGetCell( field, x, y, out var cx, out var cy ) || field.IsHole( cx, cy ) ) return false;
		var origin = OriginalParkPlacement.GetOrigin( field );
		ground = GuestRenderer.CellToEngine( field, (x + origin.X) / (field.CellSizeX * OriginalParkPlacement.ModelScale),
			(y + origin.Y) / (field.CellSizeZ * OriginalParkPlacement.ModelScale) );
		return float.IsFinite( ground.X ) && float.IsFinite( ground.Y ) && float.IsFinite( ground.Z );
	}

	/// <summary>The ride the walk reached, once.</summary>
	internal OriginalObject? TakePendingRide()
	{
		var ride = PendingRide;
		PendingRide = null;
		return ride;
	}

	/// <summary>Puts the walker at a cell's centre looking along a yaw: where a ride it went on leaves it (<c>0x42A1D0</c>).</summary>
	internal void PlaceAt( float x, float z, float? yaw )
	{
		walk.X = x;
		walk.Z = z;
		if ( yaw is { } turned )
			walk.Yaw = turned;
		Place();
	}

	public override void Update()
	{
		var io = ImGuiNET.ImGui.GetIO();
		Update( new CameraControlInput
		{
			HasFocus = Window.Current?.Focused == true, LogicalSize = Screen.Size,
			MousePosition = Input.Mouse.Position, MouseDelta = Input.Mouse.Delta, RightButton = Input.Mouse.Right,
			CaptureMouse = io.WantCaptureMouse || UiCapturesMouse, CaptureKeyboard = io.WantCaptureKeyboard || UiCapturesKeyboard,
			TextEntry = io.WantTextInput || Input.TextEntryActive, Modal = ModalOpen,
			RotateLeft = Input.Down( InputButton.RotateLeft ) || Input.IsFrameKeyDown( Key.Keypad9 ), RotateRight = Input.Down( InputButton.RotateRight ) || Input.IsFrameKeyDown( Key.Keypad7 ),
			Zoom = (Input.Down( InputButton.ZoomIn ) || Input.IsFrameKeyDown( Key.Keypad1 ) ? 1 : 0) - (Input.Down( InputButton.ZoomOut ) || Input.IsFrameKeyDown( Key.Keypad3 ) ? 1 : 0),
			Home = Input.Pressed( InputButton.Home ) || KeypadHomePressed(), DeltaSeconds = Time.Delta
		} );
	}

	private bool KeypadHomePressed()
	{
		var down = Input.IsFrameKeyDown( Key.Keypad5 );
		var pressed = down && !keypadHomeDown;
		keypadHomeDown = down;
		return pressed;
	}

	/// <summary>
	/// One frame without SDL, ImGui or renderer dependencies. The keys are the original's (<see cref="CameraControlInput.RotateLeft"/> and its pair turn, <see cref="CameraControlInput.Zoom"/> walks: up is
	/// forward), the pointer steers and the right button walks. Engine Z is up.
	/// </summary>
	public void Update( CameraControlInput input )
	{
		var inside = input.LogicalSize.X > 0 && input.LogicalSize.Y > 0 && input.MousePosition.X >= 0 && input.MousePosition.Y >= 0
			&& input.MousePosition.X < input.LogicalSize.X && input.MousePosition.Y < input.LogicalSize.Y;
		if ( !input.HasFocus || input.Modal || !inside )
			return;
		var keyboard = !input.CaptureKeyboard && !input.TextEntry;
		var mouse = !input.CaptureMouse && !input.TextEntry;
		var pointer = mouse ? RideCameraMode.PointerOffset( input.MousePosition.X, input.MousePosition.Y, input.LogicalSize.X, input.LogicalSize.Y ) : System.Numerics.Vector2.Zero;
		var keys = new CamcorderWalkInput
		{
			TurnLeft = keyboard && input.RotateLeft, TurnRight = keyboard && input.RotateRight, Forward = keyboard && input.Zoom > 0, Backward = keyboard && input.Zoom < 0,
			Button = mouse && input.RightButton, Home = keyboard && input.Home, PointerX = pointer.X, PointerY = pointer.Y
		};
		if ( keys.Home )
			Zoom = ParkCameraMode.HomeHeight;
		var milliseconds = CamcorderWalk.FrameMilliseconds( float.IsFinite( input.DeltaSeconds ) ? input.DeltaSeconds * 1000f : 0f );
		if ( walk.Step( milliseconds, in keys, Zoom, world ) is OriginalObject ride )
			PendingRide = ride;
		Place();
	}

	/// <summary>The eye and the view the walker's state gives: at the position and the ground's height plus 5, looking along the yaw and the pitch.</summary>
	private void Place()
	{
		walk.EyeY = world.Height( walk.X, walk.Z ) + CamcorderWalk.EyeAboveGround;
		var eye = OriginalParkPlacement.ToEngine( field, new System.Numerics.Vector3( walk.X, walk.EyeY, walk.Z ) );
		Position = new Vector3( eye.X, eye.Y, eye.Z );
		var (x, y, z) = walk.Forward;
		// the original's axes (x, y up, z) are the engine's (x, z, y)
		Rotation = Rotation.LookAt( new Vector3( x, z, y ) );
		if ( !EntityConstructionScope.IsStaging )
			GameAudio.UpdateListener( Position.GetSystemVector3(), Rotation.Forward.GetSystemVector3(), field, Rotation.Up.GetSystemVector3() );
	}
}
