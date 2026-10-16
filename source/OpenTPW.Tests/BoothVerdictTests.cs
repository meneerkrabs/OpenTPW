using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The ticket booth's verdict on the entrance fee (tp.exe <c>0x004FF5B0</c>) and what the guest does with it (<c>0x004FF9D0</c>).</summary>
[TestClass]
public class BoothVerdictTests
{
	private static GuestSettings Shipped() => new() { ExcitementToCostDivisor = 4, MinimumEntryFee = 20, CheapPriceMultiplier = 0.75f, AveragePriceMultiplier = 1.25f, ExpensivePriceMultiplier = 2.0f };

	[TestMethod]
	public void TheParkValueIsThePointsOverFourToSixPlusTheMinimumFee()
	{
		var settings = Shipped();
		Assert.AreEqual( 20u + 120 / 4, BoothVerdict.ParkValue( 120, 0, settings ), "R % 3 = 0" );
		Assert.AreEqual( 20u + 120 / 5, BoothVerdict.ParkValue( 120, 1, settings ) );
		Assert.AreEqual( 20u + 120 / 6, BoothVerdict.ParkValue( 120, 5, settings ), "R % 3 = 2" );
		Assert.AreEqual( 20u, BoothVerdict.ParkValue( 0, 7, settings ) );
	}

	[TestMethod]
	public void TheFeeFallsIntoTheFourBands()
	{
		var settings = Shipped();
		// Points 120 and draw 0: GP = 50, so cheap up to 37.5, fair below 62.5, expensive below 100, far too expensive from 100.
		BoothVerdictKind At( uint fee ) => BoothVerdict.Decide( 120, fee, 0, settings );
		Assert.AreEqual( BoothVerdictKind.Cheap, At( 37 ) );
		Assert.AreEqual( BoothVerdictKind.Fair, At( 38 ) );
		Assert.AreEqual( BoothVerdictKind.Fair, At( 62 ) );
		Assert.AreEqual( BoothVerdictKind.Expensive, At( 63 ) );
		Assert.AreEqual( BoothVerdictKind.Expensive, At( 99 ) );
		Assert.AreEqual( BoothVerdictKind.FarTooExpensive, At( 100 ) );
		Assert.AreEqual( BoothVerdictKind.Fair, BoothVerdict.Decide( 0, 0, 0, settings ), "fee and value at the minimum: always fair" );
		Assert.AreEqual( BoothVerdictKind.Fair, BoothVerdict.Decide( 0, 20, 0, settings ) );
		Assert.AreEqual( BoothVerdictKind.Fair, BoothVerdict.Decide( 0, 24, 0, settings ), "24 < 1.25 × 20 and > 0.75 × 20" );
		Assert.AreEqual( BoothVerdictKind.Expensive, BoothVerdict.Decide( 0, 25, 0, settings ), "25 = 1.25 × 20" );
		Assert.AreEqual( BoothVerdictKind.FarTooExpensive, BoothVerdict.Decide( 0, 40, 0, settings ) );
	}

	[TestMethod]
	public void AGuestAtAnExpensiveParkWaitsLosesHappinessAndGoesHome()
	{
		(GuestSimulation Simulation, int AtBooth, int Departed) Run( int fee, int seconds )
		{
			var settings = GuestTests.CreateSettings();
			settings.AdmissionFee = fee;
			var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 3 );
			var atBooth = 0;
			for ( var tick = 0; tick < seconds * 60; tick++ )
			{
				simulation.Tick( GuestSnapshotTests.Tick );
				atBooth = System.Math.Max( atBooth, simulation.Guests.Count( guest => guest.State == GuestState.AtTicketBooth ) );
			}
			return (simulation, atBooth, simulation.Departed);
		}
		var fair = Run( 20, 60 );
		Assert.IsTrue( fair.Simulation.Admissions > 0, "the minimum entry fee is always fair" );
		var expensive = Run( 30, 240 );
		Assert.AreEqual( 0, expensive.Simulation.Admissions, "30 is on the expensive side of an empty park's value 20: nobody pays" );
		Assert.IsTrue( expensive.Departed > 0, "a guest goes home once its happiness truncates to 0" );
		var far = Run( 40, 240 );
		Assert.AreEqual( 0, far.Simulation.Admissions );
		Assert.IsTrue( expensive.AtBooth > far.AtBooth, $"expensive: guests wait at the booth ({expensive.AtBooth}); far too expensive: they turn at once ({far.AtBooth})" );
		Assert.IsTrue( far.Departed >= expensive.Departed, $"{far.Departed} / {expensive.Departed}" );
	}

	[TestMethod]
	public void AnExpensiveVerdictRunsAgainOneTurnAfterItsCounterEmpties()
	{
		// 0x004FF9D0 stores 50 + R % 50 and returns; the updates decrement it, and the one that finds 0 runs the verdict: N + 1 turns.
		Assert.AreEqual( 51, BoothVerdict.ExpensiveWaitTurns( 0 ) );
		Assert.AreEqual( 100, BoothVerdict.ExpensiveWaitTurns( 49 ) );
		Assert.AreEqual( 51, BoothVerdict.ExpensiveWaitTurns( 50 ) );
		Assert.AreEqual( 100, BoothVerdict.ExpensiveWaitTurns( uint.MaxValue - 46 ) );
	}

	[TestMethod]
	public void TheBoothWaitIsCountedInParkTurnsNotSeconds()
	{
		var settings = GuestTests.CreateSettings();
		settings.AdmissionFee = 30;
		var simulation = new GuestSimulation( GuestTests.CreateCross(), settings, 3 );
		Guest? waiting = null;
		for ( var tick = 0; tick < 240 * 60 && waiting == null; tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			waiting = simulation.Guests.FirstOrDefault( guest => guest.State == GuestState.AtTicketBooth && guest.BoothWait > 0 );
		}
		Assert.IsNotNull( waiting, "an expensive verdict stores a wait" );
		var turns = waiting!.BoothWait;
		Assert.IsTrue( turns is >= 51 and <= 100, $"{turns}" );
		var start = simulation.ParkTurn;
		while ( simulation.ParkTurn - start < turns - 1 )
			simulation.Tick( GuestSnapshotTests.Tick );
		Assert.AreEqual( 1, waiting.BoothWait, "one count per executed park turn: the verdict runs again as the last turn's count empties it" );
		Assert.AreEqual( GuestState.AtTicketBooth, waiting.State );
	}
}
