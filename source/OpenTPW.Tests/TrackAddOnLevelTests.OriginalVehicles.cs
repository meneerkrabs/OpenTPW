using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The vehicles, riders and lists of a saved kart ride on the level of the add-on tests (TRACK-032, loader <c>0x543560</c> chunks 5, 7, 8 and 9): placed after the guest import, which restores the riders the
/// chunks name, and refused when a chunk names a guest the ride does not hold.
/// </summary>
public partial class TrackAddOnLevelTests
{
	/// <summary>A ride with a race in progress: three karts (the first with three riders), a joining guest and three leaving guests, as a block of the saver's chunks and the cells of its track.</summary>
	private (List<SaveTrackCell> Cells, SaveTrackRideBlock Block, uint Handle, BumpController Source) SavedRace()
	{
		var (source, sourceRide, _) = World();
		sourceRide.ThingId = RideThing;
		var bump = sourceRide.Runtime.Bump!;
		bump.Open();
		foreach ( var riders in new[] { new[] { 41, 42, 43 }, new[] { 44 }, new int[0] } )
		{
			foreach ( var guest in riders.Reverse() )
				bump.AddGuest( guest );
			Assert.IsTrue( bump.Launch() );
		}
		bump.Start();
		bump.Pending.Add( 51 );
		bump.Leaving.AddRange( new[] { 61, 62, 63 } );
		for ( var tick = 0; tick < 40; tick++ )
			bump.Module.Tick();
		// the synthetic station's reference lies between its objects: the karts are put in the middle of three of them, as a real track's vehicles always stand in one (0x546C80 reads the object unchecked)
		for ( var index = 0; index < bump.Vehicles.Count; index++ )
		{
			var home = bump.Objects[3 + index];
			(bump.Vehicles[index].X, bump.Vehicles[index].Y, bump.Vehicles[index].ObjectIndex) = (home.X, home.Y, home.Index);
		}
		var handle = unchecked( (uint)(bump.Type << 8 | 3) );
		return (SavedCells( source.Guests!.Grid.Cells.Tracks ), OriginalTrackVehiclesTests.Block( bump, handle ), handle, bump);
	}

	[TestMethod]
	public void ASavedRaceComesBackWithItsKartsRidersAndListsOnceTheGuestsAreRiders()
	{
		var (cells, block, handle, source) = SavedRace();
		var (target, targetRide, _) = World( laid: false );
		targetRide.ThingId = RideThing;
		var lines = target.ApplyOriginalTracks( cells, ( _, _ ) => 0, block, null, new[] { RideThingOf( handle ) } ).ToList();
		var bump = targetRide.Runtime.Bump!;
		Assert.AreEqual( BumpController.PhaseLoading, bump.Phase, "before the vehicles are placed a kart ride saved racing waits in loading" );
		Assert.AreEqual( 0, bump.VehicleCount );
		// the guest import makes the guests of state 16 riders of the ride; the vehicles are placed after it
		Assert.AreEqual( 0, targetRide.Visitors.ImportSavedRiders( new[] { 41, 42, 43, 44, 51, 61, 62, 63, 70 } ).Count );
		lines.AddRange( target.ApplyOriginalTrackRiders( block ) );

		StringAssert.Contains( string.Join( "\n", lines ), "3 vehicles with 4 riders, 1 joining and 3 leaving guests, phase 2" );
		Assert.AreEqual( BumpController.PhaseRunning, bump.Phase, "the loader reads the phase raw: the race goes on" );
		CollectionAssert.AreEqual( source.Vehicles.Select( vehicle => (vehicle.X, vehicle.Y, vehicle.Flags, vehicle.TargetBuoy, vehicle.ObjectIndex) ).ToArray(), bump.Vehicles.Select( vehicle => (vehicle.X, vehicle.Y, vehicle.Flags, vehicle.TargetBuoy, vehicle.ObjectIndex) ).ToArray() );
		CollectionAssert.AreEqual( new[] { 43, 42, 41 }, bump.Vehicles[0].Riders.ToArray() );
		CollectionAssert.AreEqual( new[] { 63, 62, 61 }, bump.Leaving.ToArray() );
		CollectionAssert.AreEqual( new[] { 51 }, bump.Pending.ToArray() );
		Assert.AreEqual( 4, bump.RiderCount );
		Assert.IsFalse( lines.Any( line => line.Contains( "not restored" ) ), string.Join( "\n", lines ) );
	}

