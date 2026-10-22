namespace OpenTPW;

public partial class Level
{
	public PoolBridgeWorld? PoolBridges { get; private set; }
	public PoolBridgeRenderer? PoolBridgeRenderer { get; private set; }

	private void SetupPoolBridges()
	{
		if ( Guests == null ) return;
		PoolBridges = new PoolBridgeWorld( Guests.Grid.Cells, SlideGround,
			( x, y ) => IsPoolBridgeSiteReserved( Objects.Grid, Coasters, x, y ) || Objects.FindAt( x, y ) != null || IsReservedByPrototype( x, y ) || PoolSlides?.SlideAt( x, y ) != null,
			IsStoodOn, () => Park?.Economy, () => FeatureGates.Pools );
		if ( PoolTool != null ) PoolTool.Bridges = PoolBridges;
	}

	/// <summary>Bridge approaches obey the normal terrain rule and cannot overlap a coaster pylon, whose occupancy is separate from the cell map.</summary>
	internal static bool IsPoolBridgeSiteReserved( IParkGrid terrain, IEnumerable<CoasterRide> coasters, int x, int y ) =>
		x < 0 || y < 0 || x >= terrain.Width || y >= terrain.Height || terrain.CheckTerrain( x, y ) != OriginalPlacementResult.Allowed
		|| coasters.Any( ride => ride.Track.Nodes.Any( node => node.X == x && node.Y == y ) );

	/// <summary>Only land walkers are lifted. Swim-session guests keep their independent water/activity height.</summary>
	private System.Numerics.Vector3 BridgeGuestFeet( Guest guest )
	{
		var feet = GuestRenderer.CellToEngine( OriginalPark!.Heightfield, guest.DrawX, guest.DrawY );
		if ( !guest.InSwimSession && PoolBridges?.At( (int)MathF.Floor( guest.DrawX ), (int)MathF.Floor( guest.DrawY ) ) is { } bridge ) feet.Z = bridge.HeightAt( guest.DrawX, guest.DrawY );
		return feet;
	}

