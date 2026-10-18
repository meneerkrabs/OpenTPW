namespace OpenTPW;

/// <summary>One drawn surface of a pool: a model entity that fills the engine's uniform buffer with the pool style's values (the deep water colour and the glow ride in its free padding).</summary>
// [EXT:pools-render] OpenTPW's own pool rendering; the original game has no pools
internal sealed class PoolPartEntity : ModelEntity
{
	public System.Numerics.Vector3 Deep { get; set; }
	public System.Numerics.Vector3 Shallow { get; set; }
	public float Glow { get; set; }
	/// <summary>True for the water: its shader reads the shallow colour in place of the sun's position.</summary>
	public bool IsWater { get; set; }
	/// <summary>[EXT:pools-waves] The wave machine's strength for the water (0 flat to 1), in the free padding after the light position.</summary>
	public float WaveAmplitude { get; set; }
	/// <summary>[EXT:pools-waves] The wave's phase in cycles at the grid origin, in the free padding after the light colour.</summary>
	public float WavePhase { get; set; }
	/// <summary>The material every model of this part is drawn with.</summary>
	public Material? PartMaterial { get; set; }

	protected override void OnRender()
	{
		if ( Model == null || Hidden )
			return;
		var model = ModelMatrix;
		if ( Cullable && Model.Bounds is { } bounds && Frustum.IsOutside( bounds.Min, bounds.Max, model * Camera.ViewMatrix * Camera.ProjMatrix ) )
			return;
		var sun = Level?.SunLight;
		// [EXT:evening] at dusk and night the lit parts keep a share of the ambient and the water's colours darken; at day (or with the extension off) all of this is as it was
		var look = Level?.Look ?? EveningLook.Day;
		var shallow = look.IsDay ? Shallow : Shallow * look.WaterTint;
		var deep = look.IsDay ? Deep : Deep * look.WaterTint;
		var uniformBuffer = new ObjectUniformBuffer
		{
			g_mModel = model,
			g_mView = Camera.ViewMatrix,
			g_mProj = Camera.ProjMatrix,
			g_vLightPos = IsWater ? shallow : sun?.Position ?? System.Numerics.Vector3.Zero,
			g_vLightColor = sun?.Color ?? System.Numerics.Vector3.One,
			g_vCameraPos = Camera.Position,
			_padding0 = IsWater ? WaveAmplitude : look.AmbientUniform,
			// [EXT:point-lights] the deck and basin shaders read this slot as the light count, so only the water (whose shader never lights) carries a phase
			_padding1 = IsWater ? WavePhase : 0f,
			g_flTime = Time.Now,
			// the graphics settings' view distance, which the park's own shader (test.shader) applies to its fog
			_padding2 = CompatibilityRuntime.RenderQuality.ViewDistanceScale,
			_padding3 = deep.X,
			_padding4 = deep.Y,
			_padding5 = deep.Z,
			_padding6 = Glow
		}.WithFog();
		// [EXT:point-lights] the deck and the basin take the park's point lights; the water's shader keeps its padding for the wave and the colours
		SetObjectUniforms( uniformBuffer, model );
		Model.Draw();
	}
}

/// <summary>
/// Draws the park's pool cells (docs/POOLS.md): a basin dug into the ground, with the deck, the walls and the floor opaque and lit and fogged as the park is, and the translucent water
/// (<c>pool-water.shader</c>, adapted from the unused <c>water.shader</c>) over them. The meshes come from <see cref="PoolMeshBuilder"/> and are rebuilt when the set of pool cells changes
/// (at most once per frame, whatever number of cells a stroke wrote); the textures and the water colours come from the level's <see cref="PoolStyle"/> and are generated in code
/// (<see cref="PoolTextures"/>). Presentation only: nothing here touches the simulation. GPU code: the mesh itself is tested on the CPU.
/// </summary>
// [EXT:pools-render] OpenTPW's own pool rendering; the original game has no pools
public sealed class PoolRenderer : Entity
{
	private readonly ParkCellMap cells;
	private readonly IParkGrid terrain;
	private readonly float cellSize;
	private readonly PoolMeshOptions options;
	private readonly PoolStyle style;
	// the opaque parts are created first, so they draw before the translucent water
	private readonly PoolPartEntity lowerWalls, floor, upperWalls, coping, water;
	// [EXT:pools-waves] the water of up to eight pools with a wave machine is drawn by entities of their own (created before the swimmers' renderer, so the order of drawing stays), each with its own wave
	private readonly PoolPartEntity[] waveWater = new PoolPartEntity[WaveSlots];
	private readonly PoolWaveSettings?[] waveSettings = new PoolWaveSettings?[WaveSlots];
	private readonly List<Texture> textures = new();
	private readonly List<Material> materials = new();
	private int seenVersion = -1, seenSitesVersion = -1;
	private ulong seenSignature;
	private bool deleted;

