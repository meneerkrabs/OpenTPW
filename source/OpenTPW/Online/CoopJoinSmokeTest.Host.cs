using OpenTPW.Online.Coop;

namespace OpenTPW;

internal sealed partial class CoopJoinSmokeTest
{
	/// <summary>Pylon offsets from a coaster station's departure cell: the sandbox smoke's circuit (CoasterTests' circuit, which passes every window).</summary>
	private static readonly (int X, int Y)[] Circuit = { (4, 0), (7, 4), (4, 8), (-4, 8), (-8, 4), (-7, 0) };
	/// <summary>Render warm-up before the park is changed: streamed meshes and textures settle.</summary>
	private const int WarmUpFrames = 45;
	/// <summary>The park runs this long alone before it is shared, so its new coasters and guests move.</summary>
	private const int SettleFrames = 600;
	/// <summary>The park minutes between the two captures: at the normal speed, then at the fastest.</summary>
	private const int NormalFrames = 2400, FastestFrames = 2400;
	/// <summary>The park after the second capture, until the end.</summary>
	private const int TailFrames = 1200;
	/// <summary>The most frames a capture may take from its assignment to its Ready.</summary>
	private const int CaptureFrames = 1800;

	private enum HostStage { WarmUp, Fixture, Settle, Stage1, Capture1, Normal, Fastest, Stage2, Capture2, Tail, Done }

	private HostStage hostStage;
	private int stageFrame;
	private Level level = null!;
	private CoopSmokeHostTransport transport = null!;
	private CoopSession session = null!;
	private double clock;
	private long hostFrame;
	private readonly List<OriginalObject> coasters = new();
	private readonly List<CoopSmokeCut> cuts = new();
	/// <summary>What the host had at the cut being taken: its digest, world hash and random streams, and the staged state found there.</summary>
	private (ulong Digest, ulong World, WorldRandomState Random, string[] Staged)? atCut;
	private readonly Dictionary<uint, byte[]> stored = new();
	private (int Staff, int X, int Y)? hand;
	private (ObjectCatalogEntry Entry, int X, int Y, int Rotation)? carried;
	/// <summary>The tour ride the second cut buys while paused, and where (researched, with the golden ticket it needs, before the park is shared).</summary>
	private (ObjectCatalogEntry Entry, int X, int Y)? tourSite;
	private ParkEconomy Economy => level.Park!.Economy;

	private void StartHost()
	{
		// Instant Action: the shipped Easymode seed with its rides, shops, paths and staff, so most of the shared park is the original's own
		level = flow.StartLevel( "jungle", original: true, developerPanels: false, start: ParkStartKind.InstantAction );
		Require( level.Park != null && level.Guests != null && level.OriginalPark != null && level.StaffHands != null, "start a writable original Jungle park in Instant Action" );
		Require( CoopSession.CanShare( level, out var refusal ), $"the park can be shared ({refusal})" );
		hostStage = HostStage.WarmUp;
	}

	/// <summary>One host frame: at most one stage step, the park's frame through the session's stream, then the cut's checks.</summary>
	private void HostUpdate()
	{
		++frame;
		if ( done )
			return;
		try
		{
			Camera.Update();
			stageFrame++;
			switch ( hostStage )
			{
				case HostStage.WarmUp:
					if ( stageFrame >= WarmUpFrames )
						Next( HostStage.Fixture );
					return;
				case HostStage.Fixture:
					BuildFixture();
					Next( HostStage.Settle );
					return;
				case HostStage.Settle:
					AdvanceHost();
					if ( stageFrame >= SettleFrames )
					{
						Host();
						Next( HostStage.Stage1 );
					}
					return;
				case HostStage.Stage1:
					StageFirstCut();
					transport.MemberJoins();
					Next( HostStage.Capture1 );
					AdvanceHost();
					return;
				case HostStage.Capture1:
					AdvanceHost();
					if ( TookCut( 1 ) )
					{
						UnstageFirstCut();
						Next( HostStage.Normal );
					}
					return;
				case HostStage.Normal:
					Script( stageFrame );
					AdvanceHost();
					if ( stageFrame >= NormalFrames )
					{
						Issue( new SetGameSpeedCommand( GameSpeed.Fastest ), "the fastest speed" );
						Next( HostStage.Fastest );
					}
					return;
				case HostStage.Fastest:
					Script( stageFrame + 7 );
					AdvanceHost();
					if ( stageFrame >= FastestFrames )
						Next( HostStage.Stage2 );
					return;
				case HostStage.Stage2:
					StageSecondCut();
					transport.MemberAsks( new CoopSnapshotRequest( 1, CoopSnapshotReason.Desync, null ) { Generation = 1 } );
					Next( HostStage.Capture2 );
					AdvanceHost();
					return;
				case HostStage.Capture2:
					AdvanceHost();
					if ( TookCut( 2 ) )
					{
						Issue( new SetGameSpeedCommand( GameSpeed.Normal ), "the park runs again" );
						Issue( new SetAttractionOpenCommand( coasters[1].Visitors.AttractionId, true ), "the closed coaster opens again" );
						Next( HostStage.Tail );
					}
					return;
				case HostStage.Tail:
					Script( stageFrame + 13 );
					AdvanceHost();
					if ( stageFrame >= TailFrames )
						FinishHost();
					return;
			}
		}
		catch ( Exception error )
		{
			Fail( error );
		}
	}

