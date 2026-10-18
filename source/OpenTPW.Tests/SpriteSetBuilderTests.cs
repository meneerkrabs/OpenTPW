using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The sprite set authoring API: no original data needed.</summary>
[TestClass]
[DoNotParallelize]
public class SpriteSetBuilderTests
{
	/// <summary>A small frame: a solid colour with a one-pixel transparent border and a half-transparent pixel.</summary>
	private static SpriteRgbaFrame Frame( int width, int height, byte r, byte g, byte b, int originX, int originY )
	{
		var rgba = new byte[width * height * 4];
		for ( var y = 1; y < height - 1; y++ )
			for ( var x = 1; x < width - 1; x++ )
			{
				var i = (y * width + x) * 4;
				rgba[i] = (byte)(r + x);
				rgba[i + 1] = (byte)(g + y);
				rgba[i + 2] = b;
				rgba[i + 3] = x == 1 && y == 1 ? (byte)128 : (byte)255;
			}
		return new SpriteRgbaFrame( width, height, rgba, originX, originY );
	}

	private static List<List<SpriteRgbaFrame>> TwoByTwo() => new()
	{
		new() { Frame( 6, 5, 10, 20, 30, -3, -5 ), Frame( 7, 5, 40, 50, 60, -3, -5 ) },
		new() { Frame( 6, 6, 70, 80, 90, -3, -6 ), Frame( 5, 6, 100, 110, 120, -2, -6 ) },
	};

	[TestMethod]
	public void TwoDirectionsByTwoFramesReadBackThroughTheReaders()
	{
		var directions = TwoByTwo();
		var set = new SpriteSetBuilder( "spr_zz" ) { KidFlag = 1 }
			.AddAnimation( 0, new[] { new[] { directions[0][0] } } )
			.AddAnimation( 3, directions.Select( x => (IReadOnlyList<SpriteRgbaFrame>)x ).ToArray() )
			.SetPerformance( 0, 0, 3, 2, 40 )
			.Build();
		CollectionAssert.AreEquivalent( new[] { ".ESP", ".FPC", ".TPC" }, set.Files.Keys.ToArray() );

		var esp = new SpriteAnimationFile( new MemoryStream( set.Files[".ESP"] ) );
		Assert.AreEqual( "SPR_ZZ.TPS", esp.BankName );
		Assert.AreEqual( ((byte)1, (byte)1), (esp.FlagA, esp.FlagB) );
		Assert.AreEqual( (".TPC", ".FPC"), (esp.BankExtension, esp.FullDetailBankExtension), "a full-detail set ships an FPC too; the game draws the TPC" );
		Assert.AreEqual( new SpriteAnimation( 0, 0, 1, 1 ), esp.Animations[0] );
		Assert.AreEqual( new SpriteAnimation( 3, 1, 2, 2 ), esp.Animations[3] );
		Assert.IsTrue( esp.Animations.Where( x => x.Slot is not (0 or 3) ).All( x => x.IsEmpty ) );
		Assert.AreEqual( 1, esp.PerformanceCount );
		Assert.AreEqual( new SpritePerformance( 0, 3, 2, 40 ), esp.Performances[0] );
		Assert.AreEqual( 350, set.Files[".ESP"].Length );

		var bank = new SpriteBankFile( new MemoryStream( set.Files[".FPC"] ) );
		Assert.AreEqual( 5, bank.Frames.Count );
		var expected = new[] { directions[0][0], directions[0][0], directions[0][1], directions[1][0], directions[1][1] };
		for ( var index = 0; index < expected.Length; index++ )
		{
			var frame = bank.Frames[index];
			var source = expected[index];
			Assert.AreEqual( (source.Width, source.Height, source.OriginX, source.OriginY), (frame.Width, frame.Height, frame.OriginX, frame.OriginY) );
			Assert.AreEqual( (128, 128), (frame.PixelsPerUnitX, frame.PixelsPerUnitY) );
			var decoded = new byte[frame.Width * frame.Height * 4];
			bank.DecodeRgba( frame, decoded, frame.Width * 4, 0, 0 );
			CollectionAssert.AreEqual( source.Rgba, decoded, $"frame {index} pixels" );
		}
		CollectionAssert.AreEqual( set.Files[".FPC"], set.Files[".TPC"] );
		// Roundtrip of what was just written.
		CollectionAssert.AreEqual( set.Files[".FPC"], SpriteBankWriter.Write( bank ) );
		CollectionAssert.AreEqual( set.Files[".ESP"], SpriteAnimationWriter.Write( esp ) );
	}

