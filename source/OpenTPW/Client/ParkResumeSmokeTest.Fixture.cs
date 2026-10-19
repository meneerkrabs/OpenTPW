using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// The capture phase's park fixture: a full-simulation Jungle park built through the user paths — one ride placed
/// from the HUD build menu with its queue laid by the queue tool's clicks, a second copy of the same ride placed
/// through the authoritative placement at another rotation with its queue built and then edited cell by cell, two
/// same-catalogue shops with distinct prices, a toilet, a demolished object (an id hole), staff, and real guests
/// seeded on the paths so queues, boarding and rides happen through the original scripts. The opt-in prototype
/// variant (<see cref="PrototypeVariant"/>) also places the developer Totem beside the spine, so the whole
/// save/resume pair proves the prototype part natively; the default fixture is exactly what it was.
/// </summary>
internal sealed partial class ParkResumeSmokeTest
{
	private const int QueueLength = 3;
	private const int SpineLength = 10;
	private const int DrinksShopInfoId = 1203;
	private const int SmallToiletInfoId = 1402;

	private void BuildFixture()
	{
		var hud = flow.Hud ?? throw new InvalidOperationException( "Native park-resume smoke test failed: the started level has no HUD." );
		var hudContext = flow.Context;
		var economy = level.Park!.Economy;

		// A path spine out of the park's initial entrance path: the same builder the HUD path tool commits through.
		// The fresh park's strip sits at the map edge, so the spine walks step by step — over existing path cells,
		// then laying fresh ones — always preferring the current direction and the way to the park's interior.
		var (start, spineEnd, laid, spineCells) = LaySpine();
		Require( laid >= SpineLength, $"lay at least {SpineLength} fresh path cells (laid {laid})" );

		// One ride from the HUD build menu, as a player would: the cheapest available HasQueue ride.
		// [EXT:mods] the mod variant takes the shipped Theme Park Inc mod's Turban Bounce (a flat ride with a queue), else the build menu's cheapest ride that a mod brings (OPENTPW_SMOKE_MODS=1), whose copies, queues and save are then the mod's
		var item = hud.Catalog.GetItems( BuildCategory.Rides )
			.Where( candidate => candidate.Entry is { HasQueue: true } && ( !ModVariant || candidate.Entry.ModId != null ) && hud.Status.IsAvailable( candidate )
				&& (hud.Status.PriceOf( candidate ) ?? candidate.Cost) <= hud.Status.Money )
			.OrderBy( candidate => ModVariant && string.Equals( candidate.Entry!.ArchiveName, "tbounce", StringComparison.OrdinalIgnoreCase ) ? 0 : 1 )
			.ThenBy( candidate => hud.Status.PriceOf( candidate ) ?? candidate.Cost ).FirstOrDefault();
		Require( item?.Entry != null, ModVariant ? "find an available, affordable HasQueue ride that a mod brings in the build menu (enable the mod, its extension and its input: scripts/smoke-tpi-rides.sh)" : "find an available, affordable HasQueue ride in the build menu" );
		Require( !ModVariant || ParkResumeMods.Capture( OpenTPW.Mods.OpenTpwExtensions.Active ) != null, "a park with a mod's ride records the mod that brings it" );
		var entry = item!.Entry!;
		var rideA = PlaceRideThroughHud( hud, hudContext, item, entry );

		// A second copy of the same ride, placed through the authoritative path at another rotation, its queue
		// built cell by cell and then edited: one cell more, one removed again.
		var siteB = FindRideSite( entry, rotation: 180, near: spineEnd, requireOnScreen: false, out var doorB );
		var rideB = level.PlaceObject( entry, siteB.X, siteB.Y, 180 );
		Require( rideB != null && rideB!.Visitors.HasCells && rideB.Visitors.HasQueue, $"place the second {entry.DisplayName} copy at rotation 180" );
		// a built ride with a queue starts closed (RIDES-015, 0x4DB712): the fixture opens it as the player would
		rideB.Open();
		var bridge = rideB.Visitors;
		Require( level.EnterQueueTool( bridge ), "start the queue tool for the second copy" );
		var lineB = LineOut( doorB, QueueLength + 1 );
		foreach ( var cell in lineB )
		{
			// the placement lays the queue cell outside the entrance itself (ObjectAccessCells), so the line starts on it
			if ( level.Guests!.Grid.IsQueue( cell.X, cell.Y ) && bridge.QueueCells.Contains( cell ) )
				continue;
			Require( level.BuildQueueCell( bridge, cell.X, cell.Y ) == QueueBuildResult.Ok, $"build the second copy's queue cell {(cell.X, cell.Y)}" );
			Require( level.Guests!.Grid.IsQueue( cell.X, cell.Y ), $"the built queue cell {(cell.X, cell.Y)} is a queue cell" );
		}
		Require( level.RemoveQueueCell( lineB[^1].X, lineB[^1].Y ) >= 1, "edit the queue: remove its last cell again" );
		Require( bridge.QueueCells.Count >= QueueLength, $"the edited queue keeps {QueueLength} cells" );
		QueuePaths.TryConnect( level.Guests!.Grid, bridge );

		Require( !ReferenceEquals( rideA, rideB ) && rideA.Visitors.AttractionId != bridge.AttractionId,
			"the two copies of the ride are distinct attractions" );
		Require( rideA.Visitors.HasCells && rideA.Visitors.EntranceCell != bridge.EntranceCell,
			"the two copies' entrances sit at distinct cells (distinct rotations)" );

		// Two shops from the same catalogue entry with distinct prices, and a toilet.
		var catalog = level.Objects.Catalog;
		var drinks = catalog.Find( DrinksShopInfoId );
		var shopEntry = drinks != null && economy.Research.IsAvailable( DrinksShopInfoId ) && economy.Balance >= drinks.BuildCost * 2 ? drinks
			: catalog.Buildable.FirstOrDefault( candidate => candidate.Category == ObjectCategory.Shop
				&& economy.Research.IsAvailable( candidate.InfoId ) && economy.Balance >= candidate.BuildCost * 2 );
		Require( shopEntry != null, "find an available, affordable shop in the catalogue" );
		var shopA = PlaceAuthoritatively( shopEntry!, rotation: 0, near: spineEnd );
		var shopB = PlaceAuthoritatively( shopEntry!, rotation: 90, near: spineEnd );
		Require( shopA != null && shopB != null && shopA!.Visitors.AttractionId != shopB!.Visitors.AttractionId, "place two copies of the shop" );
		var instanceA = level.GetEconomyInstance( shopA )!.Value;
		var instanceB = level.GetEconomyInstance( shopB )!.Value;
		Require( instanceA != instanceB, "the two shops are distinct economy instances" );
		economy.SetPrices( instanceA, 60 );
		economy.SetPrices( instanceB, 120 );
		ParkObjectState? pricedA = null, pricedB = null;
		Require( economy.TryGetObject( instanceA, out pricedA ) && economy.TryGetObject( instanceB, out pricedB ), "read both shop instances back" );
		Require( pricedA!.Price == 60 && pricedB!.Price == 120, "the two copies carry distinct prices" );
		var toiletEntry = economy.Research.IsAvailable( SmallToiletInfoId ) ? catalog.Find( SmallToiletInfoId )
			: catalog.Buildable.FirstOrDefault( candidate => candidate.Category == ObjectCategory.Feature && economy.Research.IsAvailable( candidate.InfoId ) );
		if ( toiletEntry != null )
			Require( PlaceAuthoritatively( toiletEntry, rotation: 0, near: spineEnd ) != null, "place the toilet" );

		// A demolished object leaves an id hole the restore must adopt, never burn.
		var sacrifice = catalog.Buildable.Where( candidate => economy.Research.IsAvailable( candidate.InfoId )
				&& candidate.Category is ObjectCategory.Feature or ObjectCategory.Shop && economy.Balance >= candidate.BuildCost
				// the pools extension's objects need water beside them: the generic fixture picks from the original's objects ([EXT:evening-torches] the evening extension's torches, at 50 the cheapest feature, likewise)
				&& candidate.ModId != OpenTPW.Mods.OfficialExtensions.Pools.Id && candidate.ModId != OpenTPW.Mods.OfficialExtensions.Evening.Id )
			.OrderBy( candidate => candidate.BuildCost ).FirstOrDefault();
		if ( sacrifice != null )
		{
			var doomed = PlaceAuthoritatively( sacrifice, rotation: 0, near: spineEnd );
			Require( doomed != null, "place the sacrificial object" );
			var cell = doomed!.Cells.First();
			Require( level.RemoveObjectAt( cell.X, cell.Y ), "demolish the sacrificial object for an id hole" );
		}

		// The prototype variant's developer Totem: placed last (after the budgeted picks above, so its catalogue
		// purchase cannot drain their balance) near the spine, whose walkable cells give its entrance derivation a
		// queue front. Its purchase outcome — bought, or registered uncharged when research refuses — is saved either way.
		PrototypeRide? totem = null;
		if ( PrototypeVariant )
		{
			var walkGrid = level.Guests!.Grid;
			var sites = Enumerable.Range( 0, walkGrid.CountX * walkGrid.CountY ).Select( index => (X: index % walkGrid.CountX, Y: index / walkGrid.CountX) )
				.OrderBy( cell => Math.Abs( cell.X - spineEnd.X ) + Math.Abs( cell.Y - spineEnd.Y ) );
			foreach ( var cell in sites )
			{
				if ( level.PlaceRide( OriginalParkPlacement.GetCellCenter( level.OriginalPark!.Heightfield, cell.X, cell.Y ) ) )
					break;
			}
			totem = level.PlacedRide;
			Require( totem != null && totem.Visitors.HasCells, "place the developer Totem near the spine and register it with the guests" );
			Require( level.Park!.Guests!.TryGetInstance( totem!.Visitors.AttractionId, out _ ),
				"the Totem binds an economy instance through its catalogue entry" );
			totem.Start();
			Log.Trace( $"Park-resume fixture: developer Totem placed and opened (attraction {totem.Visitors.AttractionId}, queue at {totem.Visitors.EntranceCell})." );
		}

		// The water variant's bought Splish Splash with a closed circuit (OPENTPW_SMOKE_WATER=1, ParkResumeSmokeTest.Water.cs).
		if ( WaterVariant )
			PlaceWaterFixture( spineEnd );
		// The tour variant's bought tour ride (OPENTPW_SMOKE_TOUR=1, ParkResumeSmokeTest.Tour.cs).
		if ( TourVariant )
			PlaceTourFixture( spineEnd );
		// The bumper variant's bought bumper ride (OPENTPW_SMOKE_BUMPER=1, ParkResumeSmokeTest.Bumper.cs).
		if ( BumperVariant )
			PlaceBumperFixture( spineEnd );
		// The pools variant's pool, changing room and swimmers (OPENTPW_SMOKE_POOLS=1, ParkResumeSmokeTest.Pools.cs).
		if ( PoolsVariant )
			PlacePoolsFixture( spineEnd );

		// Staff for the staff world to travel.
		foreach ( var type in new[] { StaffType.Mechanic, StaffType.Entertainer } )
		{
			var candidate = economy.Staff.Candidates.FirstOrDefault( member => member.Type == type );
			if ( candidate != null )
				economy.Hire( candidate.Id );
		}
		Require( economy.Staff.Members.Count >= 2, "hire staff into the fresh park" );

		SeedGuests( rideA, rideB, shopA, shopB, spineCells, totem );
		Log.Trace( $"Park-resume fixture: spine {start} -> {spineEnd}, rides {rideA.Entry.DisplayName} #{rideA.Visitors.AttractionId}/#{bridge.AttractionId}, "
			+ $"shops #{shopA!.Visitors.AttractionId}/#{shopB!.Visitors.AttractionId} at ${pricedA!.Price}/${pricedB!.Price}, {level.Objects.Objects.Count} objects, "
			+ $"{economy.Staff.Members.Count} staff, {level.Guests!.Guests.Count} seeded guests, balance ${economy.Balance}." );
		stage = Stage.Populate;
	}

