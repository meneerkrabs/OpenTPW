using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Draws the track tiles and the vehicles of one track family of a level (docs/TRACKS.md): each track tile is the piece mesh its cell chose, instanced at the
/// tile's origin with the engine rotation 360 − rotation (the pieces are 20 world units wide), and each vehicle is one of the ride's supplemental meshes
/// (<c>wr_ring.md2</c> for the boats, <c>gk_*.md2</c> for the karts) at the vehicle position divided by 307.2. The meshes are baked with their rest
/// transforms; a piece hides the end caps of its arm mask, and a tile whose piece plays its water clip (<see cref="TrackPieceAnimations"/>) draws the moving
/// meshes from a copy of its own draped vertices that its group of tiles (those that started together) animates. Every tile drapes onto the landscape (<see cref="TrackDrape"/>). A kart's riders are drawn by the ride-head renderer at the kart's head dummies
/// (<see cref="CollectHeads"/>).
/// </summary>
public sealed class TrackRenderer : IDisposable
{
	private const string TextureArrayName = "Color";
	/// <summary>Section units per MD2 unit: one cell is 0xC00 = 10 × 307.2.</summary>
	public const float UnitsPerModelUnit = 307.2f;

	/// <summary>
	/// A drawable mesh set with the surface it provides and, for a vehicle mesh, the rest transforms and flags of its rider head dummies by seat (head 1, 2, ...;
	/// null where the model has no record for a seat).
	/// </summary>
	private sealed record PieceGeometry( TrackSurface Surface, Matrix4x4?[] HeadNodes, uint[] HeadFlags, Matrix4x4?[] SparksNodes )
	{
		/// <summary>The drawable parts with the mesh each came from.</summary>
		public List<PiecePart> Parts { get; init; } = new();
		/// <summary>The part models, built on first use.</summary>
		public IEnumerable<Model> Models => Parts.Select( part => part.Model );
		/// <summary>The piece's mesh, for the clip's animator.</summary>
		public ModelFile? Mesh { get; init; }
		/// <summary>The piece's water clip (animation 5), or null when it has none or it cannot be played on the mesh.</summary>
		public (ModelAnimation Clip, string Name)? Water { get; init; }
		/// <summary>Per mesh: the water clip moves its texture coordinates and a playing tile draws a copy of it.</summary>
		public bool[] UvMeshes { get; init; } = Array.Empty<bool>();
	}

	/// <summary>
	/// One drawable part of a piece: the mesh it came from and its baked vertices and indices (a playing or draped tile's copy starts from them). Its textures, material and model
	/// are made on first use, so the vertices and the vehicle surface can be read without a graphics device.
	/// </summary>
	private sealed class PiecePart
	{
		private readonly ObjectCatalogEntry entry;
		private readonly string[] textures;
		private Material? material;
		private Model? model;

		public PiecePart( ObjectCatalogEntry entry, int mesh, Vertex[] vertices, uint[] indices, string[] textures )
		{
			this.entry = entry;
			this.textures = textures;
			Mesh = mesh;
			Vertices = vertices;
			Indices = indices;
		}

		public int Mesh { get; }
		public Vertex[] Vertices { get; }
		public uint[] Indices { get; }

		public Material Material => material ??= CreateMaterial();

		public Model Model => model ??= new Model( Vertices, Indices, Material, false );

		private Material CreateMaterial()
		{
			var slots = new Texture[16];
			Array.Fill( slots, Texture.Missing );
			for ( var index = 0; index < textures.Length; index++ )
				slots[index] = LoadTexture( entry, textures[index] );
			var created = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
			created.Set( TextureArrayName, slots );
			return created;
		}

		public void Release()
		{
			model?.ReleaseBuffers();
			material?.ReleaseOwnedResources();
			model = null;
			material = null;
		}
	}

	/// <summary>The tiles of one piece that started their water clip at the same moment (and froze at the same moment): one animator and, per tile, a copy of the moving meshes.</summary>
	private sealed class PieceClipGroup
	{
		public required ObjectAnimator Animator { get; init; }
		public double Shown;
		public bool Used;
		/// <summary>
		/// The copies of the moving parts with the animator version they show: one shared by the undraped tiles per part (tile -1, -1), and one per tile and part where the drape moved
		/// the part's vertices (it starts from them). <see cref="UsedCopies"/> are those the last rebuild drew.
		/// </summary>
		public readonly Dictionary<(int X, int Y, int Part), (Model Model, Vertex[] Vertices, int Version, Vertex[] Source)> Copies = new();
		public readonly HashSet<(int X, int Y, int Part)> UsedCopies = new();
	}

	/// <summary>One placed tile's drape: its draped vertices per part (null where nothing moves), the vehicle surface of its piece on the draped faces, and the models built from them.</summary>
	private sealed class TileDrape
	{
		public required (int Piece, int Rotation) Key { get; init; }
		public required Vertex[]?[] Vertices { get; init; }
		public required TrackSurface Surface { get; init; }
		/// <summary>The owned models of the draped parts, built when the tile is drawn (a part that nothing lifts uses the piece's shared model).</summary>
		public readonly Model?[] Models;
		public bool Used;

		public TileDrape( int parts ) => Models = new Model?[parts];

		public void ReleaseModels()
		{
			for ( var index = 0; index < Models.Length; index++ )
			{
				Models[index]?.ReleaseBuffers();
				Models[index] = null;
			}
		}
	}

	/// <summary>The entities of one vehicle: its body, its shadow (hovering karts) and the transform the heads hang from.</summary>
	private sealed class VehicleVisual
	{
		public int Variant = -1;
		public bool HasShadow;
		public List<ModelEntity> Body = new();
		public List<ModelEntity> Shadow = new();
		public BumpVehicle? Vehicle;
		public BumpController? Controller;
		public Matrix4x4 Transform = Matrix4x4.Identity;
		public bool Hover;
	}

	private readonly ObjectCatalogEntry entry;
	private readonly TrackPieceKit? kit;
	private readonly Func<Vector2> gridOrigin;
	private readonly Func<int, int, float> groundHeight;
	private readonly Func<int, int, float>? cornerHeight;
	private readonly Func<float, float, float>? terrainHeight;
	private readonly Func<float, float, float>? drapeHeight;
	private readonly Dictionary<int, PieceGeometry> pieces = new();
	private readonly Dictionary<(int X, int Y), TileDrape> drapes = new();
	/// <summary>Drapes a probe or a rebuild replaced while entities may still draw their models: released after the next rebuild's entities are made.</summary>
	private readonly List<TileDrape> retired = new();
	private readonly Dictionary<int, PieceGeometry?> vehicleMeshes = new();
	private readonly List<ModelEntity> tiles = new();
	private readonly Dictionary<(int Piece, double Start, double? Stop), PieceClipGroup> clipGroups = new();
	private TrackPieceAnimations? animations;
	private int animationVersion = -1;
	private readonly List<VehicleVisual> visuals = new();
	private readonly List<(BumpController Controller, BumpVehicle Vehicle)> wanted = new();
	private readonly Func<float, float, float> probe;
	private TrackCellLayer? probeLayer;
	private float probeFallback;
	private TrackSurface? rideSurface;
	private bool rideSurfaceFailed;
	private int version = -1;
	private bool disposed;

