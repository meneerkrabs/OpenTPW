namespace OpenTPW;

public sealed partial class OriginalObjectRuntime
{
	private Func<Vector3?>? eventOrigin;

	/// <summary>Presentation-only sound-space origin sampled when a routed sound EVENT or repair-hide pair executes; headless runtimes have none.</summary>
	internal void SetEventOrigin(Func<Vector3?> origin) => eventOrigin = origin;

	// [APPROX:AUDIO-908] routed EVENT and repair-hide sounds use the rendered placement origin at instruction time; native emitter anchor/update timing is unqualified, not the scream box midpoint
	internal Vector3? ReadEventOrigin()
	{
		var origin = eventOrigin?.Invoke();
		return origin is { } point && float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z) ? point : null;
	}
}
