using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The thing words +0x36 (entry cell) and +0x38 (exit cell) of every placed object of the Jungle Easymode save are the cells <see cref="Level.FirstEntranceCellOf"/> and <see cref="Level.ExitAccessCellOf"/> give
/// (tp.exe constructor 0x004DB090 at 0x004DB33C). Inconclusive without OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SavedAccessCellTests
{
	[TestMethod]
	public void EverySavedEntryAndExitCellOfTheJungleSaveIsTheShapesFirstEntranceAndLastExit()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
		{
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH." );
			return;
		}
		try
		{
			var park = OriginalPark.Load( "jungle" );
			var save = park.Save ?? throw new AssertFailedException( "no shipped save" );
			var catalog = ObjectCatalog.Load( "jungle" );
			static int Id( (int X, int Y) cell ) => cell.Y * 128 + cell.X + 1;
			var expected = new List<string>();
			foreach ( var item in save.PlacedObjects )
			{
				var record = item.Record;
				if ( catalog.Find( record.InfoId ) is not { IsTool: false } entry )
					continue;
				var points = ObjectFootprint.GetAccessPoints( entry.Shape, record.X, record.Y, record.Rotation ).ToArray();
				var entrance = Level.FirstEntranceCellOf( points ) ?? (record.X, record.Y);
				var exit = Level.ExitAccessCellOf( points ) ?? entrance;
				expected.Add( $"{record.InfoId}:{Id( entrance )}:{Id( exit )}" );
			}
			var saved = save.Attractions.Select( a => $"{a.InfoId}:{a.EntryPos}:{a.ExitPos}" ).ToList();
			Assert.IsTrue( expected.Count >= 8, "the save places its rides" );
			CollectionAssert.IsSubsetOf( expected, saved, "every placed object's +0x36 and +0x38 equal the shape's cells" );
		}
		finally { FileSystem = previous; }
	}

	/// <summary>
	/// tp.exe 0x004DE040 reads the link byte of the cell +0x36, the shape's first entrance cell (0x004DB33C): <see cref="Level.ResolveQueueEntrance(System.Collections.Generic.IEnumerable{ObjectAccessPoint}, ParkCellMap, ushort)"/>
	/// answers that cell for every queued shape of the four catalogs, whatever the rotation, and a shape with several entrances is not answered by its last one.
	/// </summary>
	[TestMethod]
	public void TheQueueEntranceOfEveryQueuedShapeIsItsFirstEntranceCell()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
		{
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH." );
			return;
		}
		try
		{
			var queued = 0;
			var several = 0;
			foreach ( var theme in new[] { "fantasy", "hallow", "jungle", "space" } )
				foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( x => x.HasQueue && !x.IsTool ) )
					foreach ( var rotation in new[] { 0, 90, 180, 270 } )
					{
						var points = ObjectFootprint.GetAccessPoints( entry.Shape, 40, 40, rotation ).ToArray();
						var entrances = points.Where( p => p.Kind == ObjectCellKind.Entrance ).ToArray();
						if ( entrances.Length == 0 ) continue;
						var cells = new ParkCellMap( 128, 128 );
						foreach ( var entrance in entrances )
						{
							cells.SetType( entrance.X, entrance.Y, ParkCellTypes.Entrance );
							cells.SetOwner( entrance.X, entrance.Y, 5 );
						}
						var resolved = Level.ResolveQueueEntrance( points, cells, 5 );
						Assert.AreEqual( (entrances[0].X, entrances[0].Y), resolved, $"{theme} {entry.InfoId} rotation {rotation}" );
						Assert.AreEqual( Level.FirstEntranceCellOf( points ), resolved, $"{theme} {entry.InfoId} rotation {rotation}: the access cell" );
						if ( rotation == 0 )
						{
							queued++;
							if ( entrances.Length > 1 ) several++;
						}
					}
			Console.WriteLine( $"QUEUE_FRONT_CORPUS queued shapes={queued} with several entrances={several}" );
			Assert.IsTrue( queued > 0, "the catalogs list queued shapes with entrances" );
		}
		finally { FileSystem = previous; }
	}

	/// <summary>
	/// tp.exe scans a shape for its first entrance column by column (0x00413410: U outer, V inner) and for the placement's last entrance or exit row by row (0x00528A70: V outer, U inner). The two orders name different
	/// cells only for a shape with several entrances or exits; none of the four catalogs has one, so the shipped parks cannot tell them apart. This pins that count, and the orders on a synthetic shape.
	/// </summary>
	[TestMethod]
	public void NoShippedShapeNamesTwoEntrancesOrExitsAndTheScanOrdersFollowTheOriginal()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
		{
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH." );
			return;
		}
		try
		{
			var multiple = 0;
			foreach ( var theme in new[] { "fantasy", "hallow", "jungle", "space" } )
				foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( x => !x.IsTool ) )
					if ( entry.Shape.Cells.Count( c => c.Kind == ObjectCellKind.Entrance ) > 1 || entry.Shape.Cells.Count( c => c.Kind == ObjectCellKind.Exit ) > 1 )
						multiple++;
			Assert.AreEqual( 0, multiple, "no shape has two entrances or two exits" );
		}
		finally { FileSystem = previous; }
		// two entrances apart in both axes: column-major picks the lowest U (then V), row-major keeps the highest V (then U)
		var points = new[]
		{
			new ObjectAccessPoint( ObjectCellKind.Entrance, 5, 1, 5, 0, U: 3, V: 0 ),
			new ObjectAccessPoint( ObjectCellKind.Entrance, 1, 4, 1, 5, U: 0, V: 3 ),
		};
		Assert.AreEqual( (1, 4), Level.FirstEntranceCellOf( points ), "0x413410: the lowest column first" );
		Assert.AreEqual( (1, 4), (ObjectAccessCells.Find( points ).Entrance!.Value.X, ObjectAccessCells.Find( points ).Entrance!.Value.Y), "0x528A70: the highest row last" );
		var apart = new[]
		{
			new ObjectAccessPoint( ObjectCellKind.Entrance, 5, 1, 5, 0, U: 3, V: 3 ),
			new ObjectAccessPoint( ObjectCellKind.Entrance, 1, 4, 1, 5, U: 0, V: 0 ),
		};
		Assert.AreEqual( (1, 4), Level.FirstEntranceCellOf( apart ), "the lowest U" );
		Assert.AreEqual( (5, 1), (ObjectAccessCells.Find( apart ).Entrance!.Value.X, ObjectAccessCells.Find( apart ).Entrance!.Value.Y), "the highest V" );
	}

	[TestMethod]
	public void TheExitCellIsTheFirstExitInColumnOrderElseTheFirstEntrance()
	{
		// [BIN:TPW-EXE:0x004DB36B exit cell source] +0x38 comes from the def's +0x4A0, which 0x00413410 fills with the first type 10 cell (U outer, V inner), else the first entrance (0x00413582)
		var exits = new[]
		{
			new ObjectAccessPoint( ObjectCellKind.Exit, 5, 1, 5, 0, U: 3, V: 0 ),
			new ObjectAccessPoint( ObjectCellKind.Exit, 1, 4, 1, 5, U: 0, V: 3 ),
			new ObjectAccessPoint( ObjectCellKind.Entrance, 9, 9, 9, 8, U: 5, V: 5 ),
		};
		Assert.AreEqual( (1, 4), Level.ExitAccessCellOf( exits ), "the lowest U wins whatever the row" );
		var sameColumn = new[]
		{
			new ObjectAccessPoint( ObjectCellKind.Exit, 2, 7, 2, 8, U: 1, V: 4 ),
			new ObjectAccessPoint( ObjectCellKind.Exit, 2, 3, 2, 2, U: 1, V: 0 ),
		};
		Assert.AreEqual( (2, 3), Level.ExitAccessCellOf( sameColumn ), "then the lowest V" );
		var entrances = new[]
		{
			new ObjectAccessPoint( ObjectCellKind.Entrance, 6, 6, 6, 5, U: 4, V: 0 ),
			new ObjectAccessPoint( ObjectCellKind.Entrance, 1, 2, 1, 1, U: 0, V: 2 ),
		};
		Assert.AreEqual( (1, 2), Level.ExitAccessCellOf( entrances ), "without an exit: the first entrance" );
		Assert.IsNull( Level.ExitAccessCellOf( Array.Empty<ObjectAccessPoint>() ) );
	}
}
