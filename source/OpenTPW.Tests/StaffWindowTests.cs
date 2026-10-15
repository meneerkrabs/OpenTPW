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
}
