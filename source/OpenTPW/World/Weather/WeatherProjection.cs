using System.Numerics;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace OpenTPW;

/// <summary>
/// How the original's weather vertices reach OpenTPW's screen. tp.exe submits the rain and snow vertices straight to its polygon path in homogeneous clip coordinates (no projection matrix, no screen matrix), so a vertex lands at
/// x / w and y / w of the half screen (<c>0x5796A0</c>, y running down) with a depth-buffer value in two ranges (below the clip z threshold of the depth split, linear in z; above it, z / w); the bolts go through the scene's
/// view-projection-screen matrix like the world. OpenTPW sends every weather vertex as a clip-space position (x, -y, depth x w, w) to the GPU, so the device interpolates texture and colour perspective-correctly as the original's rhw does, with identity
/// matrices, and the depth value is written in its own scene depth space (<see cref="WriteDepth"/>).
/// </summary>
// [BIN:TPW-EXE:0x5796A0 viewport] x_screen = x / w x half width + centre x, y_screen = y / w x half height + centre y (the display object [0x8BCBC8]: half width and height at +0x38 and +0x3C, centre at +0x28 and +0x2C); the depth value is split at a clip z threshold [0x8BCC28] (0x5796FF; the 3DNow and SSE twins 0x5F4B5A, 0x5F53A0 do the same): below it [0x8BCC34] x z + [0x8BCC38], linear in z with no divide by w, above it [0x8BCC2C] x z / w + [0x8BCC30]
// [BIN:TPW-EXE:0x583220 depth split] set every frame by the span selection (0x578CA0 -> 0x578D07 with D, the far argument: the park overview's span selection hands it its farthest corner distance (0x56BB00, at least 10), the eye views' 0x56EA90 the greatest clip z of the visible cell boxes, at least 100 (0x56ED79; its first call with 1000, 0x56EAC0, only builds the matrix the box test uses), the lobby the same farthest corner of its own camera): t = max(0.1, [+0x44] x (D - m14) / m10) with [+0x44] = 0.003 (0x582DC8), threshold zt = m10 t + m14, slope 0.1 / zt, offset 0.1, and K = (1 - 0.2) / (1 - zt / (sin(fov / 2) t)), c = 1 - K, so the linear range ends at the depth value 0.2 and the rest runs to 1 (checked by running the original's code offline: D = 1000 gives zt 2.929, K 34.08); every view that draws weather calls 0x578CA0 first (the rain's only caller 0x576DAB is in 0x576A00, which runs 0x56BB00 or 0x56EA90 before it), the lobby included
// [BIN:TPW-EXE:0x578BE0 projection depth] the scene's clip z at view depth v (original units) is m10 (v - n) with m10 = sin(fov / 2) / (1 - n / D), n = 0.1, and w = sin(fov / 2) v: a weather vertex with clip z below zt equals a scene surface at n + z / m10, and above it a surface at n / (1 - (z / w)(1 - n / D))
public static class WeatherProjection
{
	/// <summary>The original's near plane in original units (<c>0x578BE0</c> argument 0x3DCCCCCD).</summary>
	public const float OriginalNear = 0.1f;
	/// <summary>The original's far plane in original units (<c>0x578BE0</c> argument 0x447A0000).</summary>
	public const float OriginalFar = 1000f;

	/// <summary>The sine of half the field of view the original's park projection is built with (90 degrees).</summary>
	public const float ParkSine = 0.70710678f;
	/// <summary>The span selection's slope factor for the depth split's threshold ([+0x44] of the display object, 0x582DC8).</summary>
	public const float SplitFactor = 0.003f;

	/// <summary>
	/// The clip z below which the original's depth value is linear in z (zt of <c>0x583220</c>) for the far argument <paramref name="far"/> of a projection whose half field of view has the sine <paramref name="sine"/>.
	/// </summary>
	public static float SplitThreshold( float far, float sine = ParkSine )
	{
		var m10 = sine / (1f - OriginalNear / far);
		var t = MathF.Max( OriginalNear, SplitFactor * (far + m10 * OriginalNear) / m10 );
		return m10 * (t - OriginalNear);
	}

