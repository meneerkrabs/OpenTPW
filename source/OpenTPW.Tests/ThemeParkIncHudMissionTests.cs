using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The Theme Park Inc HUD's mission window and objective letter: the letter's go-to, the close of an open letter and the guard against a career without a zone.</summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncHudMissionTests
{
	private const string Standard = "BankAccountInfo.InitialCash\t50000\nBankAccountInfo.InitialAdmissionFee\t25\n"
		+ "LoanInfo[0].LoanAmount\t10000\nLoanInfo[0].APRInPercent\t20\nLoanInfo[0].RepaymentPeriodInMonths\t36\nLoanInfo[0].Lendername\t0\n"
		+ "Costs.QueueCell 25\nCosts.PathCell 10\nCosts.KartTrackCell 40\nCosts.WaterTrackCell 50\nCosts.MapCell 75\n"
		+ "PeepInfo.ExcitementToCostDivisor 6\nPeepInfo.MinimumEntryFee 20\nPeepInfo.CheapPriceMultiplier 0.75\nPeepInfo.AveragePriceMultiplier 1.25\nPeepInfo.ExpensivePriceMultiplier 2.0\n";

	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize() { previousEdition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkInc ); }

	[TestCleanup]
	public void Cleanup() => GameEditions.Select( previousEdition );

	private static ThemeParkIncCareer Career( bool zone )
	{
		var files = new TestCareerFiles()
			.With( "/missions/Miss00.sam", "Completion.EndingTheme 1\n" )
			.With( "/missions/Miss01.sam", "Completion.EndingTheme 1\n" )
			.With( "/levels/arabian/global.sam", "ParkInfo.AvailableFromStart 0\n" )
			.With( "/levels/science/global.sam", "ParkInfo.AvailableFromStart 1\n" )
			.With( "/levels/water/global.sam", "ParkInfo.AvailableFromStart 0\n" );
		var career = new ThemeParkIncCareer( files, TestCareerFiles.ShippedFiringLines );
		if ( zone )
			career.ChangeTheme( "science" );
		return career;
	}

	private static ThemeParkIncHud BuildHud( ThemeParkIncCareer career )
	{
		var settings = BalanceSettings.ForThemeParkInc( new SamSettings( new[] { SamDocument.Parse( Standard, "Standard.sam" ) } ), "science" );
		ParkEconomy economy;
		using ( ParkEconomy.EnterThemeParkIncCareer( career ) )
			economy = new ParkEconomy( settings, new EconomyObjectCatalog( Array.Empty<EconomyObjectInfo>() ), ParkGameMode.FullSimulation, 7, EconomyRules.ThemeParkIncMoney );
		var catalog = (ObjectCatalog)Activator.CreateInstance( typeof( ObjectCatalog ), BindingFlags.NonPublic | BindingFlags.Instance, null,
			new object[] { "science", Array.Empty<ObjectCatalogEntry>() }, null )!;
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		Set( level, "Objects", new ParkObjects( catalog, new SandboxParkGrid(), new WorldSeed( 1 ) ) );
		Set( level, "CellTool", new CellBuildTool() );
		var runtime = (ParkEconomyRuntime)RuntimeHelpers.GetUninitializedObject( typeof( ParkEconomyRuntime ) );
		Set( runtime, "Economy", economy );
		Set( level, "Park", runtime );
		var strings = new UiStringTable( "English", Enumerable.Range( 0, 1000 ).Select( index => $"ui{index}" ).ToArray(), Enumerable.Range( 0, 400 ).Select( index => $"help{index}" ).ToArray(),
			new string[1], new string[1] );
		return new ThemeParkIncHud( level, strings, new ThemeParkIncHudHost() ) { Texts = new ThemeParkIncMissionWindowTests.FakeTexts() };
	}

	private static void Set( object target, string property, object value ) =>
		target.GetType().GetField( "<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private static void Click( ThemeParkIncHud hud, string id )
	{
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var context = new UiContext( hud.Strings, new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1920, 1080 ), ResolveTexture = _ => null };
		var element = hud.Stack.Top!.Find( id ) ?? throw new AssertFailedException( $"The top screen has no element {id}." );
		var centre = element.ScreenRect( context.Canvas ).Center;
		hud.Stack.Update( context, UiInput.Idle( centre ) );
		hud.Stack.Update( context, UiInput.Click( centre ) );
	}

	[TestMethod]
	public void TheObjectiveLetterShowsAndItsGoToOpensTheMissionWindow()
	{
		var hud = BuildHud( Career( zone: true ) );
		hud.ShowObjectiveLetter( 1, "1.2.2001" );
		Assert.IsTrue( hud.LetterOpen );
		Assert.AreEqual( "TagSystem168", ((UiLabel)hud.Stack.Top!.Find( "title" )!).Text() );
		Assert.AreEqual( "ObjectiveComplete1\nTagSystem173\nTagSystem174", ((UiLabel)hud.Stack.Top!.Find( "body" )!).Text() );
		Click( hud, "goto" );
		Assert.IsFalse( hud.LetterOpen, "the go-to closes the letter" );
		Assert.IsTrue( hud.MissionWindowOpen && hud.Stack.Top!.Name == ThemeParkIncMissionWindow.ScreenName, "mission 1's letter leads to the mission window (kind 0xC)" );
		Assert.IsTrue( hud.TimeScale > 0, "neither window pauses the park" );
	}

	[TestMethod]
	public void OpeningTheMissionWindowClosesAnOpenLetterFirst()
	{
		var hud = BuildHud( Career( zone: true ) );
		var dismissed = 0;
		hud.ShowLetter( new ThemeParkIncLetterWindow.Letter( "Subject", "From", "To", "Date", "Body", 0 ), () => dismissed++ );
		Assert.IsTrue( hud.OpenMissionWindow() );
		Assert.AreEqual( 1, dismissed, "message 5 closes the letter as its close button does" );
		Assert.IsFalse( hud.LetterOpen );
		Assert.AreEqual( 2, hud.Stack.Screens.Count, "the HUD and the mission window" );
		Assert.IsTrue( hud.OpenMissionWindow(), "opening again builds it again" );
		Assert.AreEqual( (2, 1), (hud.Stack.Screens.Count, dismissed) );
	}

	[TestMethod]
	public void ALetterOpeningOverTheMissionWindowClosesIt()
	{
		// the letter's builder sends message 5 to the HUD's window first (0x526CD0 -> 0x5013C0), the same slot the mission window holds
		var hud = BuildHud( Career( zone: true ) );
		Assert.IsTrue( hud.OpenMissionWindow() );
		hud.ShowLetter( new ThemeParkIncLetterWindow.Letter( "Subject", "From", "To", "Date", "Body", 0 ), () => { } );
		Assert.IsFalse( hud.MissionWindowOpen, "the letter closed the mission window" );
		Assert.IsTrue( hud.LetterOpen );
		Assert.AreEqual( 2, hud.Stack.Screens.Count, "the HUD and the letter" );
	}

	[TestMethod]
	public void ACareerWithoutAZoneOpensNoMissionWindow()
	{
		var hud = BuildHud( Career( zone: false ) );
		Assert.IsFalse( hud.OpenMissionWindow() );
		Assert.IsFalse( hud.MissionWindowOpen );
	}
}
