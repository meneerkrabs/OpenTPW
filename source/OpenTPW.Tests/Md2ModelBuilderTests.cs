using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>The MD2 authoring API (docs/MD2-MODELS.md, "Writing and authoring"). No game data is used.</summary>
[TestClass]
[DoNotParallelize]
public class Md2ModelBuilderTests
{
	private static readonly Matrix4x4 SeatOffset = Matrix4x4.CreateTranslation( 3, 0.5f, -2 );

	/// <summary>A mesh node "body" (one textured quad) with a dummy child "seat"; head 2 hangs on the body, head 1 on the seat.</summary>
	private static ModelBuilder TwoNodes( out ModelNodeBuilder body, out ModelNodeBuilder seat )
	{
		var builder = new ModelBuilder { SourceName = "twonodes" };
		body = builder.AddMesh( "body" );
		var material = body.Mesh!.AddMaterial( "quad_tex", wrap: true );
		var normal = new NVector3( 0, 0, 1 );
		var a = material.AddVertex( new NVector3( 0, 0, 0 ), normal, new NVector2( 0, 0 ) );
		var b = material.AddVertex( new NVector3( 4, 0, 0 ), normal, new NVector2( 1, 0 ) );
		var c = material.AddVertex( new NVector3( 4, 2, 0 ), normal, new NVector2( 1, 1 ) );
		var d = material.AddVertex( new NVector3( 0, 2, 0 ), normal, new NVector2( 0, 1 ) );
		material.AddQuad( a, b, c, d );
		seat = builder.AddDummy( "seat", body, SeatOffset );
		body.AddHead( 2 );
		seat.AddHead( 1 );
		return builder;
	}

