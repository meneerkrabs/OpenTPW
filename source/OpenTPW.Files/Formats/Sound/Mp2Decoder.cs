namespace OpenTPW;

/// <summary>
/// Decoded PCM from <see cref="Mp2Decoder"/>: interleaved signed 16-bit samples.
/// </summary>
public sealed record Mp2Audio( int SampleRate, int Channels, short[] Samples, int FrameCount, int TrailingBytes )
{
	public int SampleFrames => Samples.Length / Channels;
	public double DurationSeconds => (double)SampleFrames / SampleRate;
}

/// <summary>
/// Clean-room MPEG Audio decoder for TPW raw SDT frame streams: MPEG-1/2 Layer I
/// and MPEG-2 (LSF) Layer II, using the shared 32-band polyphase synthesis filterbank.
/// Layer I accepts mono, stereo, dual-channel and intensity joint stereo. Layer II
/// accepts the mono/stereo modes already verified against the TPW corpus. MPEG-1
/// Layer II, free format, Layer III and MPEG-2.5 are rejected. CRC is skipped rather
/// than verified; Layer I emphasis other than none is unsupported. See docs/LIPS.md.
/// </summary>
public static partial class Mp2Decoder
{
	public const int MaximumInputBytes = 16 * 1024 * 1024;
	public const int SamplesPerFrame = 1152; // Layer II, retained for existing callers.
	public const int LayerOneSamplesPerFrame = 384;

