namespace OpenTPW;

/// <summary>Kart and water ride tracks (docs/TRACKS.md): the track tool, the stations' tiles and circuit walks, the controllers' state and the track renderer.</summary>
public partial class Level
{
	private TrackBuilder? waterTracks;
	private TrackBuilder? kartTracks;
	private readonly Dictionary<OriginalObject, TrackStation> trackStations = new();
	private readonly Dictionary<TrackFamily, TrackRenderer> trackRenderers = new();
	private bool trackRenderFailed;
	private readonly TrackPieceAnimations trackPieceAnimations = new();
	private readonly Dictionary<OriginalObject, Action<bool>> waterPieceHandlers = new();
	private static readonly TrackFamily[] TrackFamilies = { TrackFamily.Water, TrackFamily.Kart, TrackFamily.Bumper };

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
	/// <summary>Which track pieces play their water clip (presentation only).</summary>
	public TrackPieceAnimations TrackPieceAnimations => trackPieceAnimations;
	/// <summary>The renderer of one track family (null until a station of the family exists and the first frame ran).</summary>
	internal TrackRenderer? TrackRendererOf( TrackFamily family ) => trackRenderers.GetValueOrDefault( family );

	private void SetupTracks()
	{
		if ( Guests == null )
			return;
		waterTracks = new TrackBuilder( Guests.Grid.Cells, TrackFamily.Water, () => Park?.Economy, Objects.Grid, IsPathBlocked );
		kartTracks = new TrackBuilder( Guests.Grid.Cells, TrackFamily.Kart, () => Park?.Economy, Objects.Grid, IsPathBlocked );
		Objects.ObjectPlaced += OnTrackStationPlaced;
		Objects.ObjectRemoving += OnTrackStationRemoving;
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
		SetTrackStation( item, bump, station );
		if ( !waterPieceHandlers.ContainsKey( item ) )
		{
			Action<bool> handler = play => TriggerTrackPieces( item, play );
			waterPieceHandlers[item] = handler;
			bump.WaterPiecesTriggered += handler;
		}
		// A restored park carries its tiles and boats in the save's track part; the restore walks the circuit then.
		if ( resumeState != null )
			return;
		BuilderOf( item )!.PlaceStation( station );
		RewalkTrack( item, TrackWalkCause.Placement );
	}

	/// <summary>Records a placed ride's station and puts its controller's reference there (<see cref="TrackStationReference"/>; a saved legacy ride's is the first tile's centre).</summary>
	private void SetTrackStation( OriginalObject item, BumpController bump, TrackStation station )
	{
		trackStations[item] = station;
		bump.LegacyStationLayout = station.Legacy;
		var reference = station.Legacy
			? TrackStation.Centre( station.First )
			: TrackStationReference( item.Entry.Settings, item.Placement.X, item.Placement.Y, item.Placement.Rotation );
		bump.SetStation( reference.X, reference.Y, station.Heading );
	}

	/// <summary>
	/// The controller reference of a kart or water ride in section units: the cell between the station's two tiles on their centre line, where boats launch.
	/// </summary>
	// [BIN:TPW-EXE:0x00545890 track controller] the kart and water types store the creation cell as the station reference (+0x74/+0x78) without the arena's 0x600 (0x545F13, 0x5460D9); 0x529E10 hands it anchor + Bumper.*Adjust + the rotation's fixed offset, times 0xC00
	public static (int X, int Y) TrackStationReference( ObjectSettings settings, int anchorX, int anchorY, int rotation )
	{
		var (cellX, cellY) = ReferenceCell( settings, anchorX, anchorY, rotation );
		return (cellX * TrackStation.CellUnits, cellY * TrackStation.CellUnits);
	}

	private static (int X, int Y) ReferenceCell( ObjectSettings settings, int anchorX, int anchorY, int rotation )
	{
		ArgumentNullException.ThrowIfNull( settings );
		var (side, fixedX, fixedY) = rotation switch
		{
			0 => ("North", 4, 1),
			90 => ("East", 1, -5),
			180 => ("South", -5, -2),
			270 => ("West", -2, 4),
			_ => throw new ArgumentOutOfRangeException( nameof( rotation ), "Rotations are multiples of 90 degrees." )
		};
		return (anchorX + settings.GetInt( $"Bumper.{side}XAdjust" ) + fixedX, anchorY + settings.GetInt( $"Bumper.{side}YAdjust" ) + fixedY);
	}

