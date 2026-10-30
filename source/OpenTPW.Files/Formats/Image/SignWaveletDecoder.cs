using System.Buffers.Binary;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

namespace OpenTPW;

/// <summary>Decodes the version-101 sign bitmap payload to native ARGB byte order.</summary>
public static class SignWaveletDecoder
{
	private const int HeaderBytes = 24;
	private const int BilzHeaderBytes = 28;
	private const int MaximumSide = 256;

	// [BIN:STP-PPC:0x100ABDAC Bitmap::load_wavelet] the engine reads four float scales, two compressed lengths and ARGB output.
	// [EXT:hardware-d3d-path] OpenTPW models the 24-bit x87 precision of a machine with a hardware Direct3D device only; a driver that lacks DrawPrimitives2 (the software path) would leave the C runtime's 53 bits, a host-hardware dependence of the original that OpenTPW does not follow (user-approved 2026-10-30)
	// [APPROX:COMPAT-018] the arithmetic after the inflate step is tp.exe's, instruction for instruction at the thread's x87 precision (24 bits; the D4 coefficients at 53 bits, from a static initialiser): scale at the dequantisation, row and column inverse, colour and alpha conversion. The header floats feed Y, Cb, Cr, alpha in file order (the PC board reader 0x5E7990 passes them to 0x592000, once for colour with three scales and once for alpha with the fourth). Still open: the GPU row orientation of the uploaded texture and original-platform board pixels
	public static byte[] Decode( SignBitmap bitmap ) => Decode( bitmap, X87Precision.Bits != 24 );

	/// <summary>Decodes with the arithmetic on emulated x87 registers (<paramref name="emulateRegisters"/>) or, at the thread's 24 bit precision, on float32 values, which round the same way.</summary>
	internal static byte[] Decode( SignBitmap bitmap, bool emulateRegisters )
	{
		ArgumentNullException.ThrowIfNull( bitmap );
		if ( !bitmap.IsWavelet || bitmap.BytesPerPixel != 4 || bitmap.Width <= 0 || bitmap.Height <= 0
			|| bitmap.Width > MaximumSide || bitmap.Height > MaximumSide )
			throw new InvalidDataException( "Unsupported sign wavelet bitmap dimensions or format." );
		var payload = bitmap.Payload.AsSpan();
		if ( payload.Length < HeaderBytes )
			throw new InvalidDataException( "Truncated sign wavelet header." );
		var scales = new float[4];
		for ( var i = 0; i < scales.Length; i++ )
		{
			scales[i] = BinaryPrimitives.ReadSingleLittleEndian( payload.Slice( i * 4, 4 ) );
			if ( !float.IsFinite( scales[i] ) || scales[i] <= 0 )
				throw new InvalidDataException( "Invalid sign wavelet scale." );
		}
		var colorLength = BinaryPrimitives.ReadUInt32LittleEndian( payload.Slice( 16, 4 ) );
		var alphaLength = BinaryPrimitives.ReadUInt32LittleEndian( payload.Slice( 20, 4 ) );
		if ( colorLength < BilzHeaderBytes || alphaLength < BilzHeaderBytes
			|| (ulong)HeaderBytes + colorLength + alphaLength != (ulong)payload.Length )
			throw new InvalidDataException( "Invalid sign wavelet chunk lengths." );

		var side = 8;
		while ( side < Math.Max( bitmap.Width, bitmap.Height ) ) side *= 2;
		var colorBytes = Inflate( payload.Slice( HeaderBytes, (int)colorLength ), side * side * 3 / 2 );
		var alphaBytes = Inflate( payload.Slice( HeaderBytes + (int)colorLength, (int)alphaLength ), side * side );
		var colorPosition = 0;
		var alphaPosition = 0;
		var y = DecodeChannel( colorBytes, ref colorPosition, side, scales[0], WrapSmoothKind.Colour, emulateRegisters );
		var cb = DecodeChannel( colorBytes, ref colorPosition, side / 2, scales[1], WrapSmoothKind.Colour, emulateRegisters );
		var cr = DecodeChannel( colorBytes, ref colorPosition, side / 2, scales[2], WrapSmoothKind.Colour, emulateRegisters );
		var a = DecodeChannel( alphaBytes, ref alphaPosition, side, scales[3], WrapSmoothKind.Alpha, emulateRegisters );
		if ( colorPosition != colorBytes.Length || alphaPosition != alphaBytes.Length )
			throw new InvalidDataException( "Sign wavelet channels do not consume their chunks." );

		var result = new byte[bitmap.Width * bitmap.Height * 4];
		if ( !emulateRegisters )
			Convert<SingleReg>( bitmap.Width, bitmap.Height, side, y, cb, cr, a, result );
		else
			Convert<ExtReg>( bitmap.Width, bitmap.Height, side, y, cb, cr, a, result );
		return result;
	}

