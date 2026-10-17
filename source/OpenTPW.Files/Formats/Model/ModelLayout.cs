namespace OpenTPW;

/// <summary>The per-mesh file offsets of one mesh record (record +96..+112 and +148).</summary>
public sealed record ModelMeshLayout( uint Positions, uint Normals, uint TextureCoordinates, uint Materials, uint Faces, uint Corners );

/// <summary>A run of bytes that no modelled structure covers (alignment padding, unreferenced leftovers) and that is not all zero.</summary>
public sealed record ModelLayoutGap( int Offset, byte[] Bytes );

/// <summary>
/// Where every structure of a version 221.203 geometry member sits in the file: the 0xB8-byte header as read, the region offsets,
/// the node links, the name and per-mesh offsets and the non-zero bytes between structures. Kept by <see cref="ModelFile"/> when it
/// is read with <c>captureLayout: true</c> so that <see cref="ModelFileWriter"/> can write the member back byte for byte; a model
/// without a layout (a built one) gets a planned layout instead. Offsets that are zero in the file stay zero (absent region).
/// </summary>
public sealed record ModelLayout
{
	/// <summary>The header as stored; the writer patches every field it derives from the model, the rest (the opaque words) is kept.</summary>
	public byte[] Header { get; init; } = new byte[ModelFile.HeaderBytes];
	public int FileLength { get; init; }
	public uint FlagTable { get; init; }
	public uint SlotTable { get; init; }
	public uint Positions { get; init; }
	public uint Normals { get; init; }
	public uint TextureCoordinates { get; init; }
	public uint Materials { get; init; }
	public uint Faces { get; init; }
	/// <summary>The terrain heightfield header (0x6C); 0 when absent.</summary>
	public uint Heightfield { get; init; }
	public uint HeightfieldHeights { get; init; }
	public uint HeightfieldCells { get; init; }
	public uint MeshTable { get; init; }
	/// <summary>The dummy node table; 0 when the model has no dummy.</summary>
	public uint DummyTable { get; init; }
	/// <summary>The attribute record table; 0 when there is none.</summary>
	public uint Attributes { get; init; }
	/// <summary>The 16-byte path record table (header 0xAC); 0 when there is none.</summary>
	public uint Paths { get; init; }
	/// <summary>Offset of every node's NUL-terminated name.</summary>
	public IReadOnlyList<uint> NodeNames { get; init; } = Array.Empty<uint>();
	/// <summary>First child of every node (node index) or -1; the order of the sibling chain is not recoverable from parents alone.</summary>
	public IReadOnlyList<int> FirstChild { get; init; } = Array.Empty<int>();
	/// <summary>Next sibling of every node (node index) or -1.</summary>
	public IReadOnlyList<int> NextSibling { get; init; } = Array.Empty<int>();
	public IReadOnlyList<ModelMeshLayout> Meshes { get; init; } = Array.Empty<ModelMeshLayout>();
	/// <summary>Offset of every path's point list.</summary>
	public IReadOnlyList<uint> PathPoints { get; init; } = Array.Empty<uint>();
	public IReadOnlyList<ModelLayoutGap> Gaps { get; init; } = Array.Empty<ModelLayoutGap>();
}
