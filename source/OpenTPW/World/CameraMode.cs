namespace OpenTPW;

public class CameraMode
{
	public Vector3 Position { get; set; } = new();
	public Rotation Rotation { get; set; } = Rotation.Identity;

	public float FieldOfView { get; set; } = 90f;

	/// <summary>Whether the view uses the original's projection (<see cref="OriginalProjection"/>) instead of a plain vertical field of view.</summary>
	public bool UsesOriginalProjection { get; set; }

	/// <summary>The view's up vector; a mode that rolls (the ride view, with the car) overrides it. The default is the world's up.</summary>
	public virtual Vector3 Up => Vector3.Up;

	public virtual void Update()
	{

	}
}