	/// <summary>
	/// The view distance (original units) of the scene surface that has the same depth-buffer value as a weather vertex with clip coordinates <paramref name="z"/> and <paramref name="w"/> in the original's depth mapping
	/// (<paramref name="far"/> its far argument, <paramref name="sine"/> the sine of its half field of view). Infinity beyond the far plane.
	/// </summary>
	public static float EquivalentDistance( float z, float w, float far = OriginalFar, float sine = ParkSine )
	{
		// [BIN:TPW-EXE:0x5796FF viewport] the compare is unsigned: a negative z takes the divided branch
		if ( z >= 0f && z < SplitThreshold( far, sine ) )
			return OriginalNear + z / (sine / (1f - OriginalNear / far));
		return DividedDistance( z, w, far );
	}

	/// <summary>The view distance of the scene surface that has the depth-buffer value <c>z / w</c> in the original's divided mapping (the range above the split). Infinity beyond the far plane.</summary>
	public static float DividedDistance( float z, float w, float far = OriginalFar )
	{
		var denominator = 1f - z / w * (1f - OriginalNear / far);
		return denominator > 0 ? OriginalNear / denominator : float.PositiveInfinity;
	}

	/// <summary>The depth-buffer value (0 at the near plane, 1 at the far plane) a surface at <paramref name="viewDistance"/> in front of the eye has in the projection <paramref name="projection"/> (a right-handed Veldrid projection).</summary>
	public static float SceneDepth( Matrix4x4 projection, float viewDistance )
	{
		var z = -viewDistance * projection.M33 + projection.M43;
		var w = -viewDistance * projection.M34 + projection.M44;
		return z / w;
	}

	/// <summary>
	/// The depth value OpenTPW writes for a weather vertex with the original's clip z and w: the scene's own depth for the same ordering against the world (<see cref="EquivalentDistance(float, float, float, float)"/> at
	/// <paramref name="engineUnitsPerOriginal"/>), held at 0 or above because the original cuts nothing of the rain and snow before the eye (no near plane of the weather's own, no change of the scene's), and 1 beyond the far plane.
	/// </summary>
	public static float WriteDepth( float z, float w, Matrix4x4 sceneProjection, float engineUnitsPerOriginal, float far = OriginalFar, float sine = ParkSine )
	{
		var distance = EquivalentDistance( z, w, far, sine );
		if ( !float.IsFinite( distance ) )
			return 1f;
		var depth = SceneDepth( sceneProjection, distance * engineUnitsPerOriginal );
		return float.IsNaN( depth ) ? 1f : MathF.Min( MathF.Max( depth, 0f ), 1f );
	}

	/// <summary>The screen position a clip vertex has, x right and y up in -1..1 (the original's y runs down).</summary>
	public static NVector2 ToScreen( NVector3 clipXyz, float w ) => new( clipXyz.X / w, -clipXyz.Y / w );

	/// <summary>
	/// Cuts a convex polygon of clip vertices to the half space <c>zWeight x z + wWeight x w + offset &gt;= 0</c> (Sutherland-Hodgman, attributes interpolated linearly in clip space) and returns the vertex count of the result in
	/// <paramref name="output"/> (at most one more than the input).
	/// </summary>
	public static int ClipNear( ReadOnlySpan<WeatherRibbonVertex> input, Span<WeatherRibbonVertex> output, float zWeight, float wWeight, float offset )
	{
		float Distance( NVector4 clip ) => zWeight * clip.Z + wWeight * clip.W + offset;
		var count = 0;
		for ( var index = 0; index < input.Length; index++ )
		{
			var current = input[index];
			var previous = input[(index + input.Length - 1) % input.Length];
			var currentDistance = Distance( current.Clip );
			var previousDistance = Distance( previous.Clip );
			if ( (currentDistance >= 0) != (previousDistance >= 0) )
			{
				var t = previousDistance / (previousDistance - currentDistance);
				output[count++] = new WeatherRibbonVertex( NVector4.Lerp( previous.Clip, current.Clip, t ), NVector2.Lerp( previous.TexCoords, current.TexCoords, t ), NVector2.Lerp( previous.Tint, current.Tint, t ) );
			}
			if ( currentDistance >= 0 )
				output[count++] = current;
		}
		return count;
	}
}
