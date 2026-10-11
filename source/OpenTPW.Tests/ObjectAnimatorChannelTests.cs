using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Matrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>
/// The channel model of <see cref="ObjectAnimator"/> on synthetic members (docs/reverse/ANIM-plan.md §8, §13):
/// ascending-channel mixing per component, the AnimID 14 end, deferral and flush, speed, texture-coordinate
/// tracks, node-state bit 0x10, the 12-byte block and relative-animation models.
/// </summary>
[TestClass]
public class ObjectAnimatorChannelTests
{
	private static ModelFile Geometry() => new( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) );
	private static ModelAnimation Clip( byte[] data ) => new ModelFile( new MemoryStream( data ) ).Clip!;
	private static ModelAnimation Rigid() => Clip( Md2AnimationTests.CreateAnimation() );
	private static ModelAnimation RotationOnly() => Clip( Md2AnimationTests.CreateRotationOnlyAnimation() );
	private static ModelAnimation Vertex( ushort duration ) => Clip( Md2VertexAnimationTests.CreateAnimation( 0, duration ) );

	private static Matrix4x4 Expected( ModelFile model, ModelAnimationTrack translationScale, float translationTick, ModelAnimationTrack rotation, float rotationTick )
	{
		var local = Matrix4x4.CreateScale( translationScale.SampleScale( translationTick )!.Value )
			* Matrix4x4.CreateFromQuaternion( rotation.SampleRotation( rotationTick )!.Value )
			* Matrix4x4.CreateTranslation( translationScale.SampleTranslation( translationTick )!.Value );
		var parent = model.Nodes[1].ParentIndex;
		return parent < 0 ? local : local * ModelAnimationPlayer.ComputeRestTransforms( model )[parent];
	}

	/// <summary>
	/// 0xa7960 updates channels in ascending order and each samples in set mode: per component the highest channel
	/// that writes it wins, whichever started last (RIDES-002's "most recently started" rule is contradicted).
	/// </summary>
	[TestMethod]
	public void HighestChannelWinsEachComponentRegardlessOfStartOrder()
	{
		var model = Geometry();
		var full = Rigid();
		var rotation = RotationOnly();

		// Full clip on channel 0 first, rotation-only on channel 1 later: rotation from 1, scale/translation from 0.
		var animator = new ObjectAnimator( model );
		animator.Play( 0, full, "full", loop: true );
		animator.Advance( 0.5 );
		animator.Play( 1, rotation, "rotation", loop: true );
		animator.Advance( 0.25 );
		Assert.AreEqual( (22.5f, 7.5f), (animator.GetTick( 0 ), animator.GetTick( 1 )) );
		Assert.AreEqual( Expected( model, full.Tracks[0], 22.5f, rotation.Tracks[0], 7.5f ), animator.NodeTransforms[1] );

		// Rotation-only on channel 0 started last, full clip on channel 1: channel 1 writes every component.
		animator = new ObjectAnimator( model );
		animator.Play( 1, full, "full", loop: true );
		animator.Advance( 0.5 );
		animator.Play( 0, rotation, "rotation", loop: true );
		animator.Advance( 0.25 );
		Assert.AreEqual( Expected( model, full.Tracks[0], 22.5f, full.Tracks[0], 22.5f ), animator.NodeTransforms[1] );
	}

	[TestMethod]
	public void HighestChannelWinsTheVertexPose()
	{
		var model = Geometry();
		var animator = new ObjectAnimator( model );
		var ten = Vertex( 10 );
		var thirty = Vertex( 30 );
		animator.Play( 1, ten, "ten", loop: true );
		animator.Advance( 0.1 );
		animator.Play( 0, thirty, "thirty", loop: true );
		animator.Advance( 0.1 );
		var expected = new NVector3[4];
		ten.Tracks[0].VertexAnimation!.ApplyPose( animator.GetTick( 1 ), expected );
		CollectionAssert.AreEqual( expected, animator.GetVertexPositions( 0 )!.ToArray() );
	}

	/// <summary>
	/// A non-looping clip past its end restarts as AnimID 14 (0xa7498): frozen on the last frame with the pose kept and
	/// no copy-back of the stored mesh; GETANIM reports −1 (RIDES-003's hold).
	/// </summary>
	[TestMethod]
	public void EndedClipHoldsItsLastFrameAndReportsMinusOne()
	{
		var model = Geometry();
		var animator = new ObjectAnimator( model );
		var clip = Vertex( 30 );
		Assert.AreEqual( ObjectAnimator.NoAnimation, animator.GetAnimationId( 0 ) );
		Assert.AreEqual( ObjectAnimator.NoAnimation, animator.GetAnimation( 0 ) );
		Assert.AreEqual( 1000, animator.Trigger( 0, clip, "m1", 5, 0, loop: false ) );
		Assert.AreEqual( 5, animator.GetAnimation( 0 ) );
		animator.Advance( 1.0 );
		Assert.IsTrue( animator.IsChannelPlaying( 0 ), "a frame equal to the duration is not past the end" );
		animator.Advance( 0.001 );
		Assert.IsFalse( animator.IsChannelPlaying( 0 ) );
		Assert.AreEqual( ObjectAnimator.EndedAnimation, animator.GetAnimationId( 0 ) );
		Assert.AreEqual( -1, animator.GetAnimation( 0 ) );
		Assert.AreEqual( 30f, animator.GetTick( 0 ) );
		var last = new NVector3[4];
		clip.Tracks[0].VertexAnimation!.ApplyPose( 30, last );
		CollectionAssert.AreEqual( last, animator.GetVertexPositions( 0 )!.ToArray(), "the last pose stays, the stored mesh does not come back" );
		var version = animator.GetVertexVersion( 0 );
		animator.Advance( 5 );
		Assert.AreEqual( 30f, animator.GetTick( 0 ) );
		Assert.AreEqual( version, animator.GetVertexVersion( 0 ) );

		// A new trigger replaces the ended clip.
		animator.Trigger( 0, clip, "m2", 5, 1, loop: false );
		Assert.AreEqual( 5, animator.GetAnimation( 0 ) );
		Assert.AreEqual( 0f, animator.GetTick( 0 ) );
	}

	[TestMethod]
	public void MissingClipLeavesTheChannelAndReportsOneSecond()
	{
		var animator = new ObjectAnimator( Geometry() );
		Assert.AreEqual( ObjectAnimator.MissingClipMilliseconds, animator.Trigger( 0, null, "", 6, 0, loop: false ) );
		Assert.AreEqual( ObjectAnimator.NoAnimation, animator.GetAnimationId( 0 ) );
		animator.Trigger( 0, Vertex( 30 ), "m1", 5, 0, loop: true );
		Assert.AreEqual( 1000, animator.Trigger( 0, null, "", 6, 0, loop: false ) );
		Assert.AreEqual( 5, animator.GetAnimationId( 0 ) );
	}

	/// <summary>
	/// Flag 0x2 (native callers only) queues behind a running clip: the result is the remaining time plus the new length,
	/// the queued clip starts when the current one ends, and FLUSHANIM drops only the queued clip.
	/// </summary>
	[TestMethod]
	public void DeferredClipFollowsAndFlushDropsOnlyIt()
	{
		var animator = new ObjectAnimator( Geometry() );
		var thirty = Vertex( 30 );
		var ten = Vertex( 10 );
		animator.Trigger( 0, thirty, "first", 5, 0, loop: false );
		animator.Advance( 0.5 );
		var remaining = ObjectAnimator.Fctiwz( ObjectAnimator.RemainingMillisecondsPerTick * (30 - 15f) );
		Assert.AreEqual( 499, remaining );
		Assert.AreEqual( remaining + 333, animator.Trigger( 0, ten, "second", 7, 0, loop: false, defer: true ) );
		Assert.AreEqual( 7, animator.GetDeferredAnimationId( 0 ) );
		Assert.AreEqual( 5, animator.GetAnimationId( 0 ), "the running clip continues" );
		animator.Advance( 0.501 );
		Assert.AreEqual( 7, animator.GetAnimationId( 0 ) );
		Assert.AreEqual( ObjectAnimator.NoAnimation, animator.GetDeferredAnimationId( 0 ) );
		Assert.AreEqual( "second", animator.GetClip( 0 ) );

		// Missing deferred clip: +1000 ms. Flush drops the queued clip and keeps the playing one.
		Assert.AreEqual( ObjectAnimator.Fctiwz( ObjectAnimator.RemainingMillisecondsPerTick * 10 ) + 1000, animator.Trigger( 0, null, "", 8, 0, loop: false, defer: true ) );
		Assert.IsFalse( animator.Flush( 0 ), "a missing clip queues nothing" );
		animator.Trigger( 0, thirty, "third", 9, 0, loop: false, defer: true );
		Assert.IsTrue( animator.Flush( 0 ) );
		Assert.IsFalse( animator.Flush( 0 ) );
		Assert.AreEqual( 7, animator.GetAnimationId( 0 ) );
		animator.Advance( 1 );
		Assert.AreEqual( -1, animator.GetAnimation( 0 ), "ends instead of following the flushed clip" );

		// An ended channel does not defer; a plain start keeps a queued clip.
		Assert.AreEqual( 1000, animator.Trigger( 0, thirty, "fourth", 5, 0, loop: false, defer: true ) );
		Assert.AreEqual( 5, animator.GetAnimationId( 0 ) );
		animator.Trigger( 0, ten, "queued", 7, 0, loop: false, defer: true );
		animator.Trigger( 0, thirty, "restart", 5, 1, loop: false );
		Assert.AreEqual( 7, animator.GetDeferredAnimationId( 0 ) );
	}

	/// <summary>Speed scales the frame (0xa6484) and the loop carry (0xa6398) but not the returned length.</summary>
	[TestMethod]
	public void SpeedScalesTheFrameNotTheReturnedLength()
	{
		var animator = new ObjectAnimator( Geometry() );
		Assert.AreEqual( 1000, animator.Trigger( 0, Vertex( 30 ), "fast", 6, 0, loop: true, speed: 4f ) );
		Assert.AreEqual( 4f, animator.GetSpeed( 0 ) );
		animator.Advance( 0.1 );
		Assert.AreEqual( 12f, animator.GetTick( 0 ) );
		animator.Advance( 0.15 );
		Assert.AreEqual( 30f, animator.GetTick( 0 ) );
		animator.Advance( 0.01 );
		// Frame 31.2 is 1.2 ticks past the end: the carry is trunc(1000 × 1.2 / 30 / 4) = 10 ms, i.e. frame 1.2.
		Assert.AreEqual( 1.2f, animator.GetTick( 0 ), 1e-5f );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => animator.Trigger( 0, Vertex( 30 ), "zero", 6, 0, loop: true, speed: 0 ) );
	}

	/// <summary>UV tracks give the instance its own texture coordinates; losing the track restores the stored ones except on fixed items.</summary>
	[TestMethod]
	public void UvTracksPlayPerInstanceAndCopyBackOnClipChange()
	{
		var model = Geometry();
		var clip = Clip( Md2VertexAnimationTests.CreateUvAnimation() );
		var animator = new ObjectAnimator( model );
		Assert.IsNull( animator.GetTexCoords( 0 ) );
		animator.Play( 0, clip, "uv", loop: true );
		Assert.AreEqual( 0, animator.VertexLimitations.Count );
		var expected = new NVector2[4];
		clip.Tracks[0].UvAnimation!.Apply( 0, expected );
		CollectionAssert.AreEqual( expected, animator.GetTexCoords( 0 )!.ToArray() );
		var version = animator.GetVertexVersion( 0 );
		animator.Advance( 0.15 );
		Assert.AreEqual( 4.5f, animator.GetTick( 0 ) );
		clip.Tracks[0].UvAnimation!.Apply( 4.5f, expected );
		CollectionAssert.AreEqual( expected, animator.GetTexCoords( 0 )!.ToArray() );
		Assert.AreNotEqual( version, animator.GetVertexVersion( 0 ) );
		Assert.AreEqual( 4, model.Meshes[0].TexCoords.Length, "the shared model is not written" );
		Assert.AreEqual( 0.1f, model.Meshes[0].TexCoords[0].X );

		// A clip without UV tracks on the mesh restores the stored coordinates; a fixed item keeps the last ones.
		animator.Play( 0, Vertex( 30 ), "vertices", loop: true );
		Assert.IsNull( animator.GetTexCoords( 0 ) );
		var fixedItem = new ObjectAnimator( model, keepsPoseOnClipChange: true );
		fixedItem.Play( 0, clip, "uv", loop: true );
		fixedItem.Play( 0, Vertex( 30 ), "vertices", loop: true );
		Assert.IsNotNull( fixedItem.GetTexCoords( 0 ) );

		// A block whose corner count differs from the mesh is reported and leaves the stored coordinates.
		var wide = new ObjectAnimator( model );
		wide.Play( 0, Clip( Md2VertexAnimationTests.CreateUvAnimation( corners: 5 ) ), "wide", loop: true );
		Assert.IsNull( wide.GetTexCoords( 0 ) );
		Assert.AreEqual( "wide node 0: UV block animates 5 corners, the mesh has 4", wide.VertexLimitations.Single() );
	}

	/// <summary>Toggle entries and the clip's node list drive node-state bit 0x10, which hides the node's mesh (RIDES-042).</summary>
	[TestMethod]
	public void NodeStateBitFollowsTogglesAndNodeLists()
	{
		var model = Geometry();
		var animator = new ObjectAnimator( model );
		Assert.IsFalse( animator.IsNodeHidden( 0 ) );
		// Toggles 0, +3, -7 on the mesh node: set from 0, cleared from tick 3, set again from 7.
		animator.Play( 0, Vertex( 30 ), "toggles", loop: true );
		Assert.IsTrue( animator.IsNodeHidden( 0 ) );
		var version = animator.HiddenVersion;
		animator.Advance( 0.1 );
		Assert.IsFalse( animator.IsNodeHidden( 0 ) );
		Assert.AreNotEqual( version, animator.HiddenVersion );
		animator.Advance( 0.15 );
		Assert.IsTrue( animator.IsNodeHidden( 0 ) );

		// A node list sets the bit at the clip start; a dummy node keeps the state but hides nothing.
		var listed = new ObjectAnimator( model );
		listed.Play( 0, Clip( Md2VertexAnimationTests.CreateAnimationWithNodeList( 1, 30, 0, 1, 7 ) ), "list", loop: false );
		Assert.IsTrue( listed.GetNodeStateBit( 0 ) && listed.GetNodeStateBit( 1 ) );
		Assert.IsTrue( listed.IsNodeHidden( 0 ) );
		Assert.IsFalse( listed.IsNodeHidden( 1 ), "dummy nodes have no mesh" );
		// The toggles of node 1 clear its bit from tick 3; nothing clears node 0's.
		listed.Advance( 0.2 );
		Assert.IsFalse( listed.GetNodeStateBit( 1 ) );
		Assert.IsTrue( listed.IsNodeHidden( 0 ) );
	}

	[TestMethod]
	public void TwelveByteBlockShowsTheStoredMesh()
	{
		var clip = Clip( TwelveByte( Md2VertexAnimationTests.CreateAnimation( 0, 30 ) ) );
		Assert.IsNotNull( clip.Tracks[0].VertexPose );
		var animator = new ObjectAnimator( Geometry() );
		animator.Play( 0, clip, "pose", loop: true );
		Assert.IsNull( animator.GetVertexPositions( 0 ) );
		StringAssert.Contains( animator.VertexLimitations.Single(), "12-byte block" );
	}

	/// <summary>The default ride load path strips header flag 0x4, so relative-animation models play their vertex tracks in set mode (RIDES-043).</summary>
	[TestMethod]
	public void RelativeAnimationModelsPlayInSetMode()
	{
		var data = Md2ModelFileTests.CreateGeometry();
		data[0x30] |= (byte)ModelFile.RelativeAnimationFlag;
		var model = new ModelFile( new MemoryStream( data ) );
		Assert.AreEqual( ModelFile.RelativeAnimationFlag, model.HeaderFlags & ModelFile.RelativeAnimationFlag );
		var animator = new ObjectAnimator( model, keepsPoseOnClipChange: true );
		var clip = Vertex( 30 );
		animator.Play( 0, clip, "relative", loop: true );
		Assert.AreEqual( 0, animator.VertexLimitations.Count );
		var expected = new NVector3[4];
		clip.Tracks[0].VertexAnimation!.ApplyPose( 0, expected );
		CollectionAssert.AreEqual( expected, animator.GetVertexPositions( 0 )!.ToArray() );
		animator.Play( 0, Rigid(), "rigid", loop: true );
		Assert.IsNotNull( animator.GetVertexPositions( 0 ), "the fixed-item gate applies as for any set-mode model" );
	}

	[TestMethod]
	public void CanonicalStateFollowsTheChannels()
	{
		ulong Hash( ObjectAnimator animator )
		{
			var hash = new StateHasher();
			animator.AddCanonicalState( hash );
			return hash.Value;
		}
		var model = Geometry();
		var first = new ObjectAnimator( model );
		var second = new ObjectAnimator( model );
		foreach ( var animator in new[] { first, second } )
		{
			animator.Trigger( 0, Vertex( 30 ), "m1", 5, 0, loop: false );
			animator.Advance( 0.4 );
		}
		Assert.AreEqual( Hash( first ), Hash( second ) );
		second.Advance( 1 );
		Assert.AreNotEqual( Hash( first ), Hash( second ), "the ended flag and clock are hashed" );
		first.Advance( 1 );
		Assert.AreEqual( Hash( first ), Hash( second ) );
		first.Trigger( 0, Vertex( 10 ), "m2", 5, 1, loop: false );
		Assert.AreNotEqual( Hash( first ), Hash( second ) );
	}

	/// <summary>Turns the synthetic vertex record into a 12-byte block record (flag 0x5000) with a four-position pose.</summary>
	private static byte[] TwelveByte( byte[] data )
	{
		var trailer = data.Length - 72;
		var grown = new byte[data.Length + 12 + 16 + 48];
		data.AsSpan( 0, trailer ).CopyTo( grown );
		data.AsSpan( trailer ).CopyTo( grown.AsSpan( trailer + 76 ) );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( grown.AsSpan( 0x98 ), (uint)(trailer + 76) );
		var block = trailer;
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( grown.AsSpan( block ), (uint)(block + 12) );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( grown.AsSpan( block + 4 ), 4 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( grown.AsSpan( block + 8 ), (uint)(block + 28) );
		for ( var index = 0; index < 4; index++ )
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( grown.AsSpan( block + 12 + index * 4 ), (uint)index );
		const int records = 0x188;
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( grown.AsSpan( records + 4 ), 0x5000 );
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( grown.AsSpan( records + 40 ), (uint)block );
		return grown;
	}
}
