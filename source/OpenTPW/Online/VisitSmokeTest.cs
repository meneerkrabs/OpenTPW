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
	private int frame;
	private bool completed;

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
		watchdog = new System.Threading.Timer( _ =>
		{
			Console.Error.WriteLine( "Read-only visit smoke timed out before rendering or ride motion." );
			Environment.Exit( 1 );
		}, null, TimeSpan.FromSeconds( 60 ), System.Threading.Timeout.InfiniteTimeSpan );
	}

	public void Update()
	{
		if ( ++frame < 8 || completed )
			return;
		Require( level.Park == null, "economy stays absent after simulation frames" );
		if ( frame == 8 )
		{
			riddenRide = level.Objects.Objects.FirstOrDefault( RideCameraMode.CanRide ) ?? level.PlacedRide;
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
				return;
			}
		}
		if ( riddenRide != null )
		{
			Require( level.RideView != null && ReferenceEquals( Camera.Mode, level.RideView ), "the ride camera remains level-owned" );
			var (eye, look) = RideCameraMode.Frame( riddenRide, parkCamera!.Rotation.Forward );
			Require( (Camera.Position - eye).Length < 0.001f, "the rendered ride camera follows the current ride frame" );
			Require( (Camera.Rotation.Forward - (look - eye).Normal).Length < 0.001f, "the camera looks along the ride direction" );
			if ( (Camera.Position - boardedEye).Length < 0.25f )
				return; // Actual simulation frames must move the ride; repeated camera.Update calls alone prove nothing.
		}
		var capture = SandboxSmokeTest.CaptureFrame( "online-visit.png" );
		Require( capture.Width > 0 && capture.Height > 0, "visit renders a real framebuffer" );
		var rideReport = ", no rideable ride in this park";
		if ( riddenRide != null )
		{
			var riddenEye = Camera.Position;
			if ( hud != null ) hud.StopRide(); else level.ExitRideView();
			Require( level.RideView == null && ReferenceEquals( Camera.Mode, parkCamera ), "leaving the ride restores the same park camera" );
			Require( hud?.IsRiding != true, "the HUD agrees the ride view has ended" );
			level.ExitRideView();
			Require( ReferenceEquals( Camera.Mode, parkCamera ), "a repeated exit preserves the restored camera" );
			rideReport = FormattableString.Invariant( $", rode {riddenRide.Entry.DisplayName} through {(hud != null ? "HUD" : "level")} (camera {boardedEye.X:F2},{boardedEye.Y:F2},{boardedEye.Z:F2} → {riddenEye.X:F2},{riddenEye.Y:F2},{riddenEye.Z:F2})" );
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