	/// <param name="entry">The ride whose archive holds the pieces, the vehicle meshes and the textures.</param>
	/// <param name="kit">The piece kit of the ride's family; this renderer draws that family's cells and the controllers it is given. Null for the bumper cars, which have no pieces.</param>
	/// <param name="gridOrigin">Engine position of grid corner (0, 0).</param>
	/// <param name="groundHeight">Engine height of the ground at a cell.</param>
	/// <param name="cornerHeight">Engine height of the ground at a grid corner (the landscape vertex); null takes the cells' heights.</param>
	/// <param name="terrainHeight">Engine height of the ground under a model-unit point of the grid (the original's landscape sampler); null takes the cell's height.</param>
	/// <param name="drapeHeight">Engine height of the ground a draped piece vertex takes at a model-unit point of the grid (the drape's always-split sampler, <see cref="LandscapeSampler.SampleDrape"/>); null takes <paramref name="terrainHeight"/>.</param>
	public TrackRenderer( ObjectCatalogEntry entry, TrackPieceKit? kit, Func<Vector2> gridOrigin, Func<int, int, float> groundHeight, Func<int, int, float>? cornerHeight = null,
		Func<float, float, float>? terrainHeight = null, Func<float, float, float>? drapeHeight = null )
	{
		this.cornerHeight = cornerHeight;
		this.terrainHeight = terrainHeight;
		this.drapeHeight = drapeHeight;
		this.entry = entry;
		this.kit = kit;
		this.gridOrigin = gridOrigin;
		this.groundHeight = groundHeight;
		// a probe the sampler finds no surface for keeps the vehicle's last height (0x00546280: a −100 sample takes +0xA0)
		probe = ( x, y ) => SurfaceHeight( probeLayer!, x, y ) ?? probeFallback;
	}

	public TrackFamily Family => kit?.Family ?? TrackFamily.Bumper;

	/// <summary>
	/// The object standing on a cell, for the vehicles' height probes: the sampler surface of its model, the matrix from model units of the grid (the probes' x and y) to its model's own
	/// ground plane, and its base height in model units; null when no object stands on the cell. A cell holding no track, an add-on's tile and a station tile have no piece instance and
	/// are sampled through it (<see cref="SurfaceHeight"/>).
	/// </summary>
	public Func<int, int, (TrackSurface Surface, Matrix4x4 GridToModel, float BaseHeight)?>? ObjectSurfaceAt { get; set; }
	/// <summary>Tile instances drawn after the last <see cref="Update"/>.</summary>
	public int TileCount => tiles.Count;
	/// <summary>Vehicle instances drawn after the last <see cref="Update"/>.</summary>
	public int BoatCount => visuals.Count;
	/// <summary>Shadow instances of hovering karts after the last <see cref="Update"/>.</summary>
	public int ShadowCount => visuals.Count( visual => visual.Shadow.Count > 0 );

	/// <summary>
	/// Rebuilds the tile instances when the layer or the pieces' clips changed, plays the clips to the clock of <paramref name="pieceAnimations"/> and moves a
	/// vehicle instance to every vehicle of <paramref name="controllers"/>.
	/// </summary>
	public void Update( TrackCellLayer layer, IEnumerable<BumpController> controllers, TrackPieceAnimations? pieceAnimations = null )
	{
		if ( disposed )
			return;
		if ( layer.Version != version || !ReferenceEquals( pieceAnimations, animations ) || (pieceAnimations?.Version ?? -1) != animationVersion )
		{
			version = layer.Version;
			animations = pieceAnimations;
			animationVersion = pieceAnimations?.Version ?? -1;
			RebuildTiles( layer );
		}
		PlayClips();
		UpdateVehicles( layer, controllers );
	}

	/// <summary>Piece clips playing or frozen after the last <see cref="Update"/> (groups of tiles that started together).</summary>
	public int ClipGroupCount => clipGroups.Count;

	private void RebuildTiles( TrackCellLayer layer )
	{
		foreach ( var entity in tiles )
			entity.Delete();
		tiles.Clear();
		foreach ( var drape in drapes.Values )
			drape.Used = false;
		foreach ( var group in clipGroups.Values )
		{
			group.Used = false;
			group.UsedCopies.Clear();
		}
		if ( kit != null )
		{
			foreach ( var (x, y, cell) in layer.Cells() )
			{
				// [BIN:TPW-EXE:0x0053AFA0 piece instancer] a tile whose flag word has the add-on bit 0x10 gets no piece instance (+4 = 0): the add-on standing on it is drawn instead
				if ( !cell.IsOrigin || cell.Piece == 0 || cell.Family != (int)kit.Family || (cell.Flags & TrackCell.FlagAddOn) != 0 )
					continue;
				var geometry = GeometryOf( cell.Piece );
				if ( geometry == null )
					continue;
				var group = animations?.At( layer, x, y ) is { } play && geometry.Water is { } water ? ClipGroup( cell.Piece, geometry, water, play ) : null;
				var drape = DrapeOf( x, y, cell.Piece, cell.Rotation, geometry );
				drape.Used = true;
				var transform = TileTransform( x, y, cell.Rotation );
				for ( var index = 0; index < geometry.Parts.Count; index++ )
				{
					var model = group != null && geometry.UvMeshes[geometry.Parts[index].Mesh]
						? CopyFor( group, drape.Vertices[index] == null ? (-1, -1, index) : (x, y, index), geometry.Parts[index], drape.Vertices[index] ?? geometry.Parts[index].Vertices )
						: ModelOf( drape, geometry, index );
					tiles.Add( new ModelEntity { Model = model, Name = $"track:{x},{y}", SnowSurface = SnowCover.PathSurface, TransformOverride = transform, Position = new Vector3( transform.M41, transform.M42, transform.M43 ) } );
				}
			}
		}
		foreach ( var key in drapes.Where( pair => !pair.Value.Used ).Select( pair => pair.Key ).ToList() )
		{
			drapes[key].ReleaseModels();
			drapes.Remove( key );
		}
		// the entities drawing the replaced drapes and unused copies are gone now
		foreach ( var drape in retired )
			drape.ReleaseModels();
		retired.Clear();
		foreach ( var key in clipGroups.Where( pair => !pair.Value.Used ).Select( pair => pair.Key ).ToList() )
		{
			ReleaseGroup( clipGroups[key] );
			clipGroups.Remove( key );
		}
		foreach ( var group in clipGroups.Values )
		{
			foreach ( var key in group.Copies.Keys.Where( key => !group.UsedCopies.Contains( key ) ).ToList() )
			{
				group.Copies[key].Model.ReleaseBuffers();
				group.Copies.Remove( key );
			}
		}
	}

