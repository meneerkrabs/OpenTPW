using System.Text;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Builds a static version 221.203 geometry model from scratch and writes it with <see cref="ModelFileWriter"/>: nodes with a hierarchy
/// and parent-relative transforms (row-vector convention, translation in the fourth row), meshes of textured materials, texture slots
/// with the wrap/clamp flags and head records for a ride script's ADDHEAD. Animation members are out of scope.
/// <para>
/// Fields whose meaning is unknown get fixed defaults that are <b>unverified</b> against the original loader (see docs/MD2-MODELS.md,
/// "Writing and authoring"): header flags <see cref="DefaultHeaderFlags"/>, mesh node type 0x1, dummy node type 0x200, stored node index
/// = node index, slot word 0, material words 0, the opaque header words (<see cref="ModelFileWriter.OpaqueWord0C"/> and friends),
/// attribute type 0x1 / value 0 for a node without a head, and the layout planned by <see cref="ModelFileWriter"/>.
/// </para>
/// <para>
/// Mesh nodes come before dummy nodes in the written model (the file format keeps two tables), each group in the order it was added.
/// </para>
/// </summary>
public sealed class ModelBuilder
{
	/// <summary>The most common geometry header flags in the corpus (517 of 838 members); the bits are not interpreted.</summary>
	public const uint DefaultHeaderFlags = 1;
	/// <summary>Texture slot flag set by every authored slot (corpus: every slot but the multi-frame ones carries 0x1).</summary>
	public const uint SlotBaseFlags = 0x1;
	/// <summary>Texture slot flags 0x10 and 0x20 make a texture wrap (tp.exe 0x462F80, 0x567620); without them it clamps. Both are set, as in 953 corpus slots.</summary>
	public const uint SlotWrapFlags = 0x30;
	/// <summary>The default head record type: 0xB1 (487 corpus records; flag 0x80 = head record, 0x30 = has a world matrix).</summary>
	public const uint DefaultHeadType = 0xB1;
	/// <summary>The longest texture name without its <c>.tga</c> extension: the slot name field is 20 bytes with a terminating NUL.</summary>
	public const int MaximumTextureNameLength = 15;
	/// <summary>Material word +0xC bit 0x1: the mesh is drawn in one constant colour instead of per-vertex lighting (tp.exe 0x580C51; water, signs, glow). 920 of 12,951 corpus materials set it.</summary>
	public const uint SelfLitMaterialFlag = 0x1;
	public const uint MeshNodeType = 0x1;
	public const uint DummyNodeType = 0x200;

	private readonly List<ModelNodeBuilder> nodes = new();

	/// <summary>The 20-byte source name of the header (usually the authoring file's name); may be empty.</summary>
	public string SourceName { get; set; } = "";
	public uint HeaderFlags { get; set; } = DefaultHeaderFlags;
	/// <summary>Header float +0xA0: the height above the terrain the original's height sampler adds; 0 for most models.</summary>
	public float HeaderHeight { get; set; }
	public IReadOnlyList<ModelNodeBuilder> Nodes => nodes;

	/// <summary>Adds a mesh node (it gets a <see cref="ModelNodeBuilder.Mesh"/>). Exactly one node of the model has no parent.</summary>
	public ModelNodeBuilder AddMesh( string name, ModelNodeBuilder? parent = null, NMatrix4x4? local = null )
		=> Add( new ModelNodeBuilder( this, name, parent, local, true ) );

	/// <summary>Adds a dummy node (a seat, an attachment point, a group).</summary>
	public ModelNodeBuilder AddDummy( string name, ModelNodeBuilder? parent = null, NMatrix4x4? local = null )
		=> Add( new ModelNodeBuilder( this, name, parent, local, false ) );

	private ModelNodeBuilder Add( ModelNodeBuilder node )
	{
		nodes.Add( node );
		return node;
	}

	/// <summary>The member as bytes.</summary>
	public byte[] ToBytes() => ModelFileWriter.Write( BuildShell() );

	/// <summary>The member, written and read back by the ordinary <see cref="ModelFile"/> reader (so it is validated like an original member).</summary>
	public ModelFile Build() => new( new MemoryStream( ToBytes() ) );

