using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The procedural terrain texturer on synthetic levels (docs/LND.md): ramp tables, every run mode, the three layer weight quirk, bottom-up rows,
/// the grey fallback and the output layouts. The byte-exact comparison with tp.exe's routine is in <see cref="ProceduralTerrainCorpusTests"/>.
/// </summary>
[TestClass]
public class ProceduralTerrainTexturerTests
{
	// Mask ramps with a hard step (centre 128, width 0): 0 below 128, 255 above. Layer ramps A: (0, 0), B: (0, 0), C: (0, 0) are 255 above index 0.
	private static readonly byte[] StepParameters = [0, 0, 0, 0, 128, 0, 0, 0, 128, 0];

	private static readonly int[][] ColourA = [[10, 20, 30], [200, 150, 100]];
	private static readonly int[][] ColourB = [[60, 70, 80], [40, 30, 20]];
	private static readonly int[][] ColourC = [[250, 240, 230], [5, 15, 25]];

	private static int Lerp( int first, int second, int t ) => (byte)(second + (((first - second) * t) >> 8));

	private static int[] Layer( int[][] colours, int t ) => [Lerp( colours[0][0], colours[1][0], t ), Lerp( colours[0][1], colours[1][1], t ), Lerp( colours[0][2], colours[1][2], t )];

	private static int[] Mix( int[] first, int[] second, int t ) => [Lerp( first[0], second[0], t ), Lerp( first[1], second[1], t ), Lerp( first[2], second[2], t )];

	/// <summary>Masks and weights that give a mode: mask 1 / mask 2 are all (255 everywhere), none (0) or mixed (255 on odd image columns).</summary>
	private static LndFile ModeLevel( string mask1, string mask2, byte[]? parameters = null, Func<int, int, byte>? weight1 = null )
	{
		static byte Weight( string style ) => style == "none" ? (byte)0 : (byte)255;
		static byte Mask( string style, int x ) => style switch { "all" => 255, "none" => 0, _ => (x & 1) == 1 ? (byte)255 : (byte)0 };
		byte Grid( int x, int y, int channel ) => channel switch
		{
			0 => 255,
			1 => weight1?.Invoke( x, y ) ?? 255,
			2 => Weight( mask2 ),
			_ => Weight( mask1 ),
		};
		byte Pixel( int size, int image, int x, int y, int channel )
		{
			var rgb = channel - 1;
			return image switch
			{
				0 or 1 => (byte)(channel == 0 ? 0 : ColourA[image][rgb]),
				2 or 3 => (byte)(channel == 0 ? 0 : ColourB[image - 2][rgb]),
				4 or 5 => (byte)(channel == 0 ? 0 : ColourC[image - 4][rgb]),
				6 or 7 or 8 => 255,
				9 or 10 => Mask( mask2, x ),
				_ => Mask( mask1, x ),
			};
		}
		return new LndFile( new MemoryStream( SyntheticLnd.Build( 2, 2, parameters ?? StepParameters, Grid, Pixel ) ) );
	}

	private static byte[] Build( LndFile lnd, int size, int x = 0, int z = 0 )
	{
		var texturer = new ProceduralTerrainTexturer( lnd, size );
		var buffer = new byte[texturer.TextureBytes];
		Assert.IsTrue( texturer.Build( x, z, buffer ) );
		return buffer;
	}

	private static int[] Pixel( byte[] buffer, int size, int row, int column ) => [buffer[(row * size + column) * 4 + 1], buffer[(row * size + column) * 4 + 2], buffer[(row * size + column) * 4 + 3]];

	private static void AssertAll( byte[] buffer, int size, Func<int, int[]> expectedForColumn, string mode )
	{
		for ( var row = 0; row < size; row++ )
		{
			for ( var column = 0; column < size; column++ )
			{
				Assert.AreEqual( 0, buffer[(row * size + column) * 4], $"{mode}: alpha byte of ({column}, {row}) is 0 as in the original" );
				CollectionAssert.AreEqual( expectedForColumn( column ), Pixel( buffer, size, row, column ), $"{mode}: pixel ({column}, {row})" );
			}
		}
	}

	[TestMethod]
	public void RampTableMatchesTheFormula()
	{
		var table = new byte[256];
		ProceduralTerrainTexturer.BuildRamp( table, new LndRamp( 48, 40 ) );
		// h = 20: below 28 zero, above 68 full, 255 * (i - 28) / 41 between (truncating)
		Assert.AreEqual( 0, table[0] );
		Assert.AreEqual( 0, table[28] );
		Assert.AreEqual( 6, table[29] );
		Assert.AreEqual( 255 * 20 / 41, table[48] );
		Assert.AreEqual( 255 * 40 / 41, table[68] );
		Assert.AreEqual( 255, table[69] );
		Assert.AreEqual( 255, table[255] );
		// a step: width 0 puts the centre entry at zero and everything above at 255
		ProceduralTerrainTexturer.BuildRamp( table, new LndRamp( 128, 0 ) );
		Assert.AreEqual( 0, table[127] );
		Assert.AreEqual( 0, table[128] );
		Assert.AreEqual( 255, table[129] );
		// a centre near zero: the low end is negative and every entry up to the centre is on the slope
		ProceduralTerrainTexturer.BuildRamp( table, new LndRamp( 3, 10 ) );
		Assert.AreEqual( 255 * (0 - (3 - 5)) / 11, table[0] );
		Assert.AreEqual( 255, table[9] );
		Assert.ThrowsException<ArgumentException>( () => ProceduralTerrainTexturer.BuildRamp( new byte[255], new LndRamp( 1, 1 ) ) );
	}

