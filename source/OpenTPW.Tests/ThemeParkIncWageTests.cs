using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's wage demands, the wage negotiation, the dismissal and the strikes in the park (docs/reverse/TPI-WAGES.md): the decision 0x559E00, the demand row 0x5CEFB0 and its go-to 0x5511B0,
/// the wage demand window 0x59A900 and the results dialog 0x559860, the dismissal 0x629040 and the strike states 4 and 5. The tests with the park need OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncWageTests
{
	private BaseFileSystem? previous;
	private Logger? previousLog;

	[TestInitialize]
	public void Initialize() => previous = FileSystem;

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous!;
		if ( previousLog != null )
			Log = previousLog;
		previousLog = null;
		GameEditions.Select( GameEdition.ThemeParkWorld );
	}

	[TestMethod]
	public void TheDecisionComparesTheSumsThenTheSingleRatioWithTheBadWagePercentage()
	{
		// o >= d accepts the demand
		Assert.AreEqual( ThemeParkIncWageNegotiation.Accepted, ThemeParkIncWageNegotiation.Decide( 100, 50, 50, 10 ) );
		Assert.AreEqual( ThemeParkIncWageNegotiation.Accepted, ThemeParkIncWageNegotiation.Decide( 100, 60, 50, 10 ) );
		// 180 / 200 × 100 = 90, exactly 100 - 10: a grudging yes; 179 / 200 × 100 = 89.5 is below
		Assert.AreEqual( ThemeParkIncWageNegotiation.Grudging, ThemeParkIncWageNegotiation.Decide( 100, 80, 100, 10 ) );
		Assert.AreEqual( ThemeParkIncWageNegotiation.Refused, ThemeParkIncWageNegotiation.Decide( 100, 79, 100, 10 ) );
		// an offer of 0 (the close and cancel buttons) is the salary alone
		Assert.AreEqual( ThemeParkIncWageNegotiation.Grudging, ThemeParkIncWageNegotiation.Decide( 900, 0, 100, 10 ) );
		Assert.AreEqual( ThemeParkIncWageNegotiation.Refused, ThemeParkIncWageNegotiation.Decide( 899, 0, 100, 10 ) );
		// the quotient rounds to a single before the product: 2 / 3 × 100 is 66.66667f, below 67
		Assert.AreEqual( ThemeParkIncWageNegotiation.Refused, ThemeParkIncWageNegotiation.Decide( 0, 2, 3, 33 ) );
		Assert.AreEqual( ThemeParkIncWageNegotiation.Grudging, ThemeParkIncWageNegotiation.Decide( 0, 2, 3, 34 ) );
		Assert.AreEqual( 2f / 3f, ThemeParkIncWageNegotiation.SingleQuotient( 2, 3 ) );
		Assert.AreEqual( -0.5f, ThemeParkIncWageNegotiation.SingleQuotient( -1, 2 ) );
	}

	[TestMethod]
	public void TheLayoutTablesHoldTheWageWindowAndTheNegotiationDialog()
	{
		var demand = ThemeParkIncLayoutTables.WageDemand;
		Assert.AreEqual( 0x9C9F64, demand.Address );
		Assert.AreEqual( "varibox", demand.Control( 0xCA185 ).Model );
		Assert.AreEqual( 5, demand.Control( 0xCA185 ).Children.Single( child => child.Id == 3 ).Type, "the value field of layout command 19" );
		Assert.AreEqual( "b_fire", demand.Control( 0xCA183 ).Model );
		Assert.AreEqual( "b_cancel", demand.Control( 0xFFFFFFFE ).Model );
		var dialog = ThemeParkIncLayoutTables.WageNegotiation;
		Assert.AreEqual( 0x9BE518, dialog.Address );
		Assert.AreEqual( "checkbox2", dialog.Control( 0xB28D ).Model );
		Assert.AreEqual( "checkbox2", dialog.Control( 0xB28E ).Model );
		Assert.AreEqual( "b_okay", dialog.Control( 0xB28B ).Model );
	}

	[TestMethod]
	public void TheTubeDeletesByTagAndKeyAndByKey()
	{
		var tube = new ThemeParkIncTagTube();
		tube.Insert( ThemeParkIncTagLetter.WageDemand( 0xB, 40, 20, 0 ), 0, load: false );
		tube.Insert( ThemeParkIncTagLetter.WageDemand( 0xB, 41, 30, 0 ), 0, load: false );
		tube.Insert( ThemeParkIncTagLetter.Introduction( 0 ), 0, load: false );
		Assert.AreEqual( 1, tube.DeleteTagged( ThemeParkIncTagLetter.WageDemandTag, 40 ) );
		Assert.AreEqual( 0, tube.DeleteTagged( ThemeParkIncTagLetter.WageDemandTag, 40 ), "a flagged row is not flagged again" );
		Assert.AreEqual( 1, tube.DeleteKeyed( 41 ) );
		tube.Update( 0 );
		Assert.AreEqual( ThemeParkIncLetterSource.Introduction, tube.Listed.Single().Source );
	}

	[TestMethod]
	public void TheWageWindowNegotiatesTheOfferAZeroOfferOrFiresAndTheDialogAppliesOnClose()
	{
		var settings = Settings();
		var member = new ThemeParkIncStaffMember( 1, ThemeParkIncStaffRole.Handyman, 0, 1000, 100, 0, 0 );
		var events = new List<string>();
		ThemeParkIncWageDemandWindow Open() => new( Strings(), settings, member, 40, () => events.Add( "closed" ), ( _, demand, offer ) => events.Add( $"negotiate {demand} {offer}" ),
			_ => events.Add( "fired" ) );
		var window = Open();
		window.Build();
		Assert.AreEqual( 40, window.Offer, "the offer starts at the demand (+0x14 = +0x18)" );
		window.Step( 1 );
		Assert.AreEqual( 41, window.Offer, "the plus button steps by WageStep" );
		window.PressOkay();
		CollectionAssert.AreEqual( new[] { "closed", "negotiate 40 41" }, events );
		events.Clear();
		window = Open();
		window.Build();
		window.PressClose();
		CollectionAssert.AreEqual( new[] { "closed", "negotiate 40 0" }, events, "the close and cancel buttons offer 0" );
		events.Clear();
		window = Open();
		window.Build();
		window.PressFire();
		CollectionAssert.AreEqual( new[] { "fired", "closed" }, events );
		events.Clear();
		window = Open();
		window.Build();
		window.Close();
		CollectionAssert.AreEqual( new[] { "closed" }, events, "the HUD's close and Escape negotiate nothing" );

		var finished = new List<(int, ThemeParkIncWageNegotiation.Choice)>();
		var dialog = new ThemeParkIncWageNegotiationWindow( Strings(), settings, member, 100, 0, () => events.Add( "dialog closed" ), ( result, choice ) => finished.Add( (result, choice) ) );
		var screen = dialog.Build();
		Assert.AreEqual( ThemeParkIncWageNegotiation.Refused, dialog.Result, "100 / 200 × 100 is below 90" );
		Assert.IsTrue( screen.Elements.Any( element => element.Id == "letThemLeave" ), "a refused offer shows the two choices" );
		StringAssert.Contains( dialog.Body, "100", "string 0x101 carries the demand" );
		dialog.Choice = ThemeParkIncWageNegotiation.Choice.LetThemLeave;
		dialog.Close();
		dialog.Close();
		CollectionAssert.AreEqual( new[] { (ThemeParkIncWageNegotiation.Refused, ThemeParkIncWageNegotiation.Choice.LetThemLeave) }, finished, "the close applies the decision once" );
		var yes = new ThemeParkIncWageNegotiationWindow( Strings(), settings, member, 10, 10, () => { }, ( _, _ ) => { } );
		Assert.IsFalse( yes.Build().Elements.Any( element => element.Id == "acceptDemand" ), "a yes hides the choices" );
		Assert.AreEqual( "ui255", yes.Body );
	}

	[TestMethod]
	public void ADemandFilesItsRowClosesTheOlderOneAndTheGotoFindsTheMember()
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		var member = Hire( economy, world, ThemeParkIncStaffRole.Handyman, (40, 9) );
		var older = ThemeParkIncTagLetter.WageDemand( 0xB, member.ThingId, 5, economy.Turn );
		economy.TpiTagTube!.Insert( older, (uint)economy.Turn, load: false );
		economy.FileThemeParkIncWageDemands( new[] { new ThemeParkIncStaff.WageDemand( member, 0xB, 25 ) } );
		Assert.IsTrue( older.Flagged, "0x636C90(0x90, handle) closes the member's older demand" );
		var row = economy.TpiTagTube.Listed.First();
		Assert.AreEqual( (ThemeParkIncLetterSource.WageDemand, 0x90, 0x97, 0xE, 25, member.ThingId, 2, 5, false, 0),
			(row.Source, row.Tag, row.Kind, row.GotoKind, row.GotoArgument, row.Key, row.Category, row.Picture, row.Read, row.ExpiryUnits) );
		Assert.AreEqual( (10_000, false), economy.TpiAdvisor!.Queue.Where( slot => slot.Valid && slot.Message == ParkEconomy.ThemeParkIncWageDemandAdvice ).Select( slot => (slot.Score, slot.ExplicitScore) ).Single(),
			"0x628D93: the advice 0x1C8 with its descriptor's score" );
		Assert.AreSame( member, economy.ThemeParkIncMemberOfRow( row ), "the go-to 0xE opens the window on the row's member" );
		Assert.IsNull( economy.ThemeParkIncMemberOfRow( ThemeParkIncTagLetter.Introduction( 0 ) ) );

		// a raise closes the rows and sets the salary
		var salary = member.Salary;
		economy.EndThemeParkIncNegotiation( member, ThemeParkIncWageNegotiation.Grudging, 7, 25, ThemeParkIncWageNegotiation.Choice.AcceptDemand, null );
		Assert.AreEqual( salary + 7, member.Salary );
		Assert.IsTrue( row.Flagged );
		economy.EndThemeParkIncNegotiation( member, ThemeParkIncWageNegotiation.Refused, 0, 25, ThemeParkIncWageNegotiation.Choice.AcceptDemand, null );
		Assert.AreEqual( salary + 32, member.Salary, "a refused offer whose demand the player accepts raises by the demand" );
	}

	[TestMethod]
	public void ATrainingLetterIsAnUnreadRowWithTheStaffWindowGoto()
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		var member = Hire( economy, world, ThemeParkIncStaffRole.Handyman, (40, 9) );
		economy.FileThemeParkIncTrainingLetter( member, finished: false, 7 );
		var row = economy.TpiTagTube!.Listed.First();
		Assert.AreEqual( (ThemeParkIncLetterSource.Training, 0x8F, 0xD, 5, member.ThingId, 7, 0, false), (row.Source, row.Tag, row.Argument, row.GotoKind, row.Key, row.Ability, row.Category, row.Read) );
		var json = ParkSaveFile.Serialize( economy );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( 7, restored.TpiTagTube!.Listed.First().Ability );
		economy.DismissThemeParkIncMember( member, null );
		Assert.IsTrue( row.Flagged, "the dismissal deletes every row keyed by the member (0x636B10)" );
	}

	[TestMethod]
	public void TheDismissalPaysASalaryClearsTheOnlyStrikersSlotAndTakesTheMemberAway()
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		var staff = economy.TpiStaff!;
		var member = Hire( economy, world, ThemeParkIncStaffRole.Handyman, (40, 9) );
		var other = Hire( economy, world, ThemeParkIncStaffRole.Mechanic, (41, 9) );
		economy.TpiTagTube!.Insert( ThemeParkIncTagLetter.WageDemand( 0xA, member.ThingId, 30, economy.Turn ), 0, load: false );
		StartStrike( staff, ThemeParkIncStaffRole.Handyman );
		member.State = ThemeParkIncStaffWorld.Striking;
		var balance = economy.Balance;
		var destroyed = new List<ThemeParkIncStaffMember>();
		economy.DismissThemeParkIncMember( member, destroyed.Add );
		Assert.AreEqual( balance - member.Salary, economy.Balance, "one salary as severance" );
		Assert.AreEqual( new ThemeParkIncStaff.StrikeRecord( 0, false, staff.Strikes[0].Turn ), staff.Strikes[(int)ThemeParkIncStaffRole.Handyman], "0x634390: the only striker's role" );
		Assert.IsFalse( staff.Members.Contains( member ) );
		Assert.IsTrue( staff.Members.Contains( other ) );
		CollectionAssert.AreEqual( new[] { member }, destroyed, "the person's and the thing's destroy" );
		Assert.IsTrue( economy.TpiTagTube.Rows.All( row => row.Key != member.ThingId || row.Flagged ), "0x636B10 deletes the member's rows" );

		// a striker who is not the only one keeps the strike
		var first = Hire( economy, world, ThemeParkIncStaffRole.Gardener, (42, 9) );
		var second = Hire( economy, world, ThemeParkIncStaffRole.Gardener, (43, 9) );
		StartStrike( staff, ThemeParkIncStaffRole.Gardener );
		first.State = second.State = ThemeParkIncStaffWorld.Striking;
		economy.DismissThemeParkIncMember( first, null );
		Assert.IsTrue( staff.OnStrike( ThemeParkIncStaffRole.Gardener ) );
	}

	[TestMethod]
	public void AStrikerWalksToTheStrikeAreaWaitsThereAndWalksBackWhenItEnds()
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		var staff = economy.TpiStaff!;
		Assert.AreEqual( (48, 9, 6, 1), staff.Settings.StrikeArea, "Science's FixedItemInfo.StrikeArea*" );
		Assert.AreEqual( (47, 17), staff.Settings.EntranceA );
		// three rows, so the y draw's remainder matters (Science's SizeY is 1)
		SetStrikeArea( staff.Settings, (48, 9, 6, 3) );
		var member = Hire( economy, world, ThemeParkIncStaffRole.Handyman, (40, 9) );
		StartStrike( staff, ThemeParkIncStaffRole.Handyman );
		var turn = economy.Turn;
		var before = economy.World.State;
		world.Search( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.ToStrike, member.State, "the gate's strike branch" );
		Assert.AreEqual( 25, member.SpeedBonus, "0x60B3F0(1)" );
		var expected = new WorldRng( before );
		var x = 48 + (int)(expected.NextWord() % 6u);
		expected.NextWord();
		var y = 9 + (int)(expected.NextWord() % 3u);
		Assert.AreEqual( (x, y), (member.Navigation!.TargetX >> 16, member.Navigation.TargetY >> 16), "four world draws pick the cell" );
		for ( var step = 0; step < 2000 && member.State == ThemeParkIncStaffWorld.ToStrike; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.Striking, member.State );
		Assert.AreEqual( (x, y), member.Navigation.Cell, "he stands on the drawn cell" );
		Assert.AreEqual( ThemeParkIncStaffWorld.StrikeTemplate, member.Template );
		Assert.AreEqual( (int)ThemeParkIncStaffMember.DisplayState.OnStrike, ThemeParkIncStaffWorld.DisplayCategory( member ) );
		var state = economy.World.State;
		world.Update( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.Striking, member.State, "he stands while the strike runs" );
		Assert.AreNotEqual( state, economy.World.State, "one world draw a tick" );
		// the next month's consideration ends the strike (message 5)
		var messages = staff.ConsiderStrikes( turn + 1 );
		Assert.IsTrue( messages.Contains( new ThemeParkIncStaff.StrikeMessage( ThemeParkIncStaffRole.Handyman, 5, 0 ) ) );
		world.Update( economy, member, ++turn );
		Assert.IsTrue( member.State is ThemeParkIncStaffWorld.Walking or ThemeParkIncStaffWorld.ToRestArea, $"back to the entrance: state {member.State}" );
		Assert.AreEqual( (47, 17), (member.Navigation.TargetX >> 16, member.Navigation.TargetY >> 16) );
	}

	[TestMethod]
	public void AClosedEmptyParkEndsTheStrike()
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		var staff = economy.TpiStaff!;
		var member = Hire( economy, world, ThemeParkIncStaffRole.Handyman, (40, 9) );
		StartStrike( staff, ThemeParkIncStaffRole.Handyman );
		member.State = ThemeParkIncStaffWorld.Striking;
		economy.ClosePark();
		world.Update( economy, member, economy.Turn + 1 );
		Assert.IsFalse( staff.OnStrike( ThemeParkIncStaffRole.Handyman ) );
		Assert.IsTrue( economy.TpiAdvisor!.Queue.Any( slot => slot.Valid && slot.Message == 0x35 ), "the strike's end (role handyman, kind 5) posts 0x34 + 1 (0x719270)" );
		// 0x62EB10 refuses the entrance cells (47, 17) and (48, 17) of a closed park, so the striker cannot walk back: "couldn't find park entrance" and 0x628180(0)
		Assert.AreEqual( ThemeParkIncStaffWorld.Idle, member.State );
	}

	[TestMethod]
	public void AStrikerAndADemandSurviveASaveAndAnOlderSaveIsRefused()
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		var staff = economy.TpiStaff!;
		var member = Hire( economy, world, ThemeParkIncStaffRole.Handyman, (40, 9) );
		StartStrike( staff, ThemeParkIncStaffRole.Handyman );
		var turn = economy.Turn;
		world.Search( economy, member, ++turn );
		for ( var step = 0; step < 2000 && member.State == ThemeParkIncStaffWorld.ToStrike; step++ )
			world.Update( economy, member, ++turn );
		Assert.AreEqual( ThemeParkIncStaffWorld.Striking, member.State );
		economy.FileThemeParkIncWageDemands( new[] { new ThemeParkIncStaff.WageDemand( member, 0xA, 25 ) } );
		var json = ParkSaveFile.Serialize( economy );
		StringAssert.Contains( json, "\"TpiWages\"" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), economy.Settings, economy.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ), "the save round-trips exactly" );
		Assert.AreEqual( ThemeParkIncStaffWorld.Striking, restored.TpiStaff!.Members.Single().State );
		Assert.IsTrue( restored.TpiStaff.OnStrike( ThemeParkIncStaffRole.Handyman ) );
		Assert.AreEqual( ThemeParkIncLetterSource.WageDemand, restored.TpiTagTube!.Listed.First().Source );

		var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
		node.Remove( "TpiWages" );
		var older = Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( node.ToJsonString() ) ), economy.Settings, economy.Catalog ) );
		Assert.AreEqual( ParkSaveFile.ThemeParkIncWagesMissing, older.Message );
	}

	[TestMethod]
	public void AWorldSaveFromBeforeTheWagesIsRefusedBeforeItsAssetsAreLookedAt()
	{
		var envelope = ParkResumeSaveFileTests.Envelope();
		var economy = ScienceEconomy();
		var current = envelope with { Theme = "science", Economy = ParkSaveFile.Capture( economy ) };
		var node = System.Text.Json.Nodes.JsonNode.Parse( ParkResumeJson.Serialize( current ) )!.AsObject();
		node["Economy"]!.AsObject().Remove( "TpiWages" );
		node["AssetHashes"] = new System.Text.Json.Nodes.JsonObject();
		var refusal = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( node.ToJsonString() ) ) );
		Assert.AreEqual( ParkSaveFile.ThemeParkIncWagesMissing, refusal.Message );
	}

	/// <summary>Sets the role's strike record active: a forced grievance warns at one consideration and strikes at the next.</summary>
	private static void StartStrike( ThemeParkIncStaff staff, ThemeParkIncStaffRole role )
	{
		staff.StrikeForced = true;
		var turn = 20_000L;
		while ( !staff.OnStrike( role ) && turn < 20_100 )
			staff.ConsiderStrikes( turn++ );
		staff.StrikeForced = false;
		Assert.IsTrue( staff.OnStrike( role ) );
	}

	/// <summary>Staff settings with only the keys the wage windows read (MaxExperience, MinWage, MaxWage, WageStep, PercentageForBadWageDecision, as Standard.sam's).</summary>
	private static ThemeParkIncStaffSettings Settings()
	{
		var settings = (ThemeParkIncStaffSettings)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( ThemeParkIncStaffSettings ) );
		void Set( string name, object value ) => typeof( ThemeParkIncStaffSettings ).GetField( $"<{name}>k__BackingField",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( settings, value );
		Set( "MaxExperience", 100000 );
		Set( "MinWage", 1 );
		Set( "MaxWage", 500 );
		Set( "WageStep", 1 );
		Set( "PercentageForBadWageDecision", 10 );
		return settings;
	}

	private static UiStringTable Strings()
	{
		var ui = Enumerable.Range( 0, 1000 ).Select( index => $"ui{index}" ).ToArray();
		var help = Enumerable.Range( 0, 400 ).Select( index => $"help{index}" ).ToArray();
		return new UiStringTable( "English", ui, help, Array.Empty<string>(), Array.Empty<string>() );
	}

	private void UseRetailData()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		previousLog ??= Log;
		Log = new();
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
	}

	private ParkEconomy ScienceEconomy()
	{
		UseRetailData();
		var economy = ParkEconomyRuntime.ForThemeParkIncLevel( OriginalPark.Load( "science" ) ).Economy;
		// [EXT:tpi-park-open] the level opens the Theme Park Inc park at the start
		economy.OpenPark();
		return economy;
	}

	[TestMethod]
	public void AStrikeJoinWhoseRouteIsRefusedTakesItsFourDrawsKeepsTheMaskAndFallsThroughToTheRestBranch()
	{
		// the strike area on the entrance cell of a closed park: 0x62EB10 refuses before it writes +0x1A0
		var (economy, world, member) = JoinSetup( strike: true );
		var settings = economy.TpiStaff!.Settings;
		var before = economy.World.State;
		var state = member.State;
		Assert.IsFalse( world.StrikeWalk( economy, settings, member, economy.Turn + 1 ) );
		var expected = new WorldRng( before );
		for ( var draw = 0; draw < 4; draw++ )
			expected.NextWord();
		Assert.AreEqual( expected.State, economy.World.State, "four world draws" );
		Assert.AreEqual( 1, member.Navigation!.Mask, "+0x1A0 = 1 stays: the refusal comes before the mask's write" );
		Assert.AreEqual( state, member.State, "no state set" );

		// the search after the refused join is the search without a strike, four draws later
		var (strikeEconomy, strikeWorld, striker) = JoinSetup( strike: true );
		var (plainEconomy, plainWorld, plain) = JoinSetup( strike: false );
		for ( var draw = 0; draw < 4; draw++ )
			plainEconomy.World.NextWord();
		strikeWorld.Search( strikeEconomy, striker, strikeEconomy.Turn + 1 );
		plainWorld.Search( plainEconomy, plain, plainEconomy.Turn + 1 );
		Assert.AreEqual( plainEconomy.World.State, strikeEconomy.World.State );
		Assert.AreEqual( (plain.State, plain.BaseSpeed, plain.Navigation!.TargetX, plain.Navigation.TargetY), (striker.State, striker.BaseSpeed, striker.Navigation!.TargetX, striker.Navigation.TargetY) );
	}

	private (ParkEconomy Economy, ThemeParkIncStaffWorld World, ThemeParkIncStaffMember Member) JoinSetup( bool strike )
	{
		var economy = ScienceEconomy();
		var world = World( economy );
		var staff = economy.TpiStaff!;
		SetStrikeArea( staff.Settings, (47, 17, 1, 1) );
		var member = Hire( economy, world, ThemeParkIncStaffRole.Handyman, (40, 9) );
		if ( strike )
			StartStrike( staff, ThemeParkIncStaffRole.Handyman );
		economy.ClosePark();
		return (economy, world, member);
	}

	[TestMethod]
	public void TheMonthConsidersTheStrikesBeforeThePaydaysReviewTheWages()
	{
		var economy = ScienceEconomy();
		var staff = economy.TpiStaff!;
		var members = Enumerable.Range( 0, staff.Settings.MinStaffOfTypeForStrike ).Select( _ =>
		{
			var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).FirstOrDefault( index => staff.Slots[index]?.Role == ThemeParkIncStaffRole.Handyman, -1 );
			if ( slot < 0 )
				Assert.Inconclusive( "The Science pool holds too few cleaners." );
			staff.BeginHire( slot );
			return staff.EndHire( slot, placed: true )!;
		} ).ToList();
		// the first month boundary at which the park is old enough to strike
		var boundary = economy.Turn + 1;
		while ( ParkCalendar.FieldsAtTurn( boundary ).Month == ParkCalendar.FieldsAtTurn( boundary - 1 ).Month
			|| ThemeParkIncStaff.ParkAgeMonths( boundary ) < staff.Settings.MinMonthsForStrike )
			boundary++;
		economy.Advance( ParkCalendar.TickOfTurn( boundary - 1 ) - economy.Tick );
		Assert.AreEqual( boundary - 1, economy.Turn );
		// a motivation just above the threshold that the payday's review (a salary below the expected wage) takes below it
		foreach ( var member in members )
		{
			(member.Energy, member.Happiness, member.Motivation) = (100f, 100f, staff.Settings.MotivationPercentToStrike + 1);
			member.Salary = 0;
		}
		int StrikeAdvice() => economy.TpiAdvisor!.Queue.Count( slot => slot.Valid && slot.Message is >= 0x22 and <= 0x39 or >= 0x1D1 and <= 0x1D6 or >= 0x1DD and <= 0x1E2 );
		var before = StrikeAdvice();
		economy.Advance( ParkCalendar.TickOfTurn( boundary ) - economy.Tick );
		Assert.IsTrue( members.All( member => ((int)member.Motivation & 0xFF) <= staff.Settings.MotivationPercentToStrike ), "the payday's review lowered the motivation" );
		Assert.AreEqual( 0, staff.Strikes[(int)ThemeParkIncStaffRole.Handyman].Level, "the HQ (thing 1) considered the meters before the paydays: no grievance yet" );
		Assert.AreEqual( economy.Turn, staff.Strikes[(int)ThemeParkIncStaffRole.Handyman].Turn, "the month considered the role" );
		Assert.AreEqual( before, StrikeAdvice(), "no strike message, so no strike advice" );
	}

	[TestMethod]
	public void TheHudsCloseOfItsWindowAppliesAnOpenNegotiation()
	{
		var economy = ScienceEconomy();
		var staff = economy.TpiStaff!;
		var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).First( index => staff.Slots[index] != null );
		staff.BeginHire( slot );
		var member = staff.EndHire( slot, placed: true )!;
		var hud = BuildHud( economy );
		var salary = member.Salary;
		hud.OpenNegotiation( member, 10, 10 );
		Assert.IsNotNull( hud.NegotiationWindow );
		Assert.AreEqual( ThemeParkIncWageNegotiation.Accepted, hud.NegotiationWindow!.Result );
		// another window opening sends the registered one message 5 (0x5013C0): the dialog closes, and its message 0x14 applies the decision
		typeof( ThemeParkIncHud ).GetMethod( "CloseHudWindow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.Invoke( hud, null );
		Assert.IsNull( hud.NegotiationWindow );
		Assert.IsFalse( hud.Stack.Screens.Any( screen => screen.Name == ThemeParkIncWageNegotiationWindow.ScreenName ) );
		Assert.AreEqual( salary + 10, member.Salary );
	}

	[TestMethod]
	public void EnterIsOkInTheWageWindowAndNothingInTheDialog()
	{
		var settings = Settings();
		var member = new ThemeParkIncStaffMember( 1, ThemeParkIncStaffRole.Handyman, 0, 1000, 100, 0, 0 );
		var events = new List<string>();
		var window = new ThemeParkIncWageDemandWindow( Strings(), settings, member, 40, () => events.Add( "closed" ), ( _, demand, offer ) => events.Add( $"negotiate {demand} {offer}" ),
			_ => events.Add( "fired" ) );
		var screen = window.Build();
		screen.Focus( screen.Find( "fire" ) );
		screen.Accept!();
		CollectionAssert.AreEqual( new[] { "closed", "negotiate 40 40" }, events, "Enter is command -1, OK, whatever the pointer rests on (0x59A9F6)" );
		var dialog = new ThemeParkIncWageNegotiationWindow( Strings(), settings, member, 100, 0, () => events.Add( "dialog closed" ), ( _, _ ) => events.Add( "applied" ) );
		var dialogScreen = dialog.Build();
		dialogScreen.Accept!();
		Assert.IsFalse( dialog.Closed, "the dialog's procedure takes no command -1 (0x55999F)" );
		Assert.IsNull( dialogScreen.Focused, "no element starts focused" );
		Assert.AreEqual( 500, new ThemeParkIncWageDemandWindow( Strings(), settings, member, 900, () => { }, ( _, _, _ ) => { }, _ => { } ).Offer, "the box clamps the demand (0x85CDCB)" );
	}

	private static ThemeParkIncHud BuildHud( ParkEconomy economy )
	{
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
			new object[] { "science", Array.Empty<ObjectCatalogEntry>() }, null )!;
		var level = (Level)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		SetBacking( level, "Objects", new ParkObjects( catalog, new SandboxParkGrid(), new WorldSeed( 1 ) ) );
		SetBacking( level, "CellTool", new CellBuildTool() );
		SetBacking( level, "LastActionMessage", "" );
		var runtime = (ParkEconomyRuntime)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( ParkEconomyRuntime ) );
		SetBacking( runtime, "Economy", economy );
		SetBacking( level, "Park", runtime );
		var strings = new UiStringTable( "English", Enumerable.Range( 0, 1000 ).Select( index => $"ui{index}" ).ToArray(), Enumerable.Range( 0, 400 ).Select( index => $"help{index}" ).ToArray(),
			new string[1], new string[1], staffTypes: Enumerable.Repeat( "type", 12 ).ToArray(), staffNames: Enumerable.Range( 0, 6 ).Select( _ => Enumerable.Repeat( "name", 40 ).ToArray() ).ToArray() );
		return new ThemeParkIncHud( level, strings, new ThemeParkIncHudHost() );
	}

	private static void SetBacking( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( target, value );

	private static void SetStrikeArea( ThemeParkIncStaffSettings settings, (int X, int Y, int Width, int Height) area ) => SetBacking( settings, "StrikeArea", area );

	/// <summary>A 64 × 24 map with a path along y = 9 (x 30 to 60, through the strike area 48..53, which also has rows 10 and 11) and a path from (47, 9) down to the entrance (47, 17).</summary>
	private static ThemeParkIncStaffWorld World( ParkEconomy economy )
	{
		var grid = new GuestPathGrid( 64, 24 );
		for ( var x = 30; x <= 60; x++ )
			grid.SetPath( x, 9, true );
		for ( var x = 48; x <= 53; x++ )
			for ( var y = 10; y <= 11; y++ )
				grid.SetPath( x, y, true );
		for ( var y = 9; y <= 17; y++ )
			grid.SetPath( 47, y, true );
		economy.Litter.Cells = grid.Cells;
		economy.Litter.ThemeParkInc = true;
		return new( grid );
	}

	private static ThemeParkIncStaffMember Hire( ParkEconomy economy, ThemeParkIncStaffWorld world, ThemeParkIncStaffRole role, (int X, int Y) cell )
	{
		var staff = economy.TpiStaff!;
		var slot = Enumerable.Range( 0, ThemeParkIncStaff.PoolSlots ).FirstOrDefault( index => staff.Slots[index]?.Role == role, -1 );
		if ( slot < 0 )
			Assert.Inconclusive( $"The Science pool holds no {role}." );
		Assert.IsTrue( staff.BeginHire( slot ) );
		var member = staff.EndHire( slot, placed: true )!;
		world.Construct( economy, member, cell, null );
		return member;
	}
}
