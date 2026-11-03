using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The umbrella day gate's age is the integer world-turn difference of Game.exe's <c>0x611050</c> (UMB-003), not the floor of the seconds a guest has spent in the park.</summary>
[TestClass]
public class UmbrellaDayGateAgeTests
{
	[TestMethod]
	public void TheGatesAgeIsTheIntegerTurnCountTheGuestKeeps()
	{
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 1 ) { ArrivalsEnabled = false };
		var guest = simulation.SpawnInPark( 6, 5 );
		// the seconds say 95.9 turns (their floor is 95, in the second quarter of the fourth game day), the integer difference of the world turn and the stamp says 96
		guest.TimeInParkSeconds = 95.9 * ParkCalendar.TurnMilliseconds / 1000.0;
		guest.DisplayTimeInParkTurns = 96;
		Assert.AreEqual( 96L, GuestSimulation.AgeTurnsOf( guest ) );
		Assert.IsTrue( UmbrellaRules.DayGateOpen( GuestSimulation.AgeTurnsOf( guest ) ), "96 turns are four game days of 3750 seconds a turn / 86400: the gate opens" );
		guest.DisplayTimeInParkTurns = 95;
		Assert.IsFalse( UmbrellaRules.DayGateOpen( GuestSimulation.AgeTurnsOf( guest ) ), "95 is cut to 92, three days and a bit" );
		// a save written before the integer age was kept holds only the seconds
		guest.DisplayTimeInParkTurns = null;
		Assert.AreEqual( 95L, GuestSimulation.AgeTurnsOf( guest ) );
		guest.TimeInParkSeconds = null;
		Assert.AreEqual( 0L, GuestSimulation.AgeTurnsOf( guest ), "no known time: new, the gate opens" );
	}
}
