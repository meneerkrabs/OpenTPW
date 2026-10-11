using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class ThemeParkIncSaveImportTests
{
	private static string DataPath()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH for the Theme Park Inc prebuilt-save fixture." );
		return Directory.Exists( Path.Combine( path!, "Data" ) ) ? Path.Combine( path!, "Data" ) : path!;
	}

	private static MapFile LevelMap( string dataPath, string level )
	{
		using var archive = new WadArchive( Path.Combine( dataPath, $"levels/{level}/terrain.wad" ) );
		var member = archive.GetFile( "base.map" );
		Assert.IsNotNull( member );
		return new MapFile( new MemoryStream( member.GetData() ) );
	}

	[TestMethod]
	public void EveryPrebuiltSaveImportsItsObservedParkState()
	{
		var dataPath = DataPath();
		var saves = Directory.GetFiles( Path.Combine( dataPath, "PreBuilt" ), "*.TPWS" );
		Assert.IsTrue( saves.Length > 0 );
		var map = LevelMap( dataPath, "science" );
		foreach ( var path in saves )
		{
			using var reader = new SaveReader( path );
			var payload = reader.ReadFile();
			var layout = SavePayloadLayout.ParseThemeParkInc( payload );
			Assert.AreEqual( 48, layout[SaveBlock.Camera].Length, path );
			Assert.ThrowsException<InvalidDataException>( () => SavePayloadLayout.Parse( payload ), path );
			var worldBlock = layout[SaveBlock.World];
			var grid = SaveCellGrid.ParseThemeParkInc( payload, worldBlock.Offset, worldBlock.End, map.CellCountX, map.CellCountY );
			var differences = from y in Enumerable.Range( 0, map.CellCountY ) from x in Enumerable.Range( 0, map.CellCountX )
				where grid[x, y].Attributes != map.GetCellAt( x, y )
				select (x, y, Save: grid[x, y].Attributes, Map: map.GetCellAt( x, y ));
			Assert.AreEqual( 0, differences.Count(), path );
			Assert.ThrowsException<InvalidDataException>( () => OriginalParkImport.ImportThemeParkInc( payload, LevelMap( dataPath, "arabian" ) ) );
			Assert.ThrowsException<InvalidDataException>( () => OriginalParkImport.ImportThemeParkInc( payload, LevelMap( dataPath, "water" ) ) );
			var park = OriginalParkImport.ImportThemeParkInc( payload, map );
			Assert.AreEqual( 7116, park.Cells.StartOffset, path );
			Assert.AreEqual( 1449038, park.Cells.EndOffset, path );
			Assert.AreEqual( 13, Enumerable.Range( 0, 128 * 128 ).Count( index => park.Cells[index % 128, index / 128].HasExtension ), path );
			Assert.AreEqual( 76, park.PathCells.Count, path );
			CollectionAssert.AreEqual( new[] { 1180, 1160, 1170, 1110, 1403, 1413, 1413, 1414 },
				park.PlacedObjects.Select( item => item.Record.InfoId ).ToArray(), path );
			CollectionAssert.AreEqual( new[] { 1101 }, park.UnresolvedObjects.Select( item => item.InfoId ).ToArray(), path );
			CollectionAssert.AreEqual( new[] { 1601, 103, 104, 1705, 1706, 1709, 3708, 1707, 1710, 1702, 1701, 1700, 1750, 1798 },
				park.FixedItems.Select( item => item.InfoId ).ToArray(), path );
			Assert.AreEqual( 90, park.PlacedObjects.Single( item => item.Record.Index == 27 ).Record.Rotation, path );
			var damaged = (byte[])payload.Clone();
			damaged[park.FixedItems[0].Offset] = 2;
			Assert.ThrowsException<InvalidDataException>( () => OriginalParkImport.ImportThemeParkInc( damaged, map ), path );
		}
	}

	[TestMethod]
	public void ThemeParkIncGridRejectsTruncationAndWrongProfile()
	{
		var payload = new byte[8 + 3 * SaveCell.ThemeParkIncRecordBytes];
		for ( var index = 0; index < 3; index++ )
		{
			var offset = 8 + index * SaveCell.ThemeParkIncRecordBytes;
			payload[offset] = 3;
			payload[offset + 86] = 0xFF;
			payload[offset + 87] = 0xFF;
		}
		var grid = SaveCellGrid.ParseThemeParkInc( payload, 8, payload.Length, 3, 1 );
		Assert.AreEqual( 8, grid.StartOffset );
		Assert.AreEqual( payload.Length, grid.EndOffset );
		Assert.ThrowsException<InvalidDataException>( () => SaveCellGrid.ParseThemeParkInc( payload, 8, payload.Length - 1, 3, 1 ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveCellGrid.Parse( payload, payload.Length, 3, 1 ) );
	}

	[TestMethod]
	public void PrebuiltObjectIdsResolveAgainstScienceCatalog()
	{
		var dataPath = DataPath();
		var previous = FileSystem;
		try
		{
			FileSystem = new BaseFileSystem( dataPath );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			GameEditions.Select( GameEdition.ThemeParkInc );
			ObjectCatalog.BonusDataRoot = null;
			var catalog = ObjectCatalog.Load( "science" );
			using var reader = new SaveReader( Path.Combine( dataPath, "PreBuilt/prebuilt.TPWS" ) );
			var park = OriginalParkImport.ImportThemeParkInc( reader.ReadFile(), LevelMap( dataPath, "science" ) );
			var all = park.PlacedObjects.Select( item => item.Record ).Concat( park.UnresolvedObjects ).Concat( park.FixedItems ).ToArray();
			Assert.AreEqual( 23, all.Length );
			Assert.IsTrue( park.PlacedObjects.All( item => catalog.Find( item.Record.InfoId ) != null ) );
			Assert.IsTrue( park.UnresolvedObjects.All( item => catalog.Find( item.InfoId ) != null ) );
			CollectionAssert.AreEqual( new[] { 1702, 1701, 1700, 1750, 1798 },
				park.FixedItems.Where( item => catalog.Find( item.InfoId ) == null ).Select( item => item.InfoId ).ToArray() );
			CollectionAssert.AreEqual( new[] { 1601, 103, 104 },
				park.FixedItems.Where( item => item.X != 0 || item.Y != 0 ).Select( item => item.InfoId ).ToArray() );
			var incomplete = park.UnresolvedObjects.Single();
			Assert.AreEqual( 1101, incomplete.InfoId );
			Assert.IsFalse( park.Cells[42, 30].IsOccupied );
			Assert.IsFalse( park.Cells[45, 30].IsOccupied );
		}
		finally
		{
			FileSystem = previous;
			GameEditions.Select( GameEdition.ThemeParkWorld );
		}
	}
}