	/// <summary>
	/// The centre of a bumper ride's arena in section units, the original's controller reference: the placement anchor plus the type's
	/// <c>Bumper.*Adjust</c> offset and a fixed offset for the rotation, at the middle of that cell. For the shipped bumper rides (5 × 5,
	/// Adjust North (−2, 1), East (1, 3), South (3, 0), West (0, −2)) that is the middle of the footprint, local cell (2, 2).
	/// </summary>
	internal static (int X, int Y) BumperArenaCentre( OriginalObject item ) => BumperArenaCentre( item.Entry.Settings, item.Placement.X, item.Placement.Y, item.Placement.Rotation );

	/// <inheritdoc cref="BumperArenaCentre(OriginalObject)"/>
	// [BIN:TPW-EXE:0x00529E10 ride placement] the cursor cell is 0x818C3C = (anchorY << 7) + anchorX + 1, written at 0x528E12 from the build click's anchor (0x528B44, GetCentredAnchor) just before the call at 0x528E19;
	// rotation 0/90/180/270 adds the type's +0xA4/+0xA8, +0xAC/+0xB0, +0xB4/+0xB8, +0xBC/+0xC0 (0x529E53–0x529ED7; the Bumper key table at 0x74541C lists WhichTrackType, BumperType, then North, East, South and West X/YAdjust, so +0x9C is WhichTrackType) and then (+4, +1), (+1, −5), (−5, −2), (−2, +4) (0x529EDF–0x529F2B),
	// scales by 307.2 × 10 = 0xC00 (0x529F2F) and hands the cell to the arena builder 0x545890, which adds 0x600 on both axes for the bumper types (0x545CA8, 0x545D1E, 0x545D91)
	public static (int X, int Y) BumperArenaCentre( ObjectSettings settings, int anchorX, int anchorY, int rotation )
	{
		var (cellX, cellY) = ReferenceCell( settings, anchorX, anchorY, rotation );
		return (cellX * TrackStation.CellUnits + TrackStation.CellUnits / 2, cellY * TrackStation.CellUnits + TrackStation.CellUnits / 2);
	}

	private void OnTrackStationRemoved( OriginalObject item )
	{
		if ( waterTracks == null )
			return;
		if ( waterPieceHandlers.Remove( item, out var handler ) && item.Runtime.Bump is { } bump )
			bump.WaterPiecesTriggered -= handler;
		if ( !trackStations.Remove( item, out var station ) )
			return;
		if ( TrackToolStation == item )
			CellTool.Cancel();
		trackToolStation = null;
		DemolishTrack( station, BuilderOf( item )! );
		item.Runtime.Bump?.Rebuild( TrackCircuit.Invalid( "removed" ) );
	}

	/// <summary>
	/// The money side of a kart or water ride's demolition, while its tiles are still laid: every tile laid for the ride refunds the track cell cost × the ride's
	/// scrap percentage / 100 and the demolition then charges twice that cost × the percentage / 100 once. A park load or clear removes nothing through here.
	/// </summary>
	// [BIN:STP-PPC:0x100748A0 ride demolition] the demolition replays the queue tool and the kart/water track tool from both station sides to clear the circuit, with one Spend of 2 × cell cost × 0x100E2424() / 100
	// [BIN:TPW-EXE:0x00527EE0 ride demolition] the replays run with flag 0x81B46C = 1, so ClearCell 0x53B280 refunds every cleared kart or water tile (0x53B54A..0x53B57F: a tile whose overlap word +0x20 went negative is cleared and refunds unless it is an end of the replayed line while the flag is clear; the flag is set, the type 0x19 and online cases aside), and 0x5280F0..0x528179 then debits twice the cell cost × the scrap percentage / 100 once
	// [APPROX:TRACK-007] every tile the ride owns refunds once, where the original refunds and clears only what its two walks from the station reach (0x530830 and 0x530B70 collect the circuit's corner points, at most 0x401, and 0x52FE50 replays the lines between them), so a tile cut off from its station stays and refunds nothing there; a tile written again refunds on its last clearing visit
	private void OnTrackStationRemoving( OriginalObject item )
	{
		if ( waterTracks == null || !trackStations.TryGetValue( item, out var station ) || BuilderOf( item ) is not { } builder )
			return;
		// both the refunds and the charge run only for a type with a queue (Info.HasQueue, def +0x40: 0x527F5B..0x52817E)
		if ( !item.Entry.HasQueue || Park?.Economy is not { } economy || GetEconomyInstance( item ) is not int instance )
			return;
		var tiles = builder.OwnedTileCount( builder.OwnerOf( station ) );
		for ( var tile = 0; tile < tiles; tile++ )
			economy.RefundTrackTile( builder.Purchase, instance );
		economy.ChargeTrackDemolition( builder.Purchase, instance );
	}

