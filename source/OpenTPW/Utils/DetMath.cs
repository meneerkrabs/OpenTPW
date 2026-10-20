namespace OpenTPW;

/// <summary>
/// Deterministic elementary functions for simulation code (docs/DETERMINISM.md, "Browser and native"): sin, cos, atan, atan2, acos,
/// pow and log2 written out in C# with a fixed order of IEEE double operations (add, subtract, multiply, divide, square root), so
/// every runtime computes the same bits. <see cref="Math.Sin(double)"/> and its relatives call the platform's C library instead
/// (MSVC on Windows, glibc on Linux, Apple's libm on macOS, emscripten's musl in the browser), and those libraries may round the
/// last bit differently. A lockstep session (docs/ONLINE.md, co-op) needs the browser and every desktop to agree bit for bit.
/// <para>
/// The routines are ports of FreeBSD's msun as musl ships them (<c>__sin.c</c>, <c>__cos.c</c>, <c>__rem_pio2.c</c>,
/// <c>__rem_pio2_large.c</c>, <c>sin.c</c>, <c>cos.c</c>, <c>atan.c</c>, <c>atan2.c</c>, <c>acos.c</c>, <c>pow_small.c</c> =
/// FreeBSD <c>e_pow.c</c>, <c>log2_small.c</c> = FreeBSD <c>e_log2.c</c>). Their error is below one unit in the last place; they
/// are not correctly rounded, so a result can differ from another library's in the last bit, but it is the same on every runtime.
/// <see cref="DetMathF"/> has the single-precision versions.
/// </para>
/// <para>
/// Results that are NaN are returned as <see cref="double.NaN"/>: a NaN's sign and payload made by arithmetic differ between
/// x86, ARM and WebAssembly.
/// </para>
/// </summary>
/// <remarks>
/// Copyright (C) 1993, 2004 by Sun Microsystems, Inc. All rights reserved. Developed at SunSoft, a Sun Microsystems, Inc. business.
/// Permission to use, copy, modify, and distribute this software is freely granted, provided that this notice is preserved.
/// (musl's adaptation: Copyright © 2005-2020 Rich Felker, et al., MIT licence.)
/// </remarks>
// [EXT:deterministic-math] simulation code evaluates the elementary functions here, in IEEE double with a fixed order of operations, so every runtime computes the same bits; tp.exe evaluates them on the x87 in extended precision (fsin, fpatan, fyl2x) or calls its C runtime (pow, acos). User decision, 2026-10-19: "Als EXT: deterministic-math". The start-up tables built with it equal the original's and keep their binary-evidence labels.
public static class DetMath
{
	// ---- bit access -------------------------------------------------------------------------------------------------------

	private static uint High( double x ) => (uint)(BitConverter.DoubleToUInt64Bits( x ) >> 32);
	private static uint Low( double x ) => (uint)BitConverter.DoubleToUInt64Bits( x );
	private static double FromWords( uint high, uint low ) => BitConverter.UInt64BitsToDouble( ((ulong)high << 32) | low );
	private static double WithHigh( double x, uint high ) => FromWords( high, Low( x ) );
	private static double WithLow( double x, uint low ) => FromWords( High( x ), low );

	/// <summary>x · 2ⁿ (musl's <c>scalbn</c>).</summary>
	private static double ScaleB( double x, int n )
	{
		var y = x;
		if ( n > 1023 )
		{
			y *= 8.98846567431158e307; // 0x1p1023
			n -= 1023;
			if ( n > 1023 )
			{
				y *= 8.98846567431158e307;
				n -= 1023;
				if ( n > 1023 )
					n = 1023;
			}
		}
		else if ( n < -1022 )
		{
			// 0x1p-1022 · 0x1p53 = 0x1p-969: the final n stays below -53, so no double rounding in the subnormal range
			y *= 2.0041683600089728e-292;
			n += 1022 - 53;
			if ( n < -1022 )
			{
				y *= 2.0041683600089728e-292;
				n += 1022 - 53;
				if ( n < -1022 )
					n = -1022;
			}
		}
		return y * BitConverter.UInt64BitsToDouble( (ulong)(0x3ff + n) << 52 );
	}

	// ---- sin and cos ------------------------------------------------------------------------------------------------------

	private const double S1 = -1.66666666666666324348e-01, S2 = 8.33333333332248946124e-03, S3 = -1.98412698298579493134e-04,
		S4 = 2.75573137070700676789e-06, S5 = -2.50507602534068634195e-08, S6 = 1.58969099521155010221e-10;

	/// <summary>sin(x + y) for |x + y| ≲ π/4, y the tail of x; <paramref name="iy"/> 0 when y is zero (<c>__sin.c</c>).</summary>
	private static double KernelSin( double x, double y, int iy )
	{
		var z = x * x;
		var w = z * z;
		var r = S2 + z * (S3 + z * S4) + z * w * (S5 + z * S6);
		var v = z * x;
		if ( iy == 0 )
			return x + v * (S1 + z * r);
		return x - ((z * (0.5 * y - v * r) - y) - v * S1);
	}