	private ModelFile BuildShell()
	{
		if ( nodes.Count == 0 )
			throw new InvalidOperationException( "A model needs at least one node." );
		var roots = nodes.Where( node => node.Parent == null ).ToArray();
		if ( roots.Length != 1 )
			throw new InvalidOperationException( $"A model has exactly one root node; found {roots.Length}." );
		foreach ( var node in nodes )
			CheckPrintable( node.Name, "Node name", 255 );

		// Node order: meshes first, then dummies, each in the order added.
		var ordered = nodes.Where( node => node.IsMesh ).Concat( nodes.Where( node => !node.IsMesh ) ).ToList();
		var indexOf = new Dictionary<ModelNodeBuilder, int>();
		for ( var index = 0; index < ordered.Count; index++ )
			indexOf[ordered[index]] = index;
		var meshCount = ordered.Count( node => node.IsMesh );

		var world = new Dictionary<ModelNodeBuilder, NMatrix4x4>();
		NMatrix4x4 World( ModelNodeBuilder node )
		{
			if ( world.TryGetValue( node, out var cached ) )
				return cached;
			var value = node.Parent == null ? node.Transform : node.Transform * World( node.Parent );
			world[node] = value;
			return value;
		}

		var textures = new List<ModelTexture>();
		var slotOf = new Dictionary<(string Name, bool Wrap), int>();
		var meshes = new List<ModelFile.Mesh>();
		var modelNodes = new ModelNode[ordered.Count];
		NVector3 boundsMin = new( float.PositiveInfinity ), boundsMax = new( float.NegativeInfinity );
		for ( var index = 0; index < ordered.Count; index++ )
		{
			var node = ordered[index];
			modelNodes[index] = new ModelNode( index, node.Type, (uint)index, node.Name, node.Parent == null ? -1 : indexOf[node.Parent], index < meshCount ? index : -1, node.Transform );
			if ( !node.IsMesh )
				continue;
			var mesh = BuildMesh( node, textures, slotOf );
			meshes.Add( mesh );
			var composed = World( node );
			foreach ( var position in mesh.Positions )
			{
				var moved = NVector3.Transform( new NVector3( position.X, position.Y, position.Z ), composed );
				boundsMin = NVector3.Min( boundsMin, moved );
				boundsMax = NVector3.Max( boundsMax, moved );
			}
		}
		if ( float.IsInfinity( boundsMin.X ) )
			boundsMin = boundsMax = NVector3.Zero;

		var attributes = BuildAttributes( ordered, indexOf, meshCount, out var unknown46 );
		var model = new ModelFile
		{
			SourceName = SourceName,
			HeaderFlags = HeaderFlags,
			HeaderHeight = HeaderHeight,
			BoundsMin = new Vector3( boundsMin.X, boundsMin.Y, boundsMin.Z ),
			BoundsMax = new Vector3( boundsMax.X, boundsMax.Y, boundsMax.Z ),
			Textures = textures,
			Nodes = modelNodes,
			RootNodeIndex = indexOf[roots[0]],
			Meshes = meshes,
			DummyAttributes = attributes,
			ExtraRecords = Array.Empty<byte[]>(),
			Paths = Array.Empty<ModelPath>(),
			Counts = new ModelHeaderCounts( 0, 0, 0, 0, 0, 0, 0, 0, unknown46, 0, 0 )
		};
		model.Counts = ModelFileWriter.ComputeCounts( model );
		return model;
	}