	[TestMethod]
	public void ATwoNodeModelReadsBackWithEveryFieldAndItsHeadRecords()
	{
		var builder = TwoNodes( out _, out _ );
		var model = builder.Build();

		Assert.AreEqual( ModelFileKind.Geometry, model.Kind );
		Assert.AreEqual( 221u, model.VersionMajor );
		Assert.AreEqual( 203u, model.VersionMinor );
		Assert.AreEqual( "twonodes", model.SourceName );
		Assert.AreEqual( ModelBuilder.DefaultHeaderFlags, model.HeaderFlags );

		Assert.AreEqual( 2, model.Nodes.Count );
		Assert.AreEqual( 0, model.RootNodeIndex );
		Assert.AreEqual( "body", model.Nodes[0].Name );
		Assert.AreEqual( -1, model.Nodes[0].ParentIndex );
		Assert.AreEqual( 0, model.Nodes[0].MeshIndex );
		Assert.AreEqual( ModelBuilder.MeshNodeType, model.Nodes[0].Type );
		Assert.AreEqual( Matrix4x4.Identity, model.Nodes[0].Transform );
		Assert.AreEqual( "seat", model.Nodes[1].Name );
		Assert.AreEqual( 0, model.Nodes[1].ParentIndex );
		Assert.AreEqual( -1, model.Nodes[1].MeshIndex );
		Assert.AreEqual( ModelBuilder.DummyNodeType, model.Nodes[1].Type );
		Assert.AreEqual( SeatOffset, model.Nodes[1].Transform );

		Assert.AreEqual( 1, model.Textures.Count );
		CollectionAssert.AreEqual( new[] { "quad_tex.tga" }, model.Textures[0].FrameNames.ToArray() );
		Assert.AreEqual( 0x31u, model.Textures[0].Flags, "wrap sets 0x10 and 0x20 on top of 0x1" );

		var mesh = model.Meshes.Single();
		Assert.AreEqual( "body", mesh.Name );
		Assert.AreEqual( 4, mesh.Positions.Length );
		CollectionAssert.AreEqual( new ushort[] { 0, 1, 2, 3 }, mesh.CornerPositionIndices );
		Assert.AreEqual( new NVector3( 4, 2, 0 ), ToNumerics( mesh.Positions[2] ) );
		Assert.AreEqual( new NVector2( 1, 1 ), new NVector2( mesh.TexCoords[2].X, mesh.TexCoords[2].Y ) );
		Assert.IsTrue( mesh.CornerNormals.All( normal => normal.X == 0 && normal.Y == 0 && normal.Z == 1 ) );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 0, 2, 3 }, mesh.Indices );
		Assert.AreEqual( 2, mesh.FaceNormals.Length );
		Assert.IsTrue( mesh.FaceNormals.All( normal => normal.X == 0 && normal.Y == 0 && normal.Z == 1 ), "counter-clockwise quad faces +Z" );
		CollectionAssert.AreEqual( new ushort[] { 4, 5 }, mesh.FaceNormalIndices, "face normals follow the corner normals" );
		var material = mesh.Materials.Single();
		Assert.AreEqual( "quad_tex", material.Name );
		Assert.AreEqual( 0, material.TextureIndex );
		Assert.AreEqual( 0x31u, material.Flags );
		Assert.AreEqual( 2, material.b, "face count" );
		Assert.AreEqual( 0, material.StartIndex );
		Assert.AreEqual( 3, material.EndIndex );
		Assert.AreEqual( new NVector3( 0, 0, 0 ), ToNumerics( mesh.BoundsMin ) );
		Assert.AreEqual( new NVector3( 4, 2, 0 ), ToNumerics( mesh.BoundsMax ) );
		Assert.AreEqual( new NVector3( 0, 0, 0 ), ToNumerics( model.BoundsMin ) );
		Assert.AreEqual( new NVector3( 4, 2, 0 ), ToNumerics( model.BoundsMax ) );

		// Two head records, one per node, from node Unknown46 = 0: record 0 belongs to the body, record 1 to the seat.
		Assert.AreEqual( 0, model.Counts.Unknown46 );
		Assert.AreEqual( 2, model.DummyAttributes.Count );
		Assert.AreEqual( ModelBuilder.DefaultHeadType, model.DummyAttributes[0].Type );
		Assert.AreEqual( 2u, model.DummyAttributes[0].Value );
		Assert.AreEqual( ModelBuilder.DefaultHeadType, model.DummyAttributes[1].Type );
		Assert.AreEqual( 1u, model.DummyAttributes[1].Value );

		// The existing head lookup (the code ADDHEAD runs through) finds both ids on the intended nodes.
		Assert.AreEqual( 1, RideHeadSpace.NodeForHead( model, 1 ) );
		Assert.AreEqual( 0, RideHeadSpace.NodeForHead( model, 2 ) );
		Assert.AreEqual( RideHeadSpace.NoRecord, RideHeadSpace.NodeForHead( model, 3 ) );
		var space = RideHeadSpace.Create( model, seed: 1 )!;
		Assert.AreEqual( 2, space.Count );
		Assert.AreEqual( 1, space.NodeAt( 0 ) );
		Assert.AreEqual( 0, space.NodeAt( 1 ) );
	}

	[TestMethod]
	public void HeadRecordsOnLaterDummiesGetPlainRecordsInBetween()
	{
		var builder = new ModelBuilder();
		var root = builder.AddMesh( "hull" );
		var material = root.Mesh!.AddMaterial( null );
		var normal = new NVector3( 0, 1, 0 );
		material.AddTriangle( material.AddVertex( NVector3.Zero, normal, NVector2.Zero ), material.AddVertex( NVector3.UnitX, normal, NVector2.Zero ), material.AddVertex( NVector3.UnitZ, normal, NVector2.Zero ) );
		var plain = builder.AddDummy( "marker", root );
		var second = builder.AddDummy( "seat_b", root ).AddHead( 2, 0xF1 );
		var first = builder.AddDummy( "seat_a", root ).AddHead( 1 );
		var model = builder.Build();

		Assert.AreEqual( 1, model.Counts.Unknown46, "the first dummy" );
		Assert.AreEqual( 3, model.DummyAttributes.Count );
		Assert.AreEqual( 0x1u, model.DummyAttributes[0].Type, "no head flag on the plain record" );
		Assert.AreEqual( 0u, model.DummyAttributes[0].Value );
		Assert.AreEqual( 2, RideHeadSpace.NodeForHead( model, 2 ), "seat_b is node 2" );
		Assert.AreEqual( 3, RideHeadSpace.NodeForHead( model, 1 ), "seat_a is node 3" );
		Assert.AreEqual( 0xF1u, model.DummyAttributes[1].Type );
		Assert.AreEqual( "marker", model.Nodes[1].Name );
		Assert.IsNotNull( plain );
		Assert.IsNotNull( first );
		Assert.IsNotNull( second );
		Assert.AreEqual( 1, model.Textures.Count + 1, "an untextured material owns no slot" );
		Assert.AreEqual( -1, model.Meshes[0].Materials[0].TextureIndex );
		Assert.AreEqual( "", model.Meshes[0].Materials[0].Name );
	}

	[TestMethod]
	public void WrapAndClampAreDistinctSlotsOfTheSameTexture()
	{
		var builder = new ModelBuilder();
		var root = builder.AddMesh( "m" );
		var normal = new NVector3( 0, 0, 1 );
		foreach ( var (name, wrap) in new[] { ("Grass.tga", true), ("grass", false), ("GRASS", true), ("sand.wct", false) } )
		{
			var material = root.Mesh!.AddMaterial( name, wrap );
			var a = material.AddVertex( NVector3.Zero, normal, NVector2.Zero );
			var b = material.AddVertex( NVector3.UnitX, normal, NVector2.UnitX );
			var c = material.AddVertex( NVector3.UnitY, normal, NVector2.UnitY );
			material.AddTriangle( a, b, c );
		}
		var model = builder.Build();
		CollectionAssert.AreEqual( new[] { "Grass.tga", "grass.tga", "sand.tga" }, model.Textures.Select( texture => texture.FrameNames[0] ).ToArray(),
			"names keep the case of their first use; wrap and clamp of one texture are two slots" );
		CollectionAssert.AreEqual( new uint[] { 0x31, 0x1, 0x1 }, model.Textures.Select( texture => texture.Flags ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 1, 0, 2 }, model.Meshes[0].Materials.Select( material => material.TextureIndex ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 3, 6, 9 }, model.Meshes[0].Materials.Select( material => (int)material.StartIndex ).ToArray() );
		Assert.AreEqual( 3, model.Meshes[0].Positions.Length, "equal positions are stored once" );
		Assert.AreEqual( 12, model.Meshes[0].Vertices.Length );
	}

	[TestMethod]
	public void MeshNodesComeBeforeDummiesAndAChildMayPrecedeItsMeshSibling()
	{
		var builder = new ModelBuilder();
		var root = builder.AddDummy( "root" );
		var group = builder.AddDummy( "group", root, Matrix4x4.CreateTranslation( 10, 0, 0 ) );
		var mesh = builder.AddMesh( "shape", group, Matrix4x4.CreateTranslation( 0, 5, 0 ) );
		var material = mesh.Mesh!.AddMaterial( null );
		var normal = new NVector3( 0, 0, 1 );
		material.AddTriangle( material.AddVertex( NVector3.Zero, normal, NVector2.Zero ), material.AddVertex( NVector3.UnitX, normal, NVector2.Zero ), material.AddVertex( NVector3.UnitY, normal, NVector2.Zero ) );
		var model = builder.Build();
		CollectionAssert.AreEqual( new[] { "shape", "root", "group" }, model.Nodes.Select( node => node.Name ).ToArray() );
		CollectionAssert.AreEqual( new[] { 2, -1, 1 }, model.Nodes.Select( node => node.ParentIndex ).ToArray() );
		Assert.AreEqual( 1, model.RootNodeIndex );
		Assert.AreEqual( new NVector3( 10, 5, 0 ), ToNumerics( model.BoundsMin ), "the header bounds are the composed hierarchy's box" );
		Assert.AreEqual( new NVector3( 11, 6, 0 ), ToNumerics( model.BoundsMax ) );
	}

	[TestMethod]
	public void TheWriterIsDeterministicAndRereadingAWrittenModelRewritesIdenticalBytes()
	{
		var bytes = TwoNodes( out _, out _ ).ToBytes();
		CollectionAssert.AreEqual( bytes, TwoNodes( out _, out _ ).ToBytes() );
		// planned layout of the read-back model (no captured layout)
		CollectionAssert.AreEqual( bytes, ModelFileWriter.Write( new ModelFile( new MemoryStream( bytes ) ) ) );
		// captured layout
		var captured = new ModelFile( new MemoryStream( bytes ), captureLayout: true );
		Assert.IsNotNull( captured.Layout );
		Assert.AreEqual( 0, captured.Layout!.Gaps.Count, "the planned layout leaves no non-zero byte between structures" );
		CollectionAssert.AreEqual( bytes, ModelFileWriter.Write( captured ) );
		using var stream = new MemoryStream();
		ModelFileWriter.Write( captured, stream );
		CollectionAssert.AreEqual( bytes, stream.ToArray() );
		Assert.IsNull( new ModelFile( new MemoryStream( bytes ) ).Layout, "an ordinary read keeps no layout" );
	}

	[TestMethod]
	public void InvalidModelsAreRefused()
	{
		Assert.ThrowsException<InvalidOperationException>( () => new ModelBuilder().ToBytes(), "no node" );

		var twoRoots = new ModelBuilder();
		twoRoots.AddDummy( "a" );
		twoRoots.AddDummy( "b" );
		StringAssert.Contains( Assert.ThrowsException<InvalidOperationException>( () => twoRoots.ToBytes() ).Message, "exactly one root" );

		var gap = new ModelBuilder();
		var root = gap.AddDummy( "root" );
		gap.AddDummy( "seat", root ).AddHead( 2 );
		StringAssert.Contains( Assert.ThrowsException<InvalidOperationException>( () => gap.ToBytes() ).Message, "from 1 without a gap" );

		var duplicate = new ModelBuilder();
		var top = duplicate.AddDummy( "root" );
		duplicate.AddDummy( "a", top ).AddHead( 1 );
		duplicate.AddDummy( "b", top ).AddHead( 1 );
		Assert.ThrowsException<InvalidOperationException>( () => duplicate.ToBytes() );

		var node = new ModelBuilder().AddDummy( "n" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => node.AddHead( 0 ) );
		Assert.ThrowsException<ArgumentException>( () => node.AddHead( 1, 0x31 ), "the head flag 0x80 is required" );
		node.AddHead( 1 );
		Assert.ThrowsException<InvalidOperationException>( () => node.AddHead( 2 ), "one head record per node" );

		var tooLong = new ModelBuilder();
		var mesh = tooLong.AddMesh( "m" );
		StringAssert.Contains( Assert.ThrowsException<InvalidOperationException>( () =>
		{
			var material = mesh.Mesh!.AddMaterial( "sixteen_chars_xx" );
			material.AddTriangle( material.AddVertex( NVector3.Zero, NVector3.UnitZ, NVector2.Zero ), material.AddVertex( NVector3.UnitX, NVector3.UnitZ, NVector2.Zero ), material.AddVertex( NVector3.UnitY, NVector3.UnitZ, NVector2.Zero ) );
			tooLong.ToBytes();
		} ).Message, "longer than 15" );

		var other = new ModelBuilder();
		Assert.ThrowsException<ArgumentException>( () => new ModelBuilder().AddDummy( "child", other.AddDummy( "foreign" ) ), "a parent from another model" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
		{
			var material = new ModelBuilder().AddMesh( "m" ).Mesh!.AddMaterial( null );
			material.AddTriangle( 0, 1, 2 );
		} );
	}

	[TestMethod]
	public void AnimationMembersAreNotWritten()
	{
		var animation = Md2AnimationTests.CreateAnimation();
		var model = new ModelFile( new MemoryStream( animation ) );
		Assert.AreEqual( ModelFileKind.Animation, model.Kind );
		Assert.ThrowsException<NotSupportedException>( () => ModelFileWriter.Write( model ) );
	}

	private static NVector3 ToNumerics( OpenTPW.Vector3 value ) => new( value.X, value.Y, value.Z );
}

