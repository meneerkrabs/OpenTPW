using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Renders an imported original level: the base.MD2 terrain meshes (node transforms composed up the
/// hierarchy), the heightfield surface with its per-cell ground texture slot, and the paths as a blended
/// layer on that ground. Each path cell takes the theme's PathTex shape and turn for its neighbours
/// (<see cref="PathTiles"/>; cells unchanged since the original save keep the saved ones); queues use the
/// separate QueueTex list and queue archive. The layer follows <see cref="PathSource"/> and <see cref="QueueSource"/> (the level's cell
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
		BuildHeightfield( textures );
		yield return true;
		foreach ( var _ in BuildTerrainMeshes( textures ) )
			yield return true;
		if ( missingTextures.Count > 0 )
			Log.Warning( $"Terrain textures of {Park.LevelName} that the game data lacks, drawn plain white: {string.Join( ", ", missingTextures )}." );
	}

	public OriginalPark Park { get; }
	/// <summary>Path cells; null: the MAP InitialPath and save path cells.</summary>
	public Func<int, int, bool>? PathSource { get; set; }
	/// <summary>Queue cells, drawn with the theme's QueueTex textures; null: none.</summary>
	public Func<int, int, bool>? QueueSource { get; set; }
	/// <summary>Queue predecessor links, so neighbouring independent queues do not visually join.</summary>
	public Func<int, int, byte>? QueueLinkSource { get; set; }
	public int SurfaceCellCount { get; private set; }
	/// <summary>Path and queue cells in the path layer.</summary>
	public int PathCellCount { get; private set; }
	private Dictionary<string, Texture>? heightfieldTextures;
	private readonly List<ModelEntity> pathParts = new();
	/// <summary>Path layer materials by texture set; reused when the layer is rebuilt, because materials are never disposed.</summary>
	private readonly Dictionary<string, Material> pathMaterials = new();

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
		var pathNames = ReadTextureNames( "PathTex" );
		var queueNames = ReadTextureNames( "QueueTex" );
		var cells = new Dictionary<(bool Queue, int Index), List<(int X, int Y, int Rotation)>>();
		for ( var y = 0; y < field.CellCountZ; y++ )
		{
			for ( var x = 0; x < field.CellCountX; x++ )
			{
				if ( field.GetCellTextureSlot( x, y ) < 0 )
					continue;
				PathTile tile;
				var queue = false;
				if ( IsPath( x, y ) )
					tile = ChoosePathTile( x, y );
				else if ( QueueSource?.Invoke( x, y ) == true )
				{
					queue = true;
					tile = PathTiles.ForQueueCell( x, y, field.CellCountX, field.CellCountZ, QueueSource, QueueLinkSource, IsPath );
				}
				else
					continue;
				var names = queue ? queueNames : pathNames;
				if ( !names.ContainsKey( tile.TextureIndex ) )
					tile = new PathTile( 0, tile.Rotation );
				// Hallow and Space omit the isolated square (que4); keep a queue floor rather than showing a missing texture.
				if ( queue && tile.TextureIndex == 0 && names.TryGetValue( 0, out var square ) && names.ContainsKey( 2 )
					&& FindTexture( $"/levels/{Park.LevelName}/queue", Park.LevelName, square, "textures", ListFiles ) == null )
					tile = new PathTile( 2, tile.Rotation );
				var key = (queue, tile.TextureIndex);
				if ( !cells.TryGetValue( key, out var list ) )
					cells.Add( key, list = new() );
				list.Add( (x, y, tile.Rotation) );
			}
		}
		PathCellCount = cells.Values.Sum( list => list.Count );
		// Lifted a little along the cell so the blended layer never fights the ground for depth.
		var lift = 0.01f * field.CellSizeX;
		foreach ( var batch in cells.Keys.OrderBy( tile => tile.Queue ).ThenBy( tile => tile.Index ).Chunk( MaximumTextureSlots ) )
		{
			var slots = batch.Select( tile => (tile.Queue ? queueNames : pathNames).TryGetValue( tile.Index, out var name )
				? LoadTexture( textures, name, tile.Queue ? "textures" : "pathtex", tile.Queue ? $"/levels/{Park.LevelName}/queue" : null )
				: Texture.Missing ).ToList();
			var vertices = new List<Vertex>();
			var indices = new List<uint>();
			for ( var slot = 0; slot < batch.Length; slot++ )
				foreach ( var (x, y, rotation) in cells[batch[slot]] )
					AddCell( vertices, indices, field, x, y, slot, rotation, lift );
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
	/// One cell quad. <paramref name="rotation"/> is a path texture's clockwise turn (−Y to +X), applied through
	/// <see cref="PathTiles.TextureCoordinates"/> (docs/PATHS.md, "Path textures"); ground quads keep their coordinates.
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
	private IEnumerable<bool> BuildTerrainMeshes( Dictionary<string, Texture> textures )
	{
		var batchTextures = new List<string>();
		var batchVertices = new List<Vertex>();
		var batchIndices = new List<uint>();
		foreach ( var model in new[] { Park.TerrainModel, Park.LakeModel }.OfType<ModelFile>() )
		{
			BuildTerrainMeshes( textures, model, batchTextures, batchVertices, batchIndices );
			yield return true;
		}
		FlushBatch( textures, batchTextures, batchVertices, batchIndices );
	}

	private void BuildTerrainMeshes( Dictionary<string, Texture> textures, ModelFile model, List<string> batchTextures, List<Vertex> batchVertices, List<uint> batchIndices )
	{
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

	private IReadOnlyDictionary<int, string> ReadTextureNames( string listName )
	{
		var table = ListFiles( Park.TerrainDirectory ).FirstOrDefault( file => file.EndsWith( ".tct", StringComparison.OrdinalIgnoreCase ) );
		return table == null ? new Dictionary<int, string>() : ReadTextureNames( FileSystem.ReadAllText( table ), listName );
	}

	/// <summary>Entry 0 of the "PathTex" list in a theme texture correspondence table (.tct).</summary>
	internal static string? ReadPathTextureName( string table ) => ReadPathTextureNames( table ).GetValueOrDefault( 0 );

	/// <summary>The "PathTex" list of a theme texture correspondence table (.tct): index to texture name.</summary>
	internal static IReadOnlyDictionary<int, string> ReadPathTextureNames( string table ) => ReadTextureNames( table, "PathTex" );

	internal static IReadOnlyDictionary<int, string> ReadQueueTextureNames( string table ) => ReadTextureNames( table, "QueueTex" );

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
		var part = new ModelEntity { Model = model, Name = name };
		parts.Add( part );
		return part;
	}

	private static void ScheduleDispose( Model model )
	{
		model.ReleaseBuffers();
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