	// ---- Placement helpers -----------------------------------------------------------------------

	/// <summary>Places one ride from the HUD build menu with synthetic clicks, as the sandbox smoke test's queue-tool check does.</summary>
	private OriginalObject PlaceRideThroughHud( ParkHud hud, UiContext hudContext, BuildItem item, ObjectCatalogEntry entry )
	{
		var canvas = hudContext.Canvas;
		var viewport = new System.Numerics.Vector2( canvas.Width, canvas.Height );
		var site = FindRideSite( entry, rotation: 0, near: FirstWalkable(), requireOnScreen: true, out var entrance );
		bool Clickable( (int X, int Y) cell, out System.Numerics.Vector2 point ) =>
			level.TryProjectCell( cell.X, cell.Y, viewport, out point )
			&& point.X > canvas.Width * 0.15f && point.X < canvas.Width * 0.85f && point.Y > canvas.Height * 0.1f && point.Y < canvas.Height * 0.8f
			&& !hud.Stack.Covers( canvas, point )
			&& level.TryGetGridCell( point, viewport, out var x, out var y ) && (x, y) == cell;
		Clickable( site, out var sitePoint );

		hud.BeginPlacing( item );
		Require( level.BuildEntry == entry, "choosing the ride in the build menu arms its placement" );
		hud.Update( hudContext, UiInput.Idle( sitePoint ) );
		var captured = hud.Update( hudContext, new UiInput( sitePoint, true, true, false, false, UiKeys.None ) );
		level.UpdateWorldInput( false, captured, sitePoint, viewport );
		level.UpdateWorldInput( true, captured, sitePoint, viewport );
		hud.Update( hudContext, new UiInput( sitePoint, false, false, true, false, UiKeys.None ) );
		level.UpdateWorldInput( false, false, sitePoint, viewport );
		var placed = level.Objects.FindAt( entrance.X, entrance.Y );
		Require( placed?.Entry == entry, $"a HUD park click places {entry.DisplayName}" );
		Require( placed!.IsOpen == !entry.HasQueue, "a built ride with a queue starts closed (RIDES-015, 0x4DB712)" );
		placed.Open();
		Require( level.CellTool.IsActive && level.CellTool.Mode == CellToolMode.Queue && level.CellTool.Start == (entrance.OutsideX, entrance.OutsideY),
			"placing the ride starts the queue tool at its entrance" );

		var end = LineOut( entrance, QueueLength )[^1];
		Clickable( end, out var endPoint );
		hud.Update( hudContext, UiInput.Idle( endPoint ) );
		hud.Update( hudContext, new UiInput( endPoint, true, true, false, false, UiKeys.None ) );
		hud.Update( hudContext, new UiInput( endPoint, false, false, true, false, UiKeys.None ) );
		hud.Update( hudContext, new UiInput( endPoint, true, true, false, false, UiKeys.None ) );
		hud.Update( hudContext, new UiInput( endPoint, false, false, true, false, UiKeys.None ) );
		Require( !level.CellTool.IsActive, "clicking the queue's end again ends the queue tool" );
		Require( QueuePaths.TryConnect( level.Guests!.Grid, placed!.Visitors ), "the queue's back is linked to the spine path" );
		Require( placed!.Visitors.QueueCells.Count >= QueueLength, "the HUD clicks laid the ride's queue" );
		return placed;
	}

