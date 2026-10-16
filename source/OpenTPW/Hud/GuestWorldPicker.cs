using Matrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace OpenTPW.Hud;

/// <summary>CPU hit test against the current renderer's camera-facing sprite rectangle.</summary>
// [APPROX:UI-930] rectangle hits choose the nearest projected depth, then lowest guest id; the original's sprite pass 0x5890e0 tests a circle round the sprite's projected anchor (radius squared 0.0002 x (width squared + height squared) in clip units, no alpha test), nearest clip w first, the first drawn keeping a tie — evidence needed: the clip-space position, scale (+0x2c) and aspect (0x8bcbcc) the guest draw code gives a visitor's sprite, and the sprite draw order
internal static class GuestWorldPicker
{
	internal static bool TryHit(NVector3 feet, GuestAtlasFrame frame, bool mirror, Matrix4x4 view,
		Matrix4x4 projection, NVector2 viewport, NVector2 mouse, out float depth)
	{
		depth = 0;
		if (!float.IsFinite(viewport.X) || !float.IsFinite(viewport.Y) || viewport.X <= 0 || viewport.Y <= 0 ||
			!float.IsFinite(mouse.X) || !float.IsFinite(mouse.Y) || mouse.X < 0 || mouse.Y < 0 ||
			mouse.X >= viewport.X || mouse.Y >= viewport.Y || frame.Width <= 0 || frame.Height <= 0)
			return false;
		var right = new NVector3(view.M11, view.M21, view.M31);
		var up = new NVector3(view.M12, view.M22, view.M32);
		// Same hotspot, mirroring, scale and camera basis used by GuestRenderer.OnRender.
		var left = mirror ? -(frame.OriginX + frame.Width) : frame.OriginX;
		var x0 = left * GuestRenderer.UnitsPerPixel;
		var x1 = (left + frame.Width) * GuestRenderer.UnitsPerPixel;
		var top = -frame.OriginY * GuestRenderer.UnitsPerPixel;
		var bottom = -(frame.OriginY + frame.Height) * GuestRenderer.UnitsPerPixel;
		var transform = view * projection;
		var min = new NVector2(float.PositiveInfinity);
		var max = new NVector2(float.NegativeInfinity);
		for (var corner = 0; corner < 4; corner++)
		{
			var point = feet + right * ((corner & 1) == 0 ? x0 : x1) + up * ((corner & 2) == 0 ? top : bottom);
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
		return max.X > min.X && max.Y > min.Y && mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;
	}
}
