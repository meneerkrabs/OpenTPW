namespace OpenTPW;

partial class TextureFile
{
	private uint GetAlignedSize( uint size )
	{
		uint value = (size - 1);
		uint n = 1;

		for ( ; (value >>= 1) != 0; )
		{
			++n;
		}

		if ( n < 3 )
		{
			n = 3;
		}

		return (uint)(1 << (int)n);
	}

	static float ApplyScalingCoefficientsInv( float smooth0, float coef0, float smooth1, float coef1, D4Coefficients coefs )
	{
		return smooth0 * coefs.IH0 + coef0 * coefs.IH1 +
			smooth1 * coefs.IH2 + coef1 * coefs.IH3;
	}

	static float ApplyWaveCoefficientsInv( float smooth0, float coef0, float smooth1, float coef1, D4Coefficients coefs )
	{
		return smooth0 * coefs.IG0 + coef0 * coefs.IG1 +
			smooth1 * coefs.IG2 + coef1 * coefs.IG3;
	}

	// One inverse D4 step over a row's scale/wavelet pairs. The previous pair
	// wraps around to the last one. Output pairs sit at stride 2.
	//
	// The coefficients of a row come in one of two layouts, chosen by the header's first flag bit (TextureFlags.InterleavedRows):
	// interleaved (scale, wavelet, scale, wavelet, ...; the world textures, flags 0x13) or planar (all scale
	// samples of the row, then all its wavelet samples; the interface art of ui.wad, flags 0x12).
	// [BIN:TPW-EXE:0x593ab0 planar rows, 0x595920 interleaved rows] the two decoders of the vtable at 0x7026d8: 0x593ab0 dequantises a row of N
	// coefficients into a scratch row and reads its scale samples from the first N/2 entries and its wavelet samples from the last N/2
	// (local_5c[i], local_5c[N/2 + i]); 0x595920 takes each scale/wavelet pair straight from the stream. The sign boards' wavelet decoder
	// (SignWaveletDecoder) reads planar rows the same way. The column pass is planar in both.
	void D4InverseTransformRows( float[] pSrc, float[] pDest, int offset, int pairs, D4Coefficients coefs, bool planarRows )
	{
		for ( int i = 0; i < pairs; i++ )
		{
			int prev = (i == 0) ? pairs - 1 : i - 1;
			int srcPrev = planarRows ? prev : prev * 2;
			int srcCur = planarRows ? i : i * 2;
			int waveletStep = planarRows ? pairs : 1;
			int dst = i * 2;

			float s0 = pSrc[srcPrev + offset];
			float w0 = pSrc[srcPrev + waveletStep + offset];
			float s1 = pSrc[srcCur + offset];
			float w1 = pSrc[srcCur + waveletStep + offset];

			pDest[dst + offset] = ApplyScalingCoefficientsInv( s0, w0, s1, w1, coefs );
			pDest[dst + 1 + offset] = ApplyWaveCoefficientsInv( s0, w0, s1, w1, coefs );
		}
	}

	// One inverse D4 step down a single column: scale values come from row
	// `index`, wavelet values from `index + wOffset`, and the results land in
	// interleaved output rows two strides apart.
	void D4InverseTransformColumns( float[] pSrc, float[] pDest, int size, D4Coefficients coefs, int col, int wOffset )
	{
		int count = size / 2;

		for ( int i = 0; i < count; i++ )
		{
			int prev = (i == 0) ? count - 1 : i - 1;
			int srcPrev = prev * size + col;
			int srcCur = i * size + col;
			int dst = i * 2 * size + col;

			float s0 = pSrc[srcPrev];
			float w0 = pSrc[srcPrev + wOffset];
			float s1 = pSrc[srcCur];
			float w1 = pSrc[srcCur + wOffset];

			pDest[dst] = ApplyScalingCoefficientsInv( s0, w0, s1, w1, coefs );
			pDest[dst + size] = ApplyWaveCoefficientsInv( s0, w0, s1, w1, coefs );
		}
	}

	private bool DecodeChannel( ref ImageDecodeState state, int size, ref byte[] pSrc, float[] outputBuffer, float dequantizationScale, bool planarRows )
	{
		int count = size * (size / 2);
		int index = 0;
		if ( pSrc.Length < size * size )
			throw new InvalidDataException( "Truncated WCT channel coefficients." );

		//
		// Step 1: Dequantize
		//
		for ( int i = 0; i < count; i++ )
		{
			if ( index >= pSrc.Length ) throw new InvalidDataException( "Truncated WCT channel coefficients." );
			int val = (sbyte)pSrc[index++];

			if ( val == sbyte.MinValue )
			{
				if ( pSrc.Length - index < sizeof( short ) ) throw new InvalidDataException( "Truncated WCT coefficient escape." );
				val = BitConverter.ToInt16( pSrc, index );
				index += sizeof( short );
			}

			state.DequantizationBuffer[i * 2] = (float)val;

			if ( index >= pSrc.Length ) throw new InvalidDataException( "Truncated WCT channel coefficients." );
			val = (sbyte)pSrc[index++];

			if ( val == sbyte.MinValue )
			{
				if ( pSrc.Length - index < sizeof( short ) ) throw new InvalidDataException( "Truncated WCT coefficient escape." );
				val = BitConverter.ToInt16( pSrc, index );
				index += sizeof( short );
			}

			state.DequantizationBuffer[i * 2 + 1] = (float)val;
		}

		pSrc = pSrc[index..]; // Only unconsumed bytes belong to the next channel.

		//
		// Step 2: decode rows
		//
		D4Coefficients coefs = new( dequantizationScale );
		int rowTransformSize = size / 2;
		for ( int i = 0; i < size; i++ )
			D4InverseTransformRows( state.DequantizationBuffer, state.RowDecodeBuffer, i * size, rowTransformSize, coefs, planarRows );

		//
		// Step 3: decode columns
		//
		coefs = new( 1.0f );
		int wOffset = size * (size / 2);
		for ( int i = 0; i < size; ++i )
			D4InverseTransformColumns( state.RowDecodeBuffer, outputBuffer, size, coefs, i, wOffset );

		return true;
	}
}
