using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;
using System.Text.Json;

namespace OpenTPW;

/// <summary>Original, triangulated Blender geometry. No procedural substitute is used in the game.</summary>
public sealed record PoolBridgeAsset( int Version, float UnitsPerCell, float SpanStart, float SpanEnd, float DeckHeight, PoolBridgeAssetPart[] Parts )
{
	public static PoolBridgeAsset Load( string path ) => JsonSerializer.Deserialize<PoolBridgeAsset>( File.ReadAllBytes( path ), new JsonSerializerOptions { PropertyNameCaseInsensitive = true } )
		?? throw new InvalidDataException( "The pool bridge mesh is empty." );
}
public sealed record PoolBridgeAssetPart( string Name, string Texture, float[][] Vertices, int[] Indices );

public static class PoolBridgeMeshBuilder
{
	public static PoolMeshPart Build( PoolBridgeAssetPart part, IEnumerable<PoolBridge> bridges, SVector2 origin, float cellSize = 2 )
	{
		var mesh = new PoolMeshPart();
		foreach ( var bridge in bridges )
		{
			var (dx, dy) = GuestPathGrid.Directions[bridge.Direction];
			var start = mesh.Vertices.Count;
			foreach ( var v in part.Vertices )
			{
				var along = v[0] / 10f - .5f; var across = v[1] / 10f - .5f;
				var position = new SVector3( origin.X + (bridge.X + .5f + dx * along - dy * across) * cellSize,
					origin.Y + (bridge.Y + .5f + dy * along + dx * across) * cellSize, bridge.Ground + v[2] * cellSize / 10f );
				var normal = new SVector3( dx * v[3] - dy * v[4], dy * v[3] + dx * v[4], v[5] );
				mesh.Vertices.Add( new PoolVertex( position, normal, new SVector2( v[6], 1 - v[7] ) ) );
			}
			mesh.Indices.AddRange( part.Indices.Select( i => i + start ) );
		}
		return mesh;
	}
}

/// <summary>[EXT:pools-bridges] Opaque, depth-tested Blender bridge meshes above the independent swimming layer.</summary>
public sealed class PoolBridgeRenderer : Entity
{
	private readonly PoolBridgeWorld world;
	private readonly SVector2 origin;
	private readonly PoolBridgeAsset asset;
	private readonly List<PoolPartEntity> parts = new();
	private readonly List<Material> materials = new();
	private int seenVersion = -1;
	public PoolBridgeRenderer( PoolBridgeWorld world, SVector2 origin, PoolStyle style )
	{
		this.world = world; this.origin = origin;
		Name = "PoolBridges";
		var directory = Path.Combine( PoolContent.Directory ?? PoolContent.DirectoryOf( AppContext.BaseDirectory ), "bridge" );
		asset = PoolBridgeAsset.Load( Path.Combine( directory, "bridge.json" ) );
		foreach ( var part in asset.Parts )
		{
			var material = new Material<ObjectUniformBuffer>( "content/shaders/pool-deck.shader", MaterialFlags.None );
			material.Set( "Color", new Texture( Path.Combine( directory, part.Texture ), TextureFlags.Wrap ) );
			materials.Add( material );
			parts.Add( new PoolPartEntity { Deep = style.WaterDeep.ToVector(), Shallow = style.WaterShallow.ToVector(), Glow = 0, IsWater = false, PartMaterial = material } );
		}
	}
	protected override void OnUpdate()
	{
		if ( seenVersion == world.Version ) return;
		seenVersion = world.Version;
		for ( var i = 0; i < parts.Count; i++ )
		{
			var entity = parts[i]; entity.Model?.ReleaseBuffers(); entity.Model = null;
			var mesh = PoolBridgeMeshBuilder.Build( asset.Parts[i], world.Bridges, origin );
			if ( mesh.Vertices.Count > 0 ) entity.Model = new Model( mesh.Vertices.Select( v => new Vertex( v.Position, v.Normal, v.Uv ) ).ToArray(), mesh.Indices.Select( i => (uint)i ).ToArray(), entity.PartMaterial! );
		}
	}
	protected override void OnDelete()
	{
		foreach ( var part in parts ) { part.Model?.ReleaseBuffers(); part.Model = null; part.Delete(); }
		foreach ( var material in materials ) material.ReleaseOwnedResources();
	}
}
