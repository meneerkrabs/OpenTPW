namespace OpenTPW;

/// <summary>
/// Shared CPU-side loading for original objects: parsed MD2 members (cached per file system and path, so
/// several instances of a ride parse each member once), mesh conversion and texture lookup.
/// </summary>
public static class ObjectAssets
{
	private static readonly Dictionary<string, ModelFile> models = new( StringComparer.OrdinalIgnoreCase );
	private static readonly Dictionary<string, string[]> listings = new( StringComparer.OrdinalIgnoreCase );

	public static ModelFile LoadModel( string path ) => LoadModel( FileSystem, path );

	/// <summary>Parses (once) an MD2 member from <paramref name="fileSystem"/> (the game data or a bonus-content root).</summary>
	public static ModelFile LoadModel( BaseFileSystem fileSystem, string path )
	{
		var key = fileSystem.GetAbsolutePath( path );
		lock ( models )
		{
			if ( models.TryGetValue( key, out var model ) )
				return model;
			using ( var stream = fileSystem.OpenRead( path ) )
				model = new ModelFile( stream );
			models[key] = model;
			return model;
		}
	}

	/// <summary>Engine vertices swap MD2 Y (up) and Z.</summary>
	public static System.Numerics.Vector3 ConvertAxes( System.Numerics.Vector3 position ) => new( position.X, position.Z, position.Y );

	/// <summary>
	/// Why a mesh cannot be drawn by the object renderer, or null. Empty meshes (no faces/materials) and meshes
	/// with more than 16 texture slots are skipped instead of failing the whole object.
	/// </summary>
	public static string? GetUnrenderableReason( ModelFile.Mesh mesh )
	{
		if ( mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 )
			return "no geometry";
		if ( mesh.Materials.Length == 0 )
			return "no materials";
		if ( mesh.Materials.Length > 16 )
			return "more than 16 texture slots";
		return null;
	}

	public static Vertex[] ConvertMesh( ModelFile.Mesh mesh, System.Numerics.Matrix4x4? bake = null )
	{
		if ( mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 || mesh.Indices.Length % 3 != 0 || mesh.Materials.Length == 0 || mesh.Materials.Length > 16 || mesh.Normals.Length != mesh.Vertices.Length || mesh.TexCoords.Length < mesh.Vertices.Length )
			throw new InvalidDataException( $"Invalid object mesh buffers: {mesh.Name}" );
		if ( mesh.Indices.Any( index => index >= mesh.Vertices.Length ) )
			throw new InvalidDataException( $"Invalid object mesh indices: {mesh.Name}" );

		var vertices = new Vertex[mesh.Vertices.Length];
		for ( var vertexIndex = 0; vertexIndex < vertices.Length; ++vertexIndex )
		{
			var sourceVertex = mesh.Vertices[vertexIndex];
			if ( sourceVertex.TextureIndex >= mesh.Materials.Length )
				throw new InvalidDataException( $"Invalid object material index: {mesh.Name}" );
			var position = sourceVertex.Position.GetSystemVector3();
			var normal = mesh.Normals[vertexIndex].GetSystemVector3();
			if ( bake != null )
			{
				position = System.Numerics.Vector3.Transform( position, bake.Value );
				normal = System.Numerics.Vector3.TransformNormal( normal, bake.Value );
				if ( normal.LengthSquared() > 0 )
					normal = System.Numerics.Vector3.Normalize( normal );
			}
			if ( !IsFinite( position ) || !IsFinite( normal ) || !float.IsFinite( mesh.TexCoords[vertexIndex].X ) || !float.IsFinite( mesh.TexCoords[vertexIndex].Y ) )
				throw new InvalidDataException( $"Nonfinite object mesh vertex: {mesh.Name}" );
			vertices[vertexIndex] = new Vertex
			{
				Position = ConvertAxes( position ),
				Normal = ConvertAxes( normal ),
				TexCoords = mesh.TexCoords[vertexIndex],
				TexIndex = (int)sourceVertex.TextureIndex,
				MatFlags = mesh.Materials[(int)sourceVertex.TextureIndex].Flags
			};
		}
		return vertices;
	}

