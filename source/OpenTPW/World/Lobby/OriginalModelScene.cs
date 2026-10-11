using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// A 3D front-end scene built from original MD2 models (the lobby, the online globe): meshes are composed
/// through their node hierarchy (parent-relative, docs/MD2-MODELS.md), MD2 (x, y-up, z) maps to engine
/// (x, z, y-up), and up to 16 textures share one <see cref="Model"/> per part.
/// </summary>
public abstract class OriginalModelScene : Entity
{
	protected const int MaximumTextureSlots = 16;
	protected readonly List<ModelEntity> parts = new();
	private readonly List<Model> models = new();
	private readonly Dictionary<string, Texture> textures = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Folder the scene's texture names are looked up in.</summary>
	protected abstract string TextureDirectory { get; }

	public int PartCount => parts.Count;

	/// <summary>Shows or hides every part; the scene stays loaded.</summary>
	public bool Visible
	{
		get => parts.Count == 0 || !parts[0].Hidden;
		set
		{
			foreach ( var part in parts )
				part.Hidden = !value;
		}
	}

	protected static NVector3 ToEngine( NVector3 md2 ) => new( md2.X, md2.Z, md2.Y );

	/// <summary>Adds the model's meshes, placed by <paramref name="placement"/>; returns the parts it created.</summary>
	protected IReadOnlyList<ModelEntity> AddModel( ModelFile model, NMatrix4x4 placement )
	{
		var first = parts.Count;
		var names = new List<string>();
		var vertices = new List<Vertex>();
		var indices = new List<uint>();
		foreach ( var mesh in model.Meshes )
		{
			if ( mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 || mesh.Materials.Length == 0 || mesh.TexCoords.Length < mesh.Vertices.Length || mesh.Normals.Length != mesh.Vertices.Length )
				continue;
			var world = OriginalTerrain.GetWorldMatrix( model, mesh.NodeIndex ) * placement;
			int MaterialOf( uint vertex ) => (int)Math.Min( mesh.Vertices[vertex].TextureIndex, (uint)mesh.Materials.Length - 1 );
			// A draw binds at most 16 textures; a mesh with more (the online globe has 24) is split by material.
			for ( var low = 0; low < mesh.Materials.Length; low += MaximumTextureSlots )
			{
				var high = Math.Min( mesh.Materials.Length, low + MaximumTextureSlots );
				var meshNames = mesh.Materials[low..high].Select( material => material.TextureIndex < 0 ? "" : material.Name ).ToArray();
				if ( names.Union( meshNames, StringComparer.OrdinalIgnoreCase ).Count() > MaximumTextureSlots )
					Flush( names, vertices, indices );
				var slots = meshNames.Select( name =>
				{
					var slot = names.FindIndex( existing => string.Equals( existing, name, StringComparison.OrdinalIgnoreCase ) );
					if ( slot < 0 )
					{
						names.Add( name );
						slot = names.Count - 1;
					}
					return slot;
				} ).ToArray();
				var remap = new Dictionary<uint, uint>();
				for ( var triangle = 0; triangle + 2 < mesh.Indices.Length; triangle += 3 )
				{
					var material = MaterialOf( mesh.Indices[triangle] );
					if ( material < low || material >= high )
						continue;
					for ( var corner = 0; corner < 3; corner++ )
					{
						var index = mesh.Indices[triangle + corner];
						if ( !remap.TryGetValue( index, out var mapped ) )
						{
							var source = mesh.Vertices[index];
							var normal = NVector3.TransformNormal( mesh.Normals[index].GetSystemVector3(), world );
							mapped = (uint)vertices.Count;
							vertices.Add( new Vertex
							{
								Position = ToEngine( NVector3.Transform( source.Position.GetSystemVector3(), world ) ),
								Normal = normal.LengthSquared() > 0 ? NVector3.Normalize( ToEngine( normal ) ) : NVector3.UnitZ,
								TexCoords = mesh.TexCoords[index],
								TexIndex = slots[material - low],
								MatFlags = mesh.Materials[material].Flags
							} );
							remap[index] = mapped;
						}
						indices.Add( mapped );
					}
				}
			}
		}
		Flush( names, vertices, indices );
		return parts.GetRange( first, parts.Count - first );
	}

	protected void Flush( List<string> names, List<Vertex> vertices, List<uint> indices )
	{
		if ( vertices.Count > 0 )
		{
			var slots = new Texture[MaximumTextureSlots];
			for ( var index = 0; index < slots.Length; index++ )
				slots[index] = index < names.Count ? LoadTexture( names[index] ) : slots[0] ?? Texture.Missing;
			var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
			material.Set( "Color", slots );
			var model = new Model( vertices.ToArray(), indices.ToArray(), material );
			models.Add( model );
			parts.Add( new ModelEntity { Model = model, Name = $"{GetType().Name} part {parts.Count}" } );
		}
		names.Clear();
		vertices.Clear();
		indices.Clear();
	}

	private Texture LoadTexture( string name )
	{
		if ( name.Length == 0 )
			return Texture.Missing;
		if ( textures.TryGetValue( name, out var texture ) )
			return texture;
		var path = OpenTPW.UI.Original.UiImages.Resolve( name, TextureDirectory );
		texture = path == null ? Texture.Missing : new Texture( path, TextureFlags.Repeat );
		textures[name] = texture;
		return texture;
	}

	/// <summary>Removes one part and frees its buffers.</summary>
	protected void RemovePart( ModelEntity part )
	{
		if ( !parts.Remove( part ) )
			return;
		part.Delete();
		if ( part.Model is { } model && models.Remove( model ) )
			Free( model );
	}

	private static void Free( Model model )
	{
		Asset.All.Remove( model );
		global::Global.Render.ScheduleDelete( () =>
		{
			model.VertexBuffer.Dispose();
			model.IndexBuffer?.Dispose();
		} );
	}

	protected override void OnDelete()
	{
		foreach ( var part in parts )
			part.Delete();
		parts.Clear();
		foreach ( var model in models )
			Free( model );
		models.Clear();
	}
}
