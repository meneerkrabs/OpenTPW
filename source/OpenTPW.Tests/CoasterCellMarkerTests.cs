using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic ordinary-context marker checks. These do not claim coverage of native first/last tool globals.</summary>
[TestClass]
[DoNotParallelize]
public class CoasterCellMarkerTests
{
	private Logger previousLog = null!;
	[TestInitialize]
	public void Init() { previousLog = Log; Log = new(); }
	[TestCleanup]
	public void Cleanup() => Log = previousLog;

	private static (CoasterBuilder Builder, ParkCellMap Map) NewCoaster()
	{
		var world = new CoasterWorld( 1 );
		var ride = new CoasterRide( CoasterDefinition.FromValues( CoasterTests.PcSettings() ), world, "Marker test" );
		var builder = new CoasterBuilder( ride, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( builder, (20, 20) );
		var map = new ParkCellMap( 64, 64 );
		builder.Track.BindCells( map, 123 );
		return (builder, map);
	}

	[TestMethod]
	[DataRow( (byte)0, true )]
	[DataRow( (byte)1, true )]
	[DataRow( (byte)2, false )]
	[DataRow( (byte)3, false )]
	[DataRow( (byte)4, false )]
	[DataRow( (byte)21, true )]
	public void OrdinaryContextUsesPrimaryTypeAndOwner( byte type, bool allowed )
	{
		var map = new ParkCellMap( 8, 8 );
		map.SetType( 3, 3, type );
		map.SetOwner( 3, 3, 123 );
		Assert.AreEqual( allowed, CoasterCellMarkers.Allows( map, 3, 3, 123 ) );
		if ( type == 21 ) Assert.IsFalse( CoasterCellMarkers.Allows( map, 3, 3, 124 ) );
		Assert.IsFalse( CoasterCellMarkers.Allows( map, -1, 3, 123 ) );
		map.SetFlags( 3, 3, ParkCellFlags.Unowned );
		Assert.IsFalse( CoasterCellMarkers.Allows( map, 3, 3, 123 ) );
	}

	[TestMethod]
	[DataRow( (byte)11 )]
	[DataRow( (byte)13 )]
	[DataRow( (byte)16 )]
	[DataRow( (byte)18 )]
	[DataRow( (byte)25 )]
	public void SecondaryRefusalPrecedesMatchingMarkerOwner( byte type )
	{
		var map = new ParkCellMap( 8, 8 );
		map.SetType( 3, 3, 21 );
		map.SetOwner( 3, 3, 123 );
		map.Tracks.Set( 3, 3, new TrackCell { Type = type } );
		Assert.IsFalse( CoasterCellMarkers.Allows( map, 3, 3, 123 ) );
		map.Tracks.Set( 3, 3, new TrackCell { Type = 12, Parent = map.Tracks.IndexOf( 4, 3 ) } );
		map.Tracks.Set( 4, 3, new TrackCell { Type = type } );
		Assert.IsFalse( CoasterCellMarkers.Allows( map, 3, 3, 123 ) );
	}

	[TestMethod]
	public void TrackOnlyClearRetainsStationButFullRemovalClearsIt()
	{
		var (builder, map) = NewCoaster();
		var head = builder.Track.Nodes[0];
		Assert.AreEqual( (byte)21, map.RawTypeAt( head.X, head.Y ) );
		Assert.AreSame( head, builder.Track.CellMarkers!.TokenAt( head.X, head.Y ) );
		builder.Delete();
		Assert.AreEqual( (byte)21, map.RawTypeAt( head.X, head.Y ) );
		builder.Ride.World.Remove( builder.Ride );
		Assert.AreEqual( (byte)0, map.RawTypeAt( head.X, head.Y ) );
		Assert.AreEqual( (ushort)0, map.OwnerAt( head.X, head.Y ) );
		Assert.IsNull( builder.Track.CellMarkers );
	}

	[TestMethod]
	public void ReplacementWithSameOwnerInvalidatesOldCleanup()
	{
		var (builder, map) = NewCoaster();
		var head = builder.Track.Nodes[0];
		map.SetType( head.X, head.Y, 21 );
		map.SetOwner( head.X, head.Y, 123 );
		builder.Ride.World.Remove( builder.Ride );
		Assert.AreEqual( (byte)21, map.RawTypeAt( head.X, head.Y ), "Host generation guard, not a claimed native owner check." );
	}

	[TestMethod]
	public void BindingSavedMarkerReconstructsTokenWithoutChangingMapStamp()
	{
		var (builder, map) = NewCoaster();
		var head = builder.Track.Nodes[0];
		var secondMap = new ParkCellMap( 64, 64 );
		foreach ( var node in builder.Track.Nodes )
		{
			if ( node.IsPylonless ) continue;
			secondMap.SetType( node.X, node.Y, 21 );
			secondMap.SetOwner( node.X, node.Y, 123 );
		}
		var stamp = secondMap.StampCounter;
		builder.Track.BindCells( secondMap, 123 );
		Assert.AreEqual( stamp, secondMap.StampCounter );
		Assert.AreSame( head, builder.Track.CellMarkers!.TokenAt( head.X, head.Y ) );
		Assert.AreEqual( (byte)0, map.RawTypeAt( head.X, head.Y ), "Old map's binding is released." );
	}
	[TestMethod]
	public void StacksRepublishSurvivorAndRemoveOnlyLastReservation()
	{
		var (builder, map) = NewCoaster();
		builder.Place( 24, 20 );
		var first = builder.Track.Nodes[^1];
		builder.Place( 27, 24 );
		builder.Place( 24, 20 );
		var markers = builder.Track.CellMarkers!;
		Assert.AreEqual( 2, markers.CountAt( 24, 20 ) );
		Assert.AreSame( builder.Track.Nodes[^1], markers.TokenAt( 24, 20 ), "Construction publishes the newly added node." );
		Assert.IsTrue( builder.RemoveLast() );
		Assert.AreEqual( 1, markers.CountAt( 24, 20 ) );
		Assert.AreSame( first, markers.TokenAt( 24, 20 ), "Removal republishes the surviving first stack node." );
		Assert.AreEqual( (byte)21, map.RawTypeAt( 24, 20 ) );
		builder.Move( CoasterTrack.StationNodeCount, 25, 21 );
		Assert.AreEqual( (byte)0, map.RawTypeAt( 24, 20 ) );
		Assert.AreEqual( (byte)21, map.RawTypeAt( 25, 21 ) );
	}

	[TestMethod]
	public void GeometricallyInvalidPylonStillExistsButBlockedMapCellDoesNotMutate()
	{
		var (builder, map) = NewCoaster();
		var before = builder.Track.Nodes.Count;
		builder.Place( 21, 20 );
		Assert.AreEqual( before + 1, builder.Track.Nodes.Count );
		Assert.IsTrue( builder.Track.HasInvalidNodes );
		map.SetType( 24, 22, 4 );
		var version = builder.Track.Version;
		Assert.AreEqual( CoasterPlaceResult.CellOccupied, builder.Place( 24, 22 ) );
		Assert.AreEqual( version, builder.Track.Version );
		Assert.AreEqual( (byte)4, map.RawTypeAt( 24, 22 ) );
	}

	[TestMethod]
	public void SingleCellPreviewAndDefensiveForeignLeaseAreSeparate()
	{
		var (first, map) = NewCoaster();
		first.Place( 24, 20 );
		var owningNode = first.Track.Nodes[^1];
		var secondRide = new CoasterRide( first.Ride.Definition, first.Ride.World, "Other coaster" );
		var second = new CoasterBuilder( secondRide, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( second, (20, 20) );
		second.Track.BindCells( map, 124 );
		Assert.IsTrue( CoasterCellMarkers.AllowsPreview( map, 24, 20, 124, moving: false ), "Native first+last equal-type preview accepts the foreign marker." );
		Assert.IsFalse( CoasterCellMarkers.AllowsPreview( map, 24, 20, 124, moving: true ), "Move16 does not equal type21 and requires owner." );
		Assert.AreEqual( CoasterPlaceResult.OtherCoaster, second.Place( 24, 20 ) );
		Assert.AreEqual( CoasterTrack.StationNodeCount + 1, second.Track.Nodes.Count );
		Assert.IsTrue( (second.Track.Nodes[^1].Flags & CoasterNodeFlags.Invalid) != 0 );
		Assert.AreEqual( (ushort)123, map.OwnerAt( 24, 20 ) );
		Assert.AreSame( owningNode, first.Track.CellMarkers!.TokenAt( 24, 20 ) );
		Assert.IsNull( second.Track.CellMarkers!.TokenAt( 24, 20 ), "Host guard does not steal the foreign reservation for the retained invalid node." );
		Assert.IsTrue( second.RemoveLast() );
		Assert.AreEqual( (byte)21, map.RawTypeAt( 24, 20 ) );
		Assert.AreSame( owningNode, first.Track.CellMarkers.TokenAt( 24, 20 ) );
	}

	[TestMethod]
	public void Marker100SuccessPrecedesSecondaryAndUnownedButDoesNotGrantAForeignLease()
	{
		var map = new ParkCellMap( 8, 8 );
		map.SetType( 3, 3, 21 );
		map.SetOwner( 3, 3, 123 );
		map.SetFlags( 3, 3, (ParkCellFlags)0x140 );
		map.Tracks.Set( 3, 3, new TrackCell { Type = 11 } );
		Assert.IsTrue( CoasterCellMarkers.AllowsPreview( map, 3, 3, 123, false ) );
		Assert.IsFalse( CoasterCellMarkers.AllowsPreview( map, 3, 3, 124, false ) );
		Assert.IsFalse( CoasterCellMarkers.AllowsPreview( map, 3, 3, 123, true ) );
		Assert.IsFalse( CoasterCellMarkers.Allows( map, 3, 3, 123 ), "Preview success is not permission for a host lease to overwrite another layer." );
	}

	[TestMethod]
	public void MoveAcceptsItsEqualType22ContextButPlaceDoesNot()
	{
		var (builder, map) = NewCoaster();
		builder.Place( 24, 20 );
		map.SetType( 25, 21, 22 );
		Assert.IsTrue( CoasterCellMarkers.AllowsPreview( map, 25, 21, 123, true ) );
		Assert.IsFalse( CoasterCellMarkers.AllowsPreview( map, 25, 21, 123, false ) );
		builder.Move( CoasterTrack.StationNodeCount, 25, 21 );
		Assert.AreEqual( (byte)21, map.RawTypeAt( 25, 21 ) );
		Assert.AreEqual( (ushort)123, map.OwnerAt( 25, 21 ) );
	}

	[TestMethod]
	public void RestoredTrackRebindsDerivedTokensAfterMapRestore()
	{
		var (builder, _) = NewCoaster();
		builder.Place( 24, 20 );
		var saved = CoasterSave.CaptureRide( builder.Ride, 7 );
		var restored = new CoasterRide( builder.Ride.Definition, new CoasterWorld( 1 ), "Restored" );
		var fresh = new CoasterBuilder( restored, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( fresh, (20, 20) );
		restored.RestoreSnapshot( saved );
		var map = new ParkCellMap( 64, 64 );
		foreach ( var node in restored.Track.Nodes )
		{
			if ( node.IsPylonless ) continue;
			map.SetType( node.X, node.Y, 21 );
			map.SetOwner( node.X, node.Y, 123 );
		}
		var stamp = map.StampCounter;
		restored.Track.BindCells( map, 123 );
		Assert.AreEqual( stamp, map.StampCounter );
		Assert.AreSame( restored.Track.Nodes[^1], restored.Track.CellMarkers!.TokenAt( 24, 20 ) );
		fresh.Delete();
		Assert.AreEqual( (byte)0, map.RawTypeAt( 24, 20 ) );
		Assert.AreEqual( (byte)21, map.RawTypeAt( 17, 20 ) );
	}

	[TestMethod]
	public void StackCountChangeClearsMarker100EvenWhenFirstTokenSurvives()
	{
		var (builder, map) = NewCoaster();
		var head = builder.Track.Nodes[0];
		Assert.AreEqual( 0x100, (ushort)map.FlagsAt( head.X, head.Y ) & 0x100 );
		builder.Track.Append( builder.Ride.World, builder.Ride, head.X, head.Y, 40, false );
		Assert.AreSame( builder.Track.Nodes[^1], builder.Track.CellMarkers!.TokenAt( head.X, head.Y ) );
		Assert.AreEqual( 2, builder.Track.CellMarkers.CountAt( head.X, head.Y ) );
		Assert.AreEqual( 0, (ushort)map.FlagsAt( head.X, head.Y ) & 0x100 );
		map.SetFlags( head.X, head.Y, (ParkCellFlags)((ushort)map.FlagsAt( head.X, head.Y ) | 0x100) );
		Assert.IsTrue( builder.RemoveLast() );
		Assert.AreSame( head, builder.Track.CellMarkers.TokenAt( head.X, head.Y ) );
		Assert.AreEqual( 0, (ushort)map.FlagsAt( head.X, head.Y ) & 0x100 );
	}

	/// <summary>A copy of <paramref name="map"/>'s cells (the types, owners and flags a save holds), with fresh generations, as a join's restore makes it.</summary>
	private static ParkCellMap Copy( ParkCellMap map )
	{
		var copy = new ParkCellMap( map.Width, map.Height );
		for ( var y = 0; y < map.Height; y++ )
			for ( var x = 0; x < map.Width; x++ )
			{
				copy.SetType( x, y, map.RawTypeAt( x, y ) );
				copy.SetOwner( x, y, map.OwnerAt( x, y ) );
				copy.SetFlags( x, y, map.FlagsAt( x, y ) );
			}
		return copy;
	}

	private static void AssertSameCells( ParkCellMap expected, ParkCellMap actual, string what )
	{
		for ( var y = 0; y < expected.Height; y++ )
			for ( var x = 0; x < expected.Width; x++ )
				Assert.AreEqual( (expected.RawTypeAt( x, y ), expected.OwnerAt( x, y ), expected.FlagsAt( x, y )), (actual.RawTypeAt( x, y ), actual.OwnerAt( x, y ), actual.FlagsAt( x, y )),
					$"{what}: cell ({x}, {y})" );
	}

	/// <summary>The coaster of <paramref name="builder"/> restored from its save, as a join's guest builds it.</summary>
	private static CoasterBuilder Restored( CoasterBuilder builder )
	{
		var restored = new CoasterRide( builder.Ride.Definition, new CoasterWorld( 1 ), "Joined" );
		var fresh = new CoasterBuilder( restored, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( fresh, (20, 20) );
		restored.RestoreSnapshot( CoasterSave.CaptureRide( builder.Ride, 7 ) );
		return fresh;
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] A join's guest takes the host's binding over without claiming what it does not hold: here a pylon coaster A placed on coaster B's marker (the preview
	/// accepts it, the lease is refused), whose cell B released since. The host's A has not synchronized since, so it does not hold the cell; a resume's bind would claim it at once.
	/// </summary>
	[TestMethod]
	public void AJoinAdoptsTheHostsBindingWithoutClaimingACellItWantsButDoesNotHold()
	{
		var (b, map) = NewCoaster();
		Assert.AreEqual( CoasterPlaceResult.Ok, b.Place( 24, 20 ) );
		var aRide = new CoasterRide( b.Ride.Definition, b.Ride.World, "Coaster A" );
		var a = new CoasterBuilder( aRide, new CoasterSite( 64, 64 ) );
		CoasterTests.PlaceStation( a, (20, 20) );
		a.Track.BindCells( map, 124 );
		Assert.AreEqual( CoasterPlaceResult.OtherCoaster, a.Place( 24, 20 ), "A's pylon on B's marker: kept, without the cell" );
		b.Move( CoasterTrack.StationNodeCount, 25, 21 );
		Assert.AreEqual( (byte)0, map.RawTypeAt( 24, 20 ), "B released the cell; A's track did not change, so A did not claim it" );

		var guest = Copy( map );
		var joined = Restored( a );
		var unheld = a.Track.CellMarkers!.UnheldCells().ToArray();
		var stamp = guest.StampCounter;
		joined.Track.AdoptCells( guest, 124, null, unheld );
		Assert.AreEqual( stamp, guest.StampCounter, "the adoption writes nothing" );
		AssertSameCells( map, guest, "at the cut" );
		Assert.AreEqual( 0, joined.Track.CellMarkers!.CountAt( 24, 20 ) );
		// a resume's bind would have claimed it
		var resumed = Copy( map );
		Restored( a ).Track.BindCells( resumed, 124 );
		Assert.AreEqual( (byte)21, resumed.RawTypeAt( 24, 20 ) );

		// the next change of A's track synchronizes on both, alike
		Assert.AreEqual( a.Place( 27, 24 ), joined.Place( 27, 24 ) );
		AssertSameCells( map, guest, "after an edit" );
		Assert.AreEqual( (byte)21, guest.RawTypeAt( 24, 20 ) );
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] A lease the host's binding holds stale at the cut (another writer rewrote its cell, and the binding has not synchronized since) is reported displaced,
	/// and the guest's adoption leaves it so: the next change of the track displaces it on both alike.
	/// </summary>
	[TestMethod]
	public void AJoinsAdoptionDisplacesALeaseTheHostHoldsStaleAtTheCut()
	{
		var (host, map) = NewCoaster();
		Assert.AreEqual( CoasterPlaceResult.Ok, host.Place( 24, 20 ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, host.Place( 27, 24 ) );
		map.SetType( 24, 20, 21 );
		map.SetOwner( 24, 20, 123 );
		var unheld = host.Track.CellMarkers!.UnheldCells().ToArray();
		CollectionAssert.Contains( unheld, (24, 20, true), "the stale lease is reported displaced" );

		var guest = Copy( map );
		var joined = Restored( host );
		joined.Track.AdoptCells( guest, 123, null, unheld );
		AssertSameCells( map, guest, "at the cut" );
		Assert.AreEqual( host.Place( 30, 27 ), joined.Place( 30, 27 ) );
		AssertSameCells( map, guest, "after an edit" );
		Assert.AreEqual( host.Track.CellMarkers.CountAt( 24, 20 ), joined.Track.CellMarkers!.CountAt( 24, 20 ) );
		host.Move( CoasterTrack.StationNodeCount, 26, 22 );
		joined.Move( CoasterTrack.StationNodeCount, 26, 22 );
		AssertSameCells( map, guest, "after the pylon left the cell" );
	}

	/// <summary>[EXT:online-coop-snapshot-join] The cells the host's binding wants and does not hold although the adoption would (a stale lease, a cell it may not claim) stay so on the guest.</summary>
	[TestMethod]
	public void AJoinsAdoptionLeavesTheHostsUnheldCellsUnheld()
	{
		var (host, map) = NewCoaster();
		Assert.AreEqual( CoasterPlaceResult.Ok, host.Place( 24, 20 ) );
		Assert.AreEqual( CoasterPlaceResult.Ok, host.Place( 27, 24 ) );
		// another writer rewrites a held cell with the same owner word (a stale lease, displaced at the next synchronization), and a held cell's land is no longer the park's
		map.SetType( 24, 20, 21 );
		map.SetOwner( 24, 20, 123 );
		var unowned = host.Track.Nodes[^1];
		map.SetFlags( unowned.X, unowned.Y, map.FlagsAt( unowned.X, unowned.Y ) | ParkCellFlags.Unowned );
		host.Track.CellMarkers!.Synchronize();
		var unheld = host.Track.CellMarkers.UnheldCells().ToArray();
		CollectionAssert.Contains( unheld, (24, 20, true ), "displaced" );

		var guest = Copy( map );
		var joined = Restored( host );
		joined.Track.AdoptCells( guest, 123, null, unheld );
		AssertSameCells( map, guest, "at the cut" );
		Assert.AreEqual( host.Track.CellMarkers.CountAt( 24, 20 ), joined.Track.CellMarkers!.CountAt( 24, 20 ) );
		// an edit on both: the displaced cell stays the other writer's, and the rest follows alike; then the pylon leaves the displaced cell, which neither releases
		Assert.AreEqual( host.Place( 30, 27 ), joined.Place( 30, 27 ) );
		AssertSameCells( map, guest, "after an edit" );
		host.Move( CoasterTrack.StationNodeCount, 26, 22 );
		joined.Move( CoasterTrack.StationNodeCount, 26, 22 );
		AssertSameCells( map, guest, "after the pylon left the displaced cell" );
		Assert.AreEqual( (byte)21, guest.RawTypeAt( 24, 20 ) );
		Assert.AreEqual( host.Track.CellMarkers.CountAt( 24, 20 ), joined.Track.CellMarkers.CountAt( 24, 20 ) );
	}

	[TestMethod]
	public void ForeignReplacementSurvivesSynchronizationAndDisposal()
	{
		var (builder, map) = NewCoaster();
		var head = builder.Track.Nodes[0];
		map.SetType( head.X, head.Y, 21 );
		map.SetOwner( head.X, head.Y, 999 );
		builder.Track.CellMarkers!.Synchronize();
		Assert.IsNull( builder.Track.CellMarkers.TokenAt( head.X, head.Y ) );
		builder.Ride.World.Remove( builder.Ride );
		Assert.AreEqual( (ushort)999, map.OwnerAt( head.X, head.Y ) );
		Assert.AreEqual( (byte)21, map.RawTypeAt( head.X, head.Y ) );
	}

	[TestMethod]
	public void ForeignSecondaryLayerSurvivesBindingAndDisposal()
	{
		var (builder, _) = NewCoaster();
		var head = builder.Track.Nodes[0];
		var map = new ParkCellMap( 64, 64 );
		var foreign = new TrackCell { Type = 11, Owner = 777, Piece = 4, Parent = -1, Flags = 8 };
		map.Tracks.Set( head.X, head.Y, foreign );
		builder.Track.BindCells( map, 123 );
		Assert.IsNull( builder.Track.CellMarkers!.TokenAt( head.X, head.Y ) );
		Assert.AreEqual( foreign, map.Tracks.Get( head.X, head.Y ) );
		builder.Ride.World.Remove( builder.Ride );
		Assert.AreEqual( foreign, map.Tracks.Get( head.X, head.Y ) );
		Assert.AreEqual( (byte)0, map.RawTypeAt( head.X, head.Y ) );
	}

}
