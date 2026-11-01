using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>Costumes (hats by advice), contextual help and the clip timeline of the advisor; GPU-free.</summary>
[TestClass]
[DoNotParallelize]
public class AdvisorHelpTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

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
	public void CostumeTableParsesStrictly()
	{
		var table = AdvisorCostumeTable.Parse( "# c\ncostumes = [\n  { response = 1, hat = -1, hat2 = 0 },\n  { response = 8, hat = 13, hat2 = 14 },\n]\n" );
		Assert.AreEqual( new AdvisorCostume( -1, 0 ), table[1] );
		Assert.AreEqual( new AdvisorCostume( 13, 14 ), table[8] );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorCostumeTable.Parse( "costumes = [\n  { response = 1 },\n]\n" ) );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorCostumeTable.Parse( "costumes = [\n  { response = 1, hat = 5, hat2 = 0 },\n  { response = 1, hat = 6, hat2 = 0 },\n]\n" ), "duplicate" );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorCostumeTable.Parse( "costumes = [\n  { response = 1, hat = 5, hat2 = 0 },\n" ), "unclosed" );
	}

	[TestMethod]
	public void ShippedCostumesFollowTheAdviceCategory()
	{
		var table = AdvisorCostumeTable.Parse( File.ReadAllText( Shipped( AdvisorCostumeTable.RelativePath ) ) );
		var responses = AdvisorResponses.Parse( File.ReadAllText( Shipped( AdvisorResponses.RelativePath ) ) );
		Assert.IsTrue( table.Keys.All( responses.ContainsKey ), "every costume names a response" );
		Assert.AreEqual( new AdvisorCostume( -1, 0 ), table[1], "the welcome wears the level's theme costume" );
		Assert.AreEqual( new AdvisorCostume( 9, 0 ), table[2], "waiting visitors at the gate: the ticket hat" );
		var byHat = table.Values.GroupBy( costume => (costume.Hat, costume.SecondHat) ).ToDictionary( group => group.Key, group => group.Count() );
		Assert.AreEqual( 77, byHat[(5, 0)], "hard hat" );
		Assert.AreEqual( 47, byHat[(6, 0)], "security hat" );
		Assert.AreEqual( 58, byHat[(10, 0)], "research hat" );
		Assert.AreEqual( 32, byHat[(11, 12)], "bowler and bow tie" );
		Assert.AreEqual( 18, byHat[(13, 14)], "fast food hat and spatula" );
		Assert.AreEqual( AdvisorCostume.None, AdvisorCostumeTable.For( 0 ) );
		Assert.IsTrue( table.Values.All( costume => costume.Hat is -1 or (>= 5 and <= 13) ) );
	}

	/// <summary>The global advisor's nodes as the real model lays them out (names and attribute values are the model's own).</summary>
	private static AdvisorNodeMap GlobalMap()
	{
		var names = new[] { "Bug Head", "Right Eye", "Left Eye", "ShutEye R", "ShutEye L", "Mouth - Normal", "Mouth - Aah", "Mouth - Eee", "Mouth - Ooh", "Mouth - Sss",
			"Body", "Right Antennae", "Left Antennae", "Right hand", "Spatula", "Left Hand", "Ticket Hat", "Kiosk Hat", "Bowler Hat", "Security Hat", "Handyman Hat",
			"Research Hat", "Fast Food Hat", "Hardhat", "Bowler_BTie" };
		var attributes = new (uint, uint)[] { (0x401, 20), (0x401, 19), (0x411, 21), (0x401, 14), (0x411, 22), (0x411, 9), (0x411, 8), (0x411, 11), (0x411, 6), (0x411, 7), (0x411, 10), (0x411, 13), (0x411, 5), (0x411, 12) };
		return new AdvisorNodeMap( 29, 11, attributes, names.Select( ( name, index ) => (name, index) ).ToArray() );
	}

	private static string[] Shown( AdvisorNodeMap map, HashSet<int> hidden ) =>
		map.Meshes.Where( mesh => !hidden.Contains( mesh.Node ) && !Advisor.Mouths.Contains( mesh.Name ) ).Select( mesh => mesh.Name ).ToArray();

	[TestMethod]
	public void AttributeRecordsNameNodesFromTheHeaderOffset()
	{
		var map = GlobalMap();
		Assert.AreEqual( 23, map.FindNode( AdvisorAccessories.SelectableTypeFlag, 5 ), "value 5 is the hardhat" );
		Assert.AreEqual( 14, map.FindNode( AdvisorAccessories.SelectableTypeFlag, 14 ), "value 14 is the spatula" );
		Assert.AreEqual( -1, map.FindNode( AdvisorAccessories.SelectableTypeFlag, 99 ) );
		Assert.AreEqual( -1, map.FindNode( 0x100, 5 ), "the type mask must match a bit of the record's type" );
	}

	[TestMethod]
	public void NoCostumeShowsTheBodyAndTheAntennae()
	{
		var map = GlobalMap();
		var shown = Shown( map, AdvisorAccessories.HiddenMeshNodes( map, AdvisorCostume.None, AdvisorThemeCostume.None ) );
		CollectionAssert.AreEquivalent( new[] { "Bug Head", "Right Eye", "Left Eye", "Body", "Right Antennae", "Left Antennae", "Right hand", "Left Hand" }, shown );
	}

	[DataTestMethod]
	[DataRow( 5, "Hardhat" )]
	[DataRow( 6, "Security Hat" )]
	[DataRow( 7, "Handyman Hat" )]
	[DataRow( 8, "Kiosk Hat" )]
	[DataRow( 9, "Ticket Hat" )]
	[DataRow( 10, "Research Hat" )]
	public void AHatSelectorShowsOneHat( int selector, string hat )
	{
		var map = GlobalMap();
		var shown = Shown( map, AdvisorAccessories.HiddenMeshNodes( map, new AdvisorCostume( selector, 0 ), AdvisorThemeCostume.None ) );
		CollectionAssert.Contains( shown, hat );
		Assert.AreEqual( 1, shown.Count( name => name.EndsWith( "Hat", StringComparison.Ordinal ) || name == "Hardhat" ) );
		// Every hat but the kiosk one hides the antennae; the kiosk hat leaves them.
		Assert.AreEqual( selector == 8, shown.Contains( "Right Antennae" ) && shown.Contains( "Left Antennae" ) );
	}

	[TestMethod]
	public void TheBowlerBringsItsBowTieAndTheFastFoodHatTheSpatula()
	{
		var map = GlobalMap();
		var bowler = Shown( map, AdvisorAccessories.HiddenMeshNodes( map, new AdvisorCostume( 11, 12 ), AdvisorThemeCostume.None ) );
		CollectionAssert.IsSubsetOf( new[] { "Bowler Hat", "Bowler_BTie" }, bowler );
		var food = Shown( map, AdvisorAccessories.HiddenMeshNodes( map, new AdvisorCostume( 13, 14 ), AdvisorThemeCostume.None ) );
		CollectionAssert.IsSubsetOf( new[] { "Fast Food Hat", "Spatula" }, food );
		CollectionAssert.DoesNotContain( food, "Right hand", "the spatula replaces the right hand" );
		CollectionAssert.DoesNotContain( food, "Right Antennae" );
	}

	[TestMethod]
	public void TheThemeCostumeShowsTheLevelsAddOnsAndHidesItsSubOffs()
	{
		// A level advisor like the jungle's: pith helmet (attribute 4) on node 16.
		var map = new AdvisorNodeMap( 29, 14, new (uint, uint)[] { (0x411, 21), (0x411, 22), (0x411, 4), (0x411, 18) },
			new[] { ("Bug Head", 0), ("Body", 13), ("Right hand", 14), ("Left Hand", 15), ("Pith Helmet", 16), ("Mutant Eye", 17), ("Ticket", 5) } );
		var theme = new AdvisorThemeCostume( new[] { 4, 0, 0, 0 }, new[] { 19, 20, 0, 0 } );
		var shown = Shown( map, AdvisorAccessories.HiddenMeshNodes( map, new AdvisorCostume( -1, 0 ), theme ) );
		CollectionAssert.Contains( shown, "Pith Helmet" );
		CollectionAssert.DoesNotContain( shown, "Mutant Eye" );
		CollectionAssert.DoesNotContain( shown, "Ticket" );
		// The theme costume off (a fixed sequence's accessory bit) and a response without selector show no helmet.
		CollectionAssert.DoesNotContain( Shown( map, AdvisorAccessories.HiddenMeshNodes( map, new AdvisorCostume( -1, 0 ), theme, AdvisorAccessories.SuppressCostumeBit ) ), "Pith Helmet" );
		CollectionAssert.DoesNotContain( Shown( map, AdvisorAccessories.HiddenMeshNodes( map, AdvisorCostume.None, theme ) ), "Pith Helmet" );
	}

	[TestMethod]
	public void ThemeCostumeReadsTheLevelSettings()
	{
		var settings = new SamSettings( new[] { SamDocument.Parse( "ThemeAdvisorCostumes[0].AddOns\t\t\t3\nThemeAdvisorCostumes[1].AddOns\t\t\t16\nThemeAdvisorCostumes[0].SubOffs\t\t\t19\nThemeAdvisorCostumes[1].SubOffs\t\t\t20\n", "t.sam" ) } );
		var theme = AdvisorThemeCostume.From( settings );
		CollectionAssert.AreEqual( new[] { 3, 16, 0, 0 }, theme.AddOns.ToArray() );
		CollectionAssert.AreEqual( new[] { 19, 20, 0, 0 }, theme.SubOffs.ToArray() );
	}

	[TestMethod]
	public void BlinkTogglesSwapTheEyes()
	{
		var map = GlobalMap();
		var clip = new ModelAnimation
		{
			Duration = 75,
			Tracks = new[]
			{
				new ModelAnimationTrack { NodeIndex = 1, NodeFlagToggles = new short[] { -61, 65 } },
				new ModelAnimationTrack { NodeIndex = 3, NodeFlagToggles = new short[] { 0, 61, -65 } },
			},
		};
		bool EyeOpen( float tick ) => Shown( map, AdvisorAccessories.HiddenMeshNodes( map, AdvisorCostume.None, AdvisorThemeCostume.None, 0, clip, tick ) ).Contains( "Right Eye" );
		bool ShutEye( float tick ) => Shown( map, AdvisorAccessories.HiddenMeshNodes( map, AdvisorCostume.None, AdvisorThemeCostume.None, 0, clip, tick ) ).Contains( "ShutEye R" );
		Assert.IsTrue( EyeOpen( 10 ) );
		Assert.IsFalse( ShutEye( 10 ) );
		Assert.IsFalse( EyeOpen( 62 ), "blinking" );
		Assert.IsTrue( ShutEye( 62 ) );
		Assert.IsTrue( EyeOpen( 70 ) );
		Assert.IsFalse( ShutEye( 70 ) );
	}

	[TestMethod]
	public void ClipsPlayOneAfterTheOtherEachForItsOwnLength()
	{
		// Clip 14 is 30 ticks (1000 ms) but counts 0 in the budget table; clip 15 is 20 ticks.
		var words = Enumerable.Repeat( 15u, 20 ).ToArray();
		words[13] = 30;
		words[14] = 20;
		var durations = new AdvisorClipDurations( words );
		Assert.AreEqual( 0u, durations.Of( 14 ) );
		Assert.AreEqual( 1000u, durations.PlayOf( 14 ) );
		var sequence = new AdvisorSequence( new[] { 14, 3, 15 }, (int)(durations.Of( 3 ) + durations.Of( 15 )) );
		Assert.AreEqual( 1000u + 500 + 666, AdvisorSequences.PlayMilliseconds( sequence, durations ) );
		Assert.AreEqual( new AdvisorClipPosition( 14, 0, 0f ), AdvisorTimeline.At( sequence, durations, 0 ) );
		var inFirst = AdvisorTimeline.At( sequence, durations, 500 )!.Value;
		Assert.AreEqual( 14, inFirst.Clip );
		Assert.AreEqual( 15f, inFirst.Tick, 1e-3f );
		var second = AdvisorTimeline.At( sequence, durations, 1250 )!.Value;
		Assert.AreEqual( (3, 1), (second.Clip, second.Index) );
		Assert.AreEqual( 7.5f, second.Tick, 1e-3f );
		Assert.AreEqual( 15, AdvisorTimeline.At( sequence, durations, 1600 )!.Value.Clip );
		Assert.IsNull( AdvisorTimeline.At( sequence, durations, 1000 + 500 + 666 ), "the last clip's end hides the model" );
	}

	[TestMethod]
	public void FixedRowsKeepTheAccessoryBitOfEachClip()
	{
		var durations = new AdvisorClipDurations( Enumerable.Repeat( 30u, 20 ).ToArray() );
		var sequence = AdvisorSequences.Build( 17, 1000, durations, () => 0 );
		CollectionAssert.AreEqual( new[] { 17, 17, 17, 17, 18, 19, 20 }, sequence.Clips.ToArray() );
		CollectionAssert.AreEqual( new[] { 2, 2, 2, 2, 4, 9, 0x11 }, sequence.AccessoryBits!.ToArray() );
		Assert.IsNull( AdvisorSequences.Build( -1, 1000, durations, () => 0 ).AccessoryBits, "a generated sequence has none" );
	}

	// Help ---------------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void HelpTableParsesStrictly()
	{
		var rows = AdvisorHelpTable.Parse( "messages = [\n  { message = 188, response = 407, count = 1, group = 1, duplicates = 1, mode = 2 },\n]\n" );
		Assert.AreEqual( new AdvisorHelpMessage( 188, 407, 1, 1, 1, 2 ), rows.Single() );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorHelpTable.Parse( "messages = [\n  { message = 188 },\n]\n" ) );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorHelpTable.Parse( "messages = [\n  { message = 188, response = 407, count = 0, group = 1, duplicates = 1, mode = 2 },\n]\n" ) );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorHelpTable.Parse( "messages = [\n  { message = 1, response = 4, count = 1, group = 1, duplicates = 1, mode = 2 },\n  { message = 1, response = 4, count = 1, group = 1, duplicates = 1, mode = 2 },\n]\n" ), "duplicate" );
	}

	[TestMethod]
	public void ShippedHelpMessagesAreTheTutorialGroupWithAnExistingResponse()
	{
		var rows = AdvisorHelpTable.Parse( File.ReadAllText( Shipped( AdvisorHelpTable.RelativePath ) ) );
		var responses = AdvisorResponses.Parse( File.ReadAllText( Shipped( AdvisorResponses.RelativePath ) ) );
		Assert.AreEqual( 81, rows.Count, "82 context-help descriptors, minus 291 whose response is absent from the table" );
		Assert.IsTrue( rows.All( row => row.Group == 1 && responses.ContainsKey( row.FirstResponseId ) ) );
		Assert.AreEqual( 407, rows.Single( row => row.MessageId == 188 ).FirstResponseId, "the buy window's help" );
		Assert.AreEqual( 0, rows.Single( row => row.MessageId == 195 ).GameMode, "the loans help is for the full simulation only" );
		Assert.IsTrue( AdvisorUiHelp.Windows.Values.All( message => rows.Any( row => row.MessageId == message ) ), "every window's message is a help message" );
		Assert.IsTrue( AdvisorUiHelp.ModeHelpMessages.All( message => rows.Any( row => row.MessageId == message ) ) );
	}

	[TestMethod]
	public void DescriptorTableParsesStrictly()
	{
		var rows = AdvisorDescriptorTable.Parse( "messages = [\n  { message = 0, response = 1, count = 1, group = 0, duplicates = 1, mode = 2, scan = 0, callback = 0x59c8c0 },\n]\n" );
		Assert.AreEqual( new AdvisorDescriptorRow( 0, 1, 1, 0, 1, 2, false, 0x59C8C0 ), rows.Single() );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorDescriptorTable.Parse( "messages = [\n  { message = 0 },\n]\n" ) );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorDescriptorTable.Parse( "messages = [\n  { message = 1, response = 1, count = 1, group = 0, duplicates = 1, mode = 2, scan = 0, callback = 0x1 },\n]\n" ), "records are in message order" );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorDescriptorTable.Parse( "messages = [\n  { message = 0, response = 1, count = 0, group = 0, duplicates = 1, mode = 2, scan = 0, callback = 0x1 },\n]\n" ) );
		Assert.ThrowsException<InvalidDataException>( () => AdvisorDescriptorTable.Parse( "messages = [\n  { message = 0, response = 1, count = 1, group = 0, duplicates = 1, mode = 2, scan = 0, callback = 0x1 },\n" ), "unclosed" );
	}

	[TestMethod]
	public void ShippedDescriptorTableHasTheOriginals351RecordsAndAgreesWithTheBoundRows()
	{
		var rows = AdvisorDescriptorTable.Parse( File.ReadAllText( Shipped( AdvisorDescriptorTable.RelativePath ) ) );
		Assert.AreEqual( AdvisorDescriptorTable.Count, rows.Count );
		// [BIN:TPW-EXE:0x0076E300 descriptor table] the hand-bound event rows and the help rows are rows of the same table
		foreach ( var message in AdvisorTables.AllMessages )
		{
			var row = rows[message.MessageId];
			Assert.AreEqual( (message.FirstResponseId, message.ResponseCount, message.Group, message.MaximumPendingDuplicates, message.GameMode), (row.FirstResponseId, row.ResponseCount, row.Group, row.MaximumPendingDuplicates, row.GameMode), $"message {message.MessageId}" );
		}
		Assert.AreEqual( 351 - AdvisorTables.AllMessages.Count(), AdvisorTables.UnproducedMessages.Count );
		Assert.AreEqual( 82, rows.Count( row => row.Callback == 0x59E8C0 ), "the help messages share one score callback" );
		CollectionAssert.AreEquivalent( new[] { 0, 1, 2, 3, 4, 5, 9 }, rows.Select( row => (int)row.Group ).Distinct().ToArray() );
	}

	[TestMethod]
	public void EveryMessageGroupOfTheDescriptorTableIsInTheShippedSettings()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		var sam = gamePath == null ? null : Path.Combine( gamePath, "Data", "Advisor", "Advisor.sam" );
		if ( sam == null || !File.Exists( sam ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Advisor.sam." );
		var balance = AdvisorBalance.Parse( File.ReadAllBytes( sam ), sam );
		Assert.AreEqual( AdvisorDescriptorTable.Count, balance.Descriptors().Count(), "every descriptor of the table is bound with the group's controls" );
	}

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
		""";

	private static AdvisorController Controller() => new( AdvisorBalance.Parse( Encoding.ASCII.GetBytes( Sam ), "test.sam" ) );

	[TestMethod]
	public void HelpIsPostedAtTheHelpScoreAndSaidOnce()
	{
		var controller = Controller();
		Assert.AreEqual( AdvisorEligibility.Eligible, controller.Advise( 188, 100, 0, true )!.Value.Eligibility );
		var played = new List<int>();
		controller.Update( () => 1000, () => 100, response => { played.Add( response ); return 4000; } );
		CollectionAssert.AreEqual( new[] { 407 }, played );
		Assert.AreEqual( AdvisorEligibility.AlreadyPlayed, controller.Advise( 188, 100, 0, true )!.Value.Eligibility, "tutorial group: once" );
		Assert.AreEqual( AdvisorEligibility.TutorialDisabled, controller.Advise( 189, 100, 0, false )!.Value.Eligibility );
	}

	[TestMethod]
	public void HelpRespectsTheGameMode()
	{
		var controller = Controller();
		Assert.IsNotNull( controller.Advise( 195, 100, 0, true ), "loans help: full simulation" );
		Assert.IsNull( Controller().Advise( 195, 100, 2, true ), "not in Instant Action" );
		Assert.IsNull( controller.Advise( 99999, 100, 0, true ), "no descriptor" );
	}

	[TestMethod]
	public void HelpScoresHelpMessageScoreAndTheReplayContextHelpScore()
	{
		var controller = Controller();
		Assert.AreEqual( 1000, controller.Balance.ScoreOf( 188 ), "the help descriptors' callback 0x59E8C0 reads +0x328, HelpMessage.Score" );
		Assert.AreEqual( 1500, controller.Balance.ContextHelpScore, "the F1 replay reads +0x32C, HelpMessage.ContextHelpScore" );
	}

	[TestMethod]
	public void EveryTutorialRecordOfferedIsKeptAdmittedOrNot()
	{
		var controller = Controller();
		Assert.IsNull( controller.Queue.LastTutorial );
		controller.HandleGameEvent( AdvisorGameEvent.Bankrupted, 100, 0, true );
		Assert.IsNull( controller.Queue.LastTutorial, "event advice of group 0 is not a tutorial" );
		Assert.AreEqual( AdvisorEligibility.TutorialDisabled, controller.Advise( 188, 100, 0, tutorialEnabled: false )!.Value.Eligibility );
		Assert.AreEqual( new AdvisorPendingAdvice( 188, 1000 ), controller.Queue.LastTutorial, "copied before the eligibility check" );
		controller.Advise( 189, 100, 0, true );
		Assert.AreEqual( 189, controller.Queue.LastTutorial!.MessageId, "the latest tutorial record replaces it" );
		controller.Withdraw( 188, 1000 );
		Assert.AreEqual( 189, controller.Queue.LastTutorial!.MessageId, "withdrawing another message keeps it" );
		controller.Withdraw( 189, 1000 );
		Assert.IsNull( controller.Queue.LastTutorial, "withdrawing its own message clears it" );
	}

	[TestMethod]
	public void F1OffersTheLastTutorialAgainAtTheContextScoreBypassingOnceOnly()
	{
		var controller = Controller();
		Assert.IsNull( controller.ReplayLastTutorial( 100, true ), "nothing kept" );
		controller.Advise( 188, 100, 0, true );
		var played = new List<int>();
		controller.Update( () => 1000, () => 100, response => { played.Add( response ); return 4000; } );
		Assert.AreEqual( AdvisorEligibility.AlreadyPlayed, controller.Advise( 188, 100, 0, true )!.Value.Eligibility, "a plain repeat is once-only" );
		var replay = controller.ReplayLastTutorial( 100, true );
		Assert.AreEqual( new AdvisorQueueAdmission( AdvisorEligibility.Eligible, true, 0 ), replay );
		Assert.AreEqual( new AdvisorPendingAdvice( 188, 1500, -1, true, ScoreOverridden: true ), controller.Queue.GetSlot( 0 ) );
		Assert.AreEqual( controller.Queue.GetSlot( 0 ), controller.Queue.LastTutorial, "the kept record carries the replay's score and override" );
		controller.Update( () => 1000 + 4000 + 1000, () => 100, response => { played.Add( response ); return 4000; } );
		CollectionAssert.AreEqual( new[] { 407, 407 }, played );
		Assert.AreEqual( AdvisorEligibility.TutorialDisabled, controller.ReplayLastTutorial( 100, false )!.Value.Eligibility, "the tutorial option still gates it" );
	}

	[TestMethod]
	public void TheLastTutorialRecordSurvivesASnapshotAndOldSnapshotsHaveNone()
	{
		var controller = Controller();
		controller.Advise( 199, 100, 0, true );
		var snapshot = controller.CaptureSnapshot();
		Assert.AreEqual( new AdvisorPendingAdviceSnapshot( 199, 1000, -1, false ), snapshot.Queue.LastTutorial );
		var restored = Controller();
		restored.Advise( 188, 100, 0, true );
		restored.RestoreSnapshot( snapshot );
		Assert.AreEqual( new AdvisorPendingAdvice( 199, 1000 ), restored.Queue.LastTutorial );
		restored.RestoreSnapshot( snapshot with { Queue = snapshot.Queue with { LastTutorial = null } } );
		Assert.IsNull( restored.Queue.LastTutorial, "a save from before the record was kept restores none" );
		Assert.ThrowsException<InvalidDataException>( () => restored.ValidateSnapshot( snapshot with { Queue = snapshot.Queue with { LastTutorial = new AdvisorPendingAdviceSnapshot( 106, 1000, -1, false ) } } ),
			"only a tutorial-group message can be the last tutorial" );
	}

	[TestMethod]
	public void AQueueSavedBeforeTheLastTutorialWasKeptLoadsWithNone()
	{
		var controller = Controller();
		controller.Advise( 199, 100, 0, true );
		var json = JsonNode.Parse( ParkResumeJson.Serialize( controller.CaptureSnapshot().Queue ) )!.AsObject();
		Assert.IsTrue( json.Remove( "LastTutorial" ), "the current format writes the record" );
		var queue = ParkResumeJson.Deserialize<AdvisorQueueSnapshot>( Encoding.UTF8.GetBytes( json.ToJsonString() ) );
		Assert.IsNull( queue.LastTutorial, "a save written before the record was kept has none" );
		var restored = Controller();
		restored.RestoreSnapshot( controller.CaptureSnapshot() with { Queue = queue } );
		Assert.IsNull( restored.Queue.LastTutorial );
	}

	[TestMethod]
	public void ACutOffSlapsOnlyWhileTheSpeechPlays()
	{
		var slaps = new List<uint>();
		var speaking = true;
		var draws = new Queue<int>( new[] { 7, 9, 32767 } );
		var advisor = new AutomaticAdvisor( Controller(), () => 0L, speechActive: () => speaking, slapDraw: draws.Dequeue, playSlap: slaps.Add );
		advisor.CutOffSpeech();
		advisor.CutOffSpeech();
		advisor.CutOffSpeech();
		CollectionAssert.AreEqual( new uint[] { 0x280, 0x27F, 0x27F + 32767 % 3 }, slaps, "cat_speech 0x27F + rand() % 3" );
		speaking = false;
		advisor.CutOffSpeech();
		Assert.AreEqual( 3, slaps.Count, "without a playing speech sound the slap path is silent and draws nothing" );
		Assert.AreEqual( 0, draws.Count );
	}

	[TestMethod]
	public void F1RepeatsTutorialsInTheHelpWindowsOnly()
	{
		foreach ( var window in AdvisorUiHelp.Windows.Keys.Where( name => name != ParkHud.MapScreenName ) )
			Assert.IsTrue( AdvisorUiHelp.RepeatsTutorialOnHelpKey( window ), window );
		Assert.IsFalse( AdvisorUiHelp.RepeatsTutorialOnHelpKey( ParkHud.MapScreenName ), "the map window's procedure is not a caller of the default window procedure" );
		Assert.IsFalse( AdvisorUiHelp.RepeatsTutorialOnHelpKey( "pause" ) );
		Assert.IsFalse( AdvisorUiHelp.RepeatsTutorialOnHelpKey( null ), "the park itself has no F1 row" );
	}

	[TestMethod]
	public void ClosingTheWindowDropsPendingHelp()
	{
		var controller = Controller();
		controller.Advise( 188, 100, 0, true );
		Assert.AreEqual( new AdvisorWithdrawal( 1, false ), controller.Withdraw( 188, 1000 ) );
		Assert.IsNull( controller.Update( () => 1000, () => 100, _ => 4000 ) );
		Assert.AreEqual( new AdvisorWithdrawal( 0, false ), controller.Withdraw( 188, 1000 ) );
	}

	[TestMethod]
	public void ClosingTheWindowCutsOffItsSpeechAndAThirdCutDiscardsIt()
	{
		var controller = Controller();
		uint clock = 1000;
		var played = new List<int>();
		uint? Play( int response ) { played.Add( response ); return 4000; }
		for ( var cut = 1; cut <= 2; cut++ )
		{
			Assert.AreEqual( AdvisorEligibility.Eligible, controller.Advise( 188, 100, 0, true )!.Value.Eligibility, $"cut {cut}" );
			controller.Update( () => clock, () => 100, Play );
			var withdrawal = controller.Withdraw( 188, clock + 500 );
			Assert.IsTrue( withdrawal.SpeechCancelled );
			var history = controller.Queue.GetHistory( 188 );
			Assert.IsFalse( history.HasBeenPlayed, "a cut-off help does not count as played" );
			Assert.AreEqual( (uint)cut, history.SlapCount );
			Assert.IsFalse( controller.Queue.IsBusy( clock + 500 ), "the reservation ends with the speech" );
		}
		Assert.AreEqual( AdvisorEligibility.TooManySlaps, controller.Advise( 188, 100, 0, true )!.Value.Eligibility, "discarded after the group's DiscardAfterSlaps" );
		CollectionAssert.AreEqual( new[] { 407, 407 }, played );
	}

	[TestMethod]
	public void WithdrawingAnotherMessageLeavesTheSpeechAlone()
	{
		var controller = Controller();
		controller.Advise( 188, 100, 0, true );
		controller.Update( () => 1000, () => 100, _ => 4000 );
		Assert.IsFalse( controller.Withdraw( 189, 1500 ).SpeechCancelled );
		Assert.IsFalse( controller.Withdraw( 188, 1000 + 4000 + 1000 ).SpeechCancelled, "the speech is over" );
		Assert.IsTrue( controller.Queue.GetHistory( 188 ).HasBeenPlayed );
	}

	[TestMethod]
	public void EventAdviceWaitsForTheSpeechWhileHelpWithdrawalFreesIt()
	{
		var controller = Controller();
		uint clock = 1000;
		var played = new List<int>();
		uint? Play( int response ) { played.Add( response ); return 4000; }
		controller.HandleGameEvent( AdvisorGameEvent.Bankrupted, 100, 0, true );
		controller.Update( () => clock, () => 100, Play );
		controller.Advise( 188, 100, 0, true );
		controller.Update( () => clock + 100, () => 100, Play );
		CollectionAssert.AreEqual( new[] { 274 }, played, "help waits while event advice speaks" );
		Assert.IsFalse( controller.WithdrawModeHelp( clock + 100 ), "help of a window is not mode help" );
		controller.Update( () => clock + 6000, () => 100, Play );
		CollectionAssert.AreEqual( new[] { 274, 407 }, played );
	}

	[TestMethod]
	public void ModeHelpIsWithdrawnWhenTheModeChanges()
	{
		var controller = Controller();
		controller.Advise( 203, 100, 0, true );
		controller.Update( () => 1000, () => 100, _ => 4000 );
		Assert.IsTrue( controller.WithdrawModeHelp( 1500 ), "message 203 is a mode help" );
	}

	[TestMethod]
	public void TheCurrentMessageSurvivesASnapshot()
	{
		var controller = Controller();
		controller.Advise( 188, 100, 0, true );
		controller.Update( () => 1000, () => 100, _ => 4000 );
		var snapshot = controller.CaptureSnapshot();
		Assert.AreEqual( 188, snapshot.Queue.CurrentMessageId );
		var resumed = Controller();
		resumed.RestoreSnapshot( snapshot );
		Assert.IsTrue( resumed.Withdraw( 188, 1500 ).SpeechCancelled, "the restored reservation still belongs to its message" );
		// A snapshot from before the field existed restores with no current message.
		var older = Controller();
		older.RestoreSnapshot( snapshot with { Queue = snapshot.Queue with { CurrentMessageId = null } } );
		Assert.IsFalse( older.Withdraw( 188, 1500 ).SpeechCancelled );
		Assert.ThrowsException<InvalidDataException>( () => Controller().ValidateSnapshot( snapshot with { Queue = snapshot.Queue with { CurrentMessageId = 99999 } } ) );
	}

	[TestMethod]
	public void WindowNamesMapToTheirHelp()
	{
		Assert.AreEqual( 188, AdvisorUiHelp.MessageFor( "buy" ) );
		Assert.AreEqual( 303, AdvisorUiHelp.MessageFor( "entryPrice" ) );
		Assert.AreEqual( 304, AdvisorUiHelp.MessageFor( "map" ), "the map window posts 0x130 (0x5F100D)" );
		Assert.IsNull( AdvisorUiHelp.MessageFor( "options" ), "no traced message" );
	}

	[TestMethod]
	public void OriginalModelsNameTheirAccessoryNodes()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original advisor assets." );
		var dataPath = Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		if ( !File.Exists( Path.Combine( dataPath, "global", "advisor.wad" ) ) )
			Assert.Inconclusive( "The original advisor WAD is missing." );
		var original = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			var global = AdvisorNodeMap.From( new ModelFile( "/global/advisor/Advisor.MD2" ) );
			string Name( int selector ) => global.Meshes.Single( mesh => mesh.Node == global.FindNode( AdvisorAccessories.SelectableTypeFlag, selector ) ).Name;
			Assert.AreEqual( "Hardhat", Name( 5 ) );
			Assert.AreEqual( "Security Hat", Name( 6 ) );
			Assert.AreEqual( "Handyman Hat", Name( 7 ) );
			Assert.AreEqual( "Research Hat", Name( 10 ) );
			Assert.AreEqual( "Bowler Hat", Name( 11 ) );
			Assert.AreEqual( "Bowler_BTie", Name( 12 ) );
			Assert.AreEqual( "Fast Food Hat", Name( 13 ) );
			Assert.AreEqual( "Spatula", Name( 14 ) );
			var jungle = AdvisorNodeMap.From( new ModelFile( "/levels/jungle/advisor/Advisor_JUN.MD2" ) );
			Assert.AreEqual( "Pith Helmet", jungle.Meshes.Single( mesh => mesh.Node == jungle.FindNode( AdvisorAccessories.SelectableTypeFlag, 4 ) ).Name );
			var theme = AdvisorThemeCostume.Load( FileSystem, "hallow" );
			CollectionAssert.AreEqual( new[] { 3, 16, 17, 0 }, theme.AddOns.ToArray() );
		}
		finally
		{
			FileSystem = original;
		}
	}
}
