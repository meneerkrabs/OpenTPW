using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Where a closed park stops its visitors (tp.exe <c>0x004FF730</c>, <c>0x004FF7F0</c>, <c>0x004FF9D0</c>): on the walk to the booth, not at the booth,
/// which weighs and books the fee without a closed or bankrupt test; a paid visitor is then sent home while the park stays closed.
/// </summary>
[TestClass]
public class BoothClosureTests
{
	private sealed class Gate : IGuestPayments
	{
		public bool Closed;
		public int Paid;
		public int AdmissionFee => 0;
		public bool ParkClosed => Closed;
		public bool TryPayAdmission( out int feePaid ) { feePaid = 0; Paid++; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
	}

	private static GuestSimulation Simulation( Gate gate )
	{
		var settings = GuestTests.CreateSettings();
		return new GuestSimulation( GuestTests.CreateCross(), settings, 3 ) { Payments = gate };
	}

	[TestMethod]
	public void AVisitorReachingTheBoothOfAClosedParkTurnsHomeWithoutAThought()
	{
		var gate = new Gate { Closed = true };
		var simulation = Simulation( gate );
		var sawBooth = false;
		for ( var tick = 0; tick < 90 * 60; tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			sawBooth |= simulation.Guests.Any( guest => guest.State == GuestState.AtTicketBooth );
		}
		Assert.IsFalse( sawBooth, "state 2 goes to state 3 (home), not to the booth, while the park is closed" );
		Assert.AreEqual( 0, gate.Paid );
		Assert.IsTrue( simulation.Guests.Any( guest => guest.State is GuestState.LeavingPark or GuestState.CrossingRoadHome or GuestState.WaitingToGoHome ) || simulation.Departed > 0 );
		Assert.IsTrue( simulation.Guests.All( guest => guest.Thought != GuestThought.Dissatisfied ), "no thought on the way home (0x4ff7f0)" );
	}

	[TestMethod]
	public void AVisitorAtTheBoothStillPaysAndIsThenSentHome()
	{
		var gate = new Gate();
		var simulation = Simulation( gate );
		Guest? waiting = null;
		for ( var tick = 0; tick < 90 * 60 && waiting == null; tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			waiting = simulation.Guests.FirstOrDefault( guest => guest.State == GuestState.AtTicketBooth );
		}
		Assert.IsNotNull( waiting );
		gate.Closed = true;
		var paid = gate.Paid;
		for ( var tick = 0; tick < 30 * 60 && waiting.State == GuestState.AtTicketBooth; tick++ )
			simulation.Tick( GuestSnapshotTests.Tick );
		Assert.IsTrue( gate.Paid > paid, "the booth books the fee without reading the closed word (0x4ff9d0, 0x4d0600)" );
		Assert.AreEqual( GuestState.WaitingAtBooth, waiting.State, "the paid guest stands at the booth in state 3" );
		Assert.IsTrue( waiting.Paid );
		// 0x004FF7F0: the wait SetState(3) drew (200 + R % 150 turns) counts down one per update before the way home
		var wait = waiting.BoothWait;
		Assert.IsTrue( wait is >= 200 and <= 349, $"the gate wait {wait}" );
		var turns = 0;
		for ( var tick = 0; tick < 150 * 60 && waiting.State == GuestState.WaitingAtBooth; tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			if ( simulation.TickCount % 15 == 1 )
				turns++;
		}
		Assert.IsTrue( turns >= wait - 2 && turns <= wait + 2, $"the guest waited {turns} turns for a wait of {wait}" );
		Assert.IsTrue( waiting.State is GuestState.LeavingPark or GuestState.CrossingRoadHome, $"a paid guest at a closed park goes home ({waiting.State})" );
		Assert.AreEqual( 0, waiting.ExitLevel, "state 18 with the exit counter zeroed: no recall" );
		Assert.IsTrue( waiting.TimeInParkSeconds is not null, "the admission was taken" );
	}
	[TestMethod]
	public void APaidVisitorWaitsNoLongerThanTheGateIsShutAndIsSentHome()
	{
		// tp.exe's state 3 (0x004FF7F0) walks a paid guest in only while the entrance gate's script variable 1 is 1 (0x0051A290), else it takes the way home as at a closed park
		var gate = new Gate();
		var simulation = Simulation( gate );
		var open = true;
		simulation.EntranceGateOpen = () => open;
		Guest? waiting = null;
		for ( var tick = 0; tick < 90 * 60 && waiting == null; tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			waiting = simulation.Guests.FirstOrDefault( guest => guest.State == GuestState.AtTicketBooth );
		}
		Assert.IsNotNull( waiting );
		open = false;
		for ( var tick = 0; tick < 150 * 60 && waiting.State is GuestState.AtTicketBooth or GuestState.WaitingAtBooth; tick++ )
			simulation.Tick( GuestSnapshotTests.Tick );
		Assert.IsTrue( waiting.State is GuestState.LeavingPark or GuestState.CrossingRoadHome, $"a paid guest at a shut gate goes home ({waiting.State})" );
	}

	[TestMethod]
	public void ABoothGuestPaysNothingOnceTheParkIsBankrupt()
	{
		// tp.exe's bankruptcy sets world state 4, which stops the per-turn thing loop (0x516380, ECON-030): no guest pays any more
		var economy = EconomyTestData.Park();
		economy.OpenPark();
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 3 );
		simulation.Payments = new GuestEconomyBridge( () => economy, simulation );
		Guest? waiting = null;
		for ( var tick = 0; tick < 90 * 60 && waiting == null; tick++ )
		{
			simulation.Tick( GuestSnapshotTests.Tick );
			waiting = simulation.Guests.FirstOrDefault( guest => guest.State == GuestState.AtTicketBooth );
		}
		Assert.IsNotNull( waiting );
		typeof( ParkEconomy ).GetField( "<IsBankrupt>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( economy, true );
		var admissions = economy.Counters[ParkCounters.Admissions];
		var takings = economy.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.GateTakings );
		for ( var tick = 0; tick < 30 * 60 && waiting.State == GuestState.AtTicketBooth; tick++ )
			simulation.Tick( GuestSnapshotTests.Tick );
		Assert.AreNotEqual( GuestState.EnteringPark, waiting.State );
		Assert.AreEqual( admissions, economy.Counters[ParkCounters.Admissions] );
		Assert.AreEqual( takings, economy.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.GateTakings ) );
		Assert.IsFalse( economy.TryAdmitVisitor( out _ ) );
	}
}
