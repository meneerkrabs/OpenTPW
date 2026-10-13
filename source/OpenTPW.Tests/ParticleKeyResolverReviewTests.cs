using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Matrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

[TestClass]
public class ParticleKeyResolverReviewTests
{
	private static ModelFile Geometry( uint flags = 0x130, uint nodeType = 0x200, ushort start = 1 )
	{
		var bytes = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x1B0 ), flags );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x264 ), nodeType );
		BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x46 ), start );
		return new ModelFile( new MemoryStream( bytes ) );
	}
	private static ObjectPlacement Placement( int rotation = 0 ) => new( 3, 4, rotation, new NVector2( -7, 11 ), 9 );
	private static void Near( NVector3 expected, NVector3 actual ) => Assert.IsTrue( NVector3.Distance( expected, actual ) < 0.0001f, $"{expected} != {actual}" );

	[TestMethod]
	public void PlacementAndDirectionHaveIndependentNumericWitnesses()
	{
		var model = Geometry();
		var frames = ModelAnimationPlayer.ComputeRestTransforms( model );
		frames[1] = Matrix4x4.CreateTranslation( 10, 20, 30 );
		frames[1].M31 = 3;
		frames[1].M32 = 4;
		frames[1].M33 = 0;
		var result = new ParticleKeyResolver( model ).Resolve( 7, 0x100, frames, Placement( 90 ), true );
		Assert.AreEqual( ParticleKeyStatus.Resolved, result.Status );
		// Y/Z swap, quarter-turn with cell offset, grid anchor, .2 scale, origin and height.
		Near( new NVector3( 5, 19, 13 ), result.EnginePosition!.Value );
		Near( new NVector3( 0, -0.6f, 0.8f ), result.EngineUnitDirection!.Value );
	}

	[TestMethod]
	public void MalformedFirstMatchDoesNotFallThroughToAValidDuplicate()
	{
		var original = Md2ModelFileTests.CreateGeometry();
		var bytes = new byte[original.Length + 40];
		original.CopyTo( bytes, 0 );
		BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x46 ), 0 );
		BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x48 ), 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x7C ), (uint)original.Length );
		for ( var i = 0; i < 2; i++ )
		{
			BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( original.Length + i * 20 ), 0x130 );
			BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( original.Length + i * 20 + 4 ), 7 );
		}
		var model = new ModelFile( new MemoryStream( bytes ) );
		var frames = ModelAnimationPlayer.ComputeRestTransforms( model );
		frames[0].M31 = float.NaN;
		var result = new ParticleKeyResolver( model ).Resolve( 7, 0x100, frames, Placement(), true );
		Assert.AreEqual( ParticleKeyStatus.InvalidTransform, result.Status );
		Assert.AreEqual( (0, 0), (result.AttributeIndex, result.NodeIndex) );
		Assert.IsNull( result.EnginePosition );
		Assert.IsNull( result.EngineUnitDirection );
		// An earlier same-value record outside the particle selector must not hide the second match.
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( original.Length ), 0x40 );
		model = new ModelFile( new MemoryStream( bytes ) );
		result = new ParticleKeyResolver( model ).Resolve( 7, 0x100, frames, Placement(), true );
		Assert.AreEqual( ParticleKeyStatus.Resolved, result.Status );
		Assert.AreEqual( (1, 1), (result.AttributeIndex, result.NodeIndex) );
	}

	[TestMethod]
	public void DegenerateDirectionsAndNonAffineFramesNeverReturnPartialPoses()
	{
		var model = Geometry();
		var resolver = new ParticleKeyResolver( model );
		foreach ( var direction in new[] { NVector3.Zero, new NVector3( float.NaN, 0, 0 ), new NVector3( float.PositiveInfinity, 0, 0 ), new NVector3( float.MaxValue ) } )
		{
			var frames = ModelAnimationPlayer.ComputeRestTransforms( model );
			frames[1].M31 = direction.X;
			frames[1].M32 = direction.Y;
			frames[1].M33 = direction.Z;
			var result = resolver.Resolve( 7, 0x100, frames, Placement(), true );
			Assert.AreEqual( ParticleKeyStatus.InvalidTransform, result.Status );
			Assert.IsNull( result.EnginePosition );
			Assert.IsNull( result.EngineUnitDirection );
		}
		var affine = ModelAnimationPlayer.ComputeRestTransforms( model );
		affine[1].M14 = 0.5f;
		Assert.AreEqual( ParticleKeyStatus.InvalidTransform, resolver.Resolve( 7, 0x100, affine, Placement() ).Status );
		var framesValid = ModelAnimationPlayer.ComputeRestTransforms( model );
		foreach ( var badPlacement in new[]
		{
			Placement() with { X = int.MaxValue },
			Placement() with { X = int.MinValue },
			Placement() with { Y = int.MaxValue },
			Placement() with { Y = int.MinValue },
			Placement() with { BaseHeight = float.NaN },
			Placement() with { GridOrigin = new NVector2( float.PositiveInfinity, 0 ) }
		} )
			Assert.AreEqual( ParticleKeyStatus.InvalidTransform, resolver.Resolve( 7, 0x100, framesValid, badPlacement ).Status );
	}

	[TestMethod]
	public void ResolverOutlivesInputStreamAndReturnsPoseSnapshots()
	{
		ModelFile model;
		var bytes = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x1B0 ), 0x130 );
		using ( var stream = new MemoryStream( bytes ) )
			model = new ModelFile( stream );
		var resolver = new ParticleKeyResolver( model );
		var frames = ModelAnimationPlayer.ComputeRestTransforms( model );
		var snapshot = resolver.Resolve( 7, 0x100, frames, Placement(), true );
		Assert.AreEqual( ParticleKeyStatus.Resolved, snapshot.Status );
		var position = snapshot.EnginePosition;
		frames[1] = Matrix4x4.CreateTranslation( 99, 0, 0 );
		Assert.AreNotEqual( position, resolver.Resolve( 7, 0x100, frames, Placement(), true ).EnginePosition );
		Assert.AreEqual( position, snapshot.EnginePosition );
	}

}
