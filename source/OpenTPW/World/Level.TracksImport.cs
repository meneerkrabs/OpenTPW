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

	// [APPROX:TRACK-032] SAVED-RIDE TRACK: an original save's kart or water track is imported from the structure layer's track cells (the 31-byte cells, loader 0x4D0B30 and 0x50C200: the type 11 and 13 kart and 16 and 18 water tiles and their type-12 extensions with their connection mask, flow link, flags, origin, chosen piece, overlap, alternate mask and segment; the ride of a tile is the one whose station reaches it through the masks) and each ride's circuit is walked from them, compared with chunk 4's section list and given chunk 3's words (the laps, the performance, the model words +0x14 and +0x18 the loader stores raw, and the phase; Level.ImportOriginalTracks; loader 0x543560: the ride chunk 0x543638, the section chunk 0x543FF4 through the section builder 0x54B2F0); once the guest import has made the riders, the vehicles (chunk 5: the 43 words of the 0xAC-byte record, the collision object found from the vehicle's place or the fallback place of the chunk, the aimed waypoint found from its key), their riders (chunk 9, with their seat numbers) and the joining and leaving lists (chunks 8 and 7, the join refused for a ride in phase 0, one peep list of 1024 nodes for every ride) are put on the controller as the loader leaves them, every list reversed because the loader pushes each node on the head again, the phase raw (a racing or over kart or bumper ride keeps it), the chunk-6 reference waypoint the walk's own (the open routine 0x544840 picks the same one) and nothing drawn from the world generator, as the loader neither launches a vehicle (0x54A040) nor hangs a head (0x44B410) (Level.ImportOriginalTrackRiders, BumpController.ImportOriginalVehicles, tp.exe 0x543B3C..0x543F24); a ride whose chunks do not read like the saver's, whose vehicle stands in no collision object (the module tick 0x546C80 reads the object unchecked) or aims at a waypoint of another ride, or that names a guest who is not a rider of it, is refused with its reason and keeps no vehicle (a kart or bumper ride saved racing or over then waits in loading); not imported: the flag bits of the track cells above 0x1F (the structure cell's mFlags is the layout the map cell shares, where tp.exe's cell code sets 0x20, 0x40 and 0x80 for other meanings, and its meaning on a track record is not read; OpenTPW's own 0x20 is the bridge-over-path memory), the mesh-instance words of the cells and vehicles (+4 of a cell, +8 and +0xC of a vehicle: engine handles the loader binds again, 0x463AB0, which OpenTPW's renderers make anew) and the voice and emitter handles of a vehicle (+0x20, +0x2C: a boat's voice and a smoking car's emitter start again as after a park resume); a straight or stub tile over a path or queue cell keeps that cell's type and lies beneath it (0x538FC0); a corner or junction tile replaces the cell, with the bridge remembered through the saved map layer (TRACK-010); no save of the corpus or Easymode holds a kart, water or bumper ride (their TrackRides block is the 44-byte build-stamp chunk), so the import is checked against the saver's shapes and OpenTPW's own tracks only
	/// <summary>The import of <see cref="ImportOriginalTracks"/> over the pieces of a save (the cells, the map type under a cell, the ride chunks and the attraction things): the evidence lines.</summary>
	/// <summary>The rides of the last <see cref="ApplyOriginalTracks"/> whose ride chunk was applied: their station, controller and chunk, for the vehicle and rider step (<see cref="ApplyOriginalTrackRiders"/>).</summary>
	private List<(OriginalObject Item, BumpController Bump, SaveTrackRideHeader Ride)> importedTrackRides = new();

	internal IReadOnlyList<string> ApplyOriginalTracks( IReadOnlyList<SaveTrackCell> savedCells, Func<int, int, uint> mapTypeAt, SaveTrackRideBlock? block, string? problem, IReadOnlyList<SaveAttraction> attractions )
	{
		var lines = new List<string>();
		importedTrackRides = new();
		if ( problem != null )
			lines.Add( $"[APPROX:TRACK-032] The TrackRides block does not read in the writer's shapes ({problem}): its rides are not restored." );
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
		var blocked = 0;
		var stationCells = new HashSet<(int X, int Y)>( skipped.SelectMany( item => trackStations[item].Tiles.SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) ) ) );
		foreach ( var saved in savedCells )
		{
			if ( !cells.InBounds( saved.X, saved.Y ) || stationCells.Contains( (saved.X, saved.Y) ) )
				continue;
			// a cell the park's own placement holds as an object, entrance or exit is not given to a track by the save: the placement is what this park built (a path or queue cell is what a track bridges)
			if ( cells.RawTypeAt( saved.X, saved.Y ) is ParkCellTypes.Object or ParkCellTypes.Entrance or ParkCellTypes.Exit )
			{
				blocked++;
				continue;
			}
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
			// [BIN:TPW-EXE:0x538FC0 track type op] the track type op writes the type of the record and never the cell's (0x539017..0x53908C): a straight or stub tile over a path or queue cell keeps that cell's own type and lies
			// beneath it as a separate record (ParkCellMap.PrimaryTypeAt), so the queue chain and the guests on it stay
			var bridged = cells.RawTypeAt( saved.X, saved.Y ) is (byte)ParkCellType.Path or (byte)ParkCellType.Queue
				&& origins.TryGetValue( origin, out var head ) && !TrackCellTypes.IsStation( head.Type ) && TrackBuilder.IsBridgeShape( head.Mask );
			if ( !bridged )
				cells.SetType( saved.X, saved.Y, saved.Type );
			layer.Set( saved.X, saved.Y, cell );
			written++;
		}
		if ( blocked > 0 )
			lines.Add( $"Track cells skipped: {blocked} saved track cells lie on cells this park holds as an object, entrance or exit." );
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
			// [BIN:TPW-EXE:0x543638 ride chunk] the words +0x14 and +0x18 (the model range: a new vehicle's variant is its count modulo +0x18 plus +0x14, 0x549DB0) are stored as saved after the constructor copied the template's; the word +0x10 goes to the constructor as the thing the ride belongs to (0x529F6A passes the thing being placed)
			if ( !bump.TryRestoreModelWords( unchecked((int)ride.Field14), unchecked((int)ride.Field18) ) )
				details.Add( $"the saved model words {unchecked((int)ride.Field14)} and {unchecked((int)ride.Field18)} are no range of vehicle models and the template's stay" );
			if ( (ride.Field10 & 0xFFFF) != (uint)item.ThingId )
				details.Add( $"the saved ride word +0x10 ({ride.Field10}) is not the attraction's thing id ({item.ThingId})" );
			var sections = chunks.Where( chunk => chunk.Tile is { } tile && tile.Identity.Value == ride.Identity.Value ).Select( chunk => chunk.Tile! ).ToList();
			var differences = circuit == null ? new List<string> { "no circuit walked" } : OriginalTrackImport.CompareSections( sections, bump.Sections ).ToList();
			details.Add( differences.Count == 0 ? $"{sections.Count} saved sections equal the walked circuit" : $"saved sections differ from the walked circuit ({string.Join( "; ", differences.Take( 3 ) )})" );
			if ( circuit != null && saved != null && (saved.IsTrackRideValid != 0) != circuit.IsValid )
				details.Add( $"the saved track verdict ({(saved.IsTrackRideValid != 0 ? "valid" : "invalid")}) differs from the walk's ({(circuit.IsValid ? "valid" : "invalid")})" );
			if ( words.StationX != bump.StationX || words.StationY != bump.StationY || words.Orientation != bump.Orientation )
				details.Add( $"the saved station reference ({words.StationX}, {words.StationY}, orientation {words.Orientation}) differs from the placement's ({bump.StationX}, {bump.StationY}, {bump.Orientation})" );
			lines.Add( $"Track ride imported for {what}: {string.Join( "; ", details )}." );
			importedTrackRides.Add( (item, bump, ride) );
		}
		return lines;
	}
}
