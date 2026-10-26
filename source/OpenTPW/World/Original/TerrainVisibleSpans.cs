namespace OpenTPW;

/// <summary>
/// The rows and columns of heightfield cells the original draws, as tp.exe selects them each frame (docs/LND.md, "The visible cells"): the four corner rays of the view are put on the
/// ground, the convex hull of the camera and those four points is clipped to the heightfield, scaled to cells and rasterised into a start and an end column per row. The procedural
/// terrain lists the cells these spans cover (<c>0x56F090</c>), the cache holds them and the build makes their textures.
/// </summary>
/// <remarks>
/// A port of the single precision x87 arithmetic of <c>0x56BB00</c> and the routines under it; the arithmetic is the original's operation by operation (the x87 runs with the 24-bit precision
/// control that DDSCL_FPUSETUP gives), and the x86 oracle reproduces its rows, columns and distance for 400 and more cameras (<c>terrain_spans.py</c>, <see cref="TerrainVisibleSpansTests"/>
/// pin the same cases without the executable). The one deviation is the rows: the original writes a row byte outside the 128 of its tables when the heightfield has more than 127 rows, OpenTPW
/// leaves such a field without spans.
/// </remarks>
internal sealed class TerrainVisibleSpans
{
	/// <summary>Rows (and columns) the original's span tables hold: 128 signed bytes.</summary>
	public const int Rows = 128;

	private const float PiSingle = 3.1415927f;                                                  // [0x701450]
	private static readonly float SixtiethSingle = BitConverter.UInt32BitsToSingle( 0x3B360B61 );   // [0x701494] 1/360
	private static readonly float NearOne = BitConverter.UInt32BitsToSingle( 0x3F7FFFEF );          // [0x70148C] 0.999999
	private static readonly float NoHeight = BitConverter.UInt32BitsToSingle( 0x3A83126F );         // [0x701488] 0.001

	private readonly float[] _hullX = new float[48];
	private readonly float[] _hullZ = new float[48];
	private readonly float[] _clipX = new float[48];
	private readonly float[] _clipZ = new float[48];

	/// <summary>The first row of the spans (the original's start of the row range, 0x7F when empty).</summary>
	public int MinRow { get; private set; }

	/// <summary>The last row of the spans, inclusive in the tables; the cell list takes rows below it only (<c>0x56F090</c>).</summary>
	public int MaxRow { get; private set; }

	/// <summary>The first column of each row.</summary>
	public sbyte[] Start { get; } = new sbyte[Rows];

	/// <summary>One past the last column of each row.</summary>
	public sbyte[] End { get; } = new sbyte[Rows];

	/// <summary>The largest distance from the camera to a corner ray's ground point, at least 10: the value <c>0x56BB00</c> hands to <c>0x578CA0</c> (<c>0x87B110</c>, the far plane and fog).</summary>
	public float FarthestCorner { get; private set; }

	/// <summary>Whether the last <see cref="Compute"/> selected any row.</summary>
	public bool IsEmpty => MaxRow < MinRow;

