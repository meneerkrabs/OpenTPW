using System;
using System.IO;

namespace OpenTPW.Tests;

/// <summary>
/// Reproducible synthetic procedural-terrain levels: the same formulas and LCG as <c>terrain_reading.synthetic_level</c> in
/// tools/ppc-analysis/oracle_x86, so the hashes tp.exe's build routine gave for them (<see cref="ProceduralTerrainSyntheticGolden"/>) apply here.
/// Not original data. Keep this file and the Python generator in step.
/// </summary>
internal static class SyntheticTerrain
{
	public static readonly string[] Mixes = ["C", "B", "A", "BA", "CB", "CA", "CBA", "random"];
	public const int Cells = 3;
	public static readonly (int X, int Y) GreySample = (5, 6);

	private static (string Mask1, string Mask2) Masks( string mix ) => mix switch
	{
		"C" => ("all", "all"),
		"B" => ("none", "all"),
		"A" => ("none", "none"),
		"BA" => ("none", "mixed"),
		"CB" => ("mixed", "all"),
		"CA" => ("mixed", "none"),
		"CBA" => ("mixed", "mixed"),
		_ => ("random", "random"),
	};

	/// <summary>The top byte of state * 1664525 + 1013904223 (mod 2^32).</summary>
	private sealed class Lcg( uint seed )
	{
		private uint state = seed;

		public byte Next()
		{
			state = unchecked( state * 1664525u + 1013904223u );
			return (byte)(state >> 24);
		}
	}

	public static byte[] Level( int seed, string mix, bool grey )
	{
		var lcg = new Lcg( (uint)seed );
		var parameters = new byte[10];
		for ( var index = 0; index < parameters.Length; index++ )
			parameters[index] = lcg.Next();
		if ( mix != "random" )
		{
			parameters[4] = 128;
			parameters[5] = 0;
			parameters[8] = 128;
			parameters[9] = 0;
		}
		var (mask1, mask2) = Masks( mix );

		static byte Weight( string style, int x, int y, int k ) => style switch
		{
			"none" => 0,
			"random" => (byte)(x * (k == 2 ? 53 : 17) + y * (k == 2 ? 29 : 61)),
			_ => 255,
		};

		byte Grid( int x, int y, int channel ) => channel switch
		{
			0 => grey && (x, y) == GreySample ? (byte)0x7F : (byte)255,
			1 => (byte)(x * 37 + y * 23 + seed),
			2 => Weight( mask2, x, y, 2 ),
			_ => Weight( mask1, x, y, 3 ),
		};

		(int, int) Size( int size, int image ) => (size + 1 + lcg.Next() % 24, size + 1 + lcg.Next() % 24);

		byte Pixel( int size, int image, int x, int y, int channel )
		{
			if ( image < 6 )
				return channel == 0 ? (byte)0 : lcg.Next();
			var style = image switch { 9 or 10 => mask2, 11 or 12 => mask1, _ => "detail" };
			return style switch
			{
				"all" => 255,
				"none" => 0,
				"mixed" => (lcg.Next() & 1) != 0 ? (byte)255 : (byte)0,
				_ => lcg.Next(),
			};
		}

		return SyntheticLnd.Build( Cells, Cells, parameters, Grid, Pixel, imageSize: Size );
	}

	public static LndFile Load( int seed, string mix, bool grey ) => new( new MemoryStream( Level( seed, mix, grey ) ) );
}
