namespace OpenTPW;

public partial class Level
{
	/// <summary>The first-person ride view while the player rides a placed ride; null in the park camera.</summary>
	public RideCameraMode? RideView { get; private set; }

	private CameraMode? parkCameraBeforeRide;

	/// <summary>Ride view suppresses new guest bubble work, while thoughts, voice and ordinary expiry continue. A retired simulation cannot follow a replacement's camera state.</summary>
	internal void BindGuestThoughtPresentation( GuestSimulation simulation ) =>
		simulation.ThoughtBubblesAllowed = () => ReferenceEquals( Guests, simulation ) && RideView == null;

	/// <summary>The park camera the ride was boarded from while the ride view is open, else null: its target stays where it was (the crowd sound counts around it).</summary>
	internal ParkCameraMode? ParkCameraWhileRiding => RideView != null ? parkCameraBeforeRide as ParkCameraMode : null;

	/// <summary>True while the ride view is open: the original's camcorder hides every interface layer but the viewfinder, the eject button and the advisor ([DATA:video 77:09-77:24]).</summary>
	public bool HidesInterface => RideView != null;

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
		var coaster = ride.Runtime?.Coaster;
		// the ride camera takes the coaster's train once, as it opens (0x42a560 → 0x436880)
		var train = coaster?.RideViewTrain ?? 0;
		RideView = new RideCameraMode( ride, Camera.Rotation.Forward,
			coaster != null && coasterRenderer is { } renderer ? () => renderer.TryGetSeat( coaster, train ) : null, train );
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
	// [APPROX:UI-095] the prior camera instance is restored, including the host terrain walk; the original's park camera is its own pose (yaw, height, smoothing: 0x42b1c0), which OpenTPW's host park camera does not reproduce, so the exit's pose change is limited to the target and the look direction; whether the original's object cells carry a link byte at all is not traced (OpenTPW's hold none, so the video's unchanged yaw at 77:07 and 77:24 is the rule's no-match case)
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

	/// <summary>
	/// Whether <paramref name="thing"/> is the object the exit looks for under the park camera's target: a placed object (the original's thing type 3) whose type has
	/// <c>UsageInfo.CannotRide</c> 0 (type record <c>+0x118</c>), which the shipped rides have and every shop, sideshow and feature (CannotRide 1) lacks.
	/// </summary>
	// [BIN:TPW-EXE:0x42a340 ride exit thing] the cell's thing list is walked for the first thing whose type byte (+2) is 3 and whose type record (0x4dd4e0 → 0x412e90) has +0x118 == 0; the key table at 0x7456e8 puts UsageInfo key 21 (CannotRide) at +0x118 (key 28, MinSpeed, is the known +0x134, four bytes a key)
	internal static bool IsRideExitThing( OriginalObject thing ) => thing is { IsDeleted: false } && thing.Entry.Settings.GetInt( "UsageInfo.CannotRide" ) == 0;

	/// <summary>
	/// The <see cref="GuestPathGrid.Directions"/> index the exit's yaw rule picks from the link byte of the thing's cell: 0, 1, 2 or 3 when exactly one of the link
	/// bits 1, 4, 16 and 64 is set (the byte masked with 0x55), else -1, which leaves the yaw alone.
	/// </summary>
	// [BIN:TPW-EXE:0x42a1d0 ride exit yaw] the byte is the map cell's +0xd (0x522850 read on the cell record of the thing's cell, index (+0x38 - 1) × 0x44, not a direction of the thing): masked with 0x55 it selects the park yaw 0x790a38 by a switch with no default: 1 → π, 4 → π/2, 0x10 → 0, 0x40 → 3π/2
	internal static int RideExitDirection( int linkByte ) => (linkByte & 0x55) switch { 1 => 0, 4 => 1, 0x10 => 2, 0x40 => 3, _ => -1 };

	/// <summary>
	/// Moves the park camera's target to the cell centre of the ride-exit object under it (<see cref="IsRideExitThing"/>) and, when that cell's link byte names a direction
	/// (<see cref="RideExitDirection"/>), turns the camera to look along it, as the original does when a ride is left.
	/// </summary>
	// [BIN:TPW-EXE:0x42a1d0 ride exit camera] the camera's forward on the ground is (sin yaw, cos yaw) in the original's (x, z) = the cell's (x, y) (the eye sits behind the target by the rotated offset 0x790aa0..0x790aa8, 0x42b1c0), so the yaws 0, π/2, π and 3π/2 of the four link bits look along +y, +x, -y and -x: the link's own direction, the order of GuestPathGrid.Directions
	internal void RecentreParkCamera( ParkCameraMode camera )
	{
		if ( OriginalPark?.Heightfield is not { } field || !OriginalParkPlacement.TryGetCell( field, camera.Target.X, camera.Target.Y, out var x, out var y ) )
			return;
		if ( Objects.FindAt( x, y ) is not { } thing || !IsRideExitThing( thing ) )
			return;
		var center = OriginalParkPlacement.GetCellCenter( field, thing.Placement.X, thing.Placement.Y );
		camera.Focus( new Vector3( center.X, center.Y, center.Z ) );
		if ( Guests?.Grid.Cells is { } cells && RideExitDirection( cells.LinksAt( thing.Placement.X, thing.Placement.Y ) ) is var direction and >= 0 )
		{
			var (dx, dy) = GuestPathGrid.Directions[direction];
			var ahead = OriginalParkPlacement.GetCellCenter( field, thing.Placement.X + dx, thing.Placement.Y + dy );
			camera.LookAlong( new System.Numerics.Vector2( ahead.X - center.X, ahead.Y - center.Y ) );
		}
	}
}
