using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's advisor presentation (Game.exe 0x715AB0, 0x715BC0, 0x716230, 0x716950, 0x716ED0; docs/reverse/TPI-ADVISOR.md §4). Every DataRow below is a case of
/// tools/ppc-analysis/oracle_x86/tpi_advisor_presentation_oracle.py, which runs it on the executable with rand() replaced by the C runtime's generator from the same seed and the clip table
/// <see cref="TrailerWords"/> written to 0xBEF0D0, and compares the outcome.
/// </summary>
[TestClass]
public sealed class ThemeParkIncAdvisorPresentationTests : ThemeParkIncScienceTestBase
{
	/// <summary>Synthetic trailer words of clips 1 to 25 (the oracle writes word × 1000 / 30 to 0xBEF0D0, clip 14 as 0).</summary>
	internal static readonly uint[] TrailerWords = { 36, 45, 27, 63, 21, 54, 78, 12, 90, 33, 24, 39, 66, 30, 20, 15, 14, 9, 21, 27, 30, 11, 18, 8, 19 };

	private static AdvisorClipDurations Durations() => new( TrailerWords, AdvisorClipDurations.ThemeParkIncCount );

	/// <summary>Speech data with given voices and LIP marks by response and the synthetic clip table.</summary>
	private sealed class Speech( Dictionary<int, uint>? voices = null, Dictionary<int, uint[]>? lips = null ) : IThemeParkIncAdvisorSpeechData
	{
		public uint? VoiceLength( ThemeParkIncAdviceResponse response ) => voices != null && voices.TryGetValue( response.Response, out var length ) ? length : null;
		public IReadOnlyList<uint>? LipMarks( ThemeParkIncAdviceResponse response ) => lips != null && lips.TryGetValue( response.Response, out var marks ) ? marks : null;
		public AdvisorClipDurations? ClipDurations => Durations();
	}

	/// <summary>A counting rand() from <paramref name="seed"/>.</summary>
	private sealed class Counted( uint seed )
	{
		private readonly AdvisorRandom random = new( seed );
		public int Draws { get; private set; }
		public int Next()
		{
			Draws++;
			return random.Next();
		}
	}

	/// <summary>The draws a presentation stream made since <paramref name="seed"/>.</summary>
	private static int DrawsSince( uint seed, uint state )
	{
		var random = new AdvisorRandom( seed );
		for ( var draws = 0; draws < 100_000; draws++ )
		{
			if ( random.State == state )
				return draws;
			random.Next();
		}
		throw new AssertFailedException( "the stream is not reached from the seed" );
	}

	private static string Joined( IEnumerable<int> values ) => string.Join( ",", values );

	[DataTestMethod]
	[DataRow( 0, 1u, 666, "15", 0 )]
	[DataRow( 1, 1u, 1866, "14,1,15", 2 )]
	[DataRow( 501, 1u, 2266, "14,8,1,15", 3 )]
	[DataRow( 501, 7u, 1866, "14,1,15", 2 )]
	[DataRow( 6000, 1u, 6866, "14,8,5,1,10,5,4,15", 7 )]
	[DataRow( 6000, 12345u, 7066, "14,5,6,6,4,15", 5 )]
	[DataRow( 14000, 3u, 14966, "14,7,5,2,2,8,3,4,8,9,1,15", 11 )]
	[DataRow( 100000, 1u, 23966, "14,8,5,1,10,5,9,9,3,5,6,6,2,8,2,2,6,3,8,15", 19 )]
	[DataRow( -5, 1u, 666, "15", 0 )]
	public void GeneratedSequenceMatchesTheExecutable( int budget, uint seed, int sum, string clips, int draws )
	{
		var rand = new Counted( seed );
		var sequence = ThemeParkIncAdvisorSequences.Generate( budget, Durations(), rand.Next );
		Assert.AreEqual( (sum, clips, draws), (sequence.SumMilliseconds, Joined( sequence.Clips ), rand.Draws ) );
	}

