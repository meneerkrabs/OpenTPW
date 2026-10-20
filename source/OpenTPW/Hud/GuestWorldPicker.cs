using Matrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace OpenTPW.Hud;

/// <summary>CPU hit test against the current renderer's camera-facing sprite rectangle.</summary>
// [APPROX:UI-930] VisitorCirclePicker corrects geometry only for proven ordinary sources/canonical views. Other callers retain this rectangle. Current frame selection, synchronous timing, ID ties, cross-kind competition, clipping and rendered-view validation remain qualified.
internal static class GuestWorldPicker
{
	internal static bool TryHit(NVector3 feet, GuestAtlasFrame frame, bool mirror, Matrix4x4 view,
		Matrix4x4 projection, NVector2 viewport, NVector2 mouse, out float depth, float rotation = 0f)
	{
		if (!TryBounds(feet, frame, mirror, view, projection, viewport, mouse, out depth, out var min, out var max, rotation))
			return false;
		return mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;
	}

	/// <summary>The legacy rectangle projection/clipping, shared without changing its depth or corner arithmetic.</summary>
	internal static bool TryBounds(NVector3 feet, GuestAtlasFrame frame, bool mirror, Matrix4x4 view,
		Matrix4x4 projection, NVector2 viewport, NVector2 mouse, out float depth, out NVector2 min, out NVector2 max, float rotation = 0f)
	{
		depth = 0;
		min = new NVector2(float.PositiveInfinity);
		max = new NVector2(float.NegativeInfinity);
		if (!float.IsFinite(viewport.X) || !float.IsFinite(viewport.Y) || viewport.X <= 0 || viewport.Y <= 0 ||
			!float.IsFinite(mouse.X) || !float.IsFinite(mouse.Y) || mouse.X < 0 || mouse.Y < 0 ||
			mouse.X >= viewport.X || mouse.Y >= viewport.Y || frame.Width <= 0 || frame.Height <= 0)
			return false;
		var right = new NVector3(view.M11, view.M21, view.M31);
		var up = new NVector3(view.M12, view.M22, view.M32);
		// Same hotspot, mirroring, scale and camera basis used by GuestRenderer.OnRender.
		var (x0, x1, top, bottom) = frame.Extents(mirror, GuestRenderer.UnitsPerPixel);
		var transform = view * projection;
		for (var corner = 0; corner < 4; corner++)
		{
			var (cx, cy) = ((corner & 1) == 0 ? x0 : x1, (corner & 2) == 0 ? top : bottom);
			var (sin, cos) = MathF.SinCos( rotation );
			var point = feet + right * (cx * cos + cy * sin) + up * (-cx * sin + cy * cos);
			var clip = NVector4.Transform(new NVector4(point, 1), transform);
			if (!float.IsFinite(clip.W) || clip.W <= 0)
				return false;
			var z = clip.Z / clip.W;
			var screen = new NVector2((clip.X / clip.W + 1) * viewport.X / 2, (1 - clip.Y / clip.W) * viewport.Y / 2);
			if (!float.IsFinite(z) || z < 0 || z > 1 || !float.IsFinite(screen.X) || !float.IsFinite(screen.Y))
				return false;
			depth = z;
			min = NVector2.Min(min, screen);
			max = NVector2.Max(max, screen);
		}
		return max.X > min.X && max.Y > min.Y;
	}
}
