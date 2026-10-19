namespace OpenTPW;

/// <summary>
/// The height map a sign relief effect shades: the glyph coverage reduced to binary ink, eroded one pixel at a time for as many
/// steps as the effect record's mask word says (every pixel removed in step <c>i</c> keeps a smoothstep level, the core stays
/// 255), then blurred by the record's first parameter where the glyphs have coverage (tp.exe 0x5EB820: 0x5EB270 with the
/// erosion step 0x5EA1F0, then the 2-D Gaussian 0x5EB3E0). The relief normals are built from this map, not from the glyph
/// coverage itself. Every map is one byte per pixel, row-major, <c>width</c> x <c>height</c>.
/// </summary>
// [BIN:TPW-EXE:0x005EB820 effect shape] with glyph coverage and a source strip present the effect builds a shape bitmap (0x5EB270 from the mask word at +0), blurs it with the float at +8 when that is above 0.0 (fcomp against 0x702F20 and a skip on C0 or C3, so NaN also skips; 0x5EB3E0), then shades (0x5EB8A0)
public static class SignEffectShape
{
	/// <summary>Largest blur parameter accepted (a kernel of about 53 taps a side); the shipped signs stay near 4.</summary>
	public const float MaximumSigma = 20f;

	/// <summary>The shape of the glyph coverage <paramref name="coverage"/> for an effect record's mask word and blur parameter (a new array).</summary>
	public static byte[] Build( byte[] coverage, int width, int height, uint iterations, float sigma )
	{
		ArgumentNullException.ThrowIfNull( coverage );
		if ( width <= 0 || height <= 0 || coverage.Length != width * height )
			throw new ArgumentException( "Mask dimensions do not match its length.", nameof( coverage ) );
		var shape = Bevel( coverage, width, height, iterations );
		// [BIN:TPW-EXE:0x005EB86F blur test] fld of the float at +8 and fcomp with 0.0 (0x702F20), then a skip when C0 or C3 is set: the blur runs only for a parameter above 0.0
		if ( sigma > 0f )
			Blur( shape, coverage, width, height, sigma );
		return shape;
	}

	/// <summary>The binary ink eroded <paramref name="iterations"/> times, each pixel keeping the level of the step that removed it.</summary>
	// [BIN:TPW-EXE:0x005EB270 bevel levels] ink = 255 where the coverage is not 0 (neg/sbb at 0x5EB2D1), the output starts at 0; for step i of N the level is _ftol(256 * (2 u^3 - 3 u^2)) with u = 1 - i / N (0x702F40 = 1.0, 0x702F48 = 3.0, 0x702F4C = 256.0), kept as its low byte; the surviving ink is OR-ed into the output at the end (0x5EB397)
	public static byte[] Bevel( byte[] coverage, int width, int height, uint iterations )
	{
		var shape = new byte[coverage.Length];
		var ink = new byte[coverage.Length];
		for ( var i = 0; i < coverage.Length; i++ )
			ink[i] = coverage[i] != 0 ? (byte)255 : (byte)0;
		var next = new byte[coverage.Length];
		for ( uint step = 0; step < iterations; step++ )
		{
			// [BIN:TPW-EXE:0x005EB319 level arithmetic] N is stored as a float (FILD qword, FSTP float), t = FILD(i) / N (FDIV float), u = 1.0 - t, then u³ = u * (u * u), 2u³ - 3u² (FMUL 3.0, FADD st0,st0, FSUBP), times 256.0 and _ftol
			var u = Ext.One - Ext.FromInt( step ) / Ext.FromInt( iterations ).AsFloat();
			var u2 = u * u;
			var cubed = u * u2;
			var level = unchecked( (byte)((cubed + cubed - u2 * Ext.FromFloat( 3f )) * Ext.FromFloat( 256f )).Truncate() );
			if ( !Erode( ink, next, shape, width, height, level ) )
				break;
			(ink, next) = (next, ink);
		}
		for ( var i = 0; i < shape.Length; i++ )
			shape[i] |= ink[i];
		return shape;
	}

	/// <summary>One erosion step: <paramref name="next"/> takes the minimum of each pixel and its neighbours, and a pixel that lost ink gets <paramref name="level"/> in <paramref name="shape"/>. Returns whether any pixel changed.</summary>
	// [BIN:TPW-EXE:0x005EA1F0 erosion step] the minimum runs over the pixel and its 8 neighbours, but the rows above need y > 0, the rows below y < h - 2, and the right-hand column x < w - 2 (the last two columns never look right, the last two rows never look down); the left column needs x > 0; the result goes to the second byte of a two-byte pixel and is copied back (0x5EA3BD) after the pass, and the output takes the level where the minimum differs from the pixel
	private static bool Erode( byte[] ink, byte[] next, byte[] shape, int width, int height, byte level )
	{
		var changed = false;
		for ( var y = 0; y < height; y++ )
		for ( var x = 0; x < width; x++ )
		{
			var at = y * width + x;
			var value = ink[at];
			var minimum = value;
			if ( y > 0 )
			{
				if ( x > 0 ) minimum = Math.Min( minimum, ink[at - width - 1] );
				minimum = Math.Min( minimum, ink[at - width] );
				if ( x < width - 2 ) minimum = Math.Min( minimum, ink[at - width + 1] );
			}
			if ( x > 0 ) minimum = Math.Min( minimum, ink[at - 1] );
			if ( x < width - 2 ) minimum = Math.Min( minimum, ink[at + 1] );
			if ( y < height - 2 )
			{
				if ( x > 0 ) minimum = Math.Min( minimum, ink[at + width - 1] );
				minimum = Math.Min( minimum, ink[at + width] );
				if ( x < width - 2 ) minimum = Math.Min( minimum, ink[at + width + 1] );
			}
			next[at] = minimum;
			if ( minimum != value )
			{
				shape[at] = level;
				changed = true;
			}
		}
		return changed;
	}

