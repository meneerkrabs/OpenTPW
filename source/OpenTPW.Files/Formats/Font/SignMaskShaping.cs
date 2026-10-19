namespace OpenTPW;

/// <summary>
/// The shaping of a sign paint mask before it is coloured (tp.exe 0x5EBF10, the PC twin of the Mac 0xa9178): the glyph
/// coverage is copied at the paint record's offsets, grown or shrunk by its mask word, then blurred by its parameter.
/// Every mask is one byte per pixel, row-major, <c>width</c> x <c>height</c>.
/// </summary>
// [BIN:TPW-EXE:0x005EBF10 sign paint mask] shift by the record's offsets +0xC/+0x10 (0x5EA3F0), then a mask word +4 below zero erodes by its negation (0x5EA6D0) and above zero dilates by it (0x5EA510), then a parameter +8 that is not 0.0 blurs (0x5EA8A0); called with the paint records at layer +0x430 and +0x444 (0x5ECC76, 0x5ECCA7)
public static class SignMaskShaping
{
	/// <summary>Largest blur parameter accepted (a kernel of about 2,500 taps, ten times the mask's width); the shipped signs stay far below it.</summary>
	public const float MaximumSigma = 1000f;

	/// <summary>The paint mask of <paramref name="paint"/> made from the glyph coverage <paramref name="mask"/> (a new array; the input is unchanged).</summary>
	public static byte[] Shape( byte[] mask, int width, int height, SignPaint paint )
	{
		ArgumentNullException.ThrowIfNull( mask );
		ArgumentNullException.ThrowIfNull( paint );
		if ( width <= 0 || height <= 0 || mask.Length != width * height )
			throw new ArgumentException( "Mask dimensions do not match its length.", nameof( mask ) );
		var shaped = Shift( mask, width, height, paint.OffsetX, paint.OffsetY );
		if ( paint.MaskWord < 0 )
			Erode( shaped, width, height, -paint.MaskWord );
		else if ( paint.MaskWord > 0 )
			Dilate( shaped, width, height, paint.MaskWord );
		// [BIN:TPW-EXE:0x005EBF80 blur test] fcomp of the float at +8 with 0.0 and a skip on C3: the blur runs unless the parameter is 0.0 or unordered (NaN)
		if ( paint.MaskParameter != 0f && !float.IsNaN( paint.MaskParameter ) )
			Blur( shaped, width, height, paint.MaskParameter );
		return shaped;
	}

	/// <summary>The mask translated by (dx, dy) pixels (right and down are positive); pixels moved off the mask are lost, the uncovered edge is empty.</summary>
	// [BIN:TPW-EXE:0x005EA3F0 offset copy] the destination is cleared (0x5E7840), then source (x, y) is copied to (x + dx, y + dy) for the pixels that stay inside; coverage bytes are copied as they are, not made binary
	public static byte[] Shift( byte[] mask, int width, int height, int dx, int dy )
	{
		var shifted = new byte[mask.Length];
		for ( var y = Math.Max( 0, dy ); y < Math.Min( height, height + dy ); y++ )
		for ( var x = Math.Max( 0, dx ); x < Math.Min( width, width + dx ); x++ )
			shifted[y * width + x] = mask[(y - dy) * width + x - dx];
		return shifted;
	}

	/// <summary>Grows the mask in place: each pixel becomes the maximum of the window of <paramref name="radius"/> pixels either side, first along rows, then along columns.</summary>
	// [BIN:TPW-EXE:0x005EA510 mask dilation] each row is copied to a scratch line and every pixel takes the maximum over [x - r, x + r] clipped to the row; then the same along every column
	public static void Dilate( byte[] mask, int width, int height, int radius )
	{
		var line = new byte[Math.Max( width, height )];
		for ( var y = 0; y < height; y++ )
		{
			Array.Copy( mask, y * width, line, 0, width );
			for ( var x = 0; x < width; x++ )
			{
				byte maximum = 0;
				for ( var i = Math.Max( 0, x - radius ); i <= Math.Min( width - 1, x + radius ); i++ )
					maximum = Math.Max( maximum, line[i] );
				mask[y * width + x] = maximum;
			}
		}
		for ( var x = 0; x < width; x++ )
		{
			for ( var y = 0; y < height; y++ )
				line[y] = mask[y * width + x];
			for ( var y = 0; y < height; y++ )
			{
				byte maximum = 0;
				for ( var i = Math.Max( 0, y - radius ); i <= Math.Min( height - 1, y + radius ); i++ )
					maximum = Math.Max( maximum, line[i] );
				mask[y * width + x] = maximum;
			}
		}
	}

	/// <summary>Shrinks the mask in place: each pixel becomes the minimum of the window of <paramref name="radius"/> pixels either side, first along rows, then along columns; pixels closer than the radius to an edge become 0.</summary>
	// [BIN:TPW-EXE:0x005EA6D0 mask erosion] a pixel with fewer than r pixels to the left or the right of its row (then above or below in its column) is set to 0; the others take the minimum over [i - r, i + r]
	public static void Erode( byte[] mask, int width, int height, int radius )
	{
		var line = new byte[Math.Max( width, height )];
		for ( var y = 0; y < height; y++ )
		{
			Array.Copy( mask, y * width, line, 0, width );
			for ( var x = 0; x < width; x++ )
				mask[y * width + x] = Minimum( line, x, width, radius );
		}
		for ( var x = 0; x < width; x++ )
		{
			for ( var y = 0; y < height; y++ )
				line[y] = mask[y * width + x];
			for ( var y = 0; y < height; y++ )
				mask[y * width + x] = Minimum( line, y, height, radius );
		}
	}

