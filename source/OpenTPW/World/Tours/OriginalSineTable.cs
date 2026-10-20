namespace OpenTPW;

/// <summary>
/// The sine table the engine's rotation builder reads: 4096 entries <c>sin(2 pi i / 4096)</c> as floats, indexed by the rounded angle in table steps (a cosine is the sine of the angle plus a quarter turn).
/// </summary>
// [BIN:TPW-EXE:0x004708D0 sine table] the init (called with 0x1000 from 0x449EB0 and 0x44A1D0) stores mask = N - 1 (0x7B0960), scale = N x 0.15915494 (0x6FEB9C, 1 / 2 pi; 0x7B095C) and fills 0x7B0968 with float(fsin(i / N x 6.2831855)) (0x6FEBA0); the rotation builder 0x46F6F0 looks up sine = table[round(angle x scale) & mask] and cosine = table[round((angle + 1.5707964) x scale) & mask]
internal static class OriginalSineTable
{
	public const int Size = 4096;
	private const float Scale = Size * 0.15915494f;
	private static readonly float[] Table = Build();

	private static float[] Build()
	{
		var table = new float[Size];
		for ( var index = 0; index < Size; index++ )
			table[index] = (float)DetMath.Sin( index / (double)Size * 6.2831855f );
		return table;
	}

	private static int IndexOf( float angle ) => (int)MathF.Round( angle * Scale ) & (Size - 1);

	/// <summary>The table's sine of <paramref name="angle"/> (radians).</summary>
	public static float Sin( float angle ) => Table[IndexOf( angle )];

	/// <summary>The table's cosine of <paramref name="angle"/> (radians): the sine of the angle plus a quarter turn.</summary>
	public static float Cos( float angle ) => Table[IndexOf( angle - -1.5707964f )];
}
