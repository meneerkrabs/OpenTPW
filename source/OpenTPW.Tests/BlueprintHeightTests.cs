using System;
using System.IO;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class BlueprintHeightTests
{
	[DataTestMethod]
	[DataRow(-29f, -20f)] [DataRow(-20f, -10f)] [DataRow(-19f, -10f)]
	[DataRow(-18.9f, 0f)] [DataRow(-10f, 0f)] [DataRow(-0.9f, 0f)]
	[DataRow(0f, 0f)] [DataRow(0.9f, 0f)] [DataRow(1f, 10f)]
	[DataRow(10.9f, 10f)] [DataRow(11f, 20f)]
	public void QuantizationTruncatesTwiceRatherThanCeiling( float height, float expected ) =>
		Assert.AreEqual( expected, BlueprintHeights.Quantize( height ) );

	[TestMethod]
	public void RotatedNonSquareMapSamplesPreserveBaseHeightBeforeQuantization()
	{
		const int width = 2, height = 3, profile = width * 5 * height * 5, count = width * height;
		var data = new byte[48 + profile + count * 2];
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 0 ), 3 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 2 ), 0xAB1E );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 4 ), 5 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 6 ), 100 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 8 ), width );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 10 ), height );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 12 ), 48 );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 16 ), 48 + profile );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 20 ), 48 + profile + count );
		for ( var i = 0; i < count; i++ ) data[48 + profile + i] = (byte)(i * 40);
		var map = new HmpFile( new MemoryStream( data ) );
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var placement = new ObjectPlacement( 12, 20, rotation, System.Numerics.Vector2.Zero, 7 * ObjectPlacement.ModelScale );
			var (ox, oz) = ObjectHeightMaps.InstanceOrigin( placement );
			for ( var row = 0; row < height; row++ )
				for ( var column = 0; column < width; column++ )
				{
					var engine = System.Numerics.Vector3.Transform( new System.Numerics.Vector3( column * 10 + 5, row * 10 + 5, 0 ), placement.ModelToEngine );
					var x = (int)MathF.Floor( engine.X / ObjectPlacement.CellSize );
					var y = (int)MathF.Floor( engine.Y / ObjectPlacement.CellSize );
					var index = ObjectHeightMaps.CellIndex( map, rotation, ox, oz, x, y );
					Assert.AreEqual( row * width + column, index );
					var h = map.CellMaxima.Span[index!.Value] * ObjectHeightMaps.ByteScale + placement.BaseHeight / ObjectPlacement.ModelScale;
					Assert.AreEqual( new float[] { 10, 30, 40, 60, 70, 90 }[row * width + column], BlueprintHeights.Quantize( h ) );
				}
		}
	}

	[TestMethod]
	public void NonfiniteAndOverflowInputsAreUnresolved()
	{
		foreach ( var height in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue } )
			Assert.IsNull( BlueprintHeights.Quantize( height ) );
	}

	private static ModelHeightfield Field() => new( 3, 3, 10, 10, 0, 0, Array.Empty<uint>(), Enumerable.Range( 0, 16 ).Select( i => i * 2f ).ToArray(), new uint[9] );

	[TestMethod]
	public void RaisedTopAndAllFourSideStacksKeepTerrainAndRipple()
	{
		var field = Field();
		var cells = new[] { (1, 1, BlueprintMarker.Front) };
		var offsets = BlueprintMarkers.CaptureHeights( field, cells, ( x, y ) => x == 1 && y == 1 ? 30 : 10 );
		var (vertices, indices) = BlueprintMarkers.BuildGeometry( field, cells, new Dictionary<(int, int), byte> { [(1, 1)] = 0x40 }, 0.7f, offsets );
		Assert.AreEqual( 9 * 8, vertices.Length, "one top + two steps on each of four non-preview neighbours, each double-sided" );
		Assert.AreEqual( 9 * 12, indices.Length );
		Assert.IsTrue( vertices.Take( 8 ).All( v => v.TexIndex == 6 ) );
		Assert.IsTrue( vertices.Skip( 8 ).All( v => v.TexIndex == 0 ), "arrows do not spread onto side walls" );
		var origin = OriginalParkPlacement.GetOrigin( field );
		for ( var side = 0; side < 4; side++ )
			for ( var step = 0; step < 2; step++ )
			{
				var face = vertices.Skip( 8 + (side * 2 + step) * 8 ).Take( 4 ).ToArray();
				for ( var edge = 0; edge < 2; edge++ )
				{
					var bottom = face[edge].Position;
					var top = face[edge + 2].Position;
					Assert.AreEqual( BlueprintMarkers.FaceSize * OriginalParkPlacement.ModelScale, top.Z - bottom.Z, 1e-4f );
					var cx = (int)MathF.Round( (bottom.X + origin.X) / (10 * OriginalParkPlacement.ModelScale) );
					var cy = (int)MathF.Round( (bottom.Y + origin.Y) / (10 * OriginalParkPlacement.ModelScale) );
					var expected = (10 + step * 10 + BlueprintMarkers.VertexHeight( field, cx, cy, 0.7f )) * OriginalParkPlacement.ModelScale;
					Assert.AreEqual( expected, bottom.Z, 1e-4f );
				}
			}
	}

	[TestMethod]
	public void EqualHigherAndUnknownNeighboursDoNotMakeWalls()
	{
		var field = Field();
		var cells = new[] { (1, 1, BlueprintMarker.Allowed) };
		var offsets = new Dictionary<(int, int), float?> { [(1,1)] = 20, [(1,0)] = 20, [(2,1)] = 30, [(1,2)] = null };
		Assert.AreEqual( 8, BlueprintMarkers.BuildGeometry( field, cells, heights: offsets ).Vertices.Length );
		var plain = BlueprintMarkers.CaptureHeights( field, cells, ( x, y ) => 0 );
		Assert.AreEqual( 8, BlueprintMarkers.BuildGeometry( field, cells, heights: plain ).Vertices.Length, "terrain slope does not create wall steps" );
	}

	[DataTestMethod]
	[DataRow(BlueprintMarker.Refused, 1)] [DataRow(BlueprintMarker.Area, 2)] [DataRow(BlueprintMarker.Enter, 0)]
	public void WallTexturesFollowRefusalAndAreaNotArrowDirection( BlueprintMarker marker, int slot )
	{
		var field = Field();
		var offsets = new Dictionary<(int,int), float?> { [(1,1)] = 0, [(1,0)] = -10 };
		var vertices = BlueprintMarkers.BuildGeometry( field, new[] { (1,1,marker) }, heights: offsets ).Vertices;
		Assert.AreEqual( 16, vertices.Length );
		Assert.IsTrue( vertices.Skip( 8 ).All( v => v.TexIndex == slot ) );
		Assert.AreEqual( new Vector2( 0, 0 ), vertices[8].TexCoords );
	}

	[TestMethod]
	public void CaptureSkipsOutsideAndChangesWhenOccupancyChanges()
	{
		var field = Field();
		var cells = new[] { (0,0,BlueprintMarker.Allowed) };
		var calls = 0;
		var before = BlueprintMarkers.CaptureHeights( field, cells, ( x, y ) => { calls++; return 0; } );
		Assert.AreEqual( 3, calls );
		Assert.IsNull( before[(-1,0)] );
		Assert.IsNull( before[(0,-1)] );
		var after = BlueprintMarkers.CaptureHeights( field, cells, ( x, y ) => x == 0 && y == 0 ? 20 : 0 );
		Assert.IsFalse( before.SequenceEqual( after ) );
		Assert.AreEqual( 8, BlueprintMarkers.BuildGeometry( field, cells, heights: before ).Vertices.Length );
		Assert.AreEqual( 40, BlueprintMarkers.BuildGeometry( field, cells, heights: after ).Vertices.Length );
	}

	[TestMethod]
	public void MapTypesOfObjectsNoTerrainAndWalkwaysTakeTheQuantizedGround()
	{
		var cells = new ParkCellMap( 3, 3 );
		foreach ( byte type in new byte[] { 4, 9, 10, 7, 30 } )
		{
			cells.SetType( 1, 1, type );
			Assert.AreEqual( 20f, BlueprintHeights.Sample( cells, 1, 1, ( x, y ) => 11 ), $"type {type}" );
		}
		foreach ( byte type in new byte[] { 0, 1, 2, 3, 0x15, 64 } )
		{
			cells.SetType( 1, 1, type );
			Assert.AreEqual( 0f, BlueprintHeights.Sample( cells, 1, 1, ( x, y ) => throw new Exception( "a plain cell reads no ground" ) ), $"type {type}" );
		}
	}

	[TestMethod]
	public void SecondLayerRecordsOfTracksAndPostsRaiseButStationsDoNot()
	{
		var cells = new ParkCellMap( 3, 3 );
		foreach ( byte record in new byte[] { TrackCellTypes.KartOrigin, TrackCellTypes.Extension, TrackCellTypes.WaterOrigin, TrackCellTypes.WaterAlternate } )
		{
			cells.Tracks.Set( 1, 1, new TrackCell { Type = record } );
			Assert.AreEqual( 20f, BlueprintHeights.Sample( cells, 1, 1, ( x, y ) => 11 ), $"record {record}" );
		}
		foreach ( byte record in new byte[] { TrackCellTypes.KartStart, TrackCellTypes.WaterStart } )
		{
			cells.Tracks.Set( 1, 1, new TrackCell { Type = record } );
			Assert.AreEqual( 0f, BlueprintHeights.Sample( cells, 1, 1, ( x, y ) => throw new Exception( "a station record reads no ground" ) ), $"record {record}" );
		}
	}

	[TestMethod]
	public void TheMapTypeOverwritesTheSecondLayerRaiseWithTheSameGround()
	{
		var cells = new ParkCellMap( 3, 3 );
		cells.Tracks.Set( 1, 1, new TrackCell { Type = TrackCellTypes.KartOrigin } );
		cells.SetType( 1, 1, (byte)4 );
		var reads = 0;
		Assert.AreEqual( 30f, BlueprintHeights.Sample( cells, 1, 1, ( x, y ) => { reads++; return 21.5f; } ) );
		Assert.AreEqual( 2, reads, "the record and the map type each read the ground; the second write replaces the first" );
	}

	[TestMethod]
	public void CellsOutsideTheMapAndUnnumberedGroundHaveNoRaise()
	{
		var cells = new ParkCellMap( 3, 3 );
		cells.SetType( 1, 1, (byte)4 );
		Assert.IsNull( BlueprintHeights.Sample( cells, -1, 1, ( x, y ) => 11 ) );
		Assert.IsNull( BlueprintHeights.Sample( cells, 3, 1, ( x, y ) => 11 ) );
		Assert.IsNull( BlueprintHeights.Sample( cells, 1, 1, ( x, y ) => float.NaN ) );
	}
}
