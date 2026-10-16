using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The original cell record's owner word (<c>+0x10</c>) and the cells a placement stamps beyond the plain object types (RIDES-011, RIDES-031, PATH-005): tp.exe's
/// placement commit <c>0x528A70</c> writes the new object's cell word on its footprint cells, the flag word 0x20 on an '@' path cell, 0x10 on a '+' or '#' cell.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkCellOwnerTests
{
	[TestMethod]
	public void TheCellWordIsTheOriginalsRowTimes128PlusColumnPlusOne()
	{
		// [BIN:TPW-EXE:0x00532FC0 cell word] every cell lookup decodes a cell word w as x = (w − 1) & 0x7F and y = (w − 1) >> 7
		Assert.AreEqual( (ushort)1, ParkCellMap.CellWord( 0, 0 ) );
		Assert.AreEqual( (ushort)2996, ParkCellMap.CellWord( 51, 23 ), "the Jungle Belly Bounce's anchor" );
		Assert.AreEqual( (ushort)16384, ParkCellMap.CellWord( 127, 127 ) );
	}

	[TestMethod]
	public void AFootprintWritesItsOwnerWordOnTheCellsItTypesAndAnEmptiedCellLosesIt()
	{
		var cells = new ParkCellMap( 16, 16 );
		var shape = new ObjectShape( new[] { "**", "2S" } );
		var footprint = ObjectFootprint.GetCells( shape, 3, 4, 0 ).ToList();
		var owner = ParkCellMap.CellWord( 3, 4 );
		cells.WriteFootprint( footprint, owner );
		Assert.IsTrue( footprint.All( cell => cells.OwnerAt( cell.X, cell.Y ) == owner ) );
		Assert.AreEqual( 0, cells.OwnerAt( 5, 5 ), "a cell outside the footprint has no owner" );
		Assert.AreEqual( (byte)9, cells.RawTypeAt( 3, 4 ) );
		cells.ClearFootprint( footprint );
		Assert.IsTrue( footprint.All( cell => cells.OwnerAt( cell.X, cell.Y ) == 0 && cells.RawTypeAt( cell.X, cell.Y ) == 0 ), "the owner goes with the type" );
	}

	[TestMethod]
	public void APathCellInsideAFootprintGetsNoModifyAndLinksToItsPathNeighbours()
	{
		// [BIN:TPW-EXE:0x00529173 '@' footprint cell] type 1, flag word 0x20, the link writer in mode 1 for the cell and for each path neighbour it links to
		var cells = new ParkCellMap( 16, 16 );
		cells.SetType( 5, 4, ParkCellType.Path );
		var shape = new ObjectShape( new[] { "*@*" } );
		var footprint = ObjectFootprint.GetCells( shape, 2, 4, 0 ).ToList();
		cells.WriteFootprint( footprint, ParkCellMap.CellWord( 2, 4 ) );
		Assert.AreEqual( (byte)ParkCellType.Path, cells.RawTypeAt( 3, 4 ) );
		Assert.AreEqual( ParkCellFlags.NoModify, cells.FlagsAt( 3, 4 ) );
		Assert.AreEqual( ParkCellMap.CellWord( 2, 4 ), cells.OwnerAt( 3, 4 ) );
		Assert.AreEqual( (byte)ParkCellTypes.Object, cells.RawTypeAt( 2, 4 ) );
		// The path cell at (5, 4) is two cells away, so it is not linked; a path cell beside the '@' is, both ways.
		Assert.AreEqual( 0, cells.LinksAt( 3, 4 ) & 0x04 );
		cells.ClearFootprint( footprint );
		cells.SetType( 4, 3, ParkCellType.Path );
		cells.SetType( 3, 3, ParkCellType.Path );
		var beside = ObjectFootprint.GetCells( shape, 2, 4, 0 ).ToList();
		cells.WriteFootprint( beside, ParkCellMap.CellWord( 2, 4 ) );
		Assert.AreNotEqual( 0, cells.LinksAt( 3, 4 ) & 0x01, "the '@' cell links to the path cell above it" );
		Assert.AreNotEqual( 0, cells.LinksAt( 3, 3 ) & 0x10, "and the neighbour links back" );
		cells.ClearFootprint( beside );
		Assert.AreEqual( 0, cells.LinksAt( 3, 3 ) & 0x10, "removing the object unlinks the cell again" );
		Assert.AreEqual( ParkCellFlags.None, cells.FlagsAt( 3, 4 ) );
	}

	[TestMethod]
	public void AQueueCellInsideAFootprintTakesTheGenericStampOnly()
	{
		// [BIN:TPW-EXE:0x00529119 'Q' footprint cell] the type-3 shape cell is stamped through the generic SetCellType; no link pass runs for it
		var cells = new ParkCellMap( 8, 8 );
		cells.WriteFootprint( ObjectFootprint.GetCells( new ObjectShape( new[] { "Q*" } ), 1, 1, 0 ).ToList(), ParkCellMap.CellWord( 1, 1 ) );
		Assert.AreEqual( (byte)ParkCellType.Queue, cells.RawTypeAt( 1, 1 ) );
		Assert.AreEqual( 0, cells.LinksAt( 1, 1 ) );
		Assert.AreEqual( ParkCellFlags.None, cells.FlagsAt( 1, 1 ) );
		Assert.AreEqual( ParkCellMap.CellWord( 1, 1 ), cells.OwnerAt( 1, 1 ) );
	}

	[TestMethod]
	public void AnAddOnFootprintFlagsItsCellsAndTheTrackLayerBeneath()
	{
		// [BIN:TPW-EXE:0x0052950D '+' and '#' footprint cell] the flag word 0x10 on the cell and on the second-layer cell at the same coordinates
		var cells = new ParkCellMap( 12, 12 );
		var tile = new TrackCell { Type = TrackCellTypes.KartOrigin, Flags = TrackCell.FlagBridge | TrackBuilder.FlagOverPath, Parent = -1 };
		cells.Tracks.Set( 4, 4, tile );
		cells.SetType( 4, 4, TrackCellTypes.KartOrigin );
		var plus = ObjectFootprint.GetCells( new ObjectShape( new[] { "+#" } ), 4, 4, 0 ).ToList();
		// the cell map's own type of the track cell is stood in for by the layer: the footprint rule types empty cells only
		cells.SetType( 4, 4, ParkCellType.Empty );
		cells.WriteFootprint( plus, ParkCellMap.CellWord( 4, 4 ) );
		Assert.AreEqual( ParkCellFlags.AddOn, cells.FlagsAt( 4, 4 ) );
		Assert.AreEqual( ParkCellFlags.AddOn, cells.FlagsAt( 5, 4 ), "'#' like '+'" );
		Assert.AreEqual( (byte)ParkCellTypes.Object, cells.RawTypeAt( 4, 4 ), "stored as an object cell" );
		Assert.AreEqual( TrackCell.FlagAddOn, cells.Tracks.Get( 4, 4 ).Flags, "the track cell's flag word is set to exactly 0x10, the bridge bits gone" );
		Assert.AreEqual( 0, cells.Tracks.Get( 5, 4 ).Type, "no track cell, none written" );
		cells.ClearFootprint( plus );
		Assert.AreEqual( 0, cells.Tracks.Get( 4, 4 ).Flags & TrackCell.FlagAddOn );
		Assert.AreEqual( TrackCellTypes.KartOrigin, cells.Tracks.Get( 4, 4 ).Type, "the track stays" );
	}

	[TestMethod]
	public void TheOwnerWordSurvivesASnapshotAndAnOldSnapshotLoadsWithoutIt()
	{
		var cells = new ParkCellMap( 16, 16 );
		cells.WriteFootprint( ObjectFootprint.GetCells( new ObjectShape( new[] { "*2" } ), 5, 5, 0 ).ToList(), ParkCellMap.CellWord( 5, 5 ) );
		var objects = new ParkObjects( null!, new SandboxParkGrid() );
		var snapshot = ParkLayoutSnapshot.CaptureSnapshot( cells, objects );
		Assert.AreEqual( ParkCellMap.CellWord( 5, 5 ), snapshot.Cells.Cells.Single( cell => cell.X == 5 && cell.Y == 5 ).Owner );
		var json = JsonSerializer.Serialize( snapshot );
		var restored = ParkLayoutSnapshot.RestoreCellMap( JsonSerializer.Deserialize<ParkLayoutSnapshot.Data>( json )! );
		Assert.AreEqual( ParkCellMap.CellWord( 5, 5 ), restored.OwnerAt( 6, 5 ) );
		// A save from before the owner word has no "Owner" member; it loads with owner 0 and the objects write the words again when they are placed back.
		var old = System.Text.RegularExpressions.Regex.Replace( json, ",\"Owner\":\\d+", "" );
		Assert.IsFalse( old.Contains( "Owner" ) );
		Assert.AreEqual( 0, ParkLayoutSnapshot.RestoreCellMap( JsonSerializer.Deserialize<ParkLayoutSnapshot.Data>( old )! ).OwnerAt( 6, 5 ) );
		var bad = snapshot with { Cells = snapshot.Cells with { Cells = snapshot.Cells.Cells.Append( new ParkLayoutSnapshot.CellData { X = 1, Y = 1, Type = 0, Flags = 0x20, Placements = 0, Links = 0, QueueLink = 0, Owner = 7 } ).ToList() } };
		Assert.ThrowsException<InvalidDataException>( () => ParkLayoutSnapshot.RestoreCellMap( bad ), "an owner word on an empty cell is refused" );
	}

	// ---------------------------------------------------------------- Easymode (original data)

	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
	}

	[TestMethod]
	public void EasymodesSavedOwnerWordsAreTheAnchorCellWordsThePlacementWrites()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the Jungle Easymode save." );
		Log = new();
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( OriginalParkImportTests.OriginalDataPath() );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
		var park = OriginalPark.Load( "jungle" );
		if ( park.Save == null )
			Assert.Inconclusive( "The Jungle Easymode.TPWI save is missing." );
		var cells = ParkCellMap.FromOriginal( park.Map, park.Save, park.Heightfield );
		var catalog = ObjectCatalog.Load( "jungle" );
		var typed = 0;
		var mismatches = new List<string>();
		foreach ( var record in park.Save.PlacedObjects.Select( item => item.Record ).Concat( park.Save.UnresolvedObjects ) )
		{
			if ( catalog.Find( record.InfoId ) is not { IsTool: false, IsFixedItem: false } entry )
				continue;
			var footprint = ObjectFootprint.GetCells( entry.Shape, record.X, record.Y, record.Rotation ).ToList();
			cells.WriteFootprint( footprint, ParkCellMap.CellWord( record.X, record.Y ) );
			foreach ( var (x, y, _) in footprint )
			{
				typed++;
				// [BIN:TPW-EXE:0x4d0b30 cell base serializer] mParentID is the u16 at record +11
				var saved = park.Save.Cells[x, y].MapParentId;
				if ( saved != cells.OwnerAt( x, y ) )
					mismatches.Add( $"({x}, {y}) of {entry.DisplayName} saved owner {saved}, written {cells.OwnerAt( x, y )}" );
			}
		}
		Assert.IsTrue( typed > 40, $"{typed} footprint cells" );
		Assert.AreEqual( 0, mismatches.Count, string.Join( "; ", mismatches.Take( 20 ) ) );
		// the flag the '+' and '#' cells write never occurs in the saved park, nor does any saved footprint cell carry the flag word of an '@' cell
		for ( var y = 0; y < cells.Height; y++ )
			for ( var x = 0; x < cells.Width; x++ )
				Assert.AreEqual( 0, park.Save.Cells[x, y].MapFlags & (ushort)ParkCellFlags.AddOn, $"({x}, {y}) carries flag 0x10" );
	}

	[TestMethod]
	public void TheCellsAPlacementWritesAreTheCellsEasymodeSaved()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the Jungle Easymode save." );
		Log = new();
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( OriginalParkImportTests.OriginalDataPath() );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
		var park = OriginalPark.Load( "jungle" );
		if ( park.Save == null )
			Assert.Inconclusive( "The Jungle Easymode.TPWI save is missing." );
		var catalog = ObjectCatalog.Load( "jungle" );
		// A bare map: the placement alone writes every cell compared below, so what the save holds there is what the placement wrote (the player's own queue and path cells are elsewhere).
		var cells = new ParkCellMap( park.Save.Cells.CountX, park.Save.Cells.CountY );
		var mismatches = new List<string>();
		var compared = 0;
		foreach ( var record in park.Save.PlacedObjects.Select( item => item.Record ).Concat( park.Save.UnresolvedObjects ) )
		{
			if ( catalog.Find( record.InfoId ) is not { IsTool: false, IsFixedItem: false } entry )
				continue;
			var owner = ParkCellMap.CellWord( record.X, record.Y );
			var footprint = ObjectFootprint.GetCells( entry.Shape, record.X, record.Y, record.Rotation ).ToList();
			var points = ObjectFootprint.GetAccessPoints( entry.Shape, record.X, record.Y, record.Rotation ).ToList();
			cells.WriteFootprint( footprint, owner, record.Rotation );
			ObjectAccessCells.Lay( cells, points, owner, entry.HasQueue );
			void Compare( string what, int x, int y, bool queueLink, bool linkBits )
			{
				var saved = park.Save.Cells[x, y].Record.Span;
				var savedType = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( saved.Slice( 25, 4 ) );
				var savedFlags = (ushort)(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian( saved.Slice( 2, 2 ) ) & 0x3F);
				compared++;
				if ( savedType != cells.RawTypeAt( x, y ) )
					mismatches.Add( $"{entry.DisplayName} {what} ({x}, {y}) type saved {savedType}, written {cells.RawTypeAt( x, y )}" );
				if ( savedFlags != (ushort)cells.FlagsAt( x, y ) )
					mismatches.Add( $"{entry.DisplayName} {what} ({x}, {y}) flags saved {savedFlags:X}, written {(ushort)cells.FlagsAt( x, y ):X}" );
				if ( queueLink && saved[1] != cells.QueueLinkAt( x, y ) )
					mismatches.Add( $"{entry.DisplayName} {what} ({x}, {y}) queue link saved {saved[1]:X2}, written {cells.QueueLinkAt( x, y ):X2}" );
				if ( linkBits && (saved[8] & cells.LinksAt( x, y )) != cells.LinksAt( x, y ) )
					mismatches.Add( $"{entry.DisplayName} {what} ({x}, {y}) link byte {cells.LinksAt( x, y ):X2} not within the saved {saved[8]:X2}" );
			}
			foreach ( var point in points )
			{
				// the entrance and exit cells: type, queue link (the rotated direction, or for a queue ride's entrance the way to its outside cell) and the link bit toward the outside cell
				Compare( point.Kind.ToString(), point.X, point.Y, queueLink: true, linkBits: true );
				if ( point.Kind == ObjectCellKind.Entrance || entry.HasQueue )
					Compare( point.Kind + " outside", point.OutsideX, point.OutsideY, queueLink: point.Kind == ObjectCellKind.Entrance && entry.HasQueue, linkBits: false );
			}
		}
		Assert.IsTrue( compared >= 14, $"{compared} cells compared" );
		Assert.AreEqual( 0, mismatches.Count, string.Join( "; ", mismatches.Take( 20 ) ) );
	}
}
