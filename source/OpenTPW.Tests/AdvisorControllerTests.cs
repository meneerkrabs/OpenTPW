using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class AdvisorControllerTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	// Synthetic settings in the Advisor.sam layout (values chosen for the tests, not copied from the original).
	private const string Settings = """
		# comment
		GeneralAdvisor.MinTimeAnyMessage	7
		GeneralAdvisor.MinTimeSameMessage	 99		free text after the value
		GeneralAdvisor.MinScoreForConsideration	50
		MessageGroups[0].MinTimeSameMessage		10
		MessageGroups[0].SayOnlyOnce			0
		MessageGroups[0].DiscardAfterSlaps		0
		MessageGroups[1].MinTimeSameMessage		0
		MessageGroups[1].SayOnlyOnce			1
		MessageGroups[1].DiscardAfterSlaps		2
		Welcome.Score 9000
		Bankrupted.Score	8000
		ParkNowOpen.Score	50
		ParkNowClosed.Score	51
		PrebuiltPark.Score	700
		""";

	private static AdvisorBalance Balance( string text = Settings ) => AdvisorBalance.Parse( Encoding.ASCII.GetBytes( text ), "test.sam" );

	private sealed class Harness
	{
		public readonly AdvisorController Controller = new( Balance() );
		public readonly List<int> Played = new();
		public uint Clock = 1000;
		public uint Tick = 100;
		public uint? Span = 4000;

		public int? Update()
		{
			var before = Played.Count;
			Controller.Update( () => Clock, () => Tick, response =>
			{
				Played.Add( response );
				return Span;
			} );
			return Played.Count > before ? Played[^1] : null;
		}

		public void Raise( AdvisorGameEvent gameEvent, int gameType = 0, bool tutorial = true ) =>
			Controller.HandleGameEvent( gameEvent, Tick, gameType, tutorial );
	}

	[TestMethod]
	public void GameEventsConstructTheTracedAdvice()
	{
		CollectionAssert.AreEqual( new[] { 0 }, AdvisorTables.AdviceFor( AdvisorGameEvent.LevelStarted, 0 ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 323 }, AdvisorTables.AdviceFor( AdvisorGameEvent.LevelStarted, 2 ).ToArray(), "PrebuiltPark only in game type 2" );
		CollectionAssert.AreEqual( new[] { 106 }, AdvisorTables.AdviceFor( AdvisorGameEvent.Bankrupted, 0 ).ToArray() );
		CollectionAssert.AreEqual( new[] { 128 }, AdvisorTables.AdviceFor( AdvisorGameEvent.ParkNowOpen, 0 ).ToArray() );
		CollectionAssert.AreEqual( new[] { 129 }, AdvisorTables.AdviceFor( AdvisorGameEvent.ParkNowClosed, 0 ).ToArray() );
		Assert.AreEqual( 0, AdvisorTables.AdviceFor( AdvisorGameEvent.ResetForEasyMode, 2 ).Count );
		Assert.AreEqual( 0, AdvisorTables.AdviceFor( (AdvisorGameEvent)1, 0 ).Count, "untraced events construct nothing" );
	}

	[TestMethod]
	public void BoundDescriptorsResolveToTheTracedResponsesAndBanks()
	{
		var table = AdvisorResponses.Parse( File.ReadAllText( ShippedResponses() ) );
		var expected = new (int Message, int Response, int Sample, bool Local)[]
		{
			(0, 1, 1, true), (106, 274, 424, false), (106, 275, 425, false), (128, 308, 342, false), (128, 309, 343, false),
			(129, 310, 344, false), (129, 311, 345, false), (323, 587, 606, false),
		};
		foreach ( var (message, responseId, sample, local) in expected )
		{
			var descriptor = AdvisorTables.Messages.Single( item => item.MessageId == message );
			Assert.IsTrue( responseId >= descriptor.FirstResponseId && responseId < descriptor.FirstResponseId + descriptor.ResponseCount, $"message {message} covers response {responseId}" );
			Assert.AreEqual( sample, table[responseId].Sample, $"response {responseId}" );
			Assert.AreEqual( sample, table[responseId].Lip, $"response {responseId} LIP" );
			Assert.AreEqual( local, table[responseId].Local, $"response {responseId} bank" );
		}
		Assert.AreEqual( 5, AdvisorTables.Messages.Length );
		Assert.AreEqual( 1, AdvisorTables.Messages.Single( item => item.MessageId == 323 ).Group, "PrebuiltPark is a tutorial-group message" );
	}

	[TestMethod]
	public void LevelStartSaysWelcomeThenPrebuiltParkAfterTheReservation()
	{
		var harness = new Harness();
		harness.Raise( AdvisorGameEvent.ResetForEasyMode, 2 );
		harness.Raise( AdvisorGameEvent.LevelStarted, 2 );
		Assert.AreEqual( 2, harness.Controller.Queue.Count );
		Assert.AreEqual( 1, harness.Update(), "the higher Welcome score plays first: response 1" );
		// Busy for the returned span plus 1000.
		harness.Clock = 1000 + 4000 + 999;
		Assert.IsNull( harness.Update() );
		harness.Clock = 1000 + 4000 + 1000;
		Assert.AreEqual( 587, harness.Update() );
		harness.Clock += 100000;
		Assert.IsNull( harness.Update() );
		Assert.AreEqual( 0, harness.Controller.Queue.Count );
	}

	[TestMethod]
	public void PrebuiltParkResolvesItsStoredResponseIdNotATableRow()
	{
		// The original searches the response table for the stored ID (0x10006BF8); from row 393 on many
		// rows hold a different ID, so indexing by row would play the wrong clip.
		var harness = new Harness();
		harness.Raise( AdvisorGameEvent.LevelStarted, 2 );
		harness.Update();
		harness.Clock += 10000;
		Assert.AreEqual( 587, harness.Update() );
		var text = File.ReadAllText( ShippedResponses() );
		var response = AdvisorResponses.Parse( text )[587];
		Assert.AreEqual( (606, 606, false), (response.Sample, response.Lip, response.Local) );
		var rows = text.Split( '\n' ).Where( line => line.TrimStart().StartsWith( '{' ) ).ToArray();
		Assert.AreEqual( 610, rows.Length );
		StringAssert.Contains( rows[587], "sample = 638, lip = 0,", "row 587 is a different response; lookups must go by ID" );
	}

	[TestMethod]
	public void FullSimulationLevelStartHasNoPrebuiltParkAdvice()
	{
		var harness = new Harness();
		harness.Raise( AdvisorGameEvent.LevelStarted, 0 );
		Assert.AreEqual( 1, harness.Update() );
		Assert.AreEqual( 0, harness.Controller.Queue.Count );
	}

	[TestMethod]
	public void ScoresAtTheMinimumWaitAndOnlyHigherScoresPlay()
	{
		var harness = new Harness();
		harness.Raise( AdvisorGameEvent.ParkNowOpen );
		Assert.AreEqual( 1, harness.Controller.Queue.Count, "admission has no minimum-score gate" );
		Assert.IsNull( harness.Update(), "50 is not above the minimum 50" );
		harness.Raise( AdvisorGameEvent.ParkNowClosed );
		Assert.AreEqual( 310, harness.Update() );
		Assert.AreEqual( 1, harness.Controller.Queue.Count, "ParkNowOpen stays pending" );
	}

	[TestMethod]
	public void BankruptcyCyclesThroughItsTwoResponses()
	{
		var harness = new Harness();
		var responses = new List<int?>();
		for ( var index = 0; index < 3; index++ )
		{
			harness.Raise( AdvisorGameEvent.Bankrupted );
			responses.Add( harness.Update() );
			harness.Clock += 10000;
			harness.Tick += 40;
		}
		CollectionAssert.AreEqual( new int?[] { 274, 275, 274 }, responses );
	}

	[TestMethod]
	public void RepeatIntervalUsesQuarterTicksAndEventTenKeepsTheSavedTick()
	{
		var harness = new Harness();
		harness.Raise( AdvisorGameEvent.LevelStarted );
		harness.Update();
		harness.Tick += 39;
		var refused = harness.Controller.HandleGameEvent( AdvisorGameEvent.LevelStarted, harness.Tick, 0, true );
		Assert.AreEqual( AdvisorEligibility.RepeatDelay, refused[0].Admission.Eligibility, "9 of 10 groups of four ticks" );
		harness.Raise( AdvisorGameEvent.ResetForEasyMode );
		Assert.AreEqual( AdvisorMessageHistory.Empty with { SavedGameTick = 100 }, harness.Controller.Queue.GetHistory( 0 ), "variant, played flag and slap count reset; the saved tick stays" );
		Assert.AreEqual( AdvisorEligibility.RepeatDelay, harness.Controller.HandleGameEvent( AdvisorGameEvent.LevelStarted, harness.Tick, 0, true )[0].Admission.Eligibility );
		harness.Tick += 1;
		Assert.AreEqual( AdvisorEligibility.Eligible, harness.Controller.HandleGameEvent( AdvisorGameEvent.LevelStarted, harness.Tick, 0, true )[0].Admission.Eligibility );
	}

	[TestMethod]
	public void TutorialAdviceNeedsTheTutorialOptionAndIsSaidOnce()
	{
		var harness = new Harness();
		var off = harness.Controller.HandleGameEvent( AdvisorGameEvent.LevelStarted, harness.Tick, 2, false );
		Assert.AreEqual( AdvisorEligibility.TutorialDisabled, off.Single( item => item.MessageId == 323 ).Admission.Eligibility );
		var harness2 = new Harness();
		harness2.Raise( AdvisorGameEvent.LevelStarted, 2 );
		harness2.Update();
		harness2.Clock += 10000;
		Assert.AreEqual( 587, harness2.Update() );
		var again = harness2.Controller.HandleGameEvent( AdvisorGameEvent.LevelStarted, harness2.Tick + 1000, 2, true );
		Assert.AreEqual( AdvisorEligibility.AlreadyPlayed, again.Single( item => item.MessageId == 323 ).Admission.Eligibility );
	}

	[TestMethod]
	public void UnplayedResponseStillRecordsHistoryAndReservesTheMinimumAction()
	{
		// The original wrapper keeps the player's result only as the span and succeeds (0x1000BBF0).
		var harness = new Harness { Span = null };
		harness.Raise( AdvisorGameEvent.LevelStarted );
		Assert.AreEqual( 1, harness.Update() );
		Assert.AreEqual( 0, harness.Controller.Queue.Count );
		Assert.AreEqual( new AdvisorMessageHistory( 100, 0, true, 0 ), harness.Controller.Queue.GetHistory( 0 ) );
		Assert.AreEqual( (1000u, 1000u), (harness.Controller.Queue.LastActionStarted, harness.Controller.Queue.LastActionDuration), "span 0 plus 1000" );
		Assert.IsTrue( harness.Controller.Queue.IsBusy( 1999 ) );
		Assert.IsFalse( harness.Controller.Queue.IsBusy( 2000 ) );
	}

	[TestMethod]
	public void AdvisorOptionOffConsumesTheAdviceSilently()
	{
		// Option byte +0x34 clear: the player returns 0 (0x10006BC0), so the advice is still picked and recorded.
		var harness = new Harness();
		harness.Raise( AdvisorGameEvent.LevelStarted, 2 );
		var said = new List<int>();
		harness.Controller.Update( () => harness.Clock, () => harness.Tick, response =>
		{
			said.Add( response );
			return 0u;
		} );
		CollectionAssert.AreEqual( new[] { 1 }, said );
		Assert.IsTrue( harness.Controller.Queue.GetHistory( 0 ).HasBeenPlayed );
		harness.Clock += 1000;
		Assert.AreEqual( 587, harness.Update(), "the welcome is not left pending for when the option is switched on" );
	}

	[TestMethod]
	public void BalanceReadsGroupsScoresAndRejectsMissingOrOversizedSettings()
	{
		var balance = Balance();
		Assert.AreEqual( 50, balance.MinimumScore );
		Assert.AreEqual( 7, balance.MinimumTimeAnyMessage );
		Assert.AreEqual( 99, balance.MinimumTimeSameMessage );
		Assert.AreEqual( (10u, false, 0u), balance.Groups[0] );
		Assert.AreEqual( (0u, true, 2u), balance.Groups[1] );
		Assert.AreEqual( 9000, balance.ScoreOf( 0 ) );
		Assert.AreEqual( 700, balance.ScoreOf( 323 ) );
		Assert.ThrowsException<KeyNotFoundException>( () => Balance( Settings.Replace( "Bankrupted.Score", "# Bankrupted.Score" ) ) );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorBalance.Parse( new byte[AdvisorBalance.MaximumBytes + 1], "big.sam" ) );
	}

	[TestMethod]
	public void OriginalAdvisorSettingsKeepParkOpenAdviceBelowTheMinimum()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Advisor.sam." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var file = Directory.EnumerateDirectories( dataPath ).Where( directory => string.Equals( Path.GetFileName( directory ), "Advisor", StringComparison.OrdinalIgnoreCase ) )
			.SelectMany( directory => Directory.EnumerateFiles( directory ) ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "Advisor.sam", StringComparison.OrdinalIgnoreCase ) );
		if ( file == null )
			Assert.Inconclusive( "The original Advisor/Advisor.sam is missing." );
		var balance = AdvisorBalance.Parse( File.ReadAllBytes( file! ), file! );
		Assert.AreEqual( 25, balance.MinimumScore );
		Assert.AreEqual( (120u, false, 0u), balance.Groups[0] );
		Assert.AreEqual( (0u, true, 3u), balance.Groups[1] );
		Assert.IsTrue( balance.ScoreOf( 0 ) > balance.ScoreOf( 323 ) && balance.ScoreOf( 323 ) > balance.MinimumScore, "Welcome, then PrebuiltPark" );
		Assert.IsTrue( balance.ScoreOf( 106 ) > balance.MinimumScore, "Bankrupted can play" );
		Assert.IsTrue( balance.ScoreOf( 128 ) <= balance.MinimumScore && balance.ScoreOf( 129 ) <= balance.MinimumScore, "ParkNowOpen/Closed never pass the cached-score minimum" );
	}

	private static string ShippedResponses()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var path = Path.Combine( directory.FullName, AdvisorResponses.RelativePath );
			if ( File.Exists( path ) )
				return path;
		}
		Assert.Fail( "content/data/advisor-responses.toml not found above the test directory." );
		return "";
	}
}
