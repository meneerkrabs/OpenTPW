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

	/// <summary>True while the ride view or the camcorder walk is open: the original's camcorder hides every interface layer but the viewfinder, the eject button and the advisor ([DATA:video 77:09-77:24]).</summary>
	// [BIN:TPW-EXE:0x42AB20 hide the interface] entering the walk (mode 2) or the ride (mode 4) from the park view hides the HUD window and shows the camcorder window (0x481470 -> 0x4A2AC0: message 6 to 0x7CB2AC and 0x7CB2B0)
	public bool HidesInterface => RideView != null || FreeWalk != null || LossOverlayActive;

	/// <summary>
	/// True while the lost park's hidden-HUD overlay is up: the HUD is hidden and only the loss banner shows, and the park camera is the player's (ECON-030, <c>ParkHud.LossWindow</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x4A29D0 hide HUD] the overlay window 0xDDF22 with its procedure 0x4A2840 is built and the HUD root [0x7CB2AC] hidden (0x65D9DD), the flag [0x7CB2E8] set
	public bool LossOverlayActive { get; internal set; }

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
	/// The winning screen's camera: the ride view of the finale object End, whose model carries the camera seat (seat 1, node 38 in the shipped End.MD2); false, and the camera
	/// unchanged, when its model has no seat or a ride view is already open. The view follows the seat as End's animation moves it and takes the pointer's free look like any ride.
	/// </summary>
	// [BIN:TPW-EXE:0x0048AEBA winning camera] the winning screen calls 0x4E15B0(0) on the finale thing, the Ride it! routine: 0x42A560(model, type +0x80, 0, 0, 1) looks up seat 1 with 0x44B220(model, 0x1000, 1) and enters the seat camera, mode 4 (0x42AB20); without the seat it leaves the camera alone (0x42A6EE)
	public bool EnterWinningView( OriginalObject end )
	{
		if ( RideView != null || end.IsDeleted || end.Runtime?.Model is not { } model || !Objects.Objects.Contains( end ) )
			return false;
		// the seat is the model's (a new End has no pose until its first animation step), followed from its pose once there is one
		var seat = RideSeat.FindNode( model );
		if ( seat < 0 )
			return false;
		parkCameraBeforeRide = Camera.Mode;
		RideView = new RideCameraMode( end, Camera.Rotation.Forward, () => seat < end.NodeTransforms.Count ? end.GetNodeTransform( seat ) : null ) { LooksUnderWindows = true };
		RideViewOpenedAt = Time.Now;
		Camera.Mode = RideView;
		return true;
	}

	/// <summary>
	/// The camera of the lost park: the camera seat of the park's gate (the entrance gate's fixed item <c>Gates</c>), entered as the winning screen enters End's. False, and the camera unchanged, when the park has no
	/// gate, its model has no seat or a ride view is already open.
	/// </summary>
	// [BIN:TPW-EXE:0x0048AC84 loss camera] the loss window's builder 0x48AC40 gets the gate thing from the bankruptcy (0x5168F0 passes the thing the world +0x1DA732 names) and calls 0x4E15B0(0) on it, the Ride it! routine: 0x42A560 looks up camera seat 1 in the gate model (the shipped Gates models carry the record, type 0x1031 value 1) and enters the seat camera, mode 4; without the seat it leaves the camera alone (0x42A6EE)
	public bool EnterLossView() => Objects.Objects.FirstOrDefault( IsGateObject ) is { } gate && EnterWinningView( gate );

	/// <summary>Whether <paramref name="item"/> is the park's entrance gate (the fixed item <c>Gates</c>).</summary>
	internal static bool IsGateObject( OriginalObject item ) => !item.IsDeleted && item.Entry.SettingsName.Equals( "Gates", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// Leaves the ride view and restores the camera it was entered from: the park camera as it was, or the camcorder walk, whose walker is put at the exit of the ride it went on through the entrance.
	/// </summary>
	// [BIN:TPW-EXE:0x42a190 leaving the ride] 0x42A560 toggles the ride camera off through 0x42AB20 (mode 4), which answers 1 only when the ride was entered from the walk (flag 0x400, set at 0x42ABxx when the walk bit was on): then the walk
	// resumes (flags 2) and 0x42A1D0 puts the walker at the exit of the ride whose entrance holds its cell (0x42A340, the exit cell named by the thing's +0x38, 0x42A22E) looking where the exit cell's link byte says (0x522850 & 0x55: 1 pi,
	// 4 pi/2, 0x10 0, 0x40 3 pi/2); a ride entered from the park view leaves the park camera where it was
	public void ExitRideView()
	{
		if ( RideView == null )
			return;
		if ( ReferenceEquals( Camera.Mode, RideView ) )
		{
			Camera.Mode = parkCameraBeforeRide ?? new ParkCameraMode();
			if ( Camera.Mode is FreeWalkCameraMode walker )
				PlaceWalkerAtRideExit( walker );
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
	// [BIN:TPW-EXE:0x42a1d0 ride exit yaw] the byte is the exit cell's +0xd (0x522850 read on the cell record named by mExitPos, index (+0x38 - 1) × 0x44, not a direction of the thing): masked with 0x55 it selects the park yaw 0x790a38 by a switch with no default: 1 → π, 4 → π/2, 0x10 → 0, 0x40 → 3π/2
	internal static int RideExitDirection( int linkByte ) => (linkByte & 0x55) switch { 1 => 0, 4 => 1, 0x10 => 2, 0x40 => 3, _ => -1 };

	/// <summary>The ride whose entrance holds a cell: the cell is an entrance (type 9) whose owner's thing list names a ride (<see cref="IsRideExitThing"/>), else null.</summary>
	// [BIN:TPW-EXE:0x42a340] type 9 -> owner +0x10 -> owner cell's thing list +0x24 -> type 3 with CannotRide 0.
	internal static OriginalObject? EntranceRide( ParkObjects objects, ParkCellMap cells, int x, int y )
	{
		if ( !cells.InBounds( x, y ) || cells.RawTypeAt( x, y ) != ParkCellTypes.Entrance || cells.OwnerAt( x, y ) == 0 )
			return null;
		var owner = cells.OwnerAt( x, y );
		return objects.Objects.FirstOrDefault( item => OwnerWord( item ) == owner && IsRideExitThing( item ) );
	}

	/// <summary>Resolves the live entrance owner to its last shape exit and that exit's direction byte, never to the footprint anchor or a neighbour-link byte.</summary>
	// [BIN:TPW-EXE:0x529164] shape type 10 selects 0x529239; 0x5292A7 rotates its direction and 0x5292B2 writes +0x0D through 0x5227E0. The placement retains the last exit, not the entrance or anchor.
	// [APPROX:UI-095] the exit is the live object's last shape exit, as the placement writes the thing's +0x38; a saved +0x38 that differs from it is not imported
	internal static (int X, int Y, byte Direction)? ResolveRideExit( ParkObjects objects, ParkCellMap cells, int x, int y )
	{
		if ( EntranceRide( objects, cells, x, y ) is not { } thing )
			return null;
		ObjectAccessPoint? exit = null;
		foreach ( var point in thing.AccessPoints )
			if ( point.Kind == ObjectCellKind.Exit )
				exit = point;
		if ( exit is not { } end || !cells.InBounds( end.X, end.Y ) )
			return null;
		return (end.X, end.Y, cells.QueueLinkAt( end.X, end.Y ));
	}

	/// <summary>The yaw the exit's link byte gives the walker in radians (<see cref="RideExitDirection"/>'s rule): null leaves the yaw alone.</summary>
	internal static float? RideExitYaw( int linkByte ) => (linkByte & 0x55) switch { 1 => MathF.PI, 4 => 1.5707964f, 0x10 => 0f, 0x40 => 4.712389f, _ => null };

	/// <summary>Puts the walker at the centre of the exit cell of the ride whose entrance holds its cell, looking along the exit's direction (<c>0x42A1D0</c>).</summary>
	internal void PlaceWalkerAtRideExit( FreeWalkCameraMode walker )
	{
		if ( Guests?.Grid.Cells is not { } cells )
			return;
		var (x, y) = walker.Cell;
		if ( ResolveRideExit( Objects, cells, Math.Clamp( x, 0, cells.Width - 1 ), Math.Clamp( y, 0, cells.Height - 1 ) ) is not { } exit )
			return;
		// [BIN:TPW-EXE:0x42A256 exit cell centre] x = 5 + 10 x the cell's column and z = 5 + 10 x its row (0x6FDD5C -10, 0x6FDD60 5)
		walker.PlaceAt( 5f + 10f * exit.X, 5f + 10f * exit.Y, RideExitYaw( exit.Direction ) );
	}
}
