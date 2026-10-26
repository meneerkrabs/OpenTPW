using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The floor of one placed object (docs/OBJECTS.md, "Floors"): its type's <see cref="ObjectSkirt"/> and the instance's own edge conform (<see cref="ObjectGroundConform"/>), applied to the model's
/// meshes before they become render parts. The type's parsed model stays as it was read: every mesh this changes is a copy with the conformed positions, the normalised texture coordinates and
/// the slot flags the skirt reader sets (opaque 0x800, wrap bits cleared). A model without a skirt or with nothing to conform is not copied.
/// </summary>
internal sealed class ObjectFloor
{
	private readonly ModelFile model;
	private readonly ObjectSkirt skirt;
	private readonly ObjectGroundConform.Result conform;

	private ObjectFloor( ModelFile model, ObjectSkirt skirt, ObjectGroundConform.Result conform )
	{
		this.model = model;
		this.skirt = skirt;
		this.conform = conform;
	}

	public ObjectSkirt Skirt => skirt;

	/// <summary>The floor of a model that has a skirt but no edge conform of its own (a queue model: its nodes carry the drape flag, so the drape pass, not the conform, moves them).</summary>
	internal static ObjectFloor ForSkirt( ModelFile model, ObjectSkirt skirt ) =>
		new( model, skirt, new ObjectGroundConform.Result( new Dictionary<int, NVector3[]>(), Array.Empty<(int Mesh, int Vertex)>() ) );

	/// <summary>
	/// The floor of an object of <paramref name="entry"/> at <paramref name="placement"/> over <paramref name="field"/>, or null when it has none: a fixed item, or a park without terrain.
	/// </summary>
	public static ObjectFloor? Create( ObjectCatalogEntry entry, ModelFile model, ObjectPlacement placement, ModelHeightfield? field, ObjectHoardingCache? cache = null )
	{
		if ( field == null || entry.IsFixedItem )
			return null;
		var skirt = cache != null ? cache.Skirt( entry, model, field ) : ObjectSkirt.Build( entry.Settings, model, HoardingTerrain.Of( field ) );
		if ( skirt == null )
			return null;
		var terrain = HoardingTerrain.Of( field );
		var conform = ObjectGroundConform.Apply( model, skirt, terrain,
			( x, y ) => field.GetCornerHeight( Math.Clamp( x, 0, field.CellCountX ), Math.Clamp( y, 0, field.CellCountZ ) ), ObjectGroundConform.RootPlacement( placement, terrain ) );
		return new ObjectFloor( model, skirt, conform );
	}

	/// <summary>
	/// The conformed copy of mesh <paramref name="meshIndex"/>'s positions (MD2 axes, node-local, indexed like <see cref="ModelFile.Mesh.Positions"/>), or null when the conform moved none of its vertices:
	/// the positions the cursor pick tests the object's floor with.
	/// </summary>
	public IReadOnlyList<NVector3>? ConformedPositions( int meshIndex ) => conform.Positions.TryGetValue( meshIndex, out var positions ) ? positions : null;

	/// <summary>Whether this floor changes mesh <paramref name="meshIndex"/> at all.</summary>
	public bool Changes( int meshIndex, ModelFile.Mesh mesh ) =>
		conform.Positions.ContainsKey( meshIndex ) || (meshIndex == skirt.GroundMesh && skirt.TexCoords != null) || mesh.Materials.Any( material => SlotFlagsChange( material ) != material.Flags );

	/// <summary><paramref name="mesh"/> as the instance draws it: a copy with the conformed positions, the normalised coordinates and the changed slot flags, or the mesh itself when none applies.</summary>
	public ModelFile.Mesh Apply( int meshIndex, ModelFile.Mesh mesh )
	{
		if ( !Changes( meshIndex, mesh ) )
			return mesh;
		var vertices = mesh.Vertices;
		if ( conform.Positions.TryGetValue( meshIndex, out var positions ) )
		{
			vertices = new ModelFile.Vertex[mesh.Vertices.Length];
			for ( var corner = 0; corner < vertices.Length; corner++ )
				vertices[corner] = new ModelFile.Vertex { Position = (Vector3)positions[mesh.CornerPositionIndices[corner]], TextureIndex = mesh.Vertices[corner].TextureIndex };
		}
		var texCoords = meshIndex == skirt.GroundMesh && skirt.TexCoords is { } normalised ? normalised.Select( value => (Vector2)value ).ToArray() : mesh.TexCoords;
		var movedPositions = positions?.Select( position => (Vector3)position ).ToArray();
		var materials = mesh.Materials.Select( material => new ModelFile.MaterialData
		{
			FrameOffset = material.FrameOffset, a = material.a, b = material.b, StartIndex = material.StartIndex, EndIndex = material.EndIndex, Pad = material.Pad,
			TextureIndex = material.TextureIndex, Name = material.Name, Flags = SlotFlagsChange( material )
		} ).ToArray();
		return new ModelFile.Mesh
		{
			Name = mesh.Name, NodeIndex = mesh.NodeIndex, NodeType = mesh.NodeType, VertexOffset = mesh.VertexOffset, UvOffset = mesh.UvOffset, VertCnt = mesh.VertCnt, MaterialOffset = mesh.MaterialOffset,
			FaceOffset = mesh.FaceOffset, NormalOffset = mesh.NormalOffset, FaceCount = mesh.FaceCount, VertexCount = mesh.VertexCount, VertexOrderLen = mesh.VertexOrderLen,
			VertexOrderOffset = mesh.VertexOrderOffset, Positions = movedPositions ?? mesh.Positions, CornerPositionIndices = mesh.CornerPositionIndices, Vertices = vertices, Indices = mesh.Indices,
			FaceNormalIndices = mesh.FaceNormalIndices, TexCoords = texCoords, TransformMatrix = mesh.TransformMatrix, BoundsMin = mesh.BoundsMin, BoundsMax = mesh.BoundsMax, Materials = materials,
			CornerNormals = mesh.CornerNormals, FaceNormals = mesh.FaceNormals, Normals = mesh.Normals
		};
	}

	/// <summary>The slot flags of a material after the skirt reader: the opaque bit set and the wrap bits cleared where the skirt says so.</summary>
	private uint SlotFlagsChange( ModelFile.MaterialData material )
	{
		var flags = material.Flags;
		var slot = material.TextureIndex;
		if ( slot < 0 )
			return flags;
		if ( slot < skirt.OpaqueSlots.Length && skirt.OpaqueSlots[slot] )
			flags |= TextureSlotAddressing.OpaqueSlotBit;
		if ( slot < skirt.ClampedSlots.Length && skirt.ClampedSlots[slot] )
			flags &= ~TextureSlotAddressing.WrapFlags;
		return flags;
	}
}
