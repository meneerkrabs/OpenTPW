using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's advisor senders of phase P5 (docs/reverse/TPI-ADVISOR.md §9.3): per sender the message, the score path and the acceptance, and the captions' tag records and formatters. The
/// native paths are checked by tools/ppc-analysis/oracle_x86/tpi_advisor_queue_oracle.py (check_senders_b).
/// </summary>
[TestClass]
public sealed class ThemeParkIncAdvisorSenderBTests : ThemeParkIncScienceTestBase
{
	private static ThemeParkIncCareer Career() => new( new TestCareerFiles(), TestCareerFiles.ShippedFiringLines );

	private static readonly ThemeParkIncAdviceContext Default = new( 0, false, false, 0, 0 );

	private static ThemeParkIncAdvisorSettings Settings( string text = "" ) =>
		ThemeParkIncAdvisorSettings.Bind( System.Text.Encoding.ASCII.GetBytes( "GeneralAdvisor.MinScoreForConsideration 10\n" + text ) );

	private sealed class AdviceWorld( bool met ) : IThemeParkIncAdviceWorld
	{
		public bool MissionRequirementsMet => met;
	}

	private static ThemeParkIncAdviceRecord Pending( ParkEconomy economy, int message ) => economy.TpiAdvisor!.Queue.Single( slot => slot.Valid && slot.Message == message );

	private static void Played( ThemeParkIncCareer career, int message ) => career.SetAdviceHistory( message, career.AdviceHistory( message ) with { Played = true } );

	[TestMethod]
	public void TheRequirementsMetAdviceScoresTwiceItsWordUntilPlayed()
	{
		_ = ScienceEconomy();
		var settings = ThemeParkIncAdvisorSettings.Load();
		var word = settings.Word( ThemeParkIncAdvisor.RequirementsScoreOffset );
		var career = Career();
		var advisor = new ThemeParkIncAdvisor( settings, ThemeParkIncAdvisorTable.Shipped ) { World = new AdviceWorld( true ) };
		foreach ( var message in new[] { 0x1C7, 0x1E3 } )
		{
			Assert.IsTrue( ThemeParkIncAdvisor.Scores( ThemeParkIncAdvisorTable.Shipped.Find( message )!.Value ), $"{message:X}" );
			Assert.AreEqual( unchecked( word << 1 ), advisor.Score( message, Default, career ), $"0x726FA0/0x726F50: twice the scorer word +0x7B8 for {message:X}" );
			advisor.World = new AdviceWorld( false );
			Assert.AreEqual( 0, advisor.Score( message, Default, career ), "the requirements not met (0x55A620)" );
			advisor.World = new AdviceWorld( true );
			Played( career, message );
			Assert.AreEqual( 0, advisor.Score( message, Default, career ), "played (0x71B520)" );
		}
		Assert.ThrowsException<InvalidOperationException>( () => new ThemeParkIncAdvisor( settings, ThemeParkIncAdvisorTable.Shipped ).Score( 0x1C7, Default, Career() ),
			"the callback reads the mission's requirements" );
	}

	[TestMethod]
	public void TheLakeAdviceScoresOnlyInAModeZeroFlightUntilPlayed()
	{
		var career = Career();
		var advisor = new ThemeParkIncAdvisor( Settings(), ThemeParkIncAdvisorTable.Shipped );
		var flight = new ThemeParkIncAdviceContext( 0, false, false, 30, 0 );
		Assert.IsTrue( ThemeParkIncAdvisor.Scores( ThemeParkIncAdvisorTable.Shipped.Find( 0x26D )!.Value ) );
		Assert.AreEqual( 10_000, advisor.Score( 0x26D, flight, career ), "0x729020: 10000 until played" );
		Assert.AreEqual( -1, advisor.Score( 0x26D, new ThemeParkIncAdviceContext( 0, false, false, 30, 1 ), career ), "class 6: a flight of mode 0 only" );
		Assert.AreEqual( -1, advisor.Score( 0x26D, Default, career ) );
		Played( career, 0x26D );
		Assert.AreEqual( 0, advisor.Score( 0x26D, flight, career ) );
	}

	[DataTestMethod]
	[DataRow( 0, 0x20F )]
	[DataRow( 1, 0x1C7 )]
	[DataRow( 2, 0x1E3 )]
	[DataRow( 17, 0x1E3 )]
	public void TheStatusRefreshPicksTheCountersAdvice( int counter, int expected ) => Assert.AreEqual( expected, ParkEconomy.StatusAdvice( counter ) );

	[TestMethod]
	public void TheStatusRefreshPostsNothingWhileTheRequirementsAreNotMet()
	{
		var economy = ScienceEconomy();
		if ( economy.ThemeParkIncMissionMet )
			Assert.Inconclusive( "The Science park's first mission is met at the start." );
		economy.ThemeParkIncMissionStatusAdvice();
		Assert.IsFalse( economy.TpiAdvisor!.Queue.Any( slot => slot.Valid && slot.Message is 0x20F or 0x1C7 or 0x1E3 ), "0x54D660: 0x55A620 false posts nothing" );
	}

