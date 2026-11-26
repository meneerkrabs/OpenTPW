using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's advisor queue (Game.exe 0x718190, 0x718820, 0x718B40, 0x718990, 0x718E90, 0x718F80; docs/reverse/TPI-ADVISOR.md). The acceptance rows are checked against the
/// executable by tools/ppc-analysis/oracle_x86/tpi_advisor_queue_oracle.py --check, and the queue scenarios below repeat the oracle's.
/// </summary>
[TestClass]
public sealed class ThemeParkIncAdvisorTests : ThemeParkIncScienceTestBase
{
	private const int Rain = 0x157, Heatwave = 0x154, Snow = 0x15A, RedMail = 0x1A2, LostMail = 0x1A3, Tutorial = 0x271;

	/// <summary>The shipped table with the rain row's group, limit, rule and count replaced, as the oracle patches the native row.</summary>
	private static ThemeParkIncAdvisorTable Patched( int group = 0, int limit = 255, int rule = 2, int count = 1 )
	{
		var text = File.ReadAllText( Path.Combine( AppContext.BaseDirectory, ThemeParkIncAdvisorTable.RelativePath ) );
		var rain = new Regex( @"(message = 0x157, )group = \d+(, class = 2, callback = 0x[0-9a-f]+, )duplicates = \d+(, response = 645, )count = \d+, variant = \d+" );
		Assert.IsTrue( rain.IsMatch( text ) );
		return ThemeParkIncAdvisorTable.Parse( rain.Replace( text, $"${1}group = {group}${2}duplicates = {limit}${3}count = {count}, variant = {rule}", 1 ) );
	}

	private static ThemeParkIncAdvisorSettings Settings( int group = 0, uint first = 0, uint second = 0, uint third = 0, uint later = 0, bool once = false, int slaps = 0, int minimum = 10 ) =>
		ThemeParkIncAdvisorSettings.Reset with
		{
			MinimumScore = minimum,
			Groups = Enumerable.Range( 0, ThemeParkIncAdvisorSettings.GroupCount )
				.Select( index => index == group ? new ThemeParkIncAdvisorGroup( first, second, third, later, once, slaps ) : default ).ToArray()
		};

	private static ThemeParkIncCareer Career() => new( new TestCareerFiles(), TestCareerFiles.ShippedFiringLines );

	private static ThemeParkIncAdviceRecord Record( int message, int score ) => new( 0, 0, -1, message, score, true, false, true );

	/// <summary>Updates the advisor at <paramref name="turn"/> with its clock where that many normal-speed ticks put it (248 ms each).</summary>
	private static void UpdateAt( ThemeParkIncAdvisor advisor, long turn, ThemeParkIncCareer career, ThemeParkIncAdviceContext context, Func<int, uint> speech )
	{
		At( advisor, turn );
		advisor.Update( turn, career, context, speech );
	}

	private static void At( ThemeParkIncAdvisor advisor, long turn ) => advisor.Restore( advisor.Capture() with { ClockMicroseconds = turn * 248_000 } );

	/// <summary>Puts records into the slots from the first one on, as the oracle writes them.</summary>
	private static void Fill( ThemeParkIncAdvisor advisor, params ThemeParkIncAdviceRecord[] records )
	{
		var state = advisor.Capture();
		var slots = new ThemeParkIncAdviceRecord[ThemeParkIncAdvisor.Slots];
		records.CopyTo( slots, 0 );
		advisor.Restore( state with { Slots = slots } );
	}

