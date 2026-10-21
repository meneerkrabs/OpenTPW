using System.Numerics;
using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>
/// One advisor model as the original keeps it in a model slot: the global <c>Advisor.MD2</c> (slot 0) or the level's
/// <c>Advisor_&lt;XXX&gt;.MD2</c> (slot 1), every mesh of it as a drawable part (not only the bodies: hats, props and blink
/// meshes too), the node hierarchy, and the clips that animate it (<c>Advisor&lt;suffix&gt;m&lt;n&gt;.MD2</c> in the same archive).
/// A part's vertices are written per draw from the node matrix it follows (rest pose or the playing clip), as the original's
/// mesh draw transforms them.
/// </summary>
internal sealed class AdvisorRig : IDisposable
{
	/// <summary>The advisor meshes' material flags: back faces culled (the front faces run clockwise on the screen), as the original's face test 0x581320 drops them.</summary>
	internal const MaterialFlags MeshMaterialFlags = MaterialFlags.None;

	/// <summary>One mesh: its vertices in node space and the GPU model they are written to.</summary>
	internal sealed class Part
	{
		public required string Name { get; init; }
		public required int NodeIndex { get; init; }
		public required Model Model { get; init; }
		public required Material<ObjectUniformBuffer> Material { get; init; }
		public required Vertex[] Vertices { get; init; }
		public required System.Numerics.Vector3[] Positions { get; init; }
		public required System.Numerics.Vector3[] Normals { get; init; }
		/// <summary>The mesh as stored (its positions and corners index the clips' vertex and texture-coordinate tracks).</summary>
		public required ModelFile.Mesh Mesh { get; init; }
		/// <summary>The stored texture coordinates of the drawn vertices, which a part shows when no clip animates them.</summary>
		public required System.Numerics.Vector2[] TexCoords { get; init; }
		public Matrix4x4? Uploaded { get; set; }
		/// <summary>The clip and tick whose vertex and texture-coordinate tracks the uploaded vertices hold; (0, 0) for the stored mesh.</summary>
		public (int Clip, float Tick) UploadedSource { get; set; }
		internal System.Numerics.Vector3[]? SampledPositions { get; set; }
		internal System.Numerics.Vector2[]? SampledTexCoords { get; set; }
	}

	/// <summary>The vertex (set mode) and texture-coordinate tracks a clip plays on one mesh node.</summary>
	internal sealed record MeshTracks( ModelVertexAnimation? Vertices, ModelUvAnimation? TexCoords );