	[TestMethod]
	public void EveryRunModeBlendsItsLayers()
	{
		var a = Layer( ColourA, 255 );
		var b = Layer( ColourB, 255 );
		var c = Layer( ColourC, 255 );
		foreach ( var size in new[] { 16, 32 } )
		{
			AssertAll( Build( ModeLevel( "all", "all" ), size ), size, _ => c, "C" );
			AssertAll( Build( ModeLevel( "none", "all" ), size ), size, _ => b, "B" );
			AssertAll( Build( ModeLevel( "none", "none" ), size ), size, _ => a, "A" );
			// odd columns have mask 255, even columns 0
			AssertAll( Build( ModeLevel( "none", "mixed" ), size ), size, x => (x & 1) == 1 ? Mix( b, a, 255 ) : a, "BA" );
			AssertAll( Build( ModeLevel( "mixed", "all" ), size ), size, x => (x & 1) == 1 ? Mix( c, b, 255 ) : b, "CB" );
			AssertAll( Build( ModeLevel( "mixed", "none" ), size ), size, x => (x & 1) == 1 ? Mix( c, a, 255 ) : a, "CA" );
			AssertAll( Build( ModeLevel( "mixed", "mixed" ), size ), size, x => (x & 1) == 1 ? Mix( c, Mix( b, a, 255 ), 255 ) : a, "CBA" );
		}
	}

	[TestMethod]
	public void ARunOfThreeLayersUsesTheFirstTexelsWeightOne()
	{
		// Layer A ramp (128, 255) is a slope over 0..255, so its blend depends on weight 1; the weight grid rises along x.
		var parameters = (byte[])StepParameters.Clone();
		parameters[0] = 128;
		parameters[1] = 255;
		var lnd = ModeLevel( "mixed", "mixed", parameters, ( x, _ ) => (byte)(16 * (x + 1)) );
		foreach ( var size in new[] { 16, 32 } )
		{
			var buffer = Build( lnd, size );
			var table = new byte[256];
			ProceduralTerrainTexturer.BuildRamp( table, new LndRamp( 128, 255 ) );
			for ( var run = 0; run < size; run += 16 )
			{
				// Even columns (mask 1 and mask 2 zero) show layer A with the weight of the run's first texel, not their own.
				var first = Pixel( buffer, size, 0, run );
				for ( var column = run; column < run + 16; column += 2 )
					CollectionAssert.AreEqual( first, Pixel( buffer, size, 0, column ), $"size {size} run {run} column {column}" );
			}
			// and that weight is the first texel's (grid sample 0 of the cell holds 16): the blend index is (255 * 16) >> 8
			var t = table[(255 * 16) >> 8];
			CollectionAssert.AreEqual( Layer( ColourA, t ), Pixel( buffer, size, 0, 0 ) );
			// whereas the two layer mode takes each texel's own weight 1: the same level with only mask 2 mixed shows the slope across even columns
			var twoLayers = Build( ModeLevel( "none", "mixed", parameters, ( x, _ ) => (byte)(16 * (x + 1)) ), size );
			Assert.AreNotEqual( Pixel( twoLayers, size, 0, 0 ).Sum(), Pixel( twoLayers, size, 0, 14 ).Sum(), "two layer runs vary with weight 1" );
		}
	}

	[TestMethod]
	public void RowsRunBottomUpAndWindowsFollowTheCell()
	{
		// Layer A only; both colour images hold the image row in red, so the output shows which source row each buffer row came from.
		byte Rows( int size, int image, int x, int y, int channel ) => image switch
		{
			0 or 1 => (byte)(channel == 1 ? y : 0),
			6 => 255,
			_ => 0,
		};
		var lnd = new LndFile( new MemoryStream( SyntheticLnd.Build( 2, 3, StepParameters, ( x, y, channel ) => channel == 3 || channel == 2 ? (byte)0 : (byte)255, Rows ) ) );
		foreach ( var size in new[] { 16, 32 } )
		{
			// heightfield row z = 1 of three is file row 3 - 1 - 1 = 1; the window starts at row (size * 1) % (height - size) = size % 6
			var buffer = Build( lnd, size, 1, 1 );
			var windowRow = size * 1 % 6;
			for ( var row = 0; row < size; row++ )
			{
				var sourceRow = size - 1 - row;
				Assert.AreEqual( windowRow + sourceRow, buffer[(row * size + 3) * 4 + 1], $"size {size} buffer row {row}" );
			}
		}
	}