	/// <summary>
	/// A ride site with room for a straight queue out of the entrance: placeable, every queue cell buildable, and
	/// (for the HUD copy) projectable on-screen. Sites are searched outward from <paramref name="near"/>.
	/// </summary>
	private (int X, int Y) FindRideSite( ObjectCatalogEntry entry, int rotation, (int X, int Y) near, bool requireOnScreen, out ObjectAccessPoint entrance )
	{
		ParkHud? hud = flow.Hud;
		var canvas = flow.Context.Canvas;
		var viewport = new System.Numerics.Vector2( canvas.Width, canvas.Height );
		bool Clickable( (int X, int Y) cell, out System.Numerics.Vector2 point )
		{
			point = default;
			if ( hud == null )
				return false;
			return level.TryProjectCell( cell.X, cell.Y, viewport, out point )
				&& point.X > canvas.Width * 0.15f && point.X < canvas.Width * 0.85f && point.Y > canvas.Height * 0.1f && point.Y < canvas.Height * 0.8f
				&& !hud.Stack.Covers( canvas, point )
				&& level.TryGetGridCell( point, viewport, out var x, out var y ) && (x, y) == cell;
		}
		var grid = level.Guests!.Grid;
		var candidates = Enumerable.Range( 0, grid.CountX * grid.CountY ).Select( index => (X: index % grid.CountX, Y: index / grid.CountX) )
			.OrderBy( cell => Math.Abs( cell.X - near.X ) + Math.Abs( cell.Y - near.Y ) );
		foreach ( var cell in candidates )
		{
			if ( level.CheckFreeObject( entry, cell.X, cell.Y, rotation ) != OriginalPlacementResult.Allowed || ( requireOnScreen && !Clickable( cell, out _ ) ) )
				continue;
			// a ride may now stand on plain path (the placement clears it), which would cut the spine this fixture's queues join: keep off it
			var anchor = Level.GetCentredAnchor( entry, cell.X, cell.Y, rotation );
			if ( ObjectFootprint.GetCells( entry.Shape, anchor.X, anchor.Y, rotation ).Any( part => grid.IsWalkable( part.X, part.Y ) || grid.IsQueue( part.X, part.Y ) ) )
				continue;
			var door = DoorAt( entry, cell.X, cell.Y, rotation );
			var line = LineOut( door, QueueLength + 1 );
			// the queue's back must touch the spine, as a player ends a queue line on a path cell: the placement no longer leaves a walkable entrance for guests to fall back on
			var back = line[QueueLength - 1];
			if ( requireOnScreen && !new[] { (0, -1), (1, 0), (0, 1), (-1, 0) }.Any( step => grid.IsWalkable( back.X + step.Item1, back.Y + step.Item2 ) ) )
				continue;
			if ( line.All( queueCell => level.Paths!.CheckCell( queueCell.X, queueCell.Y ) == CellBuildResult.Ok )
				&& ( !requireOnScreen || line.All( queueCell => Clickable( queueCell, out _ ) ) ) )
			{
				entrance = door;
				return cell;
			}
		}
		throw new InvalidOperationException( $"Native park-resume smoke test failed: no {( requireOnScreen ? "on-screen " : "" )}site for {entry.DisplayName} "
			+ $"at rotation {rotation} with room for a {QueueLength + 1}-cell queue." );
	}

