using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The advisor's generated animation sequences and the audio start of a response (ADVISOR-006/008/016).</summary>
[TestClass]
public class AdvisorSequenceTests
{
	/// <summary>Clip n (1–20) lasts <paramref name="milliseconds"/>[n − 1]; clip 14 is forced to 0 by the table.</summary>
	private static AdvisorClipDurations Durations( params uint[] milliseconds ) =>
		new( milliseconds.Select( value => (value * 30 + 999) / 1000 ).ToArray() );

	private static AdvisorClipDurations Uniform( uint milliseconds ) => Durations( Enumerable.Repeat( milliseconds, 20 ).ToArray() );

	private static Func<int> Script( params int[] values )
	{
		var index = 0;
		return () => values[index++];
	}

	[TestMethod]
	public void TheSequenceStreamIsTheCRuntimesRandFromSeedOne()
	{
		var random = new AdvisorRandom();
		CollectionAssert.AreEqual( new[] { 41, 18467, 6334, 26500, 19169 }, Enumerable.Range( 0, 5 ).Select( _ => random.Next() ).ToArray() );
	}

	[TestMethod]
	public void AClipLastsItsTrailerWord2TimesThousandOverThirtyAsUnsignedInteger()
	{
		Assert.AreEqual( 11000u, AdvisorClipDurations.FromTrailer( 330 ) );
		Assert.AreEqual( 33u, AdvisorClipDurations.FromTrailer( 1 ) );
		// 4294967295 * 1000 wraps to 4294966296 in 32 bits; / 30 = 143165543.
		Assert.AreEqual( 143165543u, AdvisorClipDurations.FromTrailer( uint.MaxValue ) );
		var durations = new AdvisorClipDurations( Enumerable.Repeat( 30u, 25 ).ToArray() );
		Assert.AreEqual( 1000u, durations.Of( 1 ) );
		Assert.AreEqual( 0u, durations.Of( AdvisorClipDurations.StartClip ), "the entry of clip 14 is zeroed" );
		Assert.AreEqual( 1000u, durations.Of( 20 ) );
		Assert.AreEqual( 0u, durations.Of( 21 ), "outside the table" );
		Assert.AreEqual( 0u, durations.Of( 0 ) );
	}

	[TestMethod]
	public void AGeneratedSequenceOpensWithClip14ClosesWithClip15AndSpendsItsBudget()
	{
		var durations = Uniform( 1000 );
		// Budget 2500: clip 14 (0 ms, the first roll is ignored), then roll 4 -> clip 5 (1000), roll 2 -> clip 3 (1000), budget 500, roll 6 -> clip 7 (1000 > 500) is replaced by the first clip at least 500 ms long: clip 1.
		var rolls = new List<int>();
		var sequence = AdvisorSequences.Generate( 2500, durations, () => { var roll = new[] { 9, 4, 2, 6 }[rolls.Count]; rolls.Add( roll ); return roll; } );
		CollectionAssert.AreEqual( new[] { 14, 5, 3, 1, 15 }, sequence.Clips.ToArray() );
		Assert.AreEqual( 1000 * 3 + 1000, sequence.SumMilliseconds, "clips 5, 3 and 1 plus the closing clip 15; clip 14 counts 0" );
		Assert.AreEqual( 4, rolls.Count, "one draw per clip choice, none for the closing clip" );
	}

	[TestMethod]
	public void ALongDrawnClipIsReplacedByTheFirstClipOfOneToTenThatCoversTheBudget()
	{
		var milliseconds = Enumerable.Repeat( 100u, 20 ).ToArray();
		milliseconds[3] = 400;  // clip 4
		milliseconds[6] = 900;  // clip 7
		var durations = Durations( milliseconds );
		// Budget 350 after clip 14: roll 6 -> clip 7 (900 > 350) -> the first of clips 1-10 with 350 <= length is clip 4 (400), which overshoots and ends the sequence.
		CollectionAssert.AreEqual( new[] { 14, 4, 15 }, AdvisorSequences.Generate( 350, durations, Script( 0, 6 ) ).Clips.ToArray() );
		// Budget 1000: the drawn clip 7 (900) fits and is used as drawn, then clip 1 (100) spends the rest.
		CollectionAssert.AreEqual( new[] { 14, 7, 1, 15 }, AdvisorSequences.Generate( 1000, durations, Script( 0, 6, 0 ) ).Clips.ToArray() );
		// Equality is not "longer": a drawn clip as long as the remaining budget is used as drawn.
		CollectionAssert.AreEqual( new[] { 14, 7, 15 }, AdvisorSequences.Generate( 900, durations, Script( 0, 6 ) ).Clips.ToArray() );
	}

