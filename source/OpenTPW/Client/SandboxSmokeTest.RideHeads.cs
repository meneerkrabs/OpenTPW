namespace OpenTPW;

internal sealed partial class SandboxSmokeTest
{
	private bool rideHeadPixelsVerified;
	private int rideHeadReadbackPhase;
	private int rideHeadCameraAttempt;
	private OriginalObject? rideHeadReadbackRide;
	private Vector3 rideHeadReadbackTarget;
	private CameraMode? beforeRideHeadCamera;
	private Action? beforeRideHeadUpdate;
	private Action? freezeRideHeadTime;
	private byte[]? visibleRideHeadPixels;
	private byte[]? hiddenRideHeadPixels;

	/// <summary>
	/// Isolates one naturally boarded, animating original ride, freezes updates and shader time, and keeps its camera
	/// fixed while toggling only the head renderer. Identical repeated hidden/visible frames rule out other pixel motion.
	/// The submitted-quad counter is a prerequisite, not the visibility assertion.
	/// </summary>
	private bool StepRideHeadReadback()
	{
		if ( level.OriginalPark == null || rideHeadPixelsVerified || level.RideHeadRenderer == null || level.Guests == null )
			return false;
		var renderer = level.RideHeadRenderer;
		if ( rideHeadReadbackPhase == 0 )
		{
			var candidate = level.Objects.Objects.Where( ride => !ride.IsDeleted && ride.IsAnimating
				&& objectMotion.TryGetValue( ride, out var motion ) && motion.MovedFrames >= 10 )
				.SelectMany( ride => ride.Visitors.HungHeads()
					.Where( head => RideHeadRenderer.CanDraw( level.Guests.Find( head.Visitor ), ride.Visitors.AttractionId ) )
					.Select( head => (Ride: ride, Head: head)) ).FirstOrDefault();
			if ( candidate.Ride == null )
				return false;
			rideHeadReadbackRide = candidate.Ride;
			var target = candidate.Ride.GetNodeTransform( candidate.Head.Node ).Translation;
			rideHeadReadbackTarget = new Vector3( target.X, target.Y, target.Z );
			beforeRideHeadCamera = Camera.Mode;
			beforeRideHeadUpdate = Render.OnUpdate;
			Render.OnUpdate = null;
			var frozenTime = Time.Now;
			freezeRideHeadTime = () => { Time.Now = frozenTime; Time.Delta = 0; };
			Render.PreUpdate += freezeRideHeadTime;
			renderer.OnlyAttraction = candidate.Ride.Visitors.AttractionId;
			SetRideHeadReadbackCamera();
			Log.Trace( $"Ride head pixel readback: frozen {candidate.Ride.Entry.DisplayName}, attraction {candidate.Ride.Visitors.AttractionId}, visitor {candidate.Head.Visitor}, node {candidate.Head.Node}, world {target}, {objectMotion[candidate.Ride].MovedFrames} moving frames before freeze." );
			rideHeadReadbackPhase = 1;
			return true;
		}
		switch ( rideHeadReadbackPhase )
		{
			case 1:
				Require( renderer.DrawnHeads > 0, "submit head quads on the isolated matching attraction" );
				visibleRideHeadPixels = CaptureFrame( "ride-heads-visible.png" ).Pixels;
				renderer.Visible = false;
				break;
			case 2:
				hiddenRideHeadPixels = CaptureFrame( "ride-heads-hidden.png" ).Pixels;
				break;
			case 3:
				Require( CaptureFrame( "ride-heads-hidden-repeat.png" ).Pixels.AsSpan().SequenceEqual( hiddenRideHeadPixels ),
					"frozen hidden head frames have identical world pixels" );
				renderer.Visible = true;
				break;
			case 4:
				var repeated = CaptureFrame( "ride-heads-visible-repeat.png" ).Pixels;
				Require( repeated.AsSpan().SequenceEqual( visibleRideHeadPixels ), "frozen visible head frames have identical world pixels" );
				var changed = 0;
				for ( var index = 0; index < repeated.Length; index += 4 )
					if ( !repeated.AsSpan( index, 3 ).SequenceEqual( hiddenRideHeadPixels!.AsSpan( index, 3 ) ) )
						changed++;
				if ( changed == 0 && ++rideHeadCameraAttempt < 4 )
				{
					// A head behind a ride mesh is legitimately occluded. Keep the same frozen pose and try another view.
					SetRideHeadReadbackCamera();
					rideHeadReadbackPhase = 1;
					return true;
				}
				Require( changed > 0, "isolated rider heads change GPU pixels on the frozen identified ride" );
				Log.Trace( $"Ride head isolated GPU readback: {rideHeadReadbackRide!.Entry.DisplayName}, {renderer.DrawnHeads} submitted head quads, {changed} changed pixels; repeated visible and hidden frames identical, simulation/animation/camera/shader time frozen." );
				rideHeadPixelsVerified = true;
				RestoreRideHeadReadback();
				return true;
		}
		rideHeadReadbackPhase++;
		return true;
	}

	private void SetRideHeadReadbackCamera()
	{
		var angle = rideHeadCameraAttempt * MathF.PI / 2;
		var offset = new Vector3( MathF.Cos( angle ) * 9, MathF.Sin( angle ) * 9, 7 );
		var position = rideHeadReadbackTarget + offset;
		Camera.Mode = new CameraMode { Position = position, Rotation = Rotation.LookAt( rideHeadReadbackTarget - position ), FieldOfView = 55 };
		Camera.Update();
	}

	private void RestoreRideHeadReadback()
	{
		if ( beforeRideHeadCamera == null )
			return;
		Render.PreUpdate -= freezeRideHeadTime;
		Render.OnUpdate = beforeRideHeadUpdate;
		Camera.Mode = beforeRideHeadCamera;
		Camera.Update();
		level.RideHeadRenderer!.OnlyAttraction = null;
		level.RideHeadRenderer.Visible = true;
		beforeRideHeadCamera = null;
	}
}
