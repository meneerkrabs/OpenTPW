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
		ConvertPixels( rgb, 3 );
		return rgb;
	}

	/// <summary>Same conversion as <see cref="ToRgb24"/>, written as opaque RGBA (4 bytes per pixel) into <paramref name="rgba"/>.</summary>
	public void WriteRgba32( Span<byte> rgba )
	{
		if ( rgba.Length < Width * Height * 4 )
			throw new ArgumentException( "RGBA buffer is too small for the frame.", nameof( rgba ) );
		ConvertPixels( rgba, 4 );
	}

	private void ConvertPixels( Span<byte> output, int pixelBytes )
	{
		var chromaWidth = Width / 2;
		for ( var row = 0; row < Height; row++ )
		{
			for ( var column = 0; column < Width; column++ )
			{
				var luma = (double)Y[row * Width + column];
				var chroma = (row / 2) * chromaWidth + column / 2;
				var blue = Cb[chroma] - 128.0;
				var red = Cr[chroma] - 128.0;
				var offset = (row * Width + column) * pixelBytes;
				output[offset] = ClampByte( luma + 1.402 * red );
				output[offset + 1] = ClampByte( luma - 0.344136 * blue - 0.714136 * red );
				output[offset + 2] = ClampByte( luma + 1.772 * blue );
				if ( pixelBytes == 4 )
					output[offset + 3] = 255;
			}
		}
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

	// Integer AAN prescale: round(2^17 * s(u) * s(v)) with s(0) = 1/(2*sqrt 2) and s(k) = 1/(4*cos(k*pi/16)),
	// natural order. Folding these into dequantisation leaves only the four rotations below for the transform.
	private static readonly int[] AanScale =
	{
		16384, 11812, 12540, 13933, 16384, 20853, 30274, 59384,
		11812, 8516, 9041, 10045, 11812, 15034, 21826, 42813,
		12540, 9041, 9598, 10664, 12540, 15960, 23170, 45451,
		13933, 10045, 10664, 11849, 13933, 17734, 25746, 50502,
		16384, 11812, 12540, 13933, 16384, 20853, 30274, 59384,
		20853, 15034, 15960, 17734, 20853, 26541, 38531, 75581,
		30274, 21826, 23170, 25746, 30274, 38531, 55938, 109727,
		59384, 42813, 45451, 50502, 59384, 75581, 109727, 215238,
	};

	// Rotation constants of the AAN flowgraph in 12-bit fixed point: cos(pi/4), cos(pi/8) - cos(3pi/8),
	// cos(pi/8) + cos(3pi/8), cos(3pi/8).
	private const long A1 = 2896, A2 = 2217, A4 = 5352, A5 = 1567;
	private const int ConstantBits = 12;
	// Coefficients carry 6 fraction bits; the first (column) pass drops one, the row pass output keeps five.
	private const int FractionBits = 6;
	private const int FinalShift = FractionBits - 1;
	// Empirical quarter-LSB rounding bias; it best matches the external oracle (see docs/TGQ-MOVIES.md).
	private const int FinalBias = 1 << (FinalShift - 2);

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
		var dequant = BuildDequantisation( header.Quantizer );

		var y = new byte[width * height];
		var cb = new byte[width * height / 4];
		var cr = new byte[width * height / 4];
		var reader = new BitReader( payload[HeaderBytes..] );
		var predictors = new int[3];
		var coefficients = new long[64];
		for ( var macroY = 0; macroY < height / 16; macroY++ )
		{
			for ( var macroX = 0; macroX < width / 16; macroX++ )
			{
				for ( var block = 0; block < 6; block++ )
				{
					var component = block < 4 ? 0 : block - 3;
					DecodeBlock( ref reader, component, ref predictors[component], dequant, coefficients );
					InverseTransform( coefficients );
					if ( block < 4 )
						Store( coefficients, y, width, macroX * 16 + (block & 1) * 8, macroY * 16 + (block >> 1) * 8 );
					else
						Store( coefficients, block == 4 ? cb : cr, width / 2, macroX * 8, macroY * 8 );
				}
			}
		}
		reader.RequireZeroTail();
		return new TqiFrame( width, height, y, cb, cr );
	}

	private static void DecodeBlock( ref BitReader reader, int component, ref int predictor, ReadOnlySpan<long> dequant, Span<long> coefficients )
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
		coefficients[0] = (long)predictor << FractionBits;
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

	/// <summary>
	/// Per-position dequantisation factors with the AAN prescale folded in, in units of 2^-6 pixel:
	/// floor(AanScale * W * qscale / 2^18) where qscale = (215 - 2q) * 5, i.e. W * (107.5 - q) * 0.625 / 8.
	/// </summary>
	private static long[] BuildDequantisation( int quantizer )
	{
		var qscale = (215L - 2 * quantizer) * 5;
		var dequant = new long[64];
		for ( var index = 1; index < 64; index++ )
			dequant[index] = AanScale[index] * IntraMatrix[index] * qscale >> (24 - FractionBits);
		return dequant;
	}

	/// <summary>
	/// Integer 8x8 inverse DCT: the transposed Arai-Agui-Nakajima flowgraph (adjoint of the classic AAN forward
	/// DCT), columns first with a one-bit floor shift between passes, floor products, then (x + 8) >> 5.
	/// Selected empirically against an external oracle; it is closer than an exact float IDCT but not bit-exact.
	/// </summary>
	private static void InverseTransform( Span<long> block )
	{
		for ( var column = 0; column < 8; column++ )
			Transform1D( block, column, 8, 1 );
		for ( var row = 0; row < 8; row++ )
			Transform1D( block, row * 8, 1, 0 );
	}

	private static void Transform1D( Span<long> data, int start, int stride, int shift )
	{
		var d0 = data[start];
		var d1 = data[start + stride];
		var d2 = data[start + 2 * stride];
		var d3 = data[start + 3 * stride];
		var d4 = data[start + 4 * stride];
		var d5 = data[start + 5 * stride];
		var d6 = data[start + 6 * stride];
		var d7 = data[start + 7 * stride];

		// Even part.
		var sum04 = d0 + d4;
		var difference04 = d0 - d4;
		var rotated26 = Multiply( d2 - d6, A1 );
		var sum26 = d2 + d6 + rotated26;
		var even0 = sum04 + sum26;
		var even3 = sum04 - sum26;
		var even1 = difference04 + rotated26;
		var even2 = difference04 - rotated26;

		// Odd part.
		var z13 = d5 + d3;
		var z2 = d5 - d3;
		var z11 = d1 + d7;
		var z4 = d1 - d7;
		var odd7 = z11 + z13;
		var odd11 = Multiply( z11 - z13, A1 );
		var z5 = Multiply( z2 + z4, A5 );
		var odd10 = Multiply( z2, A2 ) + z5;
		var odd12 = Multiply( z4, A4 ) - z5;
		var tap4 = odd10;
		var tap5 = odd10 + odd11;
		var tap6 = odd11 + odd12;
		var tap7 = odd12 + odd7;

		data[start] = (even0 + tap7) >> shift;
		data[start + stride] = (even1 + tap6) >> shift;
		data[start + 2 * stride] = (even2 + tap5) >> shift;
		data[start + 3 * stride] = (even3 + tap4) >> shift;
		data[start + 4 * stride] = (even3 - tap4) >> shift;
		data[start + 5 * stride] = (even2 - tap5) >> shift;
		data[start + 6 * stride] = (even1 - tap6) >> shift;
		data[start + 7 * stride] = (even0 - tap7) >> shift;
	}

	private static long Multiply( long value, long constant ) => value * constant >> ConstantBits;

	private static void Store( ReadOnlySpan<long> samples, byte[] plane, int stride, int left, int top )
	{
		for ( var row = 0; row < 8; row++ )
			for ( var column = 0; column < 8; column++ )
				plane[(top + row) * stride + left + column] = (byte)Math.Clamp( (samples[row * 8 + column] + FinalBias) >> FinalShift, 0, 255 );
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
