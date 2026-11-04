using System.Numerics;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The ride view (the original's camcorder on a ride, <c>gui_CameraFlags</c> 4): the eye sits on the ride model's camera seat (<see cref="RideSeat"/>), looks along
/// the seat's axes with the original's one-frame orientation smoothing, and the pointer's offset from the screen centre turns the view (free look). Reusable by the park
/// (the player rides with <see cref="Level.EnterRideView"/>) and by read-only visits of a shared park. A coaster takes the seat of its first train's front car model.
/// </summary>
// [BIN:TPW-EXE:0x42b1c0 camera update] the per-frame camera update: with the ride flag set, the camera matrix is the seat node's world matrix (0x790a30) with the three rotation rows blended with the node's matrix of the previous frame (0x790948) and the translation copied exactly (0x42ca04-0x42cb96), then the free-look rotation is multiplied in (0x42cba0-0x42cbdf)
// [BIN:TPW-EXE:0x42a990 ride projection] the ride camera never changes the projection (0x578be0, built once with the argument 90 at 0x42a990 and 0x54e860), so the ride view uses the park camera's projection (OriginalProjection). [APPROX:UI-094] the car 0x436880 selects is decoded (the train with the smallest position, its car 0 or the second car behind a front car of another type), the eye is its model's camera seat; the view direction is the matrix's third row (0x56b940, see SeatAxes) and the look rotations are the matrix code's (0x46f350, 0x46f650, LookAngles), which the video confirms (the view turns right and down with the pointer, 77:20-77:21); the blended basis stays orthonormal here and the first frame of a ride starts unblended (BlendFactor)
public sealed class RideCameraMode : CameraMode
{
	/// <summary>Engine units above the front car's sample the eye sits at when a coaster's car model has no seat (half of the drawn car height, CoasterRenderer).</summary>
	public const float CoasterSeatHeight = 1.5f;
	/// <summary>Engine units above the prototype ride's anchor the eye sits at when its model has no seat.</summary>
	public const float PrototypeSeatHeight = 2f;
	/// <summary>The field of view of the ride view: the original's one projection angle (<see cref="OriginalProjection"/>).</summary>
	public const float ViewFieldOfView = OriginalProjection.FieldOfView;
	/// <summary>The pointer's dead zone each side of the view's centre, in units of the half width.</summary>
	public const float LookDeadZoneX = 0.2f;

	private readonly OriginalObject ride;
	private readonly int train;
	private readonly Vector3 heading;
	private readonly Func<Matrix4x4?>? seat;
	private NVector3 up = NVector3.UnitZ;
	private Matrix4x4? previousNode;

	/// <param name="ride">The placed ride to ride: a ride whose model has a camera seat, a coaster station with a completed track, or the prototype ride.</param>
	/// <param name="heading">Horizontal look direction until a seat gives one (the view the player had when boarding).</param>
	/// <param name="seat">The seat's world matrix in engine axes (X/Y ground, Z up), null when it is not available; defaults to the ride model's seat node (a coaster has none by default).</param>
	/// <param name="train">The train of a coaster the view rides, taken once when the view opens (<see cref="CoasterRide.RideViewTrain"/>); the default is the one that is smallest in position now.</param>
	public RideCameraMode( OriginalObject ride, Vector3 heading, Func<Matrix4x4?>? seat = null, int? train = null )
	{
		this.ride = ride;
		this.train = train ?? ride?.Runtime?.Coaster?.RideViewTrain ?? 0;
		this.heading = heading;
		this.seat = seat ?? DefaultSeat( ride );
		FieldOfView = ViewFieldOfView;
		UsesOriginalProjection = true;
	}

	/// <summary>The ride being ridden.</summary>
	public OriginalObject Ride => ride;

	/// <summary>The train of a coaster being ridden (the ride view keeps its car until it closes).</summary>
	public int Train => train;

	/// <summary>The camera's up vector: the seat's, so the horizon rolls with the car.</summary>
	public override Vector3 Up => new( up.X, up.Y, up.Z );

	/// <summary>The look angles of the last frame in radians: yaw (positive turns right) and pitch (positive looks down).</summary>
	public (float Yaw, float Pitch) LastLook { get; private set; }

	/// <summary>The world matrix of the seat the view rides now (the ride model's camera seat, a coaster's camera car), null while it follows the fallback frame (<see cref="Frame"/>).</summary>
	internal Matrix4x4? CurrentSeat => seat?.Invoke();

	private static Func<Matrix4x4?>? DefaultSeat( OriginalObject? ride )
	{
		if ( ride?.Runtime == null || ride.Runtime.Coaster != null )
			return null;
		var node = RideSeat.FindNode( ride );
		return node < 0 ? null : () => node < ride.NodeTransforms.Count ? ride.GetNodeTransform( node ) : null;
	}

	/// <summary>A pointer offset that replaces the mouse (captures and tests); null reads the mouse.</summary>
	// [EXT:debug-hud] developer/screenshot helper, not original behaviour
	internal static NVector2? PointerOverride { get; set; }

	/// <summary>Whether the free look follows the pointer under an interface window too: the winning screen's full-screen window lies over the whole winning view.</summary>
	// [BIN:TPW-EXE:0x0042B8C7 winning look] the look reads the pointer against the park view's centre whenever the camera flags have 0x3C, not the window under the pointer, so the winning window (0x7472, full screen) leaves it working
	public bool LooksUnderWindows { get; init; }

	public override void Update()
	{
		var size = Screen.Size;
		var position = Input.Mouse.Position;
		// like the terrain walk: without focus, with the pointer outside the window or while a developer panel owns the mouse the view looks straight ahead
		var active = Window.Current?.Focused == true && size.X > 0 && size.Y > 0 && position.X >= 0 && position.Y >= 0 && position.X < size.X && position.Y < size.Y
			&& !ImGuiNET.ImGui.GetIO().WantCaptureMouse && (LooksUnderWindows || Level.Current?.UiCapturesMouse != true);
		var pointer = PointerOverride ?? (active ? PointerOffset( position.X, position.Y, size.X, size.Y ) : NVector2.Zero);
		Advance( Time.Delta * 1000f, pointer );
	}

	/// <summary>One frame: the seat's pose (or the fallback frame without one), smoothed, then turned by the free look.</summary>
	/// <param name="deltaMilliseconds">The frame time in milliseconds.</param>
	/// <param name="pointer">The pointer's offset from the view centre, each axis in [-1, 1] (X right, Y up).</param>
	public void Advance( float deltaMilliseconds, NVector2 pointer )
	{
		if ( seat?.Invoke() is not { } node )
		{
			var (eye, look) = Frame( ride, heading, train );
			Position = eye;
			Rotation = FrameRotation( eye, look );
			up = NVector3.UnitZ;
			previousNode = null;
			LastLook = default;
			return;
		}
		var current = SeatAxes( node );
		var smoothed = current;
		if ( previousNode is { } before )
			smoothed = Blend( current, SeatAxes( before ), BlendFactor( deltaMilliseconds ) );
		previousNode = node;
		var (yaw, pitch) = LookAngles( pointer.X, pointer.Y );
		LastLook = (yaw, pitch);
		var (lookForward, lookUp) = ApplyLook( smoothed.Forward, smoothed.Up, yaw, pitch );
		up = lookUp;
		Position = new Vector3( node.Translation.X, node.Translation.Y, node.Translation.Z );
		Rotation = BasisRotation( lookForward, lookUp );
		if ( !EntityConstructionScope.IsStaging )
			GameAudio.UpdateListener( new NVector3( Position.X, Position.Y, Position.Z ), lookForward, Level.Current?.OriginalPark?.Heightfield, lookUp );
	}

	/// <summary>
	/// The weight of the previous frame's orientation in the blend: <c>(1000 - dt) * 0.0005</c> with <c>dt</c> the frame time in whole milliseconds plus one, 100 when that is above 200
	/// (a stalled frame), about 0.48 at 30 frames a second and never more than 0.4995.
	/// </summary>
	// [BIN:TPW-EXE:0x42b2a0 frame time] dt = (the millisecond tick now (0x402d80) + 1) - the last frame's tick ([0x7909e4], stored at 0x42cce7), an integer millisecond count: the host's float milliseconds are cut to whole milliseconds first
	// [BIN:TPW-EXE:0x42b2c7 frame time limits] a dt above 200 becomes 100.0 (0x42b2d4: the stall of a hitch counts as 100 ms, not 200 or more) and one of 0 or less becomes 1.0 (0x42b2ed)
	// [BIN:TPW-EXE:0x42cba0 view smoothing] 0x42ca88-0x42cb0b: a = (1000 - dt) * 0.0005 (0x6fde34 is 0.0005; the clamp of dt to [1, 1000] at 0x42ca1b-0x42ca59 never acts after the 200 limit), and every one of the nine rotation words (the three rows of the three) is node * (1 - a) + previousNode * a with the fourth column and the translation copied from the node; the previous matrix (0x790948) is then overwritten with the node's matrix, not with the blend (0x42cb9c), so the smoothing is a one-frame average, not an accumulating ease
	// [BIN:TPW-EXE:0x42cc7c view not renormalised] the blended basis is not made orthonormal again: 0x42b070 only compares it with the last frame's matrix and 0x578bc0 copies it into the camera; OpenTPW's Blend orthonormalises its axes (a difference of the order of the square of the turn between two frames)
	public static float BlendFactor( float deltaMilliseconds )
	{
		var dt = CamcorderWalk.FrameMilliseconds( deltaMilliseconds );
		return (1000f - dt) * 0.0005f;
	}

	/// <summary>
	/// The turn the pointer asks for: <c>yaw</c> from its horizontal offset (a dead zone of 0.2 each side, then 3.5 radians per unit), <c>pitch</c> from its vertical offset
	/// (upwards from 0.2 at 1.2, downwards from -0.4 at 1.0), in radians; positive yaw turns right, positive pitch looks down.
	/// </summary>
	// [BIN:TPW-EXE:0x42b8c7 ride look] with the camera flags in 0x3c: x = (pointer x - 0x7a1c54) / 0x74d104 and y = -(pointer y - 0x7a1c58) / 0x74d108 (the view's centre and half size, written at 0x48a14e-0x48a1c5); 0x790ae0 = (x -/+ 0.2) * 3.5 outside +/-0.2 else 0; 0x790adc = (0.2 - y) * 1.2 above 0.2, -y - 0.4 below -0.4, else 0; both are read at 0x42cba5/0x42cbaa and multiplied in as a rotation about X by 0x790adc then about Y by 0x790ae0 (the axis string "XY" at 0x74daa4, 0x46f6f0); the pointer position itself is the input, with no easing of its own
	// [BIN:TPW-EXE:0x42cbc3 look rotation] the angles go to 0x46f6f0 with the axis string "XY" (index 4): about X by 0x790adc (pitch), then about Y by 0x790ae0 (yaw), each a row-vector rotation of the camera matrix (0x46f350: e1' = e1 cos - e2 sin, e2' = e2 cos + e1 sin; 0x46f650: e0' = e0 cos + e2 sin, e2' = e2 cos - e0 sin; the matrix product 0x4702d0), the standard left-handed X and Y rotations: positive yaw turns the forward axis toward +x (right) and positive pitch toward -y (down), so a pointer right of and below the centre turns the view right and down, as the video shows (77:20-77:21)
	// [BIN:TPW-EXE:0x489f50 pointer reference] the pointer's centre and half size (0x7a1c54, 0x7a1c58, 0x74d104, 0x74d108) are the full-screen park view window's (rectangle (0,0)-(2047,1535) of its creation 0x65dd7b, scaled to the screen), not the ride view window's own rectangle, which is the whole host window here too
	public static (float Yaw, float Pitch) LookAngles( float x, float y )
	{
		x = float.IsFinite( x ) ? Math.Clamp( x, -1f, 1f ) : 0;
		y = float.IsFinite( y ) ? Math.Clamp( y, -1f, 1f ) : 0;
		var yaw = x < -LookDeadZoneX ? (x + LookDeadZoneX) * 3.5f : x > LookDeadZoneX ? (x - LookDeadZoneX) * 3.5f : 0;
		var pitch = y < -0.4f ? -y - 0.4f : y > 0.2f ? (0.2f - y) * 1.2f : 0;
		return (yaw, pitch);
	}

	/// <summary>The pointer's offset from the view centre as the original computes it: -1..1 across the window, Y up.</summary>
	public static NVector2 PointerOffset( float pointerX, float pointerY, float width, float height ) =>
		new( Math.Clamp( (pointerX - width / 2f) / (width / 2f), -1f, 1f ), Math.Clamp( -(pointerY - height / 2f) / (height / 2f), -1f, 1f ) );

	// [BIN:TPW-EXE:0x56b940 camera axes] the camera matrix [0x87afe8] the update 0x42b1c0 fills (0x578bc0) is a camera-to-world matrix: its translation words +0x30..+0x38 are the eye (read at 0x56bbea), and the view's corner rays (x, y, 1) are carried through it by 0x578d20 (case 3, row vector times matrix, 0x56b940-0x56b95f), so the view direction is the matrix's third row (the node's local Z), its up the second row and its right the first
	/// <summary>
	/// The forward and up axes of a seat matrix in engine axes. A node's local Z (the model's forward) and Y (up) are the engine matrix's second and third rows
	/// (<see cref="ObjectPlacement.SwapAxes"/> swaps Y and Z); the placement's scale is divided out.
	/// </summary>
	public static (NVector3 Forward, NVector3 Up) SeatAxes( Matrix4x4 engineNode ) =>
		Orthonormal( NVector3.TransformNormal( NVector3.UnitY, engineNode ), NVector3.TransformNormal( NVector3.UnitZ, engineNode ) );

	private static (NVector3 Forward, NVector3 Up) Orthonormal( NVector3 forward, NVector3 upward )
	{
		if ( forward.LengthSquared() < 1e-12f )
			forward = NVector3.UnitX;
		forward = NVector3.Normalize( forward );
		upward -= forward * NVector3.Dot( upward, forward );
		if ( upward.LengthSquared() < 1e-12f )
		{
			var candidate = MathF.Abs( forward.Z ) < 0.9f ? NVector3.UnitZ : NVector3.UnitX;
			upward = candidate - forward * NVector3.Dot( candidate, forward );
		}
		return (forward, NVector3.Normalize( upward ));
	}

	/// <summary>The blend of the current and the previous frame's axes: current * (1 - weight) + previous * weight, made orthonormal again.</summary>
	public static (NVector3 Forward, NVector3 Up) Blend( (NVector3 Forward, NVector3 Up) current, (NVector3 Forward, NVector3 Up) previous, float weight ) =>
		Orthonormal( current.Forward * (1 - weight) + previous.Forward * weight, current.Up * (1 - weight) + previous.Up * weight );

	/// <summary>Turns a view by the free look: first the pitch about its right axis, then the yaw about its up axis (positive yaw to the right, positive pitch down).</summary>
	public static (NVector3 Forward, NVector3 Up) ApplyLook( NVector3 forward, NVector3 up, float yaw, float pitch )
	{
		if ( yaw == 0 && pitch == 0 )
			return (forward, up);
		// Engine axes are right-handed with right = forward x up; a positive turn about 'right' looks up, about 'up' looks left.
		var right = NVector3.Cross( forward, up );
		var pitched = Quaternion.CreateFromAxisAngle( right, -pitch );
		var f = NVector3.Transform( forward, pitched );
		var u = NVector3.Transform( up, pitched );
		var yawed = Quaternion.CreateFromAxisAngle( up, -yaw );
		return Orthonormal( NVector3.Transform( f, yawed ), NVector3.Transform( u, yawed ) );
	}

	/// <summary>The rotation whose forward axis is <paramref name="forward"/> and whose up axis is <paramref name="up"/> (the engine's forward is +X, right -Y, up +Z).</summary>
	public static Rotation BasisRotation( NVector3 forward, NVector3 up )
	{
		(forward, up) = Orthonormal( forward, up );
		var right = NVector3.Cross( forward, up );
		var matrix = new Matrix4x4( forward.X, forward.Y, forward.Z, 0, -right.X, -right.Y, -right.Z, 0, up.X, up.Y, up.Z, 0, 0, 0, 0, 1 );
		var q = Quaternion.Normalize( Quaternion.CreateFromRotationMatrix( matrix ) );
		return new Rotation( q.X, q.Y, q.Z, q.W );
	}

	/// <summary>The look target is a world position; rotation follows its direction relative to the eye.</summary>
	internal static Rotation FrameRotation( Vector3 eye, Vector3 look ) => Rotation.LookAt( look - eye );

	/// <summary>
	/// Whether a placed ride can be ridden: a coaster with a completed track, or any other ride whose model has a camera seat
	/// (the first dummy attribute with bit 0x1000 and value 1), or the prototype ride.
	/// </summary>
	// [BIN:TPW-EXE:0x42a560 ride camera] a ride is rideable when its model has the seat record the camera looks up; about 40 ride models carry one (coasters, the Totem, bumper cars, go-karts, tv simulator, rocket and others)
	public static bool CanRide( OriginalObject ride ) => ride is PrototypeRide || (ride.Runtime is { } runtime && (runtime.Coaster != null ? CanRide( runtime ) : runtime.IsAttraction && RideSeat.FindNode( ride ) >= 0));

	/// <summary>Whether a placed object's runtime is a coaster with a completed, running track.</summary>
	public static bool CanRide( OriginalObjectRuntime runtime ) => runtime.Coaster is { IsOperational: true, Trains.Count: > 0 };

	/// <summary>The camera frame without a seat: where the eye is and where it looks (a coaster's sampled track, the prototype's height).</summary>
	public static (Vector3 Eye, Vector3 Look) Frame( OriginalObject ride, Vector3 heading, int? train = null )
	{
		if ( CanRide( ride.Runtime ) )
			return CoasterFrame( ride.Runtime.Coaster!, ride.Placement.GridOrigin, train );
		return PrototypeFrame( ride.Position, ride is PrototypeRide prototype ? prototype.MotionHeight : 0, heading );
	}

	/// <summary>The camera car of the ride view's train (the smallest in position unless <paramref name="train"/> names one): the eye on its sample, the view along the track's direction.</summary>
	public static (Vector3 Eye, Vector3 Look) CoasterFrame( CoasterRide coaster, System.Numerics.Vector2 gridOrigin, int? train = null )
	{
		// The train's position is its middle car's; the camera car stands its offset ahead (the first car, or the second behind a front car of another type than the seats').
		var sample = coaster.SampleAt( coaster.CarCoordinate( coaster.Trains[Math.Clamp( train ?? coaster.RideViewTrain, 0, coaster.Trains.Count - 1 )], coaster.Definition.RideCameraCar ) );
		var eye = ToEngine( gridOrigin, sample.Position ) + new Vector3( 0, 0, CoasterSeatHeight );
		var direction = ToEngineDirection( sample.Direction );
		return (eye, eye + direction);
	}

	/// <summary>The prototype ride without a seat: the eye on the carriage's height above the anchor, looking as on entry.</summary>
	public static (Vector3 Eye, Vector3 Look) PrototypeFrame( Vector3 anchor, float motionHeight, Vector3 heading )
	{
		var eye = new Vector3( anchor.X, anchor.Y, anchor.Z + motionHeight + PrototypeSeatHeight );
		var horizontal = new Vector3( heading.X, heading.Y, 0 );
		return (eye, eye + (horizontal.LengthSquared > 1e-6f ? horizontal : Vector3.Forward));
	}

	/// <summary>Coaster world position (10 units per cell, Y up) to engine position (X/Y ground, Z up), as the coaster renderer maps.</summary>
	private static Vector3 ToEngine( System.Numerics.Vector2 corner, System.Numerics.Vector3 world ) =>
		new( corner.X + world.X * ObjectPlacement.ModelScale, corner.Y + world.Z * ObjectPlacement.ModelScale, world.Y * ObjectPlacement.ModelScale );

	/// <summary>A coaster world direction to an engine direction (Y up becomes Z up).</summary>
	private static Vector3 ToEngineDirection( System.Numerics.Vector3 direction ) =>
		new Vector3( direction.X, direction.Z, direction.Y ).Normal;
}
