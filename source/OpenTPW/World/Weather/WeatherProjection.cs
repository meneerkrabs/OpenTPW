using System.Numerics;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace OpenTPW;

/// <summary>Which near plane the weather is clipped by (see <see cref="WeatherProjection"/>).</summary>
public enum WeatherNearPlane
{
	/// <summary>The scene camera's near plane (0.1 engine units, 0.5 original units in the park): a drop or ribbon nearer than it is cut away. This is <c>[EXT:weather-near-plane]</c>.</summary>
	Scene,
	/// <summary>The original's near plane (0.1 original units, <c>0x578BE0</c>): nothing of the weather is cut before the eye, the depth value is held at the front of the buffer.</summary>
	Original
}

/// <summary>
/// How the original's weather vertices reach OpenTPW's screen. tp.exe submits the rain and snow vertices straight to its polygon path in homogeneous clip coordinates (no projection matrix, no screen matrix), so a vertex lands at
/// x / w and y / w of the half screen (<c>0x5796A0</c>: x / w x half width + centre, y / w x half height + centre, y running down) with the depth-buffer value z / w; the bolts go through the scene's view-projection-screen matrix like the world.
/// OpenTPW draws all of them with an identity matrix from the screen position and a depth value written in its own scene depth space.
/// </summary>
// [BIN:TPW-EXE:0x5796A0 viewport] x_screen = x / w x half width + centre x, y_screen = y / w x half height + centre y (the display object [0x8BCBC8]: half width and height at +0x38 and +0x3C, centre at +0x28 and +0x2C), z_buffer = z / w x scale + offset
// [BIN:TPW-EXE:0x578BE0 projection depth] the scene's depth-buffer value at view depth v (original units) is (v - n) / (v (1 - n / f)) with n = 0.1 and f = 1000 (m10 = sin(fov / 2) / (1 - n / f), m14 = -m10 n, w = sin(fov / 2) v), so a weather vertex with clip z / w = D sits in the depth buffer where a surface at the distance n / (1 - D (1 - n / f)) would
public static class WeatherProjection
{
	/// <summary>The original's near plane in original units (<c>0x578BE0</c> argument 0x3DCCCCCD).</summary>
	public const float OriginalNear = 0.1f;
	/// <summary>The original's far plane in original units (<c>0x578BE0</c> argument 0x447A0000).</summary>
	public const float OriginalFar = 1000f;

	/// <summary>The view distance (original units) at which a scene surface has the depth-buffer value <paramref name="depth"/> in the original's projection.</summary>
	public static float EquivalentDistance( float depth ) => OriginalNear / (1f - depth * (1f - OriginalNear / OriginalFar));

	/// <summary>The depth-buffer value (0 at the near plane, 1 at the far plane) a surface at <paramref name="viewDistance"/> in front of the eye has in the projection <paramref name="projection"/> (a right-handed Veldrid projection).</summary>
	public static float SceneDepth( Matrix4x4 projection, float viewDistance )
	{
		var z = -viewDistance * projection.M33 + projection.M43;
		var w = -viewDistance * projection.M34 + projection.M44;
		return z / w;
	}

	/// <summary>
	/// The depth value OpenTPW writes for a weather vertex whose original depth-buffer value is <paramref name="originalDepth"/> (clip z over w): the scene's own depth for the same ordering against the world
	/// (<see cref="EquivalentDistance"/> at <paramref name="engineUnitsPerOriginal"/>), held at 0 or above for <see cref="WeatherNearPlane.Original"/>, left negative (so the device cuts it) for <see cref="WeatherNearPlane.Scene"/>.
	/// </summary>
	public static float WriteDepth( float originalDepth, Matrix4x4 sceneProjection, float engineUnitsPerOriginal, WeatherNearPlane near )
	{
		var depth = SceneDepth( sceneProjection, EquivalentDistance( originalDepth ) * engineUnitsPerOriginal );
		if ( near == WeatherNearPlane.Original )
			depth = MathF.Max( depth, 0f );
		return MathF.Min( depth, 1f );
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
				output[count++] = new WeatherRibbonVertex( NVector4.Lerp( previous.Clip, current.Clip, t ), NVector2.Lerp( previous.TexCoords, current.TexCoords, t ) );
			}
			if ( currentDistance >= 0 )
				output[count++] = current;
		}
		return count;
	}
}
