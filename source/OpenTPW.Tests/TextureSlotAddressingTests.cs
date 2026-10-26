using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace OpenTPW.Tests;

/// <summary>
/// tp.exe wraps an MD2 texture slot with flag 0x10 or 0x20 and clamps every other slot (0x575E9C, 0x581847, 0x567620);
/// see docs/MD2-MODELS.md, "Texture slots".
/// </summary>
[TestClass]
public class TextureSlotAddressingTests
{
	[TestMethod]
	[DataRow( 0x10u, true )]
	[DataRow( 0x20u, true )]
	[DataRow( 0x30u, true )]
	[DataRow( 0x11u, true )]
	[DataRow( 0x73u, true )]
	[DataRow( 0x0u, false )]
	[DataRow( 0x1u, false )]
	[DataRow( 0x3u, false )]
	[DataRow( 0x41u, false )]
	[DataRow( 0x80u, false )]
	public void EitherWrapBitWrapsAndEveryOtherSlotClamps( uint slotFlags, bool wraps )
	{
		Assert.AreEqual( wraps, TextureSlotAddressing.Wraps( slotFlags ) );
		var vertexFlags = TextureSlotAddressing.VertexFlags( slotFlags );
		Assert.AreEqual( slotFlags, vertexFlags & ~TextureSlotAddressing.ClampFlag, "the slot's own flags are kept" );
		Assert.AreEqual( !wraps, (vertexFlags & TextureSlotAddressing.ClampFlag) != 0 );
	}

	[TestMethod]
	public void TheClampMarkerIsOutsideTheOriginalFlagsAndAWrappingSlotDropsIt()
	{
		Assert.AreEqual( 0u, TextureSlotAddressing.ClampFlag & 0xFFu, "the original slot flags use the low byte only" );
		Assert.AreEqual( 0x30u, TextureSlotAddressing.VertexFlags( 0x30u | TextureSlotAddressing.ClampFlag ) );
	}

	[TestMethod]
	public void EachCornerTakesTheAddressingOfItsOwnSlot()
	{
		var mesh = new ModelFile.Mesh
		{
			Name = "mixed",
			Vertices = new[]
			{
				new ModelFile.Vertex { Position = new Vector3( 0, 0, 0 ), TextureIndex = 0 },
				new ModelFile.Vertex { Position = new Vector3( 1, 0, 0 ), TextureIndex = 0 },
				new ModelFile.Vertex { Position = new Vector3( 0, 1, 0 ), TextureIndex = 0 },
				new ModelFile.Vertex { Position = new Vector3( 0, 0, 1 ), TextureIndex = 1 },
				new ModelFile.Vertex { Position = new Vector3( 1, 0, 1 ), TextureIndex = 1 },
				new ModelFile.Vertex { Position = new Vector3( 0, 1, 1 ), TextureIndex = 1 }
			},
			Indices = new uint[] { 0, 1, 2, 3, 4, 5 },
			Normals = new[] { Vector3.Up, Vector3.Up, Vector3.Up, Vector3.Up, Vector3.Up, Vector3.Up },
			TexCoords = new[] { new Vector2( 0, 0 ), new Vector2( 4, 0 ), new Vector2( 0, 4 ), new Vector2( 0, 0 ), new Vector2( 1, 0 ), new Vector2( 0, 1 ) },
			Materials = new[]
			{
				new ModelFile.MaterialData { Name = "brick", Flags = 0x13 },
				new ModelFile.MaterialData { Name = "sign", Flags = 0x3 }
			}
		};
		var vertices = ObjectAssets.ConvertMesh( mesh );
		for ( var corner = 0; corner < 3; corner++ )
			Assert.AreEqual( 0x13u, vertices[corner].MatFlags, "the tiled slot wraps" );
		for ( var corner = 3; corner < 6; corner++ )
			Assert.AreEqual( 0x3u | TextureSlotAddressing.ClampFlag, vertices[corner].MatFlags, "the other slot clamps" );
	}

	[TestMethod]
	[DataRow( "test.shader" )]
	[DataRow( "advisor.shader" )]
	public void TheObjectShadersClampOnTheSameVertexBit( string shader )
	{
		var source = File.ReadAllText( Path.Combine( RepositoryRoot(), "content", "shaders", shader ) );
		var match = Regex.Match( source, @"const uint FLAG_CLAMP = 0x([0-9A-Fa-f]+)u;" );
		Assert.IsTrue( match.Success, $"{shader} declares FLAG_CLAMP" );
		Assert.AreEqual( TextureSlotAddressing.ClampFlag, uint.Parse( match.Groups[1].Value, NumberStyles.HexNumber ) );
		Assert.AreEqual( 16, Regex.Matches( source, @"vTextureSample = SAMPLE_SLOT\( Color\d+ \);" ).Count, $"{shader} samples every slot through SAMPLE_SLOT" );
	}

	[TestMethod]
	public void TheWorldShaderTestsNeitherAlphaNorBlendsAnOpaqueSlotsFaces()
	{
		// WorldTranslucency.OpaqueSlotFlag: the original draws a slot with the runtime flag 0x800 with state 0x1256 (no alpha test, no blend), so test.shader reads the flag, forces alpha 1 and skips the discard
		var source = File.ReadAllText( Path.Combine( RepositoryRoot(), "content", "shaders", "test.shader" ) );
		var match = Regex.Match( source, @"const uint FLAG_OPAQUE_SLOT = 0x([0-9A-Fa-f]+)u;" );
		Assert.IsTrue( match.Success, "test.shader declares FLAG_OPAQUE_SLOT" );
		Assert.AreEqual( WorldTranslucency.OpaqueSlotFlag, uint.Parse( match.Groups[1].Value, NumberStyles.HexNumber ) );
		Assert.IsTrue( Regex.IsMatch( source, @"if \( \( outMatFlags & FLAG_OPAQUE_SLOT \) != 0u \)\s*vTextureSample\.a = 1\.0;\s*else if \(vTextureSample\.a < alphaReference\) discard;" ), "the flag replaces the alpha test" );
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "source", "OpenTPW.sln" ) ) )
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException( "Repository root not found." );
	}
}