	/// <summary>
	/// The drape of the tile whose origin cell is (<paramref name="x"/>, <paramref name="y"/>), built on first use from the landscape and kept while the piece and its rotation stay: every part's
	/// vertices lifted onto the landscape, and the piece's vehicle surface on the lifted faces. A drape it replaces is released with the next rebuild, since entities may still draw it.
	/// </summary>
	private TileDrape DrapeOf( int x, int y, int piece, int rotation, PieceGeometry geometry )
	{
		var key = (piece, rotation);
		if ( drapes.TryGetValue( (x, y), out var cached ) )
		{
			if ( cached.Key == key )
				return cached;
			retired.Add( cached );
		}
		var lift = TrackDrape.Lift( x, y, rotation, TileHeight( x, y ), DrapeTerrain );
		var drape = new TileDrape( geometry.Parts.Count )
		{
			Key = key,
			Vertices = geometry.Parts.Select( part => TrackDrape.Apply( part.Vertices, lift ) ).ToArray(),
			Surface = geometry.Surface.Draped( lift )
		};
		drapes[(x, y)] = drape;
		return drape;
	}

	/// <summary>The vertices of every drawn part of the tile whose origin cell is (<paramref name="x"/>, <paramref name="y"/>), in the piece's own space with the drape applied (the tile's transform places them), or none for a cell without a piece.</summary>
	internal IReadOnlyList<Vertex[]> DrapedVertices( TrackCellLayer layer, int x, int y )
	{
		var cell = layer.Get( x, y );
		if ( kit == null || !cell.IsOrigin || cell.Piece == 0 || GeometryOf( cell.Piece ) is not { } geometry )
			return Array.Empty<Vertex[]>();
		var drape = DrapeOf( x, y, cell.Piece, cell.Rotation, geometry );
		return geometry.Parts.Select( ( part, index ) => drape.Vertices[index] ?? part.Vertices ).ToList();
	}

	/// <summary>The model of a tile's part: the piece's shared model where nothing is lifted, one owned model of the draped vertices otherwise.</summary>
	private static Model ModelOf( TileDrape drape, PieceGeometry geometry, int index )
	{
		if ( drape.Vertices[index] is not { } vertices )
			return geometry.Parts[index].Model;
		return drape.Models[index] ??= new Model( vertices, geometry.Parts[index].Indices, geometry.Parts[index].Material, false );
	}

	/// <summary>The landscape a draped vertex takes: the drape sampler, else the terrain sampler, else the cell's height, in engine units at a model-unit point of the grid.</summary>
	private float DrapeTerrain( float x, float y )
	{
		if ( drapeHeight != null )
			return drapeHeight( x, y );
		if ( terrainHeight != null )
			return terrainHeight( x, y );
		return groundHeight( Math.Max( 0, (int)MathF.Floor( x / 10 ) ), Math.Max( 0, (int)MathF.Floor( y / 10 ) ) );
	}

	/// <summary>The clip group of the tiles of <paramref name="piece"/> that started (and froze) with <paramref name="play"/>, created on first use with the clip started at its frame 0.</summary>
	// [BIN:TPW-EXE:0x0052A490 forward piece walk] every piece the walk passes gets 0x004732A0(instance, 5, 0, 7, 1.0, 0): animation 5 (the mesh's m clip), variant 0, flags 7 (loop, at once), speed 1, channel 0
	private PieceClipGroup ClipGroup( int piece, PieceGeometry geometry, (ModelAnimation Clip, string Name) water, TrackPieceAnimations.Play play )
	{
		var key = (piece, play.Start, play.Stop);
		if ( !clipGroups.TryGetValue( key, out var group ) )
		{
			var animator = new ObjectAnimator( geometry.Mesh! );
			animator.Trigger( 0, water.Clip, water.Name, TrackPieceKit.WaterAnimation, 0, loop: true );
			group = new PieceClipGroup { Animator = animator };
			clipGroups[key] = group;
		}
		group.Used = true;
		return group;
	}

	/// <summary>
	/// The copy of a moving part a tile draws: the group's shared copy (key tile -1, -1) for an undraped part, the tile's own for a draped one. It is kept across rebuilds while its
	/// source vertices are the same array, so a rebuild neither reallocates nor re-uploads it.
	/// </summary>
	private static Model CopyFor( PieceClipGroup group, (int X, int Y, int Part) key, PiecePart part, Vertex[] source )
	{
		group.UsedCopies.Add( key );
		if ( group.Copies.TryGetValue( key, out var existing ) )
		{
			if ( ReferenceEquals( existing.Source, source ) )
				return existing.Model;
			existing.Model.ReleaseBuffers();
		}
		var vertices = (Vertex[])source.Clone();
		var model = new Model( vertices, part.Indices, part.Material, true );
		group.Copies[key] = (model, vertices, -1, source);
		return model;
	}

	/// <summary>Plays every clip group to its tiles' time (frozen groups stop at their freeze) and rewrites the texture coordinates of the copies that changed.</summary>
	private void PlayClips()
	{
		if ( animations == null )
			return;
		foreach ( var ((piece, start, stop), group) in clipGroups )
		{
			var target = Math.Max( 0, (stop ?? animations.Clock) - start );
			// a long gap is played in steps shorter than a clip, so a looping clip replays as often as it would frame by frame
			while ( target - group.Shown > 1e-9 )
			{
				var step = Math.Min( target - group.Shown, 0.5 );
				group.Animator.Advance( step );
				group.Shown += step;
			}
			if ( pieces.GetValueOrDefault( piece ) is not { } geometry )
				continue;
			foreach ( var (copyKey, copy) in group.Copies.ToList() )
			{
				var mesh = geometry.Parts[copyKey.Part].Mesh;
				var current = group.Animator.GetVertexVersion( mesh );
				if ( current == copy.Version )
					continue;
				ObjectRenderParts.WriteTexCoords( geometry.Mesh!.Meshes[mesh], group.Animator.GetTexCoords( mesh ), copy.Vertices );
				copy.Model.UpdateVertices( copy.Vertices );
				group.Copies[copyKey] = copy with { Version = current };
			}
		}
	}

	private static void ReleaseCopies( PieceClipGroup group )
	{
		foreach ( var copy in group.Copies.Values )
			copy.Model.ReleaseBuffers();
		group.Copies.Clear();
		group.UsedCopies.Clear();
	}

	private static void ReleaseGroup( PieceClipGroup group ) => ReleaseCopies( group );