	[DataTestMethod]
	[DataRow( 0, 3, 20, 40, 60, 0, 0, 400, -1, 0, 0, 0, 411, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 0, 3, 20, 40, 60, 0, 0, 400, -1, 0, 0, 0, 415, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 0, 3, 20, 40, 60, 0, 0, 400, -1, 0, 0, 1, 479, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 0, 3, 20, 40, 60, 0, 0, 400, -1, 0, 0, 1, 483, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 0, 3, 20, 40, 60, 0, 0, 400, -1, 0, 0, 2, 559, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 0, 3, 20, 40, 60, 0, 0, 400, -1, 0, 0, 2, 563, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 0, 3, 20, 40, 60, 0, 0, 400, -1, 0, 0, 3, 639, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 0, 3, 20, 40, 60, 0, 0, 400, -1, 0, 0, 3, 643, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 0, 3, 20, 40, 60, 0, 0, 400, -1, 0, 0, 7, 639, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 0, 3, 20, 40, 60, 0, 0, 400, -1, 0, 0, 7, 643, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 1, 4, 21, 41, 61, 0, 0, 400, -1, 0, 0, 0, 415, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 1, 4, 21, 41, 61, 0, 0, 400, -1, 0, 0, 0, 419, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 1, 4, 21, 41, 61, 0, 0, 400, -1, 0, 0, 1, 483, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 1, 4, 21, 41, 61, 0, 0, 400, -1, 0, 0, 1, 487, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 1, 4, 21, 41, 61, 0, 0, 400, -1, 0, 0, 2, 563, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 1, 4, 21, 41, 61, 0, 0, 400, -1, 0, 0, 2, 567, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 1, 4, 21, 41, 61, 0, 0, 400, -1, 0, 0, 3, 643, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 1, 4, 21, 41, 61, 0, 0, 400, -1, 0, 0, 3, 647, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 1, 4, 21, 41, 61, 0, 0, 400, -1, 0, 0, 7, 643, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 1, 4, 21, 41, 61, 0, 0, 400, -1, 0, 0, 7, 647, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 5, 8, 25, 45, 65, 0, 0, 400, -1, 0, 0, 0, 431, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 5, 8, 25, 45, 65, 0, 0, 400, -1, 0, 0, 0, 435, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 5, 8, 25, 45, 65, 0, 0, 400, -1, 0, 0, 1, 499, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 5, 8, 25, 45, 65, 0, 0, 400, -1, 0, 0, 1, 503, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 5, 8, 25, 45, 65, 0, 0, 400, -1, 0, 0, 2, 579, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 5, 8, 25, 45, 65, 0, 0, 400, -1, 0, 0, 2, 583, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 5, 8, 25, 45, 65, 0, 0, 400, -1, 0, 0, 3, 659, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 5, 8, 25, 45, 65, 0, 0, 400, -1, 0, 0, 3, 663, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 5, 8, 25, 45, 65, 0, 0, 400, -1, 0, 0, 7, 659, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 5, 8, 25, 45, 65, 0, 0, 400, -1, 0, 0, 7, 663, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 8, 11, 28, 48, 68, 0, 0, 400, -1, 0, 0, 0, 443, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 8, 11, 28, 48, 68, 0, 0, 400, -1, 0, 0, 0, 447, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 8, 11, 28, 48, 68, 0, 0, 400, -1, 0, 0, 1, 511, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 8, 11, 28, 48, 68, 0, 0, 400, -1, 0, 0, 1, 515, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 8, 11, 28, 48, 68, 0, 0, 400, -1, 0, 0, 2, 591, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 8, 11, 28, 48, 68, 0, 0, 400, -1, 0, 0, 2, 595, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 8, 11, 28, 48, 68, 0, 0, 400, -1, 0, 0, 3, 671, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 8, 11, 28, 48, 68, 0, 0, 400, -1, 0, 0, 3, 675, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 8, 11, 28, 48, 68, 0, 0, 400, -1, 0, 0, 7, 671, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 8, 11, 28, 48, 68, 0, 0, 400, -1, 0, 0, 7, 675, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 9, 12, 29, 49, 69, 0, 0, 400, -1, 0, 0, 0, 447, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 9, 12, 29, 49, 69, 0, 0, 400, -1, 0, 0, 0, 451, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 9, 12, 29, 49, 69, 0, 0, 400, -1, 0, 0, 1, 515, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 9, 12, 29, 49, 69, 0, 0, 400, -1, 0, 0, 1, 519, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 9, 12, 29, 49, 69, 0, 0, 400, -1, 0, 0, 2, 595, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 9, 12, 29, 49, 69, 0, 0, 400, -1, 0, 0, 2, 599, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 9, 12, 29, 49, 69, 0, 0, 400, -1, 0, 0, 3, 675, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 9, 12, 29, 49, 69, 0, 0, 400, -1, 0, 0, 3, 679, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 9, 12, 29, 49, 69, 0, 0, 400, -1, 0, 0, 7, 675, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 9, 12, 29, 49, 69, 0, 0, 400, -1, 0, 0, 7, 679, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 0, 50, 50, 50, 50, 0, 0, 3, -1, 0, 0, 0, 3, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 0, 50, 50, 50, 50, 0, 0, 4, -1, 0, 0, 0, 4, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 0, 50, 50, 50, 50, 0, 0, -16, -1, 0, 0, 0, 196, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 0, 50, 50, 50, 50, 0, 0, -16, -1, 0, 0, 0, 192, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 0, 5, 5, 5, 5, 0, 0, 8, -1, 0, 0, -1, 24, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 1, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 1, 0, 0, 0, 0, 1, 0, 0, -1, 1, 0, 0, 0, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 1, 0, 0, 0, 0, 1, 0, 0, -1, 1, 0, 0, 0, 1, 1, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 1, 0, 0, 0, 0, 1, 0, 0, -1, 1, 0, 0, 0, 1, 0, 1, 0, 255, 2, 1, 1 )]
	[DataRow( 3, 0, 0, 0, 0, 0, 2, 0, -1, 0, 1, 0, 0, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 3, 0, 0, 0, 0, 0, 2, 0, -1, 0, 2, 0, 0, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 3, 0, 0, 0, 0, 0, 2, 0, -1, 0, 3, 0, 0, 1, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 3, 0, 0, 0, 0, 0, 0, 0, -1, 0, 9, 0, 0, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 3, 0, 0, 0, 0, 0, 2, 0, -1, 0, 2, 0, 0, 1, 1, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 3, 0, 0, 0, 0, 0, 2, 0, -1, 0, -1, 0, 0, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 1, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 255, 2, 1, 0 )]
	[DataRow( 1, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 255, 2, 1, 1 )]
	[DataRow( 1, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 1, 1, 0, 255, 2, 1, 0 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 1, 2, 1, 1 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 1, 1, 2, 1, 0 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 2, 3, 2, 1, 1 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 3, 3, 2, 1, 0 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 2, 1, 0 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 1, 1, 1, 2, 1, 0 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 1, 0, 0, 0, 255, 0, 3, 0 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 255, 0, 3, 1 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 1, 0, 1, 0, 255, 0, 3, 1 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 1, 1, 0, 0, 255, 0, 3, 0 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 1, 0, 0, 0, 255, 2, 3, 1 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 1, 0, 0, 0, 255, 1, 3, 1 )]
	[DataRow( 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 255, 0, 0, 0 )]
	public void TheAcceptanceFollowsTheExecutable( int group, int first, int second, int third, int later, int once, int slapLimit, int time, int variant, int played, int slaps, int plays,
		int now, int tutorialOn, int bypass, int helpButton, int pending, int limit, int rule, int count, int expected )
	{
		var advisor = new ThemeParkIncAdvisor( Settings( group, (uint)first, (uint)second, (uint)third, (uint)later, once != 0, slapLimit ), Patched( group, limit, rule, count ) )
		{
			TutorialOn = tutorialOn != 0,
			FromHelpButton = helpButton != 0
		};
		Fill( advisor, Enumerable.Repeat( Record( Rain, 10_000 ), pending ).ToArray() );
		var career = Career();
		career.SetAdviceHistory( Rain, new ThemeParkIncAdviceHistory( unchecked( (uint)time ), variant, played != 0, slaps, plays ) );
		Assert.AreEqual( expected != 0, advisor.Accepts( Rain, bypass != 0, career, unchecked( (uint)now ) ) );
	}

