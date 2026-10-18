namespace OpenTPW;

/// <summary>Native acceptance of immutable shared visits, without running the sandbox build/save smoke.</summary>
internal sealed class VisitSmokeTest : IDisposable
{
	private readonly Level level;
	private readonly OpenTPW.Hud.ParkHud? hud;
	private readonly System.Threading.Timer watchdog;
	private readonly int objectCount;
	private OriginalObject? riddenRide;
	private CameraMode? parkCamera;
	private Vector3 boardedEye;
	private float boardedAt;
	private int frame;
	private bool completed;
	/// <summary>What the ride step is waiting for, for the watchdog's message.</summary>
	private volatile string waiting = "";

	public VisitSmokeTest( Level level, OpenTPW.Hud.ParkHud? hud = null )
	{
		this.level = level;
		this.hud = hud;
		Require( level.IsReadOnlyVisit && level.Park == null, "shared visit has no economy" );
		// A version 2 package must have rebuilt the owner's exact layout (digest check; docs/ONLINE.md).
		// A package that publishes no digest (validation allows it) cannot be checked, only reported.
		if ( level.Visit!.Payload.HasBuiltLayout && level.Visit.Payload.LayoutDigest != null )
			Require( SharedParkLayout.DigestOf( level ) == level.Visit.Payload.LayoutDigest, "the visit rebuilt the published layout (digest)" );
		var originalObjects = level.Objects.Objects.ToArray();
		objectCount = originalObjects.Length;
		var originalRide = level.PlacedRide;
		var entry = level.Objects.Catalog.Buildable.FirstOrDefault();
		Require( entry != null, "local catalog contains a buildable object" );
		Require( level.PlaceObject( entry!, 0, 0, 0 ) == null, "object placement is rejected" );
		foreach ( var item in originalObjects )
			Require( !level.RemoveObjectAt( item.Placement.X, item.Placement.Y ), "existing object removal is rejected" );
		Require( !level.RemoveObjectAt( 0, 0 ), "remove tool is rejected" );
		Require( originalObjects.SequenceEqual( level.Objects.Objects ), "objects are unchanged" );
		Require( !level.PlaceRide( Vector3.Zero ), "prototype placement is rejected" );
		level.RemoveRide();
		Require( ReferenceEquals( originalRide, level.PlacedRide ), "prototype removal is rejected" );
		Reject( level.SaveSandbox, "saving is rejected" );
		Reject( level.LoadSandbox, "loading is rejected" );
		Reject( () => ParkSharing.ExportLevel( level, "Visit", "OpenTPW" ), "exporting a visited park is rejected" );
		// the ride step waits for the ride's own script to move it: the Jungle's Belly Bounce (Bouncy.RSE) bounces only with riders aboard, and the visit's
		// first guests get off the bus, through the booth and onto it 40 to 55 s into the visit
		watchdog = new System.Threading.Timer( _ =>
		{
			var state = waiting;
			Console.Error.WriteLine( $"Read-only visit smoke timed out before rendering or ride motion{(state.Length > 0 ? $" ({state})" : "")}." );
			Environment.Exit( 1 );
		}, null, TimeSpan.FromSeconds( 120 ), System.Threading.Timeout.InfiniteTimeSpan );
	}

