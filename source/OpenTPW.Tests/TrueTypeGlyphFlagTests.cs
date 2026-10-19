using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The simple-glyph flag repeat bound of <see cref="TrueTypeFont"/>: a flag byte carrying the repeat
/// bit (0x08) is followed by a repeat count that must not exceed the glyph's remaining point slots
/// (<c>pointCount - i</c> after the first stored entry). Oversized counts are rejected with
/// <see cref="InvalidDataException"/> before expansion; the old filler loop silently clipped them to
/// the point count and accepted the malformed stream. Rejection is outline-scoped — glyph-slice
/// header bounds are covered by TrueTypeGlyphBoundsTests and are not re-tested here. Fixture glyphs
/// rewrite the final square body in place (glyph-relative: one end point word at +10, instruction
/// length at +12, flags from +14) and shorten its loca slice to the exact fixture length; the
/// untouched 'A' square and the empty .notdef glyph are asserted alongside, and table, glyph and
/// body positions are always resolved from the file, never hardcoded.
/// </summary>
[TestClass]
public class TrueTypeGlyphFlagTests
{
	[TestMethod]
	public void ZeroAdditionalRepeatKeepsOnlyTheFirstFlagEntryAndParsesBothPoints()
	{
		var (data, a, b) = BaseFont();
		var reference = TrueTypeFont.Parse( data );
		var metricsA = reference.GetMetrics( a );
		var outlineA = Flatten( reference.GetOutline( a ) );

		// Two points from flags 09 00 01: a repeat flag whose count is zero, then a plain on-curve
		// flag; each axis reads signed 16-bit deltas of 0 and 500.
		var font = TrueTypeFont.Parse( WithSimpleGlyph( data, b, contourEnd: 1, flags: new byte[] { 0x09, 0x00, 0x01 },
			xs: new byte[] { 0x00, 0x00, 0x01, 0xF4 }, ys: new byte[] { 0x00, 0x00, 0x01, 0xF4 }, sliceLength: 25 ) );
		CollectionAssert.AreEqual( new[] { (0f, 0f, true), (500f, 500f, true) }, Flatten( font.GetOutline( b ) ) );
		AssertNeighboursUnchanged( font, a, metricsA, outlineA );
	}

	[DataTestMethod]
	[DataRow( (byte)0x3F, DisplayName = "0x3F: positive X and Y, on curve" )]
	[DataRow( (byte)0x0F, DisplayName = "0x0F: negative X and Y, on curve" )]
	[DataRow( (byte)0x3E, DisplayName = "0x3E: positive X and Y, off curve" )]
	[DataRow( (byte)0x0E, DisplayName = "0x0E: negative X and Y, off curve" )]
	public void ExactFitRepeatCountFillsAllFivePoints( byte flag )
	{
		var (data, a, b) = BaseFont();
		var reference = TrueTypeFont.Parse( data );
		var metricsA = reference.GetMetrics( a );
		var outlineA = Flatten( reference.GetOutline( a ) );

		// Five points from one flag byte: the first entry plus a repeat count of exactly the four
		// remaining slots, then a five-byte short coordinate stream per axis (bytes 00 0A 14 1E 28,
		// i.e. deltas 0, 10, 20, 30, 40; cumulative magnitudes 0, 10, 30, 60, 100).
		var font = TrueTypeFont.Parse( WithSimpleGlyph( data, b, contourEnd: 4, flags: new byte[] { flag, 0x04 },
			xs: new byte[] { 0x00, 0x0A, 0x14, 0x1E, 0x28 }, ys: new byte[] { 0x00, 0x0A, 0x14, 0x1E, 0x28 }, sliceLength: 26 ) );
		CollectionAssert.AreEqual( ExpectedPoints( flag ), Flatten( font.GetOutline( b ) ) );
		AssertNeighboursUnchanged( font, a, metricsA, outlineA );
	}

	[TestMethod]
	public void RepeatCountOneBeyondTheRemainingSlotsIsRejected()
	{
		var (data, a, b) = BaseFont();
		var reference = TrueTypeFont.Parse( data );
		var metricsA = reference.GetMetrics( a );
		var outlineA = Flatten( reference.GetOutline( a ) );

		// The identical five-point payload with the repeat count raised from four to five: the old
		// loop clipped it back to four and silently accepted; the guard rejects before expansion.
		var font = TrueTypeFont.Parse( WithSimpleGlyph( data, b, contourEnd: 4, flags: new byte[] { 0x3F, 0x05 },
			xs: new byte[] { 0x00, 0x0A, 0x14, 0x1E, 0x28 }, ys: new byte[] { 0x00, 0x0A, 0x14, 0x1E, 0x28 }, sliceLength: 26 ) );
		Assert.ThrowsException<InvalidDataException>( () => font.GetOutline( b ) );
		AssertNeighboursUnchanged( font, a, metricsA, outlineA );
	}