	/// <summary>Places an object through the authoritative <see cref="Level.PlaceObject"/> — the commit the HUD click also runs.</summary>
	private OriginalObject? PlaceAuthoritatively( ObjectCatalogEntry entry, int rotation, (int X, int Y) near )
	{
		var site = FindSite( entry, rotation, near );
		return level.PlaceObject( entry, site.X, site.Y, rotation );
	}

	/// <summary>The nearest site to <paramref name="near"/> where the entry is placeable at this rotation, or throws.</summary>
	internal (int X, int Y) FindSite( ObjectCatalogEntry entry, int rotation, (int X, int Y) near )
	{
		var grid = level.Guests!.Grid;
		var cells = Enumerable.Range( 0, grid.CountX * grid.CountY ).Select( index => (X: index % grid.CountX, Y: index / grid.CountX) )
			.OrderBy( cell => Math.Abs( cell.X - near.X ) + Math.Abs( cell.Y - near.Y ) );
		foreach ( var cell in cells )
			if ( level.CheckFreeObject( entry, cell.X, cell.Y, rotation ) == OriginalPlacementResult.Allowed )
				return cell;
		throw new InvalidOperationException( $"Native park-resume smoke test failed: no buildable site for {entry.DisplayName} at rotation {rotation}." );
	}

