namespace OpenTPW.Hud;

/// <summary>
/// The anti-aliased line of the park map's track layer, 32 bit per pixel variant (procedure 0x5efb10, called by the map's track layer 0x5f2fd0; the 8, 16 and 24 bit variants are 0x5edad0, 0x5ee580 and
/// 0x5ef040). It is a Gupta-Sproull line: a Bresenham walk along the major axis whose every step writes three pixels (the walk's own pixel and its two neighbours across the minor axis), each blended
/// with the colour by a coverage byte derived from the pixel's distance to the true line.
/// <para>
/// <see cref="Coverage"/> reproduces the pixel order and the coverage bytes exactly, and <see cref="Blend"/> the blend of one pixel; the caller does the colour conversion (0x5fd050) and the memory.
/// The arithmetic is the x87 at the game thread's control word 0x7F (24 bit significand): every float result is rounded to a single, the integer inputs are exact and the final conversion truncates.
/// Checked against the executable's own routine in the x86 oracle on random segments (tools/ppc-analysis/oracle_x86/map_line_oracle.py, golden file MapLineGolden).
/// </para>
/// </summary>
internal static class ParkMapLine
{
	/// <summary>
	/// Walks the segment (<paramref name="x0"/>, <paramref name="y0"/>) to (<paramref name="x1"/>, <paramref name="y1"/>) and calls <paramref name="pixel"/> with (x, y, coverage) for every pixel the routine writes,
	/// in its order: per step the walk's own pixel, then the neighbour on the side of the (normalised) line's sign, then the one on the other side. Only pixels with left &lt;= x &lt; right and
	/// top &lt;= y &lt; bottom are reported. The pixel at the end point is not drawn (the walk stops when the major axis reaches it) and a zero length segment draws nothing.
	/// </summary>
	/// <remarks>
	/// The coverage is 0 to 255, and a 0 is reported: a neighbour exactly 1.0 away is still written (its distance test is "not greater than 1.0"), and the blend with coverage 0 is not the identity
	/// (see <see cref="Blend"/>: the two upper channels lose 1), so a caller must apply it. Horizontal and vertical lines write such pixels on every step.
	/// </remarks>
	public static void Coverage( int x0, int y0, int x1, int y1, int clipLeft, int clipTop, int clipRight, int clipBottom, Action<int, int, int> pixel )
	{
		var dx = x1 - x0;
		var dy = y1 - y0;
		var absDx = dx < 0 ? -dx : dx;
		var absDy = dy < 0 ? -dy : dy;

		// [BIN:TPW-EXE:0x5efb35] the two steps (sign of each delta, 1 for zero) and the two absolute deltas; the major axis is x when |dx| >= |dy| (0x5efbc1)
		var signX = dx >= 0 ? 1 : -1;
		var signY = dy >= 0 ? 1 : -1;

		if ( absDx >= absDy )
		{
			// [BIN:TPW-EXE:0x5efbcb] a segment that runs towards smaller x is walked from its far end: the end points swap and the y step flips with the sign of the delta (also for dy = 0, which only changes
			// the order of the two neighbour writes)
			if ( dx < 0 )
			{
				(x0, x1, y0) = (x1, x0, y1);
				dy = -dy;
				signY = -signY;
			}

			// [BIN:TPW-EXE:0x5efc7e] equal x ends (so a zero length segment) leave before the first pixel
			if ( x0 == x1 )
				return;

			Walk( true, x0, y0, x1, absDx, absDy, dx, dy, signY, clipLeft, clipTop, clipRight, clipBottom, pixel );
			return;
		}

		// [BIN:TPW-EXE:0x5f00d2] the y major branch: a segment that runs towards smaller y is walked from its far end, the x step flips
		var endY = y1;
		if ( dy < 0 )
		{
			(x0, y0, endY) = (x1, y1, y0);
			dx = -dx;
			signX = -signX;
		}

		Walk( false, x0, y0, endY, absDy, absDx, dx, dy, signX, clipLeft, clipTop, clipRight, clipBottom, pixel );
	}

