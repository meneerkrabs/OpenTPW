namespace OpenTPW;

public class ParkCameraMode : CameraMode
{
	public const float SandboxTargetExtent = 32f;
	public static float TargetExtent { get; set; } = SandboxTargetExtent;
	public static Vector3 InitialTarget { get; set; } = Vector3.Zero;
	// Host interaction tuning: the edge band and the right-button drag (the original's own pan model is not ported).
	public const float HostEdgeFraction = 0.015f;
	/// <summary>The original's zoom value at its own range's near end (<see cref="Height"/>).</summary>
	// [BIN:TPW-EXE:0x42b1c0 zoom range] without the flag 0x200 the zoom value (0x7909ec) is clamped to 70..130 (0x42c621-0x42c6a1: 70.0 at 0x6fde08, 130.0 at 0x6fde0c; the flag 0x200 swaps in 40..300 from 0x74c9d8/0x74c9dc, a flag toggled by 0x42a520, a key handler of the 25-entry camera key set (0x748158, entry 0x748298: key byte 0x20, modifier word 0x3f, whose reachability in the retail game is not verified); 0x42aa6e only keeps it), so the camera never gets nearer or farther than these
	public const float MinimumHeight = 70f;
	/// <summary>The original's zoom value at its far end.</summary>
	public const float MaximumHeight = 130f;
	/// <summary>The zoom value at the near end of the wide range (<see cref="GameOptions.WideZoom"/>): tp.exe's own flag-0x200 minimum.</summary>
	// [BIN:TPW-EXE:0x42c631 wide zoom range] with the mode flag 0x200 (0x42c62c: TEST BH, 2) the clamp compares the zoom value with 40.0 (0x74c9d8) and 300.0 (0x74c9dc) instead of 70.0 and 130.0; the pitch (0x42c742) and the eye (0x42c96d) are computed from the zoom value alone, so the same formulas run unchanged over the wider range
	// [EXT:wide-zoom] the original sets the flag only through a hidden camera key binding (0x42a520 from entry 0x748298 of the camera key set; key byte 0x20, modifier word 0x3f, not shown to be reachable in retail) and has no option for it; OpenTPW offers that range as an option, on by default (user-approved 2026-10-18, GameOptions.WideZoom), its far end following FIRSTPERSONVIEWDISTANCE (user-decided 2026-10-26, WideMaximumFor)
	public const float WideMinimumHeight = 40f;
	/// <summary>The zoom value at the far end of the wide range: tp.exe's own flag-0x200 maximum.</summary>
	public const float WideMaximumHeight = 300f;
	/// <summary>The zoom value of the Home pose and of a new camera (<c>0x42A990</c>: 110.0).</summary>
	public const float HomeHeight = 110f;
	/// <summary>The Home target's x and z in original units (<c>0x42A990</c>: 475.0 and 175.0, the middle of the entrance cells (47, 17) and (48, 17)).</summary>
	public const float HomeTargetX = 475f, HomeTargetZ = 175f;
	/// <summary>Engine units per original unit (a cell is ten original units, two engine units).</summary>
	public const float UnitScale = OriginalParkPlacement.ModelScale;
	/// <summary>The yaw change of the rotate keys in smooth mode, degrees per second: 0.002 radians per millisecond (<c>0x42b1c0</c>).</summary>
	public const float RotationSpeed = 0.002f * 1000f * 180f / MathF.PI;
	/// <summary>The zoom keys' rate in zoom units per second: 0.05 per millisecond (<c>0x42b1c0</c>).</summary>
	public const float ZoomSpeed = 0.05f * 1000f;
	/// <summary>The zoom change of one wheel notch (message 0x1000D, <c>0x42a760</c>).</summary>
	public const float WheelStep = 10f;
	public bool UiCapturesKeyboard { get; set; }
	public bool ModalOpen { get; set; }
	/// <summary>The text field taking the keys lies over the park view (a visit's chat entry): it keeps the keyboard, the camera keeps the pointer.</summary>
	public bool TextEntryKeepsPointer { get; set; }
	/// <summary>The UI under the pointer leaves the screen's edge band to the camera (a visit's chat overlay, whose panels lie along the edges).</summary>
	public bool EdgeScrollOverUi { get; set; }
	public Vector3 Target { get; private set; }
	/// <summary>
	/// The original's zoom value (0x7909ec: 70..130, 110 at Home, 0x42a990) in original units: it sets the pitch and the distance of the eye (<see cref="PitchOf"/>,
	/// <see cref="EyeOffset"/>). Not a height above the ground in engine units.
	/// </summary>
	public float Height { get; private set; } = HomeHeight;
	private bool wideZoom = GameOptions.Current.WideZoom;
	/// <summary>The nearest zoom value the camera allows: 70 (tp.exe's normal range) or 40 with <see cref="GameOptions.WideZoom"/>.</summary>
	public float MinimumZoom => wideZoom ? WideMinimumHeight : MinimumHeight;
	/// <summary>The farthest zoom value the camera allows: 130 (tp.exe's normal range) or 300 with <see cref="GameOptions.WideZoom"/>.</summary>
	public float MaximumZoom => wideZoom ? WideMaximumFor( RenderFog.ViewDistanceOf( RenderFog.ViewDistanceOption() ).Start ) : MaximumHeight;

