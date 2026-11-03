using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>PATH-015: the queue instances' idle restart (tp.exe 0x473C70 -> 0x473490 -> 0x4732A0, group 5, the letter M clip of the model) and the clips the queue archives ship.</summary>
[TestClass]
public class QueueClipPlayerTests
{
	private static (ModelFile Model, ModelAnimation Clip) Torch()
	{
		var model = new ModelFile( new MemoryStream( ObjectTextureFrameTests.Geometry( 3 ) ) );
		var clip = ObjectTextureFrameTests.Clip( 0, 15, (0, 0), (5, 1), (10, 2) );
		return (model, clip);
	}

	private static List<(double Time, int Frame)> Run( QueueClipPlayer player, double from, double to )
	{
		var frames = new List<(double, int)>();
		for ( var step = 0; from + step * 0.01 < to; step++ )
		{
			player.Step( 0.01 );
			frames.Add( (from + (step + 1) * 0.01, player.Animator.GetTextureFrame( 0 )) );
		}
		return frames;
	}

	[TestMethod]
	public void AnIdleChannelStartsTheClipAtTheFirstFrameAndRestartsItWhenItEnds()
	{
		var (model, clip) = Torch();
		var player = new QueueClipPlayer( model, clip );
		Assert.AreEqual( ObjectAnimator.NoAnimation, player.Animator.GetAnimation( 0 ), "the creator leaves the channel on the none animation 12" );
		var frames = Run( player, 0, 3.0 );
		Assert.AreEqual( 5, player.Animator.GetAnimationId( 0 ), "group 5 plays" );
		var order = frames.Select( frame => frame.Frame ).Aggregate( new List<int>(), ( list, frame ) => { if ( list.Count == 0 || list[^1] != frame ) list.Add( frame ); return list; } );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2 }.Take( order.Count ).ToArray(), order, "the frames over and over" );
		Assert.IsTrue( order.Count >= 15, $"about six cycles in 3 s ({order.Count} frames shown)" );
		// one cycle is 15 ticks at 30 per second, 0.5 s, plus the restart that waits for the frame that finds the channel idle
		var restarts = frames.Zip( frames.Skip( 1 ), ( a, b ) => (a, b) ).Where( pair => pair.a.Frame == 2 && pair.b.Frame == 0 ).Select( pair => pair.b.Time ).ToArray();
		Assert.IsTrue( restarts.Length >= 4 );
		Assert.IsTrue( restarts.Zip( restarts.Skip( 1 ), ( a, b ) => b - a ).All( gap => gap is > 0.49 and < 0.54 ), string.Join( ", ", restarts ) );
	}

	[TestMethod]
	public void InstancesMadeAtDifferentTimesAreInDifferentPhases()
	{
		var (model, clip) = Torch();
		var first = new QueueClipPlayer( model, clip );
		Run( first, 0, 0.6 );
		var second = new QueueClipPlayer( model, clip );
		var late = Run( second, 0.6, 0.7 );
		var early = Run( first, 0.6, 0.7 );
		Assert.AreEqual( 1, early[^1].Frame, "the first instance restarted at about 0.51 s: 0.19 s, tick 5.7" );
		Assert.AreEqual( 0, late[^1].Frame, "the second one is 0.1 s into its first cycle: tick 3" );
	}

	[TestMethod]
	public void APausedClockHoldsTheFrame()
	{
		var (model, clip) = Torch();
		var player = new QueueClipPlayer( model, clip );
		Run( player, 0, 0.3 );
		var frame = player.Animator.GetTextureFrame( 0 );
		for ( var step = 0; step < 100; step++ )
			player.Step( 0 );
		Assert.AreEqual( frame, player.Animator.GetTextureFrame( 0 ) );
	}

	[TestMethod]
	public void AStepAllocatesNothing()
	{
		var (model, clip) = Torch();
		var player = new QueueClipPlayer( model, clip );
		// the clip restarts once per cycle (a new channel state, 0.5 s here); the frames in between allocate nothing
		Run( player, 0, 0.56 );
		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( var step = 0; step < 30; step++ )
			player.Step( 0.01 );
		Assert.AreEqual( 0, GC.GetAllocatedBytesForCurrentThread() - before, "the frames of a cycle allocate nothing" );
	}

	/// <summary>Every shipped queue clip is one this renderer plays (frame tracks or texture coordinates only), and the torch models animate frames 0 to 4.</summary>
	[TestMethod]
	public void EveryShippedQueueClipIsPlayedAndTheTorchesCycle()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the queue archives." );
		var levels = Directory.EnumerateDirectories( gamePath!, "levels", SearchOption.AllDirectories ).FirstOrDefault();
		if ( levels == null )
			Assert.Inconclusive( "No levels folder." );
		var clips = 0;
		foreach ( var theme in new[] { "jungle", "hallow", "fantasy", "space" } )
		{
			var wad = Directory.EnumerateFiles( Path.Combine( levels!, theme ), "queue.wad" ).FirstOrDefault();
			if ( wad == null )
				continue;
			var archive = new WadArchive( wad );
			foreach ( var name in QueueModels.Names.Distinct() )
			{
				var main = archive.Root.Children.OfType<ArchiveFile>().FirstOrDefault( file => string.Equals( file.Name, name + ".md2", StringComparison.OrdinalIgnoreCase ) );
				var animation = archive.Root.Children.OfType<ArchiveFile>().FirstOrDefault( file => string.Equals( file.Name, name + "m.md2", StringComparison.OrdinalIgnoreCase ) );
				if ( main == null || animation == null )
					continue;
				var model = new ModelFile( new MemoryStream( main.GetData() ) );
				var clip = new ModelFile( new MemoryStream( animation.GetData() ) ).Clip!;
				Assert.IsTrue( QueueClip.Supported( clip ), $"{theme}/{name}m is played" );
				clips++;
				if ( clip.TextureFrameTracks.Count == 0 )
					continue;
				var player = new QueueClipPlayer( model, clip );
				var slot = clip.TextureFrameTracks[0].Slot;
				var shown = new List<int>();
				for ( var step = 0; step < 150; step++ )
				{
					player.Step( 0.01 );
					var frame = player.Animator.GetTextureFrame( slot );
					if ( shown.Count == 0 || shown[^1] != frame )
						shown.Add( frame );
				}
				CollectionAssert.AreEqual( Enumerable.Range( 0, shown.Count ).Select( index => index % 5 ).ToArray(), shown, $"{theme}/{name}: the fire frames cycle: {string.Join( ",", shown )}" );
				Assert.IsTrue( shown.Count >= 6, $"{theme}/{name}" );
			}
		}
		Assert.IsTrue( clips >= 8, $"the shipped queue archives hold {clips} playable clips" );
	}
}
