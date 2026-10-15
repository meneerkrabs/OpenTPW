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
public class ParticleKeyResolverTests
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

	[DataTestMethod]
	[DataRow( 0, 0f, 1f )]
	[DataRow( 90, 1f, 0f )]
	[DataRow( 180, 0f, -1f )]
	[DataRow( 270, -1f, 0f )]
	public void NativeOrdinalPositionAndDirectionFollowPlacedObjectAxes( int rotation, float directionX, float directionY )
	{
		var model = Geometry();
		var animator = new ObjectAnimator( model );
		var result = new ParticleKeyResolver( model ).Resolve( 7, 0x100, animator.NodeTransforms, Placement( rotation ), true );
		Assert.AreEqual( ParticleKeyStatus.Resolved, result.Status );
		Assert.AreEqual( (0, 1), (result.AttributeIndex, result.NodeIndex) );
		// The dummy is (0,0,5) under a root at (1,2,3); MD2 Y is engine Z.
		var transformed = NVector3.Transform( new NVector3( 1, 8, 2 ), Placement( rotation ).ModelToEngine );
		Near( transformed, result.EnginePosition!.Value );
		Near( new NVector3( directionX, directionY, 0 ), result.EngineUnitDirection!.Value );
		Assert.AreEqual( 1024f, ParticleKeyResolver.NativeDirectionMultiplier );
	}

	[TestMethod]
	public void NodeFlagReversesDirectionAndScalingDoesNotBecomeVelocityMagnitude()
	{
		var model = Geometry( nodeType: 0x600 );
		var matrices = ModelAnimationPlayer.ComputeRestTransforms( model );
		matrices[1] = Matrix4x4.CreateScale( 3, 4, 5 ) * matrices[1];
		var resolver = new ParticleKeyResolver( model );
		var withDirection = resolver.Resolve( 7, 0x100, matrices, Placement(), true );
		Near( -NVector3.UnitY, withDirection.EngineUnitDirection!.Value );
		var positionOnly = resolver.Resolve( 7, 0x100, matrices, Placement() );
		Assert.AreEqual( withDirection.EnginePosition, positionOnly.EnginePosition );
		Assert.IsNull( positionOnly.EngineUnitDirection );
	}

	[TestMethod]
	public void CurrentAnimationPoseChangesKeyPositionWithoutChangingSharedBindPose()
	{
		var model = Geometry();
		var animator = new ObjectAnimator( model );
		var resolver = new ParticleKeyResolver( model );
		var bind = resolver.Resolve( 7, 0x100, animator.RestTransforms, Placement(), true );
		var clip = new ModelFile( new MemoryStream( Md2AnimationTests.CreateAnimation() ) ).Clip!;
		animator.Play( 0, clip, "moving", loop: true );
		animator.Advance( 0.25 );
		var pose = resolver.Resolve( 7, 0x100, animator.NodeTransforms, Placement(), true );
		Assert.AreNotEqual( bind.EnginePosition, pose.EnginePosition );
		Assert.AreEqual( bind, resolver.Resolve( 7, 0x100, animator.RestTransforms, Placement(), true ) );
		Near( (ObjectPlacement.SwapAxes( animator.NodeTransforms[1] ) * Placement().ModelToEngine).Translation, pose.EnginePosition!.Value );
	}

	[TestMethod]
	public void UnsupportedSelectorsFallbacksKeysAndUnframedNodesCarryNoPose()
	{
		var model = Geometry();
		var resolver = new ParticleKeyResolver( model );
		var frames = ModelAnimationPlayer.ComputeRestTransforms( model );
		foreach ( var selector in new uint[] { 0, 0x40, 0x80, 0x140, uint.MaxValue } )
			Assert.AreEqual( ParticleKeyStatus.UnsupportedSelector, resolver.Resolve( 7, selector, frames, Placement() ).Status );
		foreach ( var key in new[] { -1, int.MinValue } )
			Assert.AreEqual( ParticleKeyStatus.UnsupportedFallback, resolver.Resolve( key, 0x100, frames, Placement(), true ).Status );
		Assert.AreEqual( ParticleKeyStatus.MissingKey, resolver.Resolve( 8, 0x100, frames, Placement() ).Status );
		foreach ( var other in new[] { Geometry( flags: 0x40 ), Geometry( flags: 0x100 ), Geometry( start: 10 ) } )
		{
			var result = new ParticleKeyResolver( other ).Resolve( 7, 0x100, ModelAnimationPlayer.ComputeRestTransforms( other ), Placement(), true );
			Assert.AreNotEqual( ParticleKeyStatus.Resolved, result.Status );
			Assert.IsNull( result.EnginePosition );
			Assert.IsNull( result.EngineUnitDirection );
		}
	}

	[TestMethod]
	public void FirstMatchingAttributeWinsAndAKeyWithoutAMatrixStartsAtTheWorldOrigin()
	{
		var original = Md2ModelFileTests.CreateGeometry();
		var bytes = new byte[original.Length + 40];
		original.CopyTo( bytes, 0 );
		BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x46 ), 0 );
		BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 0x48 ), 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 0x7C ), (uint)original.Length );
		for ( var i = 0; i < 2; i++ )
		{
			BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( original.Length + i * 20 ), i == 0 ? 0x100u : 0x130u );
			BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( original.Length + i * 20 + 4 ), 7 );
		}
		var model = new ModelFile( new MemoryStream( bytes ) );
		var result = new ParticleKeyResolver( model ).Resolve( 7, 0x100,
			ModelAnimationPlayer.ComputeRestTransforms( model ), Placement(), true );
		// [BIN:TPW-EXE:0x556BF1] a key entry without a matrix (flags 0x30 clear) is a success at world position (0, 0, 0), not a refusal
		Assert.AreEqual( ParticleKeyStatus.ResolvedAtWorldOrigin, result.Status );
		Assert.AreEqual( (0, 0), (result.AttributeIndex, result.NodeIndex) );
		Assert.IsNull( result.EnginePosition );
		Assert.IsNull( result.EngineUnitDirection );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( original.Length ), 0x130 );
		model = new ModelFile( new MemoryStream( bytes ) );
		var frames = ModelAnimationPlayer.ComputeRestTransforms( model );
		result = new ParticleKeyResolver( model ).Resolve( 7, 0x100, frames, Placement(), true );
		Assert.AreEqual( ParticleKeyStatus.Resolved, result.Status );
		Assert.AreEqual( (0, 0), (result.AttributeIndex, result.NodeIndex) );
		Near( (ObjectPlacement.SwapAxes( frames[0] ) * Placement().ModelToEngine).Translation, result.EnginePosition!.Value );
	}

	[TestMethod]
	public void CorruptMatricesAndPlacementRefuseWithoutPartialPosition()
	{
		var model = Geometry();
		var resolver = new ParticleKeyResolver( model );
		var frames = ModelAnimationPlayer.ComputeRestTransforms( model );
		foreach ( var bad in new[] { Matrix4x4.CreateScale( 0 ), new Matrix4x4(), Matrix4x4.CreateTranslation( float.NaN, 0, 0 ) } )
		{
			frames[1] = bad;
			var result = resolver.Resolve( 7, 0x100, frames, Placement(), true );
			Assert.AreEqual( ParticleKeyStatus.InvalidTransform, result.Status );
			Assert.IsNull( result.EnginePosition );
		}
		frames = ModelAnimationPlayer.ComputeRestTransforms( model );
		Assert.AreEqual( ParticleKeyStatus.InvalidTransform, resolver.Resolve( 7, 0x100, frames, Placement( 45 ) ).Status );
		Assert.AreEqual( ParticleKeyStatus.InvalidTransform, resolver.Resolve( 7, 0x100, Array.Empty<Matrix4x4>(), Placement() ).Status );
	}

	[TestMethod]
	public void OriginalRideKeysUseMetadataOrdinalsAndCurrentAnimatedMatrices()
	{
		var root = Path.Combine( OriginalParkImportTests.OriginalDataPath(), "levels" );
		var resolved = 0;
		var animated = false;
		var cameras = 0;
		var witness = "";
		foreach ( var theme in new[] { "jungle", "hallow", "fantasy", "space" } )
			foreach ( var file in Directory.EnumerateFiles( Path.Combine( root, theme, "rides" ), "*.wad" ).OrderBy( name => name, StringComparer.Ordinal ) )
			{
				using var archive = new WadArchive( file );
				var models = archive.GetFiles( "" ).Where( name => name.EndsWith( ".MD2", StringComparison.OrdinalIgnoreCase ) )
					.Select( name => archive.GetFile( name )!.GetData() )
					.Where( bytes => BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( 4 ) ) == 221 && BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( 8 ) ) == 203 )
					.Select( bytes => new ModelFile( new MemoryStream( bytes ) ) ).ToArray();
				foreach ( var model in models.Where( value => value.Kind == ModelFileKind.Geometry ) )
				{
					var resolver = new ParticleKeyResolver( model );
					var animator = new ObjectAnimator( model );
					for ( var i = 0; i < model.DummyAttributes.Count; i++ )
					{
						var key = model.DummyAttributes[i];
						if ( (key.Type & 0x40) != 0 )
						{
							Assert.AreEqual( ParticleKeyStatus.UnsupportedSelector, resolver.Resolve( (int)key.Value, 0x40, animator.NodeTransforms, Placement() ).Status );
							cameras++;
						}
						if ( (key.Type & 0x100) == 0 || (key.Type & 0x30) == 0 || key.Value > int.MaxValue )
							continue;
						var result = resolver.Resolve( (int)key.Value, 0x100, animator.NodeTransforms, Placement( 90 ), true );
						Assert.AreEqual( ParticleKeyStatus.Resolved, result.Status, $"{file}: key {key.Value}" );
						Assert.AreEqual( model.Counts.Unknown46 + result.AttributeIndex, result.NodeIndex );
						Near( (ObjectPlacement.SwapAxes( animator.NodeTransforms[result.NodeIndex] ) * Placement( 90 ).ModelToEngine).Translation, result.EnginePosition!.Value );
						Assert.AreEqual( 1f, result.EngineUnitDirection!.Value.Length(), 0.0001f );
						resolved++;
						if ( animated ) continue;
						foreach ( var clip in models.Where( value => value.Clip != null && value.Counts.NodeCount == model.Counts.NodeCount ) )
						{
							animator.Play( 0, clip.Clip!, "asset", loop: false );
							animator.Advance( 0.1 );
							var moving = resolver.Resolve( (int)key.Value, 0x100, animator.NodeTransforms, Placement( 90 ), true );
							if ( moving.Status == ParticleKeyStatus.Resolved && moving.EnginePosition != result.EnginePosition )
							{
								Near( (ObjectPlacement.SwapAxes( animator.NodeTransforms[moving.NodeIndex] ) * Placement( 90 ).ModelToEngine).Translation, moving.EnginePosition!.Value );
								animated = true;
								witness = $"{theme}/{Path.GetFileName( file )}/{model.SourceName}: key {key.Value}, node {moving.NodeIndex}, clip {clip.SourceName}";
								break;
							}
						}
					}
				}
			}
		Assert.IsTrue( resolved > 0, "The original corpus must contain selector-0x100 keys." );
		Console.WriteLine( $"Resolved {resolved} particle key queries; refused {cameras} camera selectors; animated witness: {witness}" );
		Assert.IsTrue( cameras > 0 );
		Assert.IsTrue( animated, "At least one genuine particle key must move with a shipped clip." );
	}
}
