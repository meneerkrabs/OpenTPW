using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class ImportedBoothWaitTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private sealed class Gate : IGuestPayments
	{
		public int Paid;
		public int AdmissionFee => 1;
		public bool ParkClosed => true;
		public bool TryPayAdmission( out int feePaid ) { feePaid = AdmissionFee; Paid++; return true; }
		public bool TryPayVisit( IRideVisitorBridge attraction, long guestMoney, out int pricePaid ) { pricePaid = 0; return true; }
	}

	private static (GuestSimulation Simulation, Gate Gate) Rig()
	{
		var grid = new GuestPathGrid( 20, 20 );
		for ( var x = 0; x < 16; x++ ) grid.SetPath( x, 5, true );
		grid.SetPath( 10, 6, true );
		for ( var y = 7; y <= 14; y++ ) grid.Cells.SetType( 10, y, ParkCellTypes.FixedWalkway );
		var settings = new GuestSettings { ArrivalLaneA = new[] { (10, 14), (10, 12), (10, 10), (10, 8), (10, 6) } };
		settings.Types.Add( new GuestType( 60, 600, 40 ) );
		var gate = new Gate();
		return (new GuestSimulation( grid, settings, 5 ) { ArrivalsEnabled = false, Payments = gate }, gate);
	}

	private static Guest Import( GuestSimulation simulation, uint wait, uint state = 4 )
	{
		var body = SaveTestPayloads.Guest( new SaveTestPayloads.GuestFields
		{
			Position = (10 * 256 + 128, 8 * 256 + 128), State = state, ParkOpeningWaitingTime = wait, PaidAdmission = 0
		} );
		var payload = new byte[8 + body.Length];
		body.CopyTo( payload, 8 );
		var saved = SaveGuest.Read( payload, new SaveThing( 40, SaveThingModel.Guest, 0, 0, 8, body.Length ) );
		simulation.ImportOriginal( new[] { saved }, 755, _ => 0, _ => 0 );
		return simulation.Guests.Single();
	}

	private static void Turns( GuestSimulation simulation, int turns )
	{
		var target = simulation.ParkTurn + turns;
		while ( simulation.ParkTurn < target ) simulation.Tick( 1f / 60 );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 50 )]
	[DataRow( 99 )]
	[DataRow( 1000 )]
	public void ImportedCounterWaitsItsSilentTurnsBeforeOneVerdict( int savedWait )
	{
		var (simulation, gate) = Rig();
		var random = simulation.CaptureSnapshot().RandomState;
		var guest = Import( simulation, (uint)savedWait );
		var silentTurns = Math.Min( savedWait, 2 * BoothVerdict.ExpensiveWaitBase );
		Assert.AreEqual( silentTurns == 0 ? 0 : silentTurns + 1, guest.BoothWait, "clamp before converting the original counter" );
		Assert.AreEqual( random, simulation.CaptureSnapshot().RandomState, "import draws nothing" );
		Assert.IsFalse( simulation.IsOnParkCell( guest ), "the booth walkway excludes unrelated park-cell thought draws" );
		var spent = 0;
		simulation.MoneySpent += (_, amount, _) => spent += amount;
		Turns( simulation, silentTurns );
		Assert.AreEqual( 0, gate.Paid );
		Assert.AreEqual( 0, spent );
		Assert.AreEqual( GuestState.AtTicketBooth, guest.State );
		Assert.AreEqual( random, simulation.CaptureSnapshot().RandomState, "silent booth turns draw no verdict" );
		Turns( simulation, 1 );
		Assert.AreEqual( 1, gate.Paid );
		Assert.AreEqual( 1, spent );
		Assert.IsTrue( guest.Paid );
		Assert.AreNotEqual( random, simulation.CaptureSnapshot().RandomState );
		Turns( simulation, 2 );
		Assert.AreEqual( 1, gate.Paid, "the paid state cannot repeat the verdict during catch-up" );
	}

	[TestMethod]
	public void StateThreeRetainsItsRawCounter()
	{
		var (simulation, gate) = Rig();
		var guest = Import( simulation, 50, state: 3 );
		Assert.AreEqual( 50, guest.BoothWait );
		Turns( simulation, 1 );
		Assert.AreEqual( 49, guest.BoothWait );
		Assert.AreEqual( 0, gate.Paid );
	}

	[TestMethod]
	public void OwnSaveMidCountdownDoesNotConvertTheCounterAgain()
	{
		var original = Rig();
		Import( original.Simulation, 50 );
		Turns( original.Simulation, 17 );
		original.Simulation.Tick( 1f / 60 ); // Preserve a partial park turn too.
		var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>(
			ParkResumeJson.Serialize( original.Simulation.CaptureSnapshot() ) );
		var resumed = Rig();
		resumed.Simulation.RestoreSnapshot( snapshot );
		Assert.AreEqual( 34, resumed.Simulation.Guests.Single().BoothWait );
		for ( var tick = 0; tick < 36 * 15; tick++ )
		{
			original.Simulation.Tick( 1f / 60 );
			resumed.Simulation.Tick( 1f / 60 );
			Assert.AreEqual( original.Gate.Paid, resumed.Gate.Paid );
			Assert.AreEqual( original.Simulation.Guests.Single().BoothWait, resumed.Simulation.Guests.Single().BoothWait );
			var before = new StateHasher();
			var after = new StateHasher();
			original.Simulation.AddCanonicalState( before );
			resumed.Simulation.AddCanonicalState( after );
			Assert.AreEqual( before.Value, after.Value );
		}
		Assert.AreEqual( 1, resumed.Gate.Paid );
	}
}