	private const double C1 = 4.16666666666666019037e-02, C2 = -1.38888888888741095749e-03, C3 = 2.48015872894767294178e-05,
		C4 = -2.75573143513906633035e-07, C5 = 2.08757232129817482790e-09, C6 = -1.13596475577881948265e-11;

	/// <summary>cos(x + y) for |x + y| ≲ π/4 (<c>__cos.c</c>).</summary>
	private static double KernelCos( double x, double y )
	{
		var z = x * x;
		var w = z * z;
		var r = z * (C1 + z * (C2 + z * C3)) + w * w * (C4 + z * (C5 + z * C6));
		var hz = 0.5 * z;
		w = 1.0 - hz;
		return w + (((1.0 - w) - hz) + (z * r - x * y));
	}

	/// <summary>1.5 / DBL_EPSILON = 0x1.8p52: adding and subtracting it rounds a double below 2⁵¹ to an integer (C#'s double.Epsilon is the smallest subnormal, not DBL_EPSILON).</summary>
	private const double ToInt = 6755399441055744.0;
	private const double PiOver4 = 7.85398163397448278999e-01; // 0x1.921fb54442d18p-1
	private const double InvPiOver2 = 6.36619772367581382433e-01, PiOver2_1 = 1.57079632673412561417e+00, PiOver2_1t = 6.07710050650619224932e-11,
		PiOver2_2 = 6.07710050630396597660e-11, PiOver2_2t = 2.02226624879595063154e-21, PiOver2_3 = 2.02226624871116645580e-21,
		PiOver2_3t = 8.47842766036889956997e-32;

	/// <summary>x − n·π/2 as y0 + y1 with |y| ≲ π/4; returns n (<c>__rem_pio2.c</c>). The caller handles |x| ≲ π/4.</summary>
	private static int RemPiOver2( double x, out double y0, out double y1 )
	{
		var bits = BitConverter.DoubleToUInt64Bits( x );
		var negative = (bits >> 63) != 0;
		var ix = (uint)(bits >> 32) & 0x7fffffff;
		double z, w, t, r, fn;
		int n;
		var medium = false;
		if ( ix <= 0x400f6a7a )
		{
			// |x| ≲ 5π/4
			if ( (ix & 0xfffff) == 0x921fb )
				medium = true; // |x| ≈ π/2 or 2π/2: cancellation, use the medium case
			else if ( ix <= 0x4002d97c )
			{
				// |x| ≲ 3π/4
				if ( !negative )
				{
					z = x - PiOver2_1;
					y0 = z - PiOver2_1t;
					y1 = (z - y0) - PiOver2_1t;
					return 1;
				}
				z = x + PiOver2_1;
				y0 = z + PiOver2_1t;
				y1 = (z - y0) + PiOver2_1t;
				return -1;
			}
			else
			{
				if ( !negative )
				{
					z = x - 2 * PiOver2_1;
					y0 = z - 2 * PiOver2_1t;
					y1 = (z - y0) - 2 * PiOver2_1t;
					return 2;
				}
				z = x + 2 * PiOver2_1;
				y0 = z + 2 * PiOver2_1t;
				y1 = (z - y0) + 2 * PiOver2_1t;
				return -2;
			}
		}
		else if ( ix <= 0x401c463b )
		{
			// |x| ≲ 9π/4
			if ( ix <= 0x4015fdbc )
			{
				// |x| ≲ 7π/4
				if ( ix == 0x4012d97c )
					medium = true; // |x| ≈ 3π/2
				else if ( !negative )
				{
					z = x - 3 * PiOver2_1;
					y0 = z - 3 * PiOver2_1t;
					y1 = (z - y0) - 3 * PiOver2_1t;
					return 3;
				}
				else
				{
					z = x + 3 * PiOver2_1;
					y0 = z + 3 * PiOver2_1t;
					y1 = (z - y0) + 3 * PiOver2_1t;
					return -3;
				}
			}
			else
			{
				if ( ix == 0x401921fb )
					medium = true; // |x| ≈ 4π/2
				else if ( !negative )
				{
					z = x - 4 * PiOver2_1;
					y0 = z - 4 * PiOver2_1t;
					y1 = (z - y0) - 4 * PiOver2_1t;
					return 4;
				}
				else
				{
					z = x + 4 * PiOver2_1;
					y0 = z + 4 * PiOver2_1t;
					y1 = (z - y0) + 4 * PiOver2_1t;
					return -4;
				}
			}
		}
		if ( medium || ix < 0x413921fb )
		{
			// |x| ≲ 2²⁰·π/2: rint(x / (π/2)) and three rounds of Cody-Waite reduction at most
			fn = x * InvPiOver2 + ToInt - ToInt;
			n = (int)fn;
			r = x - fn * PiOver2_1;
			w = fn * PiOver2_1t;
			// matters only with directed rounding, kept for the same operations
			if ( r - w < -PiOver4 )
			{
				n--;
				fn--;
				r = x - fn * PiOver2_1;
				w = fn * PiOver2_1t;
			}
			else if ( r - w > PiOver4 )
			{
				n++;
				fn++;
				r = x - fn * PiOver2_1;
				w = fn * PiOver2_1t;
			}
			y0 = r - w;
			var ey = (int)(BitConverter.DoubleToUInt64Bits( y0 ) >> 52) & 0x7ff;
			var ex = (int)(ix >> 20);
			if ( ex - ey > 16 )
			{
				t = r;
				w = fn * PiOver2_2;
				r = t - w;
				w = fn * PiOver2_2t - ((t - r) - w);
				y0 = r - w;
				ey = (int)(BitConverter.DoubleToUInt64Bits( y0 ) >> 52) & 0x7ff;
				if ( ex - ey > 49 )
				{
					t = r;
					w = fn * PiOver2_3;
					r = t - w;
					w = fn * PiOver2_3t - ((t - r) - w);
					y0 = r - w;
				}
			}
			y1 = (r - y0) - w;
			return n;
		}
		if ( ix >= 0x7ff00000 )
		{
			y0 = y1 = double.NaN;
			return 0;
		}
		// large arguments: z = |x| scaled to [2²³, 2²⁴), split into three 24-bit pieces
		z = BitConverter.UInt64BitsToDouble( (bits & (ulong.MaxValue >> 12)) | ((ulong)(0x3ff + 23) << 52) );
		Span<double> tx = stackalloc double[3];
		int i;
		for ( i = 0; i < 2; i++ )
		{
			tx[i] = (int)z;
			z = (z - tx[i]) * 16777216.0;
		}
		tx[i] = z;
		while ( tx[i] == 0.0 )
			i--;
		n = RemPiOver2Large( tx, out var ty0, out var ty1, (int)(ix >> 20) - (0x3ff + 23), i + 1 );
		if ( negative )
		{
			y0 = -ty0;
			y1 = -ty1;
			return -n;
		}
		y0 = ty0;
		y1 = ty1;
		return n;
	}

