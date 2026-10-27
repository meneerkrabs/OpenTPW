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
			if ( !IsFinite( position ) || !IsFinite( normal ) )
				throw new InvalidDataException( $"Nonfinite object mesh vertex: {mesh.Name}" );
			// [EXT:malformed-texture-coordinates] Non-finite texture coordinates (10 vertices of the Theme Park Inc natnav "subtank") are drawn as (0, 0): the original passes the raw floats to Direct3D (undefined behaviour), so there is no rule to recover and this is OpenTPW's handling of malformed data (formerly RIDES-030)
			var texCoords = mesh.TexCoords[vertexIndex];
			if ( !float.IsFinite( texCoords.X ) || !float.IsFinite( texCoords.Y ) )
				texCoords = default;
			vertices[vertexIndex] = new Vertex
			{
				Position = ConvertAxes( position ),
				Normal = ConvertAxes( normal ),
				TexCoords = texCoords,
				TexIndex = (int)sourceVertex.TextureIndex,
				MatFlags = TextureSlotAddressing.VertexFlags( mesh.Materials[(int)sourceVertex.TextureIndex] )
					| ( TextureSlotAddressing.IsPathLikeTexture( mesh.Materials[(int)sourceVertex.TextureIndex].Name ) ? TextureSlotAddressing.SlushFlag : 0u )
			};
		}
		return vertices;
	}

	/// <summary>
	/// Converts a mesh into drawable parts with at most 16 texture slots each (the shader's limit). Meshes with
	/// more slots (31 large floors) are split by material: every corner carries its material, so faces never
	/// span parts. Vertex texture indices are renumbered per part; <c>Textures</c> lists each part's slot names
	/// and <c>Slots</c> the MD2 texture-slot index each name came from (-1 for untextured materials), so a
	/// texture-frame track of that slot can swap the rendered frame.
	/// </summary>
	public static IReadOnlyList<(Vertex[] Vertices, uint[] Indices, string[] Textures, int[] Slots)> ConvertMeshParts( ModelFile.Mesh mesh, System.Numerics.Matrix4x4? bake = null )
	{
		if ( mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 || mesh.Materials.Length == 0 )
			return Array.Empty<(Vertex[], uint[], string[], int[])>();
		if ( mesh.Materials.Length <= 16 )
			return new[] { (ConvertMesh( mesh, bake ), mesh.Indices.ToArray(), mesh.Materials.Select( material => material.Name ).ToArray(),
				mesh.Materials.Select( material => material.TextureIndex ).ToArray()) };
		var parts = new List<(Vertex[], uint[], string[], int[])>();
		foreach ( var (first, count) in PartMaterialRanges( mesh ) )
		{
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
				parts.Add( (ConvertMesh( chunk, bake ), chunk.Indices, chunk.Materials.Select( material => material.Name ).ToArray(),
					chunk.Materials.Select( material => material.TextureIndex ).ToArray()) );
		}
		return parts;
	}

	/// <summary>
	/// The runs of a mesh's materials that <see cref="ConvertMeshParts"/> makes a part of each, as first material and count: the whole mesh when it has at most 16 materials, else each
	/// run of 16 that some face uses wholly (all three corners carry a material of the run). None for a mesh without vertices, indices or materials. Their materials' names are the
	/// parts' <c>Textures</c>, so the names a model's parts draw can be known without converting it (<see cref="ObjectRenderParts.TextureNames"/>).
	/// </summary>
	internal static IEnumerable<(int First, int Count)> PartMaterialRanges( ModelFile.Mesh mesh )
	{
		if ( mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 || mesh.Materials.Length == 0 )
			yield break;
		if ( mesh.Materials.Length <= 16 )
		{
			yield return (0, mesh.Materials.Length);
			yield break;
		}
		for ( var first = 0; first < mesh.Materials.Length; first += 16 )
		{
			var count = Math.Min( 16, mesh.Materials.Length - first );
			if ( HasFaceOf( mesh, first, count ) )
				yield return (first, count);
		}
	}

	/// <summary>Whether a face of <paramref name="mesh"/> has every corner on a vertex of a material in <paramref name="first"/> .. <paramref name="first"/> + <paramref name="count"/>.</summary>
	private static bool HasFaceOf( ModelFile.Mesh mesh, int first, int count )
	{
		for ( var face = 0; face < mesh.Indices.Length / 3; face++ )
		{
			var whole = true;
			for ( var corner = 0; corner < 3 && whole; corner++ )
			{
				var index = mesh.Indices[face * 3 + corner];
				whole = index < mesh.Vertices.Length && mesh.Vertices[index].TextureIndex >= first && mesh.Vertices[index].TextureIndex < first + count;
			}
			if ( whole )
				return true;
		}
		return false;
	}

	private static bool IsFinite( System.Numerics.Vector3 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z );

	/// <summary>Test hook: true/false forces the low-detail texture set on/off, null follows the graphics settings.</summary>
	internal static bool? LowDetailTexturesOverride { get; set; }

	/// <summary>
	/// True when the active detail has TEXTUREQUALITY 0 or 1 (the Low preset's value; Medium has 2 and High 3): objects then take their
	/// textures from the archive's <c>stexture</c> and the theme's <c>ssharete</c> instead of <c>textures</c> and <c>sharetex</c>.
	/// Only for Theme Park World (the trace is of tp.exe; Theme Park Inc keeps its `textures` folders). The choice is made when an object's textures are looked up (the original picks once at startup); objects loaded earlier keep
	/// their textures when the setting changes.
	/// </summary>
	// [BIN:TPW-EXE:0x0054DCF0 low-detail texture set] startup calls 0x4688B0(low); low = 1 selects "stexture"/"ssharete" (0x7AF650/0x7AF720). Without dialog.tcf the operand is GraphicalOptions.TEXTUREQUALITY (0x785874, option row 1 of the table at 0x7442C0) being 0 or 1; with dialog.tcf (0x78D968) it is the saved preset flag 0x800 (the Low preset, 0x44DE20); low.sam sets TEXTUREQUALITY 1, med.sam 2 and high.sam 3
	public static bool UsesLowDetailTextures => LowDetailTexturesOverride
		?? (GameEditions.Current == GameEdition.ThemeParkWorld && !TexturePack.Enhanced && IGraphicsSettings.Instance is { } graphics
			&& (graphics.Current.Preset == GraphicsPreset.Custom || graphics.GetPresetValues( graphics.Current.Preset ) != null) && graphics.Detail.TextureQuality <= 1);

	/// <summary>Directories searched for an object's textures: its archive, then the theme's shared textures (game data).</summary>
	// [BIN:TPW-EXE:0x0046EAD0 texture lookup] 0x461a60 → 0x46e670 sets the model directory, <dir>\textures (<dir>\stexture in the low-detail set) and <theme>\sharetex (<theme>\ssharete) (0x46e650); the lookup 0x46ead0 tries <dir>\textures\<name> as .wct then .tga, then assigns the shared texture without an existence check, else logs "Could not load texture…"; GTexture is only set by the coaster loader 0x42fad0, so the archive's gtexture (12 track-ride archives) stays in both sets
	public static IEnumerable<(BaseFileSystem FileSystem, string Directory)> TextureDirectories( ObjectCatalogEntry entry )
	{
		// The theme's shared folders live in the game data (FileSystem), the archive's own in the entry's file system.
		// [EXT:cross-edition-objects] an object read from another game's install (a registered sound root) shares that install's textures: its archive path names the theme
		// folder it was authored in (Theme Park Inc's science objects use its levels/science/sharetex, not the Theme Park World theme they are listed under)
		var other = ContentSoundRoots.SourceOf( entry ) is { Theme: { } sourceTheme } ? sourceTheme : null;
		foreach ( var name in TextureDirectoryNames( entry.ArchivePath, other ?? entry.Theme ) )
			yield return (name.StartsWith( entry.ArchivePath + "/", StringComparison.Ordinal ) || other != null ? entry.FileSystem : FileSystem, name);
	}

	/// <summary>The directory names of <see cref="TextureDirectories"/> for an archive and theme, in search order.</summary>
	public static IEnumerable<string> TextureDirectoryNames( string archivePath, string theme )
	{
		var low = UsesLowDetailTextures;
		// [EXT:low-detail-texture-fallback] the low sets are searched first, then the full folders: a few objects name textures that exist only in `textures`
		// (fantasy gokarts hivesign01/02, space gates Sen_sgn1/2, jungle totem tp_sign), where the original would find none. A selected texture pack replaces
		// the full textures by path, so with a pack the low set is not used at all (UsesLowDetailTextures).
		if ( low )
			yield return $"{archivePath}/stexture";
		yield return $"{archivePath}/textures";
		yield return $"{archivePath}/gtexture";
		if ( low )
			yield return $"/levels/{theme}/ssharete";
		yield return $"/levels/{theme}/sharetex";
	}

	/// <summary>Finds an MD2 material's texture (.wct) by name, or null when no searched directory has it.</summary>
	public static (BaseFileSystem FileSystem, string Path)? ResolveTexture( ObjectCatalogEntry entry, string textureName ) => ResolveTexture( entry, textureName, aheadOfUse: false );

	/// <summary>
	/// <see cref="ResolveTexture(ObjectCatalogEntry, string)"/>; <paramref name="aheadOfUse"/> for a lookup ahead of an object's own (a co-op build's type steps,
	/// <see cref="SavedTypePreparation"/>), which remembers a directory's listing and its absence as the object's lookup does, but not a read of it that failed otherwise: that one is
	/// left to the object's lookup, which may find the directory readable again.
	/// </summary>
	internal static (BaseFileSystem FileSystem, string Path)? ResolveTexture( ObjectCatalogEntry entry, string textureName, bool aheadOfUse )
	{
		var name = Path.GetFileNameWithoutExtension( textureName.TrimEnd( '\0' ) );
		foreach ( var (fileSystem, directory) in TextureDirectories( entry ) )
		{
			var path = List( fileSystem, directory, aheadOfUse ).FirstOrDefault( file => string.Equals( Path.GetFileNameWithoutExtension( file ), name, StringComparison.OrdinalIgnoreCase )
				&& !file.EndsWith( ".txt", StringComparison.OrdinalIgnoreCase ) );
			if ( path != null )
				return (fileSystem, path);
		}
		return null;
	}

	public static string? ResolveTexturePath( ObjectCatalogEntry entry, string textureName ) => ResolveTexture( entry, textureName )?.Path;

	private static string[] List( BaseFileSystem fileSystem, string directory, bool aheadOfUse = false )
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
				// a directory the data lacks stays lacking; a read that failed otherwise is remembered only on use (ResolveTexture's aheadOfUse)
				if ( aheadOfUse && exception is not (DirectoryNotFoundException or FileNotFoundException) )
					return Array.Empty<string>();
				files = Array.Empty<string>();
			}
			listings[key] = files;
			return files;
		}
	}
}
