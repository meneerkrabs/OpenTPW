using System.Linq;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// <see cref="ObjectAnimator.Advance"/> walks every node only while some channel plays: once every clip has ended and one update found nothing to change, the idle ticks that follow
/// keep the pose, the node-state bits and every version counter as they are (a skipped update is one that would have changed nothing).
/// </summary>
[TestClass]
public class ObjectAnimatorSettleTests
{
	private static ModelFile Geometry() => new( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) );
	private static ModelAnimation Clip( byte[] data ) => new ModelFile( new MemoryStream( data ) ).Clip!;
	private static ModelAnimation Rigid() => Clip( Md2AnimationTests.CreateAnimation() );
	private static ModelAnimation Vertex( ushort duration ) => Clip( Md2VertexAnimationTests.CreateAnimation( 0, duration ) );

	private const double Tick = 1d / 60;

	[TestMethod]
	public void AnEndedClipSettlesAndIdleTicksChangeNothing()
	{
		var animator = new ObjectAnimator( Geometry() );
		animator.Play( 0, Rigid(), "rigid", loop: false );
		animator.Play( 1, Vertex( 30 ), "vertex", loop: false );
		for ( var step = 0; step < 300; step++ )
			animator.Advance( Tick );
		Assert.IsFalse( animator.IsPlaying, "both clips have ended" );
		// the first idle tick may still update; after it the animator is settled
		animator.Advance( Tick );
		var (pose, hidden, texture, vertices) = (animator.PoseVersion, animator.HiddenVersion, animator.TextureFrameVersion, animator.GetVertexVersion( 0 ));
		var transforms = animator.NodeTransforms.ToArray();
		var bits = new[] { animator.GetNodeStateBit( 0 ), animator.GetNodeStateBit( 1 ) };
		for ( var step = 0; step < 50; step++ )
			animator.Advance( Tick );
		Assert.AreEqual( pose, animator.PoseVersion, "no update ran" );
		Assert.AreEqual( hidden, animator.HiddenVersion );
		Assert.AreEqual( texture, animator.TextureFrameVersion );
		Assert.AreEqual( vertices, animator.GetVertexVersion( 0 ) );
		CollectionAssert.AreEqual( transforms, animator.NodeTransforms.ToArray() );
		CollectionAssert.AreEqual( bits, new[] { animator.GetNodeStateBit( 0 ), animator.GetNodeStateBit( 1 ) } );
	}

	[TestMethod]
	public void AnAnimatorWithoutChannelsSettlesToo()
	{
		var animator = new ObjectAnimator( Geometry() );
		animator.Advance( Tick );
		var pose = animator.PoseVersion;
		var transforms = animator.NodeTransforms.ToArray();
		for ( var step = 0; step < 20; step++ )
			animator.Advance( Tick );
		Assert.AreEqual( pose, animator.PoseVersion );
		CollectionAssert.AreEqual( transforms, animator.NodeTransforms.ToArray() );
	}

	[TestMethod]
	public void ANewClipOnASettledAnimatorUpdatesAgain()
	{
		var animator = new ObjectAnimator( Geometry() );
		animator.Play( 0, Rigid(), "rigid", loop: false );
		for ( var step = 0; step < 300; step++ )
			animator.Advance( Tick );
		var pose = animator.PoseVersion;
		animator.Advance( Tick );
		Assert.AreEqual( pose, animator.PoseVersion, "settled" );
		animator.Play( 0, Rigid(), "again", loop: false );
		Assert.AreNotEqual( pose, animator.PoseVersion, "a trigger updates the pose" );
		var restarted = animator.PoseVersion;
		animator.Advance( Tick );
		Assert.AreNotEqual( restarted, animator.PoseVersion, "the new clip plays" );
	}

	/// <summary>Two ended channels that leave opposite node-state bits on one mesh toggle it on every update: that update has an effect each time, so it is never skipped.</summary>
	[TestMethod]
	public void ConflictingEndedChannelsKeepUpdating()
	{
		var animator = new ObjectAnimator( Geometry() );
		animator.Play( 0, Vertex( 30 ), "long", loop: false );
		animator.Play( 1, Vertex( 5 ), "short", loop: false );
		for ( var step = 0; step < 300; step++ )
			animator.Advance( Tick );
		Assert.IsFalse( animator.IsPlaying );
		var hidden = animator.HiddenVersion;
		var pose = animator.PoseVersion;
		animator.Advance( Tick );
		animator.Advance( Tick );
		Assert.AreNotEqual( pose, animator.PoseVersion, "the update still runs" );
		Assert.AreNotEqual( hidden, animator.HiddenVersion, "and still toggles the bit it always toggled" );
	}
}
