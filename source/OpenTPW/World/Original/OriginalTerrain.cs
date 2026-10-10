using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Renders an imported original level: the base.MD2 terrain meshes (node transforms composed up the
/// hierarchy), the heightfield surface with its per-cell ground texture slot, and the paths as a blended
/// layer on that ground. Each path cell takes the theme's PathTex shape and turn for its neighbours
/// (<see cref="PathTiles"/>; cells unchanged since the original save keep the saved ones); queue cells use
/// PathTex entry 0. The layer follows <see cref="PathSource"/> and <see cref="QueueSource"/> (the level's cell
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
			var textures = new Dictionary<string, Texture>( StringComparer.OrdinalIgnoreCase );
			BuildHeightfield( textures );
			BuildTerrainMeshes( textures );
		}
		catch
		{
			Delete();
			throw;
		}
	}

	public OriginalPark Park { get; }
	/// <summary>Path cells; null: the MAP InitialPath and save path cells.</summary>
	public Func<int, int, bool>? PathSource { get; set; }
	/// <summary>Queue cells, drawn with PathTex entry 0; null: none.</summary>
	public Func<int, int, bool>? QueueSource { get; set; }
	public int SurfaceCellCount { get; private set; }
	/// <summary>Path and queue cells in the path layer.</summary>
	public int PathCellCount { get; private set; }
	private Dictionary<string, Texture>? heightfieldTextures;
	private readonly List<ModelEntity> pathParts = new();

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
				ScheduleDispose( model );
		}
		pathParts.Clear();
		BuildPaths( heightfieldTextures );
	}
	public int TerrainMeshCount { get; private set; }

	private void BuildHeightfield( Dictionary<string, Texture> textures )
	{
		heightfieldTextures = textures;
		var field = Park.Heightfield;
		var slots = new List<Texture>();
		var slotIndices = new Dictionary<int, int>();
		var vertices = new List<Vertex>();
		var indices = new List<uint>();
		for ( var y = 0; y < field.CellCountZ; y++ )
		{
			for ( var x = 0; x < field.CellCountX; x++ )
			{
				var modelSlot = field.GetCellTextureSlot( x, y );
				if ( modelSlot < 0 )
					continue;
				if ( !slotIndices.TryGetValue( modelSlot, out var slot ) )
				{
					if ( slots.Count >= MaximumTextureSlots || modelSlot >= Park.TerrainModel.Textures.Count )
						throw new InvalidDataException( "The original heightfield uses more ground textures than the renderer supports." );
					slot = AddSlot( slots, LoadTexture( textures, Park.TerrainModel.Textures[modelSlot].FrameNames[0] ) );
					slotIndices.Add( modelSlot, slot );
				}
				AddCell( vertices, indices, field, x, y, slot, PathTiles.Identity, 0 );
			}
		}
		SurfaceCellCount = vertices.Count / 4;
		var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
		while ( slots.Count < MaximumTextureSlots )
			slots.Add( slots[0] );
		material.Set( "Color", slots.ToArray() );
		AddPart( new Model( vertices.ToArray(), indices.ToArray(), material ), "heightfield" );
		BuildPaths( textures );
	}

	/// <summary>The path layer: one quad per path or queue cell, batched by at most 16 PathTex textures.</summary>
	private void BuildPaths( Dictionary<string, Texture> textures )
	{
		var field = Park.Heightfield;
		var names = ReadPathTextureNames();
		var cells = new Dictionary<int, List<(int X, int Y, int Rotation)>>();
		for ( var y = 0; y < field.CellCountZ; y++ )
		{
			for ( var x = 0; x < field.CellCountX; x++ )
			{
				if ( field.GetCellTextureSlot( x, y ) < 0 )
					continue;
				PathTile tile;
				if ( IsPath( x, y ) )
					tile = ChoosePathTile( x, y );
				else if ( QueueSource?.Invoke( x, y ) == true )
					tile = new PathTile( 0, 0 );
				else
					continue;
				if ( !names.ContainsKey( tile.TextureIndex ) )
					tile = new PathTile( 0, tile.Rotation );
				if ( !cells.TryGetValue( tile.TextureIndex, out var list ) )
					cells.Add( tile.TextureIndex, list = new() );
				list.Add( (x, y, tile.Rotation) );
			}
		}
		PathCellCount = cells.Values.Sum( list => list.Count );
		// Lifted a little along the cell so the blended layer never fights the ground for depth.
		var lift = 0.01f * field.CellSizeX;
		foreach ( var batch in cells.Keys.Order().Chunk( MaximumTextureSlots ) )
		{
			var slots = batch.Select( index => names.TryGetValue( index, out var name ) ? LoadTexture( textures, name, "pathtex" ) : Texture.Missing ).ToList();
			var vertices = new List<Vertex>();
			var indices = new List<uint>();
			for ( var slot = 0; slot < batch.Length; slot++ )
				foreach ( var (x, y, rotation) in cells[batch[slot]] )
					AddCell( vertices, indices, field, x, y, slot, rotation, lift );
			var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
			while ( slots.Count < MaximumTextureSlots )
				slots.Add( slots[0] );
			// Clamped: a repeating sampler would blend each tile's open edge with its transparent opposite edge.
			material.Set( "Color", slots.ToArray(), SamplerType.Anisotropic );
			pathParts.Add( AddPart( new Model( vertices.ToArray(), indices.ToArray(), material ), $"paths {pathParts.Count}" ) );
		}
	}

	private bool IsPath( int x, int y ) => PathSource?.Invoke( x, y ) ??
		Park.Map.GetFlagsAt( x, y ).HasFlag( MapCellFlags.InitialPath ) || Park.Save?.Cells[x, y].IsPath == true;

	private PathTile ChoosePathTile( int x, int y ) =>
		PathTiles.ForCell( x, y, Park.Heightfield.CellCountX, Park.Heightfield.CellCountZ, IsPath, Park.Save?.Cells );

	private static int AddSlot( List<Texture> slots, Texture texture )
	{
		slots.Add( texture );
		return slots.Count - 1;
	}

	/// <summary>
	/// One cell quad. <paramref name="rotation"/> is a path texture's clockwise turn (−Y to +X); path textures have
	/// +Y at their top row, so those quads also flip V (docs/PATHS.md, "Path textures").
	/// </summary>
	private static void AddCell( List<Vertex> vertices, List<uint> indices, ModelHeightfield field, int x, int y, int slot, int rotation, float lift )
	{
		var start = (uint)vertices.Count;
		var corners = new[] { (x, y, 0f, 0f), (x + 1, y, 1f, 0f), (x, y + 1, 0f, 1f), (x + 1, y + 1, 1f, 1f) };
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
		foreach ( var offset in new uint[] { 0, 2, 3, 3, 1, 0 } )
			indices.Add( start + offset );
	}

	/// <summary>
	/// Converts every base.MD2 mesh to engine space and batches meshes so each draw call needs at
	/// most 16 textures, keeping the number of compiled materials small.
	/// </summary>
	private void BuildTerrainMeshes( Dictionary<string, Texture> textures )
	{
		var model = Park.TerrainModel;
		var batchTextures = new List<string>();
		var batchVertices = new List<Vertex>();
		var batchIndices = new List<uint>();
		foreach ( var mesh in model.Meshes )
		{
			if ( mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 || mesh.Materials.Length is 0 or > MaximumTextureSlots || mesh.Normals.Length != mesh.Vertices.Length || mesh.TexCoords.Length < mesh.Vertices.Length )
				continue;
			var names = mesh.Materials.Select( material => material.Name ).ToArray();
			if ( batchTextures.Union( names, StringComparer.OrdinalIgnoreCase ).Count() > MaximumTextureSlots )
				FlushBatch( textures, batchTextures, batchVertices, batchIndices );
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
				var position = System.Numerics.Vector3.Transform( source.Position.GetSystemVector3(), world );
				var normal = System.Numerics.Vector3.TransformNormal( mesh.Normals[index].GetSystemVector3(), world );
				var materialIndex = (int)Math.Min( source.TextureIndex, (uint)mesh.Materials.Length - 1 );
				batchVertices.Add( new Vertex
				{
					Position = OriginalParkPlacement.ToEngine( Park.Heightfield, position ),
					Normal = normal.LengthSquared() > 0 ? System.Numerics.Vector3.Normalize( new System.Numerics.Vector3( normal.X, normal.Z, normal.Y ) ) : System.Numerics.Vector3.UnitZ,
					TexCoords = mesh.TexCoords[index],
					TexIndex = slotOfMaterial[materialIndex],
					MatFlags = mesh.Materials[materialIndex].Flags
				} );
			}
			foreach ( var index in mesh.Indices )
				batchIndices.Add( first + index );
			TerrainMeshCount++;
		}
		FlushBatch( textures, batchTextures, batchVertices, batchIndices );
	}

	private void FlushBatch( Dictionary<string, Texture> textures, List<string> names, List<Vertex> vertices, List<uint> indices )
	{
		if ( vertices.Count > 0 )
		{
			var slots = new Texture[MaximumTextureSlots];
			for ( var index = 0; index < slots.Length; index++ )
				slots[index] = index < names.Count ? LoadTexture( textures, names[index] ) : slots[0];
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

	private IReadOnlyDictionary<int, string> ReadPathTextureNames()
	{
		var table = ListFiles( Park.TerrainDirectory ).FirstOrDefault( file => file.EndsWith( ".tct", StringComparison.OrdinalIgnoreCase ) );
		return table == null ? new Dictionary<int, string>() : ReadPathTextureNames( FileSystem.ReadAllText( table ) );
	}

	/// <summary>Entry 0 of the "PathTex" list in a theme texture correspondence table (.tct).</summary>
	internal static string? ReadPathTextureName( string table ) => ReadPathTextureNames( table ).GetValueOrDefault( 0 );

	/// <summary>The "PathTex" list of a theme texture correspondence table (.tct): index to texture name.</summary>
	internal static IReadOnlyDictionary<int, string> ReadPathTextureNames( string table )
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
				inPathList = string.Equals( fields[0], "PathTex", StringComparison.OrdinalIgnoreCase );
				continue;
			}
			if ( inPathList && fields.Length >= 2 && int.TryParse( fields[0], out var index ) )
				names.TryAdd( index, Path.GetFileNameWithoutExtension( fields[1] ) );
		}
		return names;
	}

	private Texture LoadTexture( Dictionary<string, Texture> textures, string name, string directory = "textures" )
	{
		var key = $"{directory}/{Path.GetFileNameWithoutExtension( name.TrimEnd( '\0' ) )}";
		if ( textures.TryGetValue( key, out var texture ) )
			return texture;
		var path = new[] { $"{Park.TerrainDirectory}/{directory}", $"/levels/{Park.LevelName}/sharetex" }
			.SelectMany( ListFiles )
			.FirstOrDefault( file => string.Equals( Path.GetFileNameWithoutExtension( file ), Path.GetFileName( key ), StringComparison.OrdinalIgnoreCase ) );
		texture = path == null ? Texture.Missing : new Texture( path, TextureFlags.Repeat );
		textures.Add( key, texture );
		return texture;
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
		var part = new ModelEntity { Model = model, Name = name };
		parts.Add( part );
		return part;
	}

	private static void ScheduleDispose( Model model )
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
			ScheduleDispose( model );
		models.Clear();
	}
}
