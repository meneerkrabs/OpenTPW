using System.Buffers.Binary;

namespace OpenTPW;

public readonly record struct TqiFrameHeader( int Width, int Height, byte Quantizer, int UnknownBytes );

/// <summary>Decoded TQI picture: 8-bit planar 4:2:0 (Y full size, Cb/Cr half size in both axes).</summary>
public sealed class TqiFrame
{
	public int Width { get; }
	public int Height { get; }
	public byte[] Y { get; }
	public byte[] Cb { get; }
	public byte[] Cr { get; }

	internal TqiFrame( int width, int height, byte[] y, byte[] cb, byte[] cr )
	{
		Width = width;
		Height = height;
		Y = y;
		Cb = cb;
		Cr = cr;
	}

	/// <summary>
	/// Converts to packed RGB24 using full-range BT.601 coefficients and nearest chroma sampling.
	/// The original player's colour matrix/range is NOT verified; this conversion is a documented assumption.
	/// </summary>
	public byte[] ToRgb24()
	{
		var rgb = new byte[Width * Height * 3];
		var chromaWidth = Width / 2;
		for ( var row = 0; row < Height; row++ )
		{
			for ( var column = 0; column < Width; column++ )
			{
				var luma = (double)Y[row * Width + column];
				var chroma = (row / 2) * chromaWidth + column / 2;
				var blue = Cb[chroma] - 128.0;
				var red = Cr[chroma] - 128.0;
				var output = (row * Width + column) * 3;
				rgb[output] = ClampByte( luma + 1.402 * red );
				rgb[output + 1] = ClampByte( luma - 0.344136 * blue - 0.714136 * red );
				rgb[output + 2] = ClampByte( luma + 1.772 * blue );
			}
		}
		return rgb;
	}

	private static byte ClampByte( double value ) => (byte)Math.Clamp( Math.Floor( value + 0.5 ), 0, 255 );
}

/// <summary>
/// CPU decoder for EA TQI intra frames ("pIQT" chunk payloads). Independent implementation from public
/// format facts: 8-byte header (u16 width, u16 height, u8 quantizer, 3 unknown bytes), then a bitstream
/// read MSB-first from little-endian 32-bit words. Each 16x16 macroblock holds four luma and two chroma
/// 8x8 blocks; each block is an MPEG-1 (ISO/IEC 11172-2) intra block without macroblock headers:
/// DC size VLC + differential, then table B.14 run/level VLCs, 6+8(+8)-bit escapes, end-of-block.
/// </summary>
public static class TqiDecoder
{
	public const int HeaderBytes = 8;
	public const int MaximumDimension = 1024;
	public const int MaximumQuantizer = 107;

	private const int EndOfBlock = -1;
	private const int Escape = -2;
	private const int Invalid = 0;

	private static readonly byte[] ZigZag =
	{
		0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
		12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
		35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
		58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
	};

	// MPEG-1 default intra quantiser matrix, natural (row-major) order.
	private static readonly byte[] IntraMatrix =
	{
		8, 16, 19, 22, 26, 27, 29, 34,
		16, 16, 22, 24, 27, 29, 34, 37,
		19, 22, 26, 27, 29, 34, 34, 38,
		22, 22, 26, 27, 29, 34, 37, 40,
		22, 26, 27, 29, 32, 35, 40, 48,
		26, 27, 29, 32, 35, 40, 48, 58,
		26, 27, 29, 34, 38, 46, 56, 69,
		27, 29, 35, 38, 46, 56, 69, 83,
	};

