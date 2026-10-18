using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A WCT whose first header flag bit is clear (0x12, the interface art of ui.wad) stores each row's wavelet coefficients planar: the row's scale
/// samples first, then its wavelet samples. tp.exe's decoder 0x593ab0 reads them so, and so does the sign boards' wavelet decoder, which is pinned
/// against the executable instruction for instruction. The texture decoder read these rows with an invented "half-scale" layout, which ghosted the
/// icons (a doubled ring around the info button, soft white edges); they now decode like the sign boards' planar rows.
/// </summary>
[TestClass]
public class TexturePlanarRowsTests
{
	/// <summary>
	/// Deterministic coefficients of one <paramref name="side"/> x <paramref name="side"/> plane in row-major order: the smooth block (the top-left quarter) around
	/// <paramref name="level"/> so the decoded values stay in range, small wavelet samples elsewhere, and a few values that take the escape (-128 then an int16).
	/// </summary>
	private static byte[] Plane( int side, uint seed, int level )
	{
		using var stream = new MemoryStream();
		var state = seed;
		for ( var row = 0; row < side; row++ )
		for ( var column = 0; column < side; column++ )
		{
			state = state * 1664525u + 1013904223u;
			var noise = (int)((state >> 16) % 17) - 8;
			var value = row < side / 2 && column < side / 2 ? level + noise : noise;
			if ( (row * side + column) % 41 == 0 )
			{
				stream.WriteByte( 0x80 );
				stream.Write( BitConverter.GetBytes( (short)(value * 3) ) );
			}
			else
				stream.WriteByte( (byte)(sbyte)value );
		}
		return stream.ToArray();
	}

	private static byte[] Bilz( byte[] uncompressed )
	{
		using var compressed = new MemoryStream();
		using ( var zlib = new ZLibStream( compressed, CompressionLevel.Optimal, leaveOpen: true ) )
			zlib.Write( uncompressed );
		var chunk = new byte[28 + compressed.Length];
		"BILZ"u8.CopyTo( chunk );
		BinaryPrimitives.WriteUInt32LittleEndian( chunk.AsSpan( 4 ), (uint)uncompressed.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( chunk.AsSpan( 8 ), (uint)chunk.Length );
		compressed.ToArray().CopyTo( chunk, 28 );
		return chunk;
	}

	[TestMethod]
	public void PlanarRowsDecodeLikeTheSignBoardsInstructionExactDecoder()
	{
		foreach ( var side in new[] { 16, 32, 64 } )
		{
			// Planes in file order: Y at side, Cb and Cr at side/2, then the alpha plane in its own chunk.
			var luma = Plane( side, 0xC0DEu + (uint)side, 60 );
			var blue = Plane( side / 2, 0xB10Eu + (uint)side, 4 );
			var red = Plane( side / 2, 0x8ED0u + (uint)side, -4 );
			var color = Bilz( System.Linq.Enumerable.ToArray( System.Linq.Enumerable.Concat( luma, System.Linq.Enumerable.Concat( blue, red ) ) ) );
			var alpha = Bilz( Plane( side, 0xA1FAu + (uint)side, 100 ) );
			// The header's shorts in file order: Y, then the plane multiplied by 1.402 (Cr), then Cb, then alpha.
			short qy = 2, qCr = -3, qCb = 4, qa = 5;
			using var wct = new MemoryStream();
			using ( var writer = new BinaryWriter( wct, System.Text.Encoding.ASCII, leaveOpen: true ) )
			{
				writer.Write( (byte)0x12 );
				writer.Write( (byte)1 );
				writer.Write( (byte)32 );
				writer.Write( (byte)4 );
				writer.Write( (short)side );
				writer.Write( (short)side );
				writer.Write( qy );
				writer.Write( qCr );
				writer.Write( qCb );
				writer.Write( qa );
				writer.Write( color.Length );
				writer.Write( alpha.Length );
				writer.Write( 0 );
				writer.Write( color );
				writer.Write( alpha );
			}
			wct.Position = 0;
			var texture = new TextureFile( wct ).Data;

			// The same chunks as a sign wavelet payload: four float scales in plane order (Y, Cb, Cr, alpha), two lengths, the chunks.
			var payload = new byte[24 + color.Length + alpha.Length];
			BinaryPrimitives.WriteSingleLittleEndian( payload.AsSpan( 0 ), 1f + 0.5f * qy );
			BinaryPrimitives.WriteSingleLittleEndian( payload.AsSpan( 4 ), 1f + 0.25f * qCb );
			BinaryPrimitives.WriteSingleLittleEndian( payload.AsSpan( 8 ), 1f + 0.25f * qCr );
			BinaryPrimitives.WriteSingleLittleEndian( payload.AsSpan( 12 ), qa + 1f );
			BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( 16 ), (uint)color.Length );
			BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( 20 ), (uint)alpha.Length );
			color.CopyTo( payload, 24 );
			alpha.CopyTo( payload, 24 + color.Length );
			var argb = SignWaveletDecoder.Decode( new SignBitmap( side, side, 4, payload ) { IsWavelet = true } );

			Assert.AreEqual( side * side * 4, texture.Data.Length );
			var worst = 0;
			var differing = 0;
			var total = 0L;
			for ( var pixel = 0; pixel < side * side; pixel++ )
			{
				// The sign decoder writes A, R, G, B; the texture decoder R, G, B, A.
				var sign = new[] { argb[pixel * 4 + 1], argb[pixel * 4 + 2], argb[pixel * 4 + 3], argb[pixel * 4] };
				for ( var channel = 0; channel < 4; channel++ )
				{
					var difference = Math.Abs( texture.Data[pixel * 4 + channel] - sign[channel] );
					worst = Math.Max( worst, difference );
					total += difference;
					if ( difference != 0 )
						differing++;
				}
			}
			// The sign decoder rounds colours through the executable's magic-number conversion where this one truncates, and adds the taps in the executable's order;
			// a level or two of rounding is all that may differ (a wrong row layout is off by tens of levels).
			Assert.IsTrue( worst <= 2 && total < side * side * 4, $"{side}x{side}: the planar texture decode is {worst} levels away from the sign boards' decoder ({differing} bytes differ, {total} levels in all)" );
		}
	}

	[TestMethod]
	public void TheInfoButtonKeepsTheBlackOutlineAroundItsLetter()
	{
		var wad = Path.Combine( Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" ) ?? "", "Data", "ui.wad" );
		if ( !File.Exists( wad ) )
		{
			Assert.Inconclusive( "Original game data is not installed." );
			return;
		}
		using var archive = new WadArchive( wad );
		var texture = new TextureFile( new MemoryStream( archive.GetFile( "textures/b_info.wct" ).GetData() ) ).Data;
		// Row 18 crosses the "i" of the info button's 36-texel disc: its white stem (texels 17..21) is walled in by a black outline on either side
		// (texels 16 and 22). The old half-scale decode smeared the outline into the stem's neighbourhood (both texels read about 0x8c..0xa6 bright).
		int Brightest( int x ) => Math.Max( texture.Data[(18 * texture.Width + x) * 4], Math.Max( texture.Data[(18 * texture.Width + x) * 4 + 1], texture.Data[(18 * texture.Width + x) * 4 + 2] ) );
		Assert.IsTrue( Brightest( 16 ) < 64 && Brightest( 22 ) < 64, $"the outline either side of the stem is black, not {Brightest( 16 )} and {Brightest( 22 )}" );
		for ( var x = 17; x <= 21; x++ )
			Assert.IsTrue( Brightest( x ) > 230, $"the stem is white at texel {x}, not {Brightest( x )}" );
	}
}
