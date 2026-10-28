namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-undo] [EXT:online-coop-claims] The kart and water track tools in a shared park's undo (docs/COOP.md, "Conflicts and undo (C5)"; C5-3): the prediction of a track line's
/// take-back, and the guard of every other player's claimed track against a line and a take-back alike. Both run the tools' own code on a disposable copy of the cells
/// (<see cref="ParkCellMap.CopyForPrediction"/>, its track layer with it) with a copy of the bank's balance and inert callbacks, and rebuild no ride's state (no controller, add-on links, door
/// or animations): the park, its rides and its ledger are untouched by any prediction, accepted or refused.
/// </summary>
public sealed partial class Level
{
	/// <summary>
	/// A builder of <paramref name="live"/>'s family over the prediction copy <paramref name="copy"/>: its occupancy read from the copy where it reads cells, its money a copy of the bank's balance
	/// (<see cref="CellPredictionMoney"/>), and no entrance re-evaluation (the copy has none).
	/// </summary>
	private TrackBuilder PredictionBuilder( TrackBuilder live, ParkCellMap copy ) => new( copy, live.Family, () => null, Objects.Grid, ( x, y ) => IsPathBlockedOn( x, y, copy ) )
	{
		PredictionMoney = Park?.Economy is { } economy ? CellPredictionMoney.Of( economy, live.Purchase ) : null
	};

	/// <summary>
	/// [EXT:online-coop-claims] Whether <paramref name="line"/> of <paramref name="player"/> leaves alone every kart or water ride claimed against that player other than its own (C5-3): its own
	/// writer lays it on a copy of the park, as <see cref="ExecuteTrackLine"/> lays it (the tiles, their arms, the paths under them, the money where it stops, the circuit's walk), and no tile of
	/// such a ride's protected track (<see cref="TrackProvenance"/>) may change (<see cref="LeavesClaimedTracksAlone"/>). A line the park itself refuses is the park's to refuse.
	/// </summary>
	internal bool TrackLineLeavesClaimedTracksAlone( int player, LayTrackLineCommand line )
	{
		if ( waterTracks == null || Guests?.Grid.Cells is not { } cells || ResolveCommandObject( line.Ride ) is not { } station || !trackStations.TryGetValue( station, out var layout )
			|| BuilderOf( station ) is not { } builder || !HasClaimedTrackAgainst( player, line.Ride ) )
			return true;
		var copy = cells.CopyForPrediction();
		var predicted = PredictionBuilder( builder, copy );
		var start = (line.StartX, line.StartY);
		new TrackLineWriter( predicted, () => TrackCircuitWalker.WalkAndRefresh( predicted, layout ), predicted.OwnerOf( layout ) ).Commit( start, (line.EndX, line.EndY) );
		return LeavesClaimedTracksAlone( player, line.Ride, cells, copy );
	}