	private static ObjectAccessPoint DoorAt( ObjectCatalogEntry entry, int x, int y, int rotation )
	{
		var anchor = Level.GetCentredAnchor( entry, x, y, rotation );
		var door = ObjectFootprint.GetAccessPoints( entry.Shape, anchor.X, anchor.Y, rotation ).FirstOrDefault( point => point.Kind == ObjectCellKind.Entrance );
		return door.Kind == ObjectCellKind.Entrance ? door : throw new InvalidOperationException(
			$"Native park-resume smoke test failed: {entry.DisplayName} has no entrance at rotation {rotation}." );
	}

	/// <summary>The queue cells a straight line out of the entrance would occupy, the door's outside cell first.</summary>
	private static (int X, int Y)[] LineOut( ObjectAccessPoint door, int length )
	{
		var (dx, dy) = (door.OutsideX - door.X, door.OutsideY - door.Y);
		if ( Math.Abs( dx ) + Math.Abs( dy ) != 1 )
			throw new InvalidOperationException( "Native park-resume smoke test failed: a ride entrance does not face a straight direction." );
		return Enumerable.Range( 0, length ).Select( step => (X: door.OutsideX + dx * step, Y: door.OutsideY + dy * step) ).ToArray();
	}

	// ---- Path helpers ----------------------------------------------------------------------------

