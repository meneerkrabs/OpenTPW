using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The original's cell types in the guest grid (ECON-050): the world init's types from the MAP byte and the terrain (tp.exe 0x536490 with
/// 0x450420) and the footprint types a placed object writes (0x528a70).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkCellTypingTests
{
	[TestMethod]
	public void TheWorldInitTypesACellFromItsMapByteAndTheTerrain()
	{
		// [BIN:TPW-EXE:0x536490 cell init] no terrain: 2 (MAP 0x02), 0x1e (MAP 0x80) or 7, with flag 0x40; with terrain: 1 (MAP 0x08) or 0
		Assert.AreEqual( ((byte)7, ParkCellFlags.Unowned), ParkCellMap.MapStartCell( 0x00, hasTerrain: false ) );
		Assert.AreEqual( ((byte)2, ParkCellFlags.Unowned), ParkCellMap.MapStartCell( 0x03, hasTerrain: false ), "water, MAP 0x02 before 0x80" );
		Assert.AreEqual( ((byte)30, ParkCellFlags.Unowned | ParkCellFlags.EntranceArea | ParkCellFlags.MapBit04), ParkCellMap.MapStartCell( 0x94, hasTerrain: false ) );
		Assert.AreEqual( ((byte)7, ParkCellFlags.Unowned | ParkCellFlags.EntranceArea), ParkCellMap.MapStartCell( 0x11, hasTerrain: false ) );
		Assert.AreEqual( ((byte)7, ParkCellFlags.Unowned), ParkCellMap.MapStartCell( 0x08, hasTerrain: false ), "the hole branch reads no InitialPath bit" );
		Assert.AreEqual( ((byte)1, ParkCellFlags.NoModify), ParkCellMap.MapStartCell( 0x08, hasTerrain: true ) );
		Assert.AreEqual( ((byte)0, ParkCellFlags.None), ParkCellMap.MapStartCell( 0x01, hasTerrain: true ), "the types 5, 2 and 0x1e written first are overwritten by 0 inside" );
		Assert.AreEqual( ((byte)0, ParkCellFlags.None), ParkCellMap.MapStartCell( 0x82, hasTerrain: true ) );
	}

	[TestMethod]
	public void APlacementStoresTheTrackAndOverlayShapeTypesAsAnObjectCell()
	{
		// [BIN:TPW-EXE:0x528a70 placement commit] 0xb, 0x10 and 0x17 become 4
		Assert.AreEqual( 4, ParkCellMap.FootprintType( ObjectShapeSymbols.TypeUpgrade ) );
		Assert.AreEqual( 4, ParkCellMap.FootprintType( ObjectShapeSymbols.TypeHash ) );
		Assert.AreEqual( 4, ParkCellMap.FootprintType( ObjectShapeSymbols.TypeTrackEnd ) );
		Assert.AreEqual( 4, ParkCellMap.FootprintType( ObjectShapeSymbols.TypeOccupied ) );
		Assert.AreEqual( 9, ParkCellMap.FootprintType( ObjectShapeSymbols.TypeEntrance ) );
		Assert.AreEqual( 10, ParkCellMap.FootprintType( ObjectShapeSymbols.TypeExit ) );
		Assert.AreEqual( 0, ParkCellMap.FootprintType( ObjectShapeSymbols.TypeFree ) );
	}

	[TestMethod]
	public void AFootprintTypesOnlyEmptyCellsAndItsRemovalEmptiesThemAgain()
	{
		var cells = new ParkCellMap( 8, 8 );
		cells.SetType( 2, 1, ParkCellType.Path );
		var shape = new ObjectShape( new[] { "**", "2S" } );
		var footprint = ObjectFootprint.GetCells( shape, 1, 1, 0 ).ToList();
		cells.WriteFootprint( footprint );
		var types = footprint.Select( cell => cells.RawTypeAt( cell.X, cell.Y ) ).OrderBy( type => type ).ToArray();
		CollectionAssert.AreEquivalent( footprint.Select( cell => cell.X == 2 && cell.Y == 1 ? (byte)1 : ParkCellMap.FootprintType( cell.Cell.CellType ) ).ToArray(), types,
			"each empty cell takes its shape type; the path cell keeps its type" );
		cells.ClearFootprint( footprint );
		Assert.IsTrue( footprint.All( cell => cells.RawTypeAt( cell.X, cell.Y ) == (cell.X == 2 && cell.Y == 1 ? 1 : 0) ) );
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

	private void UseOriginalData()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the Jungle Easymode save." );
		Log = new();
		originalFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( OriginalParkImportTests.OriginalDataPath() );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		initialized = true;
	}

	[TestMethod]
	public void TheJungleCellsTakeTheTypesEasymodeSaved()
	{
		UseOriginalData();
		var park = OriginalPark.Load( "jungle" );
		if ( park.Save == null )
			Assert.Inconclusive( "The Jungle Easymode.TPWI save is missing." );
		var cells = ParkCellMap.FromOriginal( park.Map, park.Save, park.Heightfield );
		// the footprints ParkObjects.ImportOriginal places (OriginalObject.Cells), without building the objects' models
		var catalog = ObjectCatalog.Load( "jungle" );
		foreach ( var record in park.Save.PlacedObjects.Select( item => item.Record ).Concat( park.Save.UnresolvedObjects ) )
			if ( catalog.Find( record.InfoId ) is { IsTool: false, IsFixedItem: false } entry )
				cells.WriteFootprint( ObjectFootprint.GetCells( entry.Shape, record.X, record.Y, record.Rotation ) );
		var mismatches = new List<string>();
		var counted = new Dictionary<uint, int>();
		for ( var y = 0; y < cells.Height; y++ )
		{
			for ( var x = 0; x < cells.Width; x++ )
			{
				// [BIN:TPW-EXE:0x4d0b30 cell base serializer] the map cell's mType is the u32 at record +25
				var saved = BinaryPrimitives.ReadUInt32LittleEndian( park.Save.Cells[x, y].Record.Span.Slice( 25, 4 ) );
				counted[saved] = counted.GetValueOrDefault( saved ) + 1;
				// the saved queue cells (type 3) are laid by the level's queue import, not by the world init or the placement
				if ( saved != cells.RawTypeAt( x, y ) && !(saved == 3 && cells.RawTypeAt( x, y ) == 0) )
					mismatches.Add( $"({x}, {y}) saved {saved}, typed {cells.RawTypeAt( x, y )}" );
			}
		}
		Assert.IsTrue( counted.GetValueOrDefault( 7u ) > 8000 && counted.GetValueOrDefault( 4u ) > 0 && counted.GetValueOrDefault( 9u ) > 0, string.Join( ", ", counted ) );
		Assert.AreEqual( 0, mismatches.Count, string.Join( "; ", mismatches.Take( 20 ) ) );
	}
}
