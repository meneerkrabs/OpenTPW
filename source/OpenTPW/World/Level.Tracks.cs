namespace OpenTPW;

/// <summary>Kart and water ride tracks (docs/TRACKS.md): the track tool, the stations' tiles and circuit walks, the controllers' state and the track renderer.</summary>
public partial class Level
{
	private TrackBuilder? waterTracks;
	private TrackBuilder? kartTracks;
	private readonly Dictionary<OriginalObject, TrackStation> trackStations = new();
	private readonly Dictionary<TrackFamily, TrackRenderer> trackRenderers = new();
	private bool trackRenderFailed;
	private static readonly TrackFamily[] TrackFamilies = { TrackFamily.Water, TrackFamily.Kart };

	/// <summary>The headless water track builder over the level's cell map (null in the generic sandbox).</summary>
	public TrackBuilder? WaterTracks => waterTracks;
	/// <summary>The headless go-kart track builder over the same cell map (null in the generic sandbox).</summary>
	public TrackBuilder? KartTracks => kartTracks;

	/// <summary>The builder of a station's family: kart rides lay kart tiles (type 11), water rides water tiles (type 16).</summary>
	public TrackBuilder? BuilderOf( OriginalObject station ) => station.Runtime.Bump?.Template.Family == TrackFamily.Kart ? kartTracks : waterTracks;
	private OriginalObject? trackToolStation;
	/// <summary>The water ride whose track the track tool lays, or null when the tool is off.</summary>
	public OriginalObject? TrackToolStation => CellTool.Writer is TrackLineWriter ? trackToolStation : null;
	/// <summary>The stations of the level's water rides with their tile layout.</summary>
	public IReadOnlyDictionary<OriginalObject, TrackStation> TrackStations => trackStations;
	/// <summary>Tile and boat instances the track renderer holds.</summary>
	public (int Tiles, int Boats) TrackInstances => (trackRenderers.Values.Sum( renderer => renderer.TileCount ), trackRenderers.Values.Sum( renderer => renderer.BoatCount ));
	/// <summary>The renderer of one track family (null until a station of the family exists and the first frame ran).</summary>
	internal TrackRenderer? TrackRendererOf( TrackFamily family ) => trackRenderers.GetValueOrDefault( family );

	private void SetupTracks()
	{
		if ( Guests == null )
			return;
		waterTracks = new TrackBuilder( Guests.Grid.Cells, TrackFamily.Water, () => Park?.Economy, Objects.Grid, IsPathBlocked );
		kartTracks = new TrackBuilder( Guests.Grid.Cells, TrackFamily.Kart, () => Park?.Economy, Objects.Grid, IsPathBlocked );
		Objects.ObjectPlaced += OnTrackStationPlaced;
		Objects.ObjectRemoved += OnTrackStationRemoved;
		foreach ( var item in Objects.Objects )
			OnTrackStationPlaced( item );
	}

	private void OnTrackStationPlaced( OriginalObject item )
	{
		if ( waterTracks == null || kartTracks == null || item.Runtime.Bump is not { } bump || item.IsDeleted )
			return;
		if ( bump.Template.IsBumper )
		{
			// bumper cars lay no track: the controller's arena sits on the ride's footprint
			var (arenaX, arenaY) = BumperArenaCentre( item );
			bump.SetStation( arenaX, arenaY, TrackHeading.FromRotation( item.Placement.Rotation ) );
			return;
		}
		var station = TrackStation.FromPlacement( item.Placement.X, item.Placement.Y, item.Placement.Rotation );
		trackStations[item] = station;
		var centre = TrackStation.Centre( station.First );
		bump.SetStation( centre.X, centre.Y, station.Heading );
		// A restored park carries its tiles and boats in the save's track part; the restore walks the circuit then.
		if ( resumeState != null )
			return;
		BuilderOf( item )!.PlaceStation( station );
		RewalkTrack( item );
	}

