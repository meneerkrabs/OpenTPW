using System;
using System.Collections.Generic;

namespace OpenTPW.Tests;

// Synthetic bitstreams only: no original fixture contents.
internal static class Layer1TestFrames
{
	internal sealed class BitWriter
	{
		private readonly List<bool> bits = new();
		public void Write( int value, int width )
		{
			for ( var bit = width - 1; bit >= 0; bit-- )
				bits.Add( (value & (1 << bit)) != 0 );
		}
		public byte[] Finish( int bytes )
		{
			if ( bits.Count > bytes * 8 )
				throw new InvalidOperationException( "Generated test payload exceeds frame capacity." );
			var frame = new byte[bytes];
			for ( var bit = 0; bit < bits.Count; bit++ )
				if ( bits[bit] )
					frame[bit >> 3] |= (byte)(1 << (7 - (bit & 7)));
			return frame;
		}
	}

	internal static byte[] Frame( int allocation = 0, int scale = 3, int mode = 3,
		int version = 2, int subband = 0, int padding = 0, int modeExtension = 0,
		int rightScale = 3, int? sampleCode = null, int? rightSampleCode = null, bool crc = false, int bitrateIndex = 4,
		int rateIndex = 0, int emphasis = 0 )
	{
		var rates = version == 3 ? new[] { 44100, 48000, 32000 } : new[] { 22050, 24000, 16000 };
		var bitrates = version == 3
			? new[] { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 }
			: new[] { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 };
		var bytes = (12 * bitrates[Math.Min( bitrateIndex, 14 )] * 1000 / rates[Math.Min( rateIndex, 2 )] + padding) * 4;
		if ( bytes == 0 ) bytes = 128;
		var writer = new BitWriter();
		writer.Write( 0x7FF, 11 ); writer.Write( version, 2 ); writer.Write( 3, 2 ); writer.Write( crc ? 0 : 1, 1 );
		writer.Write( bitrateIndex, 4 ); writer.Write( rateIndex, 2 ); writer.Write( padding, 1 ); writer.Write( 0, 1 );
		writer.Write( mode, 2 ); writer.Write( modeExtension, 2 ); writer.Write( 0, 2 ); writer.Write( emphasis, 2 );
		if ( crc ) writer.Write( 0, 16 );
		var channels = mode == 3 ? 1 : 2;
		var bound = mode == 1 ? (modeExtension + 1) * 4 : 32;
		for ( var band = 0; band < 32; band++ )
			for ( var channel = 0; channel < (band < bound ? channels : 1); channel++ )
				writer.Write( band == subband ? allocation : 0, 4 );
		if ( allocation != 0 )
		{
			for ( var channel = 0; channel < channels; channel++ )
				writer.Write( channel == 0 ? scale : rightScale, 6 );
			for ( var slot = 0; slot < 12; slot++ )
				for ( var channel = 0; channel < (subband < bound ? channels : 1); channel++ )
					writer.Write( channel == 1 ? rightSampleCode ?? sampleCode ?? (1 << allocation)
						: sampleCode ?? (1 << allocation), allocation + 1 );
		}
		return writer.Finish( bytes );
	}

}
