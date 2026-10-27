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
}
