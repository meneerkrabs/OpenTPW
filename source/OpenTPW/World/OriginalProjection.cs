using System.Numerics;

namespace OpenTPW;

/// <summary>The projection of the original's park, ride and walk views (the one projection tp.exe builds once, <c>0x578BE0</c>).</summary>
public static class OriginalProjection
{
	/// <summary>The field of view in degrees the projection builder is given.</summary>
	// [BIN:TPW-EXE:0x42a990 field of view] the camera init multiplies pi/2 (0x74c9b8) by 180/pi (0x6fdd80) and hands 90.0 to 0x578BE0 (near 0.1, far 1000) and to the pick frustum 0x56B790; the first-frame setup 0x54E86F hands it 90.0 again; nothing else calls the builder with another angle
	public const float FieldOfView = 90f;

	/// <summary>The half height of the visible frustum at unit depth for the given angle: the pick frustum's 0.5 times the tangent of half the angle (0.5 at 90).</summary>
	// [BIN:TPW-EXE:0x56b790 pick frustum] the four corner rays are (+-x, +-y, 1) with y = (0 - -1) x tan(angle x pi / 360) x 0.5 and x = y / the aspect word 0x8bcbcc: half height 0.5 at 90 degrees, half width 0.5 / aspect. The matrix 0x578be0 itself has tangent 1; a native capture of Lost Kingdom at the Home pose against the PC video's first park frame (gate, booths and path edges within a few pixels at 1024 x 768) agrees with the frustum's 0.5 and 0.5 / 0.75, not with the matrix's 1
	public static float HalfHeight( float fieldOfViewDegrees ) => 0.5f * MathF.Tan( fieldOfViewDegrees * MathF.PI / 360f );

	/// <summary>The projection for a window aspect (width over height): the original's vertical extent, the width following the aspect (0.75 as the aspect word is 4:3).</summary>
	public static Matrix4x4 Create( float aspect, float fieldOfViewDegrees = FieldOfView ) =>
		Matrix4x4.CreatePerspectiveFieldOfView( 2 * MathF.Atan( HalfHeight( fieldOfViewDegrees ) ), aspect, 0.1f, 10000f );
}