	/// <summary>
	/// The colour conversion of 0x5935A0 per 2x2 block, with the chroma terms formed once per chroma sample (as the original shares them between the four pixels) and every
	/// operation rounded at the x87 precision control: <c>B = Y + (Cb * 1.772 + magic)</c>, <c>R = Y + (Cr * 1.402 + magic)</c>, <c>G = Y + ((magic - Cb * 0.344136) - Cr * 0.714136)</c>, each sum stored as a float32.
	/// </summary>
	// [BIN:TPW-EXE:0x5935A0 colour conversion] fld/fmul of the chroma by the constants 0xFB277C..0xFB2788 (1.772, 1.402, 0.344136, 0.714136), fadd of 0x4B400080 (0xFB278C) for the blue and red terms and fsubr from it for green, then one fadd of the luma per pixel, fstp dword and a table byte per low 9 bits; the chroma terms stay on the register stack between the two pixel rows of a block
	private static void Convert<T>( int width, int height, int side, float[] y, float[] cb, float[] cr, float[] a, byte[] result )
		where T : struct, IReg<T>
	{
		var magic = T.Load( MagicNumber );
		var kBlue = T.Load( 1.772f );
		var kRed = T.Load( 1.402f );
		var kGreenBlue = T.Load( 0.344136f );
		var kGreenRed = T.Load( 0.714136f );
		var chromaSide = side / 2;
		for ( var row = 0; row < height; row++ )
		for ( var column = 0; column < width; column++ )
		{
			var chroma = (row / 2) * chromaSide + column / 2;
			var blueIn = T.Load( cb[chroma] );
			var redIn = T.Load( cr[chroma] );
			var blueTerm = blueIn * kBlue + magic;
			var redTerm = redIn * kRed + magic;
			var greenTerm = (magic - blueIn * kGreenBlue) - redIn * kGreenRed;
			var luma = T.Load( y[row * side + column] );
			var offset = (row * width + column) * 4;
			result[offset] = ClampByte( a[row * side + column] );
			// R, G, B in the order the decoder has always written them after the alpha byte
			result[offset + 1] = TableByte( (luma + redTerm).Store() );
			result[offset + 2] = TableByte( (luma + greenTerm).Store() );
			result[offset + 3] = TableByte( (luma + blueTerm).Store() );
		}
	}

	private static byte[] Inflate( ReadOnlySpan<byte> chunk, int maximumBytes )
	{
		if ( !chunk[..4].SequenceEqual( "BILZ"u8 ) )
			throw new InvalidDataException( "Sign wavelet chunk is not BILZ." );
		var declaredOutput = BinaryPrimitives.ReadUInt32LittleEndian( chunk.Slice( 4, 4 ) );
		var declaredStream = BinaryPrimitives.ReadUInt32LittleEndian( chunk.Slice( 8, 4 ) );
		if ( declaredOutput > maximumBytes * 3 || declaredStream != chunk.Length )
			throw new InvalidDataException( "Invalid BILZ size." );
		using var input = new MemoryStream( chunk[BilzHeaderBytes..].ToArray() );
		using var inflater = new InflaterInputStream( input );
		using var output = new MemoryStream();
		var buffer = new byte[8192];
		int read;
		while ( (read = inflater.Read( buffer, 0, buffer.Length )) != 0 )
		{
			if ( output.Length + read > maximumBytes * 3 )
				throw new InvalidDataException( "Sign wavelet chunk exceeds decoded size limit." );
			output.Write( buffer, 0, read );
		}
		if ( output.Length != declaredOutput )
			throw new InvalidDataException( "BILZ decoded size differs from its header." );
		return output.ToArray();
	}

	private enum WrapSmoothKind { Colour, Alpha }

