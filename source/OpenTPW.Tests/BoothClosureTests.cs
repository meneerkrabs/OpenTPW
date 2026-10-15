using System.Linq;
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
		public bool TryPayAdmission( long guestMoney, out int feePaid ) { feePaid = 0; Paid++; return true; }
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
		Assert.IsTrue( waiting.State is GuestState.LeavingPark or GuestState.CrossingRoadHome, $"a paid guest at a closed park goes home ({waiting.State})" );
		Assert.IsTrue( waiting.TimeInParkSeconds is not null, "the admission was taken" );
	}
}
