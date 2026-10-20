using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Glyph-local header bounds of <see cref="TrueTypeFont"/>: <see cref="TrueTypeFont.GetMetrics"/> and
/// <see cref="TrueTypeFont.GetOutline"/> must reject a glyph whose loca-derived slice holds fewer than
/// the 10 bytes of the glyf header, even when the file itself holds ample bytes after the glyph. Both
/// loca layouts are exercised; short loca stores offset/2, so slice lengths are always even and the
/// odd lengths 1, 3, 5, 7, 9 are only expressible (and only asserted) with long loca.
/// </summary>
[TestClass]
public class TrueTypeGlyphBoundsTests
{
	/// <summary>Builds a font of three glyphs (empty .notdef, 'A' and 'B' simple squares) plus 64 trailing file bytes.</summary>
	private static (byte[] Data, int GlyphA, int GlyphB) BaseFont()
	{
		var builder = new TrueTypeFontTests.FontBuilder();
		var a = builder.AddSimple( 600, 'A', TrueTypeFontTests.Square( 0, 0, 500, 500 ) );
		var b = builder.AddSimple( 700, 'B', TrueTypeFontTests.Square( 100, -100, 300, 700 ) );
		var data = builder.Build().Concat( new byte[64] ).ToArray();
		return (data, a, b);
	}

	[TestMethod]
	public void GetMetricsAndGetOutlineRejectEverySliceOfOneToNineBytesWithLongLoca()
	{
		var (data, a, b) = BaseFont();
		var reference = TrueTypeFont.Parse( data );
		var aMetrics = reference.GetMetrics( a );
		var aOutline = Flatten( reference.GetOutline( a ) );
		Assert.AreEqual( new TrueTypeGlyphMetrics( 600, 0, 0, 0, 500, 500, 1 ), aMetrics, "the reference glyph parses as before" );

		for ( var length = 1; length <= 9; length++ )
		{
			var font = TrueTypeFont.Parse( WithGlyphLength( (byte[])data.Clone(), b, length ) );
			Assert.ThrowsException<InvalidDataException>( () => font.GetMetrics( b ), $"a {length}-byte loca slice" );
			Assert.ThrowsException<InvalidDataException>( () => font.GetOutline( b ), $"a {length}-byte loca slice" );
			// The empty and adjacent untouched glyphs behave exactly as before.
			Assert.AreEqual( new TrueTypeGlyphMetrics( 500, 0, 0, 0, 0, 0, 0 ), font.GetMetrics( 0 ), $"loca length {length}" );
			Assert.AreEqual( 0, font.GetOutline( 0 ).Count, $"loca length {length}" );
			Assert.AreEqual( aMetrics, font.GetMetrics( a ), $"loca length {length}" );
			CollectionAssert.AreEqual( aOutline, Flatten( font.GetOutline( a ) ), $"loca length {length}" );
		}
	}

	[TestMethod]
	public void GetMetricsAndGetOutlineRejectEvenSliceLengthsWithShortLoca()
	{
		var (data, a, b) = BaseFont();
		var reference = TrueTypeFont.Parse( data );
		var aMetrics = reference.GetMetrics( a );

		foreach ( var length in new[] { 2, 4, 6, 8 } )
		{
			var font = TrueTypeFont.Parse( WithShortLoca( WithGlyphLength( (byte[])data.Clone(), b, length ) ) );
			Assert.ThrowsException<InvalidDataException>( () => font.GetMetrics( b ), $"a {length}-byte loca slice" );
			Assert.ThrowsException<InvalidDataException>( () => font.GetOutline( b ), $"a {length}-byte loca slice" );
			Assert.AreEqual( aMetrics, font.GetMetrics( a ), $"loca length {length}" );
		}
	}

	[TestMethod]
	public void ExactTenByteZeroContourHeaderIsAcceptedInBothLocaLayouts()
	{
		foreach ( var longLoca in new[] { true, false } )
		{
			var (data, a, b) = BaseFont();
			var reference = TrueTypeFont.Parse( data );
			var (advance, lsb) = (reference.GetMetrics( b ).AdvanceWidth, reference.GetMetrics( b ).LeftSideBearing);
			var aMetrics = reference.GetMetrics( a );

			var mutated = (byte[])data.Clone();
			WithZeroHeader( mutated, b ); // zeroes the header and gives the glyph exactly its 10 bytes
			if ( !longLoca )
				WithShortLoca( mutated );
			var font = TrueTypeFont.Parse( mutated );
			Assert.AreEqual( new TrueTypeGlyphMetrics( advance, lsb, 0, 0, 0, 0, 0 ), font.GetMetrics( b ), longLoca ? "long loca" : "short loca" );
			Assert.AreEqual( 0, font.GetOutline( b ).Count, longLoca ? "long loca" : "short loca" );
			Assert.AreEqual( aMetrics, font.GetMetrics( a ), longLoca ? "long loca" : "short loca" );
		}
	}

