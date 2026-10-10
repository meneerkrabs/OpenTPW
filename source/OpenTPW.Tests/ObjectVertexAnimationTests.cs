using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Veldrid;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Tests;

/// <summary>Vertex-animated object parts without original assets.</summary>
[TestClass]
public class ObjectVertexAnimationTests
{
	private static ModelFile.Mesh CreateMesh() => new()
	{
		Positions = new[] { new Vector3( 1, 2, 3 ), new Vector3( 4, 5, 6 ) },
		CornerPositionIndices = new ushort[] { 1, 0, 1 }
	};

	[TestMethod]
	public void PartVerticesFollowTheCornerOrderInEngineAxes()
	{
		var mesh = CreateMesh();
		var vertices = Enumerable.Range( 0, 3 ).Select( index => new Vertex { Normal = new Vector3( index, 0, 1 ), TexIndex = index } ).ToArray();
		ObjectRenderParts.WritePositions( mesh, new[] { new NVector3( 10, 20, 30 ), new NVector3( 40, 50, 60 ) }, vertices );
		CollectionAssert.AreEqual( new NVector3[] { new( 40, 60, 50 ), new( 10, 30, 20 ), new( 40, 60, 50 ) }, vertices.Select( vertex => vertex.Position.GetSystemVector3() ).ToArray() );
		// Normals and the other attributes stay as built.
		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, vertices.Select( vertex => vertex.TexIndex ).ToArray() );
		Assert.AreEqual( new NVector3( 2, 0, 1 ), vertices[2].Normal.GetSystemVector3() );