	[TestMethod]
	public void AFullQueueReplacesTheFirstLowestScoreButNeverMail()
	{
		var career = Career();
		var advisor = new ThemeParkIncAdvisor( Settings(), Patched() );
		Fill( advisor, Enumerable.Repeat( Record( RedMail, 10_000 ), 8 ).ToArray() );
		Assert.IsFalse( advisor.PostScored( Rain, 20_000, career, 0 ), "a queue full of mail refuses" );
		Fill( advisor, Record( RedMail, 1 ), Record( Rain, 50 ), Record( Rain, 30 ), Record( Rain, 30 ), Record( Rain, 70 ), Record( Rain, 90 ), Record( LostMail, 2 ), Record( Rain, 40 ) );
		Assert.IsTrue( advisor.PostScored( Rain, 30, career, 0 ), "an equal score reports success" );
		CollectionAssert.AreEqual( new[] { 1, 50, 30, 30, 70, 90, 2, 40 }, advisor.Queue.Select( slot => slot.Score ).ToArray() );
		Assert.IsTrue( advisor.PostScored( Rain, 31, career, 0 ) );
		CollectionAssert.AreEqual( new[] { 1, 50, 31, 30, 70, 90, 2, 40 }, advisor.Queue.Select( slot => slot.Score ).ToArray(), "the first lowest slot that is not mail" );
	}