	/// <summary>2/π in 24-bit pieces: enough for any double argument (<c>__rem_pio2_large.c</c>).</summary>
	private static readonly int[] TwoOverPi =
	{
		0xA2F983, 0x6E4E44, 0x1529FC, 0x2757D1, 0xF534DD, 0xC0DB62,
		0x95993C, 0x439041, 0xFE5163, 0xABDEBB, 0xC561B7, 0x246E3A,
		0x424DD2, 0xE00649, 0x2EEA09, 0xD1921C, 0xFE1DEB, 0x1CB129,
		0xA73EE8, 0x8235F5, 0x2EBB44, 0x84E99C, 0x7026B4, 0x5F7E41,
		0x3991D6, 0x398353, 0x39F49C, 0x845F8B, 0xBDF928, 0x3B1FF8,
		0x97FFDE, 0x05980F, 0xEF2F11, 0x8B5A0A, 0x6D1F6D, 0x367ECF,
		0x27CB09, 0xB74F46, 0x3F669E, 0x5FEA2D, 0x7527BA, 0xC7EBE5,
		0xF17B3D, 0x0739F7, 0x8A5292, 0xEA6BFB, 0x5FB11F, 0x8D5D08,
		0x560330, 0x46FC7B, 0x6BABF0, 0xCFBC20, 0x9AF436, 0x1DA9E3,
		0x91615E, 0xE61B08, 0x659985, 0x5F14A0, 0x68408D, 0xFFD880,
		0x4D7327, 0x310606, 0x1556CA, 0x73A8C9, 0x60E27B, 0xC08C6B,
	};

	private static readonly double[] PiOver2Pieces =
	{
		1.57079625129699707031e+00, 7.54978941586159635335e-08, 5.39030252995776476554e-15, 3.28200341580791294123e-22,
		1.27065575308067607349e-29, 1.22933308981111328932e-36, 2.73370053816464559624e-44, 2.16741683877804819444e-51,
	};

