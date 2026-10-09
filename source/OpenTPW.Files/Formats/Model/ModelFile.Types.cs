using Matrix4x4 = System.Numerics.Matrix4x4;

namespace OpenTPW;

/// <summary>
/// Observed M3D2 (.MD2) member layouts. Geometry files carry meshes/nodes; animation
/// files carry only the shared header plus an opaque animation payload.
/// </summary>
public enum ModelFileKind
{
	Geometry,
	Animation
}

/// <summary>
/// Header counts at 0x36..0x4B. Animation files repeat the counts of their base model.
/// <see cref="Unknown46"/> is not understood and is kept raw.
/// </summary>
public sealed record ModelHeaderCounts(
	ushort TextureCount,
	ushort VertexBlockCount,
	ushort UvBlockCount,
	ushort MaterialCount,
	ushort FaceCount,
	ushort ExtraRecordCount,
	ushort NodeCount,
	ushort MeshCount,
	ushort Unknown46,
	ushort DummyAttributeCount,
	ushort NormalCount );

/// <summary>
/// One texture slot. <see cref="FrameNames"/> holds one or more consecutive 20-byte names
/// (more than one when the slot is a multi-frame texture). Flag bits are not interpreted.
/// </summary>
public sealed record ModelTexture( uint Flags, uint Unknown, IReadOnlyList<string> FrameNames );

/// <summary>
/// Hierarchy node (mesh or dummy). <see cref="Transform"/> is the stored row-major matrix
/// with translation in the fourth row; whether it is parent-relative is not verified.
/// </summary>
public sealed record ModelNode( int Index, uint Type, uint StoredIndex, string Name, int ParentIndex, int MeshIndex, Matrix4x4 Transform );

/// <summary>20-byte dummy attribute record; only the first two words are separated.</summary>
public sealed record ModelDummyAttribute( uint Type, uint Value, byte[] Reserved );

/// <summary>72-byte animation trailer kept as raw little-endian words; semantics unverified.</summary>
public sealed record ModelAnimationTrailer( int Offset, IReadOnlyList<uint> Words );
