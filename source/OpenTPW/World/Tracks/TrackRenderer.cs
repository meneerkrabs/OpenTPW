using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Draws the water track tiles and the boats of a level (docs/TRACKS.md): each track tile is the piece mesh its cell chose, instanced at the tile's
/// origin with the engine rotation 360 − rotation (the pieces are 20 world units wide), and each boat is the ride's supplemental mesh
/// (<c>wr_ring.md2</c>) at the vehicle position divided by 307.2. The meshes are baked with their rest transforms and drawn as static parts; the
/// water texture animation and the arm masks of tee pieces are not reproduced (TRACK-018).
/// </summary>
public sealed class TrackRenderer : IDisposable
{
	private const string TextureArrayName = "Color";
	/// <summary>Section units per MD2 unit: one cell is 0xC00 = 10 × 307.2.</summary>
	public const float UnitsPerModelUnit = 307.2f;

	private sealed record PieceGeometry( List<Model> Models, TrackSurface Surface );

	private readonly ObjectCatalogEntry entry;
	private readonly TrackPieceKit kit;
	private readonly Func<Vector2> gridOrigin;
	private readonly Func<int, int, float> groundHeight;
	private readonly Dictionary<int, PieceGeometry> pieces = new();
	private readonly List<ModelEntity> tiles = new();
	private readonly List<List<ModelEntity>> boats = new();
	private PieceGeometry? boat;
	private int version = -1;
	private bool disposed;

	/// <param name="entry">The water ride whose archive holds the pieces and textures.</param>
	/// <param name="gridOrigin">Engine position of grid corner (0, 0).</param>
	/// <param name="groundHeight">Engine height of the ground at a cell.</param>
	public TrackRenderer( ObjectCatalogEntry entry, TrackPieceKit kit, Func<Vector2> gridOrigin, Func<int, int, float> groundHeight )
	{
		this.entry = entry;
		this.kit = kit;
		this.gridOrigin = gridOrigin;
		this.groundHeight = groundHeight;
	}

	/// <summary>Tile instances drawn after the last <see cref="Update"/>.</summary>
	public int TileCount => tiles.Count;
	/// <summary>Boat instances drawn after the last <see cref="Update"/>.</summary>
	public int BoatCount => boats.Count;

	/// <summary>Rebuilds the tile instances when the layer changed and moves a boat instance to every vehicle of <paramref name="controllers"/>.</summary>
	public void Update( TrackCellLayer layer, IEnumerable<BumpController> controllers )
	{
		if ( disposed )
			return;
		if ( layer.Version != version )
		{
			version = layer.Version;
			RebuildTiles( layer );
		}
		UpdateBoats( layer, controllers );
	}

