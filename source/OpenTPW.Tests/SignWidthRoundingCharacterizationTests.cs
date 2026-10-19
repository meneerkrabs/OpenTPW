using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// COMPAT-002 characterization of the managed sign-width arithmetic: fractional hmtx advances at 100 percent,
/// per-character truncation at other percentages, continuous horizontal/LOGFONT width scaling, and long synthetic
/// names. These tests describe the current OpenTPW layout only. They do not compare with, or claim parity to,
/// native GDI/Uniscribe rounding, and they use no game fonts or data.
/// </summary>
[TestClass]
public class SignWidthRoundingCharacterizationTests
{
	private const float Tolerance = 1e-4f;

	private static TrueTypeFont Font( params (ushort Advance, char Character)[] glyphs )
	{
		var builder = new TrueTypeFontTests.FontBuilder();
		foreach ( var (advance, character) in glyphs )
			builder.AddSimple( advance, character, TrueTypeFontTests.Square( 0, 0, 500, 500 ) );
		return TrueTypeFont.Parse( builder.Build(), "synthetic-compat-002" );
	}

	/// <summary>
	/// <see cref="TrueTypeFontTests.FontBuilder"/> emits no OS/2 table, so xAvgCharWidth is 0 and
	/// LogFontHorizontalScale is neutral. This test-only wrapper appends a minimal 78-byte OS/2 table so the
	/// existing average-width rule can be exercised with synthetic metrics.
	/// </summary>
	private static TrueTypeFont FontWithAverageCharWidth( short averageCharWidth, params (ushort Advance, char Character)[] glyphs )
	{
		var builder = new TrueTypeFontTests.FontBuilder();
		foreach ( var (advance, character) in glyphs )
			builder.AddSimple( advance, character, TrueTypeFontTests.Square( 0, 0, 500, 500 ) );
		return TrueTypeFont.Parse( AddOs2( builder.Build(), averageCharWidth ), "synthetic-compat-002-os2" );
	}

	/// <summary>Rebuilds a FontBuilder file with an OS/2 table; table checksums are not read by the parser.</summary>
	private static byte[] AddOs2( byte[] font, short averageCharWidth )
	{
		var tableCount = BinaryPrimitives.ReadUInt16BigEndian( font.AsSpan( 4, 2 ) );
		var tables = new List<(string Tag, byte[] Data)>( tableCount + 1 );
		for ( var i = 0; i < tableCount; i++ )
		{
			var record = 12 + i * 16;
			var tag = Encoding.ASCII.GetString( font, record, 4 );
			var offset = (int)BinaryPrimitives.ReadUInt32BigEndian( font.AsSpan( record + 8, 4 ) );
			var length = (int)BinaryPrimitives.ReadUInt32BigEndian( font.AsSpan( record + 12, 4 ) );
			tables.Add( (tag, font.AsSpan( offset, length ).ToArray()) );
		}

		var os2 = new byte[78];
		BinaryPrimitives.WriteInt16BigEndian( os2.AsSpan( 2 ), averageCharWidth );
		BinaryPrimitives.WriteUInt16BigEndian( os2.AsSpan( 4 ), 400 );
		BinaryPrimitives.WriteUInt16BigEndian( os2.AsSpan( 74 ), 800 );
		BinaryPrimitives.WriteUInt16BigEndian( os2.AsSpan( 76 ), 200 );
		tables.Add( ("OS/2", os2) );

		var directoryLength = 12 + tables.Count * 16;
		var output = new byte[directoryLength + tables.Sum( table => (table.Data.Length + 3) & ~3 )];
		BinaryPrimitives.WriteUInt32BigEndian( output.AsSpan( 0 ), 0x00010000 );
		BinaryPrimitives.WriteUInt16BigEndian( output.AsSpan( 4 ), (ushort)tables.Count );
		var position = directoryLength;
		for ( var i = 0; i < tables.Count; i++ )
		{
			var record = 12 + i * 16;
			Encoding.ASCII.GetBytes( tables[i].Tag ).CopyTo( output, record );
			BinaryPrimitives.WriteUInt32BigEndian( output.AsSpan( record + 8 ), (uint)position );
			BinaryPrimitives.WriteUInt32BigEndian( output.AsSpan( record + 12 ), (uint)tables[i].Data.Length );
			tables[i].Data.CopyTo( output, position );
			position += (tables[i].Data.Length + 3) & ~3;
		}
		return output;
	}

	[TestMethod]
	public void HundredPercentKeepsFractionalAdvancesAndHorizontalScaleStaysContinuous()
	{
		var font = Font( (601, 'A'), (603, 'B') );

		Assert.AreEqual( 60.1f, SignTextLayout.Measure( font, "A", 100 ).Width, Tolerance );
		Assert.AreEqual( 120.4f, SignTextLayout.Measure( font, "AB", 100 ).Width, Tolerance );
		Assert.AreEqual( 180.6f, SignTextLayout.Measure( font, "AB", 100, horizontalScale: 1.5f ).Width, Tolerance,
			"100 percent keeps the fractional advance after the horizontal scale" );
	}

