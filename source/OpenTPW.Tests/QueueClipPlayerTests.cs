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
		// one cycle is 15 ticks at 30 per second, 0.5 s, and the restart keeps the overshoot (0x47300E, 0x472C12), so the gaps are exact to a step
		var restarts = frames.Zip( frames.Skip( 1 ), ( a, b ) => (a, b) ).Where( pair => pair.a.Frame == 2 && pair.b.Frame == 0 ).Select( pair => pair.b.Time ).ToArray();
		Assert.IsTrue( restarts.Length >= 4 );
		Assert.IsTrue( restarts.Zip( restarts.Skip( 1 ), ( a, b ) => b - a ).All( gap => Math.Abs( gap - 0.5 ) <= 0.011 ), string.Join( ", ", restarts ) );
	}

	private static HashSet<(int X, int Y)> Set( params (int X, int Y)[] cells ) => cells.ToHashSet();

	/// <summary>The cells the tools re-create restart, exactly those (0x5365D0 on a class-2 cell): the line commit's span with its anchor, the linked neighbours of a clear.</summary>
	[TestMethod]
	public void OnlyTheCellsTheToolsRecreateStartAgain()
	{
		var births = new QueueBirths();
		// two parallel lanes, (5, 5..7) and (6, 5..7), and a far cell
		var lanes = new List<(int X, int Y)> { (5, 5), (5, 6), (5, 7), (6, 5), (6, 6), (6, 7), (9, 9) };
		births.Assign( lanes, Set() );
		Assert.AreEqual( 1, lanes.Select( births.Of ).Distinct().Single(), "the first build makes them all in one frame" );
		births.Assign( lanes, Set() );
		Assert.AreEqual( 1, lanes.Select( births.Of ).Distinct().Single(), "a rebuild with nothing re-created keeps every start" );
		// one cell N = (5, 8) next to the queue end A = (5, 7): the line commit re-creates {A, N}; the parallel lane's (6, 7) keeps its phase
		lanes.Add( (5, 8) );
		births.Assign( lanes, Set( (5, 7), (5, 8) ) );
		Assert.AreEqual( 2, births.Of( (5, 7) ) );
		Assert.AreEqual( 2, births.Of( (5, 8) ) );
		Assert.AreEqual( 1, births.Of( (6, 7) ), "the parallel lane is no link neighbour" );
		Assert.AreEqual( 1, births.Of( (5, 6) ) );
		Assert.AreEqual( 1, births.Of( (9, 9) ) );
		// a line c1..c3 from the anchor A = (6, 7): {A, c1, c2, c3}, A too although its model is unchanged
		lanes.AddRange( new[] { (6, 8), (6, 9), (6, 10) } );
		births.Assign( lanes, Set( (6, 7), (6, 8), (6, 9), (6, 10) ) );
		Assert.AreEqual( 3, births.Of( (6, 7) ) );
		Assert.AreEqual( 3, births.Of( (6, 10) ) );
		Assert.AreEqual( 2, births.Of( (5, 8) ), "the other lane keeps its phase" );
		Assert.AreEqual( 1, births.Of( (6, 6) ) );
		// the middle cell M = (5, 6) is cleared: its linked neighbours (the predecessor and the successor) are re-created, the parallel (6, 6) is not
		lanes.Remove( (5, 6) );
		births.Assign( lanes, Set( (5, 5), (5, 7) ) );
		Assert.AreEqual( 4, births.Of( (5, 5) ) );
		Assert.AreEqual( 4, births.Of( (5, 7) ) );
		Assert.AreEqual( 1, births.Of( (6, 6) ) );
		Assert.AreEqual( 3, births.Of( (6, 9) ) );
		// the same cells again with nothing re-created: every phase stays
		births.Assign( lanes, Set() );
		Assert.AreEqual( 4, births.Of( (5, 5) ) );
	}

	[TestMethod]
	public void AMissingMMemberIsNoClipAndDoesNotAbortTheBuild()
	{
		var loads = 0;
		Assert.IsNull( QueueClip.FindAnimation( "queend", "levels/jungle/queue", _ => new[] { "levels/jungle/queue/queend.md2", "levels/jungle/queue/quebin1m.md2" }, _ => { loads++; return null; } ) );
		Assert.AreEqual( 0, loads, "no member, no load" );
		Assert.IsNull( QueueClip.FindAnimation( "queend", "d", _ => Array.Empty<string>(), _ => throw new InvalidOperationException() ) );
		Assert.IsNull( QueueClip.FindAnimation( "queend", "d", _ => new[] { "d/QUEENDM.MD2" }, _ => throw new InvalidDataException() ), "a member that does not read is no clip" );
		var (_, clip) = Torch();
		Assert.AreSame( clip, QueueClip.FindAnimation( "queend", "d", _ => new[] { "d/QUEENDM.MD2" }, _ => clip ), "the member is found by any case" );
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
		// several cycles (0.5 s each, with the restarts in them) allocate nothing once the first has run
		Run( player, 0, 0.56 );
		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( var step = 0; step < 250; step++ )
			player.Step( 0.01 );
		Assert.AreEqual( 0, GC.GetAllocatedBytesForCurrentThread() - before, "the frames and restarts of at least two cycles allocate nothing" );
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