	// MPEG-1 Table B.14 (DCT coefficients, "next coefficient" form; codes exclude the sign bit).
	private static readonly (string Code, int Run, int Level)[] CoefficientCodes =
	{
		("10", EndOfBlock, 0), ("000001", Escape, 0),
		("11", 0, 1), ("011", 1, 1), ("0100", 0, 2), ("0101", 2, 1), ("00101", 0, 3), ("00111", 3, 1),
		("00110", 4, 1), ("000110", 1, 2), ("000111", 5, 1), ("000101", 6, 1), ("000100", 7, 1),
		("0000110", 0, 4), ("0000100", 2, 2), ("0000111", 8, 1), ("0000101", 9, 1),
		("00100110", 0, 5), ("00100001", 0, 6), ("00100101", 1, 3), ("00100100", 3, 2),
		("00100111", 10, 1), ("00100011", 11, 1), ("00100010", 12, 1), ("00100000", 13, 1),
		("0000001010", 0, 7), ("0000001100", 1, 4), ("0000001011", 2, 3), ("0000001111", 4, 2),
		("0000001001", 5, 2), ("0000001110", 14, 1), ("0000001101", 15, 1), ("0000001000", 16, 1),
		("000000011101", 0, 8), ("000000011000", 0, 9), ("000000010011", 0, 10), ("000000010000", 0, 11),
		("000000011011", 1, 5), ("000000010100", 2, 4), ("000000011100", 3, 3), ("000000010010", 4, 3),
		("000000011110", 6, 2), ("000000010101", 7, 2), ("000000010001", 8, 2), ("000000011111", 17, 1),
		("000000011010", 18, 1), ("000000011001", 19, 1), ("000000010111", 20, 1), ("000000010110", 21, 1),
		("0000000011010", 0, 12), ("0000000011001", 0, 13), ("0000000011000", 0, 14), ("0000000010111", 0, 15),
		("0000000010110", 1, 6), ("0000000010101", 1, 7), ("0000000010100", 2, 5), ("0000000010011", 3, 4),
		("0000000010010", 5, 3), ("0000000010001", 9, 2), ("0000000010000", 10, 2), ("0000000011111", 22, 1),
		("0000000011110", 23, 1), ("0000000011101", 24, 1), ("0000000011100", 25, 1), ("0000000011011", 26, 1),
		("00000000011111", 0, 16), ("00000000011110", 0, 17), ("00000000011101", 0, 18), ("00000000011100", 0, 19),
		("00000000011011", 0, 20), ("00000000011010", 0, 21), ("00000000011001", 0, 22), ("00000000011000", 0, 23),
		("00000000010111", 0, 24), ("00000000010110", 0, 25), ("00000000010101", 0, 26), ("00000000010100", 0, 27),
		("00000000010011", 0, 28), ("00000000010010", 0, 29), ("00000000010001", 0, 30), ("00000000010000", 0, 31),
		("000000000011000", 0, 32), ("000000000010111", 0, 33), ("000000000010110", 0, 34), ("000000000010101", 0, 35),
		("000000000010100", 0, 36), ("000000000010011", 0, 37), ("000000000010010", 0, 38), ("000000000010001", 0, 39),
		("000000000010000", 0, 40), ("000000000011111", 1, 8), ("000000000011110", 1, 9), ("000000000011101", 1, 10),
		("000000000011100", 1, 11), ("000000000011011", 1, 12), ("000000000011010", 1, 13), ("000000000011001", 1, 14),
		("0000000000010011", 1, 15), ("0000000000010010", 1, 16), ("0000000000010001", 1, 17), ("0000000000010000", 1, 18),
		("0000000000010100", 6, 3), ("0000000000011010", 11, 2), ("0000000000011001", 12, 2), ("0000000000011000", 13, 2),
		("0000000000010111", 14, 2), ("0000000000010110", 15, 2), ("0000000000010101", 16, 2), ("0000000000011111", 27, 1),
		("0000000000011110", 28, 1), ("0000000000011101", 29, 1), ("0000000000011100", 30, 1), ("0000000000011011", 31, 1),
	};

	// MPEG-1 Tables B.12/B.13 (dct_dc_size_luminance/chrominance), sizes 0..8.
	private static readonly string[] LumaDcCodes = { "100", "00", "01", "101", "110", "1110", "11110", "111110", "1111110" };
	private static readonly string[] ChromaDcCodes = { "00", "01", "10", "110", "1110", "11110", "111110", "1111110", "11111110" };

	// Packed lookup entries: (length << 24) | (run+2 << 12) | level; zero means invalid code.
	private static readonly int[] CoefficientLookup = BuildCoefficientLookup();
	private static readonly int[] LumaDcLookup = BuildDcLookup( LumaDcCodes );
	private static readonly int[] ChromaDcLookup = BuildDcLookup( ChromaDcCodes );
	private static readonly double[] Basis = BuildBasis();

	/// <summary>Number of run/level entries in the coefficient table (excluding end-of-block and escape).</summary>
	public static int CoefficientTableEntries => CoefficientCodes.Length - 2;