	/// <summary>
	/// The matrix of the tile whose origin cell is (<paramref name="x"/>, <paramref name="y"/>): the piece turns counter-clockwise by
	/// <paramref name="rotation"/> degrees about its centre (the original's engine rotation is 360 − rotation), then moves to the tile and the ground.
	/// </summary>
	// [BIN:TPW-EXE:0x522B90 piece instancing] the kart instancer (the water one 0x522D50 alike) turns the rotation to the engine's 360 − rotation and adds one cell to the origin cell by it (90°: y+1; 180°: x+1, y+1; 270°: x+1), then the mesh creation 0x463060 → 0x466F90 adds the same one cell again, so the instance turns about the tile's corner and sits two cells (a whole tile) over: the tile turns about its centre, as here
	// [BIN:TPW-EXE:0x463060 instance height] the instance height is 0x466D70's midrange, (lowest + highest landscape vertex of the tile's 3 × 3 vertices (every footprint cell, flag 0x10) ) / 2, written at +0x44 by 0x467030, not the mean of the four cell means
	public Matrix4x4 TileTransform( int x, int y, int rotation )
	{
		var ground = TileHeight( x, y );
		var origin = gridOrigin();
		return Matrix4x4.CreateTranslation( -10, -10, 0 )
			* Matrix4x4.CreateRotationZ( rotation * MathF.PI / 180 )
			* Matrix4x4.CreateTranslation( 10 + x * 10, 10 + y * 10, 0 )
			* Matrix4x4.CreateScale( ObjectPlacement.ModelScale )
			* Matrix4x4.CreateTranslation( origin.X, origin.Y, ground );
	}

	/// <summary>The height a tile's instance stands on: half the sum of the lowest and highest ground height among the tile's 3 × 3 grid corners (0x466D70).</summary>
	public float TileHeight( int x, int y )
	{
		if ( cornerHeight == null )
			return (groundHeight( x, y ) + groundHeight( x + 1, y ) + groundHeight( x, y + 1 ) + groundHeight( x + 1, y + 1 )) / 4;
		var (low, high) = (float.MaxValue, float.MinValue);
		for ( var dx = 0; dx <= 2; dx++ )
		{
			for ( var dy = 0; dy <= 2; dy++ )
			{
				var height = cornerHeight( x + dx, y + dy );
				low = MathF.Min( low, height );
				high = MathF.Max( high, height );
			}
		}
		return (low + high) / 2;
	}

	/// <summary>
	/// The ground the cell lookup 0x00452AE0 gives for a map cell that holds a piece instance of this renderer's family, in model units: the byte of the piece's height
	/// map (<c>.hmp</c>) x 100/255 plus the instance height. Null for a cell without a piece instance (no track, an add-on's tile, a station, another family's tile)
	/// or outside the map, where the caller takes the object on the cell or the terrain.
	/// </summary>
	// [BIN:TPW-EXE:0x0053BF30 second-layer instance] the lookup asks the cell's second-layer mesh instance first and follows an extension cell (types 0xC and 0x11) to its tile's origin cell; the piece instance (0x00522B90 / 0x00522D50 through 0x00463060) carries the piece's own map (+0xCC), read as an object's
	// [BIN:TPW-EXE:0x00466F90 instance position] the instance sits at the origin cell plus the cell offset of the engine rotation (360 - rotation: 90 y+1, 180 x+1 and y+1, 270 x+1) added twice, once by the instancer and once by 0x00466F90, so the map's 2 x 2 cells are indexed from that corner with the engine rotation
	public float? PieceGround( TrackCellLayer layer, int cellX, int cellY )
	{
		if ( kit == null || !layer.InBounds( cellX, cellY ) )
			return null;
		var cell = layer.Get( cellX, cellY );
		if ( cell.Type == 0 )
			return null;
		var (originX, originY) = cell.IsExtension ? layer.CellOf( cell.Parent ) : (cellX, cellY);
		var tile = layer.Get( originX, originY );
		if ( !tile.IsOrigin || tile.Piece == 0 || tile.Family != (int)kit.Family || (tile.Flags & TrackCell.FlagAddOn) != 0 || kit.GetHeightProfile( tile.Piece ) is not { } map )
			return null;
		var (positionX, positionY) = PieceInstanceOrigin( originX, originY, tile.Rotation );
		return ObjectHeightMaps.CellIndex( map, (360 - tile.Rotation) % 360, positionX, positionY, cellX, cellY ) is { } index
			? map.CellMaxima.Span[index] * ObjectHeightMaps.ByteScale + TileHeight( originX, originY ) / ObjectPlacement.ModelScale
			: null;
	}

	/// <summary>The instance position of a piece on the map's two ground axes, in model units: the origin cell plus two cells of the engine rotation's offset.</summary>
	internal static (int X, int Y) PieceInstanceOrigin( int x, int y, int rotation )
	{
		(int offsetX, int offsetY) = ((360 - rotation) % 360) switch { 90 => (0, 1), 180 => (1, 1), 270 => (1, 0), _ => (0, 0) };
		return ((x + 2 * offsetX) * 10, (y + 2 * offsetY) * 10);
	}

