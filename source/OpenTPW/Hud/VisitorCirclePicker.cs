using Matrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace OpenTPW.Hud;

/// <summary>A strict circle in the original's normalized pointer space, where Y points down.</summary>
internal readonly record struct VisitorPickCircle( double X, double Y, double RadiusSquared )
{
	internal bool Contains( double x, double y ) => (x - X) * (x - X) + (y - Y) * (y - Y) < RadiusSquared;
}

/// <summary>Conditional ordinary-visitor geometry; it neither selects a frame nor advances/retains a pick epoch.</summary>
internal static class VisitorCirclePicker
{
	// Widen this exact float for double dot products/determinants. This allows only float camera round-off;
	// it is not a projection/aspect tolerance. All supplied/current/expected matrix fields compare exactly.
	internal const float BasisTolerance = 1e-5f;

	internal static bool IsCanonicalView( Matrix4x4 view, Matrix4x4 projection, NVector2 viewport )
	{
		if (Camera.Mode is not ParkCameraMode || !Camera.Mode.UsesOriginalProjection || Camera.Mode.FieldOfView != 90f
			|| !ValidViewport(viewport) || !Finite(view) || !Finite(projection)
			|| view != Camera.ViewMatrix || projection != Camera.ProjMatrix
			|| view.M14 != 0 || view.M24 != 0 || view.M34 != 0 || view.M44 != 1)
			return false;
		var aspect = viewport.X / viewport.Y;
		if (!float.IsFinite(aspect) || aspect <= 0)
			return false;
		var expected = OriginalProjection.Create(aspect, OriginalProjection.FieldOfView);
		if (!Finite(expected) || projection != expected)
			return false;
		var x = new NVector3(view.M11, view.M21, view.M31);
		var y = new NVector3(view.M12, view.M22, view.M32);
		var z = new NVector3(view.M13, view.M23, view.M33);
		static double Dot(NVector3 a, NVector3 b) => (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z;
		var determinant = (double)view.M11 * ((double)view.M22 * view.M33 - (double)view.M23 * view.M32)
			- (double)view.M12 * ((double)view.M21 * view.M33 - (double)view.M23 * view.M31)
			+ (double)view.M13 * ((double)view.M21 * view.M32 - (double)view.M22 * view.M31);
		return Math.Abs(Dot(x, x) - 1) <= BasisTolerance && Math.Abs(Dot(y, y) - 1) <= BasisTolerance
			&& Math.Abs(Dot(z, z) - 1) <= BasisTolerance && Math.Abs(Dot(x, y)) <= BasisTolerance
			&& Math.Abs(Dot(x, z)) <= BasisTolerance && Math.Abs(Dot(y, z)) <= BasisTolerance
			&& double.IsFinite(determinant) && determinant > 0 && Math.Abs(determinant - 1) <= BasisTolerance;
	}

	/// <summary>Returns eligibility, not hit outcome. Invalid geometry retains the existing rectangle path.</summary>
	// [BIN:TPW-EXE:0x005890E0 circle placement] default sx=20*scale*height/width, sy=20*scale; ordinary programs 1..8 preserve primary scale 1 (0x475944, table 0x75A418).
	// [BIN:TPW-EXE:0x0057820E composed matrix] sprite hotspot already includes view*projection*screen; native W=sin(45)*host W/ModelScale at the canonical 90-degree projection.
	internal static bool TryCreate( NVector3 hotspot, GuestAtlasFrame frame, bool mirror, Matrix4x4 view,
		Matrix4x4 projection, NVector2 viewport, NVector2 mouse, out VisitorPickCircle circle, out NVector2 pointer, out float depth )
	{
		circle = default; depth = 0;
		if (!TryPointer(viewport, mouse, out pointer) || !Finite(view) || !Finite(projection)
			|| frame.Width <= 0 || frame.Height <= 0 || frame.PixelsPerUnitX == 0 || frame.PixelsPerUnitY == 0
			// The shared rectangle projection uses integer origin sums. Reject overflow before calling it.
			|| (long)frame.OriginX + frame.Width is < int.MinValue or > int.MaxValue
			|| (long)frame.OriginY + frame.Height is < int.MinValue or > int.MaxValue
			|| frame.OriginX == int.MinValue || frame.OriginY == int.MinValue)
			return false;
		var clip = NVector4.Transform(new NVector4(hotspot, 1), view * projection);
		if (!float.IsFinite(clip.X) || !float.IsFinite(clip.Y) || !float.IsFinite(clip.Z) || !float.IsFinite(clip.W) || clip.W <= 0
			|| clip.Z / clip.W < 0 || clip.Z / clip.W > 1)
			return false;
		// Keep the host's inclusive projected-Z near/far and all-four-corners clipping, not the native coarse cull.
		// Also keep its final-corner Z exactly for kid/costume/swimmer aggregation and existing precision ties.
		if (!GuestWorldPicker.TryBounds(hotspot, frame, mirror, view, projection, viewport, mouse, out depth, out _, out _))
			return false;
		var aspect = (double)viewport.Y / viewport.X;
		var nativeW = Math.Sin(Math.PI / 4) * clip.W / OriginalParkPlacement.ModelScale;
		var width = (double)frame.Width / frame.PixelsPerUnitX * 20 * aspect;
		var height = (double)frame.Height / frame.PixelsPerUnitY * 20;
		// Native mirroring negates originX; it does not use the mirrored rectangle's -(originX+width).
		var origin = (mirror ? -(double)frame.OriginX : frame.OriginX) / frame.PixelsPerUnitX * 20 * aspect;
		var x = (double)clip.X / clip.W + origin / nativeW;
		var y = -(double)clip.Y / clip.W;
		var radiusSquared = (double)0.0002f * (width * width + height * height);
		if (!double.IsFinite(nativeW) || nativeW <= 0 || !double.IsFinite(x) || !double.IsFinite(y)
			|| !double.IsFinite(radiusSquared) || radiusSquared <= 0)
			return false;
		circle = new VisitorPickCircle(x, y, radiusSquared);
		return true;
	}

	/// <summary>Drawable pixels and drawable extent only. Fractional host input has no exact native counterpart.</summary>
	// [BIN:TPW-EXE:0x0048A66B virtual mouse] integer virtual 2048x1536 coordinates; 0x532C26/0x532C45 normalize with float constants 1/1024 and 1/767.5.
	internal static bool TryPointer( NVector2 viewport, NVector2 mouse, out NVector2 pointer )
	{
		pointer = default;
		if (!ValidViewport(viewport) || !float.IsFinite(mouse.X) || !float.IsFinite(mouse.Y)
			|| mouse.X < 0 || mouse.Y < 0 || mouse.X >= viewport.X || mouse.Y >= viewport.Y)
			return false;
		// Validate before scaling; double multiplication cannot overflow for finite float input. Truncate
		// the complete quotient, not pixelX first. In-bounds values fit the native signed-16 virtual words.
		var x = Math.Truncate((double)mouse.X * 2048 / viewport.X);
		var y = Math.Truncate((double)mouse.Y * 1536 / viewport.Y);
		pointer = new NVector2((float)(x * (double)(1f / 1024f) - 1), (float)(y * (double)(1f / 767.5f) - 1));
		return true;
	}

	private static bool ValidViewport(NVector2 viewport) => float.IsFinite(viewport.X) && float.IsFinite(viewport.Y) && viewport.X > 0 && viewport.Y > 0;
	private static bool Finite(Matrix4x4 m) =>
		float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M14)
		&& float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23) && float.IsFinite(m.M24)
		&& float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M34)
		&& float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43) && float.IsFinite(m.M44);
}