	[DataTestMethod]
	[DataRow( 0, false, false, 0x58 )]
	[DataRow( 1, true, true, 0x5A )]
	[DataRow( 2, false, false, 0x5B )]
	[DataRow( 3, false, false, 0x5C )]
	[DataRow( 4, true, false, 0x59 )]
	[DataRow( 4, false, true, 0x176 )]
	[DataRow( 4, false, false, 0x18C )]
	[DataRow( 5, false, false, -1 )]
	[DataRow( 6, false, false, 0x11B )]
	[DataRow( 7, false, false, -1 )]
	[DataRow( -1, false, false, -1 )]
	public void TheResearchCompletionAdviceFollowsTheCategory( int category, bool upgrader, bool played18C, int expected ) =>
		Assert.AreEqual( expected, ParkEconomy.ResearchAdvice( category, upgrader, played18C ) ?? -1 );

	[TestMethod]
	public void AResearchCompletionPostsItsAdviceWithTheObjectAsLocateData()
	{
		var economy = ScienceEconomy();
		economy.ThemeParkIncResearchAdvice( 1160, 0 );
		var record = Pending( economy, 0x58 );
		Assert.AreEqual( ((ushort)0, 1160, economy.TpiAdvisor!.Settings.Word( 0x468 ), false), (record.Locate, record.LocateData, record.Score, record.ExplicitScore),
			"0x7194DA: locate 0, the object, the field score" );
		economy.ThemeParkIncResearchAdvice( 1160, 4 );
		Assert.IsTrue( economy.TpiAdvisor.Queue.Any( slot => slot.Valid && slot.Message == 0x18C ), "no mechanic of ability 1 and 0x18C unplayed" );
		economy.ThemeParkIncResearchAdvice( 1160, 5 );
		Assert.AreEqual( 2, economy.TpiAdvisor.Queue.Count( slot => slot.Valid ), "category 5 posts nothing" );
	}

	[DataTestMethod]
	[DataRow( ThemeParkIncStaffRole.Mechanic, 2, 2, 0x22 )]
	[DataRow( ThemeParkIncStaffRole.Handyman, 2, 2, 0x23 )]
	[DataRow( ThemeParkIncStaffRole.Guard, 2, 3, 0x1DF )]
	[DataRow( ThemeParkIncStaffRole.Entertainer, 2, 4, 0x1D4 )]
	[DataRow( ThemeParkIncStaffRole.Gardener, 2, 1, 0x2D )]
	[DataRow( ThemeParkIncStaffRole.Researcher, 2, 0, 0x2C )]
	[DataRow( ThemeParkIncStaffRole.Entertainer, 3, 0, 0x31 )]
	[DataRow( ThemeParkIncStaffRole.Handyman, 5, 0, 0x35 )]
	[DataRow( ThemeParkIncStaffRole.Mechanic, 4, 0, -1 )]
	public void TheStrikeAdviceIsTheReasonsOrTheKindsPlusTheRolesWord( ThemeParkIncStaffRole role, int kind, int reason, int expected ) =>
		Assert.AreEqual( expected, ParkEconomy.StrikeAdvice( new ThemeParkIncStaff.StrikeMessage( role, kind, reason ) ) ?? -1 );

	[TestMethod]
	public void AStrikeMessagePostsItsAdvice()
	{
		var economy = ScienceEconomy();
		economy.ThemeParkIncStrikeMessage( new ThemeParkIncStaff.StrikeMessage( ThemeParkIncStaffRole.Guard, 2, 3 ) );
		var record = Pending( economy, 0x1DF );
		Assert.AreEqual( (economy.TpiAdvisor!.Settings.Word( 0x330 ), (ushort)0, 0), (record.Score, record.Locate, record.LocateData), "0x7192C1: the field score, locate 0" );
	}

	[DataTestMethod]
	[DataRow( 39, 2f, 40f, true )]
	[DataRow( 38, 2f, 40f, true )]
	[DataRow( 37, 2f, 40f, false )]
	[DataRow( 40, 2f, 40f, false )]
	[DataRow( 39, 0f, 40f, false )]
	[DataRow( 0, 0.5f, 0.25f, true )]
	public void TheImminentAdviceNeedsTheRepairByteToCrossItsLine( int repair, float wear, float imminent, bool expected ) =>
		Assert.AreEqual( expected, ParkEconomy.CrossedImminentLine( repair, wear, imminent ) );

