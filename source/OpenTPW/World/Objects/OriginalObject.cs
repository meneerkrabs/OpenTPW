using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Where an object stands on a park grid: the anchor cell (save record X/Y), rotation in degrees, the engine
/// position of grid corner (0, 0) and the engine height of the object's base. One cell is 10 MD2 units,
/// <see cref="CellSize"/> engine units.
/// </summary>
public readonly record struct ObjectPlacement( int X, int Y, int Rotation, System.Numerics.Vector2 GridOrigin, float BaseHeight )
{
	// [APPROX:RIDES-026] Engine units: 1 MD2 unit = 0.2 (presentation scale shared with the terrain; no game rule) — evidence needed: none (engine convention)
	public const float ModelScale = 0.2f;
	public const float CellSize = 10 * ModelScale;

	/// <summary>
	/// Model-space (MD2 units, axes already swapped so XY is the ground) to engine matrix: the rigid rotation of
	/// <see cref="ObjectFootprint.ToGrid(int,int,int,System.Numerics.Vector2)"/>, the anchor offset, the 0.2 scale
	/// and the grid origin. Fixed items (Info.DontApplyOffset) use anchor (0, 0) without rotation: their
	/// models are authored in park coordinates (e.g. the Jungle gates span MD2 X 450..510, cells 45..51).
	/// </summary>
	public Matrix4x4 ModelToEngine
	{
		get
		{
			var rotation = Rotation switch
			{
				0 => Matrix4x4.Identity,
				90 => new Matrix4x4( 0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 10, 0, 1 ),
				180 => new Matrix4x4( -1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 10, 10, 0, 1 ),
				270 => new Matrix4x4( 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 10, 0, 0, 1 ),
				_ => throw new ArgumentOutOfRangeException( nameof( Rotation ), "Rotations are multiples of 90 degrees." )
			};
			return rotation
				* Matrix4x4.CreateTranslation( X * 10, Y * 10, 0 )
				* Matrix4x4.CreateScale( ModelScale )
				* Matrix4x4.CreateTranslation( GridOrigin.X, GridOrigin.Y, BaseHeight );
		}
	}

	/// <summary>Swaps MD2 Y (up) and Z on both sides of a node matrix, matching <see cref="ObjectAssets.ConvertAxes"/>.</summary>
	public static Matrix4x4 SwapAxes( Matrix4x4 node )
	{
		var swap = new Matrix4x4( 1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1 );
		return swap * node * swap;
	}
}

/// <summary>
/// A placed original ride, shop, sideshow, feature or fixed item: its original model hierarchy rendered
/// with original textures, animated by its original RSE script (<see cref="OriginalObjectRuntime"/>).
/// </summary>
public class OriginalObject : Entity
{
	private readonly List<(ModelEntity Entity, int NodeIndex)> parts = new();
	private readonly List<Model> models = new();
	private bool deleted;

	public OriginalObject( ObjectCatalogEntry entry, ObjectPlacement placement, RideScriptWorld? world = null, int? seed = null, bool open = true )
	{
		try
		{
			if ( !ObjectFootprint.IsValidRotation( placement.Rotation ) || !float.IsFinite( placement.BaseHeight ) )
				throw new ArgumentOutOfRangeException( nameof( placement ) );
			Placement = placement;
			Runtime = new OriginalObjectRuntime( entry, world, seed, open );
			Name = entry.DisplayName;
			Position = System.Numerics.Vector3.Transform( System.Numerics.Vector3.Zero, placement.ModelToEngine );
			BuildModels();
			UpdateTransforms();
		}
		catch
		{
			Delete();
			throw;
		}
	}

	public ObjectCatalogEntry Entry => Runtime.Entry;
	public OriginalObjectRuntime Runtime { get; } = null!;
	public ObjectPlacement Placement { get; }
	public RideVM? Script => Runtime.Script;
	/// <summary>Visitor side of the script (guests slice).</summary>
	public RideVisitorBridge Visitors => Runtime.Visitors;
	public bool IsOpen => Runtime.IsOpen;
	public bool IsAnimating => Runtime.Animator.IsPlaying;
	public IReadOnlyList<Matrix4x4> NodeTransforms => Runtime.Animator.NodeTransforms;
	public int PartCount => parts.Count;
	public bool IsDeleted => deleted;