	[TestMethod]
	public void ASavedVehicleWhoseRiderIsNotAGuestOfTheRideIsNotRestoredAndTheRideWaitsInLoading()
	{
		var (cells, block, handle, _) = SavedRace();
		var (target, targetRide, _) = World( laid: false );
		targetRide.ThingId = RideThing;
		target.ApplyOriginalTracks( cells, ( _, _ ) => 0, block, null, new[] { RideThingOf( handle ) } );
		// guest 42 is no rider of the ride (the guest import dropped it, or it names another ride)
		targetRide.Visitors.ImportSavedRiders( new[] { 41, 43, 44, 51, 61, 62, 63 } );
		var lines = string.Join( "\n", target.ApplyOriginalTrackRiders( block ) );
		StringAssert.Contains( lines, "name guests who are not riders of it (42)" );
		var bump = targetRide.Runtime.Bump!;
		Assert.AreEqual( (0, 0, 0, BumpController.PhaseLoading), (bump.VehicleCount, bump.Pending.Count, bump.Leaving.Count, bump.Phase) );
	}

	[TestMethod]
	public void ARideChunkThatWasNotAppliedLeavesItsVehicleChunksUnread()
	{
		var (source, sourceRide, _) = World();
		var cells = SavedCells( source.Guests!.Grid.Cells.Tracks );
		var handle = unchecked( (uint)(-9 << 8 | 3) );
		var vehicle = new uint[49];
		vehicle[0] = handle;
		var body = ChunkBytes( 3, handle, 1, 2, 3, 4, 5, 6, 7, 8, 9 ).Concat( ChunkBytes( 5, vehicle ) ).Concat( ChunkBytes( 7, handle, 1 ) ).ToArray();
		var block = SaveTrackRideBlock.ParseContainer( Words( 1, 0, (uint)(12 + body.Length) ).Concat( body ).ToArray() );
		var (target, targetRide, _) = World( laid: false );
		targetRide.ThingId = RideThing;
		var lines = string.Join( "\n", target.ApplyOriginalTracks( cells, ( _, _ ) => 0, block, null, new[] { RideThingOf( handle ) } ).Concat( target.ApplyOriginalTrackRiders( block ) ) );
		Assert.AreNotEqual( -9, targetRide.Runtime.Bump!.Type, "the kart ride of this level is a type -4 controller" );
		StringAssert.Contains( lines, "not applied: its kind -9 is not the controller type -4" );
		StringAssert.Contains( lines, "its ride chunk was not applied" );
		Assert.AreEqual( 0, targetRide.Runtime.Bump.VehicleCount );
	}

	// [BIN:TPW-EXE:0x00501A08 state 16 update] a rider of a kart ride whose vehicles were refused has nothing that would let it off: the guests are put off the ride and the report says so
	[TestMethod]
	public void RidersOfAKartRideWhoseVehiclesWereRefusedArePutOffTheRide()
	{
		var (cells, block, handle, _) = SavedRace();
		var (target, targetRide, _) = World( laid: false );
		targetRide.ThingId = RideThing;
		var lines = target.ApplyOriginalTracks( cells, ( _, _ ) => 0, block, null, new[] { RideThingOf( handle ) } ).ToList();
		var bridge = targetRide.Visitors;
		bridge.SavedRunStateImported = true;
		SaveGuest Rider( int id ) => SaveGuest.Read( Payload( SaveTestPayloads.Guest( new SaveTestPayloads.GuestFields { Position = (3 * 256 + 128, 3 * 256 + 128), State = 16, MajorDest = (ushort)RideThing } ) ), new SaveThing( id, SaveThingModel.Guest, 0, 0, 8, SaveTestPayloads.Guest( new SaveTestPayloads.GuestFields() ).Length ) );
		// guests 41, 43 and 44 are riders of the ride; guest 42, whom a kart carries, was not imported, so the vehicles are refused
		target.Guests!.Register( bridge );
		var imported = target.Guests.ImportOriginal( new[] { Rider( 41 ), Rider( 43 ), Rider( 44 ) }, 1000, thing => thing == RideThing ? bridge.AttractionId : 0, _ => 0, 3, new[] { SaveAttraction.Read( SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields() ), 0, RideThing ) } );
		Assert.AreEqual( 3, bridge.Riders.Count, string.Join( "\n", imported ) );
		lines.AddRange( target.ApplyOriginalTrackRiders( block ) );
		lines.AddRange( target.DropRidersWithoutCarrier() );
		StringAssert.Contains( string.Join( "\n", lines ), "put off the ride" );
		Assert.AreEqual( 0, bridge.Riders.Count );
		Assert.IsNull( target.Guests.Find( 41 ) );
		var validation = target.Guests.ValidateSnapshot( target.Guests.CaptureSnapshot() );
		Assert.IsTrue( validation.IsValid, string.Join( "; ", validation.Errors ) );
	}

	private static byte[] Payload( byte[] body )
	{
		var payload = new byte[8 + body.Length];
		body.CopyTo( payload, 8 );
		return payload;
	}
}
