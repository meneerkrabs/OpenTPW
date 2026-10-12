using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class ResumeQueueGeometryTests
{
	private static GuestSimulationSnapshot Snapshot( GuestSnapshotTests.Rig source )
	{
		GuestSnapshotTests.RunUntil( source, rig => rig.Rides[0].Bridge.QueueLength >= 3, 0, 9000 );
		return ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( source.Simulation.CaptureSnapshot() ) );
	}

	[TestMethod]
	public void MatchingSavedCellsAcceptCurrentQueueGeometry()
	{
		var source = new GuestSnapshotTests.Rig();
		var snapshot = Snapshot( source );
		var target = new GuestSnapshotTests.Rig();
		Level.ValidateResumeQueueGeometry( snapshot, target.Simulation );
		Assert.AreEqual( source.Grid.Version, target.Grid.Version );
	}

	[TestMethod]
	public void CurrentEpochWithDifferentGeometryIsRejectedBeforeRestore()
	{
		var source = new GuestSnapshotTests.Rig();
		var snapshot = Snapshot( source );
		var target = new GuestSnapshotTests.Rig();
		var before = GuestSnapshotTests.CanonicalHash( target );
		var altered = snapshot with
		{
			Attractions = snapshot.Attractions.Select( ( bridge, index ) => index == 0
				? bridge with { QueueCells = bridge.QueueCells.Reverse().ToArray() } : bridge ).ToArray()
		};
		Assert.ThrowsException<InvalidDataException>( () => Level.ValidateResumeQueueGeometry( altered, target.Simulation ) );
		Assert.AreEqual( before, GuestSnapshotTests.CanonicalHash( target ) );
	}

	[TestMethod]
	public void ACacheBeforeTheLastGridEditIsAllowedToRefreshOnTheNextTick()
	{
		var source = new GuestSnapshotTests.Rig();
		Snapshot( source );
		source.Grid.SetPath( 0, 1, true );
		// A save can happen after a build-tool grid edit, before the next guest tick sees that edit.
		var editedSnapshot = source.Simulation.CaptureSnapshot();
		Assert.IsTrue( editedSnapshot.SeenGridVersion < source.Grid.Version );
		var target = new GuestSnapshotTests.Rig();
		target.Grid.SetPath( 0, 1, true );
		Level.ValidateResumeQueueGeometry( editedSnapshot, target.Simulation );
	}

	[TestMethod]
	public void GuestCacheCannotReferToAnEpochAfterTheSavedLayout()
	{
		var source = new GuestSnapshotTests.Rig();
		var snapshot = Snapshot( source );
		var target = new GuestSnapshotTests.Rig();
		Assert.ThrowsException<InvalidDataException>( () => Level.ValidateResumeQueueGeometry(
			snapshot with { SeenGridVersion = target.Grid.Version + 1 }, target.Simulation ) );
	}
}
