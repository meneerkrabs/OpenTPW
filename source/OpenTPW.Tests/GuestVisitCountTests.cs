using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The guest's visit counters (tp.exe's use routine <c>0x4FD970</c>): a finished visit raises the rides ridden (+0x1C4), the shops visited (+0x1C8) or the
/// sideshows played (+0x1CC) by the type's <c>Info.WhichUIType</c> (+0x4AC) 0, 1 or 2 and none for any other kind; a sideshow's won tail raises +0x1D0.
/// </summary>
[TestClass]
public class GuestVisitCountTests
{
	/// <summary>A guest of a rig of the given kind whose visit the ride just finished (the host callback the bridge makes when the guest leaves).</summary>
	private static Guest Visit( RideVisitorKind kind )
	{
		var rig = new QueueTests.Rig( takesGuests: true, capacity: 1, kind: kind );
		var guest = rig.Send();
		((IRideVisitorHost)rig.Simulation).OnVisitorReleased( rig.Ride, guest.Id );
		return guest;
	}

	[TestMethod]
	public void ARideVisitCountsAsARide()
	{
		var guest = Visit( RideVisitorKind.Ride );
		Assert.AreEqual( (1, 0, 0, 0), (guest.RidesTaken, guest.ShopsVisited, guest.SideshowsVisited, guest.SideshowsWon) );
	}

	[TestMethod]
	public void AShopVisitCountsAsAShopNotARide()
	{
		var guest = Visit( RideVisitorKind.Shop );
		Assert.AreEqual( (0, 1, 0, 0), (guest.RidesTaken, guest.ShopsVisited, guest.SideshowsVisited, guest.SideshowsWon) );
	}

	[TestMethod]
	public void ASideshowPlayCountsAsASideshowAndAWinWhenItsTailRuns()
	{
		var guest = Visit( RideVisitorKind.Sideshow );
		Assert.AreEqual( (0, 0, 1, 1), (guest.RidesTaken, guest.ShopsVisited, guest.SideshowsVisited, guest.SideshowsWon), "no payment model: the play is not lost, so the won tail runs" );
	}

	[TestMethod]
	public void AFacilityVisitCountsNone()
	{
		var guest = Visit( RideVisitorKind.Facility );
		Assert.AreEqual( (0, 0, 0, 0), (guest.RidesTaken, guest.ShopsVisited, guest.SideshowsVisited, guest.SideshowsWon) );
	}

	[TestMethod]
	public void TheCountsSurviveASnapshot()
	{
		var rig = new QueueTests.Rig( takesGuests: true, capacity: 1, kind: RideVisitorKind.Shop );
		var guest = rig.Send();
		guest.ShopsVisited = 4;
		guest.SideshowsVisited = 3;
		guest.SideshowsWon = 2;
		var snapshot = rig.Simulation.CaptureSnapshot();
		var saved = snapshot.Guests.Single( item => item.Id == guest.Id );
		Assert.AreEqual( (4, 3, 2), (saved.ShopsVisited, saved.SideshowsVisited, saved.SideshowsWon) );
	}
}