	[DataTestMethod]
	[DataRow( 0, 5000, 1u, 500, "16", -1, 1, 0 )]
	[DataRow( 1, 5000, 1u, 1900, "18,19,20", -1, 1, 0 )]
	[DataRow( 2, 5000, 1u, 5966, "14,8,5,1,10,5,1,15", -1, 0, 7 )]
	[DataRow( 3, 5000, 1u, 5966, "14,8,5,1,10,5,1,15", -1, 0, 7 )]
	[DataRow( 4, 5000, 1u, 5966, "14,8,5,1,10,5,1,15", -1, 0, 7 )]
	[DataRow( 5, 5000, 1u, 6232, "21,21,21,21,21,22,23,24", -1, 1, 0 )]
	[DataRow( 6, 5000, 1u, 5966, "14,8,5,1,10,5,1,15", -1, 0, 7 )]
	[DataRow( 7, 5000, 1u, 5966, "14,8,5,1,10,5,1,15", -1, 0, 7 )]
	[DataRow( 8, 5000, 1u, 5966, "14,8,5,1,10,5,1,15", -1, 0, 7 )]
	[DataRow( 9, 5000, 1u, 2557, "25,25,25,25,26", -1, 1, 0 )]
	[DataRow( 10, 5000, 1u, 2166, "14,5,11,15", -1, 0, 0 )]
	[DataRow( 11, 5000, 1u, 5166, "14,11,1,12,1,15", -1, 0, 0 )]
	[DataRow( 12, 5000, 1u, 2966, "14,11,11,5,15", -1, 0, 0 )]
	[DataRow( 13, 5000, 1u, 5966, "14,8,5,1,10,5,1,15", -1, 0, 7 )]
	[DataRow( 14, 5000, 1u, 3566, "14,11,4,15", -1, 0, 0 )]
	[DataRow( 15, 5000, 1u, 5166, "14,5,1,11,6,15", -1, 0, 0 )]
	[DataRow( 16, 5000, 1u, 4466, "14,11,9,15", -1, 0, 0 )]
	[DataRow( 17, 5000, 1u, 5966, "14,8,5,1,10,5,1,15", 17000, 0, 7 )]
	[DataRow( 18, 5000, 1u, 3764, "17,17,17,17,18,19,20", 3864, 1, 0 )]
	[DataRow( 19, 5000, 1u, 5966, "14,8,5,1,10,5,1,15", -1, 0, 7 )]
	[DataRow( 9, 0, 1u, 2557, "25,25,25,25,26", -1, 1, 0 )]
	[DataRow( 17, 0, 1u, 0, "", 17000, 0, 0 )]
	[DataRow( 17, 3000, 9u, 3666, "14,2,2,15", 17000, 0, 3 )]
	public void CannedSequenceMatchesTheExecutable( int row, int budget, uint seed, int sum, string clips, int timer, int immediate, int draws )
	{
		var rand = new Counted( seed );
		var (sequence, starts) = ThemeParkIncAdvisorSequences.Canned( row, budget, Durations(), rand.Next );
		Assert.AreEqual( (sum, clips, timer, immediate == 1, draws), (sequence.SumMilliseconds, Joined( sequence.Clips ), sequence.TimerMilliseconds ?? -1, starts, rand.Draws ) );
	}

	[TestMethod]
	public void ClipTwentySixReadsTheClipListsFirstWord()
	{
		// row 9 (the forecasts' animation 25) is 25, 25, 25, 25, 26: the table has no entry 26, and the length the builder reads there is the clip list's first word, 25
		var (sequence, _) = ThemeParkIncAdvisorSequences.Canned( 9, 5_000, Durations(), () => throw new AssertFailedException( "no draw" ) );
		Assert.AreEqual( (int)(4 * Durations().Of( 25 )) + 25, sequence.SumMilliseconds );
	}

	/// <summary>The response rows of the speech cases: four the queue plays and three synthetic ones the oracle writes to 0xBEF1F0 too (a generated sequence behind a shown head, the canned
	/// timer rows 18 and 17).</summary>
	private static ThemeParkIncAdviceResponse Row( int response ) => response switch
	{
		900 => new( 900, 766, 766, -1, 0, false, -1, ThemeParkIncAdviceResponse.NoTag ),
		901 => new( 901, 766, 766, 34, 1, false, -1, ThemeParkIncAdviceResponse.NoTag ),
		902 => new( 902, 766, 766, 33, 1, false, -1, ThemeParkIncAdviceResponse.NoTag ),
		_ => ThemeParkIncAdviceResponse.For( response )!.Value
	};