	/// <summary>The centre of a bumper ride's arena in section units: the middle of the bounding box of the ride's footprint cells.</summary>
	// [BIN:TPW-EXE:0x00529E10 ride placement] the original places the arena at the placement cell plus a per-rotation offset read from the object type (+0xA4..+0xC0) and 0x600 on both axes
	// [APPROX:TRACK-030] the arena centre is the middle of the footprint's bounding box, not the original's type-record offset — evidence needed: the offset fields at the type record's +0xA4..+0xC0 and the conversion of the placement cell in 0x00529E10
	internal static (int X, int Y) BumperArenaCentre( OriginalObject item )
	{
		var cells = item.Cells.ToArray();
		if ( cells.Length == 0 )
			return (item.Placement.X * TrackStation.CellUnits + TrackStation.CellUnits / 2, item.Placement.Y * TrackStation.CellUnits + TrackStation.CellUnits / 2);
		var minX = cells.Min( cell => cell.X );
		var maxX = cells.Max( cell => cell.X );
		var minY = cells.Min( cell => cell.Y );
		var maxY = cells.Max( cell => cell.Y );
		return ((minX + maxX + 1) * TrackStation.CellUnits / 2, (minY + maxY + 1) * TrackStation.CellUnits / 2);
	}

	private void OnTrackStationRemoved( OriginalObject item )
	{
		if ( waterTracks == null || !trackStations.Remove( item, out var station ) )
			return;
		if ( TrackToolStation == item )
			CellTool.Cancel();
		trackToolStation = null;
		DemolishTrack( station, BuilderOf( item )! );
		item.Runtime.Bump?.Rebuild( TrackCircuit.Invalid( "removed" ) );
	}

	/// <summary>
	/// Removes the track of a station: every tile laid for its ride (TRACK-021), then the station tiles. Nothing is charged or refunded.
	/// </summary>
	// [BIN:STP-PPC:0x100748A0 ride demolition] the demolition replays the queue tool and the kart/water track tool from both station sides to clear the circuit, with one Spend of 2 × cell cost × 0x100E2424() / 100
	// [APPROX:TRACK-007] the demolition clears the ride's own tiles and pays nothing: the original's one Spend (2 × the cell cost scaled by a percentage) has an untraced sign and percentage — evidence needed: 0x100E2424 and the Spend's direction
	private void DemolishTrack( TrackStation station, TrackBuilder builder )
	{
		// exactly the tiles laid for this ride go, so a neighbouring ride's circuit that touches it stays
		builder.RemoveOwned( builder.OwnerOf( station ) );
		builder.RemoveStation( station );
	}

	/// <summary>
	/// Walks the circuit of <paramref name="item"/> again after a track edit: the controller takes the new sections and validity (the boats leave and the
	/// ride closes, as a re-walk does in the original), and a valid circuit ends the track tool and starts the queue tool.
	/// </summary>
	public TrackCircuit? RewalkTrack( OriginalObject item )
	{
		if ( waterTracks == null || !trackStations.TryGetValue( item, out var station ) || item.Runtime.Bump is not { } bump )
			return null;
		var circuit = TrackCircuitWalker.WalkAndRefresh( BuilderOf( item )!, station );
		bump.Rebuild( circuit );
		if ( !circuit.IsValid )
			return circuit;
		LastActionMessage = $"The track is complete: {circuit.Steps.Count} tiles.";
		// [BIN:TPW-EXE:0x0052A970 circuit walk] a valid circuit closes the tool; an invalid one plays animation 12 on the pieces and the tool continues from the last end
		// [APPROX:TRACK-006] the advisor and event messages the original raises after a walk (ids 0xCC, 0x151, 0xB8, 0xD6) are not shown: their texts and triggers are not resolved — evidence needed: the message table at 0x0059B590
		if ( TrackToolStation == item )
		{
			CellTool.Cancel();
			trackToolStation = null;
			if ( item.Entry.HasQueue && item.Runtime.IsAttraction && item.Visitors.HasCells )
				EnterQueueTool( item.Visitors );
		}
		return circuit;
	}

	/// <summary>Starts the water track tool on a station: the line starts at the tile after the station's exit.</summary>
	public bool EnterTrackTool( OriginalObject station )
	{
		if ( IsReadOnlyVisit || waterTracks == null || !trackStations.TryGetValue( station, out var layout ) )
			return false;
		StaffPlacement?.Cancel();
		var builder = BuilderOf( station )!;
		CellTool.Cancel();
		CellTool.Enter( new TrackLineWriter( builder, () => RewalkTrack( station ), builder.OwnerOf( layout ) ), layout.Exit );
		trackToolStation = station;
		return true;
	}