	/// <summary>Payne-Hanek reduction of a large argument given as 24-bit pieces, double precision (prec 1); returns n mod 8.</summary>
	private static int RemPiOver2Large( ReadOnlySpan<double> x, out double y0, out double y1, int e0, int nx )
	{
		const int jk = 4; // init_jk[1]
		const int jp = jk;
		Span<int> iq = stackalloc int[20];
		Span<double> f = stackalloc double[20];
		Span<double> fq = stackalloc double[20];
		Span<double> q = stackalloc double[20];
		int jz, carry, n, i, j, k, ih;
		double z, fw;

		var jx = nx - 1;
		var jv = (e0 - 3) / 24;
		if ( jv < 0 )
			jv = 0;
		var q0 = e0 - 24 * (jv + 1);

		j = jv - jx;
		var m = jx + jk;
		for ( i = 0; i <= m; i++, j++ )
			f[i] = j < 0 ? 0.0 : TwoOverPi[j];

		for ( i = 0; i <= jk; i++ )
		{
			for ( j = 0, fw = 0.0; j <= jx; j++ )
				fw += x[j] * f[jx + i - j];
			q[i] = fw;
		}

		jz = jk;
		while ( true )
		{
			// distill q[] into iq[] reversingly
			for ( i = 0, j = jz, z = q[jz]; j > 0; i++, j-- )
			{
				fw = (int)(5.9604644775390625e-08 * z);
				iq[i] = (int)(z - 16777216.0 * fw);
				z = q[j - 1] + fw;
			}

			z = ScaleB( z, q0 );
			z -= 8.0 * Math.Floor( z * 0.125 );
			n = (int)z;
			z -= n;
			ih = 0;
			if ( q0 > 0 )
			{
				i = iq[jz - 1] >> (24 - q0);
				n += i;
				iq[jz - 1] -= i << (24 - q0);
				ih = iq[jz - 1] >> (23 - q0);
			}
			else if ( q0 == 0 )
				ih = iq[jz - 1] >> 23;
			else if ( z >= 0.5 )
				ih = 2;

			if ( ih > 0 )
			{
				n += 1;
				carry = 0;
				for ( i = 0; i < jz; i++ )
				{
					j = iq[i];
					if ( carry == 0 )
					{
						if ( j != 0 )
						{
							carry = 1;
							iq[i] = 0x1000000 - j;
						}
					}
					else
						iq[i] = 0xffffff - j;
				}
				if ( q0 > 0 )
				{
					if ( q0 == 1 )
						iq[jz - 1] &= 0x7fffff;
					else if ( q0 == 2 )
						iq[jz - 1] &= 0x3fffff;
				}
				if ( ih == 2 )
				{
					z = 1.0 - z;
					if ( carry != 0 )
						z -= ScaleB( 1.0, q0 );
				}
			}

			if ( z == 0.0 )
			{
				j = 0;
				for ( i = jz - 1; i >= jk; i-- )
					j |= iq[i];
				if ( j == 0 )
				{
					// recomputation needed
					for ( k = 1; iq[jk - k] == 0; k++ )
					{
					}
					for ( i = jz + 1; i <= jz + k; i++ )
					{
						f[jx + i] = TwoOverPi[jv + i];
						for ( j = 0, fw = 0.0; j <= jx; j++ )
							fw += x[j] * f[jx + i - j];
						q[i] = fw;
					}
					jz += k;
					continue;
				}
			}
			break;
		}

		// chop off zero terms
		if ( z == 0.0 )
		{
			jz -= 1;
			q0 -= 24;
			while ( iq[jz] == 0 )
			{
				jz--;
				q0 -= 24;
			}
		}
		else
		{
			z = ScaleB( z, -q0 );
			if ( z >= 16777216.0 )
			{
				fw = (int)(5.9604644775390625e-08 * z);
				iq[jz] = (int)(z - 16777216.0 * fw);
				jz += 1;
				q0 += 24;
				iq[jz] = (int)fw;
			}
			else
				iq[jz] = (int)z;
		}

		fw = ScaleB( 1.0, q0 );
		for ( i = jz; i >= 0; i-- )
		{
			q[i] = fw * iq[i];
			fw *= 5.9604644775390625e-08;
		}

		for ( i = jz; i >= 0; i-- )
		{
			for ( fw = 0.0, k = 0; k <= jp && k <= jz - i; k++ )
				fw += PiOver2Pieces[k] * q[i + k];
			fq[jz - i] = fw;
		}

		fw = 0.0;
		for ( i = jz; i >= 0; i-- )
			fw += fq[i];
		y0 = ih == 0 ? fw : -fw;
		fw = fq[0] - fw;
		for ( i = 1; i <= jz; i++ )
			fw += fq[i];
		y1 = ih == 0 ? fw : -fw;
		return n & 7;
	}

	/// <summary>sin x (<c>sin.c</c>).</summary>
	public static double Sin( double x )
	{
		var ix = High( x ) & 0x7fffffff;
		if ( ix <= 0x3fe921fb )
		{
			if ( ix < 0x3e500000 )
				return x; // |x| < 2⁻²⁶
			return KernelSin( x, 0.0, 0 );
		}
		if ( ix >= 0x7ff00000 )
			return double.NaN;
		var n = RemPiOver2( x, out var y0, out var y1 );
		return (n & 3) switch
		{
			0 => KernelSin( y0, y1, 1 ),
			1 => KernelCos( y0, y1 ),
			2 => -KernelSin( y0, y1, 1 ),
			_ => -KernelCos( y0, y1 ),
		};
	}

	/// <summary>cos x (<c>cos.c</c>).</summary>
	public static double Cos( double x )
	{
		var ix = High( x ) & 0x7fffffff;
		if ( ix <= 0x3fe921fb )
		{
			if ( ix < 0x3e46a09e )
				return 1.0; // |x| < 2⁻²⁷·√2
			return KernelCos( x, 0 );
		}
		if ( ix >= 0x7ff00000 )
			return double.NaN;
		var n = RemPiOver2( x, out var y0, out var y1 );
		return (n & 3) switch
		{
			0 => KernelCos( y0, y1 ),
			1 => -KernelSin( y0, y1, 1 ),
			2 => -KernelCos( y0, y1 ),
			_ => KernelSin( y0, y1, 1 ),
		};
	}

	// ---- atan, atan2 and acos ---------------------------------------------------------------------------------------------

