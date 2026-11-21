using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's mission window, "Current Objective" (docs/reverse/TPI-MISSIONS.md §7): the generated layout tables, the briefing rule, the details page and Proceed on synthetic
/// missions and strings, and over the retail corpus when <c>OPENTPW_TPI_PATH</c> names it, the English texts and a headless draw.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncMissionWindowTests
{
	private BaseFileSystem? previous;

	[TestInitialize]
	public void Initialize()
	{
		previous = FileSystem;
		Log ??= new();
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previous!;
		GameEditions.Select( GameEdition.ThemeParkWorld );
		UiImages.ResetListings();
	}

	/// <summary>Formats every entry as its table, number and arguments, so a test sees which record the window asked for.</summary>
	internal sealed class FakeTexts : IThemeParkIncTexts
	{
		public string Format( ThemeParkIncTable table, int entry, IReadOnlyDictionary<int, string>? values = null ) =>
			$"{table}{entry}" + (values is { Count: > 0 } ? "[" + string.Join( ",", values.OrderBy( pair => pair.Key ).Select( pair => $"p{pair.Key}={pair.Value}" ) ) + "]" : "");
	}

	private sealed class World( ThemeParkIncMissionController missions ) : IThemeParkIncMissionWindowWorld
	{
		public ThemeParkIncMissionController Missions { get; } = missions;
		public long Turn { get; set; }
		public ushort CurrentObstacle { get; set; }
		public int ObstacleResearchPercent { get; set; }
		public bool ResearchersHired { get; set; }
		public bool? ObstacleStaffSlotsFilled { get; set; }
		public string PlayerName { get; set; } = "Player";
	}

	private static UiStringTable SyntheticStrings()
	{
		var ui = Enumerable.Range( 0, 1000 ).Select( index => $"ui{index}" ).ToArray();
		var help = Enumerable.Range( 0, 400 ).Select( index => $"help{index}" ).ToArray();
		return new UiStringTable( "English", ui, help, Array.Empty<string>(), Array.Empty<string>() );
	}

	private static TestCareerFiles Files( params string[] missions )
	{
		var files = new TestCareerFiles()
			.With( "/levels/arabian/global.sam", "ParkInfo.AvailableFromStart 0\n" )
			.With( "/levels/science/global.sam", "ParkInfo.AvailableFromStart 1\n" )
			.With( "/levels/water/global.sam", "ParkInfo.AvailableFromStart 0\n" );
		for ( var index = 0; index < missions.Length; index++ )
			files.With( $"/missions/Miss{index:00}.sam", missions[index] );
		return files;
	}

	private static string Statistic( int index, int type, int target, int zone = 1 ) =>
		$"ExitStatistic[{index}].Type {type}\nExitStatistic[{index}].TargetValue {target}\nExitStatistic[{index}].InTheme {zone}\n";

	/// <summary>A career at mission <paramref name="counter"/> of <paramref name="missions"/>, in the science zone, with its controller and world.</summary>
	private static (ThemeParkIncMissionWindow Window, World World, TestMissionHost Host, List<string> Closed) Window( int counter, params string[] missions )
	{
		var career = new ThemeParkIncCareer( Files( missions ), TestCareerFiles.ShippedFiringLines );
		career.ChangeTheme( "science" );
		for ( var index = 0; index < counter; index++ )
			career.Advance();
		var host = new TestMissionHost();
		var controller = new ThemeParkIncMissionController( career, host, 0 );
		var world = new World( controller );
		var closed = new List<string>();
		var window = new ThemeParkIncMissionWindow( new FakeTexts(), SyntheticStrings(), world, new ThemeParkIncMissionWindowSession(), () => closed.Add( "close" ), () => closed.Add( "challenges" ) );
		return (window, world, host, closed);
	}

	private static UiContext FakeContext( UiStringTable strings )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		return new UiContext( strings, new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1920, 1080 ), ResolveTexture = _ => null };
	}

	private static void Click( UiScreenStack stack, UiContext context, string id )
	{
		var element = stack.Top!.Find( id ) ?? throw new AssertFailedException( $"The top screen has no element {id}." );
		var centre = element.ScreenRect( context.Canvas ).Center;
		stack.Update( context, UiInput.Idle( centre ) );
		stack.Update( context, UiInput.Click( centre ) );
	}

	private const string Signature = "\nTagSystem173\nTagSystem174";

	[TestMethod]
	public void TheMissionTableIsTheCurrentObjectiveWindowWithItsControls()
	{
		var table = ThemeParkIncLayoutTables.MissionWindow;
		Assert.AreEqual( 0x9BE808, table.Address );
		Assert.AreEqual( "!window", table.Root.Model );
		Assert.AreEqual( 34, table.All.Count() );
		Assert.AreEqual( "objectim", table.Control( 0xCEC55 ).Model );
		Assert.AreEqual( "mugshot", table.Control( 0xCEC5D ).Model );
		Assert.AreEqual( "tab_text", table.Control( 0xCEC58 ).Model );
		Assert.AreEqual( 71, table.Control( 0xCEC58 ).Help );
		Assert.AreEqual( "tab_text", table.Control( 0xCEC59 ).Model );
		Assert.AreEqual( "b_ltoggle10", table.Control( 0xCEC68 ).Model );
		Assert.AreEqual( 100, table.Control( 0xCEC68 ).Help );
		Assert.AreEqual( "b_updown", table.Control( 0xCEC69 ).Model );
		Assert.AreEqual( "b_help", table.Control( 0xCEC6B ).Model );
		// the close and the cancel button share the id -2
		Assert.AreEqual( 2, table.All.Count( node => node.Id == 0xFFFFFFFEu ) );
		Assert.AreEqual( 281, table.Model( "b_cancel" ).Help );
		Assert.AreEqual( 2, table.Model( "b_close" ).Help );
		Assert.IsNotNull( table.Control( 0xCEC5A ).Inner, "the letter frame has an inner rectangle" );
	}

	[TestMethod]
	public void TheChallengesTabIsItsOwnWindow()
	{
		var table = ThemeParkIncLayoutTables.ChallengesWindow;
		Assert.AreEqual( 0x9B54CC, table.Address );
		Assert.AreEqual( 30, table.All.Count() );
		Assert.AreEqual( "tab_text", table.Control( 0xC1200 ).Model );
		Assert.AreEqual( 70, table.Control( 0xC1200 ).Help );
		Assert.AreEqual( "!listnarrow", table.Control( 0xC120E ).Model );
	}

	[TestMethod]
	public void TheBriefingFillsMissionZerosTargetAndChoosesThePromotionTextByRank()
	{
		var (zero, _, _, _) = Window( 0, Statistic( 0, 8, 40 ) );
		Assert.AreEqual( "MissionDesc0[p0=40]\n" + Signature, zero.ComposeBriefing(), "mission 0: its first statistic's target, a line feed and the signature" );

		var promotion = "Promotion.Number 2\n";
		var missions = new[] { Statistic( 0, 8, 40 ), promotion, promotion, promotion };
		var (one, oneWorld, _, _) = Window( 1, missions );
		Assert.AreEqual( "MissionDesc1\n" + Signature, one.ComposeBriefing(), "rank 1 below the required 2: the plain text" );
		oneWorld.Missions.Career.Promote( 2 );
		Assert.AreEqual( "MissionDescGotPromo1\n" + Signature, one.ComposeBriefing(), "the required rank is held: the promoted text" );
		var (two, twoWorld, _, _) = Window( 2, missions );
		twoWorld.Missions.Career.Promote( 3 );
		Assert.AreEqual( "MissionDesc2\n" + Signature, two.ComposeBriefing(), "mission 2 never takes the promoted text" );
		var (none, noneWorld, _, _) = Window( 1, Statistic( 0, 8, 40 ), "" );
		noneWorld.Missions.Career.Promote( 5 );
		Assert.AreEqual( "MissionDesc1\n" + Signature, none.ComposeBriefing(), "a mission without a required rank: the plain text" );
	}

	[TestMethod]
	public void TheDetailsListEveryRequirementAndItsStatus()
	{
		var text = Statistic( 0, 2, 5000 ) + Statistic( 1, 8, 100 ) + "Promotion.Number 3\nRequiredChallenges.Theme 1\nRequiredChallenges.Number 2\nObstacle.GoldenTickets 1\nCompletion.EndingTheme 1\n";
		var (window, world, _, _) = Window( 2, Statistic( 0, 8, 40 ), "", text );
		var career = world.Missions.Career;
		career.Promote( 1 );
		career.AddGoldenTickets( 4 );
		career.ZoneRecord( 1 )!.Data.ChallengesDone = 1;
		window.ComposeDetails();
		Assert.AreEqual( "UiText83\nUiText94\nStatTitles7[p0=5000]\nStatTitles11[p0=100]\nStatTitles3[p0=2]\n\nUiText85\nStatTitles13[p0=1]\n\n"
			+ "UiText86\nUiText98[p12=PromotionLevels1]\nStatTitles8[p0=0]\nStatTitles12[p0=0]\nStatTitles4[p0=1]\nStatTitles15[p0=4]\n", window.Details );

		career.Promote( 3 );
		window.ComposeDetails();
		StringAssert.StartsWith( window.Details, "UiText83\nUiText99[p12=PromotionLevels3]\n", "the rank is held: well done, and the text starts again" );
	}

	[TestMethod]
	public void ThePromotionHintFollowsTheCounterAndTheZone()
	{
		string Hint( int counter, string zone )
		{
			var missions = Enumerable.Range( 0, counter + 1 ).Select( index => index == counter ? $"Promotion.Number 6\nCompletion.EndingTheme {ThemeParkIncCareer.ZoneNames.ToList().IndexOf( zone )}\n" : "" ).ToArray();
			var (window, world, _, _) = Window( counter, missions );
			world.Missions.Career.ChangeTheme( zone );
			window.ComposeDetails();
			return window.Details.Split( '\n' )[1];
		}
		Assert.AreEqual( "UiText95", Hint( 4, "science" ) );
		Assert.AreEqual( "UiText100", Hint( 4, "arabian" ) );
		Assert.AreEqual( "UiText96", Hint( 10, "arabian" ) );
		Assert.AreEqual( "UiText101", Hint( 10, "science" ) );
		Assert.AreEqual( "UiText97", Hint( 14, "science" ) );
		Assert.AreEqual( "UiText102", Hint( 14, "water" ) );
		Assert.AreEqual( "UiText83", Hint( 3, "science" ), "another counter appends the last formatted text, the requirements heading" );
	}

	[TestMethod]
	public void InAnotherZoneTheDetailsNameTheObjectivesZone()
	{
		var (window, _, _, _) = Window( 1, "", Statistic( 0, 8, 40, zone: 0 ) + "Completion.EndingTheme 0\n" );
		window.ComposeDetails();
		Assert.AreEqual( "UiText86\nUiText75[p5=ThemeNames3]\n", window.Details, "rank 1 holds the absent required rank: the zone hint (Arabian Nights is THEMENAMES 3)" );
		var (polar, _, _, _) = Window( 1, "", "Completion.EndingTheme 2\nPromotion.Number 4\n" );
		polar.ComposeDetails();
		StringAssert.StartsWith( polar.Details, "UiText83\n", "below the required rank in another zone: the objectives" );
	}

	[TestMethod]
	public void AnUnresearchedObstacleListsWhatTheResearchNeeds()
	{
		var (window, world, _, _) = Window( 1, "", "Obstacle.Type 1703\nCompletion.EndingTheme 1\n" );
		Assert.AreEqual( 1, world.Missions.ObstacleState() );
		window.ComposeDetails();
		Assert.AreEqual( "UiText86\nUiText103\nUiText105\nUiText106\n", window.Details, "no scientist: 105; the research manager always exists, so never 104" );
		world.ResearchersHired = true;
		window.ComposeDetails();
		Assert.AreEqual( "UiText86\nUiText103\nUiText106\n", window.Details );
		world.ObstacleResearchPercent = 37;
		window.ComposeDetails();
		Assert.AreEqual( "UiText86\nUiText91[p11=37]\n", window.Details );

		world.Missions.Career.DiscoverObstacleAbilities();
		world.Missions.Career.MarkObstacleRemoved();
		Assert.AreEqual( 4, world.Missions.ObstacleState() );
		window.ComposeDetails();
		Assert.AreEqual( "UiText86\nUiText91[p11=37]\n\n", window.Details, "a removed obstacle writes nothing: the last text stays and a line feed follows" );
	}

	[TestMethod]
	public void TheDetailsStopAtTheEndOfTheirBuffer()
	{
		var (window, world, _, _) = Window( 1, "", "Obstacle.Type 1703\nCompletion.EndingTheme 1\n" );
		window.ComposeDetails();
		var first = window.Details;
		world.Missions.Career.DiscoverObstacleAbilities();
		world.Missions.Career.MarkObstacleRemoved();
		for ( var tick = 0; tick < 3 * ThemeParkIncMissionWindow.DetailsCapacity; tick++ )
			window.ComposeDetails();
		Assert.AreEqual( 0x9C3, window.Details.Length, "state 4 adds a line feed every tick until the 0x9C4-character buffer (terminator included) is full" );
		StringAssert.StartsWith( window.Details, first + "\n" );
		Assert.IsTrue( window.Details.Substring( first.Length ).All( character => character == '\n' ) );
	}

	[TestMethod]
	public void TheObstaclePageShowsTheStaffAndTheTickets()
	{
		var (window, world, _, _) = Window( 1, "", "Obstacle.Type 1703\nObstacle.GoldenTickets 2\nCompletion.EndingTheme 1\n" );
		world.Missions.Career.DiscoverObstacleAbilities();
		world.Missions.Career.AddGoldenTickets( 1 );
		Assert.AreEqual( ("UiText84", "UiText85 StatTitles14[p0=2]", "UiText86\nStatTitles15[p0=1]\nUiText92"), window.ComposeObstacleLines() );
		world.ObstacleStaffSlotsFilled = true;
		Assert.AreEqual( "UiText86\nStatTitles15[p0=1]\nUiText93", window.ComposeObstacleLines().Status );
	}

	[TestMethod]
	public void TheWindowShowsTheMissionAndTogglesItsPages()
	{
		var (window, world, _, closed) = Window( 1, "", "Obstacle.Type 1703\nCompletion.EndingTheme 1\n" );
		var stack = new UiScreenStack();
		var screen = window.Build();
		stack.Push( screen );
		string Text( string id ) => ((UiLabel)screen.Find( id )!).Text();
		Assert.AreEqual( "ui461", Text( "title" ) );
		Assert.AreEqual( ("Operation1", "Operation1", "TagSystem173", "Player"), (Text( "operation" ), Text( "subject" ), Text( "from" ), Text( "to" )) );
		Assert.AreEqual( ("ui857", "ui856", "ui855"), (Text( "subjectLabel" ), Text( "fromLabel" ), Text( "toLabel" )) );
		Assert.AreEqual( ("ui463", "ui464", "ui82", "ui988"), (Text( "objectivesTabText" ), Text( "challengesTabText" ), Text( "proceedText" ), Text( "pageText" )) );
		Assert.AreEqual( 11, ((UiModelImage)screen.Find( "picture" )!).Frame(), "mission 1's picture: 0x9BECCC[1] - 1" );
		Assert.AreEqual( 0, ((UiModelImage)screen.Find( "mugshot" )!).Frame() );
		Assert.AreEqual( "MissionDesc1\n" + Signature, Text( "body" ) );
		Assert.IsFalse( screen.Find( "objectivesTab" )!.Enabled, "the current tab is disabled" );
		Assert.IsFalse( screen.Find( "proceed" )!.Enabled, "the obstacle is not researched: Proceed is disabled" );
		Assert.IsFalse( screen.Find( "obstacleHeading" )!.Visible );

		var context = FakeContext( SyntheticStrings() );
		Click( stack, context, "page" );
		Assert.AreEqual( 1, window.Page );
		Assert.AreEqual( ("ui989", "UiText86\nUiText103\nUiText105\nUiText106\n"), (Text( "pageText" ), Text( "body" )) );
		Click( stack, context, "help" );
		Assert.AreEqual( 0, closed.Count, "help does not close the window" );
		Click( stack, context, "challengesTab" );
		CollectionAssert.AreEqual( new[] { "close", "challenges" }, closed );

		world.Missions.Career.DiscoverObstacleAbilities();
		window.Refresh();
		Assert.IsTrue( screen.Find( "obstacleHeading" )!.Visible, "obstacle state 2 on the details page shows the staff group" );
		Assert.AreEqual( "", Text( "body" ) );
	}

	[TestMethod]
	public void ProceedCompletesAMetMissionOrRemovesItsObstacle()
	{
		var (window, world, _, closed) = Window( 1, "", Statistic( 0, 8, 40 ) + "Completion.EndingTheme 1\n", "" );
		window.Build();
		Assert.IsFalse( window.ProceedEnabled, "the statistic is not achieved" );
		window.Proceed();
		Assert.AreEqual( (1, 0), (world.Missions.Career.MissionCounter, closed.Count), "unmet: Proceed does nothing" );

		var (plain, plainWorld, _, plainClosed) = Window( 1, "", "Completion.EndingTheme 1\n", "" );
		plain.Build();
		Assert.IsTrue( plain.ProceedEnabled, "a mission without requirements is met" );
		plain.Proceed();
		Assert.AreEqual( (2, 1), (plainWorld.Missions.Career.MissionCounter, plainClosed.Count), "OnMissionComplete with no thing to show completes at once; the window closes" );

		var (wrong, _, _, _) = Window( 1, "", "Completion.EndingTheme 2\n", "" );
		wrong.Build();
		Assert.IsFalse( wrong.ProceedEnabled, "the objective's zone is another" );
		var (anyZone, _, _, _) = Window( 1, "", "Completion.EndingTheme 3\n", "" );
		anyZone.Build();
		Assert.IsTrue( anyZone.ProceedEnabled, "EndingTheme 3 is any zone" );

		var (obstacle, obstacleWorld, obstacleHost, _) = Window( 1, "", "Obstacle.Type 1703\nCompletion.EndingTheme 1\n", "" );
		obstacleWorld.CurrentObstacle = 77;
		obstacleWorld.Missions.Career.DiscoverObstacleAbilities();
		obstacleHost.ObstacleStaffSlotsFilled = true;
		obstacle.Build();
		Assert.IsTrue( obstacle.ProceedEnabled );
		obstacle.Proceed();
		Assert.AreEqual( (ThemeParkIncMissionState.ObstacleRemoved, (ushort)77), (obstacleWorld.Missions.State, obstacleWorld.Missions.CutsceneObject), "message 0x24 with the current obstacle" );
		Assert.AreEqual( new ThemeParkIncCameraRequest( 77, 2, 0 ), obstacleHost.Cameras.Single() );
		obstacleWorld.Missions.CameraFinished( 0 );
		Assert.AreEqual( 2, obstacleWorld.Missions.Career.MissionCounter, "the camera's end (message 0x29) completes it" );
	}

	[TestMethod]
	public void FourSecondsAfterOpeningEveryTickRefreshesAndOnlyCancelDisarmsIt()
	{
		var (_, world, _, closed) = Window( 1, "", "Completion.EndingTheme 1\n", "" );
		var session = new ThemeParkIncMissionWindowSession();
		var window = new ThemeParkIncMissionWindow( new FakeTexts(), SyntheticStrings(), world, session, () => closed.Add( "close" ), () => { } );
		var stack = new UiScreenStack();
		stack.Push( window.Build() );
		Assert.IsTrue( window.ProceedEnabled );
		world.Missions.Career.ChangeTheme( "water" );
		window.Tick( 3.9f );
		Assert.IsTrue( window.ProceedEnabled && !session.RefreshArmed, "the first four seconds show the opening's state" );
		window.Tick( 0.2f );
		Assert.IsTrue( session.RefreshArmed, "the timer 0x80080 runs 4000 ms" );
		window.Tick( 0.01f );
		Assert.IsFalse( window.ProceedEnabled, "from then on every tick refreshes: the zone is wrong now" );
		Click( stack, FakeContext( SyntheticStrings() ), "cancel" );
		Assert.IsFalse( session.RefreshArmed, "cancel clears 0xA61814" );
		Assert.AreEqual( 1, closed.Count );
	}

	[TestMethod]
	public void TheRetailWindowShowsMissionZerosBriefingInEnglish()
	{
		var data = UseTpiData();
		var language = GameLanguage.Resolve( data, "English", null );
		var strings = UiStringTable.Load( language );
		var texts = ThemeParkIncTexts.For( strings, language );
		var park = OriginalPark.Load( "science" );
		var economy = ParkEconomyRuntime.ForThemeParkIncLevel( park ).Economy;
		var world = economy.TpiMissionWindowWorld( strings.Entry( ThemeParkIncText.NewPlayer ) )!;
		var window = new ThemeParkIncMissionWindow( texts, strings, world, new ThemeParkIncMissionWindowSession(), () => { }, () => { } );
		var screen = window.Build();
		string Text( string id ) => ((UiLabel)screen.Find( id )!).Text();
		Assert.AreEqual( ("Current Objective", "Operation Expand!", "Mr Maybury", "New Player"), (Text( "title" ), Text( "operation" ), Text( "from" ), Text( "to" )) );
		Assert.AreEqual( ("Objectives", "Challenges", "Proceed", "Go to Details"), (Text( "objectivesTabText" ), Text( "challengesTabText" ), Text( "proceedText" ), Text( "pageText" )) );
		StringAssert.StartsWith( window.Briefing, "I'm sure you want to expand your park" );
		StringAssert.Contains( window.Briefing, "If you can get 40 peop", "mission 0's target is filled in" );
		StringAssert.EndsWith( window.Briefing, "\n\nMr Maybury\nPresident, Theme Park Inc." );
		Assert.IsFalse( window.ProceedEnabled, "nobody is in the park yet" );
		Assert.AreEqual( 9, ((UiModelImage)screen.Find( "picture" )!).Frame() );
		window.ComposeDetails();
		Assert.AreEqual( "REQUIREMENTS FOR THIS OBJECTIVE: \nGet 40 people in the park at once.\n\nOBJECTIVE STATUS: \nNumber of people currently in the park: 0\n\n", window.Details );
	}

	[TestMethod]
	public void TheMissionWindowBuildsAndDrawsOverTheRealModels()
	{
		var data = UseTpiData();
		var language = GameLanguage.Resolve( data, "English", null );
		var strings = UiStringTable.Load( language );
		var loader = UiFonts.CachedLoader( language );
		var fonts = new UiFonts( _ => loader( "PERSONNELFILEMED.bf4" ) );
		var park = OriginalPark.Load( "science" );
		var economy = ParkEconomyRuntime.ForThemeParkIncLevel( park ).Economy;
		var window = new ThemeParkIncMissionWindow( ThemeParkIncTexts.For( strings, language ), strings, economy.TpiMissionWindowWorld( "Player" )!, new ThemeParkIncMissionWindowSession(), () => { }, () => { } );
		var stack = new UiScreenStack();
		stack.Push( window.Build() );
		var context = new UiContext( strings, fonts, new UiModels() ) { Canvas = new UiCanvas( 1920, 1080 ) };
		stack.Update( context, UiInput.Idle( new N.Vector2( 960, 540 ) ) );
		context.Batch.Clear();
		stack.Draw( context );
		Assert.IsTrue( context.Batch.Draws.Count > 0, "the window draws" );
		var models = new UiModels();
		foreach ( var name in ThemeParkIncLayoutTables.MissionWindow.All.Select( node => node.Model ).OfType<string>().Distinct() )
		{
			var model = models.Get( name );
			Assert.IsNotNull( model, $"UiModel {name} loads" );
			Assert.IsTrue( model!.Frames.Count >= 1 && model.Frames[0].Parts.Count >= 1, $"{name} has a drawable frame" );
		}
		Assert.IsTrue( models.Get( "objectim" )!.Frames.Count >= 17, "the mission pictures go up to frame 16" );
	}

	[TestMethod]
	public void TheObjectiveLetterNamesTheCompletedMissionAndItsGoToTarget()
	{
		var letter = ThemeParkIncObjectiveLetter.Compose( 4, new FakeTexts(), "Player", "1.2.2001" );
		Assert.AreEqual( ("TagSystem168", "TagSystem173", "Player", "1.2.2001", 0), (letter.Subject, letter.From, letter.To, letter.Date, letter.Picture) );
		Assert.AreEqual( "ObjectiveComplete4\nTagSystem173\nTagSystem174", letter.Body, "OBJECTIVE_COMPLETE of the completed mission and the signature" );
		Assert.AreEqual( (0x12, 0), (letter.GotoKind, letter.GotoArgument) );
		var expected = new[] { (2, 0x6A7), (0xC, 0), (0x12, 2), (0x12, 1), (0x12, 0), (2, 0xE75), (0xB, 0), (0x12, 1), (2, 0x6A6), (0xB, 0), (0x12, 2), (2, 0xA8F), (2, 0xA8E),
			(0xB, 0), (0x12, 0), (2, 0xE77), (0xB, 0), (0xC, 0) };
		CollectionAssert.AreEqual( expected, Enumerable.Range( 0, 18 ).Select( ThemeParkIncObjectiveLetter.Goto ).ToArray(), "the jump table 0x5CED9C, 17 and above the mission window" );

		var strings = SyntheticStrings();
		var dismissed = 0;
		var target = (0, 0);
		var stack = new UiScreenStack();
		var screen = ThemeParkIncLetterWindow.Create( ThemeParkIncObjectiveLetter.Compose( 1, new FakeTexts(), "Player", "" ), strings, () => dismissed++, ( kind, argument ) => target = (kind, argument) );
		stack.Push( screen );
		Assert.IsTrue( screen.Find( "goto" )!.Enabled, "the objective letter has a go-to" );
		Click( stack, FakeContext( strings ), "goto" );
		Assert.AreEqual( (1, (0xC, 0)), (dismissed, target), "go-to closes the letter and opens the mission window" );
		Assert.IsFalse( ThemeParkIncLetterWindow.Create( new ThemeParkIncLetterWindow.Letter( "", "", "", "", "", 0 ), strings, () => { } ).Find( "goto" )!.Enabled, "a notification letter has none" );
	}

	[TestMethod]
	public void TheRetailObjectiveLetterIsMrMayburysInEnglish()
	{
		var data = UseTpiData();
		var language = GameLanguage.Resolve( data, "English", null );
		var strings = UiStringTable.Load( language );
		var letter = ThemeParkIncObjectiveLetter.Compose( 0, ThemeParkIncTexts.For( strings, language ), "Player", "1.7.2000" );
		Assert.AreEqual( ("Objective Complete!", "Mr Maybury"), (letter.Subject, letter.From) );
		StringAssert.StartsWith( letter.Body, "It's good to see you're getting on with things here." );
		StringAssert.EndsWith( letter.Body, "\nMr Maybury\nPresident, Theme Park Inc." );
		Assert.AreEqual( (2, 0x6A7), (letter.GotoKind, letter.GotoArgument), "mission 0's letter leads to the lake (type 1703)" );
	}

	private string UseTpiData()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var data = Directory.EnumerateDirectories( path! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path!;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		GameEditions.Select( GameEdition.ThemeParkInc );
		ObjectCatalog.BonusDataRoot = null;
		UiImages.ResetListings();
		return data;
	}
}
