using System.Numerics;

namespace OpenTPW;

/// <summary>The projection of the original's park, ride and walk views (the projection builder <c>0x578BE0</c>), also the lobby's and the online globe's (the front-end camera method <c>0x5d8cb0</c>, called with ISLANDFOV and 60).</summary>
public static class OriginalProjection
{
	/// <summary>The field of view in degrees the projection builder is given.</summary>
	// [BIN:TPW-EXE:0x42a990 field of view] the camera init multiplies pi/2 (0x74c9b8) by 180/pi (0x6fdd80) and hands 90.0 to 0x578BE0 (near 0.1, far 1000) and to the pick frustum 0x56B790; the first-frame setup 0x54E86F hands it 90.0 again; in the park, ride and walk views nothing else calls the builder with another angle (the front-end camera method 0x5d8cb0 calls it at 0x5d8cce with ISLANDFOV or 60, and 0x44e277 with 90)
	public const float FieldOfView = 90f;

	/// <summary>The half height of the visible frustum at unit depth for the given angle: the pick frustum's 0.5 times the tangent of half the angle (0.5 at 90).</summary>
	// [BIN:TPW-EXE:0x56b790 pick frustum] the four corner rays are (+-x, +-y, 1) with y = (0 - -1) x tan(angle x pi / 360) x 0.5 and x = y / the aspect word 0x8bcbcc: half height 0.5 at 90 degrees, half width 0.5 / aspect (emulated: 0.5 and 0.667 at aspect 0.75)
	// [BIN:TPW-EXE:0x5781d0 screen matrix] the matrix 0x578be0 builds has tangent 1 (cot of half the angle, 1 at 90 degrees: m00 = m11 = 0.7071 with w = 0.7071 and depth Q x 0.7071), but the view-projection is then multiplied (0x578501, 0x5786b1, through the matrix product at vtable +0x18) with the matrix 0x87b0d0 that 0x5781d0 fills once: m00 = 2 x the aspect word, m11 = -2.0. With the screen range +-1 (the final mapping to pixels was not read, the pick frustum and the video agree with it) the visible half height at unit depth is 1/2 = 0.5 and the half width 1 / (2 x aspect), the pick frustum's values: the two agree and the video match is explained, not a free choice. The scale by 2 is the matrix 0x87b0d0 (the clip-to-screen scale), not the projection builder
	public static float HalfHeight( float fieldOfViewDegrees ) => 0.5f * MathF.Tan( fieldOfViewDegrees * MathF.PI / 360f );

	/// <summary>The projection for a window aspect (width over height): the original's vertical extent, the width following the aspect (0.75 as the aspect word is 4:3).</summary>
	/// <summary>The default far plane when nothing else sets one (engine units); the park view takes the span selection's farthest corner distance and the ride views 1000 original units (<see cref="Camera"/>).</summary>
	public const float DefaultFarPlane = 10000f;

	public static Matrix4x4 Create( float aspect, float fieldOfViewDegrees = FieldOfView, float farPlane = DefaultFarPlane ) =>
		Matrix4x4.CreatePerspectiveFieldOfView( 2 * MathF.Atan( HalfHeight( fieldOfViewDegrees ) ), aspect, 0.1f, farPlane );
}