	private static readonly double[] AtanHigh = { 4.63647609000806093515e-01, 7.85398163397448278999e-01, 9.82793723247329054082e-01, 1.57079632679489655800e+00 };
	private static readonly double[] AtanLow = { 2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17 };
	private const double AT0 = 3.33333333333329318027e-01, AT1 = -1.99999999998764832476e-01, AT2 = 1.42857142725034663711e-01,
		AT3 = -1.11111104054623557880e-01, AT4 = 9.09088713343650656196e-02, AT5 = -7.69187620504482999495e-02, AT6 = 6.66107313738753120669e-02,
		AT7 = -5.83357013379057348645e-02, AT8 = 4.97687799461593236017e-02, AT9 = -3.65315727442169155270e-02, AT10 = 1.62858201153657823623e-02;

	/// <summary>atan x (<c>atan.c</c>).</summary>
	public static double Atan( double x )
	{
		var high = High( x );
		var negative = (high >> 31) != 0;
		var ix = high & 0x7fffffff;
		int id;
		if ( ix >= 0x44100000 )
		{
			// |x| ≥ 2⁶⁶
			if ( double.IsNaN( x ) )
				return double.NaN;
			var limit = AtanHigh[3];
			return negative ? -limit : limit;
		}
		if ( ix < 0x3fdc0000 )
		{
			// |x| < 0.4375
			if ( ix < 0x3e400000 )
				return x; // |x| < 2⁻²⁷
			id = -1;
		}
		else
		{
			x = Math.Abs( x );
			if ( ix < 0x3ff30000 )
			{
				if ( ix < 0x3fe60000 )
				{
					id = 0;
					x = (2.0 * x - 1.0) / (2.0 + x);
				}
				else
				{
					id = 1;
					x = (x - 1.0) / (x + 1.0);
				}
			}
			else if ( ix < 0x40038000 )
			{
				id = 2;
				x = (x - 1.5) / (1.0 + 1.5 * x);
			}
			else
			{
				id = 3;
				x = -1.0 / x;
			}
		}
		var z = x * x;
		var w = z * z;
		var s1 = z * (AT0 + w * (AT2 + w * (AT4 + w * (AT6 + w * (AT8 + w * AT10)))));
		var s2 = w * (AT1 + w * (AT3 + w * (AT5 + w * (AT7 + w * AT9))));
		if ( id < 0 )
			return x - x * (s1 + s2);
		z = AtanHigh[id] - (x * (s1 + s2) - AtanLow[id] - x);
		return negative ? -z : z;
	}

	private const double Pi = 3.1415926535897931160E+00, PiLow = 1.2246467991473531772E-16;

	/// <summary>atan2(y, x) (<c>atan2.c</c>).</summary>
	public static double Atan2( double y, double x )
	{
		if ( double.IsNaN( x ) || double.IsNaN( y ) )
			return double.NaN;
		uint ix = High( x ), lx = Low( x ), iy = High( y ), ly = Low( y );
		if ( ((ix - 0x3ff00000) | lx) == 0 )
			return Atan( y ); // x = 1.0
		var m = ((iy >> 31) & 1) | ((ix >> 30) & 2); // 2·sign(x) + sign(y)
		ix &= 0x7fffffff;
		iy &= 0x7fffffff;
		if ( (iy | ly) == 0 )
		{
			switch ( m )
			{
				case 0:
				case 1: return y;
				case 2: return Pi;
				default: return -Pi;
			}
		}
		if ( (ix | lx) == 0 )
			return (m & 1) != 0 ? -Pi / 2 : Pi / 2;
		if ( ix == 0x7ff00000 )
		{
			if ( iy == 0x7ff00000 )
			{
				return m switch
				{
					0 => Pi / 4,
					1 => -Pi / 4,
					2 => 3 * Pi / 4,
					_ => -3 * Pi / 4,
				};
			}
			return m switch
			{
				0 => 0.0,
				1 => -0.0,
				2 => Pi,
				_ => -Pi,
			};
		}
		// |y/x| > 2⁶⁴
		if ( ix + (64 << 20) < iy || iy == 0x7ff00000 )
			return (m & 1) != 0 ? -Pi / 2 : Pi / 2;
		double z;
		if ( (m & 2) != 0 && iy + (64 << 20) < ix )
			z = 0; // |y/x| < 2⁻⁶⁴, x < 0
		else
			z = Atan( Math.Abs( y / x ) );
		return m switch
		{
			0 => z,
			1 => -z,
			2 => Pi - (z - PiLow),
			_ => (z - PiLow) - Pi,
		};
	}

	private const double PiOver2High = 1.57079632679489655800e+00, PiOver2Low = 6.12323399573676603587e-17,
		PS0 = 1.66666666666666657415e-01, PS1 = -3.25565818622400915405e-01, PS2 = 2.01212532134862925881e-01, PS3 = -4.00555345006794114027e-02,
		PS4 = 7.91534994289814532176e-04, PS5 = 3.47933107596021167570e-05, QS1 = -2.40339491173441421878e+00, QS2 = 2.02094576023350569471e+00,
		QS3 = -6.88283971605453293030e-01, QS4 = 7.70381505559019352791e-02;

	private static double AcosRatio( double z )
	{
		var p = z * (PS0 + z * (PS1 + z * (PS2 + z * (PS3 + z * (PS4 + z * PS5)))));
		var q = 1.0 + z * (QS1 + z * (QS2 + z * (QS3 + z * QS4)));
		return p / q;
	}

