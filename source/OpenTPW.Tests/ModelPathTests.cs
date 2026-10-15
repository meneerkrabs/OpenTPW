using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

[TestClass]
public class ModelPathTests
{
	private static readonly NVector3[] SixPoints =
	{
		new( 0, 0, 0 ), new( 1, 0, 0 ), new( 2, 0, 0 ), new( 3, 0, 0 ), new( 3, 0, 3 ), new( 0, 0, 3 ),
	};

	private static NVector3 Bezier( NVector3 p0, NVector3 p1, NVector3 p2, NVector3 p3, float t )
	{
		var u = 1 - t;
		return u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
	}

	private static void AreClose( NVector3 expected, NVector3 actual, string what ) =>
		Assert.IsTrue( NVector3.Distance( expected, actual ) < 1e-4f, $"{what}: expected {expected}, got {actual}" );

	[TestMethod]
	public void ClosedBezierPathsWrapAndSpanAllSegments()
	{
		var path = new ModelPath( 3, SixPoints );
		Assert.AreEqual( 2, path.SegmentCount );
		Assert.IsTrue( path.TryEvaluate( 0, out var position, out var direction ) );
		AreClose( SixPoints[0], position, "0 %" );
		AreClose( NVector3.UnitX, direction!.Value, "direction at 0 %" );
		Assert.IsTrue( path.TryEvaluate( 25, out position, out _ ) );
		AreClose( Bezier( SixPoints[0], SixPoints[1], SixPoints[2], SixPoints[3], 0.5f ), position, "25 %" );
		// The second segment closes the loop through points 3, 4, 5 and 0.
		Assert.IsTrue( path.TryEvaluate( 75, out position, out _ ) );
		AreClose( Bezier( SixPoints[3], SixPoints[4], SixPoints[5], SixPoints[0], 0.5f ), position, "75 %" );
		// Values wrap modulo 100 (plus 1000 first, so −1 is 99 %).
		Assert.IsTrue( path.TryEvaluate( 100, out position, out _ ) );
		AreClose( SixPoints[0], position, "100 %" );
		Assert.IsTrue( path.TryEvaluate( -1, out position, out _ ) );
		Assert.IsTrue( path.TryEvaluate( 99, out var ninetyNine, out _ ) );
		AreClose( ninetyNine, position, "−1 %" );
	}

	[TestMethod]
	public void OpenPathsDropTheClosingSegment()
	{
		// Open: one Bézier segment (points 0 … 3); points 4 and 5 are never read.
		var bezier = new ModelPath( 2, SixPoints );
		Assert.AreEqual( 1, bezier.SegmentCount );
		Assert.IsTrue( bezier.TryEvaluate( 50, out var position, out _ ) );
		AreClose( Bezier( SixPoints[0], SixPoints[1], SixPoints[2], SixPoints[3], 0.5f ), position, "open Bézier" );

		var linear = new ModelPath( 8, new[] { NVector3.Zero, new NVector3( 2, 0, 0 ), new NVector3( 2, 0, 4 ) } );
		Assert.IsTrue( linear.IsLinear );
		Assert.AreEqual( 2, linear.SegmentCount );
		Assert.IsTrue( linear.TryEvaluate( 75, out position, out var direction ) );
		AreClose( new NVector3( 2, 0, 2 ), position, "linear" );
		AreClose( NVector3.UnitZ, direction!.Value, "linear direction" );

		Assert.IsFalse( new ModelPath( 2, Array.Empty<NVector3>() ).TryEvaluate( 10, out _, out _ ) );
		Assert.IsFalse( bezier.TryEvaluate( float.NaN, out _, out _ ) );
	}

	[TestMethod]
	public void CatmullRomPathsPassThroughTheirPointsAndLookAtFractionPlusATenth()
	{
		var points = new[] { new NVector3( 0, 0, 0 ), new NVector3( 4, 0, 0 ), new NVector3( 4, 0, 4 ), new NVector3( 0, 0, 4 ) };
		var path = new ModelPath( 1, points );
		Assert.AreEqual( 4, path.SegmentCount );
		Assert.IsTrue( path.TryEvaluate( 25, out var position, out var direction ) );
		AreClose( points[1], position, "25 %" );
		// The original normalises the position at fraction + 0.1 itself, not a difference.
		var b = points[1];
		var (a, c, d) = (points[0], points[2], points[3]);
		const float t = 0.1f;
		var ahead = 0.5f * (2 * b + t * ((c - a) + t * ((2 * a - 5 * b + 4 * c - d) + t * (d - a + 3 * (b - c)))));
		AreClose( NVector3.Normalize( ahead ), direction!.Value, "direction" );
	}

