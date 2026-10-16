using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The visitor window's use counters (tp.exe 0x4fd970, 0x4fe816; guest +0x1c4, +0x1c8, +0x1cc, +0x1d0).</summary>
[TestClass]
public class GuestUseCounterTests
{
	private static (GuestSimulation Simulation, Guest Guest) Setup()
	{
		Log ??= new();
		var simulation = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 1 ) { ArrivalsEnabled = false };
		return (simulation, simulation.SpawnInPark( 6, 5 ));
	}

	private static void Use( GuestSimulation simulation, Guest guest, RideVisitorKind kind, bool? won = null )
	{
		var bridge = new RideVisitorBridge( 5, "Thing", kind, 1, 70, 30 ) { EntranceCell = (6, 5), ExitCell = (6, 5) };
		guest.SideshowWon = won;
		( (IRideVisitorHost)simulation ).OnVisitorReleased( bridge, guest.Id );
	}

	[TestMethod]
	public void ACompletedUseCountsByTheTypesKindAndFacilitiesCountInNone()
	{
		var (simulation, guest) = Setup();
		Use( simulation, guest, RideVisitorKind.Ride );
		Use( simulation, guest, RideVisitorKind.Ride );
		Use( simulation, guest, RideVisitorKind.Shop );
		Use( simulation, guest, RideVisitorKind.Facility );
		Assert.AreEqual( (2, 1, 0, 0), (guest.RidesTaken, guest.ShopsVisited, guest.SideshowsVisited, guest.SideshowsWon) );
	}

	[TestMethod]
	public void ALostSideshowIsPlayedButNotWon()
	{
		var (simulation, guest) = Setup();
		Use( simulation, guest, RideVisitorKind.Sideshow, won: true );
		Use( simulation, guest, RideVisitorKind.Sideshow, won: false );
		Assert.AreEqual( (0, 0, 2, 1), (guest.RidesTaken, guest.ShopsVisited, guest.SideshowsVisited, guest.SideshowsWon),
			"a lost play (win byte +0x1f1 of 0) skips the use routine that counts the win (0x4fe816)" );
	}

	[TestMethod]
	public void TheCountersSurviveASnapshotAndOldSnapshotsHoldNone()
	{
		var (simulation, guest) = Setup();
		Use( simulation, guest, RideVisitorKind.Ride );
		Use( simulation, guest, RideVisitorKind.Shop );
		var snapshot = simulation.CaptureSnapshot();
		var saved = snapshot.Guests.Single( item => item.Id == guest.Id );
		Assert.AreEqual( (1, 1, 0, 0), (saved.RidesTaken, saved.ShopsVisited, saved.SideshowsVisited, saved.SideshowsWon) );

		var legacy = snapshot with { Guests = snapshot.Guests.Select( item => item with { ShopsVisited = 0, SideshowsVisited = 0, SideshowsWon = 0 } ).ToArray() };
		var restored = new GuestSimulation( GuestTests.CreateCross(), GuestTests.CreateSettings(), 1 ) { ArrivalsEnabled = false };
		restored.RestoreSnapshot( legacy );
		var old = restored.Guests.Single( item => item.Id == guest.Id );
		Assert.AreEqual( (1, 0, 0, 0), (old.RidesTaken, old.ShopsVisited, old.SideshowsVisited, old.SideshowsWon), "an old save holds no shop, sideshow or win counts" );
	}
}
