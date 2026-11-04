using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The cell of an object's own thing: the anchor plus the type's rotated map offset (tp.exe 0x529F8C, 0x4D9CC0; UI-024).</summary>
[TestClass, DoNotParallelize]
public class ObjectThingCellTests
{
	[DataTestMethod]
	[DataRow( 0, 3, 2, 3, 2 )]
	[DataRow( 90, 3, 2, 2, -3 )]
	[DataRow( 180, 3, 2, -3, -2 )]
	[DataRow( 270, 3, 2, -2, 3 )]
	[DataRow( 360, 3, 2, 3, 2 )]
	[DataRow( 450, 3, 2, 2, -3 )]
	[DataRow( -90, 3, 2, -2, 3 )]
	[DataRow( -180, 3, 2, -3, -2 )]
	[DataRow( -270, 3, 2, 2, -3 )]
	[DataRow( 45, 3, 2, 3, 2 )]
	public void TheOffsetTurnsWithTheAngleAsTheOriginalsRotationRoutineDoes( int angle, int x, int y, int expectedX, int expectedY ) =>
		Assert.AreEqual( (expectedX, expectedY), OriginalObject.RotateOffset( x, y, angle ) );

	[TestMethod]
	public void TheThingCellIsTheAnchorPlusTheTurnedOffset()
	{
		Assert.AreEqual( (10, 20), OriginalObject.ThingCellOf( 0, 0, 10, 20, 90 ), "a type without a map offset keeps its thing on the anchor" );
		Assert.AreEqual( (12, 17), OriginalObject.ThingCellOf( 3, 2, 10, 20, 90 ) );
		Assert.AreEqual( (7, 18), OriginalObject.ThingCellOf( 3, 2, 10, 20, 180 ) );
	}

	/// <summary>Exactly two shipped objects have a map offset, both (0, 1): a feature and a ride (inconclusive without OPENTPW_GAME_PATH).</summary>
	[TestMethod]
	public void ExactlyTwoShippedObjectsHaveAMapOffset()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the map offsets of the shipped objects." );
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			var count = 0;
			var found = new System.Collections.Generic.List<string>();
			foreach ( var theme in ObjectCatalog.Themes )
				foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
				{
					count++;
					var offset = (entry.Settings.GetInt( "Info.MapOffsetX" ), entry.Settings.GetInt( "Info.MapOffsetY" ));
					if ( offset != (0, 0) )
						found.Add( $"{theme}/{entry.ArchiveName} {offset}" );
				}
			Assert.IsTrue( count > 100 );
			found.Sort( System.StringComparer.Ordinal );
			CollectionAssert.AreEqual( new[] { "jungle/5x5rck (0, 1)", "jungle/mammtunn (0, 1)" }, found );
		}
		finally
		{
			FileSystem = previous;
			ObjectCatalog.BonusDataRoot = null;
		}
	}
}
