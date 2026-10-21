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
	public OriginalObject? TrackToolStation => CellTool.Writer is ParkCommandTrackWriter or TrackLineWriter or TrackEditWriter ? trackToolStation : null;
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
		Objects.ObjectRemoving += OnAddOnRemoving;
		Objects.ObjectRemoved += OnAddOnRemoved;
		foreach ( var item in Objects.Objects )
			RegisterTrackStation( item, fresh: false );
	}

	private void OnTrackStationPlaced( OriginalObject item ) => RegisterTrackStation( item, fresh: true );

	private void RegisterTrackStation( OriginalObject item, bool fresh )
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
		if ( fresh )
			BuilderOf( item )!.PlaceInitialStation( station );
		else
			BuilderOf( item )!.PlaceStation( station );
		// [EXT:track-placement-walk] the original's placement lays the station (0x0052A1A0 from 0x00529E10) and walks nothing; OpenTPW walks the fresh station so its controller has the station's sections and verdict at once, and the pieces take the add-on walk's rule (TrackPieceAnimations.ForWalk), which on the station's open circuit stops clips none of them plays yet
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
	/// The demolition of a kart or water ride's track while its tiles are still laid: the two replays clear the chains of tiles from both sides of the station, each cleared tile refunds
	/// the track cell cost × the ride's scrap percentage / 100, and the demolition then charges twice that cost × the percentage / 100 once. A park load or clear removes nothing through here.
	/// </summary>
	// [BIN:STP-PPC:0x100748A0 ride demolition] the demolition replays the queue tool and the kart/water track tool from both station sides to clear the circuit, with one Spend of 2 × cell cost × 0x100E2424() / 100
	// [BIN:TPW-EXE:0x00527EE0 ride demolition] the replays run with flag 0x81B46C = 1, so ClearCell 0x53B280 refunds every cleared kart or water tile (0x53B54A..0x53B57F: a tile whose overlap word +0x20 went negative is cleared and refunds unless it is an end of the replayed line while the flag is clear; the flag is set, the type 0x19 and online cases aside), and 0x5280F0..0x528179 then debits twice the cell cost × the scrap percentage / 100 once
	// [BIN:TPW-EXE:0x00527EE0 demolition replays] the two replays of the track tool clear the chain of tiles from the station's exit to the open end of the track (0x530830, then the lines between its corner points) and the chain from the tile before the station (0x530B70), as TrackEditor.Demolish does: a tile no chain reaches stays in the park and refunds nothing, a tile a chain passes with a pass counter left (a junction) loses one pass only
	// [APPROX:TRACK-007] the replay clears each line between two corner points through the click handler's mode 0x35, whose per-tile arm writes for the line's step (0x53B280 with the line's (±2, 0) or (0, ±2): the arm the line used goes, the other arm stays when a pass remains, a corner's second arm after its second visit) are modelled as one clear per tile in chain order; a two-line full crossing whose laid flow includes the chain's heading takes the line step's rule (the chain's link bit and that axis's arms go, TrackEditor.Replay), but any other junction keeps the pass rule of the cut (0x53B280 with (−1, −1)) for its arms and link, not the line step's
	private void OnTrackStationRemoving( OriginalObject item )
	{
		if ( waterTracks == null || !trackStations.TryGetValue( item, out var station ) || BuilderOf( item ) is not { } builder )
			return;
		// both the replays and the charge run only for a type with a queue (Info.HasQueue, def +0x40: 0x527F5B..0x52817E)
		if ( !item.Entry.HasQueue )
			return;
		// the replays do not depend on the economy (a level without one, the sandbox, still clears its circuit, or the tiles would stay with no way to take them away); only the refunds and the charge do
		foreach ( var _ in TrackEditorOf( item, station, builder ).Demolish() )
		{
			// the refund of each cleared tile is the editor's hook (TrackEditHooksOf)
		}
		if ( Park?.Economy is { } economy && GetEconomyInstance( item ) is int instance )
			economy.ChargeTrackDemolition( builder.Purchase, instance );
	}

	private TrackEditor TrackEditorOf( OriginalObject item, TrackStation station, TrackBuilder builder ) => new( builder, station, TrackEditHooksOf( item, builder ) );

	/// <summary>
	/// Removes the station tiles of a ride; the tiles its replays cleared are gone already (<see cref="OnTrackStationRemoving"/>) and a tile no replay reached stays, as in the original.
	/// A type without a queue runs no replay and leaves its whole track.
	/// </summary>
	private void DemolishTrack( TrackStation station, TrackBuilder builder )
	{
		builder.RemoveStation( station );
	}

	/// <summary>
	/// Walks the circuit of <paramref name="item"/> again after a track edit: the controller takes the new sections and validity (the boats leave and the
	/// ride closes, as a re-walk does in the original), and a valid circuit follows the queue or ends the tool, except the live exactly-Ctrl no-queue restart. <paramref name="cause"/> says which
	/// of the original's walks this is, for the pieces' water clip: the ride's placement starts it on a valid circuit, a track line stops it.
	/// </summary>
	public TrackCircuit? RewalkTrack( OriginalObject item, TrackWalkCause cause = TrackWalkCause.Restore )
	{
		var circuit = WalkTrack( item, cause );
		if ( circuit is { IsValid: true } )
			ContinueTrackTool( item, cause );
		return circuit;
	}

	/// <summary>
	/// The world's part of <see cref="RewalkTrack"/>: the walk, the controller's sections, the add-ons, the pieces and the door. A track command walks this way on every peer
	/// (<see cref="ExecuteTrackLine"/>, <see cref="ExecuteTrackCut"/>); the tool's step after a valid walk is the issuer's (<see cref="ContinueTrackTool"/>).
	/// </summary>
	internal TrackCircuit? WalkTrack( OriginalObject item, TrackWalkCause cause )
	{
		if ( waterTracks == null || !trackStations.TryGetValue( item, out var station ) || item.Runtime.Bump is not { } bump )
			return null;
		var circuit = TrackCircuitWalker.WalkAndRefresh( BuilderOf( item )!, station );
		bump.Rebuild( circuit );
		LinkAddOns( item, circuit );
		if ( TrackPieceAnimations.ForWalk( cause, circuit.IsValid ) is { } pieces )
			trackPieceAnimations.TriggerTrack( BuilderOf( item )!.Tracks, station, pieces.Play, pieces.Backward );
		if ( cause is TrackWalkCause.Line or TrackWalkCause.Edit )
			UpdateTrackOpenState( item, bump.IsTrackValid );
		if ( !circuit.IsValid )
			return circuit;
		LastActionMessage = $"The track is complete: {circuit.Steps.Count} tiles.";
		return circuit;
	}

	/// <summary>
	/// The track tool's step after a walk that found <paramref name="item"/>'s circuit valid: the tool on that ride ends and the queue tool follows the ride's queue, or, for a live line click with exactly Ctrl and
	/// no queue to follow, the line tool restarts without a start. The issuer's machine alone takes it ([EXT:online-coop-commands]: a peer's or a replayed command moves no tool).
	/// </summary>
	internal void ContinueTrackTool( OriginalObject item, TrackWalkCause cause )
	{
		// [BIN:TPW-EXE:0x0052A970 circuit walk] a valid circuit closes the tool; the walk itself posts no advisor or event message (it calls no message builder 0x0059B590)
		// [BIN:TPW-EXE:0x00526D41 track tool help] the kart and water tool posts tool help 0xCC (204) only for a click with no line start (0x0081EDE4/E8 = −1), which sets the start (0x00525A1C → 0x00526CB3); the placement presets the start at the station's exit (0x0052A1A0), so it is reached only when the tool stays on without one after a line closes with exactly Ctrl and no queue to follow (0x00526FB3, UI-031); 0xB8, 0xD6 and 0x151 are the path and queue tool's help (0x005272F5, 0x00527351, 0x00527620)
		if ( TrackToolStation == item )
		{
			var queueFollows = cause != TrackWalkCause.Edit && item.Entry.HasQueue && item.Runtime.IsAttraction && item.Visitors.HasCells;
			// UI-031: the live line click with exactly Ctrl and no queue keeps this mode, with no start.
			// The circuit refresh above is identical; only the issuer's local tool continuation differs.
			if ( cause == TrackWalkCause.Line && keepTrackToolAfterLine && !queueFollows && CellTool.Writer is ParkCommandTrackWriter or TrackLineWriter )
			{
				CellTool.Enter( CellTool.Writer!, null );
				return;
			}
			CellTool.Cancel();
			trackToolStation = null;
			// [BIN:TPW-EXE:0x00526C70 edit walk end] a cut that leaves a valid circuit ends the tool (0x52F580(0), 0x526C6E): no queue tool follows, as it does a line that closes the circuit
			if ( queueFollows )
				EnterQueueTool( item.Visitors );
		}
	}

	/// <summary>A committed line or cut updates the attraction door through the ordinary reopen guard. Restore and placement never call this.</summary>
	// [BIN:TPW-EXE:0x004DF290 track edit reopen] after the circuit walk, a qualifying edit asks the opening guard before 0x4DF390 opens the attraction
	private void UpdateTrackOpenState( OriginalObject item, bool valid )
	{
		var runtime = item.Runtime;
		if ( item.IsDeleted || !runtime.IsAttraction )
			return;
		// [EXT:track-edit-invalid-close] keep the model and economy door consistent with an edited circuit the controller cannot accept
		if ( !valid )
		{
			runtime.Close();
			SyncOpenState( runtime );
			return;
		}
		var record = EconomyStateOf( runtime );
		var state = runtime.GetVariable( RideVariables.VAR_BROKEN ) != 0 ? RideObjectStates.BrokenDown
			: record != null ? ParkEconomy.ObjectState( record ) : RideObjectStates.Idle;
		if ( !RideOpening.MayOpen( state, record?.MaintenanceRequested == true,
			Guests == null || ParkGate.QueueConnected( Guests.Grid, item.Visitors ), 0, true ) )
			return;
		runtime.Open();
		SyncOpenState( runtime );
	}

	/// <summary>
	/// The build check of a track add-on (a jump or tunnel, the objects of <c>AddOn.UpgradeType</c> 1) for <paramref name="ride"/>: its '+' and '#' cells must lie on straight, unflagged
	/// tiles of the ride's track (<see cref="TrackBuilder.CanStandAddOn"/>), its other cells on free ground, as for any object.
	/// </summary>
	public OriginalPlacementResult CheckAddOn( ObjectCatalogEntry entry, int x, int y, int rotation, OriginalObject ride )
	{
		if ( !trackStations.TryGetValue( ride, out var station ) || BuilderOf( ride ) is not { } builder )
			return OriginalPlacementResult.Blocked;
		var owner = builder.OwnerOf( station );
		Objects.AddOnCellRule = ( cx, cy ) => builder.CanStandAddOn( cx, cy, owner );
		try
		{
			return CheckObject( entry, x, y, rotation, out _ );
		}
		finally
		{
			Objects.AddOnCellRule = null;
		}
	}

	/// <summary>
	/// Builds a track add-on for <paramref name="ride"/> (<see cref="PlaceObject"/> with the add-on rule of <see cref="CheckAddOn"/>): the placement writes the flag 0x10 on its '+' cells
	/// and the track beneath (<see cref="ParkCellMap.WriteFootprint"/>), and the circuit is walked again at once, so the sections take the add-on (<see cref="WalkAfterAddOn"/>).
	/// The ride's upgrade list offers the add-on and the build tool calls this (<see cref="BeginAddOn"/>, <see cref="BuildTarget"/>).
	/// </summary>
	public OriginalObject? PlaceAddOn( ObjectCatalogEntry entry, int x, int y, int rotation, OriginalObject ride )
	{
		if ( !trackStations.TryGetValue( ride, out var station ) || BuilderOf( ride ) is not { } builder )
			return null;
		var owner = builder.OwnerOf( station );
		Objects.AddOnCellRule = ( cx, cy ) => builder.CanStandAddOn( cx, cy, owner );
		OriginalObject? placed;
		try
		{
			placed = PlaceObject( entry, x, y, rotation );
		}
		finally
		{
			Objects.AddOnCellRule = null;
		}
		if ( placed != null )
			WalkAfterAddOn( ride );
		return placed;
	}

	/// <summary>
	/// The ride window's track button for a kart or water ride (tool 0xE or 0x13): a closed circuit opens the edit tool (two clicks cut the track between two tiles), an open one
	/// the line tool from the end of its track (the exit cell when no track is laid).
	/// </summary>
	// [BIN:TPW-EXE:0x004AF200 track button] the button sets the cursor of tool 0xE (type 1) or 0x13 (type 2) and calls the click handler; the hover 0x527960 puts the tool in that mode
	// [BIN:TPW-EXE:0x00526800 edit click] with the ride's circuit word (+0x2C) zero the click walks the track to its open end (0x530830) and the tool becomes the line tool 0xB or 0x10 (0x526C36, 0x526C50) starting there; with a closed circuit the click picks tiles for the cut
	public bool EnterTrackTool( OriginalObject station )
	{
		if ( IsReadOnlyVisit || waterTracks == null || !trackStations.TryGetValue( station, out var layout ) )
			return false;
		StaffPlacement?.Cancel();
		var builder = BuilderOf( station )!;
		CellTool.Cancel();
		var editor = new TrackEditor( builder, layout, TrackEditHooksOf( station, builder ) );
		if ( station.Runtime.Bump is { IsTrackValid: true } )
		{
			// [EXT:online-coop-commands] the cut is a command (CutTrackCommand); the tool moves on from its walk
			CellTool.Enter( new TrackEditWriter( editor, builder.Family, _ => { }, CancelTrackTool, ( first, second ) => CutTrack( station, editor, first, second ) ), null );
		}
		else
		{
			// [EXT:online-coop-commands] the walk to the open end writes the tiles' overlap words: it is a command, and the tool takes its corners
			if ( Commands.Issue( new OpenTrackLineToolCommand( station.Runtime.AttractionId ) ).Value is not TrackOpenEnd open )
				return false;
			EnterTrackLineTool( station, editor, open );
		}
		trackToolStation = station;
		return true;
	}

	/// <summary>
	/// The line tool from the open end of <paramref name="station"/>'s track; [EXT:online-coop-commands] its lines and their undo are commands (<see cref="ParkCommandTrackWriter"/>), and a walk that finds the
	/// circuit valid moves the tool on (<see cref="ContinueTrackTool"/>).
	/// </summary>
	private void EnterTrackLineTool( OriginalObject station, TrackEditor editor, TrackOpenEnd open ) =>
		editor.EnterLineTool( CellTool, null, inner => new ParkCommandTrackWriter( inner, Commands, station.Runtime.AttractionId, () => ContinueTrackTool( station, TrackWalkCause.Line ) ), open );

	/// <summary>The edit tool's cut of <paramref name="station"/>'s track between two picked tiles, through the command; null when it is refused, which ends the tool.</summary>
	private TrackCutResult? CutTrack( OriginalObject station, TrackEditor editor, (int X, int Y) first, (int X, int Y) second )
	{
		if ( Commands.Issue( new CutTrackCommand( station.Runtime.AttractionId, first.X, first.Y, second.X, second.Y ) ).Value is not TrackCutDone done )
			return null;
		FinishTrackEdit( station, editor, done );
		return done.Cut;
	}

	private void CancelTrackTool()
	{
		CellTool.Cancel();
		trackToolStation = null;
	}

	/// <summary>The money and the add-ons of a cut: each cleared tile refunds the track cell cost × the ride's scrap percentage / 100, an add-on standing on one is demolished.</summary>
	private TrackEditHooks TrackEditHooksOf( OriginalObject ride, TrackBuilder builder ) => new()
	{
		TileCleared = _ =>
		{
			if ( Park?.Economy is { } economy && GetEconomyInstance( ride ) is int instance )
				economy.RefundTrackTile( builder.Purchase, instance );
		},
		RemoveAddOn = at =>
		{
			foreach ( var (x, y) in TrackStation.TileCellsOf( at.X, at.Y ) )
			{
				if ( Objects.FindAt( x, y ) is { IsDeleted: false } addOn && addOn != ride )
				{
					Objects.Remove( addOn );
					return;
				}
			}
		}
	};

	/// <summary>
	/// After a committed cut: the walk of the circuit again (the controller closes, the pieces start or stop) and, when the circuit is open, the line tool from the end of the track.
	/// </summary>
	// [BIN:TPW-EXE:0x00526BD0 cut finish] 0x52FC80(0) repairs the station, the picks are cleared, the new start is the second click's tile (0x81EDE4/E8), then the walk 0x52A970(0) sets the circuit word: valid, 0x52A490(ride, 1) starts the pieces and the tool ends; invalid, 0x52A490 and 0x52A700 stop them, 0x530830 finds the open end and the tool becomes the line tool (0x526C19..0x526C54)
	private void FinishTrackEdit( OriginalObject station, TrackEditor editor, TrackCutDone done )
	{
		// the walks ran in the command (WalkTrack, and the open end's on an open circuit); the tool's step after them is the issuer's
		if ( done.Valid == true )
			ContinueTrackTool( station, TrackWalkCause.Edit );
		if ( done.Valid != false || done.Open is not { } open )
			return;
		CellTool.Cancel();
		EnterTrackLineTool( station, editor, open );
		trackToolStation = station;
	}

	/// <summary>Ends the track tool.</summary>
	public void ExitTrackTool()
	{
		if ( TrackToolStation != null )
			CellTool.Cancel();
		trackToolStation = null;
	}

	/// <summary>
	/// Runs a track line command (<see cref="LayTrackLineCommand"/>, <see cref="UndoTrackLineCommand"/>) for the ride whose attraction id is <paramref name="ride"/>: the ride's line writer lays the line from
	/// <paramref name="start"/> toward <paramref name="end"/>, or with <paramref name="undo"/> its editor takes that line back, and a change walks the circuit again (<see cref="WalkTrack"/>).
	/// </summary>
	internal ParkCommandResult ExecuteTrackLine( int ride, (int X, int Y) start, (int X, int Y) end, bool undo )
	{
		if ( waterTracks == null || ResolveCommandObject( ride ) is not { } station || !trackStations.TryGetValue( station, out var layout ) || BuilderOf( station ) is not { } builder )
			return ParkCommandResult.Rejected( $"Attraction {ride} lays no kart or water track." );
		TrackCircuit? walked = null;
		var writer = new TrackLineWriter( builder, () => walked = WalkTrack( station, TrackWalkCause.Line ), builder.OwnerOf( layout ), TrackEditorOf( station, layout, builder ) );
		SegmentResult? segment = null;
		if ( undo )
			writer.Undo( new SegmentResult( end, Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false ) { Start = start } );
		else
			segment = writer.Commit( start, end );
		return ParkCommandResult.Ok( new TrackLineDone( segment, walked?.IsValid ) );
	}

	/// <summary>
	/// Runs a <see cref="CutTrackCommand"/>: the closed circuit of the ride whose attraction id is <paramref name="ride"/> is cut between two tiles the edit tool's test accepts
	/// (<see cref="TrackEditWriter.Cut"/>) and walked again.
	/// </summary>
	internal ParkCommandResult ExecuteTrackCut( int ride, (int X, int Y) first, (int X, int Y) second )
	{
		if ( waterTracks == null || ResolveCommandObject( ride ) is not { } station || !trackStations.TryGetValue( station, out var layout ) || BuilderOf( station ) is not { } builder )
			return ParkCommandResult.Rejected( $"Attraction {ride} lays no kart or water track." );
		// the edit tool runs on a closed circuit only (EnterTrackTool)
		if ( station.Runtime.Bump is not { IsTrackValid: true } )
			return ParkCommandResult.Rejected( $"The track of attraction {ride} is no closed circuit." );
		var editor = TrackEditorOf( station, layout, builder );
		if ( TrackEditWriter.Cut( editor, first, second ) is not { } cut )
			return ParkCommandResult.Rejected( "The edit tool's test refuses a tile of the cut." );
		var valid = WalkTrack( station, TrackWalkCause.Edit )?.IsValid;
		// an open circuit takes the line tool from its open end (FinishTrackEdit): the walk there writes the tiles' overlap words, so it runs here
		var open = valid == false && trackStations.ContainsKey( station ) && BuilderOf( station ) != null ? editor.FindOpenEnd() : null;
		return ParkCommandResult.Ok( new TrackCutDone( cut, valid, open ) );
	}

	/// <summary>
	/// Runs an <see cref="OpenTrackLineToolCommand"/>: on the open circuit of the ride whose attraction id is <paramref name="ride"/>, the walk to the track's open end
	/// (<see cref="TrackEditor.FindOpenEnd"/>), whose corners the line tool takes.
	/// </summary>
	internal ParkCommandResult ExecuteOpenTrackLine( int ride )
	{
		if ( waterTracks == null || ResolveCommandObject( ride ) is not { } station || !trackStations.TryGetValue( station, out var layout ) || BuilderOf( station ) is not { } builder )
			return ParkCommandResult.Rejected( $"Attraction {ride} lays no kart or water track." );
		// a closed circuit opens the edit tool instead (EnterTrackTool)
		if ( station.Runtime.Bump is { IsTrackValid: true } )
			return ParkCommandResult.Rejected( $"The track of attraction {ride} is a closed circuit." );
		return ParkCommandResult.Ok( TrackEditorOf( station, layout, builder ).FindOpenEnd() );
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
					renderer = new TrackRenderer( entry, kit, () => Objects.Grid.Origin, ( x, y ) => Objects.Grid.GetCellHeight( x, y ), ( x, y ) => Objects.Grid.GetCornerHeight( x, y ), ( x, y ) => Objects.Grid.GetTerrainHeight( x, y ) )
						{ AddOnSurfaceAt = AddOnSurfaceAt };
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
	internal OriginalObject? BuildDemoTrackRide( int trackType = 2, int? addOnId = null )
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
		// an add-on two tiles long with a free cell on either side (the Teleport, the tunnels) needs a straight of two tiles clear of the station and free ground around the
		// loop: the loop is a tile deeper then, with a cell of margin
		var (depth, margin) = addOnId == null ? (4, 0) : (6, 1);
		foreach ( var cell in Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - near.X ) + Math.Abs( cell.Y - near.Y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			var anchor = GetCentredAnchor( entry, cell.X, cell.Y, 0 );
			if ( Objects.CheckFree( entry, anchor.X, anchor.Y, 0 ) != OriginalPlacementResult.Allowed
				|| !Enumerable.Range( anchor.X - 2 - margin, 9 + 2 * margin ).All( x => Enumerable.Range( anchor.Y - 1 - margin, depth + 4 + 2 * margin ).All( y => Free( x, y ) ) ) )
				continue;
			var station = PlaceObject( entry, cell.X, cell.Y, 0 ) ?? Objects.TryPlace( entry, anchor.X, anchor.Y, 0, out _ );
			if ( station == null || !trackStations.TryGetValue( station, out var layout ) || !EnterTrackTool( station ) )
				return null;
			// a built ride with a queue starts closed (RIDES-015, 0x4DB712): the capture helper opens it as the player's door button would
			station.Open();
			foreach ( var offset in new (int X, int Y)[] { (4, depth), (-2, depth), (-2, 0) } )
				CellTool.Click( (layout.First.X + offset.X, layout.First.Y + offset.Y) );
			CellTool.Cancel();
			// --debug-track-addon: the add-on goes on the circuit's straight before the race starts (its walk would end the race)
			if ( addOnId is { } id )
				BuildDemoAddOn( station, id );
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
			(Camera.Mode as ParkCameraMode)?.SetPose( new Vector3( centre.X, centre.Y, 0 ), ParkCameraMode.MaximumHeight, -20 );
			Log.Trace( $"--debug-track: {entry.DisplayName} at {layout.First} with a {(station.Runtime.Bump!.IsTrackValid ? "valid" : "invalid")} circuit of {BuilderOf( station )!.Tiles().Count()} tiles; camera on engine ({centre.X:0.#}, {centre.Y:0.#})." );
			return station;
		}
		return null;
	}

	/// <summary>
	/// Places the track add-on <paramref name="infoId"/> of <paramref name="ride"/> on the first tile of its circuit where the build tool's hover position is allowed, as the
	/// player's add-on row and click would (the debug flag <c>--debug-track-addon</c>, for captures). False when the ride has no such add-on or no tile takes it.
	/// </summary>
	// [EXT:debug-water-track] developer/screenshot helper, not original behaviour: it clicks the add-on tool for the player
	internal bool BuildDemoAddOn( OriginalObject ride, int infoId )
	{
		if ( Objects.Catalog.Find( infoId ) is not { } entry || !TrackAddOns.IsTrackAddOn( entry ) || BuilderOf( ride ) is not { } builder || !BeginAddOn( entry, ride ) )
		{
			Log.Warning( $"--debug-track-addon: {ride.Entry.DisplayName} has no track add-on {infoId}." );
			return false;
		}
		try
		{
			foreach ( var (x, y) in builder.Tiles().ToArray() )
			{
				// the pointer on the tile's far cell: the hover counts even cells from the ride's anchor, which puts a pointer on the head row or column of some tiles on the
				// tile before them (TrackAddOns.HoverTarget)
				var (targetX, targetY, rotation) = BuildTarget( entry, x + 1, y + 1 );
				if ( CheckBuildTarget( entry, targetX, targetY, rotation ) != OriginalPlacementResult.Allowed )
					continue;
				SetBuildRotation( rotation );
				// a park that has not researched the add-on gets it unpaid, as the demo ride's own placement falls back to an unpaid one
				if ( (PlaceBuildTarget( entry, targetX, targetY, rotation ) ?? PlaceAddOnUnpaid( entry, targetX, targetY, rotation, ride )) is not { } placed )
					continue;
				Log.Trace( $"--debug-track-addon: {entry.DisplayName} at ({placed.Placement.X}, {placed.Placement.Y}) turned {rotation} on the tile ({x}, {y}); the circuit walks {(ride.Runtime.Bump?.IsTrackValid == true ? "valid" : "invalid")}." );
				return true;
			}
			Log.Warning( $"--debug-track-addon: no tile of {ride.Entry.DisplayName}'s circuit takes {entry.DisplayName}." );
			return false;
		}
		finally
		{
			BuildEntry = null;
			ResetBuildRotation();
		}
	}

	/// <summary>The debug helper's placement of an add-on the park cannot buy: the add-on rule and the walk of <see cref="PlaceAddOn"/> without the purchase.</summary>
	private OriginalObject? PlaceAddOnUnpaid( ObjectCatalogEntry entry, int x, int y, int rotation, OriginalObject ride )
	{
		if ( !trackStations.TryGetValue( ride, out var station ) || BuilderOf( ride ) is not { } builder )
			return null;
		var owner = builder.OwnerOf( station );
		Objects.AddOnCellRule = ( cx, cy ) => builder.CanStandAddOn( cx, cy, owner );
		OriginalObject? placed;
		try
		{
			var anchor = GetCentredAnchor( entry, x, y, rotation );
			placed = Objects.TryPlace( entry, anchor.X, anchor.Y, rotation, out _ );
		}
		finally
		{
			Objects.AddOnCellRule = null;
		}
		if ( placed != null )
			WalkAfterAddOn( ride );
		return placed;
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
			if ( !rebuilt.SequenceEqual( entry.Sections.Select( section => section with { AddOnPassing = false } ) ) )
				throw new InvalidDataException( $"Attraction {entry.AttractionId}'s walked sections do not match the saved ones." );
			bump.Restore( entry );
			for ( var index = 0; index < entry.Sections.Count; index++ )
			{
				if ( !entry.Sections[index].AddOnPassing )
					continue;
				if ( bump.Sections[index].AddOn == null )
					throw new InvalidDataException( $"Attraction {entry.AttractionId}'s section {index} waits for an add-on's clip, but no add-on stands on it." );
				bump.Sections[index].AddOnPassing = true;
			}
			station.Runtime.ResumeBumpEffects();
		}
	}

	// [APPROX:TRACK-032] the controller of a saved ride is rebuilt from its circuit and the saved verdict, where tp.exe's loader 0x543560 reads the ride record, the circuit's section records as raw 0xac-byte copies and the reference section from a chunk stream and the ride's handle +0x28 then points at the controller (evidence needed: the section record's fields beyond the kind, place and segment)
	/// <summary>Walks a station's circuit for a restore of a shared-module save and rebuilds it with the saved verdict (<see cref="BumpController.RestoreCircuit"/>).</summary>
	private TrackCircuit? RewalkForRestore( OriginalObject item, BumpControllerSaveData entry )
	{
		if ( waterTracks == null || !trackStations.TryGetValue( item, out var station ) || item.Runtime.Bump is not { } bump )
			return null;
		var circuit = TrackCircuitWalker.WalkAndRefresh( BuilderOf( item )!, station );
		bump.RestoreCircuit( circuit, entry.IsTrackValid, entry.ObjectSlots?.Count ?? -1, entry.BuoyCount );
		LinkAddOns( item, circuit );
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

/// <summary>The riders of the track rides as head sources: every kart's, boat's and bumper car's riders hang from the head dummies of its mesh (<see cref="TrackRenderer.CollectHeads"/>).</summary>
internal sealed class TrackHeadSources : IHeadSourceProvider
{
	private readonly Func<IEnumerable<TrackRenderer>> renderers;

	public TrackHeadSources( Func<IEnumerable<TrackRenderer>> renderers ) => this.renderers = renderers;

	public void Collect( List<HeadSource> sources, int? onlyAttraction )
	{
		// [BIN:TPW-EXE:0x00549DB0 vehicle creation] the karts, the water rides' boats and the bumper cars are all made here, and it hangs every rider's head (0x00549C60)
		foreach ( var renderer in renderers() )
			renderer.CollectHeads( sources, onlyAttraction );
	}
}
