using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Draws the track tiles and the vehicles of one track family of a level (docs/TRACKS.md): each track tile is the piece mesh its cell chose, instanced at the
/// tile's origin with the engine rotation 360 − rotation (the pieces are 20 world units wide), and each vehicle is one of the ride's supplemental meshes
/// (<c>wr_ring.md2</c> for the boats, <c>gk_*.md2</c> for the karts) at the vehicle position divided by 307.2. The meshes are baked with their rest
/// transforms and drawn as static parts; the water texture animation and the arm masks of tee pieces are not reproduced (TRACK-018). A kart's riders are
/// drawn by the ride-head renderer at the kart's head dummies (<see cref="CollectHeads"/>).
/// </summary>
public sealed class TrackRenderer : IDisposable
{
	private const string TextureArrayName = "Color";
	/// <summary>Section units per MD2 unit: one cell is 0xC00 = 10 × 307.2.</summary>
	public const float UnitsPerModelUnit = 307.2f;

	/// <summary>A drawable mesh set with the surface it provides and, for a vehicle mesh, the rest transforms and flags of its rider head dummies (head 1, head 2).</summary>
	private sealed record PieceGeometry( List<Model> Models, TrackSurface Surface, Matrix4x4[] HeadNodes, uint[] HeadFlags, Matrix4x4?[] SparksNodes );

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
	private readonly Dictionary<int, PieceGeometry> pieces = new();
	private readonly Dictionary<int, PieceGeometry?> vehicleMeshes = new();
	private readonly List<ModelEntity> tiles = new();
	private readonly List<VehicleVisual> visuals = new();
	private readonly List<(BumpController Controller, BumpVehicle Vehicle)> wanted = new();
	private float? kartStationHeight;
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
	public TrackRenderer( ObjectCatalogEntry entry, TrackPieceKit? kit, Func<Vector2> gridOrigin, Func<int, int, float> groundHeight )
	{
		this.entry = entry;
		this.kit = kit;
		this.gridOrigin = gridOrigin;
		this.groundHeight = groundHeight;
		// a probe the sampler finds no surface for keeps the vehicle's last height (0x00546280: a −100 sample takes +0xA0)
		probe = ( x, y ) => SurfaceHeight( probeLayer!, x, y ) ?? probeFallback;
	}

	public TrackFamily Family => kit?.Family ?? TrackFamily.Bumper;
	/// <summary>Tile instances drawn after the last <see cref="Update"/>.</summary>
	public int TileCount => tiles.Count;
	/// <summary>Vehicle instances drawn after the last <see cref="Update"/>.</summary>
	public int BoatCount => visuals.Count;
	/// <summary>Shadow instances of hovering karts after the last <see cref="Update"/>.</summary>
	public int ShadowCount => visuals.Count( visual => visual.Shadow.Count > 0 );

	/// <summary>Rebuilds the tile instances when the layer changed and moves a vehicle instance to every vehicle of <paramref name="controllers"/>.</summary>
	public void Update( TrackCellLayer layer, IEnumerable<BumpController> controllers )
	{
		if ( disposed )
			return;
		if ( layer.Version != version )
		{
			version = layer.Version;
			RebuildTiles( layer );
		}
		UpdateVehicles( layer, controllers );
	}

	// [APPROX:TRACK-018] the pieces are static: the water texture animation (BUMP 17, animation 5 or 12 on channel 7) is stored on the controller but not played, and the tee pieces show all four arms instead of the arm mask the instancer passes — evidence needed: the instancer 0x0005A4DC and 0x00059F00 and the piece animation clips (wr_trck*m.md2, gk_trck*m.md2)
	private void RebuildTiles( TrackCellLayer layer )
	{
		foreach ( var entity in tiles )
			entity.Delete();
		tiles.Clear();
		if ( kit == null )
			return;
		foreach ( var (x, y, cell) in layer.Cells() )
		{
			if ( !cell.IsOrigin || cell.Piece == 0 || cell.Family != (int)kit.Family )
				continue;
			var geometry = GeometryOf( cell.Piece );
			if ( geometry == null )
				continue;
			var transform = TileTransform( x, y, cell.Rotation );
			foreach ( var model in geometry.Models )
				tiles.Add( new ModelEntity { Model = model, Name = $"track:{x},{y}", TransformOverride = transform, Position = new Vector3( transform.M41, transform.M42, transform.M43 ) } );
		}
	}

