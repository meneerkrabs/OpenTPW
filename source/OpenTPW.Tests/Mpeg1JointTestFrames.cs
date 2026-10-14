using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTPW.Tests;

// Independently emits ISO11172-3 2.4.1.6 fields; no decoder tables or protected-span calls.
internal static class Mpeg1JointTestFrames
{
	internal sealed record Channel( int Selection = 2, int First = 6, int Second = 9, int Third = 12, int[]? Samples = null );
	internal sealed record Band( int Subband, int LeftAllocation = 1, int RightAllocation = 1, Channel? Left = null, Channel? Right = null );
	internal sealed record Packet( byte[] Data, int Rate, char Table, int Limit, int Bound, int ProtectedAudioBits, int ScaleStart, int SampleStart, int[] ScfsiStarts );
	private static readonly int[] Rates = { 44100, 48000, 32000 };
	private static readonly int[] Bitrates = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 };
	private static readonly int[][] Levels = {
		new[] {0,3,7,15,31,63,127,255,511,1023,2047,4095,8191,16383,32767,65535},
		new[] {0,3,5,7,9,15,31,63,127,255,511,1023,2047,4095,8191,65535},
		new[] {0,3,5,7,9,15,31,65535}, new[] {0,3,5,65535},
		new[] {0,3,5,9,15,31,63,127,255,511,1023,2047,4095,8191,16383,32767},
		new[] {0,3,5,9,15,31,63,127}};
	internal static (int Width, int[] Levels) Row( char table, int sb ) => table is 'C' or 'D'
		? sb < 2 ? (4, Levels[4]) : (3, Levels[5])
		: sb < 3 ? (4, Levels[0]) : sb < 11 ? (4, Levels[1]) : sb < 23 ? (3, Levels[2]) : (2, Levels[3]);
	internal static Packet Make( int bitrateIndex = 10, int rateIndex = 0, int extension = 0, bool crc = false, int padding = 0, params Band[] bands )
	{
		var rate = Rates[rateIndex]; var bitrate = Bitrates[bitrateIndex];
		var table = bitrate <= 96 ? (rate == 32000 ? 'D' : 'C') : rate == 48000 || bitrate <= 160 ? 'A' : 'B';
		var limit = table == 'A' ? 27 : table == 'B' ? 30 : table == 'C' ? 8 : 12;
		var boundary = Math.Min( 4 + 4 * extension, limit );
		var fields = new List<(int Value, int Width, bool Protected)>(); var position = 0;
		void Put( int value, int width, bool protect = false ) { fields.Add( (value, width, protect) ); position += width; }
		Put( 0x7ff, 11 ); Put( 3, 2 ); Put( 2, 2 ); Put( crc ? 0 : 1, 1 );
		Put( bitrateIndex, 4, true ); Put( rateIndex, 2, true ); Put( padding, 1, true ); Put( 0, 1, true );
		Put( 1, 2, true ); Put( extension, 2, true ); Put( 0, 2, true ); Put( 0, 2, true );
		if ( crc ) Put( 0, 16 );
		var byBand = bands.ToDictionary( b => b.Subband );
		int Allocation( int sb, int ch ) => !byBand.TryGetValue( sb, out var b ) ? 0 : ch == 0 ? b.LeftAllocation : b.RightAllocation;
		Channel Config( int sb, int ch ) { var b = byBand[sb]; return (ch == 0 ? b.Left : b.Right) ?? new Channel(); }
		foreach ( var b in bands ) if ( b.Subband >= boundary && b.LeftAllocation != b.RightAllocation ) throw new ArgumentException( "Shared allocation must agree." );
		for ( var sb = 0; sb < limit; sb++ )
		{
			Put( Allocation( sb, 0 ), Row( table, sb ).Width, true );
			if ( sb < boundary ) Put( Allocation( sb, 1 ), Row( table, sb ).Width, true );
		}
		var scfsiStarts = new List<int>();
		for ( var sb = 0; sb < limit; sb++ ) for ( var ch = 0; ch < 2; ch++ ) if ( Allocation( sb, ch ) != 0 ) { scfsiStarts.Add( position ); Put( Config( sb, ch ).Selection, 2, true ); }
		var scaleStart = position;
		for ( var sb = 0; sb < limit; sb++ ) for ( var ch = 0; ch < 2; ch++ ) if ( Allocation( sb, ch ) != 0 )
		{
			var c = Config( sb, ch ); Put( c.First, 6 );
			if ( c.Selection is 0 or 3 ) Put( c.Second, 6 );
			if ( c.Selection is 0 or 1 ) Put( c.Third, 6 );
		}
		var sampleStart = position;
		for ( var gr = 0; gr < 12; gr++ ) for ( var sb = 0; sb < limit; sb++ ) for ( var ch = 0; ch < (sb < boundary ? 2 : 1); ch++ ) if ( Allocation( sb, ch ) != 0 )
		{
			var levels = Row( table, sb ).Levels[Allocation( sb, ch )]; var samples = Config( sb, ch ).Samples ?? new[] { levels - 1, 0, levels / 2 };
			if ( levels is 3 or 5 or 9 ) Put( samples[0] + levels * (samples[1] + levels * samples[2]), levels == 3 ? 5 : levels == 5 ? 7 : 10 );
			else { var width = System.Numerics.BitOperations.Log2( (uint)levels ) + 1; foreach ( var sample in samples ) Put( sample, width ); }
		}
		var size = 144 * bitrate * 1000 / rate + padding;
		if ( position > size * 8 ) throw new ArgumentException( "Synthetic fields exceed frame." );
		var data = new byte[size]; position = 0;
		foreach ( var f in fields ) for ( var bit = f.Width - 1; bit >= 0; bit--, position++ ) if ( ((f.Value >> bit) & 1) != 0 ) data[position / 8] |= (byte)(1 << (7 - position % 8));
		// Polynomial long division on explicitly protected field bits, independent of decoder StepCrc.
		var register = 0; var protectedIndex = 0;
		foreach ( var f in fields.Where( f => f.Protected ) ) for ( var bit = f.Width - 1; bit >= 0; bit-- )
		{
			var incoming = ((f.Value >> bit) & 1) ^ (protectedIndex++ < 16 ? 1 : 0);
			register = (register << 1) | incoming; if ( (register & 0x10000) != 0 ) register ^= 0x18005;
		}
		for ( var bit = 0; bit < 16; bit++ ) { register <<= 1; if ( (register & 0x10000) != 0 ) register ^= 0x18005; }
		if ( crc ) { data[4] = (byte)(register >> 8); data[5] = (byte)register; }
		return new Packet( data, rate, table, limit, boundary, fields.Where( f => f.Protected ).Sum( f => f.Width ) - 16, scaleStart, sampleStart, scfsiStarts.ToArray() );
	}
	internal static void Flip( byte[] data, int bit ) => data[bit / 8] ^= (byte)(1 << (7 - bit % 8));
}
