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
	public void ShippedSignsComposeOpaqueFacesWithoutKeyMagenta()
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
			Assert.IsTrue( result.Left.Where( (value, index) => index % 4 == 3 ).All( value => value == 255 ), $"transparent sign1 texel in {path}" );
			Assert.IsTrue( result.Right.Where( (value, index) => index % 4 == 3 ).All( value => value == 255 ), $"transparent sign2 texel in {path}" );
			Assert.AreEqual( 0, OpaqueKeyMagenta( result.Left ) + OpaqueKeyMagenta( result.Right ), $"opaque colour-key texels in {path}" );
			if ( sign.ExtraImage == null ) continue;
			var theme = path.StartsWith( "lobby.wad/", StringComparison.Ordinal ) ? "lobby" : path.Split( '/' )[1];
			extraByTheme[theme] = extraByTheme.GetValueOrDefault( theme ) + 1;
		}
		Assert.AreEqual( 23, extraByTheme.Values.Sum() );
		CollectionAssert.AreEquivalent( new[] { "fantasy", "hallow", "jungle", "space", "lobby" }, extraByTheme.Keys.ToArray() );
	}

	[TestMethod]
	public void BellyBounceHasNoExtraImageAndGetsAnOpaqueBacking()
	{
		var data = StringTableTests.OriginalDataDirectory();
		using var stream = File.OpenRead( Path.Combine( data, "fonts.wad" ) );
		var fonts = SignFontLibrary.Load( stream );
		var (_, bytes) = SignFileTests.OriginalSignMembers( data ).Single( member => member.Path == "levels/jungle/rides/bouncy.wad/bouncy.sgn" );
		var sign = new SignFile( bytes );
		Assert.IsNull( sign.ExtraImage, "the shipped Belly Bounce sign has no board image" );
		var result = SignSurfaceComposer.Compose( sign, fonts, new[] { "Belly", "Bounce" }, new List<string>() );
		Assert.IsTrue( result.TextPixelCount > 0 );
		Assert.IsTrue( result.Left.Where( (value, index) => index % 4 == 3 ).All( value => value == 255 ) );
		Assert.IsTrue( result.Right.Where( (value, index) => index % 4 == 3 ).All( value => value == 255 ) );
	}

	[TestMethod]
	public void JungleGateLightPaintMakesNameVisibleOnDecodedBoard()
	{
		var data = StringTableTests.OriginalDataDirectory();
		using var stream = File.OpenRead( Path.Combine( data, "fonts.wad" ) );
		var fonts = SignFontLibrary.Load( stream );
		var (_, bytes) = SignFileTests.OriginalSignMembers( data ).Single( member => member.Path == "levels/jungle/features/gates.wad/gates.sgn" );
		var sign = new SignFile( bytes );
		var nativePaint = sign.Paints[0]!;
		var dark = SignSurfaceComposer.Compose( sign, fonts, new[] { "Lost Kingdom" }, new List<string>() );
		var light = SignSurfaceComposer.Compose( sign, fonts, new[] { "Lost Kingdom" }, new List<string>(),
			nativePaint with { R = 230, G = 210, B = 170, A = 255 } );
		Assert.IsTrue( light.TextPixelCount > 0 );
		var brighter = 0;
		for ( var i = 0; i < light.Left.Length; i += 4 )
			if ( light.Left[i] > dark.Left[i] + 40 && light.Left[i + 1] > dark.Left[i + 1] + 40 ) brighter++;
		for ( var i = 0; i < light.Right.Length; i += 4 )
			if ( light.Right[i] > dark.Right[i] + 40 && light.Right[i + 1] > dark.Right[i + 1] + 40 ) brighter++;
		Assert.IsTrue( brighter > 100, $"only {brighter} gate-name pixels gained contrast" );
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
