using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>The sorts tp.exe leaves UI records and triangles in (ORACLE.md X70, UI-004): equal keys, because every UI model vertex is at depth 0.</summary>
[TestClass]
public class UiDepthSortTests
{
	[TestMethod]
	public void TieOrdersAreTheOriginalsPermutationOfEqualKeys()
	{
		for ( var count = 1; count <= UiDepthSortGolden.TieOrders.Length; count++ )
			CollectionAssert.AreEqual( UiDepthSortGolden.TieOrders[count - 1], OriginalDepthSort.TieOrder( count ), $"{count} ties" );
		Assert.AreSame( OriginalDepthSort.TieOrder( 12 ), OriginalDepthSort.TieOrder( 12 ), "cached: a draw allocates nothing" );
	}

	[TestMethod]
	public void SortMatchesTheOriginalOnTiedAndRandomKeys()
	{
		foreach ( var (keys, expected) in UiDepthSortGolden.Cases )
		{
			var floats = keys.Select( bits => BitConverter.UInt32BitsToSingle( bits ) ).ToArray();
			var order = Enumerable.Range( 0, floats.Length ).ToArray();
			OriginalDepthSort.Sort( floats, order );
			CollectionAssert.AreEqual( expected, order );
		}
	}

	[TestMethod]
	public void TheFaceTestMatchesTheOriginalOnRandomIntegerAndNearCollinearTriangles()
	{
		foreach ( var (points, expected) in UiDepthSortGolden.FaceTests )
		{
			var p = points.Select( bits => BitConverter.UInt32BitsToSingle( bits ) ).ToArray();
			Assert.AreEqual( expected, UiModel.IsClockwise( new NVector2( p[0], p[1] ), new NVector2( p[2], p[3] ), new NVector2( p[4], p[5] ) ) );
		}
	}

	[TestMethod]
	public void ATranslucentTextureDrawsItsTrianglesInTheTieOrderAndAnOpaqueOneInFileOrder()
	{
		var directory = Path.Combine( Path.GetTempPath(), "opentpw-ui-sort-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory );
		var saved = FileSystem;
		try
		{
			File.WriteAllBytes( Path.Combine( directory, "glass.wct" ), new byte[] { 0x13, 0x01, 0x20, 0x00, 0, 0, 0, 0 } );
			File.WriteAllBytes( Path.Combine( directory, "solid.wct" ), new byte[] { 0x13, 0x00, 0x18, 0x00, 0, 0, 0, 0 } );
			FileSystem = new BaseFileSystem( directory );
			UiImages.ResetListings();
			var vertices = Enumerable.Range( 0, 12 ).Select( index => new UiModelVertex( new NVector2( (index / 3) * 10 + (index % 3 == 1 ? 4 : 0), index % 3 == 2 ? 4 : 0 ), 0, NVector2.Zero ) ).ToArray();
			var indices = Enumerable.Range( 0, 12 ).ToArray();
			var frame = new UiModelFrame( "x", new[] { new UiModelPart( "glass", vertices, indices ), new UiModelPart( "solid", vertices, indices ) }, 0, 0, 12, 3 );
			var batch = new UiBatch();
			batch.AddModelFrame( frame, NVector2.Zero, NVector2.Zero, 1, name => "/" + name + ".wct" );
			var drawn = batch.Draws.SelectMany( draw => draw.Vertices ).Select( vertex => (int)vertex.Position.X ).ToArray();
			var order = OriginalDepthSort.TieOrder( 4 );
			int[] X( int triangle ) => new[] { triangle * 10, triangle * 10 + 4, triangle * 10 };
			CollectionAssert.AreEqual( order.SelectMany( X ).Concat( Enumerable.Range( 0, 4 ).SelectMany( X ) ).ToArray(), drawn );
		}
		finally
		{
			FileSystem = saved;
			UiImages.ResetListings();
			Directory.Delete( directory, true );
		}
	}
}