	[TestMethod]
	public void TheSelectionTakesTheLastPositiveMailElseTheFirstHighestScore()
	{
		var advisor = new ThemeParkIncAdvisor( Settings(), Patched() );
		Fill( advisor, Record( Rain, 0 ), Record( Rain, 40 ), Record( Rain, 90 ), Record( Rain, 90 ) );
		Assert.AreEqual( 2, advisor.Select() );
		var slots = advisor.Queue.ToArray();
		slots[5] = Record( RedMail, 1 );
		slots[6] = Record( LostMail, 2 );
		Fill( advisor, slots );
		Assert.AreEqual( 6, advisor.Select(), "the last positive mail slot" );
		slots[6] = Record( LostMail, 0 );
		Fill( advisor, slots );
		Assert.AreEqual( 5, advisor.Select(), "a mail slot needs a positive score" );
		Fill( advisor, Record( Rain, 0 ), Record( Rain, -1 ) );
		Assert.AreEqual( -1, advisor.Select(), "no positive score selects nothing" );
		Fill( advisor, Record( Rain, 90 ), Record( Tutorial, 10 ), Record( 0x1FC, 20 ) );
		Assert.AreEqual( 0, advisor.Select() );
		advisor.AdvisorPanelUp = true;
		Assert.AreEqual( 1, advisor.Select(), "with the panel up only groups 1 and 8 count (0x1FC's first row is group 0)" );
	}

	[TestMethod]
	public void ACancelClearsPendingRecordsAndUndoesTheOnePlaying()
	{
		var career = Career();
		var advisor = new ThemeParkIncAdvisor( Settings(), Patched() );
		Fill( advisor, Record( Rain, 10 ), default, default, Record( Rain, 20 ), Record( RedMail, 30 ) );
		career.SetAdviceHistory( Rain, new ThemeParkIncAdviceHistory( 0x40, 0, true, 2, 3 ) );
		advisor.Restore( advisor.Capture() with { LastStart = 1_000, LastWindow = 500, Playing = Rain } );
		// the advisor clock is the world tick times 248 ms: tick 7 is 1736, past the window; tick 5 is 1240, inside it
		At( advisor, 7 );
		Assert.IsTrue( advisor.Cancel( Rain, career, out var stopped ) );
		Assert.IsFalse( stopped );
		CollectionAssert.AreEqual( new[] { false, false, false, false, true, false, false, false }, advisor.Queue.Select( slot => slot.Valid ).ToArray() );
		Assert.AreEqual( new ThemeParkIncAdviceHistory( 0x40, 0, true, 2, 3 ), career.AdviceHistory( Rain ) );
		At( advisor, 5 );
		Assert.IsTrue( advisor.Cancel( Rain, career, out stopped ) );
		Assert.IsTrue( stopped );
		Assert.AreEqual( new ThemeParkIncAdviceHistory( 0x40, 0, false, 3, 3 ), career.AdviceHistory( Rain ) );
		Assert.AreEqual( 0u, advisor.LastWindow );
		Assert.IsFalse( advisor.Cancel( Rain, career, out _ ) );
	}

