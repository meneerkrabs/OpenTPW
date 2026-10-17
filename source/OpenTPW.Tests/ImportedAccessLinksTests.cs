using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// An imported park's entrance and exit cells carry the link byte their saved records hold (tp.exe's load reads the whole cell record), so the edge test 0x4D8750 lets a called
/// guest's walk step into the stand point (WALK-006).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ImportedAccessLinksTests
{
	[TestMethod]
	public void EasymodesEntranceAndExitCellsCarryTheirSavedLinkByte()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !System.IO.Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the Jungle Easymode save." );
		var previous = FileSystem;
		var previousLog = Log;
		try
		{
			Log = new();
			FileSystem = new BaseFileSystem( OriginalParkImportTests.OriginalDataPath() );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			var park = OriginalPark.Load( "jungle" );
			if ( park.Save == null )
				Assert.Inconclusive( "The Jungle Easymode.TPWI save is missing." );
			var cells = ParkCellMap.FromOriginal( park.Map, park.Save, park.Heightfield );
			var catalog = ObjectCatalog.Load( "jungle" );
			var checkedCells = 0;
			var linkless = new List<string>();
			foreach ( var record in park.Save.PlacedObjects.Select( item => item.Record ).Concat( park.Save.UnresolvedObjects ) )
			{
				if ( catalog.Find( record.InfoId ) is not { IsTool: false, IsFixedItem: false } entry )
					continue;
				var footprint = ObjectFootprint.GetCells( entry.Shape, record.X, record.Y, record.Rotation ).ToList();
				var owner = ParkCellMap.CellWord( record.X, record.Y );
				cells.WriteFootprint( footprint, owner, record.Rotation );
				cells.TakeSavedAccessLinks( footprint, owner, park.Save.Cells );
				foreach ( var (x, y, cell) in footprint.Where( item => item.Cell.CellType is ObjectShapeSymbols.TypeEntrance or ObjectShapeSymbols.TypeExit ) )
				{
					checkedCells++;
					var saved = (byte)park.Save.Cells[x, y].PathConnections;
					Assert.AreEqual( saved, cells.LinksAt( x, y ), $"{entry.DisplayName} ({x}, {y}) link byte" );
					if ( saved == 0 )
						linkless.Add( entry.DisplayName );
				}
			}
			Assert.IsTrue( checkedCells >= 9, $"{checkedCells} entrance and exit cells" );
			Assert.AreEqual( 0, linkless.Count, "every saved entrance and exit cell links to its outside cell: " + string.Join( ", ", linkless ) );
		}
		finally
		{
			FileSystem = previous;
			Log = previousLog;
		}
	}

	[TestMethod]
	public void APlacementLaysTheEntranceLinkByteEasymodeSaved()
	{
		// [BIN:TPW-EXE:0x0052989A entrance path cell] the placement's link writer reaches the type-9 entrance cell along its queue link, so a ride without a queue has the byte too
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !System.IO.Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the Jungle Easymode save." );
		var previous = FileSystem;
		var previousLog = Log;
		try
		{
			Log = new();
			FileSystem = new BaseFileSystem( OriginalParkImportTests.OriginalDataPath() );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			var park = OriginalPark.Load( "jungle" );
			if ( park.Save == null )
				Assert.Inconclusive( "The Jungle Easymode.TPWI save is missing." );
			var catalog = ObjectCatalog.Load( "jungle" );
			var compared = 0;
			foreach ( var record in park.Save.PlacedObjects.Select( item => item.Record ).Concat( park.Save.UnresolvedObjects ) )
			{
				if ( catalog.Find( record.InfoId ) is not { IsTool: false, IsFixedItem: false, HasQueue: false } entry )
					continue;
				var cells = new ParkCellMap( park.Save.Cells.CountX, park.Save.Cells.CountY );
				var owner = ParkCellMap.CellWord( record.X, record.Y );
				var footprint = ObjectFootprint.GetCells( entry.Shape, record.X, record.Y, record.Rotation ).ToList();
				cells.WriteFootprint( footprint, owner, record.Rotation );
				ObjectAccessCells.Lay( cells, ObjectFootprint.GetAccessPoints( entry.Shape, record.X, record.Y, record.Rotation ), owner, entry.HasQueue );
				foreach ( var (x, y, cell) in footprint.Where( item => item.Cell.CellType == ObjectShapeSymbols.TypeEntrance ) )
				{
					compared++;
					Assert.AreEqual( (byte)park.Save.Cells[x, y].PathConnections, cells.LinksAt( x, y ), $"{entry.DisplayName} ({x}, {y}) entrance link byte" );
				}
			}
			Assert.IsTrue( compared >= 7, $"{compared} entrance cells of rides without a queue" );
		}
		finally
		{
			FileSystem = previous;
			Log = previousLog;
		}
	}
}
