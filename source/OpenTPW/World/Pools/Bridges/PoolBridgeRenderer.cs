using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;
using System.Text.Json;

namespace OpenTPW;

/// <summary>Original, triangulated Blender geometry. No procedural substitute is used in the game.</summary>
public sealed record PoolBridgeAsset( int Version, float UnitsPerCell, float SpanStart, float SpanEnd, float DeckHeight, PoolBridgeAssetPart[] Parts )
{
	public static PoolBridgeAsset Load( string path )
	{
		try
		{
			return JsonSerializer.Deserialize<PoolBridgeAsset>( File.ReadAllBytes( path ), new JsonSerializerOptions { PropertyNameCaseInsensitive = true } )
				?? throw new InvalidDataException( "The pool bridge mesh is empty." );
		}
		catch ( JsonException exception )
		{
			throw new InvalidDataException( "The pool bridge mesh is malformed: " + exception.Message, exception );
		}
	}
}
public sealed record PoolBridgeAssetPart( string Name, string Texture, float[][] Vertices, int[] Indices );

public static class PoolBridgeMeshBuilder
{
	public static PoolMeshPart Build( PoolBridgeAssetPart part, IEnumerable<PoolBridge> bridges, SVector2 origin, float cellSize = OriginalParkPlacement.CellSize )
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
public sealed class PoolBridgeRenderer : Entity, IFirstFramePreparation
{
	private readonly Func<PoolBridgeWorld?> worldSource;
	private readonly SVector2 origin;
	private PoolBridgeAsset? asset;
	private readonly List<PoolPartEntity> parts = new();
	private readonly List<Material> materials = new();
	private PoolBridgeWorld? seenWorld;
	private int seenVersion = -1;
	private const int PartCount = 3;

	public PoolBridgeRenderer( PoolBridgeWorld world, SVector2 origin, PoolStyle style ) : this( () => world, origin, style ) { }

	/// <summary>Reserve opaque draw slots before sprite renderers, even when the world and its first bridge arrive later. Empty parks load no assets or GPU resources.</summary>
	public PoolBridgeRenderer( Func<PoolBridgeWorld?> worldSource, SVector2 origin, PoolStyle style )
	{
		this.worldSource = worldSource; this.origin = origin;
		Name = "PoolBridges";
		for ( var i = 0; i < PartCount; i++ )
			parts.Add( new PoolPartEntity { Deep = style.WaterDeep.ToVector(), Shallow = style.WaterShallow.ToVector(), Glow = 0, IsWater = false } );
	}

	private void LoadAssets()
	{
		if ( asset != null ) return;
		var directory = Path.Combine( PoolContent.Directory ?? PoolContent.DirectoryOf( AppContext.BaseDirectory ), "bridge" );
		var loaded = PoolBridgeAsset.Load( Path.Combine( directory, "bridge.json" ) );
		if ( loaded.Parts.Length != PartCount )
			throw new InvalidDataException( "The pool bridge mesh must contain exactly three reserved draw parts." );
		for ( var i = 0; i < PartCount; i++ )
		{
			// Blender triangles follow outward normals, not the engine's clockwise front-face convention (as with pool meshes).
			var material = new Material<ObjectUniformBuffer>( "content/shaders/pool-deck.shader", MaterialFlags.DoubleSided );
			materials.Add( material );
			material.Set( "Color", new Texture( Path.Combine( directory, loaded.Parts[i].Texture ), TextureFlags.Wrap ) );
			parts[i].PartMaterial = material;
		}
		asset = loaded;
	}

	/// <summary>[EXT:park-entry-preparation] The assets and the meshes of the bridges built so far, as the first update would make them; that update then finds them made.</summary>
	IEnumerable<bool> IFirstFramePreparation.PrepareFirstFrame()
	{
		OnUpdate();
		yield return true;
	}

	protected override void OnUpdate()
	{
		var world = worldSource();
		if ( ReferenceEquals( seenWorld, world ) && seenVersion == (world?.Version ?? 0) ) return;
		if ( world is { Count: > 0 } ) LoadAssets();
		for ( var i = 0; i < parts.Count; i++ )
		{
			var entity = parts[i]; entity.Model?.ReleaseBuffers(); entity.Model = null;
			if ( world is not { Count: > 0 } ) continue;
			var mesh = PoolBridgeMeshBuilder.Build( asset!.Parts[i], world.Bridges, origin );
			if ( mesh.Vertices.Count > 0 ) entity.Model = new Model( mesh.Vertices.Select( v => new Vertex( v.Position, v.Normal, v.Uv ) ).ToArray(), mesh.Indices.Select( i => (uint)i ).ToArray(), entity.PartMaterial! );
		}
		seenWorld = world;
		seenVersion = world?.Version ?? 0;
	}
	protected override void OnDelete()
	{
		foreach ( var part in parts ) { part.Model?.ReleaseBuffers(); part.Model = null; part.Delete(); }
		foreach ( var material in materials ) material.ReleaseOwnedResources();
	}
}