	private void UpdateVehicles( TrackCellLayer layer, IEnumerable<BumpController> controllers )
	{
		// one reused list: the vehicles of every controller in order, without a new array per update
		wanted.Clear();
		foreach ( var controller in controllers )
		{
			foreach ( var vehicle in controller.Vehicles )
				wanted.Add( (controller, vehicle) );
		}
		while ( visuals.Count > wanted.Count )
		{
			Delete( visuals[^1] );
			visuals.RemoveAt( visuals.Count - 1 );
		}
		while ( visuals.Count < wanted.Count )
			visuals.Add( new VehicleVisual() );
		probeLayer = layer;
		var origin = gridOrigin();
		for ( var index = 0; index < wanted.Count; index++ )
		{
			var (controller, vehicle) = wanted[index];
			var visual = visuals[index];
			// a bumper car is posed like a kart: the drawn heading follows its motion (the original's +0x54), the four probes read the ride object (its platform above the terrain)
			var kart = !controller.Template.IsWater;
			var hover = controller.Template.IsKart && (vehicle.Flags & BumpVehicle.Hover) != 0;
			// the shadow instance is the ride's mesh 0: the hovering karts' gk_shadow, the jungle bumper cars' wake (flag 0x2000000)
			var shadow = controller.Template.IsKart ? (vehicle.Flags & BumpVehicle.Shadow) != 0 : controller.Template.IsBumper && (vehicle.Flags & BumpVehicle.Wake) != 0;
			if ( visual.Variant != vehicle.Variant || visual.HasShadow != shadow )
				Rebuild( visual, vehicle, shadow );
			if ( visual.Body.Count == 0 )
				continue;
			var (x, y) = (vehicle.X / UnitsPerModelUnit, vehicle.Y / UnitsPerModelUnit);
			var radius = vehicle.Radius / UnitsPerModelUnit;
			probeFallback = vehicle.Height;
			Matrix4x4 transform;
			if ( kart )
			{
				// a kart is drawn along the direction it moves (the drawn heading follows the velocity), the original's +0x54
				var pose = TrackSurface.KartPose( x, y, vehicle.VisualHeading, radius, hover, (vehicle.Flags & BumpVehicle.Bank) != 0, vehicle.Age, vehicle.YawRate,
					probe );
				vehicle.Height = pose.Height;
				transform = Matrix4x4.CreateRotationX( pose.Pitch ) * Matrix4x4.CreateRotationY( -pose.Roll )
					* Matrix4x4.CreateRotationZ( -vehicle.VisualHeading * 2 * MathF.PI / BumpMath.Turn )
					* Matrix4x4.CreateTranslation( x, y, pose.Height )
					* Matrix4x4.CreateScale( ObjectPlacement.ModelScale )
					* Matrix4x4.CreateTranslation( origin.X, origin.Y, 0 );
				if ( visual.Shadow.Count > 0 && controller.Template.IsBumper )
				{
					// [BIN:TPW-EXE:0x00546280 wake] flag 0x2000000: the second instance (mesh 0, b_wake) trails the car by (sine[h] × speed × 4) / 49143 and (sine[h + 128] × speed × 4) / 49143
					// model units (0x764CD8) at the four-probe height + 0.7, turned by the car's drawn heading only (angles 0, 0, h)
					Place( visual.Shadow, WakeTransform( x, y, pose.Height, vehicle.VisualHeading, vehicle.Speed, origin ) );
				}
				else if ( visual.Shadow.Count > 0 )
				{
					// the shadow lies on the ground under the kart and follows its slope, not its hover or bank
					var ground = Matrix4x4.CreateRotationX( pose.Pitch ) * Matrix4x4.CreateRotationZ( -vehicle.VisualHeading * 2 * MathF.PI / BumpMath.Turn )
						* Matrix4x4.CreateTranslation( x, y, pose.ShadowHeight )
						* Matrix4x4.CreateScale( ObjectPlacement.ModelScale )
						* Matrix4x4.CreateTranslation( origin.X, origin.Y, 0 );
					Place( visual.Shadow, ground );
				}
			}
			else
			{
				var pose = TrackSurface.Pose( x, y, vehicle.Heading, radius, probe );
				// the boat keeps its height (+0xA0) for the next probe that finds no surface, as the karts do
				vehicle.Height = pose.Height;
				transform = Matrix4x4.CreateRotationX( pose.Pitch ) * Matrix4x4.CreateRotationY( -pose.Roll )
					* Matrix4x4.CreateRotationZ( -vehicle.Heading * 2 * MathF.PI / BumpMath.Turn )
					* Matrix4x4.CreateTranslation( x, y, pose.Height )
					* Matrix4x4.CreateScale( ObjectPlacement.ModelScale )
					* Matrix4x4.CreateTranslation( origin.X, origin.Y, 0 );
			}
			visual.Transform = transform;
			// the break smoke follows the vehicle's sparks node: number 1 on a kart, 2 on a bumper car (water boats have none)
			var sparks = controller.Template.IsKart ? 1 : controller.Template.IsBumper ? 2 : 0;
			vehicle.SparksNode = sparks > 0 && VehicleMesh( vehicle.Variant ) is { SparksNodes: var nodes } && nodes[sparks] is { } rest
				? System.Numerics.Vector3.Transform( System.Numerics.Vector3.Zero, ObjectPlacement.SwapAxes( rest ) * transform )
				: null;
			vehicle.SeatHeads = SeatHeadFlags( vehicle.Variant );
			visual.Vehicle = vehicle;
			visual.Controller = controller;
			visual.Hover = hover;
			Place( visual.Body, transform );
			// a vehicle in the teleport is hidden with its shadow (0x00548E80)
			var hidden = (vehicle.Flags & BumpVehicle.Hidden) != 0;
			foreach ( var entity in visual.Body )
				entity.Hidden = hidden;
			foreach ( var entity in visual.Shadow )
				entity.Hidden = hidden;
		}
	}

	/// <summary>The divisor of the jungle bumper car's wake offset (the float at 0x764CD8).</summary>
	public const float WakeDivisor = 49143f;
	/// <summary>The wake's height above the car's four-probe mean, in model units.</summary>
	public const float WakeHeight = 0.7f;

	/// <summary>The jungle bumper car's wake (flag 0x2000000): behind the car by its speed along the drawn heading, 0.7 above its height, level, turned by the heading.</summary>
	public static Matrix4x4 WakeTransform( float x, float y, float height, int heading, int speed, Vector2 origin )
	{
		var wakeX = x - (BumpMath.Sin( heading ) * speed * 4) / WakeDivisor;
		var wakeY = y - (BumpMath.Cos( heading ) * speed * 4) / WakeDivisor;
		return Matrix4x4.CreateRotationZ( -heading * 2 * MathF.PI / BumpMath.Turn )
			* Matrix4x4.CreateTranslation( wakeX, wakeY, height + WakeHeight )
			* Matrix4x4.CreateScale( ObjectPlacement.ModelScale )
			* Matrix4x4.CreateTranslation( origin.X, origin.Y, 0 );
	}

	private static void Place( List<ModelEntity> entities, Matrix4x4 transform )
	{
		foreach ( var entity in entities )
		{
			entity.TransformOverride = transform;
			entity.Position = new Vector3( transform.M41, transform.M42, transform.M43 );
		}
	}

	private void Rebuild( VehicleVisual visual, BumpVehicle vehicle, bool shadow )
	{
		Delete( visual );
		visual.Variant = vehicle.Variant;
		visual.HasShadow = shadow;
		if ( VehicleMesh( vehicle.Variant ) is { } body )
			visual.Body = body.Models.Select( model => new ModelEntity { Model = model, Name = "vehicle" } ).ToList();
		// the shadow instance is the ride's mesh 0 (gk_shadow) in the space and fantasy rides
		if ( shadow && VehicleMesh( 0 ) is { } shadowMesh )
			visual.Shadow = shadowMesh.Models.Select( model => new ModelEntity { Model = model, Name = "kart shadow" } ).ToList();
	}

	private static void Delete( VehicleVisual visual )
	{
		foreach ( var entity in visual.Body.Concat( visual.Shadow ) )
			entity.Delete();
		visual.Body = new List<ModelEntity>();
		visual.Shadow = new List<ModelEntity>();
		visual.Variant = -1;
	}

