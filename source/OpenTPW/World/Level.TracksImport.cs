namespace OpenTPW;

public partial class Level
{
	/// <summary>
	/// Restores the kart and water tracks of an original save: the structure layer's track cells, then each ride's circuit walked from them and checked against the TrackRides block's section list, and the controller words of its ride
	/// chunk. Only a park started from an original save does this; a resumed park restores its own tracks. A track that does not read like the original's is reported and left as the placement made it.
	/// </summary>
	private void ImportOriginalTracks()
	{
		if ( resumeState != null || Park?.Import is not { } import || OriginalPark?.Save is not { } save || GameEditions.IsThemeParkInc )
			return;
		var grid = save.Cells;
		foreach ( var line in ApplyOriginalTracks( save.TrackCells, ( x, y ) => grid[x, y].MapType, save.TrackRides, save.TrackRidesProblem, save.Attractions ) )
		{
			import.Report( line );
			Log.Trace( $"Park track import: {line}" );
		}
	}

	// [APPROX:TRACK-032] an original save's kart or water track is imported from the structure layer's track cells (the 31-byte cells, loader 0x4D0B30 and 0x50C200: the type 11 and 13 kart and 16 and 18 water tiles and their type-12 extensions with their connection mask, flow link, flags, origin, chosen piece, overlap, alternate mask and segment; the ride of a tile is the one whose station reaches it through the masks) and each ride's circuit is walked from them, compared with chunk 4's section list and given chunk 3's laps, performance and phase (Level.ImportOriginalTracks; loader 0x543560: the ride chunk 0x543638, the section chunk 0x543FF4 through the section builder 0x54B2F0); not imported: the vehicles (chunk 5, the 0xAC-byte records with their tile and waypoint keys) with their riders and the pending and leaving lists (chunks 7, 8 and 9: the guests riding are not imported either, SAVE-002), the ride words +0x10, +0x14 and +0x18 OpenTPW's controller does not hold, a kart or bumper phase of racing or over (it loads in loading, where a water ride keeps its running phase), the chunk-6 reference waypoint (the open routine 0x544840 picks the same one: the first flag-4 buoy, then the next that is not flag 2), the cells' mesh instances (the piece instances are made again) and the flag bits OpenTPW's cells do not hold; a tile that replaced path cells is a bridge only where the map layer under it holds a path or queue; no save of the corpus or Easymode holds a kart, water or bumper ride (their TrackRides block is the 44-byte build-stamp chunk), so the import is checked against the saver's shapes and OpenTPW's own tracks only
	/// <summary>The import of <see cref="ImportOriginalTracks"/> over the pieces of a save (the cells, the map type under a cell, the ride chunks and the attraction things): the evidence lines.</summary>
	internal IReadOnlyList<string> ApplyOriginalTracks( IReadOnlyList<SaveTrackCell> savedCells, Func<int, int, uint> mapTypeAt, SaveTrackRideBlock? block, string? problem, IReadOnlyList<SaveAttraction> attractions )
	{
		var lines = new List<string>();
		if ( problem != null )
			lines.Add( $"[EXT:original-engine-state-dumps] The TrackRides block does not read in the writer's shapes ({problem}): its rides are not restored." );
		if ( Guests == null || waterTracks == null || kartTracks == null )
			return lines;
		var rides = block?.Chunks.Where( chunk => chunk.Ride != null ).Select( chunk => chunk.Ride! ).ToList() ?? new List<SaveTrackRideHeader>();
		if ( savedCells.Count == 0 && rides.Count == 0 )
			return lines;
		var cells = Guests.Grid.Cells;
		var layer = cells.Tracks;
		var stations = trackStations.Where( pair => !pair.Key.IsDeleted && pair.Key.Runtime.Bump is { Template.IsBumper: false } ).OrderBy( pair => pair.Key.ThingId ).ToList();
		var origins = savedCells.Where( cell => cell.Type != TrackCellTypes.Extension ).GroupBy( cell => (cell.X, cell.Y) ).ToDictionary( group => group.Key, group => group.First() );
		var owners = new Dictionary<(int X, int Y), int>();
		var skipped = new HashSet<OriginalObject>();
		foreach ( var (item, station) in stations )
		{
			var builder = BuilderOf( item )!;
			var what = $"attraction thing {item.ThingId}";
			var misplaced = station.Tiles.Where( tile => !origins.TryGetValue( tile, out var saved ) || !TrackCellTypes.IsStation( saved.Type ) ).ToList();
			if ( misplaced.Count > 0 )
			{
				lines.Add( $"Track of {what} not imported: its saved station tiles are not at ({string.Join( "), (", misplaced.Select( tile => $"{tile.X}, {tile.Y}" ) )}), where the placement lays them." );
				skipped.Add( item );
				continue;
			}
			var owner = builder.OwnerOf( station );
			foreach ( var tile in OriginalTrackImport.Reach( station.Tiles, origins ) )
				owners.TryAdd( tile, owner );
		}
		var written = 0;
		var strayTiles = 0;
		var unknownFlags = 0;
		var stationCells = new HashSet<(int X, int Y)>( skipped.SelectMany( item => trackStations[item].Tiles.SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) ) ) );
		foreach ( var saved in savedCells )
		{
			if ( !cells.InBounds( saved.X, saved.Y ) || stationCells.Contains( (saved.X, saved.Y) ) )
				continue;
			var cell = OriginalTrackImport.ToCell( saved, layer.Width, layer.Height );
			if ( OriginalTrackImport.HasUnknownFlags( saved ) )
				unknownFlags++;
			var origin = saved.Type == TrackCellTypes.Extension ? layer.CellOf( cell.Parent >= 0 ? cell.Parent : layer.IndexOf( saved.X, saved.Y ) ) : (saved.X, saved.Y);
			cell.Owner = owners.TryGetValue( origin, out var tileOwner ) ? tileOwner : 0;
			var existing = layer.Get( saved.X, saved.Y );
			// the station tiles the placement laid already keep the ride's owner and the type the cell stood on
			if ( !existing.IsEmpty && existing.Type == cell.Type )
				(cell.BaseType, cell.Owner) = (existing.BaseType, existing.Owner != 0 ? existing.Owner : cell.Owner);
			if ( saved.Type != TrackCellTypes.Extension && cell.Owner == 0 )
				strayTiles++;
			if ( saved.Type != TrackCellTypes.Extension && !TrackCellTypes.IsStation( saved.Type ) )
			{
				var underPath = TrackStation.TileCellsOf( saved.X, saved.Y ).Any( tile => cells.InBounds( tile.X, tile.Y ) && mapTypeAt( tile.X, tile.Y ) is 1 or 3 );
				if ( underPath )
					cell.Flags |= TrackBuilder.FlagOverPath;
			}
			cells.SetType( saved.X, saved.Y, saved.Type );
			layer.Set( saved.X, saved.Y, cell );
			written++;
		}
		if ( written > 0 )
		{
			TrackBuilder.RefreshAll( layer );
			lines.Add( $"Track cells imported: {written} cells of the structure layer ({savedCells.Count( cell => cell.Type != TrackCellTypes.Extension )} tiles, {strayTiles} reached from no station{(unknownFlags > 0 ? $", {unknownFlags} with flag bits OpenTPW's cells do not hold" : "")})." );
		}
		var chunks = block?.Chunks ?? Array.Empty<SaveTrackRideChunk>();
		foreach ( var (item, station) in stations )
		{
			if ( skipped.Contains( item ) || item.Runtime.Bump is not { } bump )
				continue;
			var saved = attractions.FirstOrDefault( attraction => attraction.Id == item.ThingId );
			var ride = saved == null ? null : rides.FirstOrDefault( header => header.Identity.Value == saved.TrackRideHandle );
			var what = $"attraction thing {item.ThingId}";
			var circuit = WalkTrack( item, TrackWalkCause.Restore );
			if ( ride == null )
				continue;
			if ( ride.Identity.Kind != bump.Type )
			{
				lines.Add( $"Ride chunk of {what} not applied: its kind {ride.Identity.Kind} is not the controller type {bump.Type}." );
				continue;
			}
			var words = OriginalTrackImport.WordsOf( ride );
			var kept = bump.RestoreOriginalWords( words.Laps, words.Performance, words.Phase );
			var details = new List<string> { $"laps {words.Laps}, performance {Math.Clamp( words.Performance, 0, 100 )}, phase {kept}" };
			var sections = chunks.Where( chunk => chunk.Tile is { } tile && tile.Identity.Value == ride.Identity.Value ).Select( chunk => chunk.Tile! ).ToList();
			var differences = circuit == null ? new List<string> { "no circuit walked" } : OriginalTrackImport.CompareSections( sections, bump.Sections ).ToList();
			details.Add( differences.Count == 0 ? $"{sections.Count} saved sections equal the walked circuit" : $"saved sections differ from the walked circuit ({string.Join( "; ", differences.Take( 3 ) )})" );
			if ( circuit != null && saved != null && (saved.IsTrackRideValid != 0) != circuit.IsValid )
				details.Add( $"the saved track verdict ({(saved.IsTrackRideValid != 0 ? "valid" : "invalid")}) differs from the walk's ({(circuit.IsValid ? "valid" : "invalid")})" );
			if ( words.StationX != bump.StationX || words.StationY != bump.StationY || words.Orientation != bump.Orientation )
				details.Add( $"the saved station reference ({words.StationX}, {words.StationY}, orientation {words.Orientation}) differs from the placement's ({bump.StationX}, {bump.StationY}, {bump.Orientation})" );
			lines.Add( $"Track ride imported for {what}: {string.Join( "; ", details )}." );
		}
		var vehicles = chunks.Count( chunk => chunk.RawRecord != null );
		var peeps = chunks.Count( chunk => chunk.Type is 7 or 8 or 9 );
		if ( vehicles > 0 || peeps > 0 )
			lines.Add( $"[EXT:original-engine-state-dumps] Track ride vehicles not restored: {vehicles} vehicle records and {peeps} rider and peep chunks (the guests riding are not imported either)." );
		return lines;
	}
}