	/// <summary>[EXT:pools-waves] How many pools with waves are drawn with their own wave at once; a ninth draws flat.</summary>
	public const int WaveSlots = 8;
	/// <summary>[EXT:pools-waves] The park turn with a fraction, for the frame (set by the level; null draws flat water). Presentation only.</summary>
	public Func<double>? Turn { get; set; }
	/// <summary>[EXT:pools-waves] The park's water attractions: a pool has waves only while a wave machine of it is on.</summary>
	public PoolAttractionSites? Sites { get; set; }
	/// <summary>[EXT:pools-waves] The amplitude (0 to 1) of each wave slot in the last frame, for tests and smokes.</summary>
	public float[] WaveAmplitudes { get; } = new float[WaveSlots];

	/// <summary>The style the pool is drawn in.</summary>
	public PoolStyle Style => style;
	/// <summary>The cells drawn in the last build.</summary>
	public int DrawnCells { get; private set; }
	/// <summary>Triangles of the last build (all parts).</summary>
	public int Triangles { get; private set; }
	/// <summary>How many times the meshes were rebuilt.</summary>
	public int Rebuilds { get; private set; }
	private Dictionary<(int X, int Y), float> waterLevels = new();

	/// <summary>[EXT:pools-swim] The engine height of the water surface over a pool cell (the level surface of its group of connected cells), or null when the cell holds no water in the last build.</summary>
	public float? WaterLevelAt( int x, int y ) => waterLevels.TryGetValue( (x, y), out var level ) ? level : null;

	public PoolRenderer( ParkCellMap cells, IParkGrid terrain, PoolStyle? style = null, float cellSize = OriginalParkPlacement.CellSize, PoolMeshOptions? options = null )
	{
		this.cells = cells ?? throw new ArgumentNullException( nameof( cells ) );
		this.terrain = terrain ?? throw new ArgumentNullException( nameof( terrain ) );
		this.style = style ?? PoolStyles.Generic;
		this.cellSize = cellSize;
		this.options = options ?? PoolMeshOptions.Default;
		Name = "Pools";
		var copingTexture = Own( new Texture( PoolTextures.Coping( this.style ), PoolTextures.Size, PoolTextures.Size, TextureFlags.Wrap ) );
		var wallTexture = Own( new Texture( PoolTextures.Wall( this.style ), PoolTextures.Size, PoolTextures.Size, TextureFlags.Wrap ) );
		var waterTexture = Own( new Texture( PoolTextures.Water(), PoolTextures.Size, PoolTextures.Size, TextureFlags.Wrap ) );
		lowerWalls = Part( "content/shaders/pool-basin.shader", wallTexture, isWater: false );
		floor = Part( "content/shaders/pool-basin.shader", wallTexture, isWater: false );
		upperWalls = Part( "content/shaders/pool-deck.shader", copingTexture, isWater: false );
		coping = Part( "content/shaders/pool-deck.shader", copingTexture, isWater: false );
		water = Part( "content/shaders/pool-water.shader", waterTexture, isWater: true );
		for ( var slot = 0; slot < WaveSlots; slot++ )
			waveWater[slot] = Part( "content/shaders/pool-water.shader", waterTexture, isWater: true );
	}

	private Texture Own( Texture texture )
	{
		textures.Add( texture );
		return texture;
	}

	private PoolPartEntity Part( string shader, Texture texture, bool isWater )
	{
		// DoubleSided: the pool is seen from inside and out, and the generated windings are consistent with their normals, not with the engine's culling
		// [EXT:pools-swim] the water writes no depth, so the swimmers drawn after it (the lower half of their sprite lies under the surface) are not cut away by it; the floor and the walls, drawn before, still hide them
		var material = new Material<ObjectUniformBuffer>( shader, isWater ? MaterialFlags.DoubleSided | MaterialFlags.DisableDepthWrite : MaterialFlags.DoubleSided );
		material.Set( "Color", texture );
		materials.Add( material );
		return new PoolPartEntity { Deep = style.WaterDeep.ToVector(), Shallow = style.WaterShallow.ToVector(), Glow = style.Glow, IsWater = isWater, PartMaterial = material };
	}

