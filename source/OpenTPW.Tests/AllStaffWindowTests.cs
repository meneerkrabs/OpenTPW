using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The All Staff window (tp.exe 0x496620, table 0x750e10, window procedure 0x495da0, row adder 0x496320, row template 0x496d20): the table, the role tabs, the columns, the row order,
/// the two average-happiness bars, the entries that open it and what a row click does.
/// </summary>
[TestClass, DoNotParallelize]
public class AllStaffWindowTests
{
	private static UiContext Context( float delta ) => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Delta = delta };

	private static ParkHud HudWith( out ParkEconomy economy, params StaffType[] roles )
	{
		economy = EconomyTestData.Park();
		foreach ( var role in roles )
			EconomyTestData.HireBest( economy, role );
		return StaffWindowTests.Hud( economy );
	}

	private static void Tick( ParkHud hud, float seconds ) => hud.Stack.Top!.Update( Context( seconds ), UiInput.Idle( System.Numerics.Vector2.Zero ) );

	private static int[] Rows( ParkHud hud ) => hud.AllStaffItems.Select( member => member.Id ).ToArray();

	private static AllStaffRowValues Row( string name, string status = "Idle", long wage = 0, int skill = 0, int happiness = 0 ) => new( name, status, wage, skill, happiness );

	private static string SourceText( string file )
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "source", "OpenTPW", "Hud", "ParkHud.cs" ) ) )
			directory = directory.Parent;
		return File.ReadAllText( Path.Combine( directory!.FullName, "source", "OpenTPW", "Hud", file ) ).Replace( "\r\n", "\n" );
	}

	// ---- the table ----

	[TestMethod]
	public void TheTableIsTheOneAt0x750e10()
	{
		var table = OriginalLayoutTables.AllStaffWindow;
		Assert.AreEqual( 0x750E10, table.Address );
		Assert.AreEqual( 0x320u, table.Root.Id );
		Assert.AreEqual( "window4", table.Root.Model );
		Assert.AreEqual( new UiRect( 186, 30, 2018 - 186, 1007 - 30 ), table.Root.Rect );
		var list = table.Control( 0x321 );
		Assert.AreEqual( "list_allstaff", list.Model );
		Assert.AreEqual( new UiRect( 294, 418, 1734 - 294, 777 - 418 ), list.Inner, "the rows' region" );
		Assert.AreEqual( 124, list.Help );
		var headers = list.Children.Where( node => node.Type == 2 && node.Id is >= 0x10 and < 0x20 ).OrderBy( node => node.Id ).ToArray();
		Assert.AreEqual( 5, headers.Length, "one header per column" );
		Assert.AreEqual( new UiRect( 294, 306, 716 - 294, 412 - 306 ), headers[0].Rect );
		Assert.AreEqual( new UiRect( 1483, 306, 1733 - 1483, 411 - 306 ), headers[4].Rect );
		Assert.AreEqual( "!slider", list.Children.Single( node => node.Type == 3 ).Model2 );
		CollectionAssert.AreEqual( new[] { "b_shandy", "b_smech", "b_senter", "b_sguard", "b_sresrcher" },
			AllStaffRoster.Tabs.Select( tab => table.Control( tab.Control ).Model! ).ToArray(), "the tab controls carry the role buttons" );
		CollectionAssert.AreEqual( new[] { 125, 126, 127 }, new[] { "b_kids", "b_allthings", "b_pkinfo" }.Select( model => table.Model( model ).Help ).ToArray() );
		Assert.AreEqual( "!frame", table.Control( 0x329 ).Model, "the frame of the two happiness bars" );
	}

	// ---- the model ----

	[TestMethod]
	public void TheTabsFollowTheOriginalOrderAndMapBackFromTheRole()
	{
		// 0x4968be: the tab's button; 0x496be8: the label; 0x496bfc: the thing type; 0x506490: thing type to tab
		CollectionAssert.AreEqual( new uint[] { 0x323, 0x326, 0x325, 0x327, 0x324 }, AllStaffRoster.Tabs.Select( tab => tab.Control ).ToArray() );
		CollectionAssert.AreEqual( new[] { 107, 108, 110, 109, 111 }, AllStaffRoster.Tabs.Select( tab => (int)tab.Label ).ToArray(), "UITEXT 0x6b, 0x6c, 0x6e, 0x6d, 0x6f" );
		CollectionAssert.AreEqual( new[] { 5, 4, 6, 7, 8 }, AllStaffRoster.Tabs.Select( tab => tab.ThingType ).ToArray() );
		CollectionAssert.AreEqual( new[] { 114, 115, 116, 117, 118 }, AllStaffRoster.Tabs.Select( tab => tab.Help ).ToArray() );
		CollectionAssert.AreEqual( new[] { StaffType.Handyman, StaffType.Mechanic, StaffType.Entertainer, StaffType.Guard, StaffType.Researcher }, AllStaffRoster.Tabs.Select( tab => tab.Type ).ToArray() );
		foreach ( var type in Enum.GetValues<StaffType>() )
			Assert.AreEqual( (int)type, AllStaffRoster.TabOf( type ), $"{type} lists under the tab of its own number" );
	}

	[TestMethod]
	public void TheColumnsAreTheFiveOriginalOnes()
	{
		// 0x496620: headers UITEXT 0x65-0x69, help 0x77-0x7b; 0x6636b2: columns 0 and 1 compare text, 2 to 4 numbers; 0x496d20: alignments
		CollectionAssert.AreEqual( new[] { 101, 102, 103, 104, 105 }, AllStaffRoster.Columns.Select( column => (int)column.Header ).ToArray() );
		CollectionAssert.AreEqual( new[] { 119, 120, 121, 122, 123 }, AllStaffRoster.Columns.Select( column => column.Help ).ToArray() );
		CollectionAssert.AreEqual( new[] { false, false, true, true, true }, AllStaffRoster.Columns.Select( column => column.Numeric ).ToArray() );
		CollectionAssert.AreEqual( new[] { UiAlign.Left, UiAlign.Left, UiAlign.Right }, AllStaffRoster.Columns.Take( 3 ).Select( column => column.Align ).ToArray() );
	}

	[TestMethod]
	public void ATabArgumentOfMinusOneKeepsTheLastTab()
	{
		Assert.AreEqual( 3, AllStaffRoster.ResolveTab( -1, 3 ), "0x49681b: -1 takes the remembered tab" );
		Assert.AreEqual( 1, AllStaffRoster.ResolveTab( 1, 3 ), "another tab replaces it (0x496826)" );
		Assert.AreEqual( 1, AllStaffRoster.SortWord( 0, false ), "the sort word 0x75118c starts at 1" );
		Assert.AreEqual( -3, AllStaffRoster.SortWord( 2, true ) );
	}

	[TestMethod]
	public void TextColumnsCompareTheTextOrdinallyAndNumberColumnsTheNumbers()
	{
		Assert.IsTrue( Row( "Zed" ).CompareTo( Row( "abe" ), 0 ) < 0, "wcscmp compares code units: Z (90) before a (97)" );
		Assert.IsTrue( Row( "a", status: "Resting" ).CompareTo( Row( "a", status: "Idle" ), 1 ) > 0, "the status column compares the status text" );
		Assert.IsTrue( Row( "a", wage: 90 ).CompareTo( Row( "b", wage: 100 ), 2 ) < 0, "90 before 100 as numbers, not as text" );
		Assert.IsTrue( Row( "a", skill: 300 ).CompareTo( Row( "b", skill: 200 ), 3 ) > 0 );
		Assert.AreEqual( 0, Row( "a", happiness: 5 ).CompareTo( Row( "b", happiness: 5 ), 4 ) );
	}

	[TestMethod]
	public void ANewRowGoesBeforeTheFirstRowItSortsStrictlyBefore()
	{
		// 0x66403b with the compare 0x663edc: equal rows keep the order they were added in, in either direction
		var values = new Dictionary<int, AllStaffRowValues>
		{
			[1] = Row( "a", wage: 20 ), [2] = Row( "b", wage: 10 ), [3] = Row( "c", wage: 20 ), [4] = Row( "d", wage: 30 ), [5] = Row( "e", wage: 10 ),
		};
		var ascending = new List<int>();
		foreach ( var id in new[] { 1, 2, 3, 4, 5 } )
			AllStaffRoster.SortedInsert( ascending, id, key => values[key], 2, descending: false );
		CollectionAssert.AreEqual( new[] { 2, 5, 1, 3, 4 }, ascending, "added in the order 1..5, equal wages keep it" );
		var descending = new List<int>();
		foreach ( var id in new[] { 1, 2, 3, 4, 5 } )
			AllStaffRoster.SortedInsert( descending, id, key => values[key], 2, descending: true );
		CollectionAssert.AreEqual( new[] { 4, 1, 3, 2, 5 }, descending );
	}

	[TestMethod]
	public void AHeaderPressSortsTheRowsInTheirCurrentOrderSoEqualRowsStayWhereTheyWere()
	{
		// 0x6628f4: a selection sort over the rows as they stand, taking the first smallest (largest) each pass: not a re-sort by id
		var values = new Dictionary<int, AllStaffRowValues> { [1] = Row( "m", wage: 50 ), [2] = Row( "z", wage: 50 ), [3] = Row( "a", wage: 50 ), [4] = Row( "q", wage: 10 ) };
		var current = new[] { 3, 1, 2, 4 };
		CollectionAssert.AreEqual( new[] { 4, 3, 1, 2 }, AllStaffRoster.Resort( current, key => values[key], 2, descending: false ) );
		CollectionAssert.AreEqual( new[] { 3, 1, 2, 4 }, AllStaffRoster.Resort( current, key => values[key], 2, descending: true ), "the descending press leaves the equal rows in the current order too" );
		CollectionAssert.AreEqual( new[] { 3 }, AllStaffRoster.Resort( new[] { 3 }, key => values[key], 2, descending: false ) );
	}

	[TestMethod]
	public void TheFillVisitsTheRoleNewestFirst()
	{
		// 0x481bc0 follows the role's chain from its head, and a new thing becomes the head (0x51965e, 0x519aa0)
		var economy = EconomyTestData.Park();
		var first = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		var other = EconomyTestData.HireBest( economy, StaffType.Guard );
		var second = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		var visited = AllStaffRoster.Visit( economy.Staff.Members, StaffType.Mechanic, economy.Staff.Things );
		CollectionAssert.AreEqual( new[] { second.Id, first.Id }, visited.Select( member => member.Id ).ToArray(), "only the tab's role, the member hired last first" );
		Assert.IsFalse( visited.Any( member => member.Id == other.Id ) );
	}

	[TestMethod]
	public void TheRoleBarIsAnIntegerAverageAndTheAllStaffBarAFractionalOne()
	{
		StaffMember Member( StaffType type, float happiness ) => new() { Id = 1, Type = type, NameIndex = 0, Grade = 0, Happiness = happiness };
		var members = new[] { Member( StaffType.Handyman, 40.9f ), Member( StaffType.Handyman, 80 ), Member( StaffType.Mechanic, 100 ) };
		// 0x4c9cc0: (40 + 80) / 2 = 60, then × 1024 / 100
		Assert.AreEqual( (60 << 10) / 100, AllStaffRoster.RoleHappinessGauge( members, StaffType.Handyman ) );
		Assert.AreEqual( 0, AllStaffRoster.RoleHappinessGauge( members, StaffType.Guard ), "no member of the role reads 0" );
		var odd = new[] { Member( StaffType.Guard, 10 ), Member( StaffType.Guard, 11 ) };
		Assert.AreEqual( (10 << 10) / 100, AllStaffRoster.RoleHappinessGauge( odd, StaffType.Guard ), "the role's average is an integer division: 10.5 reads 10" );
		// 0x4c7cc0: 220 / 3 as a float, × 1024.0 × 0.01f, truncated
		Assert.AreEqual( 750, AllStaffRoster.AllHappinessGauge( members ) );
		Assert.AreEqual( 1023, AllStaffRoster.AllHappinessGauge( new[] { Member( StaffType.Handyman, 100 ) } ), "the single 0.01f is a little below 0.01, so a full 100 truncates to 1023" );
		Assert.AreEqual( 0, AllStaffRoster.AllHappinessGauge( Array.Empty<StaffMember>() ), "no staff: the constant 0.0 at 0x70031c" );
	}

	[TestMethod]
	public void ARefreshRewritesStatusSkillAndHappinessAndKeepsNameAndWage()
	{
		// 0x495de0: the mask 0x1a is columns 1, 3 and 4
		var refreshed = AllStaffRoster.Refreshed( Row( "Ann", "Idle", 90, 100, 200 ), Row( "Annie", "Working", 120, 300, 400 ) );
		Assert.AreEqual( new AllStaffRowValues( "Ann", "Working", 90, 300, 400 ), refreshed );
	}

	// ---- the window ----

	[TestMethod]
	public void TheWindowIsBuiltFromTheTableAndPostsAdvisorHelp285()
	{
		var hud = HudWith( out _, StaffType.Handyman );
		hud.OpenAllStaffWindow();
		var screen = hud.Stack.Top!;
		var table = OriginalLayoutTables.AllStaffWindow;
		Assert.AreEqual( ParkHud.AllStaffScreenName, screen.Name );
		Assert.IsTrue( hud.AllStaffWindowOpen );
		Assert.AreEqual( 285, AdvisorUiHelp.MessageFor( screen.Name ), "0x496ba3 posts 0x11d" );
		Assert.IsTrue( AdvisorUiHelp.WithdrawnWith( screen.Name ).Contains( 285 ), "message 0x14 withdraws it (0x495f3b)" );
		Assert.IsTrue( AdvisorUiHelp.RepeatsTutorialOnHelpKey( screen.Name ), "the procedure hands the other keys to 0x488ba0 (0x495ee5)" );
		Assert.AreEqual( table.Root.Rect, screen.Find( "window" )!.Bounds );
		Assert.AreEqual( table.Control( 0x328 ).Rect, screen.Find( "title" )!.Bounds );
		Assert.AreEqual( new UiRect( 294, 418, 1734 - 294, 777 - 418 ), screen.Find( "allStaffList" )!.Bounds );
		Assert.AreEqual( "ui100", ((UiLabel)screen.Find( "title" )!).Text(), "UITEXT 0x64" );
		for ( var column = 0; column < 5; column++ )
		{
			var header = screen.Find( $"header{column}" )!;
			Assert.AreEqual( table.Control( (uint)(0x10 + column) ).Rect, header.Bounds );
			Assert.AreEqual( $"help{119 + column}", header.CurrentHelp, "help 0x77-0x7b" );
		}
		foreach ( var (id, model) in new[] { ("exit", "b_okay"), ("parkInfo", "b_pkinfo"), ("allVisitors", "b_kids"), ("allAttractions", "b_allthings") } )
		{
			var button = (UiButton)screen.Find( id )!;
			Assert.AreEqual( model, button.Model, id );
			Assert.AreEqual( table.Model( model ).Rect, button.Bounds, id );
			Assert.AreEqual( $"help{table.Model( model ).Help}", button.CurrentHelp, id );
		}
		for ( var tab = 0; tab < 5; tab++ )
		{
			var button = (UiButton)screen.Find( $"tab{tab}" )!;
			Assert.AreEqual( AllStaffRoster.Tabs[tab].Model, button.Model );
			Assert.AreEqual( table.Control( AllStaffRoster.Tabs[tab].Control ).Rect, button.Bounds );
			Assert.AreEqual( $"help{114 + tab}", button.CurrentHelp );
			Assert.AreEqual( 0xBDu, button.ClickSound, "the radio group's sound" );
		}
	}

	[TestMethod]
	public void TheRowsHoldTheCellsOfTheRowAdder()
	{
		// 0x496320: name, status text (0 for a patrolling member without an own area), wage, skill and happiness bars
		var hud = HudWith( out var economy, StaffType.Handyman );
		var member = economy.Staff.Members[0];
		member.Name = "Ann";
		member.State = StaffState.Patrolling;
		member.Happiness = 55.7f;
		hud.OpenAllStaffWindow();
		var row = hud.AllStaffRowOf( member.Id )!.Value;
		Assert.AreEqual( "Ann", row.Name );
		Assert.AreEqual( "Idle", row.Status, "patrolling without an own patrol area shows as idle (0x506eb0)" );
		Assert.AreEqual( economy.Staff.MonthlyWage( member ), row.Wage );
		Assert.AreEqual( ParkStaff.StaffSkill( member ) * 1024 / 100, row.SkillGauge );
		Assert.AreEqual( (55 << 10) / 100, row.HappinessGauge, "the low byte of the truncated happiness × 1024 / 100" );
		hud.CloseAllStaffWindow();
		member.SetPatrolArea( (1, 1), (4, 4) );
		hud.OpenAllStaffWindow();
		Assert.AreEqual( "Patrolling", hud.AllStaffRowOf( member.Id )!.Value.Status );
	}

	[TestMethod]
	public void AnEmptyTabListsNobodyAndAPressFillsItFromItsRole()
	{
		var hud = HudWith( out var economy, StaffType.Handyman, StaffType.Mechanic, StaffType.Mechanic );
		hud.OpenAllStaffWindow();
		Assert.AreEqual( 0, hud.AllStaffTab, "the first tab on the first opening" );
		Assert.AreEqual( 1, hud.AllStaffItems.Count );
		((UiButton)hud.Stack.Top!.Find( "tab1" )!).Clicked!();
		Assert.AreEqual( 1, hud.AllStaffTab );
		Assert.AreEqual( 2, hud.AllStaffItems.Count );
		Assert.IsTrue( hud.AllStaffItems.All( member => member.Type == StaffType.Mechanic ) );
		Assert.IsTrue( ((UiButton)hud.Stack.Top!.Find( "tab1" )!).Selected!() && !((UiButton)hud.Stack.Top!.Find( "tab0" )!).Selected!() );
		((UiButton)hud.Stack.Top!.Find( "tab3" )!).Clicked!();
		Assert.AreEqual( 0, hud.AllStaffItems.Count, "nobody in the guards' tab" );
		Assert.AreEqual( 0, hud.SelectedAllStaffId );
		Assert.IsNotNull( economy );
	}

	[TestMethod]
	public void ATabPressSelectsTheFirstRowAndScrollsToTheTop()
	{
		var hud = HudWith( out var economy, StaffType.Mechanic, StaffType.Mechanic, StaffType.Mechanic );
		hud.OpenAllStaffWindow( 1 );
		hud.SelectAllStaff( 2 );
		hud.SetAllStaffFirstRow( 1 );
		var first = hud.AllStaffItems[0].Id;
		Assert.AreNotEqual( first, hud.SelectedAllStaffId );
		((UiButton)hud.Stack.Top!.Find( "tab1" )!).Clicked!();
		Assert.AreEqual( first, hud.SelectedAllStaffId, "the first row added is chosen (0x66403b → 0x66525c)" );
		Assert.AreEqual( 0, hud.AllStaffFirstRow );
		Assert.IsNotNull( economy );
	}

	[TestMethod]
	public void TheTabIsKeptForTheSessionAndAMinusOneReopeningShowsIt()
	{
		var hud = HudWith( out _, StaffType.Entertainer );
		hud.OpenAllStaffWindow();
		((UiButton)hud.Stack.Top!.Find( "tab2" )!).Clicked!();
		hud.CloseAllStaffWindow();
		Assert.IsFalse( hud.AllStaffWindowOpen );
		hud.OpenAllStaffWindow();
		Assert.AreEqual( 2, hud.AllStaffTab, "0x7ca16c is read back for the argument -1" );
		Assert.AreEqual( 1, hud.AllStaffItems.Count );
		hud.CloseAllStaffWindow();
		hud.OpenAllStaffWindow( 4 );
		Assert.AreEqual( 4, hud.AllStaffTab, "an explicit tab replaces it" );
	}

	[TestMethod]
	public void AMemberHandedToTheBuilderIsSelected()
	{
		var hud = HudWith( out var economy, StaffType.Guard, StaffType.Guard, StaffType.Guard );
		var chosen = economy.Staff.Members.OrderBy( member => member.Id ).Last();
		chosen.Name = "Zed";
		hud.OpenAllStaffWindow( AllStaffRoster.TabOf( chosen.Type ), chosen );
		Assert.AreEqual( 3, hud.AllStaffTab );
		Assert.AreEqual( chosen.Id, hud.SelectedAllStaffId, "0x664d2a finds the row, 0x665739 selects it" );
		Assert.AreNotEqual( hud.AllStaffItems[0].Id, hud.SelectedAllStaffId, "the last name is not the first row" );
	}

	[TestMethod]
	public void AHeaderPressSortsAndTheSortIsKeptForTheSession()
	{
		var hud = HudWith( out var economy, StaffType.Handyman, StaffType.Handyman, StaffType.Handyman );
		var members = economy.Staff.Members.OrderBy( member => member.Id ).ToArray();
		(members[0].Name, members[1].Name, members[2].Name) = ("Mid", "Zed", "Abe");
		foreach ( var member in members )
			member.Grade = 1;
		members[1].Grade = 2;
		hud.OpenAllStaffWindow();
		CollectionAssert.AreEqual( new[] { members[2].Id, members[0].Id, members[1].Id }, Rows( hud ), "sort word 1: the name ascending" );
		Assert.AreEqual( (0, false), hud.AllStaffSort );
		hud.Stack.Top!.Find( "header0" )!.Activate();
		CollectionAssert.AreEqual( new[] { members[1].Id, members[0].Id, members[2].Id }, Rows( hud ), "the first press on column 0 descends" );
		hud.Stack.Top!.Find( "header2" )!.Activate();
		Assert.AreEqual( (2, false), hud.AllStaffSort, "a new column starts ascending" );
		CollectionAssert.AreEqual( new[] { members[0].Id, members[2].Id, members[1].Id }, Rows( hud ).ToArray(), "the equal wages stay in the order they had (Mid, Abe), the dearer member last" );
		hud.Stack.Top!.Find( "header2" )!.Activate();
		Assert.AreEqual( (2, true), hud.AllStaffSort );
		CollectionAssert.AreEqual( new[] { members[1].Id, members[0].Id, members[2].Id }, Rows( hud ).ToArray() );
		hud.CloseAllStaffWindow();
		hud.OpenAllStaffWindow();
		Assert.AreEqual( (2, true), hud.AllStaffSort, "message 0x406 wrote the sort word back (0x495fe0)" );
		CollectionAssert.AreEqual( new[] { members[1].Id, members[2].Id, members[0].Id }, Rows( hud ), "the reopening fills by the sorted insert in the fill's order, newest first among equals" );
	}

	[TestMethod]
	public void TheStatusColumnSortsByTheStatusText()
	{
		var hud = HudWith( out var economy, StaffType.Guard, StaffType.Guard, StaffType.Guard );
		var members = economy.Staff.Members.OrderBy( member => member.Id ).ToArray();
		(members[0].State, members[1].State, members[2].State) = (StaffState.Working, StaffState.Resting, StaffState.Idle);
		hud.OpenAllStaffWindow( 3 );
		hud.Stack.Top!.Find( "header1" )!.Activate();
		CollectionAssert.AreEqual( new[] { members[2].Id, members[1].Id, members[0].Id }, Rows( hud ), "Idle, Resting, Working" );
	}

	[TestMethod]
	public void TheTwoSecondRefreshRewritesTheValuesInPlaceAndNeverMovesARow()
	{
		var hud = HudWith( out var economy, StaffType.Handyman, StaffType.Handyman );
		var members = economy.Staff.Members.OrderBy( member => member.Id ).ToArray();
		(members[0].Name, members[1].Name) = ("Abe", "Bob");
		hud.OpenAllStaffWindow();
		var wage = hud.AllStaffRowOf( members[0].Id )!.Value.Wage;
		members[0].Happiness = 10;
		members[0].State = StaffState.Working;
		members[0].Grade += 1;
		Tick( hud, 1.9f );
		Assert.AreEqual( "Idle", hud.AllStaffRowOf( members[0].Id )!.Value.Status, "nothing is rewritten before the timer fires" );
		Tick( hud, 0.2f );
		var row = hud.AllStaffRowOf( members[0].Id )!.Value;
		Assert.AreEqual( "Working", row.Status );
		Assert.AreEqual( (10 << 10) / 100, row.HappinessGauge );
		Assert.AreEqual( ParkStaff.StaffSkill( members[0] ) * 1024 / 100, row.SkillGauge );
		Assert.AreEqual( wage, row.Wage, "the wage cell is only written when the row is added (mask 0x1a)" );
		CollectionAssert.AreEqual( new[] { members[0].Id, members[1].Id }, Rows( hud ) );
		members[0].Name = "Zed";
		hud.Stack.Top!.Find( "header0" )!.Activate();
		hud.Stack.Top!.Find( "header0" )!.Activate();
		Assert.AreEqual( "Abe", hud.AllStaffRowOf( members[0].Id )!.Value.Name, "the name is the one the row was added with" );
	}

	[TestMethod]
	public void TheHappinessBarsAreTakenWhenTheWindowOpensAndOnATabPressOnly()
	{
		// 0x496a8f and 0x496b71 set the bars in the builder; the 0x80083 timer handler (0x495de0) refreshes the rows alone
		var hud = HudWith( out var economy, StaffType.Handyman, StaffType.Handyman, StaffType.Mechanic );
		var handymen = economy.Staff.Members.Where( member => member.Type == StaffType.Handyman ).ToArray();
		(handymen[0].Happiness, handymen[1].Happiness) = (40, 80);
		economy.Staff.Members.Single( member => member.Type == StaffType.Mechanic ).Happiness = 100;
		hud.OpenAllStaffWindow( 0 );
		Assert.AreEqual( ((60 << 10) / 100, 750), hud.AllStaffHappinessBars );
		Assert.AreEqual( "ui107", hud.AllStaffRoleLabel, "the cleaners' label (UITEXT 0x6b)" );
		Assert.AreEqual( "ui106", ((UiLabel)hud.Stack.Top!.Find( "allLabel" )!).Text(), "UITEXT 0x6a" );
		handymen[0].Happiness = 0;
		Tick( hud, 2.1f );
		Assert.AreEqual( ((60 << 10) / 100, 750), hud.AllStaffHappinessBars, "the timer leaves the bars" );
		((UiButton)hud.Stack.Top!.Find( "tab0" )!).Clicked!();
		Assert.AreEqual( ((40 << 10) / 100, 614), hud.AllStaffHappinessBars, "a tab press takes them again, even on the tab already shown" );
		((UiButton)hud.Stack.Top!.Find( "tab1" )!).Clicked!();
		Assert.AreEqual( ((100 << 10) / 100, 614), hud.AllStaffHappinessBars );
		Assert.AreEqual( "ui108", hud.AllStaffRoleLabel, "the mechanics' label" );
	}

	[TestMethod]
	public void AMemberWhoLeavesTakesTheRowAndTheSelectionMovesToTheFirstRow()
	{
		var hud = HudWith( out var economy, StaffType.Researcher, StaffType.Researcher );
		var members = economy.Staff.Members.OrderBy( member => member.Id ).ToArray();
		(members[0].Name, members[1].Name) = ("Abe", "Bob");
		hud.OpenAllStaffWindow( 4, members[1] );
		Assert.AreEqual( members[1].Id, hud.SelectedAllStaffId );
		economy.Fire( members[1].Id );
		Tick( hud, 0.1f );
		CollectionAssert.AreEqual( new[] { members[0].Id }, Rows( hud ) );
		Assert.AreEqual( members[0].Id, hud.SelectedAllStaffId, "[EXT:list-selection-by-id]" );
	}

	// ---- the row messages ----

	[TestMethod]
	public void ALeftClickOnARowOpensTheEmployeeWindowInPlaceOfThisOne()
	{
		var hud = HudWith( out var economy, StaffType.Mechanic, StaffType.Mechanic );
		var members = economy.Staff.Members.OrderBy( member => member.Id ).ToArray();
		(members[0].Name, members[1].Name) = ("Abe", "Bob");
		hud.OpenAllStaffWindow( 1 );
		var list = (IUiRowList)hud.Stack.Top!.Find( "allStaffList" )!;
		Assert.IsTrue( list.RowClicked( 1, left: true ), "0x400 → 0x4b6080" );
		Assert.IsTrue( hud.EmployeeWindowOpen );
		Assert.IsFalse( hud.AllStaffWindowOpen, "the opener closes the open window (0x48cea0)" );
		Assert.AreSame( members[1], hud.Employee );
		Assert.AreEqual( 192, AdvisorUiHelp.MessageFor( "employee" ) );
	}

	[TestMethod]
	public void ARightClickSelectsTheRowAndClosesTheWindowWhetherOrNotTheCameraMoves()
	{
		var hud = HudWith( out var economy, StaffType.Guard, StaffType.Guard );
		var members = economy.Staff.Members.OrderBy( member => member.Id ).ToArray();
		(members[0].Name, members[1].Name) = ("Abe", "Bob");
		hud.OpenAllStaffWindow( 3 );
		var list = (IUiRowList)hud.Stack.Top!.Find( "allStaffList" )!;
		Assert.IsTrue( list.RowClicked( 1, left: false ), "0x402" );
		Assert.AreEqual( members[1].Id, hud.SelectedAllStaffId, "the click selects the row first" );
		Assert.IsFalse( hud.AllStaffWindowOpen, "0x658d9f closes the window after 0x4867b0" );
		Assert.IsFalse( hud.EmployeeWindowOpen );
	}

	[TestMethod]
	public void TheEmployeeWindowsAllStaffButtonOpensTheRolesTabWithTheEmployeeSelected()
	{
		var hud = HudWith( out var economy, StaffType.Handyman, StaffType.Researcher, StaffType.Researcher );
		var researcher = economy.Staff.Members.Where( member => member.Type == StaffType.Researcher ).OrderBy( member => member.Id ).Last();
		hud.OpenEmployeeWindow( researcher );
		hud.ShowAllStaffOfEmployee();
		Assert.IsFalse( hud.EmployeeWindowOpen );
		Assert.AreEqual( (4, researcher.Id), (hud.AllStaffTab, hud.SelectedAllStaffId), "0x4b5dd9 passes 0x506490 of the thing and the thing" );
	}

	// ---- the entries ----

	[TestMethod]
	public void TheInfoButtonOpensTheInfoWindowThatWasOpenLast()
	{
		// 0x4a0810 records the kind in 0x7cb2a0 as each of the four windows opens (3 park information, 4 All Staff, 5 attractions, 6 visitors); 0x4a098a opens it again
		var hud = HudWith( out _, StaffType.Handyman );
		hud.OpenLastInfoWindow();
		Assert.IsTrue( hud.ParkInfoWindowOpen, "the HUD starts it at the park information (0x4a1dbe)" );
		hud.CloseParkInfoWindow();
		hud.OpenAllStaffWindow();
		hud.CloseAllStaffWindow();
		hud.OpenLastInfoWindow();
		Assert.IsTrue( hud.AllStaffWindowOpen && !hud.ParkInfoWindowOpen, "after the All Staff window: 0x496620 with -1" );
		hud.CloseAllStaffWindow();
		hud.OpenVisitorWindow();
		hud.CloseVisitorWindow();
		hud.OpenLastInfoWindow();
		Assert.IsTrue( hud.VisitorWindowOpen, "after the All Visitors window" );
		hud.CloseVisitorWindow();
		hud.OpenParkInfoWindow();
		hud.CloseParkInfoWindow();
		hud.OpenLastInfoWindow();
		Assert.IsTrue( hud.ParkInfoWindowOpen, "after the park information" );
	}

	[TestMethod]
	public void TheHudsInfoButtonAndEveryInfoWindowOpenerUseTheMemory()
	{
		Assert.IsTrue( SourceText( "ParkHud.cs" ).Contains( "\"info\", \"b_info\", Rect( 287, 1133, 405, 1252 ), 470, OpenLastInfoWindow )" ), "the info button" );
		Assert.IsTrue( SourceText( "ParkHud.InfoWindow.cs" ).Contains( "RememberInfoWindow( InfoWindowKind.ParkInfo );" ) );
		Assert.IsTrue( SourceText( "ParkHud.AttractionsWindow.cs" ).Contains( "RememberInfoWindow( InfoWindowKind.Attractions );" ) );
		Assert.IsTrue( SourceText( "ParkHud.VisitorWindow.cs" ).Contains( "RememberInfoWindow( InfoWindowKind.Visitors );" ) );
		Assert.IsTrue( SourceText( "ParkHud.AllStaffWindow.cs" ).Contains( "RememberInfoWindow( InfoWindowKind.AllStaff );" ) );
	}

	[TestMethod]
	public void TheVisitorsAttractionsAndParkInformationButtonsOpenThisWindowOnItsLastTab()
	{
		foreach ( var source in new[] { "ParkHud.InfoWindow.cs", "ParkHud.AttractionsWindow.cs", "ParkHud.VisitorWindow.cs" } )
		{
			var text = SourceText( source );
			Assert.IsTrue( text.Contains( "OpenAllStaffWindow(); }" ), $"{source}: b_allstaff runs 0x496620 with -1" );
			Assert.IsFalse( text.Contains( "OpenStaffWindow(); ShowEmployedStaff( true )" ), $"{source}: no longer the hire window's employee view" );
		}
	}

	[TestMethod]
	public void TheHireWindowKeepsItsEmployeeViewStandIn()
	{
		var hud = HudWith( out var economy, StaffType.Mechanic );
		hud.OpenStaffWindow();
		hud.ShowEmployedStaff( true );
		hud.FilterStaff( StaffType.Mechanic );
		Assert.IsTrue( hud.StaffWindowOpen && hud.ShowingEmployedStaff );
		Assert.AreEqual( 1, hud.StaffItems.Count );
		var text = SourceText( "ParkHud.Staff.cs" );
		Assert.IsTrue( text.Contains( "[EXT:staff-employee-view]" ), "the stand-in stays until the user decides" );
		Assert.IsTrue( text.Contains( "separate All Staff window exists" ), "its comment says the original's window is ported" );
		Assert.IsNotNull( economy );
	}
}
