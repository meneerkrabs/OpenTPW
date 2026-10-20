using System.Linq;
using OpenTPW.UI.Original;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>The hire window's mini balance and the per-employee window (tp.exe 0x49bdd0 and 0x4b6080, table 0x756f48).</summary>
[TestClass]
[DoNotParallelize]
public class StaffWindowTests
{
	private static void Set( object o, string field, object? value ) => o.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( o, value );

	/// <summary>A HUD over a guest simulation and <paramref name="economy"/>.</summary>
	internal static ParkHud Hud( ParkEconomy economy )
	{
		var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
		Set( hud, "<Status>k__BackingField", new EconomyParkStatus( () => economy ) );
		CommandTestLevel.AttachPark( (Level)typeof( ParkHud ).GetField( "level", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( hud )!, economy );
		// the language tables the windows read (no game data in the unit tests)
		Set( hud, "staffNames", System.Enum.GetValues<StaffType>().ToDictionary( type => type, _ => System.Array.Empty<string>() ) );
		Set( hud, "staffTypes", System.Array.Empty<string>() );
		Set( hud, "staffStates", new[] { "Idle", "Patrolling", "Working", "Resting", "On strike", "Picked up" } );
		return hud;
	}

	[TestMethod]
	public void TheHireBalanceReadsTheLastClosedMonthNotTheCurrentOne()
	{
		Assert.AreEqual( (0L, 0L, 0L), ParkHud.HireBalanceSample( System.Array.Empty<LedgerMonth>() ), "an empty sample ring reads 0 (index -1, not wrapped)" );
		Assert.AreEqual( (500L, 120L + 40L, 300L - 120L, 500L - 40L - 300L), ParkHud.HireBalance( (500, 120, 300), 40 ),
			"staff costs + wage, money out - staff costs, money in - wage - money out" );

		var economy = EconomyTestData.Park();
		var mechanic = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		economy.AdvanceMonths( 1 );
		var closed = economy.Ledger.History[^1];
		var wage = economy.Settings.GetMonthlyWage( StaffType.Mechanic, mechanic.Grade );
		Assert.AreEqual( wage, closed[LedgerCategory.StaffCosts], "the closed month holds the wage" );
		var hud = Hud( economy );
		hud.OpenStaffWindow();
		var candidate = hud.StaffCandidates.First();
		var candidateWage = economy.Settings.GetMonthlyWage( candidate.Type, candidate.Grade );
		Assert.AreEqual( ParkHud.HireBalance( (closed.MoneyIn, wage, closed.MoneyOut), candidateWage ), hud.CandidateBalance( candidate ) );
		Assert.AreEqual( 0, economy.Ledger.CurrentMonth( economy.ParkRating, economy.ParkValue )[LedgerCategory.StaffCosts], "the current month has no wage yet, so it is not the one shown" );
	}

	[TestMethod]
	public void TheTrendArrowShowsWhetherTheLastClosedMonthLostMoney()
	{
		// [BIN:TPW-EXE:0x4a0e30 date refresh] frame 1 when the newest money-out sample exceeds the newest money-in sample; empty rings tie, so the frame is 0
		var economy = EconomyTestData.Park();
		var hud = Hud( economy );
		Assert.AreEqual( 0, hud.CashTrendFrame, "before the first month closes both rings are empty" );
		var mechanic = EconomyTestData.HireBest( economy, StaffType.Mechanic );
		economy.AdvanceMonths( 1 );
		var closed = economy.Ledger.History[^1];
		Assert.IsTrue( closed.MoneyOut > closed.MoneyIn, "a month with only a wage spent more than it took" );
		Assert.AreEqual( 1, hud.CashTrendFrame );
		Assert.IsNotNull( mechanic );
	}

	[TestMethod]
	public void TheHireBalanceIsTakenWhenTheWindowOpens()
	{
		var economy = EconomyTestData.Park();
		EconomyTestData.HireBest( economy, StaffType.Mechanic );
		var hud = Hud( economy );
		hud.OpenStaffWindow();
		var candidate = hud.StaffCandidates.First();
		var wage = economy.Settings.GetMonthlyWage( candidate.Type, candidate.Grade );
		Assert.AreEqual( (0L, wage, 0L, -wage), hud.CandidateBalance( candidate ), "before the first month closes the samples are 0" );
		economy.AdvanceMonths( 1 );
		Assert.AreEqual( (0L, wage, 0L, -wage), hud.CandidateBalance( candidate ), "a month closing while the window is open does not change it (the builder copies the samples)" );
		hud.CloseStaffWindow();
		hud.OpenStaffWindow();
		Assert.AreEqual( economy.Ledger.History[^1][LedgerCategory.StaffCosts] + wage, hud.CandidateBalance( candidate )!.Value.StaffCosts, "reopening reads the closed month" );
		Assert.IsTrue( economy.Ledger.History[^1][LedgerCategory.StaffCosts] > 0 );
	}

	// ---- The per-employee window (table 0x756f48) ----

	private static ParkHud EmployeeHud( out ParkEconomy economy, params StaffType[] roles )
	{
		economy = EconomyTestData.Park();
		foreach ( var role in roles )
			EconomyTestData.HireBest( economy, role );
		return Hud( economy );
	}

	[TestMethod]
	public void AClickOnAnEmployeeRowOpensTheEmployeeWindowInPlaceOfTheStaffWindow()
	{
		var hud = EmployeeHud( out var economy, StaffType.Handyman, StaffType.Mechanic );
		hud.OpenStaffWindow();
		hud.ShowEmployedStaff( true );
		hud.FilterStaff( StaffType.Mechanic );
		var list = (IUiRowList)hud.Stack.Top!.Find( "staffList" )!;
		Assert.IsTrue( list.RowClicked( 0, left: true ), "the All Staff list's row message 0x400 opens the per-employee window" );
		Assert.IsTrue( hud.EmployeeWindowOpen );
		Assert.IsFalse( hud.StaffWindowOpen, "opening a window closes the open one (0x485b40)" );
		Assert.AreEqual( "employee", hud.Stack.Top!.Name );
		Assert.AreSame( economy.Staff.Members.Single( member => member.Type == StaffType.Mechanic ), hud.Employee );
		Assert.AreEqual( 192, AdvisorUiHelp.MessageFor( "employee" ), "the window posts help 0xc0" );
	}

	[TestMethod]
	public void TheEmployeeWindowsControlsComeFromTheTable()
	{
		var hud = EmployeeHud( out var economy, StaffType.Handyman );
		Assert.IsTrue( hud.OpenEmployeeWindow( economy.Staff.Members[0] ) );
		var screen = hud.Stack.Top!;
		var table = OriginalLayoutTables.EmployeeWindow;
		foreach ( var (id, model) in new[] { ("dismissStaff", "b_fire"), ("pickUpStaff", "b_pick"), ("patrol", "b_route"), ("previousEmployee", "b_arup"),
			("nextEmployee", "b_ardown"), ("allStaff", "b_allstaff"), ("locateEmployee", "b_locate"), ("exit", "b_okay") } )
		{
			var button = (UiButton)screen.Find( id )!;
			Assert.AreEqual( model, button.Model, id );
			Assert.AreEqual( table.Model( model ).Rect, button.Bounds, id );
		}
		Assert.AreEqual( new UiRect( 380, 694, 154, 153 ), screen.Find( "dismissStaff" )!.Bounds, "b_fire (380,694)-(534,847)" );
		Assert.AreEqual( new UiRect( 539, 694, 153, 153 ), screen.Find( "pickUpStaff" )!.Bounds, "b_pick (539,694)-(692,847)" );
		Assert.AreEqual( "help89", screen.Find( "dismissStaff" )!.CurrentHelp );
		Assert.AreEqual( "help88", screen.Find( "pickUpStaff" )!.CurrentHelp );
		Assert.AreEqual( "help86", screen.Find( "patrol" )!.CurrentHelp, "the patrol toggle offers to set an area" );
		economy.Staff.Members[0].SetPatrolArea( (1, 1), (3, 3) );
		Assert.AreEqual( "help87", screen.Find( "patrol" )!.CurrentHelp, "and to clear one the employee has" );
		Assert.AreEqual( table.Control( 0x520 ).Rect, screen.Find( "title" )!.Bounds, "the name is the title" );
		Assert.IsInstanceOfType( screen.Find( "portraitFigure" ), typeof( UiSpriteFigure ), "the portrait frame plays the employee's figure (0x4b5330), not the role name" );
		Assert.AreEqual( table.Control( 0x511 ).Rect, screen.Find( "portraitFigure" )!.Bounds );
		Assert.IsNull( screen.Find( "portraitText" ) );
		Assert.AreEqual( table.Control( 0x51B ).Rect, screen.Find( "employedFor" )!.Bounds );
		Assert.AreEqual( table.Control( 0x513 ).Rect, screen.Find( "happinessBar" )!.Bounds, "happygrad bar beside the Happiness label" );
		Assert.AreEqual( table.Control( 0x514 ).Rect, screen.Find( "skillBar" )!.Bounds );
		Assert.AreEqual( table.Control( 0x515 ).Rect, screen.Find( "energyBar" )!.Bounds );
		Assert.AreEqual( "ui69", ((UiLabel)screen.Find( "label518" )!).Text(), "Happiness is UITEXT 69" );
		Assert.AreEqual( "ui68", ((UiLabel)screen.Find( "label51c" )!).Text(), "Energy is UITEXT 68" );
		Assert.IsFalse( hud.EmployeeArrowsVisible, "one employee hides the arrows (0x4b5a60)" );
	}

	[TestMethod]
	public void TheArrowsCycleTheStaffNewestFirstAndWrap()
	{
		var hud = EmployeeHud( out var economy, StaffType.Handyman, StaffType.Mechanic, StaffType.Guard );
		var (oldest, middle, newest) = (economy.Staff.Members[0], economy.Staff.Members[1], economy.Staff.Members[2]);
		Assert.AreSame( oldest, EmployeeWindowRules.Cycle( economy.Staff.Members, middle.Id, forward: true ), "forward walks to the older thing" );
		Assert.AreSame( newest, EmployeeWindowRules.Cycle( economy.Staff.Members, oldest.Id, forward: true ), "and wraps to the head of the list, the newest" );
		Assert.AreSame( newest, EmployeeWindowRules.Cycle( economy.Staff.Members, middle.Id, forward: false ), "back walks to the newer thing" );
		Assert.AreSame( oldest, EmployeeWindowRules.Cycle( economy.Staff.Members, newest.Id, forward: false ), "and wraps to the tail, the oldest" );
		hud.OpenEmployeeWindow( middle );
		Assert.IsTrue( hud.EmployeeArrowsVisible );
		hud.Stack.Top!.Find( "nextEmployee" )!.Activate();
		Assert.AreSame( oldest, hud.Employee, "b_ardown (help 91) cycles forward" );
		hud.Stack.Top!.Find( "previousEmployee" )!.Activate();
		hud.Stack.Top!.Find( "previousEmployee" )!.Activate();
		Assert.AreSame( newest, hud.Employee, "b_arup (help 92) cycles back" );
	}

	[TestMethod]
	public void TheStatsFrameFollowsTheOriginalScales()
	{
		var member = new StaffMember { Id = 5, Type = StaffType.Handyman, NameIndex = 0, Grade = 2, TrainingPoints = 50, HiredTick = 0 };
		member.Happiness = 61.9f;
		member.Energy = 100f;
		var stats = EmployeeWindowRules.Stats( member, ParkCalendar.TickAtDay( 7 ), 450 );
		Assert.AreEqual( 7L, stats.EmployedDays, "whole days since the hire" );
		Assert.AreEqual( 6L, EmployeeWindowRules.EmployedDays( ParkCalendar.TickAtDay( 7 ) - 1, 0 ), "a day not yet complete does not count" );
		Assert.AreEqual( 450L, stats.MonthlyWage );
		Assert.AreEqual( 50 * 1024 / 100, stats.SkillGauge, "skill 20 x 2.5 = 50 on the 1024 scale" );
		Assert.AreEqual( 61 * 1024 / 100, stats.HappinessGauge, "the truncated happiness byte" );
		Assert.AreEqual( 1024, stats.EnergyGauge );
		Assert.AreEqual( (300 & 0xff) * 1024 / 100, EmployeeWindowRules.ByteGauge( 300 ), "only the low byte counts" );
		member.State = StaffState.Patrolling;
		Assert.AreEqual( StaffState.Idle, EmployeeWindowRules.DisplayStatus( member ), "patrolling without an area of its own shows as idle" );
		member.SetPatrolArea( (1, 1), (2, 2) );
		Assert.AreEqual( StaffState.Patrolling, EmployeeWindowRules.DisplayStatus( member ) );
		CollectionAssert.AreEqual( new[] { 0, 0, 1, 2, 3, 9 },
			new[] { StaffState.Idle, StaffState.Patrolling, StaffState.Working, StaffState.Resting, StaffState.OnStrike, StaffState.PickedUp }.Select( EmployeeWindowRules.StatusIcon ).ToArray() );
	}

	[TestMethod]
	public void TheStatsRefreshEveryFourSeconds()
	{
		var hud = EmployeeHud( out var economy, StaffType.Handyman );
		var member = economy.Staff.Members[0];
		member.Happiness = 80;
		hud.OpenEmployeeWindow( member );
		Assert.AreEqual( 80 * 1024 / 100, hud.EmployeeStatsShown.HappinessGauge );
		member.Happiness = 20;
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new System.IO.FileNotFoundException( name ) ) ) { Delta = 3.9f };
		hud.Stack.Top!.Update( context, UiInput.Idle( System.Numerics.Vector2.Zero ) );
		Assert.AreEqual( 80 * 1024 / 100, hud.EmployeeStatsShown.HappinessGauge, "the bars keep their values between refreshes" );
		context.Delta = 0.2f;
		hud.Stack.Top!.Update( context, UiInput.Idle( System.Numerics.Vector2.Zero ) );
		Assert.AreEqual( 20 * 1024 / 100, hud.EmployeeStatsShown.HappinessGauge, "timer 0x80080 refreshes them after 4000 ms" );
	}

	[TestMethod]
	public void DismissClosesTheWindowAndAsksFirstWithConfirmations()
	{
		var previous = GameOptions.Current;
		try
		{
			GameOptions.Current = new GameOptions { Confirmations = false };
			var hud = EmployeeHud( out var economy, StaffType.Handyman, StaffType.Guard );
			var first = economy.Staff.Members[0];
			hud.OpenEmployeeWindow( first );
			hud.Stack.Top!.Find( "dismissStaff" )!.Activate();
			Assert.IsFalse( hud.EmployeeWindowOpen );
			Assert.IsTrue( economy.Staff.Members.All( member => member.Id != first.Id ), "without the option the employee goes at once (0x505790)" );

			GameOptions.Current = new GameOptions { Confirmations = true };
			var second = economy.Staff.Members[0];
			hud.OpenEmployeeWindow( second );
			hud.Stack.Top!.Find( "dismissStaff" )!.Activate();
			Assert.IsFalse( hud.EmployeeWindowOpen, "the window closes as the box opens" );
			Assert.AreEqual( "confirmDismissStaff", hud.Stack.Top!.Name );
			Assert.AreEqual( 1, economy.Staff.Members.Count, "nothing happens before the answer" );
			hud.Stack.Top!.Find( "choice0" )!.Activate();
			Assert.AreEqual( 0, economy.Staff.Members.Count, "yes runs 0x4b6070 → 0x505790" );
		}
		finally { GameOptions.Current = previous; }
	}

	[TestMethod]
	public void ThePatrolToggleClearsAnAreaAndTheAllStaffButtonListsTheRole()
	{
		var hud = EmployeeHud( out var economy, StaffType.Handyman, StaffType.Guard );
		var guard = economy.Staff.Members.Single( member => member.Type == StaffType.Guard );
		guard.SetPatrolArea( (1, 1), (4, 4) );
		hud.OpenEmployeeWindow( guard );
		Assert.IsTrue( ((UiButton)hud.Stack.Top!.Find( "patrol" )!).Selected!(), "checked while the employee has an area" );
		Assert.IsTrue( hud.PressEmployeePatrol(), "0x4b5b50(0) clears the area" );
		Assert.IsFalse( guard.HasPatrolArea );
		Assert.IsTrue( hud.EmployeeWindowOpen, "clearing keeps the window" );
		Assert.IsFalse( hud.PressEmployeePatrol(), "setting one needs the patrol tool" );
		hud.Stack.Top!.Find( "allStaff" )!.Activate();
		Assert.IsFalse( hud.EmployeeWindowOpen );
		Assert.IsTrue( hud.StaffWindowOpen && hud.ShowingEmployedStaff, "b_allstaff opens the staff list (0x496620)" );
		Assert.AreEqual( StaffType.Guard, hud.StaffFilter, "on the employee's role (0x506490)" );
	}

	[TestMethod]
	public void TheEmployeeWindowClosesWhenItsEmployeeLeaves()
	{
		var hud = EmployeeHud( out var economy, StaffType.Handyman );
		hud.OpenEmployeeWindow( economy.Staff.Members[0] );
		economy.Fire( economy.Staff.Members[0].Id );
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new System.IO.FileNotFoundException( name ) ) );
		hud.Stack.Top!.Update( context, UiInput.Idle( System.Numerics.Vector2.Zero ) );
		Assert.IsFalse( hud.EmployeeWindowOpen );
	}

	[TestMethod]
	public void TheStaffKeyLocatesTheEmployeeWhoseWindowIsOpen()
	{
		// tp.exe 0x481790: while the per-employee window is open the key opens 0x4b3f60 with its employee [0x7c2658], else with no actor
		var hud = EmployeeHud( out var economy, StaffType.Guard );
		var guard = economy.Staff.Members[0];
		hud.OpenStaffLocator();
		Assert.AreEqual( (LocatorMode.Staff, 0, 0), (hud.Locator.Mode, hud.Locator.Role, hud.Locator.ActorId) );
		hud.CloseLocator();
		hud.OpenEmployeeWindow( guard );
		hud.OpenStaffLocator();
		Assert.AreEqual( (LocatorMode.Staff, LocatorModel.RoleOf( StaffType.Guard ), guard.Id, true),
			(hud.Locator.Mode, hud.Locator.Role, hud.Locator.ActorId, hud.Locator.RadioChosen) );
		Assert.IsFalse( hud.EmployeeWindowOpen, "the locator closes the open window" );
	}
}

