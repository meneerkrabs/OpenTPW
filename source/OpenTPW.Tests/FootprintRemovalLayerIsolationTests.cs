using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Synthetic host-contract regressions, not evidence of the original demolition helper's writes (ECON-050).</summary>
[TestClass]
public class FootprintRemovalLayerIsolationTests
{
	[TestMethod]
	public void AddOnRemovalChangesOnlyItsFlagInEachRetainedTrackRecord()
	{
		var types = new byte[] { 11, 12, 13, 16, 18, 25 };
		foreach ( var symbol in new[] { '+', '#' } )
		foreach ( var type in types )
		for ( var bridgeFlags = 0; bridgeFlags <= TrackCell.BridgeMask; bridgeFlags++ )
		{
			var cells = new ParkCellMap( 8, 8 );
			cells.SetType( 3, 3, type );
			// Seed the state at removal, including independent writes after placement. WriteFootprint itself replaces flags.
			var retainedFlags = ParkCellFlags.NoModify | ParkCellFlags.EntranceArea | ParkCellFlags.MapBit04;
			cells.SetFlags( 3, 3, retainedFlags | ParkCellFlags.AddOn );
			cells.SetOwner( 3, 3, ParkCellMap.CellWord( 1, 1 ) );
			cells.SetLinks( 3, 3, 0x44 );
			cells.SetPlacementCount( 3, 3, 3 );
			var track = Record( type, (byte)(bridgeFlags | TrackCell.FlagAddOn) );
			cells.Tracks.Set( 3, 3, track );
			cells.SetType( 4, 3, ParkCellType.Queue );
			cells.SetFlags( 4, 3, ParkCellFlags.NoModify );
			cells.SetOwner( 4, 3, ParkCellMap.CellWord( 1, 1 ) );
			cells.SetLinks( 4, 3, 0x10 );
			cells.SetQueueLink( 4, 3, 0x01 );
			cells.SetPlacementCount( 4, 3, 2 );
			cells.Tracks.Set( 4, 3, Record( TrackCellTypes.Extension, TrackCell.FlagAddOn ) );
			var sentinel = State( cells, 4, 3 );
			var sentinelTrack = cells.Tracks.Get( 4, 3 );
			var before = State( cells, 3, 3 );
			var count = cells.Tracks.Count;
			var typeCount = cells.CountOf( (ParkCellType)type );
			var footprint = ObjectFootprint.GetCells( new ObjectShape( new[] { symbol.ToString() } ), 3, 3, 0 ).ToArray();
			Assert.AreEqual( 1, footprint.Length );
			cells.ClearFootprint( footprint );
			var expectedTrack = track with { Flags = (byte)bridgeFlags };
			Assert.AreEqual( expectedTrack, cells.Tracks.Get( 3, 3 ), $"{symbol}, type {type}, bridge flags {bridgeFlags}: every other track field survives" );
			Assert.AreEqual( before with { Flags = retainedFlags }, State( cells, 3, 3 ) );
			Assert.AreEqual( sentinel, State( cells, 4, 3 ) );
			Assert.AreEqual( sentinelTrack, cells.Tracks.Get( 4, 3 ) );
			Assert.AreEqual( count, cells.Tracks.Count );
			Assert.AreEqual( typeCount, cells.CountOf( (ParkCellType)type ) );
			var trackVersion = cells.Tracks.Version;
			cells.ClearFootprint( footprint );
			Assert.AreEqual( expectedTrack, cells.Tracks.Get( 3, 3 ), "repeated removal does not erase unrelated flags" );
			Assert.AreEqual( trackVersion, cells.Tracks.Version, "no second track write when AddOn is absent" );
			Assert.AreEqual( before with { Flags = retainedFlags }, State( cells, 3, 3 ) );
		}
	}

