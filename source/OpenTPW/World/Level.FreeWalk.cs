namespace OpenTPW;

public partial class Level
{
	public FreeWalkCameraMode? FreeWalk { get; private set; }
	private CameraMode? cameraBeforeFreeWalk;

	/// <summary>The park camera the free walk was entered from while it lasts, else null.</summary>
	internal ParkCameraMode? ParkCameraWhileWalking => FreeWalk != null ? cameraBeforeFreeWalk as ParkCameraMode : null;

	public bool EnterFreeWalk( Vector3 position )
	{
		if ( GameEditions.Current != GameEdition.ThemeParkWorld || FreeWalk != null || RideView != null
			|| Camera.Mode is not ParkCameraMode || OriginalPark?.Heightfield is not { } field
			|| !FreeWalkCameraMode.TryGround( field, position.X, position.Y, out var ground ) ) return false;
		cameraBeforeFreeWalk = Camera.Mode;
		FreeWalk = new FreeWalkCameraMode( field, ground, Camera.Mode.Rotation.Forward );
		Camera.Mode = FreeWalk;
		return true;
	}

	public void ExitFreeWalk()
	{
		if ( FreeWalk == null ) return;
		if ( RideView != null ) ExitRideView();
		if ( ReferenceEquals( Camera.Mode, FreeWalk ) )
		{
			Camera.Mode = cameraBeforeFreeWalk ?? new ParkCameraMode();
			if ( Camera.Mode is ParkCameraMode park && !EntityConstructionScope.IsStaging ) park.UpdateAudioListener();
		}
		FreeWalk = null;
		cameraBeforeFreeWalk = null;
	}

	internal bool TryEnterFreeWalk( System.Numerics.Vector2 mouse, System.Numerics.Vector2 viewport ) =>
		ParkPlacement.TryGetGroundPoint( mouse, viewport, Camera.ViewMatrix, Camera.ProjMatrix, out var point ) && EnterFreeWalk( point );
}
