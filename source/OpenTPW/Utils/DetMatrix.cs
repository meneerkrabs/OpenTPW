using System.Numerics;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Matrix and vector arithmetic for simulation code with plain single-precision multiplies and adds in a fixed order (docs/DETERMINISM.md,
/// "Browser and native"). System.Numerics evaluates the <see cref="Matrix4x4"/> product, <see cref="Vector3.Transform(Vector3, Matrix4x4)"/> and
/// <see cref="Vector3.Lerp(Vector3, Vector3, float)"/> with <c>MultiplyAddEstimate</c>, which fuses the multiply and the add where the processor has
/// fused multiply-add and rounds twice where it has not, so their last bit depends on the runtime. The JIT does not contract the scalar
/// expressions here, so every runtime rounds every product and every sum, left to right.
/// </summary>
// [EXT:deterministic-math] the composition follows the row-vector convention of System.Numerics with each product and each sum rounded to single precision, left to right; tp.exe composes its node matrices on the x87 (0x46f650 and its relatives)
public static class DetMatrix
{
	/// <summary>The row-major product <paramref name="a"/> × <paramref name="b"/>, each element <c>a1·b1 + a2·b2 + a3·b3 + a4·b4</c> summed left to right.</summary>
	public static Matrix4x4 Multiply( in Matrix4x4 a, in Matrix4x4 b ) => new(
		a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31 + a.M14 * b.M41,
		a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32 + a.M14 * b.M42,
		a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33 + a.M14 * b.M43,
		a.M11 * b.M14 + a.M12 * b.M24 + a.M13 * b.M34 + a.M14 * b.M44,
		a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31 + a.M24 * b.M41,
		a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32 + a.M24 * b.M42,
		a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33 + a.M24 * b.M43,
		a.M21 * b.M14 + a.M22 * b.M24 + a.M23 * b.M34 + a.M24 * b.M44,
		a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31 + a.M34 * b.M41,
		a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32 + a.M34 * b.M42,
		a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33 + a.M34 * b.M43,
		a.M31 * b.M14 + a.M32 * b.M24 + a.M33 * b.M34 + a.M34 * b.M44,
		a.M41 * b.M11 + a.M42 * b.M21 + a.M43 * b.M31 + a.M44 * b.M41,
		a.M41 * b.M12 + a.M42 * b.M22 + a.M43 * b.M32 + a.M44 * b.M42,
		a.M41 * b.M13 + a.M42 * b.M23 + a.M43 * b.M33 + a.M44 * b.M43,
		a.M41 * b.M14 + a.M42 * b.M24 + a.M43 * b.M34 + a.M44 * b.M44 );

	/// <summary>The point <paramref name="v"/> (w = 1) times <paramref name="m"/>: <c>x·m11 + y·m21 + z·m31 + m41</c> and so on, summed left to right.</summary>
	public static NVector3 TransformPosition( NVector3 v, in Matrix4x4 m ) => new(
		v.X * m.M11 + v.Y * m.M21 + v.Z * m.M31 + m.M41,
		v.X * m.M12 + v.Y * m.M22 + v.Z * m.M32 + m.M42,
		v.X * m.M13 + v.Y * m.M23 + v.Z * m.M33 + m.M43 );

	/// <summary>The linear blend <c>a + (b − a)·t</c> per component.</summary>
	public static NVector3 Lerp( NVector3 a, NVector3 b, float t ) => new( a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t );

	/// <summary>
	/// The sign-preserving slerp of <c>ModelAnimationTrack.Slerp</c>, the original's table-sine blend (<see cref="ModelRotationBlend"/>) with the deterministic table (<see cref="OriginalSineTable"/>) and DetMath's arc cosine.
	/// </summary>
	public static Quaternion Slerp( Quaternion from, Quaternion to, float fraction ) => ModelRotationBlend.Blend( from, to, fraction, OriginalSineTable.Entries, DeterministicAcos );

	private static readonly Func<float, float> DeterministicAcos = value => DetMathF.Acos( value );
}
