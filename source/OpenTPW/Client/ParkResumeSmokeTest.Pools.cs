namespace OpenTPW;

/// <summary>
/// The opt-in pools variant of the park-resume fixture (<c>OPENTPW_SMOKE_POOLS=1</c> in both phases, with <c>--enable-mod opentpw.pools</c>, docs/POOLS.md): a pool of 6 x 4 cells is painted near the path spine, a
/// changing room is built beside it through the level's own placement, and guests swim in it at the save. The save records the extension and the room's asset keys and holds the swimmers; the restore phase checks
/// the room and the swimmers came back in the water and continues the same ticks, whose canonical hashes must equal the capture phase's.
/// </summary>
internal sealed partial class ParkResumeSmokeTest
{
	/// <summary>Both phases must agree: <c>OPENTPW_SMOKE_POOLS=1</c> adds a pool, a changing room and swimmers.</summary>
	internal static bool PoolsVariant => Environment.GetEnvironmentVariable( "OPENTPW_SMOKE_POOLS" ) is "1" or "true";

	private OriginalObject? PoolRoom() => level.Objects.Objects.FirstOrDefault( item => !item.IsDeleted && item.Visitors.IsChangingRoom );

	private bool poolsSwimmersStarted;

	/// <summary>
	/// The variant's populate condition: swimmers are in the water (always true without the variant). The first time it is asked, at a moment the other conditions of the save state hold, three guests are put into the
	/// water, so they are mid-swim (a swim lasts 40 to 70 seconds) when the save is made on the following ticks.
	/// </summary>
	private bool PoolsReady()
	{
		if ( !PoolsVariant )
			return true;
		if ( !poolsSwimmersStarted )
		{
			poolsSwimmersStarted = true;
			var started = level.Guests!.DebugStartSwimmers( PoolRoom()!.Visitors, 3 );
			Require( started == 3, "three guests swim in the room's pool" );
			return false;
		}
		return level.Guests!.Guests.Count( guest => guest.IsSwimming ) >= 2;
	}

	private void PlacePoolsFixture( (int X, int Y) near )
	{
		Require( FeatureGates.Pools && level.PoolBuilder != null, "the opentpw.pools extension is on (--enable-mod opentpw.pools) for the pools variant" );
		var builder = level.PoolBuilder!;
		var shape = PoolBuilder.Rectangle( (0, 0), (5, 3) ).ToArray();
		var grid = level.Objects.Grid;
		(int X, int Y)? origin = null;
		foreach ( var cell in Enumerable.Range( 0, grid.Width * grid.Height ).Select( index => (X: index % grid.Width, Y: index / grid.Width) )
			.OrderBy( cell => Math.Abs( cell.X - near.X ) + Math.Abs( cell.Y - near.Y ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			// room for the cabin on every side of the basin as well: a margin of four cells that holds no object and no path
			if ( shape.All( offset => builder.Check( cell.X + offset.X, cell.Y + offset.Y ) == PoolBuildResult.Ok ) )
			{
				origin = cell;
				break;
			}
		}
		Require( origin != null, "find a free site for a pool near the spine" );
		var cells = shape.Select( offset => (X: origin!.Value.X + offset.X, Y: origin.Value.Y + offset.Y) ).ToArray();
		Require( builder.PaintCells( cells ).Completed, "paint the pool through the pool builder" );
		// [EXT:pools-river] the basin's perimeter is a lazy river (a ring of 16 cells round a middle of 8), [EXT:pools-attractions] and the four water attractions stand beside it
		foreach ( var cell in cells.Where( cell => cell.X == origin!.Value.X || cell.X == origin.Value.X + 5 || cell.Y == origin.Value.Y || cell.Y == origin.Value.Y + 3 ) )
			level.Guests!.Grid.Cells.SetFlags( cell.X, cell.Y, ParkCellFlags.PoolRiver );
		var room = level.BuildChangingRoomBeside( cells );
		Require( room != null, "build a changing room beside the pool through the level's placement" );
		var attractions = level.BuildPoolAttractionsBeside( cells, "all" );
		Log.Trace( $"Park-resume fixture: {attractions.Count} water attraction(s): {string.Join( ", ", attractions.Select( item => item.Entry.DisplayName ) )}." );
		Log.Trace( $"Park-resume fixture: pool of {cells.Length} cells at {origin}, {room!.Entry.DisplayName} #{room.Visitors.AttractionId} at {room.Placement.X},{room.Placement.Y}." );
	}

	/// <summary>The restore phase's proof that the pool, the room and the swimmers came back: the cells, the room's flags and the guests in the water.</summary>
	private void VerifyPoolsRestored()
	{
		if ( !PoolsVariant )
			return;
		var room = PoolRoom();
		Require( room != null && room.Visitors.PoolAccessCandidates.Count > 0, "the resumed park holds its changing room with its pool-side cells" );
		var cells = level.Guests!.Grid.Cells;
		Require( cells.CountOf( ParkCellType.PoolWater ) == 24, $"the resumed park holds its 24 pool cells ({cells.CountOf( ParkCellType.PoolWater )})" );
		var region = level.Guests.SwimRegionOf( room!.Visitors );
		Require( region != null && region.Cells.Count == 24, "the room's swim region is the whole pool" );
		var swimmers = level.Guests.Guests.Where( guest => guest.InSwimSession ).ToList();
		Require( swimmers.Count >= 1, "the resumed park holds guests in a swim session" );
		Require( swimmers.Where( guest => guest.IsSwimming ).All( guest => region!.IsWater( guest.Navigation.X, guest.Navigation.Y ) ), "every resumed swimmer is in the water" );
		Require( swimmers.All( guest => guest.AttractionId == room.Visitors.AttractionId ), "and names its changing room" );
		// [EXT:pools-river] [EXT:pools-waves] [EXT:pools-attractions] the ring is derived again from the saved flags, the pool kept its wave settings, and the water attractions are placed objects again
		Require( PoolRiverMap.Of( cells ).Rings.Count == 1 && PoolRiverMap.Of( cells ).Rings[0].Cells.Count == 16, "the resumed park holds its lazy river ring of 16 cells" );
		var waves = level.PoolSites.All.Where( site => site.Kind == PoolAttractionKind.WaveMachine ).Select( site => (PoolWaveSettings?)PoolWaveSettings.At( cells, site.Access ) ).FirstOrDefault();
		Require( waves != null || !level.Objects.Objects.Any( item => !item.IsDeleted && PoolAttractions.KindOf( item.Entry ) == PoolAttractionKind.WaveMachine ), "the resumed pool holds its wave settings (a wave machine stands beside it)" );
		var kinds = level.Objects.Objects.Where( item => !item.IsDeleted ).Select( item => PoolAttractions.KindOf( item.Entry ) ).Where( kind => kind != PoolAttractionKind.None ).Distinct().Count();
		Require( kinds >= 1 && level.PoolSites.All.Count == kinds, $"the resumed park holds its water attractions beside the water ({kinds} kinds, {level.PoolSites.All.Count} sites)" );
		Log.Trace( $"Park-resume restore: the river ring, the waves ({waves}) and {kinds} kinds of water attraction came back." );
		Log.Trace( $"Park-resume restore: the changing room came back with {swimmers.Count} swimmer(s) in its {region!.Cells.Count}-cell pool." );
	}
}