	/// <summary>
	/// Removes the track of a station: every tile laid for its ride (TRACK-021), then the station tiles. The money was settled by <see cref="OnTrackStationRemoving"/>.
	/// </summary>
	private void DemolishTrack( TrackStation station, TrackBuilder builder )
	{
		// exactly the tiles laid for this ride go, so a neighbouring ride's circuit that touches it stays
		builder.RemoveOwned( builder.OwnerOf( station ) );
		builder.RemoveStation( station );
	}

	/// <summary>
	/// Walks the circuit of <paramref name="item"/> again after a track edit: the controller takes the new sections and validity (the boats leave and the
	/// ride closes, as a re-walk does in the original), and a valid circuit ends the track tool and starts the queue tool. <paramref name="cause"/> says which
	/// of the original's walks this is, for the pieces' water clip: the ride's placement starts it on a valid circuit, a track line stops it.
	/// </summary>
	public TrackCircuit? RewalkTrack( OriginalObject item, TrackWalkCause cause = TrackWalkCause.Restore )
	{
		if ( waterTracks == null || !trackStations.TryGetValue( item, out var station ) || item.Runtime.Bump is not { } bump )
			return null;
		var circuit = TrackCircuitWalker.WalkAndRefresh( BuilderOf( item )!, station );
		bump.Rebuild( circuit );
		if ( TrackPieceAnimations.ForWalk( cause, circuit.IsValid ) is { } pieces )
			trackPieceAnimations.TriggerTrack( BuilderOf( item )!.Tracks, station, pieces.Play, pieces.Backward );
		if ( !circuit.IsValid )
			return circuit;
		LastActionMessage = $"The track is complete: {circuit.Steps.Count} tiles.";
		// [BIN:TPW-EXE:0x0052A970 circuit walk] a valid circuit closes the tool; the walk itself posts no advisor or event message (it calls no message builder 0x0059B590)
		// [BIN:TPW-EXE:0x00526D41 track tool help] the kart and water tool posts tool help 0xCC (204) only for a click with no line start (0x0081EDE4/E8 = −1), which sets the start (0x00525A1C → 0x00526CB3); the placement presets the start at the station's exit (0x0052A1A0), so it is reached only when the tool stays on without one after a line closes with exactly Ctrl and no queue to follow (0x00526FB3, UI-031); 0xB8, 0xD6 and 0x151 are the path and queue tool's help (0x005272F5, 0x00527351, 0x00527620)
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
		CellTool.Enter( new TrackLineWriter( builder, () => RewalkTrack( station, TrackWalkCause.Line ), builder.OwnerOf( layout ) ), layout.Exit );
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
		var result = new TrackLineWriter( builder, () => RewalkTrack( station, TrackWalkCause.Line ), builder.OwnerOf( trackStations[station] ) ).Commit( from, to );
		LastActionMessage = result.Completed ? $"Built {result.Built.Count} track tiles for ${result.Charged}." : $"Cannot build a track here: {result.StoppedBy}.";
		return result;
	}

	/// <summary>BUMP 17 changed a ride's water word: the pieces of its track start or stop from both station sides.</summary>
	private void TriggerTrackPieces( OriginalObject item, bool play )
	{
		if ( trackStations.TryGetValue( item, out var station ) && BuilderOf( item ) is { } builder )
			trackPieceAnimations.TriggerTrack( builder.Tracks, station, play, backward: true );
	}