	/// <summary>acos x (<c>acos.c</c>).</summary>
	public static double Acos( double x )
	{
		var hx = High( x );
		var ix = hx & 0x7fffffff;
		if ( ix >= 0x3ff00000 )
		{
			// |x| ≥ 1 or NaN
			if ( ((ix - 0x3ff00000) | Low( x )) == 0 )
				return (hx >> 31) != 0 ? 2 * PiOver2High : 0;
			return double.NaN;
		}
		if ( ix < 0x3fe00000 )
		{
			// |x| < 0.5
			if ( ix <= 0x3c600000 )
				return PiOver2High; // |x| < 2⁻⁵⁷
			return PiOver2High - (x - (PiOver2Low - x * AcosRatio( x * x )));
		}
		double z, w, s;
		if ( (hx >> 31) != 0 )
		{
			// x < -0.5
			z = (1.0 + x) * 0.5;
			s = Math.Sqrt( z );
			w = AcosRatio( z ) * s - PiOver2Low;
			return 2 * (PiOver2High - (s + w));
		}
		// x > 0.5
		z = (1.0 - x) * 0.5;
		s = Math.Sqrt( z );
		var df = WithLow( s, 0 );
		var c = (z - df * df) / (s + df);
		w = AcosRatio( z ) * s + c;
		return 2 * (df + w);
	}

	// ---- log2 -------------------------------------------------------------------------------------------------------------

	private const double InvLn2High = 1.44269504072144627571e+00, InvLn2Low = 1.67517131648865118353e-10,
		Lg1 = 6.666666666666735130e-01, Lg2 = 3.999999999940941908e-01, Lg3 = 2.857142874366239149e-01, Lg4 = 2.222219843214978396e-01,
		Lg5 = 1.818357216161805012e-01, Lg6 = 1.531383769920937332e-01, Lg7 = 1.479819860511658591e-01;

	/// <summary>log₂ x (FreeBSD <c>e_log2.c</c>, musl <c>log2_small.c</c>).</summary>
	public static double Log2( double x )
	{
		var bits = BitConverter.DoubleToUInt64Bits( x );
		var hx = (uint)(bits >> 32);
		var k = 0;
		if ( hx < 0x00100000 || (hx >> 31) != 0 )
		{
			if ( bits << 1 == 0 )
				return double.NegativeInfinity; // log(±0)
			if ( (hx >> 31) != 0 )
				return double.NaN; // log of a negative number
			// subnormal: scale up
			k -= 54;
			x *= 18014398509481984.0; // 0x1p54
			bits = BitConverter.DoubleToUInt64Bits( x );
			hx = (uint)(bits >> 32);
		}
		else if ( hx >= 0x7ff00000 )
			return double.IsNaN( x ) ? double.NaN : x;
		else if ( hx == 0x3ff00000 && bits << 32 == 0 )
			return 0;

		// reduce x into [√2/2, √2]
		hx += 0x3ff00000 - 0x3fe6a09e;
		k += (int)(hx >> 20) - 0x3ff;
		hx = (hx & 0x000fffff) + 0x3fe6a09e;
		x = BitConverter.UInt64BitsToDouble( ((ulong)hx << 32) | (bits & 0xffffffff) );

		var f = x - 1.0;
		var hfsq = 0.5 * f * f;
		var s = f / (2.0 + f);
		var z = s * s;
		var w = z * z;
		var t1 = w * (Lg2 + w * (Lg4 + w * Lg6));
		var t2 = z * (Lg1 + w * (Lg3 + w * (Lg5 + w * Lg7)));
		var r = t2 + t1;

		// hi + lo = f − hfsq + s·(hfsq + R) ≈ log(1 + f)
		var hi = f - hfsq;
		hi = BitConverter.UInt64BitsToDouble( BitConverter.DoubleToUInt64Bits( hi ) & (ulong.MaxValue << 32) );
		var lo = f - hi - hfsq + s * (hfsq + r);

		var valueHigh = hi * InvLn2High;
		var valueLow = (lo + hi) * InvLn2Low + lo * InvLn2High;

		double y = k;
		w = y + valueHigh;
		valueLow += (y - w) + valueHigh;
		valueHigh = w;
		return valueLow + valueHigh;
	}

	// ---- pow --------------------------------------------------------------------------------------------------------------

	private static readonly double[] Bp = { 1.0, 1.5 };
	private static readonly double[] DpHigh = { 0.0, 5.84962487220764160156e-01 };
	private static readonly double[] DpLow = { 0.0, 1.35003920212974897128e-08 };
	private const double Two53 = 9007199254740992.0, Huge = 1.0e300, Tiny = 1.0e-300,
		L1 = 5.99999999999994648725e-01, L2 = 4.28571428578550184252e-01, L3 = 3.33333329818377432918e-01, L4 = 2.72728123808534006489e-01,
		L5 = 2.30660745775561754067e-01, L6 = 2.06975017800338417784e-01, P1 = 1.66666666666666019037e-01, P2 = -2.77777777770155933842e-03,
		P3 = 6.61375632143793436117e-05, P4 = -1.65339022054652515390e-06, P5 = 4.13813679705723846039e-08, Lg2Full = 6.93147180559945286227e-01,
		Lg2High = 6.93147182464599609375e-01, Lg2Low = -1.90465429995776804525e-09, Ovt = 8.0085662595372944372e-017, Cp = 9.61796693925975554329e-01,
		CpHigh = 9.61796700954437255859e-01, CpLow = -7.02846165095275826516e-09, InvLn2 = 1.44269504088896338700e+00,
		InvLn2High24 = 1.44269502162933349609e+00, InvLn2Low24 = 1.92596299112661746887e-08;