	/// <summary>The pool cells of the map in row order.</summary>
	public static List<(int X, int Y)> PoolCells( ParkCellMap cells )
	{
		var list = new List<(int X, int Y)>();
		if ( cells.CountOf( ParkCellType.PoolWater ) == 0 )
			return list;
		for ( var y = 0; y < cells.Height; y++ )
			for ( var x = 0; x < cells.Width; x++ )
				if ( cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater )
					list.Add( (x, y) );
		return list;
	}

	/// <summary>Builds the mesh of <paramref name="poolCells"/> now.</summary>
	public PoolMesh BuildMesh( IEnumerable<(int X, int Y)> poolCells )
	{
		// [EXT:pools-river] the water of a river cell carries its current
		var rivers = PoolRiverMap.Of( cells );
		return PoolMeshBuilder.Build( poolCells, ( x, y ) => cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater, Ground, terrain.Origin, cellSize, options,
			rivers.Any ? ( x, y ) => rivers.FlowAt( x, y ) : null );
	}

	/// <summary>
	/// The ground height at a point in cell units, from the terrain's sampler (<see cref="LandscapeSampler"/> through <see cref="IParkGrid.GetTerrainHeight"/>, which takes model units, 10 to
	/// a cell): the height the land is drawn at, so the deck follows it. The sampler answers 0 on the map's far edge, so the point is held just inside.
	/// </summary>
	private float Ground( float cellX, float cellY ) =>
		terrain.GetTerrainHeight( Math.Min( cellX * LandscapeSampler.CellUnits, terrain.Width * LandscapeSampler.CellUnits - 0.001f ),
			Math.Min( cellY * LandscapeSampler.CellUnits, terrain.Height * LandscapeSampler.CellUnits - 0.001f ) );

	protected override void OnUpdate()
	{
		if ( deleted )
			return;
		// the cell map's version moves with every write, a stroke writes many cells between two frames, and this runs once a frame: one rebuild per frame at most
		var sitesVersion = Sites != null && Sites.All.Count >= 0 ? Sites.Version : 0;
		if ( cells.Version != seenVersion || sitesVersion != seenSitesVersion )
		{
			seenVersion = cells.Version;
			seenSitesVersion = sitesVersion;
			var poolCells = PoolCells( cells );
			var signature = Signature( poolCells );
			signature = (signature ^ (uint)sitesVersion) * 1099511628211UL;
			if ( signature != seenSignature || poolCells.Count != DrawnCells )
			{
				seenSignature = signature;
				DrawnCells = poolCells.Count;
				var mesh = BuildMesh( poolCells );
				waterLevels = PoolMeshBuilder.WaterLevels( poolCells, Ground, options );
				Triangles = mesh.TriangleCount;
				Rebuilds++;
				Replace( lowerWalls, mesh.LowerWalls );
				Replace( floor, mesh.Floor );
				Replace( upperWalls, mesh.UpperWalls );
				Replace( coping, mesh.Coping );
				var split = SplitWater( mesh.Water, poolCells );
				Replace( water, split.Flat );
				for ( var slot = 0; slot < WaveSlots; slot++ )
					Replace( waveWater[slot], split.Waves[slot] );
				Array.Copy( split.Settings, waveSettings, WaveSlots );
			}
		}
		UpdateWaves();
	}

	/// <summary>
	/// [EXT:pools-waves] Every frame: the wave slots' strength and phase from the park turn. The strength is the run's swell (<see cref="PoolWaves.Envelope"/>) times the height setting, 0 to 1; the phase is
	/// the wave's phase in cycles at the grid origin, so the surface agrees with <see cref="PoolWaves.SmoothWave"/>, the function the swimmers bob on.
	/// </summary>
	private void UpdateWaves()
	{
		var turn = Turn?.Invoke() ?? 0.0;
		for ( var slot = 0; slot < WaveSlots; slot++ )
		{
			var amplitude = 0f;
			if ( waveSettings[slot] is { } settings && Turn != null )
				amplitude = PoolWaves.Envelope( settings, (long)Math.Floor( turn ) ) / 64f * Math.Clamp( settings.Height, 1, 3 ) / 3f;
			WaveAmplitudes[slot] = amplitude;
			var cycles = turn / PoolWaves.PeriodTurns + (terrain.Origin.X + terrain.Origin.Y) / cellSize * PoolWaves.PhasePerCell / 64.0;
			waveWater[slot].WaveAmplitude = amplitude;
			waveWater[slot].WavePhase = (float)(cycles - Math.Floor( cycles ));
		}
	}

