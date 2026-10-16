using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The station of a kart or water ride is the line of tp.exe's 0x52A1A0 (two tiles on the footprint's rows 1 and 2 across the heading, the entrance row beside them) and the
/// controller's reference is the cell between the tiles (0x529E10, 0x545890); a ride saved before keeps its old layout (docs/TRACKS.md, "The station"; TRACK-005 narrowed).
/// </summary>
[TestClass]
public class TrackStationLayoutTests
{
	/// <summary>The kart and water rides' shipped adjust keys: North (−2, 1), East (1, 4), South (4, 1), West (1, −2).</summary>
	private const string ShippedAdjust = "Bumper.NorthXAdjust -2\nBumper.NorthYAdjust 1\nBumper.EastXAdjust 1\nBumper.EastYAdjust 4\nBumper.SouthXAdjust 4\nBumper.SouthYAdjust 1\nBumper.WestXAdjust 1\nBumper.WestYAdjust -2\n";

	private static ObjectSettings Settings( string text ) => new( new[] { new ObjectSettingsFile( text, "ride.sam" ) } );

	[TestMethod]
	public void TheTilesLieOnTheFootprintsRowsOneAndTwoAcrossTheHeading()
	{
		foreach ( var (width, height) in new[] { (4, 4), (4, 3) } )
		{
			foreach ( var rotation in new[] { 0, 90, 180, 270 } )
			{
				var shape = new ObjectShape( Enumerable.Repeat( new string( '*', width ), height ).ToArray() );
				var anchor = Level.GetCentredAnchor( shape, 30, 40, rotation );
				var station = TrackStation.FromPlacement( anchor.X, anchor.Y, rotation );
				var tiles = station.Tiles.SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) ).ToHashSet();
				var rows = Enumerable.Range( 0, 4 ).SelectMany( u => new[] { 1, 2 }.Select( v => ObjectFootprint.ToGrid( anchor.X, anchor.Y, rotation, u, v ) ) ).ToHashSet();
				CollectionAssert.AreEquivalent( rows.ToList(), tiles.ToList(), $"{width} x {height} at {rotation}: the four footprint columns of rows 1 and 2" );
			}
		}
	}

	[TestMethod]
	public void TheControllerReferenceIsTheJunctionOfTheTwoTilesInEveryRotation()
	{
		var settings = Settings( ShippedAdjust );
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var station = TrackStation.FromPlacement( 20, 20, rotation );
			Assert.AreEqual( station.Reference, Level.TrackStationReference( settings, 20, 20, rotation ), $"rotation {rotation}: the original's anchor + Adjust + fixed offset" );
			// the junction lies on the centre line of the band: the second tile's centre is one cell further along the heading
			var second = TrackStation.Centre( station.Second );
			Assert.AreEqual( (second.X - TrackStation.CellUnits * TrackHeading.Dx( station.Heading ), second.Y - TrackStation.CellUnits * TrackHeading.Dy( station.Heading )), station.Reference );
		}
	}

	[TestMethod]
	public void ARideSavedBeforeKeepsTheOldLayoutAndReference()
	{
		var legacy = TrackStation.FromPlacementLegacy( 10, 10, 0 );
		Assert.IsTrue( legacy.Legacy );
		Assert.AreEqual( ((10, 10), (12, 10)), (legacy.First, legacy.Second) );
		Assert.AreEqual( TrackStation.Centre( legacy.First ), legacy.Reference, "the old reference was the first tile's centre" );
		Assert.IsFalse( TrackStation.FromPlacement( 10, 10, 0 ).Legacy );
	}

	[TestMethod]
	public void ASavedControllerWithoutTheLayoutFieldReadsAsLegacy()
	{
		var current = new BumpControllerSaveData( 3, -5, 0, 50, 0, 0, 0, 0, false, true, new(), new(), new(), new() );
		Assert.AreEqual( 0, current.StationLayout, "an older save has no station layout field" );
		var json = ParkResumeJson.Serialize( current with { StationLayout = 1 } );
		Assert.AreEqual( 1, ParkResumeJson.Deserialize<BumpControllerSaveData>( json ).StationLayout );
		var text = System.Text.Encoding.UTF8.GetString( json );
		var stripped = System.Text.RegularExpressions.Regex.Replace( text, ",?\"StationLayout\":\\s*1", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase );
		Assert.AreNotEqual( text, stripped, "the field was written" );
		Assert.AreEqual( 0, ParkResumeJson.Deserialize<BumpControllerSaveData>( System.Text.Encoding.UTF8.GetBytes( stripped ) ).StationLayout );
	}
}