	[TestMethod]
	public void GetMetricsAndGetOutlineRejectShortZeroContourSlices()
	{
		// The old whole-file bound turned a 9-byte zero-contour slice (8 with short loca, whose
		// offset/2 storage always yields even lengths) into a valid empty outline.
		var (longData, _, longGlyph) = BaseFont();
		var longFont = TrueTypeFont.Parse( WithGlyphLength( WithZeroHeader( (byte[])longData.Clone(), longGlyph ), longGlyph, 9 ) );
		Assert.ThrowsException<InvalidDataException>( () => longFont.GetMetrics( longGlyph ), "long loca" );
		Assert.ThrowsException<InvalidDataException>( () => longFont.GetOutline( longGlyph ), "long loca" );

		var (shortData, _, shortGlyph) = BaseFont();
		var shortFont = TrueTypeFont.Parse( WithShortLoca( WithGlyphLength( WithZeroHeader( (byte[])shortData.Clone(), shortGlyph ), shortGlyph, 8 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => shortFont.GetMetrics( shortGlyph ), "short loca" );
		Assert.ThrowsException<InvalidDataException>( () => shortFont.GetOutline( shortGlyph ), "short loca" );
	}

	[TestMethod]
	public void SimpleAndCompositeGlyphsKeepTheirExistingBehavior()
	{
		var (data, a, b) = BaseFont();
		var font = TrueTypeFont.Parse( data );
		Assert.AreEqual( new TrueTypeGlyphMetrics( 700, 100, 100, -100, 300, 700, 1 ), font.GetMetrics( b ) );

		var builder = new TrueTypeFontTests.FontBuilder();
		var bar = builder.AddSimple( 500, null, TrueTypeFontTests.Square( 0, 0, 250, 1000 ) );
		var composite = builder.AddComposite( 1000, 'U', (bar, 0, 0), (bar, 750, 0) );
		var compositeFont = TrueTypeFont.Parse( builder.Build() );
		Assert.AreEqual( new TrueTypeGlyphMetrics( 500, 0, 0, 0, 250, 1000, 1 ), compositeFont.GetMetrics( bar ) );
		var outline = compositeFont.GetOutline( composite );
		Assert.AreEqual( 2, outline.Count );
		Assert.AreEqual( 750f, outline[1].Min( point => point.X ) );
		CollectionAssert.AreEqual(
			new[] { (0f, 0f, true), (0f, 1000f, true), (250f, 1000f, true), (250f, 0f, true) },
			Flatten( compositeFont.GetOutline( bar ) ) );
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

	/// <summary>Rewrites the long loca so <paramref name="glyph"/>'s slice holds exactly <paramref name="length"/> bytes.</summary>
	private static byte[] WithGlyphLength( byte[] data, int glyph, int length )
	{
		var (loca, _) = FindTable( data, "loca" );
		var start = BinaryPrimitives.ReadUInt32BigEndian( data.AsSpan( loca + glyph * 4 ) );
		BinaryPrimitives.WriteUInt32BigEndian( data.AsSpan( loca + (glyph + 1) * 4 ), start + (uint)length );
		return data;
	}

	/// <summary>Zeroes the first 10 bytes of a glyph (zero contour count and bounding box) and gives it exactly those bytes.</summary>
	private static byte[] WithZeroHeader( byte[] data, int glyph )
	{
		var (glyf, _) = FindTable( data, "glyf" );
		var (loca, _) = FindTable( data, "loca" );
		var start = (int)BinaryPrimitives.ReadUInt32BigEndian( data.AsSpan( loca + glyph * 4 ) );
		data.AsSpan( glyf + start, 10 ).Clear();
		return WithGlyphLength( data, glyph, 10 );
	}

	/// <summary>Converts the font from long loca (indexToLocFormat 1) to short loca (0); entries become offset/2 words.</summary>
	private static byte[] WithShortLoca( byte[] data )
	{
		var (head, _) = FindTable( data, "head" );
		BinaryPrimitives.WriteUInt16BigEndian( data.AsSpan( head + 50 ), 0 );
		var (loca, locaLength) = FindTable( data, "loca" );
		for ( var i = 0; i < locaLength / 4; i++ )
		{
			var offset = BinaryPrimitives.ReadUInt32BigEndian( data.AsSpan( loca + i * 4 ) );
			BinaryPrimitives.WriteUInt16BigEndian( data.AsSpan( loca + i * 2 ), (ushort)(offset / 2) );
		}
		return data;
	}
}
