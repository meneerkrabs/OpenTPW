namespace OpenTPW;

public partial class Level
{
	/// <summary>The first-person ride view while the player rides a placed ride; null in the park camera.</summary>
	public RideCameraMode? RideView { get; private set; }

	private CameraMode? parkCameraBeforeRide;

	/// <summary>
	/// Enters the ride view of a placed ride from the park camera: the original's "Ride it!" (ride window,
	/// <c>gui_CameraFlags</c> 4; maexah/OpenTPW hud.md, "Four ways out of camcorder mode"). False when the ride
	/// cannot be ridden (no completed coaster track and not the prototype ride) or the view is already open.
	/// </summary>
	// [APPROX:UI-093] the entry is the selected ride's key (E), not the original's "Ride it!" button, whose art and position are not in the repository; the heading is the park camera's forward
	public bool EnterRideView( OriginalObject ride )
	{
		if ( RideView != null || !RideCameraMode.CanRide( ride ) )
			return false;
		parkCameraBeforeRide = Camera.Mode;
		RideView = new RideCameraMode( ride, Camera.Rotation.Forward );
		Camera.Mode = RideView;
		return true;
	}

	/// <summary>
	/// Leaves the ride view and restores the park camera it was entered from. The original leaves to the
	/// first-person walk when the ride was entered from it, else to the orbit (ride-view exits: Escape, the
	/// eject button, and a right click with RMB cancel on).
	/// </summary>
	// [APPROX:UI-095] the orbit camera is restored, not the walking first-person view: the first-person walk is not implemented, so the park camera is the only other view
	public void ExitRideView()
	{
		if ( RideView == null )
			return;
		Camera.Mode = parkCameraBeforeRide ?? new ParkCameraMode();
		RideView = null;
		parkCameraBeforeRide = null;
	}
}
