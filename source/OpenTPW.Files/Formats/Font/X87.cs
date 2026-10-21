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
/// A blur kernel's taps with their products by byte value, made when a value first meets the kernel: a sign mask holds a handful of distinct bytes, so
/// each tap's product with the byte is rounded once instead of at every pixel (<c>FromByte(value) * tap</c>, exactly, at the precision the table was made for).
/// </summary>
internal sealed class ScaledTaps
{
	private readonly Ext[] taps;
	private readonly Ext[]?[] byValue = new Ext[256][];

	public ScaledTaps( Ext[] taps ) => this.taps = taps;

	public Ext[] Taps => taps;

	/// <summary>The products of every tap with <paramref name="value"/> (not for 0, whose products are all zero).</summary>
	public Ext[] For( byte value )
	{
		var products = Volatile.Read( ref byValue[value] );
		if ( products != null )
			return products;
		var scaled = Ext.FromByte( value );
		products = new Ext[taps.Length];
		for ( var i = 0; i < products.Length; i++ )
			products[i] = scaled * taps[i];
		// A mask with many distinct bytes (smooth glyph edges) would keep a table of every tap for each of them: only the first few are kept (memory is bounded).
		// Signs may compose on several threads: a slot is claimed by an atomic count, and a table is published whole (a reader sees null or a complete one; two
		// threads that make the same value's table make equal ones, and the one kept does not matter).
		if ( Volatile.Read( ref kept ) < MaximumKept && Interlocked.Increment( ref kept ) <= MaximumKept )
			Volatile.Write( ref byValue[value], products );
		return products;
	}

	private const int MaximumKept = 64;
	private int kept;
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
		// the 128-bit product as two words (UInt128 arithmetic is slow in the browser's AOT build, docs/BROWSER-PERFORMANCE.md "Event hitches")
		var high = Math.BigMul( a.mantissa, b.mantissa, out var low );
		return Make( a.negative != b.negative, high, low, a.exponent + b.exponent, false, X87Precision.Bits );
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
		// the two mantissas as 126-bit magnitudes in word pairs (high, low): mantissa << 62
		var bigHigh = big.mantissa >> 2;
		var bigLow = big.mantissa << 62;
		var smallHigh = small.mantissa >> 2;
		var smallLow = small.mantissa << 62;
		var sticky = false;
		if ( distance >= 126 )
		{
			sticky = true;
			smallHigh = smallLow = 0;
		}
		else if ( distance >= 64 )
		{
			var extra = distance - 64;
			sticky = smallLow != 0 || (smallHigh & ((1UL << extra) - 1)) != 0;
			smallLow = smallHigh >> extra;
			smallHigh = 0;
		}
		else if ( distance > 0 )
		{
			sticky = (smallLow & ((1UL << distance) - 1)) != 0;
			smallLow = (smallLow >> distance) | (smallHigh << (64 - distance));
			smallHigh >>= distance;
		}
		var exponent = big.exponent - 62;
		if ( big.negative == small.negative )
		{
			var low = bigLow + smallLow;
			var high = bigHigh + smallHigh + (low < bigLow ? 1UL : 0UL);
			return Make( big.negative, high, low, exponent, sticky, precision );
		}
		var differenceLow = bigLow - smallLow;
		var differenceHigh = bigHigh - smallHigh - (bigLow < smallLow ? 1UL : 0UL);
		if ( sticky )
		{
			if ( differenceLow == 0 )
				differenceHigh--;
			differenceLow--;
		}
		if ( differenceHigh == 0 && differenceLow == 0 && !sticky )
			return Zero;
		return Make( big.negative, differenceHigh, differenceLow, exponent, sticky, precision );
	}

	/// <summary>Rounds (magnitude + a sticky fraction below one unit) * 2^exponent to <paramref name="precision"/> bits, to nearest even.</summary>
	private static Ext Make( bool negative, UInt128 magnitude, int exponent, bool sticky, int precision ) =>
		Make( negative, (ulong)(magnitude >> 64), (ulong)magnitude, exponent, sticky, precision );

	/// <summary>
	/// <see cref="Make(bool, UInt128, int, bool, int)"/> with the magnitude as two words, <c>high:low</c>. The same rounding without 128-bit
	/// operators: <see cref="UInt128"/> shifts and comparisons cost most of a sign's composition in the browser's AOT build (each blur tap is one
	/// multiply and one add, each rounded here); <c>X87FastPathTests</c> compare it with the 128-bit form on random values.
	/// </summary>
	private static Ext Make( bool negative, ulong high, ulong low, int exponent, bool sticky, int precision )
	{
		if ( (high | low) == 0 )
			return Zero;
		var top = high != 0 ? 127 - BitOperations.LeadingZeroCount( high ) : 63 - BitOperations.LeadingZeroCount( low );
		var shift = top - (precision - 1);
		ulong kept;
		if ( shift > 0 )
		{
			ulong droppedHigh, droppedLow, halfHigh, halfLow;
			if ( shift >= 64 )
			{
				var extra = shift - 64;
				kept = high >> extra;
				droppedHigh = high & ((1UL << extra) - 1);
				droppedLow = low;
				(halfHigh, halfLow) = extra == 0 ? (0UL, 1UL << 63) : (1UL << (extra - 1), 0UL);
			}
			else
			{
				kept = (low >> shift) | (high << (64 - shift));
				droppedHigh = 0;
				droppedLow = low & ((1UL << shift) - 1);
				(halfHigh, halfLow) = (0UL, 1UL << (shift - 1));
			}
			var above = droppedHigh > halfHigh || (droppedHigh == halfHigh && droppedLow > halfLow);
			var tie = droppedHigh == halfHigh && droppedLow == halfLow;
			if ( above || (tie && (sticky || (kept & 1) == 1)) )
			{
				kept++;
				if ( precision == 64 ? kept == 0 : kept >> precision != 0 )
				{
					kept = precision == 64 ? 1UL << 63 : kept >> 1;
					shift++;
				}
			}
		}
		else
		{
			kept = low << -shift;
		}
		return new Ext( negative, kept << (64 - precision), exponent + shift - (64 - precision) );
	}

	/// <summary>The extended values 0..255 (a byte's <see cref="FromInt"/>), made once.</summary>
	private static readonly Ext[] ByteValues = Enumerable.Range( 0, 256 ).Select( value => FromInt( value ) ).ToArray();

	/// <summary><see cref="FromInt"/> of a byte, from a table.</summary>
	public static Ext FromByte( byte value ) => ByteValues[value];

	/// <summary>The sign, mantissa and exponent as stored (tests).</summary>
	internal (bool Negative, ulong Mantissa, int Exponent) Parts => (negative, mantissa, exponent);

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
