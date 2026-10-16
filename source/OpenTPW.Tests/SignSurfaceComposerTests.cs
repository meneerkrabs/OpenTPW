using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SignSurfaceComposerTests
{
	[TestMethod]
	public void MaterialStripUsesIntegerBucketAverages()
	{
		var strip = new SignBitmap( 1, 2, 4, new byte[] { 1, 0, 0, 0, 0, 3, 0, 0 } );
		CollectionAssert.AreEqual( new byte[] { 0, 1, 0, 0 }, SignSurfaceComposer.DescimateStrip( strip, 1 ) );
	}

	[TestMethod]
	public void RawBoardUsesNativeAlphaBlitAndTwo128SquareDestinations()
	{
		var signBytes = SignFileTests.CreateNativeMetadataSign( (0, "Test", "TEST____.TTF", 100, 0, -100), (0, "Test", "TEST____.TTF", 100, 128, -90) );
		var board = new byte[12 + SignSurfaceComposer.Width * SignSurfaceComposer.Height * 4];
		BinaryPrimitives.WriteUInt32LittleEndian( board, SignSurfaceComposer.Width );
		BinaryPrimitives.WriteUInt32LittleEndian( board.AsSpan( 4 ), SignSurfaceComposer.Height );
		BinaryPrimitives.WriteUInt32LittleEndian( board.AsSpan( 8 ), 4 );
		for ( var i = 12; i < board.Length; i += 4 )
		{
			board[i] = 255;
			board[i + 1] = 200;
			board[i + 2] = 100;
			board[i + 3] = 50;
		}
		BinaryPrimitives.WriteUInt32LittleEndian( signBytes, 100 );
		signBytes[8] = 1;
		var sign = new SignFile( signBytes.Concat( board ).ToArray() );
		var composed = SignSurfaceComposer.Compose( sign, SignFontLibrary.FromFiles( Array.Empty<(string, byte[])>() ), new string?[] { null, null }, new List<string>() );
		Assert.AreEqual( 0, composed.TextPixelCount );
		Assert.AreEqual( SignSurfaceComposer.HalfWidth * SignSurfaceComposer.Height * 4, composed.Left.Length );
		CollectionAssert.AreEqual( new byte[] { 199, 99, 49, 255 }, composed.Left[..4] );
		CollectionAssert.AreEqual( composed.Left, composed.Right );
	}

	[TestMethod]
	public void ShippedSignsComposeTransparentFacesWithoutKeyMagenta()
	{
		var data = StringTableTests.OriginalDataDirectory();
		using var stream = File.OpenRead( Path.Combine( data, "fonts.wad" ) );
		var fonts = SignFontLibrary.Load( stream );
		var signs = SignFileTests.OriginalSignMembers( data ).ToList();
		Assert.AreEqual( 84, signs.Count );
		var extraByTheme = new Dictionary<string, int>( StringComparer.Ordinal );
		foreach ( var (path, bytes) in signs )
		{
			var sign = new SignFile( bytes );
			var diagnostics = new List<string>();
			var result = SignSurfaceComposer.Compose( sign, fonts, new[] { "Sign", "Board" }, diagnostics );
			Assert.AreEqual( SignSurfaceComposer.HalfWidth * SignSurfaceComposer.Height * 4, result.Left.Length, path );
			Assert.AreEqual( result.Left.Length, result.Right.Length, path );
			// COMPAT-030 retired: the surface starts transparent, so a sign without a board keeps transparent texels between its letters.
			if ( sign.ExtraImage == null )
				Assert.IsTrue( result.Left.Where( (value, index) => index % 4 == 3 ).Any( value => value == 0 ), $"no transparent sign1 texel in {path}" );
			else
				Assert.IsTrue( result.Left.Concat( result.Right ).Where( (value, index) => index % 4 == 3 ).Any( value => value < 255 ), $"the board sign {path} is opaque everywhere: its board alpha was lost" );
			Assert.AreEqual( 0, OpaqueKeyMagenta( result.Left ) + OpaqueKeyMagenta( result.Right ), $"opaque colour-key texels in {path}" );
			if ( sign.ExtraImage == null ) continue;
			var theme = path.StartsWith( "lobby.wad/", StringComparison.Ordinal ) ? "lobby" : path.Split( '/' )[1];
			extraByTheme[theme] = extraByTheme.GetValueOrDefault( theme ) + 1;
		}
		Assert.AreEqual( 23, extraByTheme.Values.Sum() );
		CollectionAssert.AreEquivalent( new[] { "fantasy", "hallow", "jungle", "space", "lobby" }, extraByTheme.Keys.ToArray() );
	}

	[TestMethod]
	public void BellyBounceHasNoExtraImageAndIsTransparentBetweenTheLetters()
	{
		var data = StringTableTests.OriginalDataDirectory();
		using var stream = File.OpenRead( Path.Combine( data, "fonts.wad" ) );
		var fonts = SignFontLibrary.Load( stream );
		var (_, bytes) = SignFileTests.OriginalSignMembers( data ).Single( member => member.Path == "levels/jungle/rides/bouncy.wad/bouncy.sgn" );
		var sign = new SignFile( bytes );
		Assert.IsNull( sign.ExtraImage, "the shipped Belly Bounce sign has no board image" );
		var result = SignSurfaceComposer.Compose( sign, fonts, new[] { "Belly", "Bounce" }, new List<string>() );
		Assert.IsTrue( result.TextPixelCount > 0 );
		foreach ( var half in new[] { result.Left, result.Right } )
		{
			var alpha = half.Where( (value, index) => index % 4 == 3 ).ToArray();
			Assert.IsTrue( alpha.Any( value => value == 0 ), "wood and sky show between the letters" );
			Assert.IsTrue( alpha.Any( value => value > 0 ), "the letters are drawn" );
		}
	}

	/// <summary>A partial-alpha texel is straight alpha: the colour is the board's own, not scaled by the alpha again, and transparent neighbours carry that colour for mips.</summary>
	[TestMethod]
	public void PartialAlphaTexelsKeepTheirColourAndTransparentTexelsBleedIt()
	{
		var signBytes = SignFileTests.CreateNativeMetadataSign( (0, "Test", "TEST____.TTF", 100, 0, -100), (0, "Test", "TEST____.TTF", 100, 128, -90) );
		var board = new byte[12 + SignSurfaceComposer.Width * SignSurfaceComposer.Height * 4];
		BinaryPrimitives.WriteUInt32LittleEndian( board, SignSurfaceComposer.Width );
		BinaryPrimitives.WriteUInt32LittleEndian( board.AsSpan( 4 ), SignSurfaceComposer.Height );
		BinaryPrimitives.WriteUInt32LittleEndian( board.AsSpan( 8 ), 4 );
		for ( var y = 0; y < SignSurfaceComposer.Height; y++ )
		for ( var x = 0; x < SignSurfaceComposer.Width; x++ )
		{
			var at = 12 + (y * SignSurfaceComposer.Width + x) * 4;
			if ( x >= SignSurfaceComposer.HalfWidth ) continue; // right half stays alpha 0
			board[at] = 128;
			board[at + 1] = 200;
			board[at + 2] = 100;
			board[at + 3] = 50;
		}
		BinaryPrimitives.WriteUInt32LittleEndian( signBytes, 100 );
		signBytes[8] = 1;
		var sign = new SignFile( signBytes.Concat( board ).ToArray() );
		var composed = SignSurfaceComposer.Compose( sign, SignFontLibrary.FromFiles( Array.Empty<(string, byte[])>() ), new string?[] { null, null }, new List<string>() );
		var left = composed.Left;
		Assert.AreEqual( 128, left[3] );
		Assert.IsTrue( Math.Abs( left[0] - 200 ) <= 2 && Math.Abs( left[1] - 100 ) <= 2 && Math.Abs( left[2] - 50 ) <= 2, $"straight colour expected (200, 100, 50), got ({left[0]}, {left[1]}, {left[2]})" );
		var right = composed.Right;
		Assert.AreEqual( 0, right[3] );
		Assert.IsTrue( Math.Abs( right[0] - 200 ) <= 2 && Math.Abs( right[1] - 100 ) <= 2 && Math.Abs( right[2] - 50 ) <= 2, $"a transparent texel carries its neighbours' colour, got ({right[0]}, {right[1]}, {right[2]})" );
	}

	/// <summary>The paint mask is translated by the record's offsets (right and down positive): the shadow adds covered texels beside the glyphs.</summary>
	[TestMethod]
	public void PaintOffsetsMoveTheShadowBesideTheGlyphs()
	{
		var data = StringTableTests.OriginalDataDirectory();
		using var stream = File.OpenRead( Path.Combine( data, "fonts.wad" ) );
		var fonts = SignFontLibrary.Load( stream );
		var (_, bytes) = SignFileTests.OriginalSignMembers( data ).Single( member => member.Path == "levels/jungle/rides/monkey.wad/monkey.sgn" );
		var sign = new SignFile( bytes );
		Assert.IsNull( sign.ExtraImage, "Wilde Aap has no board, so every covered texel is paint or effect" );
		var nativePaint = sign.Paints[0]!;
		var flat = SignSurfaceComposer.Compose( sign, fonts, new[] { "Wilde", "Aap" }, new List<string>(), nativePaint with { OffsetX = 0, OffsetY = 0 } );
		var shadow = SignSurfaceComposer.Compose( sign, fonts, new[] { "Wilde", "Aap" }, new List<string>(), nativePaint with { OffsetX = 6, OffsetY = 6 } );
		int Covered( (byte[] Left, byte[] Right, int Pixels) result ) => result.Left.Concat( result.Right ).Where( (value, index) => index % 4 == 3 && value > 0 ).Count();
		Assert.IsTrue( Covered( shadow ) > Covered( flat ), "an offset paint covers texels the glyphs alone do not" );
	}

	[TestMethod]
	public void ShiftMovesAMaskRightAndDownAndDropsWhatLeavesTheSurface()
	{
		var mask = new byte[SignSurfaceComposer.Width * SignSurfaceComposer.SurfaceHeight];
		mask[0] = 9;
		mask[(SignSurfaceComposer.SurfaceHeight - 1) * SignSurfaceComposer.Width + SignSurfaceComposer.Width - 1] = 7;
		var shifted = SignSurfaceComposer.Shift( mask, 2, 3 );
		Assert.AreEqual( 9, shifted[3 * SignSurfaceComposer.Width + 2] );
		Assert.AreEqual( 9, shifted.Sum( value => (int)value ), "the texel that left the surface is gone" );
		Assert.AreSame( mask, SignSurfaceComposer.Shift( mask, 0, 0 ) );
		var back = SignSurfaceComposer.Shift( mask, -1, -1 );
		Assert.AreEqual( 7, back[(SignSurfaceComposer.SurfaceHeight - 2) * SignSurfaceComposer.Width + SignSurfaceComposer.Width - 2] );
	}

	/// <summary>The 512x512 text raster is reduced to the 256x256 mask by summing every 2x2 block of coverage bytes and shifting right by 2 (tp.exe 0x5EAFC3), not by two truncated halvings.</summary>
	[TestMethod]
	public void TextReductionSumsEachTwoByTwoBlockAndShiftsRightByTwo()
	{
		const int textWidth = SignSurfaceComposer.Width * 2;
		var text = new byte[textWidth * SignSurfaceComposer.SurfaceHeight * 2 * 4];
		void Coverage( int x, int y, byte value ) => text[(y * textWidth + x) * 4 + 3] = value;
		// block (0, 0): 1, 0 / 3, 0 sums to 4 and gives 1 (two truncated halvings would give 0)
		Coverage( 0, 0, 1 );
		Coverage( 0, 1, 3 );
		// block (1, 0): 3, 3 / 3, 2 sums to 11 and gives 2
		Coverage( 2, 0, 3 ); Coverage( 3, 0, 3 ); Coverage( 2, 1, 3 ); Coverage( 3, 1, 2 );
		// block (2, 0): all 255 stays 255; block (3, 0): three 255 and one 0 gives 191
		for ( var dy = 0; dy < 2; dy++ )
		for ( var dx = 0; dx < 2; dx++ )
			Coverage( 4 + dx, dy, 255 );
		Coverage( 6, 0, 255 ); Coverage( 7, 0, 255 ); Coverage( 6, 1, 255 );
		// the lowest block: DIB rows 510 and 511 reach mask row 255
		Coverage( 20, 510, 255 ); Coverage( 21, 511, 255 );
		var mask = SignSurfaceComposer.ReduceText( text );
		Assert.AreEqual( SignSurfaceComposer.Width * SignSurfaceComposer.SurfaceHeight, mask.Length );
		CollectionAssert.AreEqual( new byte[] { 1, 2, 255, 191 }, mask[..4] );
		Assert.AreEqual( 127, mask[255 * SignSurfaceComposer.Width + 10] );
	}

	private static SignEffect Relief( float p0, float @base, float diffuse, float specular, float exponent, float theta, float phi, uint maskWord = 0, float blur = 0f ) =>
		new( 1, maskWord, new[] { p0, blur, @base, diffuse, specular, exponent, theta, phi }, 0, 0 );

	private static SignBitmap Strip( int width, int height, Func<int, byte> red ) =>
		new( width, height, 4, Enumerable.Range( 0, width * height ).SelectMany( i => new byte[] { 255, red( i / width ), red( i / width ), red( i / width ) } ).ToArray() );

	/// <summary>The relief reads the shape byte after the current one and the byte one row below without a clamp: at the last column the next byte is the first pixel of the next row (tp.exe 0x5EB8A0).</summary>
	[TestMethod]
	public void ReliefReadsTheNextShapeByteWithoutClampingAtTheLastColumn()
	{
		var mask = new byte[SignSurfaceComposer.Width * SignSurfaceComposer.SurfaceHeight];
		mask[10 * SignSurfaceComposer.Width + 100] = 200;
		mask[10 * SignSurfaceComposer.Width + SignSurfaceComposer.Width - 1] = 200;
		// the light comes from the left (phi 180 degrees), the bump scale is large: an isolated texel has a right neighbour of 0 and tilts towards the light
		var effect = Relief( 100f, 0f, 1f, 0f, 1f, 0f, 180f );
		var destination = new byte[SignSurfaceComposer.Width * SignSurfaceComposer.SurfaceHeight * 4];
		SignSurfaceComposer.Effect( destination, mask, Strip( 1, 1, _ => 200 ), effect, new List<string>() );
		var inside = destination[(10 * SignSurfaceComposer.Width + 100) * 4 + 1];
		var edge = destination[(10 * SignSurfaceComposer.Width + SignSurfaceComposer.Width - 1) * 4 + 1];
		Assert.IsTrue( inside > 0, "the lit isolated texel is coloured" );
		Assert.AreEqual( inside, edge, "the last column sees the next row's first texel (0), as an interior isolated texel sees its neighbour (0); a clamp would see itself and leave it unlit" );
	}

	[TestMethod]
	public void ReliefInTheLastRowReadsNothingBelowAndDoesNotThrow()
	{
		var mask = new byte[SignSurfaceComposer.Width * SignSurfaceComposer.SurfaceHeight];
		mask[(SignSurfaceComposer.SurfaceHeight - 1) * SignSurfaceComposer.Width + 50] = 255;
		var destination = new byte[mask.Length * 4];
		SignSurfaceComposer.Effect( destination, mask, Strip( 1, 1, _ => 200 ), Relief( 100f, 0.5f, 0f, 0f, 1f, 0f, 0f ), new List<string>() );
		Assert.AreEqual( 255, destination[((SignSurfaceComposer.SurfaceHeight - 1) * SignSurfaceComposer.Width + 50) * 4] );
	}

	/// <summary>The measured text extent spans the whole 256-row mask, not only the 128 rows that reach the textures: the material strip is resampled to it (tp.exe 0x5EB035, 0x5ECCC5).</summary>
	[TestMethod]
	public void TheReliefExtentCoversTextBelowThePackedRows()
	{
		var mask = new byte[SignSurfaceComposer.Width * SignSurfaceComposer.SurfaceHeight];
		for ( var x = 10; x < 20; x++ )
		{
			mask[10 * SignSurfaceComposer.Width + x] = 255;
			mask[200 * SignSurfaceComposer.Width + x] = 255;
		}
		var destination = new byte[mask.Length * 4];
		// 191 strip rows for the 191 rows between the first and the last text row: the strip row of each text row is its distance from the top
		SignSurfaceComposer.Effect( destination, mask, Strip( 1, 191, row => (byte)row ), Relief( 0f, 1f, 0f, 0f, 1f, 0f, 0f ), new List<string>() );
		var top = destination[(10 * SignSurfaceComposer.Width + 15) * 4 + 1];
		var bottom = destination[(200 * SignSurfaceComposer.Width + 15) * 4 + 1];
		Assert.IsTrue( top <= 1, $"the first text row samples strip row 0, got {top}" );
		Assert.IsTrue( Math.Abs( bottom - 190 ) <= 1, $"the row 190 below it samples strip row 190, got {bottom}" );
	}

	/// <summary>The mask word of an effect record erodes the shape the normals are built from: a lit bevel appears along the edge of a block.</summary>
	[TestMethod]
	public void ReliefNormalsFollowTheErodedShapeNotTheRawCoverage()
	{
		var mask = new byte[SignSurfaceComposer.Width * SignSurfaceComposer.SurfaceHeight];
		for ( var y = 20; y < 40; y++ )
		for ( var x = 20; x < 60; x++ )
			mask[y * SignSurfaceComposer.Width + x] = 255;
		byte[] Render( uint word )
		{
			var destination = new byte[mask.Length * 4];
			SignSurfaceComposer.Effect( destination, mask, Strip( 1, 20, _ => 200 ), Relief( 8f, 0.2f, 1f, 0f, 1f, 0f, 0f, word ), new List<string>() );
			return destination;
		}
		var flat = Render( 0 );
		var bevel = Render( 5 );
		int Green( byte[] image, int x, int y ) => image[(y * SignSurfaceComposer.Width + x) * 4 + 2];
		// inside a block of binary ink the shape is flat, so the normal is straight up and every interior texel has the same colour; the bevel adds a slope two texels in
		Assert.AreEqual( Green( flat, 30, 30 ), Green( flat, 40, 30 ), "a flat shape shades every interior texel alike" );
		Assert.AreNotEqual( Green( flat, 22, 30 ), Green( bevel, 22, 30 ), "the eroded shape slopes near the edge" );
		Assert.AreEqual( 255, flat[(30 * SignSurfaceComposer.Width + 30) * 4 ], "alpha is the glyph coverage, not the shape" );
	}

	/// <summary>
	/// COMPAT-006: sign text is never pair-kerned. The sign line goes through Uniscribe, whose placement (usp10.dll ScriptPlace 0x6510CEDE, the generic
	/// routine 0x65113458 and the width cache 0x6510F241) sums per-glyph advances and reads no kern table, so a font with a kern pair for "AB"
	/// gives the same mask as the same font without it.
	/// </summary>
	[TestMethod]
	public void SignMasksIgnoreTheKernTableOfTheirFont()
	{
		byte[] Font( bool kern )
		{
			var builder = new TrueTypeFontTests.FontBuilder();
			var a = builder.AddSimple( 600, 'A', TrueTypeFontTests.Square( 0, 0, 500, 500 ) );
			var b = builder.AddSimple( 700, 'B', TrueTypeFontTests.Square( 0, 0, 500, 500 ) );
			if ( kern )
				builder.AddKerning( a, b, -300 );
			return builder.Build();
		}
		var sign = new SignFile( SignFileTests.CreateNativeMetadataSign( (0, "Test", "TEST____.TTF", 100, 0, -100), (0, "Test", "TEST____.TTF", 100, 128, -90) ) );
		byte[] Mask( bool kern ) => SignSurfaceComposer.RenderMask( sign, SignFontLibrary.FromFiles( new[] { ("TEST____.TTF", Font( kern )) } ), new string?[] { "AB", null }, 0, new List<string>() );
		var plain = Mask( false );
		Assert.IsTrue( plain.Any( value => value != 0 ), "the line is drawn" );
		CollectionAssert.AreEqual( plain, Mask( true ), "a kern pair does not move the glyphs" );
	}

	private static int OpaqueKeyMagenta( byte[] rgba )
	{
		var count = 0;
		for ( var i = 0; i < rgba.Length; i += 4 )
			if ( rgba[i] == 255 && rgba[i + 1] == 0 && rgba[i + 2] == 255 && rgba[i + 3] == 255 )
				count++;
		return count;
	}
}
