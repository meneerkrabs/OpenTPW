using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The automatic advisor's speech with its decode over frames (<see cref="Advisor.SlicedDecoding"/>, [EXT:advisor-sliced-decode], docs/BROWSER-PERFORMANCE.md
/// "Event hitches"), driven by a test advisor clock: the speaking state while the PCM decodes, silence, kill and a new line during the decode, the audio starting
/// only once it is due, the mouth following audio that started late, and the end of a line whose decode fails after it started.
/// </summary>
[TestClass, DoNotParallelize]
public class SlicedAdvisorSpeechTests
{
	private long now;
	private bool slicedBefore;
	private TimeSpan budgetBefore;

	/// <summary>An audio output whose position runs on the test clock from the moment it is played.</summary>
	private sealed class ClockedOutput( int sampleRate, Func<long> clock ) : IMovieAudioOutput
	{
		private long? playedAt;
		private long queued;
		public int SampleRate => sampleRate;
		public long PlayedFrames => playedAt is { } at ? Math.Min( queued, (clock() - at) * sampleRate / 1000 ) : 0;
		public long QueuedFrames => queued - PlayedFrames;
		public double LatencySeconds => 0;
		public void Queue( ReadOnlySpan<short> interleavedStereo ) => queued += interleavedStereo.Length / 2;
		public void Play() => playedAt ??= clock();
		public void Stop() { }
		public void Dispose() { }
	}