	/// <summary>Occupied grid cells with their shape kinds (entrance, exit, ...).</summary>
	public IEnumerable<(int X, int Y, ObjectShapeCell Cell)> Cells => Entry.IsFixedItem
		? Enumerable.Empty<(int, int, ObjectShapeCell)>()
		: ObjectFootprint.GetCells( Entry.Shape, Placement.X, Placement.Y, Placement.Rotation );

	/// <summary>Entrance/exit cells and the outside cells guests queue at or leave to (guests slice).</summary>
	public IEnumerable<ObjectAccessPoint> AccessPoints => Entry.IsFixedItem
		? Enumerable.Empty<ObjectAccessPoint>()
		: ObjectFootprint.GetAccessPoints( Entry.Shape, Placement.X, Placement.Y, Placement.Rotation );

	public void Open() => Runtime.Open();
	public void Close() => Runtime.Close();

	/// <summary>Engine-space matrix of a model node (e.g. a seat dummy) for other systems.</summary>
	public Matrix4x4 GetNodeTransform( int node ) => ObjectPlacement.SwapAxes( NodeTransforms[node] ) * Placement.ModelToEngine;

	/// <summary>One fixed simulation tick.</summary>
	public void Simulate( double deltaSeconds )
	{
		if ( deleted )
			return;
		Runtime.Simulate( deltaSeconds );
		UpdateTransforms();
	}

	private void BuildModels()
	{
		foreach ( var part in ObjectRenderParts.Build( Entry, Runtime.Model ) )
		{
			var slots = new Texture[16];
			Array.Fill( slots, Texture.Missing );
			for ( var index = 0; index < part.Textures.Length; index++ )
			{
				slots[index] = LoadTexture( Entry, part.Textures[index] );
			}
			var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
			material.Set( "Color", slots );
			var model = new Model( part.Vertices, part.Indices, material );
			models.Add( model );
			parts.Add( (new ModelEntity { Model = model, Name = $"{Entry.ArchiveName}:{part.Name}" }, part.NodeIndex) );
		}
	}

	private static readonly Dictionary<string, Texture> bonusTextures = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Game-data textures go through the shared texture cache; bonus-content textures are decoded from their own root.</summary>
	private static Texture LoadTexture( ObjectCatalogEntry entry, string name )
	{
		var resolved = ObjectAssets.ResolveTexture( entry, name );
		if ( resolved == null )
			return Texture.Missing;
		var (fileSystem, path) = resolved.Value;
		if ( fileSystem == FileSystem )
			return new Texture( path, TextureFlags.Repeat );
		var key = fileSystem.GetAbsolutePath( path );
		if ( !bonusTextures.TryGetValue( key, out var texture ) )
		{
			using var stream = fileSystem.OpenRead( path );
			var data = new TextureFile( stream ).Data;
			texture = new Texture( data.Data, data.Width, data.Height, TextureFlags.Repeat );
			bonusTextures[key] = texture;
		}
		return texture;
	}

	private void UpdateTransforms()
	{
		var placement = Placement.ModelToEngine;
		var nodes = Runtime.Animator.NodeTransforms;
		foreach ( var (entity, node) in parts )
		{
			var transform = node < 0 ? placement : ObjectPlacement.SwapAxes( nodes[node] ) * placement;
			entity.TransformOverride = transform;
			entity.Position = new Vector3( transform.M41, transform.M42, transform.M43 );
		}
	}

	protected override void OnDelete()
	{
		deleted = true;
		Runtime?.Stop();
		foreach ( var (entity, _) in parts )
			entity.Delete();
		parts.Clear();
		foreach ( var model in models )
		{
			Asset.All.Remove( model );
			global::Global.Render.ScheduleDelete( () =>
			{
				model.VertexBuffer.Dispose();
				model.IndexBuffer?.Dispose();
			} );
		}
		models.Clear();
	}
}