	/// <summary>Updates the track renderers (one per track family): tile instances after a layer change, vehicles at their positions, piece clips <paramref name="seconds"/> on.</summary>
	private void UpdateTrackRendering( double seconds = 0 )
	{
		if ( trackRenderFailed || waterTracks == null )
			return;
		trackPieceAnimations.Prune( waterTracks.Tracks );
		trackPieceAnimations.Advance( seconds );
		try
		{
			foreach ( var family in TrackFamilies )
			{
				// the bumper cars have no station tiles: the renderer of their family draws the cars of every bumper ride of the level
				var first = family == TrackFamily.Bumper
					? Objects.Objects.FirstOrDefault( item => !item.IsDeleted && item.Runtime.Bump?.Template.IsBumper == true )
					: trackStations.Keys.FirstOrDefault( item => !item.IsDeleted && item.Runtime.Bump?.Template.Family == family );
				if ( first == null )
				{
					if ( trackRenderers.Remove( family, out var stale ) )
						stale.Dispose();
					continue;
				}
				if ( !trackRenderers.TryGetValue( family, out var renderer ) )
				{
					var entry = first.Entry;
					var kit = family == TrackFamily.Bumper ? null : TrackPieceKit.Load( entry.FileSystem, entry.ArchivePath, family );
					renderer = new TrackRenderer( entry, kit, () => Objects.Grid.Origin, ( x, y ) => Objects.Grid.GetCellHeight( x, y ), ( x, y ) => Objects.Grid.GetCornerHeight( x, y ), ( x, y ) => Objects.Grid.GetTerrainHeight( x, y ) );
					trackRenderers[family] = renderer;
				}
				renderer.Update( waterTracks.Tracks, Objects.ScriptWorld.Bumpers.Controllers.Where( controller => controller.Template.RenderFamily == family ), trackPieceAnimations );
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
			&& grid.CheckTerrain( x, y ) is OriginalPlacementResult.Allowed && Guests.Grid.Cells.RawTypeAt( x, y ) == 0 && !Guests.Grid.Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );
		var near = Guests.Grid.Cells.Width > 0 ? (X: Guests.Grid.CountX / 2, Y: Guests.Grid.CountY / 2) : (X: 0, Y: 0);
		var path = Enumerable.Range( 0, Guests.Grid.CountX * Guests.Grid.CountY ).Select( index => (X: index % Guests.Grid.CountX, Y: index / Guests.Grid.CountX) ).Where( cell => Guests.Grid.IsWalkable( cell.X, cell.Y ) ).ToArray();
		if ( path.Length > 0 )
			near = (path.Sum( cell => cell.X ) / path.Length, path.Sum( cell => cell.Y ) / path.Length);
		foreach ( var cell in Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - near.X ) + Math.Abs( cell.Y - near.Y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			var anchor = GetCentredAnchor( entry, cell.X, cell.Y, 0 );
			if ( Objects.CheckFree( entry, anchor.X, anchor.Y, 0 ) != OriginalPlacementResult.Allowed
				|| !Enumerable.Range( anchor.X - 2, 9 ).All( x => Enumerable.Range( anchor.Y - 1, 8 ).All( y => Free( x, y ) ) ) )
				continue;
			var station = PlaceObject( entry, cell.X, cell.Y, 0 ) ?? Objects.TryPlace( entry, anchor.X, anchor.Y, 0, out _ );
			if ( station == null || !trackStations.TryGetValue( station, out var layout ) || !EnterTrackTool( station ) )
				return null;
			// a built ride with a queue starts closed (RIDES-015, 0x4DB712): the capture helper opens it as the player's door button would
			station.Open();
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
		// a controller saved before the station followed the original's line (no station layout field) keeps its old station tiles and reference
		foreach ( var entry in saved.Controllers.Where( entry => entry.StationLayout == 0 ) )
		{
			if ( controllers.FirstOrDefault( item => item.Runtime.AttractionId == entry.AttractionId ) is { } ride && ride.Runtime.Bump is { Template.IsBumper: false } track )
				SetTrackStation( ride, track, TrackStation.FromPlacementLegacy( ride.Placement.X, ride.Placement.Y, ride.Placement.Rotation ) );
		}
		TrackSave.RestoreCells( Guests.Grid.Cells, saved );
		var module = Objects.ScriptWorld.Bumpers;
		// a save of the shared module (it carries the module clock and every controller's object slots) is trusted: saved pool slots, verdicts and object slots
		var shared = saved.FrameCount != null;
		module.SharedSaveRestore = shared;
		try
		{
			RestoreTrackControllers( saved, controllers, shared );
		}
		finally
		{
			module.SharedSaveRestore = false;
		}
		if ( controllers.Count - legacy.Count != saved.Controllers.Count )
			throw new InvalidDataException( $"The save holds {saved.Controllers.Count} track controllers, but this park places {controllers.Count - legacy.Count} rides that need one." );
		MigrateLegacyKarts( legacy );
		TrackSave.RestoreModule( module, saved, saved.Controllers
			.Select( entry => (controllers.First( item => item.Runtime.AttractionId == entry.AttractionId ).Runtime.Bump!, (IReadOnlyList<int>?)entry.ObjectSlots) ).ToList() );
	}

	private void RestoreTrackControllers( TrackWorldSaveData saved, List<OriginalObject> controllers, bool shared )
	{
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
				if ( shared )
					bump.RestoreArena( entry.ObjectSlots is { Count: > 0 }, entry.BuoyCount );
				bump.Restore( entry );
				station.Runtime.ResumeBumpEffects();
				continue;
			}
			var circuit = (shared ? RewalkForRestore( station, entry ) : RewalkTrack( station ))
				?? throw new InvalidDataException( $"Attraction {entry.AttractionId} has no track station." );
			// a shared-module save may hold a ride the shared pools refused (saved invalid, walking valid): the restore keeps the refusal
			if ( shared ? entry.IsTrackValid && !circuit.IsValid : circuit.IsValid != entry.IsTrackValid )
				throw new InvalidDataException( $"Attraction {entry.AttractionId}'s circuit walks {(circuit.IsValid ? "valid" : "invalid")}, the save says {(entry.IsTrackValid ? "valid" : "invalid")}." );
			var rebuilt = bump.Sections.Select( section => new TrackSectionSaveData( section.Kind, section.NoBuoy, section.X, section.Y, section.Segment ) ).ToList();
			if ( !rebuilt.SequenceEqual( entry.Sections ) )
				throw new InvalidDataException( $"Attraction {entry.AttractionId}'s walked sections do not match the saved ones." );
			bump.Restore( entry );
			station.Runtime.ResumeBumpEffects();
		}
	}

