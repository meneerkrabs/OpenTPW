using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The track symbol faces of the edit tool's ghost (TRACK-018; tp.exe <c>0x5370E0</c>, <c>0x53C3F0</c>, <c>0x53DF30</c>): one 20 unit face over the tile, its texture coordinates the ±0.4 square turned by the camera yaw.</summary>
[TestClass]
public class TrackGhostSymbolTests
{
	private static ModelHeightfield Field( float[]? heights = null ) => new( 4, 4, 10, 10, 0, 0, Array.Empty<uint>(), heights ?? new float[25], new uint[16] );

	[TestMethod]
	public void ASymbolIsOneTwentyUnitFaceWithTheSquareOfTheRotatingTextureSet()
	{
		var (vertices, indices) = BlueprintMarkers.BuildGeometry( Field(), new[] { (1, 1, BlueprintMarker.Erase) }, null, 0 );
		Assert.AreEqual( 8, vertices.Length, "one face, both sides" );
		Assert.AreEqual( 12, indices.Length );
		Assert.IsTrue( vertices.All( vertex => vertex.TexIndex == 13 ), "m_erase is texture 13" );
		var front = vertices.Take( 4 ).ToArray();
		var uv = front.Select( vertex => vertex.TexCoords ).ToArray();
		// the entries 5 to 8 at yaw 0: (-0.4, -0.4), (0.4, -0.4), (-0.4, 0.4), (0.4, 0.4), each moved by +0.5
		Assert.AreEqual( 0.1f, uv[0].X, 1e-5f );
		Assert.AreEqual( 0.1f, uv[0].Y, 1e-5f );
		Assert.AreEqual( 0.9f, uv[1].X, 1e-5f );
		Assert.AreEqual( 0.9f, uv[3].Y, 1e-5f );
		var span = front.Max( vertex => vertex.Position.X ) - front.Min( vertex => vertex.Position.X );
		Assert.AreEqual( 20 * OriginalParkPlacement.ModelScale, span, 1e-3f, "the face spans the tile's two cells" );
	}

	[TestMethod]
	public void TheCameraYawTurnsTheSymbolWithTheSineTable()
	{
		var (vertices, _) = BlueprintMarkers.BuildGeometry( Field(), new[] { (0, 0, BlueprintMarker.Break) }, null, 0, null, MathF.PI / 2 );
		Assert.AreEqual( 9, vertices[0].TexIndex );
		var uv = vertices.Take( 4 ).Select( vertex => vertex.TexCoords ).ToArray();
		// a quarter turn: x' = cos x - sin y, y' = sin x + cos y with sin 1 and cos 0 at the table's resolution
		Assert.AreEqual( 0.5f + 0.4f, uv[0].X, 1e-4f, "(-0.4, -0.4) goes to (0.4, -0.4)" );
		Assert.AreEqual( 0.5f - 0.4f, uv[0].Y, 1e-4f );
		Assert.AreEqual( 0.5f - 0.4f, uv[3].X, 1e-4f, "(0.4, 0.4) goes to (-0.4, 0.4)" );
		Assert.AreEqual( 0.5f + 0.4f, uv[3].Y, 1e-4f );
	}

	[TestMethod]
	public void TheFourCornersTakeTheTerrainPlusTheLiftAndTheRipple()
	{
		var heights = new float[25];
		for ( var index = 0; index < heights.Length; index++ )
			heights[index] = index * 2f;
		var field = Field( heights );
		var (vertices, _) = BlueprintMarkers.BuildGeometry( field, new[] { (1, 1, BlueprintMarker.Erase) }, null, 0.5f );
		var origin = OriginalParkPlacement.GetOrigin( field );
		foreach ( var (cx, cy) in new[] { (1, 1), (3, 1), (1, 3), (3, 3) } )
		{
			var expected = field.GetCornerHeight( cx, cy ) + BlueprintMarkers.Lift + BlueprintMarkers.Ripple( 0.5f, cx * 10, cy * 10 );
			var engineX = cx * 10 * OriginalParkPlacement.ModelScale - origin.X;
			Assert.IsTrue( vertices.Any( vertex => MathF.Abs( vertex.Position.X - engineX ) < 1e-4f && MathF.Abs( vertex.Position.Z - expected * OriginalParkPlacement.ModelScale ) < 1e-3f ),
				$"corner ({cx}, {cy}) of the 2 x 2 tile" );
		}
	}
}