	private static uint[]? Marks( string marks ) => marks.Length == 0 ? null : marks.Split( ';' ).Select( uint.Parse ).ToArray();

	[DataTestMethod]
	[DataRow( 645, 5526, "", 1, 0, 0, 0, 10000, 1u, 4223, 9800u, 12357u, 0u, 1, 0, 0, 0, 9799, 0, 0u, "25,25,25,25,26" )]
	[DataRow( 642, 5758, "", 1, 0, 0, 0, 2000, 1u, 4223, 1800u, 4357u, 0u, 1, 0, 0, 0, 1799, 0, 0u, "25,25,25,25,26" )]
	[DataRow( 748, 20433, "", 1, 0, 0, 0, 10000, 1u, 23732, 10800u, 32866u, 10800u, 0, 1, 17, 0, 10799, 0, 0u, "14,8,5,1,10,5,9,9,3,5,6,6,2,8,2,2,1,15" )]
	[DataRow( 748, 20433, "", 1, 0, 0, 0, 10000, 99u, 23832, 10800u, 32966u, 10800u, 0, 1, 16, 0, 10799, 0, 0u, "14,6,10,10,1,8,7,10,8,8,9,2,4,6,6,1,15" )]
	[DataRow( 748, -1, "", 1, 0, 0, 0, 10000, 1u, 3932, 10800u, 13066u, 10800u, 0, 1, 3, 0, 10799, 0, 0u, "14,8,1,15" )]
	[DataRow( 645, -1, "", 1, 0, 0, 0, 10000, 1u, 4223, 9800u, 12357u, 0u, 0, 0, 0, 0, 9799, 0, 0u, "25,25,25,25,26" )]
	[DataRow( 749, 13978, "", 1, 0, 0, 1, 50, 1u, 4223, 4294967146u, 2407u, 0u, 1, 1, 0, 0, -151, 0, 0u, "25,25,25,25,26" )]
	[DataRow( 645, 5526, "", 1, 0, 1, 0, 10000, 1u, 0, 0u, 0u, 0u, 0, 0, 0, 0, 0, 0, 0u, "" )]
	[DataRow( 645, 5526, "", 0, 0, 0, 0, 10000, 1u, 0, 0u, 0u, 0u, 0, 0, 0, 0, 0, 0, 0u, "" )]
	[DataRow( 645, 5526, "", 0, 1, 0, 0, 10000, 1u, 4223, 9800u, 12357u, 0u, 1, 0, 0, 0, 9799, 0, 0u, "25,25,25,25,26" )]
	[DataRow( 748, 20433, "", 0, 1, 0, 0, 10000, 5u, 24432, 10800u, 33566u, 10800u, 0, 1, 17, 0, 10799, 0, 0u, "14,4,6,10,1,1,8,10,3,1,5,2,4,7,7,8,1,15" )]
	[DataRow( 645, 5526, "1141088;1539410;2441133", 1, 0, 0, 0, 10000, 1u, 4223, 9800u, 12357u, 0u, 1, 0, 0, 1, 10941, 1, 0u, "25,25,25,25,26" )]
	[DataRow( 645, 5526, "120000;400000", 1, 0, 0, 0, 10000, 1u, 4223, 9800u, 12357u, 0u, 1, 0, 0, 1, 9920, 1, 0u, "25,25,25,25,26" )]
	[DataRow( 645, 5526, "1141088;1539410", 1, 0, 0, 1, 10000, 1u, 4223, 9800u, 12357u, 0u, 1, 1, 0, 0, 9799, 0, 0u, "25,25,25,25,26" )]
	[DataRow( 748, 20433, "1330793;1549977", 1, 0, 0, 0, 10000, 1u, 23732, 10800u, 32866u, 10800u, 0, 1, 17, 0, 10799, 0, 0u, "14,8,5,1,10,5,9,9,3,5,6,6,2,8,2,2,1,15" )]
	[DataRow( 900, 5526, "1141088;1539410;2441133", 1, 0, 0, 0, 10000, 1u, 8532, 10800u, 17666u, 10800u, 0, 0, 7, 1, 11941, 0, 0u, "14,8,5,1,10,5,4,15" )]
	[DataRow( 900, -1, "1141088", 1, 0, 0, 0, 10000, 3u, 3532, 10800u, 12666u, 10800u, 0, 0, 2, 1, 11941, 0, 0u, "14,1,15" )]
	[DataRow( 901, 5526, "1141088;1539410", 1, 0, 0, 0, 10000, 1u, 5430, 9800u, 13564u, 0u, 1, 0, 0, 1, 10941, 1, 13664u, "17,17,17,17,18,19,20" )]
	[DataRow( 902, 5526, "", 1, 0, 0, 0, 10000, 1u, 8532, 10800u, 17666u, 10800u, 0, 0, 7, 0, 10799, 0, 27800u, "14,8,5,1,10,5,4,15" )]
	[DataRow( 902, 2000, "", 1, 0, 0, 0, 3000, 4u, 5332, 3800u, 7466u, 3800u, 0, 0, 3, 0, 3799, 0, 20800u, "14,6,1,15" )]
	public void SpeechMatchesTheExecutable( int response, int voice, string marks, int advisorOn, int help, int speaking, int animationWord, int now, uint seed, int duration, uint baseTime,
		uint end, uint voiceDue, int voiceLive, int headHidden, int draws, int lipLoaded, int lipNext, int talking, uint timer, string clips )
	{
		var presentation = new ThemeParkIncAdvisorPresentation( new Speech( voice >= 0 ? new() { [response] = (uint)voice } : null,
			Marks( marks ) is { } lips ? new() { [response] = lips } : null ), seed );
		if ( speaking == 1 )
			presentation.Restore( presentation.Capture() with { Speaking = true, Response = 645 } );
		var said = presentation.Speak( Row( response ), (uint)now, animationWord == 1, advisorOn == 1, help == 1 );
		Assert.AreEqual( (uint)duration, said );
		Assert.AreEqual( draws, DrawsSince( seed, presentation.RandomState ) );
		if ( duration == 0 )
			return;
		Assert.AreEqual( (baseTime, end, voiceDue, voiceLive == 1, headHidden == 1, true),
			(presentation.Base, presentation.SpeechEnd, presentation.VoiceDue, presentation.VoiceLive, presentation.HeadHidden, presentation.Speaking) );
		Assert.AreEqual( (lipLoaded == 1, lipNext, talking == 1, timer, clips),
			(presentation.LipLoaded, presentation.LipNext, presentation.Talking, presentation.Timer, Joined( presentation.Clips )) );
	}