	[TestMethod]
	public void Count255ExceedsTheFourRemainingSlotsBy251AndIsRejected()
	{
		var (data, a, b) = BaseFont();
		var reference = TrueTypeFont.Parse( data );
		var metricsA = reference.GetMetrics( a );
		var outlineA = Flatten( reference.GetOutline( a ) );

		// After the first stored entry only four slots remain, so 255 exceeds the capacity by 251,
		// not by 255; the guard rejects it before expansion regardless of the coordinate payload.
		var font = TrueTypeFont.Parse( WithSimpleGlyph( data, b, contourEnd: 4, flags: new byte[] { 0x3F, 0xFF },
			xs: new byte[] { 0x00, 0x0A, 0x14, 0x1E, 0x28 }, ys: new byte[] { 0x00, 0x0A, 0x14, 0x1E, 0x28 }, sliceLength: 26 ) );
		Assert.ThrowsException<InvalidDataException>( () => font.GetOutline( b ) );
		AssertNeighboursUnchanged( font, a, metricsA, outlineA );
	}

	[TestMethod]
	public void SliceEndingAfterTheRepeatFlagByteWithoutItsCountIsRejectedByExistingBounds()
	{
		var (data, a, b) = BaseFont();
		var reference = TrueTypeFont.Parse( data );
		var metricsA = reference.GetMetrics( a );
		var outlineA = Flatten( reference.GetOutline( a ) );

		// The 15-byte slice ends right after the repeat flag byte 09: the existing glyph slice bound
		// rejects reading the repeat count, so the guard needs no truncated read of its own.
		var font = TrueTypeFont.Parse( WithSimpleGlyph( data, b, contourEnd: 1, flags: new byte[] { 0x09 },
			xs: Array.Empty<byte>(), ys: Array.Empty<byte>(), sliceLength: 15 ) );
		Assert.ThrowsException<InvalidDataException>( () => font.GetOutline( b ) );
		AssertNeighboursUnchanged( font, a, metricsA, outlineA );
	}

	[TestMethod]
	public void ExactFitFlagsWithoutCoordinateRoomAreRejectedByExistingBounds()
	{
		var (data, a, b) = BaseFont();
		var reference = TrueTypeFont.Parse( data );
		var metricsA = reference.GetMetrics( a );
		var outlineA = Flatten( reference.GetOutline( a ) );

		// The exact-fit repeat of four is legal (four slots remain); the 16-byte slice ends before
		// the coordinates, so the existing coordinate bound rejects, not the repeat guard.
		var font = TrueTypeFont.Parse( WithSimpleGlyph( data, b, contourEnd: 4, flags: new byte[] { 0x3F, 0x04 },
			xs: Array.Empty<byte>(), ys: Array.Empty<byte>(), sliceLength: 16 ) );
		Assert.ThrowsException<InvalidDataException>( () => font.GetOutline( b ) );
		AssertNeighboursUnchanged( font, a, metricsA, outlineA );
	}

	[TestMethod]
	public void SliceEndingBetweenTheXAndYStreamsIsRejectedByExistingBounds()
	{
		var (data, a, b) = BaseFont();
		var reference = TrueTypeFont.Parse( data );
		var metricsA = reference.GetMetrics( a );
		var outlineA = Flatten( reference.GetOutline( a ) );

		// The five short X bytes complete inside the 21-byte slice; the first Y read hits the slice end.
		var font = TrueTypeFont.Parse( WithSimpleGlyph( data, b, contourEnd: 4, flags: new byte[] { 0x3F, 0x04 },
			xs: new byte[] { 0x00, 0x0A, 0x14, 0x1E, 0x28 }, ys: Array.Empty<byte>(), sliceLength: 21 ) );
		Assert.ThrowsException<InvalidDataException>( () => font.GetOutline( b ) );
		AssertNeighboursUnchanged( font, a, metricsA, outlineA );
	}

	/// <summary>
	/// The hand-derived points for the five-byte short stream 00 0A 14 1E 28 (deltas 0, 10, 20, 30,
	/// 40; cumulative magnitudes 0, 10, 30, 60, 100 on each axis) under each repeated flag byte.
	/// </summary>
	private static (float X, float Y, bool OnCurve)[] ExpectedPoints( byte flag ) => flag switch
	{
		0x3F => new[] { (0f, 0f, true), (10f, 10f, true), (30f, 30f, true), (60f, 60f, true), (100f, 100f, true) },
		0x0F => new[] { (0f, 0f, true), (-10f, -10f, true), (-30f, -30f, true), (-60f, -60f, true), (-100f, -100f, true) },
		0x3E => new[] { (0f, 0f, false), (10f, 10f, false), (30f, 30f, false), (60f, 60f, false), (100f, 100f, false) },
		0x0E => new[] { (0f, 0f, false), (-10f, -10f, false), (-30f, -30f, false), (-60f, -60f, false), (-100f, -100f, false) },
		_ => throw new ArgumentException( $"Fixture flags are limited to 0x3F, 0x0F, 0x3E and 0x0E, not 0x{flag:X2}." )
	};