	private static readonly int[] Mpeg2Bitrates = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };
	private static readonly int[] Mpeg2SampleRates = { 22050, 24000, 16000 };

	// Quantization classes (ISO/IEC 11172-3 Table 3-B.4): levels and whether three
	// samples share one grouped codeword. Class 0 means "no allocation".
	private static readonly int[] ClassLevels = { 0, 3, 5, 7, 9, 15, 31, 63, 127, 255, 511, 1023, 2047, 4095, 8191, 16383, 32767, 65535 };
	private static readonly int[] ClassBits = { 0, 5, 7, 3, 10, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

	// MPEG-2 LSF allocation (ISO/IEC 13818-3 Layer II): (subband count, nbal bits, class
	// per allocation code). The 7-level entry in the first row was confirmed by the TPW
	// corpus: every frame of the 1,089 Layer II streams consumes its payload to within
	// 23 spare bits, which no tested alternative row achieves.
	// [DATA:speechHD.SDT/MusicHD.sdt:all Layer II frames fit this table to ≤23 spare bits]
	private static readonly (int Count, int Bits, int[] Classes)[] AllocationTable =
	{
		(4, 4, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }),
		(7, 3, new[] { 0, 1, 2, 4, 5, 6, 7, 8 }),
		(19, 2, new[] { 0, 1, 2, 4 }),
	};
	private const int SubbandLimit = 30;

	// ISO/IEC 11172-3 Table 3-B.3 synthesis window D[0..256], in units of 2^-16
	// (the standard's nine-decimal values are exact multiples of 1/65536).
	// [APPROX:ADVISOR-014] Values read from the locally installed ffmpeg's data table; checked against the standard's
	// D[1] and D[256] and by ≤1 LSB corpus output vs ffmpeg — evidence needed: full comparison with the published standard text
	private static readonly int[] HalfWindow =
	{
		0, -1, -1, -1, -1, -1, -1, -2, -2, -2, -2, -3, -3, -4, -4, -5,
		-5, -6, -7, -7, -8, -9, -10, -11, -13, -14, -16, -17, -19, -21, -24, -26,
		-29, -31, -35, -38, -41, -45, -49, -53, -58, -63, -68, -73, -79, -85, -91, -97,
		-104, -111, -117, -125, -132, -139, -147, -154, -161, -169, -176, -183, -190, -196, -202, -208,
		213, 218, 222, 225, 227, 228, 228, 227, 224, 221, 215, 208, 200, 189, 177, 163,
		146, 127, 106, 83, 57, 29, -2, -36, -72, -111, -153, -197, -244, -294, -347, -401,
		-459, -519, -581, -645, -711, -779, -848, -919, -991, -1064, -1137, -1210, -1283, -1356, -1428, -1498,
		-1567, -1634, -1698, -1759, -1817, -1870, -1919, -1962, -2001, -2032, -2057, -2075, -2085, -2087, -2080, -2063,
		2037, 2000, 1952, 1893, 1822, 1739, 1644, 1535, 1414, 1280, 1131, 970, 794, 605, 402, 185,
		-45, -288, -545, -814, -1095, -1388, -1692, -2006, -2330, -2663, -3004, -3351, -3705, -4063, -4425, -4788,
		-5153, -5517, -5879, -6237, -6589, -6935, -7271, -7597, -7910, -8209, -8491, -8755, -8998, -9219, -9416, -9585,
		-9727, -9838, -9916, -9959, -9966, -9935, -9863, -9750, -9592, -9389, -9139, -8840, -8492, -8092, -7640, -7134,
		6574, 5959, 5288, 4561, 3776, 2935, 2037, 1082, 70, -998, -2122, -3300, -4533, -5818, -7154, -8540,
		-9975, -11455, -12980, -14548, -16155, -17799, -19478, -21189, -22929, -24694, -26482, -28289, -30112, -31947, -33791, -35640,
		-37489, -39336, -41176, -43006, -44821, -46617, -48390, -50137, -51853, -53534, -55178, -56778, -58333, -59838, -61289, -62684,
		-64019, -65290, -66494, -67629, -68692, -69679, -70590, -71420, -72169, -72835, -73415, -73908, -74313, -74630, -74856, -74992,
		75038
	};

	private static readonly float[] Window = BuildWindow();
	private static readonly float[] Matrix = BuildMatrix();
	private static readonly float[] ScaleFactors = Enumerable.Range( 0, 63 ).Select( index => (float)Math.Pow( 2.0, 1.0 - index / 3.0 ) ).ToArray();

	private static float[] BuildWindow()
	{
		var window = new float[512];
		for ( var index = 0; index <= 256; index++ )
		{
			var value = HalfWindow[index] / 65536f;
			window[index] = value;
			if ( index is > 0 and < 256 )
				window[512 - index] = (index & 63) == 0 ? value : -value;
		}
		return window;
	}

	private static float[] BuildMatrix()
	{
		var matrix = new float[64 * 32];
		for ( var row = 0; row < 64; row++ )
			for ( var column = 0; column < 32; column++ )
				matrix[row * 32 + column] = (float)Math.Cos( (16 + row) * (2 * column + 1) * Math.PI / 64.0 );
		return matrix;
	}

	public static Mp2Audio Decode( ReadOnlySpan<byte> data )
	{
		if ( data.Length > MaximumInputBytes )
			throw new InvalidDataException( "MP2 stream exceeds the input byte limit." );
		var output = new List<short>();
		var synthesis = new[] { new Synthesis(), new Synthesis() };
		var offset = 0;
		var frames = 0;
		int sampleRate = 0, channels = 0, layer = 0;
		while ( data.Length - offset >= 4 )
		{
			var header = Header.Parse( data[offset..] );
			if ( frames == 0 )
				(sampleRate, channels, layer) = (header.SampleRate, header.Channels, header.Layer);
			else if ( header.SampleRate != sampleRate || header.Channels != channels || header.Layer != layer )
				throw new NotSupportedException( $"MP2 frame {frames} changes sample rate, channel count or layer." );
			if ( header.FrameBytes > data.Length - offset )
				break;
			if ( header.Layer == 1 )
				DecodeLayerOneFrame( header, data.Slice( offset, header.FrameBytes ), synthesis, output );
			else
				DecodeFrame( header, data.Slice( offset, header.FrameBytes ), synthesis, output );
			offset += header.FrameBytes;
			frames++;
		}
		if ( frames == 0 )
			throw new InvalidDataException( "MP2 stream contains no complete frame." );
		return new Mp2Audio( sampleRate, channels, output.ToArray(), frames, data.Length - offset );
	}

	/// <summary>
	/// Read format metadata using the decode header rules without decoding PCM.
	/// Require a complete first frame; unsupported or damaged entries can still be
	/// listed by an archive reader and will be rejected separately during Decode.
	/// </summary>
	internal static bool TryReadFrameFormat( ReadOnlySpan<byte> data, out int sampleRate, out int channels )
	{
		sampleRate = channels = 0;
		if ( data.Length < 4 )
			return false;
		try
		{
			var header = Header.Parse( data );
			if ( header.FrameBytes > data.Length )
				return false;
			(sampleRate, channels) = (header.SampleRate, header.Channels);
			return true;
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException )
		{
			return false;
		}
	}

	private readonly record struct Header( bool Crc, int SampleRate, int Mode, int ModeExtension, int Layer, int FrameBytes )
	{
		public int Channels => Mode == 3 ? 1 : 2;

		public static Header Parse( ReadOnlySpan<byte> data )
		{
			var word = (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);
			if ( (word & 0xFFE00000) != 0xFFE00000 )
				throw new InvalidDataException( "MP2 frame sync word is missing." );
			var version = (int)(word >> 19) & 3;
			var layer = (int)(word >> 17) & 3;
			if ( version == 1 )
				throw new InvalidDataException( "MP2 frame uses the reserved MPEG version." );
			if ( layer == 0 )
				throw new InvalidDataException( "MPEG frame uses the reserved layer." );
			if ( version == 0 )
				throw new NotSupportedException( "MPEG-2.5 audio is not supported." );
			if ( layer is not (2 or 3) )
				throw new NotSupportedException( "MPEG Audio Layer III is not supported." );
			if ( layer == 2 && version != 2 )
				throw new NotSupportedException( "Only MPEG-2 (LSF) Layer II is supported." );
			var bitrateIndex = (int)(word >> 12) & 15;
			var rateIndex = (int)(word >> 10) & 3;
			if ( bitrateIndex == 0 )
				throw new NotSupportedException( "Free-format MPEG audio is not supported." );
			if ( bitrateIndex == 15 || rateIndex == 3 )
				throw new InvalidDataException( "MP2 frame header uses a reserved bitrate or sample rate." );
			var bitrate = layer == 3 ? LayerOneBitrates[version == 3 ? 1 : 0][bitrateIndex] : Mpeg2Bitrates[bitrateIndex];
			var sampleRate = Mpeg2SampleRates[rateIndex] * (version == 3 ? 2 : 1);
			var mode = (int)(word >> 6) & 3;
			if ( mode == 1 && layer == 2 )
				throw new NotSupportedException( "Joint-stereo MPEG audio is not supported." );
			var padding = (int)(word >> 9) & 1;
			if ( layer == 3 && (word & 3) == 2 )
				throw new InvalidDataException( "Layer I frame uses reserved emphasis." );
			if ( layer == 3 && (word & 3) != 0 )
				throw new NotSupportedException( "Layer I de-emphasis is not supported." );
			var frameBytes = layer == 3
				? (12 * bitrate * 1000 / sampleRate + padding) * 4
				: 144 * bitrate * 1000 / sampleRate + padding;
			return new Header( (word & 0x10000) == 0, sampleRate, mode, (int)(word >> 4) & 3,
				layer == 3 ? 1 : 2, frameBytes );
		}
	}

	private ref struct Bits
	{
		private readonly ReadOnlySpan<byte> data;
		private int position;

		public Bits( ReadOnlySpan<byte> data, int bitPosition )
		{
			this.data = data;
			position = bitPosition;
		}

		public int Read( int count )
		{
			var value = 0;
			for ( var index = 0; index < count; index++ )
			{
				var byteIndex = position >> 3;
				if ( byteIndex >= data.Length )
					throw new InvalidDataException( "MP2 frame payload is truncated." );
				value = value << 1 | (data[byteIndex] >> (7 - (position & 7))) & 1;
				position++;
			}
			return value;
		}
	}

	private sealed class Synthesis
	{
		private readonly float[] v = new float[1024];
		private int start;

		public void Run( ReadOnlySpan<float> subbands, Span<float> pcm )
		{
			start = (start - 64) & 1023;
			for ( var row = 0; row < 64; row++ )
			{
				var sum = 0f;
				var matrixRow = row * 32;
				for ( var column = 0; column < 32; column++ )
					sum += Matrix[matrixRow + column] * subbands[column];
				v[(start + row) & 1023] = sum;
			}
			for ( var sample = 0; sample < 32; sample++ )
			{
				var sum = 0f;
				for ( var block = 0; block < 8; block++ )
				{
					var windowIndex = block * 64 + sample;
					sum += v[(start + block * 128 + sample) & 1023] * Window[windowIndex];
					sum += v[(start + block * 128 + 96 + sample) & 1023] * Window[windowIndex + 32];
				}
				pcm[sample] = sum;
			}
		}
	}

	private static void DecodeFrame( Header header, ReadOnlySpan<byte> frame, Synthesis[] synthesis, List<short> output )
	{
		var bits = new Bits( frame, header.Crc ? 48 : 32 );
		var channels = header.Channels;

		var classes = new int[2, 32];
		var subband = 0;
		foreach ( var row in AllocationTable )
			for ( var count = 0; count < row.Count; count++, subband++ )
				for ( var channel = 0; channel < channels; channel++ )
					classes[channel, subband] = row.Classes[bits.Read( row.Bits )];

		var selection = new int[2, 32];
		for ( subband = 0; subband < SubbandLimit; subband++ )
			for ( var channel = 0; channel < channels; channel++ )
				if ( classes[channel, subband] != 0 )
					selection[channel, subband] = bits.Read( 2 );

		var factors = new float[2, 32, 3];
		for ( subband = 0; subband < SubbandLimit; subband++ )
		{
			for ( var channel = 0; channel < channels; channel++ )
			{
				if ( classes[channel, subband] == 0 )
					continue;
				int first, second, third;
				switch ( selection[channel, subband] )
				{
					case 0: first = ReadFactor( ref bits ); second = ReadFactor( ref bits ); third = ReadFactor( ref bits ); break;
					case 1: first = second = ReadFactor( ref bits ); third = ReadFactor( ref bits ); break;
					case 2: first = second = third = ReadFactor( ref bits ); break;
					default: first = ReadFactor( ref bits ); second = third = ReadFactor( ref bits ); break;
				}
				factors[channel, subband, 0] = ScaleFactors[first];
				factors[channel, subband, 1] = ScaleFactors[second];
				factors[channel, subband, 2] = ScaleFactors[third];
			}
		}

		Span<float> samples = stackalloc float[2 * 3 * 32];
		Span<int> codes = stackalloc int[3];
		var frameStart = output.Count;
		for ( var granule = 0; granule < 12; granule++ )
		{
			samples.Clear();
			var part = granule / 4;
			for ( subband = 0; subband < SubbandLimit; subband++ )
			{
				for ( var channel = 0; channel < channels; channel++ )
				{
					var quantClass = classes[channel, subband];
					if ( quantClass == 0 )
						continue;
					var levels = ClassLevels[quantClass];
					var width = ClassBits[quantClass];
					if ( quantClass is 1 or 2 or 4 )
					{
						var code = bits.Read( width );
						for ( var index = 0; index < 3; index++ )
						{
							codes[index] = code % levels;
							code /= levels;
						}
						if ( code != 0 )
							throw new InvalidDataException( "MP2 grouped sample code is out of range." );
					}
					else
					{
						for ( var index = 0; index < 3; index++ )
							codes[index] = bits.Read( width );
					}
					for ( var index = 0; index < 3; index++ )
						samples[(channel * 3 + index) * 32 + subband] = (2 * codes[index] + 1 - levels) / (float)levels * factors[channel, subband, part];
				}
			}
			for ( var index = 0; index < 3; index++ )
				AppendSynthesisSlot( samples[(index * 32)..], channels, 3 * 32, synthesis, output );
		}
		if ( output.Count - frameStart != SamplesPerFrame * channels )
			throw new InvalidOperationException( "MP2 frame produced an unexpected sample count." );
	}

	private static void AppendSynthesisSlot( ReadOnlySpan<float> samples, int channels, int channelStride,
		Synthesis[] synthesis, List<short> output )
	{
		Span<float> pcm = stackalloc float[32];
		var slotStart = output.Count;
		for ( var sample = 0; sample < 32 * channels; sample++ )
			output.Add( 0 );
		for ( var channel = 0; channel < channels; channel++ )
		{
			synthesis[channel].Run( samples.Slice( channel * channelStride, 32 ), pcm );
			for ( var sample = 0; sample < 32; sample++ )
				output[slotStart + sample * channels + channel] = (short)Math.Clamp(
					MathF.Round( pcm[sample] * 32768f ), short.MinValue, short.MaxValue );
		}
	}

	private static int ReadFactor( ref Bits bits )
	{
		var index = bits.Read( 6 );
		if ( index == 63 )
			throw new InvalidDataException( "MP2 scalefactor index 63 is reserved." );
		return index;
	}
}