	[TestMethod]
	public void PlaybackWaitsForTheWindowOnTheTickClockAndWritesTheCareersHistory()
	{
		var career = Career();
		var advisor = new ThemeParkIncAdvisor( Settings( minimum: 10 ), ThemeParkIncAdvisorTable.Shipped );
		var context = new ThemeParkIncAdviceContext( 0, false, false, 0, 0 );
		Assert.IsTrue( advisor.Post( Rain, career, 200 << 2, context ) );
		Assert.IsTrue( advisor.Post( Rain, career, 200 << 2, context ), "a pending duplicate is allowed (limit 255)" );
		UpdateAt( advisor, 800, career, context, _ => 2_750 );
		Assert.AreEqual( new ThemeParkIncAdvicePlay( Rain, 645, 0, 0 ), advisor.TakePlayed().Single() );
		Assert.AreEqual( (800u * 248, 3_750u), (advisor.LastStart, advisor.LastWindow), "the speech plus 1000 ms" );
		Assert.AreEqual( new ThemeParkIncAdviceHistory( 800, 0, true, 0, 1 ), career.AdviceHistory( Rain ) );
		// 3750 ms is 15.1 ticks of 248 ms: the second record waits 16 ticks
		UpdateAt( advisor, 815, career, context, _ => 0 );
		Assert.AreEqual( 0, advisor.TakePlayed().Count );
		UpdateAt( advisor, 816, career, context, _ => 0 );
		Assert.AreEqual( 645, advisor.TakePlayed().Single().Response, "the one-response cycle stays at variant 0" );
		Assert.AreEqual( new ThemeParkIncAdviceHistory( 816, 0, true, 0, 2 ), career.AdviceHistory( Rain ) );
		Assert.AreEqual( 1_000u, advisor.LastWindow, "no voice: the window is the 1000 ms the update adds" );
	}

	[TestMethod]
	public void AScoreAtTheMinimumIsNotPlayedAndAFlightRefusesTheForecastsClass()
	{
		var career = Career();
		var advisor = new ThemeParkIncAdvisor( Settings( minimum: 10_000 ), ThemeParkIncAdvisorTable.Shipped );
		var idle = new ThemeParkIncAdviceContext( 0, false, false, 0, 0 );
		Assert.IsTrue( advisor.Post( Heatwave, career, 0, idle ) );
		UpdateAt( advisor, 1, career, idle, _ => 0 );
		Assert.AreEqual( 0, advisor.TakePlayed().Count, "the stored score must exceed the minimum" );
		var flight = idle with { AnimationThing = 7, AnimationVariant = 0 };
		Assert.AreEqual( -1, advisor.Score( Snow, flight ), "class 2 is not admitted during a flight" );
		Assert.IsTrue( advisor.Post( Snow, career, 0, flight ), "an ineligible record (-1) still enters a free slot" );
		Assert.AreEqual( -1, advisor.Queue[1].Score );
		for ( var klass = 0; klass < 8; klass++ )
			Assert.AreEqual( klass == 6, ThemeParkIncAdvisor.Admits( klass, flight ) );
	}

	[DataTestMethod]
	[DataRow( 0, 0, 1 )]
	[DataRow( 0, 2, 0 )]
	[DataRow( 1, 1, 2 )]
	[DataRow( 1, 2, 2 )]
	[DataRow( 2, 2, 0 )]
	[DataRow( 2, -1, 0 )]
	[DataRow( 7, 0, 0 )]
	public void TheVariantRuleTakesTheNextResponse( int rule, int last, int expected )
	{
		// three responses from 645: rule 0 goes to the message id after the last (343, out of range: 0), rule 1 stays at the last, rule 2 wraps, another rule takes the message id
		var career = Career();
		var advisor = new ThemeParkIncAdvisor( Settings(), Patched( rule: rule, count: 3 ) ) { FromHelpButton = true };
		career.SetAdviceHistory( Rain, new ThemeParkIncAdviceHistory( 0, last, false, 0, 0 ) );
		Assert.IsTrue( advisor.PostScored( Rain, 100, career, 0 ) );
		UpdateAt( advisor, 1, career, default, _ => 0 );
		Assert.AreEqual( 645 + expected, advisor.TakePlayed().Single().Response );
		Assert.AreEqual( expected, career.AdviceHistory( Rain ).Variant );
	}