		// Null restores the stored positions.
		ObjectRenderParts.WritePositions( mesh, null, vertices );
		CollectionAssert.AreEqual( new NVector3[] { new( 4, 6, 5 ), new( 1, 3, 2 ), new( 4, 6, 5 ) }, vertices.Select( vertex => vertex.Position.GetSystemVector3() ).ToArray() );
		Assert.ThrowsException<ArgumentException>( () => ObjectRenderParts.WritePositions( mesh, null, new Vertex[2] ) );
		Assert.ThrowsException<ArgumentException>( () => ObjectRenderParts.WritePositions( mesh, new NVector3[3], vertices ) );
	}

	/// <summary>
	/// The original channel clock on the synthetic vertex clip (0xa6484, 0xa7190 -> 0xa67d8 -> 0xa6398): frames
	/// are <c>30 × whole ms / 1000</c>; a frame equal to the duration shows the last key; a looping clip
	/// replays once per update from the carry capped at the duration and truncated to whole milliseconds;
	/// a non-looping clip finishes only past its end.
	/// </summary>
	[TestMethod]
	public void LoopReplaysFromTheWholeMillisecondCarryOnlyPastTheEnd()
	{
		var model = new ModelFile( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) );
		ModelAnimation Clip( ushort duration ) => new ModelFile( new MemoryStream( Md2VertexAnimationTests.CreateAnimation( 0, duration ) ) ).Clip!;
		var thirty = Clip( 30 );
		NVector3[] PoseAt( ModelAnimation clip, float tick )
		{
			var positions = new NVector3[model.Meshes[0].Positions.Length];
			clip.Tracks.Single().VertexAnimation!.ApplyPose( tick, positions );
			return positions;
		}

		var animator = new ObjectAnimator( model );
		animator.Play( 0, thirty, "synth", loop: true );
		Assert.AreEqual( 0, animator.VertexLimitations.Count );
		animator.Advance( 0.999 );
		Assert.AreEqual( 30f * 999f / 1000f, animator.GetTick( 0 ) );
		animator.Advance( 0.001 );
		Assert.AreEqual( 30f, animator.GetTick( 0 ), "1000 ms is the duration, not past it" );
		CollectionAssert.AreEqual( PoseAt( thirty, 30 ), animator.GetVertexPositions( 0 )!.ToArray() );
		Assert.AreNotEqual( PoseAt( thirty, 30 )[2], PoseAt( thirty, 0 )[2], "the last key differs from key 0" );
		animator.Advance( 0.0005 );
		Assert.AreEqual( 30f, animator.GetTick( 0 ), "whole milliseconds" );
		animator.Advance( 0.0995 );
		Assert.AreEqual( 3f, animator.GetTick( 0 ) );
		CollectionAssert.AreEqual( PoseAt( thirty, 3 ), animator.GetVertexPositions( 0 )!.ToArray() );

		// One update far past the end replays once, from the carry capped at the duration.
		animator = new ObjectAnimator( model );
		animator.Play( 0, thirty, "synth", loop: true );
		animator.Advance( 2.5 );
		Assert.AreEqual( 30f, animator.GetTick( 0 ) );
		animator.Advance( 0.1 );
		Assert.AreEqual( 3f, animator.GetTick( 0 ) );

		// 10 ticks are 333.3 ms: at 334 ms the 0.02-tick carry truncates to 0 ms, so the clip restarts at
		// 334 ms and 333 ms later is at 9.99 ticks (a phase-free modulo would give 0.02 and 0.01).
		var ten = Clip( 10 );
		animator = new ObjectAnimator( model );
		animator.Play( 0, ten, "synth", loop: true );
		animator.Advance( 0.334 );
		Assert.AreEqual( 0f, animator.GetTick( 0 ) );
		CollectionAssert.AreEqual( PoseAt( ten, 0 ), animator.GetVertexPositions( 0 )!.ToArray() );
		animator.Advance( 0.333 );
		Assert.AreEqual( 30f * 333f / 1000f, animator.GetTick( 0 ) );

		animator = new ObjectAnimator( model );
		animator.Play( 0, thirty, "synth", loop: false );
		animator.Advance( 1.0 );
		Assert.IsTrue( animator.IsChannelPlaying( 0 ), "a frame equal to the duration is not past the end" );
		animator.Advance( 0.001 );
		Assert.IsFalse( animator.IsChannelPlaying( 0 ) );
		Assert.AreEqual( 30f, animator.GetTick( 0 ) );
	}

	/// <summary>
	/// The bind's copy-back gate (0xa5894) after a clip completes naturally: a finished clip holds its last
	/// pose on every object; replacing it with a clip that has only matrix tracks shows an ordinary object's
	/// stored mesh, while a fixed item (object flag 0x00100000 without 0x8) keeps the last pose.
	/// </summary>
	[TestMethod]
	public void FixedItemsKeepTheirLastVertexPoseWhenTheClipEnds()
	{
		var model = new ModelFile( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) );
		var clip = new ModelFile( new MemoryStream( Md2VertexAnimationTests.CreateAnimation( 0, 30 ) ) ).Clip!;
		var matrixOnly = new ModelFile( new MemoryStream( Md2AnimationTests.CreateAnimation() ) ).Clip!;
		Assert.IsTrue( matrixOnly.Tracks.All( track => track.NodeIndex != model.Meshes[0].NodeIndex && track.VertexAnimation == null ) );
		var last = new NVector3[model.Meshes[0].Positions.Length];
		clip.Tracks.Single().VertexAnimation!.ApplyPose( 30, last );

		var fixedItem = new ObjectAnimator( model, keepsPoseOnClipChange: true );
		var ordinary = new ObjectAnimator( model );
		var versions = new int[2];
		var animators = new[] { fixedItem, ordinary };
		for ( var index = 0; index < animators.Length; index++ )
		{
			var animator = animators[index];
			animator.Play( 0, clip, "synth", loop: false );
			animator.Advance( 1.0 );
			Assert.IsTrue( animator.IsChannelPlaying( 0 ) );
			versions[index] = animator.GetVertexVersion( 0 );
			animator.Advance( 0.001 );
			Assert.IsFalse( animator.IsChannelPlaying( 0 ), "completed without Stop" );
			Assert.AreEqual( 30f, animator.GetTick( 0 ) );
			CollectionAssert.AreEqual( last, animator.GetVertexPositions( 0 )!.ToArray(), "a finished clip holds its last pose" );
			Assert.AreEqual( versions[index], animator.GetVertexVersion( 0 ), "no upload while the last key holds" );
		}

		// The next clip animates matrices only, so the mesh loses its vertex track.
		foreach ( var animator in animators )
		{
			animator.Play( 0, matrixOnly, "matrices", loop: true );
			animator.Advance( 0.5 );
			Assert.AreEqual( 15f, animator.GetTick( 0 ) );
		}
		CollectionAssert.AreEqual( last, fixedItem.GetVertexPositions( 0 )!.ToArray() );
		Assert.AreEqual( versions[0], fixedItem.GetVertexVersion( 0 ), "no upload for a kept pose" );
		Assert.IsNull( ordinary.GetVertexPositions( 0 ) );
		Assert.AreNotEqual( versions[1], ordinary.GetVertexVersion( 0 ), "the stored mesh is uploaded again" );
		fixedItem.StopAll();
		CollectionAssert.AreEqual( last, fixedItem.GetVertexPositions( 0 )!.ToArray(), "flushing keeps it too" );

		// A new vertex clip resamples the kept mesh.
		fixedItem.Play( 0, clip, "synth", loop: true );
		Assert.AreEqual( 0f, fixedItem.GetTick( 0 ) );
		Assert.AreNotEqual( versions[0], fixedItem.GetVertexVersion( 0 ) );
	}

	/// <summary>
	/// A 15 ticks/s animator, an OpenTPW extension (the original's channel clock is the constant 30): the
	/// endpoint, the strict past-the-end test, the capped whole-millisecond carry and the fixed-item pose all
	/// use the instance rate, so the same milliseconds give half the 30 ticks/s frames.
	/// </summary>
	[TestMethod]
	public void ACustomRateDrivesTheEndpointCarryAndFixedPose()
	{
		var model = new ModelFile( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) );
		var thirty = new ModelFile( new MemoryStream( Md2VertexAnimationTests.CreateAnimation( 0, 30 ) ) ).Clip!;
		var ten = new ModelFile( new MemoryStream( Md2VertexAnimationTests.CreateAnimation( 0, 10 ) ) ).Clip!;
		var matrixOnly = new ModelFile( new MemoryStream( Md2AnimationTests.CreateAnimation() ) ).Clip!;
		NVector3[] PoseAt( ModelAnimation clip, float tick )
		{
			var positions = new NVector3[model.Meshes[0].Positions.Length];
			clip.Tracks.Single().VertexAnimation!.ApplyPose( tick, positions );
			return positions;
		}

		Assert.AreEqual( 30f, ObjectAnimator.DefaultTicksPerSecond );
		var original = new ObjectAnimator( model );
		var half = new ObjectAnimator( model, 15 );
		Assert.AreEqual( 15f, half.TicksPerSecond );
		Assert.AreEqual( 1.0, original.Play( 0, thirty, "synth", loop: true ), 1e-9 );
		Assert.AreEqual( 2.0, half.Play( 0, thirty, "synth", loop: true ), 1e-9 );
		original.Advance( 1.0 );
		half.Advance( 1.0 );
		Assert.AreEqual( (30f, 15f), (original.GetTick( 0 ), half.GetTick( 0 )) );

		// 2000 ms is the endpoint, not past it; 100 ms more replays from the 1.5-tick carry, which is
		// 1000 × 1.5 / 15 = 100 ms (the 30 ticks/s formula would give 50 ms).
		half.Advance( 1.0 );
		Assert.AreEqual( 30f, half.GetTick( 0 ) );
		CollectionAssert.AreEqual( PoseAt( thirty, 30 ), half.GetVertexPositions( 0 )!.ToArray() );
		half.Advance( 0.1 );
		Assert.AreEqual( 1.5f, half.GetTick( 0 ) );
		CollectionAssert.AreEqual( PoseAt( thirty, 1.5f ), half.GetVertexPositions( 0 )!.ToArray() );

		// One update far past the end replays once from the carry capped at the duration (2000 ms).
		half = new ObjectAnimator( model, 15 );
		half.Play( 0, thirty, "synth", loop: true );
		half.Advance( 5.0 );
		Assert.AreEqual( 30f, half.GetTick( 0 ) );
		half.Advance( 0.1 );
		Assert.AreEqual( 1.5f, half.GetTick( 0 ) );

		// 10 ticks are 666.7 ms at 15 ticks/s: at 667 ms the 0.005-tick carry truncates to 0 ms.
		half = new ObjectAnimator( model, 15 );
		half.Play( 0, ten, "synth", loop: true );
		half.Advance( 0.667 );
		Assert.AreEqual( 0f, half.GetTick( 0 ) );
		half.Advance( 0.666 );
		Assert.AreEqual( 15f * 666f / 1000f, half.GetTick( 0 ) );

		// A fixed item at 15 ticks/s finishes only past 2000 ms and keeps that pose through a matrix-only clip.
		var fixedItem = new ObjectAnimator( model, 15, keepsPoseOnClipChange: true );
		fixedItem.Play( 0, thirty, "synth", loop: false );
		fixedItem.Advance( 2.0 );
		Assert.IsTrue( fixedItem.IsChannelPlaying( 0 ) );
		fixedItem.Advance( 0.001 );
		Assert.IsFalse( fixedItem.IsChannelPlaying( 0 ) );
		var version = fixedItem.GetVertexVersion( 0 );
		fixedItem.Play( 0, matrixOnly, "matrices", loop: false );
		fixedItem.Advance( 1.0 );
		Assert.AreEqual( 15f, fixedItem.GetTick( 0 ) );
		CollectionAssert.AreEqual( PoseAt( thirty, 30 ), fixedItem.GetVertexPositions( 0 )!.ToArray() );
		Assert.AreEqual( version, fixedItem.GetVertexVersion( 0 ) );
	}

	/// <summary>A vertex track drives one mesh record, so two meshes on one node are rejected, not half-animated.</summary>
	[TestMethod]
	public void MeshesSharingANodeAreRejected()
	{
		var model = new ModelFile( new MemoryStream( Md2ModelFileTests.CreateGeometry() ) );
		Assert.AreEqual( 0, model.Meshes.Single().NodeIndex );
		_ = new ObjectAnimator( model );
		model.Meshes.Add( new ModelFile.Mesh { NodeIndex = 0 } );
		Assert.ThrowsException<ArgumentException>( () => new ObjectAnimator( model ) );
	}

	/// <summary>
	/// Two instances' dynamic vertex buffers on Metal: each keeps its own uploads through several frames, the
	/// buffers keep their size and the source array is not touched. Inconclusive without a native Metal run.
	/// </summary>
	[TestMethod]
	[DoNotParallelize]
	public void NativeDynamicVertexBuffersKeepEachInstancesUpload()
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_NATIVE_SHADER_TESTS" ) != "1" || !OperatingSystem.IsMacOS() )
			Assert.Inconclusive( "Set OPENTPW_NATIVE_SHADER_TESTS=1 on macOS for native Metal tests." );
		using var device = GraphicsDevice.CreateMetal( new GraphicsDeviceOptions() );
		var source = Enumerable.Range( 0, 5 ).Select( index => new Vertex( new Vector3( index, 0, 0 ) ) { TexIndex = index } ).ToArray();
		var snapshot = (Vertex[])source.Clone();
		var first = new ModelVertexBuffer( device, source, dynamic: true );
		var second = new ModelVertexBuffer( device, source, dynamic: true );
		var still = new ModelVertexBuffer( device, source, dynamic: false );
		using var commands = device.ResourceFactory.CreateCommandList();
		try
		{
			var size = first.Buffer.SizeInBytes;
			Assert.AreEqual( (uint)(5 * System.Runtime.InteropServices.Marshal.SizeOf<Vertex>()), size );
			// A dynamic buffer starts with the built vertices, like a static one.
			CollectionAssert.AreEqual( snapshot, ReadBack( device, commands, first.Buffer, 5 ) );
			Vertex[] Shifted( float y ) => source.Select( vertex => { vertex.Position = new Vector3( vertex.Position.X, y, 0 ); return vertex; } ).ToArray();
			var firstPose = Shifted( 0 );
			var secondPose = Shifted( 0 );
			for ( var frame = 1; frame <= 3; frame++ )
			{
				// One array per instance is reused across frames, as OriginalObject does.
				Array.Copy( Shifted( frame ), firstPose, firstPose.Length );
				Array.Copy( Shifted( -10 * frame ), secondPose, secondPose.Length );
				first.Set( firstPose );
				second.Set( secondPose );
				commands.Begin();
				first.Flush( commands );
				second.Flush( commands );
				commands.End();
				device.SubmitCommands( commands );
				Assert.IsFalse( first.HasPendingUpdate || second.HasPendingUpdate );
			}
			device.WaitForIdle();
			CollectionAssert.AreEqual( Shifted( 3 ), ReadBack( device, commands, first.Buffer, 5 ) );
			CollectionAssert.AreEqual( Shifted( -30 ), ReadBack( device, commands, second.Buffer, 5 ) );
			CollectionAssert.AreEqual( snapshot, ReadBack( device, commands, still.Buffer, 5 ) );
			CollectionAssert.AreEqual( snapshot, source );
			Assert.AreEqual( size, first.Buffer.SizeInBytes );
			Assert.AreEqual( size, second.Buffer.SizeInBytes );
			Assert.ThrowsException<InvalidOperationException>( () => still.Set( firstPose ) );
			Assert.ThrowsException<ArgumentException>( () => first.Set( new Vertex[4] ) );
		}
		finally
		{
			first.Buffer.Dispose();
			second.Buffer.Dispose();
			still.Buffer.Dispose();
		}
	}

	private static Vertex[] ReadBack( GraphicsDevice device, CommandList commands, DeviceBuffer buffer, int count )
	{
		using var staging = device.ResourceFactory.CreateBuffer( new BufferDescription( buffer.SizeInBytes, BufferUsage.Staging ) );
		commands.Begin();
		commands.CopyBuffer( buffer, 0, staging, 0, buffer.SizeInBytes );
		commands.End();
		device.SubmitCommands( commands );
		device.WaitForIdle();
		var map = device.Map<Vertex>( staging, MapMode.Read );
		try
		{
			return Enumerable.Range( 0, count ).Select( index => map[index] ).ToArray();
		}
		finally
		{
			device.Unmap( staging );
		}
	}
}