	private void Next( HostStage stage )
	{
		hostStage = stage;
		stageFrame = 0;
	}

	/// <summary>The park's frame: the session clock moves one fixed frame, what the transport holds is raised, then the park runs at its session time scale.</summary>
	private void AdvanceHost()
	{
		clock += Step;
		hostFrame++;
		transport?.Pump();
		level.SimulationTimeScale = GameFlow.SessionTimeScale( level ) ?? 1f;
		level.AdvanceFrame( Step );
		if ( atCut is { } cut )
		{
			// the capture took its cut at the end of this frame's simulation step: it changed nothing of the park
			atCut = null;
			var digest = CoopDigest.Compute( level, out var world );
			Require( (digest, world) == (cut.Digest, cut.World) && level.CaptureRandomState() == cut.Random,
				$"the capture left the park's digest, world hash and random streams as they were ({digest:X16}/{cut.Digest:X16}, {world:X16}/{cut.World:X16})" );
			pendingStaged = cut.Staged;
		}
	}

	private string[]? pendingStaged;

	private ParkCommandResult Issue( ParkCommand command, string what )
	{
		var result = level.Commands.Issue( command );
		Require( result.Accepted, $"{what} ({command.Kind}: {result.Reason})" );
		return result;
	}

	// ---- The fixture -----------------------------------------------------------------------

	/// <summary>Two coasters by the coaster tool's commands, staff to pick up and the park open: the park as the session starts it.</summary>
	private void BuildFixture()
	{
		// enough money for the coasters' pylons and the staff, before the park is shared (nothing after that changes it outside the commands)
		Economy.Ledger.Post( LedgerCategory.OtherIncome, 2_000_000 );
		var used = new HashSet<(int, int)>();
		coasters.Add( BuildCoaster( used ) );
		coasters.Add( BuildCoaster( used ) );
		if ( !Economy.Staff.Members.Any( member => member.State != StaffState.PickedUp ) )
			HireStaff();
		HireStaff();
		BuildPools( used );
		PrepareTour( used );
		BuildWaterRide( used );
		MoveUnderNegativeBalance();
		if ( !Economy.IsParkOpen )
			Economy.OpenPark();
		Log.Trace( $"Co-op join smoke: Jungle with {level.Objects.Objects.Count( item => !item.IsDeleted )} objects, {coasters.Count} coasters, {Economy.Staff.Members.Count} staff, "
			+ $"{level.Guests!.Guests.Count} guests." );
	}

