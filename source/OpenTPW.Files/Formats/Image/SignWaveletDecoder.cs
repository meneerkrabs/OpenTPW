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
	public static byte[] Decode( SignBitmap bitmap )
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
		var y = DecodeChannel( colorBytes, ref colorPosition, side, scales[0] );
		var cb = DecodeChannel( colorBytes, ref colorPosition, side / 2, scales[1] );
		var cr = DecodeChannel( colorBytes, ref colorPosition, side / 2, scales[2] );
		var a = DecodeChannel( alphaBytes, ref alphaPosition, side, scales[3] );
		if ( colorPosition != colorBytes.Length || alphaPosition != alphaBytes.Length )
			throw new InvalidDataException( "Sign wavelet channels do not consume their chunks." );

		var result = new byte[bitmap.Width * bitmap.Height * 4];
		for ( var row = 0; row < bitmap.Height; row++ )
		for ( var column = 0; column < bitmap.Width; column++ )
		{
			var luma = y[row * side + column];
			var chromaIndex = (row / 2) * (side / 2) + column / 2;
			var blueDifference = cb[chromaIndex];
			var redDifference = cr[chromaIndex];
			var offset = (row * bitmap.Width + column) * 4;
			result[offset] = ClampByte( a[row * side + column] );
			// [BIN:TPW-EXE:0x5935A0 YCbCr to RGB] R = Y + 1.402 Cr, G = Y - 0.344136 Cb - 0.714136 Cr, B = Y + 1.772 Cb (float32 constants)
			result[offset + 1] = ColourByte( luma + 1.402f * redDifference );
			result[offset + 2] = ColourByte( luma - 0.344136f * blueDifference - 0.714136f * redDifference );
			result[offset + 3] = ColourByte( luma + 1.772f * blueDifference );
		}
		return result;
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

	private static float[] DecodeChannel( byte[] input, ref int position, int size, float scale )
	{
		var coefficients = new float[size * size];
		for ( var i = 0; i < coefficients.Length; i++ )
		{
			if ( position >= input.Length ) throw new InvalidDataException( "Truncated sign wavelet channel." );
			int value = (sbyte)input[position++];
			if ( value == sbyte.MinValue )
			{
				if ( position > input.Length - 2 ) throw new InvalidDataException( "Truncated extended wavelet coefficient." );
				value = BinaryPrimitives.ReadInt16LittleEndian( input.AsSpan( position, 2 ) );
				position += 2;
			}
			coefficients[i] = value;
		}
		var rows = new float[size * size];
		var output = new float[size * size];
		InverseRows( coefficients, rows, size, scale );
		InverseColumns( rows, output, size );
		return output;
	}

	private static void InverseRows( float[] source, float[] destination, int size, float scale )
	{
		var coefficients = new D4( scale );
		for ( var row = 0; row < size; row++ )
		for ( var index = 0; index < size / 2; index++ )
		{
			var previous = index == 0 ? size / 2 - 1 : index - 1;
			var offset = row * size;
			// [BIN:STP-PPC:0x1003339C..0x100335AC] low and high coefficients occupy separate row halves.
			InversePair( source[offset + previous], source[offset + previous + size / 2],
				source[offset + index], source[offset + index + size / 2], coefficients,
				out destination[offset + index * 2], out destination[offset + index * 2 + 1] );
		}
	}

	private static void InverseColumns( float[] source, float[] destination, int size )
	{
		var coefficients = new D4( 1 );
		for ( var column = 0; column < size; column++ )
		for ( var index = 0; index < size / 2; index++ )
		{
			var previous = index == 0 ? size / 2 - 1 : index - 1;
			InversePair( source[previous * size + column], source[(previous + size / 2) * size + column],
				source[index * size + column], source[(index + size / 2) * size + column], coefficients,
				out destination[index * 2 * size + column], out destination[(index * 2 + 1) * size + column] );
		}
	}

	private static void InversePair( float smooth0, float wave0, float smooth1, float wave1,
		D4 c, out float smooth, out float wave )
	{
		smooth = smooth0 * c.H2 + wave0 * c.H1 + smooth1 * c.H0 + wave1 * c.H3;
		wave = smooth0 * c.H3 - wave0 * c.H0 + smooth1 * c.H1 - wave1 * c.H2;
	}

	private static byte ClampByte( float value ) => (byte)Math.Clamp( value, 0, 255 );

	/// <summary>
	/// A colour byte as the PC converter forms it: the sum plus 12583040.0 (1.5 x 2^23 + 128) is stored as a float32, which rounds it to a whole number (ties to even), and its low 9 bits
	/// index a table of clamp(index - 128); a sum outside -128..383 wraps through those 9 bits.
	/// </summary>
	// [BIN:TPW-EXE:0x5935A0 colour magic number] fadd with 0x4B400080, fstp dword, mov eax, [mem], and eax, 0x1FF, then the byte at 0xC27008 + eax; the table fill (0x5916C0) is clamp(i - 0x80, 0, 255) for i = 0..511
	// [APPROX:COMPAT-018] the x87 precision control at that point (24 or 64 bit mantissa) decides whether the chroma term and the sum round separately or once, which moves a result by one level on a few pixels; the alpha channel's conversion is not read
	internal static byte ColourByte( float value )
	{
		var rounded = (int)MathF.Round( Math.Clamp( value, -4194304f, 4194304f ), MidpointRounding.ToEven );
		return (byte)Math.Clamp( ((rounded + 128) & 0x1FF) - 128, 0, 255 );
	}

	private readonly record struct D4( float Scale )
	{
		private static readonly double RootThree = Math.Sqrt( 3 );
		private static readonly double Denominator = 4 * Math.Sqrt( 2 );
		public float H0 => (float)((1 + RootThree) / Denominator) * Scale;
		public float H1 => (float)((3 + RootThree) / Denominator) * Scale;
		public float H2 => (float)((3 - RootThree) / Denominator) * Scale;
		public float H3 => (float)((1 - RootThree) / Denominator) * Scale;
	}
}
