using System.Linq;
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
}
