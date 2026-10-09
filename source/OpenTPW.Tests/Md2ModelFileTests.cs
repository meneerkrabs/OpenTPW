using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class Md2ModelFileTests
{
	// Synthetic layout: one textured quad mesh (root) with one dummy child and one dummy attribute.
	private const int FlagTable = 0xB8;
	private const int NameTable = 0xC0;
	private const int SlotTable = 0xD4;
	private const int Positions = 0xE4;
	private const int Normals = 0x114;
	private const int Uvs = 0x15C;
	private const int Materials = 0x17C;
	private const int Faces = 0x18C;
	private const int Order = 0x19C;
	private const int MeshName = 0x1A4;
	private const int DummyName = 0x1A9;
	private const int Attributes = 0x1B0;
	private const int MeshTable = 0x1C4;
	private const int DummyTable = 0x264;
	private const int FileLength = 0x2BC;

	internal static byte[] CreateGeometry()
	{
		var data = new byte[FileLength];
		WriteHeader( data, 221, 203 );
		ushort[] counts = { 1, 1, 1, 1, 2, 0, 2, 1, 1, 1, 6 };
		for ( var index = 0; index < counts.Length; index++ )
			W16( data, 0x36 + index * 2, counts[index] );
		int[] offsets = { FlagTable, SlotTable, Positions, Normals, Uvs, Materials, Faces, 0, MeshTable, DummyTable, MeshTable, Attributes };
		for ( var index = 0; index < offsets.Length; index++ )
			W32( data, 0x50 + index * 4, (uint)offsets[index] );
		WF( data, 0x80, -1, -2, -3, 4, 5, 6 );

		W32( data, FlagTable, 0x31 );
		Encoding.ASCII.GetBytes( "tex_a.tga" ).CopyTo( data, NameTable );
		W16( data, SlotTable + 10, 1 );
		W32( data, SlotTable + 12, NameTable );

		// Positions (0,0,0) (1,0,0) (1,1,0) (0,1,0) as one X×4/Y×4/Z×4 block.
		WF( data, Positions, 0, 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 0 );
		for ( var index = 0; index < 4; index++ )
			WF( data, Normals + index * 12, 0, 0, 1 );
		WF( data, Normals + 48, 0, 0, 1, 0, 0, -1 );
		WF( data, Uvs, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f );
		W32( data, Materials, FlagTable );
		W16( data, Materials + 6, 2 );
		W16( data, Materials + 8, 0 );
		W16( data, Materials + 10, 3 );
		ushort[] faces = { 4, 0, 1, 2, 5, 0, 2, 3 };
		for ( var index = 0; index < faces.Length; index++ )
			W16( data, Faces + index * 2, faces[index] );
		ushort[] order = { 1, 2, 3, 0 };
		for ( var index = 0; index < order.Length; index++ )
			W16( data, Order + index * 2, order[index] );
		Encoding.ASCII.GetBytes( "quad\0dummy\0" ).CopyTo( data, MeshName );
		W32( data, Attributes, 0x100000B1 );
		W32( data, Attributes + 4, 7 );

		W32( data, MeshTable, 1 );
		W32( data, MeshTable + 12, DummyTable );
		WriteIdentity( data, MeshTable + 16, 1, 2, 3 );
		W32( data, MeshTable + 84, MeshName );
		W16( data, MeshTable + 88, 4 );
		W16( data, MeshTable + 90, 1 );
		W16( data, MeshTable + 92, 2 );
		W16( data, MeshTable + 94, 4 );
		int[] meshOffsets = { Positions, Normals, Uvs, Materials, Faces };
		for ( var index = 0; index < meshOffsets.Length; index++ )
			W32( data, MeshTable + 96 + index * 4, (uint)meshOffsets[index] );
		WF( data, MeshTable + 120, 0, 0, 0, 1, 1, 0 );
		W32( data, MeshTable + 148, Order );

		W32( data, DummyTable, 0x200 );
		W32( data, DummyTable + 4, MeshTable );
		WriteIdentity( data, DummyTable + 16, 0, 0, 5 );
		W32( data, DummyTable + 80, 1 );
		W32( data, DummyTable + 84, DummyName );
		return data;
	}

	private static byte[] CreateAnimation()
	{
		var data = new byte[0xB8 + 8 + 72];
		WriteHeader( data, 221, 203 );
		W16( data, 0x42, 2 );
		W32( data, 0x98, 0xC0 );
		W32( data, 0xC0, 4 );
		W32( data, 0xC0 + 8, 430 );
		W32( data, 0xC0 + 32, 0xB8 );
		return data;
	}

	private static void WriteHeader( byte[] data, uint major, uint minor )
	{
		W32( data, 0, ModelFile.Magic );
		W32( data, 4, major );
		W32( data, 8, minor );
		Encoding.ASCII.GetBytes( "synth.MD2" ).CopyTo( data, 0x18 );
		W32( data, 0x30, 1 );
	}

	private static void WriteIdentity( byte[] data, int offset, float x, float y, float z ) =>
		WF( data, offset, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1 );

	private static void W16( byte[] data, int offset, ushort value ) => BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( offset ), value );
	private static void W32( byte[] data, int offset, uint value ) => BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( offset ), value );
	private static void W32( byte[] data, int offset, int value ) => W32( data, offset, (uint)value );

	private static void WF( byte[] data, int offset, params float[] values )
	{
		for ( var index = 0; index < values.Length; index++ )
			BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( offset + index * 4 ), values[index] );
	}

	private static ModelFile Read( byte[] data ) => new( new MemoryStream( data ) );
	private static void Rejects( byte[] data ) => Assert.ThrowsException<InvalidDataException>( () => Read( data ) );

	[TestMethod]
	public void DecodesSyntheticGeometryLayout()
	{
		using var stream = new MemoryStream( CreateGeometry() );
		var model = new ModelFile( stream );
		Assert.IsTrue( stream.CanRead );
		Assert.AreEqual( ModelFileKind.Geometry, model.Kind );
		Assert.AreEqual( "synth.MD2", model.SourceName );
		Assert.AreEqual( 1u, model.HeaderFlags );
		Assert.AreEqual( new Vector3( -1, -2, -3 ), model.BoundsMin );
		Assert.AreEqual( new Vector3( 4, 5, 6 ), model.BoundsMax );
		Assert.IsNull( model.Animation );

		var texture = model.Textures.Single();
		Assert.AreEqual( 0x31u, texture.Flags );
		CollectionAssert.AreEqual( new[] { "tex_a.tga" }, texture.FrameNames.ToArray() );

		Assert.AreEqual( 2, model.Nodes.Count );
		Assert.AreEqual( 0, model.RootNodeIndex );
		var dummy = model.Nodes[1];
		Assert.AreEqual( "dummy", dummy.Name );
		Assert.AreEqual( 0x200u, dummy.Type );
		Assert.AreEqual( 0, dummy.ParentIndex );
		Assert.AreEqual( -1, dummy.MeshIndex );
		Assert.AreEqual( 5f, dummy.Transform.M43 );
		var attribute = model.DummyAttributes.Single();
		Assert.AreEqual( 0x100000B1u, attribute.Type );
		Assert.AreEqual( 7u, attribute.Value );

		var mesh = model.Meshes.Single();
		Assert.AreEqual( "quad", mesh.Name );
		Assert.AreEqual( 1u, mesh.NodeType );
		Assert.AreEqual( 1f, mesh.TransformMatrix.M41 );
		Assert.AreEqual( 3f, mesh.TransformMatrix.M43 );
		Assert.AreEqual( 4, mesh.Positions.Length );
		Assert.AreEqual( new Vector3( 1, 1, 0 ), mesh.Positions[2] );
		CollectionAssert.AreEqual( new ushort[] { 1, 2, 3, 0 }, mesh.CornerPositionIndices );
		CollectionAssert.AreEqual( new[] { new Vector3( 1, 0, 0 ), new Vector3( 1, 1, 0 ), new Vector3( 0, 1, 0 ), new Vector3( 0, 0, 0 ) }, mesh.Vertices.Select( vertex => vertex.Position ).ToArray() );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 0, 2, 3 }, mesh.Indices );
		CollectionAssert.AreEqual( new ushort[] { 4, 5 }, mesh.FaceNormalIndices );
		CollectionAssert.AreEqual( new[] { new Vector2( 0.1f, 0.5f ), new Vector2( 0.2f, 0.6f ), new Vector2( 0.3f, 0.7f ), new Vector2( 0.4f, 0.8f ) }, mesh.TexCoords );
		Assert.AreEqual( 4, mesh.CornerNormals.Length );
		CollectionAssert.AreEqual( new[] { new Vector3( 0, 0, 1 ), new Vector3( 0, 0, -1 ) }, mesh.FaceNormals );
		Assert.AreEqual( mesh.Vertices.Length, mesh.Normals.Length );
		Assert.AreEqual( new Vector3( 1, 1, 0 ), mesh.BoundsMax );

		var material = mesh.Materials.Single();
		Assert.AreEqual( 0, material.TextureIndex );
		Assert.AreEqual( "tex_a", material.Name );
		Assert.AreEqual( 0x31u, material.Flags );
		Assert.AreEqual( (ushort)2, material.b );
		Assert.IsTrue( mesh.Vertices.All( vertex => vertex.TextureIndex == 0 ) );
	}

	[TestMethod]
	public void DecodesEmptyAnimationContainerTrailer()
	{
		var model = Read( CreateAnimation() );
		Assert.AreEqual( ModelFileKind.Animation, model.Kind );
		Assert.AreEqual( 0, model.Meshes.Count );
		Assert.AreEqual( 0, model.Nodes.Count );
		Assert.AreEqual( (ushort)2, model.Counts.NodeCount );
		Assert.AreEqual( 0xC0, model.Animation!.Offset );
		Assert.AreEqual( 18, model.Animation.Words.Count );
		Assert.AreEqual( 430u, model.Animation.Words[2] );
		Assert.AreEqual( 430, model.Clip!.Duration );
		Assert.AreEqual( 0, model.Clip.Tracks.Count );
	}

	[TestMethod]
	public void RendererNormalsUseVerifiedStoredCornerNormals()
	{
		var data = CreateGeometry();
		WF( data, Normals + 12, 0.6f, 0, 0.8f ); // unit, same hemisphere as its faces: kept
		WF( data, Normals + 24, 0, 0, -1 ); // opposes its faces: recomputed
		WF( data, Normals + 36, 0, 0, 2 ); // not unit length: recomputed
		var mesh = Read( data ).Meshes.Single();
		Assert.AreEqual( new Vector3( 0, 0, 1 ), mesh.Normals[0] );
		Assert.AreEqual( new Vector3( 0.6f, 0, 0.8f ), mesh.Normals[1] );
		Assert.AreEqual( new Vector3( 0, 0, 1 ), mesh.Normals[2] );
		Assert.AreEqual( new Vector3( 0, 0, 1 ), mesh.Normals[3] );
		Assert.AreEqual( new Vector3( 0, 0, -1 ), mesh.CornerNormals[2] );
	}

	[TestMethod]
	public void AcceptsMaterialWithoutTexturePointer()
	{
		var data = CreateGeometry();
		W32( data, Materials, 0 );
		var material = Read( data ).Meshes.Single().Materials.Single();
		Assert.AreEqual( -1, material.TextureIndex );
		Assert.AreEqual( "", material.Name );
		Assert.AreEqual( 0u, material.Flags );
	}

	[TestMethod]
	public void ReadsNonseekableShortReadsWithoutClosingInput()
	{
		using var stream = new ShortReadStream( CreateGeometry() );
		Assert.AreEqual( 1, new ModelFile( stream ).Meshes.Count );
		Assert.IsTrue( stream.CanRead );
	}

	[TestMethod]
	public void RejectsOversizedInputWithoutClosingIt()
	{
		using var stream = new MemoryStream( new byte[ModelFile.MaximumFileBytes + 1] );
		Assert.ThrowsException<InvalidDataException>( () => new ModelFile( stream ) );
		Assert.IsTrue( stream.CanRead );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 4 )]
	[DataRow( 0xB7 )]
	public void RejectsTruncatedHeader( int length )
	{
		var data = CreateGeometry();
		Array.Resize( ref data, length );
		Rejects( data );
	}

	[DataTestMethod]
	[DataRow( 0x200 )]
	[DataRow( 0x2BB )]
	public void RejectsTruncatedBody( int length )
	{
		var data = CreateGeometry();
		Array.Resize( ref data, length );
		Rejects( data );
	}

	[TestMethod]
	public void RejectsBadMagicAndUnsupportedVersion()
	{
		var data = CreateGeometry();
		data[0] ^= 1;
		Rejects( data );
		data = CreateGeometry();
		W32( data, 4, 207 );
		W32( data, 8, 201 );
		Assert.ThrowsException<NotSupportedException>( () => Read( data ) );
	}

	[DataTestMethod]
	[DataRow( 0x50 )]
	[DataRow( 0x54 )]
	[DataRow( 0x58 )]
	[DataRow( 0x5C )]
	[DataRow( 0x60 )]
	[DataRow( 0x64 )]
	[DataRow( 0x68 )]
	[DataRow( 0x6C )]
	[DataRow( 0x70 )]
	[DataRow( 0x74 )]
	[DataRow( 0x78 )]
	[DataRow( 0x7C )]
	[DataRow( MeshTable + 84 )]
	[DataRow( MeshTable + 96 )]
	[DataRow( MeshTable + 100 )]
	[DataRow( MeshTable + 104 )]
	[DataRow( MeshTable + 108 )]
	[DataRow( MeshTable + 112 )]
	[DataRow( MeshTable + 148 )]
	[DataRow( SlotTable + 12 )]
	public void RejectsPointersOutsideTheFileOrHeader( int field )
	{
		var data = CreateGeometry();
		W32( data, field, 0xFFFFFFF0 );
		Rejects( data );
		W32( data, field, 0x10 );
		Rejects( data );
	}

	[TestMethod]
	public void RejectsMissingCornerPositionAndMaterialReferences()
	{
		var data = CreateGeometry();
		W16( data, Faces + 6, 4 );
		Rejects( data );
		data = CreateGeometry();
		W16( data, Order + 2, 4 );
		Rejects( data );
		data = CreateGeometry();
		W32( data, Materials, FlagTable + 4 );
		Rejects( data );
		data = CreateGeometry();
		W32( data, Materials, FlagTable + 8 );
		Rejects( data );
		data = CreateGeometry();
		W16( data, Materials + 10, 4 );
		Rejects( data );
		data = CreateGeometry();
		W16( data, Materials + 8, 3 );
		W16( data, Materials + 10, 2 );
		Rejects( data );
	}

	[TestMethod]
	public void RejectsMalformedHierarchy()
	{
		var data = CreateGeometry();
		W32( data, DummyTable + 12, MeshTable );
		Rejects( data ); // cycle back to the root
		data = CreateGeometry();
		W32( data, DummyTable + 4, 0 );
		Rejects( data ); // parent pointer disagrees with child link
		data = CreateGeometry();
		W32( data, MeshTable + 12, 0 );
		Rejects( data ); // dummy unreachable
		data = CreateGeometry();
		W32( data, 0x78, DummyTable );
		Rejects( data ); // root has a parent
		data = CreateGeometry();
		W32( data, DummyTable + 8, DummyTable + 4 );
		Rejects( data ); // sibling points inside a record
		data = CreateGeometry();
		W16( data, 0x42, 0 );
		Rejects( data );
	}

	[DataTestMethod]
	[DataRow( 0x38 )]
	[DataRow( 0x3A )]
	[DataRow( 0x3C )]
	[DataRow( 0x3E )]
	[DataRow( 0x4A )]
	public void RejectsHeaderTotalsThatDisagreeWithMeshes( int field )
	{
		var data = CreateGeometry();
		W16( data, field, (ushort)(BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( field ) ) + 1) );
		Rejects( data );
	}

	[TestMethod]
	public void RejectsInvalidTextureAndNodeNames()
	{
		var data = CreateGeometry();
		W16( data, SlotTable + 10, 0 );
		Rejects( data ); // zero frames
		data = CreateGeometry();
		W16( data, SlotTable + 10, 2 );
		Rejects( data ); // frame list overlaps the slot table
		data = CreateGeometry();
		Encoding.ASCII.GetBytes( "abcdefghijklmnopqrst" ).CopyTo( data, NameTable );
		Rejects( data ); // fixed name without terminator
		data = CreateGeometry();
		data[NameTable] = 0x07;
		Rejects( data );
		data = CreateGeometry();
		data[MeshName] = 0xFF;
		Rejects( data );
		data = CreateGeometry();
		W32( data, DummyTable + 84, FileLength - 1 );
		data[FileLength - 1] = (byte)'x';
		Rejects( data ); // node name runs off the end
	}

	[TestMethod]
	public void RejectsInvalidAnimationTrailer()
	{
		var data = CreateAnimation();
		W32( data, 0x98, 0xBC );
		Rejects( data );
		data = CreateAnimation();
		W32( data, 0xC0 + 32, 0xC0 );
		Rejects( data );
		data = CreateAnimation();
		W32( data, 0xC0 + 60, 0x10 );
		Rejects( data );
		data = CreateAnimation();
		Array.Resize( ref data, 0xB8 + 71 );
		Rejects( data );
	}

	private sealed class ShortReadStream : MemoryStream
	{
		public ShortReadStream( byte[] data ) : base( data ) { }
		public override bool CanSeek => false;
		public override long Length => throw new NotSupportedException();
		public override int Read( byte[] readBuffer, int offset, int count ) => base.Read( readBuffer, offset, Math.Min( count, 5 ) );
	}
}
