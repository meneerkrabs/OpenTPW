using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class FontAtlasTests
{
	internal sealed record SyntheticGlyph( char Character, int Width, int Height, int OffsetX, int OffsetY, int Advance, byte[]? Coverage = null );

	internal static FontFile CreateFont( byte heightHint, params SyntheticGlyph[] glyphs )
	{
		var records = glyphs.Select( glyph =>
		{
			var coverage = glyph.Coverage ?? Enumerable.Range( 0, glyph.Width * glyph.Height ).Select( sample => (byte)(1 + sample % 15) ).ToArray();
			var packed = new byte[(coverage.Length + 1) / 2];
			for ( var sample = 0; sample < coverage.Length; sample++ )
				packed[sample / 2] |= (byte)(coverage[sample] << (sample % 2 == 0 ? 4 : 0));
			var record = new byte[24 + packed.Length];
			BinaryPrimitives.WriteUInt16LittleEndian( record, glyph.Character );
			BinaryPrimitives.WriteUInt32LittleEndian( record.AsSpan( 4 ), (uint)packed.Length );
			BinaryPrimitives.WriteUInt32LittleEndian( record.AsSpan( 8 ), (uint)packed.Length );
			BinaryPrimitives.WriteUInt16LittleEndian( record.AsSpan( 16 ), (ushort)glyph.Width );
			BinaryPrimitives.WriteUInt16LittleEndian( record.AsSpan( 18 ), (ushort)glyph.Height );
			record[20] = unchecked((byte)(sbyte)glyph.OffsetX);
			record[21] = unchecked((byte)(sbyte)glyph.OffsetY);
			BinaryPrimitives.WriteInt16LittleEndian( record.AsSpan( 22 ), (short)glyph.Advance );
			packed.CopyTo( record, 24 );
			return record;
		} ).ToArray();
		var data = new List<byte>();
		data.AddRange( "F4FB"u8.ToArray() );
		data.Add( 1 );
		data.Add( heightHint );
		data.AddRange( BitConverter.GetBytes( (ushort)glyphs.Length ) );
		var offset = 8 + glyphs.Length * 4;
		foreach ( var record in records )
		{
			data.AddRange( BitConverter.GetBytes( (uint)offset ) );
			offset += record.Length;
		}
		foreach ( var record in records )
			data.AddRange( record );
		return new FontFile( new MemoryStream( data.ToArray() ) );
	}

	[TestMethod]
	public void AtlasMapsCoverageToAlphaAndPreservesMetrics()
	{
		var font = CreateFont( 9,
			new SyntheticGlyph( 'A', 3, 2, -1, 2, 4, new byte[] { 0, 1, 2, 13, 14, 15 } ),
			new SyntheticGlyph( ' ', 0, 0, 0, 0, 3 ),
			new SyntheticGlyph( 'g', 2, 4, 0, 4, 3 ) );
		var atlas = new FontAtlas( font );
		Assert.AreEqual( 64, atlas.Width );
		Assert.AreEqual( 8, atlas.Height );
		Assert.AreEqual( 9, atlas.LineHeight );
		var a = atlas.Glyphs['A'];
		Assert.AreEqual( (3, 2, -1, 2, 4, 0), (a.Width, a.Height, a.OffsetX, a.OffsetY, a.Advance, a.SourceIndex) );
		CollectionAssert.AreEqual( new byte[] { 0, 17, 34, 221, 238, 255 }, ReadRegion( atlas, a ) );
		var space = atlas.Glyphs[' '];
		Assert.AreEqual( (0, 0, 3), (space.Width, space.Height, space.Advance) );
		var g = atlas.Glyphs['g'];
		Assert.AreEqual( (FontAtlas.Padding, FontAtlas.Padding), (g.X, g.Y), "Tallest glyph is packed first." );
		Assert.AreEqual( g.X + g.Width + FontAtlas.Padding, a.X );
		CollectionAssert.AreEqual( font.Glyphs[2].Coverage.Select( value => (byte)(value * 17) ).ToArray(), ReadRegion( atlas, g ) );
		Assert.AreEqual( atlas.Alpha.Count( value => value != 0 ), 5 + 8 );
		var rgba = atlas.ToRgba();
		Assert.AreEqual( atlas.Alpha.Length * 4, rgba.Length );
		Assert.IsTrue( Enumerable.Range( 0, atlas.Alpha.Length ).All( pixel => rgba[pixel * 4] == 255 && rgba[pixel * 4 + 1] == 255 && rgba[pixel * 4 + 2] == 255 && rgba[pixel * 4 + 3] == atlas.Alpha[pixel] ) );
	}

	[TestMethod]
	public void AtlasUsesFirstDuplicateRecordAndIsDeterministic()
	{
		var glyphs = new[]
		{
			new SyntheticGlyph( 'x', 2, 2, 0, 0, 3 ),
			new SyntheticGlyph( 'x', 5, 5, 0, 0, 9 ),
			new SyntheticGlyph( 'y', 2, 3, 0, 0, 3 ),
		};
		var first = new FontAtlas( CreateFont( 5, glyphs ) );
		var second = new FontAtlas( CreateFont( 5, glyphs ) );
		Assert.AreEqual( 2, first.Glyphs.Count );
		Assert.AreEqual( (2, 3, 0), (first.Glyphs['x'].Width, first.Glyphs['x'].Advance, first.Glyphs['x'].SourceIndex) );
		CollectionAssert.AreEqual( first.Alpha, second.Alpha );
		Assert.AreEqual( first.Glyphs['y'], second.Glyphs['y'] );
	}

	[TestMethod]
	public void AtlasGrowsToFitAndUsesHintFallback()
	{
		var glyphs = Enumerable.Range( 0, 40 ).Select( index => new SyntheticGlyph( (char)('!' + index), 20, 30, 0, index % 3, 21 ) ).ToArray();
		var font = CreateFont( 0, glyphs );
		var atlas = new FontAtlas( font );
		Assert.AreEqual( 256, atlas.Width, "Six 20-pixel columns at 128 need 218 rows, exceeding a square atlas." );
		Assert.AreEqual( 128, atlas.Height );
		Assert.AreEqual( 32, atlas.LineHeight, "Zero header hint falls back to the lowest glyph bottom." );
		AssertPacking( atlas, font );
		Assert.ThrowsException<InvalidDataException>( () => new FontAtlas( CreateFont( 1, new SyntheticGlyph( 'w', FontAtlas.MaximumSize - 1, 1, 0, 0, 1 ) ) ) );
	}

	[TestMethod]
	public void LayoutAppliesAdvanceOffsetsAndLineBreaks()
	{
		var atlas = new FontAtlas( CreateFont( 10,
			new SyntheticGlyph( 'A', 3, 4, 0, 2, 4 ),
			new SyntheticGlyph( 'j', 2, 6, -1, 3, 2 ),
			new SyntheticGlyph( ' ', 0, 0, 0, 0, 3 ),
			new SyntheticGlyph( '?', 2, 4, 0, 2, 3 ) ) );
		var layout = TextLayout.Create( atlas, "Aj A\r\njA\rA" );
		Assert.AreEqual( 3, layout.LineCount );
		Assert.AreEqual( 30, layout.Height );
		Assert.AreEqual( 13, layout.Width );
		Assert.AreEqual( "A0,2 j3,3 A9,2 j-1,13 A2,12 A0,22", Describe( layout ) );
		Assert.AreEqual( 0, layout.MissingCharacters.Count );

		var missing = TextLayout.Create( atlas, "Aé\tA" );
		Assert.AreEqual( "A0,2 ?4,2 ?7,2 A10,2", Describe( missing ) );
		CollectionAssert.AreEqual( new[] { 'é', '\t' }, missing.MissingCharacters.ToArray() );
		var skipped = TextLayout.Create( atlas, "AéA", fallback: '#' );
		Assert.AreEqual( "A0,2 A4,2", Describe( skipped ) );
		Assert.AreEqual( 8, skipped.Width );

		var empty = TextLayout.Create( atlas, "" );
		Assert.AreEqual( (1, 10, 0, 0), (empty.LineCount, empty.Height, empty.Width, empty.Glyphs.Count) );
	}

	[TestMethod]
	public void LayoutWrapsAtSpacesAndBreaksOverlongWords()
	{
		var atlas = new FontAtlas( CreateFont( 10,
			new SyntheticGlyph( 'a', 3, 3, 0, 0, 4 ),
			new SyntheticGlyph( ' ', 0, 0, 0, 0, 2 ) ) );
		var wrapped = TextLayout.Create( atlas, "aa aa  aaa", 18 );
		Assert.AreEqual( 2, wrapped.LineCount );
		Assert.AreEqual( "a0,0 a4,0 a10,0 a14,0 a0,10 a4,10 a8,10", Describe( wrapped ) );
		Assert.AreEqual( 18, wrapped.Width, "Wrapping fits whole words before the maximum when they are reachable." );

		var narrow = TextLayout.Create( atlas, "aa aa  aaa", 9 );
		Assert.AreEqual( "a0,0 a4,0 a0,10 a4,10 a0,20 a4,20 a0,30", Describe( narrow ) );
		Assert.IsTrue( narrow.Width <= 9 );
		Assert.AreEqual( 4, narrow.LineCount );
	}

	[TestMethod]
	public void OnlySpaceAndTabAreBreakOpportunitiesAsInTheOriginalFormatter()
	{
		var atlas = new FontAtlas( CreateFont( 10,
			new SyntheticGlyph( 'a', 3, 3, 0, 0, 4 ),
			new SyntheticGlyph( '-', 3, 3, 0, 0, 4 ),
			new SyntheticGlyph( '\t', 0, 0, 0, 0, 2 ),
			new SyntheticGlyph( ' ', 0, 0, 0, 0, 2 ) ) );
		// tp.exe 0x6aeab0 flags space and tab (0x12) as break opportunities and nothing else: a hyphen does not break, a tab does
		Assert.AreEqual( 4, TextLayout.Create( atlas, "a-a-a-a", 8 ).LineCount, "no break at the hyphens: the run breaks between characters, two glyphs a line" );
		Assert.AreEqual( 2, TextLayout.Create( atlas, "aa\taa", 8 ).LineCount, "the tab is a break opportunity and the line drops it" );
	}

	[TestMethod]
	public void MissingSpaceWrapsAtItsSourceCharacter()
	{
		var atlas = new FontAtlas( CreateFont( 10,
			new SyntheticGlyph( 'a', 2, 2, 0, 0, 4 ),
			new SyntheticGlyph( '?', 2, 2, 0, 0, 4 ) ) );
		// the atlas has no space: the fallback '?' is drawn for it, but the source character is still a break opportunity
		var layout = TextLayout.Create( atlas, "a a", 9 );
		Assert.AreEqual( 2, layout.LineCount );
		CollectionAssert.AreEqual( new[] { 4, 4 }, layout.LineWidths.ToArray() );
		Assert.AreEqual( 4, layout.Width );
		Assert.AreEqual( "a0,0 a0,10", Describe( layout ) );
		CollectionAssert.AreEqual( new[] { ' ' }, layout.MissingCharacters.ToArray() );
	}

	[TestMethod]
	public void MissingTabWrapsAtItsSourceCharacter()
	{
		var atlas = new FontAtlas( CreateFont( 10,
			new SyntheticGlyph( 'a', 2, 2, 0, 0, 4 ),
			new SyntheticGlyph( '?', 2, 2, 0, 0, 4 ) ) );
		var layout = TextLayout.Create( atlas, "a\ta", 9 );
		Assert.AreEqual( 2, layout.LineCount );
		CollectionAssert.AreEqual( new[] { 4, 4 }, layout.LineWidths.ToArray() );
		Assert.AreEqual( 4, layout.Width );
		Assert.AreEqual( "a0,0 a0,10", Describe( layout ) );
		CollectionAssert.AreEqual( new[] { '\t' }, layout.MissingCharacters.ToArray() );
	}

	[TestMethod]
	public void MissingOrdinaryCharacterWithSpaceFallbackStaysInTheWord()
	{
		var atlas = new FontAtlas( CreateFont( 10,
			new SyntheticGlyph( 'a', 2, 2, 0, 0, 4 ),
			new SyntheticGlyph( ' ', 0, 0, 0, 0, 2 ) ) );
		// 'x' is missing and draws as the space fallback, but its source character is not a break, so it stays in the word
		var layout = TextLayout.Create( atlas, "axa", 9, fallback: ' ' );
		Assert.AreEqual( 2, layout.LineCount );
		CollectionAssert.AreEqual( new[] { 6, 4 }, layout.LineWidths.ToArray() );
		Assert.AreEqual( 6, layout.Width );
		Assert.AreEqual( "a0,0 a0,10", Describe( layout ) );
		CollectionAssert.AreEqual( new[] { 'x' }, layout.MissingCharacters.ToArray() );
	}

	[TestMethod]
	public void BreakCharacterWithEnoughRoomDoesNotWrapAndMissingFallbackStillRenders()
	{
		var present = new FontAtlas( CreateFont( 10,
			new SyntheticGlyph( 'a', 2, 2, 0, 0, 4 ),
			new SyntheticGlyph( ' ', 0, 0, 0, 0, 4 ) ) );
		var fitting = TextLayout.Create( present, "a a", 12 );
		Assert.AreEqual( 1, fitting.LineCount );
		CollectionAssert.AreEqual( new[] { 12 }, fitting.LineWidths.ToArray() );
		Assert.AreEqual( "a0,0 a8,0", Describe( fitting ) );
		Assert.AreEqual( 0, fitting.MissingCharacters.Count );

		// the same text with the space missing: the line still fits, so the fallback is drawn rather than dropped
		var missing = new FontAtlas( CreateFont( 10,
			new SyntheticGlyph( 'a', 2, 2, 0, 0, 4 ),
			new SyntheticGlyph( '?', 2, 2, 0, 0, 4 ) ) );
		var rendered = TextLayout.Create( missing, "a a", 12 );
		Assert.AreEqual( 1, rendered.LineCount );
		CollectionAssert.AreEqual( new[] { 12 }, rendered.LineWidths.ToArray() );
		Assert.AreEqual( "a0,0 ?4,0 a8,0", Describe( rendered ) );
		CollectionAssert.AreEqual( new[] { ' ' }, rendered.MissingCharacters.ToArray() );
		var unwrapped = TextLayout.Create( missing, "a a" );
		Assert.AreEqual( 1, unwrapped.LineCount );
		Assert.AreEqual( 12, unwrapped.Width );
		Assert.AreEqual( "a0,0 ?4,0 a8,0", Describe( unwrapped ) );
	}

	[TestMethod]
	public void CentredWrappedTextPlacesLinesFromTheAdvanceBlockWidth()
	{
		var atlas = new FontAtlas( CreateFont( 10,
			new SyntheticGlyph( 'a', 2, 2, 0, 0, 4 ),
			new SyntheticGlyph( '?', 2, 2, 0, 0, 4 ) ) );
		var context = new UiContext( null!, null!, null! );
		context.DrawText( atlas, "a a", new UiRect( 0, 0, 9, 40 ), new RgbaByte( 255, 255, 255, 255 ),
			UiAlign.Center, wrap: true, textScale: 1 );

		var glyphs = context.Batch.Glyphs;
		Assert.AreEqual( 2, glyphs.Count );
		Assert.AreEqual( (2, 10, 2, 2), (glyphs[0].X, glyphs[0].Y, glyphs[0].Width, glyphs[0].Height) );
		Assert.AreEqual( (2, 20, 2, 2), (glyphs[1].X, glyphs[1].Y, glyphs[1].Width, glyphs[1].Height) );

		var text = context.Batch.TextDraws.Single();
		Assert.AreEqual( "a a", text.Text );
		Assert.AreEqual( (2f, 10f, 4f, 20f), (text.Rect.X, text.Rect.Y, text.Rect.Width, text.Rect.Height) );
		Assert.AreEqual( (0, 2), (text.FirstGlyph, text.GlyphCount) );
	}

	[TestMethod]
	public void PlainTextDrawsOnceAndATitleDrawsOneComposedBitmapWithTheCopyTwoPixelsLeftAndBelow()
	{
		var atlas = new FontAtlas( CreateFont( 10, new SyntheticGlyph( 'a', 2, 2, 0, 0, 4 ), new SyntheticGlyph( '?', 2, 2, 0, 0, 4 ) ) );
		var rect = new UiRect( 0, 0, 40, 20 );
		var yellow = new RgbaByte( 234, 239, 102, 255 );
		// 0x48f830: plain text is one pass, whatever the default
		var plain = new UiContext( null!, null!, null! );
		plain.DrawText( atlas, "a", rect, yellow, UiAlign.Center, textScale: 1 );
		Assert.AreEqual( 1, plain.Batch.Glyphs.Count );
		Assert.AreEqual( yellow, plain.Batch.Glyphs[0].Color );
		Assert.IsNull( plain.Batch.Draws.Single().Texture.Title );
		// 0x491ab0: one quad of the composed bitmap (copy, then string), modulated by the control colour; the string's glyph stays recorded where it is drawn
		var title = new UiContext( null!, null!, null! );
		title.DrawText( atlas, "a", rect, yellow, UiAlign.Center, false, UiBatch.TitleCopyAlpha, textScale: 1 );
		var glyph = title.Batch.Glyphs.Single();
		Assert.AreEqual( (plain.Batch.Glyphs[0].X, plain.Batch.Glyphs[0].Y), (glyph.X, glyph.Y), "the copy does not move the string" );
		Assert.AreEqual( yellow, glyph.Color );
		Assert.AreEqual( 1, title.Batch.TextDraws.Count );
		var draw = title.Batch.Draws.Single();
		Assert.IsNotNull( draw.Texture.Title );
		Assert.IsTrue( draw.Vertices.All( vertex => vertex.Color == yellow ) );
		var bitmap = draw.Texture.Title!;
		Assert.AreEqual( (glyph.X - 2, glyph.Y), (draw.Vertices.Min( vertex => vertex.Position.X ), draw.Vertices.Min( vertex => vertex.Position.Y )), "the bitmap reaches two pixels left of the string and starts at its top" );
		Assert.AreEqual( (4, 4), (bitmap.TextWidth, bitmap.TextHeight), "the 2 x 2 glyph plus the copy's two pixels in each direction" );
	}

	[TestMethod]
	public void ATitleIsPlacedByItsOwnBoxNotByTheCopy()
	{
		var atlas = new FontAtlas( CreateFont( 10, new SyntheticGlyph( 'a', 2, 2, 0, 0, 4 ), new SyntheticGlyph( '?', 2, 2, 0, 0, 4 ) ) );
		var rect = new UiRect( 0, 0, 100, 40 );
		var white = new RgbaByte( 255, 255, 255, 255 );
		var plain = new UiContext( null!, null!, null! );
		plain.DrawTextAt( atlas, 2, "aa", rect, white, UiAlign.Center );
		var title = new UiContext( null!, null!, null! );
		title.DrawTextAt( atlas, 2, "aa", rect, white, UiAlign.Center, UiBatch.TitleCopyAlpha );
		CollectionAssert.AreEqual( plain.Batch.Glyphs.Select( glyph => (glyph.X, glyph.Y) ).ToList(), title.Batch.Glyphs.Select( glyph => (glyph.X, glyph.Y) ).ToList() );
	}

	[TestMethod]
	public void TheComposedTitleBitmapWritesTheStringOverTheCopyAndDropsLevelZero()
	{
		// coverage 15 and 1 in a 2 x 1 glyph: the copy has levels 7 and 0 (c x 128 / 255), the string 15 and 1
		var atlas = new FontAtlas( CreateFont( 10, new SyntheticGlyph( 'a', 2, 1, 0, 0, 4, [15, 1] ), new SyntheticGlyph( '?', 2, 1, 0, 0, 4, [15, 1] ) ) );
		var layout = TextLayout.Create( atlas, "a" );
		(byte R, byte G, byte B, byte A) Texel( UiTitleBitmap bitmap, int x, int y ) { var at = (y * bitmap.Width + x) * 4; return (bitmap.Rgba[at], bitmap.Rgba[at + 1], bitmap.Rgba[at + 2], bitmap.Rgba[at + 3]); }

		var bitmap = UiTitleBitmap.Compose( atlas, layout, 0x80, 1 );
		Assert.AreEqual( (4, 3), (bitmap.Width, bitmap.Height), "the 2 x 1 glyph plus two pixels left and two below (the string is at column 2, row 0; the copy at column 0, row 2)" );
		Assert.AreEqual( ((byte)255, (byte)255, (byte)255, (byte)255), Texel( bitmap, 2, 0 ), "string, coverage 15: white, alpha 255" );
		Assert.AreEqual( ((byte)255, (byte)255, (byte)255, (byte)17), Texel( bitmap, 3, 0 ), "string, coverage 1: alpha 17" );
		Assert.AreEqual( ((byte)128, (byte)128, (byte)128, (byte)(7 * 17)), Texel( bitmap, 0, 2 ), "copy, coverage 15: grey, alpha 17 x floor(15 x 128 / 255) = 119" );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)0, (byte)0), Texel( bitmap, 1, 2 ), "copy, coverage 1: level 0, nothing written" );

		// Theme Park Inc's copy has the colour alpha 0xc8: 15 x 200 / 255 = 11 levels
		var tpi = UiTitleBitmap.Compose( atlas, layout, 0xC8, 1 );
		Assert.AreEqual( ((byte)128, (byte)128, (byte)128, (byte)(11 * 17)), Texel( tpi, 0, 2 ) );
		Assert.AreEqual( ((byte)0, (byte)0, (byte)0, (byte)0), Texel( tpi, 1, 2 ), "coverage 1 x 200 / 255 is level 0 as well" );
	}

	[TestMethod]
	public void WhereTheCopyAndTheStringShareATexelTheStringReplacesIt()
	{
		// a 3 x 3 glyph: the bitmap's texel (2, 2) is the string's glyph pixel (0, 2) and the copy's glyph pixel (2, 0)
		(byte R, byte G, byte B, byte A) Shared( byte stringCoverage, byte copyCoverage )
		{
			var coverage = new byte[9];
			coverage[2 * 3 + 0] = stringCoverage;
			coverage[0 * 3 + 2] = copyCoverage;
			var atlas = new FontAtlas( CreateFont( 10, new SyntheticGlyph( 'a', 3, 3, 0, 0, 4, coverage ), new SyntheticGlyph( '?', 3, 3, 0, 0, 4, coverage ) ) );
			var bitmap = UiTitleBitmap.Compose( atlas, TextLayout.Create( atlas, "a" ), 0x80, 1 );
			Assert.AreEqual( (5, 5), (bitmap.Width, bitmap.Height) );
			var at = (2 * bitmap.Width + 2) * 4;
			return (bitmap.Rgba[at], bitmap.Rgba[at + 1], bitmap.Rgba[at + 2], bitmap.Rgba[at + 3]);
		}
		Assert.AreEqual( ((byte)255, (byte)255, (byte)255, (byte)255), Shared( 15, 15 ), "coverage 15 on both: the string's white, alpha 255, not a blend" );
		Assert.AreEqual( ((byte)255, (byte)255, (byte)255, (byte)17 ), Shared( 1, 15 ), "coverage 1 string over a coverage 15 copy: the string's alpha 17, replacing the copy's 119" );
		Assert.AreEqual( ((byte)128, (byte)128, (byte)128, (byte)119), Shared( 0, 15 ), "no string texel: the copy's grey, alpha 119" );
	}

	[TestMethod]
	public void TheTitleBitmapCacheReturnsOneInstancePerLayoutAndDropsTheOldest()
	{
		var atlas = new FontAtlas( CreateFont( 10, new SyntheticGlyph( 'a', 2, 2, 0, 0, 4 ), new SyntheticGlyph( '?', 2, 2, 0, 0, 4 ) ) );
		var first = UiTitleBitmap.Get( atlas, TextLayout.Create( atlas, "a" ), 0x80, 1 )!;
		Assert.AreSame( first, UiTitleBitmap.Get( atlas, TextLayout.Create( atlas, "a" ), 0x80, 1 ) );
		Assert.AreNotSame( first, UiTitleBitmap.Get( atlas, TextLayout.Create( atlas, "a" ), 0xC8, 1 ), "the copy alpha is part of the key" );
		Assert.AreSame( first.Texture.Title, first );
		Assert.IsFalse( first.Evicted );
		for ( var length = 2; length <= UiTitleBitmap.Capacity + 1; length++ )
			UiTitleBitmap.Get( atlas, TextLayout.Create( atlas, new string( 'a', length ) ), 0x80, 1 );
		Assert.IsTrue( first.Evicted, "past the capacity the least recently used bitmap is dropped" );
		Assert.AreNotSame( first, UiTitleBitmap.Get( atlas, TextLayout.Create( atlas, "a" ), 0x80, 1 ), "a dropped layout is composed again" );
	}

	[TestMethod]
	public void TheTitleBitmapCacheIsAlsoBoundedByItsPixelBytes()
	{
		var atlas = new FontAtlas( CreateFont( 10, new SyntheticGlyph( 'a', 100, 100, 0, 0, 100 ), new SyntheticGlyph( '?', 100, 100, 0, 0, 100 ) ) );
		var first = UiTitleBitmap.Get( atlas, TextLayout.Create( atlas, "a" ), 0x80, 1 )!;
		UiTitleBitmap last = first;
		for ( var length = 2; length <= 60; length++ )
			last = UiTitleBitmap.Get( atlas, TextLayout.Create( atlas, new string( 'a', length ) ), 0x80, 1 )!;
		Assert.IsTrue( first.Evicted, "about 76 MB of bitmaps were composed, past the 16 MB budget" );
		Assert.IsFalse( last.Evicted );
	}

	[TestMethod]
	public void ACappedFactorFillsTheEmptyTexelsWithTheNearestColour()
	{
		var atlas = new FontAtlas( CreateFont( 10, new SyntheticGlyph( 'a', 2, 1, 0, 0, 4, [15, 0] ), new SyntheticGlyph( '?', 2, 1, 0, 0, 4, [15, 0] ) ) );
		var bitmap = UiTitleBitmap.Compose( atlas, TextLayout.Create( atlas, "a" ), 0x80, 1, fillEmptyColor: true );
		// the copy's texel (0, 2) is grey; the empty texel (1, 2) beside it takes its colour and stays invisible; the empty texel (3, 0) beside the string takes white
		Assert.AreEqual( ((byte)128, (byte)0), (bitmap.Rgba[(2 * bitmap.Width + 1) * 4], bitmap.Rgba[(2 * bitmap.Width + 1) * 4 + 3]) );
		Assert.AreEqual( ((byte)255, (byte)0), (bitmap.Rgba[3 * 4], bitmap.Rgba[3 * 4 + 3]) );
	}

	[TestMethod]
	public void ATitleIsCutOffAtItsControlBoxAndPlainTextIsNot()
	{
		var atlas = new FontAtlas( CreateFont( 10, new SyntheticGlyph( 'a', 4, 4, 0, 0, 4 ), new SyntheticGlyph( '?', 4, 4, 0, 0, 4 ) ) );
		var white = new RgbaByte( 255, 255, 255, 255 );
		var rect = new UiRect( 10, 10, 6, 6 );
		var title = new UiContext( null!, null!, null! );
		title.DrawText( atlas, "aaaa", rect, white, UiAlign.Left, false, UiBatch.TitleCopyAlpha, textScale: 1 );
		var vertices = title.Batch.Draws.SelectMany( draw => draw.Vertices ).ToList();
		Assert.IsTrue( vertices.Count > 0 );
		Assert.IsTrue( vertices.All( vertex => vertex.Position.X >= rect.X && vertex.Position.X <= rect.Right && vertex.Position.Y >= rect.Y && vertex.Position.Y <= rect.Bottom ), "the drawn quad stays inside the box (0x48f3d0)" );
		var plain = new UiContext( null!, null!, null! );
		plain.DrawText( atlas, "aaaa", rect, white, UiAlign.Left, false, 0, textScale: 1 );
		Assert.IsTrue( plain.Batch.Draws.SelectMany( draw => draw.Vertices ).Any( vertex => vertex.Position.X > rect.Right ), "plain text keeps drawing past its box (UI-007)" );
	}

	[TestMethod]
	public void EveryOriginalEnglishFontPacksAllGlyphsWithoutOverlap()
	{
		var directory = OriginalFontDirectory();
		var patched = OriginalEdition.IsPatch2( OriginalDataDirectory() );
		OriginalEdition.AssertPatch2Fonts( directory, patched );
		var files = OriginalEdition.BaseFonts( directory );
		Assert.AreEqual( 33, files.Length );
		if ( patched )
			files = files.Concat( OriginalEdition.Patch2Fonts.Keys.Select( name => GameLanguage.FindEntry( directory, name, false )! ) ).ToArray();
		foreach ( var file in files )
		{
			using var stream = File.OpenRead( file );
			var font = new FontFile( stream );
			var atlas = new FontAtlas( font );
			Assert.AreEqual( font.Glyphs.Select( glyph => glyph.Character ).Distinct().Count(), atlas.Glyphs.Count, file );
			AssertPacking( atlas, font );
		}
	}

	[DataTestMethod]
	[DataRow( "GAME8AA.bf4", 128, 128, 11 )]
	[DataRow( "SESHMED.bf4", 256, 256, 31 )]
	public void OriginalFontAtlasIsPinned( string name, int width, int height, int lineHeight )
	{
		var atlas = new FontAtlas( new FontFile( new MemoryStream( File.ReadAllBytes( Path.Combine( OriginalFontDirectory(), name ) ) ) ) );
		Assert.AreEqual( (width, height, lineHeight), (atlas.Width, atlas.Height, atlas.LineHeight) );
		Assert.AreEqual( 192, atlas.Glyphs.Count, "191 non-space characters plus one space; duplicate space slots are not packed." );
		Assert.AreEqual( PinnedAtlasHashes[name], Convert.ToHexString( SHA256.HashData( atlas.Alpha ) ) );
	}

	private static readonly Dictionary<string, string> PinnedAtlasHashes = new()
	{
		["GAME8AA.bf4"] = "9D3EE00B92C8D64863AE04E874D3D3D22C76AE464D57FBC06F53B8DF186DA336",
		["SESHMED.bf4"] = "14C2048CC0FBEF99D5BEF89F8EDA0291450E9A88EFA22E73A9592EDB9BC247FE",
	};

	[TestMethod]
	public void OriginalGameStringsLayOutWithPinnedMetrics()
	{
		var dataPath = OriginalDataDirectory();
		_ = OriginalFontDirectory();
		var originalFileSystem = FileSystem;
		try
		{
			FileSystem = new BaseFileSystem( dataPath );
			var uiText = new StringFile( "Language/English/UITEXT.str" );
			var objectNames = new StringFile( "Language/English/OBJECT_NAMES.str" );
			var game8 = new FontAtlas( new FontFile( "Language/English/GAME8AA.bf4" ) );
			var sesh = new FontAtlas( new FontFile( "Language/English/SESHMED.bf4" ) );

			Assert.AreEqual( "Excitement", uiText[(int)UIStrings.Excitement] );
			var excitement = TextLayout.Create( game8, uiText[(int)UIStrings.Excitement] );
			Assert.AreEqual( "E0,0 x7,2 c13,2 i19,0 t22,1 e27,2 m33,2 e41,2 n47,2 t53,1", Describe( excitement ) );
			Assert.AreEqual( (58, 11, 1), (excitement.Width, excitement.Height, excitement.LineCount) );

			Assert.AreEqual( "Totem", objectNames[29] );
			var totem = TextLayout.Create( sesh, objectNames[29] );
			Assert.AreEqual( "T0,7 o12,12 t22,7 e30,15 m40,14", Describe( totem ) );
			Assert.AreEqual( (53, 31), (totem.Width, totem.Height) );

			var quit = TextLayout.Create( game8, uiText[(int)UIStrings.ConfirmQuit], 120 );
			Assert.AreEqual( "QUIT GAME\n\nAre you sure you want to quit the game ?", uiText[(int)UIStrings.ConfirmQuit] );
			Assert.AreEqual( (4, 113, 44), (quit.LineCount, quit.Width, quit.Height) );
			Assert.AreEqual( "QUITGAME||Areyousureyouwant|toquitthegame?", InkPerLine( quit, game8.LineHeight ) );
			Assert.AreEqual( "CD2F34C147FADEC87013F5FA6DE5809A61E73CC74F915BB41001FC8F58DB037B", Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( Describe( quit ) ) ) ) );

			foreach ( var entry in uiText.Entries )
				Assert.AreEqual( 0, TextLayout.Create( game8, entry ).MissingCharacters.Count, entry );
		}
		finally
		{
			FileSystem = originalFileSystem;
		}
	}

	internal static string Describe( TextLayoutResult layout ) => string.Join( " ", layout.Glyphs.Select( glyph => $"{glyph.Character}{glyph.X},{glyph.Y}" ) );

	private static string InkPerLine( TextLayoutResult layout, int lineHeight ) =>
		string.Join( "|", Enumerable.Range( 0, layout.LineCount ).Select( line => string.Concat( layout.Glyphs.Where( glyph => (glyph.Y - glyph.Glyph.OffsetY) / lineHeight == line ).Select( glyph => glyph.Character ) ) ) );

	private static byte[] ReadRegion( FontAtlas atlas, FontAtlasGlyph glyph )
	{
		var region = new byte[glyph.Width * glyph.Height];
		for ( var row = 0; row < glyph.Height; row++ )
			Array.Copy( atlas.Alpha, (glyph.Y + row) * atlas.Width + glyph.X, region, row * glyph.Width, glyph.Width );
		return region;
	}

	private static void AssertPacking( FontAtlas atlas, FontFile font )
	{
		Assert.IsTrue( atlas.Width >= FontAtlas.MinimumSize && (atlas.Width & (atlas.Width - 1)) == 0 );
		Assert.IsTrue( atlas.Height > 0 && (atlas.Height & (atlas.Height - 1)) == 0 && atlas.Height <= atlas.Width );
		var owner = new int[atlas.Alpha.Length];
		var packed = atlas.Glyphs.Values.Where( glyph => glyph.Width > 0 && glyph.Height > 0 ).ToArray();
		for ( var item = 0; item < packed.Length; item++ )
		{
			var glyph = packed[item];
			var source = font.Glyphs[glyph.SourceIndex];
			Assert.AreEqual( (source.Character, source.Width, source.Height, (int)source.OffsetX, (int)source.OffsetY, (int)source.Advance),
				(glyph.Character, glyph.Width, glyph.Height, glyph.OffsetX, glyph.OffsetY, glyph.Advance) );
			Assert.IsTrue( glyph.X >= FontAtlas.Padding && glyph.Y >= FontAtlas.Padding && glyph.X + glyph.Width + FontAtlas.Padding <= atlas.Width && glyph.Y + glyph.Height + FontAtlas.Padding <= atlas.Height );
			CollectionAssert.AreEqual( source.Coverage.Select( value => (byte)(value * 17) ).ToArray(), ReadRegion( atlas, glyph ) );
			for ( var row = -FontAtlas.Padding; row < glyph.Height + FontAtlas.Padding; row++ )
			{
				for ( var column = -FontAtlas.Padding; column < glyph.Width + FontAtlas.Padding; column++ )
				{
					var pixel = (glyph.Y + row) * atlas.Width + glyph.X + column;
					var inside = row >= 0 && column >= 0 && row < glyph.Height && column < glyph.Width;
					if ( inside )
					{
						Assert.AreEqual( 0, owner[pixel], $"Glyph {glyph.Character} overlaps another glyph." );
						owner[pixel] = item + 1;
					}
					else
						Assert.IsTrue( owner[pixel] == 0 || owner[pixel] == -1, $"Glyph {glyph.Character} padding overlaps another glyph." );
				}
			}
			for ( var row = -FontAtlas.Padding; row < glyph.Height + FontAtlas.Padding; row++ )
				for ( var column = -FontAtlas.Padding; column < glyph.Width + FontAtlas.Padding; column++ )
				{
					var pixel = (glyph.Y + row) * atlas.Width + glyph.X + column;
					if ( owner[pixel] == 0 )
						owner[pixel] = -1;
				}
		}
		for ( var pixel = 0; pixel < atlas.Alpha.Length; pixel++ )
			Assert.IsTrue( owner[pixel] > 0 || atlas.Alpha[pixel] == 0, "Alpha outside a glyph rectangle must be transparent." );
	}

	private static string OriginalDataDirectory()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the selected original English BF4 corpus." );
		return Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
	}

	private static string OriginalFontDirectory()
	{
		var directory = Path.Combine( OriginalDataDirectory(), "Language", "English" );
		if ( !Directory.Exists( directory ) )
			Assert.Inconclusive( "The selected English BF4 corpus is missing." );
		return directory;
	}
}
