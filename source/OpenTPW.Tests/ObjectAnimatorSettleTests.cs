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
		// the test is only meaningful if each clip alone leaves the opposite flag on the same node
		var longAlone = new ObjectAnimator( Geometry() );
		longAlone.Play( 0, Vertex( 30 ), "long", loop: false );
		var shortAlone = new ObjectAnimator( Geometry() );
		shortAlone.Play( 0, Vertex( 5 ), "short", loop: false );
		for ( var step = 0; step < 300; step++ )
		{
			longAlone.Advance( Tick );
			shortAlone.Advance( Tick );
		}
		Assert.AreNotEqual( longAlone.GetNodeStateBit( 0 ), shortAlone.GetNodeStateBit( 0 ), "the two clips end with opposite flags on node 0" );
		var hidden = animator.HiddenVersion;
		var pose = animator.PoseVersion;
		animator.Advance( Tick );
		animator.Advance( Tick );
		Assert.AreNotEqual( pose, animator.PoseVersion, "the update still runs" );
		Assert.AreNotEqual( hidden, animator.HiddenVersion, "and still toggles the bit it always toggled" );
	}

	/// <summary>Seeded random drives of two animators, one with the settle skip and one without, agree after every step in everything an update can touch.</summary>
	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 3 )]
	[DataRow( 4 )]
	[DataRow( 5 )]
	[DataRow( 6 )]
	public void TheSettleSkipChangesNothingUnderRandomDrives( int seed )
	{
		var clips = new System.Collections.Generic.Dictionary<string, ModelAnimation>
		{
			["rigid"] = Rigid(), ["vertex30"] = Vertex( 30 ), ["vertex5"] = Vertex( 5 ),
			["uv"] = Clip( Md2VertexAnimationTests.CreateUvAnimation() ), ["rotation"] = Clip( Md2AnimationTests.CreateRotationOnlyAnimation() ),
		};
		var names = clips.Keys.ToArray();
		var model = Geometry();
		var fast = new ObjectAnimator( model );
		var slow = new ObjectAnimator( model ) { DisableSettleSkip = true };
		var random = new System.Random( seed );
		ResolvedAnimatorClip? Resolve( AnimatorClipIdentity identity ) => clips.TryGetValue( identity.Name, out var clip ) ? new ResolvedAnimatorClip( identity.Name, clip ) : null;

		for ( var step = 0; step < 600; step++ )
		{
			var operation = random.Next( 16 );
			var channel = random.Next( 3 );
			var name = names[random.Next( names.Length )];
			var loop = random.Next( 2 ) == 0;
			var stopAll = random.Next( 8 ) == 0;
			var seconds = operation == 7 ? 0 : random.Next( 1, 4 ) * Tick * 3;
			foreach ( var animator in new[] { fast, slow } )
			{
				switch ( operation )
				{
					case 0: animator.Play( channel, clips[name], name, loop ); break;
					case 1: animator.Trigger( channel, clips[name], name, 5 + channel, 0, loop, defer: true ); break;
					case 2: animator.Stop( channel ); break;
					case 3: animator.Flush( channel ); break;
					case 4 when stopAll: animator.StopAll(); break;
					case 5: animator.RestoreSnapshot( animator.CaptureSnapshot(), Resolve ); break;
					case 6:
						for ( var slot = 0; slot < model.Textures.Count; slot++ )
							if ( model.Textures[slot].FrameNames.Count > 1 )
								animator.SetTextureFrameAvailability( slot, Enumerable.Range( 0, model.Textures[slot].FrameNames.Count ).Select( frame => frame % 2 == 0 ).ToArray() );
						break;
					default: animator.Advance( seconds ); break;
				}
			}
			AssertSame( fast, slow, model, $"seed {seed}, step {step}, operation {operation}" );
		}
		Assert.IsTrue( fast.PoseVersion <= slow.PoseVersion, "the skip never updates more often" );
	}

	private static void AssertSame( ObjectAnimator a, ObjectAnimator b, ModelFile model, string where )
	{
		CollectionAssert.AreEqual( a.NodeTransforms.ToArray(), b.NodeTransforms.ToArray(), where );
		for ( var node = 0; node < model.Nodes.Count; node++ )
			Assert.AreEqual( a.GetNodeStateBit( node ), b.GetNodeStateBit( node ), $"{where}, node {node}" );
		for ( var slot = 0; slot < model.Textures.Count; slot++ )
			Assert.AreEqual( a.GetTextureFrame( slot ), b.GetTextureFrame( slot ), $"{where}, slot {slot}" );
		for ( var mesh = 0; mesh < model.Meshes.Count; mesh++ )
		{
			Assert.AreEqual( a.GetVertexVersion( mesh ), b.GetVertexVersion( mesh ), $"{where}, mesh {mesh} version" );
			CollectionAssert.AreEqual( a.GetVertexPositions( mesh )?.ToArray(), b.GetVertexPositions( mesh )?.ToArray(), $"{where}, mesh {mesh} positions" );
			CollectionAssert.AreEqual( a.GetTexCoords( mesh )?.ToArray(), b.GetTexCoords( mesh )?.ToArray(), $"{where}, mesh {mesh} texture coordinates" );
		}
		Assert.AreEqual( a.HiddenVersion, b.HiddenVersion, where );
		Assert.AreEqual( a.TextureFrameVersion, b.TextureFrameVersion, where );
		var (hashA, hashB) = (new StateHasher(), new StateHasher());
		a.AddCanonicalState( hashA );
		b.AddCanonicalState( hashB );
		Assert.AreEqual( hashA.Value, hashB.Value, where );
	}
}