/// <summary>Vertex tracks of the original objects at run time; inconclusive without OPENTPW_GAME_PATH.</summary>
[TestClass]
[DoNotParallelize]
public class ObjectVertexAnimationCorpusTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the object catalog corpus." );
		originalFileSystem = previous;
		initialized = true;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}

	/// <summary>
	/// Every catalog clip with a vertex track plays it over the whole clip, except the 12-byte layout,
	/// which is reported and leaves the stored mesh. No catalog model has the relative-animation flag.
	/// </summary>
	[TestMethod]
	public void EveryCatalogVertexTrackPlaysOrIsReported()
	{
		int clips = 0, played = 0, limitedClips = 0, limitations = 0, relative = 0, poses = 0;
		var fixedClips = new List<string>();
		var meshes = 0;
		foreach ( var theme in ObjectCatalog.Themes )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				relative += (model.HeaderFlags & ModelFile.RelativeAnimationFlag) != 0 ? 1 : 0;
				var vertexMeshes = ObjectRenderParts.FindVertexAnimatedMeshes( entry, model );
				meshes += vertexMeshes.Count( animated => animated );
				var parts = ObjectRenderParts.Build( entry, model );
				foreach ( var mesh in Enumerable.Range( 0, vertexMeshes.Length ).Where( mesh => vertexMeshes[mesh] && ObjectAssets.GetUnrenderableReason( model.Meshes[mesh] ) == null ) )
					Assert.IsTrue( parts.Any( part => part.MeshIndex == mesh && part.NodeIndex == model.Meshes[mesh].NodeIndex ), $"{entry} mesh {mesh} needs an unbaked part" );
				foreach ( var animation in entry.Animations )
				{
					var clip = ObjectAssets.LoadModel( entry.FileSystem, animation.Path ).Clip!;
					var tracks = clip.Tracks.Where( track => (track.Flags & ModelAnimationTrack.VertexAnimationFlag) != 0 ).ToArray();
					if ( tracks.Length == 0 )
						continue;
					clips++;
					var animator = new ObjectAnimator( model, keepsPoseOnClipChange: entry.IsFixedItem );
					animator.Play( 0, clip, animation.Name, false );
					limitations += animator.VertexLimitations.Count;
					limitedClips += animator.VertexLimitations.Count > 0 ? 1 : 0;
					var supported = tracks.Where( track => track.VertexAnimation != null ).ToArray();
					if ( supported.Length == 0 )
						continue;
					played++;
					for ( var step = 0; step <= 3; step++ )
					{
						if ( step > 0 )
							animator.Advance( clip.Duration / 3.0 / animator.TicksPerSecond );
						foreach ( var track in supported )
						{
							var mesh = model.Meshes.FindIndex( candidate => candidate.NodeIndex == track.NodeIndex );
							var positions = animator.GetVertexPositions( mesh );
							Assert.IsNotNull( positions, $"{animation.Path} node {track.NodeIndex}" );
							Assert.AreEqual( model.Meshes[mesh].Positions.Length, positions.Count );
							Assert.IsTrue( positions.All( position => float.IsFinite( position.X ) && float.IsFinite( position.Y ) && float.IsFinite( position.Z ) ), animation.Path );
							poses++;
						}
					}
					// Ending the clip keeps a fixed item's last pose and restores everything else's stored mesh.
					animator.StopAll();
					if ( entry.IsFixedItem )
						fixedClips.Add( animation.Path );
					foreach ( var track in supported )
					{
						var mesh = model.Meshes.FindIndex( candidate => candidate.NodeIndex == track.NodeIndex );
						Assert.AreEqual( entry.IsFixedItem, animator.GetVertexPositions( mesh ) != null, animation.Path );
					}
				}
			}
		}
		Assert.AreEqual( 0, relative );
		Assert.AreEqual( (609, 599, 10, 23), (clips, played, limitedClips, limitations) );
		Assert.AreEqual( 0, poses % 4 );
		Assert.AreEqual( 3, fixedClips.Count, string.Join( ", ", fixedClips ) );
		Console.WriteLine( $"{meshes} vertex-animated meshes, {poses} sampled poses, fixed-item vertex clips {string.Join( ", ", fixedClips )}" );
	}

	/// <summary>
	/// Two Belly Bounce instances share one parsed model but play the vertex-animated clip at different
	/// phases: each holds its own pose (equal to a direct sample), the parsed positions never change, and
	/// each instance's render parts get their own vertices.
	/// </summary>
	[TestMethod]
	public void TwoInstancesOfOneAssetKeepIndependentVertexPoses()
	{
		var entry = ObjectCatalog.Load( "jungle" ).Entries.Single( candidate => candidate.ArchiveName == "bouncy" );
		var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
		Assert.AreSame( model, ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath ), "instances share the parsed model" );
		var file = entry.Animations.Single( animation => string.Equals( animation.Name, "bouncyc", StringComparison.OrdinalIgnoreCase ) );
		var clip = ObjectAssets.LoadModel( entry.FileSystem, file.Path ).Clip!;
		Assert.AreEqual( 150, clip.Duration );
		var tracks = clip.Tracks.Where( track => track.VertexAnimation != null ).ToArray();
		var meshes = tracks.Select( track => model.Meshes.FindIndex( mesh => mesh.NodeIndex == track.NodeIndex ) ).ToArray();
		Assert.AreEqual( 6, meshes.Length );
		Assert.IsTrue( meshes.All( mesh => mesh >= 0 ) );
		var stored = model.Meshes.Select( mesh => mesh.Positions.Select( position => position.GetSystemVector3() ).ToArray() ).ToArray();

		var first = new ObjectAnimator( model );
		var second = new ObjectAnimator( model );
		first.Play( 0, clip, file.Name, loop: true );
		second.Play( 0, clip, file.Name, loop: true );
		first.Advance( 1.0 );
		second.Advance( 3.5 );
		Assert.AreEqual( (30f, 105f), (first.GetTick( 0 ), second.GetTick( 0 )) );
		var differs = false;
		for ( var index = 0; index < tracks.Length; index++ )
		{
			var expectedFirst = Pose( tracks[index], model.Meshes[meshes[index]], 30 );
			var expectedSecond = Pose( tracks[index], model.Meshes[meshes[index]], 105 );
			CollectionAssert.AreEqual( expectedFirst, first.GetVertexPositions( meshes[index] )!.ToArray() );
			CollectionAssert.AreEqual( expectedSecond, second.GetVertexPositions( meshes[index] )!.ToArray() );
			differs |= !expectedFirst.SequenceEqual( expectedSecond );
		}
		Assert.IsTrue( differs, "the two phases must give different poses" );

		// Render parts: every Build gives an instance its own arrays; writing one leaves the other alone.
		var firstParts = ObjectRenderParts.Build( entry, model );
		var secondParts = ObjectRenderParts.Build( entry, model );
		var checkedParts = 0;
		for ( var index = 0; index < firstParts.Count; index++ )
		{
			var mesh = firstParts[index].MeshIndex;
			if ( !meshes.Contains( mesh ) )
				continue;
			Assert.AreEqual( mesh, secondParts[index].MeshIndex );
			Assert.AreNotSame( firstParts[index].Vertices, secondParts[index].Vertices );
			ObjectRenderParts.WritePositions( model.Meshes[mesh], first.GetVertexPositions( mesh ), firstParts[index].Vertices );
			ObjectRenderParts.WritePositions( model.Meshes[mesh], second.GetVertexPositions( mesh ), secondParts[index].Vertices );
			var corners = model.Meshes[mesh].CornerPositionIndices;
			CollectionAssert.AreEqual( corners.Select( corner => ObjectAssets.ConvertAxes( first.GetVertexPositions( mesh )![corner] ) ).ToArray(),
				firstParts[index].Vertices.Select( vertex => vertex.Position.GetSystemVector3() ).ToArray() );
			CollectionAssert.AreEqual( corners.Select( corner => ObjectAssets.ConvertAxes( second.GetVertexPositions( mesh )![corner] ) ).ToArray(),
				secondParts[index].Vertices.Select( vertex => vertex.Position.GetSystemVector3() ).ToArray() );
			checkedParts++;
		}
		Assert.IsTrue( checkedParts >= meshes.Length );

		// An unchanged tick keeps the version (no upload). At exactly the duration the loop has not ended
		// (strict test), so the last key shows; 100 ms later it replays from the 3-tick carry and resamples.
		var version = first.GetVertexVersion( meshes[0] );
		first.Advance( 0 );
		Assert.AreEqual( version, first.GetVertexVersion( meshes[0] ) );
		first.Advance( 4.0 );
		Assert.AreEqual( 150f, first.GetTick( 0 ) );
		CollectionAssert.AreEqual( Pose( tracks[0], model.Meshes[meshes[0]], 150 ), first.GetVertexPositions( meshes[0] )!.ToArray() );
		version = first.GetVertexVersion( meshes[0] );
		first.Advance( 0.1 );
		Assert.AreEqual( 3f, first.GetTick( 0 ) );
		Assert.AreNotEqual( version, first.GetVertexVersion( meshes[0] ) );
		CollectionAssert.AreEqual( Pose( tracks[0], model.Meshes[meshes[0]], 3 ), first.GetVertexPositions( meshes[0] )!.ToArray() );

		// Stopping one instance restores its stored mesh and leaves the other instance's pose.
		first.Stop( 0 );
		Assert.IsTrue( meshes.All( mesh => first.GetVertexPositions( mesh ) == null ) );
		CollectionAssert.AreEqual( Pose( tracks[0], model.Meshes[meshes[0]], 105 ), second.GetVertexPositions( meshes[0] )!.ToArray() );
		for ( var mesh = 0; mesh < model.Meshes.Count; mesh++ )
			CollectionAssert.AreEqual( stored[mesh], model.Meshes[mesh].Positions.Select( position => position.GetSystemVector3() ).ToArray(), $"mesh {mesh} of the shared model" );
	}

	private static NVector3[] Pose( ModelAnimationTrack track, ModelFile.Mesh mesh, float tick )
	{
		var positions = new NVector3[mesh.Positions.Length];
		track.VertexAnimation!.ApplyPose( tick, positions );
		return positions;
	}
}
