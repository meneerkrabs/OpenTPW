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
}
