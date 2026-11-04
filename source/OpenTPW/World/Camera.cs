using System.Numerics;

namespace OpenTPW;

public static class Camera
{
	public static Vector3 Position => CameraMode?.Position ?? Vector3.Zero;
	public static Rotation Rotation => CameraMode?.Rotation ?? Rotation.Identity;

	public static Matrix4x4 ViewMatrix { get; set; }
	public static Matrix4x4 ProjMatrix { get; set; }

	private static CameraMode CameraMode = new();

	/// <summary>The active camera mode, e.g. to restore a park's camera after the online globe.</summary>
	public static CameraMode Mode { get => CameraMode; set => CameraMode = value; }

	/// <summary>
	/// The park view's far plane in engine units for the camera's view matrix, or null: the farthest corner distance of the span selection (<c>0x56BB00</c> hands it to <c>0x578CA0</c> every frame, which rebuilds the projection with it);
	/// set by the level's terrain.
	/// </summary>
	public static Func<Matrix4x4, float, float?>? ParkFarPlane
	{
		get => parkFarPlane;
		set
		{
			parkFarPlane = value;
			parkFarLag = null;
		}
	}

	private static Func<Matrix4x4, float, float?>? parkFarPlane;

	// the far plane the previous frame's span selection set (the park projection of a frame uses it, see FarPlaneOf)
	private static float? parkFarLag;

	/// <summary>
	/// The far argument of the first-person views in original units for the camera's view matrix, the display's height over its width and the sine and cosine of half the field of view, or null: the greatest clip depth of the cell boxes
	/// the view reaches (<c>0x56EA90</c> hands it to <c>0x578CA0</c>, which also sets the weather's depth split); set by the level's terrain.
	/// </summary>
	public static Func<Matrix4x4, float, float, float, float?>? EyeViewFarArgument { get; set; }

	/// <summary>
	/// The lobby view's far plane in engine units (the lobby's units are the original's) for the camera's view matrix and the display's height over its width, or null: the farthest corner distance of the span selection over the lobby terrain
	/// (<c>0x56BB00</c> hands it to <c>0x578CA0</c> before the weather, as in the park); set by the game flow while the front end shows its lobby.
	/// </summary>
	public static Func<Matrix4x4, float, float?>? LobbyFarPlane { get; set; }

	/// <summary>The far plane of the online globe's view: the camera method's 1000.</summary>
	public const float FrontEndFarPlane = 1000f;

	/// <summary>The globe's far plane: the same builder argument.</summary>
	public const float GlobeFarPlane = FrontEndFarPlane;

	private static float FarPlaneOf( CameraMode mode, Matrix4x4 view )
	{
		// the ride and walk views' far argument is the greatest depth of the cell boxes 0x56EA90 selects through 0x56E7E0 and 0x561BC0 (at least 100 original units, [this+0x1C], handed to 0x578CA0 at 0x56ED79); it only sets the weather's depth split (EyeViewFarArgument), the engine's projection of those views keeps the default
		// the heightfield's span selection works on the display's height over its width, the aspect word 0x8BCBCC
		// [BIN:TPW-EXE:0x5d8cc4 globe far plane] the camera method builds every projection with far 1000 (0x447a0000) and only a terrain's span selection (0x56bb00 -> 0x578ca0) changes it: the globe has none
		if ( mode is GlobeCameraMode )
			return GlobeFarPlane;
		if ( mode is LobbyCameraMode && LobbyFarPlane?.Invoke( view, 1f / Screen.Aspect ) is { } lobbyFar && float.IsFinite( lobbyFar ) && lobbyFar > 0.1f )
			return lobbyFar;
		if ( mode is LobbyCameraMode )
			return FrontEndFarPlane;
		return mode is ParkCameraMode ? ParkFarFor( view, 1f / Screen.Aspect ) : OriginalProjection.DefaultFarPlane;
	}

	/// <summary>
	/// The far plane the park projection of this frame uses, and the span selection of this frame, which sets the next frame's.
	/// </summary>
	// [BIN:TPW-EXE:0x576A2C far plane lag] the frame function composes the view-projection matrix first (0x576A2C) and only afterwards runs the span selection (0x576ADD -> 0x56BB00 -> 0x56BD3E -> 0x578CA0), which sets the new far plane for the next composition: a frame is drawn with the far plane of the span selection of the frame before it, the default one before any
	// [APPROX:UI-107] not ported: the first park frame after a ride or walk view uses the far plane 0x56ED79 last set through 0x578CA0 (that view's box depth); here the lag continues from the last park frame's far
	internal static float ParkFarFor( Matrix4x4 view, float aspectWord )
	{
		var used = parkFarLag ?? OriginalProjection.DefaultFarPlane;
		if ( parkFarPlane?.Invoke( view, aspectWord ) is { } far )
			parkFarLag = far;
		return used;
	}

	private static void CalcViewProjMatrix()
	{
		if ( CameraMode == null )
			return;

		var up = CameraMode.Up;
		var direction = CameraMode.Rotation.Forward;
		var position = CameraMode.Position;

		ViewMatrix = Matrix4x4.CreateLookTo( 
			position.GetSystemVector3(), 
			direction.GetSystemVector3(), 
			up.GetSystemVector3()
		);

		var fieldOfView = CameraMode.FieldOfView;

		ProjMatrix = CameraMode.UsesOriginalProjection
			? OriginalProjection.Create( Screen.Aspect, fieldOfView, FarPlaneOf( CameraMode, ViewMatrix ) )
			: Matrix4x4.CreatePerspectiveFieldOfView(
				fieldOfView.DegreesToRadians(),
				Screen.Aspect,
				0.1f,
				10000.0f
			);
	}

	public static void SetCameraMode<T>() where T : CameraMode
	{
		CameraMode = Activator.CreateInstance<T>();
	}

	public static void Update()
	{
		CameraMode.Update();

		// Run view/proj matrix calculations
		CalcViewProjMatrix();
	}
}
