using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class RideAssetTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game root or data directory for CPU ride asset tests." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var rideDirectory = Path.Combine( dataPath, "levels", "jungle", "rides" );
		if ( !Directory.Exists( rideDirectory ) || !Directory.EnumerateFiles( rideDirectory ).Any( path => string.Equals( Path.GetFileName( path ), "totem.wad", StringComparison.OrdinalIgnoreCase ) ) )
			Assert.Inconclusive( "Original Jungle Totem ride assets are not installed." );

		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}

	[TestMethod]
	public void OriginalTotemMeshBuffersConvertWithoutCreatingGpuResources()
	{
		var model = new ModelFile( $"{PrototypeRide.ArchivePath}/totem.MD2" );
		Assert.IsTrue( model.Meshes.Count > 1 );
		Assert.AreEqual( 1, model.Meshes.Count( mesh => PrototypeRide.IsCarriage( mesh.Name ) ) );
		Assert.IsTrue( model.Meshes.Any( mesh => mesh.Name.TrimEnd( '\0' ) == "tp_pole" ) );
		foreach ( var mesh in model.Meshes )
		{
			var vertices = PrototypeRide.ConvertMesh( mesh );
			Assert.AreEqual( mesh.Vertices.Length, vertices.Length );
			Assert.IsTrue( mesh.Indices.All( index => index < vertices.Length ) );
			Assert.AreEqual( mesh.Vertices[0].Position.X, vertices[0].Position.X );
			Assert.AreEqual( mesh.Vertices[0].Position.Y, vertices[0].Position.Z );
			Assert.AreEqual( mesh.Vertices[0].Position.Z, vertices[0].Position.Y );
			Assert.AreEqual( mesh.Normals[0].Y, vertices[0].Normal.Z );
			Assert.AreEqual( mesh.Normals[0].Z, vertices[0].Normal.Y );
		}
	}

	[TestMethod]
	public void EveryReferencedOriginalTextureResolvesAndDecodesOnCpu()
	{
		var model = new ModelFile( $"{PrototypeRide.ArchivePath}/totem.MD2" );
		foreach ( var name in model.Meshes.SelectMany( mesh => mesh.Materials ).Select( material => material.Name ).Distinct( StringComparer.OrdinalIgnoreCase ) )
		{
			var path = PrototypeRide.ResolveTexturePath( FileSystem, name );
			using var stream = FileSystem.OpenRead( path );
			Assert.IsNotNull( stream, $"Original texture is missing: {path}" );
			var texture = new TextureFile( stream ).Data;
			Assert.IsTrue( texture.Width > 0 && texture.Height > 0, path );
			Assert.AreEqual( texture.Width * texture.Height * 4, texture.Data.Length, path );
		}
	}

	[TestMethod]
	public void TotemSettingsAndSharedTextureMappingIdentifyTheOriginalRide()
	{
		var settings = new SettingsFile( $"{PrototypeRide.ArchivePath}/Totem.sam" );
		Assert.AreEqual( "1110", settings["Info.Id"] );
		Assert.AreEqual( "12", settings["UsageInfo.MaxCapacity"] );
		StringAssert.Contains( PrototypeRide.ResolveTexturePath( FileSystem, "tp_cart" ).Replace( '\\', '/' ), "/rides/totem/textures/" );
		StringAssert.Contains( PrototypeRide.ResolveTexturePath( FileSystem, "jt_f1" ).Replace( '\\', '/' ), "/jungle/sharetex/" );
	}
}

