using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The track tool's globals 0x7639E0/E4 and 0x7639E8/EC keep what the last select or tool start wrote, whichever ride that was (D6, ORACLE.md X37): the tile the demolition's record clear spares is the
/// one they name, not the demolished ride's own exit tile.
/// </summary>
[TestClass]
public class TrackToolGlobalsTests
{
	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 128;
		public int Height => 128;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private static (TrackBuilder Builder, ParkCellMap Map) Rig()
	{
		var map = new ParkCellMap( 128, 128 );
		return (new TrackBuilder( map, TrackFamily.Kart, () => null, new FlatGrid() ), map);
	}

	// a station whose exit tile is (exitX, 40): the second station tile two cells before it
	private static TrackStation StationWithExit( int exitX ) => new( 0, TrackHeading.PosX, (exitX - 4, 40), (exitX - 2, 40) );

	private static void LayTile( TrackBuilder builder, ParkCellMap map, TrackStation station, int x )
	{
		map.Tracks.Set( x, 40, new TrackCell { Type = TrackCellTypes.KartOrigin, Mask = 0x44, Link = 0x04, Overlap = 0, Owner = builder.OwnerOf( station ), Parent = -1 } );
		foreach ( var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1) } )
			map.Tracks.Set( x + dx, 40 + dy, new TrackCell { Type = TrackCellTypes.Extension, Parent = map.Tracks.IndexOf( x, 40 ), Owner = builder.OwnerOf( station ) } );
	}

	[TestMethod]
	public void TheTileTheGlobalsNameIsSparedWhateverRideDemolishesIt()
	{
		var (builder, map) = Rig();
		var other = StationWithExit( 60 );
		var own = StationWithExit( 90 );
		LayTile( builder, map, own, 30 );
		var shared = new TrackToolGlobals( (30, 40), (0, 0) );
		var editor = new TrackEditor( builder, own, null, shared );
		Assert.IsFalse( editor.ClearUnderDemolition( (30, 40) ), "the tile the globals name as the exit survives the clear of its ride's demolition" );
		Assert.IsFalse( map.Tracks.Get( 30, 40 ).IsEmpty );
		// the last select named another ride's tiles: the demolished ride's own tile has no exemption
		shared.Exit = other.Exit;
		shared.Entry = other.Entry;
		Assert.IsTrue( editor.ClearUnderDemolition( (30, 40) ), "a tile the globals do not name is cleared" );
		Assert.IsTrue( map.Tracks.Get( 30, 40 ).IsEmpty );
	}

	[TestMethod]
	public void AnEditorWithoutSharedGlobalsSparesItsOwnExitAndEntryTiles()
	{
		var (builder, map) = Rig();
		var own = StationWithExit( 90 );
		LayTile( builder, map, own, own.Exit.X );
		var editor = new TrackEditor( builder, own );
		Assert.AreEqual( own.Exit, editor.ToolExit );
		Assert.AreEqual( own.Entry, editor.ToolEntry );
		Assert.IsFalse( editor.ClearUnderDemolition( own.Exit ), "a headless editor keeps the ride's own exit tile" );
	}

	[TestMethod]
	public void TheToolStartWritesTheGlobalsAndTheHoverWritesTheAnchor()
	{
		var (builder, _) = Rig();
		var station = StationWithExit( 90 );
		var shared = new TrackToolGlobals();
		var editor = new TrackEditor( builder, station, null, shared );
		Assert.AreEqual( ((0, 0), (0, 0)), (shared.Exit, shared.Entry), "a game starts with the zeroed globals; building an editor writes nothing" );
		editor.UseLineToolExit();
		Assert.AreEqual( (station.Exit, station.Entry), (shared.Exit, shared.Entry) );
		editor.UseEditToolExit();
		Assert.AreEqual( (station.Anchor, station.Entry), (shared.Exit, shared.Entry), "the edit tool's hover names the raw anchor as the exit and the mapped entry" );
	}
}
