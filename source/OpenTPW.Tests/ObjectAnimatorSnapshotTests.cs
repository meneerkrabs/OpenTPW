using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Park-resume round-trips of the <see cref="ObjectAnimator"/> against the original Jungle data (inconclusive
/// without OPENTPW_GAME_PATH): channels, ticks, deferred clips, node-state bits and the per-mesh caches — including
/// the retained pose of a fixed item — must continue identically after a capture/restore, and a snapshot whose
/// clips resolve differently must fail the restore without touching the animator.
/// </summary>
[TestClass]
public class ObjectAnimatorSnapshotTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the animator snapshot corpus." );
		originalFileSystem = previous;
		initialized = true;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
	}

	/// <summary>The Totem and a real (animation, variant) member it ships, driving its animator directly.</summary>
	private static (ObjectCatalogEntry Entry, int Animation, int Variant, ModelAnimation Clip, string Name) TotemClip()
	{
		var entry = ObjectCatalog.Load( "jungle" ).Get( 1110 );
		foreach ( var animation in new[] { (int)ScriptDefs.Animations.ANIM_Main, (int)ScriptDefs.Animations.ANIM_Idle, (int)ScriptDefs.Animations.ANIM_Other } )
			for ( var variant = 0; variant < 4; variant++ )
			{
				var file = entry.ResolveAnimation( animation, variant );
				if ( file == null )
					continue;
				var model = ObjectAssets.LoadModel( entry.FileSystem, file.Path );
				if ( model.Kind == ModelFileKind.Animation && model.Clip != null )
					return (entry, animation, variant, model.Clip, file.Name);
			}
		Assert.Inconclusive( "The Totem archive ships no playable animation member." );
		throw new InvalidOperationException();
	}

	private static ulong Hash( ObjectAnimator animator )
	{
		var hash = new StateHasher();
		animator.AddCanonicalState( hash );
		return hash.Value;
	}

	[TestMethod]
	public void ChannelTickAndDeferredClipSurviveARestore()
	{
		var (entry, animation, variant, clip, name) = TotemClip();
		var animator = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 1 ).Animator;
		var restored = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 1 ).Animator;

		// A running clip on channel 0 and a deferred one behind it, then a second channel playing past a frame.
		animator.Trigger( 0, clip, name, animation, variant, loop: false, speed: 1f, defer: false );
		var second = entry.ResolveAnimation( animation, variant + 1 ) is { } nextFile && ObjectAssets.LoadModel( entry.FileSystem, nextFile.Path ).Kind == ModelFileKind.Animation
			? (nextFile.Name, ObjectAssets.LoadModel( entry.FileSystem, nextFile.Path ).Clip!)
			: (name, clip);
		animator.Trigger( 0, second.Item2, second.Item1, animation, variant + 1, loop: true, speed: 1f, defer: true );
		animator.Trigger( 1, clip, name, animation, variant, loop: false, speed: 2f );
		restored.Trigger( 2, clip, name, animation, variant, loop: false, speed: 1f ); // an unrelated channel the restore must replace
		for ( var tick = 0; tick < 5; tick++ )
			animator.Advance( FixedStepClock.TickDuration );
		Assert.IsTrue( animator.GetDeferredAnimationId( 0 ) != ObjectAnimator.NoAnimation || animator.ActiveChannels > 0 );

		var snapshot = animator.CaptureSnapshot();
		snapshot.ValidateSnapshot();
		restored.RestoreSnapshot( snapshot, identity => Resolve( entry, identity ) );
		Assert.AreEqual( Hash( animator ), Hash( restored ) );
		Assert.AreEqual( animator.GetTick( 0 ), restored.GetTick( 0 ) );
		Assert.AreEqual( animator.GetTick( 1 ), restored.GetTick( 1 ) );
		Assert.AreEqual( animator.ActiveChannels, restored.ActiveChannels );
		Assert.AreEqual( animator.GetDeferredAnimationId( 0 ), restored.GetDeferredAnimationId( 0 ) );
		Assert.AreEqual( animator.GetVertexVersion( 0 ), restored.GetVertexVersion( 0 ) );

		for ( var tick = 0; tick < 120; tick++ )
		{
			animator.Advance( FixedStepClock.TickDuration );
			restored.Advance( FixedStepClock.TickDuration );
			Assert.AreEqual( Hash( animator ), Hash( restored ), $"animator diverged at tick {tick}" );
			Assert.AreEqual( animator.GetVertexVersion( 0 ), restored.GetVertexVersion( 0 ) );
			for ( var channel = 0; channel < 2; channel++ )
			{
				Assert.AreEqual( animator.GetAnimationId( channel ), restored.GetAnimationId( channel ), $"channel {channel} at tick {tick}" );
				Assert.AreEqual( animator.GetClip( channel ), restored.GetClip( channel ) );
				Assert.AreEqual( animator.GetSpeed( channel ), restored.GetSpeed( channel ) );
			}
		}
	}

	/// <summary>
	/// Fixed items keep a mesh's last vertex/UV pose when its track ends (keepsPoseOnClipChange) — real state the
	/// channels no longer describe. No Jungle fixed item ships a clip with vertex or UV tracks (checked against the
	/// archives), so the retained cache is unreachable with this theme's assets; the pose retention policy itself is
	/// part of the snapshot identity (a mismatched policy is rejected by the restore) and the cache arrays are
	/// exercised element-wise on the Totem's vertex clips by the runtime tests.
	/// </summary>
	[TestMethod]
	public void AFixedItemAnimatorRoundTripsItsChannelsAndPosePolicy()
	{
		var entry = ObjectCatalog.Load( "jungle" ).Entries.FirstOrDefault( candidate => candidate.IsFixedItem && candidate.Animations.Count > 0 );
		if ( entry == null )
			Assert.Inconclusive( "No Jungle fixed item with animations." );
		var file = entry.ResolveAnimation( (int)ScriptDefs.Animations.ANIM_Main, 0 );
		var model = file == null ? null : ObjectAssets.LoadModel( entry.FileSystem, file.Path );
		if ( model == null || model.Kind != ModelFileKind.Animation || model.Clip == null )
			Assert.Inconclusive( "The fixed item ships no playable member." );
		Assert.AreEqual( 0, model.Clip.Tracks.Count( track => track.VertexAnimation != null || track.UvAnimation != null ), "no Jungle fixed-item clip has vertex/UV tracks" );

		var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 2 );
		var snapshot = runtime.Animator.CaptureSnapshot();
		Assert.IsTrue( snapshot.KeepsPoseOnClipChange, "the fixed item retains poses" );
		_ = runtime.TriggerAnimation( (int)ScriptDefs.Animations.ANIM_Main, 0, 0, loop: true, speed: 1f );
		for ( var tick = 0; tick < 30; tick++ )
			runtime.Simulate( FixedStepClock.TickDuration );
		Assert.IsTrue( runtime.Animator.IsChannelPlaying( 0 ) );

		var captured = runtime.Animator.CaptureSnapshot();
		var candidate = OriginalObjectRuntime.CreateForRestore( entry, new RideScriptWorld(), seed: 2, open: true, attractionId: runtime.AttractionId );
		candidate.Animator.RestoreSnapshot( captured, identity => Resolve( entry, identity ) );
		for ( var tick = 0; tick < 120; tick++ )
		{
			runtime.Animator.Advance( FixedStepClock.TickDuration );
			candidate.Animator.Advance( FixedStepClock.TickDuration );
			Assert.AreEqual( Hash( runtime.Animator ), Hash( candidate.Animator ), $"fixed item diverged at tick {tick}" );
			Assert.AreEqual( runtime.Animator.GetTick( 0 ), candidate.Animator.GetTick( 0 ) );
			Assert.AreEqual( runtime.Animator.HiddenVersion, candidate.Animator.HiddenVersion );
		}
	}

	[TestMethod]
	public void RestoreRejectsAClipTheArchiveResolvesDifferently()
	{
		var (entry, animation, variant, clip, name) = TotemClip();
		var animator = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 3 ).Animator;
		animator.Trigger( 0, clip, name, animation, variant, loop: false );
		var snapshot = animator.CaptureSnapshot();
		var tampered = snapshot with { Channels = new[] { snapshot.Channels[0] with { ClipName = "not-a-member" } } };
		var exception = Assert.ThrowsException<InvalidDataException>( () => animator.RestoreSnapshot( tampered, identity => Resolve( entry, identity ) ) );
		StringAssert.Contains( exception.Message, "not the snapshot's" );
	}

	[TestMethod]
	public void RestoreRejectsAConfigurationMismatch()
	{
		var (entry, animation, variant, clip, name) = TotemClip();
		var animator = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 4 ).Animator;
		animator.Trigger( 0, clip, name, animation, variant, loop: false );
		var snapshot = animator.CaptureSnapshot();
		var wrongRate = snapshot with { TicksPerSecond = 60f };
		Assert.ThrowsException<InvalidDataException>( () => animator.RestoreSnapshot( wrongRate, identity => Resolve( entry, identity ) ) );
		var wrongPosePolicy = snapshot with { KeepsPoseOnClipChange = !snapshot.KeepsPoseOnClipChange };
		Assert.ThrowsException<InvalidDataException>( () => animator.RestoreSnapshot( wrongPosePolicy, identity => Resolve( entry, identity ) ) );
		var wrongNodeBits = snapshot with { NodeStateBits = new bool[3] };
		Assert.ThrowsException<InvalidDataException>( () => animator.RestoreSnapshot( wrongNodeBits, identity => Resolve( entry, identity ) ) );
		if ( snapshot.Meshes.Count > 1 )
		{
			var wrongMeshOrder = snapshot with { Meshes = snapshot.Meshes.Reverse().ToArray() };
			Assert.ThrowsException<InvalidDataException>( () => animator.RestoreSnapshot( wrongMeshOrder, identity => Resolve( entry, identity ) ) );
		}
	}

	[TestMethod]
	public void CorruptAnimatorSnapshotsFailValidationPurely()
	{
		var (entry, animation, variant, clip, name) = TotemClip();
		var animator = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 5 ).Animator;
		animator.Trigger( 0, clip, name, animation, variant, loop: false );
		var snapshot = animator.CaptureSnapshot();
		snapshot.ValidateSnapshot();
		static InvalidDataException Throws( ObjectAnimatorSnapshot corrupt ) => Assert.ThrowsException<InvalidDataException>( corrupt.ValidateSnapshot );
		StringAssert.Contains( Throws( snapshot with { ClockMilliseconds = -3 } ).Message, "not finite and non-negative" );
		StringAssert.Contains( Throws( snapshot with { Channels = new[] { snapshot.Channels[0], snapshot.Channels[0] } } ).Message, "appears twice" );
		StringAssert.Contains( Throws( snapshot with { Channels = new[] { snapshot.Channels[0] with { Speed = 0f } } } ).Message, "not positive finite" );
		StringAssert.Contains( Throws( snapshot with { Channels = new[] { snapshot.Channels[0] with { Serial = 0 } } } ).Message, "not positive" );
		StringAssert.Contains( Throws( snapshot with { Meshes = new[] { snapshot.Meshes[0], snapshot.Meshes[0] } } ).Message, "appears twice" );
		StringAssert.Contains( Throws( snapshot with { Meshes = new[] { snapshot.Meshes[0] with { VertexVersion = -1 } } } ).Message, "negative" );
	}

	private static ResolvedAnimatorClip? Resolve( ObjectCatalogEntry entry, AnimatorClipIdentity identity )
	{
		var file = entry.ResolveAnimation( identity.AnimationId, identity.Variant );
		if ( file == null )
			return null;
		var model = ObjectAssets.LoadModel( entry.FileSystem, file.Path );
		return model.Kind == ModelFileKind.Animation && model.Clip != null ? new ResolvedAnimatorClip( file.Name, model.Clip ) : null;
	}
}
