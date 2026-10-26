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

	/// <summary>The vertex flag that makes <c>test.shader</c> use the alpha test of the cut-out state 0x2756, 0xF0/255: a face of a texture with an alpha plane and an alpha byte of 0.</summary>
	public const uint CutoutFlag = 0x1000_0000;

	/// <summary>
	/// The vertex flag of a corner whose texture slot carries the runtime slot flag 0x800 (<see cref="TextureSlotAddressing.OpaqueSlotBit"/>): the original draws such a slot's faces with state word 0x1256
	/// whatever the texture's own alpha type says (no blend, no alpha test), so <c>test.shader</c> neither tests nor blends them. Bit 27 is free like bits 28 to 31.
	/// </summary>
	// [BIN:TPW-EXE:0x00581790 slot flag 0x800] the face state word is built per face: when the slot word's byte +1 has bit 8 (the slot flag 0x800, 0x00581820) the state gets 0x1256, else the texture's own state (0x00590C40); the skirt reader 0x0046AA10 sets the flag on the slots of a ride's edge floor art
	public const uint OpaqueSlotFlag = 0x0800_0000;

	/// <summary>
	/// Makes the slots of <paramref name="types"/> that carry <see cref="OpaqueSlotFlag"/> on some vertex opaque: the faces of such a slot are neither alpha tested nor sorted into the translucent pass.
	/// Returns whether a slot changed.
	/// </summary>
	public static bool ForceOpaqueSlots( TextureAlphaType[] types, Vertex[] vertices )
	{
		var changed = false;
		foreach ( var vertex in vertices )
		{
			var slot = vertex.TexIndex;
			if ( (vertex.MatFlags & OpaqueSlotFlag) != 0 && slot >= 0 && slot < types.Length && types[slot] != TextureAlphaType.Opaque )
			{
				types[slot] = TextureAlphaType.Opaque;
				changed = true;
			}
		}
		return changed;
	}

	/// <summary>The original's alpha test of the translucent state, 0x10 of 255 (the shader's constant).</summary>
	public const float AlphaTestReference = 0x10 / 255f;

	/// <summary>The original's alpha test of the cut-out state, 0xF0 of 255 (the shader's constant).</summary>
	public const float CutoutReference = 0xF0 / 255f;

	/// <summary>Whether geometry textured with <paramref name="type"/> is drawn in the translucent pass (the original's states without bits 0x1000 and 0x2000: only 0x556 of the world's).</summary>
	public static bool IsTranslucent( TextureAlphaType type ) => type == TextureAlphaType.Translucent;

	/// <summary>The vertex flag of a texture's alpha type: the alpha test its state word asks for (none for an opaque texture, which tests at the engine's 0.1 as always).</summary>
	public static uint FlagFor( TextureAlphaType type ) => type switch
	{
		TextureAlphaType.Cutout => CutoutFlag,
		TextureAlphaType.Translucent => AlphaTestLowFlag,
		_ => 0u
	};

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

	/// <summary>
	/// The translucent faces of one texture slot of a model, as geometry of their own: <see cref="Vertices"/> and <see cref="Indices"/> hold only what the faces use, and
	/// <see cref="Map"/> gives each vertex's number in the vertices that were split (for a model whose vertices are replaced every frame).
	/// </summary>
	public sealed record Group( int Slot, Vertex[] Vertices, uint[] Indices, int[] Map );

	/// <summary>Geometry split by the original's pass: the faces drawn at once (with the map of their vertices) and the translucent faces by texture slot, in the order of their first face.</summary>
	public sealed record Split( Vertex[] OpaqueVertices, uint[] OpaqueIndices, int[]? OpaqueMap, IReadOnlyList<Group> Translucent )
	{
		/// <summary>Faces with corners in both passes, counted as translucent (none in the shipped data).</summary>
		public int MixedFaces { get; init; }
	}

	/// <summary>
	/// Splits indexed triangles by the pass of their texture slot (<see cref="Vertex.TexIndex"/>, with <paramref name="slotIsTranslucent"/> saying which slots are translucent). A face with any translucent
	/// corner is a translucent face, and it belongs to the slot of its first translucent corner; one group is made per slot, like the original's one record per submitted run of one texture. Vertices keep their order.
	/// When no face is translucent the input arrays are returned as they are (no map, no groups), so geometry without translucent textures is unchanged. Faces whose indices lie outside
	/// <paramref name="vertices"/> are not drawn by either part.
	/// </summary>
	public static Split SplitFaces( Vertex[] vertices, uint[] indices, Func<int, bool> slotIsTranslucent )
	{
		var translucentVertex = new bool[vertices.Length];
		var any = false;
		for ( var index = 0; index < vertices.Length; index++ )
			any |= translucentVertex[index] = slotIsTranslucent( vertices[index].TexIndex );
		if ( !any )
			return new Split( vertices, indices, null, [] );

		var opaqueIndices = new List<uint>();
		var groups = new List<(int Slot, List<uint> Indices)>();
		var mixed = 0;
		for ( var face = 0; face + 2 < indices.Length; face += 3 )
		{
			uint a = indices[face], b = indices[face + 1], c = indices[face + 2];
			if ( a >= vertices.Length || b >= vertices.Length || c >= vertices.Length )
				continue;
			var count = (translucentVertex[a] ? 1 : 0) + (translucentVertex[b] ? 1 : 0) + (translucentVertex[c] ? 1 : 0);
			if ( count is > 0 and < 3 )
				mixed++;
			List<uint> target;
			if ( count == 0 )
				target = opaqueIndices;
			else
			{
				var slot = vertices[translucentVertex[a] ? a : translucentVertex[b] ? b : c].TexIndex;
				var group = groups.FindIndex( candidate => candidate.Slot == slot );
				if ( group < 0 )
				{
					groups.Add( (slot, new List<uint>()) );
					group = groups.Count - 1;
				}
				target = groups[group].Indices;
			}
			target.Add( a );
			target.Add( b );
			target.Add( c );
		}
		var opaque = Compact( vertices, opaqueIndices );
		return new Split( opaque.Vertices, opaque.Indices, opaque.Map, groups.Select( group =>
		{
			var compact = Compact( vertices, group.Indices );
			return new Group( group.Slot, compact.Vertices, compact.Indices, compact.Map );
		} ).ToArray() ) { MixedFaces = mixed };
	}

	/// <summary>The vertices the faces use, in their original order, the faces renumbered to them, and each vertex's original number.</summary>
	private static (Vertex[] Vertices, uint[] Indices, int[] Map) Compact( Vertex[] vertices, List<uint> indices )
	{
		var order = indices.Distinct().Order().ToArray();
		var renumber = new Dictionary<uint, uint>( order.Length );
		for ( var index = 0; index < order.Length; index++ )
			renumber.Add( order[index], (uint)index );
		return (order.Select( index => vertices[index] ).ToArray(), indices.Select( index => renumber[index] ).ToArray(), order.Select( index => (int)index ).ToArray());
	}

	/// <summary>Adds the alpha test flag of each vertex's texture slot to its flags (<see cref="FlagFor"/>); the vertices are changed in place.</summary>
	public static void ApplyAlphaFlags( Vertex[] vertices, TextureAlphaType[] slotTypes )
	{
		for ( var index = 0; index < vertices.Length; index++ )
		{
			var slot = vertices[index].TexIndex;
			if ( slot >= 0 && slot < slotTypes.Length )
				vertices[index].MatFlags |= FlagFor( slotTypes[slot] );
		}
	}

	/// <summary>
	/// The depth of a point for the sort keys: the clip-space z over w of <paramref name="modelViewProjection"/> (row-vector convention), the post-projection z that the original sums (the transformed vertex's +8),
	/// which grows with distance. A point at or behind the eye plane is the nearest, as the clipper leaves it out of the original's sums.
	/// </summary>
	public static float Depth( in System.Numerics.Vector3 position, in Matrix4x4 modelViewProjection )
	{
		var w = position.X * modelViewProjection.M14 + position.Y * modelViewProjection.M24 + position.Z * modelViewProjection.M34 + modelViewProjection.M44;
		if ( w <= 1e-6f )
			return -1f;
		var z = position.X * modelViewProjection.M13 + position.Y * modelViewProjection.M23 + position.Z * modelViewProjection.M33 + modelViewProjection.M43;
		return z / w;
	}

	/// <summary>The mean depth of a record's vertices (the original's +0x30 over the vertex count at +0x20), the key the records are sorted by.</summary>
	public static float MeanDepth( System.Numerics.Vector3[] positions, in Matrix4x4 modelViewProjection )
	{
		if ( positions.Length == 0 )
			return 0f;
		var sum = 0.0;
		for ( var index = 0; index < positions.Length; index++ )
			sum += Depth( positions[index], modelViewProjection );
		return (float)(sum / positions.Length);
	}

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