	[TestMethod]
	public void TheImminentLineComparesAtSinglePrecisionAndAnUnorderedLineNeverPosts()
	{
		// 0x5E2791: fild b, fadd the single wear: the sum is a single at the 24-bit control word, so 39 + 0.99999994f rounds to 40
		Assert.IsTrue( ParkEconomy.CrossedImminentLine( 39, 0.99999994f, 40f ) );
		Assert.IsFalse( ParkEconomy.CrossedImminentLine( 39, 1f, float.NaN ), "an unordered fcomp sets C0: below, so no post" );
	}

	/// <summary>A Robot Swing (Info.Id 1101) built in the economy.</summary>
	private static ParkObjectState Ride( ParkEconomy economy )
	{
		economy.TryBuild( 1101, out var item );
		Assert.IsNotNull( item );
		return item!;
	}

	[TestMethod]
	public void ABreakdownPostsTheMechanicsHqsAdviceAndACondemnedRideTheAdvisors()
	{
		var economy = ScienceEconomy();
		var ride = Ride( economy );
		var staff = economy.TpiStaff!;
		if ( staff.OfRole( ThemeParkIncStaffRole.Mechanic ).Any() )
			Assert.Inconclusive( "The Science park starts with mechanics." );
		economy.ThemeParkIncRideStateAdvice( ride, RideObjectStates.BrokenDown );
		Assert.AreEqual( (ushort)ride.InfoId, (ushort)Pending( economy, 0x41 ).LocateData, "0x5DAEF9: no mechanic: 0x41 with the ride's type" );
		economy.ThemeParkIncRideStateAdvice( ride, RideObjectStates.Condemned );
		Assert.AreEqual( (0, economy.TpiAdvisor!.Settings.Word( 0x6EC )), (Pending( economy, 0xDB ).LocateData, Pending( economy, 0xDB ).Score), "0x71A3AE: the ride as locate word" );

		var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).FirstOrDefault( index => staff.Slots[index]?.Role == ThemeParkIncStaffRole.Mechanic, -1 );
		if ( slot < 0 )
			Assert.Inconclusive( "The Science pool holds no mechanic." );
		Assert.IsTrue( staff.BeginHire( slot ) );
		var mechanic = staff.EndHire( slot, placed: true )!;
		mechanic.State = 0;
		economy.ThemeParkIncRideStateAdvice( ride, RideObjectStates.BrokenDown );
		Assert.IsFalse( economy.TpiAdvisor.Queue.Any( item => item.Valid && item.Message == 0x42 ), "an idle mechanic without a ride: nothing" );
		mechanic.State = 4;
		economy.ThemeParkIncRideStateAdvice( ride, RideObjectStates.BrokenDown );
		Assert.IsTrue( economy.TpiAdvisor.Queue.Any( item => item.Valid && item.Message == 0x42 ), "a striker (display state 4) does not come: 0x42" );
		economy.CancelThemeParkIncAdvice( 0x42 );
		mechanic.RideId = ride.Id;
		economy.ThemeParkIncRideStateAdvice( ride, RideObjectStates.BrokenDown );
		Assert.IsFalse( economy.TpiAdvisor.Queue.Any( item => item.Valid && item.Message == 0x42 ), "a mechanic going to the ride: nothing" );
	}

	[TestMethod]
	public void AMechanicsClaimPostsAZeroScoreAdvice()
	{
		var economy = ScienceEconomy();
		var ride = Ride( economy );
		economy.ThemeParkIncMechanicClaimAdvice( ride );
		var record = Pending( economy, 0x43 );
		Assert.AreEqual( (0, (ushort)ride.InfoId), (record.Score, (ushort)record.LocateData), "0x5DA4AD: the constant 0 never plays" );
	}

	[DataTestMethod]
	[DataRow( 1160, 0x21F )]
	[DataRow( 1180, 0x1E7 )]
	[DataRow( 1110, 0x1E7 )]
	public void ASpecialRideReadyToActivateNamesItsAdvice( int infoId, int expected ) => Assert.AreEqual( expected, ParkEconomy.ActivationAdvice( infoId ) );

	[TestMethod]
	public void TheBanksRedSixMonthsAdviceIsPostedBeforeItsMail()
	{
		var economy = ScienceEconomy();
		Assert.IsTrue( economy.PostThemeParkIncAdvice( 0x66 ) );
		Assert.AreEqual( (economy.TpiAdvisor!.Settings.Word( 0x4C4 ), false), (Pending( economy, 0x66 ).Score, Pending( economy, 0x66 ).ExplicitScore), "0x71912A: the field score" );
		Assert.IsFalse( economy.PostThemeParkIncAdvice( 0x66 ), "its limit is 1" );
	}

	[TestMethod]
	public void TheCaptionsFormattersFileARowOnlyWithTheirLocateWords()
	{
		foreach ( var (tag, formatter) in new[] { (0, 0), (5, 0), (6, 2), (8, 2), (9, 2), (0xE, 3), (0x13, 3), (0x20, 0), (0x2B, 0), (0x69, 2) } )
			Assert.AreEqual( formatter, ThemeParkIncAdviceTag.For( tag )!.Value.Formatter, $"tag {tag}" );
		var thing = ThemeParkIncAdviceTag.For( 6 )!.Value;
		Assert.IsTrue( thing.Files( 31, 0 ) );
		Assert.IsFalse( thing.Files( 0, 1160 ), "0x6365EC: no thing, no row" );
		var type = ThemeParkIncAdviceTag.For( 0xF )!.Value;
		Assert.IsTrue( type.Files( 0, 1160 ) );
		Assert.IsFalse( type.Files( 31, 0x10000 ), "0x63665F: the low word of the locate dword is 0" );
		Assert.IsNull( ThemeParkIncTagLetter.Advice( new ThemeParkIncAdviceBroadcast( 6, 0, 1160 ), 0 ) );
		var row = ThemeParkIncTagLetter.Advice( new ThemeParkIncAdviceBroadcast( 8, 31, 1160 ), 0 )!;
		Assert.AreEqual( (8, 6, 0x13, 31, 1160, 2), (row.Tag, row.Kind, row.GotoKind, row.Key, row.GotoArgument, row.Category) );
	}

	[TestMethod]
	public void ARepairsStartFlagsTheRidesNoticesOnly()
	{
		var tube = new ThemeParkIncTagTube();
		// a tag-8 row (kind 6) supersedes an unflagged tag-6 row of the same type (0x636880): the types differ here
		foreach ( var (tag, key, type) in new[] { (6, 31, 1160), (8, 31, 1180), (9, 32, 1190), (0xF, 31, 1160) } )
			tube.Insert( ThemeParkIncTagLetter.Advice( new ThemeParkIncAdviceBroadcast( tag, (ushort)key, type ), 0 )!, 0, load: false );
		Assert.AreEqual( 4, tube.Listed.Count() );
		Assert.AreEqual( 2, tube.DeleteTagged( ParkEconomy.RideNoticeTags, 31 ), "0x636D10(4, {6, 7, 8, 9}, ride): tags 6 and 8 of the ride" );
		Assert.AreEqual( 2, tube.Listed.Count() );
		tube.Insert( ThemeParkIncTagLetter.Advice( new ThemeParkIncAdviceBroadcast( 6, 33, 1180 ), 0 )!, 0, load: false );
		tube.Insert( ThemeParkIncTagLetter.Advice( new ThemeParkIncAdviceBroadcast( 8, 34, 1180 ), 0 )!, 0, load: false );
		Assert.AreEqual( 3, tube.Listed.Count(), "the tag-8 row (kind 6, go-to 0x13) supersedes the tag-6 row of its type whatever the key" );
	}

	[TestMethod]
	public void AChallengeWithoutItsSendersAdviceIsRefusedWhenItsFileLoads()
	{
		static SamSettings Sam( string text ) => new( new[] { SamDocument.Parse( text, "test" ) } );
		SamSettings Standard() => Sam( "Challenges.DaysBetweenOffers 10\nChallengesInThisLevel[0].ChallengeType 3\n" );
		SamSettings Challenges( string sender ) => Sam( $"Challenges[3].Type 1\n{sender}" );
		Assert.AreEqual( 5, ThemeParkIncChallengeSettings.Read( Standard(), Challenges( "Challenges[3].Sender 5\n" ) )!.Rows[3].Sender );
		foreach ( var sender in new[] { "", "Challenges[3].Sender 0\n", "Challenges[3].Sender 1\n", "Challenges[3].Sender 7\n", "Challenges[3].Sender 9\n" } )
			Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncChallengeSettings.Read( Standard(), Challenges( sender ) ), $"'{sender.Trim()}'" );
		Assert.IsNotNull( ThemeParkIncChallengeSettings.Read( Standard(), Sam( "Challenges[3].Sender 0\n" ) ), "a row without a type is no challenge" );
	}

	[TestMethod]
	public void TheSettingsAreTheirWords()
	{
		var a = Settings( "HelpMessage.Score 1000\n" );
		var b = Settings( "HelpMessage.Score 1000\n" );
		var c = Settings( "HelpMessage.Score 999\n" );
		Assert.AreEqual( a, b );
		Assert.AreEqual( a.GetHashCode(), b.GetHashCode() );
		Assert.AreNotEqual( a, c );
		Assert.AreEqual( 10, a.MinimumScore );
		Assert.AreEqual( new ThemeParkIncAdvisorGroup( 7, 0, 0, 0, true, 2 ),
			Settings( "MessageGroups[3].MinTimeSameMessage1 7\nMessageGroups[3].SayOnlyOnce 1\nMessageGroups[3].DiscardAfterSlaps 2\n" ).Groups[3], "the groups are read from the words" );
	}
}
