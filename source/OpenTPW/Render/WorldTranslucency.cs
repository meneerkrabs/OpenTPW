using System.Numerics;

namespace OpenTPW;

/// <summary>
/// The original's translucent pass for world geometry (terrain meshes and placed objects). tp.exe gives every textured face a state word from its texture's own alpha type
/// (<see cref="TextureAlphaType"/>); a face whose state lacks bits 0x1000 and 0x2000 (the Direct3D renderer's mask <c>[[0x879AB0]+0xC]</c> = 0x3000) is not drawn where it is batched but kept,
/// its triangles sorted far to near, and drawn after every other model face, far to near by the average depth of its vertices. This class decides which faces those are and holds the sort keys.
/// </summary>
// [BIN:TPW-EXE:0x590C40 state word, 0x581790 face state, 0x5755F0 texture flags] a face with texture flag 0x200 (an alpha plane with a non-zero header alpha byte) gets state 0x556: an alpha blend with an alpha test at 0x10/255 (0x567620: bit 0x400 enables
// the test, bit 0x200 picks 0xF0 and its absence 0x10, bit 0x100 enables the blend, bit 0x800 would stop the depth write and 0x556 lacks it); an alpha plane with a zero alpha byte (flag 0x100) gets 0x2756 (test at 0xF0/255, drawn immediately), anything
// else 0x1256 (no test, no blend). The frame batcher 0x581DB0 (called by 0x582170 after every other pass of the scene 0x576A00) draws the faces whose state has bit 0x1000 or 0x2000 at once, in
// ascending state order, and hands the others to the deferred list of 0x565590, which 0x582AD0 draws after all of them, sorted by 0x5829E0 on the record's average vertex depth (+0x30 divided by the vertex count at +0x20), largest first;
// 0x565590 first sorts the record's own triangles by the sum of their three vertex depths (0x565490, largest first).
// [BIN:TPW-EXE:0x44DD50 renderer mode, 0x590C40] the hardware path is mode 1 (DAT_008BD524 = 1); mode 4 is the software renderer, whose integer depth sort (0x5653D0) is the same rule
internal static class WorldTranslucency
{
	/// <summary>
	/// The vertex flag that makes <c>test.shader</c> use the alpha test of the original's translucent state, 0x10/255, instead of the engine's 0.1. Bit 29 is free like bits 30 and 31
	/// (<see cref="TextureSlotAddressing"/>: the corpus slot flags are 0x1..0x73).
	/// </summary>
	public const uint AlphaTestLowFlag = 0x2000_0000;

	/// <summary>The original's alpha test of the translucent state, 0x10 of 255 (the shader's constant).</summary>
	public const float AlphaTestReference = 0x10 / 255f;

	/// <summary>Whether geometry textured with <paramref name="type"/> is drawn in the translucent pass (the original's states without bits 0x1000 and 0x2000: only 0x556 of the world's).</summary>
	public static bool IsTranslucent( TextureAlphaType type ) => type == TextureAlphaType.Translucent;

	/// <summary>
	/// The original's state word of a face by its texture's alpha type, without the model and texture flags OpenTPW has no counterpart of (0x590C40 and 0x581790: the wrap bit 0x400000,
	/// the no-depth-write bit 0x800 of a model's own flag, 0x5156 for model flag 0x2000000).
	/// </summary>
	public static uint StateWord( TextureAlphaType type ) => type switch
	{
		TextureAlphaType.Cutout => 0x2756,
		TextureAlphaType.Translucent => 0x556,
		_ => 0x1256
	};

	/// <summary>Whether the renderer draws a state word at once (bits 0x1000 or 0x2000 of the mask 0x3000) rather than keeping it for the sorted translucent list.</summary>
	public static bool DrawsImmediately( uint state ) => (state & 0x3000) != 0;

	/// <summary>Geometry split by the original's pass: the faces drawn at once and the faces kept for the sorted pass (null when there are none), each with only the vertices its faces use.</summary>
	public readonly record struct Split( Vertex[] OpaqueVertices, uint[] OpaqueIndices, Vertex[]? TranslucentVertices, uint[]? TranslucentIndices )
	{
		/// <summary>Faces with corners in both passes, counted as translucent (none in the shipped data).</summary>
		public int MixedFaces { get; init; }
	}