	// [APPROX:TRACK-018] the pieces are static: the water texture animation (BUMP 17, animation 5 or 12 on channel 7) is stored on the controller but not played, and the tee pieces show all four arms instead of the arm mask the instancer passes — evidence needed: the instancer 0x0005A4DC and 0x00059F00 and the piece animation clips (wr_trck*m.md2)
	private void RebuildTiles( TrackCellLayer layer )
	{
		foreach ( var entity in tiles )
			entity.Delete();
		tiles.Clear();
		foreach ( var (x, y, cell) in layer.Cells() )
		{
			if ( !cell.IsOrigin || cell.Piece == 0 || cell.Family != (int)TrackFamily.Water )
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
	// [APPROX:TRACK-003] the one-cell instance offset of the original is unresolved for 20-unit tiles; OpenTPW turns each tile about its own centre, which keeps every rotation inside the tile — evidence needed: the instancer 0x1008B610 and the mesh pivot of the pieces (Bumper.NorthXAdjust and its siblings are read but unused)
	// [APPROX:TRACK-001] a tile stands on the mean ground height of its four cells (the height profile .hmp and the original's height source are not decoded)
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

	private void UpdateBoats( TrackCellLayer layer, IEnumerable<BumpController> controllers )
	{
		var wanted = controllers.SelectMany( controller => controller.Vehicles ).ToArray();
		while ( boats.Count > wanted.Length )
		{
			foreach ( var entity in boats[^1] )
				entity.Delete();
			boats.RemoveAt( boats.Count - 1 );
		}
		if ( wanted.Length > 0 && boat == null )
			boat = BuildBoat();
		if ( boat == null )
			return;
		while ( boats.Count < wanted.Length )
			boats.Add( boat.Models.Select( model => new ModelEntity { Model = model, Name = "boat" } ).ToList() );
		var origin = gridOrigin();
		for ( var index = 0; index < wanted.Length; index++ )
		{
			var vehicle = wanted[index];
			var (x, y) = (vehicle.X / UnitsPerModelUnit, vehicle.Y / UnitsPerModelUnit);
			var pose = TrackSurface.Pose( x, y, vehicle.Heading, vehicle.Radius / UnitsPerModelUnit, ( probeX, probeY ) => SurfaceHeight( layer, probeX, probeY ) );
			var transform = Matrix4x4.CreateRotationX( pose.Pitch ) * Matrix4x4.CreateRotationY( -pose.Roll )
				* Matrix4x4.CreateRotationZ( -vehicle.Heading * 2 * MathF.PI / BumpMath.Turn )
				* Matrix4x4.CreateTranslation( x, y, pose.Height )
				* Matrix4x4.CreateScale( ObjectPlacement.ModelScale )
				* Matrix4x4.CreateTranslation( origin.X, origin.Y, 0 );
			foreach ( var entity in boats[index] )
			{
				entity.TransformOverride = transform;
				entity.Position = new Vector3( transform.M41, transform.M42, transform.M43 );
			}
		}
	}

	/// <summary>
	/// The height of the surface a boat floats on at a model-unit point, as an absolute height in model units: the water faces of the track tile under the
	/// point (the tile's ground plus the face height), the station's water plane, else the terrain.
	/// </summary>
	// [BIN:TPW-EXE:0x00546280 vehicle height] four probes at ± the vehicle radius along the facing and across it; each takes the highest surface triangle under the point (faces whose first u16 has bit 0x8000), else the terrain plus the object's base height; the mean is the height (+0xA0), the front/back and right/left differences the pitch and roll
	// [APPROX:TRACK-001] a tile's base is the mean ground of its four cells, the surface faces are the pieces' water meshes (TrackSurface) and the station tiles use one water height above the ground; the face-flag test and the surface object lookup of the original are not reproduced
	private float SurfaceHeight( TrackCellLayer layer, float x, float y )
	{
		var cellX = Math.Clamp( (int)MathF.Floor( x / 10 ), 0, layer.Width - 1 );
		var cellY = Math.Clamp( (int)MathF.Floor( y / 10 ), 0, layer.Height - 1 );
		float Ground( int gx, int gy ) => groundHeight( Math.Clamp( gx, 0, layer.Width - 1 ), Math.Clamp( gy, 0, layer.Height - 1 ) ) / ObjectPlacement.ModelScale;
		var cell = layer.Get( cellX, cellY );
		if ( cell.Type == 0 )
			return Ground( cellX, cellY );
		var (ox, oy) = cell.IsExtension ? layer.CellOf( cell.Parent ) : (cellX, cellY);
		var tile = layer.Get( ox, oy );
		var baseHeight = (Ground( ox, oy ) + Ground( ox + 1, oy ) + Ground( ox, oy + 1 ) + Ground( ox + 1, oy + 1 )) / 4;
		if ( TrackCellTypes.IsStation( tile.Type ) )
			return baseHeight + TrackSurface.StationWaterHeight;
		if ( tile.Piece != 0 && GeometryOf( tile.Piece ) is { } geometry )
		{
			// the tile turned counter-clockwise by its rotation: undo it about the tile's centre to read the piece's own faces
			var (dx, dy) = (x - (ox * 10 + 10), y - (oy * 10 + 10));
			var angle = tile.Rotation * MathF.PI / 180;
			var (lx, ly) = (10 + dx * MathF.Cos( angle ) + dy * MathF.Sin( angle ), 10 - dx * MathF.Sin( angle ) + dy * MathF.Cos( angle ));
			if ( geometry.Surface.HeightAt( lx, ly ) is { } surface )
				return baseHeight + surface;
		}
		return Ground( cellX, cellY );
	}

	// ---- Geometry ----------------------------------------------------------------------------------

	private PieceGeometry? GeometryOf( int piece )
	{
		if ( pieces.TryGetValue( piece, out var cached ) )
			return cached;
		PieceGeometry? geometry = null;
		if ( kit.GetModel( piece ) is { } model )
			geometry = Build( model, entry );
		pieces[piece] = geometry!;
		return geometry;
	}

	/// <summary>The boat mesh: the ride's first supplemental mesh, <c>wr_ring.md2</c> next to the ride's own mesh.</summary>
	private PieceGeometry? BuildBoat()
	{
		var directory = entry.ArchivePath;
		var name = entry.Settings["SupplementalMeshes[0].FileName"] ?? "wr_ring.md2";
		try
		{
			var path = entry.FileSystem.GetFiles( directory ).FirstOrDefault( file => string.Equals( Path.GetFileName( file ), name.Trim( '"' ), StringComparison.OrdinalIgnoreCase ) );
			if ( path == null )
				return null;
			var model = ObjectAssets.LoadModel( entry.FileSystem, path );
			return model.Kind == ModelFileKind.Geometry ? Build( model, entry ) : null;
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
		{
			Log?.Warning( $"The water ride's boat mesh could not be read: {exception.Message}" );
			return null;
		}
	}

	/// <summary>Bakes every mesh of a piece with its rest transform and builds one drawable model per mesh part.</summary>
	private static PieceGeometry Build( ModelFile model, ObjectCatalogEntry entry )
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
		return new PieceGeometry( models, TrackSurface.FromModel( model ) );
	}

	private static Texture LoadTexture( ObjectCatalogEntry entry, string name )
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
		foreach ( var entity in tiles.Concat( boats.SelectMany( boatEntities => boatEntities ) ) )
			entity.Delete();
		tiles.Clear();
		boats.Clear();
		foreach ( var geometry in pieces.Values.Append( boat! ).Where( item => item != null ) )
		{
			foreach ( var model in geometry.Models )
			{
				model.ReleaseBuffers();
				model.Material.ReleaseOwnedResources();
			}
		}
		pieces.Clear();
	}
}
