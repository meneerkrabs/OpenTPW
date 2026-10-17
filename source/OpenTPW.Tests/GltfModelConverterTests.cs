using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The glTF to MD2 converter over hand-built files (docs/CONTENT-KIT.md, "Converter conventions"): the handedness and texture-V conventions,
/// the hierarchy, materials and heads, and every refusal. No game data and no GPU.
/// </summary>
[TestClass]
public class GltfModelConverterTests
{
	private static GltfModelConversion Convert( byte[] glb ) => GltfModelConverter.Convert( GltfScene.Read( glb ) );

	private static InvalidDataException Refused( byte[] glb ) => Assert.ThrowsException<InvalidDataException>( () => Convert( glb ) );

	private static ModelFile Read( GltfModelConversion conversion ) => conversion.Model.Build();

	/// <summary>One textured quad under a root, in the shape the convention tests use.</summary>
	private static byte[] QuadScene( Vector3? translation = null, Quaternion? rotation = null, string imageName = "tile", bool selfLit = false )
	{
		var glb = new GlbBuilder();
		var image = glb.AddImage( imageName );
		var material = glb.AddMaterial( "tile", image, selfLit: selfLit );
		var mesh = glb.AddMesh( "quad", GlbBuilder.Quad( material ) );
		var node = glb.AddNode( "quad", mesh, translation, rotation );
		return glb.Build( node );
	}

	// ---- Conventions ---------------------------------------------------------------------------------------

