using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Texture-frame tracks applied at runtime (RIDES-035): the winning channel's <c>FrameAt</c> selects the
/// frame a model texture slot shows, multi-channel conflicts resolve like every node component, looping
/// clips cycle, invalid slot and frame references are refused, and render parts carry the MD2 slot each
/// texture came from. Semantics from the decoded corpus (docs/MD2-MODELS.md); no original asset needed.
/// </summary>
[TestClass]
public class ObjectTextureFrameTests
{
	[TestMethod]
	public void TheWinningChannelSelectsTheSlotFrame()
	{
		var model = new ModelFile( new MemoryStream( Geometry( frames: 4 ) ) );
		Assert.AreEqual( 4, model.Textures[0].FrameNames.Count );
		var clip = Clip( 0, (0, 0), (6, 1), (12, 2) );
		var animator = new ObjectAnimator( model );
		Assert.IsFalse( animator.HasTextureFrames );
		Assert.AreEqual( 0, animator.GetTextureFrame( 0 ) );
		animator.Play( 0, clip, "synth", loop: false );
		Assert.IsTrue( animator.HasTextureFrames );

		// The clip update samples at the channel tick: frame 1 from tick 6, frame 2 from tick 12, the last key holds.
		animator.Advance( 6.0 / animator.TicksPerSecond );
		Assert.AreEqual( 1, animator.GetTextureFrame( 0 ) );
		animator.Advance( 6.0 / animator.TicksPerSecond );
		Assert.AreEqual( 2, animator.GetTextureFrame( 0 ) );
		animator.Advance( 5.0 / animator.TicksPerSecond );
		Assert.AreEqual( 2, animator.GetTextureFrame( 0 ), "the last key holds" );

		// One millisecond past the end the non-looping clip ends frozen; the frame stays at its last key.
		animator.Advance( 1.0 / animator.TicksPerSecond );
		Assert.AreEqual( 2, animator.GetTextureFrame( 0 ), "an ended clip keeps its last frame" );
	}

	[TestMethod]
	public void LoopingClipsCycleTheFrameTrack()
	{
		var model = new ModelFile( new MemoryStream( Geometry( frames: 4 ) ) );
		var clip = Clip( 0, (0, 0), (10, 1) );
		var animator = new ObjectAnimator( model );
		animator.Play( 0, clip, "synth", loop: true );
		// The channel clock counts whole milliseconds: 334 ms gives tick 10 (30 x 334 / 1000 truncated).
		animator.Advance( 0.334 );
		Assert.AreEqual( 1, animator.GetTextureFrame( 0 ) );
		// 100 ms later the 12-tick clip is strictly past its end (tick 13) and replays from the carry: tick 1.
		animator.Advance( 0.1 );
		Assert.AreEqual( 0, animator.GetTextureFrame( 0 ), "the loop restarts the frame track" );
		animator.Advance( 0.334 );
		Assert.AreEqual( 1, animator.GetTextureFrame( 0 ), "the second cycle reaches key 1 again" );
	}

	[TestMethod]
	public void HigherChannelsWinAndNullSamplesKeepTheLowerChannelFrame()
	{
		var model = new ModelFile( new MemoryStream( Geometry( frames: 4 ) ) );
		var lower = Clip( 0, 40, (0, 0), (30, 1) );
		// First key at tick 10: before it the track leaves the slot unchanged (FrameAt null).
		var higher = Clip( 0, 40, (10, 2) );
		var animator = new ObjectAnimator( model );
		animator.Play( 0, lower, "lower", loop: false );
		animator.Advance( 1.0 );
		Assert.AreEqual( 1, animator.GetTextureFrame( 0 ), "the lower channel's key 1 shows" );

		animator.Play( 1, higher, "higher", loop: false );
		Assert.AreEqual( 1, animator.GetTextureFrame( 0 ), "before the higher channel's first key the slot is unchanged" );
		// The higher clip starts at a whole millisecond; 400 ms in its tick passes 10 and frame 2 wins.
		animator.Advance( 0.4 );
		Assert.AreEqual( 2, animator.GetTextureFrame( 0 ), "the higher channel's frame wins from its first key" );

		// Stopping the higher channel returns the slot to the lower channel's frame, and StopAll resets it.
		animator.Stop( 1 );
		Assert.AreEqual( 1, animator.GetTextureFrame( 0 ), "the remaining channel's frame shows again" );
		animator.StopAll();
		Assert.AreEqual( 0, animator.GetTextureFrame( 0 ), "a slot no clip addresses shows frame 0" );
	}