	private sealed class NoParticles : IAdvisorParticlePool
	{
		public object? Create( int effectId, (int X, int Y, int Z) native ) => null;
		public void Move( object handle, (int X, int Y, int Z) native ) { }
		public void Kill( object handle ) { }
	}

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		now = 10_000;
		slicedBefore = Advisor.SlicedDecoding;
		budgetBefore = Advisor.DecodeSliceBudget;
		Advisor.SlicedDecoding = true;
		// one MPEG frame per slice: the decode takes as many updates as the stream has frames
		Advisor.DecodeSliceBudget = TimeSpan.Zero;
		Advisor.PlayerFactory = audio => new SpeechAudioPlayer( audio, new ClockedOutput( audio.SampleRate, () => now ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		Advisor.SlicedDecoding = slicedBefore;
		Advisor.DecodeSliceBudget = budgetBefore;
		Advisor.PlayerFactory = null;
		AdvisorEntryPreparation.Drop();
	}

	/// <summary>A line a park's build decoded ahead (<see cref="AdvisorEntryPreparation.LineSteps"/>), as its key names the test's parts, in the shared store.</summary>
	private static Mp2Audio DecodedAhead( byte[] mpeg, BaseFileSystem files )
	{
		var audio = Mp2Decoder.Decode( mpeg, null );
		var store = new AdvisorEntryPreparation.Store();
		store.Keep( AdvisorEntryPreparation.LineKey( "test", Response( 0 ).Sample, null, mpeg.Length ), files, files.LookupGeneration, audio );
		AdvisorEntryPreparation.Adopt( store );
		return audio;
	}

	/// <summary>
	/// A line its park's build decoded ahead is taken, not decoded again, in either mode: it starts at its due time with no lateness however long its decode over frames would
	/// have taken, and it plays the very PCM the build made.
	/// </summary>
	[TestMethod]
	public void ALineDecodedAheadIsTakenAndStartsWhenDue()
	{
		var previous = FileSystem;
		FileSystem = new BaseFileSystem( Directory.CreateTempSubdirectory( "opentpw-advisor-line-" ).FullName );
		try
		{
			foreach ( var sliced in new[] { true, false } )
			{
				Advisor.SlicedDecoding = sliced;
				Mp2Audio? played = null;
				Advisor.PlayerFactory = audio => new SpeechAudioPlayer( played = audio, new ClockedOutput( audio.SampleRate, () => now ) );
				using var advisor = NewAdvisor();
				var mpeg = Speech( 40 );
				var ahead = DecodedAhead( mpeg, FileSystem );
				var request = now;
				Say( advisor, DelayedAnimation, mpeg );
				Assert.AreEqual( 0, AdvisorEntryPreparation.Count, $"sliced {sliced}: the line took its decoded audio" );
				var start = AdvisorSpeechStart.For( request, DelayedAnimation );
				// 40 frames decoded one per update of 50 ms would end about 2 s after the request (TheMouthFollowsTheAudioAfterALateStart): this one is due at its start
				var started = UpdateUntilStarted( advisor, 50 );
				Assert.IsTrue( started >= start.AudioStartMilliseconds && started - start.AudioStartMilliseconds < 50, $"sliced {sliced}: started at the first update at its start ({started})" );
				Assert.AreEqual( TimeSpan.Zero, advisor.LipPosition, $"sliced {sliced}: no lateness" );
				Assert.AreSame( ahead, played, $"sliced {sliced}: the build's PCM plays" );
			}
		}
		finally
		{
			FileSystem = previous;
		}
	}

	/// <summary>A line decoded ahead before the file system changed what it reads (a mod or language switch) is not taken: the line is decoded again.</summary>
	[TestMethod]
	public void ALineDecodedAheadBeforeAFileSystemChangeIsDecodedAgain()
	{
		Advisor.SlicedDecoding = false;
		Mp2Audio? played = null;
		Advisor.PlayerFactory = audio => new SpeechAudioPlayer( played = audio, new ClockedOutput( audio.SampleRate, () => now ) );
		var files = new BaseFileSystem( Directory.CreateTempSubdirectory( "opentpw-advisor-line-" ).FullName );
		var previous = FileSystem;
		FileSystem = files;
		try
		{
			using var advisor = NewAdvisor();
			var mpeg = Speech( 10 );
			var ahead = DecodedAhead( mpeg, files );
			files.InvalidateLookups();
			Say( advisor, DelayedAnimation, mpeg );
			UpdateUntilStarted( advisor, 10 );
			Assert.IsNotNull( played );
			Assert.AreNotSame( ahead, played, "a stale line is decoded again" );
			CollectionAssert.AreEqual( ahead.Samples, played!.Samples, "to the same PCM" );
		}
		finally
		{
			FileSystem = previous;
		}
	}

	private Advisor NewAdvisor()
	{
		var advisor = Advisor.WithoutModel( () => now );
		advisor.ParticlePool = new NoParticles();
		advisor.PlayUiSound = _ => { };
		return advisor;
	}

	/// <summary>A mono MPEG-2 Layer II frame of silence at 22,050 Hz (1,152 samples, about 52 ms).</summary>
	private static byte[] LayerTwoFrame()
	{
		var frame = new byte[144 * 48000 / 22050];
		var word = 0xFFE00000u | 2u << 19 | 2u << 17 | 1u << 16 | 6u << 12 | 3u << 6;
		for ( var index = 0; index < 4; index++ )
			frame[index] = (byte)(word >> (24 - index * 8));
		return frame;
	}

	private static byte[] Speech( int frames ) => Enumerable.Range( 0, frames ).SelectMany( _ => LayerTwoFrame() ).ToArray();

	/// <summary>A generated sequence: its audio starts 800 ms after the request (<see cref="AdvisorSpeechStart.Delayed"/>).</summary>
	private const int DelayedAnimation = 0;

	/// <summary>A fixed sequence whose audio starts at the request.</summary>
	private static int ImmediateAnimation() => Enumerable.Range( 1, 200 ).First( AdvisorSequences.StartsImmediately );

	private static AdvisorResponse Response( int animation ) => new( 60_000, 0, 1, animation, 0, false );

	private void Say( Advisor advisor, int animation, byte[] mpeg ) =>
		advisor.SayResponseParts( Response( animation ), mpeg, null, new LipSyncTimeline( new uint[] { 100, 300 } ), "test", level: null );

	/// <summary>Advances the clock by <paramref name="step"/> and updates until the audio starts; the clock at the start.</summary>
	private long UpdateUntilStarted( Advisor advisor, int step )
	{
		for ( var frame = 0; frame < 1000 && !advisor.AudioStarted; frame++ )
		{
			Assert.IsTrue( advisor.IsSpeaking, "a line still decoding or waiting for its start is being spoken" );
			Assert.AreEqual( TimeSpan.Zero, advisor.LipPosition, "the mouth is silent until the audio starts" );
			now += step;
			advisor.Update();
		}
		Assert.IsTrue( advisor.AudioStarted );
		return now;
	}

	[TestMethod]
	public void TheMouthFollowsTheAudioAfterALateStart()
	{
		foreach ( var animation in new[] { DelayedAnimation, ImmediateAnimation() } )
		{
			using var advisor = NewAdvisor();
			var request = now;
			Say( advisor, animation, Speech( 40 ) );
			var start = AdvisorSpeechStart.For( request, animation );
			Assert.AreEqual( animation != DelayedAnimation, !start.Delayed );
			// 40 frames decoded one per update of 50 ms: the decode ends about 2 s after the request, well after the audio was due
			var started = UpdateUntilStarted( advisor, 50 );
			Assert.IsTrue( started > start.AudioStartMilliseconds, $"animation {animation}: the audio started late" );
			// at the late start the mouth stands where it stands at an on-time start: its lead over the audio
			Assert.AreEqual( TimeSpan.FromMilliseconds( start.LipLeadOverAudioMilliseconds ), advisor.LipPosition, $"animation {animation}" );
			foreach ( var step in new[] { 17, 250, 733 } )
			{
				now += step;
				advisor.Update();
				// the audio position counts whole samples: within one sample (about 0.05 ms) of the clock
				Assert.AreEqual( advisor.Position.TotalMilliseconds + start.LipLeadOverAudioMilliseconds, advisor.LipPosition.TotalMilliseconds, 0.1, $"animation {animation}: the mouth leads the audio heard by its lead" );
			}
			Assert.IsTrue( advisor.Position > TimeSpan.Zero );
		}
	}

	[TestMethod]
	public void TheAudioStartsOnlyOnceDueAndWithoutLatenessWhenTheDecodeIsDone()
	{
		using var advisor = NewAdvisor();
		var request = now;
		Say( advisor, DelayedAnimation, Speech( 10 ) );
		var start = AdvisorSpeechStart.For( request, DelayedAnimation );
		// the decode ends after 11 updates of 10 ms; the audio still waits for its start 800 ms after the request
		var started = UpdateUntilStarted( advisor, 10 );
		Assert.AreEqual( start.AudioStartMilliseconds, started, "started at the first update at its start, as the one-go decode starts" );
		Assert.AreEqual( TimeSpan.Zero, advisor.LipPosition, "no lateness: the delayed start's LIP clock begins with the audio" );
		now += 100;
		advisor.Update();
		Assert.AreEqual( TimeSpan.FromMilliseconds( 100 ), advisor.LipPosition );
		Assert.AreEqual( 100, advisor.Position.TotalMilliseconds, 0.1 );
	}

	[TestMethod]
	public void SilenceKillAndANewLineDuringTheDecode()
	{
		using var advisor = NewAdvisor();
		Say( advisor, DelayedAnimation, Speech( 40 ) );
		for ( var frame = 0; frame < 5; frame++ ) { now += 10; advisor.Update(); }
		Assert.IsTrue( advisor.IsSpeaking, "speaking while decoding" );
		Assert.IsFalse( advisor.AudioStarted );
		advisor.Silence();
		Assert.IsFalse( advisor.IsSpeaking, "silenced during the decode" );
		for ( var frame = 0; frame < 200; frame++ ) { now += 10; advisor.Update(); }
		Assert.IsFalse( advisor.AudioStarted, "a silenced decode never starts" );
		Assert.IsFalse( advisor.IsSpeaking );

		Say( advisor, DelayedAnimation, Speech( 40 ) );
		for ( var frame = 0; frame < 5; frame++ ) { now += 10; advisor.Update(); }
		Assert.IsTrue( advisor.IsDrawn );
		advisor.Kill();
		Assert.IsFalse( advisor.IsSpeaking, "killed during the decode" );
		Assert.IsFalse( advisor.IsDrawn, "and hidden" );
		for ( var frame = 0; frame < 200; frame++ ) { now += 10; advisor.Update(); }
		Assert.IsFalse( advisor.AudioStarted, "a killed decode never starts" );

		// a new line during the decode replaces it: the audio that starts is the new line's
		Say( advisor, DelayedAnimation, Speech( 40 ) );
		for ( var frame = 0; frame < 5; frame++ ) { now += 10; advisor.Update(); }
		Say( advisor, DelayedAnimation, Speech( 8 ) );
		var shortLine = Mp2Decoder.Decode( Speech( 8 ) );
		Assert.AreEqual( TimeSpan.FromSeconds( shortLine.DurationSeconds ), advisor.Duration );
		UpdateUntilStarted( advisor, 10 );
		now += (long)Math.Ceiling( shortLine.DurationSeconds * 1000 );
		advisor.Update();
		Assert.IsFalse( advisor.IsSpeaking, "the new line's audio ended at its own length" );
		Assert.IsFalse( advisor.TakeUnsaidLine() );
	}

	[TestMethod]
	public void ALineWhoseDecodeFailsAfterItStartedEndsAsALineThatCannotBeSaid()
	{
		var good = Layer1TestFrames.Frame( allocation: 1, bitrateIndex: 8 );
		// scalefactor index 63 is reserved: the header reads, the payload does not decode
		var damaged = Layer1TestFrames.Frame( allocation: 1, scale: 63, bitrateIndex: 8 );
		var mpeg = Enumerable.Range( 0, 6 ).SelectMany( _ => good ).Concat( damaged ).ToArray();
		Assert.IsTrue( Mp2Decoder.TryMeasure( mpeg, null, out _, out _ ), "the length is measured" );
		Assert.ThrowsException<InvalidDataException>( () => Mp2Decoder.Decode( mpeg ), "the one-go decode, before the line, fails" );

		using var advisor = NewAdvisor();
		Say( advisor, DelayedAnimation, mpeg );
		Assert.IsTrue( advisor.IsSpeaking );
		Assert.IsTrue( advisor.IsDrawn );
		for ( var frame = 0; frame < 20 && advisor.IsSpeaking; frame++ ) { now += 10; advisor.Update(); }
		Assert.IsFalse( advisor.IsSpeaking, "the line ended" );
		Assert.IsFalse( advisor.IsDrawn, "the model is hidden" );
		Assert.IsFalse( advisor.AudioStarted );
		Assert.IsTrue( advisor.TakeUnsaidLine(), "reported once" );
		Assert.IsFalse( advisor.TakeUnsaidLine() );
		for ( var frame = 0; frame < 100; frame++ ) { now += 10; advisor.Update(); }
		Assert.IsFalse( advisor.IsSpeaking, "the failure does not repeat" );
		Assert.IsFalse( advisor.TakeUnsaidLine() );
	}

	[TestMethod]
	public void AnUnsaidLineReservesWhatALineSkippedBeforeItStartsReserves()
	{
		static AdvisorScoreQueue Queue() => new( new[] { new AdvisorMessageDescriptor( 10_000, 50_000, 3, 0, 0, false, 0, 255 ) }, new Dictionary<int, AdvisorMessageHistory>(), 25 );
		static void Played( AdvisorScoreQueue queue, uint span )
		{
			queue.Enqueue( new AdvisorPendingAdvice( 10_000, 30, -1, false ), 100, true );
			var selection = queue.SelectNext( 1000 )!.Value;
			queue.BeginPlaybackAttempt( selection );
			queue.CompletePlaybackAttempt( selection, true, 1000, 100, span );
		}
		var skipped = Queue();
		Played( skipped, 0 );
		var unsaid = Queue();
		Played( unsaid, 6000 );
		unsaid.ReserveAsUnsaid();
		Assert.AreEqual( (skipped.LastActionStarted, skipped.LastActionDuration, skipped.CurrentMessageId), (unsaid.LastActionStarted, unsaid.LastActionDuration, unsaid.CurrentMessageId) );
		Assert.AreEqual( skipped.GetHistory( 10_000 ), unsaid.GetHistory( 10_000 ) );
		// a reservation a withdrawal ended stays ended
		var withdrawn = Queue();
		Played( withdrawn, 6000 );
		withdrawn.Withdraw( 10_000, 1500 );
		withdrawn.ReserveAsUnsaid();
		Assert.AreEqual( 0u, withdrawn.LastActionDuration );
	}
}