	/// <summary>Ends the track tool.</summary>
	public void ExitTrackTool()
	{
		if ( TrackToolStation != null )
			CellTool.Cancel();
		trackToolStation = null;
	}

	/// <summary>
	/// Lays the snapped straight track line from <paramref name="from"/> toward <paramref name="to"/> for a station (headless API; the track tool commits
	/// through the same writer): tiles two cells apart, charged <c>Costs.WaterTrackCell</c> each. Read-only visits and levels without tracks lay nothing.
	/// </summary>
	public SegmentResult? BuildTrack( OriginalObject station, (int X, int Y) from, (int X, int Y) to )
	{
		if ( IsReadOnlyVisit || waterTracks == null || !trackStations.ContainsKey( station ) )
		{
			LastActionMessage = "Cannot build a track here.";
			return null;
		}
		var builder = BuilderOf( station )!;
		var result = new TrackLineWriter( builder, () => RewalkTrack( station ), builder.OwnerOf( trackStations[station] ) ).Commit( from, to );
		LastActionMessage = result.Completed ? $"Built {result.Built.Count} track tiles for ${result.Charged}." : $"Cannot build a track here: {result.StoppedBy}.";
		return result;
	}

	/// <summary>Updates the track renderers (one per track family): tile instances after a layer change, vehicles at their positions.</summary>
	private void UpdateTrackRendering()
	{
		if ( trackRenderFailed || waterTracks == null )
			return;
		try
		{
			foreach ( var family in TrackFamilies )
			{
				var first = trackStations.Keys.FirstOrDefault( item => !item.IsDeleted && item.Runtime.Bump?.Template.Family == family );
				if ( first == null )
				{
					if ( trackRenderers.Remove( family, out var stale ) )
						stale.Dispose();
					continue;
				}
				if ( !trackRenderers.TryGetValue( family, out var renderer ) )
				{
					var entry = first.Entry;
					var kit = TrackPieceKit.Load( entry.FileSystem, entry.ArchivePath, family );
					renderer = new TrackRenderer( entry, kit, () => Objects.Grid.Origin, ( x, y ) => Objects.Grid.GetCellHeight( x, y ) );
					trackRenderers[family] = renderer;
				}
				renderer.Update( waterTracks.Tracks, Objects.ScriptWorld.Bumpers.Controllers.Where( controller => controller.Template.Family == family ) );
			}
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or IOException or NotSupportedException )
		{
			trackRenderFailed = true;
			Log.Warning( $"The track renderer was disabled: {exception.Message}" );
		}
	}

