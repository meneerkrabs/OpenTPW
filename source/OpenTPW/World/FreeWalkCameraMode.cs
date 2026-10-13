namespace OpenTPW;

/// <summary>Host terrain inspection camera. No object collision, photography or recording.</summary>
// [APPROX:UI-932] terrain-sampled eye height, movement and look tuning are host choices, not native camcorder reconstruction.
public sealed class FreeWalkCameraMode : CameraMode
{
	public const float EyeHeight = 1.6f, MoveSpeed = 4f, TurnSpeed = 90f;
	private readonly ModelHeightfield field;
	private bool dragging;
	public float Yaw { get; private set; }
	public float Pitch { get; private set; }
	public bool UiCapturesMouse { get; set; }
	public bool UiCapturesKeyboard { get; set; }
	public bool ModalOpen { get; set; }

	internal FreeWalkCameraMode( ModelHeightfield field, Vector3 ground, Vector3 heading )
	{
		this.field = field;
		Position = ground + Vector3.Up * EyeHeight;
		Yaw = float.IsFinite( heading.X ) && float.IsFinite( heading.Y ) && heading.X * heading.X + heading.Y * heading.Y > 0
			? MathF.Atan2( heading.Y, heading.X ).RadiansToDegrees() : 0;
		FieldOfView = 55;
		Rotation = Rotation.From( 0, Yaw, 0 );
	}

	internal static bool TryGround( ModelHeightfield field, float x, float y, out Vector3 ground )
	{
		ground = default;
		if ( !OriginalParkPlacement.TryGetCell( field, x, y, out var cx, out var cy ) || field.IsHole( cx, cy ) ) return false;
		var origin = OriginalParkPlacement.GetOrigin( field );
		ground = GuestRenderer.CellToEngine( field, (x + origin.X) / (field.CellSizeX * OriginalParkPlacement.ModelScale),
			(y + origin.Y) / (field.CellSizeZ * OriginalParkPlacement.ModelScale) );
		return float.IsFinite( ground.X ) && float.IsFinite( ground.Y ) && float.IsFinite( ground.Z );
	}

	public override void Update()
	{
		var io = ImGuiNET.ImGui.GetIO();
		Update( new CameraControlInput
		{
			HasFocus = Window.Current?.SdlWindow.Focused == true, LogicalSize = Screen.Size,
			MousePosition = Input.Mouse.Position, MouseDelta = Input.Mouse.Delta, RightButton = Input.Mouse.Right,
			CaptureMouse = io.WantCaptureMouse || UiCapturesMouse, CaptureKeyboard = io.WantCaptureKeyboard || UiCapturesKeyboard,
			TextEntry = io.WantTextInput || Input.TextEntryActive, Modal = ModalOpen,
			Forward = Input.Forward, Right = Input.Right,
			RotateLeft = Input.Down( InputButton.RotateLeft ), RotateRight = Input.Down( InputButton.RotateRight ), DeltaSeconds = Time.Delta
		} );
	}

	/// <summary>Native-free controls: WASD move, rotation keys turn, RMB drag looks. Engine Z is up.</summary>
	public void Update( CameraControlInput input )
	{
		var inside = input.LogicalSize.X > 0 && input.LogicalSize.Y > 0 && input.MousePosition.X >= 0 && input.MousePosition.Y >= 0
			&& input.MousePosition.X < input.LogicalSize.X && input.MousePosition.Y < input.LogicalSize.Y;
		if ( !input.HasFocus || input.Modal || !inside ) { dragging = false; return; }
		var dt = float.IsFinite( input.DeltaSeconds ) ? Math.Clamp( input.DeltaSeconds, 0, .1f ) : 0;
		var keyboard = !input.CaptureKeyboard && !input.TextEntry;
		if ( keyboard ) Yaw += ((input.RotateLeft ? 1 : 0) - (input.RotateRight ? 1 : 0)) * TurnSpeed * dt;
		var mouse = !input.CaptureMouse && !input.TextEntry;
		if ( mouse && input.RightButton && dragging && float.IsFinite( input.MouseDelta.X ) && float.IsFinite( input.MouseDelta.Y ) )
		{
			Yaw += (float)Math.Clamp( (double)input.MouseDelta.X / input.LogicalSize.X * 180, -180, 180 );
			Pitch = Math.Clamp( Pitch - (float)Math.Clamp( (double)input.MouseDelta.Y / input.LogicalSize.Y * 180, -180, 180 ), -80, 80 );
		}
		dragging = mouse && input.RightButton;
		Yaw = MathF.IEEERemainder( Yaw, 360 );
		Rotation = Rotation.From( Pitch, Yaw, 0 );
		var flat = Rotation.From( 0, Yaw, 0 );
		var f = keyboard && float.IsFinite( input.Forward ) ? Math.Clamp( input.Forward, -1, 1 ) : 0;
		var r = keyboard && float.IsFinite( input.Right ) ? Math.Clamp( input.Right, -1, 1 ) : 0;
		var length = MathF.Sqrt( f * f + r * r );
		if ( length > 1 ) { f /= length; r /= length; }
		var next = Position + (flat.Forward * f + flat.Right * r) * MoveSpeed * dt;
		if ( TryGround( field, next.X, next.Y, out var ground ) ) Position = ground + Vector3.Up * EyeHeight;
		if ( !EntityConstructionScope.IsStaging ) GameAudio.UpdateListener( Position.GetSystemVector3(), Rotation.Forward.GetSystemVector3(), field );
	}
}
