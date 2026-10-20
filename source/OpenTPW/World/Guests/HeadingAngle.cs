namespace OpenTPW;

/// <summary>
/// The 11-bit angle routine tp.exe uses for a guest's heading (<c>0x006E7074</c>, the segment <c>cseg</c> after the code): a table-driven arctangent over the
/// offset, 2048 units to the turn, 0 pointing along −Y and growing through −X (0x200) and +Y (0x400) to +X (0x600).
/// </summary>
public static class HeadingAngle
{
	/// <summary>Units in a turn: the routine answers modulo 0x800.</summary>
	public const int Turn = 0x800;

	// [BIN:TPW-EXE:0x006E71B4 arctangent table] 257 words, entry i = floor(atan(i / 256) × 1024 / π) (checked against all 257 words of the executable): 0 to 256, the angle of a ratio i / 256 in the same units
	private static readonly ushort[] Table = Enumerable.Range( 0, 257 ).Select( index => (ushort)Math.Floor( DetMath.Atan( index / 256d ) * 1024d / Math.PI ) ).ToArray();

	/// <summary>The table entry for the ratio <paramref name="numerator"/> / <paramref name="denominator"/> (the routine's <c>(n &lt;&lt; 8) / d</c>, an unsigned divide of the larger into the smaller).</summary>
	private static int Entry( int numerator, int denominator ) => Table[(int)(((uint)numerator << 8) / (uint)denominator)];

	/// <summary>
	/// The routine's answer for the two signed words it is given (<paramref name="first"/> then <paramref name="second"/>, the guest's offset to the target as the
	/// callers push it: the negated X offset and the Y offset), 0 for both 0.
	/// </summary>
	// [BIN:TPW-EXE:0x006E7074 atan2] by quadrant of (first, second) the smaller magnitude is divided into the larger, (min << 8) / max indexes the table, and the entry is added to or subtracted from the quadrant's base 0x000, 0x200, 0x400, 0x600 or 0x800 of the octant, masked with 0x7FF; both 0 answers 0
	public static int Of( int first, int second )
	{
		if ( first == 0 && second == 0 )
			return 0;
		int angle;
		if ( first >= 0 )
		{
			if ( second >= 0 )
				angle = first < second ? 0x400 - Entry( first, second ) : 0x200 + Entry( second, first );
			else
			{
				var depth = -second;
				angle = first < depth ? Entry( first, depth ) : 0x200 - Entry( depth, first );
			}
		}
		else
		{
			var across = -first;
			if ( second >= 0 )
				angle = across < second ? 0x400 + Entry( across, second ) : 0x600 - Entry( second, across );
			else
			{
				var depth = -second;
				angle = across < depth ? 0x800 - Entry( across, depth ) : 0x600 + Entry( depth, across );
			}
		}
		return angle & 0x7FF;
	}

	/// <summary>The heading vector (cell units) of a guest facing the offset (<paramref name="dx"/>, <paramref name="dy"/>) as 0x004FEF25's turn writes it: the quantised angle of (−dx, dy), back as (−sin, −cos).</summary>
	public static (float X, float Y) Facing( int dx, int dy )
	{
		var radians = Of( -dx, dy ) * (2 * Math.PI / Turn);
		// [EXT:deterministic-math] the sine and cosine of the quantised angle in IEEE double by DetMath, the same bits on every runtime, not in x87 extended precision (docs/DETERMINISM.md, "Browser and native")
		return ((float)-DetMath.Sin( radians ), (float)-DetMath.Cos( radians ));
	}
}
