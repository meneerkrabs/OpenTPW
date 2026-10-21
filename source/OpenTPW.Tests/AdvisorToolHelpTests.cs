using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// ADVISOR-009: the help the HUD's panels and tools post through tp.exe's 0x486B00 and withdraw through 0x486B40 or the interface mode change 0x52F200. A real
/// HUD over the object-window fixture, with the advisor replaced by a recorder of the host's help and mode callbacks. GPU-free.
/// </summary>
[TestClass, DoNotParallelize]
public class AdvisorToolHelpTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private RideUpgradeIngressTests? fixture;
	private UiContext? context;

	[TestInitialize]
	public void Init() => Log ??= new();

	[TestCleanup]
	public void Cleanup() => fixture?.Cleanup();

	/// <summary>A HUD whose host records every help post, withdrawal and mode change in <paramref name="events"/> ("post N", "withdraw N", "mode").</summary>
	private ParkHud NewHud( out List<string> events, out Level level, BuildCategory category = BuildCategory.Rides, string extra = "", bool recordWindows = false )
	{
		fixture = new();
		fixture.Setup();
		object?[] args = { category, null, null, null, null, null, ParkGameMode.FullSimulation, extra };
		var hud = (ParkHud)typeof( RideUpgradeIngressTests ).GetMethod( "Fixture", Private )!.Invoke( fixture, args )!;
		context = (UiContext)typeof( RideUpgradeIngressTests ).GetMethod( "Context", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, null )!;
		var recorded = new List<string>();
		events = recorded;
		typeof( ParkHud ).GetField( "host", Private )!.SetValue( hud, new HudHost
		{
			HelpPosted = message => recorded.Add( $"post {message}" ),
			HelpWithdrawn = message => recorded.Add( $"withdraw {message}" ),
			ModeChanged = () => recorded.Add( "mode" ),
			WindowOpened = window => { if ( recordWindows ) recorded.Add( $"open {window}" ); },
			WindowClosed = window => { if ( recordWindows ) recorded.Add( $"close {window}" ); }
		} );
		level = (Level)typeof( ParkHud ).GetField( "level", Private )!.GetValue( hud )!;
		return hud;
	}

	private void Frame( ParkHud hud, float delta = 0.016f )
	{
		context!.Delta = delta;
		hud.Update( context, UiInput.Idle( new System.Numerics.Vector2( -1 ) ), false, false, false );
	}

	[TestMethod]
	public void AnOpenedMessagePostsHelp284AndClosingItWithdrawsIt()
	{
		var hud = NewHud( out var events, out _ );
		hud.PostGameMessage( "A message" );
		Frame( hud );
		Assert.AreEqual( 0, events.Count, "a pending message in the tube posts nothing" );
		Assert.IsTrue( hud.OpenPendingMessage( 0 ) );
		CollectionAssert.AreEqual( new[] { "post 284" }, events, "0x48D9C0 posts 0x11C when the item is selected" );
		hud.CloseMessagePanel();
		CollectionAssert.AreEqual( new[] { "post 284", "withdraw 284" }, events, "0x48E4BF withdraws it when the panel closes" );
		hud.CloseMessagePanel();
		Assert.AreEqual( 2, events.Count, "a panel that is already closed withdraws nothing" );
	}

	[TestMethod]
	public void TheMapWindowCarriesHelp304()
	{
		Assert.AreEqual( 304, AdvisorUiHelp.MessageFor( ParkHud.MapScreenName ) );
		CollectionAssert.AreEqual( new[] { 304 }, AdvisorUiHelp.WithdrawnWith( ParkHud.MapScreenName ).ToArray() );
		Assert.IsTrue( AdvisorUiHelp.AllWindowMessages.Contains( 304 ), "a park that ends withdraws it" );
	}

	// ---- The coaster tool ----------------------------------------------------------------------------------

	private static void SetField( object target, string name, object? value ) =>
		target.GetType().GetField( name, Private )!.SetValue( target, value );

	/// <summary>Runs the coaster tool's panel on the first object of the fixture's level (the panel and the tool's station are all the HUD reads here).</summary>
	private static CoasterToolPanel StartCoasterTool( ParkHud hud, Level level, bool flume = false )
	{
		var settings = CoasterTests.PcSettings();
		if ( flume )
			settings["sCoasterType.bSpecialIsTrough"] = "1";
		var ride = new CoasterRide( CoasterDefinition.FromValues( settings ), new CoasterWorld( 1 ), "Test Coaster" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, (20, 20) );
		var panel = CoasterPanelRig.Create( builder );
		SetField( level, "<CoasterToolStation>k__BackingField", level.Objects.Objects.First() );
		SetField( level, "<CoasterPanel>k__BackingField", panel );
		return panel;
	}

	private static void Press( ParkHud hud, string id )
	{
		var button = (UiButton)hud.Screen.Find( id )!;
		Assert.IsTrue( button.Enabled, id );
		button.Clicked!();
	}

	[TestMethod]
	public void TheCoasterPanelsButtonsPostTheirHelpAndTheCloseWithdrawsAllFive()
	{
		var hud = NewHud( out var events, out var level );
		Frame( hud );
		StartCoasterTool( hud, level );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "mode" }, events, "the tool start is a mode change" );
		events.Clear();
		Press( hud, "coasterBuild" );
		Press( hud, "coasterMove" );
		Press( hud, "coasterLoop" );
		Press( hud, "coasterTilt" );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "post 293", "post 289", "post 294" }, events, "build 0x125, move 0x121, loop 0x126; the tilt button posts none (0x4982A0)" );
		events.Clear();
		level.ExitCoasterTool();
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "withdraw 293", "withdraw 295", "withdraw 294", "withdraw 289", "withdraw 292", "mode" }, events,
			"the close message withdraws 0x125, 0x127, 0x126, 0x121 and 0x124 in that order" );
	}

	[TestMethod]
	public void TheLoopButtonOfAFlumeCoasterPostsHelp295()
	{
		var hud = NewHud( out var events, out var level );
		Frame( hud );
		StartCoasterTool( hud, level, flume: true );
		Frame( hud );
		events.Clear();
		Press( hud, "coasterLoop" );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "post 295" }, events, "mask bit 0x200 (definition flag 0x100) posts 0x127 instead of 0x126" );
	}

	[TestMethod]
	public void ADisabledCoasterButtonPostsNothing()
	{
		var hud = NewHud( out var events, out var level );
		Frame( hud );
		var panel = StartCoasterTool( hud, level );
		Frame( hud );
		events.Clear();
		Assert.IsFalse( panel.IsEnabled( CoasterToolButton.Demolish ), "an unfinished circuit has no demolish button" );
		var button = (UiButton)hud.Screen.Find( "coasterDemolish" )!;
		Assert.IsFalse( button.Enabled );
		Frame( hud );
		Assert.AreEqual( 0, events.Count );
	}

	// ---- The path and queue tools --------------------------------------------------------------------------

	/// <summary>A line writer that accepts everything, in the mode under test.</summary>
	private sealed class LineWriter( CellToolMode mode ) : ICellLineWriter
	{
		public CellToolMode Mode => mode;
		public SegmentResult Preview( (int X, int Y) start, (int X, int Y) cursor ) =>
			new( cursor, System.Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false ) { Start = start, Line = ParkPathBuilder.LayLine( start, cursor ) };
		public SegmentResult Commit( (int X, int Y) start, (int X, int Y) end ) => Preview( start, end );
		public void Undo( SegmentResult segment ) { }
	}

	/// <summary>One click of the HUD's tool handler: the tool's own click followed by the HUD's report of it.</summary>
	private static SegmentResult? ToolClick( ParkHud hud, CellBuildTool tool, (int X, int Y) cell, bool joinedPath = false )
	{
		var hadStart = tool.Start != null;
		var mode = tool.Mode;
		var before = hud.InterfaceMode();
		var result = tool.Click( cell );
		if ( result is not { GhostRefused: true } )
			hud.ReportCellToolClick( hadStart, mode, tool, joinedPath, before );
		return result;
	}

	[TestMethod]
	public void ThePathToolsFirstClickPostsHelp184AndTheThirdLaterClickHelp214()
	{
		var hud = NewHud( out var events, out var level );
		Frame( hud );
		var tool = level.CellTool;
		tool.Enter( new LineWriter( CellToolMode.Path ), null );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "mode" }, events, "the tool start is a mode change and the path tool posts nothing by itself" );
		events.Clear();
		ToolClick( hud, tool, (2, 2) );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "post 184" }, events, "0x5272FE: the click that stores the start posts 0xB8" );
		events.Clear();
		ToolClick( hud, tool, (2, 6) );
		ToolClick( hud, tool, (6, 6) );
		Frame( hud );
		Assert.AreEqual( 0, events.Count, "the first and second later click post nothing" );
		ToolClick( hud, tool, (6, 2) );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "post 214" }, events, "0x52735A: the counter reaches 3 on the third click after the first" );
		events.Clear();
		ToolClick( hud, tool, (2, 2) );
		Frame( hud );
		Assert.AreEqual( 0, events.Count, "only the click that reaches 3 posts (cmp eax, 3; jne)" );
	}

	[TestMethod]
	public void ARefusedStartPostsNothingAndTheCounterRestartsAtEachAcceptedFirstClick()
	{
		var hud = NewHud( out var events, out var level );
		Frame( hud );
		var tool = level.CellTool;
		tool.Enter( new LineWriter( CellToolMode.Path ), null );
		Frame( hud );
		events.Clear();
		ToolClick( hud, tool, (2, 2) );
		ToolClick( hud, tool, (2, 6) );
		ToolClick( hud, tool, (6, 6) );
		tool.Cancel();
		Frame( hud );
		events.Clear();
		tool.Enter( new LineWriter( CellToolMode.Path ), null );
		Frame( hud );
		events.Clear();
		ToolClick( hud, tool, (3, 3) );
		ToolClick( hud, tool, (3, 7) );
		ToolClick( hud, tool, (7, 7) );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "post 184" }, events, "the first click cleared the counter (0x527316): two later clicks only" );
	}

	[TestMethod]
	public void TheQueueToolPostsHelp203WhenItStartsAndHelp337WhenItEndsWithoutJoiningAPath()
	{
		var hud = NewHud( out var events, out var level );
		Frame( hud );
		var tool = level.CellTool;
		tool.Enter( new LineWriter( CellToolMode.Queue ), (4, 4) );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "mode", "post 203" }, events, "SetMode(3) withdraws the tool help and then posts 0xCB" );
		events.Clear();
		ToolClick( hud, tool, (4, 9) );
		Frame( hud );
		Assert.AreEqual( 0, events.Count, "a queue line that continues posts nothing" );
		ToolClick( hud, tool, (4, 9) );
		Assert.IsFalse( tool.IsActive, "a click on the start ends the tool" );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "mode", "post 337" }, events, "the end posts 0x151 after the mode change" );
	}

	[TestMethod]
	public void AQueueThatJoinsAPathEndsWithoutHelp337()
	{
		var hud = NewHud( out var events, out var level );
		Frame( hud );
		var tool = level.CellTool;
		tool.Enter( new LineWriter( CellToolMode.Queue ), (4, 4) );
		Frame( hud );
		events.Clear();
		ToolClick( hud, tool, (4, 4), joinedPath: true );
		Assert.IsFalse( tool.IsActive );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "mode" }, events, "the link flag 0x816D60 plays the join sound and posts no help" );
	}

	[TestMethod]
	public void TheKartAndWaterToolsPostNoPathHelp()
	{
		var hud = NewHud( out var events, out var level );
		Frame( hud );
		var tool = level.CellTool;
		tool.Enter( new LineWriter( CellToolMode.WaterTrack ), null );
		Frame( hud );
		events.Clear();
		ToolClick( hud, tool, (2, 2) );
		ToolClick( hud, tool, (2, 6) );
		ToolClick( hud, tool, (6, 6) );
		ToolClick( hud, tool, (6, 2) );
		Frame( hud );
		Assert.AreEqual( 0, events.Count, "the handler 0x527200 is the path and queue modes' only" );
	}

	// ---- The buy list ---------------------------------------------------------------------------------------

	[TestMethod]
	public void ABuyItemPostsHelp201ForTheFirstRideAnd206Otherwise()
	{
		Assert.AreEqual( 201, AdvisorToolHelp.BuildStart( 0, anyRidePlaced: false ), "0x4ac6a5: a ride with none placed" );
		Assert.AreEqual( 206, AdvisorToolHelp.BuildStart( 0, anyRidePlaced: true ), "0x4ac7d4: a ride exists" );
		Assert.AreEqual( 206, AdvisorToolHelp.BuildStart( 1, anyRidePlaced: false ), "a shop is not a ride" );
		Assert.AreEqual( 206, AdvisorToolHelp.BuildStart( 2, anyRidePlaced: false ) );
		Assert.AreEqual( 206, AdvisorToolHelp.BuildStart( 3, anyRidePlaced: false ) );
		Assert.AreEqual( 206, AdvisorToolHelp.BuildStart( -1, anyRidePlaced: false ), "an entry without the key is no ride" );
	}

	[TestMethod]
	public void StartingToPlaceAnItemPostsItsHelpAfterTheModeChange()
	{
		var hud = NewHud( out var events, out var level );
		Frame( hud );
		var placed = level.Objects.Objects.First();
		var item = new BuildItem( "ride", 1100, BuildCategory.Rides, 0, 0, null, System.Array.Empty<string>(), null, placed.Entry );
		hud.BeginPlacing( item );
		Assert.IsNotNull( level.BuildEntry, "the placement tool is armed" );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "mode", "post 206" }, events, "a ride already stands in the park" );
		events.Clear();
		level.BuildEntry = null;
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "mode" }, events, "leaving the tool is a mode change and posts nothing" );
		foreach ( var existing in level.Objects.Objects.ToArray() )
			existing.Delete();
		events.Clear();
		hud.BeginPlacing( item );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "mode", "post 201" }, events, "no ride is placed: the first-ride help" );
	}

	[TestMethod]
	public void TheLandRowsPostTheirToolHelp()
	{
		Assert.AreEqual( 345, AdvisorToolHelp.ClearLandTool, "row -2 builds tool 0x3A and posts 0x159" );
		Assert.AreEqual( 346, AdvisorToolHelp.BuyLandTool, "row -1 builds tool 0x39 and posts 0x15A" );
	}

	// ---- The employee window and the patrol tool ------------------------------------------------------------

	private ParkHud NewEmployeeHud( out List<string> events, out ParkEconomy economy, params StaffType[] roles )
	{
		economy = EconomyTestData.Park();
		foreach ( var role in roles )
			EconomyTestData.HireBest( economy, role );
		var hud = StaffWindowTests.Hud( economy );
		var recorded = new List<string>();
		events = recorded;
		SetField( hud, "host", new HudHost
		{
			HelpPosted = message => recorded.Add( $"post {message}" ),
			HelpWithdrawn = message => recorded.Add( $"withdraw {message}" ),
			ModeChanged = () => recorded.Add( "mode" )
		} );
		return hud;
	}

	[TestMethod]
	public void TheEmployeeWindowPostsHelp212OnlyForAnEmployeeWithAnOwnPatrolArea()
	{
		var hud = NewEmployeeHud( out var events, out var economy, StaffType.Guard, StaffType.Handyman );
		var guard = economy.Staff.Members.Single( member => member.Type == StaffType.Guard );
		var handyman = economy.Staff.Members.Single( member => member.Type == StaffType.Handyman );
		guard.SetPatrolArea( (1, 1), (4, 4) );
		Assert.IsTrue( hud.OpenEmployeeWindow( handyman ) );
		Assert.AreEqual( 0, events.Count, "the whole park: 0x506eb0 answers 0" );
		Assert.IsTrue( hud.CycleEmployee( true ) || hud.CycleEmployee( false ) );
		Assert.AreEqual( guard.Id, hud.Employee!.Id );
		CollectionAssert.AreEqual( new[] { "post 212" }, events, "the refresh after the cycle posts 0xD4" );
		events.Clear();
		hud.CloseEmployeeWindow();
		Assert.IsTrue( hud.OpenEmployeeWindow( guard ) );
		CollectionAssert.AreEqual( new[] { "post 212" }, events, "the window's own refresh posts it as it is built" );
	}

	[TestMethod]
	public void TheEmployeeWindowsCloseWithdrawsBothOfItsMessages()
	{
		CollectionAssert.AreEqual( new[] { 192, 212 }, AdvisorUiHelp.WithdrawnWith( "employee" ).ToArray(), "0x4B5CF8 and 0x4B5D05" );
		CollectionAssert.AreEqual( new[] { 188 }, AdvisorUiHelp.WithdrawnWith( "buy" ).ToArray() );
	}

	[TestMethod]
	public void SettingAPatrolAreaPostsHelp193AfterTheWindowClosed()
	{
		var hud = NewEmployeeHud( out var events, out var economy, StaffType.Guard );
		var level = (Level)typeof( ParkHud ).GetField( "level", Private )!.GetValue( hud )!;
		SetField( level, "<PatrolTool>k__BackingField", new PatrolAreaTool( () => economy, command => level.Commands.Issue( command ) ) );
		var guard = economy.Staff.Members[0];
		Assert.IsTrue( hud.OpenEmployeeWindow( guard ) );
		events.Clear();
		Assert.IsTrue( hud.PressEmployeePatrol() );
		Assert.IsFalse( hud.EmployeeWindowOpen, "the window closes" );
		Assert.IsTrue( level.PatrolTool!.IsActive );
		typeof( ParkHud ).GetMethod( "ReportInterfaceMode", Private )!.Invoke( hud, null );
		CollectionAssert.AreEqual( new[] { "post 193" }, events, "0x4B5B50 posts 0xC1 once the tool is set (the first frame only records the mode)" );
	}

	// ---- The locator panels ----------------------------------------------------------------------------------

	[TestMethod]
	public void TheLocatorPanelsPostTheirHelpAndTheCloseWithdrawsBoth()
	{
		var hud = NewEmployeeHud( out var events, out _, StaffType.Guard );
		hud.OpenStaffLocator();
		CollectionAssert.AreEqual( new[] { "post 198" }, events, "0x4b3f60 without an actor posts 0xC6" );
		events.Clear();
		hud.OpenVisitorLocator();
		CollectionAssert.AreEqual( new[] { "post 287" }, events, "0x4b4280 posts 0x11F" );
		events.Clear();
		hud.CloseLocator();
		CollectionAssert.AreEqual( new[] { "withdraw 287", "withdraw 198" }, events, "the close message withdraws 0x11F and then 0xC6" );
		events.Clear();
		hud.CloseLocator();
		Assert.AreEqual( 0, events.Count, "a closed panel withdraws nothing" );
	}

	[TestMethod]
	public void TheStaffLocatorOfAnEmployeeWindowStillPostsTheStaffHelp()
	{
		var hud = NewEmployeeHud( out var events, out var economy, StaffType.Guard );
		Assert.IsTrue( hud.OpenEmployeeWindow( economy.Staff.Members[0] ) );
		events.Clear();
		hud.OpenStaffLocator();
		Assert.AreEqual( 3, hud.Locator.Role );
		CollectionAssert.AreEqual( new[] { "post 198" }, events );
	}

	// ---- The feature windows -----------------------------------------------------------------------------------

	[TestMethod]
	public void TheFeatureWindowsPostTheirMessagesAndWithdrawThem()
	{
		Assert.AreEqual( 213, AdvisorUiHelp.MessageFor( ObjectInfoHelp.WindowName( ObjectInfoKind.Relief, 24.9, 1 ) ), "0x4979c3: the low byte below 0x19 posts 0xD5" );
		Assert.AreEqual( 299, AdvisorUiHelp.MessageFor( ObjectInfoHelp.WindowName( ObjectInfoKind.Relief, 25, 2 ) ), "0x4979d6: otherwise 0x12B" );
		Assert.AreEqual( 213, AdvisorUiHelp.MessageFor( ObjectInfoHelp.WindowName( ObjectInfoKind.Relief, 0, 3 ) ), "a state of 0 is below 25 (a ride's repair 0 is not low)" );
		Assert.AreEqual( 299, AdvisorUiHelp.MessageFor( ObjectInfoHelp.WindowName( ObjectInfoKind.Relief, null, 4 ) ) );
		Assert.AreEqual( 213, AdvisorUiHelp.MessageFor( ObjectInfoHelp.WindowName( ObjectInfoKind.Relief, 256 + 10, 5 ) ), "the original tests the low byte (cmp al, 0x19)" );
		CollectionAssert.AreEqual( new[] { 299, 213 }, AdvisorUiHelp.WithdrawnWith( ObjectInfoHelp.WindowName( ObjectInfoKind.Relief, 80, 1 ) ).ToArray(), "0x4976A0 then 0x4976AD" );
		Assert.AreEqual( 300, AdvisorUiHelp.MessageFor( ObjectInfoHelp.WindowName( ObjectInfoKind.Rest, 50, 1 ) ) );
		CollectionAssert.AreEqual( new[] { 300 }, AdvisorUiHelp.WithdrawnWith( ObjectInfoHelp.WindowName( ObjectInfoKind.Rest, 50, 1 ) ).ToArray() );
		Assert.AreEqual( 298, AdvisorUiHelp.MessageFor( ObjectInfoHelp.WindowName( ObjectInfoKind.Feature, 50, 1 ) ) );
		CollectionAssert.AreEqual( new[] { 298 }, AdvisorUiHelp.WithdrawnWith( ObjectInfoHelp.WindowName( ObjectInfoKind.Feature, 50, 1 ) ).ToArray() );
		Assert.IsTrue( new[] { 213, 298, 299, 300 }.All( AdvisorUiHelp.AllWindowMessages.Contains ) );
	}

	[TestMethod]
	[DataRow( "", 298, "Feature" )]
	[DataRow( "UsageInfo.ProvidesRelief 1\n", 299, "Relief" )]
	[DataRow( "UsageInfo.ChillsYouOut 1\n", 300, "Rest" )]
	[DataRow( "UsageInfo.ProvidesRelief 1\nUsageInfo.ChillsYouOut 1\n", 299, "Relief" )]
	public void AFeatureOpensTheWindowItsFlagsChoose( string extra, int message, string kind )
	{
		var hud = NewHud( out var events, out _, BuildCategory.Features, extra, recordWindows: true );
		Frame( hud );
		var opened = events.Single( entry => entry.StartsWith( "open objectInfo/", System.StringComparison.Ordinal ) );
		StringAssert.StartsWith( opened, $"open objectInfo/{kind}/" );
		Assert.AreEqual( message, AdvisorUiHelp.MessageFor( opened["open ".Length..] ), "the repair of a fresh object is not below 25" );
	}

	[TestMethod]
	public void AFixedItemHasNoWindowAndPostsNoHelp()
	{
		var hud = NewHud( out var events, out _, (BuildCategory)4, recordWindows: true );
		Frame( hud );
		Assert.IsFalse( events.Any( entry => entry.StartsWith( "open objectInfo/", System.StringComparison.Ordinal ) ), "0x486920: the jump table ends at kind 3" );
	}

	[TestMethod]
	public void ARideTypeAddOnOfKind2OpensItsOwnWindowWithHelp301()
	{
		Assert.AreEqual( 301, AdvisorUiHelp.MessageFor( ObjectInfoHelp.WindowName( ObjectInfoKind.AddOn, 50, 1 ) ) );
		CollectionAssert.AreEqual( new[] { 301 }, AdvisorUiHelp.WithdrawnWith( ObjectInfoHelp.WindowName( ObjectInfoKind.AddOn, 50, 1 ) ).ToArray(), "0x4B6850" );
		var hud = NewHud( out var events, out _, BuildCategory.Rides, "AddOn.UpgradeType 2\n", recordWindows: true );
		Frame( hud );
		StringAssert.StartsWith( events.Single( entry => entry.StartsWith( "open objectInfo/", System.StringComparison.Ordinal ) ), "open objectInfo/AddOn/", "type record +0x288 == 2 (0x486958)" );
	}

	[TestMethod]
	public void ATrackAddOnOfKind1KeepsTheRideWindow()
	{
		var hud = NewHud( out var events, out _, BuildCategory.Rides, "AddOn.UpgradeType 1\n", recordWindows: true );
		Frame( hud );
		StringAssert.StartsWith( events.Single( entry => entry.StartsWith( "open objectInfo/", System.StringComparison.Ordinal ) ), "open objectInfo/Ride" );
	}

	[TestMethod]
	public void TheThirdLaterClickThatEndsTheToolLeavesNoHelp214Pending()
	{
		var hud = NewHud( out var events, out var level );
		Frame( hud );
		var tool = level.CellTool;
		tool.Enter( new LineWriter( CellToolMode.Path ), null );
		Frame( hud );
		ToolClick( hud, tool, (2, 2) );
		ToolClick( hud, tool, (2, 6) );
		ToolClick( hud, tool, (6, 6) );
		Frame( hud );
		events.Clear();
		ToolClick( hud, tool, (6, 6) );
		Assert.IsFalse( tool.IsActive, "a click on the start ends the tool" );
		Frame( hud );
		CollectionAssert.AreEqual( new[] { "mode" }, events, "0xD6 is posted before the line is laid and the tool's end (SetMode(0)) withdraws it" );
	}

	// ---- The shipped tables ------------------------------------------------------------------------------------

	private static string ShippedData( string relative )
	{
		for ( var directory = new System.IO.DirectoryInfo( System.AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var path = System.IO.Path.Combine( directory.FullName, relative );
			if ( System.IO.File.Exists( path ) )
				return System.IO.File.ReadAllText( path );
		}
		Assert.Fail( $"{relative} not found above the test directory." );
		return "";
	}

	[TestMethod]
	public void EveryMessageTheHudPostsIsAGroupOneHelpDescriptorOfTheOriginalsTable()
	{
		var help = AdvisorHelpTable.Parse( ShippedData( AdvisorHelpTable.RelativePath ) ).ToDictionary( row => row.MessageId );
		var posted = AdvisorUiHelp.AllWindowMessages.Concat( AdvisorUiHelp.ModeHelpMessages ).Concat( AdvisorToolHelp.All ).Distinct().ToArray();
		Assert.AreEqual( 42, posted.Length, "forty-two of the 81 help messages have a producer here" );
		foreach ( var message in posted )
		{
			Assert.IsTrue( help.TryGetValue( message, out var row ), $"message {message} is a help descriptor" );
			Assert.AreEqual( 1, row.Group, $"message {message} is of the tutorial group" );
		}
	}

	[TestMethod]
	public void ASampleOfTheHelpTableMatchesTpExe()
	{
		// descriptor rows of 0x76E300 (filled by 0x5A0A50), read with the script of tools/ppc-analysis/lanes/advisor: (message, first response, game mode)
		var expected = new (int Message, int Response, byte Mode)[]
		{
			(184, 403, 2), (193, 412, 2), (195, 414, 0), (198, 417, 2), (200, 419, 0), (201, 420, 2), (203, 422, 2), (205, 424, 2), (206, 425, 2), (212, 431, 2), (213, 432, 2),
			(214, 433, 2), (284, 544, 2), (287, 547, 2), (289, 549, 2), (292, 552, 2), (293, 553, 2), (294, 554, 2), (295, 555, 2), (298, 558, 2), (299, 559, 2), (300, 560, 2),
			(301, 561, 2), (304, 564, 2), (305, 565, 2), (337, 601, 2), (345, 607, 2), (346, 608, 2)
		};
		var help = AdvisorHelpTable.Parse( ShippedData( AdvisorHelpTable.RelativePath ) ).ToDictionary( row => row.MessageId );
		foreach ( var (message, response, mode) in expected )
			Assert.AreEqual( (response, mode), (help[message].FirstResponseId, help[message].GameMode), $"message {message}" );
	}

	[TestMethod]
	public void TheDescriptorTablesScanFlagsAreThoseOfTheOriginal()
	{
		var rows = AdvisorDescriptorTable.Parse( ShippedData( AdvisorDescriptorTable.RelativePath ) );
		Assert.AreEqual( 155, rows.Count( row => row.Scan ), "0x59C680 scores the descriptors whose first word is 0" );
		Assert.AreEqual( 21, rows.Count( row => row.Scan && row.Callback == 0x5D60C0 ), "21 of them score 0" );
		Assert.AreEqual( 134, rows.Count( row => row.Scan && row.Callback != 0x5D60C0 ), "134 compute a score" );
		Assert.IsTrue( rows.Where( row => row.Callback == 0x59E8C0 ).All( row => !row.Scan ), "the help messages are skipped by the scan" );
		foreach ( var message in new[] { 0, 66, 68, 69, 70, 93, 94, 95, 96, 97, 106, 125, 126, 127, 128, 129, 136, 141, 167, 215, 237, 271, 311, 315, 323 } )
			Assert.IsFalse( rows[message].Scan && message != 141, $"game code posts {message}" );
	}
}