	/// <summary>
	/// The spans for a camera in the heightfield's own (model) space: the position, the matrix columns right, up and forward as nine values, the lowest and highest height of the field, the field of view in
	/// degrees, the window's aspect word (height over width), the maximum distance (<c>0x8BD4C0</c>) and the heightfield's origin, cell counts and cell sizes. Returns false when the field has more rows than the tables hold.
	/// </summary>
	// [BIN:TPW-EXE:0x0056BB00 visible spans] corner rays (-w, h, 1), (w, h, 1), (w, -h, 1), (-w, -h, 1) with h = tan(fov pi / 360) and w = h / aspect, through the camera matrix (0x578D20 mode 3), each put on the ground (0x56B940), the hull of the camera and the four points (0x56BD60), clipped (0x56C5A0) to the field less its last cell, scaled by 1 / the cell size and rasterised (0x56D600)
	public bool Compute( ReadOnlySpan<float> camera, ReadOnlySpan<float> matrix, float low, float high, float fieldOfView, float aspect, float maxDistance,
		float originX, float originZ, int cellsX, int cellsZ, float sizeX, float sizeZ )
	{
		Array.Clear( Start );
		Array.Clear( End );
		MinRow = 0x7F;
		MaxRow = 0;
		FarthestCorner = 10f;
		if ( cellsX > Rows - 1 || cellsZ > Rows - 1 )
			return false;

		var angle = fieldOfView * PiSingle * SixtiethSingle;
		var h = (float)Math.Tan( angle );
		var w = h / aspect;
		Span<float> rayX = [-w, w, w, -w];
		Span<float> rayY = [h, h, -h, -h];

		var hullX = _hullX;
		var hullZ = _hullZ;
		hullX[0] = camera[0];
		hullZ[0] = camera[2];
		var count = 1;
		for ( var corner = 0; corner < 4; corner++ )
		{
			var (x, y, z) = Rotate( matrix, rayX[corner], rayY[corner] );
			var (pointX, pointY, pointZ) = GroundPoint( camera, x, y, z, low, high, maxDistance );
			count = HullAdd( hullX, hullZ, count, pointX, pointZ );
			var dx = pointX - camera[0];
			var dy = pointY - camera[1];
			var dz = pointZ - camera[2];
			var distance = (float)Math.Sqrt( dx * dx + dy * dy + dz * dz );
			if ( !(distance <= FarthestCorner) )
				FarthestCorner = distance;
		}

		var xHigh = originX + cellsX * sizeX - sizeX;
		var zHigh = originZ + cellsZ * sizeZ - sizeZ;
		var clipX = _clipX;
		var clipZ = _clipZ;
		count = ClipAxis( hullX, hullZ, count, clipX, clipZ, false, originZ, zHigh );
		count = ClipAxis( clipX, clipZ, count, hullX, hullZ, true, originX, xHigh );
		var inverseX = 1f / sizeX;
		var inverseZ = 1f / sizeZ;
		for ( var index = 0; index < count; index++ )
		{
			hullX[index] *= inverseX;
			hullZ[index] *= inverseZ;
		}
		Rasterise( hullX, hullZ, count );
		return true;
	}

	/// <summary>0x578D20 mode 3: the ray through the matrix's columns; the products in the order the x87 adds them (the two nearer terms first).</summary>
	private static (float X, float Y, float Z) Rotate( ReadOnlySpan<float> m, float x, float y )
	{
		// the ray's z is 1
		var rx = m[0] * x + m[6] + m[3] * y;
		var ry = m[4] * y + m[7] + m[1] * x;
		var rz = m[5] * y + m[8] + m[2] * x;
		return (rx, ry, rz);
	}

	/// <summary>0x56B940: the ray's point on the ground; a parameter that is negative or beyond half the maximum distance becomes that half, equal parameters give that value and others their mean.</summary>
	private static (float X, float Y, float Z) GroundPoint( ReadOnlySpan<float> camera, float dx, float dy, float dz, float low, float high, float maxDistance )
	{
		if ( dy == 0f )
			dy = NoHeight;
		var half = maxDistance * 0.5f;
		var t1 = (low - camera[1]) / dy;
		if ( t1 < 0f || t1 > half )
			t1 = half;
		var t2 = (high - camera[1]) / dy;
		if ( t2 < 0f || t2 > half )
			t2 = half;
		var t = t1 == t2 ? t2 : (t2 + t1) * 0.5f;
		return (t * dx + camera[0], t * dy + camera[1], t * dz + camera[2]);
	}

	/// <summary>(p.x - a.x) * -(b.z - a.z) + (p.z - a.z) * (b.x - a.x)</summary>
	private static float Cross( float px, float pz, float ax, float az, float bx, float bz )
		=> (px - ax) * -(bz - az) + (pz - az) * (bx - ax);

