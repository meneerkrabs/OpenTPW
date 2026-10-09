using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class OriginalParkImportTests
{
	private static byte[] CreateCellRecord( byte attributes, bool path = false, byte connections = 0, bool occupied = false, bool extension = false )
	{
		var record = new byte[extension ? 94 : 84];
		record[0] = (byte)(extension ? 7 : 3);
		record[SaveCell.PathConnectionsOffset] = connections;
		record[SaveCell.OccupancyOffset] = (byte)(occupied ? 11 : 0);
		record[SaveCell.PathFlagsOffset] = (byte)(path ? 1 : 0);
		record[SaveCell.AttributeOffset] = attributes;
		record[82] = 0xFF;
		record[83] = 0xFF;
		if ( extension )
			record[90] = 5;
		return record;
	}

	/// <summary>Opaque header, then X-fastest records for game cells (x, y) with attributes from <paramref name="map"/>.</summary>
	private static byte[] CreateGrid( MapFile map, Func<int, int, byte[]>? cell = null, int headerBytes = 13 )
	{
		using var output = new MemoryStream();
		output.Write( Enumerable.Repeat( (byte)0xCD, headerBytes ).ToArray() );
		for ( var y = 0; y < map.CellCountY; y++ )
			for ( var x = 0; x < map.CellCountX; x++ )
				output.Write( cell?.Invoke( x, y ) ?? CreateCellRecord( map.GetCellAt( x, y ) ) );
		output.Write( new byte[] { 0x2A, 0, 0, 0 } );
		return output.ToArray();
	}

	private static byte[] CreateObjectRecord( int infoId, int x, int y, int width, int height, int kind, int index, int rotation )
	{
		var record = new byte[60];
		record[0] = 1;
		var values = new[] { infoId, x, y, width, height, kind, index };
		for ( var field = 0; field < values.Length; field++ )
			BinaryPrimitives.WriteInt32LittleEndian( record.AsSpan( 1 + field * 4 ), values[field] );
		BinaryPrimitives.WriteUInt16LittleEndian( record.AsSpan( 39 ), (ushort)rotation );
		return record;
	}

	/// <summary>Grid prefix followed by every known section marker; SYSG carries <paramref name="sysg"/>.</summary>
	private static byte[] CreatePayload( byte[] prefix, byte[] sysg )
	{
		using var output = new MemoryStream();
		output.Write( prefix );
		foreach ( var tag in SavePayloadLayout.ObservedEasymodeOrder )
		{
			output.Write( Encoding.ASCII.GetBytes( tag ) );
			output.Write( tag == "SYSG" ? sysg : new byte[8] );
		}
		return output.ToArray();
	}

	private static MapFile CreateMap( int countX, int countY, Func<int, int, byte> value )
	{
		var cells = new byte[countX * countY];
		for ( var x = 0; x < countX; x++ )
			for ( var y = 0; y < countY; y++ )
				cells[x * countY + y] = value( x, y );
		var data = new byte[36 + 8 + 28 + cells.Length + 8];
		"TP2M"u8.CopyTo( data );
		"Theme Park 2 Attribute Map File"u8.CopyTo( data.AsSpan( 4 ) );
		"MAP "u8.CopyTo( data.AsSpan( 36 ) );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 40 ), (uint)(28 + cells.Length) );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 44 ), (uint)countY );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 48 ), (uint)countX );
		cells.CopyTo( data, 72 );
		"END "u8.CopyTo( data.AsSpan( 72 + cells.Length ) );
		return new MapFile( new MemoryStream( data ) );
	}

	[TestMethod]
	public void MapGameCoordinatesUseFileRowsForXAndMaskUnverifiedBits()
	{
		var map = CreateMap( 3, 2, ( x, y ) => (byte)(x * 10 + y) );
		Assert.AreEqual( 3, map.CellCountX );
		Assert.AreEqual( 2, map.CellCountY );
		Assert.AreEqual( (byte)21, map.GetCellAt( 2, 1 ) );
		Assert.AreEqual( map.GetCell( 1, 2 ), map.GetCellAt( 2, 1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => map.GetCellAt( 3, 0 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => map.GetCellAt( 0, 2 ) );
		var flags = CreateMap( 1, 1, ( _, _ ) => 0xFF );
		Assert.AreEqual( MapCellFlags.AllVerified, flags.GetFlagsAt( 0, 0 ) );
		Assert.AreEqual( MapCellFlags.EntranceArea | MapCellFlags.FixedWalkway, CreateMap( 1, 1, ( _, _ ) => 148 ).GetFlagsAt( 0, 0 ) );
		Assert.AreEqual( MapCellFlags.Blocked | MapCellFlags.Water, CreateMap( 1, 1, ( _, _ ) => 3 ).GetFlagsAt( 0, 0 ) );
	}

	[TestMethod]
	public void ParsesXFastestCellGridWithExtensionsAndInterpretedFields()
	{
		var map = CreateMap( 3, 2, ( x, y ) => (byte)(x == 2 ? 1 : 0) );
		var payload = CreateGrid( map, ( x, y ) => (x, y) switch
		{
			(1, 0) => CreateCellRecord( 0, path: true, connections: 0x44 | 0x02 ),
			(0, 1) => CreateCellRecord( 0, occupied: true, extension: true ),
			_ => CreateCellRecord( map.GetCellAt( x, y ) ),
		} );
		var grid = SaveCellGrid.Parse( payload, payload.Length, 3, 2 );
		Assert.AreEqual( 13, grid.StartOffset );
		Assert.AreEqual( 13 + 5 * 84 + 94, grid.EndOffset );
		Assert.IsTrue( grid[1, 0].IsPath );
		Assert.AreEqual( SavePathConnections.PositiveX | SavePathConnections.NegativeX, grid[1, 0].PathConnections & SavePathConnections.Cardinal );
		Assert.IsTrue( grid[0, 1].IsOccupied );
		Assert.IsTrue( grid[0, 1].HasExtension );
		Assert.AreEqual( 94, grid[0, 1].Record.Length );
		Assert.AreEqual( (byte)1, grid[2, 1].Attributes );
		Assert.IsFalse( grid[2, 1].IsPath || grid[2, 1].IsOccupied || grid[2, 1].HasExtension );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => grid[3, 0] );
	}

	[TestMethod]
	public void RejectsMissingTruncatedAndAmbiguousCellGrids()
	{
		var map = CreateMap( 2, 2, ( _, _ ) => 0 );
		var payload = CreateGrid( map );
		Assert.ThrowsException<InvalidDataException>( () => SaveCellGrid.Parse( payload, payload.Length - 10, 2, 2 ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveCellGrid.Parse( payload, payload.Length, 3, 2 ) );
		var broken = (byte[])payload.Clone();
		broken[13 + 84 + 83] = 0;
		Assert.ThrowsException<InvalidDataException>( () => SaveCellGrid.Parse( broken, broken.Length, 2, 2 ) );
		var badType = (byte[])payload.Clone();
		badType[13 + 2 * 84] = 5;
		Assert.ThrowsException<InvalidDataException>( () => SaveCellGrid.Parse( badType, badType.Length, 2, 2 ) );
		// Five valid records: the grid could start at the first or second record.
		var longer = CreateGrid( CreateMap( 5, 1, ( _, _ ) => 0 ) );
		var exception = Assert.ThrowsException<InvalidDataException>( () => SaveCellGrid.Parse( longer, longer.Length, 2, 2 ) );
		StringAssert.Contains( exception.Message, "ambiguous" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => SaveCellGrid.Parse( payload, payload.Length + 1, 2, 2 ) );
	}

	[TestMethod]
	public void ScansObjectRecordsAndAppliesOnlyVerifiedFootprintRules()
	{
		var map = CreateMap( 8, 8, ( _, _ ) => 0 );
		var sysg = new[]
		{
			new byte[] { 1, 2, 3 },
			CreateObjectRecord( 1601, 1, 1, 6, 3, SaveObject.FixedItemKind, 1, 0 ),
			CreateObjectRecord( 1100, 2, 2, 3, 4, SaveObject.PlacedObjectKind, 3, 0 ),
			CreateObjectRecord( 1411, 5, 6, 2, 2, SaveObject.PlacedObjectKind, 4, 90 ),
			CreateObjectRecord( 1402, 7, 7, 1, 1, SaveObject.PlacedObjectKind, 5, 270 ),
			CreateObjectRecord( 1999, 1, 1, 3, 2, SaveObject.PlacedObjectKind, 6, 90 ),
			CreateObjectRecord( 1999, 1, 1, 1, 1, 816, 7, 0 ),
			CreateObjectRecord( 1999, 8, 1, 1, 1, SaveObject.PlacedObjectKind, 8, 0 ),
			CreateObjectRecord( 1999, 1, 1, 1, 1, SaveObject.PlacedObjectKind, 2, 0 ),
		}.SelectMany( bytes => bytes ).ToArray();
		var payload = CreatePayload( CreateGrid( map ), sysg );
		var objects = SaveObjectList.Parse( payload, SavePayloadLayout.Parse( payload ), 8, 8 );
		CollectionAssert.AreEqual( new[] { 1, 3, 4, 5, 6 }, objects.Select( item => item.Index ).ToArray() );
		Assert.IsTrue( objects[0].IsFixedItem );
		Assert.AreEqual( new SaveObject( objects[1].Offset, 1100, 2, 2, 3, 4, 815, 3, 0 ), objects[1] );
		AssertFootprint( objects[1], 2, 2, 4, 5 );
		AssertFootprint( objects[2], 5, 5, 6, 6 );
		AssertFootprint( objects[3], 7, 7, 7, 7 );
		Assert.IsFalse( objects[4].TryGetFootprint( out _, out _, out _, out _ ) );
	}

	private static void AssertFootprint( SaveObject item, int minX, int minY, int maxX, int maxY )
	{
		Assert.IsTrue( item.TryGetFootprint( out var actualMinX, out var actualMinY, out var actualMaxX, out var actualMaxY ) );
		Assert.AreEqual( (minX, minY, maxX, maxY), (actualMinX, actualMinY, actualMaxX, actualMaxY) );
	}

	[TestMethod]
	public void ImportsPathsAndCrossCheckedObjects()
	{
		var map = CreateMap( 6, 6, ( x, y ) => (byte)(x == 0 ? 1 : x == 1 && y == 1 ? 8 : 0) );
		var occupied = new HashSet<(int, int)> { (3, 3), (4, 3), (3, 2), (4, 2), (5, 5) };
		var prefix = CreateGrid( map, ( x, y ) => CreateCellRecord( map.GetCellAt( x, y ), path: (x, y) is (1, 1) or (2, 1), occupied: occupied.Contains( (x, y) ) ) );
		var sysg = CreateObjectRecord( 1411, 3, 3, 2, 2, SaveObject.PlacedObjectKind, 1, 90 )
			.Concat( CreateObjectRecord( 1603, 4, 4, 1, 1, SaveObject.FixedItemKind, 2, 0 ) )
			.Concat( CreateObjectRecord( 1100, 5, 5, 1, 2, SaveObject.PlacedObjectKind, 3, 90 ) ).ToArray();
		var park = OriginalParkImport.Import( CreatePayload( prefix, sysg ), map );
		CollectionAssert.AreEqual( new[] { (1, 1), (2, 1) }, park.PathCells.ToArray() );
		Assert.AreEqual( 1, park.PlacedObjects.Count );
		Assert.AreEqual( (3, 2, 4, 3), (park.PlacedObjects[0].MinX, park.PlacedObjects[0].MinY, park.PlacedObjects[0].MaxX, park.PlacedObjects[0].MaxY) );
		Assert.IsTrue( park.IsOccupiedByObject( 4, 2 ) );
		Assert.IsFalse( park.IsOccupiedByObject( 5, 5 ) );
		Assert.AreEqual( 1603, park.FixedItems.Single().InfoId );
		Assert.AreEqual( 1100, park.UnresolvedObjects.Single().InfoId );
	}

	[TestMethod]
	public void RejectsSaveForAnotherMapOrUnoccupiedObjectFootprint()
	{
		var map = CreateMap( 4, 4, ( x, _ ) => (byte)(x == 0 ? 1 : 0) );
		var other = CreateMap( 4, 4, ( x, _ ) => (byte)(x == 3 ? 1 : 0) );
		var payload = CreatePayload( CreateGrid( map ), Array.Empty<byte>() );
		Assert.AreEqual( 0, OriginalParkImport.Import( payload, map ).PathCells.Count );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => OriginalParkImport.Import( payload, other ) ).Message, "another map" );
		var unoccupied = CreatePayload( CreateGrid( map ), CreateObjectRecord( 1100, 1, 1, 2, 2, SaveObject.PlacedObjectKind, 1, 0 ) );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => OriginalParkImport.Import( unoccupied, map ) ).Message, "not occupied" );
	}

	[TestMethod]
	public void OriginalJungleEasyModeImportsProvenParkState()
	{
		var payload = ReadEasymodePayload();
		var map = new MapFile( new MemoryStream( ReadArchiveMember( "levels/jungle/terrain.wad", "base.map" ) ) );
		var park = OriginalParkImport.Import( payload, map );
		Assert.AreEqual( 6765, park.Cells.StartOffset );
		Assert.AreEqual( 1385521, park.Cells.EndOffset );
		Assert.AreEqual( 78, park.PathCells.Count );
		var initialPath = Enumerable.Range( 0, 128 * 128 ).Select( index => (X: index / 128, Y: index % 128) )
			.Where( cell => map.GetFlagsAt( cell.X, cell.Y ).HasFlag( MapCellFlags.InitialPath ) ).ToArray();
		CollectionAssert.AreEquivalent( Enumerable.Range( 17, 5 ).SelectMany( y => new[] { (47, y), (48, y) } ).ToArray(), initialPath );
		Assert.IsTrue( initialPath.All( cell => park.Cells[cell.X, cell.Y].IsPath ) );
		Assert.IsTrue( park.PathCells.All( cell => (map.GetFlagsAt( cell.X, cell.Y ) & ~MapCellFlags.InitialPath) == MapCellFlags.None ) );
		Assert.AreEqual( 250, Enumerable.Range( 0, 128 * 128 ).Count( index => park.Cells[index % 128, index / 128].HasExtension ) );

		// Every cardinal path neighbour has its connection bit; extra bits lead to object/queue cells.
		var isPath = park.PathCells.ToHashSet();
		var directions = new (SavePathConnections Bit, int X, int Y)[]
		{
			(SavePathConnections.NegativeY, 0, -1), (SavePathConnections.PositiveX, 1, 0),
			(SavePathConnections.PositiveY, 0, 1), (SavePathConnections.NegativeX, -1, 0),
		};
		var extraBits = 0;
		foreach ( var (x, y) in park.PathCells )
		{
			foreach ( var (bit, dx, dy) in directions )
			{
				var neighbourIsPath = isPath.Contains( (x + dx, y + dy) );
				var hasBit = park.Cells[x, y].PathConnections.HasFlag( bit );
				Assert.IsTrue( hasBit || !neighbourIsPath, $"({x}, {y}) {bit}" );
				if ( hasBit && !neighbourIsPath )
				{
					extraBits++;
					Assert.IsTrue( park.Cells[x + dx, y + dy].IsOccupied || park.Cells[x + dx, y + dy].HasExtension, $"({x}, {y}) {bit}" );
				}
			}
		}
		Assert.AreEqual( 9, extraBits );

		var expected = new (int Id, int X, int Y, int W, int H, int Rotation, int MinX, int MinY, int MaxX, int MaxY)[]
		{
			(1100, 51, 23, 3, 4, 0, 51, 23, 53, 26), (1303, 51, 30, 3, 3, 0, 51, 30, 53, 32), (1203, 43, 30, 2, 2, 0, 43, 30, 44, 31),
			(1406, 44, 29, 1, 1, 0, 44, 29, 44, 29), (1413, 55, 29, 1, 1, 0, 55, 29, 55, 29), (1413, 40, 29, 1, 1, 0, 40, 29, 40, 29),
			(1411, 58, 16, 2, 2, 90, 58, 15, 59, 16), (1402, 55, 17, 1, 1, 270, 55, 17, 55, 17), (1402, 55, 16, 1, 1, 270, 55, 16, 55, 16),
			(1402, 55, 15, 1, 1, 270, 55, 15, 55, 15), (1403, 57, 19, 3, 3, 90, 57, 17, 59, 19),
		};
		CollectionAssert.AreEqual( expected, park.PlacedObjects.Select( item => (item.Record.InfoId, item.Record.X, item.Record.Y, item.Record.Width,
			item.Record.Height, item.Record.Rotation, item.MinX, item.MinY, item.MaxX, item.MaxY) ).ToArray() );
		CollectionAssert.AreEqual( new[] { (1601, 45, 16, 1), (1603, 48, 17, 2), (1600, 48, 17, 15) },
			park.FixedItems.Select( item => (item.InfoId, item.X, item.Y, item.Index) ).ToArray() );
		Assert.AreEqual( 0, park.UnresolvedObjects.Count );
		Assert.IsTrue( park.PlacedObjects.All( item => Cells( item ).All( cell => map.GetFlagsAt( cell.X, cell.Y ) == MapCellFlags.None ) ) );
		Assert.IsTrue( park.PlacedObjects.All( item => Cells( item ).All( cell => !isPath.Contains( cell ) ) ) );
	}

	[TestMethod]
	public void OriginalJungleEasyModeDoesNotMatchTheAlternativeTerrainMap()
	{
		var payload = ReadEasymodePayload();
		var terrainMap = new MapFile( new MemoryStream( ReadArchiveMember( "levels/jungle/terrain.wad", "terrain.map" ) ) );
		Assert.ThrowsException<InvalidDataException>( () => OriginalParkImport.Import( payload, terrainMap ) );
	}

	private static IEnumerable<(int X, int Y)> Cells( ImportedParkObject item )
	{
		for ( var x = item.MinX; x <= item.MaxX; x++ )
			for ( var y = item.MinY; y <= item.MaxY; y++ )
				yield return (x, y);
	}

	internal static string OriginalDataPath()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Jungle Easymode/terrain fixtures." );
		return Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
	}

	internal static byte[] ReadArchiveMember( string relativeArchive, string member )
	{
		var archivePath = Path.Combine( OriginalDataPath(), relativeArchive );
		if ( !File.Exists( archivePath ) )
			Assert.Inconclusive( $"Original archive is missing: {relativeArchive}" );
		using var archive = new WadArchive( archivePath );
		var file = archive.GetFile( member );
		if ( file == null )
			Assert.Inconclusive( $"Original archive member is missing: {relativeArchive}/{member}" );
		return file!.GetData();
	}

	private static byte[] ReadEasymodePayload()
	{
		var path = Path.Combine( OriginalDataPath(), "levels", "jungle", "Easymode.TPWI" );
		if ( !File.Exists( path ) )
			Assert.Inconclusive( "The original Jungle Easymode.TPWI fixture is missing." );
		using var reader = new SaveReader( path );
		return reader.ReadFile();
	}
}
