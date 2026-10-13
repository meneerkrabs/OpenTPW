using System;
using System.Buffers.Binary;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class Md2AllocationReviewTests
{
	private static void U16( byte[] bytes, int offset, ushort value ) => BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( offset ), value );
	private static void U32( byte[] bytes, int offset, int value ) => BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( offset ), (uint)value );
	private static ModelFile Read( byte[] bytes ) => new( new MemoryStream( bytes ) );

	private static byte[] SharedIndexedMeshes()
	{
		var original = Md2ModelFileTests.CreateGeometry();
		var oldRecord = (int)BinaryPrimitives.ReadUInt32LittleEndian( original.AsSpan( 0x70 ) );
		var records = original.Length;
		var bytes = new byte[records + 2 * ModelFile.MeshRecordBytes];
		original.CopyTo( bytes, 0 );
		// Header totals count both decoded meshes. Their physical position/UV/normal/material/face
		// spans may alias; the existing reader does not require nonoverlapping source spans.
		foreach ( var offset in new[] { 0x38, 0x3A, 0x3C, 0x3E, 0x4A } )
			U16( bytes, offset, (ushort)(2 * BinaryPrimitives.ReadUInt16LittleEndian( original.AsSpan( offset ) )) );
		U16( bytes, 0x42, 2 ); U16( bytes, 0x44, 2 ); U16( bytes, 0x48, 0 );
		U32( bytes, 0x70, records ); U32( bytes, 0x74, 0 ); U32( bytes, 0x78, records );
		for ( var mesh = 0; mesh < 2; mesh++ )
		{
			var record = records + mesh * ModelFile.MeshRecordBytes;
			original.AsSpan( oldRecord, ModelFile.MeshRecordBytes ).CopyTo( bytes.AsSpan( record ) );
			U32( bytes, record + 4, mesh == 0 ? 0 : records );
			U32( bytes, record + 8, 0 );
			U32( bytes, record + 12, mesh == 0 ? records + ModelFile.MeshRecordBytes : 0 );
		}
		return bytes;
	}

	[TestMethod]
	public void SharedIndexedGeometryStillDecodesWhenAggregateTotalsAgree()
	{
		var expected = Read( Md2ModelFileTests.CreateGeometry() ).Meshes[0];
		var model = Read( SharedIndexedMeshes() );
		Assert.AreEqual( 2, model.Meshes.Count );
		foreach ( var mesh in model.Meshes )
		{
			CollectionAssert.AreEqual( expected.Positions, mesh.Positions );
			CollectionAssert.AreEqual( expected.TexCoords, mesh.TexCoords );
			CollectionAssert.AreEqual( expected.Indices, mesh.Indices );
			CollectionAssert.AreEqual( expected.CornerPositionIndices, mesh.CornerPositionIndices );
			CollectionAssert.AreEqual( expected.Normals, mesh.Normals );
			Assert.AreEqual( expected.Materials[0].TextureIndex, mesh.Materials[0].TextureIndex );
		}
	}

	[TestMethod]
	public void TwoMaximumVertexMeshesRemainSupportedWithMatchingAggregateHeader()
	{
		var original = Md2BoundaryTests.SharedPositionMeshes( 2 );
		var positions = (int)BinaryPrimitives.ReadUInt32LittleEndian( original.AsSpan( 0x58 ) );
		const ushort blocks = 32768;
		Array.Resize( ref original, positions + blocks * 48 );
		U16( original, 0x38, blocks );
		var model = Read( original );
		Assert.AreEqual( 2, model.Meshes.Count );
		Assert.AreEqual( ushort.MaxValue, model.Meshes[0].Positions.Length );
		Assert.AreEqual( ushort.MaxValue, model.Meshes[1].Positions.Length );
	}

	[DataTestMethod]
	[DataRow( 0x38 )]
	[DataRow( 0x3A )]
	[DataRow( 0x3C )]
	[DataRow( 0x3E )]
	[DataRow( 0x4A )]
	public void EachMonotonicTotalRetainsItsHeaderConsistencyRefusal( int field )
	{
		var bytes = SharedIndexedMeshes();
		var total = BinaryPrimitives.ReadUInt16LittleEndian( bytes.AsSpan( field ) );
		// Each individual source span still fits; only the combined decoded total disagrees.
		U16( bytes, field, (ushort)(total - 1) );
		var exception = Assert.ThrowsException<InvalidDataException>( () => Read( bytes ) );
		StringAssert.Contains( exception.Message, "mesh totals disagree with the header counts" );
	}

	[DataTestMethod]
	[DataRow( 0x38 )]
	[DataRow( 0x3A )]
	[DataRow( 0x3C )]
	[DataRow( 0x3E )]
	[DataRow( 0x4A )]
	public void DeficientDecodedTotalsStillReachTheFinalEqualityCheck( int field )
	{
		var bytes = SharedIndexedMeshes();
		var total = BinaryPrimitives.ReadUInt16LittleEndian( bytes.AsSpan( field ) );
		U16( bytes, field, (ushort)(total + 1) );
		var exception = Assert.ThrowsException<InvalidDataException>( () => Read( bytes ) );
		StringAssert.Contains( exception.Message, "mesh totals disagree with the header counts" );
	}
}