	private static ModelFile.Mesh BuildMesh( ModelNodeBuilder node, List<ModelTexture> textures, Dictionary<(string Name, bool Wrap), int> slotOf )
	{
		var source = node.Mesh!;
		var positions = new List<Vector3>();
		var positionOf = new Dictionary<(int, int, int), ushort>();
		var cornerPositions = new List<ushort>();
		var texCoords = new List<Vector2>();
		var cornerNormals = new List<Vector3>();
		var indices = new List<uint>();
		var faceNormals = new List<Vector3>();
		var materials = new List<ModelFile.MaterialData>();
		foreach ( var material in source.Materials )
		{
			if ( material.Vertices.Count == 0 )
				throw new InvalidOperationException( $"Mesh '{node.Name}' has a material without vertices." );
			var firstCorner = cornerPositions.Count;
			var firstFace = indices.Count / 3;
			foreach ( var vertex in material.Vertices )
			{
				var key = (BitConverter.SingleToInt32Bits( vertex.Position.X ), BitConverter.SingleToInt32Bits( vertex.Position.Y ), BitConverter.SingleToInt32Bits( vertex.Position.Z ));
				if ( !positionOf.TryGetValue( key, out var position ) )
				{
					if ( positions.Count >= ushort.MaxValue )
						throw new InvalidOperationException( $"Mesh '{node.Name}' has more than {ushort.MaxValue} positions." );
					position = (ushort)positions.Count;
					positionOf[key] = position;
					positions.Add( new Vector3( vertex.Position.X, vertex.Position.Y, vertex.Position.Z ) );
				}
				cornerPositions.Add( position );
				texCoords.Add( new Vector2( vertex.TextureCoordinate.X, vertex.TextureCoordinate.Y ) );
				cornerNormals.Add( new Vector3( vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z ) );
			}
			if ( cornerPositions.Count > ushort.MaxValue )
				throw new InvalidOperationException( $"Mesh '{node.Name}' has more than {ushort.MaxValue} corners." );
			foreach ( var (a, b, c) in material.Triangles )
			{
				indices.Add( (uint)(firstCorner + a) );
				indices.Add( (uint)(firstCorner + b) );
				indices.Add( (uint)(firstCorner + c) );
				var p0 = material.Vertices[a].Position;
				var normal = NVector3.Cross( material.Vertices[b].Position - p0, material.Vertices[c].Position - p0 );
				faceNormals.Add( normal.LengthSquared() > 0 ? NVector3.Normalize( normal ).ToOpenTpw() : Vector3.Zero );
			}
			var slot = -1;
			string? stem = null;
			if ( material.TextureName != null )
			{
				stem = TextureStem( material.TextureName );
				var key = (stem.ToLowerInvariant(), material.Wrap);
				if ( !slotOf.TryGetValue( key, out slot ) )
				{
					slot = textures.Count;
					slotOf[key] = slot;
					textures.Add( new ModelTexture( SlotBaseFlags | (material.Wrap ? SlotWrapFlags : 0), 0, new[] { stem + ".tga" } ) );
				}
			}
			materials.Add( new ModelFile.MaterialData
			{
				b = (ushort)(indices.Count / 3 - firstFace),
				StartIndex = (ushort)firstCorner,
				EndIndex = (ushort)(cornerPositions.Count - 1),
				TextureIndex = slot,
				Name = stem ?? "",
				Flags = slot < 0 ? 0 : textures[slot].Flags,
				Pad = material.SelfLit ? SelfLitMaterialFlag : 0
			} );
		}
		if ( indices.Count / 3 > ushort.MaxValue )
			throw new InvalidOperationException( $"Mesh '{node.Name}' has more than {ushort.MaxValue} faces." );
		var corners = cornerPositions.Count;
		var faceNormalIndices = new ushort[faceNormals.Count];
		for ( var face = 0; face < faceNormalIndices.Length; face++ )
			faceNormalIndices[face] = (ushort)(corners + face);

		Vector3 min = positions.Count == 0 ? Vector3.Zero : positions[0], max = min;
		foreach ( var position in positions )
		{
			min = new Vector3( MathF.Min( min.X, position.X ), MathF.Min( min.Y, position.Y ), MathF.Min( min.Z, position.Z ) );
			max = new Vector3( MathF.Max( max.X, position.X ), MathF.Max( max.Y, position.Y ), MathF.Max( max.Z, position.Z ) );
		}
		return new ModelFile.Mesh
		{
			Name = node.Name,
			Positions = positions.ToArray(),
			CornerPositionIndices = cornerPositions.ToArray(),
			TexCoords = texCoords.ToArray(),
			CornerNormals = cornerNormals.ToArray(),
			FaceNormals = faceNormals.ToArray(),
			Indices = indices.ToArray(),
			FaceNormalIndices = faceNormalIndices,
			Materials = materials.ToArray(),
			BoundsMin = min,
			BoundsMax = max
		};
	}