	/// <summary>
	/// One channel as 0x592B80 (dequantise one row: each coefficient times the header scale, stored as a float32), 0x592C50 (the 1-D inverse of that row) and then the column pass
	/// (0x5933A0 for the colour planes, 0x592E60 for the alpha plane) run it. Applying the scale to the coefficients is what the original does; it does not enter the filter.
	/// </summary>
	// [BIN:TPW-EXE:0x592E60 header scale] the scale (stack argument) multiplies each dequantised coefficient in 0x592B80 (fmul dword [ebp+0x14], fstp dword) and in 0x592E60's inlined copy (fmul [ebp+0x18]); the D4 coefficients are not scaled
	private static float[] DecodeChannel( byte[] input, ref int position, int size, float scale, WrapSmoothKind kind, bool emulateRegisters )
	{
		var rows = new float[size * size];
		var row = new float[size];
		var single = !emulateRegisters;
		for ( var r = 0; r < size; r++ )
		{
			for ( var i = 0; i < size; i++ )
			{
				if ( position >= input.Length ) throw new InvalidDataException( "Truncated sign wavelet channel." );
				int value = (sbyte)input[position++];
				if ( value == sbyte.MinValue )
				{
					if ( position > input.Length - 2 ) throw new InvalidDataException( "Truncated extended wavelet coefficient." );
					value = BinaryPrimitives.ReadInt16LittleEndian( input.AsSpan( position, 2 ) );
					position += 2;
				}
				// the table float times the scale, stored as a float32 (the product of two floats is exact at 53 and 64 bits and one rounding at 24)
				row[i] = value * scale;
			}
			if ( single )
				InverseRow<SingleReg>( row, rows, r * size, size );
			else
				InverseRow<ExtReg>( row, rows, r * size, size );
		}
		var output = new float[size * size];
		if ( single )
			InverseColumns<SingleReg>( rows, output, size, kind );
		else
			InverseColumns<ExtReg>( rows, output, size, kind );
		return output;
	}

	/// <summary>
	/// The D4 coefficients as the C++ static initialisers 0x591600, 0x591630, 0x591660 and 0x591690 compute them: they run in the CRT start-up, after <c>_controlfp(_PC_53)</c> (0x6804D0 through the FP hook
	/// <c>[0x77E718]</c> = 0x67A5F0) and before the display and Direct3D init that drop the thread to 24 bits, so the square roots, the sum, the product and the quotient round at 53 bits and the result is stored as a float32.
	/// </summary>
	// [BIN:TPW-EXE:0x591600 D4 coefficients] fld 3.0, fsqrt, fld 2.0, fsqrt, fxch, fsub/fsubr of -1.0 / -3.0 / 3.0 / 1.0 (0x702618, 0x702630, 0x702610, 0x702638), fmul 0.25, fxch, fdivp, fstp dword to 0xC27208..0xC27214; the four entries sit in the initialiser table at 0x72BD70 inside the C++ range 0x727000..0x72E0E8 that _cinit (0x67F8F0) walks after the FP hook
	internal static (float H0, float H1, float H2, float H3) D4Coefficients() => X87Precision.With( 53, () =>
	{
		var root3 = Ext.Sqrt( Ext.FromInt( 3 ) );
		var root2 = Ext.Sqrt( Ext.FromInt( 2 ) );
		var quarter = Ext.FromDouble( 0.25 );
		float Form( Ext numerator ) => (numerator * quarter / root2).ToFloat();
		return (Form( root3 - Ext.FromInt( -1 ) ), Form( root3 - Ext.FromInt( -3 ) ), Form( Ext.FromInt( 3 ) - root3 ), Form( Ext.FromInt( 1 ) - root3 ));
	} );

	private static readonly (float H0, float H1, float H2, float H3) D4 = D4Coefficients();

	/// <summary>A float32 loaded into an x87 register and the arithmetic on it: the single-precision register and the emulated register with the full 64-bit mantissa both round at the precision control.</summary>
	internal interface IReg<T> where T : struct, IReg<T>
	{
		static abstract T Load( float value );
		static abstract T operator +( T a, T b );
		static abstract T operator -( T a, T b );
		static abstract T operator *( T a, T b );

		/// <summary>FSTP to a float32.</summary>
		float Store();
	}

	/// <summary>A register at 24 mantissa bits: with no wider exponent range in play it computes what float32 arithmetic computes.</summary>
	internal readonly record struct SingleReg( float Value ) : IReg<SingleReg>
	{
		public static SingleReg Load( float value ) => new( value );
		public static SingleReg operator +( SingleReg a, SingleReg b ) => new( a.Value + b.Value );
		public static SingleReg operator -( SingleReg a, SingleReg b ) => new( a.Value - b.Value );
		public static SingleReg operator *( SingleReg a, SingleReg b ) => new( a.Value * b.Value );
		public float Store() => Value;
	}

