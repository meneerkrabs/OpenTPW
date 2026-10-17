namespace OpenTPW;

/// <summary>
/// Draws the park's water slides (docs/POOLS.md, "Water slides"): the track's shell in the flume and tube textures of the park's pool style and the pylons under it, opaque and lit and fogged like the pool's
/// deck (<c>pool-deck.shader</c>, so the browser build needs no shader of its own). The meshes come from <see cref="PoolSlideMeshBuilder"/> and are rebuilt, at most once a frame, when the slides or the pool
/// cells change. Presentation only: nothing here touches the simulation. GPU code: the mesh itself is tested on the CPU.
/// </summary>
// [EXT:pools-render] OpenTPW's own slide rendering; the original game has no pools
public sealed class PoolSlideRenderer : Entity
{
	private readonly PoolSlideWorld world;
	private readonly Func<PoolSlideSite?> site;
	private readonly ParkCellMap cells;
	private readonly System.Numerics.Vector2 origin;
	private readonly float cellSize;
	private readonly PoolPartEntity flume, tube, pylons;
	private readonly List<Texture> textures = new();
	private readonly List<Material> materials = new();
	private int seenVersion = -1, seenPoolWrites = -1;
	private bool deleted;

	/// <summary>Triangles of the last build.</summary>
	public int Triangles { get; private set; }
	/// <summary>How many times the meshes were rebuilt.</summary>
	public int Rebuilds { get; private set; }

	public PoolSlideRenderer( PoolSlideWorld world, Func<PoolSlideSite?> site, ParkCellMap cells, System.Numerics.Vector2 origin, PoolStyle style, float cellSize = OriginalParkPlacement.CellSize )
	{
		this.world = world ?? throw new ArgumentNullException( nameof( world ) );
		this.site = site ?? throw new ArgumentNullException( nameof( site ) );
		this.cells = cells ?? throw new ArgumentNullException( nameof( cells ) );
		this.origin = origin;
		this.cellSize = cellSize;
		Name = "PoolSlides";
		flume = Part( PoolSlideTextures.Surface( style, PoolSlideProfile.Flume ), style );
		tube = Part( PoolSlideTextures.Surface( style, PoolSlideProfile.Tube ), style );
		pylons = Part( PoolTextures.Coping( style ), style );
	}

	private PoolPartEntity Part( byte[] pixels, PoolStyle style )
	{
		var texture = new Texture( pixels, PoolSlideTextures.Size, PoolSlideTextures.Size, TextureFlags.Wrap );
		textures.Add( texture );
		// DoubleSided: the shell is seen from inside (the flume's bed) and out
		var material = new Material<ObjectUniformBuffer>( "content/shaders/pool-deck.shader", MaterialFlags.DoubleSided );
		material.Set( "Color", texture );
		materials.Add( material );
		return new PoolPartEntity { Deep = style.WaterDeep.ToVector(), Shallow = style.WaterShallow.ToVector(), Glow = 0f, IsWater = false, PartMaterial = material };
	}

	/// <summary>The mesh of every slide of the world now (for tests and captures).</summary>
	public PoolSlideMesh BuildMesh( PoolSlideProfile? only = null )
	{
		if ( site() is not { } place )
			return PoolSlideMesh.Empty;
		return PoolSlideMeshBuilder.Build( world.Slides.Where( slide => only == null || slide.Profile == only ), place.Ground, place.WaterLevel, origin, cellSize );
	}

	protected override void OnUpdate()
	{
		if ( deleted || (world.Version == seenVersion && cells.PoolWrites == seenPoolWrites) )
			return;
		(seenVersion, seenPoolWrites) = (world.Version, cells.PoolWrites);
		var flumes = BuildMesh( PoolSlideProfile.Flume );
		var tubes = BuildMesh( PoolSlideProfile.Tube );
		Triangles = flumes.TriangleCount + tubes.TriangleCount;
		Rebuilds++;
		Replace( flume, flumes.Track );
		Replace( tube, tubes.Track );
		// both profiles' pylons are one model
		var posts = new PoolMeshPart();
		foreach ( var part in new[] { flumes.Pylons, tubes.Pylons } )
		{
			var @base = posts.Vertices.Count;
			posts.Vertices.AddRange( part.Vertices );
			posts.Indices.AddRange( part.Indices.Select( index => index + @base ) );
		}
		Replace( pylons, posts );
	}

	private static void Replace( PoolPartEntity entity, PoolMeshPart part )
	{
		entity.Model?.ReleaseBuffers();
		entity.Model = null;
		if ( part.Vertices.Count == 0 )
			return;
		var vertices = part.Vertices.Select( vertex => new Vertex( vertex.Position, vertex.Normal, vertex.Uv ) ).ToArray();
		entity.Model = new Model( vertices, part.Indices.Select( index => (uint)index ).ToArray(), entity.PartMaterial! );
	}

	protected override void OnDelete()
	{
		deleted = true;
		foreach ( var part in new[] { flume, tube, pylons } )
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