	public static TqiFrameHeader ReadHeader( ReadOnlySpan<byte> payload )
	{
		if ( payload.Length < HeaderBytes )
			throw new InvalidDataException( "TQI frame header is truncated." );
		var width = BinaryPrimitives.ReadUInt16LittleEndian( payload );
		var height = BinaryPrimitives.ReadUInt16LittleEndian( payload[2..] );
		var quantizer = payload[4];
		if ( width == 0 || height == 0 || width % 16 != 0 || height % 16 != 0 || width > MaximumDimension || height > MaximumDimension )
			throw new InvalidDataException( $"TQI frame dimensions {width}x{height} are unsupported." );
		if ( quantizer > MaximumQuantizer )
			throw new InvalidDataException( $"TQI quantizer {quantizer} is outside the supported range." );
		if ( (payload.Length - HeaderBytes) % 4 != 0 )
			throw new InvalidDataException( "TQI bitstream is not a whole number of 32-bit words." );
		return new TqiFrameHeader( width, height, quantizer, payload[5] | payload[6] << 8 | payload[7] << 16 );
	}

	public static TqiFrame Decode( ReadOnlySpan<byte> payload )
	{
		var header = ReadHeader( payload );
		var width = header.Width;
		var height = header.Height;
		var scale = (107.5 - header.Quantizer) * 0.625 / 8.0;
		var dequant = new double[64];
		dequant[0] = 8.0;
		for ( var index = 1; index < 64; index++ )
			dequant[index] = IntraMatrix[index] * scale;

		var y = new byte[width * height];
		var cb = new byte[width * height / 4];
		var cr = new byte[width * height / 4];
		var reader = new BitReader( payload[HeaderBytes..] );
		var predictors = new int[3];
		var coefficients = new double[64];
		var temporary = new double[64];
		for ( var macroY = 0; macroY < height / 16; macroY++ )
		{
			for ( var macroX = 0; macroX < width / 16; macroX++ )
			{
				for ( var block = 0; block < 6; block++ )
				{
					var component = block < 4 ? 0 : block - 3;
					DecodeBlock( ref reader, component, ref predictors[component], dequant, coefficients );
					InverseTransform( coefficients, temporary );
					if ( block < 4 )
						Store( temporary, y, width, macroX * 16 + (block & 1) * 8, macroY * 16 + (block >> 1) * 8 );
					else
						Store( temporary, block == 4 ? cb : cr, width / 2, macroX * 8, macroY * 8 );
				}
			}
		}
		reader.RequireZeroTail();
		return new TqiFrame( width, height, y, cb, cr );
	}

	private static void DecodeBlock( ref BitReader reader, int component, ref int predictor, ReadOnlySpan<double> dequant, Span<double> coefficients )
	{
		coefficients.Clear();
		var entry = (component == 0 ? LumaDcLookup : ChromaDcLookup)[reader.Peek( 8 )];
		if ( entry == 0 )
			throw new InvalidDataException( "TQI DC size code is invalid." );
		reader.Skip( entry >> 24 );
		var size = entry & 0xff;
		if ( size > 0 )
		{
			var bits = reader.Read( size );
			predictor += (bits >> (size - 1)) != 0 ? bits : bits - (1 << size) + 1;
		}
		coefficients[0] = predictor * dequant[0];
		var position = 0;
		while ( true )
		{
			entry = CoefficientLookup[reader.Peek( 16 )];
			if ( entry == Invalid )
				throw new InvalidDataException( "TQI coefficient code is invalid." );
			reader.Skip( entry >> 24 );
			var run = ((entry >> 12) & 0xfff) - 2;
			int level;
			if ( run == EndOfBlock )
				return;
			if ( run == Escape )
			{
				run = reader.Read( 6 );
				level = reader.Read( 8 );
				if ( level == 0 )
				{
					level = reader.Read( 8 );
					if ( level < 128 )
						throw new InvalidDataException( "TQI escape uses a forbidden extended level." );
				}
				else if ( level == 128 )
				{
					level = reader.Read( 8 ) - 256;
					if ( level > -129 )
						throw new InvalidDataException( "TQI escape uses a forbidden extended level." );
				}
				else if ( level > 128 )
					level -= 256;
			}
			else
			{
				level = entry & 0xfff;
				if ( reader.Read( 1 ) != 0 )
					level = -level;
			}
			position += run + 1;
			if ( position > 63 )
				throw new InvalidDataException( "TQI block coefficients overflow 64 entries." );
			var natural = ZigZag[position];
			coefficients[natural] = level * dequant[natural];
		}
	}

