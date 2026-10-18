namespace OpenTPW;

public partial class Level
{
	private PoolAttractionSites? poolSites;

	/// <summary>[EXT:pools-attractions] The placed water attractions (rope swing, jets, wave machine, spa), derived from the placed objects and the pool cells (docs/POOLS.md).</summary>
	public PoolAttractionSites PoolSites => poolSites ??= new PoolAttractionSites( () => Objects.Objects, () => Guests?.Grid.Cells );

	/// <summary>[EXT:pools-attractions] Draws the spray of the park's water jets; created with the first jet.</summary>
	public PoolSprayRenderer? PoolSprayRenderer { get; private set; }

	/// <summary>
	/// [EXT:pools-waves] [EXT:pools-attractions] Every frame while a pool is drawn: gives the water the park turn its waves run on, and creates the spray renderer once a water jet stands beside the water.
	/// Presentation only.
	/// </summary>
	private void RefreshPoolEffects()
	{
		if ( PoolRenderer == null || Guests == null || OriginalPark == null )
			return;
		var guests = Guests;
		PoolRenderer.Turn ??= () => PoolWaterEffects.SmoothTurn( guests );
		PoolRenderer.Sites ??= PoolSites;
		if ( PoolSprayRenderer == null && PoolSites.All.Any( site => site.Kind == PoolAttractionKind.Jets ) )
			PoolSprayRenderer = new PoolSprayRenderer( PoolSpraySources, () => guests.TimeSeconds );
	}

	private IEnumerable<PoolSpraySource> PoolSpraySources()
	{
		if ( OriginalPark == null )
			yield break;
		foreach ( var site in PoolSites.All )
		{
			if ( site.Kind != PoolAttractionKind.Jets )
				continue;
			var (cx, cy) = (site.Cells.Average( cell => cell.X ) + 0.5, site.Cells.Average( cell => cell.Y ) + 0.5);
			var nozzle = GuestRenderer.CellToEngine( OriginalPark.Heightfield, (float)cx, (float)cy );
			var (dx, dy) = (site.Access.X + 0.5 - cx, site.Access.Y + 0.5 - cy);
			var length = Math.Sqrt( dx * dx + dy * dy );
			var direction = length > 0 ? new System.Numerics.Vector2( (float)(dx / length), (float)(dy / length) ) : new System.Numerics.Vector2( 0, 1 );
			var level = PoolRenderer?.WaterLevelAt( site.Access.X, site.Access.Y ) ?? nozzle.Z;
			yield return new PoolSpraySource( new System.Numerics.Vector3( nozzle.X, nozzle.Y, nozzle.Z + 1.0f ), direction, level );
		}
	}

	/// <summary>
	/// [EXT:pools-waves] The wave settings of a placed wave machine (the machine's own, in the flags of its access cell; a machine whose cell holds none runs <see cref="PoolWaveSettings.Default"/>), or null
	/// when the object is no wave machine with water beside it or the pools extension is off.
	/// </summary>
	public PoolWaveSettings? PoolWavesOf( OriginalObject item ) =>
		FeatureGates.Pools && Guests?.Grid.Cells is { } cells && PoolAttractions.KindOf( item.Entry ) == PoolAttractionKind.WaveMachine && PoolSites.Find( item.ThingId ) is { } site
			? PoolWaveSettings.At( cells, site.Access )
			: null;

	/// <summary>
	/// [EXT:pools-waves] Writes the wave settings of a placed wave machine (height 1 to 3 and interval 0 to 2 are clamped as the flags hold them). False, and nothing written, when the object is no wave machine
	/// with water beside it.
	/// </summary>
	public bool SetPoolWaves( OriginalObject item, PoolWaveSettings settings )
	{
		if ( PoolWavesOf( item ) is null || Guests?.Grid.Cells is not { } cells || PoolSites.Find( item.ThingId ) is not { } site )
			return false;
		PoolWaveSettings.SetAt( cells, site.Access, new PoolWaveSettings( settings.On, Math.Clamp( settings.Height, 1, 3 ), Math.Clamp( settings.Interval, 0, 2 ) ) );
		return true;
	}

	/// <summary>
	/// [EXT:pools-waves] Steps a wave machine's settings to the next preset (<see cref="PoolWaveSettings.NextPreset"/>; a machine running the default moves on from it). Returns them, or null when the object is no
	/// wave machine with water beside it. The HUD sets them in the machine's settings window (<see cref="SetPoolWaves"/>); this stays for scripted use.
	/// </summary>
	public PoolWaveSettings? CyclePoolWaves( OriginalObject item )
	{
		if ( PoolWavesOf( item ) is not { } current )
			return null;
		var next = PoolWaveSettings.NextPreset( current );
		return SetPoolWaves( item, next ) ? next : null;
	}

	/// <summary>
	/// [EXT:debug-attractions] Smoke-only: places the water attractions of the listed kinds (<c>ropeswing,jets,wavemachine,spa</c> or <c>all</c>) beside the pool cells <paramref name="pool"/>
	/// through the level's own placement (charged; the rule needs the pool on their water side) at the first anchor and rotation within three cells of the pool that the rules allow, one per kind.
	/// </summary>
	internal List<OriginalObject> BuildPoolAttractionsBeside( IReadOnlyList<(int X, int Y)> pool, string kinds )
	{
		var placedObjects = new List<OriginalObject>();
		if ( pool.Count == 0 || !FeatureGates.Pools )
			return placedObjects;
		var wanted = kinds.Split( ',' ).Select( kind => kind.Trim().ToLowerInvariant() ).ToHashSet();
		var (minX, maxX, minY, maxY) = (pool.Min( cell => cell.X ) - 3, pool.Max( cell => cell.X ) + 3, pool.Min( cell => cell.Y ) - 3, pool.Max( cell => cell.Y ) + 3);
		foreach ( var entry in Objects.Catalog.Entries.Where( entry => PoolAttractions.KindOf( entry ) != PoolAttractionKind.None ) )
		{
			var kind = PoolAttractions.KindOf( entry );
			if ( !(wanted.Contains( "all" ) || wanted.Contains( kind.ToString().ToLowerInvariant() )) || placedObjects.Any( item => PoolAttractions.KindOf( item.Entry ) == kind ) )
				continue;
			var done = false;
			foreach ( var rotation in new[] { 0, 90, 180, 270 } )
				for ( var y = minY; y <= maxY && !done; y++ )
					for ( var x = minX; x <= maxX && !done; x++ )
						if ( CheckObject( entry, x, y, rotation ) == OriginalPlacementResult.Allowed && PlaceObject( entry, x, y, rotation ) is { } placed )
						{
							placedObjects.Add( placed );
							done = true;
						}
		}
		return placedObjects;
	}
}