	[TestMethod]
	public void TheSetLoadsThroughTheGuestAtlasLikeAShippedKidSet()
	{
		var directions = TwoByTwo();
		var set = new SpriteSetBuilder( "SPR_ZZ" )
			.AddAnimation( 0, new[] { new[] { directions[0][0] } } )
			.AddAnimation( 1, directions.Select( x => (IReadOnlyList<SpriteRgbaFrame>)x ).ToArray() )
			.Build();
		var folder = Path.Combine( Path.GetTempPath(), "opentpw-spriteset-" + Guid.NewGuid().ToString( "N" ) );
		var previous = FileSystem;
		try
		{
			set.WriteTo( Path.Combine( folder, "Kids" ) );
			FileSystem = new BaseFileSystem( folder );
			// A sub-folder like the game's Generic/Kids: LoadSets joins "<directory>/<file>", and "//file" is not a valid path on Windows.
			var atlases = GuestSpriteAtlas.LoadSets( "/Kids", "kids" );
			Assert.AreEqual( 1, atlases.Count );
			var atlas = atlases[0];
			Assert.AreEqual( "SPR_ZZ", atlas.Name );
			Assert.AreEqual( 5, atlas.Frames.Length );
			Assert.AreEqual( 2, atlas.FramesPerDirection( GuestSpriteAtlas.WalkSlot ) );
			Assert.AreEqual( 1, atlas.FramesPerDirection( GuestSpriteAtlas.StandSlot ) );
			// Direction 1, step 1 of the walk slot is the last frame; step 2 wraps to step 0.
			Assert.AreEqual( 4, atlas.GetFrame( GuestSpriteAtlas.WalkSlot, 1, 1 ) );
			Assert.AreEqual( 3, atlas.GetFrame( GuestSpriteAtlas.WalkSlot, 1, 2 ) );
			// An empty slot falls back to standing.
			Assert.AreEqual( 0, atlas.GetFrame( GuestSpriteAtlas.RunSlot, 0, 0 ) );
			// A pixel of the last frame is in the atlas at its slot rectangle.
			var frame = atlas.Frames[4];
			var source = directions[1][1];
			var x = 2;
			var y = 2;
			var from = (y * source.Width + x) * 4;
			var to = ((frame.Y + y) * atlas.Width + frame.X + x) * 4;
			CollectionAssert.AreEqual( source.Rgba.AsSpan( from, 4 ).ToArray(), atlas.Pixels.AsSpan( to, 4 ).ToArray() );
		}
		finally
		{
			FileSystem = previous;
			if ( Directory.Exists( folder ) )
				Directory.Delete( folder, true );
		}
	}

	[TestMethod]
	public void MoreThan255ColoursAreReducedToTheNearestPaletteEntry()
	{
		var rgba = new byte[32 * 32 * 4];
		for ( var i = 0; i < 32 * 32; i++ )
		{
			rgba[i * 4] = (byte)(i * 7);
			rgba[i * 4 + 1] = (byte)(i * 3);
			rgba[i * 4 + 2] = (byte)(i / 4 * 5);
			rgba[i * 4 + 3] = 255;
		}
		var set = new SpriteSetBuilder( "SPR_QQ" ) { FullDetail = false }
			.AddAnimation( 0, new[] { new[] { new SpriteRgbaFrame( 32, 32, rgba, 0, 0 ) } } )
			.Build();
		CollectionAssert.AreEquivalent( new[] { ".ESP", ".TPC" }, set.Files.Keys.ToArray() );
		Assert.AreEqual( ".TPC", new SpriteAnimationFile( new MemoryStream( set.Files[".ESP"] ) ).BankExtension );
		var bank = new SpriteBankFile( new MemoryStream( set.Files[".TPC"] ) );
		var decoded = new byte[rgba.Length];
		bank.DecodeRgba( bank.Frames[0], decoded, 32 * 4, 0, 0 );
		var worst = 0;
		for ( var i = 0; i < rgba.Length; i += 4 )
			worst = Math.Max( worst, Enumerable.Range( 0, 3 ).Max( c => Math.Abs( rgba[i + c] - decoded[i + c] ) ) );
		Assert.IsTrue( worst <= 40, $"worst channel error {worst}" );
		Assert.IsTrue( decoded.Where( ( _, i ) => i % 4 == 3 ).All( a => a == 255 ) );
	}

	[TestMethod]
	public void RowsTooWideForTheLengthByteAreRejected()
	{
		// Alternating pixels never repeat: 1000 literals need 1000 + 8 bytes, over the 255 a row can hold.
		var rgba = new byte[1000 * 4];
		for ( var i = 0; i < 1000; i++ )
		{
			rgba[i * 4] = (byte)(i % 2 * 200);
			rgba[i * 4 + 3] = 255;
		}
		var builder = new SpriteSetBuilder( "SPR_WW" ).AddAnimation( 0, new[] { new[] { new SpriteRgbaFrame( 1000, 1, rgba, 0, 0 ) } } );
		Assert.ThrowsException<InvalidDataException>( () => builder.Build() );
	}

