namespace OpenTPW;

/// <summary>
/// The ride camera: the first-person view from a placed ride, reusable by the park (the player rides it with
/// <see cref="Level.EnterRideView"/>) and by read-only visits of a shared park. The original had ride cameras:
/// the Mac binary's camera-mode classes <c>CMMFirstPersonTarget</c> (Mac <c>0x1006CF44</c>, 3,312 bytes, its
/// body starts by reading a thing's position into a cell index) and <c>CMMCoaster</c> (vtables data:0x3d758 and
/// data:0x3d7a4). The "Ride it!" entry and the exits are the maexah/OpenTPW lead (docs/exe/hud.md); OpenTPW
/// offers the ride where its motion is traced: a coaster, from its first train's front car along the sampled
/// track, and the prototype Totem, on its carriage's modelled height.
/// </summary>
// [APPROX:UI-094] the view itself is an approximation: the eye sits half a car above the first train's front car
// sample (coaster) or on the carriage node's height above the ride's anchor (prototype), the look direction follows
// the track's sampled direction or the entry camera's heading, and the walk, the sway and the camera-mode Update
// bodies are not decoded — evidence needed: the Update of CMMFirstPersonTarget (Mac 0x1006CF44) and CMMCoaster,
// and the seat position the ride's script gives the rider.
// Riding a coaster follows the first train only; the other trains are not chosen by the player.
public sealed class RideCameraMode : CameraMode
{
	/// <summary>Engine units above the front car's sample the eye sits at (half of the drawn car height, CoasterRenderer).</summary>
	public const float CoasterSeatHeight = 1.5f;
	/// <summary>Engine units above the prototype ride's anchor the eye sits at.</summary>
	public const float PrototypeSeatHeight = 2f;
	/// <summary>The field of view of the ride view; the original's first-person field of view is not traced (UI-094).</summary>
	public const float ViewFieldOfView = 55f;

	private readonly OriginalObject ride;
	private readonly Vector3 heading;

	/// <param name="ride">The placed ride to ride: a coaster station with a completed track, or the prototype ride.</param>
	/// <param name="heading">Horizontal look direction until the track gives one (the view the player had when boarding).</param>
	public RideCameraMode( OriginalObject ride, Vector3 heading )
	{
		this.ride = ride;
		this.heading = heading;
		FieldOfView = ViewFieldOfView;
	}

	/// <summary>The ride being ridden.</summary>
	public OriginalObject Ride => ride;

	public override void Update()
	{
		var (eye, look) = Frame( ride, heading );
		Position = eye;
		Rotation = Rotation.LookAt( look );
	}

	/// <summary>Whether a placed ride can be ridden: the prototype ride, or a coaster whose track is complete.</summary>
	public static bool CanRide( OriginalObject ride ) => ride is PrototypeRide || CanRide( ride.Runtime );

	/// <summary>Whether a placed object's runtime is a coaster with a completed, running track.</summary>
	public static bool CanRide( OriginalObjectRuntime runtime ) => runtime.Coaster is { IsOperational: true, Trains.Count: > 0 };

	/// <summary>The camera frame for the current state of the ridden ride: where the eye is and where it looks.</summary>
	public static (Vector3 Eye, Vector3 Look) Frame( OriginalObject ride, Vector3 heading )
	{
		if ( CanRide( ride.Runtime ) )
			return CoasterFrame( ride.Runtime.Coaster!, ride.Placement.GridOrigin );
		return PrototypeFrame( ride.Position, ride is PrototypeRide prototype ? prototype.MotionHeight : 0, heading );
	}

	/// <summary>The front car of the coaster's first train: the eye on its sample, the view along the track's direction.</summary>
	public static (Vector3 Eye, Vector3 Look) CoasterFrame( CoasterRide coaster, System.Numerics.Vector2 gridOrigin )
	{
		var sample = coaster.SampleAt( coaster.Trains[0].Position );
		var eye = ToEngine( gridOrigin, sample.Position ) + new Vector3( 0, 0, CoasterSeatHeight );
		var direction = ToEngineDirection( sample.Direction );
		return (eye, eye + direction);
	}

	/// <summary>The prototype ride: the eye on the carriage's height above the anchor, looking as on entry.</summary>
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