	/// <summary>
	/// An object put back by the Move tool while the balance is below zero: the laid cells' money test (tp.exe 0x534767) leaves its entrance without the link to the path in front of it,
	/// which a load repairs and a join keeps (docs/COOP.md, "The game's join"). Before the park is shared: the balance is taken below zero and given back outside the commands.
	/// </summary>
	private void MoveUnderNegativeBalance()
	{
		var cells = level.Guests!.Grid.Cells;
		var queued = level.Guests.Attractions.OfType<RideVisitorBridge>().Where( ride => ride.HasQueue ).Select( ride => ride.AttractionId ).ToHashSet();
		bool FacesPath( ObjectAccessPoint point ) => point.Kind == ObjectCellKind.Entrance && cells.InBounds( point.OutsideX, point.OutsideY )
			&& cells.RawTypeAt( point.OutsideX, point.OutsideY ) == (byte)ParkCellType.Path;
		// the first cut's Move takes the first of these objects (StageFirstCut): this one is the last, so its cells stay as this move leaves them
		var movable = level.Objects.Objects.Where( item => !item.IsDeleted && item.Runtime.Coaster == null && !item.Entry.IsFixedItem && level.GetEconomyInstance( item ) != null
			&& !queued.Contains( item.Visitors.AttractionId ) ).OrderBy( item => item.Visitors.AttractionId ).ToList();
		// not a pool's object: its swimmers would leave the water with it
		var item = movable.Skip( 1 ).LastOrDefault( candidate => !candidate.Entry.HasQueue && PoolAttractions.KindOf( candidate.Entry ) == PoolAttractionKind.None
			&& !PoolChangingRooms.IsChangingRoom( candidate.Entry ) && candidate.AccessPoints.Any( FacesPath ) );
		Require( item != null, "an object with its entrance on a path, besides the one the first cut's Move takes" );
		var (entry, x, y, rotation) = (item!.Entry, item.Placement.X, item.Placement.Y, item.Placement.Rotation);
		Issue( new PickUpObjectCommand( item.Visitors.AttractionId ), "an object is picked up to be put back under a negative balance" );
		var balance = Economy.Balance;
		Economy.Ledger.Post( LedgerCategory.OtherCosts, balance + 1 );
		var click = Level.GetClickCell( entry, x, y, rotation );
		Issue( new PlaceObjectCommand( ParkCommandCodec.EntryKey( entry ), click.X, click.Y, rotation, Moving: true ), "the object is put back while the balance is below zero" );
		Economy.Ledger.Post( LedgerCategory.OtherIncome, balance - Economy.Balance );
		unlinked = level.Objects.Objects.LastOrDefault( other => !other.IsDeleted && ReferenceEquals( other.Entry, entry ) && (other.Placement.X, other.Placement.Y) == (x, y) );
		Require( unlinked != null && UnlinkedEntrance( level ), $"{entry.DisplayName}'s entrance is left without its link, which a load would repair" );
		Log.Trace( $"Co-op join smoke: {entry.DisplayName} put back at ({x}, {y}) under a negative balance, its entrance without its link." );
	}

	/// <summary>The object put back under a negative balance (<see cref="MoveUnderNegativeBalance"/>).</summary>
	private OriginalObject? unlinked;

	/// <summary>Whether that object's entrance still lacks its link in <paramref name="park"/>: a load's repair of a copy of its cells would lay it.</summary>
	private bool UnlinkedEntrance( Level park ) => unlinked is { IsDeleted: false } item && park.Guests?.Grid.Cells is { } map
		&& ObjectAccessCells.RepairMissingLinks( ParkLayoutSnapshot.RestoreCellMap( ParkLayoutSnapshot.CaptureSnapshot( map, park.Objects ) ), item.AccessPoints, item.Entry.HasQueue ) > 0;