	[DataTestMethod]
	[DataRow( 1, 1, 0, 0, 1u, 1136, 1 )]
	[DataRow( 1, 1, 0, 0, 2u, 1134, 1 )]
	[DataRow( 1, 1, 0, 0, 3u, 1134, 1 )]
	[DataRow( 1, 0, 0, 0, 1u, 0, 0 )]
	[DataRow( 1, 1, 1, 0, 1u, 0, 0 )]
	[DataRow( 1, 1, 0, 1, 1u, 0, 0 )]
	[DataRow( 0, 1, 0, 0, 1u, 0, 0 )]
	public void StopMatchesTheExecutable( int speaking, int voiceLive, int headHidden, int headSuppressed, uint seed, int sound, int draws )
	{
		var presentation = new ThemeParkIncAdvisorPresentation( new Speech(), seed );
		presentation.Restore( presentation.Capture() with
		{
			Speaking = speaking == 1, Response = 645, VoiceLive = voiceLive == 1, HeadHidden = headHidden == 1, HeadSuppressed = headSuppressed == 1
		} );
		presentation.Stop();
		var stop = presentation.TakeCues().SingleOrDefault( cue => cue.Kind == ThemeParkIncAdvisorCueKind.Stop );
		Assert.AreEqual( ((uint)sound, draws, false), (stop.Sound, DrawsSince( seed, presentation.RandomState ), presentation.Speaking) );
	}