	[TestMethod]
	public void PercentTruncatesEachCharacterAdvanceNotTheLineTotal()
	{
		var font = Font( (612, 'A') ); // 61.2 px per A at a 100 px em.

		Assert.AreEqual( 61.2f, SignTextLayout.ScaledAdvance( 61.2f, 100 ), Tolerance );
		Assert.AreEqual( 30f, SignTextLayout.ScaledAdvance( 61.2f, 50 ), Tolerance );

		var full = SignTextLayout.Measure( font, "AA", 100 ).Width;
		var half = SignTextLayout.Measure( font, "AA", 100, widthPercent: 50 ).Width;
		Assert.AreEqual( 122.4f, full, Tolerance );
		Assert.AreEqual( 60f, half, Tolerance, "each 30.6 px advance truncates to 30 before the line is summed" );
		Assert.AreEqual( 61f, MathF.Truncate( full * 50 / 100f ), Tolerance, "truncating the line total instead would give 61" );
	}

	[TestMethod]
	public void PercentTruncationHappensAfterHorizontalScaleAndLosesFractions()
	{
		var font = Font( (601, 'A'), (603, 'B') );
		var full = SignTextLayout.Measure( font, "AB", 100, horizontalScale: 1.5f ).Width;
		var percent = SignTextLayout.Measure( font, "AB", 100, horizontalScale: 1.5f, widthPercent: 90 ).Width;

		Assert.AreEqual( 180.6f, full, Tolerance );
		Assert.AreEqual( 162f, percent, Tolerance, "90.15 and 90.45 px truncate to 81 each" );
		Assert.AreEqual( 162.54f, full * 0.9f, Tolerance );
		Assert.IsTrue( MathF.Abs( percent - full * 0.9f ) > 0.5f, "percent truncation is not a continuous 90 percent of the line" );
	}

	[TestMethod]
	public void LogFontWidthScaleIsContinuousWhilePercentQuantizes()
	{
		var font = FontWithAverageCharWidth( 600, (601, 'A') );
		Assert.AreEqual( 600, font.AverageCharWidth );

		Assert.AreEqual( 1f, SignTextLayout.LogFontHorizontalScale( font, 100, 0 ), Tolerance );
		Assert.AreEqual( 1f, SignTextLayout.LogFontHorizontalScale( font, 100, -90 ), Tolerance );
		Assert.AreEqual( 1f, SignTextLayout.LogFontHorizontalScale( font, 100, 60 ), Tolerance );
		Assert.AreEqual( 1.5f, SignTextLayout.LogFontHorizontalScale( font, 100, 90 ), 1e-5f );

		var logFontScaled = SignTextLayout.Measure( font, "A", 100, horizontalScale: SignTextLayout.LogFontHorizontalScale( font, 100, 90 ) ).Width;
		Assert.AreEqual( 90.15f, logFontScaled, Tolerance, "the LOGFONT width ratio does not round" );
		Assert.AreEqual( 81f, SignTextLayout.Measure( font, "A", 100, horizontalScale: 1.5f, widthPercent: 90 ).Width, Tolerance,
			"the percent truncates the already scaled advance" );
	}

	[TestMethod]
	public void SyntheticFontWithoutOs2HasNoLogFontWidthScale()
	{
		var font = Font( (601, 'A') );

		Assert.AreEqual( 0, font.AverageCharWidth );
		Assert.AreEqual( 1f, SignTextLayout.LogFontHorizontalScale( font, 100, 120 ), Tolerance );
		Assert.AreEqual( SignTextLayout.Measure( font, "A", 100 ).Width,
			SignTextLayout.Measure( font, "A", 100, horizontalScale: SignTextLayout.LogFontHorizontalScale( font, 100, 120 ) ).Width, Tolerance );
	}

	[TestMethod]
	public void LongSyntheticNameCharacterizesPercentAndLogFontWidthFits()
	{
		const int Count = 32;
		var name = new string( 'A', Count );
		var font = FontWithAverageCharWidth( 600, (601, 'A') );

		var full = SignTextLayout.Measure( font, name, 144 ).Width;
		var percent = SignTextLayout.Measure( font, name, 144, widthPercent: 90 ).Width;
		Assert.AreEqual( Count * 601 * 0.144f, full, 1e-2f );
		Assert.AreEqual( Count * 77f, percent, 1e-2f, "86.544 px at 90 percent truncates to 77 px per character" );
		Assert.IsTrue( full * 0.9f - percent > 20f, "the per-character loss accumulates over a long name" );

		float Extent( int width, int widthPercent ) =>
			SignTextLayout.Measure( font, name, 144, SignTextLayout.LogFontHorizontalScale( font, 144, width ), widthPercent: widthPercent ).Width;

		var fitted = SignTextLayout.FitLogFontWidth( font, name, 144, 58, 512 );
		Assert.AreEqual( 15, fitted, "average-width scaling narrows the 32-character name to LOGFONT width 15" );
		Assert.IsTrue( Extent( fitted, 100 ) < 512 );
		Assert.IsTrue( Extent( fitted + 3, 100 ) >= 512 );

		var fittedAt90 = SignTextLayout.FitLogFontWidth( font, name, 144, 58, 512, widthPercent: 90 );
		Assert.AreEqual( 17, fittedAt90, "the percent lets a wider LOGFONT width fit" );
		Assert.IsTrue( fittedAt90 > fitted );
		Assert.IsTrue( Extent( fittedAt90, 90 ) < 512 );
		Assert.IsTrue( Extent( fittedAt90 + 3, 90 ) >= 512 );
	}
}