	[TestMethod]
	public void ADelayedHelpMessageIsPostedOnceThePanelDelayHasPassed()
	{
		var career = Career();
		var advisor = new ThemeParkIncAdvisor( Settings() with { PanelDelay = 3_000 }, ThemeParkIncAdvisorTable.Shipped );
		advisor.PostDelayed( Rain );
		UpdateAt( advisor, 12, career, default, _ => 0 );
		Assert.IsTrue( advisor.DelayedFromPanel, "2976 ms of the 3000 have passed" );
		UpdateAt( advisor, 13, career, default, _ => 0 );
		Assert.IsFalse( advisor.DelayedFromPanel );
		Assert.AreEqual( 0, advisor.TakePlayed().Count, "the posting tick plays nothing" );
		Assert.AreEqual( Rain, advisor.Queue[0].Message );
		UpdateAt( advisor, 14, career, default, _ => 0 );
		Assert.AreEqual( Rain, advisor.TakePlayed().Single().Message );
	}

	[DataTestMethod]
	[DataRow( 1.0, 248_000L )]
	[DataRow( 2.0, 124_000L )]
	[DataRow( 4.0, 62_000L )]
	[DataRow( (double)0.7f, 354_286L )]
	public void AWorldTickSpansTheRealTimeOfItsClockRate( double rate, long microseconds )
	{
		// the world ticks run on the rate-scaled park clock, the advisor's sub-clock on real milliseconds: a tick at rate r is 248 / r real ms
		var advisor = new ThemeParkIncAdvisor( Settings(), ThemeParkIncAdvisorTable.Shipped );
		advisor.AdvanceClock( rate );
		advisor.AdvanceClock( rate );
		Assert.AreEqual( 2 * microseconds, advisor.ClockMicroseconds );
	}

	[TestMethod]
	public void TheSpeedDecidesHowManyTicksAWindowSpans()
	{
		var economy = ScienceEconomy();
		economy.TpiSpeedRate = 2;
		var clock = economy.TpiAdvisor!.ClockMicroseconds;
		economy.Advance( ParkCalendar.TickOfTurn( economy.Turn + 10 ) - economy.Tick );
		Assert.AreEqual( clock + 10 * 124_000, economy.TpiAdvisor.ClockMicroseconds );
	}

	[TestMethod]
	public void ASaveWithARecordWhoseScoreCannotBeComputedIsInvalid()
	{
		var table = ThemeParkIncAdvisorTable.Shipped;
		var computed = table.Descriptors.First( row => row.ConstantScore == null ).Message;
		var advisor = new ThemeParkIncAdvisor( Settings(), table );
		var state = advisor.Capture();
		var slots = new ThemeParkIncAdviceRecord[ThemeParkIncAdvisor.Slots];
		slots[0] = Record( computed, 50 );
		Assert.IsTrue( ThemeParkIncAdvisor.IsValid( state with { Slots = slots }, table ), "an explicit score needs no callback" );
		slots[0] = slots[0] with { ExplicitScore = false };
		Assert.IsFalse( ThemeParkIncAdvisor.IsValid( state with { Slots = slots }, table ), "the playback would score it again" );
		Assert.IsFalse( ThemeParkIncAdvisor.IsValid( state with { StoredTutorial = slots[0] }, table ) );
		Assert.IsFalse( ThemeParkIncAdvisor.IsValid( state with { DelayedFromPanel = true, DelayedMessage = computed }, table ) );
		Assert.IsTrue( ThemeParkIncAdvisor.IsValid( state with { DelayedFromPanel = true, DelayedMessage = Rain }, table ) );
		Assert.ThrowsException<InvalidDataException>( () => advisor.Restore( state with { Slots = slots } ) );
	}