	[DataTestMethod]
	[DataRow( "1141088;1539410;2441133;2839455;5441950", 9800, 9800, 31, 200, 1u, 0, "2,2,2,2,3,3,3,3,5,5,5,5,1,1,1,1,5,5,5,5,5,5,5,5,4,4,4,4,4,4,4,4,3,3,3,3,5,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,2,2,2,2,3,3,3,3,2,2,2,2,2,2,2,2,1,1,1,1,3,1,1,1,1,1,1,1,1,1,1,1,1,1,3,3,3,3,2,2,2,2,2,2,2,2,5,5,5,5,3,3,3,3,4,4,4,4,3,3,3,3,3,3,3,3,2,2,2,2,2,2,2,2,4,4,4,4,1,1,1,1,3,3,3,3,2,2,2,2,2,2,2,2,4,4,4,4,5,5,5,5,3,3,3,3,3,3,3,3,5,5,5,5,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1", 0, 39 )]
	[DataRow( "1141088;1539410;2441133;2839455;5441950", 9800, 9800, 124, 60, 7u, 0, "2,3,1,1,2,2,3,5,3,5,1,1,1,4,5,3,2,3,1,4,1,1,1,5,3,3,1,1,1,3,5,4,2,2,1,4,4,1,5,4,1,2,5,3,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1", 0, 38 )]
	[DataRow( "0;400000;900000", 1000, 1000, 31, 40, 1u, 2000, "0,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1", 0, 0 )]
	[DataRow( "250000", 500, 400, 62, 12, 3u, 0, "4,4,2,2,5,5,1,1,1,1,1,1", 0, 3 )]
	public void FramesMatchTheExecutable( string marks, int baseTime, int start, int step, int frames, uint seed, int mouthNext, string mouths, int talking, int draws )
	{
		var lips = marks.Split( ';' ).Select( uint.Parse ).ToArray();
		var presentation = new ThemeParkIncAdvisorPresentation( new Speech( lips: new() { [645] = lips } ), seed );
		presentation.Restore( presentation.Capture() with
		{
			Speaking = true, Response = 645, Base = (uint)baseTime, SpeechEnd = int.MaxValue, LipLoaded = true, LipIndex = 1, LipNext = baseTime + (int)lips[0] / 1000, Talking = true,
			MouthNext = mouthNext
		} );
		var shown = new List<int>();
		for ( var frame = 0; frame < frames; frame++ )
		{
			presentation.Frame( (uint)(start + frame * step) );
			shown.Add( presentation.Mouth );
		}
		Assert.AreEqual( (mouths, talking == 1, draws), (Joined( shown ), presentation.Talking, DrawsSince( seed, presentation.RandomState )) );
	}