	/// <summary>
	/// Places a water ride (<paramref name="trackType"/> 2) or a go-kart ride (1) with a small closed circuit near the park's initial path and points the park
	/// camera at it (the debug flags <c>--debug-water-track</c> and <c>--debug-kart-track</c>, for captures; the park pays for the ride when it is available
	/// and for the tiles). Null when no site has room.
	/// </summary>
	// [EXT:debug-water-track] developer/screenshot helper, not original behaviour: it clicks the track tool for the player
	internal OriginalObject? BuildDemoTrackRide( int trackType = 2 )
	{
		if ( IsReadOnlyVisit || waterTracks == null || Guests == null || OriginalPark == null )
			return null;
		var entry = Objects.Catalog.Buildable.Where( item => item.Settings.GetInt( "Bumper.WhichTrackType" ) == trackType ).OrderBy( item => item.InfoId ).FirstOrDefault();
		if ( entry == null )
			return null;
		var grid = Objects.Grid;
		bool Free( int x, int y ) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height && Objects.FindAt( x, y ) == null
			&& grid.CheckTerrain( x, y ) is OriginalPlacementResult.Allowed && Guests.Grid.Cells.RawTypeAt( x, y ) == 0;
		var near = Guests.Grid.Cells.Width > 0 ? (X: Guests.Grid.CountX / 2, Y: Guests.Grid.CountY / 2) : (X: 0, Y: 0);
		var path = Enumerable.Range( 0, Guests.Grid.CountX * Guests.Grid.CountY ).Select( index => (X: index % Guests.Grid.CountX, Y: index / Guests.Grid.CountX) ).Where( cell => Guests.Grid.IsWalkable( cell.X, cell.Y ) ).ToArray();
		if ( path.Length > 0 )
			near = (path.Sum( cell => cell.X ) / path.Length, path.Sum( cell => cell.Y ) / path.Length);
		foreach ( var cell in Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - near.X ) + Math.Abs( cell.Y - near.Y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			var anchor = GetCentredAnchor( entry, cell.X, cell.Y, 0 );
			if ( Objects.Check( entry, anchor.X, anchor.Y, 0 ) != OriginalPlacementResult.Allowed
				|| !Enumerable.Range( anchor.X - 2, 9 ).All( x => Enumerable.Range( anchor.Y - 1, 8 ).All( y => Free( x, y ) ) ) )
				continue;
			var station = PlaceObject( entry, cell.X, cell.Y, 0 ) ?? Objects.TryPlace( entry, anchor.X, anchor.Y, 0, out _ );
			if ( station == null || !trackStations.TryGetValue( station, out var layout ) || !EnterTrackTool( station ) )
				return null;
			foreach ( var offset in new (int X, int Y)[] { (4, 4), (-2, 4), (-2, 0) } )
				CellTool.Click( (layout.First.X + offset.X, layout.First.Y + offset.Y) );
			CellTool.Cancel();
			if ( trackType == 1 && station.Runtime.Bump is { IsTrackValid: true } karts )
			{
				// the kart script waits for real guests, so the capture helper plays its part: three karts with a rider each, on a long race
				karts.Command( 7, 0 );
				for ( var car = 0; car < 3; car++ )
				{
					karts.Command( 1, 9000 + car );
					karts.Command( 4, 0 );
				}
				karts.Command( 14, 20 );
				karts.Command( 3, 0 );
			}
			var centre = OriginalParkPlacement.GetCellCenter( OriginalPark.Heightfield, layout.First.X + 2, layout.First.Y + 2 );
			(Camera.Mode as ParkCameraMode)?.SetPose( new Vector3( centre.X, centre.Y, 0 ), 22, -20 );
			Log.Trace( $"--debug-track: {entry.DisplayName} at {layout.First} with a {(station.Runtime.Bump!.IsTrackValid ? "valid" : "invalid")} circuit of {BuilderOf( station )!.Tiles().Count()} tiles; camera on engine ({centre.X:0.#}, {centre.Y:0.#})." );
			return station;
		}
		return null;
	}

	/// <summary>The water ride of <see cref="BuildDemoTrackRide"/>.</summary>
	internal OriginalObject? BuildDemoWaterRide() => BuildDemoTrackRide( 2 );

	// ---- Park-resume snapshot -----------------------------------------------------------------------

	/// <summary>The park-resume track part, or null when the park holds no track tile and no controller. Call between simulation ticks.</summary>
	internal TrackWorldSaveData? CaptureTracksSnapshot() => Guests == null ? null : TrackSave.Capture( Guests.Grid.Cells, Objects.ScriptWorld.Bumpers,
		LiveWaterRides().Select( item => (item.Runtime.AttractionId, item.Runtime.LiveBump!) ) );

	/// <summary>The placed rides that carry a live controller: the one rule the track capture and restore share (a faulted or removed ride has none).</summary>
	private IEnumerable<OriginalObject> LiveWaterRides() => Objects.Objects.Where( item => !item.IsDeleted && item.Runtime.LiveBump != null );