	/// <summary>
	/// [EXT:wide-zoom] The farthest zoom value the wide range allows for a fog start (original units): the largest zoom at which the ground point at the top centre of the screen is still before the fog start, never below
	/// the original's 130 and at most <see cref="WideMaximumHeight"/>. Beyond it the original's own formulas show the whole view as fog (docs/LND.md, "Fog and far plane").
	/// </summary>
	public static float WideMaximumFor( float fogStart )
	{
		if ( TopCentreFogDepth( WideMaximumHeight ) < fogStart )
			return WideMaximumHeight;
		float low = MaximumHeight, high = WideMaximumHeight;
		if ( TopCentreFogDepth( low ) >= fogStart )
			return MaximumHeight;
		for ( var step = 0; step < 40; step++ )
		{
			var middle = (low + high) / 2;
			if ( TopCentreFogDepth( middle ) < fogStart )
				low = middle;
			else
				high = middle;
		}
		return Math.Clamp( low, MaximumHeight, WideMaximumHeight );
	}

	/// <summary>
	/// The fog depth w (original units: the projection's w, sin(45 degrees) x the view depth) of the ground at the top centre of the screen for a zoom value, on the ground under the target: the eye is zoom x sin(pitch) above it
	/// and the top edge's ray descends sin(pitch) - 0.5 cos(pitch) per unit of depth (the screen's half height at unit depth is 0.5, <see cref="OriginalProjection.HalfHeight"/>).
	/// </summary>
	public static float TopCentreFogDepth( float zoom )
	{
		var pitch = PitchOf( zoom );
		var descent = MathF.Sin( pitch ) - OriginalProjection.HalfHeight( OriginalProjection.FieldOfView ) * MathF.Cos( pitch );
		if ( descent <= 0 )
			return float.MaxValue;
		return zoom * MathF.Sin( pitch ) / descent * MathF.Sin( OriginalProjection.FieldOfView * MathF.PI / 360f );
	}
	/// <summary>The bearing of the eye from the target in degrees (the eye is at <c>Target + (cos, sin) * distance</c>); the original's yaw 0 is -90 here (<see cref="OriginalYaw"/>).</summary>
	public float Yaw { get; private set; } = HomeYaw;
	/// <summary>The yaw of the Home pose: the original's yaw 0 looks along +Y (its +z), so the eye sits to the -Y side of the target.</summary>
	public const float HomeYaw = -90f;
	private float smoothedGround;
	private bool dragging;
	private bool middleTurning, middleWasDown;
	private readonly ReferenceFrameClock turnFrames = new();

	public ParkCameraMode()
	{
		// [BIN:TPW-EXE:0x42a990 projection] the camera init builds the projection once with pi/2 radians scaled to 90 degrees (0x578be0, near 0.1, far 1000) and the pose: zoom 110, yaw 0, target (475, 0, 175) (the entrance cells 47/48, 17 of every level's Standard.sam)
		FieldOfView = OriginalProjection.FieldOfView;
		UsesOriginalProjection = true;
		Focus( InitialTarget );
		smoothedGround = GroundAtTarget();
		UpdateTransform();
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
		Height = float.IsFinite( height ) ? Math.Clamp( height, MinimumZoom, MaximumZoom ) : Height;
		Yaw = float.IsFinite( yaw ) ? yaw % 360 : Yaw;
		Focus( target );
		smoothedGround = GroundAtTarget();
		UpdateTransform();
	}

