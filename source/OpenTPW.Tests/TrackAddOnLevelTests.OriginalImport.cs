using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The kart and water tracks of an original save (TRACK-032, loader <c>0x543560</c>), on the asset-free level of the add-on tests: the track cells of the structure layer and the TrackRides chunks of a laid ride are written
/// the way tp.exe's saver writes them, and a twin level that has only the ride's station takes the same track, circuit and controller words from them.
/// </summary>
public partial class TrackAddOnLevelTests
{
	private const int RideThing = 12;

	private static byte[] Words( params uint[] words ) => words.SelectMany( BitConverter.GetBytes ).ToArray();

	private static byte[] ChunkBytes( uint type, params uint[] payload ) => Words( type, 0, (uint)(12 + payload.Length * 4) ).Concat( Words( payload ) ).ToArray();

	/// <summary>The structure layer's track cells of a level as the save holds them (the 31-byte cells' fields, the origin's cell word in an extension).</summary>
	private static List<SaveTrackCell> SavedCells( TrackCellLayer layer ) =>
		layer.Cells().Select( item => new SaveTrackCell( item.X, item.Y, item.Cell.Type, item.Cell.Mask, item.Cell.Link, item.Cell.Flags,
			item.Cell.IsExtension ? (ushort)(layer.CellOf( item.Cell.Parent ).Y * 128 + layer.CellOf( item.Cell.Parent ).X + 1) : (ushort)0,
			item.Cell.Family, item.Cell.Piece, item.Cell.Rotation, item.Cell.Overlap, (byte)item.Cell.AltMask, (ushort)item.Cell.Segment, 0 ) ).ToList();

	/// <summary>The TrackRides block of one ride as the saver writes it: the build stamp chunk, the ride chunk and one section chunk per section.</summary>
	private static (SaveTrackRideBlock Block, uint Handle) SavedBlock( BumpController bump, int slot = 3 )
	{
		var handle = unchecked( (uint)(bump.Type << 8 | slot) );
		var chunks = new List<byte>();
		// the build stamp chunk: the time and the date strings the saver writes ("15:25:02", "Mar 17 2000" and its terminator)
		chunks.AddRange( Words( 2, 0, 32 ).Concat( System.Text.Encoding.ASCII.GetBytes( "15:25:02Mar 17 2000\0" ) ) );
		chunks.AddRange( ChunkBytes( 3, handle, (uint)bump.StationX, (uint)bump.StationY, (uint)bump.Orientation, 0x10, (uint)bump.Performance, 0x14, 0x18, unchecked( (uint)bump.Laps ), (uint)bump.Phase ) );
		foreach ( var section in bump.Sections )
			chunks.AddRange( ChunkBytes( 4, handle, (uint)section.KindWord, (uint)section.X, (uint)section.Y ) );
		var body = chunks.ToArray();
		return (SaveTrackRideBlock.ParseContainer( Words( 1, 0, (uint)(12 + body.Length) ).Concat( body ).ToArray() ), handle);
	}