	[DataTestMethod]
	[DataRow( "", 1000, 1000, 31, 60, 1u, 0, 1, 1500, 500, 1300, 0, 0, "1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1", "end@17", 0, 0, 0 )]
	[DataRow( "", 1000, 1000, 31, 60, 1u, 0, 0, 0, 900, 1200, 1100, 1400, "1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1", "voice@4;timer@13;end@34", 1, 0, 0 )]
	[DataRow( "1141088;1539410;2441133", 800, 1000, 31, 120, 5u, 0, 0, 0, 3000, 2500, 1800, 0, "5,5,5,5,4,4,4,4,1,1,1,1,5,5,5,5,1,1,1,1,1,1,1,1,3,3,3,3,5,5,5,1,1,1,1,1,1,1,1,1,1,1,1,1,3,3,3,3,1,1,1,1,5,5,5,5,2,2,2,2,4,4,4,4,2,2,2,2,2,2,2,2,3,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1", "voice@26", 0, 1, 16 )]
	[DataRow( "", 1000, 1000, 31, 20, 1u, 0, 0, 0, -1, 1100, 1050, 0, "1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1", "voice@2;end@4", 1, 0, 0 )]
	[DataRow( "250000", 500, 400, 62, 30, 3u, 0, 1, 900, 0, 700, 0, 600, "4,4,2,2,5,5,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1", "timer@4;end@9", 0, 0, 3 )]
	[DataRow( "400000;900000", 2000, 2000, 124, 40, 9u, 0, 1, 5000, 0, 2600, 0, 2300, "4,2,1,4,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1", "timer@3;end@25", 0, 0, 4 )]
	public void FrameEventsMatchTheExecutable( string marks, int baseTime, int start, int step, int frames, uint seed, int mouthNext, int voiceLive, int voiceEnd, int voiceLength,
		int end, int voiceDue, int timer, string mouths, string events, int talking, int speaking, int draws )
	{
		var lips = Marks( marks );
		var presentation = new ThemeParkIncAdvisorPresentation( new Speech( voiceLength >= 0 ? new() { [645] = (uint)voiceLength } : null, lips != null ? new() { [645] = lips } : null ), seed );
		presentation.Restore( presentation.Capture() with
		{
			Speaking = true, Response = 645, Base = (uint)baseTime, SpeechEnd = (uint)end, VoiceDue = (uint)voiceDue, VoiceLive = voiceLive == 1, VoiceEnd = (uint)voiceEnd,
			Timer = (uint)timer, LipLoaded = lips != null, LipIndex = lips != null ? 1 : 0, LipNext = lips != null ? baseTime + (int)lips[0] / 1000 : 0, Talking = lips != null,
			MouthNext = mouthNext
		} );
		var shown = new List<int>();
		var seen = new List<string>();
		for ( var frame = 0; frame < frames; frame++ )
		{
			var wasSpeaking = presentation.Speaking;
			presentation.Frame( (uint)(start + frame * step) );
			foreach ( var cue in presentation.TakeCues() )
				seen.Add( $"{(cue.Kind == ThemeParkIncAdvisorCueKind.Voice ? "voice" : "timer")}@{frame}" );
			if ( wasSpeaking && !presentation.Speaking )
				seen.Add( $"end@{frame}" );
			shown.Add( presentation.Mouth );
		}
		Assert.AreEqual( (mouths, events, talking == 1, speaking == 1, draws),
			(Joined( shown ), string.Join( ";", seen ), presentation.Talking, presentation.Speaking, DrawsSince( seed, presentation.RandomState )) );
	}

	private const int Rain = 0x157, Heatwave = 0x154;

	private static ThemeParkIncCareer Career() => new( new TestCareerFiles(), TestCareerFiles.ShippedFiringLines );

	private static ThemeParkIncAdvisor Advisor( IThemeParkIncAdvisorSpeechData speech, uint seed = 1 ) =>
		new( ThemeParkIncAdvisorSettings.Bind( System.Text.Encoding.ASCII.GetBytes( "GeneralAdvisor.MinScoreForConsideration 10\n" ) ), ThemeParkIncAdvisorTable.Shipped, speech, seed );

	[TestMethod]
	public void TheDefaultSessionSeedStartsTheStreamWhereTheRuntimeStartsRand()
	{
		Assert.AreEqual( 1u, ParkEconomy.PresentationSeed( WorldSeed.Default.EconomyStream ) );
		Assert.AreNotEqual( 1u, ParkEconomy.PresentationSeed( new WorldSeed( 42 ).EconomyStream ) );
	}

	[TestMethod]
	public void TheClipTableKeepsTwentyEntriesForThemeParkWorld()
	{
		var words = Enumerable.Repeat( 30u, 25 ).ToArray();
		Assert.AreEqual( (20, 0u), (new AdvisorClipDurations( words ).Clips, new AdvisorClipDurations( words ).Of( 21 )) );
		Assert.AreEqual( (25, 1000u, 0u), (Durations().Clips, new AdvisorClipDurations( words, 25 ).Of( 25 ), Durations().Of( AdvisorClipDurations.StartClip )) );
	}

