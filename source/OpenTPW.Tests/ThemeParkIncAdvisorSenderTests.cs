using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's advisor senders of phase P4 (docs/reverse/TPI-ADVISOR.md §9): the response table, the score paths (constants, scorer words, the history callbacks), the window help
/// (<c>0x503A40</c>, <c>0x503AD0</c>, <c>0x5039F0</c>, <c>0x503B40</c>), the stored tutorial (<c>0x718AB0</c>), the unread-mail counter (<c>0x54F1A0</c>), the mission controller's and the challenge
/// system's advice. The tables are checked against the executable by tools/ppc-analysis/oracle_x86/tpi_advisor_tables_oracle.py and tpi_advisor_queue_oracle.py.
/// </summary>
[TestClass]
public sealed class ThemeParkIncAdvisorSenderTests : ThemeParkIncScienceTestBase
{
	private static ThemeParkIncCareer Career() => new( new TestCareerFiles(), TestCareerFiles.ShippedFiringLines );

	private static readonly ThemeParkIncAdviceContext Default = new( 0, false, false, 0, 0 );

	/// <summary>Settings with every group's controls at zero, <paramref name="help"/> as HelpMessage.Score and <paramref name="context"/> as HelpMessage.ContextHelpScore.</summary>
	private static ThemeParkIncAdvisorSettings Settings( int help = 1000, int context = 900, bool tutorialOnce = false ) =>
		ThemeParkIncAdvisorSettings.Bind( System.Text.Encoding.ASCII.GetBytes(
			$"GeneralAdvisor.MinScoreForConsideration 10\nMessageGroups[1].SayOnlyOnce {(tutorialOnce ? 1 : 0)}\nHelpMessage.Score {help}\nHelpMessage.ContextHelpScore {context}\n" ) );

	private static ThemeParkIncAdvisor Advisor( ThemeParkIncAdvisorSettings? settings = null ) => new( settings ?? Settings(), ThemeParkIncAdvisorTable.Shipped );

	private static IEnumerable<ThemeParkIncAdviceRecord> Pending( ThemeParkIncAdvisor advisor ) => advisor.Queue.Where( slot => slot.Valid );