	[TestMethod]
	public void CellsWithAnInvalidInnerSampleAreGrey()
	{
		// Sample (4x + 1, 4 row + 1) of cell (0, 0) in file row 1 (two cells down, z = 0): sample (1, 5)
		byte Grid( int x, int y, int channel ) => channel == 0 ? (x == 1 && y == 5 ? (byte)0xFE : (byte)255) : (byte)255;
		var lnd = new LndFile( new MemoryStream( SyntheticLnd.Build( 2, 2, StepParameters, Grid, ( _, _, _, _, _ ) => 100 ) ) );
		var texturer = new ProceduralTerrainTexturer( lnd, 16 );
		var buffer = new byte[texturer.TextureBytes];
		Array.Fill( buffer, (byte)1 );
		Assert.IsFalse( texturer.Build( 0, 0, buffer ), "z = 0 is file row 1" );
		for ( var index = 0; index < buffer.Length; index += 4 )
			CollectionAssert.AreEqual( new byte[] { 0, 0x7F, 0x7F, 0x7F }, buffer[index..( index + 4 )] );
		Assert.IsTrue( texturer.Build( 1, 0, buffer ), "the neighbouring cell is valid" );
		Assert.IsTrue( texturer.Build( 0, 1, buffer ), "the cell above is valid" );
		// An invalid sample outside the four inner samples of the cell does not matter.
		byte EdgeGrid( int x, int y, int channel ) => channel == 0 && x == 0 && y == 5 ? (byte)0 : (byte)255;
		var edge = new ProceduralTerrainTexturer( new LndFile( new MemoryStream( SyntheticLnd.Build( 2, 2, StepParameters, EdgeGrid, ( _, _, _, _, _ ) => 100 ) ) ), 16 );
		Assert.IsTrue( edge.Build( 0, 0, buffer ) );
	}

	[TestMethod]
	public void ArgbHasTheBytesOfEachPixelReversed()
	{
		var lnd = ModeLevel( "none", "mixed" );
		var texturer = new ProceduralTerrainTexturer( lnd, 16 );
		var head = new byte[texturer.TextureBytes];
		var argb = new byte[texturer.TextureBytes];
		texturer.Build( 1, 1, head );
		texturer.BuildArgb( 1, 1, argb );
		for ( var index = 0; index < head.Length; index += 4 )
		{
			Assert.AreEqual( head[index + 3], argb[index], "blue first" );
			Assert.AreEqual( head[index + 2], argb[index + 1] );
			Assert.AreEqual( head[index + 1], argb[index + 2], "red third" );
			Assert.AreEqual( 0, argb[index + 3], "alpha 0, as in the original; the renderer writes 255" );
		}
	}

	[TestMethod]
	public void BuildIsDeterministicAndOnlyWritesOneTexture()
	{
		var lnd = ModeLevel( "mixed", "mixed" );
		var texturer = new ProceduralTerrainTexturer( lnd, 32 );
		var first = new byte[texturer.TextureBytes + 8];
		var second = new byte[texturer.TextureBytes + 8];
		Array.Fill( first, (byte)0xAA );
		Array.Fill( second, (byte)0xBB );
		texturer.Build( 1, 0, first );
		texturer.Build( 0, 1, second );
		texturer.Build( 1, 0, second );
		CollectionAssert.AreEqual( first[..texturer.TextureBytes], second[..texturer.TextureBytes], "the same cell twice gives the same bytes, whatever was built in between" );
		CollectionAssert.AreEqual( Enumerable.Repeat( (byte)0xAA, 8 ).ToArray(), first[texturer.TextureBytes..], "bytes after the texture are untouched" );
	}

	[TestMethod]
	public void RejectsBadArguments()
	{
		var lnd = ModeLevel( "all", "all" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => new ProceduralTerrainTexturer( lnd, 8 ) );
		Assert.ThrowsException<ArgumentNullException>( () => new ProceduralTerrainTexturer( null!, 16 ) );
		var texturer = new ProceduralTerrainTexturer( lnd, 16 );
		Assert.AreEqual( 2, texturer.CellsX );
		Assert.AreEqual( 2, texturer.CellsY );
		Assert.AreEqual( 16 * 16 * 4, texturer.TextureBytes );
		Assert.ThrowsException<ArgumentException>( () => texturer.Build( 0, 0, new byte[texturer.TextureBytes - 1] ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => texturer.Build( 3, 1, new byte[texturer.TextureBytes] ), "x beyond the weight grid" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => texturer.Build( 2, 0, new byte[texturer.TextureBytes] ), "the column after the last one reads past the grid at its last row" );
		Assert.IsTrue( texturer.Build( 2, 1, new byte[texturer.TextureBytes] ), "the column after the last one reads on into the next sample row" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => texturer.Build( 0, 2, new byte[texturer.TextureBytes] ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => texturer.Build( -1, 0, new byte[texturer.TextureBytes] ) );
	}
}