	/// <summary>
	/// One attribute record per node from the first record's node to the last head node. Record i belongs to node Unknown46 + i
	/// (docs/GUESTS.md "Riders' heads"), so the table starts at the first dummy (or an earlier head node) and spans every node up to the
	/// last head; nodes without a head get a plain record (type 0x1, value 0, no head flag).
	/// </summary>
	private static ModelDummyAttribute[] BuildAttributes( List<ModelNodeBuilder> ordered, Dictionary<ModelNodeBuilder, int> indexOf, int meshCount, out ushort unknown46 )
	{
		var heads = ordered.Where( node => node.HeadId != null ).ToList();
		if ( heads.Count == 0 )
		{
			unknown46 = 0xFFFF;
			return Array.Empty<ModelDummyAttribute>();
		}
		var ids = heads.Select( node => node.HeadId!.Value ).OrderBy( id => id ).ToArray();
		for ( var index = 0; index < ids.Length; index++ )
		{
			if ( ids[index] != index + 1 )
				throw new InvalidOperationException( "Head ids must be unique and run from 1 without a gap (the ride script counts them from 1 until one is missing)." );
		}
		var first = Math.Min( meshCount, heads.Min( node => indexOf[node] ) );
		var last = heads.Max( node => indexOf[node] );
		unknown46 = (ushort)first;
		var attributes = new ModelDummyAttribute[last - first + 1];
		for ( var index = 0; index < attributes.Length; index++ )
		{
			var node = ordered[first + index];
			attributes[index] = node.HeadId is { } id
				? new ModelDummyAttribute( node.HeadType, (uint)id, new byte[12] )
				: new ModelDummyAttribute( 0x1, 0, new byte[12] );
		}
		return attributes;
	}

	internal static string TextureStem( string name )
	{
		var stem = name.EndsWith( ".tga", StringComparison.OrdinalIgnoreCase ) || name.EndsWith( ".wct", StringComparison.OrdinalIgnoreCase ) ? name[..^4] : name;
		CheckPrintable( stem, "Texture name", MaximumTextureNameLength );
		if ( stem.Length == 0 || stem.Contains( '/' ) || stem.Contains( '\\' ) )
			throw new InvalidOperationException( $"Texture name '{name}' must be a plain file name." );
		return stem;
	}

	internal static void CheckPrintable( string value, string what, int maximumLength )
	{
		if ( value.Length > maximumLength )
			throw new InvalidOperationException( $"{what} '{value}' is longer than {maximumLength} characters." );
		foreach ( var character in value )
		{
			if ( character < 0x20 || character > 0x7E )
				throw new InvalidOperationException( $"{what} '{value}' contains a character outside printable ASCII." );
		}
	}
}

/// <summary>A node of a <see cref="ModelBuilder"/>: name, parent, parent-relative transform and, for a mesh node, its geometry.</summary>
public sealed class ModelNodeBuilder
{
	internal ModelNodeBuilder( ModelBuilder owner, string name, ModelNodeBuilder? parent, NMatrix4x4? local, bool isMesh )
	{
		ArgumentNullException.ThrowIfNull( name );
		if ( parent != null && !owner.Nodes.Contains( parent ) )
			throw new ArgumentException( "The parent belongs to another model.", nameof( parent ) );
		Name = name;
		Parent = parent;
		Transform = local ?? NMatrix4x4.Identity;
		IsMesh = isMesh;
		Type = isMesh ? ModelBuilder.MeshNodeType : ModelBuilder.DummyNodeType;
		if ( isMesh )
			Mesh = new ModelMeshBuilder();
	}

	public string Name { get; }
	public ModelNodeBuilder? Parent { get; }
	/// <summary>The parent-relative matrix (row vectors, translation in the fourth row); the world matrix is <c>local × parent world</c>.</summary>
	public NMatrix4x4 Transform { get; set; }
	public bool IsMesh { get; }
	/// <summary>The node type word (0x1 mesh, 0x200 dummy by default; other corpus values 0x401, 0x2, 0x208, 0x300, 0x600 are not interpreted).</summary>
	public uint Type { get; set; }
	public ModelMeshBuilder? Mesh { get; }
	/// <summary>The head id of this node's head record (1..n), or null. A ride script's ADDHEAD hangs a guest head on the node of its head id.</summary>
	public int? HeadId { get; private set; }
	/// <summary>The head record's type word; flag 0x80 marks it as a head record.</summary>
	public uint HeadType { get; private set; } = ModelBuilder.DefaultHeadType;