/// <summary>A built model loads through the CPU path the game uses for object models: a mod's objects.json, the catalog entry, <see cref="ObjectAssets"/>.</summary>
[TestClass]
[DoNotParallelize]
public class Md2ModelBuilderObjectPathTests
{
	private static readonly IOpenTpwExtension Core = new OpenTpwExtension( "opentpw.core-test", "Core test", new ModVersion( 1, 0, 0 ), true, false );
	private string root = null!;
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Initialize()
	{
		root = Directory.CreateTempSubdirectory( "opentpw-md2builder-" ).FullName;
		previousFileSystem = FileSystem;
		var game = Path.Combine( root, "game" );
		var totem = Path.Combine( game, "levels", "jungle", "rides", "totem" );
		Directory.CreateDirectory( totem );
		File.WriteAllText( Path.Combine( totem, "totem.sam" ), "Info.Id\t1110\nInfo.Name\t\"Inca Totem\"\nInfo.WhichUIType\t0\nUpgrades[0].CostOfUpgrade\t100\n" );
		File.WriteAllBytes( Path.Combine( totem, "totem.MD2" ), Array.Empty<byte>() );
		FileSystem = new BaseFileSystem( game );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previousFileSystem!;
		Directory.Delete( root, true );
	}

	[TestMethod]
	public void ABuiltModelLoadsAsAModObjectThroughTheCatalogAndObjectAssets()
	{
		var builder = new ModelBuilder();
		var body = builder.AddMesh( "body" );
		var material = body.Mesh!.AddMaterial( "quad_tex", wrap: true );
		var normal = new NVector3( 0, 0, 1 );
		var a = material.AddVertex( new NVector3( 0, 0, 0 ), normal, new NVector2( 0, 0 ) );
		var b = material.AddVertex( new NVector3( 4, 0, 0 ), normal, new NVector2( 1, 0 ) );
		var c = material.AddVertex( new NVector3( 4, 2, 0 ), normal, new NVector2( 1, 1 ) );
		var d = material.AddVertex( new NVector3( 0, 2, 0 ), normal, new NVector2( 0, 1 ) );
		material.AddQuad( a, b, c, d );
		builder.AddDummy( "seat", body, Matrix4x4.CreateTranslation( 1, 1, 1 ) ).AddHead( 1 );

		var mod = Path.Combine( root, "mods", "community.pool" );
		var archive = Path.Combine( mod, "objects", "pool" );
		Directory.CreateDirectory( Path.Combine( archive, "textures" ) );
		File.WriteAllText( Path.Combine( mod, ModManifest.FileName ), """{ "schema": 1, "id": "community.pool", "name": "Pool", "version": "1.0.0", "category": "community", "content": { "objects": "objects.json" } }""" );
		File.WriteAllText( Path.Combine( mod, "objects.json" ), """{ "schema": 1, "idRange": { "first": 20000, "last": 20009 }, "objects": [{ "archive": "objects/pool", "theme": "jungle", "category": "rides", "infoId": 20001, "names": { "English": "Pool" } }] }""" );
		File.WriteAllText( Path.Combine( archive, "pool.sam" ), "Info.Id\t5\nInfo.Name\t\"Pool\"\nInfo.WhichUIType\t0\nUpgrades[0].CostOfUpgrade\t100\n" );
		File.WriteAllBytes( Path.Combine( archive, "pool.MD2" ), builder.ToBytes() );
		File.WriteAllBytes( Path.Combine( archive, "textures", "quad_tex.tga" ), new byte[] { 0 } );

		var set = ModSet.Resolve( ModDiscovery.Discover( new[] { mod }, Array.Empty<string>(), null, null ).ToList(),
			new ModSettings { Enabled = new Dictionary<string, bool> { ["community.pool"] = true } }, null, GameEdition.ThemeParkWorld, new[] { Core } );
		Assert.AreEqual( 1, set.Mods.Count, string.Join( "; ", set.Refused.Select( refusal => refusal.Reason ) ) );
		using ( ModContentRoots.Use( set, FileSystem ) )
		{
			var entry = ObjectCatalog.Load( "jungle" ).Get( 20001 );
			Assert.AreEqual( "/objects/pool", entry.ArchivePath );
			var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
			Assert.AreEqual( 2, model.Nodes.Count );
			var mesh = model.Meshes.Single();
			Assert.IsNull( ObjectAssets.GetUnrenderableReason( mesh ) );
			var vertices = ObjectAssets.ConvertMesh( mesh );
			Assert.AreEqual( 4, vertices.Length );
			Assert.AreEqual( "quad_tex", mesh.Materials[0].Name );
			// the texture resolves by name in <archive>/textures
			var texture = ObjectAssets.ResolveTexture( entry, mesh.Materials[0].Name );
			Assert.IsNotNull( texture );
			Assert.AreEqual( "quad_tex.tga", Path.GetFileName( texture!.Value.Path ) );
			// and the head record is found by the ride head space
			Assert.AreEqual( 1, RideHeadSpace.Create( model, seed: 1 )!.NodeAt( 0 ) );
		}
	}
}