	/// <summary>
	/// [EXT:pools-waves] Takes the water quads of the pools that have wave settings out of the flat water and into one part per pool (at most <see cref="WaveSlots"/>, in row order of their first cell);
	/// the quads of every other pool stay in the flat part. A quad belongs to the pool of the cell its middle lies in.
	/// </summary>
	private (PoolMeshPart Flat, PoolMeshPart[] Waves, PoolWaveSettings?[] Settings) SplitWater( PoolMeshPart waterPart, IReadOnlyList<(int X, int Y)> poolCells )
	{
		var waves = Enumerable.Range( 0, WaveSlots ).Select( _ => new PoolMeshPart() ).ToArray();
		var settings = new PoolWaveSettings?[WaveSlots];
		var slotOf = new Dictionary<(int X, int Y), int>();
		var seen = new HashSet<(int X, int Y)>();
		var next = 0;
		foreach ( var start in poolCells )
		{
			if ( !seen.Add( start ) )
				continue;
			var region = PoolSwimRegion.Build( cells, start );
			if ( region == null )
				continue;
			foreach ( var cell in region.Cells )
				seen.Add( cell );
			if ( next < WaveSlots && Sites != null && PoolWaveSettings.ActiveIn( Sites, cells, region ) is { } found )
			{
				settings[next] = found;
				foreach ( var cell in region.Cells )
					slotOf[cell] = next;
				next++;
			}
		}
		if ( next == 0 )
			return (waterPart, waves, settings);
		var flat = new PoolMeshPart();
		for ( var quad = 0; quad + 3 < waterPart.Vertices.Count; quad += 4 )
		{
			var centre = (waterPart.Vertices[quad].Position + waterPart.Vertices[quad + 2].Position) / 2f;
			var cell = ((int)MathF.Floor( (centre.X - terrain.Origin.X) / cellSize ), (int)MathF.Floor( (centre.Y - terrain.Origin.Y) / cellSize ));
			var target = slotOf.TryGetValue( cell, out var slot ) ? waves[slot] : flat;
			var @base = target.Vertices.Count;
			for ( var corner = 0; corner < 4; corner++ )
				target.Vertices.Add( waterPart.Vertices[quad + corner] );
			// the quads were built as four vertices and the indices 0 1 2, 0 2 3 (PoolMeshPart.AddQuad)
			var source = quad / 4 * 6;
			for ( var index = 0; index < 6; index++ )
				target.Indices.Add( @base + waterPart.Indices[source + index] - quad );
		}
		return (flat, waves, settings);
	}

	private ulong Signature( IReadOnlyList<(int X, int Y)> poolCells )
	{
		var hash = 14695981039346656037UL;
		foreach ( var (x, y) in poolCells )
			// [EXT:pools-river] [EXT:pools-waves] the river and wave flags of a cell are part of what is drawn (the current, the pool that has waves)
			hash = (hash ^ (uint)(y * 4096 + x) ^ ((ulong)((ushort)cells.FlagsAt( x, y ) & (ushort)(ParkCellFlags.PoolRiver | ParkCellFlags.PoolWaveBits)) << 32)) * 1099511628211UL;
		return hash;
	}

	private static void Replace( PoolPartEntity entity, PoolMeshPart part )
	{
		entity.Model?.ReleaseBuffers();
		entity.Model = null;
		if ( part.Vertices.Count == 0 )
			return;
		var material = entity.PartMaterial!;
		var vertices = part.Vertices.Select( vertex => new Vertex( vertex.Position, vertex.Normal, vertex.Uv ) ).ToArray();
		entity.Model = new Model( vertices, part.Indices.Select( index => (uint)index ).ToArray(), material );
	}

	protected override void OnDelete()
	{
		deleted = true;
		foreach ( var part in new[] { lowerWalls, floor, upperWalls, coping, water }.Concat( waveWater ) )
		{
			part.Model?.ReleaseBuffers();
			part.Model = null;
			part.Delete();
		}
		foreach ( var texture in textures )
			texture.ReleaseOwned();
		foreach ( var material in materials )
			material.ReleaseOwnedResources();
	}
}
