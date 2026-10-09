using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class LipSyncTimelineTests
{
	[TestMethod]
	public void TalksUntilFirstMarkAndTogglesAtEachMark()
	{
		// Global sp_001.LIP.
		var timeline = new LipSyncTimeline( new uint[] { 2226893, 2812380, 4058820 } );
		Assert.IsTrue( timeline.IsTalking( 0 ) );
		Assert.IsTrue( timeline.IsTalking( 2226892 ) );
		Assert.IsFalse( timeline.IsTalking( 2226893 ) );
		Assert.IsFalse( timeline.IsTalking( 2812379 ) );
		Assert.IsTrue( timeline.IsTalking( 2812380 ) );
		Assert.IsTrue( timeline.IsTalking( TimeSpan.FromSeconds( 4 ) ) );
		Assert.IsFalse( timeline.IsTalking( 4058820 ) );
		Assert.IsFalse( timeline.IsTalking( long.MaxValue ) );
		Assert.IsFalse( timeline.IsTalking( -1 ) );
		Assert.AreEqual( 4058820, timeline.EndMicroseconds );
		CollectionAssert.AreEqual( new[] { (0L, 2226893L), (2812380L, 4058820L) }, timeline.TalkingIntervals.ToArray() );
	}

	[TestMethod]
	public void LeadingZeroMarkStartsSilent()
	{
		var timeline = new LipSyncTimeline( new uint[] { 0, 611111, 2440317 } );
		Assert.IsFalse( timeline.IsTalking( 0 ) );
		Assert.IsFalse( timeline.IsTalking( 611110 ) );
		Assert.IsTrue( timeline.IsTalking( 611111 ) );
		CollectionAssert.AreEqual( new[] { (611111L, 2440317L) }, timeline.TalkingIntervals.ToArray() );
		Assert.AreEqual( 0, new LipSyncTimeline( new uint[] { 0 } ).TalkingIntervals.Count );
	}

	[TestMethod]
	public void SingleMarkIsOneUtterance()
	{
		var timeline = new LipSyncTimeline( new uint[] { 305804 } );
		Assert.IsTrue( timeline.IsTalking( 305803 ) );
		Assert.IsFalse( timeline.IsTalking( 305804 ) );
	}

	[TestMethod]
	public void EvenMarkCountLeavesFinalIntervalOpen()
	{
		var timeline = new LipSyncTimeline( new uint[] { 10, 20 } );
		Assert.IsTrue( timeline.IsTalking( 1_000_000 ) );
		Assert.AreEqual( long.MaxValue, timeline.TalkingIntervals[^1].EndMicroseconds );
	}

	[TestMethod]
	public void RejectsUnorderedMarks()
	{
		Assert.ThrowsException<ArgumentException>( () => new LipSyncTimeline( new uint[] { 5, 5 } ) );
		Assert.ThrowsException<ArgumentNullException>( () => new LipSyncTimeline( (IReadOnlyList<uint>)null! ) );
	}

	[TestMethod]
	public void OriginalTalkingIntervalsAreLouderThanSilentIntervals()
	{
		var bank = Mp2DecoderTests.OpenSpeechBank();
		var lipsPath = Path.Combine( Mp2DecoderTests.SpeechDirectory(), "lips.wad" );
		if ( !File.Exists( lipsPath ) )
			Assert.Inconclusive( "The global lips.wad is missing." );
		using var lips = new WadArchive( lipsPath );
		var talking = new List<double>();
		var silent = new List<double>();
		var clips = 0;
		var overruns = new List<string>();
		foreach ( var name in lips.GetFiles( "" ) )
		{
			var stem = Path.GetFileNameWithoutExtension( name );
			var clip = bank.soundFiles.Single( file => string.Equals( Path.GetFileNameWithoutExtension( file.Name ), stem, StringComparison.OrdinalIgnoreCase ) );
			if ( (clip.FrameData[1] & 0x06) == 0x06 )
				continue; // z_error is Layer I.
			var audio = Mp2Decoder.Decode( clip.FrameData );
			var timeline = new LipSyncTimeline( new LipSyncFile( new MemoryStream( lips.GetFile( name ).GetData() ) ) );
			if ( timeline.EndMicroseconds > audio.DurationSeconds * 1e6 )
				overruns.Add( stem );
			clips++;
			// 10 ms RMS windows, classified by the timeline at the window centre.
			const int window = 220;
			for ( var start = 0; start + window <= audio.Samples.Length; start += window )
			{
				var energy = 0.0;
				for ( var index = start; index < start + window; index++ )
					energy += (double)audio.Samples[index] * audio.Samples[index];
				var decibels = 10 * Math.Log10( energy / window + 1e-3 );
				var centre = (long)((start + window / 2) * 1e6 / audio.SampleRate);
				(timeline.IsTalking( centre ) ? talking : silent).Add( decibels );
			}
		}
		Assert.AreEqual( 638, clips );
		CollectionAssert.AreEqual( new[] { "sp_478" }, overruns );
		Console.WriteLine( $"{talking.Count} talking windows {talking.Average():F1} dB; {silent.Count} silent windows {silent.Average():F1} dB (re 1 LSB)." );
		Assert.IsTrue( talking.Average() - silent.Average() > 20, $"talking {talking.Average():F1} dB, silent {silent.Average():F1} dB" );
	}

	[TestMethod]
	public void OriginalLastMarkFitsSpeechDurationInMicroseconds()
	{
		// Independent check of ADVISOR-013: if marks are microseconds, last / duration is near 1
		// over the corpus (a millisecond or 1/1000 unit would give ~1000 or ~0.001).
		var bank = Mp2DecoderTests.OpenSpeechBank();
		var lipsPath = Path.Combine( Mp2DecoderTests.SpeechDirectory(), "lips.wad" );
		if ( !File.Exists( lipsPath ) )
			Assert.Inconclusive( "The global lips.wad is missing." );
		using var lips = new WadArchive( lipsPath );
		var ratios = new List<double>();
		var overruns = new List<string>();
		var shortest = double.MaxValue;
		foreach ( var name in lips.GetFiles( "" ) )
		{
			var stem = Path.GetFileNameWithoutExtension( name );
			var clip = bank.soundFiles.Single( file => string.Equals( Path.GetFileNameWithoutExtension( file.Name ), stem, StringComparison.OrdinalIgnoreCase ) );
			if ( (clip.FrameData[1] & 0x06) == 0x06 )
				continue; // z_error is Layer I.
			var audio = Mp2Decoder.Decode( clip.FrameData );
			var timeline = new LipSyncTimeline( new LipSyncFile( new MemoryStream( lips.GetFile( name ).GetData() ) ) );
			var ratio = timeline.EndMicroseconds / (audio.DurationSeconds * 1e6);
			ratios.Add( ratio );
			if ( ratio > 1 )
				overruns.Add( stem );
			if ( timeline.EndMicroseconds > 0 )
				shortest = Math.Min( shortest, ratio );
		}
		ratios.Sort();
		var median = ratios[ratios.Count / 2];
		Console.WriteLine( $"last mark / duration: n={ratios.Count} min={ratios[0].ToString( "F6", CultureInfo.InvariantCulture )} shortest-talking={shortest.ToString( "F6", CultureInfo.InvariantCulture )} median={median.ToString( "F6", CultureInfo.InvariantCulture )} max={ratios[^1].ToString( "F6", CultureInfo.InvariantCulture )}" );
		Assert.AreEqual( 638, ratios.Count );
		// sp_478 is the only English global clip whose last mark passes its end (about 1.05x).
		CollectionAssert.AreEqual( new[] { "sp_478" }, overruns );
		Assert.IsTrue( ratios[^1] < 1.06, $"max {ratios[^1]}" );
		Assert.IsTrue( shortest > 0.03, $"shortest talking clip {shortest}" );
		Assert.IsTrue( median > 0.95 && median < 1.0, $"median {median}" );
	}
}
