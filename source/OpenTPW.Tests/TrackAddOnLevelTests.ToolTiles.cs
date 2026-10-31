using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The tiles a demolishing command carries (ToolTiles, ORACLE.md 7.30) against a real track ride: an add-on stands on the ride's exit tile, and selling it spares that tile only when the
/// tiles the command carries name it (the record clear 0x53B280 spares the tiles of the track tool's globals, whichever ride they belong to).
/// </summary>
public partial class TrackAddOnLevelTests
{
	/// <summary>The world with a jump placed on the ride's exit tile (14, 10); the ride's exit and entry tiles, and the cell the jump stands on.</summary>
	private (Level Level, OriginalObject Ride, OriginalObject AddOn, ToolTiles Own, (int X, int Y) Cell) JumpOnTheExitTile()
	{
		var (level, ride, jump) = World();
		var station = level.TrackStations[ride];
		Assert.AreEqual( (14, 10), station.Exit );
		Assert.IsTrue( level.BeginAddOn( jump, ride ) );
		var (x, y, rotation) = level.BuildTarget( jump, 15, 11 );
		var addOn = (OriginalObject?)typeof( Level ).GetMethod( "PlaceAddOnUnpaid", Private )!.Invoke( level, new object[] { jump, x, y, rotation, ride } );
		Assert.IsNotNull( addOn );
		CollectionAssert.AreEquivalent( TrackStation.TileCellsOf( 14, 10 ).ToArray(), addOn!.Cells.Select( cell => (cell.X, cell.Y) ).ToArray(), "the jump stands on the exit tile" );
		return (level, ride, addOn, new ToolTiles( station.Exit.X, station.Exit.Y, station.Entry.X, station.Entry.Y ), (addOn.Cells.First().X, addOn.Cells.First().Y));
	}

	private static bool TileHeld( Level level ) => TrackStation.TileCellsOf( 14, 10 ).All( cell => !level.KartTracks!.Tracks.Get( cell.X, cell.Y ).IsEmpty );

	private static bool TileGone( Level level ) => TrackStation.TileCellsOf( 14, 10 ).All( cell => level.KartTracks!.Tracks.Get( cell.X, cell.Y ).IsEmpty );

	[TestMethod]
	public void ASaleSparesTheTileTheCarriedTilesNameAndClearsItOtherwiseOnEveryPeer()
	{
		// tiles that name the ride's exit tile: the sale of the jump on it spares the tile
		var (spared, _, _, own, cell) = JumpOnTheExitTile();
		Assert.IsTrue( spared.Commands.Apply( new RemoveObjectAtCommand( cell.X, cell.Y, own ) ).Accepted );
		Assert.IsTrue( TileHeld( spared ), "the carried exit tile survives the clear" );
		// tiles of another ride: the same sale destroys the tile
		var (cleared, _, _, _, _) = JumpOnTheExitTile();
		Assert.IsTrue( cleared.Commands.Apply( new RemoveObjectAtCommand( cell.X, cell.Y, new ToolTiles( 40, 40, 38, 40 ) ) ).Accepted );
		Assert.IsTrue( TileGone( cleared ), "tiles that name another ride's tiles spare nothing here" );
		// a peer whose own globals say the opposite runs the logged command as it was issued: only the carried tiles decide
		var (twinSpared, _, _, _, _) = JumpOnTheExitTile();
		twinSpared.ClientToolGlobals.Exit = (40, 40);
		twinSpared.ClientToolGlobals.Entry = (38, 40);
		Assert.IsTrue( twinSpared.Commands.Apply( (ParkCommand)spared.Commands.Log.Single().Command ).Accepted );
		Assert.IsTrue( TileHeld( twinSpared ), "the peer spared the tile as the issuer did" );
		var (twinCleared, _, _, _, _) = JumpOnTheExitTile();
		twinCleared.ClientToolGlobals.Exit = own.ExitX == 0 ? (1, 1) : (own.ExitX, own.ExitY);
		twinCleared.ClientToolGlobals.Entry = (own.EntryX, own.EntryY);
		Assert.IsTrue( twinCleared.Commands.Apply( (ParkCommand)cleared.Commands.Log.Single().Command ).Accepted );
		Assert.IsTrue( TileGone( twinCleared ), "a peer whose globals name the tile still clears it: the command's tiles decide" );
		// the bytes keep them
		var decoded = (RemoveObjectAtCommand)ParkCommandCodec.Decode( ParkCommandCodec.Encode( (ParkCommand)spared.Commands.Log.Single().Command ) );
		Assert.AreEqual( own, decoded.Tiles );
	}

	[TestMethod]
	public void ACommandDecodedWithoutTilesSparesTheRidesOwnExitTile()
	{
		var (level, _, _, own, cell) = JumpOnTheExitTile();
		// a version 6 recording: no tiles, so the editor's own globals (the ride's exit and entry tiles) apply
		var old = new RemoveObjectAtCommand( cell.X, cell.Y );
		Assert.IsNull( ((RemoveObjectAtCommand)ParkCommandCodec.Decode( ParkCommandCodec.Encode( old ) )).Tiles );
		level.ClientToolGlobals.Exit = (40, 40);
		level.ClientToolGlobals.Entry = (38, 40);
		Assert.IsTrue( level.Commands.Apply( old ).Accepted );
		Assert.IsTrue( TileHeld( level ), $"the ride's own exit tile {own} stays without carried tiles" );
	}
}