	[TestMethod]
	public void ACancelledSpeechEndsWithAStopSoundFromThePresentationStream()
	{
		var career = Career();
		var advisor = Advisor( new Speech( new() { [645] = 5_526 } ) );
		Assert.IsTrue( advisor.Post( Rain, career, 0, default ) );
		advisor.AdvanceClock( 1 );
		advisor.Update( 1, career, default );
		Assert.IsTrue( advisor.Presentation.Speaking, "the forecast speaks" );
		Assert.AreEqual( 1u, advisor.Presentation.RandomState, "a canned row with clips draws nothing, and there is no LIP to talk" );
		Assert.IsTrue( advisor.Cancel( Rain, career, out var stopped ) && stopped );
		var stop = advisor.Presentation.TakeCues().Single( cue => cue.Kind == ThemeParkIncAdvisorCueKind.Stop );
		var first = new AdvisorRandom( 1 ).Next();
		Assert.AreEqual( ThemeParkIncAdvisorPresentation.FirstStopSound + (uint)(first % 3), stop.Sound );
		Assert.IsFalse( advisor.Presentation.Speaking );
	}

	[TestMethod]
	public void WithTheAdvisorOffOnlyTheHelpButtonsResponseSpeaks()
	{
		var career = Career();
		var advisor = Advisor( new Speech( new() { [645] = 5_526 } ) );
		advisor.AdvisorOn = false;
		Assert.IsTrue( advisor.Post( Rain, career, 0, default ) );
		advisor.AdvanceClock( 1 );
		advisor.Update( 1, career, default );
		Assert.AreEqual( (1_000u, false), (advisor.LastWindow, advisor.Presentation.Speaking), "the speech refuses: the window is the update's 1000 ms" );
		Assert.AreEqual( 1, career.AdviceHistory( Rain ).Plays, "the play still counts" );
		advisor.FromHelpButton = true;
		Assert.IsTrue( advisor.Post( Heatwave, career, 0, default ) );
		advisor.FromHelpButton = false;
		for ( var turn = 2; turn < 8; turn++ )
		{
			advisor.AdvanceClock( 1 );
			advisor.Update( turn, career, default );
		}
		Assert.IsTrue( advisor.Presentation.Speaking, "the help button's response is said with the advisor off" );
		Assert.AreEqual( 642, advisor.Presentation.Response );
		Assert.AreEqual( ThemeParkIncAdvisor.NoResponse, advisor.HelpResponse, "the speech used the help response up" );
	}

	[TestMethod]
	public void AStateFromBeforeThePresentationRestartsItSilentFromTheSessionSeed()
	{
		var advisor = Advisor( new Speech( new() { [645] = 5_526 } ), seed: 77 );
		var career = Career();
		Assert.IsTrue( advisor.Post( Rain, career, 0, default ) );
		advisor.AdvanceClock( 1 );
		advisor.Update( 1, career, default );
		var older = advisor.Capture() with { PresentationRandom = null, AdvisorOn = true };
		advisor.AdvisorOn = false;
		advisor.Restore( older );
		Assert.AreEqual( (false, 77u, true), (advisor.Presentation.Speaking, advisor.Presentation.RandomState, advisor.AdvisorOn) );
		Assert.AreEqual( older.LastWindow, advisor.LastWindow, "the window the older model set stays" );
		// the save's own options (an unknown member is refused, enums are strings), as a P1 save's TpiAdvisor is read
		var json = System.Text.Json.JsonSerializer.Serialize( advisor.Capture(), ParkSaveFile.Options );
		StringAssert.Contains( json, "\"PresentationRandom\"" );
		var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		node.Remove( "PresentationRandom" );
		node.Remove( "AdvisorOn" );
		var parsed = System.Text.Json.JsonSerializer.Deserialize<ThemeParkIncAdvisorSnapshot>( node.ToJsonString(), ParkSaveFile.Options )!;
		Assert.AreEqual( (null, true), (parsed.PresentationRandom, parsed.AdvisorOn), "the members are optional" );
	}

