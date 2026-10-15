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
		var mask = new byte[SignSurfaceComposer.Width * SignSurfaceComposer.Height];
		mask[0] = 9;
		mask[(SignSurfaceComposer.Height - 1) * SignSurfaceComposer.Width + SignSurfaceComposer.Width - 1] = 7;
		var shifted = SignSurfaceComposer.Shift( mask, 2, 3 );
		Assert.AreEqual( 9, shifted[3 * SignSurfaceComposer.Width + 2] );
		Assert.AreEqual( 9, shifted.Sum( value => (int)value ), "the texel that left the surface is gone" );
		Assert.AreSame( mask, SignSurfaceComposer.Shift( mask, 0, 0 ) );
		var back = SignSurfaceComposer.Shift( mask, -1, -1 );
		Assert.AreEqual( 7, back[(SignSurfaceComposer.Height - 2) * SignSurfaceComposer.Width + SignSurfaceComposer.Width - 2] );
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
