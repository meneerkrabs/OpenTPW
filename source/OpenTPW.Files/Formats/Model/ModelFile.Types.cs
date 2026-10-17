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

/// <summary>
/// Terrain heightfield located by the MD2 header pointer at 0x6C (terrain <c>base.MD2</c> only).
/// 48-byte header: four opaque words, cell size X/Z (floats), cell counts X/Z, two floats that
/// bracket the heights (kept raw), then pointers to (CountX+1)*(CountZ+1) corner heights and
/// CountX*CountZ per-cell words, both stored X-fastest. Corner (x, z) lies at MD2 world
/// (x * CellSizeX, height, z * CellSizeZ). Cell word <see cref="HoleCellWord"/> marks cells
/// without heightfield surface; otherwise the high 16 bits select an MD2 texture slot (always a
/// <c>*_bas1..6</c> ground texture in the four terrain models). The low 16 bits are not interpreted.
/// </summary>
public sealed class ModelHeightfield
{
	public const int HeaderBytes = 48;
	public const int MaximumCellCount = 1024;
	public const uint HoleCellWord = 1;
	/// <summary>Cell-word bit 0x1: a hole (no surface, no height).</summary>
	public const uint HoleFlag = 0x1;
	/// <summary>Cell-word bit 0x4: the cell splits along (x+1, z)–(x, z+1) instead of (x, z)–(x+1, z+1); 0x2 is its complement.</summary>
	public const uint AntiDiagonalFlag = 0x4;
	/// <summary>Cell-word bits 0x8/0x10/0x20: the ground texture turns by one, two or three corner steps (0x56F4F0).</summary>
	public const uint TextureRotationFlags = 0x38;
	/// <summary>Cell-word bit 0x40: the ground texture is mirrored (corners 1 and 3 swapped before the turn, 0x56F4FC).</summary>
	public const uint TextureMirrorFlag = 0x40;

	private readonly float[] heights;
	private readonly uint[] cellWords;

	internal ModelHeightfield( int cellCountX, int cellCountZ, float cellSizeX, float cellSizeZ, float storedLowerHeight, float storedUpperHeight,
		IReadOnlyList<uint> opaqueHeaderWords, float[] heights, uint[] cellWords )
	{
		CellCountX = cellCountX;
		CellCountZ = cellCountZ;
		CellSizeX = cellSizeX;
		CellSizeZ = cellSizeZ;
		StoredLowerHeight = storedLowerHeight;
		StoredUpperHeight = storedUpperHeight;
		OpaqueHeaderWords = opaqueHeaderWords;
		this.heights = heights;
		this.cellWords = cellWords;
	}

	/// <summary>The stored corner heights, X-fastest (for <see cref="ModelFileWriter"/>).</summary>
	internal IReadOnlyList<float> Heights => heights;
	/// <summary>The stored cell words, X-fastest (for <see cref="ModelFileWriter"/>).</summary>
	internal IReadOnlyList<uint> CellWords => cellWords;

	public int CellCountX { get; }
	public int CellCountZ { get; }
	public float CellSizeX { get; }
	public float CellSizeZ { get; }
	/// <summary>Raw header floats; observed to bracket the heights (integer-truncated), not used for validation.</summary>
	public float StoredLowerHeight { get; }
	public float StoredUpperHeight { get; }
	public IReadOnlyList<uint> OpaqueHeaderWords { get; }

	public float GetCornerHeight( int x, int z )
	{
		if ( (uint)x > (uint)CellCountX || (uint)z > (uint)CellCountZ )
			throw new ArgumentOutOfRangeException( (uint)x > (uint)CellCountX ? nameof( x ) : nameof( z ) );
		return heights[z * (CellCountX + 1) + x];
	}

	public uint GetCellWord( int x, int z )
	{
		if ( (uint)x >= (uint)CellCountX || (uint)z >= (uint)CellCountZ )
			throw new ArgumentOutOfRangeException( (uint)x >= (uint)CellCountX ? nameof( x ) : nameof( z ) );
		return cellWords[z * CellCountX + x];
	}

	public bool IsHole( int x, int z ) => GetCellWord( x, z ) == HoleCellWord;

	/// <summary>
	/// True when the cell's two triangles meet along (x+1, z)–(x, z+1). [BIN:TPW-EXE:0x00452560 heightfield height] the height
	/// lookup splits on cell-word bit 0x4 (also 0x4527F0, 0x46A34C); the terrain renderer 0x56F670 orders the cell's corners by
	/// bit 0x2, which every shipped cell has exactly when 0x4 is clear.
	/// </summary>
	public bool SplitsAlongAntiDiagonal( int x, int z ) => (GetCellWord( x, z ) & AntiDiagonalFlag) != 0;

	/// <summary>
	/// Ground height at cell coordinates (<paramref name="cellX"/>, <paramref name="cellZ"/>) on the cell's triangle, as 0x452560
	/// interpolates it: the plane of the triangle the point lies in, split by <see cref="SplitsAlongAntiDiagonal"/>. Outside the
	/// grid the point is clamped to the edge; a hole has no height in the original (it returns FLT_MAX) and here keeps its corners'.
	/// </summary>
	public float GetHeight( float cellX, float cellZ )
	{
		var x = Math.Clamp( (int)MathF.Floor( cellX ), 0, CellCountX - 1 );
		var z = Math.Clamp( (int)MathF.Floor( cellZ ), 0, CellCountZ - 1 );
		var fx = Math.Clamp( cellX - x, 0f, 1f );
		var fz = Math.Clamp( cellZ - z, 0f, 1f );
		var (h00, h10, h01, h11) = (GetCornerHeight( x, z ), GetCornerHeight( x + 1, z ), GetCornerHeight( x, z + 1 ), GetCornerHeight( x + 1, z + 1 ));
		if ( !SplitsAlongAntiDiagonal( x, z ) )
			return fx < fz
				? h00 + fx * (h11 - h01) + fz * (h01 - h00)
				: h00 + fx * (h10 - h00) + fz * (h11 - h10);
		return 1 - fz <= fx
			? h01 + fx * (h11 - h01) + (fz - 1) * (h11 - h10)
			: h00 + fx * (h10 - h00) + fz * (h01 - h00);
	}

	/// <summary>MD2 texture slot of a surface cell, or -1 for a hole.</summary>
	public int GetCellTextureSlot( int x, int z ) => IsHole( x, z ) ? -1 : (int)(GetCellWord( x, z ) >> 16);
}