	/// <summary>
	/// Restores the saved tracks into this candidate level, after the objects were placed and their runtimes restored: the cells go back into the layer, each
	/// station's circuit is walked and checked against the saved sections and validity, then the controller state and boats are restored. Throws
	/// <see cref="InvalidDataException"/> on any mismatch.
	/// </summary>
	internal void RestoreTracksSnapshot( TrackWorldSaveData? saved )
	{
		var controllers = LiveWaterRides().ToList();
		// a save written before the kart rides had controllers holds none for a kart station: its track is empty, so the station tiles are laid and the fresh controller kept
		var legacy = controllers.Where( item => item.Runtime.Bump!.Template is { IsKart: true } or { IsBumper: true } && saved?.Controllers.Any( entry => entry.AttractionId == item.Runtime.AttractionId ) != true ).ToList();
		if ( saved == null )
		{
			if ( controllers.Any( item => item.Runtime.LiveBump!.Vehicles.Count > 0 ) )
				throw new InvalidDataException( "The park holds boats but the save carries no track part." );
			if ( controllers.Count != legacy.Count )
				throw new InvalidDataException( "The save carries no track part, but the park places water rides." );
			MigrateLegacyKarts( legacy );
			return;
		}
		if ( Guests == null || waterTracks == null )
			throw new InvalidDataException( "The saved park has tracks, but this level has no track layer." );
		TrackSave.RestoreCells( Guests.Grid.Cells, saved );
		foreach ( var entry in saved.Controllers )
		{
			var station = controllers.FirstOrDefault( item => item.Runtime.AttractionId == entry.AttractionId )
				?? throw new InvalidDataException( $"The save holds a track controller for attraction {entry.AttractionId}, which this park does not place as a track ride." );
			var bump = station.Runtime.Bump!;
			if ( bump.Type != entry.Type )
				throw new InvalidDataException( $"Attraction {entry.AttractionId}'s controller type {bump.Type} is not the saved {entry.Type}." );
			if ( bump.Template.IsBumper )
			{
				// a bumper ride has no circuit: the arena is rebuilt from the placement, the saved state goes straight in
				if ( entry.Sections.Count != 0 )
					throw new InvalidDataException( $"Attraction {entry.AttractionId} is a bumper ride but the save holds track sections for it." );
				bump.Restore( entry );
				station.Runtime.ResumeBumpEffects();
				continue;
			}
			var circuit = RewalkTrack( station ) ?? throw new InvalidDataException( $"Attraction {entry.AttractionId} has no track station." );
			if ( circuit.IsValid != entry.IsTrackValid )
				throw new InvalidDataException( $"Attraction {entry.AttractionId}'s circuit walks {(circuit.IsValid ? "valid" : "invalid")}, the save says {(entry.IsTrackValid ? "valid" : "invalid")}." );
			var rebuilt = bump.Sections.Select( section => new TrackSectionSaveData( section.Kind, section.NoBuoy, section.X, section.Y, section.Segment ) ).ToList();
			if ( !rebuilt.SequenceEqual( entry.Sections ) )
				throw new InvalidDataException( $"Attraction {entry.AttractionId}'s walked sections do not match the saved ones." );
			bump.Restore( entry );
			station.Runtime.ResumeBumpEffects();
		}
		if ( controllers.Count - legacy.Count != saved.Controllers.Count )
			throw new InvalidDataException( $"The save holds {saved.Controllers.Count} track controllers, but this park places {controllers.Count - legacy.Count} rides that need one." );
		MigrateLegacyKarts( legacy );
		Objects.ScriptWorld.Bumpers.CosmeticState = saved.CosmeticState;
	}

	/// <summary>Lays the station tiles of kart rides that an older save carried without a controller and walks their (empty) circuit.</summary>
	// [APPROX:TRACK-027] a save from before the go-kart or bumper-car controllers (no controller for such a ride) restores its rides as freshly placed ones: station tiles and no track for karts, an arena for bumper cars, a closed controller; the guests a bumper ride still held go to its leaving list
	private void MigrateLegacyKarts( IEnumerable<OriginalObject> legacy )
	{
		foreach ( var item in legacy )
		{
			if ( item.Runtime.Bump is { Template.IsBumper: true } bumper )
			{
				// a bumper ride of an older save had no controller, so its riders (stuck aboard with nothing to release them) are handed to the new controller's leaving list: the script's BUMP 2 returns them one by one
				bumper.Leaving.AddRange( item.Visitors.Riders.Where( guest => !bumper.Leaving.Contains( guest ) ) );
				continue;
			}
			if ( !trackStations.TryGetValue( item, out var station ) )
				continue;
			BuilderOf( item )!.PlaceStation( station );
			RewalkTrack( item );
		}
	}
}

/// <summary>The riders of the go-karts as head sources: every kart's riders hang from the head dummies of the kart mesh (<see cref="TrackRenderer.CollectHeads"/>).</summary>
internal sealed class TrackHeadSources : IHeadSourceProvider
{
	private readonly Func<IEnumerable<TrackRenderer>> renderers;

	public TrackHeadSources( Func<IEnumerable<TrackRenderer>> renderers ) => this.renderers = renderers;

	public void Collect( List<HeadSource> sources, int? onlyAttraction )
	{
		foreach ( var renderer in renderers() )
		{
			if ( renderer.Family == TrackFamily.Kart )
				renderer.CollectHeads( sources, onlyAttraction );
		}
	}
}
