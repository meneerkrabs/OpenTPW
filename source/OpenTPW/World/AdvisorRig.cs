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
		public Matrix4x4? Uploaded { get; set; }
	}

	private static readonly Regex ModelName = new( @"^Advisor(?<suffix>_[A-Za-z]+)?\.MD2$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant );

	private readonly List<Part> parts = new();
	private HashSet<string>? members;
	private readonly Dictionary<int, ModelAnimation?> clips = new();
	private readonly Dictionary<int, ModelAnimationPlayer?> players = new();
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

	/// <summary>The global advisor (<c>/global/advisor/Advisor.MD2</c>).</summary>
	public static AdvisorRig LoadGlobal() => Load( Advisor.ArchivePath, "Advisor.MD2", "", required: true )!;

	/// <summary>The level's advisor model, or null when the level has none (or it cannot be read; logged).</summary>
	public static AdvisorRig? LoadLevel( BaseFileSystem fileSystem, string level )
	{
		ArgumentNullException.ThrowIfNull( fileSystem );
		var archive = $"/levels/{level}/advisor";
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
				// [BIN:TPW-EXE:0x00566957 device init] the only Direct3D CULLMODE write is D3DCULL_NONE; no other constant-state SetRenderState sets it and the register-driven setter 0x567A10 only toggles ALPHABLENDENABLE, so the original draws both faces
				var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader", MaterialFlags.DoubleSided );
				material.Set( "Color", textureSlots );
				// [APPROX:ADVISOR-007] Drawn double-sided as the original's Direct3D device does (no hardware culling); the mesh path has no back-face test (0x57A750 defer/skip/matrix select, 0x57AA10 per-vertex setup, 0x574110 through 0x574660 light vectors and colours), but the final submit 0x581CD0 -> [0x8BCBB8] vtable +8 is unread — evidence needed: that backend or a capture
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
		return rig;
	}

	/// <summary>Writes a part's vertices for the node matrix <paramref name="world"/>, unless they already hold it.</summary>
	public void Write( Part part, Matrix4x4 world )
	{
		if ( part.Uploaded == world )
			return;
		var vertices = part.Vertices;
		for ( var index = 0; index < vertices.Length; index++ )
		{
			vertices[index].Position = System.Numerics.Vector3.Transform( part.Positions[index], world );
			var normal = System.Numerics.Vector3.TransformNormal( part.Normals[index], world );
			vertices[index].Normal = normal.LengthSquared() > 0 ? System.Numerics.Vector3.Normalize( normal ) : normal;
		}
		part.Model.UpdateVertices( vertices );
		part.Uploaded = world;
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
	/// rotation and scale of their nodes; vertex tracks are not played, ADVISOR-006); the bind pose when the clip is missing.
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
			return Rest;
		// [APPROX:ADVISOR-006] The clips' node tracks (translation, rotation, scale) and blink toggles are played; their vertex tracks (antennae and hands), the texture-coordinate tracks of the level advisors and the 12-byte block are not, and a clip the archive lacks leaves the bind pose — evidence needed: a skinned draw of the vertex tracks and a reader of the toggled mesh-node bit (mesh record +0, toggled at 0x472E75/0x472401; the draw-path bit 0x10 at 0x57A797 is a render-node deferral marker, RIDES-042)
		player.SetTick( tick );
		player.ComputeWorldTransforms( worlds );
		return worlds;
	}

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		foreach ( var part in parts )
			part.Model.ReleaseBuffers();
		parts.Clear();
	}
}
