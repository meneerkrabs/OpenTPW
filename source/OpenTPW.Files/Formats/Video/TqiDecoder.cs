using System.Buffers.Binary;

namespace OpenTPW;

public readonly record struct TqiFrameHeader( int Width, int Height, byte Quantizer, int UnknownBytes );

/// <summary>
/// Decoded TQI picture: the original decoder's 7-bit planar 4:2:0 samples (Y full size, Cb/Cr half size in both axes; 0–127,
/// 64 the chroma zero), clamped as its colour tables clamp them.
/// </summary>
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

	/// <summary>Converts to packed RGB24 with the original player's colour tables (<see cref="TqiColour"/>).</summary>
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
				var chroma = (row / 2) * chromaWidth + column / 2;
				var (red, green, blue) = TqiColour.Convert( Y[row * Width + column], Cb[chroma], Cr[chroma] );
				var offset = (row * Width + column) * pixelBytes;
				output[offset] = red;
				output[offset + 1] = green;
				output[offset + 2] = blue;
				if ( pixelBytes == 4 )
					output[offset + 3] = 255;
			}
		}
	}
}

/// <summary>
/// The original movie player's colour conversion (tp.exe 0x670350, row writers 0x675D90 and its kin): three packed tables add
/// a pixel's luma, blue-difference and red-difference terms into three 9-bit fields (blue at bit 0, green at bit 11, red at
/// bit 23), and three channel tables turn each field n into an 8-bit value clamp(brightness · (2n − base) / 100), the brightness
/// the library's default 100. The result is the colour before the player packs it into the display surface's bit depth.
/// </summary>
// [BIN:TPW-EXE:0x00670350 TQI colour tables] for i = −256…255 at index (i − 192) & 511 (the writers read 256 entries further on, so a sample v is i = v − 64): luma clamp(i, −64, 63) · 0x800801 + 0x4883D8A9; with x = clamp + 0.5 (fsub −0.5) the Cb entry (trunc(0.5 − x · 0.3441) & 511) << 11 | trunc(0.5 + x · 1.772) & 511 and the Cr entry ((trunc(0.5 − x · 0.7141) & 511) + (trunc(0.5 + x · 1.402) & 511) << 12) << 11 (_ftol 0x67A830 truncates); the channel tables run c = −177, −133, −225 upward in steps of 2 for 306, 262 and 354 entries
// [BIN:TPW-EXE:0x006701A2 TQI brightness] the open sets the brightness word (+0x78) to 100 unless its flags carry 0x100000
public static class TqiColour
{
	private static readonly uint[] LumaTerms = new uint[TqiDecoder.MaximumSample + 1];
	private static readonly uint[] BlueTerms = new uint[TqiDecoder.MaximumSample + 1];
	private static readonly uint[] RedTerms = new uint[TqiDecoder.MaximumSample + 1];

	/// <summary>The original's default brightness (percent).</summary>
	public const int Brightness = 100;

	static TqiColour()
	{
		for ( var sample = 0; sample <= TqiDecoder.MaximumSample; sample++ )
		{
			var centred = sample - 64;
			var x = centred + 0.5;
			LumaTerms[sample] = unchecked((uint)(centred * 0x800801 + 0x4883D8A9));
			BlueTerms[sample] = (Truncate( 0.5 - x * 0.3441 ) << 11) + Truncate( 0.5 - x * -1.772 );
			RedTerms[sample] = (Truncate( 0.5 - x * 0.7141 ) + (Truncate( 0.5 - x * -1.402 ) << 12)) << 11;
		}
	}

	private static uint Truncate( double value ) => (uint)(int)value & 0x1FF;

	/// <summary>One pixel: 7-bit Y, Cb and Cr samples (0–127) to 8-bit red, green and blue.</summary>
	public static (byte Red, byte Green, byte Blue) Convert( byte y, byte cb, byte cr )
	{
		var sum = unchecked(LumaTerms[y] + BlueTerms[cb] + RedTerms[cr]);
		return (Channel( sum >> 23, -177 ), Channel( (sum >> 11) & 0x1FF, -133 ), Channel( sum & 0x1FF, -225 ));
	}

	private static byte Channel( uint field, int start ) => (byte)Math.Clamp( Brightness * (start + 2 * (int)field) / 100, 0, 255 );
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

	// [BIN:TPW-EXE:0x00705C88 TQI prescale] the AAN prescale s(u)·s(v) in Q13 (s(0) = 1, s(k) = 1/(2·cos(k·π/16)) · √2…), natural order
	private static readonly int[] PrescaleQ13 =
	{
		8192, 5906, 6270, 6967, 8192, 10426, 15137, 29692,
		5906, 4258, 4520, 5023, 5906, 7517, 10913, 21407,
		6270, 4520, 4799, 5332, 6270, 7980, 11585, 22725,
		6967, 5023, 5332, 5925, 6967, 8867, 12873, 25251,
		8192, 5906, 6270, 6967, 8192, 10426, 15137, 29692,
		10426, 7517, 7980, 8867, 10426, 13270, 19266, 37791,
		15137, 10913, 11585, 12873, 15137, 19266, 27969, 54864,
		29692, 21407, 22725, 25251, 29692, 37791, 54864, 107619,
	};