	/// <summary>
	/// Turns the camera to look along the ground direction <paramref name="direction"/> (engine X/Y), keeping the target and the height: the eye goes behind the
	/// target, <c>Position = Target + (cos yaw, sin yaw) × <see cref="HorizontalDistance"/></c>. A zero or non-finite direction changes nothing.
	/// </summary>
	public void LookAlong( System.Numerics.Vector2 direction )
	{
		if ( !float.IsFinite( direction.X ) || !float.IsFinite( direction.Y ) || direction.LengthSquared() < 1e-12f )
			return;
		Yaw = MathF.Atan2( -direction.Y, -direction.X ) * 180f / MathF.PI;
		UpdateTransform();
	}

	/// <summary>
	/// Whether the keypad's scroll keys scroll now: not while Alt alone is held, where Alt+Num4 and Alt+Num6 are the ProfitYear and CoasterHeight cheats (docs/CHEATS.md).
	/// </summary>
	// [BIN:TPW-EXE:0x40C900 context key match] a key event matches a context entry only on the exact key and modifier mask; the camera context's table 0x748158 holds VK_NUMPAD4 (entry 15) and VK_NUMPAD6 (entry 22) with
	// mask 0, so with Alt held the press reaches the cheat context instead and the camera does not scroll (other modifiers are left as OpenTPW had them)
	internal static bool KeypadScroll => Input.ModifierMaskDown != Input.ModifierMask( false, false, true );

	public override void Update()
	{
		var io = ImGuiNET.ImGui.GetIO();
		Update( new CameraControlInput
		{
			HasFocus = Window.Current?.Focused == true,
			LogicalSize = Screen.Size, MousePosition = Input.Mouse.Position, MouseDelta = Input.Mouse.Delta,
			CaptureMouse = io.WantCaptureMouse || Level.Current?.UiCapturesMouse == true,
			CaptureKeyboard = io.WantCaptureKeyboard || UiCapturesKeyboard,
			TextEntry = io.WantTextInput || (Input.TextEntryActive && !TextEntryKeepsPointer), Modal = ModalOpen,
			EdgeScrollOverUi = EdgeScrollOverUi && !io.WantCaptureMouse,
			RightButton = Input.Mouse.Right, MiddleButton = Input.Mouse.Middle, Wheel = Input.Mouse.Wheel,
			Forward = Input.Forward, Right = Input.Right + (KeypadScroll && Input.Down( InputButton.ScrollRight ) ? 1 : 0) - (KeypadScroll && Input.Down( InputButton.ScrollLeft ) ? 1 : 0),
			RotateLeft = Input.Down( InputButton.RotateLeft ), RotateRight = Input.Down( InputButton.RotateRight ),
			RotateLeftPressed = Input.Pressed( InputButton.RotateLeft ), RotateRightPressed = Input.Pressed( InputButton.RotateRight ),
			Zoom = (Input.Down( InputButton.ZoomIn ) ? 1 : 0) - (Input.Down( InputButton.ZoomOut ) ? 1 : 0),
			Home = Input.Pressed( InputButton.Home ), DeltaSeconds = Time.Delta
		}, GameOptions.Current );
	}

	/// <summary>Apply one frame without SDL, ImGui or renderer dependencies.</summary>
	public void Update( CameraControlInput input, GameOptions options )
	{
		Step( input, options );
		SmoothGround( input.DeltaSeconds );
	}