	private static readonly Regex ModelName = new( @"^Advisor(?<suffix>_[A-Za-z]+)?\.MD2$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant );

	private readonly List<Part> parts = new();
	private HashSet<string>? members;
	private readonly Dictionary<int, ModelAnimation?> clips = new();
	private readonly Dictionary<int, ModelAnimationPlayer?> players = new();
	private readonly Dictionary<int, MeshTracks?[]?> meshTracks = new();
	private readonly Matrix4x4[] worlds;
	private bool disposed;

	private AdvisorRig( string archive, string suffix, ModelFile file )
	{
		Archive = archive;
		Suffix = suffix;
		File = file;
		Rest = ModelAnimationPlayer.ComputeRestTransforms( file );
		Nodes = AdvisorNodeMap.From( file );
		worlds = new Matrix4x4[file.Nodes.Count];
	}

	/// <summary>The archive path (<c>/global/advisor</c> or <c>/levels/&lt;level&gt;/advisor</c>).</summary>
	public string Archive { get; }
	/// <summary>The suffix of the model's and clips' names (<c>_JUN</c>; empty for the global advisor).</summary>
	public string Suffix { get; }
	public ModelFile File { get; }
	/// <summary>The nodes, meshes and dummy attributes the accessory selector reads.</summary>
	public AdvisorNodeMap Nodes { get; }
	/// <summary>Model-space matrices of the bind pose.</summary>
	public Matrix4x4[] Rest { get; }
	public IReadOnlyList<Part> Parts => parts;

	/// <summary>The member name of the model in an archive's file list, or null when the archive has none (clips are <c>...m&lt;n&gt;</c> and do not match).</summary>
	internal static (string Member, string Suffix)? FindModel( IEnumerable<string> members )
	{
		foreach ( var member in members )
		{
			var match = ModelName.Match( Path.GetFileName( member ) );
			if ( match.Success )
				return (Path.GetFileName( member ), match.Groups["suffix"].Value);
		}
		return null;
	}

	/// <summary>The global advisor (<c>/global/advisor/Advisor.MD2</c>); the one a park build prepared, when there is one (<see cref="AdvisorEntryPreparation"/>).</summary>
	public static AdvisorRig LoadGlobal() =>
		AdvisorEntryPreparation.TryTake<AdvisorRig>( AdvisorEntryPreparation.GlobalRigKey, FileSystem, out var prepared ) && prepared != null ? prepared : LoadGlobalModel();

	/// <summary>
	/// Makes a material of the advisor's shader and releases it: the GL program of that shader, which the browser's renderer keeps for later materials,
	/// is then compiled in a build step of its own instead of inside the long step that reads the advisor's model (docs/BROWSER-PERFORMANCE.md, "Build steps").
	/// </summary>
	internal static void WarmShader() => new Material<ObjectUniformBuffer>( "content/shaders/advisor.shader", MeshMaterialFlags ).ReleaseOwnedResources();

	/// <summary>Reads the global advisor's model.</summary>
	internal static AdvisorRig LoadGlobalModel() => Load( Advisor.ArchivePath, "Advisor.MD2", "", required: true )!;

	/// <summary>The archive of a level's advisor model and clips.</summary>
	internal static string LevelArchive( string level ) => $"/levels/{level}/advisor";

	/// <summary>The level's advisor model, or null when the level has none (or it cannot be read; logged).</summary>
	public static AdvisorRig? LoadLevel( BaseFileSystem fileSystem, string level )
	{
		ArgumentNullException.ThrowIfNull( fileSystem );
		var archive = LevelArchive( level );
		try
		{
			if ( FindModel( fileSystem.GetFiles( archive ) ) is not { } model )
				return null;
			return Load( archive, model.Member, model.Suffix, required: false );
		}
		catch ( Exception exception ) when ( exception is IOException or DirectoryNotFoundException or InvalidDataException or NotSupportedException or FileNotFoundException or UnauthorizedAccessException )
		{
			Log.Warning( $"The advisor model of level {level} could not be loaded: {exception.Message}" );
			return null;
		}
	}

	private static AdvisorRig? Load( string archive, string member, string suffix, bool required )
	{
		var file = new ModelFile( $"{archive}/{member}" );
		if ( required )
		{
			// The advisor cannot be drawn without its body, head, eyes and mouths.
			foreach ( var name in Advisor.BodyMeshes.Concat( Advisor.Mouths ) )
				if ( !file.Meshes.Any( mesh => mesh.Name == name ) )
					throw new InvalidDataException( $"The original advisor model has no '{name}' mesh." );
		}
		var rig = new AdvisorRig( archive, suffix, file );
		var textures = new Dictionary<string, Texture>( StringComparer.OrdinalIgnoreCase );
		// A level advisor's textures: its own, then the global advisor's (the level models share the eyes' and mouths' art)
		var textureFiles = new List<string>();
		foreach ( var directory in required ? new[] { $"{archive}/textures" } : new[] { $"{archive}/textures", $"{Advisor.ArchivePath}/textures" } )
		{
			try
			{
				textureFiles.AddRange( FileSystem.GetFiles( directory ) );
			}
			catch ( Exception exception ) when ( !required && exception is IOException or DirectoryNotFoundException or UnauthorizedAccessException )
			{
				// no textures folder here
			}
		}
		try
		{
			foreach ( var mesh in file.Meshes )
			{
				var vertices = PrototypeRide.ConvertMesh( mesh );
				var textureSlots = new Texture[16];
				Array.Fill( textureSlots, Texture.Missing );
				for ( var slot = 0; slot < mesh.Materials.Length; slot++ )
				{
					var textureName = mesh.Materials[slot].Name;
					var path = textureFiles.FirstOrDefault( entry => string.Equals( Path.GetFileNameWithoutExtension( entry ), textureName, StringComparison.OrdinalIgnoreCase ) );
					if ( path == null )
					{
						// The base meshes must have their art; a missing accessory texture is only a plain mesh.
						if ( AdvisorAccessories.IsBaseMesh( mesh.Name ) )
							throw new FileNotFoundException( $"Original advisor texture was not found: {textureName}" );
						Log.Warning( $"Advisor texture {textureName} of {archive}/{member} was not found; its mesh {mesh.Name} is drawn untextured." );
						continue;
					}
					if ( !textures.TryGetValue( path, out var texture ) )
					{
						texture = new Texture( path, TextureFlags.Repeat );
						textures.Add( path, texture );
					}
					textureSlots[slot] = texture;
				}
				// [BIN:TPW-EXE:0x581320 face test] the engine drops a triangle itself unless its corners run clockwise on the screen: the batcher's primitive builder
				// 0x580530 (the only caller of 0x581320, reached from the scene draw 0x576a00 → 0x582170 → 0x581db0) tests the determinant of each triangle's x, y
				// and w after the advisor's own view (diag(1, -1, 1, 1), 0x57a260) and the viewport 0x579690, which scales y by the positive half height (0x582f70),
				// so on the y-down screen a positive determinant means the corners run clockwise; the Direct3D cull mode D3DCULL_NONE (0x566957) is consistent with
				// that, as the engine has already dropped those faces; so the material culls the faces that run anticlockwise on the screen (front faces clockwise,
				// as for the park's objects)
				var material = new Material<ObjectUniformBuffer>( "content/shaders/advisor.shader", MeshMaterialFlags );
				material.Set( "Color", textureSlots );
				// [BIN:TPW-EXE:0x581CD0 mesh submit] the submit (its only caller is 0x57a530) queues the primitive (at most 0x1000, queue [0x8BCBB8]) after transforming its vertices through [0x87B1D8] vtable +8, the per-CPU 4x4 transform chosen at 0x5781D0 (x87 0x579250, 3DNow 0x5F4730, 0x5F4F81); the scene draw flushes that queue through 0x581DB0, which orders by state and texture, and builds each primitive in 0x580530, which applies the face test above
				var positions = new System.Numerics.Vector3[mesh.Vertices.Length];
				var normals = new System.Numerics.Vector3[mesh.Vertices.Length];
				for ( var index = 0; index < positions.Length; index++ )
				{
					positions[index] = mesh.Vertices[index].Position.GetSystemVector3();
					normals[index] = mesh.Normals[index].GetSystemVector3();
				}
				var part = new Part
				{
					Name = mesh.Name,
					NodeIndex = mesh.NodeIndex,
					Model = new Model( vertices, mesh.Indices, material, dynamicVertices: true ),
					Material = material,
					Vertices = vertices,
					Positions = positions,
					Normals = normals,
					Mesh = mesh,
					TexCoords = vertices.Select( vertex => new System.Numerics.Vector2( vertex.TexCoords.X, vertex.TexCoords.Y ) ).ToArray(),
				};
				rig.parts.Add( part );
				rig.Write( part, rig.Rest[mesh.NodeIndex] );
			}
		}
		catch
		{
			rig.Dispose();
			throw;
		}
		rig.Preload();
		return rig;
	}

	/// <summary>
	/// Writes a part's vertices for the node matrix <paramref name="world"/>, with the vertex and texture-coordinate tracks clip
	/// <paramref name="clip"/> plays on its node at <paramref name="tick"/> (the stored mesh without a clip or a track), unless they
	/// already hold it.
	/// </summary>
	// [BIN:TPW-EXE:0x00429DF0 advisor model draw] the advisor's model slots (table 0x790398, stride 0xB0, the table "Kill advisor" 0x429D60 stops) are drawn each frame by the shared instance draw 0x473C70(instance, 1, 0), which runs the channel sampler 0x4735D0 -> 0x471860 for each animation channel of the instance (count word +0xE, stride 0x38, 0x473D1A..0x473D36): the vertex pass (set mode, the advisor models' header flag 4 is clear) and the texture-coordinate pass 0x4745C0, as for objects (ObjectAnimator)
	// The advisor models' normals are the stored ones: set mode does not recompute them (ObjectAnimator.GetVertexLimitation)
	public void Write( Part part, Matrix4x4 world, int clip = 0, float tick = 0 )
	{
		var tracks = clip > 0 ? Tracks( clip, part.NodeIndex ) : null;
		var source = tracks == null ? (0, 0f) : (clip, tick);
		if ( part.Uploaded == world && part.UploadedSource == source )
			return;
		System.Numerics.Vector3[]? positions = null;
		if ( tracks?.Vertices is { } vertexTrack )
		{
			positions = part.SampledPositions ??= new System.Numerics.Vector3[part.Mesh.Positions.Length];
			vertexTrack.ApplyPose( tick, positions );
		}
		System.Numerics.Vector2[]? texCoords = null;
		if ( tracks?.TexCoords is { } uvTrack )
		{
			texCoords = part.SampledTexCoords ??= new System.Numerics.Vector2[part.Mesh.TexCoords.Length];
			uvTrack.Apply( tick, texCoords );
		}
		var vertices = part.Vertices;
		var corners = part.Mesh.CornerPositionIndices;
		for ( var index = 0; index < vertices.Length; index++ )
		{
			var position = positions != null && index < corners.Length ? positions[corners[index]] : part.Positions[index];
			vertices[index].Position = System.Numerics.Vector3.Transform( position, world );
			var normal = System.Numerics.Vector3.TransformNormal( part.Normals[index], world );
			vertices[index].Normal = normal.LengthSquared() > 0 ? System.Numerics.Vector3.Normalize( normal ) : normal;
			var coordinates = texCoords != null && index < texCoords.Length ? texCoords[index] : part.TexCoords[index];
			vertices[index].TexCoords = new Vector2( coordinates.X, coordinates.Y );
		}
		part.Model.UpdateVertices( vertices );
		part.Uploaded = world;
		part.UploadedSource = source;
	}

	/// <summary>The vertex and texture-coordinate tracks clip <paramref name="number"/> plays on mesh node <paramref name="node"/>; null when it plays none (or does not fit the mesh).</summary>
	internal MeshTracks? Tracks( int number, int node )
	{
		if ( !meshTracks.TryGetValue( number, out var byNode ) )
		{
			byNode = Clip( number ) is { } clip ? FindMeshTracks( File, clip, number, Archive ) : null;
			meshTracks[number] = byNode;
		}
		return byNode != null && (uint)node < byNode.Length ? byNode[node] : null;
	}

	/// <summary>
	/// The vertex and texture-coordinate tracks of <paramref name="clip"/> by mesh node of <paramref name="file"/>, with the fit the object
	/// animator requires (a vertex block for the mesh's positions, a texture-coordinate block for its corners); a track that does not fit is
	/// logged and not played.
	/// </summary>
	internal static MeshTracks?[] FindMeshTracks( ModelFile file, ModelAnimation clip, int number = 0, string? archive = null )
	{
		var byNode = new MeshTracks?[file.Nodes.Count];
		foreach ( var track in clip.Tracks )
		{
			if ( (uint)track.NodeIndex >= byNode.Length || file.Meshes.FirstOrDefault( mesh => mesh.NodeIndex == track.NodeIndex ) is not { } mesh )
				continue;
			var vertices = track.VertexAnimation is { } animation && animation.GetPoseLimitation( mesh.Positions.Length, clip.Duration ) == null ? animation : null;
			var texCoords = track.UvAnimation is { } uv && uv.CornerCount == mesh.TexCoords.Length ? uv : null;
			if ( track.VertexAnimation != null && vertices == null || track.UvAnimation != null && texCoords == null )
				Log.Warning( $"Advisor clip {number} of {archive}: a vertex or texture-coordinate track of node {track.NodeIndex} does not fit its mesh and is not played." );
			if ( vertices != null || texCoords != null )
				byNode[track.NodeIndex] = new MeshTracks( vertices, texCoords );
		}
		return byNode;
	}

	/// <summary>Loads the clips 1-20 and their players now, so that playing one never reads a file in the render path.</summary>
	private void Preload()
	{
		for ( var number = 1; number <= AdvisorClipDurations.Count; number++ )
			Pose( number, 0 );
	}

	/// <summary>Clip <paramref name="number"/> of this model's archive; null when the archive lacks it or it cannot be read.</summary>
	public ModelAnimation? Clip( int number )
	{
		if ( clips.TryGetValue( number, out var cached ) )
			return cached;
		ModelAnimation? clip = null;
		try
		{
			members ??= FileSystem.GetFiles( Archive ).Select( Path.GetFileName ).ToHashSet( StringComparer.OrdinalIgnoreCase )!;
			var name = Advisor.AnimationClipName( Suffix, number );
			if ( members.Contains( name ) )
				clip = new ModelFile( $"{Archive}/{name}" ).Clip;
		}
		catch ( Exception exception ) when ( exception is IOException or DirectoryNotFoundException or InvalidDataException or NotSupportedException or FileNotFoundException or UnauthorizedAccessException )
		{
			Log.Trace( $"Advisor clip {number} of {Archive} is not available: {exception.Message}" );
		}
		clips[number] = clip;
		return clip;
	}

	/// <summary>
	/// The node matrices of clip <paramref name="number"/> at <paramref name="tick"/> (the clip's tracks replace the stored translation,
	/// rotation and scale of their nodes; their vertex and texture-coordinate tracks are written per part, <see cref="Write"/>); the bind
	/// pose when the clip is missing. <see cref="SampledTick"/> is the tick the clip was sampled at (clamped to its length).
	/// </summary>
	public Matrix4x4[] Pose( int number, float tick )
	{
		if ( !players.TryGetValue( number, out var player ) )
		{
			player = null;
			if ( Clip( number ) is { } clip )
			{
				try
				{
					player = new ModelAnimationPlayer( File, clip, AdvisorTimeline.TicksPerSecond ) { Loop = false };
				}
				catch ( Exception exception ) when ( exception is InvalidDataException or ArgumentException )
				{
					Log.Warning( $"Advisor clip {number} of {Archive} does not fit its model: {exception.Message}" );
				}
			}
			players[number] = player;
		}
		if ( player == null )
		{
			SampledTick = 0;
			return Rest;
		}
		// [BIN:TPW-EXE:0x00472F60 animation start] a clip the model's archive lacks has no loaded flag in its slot ((slot + 2) * 12, bits 3): the start clears the animation (current clip 0xC = none, the previous one ends through 0x472310) and 0x4732A0 still returns the 1000 ms floor, so the model keeps its rest pose; the node tracks, blink toggles, vertex tracks (antennae, hands, eyes) and texture-coordinate tracks (the level advisors' eyes) of a clip it has are played, and a toggled mesh-node bit hides that mesh in the draw walk (0x57A5E3 tests 0x8210)
		player.SetTick( tick );
		SampledTick = player.Tick;
		player.ComputeWorldTransforms( worlds );
		return worlds;
	}

	/// <summary>The tick the last <see cref="Pose"/> sampled its clip at (0 for the bind pose).</summary>
	public float SampledTick { get; private set; }

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		foreach ( var part in parts )
		{
			part.Model.ReleaseBuffers();
			// The material owns its pipeline, scratch buffer and shader reference.
			part.Material.ReleaseOwnedResources();
		}
		parts.Clear();
	}
}