[TestClass]
public class RideMeshConversionTests
{
	private static ModelFile.Mesh CreateMesh()
	{
		return new ModelFile.Mesh
		{
			Name = "tp_cart\0",
			Vertices = new[]
			{
				new ModelFile.Vertex { Position = new Vector3( 1, 2, 3 ), TextureIndex = 0 },
				new ModelFile.Vertex { Position = new Vector3( 4, 5, 6 ), TextureIndex = 0 },
				new ModelFile.Vertex { Position = new Vector3( 7, 8, 9 ), TextureIndex = 0 }
			},
			Indices = new uint[] { 0, 1, 2 },
			Normals = new[] { Vector3.Up, Vector3.Up, Vector3.Up },
			TexCoords = new[] { new Vector2( 0, 0 ), new Vector2( 1, 0 ), new Vector2( 0, 1 ) },
			Materials = new[] { new ModelFile.MaterialData { Name = "tp_cart", Flags = 7 } }
		};
	}

	[TestMethod]
	public void ConvertsOriginalAxesNormalsAndMaterialFlagsWithoutGpu()
	{
		var mesh = CreateMesh();
		var vertices = PrototypeRide.ConvertMesh( mesh );
		Assert.AreEqual( new Vector3( 1, 3, 2 ), vertices[0].Position );
		Assert.AreEqual( new Vector3( 0, 1, 0 ), vertices[0].Normal );
		Assert.AreEqual( mesh.TexCoords[1], vertices[1].TexCoords );
		Assert.AreEqual( 0, vertices[1].TexIndex );
		Assert.AreEqual( 7u, vertices[1].MatFlags );
		Assert.IsTrue( PrototypeRide.IsCarriage( "TP_CART\0" ) );
		Assert.IsFalse( PrototypeRide.IsCarriage( "tp_pole\0" ) );
		Assert.IsFalse( PrototypeRide.IsCarriage( "tp_carttop" ) );
	}

	[TestMethod]
	public void InvalidBuffersAndMaterialIndicesAreRejected()
	{
		var mesh = CreateMesh();
		mesh.Indices[2] = 3;
		Assert.ThrowsException<InvalidDataException>( () => PrototypeRide.ConvertMesh( mesh ) );
		mesh = CreateMesh();
		mesh.Normals = Array.Empty<Vector3>();
		Assert.ThrowsException<InvalidDataException>( () => PrototypeRide.ConvertMesh( mesh ) );
		mesh = CreateMesh();
		mesh.TexCoords = Array.Empty<Vector2>();
		Assert.ThrowsException<InvalidDataException>( () => PrototypeRide.ConvertMesh( mesh ) );
		mesh = CreateMesh();
		mesh.Vertices[0] = new ModelFile.Vertex { Position = Vector3.Zero, TextureIndex = 1 };
		Assert.ThrowsException<InvalidDataException>( () => PrototypeRide.ConvertMesh( mesh ) );
		mesh = CreateMesh();
		mesh.Vertices[0] = new ModelFile.Vertex { Position = new Vector3( float.NaN, 0, 0 ), TextureIndex = 0 };
		Assert.ThrowsException<InvalidDataException>( () => PrototypeRide.ConvertMesh( mesh ) );
	}

	[TestMethod]
	public void RenderTransformSwapsAxesAroundTheModelSpaceNodeMatrix()
	{
		var position = new Vector3( 10, 20, 1 );
		// Converted vertex (renderer axes) of model-space point (1, 2, 3) is (1, 3, 2).
		var node = System.Numerics.Matrix4x4.CreateRotationY( MathF.PI / 2 ) * System.Numerics.Matrix4x4.CreateTranslation( 15, -2, 20 );
		var expectedModel = System.Numerics.Vector3.Transform( new System.Numerics.Vector3( 1, 2, 3 ), node );
		var actual = System.Numerics.Vector3.Transform( new System.Numerics.Vector3( 1, 3, 2 ), PrototypeRide.ToRenderTransform( node, position ) );
		var expected = (new System.Numerics.Vector3( expectedModel.X, expectedModel.Z, expectedModel.Y ) - new System.Numerics.Vector3( 15, 20, 0 )) * PrototypeRide.ModelScale
			+ new System.Numerics.Vector3( 10, 20, 1 );
		Assert.AreEqual( 0, System.Numerics.Vector3.Distance( expected, actual ), 1e-5 );
	}
}