	private void Step( CameraControlInput input, GameOptions options )
	{
		// The wide range follows the option live: switching it off while zoomed out brings the zoom back inside 70..130.
		// the wide range also follows FIRSTPERSONVIEWDISTANCE (WideMaximumFor), so a changed setting clamps the zoom at once
		wideZoom = options.WideZoom;
		var clamped = Math.Clamp( Height, MinimumZoom, MaximumZoom );
		if ( clamped != Height ) { Height = clamped; UpdateTransform(); }
		// with the pointer outside the window the camera takes no input, its edge band included (the browser reports a page with no pointer over its canvas there, at (-1, -1): EXT:browser-no-pointer)
		var inside = input.LogicalSize.X > 0 && input.LogicalSize.Y > 0
			&& input.MousePosition.X >= 0 && input.MousePosition.Y >= 0
			&& input.MousePosition.X < input.LogicalSize.X && input.MousePosition.Y < input.LogicalSize.Y;
		if ( !input.HasFocus || input.Modal || !inside ) { dragging = false; rightWasDown = false; rightDragArmed = false; middleTurning = false; middleWasDown = input.MiddleButton; turnFrames.Reset(); return; }
		var dt = float.IsFinite( input.DeltaSeconds ) ? Math.Clamp( input.DeltaSeconds, 0, 0.1f ) : 0;
		var keyboard = !input.CaptureKeyboard && !input.TextEntry;
		if ( keyboard )
		{
			if ( input.Home ) { Yaw = HomeYaw; Height = HomeHeight; Focus( InitialTarget ); }
			var rotation = options.Rotation == RotationMode.Smooth
				? ((input.RotateRight ? 1 : 0) - (input.RotateLeft ? 1 : 0)) * RotationSpeed * dt
				: ((input.RotateRightPressed ? 1 : 0) - (input.RotateLeftPressed ? 1 : 0)) * 90;
			Yaw = (Yaw + rotation) % 360;
			Height = Math.Clamp( Height - input.Zoom * ZoomSpeed * dt, MinimumZoom, MaximumZoom );
		}
		Yaw = (Yaw + MiddleTurn( input, dt )) % 360;
		var radians = Yaw.DegreesToRadians();
		var forward = new Vector3( -MathF.Cos( radians ), -MathF.Sin( radians ), 0 );
		var right = new Vector3( forward.Y, -forward.X, 0 );
		float x = keyboard ? input.Right : 0, y = keyboard ? input.Forward : 0;
		var mouse = !input.CaptureMouse && !input.TextEntry;
		ArmRightDrag( input, options, dt );
		if ( mouse )
		{
			Height = Math.Clamp( Height - input.Wheel * WheelStep, MinimumZoom, MaximumZoom );
			if ( options.Scroll != ScrollMode.Pushscroll && input.RightButton && dragging )
				Target += (right * input.MouseDelta.X / input.LogicalSize.X - forward * input.MouseDelta.Y / input.LogicalSize.Y) * HorizontalSpan( Height );
		}
		// the edge band scrolls over the park, and over a UI that leaves the band to the camera (the chat overlay's panels along the edges)
		if ( options.Scroll == ScrollMode.Pushscroll && !input.TextEntry && (mouse || input.EdgeScrollOverUi) )
		{
			x += Edge( input.MousePosition.X, input.LogicalSize.X );
			y -= Edge( input.MousePosition.Y, input.LogicalSize.Y );
		}
		dragging = mouse && options.Scroll == ScrollMode.RightButton && input.RightButton && rightDragArmed;
		var length = MathF.Sqrt( x * x + y * y );
		if ( length > 1 ) { x /= length; y /= length; }
		Focus( Target + (right * x + forward * y) * dt * PanSpeed( Height ) );
	}

	private bool rightWasDown, rightDragArmed;
	private float rightHeldSeconds;
	private Vector2 rightPressPosition;