	/// <summary>
	/// The rider heads of the vehicles: each rider hangs from the vehicle mesh's head dummy of its seat (class 0x100000B1: number 1, and 2 in the Halloween karts; 1 to 5 in the
	/// water rides' <c>wr_ring</c> boats).
	/// </summary>
	// [BIN:TPW-EXE:0x0044B410 head draw] the seat's dummy node is found by class 0x80 and its number (0x0044B220); each rider is drawn as the guest's head sprite at that node (0x00475A10), the ADDHEAD mechanism
	// [BIN:TPW-EXE:0x00549C60 seat order] 0x549C60 walks the joining list the vehicle holds (vehicle +0x30, the newest joiner first because the join 0x54AA80 puts a guest at the front) and numbers its nodes 1 to n (node +0xC), looks up the head dummy n of class 0x80 (0x44B220) and hangs the guest's head there (0x44B410, which marks the head record and has 0x475A10 create the head thing, drawn by the shared thing renderer, sizes under the HEADS entries); a node with no dummy keeps head node -1 and is counted all the same (controller +0x60)
	// [BIN:TPW-EXE:0x0054AA80 joining node] a guest on the joining list is not drawn: the join writes the node {guest, ride, head node -1} (0x54AB11..0x54AB16) and no head hangs before the vehicle takes the list, at its creation (0x549DB0 calls 0x549C60 with the beam flag 0, 0x549FF5) or its load (0x549B80 with 1); the other callers of 0x44B410 are the coaster boarding (0x43B70D), the vehicle's show (0x54900C), the script's ADDHEAD (0x554D13, 0x557EB9) and the flyers (0x55D654). (TRACK-026 retired.)
	internal void CollectHeads( List<HeadSource> sources, int? onlyAttraction )
	{
		foreach ( var visual in visuals )
		{
			if ( visual.Vehicle is not { } vehicle || visual.Controller is not { AttractionId: >= 0 } controller || onlyAttraction is { } only && only != controller.AttractionId
				|| VehicleMesh( vehicle.Variant ) is not { } geometry )
				continue;
			// a hidden vehicle's riders are taken down with it (0x00548E80 → 0x0044B4C0) and hung again when it shows
			if ( (vehicle.Flags & BumpVehicle.Hidden) != 0 )
				continue;
			for ( var seat = 0; seat < vehicle.Riders.Count && seat < geometry.HeadNodes.Length; seat++ )
			{
				if ( geometry.HeadNodes[seat] is not { } rest )
					continue;
				var node = ObjectPlacement.SwapAxes( rest ) * visual.Transform;
				sources.Add( new HeadSource( vehicle.Riders[seat], controller.AttractionId, ObjectPlacement.SwapAxes( node ), geometry.HeadFlags[seat] ) );
			}
		}
	}

	/// <summary>
	/// The height of the surface a vehicle runs on at a model-unit point, as an absolute height in model units, or null when the sampler finds no surface there (the probe
	/// keeps the vehicle's last height): the object on the probed cell is sampled as the original's <c>0x00450EA0</c> chooses (<see cref="TrackSurface.FromObject"/>): the
	/// flat pieces and the bumper rides as the ground plus their header's +0xA0, the hill pieces by their surface faces. A cell whose track tile has no piece instance (a station tile, an
	/// add-on's tile) or that holds no track gives its owner object, sampled in that object's own mode, and no object leaves the probe where it was.
	/// </summary>
	// [BIN:TPW-EXE:0x00546280 vehicle height] four probes at ± the vehicle radius along the facing and across it; each probe's cell object (0x00450AC0: the cell's owner, a track cell through 0x0053BF30) is selected (0x00450EA0) and sampled (0x004511A0); a −100 sample keeps the vehicle's previous height (+0xA0); the mean is the height, the front/back and right/left differences the pitch and roll
	// [BIN:TPW-EXE:0x00546280 probe without object] a probe whose cell has no object (0x450AC0 answers 0) is never sampled: its height slot holds the vehicle's own +0xA0 from the probe setup (0x5462F8 onward, stored before the lookup), so it adds the previous height to the mean
	internal float? SurfaceHeight( TrackCellLayer layer, float x, float y )
	{
		var cellX = Math.Clamp( (int)MathF.Floor( x / 10 ), 0, layer.Width - 1 );
		var cellY = Math.Clamp( (int)MathF.Floor( y / 10 ), 0, layer.Height - 1 );
		float Ground( int gx, int gy ) => groundHeight( Math.Clamp( gx, 0, layer.Width - 1 ), Math.Clamp( gy, 0, layer.Height - 1 ) ) / ObjectPlacement.ModelScale;
		// [BIN:TPW-EXE:0x004527F0 landscape sampler] the terrain at the probe is the landscape's triangle height at the point, 0 off the map (0x4511A0 adds the object's +0xA0)
		float Terrain() => terrainHeight != null ? terrainHeight( x, y ) / ObjectPlacement.ModelScale : Ground( cellX, cellY );
		if ( kit == null )
		{
			// a bumper ride: the cars stand on the ride object, sampled like a piece (the jungle pot's platform at +29.8, the castle's at +15)
			return RideSurface() is { Mode: TrackSamplerMode.Terrain, TerrainOffset: { } platform } ? Terrain() + platform : Ground( cellX, cellY );
		}
		var cell = layer.Get( cellX, cellY );
		if ( cell.Type == 0 )
			return ObjectHeight( cellX, cellY, x, y, Terrain );
		var (ox, oy) = cell.IsExtension ? layer.CellOf( cell.Parent ) : (cellX, cellY);
		var tile = layer.Get( ox, oy );
		// [BIN:TPW-EXE:0x00452A60 no piece instance] a track cell whose tile has no piece instance gives the cell's owner thing (0x00527E80), and an object (thing type 3) is sampled through its own instance +0x20: the add-on's tile, so the vehicles drive on the add-on's model, and the station tiles, which never get a piece (0x5387BC skips 0x53AFA0 for types 0xD and 0x12), so they stand on the ride's own model
		if ( TrackCellTypes.IsStation( tile.Type ) || (tile.Flags & TrackCell.FlagAddOn) != 0 )
			return ObjectHeight( cellX, cellY, x, y, Terrain );
		var baseHeight = TileHeight( ox, oy ) / ObjectPlacement.ModelScale;
		if ( tile.Piece != 0 && GeometryOf( tile.Piece ) is { } geometry )
		{
			// the tile turned counter-clockwise by its rotation: undo it about the tile's centre to read the piece's own faces
			var (dx, dy) = (x - (ox * 10 + 10), y - (oy * 10 + 10));
			var angle = tile.Rotation * MathF.PI / 180;
			var (lx, ly) = (10 + dx * MathF.Cos( angle ) + dy * MathF.Sin( angle ), 10 - dx * MathF.Sin( angle ) + dy * MathF.Cos( angle ));
			// [BIN:TPW-EXE:0x004511A0 sample] mode 1 (every piece without a surface record: the loader's header bit 0x80): the landscape under the probe + header +0xA0, not the tile's instance height
			if ( geometry.Surface is { Mode: TrackSamplerMode.Terrain, TerrainOffset: { } offset } )
				return Terrain() + offset;
			// mode 2: the faces of the tile's draped copy of the piece, on the tile's instance height
			var surface = DrapeOf( ox, oy, tile.Piece, tile.Rotation, geometry ).Surface;
			return surface.HeightAt( lx, ly ) is { } height ? baseHeight + height : null;
		}
		return Terrain();
	}

