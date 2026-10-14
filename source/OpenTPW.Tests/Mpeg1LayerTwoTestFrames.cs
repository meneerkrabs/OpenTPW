using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTPW.Tests;

// Independent synthetic encoder from ISO11172-3 B.2a-d/B.4 fields; never reads decoder tables.
internal static class Mpeg1LayerTwoTestFrames
{
	internal sealed record Band( int Channel, int Subband, int Allocation = 1, int Scfsi = 2, int FirstScale = 3, int SecondScale = 6, int ThirdScale = 9, int[]? Codes = null );
	internal sealed record Packet( byte[] Data, int Rate, int Channels, char Table, int ProtectedAudioBits, int ScaleStart );
	private static readonly int[] Rates = { 44100, 48000, 32000 };
	private static readonly int[] Bitrates = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 };
	private static readonly int[] H0 = { 0, 3, 7, 15, 31, 63, 127, 255, 511, 1023, 2047, 4095, 8191, 16383, 32767, 65535 };
	private static readonly int[] H1 = { 0, 3, 5, 7, 9, 15, 31, 63, 127, 255, 511, 1023, 2047, 4095, 8191, 65535 };
	private static readonly int[] H2 = { 0, 3, 5, 7, 9, 15, 31, 65535 };
	private static readonly int[] H3 = { 0, 3, 5, 65535 };
	private static readonly int[] L0 = { 0, 3, 5, 9, 15, 31, 63, 127, 255, 511, 1023, 2047, 4095, 8191, 16383, 32767 };
	private static readonly int[] L1 = { 0, 3, 5, 9, 15, 31, 63, 127 };
	internal static (int Bits, int[] Levels) Row( char table, int subband ) => table is 'C' or 'D'
		? subband < 2 ? (4, L0) : (3, L1)
		: subband < 3 ? (4, H0) : subband < 11 ? (4, H1) : subband < 23 ? (3, H2) : (2, H3);
	internal static int Limit( char table ) => table switch { 'A' => 27, 'B' => 30, 'C' => 8, _ => 12 };
	internal static Packet Frame( int bitrateIndex = 8, int rateIndex = 0, int mode = 3, bool crc = false, int padding = 0, int modeExtension = 0, params Band[] bands )
	{
		var rate = Rates[rateIndex]; var bitrate = Bitrates[bitrateIndex]; var channels = mode == 3 ? 1 : 2; var perChannel = bitrate / channels;
		var table = perChannel <= 48 ? (rate == 32000 ? 'D' : 'C') : rate == 48000 || perChannel <= 80 ? 'A' : 'B'; var limit = Limit( table );
		var byBand = bands.ToDictionary( band => (band.Channel, band.Subband) ); var writer = new Layer1TestFrames.BitWriter(); var protectedFields = new List<(int, int)>(); var offset = 0;
		void Put( int value, int width, bool protect = false ) { writer.Write( value, width ); offset += width; if ( protect ) protectedFields.Add( (value, width) ); }
		Put( 0x7ff, 11 ); Put( 3, 2 ); Put( 2, 2 ); Put( crc ? 0 : 1, 1 ); Put( bitrateIndex, 4, true ); Put( rateIndex, 2, true ); Put( padding, 1, true ); Put( 0, 1, true ); Put( mode, 2, true ); Put( modeExtension, 2, true ); Put( 0, 2, true ); Put( 0, 2, true );
		if ( crc ) Put( 0, 16 );
		for ( var sb = 0; sb < limit; sb++ ) for ( var channel = 0; channel < channels; channel++ ) Put( byBand.TryGetValue( (channel, sb), out var band ) ? band.Allocation : 0, Row( table, sb ).Bits, true );
		for ( var sb = 0; sb < limit; sb++ ) for ( var channel = 0; channel < channels; channel++ ) if ( byBand.TryGetValue( (channel, sb), out var band ) && band.Allocation != 0 ) Put( band.Scfsi, 2, true );
		var scaleStart = offset;
		for ( var sb = 0; sb < limit; sb++ ) for ( var channel = 0; channel < channels; channel++ ) if ( byBand.TryGetValue( (channel, sb), out var band ) && band.Allocation != 0 )
		{
			Put( band.FirstScale, 6 );
			if ( band.Scfsi is 0 or 3 ) Put( band.SecondScale, 6 );
			if ( band.Scfsi is 0 or 1 ) Put( band.ThirdScale, 6 );
		}
		for ( var granule = 0; granule < 12; granule++ ) for ( var sb = 0; sb < limit; sb++ ) for ( var channel = 0; channel < channels; channel++ ) if ( byBand.TryGetValue( (channel, sb), out var band ) && band.Allocation != 0 )
		{
			var levels = Row( table, sb ).Levels[band.Allocation]; var codes = band.Codes ?? new[] { levels - 1, levels - 1, levels - 1 };
			if ( levels is 3 or 5 or 9 ) Put( codes[0] + levels * (codes[1] + levels * codes[2]), levels == 3 ? 5 : levels == 5 ? 7 : 10 );
			else { var width = System.Numerics.BitOperations.Log2( (uint)levels ) + 1; foreach ( var code in codes ) Put( code, width ); }
		}
		var data = writer.Finish( 144 * bitrate * 1000 / rate + padding );
		if ( crc ) { ushort state = ushort.MaxValue; foreach ( var field in protectedFields ) for ( var shift = field.Item2 - 1; shift >= 0; shift-- ) { var feedback = ((state >> 15) ^ ((field.Item1 >> shift) & 1)) != 0; state = (ushort)(state << 1); if ( feedback ) state ^= 0x8005; } data[4] = (byte)(state >> 8); data[5] = (byte)state; }
		return new Packet( data, rate, channels, table, protectedFields.Sum( field => field.Item2 ) - 16, scaleStart );
	}
}