	public void Update()
	{
		if ( ++frame < 8 || completed )
			return;
		Require( level.Park == null, "economy stays absent after simulation frames" );
		if ( frame == 8 )
		{
			// a ride published closed never runs: the first open rideable ride (the prototype after the park's objects), else the first rideable one
			var rideable = level.Objects.Objects.Where( RideCameraMode.CanRide ).Concat( level.PlacedRide is { } prototype ? [prototype] : [] ).ToList();
			riddenRide = rideable.FirstOrDefault( item => item.IsOpen ) ?? rideable.FirstOrDefault();
			if ( riddenRide != null )
			{
				parkCamera = Camera.Mode;
				if ( hud != null )
				{
					hud.SelectObject( riddenRide );
					hud.StartRide( riddenRide );
					Require( hud.IsRiding, "the visit HUD boards the published ride" );
				}
				else Require( level.EnterRideView( riddenRide ), "the visit boards the published ride" );
				Require( level.RideView != null && ReferenceEquals( Camera.Mode, level.RideView ),
					"the HUD and level share one ride camera" );
				Require( !level.EnterRideView( riddenRide ), "boarding again cannot overwrite the return camera" );
				Camera.Update();
				boardedEye = Camera.Position;
				boardedAt = Time.Now;
				Log.Trace( $"Visit smoke: boarded {riddenRide.Entry.DisplayName} ({(riddenRide.IsOpen ? "open" : "closed")}, {Riders( riddenRide )} riders aboard, "
					+ $"{level.Guests?.Guests.Count ?? 0} guests in the park); waiting for simulation frames to move it." );
				return;
			}
		}
		if ( riddenRide != null )
		{
			Require( level.RideView != null && ReferenceEquals( Camera.Mode, level.RideView ), "the ride camera remains level-owned" );
			// a ride with a seat is ridden from the seat's pose (RideCameraMode.Advance), whose view is smoothed and turned by the free look: the ride model's camera seat,
			// or a coaster's camera car; the others from the fallback frame
			var view = level.RideView!;
			if ( view.CurrentSeat is { } seat )
			{
				Require( (Camera.Position - new Vector3( seat.Translation.X, seat.Translation.Y, seat.Translation.Z )).Length < 0.001f, "the rendered ride camera sits on the ride's seat" );
				var node = riddenRide.Runtime?.Coaster == null ? RideSeat.FindNode( riddenRide ) : -1;
				Require( riddenRide.Runtime?.Coaster != null || (node >= 0 && (riddenRide.GetNodeTransform( node ).Translation - seat.Translation).Length() < 0.001f),
					"the seat is the ride model's camera seat" );
			}
			else
			{
				var (eye, look) = RideCameraMode.Frame( riddenRide, parkCamera!.Rotation.Forward, view.Train );
				Require( (Camera.Position - eye).Length < 0.001f, "the rendered ride camera follows the current ride frame" );
				Require( (Camera.Rotation.Forward - (look - eye).Normal).Length < 0.001f, "the camera looks along the ride direction" );
			}
			if ( (Camera.Position - boardedEye).Length < 0.25f )
			{
				waiting = FormattableString.Invariant( $"{riddenRide.Entry.DisplayName} {(riddenRide.IsOpen ? "open" : "closed")}: {Riders( riddenRide )} riders aboard, {riddenRide.Runtime?.Visitors.QueueLength ?? 0} queued, {level.Guests?.Guests.Count ?? 0} guests in the park, camera moved {(Camera.Position - boardedEye).Length:F2} of 0.25" );
				return; // Actual simulation frames must move the ride; repeated camera.Update calls alone prove nothing.
			}
		}
		var capture = SandboxSmokeTest.CaptureFrame( "online-visit.png" );
		Require( capture.Width > 0 && capture.Height > 0, "visit renders a real framebuffer" );
		var rideReport = ", no rideable ride in this park";
		if ( riddenRide != null )
		{
			var riddenEye = Camera.Position;
			var riders = Riders( riddenRide );
			var seconds = Time.Now - boardedAt;
			if ( hud != null ) hud.StopRide(); else level.ExitRideView();
			Require( level.RideView == null && ReferenceEquals( Camera.Mode, parkCamera ), "leaving the ride restores the same park camera" );
			Require( hud?.IsRiding != true, "the HUD agrees the ride view has ended" );
			level.ExitRideView();
			Require( ReferenceEquals( Camera.Mode, parkCamera ), "a repeated exit preserves the restored camera" );
			rideReport = FormattableString.Invariant( $", rode {riddenRide.Entry.DisplayName} through {(hud != null ? "HUD" : "level")} (moved {seconds:F1} s after boarding with {riders} {(riders == 1 ? "rider" : "riders")} aboard; camera {boardedEye.X:F2},{boardedEye.Y:F2},{boardedEye.Z:F2} → {riddenEye.X:F2},{riddenEye.Y:F2},{riddenEye.Z:F2})" );
		}
		Require( level.Objects.Objects.Count == objectCount && level.Park == null, "riding changed no objects or economy" );
		if ( level.Visit!.Payload.HasBuiltLayout && level.Visit.Payload.LayoutDigest != null )
			Require( SharedParkLayout.DigestOf( level ) == level.Visit.Payload.LayoutDigest, "riding preserves the published layout digest" );
		completed = true;
		Log.Trace( $"Read-only visit smoke passed: {frame} frames, {level.Objects.Objects.Count} immutable local objects"
			+ (level.Visit!.Payload.HasBuiltLayout && level.Visit.Payload.LayoutDigest != null ? $", layout digest {level.Visit.Payload.LayoutDigest} verified" : "")
			+ rideReport
			+ $", no economy, build/remove/save/load/export rejected, GPU capture {capture.Width}x{capture.Height}." );
		Render.Window.SdlWindow.Close();
	}

	/// <summary>The guests on the ride now: its script's VAR_ONRIDE, or the bridge's riders (a coaster's on its trains).</summary>
	private static int Riders( OriginalObject ride ) => ride.Runtime?.Visitors is { } visitors ? Math.Max( visitors.RidersOnBoard, visitors.Riders.Count ) : 0;

	private static void Reject( Action action, string step )
	{
		try { action(); }
		catch ( InvalidOperationException ) { return; }
		throw new InvalidOperationException( "Read-only visit smoke failed: " + step );
	}

	private static void Require( bool condition, string step )
	{
		if ( !condition )
			throw new InvalidOperationException( "Read-only visit smoke failed: " + step );
	}

	public void VerifyCompleted() => Require( completed, "rendering completed" );

	public void Dispose()
	{
		watchdog.Dispose();
		Render.PostUpdate -= Update;
	}
}
