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
	/// <summary>[EXT:object-lights] The type word of a light dummy's attribute record: class 0x20000 and a data block (<see cref="ModelLights"/>).</summary>
	public const uint LightType = ModelDummyAttribute.LightFlag | ModelDummyAttribute.DataFlag;
	/// <summary>The longest source name (the header field is 20 bytes with a terminating NUL).</summary>
	public const int MaximumSourceNameLength = 19;
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
		CheckPrintable( SourceName, "Source name", MaximumSourceNameLength );
		if ( !float.IsFinite( HeaderHeight ) )
			throw new InvalidOperationException( "The header height is not finite." );
		var names = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var node in nodes )
		{
			CheckPrintable( node.Name, "Node name", 255 );
			if ( !names.Add( node.Name ) )
				throw new InvalidOperationException( $"Two nodes are named '{node.Name}'; node names must be unique." );
			if ( !IsFinite( node.Transform ) )
				throw new InvalidOperationException( $"The transform of node '{node.Name}' is not finite." );
			if ( node.IsMesh && node.Mesh!.Materials.Count == 0 )
				throw new InvalidOperationException( $"Mesh '{node.Name}' has no material; add a material with vertices or use a dummy node." );
		}

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
		NVector3 boundsMin = default, boundsMax = default;
		var anyPosition = false;
		for ( var index = 0; index < ordered.Count; index++ )
		{
			var node = ordered[index];
			var children = ordered.Where( other => other.Parent == node ).Select( other => indexOf[other] ).OrderBy( child => child ).ToArray();
			var siblings = node.Parent == null ? Array.Empty<int>() : ordered.Where( other => other.Parent == node.Parent ).Select( other => indexOf[other] ).OrderBy( sibling => sibling ).ToArray();
			var next = siblings.Where( sibling => sibling > index ).DefaultIfEmpty( -1 ).First();
			modelNodes[index] = new ModelNode( index, node.Type, (uint)index, node.Name, node.Parent == null ? -1 : indexOf[node.Parent], index < meshCount ? index : -1, node.Transform,
				children.Length == 0 ? -1 : children[0], next );
			if ( !node.IsMesh )
				continue;
			var mesh = BuildMesh( node, textures, slotOf );
			meshes.Add( mesh );
			var composed = World( node );
			foreach ( var position in mesh.Positions )
			{
				var moved = NVector3.Transform( new NVector3( position.X, position.Y, position.Z ), composed );
				boundsMin = anyPosition ? NVector3.Min( boundsMin, moved ) : moved;
				boundsMax = anyPosition ? NVector3.Max( boundsMax, moved ) : moved;
				anyPosition = true;
			}
		}

		var attributes = BuildAttributes( ordered, indexOf, meshCount, out var unknown46 );
		var model = new ModelFile
		{
			SourceName = SourceName,
			HeaderFlags = HeaderFlags,
			HeaderHeight = HeaderHeight,
			HeaderOpaque = ModelHeaderOpaque.Default,
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
		var lights = ordered.Where( node => node.LightNumber != null ).ToList();
		if ( heads.Count == 0 && lights.Count == 0 )
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
		// [EXT:object-lights] light numbers are unique (the original's lookup takes the first dummy of the number) and start anywhere from 0; a script names them
		var numbers = lights.Select( node => node.LightNumber!.Value ).OrderBy( number => number ).ToArray();
		for ( var index = 1; index < numbers.Length; index++ )
		{
			if ( numbers[index] == numbers[index - 1] )
				throw new InvalidOperationException( $"Light number {numbers[index]} is used by two nodes; light numbers must be unique." );
		}
		var recorded = heads.Concat( lights ).ToList();
		var first = Math.Min( meshCount, recorded.Min( node => indexOf[node] ) );
		var last = recorded.Max( node => indexOf[node] );
		unknown46 = (ushort)first;
		var attributes = new ModelDummyAttribute[last - first + 1];
		for ( var index = 0; index < attributes.Length; index++ )
		{
			var node = ordered[first + index];
			attributes[index] = node.HeadId is { } id
				? new ModelDummyAttribute( node.HeadType, (uint)id, new byte[12], node.HeadData )
				: node.LightNumber is { } number
					? new ModelDummyAttribute( LightType, (uint)number, new byte[12], node.LightData )
					: new ModelDummyAttribute( 0x1, 0, new byte[12] );
		}
		return attributes;
	}

	internal static bool IsFinite( NMatrix4x4 matrix ) =>
		float.IsFinite( matrix.M11 ) && float.IsFinite( matrix.M12 ) && float.IsFinite( matrix.M13 ) && float.IsFinite( matrix.M14 )
		&& float.IsFinite( matrix.M21 ) && float.IsFinite( matrix.M22 ) && float.IsFinite( matrix.M23 ) && float.IsFinite( matrix.M24 )
		&& float.IsFinite( matrix.M31 ) && float.IsFinite( matrix.M32 ) && float.IsFinite( matrix.M33 ) && float.IsFinite( matrix.M34 )
		&& float.IsFinite( matrix.M41 ) && float.IsFinite( matrix.M42 ) && float.IsFinite( matrix.M43 ) && float.IsFinite( matrix.M44 );

	internal static bool IsFinite( NVector3 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z );

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
	public NMatrix4x4 Transform
	{
		get => transform;
		set => transform = ModelBuilder.IsFinite( value ) ? value : throw new ArgumentException( "The transform is not finite.", nameof( value ) );
	}

	private NMatrix4x4 transform;
	public bool IsMesh { get; }
	/// <summary>The node type word (0x1 mesh, 0x200 dummy by default; other corpus values 0x401, 0x2, 0x208, 0x300, 0x600 are not interpreted).</summary>
	public uint Type { get; set; }
	public ModelMeshBuilder? Mesh { get; }
	/// <summary>The head id of this node's head record (1..n), or null. A ride script's ADDHEAD hangs a guest head on the node of its head id.</summary>
	public int? HeadId { get; private set; }
	/// <summary>The head record's type word; flag 0x80 marks it as a head record.</summary>
	public uint HeadType { get; private set; } = ModelBuilder.DefaultHeadType;
	/// <summary>The 16-byte data block of a head record whose type has flag 0x40 (such as 0xF1); null for 0xB1.</summary>
	public ModelAttributeData? HeadData { get; private set; }

	/// <summary>
	/// Makes this node carry head record <paramref name="headId"/>. A node has at most one head record; ids are unique and run from 1. A type with flag 0x40
	/// (0xF1, 0x400F1, ...) points at a 16-byte data block of unknown meaning that cannot be defaulted, so it needs <paramref name="data"/>; a type without
	/// the flag (the default 0xB1) takes none.
	/// </summary>
	public ModelNodeBuilder AddHead( int headId, uint type = ModelBuilder.DefaultHeadType, ModelAttributeData? data = null )
	{
		if ( headId < 1 )
			throw new ArgumentOutOfRangeException( nameof( headId ), "Head ids start at 1." );
		if ( (type & 0x80) == 0 )
			throw new ArgumentException( "A head record type needs flag 0x80.", nameof( type ) );
		if ( (type & ModelDummyAttribute.DataFlag) != 0 && data == null )
			throw new ArgumentException( "A head record type with flag 0x40 points at a 16-byte data block; pass it as data (its meaning is unknown, so there is no default), or use type 0xB1.", nameof( data ) );
		if ( (type & ModelDummyAttribute.DataFlag) == 0 && data != null )
			throw new ArgumentException( "Only a type with flag 0x40 has a data block.", nameof( data ) );
		if ( data is { } block )
		{
			if ( !float.IsFinite( block.X ) || !float.IsFinite( block.Y ) || !float.IsFinite( block.Z ) || (block.Position is { } place && !ModelBuilder.IsFinite( place )) )
				throw new ArgumentException( "The data block holds a non-finite float.", nameof( data ) );
			if ( block.Word > 0xFFFF )
				throw new ArgumentException( "The high 16 bits of the data word are the distance to the position entry; give the position as Position and keep Word below 0x10000.", nameof( data ) );
		}
		if ( HeadId != null )
			throw new InvalidOperationException( $"Node '{Name}' already has head record {HeadId}." );
		if ( LightNumber != null )
			throw new InvalidOperationException( $"Node '{Name}' is already light {LightNumber}; a node has one attribute record." );
		HeadId = headId;
		HeadType = type;
		HeadData = data;
		return this;
	}

	/// <summary>[EXT:object-lights] The light number of this node's light dummy record, or null.</summary>
	public int? LightNumber { get; private set; }
	/// <summary>[EXT:object-lights] The data block of the light record: colour in X, Y, Z and the radius in model units in the word (0 = the default 100).</summary>
	public ModelAttributeData? LightData { get; private set; }

	/// <summary>
	/// [EXT:object-lights] Makes this node a light dummy (docs/LIGHTS.md): attribute class 0x20000 with the light number as value, which the four light opcodes address, and a data
	/// block that carries the base <paramref name="colour"/> (linear 0..1 channels) and the <paramref name="radiusModelUnits"/> (1 to 65535 model units; 0 keeps the engine default of
	/// 100, the original's point-light radius). The node's world transform places the light. A node has at most one attribute record, and light numbers are unique.
	/// </summary>
	public ModelNodeBuilder AddLight( int number, NVector3 colour, int radiusModelUnits = 0 )
	{
		if ( number < 0 || number > short.MaxValue )
			throw new ArgumentOutOfRangeException( nameof( number ), "Light numbers run from 0 to 32767 (a script operand is 16 bits)." );
		if ( !ModelBuilder.IsFinite( colour ) || colour.X < 0 || colour.Y < 0 || colour.Z < 0 || colour.X > 1 || colour.Y > 1 || colour.Z > 1 )
			throw new ArgumentException( "A light colour has three channels from 0 to 1.", nameof( colour ) );
		if ( radiusModelUnits < 0 || radiusModelUnits > ushort.MaxValue )
			throw new ArgumentOutOfRangeException( nameof( radiusModelUnits ), "The radius is 1 to 65535 model units, or 0 for the default." );
		if ( LightNumber != null )
			throw new InvalidOperationException( $"Node '{Name}' is already light {LightNumber}." );
		if ( HeadId != null )
			throw new InvalidOperationException( $"Node '{Name}' already has head record {HeadId}; a node has one attribute record." );
		LightNumber = number;
		LightData = new ModelAttributeData( (uint)radiusModelUnits, colour.X, colour.Y, colour.Z );
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
		if ( !ModelBuilder.IsFinite( position ) || !ModelBuilder.IsFinite( normal ) || !float.IsFinite( textureCoordinate.X ) || !float.IsFinite( textureCoordinate.Y ) )
			throw new ArgumentException( "A vertex position, normal or texture coordinate is not finite." );
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