	[TestMethod]
	public void InvalidSlotsAndFramesAreRefused()
	{
		var model = new ModelFile( new MemoryStream( Geometry( frames: 2 ) ) );
		Assert.AreEqual( 1, model.Textures.Count );
		// A slot the model does not have (the corpus case Pmegacostm) never addresses a slot.
		var animator = new ObjectAnimator( model );
		animator.Play( 0, Clip( 1, (0, 1) ), "slot", loop: false );
		animator.Advance( 1.0 );
		Assert.IsFalse( animator.HasTextureFrames, "a track outside the model's slots never addresses a slot" );

		// A frame past the slot's two names addresses the slot but is not applied.
		var frames = new ObjectAnimator( model );
		frames.Play( 0, Clip( 0, (0, 5) ), "frame", loop: false );
		frames.Advance( 1.0 );
		Assert.IsTrue( frames.HasTextureFrames );
		Assert.AreEqual( 0, frames.GetTextureFrame( 0 ), "a frame outside the slot's names is not applied" );
		Assert.AreEqual( 0, frames.TextureFrameVersion );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => frames.GetTextureFrame( 1 ) );

		// The trailer flag gates the tracks: without bit 0x2 a decoded track is not applied.
		var gated = new ObjectAnimator( model );
		gated.Play( 0, Clip( 0, false, (0, 1) ), "gated", loop: false );
		gated.Advance( 1.0 );
		Assert.IsFalse( gated.HasTextureFrames );
		Assert.AreEqual( 0, gated.GetTextureFrame( 0 ) );
	}

	[TestMethod]
	public void TheVersionCountsOnlyRealChanges()
	{
		var model = new ModelFile( new MemoryStream( Geometry( frames: 4 ) ) );
		var animator = new ObjectAnimator( model );
		animator.Play( 0, Clip( 0, (0, 0), (10, 1) ), "synth", loop: false );
		var version = animator.TextureFrameVersion;
		animator.Advance( 0 );
		Assert.AreEqual( version, animator.TextureFrameVersion, "an unchanged tick does not change the frame" );
		// 334 ms of channel clock reach tick 10 (30 x 334 / 1000 truncated).
		animator.Advance( 0.334 );
		Assert.AreNotEqual( version, animator.TextureFrameVersion );
	}

	/// <summary>
	/// Two animators of one parsed model keep independent slot frames (the model is never written), and the
	/// texture-frame pass allocates nothing per update at a held frame.
	/// </summary>
	[TestMethod]
	public void InstancesAreIndependentAndTheFramePassAllocatesNothing()
	{
		var model = new ModelFile( new MemoryStream( Geometry( frames: 4 ) ) );
		var first = new ObjectAnimator( model );
		var second = new ObjectAnimator( model );
		first.Play( 0, Clip( 0, (0, 0), (30, 1) ), "on", loop: true );
		second.Play( 0, Clip( 0, (0, 0), (30, 1) ), "on", loop: true );
		first.Advance( 1.0 );
		Assert.AreEqual( 1, first.GetTextureFrame( 0 ) );
		Assert.AreEqual( 0, second.GetTextureFrame( 0 ), "the shared model's other instance is untouched" );
		Assert.AreEqual( 4, model.Textures[0].FrameNames.Count, "the parsed slot names stay" );

		// The same clip without trailer bit 0x2 runs the update without the frame pass; at a held frame
		// both paths must allocate the same (the channel update's own bookkeeping, nothing per frame track).
		var gated = new ObjectAnimator( model );
		gated.Play( 0, Clip( 0, false, (0, 0), (30, 1) ), "off", loop: true );
		gated.Advance( 1.0 );
		first.Advance( 0 );
		gated.Advance( 0 );
		long Allocated( ObjectAnimator animator )
		{
			var before = GC.GetAllocatedBytesForCurrentThread();
			for ( var index = 0; index < 100; index++ )
				animator.Advance( 0 );
			return GC.GetAllocatedBytesForCurrentThread() - before;
		}
		Assert.AreEqual( Allocated( gated ), Allocated( first ), "the texture-frame pass allocates nothing per update" );
	}

	[TestMethod]
	public void StableHigherChannelWinnerDoesNotInvalidateTextures()
	{
		var model = new ModelFile( new MemoryStream( Geometry( frames: 4 ) ) );
		var animator = new ObjectAnimator( model );
		animator.Play( 0, Clip( 0, (0, 1) ), "lower", loop: true );
		animator.Play( 1, Clip( 0, (0, 2) ), "higher", loop: true );
		var version = animator.TextureFrameVersion;
		animator.Advance( 0 );
		Assert.AreEqual( 2, animator.GetTextureFrame( 0 ) );
		Assert.AreEqual( version, animator.TextureFrameVersion, "intermediate lower-channel frames are not displayed" );
	}

	[TestMethod]
	public void StaticBatchesKeepIdenticalArtInDistinctModelSlots()
	{
		var model = new ModelFile( new MemoryStream( Geometry( frames: 4 ) ) );
		var mesh = model.Meshes.Single();
		model.Meshes.Clear();
		for ( var slot = 0; slot < 17; slot++ )
			model.Meshes.Add( new ModelFile.Mesh
			{
				NodeIndex = mesh.NodeIndex,
				Name = "slot" + slot,
				Vertices = mesh.Vertices,
				Indices = mesh.Indices,
				TexCoords = mesh.TexCoords,
				Normals = mesh.Normals,
				Materials = new[] { new ModelFile.MaterialData { Name = "shared", TextureIndex = slot } }
			} );
		var entry = new ObjectCatalogEntry( "test", ObjectCategory.Ride, "test", new ObjectSettings( Array.Empty<ObjectSettingsFile>() ),
			new ObjectShape( new[] { "*" } ), "model", null, null, Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(),
			Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( Path.GetTempPath() ), null );
		var parts = ObjectRenderParts.Build( entry, model );
		Assert.AreEqual( 2, parts.Count, "17 independent slots require two shader arrays" );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 17 ).ToArray(), parts.SelectMany( part => part.TextureSlots! ).ToArray() );
		Assert.IsTrue( parts.All( part => part.Textures.Length <= 16 ) );
		Assert.AreEqual( 16, parts[0].Vertices.Select( vertex => vertex.TexIndex ).Distinct().Count() );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void HeldFrameHistorySurvivesTheSaveCodecAndFreshRestore( bool invalidKey )
	{
		var model = new ModelFile( new MemoryStream( Geometry( frames: 4 ) ) );
		var selected = Clip( 0, (0, 2) );
		var holding = invalidKey ? Clip( 0, (0, 99) ) : Clip( 0, (10, 1) );
		var animator = new ObjectAnimator( model, holdsLastFrameAtClipEnd: true );
		animator.Play( 0, selected, "selected", loop: false );
		animator.Advance( 0.1 ); // Native held ID12: replacement skips outgoing cleanup.
		animator.Play( 0, holding, "holding", loop: false );
		Assert.AreEqual( 2, animator.GetTextureFrame( 0 ) );
		var snapshot = ParkResumeJson.Deserialize<ObjectAnimatorSnapshot>( ParkResumeJson.Serialize( animator.CaptureSnapshot() ) );
		var restored = new ObjectAnimator( model, holdsLastFrameAtClipEnd: true );
		restored.RestoreSnapshot( snapshot, identity => new ResolvedAnimatorClip( identity.Name, holding ) );
		Assert.AreEqual( 2, restored.GetTextureFrame( 0 ), "a fresh animator retains legitimate held-ID12 frame history" );
		animator.Advance( FixedStepClock.TickDuration );
		restored.Advance( FixedStepClock.TickDuration );
		Assert.AreEqual( animator.GetTextureFrame( 0 ), restored.GetTextureFrame( 0 ) );
		Assert.AreEqual( animator.TextureFrameVersion, restored.TextureFrameVersion );
		var copy = snapshot.DeepCopy();
		copy.TextureFrames!.Frames[0] = 1;
		Assert.AreEqual( 2, snapshot.TextureFrames!.Frames[0], "capture and deep copy own their history arrays" );
		var legacy = new ObjectAnimator( model, holdsLastFrameAtClipEnd: true );
		legacy.RestoreSnapshot( snapshot with { TextureFrames = null }, identity => new ResolvedAnimatorClip( identity.Name, holding ) );
		Assert.AreEqual( 0, legacy.GetTextureFrame( 0 ), "old saves without history keep their frame-0 initialization" );
	}

	[TestMethod]
	public void MalformedTextureFrameHistoryIsRejectedBeforeRestoreMutatesState()
	{
		var model = new ModelFile( new MemoryStream( Geometry( frames: 4 ) ) );
		var animator = new ObjectAnimator( model );
		var clip = Clip( 0, (0, 2) );
		animator.Play( 0, clip, "selected", loop: false );
		var snapshot = animator.CaptureSnapshot();
		var bad = new[]
		{
			new AnimatorTextureFrameSnapshot { Frames = Array.Empty<int>(), AnimatedSlots = Array.Empty<bool>(), Version = 0 },
			new AnimatorTextureFrameSnapshot { Frames = new[] { 4 }, AnimatedSlots = new[] { true }, Version = 0 },
			new AnimatorTextureFrameSnapshot { Frames = new[] { -1 }, AnimatedSlots = new[] { true }, Version = 0 },
			new AnimatorTextureFrameSnapshot { Frames = new[] { 2 }, AnimatedSlots = new[] { false }, Version = 0 },
			new AnimatorTextureFrameSnapshot { Frames = new[] { 2 }, AnimatedSlots = Array.Empty<bool>(), Version = 0 }
		};
		foreach ( var history in bad )
		{
			var decoded = ParkResumeJson.Deserialize<ObjectAnimatorSnapshot>( ParkResumeJson.Serialize( snapshot with { TextureFrames = history } ) );
			Assert.ThrowsException<InvalidDataException>( () => animator.RestoreSnapshot( decoded, identity => new ResolvedAnimatorClip( identity.Name, clip ) ) );
			Assert.AreEqual( 2, animator.GetTextureFrame( 0 ) );
			Assert.AreEqual( "selected", animator.GetClip( 0 ) );
		}
	}

	[TestMethod]
	public void MissingDecodedArtRetainsTheVisibleFrameAcrossSaveRestore()
	{
		var model = new ModelFile( new MemoryStream( Geometry( frames: 4 ) ) );
		var clip = Clip( 0, 40, (0, 1), (10, 3) );
		var animator = new ObjectAnimator( model );
		var available = new[] { true, true, true, false };
		animator.SetTextureFrameAvailability( 0, available );
		available[3] = true; // the animator owns a copy, not the caller's mutable array
		animator.Play( 0, clip, "missing", loop: false );
		animator.Advance( 0.5 );
		Assert.AreEqual( 1, animator.GetTextureFrame( 0 ), "missing frame 3 holds the visible frame 1" );
		var saved = ParkResumeJson.Deserialize<ObjectAnimatorSnapshot>( ParkResumeJson.Serialize( animator.CaptureSnapshot() ) );
		var restored = new ObjectAnimator( model );
		restored.SetTextureFrameAvailability( 0, new[] { true, true, true, false } );
		restored.RestoreSnapshot( saved, identity => new ResolvedAnimatorClip( identity.Name, clip ) );
		restored.Advance( FixedStepClock.TickDuration );
		Assert.AreEqual( 1, restored.GetTextureFrame( 0 ) );
		Assert.ThrowsException<ArgumentException>( () => restored.SetTextureFrameAvailability( 0, Array.Empty<bool>() ) );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void HeadlessFrameHistoryRespectsDecodedArtAvailabilityOnRestore( bool invalidKey )
	{
		var model = new ModelFile( new MemoryStream( Geometry( frames: 4 ) ) );
		var selected = Clip( 0, (0, 3) );
		var holding = invalidKey ? Clip( 0, (0, 99), (10, 1) ) : Clip( 0, (10, 1) );
		var headless = new ObjectAnimator( model, holdsLastFrameAtClipEnd: true );
		headless.Play( 0, selected, "selected", loop: false );
		headless.Advance( 0.1 ); // Hold leaves slot history without an outgoing bound clip.
		headless.Play( 0, holding, "holding", loop: false );
		var saved = ParkResumeJson.Deserialize<ObjectAnimatorSnapshot>( ParkResumeJson.Serialize( headless.CaptureSnapshot() ) );
		Assert.AreEqual( 3, saved.TextureFrames!.Frames[0], "headless consumers retain raw decoded selection" );

		var rendered = new ObjectAnimator( model, holdsLastFrameAtClipEnd: true );
		rendered.SetTextureFrameAvailability( 0, new[] { true, true, true, false } );
		rendered.RestoreSnapshot( saved, identity => new ResolvedAnimatorClip( identity.Name, holding ) );
		Assert.AreEqual( 0, rendered.GetTextureFrame( 0 ), "unavailable restored art starts at the base texture actually shown" );
		Assert.AreEqual( saved.TextureFrames.Version + 1, rendered.TextureFrameVersion );
		var lateRenderer = new ObjectAnimator( model, holdsLastFrameAtClipEnd: true );
		lateRenderer.RestoreSnapshot( saved, identity => new ResolvedAnimatorClip( identity.Name, holding ) );
		lateRenderer.SetTextureFrameAvailability( 0, new[] { true, true, true, false } );
		CollectionAssert.AreEqual( ParkResumeJson.Serialize( rendered.CaptureSnapshot() ), ParkResumeJson.Serialize( lateRenderer.CaptureSnapshot() ), "availability registration order preserves the complete resumed state" );
		rendered.Advance( 0.334 );
		lateRenderer.Advance( 0.334 );
		Assert.AreEqual( 1, rendered.GetTextureFrame( 0 ), "the next available key resumes normally" );
		CollectionAssert.AreEqual( ParkResumeJson.Serialize( rendered.CaptureSnapshot() ), ParkResumeJson.Serialize( lateRenderer.CaptureSnapshot() ), "both restored animators continue identically" );
		Assert.AreEqual( 3, saved.TextureFrames.Frames[0], "restoring does not mutate saved history" );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void OrdinaryReplacementResetsBeforeFirstKeyEvenForFixedItems( bool fixedItem )
	{
		var model = new ModelFile( new MemoryStream( Geometry( 4 ) ) );
		var animator = new ObjectAnimator( model, keepsPoseOnClipChange: fixedItem );
		animator.Play( 0, Clip( 0, (0, 2) ), "old", loop: false );
		animator.Advance( 0.1 ); // Ended14 is still bound, not Held12.
		Assert.AreEqual( 2, animator.GetTextureFrame( 0 ) );
		animator.Play( 0, Clip( 0, (10, 1) ), "new", loop: false );
		Assert.AreEqual( 0, animator.GetTextureFrame( 0 ), "cleanup runs before the new first key" );
		animator.Advance( 0.334 );
		Assert.AreEqual( 1, animator.GetTextureFrame( 0 ) );
		animator.StopAll();
		Assert.AreEqual( 0, animator.GetTextureFrame( 0 ) );
	}

	[DataTestMethod]
	[DataRow( true, 0x71u, 0 )]
	[DataRow( false, 0x71u, 2 )]
	[DataRow( true, 0x31u, 2 )]
	public void CleanupUsesOutgoingTrailerAndRawSlotFlagsNotAddressedTracks( bool outgoingEnabled, uint flags, int expected )
	{
		var model = new ModelFile( new MemoryStream( Geometry( 4, slots: 2, slotFlags: flags ) ) );
		var animator = new ObjectAnimator( model, holdsLastFrameAtClipEnd: true );
		animator.Play( 0, Clip( 0, outgoingEnabled, 100, (0, 1) ), "outgoing", loop: false );
		animator.Play( 1, Clip( 1, (0, 2) ), "unrelated", loop: false );
		animator.Advance( 0.1 ); // Slot1's channel becomes Held12 and ceases writing.
		Assert.AreEqual( 2, animator.GetTextureFrame( 1 ) );
		animator.Stop( 0 );
		Assert.AreEqual( expected, animator.GetTextureFrame( 1 ), "reset walks all flagged slots, not outgoing slot0" );
		animator.Advance( 0.1 );
		Assert.AreEqual( expected, animator.GetTextureFrame( 1 ), "Held12 must not write its old texture again" );
	}

	[DataTestMethod]
	[DataRow( false, 2 )]
	[DataRow( true, 1 )]
	public void HeldCeasesCompetingButEndedRemainsABoundTextureWriter( bool hold, int expected )
	{
		var model = new ModelFile( new MemoryStream( Geometry( 4 ) ) );
		var animator = new ObjectAnimator( model, holdsLastFrameAtClipEnd: hold );
		animator.Play( 0, Clip( 0, 100, (0, 0), (6, 1) ), "lower", loop: false );
		animator.Play( 1, Clip( 0, (0, 2) ), "higher", loop: false );
		animator.Advance( 0.1 );
		Assert.AreEqual( 2, animator.GetTextureFrame( 0 ), "the hold transition still samples its final endpoint once" );
		animator.Advance( 0.2 );
		Assert.AreEqual( expected, animator.GetTextureFrame( 0 ) );
		var snapshot = animator.CaptureSnapshot();
		var restored = new ObjectAnimator( model, holdsLastFrameAtClipEnd: hold );
		restored.RestoreSnapshot( snapshot, identity => new ResolvedAnimatorClip( identity.Name,
			identity.Name == "lower" ? Clip( 0, 100, (0, 0), (6, 1) ) : Clip( 0, (0, 2) ) ) );
		restored.Advance( 0.1 );
		Assert.AreEqual( expected, restored.GetTextureFrame( 0 ), "restore must not replay a held endpoint" );
	}

	[TestMethod]
	public void LoopReplayResetsBeforeItsFirstKeyAndDisabledReplacementStillCleansOldClip()
	{
		var model = new ModelFile( new MemoryStream( Geometry( 4 ) ) );
		var animator = new ObjectAnimator( model );
		animator.Play( 0, Clip( 0, (10, 2) ), "loop", loop: true );
		animator.Advance( 0.334 );
		Assert.AreEqual( 2, animator.GetTextureFrame( 0 ) );
		animator.Advance( 0.1 );
		Assert.AreEqual( 0, animator.GetTextureFrame( 0 ), "loop rebind resets before delayed first key" );
		animator.Advance( 0.334 );
		Assert.AreEqual( 2, animator.GetTextureFrame( 0 ) );
		animator.Play( 0, Clip( 0, false, (0, 1) ), "disabled", loop: false );
		Assert.AreEqual( 0, animator.GetTextureFrame( 0 ), "outgoing trailer controls cleanup, not incoming trailer" );
	}

	[TestMethod]
	public void HigherChannelReplayCleanupRunsAfterLowerChannelSample()
	{
		var model = new ModelFile( new MemoryStream( Geometry( 4 ) ) );
		var animator = new ObjectAnimator( model );
		animator.Play( 0, Clip( 0, 100, (0, 1) ), "lower", loop: false );
		animator.Play( 1, Clip( 0, (10, 2) ), "higher", loop: true );
		animator.Advance( 0.334 );
		Assert.AreEqual( 2, animator.GetTextureFrame( 0 ) );
		animator.Advance( 0.1 );
		Assert.AreEqual( 0, animator.GetTextureFrame( 0 ), "higher replay cleans the lower writer's earlier sample" );
		var saved = animator.CaptureSnapshot();
		var restored = new ObjectAnimator( model );
		restored.RestoreSnapshot( saved, identity => new ResolvedAnimatorClip( identity.Name,
			identity.Name == "lower" ? Clip( 0, 100, (0, 1) ) : Clip( 0, (10, 2) ) ) );
		Assert.AreEqual( 0, restored.GetTextureFrame( 0 ), "restore preserves the post-cleanup boundary rather than resampling" );
		restored.SetTextureFrameAvailability( 0, new[] { true, true, true, true } );
		Assert.AreEqual( 0, restored.GetTextureFrame( 0 ), "renderer registration must not erase the cleanup boundary" );
		var registeredFirst = new ObjectAnimator( model );
		registeredFirst.SetTextureFrameAvailability( 0, new[] { true, true, true, true } );
		registeredFirst.RestoreSnapshot( saved, identity => new ResolvedAnimatorClip( identity.Name,
			identity.Name == "lower" ? Clip( 0, 100, (0, 1) ) : Clip( 0, (10, 2) ) ) );
		CollectionAssert.AreEqual( ParkResumeJson.Serialize( registeredFirst.CaptureSnapshot() ),
			ParkResumeJson.Serialize( restored.CaptureSnapshot() ), "availability registration order preserves saved boundary" );
		animator.Advance( 0 );
		restored.Advance( 0 );
		Assert.AreEqual( 1, animator.GetTextureFrame( 0 ), "on the next pass without cleanup, lower contributes again" );
		Assert.AreEqual( animator.GetTextureFrame( 0 ), restored.GetTextureFrame( 0 ) );
	}

	[TestMethod]
	public void HigherDeferredReplacementCleansAfterLowerSample()
	{
		var model = new ModelFile( new MemoryStream( Geometry( 4 ) ) );
		var animator = new ObjectAnimator( model );
		animator.Play( 0, Clip( 0, 100, (0, 1) ), "lower", loop: false );
		animator.Trigger( 1, Clip( 0, (0, 2) ), "outgoing", 0, 0, loop: false );
		animator.Trigger( 1, Clip( 0, (10, 3) ), "deferred", 1, 0, loop: false, defer: true );
		Assert.AreEqual( 2, animator.GetTextureFrame( 0 ) );
		animator.Advance( 0.1 );
		Assert.AreEqual( "deferred", animator.GetClip( 1 ) );
		Assert.AreEqual( 0, animator.GetTextureFrame( 0 ), "deferred cleanup is in native channel order" );
		animator.Advance( 0.334 );
		Assert.AreEqual( 3, animator.GetTextureFrame( 0 ) );
	}

	// ---- synthetic files ----

	/// <summary>
	/// The synthetic geometry of Md2ModelFileTests (one textured quad, mesh node with one dummy child), with
	/// the single texture slot holding <paramref name="frames"/> frame names.
	/// </summary>
	private static byte[] Geometry( int frames, int slots = 1, uint slotFlags = 0x71 )
	{
		int FlagTable = 0xB8, NameTable = FlagTable + slots * 8, SlotTable = NameTable + slots * frames * 20;
		int Positions = SlotTable + slots * 16, Normals = Positions + 48, Uvs = Normals + 72, Materials = Uvs + 32, Faces = Materials + 16, Order = Faces + 16;
		int MeshName = Order + 8, DummyName = MeshName + 5, Attributes = DummyName + 8, MeshTable = Attributes + 20, DummyTable = MeshTable + 160;
		var data = new byte[DummyTable + 88];
		W32( data, 0, ModelFile.Magic );
		W32( data, 4, 221 );
		W32( data, 8, 203 );
		Encoding.ASCII.GetBytes( "synthtf.MD2" ).CopyTo( data, 0x18 );
		ushort[] counts = { (ushort)slots, 1, 1, 1, 2, 0, 2, 1, 1, 1, 6 };
		for ( var index = 0; index < counts.Length; index++ )
			W16( data, 0x36 + index * 2, counts[index] );
		int[] offsets = { FlagTable, SlotTable, Positions, Normals, Uvs, Materials, Faces, 0, MeshTable, DummyTable, MeshTable, Attributes };
		for ( var index = 0; index < offsets.Length; index++ )
			W32( data, 0x50 + index * 4, (uint)offsets[index] );
		WF( data, 0x80, -1, -2, -3, 4, 5, 6 );

		for ( var slot = 0; slot < slots; slot++ )
		{
			W32( data, FlagTable + slot * 8, slotFlags );
			for ( var frame = 0; frame < frames; frame++ )
				Encoding.ASCII.GetBytes( $"tex_a{frame}.tga\0" ).CopyTo( data, NameTable + (slot * frames + frame) * 20 );
			W16( data, SlotTable + slot * 16 + 10, (ushort)frames );
			W32( data, SlotTable + slot * 16 + 12, (uint)(NameTable + slot * frames * 20) );
		}

		// Positions (0,0,0) (1,0,0) (1,1,0) (0,1,0) as one X×4/Y×4/Z×4 block.
		WF( data, Positions, 0, 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 0 );
		for ( var index = 0; index < 4; index++ )
			WF( data, Normals + index * 12, 0, 0, 1 );
		WF( data, Normals + 48, 0, 0, 1, 0, 0, -1 );
		WF( data, Uvs, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f );
		W32( data, Materials, FlagTable );
		W16( data, Materials + 6, 2 );
		W16( data, Materials + 8, 0 );
		W16( data, Materials + 10, 3 );
		ushort[] faces = { 4, 0, 1, 2, 5, 0, 2, 3 };
		for ( var index = 0; index < faces.Length; index++ )
			W16( data, Faces + index * 2, faces[index] );
		ushort[] order = { 1, 2, 3, 0 };
		for ( var index = 0; index < order.Length; index++ )
			W16( data, Order + index * 2, order[index] );
		Encoding.ASCII.GetBytes( "quad\0dummy\0" ).CopyTo( data, MeshName );
		W32( data, Attributes, 0x100000B1 );
		W32( data, Attributes + 4, 7 );

		W32( data, MeshTable, 1 );
		W32( data, MeshTable + 12, DummyTable );
		WriteIdentity( data, MeshTable + 16, 1, 2, 3 );
		W32( data, MeshTable + 84, MeshName );
		W16( data, MeshTable + 88, 4 );
		W16( data, MeshTable + 90, 1 );
		W16( data, MeshTable + 92, 2 );
		W16( data, MeshTable + 94, 4 );
		int[] meshOffsets = { Positions, Normals, Uvs, Materials, Faces };
		for ( var index = 0; index < meshOffsets.Length; index++ )
			W32( data, MeshTable + 96 + index * 4, (uint)meshOffsets[index] );
		WF( data, MeshTable + 120, 0, 0, 0, 1, 1, 0 );
		W32( data, MeshTable + 148, Order );

		W32( data, DummyTable, 0x200 );
		W32( data, DummyTable + 4, MeshTable );
		WriteIdentity( data, DummyTable + 16, 0, 0, 5 );
		W32( data, DummyTable + 80, 1 );
		W32( data, DummyTable + 84, DummyName );
		return data;
	}

	private static void WriteIdentity( byte[] data, int offset, params float[] translation )
	{
		WF( data, offset, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 );
		WF( data, offset + 48, translation[0], translation[1], translation[2], 1 );
	}

	/// <summary>
	/// A synthetic animation whose only record is a texture-frame track on <paramref name="slot"/> (trailer bit
	/// 0x2 optional), built on Md2VertexAnimationTests' trailer with the vertex, toggle and key payload cleared.
	/// </summary>
	private static ModelAnimation Clip( int slot, params (int Tick, int Frame)[] keys ) => Clip( slot, true, 0, keys );

	private static ModelAnimation Clip( int slot, int duration, params (int Tick, int Frame)[] keys ) => Clip( slot, true, duration, keys );

	private static ModelAnimation Clip( int slot, bool enableFlag, params (int Tick, int Frame)[] keys ) => Clip( slot, enableFlag, 0, keys );

	private static ModelAnimation Clip( int slot, bool enableFlag, int duration, params (int Tick, int Frame)[] keys )
	{
		var length = Math.Max( duration, keys.Max( key => key.Tick ) + 2 );
		var data = Md2VertexAnimationTests.CreateAnimation( 0, 10 );
		var trailer = data.Length - 72;
		// The record table sits at trailer word 11; clear its flags and payload pointers, keeping its index,
		// so the clip carries no vertex, toggle or rigid payload — a pure texture-frame clip.
		var records = (int)BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( trailer + 44 ) );
		W32( data, records + 4, 0 );
		W32( data, records + 12, 0 );
		W32( data, records + 40, 0 );
		W32( data, records + 48, 0 );
		// Replace the built-in track (slot 1, three keys) with the requested one; the key table holds six u16.
		var frameTracks = (int)BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( trailer + 48 ) );
		W16( data, frameTracks, (ushort)slot );
		W16( data, frameTracks + 2, (ushort)keys.Length );
		var frameKeys = (int)BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( frameTracks + 4 ) );
		for ( var index = 0; index < keys.Length; index++ )
		{
			W16( data, frameKeys + index * 4, (ushort)keys[index].Tick );
			W16( data, frameKeys + index * 4 + 2, (ushort)keys[index].Frame );
		}
		W32( data, trailer + 8, (uint)length );
		var flags = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( trailer ) );
		W32( data, trailer, enableFlag ? flags | 2u : flags & ~2u );
		return new ModelFile( new MemoryStream( data ) ).Clip!;
	}

	private static void W16( byte[] data, int offset, ushort value ) => BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( offset ), value );
	private static void W32( byte[] data, int offset, uint value ) => BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( offset ), value );
	private static void W32( byte[] data, int offset, int value ) => W32( data, offset, (uint)value );
	private static void WF( byte[] data, int offset, params float[] values )
	{
		for ( var index = 0; index < values.Length; index++ )
			BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( offset + index * 4 ), values[index] );
	}
	/// <summary>RIDES-035: the slot bind (0x0057A420) takes the track's frame only while the render flag 0x40 (the ANIMATINGTEXTURES option) is set, else frame 0.</summary>
	[TestMethod]
	public void ASlotShowsFrameZeroWhileTextureAnimationIsOff()
	{
		Assert.AreEqual( 3, OriginalObject.TextureFrameToShow( 3, animating: true ) );
		Assert.AreEqual( 0, OriginalObject.TextureFrameToShow( 3, animating: false ) );
		Assert.AreEqual( 0, OriginalObject.TextureFrameToShow( 0, animating: true ) );
	}
}
