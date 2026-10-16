using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The bumper arena centre (docs/TRACKS.md, "Arena centre"; TRACK-030 retired): the ride placement 0x529E10 puts the controller at the build click's anchor
/// plus the type's <c>Bumper.*Adjust</c> offset and a fixed offset for the rotation, × 0xC00, and the arena builder adds 0x600.
/// </summary>
[TestClass]
public class BumperArenaTests
{
	private const string ShippedAdjust = "Bumper.NorthXAdjust -2\nBumper.NorthYAdjust 1\nBumper.EastXAdjust 1\nBumper.EastYAdjust 3\nBumper.SouthXAdjust 3\nBumper.SouthYAdjust 0\nBumper.WestXAdjust 0\nBumper.WestYAdjust -2\n";

	private static ObjectSettings Settings( string text ) => new( new[] { new ObjectSettingsFile( text, "bumper.sam" ) } );

	private static (int X, int Y) CellMiddle( int x, int y ) => (x * TrackStation.CellUnits + TrackStation.CellUnits / 2, y * TrackStation.CellUnits + TrackStation.CellUnits / 2);

	[TestMethod]
	public void TheArenaIsTheAnchorPlusAdjustAndTheFixedOffset()
	{
		var none = Settings( "Info.Id 1\n" );
		Assert.AreEqual( CellMiddle( 14, 11 ), Level.BumperArenaCentre( none, 10, 10, 0 ) );
		Assert.AreEqual( CellMiddle( 11, 5 ), Level.BumperArenaCentre( none, 10, 10, 90 ) );
		Assert.AreEqual( CellMiddle( 5, 8 ), Level.BumperArenaCentre( none, 10, 10, 180 ) );
		Assert.AreEqual( CellMiddle( 8, 14 ), Level.BumperArenaCentre( none, 10, 10, 270 ) );
		Assert.AreEqual( CellMiddle( 10 - 2 + 4, 10 + 1 + 1 ), Level.BumperArenaCentre( Settings( ShippedAdjust ), 10, 10, 0 ) );
		Assert.ThrowsException<System.ArgumentOutOfRangeException>( () => Level.BumperArenaCentre( none, 10, 10, 45 ) );
	}

	[TestMethod]
	public void TheShippedAdjustCentresTheArenaOnTheFootprint()
	{
		// a 5 × 5 ride: the original offsets land on local cell (2, 2), the middle of the footprint, in every rotation
		var shape = new ObjectShape( Enumerable.Repeat( "*****", 5 ).ToArray() );
		var settings = Settings( ShippedAdjust );
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var anchor = Level.GetCentredAnchor( shape, 30, 40, rotation );
			var middle = ObjectFootprint.ToGrid( anchor.X, anchor.Y, rotation, 2, 2 );
			Assert.AreEqual( (30, 40), middle, $"the click is the middle cell at {rotation}" );
			Assert.AreEqual( CellMiddle( middle.X, middle.Y ), Level.BumperArenaCentre( settings, anchor.X, anchor.Y, rotation ), $"rotation {rotation}" );
		}
	}

	[DataTestMethod]
	[DataRow( "jungle", "/levels/jungle/rides/bumper" )]
	[DataRow( "hallow", "/levels/hallow/rides/bumper" )]
	[DataRow( "space", "/levels/space/rides/bumper" )]
	[DataRow( "fantasy", "/levels/fantasy/rides/bbugs" )]
	public void ShippedBumperArenasSitOnTheMiddleOfTheirFootprint( string theme, string archive )
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original data." );
		try
		{
			var entry = ObjectCatalog.Load( theme ).Entries.Single( item => item.ArchivePath == archive );
			Assert.AreEqual( (5, 5, 25), (entry.Shape.Width, entry.Shape.Height, entry.Shape.OccupiedCells.Count()), "the shipped bumper rides are full 5 × 5 squares" );
			foreach ( var rotation in new[] { 0, 90, 180, 270 } )
			{
				var cells = ObjectFootprint.GetCells( entry.Shape, 20, 20, rotation ).ToArray();
				var middle = ((cells.Min( cell => cell.X ) + cells.Max( cell => cell.X ) + 1) * TrackStation.CellUnits / 2, (cells.Min( cell => cell.Y ) + cells.Max( cell => cell.Y ) + 1) * TrackStation.CellUnits / 2);
				Assert.AreEqual( middle, Level.BumperArenaCentre( entry.Settings, 20, 20, rotation ), $"{theme} at {rotation}" );
			}
		}
		finally { FileSystem = previous; }
	}
}