	private static void InverseTransform( Span<double> coefficients, Span<double> output )
	{
		// Separable orthonormal 8x8 IDCT: output[r,c] = sum_v sum_u B[v,r] F[v,u] B[u,c].
		Span<double> rows = stackalloc double[64];
		for ( var v = 0; v < 8; v++ )
		{
			var any = false;
			for ( var u = 0; u < 8; u++ )
				any |= coefficients[v * 8 + u] != 0;
			for ( var column = 0; column < 8; column++ )
			{
				var sum = 0.0;
				if ( any )
					for ( var u = 0; u < 8; u++ )
						sum += coefficients[v * 8 + u] * Basis[u * 8 + column];
				rows[v * 8 + column] = sum;
			}
		}
		for ( var row = 0; row < 8; row++ )
		{
			for ( var column = 0; column < 8; column++ )
			{
				var sum = 0.0;
				for ( var v = 0; v < 8; v++ )
					sum += Basis[v * 8 + row] * rows[v * 8 + column];
				output[row * 8 + column] = sum;
			}
		}
	}

	private static void Store( ReadOnlySpan<double> samples, byte[] plane, int stride, int left, int top )
	{
		for ( var row = 0; row < 8; row++ )
			for ( var column = 0; column < 8; column++ )
				plane[(top + row) * stride + left + column] = (byte)Math.Clamp( Math.Floor( samples[row * 8 + column] + 0.5 ), 0, 255 );
	}

	private static double[] BuildBasis()
	{
		var basis = new double[64];
		for ( var frequency = 0; frequency < 8; frequency++ )
			for ( var sample = 0; sample < 8; sample++ )
				basis[frequency * 8 + sample] = (frequency == 0 ? Math.Sqrt( 0.125 ) : 0.5) * Math.Cos( (2 * sample + 1) * frequency * Math.PI / 16 );
		return basis;
	}

	private static int[] BuildCoefficientLookup()
	{
		var lookup = new int[1 << 16];
		foreach ( var (code, run, level) in CoefficientCodes )
			Fill( lookup, code, 16, (code.Length << 24) | ((run + 2) << 12) | level );
		return lookup;
	}

	private static int[] BuildDcLookup( string[] codes )
	{
		var lookup = new int[1 << 8];
		for ( var size = 0; size < codes.Length; size++ )
			Fill( lookup, codes[size], 8, (codes[size].Length << 24) | size );
		return lookup;
	}

	private static void Fill( int[] lookup, string code, int width, int value )
	{
		var prefix = Convert.ToInt32( code, 2 ) << (width - code.Length);
		var span = 1 << (width - code.Length);
		for ( var index = prefix; index < prefix + span; index++ )
		{
			if ( lookup[index] != 0 )
				throw new InvalidOperationException( $"TQI VLC table is not prefix-free at code {code}." );
			lookup[index] = value;
		}
	}

	private ref struct BitReader
	{
		private readonly ReadOnlySpan<byte> data;
		private readonly long totalBits;
		private long position;

		public BitReader( ReadOnlySpan<byte> data )
		{
			this.data = data;
			totalBits = (long)data.Length * 8;
			position = 0;
		}

		private uint Word( long index ) => index < data.Length / 4 ? BinaryPrimitives.ReadUInt32LittleEndian( data[(int)(index * 4)..] ) : 0u;

		public int Peek( int count )
		{
			var window = ((ulong)Word( position >> 5 ) << 32) | Word( (position >> 5) + 1 );
			return (int)((window << (int)(position & 31)) >> (64 - count));
		}

		public void Skip( int count )
		{
			position += count;
			if ( position > totalBits )
				throw new InvalidDataException( "TQI bitstream is truncated." );
		}

		public int Read( int count )
		{
			var value = Peek( count );
			Skip( count );
			return value;
		}

		public void RequireZeroTail()
		{
			if ( totalBits - position >= 32 )
				throw new InvalidDataException( "TQI bitstream has unused whole words after the last macroblock." );
			var remaining = (int)(totalBits - position);
			if ( remaining > 0 && Peek( remaining ) != 0 )
				throw new InvalidDataException( "TQI bitstream padding bits must be zero." );
		}
	}
}