	[TestMethod]
	public void PathParametersBlendPerTickAndAcrossTheSeam()
	{
		var block = new ModelPathParameters( 0, 0, new[] { 10f, 20f, 95f, 5f } );
		Assert.AreEqual( 15f, block.Sample( 0.5f, false ) );
		// More than 50 apart: the smaller value gains 100 and the blend is taken modulo 100.
		Assert.AreEqual( 97.5f, block.Sample( 2.25f, false )!.Value, 1e-4f );
		Assert.AreEqual( 2.5f, block.Sample( 2.75f, false )!.Value, 1e-4f );
		// The last value holds on an open path and blends with the first on a closed one.
		Assert.AreEqual( 5f, block.Sample( 3.5f, false ) );
		Assert.AreEqual( 7.5f, block.Sample( 3.5f, true ) );
		// At local time = count the last pair is used with fraction 1; past it the node is left alone.
		Assert.AreEqual( 5f, block.Sample( 4f, false ) );
		Assert.IsNull( block.Sample( 4.5f, false ) );
		Assert.IsNull( block.Sample( -0.5f, false ) );
		Assert.IsNull( new ModelPathParameters( 2, 0, new[] { 1f } ).Sample( 1f, false ) );
	}

	[TestMethod]
	public void FacingAVerticalPathStaysFinite()
	{
		// up × direction is zero here; the original divides by it, OpenTPW picks +X made perpendicular instead.
		var rows = ModelPath.Facing( NVector3.UnitY, NVector3.UnitY );
		AreClose( NVector3.UnitX, new NVector3( rows.M11, rows.M12, rows.M13 ), "row 0" );
		AreClose( NVector3.UnitY, new NVector3( rows.M31, rows.M32, rows.M33 ), "row 2" );
		Assert.IsTrue( float.IsFinite( rows.M21 ) && float.IsFinite( rows.M22 ) && float.IsFinite( rows.M23 ) );
	}

	[TestMethod]
	public void FacingRowsAreOrthonormalWithRowTwoAlongThePath()
	{
		var direction = NVector3.Normalize( new NVector3( 1, 0.2f, 1 ) );
		var rows = ModelPath.Facing( direction, NVector3.UnitY );
		var right = new NVector3( rows.M11, rows.M12, rows.M13 );
		var up = new NVector3( rows.M21, rows.M22, rows.M23 );
		AreClose( direction, new NVector3( rows.M31, rows.M32, rows.M33 ), "row 2" );
		Assert.AreEqual( 1f, right.Length(), 1e-5f );
		Assert.AreEqual( 1f, up.Length(), 1e-5f );
		Assert.AreEqual( 0f, NVector3.Dot( right, up ), 1e-5f );
		Assert.AreEqual( 0f, NVector3.Dot( right, direction ), 1e-5f );
		Assert.IsTrue( up.Y > 0.9f, "row 1 stays near the given up" );
	}
}

[TestClass]
public class ModelFilePathTableTests
{
	/// <summary>The synthetic geometry with a path table of <paramref name="counts"/>.Length records after the file, each with its own points.</summary>
	private static byte[] WithPaths( params ushort[] counts )
	{
		var geometry = Md2ModelFileTests.CreateGeometry();
		var table = geometry.Length;
		var points = table + counts.Length * 16;
		var data = new byte[points + counts.Sum( count => count * 12 )];
		geometry.CopyTo( data, 0 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 0x40 ), (ushort)counts.Length );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 0xAC ), (uint)table );
		for ( var index = 0; index < counts.Length; index++ )
		{
			var record = table + index * 16;
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( record ), 3 );
			System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( record + 4 ), counts[index] );
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( record + 8 ), (uint)points );
			for ( var value = 0; value < counts[index] * 3; value++ )
				System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( points + value * 4 ), value );
			points += counts[index] * 12;
		}
		return data;
	}

	private static ModelFile Read( byte[] data ) => new( new System.IO.MemoryStream( data ) );

	[TestMethod]
	public void PathRecordsDecodeTheirPoints()
	{
		var model = Read( WithPaths( 6, 3 ) );
		Assert.AreEqual( 2, model.Paths.Count );
		Assert.AreEqual( 2, model.ExtraRecords.Count );
		Assert.IsTrue( model.Paths[0].IsClosed && model.Paths[0].IsBezier );
		Assert.AreEqual( new NVector3( 3, 4, 5 ), model.Paths[0].Points[1] );
		Assert.AreEqual( 3, model.Paths[1].Points.Count );
		// Neither the root mesh nor the dummy hangs under a path dummy (type bit 0x8).
		for ( var node = 0; node < model.Nodes.Count; node++ )
			Assert.IsNull( model.GetPathOf( node ) );
		Assert.IsNull( model.GetPathOf( -1 ) );
	}

	[TestMethod]
	public void EmptyNonFiniteAndOutOfRangePathsAreRejected()
	{
		Assert.ThrowsException<System.IO.InvalidDataException>( () => Read( WithPaths( 0 ) ) );
		var nan = WithPaths( 3 );
		System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( nan.AsSpan( nan.Length - 4 ), float.NaN );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => Read( nan ) );
		var outside = WithPaths( 3 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( outside.AsSpan( Md2ModelFileTests.CreateGeometry().Length + 4 ), 4 );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => Read( outside ) );
	}
}
