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
		var uniformBuffer = new ObjectUniformBuffer
		{
			g_mModel = model,
			g_mView = Camera.ViewMatrix,
			g_mProj = Camera.ProjMatrix,
			g_vLightPos = IsWater ? Shallow : sun?.Position ?? System.Numerics.Vector3.Zero,
			g_vLightColor = sun?.Color ?? System.Numerics.Vector3.One,
			g_vCameraPos = Camera.Position,
			g_flTime = Time.Now,
			// the graphics settings' view distance, which the park's own shader (test.shader) applies to its fog
			_padding2 = CompatibilityRuntime.RenderQuality.ViewDistanceScale,
			_padding3 = Deep.X,
			_padding4 = Deep.Y,
			_padding5 = Deep.Z,
			_padding6 = Glow
		}.WithFog();
		Model.Material.Set( "ObjectUniformBuffer", uniformBuffer );
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
	private readonly List<Texture> textures = new();
	private readonly List<Material> materials = new();
	private int seenVersion = -1;
	private ulong seenSignature;
	private bool deleted;

	/// <summary>The style the pool is drawn in.</summary>
	public PoolStyle Style => style;
	/// <summary>The cells drawn in the last build.</summary>
	public int DrawnCells { get; private set; }
	/// <summary>Triangles of the last build (all parts).</summary>
	public int Triangles { get; private set; }
	/// <summary>How many times the meshes were rebuilt.</summary>
	public int Rebuilds { get; private set; }

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
	}

	private Texture Own( Texture texture )
	{
		textures.Add( texture );
		return texture;
	}

	private PoolPartEntity Part( string shader, Texture texture, bool isWater )
	{
		// DoubleSided: the pool is seen from inside and out, and the generated windings are consistent with their normals, not with the engine's culling
		var material = new Material<ObjectUniformBuffer>( shader, MaterialFlags.DoubleSided );
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
	public PoolMesh BuildMesh( IEnumerable<(int X, int Y)> poolCells ) =>
		PoolMeshBuilder.Build( poolCells, ( x, y ) => cells.RawTypeAt( x, y ) == ParkCellTypes.PoolWater, Ground, terrain.Origin, cellSize, options );

	/// <summary>
	/// The ground height at a point in cell units, from the terrain's sampler (<see cref="LandscapeSampler"/> through <see cref="IParkGrid.GetTerrainHeight"/>, which takes model units, 10 to
	/// a cell): the height the land is drawn at, so the deck follows it. The sampler answers 0 on the map's far edge, so the point is held just inside.
	/// </summary>
	private float Ground( float cellX, float cellY ) =>
		terrain.GetTerrainHeight( Math.Min( cellX * LandscapeSampler.CellUnits, terrain.Width * LandscapeSampler.CellUnits - 0.001f ),
			Math.Min( cellY * LandscapeSampler.CellUnits, terrain.Height * LandscapeSampler.CellUnits - 0.001f ) );

	protected override void OnUpdate()
	{
		// the cell map's version moves with every write, a stroke writes many cells between two frames, and this runs once a frame: one rebuild per frame at most
		if ( deleted || cells.Version == seenVersion )
			return;
		seenVersion = cells.Version;
		var poolCells = PoolCells( cells );
		var signature = Signature( poolCells );
		if ( signature == seenSignature && poolCells.Count == DrawnCells )
			return;
		seenSignature = signature;
		DrawnCells = poolCells.Count;
		var mesh = BuildMesh( poolCells );
		Triangles = mesh.TriangleCount;
		Rebuilds++;
		Replace( lowerWalls, mesh.LowerWalls );
		Replace( floor, mesh.Floor );
		Replace( upperWalls, mesh.UpperWalls );
		Replace( coping, mesh.Coping );
		Replace( water, mesh.Water );
	}

	private static ulong Signature( IReadOnlyList<(int X, int Y)> poolCells )
	{
		var hash = 14695981039346656037UL;
		foreach ( var (x, y) in poolCells )
			hash = (hash ^ (uint)(y * 4096 + x)) * 1099511628211UL;
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
		foreach ( var part in new[] { lowerWalls, floor, upperWalls, coping, water } )
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
