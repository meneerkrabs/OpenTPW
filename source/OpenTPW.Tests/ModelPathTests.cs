using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Matrix4x4 = System.Numerics.Matrix4x4;
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

	[TestMethod]
	public void HugeClosedBezierControlPointsFailClosedAtTheFirstSegmentMidpoint()
	{
		// Closed kind-3 six-point path whose first Bézier segment's control points sit one factor below the float
		// range; points 4 and 5 are the closing segment's (zero) middle controls.
		var huge = 2e38f;
		var path = new ModelPath( 3, new[] { new NVector3( 0, 0, 0 ), new NVector3( huge, 0, 0 ), new NVector3( huge, 0, 0 ),
			new NVector3( 0, 0, 0 ), NVector3.Zero, NVector3.Zero } );
		Assert.AreEqual( 2, path.SegmentCount );

		// At 25 % the closed two-segment sampler selects the first segment's midpoint (segment 0, fraction 1/2). The
		// double Bernstein value there, 3/8·p1 + 3/8·p2 = 1.5e38, is finite and within the float range, but the float
		// polynomial overflows 3·(p1 − p0) = 6e38 before any cancellation, so the position is NaN.
		var doubleBernstein = 3.0 / 8.0 * (double)huge + 3.0 / 8.0 * (double)huge;
		Assert.IsTrue( doubleBernstein <= (double)float.MaxValue, $"{doubleBernstein}" );
		var floatBernstein = Bezier( path.Points[0], path.Points[1], path.Points[2], path.Points[3], 0.5f );
		Assert.IsTrue( float.IsFinite( floatBernstein.X ), $"{floatBernstein.X}" );
		Assert.AreEqual( float.PositiveInfinity, 3f * ( path.Points[1].X - path.Points[0].X ), "the Horner form overflows first" );

		Assert.IsFalse( path.TryEvaluate( 25, out var position, out var direction ), "the failed evaluation must fail closed" );
		Assert.AreEqual( default( NVector3 ), position );
		Assert.IsNull( direction );
	}

	[TestMethod]
	public void FiniteHugeLinearPathKeepsExactComponents()
	{
		// Positive control at the float range: one open linear segment, percent 50 → fraction 1/2, stays finite and
		// exact in OpenTPW's float arithmetic.
		var path = new ModelPath( 8, new[] { new NVector3( 3e38f, 0, 0 ), new NVector3( 3e38f, 0, 4f ) } );
		Assert.AreEqual( 1, path.SegmentCount );
		Assert.IsTrue( path.TryEvaluate( 50, out var position, out var direction ) );
		// Exact per-component assertions, not a Distance, which would overflow at this magnitude.
		Assert.AreEqual( 3e38f, position.X );
		Assert.AreEqual( 0f, position.Y );
		Assert.AreEqual( 2f, position.Z );
		Assert.IsTrue( float.IsFinite( position.X ) && float.IsFinite( position.Y ) && float.IsFinite( position.Z ) );
		Assert.IsNotNull( direction );
		AreClose( NVector3.UnitZ, direction!.Value, "huge linear direction" );
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

	/// <summary>
	/// Gives <see cref="Md2ModelFileTests.CreateGeometry"/> one kind-3 path record (as <see cref="WithPaths"/> builds them)
	/// whose points are explicit, so a test can pin huge or ordinary control points.
	/// </summary>
	private static byte[] GeometryWithPath( params NVector3[] points )
	{
		var geometry = Md2ModelFileTests.CreateGeometry();
		var table = geometry.Length;
		var pointsOffset = table + 16;
		var data = new byte[pointsOffset + points.Length * 12];
		geometry.CopyTo( data, 0 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 0x40 ), 1 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 0xAC ), (uint)table );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( table ), 3 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( table + 4 ), (ushort)points.Length );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( table + 8 ), (uint)pointsOffset );
		for ( var index = 0; index < points.Length; index++ )
		{
			System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( pointsOffset + index * 12 ), points[index].X );
			System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( pointsOffset + index * 12 + 4 ), points[index].Y );
			System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( pointsOffset + index * 12 + 8 ), points[index].Z );
		}
		return data;
	}

	/// <summary>
	/// A minimal animation member with one flag-<paramref name="pathFlags"/> record for node 0 (duration 10 ticks) and a
	/// path-parameter block of <paramref name="values"/>, laid out from the trailer format of <see cref="ModelAnimation.Decode"/>
	/// and decoded through <see cref="ModelFile"/> like any clip.
	/// </summary>
	private static byte[] CreatePathAnimation( float[] values, uint pathFlags )
	{
		const int record = 0xB8;
		const int block = 0xF8;
		const int table = 0x108;
		const int trailer = 0x10C;
		var data = new byte[trailer + 72];
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 0 ), ModelFile.Magic );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 4 ), 221 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 8 ), 203 );
		System.Text.Encoding.ASCII.GetBytes( "synth.MD2" ).CopyTo( data, 0x18 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 0x98 ), trailer );

		// Record 0: index 0, flags, duration 10, node 0, key pointers all absent, word 9 → the path-parameter block.
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( record ), 0 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( record + 4 ), pathFlags );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( record + 12 ), 10 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( record + 36 ), block );

		// Path-parameter block: start tick 0, one value per tick, reserved 0, table pointer.
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( block ), 0 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( block + 4 ), (uint)values.Length );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( block + 8 ), 0 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( block + 12 ), (uint)table );
		for ( var index = 0; index < values.Length; index++ )
			System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( table + index * 4 ), values[index] );

		// Trailer: duration, one track with no rigid keys, record table pointer at word 11.
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( trailer + 8 ), 10 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( trailer + 16 ), 1 << 16 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( trailer + 44 ), record );
		return data;
	}

	/// <summary>
	/// The parsed geometry with the tracked node hanging under a type-bit-0x8 dummy ("Kart_path01"-style 0x208) whose
	/// stored index selects path 0, and a non-zero rest translation on the tracked node.
	/// </summary>
	private static ModelFile PlayerModel( byte[] geometryData )
	{
		var model = Read( geometryData );
		model.Nodes = new[]
		{
			new ModelNode( 0, ModelBuilder.MeshNodeType, 0, "body", 1, 0, Matrix4x4.CreateTranslation( 7, 8, 9 ) ),
			new ModelNode( 1, ModelBuilder.DummyNodeType | 0x8, 0, "Kart_path01", -1, -1, Matrix4x4.Identity ),
		};
		model.RootNodeIndex = 1;
		return model;
	}

	/// <summary>The animation of <see cref="CreatePathAnimation"/> against <see cref="PlayerModel"/>, ticked to <paramref name="tick"/>.</summary>
	private static Matrix4x4 PlayerAt( byte[] animationData, byte[] geometryData, float tick )
	{
		var clip = Read( animationData ).Clip!;
		var player = new ModelAnimationPlayer( PlayerModel( geometryData ), clip, 1 );
		player.SetTick( tick );
		return player.LocalTransform( 0 );
	}

	/// <summary>The ordinary corpus-style closed Bézier loop the sampler tests below place nodes on.</summary>
	private static readonly NVector3[] OrdinarySixPoints =
	{
		new( 0, 0, 0 ), new( 1, 0, 0 ), new( 2, 0, 0 ), new( 3, 0, 0 ), new( 3, 0, 3 ), new( 0, 0, 3 ),
	};

	/// <summary>The Bernstein form of a cubic Bézier at t = ½, the value the first segment's midpoint has.</summary>
	private static NVector3 BernsteinMidpoint( NVector3 p0, NVector3 p1, NVector3 p2, NVector3 p3 ) =>
		0.125f * p0 + 0.375f * p1 + 0.375f * p2 + 0.125f * p3;

	private static void Close( float expected, float actual, string what ) =>
		Assert.IsTrue( MathF.Abs( expected - actual ) < 1e-4f, $"{what}: expected {expected}, got {actual}" );

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

	[TestMethod]
	public void ThePlayerKeepsTheRestTranslationWhenThePathFailsClosed()
	{
		var huge = 2e38f;
		var points = new[] { new NVector3( 0, 0, 0 ), new NVector3( huge, 0, 0 ), new NVector3( huge, 0, 0 ),
			new NVector3( 0, 0, 0 ), NVector3.Zero, NVector3.Zero };
		var geometry = GeometryWithPath( points );
		// Both path orientation states fail closed, so the 0x200-only and the 0x600 record keep the rest translation,
		// neither of them replaces it with the NaN the polynomial produced nor places the node anywhere else.
		foreach ( var flags in new[] { ModelAnimationTrack.PathParameterFlag, ModelAnimationTrack.PathFlags } )
		{
			var local = PlayerAt( CreatePathAnimation( new[] { 25f }, flags ), geometry, 0 );
			Close( 7, local.M41, $"rest translation X, flags 0x{flags:X}" );
			Close( 8, local.M42, $"rest translation Y, flags 0x{flags:X}" );
			Close( 9, local.M43, $"rest translation Z, flags 0x{flags:X}" );
			Assert.IsTrue( float.IsFinite( local.M41 ) && float.IsFinite( local.M42 ) && float.IsFinite( local.M43 ) );
		}
	}

	[TestMethod]
	public void ThePlayerStillPlacesTheNodeOnAnOrdinaryPath()
	{
		var geometry = GeometryWithPath( OrdinarySixPoints );
		// The 0x200-only state places the node on the path; the 0x600 state additionally turns it along it.
		foreach ( var flags in new[] { ModelAnimationTrack.PathParameterFlag, ModelAnimationTrack.PathFlags } )
		{
			var midpoint = BernsteinMidpoint( OrdinarySixPoints[0], OrdinarySixPoints[1], OrdinarySixPoints[2], OrdinarySixPoints[3] );
			var local = PlayerAt( CreatePathAnimation( new[] { 25f }, flags ), geometry, 0 );
			Close( midpoint.X, local.M41, $"position X on the path, flags 0x{flags:X}" );
			Close( midpoint.Y, local.M42, $"position Y on the path, flags 0x{flags:X}" );
			Close( midpoint.Z, local.M43, $"position Z on the path, flags 0x{flags:X}" );
			if ( flags == ModelAnimationTrack.PathFlags )
			{
				Close( 1, local.M31, "row 2 X faces along the path" );
				Close( 0, local.M32, "row 2 Y faces along the path" );
				Close( 0, local.M33, "row 2 Z faces along the path" );
			}
		}
	}
}