	/// <summary>A coaster station on a free site near the paths, its circuit built and completed by the coaster tool's commands (the sandbox smoke's circuit).</summary>
	private OriginalObject BuildCoaster( HashSet<(int, int)> used )
	{
		var stations = level.Objects.Catalog.Buildable.Where( item => CoasterDefinition.IsCoaster( item.Settings ) ).OrderBy( item => item.InfoId ).ToList();
		Require( stations.Count > 0, "the theme's catalogue has a coaster station" );
		var entry = stations[coasters.Count % stations.Count];
		var grid = level.Objects.Grid;
		var cells = level.Guests!.Grid.Cells;
		var focus = OriginalParkPlacement.TryGetCell( level.OriginalPark!.Heightfield, level.PathFocus.X, level.PathFocus.Y, out var focusX, out var focusY ) ? (X: focusX, Y: focusY) : (X: 64, Y: 64);
		bool Free( (int X, int Y) cell ) => level.Objects.FindAt( cell.X, cell.Y ) == null && grid.CheckTerrain( cell.X, cell.Y ) == OriginalPlacementResult.Allowed
			&& !cells.FlagsAt( cell.X, cell.Y ).HasFlag( ParkCellFlags.Unowned ) && !used.Contains( cell );
		foreach ( var (x, y) in Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - focus.X ) + Math.Abs( cell.Y - focus.Y ) ) )
		{
			if ( level.Objects.CheckFree( entry, x, y, 0 ) != OriginalPlacementResult.Allowed || CoasterBuilder.StationEnds( entry.Shape, x, y, 0 ) is not { } ends )
				continue;
			var circuit = Circuit.Select( offset => (ends.Depart.X + offset.X, ends.Depart.Y + offset.Y) ).Append( ends.Return ).ToArray();
			var footprint = ObjectFootprint.GetCells( entry.Shape, x, y, 0 ).Select( cell => (cell.X, cell.Y) ).ToArray();
			if ( !circuit.All( Free ) || footprint.Any( used.Contains ) )
				continue;
			var click = Level.GetClickCell( entry, x, y, 0 );
			var placed = level.Commands.Issue( new PlaceObjectCommand( ParkCommandCodec.EntryKey( entry ), click.X, click.Y, 0 ) );
			OriginalObject? station;
			if ( placed.Accepted )
				station = level.Objects.Objects.LastOrDefault( item => !item.IsDeleted && item.Runtime.Coaster != null && !coasters.Contains( item ) );
			else
			{
				// the seed has not researched every coaster: before the park is shared the station is placed as the sandbox smoke places one without a purchase (the session's start binds it
				// to the economy, CoopSession.Host)
				Log.Trace( $"Co-op join smoke: the build tool refused {entry.DisplayName} at ({x}, {y}) ({placed.Reason}); placed without a purchase." );
				station = level.Objects.TryPlace( entry, x, y, 0, out _ );
				if ( station == null )
					continue;
			}
			Require( station?.Runtime.Coaster != null, $"a coaster station of {entry.DisplayName} with its controller" );
			var id = station!.Visitors.AttractionId;
			Issue( new BeginCoasterToolCommand( id ), "the coaster tool starts on the new station" );
			var builder = level.GetCoasterBuilder( station )!;
			var depart = builder.Track.DepartCell!.Value;
			foreach ( var (dx, dy) in Circuit )
				Issue( new CoasterClickCommand( id, depart.X + dx, depart.Y + dy, builder.Height, false, 0 ), $"a pylon at ({depart.X + dx}, {depart.Y + dy})" );
			var back = builder.Track.ReturnCell!.Value;
			Issue( new CoasterClickCommand( id, back.X, back.Y, builder.Height, false, 0 ), "a click on the station end closes the circuit" );
			Issue( new CoasterClickCommand( id, back.X, back.Y, builder.Height, false, 0 ), "a second click completes the coaster" );
			Require( builder.Track.IsRunnable && builder.Ride.IsOperational && station.IsOpen && !level.IsCoasterToolActive, "a completed coaster, open, the tool ended" );
			// the circuit's cells and a margin around them stay free of the next coaster
			foreach ( var (cx, cy) in circuit.Concat( footprint ) )
				for ( var mx = -2; mx <= 2; mx++ )
					for ( var my = -2; my <= 2; my++ )
						used.Add( (cx + mx, cy + my) );
			return station;
		}
		throw new InvalidOperationException( "Native co-op join smoke test failed: find a site for a coaster and its circuit." );
	}

	/// <summary>
	/// [EXT:pools] A pool of 6 x 3 cells near the paths with a lazy river round it, its changing room, the water attractions, a slide, a bridge over it and swimmers in it: the
	/// park-resume smoke's pools fixture, three cells deep so a bridge spans it, built before the park is shared.
	/// </summary>
	private void BuildPools( HashSet<(int, int)> used )
	{
		// the smoke runs with the default extensions (ModStartup), so the pools extension is on in both phases
		Require( FeatureGates.Pools && level.PoolBuilder != null, "the pools extension is on" );
		var builder = level.PoolBuilder!;
		var focus = OriginalParkPlacement.TryGetCell( level.OriginalPark!.Heightfield, level.PathFocus.X, level.PathFocus.Y, out var focusX, out var focusY ) ? (X: focusX, Y: focusY) : (X: 64, Y: 64);
		// three cells deep: a bridge spans three water cells (Level.BuildDebugPoolBridge)
		var shape = PoolBuilder.Rectangle( (0, 0), (5, 2) ).ToArray();
		var grid = level.Objects.Grid;
		var origin = Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - focus.X ) + Math.Abs( cell.Y - focus.Y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X )
			.Where( cell => shape.All( offset => builder.Check( cell.X + offset.X, cell.Y + offset.Y ) == PoolBuildResult.Ok
				&& !used.Contains( (cell.X + offset.X, cell.Y + offset.Y) ) ) ).Cast<(int X, int Y)?>().FirstOrDefault();
		Require( origin != null, "find a free site for a pool near the paths" );
		var cells = shape.Select( offset => (X: origin!.Value.X + offset.X, Y: origin.Value.Y + offset.Y) ).ToArray();
		Require( builder.PaintCells( cells ).Completed, "paint the pool through the pool builder" );
		foreach ( var cell in cells.Where( cell => cell.X == origin!.Value.X || cell.X == origin.Value.X + 5 || cell.Y == origin.Value.Y || cell.Y == origin.Value.Y + 2 ) )
			level.Guests!.Grid.Cells.SetFlags( cell.X, cell.Y, ParkCellFlags.PoolRiver );
		var room = level.BuildChangingRoomBeside( cells );
		Require( room != null, "build a changing room beside the pool" );
		var attractions = level.BuildPoolAttractionsBeside( cells, "all" );
		var tower = level.BuildSlideBeside( cells, PoolSlideProfile.Flume );
		var bridge = level.BuildDebugPoolBridge( cells );
		var swimmers = level.Guests!.DebugStartSwimmers( room!.Visitors, 3 );
		foreach ( var cell in cells )
			for ( var mx = -4; mx <= 4; mx++ )
				for ( var my = -4; my <= 4; my++ )
					used.Add( (cell.X + mx, cell.Y + my) );
		Require( tower != null && bridge != null && swimmers > 0, $"the pool has its slide, a bridge and swimmers (slide {tower != null}, bridge {bridge != null}, {swimmers} swimmers)" );
		Log.Trace( $"Co-op join smoke: a pool of {cells.Length} cells at {origin}, {attractions.Count} water attraction(s), a slide, a bridge, {swimmers} swimmer(s)." );
	}

	/// <summary>
	/// [EXT:online-coop-snapshot-join] The tour ride the second cut buys while the park is paused (C5: a live tour ride whose script has not created its controller yet): researched, with the
	/// golden ticket it needs, and a free site found before the park is shared, as the park-resume smoke's tour fixture prepares one.
	/// </summary>
	private void PrepareTour( HashSet<(int, int)> used )
	{
		var economy = Economy;
		var entry = level.Objects.Catalog.Buildable.Where( candidate => candidate.Settings.GetInt( "Bumper.WhichTrackType" ) == 0 && candidate.ScriptPath != null
			&& candidate.ScriptPath.Contains( "tour", StringComparison.OrdinalIgnoreCase ) ).OrderBy( candidate => candidate.InfoId ).FirstOrDefault();
		Require( entry != null, "the theme's catalogue has a tour ride" );
		var research = economy.Research;
		research.Restore( research.Completed.Append( (entry!.InfoId, 0) ).Distinct().ToList(),
			research.Items.Select( item => ((item.InfoId, item.Level), research.GetProgress( item )) ).Where( pair => pair.Item2 != 0 ).ToList(), research.Effort );
		var objectives = economy.Objectives;
		objectives.Restore( objectives.NextOfferDay, objectives.NextListPosition, objectives.Current, objectives.Declines, objectives.Finished, objectives.GoldenTickets.Append( GoldenTicketKind.Visitors ).ToList() );
		Require( economy.CheckBuild( entry.InfoId ) == ParkEconomy.PurchaseResult.Ok, $"the tour ride can be bought ({economy.CheckBuild( entry.InfoId )})" );
		var grid = level.Objects.Grid;
		bool Free( int x, int y ) => x >= 0 && y >= 0 && x < grid.Width && y < grid.Height && level.Objects.FindAt( x, y ) == null && !used.Contains( (x, y) )
			&& grid.CheckTerrain( x, y ) is OriginalPlacementResult.Allowed && level.Guests!.Grid.Cells.RawTypeAt( x, y ) == 0 && !level.Guests!.Grid.Cells.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );
		var focus = OriginalParkPlacement.TryGetCell( level.OriginalPark!.Heightfield, level.PathFocus.X, level.PathFocus.Y, out var focusX, out var focusY ) ? (X: focusX, Y: focusY) : (X: 64, Y: 64);
		foreach ( var cell in Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - focus.X ) + Math.Abs( cell.Y - focus.Y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			var anchor = Level.GetCentredAnchor( entry, cell.X, cell.Y, 0 );
			if ( level.Objects.CheckFree( entry, anchor.X, anchor.Y, 0 ) != OriginalPlacementResult.Allowed
				|| !Enumerable.Range( anchor.X - 1, 7 ).All( x => Enumerable.Range( anchor.Y - 1, 7 ).All( y => Free( x, y ) ) ) )
				continue;
			tourSite = (entry, cell.X, cell.Y);
			for ( var x = anchor.X - 2; x <= anchor.X + 7; x++ )
				for ( var y = anchor.Y - 2; y <= anchor.Y + 7; y++ )
					used.Add( (x, y) );
			return;
		}
		throw new InvalidOperationException( "Native co-op join smoke test failed: find a free site for the tour ride." );
	}

	/// <summary>Hires the cheapest candidate onto a free cell of the paths, by the hire tool's commands.</summary>
	private void HireStaff()
	{
		var candidate = Economy.Staff.Candidates.FirstOrDefault();
		Require( candidate != null, "the staff list offers a candidate" );
		Issue( new BeginStaffHireCommand( candidate!.Id ), "a hire begins" );
		var hands = level.StaffHands!;
		var grid = level.Guests!.Grid;
		var cell = Enumerable.Range( 0, 128 * 128 ).Select( index => (X: index % 128, Y: index / 128) )
			.Where( cell => grid.IsWalkable( cell.X, cell.Y ) && hands.IsValidCell( cell.X, cell.Y ) ).Cast<(int X, int Y)?>().FirstOrDefault();
		Require( cell != null, "a free path cell to put the new employee on" );
		Issue( new PlaceStaffCommand( candidate.Id, cell!.Value.X, cell.Value.Y ), "the new employee is put down" );
	}

	/// <summary>The session over the recording transport, on the smoke's clock and a fixed afternoon; its captures checked at the cut and when stored.</summary>
	private void Host()
	{
		transport = new CoopSmokeHostTransport( () => hostFrame );
		session = CoopSession.Host( level, transport, () => clock, () => new DateTime( 2026, 10, 21, 15, 0, 0 ).AddSeconds( clock ) );
		var stream = session.HostStream!;
		stream.CaptureFault = park =>
		{
			var digest = CoopDigest.Compute( park, out var world );
			atCut = (digest, world, park.CaptureRandomState(), StagedAtCut( park ));
		};
		stream.CaptureStored += ( generation, payload ) => stored[generation] = payload;
		Log.Trace( $"Co-op join smoke: the park is hosted at tick {level.TickCount}." );
	}

	// ---- The staged state ------------------------------------------------------------------

	/// <summary>Before the first cut: an employee in hand, a pending hire, a carried Move entry and the coaster tool open on the second coaster, all by the tools' commands.</summary>
	private void StageFirstCut()
	{
		// first: entering the track tool cancels the staff placement, which holds the hand and the hire staged below
		CutWaterTrack();
		var hands = level.StaffHands!;
		foreach ( var member in Economy.Staff.Members.ToArray() )
			if ( level.Commands.Issue( new PickUpStaffCommand( member.Id ) ).Accepted )
				break;
		hand = hands.Of( ParkCommands.LocalPlayer );
		Require( hand != null, "an employee is picked up into the hand" );
		var candidate = Economy.Staff.Candidates.FirstOrDefault();
		Require( candidate != null, "the staff list offers a candidate to hire" );
		Issue( new BeginStaffHireCommand( candidate!.Id ), "a hire waits to be placed" );
		var rides = level.Guests!.Attractions.OfType<RideVisitorBridge>().Where( ride => ride.HasQueue ).Select( ride => ride.AttractionId ).ToHashSet();
		var movable = level.Objects.Objects.Where( item => !item.IsDeleted && item.Runtime.Coaster == null && !item.Entry.IsFixedItem && level.GetEconomyInstance( item ) != null
			&& !rides.Contains( item.Visitors.AttractionId ) ).OrderBy( item => item.Visitors.AttractionId ).ToList();
		foreach ( var item in movable )
		{
			var placement = (item.Entry, item.Placement.X, item.Placement.Y, item.Placement.Rotation);
			if ( level.Commands.Issue( new PickUpObjectCommand( item.Visitors.AttractionId ) ).Accepted )
			{
				carried = placement;
				break;
			}
		}
		Require( carried != null, "an object is picked up by the Move tool" );
		Issue( new BeginCoasterToolCommand( coasters[1].Visitors.AttractionId ), "the coaster tool opens on a built coaster" );
		Require( !coasters[1].IsOpen, "the coaster tool closed its coaster" );
	}

	/// <summary>The staged state at a cut, from the join state the capture carries and the park: what the cut is checked to hold.</summary>
	private string[] StagedAtCut( Level park )
	{
		var state = park.CaptureJoinState();
		var staged = new List<string>();
		if ( state.StaffHands.Count > 0 ) staged.Add( "employee in hand" );
		if ( state.PendingHire != 0 ) staged.Add( "pending hire" );
		if ( state.CarriedEntries.Count > 0 ) staged.Add( "carried Move entry" );
		if ( state.CoastersBeingEdited.Count > 0 ) staged.Add( "coaster tool open" );
		if ( state.SessionClockTicks != null ) staged.Add( "session clock" );
		if ( park.Coasters.Count >= 2 ) staged.Add( "several coasters" );
		if ( park.Park?.Economy.Speed == GameSpeed.Paused && park.Commands.ScheduledCount == 0 && park.Commands.Log.Count > 0 && park.Commands.Log[^1].Tick == park.TickCount ) staged.Add( "paused with edits since its last tick" );
		if ( coasters.Any( coaster => !coaster.IsDeleted && !coaster.IsOpen && coaster.Runtime.Coaster is { BeingEdited: false, State: CoasterControllerState.Built } ) ) staged.Add( "built coaster closed" );
		if ( park.Objects.Objects.Any( item => !item.IsDeleted && item.Runtime.UsesTour && !item.Runtime.IsStopped && item.Runtime.LiveTour == null ) ) staged.Add( "tour ride without a live tour" );
		if ( park.Guests?.Grid.Cells is { } map && Enumerable.Range( 0, map.Width * map.Height ).Any( index => map.TypeAt( index % map.Width, index / map.Width ) == ParkCellType.PoolWater
			&& map.FlagsAt( index % map.Width, index / map.Width ).HasFlag( ParkCellFlags.PoolRiver ) ) ) staged.Add( "pool with a river" );
		if ( UnlinkedEntrance( park ) ) staged.Add( "entrance without its link" );
		staged.AddRange( WaterStagedAtCut( park ) );
		if ( park.PoolSlides?.Slides.Any() == true ) staged.Add( "slide" );
		if ( park.PoolBridges?.Count > 0 ) staged.Add( "bridge" );
		if ( park.Guests?.Guests.Any( guest => guest.IsSwimming ) == true ) staged.Add( "swimmers" );
		return staged.ToArray();
	}

	/// <summary>After the first cut: the hand put down where it was, the hire cancelled, the carried object placed back, the coaster tool left.</summary>
	private void UnstageFirstCut()
	{
		Issue( new DropStaffCommand( hand!.Value.Staff, hand.Value.X, hand.Value.Y ), "the employee is put back down" );
		Issue( new CancelStaffHireCommand(), "the pending hire is cancelled" );
		var (entry, x, y, rotation) = carried!.Value;
		var click = Level.GetClickCell( entry, x, y, rotation );
		if ( !level.Commands.Issue( new PlaceObjectCommand( ParkCommandCodec.EntryKey( entry ), click.X, click.Y, rotation, Moving: true ) ).Accepted )
			Log.Trace( "Co-op join smoke: the carried object stays in the hand (its place was taken meanwhile)." );
		Issue( new EndCoasterToolCommand( coasters[1].Visitors.AttractionId ), "the coaster tool closes" );
		MendWaterTrack();
	}

	/// <summary>
	/// Before the second cut: the park paused with edits since its last tick: the entrance fee, and a coaster closed while built (state 2: the coaster tool's exit leaves it built until its
	/// script opens it, which a paused park does not run), as the park's door closes one; a restore would let it out again.
	/// </summary>
	private void StageSecondCut()
	{
		Issue( new SetGameSpeedCommand( GameSpeed.Paused ), "the park is paused" );
		Issue( new SetEntranceFeeCommand( Economy.EntranceFee + 3 ), "the entrance fee changes while paused" );
		var coaster = coasters[1];
		Issue( new BeginCoasterToolCommand( coaster.Visitors.AttractionId ), "the coaster tool opens while paused" );
		Issue( new EndCoasterToolCommand( coaster.Visitors.AttractionId ), "the coaster tool closes, the coaster built" );
		Issue( new SetAttractionOpenCommand( coaster.Visitors.AttractionId, false ), "the built coaster is closed" );
		Require( !coaster.IsOpen && coaster.Runtime.Coaster is { BeingEdited: false, State: CoasterControllerState.Built }, "a coaster closed while built, its tool not open" );
		SellAddOnOffTheTrack();
		// a tour ride bought while paused: its script has not run, so it has no controller yet (C5)
		var (entry, x, y) = tourSite!.Value;
		Issue( new PlaceObjectCommand( ParkCommandCodec.EntryKey( entry ), x, y, 0 ), "the tour ride is bought while paused" );
	}

	/// <summary>The scripted edits of the park minutes: the entrance fee, a ride closed and opened again, a hire placed, an employee moved.</summary>
	private void Script( int at )
	{
		switch ( at % 600 )
		{
			case 50:
				Issue( new SetEntranceFeeCommand( Math.Max( 1, Economy.EntranceFee + (at / 600 % 2 == 0 ? 1 : -1) ) ), "the entrance fee changes" );
				break;
			case 150:
				Issue( new SetAttractionOpenCommand( coasters[1].Visitors.AttractionId, false ), "a coaster closes" );
				break;
			case 300:
				Issue( new SetAttractionOpenCommand( coasters[1].Visitors.AttractionId, true ), "the coaster opens again" );
				break;
			case 420 when Economy.Staff.Members.Count < 12:
				HireStaff();
				break;
		}
	}

	// ---- The cuts and the end ---------------------------------------------------------------

	/// <summary>Whether the capture of the virtual member's <paramref name="number"/>th assignment has been answered as ready: then it is checked and recorded.</summary>
	private bool TookCut( int number )
	{
		var readies = transport.Messages.Where( message => message.Message is CoopSnapshotReady ).ToList();
		var failed = transport.Messages.FirstOrDefault( message => message.Message is CoopSnapshotFailed );
		Require( failed.Message == null, $"the host's capture failed: {failed.Message}" );
		if ( readies.Count < number )
		{
			Require( stageFrame < CaptureFrames, $"capture {number} was answered within {CaptureFrames} frames" );
			return false;
		}
		var (readyFrame, message) = readies[number - 1];
		var ready = (CoopSnapshotReady)message;
		var (assignedFrame, assignment) = transport.Messages.First( item => item.Message is CoopSnapshotAssigned && item.Message.Generation == ready.Generation );
		var assigned = ((CoopSnapshotAssigned)assignment).Frame;
		Require( stored.TryGetValue( ready.Generation, out var payload ) && Sha256Hex( payload ) == ready.Sha256 && payload.Length == ready.Bytes, "the Ready names the stored capture" );
		var decoded = CoopJoinPayload.Decode( payload! );
		Require( (decoded.Generation, decoded.Records, decoded.Ticks, decoded.Frame, decoded.Digest, decoded.WorldHash) == (ready.Generation, ready.Records, ready.Ticks, ready.Frame, ready.Digest,
			ready.WorldHash), "the capture's cut is the Ready's" );
		var staged = pendingStaged ?? throw new InvalidOperationException( "Native co-op join smoke test failed: the capture's cut was seen." );
		pendingStaged = null;
		string[] expected = number == 1
			? new[] { "employee in hand", "pending hire", "carried Move entry", "coaster tool open", "session clock", "several coasters", "entrance without its link",
				"water track cut open after a Backspace", "pool with a river", "slide", "bridge", "swimmers" }
			: new[] { "session clock", "several coasters", "paused with edits since its last tick", "built coaster closed", "tour ride without a live tour", "entrance without its link",
				"add-on sold off a track", "pool with a river", "slide", "bridge" };
		Require( expected.All( staged.Contains ), $"cut {number} holds what was staged for it (held: {string.Join( ", ", staged )})" );
		var file = $"capture-{number}.otpwjoin";
		File.WriteAllBytes( Path.Combine( evidenceDirectory, file ), payload! );
		cuts.Add( new CoopSmokeCut( ready.Generation, assigned, assignedFrame, ready.Records, ready.Ticks, ready.Frame, ready.Digest, ready.WorldHash, ready.Sha256, ready.Bytes, readyFrame, file, staged ) );
		Log.Trace( $"Co-op join smoke: capture {number} (generation {ready.Generation}) at frame {ready.Frame}, tick {ready.Ticks}, record {ready.Records}: {payload!.Length / 1024.0:0} KiB, "
			+ $"held {string.Join( ", ", staged )}." );
		return true;
	}

	private void FinishHost()
	{
		var stream = session.HostStream!;
		stream.PublishNow();
		Require( level.Commands.ScheduledCount == 0, "no command waits at the end" );
		var digest = CoopDigest.Compute( level, out var world );
		var save = ParkResumeSaveFile.Serialize( level.CaptureJoinSnapshot() );
		File.WriteAllBytes( Path.Combine( evidenceDirectory, "host-final-save.json" ), save );
		File.WriteAllBytes( Path.Combine( evidenceDirectory, "host-final-state.bin" ), level.CaptureJoinState().Encode() );
		WriteFrames( transport.Frames );
		var evidence = HostEvidence( completed: true, failure: null ) with { FinalDigest = digest, FinalWorldHash = world, FinalSaveSha256 = Sha256Hex( save ) };
		WriteJson( "host-evidence.json", evidence );
		session.End( "The smoke ended." );
		Next( HostStage.Done );
		Complete( $"Native co-op join smoke (host): {transport.Frames.Count} frames to frame {evidence.LastFrame}, tick {evidence.LastTick}, record {evidence.LastRecord}; "
			+ $"{cuts.Count} captures; digest {digest:X16}." );
	}

	private CoopSmokeHostEvidence HostEvidence( bool completed, string? failure ) => new( completed, failure, level?.LevelName ?? "jungle", ParkStartKind.InstantAction.ToString(),
		coasters.Count, cuts.ToArray(), transport?.Frames.Count > 0 ? transport.Frames[^1].Seq : 0, level?.TickCount ?? 0, session?.HostStream?.LastRecord ?? 0, 0, 0, "", hostFrame );
}
