using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Renders an imported original level: the base.MD2 terrain meshes (node transforms composed up the
/// hierarchy), the heightfield surface with its per-cell ground texture slot, and the paths as a blended
/// layer on that ground. Each path cell takes the theme's PathTex shape and turn for its neighbours
/// (<see cref="PathTiles"/>; cells unchanged since the original save keep the saved ones); queue cells carry the theme's
/// queue models (<see cref="QueueModels"/>) on their ground. The layer follows <see cref="PathSource"/> and <see cref="QueueSource"/> (the level's cell
/// map) after <see cref="RefreshPaths"/>. Placed objects are drawn with their original models by
/// <see cref="ParkObjects"/>, not as footprint markers.
/// </summary>
public sealed class OriginalTerrain : Entity
{
	private const int MaximumTextureSlots = 16;
	private readonly List<ModelEntity> parts = new();
	private readonly List<Model> models = new();

	public OriginalTerrain( OriginalPark park )
	{
		Park = park;
		try
		{
			foreach ( var _ in Build() ) { }
		}
		catch
		{
			Delete();
			throw;
		}
	}

	private OriginalTerrain( OriginalPark park, bool _ ) => Park = park;

	/// <summary>
	/// The terrain built in steps, for a park built over frames (docs/BROWSER-PERFORMANCE.md): null after each part, then the terrain.
	/// As in the constructor, a failure deletes what was built.
	/// </summary>
	internal static IEnumerable<OriginalTerrain?> BuildSteps( OriginalPark park )
	{
		var terrain = new OriginalTerrain( park, false );
		using var steps = terrain.Build().GetEnumerator();
		while ( true )
		{
			bool more;
			try
			{
				more = steps.MoveNext();
			}
			catch
			{
				terrain.Delete();
				throw;
			}
			if ( !more )
				break;
			yield return null;
		}
		yield return terrain;
	}

	/// <summary>The heightfield, then each terrain model.</summary>
	private IEnumerable<bool> Build()
	{
		var textures = new Dictionary<string, Texture>( StringComparer.OrdinalIgnoreCase );
		foreach ( var _ in BuildHeightfield( textures ) )
			yield return true;
		yield return true;
		foreach ( var _ in BuildTerrainMeshes( textures ) )
			yield return true;
		if ( missingTextures.Count > 0 )
			Log.Warning( $"Terrain textures of {Park.LevelName} that the game data lacks, drawn plain white: {string.Join( ", ", missingTextures )}." );
	}

	public OriginalPark Park { get; }
	/// <summary>Path cells; null: the MAP InitialPath and save path cells.</summary>
	public Func<int, int, bool>? PathSource { get; set; }
	/// <summary>Queue cells, drawn with the theme's queue models instead of a path tile; null: none.</summary>
	public Func<int, int, bool>? QueueSource { get; set; }
	/// <summary>The queue model of a queue cell (<see cref="QueueModels.Choose"/>); null: queue cells are left as ground.</summary>
	public Func<int, int, QueueModelTile?>? QueueModelSource { get; set; }
	/// <summary>Whether a path cell's straight or edge takes the second texture (the chooser's roll, <see cref="PathTileVariants"/>); null: the first.</summary>
	public Func<int, int, bool>? PathVariantSource { get; set; }
	public int SurfaceCellCount { get; private set; }
	/// <summary>Path cells in the path layer.</summary>
	public int PathCellCount { get; private set; }
	private Dictionary<string, Texture>? heightfieldTextures;
	private readonly List<ModelEntity> pathParts = new();
	/// <summary>Path layer materials by texture set; kept when the layer is rebuilt, since a texture set that comes back is drawn by the same material; released with the terrain.</summary>
	private readonly Dictionary<string, Material> pathMaterials = new();

	/// <summary>Rebuilds the path layer so built and removed path cells show.</summary>
	public void RefreshPaths()
	{
		if ( heightfieldTextures == null )
			return;
		foreach ( var old in pathParts )
		{
			parts.Remove( old );
			old.Delete();
			if ( old.Model is { } model && models.Remove( model ) )
			{
				ScheduleDispose( model );
				// a queue model's material is its own (made again with every rebuild), a path batch's is shared through pathMaterials and kept for the next one
				if ( !pathMaterials.ContainsValue( model.Material ) )
					model.Material.ReleaseOwnedResources();
			}
		}
		pathParts.Clear();
		RefreshHoles();
		BuildPaths( heightfieldTextures );
	}
	public int TerrainMeshCount { get; private set; }

	/// <summary>
	/// [EXT:pools-render] Cells the terrain renderer leaves out of the ground mesh as render-only holes (the level: the pool cells, while the <c>opentpw.pools</c> extension is on), so a pool can be
	/// drawn into the ground. Only the drawn mesh follows it: height lookups, placement, the cell map and every other part of the simulation never read it. Null: none.
	/// </summary>
	public Func<int, int, bool>? RenderHoleSource { get; set; }

	/// <summary>The cells of the ground mesh: every cell with a texture slot, less the render-only holes (<paramref name="renderHole"/>), with the model's texture slot.</summary>
	internal static IEnumerable<(int X, int Y, int ModelSlot)> HeightfieldCells( ModelHeightfield field, Func<int, int, bool>? renderHole )
	{
		for ( var y = 0; y < field.CellCountZ; y++ )
		{
			for ( var x = 0; x < field.CellCountX; x++ )
			{
				var modelSlot = field.GetCellTextureSlot( x, y );
				if ( modelSlot < 0 || renderHole?.Invoke( x, y ) == true )
					continue;
				yield return (x, y, modelSlot);
			}
		}
	}

	private ModelEntity? heightfieldPart;
	private Material? heightfieldMaterial;
	private Dictionary<int, int>? heightfieldSlotIndices;
	private ulong heightfieldHoleSignature;

	private ulong HoleSignature()
	{
		var hash = 14695981039346656037UL;
		if ( RenderHoleSource is not { } hole )
			return hash;
		var field = Park.Heightfield;
		for ( var y = 0; y < field.CellCountZ; y++ )
			for ( var x = 0; x < field.CellCountX; x++ )
				if ( hole( x, y ) )
					hash = (hash ^ (uint)(y * 4096 + x)) * 1099511628211UL;
		return hash;
	}

	/// <summary>The ground mesh and the path layer; a value after each ground texture and after the mesh (a park built over frames, docs/BROWSER-PERFORMANCE.md, "Build steps").</summary>
	private IEnumerable<bool> BuildHeightfield( Dictionary<string, Texture> textures )
	{
		heightfieldTextures = textures;
		var field = Park.Heightfield;
		viewSpans = new TerrainViewSpans( field );
		Camera.ParkFarPlane = FarPlane;
		var slots = new List<Texture>();
		var slotIndices = new Dictionary<int, int>();
		// the texture slots are assigned over every cell, holes included, so rebuilding the mesh for another set of render holes keeps the one material
		foreach ( var (_, _, modelSlot) in HeightfieldCells( field, null ) )
		{
			if ( slotIndices.ContainsKey( modelSlot ) )
				continue;
			if ( slots.Count >= MaximumTextureSlots || modelSlot >= Park.TerrainModel.Textures.Count )
				throw new InvalidDataException( "The original heightfield uses more ground textures than the renderer supports." );
			slotIndices.Add( modelSlot, AddSlot( slots, LoadTexture( textures, Park.TerrainModel.Textures[modelSlot].FrameNames[0] ) ) );
			yield return true;
		}
		var staticSlots = slots.ToList();
		var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
		while ( slots.Count < MaximumTextureSlots )
			slots.Add( slots[0] );
		material.Set( "Color", slots.ToArray() );
		heightfieldMaterial = material;
		heightfieldSlotIndices = slotIndices;
		SurfaceCellCount = HeightfieldCells( field, null ).Count();
		heightfieldHoleSignature = HoleSignature();
		heightfieldPart = AddPart( BuildHeightfieldModel( material, slotIndices ), "heightfield" );
		yield return true;
		TryAddProceduralGround( staticSlots, slotIndices );
		yield return true;
		BuildPaths( textures );
	}

	private Model BuildHeightfieldModel( Material material, Dictionary<int, int> slotIndices, Func<int, int, bool>? include = null )
	{
		var vertices = new List<Vertex>();
		var indices = new List<uint>();
		foreach ( var (x, y, modelSlot) in HeightfieldCells( Park.Heightfield, RenderHoleSource ) )
		{
			if ( include != null && !include( x, y ) )
				continue;
			AddCell( vertices, indices, Park.Heightfield, x, y, slotIndices[modelSlot], PathTiles.Identity, 0, Park.Heightfield.GetCellWord( x, y ) );
		}
		return new Model( vertices.ToArray(), indices.ToArray(), material );
	}

	private TerrainViewSpans? viewSpans;

	/// <summary>
	/// The park view's far plane (<see cref="Camera.ParkFarPlane"/>): the farthest corner distance of this frame's spans, in engine units (the original's <c>0x56BB00</c> hands it to <c>0x578CA0</c> before the frame draws).
	/// </summary>
	private float? FarPlane( Matrix4x4 view, float aspectWord )
	{
		if ( viewSpans == null || heightfieldPart == null )
			return null;
		// FIRSTPERSONVIEWDISTANCE takes effect at once, as the option apply (0x44D8F0) writes [0x8BD4C0] when it runs
		viewSpans.ViewDistance = RenderFog.ViewDistanceOf( RenderFog.ViewDistanceOption() ).Distance;
		return viewSpans.Update( heightfieldPart.DrawMatrix * view, aspectWord ) ? viewSpans.FarthestCorner * OriginalParkPlacement.ModelScale : null;
	}

	private ProceduralTerrain? procedural;
	private ModelEntity? proceduralPart;
	private ModelEntity? outsidePart;

	/// <summary>The procedural ground of the level (<see cref="ProceduralTerrain"/>), or null when the option is off, the level has no <c>base.lnd</c> or the pages do not fit the material.</summary>
	internal ProceduralTerrain? ProceduralGround => procedural;

	/// <summary>
	/// Draws the ground with the original's procedural textures when the options ask for them (<see cref="ProceduralTerrainSettings.Current"/>, read now, as the original applies them at a level load) and the
	/// level has the data: the static ground part then waits for the camera modes that suspend the texturing, and the cells the rectangle leaves out keep a static part of their own.
	/// </summary>
	private void TryAddProceduralGround( List<Texture> staticSlots, Dictionary<int, int> slotIndices )
	{
		var settings = ProceduralTerrainSettings.Current();
		if ( !settings.Enabled )
			return;
		var field = Park.Heightfield;
		// the archive's members are listed, not tested with FileExists (as the lake model is found)
		var lndPath = FileSystem.GetFiles( $"/levels/{Park.LevelName}/terrain" ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "base.lnd", StringComparison.OrdinalIgnoreCase ) );
		if ( lndPath == null || ProceduralTerrainRectangle.ReadLevelSettings( Park.LevelName ) is not { } levelSettings || ProceduralTerrainRectangle.From( levelSettings ) is not { } rectangle )
			return;
		var pageCount = ProceduralTerrainAtlas.PageCountFor( settings.Size, settings.CacheCells );
		if ( staticSlots.Count + pageCount > MaximumTextureSlots )
		{
			Log.Warning( $"Procedural terrain textures of {Park.LevelName} are off: {staticSlots.Count} ground textures and {pageCount} pages do not fit {MaximumTextureSlots} slots." );
			return;
		}
		LndFile lnd;
		try
		{
			using var stream = FileSystem.OpenRead( $"/levels/{Park.LevelName}/terrain/base.lnd" );
			lnd = new LndFile( stream );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"Procedural terrain textures of {Park.LevelName} are off: base.lnd could not be read: {exception.Message}" );
			return;
		}
		var ground = new ProceduralTerrain( field, lnd, rectangle, settings, staticSlots.Count, viewSpans: viewSpans );
		var slots = staticSlots.Concat( ground.Pages ).ToList();
		while ( slots.Count < MaximumTextureSlots )
			slots.Add( slots[0] );
		var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
		material.Set( "Color", slots.ToArray() );
		procedural = ground;
		proceduralPart = AddPart( ground.CreateModel( material ), "procedural heightfield" );
		// the cells the procedural textures do not reach keep the static ground
		if ( HeightfieldCells( field, null ).Any( cell => !ground.IsProcedural( cell.X, cell.Y ) ) )
			outsidePart = AddPart( BuildHeightfieldModel( heightfieldMaterial!, slotIndices, ( x, y ) => !ground.IsProcedural( x, y ) ), "heightfield outside the procedural rectangle" );
		heightfieldPart!.Hidden = true;
		Log.Trace( $"Procedural terrain textures of {Park.LevelName}: {settings.Size} x {settings.Size}, {settings.CacheCells} cached cells in {pageCount} page(s) ({ground.Atlas.Bytes / 1048576} MB), rectangle ({rectangle.X0}, {rectangle.Y0}) to ({rectangle.X1}, {rectangle.Y1})." );
	}

	/// <summary>
	/// Camera modes the original draws with the static ground: the terrain camcorder (bit 0x2 of [0x790AB0], left through 0x42AE70) and the ride camera (bit 0x4, entered for a ride whose record has flag 0x400000, 0x4E15B0),
	/// which switch the procedural textures off (0x42AB20 calls 0x457A40 on entering them and 0x457A00 on leaving).
	/// </summary>
	// [BIN:TPW-EXE:0x0042AB20 camera mode switch] the modes with the bits 0x2 (the terrain camcorder, exit 0x42AE70) and 0x4 (a ride, record flag 0x400000, 0x4E15B0) of [0x790AB0] turn the procedural textures off (0x457A40, remembered in bit 0x200) and leaving them turns them on again (0x457A00), which does so only when [0x78D8D8] is 1 and otherwise clears the bits 0x6000
	private static bool SuspendsProceduralGround( CameraMode mode ) => mode is RideCameraMode or FreeWalkCameraMode;

	protected override void OnRender()
	{
		if ( procedural == null || proceduralPart == null || heightfieldPart == null )
			return;
		var suspended = SuspendsProceduralGround( Camera.Mode );
		proceduralPart.Hidden = suspended;
		heightfieldPart.Hidden = !suspended;
		if ( outsidePart != null )
			outsidePart.Hidden = suspended;
		if ( !suspended )
			procedural.Refresh( proceduralPart.DrawMatrix * Camera.ViewMatrix, Camera.ProjMatrix, RenderHoleSource, heightfieldHoleSignature );
	}

	/// <summary>[EXT:pools-render] Rebuilds the ground mesh when the render-only holes changed since it was last built.</summary>
	public void RefreshHoles()
	{
		if ( heightfieldPart == null || heightfieldMaterial == null || heightfieldSlotIndices == null )
			return;
		var signature = HoleSignature();
		if ( signature == heightfieldHoleSignature )
			return;
		heightfieldHoleSignature = signature;
		var old = heightfieldPart;
		parts.Remove( old );
		old.Delete();
		if ( old.Model is { } oldModel && models.Remove( oldModel ) )
			ScheduleDispose( oldModel );
		var hidden = old.Hidden;
		heightfieldPart = AddPart( BuildHeightfieldModel( heightfieldMaterial, heightfieldSlotIndices ), "heightfield" );
		heightfieldPart.Hidden = hidden;
		if ( outsidePart != null && procedural is { } ground )
		{
			var outside = outsidePart;
			parts.Remove( outside );
			outside.Delete();
			if ( outside.Model is { } outsideModel && models.Remove( outsideModel ) )
				ScheduleDispose( outsideModel );
			outsidePart = AddPart( BuildHeightfieldModel( heightfieldMaterial, heightfieldSlotIndices, ( x, y ) => !ground.IsProcedural( x, y ) ), "heightfield outside the procedural rectangle" );
			outsidePart.Hidden = outside.Hidden;
		}
	}

	/// <summary>The path layer: one quad per path cell, batched by at most 16 PathTex textures, and each queue cell's queue model.</summary>
	private void BuildPaths( Dictionary<string, Texture> textures )
	{
		var field = Park.Heightfield;
		var pathNames = ReadTextureNames( "PathTex" );
		var cells = new Dictionary<int, List<(int X, int Y, int Rotation)>>();
		var queues = new List<(int X, int Y, QueueModelTile Tile)>();
		for ( var y = 0; y < field.CellCountZ; y++ )
		{
			for ( var x = 0; x < field.CellCountX; x++ )
			{
				if ( field.GetCellTextureSlot( x, y ) < 0 )
					continue;
				if ( QueueSource?.Invoke( x, y ) == true )
				{
					// The ground under a queue model keeps its terrain texture: the refresh gives it no path tile (0x5365D0).
					if ( QueueModelSource?.Invoke( x, y ) is { } queueTile )
						queues.Add( (x, y, queueTile) );
					continue;
				}
				if ( !IsPath( x, y ) )
					continue;
				var tile = ChoosePathTile( x, y );
				if ( !pathNames.ContainsKey( tile.TextureIndex ) )
					tile = new PathTile( 0, tile.Rotation );
				if ( !cells.TryGetValue( tile.TextureIndex, out var list ) )
					cells.Add( tile.TextureIndex, list = new() );
				list.Add( (x, y, tile.Rotation) );
			}
		}
		PathCellCount = cells.Values.Sum( list => list.Count );
		// Lifted a little along the cell so the blended layer never fights the ground for depth.
		var lift = 0.01f * field.CellSizeX;
		foreach ( var batch in PathTiles.Batches( cells.Keys, MaximumTextureSlots, pathNames.ContainsKey ) )
		{
			var slots = batch.Select( index => pathNames.TryGetValue( index, out var name ) ? LoadTexture( textures, name, "pathtex" ) : Texture.Missing ).ToList();
			var vertices = new List<Vertex>();
			var indices = new List<uint>();
			for ( var slot = 0; slot < batch.Length; slot++ )
				foreach ( var (x, y, rotation) in cells.GetValueOrDefault( batch[slot] ) ?? new() )
					AddCell( vertices, indices, field, x, y, slot, rotation, lift );
			var key = string.Join( ',', batch );
			if ( !pathMaterials.TryGetValue( key, out var material ) )
			{
				material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
				while ( slots.Count < MaximumTextureSlots )
					slots.Add( slots[0] );
				// Clamped: a repeating sampler would blend each tile's open edge with its transparent opposite edge.
				material.Set( "Color", slots.ToArray(), SamplerType.Anisotropic );
				pathMaterials.Add( key, material );
			}
			pathParts.Add( AddPathPart( new Model( vertices.ToArray(), indices.ToArray(), material ), $"paths {pathParts.Count}" ) );
		}
		BuildQueueModels( textures, queues );
	}

	/// <summary>Queue cells drawn with a queue model.</summary>
	public int QueueModelCount { get; private set; }

	/// <summary>
	/// The queue models of the queue cells (<see cref="QueueModels"/>), baked into static batches: each instance stands on its cell turned by
	/// its yaw as a placed object of one cell would be (<see cref="ObjectPlacement.CellRotation"/>), each vertex on the ground under it.
	/// </summary>
	// [APPROX:PATH-015] the queue models are baked with each vertex raised by the drawn ground under it (LandscapeSampler, the path layer's lift above it, so the models follow a sloped cell and their floor shows over the grass as in the original's footage) and drawn without the ground deformation of their .hmp files (as objects, RIDES-014) and without the animation of their texture frames (the fire0 torches of queend and quebin1/2) and of the load flag 0x100000 of the two quedead entries — evidence needed: the .hmp deformation and the model loader's flag 0x100000 (0x4629D0)
	private void BuildQueueModels( Dictionary<string, Texture> textures, List<(int X, int Y, QueueModelTile Tile)> queues )
	{
		QueueModelCount = 0;
		if ( queues.Count == 0 )
			return;
		var field = Park.Heightfield;
		// The path layer's lift, so the queue's floor never fights the ground for depth.
		var lift = 0.01f * field.CellSizeX;
		var directory = $"/levels/{Park.LevelName}/queue";
		var batchTextures = new List<string>();
		var batchVertices = new List<Vertex>();
		var batchIndices = new List<uint>();
		foreach ( var group in queues.Where( queue => queue.Tile.Model >= 0 && queue.Tile.Model < QueueModels.Names.Count ).GroupBy( queue => queue.Tile.Model ).OrderBy( group => group.Key ) )
		{
			ModelFile model;
			try
			{
				model = ObjectAssets.LoadModel( $"{directory}/{QueueModels.Names[group.Key]}.md2" );
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
			{
				missingTextures.Add( $"{QueueModels.Names[group.Key]}.md2" );
				continue;
			}
			foreach ( var (x, y, tile) in group )
			{
				var turn = ObjectPlacement.CellRotation( tile.Yaw );
				foreach ( var mesh in model.Meshes )
				{
					if ( ObjectAssets.GetUnrenderableReason( mesh ) != null || mesh.Normals.Length != mesh.Vertices.Length || mesh.TexCoords.Length < mesh.Vertices.Length )
						continue;
					var names = mesh.Materials.Select( material => material.Name ).ToArray();
					if ( batchTextures.Union( names, StringComparer.OrdinalIgnoreCase ).Count() > MaximumTextureSlots )
						FlushQueueBatch( textures, directory, batchTextures, batchVertices, batchIndices );
					var slotOfMaterial = names.Select( name =>
					{
						var slot = batchTextures.FindIndex( existing => string.Equals( existing, name, StringComparison.OrdinalIgnoreCase ) );
						if ( slot < 0 )
						{
							batchTextures.Add( name );
							slot = batchTextures.Count - 1;
						}
						return slot;
					} ).ToArray();
					var world = GetWorldMatrix( model, mesh.NodeIndex );
					var first = (uint)batchVertices.Count;
					for ( var index = 0; index < mesh.Vertices.Length; index++ )
					{
						var source = mesh.Vertices[index];
						// Model space (Y up) → the cell's ground axes, turned within the cell → park model space at the cell → engine.
						var local = System.Numerics.Vector3.Transform( ObjectAssets.ConvertAxes( System.Numerics.Vector3.Transform( source.Position.GetSystemVector3(), world ) ), turn );
						var normal = System.Numerics.Vector3.TransformNormal( ObjectAssets.ConvertAxes( System.Numerics.Vector3.TransformNormal( mesh.Normals[index].GetSystemVector3(), world ) ), turn );
						var materialIndex = (int)Math.Min( source.TextureIndex, (uint)mesh.Materials.Length - 1 );
						var (groundX, groundY) = (x * field.CellSizeX + local.X, y * field.CellSizeZ + local.Y);
						batchVertices.Add( new Vertex
						{
							Position = OriginalParkPlacement.ToEngine( field, new System.Numerics.Vector3( groundX, GroundUnder( field, groundX, groundY ) + lift + local.Z, groundY ) ),
							Normal = normal.LengthSquared() > 0 ? System.Numerics.Vector3.Normalize( normal ) : System.Numerics.Vector3.UnitZ,
							TexCoords = mesh.TexCoords[index],
							TexIndex = slotOfMaterial[materialIndex],
							MatFlags = TextureSlotAddressing.VertexFlags( mesh.Materials[materialIndex].Flags )
						} );
					}
					foreach ( var index in mesh.Indices )
						batchIndices.Add( first + index );
				}
				QueueModelCount++;
			}
		}
		FlushQueueBatch( textures, directory, batchTextures, batchVertices, batchIndices );
	}

	/// <summary>The drawn ground's height under a point in model units (<see cref="LandscapeSampler"/>), the point kept on the map.</summary>
	internal static float GroundUnder( ModelHeightfield field, float x, float y ) =>
		LandscapeSampler.Sample( field.CellCountX, field.CellCountZ, field.CellSizeX, field.CellSizeZ, field.GetCornerHeight, field.SplitsAlongAntiDiagonal,
			Math.Clamp( x, 0, field.CellCountX * field.CellSizeX - 0.001f ), Math.Clamp( y, 0, field.CellCountZ * field.CellSizeZ - 0.001f ) );

	private void FlushQueueBatch( Dictionary<string, Texture> textures, string directory, List<string> names, List<Vertex> vertices, List<uint> indices )
	{
		if ( vertices.Count > 0 )
		{
			var slots = new Texture[MaximumTextureSlots];
			for ( var index = 0; index < slots.Length; index++ )
				slots[index] = index < names.Count ? LoadTexture( textures, names[index], "textures", directory ) : slots[0];
			var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
			material.Set( "Color", slots );
			pathParts.Add( AddPathPart( new Model( vertices.ToArray(), indices.ToArray(), material ), $"queue models {pathParts.Count}" ) );
		}
		names.Clear();
		vertices.Clear();
		indices.Clear();
	}

	private bool IsPath( int x, int y ) => PathSource?.Invoke( x, y ) ??
		Park.Map.GetFlagsAt( x, y ).HasFlag( MapCellFlags.InitialPath ) || Park.Save?.Cells[x, y].IsPath == true;

	private PathTile ChoosePathTile( int x, int y ) =>
		PathTiles.ForCell( x, y, Park.Heightfield.CellCountX, Park.Heightfield.CellCountZ, IsPath, Park.Save?.Cells, PathVariantSource?.Invoke( x, y ) == true );

	private static int AddSlot( List<Texture> slots, Texture texture )
	{
		slots.Add( texture );
		return slots.Count - 1;
	}

	/// <summary>
	/// One cell quad. <paramref name="rotation"/> is a path texture's clockwise turn (−Y to +X), applied through
	/// <see cref="PathTiles.TextureCoordinates"/> (docs/PATHS.md, "Path textures"); ground quads keep their coordinates.
	/// </summary>
	private static readonly uint[] DiagonalTriangles = { 0, 2, 3, 3, 1, 0 };
	private static readonly uint[] AntiDiagonalTriangles = { 0, 2, 1, 1, 2, 3 };

	private static void AddCell( List<Vertex> vertices, List<uint> indices, ModelHeightfield field, int x, int y, int slot, int rotation, float lift, uint groundWord = 0 )
	{
		var start = (uint)vertices.Count;
		// the ground texture's turn and mirror from the cell word (TerrainCellTexture); the path overlays carry no cell word and keep the path tile's own turn
		var (u0, v0) = TerrainCellTexture.Coordinate( TerrainCellTexture.Cell00, groundWord );
		var (u1, v1) = TerrainCellTexture.Coordinate( TerrainCellTexture.Cell10, groundWord );
		var (u2, v2) = TerrainCellTexture.Coordinate( TerrainCellTexture.Cell01, groundWord );
		var (u3, v3) = TerrainCellTexture.Coordinate( TerrainCellTexture.Cell11, groundWord );
		var corners = new[] { (x, y, u0, v0), (x + 1, y, u1, v1), (x, y + 1, u2, v2), (x + 1, y + 1, u3, v3) };
		if ( rotation != PathTiles.Identity )
			corners = corners.Select( corner => (corner.Item1, corner.Item2, PathTiles.TextureCoordinates( corner.Item3, corner.Item4, rotation )) )
				.Select( corner => (corner.Item1, corner.Item2, corner.Item3.U, corner.Item3.V) ).ToArray();
		var positions = corners.Select( corner => OriginalParkPlacement.ToEngine( field,
			new System.Numerics.Vector3( corner.Item1 * field.CellSizeX, field.GetCornerHeight( corner.Item1, corner.Item2 ) + lift, corner.Item2 * field.CellSizeZ ) ) ).ToArray();
		var normal = System.Numerics.Vector3.Normalize( System.Numerics.Vector3.Cross( positions[1] - positions[0], positions[2] - positions[0] ) );
		if ( normal.Z < 0 )
			normal = -normal;
		for ( var index = 0; index < 4; index++ )
		{
			vertices.Add( new Vertex
			{
				Position = positions[index],
				Normal = normal,
				TexCoords = new Vector2( corners[index].Item3, corners[index].Item4 ),
				TexIndex = slot
			} );
		}
		// The cell's own diagonal (ModelHeightfield.SplitsAlongAntiDiagonal), so the drawn ground matches the heights things stand on.
		foreach ( var offset in field.SplitsAlongAntiDiagonal( x, y ) ? AntiDiagonalTriangles : DiagonalTriangles )
			indices.Add( start + offset );
	}

	/// <summary>
	/// Converts every base.MD2 mesh to engine space and batches meshes so each draw call needs at
	/// most 16 textures, keeping the number of compiled materials small.
	/// </summary>
	private IEnumerable<bool> BuildTerrainMeshes( Dictionary<string, Texture> textures )
	{
		var batchTextures = new List<string>();
		var batchVertices = new List<Vertex>();
		var batchIndices = new List<uint>();
		foreach ( var model in new[] { Park.TerrainModel, Park.LakeModel }.OfType<ModelFile>() )
		{
			foreach ( var _ in BuildTerrainMeshes( textures, model, batchTextures, batchVertices, batchIndices ) )
				yield return true;
			yield return true;
		}
		foreach ( var _ in FlushBatch( textures, batchTextures, batchVertices, batchIndices ) )
			yield return true;
	}

	/// <summary>The terrain meshes of one model into batches; a value after each mesh and each texture a full batch loads.</summary>
	private IEnumerable<bool> BuildTerrainMeshes( Dictionary<string, Texture> textures, ModelFile model, List<string> batchTextures, List<Vertex> batchVertices, List<uint> batchIndices )
	{
		foreach ( var mesh in model.Meshes )
		{
			if ( mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 || mesh.Materials.Length is 0 or > MaximumTextureSlots || mesh.Normals.Length != mesh.Vertices.Length || mesh.TexCoords.Length < mesh.Vertices.Length )
				continue;
			var names = mesh.Materials.Select( material => material.Name ).ToArray();
			// [BIN:TPW-EXE:0x590C40 state word] a mesh with a translucent texture (the header of the texture's own file, WorldTranslucency) is one record of the original's sorted list: it is its own model,
			// and the model draws its translucent faces after everything else (Model.Companions); the other meshes batch as they always did
			var meshTextures = new Texture[names.Length];
			for ( var material = 0; material < names.Length; material++ )
			{
				var before = textures.Count;
				meshTextures[material] = LoadTexture( textures, names[material] );
				if ( textures.Count != before )
					yield return true;
			}
			var world = GetWorldMatrix( model, mesh.NodeIndex );
			if ( meshTextures.Any( texture => WorldTranslucency.IsTranslucent( texture.AlphaType ) ) )
			{
				AddTranslucentMesh( mesh, names, meshTextures, world );
				TerrainMeshCount++;
				yield return true;
				continue;
			}
			if ( batchTextures.Union( names, StringComparer.OrdinalIgnoreCase ).Count() > MaximumTextureSlots )
				foreach ( var _ in FlushBatch( textures, batchTextures, batchVertices, batchIndices ) )
					yield return true;
			var slotOfMaterial = names.Select( name =>
			{
				var slot = batchTextures.FindIndex( existing => string.Equals( existing, name, StringComparison.OrdinalIgnoreCase ) );
				if ( slot < 0 )
				{
					batchTextures.Add( name );
					slot = batchTextures.Count - 1;
				}
				return slot;
			} ).ToArray();

			var first = (uint)batchVertices.Count;
			for ( var index = 0; index < mesh.Vertices.Length; index++ )
				batchVertices.Add( MeshVertex( mesh, world, index, slotOfMaterial ) );
			foreach ( var index in mesh.Indices )
				batchIndices.Add( first + index );
			TerrainMeshCount++;
			yield return true;
		}
	}

	/// <summary>One converted vertex of a terrain mesh: engine space, with its texture index taken from <paramref name="slotOfMaterial"/> by the vertex's material.</summary>
	private Vertex MeshVertex( ModelFile.Mesh mesh, Matrix4x4 world, int index, int[] slotOfMaterial )
	{
		var source = mesh.Vertices[index];
		var position = System.Numerics.Vector3.Transform( source.Position.GetSystemVector3(), world );
		var normal = System.Numerics.Vector3.TransformNormal( mesh.Normals[index].GetSystemVector3(), world );
		var materialIndex = (int)Math.Min( source.TextureIndex, (uint)mesh.Materials.Length - 1 );
		return new Vertex
		{
			Position = OriginalParkPlacement.ToEngine( Park.Heightfield, position ),
			Normal = normal.LengthSquared() > 0 ? System.Numerics.Vector3.Normalize( new System.Numerics.Vector3( normal.X, normal.Z, normal.Y ) ) : System.Numerics.Vector3.UnitZ,
			TexCoords = mesh.TexCoords[index],
			TexIndex = slotOfMaterial[materialIndex],
			// [EXT:winter-snow-paths] the entrance plaza and its kerbs (grd_pav, grd_ctr, grd_krb) are walking surfaces: slush under snow, like the path layer
			MatFlags = TextureSlotAddressing.VertexFlags( mesh.Materials[materialIndex].Flags ) | ( TextureSlotAddressing.IsPathLikeTexture( mesh.Materials[materialIndex].Name ) ? TextureSlotAddressing.SlushFlag : 0u )
		};
	}

	/// <summary>Materials of the translucent meshes by their texture names: meshes with the same textures share one.</summary>
	private readonly Dictionary<string, Material> translucentMaterials = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>A terrain mesh with a translucent texture as a model of its own; the model splits its translucent faces by texture and draws them in the translucent pass.</summary>
	private void AddTranslucentMesh( ModelFile.Mesh mesh, string[] names, Texture[] meshTextures, Matrix4x4 world )
	{
		var key = string.Join( "|", names );
		if ( !translucentMaterials.TryGetValue( key, out var material ) )
		{
			var slots = new Texture[MaximumTextureSlots];
			for ( var index = 0; index < slots.Length; index++ )
				slots[index] = index < meshTextures.Length ? meshTextures[index] : meshTextures[0];
			var created = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
			created.Set( "Color", slots );
			translucentMaterials[key] = material = created;
		}
		var identity = Enumerable.Range( 0, names.Length ).ToArray();
		var vertices = new Vertex[mesh.Vertices.Length];
		for ( var index = 0; index < vertices.Length; index++ )
			vertices[index] = MeshVertex( mesh, world, index, identity );
		AddPart( new Model( vertices, mesh.Indices.ToArray(), material ), $"terrain translucent mesh {models.Count}" );
	}

	/// <summary>Makes a batch's model; a value after each of its textures.</summary>
	private IEnumerable<bool> FlushBatch( Dictionary<string, Texture> textures, List<string> names, List<Vertex> vertices, List<uint> indices )
	{
		if ( vertices.Count > 0 )
		{
			var slots = new Texture[MaximumTextureSlots];
			for ( var index = 0; index < slots.Length; index++ )
			{
				slots[index] = index < names.Count ? LoadTexture( textures, names[index] ) : slots[0];
				if ( index < names.Count )
					yield return true;
			}
			var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
			material.Set( "Color", slots );
			AddPart( new Model( vertices.ToArray(), indices.ToArray(), material ), $"terrain batch {models.Count}" );
		}
		names.Clear();
		vertices.Clear();
		indices.Clear();
	}

	/// <summary>Composes node transforms from the mesh node up to the root (row-vector convention).</summary>
	internal static Matrix4x4 GetWorldMatrix( ModelFile model, int nodeIndex )
	{
		var matrix = Matrix4x4.Identity;
		for ( var guard = 0; nodeIndex >= 0 && guard <= model.Nodes.Count; guard++ )
		{
			matrix *= model.Nodes[nodeIndex].Transform;
			nodeIndex = model.Nodes[nodeIndex].ParentIndex;
		}
		return matrix;
	}

	private IReadOnlyDictionary<int, string> ReadTextureNames( string listName )
	{
		var table = ListFiles( Park.TerrainDirectory ).FirstOrDefault( file => file.EndsWith( ".tct", StringComparison.OrdinalIgnoreCase ) );
		return table == null ? new Dictionary<int, string>() : ReadTextureNames( FileSystem.ReadAllText( table ), listName );
	}

	/// <summary>Entry 0 of the "PathTex" list in a theme texture correspondence table (.tct).</summary>
	internal static string? ReadPathTextureName( string table ) => ReadPathTextureNames( table ).GetValueOrDefault( 0 );

	/// <summary>The "PathTex" list of a theme texture correspondence table (.tct): index to texture name.</summary>
	internal static IReadOnlyDictionary<int, string> ReadPathTextureNames( string table ) => ReadTextureNames( table, "PathTex" );

	private static IReadOnlyDictionary<int, string> ReadTextureNames( string table, string listName )
	{
		var names = new Dictionary<int, string>();
		var inPathList = false;
		foreach ( var rawLine in table.Split( '\n' ) )
		{
			var line = rawLine.Trim();
			if ( line.Length == 0 || line.StartsWith( '#' ) )
				continue;
			var fields = line.Split( (char[]?)null, StringSplitOptions.RemoveEmptyEntries );
			if ( fields.Length == 1 )
			{
				inPathList = string.Equals( fields[0], listName, StringComparison.OrdinalIgnoreCase );
				continue;
			}
			if ( inPathList && fields.Length >= 2 && int.TryParse( fields[0], out var index ) )
				names.TryAdd( index, Path.GetFileNameWithoutExtension( fields[1] ) );
		}
		return names;
	}

	private Texture LoadTexture( Dictionary<string, Texture> textures, string name, string directory = "textures", string? terrainDirectory = null )
	{
		terrainDirectory ??= Park.TerrainDirectory;
		var key = $"{terrainDirectory}/{directory}/{Path.GetFileNameWithoutExtension( name.TrimEnd( '\0' ) )}";
		if ( textures.TryGetValue( key, out var texture ) )
			return texture;
		var path = FindTexture( terrainDirectory, Park.LevelName, name, directory, ListFiles );
		if ( path == null )
			missingTextures.Add( Path.GetFileName( key ) );
		texture = path == null ? Texture.Missing : new Texture( path, TextureFlags.Repeat );
		textures.Add( key, texture );
		return texture;
	}

	private readonly SortedSet<string> missingTextures = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The file of a terrain texture: the terrain archive's <paramref name="directory"/>, then the theme's shared textures, matched by
	/// name without extension (`.wct` in Theme Park World, `.fsh` or `.tga` in Theme Park Inc).
	/// </summary>
	internal static string? FindTexture( string terrainDirectory, string levelName, string name, string directory, Func<string, string[]> listFiles )
	{
		var baseName = Path.GetFileNameWithoutExtension( name.TrimEnd( '\0' ) );
		return new[] { $"{terrainDirectory}/{directory}", $"/levels/{levelName}/sharetex" }
			.SelectMany( listFiles )
			.FirstOrDefault( file => string.Equals( Path.GetFileNameWithoutExtension( file ), baseName, StringComparison.OrdinalIgnoreCase ) );
	}

	private readonly Dictionary<string, string[]> listings = new( StringComparer.OrdinalIgnoreCase );

	private string[] ListFiles( string directory )
	{
		if ( listings.TryGetValue( directory, out var files ) )
			return files;
		try
		{
			files = FileSystem.GetFiles( directory );
		}
		catch ( DirectoryNotFoundException )
		{
			files = Array.Empty<string>();
		}
		listings.Add( directory, files );
		return files;
	}

	private ModelEntity AddPart( Model model, string name )
	{
		models.Add( model );
		// [EXT:winter-snow-surfaces] every terrain part draws with test.shader: ground takes the full cover (the paths and queue models change it to slush where they are added)
		var part = new ModelEntity { Model = model, Name = name, SnowSurface = SnowCover.GroundSurface };
		parts.Add( part );
		return part;
	}

	/// <summary>A part of the path layer (paths and queue models): under snow it shows slush, not white (<see cref="SnowCover.PathSurface"/>).</summary>
	// [EXT:winter-snow-paths] the user's decision (2026-10-18): paths turn to slush so they stay readable
	private ModelEntity AddPathPart( Model model, string name )
	{
		var part = AddPart( model, name );
		part.SnowSurface = SnowCover.PathSurface;
		return part;
	}

	private static void ScheduleDispose( Model model )
	{
		model.ReleaseBuffers();
	}

	protected override void OnDelete()
	{
		if ( Camera.ParkFarPlane == FarPlane )
			Camera.ParkFarPlane = null;
		procedural?.Dispose();
		procedural = null;
		foreach ( var part in parts )
			part.Delete();
		parts.Clear();
		foreach ( var model in models )
			ScheduleDispose( model );
		// The materials are the terrain's own (each part made one, the path layer shares one per texture set, the heightfield keeps its own across hole changes) and carry a shader copy, a
		// pipeline and a uniform buffer each: they leave with the entity, as an object's do (OriginalObject).
		foreach ( var material in models.Select( model => model.Material ).Concat( pathMaterials.Values ).Concat( heightfieldMaterial != null ? new[] { heightfieldMaterial } : [] ).Distinct() )
			material.ReleaseOwnedResources();
		pathMaterials.Clear();
		heightfieldMaterial = null;
		models.Clear();
	}
}
