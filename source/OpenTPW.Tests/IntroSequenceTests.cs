using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class IntroSequenceTests
{
	private sealed class Movie : IIntroMovie
	{
		public Action OnUpdate { get; init; } = () => { };
		public bool IsFinished { get; set; }
		public int Updates { get; private set; }
		public int Disposals { get; private set; }
		public void Update() { Updates++; OnUpdate(); }
		public void Skip() => IsFinished = true;
		public void Render() { }
		public void Dispose() => Disposals++;
	}

	[TestMethod]
	public void DecodeFailureDisposesOpenedMovieAndContinuesTheQueue()
	{
		var broken = new Movie { OnUpdate = () => throw new InvalidDataException( "truncated frame" ) };
		var following = new Movie();
		var opened = new List<string>();
		var scaling = new List<bool>();
		using var intro = new IntroSequence( new[] { "broken", "following" }, name =>
		{
			opened.Add( name );
			return name == "broken" ? broken : following;
		}, () => false, scaling.Add );
		var completions = 0;
		intro.Completed += () => completions++;
		intro.Update();
		Assert.AreEqual( 1, broken.Disposals );
		Assert.IsFalse( intro.IsCompleted );
		intro.Update();
		CollectionAssert.AreEqual( new[] { "broken", "following" }, opened );
		Assert.AreEqual( 1, following.Updates );
		following.IsFinished = true;
		intro.Update();
		intro.Update();
		intro.Update();
		Assert.AreEqual( 1, following.Disposals );
		Assert.AreEqual( 1, completions );
		CollectionAssert.AreEqual( new[] { false, true }, scaling );
	}

	[TestMethod]
	public void UnreadableOpenIsSkippedAndDisposeCancelsRemainingWork()
	{
		var current = new Movie();
		var opened = new List<string>();
		var intro = new IntroSequence( new[] { "denied", "current", "unopened" }, name =>
		{
			opened.Add( name );
			if ( name == "denied" ) throw new UnauthorizedAccessException();
			return current;
		}, () => false, _ => { } );
		var completions = 0;
		intro.Completed += () => completions++;
		intro.Update();
		intro.Dispose();
		intro.Dispose();
		intro.Update();
		Assert.AreEqual( 1, current.Disposals );
		Assert.AreEqual( 0, completions );
		CollectionAssert.AreEqual( new[] { "denied", "current" }, opened );
	}

	[TestMethod]
	public void ProgrammingFailureIsNotHiddenAsAnUnreadableMovie()
	{
		var movie = new Movie { OnUpdate = () => throw new InvalidOperationException( "programming failure" ) };
		using var intro = new IntroSequence( new[] { "movie" }, _ => movie, () => false, _ => { } );
		Assert.ThrowsException<InvalidOperationException>( intro.Update );
	}

	[TestMethod]
	public void CaptureAndEveryNonemptyEnvironmentOverrideBypassIntros()
	{
		Assert.IsTrue( IntroPlaylist.ShouldPlay( true, false, Array.Empty<string>(), null ) );
		Assert.IsFalse( IntroPlaylist.ShouldPlay( true, false, new[] { "--capture-world", "capture.png" }, null ) );
		Assert.IsFalse( IntroPlaylist.ShouldPlay( true, false, new[] { "--no-intro" }, null ) );
		Assert.IsFalse( IntroPlaylist.ShouldPlay( true, true, Array.Empty<string>(), null ) );
		Assert.IsFalse( IntroPlaylist.ShouldPlay( false, false, Array.Empty<string>(), null ) );
		foreach ( var value in new[] { "0", "1", "false", " " } )
			Assert.IsFalse( IntroPlaylist.ShouldPlay( true, false, Array.Empty<string>(), value ) );
	}

	private sealed class Audio : IMovieAudioOutput
	{
		public int SampleRate => 22050;
		public long PlayedFrames => 0;
		public long QueuedFrames => 0;
		public double LatencySeconds => 0;
		public int Disposals { get; private set; }
		public void Queue( ReadOnlySpan<short> samples ) { }
		public void Play() { }
		public void Stop() { }
		public void Dispose() => Disposals++;
	}

	[TestMethod]
	public void FailedPlaybackConstructionReleasesAudioAndSuccessTransfersOwnership()
	{
		var rejected = new Audio();
		Assert.ThrowsException<NotSupportedException>( () => MovieScreen.CreatePlayback( MoviePlaybackTests.Movie( 1, 0, frameRateTag: false ), rejected, 1 ) );
		Assert.AreEqual( 1, rejected.Disposals );
		var accepted = new Audio();
		using ( var playback = MovieScreen.CreatePlayback( MoviePlaybackTests.Movie( 1, 1 ), accepted, 0.5f ) )
		{
			Assert.AreEqual( 0, accepted.Disposals );
			Assert.AreEqual( 0.5f, playback.Gain );
		}
		Assert.AreEqual( 1, accepted.Disposals );
	}
}
