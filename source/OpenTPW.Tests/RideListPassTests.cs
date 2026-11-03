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

	/// <summary>A node an earlier clip hid and a later clip's track writes without listing it: the reset clears it for the drawn pose only.</summary>
	[TestMethod]
	public void TheResetChangesTheDrawnStateWhileSavesAndPicksKeepTheSimulations()
	{
		var animator = new ObjectAnimator( Geometry() );
		animator.Play( 0, Clip( Md2VertexAnimationTests.CreateAnimationWithNodeList( 1, 30, 0 ) ), "hider", loop: false );
		animator.Advance( 0.01 );
		animator.Stop( 0 );
		// the writer's first toggle moves from tick 0 to tick 2 (-2: set from there), so before it the node keeps what the first clip left
		var writer = Md2VertexAnimationTests.CreateAnimationWithNodeList( 0, 30 );
		var toggles = writer.AsSpan().IndexOf( new byte[] { 0x00, 0x00, 0x03, 0x00, 0xF9, 0xFF } );
		Assert.IsTrue( toggles >= 0, "the fixture's toggles 0, +3, -7" );
		writer[toggles] = 0xFE;
		writer[toggles + 1] = 0xFF;
		animator.Play( 1, Clip( writer ), "writer", loop: true );
		animator.Advance( 0.01 );
		Assert.IsTrue( animator.IsNodeHidden( 0 ), "the bit set by the first clip's list is still there; the writer's first toggle comes at tick 2" );
		var before = System.Text.Json.JsonSerializer.Serialize( animator.CaptureSnapshot() );

		animator.ResetToStored();

		Assert.IsFalse( animator.IsNodeHidden( 0 ), "the drawn pose lost the bit (0x4726D0 clears what a track writes, the rebind only restores what a list names)" );
		Assert.IsTrue( animator.IsNodeHiddenInSimulation( 0 ), "the simulation's view, which picks read, did not change" );
		Assert.AreEqual( before, System.Text.Json.JsonSerializer.Serialize( animator.CaptureSnapshot() ), "and neither did the save" );
		animator.Advance( 0.01 );
		Assert.IsFalse( animator.IsNodeHidden( 0 ), "a step that writes nothing to the node leaves the reset where it is (0x4726D0's result is the only copy, tp.exe keeps it until the next writer)" );
		Assert.IsTrue( animator.IsNodeHiddenInSimulation( 0 ), "the simulation steps from its own state meanwhile" );
		animator.Advance( 0.07 );
		Assert.IsTrue( animator.IsNodeHidden( 0 ), "the writer's first toggle (tick 2) writes the bit, and the drawn state is the simulation's again" );
		Assert.AreEqual( animator.IsNodeHiddenInSimulation( 0 ), animator.IsNodeHidden( 0 ) );
	}

	/// <summary>RIDES-032: the reset stays through any number of steps that do not touch an element, and a save in between is the simulation's.</summary>
	[TestMethod]
	public void TheResetStaysThroughStepsThatWriteNothingToTheNode()
	{
		var plain = new ObjectAnimator( Geometry() );
		var drawn = new ObjectAnimator( Geometry() );
		ObjectAnimator[] both = [plain, drawn];
		foreach ( var animator in both )
		{
			animator.Play( 0, Clip( Md2VertexAnimationTests.CreateAnimationWithNodeList( 1, 30, 0 ) ), "hider", loop: false );
			animator.Advance( 0.01 );
			animator.Stop( 0 );
			var writer = Md2VertexAnimationTests.CreateAnimationWithNodeList( 0, 30 );
			var toggles = writer.AsSpan().IndexOf( new byte[] { 0x00, 0x00, 0x03, 0x00, 0xF9, 0xFF } );
			writer[toggles] = 0xFE;
			writer[toggles + 1] = 0xFF;
			animator.Play( 1, Clip( writer ), "writer", loop: true );
			animator.Advance( 0.01 );
		}
		drawn.ResetToStored();
		Assert.IsFalse( drawn.IsNodeHidden( 0 ) );
		drawn.Advance( 0.005 );
		plain.Advance( 0.005 );
		drawn.Advance( 0.005 );
		plain.Advance( 0.005 );
		Assert.IsFalse( drawn.IsNodeHidden( 0 ), "still the reset after several steps before the toggle" );
		Assert.AreEqual(
			System.Text.Json.JsonSerializer.Serialize( plain.CaptureSnapshot() ),
			System.Text.Json.JsonSerializer.Serialize( drawn.CaptureSnapshot() ), "the saved simulation never saw the reset" );
		var left = new StateHasher(); plain.AddCanonicalState( left );
		var right = new StateHasher(); drawn.AddCanonicalState( right );
		Assert.AreEqual( left.Value, right.Value, "nor did the hash" );
		for ( var step = 0; step < 10; step++ )
		{
			drawn.Advance( 0.01 );
			plain.Advance( 0.01 );
		}
		Assert.AreEqual( plain.IsNodeHidden( 0 ), drawn.IsNodeHidden( 0 ), "once the toggle wrote the bit both agree for good" );
		Assert.AreEqual(
			System.Text.Json.JsonSerializer.Serialize( plain.CaptureSnapshot() ),
			System.Text.Json.JsonSerializer.Serialize( drawn.CaptureSnapshot() ) );
	}

	/// <summary>RIDES-032: the steps after a reset, with the drawn state still apart from the simulation's, allocate nothing either.</summary>
	[TestMethod]
	public void StepsWithALiveResetAllocateNothing()
	{
		var animator = new ObjectAnimator( Geometry() );
		animator.Play( 0, Clip( Md2VertexAnimationTests.CreateAnimationWithNodeList( 1, 30, 0 ) ), "hider", loop: true );
		animator.Advance( 0.01 );
		animator.ResetToStored();
		for ( var warm = 0; warm < 5; warm++ )
			animator.Advance( 0 );
		var before = System.GC.GetAllocatedBytesForCurrentThread();
		for ( var index = 0; index < 100; index++ )
			animator.Advance( 0 );
		Assert.AreEqual( 0, System.GC.GetAllocatedBytesForCurrentThread() - before );
		for ( var warm = 0; warm < 5; warm++ )
			animator.Advance( 0.001 );
		before = System.GC.GetAllocatedBytesForCurrentThread();
		for ( var index = 0; index < 100; index++ )
			animator.Advance( 0.001 );
		Assert.AreEqual( 0, System.GC.GetAllocatedBytesForCurrentThread() - before, "steps that sample allocate nothing" );
	}
}
