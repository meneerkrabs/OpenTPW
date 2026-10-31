using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

// Reuses the nonparallel, asset-free Level fixture and its saved/restored globals.
// These exercise the command's world handler and the track snapshot restore pipeline,
// not GPU rendering, primary-layer underlay persistence, or full ApplyResumeSnapshot.
public partial class TrackAddOnLevelTests
{
	private (Level Level, OriginalObject Ride) OpenBridgeRouteLevel()
	{
		var (level, ride, _) = World( laid: false );
		var builder = level.KartTracks!;
		// [BIN:TPW-EXE:0x00538FC0 track type op] paths under two tiles of the line stay (only the line's last tile clears the paths under it) and the tiles bridge them
		foreach ( var x in new[] { 18, 19, 20, 21 } )
			foreach ( var y in new[] { 10, 11 } )
				level.Guests!.Grid.Cells.SetType( x, y, ParkCellType.Path );
		var writer = new TrackLineWriter( builder, null, builder.OwnerOf( level.TrackStations[ride] ) );
		writer.Commit( (14, 10), (30, 10) );
		writer.Commit( (30, 10), (30, 14) );
		writer.Commit( (30, 14), (8, 14) );
		return (level, ride);
	}

	/// <summary>The two bridged straights the walk round the ride's track gave their flag words: 18 has a bridge ahead, 20 one behind (the walk runs along the station's heading, +X).</summary>
	private static void AssertBridgeRoutePair( Level level )
	{
		foreach ( var x in new[] { 18, 20 } )
		{
			var tile = level.KartTracks!.Tracks.Get( x, 10 );
			var up = x == 18;
			Assert.AreEqual( (int)(up ? TrackCell.FlagBridgeUp : TrackCell.FlagBridgeDown), tile.Flags & TrackCell.BridgeMask, $"bridge at {x},10" );
			Assert.AreEqual( 0, tile.Flags & 0x20, "the tile holds no memory of paths of its own: the paths stay in the map layer" );
			Assert.AreEqual( (byte)ParkCellType.Path, level.Guests!.Grid.Cells.PrimaryTypeAt( x, 10 ), "the path stays under the tile" );
			var piece = TrackPieceSelector.Select( tile.Type, tile.Mask, tile.Link, tile.Flags );
			Assert.AreEqual( new TrackPiece( tile.Family, tile.Piece, tile.Rotation ), piece );
		}
	}

	private static void CloseBridgeRouteByCommand( Level level, OriginalObject ride )
	{
		// This is the existing live command world handler, with its actual writer callback
		// to Level.WalkTrack. It does not substitute a callback defined by the test.
		var result = level.ExecuteTrackLine( ride.Runtime.AttractionId, (8, 14), (8, 10), false );
		Assert.IsTrue( result.Accepted, result.Reason );
		Assert.IsInstanceOfType( result.Value, typeof( TrackLineDone ) );
		Assert.AreEqual( true, ((TrackLineDone)result.Value!).Valid );
		Assert.IsTrue( ride.Runtime.Bump!.IsTrackValid );
	}

	[TestMethod]
	public void LevelLinesOverPathsBridgeThemAndTheWalkGivesTheFlagWords()
	{
		var (level, ride) = OpenBridgeRouteLevel();
		var open = level.WalkTrack( ride, TrackWalkCause.Restore );
		Assert.IsNotNull( open );
		Assert.IsFalse( open!.IsValid );
		AssertBridgeRoutePair( level );
		CloseBridgeRouteByCommand( level, ride );
		AssertBridgeRoutePair( level );
		Assert.IsTrue( level.WalkTrack( ride, TrackWalkCause.Edit )!.IsValid );
		AssertBridgeRoutePair( level );
	}

	[TestMethod]
	public void LevelTrackSnapshotRestoreKeepsTheFlagWords()
	{
		var (level, ride) = OpenBridgeRouteLevel();
		CloseBridgeRouteByCommand( level, ride );
		AssertBridgeRoutePair( level );
		var captured = level.CaptureTracksSnapshot();
		Assert.IsNotNull( captured );
		Assert.IsNotNull( captured!.FrameCount );
		var saved = JsonSerializer.Deserialize<TrackWorldSaveData>( JsonSerializer.Serialize( captured ) )!;
		Assert.AreEqual( 1, saved.Controllers.Count );
		Assert.AreEqual( 1, saved.Controllers.Single().StationLayout );
		TrackSave.RestoreCells( level.Guests!.Grid.Cells, saved );
		AssertBridgeRoutePair( level );
		level.RestoreTracksSnapshot( saved );
		AssertBridgeRoutePair( level );
		Assert.IsTrue( ride.Runtime.Bump!.IsTrackValid );
		var recaptured = level.CaptureTracksSnapshot()!;
		CollectionAssert.AreEqual( saved.Cells.ToArray(), recaptured.Cells.ToArray(), "the track cell payload, including the bridge state, round-trips" );
		CollectionAssert.AreEqual( saved.Controllers.Single().Sections.ToArray(), recaptured.Controllers.Single().Sections.ToArray(), "the live restore validates and preserves circuit sections" );
	}
}
