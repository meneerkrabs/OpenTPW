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
	// wraps around to the last one. Source pairs sit at stride 2, except that
	// half-scale data halves the source index; output pairs sit at stride 2
	// for full-scale data and stride 1 for half-scale, so half-scale output
	// neighbours overlap by one sample.
	void D4InverseTransformRows( ref List<float> pSrc, ref List<float> pDest, int offset, int size, D4Coefficients coefs, bool isHalfScale )
	{
		for ( int i = 0; i < size; i++ )
		{
			int prev = (i == 0) ? size - 1 : i - 1;
			int srcPrev = isHalfScale ? prev / 2 : prev * 2;
			int srcCur = isHalfScale ? i / 2 : i * 2;
			int dst = isHalfScale ? i : i * 2;

			float s0 = pSrc[srcPrev + offset];
			float w0 = pSrc[srcPrev + 1 + offset];
			float s1 = pSrc[srcCur + offset];
			float w1 = pSrc[srcCur + 1 + offset];

			pDest[dst + offset] = ApplyScalingCoefficientsInv( s0, w0, s1, w1, coefs );
			pDest[dst + 1 + offset] = ApplyWaveCoefficientsInv( s0, w0, s1, w1, coefs );
		}
	}

	// One inverse D4 step down a single column: scale values come from row
	// `index`, wavelet values from `index + wOffset`, and the results land in
	// interleaved output rows two strides apart.
	void D4InverseTransformColumns( ref List<float> pSrc, ref List<float> pDest, int size, D4Coefficients coefs, int col, int wOffset )
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

	private bool DecodeChannel( ref ImageDecodeState state, int size, ref byte[] pSrc, ref List<float> outputBuffer, float dequantizationScale, bool isHalfScale, bool isAlpha = false )
	{
		int count = size * (size / 2);
		int index = 0;

		//
		// Step 1: Dequantize
		//
		for ( int i = 0; i < count; i++ )
		{
			int val = (sbyte)pSrc[index++];

			if ( val == sbyte.MinValue )
			{
				val = BitConverter.ToInt16( pSrc, index );
				index += sizeof( short );
			}

			state.DequantizationBuffer[i * 2] = (float)val;

			val = (sbyte)pSrc[index++];

			if ( val == sbyte.MinValue )
			{
				val = BitConverter.ToInt16( pSrc, index );
				index += sizeof( short );
			}

			state.DequantizationBuffer[i * 2 + 1] = (float)val;
		}

		Array.Copy( pSrc, index, pSrc, 0, pSrc.Length - index );

		//
		// Step 2: decode rows
		//
		if ( isHalfScale )
			dequantizationScale *= MathF.PI * 1/6f;

		D4Coefficients coefs = new( dequantizationScale );
		int rowTransformSize = (isHalfScale) ? size : size / 2;
		for ( int i = 0; i < size; i++ )
			D4InverseTransformRows( ref state.DequantizationBuffer, ref state.RowDecodeBuffer, i * size, rowTransformSize, coefs, isHalfScale );

		//
		// Step 3: decode columns
		//
		coefs = new( 1.0f );
		int wOffset = isAlpha && isHalfScale ? (size * size) : size * (size / 2);
		for ( int i = 0; i < size; ++i )
			D4InverseTransformColumns( ref state.RowDecodeBuffer, ref outputBuffer, size, coefs, i, wOffset );

		return true;
	}
}