	/// <summary>
	/// The height an object on a cell gives a probe at (<paramref name="x"/>, <paramref name="y"/>) in its own sampler mode (<c>0x450EA0</c>, <c>0x4511A0</c>): the landscape plus its header's
	/// +0xA0 in the terrain mode, else its faces above its base; null when no object stands on the cell or its faces do not cover the point.
	/// </summary>
	private float? ObjectHeight( int cellX, int cellY, float x, float y, Func<float> terrain )
	{
		if ( ObjectSurfaceAt?.Invoke( cellX, cellY ) is not { } owner )
			return null;
		// the object's own sampler (0x00450EA0): the landscape at the probe plus its header's +0xA0, or its surface faces above its base
		if ( owner.Surface is { Mode: TrackSamplerMode.Terrain, TerrainOffset: { } offset } )
			return terrain() + offset;
		var local = System.Numerics.Vector3.Transform( new System.Numerics.Vector3( x, y, 0 ), owner.GridToModel );
		return owner.Surface.HeightAt( local.X, local.Y ) is { } height ? owner.BaseHeight + height : null;
	}

	/// <summary>The sampler surface of the ride's own model (the station of a kart or water ride, the arena of a bumper ride), or null when it cannot be read.</summary>
	private TrackSurface? RideSurface()
	{
		if ( rideSurface != null || rideSurfaceFailed )
			return rideSurface;
		try
		{
			rideSurface = TrackSurface.FromObject( ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath ) );
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
		{
			rideSurfaceFailed = true;
			Log?.Warning( $"The track ride's model {entry.ModelPath} could not be sampled: {exception.Message}" );
		}
		return rideSurface;
	}

	// ---- Geometry ----------------------------------------------------------------------------------

	private PieceGeometry? GeometryOf( int piece )
	{
		if ( pieces.TryGetValue( piece, out var cached ) )
			return cached;
		PieceGeometry? geometry = null;
		if ( kit?.GetModel( piece ) is { } model )
		{
			geometry = Build( model, entry, TrackSurface.FromObject( model, drapeLoaded: true ), TrackPieceKit.ArmMaskedNodes( model, kit.Entries[piece].ArmMask ) );
			if ( kit.GetAnimation( piece, TrackPieceKit.WaterAnimation ) is { } water && PlayableUvMeshes( model, water.Clip, geometry ) is { } uvMeshes )
				geometry = geometry with { Water = water, UvMeshes = uvMeshes };
		}
		pieces[piece] = geometry!;
		return geometry;
	}

	private readonly Dictionary<int, bool[]?> seatHeadFlags = new();

	/// <summary>Which seats of a variant's mesh have a head dummy, cached per variant (the head dummies are fixed on the mesh), or null when the mesh is missing.</summary>
	private bool[]? SeatHeadFlags( int variant )
	{
		if ( !seatHeadFlags.TryGetValue( variant, out var flags ) )
			seatHeadFlags[variant] = flags = VehicleMesh( variant ) is { HeadNodes: var nodes } ? nodes.Select( node => node != null ).ToArray() : null;
		return flags;
	}

	/// <summary>
	/// The seat head flags of a ride's vehicle variant read from the archive's mesh without building drawables, for the controller's loading beams (null when the mesh is missing or unreadable).
	/// </summary>
	internal static bool[]? ReadSeatHeadFlags( ObjectCatalogEntry entry, int variant )
	{
		try
		{
			var path = VehicleMeshPath( entry, variant );
			if ( path == null )
				return null;
			var model = ObjectAssets.LoadModel( entry.FileSystem, path );
			return model.Kind == ModelFileKind.Geometry
				? SeatHeads( model, ModelAnimationPlayer.ComputeRestTransforms( model ) ).Nodes.Select( node => node != null ).ToArray()
				: null;
		}
		catch ( Exception )
		{
			// presentation only: an unreadable mesh must never escape into the simulation tick that asks for the beams
			return null;
		}
	}

	/// <summary>The archive path of a vehicle variant's mesh (the ride's supplemental mesh <c>n</c>, <c>wr_ring.md2</c> for variant 0), or null when the archive lacks it.</summary>
	private static string? VehicleMeshPath( ObjectCatalogEntry entry, int variant )
	{
		var name = VehicleMeshName( entry, variant );
		return name == null ? null : entry.FileSystem.GetFiles( entry.ArchivePath ).FirstOrDefault( file => string.Equals( Path.GetFileName( file ), name, StringComparison.OrdinalIgnoreCase ) );
	}

	private static string? VehicleMeshName( ObjectCatalogEntry entry, int variant ) =>
		(entry.Settings[$"SupplementalMeshes[{variant}].FileName"] ?? (variant == 0 ? "wr_ring.md2" : null))?.Trim( '"', ' ' );