	/// <summary>0x56BD60: adds a point to the convex hull of x and z; the hull keeps one orientation whichever way the first three points turn.</summary>
	private static int HullAdd( float[] hx, float[] hz, int count, float px, float pz )
	{
		if ( count == 0 )
		{
			hx[0] = px;
			hz[0] = pz;
			return 1;
		}
		if ( count == 1 )
		{
			if ( px != hx[0] || pz != hz[0] )
			{
				hx[1] = px;
				hz[1] = pz;
				return 2;
			}
			return 1;
		}
		if ( count == 2 )
		{
			var side = Cross( px, pz, hx[0], hz[0], hx[1], hz[1] );
			if ( side > 0f )
			{
				hx[2] = hx[1];
				hz[2] = hz[1];
				hx[1] = px;
				hz[1] = pz;
				return 3;
			}
			if ( side < 0f )
			{
				hx[2] = px;
				hz[2] = pz;
				return 3;
			}
			var s = hx[1] - hx[0] == 0f ? (pz - hz[0]) / (hz[1] - hz[0]) : (px - hx[0]) / (hx[1] - hx[0]);
			if ( s <= 1f )
			{
				if ( s < 0f )
				{
					hx[0] = px;
					hz[0] = pz;
				}
			}
			else
			{
				hx[1] = px;
				hz[1] = pz;
			}
			return 2;
		}

		Span<bool> outside = stackalloc bool[count];
		var visible = 0;
		for ( var e = count - 1; e >= 0; e-- )
		{
			var j = e - 1 >= 0 ? e - 1 : count - 1;
			outside[e] = 0f < Cross( px, pz, hx[j], hz[j], hx[e], hz[e] );
			if ( outside[e] )
				visible++;
		}
		if ( visible == 0 )
			return count;
		var first = outside.IndexOf( true );
		if ( visible < 2 )
		{
			for ( var index = count; index > first; index-- )
			{
				hx[index] = hx[index - 1];
				hz[index] = hz[index - 1];
			}
			hx[first] = px;
			hz[first] = pz;
			return count + 1;
		}

		var cursor = count - 1;
		while ( !outside[cursor] )
			cursor--;
		while ( cursor > -1 && outside[cursor] )
			cursor--;
		var start = cursor + 1;
		Span<float> rotatedX = stackalloc float[count];
		Span<float> rotatedZ = stackalloc float[count];
		Span<bool> rotatedOutside = stackalloc bool[count];
		for ( var k = 0; k < count; k++ )
		{
			rotatedX[k] = hx[(k + start) % count];
			rotatedZ[k] = hz[(k + start) % count];
			rotatedOutside[k] = outside[(k + start) % count];
		}
		hx[0] = px;
		hz[0] = pz;
		hx[1] = rotatedX[visible - 1];
		hz[1] = rotatedZ[visible - 1];
		var written = 2;
		var read = 1;
		while ( read != 0 )
		{
			if ( !rotatedOutside[read] )
			{
				hx[written] = rotatedX[read];
				hz[written] = rotatedZ[read];
				written++;
			}
			read++;
			if ( read == count )
				read = 0;
		}
		return written;
	}

	/// <summary>
	/// One pass of 0x56C5A0 over the z (or the x) slab: each edge from the previous to the current point is classed by where its ends lie (0 at or below <paramref name="low"/>, 1 inside, 2 above <paramref name="high"/>);
	/// code = previous + 3 current gives the points kept: the previous point when it is inside, and the crossings (0x56D590 for z, 0x56D520 for x).
	/// </summary>
	private static int ClipAxis( float[] inX, float[] inZ, int count, float[] outX, float[] outZ, bool alongX, float low, float high )
	{
		var written = 0;
		var previous = count - 1;
		for ( var current = 0; current < count; current++ )
		{
			var valueCur = alongX ? inX[current] : inZ[current];
			var valuePrev = alongX ? inX[previous] : inZ[previous];
			var classCur = (low < valueCur ? 1 : 0) + (high < valueCur ? 1 : 0);
			var classPrev = (low < valuePrev ? 1 : 0) + (high < valuePrev ? 1 : 0);
			// the points kept: 1 = the previous point, 2 = the crossing of the high bound, 3 = the crossing of the low bound
			var first = 0;
			var second = 0;
			switch ( classPrev + 3 * classCur )
			{
				case 1: first = 1; second = 3; break;
				case 2: first = 2; second = 3; break;
				case 3: first = 3; break;
				case 4: first = 1; break;
				case 5: first = 2; break;
				case 6: first = 3; second = 2; break;
				case 7: first = 1; second = 2; break;
			}
			for ( var step = 0; step < 2; step++ )
			{
				var kind = step == 0 ? first : second;
				if ( kind == 0 )
					continue;
				if ( kind == 1 )
				{
					outX[written] = inX[previous];
					outZ[written] = inZ[previous];
				}
				else
				{
					var bound = kind == 2 ? high : low;
					if ( alongX )
					{
						var f = (bound - inX[previous]) / (inX[current] - inX[previous]);
						outX[written] = bound;
						outZ[written] = (inZ[current] - inZ[previous]) * f + inZ[previous];
					}
					else
					{
						var f = (bound - inZ[previous]) / (inZ[current] - inZ[previous]);
						outX[written] = (inX[current] - inX[previous]) * f + inX[previous];
						outZ[written] = bound;
					}
				}
				written++;
			}
			previous = current;
		}
		return written;
	}

