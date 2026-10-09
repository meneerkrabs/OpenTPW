using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class TrueTypeFontTests
{
	// ---- Synthetic fonts (no assets) ----

	/// <summary>Builds a minimal glyf-outline TrueType file (upem 1000, ascender 800, descender -200).</summary>
	internal sealed class FontBuilder
	{
		private readonly List<byte[]> glyphs = new() { Array.Empty<byte>() };
		private readonly List<(ushort Advance, short Lsb)> metrics = new() { (500, 0) };
		private readonly SortedDictionary<int, int> map = new();
		private readonly List<(int Left, int Right, short Value)> kerning = new();

		public int AddSimple( ushort advance, char? character, params (int X, int Y, bool On)[][] contours )
		{
			var body = new List<byte>();
			var points = contours.SelectMany( contour => contour ).ToArray();
			void I16( int value ) { body.Add( (byte)(value >> 8) ); body.Add( (byte)value ); }
			I16( contours.Length );
			I16( points.Min( point => point.X ) );
			I16( points.Min( point => point.Y ) );
			I16( points.Max( point => point.X ) );
			I16( points.Max( point => point.Y ) );
			var end = -1;
			foreach ( var contour in contours )
			{
				end += contour.Length;
				I16( end );
			}
			I16( 0 );
			foreach ( var point in points )
				body.Add( (byte)(point.On ? 1 : 0) );
			var previous = 0;
			foreach ( var point in points )
			{
				I16( point.X - previous );
				previous = point.X;
			}
			previous = 0;
			foreach ( var point in points )
			{
				I16( point.Y - previous );
				previous = point.Y;
			}
			return Add( body.ToArray(), advance, (short)points.Min( point => point.X ), character );
		}

		public int AddComposite( ushort advance, char? character, params (int Glyph, int Dx, int Dy)[] components )
		{
			var body = new List<byte>();
			void I16( int value ) { body.Add( (byte)(value >> 8) ); body.Add( (byte)value ); }
			I16( -1 );
			I16( 0 );
			I16( 0 );
			I16( 1000 );
			I16( 1000 );
			for ( var i = 0; i < components.Length; i++ )
			{
				I16( 0x0001 | 0x0002 | (i < components.Length - 1 ? 0x0020 : 0) );
				I16( components[i].Glyph );
				I16( components[i].Dx );
				I16( components[i].Dy );
			}
			return Add( body.ToArray(), advance, 0, character );
		}

		public void AddKerning( int left, int right, short value ) => kerning.Add( (left, right, value) );

		private int Add( byte[] body, ushort advance, short lsb, char? character )
		{
			glyphs.Add( body.Length % 2 == 0 ? body : body.Append( (byte)0 ).ToArray() );
			metrics.Add( (advance, lsb) );
			if ( character != null )
				map[character.Value] = glyphs.Count - 1;
			return glyphs.Count - 1;
		}

		public byte[] Build()
		{
			var tables = new SortedDictionary<string, byte[]>( StringComparer.Ordinal );
			var head = new byte[54];
			BinaryPrimitives.WriteUInt32BigEndian( head.AsSpan( 0 ), 0x00010000 );
			BinaryPrimitives.WriteUInt16BigEndian( head.AsSpan( 18 ), 1000 );
			BinaryPrimitives.WriteInt16BigEndian( head.AsSpan( 42 ), 1000 );
			BinaryPrimitives.WriteInt16BigEndian( head.AsSpan( 50 ), 1 );
			tables["head"] = head;
			var hhea = new byte[36];
			BinaryPrimitives.WriteInt16BigEndian( hhea.AsSpan( 4 ), 800 );
			BinaryPrimitives.WriteInt16BigEndian( hhea.AsSpan( 6 ), -200 );
			BinaryPrimitives.WriteUInt16BigEndian( hhea.AsSpan( 34 ), (ushort)glyphs.Count );
			tables["hhea"] = hhea;
			var maxp = new byte[6];
			BinaryPrimitives.WriteUInt32BigEndian( maxp, 0x00005000 );
			BinaryPrimitives.WriteUInt16BigEndian( maxp.AsSpan( 4 ), (ushort)glyphs.Count );
			tables["maxp"] = maxp;
			var hmtx = new byte[glyphs.Count * 4];
			for ( var i = 0; i < glyphs.Count; i++ )
			{
				BinaryPrimitives.WriteUInt16BigEndian( hmtx.AsSpan( i * 4 ), metrics[i].Advance );
				BinaryPrimitives.WriteInt16BigEndian( hmtx.AsSpan( i * 4 + 2 ), metrics[i].Lsb );
			}
			tables["hmtx"] = hmtx;
			var loca = new byte[(glyphs.Count + 1) * 4];
			var glyf = new List<byte>();
			for ( var i = 0; i < glyphs.Count; i++ )
			{
				BinaryPrimitives.WriteUInt32BigEndian( loca.AsSpan( i * 4 ), (uint)glyf.Count );
				glyf.AddRange( glyphs[i] );
			}
			BinaryPrimitives.WriteUInt32BigEndian( loca.AsSpan( glyphs.Count * 4 ), (uint)glyf.Count );
			tables["loca"] = loca;
			tables["glyf"] = glyf.ToArray();
			tables["cmap"] = BuildCmap();
			if ( kerning.Count > 0 )
			{
				var kern = new byte[4 + 14 + kerning.Count * 6];
				BinaryPrimitives.WriteUInt16BigEndian( kern.AsSpan( 2 ), 1 );
				BinaryPrimitives.WriteUInt16BigEndian( kern.AsSpan( 6 ), (ushort)(14 + kerning.Count * 6) );
				BinaryPrimitives.WriteUInt16BigEndian( kern.AsSpan( 8 ), 0x0001 );
				BinaryPrimitives.WriteUInt16BigEndian( kern.AsSpan( 10 ), (ushort)kerning.Count );
				for ( var i = 0; i < kerning.Count; i++ )
				{
					BinaryPrimitives.WriteUInt16BigEndian( kern.AsSpan( 18 + i * 6 ), (ushort)kerning[i].Left );
					BinaryPrimitives.WriteUInt16BigEndian( kern.AsSpan( 20 + i * 6 ), (ushort)kerning[i].Right );
					BinaryPrimitives.WriteInt16BigEndian( kern.AsSpan( 22 + i * 6 ), kerning[i].Value );
				}
				tables["kern"] = kern;
			}

			var output = new List<byte>();
			var directory = new byte[12 + tables.Count * 16];
			BinaryPrimitives.WriteUInt32BigEndian( directory, 0x00010000 );
			BinaryPrimitives.WriteUInt16BigEndian( directory.AsSpan( 4 ), (ushort)tables.Count );
			var offset = directory.Length;
			var index = 0;
			foreach ( var (tag, data) in tables )
			{
				var record = 12 + index++ * 16;
				System.Text.Encoding.ASCII.GetBytes( tag ).CopyTo( directory, record );
				BinaryPrimitives.WriteUInt32BigEndian( directory.AsSpan( record + 8 ), (uint)offset );
				BinaryPrimitives.WriteUInt32BigEndian( directory.AsSpan( record + 12 ), (uint)data.Length );
				offset += (data.Length + 3) & ~3;
			}
			output.AddRange( directory );
			foreach ( var data in tables.Values )
			{
				output.AddRange( data );
				while ( output.Count % 4 != 0 )
					output.Add( 0 );
			}
			return output.ToArray();
		}

		private byte[] BuildCmap()
		{
			// One segment per mapped character plus the 0xFFFF terminator (format 4, idDelta mapping).
			var segments = map.Select( pair => (Start: pair.Key, End: pair.Key, Delta: pair.Value - pair.Key) ).Append( (Start: 0xFFFF, End: 0xFFFF, Delta: 1) ).ToArray();
			var count = segments.Length;
			var length = 16 + count * 8;
			var table = new byte[4 + 8 + length];
			BinaryPrimitives.WriteUInt16BigEndian( table.AsSpan( 2 ), 1 );
			BinaryPrimitives.WriteUInt16BigEndian( table.AsSpan( 4 ), 3 );
			BinaryPrimitives.WriteUInt16BigEndian( table.AsSpan( 6 ), 1 );
			BinaryPrimitives.WriteUInt32BigEndian( table.AsSpan( 8 ), 12 );
			var sub = table.AsSpan( 12 );
			BinaryPrimitives.WriteUInt16BigEndian( sub, 4 );
			BinaryPrimitives.WriteUInt16BigEndian( sub[2..], (ushort)length );
			BinaryPrimitives.WriteUInt16BigEndian( sub[6..], (ushort)(count * 2) );
			for ( var i = 0; i < count; i++ )
			{
				BinaryPrimitives.WriteUInt16BigEndian( sub[(14 + i * 2)..], (ushort)segments[i].End );
				BinaryPrimitives.WriteUInt16BigEndian( sub[(16 + count * 2 + i * 2)..], (ushort)segments[i].Start );
				BinaryPrimitives.WriteInt16BigEndian( sub[(16 + count * 4 + i * 2)..], (short)segments[i].Delta );
			}
			return table;
		}
	}

	private static (int, int, bool)[] Square( int x0, int y0, int x1, int y1, bool clockwise = true ) => clockwise
		? new[] { (x0, y0, true), (x0, y1, true), (x1, y1, true), (x1, y0, true) }
		: new[] { (x0, y0, true), (x1, y0, true), (x1, y1, true), (x0, y1, true) };

	[TestMethod]
	public void ParsesTablesMetricsCharacterMapAndKerning()
	{
		var builder = new FontBuilder();
		var square = builder.AddSimple( 600, 'A', Square( 0, 0, 500, 500 ) );
		var other = builder.AddSimple( 700, 'B', Square( 100, -100, 300, 700 ) );
		builder.AddKerning( square, other, -50 );
		var font = TrueTypeFont.Parse( builder.Build(), "synthetic" );
		Assert.AreEqual( 1000, font.UnitsPerEm );
		Assert.AreEqual( 3, font.GlyphCount );
		Assert.AreEqual( (800, -200), (font.Ascender, font.Descender) );
		Assert.AreEqual( (800, 200), (font.WindowsAscent, font.WindowsDescent), "falls back to hhea without OS/2" );
		Assert.AreEqual( (3, 1, 4), font.CharacterMapSource );
		Assert.AreEqual( square, font.GetGlyphIndex( 'A' ) );
		Assert.AreEqual( other, font.GetGlyphIndex( 'B' ) );
		Assert.AreEqual( 0, font.GetGlyphIndex( 'C' ) );
		Assert.AreEqual( new TrueTypeGlyphMetrics( 700, 100, 100, -100, 300, 700, 1 ), font.GetMetrics( other ) );
		Assert.AreEqual( -50, font.GetKerning( square, other ) );
		Assert.AreEqual( 0, font.GetKerning( other, square ) );
		Assert.AreEqual( 0.032f, font.ScaleForGdiHeight( -32 ), 1e-6f );
		Assert.AreEqual( 0.032f, font.ScaleForGdiHeight( 32 ), 1e-6f, "positive height = cell height (ascent + descent = 1000)" );
	}

	[TestMethod]
	public void RasterizesWithExactAreaCoverageOnPixelBoundaries()
	{
		var builder = new FontBuilder();
		var glyph = builder.AddSimple( 1000, 'A', Square( 0, 0, 1000, 1000 ) );
		var font = TrueTypeFont.Parse( builder.Build() );
		var bitmap = TrueTypeRasterizer.RasterizeGlyph( font, glyph, 0.004f ); // 4x4 pixels
		Assert.AreEqual( (4, 4, 0, 4), (bitmap.Width, bitmap.Height, bitmap.OriginX, bitmap.OriginY) );
		Assert.IsTrue( bitmap.Coverage.All( value => value == 255 ) );

		// Half-pixel offset: the left and right columns are half covered.
		var shifted = TrueTypeRasterizer.Rasterize( new[] { (font.GetOutline( glyph ), 125f) }, 0.004f );
		Assert.AreEqual( 5, shifted.Width );
		for ( var y = 0; y < 4; y++ )
		{
			var row = Enumerable.Range( 0, 5 ).Select( x => (int)shifted[x, y] ).ToArray();
			Assert.IsTrue( Math.Abs( row[0] - 128 ) <= 1 && Math.Abs( row[4] - 128 ) <= 1 && row[1..4].All( value => value == 255 ), string.Join( ",", row ) );
		}
	}

	[TestMethod]
	public void UsesNonZeroWindingForOverlapsAndHoles()
	{
		var builder = new FontBuilder();
		var overlap = builder.AddSimple( 1000, 'O', Square( 0, 0, 600, 1000 ), Square( 400, 0, 1000, 1000 ) );
		var hole = builder.AddSimple( 1000, 'H', Square( 0, 0, 1000, 1000 ), Square( 250, 250, 750, 750, clockwise: false ) );
		var sameDirection = builder.AddSimple( 1000, 'S', Square( 0, 0, 1000, 1000 ), Square( 250, 250, 750, 750 ) );
		var font = TrueTypeFont.Parse( builder.Build() );
		Assert.IsTrue( TrueTypeRasterizer.RasterizeGlyph( font, overlap, 0.004f ).Coverage.All( value => value == 255 ), "overlapping contours stay at full coverage" );
		var holed = TrueTypeRasterizer.RasterizeGlyph( font, hole, 0.004f );
		Assert.AreEqual( 0, holed[1, 1] );
		Assert.AreEqual( 0, holed[2, 2] );
		Assert.AreEqual( 255, holed[0, 0] );
		Assert.IsTrue( TrueTypeRasterizer.RasterizeGlyph( font, sameDirection, 0.004f ).Coverage.All( value => value == 255 ), "a same-direction inner contour is not a hole under non-zero winding" );
	}

	[TestMethod]
	public void FlattensQuadraticCurvesAndImpliedOnCurvePoints()
	{
		var builder = new FontBuilder();
		// A circle-like contour of four off-curve points only (all on-curve points implied).
		var circle = builder.AddSimple( 1000, 'C', new[] { (500, 0, false), (0, 500, false), (500, 1000, false), (1000, 500, false) } );
		var font = TrueTypeFont.Parse( builder.Build() );
		var bitmap = TrueTypeRasterizer.RasterizeGlyph( font, circle, 0.064f );
		Assert.AreEqual( (48, 48), (bitmap.Width, bitmap.Height) );
		var covered = bitmap.Coverage.Sum( value => value / 255.0 );
		// The implied curve is a rounded diamond between the inscribed diamond (area 0.5) and the square.
		Assert.IsTrue( covered > 0.6 * 48 * 48 && covered < 0.75 * 48 * 48, $"coverage {covered}" );
		Assert.AreEqual( 255, bitmap[24, 24] );
		Assert.AreEqual( 0, bitmap[0, 0] );
	}

	[TestMethod]
	public void FlattensCompositeGlyphsWithOffsets()
	{
		var builder = new FontBuilder();
		var bar = builder.AddSimple( 500, null, Square( 0, 0, 250, 1000 ) );
		var composite = builder.AddComposite( 1000, 'U', (bar, 0, 0), (bar, 750, 0) );
		var font = TrueTypeFont.Parse( builder.Build() );
		var outline = font.GetOutline( composite );
		Assert.AreEqual( 2, outline.Count );
		Assert.AreEqual( 750f, outline[1].Min( point => point.X ) );
		var bitmap = TrueTypeRasterizer.RasterizeGlyph( font, composite, 0.004f );
		CollectionAssert.AreEqual( new byte[] { 255, 0, 0, 255 }, Enumerable.Range( 0, 4 ).Select( x => bitmap[x, 0] ).ToArray() );
	}

	[TestMethod]
	public void RejectsTruncatedOrForeignData()
	{
		var builder = new FontBuilder();
		builder.AddSimple( 600, 'A', Square( 0, 0, 500, 500 ) );
		var data = builder.Build();
		Assert.ThrowsException<InvalidDataException>( () => TrueTypeFont.Parse( data[..40] ) );
		Assert.ThrowsException<InvalidDataException>( () => TrueTypeFont.Parse( "OTTO\0\0\0\0\0\0\0\0"u8.ToArray() ) );
		var corrupt = (byte[])data.Clone();
		// Point the first table past the end of the file.
		BinaryPrimitives.WriteUInt32BigEndian( corrupt.AsSpan( 12 + 8 ), (uint)data.Length );
		Assert.ThrowsException<InvalidDataException>( () => TrueTypeFont.Parse( corrupt ) );
	}

	[TestMethod]
	public void LaysOutTextWithAdvancesKerningAndMissingGlyphReport()
	{
		var builder = new FontBuilder();
		var a = builder.AddSimple( 600, 'A', Square( 0, 0, 500, 500 ) );
		var b = builder.AddSimple( 700, 'B', Square( 0, 0, 500, 500 ) );
		builder.AddKerning( a, b, -100 );
		var font = TrueTypeFont.Parse( builder.Build() );
		var metrics = SignTextLayout.Measure( font, "AB", 100, kerning: true );
		Assert.AreEqual( (600 - 100 + 700) * 0.1f, metrics.Width, 1e-4f );
		Assert.AreEqual( (80f, 20f), (metrics.Ascent, metrics.Descent) );
		Assert.AreEqual( (600 + 700) * 0.1f, SignTextLayout.Measure( font, "AB", 100, kerning: false ).Width, 1e-4f );
		CollectionAssert.AreEqual( new[] { (int)'Z' }, SignTextLayout.FindMissing( font, "A Z\n" ).ToArray() );
		CollectionAssert.AreEqual( new[] { (int)'Z' }, SignTextLayout.Measure( font, "AZ", 100 ).MissingCodePoints.ToArray() );

		var canvas = new byte[32 * 64 * 4];
		var used = SignTextLayout.DrawLine( canvas, 32, 64, font, "AB", 40, (255, 0, 0), margin: 0, kerning: true );
		Assert.AreEqual( 40 * 32 / (1200 * 0.04f), used, 1e-3f, "shrunk to fit the canvas width" );
		Assert.IsTrue( canvas.Where( ( value, index ) => index % 4 == 3 ).Any( value => value == 255 ) );
		var (rgba, width, height, baseline) = SignTextLayout.RenderRgba( font, "A", 10, (1, 2, 3), padding: 1 );
		Assert.AreEqual( (7, 7, 6), (width, height, baseline) );
		CollectionAssert.AreEqual( new byte[] { 1, 2, 3, 255 }, rgba.AsSpan( (1 * width + 1) * 4, 4 ).ToArray() );
		Assert.AreEqual( 0, rgba[3], "padding is transparent" );
	}

	// ---- Original fonts: Data/fonts.wad (OPENTPW_GAME_PATH) ----

	private static SignFontLibrary OriginalLibrary()
	{
		var path = Path.Combine( StringTableTests.OriginalDataDirectory(), "fonts.wad" );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( "The selected installation has no fonts.wad." );
		using var stream = File.OpenRead( path );
		return SignFontLibrary.Load( stream );
	}

	[TestMethod]
	public void ReadsAllSeventeenOriginalFontsFromTheWadInMemory()
	{
		var library = OriginalLibrary();
		Assert.AreEqual( 0, library.Diagnostics.Count, string.Join( "; ", library.Diagnostics ) );
		CollectionAssert.AreEqual( new[] { "BIGLA___.TTF", "BIGLOA__.TTF", "CLUNAB__.TTF", "CLUNA___.TTF", "EGGITAOE.TTF", "GARGA___.TTF", "GARGSAI_.TTF", "GARGSA__.TTF",
			"HAUNTAOE.TTF", "INTRAB__.TTF", "INTRA___.TTF", "KRELAB__.TTF", "KRELA___.TTF", "LINUPA__.TTF", "TANNAO__.TTF", "TANNA___.TTF", "YOUNIA__.TTF" }, library.FileNames.ToArray() );
		Assert.AreEqual( SignFontLibrary.ShippedFontCount, library.Fonts.Count() );
		foreach ( var font in library.Fonts )
		{
			Assert.AreEqual( 2048, font.UnitsPerEm, font.SourceName );
			Assert.AreEqual( (3, 1, 4), font.CharacterMapSource, font.SourceName );
			Assert.AreEqual( 307, font.CodePoints.Count, font.SourceName );
			Assert.IsFalse( font.HasGlyph( '€' ), "1999 fonts predate the euro sign" );
			for ( var glyph = 0; glyph < font.GlyphCount; glyph++ )
				font.GetOutline( glyph );
		}
		Assert.AreSame( library.Get( "YOUNIA__.TTF" ), library.Get( "young itch aoe" ) );
		Assert.AreSame( library.Get( "CLUNAB__.TTF" ), library.Get( "Clunker AOE Bold" ) );
		Assert.IsNull( library.Find( "Arial" ) );
	}

	/// <summary>
	/// File SHA-256 (identical to the loose TTFs the original setup unpacks into <c>tpwfnt</c>), glyph
	/// metrics of 'A' and the hash of 'A' rasterized at a 32-pixel em.
	/// </summary>
	[DataTestMethod]
	[DataRow( "BIGLA___.TTF", "Big Limbo AOE", "Regular", 306, 36, 1031, 0, 0, 21, 1006, 1589, 1347, "B0EC3340B2521E59E8FE67534508F6284A7CDFBA13C191272F4CC3970AC72C60", "F78FF7827896973222806086B082ED7416351A19B8248A18D8F5E40B999BC95A", 16, 25 )]
	[DataRow( "BIGLOA__.TTF", "Big Limbo'd Out AOE", "Regular", 308, 36, 1032, -61, -61, -46, 1071, 1640, 1347, "03D234A8E80BF8CCF3B1DA4B2909A821FC1143A666B615818000C6FBCBE11299", "83CEA2D7FC0C507E13011B3F3744222F817A8DBE6FA7FB0974C58A6197667EC7", 18, 27 )]
	[DataRow( "CLUNAB__.TTF", "Clunker AOE", "Bold", 307, 36, 1210, -132, -132, -192, 1212, 1561, 1868, "3F3F3934B9DBDF45FAD5FF92FE59B2D5B4157345A219BE3FB3F1D449985519B8", "366C8684321614B65A065435410935F00093076467C9B2DFD777C935DE79D79D", 22, 28 )]
	[DataRow( "CLUNA___.TTF", "Clunker AOE", "Regular", 307, 36, 1210, 0, 0, 0, 1099, 1500, 1868, "583E9201F0AEEAD00284E0BBE474887328FAF816457C8767A97810636ED2703F", "110DFFE4061D228A4C6F0F810B267DE013A5B742B61AA1B4373E1A996879B5DC", 18, 24 )]
	[DataRow( "EGGITAOE.TTF", "EggIt AOE", "Regular", 308, 36, 606, 9, 9, 48, 591, 1182, 1800, "72D4AB6117F8F2D6C9A0A5A243ED91EB532327309E245E1CFD13E14D8FED5FFE", "3D2D6733032EBBE3938F7B4D9454D9E94E32E195EED1C6970B0DD9BD15E913DC", 10, 19 )]
	[DataRow( "GARGA___.TTF", "Gargamel AOE", "Regular", 306, 36, 1023, 1, 1, 8, 933, 1521, 464, "03E930B9F91DE5EEA7C62D438A76573B8C1F25F8BF74BA96575373759BF31D68", "1873F2B4AF1C2623695578B6F94C459CB6280BEDE34374A57DFDE6BB177891D0", 15, 24 )]
	[DataRow( "GARGSAI_.TTF", "Gargamel Smurf AOE", "Italic", 308, 36, 1024, 2, 2, 6, 989, 1075, 464, "68B3E8AE58D97A611CC5E5ADF4B2A0459502385BDCE70022B7064575B920AEC2", "327F32A68EB9EDD885DA17EE47DF75EB0FA710656441B013F3B2A19916287573", 16, 17 )]
	[DataRow( "GARGSA__.TTF", "Gargamel Smurf AOE", "Regular", 308, 36, 1024, 1, 1, 6, 933, 1075, 464, "011545802224EF6E22F024BBAF6E761437E68A9E7CA6A7B023150ED2109A69BF", "897F57CC45264A0F05CF4814852207717591509753C852AAB72C1E1D8E1F9FF6", 15, 17 )]
	[DataRow( "HAUNTAOE.TTF", "Haunt AOE", "Regular", 307, 36, 738, 4, 4, 50, 653, 1577, 1000, "9F829459AE2E1AD4C352D725523FA376EC61FF3FBBAC808DE83732597B8F992A", "5686AF3DC634F698C31099AF16285AB2096A4A41789169514D26AB4C3AA7E66A", 11, 25 )]
	[DataRow( "INTRAB__.TTF", "Intruder AOE", "Bold", 307, 36, 977, 0, 0, 76, 891, 1613, 967, "B5D80C96377170A5F145DEA96D5326EAE1E93FC3E5F113DFCF7A6C1839F087B8", "CA6F2EFA5F09F1B0111D8C87CB2FFF974A58E918303F73563665F8B679AB73B4", 14, 25 )]
	[DataRow( "INTRA___.TTF", "Intruder AOE", "Regular", 307, 36, 977, 0, 0, 76, 891, 1613, 967, "C3946E29D0505CC07FCE7F0CC4F232E23F04701D15701E1F2F41EE748CA0A0D6", "E83A2B03C8E902412C5BE92FB505E0B437DB87888BAFC1786D52AAB54589FBA4", 14, 25 )]
	[DataRow( "KRELAB__.TTF", "Krelesanta AOE", "Bold", 308, 36, 488, -8, -8, -17, 449, 1418, 1117, "F3B744FB958623BAA95BF5D1527051C48CDCA243E878C4BC89CB6D31941F7A52", "C7A5FE933BA5BD051479EC45D6C6BBA379522127A0A067C15421B1852D1844AF", 9, 24 )]
	[DataRow( "KRELA___.TTF", "Krelesanta AOE", "Regular", 306, 36, 488, 3, 3, -1, 435, 1404, 1117, "AC7F4AB9B38440A9B60A2FA3AE3F5AB034EAD0D2AD026EE2F35AEFEF0A60D63C", "631FED0DDB9FD19509F6E1E61D3C7597F3241EB4701D7016B2FA63D9E113611B", 7, 23 )]
	[DataRow( "LINUPA__.TTF", "LinusPlay AOE", "Regular", 307, 36, 1845, 0, 0, 0, 1758, 1638, 12657, "76AF5878F5446F9E03C744FC791AB27ACBDA6B89613BC42D02914C0CFA2D8BED", "FA9CBCD1ED60F37BE9071763B78C493543BDC58E93140E249EB99B946CBB458C", 28, 26 )]
	[DataRow( "TANNAO__.TTF", "Tannarin AOE", "Oblique", 308, 36, 2105, 142, 142, 0, 2145, 1302, 2000, "9CC389F75CE034DF3970C8F38CD0C19E167A64579D77F1FE93A1B194F04B5097", "4781CC3323C0D61904C2982FF8031E02E25FF26A163FD5D0996B2EB0CEA5039A", 32, 21 )]
	[DataRow( "TANNA___.TTF", "Tannarin AOE", "Regular", 308, 36, 2105, 0, 0, 0, 1995, 1302, 2000, "10EA17E010C03076F8476B67E91249E27235F09C227FFEEDEFB8D36DFDD642AF", "264065175BE6C770EA34D4EFB42523B179B425E92BD297680AB2BD614E7F7608", 32, 21 )]
	[DataRow( "YOUNIA__.TTF", "Young Itch AOE", "Regular", 307, 36, 824, -9, -9, -7, 774, 1486, 1801, "939AE2C9095F54152F5AC2B25C459EE8CF52C477BB1DF83F231DEFA3C2DE7DA1", "2216AB345E4D8D66F6C133DEC0C9592CFC309D979F85A303EE8FCD56BA6917AA", 14, 25 )]
	public void OriginalFontMetricsAndRasterizedGlyphsArePinned( string fileName, string family, string subfamily, int glyphCount, int glyphA,
		int advance, int leftSideBearing, int xMin, int yMin, int xMax, int yMax, int kerningPairs, string fileHash, string rasterHash, int width, int height )
	{
		var library = OriginalLibrary();
		var font = library.Get( fileName );
		Assert.AreEqual( (family, subfamily, glyphCount, kerningPairs), (font.FamilyName, font.SubfamilyName, font.GlyphCount, font.KerningPairCount) );
		Assert.AreEqual( fileHash, Convert.ToHexString( SHA256.HashData( library.GetFileData( fileName ) ) ) );
		Assert.AreEqual( glyphA, font.GetGlyphIndex( 'A' ) );
		var metrics = font.GetMetrics( glyphA );
		Assert.AreEqual( (advance, leftSideBearing, xMin, yMin, xMax, yMax), (metrics.AdvanceWidth, metrics.LeftSideBearing, metrics.XMin, metrics.YMin, metrics.XMax, metrics.YMax) );
		var bitmap = TrueTypeRasterizer.RasterizeGlyph( font, glyphA, font.ScaleForEmHeight( 32 ) );
		Assert.AreEqual( (width, height), (bitmap.Width, bitmap.Height) );
		Assert.AreEqual( rasterHash, bitmap.ComputeHash() );

		// Optional cross-check against the loose files the original setup unpacks (tpwfnt).
		var looseDirectory = Environment.GetEnvironmentVariable( "OPENTPW_TPWFNT_PATH" );
		if ( !string.IsNullOrWhiteSpace( looseDirectory ) && File.Exists( Path.Combine( looseDirectory, fileName ) ) )
			Assert.AreEqual( fileHash, Convert.ToHexString( SHA256.HashData( File.ReadAllBytes( Path.Combine( looseDirectory, fileName ) ) ) ), "fonts.wad member matches the loose tpwfnt file" );
	}

	[TestMethod]
	public void OriginalGateNameLineIsPinned()
	{
		var bitmap = SignTextLayout.Rasterize( OriginalLibrary().Get( "Young Itch AOE" ), "Lost Kingdom", 144 );
		Assert.AreEqual( (524, 148, 3, 108), (bitmap.Width, bitmap.Height, bitmap.OriginX, bitmap.OriginY) );
		Assert.AreEqual( "9026F97BD4D60E6E95AA9AD1B9E84754A19A9AB731EF367DD8933C67F421D9C4", bitmap.ComputeHash() );
	}
}
