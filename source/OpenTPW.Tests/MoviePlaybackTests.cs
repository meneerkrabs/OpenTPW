using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class MoviePlaybackTests
{
	private const int SampleRate = 22050;
	private const int BlockSamples = 728;

	// ---- synthetic movies -----------------------------------------------------------------------

	private static byte[] Chunk( string fourCC, byte[] payload )
	{
		var chunk = new byte[8 + payload.Length];
		Encoding.ASCII.GetBytes( fourCC ).CopyTo( chunk, 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( chunk.AsSpan( 4 ), (uint)chunk.Length );
		payload.CopyTo( chunk, 8 );
		return chunk;
	}

	// One 16x16 intra frame whose six blocks are DC size 0 + end of block: Y "100"+"10" x4, C "00"+"10" x2.
	private static byte[] Frame()
	{
		var bits = string.Concat( Enumerable.Repeat( "10010", 4 ) ) + "0010" + "0010";
		var word = Convert.ToUInt32( bits.PadRight( 32, '0' ), 2 );
		var payload = new byte[12];
		BinaryPrimitives.WriteUInt16LittleEndian( payload, 16 );
		BinaryPrimitives.WriteUInt16LittleEndian( payload.AsSpan( 2 ), 16 );
		payload[4] = 99;
		BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( 8 ), word );
		return payload;
	}

	private static byte[] AudioBlock( int samples, byte sampleByte )
	{
		var frames = (samples + 27) / 28;
		var block = new byte[12 + frames * 30];
		BinaryPrimitives.WriteUInt32LittleEndian( block, (uint)samples );
		for ( var frame = 0; frame < frames; frame++ )
		{
			block[12 + frame * 30] = 0x00;
			block[13 + frame * 30] = 0xcc;
			for ( var index = 0; index < 28; index++ )
				block[14 + frame * 30 + index] = sampleByte;
		}
		return block;
	}

	/// <summary>Builds a 16x16 movie with <paramref name="frames"/> video frames and audio blocks of 728 samples.</summary>
	private static TgqMovieFile Movie( int frames, int audioBlocks, bool frameRateTag = true )
	{
		var samples = audioBlocks * BlockSamples;
		var tags = new List<byte>();
		if ( frameRateTag )
			tags.AddRange( new byte[] { 0x1b, 1, 30 } );
		tags.AddRange( new byte[] { 0x85, 4, (byte)(samples >> 24), (byte)(samples >> 16), (byte)(samples >> 8), (byte)samples, 0x82, 1, 2, 0x83, 1, 7, 0xff } );
		var header = new byte[] { (byte)'P', (byte)'T', 0, 0 }.Concat( tags ).ToArray();
		var count = new byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian( count, (uint)audioBlocks );
		var parts = new List<byte[]> { Chunk( "SCHl", header ), Chunk( "SCCl", count ) };
		for ( var index = 0; index < Math.Max( frames, audioBlocks ); index++ )
		{
			if ( index < frames )
				parts.Add( Chunk( "pIQT", Frame() ) );
			if ( index < audioBlocks )
				parts.Add( Chunk( "SCDl", AudioBlock( BlockSamples, (byte)(0x10 + index % 7) ) ) );
		}
		parts.Add( Chunk( "SCEl", Array.Empty<byte>() ) );
		return new TgqMovieFile( new MemoryStream( parts.SelectMany( part => part ).ToArray() ) );
	}

	/// <summary>Audio device that reports its position in whole device buffers, like SDL's queue.</summary>
	private sealed class SteppedAudioOutput : IMovieAudioOutput
	{
		private readonly SimulatedMovieAudioOutput inner = new( MoviePlaybackTests.SampleRate );
		private readonly int bufferFrames;
		public SteppedAudioOutput( int bufferFrames ) => this.bufferFrames = bufferFrames;
		public int SampleRate => inner.SampleRate;
		public long PlayedFrames => inner.PlayedFrames / bufferFrames * bufferFrames;
		public long QueuedFrames => inner.QueuedFrames;
		public double LatencySeconds => bufferFrames / (double)SampleRate;
		public void Queue( ReadOnlySpan<short> interleavedStereo ) => inner.Queue( interleavedStereo );
		public void Play() => inner.Play();
		public void Stop() => inner.Stop();
		public void Advance( double seconds ) => inner.Advance( seconds );
		public void Dispose() => inner.Dispose();
	}

	// ---- sync policy ----------------------------------------------------------------------------

	[TestMethod]
	public void FixedStepClockAt60HzShowsEveryFrameOnceAndHoldsEachForOneUpdate()
	{
		var result = MovieSimulation.Run( Movie( 30, 0 ), 1d / 60, withAudio: false );
		Assert.AreEqual( 30, result.FramesDecoded );
		Assert.AreEqual( 0, result.FramesDropped );
		Assert.AreEqual( 29, result.LastFrameIndex );
		Assert.AreEqual( 30, result.Holds );
		Assert.AreEqual( 1.0, result.Clock, 1e-9 );
	}

	[TestMethod]
	public void SlowUpdatesDropFramesWithoutDecodingThem()
	{
		var decoded = new List<int>();
		var result = MovieSimulation.Run( Movie( 30, 0 ), 0.05, withAudio: false, onFrame: playback => decoded.Add( playback.CurrentFrameIndex ) );
		// Three 1/60 s ticks per update: frames floor(1.5 k); the update reaching 1.0 s ends playback instead.
		Assert.AreEqual( 20, result.FramesDecoded );
		Assert.AreEqual( 9, result.FramesDropped );
		Assert.AreEqual( 28, result.LastFrameIndex );
		Assert.AreEqual( decoded.Count, result.FramesDecoded );
		CollectionAssert.AreEqual( decoded.OrderBy( index => index ).ToList(), decoded );
		Assert.IsTrue( decoded.Zip( decoded.Skip( 1 ), ( a, b ) => b - a ).All( step => step is 1 or 2 ) );
	}

	[TestMethod]
	public void HitchWithoutAudioIsCappedByTheFixedStepClock()
	{
		using var playback = new MoviePlayback( Movie( 60, 0 ), null );
		Assert.IsTrue( playback.Update( 0 ) );
		Assert.AreEqual( 0, playback.CurrentFrameIndex );
		Assert.IsTrue( playback.Update( 1.0 ) );
		// 16 catch-up ticks of 1/60 s: the video loses the rest of the hitch instead of jumping a second ahead.
		Assert.AreEqual( 16 / 60.0, playback.Clock, 1e-9 );
		Assert.AreEqual( 8, playback.CurrentFrameIndex );
		Assert.AreEqual( 7, playback.FramesDropped );
		Assert.AreEqual( MovieClockSource.FixedStep, playback.ClockSource );
	}

	[TestMethod]
	public void AudioPositionDrivesTheClockRegardlessOfElapsedTime()
	{
		var output = new SimulatedMovieAudioOutput( SampleRate, speed: 0.5 );
		using var playback = new MoviePlayback( Movie( 30, 31 ), output );
		Assert.AreEqual( MovieClockSource.Audio, playback.ClockSource );
		playback.Update( 0 );
		Assert.IsTrue( output.IsPlaying );
		for ( var update = 0; update < 60; update++ )
		{
			output.Advance( 1d / 60 );
			playback.Update( 1d / 60 );
		}
		// One wall-clock second at half device speed is half a second of movie time.
		Assert.AreEqual( 0.5, playback.Clock, 1d / SampleRate );
		Assert.AreEqual( 15, playback.CurrentFrameIndex );
		Assert.AreEqual( 0, playback.FramesDropped );
	}

	[TestMethod]
	public void SteppedAudioPositionIsSmoothedWithoutDrops()
	{
		var output = new SteppedAudioOutput( 1024 );
		using var playback = new MoviePlayback( Movie( 30, 31 ), output );
		playback.Update( 0 );
		var maximumLag = 0.0;
		for ( var update = 1; update <= 50; update++ )
		{
			output.Advance( 1d / 60 );
			playback.Update( 1d / 60 );
			// The clock may trail the true position by at most the device buffer and never runs ahead of it.
			var truth = update / 60.0 - output.LatencySeconds;
			Assert.IsTrue( playback.Clock <= Math.Max( 0, truth ) + output.LatencySeconds + 1e-9, $"update {update}" );
			maximumLag = Math.Max( maximumLag, truth - playback.Clock );
		}
		Assert.AreEqual( 0, playback.FramesDropped );
		Assert.IsTrue( maximumLag <= output.LatencySeconds + 1d / 60, maximumLag.ToString() );
	}

	[TestMethod]
	public void LongerAudioHoldsTheLastFrameUntilItEnds()
	{
		var result = MovieSimulation.Run( Movie( 30, 61 ), 1d / 60, withAudio: true );
		Assert.AreEqual( 30, result.FramesDecoded );
		Assert.AreEqual( 0, result.FramesDropped );
		Assert.AreEqual( 29, result.LastFrameIndex );
		Assert.IsTrue( result.Clock >= 61.0 * BlockSamples / SampleRate, result.Clock.ToString() );
		Assert.IsTrue( result.Clock < 61.0 * BlockSamples / SampleRate + 0.05, result.Clock.ToString() );
		// Muted playback ends with the video instead.
		Assert.AreEqual( 1.0, MovieSimulation.Run( Movie( 30, 61 ), 1d / 60, withAudio: false ).Clock, 1e-9 );
	}

	[TestMethod]
	public void ShorterAudioHandsOverToTheFixedStepClock()
	{
		var output = new SimulatedMovieAudioOutput( SampleRate );
		using var playback = new MoviePlayback( Movie( 60, 15 ), output );
		playback.Update( 0 );
		var updates = 0;
		while ( !playback.IsFinished && updates++ < 1000 )
		{
			output.Advance( 1d / 60 );
			playback.Update( 1d / 60 );
		}
		Assert.IsTrue( playback.IsFinished );
		Assert.AreEqual( 2.0, playback.Duration, 1e-9 );
		Assert.AreEqual( 60, playback.FramesDecoded + playback.FramesDropped );
		Assert.IsTrue( playback.FramesDropped <= 1, playback.FramesDropped.ToString() );
		Assert.AreEqual( MovieClockSource.FixedStep, playback.ClockSource );
	}

	[TestMethod]
	public void AudioQueueStaysBoundedAndNeverUnderrunsMidStream()
	{
		var result = MovieSimulation.Run( Movie( 30, 120 ), 1d / 60, withAudio: true );
		var lead = (long)(MoviePlayback.AudioLeadSeconds * SampleRate);
		Assert.IsTrue( result.MaximumQueuedAudioFrames >= lead );
		Assert.IsTrue( result.MaximumQueuedAudioFrames < lead + MoviePlayback.AudioChunkFrames );
		// Only the last device step may ask for audio past the end of the soundtrack.
		Assert.IsTrue( result.UnderrunFrames <= SampleRate / 60 + 1, result.UnderrunFrames.ToString() );
	}

	[TestMethod]
	public void SkipStopsAudioAndFinishes()
	{
		var output = new SimulatedMovieAudioOutput( SampleRate );
		using var playback = new MoviePlayback( Movie( 30, 31 ), output );
		playback.Update( 0 );
		Assert.IsTrue( output.QueuedFrames > 0 );
		playback.Skip();
		Assert.IsTrue( playback.IsFinished && playback.WasSkipped );
		Assert.IsFalse( output.IsPlaying );
		Assert.AreEqual( 0, output.QueuedFrames );
		Assert.IsFalse( playback.Update( 1 ) );
		Assert.AreEqual( 0, playback.CurrentFrameIndex );
	}

	[TestMethod]
	public void RejectsMissingFrameRateMismatchedOutputAndBadElapsedTime()
	{
		Assert.ThrowsException<NotSupportedException>( () => new MoviePlayback( Movie( 2, 1, frameRateTag: false ), null ) );
		Assert.ThrowsException<ArgumentException>( () => new MoviePlayback( Movie( 2, 1 ), new SimulatedMovieAudioOutput( 44100 ) ) );
		using var playback = new MoviePlayback( Movie( 2, 1 ), null );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => playback.Update( -1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => playback.Update( double.NaN ) );
	}

	// ---- streaming audio, colour, presentation helpers -------------------------------------------

	[DataTestMethod]
	[DataRow( 1 )]
	[DataRow( 333 )]
	[DataRow( 4096 )]
	public void AudioReaderStreamsTheSamePcmAsWholeDecode( int chunkFrames )
	{
		var movie = Movie( 1, 9 );
		var expected = movie.DecodeAudio();
		var reader = new TgqAudioReader( movie );
		var actual = new List<short>();
		var buffer = new short[chunkFrames * 2];
		int frames;
		while ( (frames = reader.Read( buffer )) > 0 )
			actual.AddRange( buffer.Take( frames * 2 ) );
		CollectionAssert.AreEqual( expected, actual );
		Assert.IsTrue( reader.EndOfStream );
		Assert.AreEqual( 9L * BlockSamples, reader.Position );
	}

	[TestMethod]
	public void RgbaConversionMatchesRgb24WithOpaqueAlpha()
	{
		var frame = TqiDecoder.Decode( Frame() );
		var rgb = frame.ToRgb24();
		var rgba = new byte[16 * 16 * 4];
		frame.WriteRgba32( rgba );
		for ( var pixel = 0; pixel < 256; pixel++ )
		{
			CollectionAssert.AreEqual( rgb.Skip( pixel * 3 ).Take( 3 ).ToArray(), rgba.Skip( pixel * 4 ).Take( 3 ).ToArray() );
			Assert.AreEqual( 255, rgba[pixel * 4 + 3] );
		}
		Assert.ThrowsException<ArgumentException>( () => frame.WriteRgba32( new byte[10] ) );
	}

	[DataTestMethod]
	[DataRow( 1280, 720, 312, 0, 655, 720 )]
	[DataRow( 320, 352, 0, 0, 320, 352 )]
	[DataRow( 640, 1000, 0, 148, 640, 704 )]
	public void FitKeepsSquarePixelAspectCentred( int targetWidth, int targetHeight, int x, int y, int width, int height )
	{
		Assert.AreEqual( (x, y, width, height), MoviePresenter.Fit( 320, 352, targetWidth, targetHeight ) );
	}

	[DataTestMethod]
	[DataRow( 640, 480, 0, 64, 640, 352 )]
	[DataRow( 1280, 720, 0, 8, 1280, 704 )]
	[DataRow( 1280, 960, 0, 128, 1280, 704 )]
	public void FitUsesTheOriginalsStretchedDisplayRectangle( int targetWidth, int targetHeight, int x, int y, int width, int height )
	{
		// A 4:3 screen shows the movie in 640 x 352 centred vertically, as the original's movie box does.
		Assert.AreEqual( (x, y, width, height), MoviePresenter.Fit( 320, 352, targetWidth, targetHeight, MoviePresenter.OriginalDisplayAspect ) );
	}

	[TestMethod]
	public void LibraryResolvesNamesCaseInsensitivelyInsideMovies()
	{
		var root = Path.Combine( Path.GetTempPath(), $"opentpw-movies-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( root, "movies" ) );
		try
		{
			File.WriteAllBytes( Path.Combine( root, "movies", "BF.TGQ" ), Array.Empty<byte>() );
			Assert.AreEqual( Path.Combine( root, "movies", "BF.TGQ" ), MovieLibrary.Resolve( root, "bf" ) );
			Assert.AreEqual( Path.Combine( root, "movies", "BF.TGQ" ), MovieLibrary.Resolve( root, "bf.tgq" ) );
			CollectionAssert.AreEqual( new[] { "BF" }, MovieLibrary.List( root ).ToArray() );
			Assert.ThrowsException<FileNotFoundException>( () => MovieLibrary.Resolve( root, "plan" ) );
			Assert.ThrowsException<ArgumentException>( () => MovieLibrary.Resolve( root, "../bf" ) );
			Assert.ThrowsException<ArgumentException>( () => MovieLibrary.Resolve( root, "sub/bf" ) );
		}
		finally
		{
			Directory.Delete( root, true );
		}
	}

	// ---- private original corpus ----------------------------------------------------------------

	[DataTestMethod]
	[DataRow( "bf.tgq", 255 )]
	[DataRow( "plan.tgq", 1138 )]
	public void OriginalMoviesPlayHeadlessWithoutDropsAt60Hz( string name, int frames )
	{
		var movie = new TgqMovieFile( File.OpenRead( Path.Combine( OriginalMovieDirectory(), name ) ) );
		var result = MovieSimulation.Run( movie, 1d / 60, withAudio: true );
		Assert.AreEqual( frames, result.FramesDecoded );
		Assert.AreEqual( 0, result.FramesDropped );
		var audioSeconds = movie.AudioHeader.SampleCount / (double)movie.AudioHeader.SampleRate;
		Assert.IsTrue( result.Clock >= audioSeconds && result.Clock < audioSeconds + 0.05, result.Clock.ToString() );
		Assert.IsTrue( result.UnderrunFrames <= movie.AudioHeader.SampleRate / 60 + 1 );
	}

	[TestMethod]
	public void PlanHoldsItsLastFrameThroughTheAudioTail()
	{
		var movie = new TgqMovieFile( File.OpenRead( Path.Combine( OriginalMovieDirectory(), "plan.tgq" ) ) );
		double lastFrameShownAt = -1;
		var result = MovieSimulation.Run( movie, 1d / 60, withAudio: true, onFrame: playback =>
		{
			if ( playback.CurrentFrameIndex == movie.VideoFrameCount - 1 )
				lastFrameShownAt = playback.Clock;
		} );
		Assert.AreEqual( 1137.0 / 30, lastFrameShownAt, 1d / 30 );
		Assert.IsTrue( result.Clock - lastFrameShownAt > 12.8, (result.Clock - lastFrameShownAt).ToString() );
	}

	[TestMethod]
	public void OriginalMovieDropsFramesOnSlowUpdatesButKeepsAudioTime()
	{
		var movie = new TgqMovieFile( File.OpenRead( Path.Combine( OriginalMovieDirectory(), "bf.tgq" ) ) );
		var result = MovieSimulation.Run( movie, 1d / 12, withAudio: true );
		Assert.AreEqual( 255, result.FramesDecoded + result.FramesDropped );
		Assert.IsTrue( result.FramesDropped > 100, result.FramesDropped.ToString() );
		Assert.IsTrue( result.Clock >= 194815.0 / 22050 );
	}

	private static string OriginalMovieDirectory()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the selected original TGQ movie corpus." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var directory = Directory.EnumerateDirectories( dataPath ).FirstOrDefault( entry => string.Equals( Path.GetFileName( entry ), "movies", StringComparison.OrdinalIgnoreCase ) );
		if ( directory == null )
			Assert.Inconclusive( "The selected original TGQ movie corpus is missing." );
		return directory!;
	}
}
