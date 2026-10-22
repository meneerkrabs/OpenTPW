using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>[EXT:pools-bridges] The buy window's preview of the single pool bridge row: the committed Blender mesh, drawn by the ordinary buy preview.</summary>
[TestClass]
public class PoolBridgePreviewTests
{
	private static string Folder()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var path = Path.Combine( directory.FullName, "content", "pools", "bridge" );
			if ( File.Exists( Path.Combine( path, "bridge.json" ) ) ) return path;
		}
		throw new FileNotFoundException( "The committed pool bridge asset was not found above the test binaries." );
	}

	[TestMethod]
	public void TheBridgeRowHasAPreviewMadeOfItsOwnMeshAndTextures()
	{
		var asset = PoolBridgeAsset.Load( Path.Combine( Folder(), "bridge.json" ) );
		var icon = PoolBridgePreview.Create( Folder() );
		Assert.IsNotNull( icon, "the row's panel is not empty" );
		Assert.AreEqual( asset.Parts.Sum( part => part.Indices.Length / 3 ), icon.TriangleCount );
		Assert.AreEqual( 0, icon.RuntimeTextureTriangleCount );
		var (min, max) = icon.Bounds;
		Assert.AreEqual( PoolBridge.Length * asset.UnitsPerCell, max.X - min.X, 1.5f, "the model is the seven cells long" );
		Assert.IsTrue( max.X - min.X > 3 * (max.Z - min.Z), "long and narrow" );
		Assert.IsTrue( max.Y > min.Y, "it has a deck above the ground" );
	}

	[TestMethod]
	public void ThePreviewTurnsAndFitsTheBuyPreviewFrame()
	{
		var icon = PoolBridgePreview.Create( Folder() )!;
		var frame = new UiRect( 437, 202, 360, 363 );
		foreach ( var yaw in new[] { 0f, 0.8f, 1.6f, 2.4f, 3.2f, 4.8f } )
		{
			var arranged = icon.ArrangeBuyPreview( frame, yaw, 1 );
			var kept = arranged.Triangles.Where( triangle => triangle.FacesViewer ).ToArray();
			Assert.IsTrue( kept.Length > 100, $"yaw {yaw}: the bridge shows" );
			var xs = kept.SelectMany( triangle => new[] { triangle.A.X, triangle.B.X, triangle.C.X } ).ToArray();
			Assert.IsTrue( xs.Min() > frame.X - 40 && xs.Max() < frame.Right + 40, $"yaw {yaw}: the bridge stays in the frame ({xs.Min()}..{xs.Max()})" );
		}
	}

	[TestMethod]
	public void WithoutTheContentTheRowHasNoPreview()
	{
		var empty = Directory.CreateTempSubdirectory().FullName;
		try { Assert.IsNull( PoolBridgePreview.Create( empty ) ); }
		finally { Directory.Delete( empty, true ); }
	}

	[TestMethod]
	public void TheRowIsThePoolBridgeRowAndTheOldDirectionRowsAreGone()
	{
		Assert.IsTrue( ParkHud.IsPoolRow( ParkHud.PoolBridgeRow ) );
		Assert.AreEqual( -21, ParkHud.PoolBridgeRow.InfoId );
		Assert.IsNull( ParkHud.PoolBridgeRow.Entry );
		Assert.IsFalse( typeof( ParkHud ).GetFields().Any( field => field.Name is "PoolBridgeEastRow" or "PoolBridgeSouthRow" ) );
	}
}