	/// <summary>
	/// Splits indexed triangles by the pass of their texture slot (<see cref="Vertex.TexIndex"/>, with <paramref name="slotIsTranslucent"/> saying which slots are translucent). A face with any translucent
	/// corner is a translucent face. Vertices keep their order; a vertex of the translucent part gets <see cref="AlphaTestLowFlag"/>. When no face is translucent the input arrays are returned as they are,
	/// so geometry without translucent textures is unchanged. Faces whose indices lie outside <paramref name="vertices"/> are not drawn by either part.
	/// </summary>
	public static Split SplitFaces( Vertex[] vertices, uint[] indices, Func<int, bool> slotIsTranslucent )
	{
		var translucentVertex = new bool[vertices.Length];
		var any = false;
		for ( var index = 0; index < vertices.Length; index++ )
			any |= translucentVertex[index] = slotIsTranslucent( vertices[index].TexIndex );
		if ( !any )
			return new Split( vertices, indices, null, null );

		var opaqueIndices = new List<uint>();
		var translucentIndices = new List<uint>();
		var mixed = 0;
		for ( var face = 0; face + 2 < indices.Length; face += 3 )
		{
			uint a = indices[face], b = indices[face + 1], c = indices[face + 2];
			if ( a >= vertices.Length || b >= vertices.Length || c >= vertices.Length )
				continue;
			var count = (translucentVertex[a] ? 1 : 0) + (translucentVertex[b] ? 1 : 0) + (translucentVertex[c] ? 1 : 0);
			if ( count is > 0 and < 3 )
				mixed++;
			var target = count > 0 ? translucentIndices : opaqueIndices;
			target.Add( a );
			target.Add( b );
			target.Add( c );
		}
		var opaque = Compact( vertices, opaqueIndices, 0u );
		var translucent = Compact( vertices, translucentIndices, AlphaTestLowFlag );
		return new Split( opaque.Vertices, opaque.Indices, translucent.Vertices, translucent.Indices ) { MixedFaces = mixed };
	}

	/// <summary>The vertices the faces use, in their original order and with <paramref name="flag"/> added to their flags, and the faces renumbered to them.</summary>
	private static (Vertex[] Vertices, uint[] Indices) Compact( Vertex[] vertices, List<uint> indices, uint flag )
	{
		var map = new Dictionary<uint, uint>();
		foreach ( var index in indices.Distinct().Order() )
			map.Add( index, (uint)map.Count );
		var result = new Vertex[map.Count];
		foreach ( var (from, to) in map )
		{
			var vertex = vertices[from];
			vertex.MatFlags |= flag;
			result[to] = vertex;
		}
		return (result, indices.Select( index => map[index] ).ToArray());
	}

	/// <summary>Flags every vertex of a part drawn whole in the translucent pass (<see cref="AlphaTestLowFlag"/>); for vertex-animated parts, whose vertex arrays are rewritten in place and so are not split.</summary>
	public static void FlagAll( Vertex[] vertices )
	{
		for ( var index = 0; index < vertices.Length; index++ )
			vertices[index].MatFlags |= AlphaTestLowFlag;
	}

	/// <summary>Whether any face of the part uses a translucent slot (the parts that are drawn whole: vertex-animated ones).</summary>
	public static bool AnyTranslucentFace( Vertex[] vertices, uint[] indices, Func<int, bool> slotIsTranslucent )
	{
		foreach ( var index in indices )
			if ( index < vertices.Length && slotIsTranslucent( vertices[index].TexIndex ) )
				return true;
		return false;
	}

	/// <summary>
	/// The depth of a point for the sort keys: the clip-space w of <paramref name="modelViewProjection"/> (row-vector convention), the distance along the view direction, which grows with the
	/// depth buffer's value. The original sums the post-projection z of the vertices; both rank faces far to near alike except for faces whose summed depths nearly tie.
	/// </summary>
	public static float Depth( in System.Numerics.Vector3 position, in Matrix4x4 modelViewProjection ) =>
		position.X * modelViewProjection.M14 + position.Y * modelViewProjection.M24 + position.Z * modelViewProjection.M34 + modelViewProjection.M44;

	/// <summary>
	/// The order the triangles of one record are drawn in: by the sum of their three corner depths, largest (farthest) first, ties in the faces' own order (0x565490 sorts by quicksort, which has no stable order to keep).
	/// <paramref name="order"/> receives the triangle numbers; <paramref name="keys"/> is scratch of the same length. The depth sum and the triangle number are packed into one 64-bit key, so the sort is one
	/// primitive sort with no comparer and the order is the same on every run.
	/// </summary>
	public static void SortTriangles( System.Numerics.Vector3[] positions, uint[] indices, in Matrix4x4 modelViewProjection, int[] order, ulong[] keys )
	{
		var triangles = indices.Length / 3;
		for ( var triangle = 0; triangle < triangles; triangle++ )
		{
			var depth = Depth( positions[(int)indices[triangle * 3]], modelViewProjection ) + Depth( positions[(int)indices[triangle * 3 + 1]], modelViewProjection )
				+ Depth( positions[(int)indices[triangle * 3 + 2]], modelViewProjection );
			// the float's bits in ascending order, complemented so the largest depth sorts first
			var bits = BitConverter.SingleToUInt32Bits( depth );
			var ascending = (bits & 0x8000_0000u) != 0 ? ~bits : bits | 0x8000_0000u;
			keys[triangle] = (ulong)~ascending << 32 | (uint)triangle;
		}
		Array.Sort( keys, 0, triangles );
		for ( var triangle = 0; triangle < triangles; triangle++ )
			order[triangle] = (int)(keys[triangle] & 0xFFFF_FFFFu);
	}
}