	private static byte Minimum( byte[] line, int at, int length, int radius )
	{
		if ( at < radius || at > length - radius - 1 )
			return 0;
		byte minimum = 255;
		for ( var i = Math.Max( 0, at - radius ); i <= Math.Min( length - 1, at + radius ); i++ )
			minimum = Math.Min( minimum, line[i] );
		return minimum;
	}

	/// <summary>
	/// The normalized one-dimensional Gaussian of the blur: <c>3 + trunc(2.5 sigma)</c> taps centred on tap <c>(n - 1) / 2</c>
	/// (an even count leans right), each <c>exp(-0.5 d² / sigma²) / (2 pi sigma²)</c> divided by the sum of all taps.
	/// </summary>
	// [BIN:TPW-EXE:0x005EA8A0 Gaussian kernel] n = 3 - _ftol(sigma * -2.5) (0x702F38 = -2.5, _ftol 0x67A830 truncates); each tap is 2^(log2(e) * (d² * -0.5 / sigma²)) / (sigma² * 6.2831853) (0x702F30, 0x702F28), summed, stored as double and divided by the sum
	public static double[] Kernel( float sigma ) => Array.ConvertAll( KernelTaps( sigma ).Taps, tap => tap.ToDouble() );

	private static ScaledTaps KernelTaps( float sigma )
	{
		var s = (double)sigma;
		if ( !double.IsFinite( s ) || Math.Abs( s ) > MaximumSigma )
			throw new NotSupportedException( "Sign mask blur parameter is not supported by the compositor." );
		var count = 3 - (int)(Ext.FromFloat( sigma ) * Ext.FromDouble( -2.5 )).Truncate();
		if ( count <= 0 )
			return new ScaledTaps( Array.Empty<Ext>() );
		var centre = (count - 1) / 2;
		// the taps depend on the sigma and the precision only: made once per pair (every sign layer of a theme repeats a few sigmas)
		var key = (BitConverter.SingleToInt32Bits( sigma ), X87Precision.Bits);
		lock ( kernelCache )
			if ( kernelCache.TryGetValue( key, out var cached ) )
				return cached;
		var taps = new ScaledTaps( Ext.GaussianTaps( sigma, Enumerable.Range( 0, count ).Select( k => (k - centre) * (k - centre) ).ToArray() ) );
		lock ( kernelCache )
			kernelCache[key] = taps;
		return taps;
	}

	private static readonly Dictionary<(int Sigma, int Precision), ScaledTaps> kernelCache = new();

	/// <summary>
	/// Blurs the mask in place with <see cref="Kernel"/>, first along rows, then along columns. Taps that fall off the mask
	/// are dropped without renormalizing, so coverage fades at the mask's edges; each sum is truncated to a byte.
	/// </summary>
	// [BIN:TPW-EXE:0x005EA9DC..0x005EAA40 horizontal pass] for pixel x the taps run over [max(0, x - c), min(w - 1, x + n - c - 1)], tap index i - x + c, summed from 0.0 in source order and truncated with _ftol; the vertical pass 0x5EAAC1..0x5EAB1F does the same along each column of the row-blurred mask
	public static void Blur( byte[] mask, int width, int height, float sigma )
	{
		var kernel = KernelTaps( sigma );
		// [BIN:TPW-EXE:0x005EA8A0 empty kernel] with no taps (a parameter of -1.2 or less) both passes still run, every tap range [x - c, x + n - c - 1] is empty, and each pixel is written as the truncated empty sum 0
		if ( kernel.Taps.Length == 0 )
		{
			Array.Clear( mask );
			return;
		}
		var centre = (kernel.Taps.Length - 1) / 2;
		var line = new byte[Math.Max( width, height )];
		for ( var y = 0; y < height; y++ )
		{
			Array.Copy( mask, y * width, line, 0, width );
			for ( var x = 0; x < width; x++ )
				mask[y * width + x] = Convolve( line, x, width, kernel, centre );
		}
		for ( var x = 0; x < width; x++ )
		{
			for ( var y = 0; y < height; y++ )
				line[y] = mask[y * width + x];
			for ( var y = 0; y < height; y++ )
				mask[y * width + x] = Convolve( line, y, height, kernel, centre );
		}
	}

	private static byte Convolve( byte[] line, int at, int length, ScaledTaps kernel, int centre )
	{
		var first = Math.Max( 0, at - centre );
		var last = Math.Min( length - 1, at + kernel.Taps.Length - centre - 1 );
		var sum = Ext.Zero;
		for ( var i = first; i <= last; i++ )
		{
			// a zero byte adds a zero product, which leaves a sum that is already rounded as it was (most of a glyph mask is empty)
			if ( line[i] == 0 )
				continue;
			sum += kernel.For( line[i] )[i - at + centre];
		}
		return unchecked( (byte)sum.Truncate() );
	}
}
