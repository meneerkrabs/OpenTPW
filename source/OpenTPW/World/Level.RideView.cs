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
	// [EXT:ride-view-key] the E key also enters the ride view of the selected ride; the original has only the ride window's Ride it! button (table 0x755150, b_ride it!, help 16), which ParkHud.ObjectWindow.cs draws; the heading is the park camera's forward
	public bool EnterRideView( OriginalObject ride )
	{
		if ( RideView != null || ride.IsDeleted || !RideCameraMode.CanRide( ride )
			|| (!ReferenceEquals( ride, PlacedRide ) && !Objects.Objects.Contains( ride )) )
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
	// [APPROX:UI-095] the prior camera instance is restored, including the host terrain walk; native exit state remains unverified.
	public void ExitRideView()
	{
		if ( RideView == null )
			return;
		if ( ReferenceEquals( Camera.Mode, RideView ) )
			Camera.Mode = parkCameraBeforeRide ?? new ParkCameraMode();
		RideView = null;
		parkCameraBeforeRide = null;
	}
}