	[TestMethod]
	public void TheForecastsCaptionIsAnUnreadMessageCentreRow()
	{
		var economy = ScienceEconomy();
		var tube = economy.TpiTagTube!;
		var unread = tube.Unread;
		Assert.IsTrue( economy.PostThemeParkIncAdvice( Rain ) );
		economy.Advance( ParkCalendar.TickOfTurn( economy.Turn + 1 ) - economy.Tick );
		var row = tube.Listed.First();
		Assert.AreEqual( (ThemeParkIncLetterSource.Advice, 65, 0x97, false, ThemeParkIncAdviceTag.SenderPicture, 0, 0, 0),
			(row.Source, row.Tag, row.Kind, row.Read, row.Picture, row.Category, row.ExpiryUnits, row.GotoKind) );
		Assert.AreEqual( unread + 1, tube.Unread );
		Assert.IsTrue( ThemeParkIncTagTube.IsValid( tube.Capture() ) );
	}

	[TestMethod]
	public void ASecondForecastWhileTheFirstVoiceStillPlaysIsNotSaid()
	{
		var economy = ScienceEconomy();
		economy.Advance( ParkCalendar.TickOfTurn( 8 ) - economy.Tick );
		Assert.IsTrue( economy.PostThemeParkIncAdvice( Rain ) );
		Assert.IsTrue( economy.PostThemeParkIncAdvice( Heatwave ) );
		var advisor = economy.TpiAdvisor!;
		var start = economy.Turn + 1;
		economy.Advance( ParkCalendar.TickOfTurn( start ) - economy.Tick );
		Assert.AreEqual( (645, 5_223u), (advisor.Presentation.Response, advisor.LastWindow), "the mail-free queue plays the first highest score, the rain" );
		// the window (5223 ms) ends at the 22nd tick (5456 ms), before the rain voice (5526 ms): the heatwave's speech is refused and its window is the update's 1000 ms
		economy.Advance( ParkCalendar.TickOfTurn( start + 22 ) - economy.Tick );
		Assert.AreEqual( (Heatwave, 1_000u, 645), (advisor.Playing, advisor.LastWindow, advisor.Presentation.Response) );
		Assert.IsTrue( advisor.Presentation.Speaking, "the rain voice still plays" );
		economy.Advance( ParkCalendar.TickOfTurn( start + 23 ) - economy.Tick );
		Assert.IsFalse( advisor.Presentation.Speaking, "the voice ended after 5526 ms" );
	}

	[TestMethod]
	public void ALoadDuringASpeechStartsSilentWithTheStreamGoingOn()
	{
		var economy = ScienceEconomy();
		economy.Advance( ParkCalendar.TickOfTurn( 8 ) - economy.Tick );
		Assert.IsTrue( economy.PostThemeParkIncAdvice( Rain ) );
		economy.Advance( ParkCalendar.TickOfTurn( economy.Turn + 6 ) - economy.Tick );
		var advisor = economy.TpiAdvisor!;
		Assert.IsTrue( advisor.Presentation is { Speaking: true, LipLoaded: true } );
		var stream = advisor.Presentation.RandomState;
		Assert.AreNotEqual( 1u, stream, "the talking mouth drew from the stream" );
		var loaded = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( economy ) ) ), economy.Settings, economy.Catalog );
		// 0x6A36DE: a load runs the advisor init 0x715850 before the park file, so the speech, the voice, the LIP and the mouth start as it leaves them
		Assert.IsTrue( loaded.TpiAdvisor!.Presentation is { Speaking: false, VoiceLive: false, VoiceDue: 0, LipLoaded: false, Talking: false, Mouth: 0, MouthNext: 0, Timer: 0 } );
		Assert.AreEqual( stream, loaded.TpiAdvisor.Presentation.RandomState, "the presentation stream goes on from its saved state" );
		Assert.AreEqual( (advisor.LastStart, advisor.LastWindow), (loaded.TpiAdvisor.LastStart, loaded.TpiAdvisor.LastWindow), "the queue's window is the advisor thing's, which the save keeps" );
		loaded.Advance( ParkCalendar.TickOfTurn( loaded.Turn + 30 ) - loaded.Tick );
		Assert.AreEqual( stream, loaded.TpiAdvisor.Presentation.RandomState, "a silent advisor draws nothing" );
	}
}