	/// <summary>x^y (FreeBSD <c>e_pow.c</c>, musl <c>pow_small.c</c>).</summary>
	public static double Pow( double x, double y )
	{
		double z, ax, zHigh, zLow, pHigh, pLow, y1, t1, t2, r, s, t, u, v, w;
		int i, j, k, yIsInt, n;
		var hx = (int)High( x );
		var lx = Low( x );
		var hy = (int)High( y );
		var ly = Low( y );
		var ix = hx & 0x7fffffff;
		var iy = hy & 0x7fffffff;

		// x**0 = 1, even if x is NaN
		if ( (iy | (int)ly) == 0 )
			return 1.0;
		// 1**y = 1, even if y is NaN
		if ( hx == 0x3ff00000 && lx == 0 )
			return 1.0;
		if ( ix > 0x7ff00000 || (ix == 0x7ff00000 && lx != 0) || iy > 0x7ff00000 || (iy == 0x7ff00000 && ly != 0) )
			return double.NaN;

		// whether y is an odd (1) or even (2) integer when x < 0
		yIsInt = 0;
		if ( hx < 0 )
		{
			if ( iy >= 0x43400000 )
				yIsInt = 2;
			else if ( iy >= 0x3ff00000 )
			{
				k = (iy >> 20) - 0x3ff;
				if ( k > 20 )
				{
					var jl = ly >> (52 - k);
					if ( (jl << (52 - k)) == ly )
						yIsInt = 2 - (int)(jl & 1);
				}
				else if ( ly == 0 )
				{
					j = iy >> (20 - k);
					if ( (j << (20 - k)) == iy )
						yIsInt = 2 - (j & 1);
				}
			}
		}

		// special y
		if ( ly == 0 )
		{
			if ( iy == 0x7ff00000 )
			{
				// y is ±inf
				if ( ((ix - 0x3ff00000) | (int)lx) == 0 )
					return 1.0; // (-1)**±inf
				if ( ix >= 0x3ff00000 )
					return hy >= 0 ? y : 0.0;
				return hy >= 0 ? 0.0 : -y;
			}
			if ( iy == 0x3ff00000 )
				return hy >= 0 ? x : 1 / x; // y is ±1
			if ( hy == 0x40000000 )
				return x * x; // y is 2
			if ( hy == 0x3fe00000 && hx >= 0 )
				return Math.Sqrt( x ); // y is 0.5, x ≥ +0
		}

		ax = Math.Abs( x );
		// special x
		if ( lx == 0 && (ix == 0x7ff00000 || ix == 0 || ix == 0x3ff00000) )
		{
			// x is ±0, ±inf, ±1
			z = ax;
			if ( hy < 0 )
				z = 1.0 / z;
			if ( hx < 0 )
			{
				if ( ((ix - 0x3ff00000) | yIsInt) == 0 )
					return double.NaN; // (-1)**non-integer
				if ( yIsInt == 1 )
					z = -z;
			}
			return z;
		}

		s = 1.0;
		if ( hx < 0 )
		{
			if ( yIsInt == 0 )
				return double.NaN; // (x<0)**(non-integer)
			if ( yIsInt == 1 )
				s = -1.0;
		}

		if ( iy > 0x41e00000 )
		{
			// |y| > 2³¹
			if ( iy > 0x43f00000 )
			{
				// |y| > 2⁶⁴: overflow or underflow
				if ( ix <= 0x3fefffff )
					return hy < 0 ? Huge * Huge : Tiny * Tiny;
				if ( ix >= 0x3ff00000 )
					return hy > 0 ? Huge * Huge : Tiny * Tiny;
			}
			if ( ix < 0x3fefffff )
				return hy < 0 ? s * Huge * Huge : s * Tiny * Tiny;
			if ( ix > 0x3ff00000 )
				return hy > 0 ? s * Huge * Huge : s * Tiny * Tiny;
			// |1 − x| ≤ 2⁻²⁰: log(x) by x − x²/2 + x³/3 − x⁴/4
			t = ax - 1.0;
			w = (t * t) * (0.5 - t * (0.3333333333333333333333 - t * 0.25));
			u = InvLn2High24 * t;
			v = t * InvLn2Low24 - w * InvLn2;
			t1 = u + v;
			t1 = WithLow( t1, 0 );
			t2 = v - (t1 - u);
		}
		else
		{
			n = 0;
			if ( ix < 0x00100000 )
			{
				// subnormal x
				ax *= Two53;
				n -= 53;
				ix = (int)High( ax );
			}
			n += (ix >> 20) - 0x3ff;
			j = ix & 0x000fffff;
			ix = j | 0x3ff00000;
			if ( j <= 0x3988E )
				k = 0; // |x| < √(3/2)
			else if ( j < 0xBB67A )
				k = 1; // |x| < √3
			else
			{
				k = 0;
				n += 1;
				ix -= 0x00100000;
			}
			ax = WithHigh( ax, (uint)ix );

			// ss = s_h + s_l = (x − 1)/(x + 1) or (x − 1.5)/(x + 1.5)
			u = ax - Bp[k];
			v = 1.0 / (ax + Bp[k]);
			var ss = u * v;
			var sHigh = WithLow( ss, 0 );
			var tHigh = FromWords( (uint)(((ix >> 1) | 0x20000000) + 0x00080000 + (k << 18)), 0 );
			var tLow = ax - (tHigh - Bp[k]);
			var sLow = v * ((u - sHigh * tHigh) - sHigh * tLow);
			// log(ax)
			var s2 = ss * ss;
			r = s2 * s2 * (L1 + s2 * (L2 + s2 * (L3 + s2 * (L4 + s2 * (L5 + s2 * L6)))));
			r += sLow * (sHigh + ss);
			s2 = sHigh * sHigh;
			tHigh = 3.0 + s2 + r;
			tHigh = WithLow( tHigh, 0 );
			tLow = r - ((tHigh - 3.0) - s2);
			u = sHigh * tHigh;
			v = sLow * tHigh + tLow * ss;
			pHigh = u + v;
			pHigh = WithLow( pHigh, 0 );
			pLow = v - (pHigh - u);
			zHigh = CpHigh * pHigh;
			zLow = CpLow * pHigh + pLow * Cp + DpLow[k];
			t = n;
			t1 = ((zHigh + zLow) + DpHigh[k]) + t;
			t1 = WithLow( t1, 0 );
			t2 = zLow - (((t1 - t) - DpHigh[k]) - zHigh);
		}

		// (y1 + y2)·(t1 + t2)
		y1 = WithLow( y, 0 );
		pLow = (y - y1) * t1 + y * t2;
		pHigh = y1 * t1;
		z = pLow + pHigh;
		j = (int)High( z );
		i = (int)Low( z );
		if ( j >= 0x40900000 )
		{
			// z ≥ 1024
			if ( ((j - 0x40900000) | i) != 0 )
				return s * Huge * Huge;
			if ( pLow + Ovt > z - pHigh )
				return s * Huge * Huge;
		}
		else if ( (j & 0x7fffffff) >= 0x4090cc00 )
		{
			// z ≤ -1075
			if ( ((j - unchecked((int)0xc090cc00)) | i) != 0 )
				return s * Tiny * Tiny;
			if ( pLow <= z - pHigh )
				return s * Tiny * Tiny;
		}

		// 2**(p_h + p_l)
		i = j & 0x7fffffff;
		k = (i >> 20) - 0x3ff;
		n = 0;
		if ( i > 0x3fe00000 )
		{
			// |z| > 0.5: n = [z + 0.5]
			n = j + (0x00100000 >> (k + 1));
			k = ((n & 0x7fffffff) >> 20) - 0x3ff;
			t = FromWords( (uint)(n & ~(0x000fffff >> k)), 0 );
			n = ((n & 0x000fffff) | 0x00100000) >> (20 - k);
			if ( j < 0 )
				n = -n;
			pHigh -= t;
		}
		t = pLow + pHigh;
		t = WithLow( t, 0 );
		u = t * Lg2High;
		v = (pLow - (t - pHigh)) * Lg2Full + t * Lg2Low;
		z = u + v;
		w = v - (z - u);
		t = z * z;
		t1 = z - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
		r = (z * t1) / (t1 - 2.0) - (w + z * w);
		z = 1.0 - (r - z);
		j = (int)High( z );
		j += n << 20;
		if ( (j >> 20) <= 0 )
			z = ScaleB( z, n ); // subnormal result
		else
			z = WithHigh( z, (uint)j );
		return s * z;
	}
}

/// <summary>
/// The single-precision counterparts of <see cref="DetMath"/> for code that used <see cref="MathF"/>: each evaluates the double routine and
/// rounds once to single, so they are as deterministic as <see cref="DetMath"/> and agree with a correctly rounded single result except
/// in the rare cases where the double result lies within its last unit of a single rounding boundary.
/// </summary>
public static class DetMathF
{
	/// <summary>sin x.</summary>
	public static float Sin( float x ) => (float)DetMath.Sin( x );

	/// <summary>cos x.</summary>
	public static float Cos( float x ) => (float)DetMath.Cos( x );

	/// <summary>(sin x, cos x).</summary>
	public static (float Sin, float Cos) SinCos( float x ) => (Sin( x ), Cos( x ));

	/// <summary>atan2(y, x).</summary>
	public static float Atan2( float y, float x ) => (float)DetMath.Atan2( y, x );

	/// <summary>acos x.</summary>
	public static float Acos( float x ) => (float)DetMath.Acos( x );
}