	/// <summary>Encodes a one-row frame, checks it reads back and returns the row's encoded length.</summary>
	private static int RoundtripRow( byte[] row )
	{
		var frame = new SpriteFrame( row.Length, 1, 0, 0, 128, 128, row );
		var bytes = SpriteBankWriter.Write( new byte[SpriteBankFile.PaletteEntries * 4], new[] { frame } );
		var bank = new SpriteBankFile( new MemoryStream( bytes ) );
		CollectionAssert.AreEqual( row, bank.Frames[0].Indices );
		return bytes[SpriteBankFile.HeaderBytes + SpriteBankFile.PaletteEntries * 4 + SpriteBankFile.FrameHeaderBytes];
	}

	[TestMethod]
	public void ARepeatOf128PlusAlternatingPixelsFitsInExactly255Bytes()
	{
		var row = new byte[128 + 251];
		Array.Fill( row, (byte)1, 0, 128 );
		for ( var i = 128; i < row.Length; i++ )
			row[i] = (byte)(2 + i % 2);
		Assert.AreEqual( 255, RoundtripRow( row ) );
	}

	[TestMethod]
	public void GreedyTwoRunsThatOverflowFallBackToLiterals()
	{
		var row = new byte[192];
		for ( var i = 0; i < 64; i++ )
		{
			row[i * 3] = 1;
			row[i * 3 + 1] = 2;
			row[i * 3 + 2] = 2;
		}
		Assert.IsTrue( RoundtripRow( row ) <= 255 );
	}

	[TestMethod]
	public void ARowWithNoEncodingUnder256BytesIsRejected()
	{
		var row = new byte[254];
		for ( var i = 0; i < row.Length; i++ )
			row[i] = (byte)(i % 2);
		// 254 alternating pixels: 2 literal controls + 254 bytes + row length = 256 content bytes.
		Assert.ThrowsException<InvalidDataException>( () => SpriteBankWriter.EncodeRows( row, row.Length, 1 ) );
	}

	[TestMethod]
	public void ShippedStyleRowsKeepTheGreedyEncoding()
	{
		// A run of two is a repeat: 1 1 2 -> R2:1 L1:2.
		var encoded = SpriteBankWriter.EncodeRows( new byte[] { 1, 1, 2 }, 3, 1 );
		CollectionAssert.AreEqual( new byte[] { 4, 0xFE, 1, 1, 2 }, encoded );
	}

	[TestMethod]
	public void PerformanceEntriesAreContiguousAndPlayPopulatedSlots()
	{
		var frame = Frame( 4, 4, 1, 2, 3, 0, 0 );
		var builder = new SpriteSetBuilder( "SPR_PP" ).AddAnimation( 4, new[] { new[] { frame } } );
		Assert.ThrowsException<ArgumentException>( () => builder.SetPerformance( 1, 0, 4 ) );
		Assert.ThrowsException<ArgumentException>( () => builder.SetPerformance( 0, 0, 16 ) );
		Assert.ThrowsException<ArgumentException>( () => builder.SetPerformance( 0, 0, 5 ) );
		builder.SetPerformance( 0, 0, 4 );
		Assert.ThrowsException<ArgumentException>( () => builder.SetPerformance( 0, 0, 4 ) );
	}

	[TestMethod]
	public void AFailedAddAnimationChangesNothingAndWideFramesAreRejected()
	{
		var good = Frame( 4, 4, 1, 2, 3, 0, 0 );
		var bad = new SpriteRgbaFrame( 4, 4, new byte[3], 0, 0 );
		var builder = new SpriteSetBuilder( "SPR_AA" );
		Assert.ThrowsException<ArgumentException>( () => builder.AddAnimation( 0, new[] { new[] { good, bad } } ) );
		Assert.AreEqual( 0, builder.FrameCount );
		builder.AddAnimation( 0, new[] { new[] { good } } );
		var wide = new SpriteRgbaFrame( 1021, 1, new byte[1021 * 4], 0, 0 );
		Assert.ThrowsException<ArgumentException>( () => builder.AddAnimation( 1, new[] { new[] { wide } } ) );
		Assert.AreEqual( 1, builder.FrameCount );
		Assert.ThrowsException<ArgumentException>( () => SpriteBankWriter.Write( new byte[SpriteBankFile.PaletteEntries * 4], new[] { new SpriteFrame( -1, -1, 0, 0, 128, 128, new byte[1] ) } ) );
	}
}
