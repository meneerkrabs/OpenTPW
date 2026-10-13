using System;
using System.Buffers.Binary;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Geometry table boundaries and allocation order over synthetic MD2 bytes.</summary>
[TestClass]
public class Md2BoundaryTests
{
	private static void U16( byte[] data, int offset, ushort value ) => BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( offset ), value );
	private static void U32( byte[] data, int offset, int value ) => BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( offset ), (uint)value );
	private static int ReadOffset( byte[] data, int offset ) => (int)BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset ) );
	private static ModelFile Read( byte[] data ) => new( new MemoryStream( data ) );

	internal static byte[] SharedPositionMeshes( ushort meshCount )
	{
		var original = Md2ModelFileTests.CreateGeometry();
		var originalMesh = original.AsSpan( ReadOffset( original, 0x70 ), ModelFile.MeshRecordBytes ).ToArray();
		var records = original.Length;
		var positions = records + meshCount * ModelFile.MeshRecordBytes;
		const ushort blocks = 65536 / 4; // Padding of the largest u16 vertex count.
		var data = new byte[positions + blocks * 48];
		original.CopyTo( data, 0 );
		U16( data, 0x38, blocks );
		U16( data, 0x3A, 0 ); // no UVs, materials or faces
		U16( data, 0x3C, 0 );
		U16( data, 0x3E, 0 );
		U16( data, 0x42, meshCount );
		U16( data, 0x44, meshCount );
		U16( data, 0x48, 0 ); // no attributes
		U16( data, 0x4A, 0 ); // no normals
		U32( data, 0x58, positions );
		U32( data, 0x70, records );
		U32( data, 0x74, 0 ); // all nodes are meshes
		U32( data, 0x78, records );
		for ( var index = 0; index < meshCount; index++ )
		{
			var record = records + index * ModelFile.MeshRecordBytes;
			originalMesh.CopyTo( data, record );
			U32( data, record + 4, index == 0 ? 0 : records );
			U32( data, record + 8, index > 0 && index + 1 < meshCount ? record + ModelFile.MeshRecordBytes : 0 );
			U32( data, record + 12, index == 0 && meshCount > 1 ? record + ModelFile.MeshRecordBytes : 0 );
			U16( data, record + 88, ushort.MaxValue );
			U16( data, record + 90, 0 );
			U16( data, record + 92, 0 );
			U16( data, record + 94, 0 );
			U32( data, record + 96, positions );
		}
		return data;
	}

	[TestMethod]
	public void ValidMaximumPerMeshVertexCountStillReads()
	{
		var model = Read( SharedPositionMeshes( 1 ) );
		Assert.AreEqual( 1, model.Meshes.Count );
		Assert.AreEqual( ushort.MaxValue, model.Meshes[0].Positions.Length );
		Assert.AreEqual( 0, model.Meshes[0].Indices.Length );
	}

	[TestMethod]
	public void ExcessSharedMeshCountsRejectBeforeRepeatedLargeAllocations()
	{
		var data = SharedPositionMeshes( 32 );
		Assert.ThrowsException<InvalidDataException>( () => Read( data ) );
		var before = GC.GetAllocatedBytesForCurrentThread();
		Assert.ThrowsException<InvalidDataException>( () => Read( data ) );
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Console.WriteLine( $"Aliased mesh rejection allocated {allocated} bytes." );
		Assert.IsTrue( allocated < 8 * 1024 * 1024, $"header-total mismatch allocated repeated positions: {allocated} bytes" );
	}
	[TestMethod]
	public void EveryTruncatedGeometryPrefixReportsMalformedData()
	{
		var valid = Md2ModelFileTests.CreateGeometry();
		for ( var length = 0; length < valid.Length; length++ )
		{
			var prefix = length;
			Assert.ThrowsException<InvalidDataException>( () => new ModelFile( new MemoryStream( valid, 0, prefix ) ), $"prefix {prefix}" );
		}
	}

	[DataTestMethod]
	[DataRow( 0x36 )]
	[DataRow( 0x38 )]
	[DataRow( 0x3A )]
	[DataRow( 0x3C )]
	[DataRow( 0x3E )]
	[DataRow( 0x40 )]
	[DataRow( 0x42 )]
	[DataRow( 0x44 )]
	[DataRow( 0x48 )]
	[DataRow( 0x4A )]
	public void MaximumDeclaredCountsCannotEscapeSmallFileBoundaries( int field )
	{
		var data = Md2ModelFileTests.CreateGeometry();
		U16( data, field, ushort.MaxValue );
		Assert.ThrowsException<InvalidDataException>( () => Read( data ) );
	}

}