	[TestMethod]
	public void TheResponseTableIsTheExecutablesAndKeepsTheRowsPlayedSoFar()
	{
		var table = ThemeParkIncAdviceResponseTable.Shipped;
		Assert.AreEqual( ThemeParkIncAdviceResponseTable.Rows, table.Responses.Count );
		foreach ( var known in ThemeParkIncAdviceResponse.All )
			Assert.AreEqual( known, table.Find( known.Response ), $"response {known.Response}" );
		Assert.AreEqual( 2, table.Responses.Count( row => row.Response == 833 ), "833 has two rows" );
		Assert.AreEqual( 953u, table.Find( 833 )!.Value.Voice, "the lookup takes the first row (0x716D10)" );
		// the unread mail (0x198: 739; 0x15D: 652, 653), the help of the ported windows and the researched-obstacle advice with its caption
		Assert.AreEqual( new ThemeParkIncAdviceResponse( 739, 859, 859, -1, 0, false, 0, ThemeParkIncAdviceResponse.NoTag ), table.Find( 739 ) );
		Assert.AreEqual( new ThemeParkIncAdviceResponse( 763, 883, 883, -1, 0, false, 0, ThemeParkIncAdviceResponse.NoTag ), table.Find( 763 ) );
		Assert.AreEqual( new ThemeParkIncAdviceResponse( 823, 943, 943, 25, 1, false, -1, 0x60 ), table.Find( 823 ) );
		Assert.IsTrue( table.Find( 1 )!.Value.LevelBank, "the welcome (message 0, response 1) speaks from the level's bank" );
		Assert.AreEqual( 55, table.Responses.Count( row => row.SecondAccessory != 0 ), "the word +0x18 (0x715F9C) is read into SecondAccessory" );
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncAdviceResponseTable.Parse( "responses = [\n]\n" ) );
		// every caption the posted advice can broadcast has a tag record
		foreach ( var response in new[] { 823, 824, 825, 826 } )
			Assert.IsNotNull( ThemeParkIncAdviceTag.For( table.Find( response )!.Value.Tag ), $"response {response}" );
	}

	[TestMethod]
	public void TheScorePathsAreTheCallbacks()
	{
		var table = ThemeParkIncAdvisorTable.Shipped;
		Assert.AreEqual( 0x49C, table.Find( 0x1AF )!.Value.Field, "the window help's callback 0x71FE90 returns HelpMessage.Score" );
		Assert.AreEqual( 0x49C, table.Find( 0x277 )!.Value.Field, "0x729210" );
		Assert.AreEqual( 0x770, table.Find( 0x1E8 )!.Value.Field, "0x727070: TutorialUIPanel.Score" );
		Assert.AreEqual( 194, table.Descriptors.Count( row => row.Field != null ) );
		var advisor = Advisor( Settings( help: 1234 ) );
		var career = Career();
		Assert.AreEqual( 10_000, advisor.Score( 0x198, Default ) );
		Assert.AreEqual( 10_000, advisor.Score( 0x15D, Default ) );
		Assert.AreEqual( 1234, advisor.Score( 0x1AF, Default ) );
		Assert.AreEqual( 10_000, advisor.Score( 0x223, Default ), "the mission panel advice" );
		// 0x727410 and 0x728580: 10000 until the history has 0x1FA (0x21D) played
		Assert.AreEqual( 10_000, advisor.Score( 0x1FA, Default, career ) );
		Assert.AreEqual( 10_000, advisor.Score( 0x21D, new ThemeParkIncAdviceContext( 0, false, false, 7, 0 ), career ), "class 6: a flight of mode 0" );
		career.SetAdviceHistory( 0x1FA, career.AdviceHistory( 0x1FA ) with { Played = true } );
		career.SetAdviceHistory( 0x21D, career.AdviceHistory( 0x21D ) with { Played = true } );
		Assert.AreEqual( 0, advisor.Score( 0x1FA, Default, career ) );
		Assert.AreEqual( 0, advisor.Score( 0x21D, new ThemeParkIncAdviceContext( 0, false, false, 7, 0 ), career ) );
		// class 6 (0x26C, 0x21D) is refused by any context but a flight of mode 0: the mode-2 flight the controller has just started scores them -1
		Assert.AreEqual( -1, advisor.Score( 0x26C, new ThemeParkIncAdviceContext( 0, false, false, 7, 2 ) ) );
		Assert.AreEqual( -1, advisor.Score( 0x26C, Default ) );
		Assert.ThrowsException<InvalidOperationException>( () => advisor.Score( 0x1FA, Default ), "the history callback needs the career" );
		Assert.ThrowsException<NotSupportedException>( () => advisor.Score( 0x211, Default ), "0x727E60 reads the first-hour clock (P7)" );
	}

	[TestMethod]
	public void TheHelpButtonBypassesOnceOnlyAndBecomesTheHelpMessage()
	{
		var advisor = Advisor( Settings( tutorialOnce: true ) );
		var career = Career();
		career.SetAdviceHistory( 0x1AF, career.AdviceHistory( 0x1AF ) with { Played = true } );
		advisor.PostDelayed( 0x1B0 );
		Assert.IsFalse( advisor.PanelHelp( 0x1AF, career, 0, Default ), "SayOnlyOnce refuses the played group-1 help said at once (0x5039F0)" );
		Assert.IsTrue( advisor.DelayedFromPanel, "0x5039F0 leaves the delayed help" );
		Assert.IsTrue( advisor.HelpButton( 0x1AF, career, 0, Default ), "0x503A40 sets +0x10C around the post: the once-only check is skipped" );
		Assert.AreEqual( (false, false, 0x1AF), (advisor.DelayedFromPanel, advisor.FromHelpButton, advisor.HelpMessage), "+0x10D and +0x10C cleared, 0x9DDBEC the message" );
		var record = Pending( advisor ).Single();
		Assert.AreEqual( (0x1AF, 1000, false, false), (record.Message, record.Score, record.ExplicitScore, record.Bypass), "the descriptor's score: HelpMessage.Score" );
		Assert.AreEqual( 0x1AF, advisor.StoredTutorial.Message, "a group-1 record is kept as the stored tutorial" );
	}

	[TestMethod]
	public void TheHelpCancelDropsTheDelayAndThePendingRecords()
	{
		var advisor = Advisor();
		var career = Career();
		Assert.IsTrue( advisor.PanelHelp( 0x1B6, career, 0, Default ) );
		advisor.PostDelayed( 0x277 );
		Assert.IsTrue( advisor.CancelHelp( 0x1B6, career ) );
		Assert.AreEqual( (false, 0), (advisor.DelayedFromPanel, Pending( advisor ).Count()), "0x503B40: +0x10D = 0, then 0x718990" );
		Assert.IsFalse( advisor.CancelHelp( 0x1B6, career ), "nothing left to cancel" );
	}

	[TestMethod]
	public void TheDelayedHelpIsPostedOnceThePanelDelayHasPassed()
	{
		var settings = ThemeParkIncAdvisorSettings.Bind( System.Text.Encoding.ASCII.GetBytes( "GeneralAdvisor.MinScoreForConsideration 10\nGeneralAdvisor.UIPanelDelay 3000\nHelpMessage.Score 1000\n" ) );
		var advisor = new ThemeParkIncAdvisor( settings, ThemeParkIncAdvisorTable.Shipped );
		var career = Career();
		advisor.PostDelayed( 0x1AF );
		for ( var tick = 0; tick < 12; tick++ )
		{
			advisor.AdvanceClock( 1 );
			advisor.Update( tick, career, Default, _ => 0 );
		}
		Assert.AreEqual( (true, 0), (advisor.DelayedFromPanel, Pending( advisor ).Count()), "12 ticks are 2976 ms: the delay has not passed" );
		advisor.AdvanceClock( 1 );
		advisor.Update( 12, career, Default, _ => 0 );
		Assert.IsFalse( advisor.DelayedFromPanel );
		Assert.AreEqual( (0x1AF, 1000), Pending( advisor ).Select( slot => (slot.Message, slot.Score) ).Single(), "posted with its descriptor's score" );
	}

	[TestMethod]
	public void TheRepeatTutorialOffersTheStoredRecordWithTheContextHelpScore()
	{
		var advisor = Advisor( Settings( context: 900, tutorialOnce: true ) );
		var career = Career();
		Assert.IsFalse( advisor.RepeatTutorial( career, 0 ), "nothing stored (+0x100 clear)" );
		Assert.IsTrue( advisor.Post( 0x1FA, career, 0, Default ) );
		Assert.IsTrue( advisor.Cancel( 0x1FA, career, out _ ) );
		career.SetAdviceHistory( 0x1FA, career.AdviceHistory( 0x1FA ) with { Played = true } );
		Assert.IsFalse( advisor.Post( 0x1FA, career, 0, Default ), "say only once" );
		Assert.IsTrue( advisor.RepeatTutorial( career, 0 ), "the bypass byte skips the once-only check" );
		var record = Pending( advisor ).Single();
		Assert.AreEqual( (0x1FA, 900, true, true), (record.Message, record.Score, record.ExplicitScore, record.Bypass) );
		Assert.AreEqual( record, advisor.StoredTutorial, "the enqueue keeps the record again" );
	}

	[TestMethod]
	public void TheUnreadMailCounterPostsItsAdviceAtThreeThenSix()
	{
		var economy = ScienceEconomy();
		var advisor = economy.TpiAdvisor!;
		var tube = economy.TpiTagTube!;
		if ( tube.Unread != 0 || tube.Threshold != ThemeParkIncTagTube.FirstThreshold )
			Assert.Inconclusive( "The Science park starts with unread mail." );
		var posted = new List<int>();
		for ( var letter = 1; letter <= 6; letter++ )
		{
			economy.FileThemeParkIncAdviceRow( new ThemeParkIncAdviceBroadcast( 62, 0, 0 ) );
			posted.AddRange( advisor.Queue.Where( slot => slot.Valid && slot.Message is 0x198 or 0x15D ).Select( slot => slot.Message ).Except( posted ) );
			Assert.AreEqual( letter, tube.Unread );
			if ( letter == 3 )
				CollectionAssert.AreEqual( new[] { 0x198 }, posted, "the third unread letter posts 0x198 (threshold 3)" );
		}
		CollectionAssert.AreEqual( new[] { 0x198, 0x15D }, posted, "the sixth posts 0x15D (threshold 6)" );
		Assert.AreEqual( 12, tube.Threshold );
		Assert.IsTrue( advisor.Queue.Where( slot => slot.Valid && slot.Message is 0x198 or 0x15D ).All( slot => slot.Score == 10_000 && !slot.ExplicitScore ), "the descriptor's score" );
	}

	[TestMethod]
	public void TheChallengeSendersNameTheirAdvice()
	{
		CollectionAssert.AreEqual( new[] { 0x239, 0x25C, 0x245, 0x265, 0x255 }, Enumerable.Range( 2, 5 ).Select( sender => ThemeParkIncChallenges.SenderAdvice( sender ) ).ToArray(),
			"word 1, which the accept cancels (0x5C84EB)" );
		CollectionAssert.AreEqual( new[] { 0x23A, 0x25D, 0x246, 0x266, 0x256 },
			Enumerable.Range( 2, 5 ).Select( sender => ThemeParkIncChallenges.SenderAdvice( sender, ThemeParkIncChallenges.TimeOutCancelWord ) ).ToArray(), "word 4, which the time-out cancels (0x5C216E)" );
		foreach ( var sender in new[] { 0, 1, 7, 8, 9 } )
			Assert.ThrowsException<InvalidOperationException>( () => ThemeParkIncChallenges.SenderAdvice( sender ), $"sender {sender}" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => ThemeParkIncChallenges.SenderAdvice( 2, 2 ) );
	}

	[TestMethod]
	public void EveryRetailChallengeHasASenderWithAdviceAndTheAcceptCancelsIt()
	{
		var economy = ScienceEconomy();
		economy.OpenPark();
		var challenges = economy.TpiChallenges!;
		foreach ( var slot in challenges.Slots.Where( slot => slot.Type != 0 ) )
			Assert.IsTrue( slot.Sender is >= 2 and <= 6, $"sender {slot.Sender}" );
		var offered = Enumerable.Range( 0, ThemeParkIncChallenges.SlotCount ).FirstOrDefault( index => challenges.Slots[index].Type != 0 && ThemeParkIncChallenges.IsPorted( challenges.Slots[index].Type ), -1 );
		if ( offered < 0 )
			Assert.Inconclusive( "The Science park offers no ported challenge." );
		Assert.AreEqual( offered, economy.DebugOfferThemeParkIncChallenge( offered ) );
		var advice = ThemeParkIncChallenges.SenderAdvice( challenges.Slots[offered].Sender );
		Assert.IsTrue( economy.TpiAdvisor!.PostScored( advice, 500, economy.TpiCareer!, economy.Turn ) );
		if ( !economy.CanAcceptThemeParkIncChallenge( offered ) )
			Assert.Inconclusive( "The Science park is closed." );
		economy.AcceptThemeParkIncChallenge( offered );
		Assert.IsFalse( economy.TpiAdvisor.Queue.Any( slot => slot.Valid && slot.Message == advice ), "0x5C84C0 cancels the sender's advice first" );
	}

	[DataTestMethod]
	[DataRow( 0, false, 0x19B )]
	[DataRow( 0, true, 0x172 )]
	[DataRow( 3, false, 0x1EB )]
	[DataRow( 6, true, 0x1EC )]
	[DataRow( 9, false, 0x1ED )]
	[DataRow( 12, false, 0x1EE )]
	[DataRow( 4, true, 0x172 )]
	[DataRow( 13, false, 0x19B )]
	public void TheResearchedObstacleSaysTheMissionsAdvice( int counter, bool played19B, int expected ) =>
		Assert.AreEqual( expected, ThemeParkIncMissionController.ObstacleResearchedAdvice( counter, played19B ) );

	[TestMethod]
	public void TheMissionPanelAdviceIsTheCountersRow()
	{
		Assert.AreEqual( 0x223, ThemeParkIncMissionWindow.PanelAdvice( 0 ) );
		Assert.AreEqual( 0x234, ThemeParkIncMissionWindow.PanelAdvice( 17 ) );
		Assert.AreEqual( 0, ThemeParkIncMissionWindow.PanelAdvice( 18 ) );
		Assert.ThrowsException<NotSupportedException>( () => ThemeParkIncMissionWindow.PanelAdvice( 20 ) );
		var table = ThemeParkIncAdvisorTable.Shipped;
		for ( var counter = 0; counter <= 17; counter++ )
			Assert.AreEqual( (8, 10_000), (table.Find( ThemeParkIncMissionWindow.PanelAdvice( counter ) )!.Value.Group, table.Find( ThemeParkIncMissionWindow.PanelAdvice( counter ) )!.Value.ConstantScore),
				"mail group, constant 10000" );
	}

	[TestMethod]
	public void TheEconomysControllerPostsThePromotionAdvice()
	{
		var economy = ScienceEconomy();
		economy.TpiMissions!.Promotion( economy.TpiCareer!.Rank + 1 );
		var record = economy.TpiAdvisor!.Queue.Single( slot => slot.Valid && slot.Message == 0x1A4 );
		Assert.AreEqual( (10_000, false), (record.Score, record.ExplicitScore), "0x5F10F7: the descriptor's score" );
		economy.TpiMissions.CameraFinished( economy.Turn );
		Assert.IsTrue( economy.TpiAdvisor.Queue.Any( slot => slot.Valid && slot.Message == 0x1A4 ), "the flight's end cancels 0x172, 0x19B, 0x1E3, 0x20F and 0x210 only" );
	}

	/// <summary>Records what a window asks of the advisor.</summary>
	internal sealed class RecordingAdvice : IThemeParkIncWindowAdvice
	{
		public List<string> Calls { get; } = new();
		public void DelayedHelp( int message ) => Calls.Add( $"delayed {message:X}" );
		public bool HelpButton( int message )
		{
			Calls.Add( $"help {message:X}" );
			return true;
		}
		public bool CancelHelp( int message )
		{
			Calls.Add( $"cancel help {message:X}" );
			return true;
		}
		public bool Post( int message )
		{
			Calls.Add( $"post {message:X}" );
			return true;
		}
		public bool Cancel( int message )
		{
			Calls.Add( $"cancel {message:X}" );
			return true;
		}
	}

	private sealed class TubeWorld( ThemeParkIncTagTube tube ) : IThemeParkIncTagTubeWorld
	{
		public ThemeParkIncTagTube Tube => tube;
		public void Delete( ThemeParkIncTagTubeRow row ) => tube.Delete( row );
		public bool Read( ThemeParkIncTagTubeRow row ) => tube.MarkRead( row );
	}

	[TestMethod]
	public void TheTagTubeWindowSaysItsHelpAndCancelsItAndTheGotoHintWhenItGoes()
	{
		var strings = new UiStringTable( "English", Enumerable.Range( 0, 1000 ).Select( index => $"ui{index}" ).ToArray(), Enumerable.Range( 0, 400 ).Select( index => $"help{index}" ).ToArray(),
			Array.Empty<string>(), Array.Empty<string>() );
		var advice = new RecordingAdvice();
		var closed = 0;
		var window = new ThemeParkIncTagTubeWindow( strings, new TubeWorld( new ThemeParkIncTagTube() ), new ThemeParkIncTagTubeSession(),
			row => new ThemeParkIncLetterWindow.Letter( "s", "f", "t", "d", "b", -1, 0, 0 ), () => closed++, _ => { } ) { Advice = advice };
		var screen = window.Build();
		CollectionAssert.AreEqual( new[] { "delayed 1AF" }, advice.Calls, "0x5523E6: the builder ends with 0x503AD0(0x1AF)" );
		((UiButton)screen.Find( "help" )!).Clicked!();
		Assert.AreEqual( "help 1AF", advice.Calls[^1], "0x551A14: the help button" );
		window.Close();
		CollectionAssert.AreEqual( new[] { "cancel help 1AF", "cancel 211" }, advice.Calls.Skip( 2 ).ToArray(), "0x5517E0: the destroy cancels the help, then the go-to hint" );
		Assert.AreEqual( 1, closed );
	}
}