	[TestMethod]
	public void ASequenceHoldsAtMostNineteenClipsBeforeClipFifteen()
	{
		var sequence = AdvisorSequences.Generate( 1_000_000, Uniform( 1 ), () => 3 );
		Assert.AreEqual( 20, sequence.Clips.Count );
		Assert.AreEqual( 14, sequence.Clips[0] );
		Assert.IsTrue( sequence.Clips.Skip( 1 ).Take( 18 ).All( clip => clip == 4 ) );
		Assert.AreEqual( 15, sequence.Clips[19] );
		// No budget: clip 14 would be first, but the loop ends before it at once.
		CollectionAssert.AreEqual( new[] { 15 }, AdvisorSequences.Generate( 0, Uniform( 1000 ), () => 0 ).Clips.ToArray() );
	}

	[TestMethod]
	public void TheResponseSpanCountsTheClosingClipTwice()
	{
		var durations = Uniform( 600 );
		var sequence = AdvisorSequences.Generate( 2000, durations, Script( 0, 0, 1, 2, 3 ) );
		Assert.AreEqual( 15, sequence.Clips[^1] );
		var clipsBeforeTheEnd = sequence.Clips.Take( sequence.Clips.Count - 1 ).Sum( clip => (int)durations.Of( clip ) );
		Assert.AreEqual( clipsBeforeTheEnd + 600, sequence.SumMilliseconds );
		Assert.AreEqual( (uint)(sequence.SumMilliseconds + 1000 + 600), AdvisorSequences.ResponseSpan( sequence, durations ) );
	}

	[TestMethod]
	public void OnlySequences16And17And29StartTheirAudioAtOnce()
	{
		Assert.IsTrue( new[] { 16, 17, 29 }.All( AdvisorSequences.StartsImmediately ) );
		Assert.IsFalse( new[] { -1, 21, 22, 23, 24, 25, 26, 27, 28 }.Any( AdvisorSequences.StartsImmediately ) );
	}

	[TestMethod]
	public void TheTenFixedRowsAreImported()
	{
		var rows = Enumerable.Range( 16, 14 ).Where( sequence => AdvisorFixedSequences.Row( sequence ).Count > 0 ).ToArray();
		CollectionAssert.AreEqual( new[] { 16, 17, 21, 22, 23, 25, 26, 27, 28, 29 }, rows, "rows 18-20 and 24 are empty and unused" );
		CollectionAssert.AreEqual( new[] { 14, 11, 1, 12, 1, 15 }, AdvisorFixedSequences.Row( 22 ).Select( entry => entry.Clip ).ToArray() );
		CollectionAssert.AreEqual( new[] { 17, 17, 17, 17, 18, 19, 20 }, AdvisorFixedSequences.Row( 17 ).Select( entry => entry.Clip ).ToArray() );
		CollectionAssert.AreEqual( new[] { 2, 2, 2, 2, 4, 9, 0x11 }, AdvisorFixedSequences.Row( 17 ).Select( entry => entry.Bits ).ToArray(), "the accessory bits (entry >> 16 & 0x7F)" );
		Assert.AreEqual( 0, AdvisorFixedSequences.Row( -1 ).Count, "a generated response has no row" );
		var timers = AdvisorFixedSequences.Row( 29 ).Where( entry => entry.IsTimer ).ToArray();
		Assert.AreEqual( 1, timers.Length );
		Assert.AreEqual( 1000, timers[0].TimerMilliseconds );
		Assert.AreEqual( 17000, AdvisorFixedSequences.Row( 28 ).Single().TimerMilliseconds );
	}

	[TestMethod]
	public void AFixedSequenceSumsItsClipsAndTheSpanAddsOneSecondAndClip15()
	{
		var milliseconds = Enumerable.Repeat( 100u, 20 ).ToArray();
		milliseconds[4] = 700;   // clip 5
		milliseconds[10] = 300;  // clip 11
		milliseconds[14] = 600;  // clip 15
		var durations = Durations( milliseconds );
		// Row 21: clip 14 (0), clip 5, clip 11, clip 15.
		var sequence = AdvisorSequences.Build( 21, 5000, durations, () => throw new InvalidOperationException( "a fixed row draws nothing" ) );
		CollectionAssert.AreEqual( new[] { 14, 5, 11, 15 }, sequence.Clips.ToArray() );
		Assert.AreEqual( 700 + 300 + 600, sequence.SumMilliseconds, "the sum is when the last clip ends" );
		Assert.AreEqual( (uint)(1600 + 1000 + 600), AdvisorSequences.ResponseSpan( sequence, durations ) );
		Assert.IsNull( sequence.TimerMilliseconds );
	}

	[TestMethod]
	public void ATimerEntrySetsTheTimerFromTheClipSumSoFarAndAddsNoClip()
	{
		var durations = Uniform( 200 );
		var sequence = AdvisorSequences.Build( 29, 5000, durations, () => 0 );
		CollectionAssert.AreEqual( new[] { 17, 17, 17, 17, 18, 19, 20 }, sequence.Clips.ToArray() );
		Assert.AreEqual( 7 * 200, sequence.SumMilliseconds, "the timer's milliseconds are not part of the sum" );
		Assert.AreEqual( 6 * 200 + 1000, sequence.TimerMilliseconds, "the clips before the timer plus its 1000 ms" );
	}