/// <summary>The All Visitors window from tp.exe's table 0x7506c8.</summary>
[TestClass]
[DoNotParallelize]
public class VisitorsWindowTests
{
	private static void Set( object o, string field, object? value ) => o.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( o, value );

	private static ParkHud Hud( params Guest[] guests )
	{
		var hud = VisitorWorldSelectionTests.Fixture( out var simulation, out _ );
		Set( simulation, "guests", guests.ToList() );
		return hud;
	}

	private static Guest Visitor( int id, int money = 20, float happiness = 50, double? seconds = 0 ) =>
		new( id, 0 ) { State = GuestState.WalkingAround, Money = money, Happiness = happiness, TimeInParkSeconds = seconds };

	private static UiContext Context( float delta ) => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new System.IO.FileNotFoundException( name ) ) ) { Delta = delta };

	[TestMethod]
	public void TheWindowsControlsComeFromTheTable()
	{
		var hud = Hud( Visitor( 1 ) );
		hud.OpenVisitorWindow();
		var screen = hud.Stack.Top!;
		var table = OriginalLayoutTables.VisitorsWindow;
		Assert.AreEqual( "visitors", screen.Name );
		Assert.AreEqual( table.Root.Rect, screen.Find( "window" )!.Bounds );
		Assert.AreEqual( table.Control( 0x1E498 ).Rect, screen.Find( "title" )!.Bounds );
		Assert.AreEqual( new UiRect( 294, 315, 1426, 599 ), screen.Find( "visitorList" )!.Bounds, "the list's inner rectangle" );
		Assert.AreEqual( 56f, ParkHud.VisitorRowHeight, "the row template's height 0x38" );
		Assert.AreEqual( 10, ParkHud.VisitorVisibleRows );
		var headers = new[] { "headerNumber", "headerCash", "headerTime", "headerRides", "headerThought", "headerHappiness" };
		for ( var column = 0; column < headers.Length; column++ )
		{
			var header = screen.Find( headers[column] )!;
			Assert.AreEqual( table.Control( (uint)(0x10 + column) ).Rect, header.Bounds, headers[column] );
			Assert.AreEqual( $"help{129 + column}", header.CurrentHelp, "help 0x81-0x86" );
		}
		foreach ( var (id, model) in new[] { ("exit", "b_okay"), ("parkInfo", "b_pkinfo"), ("allStaff", "b_allstaff"), ("allAttractions", "b_allthings") } )
		{
			var button = (UiButton)screen.Find( id )!;
			Assert.AreEqual( model, button.Model, id );
			Assert.AreEqual( table.Model( model ).Rect, button.Bounds, id );
			Assert.AreEqual( $"help{table.Model( model ).Help}", button.CurrentHelp, id );
		}
	}

	[TestMethod]
	public void AHeaderPressSortsByItsColumn()
	{
		var hud = Hud( Visitor( 1, money: 90 ), Visitor( 2, money: 10 ), Visitor( 3, money: 50 ) );
		hud.OpenVisitorWindow();
		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, hud.VisitorItems.Select( guest => guest.Id ).ToArray(), "sort word 1: number ascending" );
		hud.Stack.Top!.Find( "headerCash" )!.Activate();
		CollectionAssert.AreEqual( new[] { 2, 3, 1 }, hud.VisitorItems.Select( guest => guest.Id ).ToArray(), "a new column starts ascending" );
		hud.Stack.Top!.Find( "headerCash" )!.Activate();
		CollectionAssert.AreEqual( new[] { 1, 3, 2 }, hud.VisitorItems.Select( guest => guest.Id ).ToArray(), "the same column reverses" );
		Assert.AreEqual( (1, true), hud.VisitorSort );
	}

	[TestMethod]
	public void TheRefreshKeepsTheRowOrderUntilAHeaderPress()
	{
		// [BIN:TPW-EXE:0x00493230 refresh] timer 0x80083 rewrites the cells in place (0x6644ea) and never sorts; only a header press (0x665a44) does
		var guests = new[] { Visitor( 1, money: 90 ), Visitor( 2, money: 10 ), Visitor( 3, money: 50 ) };
		var hud = Hud( guests );
		hud.OpenVisitorWindow();
		hud.Stack.Top!.Find( "headerCash" )!.Activate();
		CollectionAssert.AreEqual( new[] { 2, 3, 1 }, hud.VisitorItems.Select( guest => guest.Id ).ToArray() );
		guests[0].Money = 1;
		hud.Stack.Top!.Update( Context( 2.1f ), UiInput.Idle( System.Numerics.Vector2.Zero ) );
		CollectionAssert.AreEqual( new[] { 2, 3, 1 }, hud.VisitorItems.Select( guest => guest.Id ).ToArray(), "the refresh took the new cash but kept the order" );
		hud.Stack.Top!.Find( "headerCash" )!.Activate();
		CollectionAssert.AreEqual( new[] { 3, 2, 1 }, hud.VisitorItems.Select( guest => guest.Id ).ToArray(), "a header press sorts the list again by the new values (the same column reverses)" );
	}

	[TestMethod]
	public void ANewRowGoesBeforeTheFirstRowItSortsStrictlyBefore()
	{
		// [BIN:TPW-EXE:0x0066403b sorted row insert, 0x00663edc compare] ties go after the equal rows, a row sorting before the head becomes the head
		var order = new System.Collections.Generic.List<int> { 1, 2, 3 };
		var values = new System.Collections.Generic.Dictionary<int, int> { [1] = 10, [2] = 20, [3] = 20, [4] = 20, [5] = 5, [6] = 99 };
		var rows = ListRowOrder.Keep( order, new[] { 1, 2, 3, 4, 5, 6 }, id => id, ( added, row ) => values[added] < values[row] );
		CollectionAssert.AreEqual( new[] { 5, 1, 2, 3, 4, 6 }, rows.ToArray() );
		var descending = new System.Collections.Generic.List<int> { 3, 2, 1 };
		rows = ListRowOrder.Keep( descending, new[] { 1, 2, 3, 4, 5, 6 }, id => id, ( added, row ) => values[row] < values[added] );
		CollectionAssert.AreEqual( new[] { 6, 3, 2, 4, 1, 5 }, rows.ToArray() );
	}

	[TestMethod]
	public void NewRowsShowAQuarterOfTheTimeUntilTheTwoSecondRefresh()
	{
		var guest = Visitor( 1, seconds: 400 * 0.248 + 0.01 );
		var hud = Hud( guest );
		hud.OpenVisitorWindow();
		var values = (VisitorRowValues)typeof( ParkHud ).GetMethod( "VisitorValuesOf", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( hud, new object[] { guest } )!;
		Assert.AreEqual( 100L * 3750 / 3600, values.Hours, "0x493800 adds the row with (turns >> 2) turns" );
		hud.Stack.Top!.Update( Context( 1.9f ), UiInput.Idle( System.Numerics.Vector2.Zero ) );
		guest.Money = 5;
		hud.Stack.Top!.Update( Context( 0.2f ), UiInput.Idle( System.Numerics.Vector2.Zero ) );
		values = (VisitorRowValues)typeof( ParkHud ).GetMethod( "VisitorValuesOf", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( hud, new object[] { guest } )!;
		Assert.AreEqual( 400L * 3750 / 3600, values.Hours, "timer 0x80083 rewrites the cells with the whole 4-turn steps" );
		Assert.AreEqual( 5, values.Cash );
	}

	[TestMethod]
	public void TheNavigationButtonsReplaceTheWindow()
	{
		var hud = Hud( Visitor( 1 ) );
		hud.OpenVisitorWindow();
		hud.Stack.Top!.Find( "allStaff" )!.Activate();
		Assert.IsFalse( hud.VisitorWindowOpen );
		Assert.IsTrue( hud.StaffWindowOpen && hud.ShowingEmployedStaff, "b_allstaff opens the staff list (0x496620)" );
		hud.CloseStaffWindow();
		hud.OpenVisitorWindow();
		hud.Stack.Top!.Find( "exit" )!.Activate();
		Assert.IsFalse( hud.VisitorWindowOpen, "the tick (-2) closes it" );
	}

	[TestMethod]
	public void TheHireListSharesTheBuyListsRowPitch()
	{
		// the hire list's row factory 0x49bbb0 is the buy list's 0x4ad240 formula on the same font slot 7
		Assert.AreEqual( ParkHud.BuyRowHeight, ParkHud.StaffRowHeight );
		Assert.AreEqual( 11, ParkHud.StaffVisibleRows, "(bottom - top) / pitch whole rows (0x663786)" );
	}
}
