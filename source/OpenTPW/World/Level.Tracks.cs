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
			// [BIN:TPW-EXE:0x00538CF0 cell op 0x87] the exit and entry tiles' first op (0x538D76..0x538D9E) demolishes the object owning each of their cells that is a type 4, 9 or 10 cell (0x527EE0, 0x527D60) when 0x818C14 < 0x818C10: the placement's tool priority is the ride's Info.OverwritePriority (5, the hover 0x5234D0 at 0x5236A4) and 0x818C14, the priority of the last owner the footprint's validation met (0x535788), is below it for any placement the validator allowed (0x535D66); the two tiles lie outside the 4-cell footprint, so nothing validated them. A kart or water ride's station cells are no such cells (TrackBuilder.GiveWay), but the map of OpenTPW's objects holds them, so they are skipped there
			BuilderOf( item )!.PlaceInitialStation( station, ( x, y ) => DemolishObjectAt( x, y ) );
		else
			BuilderOf( item )!.PlaceStation( station );
		// [EXT:track-placement-walk] the original's placement lays the station (0x0052A1A0 from 0x00529E10) and walks nothing; OpenTPW walks the fresh station so its controller has the station's sections at once, without writing the track word (it keeps the constructor's 1 until a track edit, as the original's does), and the pieces take the add-on walk's rule (TrackPieceAnimations.ForWalk), which on the station's open circuit stops clips none of them plays yet
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
		DemolishTrack( item, station, BuilderOf( item )! );
		item.Runtime.Bump?.Rebuild( TrackCircuit.Invalid( "removed" ) );
	}

	/// <summary>
	/// The demolition of a kart or water ride's track while its tiles are still laid: the two replays clear the chains of tiles from both sides of the station, each cleared tile refunds
	/// the track cell cost × the ride's scrap percentage / 100, and the demolition then charges twice that cost × the percentage / 100 once. A park load or clear removes nothing through here.
	/// </summary>
	// [BIN:STP-PPC:0x100748A0 ride demolition] the demolition replays the queue tool and the kart/water track tool from both station sides to clear the circuit, with one Spend of 2 × cell cost × 0x100E2424() / 100
	// [BIN:TPW-EXE:0x00527EE0 ride demolition] the replays run with flag 0x81B46C = 1, so ClearCell 0x53B280 refunds every cleared kart or water tile (0x53B54A..0x53B57F: a tile whose overlap word +0x20 went negative is cleared and refunds unless it is an end of the replayed line while the flag is clear; the flag is set, the type 0x19 and online cases aside), and 0x5280F0..0x528179 then debits twice the cell cost × the scrap percentage / 100 once
	// [BIN:TPW-EXE:0x00527EE0 demolition replays] the two replays of the track tool clear the chain of tiles from the station's exit to the open end of the track (0x530830, then the lines between its corner points) and the chain from the tile before the station (0x530B70), as TrackEditor.Demolish does: a tile no chain reaches stays in the park and refunds nothing, a tile a chain passes with a pass counter left (a junction) loses one pass only
	// [BIN:TPW-EXE:0x0053B280 replay clears] each line between two corner points of the collected stacks is replayed through the click handler's mode 0x35 (0x52FE50) with the clear op's line step, and the cut's pass rule (-1, -1) applies to a non-line junction: TrackEditor.ClearCell is the full port
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
	/// Removes the station tiles of a ride (pass 2 of the demolition); the tiles its replays cleared are gone already (<see cref="OnTrackStationRemoving"/>) and a tile no replay reached stays, as in the original.
	/// A type without a queue runs no replay and leaves its whole track.
	/// </summary>
	private void DemolishTrack( OriginalObject item, TrackStation station, TrackBuilder builder )
	{
		// pass 2 of the demolition clears the record under every cell of the ride's shape with 0x53B280(0, 0, 1): the station tiles go through the record clear, which takes the arms of the tiles beside them
		var editor = TrackEditorOf( item, station, builder );
		foreach ( var (x, y, cell) in item.Cells )
		{
			if ( ParkCellMap.FootprintType( cell.CellType ) != 0 && builder.Cells.InBounds( x, y ) )
				editor.ClearUnderDemolition( (x, y) );
		}
		// a station tile outside the shape (a legacy layout) goes as it always did
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
		// [BIN:TPW-EXE:0x00529E10 placement walks nothing] the ride's placement lays the station and walks no circuit, so the track word +0x2C keeps the constructor's 1 until a track edit writes it (0x524D1B, 0x5251B8): OpenTPW's placement walk builds the station's sections without writing the word
		bump.Rebuild( circuit, writesWord: cause != TrackWalkCause.Placement );
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
	/// The track tool's step after a walk that found <paramref name="item"/>'s circuit valid: the tool on that ride ends and, while a placement's queue start is pending
	/// (<see cref="placedQueueStart"/>), the queue tool follows that ride's queue; with none pending, a live line click with exactly Ctrl restarts the line tool without a start.
	/// The issuer's machine alone takes it ([EXT:online-coop-commands]: a peer's or a replayed command moves no tool). [EXT:online-coop-undo] In a shared park a line's continuation follows the
	/// restart its line carried (<paramref name="keeps"/>, <see cref="LayTrackLineCommand.KeepsTool"/>), which the host's journal reads too (C5-3); null takes the click's own.
	/// </summary>
	internal void ContinueTrackTool( OriginalObject item, TrackWalkCause cause, bool? keeps = null )
	{
		// [BIN:TPW-EXE:0x0052A970 circuit walk] a valid circuit closes the tool; the walk itself posts no advisor or event message (it calls no message builder 0x0059B590)
		// [BIN:TPW-EXE:0x00526D41 track tool help] the kart and water tool posts tool help 0xCC (204) only for a click with no line start (0x0081EDE4/E8 = −1), which sets the start (0x00525A1C → 0x00526CB3); the placement presets the start at the station's exit (0x0052A1A0), so it is reached only when the tool stays on without one after a line closes with exactly Ctrl and no queue start pending (0x00526FB3, UI-031), and the HUD's report of that click posts it (ParkHud.ReportCellToolClick, AdvisorToolHelp.TrackLineStart); 0xB8, 0xD6 and 0x151 are the path and queue tool's help (0x005272F5, 0x00527351, 0x00527620)
		if ( TrackToolStation == item )
		{
			// [BIN:TPW-EXE:0x00526F95 queue start test] the line's close takes the queue tool only while the placement's queue start 0x8186C0 is pending: it starts the queue tool at that start's cell (0x7639B8/0x7639BC, 0x526FB9..0x527004), which the last placement of a ride with an entrance stored and which may be another ride's, and takes it (0x527056); with none pending the modifier word decides (0x527146)
			// A pending start whose ride was sold since is dropped (an OpenTPW guard): the original's record stays pending and the close hands its cell to the queue tool, where OpenTPW's queue tool needs the ride
			var queue = cause != TrackWalkCause.Edit && placedQueueStart is { IsDeleted: false } placed ? placed : null;
			// UI-031: the live line click with exactly Ctrl and no queue start keeps this mode, with no start.
			// The circuit refresh above is identical; only the issuer's local tool continuation differs.
			if ( cause == TrackWalkCause.Line && (keeps ?? (keepTrackToolAfterLine && queue == null)) && CellTool.Writer is ParkCommandTrackWriter or TrackLineWriter )
			{
				CellTool.Enter( CellTool.Writer!, null );
				return;
			}
			CellTool.Cancel();
			trackToolStation = null;
			// [BIN:TPW-EXE:0x00526C70 edit walk end] a cut that leaves a valid circuit ends the tool (0x52F580(0), 0x526C6E): no queue tool follows, as it does a line that closes the circuit
			if ( queue != null )
			{
				placedQueueStart = null;
				EnterQueueTool( queue.Visitors );
			}
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
		// [BIN:TPW-EXE:0x004DF290 reopen guard state] the state word, as the guard reads it
		var state = record != null ? ParkEconomy.ObjectState( record ) : runtime.Visitors.StateWord.Value;
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
	public OriginalPlacementResult CheckAddOn( ObjectCatalogEntry entry, int x, int y, int rotation, OriginalObject ride ) => CheckAddOn( entry, x, y, rotation, ride, out _ );

	/// <summary>
	/// <see cref="CheckAddOn(ObjectCatalogEntry, int, int, int, OriginalObject)"/> with the objects it would demolish and the path cells it would clear ([EXT:online-coop-claims] a shared park's
	/// claims ask them, <see cref="CoopBuildRights.PlacementLeavesClaimsAlone"/>).
	/// </summary>
	internal OriginalPlacementResult CheckAddOn( ObjectCatalogEntry entry, int x, int y, int rotation, OriginalObject ride, out ObjectPlacementPlan plan )
	{
		plan = ObjectPlacementPlan.None;
		if ( !trackStations.TryGetValue( ride, out var station ) || BuilderOf( ride ) is not { } builder )
			return OriginalPlacementResult.Blocked;
		var owner = builder.OwnerOf( station );
		Objects.AddOnCellRule = ( cx, cy ) => builder.CanStandAddOn( cx, cy, owner );
		try
		{
			return CheckObject( entry, x, y, rotation, out plan );
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
	public bool EnterTrackTool( OriginalObject station, bool justPlaced = false ) => BeginTrackTool( station, placed: false, justPlaced ) == TrackToolEntry.Entered;

	/// <summary>
	/// The track tool's entry on <paramref name="station"/> (<see cref="EnterTrackTool"/>; with <paramref name="placed"/>, the tool a ride the player just placed opens, which leaves the ride's queue
	/// start pending, <see cref="EnterPlacedTrackTool"/>; with <paramref name="justPlaced"/>, implied by <paramref name="placed"/>, the line tool as a placement starts it, whatever the circuit):
	/// it started, or [EXT:online-coop-claims] a guest's claim went to the host and the tool starts from its outcome (C3c-1b, <see cref="CompleteTrackToolStart"/>), or a claim refused it
	/// (another player's, or the session's rights), or nothing started.
	/// </summary>
	internal TrackToolEntry BeginTrackTool( OriginalObject station, bool placed, bool justPlaced = false )
	{
		justPlaced |= placed;
		if ( IsReadOnlyVisit || waterTracks == null || !trackStations.TryGetValue( station, out var layout ) )
			return TrackToolEntry.None;
		// [EXT:online-coop-claims] a ride another player claims starts no tool, and the tool on stays: its track is that player's to edit now (C3c-1)
		if ( CoopSession != null && ClaimedAgainst( Commands.Player, station.Runtime.AttractionId ) )
		{
			NoteSessionRefusal( CoopRefusal.Claimed );
			return TrackToolEntry.Refused;
		}
		// [EXT:online-coop-commands] a guest's coaster commands still to send go first: a claim sent now would come before them (C3c-2)
		if ( !CoasterCommandsSentBeforeClaim() )
			return TrackToolEntry.Refused;
		// [EXT:online-coop-commands] another tool: a guest's queue edit, Move pick-up or track entry that waits for the host starts no tool once it is back (C3b, C3c-1b); the coaster tool ends
		// first, so no claim moves away from a coaster whose panel is open (C3c-2)
		DropToolStartWait();
		StaffPlacement?.Cancel();
		ExitCoasterTool();
		var builder = BuilderOf( station )!;
		CellTool.Cancel();
		// [EXT:online-coop-claims] a guest's tool claims its ride through the host (C3c-1b): the tool starts from the claim's outcome, the latest entry's alone (an earlier one's wait was
		// dropped above), and the claim moves from a ride an earlier entry claimed; the coaster tool that ended sends its last commands first (C3c-2)
		if ( CoopSession != null && Commands.SendsRequests )
		{
			if ( !CoasterCommandsSentBeforeClaim() )
				return TrackToolEntry.Refused;
			if ( Commands.Issue( new ClaimRideCommand( station.Runtime.AttractionId ) ).Request is not { } claim )
				return TrackToolEntry.Refused;
			WaitingToolStart = claim;
			pendingTrackStart = new PendingTrackStart( claim, station.Runtime.AttractionId, placed, justPlaced );
			return TrackToolEntry.Pending;
		}
		// [EXT:online-coop-claims] the tool claims its ride once the tool it replaces is gone (C3c-1); the claim moves from a ride the replaced tool held
		if ( !ClaimForTrackTool( station ) )
			return TrackToolEntry.Refused;
		var editor = new TrackEditor( builder, layout, TrackEditHooksOf( station, builder ) );
		// [BIN:TPW-EXE:0x0052A050 line tool after a placement] the placement starts the line tool itself (mode 3 with its line start), without reading the circuit word that the tool button's click reads (0x5267F2)
		if ( !justPlaced && station.Runtime.Bump is { IsTrackValid: true } )
			EnterTrackEditTool( station, editor );
		else
		{
			// [EXT:online-coop-commands] the walk to the open end writes the tiles' overlap words: it is a command, and the tool takes its corners
			if ( Commands.Issue( new OpenTrackLineToolCommand( station.Runtime.AttractionId ) ).Value is not TrackOpenEnd open )
				return TrackToolEntry.None;
			EnterTrackLineTool( station, editor, open );
		}
		trackToolStation = station;
		if ( placed )
			placedQueueStart = PlacedQueueStart( station );
		return TrackToolEntry.Entered;
	}

	/// <summary>The queue start a placed kart or water ride leaves pending (<see cref="placedQueueStart"/>): a ride with a queue, so the close of its circuit hands over to the queue tool.</summary>
	private static OriginalObject? PlacedQueueStart( OriginalObject item ) => item.Entry.HasQueue && item.Runtime.IsAttraction && item.Visitors.HasCells ? item : null;

	/// <summary>
	/// [EXT:online-coop-claims] A guest's track or coaster tool entry that waits for the host (C3c-1b, C3c-2): the request of its claim, then of its open (a coaster's: its entry), the ride,
	/// whether a placement opened a kart or water ride's tool (its queue start), and whether that tool starts as a placement's (the line tool whatever the circuit).
	/// </summary>
	private sealed record PendingTrackStart( CoopGuestCommands.Request Request, int Ride, bool Placed, bool JustPlaced = false );

	/// <summary>[EXT:online-coop-claims] The guest's track or coaster tool entry that waits for the host, or null (C3c-1b, C3c-2): <see cref="WaitingToolStart"/> holds its request.</summary>
	private PendingTrackStart? pendingTrackStart;

	/// <summary>
	/// [EXT:online-coop-claims] The outcome of the guest's track tool entry that waits for the host (C3c-1b), once its request is back (the HUD calls this after <see cref="EndWaiting"/>):
	/// <list type="bullet">
	/// <item>the claim accepted: a closed circuit opens the edit tool; an open one sends the walk to its open end (<see cref="OpenTrackLineToolCommand"/>), a second request with the same wait;</item>
	/// <item>the open accepted: the line tool starts from the open end its record walked to.</item>
	/// </list>
	/// Either starts the tool only while the park view has no tool and the park's claims, with every record applied so far, still give the ride to this guest's seat (a claim taken and let go
	/// in one host frame starts nothing: <see cref="TrackToolStart.ClaimGone"/>).
	/// </summary>
	internal TrackToolStart CompleteTrackToolStart( CoopGuestCommands.Request request )
	{
		ArgumentNullException.ThrowIfNull( request );
		if ( pendingTrackStart is not { } start || !ReferenceEquals( start.Request, request ) )
			return TrackToolStart.None;
		pendingTrackStart = null;
		if ( !request.Accepted || IsReadOnlyVisit || ResolveCommandObject( start.Ride ) is not { IsDeleted: false } station )
			return TrackToolStart.None;
		// [EXT:online-coop-claims] a coaster's entry: after the claim the entry's writes go as a second request, whose outcome opens the panel (C3c-2)
		if ( station.Runtime.Coaster != null )
			return CompleteCoasterToolStart( request, start, station );
		if ( waterTracks == null || !trackStations.TryGetValue( station, out var layout ) || BuilderOf( station ) is not { } builder )
			return TrackToolStart.None;
		if ( ClaimOf( start.Ride ) != Commands.Player )
			return TrackToolStart.ClaimGone;
		if ( !NoToolActive )
			return TrackToolStart.None;
		var editor = new TrackEditor( builder, layout, TrackEditHooksOf( station, builder ) );
		if ( request.Command is ClaimRideCommand )
		{
			if ( !start.JustPlaced && station.Runtime.Bump is { IsTrackValid: true } )
			{
				EnterTrackEditTool( station, editor );
				trackToolStation = station;
				if ( start.Placed )
					placedQueueStart = PlacedQueueStart( station );
				// a press made before the tool started gives it no click with its release
				HostToolStarts++;
				return TrackToolStart.Entered;
			}
			if ( Commands.Issue( new OpenTrackLineToolCommand( start.Ride ) ).Request is not { } open )
				return TrackToolStart.None;
			WaitingToolStart = open;
			pendingTrackStart = start with { Request = open };
			return TrackToolStart.Pending;
		}
		if ( request.Result?.Value is not TrackOpenEnd end )
			return TrackToolStart.None;
		EnterTrackLineTool( station, editor, end );
		trackToolStation = station;
		if ( start.Placed )
			placedQueueStart = PlacedQueueStart( station );
		HostToolStarts++;
		return TrackToolStart.Entered;
	}

	/// <summary>
	/// [EXT:online-coop-claims] The outcome of a guest's coaster tool entry (C3c-2): the claim accepted sends <see cref="BeginCoasterToolCommand"/> as a second request with the same wait; that
	/// one accepted opens the panel, counted as a tool the host's answer started. Either only while the park view has no tool and the claims still give the coaster to this seat.
	/// </summary>
	private TrackToolStart CompleteCoasterToolStart( CoopGuestCommands.Request request, PendingTrackStart start, OriginalObject station )
	{
		if ( GetCoasterBuilder( station ) is not { } builder )
			return TrackToolStart.None;
		if ( ClaimOf( start.Ride ) != Commands.Player )
			return TrackToolStart.ClaimGone;
		if ( !NoToolActive )
			return TrackToolStart.None;
		if ( request.Command is ClaimRideCommand )
		{
			if ( Commands.Issue( new BeginCoasterToolCommand( start.Ride ) ).Request is not { } begin )
				return TrackToolStart.None;
			WaitingToolStart = begin;
			pendingTrackStart = start with { Request = begin };
			return TrackToolStart.Pending;
		}
		OpenCoasterPanel( station, builder );
		HostToolStarts++;
		return TrackToolStart.Entered;
	}

	/// <summary>
	/// The edit tool on <paramref name="station"/>'s closed circuit; [EXT:online-coop-commands] its cut is a command (<see cref="CutTrackCommand"/>), a guest's a request whose outcome finishes the edit
	/// (C3c-1b), and the tool moves on from the cut's walk.
	/// </summary>
	private void EnterTrackEditTool( OriginalObject station, TrackEditor editor )
	{
		editor.UseEditToolExit();
		CellTool.Enter( new TrackEditWriter( editor, BuilderOf( station )!.Family, _ => { }, CancelTrackTool, ( first, second ) => SendTrackCut( station, editor, first, second ) ), null );
	}

	/// <summary>
	/// The line tool from the open end of <paramref name="station"/>'s track; [EXT:online-coop-commands] its lines and their undo are commands (<see cref="ParkCommandTrackWriter"/>), and a walk that finds the
	/// circuit valid moves the tool on (<see cref="ContinueTrackTool"/>).
	/// </summary>
	private void EnterTrackLineTool( OriginalObject station, TrackEditor editor, TrackOpenEnd open ) =>
		editor.EnterLineTool( CellTool, null, inner => new ParkCommandTrackWriter( inner, Commands, station.Runtime.AttractionId, keeps => ContinueTrackTool( station, TrackWalkCause.Line, keeps ),
			() => KeepsTrackTool, () => UndoJournal?.LastRecord ), open );

	/// <summary>
	/// [EXT:online-coop-undo] The restart a track line's click asks for after a close (UI-031): its exactly Ctrl with no placement's queue start pending (<see cref="ContinueTrackTool"/>), read at the
	/// click, which a shared park's line carries (C5-3).
	/// </summary>
	private bool KeepsTrackTool => keepTrackToolAfterLine && placedQueueStart is not { IsDeleted: false };

	/// <summary>
	/// The edit tool's cut of <paramref name="station"/>'s track between two picked tiles, through the command: done at once (null when it is refused, which ends the tool), or [EXT:online-coop-commands]
	/// a guest's that waits for the host, whose outcome finishes the edit (C3c-1b).
	/// </summary>
	private TrackCutSent SendTrackCut( OriginalObject station, TrackEditor editor, (int X, int Y) first, (int X, int Y) second )
	{
		var result = Commands.Issue( new CutTrackCommand( station.Runtime.AttractionId, first.X, first.Y, second.X, second.Y ) );
		if ( result.Request is { } request )
			return new TrackCutSent( null, new WaitingTrackCut( request, outcome => FinishTrackCut( station, editor, outcome ) ) );
		return new TrackCutSent( FinishTrackCut( station, editor, result ) );
	}

	/// <summary>The edit after a cut's <paramref name="result"/> (<see cref="FinishTrackEdit"/>): the cut, or null when it was refused.</summary>
	private TrackCutResult? FinishTrackCut( OriginalObject station, TrackEditor editor, ParkCommandResult result )
	{
		if ( result.Value is not TrackCutDone done )
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
					// [BIN:TPW-EXE:0x0053B703 add-on of a cleared tile] the tile's clear demolishes the add-on with the third argument 1: its pass 2 takes the flag 0x10 off the records and clears no tile again
					removingAddOnOfClearedTile = true;
					try { Objects.Remove( addOn ); }
					finally { removingAddOnOfClearedTile = false; }
					return;
				}
			}
		}
	};

	/// <summary>The demolition's passes over the footprint of a removed object (<see cref="ParkCellMap.DemolishFootprint"/>), with the records of the second layer cleared by their ride's track editor.</summary>
	internal void DemolishFootprintCells( OriginalObject item, ParkCellMap cells, Action<ushort>? queueCellRemoved ) =>
		cells.DemolishFootprint( item.Cells, queueCellRemoved, ClearTrackRecordUnder, moved: removingAddOnOfClearedTile );

	/// <summary>Set while the clear of a tile removes the add-on object standing on it (<see cref="ParkCellMap.DemolishFootprint"/>'s <c>moved</c>).</summary>
	private bool removingAddOnOfClearedTile;

	/// <summary>
	/// The record clear of a demolition's second pass on the second layer's record under the mask cell (x, y): the ride the tile belongs to clears it through its track editor
	/// (<see cref="TrackEditor.ClearUnderDemolition"/>, with the refund and the add-on removal of the cut's hooks); a tile no ride owns goes alone.
	/// </summary>
	private bool ClearTrackRecordUnder( int x, int y )
	{
		if ( Guests?.Grid.Cells is not { } cells || cells.Tracks.Get( x, y ) is not { IsEmpty: false } cell )
			return false;
		(int X, int Y) origin = cell.IsExtension && cell.Parent >= 0 ? cells.Tracks.CellOf( cell.Parent ) : (x, y);
		var head = cells.Tracks.Get( origin.X, origin.Y );
		foreach ( var (ride, station) in trackStations )
		{
			if ( BuilderOf( ride ) is { } builder && head.Owner != 0 && builder.OwnerOf( station ) == head.Owner )
				return TrackEditorOf( ride, station, builder ).ClearUnderDemolition( (x, y) );
		}
		// a tile laid without a ride (a rig's, an old save's) has no exit or entry tile to spare
		return cells.ClearTrackRecord( x, y );
	}

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
	internal ParkCommandResult ExecuteTrackLine( int ride, (int X, int Y) start, (int X, int Y) end, bool undo, bool last = false )
	{
		if ( waterTracks == null || ResolveCommandObject( ride ) is not { } station || !trackStations.TryGetValue( station, out var layout ) || BuilderOf( station ) is not { } builder )
			return ParkCommandResult.Rejected( $"Attraction {ride} lays no kart or water track." );
		TrackCircuit? walked = null;
		var writer = new TrackLineWriter( builder, () => walked = WalkTrack( station, TrackWalkCause.Line ), builder.OwnerOf( layout ), TrackEditorOf( station, layout, builder ) );
		SegmentResult? segment = null;
		if ( undo )
			writer.Undo( new SegmentResult( end, Array.Empty<(int X, int Y)>(), 0, CellBuildResult.Ok, false ) { Start = start, IsFirstLine = last } );
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
		// a ride no edit has walked reads 1 from its constructor (the placement starts this tool itself): its station-only circuit is open
		if ( station.Runtime.Bump is { IsTrackValid: true, TrackWordWritten: true } )
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
					renderer = new TrackRenderer( entry, kit, () => Objects.Grid.Origin, ( x, y ) => Objects.Grid.GetCellHeight( x, y ), ( x, y ) => Objects.Grid.GetCornerHeight( x, y ), ( x, y ) => Objects.Grid.GetTerrainHeight( x, y ), ( x, y ) => Objects.Grid.GetDrapeHeight( x, y ) )
						{ ObjectSurfaceAt = ObjectSurfaceAt };
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
			if ( station == null || !trackStations.TryGetValue( station, out var layout ) || !EnterTrackTool( station, justPlaced: true ) )
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
		// [EXT:online-coop-snapshot-join] kept for a join: only a ride restored from such a save keeps its legacy station live (saved as layout 0), and this sets that same station again
		foreach ( var entry in saved.Controllers.Where( entry => entry.StationLayout == 0 ) )
		{
			if ( controllers.FirstOrDefault( item => item.Runtime.AttractionId == entry.AttractionId ) is { } ride && ride.Runtime.Bump is { Template.IsBumper: false } track )
				SetTrackStation( ride, track, TrackStation.FromPlacementLegacy( ride.Placement.X, ride.Placement.Y, ride.Placement.Rotation ) );
		}
		TrackSave.RestoreCells( Guests.Grid.Cells, saved );
		// [BIN:TPW-EXE:0x00528A70 station under its footprint] a save written before the footprint's type went under the station tiles holds base type 0 there (0 points for the net worth, not 2): the footprint writer puts the type under them again, as a fresh placement does; a join keeps the host's cells exactly (EXT:online-coop-snapshot-join)
		if ( joinRestore == null )
		{
			foreach ( var station in controllers.Where( item => trackStations.ContainsKey( item ) ) )
				Guests.Grid.Cells.WriteFootprint( station.Cells, OwnerWord( station ), station.Placement.Rotation );
		}
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
			if ( entry.TrackWordWritten != false && (shared ? entry.IsTrackValid && !circuit.IsValid : circuit.IsValid != entry.IsTrackValid) )
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

/// <summary>How a track tool's entry went (<see cref="Level.BeginTrackTool"/>, <see cref="Level.EnterPlacedTrackTool"/>).</summary>
internal enum TrackToolEntry
{
	/// <summary>No tool started.</summary>
	None,
	/// <summary>The tool started.</summary>
	Entered,
	/// <summary>[EXT:online-coop-claims] A guest's claim went to the host: the tool starts from its outcome (C3c-1b).</summary>
	Pending,
	/// <summary>[EXT:online-coop-claims] A claim refused the tool (another player's, or the session's rights): nothing follows a placement then, its queue tool neither (C3c-1b).</summary>
	Refused
}

/// <summary>[EXT:online-coop-claims] What the outcome of a guest's track tool entry did (<see cref="Level.CompleteTrackToolStart"/>, C3c-1b).</summary>
internal enum TrackToolStart
{
	/// <summary>No tool started: the request was refused, the view has a tool by now, the ride is gone, or the request was no entry's.</summary>
	None,
	/// <summary>The tool started.</summary>
	Entered,
	/// <summary>The claim came back and the walk to the open end went to the host: the line tool starts from its outcome.</summary>
	Pending,
	/// <summary>The claim came back, but the park's claims no longer give the ride to this seat (the host let it go in the same frame).</summary>
	ClaimGone
}