	[TestMethod]
	public void ASequenceWhoseClipsSumToZeroFallsBackToTheGeneratorKeepingItsTimer()
	{
		// Row 28 is a timer only; clips missing from the archives (length 0) do the same for any row.
		var generated = AdvisorSequences.Generate( 1500, Uniform( 500 ), Script( 0, 0, 1, 2, 3 ) );
		var sequence = AdvisorSequences.Build( 28, 1500, Uniform( 500 ), Script( 0, 0, 1, 2, 3 ) );
		CollectionAssert.AreEqual( generated.Clips.ToArray(), sequence.Clips.ToArray() );
		Assert.AreEqual( generated.SumMilliseconds, sequence.SumMilliseconds );
		Assert.AreEqual( 17000, sequence.TimerMilliseconds );
		var missing = AdvisorSequences.Build( 16, 1500, Durations( new uint[20] ), () => 0 );
		Assert.AreEqual( 15, missing.Clips[^1], "row 16 holds only clip 16; with no length it is generated (which, with no lengths at all, still ends with clip 15)" );
		CollectionAssert.AreEqual( AdvisorSequences.Generate( 1500, Durations( new uint[20] ), () => 0 ).Clips.ToArray(), missing.Clips.ToArray() );
	}

	[TestMethod]
	public void AGeneratedResponseHasNoRow()
	{
		var generated = AdvisorSequences.Generate( 2500, Uniform( 1000 ), Script( 9, 4, 2, 6 ) );
		var built = AdvisorSequences.Build( -1, 2500, Uniform( 1000 ), Script( 9, 4, 2, 6 ) );
		CollectionAssert.AreEqual( generated.Clips.ToArray(), built.Clips.ToArray() );
	}

	[TestMethod]
	public void AnImmediateStartHasTheLipClockTwoHundredMillisecondsBeforeTheRequest()
	{
		var start = AdvisorSpeechStart.For( 5000, 16 );
		Assert.IsFalse( start.Delayed );
		Assert.AreEqual( 5000, start.AudioStartMilliseconds );
		Assert.AreEqual( 4800, start.LipBaseMilliseconds );
		Assert.AreEqual( 200, start.LipLeadOverAudioMilliseconds );
		Assert.IsTrue( start.AudioDue( 5000 ) );
	}

	[TestMethod]
	public void ADelayedStartRestartsTheAudioEightHundredMillisecondsLaterWithEqualityCounting()
	{
		var start = AdvisorSpeechStart.For( 5000, -1 );
		Assert.IsTrue( start.Delayed );
		Assert.AreEqual( 5800, start.AudioStartMilliseconds );
		Assert.AreEqual( 5800, start.LipBaseMilliseconds, "the LIP base moves to the restart" );
		Assert.AreEqual( 0, start.LipLeadOverAudioMilliseconds );
		Assert.IsFalse( start.AudioDue( 5799 ) );
		Assert.IsTrue( start.AudioDue( 5800 ) );
		Assert.IsTrue( start.AudioDue( 6000 ) );
	}

	[TestMethod]
	public void TheDeadlinesSurviveTheWrapOfTheThirtyTwoBitAdvisorClock()
	{
		long request = uint.MaxValue - 300; // 4294966995; 800 ms later the clock reads 499 after the wrap
		var delayed = AdvisorSpeechStart.For( request, -1 );
		Assert.IsFalse( delayed.AudioDue( request + 799 - (1L << 32) ) );
		Assert.IsFalse( delayed.AudioDue( uint.MaxValue ) );
		Assert.IsTrue( delayed.AudioDue( 499 ), "800 ms after the request, past the wrap" );
		Assert.IsTrue( delayed.AudioDue( 1000 ) );
		Assert.IsTrue( AdvisorSpeechStart.For( request, 16 ).AudioDue( request ) );
		Assert.IsTrue( delayed.Within( 499, 2000 ) );
		Assert.IsFalse( delayed.Within( 1800, 2000 ) );
		// The LIP clock runs on the advisor clock minus its base, also across the wrap: 1000 ms after the restart.
		Assert.AreEqual( 1000, delayed.LipMilliseconds( 1499 ) );
		Assert.AreEqual( 200 + 50, AdvisorSpeechStart.For( 100, 16 ).LipMilliseconds( 150 ) );
	}

	[TestMethod]
	public void ALateFirstFrameStillStartsTheMouthAtTheElapsedAdvisorTime()
	{
		var start = AdvisorSpeechStart.For( 5000, -1 );
		Assert.AreEqual( 0, start.LipMilliseconds( 5800 ) );
		Assert.AreEqual( 120, start.LipMilliseconds( 5920 ), "a frame 120 ms after the deadline reads 120, not 0" );
	}
}