	/// <summary>0x56D600: the columns the polygon touches on each row, from the edges: a falling edge lowers the row's start, a rising one raises its end (plus 0.999999 for the ends).</summary>
	private void Rasterise( float[] px, float[] pz, int count )
	{
		var start = Start;
		var end = End;
		Array.Fill( start, (sbyte)0x7F );
		var previous = count - 1;
		for ( var index = 0; index < count; index++ )
		{
			float zCur = pz[index], zPrev = pz[previous];
			if ( zCur > zPrev )
			{
				var a = (int)px[previous];
				var b = (int)px[index];
				var zMin = (int)zPrev;
				var zMax = (int)(zCur + NearOne);
				var slope = (px[index] - px[previous]) / (zCur - zPrev);
				var x = px[previous];
				if ( (int)zCur > (int)zPrev )
				{
					var k = (int)(zPrev + NearOne);
					x = (k - zPrev) * slope + x;
				}
				if ( zMin < MinRow )
					MinRow = zMin;
				if ( zMax > MaxRow )
					MaxRow = zMax;
				if ( (uint)zMin < Rows && start[zMin] > a )
					start[zMin] = (sbyte)a;
				if ( (uint)zMax < Rows && start[zMax] > b )
					start[zMax] = (sbyte)b;
				if ( (uint)zMin < Rows && start[zMin] > (int)x )
					start[zMin] = (sbyte)(int)x;
				for ( var row = zMin + 1; row < zMax; row++ )
				{
					if ( (uint)row < Rows && start[row] > (int)x )
						start[row] = (sbyte)(int)x;
					if ( row < zMax - 1 )
					{
						x += slope;
						if ( (uint)row < Rows && start[row] > (int)x )
							start[row] = (sbyte)(int)x;
					}
				}
				if ( (uint)zMax < Rows && start[zMax] > (int)x )
					start[zMax] = (sbyte)(int)x;
			}
			else if ( zCur < zPrev )
			{
				var a = (int)(px[previous] + NearOne);
				var b = (int)(px[index] + NearOne);
				var zHigh = (int)(zPrev + NearOne);
				var zLow = (int)zCur;
				var slope = (px[previous] - px[index]) / (zPrev - zCur);
				var x = px[index] + NearOne;
				if ( (int)zCur < (int)zPrev )
				{
					var k = (int)(zCur + NearOne);
					x = (k - zCur) * slope + x;
				}
				if ( zLow < MinRow )
					MinRow = zLow;
				if ( zHigh > MaxRow )
					MaxRow = zHigh;
				if ( (uint)zHigh < Rows && end[zHigh] < a )
					end[zHigh] = (sbyte)a;
				if ( (uint)zLow < Rows && end[zLow] < b )
					end[zLow] = (sbyte)b;
				if ( (uint)zLow < Rows && end[zLow] < (int)x )
					end[zLow] = (sbyte)(int)x;
				for ( var row = zLow + 1; row < zHigh; row++ )
				{
					if ( (uint)row < Rows && end[row] < (int)x )
						end[row] = (sbyte)(int)x;
					if ( row < zHigh - 1 )
					{
						x += slope;
						if ( (uint)row < Rows && end[row] < (int)x )
							end[row] = (sbyte)(int)x;
					}
				}
				if ( (uint)zHigh < Rows && end[zHigh] < (int)x )
					end[zHigh] = (sbyte)(int)x;
			}
			previous = index;
		}
		for ( var row = MinRow; row <= MaxRow; row++ )
			if ( (uint)row < Rows && end[row] < start[row] )
				end[row] = start[row];
	}
}