	private (int X, int Y) FirstWalkable()
	{
		var grid = level.Guests!.Grid;
		for ( var index = 0; index < grid.CountX * grid.CountY; index++ )
			if ( grid.IsWalkable( index % grid.CountX, index / grid.CountX ) )
				return (index % grid.CountX, index / grid.CountX);
		throw new InvalidOperationException( "Native park-resume smoke test failed: the fresh park has no walkable cell." );
	}

	/// <summary>
	/// Walks a path spine out of the initial entrance strip: over already-built path cells, then laying fresh ones
	/// one commit at a time through <see cref="Level.BuildPath"/> (the HUD path tool's builder), preferring the
	/// current direction and otherwise steering toward the park's interior. Returns start, end, fresh-cell count and
	/// every cell actually walked (all walkable by construction).
	/// </summary>
	private ((int X, int Y) Start, (int X, int Y) End, int Laid, List<(int X, int Y)> Cells) LaySpine()
	{
		var grid = level.Guests!.Grid;
		var start = FirstWalkable();
		var current = start;
		var cells = new List<(int X, int Y)> { start };
		var (lastDx, lastDy) = (0, 0);
		var laid = 0;
		for ( var step = 0; step < SpineLength * 8; step++ )
		{
			var options = GuestPathGrid.Directions
				.OrderBy( direction => direction == (lastDx, lastDy) ? 0 : 1 )
				.ThenBy( direction => Math.Abs( current.X + direction.DX - grid.CountX / 2 ) + Math.Abs( current.Y + direction.DY - grid.CountY / 2 ) );
			var moved = false;
			foreach ( var (dx, dy) in options )
			{
				var next = (X: current.X + dx, Y: current.Y + dy);
				if ( !grid.InBounds( next.X, next.Y ) )
					continue;
				var check = level.Paths!.CheckCell( next.X, next.Y );
				if ( check == CellBuildResult.Existing )
				{
					// Travel along the initial strip toward buildable ground without laying anything.
					current = next;
					(lastDx, lastDy) = (dx, dy);
					cells.Add( next );
					moved = true;
					break;
				}
				if ( check != CellBuildResult.Ok || level.BuildPath( current, next ) is not { Completed: true } )
					continue;
				current = next;
				(lastDx, lastDy) = (dx, dy);
				laid++;
				cells.Add( next );
				moved = true;
				break;
			}
			if ( !moved || laid >= SpineLength + 6 )
				break;
		}
		return (start, current, laid, cells);
	}

	// ---- Guests ----------------------------------------------------------------------------------