	// [BIN:TPW-EXE:0x00FBB5B0 TQI scan] the block decoder 0x6747D0 counts a byte from 63 down and stores coefficient 63 − n at
	// table[n]: MPEG-1's zigzag transposed, while the weights it multiplies by are indexed by that same transposed position
	private static readonly byte[] TransposedScan = ZigZag.Select( position => (byte)((position & 7) * 8 + (position >> 3)) ).ToArray();

	// [BIN:TPW-EXE:0x00FBB598 TQI transform constants] √2/2 in Q31 (taken as the doubled high dword of a 32×32 product) and the odd
	// part's three single-precision factors; 1.5 · 2^52 rounds a double to an integer, half to even, through its low dword
	private const int HalfSqrt2Q31 = 0x5A82799A;
	private const float OddFactorA = 0.5411961f;
	private const float OddFactorB = 1.306563f;
	private const float OddFactorZ = 0.3826834f;
	private const double RoundingMagic = 6755399441055744.0;
	/// <summary>The second pass shifts each sample right by 17 without a bias: the samples are 7-bit (0–127, 64 the chroma zero).</summary>
	private const int SampleShift = 17;
	/// <summary>The largest sample the colour tables distinguish; they clamp anything outside 0–127.</summary>
	public const int MaximumSample = 127;

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
		var (header, y, cb, cr) = DecodeSamples( payload );
		return new TqiFrame( header.Width, header.Height, Clamp( y ), Clamp( cb ), Clamp( cr ) );
	}

	/// <summary>
	/// The original decoder's samples before its colour tables, unclamped (tp.exe frame decode 0x670C20, case 4): the matrix of
	/// <see cref="BuildMatrix"/>, the block decoder 0x6747D0 and the two transform passes 0x67673C/0x676965, per 16×16 macroblock
	/// four luma blocks (left to right, top to bottom) and one Cb and one Cr block.
	/// </summary>
	internal static (TqiFrameHeader Header, int[] Y, int[] Cb, int[] Cr) DecodeSamples( ReadOnlySpan<byte> payload )
	{
		var header = ReadHeader( payload );
		var width = header.Width;
		var height = header.Height;
		var matrix = BuildMatrix( header.Quantizer );

		var y = new int[width * height];
		var cb = new int[width * height / 4];
		var cr = new int[width * height / 4];
		var reader = new BitReader( payload[HeaderBytes..] );
		var predictors = new int[3];
		var coefficients = new int[64];
		var samples = new int[64];
		for ( var macroY = 0; macroY < height / 16; macroY++ )
		{
			for ( var macroX = 0; macroX < width / 16; macroX++ )
			{
				for ( var block = 0; block < 6; block++ )
				{
					var component = block < 4 ? 0 : block - 3;
					DecodeBlock( ref reader, component, ref predictors[component], matrix, coefficients );
					InverseTransform( coefficients, samples );
					if ( block < 4 )
						Store( samples, y, width, macroX * 16 + (block & 1) * 8, macroY * 16 + (block >> 1) * 8 );
					else
						Store( samples, block == 4 ? cb : cr, width / 2, macroX * 8, macroY * 8 );
				}
			}
		}
		reader.RequireZeroTail();
		return (header, y, cb, cr);
	}

	private static byte[] Clamp( int[] samples ) => samples.Select( sample => (byte)Math.Clamp( sample, 0, MaximumSample ) ).ToArray();

	private static void DecodeBlock( ref BitReader reader, int component, ref int predictor, ReadOnlySpan<int> matrix, Span<int> coefficients )
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
		// [BIN:TPW-EXE:0x006747D0 TQI block decoder] the DC is the predictor times the matrix's first entry (2^16), each AC level
		// times the matrix entry at its transposed scan position, plain 32-bit products
		coefficients[0] = unchecked(predictor * matrix[0]);
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
			var stored = TransposedScan[position];
			coefficients[stored] = unchecked(level * matrix[stored]);
		}
	}

	/// <summary>
	/// The dequantisation matrix of a frame's quantiser q, the AAN prescale folded in (tp.exe 0x6710C0): for q = 100 eight times
	/// the prescale; otherwise each MPEG-1 intra weight W times (float)((107 − q) / 16) times 2^16, truncated, then times the Q13
	/// prescale with the product's low 16 bits rounded half up; the first entry is always 2^16. Indexed by the stored position.
	/// </summary>
	// [BIN:TPW-EXE:0x006710C0 TQI matrix] (107.0 − q) · 0.0625 is stored as a float, W · f · 65536.0 goes through _ftol (truncation, 0x67A830), and imul/shr 16/adc rounds S · t; M[0] = 8192 · 8 · 2^16 / 2^16
	internal static int[] BuildMatrix( int quantizer )
	{
		var matrix = new int[64];
		var factor = (float)((107.0 - quantizer) * 0.0625);
		for ( var index = 0; index < 64; index++ )
		{
			long weight = quantizer == 100 ? 0x80000 : (long)((double)IntraMatrix[index] * factor * 65536.0);
			matrix[index] = unchecked((int)((PrescaleQ13[index] * weight + 0x8000) >> 16));
		}
		matrix[0] = 0x10000;
		return matrix;
	}

	/// <summary>
	/// The 8×8 inverse transform of tp.exe's two passes over the stored (transposed) coefficients: the first (0x67673C) runs along
	/// each stored row, copies the first input to all eight outputs when the other seven are zero, and writes its outputs down a
	/// column; the second (0x676965) runs along each of those columns and shifts every output right by 17. The two transposes
	/// cancel the transposed scan, so the samples come out in natural order.
	/// </summary>
	private static void InverseTransform( ReadOnlySpan<int> coefficients, Span<int> samples )
	{
		Span<int> columns = stackalloc int[64];
		Span<int> outputs = stackalloc int[8];
		for ( var row = 0; row < 8; row++ )
		{
			var input = coefficients.Slice( row * 8, 8 );
			if ( (input[1] | input[2] | input[3] | input[4] | input[5] | input[6] | input[7]) == 0 )
				outputs.Fill( input[0] );
			else
				Butterfly( input, outputs );
			for ( var index = 0; index < 8; index++ )
				columns[index * 8 + row] = outputs[index];
		}
		for ( var line = 0; line < 8; line++ )
		{
			Butterfly( columns.Slice( line * 8, 8 ), outputs );
			for ( var index = 0; index < 8; index++ )
				samples[line * 8 + index] = outputs[index] >> SampleShift;
		}
	}

	/// <summary>
	/// One pass of tp.exe's AAN butterfly over x0…x7 (0x67673C, 0x676965): integer adds, the √2/2 rotations as doubled high dwords of
	/// Q31 products, and the odd part's rotation in double precision, each result rounded half to even by the 1.5 · 2^52 trick.
	/// </summary>
	// [BIN:TPW-EXE:0x0067677E TQI odd part] z10 = x5 − x3 and z12 = x1 − x7 are loaded with fild; a = z10 · 0.5411961 + (z10 + z12) · 0.3826834 and b = z12 · 1.306563 − (z10 + z12) · 0.3826834 are each rounded through fadd 1.5·2^52 and the low dword of an fstp double (the FPU in at least 53-bit precision)
	private static void Butterfly( ReadOnlySpan<int> x, Span<int> output )
	{
		unchecked
		{
			var z13 = x[3] + x[5];
			var z10 = x[5] - x[3];
			var z11 = x[7] + x[1];
			var z12 = x[1] - x[7];
			var t7 = z13 + z11;
			var z5 = ((double)z12 + z10) * OddFactorZ;
			var a = RoundHalfEven( z10 * (double)OddFactorA + z5 );
			var b = RoundHalfEven( z12 * (double)OddFactorB - z5 );
			var r11 = HalfSqrt2( z11 - z13 );
			var p = a + r11;
			var q = r11 + b;
			var w = b + t7;

			var t13 = x[6] + x[2];
			var t10 = x[4] + x[0];
			var t11 = x[0] - x[4];
			var r = HalfSqrt2( x[2] - x[6] );
			var e = t13 + r;
			var plus = r + t11;
			var minus = t11 - r;
			output[0] = (e + t10) + w;
			output[1] = q + plus;
			output[2] = minus + p;
			output[3] = (t10 - e) + a;
			output[4] = (t10 - e) - a;
			output[5] = minus - p;
			output[6] = plus - q;
			output[7] = (e + t10) - w;
		}
	}

	private static int HalfSqrt2( int value ) => unchecked((int)(((long)value * HalfSqrt2Q31) >> 32) << 1);

	private static int RoundHalfEven( double value ) => unchecked((int)BitConverter.DoubleToInt64Bits( value + RoundingMagic ));

	private static void Store( ReadOnlySpan<int> samples, int[] plane, int stride, int left, int top )
	{
		for ( var row = 0; row < 8; row++ )
			for ( var column = 0; column < 8; column++ )
				plane[(top + row) * stride + left + column] = samples[row * 8 + column];
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