	/// <summary>An emulated x87 register: every operation rounds at the thread's precision control (24, 53 or 64 bits).</summary>
	internal readonly record struct ExtReg( Ext Value ) : IReg<ExtReg>
	{
		public static ExtReg Load( float value ) => new( Ext.FromFloat( value ) );
		public static ExtReg operator +( ExtReg a, ExtReg b ) => new( a.Value + b.Value );
		public static ExtReg operator -( ExtReg a, ExtReg b ) => new( a.Value - b.Value );
		public static ExtReg operator *( ExtReg a, ExtReg b ) => new( a.Value * b.Value );
		public float Store() => Value.ToFloat();
	}

	private readonly record struct Taps<T>( T A, T B, T C, T D ) where T : struct, IReg<T>
	{
		public static Taps<T> Create() => new( T.Load( D4.H0 ), T.Load( D4.H1 ), T.Load( D4.H2 ), T.Load( D4.H3 ) );
	}

	// The pair formulas, in the operand order of the original's instructions (s0, w0 = the previous smooth and wave sample, s1, w1 = the current ones). Each product is a float32 times a coefficient;
	// the pairs in parentheses are the FADDP / FSUBP of two values held on the register stack.
	// [BIN:TPW-EXE:0x592C50 pair formulas] steady loop (0x592D32..0x592DA8): smooth = (s1*A + w1*D) + (w0*B + s0*C); wave = (s1*B + s0*D) - (w0*A + w1*C); last pair of a row (0x592DAA..0x592DE2): same smooth, wave = s0*D + (s1*B - (w0*A + w1*C)); wrap pair 0 (0x592DEA..0x592E47): smooth = ((s0*C + w0*B) + w1*D) + s1*A, wave = ((s0*D + s1*B) - w0*A) - w1*C; short rows (n < 6, 0x592C8D..0x592CED): smooth = ((w1*D + s0*C) + s1*A) + w0*B, same wave
	private static void Steady<T>( in Taps<T> k, float s0, float s1, float w0, float w1, out float smooth, out float wave ) where T : struct, IReg<T>
	{
		var ls0 = T.Load( s0 );
		var ls1 = T.Load( s1 );
		var lw0 = T.Load( w0 );
		var lw1 = T.Load( w1 );
		smooth = ((ls1 * k.A + lw1 * k.D) + (lw0 * k.B + ls0 * k.C)).Store();
		wave = ((ls1 * k.B + ls0 * k.D) - (lw0 * k.A + lw1 * k.C)).Store();
	}

	private static void Last<T>( in Taps<T> k, float s0, float s1, float w0, float w1, out float smooth, out float wave ) where T : struct, IReg<T>
	{
		var ls0 = T.Load( s0 );
		var ls1 = T.Load( s1 );
		var lw0 = T.Load( w0 );
		var lw1 = T.Load( w1 );
		smooth = ((ls1 * k.A + lw1 * k.D) + (lw0 * k.B + ls0 * k.C)).Store();
		wave = (ls0 * k.D + (ls1 * k.B - (lw0 * k.A + lw1 * k.C))).Store();
	}

	private static void Wrap<T>( in Taps<T> k, float s0, float s1, float w0, float w1, WrapSmoothKind kind, out float smooth, out float wave ) where T : struct, IReg<T>
	{
		var ls0 = T.Load( s0 );
		var ls1 = T.Load( s1 );
		var lw0 = T.Load( w0 );
		var lw1 = T.Load( w1 );
		// the colour column pass (0x5933A0) adds the products as w1, w0, s0, s1; the row pass and the alpha column pass (0x592E60) as s0, w0, w1, s1
		smooth = (kind == WrapSmoothKind.Colour
			? ((lw1 * k.D + lw0 * k.B) + ls0 * k.C) + ls1 * k.A
			: ((ls0 * k.C + lw0 * k.B) + lw1 * k.D) + ls1 * k.A).Store();
		wave = (((ls0 * k.D + ls1 * k.B) - lw0 * k.A) - lw1 * k.C).Store();
	}

	private static void Short<T>( in Taps<T> k, float s0, float s1, float w0, float w1, out float smooth, out float wave ) where T : struct, IReg<T>
	{
		var ls0 = T.Load( s0 );
		var ls1 = T.Load( s1 );
		var lw0 = T.Load( w0 );
		var lw1 = T.Load( w1 );
		smooth = (((lw1 * k.D + ls0 * k.C) + ls1 * k.A) + lw0 * k.B).Store();
		wave = (((ls0 * k.D + ls1 * k.B) - lw0 * k.A) - lw1 * k.C).Store();
	}

