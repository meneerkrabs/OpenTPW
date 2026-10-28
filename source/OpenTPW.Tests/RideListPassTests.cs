using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// RIDES-032 / MD2-900 / PTCL-900: the list pass and the render callback of the original (tp.exe 0x44E410, 0x463030, 0x44E380, 0x4726D0, 0x44AB30) as far as they show: the stored pose
/// an instance returns to when it was left out of the previous frame's drawing, and the key table the EVENT keys read.
/// </summary>
[TestClass]
[DoNotParallelize]
public class RideListPassTests
{
	private static ModelFile Geometry() => new( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) );
	private static ModelAnimation Clip( byte[] data ) => new ModelFile( new MemoryStream( data ) ).Clip!;

	/// <summary>0x4726D0 clears node-state bit 0x10 on the clip's listed nodes and track nodes (unless bit 31), then the rebind (0x472D70) sets it again on every listed node; only a track-written node that is not listed loses it.</summary>
	[TestMethod]
	public void TheReturnFromTheListPassRebindsTheListedNodes()
	{
		var animator = new ObjectAnimator( Geometry() );
		animator.Play( 0, Clip( Md2VertexAnimationTests.CreateAnimationWithNodeList( 1, 30, 0, 1, 7 ) ), "list", loop: false );
		animator.Advance( 0.2 );
		Assert.IsTrue( animator.IsNodeHidden( 0 ), "the clip start hid the listed mesh node; no key of the track shows it" );
		Assert.IsFalse( animator.GetNodeStateBit( 1 ), "the toggles of node 1 cleared it from tick 3" );
		var version = animator.HiddenVersion;
		var poseVersion = animator.PoseVersion;

		animator.ResetToStored();

		Assert.IsTrue( animator.IsNodeHidden( 0 ), "the clear is followed by the rebind, which hides the listed mesh node again" );
		Assert.IsFalse( animator.GetNodeStateBit( 1 ), "the track's own key is sampled again at the clip's tick" );
		Assert.AreNotEqual( version, animator.HiddenVersion );
		Assert.AreNotEqual( poseVersion, animator.PoseVersion, "the pose is sampled again" );
	}

	[TestMethod]
	public void ANodeWithBit31IsLeftAlone()
	{
		var animator = new ObjectAnimator( Geometry() );
		animator.Play( 0, Clip( Md2VertexAnimationTests.CreateAnimationWithNodeList( 1, 30, 0, 1, 7 ) ), "list", loop: false );
		animator.SetNodeHidden( 0, true );
		animator.ResetToStored();
		Assert.IsTrue( animator.IsNodeHidden( 0 ), "a node the walk pinned (state bit 31) keeps its state" );
	}

	[TestMethod]
	public void TheResetKeepsTheScriptVisibleState()
	{
		var plain = new ObjectAnimator( Geometry() );
		var reset = new ObjectAnimator( Geometry() );
		foreach ( var animator in new[] { plain, reset } )
		{
			animator.Play( 0, Clip( Md2AnimationTests.CreateAnimation() ), "rigid", loop: true );
			animator.Advance( 0.3 );
		}
		reset.ResetToStored();
		var left = new StateHasher(); plain.AddCanonicalState( left );
		var right = new StateHasher(); reset.AddCanonicalState( right );
		Assert.AreEqual( left.Value, right.Value, "channels, clock and ticks are the simulation's; the reset touches presentation only" );
		for ( var node = 0; node < plain.NodeTransforms.Count; node++ )
			Assert.AreEqual( plain.NodeTransforms[node], reset.NodeTransforms[node], "the pose of the same tick is sampled again" );
	}

	/// <summary>The reset is what the drawn pose shows: the simulation's save, its picks and its hidden state stay what they were without it.</summary>
	[TestMethod]
	public void ASaveAfterDrawingEqualsASaveWithoutDrawing()
	{
		ObjectAnimator Make()
		{
			var animator = new ObjectAnimator( Geometry() );
			animator.Play( 0, Clip( Md2VertexAnimationTests.CreateAnimationWithNodeList( 1, 30, 0, 1, 7 ) ), "list", loop: false );
			animator.Advance( 0.2 );
			return animator;
		}
		var plain = Make();
		var drawn = Make();
		drawn.ResetToStored();
		for ( var node = 0; node < plain.NodeTransforms.Count; node++ )
			Assert.AreEqual( plain.IsNodeHiddenInSimulation( node ), drawn.IsNodeHiddenInSimulation( node ), $"node {node} picks the same" );
		Assert.AreEqual(
			System.Text.Json.JsonSerializer.Serialize( plain.CaptureSnapshot() ),
			System.Text.Json.JsonSerializer.Serialize( drawn.CaptureSnapshot() ), "the snapshot is the simulation's" );
		drawn.Advance( 0.1 );
		plain.Advance( 0.1 );
		Assert.AreEqual(
			System.Text.Json.JsonSerializer.Serialize( plain.CaptureSnapshot() ),
			System.Text.Json.JsonSerializer.Serialize( drawn.CaptureSnapshot() ), "the next simulation step starts from the simulation's state" );
	}
}