	[TestMethod]
	public void TheMailIsPlayedBeforeAPendingForecast()
	{
		var career = Career();
		var advisor = new ThemeParkIncAdvisor( Settings(), ThemeParkIncAdvisorTable.Shipped );
		var context = new ThemeParkIncAdviceContext( 0, false, false, 0, 0 );
		Assert.IsTrue( advisor.Post( Rain, career, 0, context ) );
		Assert.IsTrue( advisor.Post( RedMail, career, 0, context ) );
		UpdateAt( advisor, 1, career, context, _ => 0 );
		Assert.AreEqual( new ThemeParkIncAdvicePlay( RedMail, 749, 0, 0 ), advisor.TakePlayed().Single() );
	}

	[TestMethod]
	public void TheShippedTableHoldsTheExecutablesRows()
	{
		var table = ThemeParkIncAdvisorTable.Shipped;
		Assert.AreEqual( 664, table.Descriptors.Count );
		Assert.AreEqual( 606, table.Descriptors.Select( row => row.Message ).Distinct().Count() );
		Assert.AreEqual( 290, table.Descriptors.Count( row => row.Scan ) );
		Assert.IsTrue( table.Descriptors.All( row => row.VariantRule == 2 ), "rule 3, the world-generator variant, is in no row" );
		Assert.AreEqual( new ThemeParkIncAdvisorDescriptor( 430, false, Rain, 0, 2, 0x409921, 255, 645, 1, 2, 10_000 ), table.Find( Rain ) );
		Assert.AreEqual( 8, table.Find( RedMail )!.Value.Group );
		Assert.IsNotNull( table.Find( 0x58 ), "0x58 to 0x5C have rows (group 7)" );
		Assert.IsNull( table.Find( ThemeParkIncAdvisorTable.NoMessage ) );
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncAdvisorTable.Parse( "messages = [\n]\n" ) );
	}

	[TestMethod]
	public void TheSettingsBindThroughTheSchemaAndStopAtTheFirstUnboundKey()
	{
		var settings = ThemeParkIncAdvisorSettings.Bind( Encoding.ASCII.GetBytes(
			"GeneralAdvisor.MinScoreForConsideration 10\nGeneralAdvisor.UIPanelDelay 3000\nMessageGroups[1].MinTimeSameMessage2 7\nMessageGroups[1].SayOnlyOnce 1\n"
			+ "MessageGroups[1].DiscardAfterSlaps 2\nGroupTimes[0].MinTimeForMessage 5\nMessageGroups[2].MinTimeSameMessage1 9\n" ) );
		Assert.AreEqual( (10, 3000), (settings.MinimumScore, settings.PanelDelay) );
		Assert.AreEqual( new ThemeParkIncAdvisorGroup( 0, 7, 0, 0, true, 2 ), settings.Groups[1] );
		Assert.AreEqual( default, settings.Groups[2], "the parse stopped at the key the prefix schema does not bind" );
		Assert.AreEqual( 7u, settings.Groups[1].RepeatInterval( 1 ) );
		Assert.AreEqual( 0, ThemeParkIncAdvisorSettings.Bind( null ).MinimumScore );
	}

	[TestMethod]
	public void TheRetailAdvisorSamBindsEveryGroup()
	{
		_ = ScienceEconomy();
		var settings = ThemeParkIncAdvisorSettings.Load();
		Assert.AreEqual( (10, 3000, 1), (settings.MinimumScore, settings.PanelDelay, settings.SelectionModeFilter) );
		Assert.AreEqual( new ThemeParkIncAdvisorGroup( 120, 300, 600, 1800, false, 0 ), settings.Groups[0] );
		Assert.AreEqual( new ThemeParkIncAdvisorGroup( 0, 0, 0, 0, true, 2 ), settings.Groups[1] );
		Assert.AreEqual( default, settings.Groups[8], "the file has no group 8" );
	}

	[TestMethod]
	public void AForecastPlaysAtTheAdvisorsNextTickAndHoldsItsVoiceAsTheWindow()
	{
		var economy = ScienceEconomy();
		// a play stamped at a world word below 4 reads as never played (time >> 2 == 0): start past it
		economy.Advance( ParkCalendar.TickOfTurn( 8 ) - economy.Tick );
		economy.TakeThemeParkIncAdvice();
		Assert.IsTrue( economy.PostThemeParkIncAdvice( Rain ) );
		var turn = economy.Turn;
		var clock = economy.TpiAdvisor!.ClockMicroseconds;
		economy.Advance( ParkCalendar.TickOfTurn( turn + 1 ) - economy.Tick );
		Assert.AreEqual( new ThemeParkIncAdvicePlay( Rain, 645, 0, 0 ), economy.TakeThemeParkIncAdvice().Single() );
		var advisor = economy.TpiAdvisor!;
		Assert.AreEqual( (uint)((clock + 248_000) / 1000), advisor.LastStart, "a world tick at normal speed is 248 ms on the advisor clock" );
		Assert.IsTrue( advisor.LastWindow > 2_000, $"the rain voice is longer than a second (window {advisor.LastWindow})" );
		Assert.AreEqual( new ThemeParkIncAdviceHistory( (uint)(turn + 1), 0, true, 0, 1 ), economy.TpiCareer!.AdviceHistory( Rain ) );
		Assert.IsFalse( economy.PostThemeParkIncAdvice( Rain ), "after one play group 0's second interval (300 world ticks >> 2) has not passed" );
	}

	[TestMethod]
	public void TheAdvisorSurvivesASaveAndAnOlderSaveIsRefused()
	{
		var economy = ScienceEconomy();
		Assert.IsTrue( economy.PostThemeParkIncAdvice( Rain ) );
		economy.TpiAdvisor!.Restore( economy.TpiAdvisor.Capture() with { LastStart = 9_000, LastWindow = 4_000, Playing = Heatwave } );
		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"TpiAdvisor\"" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ), "the save round-trips exactly" );
		Assert.AreEqual( (9_000u, 4_000u), (restored.TpiAdvisor!.LastStart, restored.TpiAdvisor.LastWindow), "the window is on the tick clock, which the save keeps" );

		var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		node.Remove( "TpiAdvisor" );
		var older = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ) );
		Assert.AreEqual( ParkSaveFile.AdvisorMissing, older.Message );
		node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		node["TpiForecast"] = new System.Text.Json.Nodes.JsonObject();
		Assert.AreEqual( ParkSaveFile.AdvisorMissing, Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ) ).Message );

		var envelope = ParkResumeSaveFileTests.Envelope() with { Theme = "science", Economy = ParkSaveFile.Capture( economy ) };
		var world = System.Text.Json.Nodes.JsonNode.Parse( ParkResumeJson.Serialize( envelope ) )!.AsObject();
		world["Economy"]!.AsObject().Remove( "TpiAdvisor" );
		world["AssetHashes"] = new System.Text.Json.Nodes.JsonObject();
		Assert.AreEqual( ParkSaveFile.AdvisorMissing, Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Deserialize( Encoding.UTF8.GetBytes( world.ToJsonString() ) ) ).Message,
			"refused before the assets are looked at" );
	}

	[TestMethod]
	public void AThemeParkWorldSaveWithTheAdvisorIsRefused()
	{
		var tpw = new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( Array.Empty<EconomyObjectInfo>() ), ParkGameMode.FullSimulation, 7 );
		Assert.IsNull( tpw.TpiAdvisor );
		var node = System.Text.Json.Nodes.JsonNode.Parse( ParkSaveFile.Serialize( tpw ) )!.AsObject();
		node["TpiAdvisor"] = System.Text.Json.JsonSerializer.SerializeToNode( new ThemeParkIncAdvisor( ThemeParkIncAdvisorSettings.Reset, ThemeParkIncAdvisorTable.Shipped ).Capture() );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( node.ToJsonString() ) ) );
	}
}
