namespace OpenTPW;

public static partial class Mp2Decoder
{
	// ISO/IEC 11172-3 Layer I and 13818-3 low-sampling-frequency header tables.
	private static readonly int[][] LayerOneBitrates =
	{
		new[] { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 },
		new[] { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 },
	};

	private static readonly float[,] LayerOneFactors = BuildLayerOneFactors();

	private static float[,] BuildLayerOneFactors()
	{
		var factors = new float[15, 63];
		for ( var allocation = 1; allocation <= 14; allocation++ )
			for ( var scale = 0; scale < 63; scale++ )
				// Fold requantizer scale in double before one float conversion. All 882
				// entries match the identified Mac table used at sound code 0x3054.
				factors[allocation, scale] = (float)(Math.Pow( 2, 2 - scale / 3.0 ) / ((1 << (allocation + 1)) - 1));
		return factors;
	}

	private static void DecodeLayerOneFrame( Header header, ReadOnlySpan<byte> frame,
		Synthesis[] synthesis, List<short> output )
	{
		var bits = new Bits( frame, header.Crc ? 48 : 32 );
		var channels = header.Channels;
		var bound = header.Mode == 1 ? (header.ModeExtension + 1) * 4 : 32;
		var allocation = new int[2, 32];
		for ( var subband = 0; subband < 32; subband++ )
		{
			for ( var channel = 0; channel < (subband < bound ? channels : 1); channel++ )
			{
				var value = bits.Read( 4 );
				if ( value == 15 )
					throw new InvalidDataException( "Layer I allocation 15 is forbidden." );
				allocation[channel, subband] = value;
			}
			if ( channels == 2 && subband >= bound )
				allocation[1, subband] = allocation[0, subband];
		}

		var factors = new float[2, 32];
		for ( var subband = 0; subband < 32; subband++ )
			for ( var channel = 0; channel < channels; channel++ )
				if ( allocation[channel, subband] != 0 )
					factors[channel, subband] = LayerOneFactors[allocation[channel, subband], ReadFactor( ref bits )];

		Span<float> samples = stackalloc float[2 * 32];
		var frameStart = output.Count;
		for ( var slot = 0; slot < 12; slot++ )
		{
			samples.Clear();
			for ( var subband = 0; subband < 32; subband++ )
			{
				for ( var channel = 0; channel < (subband < bound ? channels : 1); channel++ )
				{
					var allocated = allocation[channel, subband];
					if ( allocated == 0 )
						continue;
					var code = bits.Read( allocated + 1 );
					// ISO 11172-3 2.4.3.2.1, confirmed by the original numerator at
					// sound 0x3030–0x306c: code + 1 - 2^allocation, then combined factor.
					var centered = code + 1 - (1 << allocated);
					samples[channel * 32 + subband] = centered * factors[channel, subband];
					if ( channels == 2 && subband >= bound )
						samples[32 + subband] = centered * factors[1, subband];
				}
			}
			AppendSynthesisSlot( samples, channels, 32, synthesis, output );
		}
		if ( output.Count - frameStart != LayerOneSamplesPerFrame * channels )
			throw new InvalidOperationException( "Layer I frame produced an unexpected sample count." );
	}
}
