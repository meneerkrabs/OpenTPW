using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The content kit's binary glTF reader (GltfScene) over hand-built files and, when Blender is installed, the sample the Blender scripts export.</summary>
[TestClass]
public class GltfSceneTests
{
	/// <summary>Builds a .glb from a JSON document and a binary chunk.</summary>
	internal static byte[] Glb( string json, byte[] binary )
	{
		var jsonBytes = Encoding.UTF8.GetBytes( json );
		var jsonPadded = (jsonBytes.Length + 3) & ~3;
		var binaryPadded = (binary.Length + 3) & ~3;
		var data = new byte[12 + 8 + jsonPadded + (binary.Length > 0 ? 8 + binaryPadded : 0)];
		BinaryPrimitives.WriteUInt32LittleEndian( data, 0x46546C67 );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 4 ), 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 8 ), (uint)data.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 12 ), (uint)jsonPadded );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 16 ), 0x4E4F534A );
		data.AsSpan( 20, jsonPadded ).Fill( (byte)' ' );
		jsonBytes.CopyTo( data, 20 );
		if ( binary.Length > 0 )
		{
			var offset = 20 + jsonPadded;
			BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( offset ), (uint)binaryPadded );
			BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( offset + 4 ), 0x004E4942 );
			binary.CopyTo( data, offset + 8 );
		}
		return data;
	}

	/// <summary>One textured triangle under a parent node, plus a head empty with extras.</summary>
	internal static byte[] Triangle( string extraNodes = "", string extraJson = "" )
	{
		var binary = new List<byte>();
		void Floats( params float[] values ) { foreach ( var v in values ) binary.AddRange( BitConverter.GetBytes( v ) ); }
		Floats( 0, 0, 0, 10, 0, 0, 0, 0, 10 );      // positions, 36 bytes
		Floats( 0, 1, 0, 0, 1, 0, 0, 1, 0 );        // normals, 36 bytes
		Floats( 0, 0, 1, 0, 0, 1 );                 // uvs, 24 bytes
		binary.AddRange( new byte[] { 0, 0, 1, 0, 2, 0, 0, 0 } ); // u16 indices + padding, 8 bytes
		binary.AddRange( new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' } ); // image stub, 4 bytes
		var json = $$"""
		{ "asset": { "version": "2.0" }, "scene": 0, "scenes": [ { "nodes": [ 0 ] } ],
		  "nodes": [
		    { "name": "root", "children": [ 1, 2 ]{{extraNodes}} },
		    { "name": "part", "mesh": 0, "translation": [ 1, 2, 3 ] },
		    { "name": "head01", "translation": [ 0, 1, 0 ], "extras": { "opentpw_head": 1 } } ],
		  "meshes": [ { "name": "tri", "primitives": [ { "attributes": { "POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2 }, "indices": 3, "material": 0 } ] } ],
		  "materials": [ { "name": "tile", "pbrMetallicRoughness": { "baseColorTexture": { "index": 0 } }, "extras": { "opentpw_wrap": false, "opentpw_self_lit": true } } ],
		  "textures": [ { "source": 0 } ],
		  "images": [ { "name": "tile", "mimeType": "image/png", "bufferView": 4 } ],
		  "accessors": [
		    { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
		    { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC3" },
		    { "bufferView": 2, "componentType": 5126, "count": 3, "type": "VEC2" },
		    { "bufferView": 3, "componentType": 5123, "count": 3, "type": "SCALAR" } ],
		  "bufferViews": [
		    { "buffer": 0, "byteOffset": 0, "byteLength": 36 }, { "buffer": 0, "byteOffset": 36, "byteLength": 36 },
		    { "buffer": 0, "byteOffset": 72, "byteLength": 24 }, { "buffer": 0, "byteOffset": 96, "byteLength": 6 },
		    { "buffer": 0, "byteOffset": 104, "byteLength": 4 } ],
		  "buffers": [ { "byteLength": 108 } ]{{extraJson}} }
		""";
		return Glb( json, binary.ToArray() );
	}

	[TestMethod]
	public void ReadsNodesMeshesMaterialsImagesAndExtras()
	{
		var scene = GltfScene.Read( Triangle() );

		CollectionAssert.AreEqual( new[] { 0 }, scene.Roots.ToArray() );
		Assert.AreEqual( 3, scene.Nodes.Count );
		Assert.AreEqual( 0, scene.Nodes[1].Parent );
		Assert.AreEqual( new NumericsVector3( 1, 2, 3 ), scene.Nodes[1].Local.Translation );
		Assert.AreEqual( 1, scene.Nodes[2].Extras["opentpw_head"].GetInt32() );

		var primitive = scene.Meshes[0].Primitives.Single();
		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, primitive.Indices );
		Assert.AreEqual( new NumericsVector3( 10, 0, 0 ), primitive.Positions[1] );
		Assert.AreEqual( new NumericsVector2( 1, 0 ), primitive.TexCoords![1] );
		Assert.AreEqual( NumericsVector3.UnitY, primitive.Normals![2] );

		var material = scene.Materials.Single();
		Assert.AreEqual( 0, material.Image );
		Assert.IsFalse( material.Extras["opentpw_wrap"].GetBoolean() );
		Assert.IsTrue( material.Extras["opentpw_self_lit"].GetBoolean() );
		Assert.AreEqual( "image/png", scene.Images[0].MimeType );
		Assert.AreEqual( 4, scene.Images[0].Data.Length );
	}

	[TestMethod]
	public void ComposesTranslationRotationAndScaleAsRowVectors()
	{
		var json = """
		{ "asset": { "version": "2.0" }, "nodes": [ { "translation": [ 5, 0, 0 ], "rotation": [ 0, 0.70710677, 0, 0.70710677 ], "scale": [ 2, 2, 2 ] } ] }
		""";
		var local = GltfScene.Read( Glb( json, Array.Empty<byte>() ) ).Nodes[0].Local;
		// Scale, then a quarter turn about +Y (+X goes to -Z), then the translation.
		var point = NumericsVector3.Transform( NumericsVector3.UnitX, local );
		Assert.AreEqual( 5f, point.X, 1e-5f );
		Assert.AreEqual( -2f, point.Z, 1e-5f );
	}

	[TestMethod]
	public void ReadsAColumnMajorMatrixAsTheRowVectorTransform()
	{
		var json = """
		{ "asset": { "version": "2.0" }, "nodes": [ { "matrix": [ 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 7, 8, 9, 1 ] } ] }
		""";
		Assert.AreEqual( new NumericsVector3( 7, 8, 9 ), GltfScene.Read( Glb( json, Array.Empty<byte>() ) ).Nodes[0].Local.Translation );
	}

	[TestMethod]
	public void RefusesWhatTheContentKitDoesNotImport()
	{
		Assert.ThrowsException<InvalidDataException>( () => GltfScene.Read( new byte[] { 1, 2, 3 } ) );
		Assert.ThrowsException<InvalidDataException>( () => GltfScene.Read( Triangle( extraJson: ", \"animations\": [ {} ]" ) ) );
		Assert.ThrowsException<InvalidDataException>( () => GltfScene.Read( Triangle( extraJson: ", \"skins\": [ {} ]" ) ) );
		Assert.ThrowsException<InvalidDataException>( () => GltfScene.Read( Triangle( extraJson: ", \"extensionsRequired\": [ \"KHR_draco_mesh_compression\" ]" ) ) );
		// A child listed by two parents, and a child that is its own parent.
		Assert.ThrowsException<InvalidDataException>( () => GltfScene.Read( ShareChild() ) );
		var truncated = Triangle();
		BinaryPrimitives.WriteUInt32LittleEndian( truncated.AsSpan( 8 ), (uint)truncated.Length + 4 );
		Assert.ThrowsException<InvalidDataException>( () => GltfScene.Read( truncated ) );
	}

	private static byte[] ShareChild() => Glb( """
		{ "asset": { "version": "2.0" }, "nodes": [ { "children": [ 2 ] }, { "children": [ 2 ] }, { } ] }
		""", Array.Empty<byte>() );

	[TestMethod]
	public void RefusesAnIndexOutsideTheVertices()
	{
		var data = Triangle();
		// The third u16 index sits at binary offset 100: point it at vertex 3 of 3.
		var binaryStart = data.Length - 108;
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( binaryStart + 100 ), 3 );
		Assert.ThrowsException<InvalidDataException>( () => GltfScene.Read( data ) );
	}

	/// <summary>The Blender sample (tools/blender/opentpw_sample_pool.py) reads with its six head points and two materials. Needs Blender (OPENTPW_BLENDER or /Applications/Blender.app).</summary>
	[TestMethod]
	public void ReadsTheBlenderSamplePool()
	{
		var blender = Environment.GetEnvironmentVariable( "OPENTPW_BLENDER" ) is { Length: > 0 } configured ? configured : "/Applications/Blender.app/Contents/MacOS/Blender";
		if ( !File.Exists( blender ) )
			Assert.Inconclusive( "Blender is not installed; set OPENTPW_BLENDER to run the exporter." );
		var script = Path.Combine( RepositoryRoot(), "tools", "blender", "opentpw_sample_pool.py" );
		var output = Path.Combine( Path.GetTempPath(), $"opentpw-sample-pool-{Guid.NewGuid():N}.glb" );
		try
		{
			var start = new ProcessStartInfo( blender ) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetTempPath() };
			foreach ( var argument in new[] { "-b", "--factory-startup", "--python", script, "--", "--out", output } )
				start.ArgumentList.Add( argument );
			using var process = Process.Start( start )!;
			var log = process.StandardOutput.ReadToEndAsync();
			var errors = process.StandardError.ReadToEndAsync();
			Assert.IsTrue( process.WaitForExit( 120_000 ), "Blender did not finish." );
			Assert.AreEqual( 0, process.ExitCode, errors.Result + log.Result );

			var scene = GltfScene.Read( output );
			var heads = scene.Nodes.Where( n => n.Extras.ContainsKey( "opentpw_head" ) ).ToList();
			CollectionAssert.AreEquivalent( new[] { 1, 2, 3, 4, 5, 6 }, heads.Select( h => h.Extras["opentpw_head"].GetInt32() ).ToArray() );
			// Y-up export: every head floats just above the water at the same height.
			Assert.IsTrue( heads.All( h => Math.Abs( h.Local.Translation.Y - 0.9f ) < 1e-4f ) );
			var water = scene.Materials.Single( m => m.Name == "pool_water" );
			Assert.IsTrue( water.Extras["opentpw_self_lit"].GetBoolean() );
			Assert.AreEqual( "image/png", scene.Images[water.Image].MimeType );
			Assert.AreEqual( 5, scene.Nodes.Count( n => n.Mesh >= 0 ) );
		}
		finally
		{
			File.Delete( output );
		}
	}

	private static string RepositoryRoot()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
			if ( Directory.Exists( Path.Combine( directory.FullName, "tools", "blender" ) ) )
				return directory.FullName;
		throw new DirectoryNotFoundException( "Repository root with tools/blender not found." );
	}
}
