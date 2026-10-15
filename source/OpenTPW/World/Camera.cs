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

		ProjMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
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