	[TestMethod]
	public void PositionsAreMirroredInZAndNormalsFollow()
	{
		var model = Read( Convert( QuadScene() ) );
		var mesh = model.Meshes.Single();
		// (x, y, z) -> (x, y, -z); the quad lies in z = 0 so only the normal shows it
		CollectionAssert.AreEquivalent( new[] { (0f, 0f), (4f, 0f), (4f, 2f), (0f, 2f) }, mesh.Vertices.Select( vertex => (vertex.Position.X, vertex.Position.Y) ).ToArray() );
		Assert.IsTrue( mesh.Vertices.All( vertex => vertex.Position.Z == 0 ) );
		Assert.IsTrue( mesh.Normals.All( normal => normal.Z == -1 && normal.X == 0 && normal.Y == 0 ) );

		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImage( "tile" ) );
		var deep = new GlbBuilder.Primitive( new float[] { 0, 0, 5, 1, 0, 5, 0, 1, 5 }, new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 }, new float[] { 0, 1, 1, 1, 0, 0 }, new[] { 0, 1, 2 }, material );
		var moved = Read( Convert( glb.Build( glb.AddNode( "deep", glb.AddMesh( "deep", deep ) ) ) ) ).Meshes.Single();
		Assert.IsTrue( moved.Vertices.All( vertex => vertex.Position.Z == -5 ), "z 5 becomes -5" );
	}

	[TestMethod]
	public void TextureVIsFlippedBecauseMd2RunsBottomUp()
	{
		var mesh = Read( Convert( QuadScene() ) ).Meshes.Single();
		// glTF: the quad's top (y = 2) shows the image's top row, v = 0. MD2: the top of the image is V = 1.
		foreach ( var (vertex, index) in mesh.Vertices.Select( ( vertex, index ) => (vertex, index) ) )
		{
			var expectedV = vertex.Position.Y == 2 ? 1f : 0f;
			Assert.AreEqual( expectedV, mesh.TexCoords[index].Y, $"vertex {index}" );
			Assert.AreEqual( vertex.Position.X == 4 ? 1f : 0f, mesh.TexCoords[index].X, "U is unchanged" );
		}
		Assert.AreEqual( new System.Numerics.Vector2( 1, 0.25f ), GltfModelConverter.ToMd2TexCoord( new System.Numerics.Vector2( 1, 0.75f ) ) );
	}

	[TestMethod]
	public void WindingIsReversedSoTheStoredFaceNormalPointsOutward()
	{
		var mesh = Read( Convert( QuadScene() ) ).Meshes.Single();
		Assert.AreEqual( 2, mesh.Indices.Length / 3 );
		for ( var face = 0; face < 2; face++ )
		{
			var p = Enumerable.Range( 0, 3 ).Select( corner => mesh.Vertices[(int)mesh.Indices[face * 3 + corner]].Position.GetSystemVector3() ).ToArray();
			var cross = NVector3.Normalize( NVector3.Cross( p[1] - p[0], p[2] - p[0] ) );
			// the corpus rule: cross(p1 - p0, p2 - p0) is the face normal, and it agrees with the (reflected) vertex normal
			Assert.AreEqual( -1f, cross.Z, 1e-5f, $"face {face}" );
			Assert.AreEqual( cross.Z, mesh.FaceNormals[face].Z, 1e-5f );
		}
	}

	/// <summary>The frame the engine draws an MD2 vertex in: ObjectAssets.ConvertAxes (Y and Z swapped) of the node-composed position, as OriginalObject does.</summary>
	private static (NVector3[] Positions, System.Numerics.Vector2[] Uvs, uint[] Indices) EngineTriangles( ModelFile model, string node )
	{
		var mesh = model.Meshes.Single( candidate => model.Nodes[candidate.NodeIndex].Name == node );
		var world = Matrix4x4.Identity;
		for ( var index = mesh.NodeIndex; index >= 0; index = model.Nodes[index].ParentIndex )
			world *= model.Nodes[index].Transform;
		var engine = ObjectPlacement.SwapAxes( world );
		var vertices = ObjectAssets.ConvertMesh( mesh );
		return (vertices.Select( vertex => NVector3.Transform( vertex.Position.GetSystemVector3(), engine ) ).ToArray(),
			vertices.Select( vertex => new System.Numerics.Vector2( vertex.TexCoords.X, vertex.TexCoords.Y ) ).ToArray(), mesh.Indices.ToArray());
	}

	/// <summary>glTF (x, y, z), right-handed Y-up, in the engine frame: Blender (x, y, z) is the engine's (x, y, z) and glTF's (x, z, -y).</summary>
	private static NVector3 GltfToEngine( NVector3 value ) => new( value.X, -value.Z, value.Y );

	/// <summary>
	/// The end-to-end convention check, in the engine's right-handed camera frame (Veldrid, FrontFace.Clockwise, back faces culled): a triangle is drawn when the viewer
	/// stands on the side opposite <c>cross(p1 - p0, p2 - p0)</c>; from there the image must read as in glTF: on the glTF front side, U to the right, the image's top up.
	/// </summary>
	private static void AssertQuadReadsLikeGltf( ModelFile model, string node, NVector3 gltfFrontNormal )
	{
		var (positions, uvs, indices) = EngineTriangles( model, node );
		var checkedFaces = 0;
		for ( var face = 0; face < indices.Length / 3; face++ )
		{
			var i = Enumerable.Range( 0, 3 ).Select( corner => (int)indices[face * 3 + corner] ).ToArray();
			var d1 = positions[i[1]] - positions[i[0]];
			var d2 = positions[i[2]] - positions[i[0]];
			var away = NVector3.Normalize( NVector3.Cross( d1, d2 ) ); // points away from the viewer
			Assert.IsTrue( NVector3.Dot( -away, NVector3.Normalize( GltfToEngine( gltfFrontNormal ) ) ) > 0.99f, $"face {face} is drawn from the glTF front side" );
			var right = NVector3.Normalize( NVector3.Cross( away, NVector3.UnitZ ) );
			var up = NVector3.Cross( right, away );
			float du1 = uvs[i[1]].X - uvs[i[0]].X, dv1 = uvs[i[1]].Y - uvs[i[0]].Y, du2 = uvs[i[2]].X - uvs[i[0]].X, dv2 = uvs[i[2]].Y - uvs[i[0]].Y;
			var determinant = du1 * dv2 - du2 * dv1;
			var alongU = (d1 * dv2 - d2 * dv1) / determinant; // dP/dU
			var alongV = (d2 * du1 - d1 * du2) / determinant; // dP/dV
			Assert.IsTrue( NVector3.Dot( alongU, right ) > 0, $"face {face}: U runs to the viewer's right" );
			Assert.IsTrue( NVector3.Dot( alongV, up ) > 0, $"face {face}: V runs up (the image's top row, V = 1, is at the top)" );
			checkedFaces++;
		}
		Assert.AreEqual( 2, checkedFaces );
	}

	[TestMethod]
	public void AGltfQuadReadsUnmirroredInTheEngineFrame()
	{
		AssertQuadReadsLikeGltf( Read( Convert( QuadScene() ) ), "quad", new NVector3( 0, 0, 1 ) );
	}

	[TestMethod]
	public void ARotatedAndMovedQuadStillReadsUnmirrored()
	{
		// the node matrix is conjugated by the same mirror: a quarter turn about Y (+Z front -> +X front) and a translation
		var rotation = Quaternion.CreateFromAxisAngle( NVector3.UnitY, MathF.PI / 2 );
		var model = Read( Convert( QuadScene( new Vector3( 3, 1, -2 ), rotation ) ) );
		AssertQuadReadsLikeGltf( model, "quad", NVector3.TransformNormal( new NVector3( 0, 0, 1 ), Matrix4x4.CreateFromQuaternion( rotation ) ) );

		// and through a parent: front normal rotated by the parent, quad offset by both
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImage( "tile" ) );
		var mesh = glb.AddMesh( "quad", GlbBuilder.Quad( material ) );
		var childRotation = Quaternion.CreateFromYawPitchRoll( 0.7f, -0.4f, 0.3f ); // tilted, not horizontal: the screen's up needs a vertical component
		var parentRotation = Quaternion.CreateFromAxisAngle( NVector3.UnitY, MathF.PI / 3 );
		var child = glb.AddNode( "quad", mesh, new Vector3( 1, 0, 0 ), childRotation );
		var parent = glb.AddNode( "parent", -1, new Vector3( 0, 2, 5 ), parentRotation, new[] { child } );
		// row vectors: world = child * parent
		var world = Matrix4x4.CreateFromQuaternion( childRotation ) * Matrix4x4.CreateFromQuaternion( parentRotation );
		AssertQuadReadsLikeGltf( Read( Convert( glb.Build( parent ) ) ), "quad", NVector3.TransformNormal( new NVector3( 0, 0, 1 ), world ) );
	}

	[TestMethod]
	public void TheMatrixConjugationNegatesTheEntriesThatMixZ()
	{
		var matrix = Matrix4x4.CreateFromYawPitchRoll( 0.3f, 0.5f, -0.2f ) * Matrix4x4.CreateTranslation( 1, 2, 3 );
		var mirrored = GltfModelConverter.ToMd2Matrix( matrix );
		var mirror = Matrix4x4.CreateScale( 1, 1, -1 );
		var expected = mirror * matrix * mirror;
		foreach ( var (a, b) in new[] { (mirrored.M11, expected.M11), (mirrored.M13, expected.M13), (mirrored.M23, expected.M23), (mirrored.M31, expected.M31), (mirrored.M33, expected.M33), (mirrored.M41, expected.M41), (mirrored.M43, expected.M43), (mirrored.M44, expected.M44) } )
			Assert.AreEqual( b, a, 1e-5f );
		// a point moved by M then mirrored equals the mirrored point moved by the conjugated matrix
		var point = new NVector3( 0.4f, -1.2f, 2.5f );
		var left = GltfModelConverter.ToMd2Position( NVector3.Transform( point, matrix ) );
		var right = NVector3.Transform( GltfModelConverter.ToMd2Position( point ), mirrored );
		Assert.AreEqual( 0f, ( left - right ).Length(), 1e-4f );
	}

	// ---- Structure ------------------------------------------------------------------------------------------

	[TestMethod]
	public void SeveralRootsGetADummyRootAndASingleRootIsKept()
	{
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImage( "tile" ) );
		var mesh = glb.AddMesh( "quad", GlbBuilder.Quad( material ) );
		var a = glb.AddNode( "a", mesh );
		var b = glb.AddNode( "b", mesh, new Vector3( 5, 0, 0 ) );
		var model = Read( GltfModelConverter.Convert( GltfScene.Read( glb.Build( a, b ) ), new GltfConversionOptions { RootName = "pool" } ) );
		var root = model.Nodes[model.RootNodeIndex];
		Assert.AreEqual( "pool", root.Name );
		Assert.AreEqual( 3, model.Nodes.Count );
		Assert.AreEqual( 2, model.Nodes.Count( node => node.ParentIndex == model.RootNodeIndex ) );
		Assert.AreEqual( -1, root.MeshIndex, "the added root is a dummy" );

		var single = Read( Convert( QuadScene() ) );
		Assert.AreEqual( 1, single.Nodes.Count );
		Assert.AreEqual( "quad", single.Nodes[single.RootNodeIndex].Name );
	}

	[TestMethod]
	public void TheDummyRootNameAvoidsTheNodeNames()
	{
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImage( "tile" ) );
		var mesh = glb.AddMesh( "quad", GlbBuilder.Quad( material ) );
		var model = Read( Convert( glb.Build( glb.AddNode( "root", mesh ), glb.AddNode( "other", mesh ) ) ) );
		Assert.AreEqual( "root1", model.Nodes[model.RootNodeIndex].Name );
	}

	[TestMethod]
	public void NodesKeepTheirHierarchyKindAndLocalTransform()
	{
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImage( "tile" ) );
		var mesh = glb.AddMesh( "quad", GlbBuilder.Quad( material ) );
		var leaf = glb.AddNode( "leaf", mesh, new Vector3( 1, 2, 3 ) );
		var group = glb.AddNode( "group", -1, new Vector3( 10, 0, 4 ), null, new[] { leaf } );
		var model = Read( Convert( glb.Build( group ) ) );
		var groupNode = model.Nodes.Single( node => node.Name == "group" );
		var leafNode = model.Nodes.Single( node => node.Name == "leaf" );
		Assert.AreEqual( -1, groupNode.MeshIndex, "a node without a mesh is a dummy" );
		Assert.AreEqual( groupNode.Index, leafNode.ParentIndex );
		Assert.IsTrue( leafNode.MeshIndex >= 0 );
		// translation z flips sign, x and y stay (row-vector matrix, translation in the fourth row)
		Assert.AreEqual( (1f, 2f, -3f), (leafNode.Transform.M41, leafNode.Transform.M42, leafNode.Transform.M43) );
		Assert.AreEqual( (10f, 0f, -4f), (groupNode.Transform.M41, groupNode.Transform.M42, groupNode.Transform.M43) );
	}

	[TestMethod]
	public void PrimitivesOfOneMaterialMergeAndCornersAreDeduplicatedPerMaterial()
	{
		var glb = new GlbBuilder();
		var tile = glb.AddMaterial( "tile", glb.AddImage( "tile" ) );
		var water = glb.AddMaterial( "water", glb.AddImage( "water" ), wrap: false );
		// two primitives of `tile` that share their edge corners exactly, and one of `water` at the same positions
		var first = GlbBuilder.Quad( tile );
		var second = new GlbBuilder.Primitive( first.Positions, first.Normals, first.Uvs, first.Indices, tile );
		var other = GlbBuilder.Quad( water );
		var model = Read( Convert( glb.Build( glb.AddNode( "mixed", glb.AddMesh( "mixed", first, other, second ) ) ) ) );
		var mesh = model.Meshes.Single();
		Assert.AreEqual( 2, mesh.Materials.Length, "the two tile primitives are one material" );
		CollectionAssert.AreEqual( new[] { "tile", "water" }, mesh.Materials.Select( material => material.Name ).ToArray() );
		Assert.AreEqual( 8, mesh.Vertices.Length, "4 corners per material, the repeated quad adds none" );
		Assert.AreEqual( 6, mesh.Indices.Length / 3, "the tile quad twice (4 faces) plus the water quad (2)" );
		Assert.AreEqual( 4, mesh.Positions.Length, "equal positions are stored once" );
	}

	[TestMethod]
	public void MaterialExtrasSetWrapAndSelfLit()
	{
		var glb = new GlbBuilder();
		var wrapped = glb.AddMaterial( "wrapped", glb.AddImage( "wrapped" ) ); // default wrap = true, self lit = false
		var clamped = glb.AddMaterial( "clamped", glb.AddImage( "clamped" ), wrap: false, selfLit: true );
		var model = Read( Convert( glb.Build( glb.AddNode( "m", glb.AddMesh( "m", GlbBuilder.Quad( wrapped ), GlbBuilder.Quad( clamped ) ) ) ) ) );
		var materials = model.Meshes.Single().Materials;
		var wrappedMaterial = materials.Single( material => material.Name == "wrapped" );
		var clampedMaterial = materials.Single( material => material.Name == "clamped" );
		Assert.AreEqual( ModelBuilder.SlotBaseFlags | ModelBuilder.SlotWrapFlags, wrappedMaterial.Flags );
		Assert.AreEqual( ModelBuilder.SlotBaseFlags, clampedMaterial.Flags );
		Assert.AreEqual( 0u, wrappedMaterial.Pad );
		Assert.AreEqual( ModelBuilder.SelfLitMaterialFlag, clampedMaterial.Pad & ModelBuilder.SelfLitMaterialFlag, "material word +0xC bit 0x1" );
		Assert.AreEqual( "wrapped.tga", model.Textures[wrappedMaterial.TextureIndex].FrameNames[0] );
	}

	[TestMethod]
	public void NumericExtrasAreAcceptedAndOtherTypesRefused()
	{
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImage( "tile" ), rawExtras: "\"opentpw_wrap\": 0, \"opentpw_self_lit\": 1" );
		var model = Read( Convert( glb.Build( glb.AddNode( "m", glb.AddMesh( "m", GlbBuilder.Quad( material ) ) ) ) ) );
		Assert.AreEqual( ModelBuilder.SlotBaseFlags, model.Meshes.Single().Materials[0].Flags );
		Assert.AreEqual( 1u, model.Meshes.Single().Materials[0].Pad );

		var bad = new GlbBuilder();
		var badMaterial = bad.AddMaterial( "m", bad.AddImage( "tile" ), rawExtras: "\"opentpw_wrap\": \"yes\"" );
		StringAssert.Contains( Refused( bad.Build( bad.AddNode( "m", bad.AddMesh( "m", GlbBuilder.Quad( badMaterial ) ) ) ) ).Message, "opentpw_wrap" );
	}

	[TestMethod]
	public void HeadExtrasMakeHeadRecordsAndAreFoundByTheRideHeadSpace()
	{
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImage( "tile" ) );
		var mesh = glb.AddMesh( "quad", GlbBuilder.Quad( material ) );
		var h2 = glb.AddNode( "head02", -1, new Vector3( 2, 1, 0 ), null, null, "\"opentpw_head\": 2" );
		var h1 = glb.AddNode( "head01", -1, new Vector3( 1, 1, 0 ), null, null, "\"opentpw_head\": 1" );
		var body = glb.AddNode( "body", mesh, null, null, new[] { h2, h1 } );
		var conversion = Convert( glb.Build( body ) );
		Assert.AreEqual( 0, conversion.Warnings.Count );
		var model = Read( conversion );
		var space = RideHeadSpace.Create( model, seed: 1 )!;
		Assert.AreEqual( 2, space.Count );
		Assert.AreEqual( "head01", model.Nodes[RideHeadSpace.NodeForHead( model, 1 )].Name );
		Assert.AreEqual( "head02", model.Nodes[RideHeadSpace.NodeForHead( model, 2 )].Name );
	}

	[TestMethod]
	public void HeadIdsMustBeIntegersFromOneWithoutGaps()
	{
		byte[] Scene( params string[] extras )
		{
			var glb = new GlbBuilder();
			var material = glb.AddMaterial( "m", glb.AddImage( "tile" ) );
			var mesh = glb.AddMesh( "quad", GlbBuilder.Quad( material ) );
			var heads = extras.Select( ( extra, index ) => glb.AddNode( $"head{index}", -1, null, null, null, extra ) ).ToArray();
			return glb.Build( glb.AddNode( "body", mesh, null, null, heads ) );
		}
		StringAssert.Contains( Refused( Scene( "\"opentpw_head\": 1", "\"opentpw_head\": 3" ) ).Message, "without a gap" );
		StringAssert.Contains( Refused( Scene( "\"opentpw_head\": 1", "\"opentpw_head\": 1" ) ).Message, "without a gap" );
		StringAssert.Contains( Refused( Scene( "\"opentpw_head\": 0" ) ).Message, "opentpw_head" );
		StringAssert.Contains( Refused( Scene( "\"opentpw_head\": 1.5" ) ).Message, "opentpw_head" );
		StringAssert.Contains( Refused( Scene( "\"opentpw_head\": \"1\"" ) ).Message, "opentpw_head" );
	}

	[TestMethod]
	public void MissingNormalsBecomeFlatFaceNormals()
	{
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImage( "tile" ) );
		var quad = GlbBuilder.Quad( material );
		var model = Read( Convert( glb.Build( glb.AddNode( "m", glb.AddMesh( "m", quad with { Normals = null } ) ) ) ) );
		Assert.IsTrue( model.Meshes.Single().Normals.All( normal => Math.Abs( normal.Z + 1 ) < 1e-5f ), "outward, like the reflected vertex normals of the same quad" );
	}

	[TestMethod]
	public void DoubleSidedMaterialsWarn()
	{
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "twosided", glb.AddImage( "tile" ), doubleSided: true );
		var conversion = Convert( glb.Build( glb.AddNode( "m", glb.AddMesh( "m", GlbBuilder.Quad( material ) ) ) ) );
		Assert.AreEqual( 1, conversion.Warnings.Count );
		StringAssert.Contains( conversion.Warnings[0], "twosided" );
	}

	// ---- Textures --------------------------------------------------------------------------------------------

	[TestMethod]
	public void TextureNamesComeFromTheImageNamesWithoutTheExtension()
	{
		var conversion = Convert( QuadScene( imageName: "Pool_Tile.PNG" ) );
		Assert.AreEqual( "Pool_Tile", conversion.Textures.Single().Name );
		Assert.AreEqual( "Pool_Tile.tga", Read( conversion ).Textures[0].FrameNames[0] );
		Assert.AreEqual( 15, Convert( QuadScene( imageName: "123456789012345" ) ).Textures.Single().Name.Length );
	}

	[TestMethod]
	public void LongOrOddTextureNamesAreRefused()
	{
		StringAssert.Contains( Refused( QuadScene( imageName: "1234567890123456" ) ).Message, "15" );
		StringAssert.Contains( Refused( QuadScene( imageName: "sehr_schön" ) ).Message, "printable ASCII" );
		StringAssert.Contains( Refused( QuadScene( imageName: "sub/dir" ) ).Message, "plain file name" );
	}

	[TestMethod]
	public void TwoImagesWithTheSameNameAreRefused()
	{
		var glb = new GlbBuilder();
		var a = glb.AddMaterial( "a", glb.AddImage( "tile.png" ) );
		var b = glb.AddMaterial( "b", glb.AddImage( "TILE.jpg", red: 1 ) );
		StringAssert.Contains( Refused( glb.Build( glb.AddNode( "m", glb.AddMesh( "m", GlbBuilder.Quad( a ), GlbBuilder.Quad( b ) ) ) ) ).Message, "both become the texture" );
	}

	[TestMethod]
	public void OnlyPngAndJpegImagesAreRead()
	{
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImageBytes( "tile", new byte[] { 1, 2, 3, 4 }, "image/webp" ) );
		StringAssert.Contains( Refused( glb.Build( glb.AddNode( "m", glb.AddMesh( "m", GlbBuilder.Quad( material ) ) ) ) ).Message, "image/webp" );
	}

	// ---- Refusals --------------------------------------------------------------------------------------------

	[TestMethod]
	public void AMeshWithoutUvsIsRefused()
	{
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImage( "tile" ) );
		var message = Refused( glb.Build( glb.AddNode( "plain", glb.AddMesh( "plain", GlbBuilder.Quad( material ) with { Uvs = null } ) ) ) ).Message;
		StringAssert.Contains( message, "plain" );
		StringAssert.Contains( message, "UV" );
	}

	[TestMethod]
	public void AMaterialWithoutATextureIsRefused()
	{
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "flat", -1 );
		StringAssert.Contains( Refused( glb.Build( glb.AddNode( "m", glb.AddMesh( "m", GlbBuilder.Quad( material ) ) ) ) ).Message, "flat" );
		var none = new GlbBuilder();
		StringAssert.Contains( Refused( none.Build( none.AddNode( "m", none.AddMesh( "m", GlbBuilder.Quad( -1 ) ) ) ) ).Message, "no material" );
	}

	[TestMethod]
	public void TooManyVerticesForAnMd2MeshAreRefused()
	{
		// 22,000 triangles of three distinct corners each: 66,000 corners and positions, more than a u16 count holds
		const int triangles = 22_000;
		var positions = new float[triangles * 9];
		var normals = new float[triangles * 9];
		var uvs = new float[triangles * 6];
		var indices = Enumerable.Range( 0, triangles * 3 ).ToArray();
		for ( var triangle = 0; triangle < triangles; triangle++ )
		{
			positions[triangle * 9] = triangle;
			positions[triangle * 9 + 3] = triangle + 0.5f;
			positions[triangle * 9 + 4] = 1;
			positions[triangle * 9 + 6] = triangle;
			positions[triangle * 9 + 7] = 1;
			for ( var vertex = 0; vertex < 3; vertex++ )
				normals[triangle * 9 + vertex * 3 + 2] = 1;
		}
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImage( "tile" ) );
		var message = Refused( glb.Build( glb.AddNode( "big", glb.AddMesh( "big", new GlbBuilder.Primitive( positions, normals, uvs, indices, material ) ) ) ) ).Message;
		StringAssert.Contains( message, "big" );
		StringAssert.Contains( message, "65535" );
	}

	[TestMethod]
	public void SeamsThatSplitCornersCountTowardsTheLimit()
	{
		// 21,846 triangles share 21,848 positions in a strip but every triangle has its own UVs: 65,538 corners, 21,848 positions
		const int triangles = 21_846;
		var positions = new List<float>();
		var normals = new List<float>();
		var uvs = new List<float>();
		var indices = new List<int>();
		for ( var triangle = 0; triangle < triangles; triangle++ )
		{
			foreach ( var (x, y) in new[] { (triangle % 100, triangle / 100), (triangle % 100 + 1, triangle / 100), (triangle % 100, triangle / 100 + 1) } )
			{
				indices.Add( positions.Count / 3 );
				positions.AddRange( new float[] { x, y, 0 } );
				normals.AddRange( new float[] { 0, 0, 1 } );
				uvs.AddRange( new[] { triangle * 1e-5f, 0 } );
			}
		}
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImage( "tile" ) );
		var message = Refused( glb.Build( glb.AddNode( "seams", glb.AddMesh( "seams", new GlbBuilder.Primitive( positions.ToArray(), normals.ToArray(), uvs.ToArray(), indices.ToArray(), material ) ) ) ) ).Message;
		StringAssert.Contains( message, "vertices once split" );
	}

	[TestMethod]
	public void NodeNamesMustBePrintableAsciiAndUnique()
	{
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImage( "tile" ) );
		var mesh = glb.AddMesh( "quad", GlbBuilder.Quad( material ) );
		StringAssert.Contains( Refused( glb.Build( glb.AddNode( "wände", mesh ) ) ).Message, "printable ASCII" );

		var twice = new GlbBuilder();
		var material2 = twice.AddMaterial( "m", twice.AddImage( "tile" ) );
		var mesh2 = twice.AddMesh( "quad", GlbBuilder.Quad( material2 ) );
		var child = twice.AddNode( "Part", mesh2 );
		var parent = twice.AddNode( "part", -1, null, null, new[] { child } );
		StringAssert.Contains( Refused( twice.Build( parent ) ).Message, "unique" );
	}

	[TestMethod]
	public void NonFiniteNumbersAreRefused()
	{
		var glb = new GlbBuilder();
		var material = glb.AddMaterial( "m", glb.AddImage( "tile" ) );
		var quad = GlbBuilder.Quad( material );
		var broken = quad with { Positions = (float[])quad.Positions.Clone() };
		broken.Positions[4] = float.NaN;
		StringAssert.Contains( Refused( glb.Build( glb.AddNode( "m", glb.AddMesh( "m", broken ) ) ) ).Message, "not finite" );

	}

	[TestMethod]
	public void ASceneWithoutNodesOrAMeshWithoutPrimitivesIsRefused()
	{
		var empty = new GlbBuilder();
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => Convert( empty.Build() ) ).Message, "no nodes" );
		var glb = new GlbBuilder();
		var mesh = glb.AddMesh( "empty" );
		StringAssert.Contains( Refused( glb.Build( glb.AddNode( "m", mesh ) ) ).Message, "no primitives" );
	}

	// ---- The builder's self-lit flag ---------------------------------------------------------------------------

	[TestMethod]
	public void ModelBuilderWritesTheSelfLitMaterialWord()
	{
		var builder = new ModelBuilder();
		var node = builder.AddMesh( "m" );
		var lit = node.Mesh!.AddMaterial( "lit_tex" );
		lit.SelfLit = true;
		var a = lit.AddVertex( new NVector3( 0, 0, 0 ), NVector3.UnitY, System.Numerics.Vector2.Zero );
		var b = lit.AddVertex( new NVector3( 1, 0, 0 ), NVector3.UnitY, System.Numerics.Vector2.UnitX );
		var c = lit.AddVertex( new NVector3( 0, 0, 1 ), NVector3.UnitY, System.Numerics.Vector2.UnitY );
		lit.AddTriangle( a, b, c );
		var plain = node.Mesh.AddMaterial( "plain_tex" );
		var d = plain.AddVertex( new NVector3( 0, 1, 0 ), NVector3.UnitY, System.Numerics.Vector2.Zero );
		var e = plain.AddVertex( new NVector3( 1, 1, 0 ), NVector3.UnitY, System.Numerics.Vector2.UnitX );
		var f = plain.AddVertex( new NVector3( 0, 1, 1 ), NVector3.UnitY, System.Numerics.Vector2.UnitY );
		plain.AddTriangle( d, e, f );
		var materials = builder.Build().Meshes.Single().Materials;
		CollectionAssert.AreEqual( new uint[] { 1, 0 }, materials.Select( material => material.Pad ).ToArray() );
		// the written bytes carry it at +0xC of the material record: round-trips through the writer
		var again = ModelFileWriter.Write( builder.Build() );
		CollectionAssert.AreEqual( new uint[] { 1, 0 }, new ModelFile( new MemoryStream( again ) ).Meshes.Single().Materials.Select( material => material.Pad ).ToArray() );
	}
}
