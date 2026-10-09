using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// EA-XA ADPCM (revision 1 layout, as observed in Theme Park World SCDl blocks) for interleaved stereo.
/// Block layout: u32 sample frames, per channel int16 current then previous predictor state,
/// then 30-byte frames of one coefficient-index byte, one shift byte and 28 sample bytes
/// (left nibble high, right nibble low). Each frame yields 28 samples per channel.
/// </summary>
public static class EaXaAdpcmDecoder
{
	public const int SamplesPerFrame = 28;
	public const int StereoFrameBytes = 30;
	public const int StereoBlockHeaderBytes = 12;

	private static readonly int[] Coefficient1 = { 0, 240, 460, 392 };
	private static readonly int[] Coefficient2 = { 0, 0, -208, -220 };

	/// <summary>Reads and validates the declared sample-frame count of a stereo block without decoding it.</summary>
	public static int ReadStereoBlockSampleCount( ReadOnlySpan<byte> block )
	{
		if ( block.Length < StereoBlockHeaderBytes )
			throw new InvalidDataException( "EA-XA block header is truncated." );
		var declared = BinaryPrimitives.ReadUInt32LittleEndian( block );
		var frames = ((long)declared + SamplesPerFrame - 1) / SamplesPerFrame;
		var used = StereoBlockHeaderBytes + frames * StereoFrameBytes;
		if ( used > block.Length )
			throw new InvalidDataException( "EA-XA block sample count exceeds its encoded frames." );
		var slack = block.Length - (int)used;
		if ( slack >= 4 )
			throw new InvalidDataException( "EA-XA block has unexplained trailing bytes." );
		foreach ( var value in block[(int)used..] )
			if ( value != 0 )
				throw new InvalidDataException( "EA-XA block alignment padding must be zero." );
		return (int)declared;
	}

	/// <summary>Decodes one stereo block into interleaved signed 16-bit PCM; returns the sample frames written.</summary>
	public static int DecodeStereoBlock( ReadOnlySpan<byte> block, Span<short> interleavedOutput )
	{
		var sampleFrames = ReadStereoBlockSampleCount( block );
		if ( interleavedOutput.Length < sampleFrames * 2 )
			throw new ArgumentException( "Output buffer is too small for the EA-XA block.", nameof( interleavedOutput ) );
		Span<int> current = stackalloc int[2];
		Span<int> previous = stackalloc int[2];
		for ( var channel = 0; channel < 2; channel++ )
		{
			current[channel] = BinaryPrimitives.ReadInt16LittleEndian( block[(4 + channel * 4)..] );
			previous[channel] = BinaryPrimitives.ReadInt16LittleEndian( block[(6 + channel * 4)..] );
		}
		var offset = StereoBlockHeaderBytes;
		var written = 0;
		while ( written < sampleFrames )
		{
			var predictors = block[offset];
			var shifts = block[offset + 1];
			var leftIndex = predictors >> 4;
			var rightIndex = predictors & 15;
			if ( leftIndex > 3 || rightIndex > 3 )
				throw new InvalidDataException( "EA-XA frame uses an unknown predictor index." );
			var leftShift = (shifts >> 4) + 8;
			var rightShift = (shifts & 15) + 8;
			offset += 2;
			for ( var sample = 0; sample < SamplesPerFrame; sample++, offset++ )
			{
				var packed = block[offset];
				var left = Predict( packed >> 4, leftShift, leftIndex, ref current[0], ref previous[0] );
				var right = Predict( packed & 15, rightShift, rightIndex, ref current[1], ref previous[1] );
				if ( written < sampleFrames )
				{
					interleavedOutput[written * 2] = left;
					interleavedOutput[written * 2 + 1] = right;
					written++;
				}
			}
		}
		return sampleFrames;
	}

	private static short Predict( int nibble, int shift, int index, ref int current, ref int previous )
	{
		var scaled = (nibble << 28) >> shift;
		var value = (scaled + current * Coefficient1[index] + previous * Coefficient2[index] + 0x80) >> 8;
		value = Math.Clamp( value, short.MinValue, short.MaxValue );
		previous = current;
		current = value;
		return (short)value;
	}
}