	/// <summary>
	/// The walk of both branches (they are mirror images): <paramref name="major"/> is the length along the walking axis, <paramref name="minor"/> the other absolute delta, <paramref name="side"/> the step of
	/// the neighbour pixels (the sign that keeps their order), <paramref name="end"/> the coordinate on the walking axis where the walk stops (exclusive).
	/// </summary>
	private static void Walk( bool alongX, int x, int y, int end, int major, int minor, int dx, int dy, int side,
		int left, int top, int right, int bottom, Action<int, int, int> pixel )
	{
		// [BIN:TPW-EXE:0x5efc23] the Bresenham constants of the walk: A = 2 |minor|, B = 2 (|minor| - major), the decision term starts at A - major, the distance numerator u at 0
		var stepA = 2 * minor;
		var stepB = 2 * (minor - major);
		var decision = stepA - major;
		var numerator = 0;

		// [BIN:TPW-EXE:0x5efc42] the length is fild / fsqrt of dx * dx + dy * dy (a 32 bit integer sum, exact in the register) rounded to a single; 1 / (2 L) is fadd / fdivr 1.0 (a single again), and
		// the cosine term K = 2 * (major * (1 / (2 L))), the product rounded to a single before the exact doubling (0x5efc96-0x5efca2)
		var squares = unchecked( dx * dx + dy * dy );
		var length = (float)Math.Sqrt( squares );
		var inverse = (float)( 1.0 / (2.0 * length) );
		var k = 2.0 * (float)( (double)major * inverse );

		while ( true )
		{
			// [BIN:TPW-EXE:0x5efcbc] t = u * (1 / (2 L)) as a single; the walk's own pixel is |t| away from the line (0x5efcc4), the neighbours |K - t| (0x5efdf8) and |K + t| (0x5eff1a): the first neighbour is
			// one `side` step along the minor axis, the second one `side` step back
			var t = (float)( (double)numerator * inverse );
			if ( alongX )
			{
				Plot( x, y, Math.Abs( (double)t ), left, top, right, bottom, pixel );
				Plot( x, y + side, Math.Abs( (double)(float)( k - t ) ), left, top, right, bottom, pixel );
				Plot( x, y - side, Math.Abs( (double)(float)( k + t ) ), left, top, right, bottom, pixel );
			}
			else
			{
				Plot( x, y, Math.Abs( (double)t ), left, top, right, bottom, pixel );
				Plot( x + side, y, Math.Abs( (double)(float)( k - t ) ), left, top, right, bottom, pixel );
				Plot( x - side, y, Math.Abs( (double)(float)( k + t ) ), left, top, right, bottom, pixel );
			}

			// [BIN:TPW-EXE:0x5f004c] the midpoint step: a negative decision term stays on the minor coordinate (u = major + d, d += A), otherwise it steps (u = d - major, d += B, the minor
			// coordinate moves by `side`); the major coordinate always advances by one, and the walk ends when it reaches `end` (0x5f00ba)
			if ( decision < 0 )
			{
				numerator = major + decision;
				decision += stepA;
			}
			else
			{
				numerator = decision - major;
				decision += stepB;
				if ( alongX )
					y += side;
				else
					x += side;
			}

			if ( alongX )
			{
				x++;
				if ( x == end )
					return;
			}
			else
			{
				y++;
				if ( y == end )
					return;
			}
		}
	}

	/// <summary>
	/// One candidate pixel: inside the clip rectangle (0x5efcb7-0x5efce9; left and top inclusive, right and bottom exclusive) and a distance that is not above 1.0 (fcomp against the double at 0x702fa8, 0x5efcf3), the
	/// coverage byte is the truncated single of (1.0 - distance) * 255.0 (0x5efd04-0x5efd14; the double at 0x702fb0, the float to int helper 0x67a830 rounds toward zero).
	/// </summary>
	private static void Plot( int x, int y, double distance, int left, int top, int right, int bottom, Action<int, int, int> pixel )
	{
		if ( x < left || x >= right || y < top || y >= bottom || distance > 1.0 )
			return;

		var coverage = (int)(float)( (double)(float)( 1.0 - distance ) * 255.0 );
		pixel( x, y, coverage );
	}

	/// <summary>
	/// The blend of one 32 bit pixel (0x5efd21-0x5efdb7): <paramref name="source"/> is the colour as the surface's pixel value (0x5fd050), <paramref name="destination"/> the pixel being replaced. Each of the three
	/// channel masks selects its bits of both pixels (still in place, not shifted down), and the channel is v = source * c + destination * (255 - c), then ((v >> 8) + v + 1) >> 8 under the mask, all in 32 bit
	/// unsigned arithmetic; the fourth mask keeps its bits of the old pixel and every bit outside the four masks is cleared. Because v is not shifted down, a channel above the lowest comes out
	/// one lower than the usual rounding of source * c / 255 for a full source (white over black gives c in the lowest channel and c - 1 in the others for c >= 2), and a coverage of 0 lowers them too.
	/// </summary>
	public static uint Blend( uint source, uint destination, int coverage, uint mask0, uint mask1, uint mask2, uint mask3 )
	{
		var c = (uint)coverage & 0xFF;
		return Channel( source, destination, c, mask0 ) | Channel( source, destination, c, mask1 ) | Channel( source, destination, c, mask2 ) | (destination & mask3);
	}

	private static uint Channel( uint source, uint destination, uint c, uint mask )
	{
		unchecked
		{
			var v = (source & mask) * c + (destination & mask) * (255u - c);
			return (((v >> 8) + v + 1) >> 8) & mask;
		}
	}
}
