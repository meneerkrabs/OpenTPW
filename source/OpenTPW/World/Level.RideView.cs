namespace OpenTPW;

public partial class Level
{
	/// <summary>The first-person ride view while the player rides a placed ride; null in the park camera.</summary>
	public RideCameraMode? RideView { get; private set; }

	private CameraMode? parkCameraBeforeRide;

	/// <summary>True while the ride view is open: the original's camcorder hides every interface layer but the viewfinder, the eject button and the advisor ([DATA:video 77:09-77:24]).</summary>
	public bool HidesInterface => RideView != null;

	/// <summary>
	/// Enters the ride view of a placed ride from the park camera: the original's "Ride it!" (ride window,
	/// <c>gui_CameraFlags</c> 4; maexah/OpenTPW hud.md, "Four ways out of camcorder mode"). False when the ride
	/// cannot be ridden (no completed coaster track and not the prototype ride) or the view is already open.
	/// </summary>
	// [APPROX:UI-093] the entry is the selected ride's key (E), not the original's "Ride it!" button, whose art and position are not in the repository; the heading is the park camera's forward
	public bool EnterRideView( OriginalObject ride )
	{
		if ( RideView != null || ride.IsDeleted || !RideCameraMode.CanRide( ride )
			|| (!ReferenceEquals( ride, PlacedRide ) && !Objects.Objects.Contains( ride )) )
			return false;
		parkCameraBeforeRide = Camera.Mode;
		var coaster = ride.Runtime?.Coaster;
		RideView = new RideCameraMode( ride, Camera.Rotation.Forward,
			coaster != null && coasterRenderer is { } renderer ? () => renderer.TryGetSeat( coaster ) : null );
		RideViewOpenedAt = Time.Now;
		Camera.Mode = RideView;
		return true;
	}

	/// <summary>
	/// Leaves the ride view and restores the park camera it was entered from. The original leaves to the
	/// first-person walk when the ride was entered from it, else to the orbit (ride-view exits: Escape, the
	/// eject button, and a right click with RMB cancel on).
	/// </summary>
	// [BIN:TPW-EXE:0x42a190 ride exit] the exit clears the seat node's mark and, when the camera mode toggled off (0x42a560), takes the cell under the park camera's target and looks for a thing of type 3 there (0x42a340); if there is one the park camera's target moves to the centre of that thing's cell (0x42a1d0: (index - 1 & 0x7f) * 10 + 5 and (index - 1 >> 7) * 10 + 5), otherwise it stays; the camcorder walk (0x42ae70) instead restores the saved target and yaw exactly
	// [APPROX:UI-095] which thing the original's type 3 with +0x118 == 0 is is not identified, so any placed object under the park camera's target stands in; the original also sets the park yaw from the thing's direction bits (0x522850 & 0x55: 1 pi, 4 pi/2, 0x10 0, 0x40 3 pi/2), which is not applied because its mapping to OpenTPW's yaw is not traced and the video (77:07 and 77:24) shows an unchanged yaw; the prior camera instance is restored, including the host terrain walk
	public void ExitRideView()
	{
		if ( RideView == null )
			return;
		if ( ReferenceEquals( Camera.Mode, RideView ) )
		{
			Camera.Mode = parkCameraBeforeRide ?? new ParkCameraMode();
			if ( Camera.Mode is ParkCameraMode parkCamera )
				RecentreParkCamera( parkCamera );
		}
		RideView = null;
		parkCameraBeforeRide = null;
	}

	/// <summary>When the ride view was opened, in seconds of <see cref="Time.Now"/>: the right-click exit waits two seconds from here.</summary>
	public float RideViewOpenedAt { get; private set; }

	/// <summary>Moves the park camera's target to the cell centre of the object under it, as the original does when a ride is left.</summary>
	internal void RecentreParkCamera( ParkCameraMode camera )
	{
		if ( OriginalPark?.Heightfield is not { } field || !OriginalParkPlacement.TryGetCell( field, camera.Target.X, camera.Target.Y, out var x, out var y ) )
			return;
		if ( Objects.FindAt( x, y ) is not { IsDeleted: false } thing || thing.Entry.IsFixedItem )
			return;
		var center = OriginalParkPlacement.GetCellCenter( field, thing.Placement.X, thing.Placement.Y );
		camera.Focus( new Vector3( center.X, center.Y, center.Z ) );
	}
}