	/// <summary>
	/// The matrix of the tile whose origin cell is (<paramref name="x"/>, <paramref name="y"/>): the piece turns counter-clockwise by
	/// <paramref name="rotation"/> degrees about its centre (the original's engine rotation is 360 − rotation), then moves to the tile and the ground.
	/// </summary>
	// [BIN:STP-PPC:0x1008B610 piece instancing] the instance sits at the origin cell plus an offset by rotation (90°: y+1; 180°: x+1, y+1; 270°: x+1) with engine rotation 360 − rotation, skipped for flag 0x10 or piece 0
	// [APPROX:TRACK-003] the one-cell instance offset of the original is unresolved for 20-unit tiles; OpenTPW turns each tile about its own centre, which keeps every rotation inside the tile — evidence needed: the instancer 0x1008B610's PC twin (the Bumper.*Adjust keys are the station-reference offsets of TRACK-005 and TRACK-030, not a mesh pivot)
	// [APPROX:TRACK-001] a tile stands on the mean ground height of its four cells; the original's instance height for a piece is not read
	public Matrix4x4 TileTransform( int x, int y, int rotation )
	{
		var ground = (groundHeight( x, y ) + groundHeight( x + 1, y ) + groundHeight( x, y + 1 ) + groundHeight( x + 1, y + 1 )) / 4;
		var origin = gridOrigin();
		return Matrix4x4.CreateTranslation( -10, -10, 0 )
			* Matrix4x4.CreateRotationZ( rotation * MathF.PI / 180 )
			* Matrix4x4.CreateTranslation( 10 + x * 10, 10 + y * 10, 0 )
			* Matrix4x4.CreateScale( ObjectPlacement.ModelScale )
			* Matrix4x4.CreateTranslation( origin.X, origin.Y, ground );
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
			visual.Vehicle = vehicle;
			visual.Controller = controller;
			visual.Hover = hover;
			Place( visual.Body, transform );
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
	/// The rider heads of the vehicles: each rider hangs from the kart mesh's head dummy of its seat (class 0x100000B1, number 1, and 2 in the Halloween karts).
	/// </summary>
	// [BIN:TPW-EXE:0x0044B410 head draw] the seat's dummy node is found by class 0x80 and its number (0x0044B220); each rider is drawn as the guest's head sprite at that node (0x00475A10), the ADDHEAD mechanism
	// [APPROX:TRACK-026] the head sprites hang from the dummies through the ride-head renderer (seat order as traced: [BIN:TPW-EXE:0x00549C60] gives rider i of the car's rider list dummy i); the draw internals 0x44B410 and 0x475A10 and the boarding guest drawn between BUMP 1 and the car's creation are not traced
	internal void CollectHeads( List<HeadSource> sources, int? onlyAttraction )
	{
		foreach ( var visual in visuals )
		{
			if ( visual.Vehicle is not { } vehicle || visual.Controller is not { AttractionId: >= 0 } controller || onlyAttraction is { } only && only != controller.AttractionId
				|| VehicleMesh( vehicle.Variant ) is not { } geometry )
				continue;
			for ( var seat = 0; seat < vehicle.Riders.Count && seat < geometry.HeadNodes.Length; seat++ )
			{
				var node = ObjectPlacement.SwapAxes( geometry.HeadNodes[seat] ) * visual.Transform;
				sources.Add( new HeadSource( vehicle.Riders[seat], controller.AttractionId, ObjectPlacement.SwapAxes( node ), geometry.HeadFlags[seat] ) );
			}
		}
	}

	/// <summary>
	/// The height of the surface a vehicle runs on at a model-unit point, as an absolute height in model units, or null when the sampler finds no surface there (the probe
	/// keeps the vehicle's last height): the object on the probed cell is sampled as the original's <c>0x00450EA0</c> chooses (<see cref="TrackSurface.FromObject"/>): the
	/// flat pieces, the stations and the bumper rides as the ground plus their header's +0xA0, the hill pieces by their surface faces; a cell with no track is the terrain.
	/// </summary>
	// [BIN:TPW-EXE:0x00546280 vehicle height] four probes at ± the vehicle radius along the facing and across it; each probe's cell object (0x00450AC0: the cell's owner, a track cell through 0x0053BF30) is selected (0x00450EA0) and sampled (0x004511A0); a −100 sample keeps the vehicle's previous height (+0xA0); the mean is the height, the front/back and right/left differences the pitch and roll
	// [APPROX:TRACK-001] the terrain under a terrain-sampled object is the mean ground of the tile's (or bumper cell's) four corners, not the original's triangle sampler 0x004527F0 at the probe; a cell with no track gives the terrain where the original samples its owner object (or keeps the previous height without one); the kart stations whose model samples faces (Halloween, space) use the straight piece's floor
	private float? SurfaceHeight( TrackCellLayer layer, float x, float y )
	{
		var cellX = Math.Clamp( (int)MathF.Floor( x / 10 ), 0, layer.Width - 1 );
		var cellY = Math.Clamp( (int)MathF.Floor( y / 10 ), 0, layer.Height - 1 );
		float Ground( int gx, int gy ) => groundHeight( Math.Clamp( gx, 0, layer.Width - 1 ), Math.Clamp( gy, 0, layer.Height - 1 ) ) / ObjectPlacement.ModelScale;
		float Mean( int gx, int gy ) => (Ground( gx, gy ) + Ground( gx + 1, gy ) + Ground( gx, gy + 1 ) + Ground( gx + 1, gy + 1 )) / 4;
		if ( kit == null )
		{
			// a bumper ride: the cars stand on the ride object, sampled like a piece (the jungle pot's platform at +29.8, the castle's at +15)
			return RideSurface() is { Mode: TrackSamplerMode.Terrain, TerrainOffset: { } platform } ? Mean( cellX, cellY ) + platform : Ground( cellX, cellY );
		}
		var cell = layer.Get( cellX, cellY );
		if ( cell.Type == 0 )
			return Ground( cellX, cellY );
		var (ox, oy) = cell.IsExtension ? layer.CellOf( cell.Parent ) : (cellX, cellY);
		var tile = layer.Get( ox, oy );
		var baseHeight = Mean( ox, oy );
		if ( TrackCellTypes.IsStation( tile.Type ) )
		{
			if ( RideSurface() is { Mode: TrackSamplerMode.Terrain, TerrainOffset: { } floor } )
				return baseHeight + floor;
			return baseHeight + (kit.Family == TrackFamily.Kart ? KartStationHeight() : TrackSurface.StationWaterHeight);
		}
		if ( tile.Piece != 0 && GeometryOf( tile.Piece ) is { } geometry )
		{
			// the tile turned counter-clockwise by its rotation: undo it about the tile's centre to read the piece's own faces
			var (dx, dy) = (x - (ox * 10 + 10), y - (oy * 10 + 10));
			var angle = tile.Rotation * MathF.PI / 180;
			var (lx, ly) = (10 + dx * MathF.Cos( angle ) + dy * MathF.Sin( angle ), 10 - dx * MathF.Sin( angle ) + dy * MathF.Cos( angle ));
			return geometry.Surface.HeightAt( lx, ly ) is { } surface ? baseHeight + surface : null;
		}
		return Ground( cellX, cellY );
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

	/// <summary>The floor height of a kart station tile: the straight piece's floor at its centre.</summary>
	private float KartStationHeight()
	{
		kartStationHeight ??= GeometryOf( TrackPieceSelector.PieceStraight )?.Surface.HeightAt( 10, 10 ) ?? 3f;
		return kartStationHeight.Value;
	}

	// ---- Geometry ----------------------------------------------------------------------------------

	private PieceGeometry? GeometryOf( int piece )
	{
		if ( pieces.TryGetValue( piece, out var cached ) )
			return cached;
		PieceGeometry? geometry = null;
		if ( kit?.GetModel( piece ) is { } model )
		{
			geometry = Build( model, entry, TrackSurface.FromObject( model ) );
		}
		pieces[piece] = geometry!;
		return geometry;
	}

	/// <summary>The mesh of a vehicle variant: the ride's supplemental mesh <c>n</c> (<c>wr_ring.md2</c>, <c>gk_blue.md2</c>, …), or null when the archive lacks it.</summary>
	private PieceGeometry? VehicleMesh( int variant )
	{
		if ( vehicleMeshes.TryGetValue( variant, out var cached ) )
			return cached;
		PieceGeometry? geometry = null;
		var directory = entry.ArchivePath;
		var name = entry.Settings[$"SupplementalMeshes[{variant}].FileName"] ?? (variant == 0 ? "wr_ring.md2" : null);
		try
		{
			var path = name == null ? null : entry.FileSystem.GetFiles( directory ).FirstOrDefault( file => string.Equals( Path.GetFileName( file ), name.Trim( '"', ' ' ), StringComparison.OrdinalIgnoreCase ) );
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

	/// <summary>Bakes every mesh of a piece with its rest transform and builds one drawable model per mesh part.</summary>
	private static PieceGeometry Build( ModelFile model, ObjectCatalogEntry entry, TrackSurface? surface )
	{
		var rest = ModelAnimationPlayer.ComputeRestTransforms( model );
		var models = new List<Model>();
		foreach ( var mesh in model.Meshes )
		{
			if ( ObjectAssets.GetUnrenderableReason( mesh ) != null && ObjectAssets.GetUnrenderableReason( mesh ) != "more than 16 texture slots" )
				continue;
			var node = mesh.NodeIndex >= 0 && mesh.NodeIndex < model.Nodes.Count ? mesh.NodeIndex : model.RootNodeIndex;
			var bake = node >= 0 ? rest[node] : Matrix4x4.Identity;
			foreach ( var (vertices, indices, textures, _) in ObjectAssets.ConvertMeshParts( mesh, bake ) )
			{
				var slots = new Texture[16];
				Array.Fill( slots, Texture.Missing );
				for ( var index = 0; index < textures.Length; index++ )
					slots[index] = LoadTexture( entry, textures[index] );
				var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
				material.Set( TextureArrayName, slots );
				models.Add( new Model( vertices, indices, material, false ) );
			}
		}
		var heads = new List<Matrix4x4>();
		var flags = new List<uint>();
		for ( var id = 1; id <= 2; id++ )
		{
			var node = RideHeadSpace.NodeForHead( model, id );
			if ( node < 0 || node >= rest.Length )
				break;
			heads.Add( rest[node] );
			flags.Add( model.Nodes[node].Type );
		}
		var sparks = new Matrix4x4?[3];
		for ( var number = 1; number <= 2; number++ )
		{
			var node = SparksNode( model, number );
			sparks[number] = node >= 0 && node < rest.Length ? rest[node] : null;
		}
		return new PieceGeometry( models, surface ?? TrackSurface.FromObject( model ), heads.ToArray(), flags.ToArray(), sparks );
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
		using var stream = fileSystem.OpenRead( path );
		var data = new TextureFile( stream ).Data;
		return new Texture( data.Data, data.Width, data.Height, TextureFlags.Repeat );
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
		foreach ( var geometry in pieces.Values.Concat( vehicleMeshes.Values ).Where( item => item != null ) )
		{
			foreach ( var model in geometry!.Models )
			{
				model.ReleaseBuffers();
				model.Material.ReleaseOwnedResources();
			}
		}
		pieces.Clear();
		vehicleMeshes.Clear();
	}
}