	/// <summary>
	/// With the RMBCancelOn option the right press is the camera's drag only once it has lasted 200 ms or the pointer has strayed 8 px on an axis; before that it may still be a quick click, which the park view
	/// answers on its release by ending its tool or mode (<see cref="UiInput.RightClicked"/>). With the option off the drag starts at once.
	/// </summary>
	// [BIN:TPW-EXE:0x4881AE held-back right press] with the option set the park view's mouse procedure 0x4881A0 does not forward the right press (0x10005) to the camera (0x74FC2C cleared at 0x488354) but remembers its time and position (0x7C2504, 0x7B5344/0x7B9774); the first message 0x1E older than 200 ms (0x48823F) or a move beyond 8 px on either axis (0x488265, 0x48827F) forwards it (0x42A760 with 0x10005 at 0x48828B), the drag starting at that position
	private void ArmRightDrag( CameraControlInput input, GameOptions options, float dt )
	{
		if ( !input.RightButton )
		{
			rightWasDown = false;
			rightDragArmed = false;
			rightHeldSeconds = 0;
			return;
		}
		if ( !rightWasDown )
		{
			rightWasDown = true;
			rightDragArmed = !options.RmbCancel;
			rightHeldSeconds = 0;
			rightPressPosition = input.MousePosition;
			return;
		}
		if ( rightDragArmed )
			return;
		rightHeldSeconds += dt;
		if ( rightHeldSeconds > OpenTPW.UI.Original.UiInputSource.RightClickMilliseconds / 1000f || MathF.Abs( input.MousePosition.X - rightPressPosition.X ) > OpenTPW.UI.Original.UiInputSource.RightClickSlop
			|| MathF.Abs( input.MousePosition.Y - rightPressPosition.Y ) > OpenTPW.UI.Original.UiInputSource.RightClickSlop )
			rightDragArmed = true;
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
	/// <summary>The pan speed at a zoom value, engine units per second: the original's steady-state velocity of 0.02 x the square root of the zoom value per millisecond.</summary>
	// [BIN:TPW-EXE:0x42b1c0 pan velocity] each frame the velocity becomes (sqrt(zoom) x 0.02 x key direction + the old velocity) x 0.5 (0x42bc30-0x42bc96: fsqrt, 0.02 at 0x6fddf8, 0.5 at 0x6fddfc), which settles at 0.02 x sqrt(zoom) original units per millisecond; the target then moves by the frame's milliseconds times that. OpenTPW takes the settled value (no ramp)
	public static float PanSpeed( float zoom ) => 0.02f * MathF.Sqrt( zoom ) * 1000f * UnitScale;

	/// <summary>The ground distance the right-button drag covers for a whole screen width at a zoom value (host choice: about the width of the view at the target).</summary>
	public static float HorizontalSpan( float zoom ) => 2f * zoom * UnitScale;

	/// <summary>
	/// The pitch of the camera in radians for a zoom value: from 45 degrees at 70 to 65 degrees at 130 (1.134464 rad). Outside 70..130 the line keeps going
	/// (35 degrees at 40) until the original's clamp to 0..1.4707963 (84.3 degrees, reached at a zoom value of about 188 and kept up to 300).
	/// </summary>
	// [BIN:TPW-EXE:0x42b1c0 pitch] t = (zoom - 70) / 60 (0x42c742: 70.0 at 0x6fde08, 0.016666668 at 0x6fde14); pitch (0x790a3c) = (1 - t) x 0.7853982 - t x -1.134464 (0x6fde18), clamped to 0..1.4707963 (0x42c7c0)
	public static float PitchOf( float zoom )
	{
		var t = (zoom - 70f) * 0.016666668f;
		return Math.Clamp( (1f - t) * 0.7853982f - t * -1.134464f, 0f, 1.4707963f );
	}

	/// <summary>The ground distance from the target to the point under the eye, in original units: the zoom value times the cosine of the pitch.</summary>
	public static float HorizontalDistanceOf( float zoom ) => zoom * MathF.Cos( PitchOf( zoom ) );

	/// <summary>The ground distance from the target to the point under the eye, in engine units.</summary>
	public float HorizontalDistance => HorizontalDistanceOf( Height ) * UnitScale;

	/// <summary>
	/// The eye's offset from the target in engine units for a zoom value and a bearing in degrees: the ground part is the zoom value times the cosine of the pitch
	/// toward the bearing, the height is the zoom value times the sine of the pitch, plus the smoothed ground height under the target.
	/// </summary>
	// [BIN:TPW-EXE:0x42b1c0 eye] the eye offset (0x790aa0..0x790aa8) is (0, zoom x sin(pitch), -zoom x cos(pitch)) turned about the up axis by the smoothed yaw (0x7909e0): x = sin(yaw) x its z part, z = cos(yaw) x its z part (0x42c96d-0x42c9fe); the eye is the target (0x7908f0, 0x7908f4 = 0, 0x7908f8) plus that offset, and its height also gets the ground height under the target (0x452560 with flag 1, averaged with the previous frame's value 0x790ae4 each frame, 0x42c817-0x42c848)
	public static Vector3 EyeOffset( float zoom, float yawDegrees, float ground )
	{
		var pitch = PitchOf( zoom );
		var distance = zoom * MathF.Cos( pitch ) * UnitScale;
		var radians = yawDegrees.DegreesToRadians();
		return new Vector3( MathF.Cos( radians ) * distance, MathF.Sin( radians ) * distance, zoom * MathF.Sin( pitch ) * UnitScale + ground );
	}

	/// <summary>The view direction for a zoom value and a bearing in degrees: along the pitch, toward the target's side of the eye.</summary>
	// [BIN:TPW-EXE:0x42b1c0 view matrix] the camera matrix (0x790990) is the x rotation by the pitch (0x46fb50: rows (1,0,0), (0,cos,sin), (0,-sin,cos)) turned about the up axis by the smoothed yaw (0x46f650); its third row is the view direction, so the camera looks down by the pitch and not at the target point itself (the ground height under the target only lifts the eye)
	public static Vector3 ViewDirection( float zoom, float yawDegrees )
	{
		var pitch = PitchOf( zoom );
		var radians = yawDegrees.DegreesToRadians();
		return new Vector3( -MathF.Cos( radians ) * MathF.Cos( pitch ), -MathF.Sin( radians ) * MathF.Cos( pitch ), -MathF.Sin( pitch ) );
	}

	/// <summary>The yaw in the original's terms, radians: its forward on the ground is (sin, cos) in (x, y).</summary>
	public static float OriginalYaw( float yawDegrees ) => (-90f - yawDegrees).DegreesToRadians();

	/// <summary>The yaw in this camera's degrees of an original yaw in radians (the inverse of <see cref="OriginalYaw"/>).</summary>
	public static float YawOf( float originalRadians ) => -90f - originalRadians.RadiansToDegrees();

	/// <summary>
	/// The pose a save's camera block gives the camera when the park is loaded: the zoom (0x7909EC, clamped to the zoom range), the yaw (0x790A38, radians) and the required point of
	/// interest (0x7908F0, original units: x and z on the ground), the point the camera looks at. Null when a value is not a finite number or the point is off the map.
	/// </summary>
	// [BIN:TPW-EXE:0x42CEC0 camera loader] gf_CameraZoom, gf_YRotation, gui_CameraFlags (cleared again after the read), gs_RequiredPOIPosition (three floats), gs_SavedPOIPosition and the saved yaw are read into the camera's own words; the camera then steers to the required point
	public static (float X, float Z, float Height, float Yaw)? PoseOfSave( SaveCamera camera )
	{
		var (x, z) = (camera.RequiredPoi.X, camera.RequiredPoi.Z);
		if ( !float.IsFinite( camera.Zoom ) || !float.IsFinite( camera.YRotation ) || !float.IsFinite( x ) || !float.IsFinite( z ) || x < 0 || z < 0 || x > 1280 || z > 1280 )
			return null;
		return (x, z, Math.Clamp( camera.Zoom, MinimumHeight, MaximumHeight ), YawOf( camera.YRotation ));
	}

	private float GroundAtTarget()
	{
		if ( Level.Current?.OriginalPark?.Heightfield is not { } field )
			return 0;
		var origin = OriginalParkPlacement.GetOrigin( field );
		var ground = OriginalTerrain.GroundUnder( field, (Target.X + origin.X) / UnitScale, (Target.Y + origin.Y) / UnitScale ) * UnitScale;
		return float.IsFinite( ground ) ? ground : 0;
	}

	/// <summary>Moves the smoothed ground height toward the ground under the target: the original averages the two once per frame, here per 60 Hz reference frame of elapsed time.</summary>
	private void SmoothGround( float deltaSeconds )
	{
		var frames = float.IsFinite( deltaSeconds ) ? Math.Clamp( deltaSeconds, 0, 0.5f ) * ReferenceFrameClock.FramesPerSecond : 0;
		var keep = MathF.Pow( 0.5f, frames );
		var ground = GroundAtTarget();
		var next = ground + (smoothedGround - ground) * keep;
		if ( MathF.Abs( next - smoothedGround ) < 1e-6f )
			return;
		smoothedGround = next;
		UpdateTransform();
	}

	private void UpdateTransform()
	{
		Position = Target + EyeOffset( Height, Yaw, smoothedGround );
		Rotation = Rotation.LookAt( ViewDirection( Height, Yaw ) );
		if ( !EntityConstructionScope.IsStaging ) UpdateAudioListener();
	}

	internal void UpdateAudioListener()
	{
		GameAudio.UpdateListener( GameAudio.ListenerPlace( Position.GetSystemVector3(), Target.GetSystemVector3(), eyeMode: false ),
			(Target - Position).GetSystemVector3(), Level.Current?.OriginalPark?.Heightfield, Rotation.Up.GetSystemVector3() );
	}
}
