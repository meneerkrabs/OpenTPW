using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// PTCL-908: a guest of a ride with <c>UsageInfo.RequiresTeleport</c> (type record +0x100) beams in when it boards (tp.exe 0x501db0 state 16) and out when it leaves (state 15), each time
/// as particle 0x13 at the guest's place (0x4fa470).
/// </summary>
[TestClass]
public class GuestTeleportBeamTests
{
	[TestMethod]
	public void ATeleportRideBeamsTheGuestAtBoardingAndAtLeaving()
	{
		var rig = new QueueTests.Rig( takesGuests: true, capacity: 1, kind: RideVisitorKind.Ride );
		rig.Ride.RequiresTeleport = true;
		var beamed = new List<int>();
		rig.Simulation.Teleported += guest => beamed.Add( guest.Id );
		var guest = rig.Send();
		Assert.IsTrue( beamed.Count <= 1, "boarding may already have run inside Send" );
		var host = (IRideVisitorHost)rig.Simulation;
		var before = beamed.Count;
		host.OnVisitorReleased( rig.Ride, guest.Id );
		Assert.AreEqual( before + 1, beamed.Count, "leaving beams once" );
		Assert.IsTrue( beamed.All( id => id == guest.Id ) );
		host.OnVisitorBoarded( rig.Ride, guest.Id );
		Assert.AreEqual( before + 2, beamed.Count, "boarding beams once" );
	}

	[TestMethod]
	public void AnOrdinaryRideBeamsNobody()
	{
		var rig = new QueueTests.Rig( takesGuests: true, capacity: 1, kind: RideVisitorKind.Ride );
		var beamed = 0;
		rig.Simulation.Teleported += _ => beamed++;
		var guest = rig.Send();
		var host = (IRideVisitorHost)rig.Simulation;
		host.OnVisitorBoarded( rig.Ride, guest.Id );
		host.OnVisitorReleased( rig.Ride, guest.Id );
		Assert.AreEqual( 0, beamed );
	}

	[TestMethod]
	public void TheBeamStartsAtTheGuestsPlaceInNativeUnitsOnTheGround()
	{
		// a 2 x 2 field of 10 x 10 unit cells whose corners are all 6 high: flat ground at height 6
		var field = new ModelHeightfield( 2, 2, 10, 10, 0, 0, System.Array.Empty<uint>(), new[] { 6f, 6f, 6f, 6f, 6f, 6f, 6f, 6f, 6f }, new uint[4] );
		Assert.AreEqual( (15 * 1024, 6 * 1024, 5 * 1024 + 512), Level.GuestBeamPlace( field, 1.5f, 0.55f ), "x = 1.5 cells = 15 units, z = 0.55 cells = 5.5 units, each times 1024 and truncated; y is the ground" );
		Assert.AreEqual( 19, Level.BeamUpEffectId );
	}
}
