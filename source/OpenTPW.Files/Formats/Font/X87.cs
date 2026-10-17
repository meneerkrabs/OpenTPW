using System.Numerics;

namespace OpenTPW;

/// <summary>
/// The x87 precision control (bits 8-9 of the control word) the sign generator's and the wavelet decoder's floating-point code runs at: 24, 53 or 64
/// mantissa bits. tp.exe's thread runs at 24: the CRT start-up sets 53 (0x6804D0, <c>_controlfp(_PC_53, _MCW_PC)</c>), then
/// DirectDraw <c>SetCooperativeLevel</c> with DDSCL_FPUSETUP (0x563914: 0x851, 0x563B92: 0x808) makes the shipped DirectX 6.1
/// <c>D3DIM.DLL</c> CreateDevice set single precision and leave it (CW 0x007F), and the only later control-word writer, the
/// render function 0x576EC0, restores the word it found. The wavelet codec (0x564B20) is registered by the engine init 0x572780, which
/// runs after the display init and the Direct3D device start (0x5836D0 -> 0x572C40 -> 0x573080) in 0x44DF20, and the sign generator builds its own decoder
/// object (0x5EBFC0) later still, so every wavelet decode runs at 24 bits; only the D4 coefficient initialisers (0x591600..0x591690, in the CRT's C++ initialiser
/// table) run earlier, at 53.
/// </summary>
// [BIN:TPW-EXE:0x00576EC0 control word] the only code that rewrites the x87 control word besides the CRT and rounding-mode wrappers (_ftol 0x67A830 saves and restores the word): 0x591220 forces PC=24 and returns the old word, 0x591240 restores it (0x591250 would restore it with PC=64, only when bit 1 of [0x8BD508] is set, which no instruction sets); the sign generator is not reached from that function
// [BIN:D3DIM-DLL:0x56664F87 CreateDevice FPU setup] with the DDSCL_FPUSETUP flag (DDRAW-DLL 0xBAABE771 maps 0x800 to local flag 0x8000) a class-2 device sets CW = (CW & 0xFCFF | 0x3F) & 0xF3FF and does not restore it; the class is chosen at 0x56664DD8..0x56664EEA: a HAL device is class 2 only when the driver reports DrawPrimitives2 (the callbacks at global +0x638, field +0x14), else class 1 or 0 (CW untouched, the CRT's 53 bits; ForceDouble, default on, only raises PC to 53 inside D3D calls); every other device is class 2 unless the registry values DisableDP or DisableDP2 under HKLM\\Software\\Microsoft\\Direct3D are set; tp.exe names only the HAL device GUID (no reference to the RGB or MMX GUIDs), so on a driver without DrawPrimitives2 the original would have run at 53 bits
internal static class X87Precision
{
	/// <summary>The mantissa bits the original's thread ran at while a sign was composed.</summary>
	public const int Original = 24;

	[ThreadStatic] private static int bits;

	/// <summary>The mantissa bits the current thread rounds at (24, 53 or 64).</summary>
	public static int Bits => bits == 0 ? Original : bits;

	/// <summary>Runs <paramref name="action"/> with every emulated x87 operation on this thread rounding to <paramref name="mantissaBits"/> bits (tests).</summary>
	public static T With<T>( int mantissaBits, Func<T> action )
	{
		if ( mantissaBits is not (24 or 53 or 64) )
			throw new ArgumentOutOfRangeException( nameof( mantissaBits ) );
		var previous = bits;
		bits = mantissaBits;
		try { return action(); }
		finally { bits = previous; }
	}
}