	/// <summary>
	/// The square Gaussian of the shape blur: <c>3 + trunc(2.5 sigma)</c> taps a side, centred on tap <c>(n - 1) / 2</c>, each
	/// <c>exp(-0.5 (dx² + dy²) / sigma²) / (2 pi sigma²)</c> divided by the sum of all taps; row-major.
	/// </summary>
	// [BIN:TPW-EXE:0x005EB3E0 shape kernel] n = 3 - _ftol(sigma * -2.5) (0x702F38); tap (a, b) = 2^(log2(e) * ((a - c)² + (b - c)²) * -0.5 / sigma²) / (sigma² * 6.2831853) stored as double, then every tap divided by their sum; c = (n - 1) / 2
	public static double[] Kernel( float sigma, out int size ) => Array.ConvertAll( KernelTaps( sigma, out size ).Taps, tap => tap.ToDouble() );

	private static ScaledTaps KernelTaps( float sigma, out int size )
	{
		var s = (double)sigma;
		if ( !double.IsFinite( s ) || Math.Abs( s ) > MaximumSigma )
			throw new NotSupportedException( "Sign shape blur parameter is not supported by the compositor." );
		size = 3 - (int)(Ext.FromFloat( sigma ) * Ext.FromDouble( -2.5 )).Truncate();
		if ( size <= 0 )
		{
			size = 0;
			return new ScaledTaps( Array.Empty<Ext>() );
		}
		var centre = (size - 1) / 2;
		var distances = new int[size * size];
		for ( var a = 0; a < size; a++ )
		for ( var b = 0; b < size; b++ )
			distances[a * size + b] = (a - centre) * (a - centre) + (b - centre) * (b - centre);
		// the taps depend on the sigma and the precision only: made once per pair
		var key = (BitConverter.SingleToInt32Bits( sigma ), X87Precision.Bits);
		lock ( kernelCache )
			if ( kernelCache.TryGetValue( key, out var cached ) )
				return cached;
		var taps = new ScaledTaps( Ext.GaussianTaps( sigma, distances ) );
		lock ( kernelCache )
			kernelCache[key] = taps;
		return taps;
	}

	private static readonly Dictionary<(int Sigma, int Precision), ScaledTaps> kernelCache = new();

	/// <summary>
	/// Blurs <paramref name="shape"/> in place where <paramref name="coverage"/> is not 0, from the shape as it was before the
	/// blur (rows above the current one are kept as copies, rows below are read live), with coordinates clamped to the nearest
	/// edge; each sum is truncated to a byte.
	/// </summary>
	// [BIN:TPW-EXE:0x005EB5D3..0x005EB6EC shape convolution] the rows of the window come from a ring: the rows up to the current one are copies taken before they were written (the first row repeated above the top), the rows below point into the live shape (clamped to the last row); a column index is clamped to 0..w-1 (0x5EB688); taps are summed from 0.0 in row-major kernel order and truncated with _ftol (0x5EB6E3), stored as the low byte, only for pixels whose coverage byte is not 0
	public static void Blur( byte[] shape, byte[] coverage, int width, int height, float sigma )
	{
		var kernel = KernelTaps( sigma, out var size );
		var centre = (size - 1) / 2;
		var rowsBelow = (size + 2) / 2 - 1;
		var original = (byte[])shape.Clone();
		for ( var y = 0; y < height; y++ )
		for ( var x = 0; x < width; x++ )
		{
			if ( coverage[y * width + x] == 0 )
				continue;
			var sum = Ext.Zero;
			var tap = 0;
			for ( var ky = -centre; ky <= rowsBelow; ky++ )
			{
				var row = Math.Clamp( y + ky, 0, height - 1 );
				// rows after the current one are live: they differ from the original only on the last row, where the clamp lands on the row being written
				var source = ky > 0 ? shape : original;
				for ( var kx = -centre; kx <= rowsBelow; kx++ )
				{
					// a zero byte adds a zero product, which leaves a sum that is already rounded as it was
					var value = source[row * width + Math.Clamp( x + kx, 0, width - 1 )];
					if ( value != 0 )
						sum += kernel.For( value )[tap];
					tap++;
				}
			}
			shape[y * width + x] = unchecked( (byte)sum.Truncate() );
		}
	}
}