	// [APPROX:TRACK-032] the controller of a saved ride is rebuilt from its circuit and the saved verdict, where tp.exe's loader 0x543560 restores it from the save stream and the ride's handle +0x28 points at it (evidence needed: the record layout read by 0x543560)
	/// <summary>Walks a station's circuit for a restore of a shared-module save and rebuilds it with the saved verdict (<see cref="BumpController.RestoreCircuit"/>).</summary>
	private TrackCircuit? RewalkForRestore( OriginalObject item, BumpControllerSaveData entry )
	{
		if ( waterTracks == null || !trackStations.TryGetValue( item, out var station ) || item.Runtime.Bump is not { } bump )
			return null;
		var circuit = TrackCircuitWalker.WalkAndRefresh( BuilderOf( item )!, station );
		bump.RestoreCircuit( circuit, entry.IsTrackValid, entry.ObjectSlots?.Count ?? -1, entry.BuoyCount );
		return circuit;
	}

	/// <summary>Lays the station tiles of kart rides that an older save carried without a controller and walks their (empty) circuit.</summary>
	// [EXT:legacy-kart-save] a save from before the go-kart or bumper-car controllers (no controller for such a ride) restores its rides as freshly placed ones: station tiles and no track for karts, an arena for bumper cars, a closed controller; the guests a bumper ride still held go to its leaving list (save compatibility, no original rule)
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
			if ( renderer.Family is TrackFamily.Kart or TrackFamily.Bumper )
				renderer.CollectHeads( sources, onlyAttraction );
		}
	}
}