/// <summary>
/// One x87 register value: sign, a 64-bit normalised mantissa and an unbounded exponent (value = mantissa * 2^exponent, zero has
/// mantissa 0). Add, subtract, multiply, divide and square root round to nearest-even at the current precision control, as the
/// FADD, FSUB, FMUL, FDIV and FSQRT instructions do; loads are exact and stores to float or double round to that format.
/// The transcendental instructions (FSIN, FCOS, F2XM1, FYL2X) are not affected by the precision control on the x87 and are
/// computed here in double precision, which is exact to within the last bits of the extended result.
/// </summary>
internal readonly struct Ext
{
	private readonly ulong mantissa;
	private readonly int exponent;
	private readonly bool negative;

	private Ext( bool negative, ulong mantissa, int exponent )
	{
		this.negative = negative;
		this.mantissa = mantissa;
		this.exponent = mantissa == 0 ? 0 : exponent;
	}

	public static Ext Zero => default;

	/// <summary>FLDL2E: log2(e) as the extended constant the instruction loads (mantissa 0xB8AA3B295C17F0BC).</summary>
	public static Ext Log2E => new( false, 0xB8AA3B295C17F0BCUL, -63 );

	public static Ext One => FromInt( 1 );
	public bool IsZero => mantissa == 0;
	public bool IsPositive => mantissa != 0 && !negative;
	public bool IsNegative => mantissa != 0 && negative;

	public static Ext FromInt( long value ) => value == 0 ? Zero : Make( value < 0, (UInt128)(ulong)(value < 0 ? -value : value), 0, false, 64 );

	public static Ext FromFloat( float value ) => FromDouble( value );

	public static Ext FromDouble( double value )
	{
		if ( !double.IsFinite( value ) )
			throw new NotSupportedException( "Non-finite values are not supported by the x87 sign arithmetic." );
		if ( value == 0 )
			return Zero;
		var bitsOf = BitConverter.DoubleToInt64Bits( value );
		var biased = (int)((bitsOf >> 52) & 0x7FF);
		var fraction = (ulong)bitsOf & 0xFFFFFFFFFFFFFUL;
		if ( biased == 0 )
			return Make( bitsOf < 0, fraction, -1074, false, 64 );
		return Make( bitsOf < 0, fraction | (1UL << 52), biased - 1075, false, 64 );
	}

	public Ext Negate() => IsZero ? this : new Ext( !negative, mantissa, exponent );

	/// <summary>The value rounded to <paramref name="bits"/> mantissa bits (a store to float or double and a reload).</summary>
	public Ext RoundTo( int bits ) => IsZero ? this : Make( negative, mantissa, exponent, false, bits );

	/// <summary>FST/FSTP to a 32-bit float, reloaded: the value as a float held at full register width.</summary>
	public Ext AsFloat() => IsZero ? this : Make( negative, mantissa, exponent, false, 24 );

	public float ToFloat() => (float)ToDouble( 24 );

	public double ToDouble( int bits = 53 )
	{
		if ( IsZero )
			return 0.0;
		var rounded = Make( negative, mantissa, exponent, false, Math.Min( bits, 53 ) );
		var value = Math.ScaleB( (double)(rounded.mantissa >> 11), rounded.exponent + 11 );
		return rounded.negative ? -value : value;
	}

	/// <summary>FISTP with truncation toward zero (the <c>_ftol</c> wrapper sets the rounding mode); results beyond 63 bits are not supported.</summary>
	public long Truncate()
	{
		if ( IsZero )
			return 0;
		long magnitude;
		if ( exponent >= 0 )
			magnitude = exponent > 0 ? long.MaxValue : (long)mantissa;
		else if ( exponent <= -64 )
			magnitude = 0;
		else
			magnitude = (long)(mantissa >> -exponent);
		return negative ? -magnitude : magnitude;
	}

	/// <summary>The value times 2^<paramref name="power"/> (FSCALE); exact.</summary>
	public Ext Scale( int power ) => IsZero ? this : new Ext( negative, mantissa, exponent + power );

	/// <summary>FRNDINT with round-to-nearest-even.</summary>
	public Ext RoundToInteger()
	{
		if ( IsZero || exponent >= 0 )
			return this;
		if ( exponent <= -66 )
			return Zero;
		var shift = -exponent;
		var whole = shift >= 64 ? 0UL : mantissa >> shift;
		var remainder = shift >= 64 ? mantissa : mantissa & ((1UL << shift) - 1);
		var half = 1UL << (shift - 1);
		if ( remainder > half || (remainder == half && (whole & 1) == 1) )
			whole++;
		return whole == 0 ? Zero : Make( negative, whole, 0, false, 64 );
	}

	public int CompareTo( Ext other )
	{
		if ( IsZero && other.IsZero ) return 0;
		if ( IsZero ) return other.negative ? 1 : -1;
		if ( other.IsZero ) return negative ? -1 : 1;
		if ( negative != other.negative ) return negative ? -1 : 1;
		var magnitude = exponent != other.exponent ? exponent.CompareTo( other.exponent ) : mantissa.CompareTo( other.mantissa );
		return negative ? -magnitude : magnitude;
	}

	/// <summary>FADD / FADDP at the current precision control.</summary>
	public static Ext operator +( Ext a, Ext b ) => Add( a, b, X87Precision.Bits );

	/// <summary>FSUB / FSUBP at the current precision control.</summary>
	public static Ext operator -( Ext a, Ext b ) => Add( a, b.Negate(), X87Precision.Bits );

	/// <summary>FMUL / FMULP at the current precision control.</summary>
	public static Ext operator *( Ext a, Ext b )
	{
		if ( a.IsZero || b.IsZero )
			return Zero;
		var product = (UInt128)a.mantissa * b.mantissa;
		return Make( a.negative != b.negative, product, a.exponent + b.exponent, false, X87Precision.Bits );
	}

	/// <summary>FDIV / FDIVR at the current precision control.</summary>
	public static Ext operator /( Ext a, Ext b )
	{
		if ( b.IsZero )
			throw new DivideByZeroException();
		if ( a.IsZero )
			return Zero;
		var numerator = (UInt128)a.mantissa << 62;
		var high = numerator / b.mantissa;
		var rest = (UInt128)(numerator % b.mantissa) << 64;
		var low = rest / b.mantissa;
		var quotient = (high << 64) | low;
		return Make( a.negative != b.negative, quotient, a.exponent - b.exponent - 126, (rest % b.mantissa) != 0, X87Precision.Bits );
	}

	/// <summary>FSQRT at the current precision control.</summary>
	public static Ext Sqrt( Ext a )
	{
		if ( a.IsNegative )
			throw new NotSupportedException( "Square root of a negative value." );
		if ( a.IsZero )
			return Zero;
		var shift = ((a.exponent - 80) & 1) == 0 ? 80 : 81;
		var radicand = (BigInteger)a.mantissa << shift;
		var root = IntegerSquareRoot( radicand );
		var exact = root * root == radicand;
		return Make( false, (UInt128)root, (a.exponent - shift) / 2, !exact, X87Precision.Bits );
	}

	public static Ext Sin( Ext a ) => FromDouble( Math.Sin( a.ToDouble() ) );
	public static Ext Cos( Ext a ) => FromDouble( Math.Cos( a.ToDouble() ) );

	/// <summary>F2XM1: 2^x - 1 for |x| &lt;= 1.</summary>
	public static Ext Exp2Minus1( Ext a )
	{
		var x = a.ToDouble();
		var y = x * Math.Log( 2.0 );
		return FromDouble( Math.Abs( y ) < 1e-3 ? y * (1 + y * (0.5 + y * (1.0 / 6 + y * (1.0 / 24 + y / 120)))) : Math.Pow( 2.0, x ) - 1.0 );
	}

	/// <summary>FYL2X: y * log2(x) for a positive x.</summary>
	public static Ext Log2Times( Ext y, Ext x ) => FromDouble( y.ToDouble() * Math.Log2( x.ToDouble() ) );

	/// <summary>
	/// The CRT <c>pow</c> (0x67C5D0 into 0x67C5F2, which keeps the caller's precision control): FYL2X, then 2^z as
	/// FRNDINT, FSUB (the fraction), F2XM1, FLD1, FADDP, FSCALE, so the sum with 1 and the fraction round at the precision control.
	/// </summary>
	// [BIN:TPW-EXE:0x0067C5F2 pow] positive finite base: FYL2X (0x67C63F), then 0x686170: fld st0; frndint; fsubr st1,st0; fxch; fchs; f2xm1; fld1; faddp; fscale; fstp st1; the control word is only changed when it is not 0x27F, by 0x686185 which keeps bits 8-9 and sets the exception masks
	public static Ext Pow( Ext x, Ext y )
	{
		var z = Log2Times( y, x );
		var whole = z.RoundToInteger();
		var fraction = z - whole;
		var power = One + Exp2Minus1( fraction );
		return power.Scale( (int)whole.Truncate() );
	}

	private static Ext Add( Ext a, Ext b, int precision )
	{
		if ( a.IsZero && b.IsZero )
			return Zero;
		if ( a.IsZero )
			return Make( b.negative, b.mantissa, b.exponent, false, precision );
		if ( b.IsZero )
			return Make( a.negative, a.mantissa, a.exponent, false, precision );
		var big = a;
		var small = b;
		if ( a.exponent < b.exponent || (a.exponent == b.exponent && a.mantissa < b.mantissa) )
		{
			big = b;
			small = a;
		}
		var distance = big.exponent - small.exponent;
		var bigMagnitude = (UInt128)big.mantissa << 62;
		var smallMagnitude = (UInt128)small.mantissa << 62;
		var sticky = false;
		if ( distance >= 126 )
		{
			sticky = true;
			smallMagnitude = 0;
		}
		else if ( distance > 0 )
		{
			sticky = (smallMagnitude & (((UInt128)1 << distance) - 1)) != 0;
			smallMagnitude >>= distance;
		}
		var exponent = big.exponent - 62;
		if ( big.negative == small.negative )
			return Make( big.negative, bigMagnitude + smallMagnitude, exponent, sticky, precision );
		var difference = bigMagnitude - smallMagnitude - (sticky ? (UInt128)1 : 0);
		if ( difference == 0 && !sticky )
			return Zero;
		return Make( big.negative, difference, exponent, sticky, precision );
	}

	/// <summary>Rounds (magnitude + a sticky fraction below one unit) * 2^exponent to <paramref name="precision"/> bits, to nearest even.</summary>
	private static Ext Make( bool negative, UInt128 magnitude, int exponent, bool sticky, int precision )
	{
		if ( magnitude == 0 )
			return Zero;
		var top = 127 - (int)UInt128.LeadingZeroCount( magnitude );
		var shift = top - (precision - 1);
		UInt128 kept;
		if ( shift > 0 )
		{
			var mask = ((UInt128)1 << shift) - 1;
			var dropped = magnitude & mask;
			kept = magnitude >> shift;
			var half = (UInt128)1 << (shift - 1);
			if ( dropped > half || (dropped == half && (sticky || (kept & 1) == 1)) )
			{
				kept++;
				if ( kept >> precision != 0 )
				{
					kept >>= 1;
					shift++;
				}
			}
		}
		else
		{
			kept = magnitude << -shift;
		}
		return new Ext( negative, (ulong)(kept << (64 - precision)), exponent + shift - (64 - precision) );
	}

	private static BigInteger IntegerSquareRoot( BigInteger value )
	{
		var guess = BigInteger.One << (int)((value.GetBitLength() + 1) / 2);
		while ( true )
		{
			var next = (guess + value / guess) >> 1;
			if ( next >= guess )
				return guess;
			guess = next;
		}
	}

	/// <summary>
	/// One Gaussian tap sequence of the sign blurs (0x5EA8A0 and 0x5EB3E0): <c>2^(log2(e) * (d² * -0.5 / s²)) / (s² * 6.2831853)</c>
	/// for every squared distance, each tap stored as a double, then every stored tap divided by their register-width sum and
	/// stored as a double again. The taps come back as reloaded doubles.
	/// </summary>
	// [BIN:TPW-EXE:0x005EA8A0 kernel arithmetic] s² = FMUL, A = s² * 6.2831853 (0x702F28); per tap FILD d², FMUL -0.5 (0x702F30), FDIV by s², FLDL2E, FMULP, then FLD st0; FRNDINT; FXCH; FSUB (the fraction); F2XM1; FLD1; FADDP; FSCALE, FDIV by A, FADD into the running sum, FSTP double; a second loop FLD double, FDIV by the sum, FSTP double
	public static Ext[] GaussianTaps( float sigma, IReadOnlyList<int> squaredDistances )
	{
		var s = FromFloat( sigma );
		var squared = s * s;
		var divisor = squared * FromDouble( 6.2831853 );
		var minusHalf = FromDouble( -0.5 );
		var taps = new double[squaredDistances.Count];
		var sum = Zero;
		for ( var i = 0; i < taps.Length; i++ )
		{
			var x = (FromInt( squaredDistances[i] ) * minusHalf / squared) * Log2E;
			var whole = x.RoundToInteger();
			var weight = (One + Exp2Minus1( x - whole )).Scale( (int)whole.Truncate() );
			var tap = weight / divisor;
			sum += tap;
			taps[i] = tap.ToDouble();
		}
		var result = new Ext[taps.Length];
		for ( var i = 0; i < taps.Length; i++ )
			result[i] = FromDouble( (FromDouble( taps[i] ) / sum).ToDouble() );
		return result;
	}
}