	/// <summary>
	/// Converts a mesh into drawable parts with at most 16 texture slots each (the shader's limit). Meshes with
	/// more slots (31 large floors) are split by material: every corner carries its material, so faces never
	/// span parts. Vertex texture indices are renumbered per part; <c>Textures</c> lists each part's slot names.
	/// </summary>
	public static IReadOnlyList<(Vertex[] Vertices, uint[] Indices, string[] Textures)> ConvertMeshParts( ModelFile.Mesh mesh, System.Numerics.Matrix4x4? bake = null )
	{
		if ( mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 || mesh.Materials.Length == 0 )
			return Array.Empty<(Vertex[], uint[], string[])>();
		if ( mesh.Materials.Length <= 16 )
			return new[] { (ConvertMesh( mesh, bake ), mesh.Indices.ToArray(), mesh.Materials.Select( material => material.Name ).ToArray()) };
		var parts = new List<(Vertex[], uint[], string[])>();
		for ( var first = 0; first < mesh.Materials.Length; first += 16 )
		{
			var count = Math.Min( 16, mesh.Materials.Length - first );
			var chunk = new ModelFile.Mesh
			{
				Name = mesh.Name,
				NodeIndex = mesh.NodeIndex,
				Vertices = mesh.Vertices.Select( vertex => new ModelFile.Vertex { Position = vertex.Position, TextureIndex = vertex.TextureIndex >= first && vertex.TextureIndex < first + count ? vertex.TextureIndex - (uint)first : 0 } ).ToArray(),
				Normals = mesh.Normals,
				TexCoords = mesh.TexCoords,
				Materials = mesh.Materials.Skip( first ).Take( count ).ToArray(),
				Indices = Enumerable.Range( 0, mesh.Indices.Length / 3 )
					.Where( face => mesh.Indices.Skip( face * 3 ).Take( 3 ).All( index => index < mesh.Vertices.Length && mesh.Vertices[index].TextureIndex >= first && mesh.Vertices[index].TextureIndex < first + count ) )
					.SelectMany( face => mesh.Indices.Skip( face * 3 ).Take( 3 ) ).ToArray()
			};
			if ( chunk.Indices.Length > 0 )
				parts.Add( (ConvertMesh( chunk, bake ), chunk.Indices, chunk.Materials.Select( material => material.Name ).ToArray()) );
		}
		return parts;
	}

	private static bool IsFinite( System.Numerics.Vector3 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z );

	/// <summary>Directories searched for an object's textures: its archive, then the theme's shared textures (game data).</summary>
	// [APPROX:RIDES-022] Texture search: archive textures, gtexture, theme sharetex (stexture/ssharete low-detail sets unused) — evidence needed: binary texture lookup order
	public static IEnumerable<(BaseFileSystem FileSystem, string Directory)> TextureDirectories( ObjectCatalogEntry entry )
	{
		yield return (entry.FileSystem, $"{entry.ArchivePath}/textures");
		yield return (entry.FileSystem, $"{entry.ArchivePath}/gtexture");
		yield return (FileSystem, $"/levels/{entry.Theme}/sharetex");
	}

	/// <summary>Finds an MD2 material's texture (.wct) by name, or null when no searched directory has it.</summary>
	public static (BaseFileSystem FileSystem, string Path)? ResolveTexture( ObjectCatalogEntry entry, string textureName )
	{
		var name = Path.GetFileNameWithoutExtension( textureName.TrimEnd( '\0' ) );
		foreach ( var (fileSystem, directory) in TextureDirectories( entry ) )
		{
			var path = List( fileSystem, directory ).FirstOrDefault( file => string.Equals( Path.GetFileNameWithoutExtension( file ), name, StringComparison.OrdinalIgnoreCase )
				&& !file.EndsWith( ".txt", StringComparison.OrdinalIgnoreCase ) );
			if ( path != null )
				return (fileSystem, path);
		}
		return null;
	}

	public static string? ResolveTexturePath( ObjectCatalogEntry entry, string textureName ) => ResolveTexture( entry, textureName )?.Path;

	private static string[] List( BaseFileSystem fileSystem, string directory )
	{
		var key = fileSystem.GetAbsolutePath( directory );
		lock ( listings )
		{
			if ( listings.TryGetValue( key, out var files ) )
				return files;
			try
			{
				files = fileSystem.GetFiles( directory ).Select( file => fileSystem.GetRelativePath( fileSystem.GetAbsolutePath( file ) ) ).ToArray();
			}
			catch ( Exception exception ) when ( exception is DirectoryNotFoundException or FileNotFoundException or IOException )
			{
				files = Array.Empty<string>();
			}
			listings[key] = files;
			return files;
		}
	}
}