	private static SaveAttraction RideThingOf( uint handle ) =>
		SaveAttraction.Read( SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields() ), 0, RideThing ) with { TrackRideHandle = handle, IsTrackRideValid = 1 };

	[TestMethod]
	public void ASavedKartTrackComesBackWithItsCircuitAndControllerWords()
	{
		var (source, sourceRide, _) = World();
		var sourceBump = sourceRide.Runtime.Bump!;
		sourceBump.RestoreOriginalWords( -3, 70, BumpController.PhaseLoading );
		sourceRide.ThingId = RideThing;
		var cells = SavedCells( source.Guests!.Grid.Cells.Tracks );
		Assert.IsTrue( cells.Count > 40, "the loop's tiles and the station's" );
		var (block, handle) = SavedBlock( sourceBump );
		Assert.AreEqual( sourceBump.Sections.Count, block.Chunks.Count( chunk => chunk.Tile != null ) );

		var (target, targetRide, _) = World( laid: false );
		targetRide.ThingId = RideThing;
		Assert.IsFalse( target.KartTracks!.IsTile( 30, 14 ), "the twin starts with its station alone" );
		var lines = target.ApplyOriginalTracks( cells, ( _, _ ) => 0, block, null, new[] { RideThingOf( handle ) } );

		Assert.IsTrue( target.KartTracks.IsTile( 30, 14 ) && target.KartTracks.IsTile( 8, 10 ), "the saved tiles are laid" );
		var bump = targetRide.Runtime.Bump!;
		Assert.IsTrue( bump.IsTrackValid, "the circuit walks valid from the saved cells" );
		Assert.AreEqual( sourceBump.Sections.Count, bump.Sections.Count );
		CollectionAssert.AreEqual( sourceBump.Sections.Select( section => (section.KindWord, section.X, section.Y) ).ToArray(), bump.Sections.Select( section => (section.KindWord, section.X, section.Y) ).ToArray() );
		Assert.AreEqual( (-3, 70, BumpController.PhaseLoading), (bump.Laps, bump.Performance, bump.Phase) );
		var sourceLayer = source.Guests.Grid.Cells.Tracks;
		var targetLayer = target.Guests!.Grid.Cells.Tracks;
		foreach ( var item in sourceLayer.Cells() )
		{
			var other = targetLayer.Get( item.X, item.Y );
			Assert.AreEqual( (item.Cell.Type, item.Cell.BaseType, item.Cell.Mask, item.Cell.Link, item.Cell.Flags, item.Cell.Parent, item.Cell.Family, item.Cell.Piece, item.Cell.Rotation, item.Cell.Overlap, item.Cell.AltMask, item.Cell.Segment, item.Cell.Owner),
				(other.Type, other.BaseType, other.Mask, other.Link, other.Flags, other.Parent, other.Family, other.Piece, other.Rotation, other.Overlap, other.AltMask, other.Segment, other.Owner), $"track cell ({item.X}, {item.Y})" );
			Assert.AreEqual( source.Guests.Grid.Cells.RawTypeAt( item.X, item.Y ), target.Guests.Grid.Cells.RawTypeAt( item.X, item.Y ), $"map type ({item.X}, {item.Y})" );
		}
		Assert.AreEqual( sourceLayer.Count, targetLayer.Count );
		var layerHash = ( Level level ) => { var hash = new StateHasher(); level.Guests!.Grid.Cells.Tracks.AddCanonicalState( hash ); return hash.Value; };
		var controllerHash = ( OriginalObject ride ) => { var hash = new StateHasher(); ride.Runtime.Bump!.AddCanonicalState( hash ); return hash.Value; };
		var sc = source.Guests.Grid.Cells;
		var tc = target.Guests.Grid.Cells;
		for ( var y = 0; y < sc.Height; y++ )
			for ( var x = 0; x < sc.Width; x++ )
				Assert.AreEqual( (sc.RawTypeAt( x, y ), sc.FlagsAt( x, y ), sc.LinksAt( x, y ), sc.QueueLinkAt( x, y ), sc.OwnerAt( x, y )), (tc.RawTypeAt( x, y ), tc.FlagsAt( x, y ), tc.LinksAt( x, y ), tc.QueueLinkAt( x, y ), tc.OwnerAt( x, y )), $"map cell ({x}, {y})" );
		Assert.AreEqual( layerHash( source ), layerHash( target ), "the track layer" );
		Assert.AreEqual( controllerHash( sourceRide ), controllerHash( targetRide ), "the controller" );
		StringAssert.Contains( string.Join( "\n", lines ), "saved sections equal the walked circuit" );
		Assert.IsFalse( lines.Any( line => line.Contains( "differ" ) ), string.Join( "\n", lines ) );
	}

	[TestMethod]
	public void ASavedTrackThatDisagreesWithItsWalkIsReportedAndTheWalkKept()
	{
		var (source, sourceRide, _) = World();
		var cells = SavedCells( source.Guests!.Grid.Cells.Tracks );
		sourceRide.ThingId = RideThing;
		var (block, handle) = SavedBlock( sourceRide.Runtime.Bump! );
		// one section's corner is not where the cells walk
		var done = false;
		var tampered = block.Chunks.Where( chunk => chunk.Tile != null ).Select( chunk => chunk.Tile! ).Select( tile =>
		{
			if ( done || tile.Coordinate0 != 14 * TrackSections.Cell )
				return tile;
			done = true;
			return tile with { Coordinate0 = tile.Coordinate0 + 1 };
		} ).ToList();
		Assert.AreEqual( 1, tampered.Count( tile => tile.Coordinate0 % TrackSections.Cell == 1 ) );
		var (target, targetRide, _) = World( laid: false );
		targetRide.ThingId = RideThing;
		var saved = block.Chunks.Where( chunk => chunk.Tile != null ).ToList();
		// the block is rebuilt with the tampered section: a section list of the same length with one wrong corner
		var chunks = new List<byte>( block.Chunks.Where( chunk => chunk.Ride != null || chunk.Type == 2 ).SelectMany( chunk => ChunkBytes( chunk.Type, Enumerable.Range( 0, chunk.Payload.Length / 4 ).Select( index => BinaryPrimitives.ReadUInt32LittleEndian( chunk.Payload[(index * 4)..] ) ).ToArray() ) ) );
		foreach ( var tile in tampered )
			chunks.AddRange( ChunkBytes( 4, handle, tile.Type, (uint)tile.Coordinate0, (uint)tile.Coordinate1 ) );
		var mismatched = SaveTrackRideBlock.ParseContainer( Words( 1, 0, (uint)(12 + chunks.Count) ).Concat( chunks ).ToArray() );
		var lines = target.ApplyOriginalTracks( cells, ( _, _ ) => 0, mismatched, null, new[] { RideThingOf( handle ) } );
		Assert.IsTrue( targetRide.Runtime.Bump!.IsTrackValid, "the walk of the cells is kept" );
		StringAssert.Contains( string.Join( "\n", lines ), "saved sections differ from the walked circuit" );
		Assert.AreEqual( saved.Count, targetRide.Runtime.Bump.Sections.Count );
	}

	[TestMethod]
	public void ASavedTrackCellOnAnObjectCellOfThisParkIsSkippedAndCounted()
	{
		var (source, _, _) = World();
		var cells = SavedCells( source.Guests!.Grid.Cells.Tracks );
		var (target, targetRide, _) = World( laid: false );
		targetRide.ThingId = RideThing;
		var plain = cells.First( cell => cell.Type != TrackCellTypes.Extension && !TrackCellTypes.IsStation( cell.Type ) );
		var map = target.Guests!.Grid.Cells;
		map.SetType( plain.X, plain.Y, ParkCellTypes.Object );
		Assert.AreEqual( ParkCellTypes.Object, map.RawTypeAt( plain.X, plain.Y ), "precondition" );
		var lines = target.ApplyOriginalTracks( cells, ( _, _ ) => 0, null, null, new[] { RideThingOf( 0 ) } );
		StringAssert.Contains( string.Join( "\n", lines ), "Track cells skipped: 1 saved track cells lie on cells this park holds as an object" );
		Assert.AreEqual( ParkCellTypes.Object, map.RawTypeAt( plain.X, plain.Y ), "the park's object cell is not given to the track" );
	}

	[TestMethod]
	public void StationTilesWhereThePlacementDoesNotLayThemKeepThePlacementsStation()
	{
		var (source, sourceRide, _) = World();
		var cells = SavedCells( source.Guests!.Grid.Cells.Tracks ).Select( cell => cell with { X = cell.X + 20, Y = cell.Y, Parent = cell.Parent == 0 ? cell.Parent : (ushort)(cell.Parent + 20) } ).ToList();
		var (target, targetRide, _) = World( laid: false );
		targetRide.ThingId = RideThing;
		var station = target.TrackStations[targetRide];
		var placed = target.KartTracks!.Tracks.Get( station.First.X, station.First.Y );
		var lines = target.ApplyOriginalTracks( cells.Where( cell => cell.X >= 20 && cell.Y >= 14 ).ToList(), ( _, _ ) => 0, null, null, new[] { RideThingOf( 0 ) } );
		StringAssert.Contains( string.Join( "\n", lines ), "its saved station tiles are not at" );
		Assert.AreEqual( placed, target.KartTracks.Tracks.Get( station.First.X, station.First.Y ), "the placement's station tile is not overwritten" );
	}

	[TestMethod]
	public void ARideChunkOfAnotherKindOrWithVehiclesAndPeepsIsReportedNotApplied()
	{
		var (source, sourceRide, _) = World();
		var cells = SavedCells( source.Guests!.Grid.Cells.Tracks );
		var bump = sourceRide.Runtime.Bump!;
		var handle = unchecked( (uint)(-9 << 8 | 3) );
		var body = ChunkBytes( 3, handle, 1, 2, 3, 4, 5, 6, 7, 8, 9 ).Concat( ChunkBytes( 5, new uint[49].Select( ( _, index ) => index == 0 ? handle : 0u ).ToArray() ) ).Concat( ChunkBytes( 7, handle, 1 ) ).ToArray();
		var block = SaveTrackRideBlock.ParseContainer( Words( 1, 0, (uint)(12 + body.Length) ).Concat( body ).ToArray() );
		var (target, targetRide, _) = World( laid: false );
		targetRide.ThingId = RideThing;
		var lines = string.Join( "\n", target.ApplyOriginalTracks( cells, ( _, _ ) => 0, block, null, new[] { RideThingOf( handle ) } ) );
		Assert.AreNotEqual( -9, targetRide.Runtime.Bump!.Type, "the kart ride of this level is a type -4 controller" );
		StringAssert.Contains( lines, "not applied: its kind -9 is not the controller type -4" );
		StringAssert.Contains( lines, "1 vehicle records and 1 rider and peep chunks" );
	}
}
