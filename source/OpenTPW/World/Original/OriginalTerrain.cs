using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Renders an imported original level: the base.MD2 terrain meshes (node transforms composed up the
/// hierarchy) and the heightfield surface with its per-cell ground texture slot. A path is not a layer over the ground, as in the original: each path cell takes the
/// theme's PathTex shape and turn for its neighbours (<see cref="PathTiles"/>; cells unchanged since the original save keep the saved ones) as its own cell texture
/// (<see cref="PathCellWord"/>), so the ground mesh leaves the cell out and the path tile is drawn in its place, turned by the cell word, or, with the procedural
/// terrain textures on, is composited into the cell's procedural texture (<see cref="ProceduralTerrain.SetCellArt"/>). Queue cells carry the theme's
/// queue models (<see cref="QueueModels"/>) as objects of their own. Both follow <see cref="PathSource"/> and <see cref="QueueSource"/> (the level's cell
/// map) after <see cref="RefreshPaths"/>. Placed objects are drawn with their original models by
/// <see cref="ParkObjects"/>, not as footprint markers.
/// </summary>
public sealed partial class OriginalTerrain : Entity
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
	/// <summary>The map flags of a cell (<see cref="ParkCellFlags"/>), for the darkening of the land outside the park (<see cref="UnownedLandDim"/>); null: no land is dimmed.</summary>
	public Func<int, int, ParkCellFlags>? LandFlagsSource { get; set; }

	private bool landDimEnabled;
	private bool terrainUnlit;

	/// <summary>The vertex flags every terrain corner carries for the lighting option: the unlit constant at LIGHTING 0.</summary>
	private uint TerrainFlags => terrainUnlit ? TextureSlotAddressing.TerrainUnlitFlag : 0u;

	/// <summary>Whether the heightfield cell (<paramref name="x"/>, <paramref name="y"/>) is drawn dimmed: the lighting option allows it and the map cell the land refresh reads for it is unowned outside the entrance area.</summary>
	private bool DimOf( int x, int y )
	{
		if ( !landDimEnabled || LandFlagsSource is not { } flags )
			return false;
		var (mx, my) = UnownedLandDim.MapCell( x, y, Park.Map.CellCountX, Park.Map.CellCountY );
		return UnownedLandDim.Dims( flags( mx, my ) );
	}

	private ulong ComputeDimSignature()
	{
		var hash = 14695981039346656037UL;
		if ( !landDimEnabled || LandFlagsSource == null )
			return hash;
		var field = Park.Heightfield;
		for ( var y = 0; y < field.CellCountZ; y++ )
			for ( var x = 0; x < field.CellCountX; x++ )
				if ( DimOf( x, y ) )
					hash = (hash ^ (uint)(y * 4096 + x + 0x20000000)) * 1099511628211UL;
		return hash;
	}
	public int SurfaceCellCount { get; private set; }
	/// <summary>Path cells that carry a path tile now.</summary>
	public int PathCellCount { get; private set; }
	private Dictionary<string, Texture>? heightfieldTextures;

	/// <summary>One path cell's tile: the texture (an index of the theme's <c>PathTex</c> list) and its turn.</summary>
	private readonly record struct PathCell( int X, int Y, int Texture, int Rotation );

	/// <summary>The path cells as the cell map has them (key <c>y * cells across + x</c>); the ground leaves each of them out.</summary>
	private Dictionary<int, PathCell> pathCells = new();
	private readonly List<(int X, int Y, QueueModelTile Tile)> queueCells = new();

	/// <summary>The queue models of the queue cells: parts of their own, drawn whatever the camera mode is.</summary>
	private readonly List<ModelEntity> queueParts = new();
	/// <summary>The static path cells the procedural ground does not reach (outside its rectangle or its weight grid), or every path cell while the procedural textures are off.</summary>
	private readonly List<ModelEntity> pathPartsOutside = new();
	/// <summary>With the procedural textures on, every path cell as a static cell, for the camera modes that suspend them; built when such a mode first shows them and again after a change.</summary>
	private readonly List<ModelEntity> pathPartsAll = new();
	private bool pathPartsAllStale = true;
	/// <summary>Path cell materials by texture set; kept when the cells are rebuilt, since a texture set that comes back is drawn by the same material; released with the terrain.</summary>
	private readonly Dictionary<string, Material> pathMaterials = new();

	/// <summary>
	/// Reads the cell map again so built and removed path cells show: the ground leaves the path cells out, the static path cells and the queue models are made again, and with the procedural textures on the cells whose
	/// tile changed are marked to be built again (the dirty bit of the original's cell word).
	/// </summary>
	public void RefreshPaths()
	{
		if ( heightfieldTextures == null )
			return;
		ScanCells();
		SyncProceduralArt();
		// the dim answers follow the live cell flags, which only a land refresh changes: scanned once here, then read by the signatures
		dimSignature = ComputeDimSignature();
		if ( dimSignature != proceduralDimSignature )
		{
			proceduralDimSignature = dimSignature;
			procedural?.MarkMeshStale();
		}
		DeleteParts( queueParts );
		DeleteParts( pathPartsOutside );
		DeleteParts( pathPartsAll );
		pathPartsAllStale = true;
		RefreshHoles();
		BuildPaths( heightfieldTextures );
	}

	private void DeleteParts( List<ModelEntity> list )
	{
		foreach ( var old in list )
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
		list.Clear();
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
	private ulong heightfieldSkipSignature;
	private ulong outsideSkipSignature;
	/// <summary>The dim answers as the last scan of the cells found them, and as the procedural mesh was last told.</summary>
	private ulong dimSignature;
	private ulong proceduralDimSignature;

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

	/// <summary>The signature of the cells the static ground leaves out: the holes (<paramref name="holes"/>) and the path cells, which take a texture of their own.</summary>
	private ulong SkipSignature( ulong holes )
	{
		var hash = holes ^ dimSignature;
		var countX = Park.Heightfield.CellCountX;
		foreach ( var key in pathCells.Keys.Order() )
			hash = (hash ^ (uint)(key / countX * 4096 + key % countX + 0x40000000)) * 1099511628211UL;
		return hash;
	}

	/// <summary>The signature of the skipped cells the procedural ground does not reach: the cells of <see cref="outsidePart"/> that are left out.</summary>
	private ulong OutsideSkipSignature( ProceduralTerrain ground )
	{
		var hash = 14695981039346656037UL;
		var field = Park.Heightfield;
		for ( var y = 0; y < field.CellCountZ; y++ )
			for ( var x = 0; x < field.CellCountX; x++ )
				if ( !ground.IsProcedural( x, y ) )
					hash = (hash ^ (uint)(y * 4096 + x + (GroundSkips( x, y ) ? 0 : 0x100000) + (DimOf( x, y ) ? 0x200000 : 0))) * 1099511628211UL;
		return hash;
	}

	/// <summary>Whether the static ground leaves cell (<paramref name="x"/>, <paramref name="y"/>) out: a hole, or a path cell, whose texture is the path tile (<see cref="PathCellWord"/>).</summary>
	private bool GroundSkips( int x, int y ) => RenderHoleSource?.Invoke( x, y ) == true || pathCells.ContainsKey( y * Park.Heightfield.CellCountX + x );

	/// <summary>The ground mesh and the path cells; a value after each ground texture and after the mesh (a park built over frames, docs/BROWSER-PERFORMANCE.md, "Build steps").</summary>
	private IEnumerable<bool> BuildHeightfield( Dictionary<string, Texture> textures )
	{
		heightfieldTextures = textures;
		// the LIGHTING option is applied when a level is loaded, like the procedural textures
		var lighting = IGraphicsSettings.Instance?.Detail.Lighting ?? 1;
		landDimEnabled = UnownedLandDim.Enabled( lighting );
		terrainUnlit = UnownedLandDim.Unlit( lighting );
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
		// the path cells are known before the ground is made: the ground leaves them out
		ScanCells();
		dimSignature = proceduralDimSignature = ComputeDimSignature();
		heightfieldHoleSignature = HoleSignature();
		heightfieldSkipSignature = SkipSignature( heightfieldHoleSignature );
		heightfieldPart = AddPart( BuildHeightfieldModel( material, slotIndices ), "heightfield" );
		yield return true;
		TryAddProceduralGround( staticSlots, slotIndices );
		yield return true;
		BuildPaths( textures );
	}

	private Model BuildHeightfieldModel( Material material, Dictionary<int, int> slotIndices, Func<int, int, bool>? include = null )
	{
		var capacity = Park.Heightfield.CellCountX * Park.Heightfield.CellCountZ;
		var vertices = new List<Vertex>( include == null ? capacity * 4 : 64 );
		var indices = new List<uint>( include == null ? capacity * 6 : 96 );
		foreach ( var (x, y, modelSlot) in HeightfieldCells( Park.Heightfield, GroundSkips ) )
		{
			if ( include != null && !include( x, y ) )
				continue;
			AddCell( vertices, indices, Park.Heightfield, x, y, slotIndices[modelSlot], Park.Heightfield.GetCellWord( x, y ), (DimOf( x, y ) ? TextureSlotAddressing.LandDimFlag : 0u) | TerrainFlags );
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
		ground.Unlit = terrainUnlit;
		proceduralPart = AddPart( ground.CreateModel( material ), "procedural heightfield" );
		ground.LandDim = DimOf;
		// the path cells the ground reaches carry their tile as art
		SyncProceduralArt();
		// the cells the procedural textures do not reach keep the static ground
		if ( HeightfieldCells( field, null ).Any( cell => !ground.IsProcedural( cell.X, cell.Y ) ) )
			outsidePart = AddPart( BuildHeightfieldModel( heightfieldMaterial!, slotIndices, ( x, y ) => !ground.IsProcedural( x, y ) ), "heightfield outside the procedural rectangle" );
		outsideSkipSignature = outsidePart != null ? OutsideSkipSignature( ground ) : 0;
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
		if ( suspended && staticHolesStale )
			RebuildHeightfieldPart();
		// the static ground has no path cells (they are cells of their own): a mode that suspends the procedural textures shows every path cell as a static one, the other modes only those the procedural ground does not reach
		if ( suspended && pathPartsAllStale && heightfieldTextures != null )
			BuildStaticPathParts( pathCells.Values, pathPartsAll, heightfieldTextures );
		foreach ( var part in pathPartsAll )
			part.Hidden = !suspended;
		foreach ( var part in pathPartsOutside )
			part.Hidden = suspended;
		proceduralPart.Hidden = suspended;
		heightfieldPart.Hidden = !suspended;
		if ( outsidePart != null )
			outsidePart.Hidden = suspended;
		if ( !suspended )
			procedural.Refresh( proceduralPart.DrawMatrix * Camera.ViewMatrix, Camera.ProjMatrix, RenderHoleSource, heightfieldHoleSignature );
	}

	/// <summary>
	/// Rebuilds the static ground meshes when the cells they leave out changed since they were last built: the render-only holes ([EXT:pools-render]) and the path cells, which take the path tile as their
	/// own texture. The holes' signature alone is what the procedural ground is told (<see cref="ProceduralTerrain.Refresh"/>).
	/// </summary>
	public void RefreshHoles()
	{
		if ( heightfieldPart == null || heightfieldMaterial == null || heightfieldSlotIndices == null )
			return;
		var holes = HoleSignature();
		heightfieldHoleSignature = holes;
		var signature = SkipSignature( holes );
		if ( signature == heightfieldSkipSignature )
			return;
		heightfieldSkipSignature = signature;
		// the part of the ground outside the procedural rectangle changes only when a skipped cell out there does
		var outsideSignature = outsidePart != null && procedural is { } groundNow ? OutsideSkipSignature( groundNow ) : 0;
		var outsideChanged = outsideSignature != outsideSkipSignature;
		outsideSkipSignature = outsideSignature;
		// with the procedural ground the static mesh is hidden: it is rebuilt when a camera mode that suspends the textures shows it, not at every placement
		if ( procedural != null && heightfieldPart.Hidden )
			staticHolesStale = true;
		else
			RebuildHeightfieldPart();
		if ( outsidePart != null && outsideChanged && procedural is { } ground )
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

	private bool staticHolesStale;

	/// <summary>The static ground mesh made again for the holes as they are now.</summary>
	private void RebuildHeightfieldPart()
	{
		staticHolesStale = false;
		var old = heightfieldPart!;
		parts.Remove( old );
		old.Delete();
		if ( old.Model is { } oldModel && models.Remove( oldModel ) )
			ScheduleDispose( oldModel );
		var hidden = old.Hidden;
		heightfieldPart = AddPart( BuildHeightfieldModel( heightfieldMaterial!, heightfieldSlotIndices!, null ), "heightfield" );
		heightfieldPart.Hidden = hidden;
	}

	/// <summary>
	/// Reads the cell map into the path cells and the queue cells. A path cell takes the theme's PathTex shape and turn for its neighbours (<see cref="PathTiles"/>); a queue cell takes no tile (the ground under a queue model
	/// keeps its terrain texture: the refresh gives it no path tile, 0x5365D0; the original makes the cell a hole of the ground and fills it with a composite floor, PATH-015).
	/// </summary>
	private void ScanCells()
	{
		var field = Park.Heightfield;
		var pathNames = ReadTextureNames( "PathTex" );
		var cells = new Dictionary<int, PathCell>();
		queueCells.Clear();
		for ( var y = 0; y < field.CellCountZ; y++ )
		{
			for ( var x = 0; x < field.CellCountX; x++ )
			{
				if ( field.GetCellTextureSlot( x, y ) < 0 )
					continue;
				if ( QueueSource?.Invoke( x, y ) == true )
				{
					if ( QueueModelSource?.Invoke( x, y ) is { } queueTile )
						queueCells.Add( (x, y, queueTile) );
					continue;
				}
				if ( !IsPath( x, y ) )
					continue;
				var tile = ChoosePathTile( x, y );
				if ( !pathNames.ContainsKey( tile.TextureIndex ) )
					tile = new PathTile( 0, tile.Rotation );
				cells[y * field.CellCountX + x] = new PathCell( x, y, tile.TextureIndex, tile.Rotation );
			}
		}
		pathCells = cells;
		PathCellCount = cells.Count;
	}

	/// <summary>The path art of the texture indexes read so far: the tile's texels as the composite reads them (<see cref="ObjectFloorBase.FlipRows"/>), or null when the data lacks the texture.</summary>
	private readonly Dictionary<int, (byte[] Rgba, int Width, int Height)?> pathArt = new();

	private (byte[] Rgba, int Width, int Height)? PathArtOf( int texture )
	{
		if ( pathArt.TryGetValue( texture, out var cached ) )
			return cached;
		(byte[], int, int)? art = null;
		if ( ReadTextureNames( "PathTex" ).TryGetValue( texture, out var name ) && FindTexture( Park.TerrainDirectory, Park.LevelName, name, "pathtex", ListFiles ) is { } path )
		{
			try
			{
				var (data, width, height) = path.EndsWith( ".wct", StringComparison.OrdinalIgnoreCase ) ? Texture.DecodeWct( path ) : Texture.DecodeImage( FileSystem, path );
				art = (ObjectFloorBase.FlipRows( data, width, height ), width, height);
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
			{
				art = null;
			}
		}
		pathArt[texture] = art;
		return art;
	}

	/// <summary>The word the cell refresh gives a path cell: the tile's slot (here the texture's index), the turn bits of its angle and the art bits over the cell's file word (<see cref="PathCellWord"/>).</summary>
	private uint WordOf( in PathCell cell ) => PathCellWord.Word( Park.Heightfield.GetCellWord( cell.X, cell.Y ), cell.Texture, cell.Rotation );

	/// <summary>
	/// With the procedural textures on, hands every cell of the procedural ground its tile as art (or takes the art of a cell that is no path any more): the cells whose tile changed are marked to be built again.
	/// </summary>
	private void SyncProceduralArt()
	{
		if ( procedural is not { } ground )
			return;
		var countX = Park.Heightfield.CellCountX;
		var wanted = new HashSet<int>();
		foreach ( var (key, cell) in pathCells )
		{
			if ( !ground.IsProcedural( cell.X, cell.Y ) )
				continue;
			wanted.Add( key );
			var art = PathArtOf( cell.Texture );
			ground.SetCellArt( cell.X, cell.Y, art is { } tile ? new ProceduralCellArt( cell.Texture, tile.Rgba, tile.Width, tile.Height, WordOf( cell ) ) : null );
		}
		// a cell that was a path and is not now takes its ground word again (the restore of 0x46DF50)
		foreach ( var previous in artKeys.Where( key => !wanted.Contains( key ) ) )
			ground.SetCellArt( previous % countX, previous / countX, null );
		artKeys.Clear();
		artKeys.UnionWith( wanted );
	}

	private readonly HashSet<int> artKeys = new();

	/// <summary>The static cells of the path cells and the queue models; with the procedural textures on only the cells the procedural ground does not reach are made now.</summary>
	private void BuildPaths( Dictionary<string, Texture> textures )
	{
		if ( procedural is { } ground )
			BuildStaticPathParts( pathCells.Values.Where( cell => !ground.IsProcedural( cell.X, cell.Y ) ), pathPartsOutside, textures );
		else
			BuildStaticPathParts( pathCells.Values, pathPartsOutside, textures );
		BuildQueueModels( textures, queueCells );
	}

	/// <summary>
	/// Path cells as cells of their own, one quad each, batched by at most 16 PathTex textures: the quad lies on the ground cell's triangles (the cell's own diagonal) with no lift, since nothing is under it, and takes the
	/// texture coordinates the cell word's turn bits give it (<see cref="TerrainCellTexture"/>), as the original's terrain draw does for every cell. The quad draws opaque: the terrain pass gives every record the state word 0x1256
	/// whatever the texture's own alpha type is, so a tile's transparent texels are drawn with their stored colour (painted grass in the shipped tiles) and nothing shows through.
	/// </summary>
	// [BIN:TPW-EXE:0x0056F753 terrain pass state] the cell draw creates its records with the constant state 0x1256 (0x101256 when [0x8BCBC8] has 0x2000), never with 0x590C40's answer for the cell's texture: no blend, no alpha test
	private void BuildStaticPathParts( IEnumerable<PathCell> cells, List<ModelEntity> target, Dictionary<string, Texture> textures )
	{
		if ( ReferenceEquals( target, pathPartsAll ) )
			pathPartsAllStale = false;
		var field = Park.Heightfield;
		var pathNames = ReadTextureNames( "PathTex" );
		var byTexture = new Dictionary<int, List<PathCell>>();
		foreach ( var cell in cells )
		{
			if ( !byTexture.TryGetValue( cell.Texture, out var list ) )
				byTexture.Add( cell.Texture, list = new() );
			list.Add( cell );
		}
		foreach ( var batch in PathTiles.Batches( byTexture.Keys, MaximumTextureSlots, pathNames.ContainsKey ) )
		{
			var slots = batch.Select( index => pathNames.TryGetValue( index, out var name ) ? LoadTexture( textures, name, "pathtex" ) : Texture.Missing ).ToList();
			var vertices = new List<Vertex>();
			var indices = new List<uint>();
			for ( var slot = 0; slot < batch.Length; slot++ )
				foreach ( var cell in byTexture.GetValueOrDefault( batch[slot] ) ?? new() )
					AddCell( vertices, indices, field, cell.X, cell.Y, slot, WordOf( cell ), WorldTranslucency.OpaqueSlotFlag | TerrainFlags | (DimOf( cell.X, cell.Y ) ? TextureSlotAddressing.LandDimFlag : 0u) );
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
			target.Add( AddPathPart( new Model( vertices.ToArray(), indices.ToArray(), material ), $"paths {target.Count}" ) );
		}
	}

	/// <summary>Queue cells drawn with a queue model.</summary>
	public int QueueModelCount { get; private set; }

	/// <summary>
	/// The queue models of the queue cells (<see cref="QueueModels"/>), baked into static batches: each instance stands on its cell turned by
	/// its yaw as a placed object of one cell would be (<see cref="ObjectPlacement.CellRotation"/>), each vertex on the ground under it.
	/// </summary>
	// [APPROX:PATH-015] the queue models are baked draped onto the landscape as tp.exe does (QueueModelDrape, 0x469A80, static batches), the cell a hole of the ground (0x46DF50) with the skirt of the object loader (opaque slot, normalised coordinates) and, with the procedural terrain textures on, the composite floor of the base art over the cell's ground (0x463060, 0x57ACE0; QueueModelFloor), and drawn without the animation of their texture frames (the fire0 torches of queend and quebin1/2) and of the load flag 0x100000 of the two quedead entries — evidence needed: the model loader's flag 0x100000 (0x4629D0) and the texture-frame animation of the torches
	private void BuildQueueModels( Dictionary<string, Texture> textures, List<(int X, int Y, QueueModelTile Tile)> queues )
	{
		QueueModelCount = 0;
		var field = Park.Heightfield;
		var directory = $"/levels/{Park.LevelName}/queue";
		var valid = queues.Where( queue => queue.Tile.Model >= 0 && queue.Tile.Model < QueueModels.Names.Count ).ToList();
		// [BIN:TPW-EXE:0x00763388 queue model table] the loader 0x522900 fills 8 slots (the table ends at 0x7633E8); the chooser's index (class-2 base 0..3, +1 for a corner, +3 per linked path neighbour) can reach 8..10, where the original would read past its slots
		foreach ( var (x, y, tile) in queues.Where( queue => queue.Tile.Model < 0 || queue.Tile.Model >= QueueModels.Names.Count ) )
		{
			if ( warnedQueueCells.Add( (x, y) ) )
				Log.Warning( $"Queue cell ({x}, {y}) chose model index {tile.Model}, past the 8 queue model slots of tp.exe; the original reads past its slot array there, so no model is drawn." );
		}
		var floors = new Dictionary<int, QueueModelFloor>();
		foreach ( var key in valid.Select( queue => queue.Tile.Model ).Distinct().OrderBy( key => key ) )
		{
			try
			{
				floors[key] = QueueModelFloor.Create( ObjectAssets.LoadModel( $"{directory}/{QueueModels.Names[key]}.md2" ), field );
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
			{
				missingTextures.Add( $"{QueueModels.Names[key]}.md2" );
			}
		}
		// with the procedural terrain textures on, every queue cell with a composite takes a tile of the atlas (the skirt word and 0x57ACE0 of the placement); the atlas and the tiles of unchanged cells persist across rebuilds
		var tileOfCell = new Dictionary<(int X, int Y), int>();
		var wanted = new List<((int Model, int X, int Y, int Yaw) Key, QueueModelFloor Floor)>();
		if ( procedural is { } ground )
		{
			foreach ( var (x, y, tile) in valid )
			{
				if ( floors.TryGetValue( tile.Model, out var floor ) && floor.HasComposite )
					wanted.Add( ((tile.Model, x, y, tile.Yaw), floor) );
			}
			if ( queueAtlas != null && queueAtlas.TileSize != ground.Size )
				ResetQueueAtlas();
			// the composites of cells that no longer exist go, and so do their tiles (reused only from the next rebuild on: the parts drawn until now may still point at them)
			var keys = wanted.Select( entry => entry.Key ).ToHashSet();
			foreach ( var key in queueComposites.Keys.Where( key => !keys.Contains( key ) ).ToList() )
			{
				queueComposites.Remove( key );
				if ( queueSlotOf.Remove( key, out var freed ) )
					queueQuarantine.Add( freed );
			}
			var fresh = new List<(int Slot, byte[] Rgba)>();
			var assigned = new List<((int Model, int X, int Y, int Yaw) Key, int Slot, byte[] Rgba)>();
			foreach ( var (key, floor) in wanted )
			{
				if ( !queueComposites.TryGetValue( key, out var rgba ) )
				{
					rgba = floor.Compose( field, key.X, key.Y, key.Yaw, ground.Texturer, slot => QueueSlotArt( floor.Model, slot, directory ) );
					queueComposites[key] = rgba;
				}
				if ( rgba == null )
					continue;
				var isNew = !queueSlotOf.ContainsKey( key );
				if ( isNew )
					queueSlotOf[key] = queueFreeSlots.Count > 0 ? queueFreeSlots.Pop() : queueNextSlot++;
				assigned.Add( (key, queueSlotOf[key], rgba) );
				if ( isNew )
					fresh.Add( (queueSlotOf[key], rgba) );
			}
			foreach ( var slot in queueQuarantine )
				queueFreeSlots.Push( slot );
			queueQuarantine.Clear();
			if ( assigned.Count > 0 )
			{
				if ( queueAtlas == null || queueNextSlot > queueAtlas.Slots )
				{
					// a bigger atlas holds every tile again; the old one is what the parts still drawn point into, so it is retired, not freed
					var old = queueAtlas;
					queueAtlas = new ProceduralTerrainAtlas( ground.Size, Math.Max( queueNextSlot, old == null ? queueNextSlot : old.Slots * 2 ) );
					if ( old != null )
					{
						retiredQueueAtlas?.Dispose();
						retiredQueueAtlas = old;
					}
					foreach ( var (_, slot, rgba) in assigned )
						queueAtlas.Upload( slot, ObjectFloorBase.FlipRows( rgba, ground.Size, ground.Size ) );
				}
				else
				{
					foreach ( var (slot, rgba) in fresh )
						queueAtlas.Upload( slot, ObjectFloorBase.FlipRows( rgba, ground.Size, ground.Size ) );
				}
				foreach ( var (key, slot, _) in assigned )
					tileOfCell[(key.X, key.Y)] = slot;
			}
		}
		else if ( queueAtlas != null )
			ResetQueueAtlas();
		var batchTextures = new List<string>();
		var batchVertices = new List<Vertex>();
		var batchIndices = new List<uint>();
		foreach ( var group in valid.Where( queue => floors.ContainsKey( queue.Tile.Model ) ).GroupBy( queue => queue.Tile.Model ).OrderBy( group => group.Key ) )
		{
			var floor = floors[group.Key];
			var model = floor.Model;
			foreach ( var (x, y, tile) in group )
			{
				// the instance height and the chain of every node on this cell; each vertex is draped by its own chain (QueueModelDrape)
				var baseHeight = QueueModelDrape.BaseHeight( field, x, y );
				var placement = QueueModelDrape.Placement( field, model, x, y, tile.Yaw, baseHeight );
				var chains = QueueModelDrape.Chains( model, placement );
				var rootChain = ModelDrape.Chain( placement, System.Numerics.Matrix4x4.Identity );
				var hasTile = tileOfCell.TryGetValue( (x, y), out var tileIndex );
				for ( var meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++ )
				{
					var mesh = floor.Floor.Apply( meshIndex, model.Meshes[meshIndex] );
					if ( ObjectAssets.GetUnrenderableReason( mesh ) != null || mesh.Normals.Length != mesh.Vertices.Length || mesh.TexCoords.Length < mesh.Vertices.Length )
						continue;
					// the composite stands in for the skirt entry's slot: the materials that name it read the cell's tile of an atlas page
					var composite = hasTile && mesh.Materials.Any( material => material.TextureIndex == floor.Entry.Slot ) ? queueAtlas!.PageOf( tileIndex ) : -1;
					var names = mesh.Materials.Select( material => hasTile && material.TextureIndex == floor.Entry.Slot ? $"{CompositePrefix}{composite}" : material.Name ).ToArray();
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
					var first = (uint)batchVertices.Count;
					var nodeChain = mesh.NodeIndex >= 0 && mesh.NodeIndex < chains.Length ? chains[mesh.NodeIndex] : rootChain;
					for ( var index = 0; index < mesh.Vertices.Length; index++ )
					{
						var source = mesh.Vertices[index];
						// Model space (Y up) → the grid's frame through the node's chain with the vertex's height on the landscape under it → engine.
						var placed = QueueModelDrape.Vertex( field, nodeChain, source.Position.GetSystemVector3(), baseHeight );
						var normal = ObjectAssets.ConvertAxes( System.Numerics.Vector3.TransformNormal( mesh.Normals[index].GetSystemVector3(), nodeChain ) );
						var materialIndex = (int)Math.Min( source.TextureIndex, (uint)mesh.Materials.Length - 1 );
						var texCoords = mesh.TexCoords[index];
						if ( hasTile && mesh.Materials[materialIndex].TextureIndex == floor.Entry.Slot )
							texCoords = queueAtlas!.PageCoordinates( tileIndex, texCoords );
						batchVertices.Add( new Vertex
						{
							Position = OriginalParkPlacement.ToEngine( field, placed ),
							Normal = normal.LengthSquared() > 0 ? System.Numerics.Vector3.Normalize( normal ) : System.Numerics.Vector3.UnitZ,
							TexCoords = texCoords,
							TexIndex = slotOfMaterial[materialIndex],
							MatFlags = TextureSlotAddressing.VertexFlags( mesh.Materials[materialIndex] )
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

	/// <summary>The name a batch slot takes for a page of the queue composites (the atlas, not a file).</summary>
	private const string CompositePrefix = "\u0001queue-composite-";

	private ProceduralTerrainAtlas? queueAtlas, retiredQueueAtlas;

	/// <summary>The atlas tile each queue composite lives in, stable across rebuilds so only the tiles of changed cells are uploaded.</summary>
	private readonly Dictionary<(int Model, int X, int Y, int Yaw), int> queueSlotOf = new();
	private readonly Stack<int> queueFreeSlots = new();
	private readonly List<int> queueQuarantine = new();
	private int queueNextSlot;

	/// <summary>The queue cells already warned about for a model index past the table (once per cell).</summary>
	private readonly HashSet<(int X, int Y)> warnedQueueCells = new();

	/// <summary>Drops the atlas and every tile assignment (the tile size changed, or the procedural textures went off); the old atlas is retired, not freed.</summary>
	private void ResetQueueAtlas()
	{
		if ( queueAtlas != null )
		{
			retiredQueueAtlas?.Dispose();
			retiredQueueAtlas = queueAtlas;
			queueAtlas = null;
		}
		queueComposites.Clear();
		queueSlotOf.Clear();
		queueFreeSlots.Clear();
		queueQuarantine.Clear();
		queueNextSlot = 0;
	}

	/// <summary>The composites made so far by (model, cell, yaw): the placement of a queue cell makes one once (the ground under it does not change), so a rebuild of the path layer reuses it.</summary>
	private readonly Dictionary<(int Model, int X, int Y, int Yaw), byte[]?> queueComposites = new();

	/// <summary>The art of a slot of a queue model (its first frame's file in the queue archive), as the composite reads it.</summary>
	private ObjectFloorBase.SlotArt? QueueSlotArt( ModelFile model, int slot, string directory )
	{
		if ( slot < 0 || slot >= model.Textures.Count || model.Textures[slot].FrameNames.Count == 0 )
			return null;
		var path = FindTexture( directory, Park.LevelName, model.Textures[slot].FrameNames[0], "textures", ListFiles );
		if ( path == null )
			return null;
		var (data, width, height) = path.EndsWith( ".wct", StringComparison.OrdinalIgnoreCase ) ? Texture.DecodeWct( path ) : Texture.DecodeImage( FileSystem, path );
		return new ObjectFloorBase.SlotArt( data, width, height );
	}

	/// <summary>The drawn ground's height under a point in model units (<see cref="LandscapeSampler"/>), the point kept on the map.</summary>
	internal static float GroundUnder( ModelHeightfield field, float x, float y ) =>
		LandscapeSampler.Sample( field.CellCountX, field.CellCountZ, field.CellSizeX, field.CellSizeZ, field.GetCornerHeight, field.SplitsAlongAntiDiagonal,
			Math.Clamp( x, 0, field.CellCountX * field.CellSizeX - 0.001f ), Math.Clamp( y, 0, field.CellCountZ * field.CellSizeZ - 0.001f ) );

	private Texture QueueSlotTexture( Dictionary<string, Texture> textures, string name, string directory ) =>
		name.StartsWith( CompositePrefix, StringComparison.Ordinal ) && queueAtlas != null && int.TryParse( name.AsSpan( CompositePrefix.Length ), out var page ) ? queueAtlas.Pages[page] : LoadTexture( textures, name, "textures", directory );

	private void FlushQueueBatch( Dictionary<string, Texture> textures, string directory, List<string> names, List<Vertex> vertices, List<uint> indices )
	{
		if ( vertices.Count > 0 )
		{
			var slots = new Texture[MaximumTextureSlots];
			for ( var index = 0; index < slots.Length; index++ )
				slots[index] = index < names.Count ? QueueSlotTexture( textures, names[index], directory ) : slots[0];
			var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
			material.Set( "Color", slots );
			queueParts.Add( AddPathPart( new Model( vertices.ToArray(), indices.ToArray(), material ), $"queue models {queueParts.Count}" ) );
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
	/// One cell quad, on the cell's own diagonal (<see cref="ModelHeightfield.SplitsAlongAntiDiagonal"/>), with the texture coordinates the cell word's turn and mirror bits give it
	/// (<see cref="TerrainCellTexture"/>): the ground cell's own word, or a path cell's (<see cref="PathCellWord"/>), whose turn bits are the tile's angle.
	/// </summary>
	private static readonly uint[] DiagonalTriangles = { 0, 2, 3, 3, 1, 0 };
	private static readonly uint[] AntiDiagonalTriangles = { 0, 2, 1, 1, 2, 3 };

	internal static void AddCell( List<Vertex> vertices, List<uint> indices, ModelHeightfield field, int x, int y, int slot, uint cellWord, uint matFlags = 0 )
	{
		var start = (uint)vertices.Count;
		// the corners in the renderer's order: (x, z), (x + 1, z), (x, z + 1), (x + 1, z + 1) with the cell corner each takes its texture coordinate from
		Span<System.Numerics.Vector3> positions = stackalloc System.Numerics.Vector3[4];
		Span<Vector2> coordinates = stackalloc Vector2[4];
		ReadOnlySpan<int> cellCorners = [TerrainCellTexture.Cell00, TerrainCellTexture.Cell10, TerrainCellTexture.Cell01, TerrainCellTexture.Cell11];
		for ( var index = 0; index < 4; index++ )
		{
			var cx = x + (index & 1);
			var cy = y + (index >> 1);
			positions[index] = OriginalParkPlacement.ToEngine( field, new System.Numerics.Vector3( cx * field.CellSizeX, field.GetCornerHeight( cx, cy ), cy * field.CellSizeZ ) );
			var (u, v) = TerrainCellTexture.Coordinate( cellCorners[index], cellWord );
			coordinates[index] = new Vector2( u, v );
		}
		var normal = System.Numerics.Vector3.Normalize( System.Numerics.Vector3.Cross( positions[1] - positions[0], positions[2] - positions[0] ) );
		if ( normal.Z < 0 )
			normal = -normal;
		for ( var index = 0; index < 4; index++ )
			vertices.Add( new Vertex { Position = positions[index], Normal = normal, TexCoords = coordinates[index], TexIndex = slot, MatFlags = matFlags } );
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
		uvAnimator = TerrainUvAnimator.Create( Park.TerrainModel, ListFiles( Park.TerrainDirectory ) );
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
		var runningIndex = -1;
		foreach ( var mesh in model.Meshes )
		{
			runningIndex++;
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
			// a mesh the terrain clip scrolls (the river and waterfall of the Jungle) is a model of its own with dynamic vertices, like a translucent one
			var meshIndex = uvAnimator != null && ReferenceEquals( model, Park.TerrainModel ) ? runningIndex : -1;
			var scrolls = meshIndex >= 0 && uvAnimator!.Animates( meshIndex );
			if ( scrolls || meshTextures.Any( texture => WorldTranslucency.IsTranslucent( texture.AlphaType ) ) )
			{
				var (added, addedVertices) = AddTranslucentMesh( mesh, names, meshTextures, world, scrolls );
				if ( scrolls )
					uvAnimator!.Register( meshIndex, addedVertices, added.UpdateVertices );
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
			MatFlags = TextureSlotAddressing.VertexFlags( mesh.Materials[materialIndex] ) | ( TextureSlotAddressing.IsPathLikeTexture( mesh.Materials[materialIndex].Name ) ? TextureSlotAddressing.SlushFlag : 0u )
		};
	}

	/// <summary>Materials of the translucent meshes by their texture names: meshes with the same textures share one.</summary>
	private readonly Dictionary<string, Material> translucentMaterials = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>A terrain mesh with a translucent texture as a model of its own; the model splits its translucent faces by texture and draws them in the translucent pass.</summary>
	private (Model Model, Vertex[] Vertices) AddTranslucentMesh( ModelFile.Mesh mesh, string[] names, Texture[] meshTextures, Matrix4x4 world, bool dynamicVertices = false )
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
		var model = new Model( vertices, mesh.Indices.ToArray(), material, dynamicVertices );
		AddPart( model, $"terrain translucent mesh {models.Count}" );
		return (model, vertices);
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
		queueAtlas?.Dispose();
		retiredQueueAtlas?.Dispose();
		queueAtlas = retiredQueueAtlas = null;
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
