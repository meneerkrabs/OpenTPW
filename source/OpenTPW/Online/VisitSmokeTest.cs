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
	private readonly Action<OpenTPW.UI.Original.UiInput>? injectInput;
	/// <summary>The scripted online players (<c>--smoke-presence</c>), or null.</summary>
	private readonly VisitPresenceSmoke? presence;
	private bool ridePhaseDone;
	private string rideReport = "";
	private (int Width, int Height) captureSize;

	/// <param name="injectInput">Hands the next frame's input to the flow (the online players' HUD click).</param>
	/// <param name="attachPresence">Attaches the scripted online players to the flow (null: the visit has none).</param>
	public VisitSmokeTest( Level level, OpenTPW.Hud.ParkHud? hud = null, Action<OpenTPW.UI.Original.UiInput>? injectInput = null, Func<Level, VisitPresenceSmoke?>? attachPresence = null )
	{
		this.level = level;
		this.hud = hud;
		this.injectInput = injectInput;
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
		if ( attachPresence != null )
		{
			presence = attachPresence( level );
			Require( presence != null, "the scripted online players attach to the visit" );
			// the names are drawn in the overlay pass: the players' capture reads the whole output
			Render.CaptureOutput = true;
		}
		watchdog = new System.Threading.Timer( _ =>
		{
			Console.Error.WriteLine( "Read-only visit smoke timed out before rendering or ride motion." );
			Environment.Exit( 1 );
		}, null, TimeSpan.FromSeconds( 60 ), System.Threading.Timeout.InfiniteTimeSpan );
	}

	public void Update()
	{
		presence?.Update( Time.Delta, injectInput, hud );
		if ( ++frame < 8 || completed )
			return;
		if ( ridePhaseDone )
		{
			Finish();
			return;
		}
		Require( level.Park == null, "economy stays absent after simulation frames" );
		// the online players' checks need the park view and its HUD clicks, so their run leaves the ride step to the plain visit smoke
		if ( frame == 8 && presence != null )
		{
			ridePhaseDone = true;
			rideReport = ", ride step left to the plain visit smoke";
			Finish();
			return;
		}
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
				Log.Trace( $"Visit smoke: boarded {riddenRide.Entry.DisplayName}." );
				return;
			}
		}
		if ( riddenRide != null )
		{
			Require( level.RideView != null && ReferenceEquals( Camera.Mode, level.RideView ), "the ride camera remains level-owned" );
			// a ride whose model has a camera seat is ridden from the seat's pose (RideCameraMode.Advance), whose view is smoothed and turned by the free look;
			// the others from the fallback frame
			var seat = riddenRide.Runtime is { Coaster: null } ? RideSeat.FindNode( riddenRide ) : -1;
			if ( seat >= 0 && seat < riddenRide.NodeTransforms.Count )
			{
				var pose = riddenRide.GetNodeTransform( seat ).Translation;
				Require( (Camera.Position - new Vector3( pose.X, pose.Y, pose.Z )).Length < 0.001f, "the rendered ride camera sits on the ride's seat" );
			}
			else
			{
				var (eye, look) = RideCameraMode.Frame( riddenRide, parkCamera!.Rotation.Forward );
				Require( (Camera.Position - eye).Length < 0.001f, "the rendered ride camera follows the current ride frame" );
				Require( (Camera.Rotation.Forward - (look - eye).Normal).Length < 0.001f, "the camera looks along the ride direction" );
			}
			if ( (Camera.Position - boardedEye).Length < 0.25f )
				return; // Actual simulation frames must move the ride; repeated camera.Update calls alone prove nothing.
		}
		var capture = SandboxSmokeTest.CaptureFrame( "online-visit.png" );
		Require( capture.Width > 0 && capture.Height > 0, "visit renders a real framebuffer" );
		captureSize = (capture.Width, capture.Height);
		rideReport = ", no rideable ride in this park";
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
		ridePhaseDone = true;
		Log.Trace( $"Visit smoke: the visit checks passed at frame {frame}{(presence != null ? "; checking the online players" : "")}." );
		Finish();
	}

	/// <summary>Ends the smoke once the online players' checks (if any) are through: they need the park view, so they run after the ride.</summary>
	private void Finish()
	{
		if ( presence is { Completed: false } )
		{
			Require( frame < 3000, "the online players' checks finish" );
			return;
		}
		if ( presence != null )
		{
			var players = SandboxSmokeTest.CaptureOutputFrame( "online-visit-presence.png" );
			Require( players.Width > 0 && players.Height > 0, "the visit with online players renders a real framebuffer" );
			captureSize = (players.Width, players.Height);
		}
		Require( level.Objects.Objects.Count == objectCount && level.Park == null, "the online players changed no objects or economy" );
		if ( level.Visit!.Payload.HasBuiltLayout && level.Visit.Payload.LayoutDigest != null )
			Require( SharedParkLayout.DigestOf( level ) == level.Visit.Payload.LayoutDigest, "the visit keeps the published layout digest" );
		completed = true;
		Log.Trace( $"Read-only visit smoke passed: {frame} frames, {level.Objects.Objects.Count} immutable local objects"
			+ (level.Visit!.Payload.HasBuiltLayout && level.Visit.Payload.LayoutDigest != null ? $", layout digest {level.Visit.Payload.LayoutDigest} verified" : "")
			+ rideReport + (presence?.Report ?? "")
			+ $", no economy, build/remove/save/load/export rejected, GPU capture {captureSize.Width}x{captureSize.Height}." );
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
