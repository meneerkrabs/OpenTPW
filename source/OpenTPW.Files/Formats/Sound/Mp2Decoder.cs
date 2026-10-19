namespace OpenTPW;

/// <summary>
/// Decoded PCM from <see cref="Mp2Decoder"/>: interleaved signed 16-bit samples.
/// </summary>
public sealed record Mp2Audio( int SampleRate, int Channels, short[] Samples, int FrameCount, int TrailingBytes,
	int CrcChecks = 0, int CrcFailures = 0, int Emphasis = 0 )
{
	public int SampleFrames => Samples.Length / Channels;
	public double DurationSeconds => (double)SampleFrames / SampleRate;

	/// <summary>
	/// The audio cut to <paramref name="playbackBytes"/> bytes of 16-bit PCM (whole sample frames), the length an SDT entry's
	/// header gives (<see cref="SdtEntryHeader.PlaybackBytes"/>); unchanged when null or not shorter than the decoded audio.
	/// </summary>
	public Mp2Audio TrimTo( int? playbackBytes )
	{
		if ( playbackBytes is not { } bytes || bytes < 0 )
			return this;
		var samples = bytes / (2 * Channels) * Channels;
		return samples >= Samples.Length ? this : this with { Samples = Samples[..samples] };
	}
}

/// <summary>
/// Clean-room MPEG Audio decoder for TPW raw SDT frame streams: MPEG-1/2 Layer I
/// and MPEG-1/2 Layer II, using the shared 32-band polyphase synthesis filterbank.
/// Layer I accepts mono, stereo, dual-channel and intensity joint stereo. Layer II
/// accepts mono, stereo and dual channel, plus MPEG-1 intensity joint stereo.
/// MPEG-2 Layer II joint stereo, free format, Layer III and MPEG-2.5 are rejected. CRC check words are
/// verified when present (mismatches are counted, not concealed); a nonzero emphasis
/// indication is applied as a de-emphasis filter after synthesis. See docs/LIPS.md.
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

	// ISO/IEC 11172-3 Table 3-B.3 synthesis window D[0..256], in units of 2^-16
	// (the standard's nine-decimal values are exact multiples of 1/65536).
	// [BIN:TPW-EXE:0x0070BD08 synthesis window] the original's table of 544 floats (17 rows of 32, each row's 16 taps stored twice) holds −0.5·D[32·tap+row] for these values (read by 0x6C9270); the whole table was compared with this array and its mirrored extension
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

	public static Mp2Audio Decode( ReadOnlySpan<byte> data ) => Decode( data, null );

	/// <summary>Decodes, keeping at most <paramref name="playbackBytes"/> bytes of PCM (<see cref="Mp2Audio.TrimTo"/>) without a second copy.</summary>
	public static Mp2Audio Decode( ReadOnlySpan<byte> data, int? playbackBytes )
	{
		if ( data.Length > MaximumInputBytes )
			throw new InvalidDataException( "MP2 stream exceeds the input byte limit." );
		var state = new DecodeState();
		state.Process( data, int.MaxValue );
		return state.Finish( data.Length, playbackBytes );
	}

	/// <summary>Decodes the same PCM in bounded frame batches, yielding to a task between batches for threadless browser hosts.</summary>
	/// <param name="yieldBatch">Optional scheduler hook; the default uses a timer task so browser rendering and input can run.</param>
	public static async Task<Mp2Audio> DecodeCooperativelyAsync( ReadOnlyMemory<byte> data, int framesPerBatch = 4, Func<Task>? yieldBatch = null, int? playbackBytes = null )
	{
		if ( data.Length > MaximumInputBytes )
			throw new InvalidDataException( "MP2 stream exceeds the input byte limit." );
		if ( framesPerBatch is < 1 or > 8 )
			throw new ArgumentOutOfRangeException( nameof( framesPerBatch ) );
		var state = new DecodeState();
		while ( state.Process( data.Span, framesPerBatch ) )
			await (yieldBatch?.Invoke() ?? Task.Delay( 1 ));
		return state.Finish( data.Length, playbackBytes );
	}

	/// <summary>
	/// A decode that runs in slices of a time budget on the caller's own frames (the browser has no second thread, and a timer task per batch
	/// delays the result by a timer tick each): the same PCM as <see cref="Decode(ReadOnlySpan{byte}, int?)"/>.
	/// </summary>
	public sealed class SlicedDecode
	{
		private readonly DecodeState state = new();
		private readonly ReadOnlyMemory<byte> data;
		private readonly int? playbackBytes;

		public SlicedDecode( ReadOnlyMemory<byte> data, int? playbackBytes = null )
		{
			if ( data.Length > MaximumInputBytes )
				throw new InvalidDataException( "MP2 stream exceeds the input byte limit." );
			this.data = data;
			this.playbackBytes = playbackBytes;
		}

		/// <summary>The decoded audio once <see cref="Run"/> has returned true.</summary>
		public Mp2Audio? Result { get; private set; }

		/// <summary>Decodes whole MPEG frames until <paramref name="budget"/> has passed (at least one); true once everything is decoded.</summary>
		public bool Run( TimeSpan budget )
		{
			if ( Result != null )
				return true;
			var started = System.Diagnostics.Stopwatch.GetTimestamp();
			while ( state.Process( data.Span, 1 ) )
				if ( System.Diagnostics.Stopwatch.GetElapsedTime( started ) >= budget )
					return false;
			Result = state.Finish( data.Length, playbackBytes );
			return true;
		}
	}

	/// <summary>
	/// The length <see cref="Decode(ReadOnlySpan{byte}, int?)"/> would give, from the frame headers alone (no PCM is made): the sample rate and
	/// the sample frames kept. False for a stream the decoder would reject or that has no complete frame, so a caller falls back to decoding.
	/// </summary>
	public static bool TryMeasure( ReadOnlySpan<byte> data, int? playbackBytes, out int sampleRate, out int sampleFrames )
	{
		sampleRate = sampleFrames = 0;
		if ( data.Length > MaximumInputBytes )
			return false;
		try
		{
			int offset = 0, frames = 0, rate = 0, channels = 0, layer = 0, emphasis = 0;
			long samples = 0;
			while ( data.Length - offset >= 4 )
			{
				var header = Header.Parse( data[offset..] );
				if ( header.FrameBytes > data.Length - offset )
					break;
				if ( frames == 0 )
					(rate, channels, layer, emphasis) = (header.SampleRate, header.Channels, header.Layer, header.Emphasis);
				else if ( header.SampleRate != rate || header.Channels != channels || header.Layer != layer || header.Emphasis != emphasis )
					return false;
				samples += (long)(header.Layer == 1 ? LayerOneSamplesPerFrame : SamplesPerFrame) * channels;
				offset += header.FrameBytes;
				frames++;
			}
			if ( frames == 0 )
				return false;
			if ( playbackBytes is { } bytes && bytes >= 0 )
				samples = Math.Min( samples, bytes / (2 * channels) * channels );
			sampleRate = rate;
			sampleFrames = (int)(samples / channels);
			return true;
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException )
		{
			return false;
		}
	}

	private sealed class DecodeState
	{
		private readonly List<short> output = new();
		private readonly Synthesis[] synthesis = { new(), new() };
		private int offset, frames, crcChecks, crcFailures, sampleRate, channels, layer, emphasis;
		private DeEmphasis? deEmphasis;

		/// <summary>Processes at most the requested number of complete MPEG frames; true when another batch may remain.</summary>
		public bool Process( ReadOnlySpan<byte> data, int frameBudget )
		{
			for ( var batchFrame = 0; batchFrame < frameBudget && data.Length - offset >= 4; batchFrame++ )
			{
				var header = Header.Parse( data[offset..] );
				// Only complete frames establish or change the decoded stream's format.
				if ( header.FrameBytes > data.Length - offset )
					return false;
				if ( frames == 0 )
				{
					(sampleRate, channels, layer, emphasis) = (header.SampleRate, header.Channels, header.Layer, header.Emphasis);
					// [EXT:mpeg-deemphasis] de-emphasis of a stream that signals it is an OpenTPW decoder choice (a first-order bilinear shelf of the published 50/15 us and J.17 curves): every shipped frame (411,333 Layer I/II frames in all .sdt banks) has emphasis 0, so the original's decoder (not located) cannot be observed with shipped data
					if ( emphasis != 0 )
						deEmphasis = new DeEmphasis( emphasis, sampleRate );
				}
				else if ( header.SampleRate != sampleRate || header.Channels != channels || header.Layer != layer || header.Emphasis != emphasis )
					throw new NotSupportedException( $"MP2 frame {frames} changes sample rate, channel count, layer or emphasis." );
				// [EXT:mpeg-crc] a failed frame CRC is counted while the frame still decodes (an OpenTPW decoder robustness choice): every shipped frame has protection_bit 1 (no CRC word)
				if ( header.Crc )
				{
					crcChecks++;
					// [EXT:mpeg-crc-policy] A CRC mismatch is counted and the frame still decodes; ISO/IEC 11172-3 2.4.3.1 only
					// recommends concealment (muting or repeating), and no shipped TPW frame carries a CRC word — evidence
					// needed: the original player's behaviour on a failed frame CRC
					if ( !VerifyCrc( data.Slice( offset, header.FrameBytes ), header ) )
						crcFailures++;
				}
				if ( header.Layer == 1 )
					DecodeLayerOneFrame( header, data.Slice( offset, header.FrameBytes ), synthesis, output, deEmphasis );
				else
					DecodeFrame( header, data.Slice( offset, header.FrameBytes ), synthesis, output, deEmphasis );
				offset += header.FrameBytes;
				frames++;
			}
			return data.Length - offset >= 4;
		}

		public Mp2Audio Finish( int inputLength, int? playbackBytes = null )
		{
			if ( frames == 0 )
				throw new InvalidDataException( "MP2 stream contains no complete frame." );
			var kept = output.Count;
			if ( playbackBytes is { } bytes && bytes >= 0 )
				kept = Math.Min( kept, bytes / (2 * channels) * channels );
			var samples = System.Runtime.InteropServices.CollectionsMarshal.AsSpan( output )[..kept].ToArray();
			return new Mp2Audio( sampleRate, channels, samples, frames, inputLength - offset, crcChecks, crcFailures, emphasis );
		}
	}

	/// <summary>
	/// ISO/IEC 11172-3 2.4.3.1: CRC-16 with G(x) = x¹⁶ + x¹⁵ + x² + 1, all-ones start,
	/// over header bits 16-31 (bit_rate_index through emphasis) and the first
	/// <see cref="ProtectedBits"/> audio_data bits after the check word. Layer II protects
	/// its transmitted allocation/scfsi fields, not their maximum count;
	/// see Annex B Table B.5 and <see cref="ProtectedBits"/>.
	/// </summary>
	private static bool VerifyCrc( ReadOnlySpan<byte> frame, Header header )
	{
		var protectedBits = ProtectedBits( frame, header );
		if ( 48 + protectedBits > frame.Length * 8 )
			throw new InvalidDataException( "MP2 frame is too short for its CRC-protected bits." );
		var crc = 0xFFFF;
		StepCrc( ref crc, frame, 16, 16 );
		StepCrc( ref crc, frame, 48, protectedBits );
		return crc == (frame[4] << 8 | frame[5]);
	}

	/// <summary>
	/// Layer I retains its existing 128/256-bit coverage. For supported Layer II,
	/// ISO/IEC 11172-3 B.5 protects allocation plus transmitted scfsi (2.4.1.6: only
	/// nonzero allocations have scfsi). Use the frame's version-specific table and add
	/// two bits for each allocated channel/subband, excluding scalefactors and samples.
	/// Joint bands transmit one allocation but retain both channels' scfsi.
	/// </summary>
	private static int ProtectedBits( ReadOnlySpan<byte> frame, Header header )
	{
		if ( header.Layer == 1 )
			return 128 * header.Channels;
		var bits = new Bits( frame, 48 );
		var table = LayerTwoAllocation( header );
		var bound = header.Mode == 1 ? Math.Min( (header.ModeExtension + 1) * 4, table.Sum( row => row.Count ) ) : 32;
		var count = 0;
		var band = 0;
		foreach ( var row in table )
			for ( var subband = 0; subband < row.Count; subband++, band++ )
				for ( var channel = 0; channel < (band < bound ? header.Channels : 1); channel++ )
				{
					count += row.Bits;
					if ( bits.Read( row.Bits ) != 0 ) count += band < bound ? 2 : 2 * header.Channels;
				}
		return count;
	}

	private static void StepCrc( ref int crc, ReadOnlySpan<byte> frame, int start, int count )
	{
		for ( var index = start; index < start + count; index++ )
		{
			crc ^= ((frame[index >> 3] >> (7 - (index & 7))) & 1) << 15;
			crc = (crc & 0x8000) != 0 ? (crc << 1 ^ 0x8005) & 0xFFFF : crc << 1 & 0xFFFF;
		}
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

	private readonly record struct Header( bool Crc, int SampleRate, int Mode, int ModeExtension, int Layer, int FrameBytes,
		int Emphasis, int Version, int Bitrate )
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
			var bitrateIndex = (int)(word >> 12) & 15;
			var rateIndex = (int)(word >> 10) & 3;
			if ( bitrateIndex == 0 )
				throw new NotSupportedException( "Free-format MPEG audio is not supported." );
			if ( bitrateIndex == 15 || rateIndex == 3 )
				throw new InvalidDataException( "MP2 frame header uses a reserved bitrate or sample rate." );
			var bitrate = layer == 3 ? LayerOneBitrates[version == 3 ? 1 : 0][bitrateIndex]
				: version == 3 ? Mpeg1LayerTwoBitrates[bitrateIndex] : Mpeg2Bitrates[bitrateIndex];
			var sampleRate = Mpeg2SampleRates[rateIndex] * (version == 3 ? 2 : 1);
			var mode = (int)(word >> 6) & 3;
			if ( mode == 1 && layer == 2 && version != 3 )
				throw new NotSupportedException( "Joint-stereo MPEG audio is not supported." );
			if ( layer == 2 && version == 3 && (mode == 3 ? bitrate > 192 : bitrate is 32 or 48 or 56 or 80) )
				throw new InvalidDataException( "MPEG-1 Layer II bitrate is not allowed for its channel mode." );
			var padding = (int)(word >> 9) & 1;
			var emphasis = (int)word & 3;
			if ( emphasis == 2 )
				throw new InvalidDataException( "MPEG frame header uses reserved emphasis." );
			var frameBytes = layer == 3
				? (12 * bitrate * 1000 / sampleRate + padding) * 4
				: 144 * bitrate * 1000 / sampleRate + padding;
			return new Header( (word & 0x10000) == 0, sampleRate, mode, (int)(word >> 4) & 3,
				layer == 3 ? 1 : 2, frameBytes, emphasis, version, bitrate );
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

	/// <summary>
	/// First-order shelving de-emphasis applied per channel to the synthesis output.
	/// Emphasis '01' (50/15 µs) inverts the broadcast pre-emphasis whose time constants
	/// are 50 µs (pole) and 15 µs (zero); emphasis '11' (CCITT J.17) inverts the published
	/// J.17 curve, insertion loss 10·log₁₀((75+(ω/3000)²)/(1+(ω/3000)²)) dB, a shelf with
	/// pole time constant 1/3000 s and zero time constant 1/(3000·√75) s (18.75 dB at high
	/// frequencies). The network's absolute 18.75 dB pad cancels in the encoder/decoder
	/// round trip, so both curves are realized at unity gain at DC.
	/// </summary>
	// [EXT:mpeg-deemphasis-filter] the standards specify only the analogue de-emphasis characteristic; this realization is the
	// bilinear transform of the first-order shelf at the stream's sample rate, J.17 normalized to unity DC gain —
	// evidence needed: the original decoder's filter realization or an emphasised original stream
	private sealed class DeEmphasis
	{
		private readonly double b0, b1, a1;
		private readonly double[] previousInput = new double[2];
		private readonly double[] previousOutput = new double[2];

		public DeEmphasis( int emphasis, int sampleRate )
		{
			var zero = emphasis == 1 ? 15e-6 : 1.0 / (3000 * Math.Sqrt( 75 ));
			var pole = emphasis == 1 ? 50e-6 : 1.0 / 3000;
			// Bilinear transform of (1 + s·zero)/(1 + s·pole) with s = 2·rate·(1-z⁻¹)/(1+z⁻¹).
			var highZero = 2.0 * sampleRate * zero;
			var highPole = 2.0 * sampleRate * pole;
			b0 = (1 + highZero) / (1 + highPole);
			b1 = (1 - highZero) / (1 + highPole);
			a1 = (1 - highPole) / (1 + highPole);
		}

		public void Run( int channel, Span<float> pcm )
		{
			var previousInput = this.previousInput[channel];
			var previousOutput = this.previousOutput[channel];
			for ( var index = 0; index < pcm.Length; index++ )
			{
				var input = (double)pcm[index];
				var output = b0 * input + b1 * previousInput - a1 * previousOutput;
				previousInput = input;
				previousOutput = output;
				pcm[index] = (float)output;
			}
			this.previousInput[channel] = previousInput;
			this.previousOutput[channel] = previousOutput;
		}
	}

	private static void DecodeFrame( Header header, ReadOnlySpan<byte> frame, Synthesis[] synthesis, List<short> output,
		DeEmphasis? deEmphasis )
	{
		var bits = new Bits( frame, header.Crc ? 48 : 32 );
		var channels = header.Channels;
		var allocationTable = LayerTwoAllocation( header );
		var subbandLimit = allocationTable.Sum( row => row.Count );
		var bound = header.Mode == 1 ? Math.Min( (header.ModeExtension + 1) * 4, subbandLimit ) : subbandLimit;

		var classes = new int[2, 32];
		var subband = 0;
		foreach ( var row in allocationTable )
			for ( var count = 0; count < row.Count; count++, subband++ )
			{
				for ( var channel = 0; channel < (subband < bound ? channels : 1); channel++ )
					classes[channel, subband] = row.Classes[bits.Read( row.Bits )];
				if ( channels == 2 && subband >= bound ) classes[1, subband] = classes[0, subband];
			}

		var selection = new int[2, 32];
		for ( subband = 0; subband < subbandLimit; subband++ )
			for ( var channel = 0; channel < channels; channel++ )
				if ( classes[channel, subband] != 0 )
					selection[channel, subband] = bits.Read( 2 );

		var factors = new float[2, 32, 3];
		for ( subband = 0; subband < subbandLimit; subband++ )
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
			for ( subband = 0; subband < subbandLimit; subband++ )
			{
				for ( var channel = 0; channel < (subband < bound ? channels : 1); channel++ )
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
					{
						samples[(channel * 3 + index) * 32 + subband] = (2 * codes[index] + 1 - levels) / (float)levels * factors[channel, subband, part];
						if ( channels == 2 && subband >= bound )
							samples[(3 + index) * 32 + subband] = (2 * codes[index] + 1 - levels) / (float)levels * factors[1, subband, part];
					}
				}
			}
			for ( var index = 0; index < 3; index++ )
				AppendSynthesisSlot( samples[(index * 32)..], channels, 3 * 32, synthesis, output, deEmphasis );
		}
		if ( output.Count - frameStart != SamplesPerFrame * channels )
			throw new InvalidOperationException( "MP2 frame produced an unexpected sample count." );
	}

	private static void AppendSynthesisSlot( ReadOnlySpan<float> samples, int channels, int channelStride,
		Synthesis[] synthesis, List<short> output, DeEmphasis? deEmphasis )
	{
		Span<float> pcm = stackalloc float[32];
		var slotStart = output.Count;
		for ( var sample = 0; sample < 32 * channels; sample++ )
			output.Add( 0 );
		for ( var channel = 0; channel < channels; channel++ )
		{
			synthesis[channel].Run( samples.Slice( channel * channelStride, 32 ), pcm );
			if ( deEmphasis != null )
				deEmphasis.Run( channel, pcm );
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
