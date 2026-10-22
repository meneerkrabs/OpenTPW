using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class LoadProfileTests
{
	[TestCleanup]
	public void Cleanup() => LoadProfile.Enable( false );

	[TestMethod]
	public void SectionsCountOnlyWhileEnabledAndNest()
	{
		LoadProfile.Enable( false );
		using ( LoadProfile.Measure( "off" ) ) { }
		Assert.AreEqual( 0, LoadProfile.Snapshot().Count, "nothing is recorded while off" );

		LoadProfile.Enable( true );
		using ( LoadProfile.Measure( "outer" ) )
		{
			for ( var index = 0; index < 3; index++ )
				using ( LoadProfile.Measure( "inner" ) )
					Thread.Sleep( 2 );
		}
		var snapshot = LoadProfile.Snapshot();
		Assert.AreEqual( (1, 3), (snapshot["outer"].Count, snapshot["inner"].Count) );
		Assert.IsTrue( snapshot["outer"].Milliseconds >= snapshot["inner"].Milliseconds, "a parent's time includes its children's" );
		Assert.IsTrue( snapshot["inner"].Milliseconds >= 5 );

		LoadProfile.Enable( true );
		Assert.AreEqual( 0, LoadProfile.Snapshot().Count, "enabling again starts a new profile" );
	}

	[TestMethod]
	public void TimeWhileThePausedLoadWaitsIsLeftOut()
	{
		// Warm the measured methods: compilation is real work, but this control tests pause accounting.
		LoadProfile.Enable( true );
		using ( LoadProfile.Measure( "warm" ) ) { LoadProfile.Pause(); LoadProfile.Resume(); }
		_ = LoadProfile.Snapshot();
		LoadProfile.Enable( true );
		var beginBefore = Stopwatch.GetTimestamp();
		var section = LoadProfile.Measure( "built over frames" );
		var beginAfter = Stopwatch.GetTimestamp();
		Thread.Sleep( 5 );
		var nestedBeginBefore = Stopwatch.GetTimestamp();
		var nested = LoadProfile.Measure( "nested over frames" );
		var nestedBeginAfter = Stopwatch.GetTimestamp();
		var pauseBefore = Stopwatch.GetTimestamp();
		LoadProfile.Pause();
		var pauseAfter = Stopwatch.GetTimestamp();
		Thread.Sleep( 100 );
		LoadProfile.Pause(); // A repeated pause must not restart the excluded interval.
		Thread.Sleep( 100 );
		var resumeBefore = Stopwatch.GetTimestamp();
		LoadProfile.Resume();
		var resumeAfter = Stopwatch.GetTimestamp();
		LoadProfile.Resume(); // Resuming twice must not subtract the wait twice.
		Thread.Sleep( 5 );
		var nestedEndBefore = Stopwatch.GetTimestamp();
		nested.Dispose();
		var nestedEndAfter = Stopwatch.GetTimestamp();
		var endBefore = Stopwatch.GetTimestamp();
		section.Dispose();
		var endAfter = Stopwatch.GetTimestamp();
		var snapshot = LoadProfile.Snapshot();
		// Each profiler clock read lies inside its independently observed call brackets.
		var pausedMinimum = resumeBefore - pauseAfter;
		var pausedMaximum = resumeAfter - pauseBefore;
		var tickMs = 1000d / Stopwatch.Frequency;
		Assert.IsTrue( pausedMinimum > 0, "The independently measured paused interval must contain a real wait." );
		Check( "built over frames", beginBefore, beginAfter, endBefore, endAfter );
		Check( "nested over frames", nestedBeginBefore, nestedBeginAfter, nestedEndBefore, nestedEndAfter );
		void Check( string name, long beforeStart, long afterStart, long beforeEnd, long afterEnd )
		{
			var recorded = snapshot[name];
			var workMinimum = beforeEnd - afterStart - pausedMaximum;
			var workMaximum = afterEnd - beforeStart - pausedMinimum;
			Assert.AreEqual( 1, recorded.Count );
			var separation = beforeEnd - afterStart - workMaximum;
			Assert.IsTrue( separation > 1, "The observed work and unexcluded elapsed bounds must remain distinct after the one-tick tolerance." );
			Console.WriteLine( $"{name}: recorded {recorded.Milliseconds:R} ms, work bounds [{workMinimum * tickMs:R}, {workMaximum * tickMs:R}] ms, separation {separation * tickMs:R} ms." );
			Assert.IsTrue( recorded.Milliseconds >= workMinimum * tickMs - tickMs && recorded.Milliseconds <= workMaximum * tickMs + tickMs,
				$"{name}: recorded {recorded.Milliseconds} ms; observed work bounds [{workMinimum * tickMs}, {workMaximum * tickMs}] ms, paused bounds [{pausedMinimum * tickMs}, {pausedMaximum * tickMs}] ms." );
		}
	}

	[TestMethod]
	public void OtherThreadsAreNotCounted()
	{
		LoadProfile.Enable( true );
		Task.Run( () => { using ( LoadProfile.Measure( "worker" ) ) { } } ).Wait();
		Assert.IsFalse( LoadProfile.Snapshot().ContainsKey( "worker" ) );
	}

	/// <summary>The counted amounts of --startup-profile and --hitch-report (docs/ENGINE-PERFORMANCE.md, "Start and build hitches"): every thread counts while on, nothing while off.</summary>
	[TestMethod]
	public void CountersAddOnEveryThreadWhileRecording()
	{
		LoadProfile.Enable( false );
		LoadProfile.Count( "files", 3 );
		Assert.AreEqual( 0, LoadProfile.Counters().Count, "nothing is counted while off" );
		Assert.IsFalse( LoadProfile.Recording );

		LoadProfile.Enable( true );
		Assert.IsTrue( LoadProfile.Recording );
		LoadProfile.Count( "files" );
		Task.WaitAll( Enumerable.Range( 0, 4 ).Select( _ => Task.Run( () => { for ( var index = 0; index < 1000; index++ ) LoadProfile.Count( "bytes", 2 ); } ) ).ToArray() );
		var counters = LoadProfile.Counters();
		Assert.AreEqual( 1, counters["files"] );
		Assert.AreEqual( 8000, counters["bytes"], "helper threads count too, without losing additions" );

		LoadProfile.Enable( true );
		Assert.AreEqual( 0, LoadProfile.Counters().Count, "enabling again starts new counters" );
	}

	/// <summary>Frame marks record only on the thread that turned them on: a section closed on a helper thread is left out (the desktop has such threads).</summary>
	[TestMethod]
	public void FrameMarksIgnoreOtherThreads()
	{
		try
		{
			FrameMarks.Enable( true );
			FrameMarks.BeginFrame();
			FrameMarks.Add( "main", 10 );
			Task.Run( () => FrameMarks.Add( "worker", 10 ) ).Wait();
			var snapshot = FrameMarks.Snapshot();
			Assert.IsTrue( snapshot.ContainsKey( "main" ) );
			Assert.IsFalse( snapshot.ContainsKey( "worker" ) );
		}
		finally
		{
			FrameMarks.Enable( false );
		}
	}
}