	/// <summary>The mesh of a vehicle variant: the ride's supplemental mesh <c>n</c> (<c>wr_ring.md2</c>, <c>gk_blue.md2</c>, …), or null when the archive lacks it.</summary>
	private PieceGeometry? VehicleMesh( int variant )
	{
		if ( vehicleMeshes.TryGetValue( variant, out var cached ) )
			return cached;
		PieceGeometry? geometry = null;
		var name = VehicleMeshName( entry, variant );
		try
		{
			var path = VehicleMeshPath( entry, variant );
			if ( path != null )
			{
				var model = ObjectAssets.LoadModel( entry.FileSystem, path );
				if ( model.Kind == ModelFileKind.Geometry )
					geometry = Build( model, entry, null );
			}
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
		{
			Log?.Warning( $"The track ride's vehicle mesh {name} could not be read: {exception.Message}" );
		}
		vehicleMeshes[variant] = geometry;
		return geometry;
	}

	/// <summary>
	/// The meshes whose texture coordinates <paramref name="clip"/> moves and whose drawn parts keep one vertex per corner (the copies are rewritten corner by
	/// corner), or null when the clip moves none or the animator cannot bind the mesh.
	/// </summary>
	// [DATA:levels/hallow/rides/gokarts gk_trckH*m.MD2] only texture-coordinate tracks are drawn, which is all the shipped piece clips need: the water clips and the space kart clips carry texture-coordinate tracks, the Crypt Karts' bridge clips (gk_trckH, _u, _d and _a with their m members) carry no node track and one texture-frame track on slot 0 (h_trkunder.tga, a slot of one frame) whose 20 keys all name frame 0, so the clip update (0xa56f8) rewrites the frame the slot already shows, and no other member of the archive animates the flame slot 7 (fire0.tga..flame2.tga); TrackPieceClipDataTests
	private static bool[]? PlayableUvMeshes( ModelFile model, ModelAnimation clip, PieceGeometry geometry )
	{
		var moved = new bool[model.Meshes.Count];
		foreach ( var track in clip.Tracks.Where( track => track.UvAnimation != null ) )
		{
			for ( var mesh = 0; mesh < model.Meshes.Count; mesh++ )
			{
				if ( model.Meshes[mesh].NodeIndex == track.NodeIndex )
					moved[mesh] = true;
			}
		}
		foreach ( var part in geometry.Parts.Where( part => moved[part.Mesh] && part.Vertices.Length != model.Meshes[part.Mesh].CornerPositionIndices.Length ) )
			moved[part.Mesh] = false;
		if ( !moved.Any( value => value ) )
			return null;
		try
		{
			_ = new ObjectAnimator( model );
		}
		catch ( ArgumentException )
		{
			// two meshes on one node: the animator cannot bind the piece, which then stays static
			return null;
		}
		return moved;
	}

	/// <summary>
	/// Bakes every mesh of a piece with its rest transform into one part per mesh part (its drawable model is made on first use), leaving out the meshes on <paramref name="hidden"/>
	/// nodes (the end caps the piece's arm mask hides).
	/// </summary>
	private static PieceGeometry Build( ModelFile model, ObjectCatalogEntry entry, TrackSurface? surface, IReadOnlySet<int>? hidden = null )
	{
		var rest = ModelAnimationPlayer.ComputeRestTransforms( model );
		var parts = new List<PiecePart>();
		for ( var meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++ )
		{
			var mesh = model.Meshes[meshIndex];
			if ( ObjectAssets.GetUnrenderableReason( mesh ) != null && ObjectAssets.GetUnrenderableReason( mesh ) != "more than 16 texture slots" )
				continue;
			var node = mesh.NodeIndex >= 0 && mesh.NodeIndex < model.Nodes.Count ? mesh.NodeIndex : model.RootNodeIndex;
			if ( hidden != null && node >= 0 && hidden.Contains( node ) )
				continue;
			var bake = node >= 0 ? rest[node] : Matrix4x4.Identity;
			foreach ( var (vertices, indices, textures, _) in ObjectAssets.ConvertMeshParts( mesh, bake ) )
				parts.Add( new PiecePart( entry, meshIndex, vertices, indices, textures ) );
		}
		var (heads, flags) = SeatHeads( model, rest );
		var sparks = new Matrix4x4?[3];
		for ( var number = 1; number <= 2; number++ )
		{
			var node = SparksNode( model, number );
			sparks[number] = node >= 0 && node < rest.Length ? rest[node] : null;
		}
		return new PieceGeometry( surface ?? TrackSurface.FromObject( model ), heads, flags, sparks ) { Parts = parts, Mesh = model };
	}

	/// <summary>
	/// The rest transforms and node flags of a vehicle mesh's rider head dummies by seat: seat k - 1 holds the head of rider k, null where the model has no usable record for it.
	/// </summary>
	// [BIN:TPW-EXE:0x00549C60 seat heads] the vehicle creation 0x00549DB0 hangs rider k of the car's rider list (k = 1, 2, ...) on the dummy with flag 0x80 and number k (0x0044B220); a rider
	// whose number has no usable record gets no head. Every vehicle family takes this path: the karts carry heads 1 and 2, the water rides' wr_ring boats 1 to 5
	internal static (Matrix4x4?[] Nodes, uint[] Flags) SeatHeads( ModelFile model, Matrix4x4[] rest )
	{
		var last = model.DummyAttributes.Where( attribute => (attribute.Type & RideHeadSpace.HeadFlag) != 0 ).Select( attribute => (int)Math.Min( attribute.Value, 64u ) ).DefaultIfEmpty( 0 ).Max();
		var nodes = new Matrix4x4?[last];
		var flags = new uint[last];
		for ( var id = 1; id <= last; id++ )
		{
			var node = RideHeadSpace.NodeForHead( model, id );
			if ( node < 0 || node >= rest.Length )
				continue;
			nodes[id - 1] = rest[node];
			flags[id - 1] = model.Nodes[node].Type;
		}
		return (nodes, flags);
	}

	/// <summary>The model node of a vehicle's sparks dummy: the first dummy record with class bit 0x100 and the number, or −1.</summary>
	// [BIN:TPW-EXE:0x0044B220 dummy by class] the first dummy record (20 bytes at +0x7C, +0x48 of them) whose number (+4) matches and whose class word has a requested bit; the vehicle creation 0x54A040 asks class 0x100 number 1 for a kart (0x54A6D2) and number 2 for a bumper car (0x54A1C8) and keeps the index in +0x24
	internal static int SparksNode( ModelFile model, int number )
	{
		for ( var index = 0; index < model.DummyAttributes.Count; index++ )
		{
			var attribute = model.DummyAttributes[index];
			if ( (attribute.Type & 0x100) == 0 || attribute.Value != (uint)number )
				continue;
			var node = model.Counts.Unknown46 + index;
			return node < model.Nodes.Count ? node : -1;
		}
		return -1;
	}

	/// <summary>The texture of a model material or track texture of an object's archive (the textures, gtexture and theme sharetex directories), or <see cref="Texture.Missing"/>.</summary>
	internal static Texture LoadTexture( ObjectCatalogEntry entry, string name )
	{
		var resolved = ObjectAssets.ResolveTexture( entry, name );
		if ( resolved == null )
			return Texture.Missing;
		var (fileSystem, path) = resolved.Value;
		if ( fileSystem == FileSystem )
			return new Texture( path, TextureFlags.Repeat );
		return Texture.FromFileSystem( fileSystem, path, TextureFlags.Repeat );
	}

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		foreach ( var entity in tiles.Concat( visuals.SelectMany( visual => visual.Body.Concat( visual.Shadow ) ) ) )
			entity.Delete();
		tiles.Clear();
		visuals.Clear();
		foreach ( var group in clipGroups.Values )
			ReleaseGroup( group );
		clipGroups.Clear();
		foreach ( var drape in drapes.Values.Concat( retired ) )
			drape.ReleaseModels();
		drapes.Clear();
		retired.Clear();
		foreach ( var geometry in pieces.Values.Concat( vehicleMeshes.Values ).Where( item => item != null ) )
		{
			foreach ( var part in geometry!.Parts )
				part.Release();
		}
		pieces.Clear();
		vehicleMeshes.Clear();
		seatHeadFlags.Clear();
	}
}