	private void SeedGuests( OriginalObject rideA, OriginalObject rideB, OriginalObject shopA, OriginalObject shopB,
		IReadOnlyList<(int X, int Y)> spineCells, PrototypeRide? totem = null )
	{
		var guests = level.Guests!;
		void Seed( OriginalObject attraction, int count )
		{
			var fallback = attraction.Cells.First();
			var anchor = attraction.Visitors.HasCells ? attraction.Visitors.EntranceCell : (fallback.X, fallback.Y);
			foreach ( var cell in WalkableNear( anchor.X, anchor.Y ).Take( count ) )
			{
				var guest = guests.SpawnInPark( cell.X, cell.Y );
				guest.AttractionId = attraction.Visitors.AttractionId;
				guest.State = GuestState.GoingToRide;
			}
		}
		Seed( rideA, 4 );
		Seed( rideB, 4 );
		Seed( shopA, 1 );
		Seed( shopB, 1 );
		if ( totem != null )
			Seed( totem, 3 );
		// Wanderers spread along the actually-laid spine cells, so the park is not only commuters.
		// a ride placed over the spine takes its footprint cells (the placement clears a plain path under it), so only live path cells seed
		foreach ( var cell in spineCells.Where( cell => level.Guests!.Grid.IsWalkable( cell.X, cell.Y ) ).Where( ( _, index ) => index % Math.Max( 1, spineCells.Count / 4 ) == 0 ).Take( 4 ) )
			guests.SpawnInPark( cell.X, cell.Y );
		Require( guests.Guests.Count >= 14, $"seed {guests.Guests.Count} guests around the rides, shops and paths" );
	}

	private IEnumerable<(int X, int Y)> WalkableNear( int x, int y )
	{
		var grid = level.Guests!.Grid;
		return Enumerable.Range( 0, grid.CountX * grid.CountY )
			.Select( index => (X: index % grid.CountX, Y: index / grid.CountX) )
			.Where( cell => grid.IsWalkable( cell.X, cell.Y ) )
			.OrderBy( cell => Math.Abs( cell.X - x ) + Math.Abs( cell.Y - y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X );
	}

	// ---- Populate --------------------------------------------------------------------------------

	/// <summary>Controlled 1/60 advances until guests queue, board and ride through the original scripts.</summary>
	private void PopulateStep()
	{
		for ( var step = 0; step < PopulateAdvancesPerFrame; step++ )
		{
			if ( RichMoment() && WaterReady() && BumperReady() && TourReady() && PoolsReady() )
			{
				Log.Trace( $"Park-resume populate: rich state after {populateTicks} ticks ({DescribeGuests()})." );
				stage = Stage.Save;
				return;
			}
			level.AdvanceSimulation( FixedStepClock.TickDuration );
			populateTicks++;
		}
		if ( populateTicks >= MaximumPopulateTicks )
			Require( false, $"guests never reached the rich save state in {MaximumPopulateTicks} ticks: {DescribeGuests()}; queues "
				+ string.Join( ", ", level.Objects.Objects.Where( item => item.Visitors.HasQueue )
					.Select( item => $"{item.Entry.DisplayName}#{item.Visitors.AttractionId}: {item.Visitors.QueueLength} queued, open {item.Visitors.IsOpen}, join {item.Visitors.JoinCell}, cells {string.Join( ">", item.Visitors.QueueCells )}, entrance {item.Visitors.EntranceCell}" ) ) );
	}

	private bool RichMoment()
	{
		var guests = level.Guests!.Guests;
		return guests.Count >= 14
			&& guests.Count( guest => guest.State is GuestState.Queueing or GuestState.MovingUpQueue ) >= 2
			&& guests.Count( guest => guest.State is GuestState.Using ) >= 1
			&& guests.Count( guest => guest.State is GuestState.WalkingAround or GuestState.DoingNothing or GuestState.GoingToRide ) >= 2;
	}

	private string DescribeGuests()
	{
		var guests = level.Guests!.Guests;
		int Count( GuestState state ) => guests.Count( guest => guest.State == state );
		return $"{guests.Count} guests: {Count( GuestState.WalkingAround )} walking, {Count( GuestState.GoingToRide )} going, "
			+ $"{Count( GuestState.Queueing )}+{Count( GuestState.MovingUpQueue )} queued, {Count( GuestState.Boarding )}+{Count( GuestState.WaitingToBoard )} boarding, "
			+ $"{Count( GuestState.Using )} riding; boarded {string.Join( "/", level.Objects.Objects.Where( item => item.Visitors.HasQueue ).Select( item => item.Visitors.BoardedTotal ) )}";
	}
}
