namespace OpenTPW;

/// <summary>A native-free frame snapshot. Mouse position/delta and window size are SDL logical units.</summary>
public readonly record struct CameraControlInput
{
	public bool HasFocus { get; init; }
	public bool CaptureMouse { get; init; }
	public bool CaptureKeyboard { get; init; }
	public bool TextEntry { get; init; }
	/// <summary>The UI that captures the pointer leaves the screen's edge band to the camera: pushing the pointer there still scrolls (push-scroll mode).</summary>
	public bool EdgeScrollOverUi { get; init; }
	public bool Modal { get; init; }
	public Vector2 MousePosition { get; init; }
	/// <summary>Previous minus current position, matching Input.Mouse.Delta.</summary>
	public Vector2 MouseDelta { get; init; }
	public Point2 LogicalSize { get; init; }
	public bool RightButton { get; init; }
	public bool MiddleButton { get; init; }
	public float Wheel { get; init; }
	public float Forward { get; init; }
	public float Right { get; init; }
	public bool RotateLeft { get; init; }
	public bool RotateRight { get; init; }
	public bool RotateLeftPressed { get; init; }
	public bool RotateRightPressed { get; init; }
	public float Zoom { get; init; }
	public bool Home { get; init; }
	public float DeltaSeconds { get; init; }
}