	/// <summary>
	/// Smoke-only: find a real seven-cell bridge footprint and build it through the ordinary paid builders. <paramref name="withPaths"/> false lays no path at the outside ends
	/// (a bridge needs none); <paramref name="onlyDirection"/> restricts the search to one direction.
	/// </summary>
	internal PoolBridge? BuildDebugPoolBridge( IReadOnlyList<(int X, int Y)> pool, bool withPaths = true, int? onlyDirection = null )
	{
		if ( PoolBridges == null || Paths == null || Guests == null || IsReadOnlyVisit || pool.Count == 0 )
		{
			Log.Warning( "--debug-bridge: bridge/path builders are unavailable or the park is read-only." );
			return null;
		}
		var centreX = pool.Average( c => c.X );
		var centreY = pool.Average( c => c.Y );
		var cells = Guests.Grid.Cells;
		var attempts = 0;
		string refusal = "no three-water-cell span with two land cells at either end";
		foreach ( var centre in pool.OrderBy( c => Math.Abs( c.X - centreX ) + Math.Abs( c.Y - centreY ) ).ThenBy( c => c.Y ).ThenBy( c => c.X ) )
			foreach ( var direction in new[] { 1, 2, 3, 0 }.Where( direction => onlyDirection == null || onlyDirection == direction ) )
			{
				var (dx, dy) = GuestPathGrid.Directions[direction];
				var candidate = new PoolBridge( centre.X - 3 * dx, centre.Y - 3 * dy, direction, 0 );
				if ( !Enumerable.Range( 0, PoolBridge.Length ).All( i =>
				{
					var c = candidate.Cell( i );
					return cells.InBounds( c.X, c.Y ) && (i is >= 2 and <= 4
						? cells.TypeAt( c.X, c.Y ) == ParkCellType.PoolWater
						: cells.TypeAt( c.X, c.Y ) is ParkCellType.Empty or ParkCellType.Path);
				} ) ) continue;
				var ends = new[] { candidate.Cell( -1 ), candidate.Cell( PoolBridge.Length ) };
				if ( !withPaths )
				{
					var bare = PoolBridges.TryAdd( candidate.X, candidate.Y, direction );
					if ( bare == PoolBridgeResult.Ok )
					{
						Log.Trace( $"--debug-bridge: built at {candidate.X},{candidate.Y}, direction {direction}, with no path at either end; price {PoolBridgeWorld.Price}." );
						return PoolBridges.At( candidate.X, candidate.Y );
					}
					refusal = bare.ToString();
					continue;
				}
				if ( !ends.All( c => cells.InBounds( c.X, c.Y ) && cells.TypeAt( c.X, c.Y ) is ParkCellType.Empty or ParkCellType.Path
					&& Paths.CheckCell( c.X, c.Y ) is CellBuildResult.Ok or CellBuildResult.Existing ) )
				{
					refusal = "an outside endpoint failed the normal path placement rules";
					continue;
				}
				attempts++;
				var added = new List<(int X, int Y)>();
				var endpointsReady = true;
				foreach ( var end in ends )
				{
					if ( cells.TypeAt( end.X, end.Y ) == ParkCellType.Path ) continue;
					var stroke = Paths.BuildSegment( end, end );
					added.AddRange( stroke.Built );
					if ( !stroke.Completed || cells.TypeAt( end.X, end.Y ) != ParkCellType.Path )
					{
						refusal = $"endpoint path: {stroke.StoppedBy}";
						endpointsReady = false;
						break;
					}
				}
				if ( endpointsReady )
				{
					var result = PoolBridges.TryAdd( candidate.X, candidate.Y, direction );
					if ( result == PoolBridgeResult.Ok )
					{
						var built = PoolBridges.At( candidate.X, candidate.Y )!;
						Log.Trace( $"--debug-bridge: built at {built.X},{built.Y}, direction {direction}; outer paths {ends[0]} and {ends[1]}; normal bridge price {PoolBridgeWorld.Price}." );
						return built;
					}
					refusal = result.ToString();
				}
				foreach ( var end in added )
					Log.Trace( $"--debug-bridge: refused ({refusal}); temporary endpoint {end} removal: {Paths.Remove( end.X, end.Y )}." );
			}
		Log.Warning( $"--debug-bridge: no bridge built after {attempts} candidate attempts; last refusal: {refusal}. Use a three-cell-wide basin, for example --debug-pool 0,0,3,8." );
		return null;
	}

	/// <summary>Smoke-only: two ordinary walkers request opposite end-to-end routes after pool objects and swimmers are set up.</summary>
	internal void StartDebugBridgeWalkers( PoolBridge bridge )
	{
		if ( Guests == null || PoolBridges?.At( bridge.X, bridge.Y ) != bridge ) return;
		if ( bridge.Cell( -1 ) is var first && bridge.Cell( PoolBridge.Length ) is var last
			&& (Guests.Grid.Cells.TypeAt( first.X, first.Y ) != ParkCellType.Path || Guests.Grid.Cells.TypeAt( last.X, last.Y ) != ParkCellType.Path) )
		{
			Log.Trace( "--debug-bridge: no walkers: the bridge has no path at an outside end." );
			return;
		}
		Park?.Economy.OpenPark();
		foreach ( var reverse in new[] { false, true } )
		{
			var start = bridge.Cell( reverse ? PoolBridge.Length : -1 );
			var end = bridge.Cell( reverse ? -1 : PoolBridge.Length );
			var guest = Guests.SpawnInPark( start.X, start.Y );
			var target = GuestNavigation.BytePoint( end, GuestWander.CentreByte, GuestWander.CentreByte );
			var found = guest.Navigation.Navigate( Guests.Grid.Cells, target.X, target.Y );
			guest.HasWaypoint = true;
			guest.State = GuestState.WalkingAround;
			Log.Trace( $"--debug-bridge: walker {guest.Id}, {start} -> {end}, navigation found: {found}." );
		}
	}
}
