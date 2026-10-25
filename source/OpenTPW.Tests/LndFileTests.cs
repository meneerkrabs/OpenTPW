using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic tests of the <c>base.lnd</c> reader (docs/LND.md); the shipped files are in <see cref="ProceduralTerrainCorpusTests"/>.</summary>
[TestClass]
public class LndFileTests
{
	private static readonly byte[] Parameters = [48, 40, 40, 90, 68, 40, 97, 30, 55, 50];

	private static byte Grid( int x, int y, int channel ) => (byte)(x * 7 + y * 13 + channel * 3);

	private static byte Pixel( int size, int image, int x, int y, int channel ) => (byte)(size + image * 5 + x * 3 + y * 11 + channel);

	private static byte[] Build( int trailing = 0 ) => SyntheticLnd.Build( 3, 2, Parameters, Grid, Pixel, trailingBytes: trailing );

	private static LndFile Parse( byte[] data ) => new( new MemoryStream( data ) );

	[TestMethod]
	public void ReadsTheLayoutOfASyntheticFile()
	{
		var lnd = Parse( Build() );
		Assert.AreEqual( 3, lnd.Version );
		CollectionAssert.AreEqual( Parameters, lnd.Parameters.ToArray() );
		Assert.AreEqual( new LndRamp( 48, 40 ), lnd.LayerA );
		Assert.AreEqual( new LndRamp( 40, 90 ), lnd.LayerB );
		Assert.AreEqual( new LndRamp( 68, 40 ), lnd.Mask2 );
		Assert.AreEqual( new LndRamp( 97, 30 ), lnd.LayerC );
		Assert.AreEqual( new LndRamp( 55, 50 ), lnd.Mask1 );
		Assert.AreEqual( 13, lnd.GridWidth );
		Assert.AreEqual( 9, lnd.GridHeight );
		Assert.AreEqual( 3, lnd.CellsX );
		Assert.AreEqual( 2, lnd.CellsY );
		Assert.AreEqual( Grid( 5, 6, 2 ), lnd.GridSample( 5, 6, 2 ) );
		Assert.AreEqual( 3, lnd.Sets.Count );
		for ( var set = 0; set < 3; set++ )
		{
			var size = 64 >> set;
			Assert.AreEqual( size, lnd.Sets[set].TextureSize );
			Assert.AreEqual( set, lnd.Sets[set].Index );
			Assert.AreSame( lnd.Sets[set], lnd.SetFor( size ) );
			Assert.AreEqual( 13, lnd.Sets[set].Images.Count );
			for ( var image = 0; image < 13; image++ )
			{
				var item = lnd.Sets[set].Images[image];
				Assert.AreEqual( size + 8, item.Width );
				Assert.AreEqual( size + 6, item.Height );
				Assert.AreEqual( image < 6 ? 4 : 1, item.BytesPerPixel );
				Assert.AreEqual( item.Width * item.Height * item.BytesPerPixel, item.Pixels.Length );
				var bytes = item.BytesPerPixel;
				Assert.AreEqual( Pixel( size, image, 4, 3, bytes - 1 ), item.Pixels.Span[(3 * item.Width + 4) * bytes + bytes - 1] );
			}
		}
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => lnd.SetFor( 128 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => lnd.GridSample( 13, 0, 0 ) );
	}

	[TestMethod]
	public void RefusesAnyDeviationFromTheLayout()
	{
		var good = Build();
		Parse( good );
		Assert.ThrowsException<InvalidDataException>( () => Parse( Build( trailing: 1 ) ), "the parse must end exactly at the end of the file" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( good[..^1] ), "a file one byte short" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( good[..20] ), "a truncated weight grid" );
		Assert.ThrowsException<InvalidDataException>( () => Parse( [] ), "an empty file" );
		var version = (byte[])good.Clone();
		version[0] = 2;
		Assert.ThrowsException<InvalidDataException>( () => Parse( version ), "a version other than 3" );
		var depth = (byte[])good.Clone();
		depth[11 + 8] = 3;
		Assert.ThrowsException<InvalidDataException>( () => Parse( depth ), "a weight grid that is not four bytes per sample" );
		var tiny = (byte[])good.Clone();
		tiny[11] = 4;
		Assert.ThrowsException<InvalidDataException>( () => Parse( tiny ), "a weight grid under one cell wide" );
		var huge = (byte[])good.Clone();
		huge[11 + 3] = 0x80;
		Assert.ThrowsException<InvalidDataException>( () => Parse( huge ), "a size field over int.MaxValue" );
		var setHeader = 11 + 12 + 13 * 9 * 4;
		var order = (byte[])good.Clone();
		order[setHeader] = 32;
		Assert.ThrowsException<InvalidDataException>( () => Parse( order ), "sets out of order" );
		var index = (byte[])good.Clone();
		index[setHeader + 4] = 1;
		Assert.ThrowsException<InvalidDataException>( () => Parse( index ), "a set index that does not match its position" );
		var role = (byte[])good.Clone();
		role[setHeader + 5 + 8] = 1;
		Assert.ThrowsException<InvalidDataException>( () => Parse( role ), "a colour image that is not four bytes per pixel" );
		var small = (byte[])good.Clone();
		small[setHeader + 5] = 64;
		Assert.ThrowsException<InvalidDataException>( () => Parse( small ), "an image no wider than the texture size, so its window has no wrap period" );
		Assert.ThrowsException<ArgumentNullException>( () => new LndFile( (Stream)null! ) );
		Assert.ThrowsException<InvalidDataException>( () => Parse( new byte[LndFile.MaximumFileBytes + 1] ), "a file over the byte limit" );
	}
}