	/// <summary>Makes this node carry head record <paramref name="headId"/>. A node has at most one head record; ids are unique and run from 1.</summary>
	public ModelNodeBuilder AddHead( int headId, uint type = ModelBuilder.DefaultHeadType )
	{
		if ( headId < 1 )
			throw new ArgumentOutOfRangeException( nameof( headId ), "Head ids start at 1." );
		if ( (type & 0x80) == 0 )
			throw new ArgumentException( "A head record type needs flag 0x80.", nameof( type ) );
		if ( HeadId != null )
			throw new InvalidOperationException( $"Node '{Name}' already has head record {HeadId}." );
		HeadId = headId;
		HeadType = type;
		return this;
	}
}

/// <summary>The geometry of a mesh node: materials, each with its own vertices and triangles (a corner belongs to one material).</summary>
public sealed class ModelMeshBuilder
{
	private readonly List<ModelMaterialBuilder> materials = new();

	public IReadOnlyList<ModelMaterialBuilder> Materials => materials;

	/// <summary>
	/// Adds a material. <paramref name="textureName"/> is the texture's file name with or without <c>.tga</c>/<c>.wct</c> (at most
	/// <see cref="ModelBuilder.MaximumTextureNameLength"/> characters); the game resolves it in <c>&lt;archive&gt;/textures/&lt;name&gt;.wct|.tga</c>.
	/// Null makes an untextured material. <paramref name="wrap"/> sets the slot's wrap flags (0x10/0x20), else the texture clamps.
	/// </summary>
	public ModelMaterialBuilder AddMaterial( string? textureName, bool wrap = false )
	{
		var material = new ModelMaterialBuilder( textureName, wrap );
		materials.Add( material );
		return material;
	}
}

/// <summary>One textured part of a mesh: its corners (position, normal, texture coordinate) and triangles over them.</summary>
public sealed class ModelMaterialBuilder
{
	internal ModelMaterialBuilder( string? textureName, bool wrap )
	{
		TextureName = textureName;
		Wrap = wrap;
	}

	public string? TextureName { get; }
	public bool Wrap { get; }
	/// <summary>Sets material word +0xC bit 0x1 (<see cref="ModelBuilder.SelfLitMaterialFlag"/>): the material is drawn without per-vertex lighting. Default false.</summary>
	public bool SelfLit { get; set; }
	internal List<ModelBuilderVertex> Vertices { get; } = new();
	internal List<(int A, int B, int C)> Triangles { get; } = new();

	/// <summary>Adds a corner and returns its index within this material. Equal positions share one stored position.</summary>
	public int AddVertex( NVector3 position, NVector3 normal, NVector2 textureCoordinate )
	{
		Vertices.Add( new ModelBuilderVertex( position, normal, textureCoordinate ) );
		return Vertices.Count - 1;
	}

	/// <summary>Adds a triangle over corners of this material (counter-clockwise seen from the front; the face normal is cross(p1 − p0, p2 − p0)).</summary>
	public void AddTriangle( int a, int b, int c )
	{
		foreach ( var corner in new[] { a, b, c } )
		{
			if ( corner < 0 || corner >= Vertices.Count )
				throw new ArgumentOutOfRangeException( nameof( a ), "A triangle references a corner that was not added." );
		}
		Triangles.Add( (a, b, c) );
	}

	/// <summary>Adds the quad a, b, c, d (counter-clockwise) as the triangles (a, b, c) and (a, c, d).</summary>
	public void AddQuad( int a, int b, int c, int d )
	{
		AddTriangle( a, b, c );
		AddTriangle( a, c, d );
	}
}

internal readonly record struct ModelBuilderVertex( NVector3 Position, NVector3 Normal, NVector2 TextureCoordinate );

internal static class ModelBuilderExtensions
{
	public static Vector3 ToOpenTpw( this NVector3 value ) => new( value.X, value.Y, value.Z );
}
