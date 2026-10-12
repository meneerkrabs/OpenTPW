using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public sealed class GuestSnapshotJsonReviewTests
{
	[TestMethod]
	public void QueueTupleFieldsSurviveTheActualSnapshotCodec()
	{
		var bridge = new RideVisitorBridge( 17, "resume codec", RideVisitorKind.Ride, 4, 10, 100 );
		var original = bridge.CaptureSnapshot() with
		{
			Queue = new[] { 41, 42 },
			OnRide = new[] { 51, 52 },
			CalledGuest = 41,
			LetMeOnWritten = true,
			QueueCells = new[] { (X: 3, Y: 4), (X: 3, Y: 5) },
			JoinCell = (X: 3, Y: 6),
			Limbo = new[] { (Guest: 51, Until: 12.75) },
			Bounce = new[] { (Guest: 52, Until: 13.5, Start: 9.125) },
			BoardedTotal = 2
		};
		var loaded = ParkResumeJson.Deserialize<RideVisitorBridgeSnapshot>( ParkResumeJson.Serialize( original ) );
		CollectionAssert.AreEqual( original.QueueCells, loaded.QueueCells );
		Assert.AreEqual( original.JoinCell, loaded.JoinCell );
		CollectionAssert.AreEqual( original.Limbo, loaded.Limbo );
		CollectionAssert.AreEqual( original.Bounce, loaded.Bounce );
		CollectionAssert.AreEqual( original.Queue, loaded.Queue );
		bridge.RestoreSnapshot( loaded );
		CollectionAssert.AreEqual( original.Queue, bridge.Queue.ToArray() );
		CollectionAssert.AreEqual( original.OnRide, bridge.Riders.ToArray() );
	}
}
