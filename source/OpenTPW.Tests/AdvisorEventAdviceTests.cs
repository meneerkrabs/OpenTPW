using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// ADVISOR-009: the advice game code posts with a getter score (staff strike messages 33-57, research done 93-97, new research groups 133-136, all research done 167,
/// ride breakdowns 66/68/69): the table of their score keys, the staff message numbering and the controller's admission of them. GPU-free.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AdvisorEventAdviceTests
{
	private const string Sam = """
		GeneralAdvisor.MinTimeAnyMessage	5
		GeneralAdvisor.MinTimeSameMessage	120
		GeneralAdvisor.MinScoreForConsideration	25
		MessageGroups[0].MinTimeSameMessage		10
		MessageGroups[0].SayOnlyOnce			0
		MessageGroups[0].DiscardAfterSlaps		0
		MessageGroups[1].MinTimeSameMessage		0
		MessageGroups[1].SayOnlyOnce			1
		MessageGroups[1].DiscardAfterSlaps		2
		Welcome.Score 9000
		Bankrupted.Score	8000
		ParkNowOpen.Score	20
		ParkNowClosed.Score	20
		PrebuiltPark.Score	700
		HelpMessage.Score	1000
		HelpMessage.ContextHelpScore	1500
		StaffUnhappyMechanics.Score	70
		StaffStrikingGuards.Score	280
		StrikeEndBadResearchers.Score	120
		ResearchShopResearched.Score	55
		NewResearchGroupFeature.Score	40
		RidesBreakdownNoMechanics.Score	105
		""";

	[TestInitialize]
	public void Setup() => Log ??= new();

	private static AdvisorController Controller() => new( AdvisorBalance.Parse( Encoding.ASCII.GetBytes( Sam ), "test.sam" ) );

	[TestMethod]
	public void EveryStrikeMessageHasItsOwnAdviceOfTheFiveByFiveBlock()
	{
		var seen = new System.Collections.Generic.HashSet<int>();
		foreach ( var kind in Enum.GetValues<StrikeMessageKind>() )
			foreach ( var role in Enum.GetValues<StaffType>() )
			{
				var message = new StrikeMessage( role, kind );
				var advice = AdvisorTables.StaffMessage( message );
				Assert.IsTrue( advice is >= 33 and <= 57, $"{role} {kind}: {advice}" );
				Assert.IsTrue( seen.Add( advice ), $"{role} {kind} shares advice {advice}" );
				Assert.AreEqual( message.TagSystemIndex - 5, advice, "0x59AFC0 numbers them 33 + 5 × kind + the role's place" );
			}
		Assert.AreEqual( 25, seen.Count );
		Assert.AreEqual( 33, AdvisorTables.StaffMessage( new StrikeMessage( StaffType.Mechanic, StrikeMessageKind.Warning ) ), "mechanics first" );
		Assert.AreEqual( 34, AdvisorTables.StaffMessage( new StrikeMessage( StaffType.Handyman, StrikeMessageKind.Warning ) ) );
		Assert.AreEqual( 45, AdvisorTables.StaffMessage( new StrikeMessage( StaffType.Guard, StrikeMessageKind.Strike ) ) );
		Assert.AreEqual( 57, AdvisorTables.StaffMessage( new StrikeMessage( StaffType.Researcher, StrikeMessageKind.Over ) ) );
	}

	[TestMethod]
	public void TheStrikeAdviceCaptionsAreTheStrikeMessagesTexts()
	{
		foreach ( var kind in Enum.GetValues<StrikeMessageKind>() )
			foreach ( var role in Enum.GetValues<StaffType>() )
			{
				var message = new StrikeMessage( role, kind );
				var definition = AdvisorTables.EventMessages.Single( item => item.MessageId == AdvisorTables.StaffMessage( message ) );
				for ( var variant = 0; variant < definition.ResponseCount; variant++ )
					Assert.AreEqual( message.TagSystemIndex, AdvisorCaptionTable.For( definition.FirstResponseId + variant )?.TextEntry, $"{role} {kind} response {definition.FirstResponseId + variant}" );
			}
	}

	[TestMethod]
	public void TheEventMessagesTakeTheirControlsFromTheOriginalsDescriptorTable()
	{
		var rows = AdvisorDescriptorTable.Parse( File.ReadAllText( Shipped( AdvisorDescriptorTable.RelativePath ) ) );
		Assert.AreEqual( 25 + 3 + 1 + 5 + 4 + 1 + 4 + AdvisorChallengeMessages.EventScoreKeys.Count, AdvisorTables.EventMessages.Count, "staff 33-57, 66/68/69, the condemned ride 258, research 93-97, groups 133-136, 167, the golden-ticket moves 234-237 and the ticket wins, first-ticket lines and challenge lines (AdvisorChallengeMessages)" );
		foreach ( var message in AdvisorTables.EventMessages )
		{
			var row = rows[message.MessageId];
			Assert.IsFalse( row.Scan, $"message {message.MessageId} is posted by game code" );
			Assert.AreEqual( (row.FirstResponseId, row.ResponseCount, row.Group, row.MaximumPendingDuplicates, row.GameMode), (message.FirstResponseId, message.ResponseCount, message.Group, message.MaximumPendingDuplicates, message.GameMode) );
		}
		Assert.AreEqual( 0, AdvisorTables.EventMessages.Single( message => message.MessageId == 33 ).GameMode, "the staff advice is for the full simulation only" );
		Assert.AreEqual( 2, AdvisorTables.EventMessages.Single( message => message.MessageId == 93 ).GameMode );
		Assert.AreEqual( 255, AdvisorTables.EventMessages.Single( message => message.MessageId == 93 ).MaximumPendingDuplicates );
	}

	[TestMethod]
	public void TheControllerQueuesTheAdviceWithTheScoreOfItsKey()
	{
		var controller = Controller();
		Assert.AreEqual( AdvisorEligibility.Eligible, controller.Advise( 33, 100, 0, true )!.Value.Eligibility );
		Assert.AreEqual( 70, controller.Queue.GetSlot( 0 )!.Score, "StaffUnhappyMechanics.Score" );
		Assert.AreEqual( 105, controller.Balance.ScoreOf( 68 ) );
		Assert.AreEqual( 0, controller.Balance.ScoreOf( 69 ), "a key the settings file lacks scores 0 and stays below the minimum" );
		Assert.AreEqual( 0x2710, controller.Balance.ScoreOf( 167 ), "0x59F9A0 returns the constant 0x2710" );
		Assert.IsNull( controller.Advise( 45, 100, 2, true ), "the staff advice is not said in Instant Action" );
		Assert.IsNotNull( controller.Advise( 95, 100, 2, true ), "research done is said in every game mode" );
	}

	[TestMethod]
	public void ARepeatedStaffAdviceWaitsForThePendingOne()
	{
		var controller = Controller();
		Assert.IsTrue( controller.Advise( 45, 100, 0, true )!.Value.Stored );
		Assert.AreEqual( AdvisorEligibility.DuplicateLimit, controller.Advise( 45, 100, 0, true )!.Value.Eligibility, "the descriptor allows one pending copy" );
	}

	[TestMethod]
	public void EveryScoreKeyExistsInTheShippedAdvisorSettings()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		var sam = gamePath == null ? null : Path.Combine( gamePath, "Data", "Advisor", "Advisor.sam" );
		if ( sam == null || !File.Exists( sam ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Advisor.sam." );
		var text = File.ReadAllText( sam, Encoding.Latin1 );
		foreach ( var (_, key, fixedScore) in AdvisorTables.EventScoreKeys.Where( entry => entry.FixedScore == null ) )
			StringAssert.Contains( text, key + "\t", $"{key} is a key of Advisor.sam" );
	}

	private static string Shipped( string relative )
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var path = Path.Combine( directory.FullName, relative );
			if ( File.Exists( path ) )
				return path;
		}
		Assert.Fail( $"{relative} not found above the test directory." );
		return "";
	}

	[TestMethod]
	public void ResearchDoneAdviceFollowsTheCategory()
	{
		Assert.AreEqual( 93, AutomaticAdvisor.ResearchDoneMessage( ResearchCategory.Ride ) );
		Assert.AreEqual( 94, AutomaticAdvisor.ResearchDoneMessage( ResearchCategory.Upgrade ) );
		Assert.AreEqual( 95, AutomaticAdvisor.ResearchDoneMessage( ResearchCategory.Shop ) );
		Assert.AreEqual( 96, AutomaticAdvisor.ResearchDoneMessage( ResearchCategory.Sideshow ) );
		Assert.AreEqual( 97, AutomaticAdvisor.ResearchDoneMessage( ResearchCategory.Feature ) );
	}

	[TestMethod]
	public void NewResearchGroupAdviceHasNoneForUpgrades()
	{
		Assert.AreEqual( 133, AutomaticAdvisor.NewResearchGroupMessage( ResearchCategory.Ride ) );
		Assert.AreEqual( 134, AutomaticAdvisor.NewResearchGroupMessage( ResearchCategory.Shop ) );
		Assert.AreEqual( 135, AutomaticAdvisor.NewResearchGroupMessage( ResearchCategory.Sideshow ) );
		Assert.AreEqual( 136, AutomaticAdvisor.NewResearchGroupMessage( ResearchCategory.Feature ) );
		Assert.IsNull( AutomaticAdvisor.NewResearchGroupMessage( ResearchCategory.Upgrade ) );
	}
}