	/// <summary>
	/// Builds a font of three glyphs with <see cref="TrueTypeFontTests.FontBuilder"/>: an empty
	/// .notdef glyph, an untouched 'A' square and a final 'B' square whose 34-byte body the fixtures
	/// rewrite, plus 64 trailing file bytes so a shortened slice never sits at the end of the file.
	/// </summary>
	private static (byte[] Data, int GlyphA, int GlyphB) BaseFont()
	{
		var builder = new TrueTypeFontTests.FontBuilder();
		var a = builder.AddSimple( 600, 'A', TrueTypeFontTests.Square( 0, 0, 500, 500 ) );
		var b = builder.AddSimple( 700, 'B', TrueTypeFontTests.Square( 100, -100, 300, 700 ) );
		var data = builder.Build().Concat( new byte[64] ).ToArray();
		return (data, a, b);
	}

	/// <summary>The untouched 'A' square and the empty .notdef glyph keep their existing behavior.</summary>
	private static void AssertNeighboursUnchanged( TrueTypeFont font, int glyphA, TrueTypeGlyphMetrics metricsA, (float X, float Y, bool OnCurve)[] outlineA )
	{
		Assert.AreEqual( metricsA, font.GetMetrics( glyphA ) );
		CollectionAssert.AreEqual( outlineA, Flatten( font.GetOutline( glyphA ) ) );
		Assert.AreEqual( new TrueTypeGlyphMetrics( 500, 0, 0, 0, 0, 0, 0 ), font.GetMetrics( 0 ) );
		Assert.AreEqual( 0, font.GetOutline( 0 ).Count );
	}

	private static (float X, float Y, bool OnCurve)[] Flatten( IReadOnlyList<IReadOnlyList<TrueTypePoint>> outline ) =>
		outline.SelectMany( contour => contour ).Select( point => (point.X, point.Y, point.OnCurve) ).ToArray();

	/// <summary>Reads a table record from the font's directory (tags are four ASCII bytes, offsets and lengths are big-endian).</summary>
	private static (int Offset, int Length) FindTable( byte[] data, string tag )
	{
		var count = BinaryPrimitives.ReadUInt16BigEndian( data.AsSpan( 4 ) );
		for ( var i = 0; i < count; i++ )
		{
			var record = 12 + i * 16;
			if ( Encoding.ASCII.GetString( data, record, 4 ) == tag )
				return ( (int)BinaryPrimitives.ReadUInt32BigEndian( data.AsSpan( record + 8 ) ), (int)BinaryPrimitives.ReadUInt32BigEndian( data.AsSpan( record + 12 ) ) );
		}
		throw new InvalidOperationException( $"The synthetic font has no {tag} table." );
	}

	/// <summary>File offset of a glyph's body: the glyf table's offset plus the glyph's byte offset from loca.</summary>
	private static int GlyphBodyStart( byte[] data, int glyph )
	{
		var (glyf, _) = FindTable( data, "glyf" );
		var (loca, _) = FindTable( data, "loca" );
		return glyf + (int)BinaryPrimitives.ReadUInt32BigEndian( data.AsSpan( loca + glyph * 4 ) );
	}

	/// <summary>Rewrites the long loca so <paramref name="glyph"/>'s slice holds exactly <paramref name="length"/> bytes.</summary>
	private static byte[] WithGlyphLength( byte[] data, int glyph, int length )
	{
		var (loca, _) = FindTable( data, "loca" );
		var start = BinaryPrimitives.ReadUInt32BigEndian( data.AsSpan( loca + glyph * 4 ) );
		BinaryPrimitives.WriteUInt32BigEndian( data.AsSpan( loca + (glyph + 1) * 4 ), start + (uint)length );
		return data;
	}

	/// <summary>
	/// Rewrites the font's final glyph (a one-contour simple square) in place: one end point word at
	/// +10 (<paramref name="contourEnd"/>), instruction length 0 at +12, then the flags, X and Y
	/// streams from +14, and a loca slice of exactly <paramref name="sliceLength"/> bytes; the file
	/// keeps the unused trailing bytes, and the header bytes the fixtures do not need stay untouched.
	/// </summary>
	private static byte[] WithSimpleGlyph( byte[] data, int glyph, int contourEnd, byte[] flags, byte[] xs, byte[] ys, int sliceLength )
	{
		var start = GlyphBodyStart( data, glyph );
		WriteU16( data, start + 10, contourEnd );
		WriteU16( data, start + 12, 0 );
		var position = start + 14;
		foreach ( var value in flags )
			data[position++] = value;
		foreach ( var value in xs )
			data[position++] = value;
		foreach ( var value in ys )
			data[position++] = value;
		Assert.IsTrue( position - start <= 34, "a fixture must fit the built square glyph's 34 bytes" );
		Assert.IsTrue( sliceLength <= position - start, "a slice must not outgrow the written payload" );
		return WithGlyphLength( data, glyph, sliceLength );
	}

	private static void WriteU16( byte[] data, int offset, int value )
	{
		BinaryPrimitives.WriteUInt16BigEndian( data.AsSpan( offset ), (ushort)value );
	}
}