	[TestMethod]
	public void MismatchedFreeAndOutOfBoundsFootprintsDoNotTouchEitherLayer()
	{
		foreach ( var symbol in new[] { '*', '+', '#', '@', 'Q', '2', 'S', 'D' } )
		{
			var cells = new ParkCellMap( 8, 8 );
			cells.SetType( 3, 3, ParkCellTypes.Water );
			cells.SetFlags( 3, 3, ParkCellFlags.AddOn | ParkCellFlags.NoModify | ParkCellFlags.MapBit04 );
			cells.SetOwner( 3, 3, ParkCellMap.CellWord( 1, 1 ) );
			cells.SetPlacementCount( 3, 3, 7 );
			var track = Record( TrackCellTypes.WaterOrigin, TrackCell.FlagAddOn | TrackCell.FlagBridge );
			cells.Tracks.Set( 3, 3, track );
			var before = State( cells, 3, 3 );
			// Empty type matches a free cell's type 0: only the free-cell guard prevents destructive clearing.
			cells.SetFlags( 2, 2, ParkCellFlags.EntranceArea | ParkCellFlags.MapBit04 );
			cells.SetOwner( 2, 2, ParkCellMap.CellWord( 1, 1 ) );
			cells.SetLinks( 2, 2, 0x44 );
			cells.SetQueueLink( 2, 2, 0x01 );
			cells.SetPlacementCount( 2, 2, 5 );
			cells.Tracks.Set( 2, 2, track );
			var freeBefore = State( cells, 2, 2 );
			var version = cells.Version;
			var trackVersion = cells.Tracks.Version;
			var footprint = ObjectFootprint.GetCells( new ObjectShape( new[] { symbol.ToString() } ), 3, 3, 0 ).ToArray();
			Assert.AreEqual( 1, footprint.Length );
			var shapeCell = footprint[0].Cell;
			cells.ClearFootprint( footprint.Concat( new[]
			{
				(X: -1, Y: 3, Cell: shapeCell), (X: 8, Y: 3, Cell: shapeCell),
				(X: 3, Y: -1, Cell: shapeCell), (X: 3, Y: 8, Cell: shapeCell),
				(X: 2, Y: 2, Cell: new ObjectShapeCell( 0, 0, ObjectCellKind.Free, '.' ))
			} ) );
			Assert.AreEqual( before, State( cells, 3, 3 ), symbol.ToString() );
			Assert.AreEqual( freeBefore, State( cells, 2, 2 ), "free-cell guard preserves even metadata on an empty cell" );
			Assert.AreEqual( track, cells.Tracks.Get( 2, 2 ) );
			Assert.AreEqual( track, cells.Tracks.Get( 3, 3 ) );
			Assert.AreEqual( version, cells.Version, "rejected footprint cells perform no map write" );
			Assert.AreEqual( trackVersion, cells.Tracks.Version, "a mismatched map type also guards the track layer" );
			Assert.AreEqual( 1, cells.CountOf( (ParkCellType)ParkCellTypes.Water ) );
			Assert.AreEqual( 2, cells.Tracks.Count );
		}
	}

	[TestMethod]
	public void RemovingAnAtPathUnlinksItsNeighbourWithoutClearingLandFlagsOrTheTrackLayer()
	{
		var cells = new ParkCellMap( 8, 8 );
		var footprint = ObjectFootprint.GetCells( new ObjectShape( new[] { "@" } ), 3, 3, 0 ).ToArray();
		cells.SetType( 4, 3, ParkCellType.Path );
		cells.WriteFootprint( footprint, ParkCellMap.CellWord( 3, 3 ) );
		Assert.AreNotEqual( 0, cells.LinksAt( 3, 3 ) & 0x04 );
		Assert.AreNotEqual( 0, cells.LinksAt( 4, 3 ) & 0x40 );
		var retainedFlags = ParkCellFlags.EntranceArea | ParkCellFlags.MapBit04;
		cells.SetFlags( 3, 3, cells.FlagsAt( 3, 3 ) | retainedFlags );
		cells.SetPlacementCount( 3, 3, 2 );
		var track = Record( TrackCellTypes.KartOrigin, TrackCell.FlagBridge | TrackCell.FlagAddOn );
		cells.Tracks.Set( 3, 3, track );
		var trackVersion = cells.Tracks.Version;
		cells.ClearFootprint( footprint );
		// the @ cell's own type goes; the type byte then shows the track tile that lies beneath (ParkCellMap.PrimaryTypeAt)
		Assert.AreEqual( new CellState( TrackCellTypes.KartOrigin, retainedFlags, 0, 0, 0, 0 ), State( cells, 3, 3 ) );
		Assert.AreEqual( (byte)ParkCellType.Path, cells.RawTypeAt( 4, 3 ) );
		Assert.AreEqual( 0, cells.LinksAt( 4, 3 ) & 0x40 );
		Assert.AreEqual( 1, cells.CountOf( ParkCellType.Path ) );
		// the removal writes no record of its own; the only change is the bridge re-test of the tile the path cell lay over (0x538A92: the neighbour's path cell (4, 3) still lies over the tile, so it stays a bridge; the piece is picked again)
		var after = cells.Tracks.Get( 3, 3 );
		Assert.AreEqual( track.Type, after.Type );
		Assert.AreEqual( track.Flags, after.Flags, "the add-on flag and the bridge stay" );
		Assert.AreEqual( track.Owner, after.Owner );
		Assert.AreEqual( track.Mask, after.Mask );
	}

	private readonly record struct CellState( byte Type, ParkCellFlags Flags, ushort Owner, byte Links, byte QueueLink, short Placements );
	private static CellState State( ParkCellMap cells, int x, int y ) =>
		new( cells.RawTypeAt( x, y ), cells.FlagsAt( x, y ), cells.OwnerAt( x, y ), cells.LinksAt( x, y ), cells.QueueLinkAt( x, y ), cells.PlacementCountAt( x, y ) );

	private static TrackCell Record( byte type, byte flags ) => new()
	{
		Type = type, BaseType = 1, Mask = 0x44, Link = 0x04, Flags = flags,
		Parent = type == TrackCellTypes.Extension ? 18 : -1, Family = 3, Piece = 7,
		Rotation = 90, Overlap = 2, AltMask = 0x11, Segment = 9, Owner = 19
	};
}
