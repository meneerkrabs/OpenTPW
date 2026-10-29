using System.Numerics;

namespace OpenTPW;

/// <summary>
/// The rotation blend of the clip sampler (tp.exe <c>0x4742A0</c>, reached through <c>0x474490</c>): a spherical blend of two keys that takes its sines from the engine's 4096-entry table.
/// The caller supplies the table and the arc cosine, so the simulation can use its deterministic ones (docs/DETERMINISM.md) and the viewers the platform's.
/// </summary>
public static class ModelRotationBlend
{
	/// <summary>The table's entries (<c>0x7B0960</c> holds the mask <c>N − 1</c>).</summary>
	public const int TableSize = 4096;

	/// <summary><c>N × 0.15915494</c> as the single-precision product the init stores at <c>0x7B095C</c>: table steps per radian.</summary>
	// [BIN:TPW-EXE:0x4708D0 sine table] the init stores mask = N - 1 (0x7B0960) and scale = N x 0.15915494 (0x6FEB9C; 0x7B095C) and fills 0x7B0968 with float(sin(i / N x 6.2831855)); the sampler's blend reads it through ftol (fistp: the nearest index, ties to even)
	public static readonly float Scale = TableSize * 0.15915494f;

	/// <summary>The platform's table: <c>(float)sin(2 π i / N)</c> with the platform's sine.</summary>
	public static readonly float[] PlatformTable = BuildPlatformTable();

	private static float[] BuildPlatformTable()
	{
		var table = new float[TableSize];
		for ( var index = 0; index < TableSize; index++ )
			table[index] = (float)Math.Sin( index / (double)TableSize * 6.2831855f );
		return table;
	}

	/// <summary>
	/// The blend of <paramref name="from"/> and <paramref name="to"/> at <paramref name="t"/>, in the original's order and single precision, normalised at the end where the original's matrix builder (<c>0x474120</c>) divides by the squared length.
	/// </summary>
	// [BIN:TPW-EXE:0x4742A0 rotation blend] d = w·w + z·z + y·y + x·x summed in that order; d + 1 at most 0.001 (0x6FEC70) takes the opposite-keys branch, 1 − d at most 0.001 the nearly equal one (weights 1 − t and t); else θ = acos(d), sin θ = table[ftol(θ·scale) & mask] and the weights table[ftol((1 − t)·θ·scale) & mask] / sin θ and table[ftol(θ·t·scale) & mask] / sin θ, the keys scaled and added componentwise; no hemisphere flip
	// [BIN:TPW-EXE:0x4743E0 opposite keys] with d + 1 at most 0.001 the result starts as (−from.y, from.x, −from.w, from.z); x, y and z become that times table[ftol(t·π/2·scale)] plus from times table[ftol((1 − t)·π/2·scale)], and w stays from.z (0x4743E0..0x474487 never writes the fourth component again)
	public static Quaternion Blend( Quaternion from, Quaternion to, float t, ReadOnlySpan<float> table, Func<float, float> acos )
	{
		var d = to.W * from.W + to.Z * from.Z;
		d += to.Y * from.Y;
		d += to.X * from.X;
		float x, y, z, w;
		if ( !(d + 1f > 0.001f) )
		{
			const float HalfPi = 1.5707964f;
			var fromWeight = table[(int)MathF.Round( (1f - t) * HalfPi * Scale ) & (TableSize - 1)];
			var turnedWeight = table[(int)MathF.Round( t * HalfPi * Scale ) & (TableSize - 1)];
			x = -from.Y * turnedWeight + from.X * fromWeight;
			y = turnedWeight * from.X + fromWeight * from.Y;
			z = turnedWeight * -from.W + fromWeight * from.Z;
			w = from.Z;
		}
		else
		{
			float first, second;
			if ( !(1f - d > 0.001f) )
			{
				first = 1f - t;
				second = t;
			}
			else
			{
				var angle = acos( d );
				var sine = table[(int)MathF.Round( angle * Scale ) & (TableSize - 1)];
				first = table[(int)MathF.Round( (1f - t) * angle * Scale ) & (TableSize - 1)] / sine;
				second = table[(int)MathF.Round( angle * t * Scale ) & (TableSize - 1)] / sine;
			}
			x = from.X * first + to.X * second;
			y = first * from.Y + second * to.Y;
			z = second * to.Z + first * from.Z;
			w = first * from.W + to.W * second;
		}
		var length = MathF.Sqrt( x * x + y * y + z * z + w * w );
		return length > 0 ? new Quaternion( x / length, y / length, z / length, w / length ) : new Quaternion( x, y, z, w );
	}
}