	/// <summary>
	/// [EXT:online-coop-undo] Whether a track line's take-back by <paramref name="inverse"/> would do exactly the line's work back now (C5-3): every tile it laid still a tile of the ride and every
	/// tile it raised still a tile, then its own undo (<see cref="TrackEditor.UndoLine"/>: the directional clear, the station's repair with the derived first-line flag) and the circuit's walk run on a
	/// copy of the park with inert callbacks, as <see cref="ExecuteTrackLine"/> runs them. The clear must clear every tile in <paramref name="laid"/> but the ride's exit and entry tiles, which it
	/// spares as single player's Backspace does, and count down every tile in <paramref name="raised"/> by exactly one; and every other ride claimed against the inverse's player keeps its protected
	/// track (<see cref="LeavesClaimedTracksAlone"/>). The repair and the walk are the undo's own, on the claimed ride's own track, and are not compared with the line.
	/// </summary>
	internal bool PredictTrackLineUndo( UndoTrackLineCommand inverse, IReadOnlyCollection<(int X, int Y)> laid, IReadOnlyCollection<(int X, int Y)> raised )
	{
		if ( waterTracks == null || Guests?.Grid.Cells is not { } cells || ResolveCommandObject( inverse.Ride ) is not { } station || !trackStations.TryGetValue( station, out var layout )
			|| BuilderOf( station ) is not { } builder )
			return false;
		var tracks = cells.Tracks;
		var owner = builder.OwnerOf( layout );
		// the step still there: a tile it laid another command cleared, even one taken back since, leaves its undo less to clear than it laid
		if ( laid.Any( tile => tracks.Get( tile.X, tile.Y ) is not { IsOrigin: true } cell || cell.Type != builder.OriginType || (cell.Owner != owner && cell.Owner != 0) )
			|| raised.Any( tile => !tracks.Get( tile.X, tile.Y ).IsOrigin ) )
			return false;
		var copy = cells.CopyForPrediction();
		var predicted = PredictionBuilder( builder, copy );
		// no hooks: a cleared tile's refund and an add-on's demolition are the take-back's own, after the prediction
		var editor = new TrackEditor( predicted, layout );
		new TrackLineWriter( predicted, () => TrackCircuitWalker.WalkAndRefresh( predicted, layout ), owner, editor )
			.Undo( new SegmentResult( (inverse.EndX, inverse.EndY), Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false ) { Start = (inverse.StartX, inverse.StartY), IsFirstLine = inverse.Last } );
		if ( laid.Any( tile => tile != layout.Exit && tile != layout.Entry && copy.Tracks.Get( tile.X, tile.Y ).Type != 0 ) )
			return false;
		if ( raised.Any( tile => copy.Tracks.Get( tile.X, tile.Y ) is not { IsOrigin: true } after || after.Overlap != tracks.Get( tile.X, tile.Y ).Overlap - 1 ) )
			return false;
		return LeavesClaimedTracksAlone( inverse.Player, inverse.Ride, cells, copy );
	}

	/// <summary>[EXT:online-coop-claims] Whether a kart or water ride other than <paramref name="ownRide"/> is claimed against <paramref name="player"/> (C5-3).</summary>
	private bool HasClaimedTrackAgainst( int player, int ownRide ) =>
		HasClaimsAgainst( player ) && trackStations.Keys.Any( ride => !ride.IsDeleted && ride.Runtime.AttractionId != ownRide && ClaimedAgainst( player, ride.Runtime.AttractionId ) );

	/// <summary>
	/// [EXT:online-coop-claims] Whether an operation of <paramref name="player"/> on its own ride <paramref name="ownRide"/>, predicted from <paramref name="before"/> (the park's cells) to
	/// <paramref name="after"/> (the prediction's copy), leaves alone every other kart or water ride claimed against that player (C5-3): the test is of real effects, not of the setters called. For
	/// each such ride, no tile of its protected track before the operation (<see cref="TrackProvenance"/>) may have any field changed after it (each cell's track record and map fields compared, so a
	/// neighbour's refresh that writes the same values is no change), and its protected track after it must be the same tiles (no tile joined to its dangling arm, none a walk now goes on through,
	/// none cut away).
	/// </summary>
	private bool LeavesClaimedTracksAlone( int player, int ownRide, ParkCellMap before, ParkCellMap after )
	{
		var claimed = trackStations.Where( pair => !pair.Key.IsDeleted && pair.Key.Runtime.AttractionId != ownRide && ClaimedAgainst( player, pair.Key.Runtime.AttractionId ) )
			.OrderBy( pair => pair.Key.Runtime.AttractionId ).ToArray();
		if ( claimed.Length == 0 )
			return true;
		// the rides' walks write the tiles they visit: each side's provenance reads a throwaway copy of its own
		var scratchBefore = before.CopyForPrediction();
		var scratchAfter = after.CopyForPrediction();
		foreach ( var (ride, station) in claimed )
		{
			if ( BuilderOf( ride ) is not { } builder )
				continue;
			var was = TrackProvenance.ProtectedTiles( scratchBefore, builder.Family, Objects.Grid, station );
			var now = TrackProvenance.ProtectedTiles( scratchAfter, builder.Family, Objects.Grid, station );
			if ( !was.SetEquals( now ) )
				return false;
			foreach ( var (x, y) in was )
			{
				foreach ( var (cx, cy) in TrackStation.TileCellsOf( x, y ) )
				{
					if ( before.InBounds( cx, cy ) && (!before.Tracks.Get( cx, cy ).SameAs( after.Tracks.Get( cx, cy ) ) || before.FieldsAt( cx, cy ) != after.FieldsAt( cx, cy )) )
						return false;
				}
			}
		}
		return true;
	}
}