	/// <summary>The 1-D inverse of one row (0x592C50): low half smooth samples, high half wave samples, the pair 0 wrapping to the last sample.</summary>
	private static void InverseRow<T>( float[] source, float[] destination, int offset, int size ) where T : struct, IReg<T>
	{
		var k = Taps<T>.Create();
		var half = size / 2;
		Wrap( k, source[half - 1], source[0], source[size - 1], source[half], WrapSmoothKind.Alpha, out destination[offset], out destination[offset + 1] );
		for ( var pair = 1; pair < half; pair++ )
		{
			float s0 = source[pair - 1], s1 = source[pair], w0 = source[half + pair - 1], w1 = source[half + pair];
			var o = offset + pair * 2;
			if ( size < 6 )
				Short( k, s0, s1, w0, w1, out destination[o], out destination[o + 1] );
			else if ( pair == half - 1 )
				Last( k, s0, s1, w0, w1, out destination[o], out destination[o + 1] );
			else
				Steady( k, s0, s1, w0, w1, out destination[o], out destination[o + 1] );
		}
	}

	/// <summary>
	/// The column pass over the row-transformed plane (0x5933A0 / 0x592E60): every column of a pair of source rows gives two output rows with the same formulas, the last column of each pair of rows taking the
	/// last-pair wave, and the wrap pair 0 in a loop of its own.
	/// </summary>
	private static void InverseColumns<T>( float[] source, float[] destination, int size, WrapSmoothKind kind ) where T : struct, IReg<T>
	{
		var k = Taps<T>.Create();
		var half = size / 2;
		for ( var pair = 1; pair < half; pair++ )
		for ( var column = 0; column < size; column++ )
		{
			float s0 = source[(pair - 1) * size + column], s1 = source[pair * size + column];
			float w0 = source[(half + pair - 1) * size + column], w1 = source[(half + pair) * size + column];
			var o = pair * 2 * size + column;
			if ( column == size - 1 )
				Last( k, s0, s1, w0, w1, out destination[o], out destination[o + size] );
			else
				Steady( k, s0, s1, w0, w1, out destination[o], out destination[o + size] );
		}
		for ( var column = 0; column < size; column++ )
			Wrap( k, source[(half - 1) * size + column], source[column], source[(size - 1) * size + column], source[half * size + column], kind,
				out destination[column], out destination[size + column] );
	}

	/// <summary>
	/// The alpha plane's conversion in 0x592E60: a value below 0 is 0, above 255 is 255 and the rest is truncated toward zero (the <c>_ftol</c> wrapper), then masked to a byte; a value that compares unordered (NaN) takes the first branch, 0.
	/// </summary>
	// [BIN:TPW-EXE:0x593301 alpha conversion] fld, fcomp dword [0x70268C] (0.0), fnstsw, test ah,1 -> 0; fcomp dword [0x702690] (255.0), test ah,0x41 jne -> _ftol and 0xFF, else 255
	private static byte ClampByte( float value ) => !(value >= 0) ? (byte)0 : value > 255 ? (byte)255 : (byte)(int)value;

	/// <summary>1.5 x 2^23 + 128 as the float32 the converter adds (0x4B400080).</summary>
	private const float MagicNumber = 12583040f;

	/// <summary>
	/// A colour byte as the PC converter forms it from the stored float32 sum: its low 9 bits index a table of clamp(index - 128), so a sum outside -128..383 wraps through those 9 bits.
	/// </summary>
	// [BIN:TPW-EXE:0x5935A0 colour table lookup] mov eax, [mem]; and eax, 0x1FF; mov al, [eax + 0xC27008]; the table fill (0x5916C0) is clamp(i - 0x80, 0, 255) for i = 0..511
	internal static byte TableByte( float storedSum )
		=> (byte)Math.Clamp( (BitConverter.SingleToInt32Bits( storedSum ) & 0x1FF) - 128, 0, 255 );

	/// <summary>A colour byte from a plain sum: it is added to the magic number as a float32, which rounds it to a whole number (ties to even), then looked up (test helper for the conversion's rounding).</summary>
	internal static byte ColourByte( float value ) => TableByte( value + MagicNumber );
}
