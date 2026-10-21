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
		var writer = new TrackLineWriter( builder, null, builder.OwnerOf( level.TrackStations[ride] ) );
		writer.Commit( (14, 10), (30, 10) );
		writer.Commit( (30, 10), (30, 14) );
		writer.Commit( (30, 14), (8, 14) );
		// Synthetic persisted straight links disagree with the station's +X traversal.
		// Only bridge-base memory is seeded; real Refresh computes the low nibble.
		foreach ( var x in new[] { 18, 20 } )
		{
			var tile = builder.Tracks.Get( x, 10 );
			tile.Link = TrackHeading.NegX;
			tile.Flags = TrackBuilder.FlagOverPath;
			builder.Tracks.Set( x, 10, tile );
		}
		TrackBuilder.RefreshAll( builder.Tracks );
		return (level, ride);
	}

	private static void AssertBridgeRoutePair( Level level, bool routeDirected )
	{
		foreach ( var x in new[] { 18, 20 } )
		{
			var tile = level.KartTracks!.Tracks.Get( x, 10 );
			var up = routeDirected ? x == 18 : x == 20;
			Assert.AreEqual( (int)(up ? TrackCell.FlagBridgeUp : TrackCell.FlagBridgeDown), tile.Flags & TrackCell.BridgeMask, $"bridge at {x},10" );
			Assert.AreEqual( TrackHeading.NegX, tile.Link, "the correction must not rewrite persisted straight links" );
			Assert.AreEqual( (int)TrackBuilder.FlagOverPath, tile.Flags & TrackBuilder.FlagOverPath );
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
	public void ClosingLevelLineCommandCorrectsBridgesOnlyAfterAValidRouteExists()
	{
		var (level, ride) = OpenBridgeRouteLevel();
		var open = level.WalkTrack( ride, TrackWalkCause.Restore );
		Assert.IsNotNull( open );
		Assert.IsFalse( open!.IsValid );
		AssertBridgeRoutePair( level, routeDirected: false );
		CloseBridgeRouteByCommand( level, ride );
		AssertBridgeRoutePair( level, routeDirected: true );
		Assert.IsTrue( level.WalkTrack( ride, TrackWalkCause.Edit )!.IsValid );
		AssertBridgeRoutePair( level, routeDirected: true );
	}

	[TestMethod]
	public void LevelTrackSnapshotRestoreRewalkCorrectsContextFreeRestoredBridges()
	{
		var (level, ride) = OpenBridgeRouteLevel();
		CloseBridgeRouteByCommand( level, ride );
		AssertBridgeRoutePair( level, routeDirected: true );
		var captured = level.CaptureTracksSnapshot();
		Assert.IsNotNull( captured );
		// The existing shared-module discriminator selects RewalkForRestore, rather
		// than the legacy RewalkTrack branch inside RestoreTrackControllers.
		Assert.IsNotNull( captured!.FrameCount );
		var saved = JsonSerializer.Deserialize<TrackWorldSaveData>( JsonSerializer.Serialize( captured ) )!;
		Assert.AreEqual( 1, saved.Controllers.Count );
		Assert.AreEqual( 1, saved.Controllers.Single().StationLayout );
		TrackSave.RestoreCells( level.Guests!.Grid.Cells, saved );
		AssertBridgeRoutePair( level, routeDirected: false );
		// Calls RestoreCells -> RestoreTrackControllers -> RewalkForRestore ->
		// WalkAndRefresh, then validates rebuilt sections and restores controller/module.
		level.RestoreTracksSnapshot( saved );
		AssertBridgeRoutePair( level, routeDirected: true );
		Assert.IsTrue( ride.Runtime.Bump!.IsTrackValid );
		var recaptured = level.CaptureTracksSnapshot()!;
		CollectionAssert.AreEqual( saved.Cells.ToArray(), recaptured.Cells.ToArray(), "the track cell payload, including preserved links and bridge state, round-trips" );
		CollectionAssert.AreEqual( saved.Controllers.Single().Sections.ToArray(), recaptured.Controllers.Single().Sections.ToArray(), "the live restore validates and preserves circuit sections" );
	}
}
