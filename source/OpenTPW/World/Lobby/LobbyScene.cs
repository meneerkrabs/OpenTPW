using OpenTPW.FrontEnd;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The original 3D front-end lobby: the lobby terrain (<c>lobby/terrain/Base.MD2</c>: sea meshes and
/// heightfield) with the four theme islands and their gates placed at the ISLANDCAMERAPOSITION of
/// their index, turned by the ISLAND angle (docs/UI.md). Node transforms are composed up the
/// hierarchy (parent-relative, docs/MD2-MODELS.md); MD2 (x, y-up, z) maps to engine (x, z, y-up).
/// Flying meshes, rain, lightning and the online globe are not rendered.
/// </summary>
public sealed class LobbyScene : Entity
{
	private const int MaximumTextureSlots = 16;
	private const string TerrainDirectory = "/lobby/terrain";
	private readonly List<ModelEntity> parts = new();
	private readonly List<Model> models = new();
	private readonly Dictionary<string, Texture> textures = new( StringComparer.OrdinalIgnoreCase );

	public LobbyScene( LobbyDefinition definition )
	{
		Definition = definition;
		try
		{
			var terrain = new ModelFile( $"{TerrainDirectory}/Base.MD2" );
			AddModel( terrain, NMatrix4x4.Identity );
			if ( terrain.Heightfield != null )
				AddHeightfield( terrain );
			foreach ( var island in definition.Islands )
			{
				var (x, z) = definition.PositionOf( island );
				var placement = NMatrix4x4.CreateRotationY( island.Angle * MathF.PI / 180 ) * NMatrix4x4.CreateTranslation( x, 0, z );
				foreach ( var name in new[] { island.IslandModel, island.GateModel } )
				{
					var file = FindModel( name );
					if ( file != null )
						AddModel( new ModelFile( file ), placement );
				}
				IslandCount++;
			}
		}
		catch
		{
			Delete();
			throw;
		}
	}

	public LobbyDefinition Definition { get; }
	public int IslandCount { get; private set; }
	public int PartCount => parts.Count;

	/// <summary>Engine-space centre of an island's camera orbit (its position at the ISLAND height).</summary>
	public Vector3 Target( LobbyIslandInfo island )
	{
		var (x, z) = Definition.PositionOf( island );
		return new Vector3( x, z, island.Height );
	}

	private static string? FindModel( string name )
	{
		try
		{
			return FileSystem.GetFiles( TerrainDirectory ).FirstOrDefault( file => file.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase )
				&& string.Equals( Path.GetFileNameWithoutExtension( file ), name, StringComparison.OrdinalIgnoreCase ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
		{
			Log.Warning( $"Lobby model {name}: {exception.Message}" );
			return null;
		}
	}

	private static NVector3 ToEngine( NVector3 md2 ) => new( md2.X, md2.Z, md2.Y );

	private void AddModel( ModelFile model, NMatrix4x4 placement )
	{
		var names = new List<string>();
		var vertices = new List<Vertex>();
		var indices = new List<uint>();
		foreach ( var mesh in model.Meshes )
		{
			if ( mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 || mesh.Materials.Length is 0 or > MaximumTextureSlots || mesh.TexCoords.Length < mesh.Vertices.Length || mesh.Normals.Length != mesh.Vertices.Length )
				continue;
			var meshNames = mesh.Materials.Select( material => material.TextureIndex < 0 ? "" : material.Name ).ToArray();
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
			var world = OriginalTerrain.GetWorldMatrix( model, mesh.NodeIndex ) * placement;
			var first = (uint)vertices.Count;
			for ( var index = 0; index < mesh.Vertices.Length; index++ )
			{
				var source = mesh.Vertices[index];
				var materialIndex = (int)Math.Min( source.TextureIndex, (uint)mesh.Materials.Length - 1 );
				var normal = NVector3.TransformNormal( mesh.Normals[index].GetSystemVector3(), world );
				vertices.Add( new Vertex
				{
					Position = ToEngine( NVector3.Transform( source.Position.GetSystemVector3(), world ) ),
					Normal = normal.LengthSquared() > 0 ? NVector3.Normalize( ToEngine( normal ) ) : NVector3.UnitZ,
					TexCoords = mesh.TexCoords[index],
					TexIndex = slots[materialIndex],
					MatFlags = mesh.Materials[materialIndex].Flags
				} );
			}
			foreach ( var index in mesh.Indices )
				indices.Add( first + index );
		}
		Flush( names, vertices, indices );
	}

	/// <summary>The lobby heightfield (111×110 cells) with each cell's ground texture slot, like the park terrain.</summary>
	private void AddHeightfield( ModelFile model )
	{
		var field = model.Heightfield!;
		var node = model.Nodes.FirstOrDefault( candidate => string.Equals( candidate.Name, "heightfield", StringComparison.OrdinalIgnoreCase ) );
		var world = node == null ? NMatrix4x4.Identity : OriginalTerrain.GetWorldMatrix( model, node.Index );
		var names = new List<string>();
		var vertices = new List<Vertex>();
		var indices = new List<uint>();
		for ( var z = 0; z < field.CellCountZ; z++ )
		{
			for ( var x = 0; x < field.CellCountX; x++ )
			{
				var slot = field.GetCellTextureSlot( x, z );
				if ( slot < 0 || slot >= model.Textures.Count )
					continue;
				var name = Path.GetFileNameWithoutExtension( model.Textures[slot].FrameNames[0] );
				var textureIndex = names.FindIndex( existing => string.Equals( existing, name, StringComparison.OrdinalIgnoreCase ) );
				if ( textureIndex < 0 )
				{
					if ( names.Count >= MaximumTextureSlots )
						continue;
					names.Add( name );
					textureIndex = names.Count - 1;
				}
				var start = (uint)vertices.Count;
				var corners = new[] { (x, z, 0f, 0f), (x + 1, z, 1f, 0f), (x, z + 1, 0f, 1f), (x + 1, z + 1, 1f, 1f) };
				var positions = corners.Select( corner => ToEngine( NVector3.Transform(
					new NVector3( corner.Item1 * field.CellSizeX, field.GetCornerHeight( corner.Item1, corner.Item2 ), corner.Item2 * field.CellSizeZ ), world ) ) ).ToArray();
				var normal = NVector3.Normalize( NVector3.Cross( positions[1] - positions[0], positions[2] - positions[0] ) );
				if ( normal.Z < 0 )
					normal = -normal;
				for ( var corner = 0; corner < 4; corner++ )
					vertices.Add( new Vertex { Position = positions[corner], Normal = normal, TexCoords = new Vector2( corners[corner].Item3, corners[corner].Item4 ), TexIndex = textureIndex } );
				foreach ( var offset in new uint[] { 0, 2, 3, 3, 1, 0 } )
					indices.Add( start + offset );
			}
		}
		Flush( names, vertices, indices );
	}

	private void Flush( List<string> names, List<Vertex> vertices, List<uint> indices )
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
			parts.Add( new ModelEntity { Model = model, Name = $"lobby part {parts.Count}" } );
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
		var path = OpenTPW.UI.Original.UiImages.Resolve( name, $"{TerrainDirectory}/textures" );
		texture = path == null ? Texture.Missing : new Texture( path, TextureFlags.Repeat );
		textures[name] = texture;
		return texture;
	}

	protected override void OnDelete()
	{
		foreach ( var part in parts )
			part.Delete();
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
