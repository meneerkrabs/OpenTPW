using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NVector3 = System.Numerics.Vector3;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace OpenTPW;

/// <summary>
/// The model test of the original's cursor pick (tp.exe <c>0x45d090</c>) over an object's MD2 model at its current pose: the nodes depth first from the root,
/// each drawn node's faces in their stored order, the first face whose plane hit lies inside it (<see cref="ObjectRayPick.TryHitFace"/>).
/// </summary>
// [BIN:TPW-EXE:0x45d090 model walk] a node's own faces are tested unless its state word has 0x10 or any of 0x8210 (0x45d0b4-0x45d0c6; 0x10 is the toggled
// hidden bit, ObjectAnimator.IsNodeHidden), material by material (the node's +0x5a records of 16 bytes at +0x6c: face start word +4, face count +6,
// 0x45d0d6-0x45d116) and face by face (the 8-byte records at +0x70: the normal index & 0x7fff and three corners mapped through the corner table +0x94 to
// positions, 0x45d11e-0x45d16c), then, unless the state has 0x20, its children from the first (+0xc) along the siblings (+8) until one hits
// (0x45d4cf-0x45d504), a hidden node's children too; the first hit returns at once (0x45d511), so the hit is the first face in this order, not the nearest
// [BIN:TPW-EXE:0x45d0b4 node bits at pick time] bit 0x20 (the walk then leaves the children out, 0x45d4cf) and 0x8000 are never set when the picker runs: no shipped object model
// carries a node word with 0x10, 0x20 or 0x8000 (ObjectPickNodeBitsCorpusTests counts every node of the 274 object models: only 0x1, 0x8, 0x200 and 0x400 occur), the one runtime writer
// of 0x20 is not a node word (the writers of an immediate 0x20 into a node-sized word of the .text are the key-table flag 0x44aaa5, the texture slot 0x462fbc, the saved channel flags 0x464b06,
// the thing flags 0x4db41c), the animation sets 0x10 only (ObjectAnimator) and the eye views set 0x8000 (0x42ab20, 0x42ad94) and clear it again on leaving, which the picker never overlaps
// (it runs only while the camera word 0x790ab0 has none of the eye bits 0x16, 0x54e2c0)
// [BIN:TPW-EXE:0x473c30 normal refresh] a placed object's creation runs the ground conform 0x469940 for every non-fixed object (creation flag 8, 0x463060), which sets node flag 0x10000 on the root node and
// model flag 0x40000 whether or not the type lists ground vertices; every object model has flag 0x20000 (the object loader 0x413c10 passes 0x50c00, 0x450c00, 0xb24a9 or 0x4b24a9, whose bit 0x10000
// the mesh loader 0x4629d0 turns into load flag 0x800, which 0x46220e turns into model flag 0x20000), so the first visit of the cell by 0x45cdc0 runs 0x473c30 (0x45cecf-0x45ced9): the faces of each node
// with flag 0x10000 (the root node alone) get a new normal, the unit normal of (corner[word 3] - corner[word 2]) x (corner[word 1] - corner[word 2]) (0x473a40: the base is the face's second corner word;
// the cross product's sign is the reverse of e1 x e2, which the plane equation does not see), written to the normal index of the face (word 0 & 0x7fff) in the face order, so faces sharing an index
// keep the last one's, and (0, -1, 0) when the x part is not a number; the node flag and the model flag are cleared (0x473c30's tail), so it runs once. OpenTPW does it at the first model test of
// the instance, from the positions the test then uses (the instance's conformed copy)
// The material face ranges of every shipped object model lie within its faces (ObjectRayPickTests checks all 1630 meshes); OpenTPW's own built models leave
// every start word at 0 but ship no height map, so the walk never names them (0x452ae0)
public static class ObjectPickModel
{
	/// <summary>A mesh's positions, faces and normals in the forms the test reads, built once per model.</summary>
	private sealed class Mesh
	{
		public required NVector3[] Positions { get; init; }
		/// <summary>Per face: three position indices (the corners mapped through the corner table).</summary>
		public required int[] Faces { get; init; }
		/// <summary>Per face: its stored normal, from the mesh's normal block (corner normals, then face normals) by the face's index &amp; 0x7fff.</summary>
		public required NVector3[] FaceNormals { get; init; }
		/// <summary>The faces in the order the test walks them: material by material through each material's face range.</summary>
		public required int[] Order { get; init; }
		/// <summary>The mesh's normal block as the original holds it: the corner normals, then the face normals.</summary>
		public required NVector3[] NormalBlock { get; init; }
		/// <summary>Per face: its normal index (the face word &amp; 0x7fff), -1 when the mesh has none for it.</summary>
		public required int[] NormalIndices { get; init; }
	}

	private static readonly ConditionalWeakTable<ModelFile, Mesh?[]> meshes = new();

	private static Mesh?[] MeshesOf( ModelFile model ) => meshes.GetValue( model, Build );

	private static Mesh?[] Build( ModelFile model )
	{
		var result = new Mesh?[model.Meshes.Count];
		for ( var index = 0; index < result.Length; index++ )
		{
			var mesh = model.Meshes[index];
			var faceCount = mesh.Indices.Length / 3;
			if ( faceCount == 0 || mesh.Positions.Length == 0 )
				continue;
			var positions = new NVector3[mesh.Positions.Length];
			var min = new NVector3( float.PositiveInfinity );
			var max = new NVector3( float.NegativeInfinity );
			for ( var position = 0; position < positions.Length; position++ )
			{
				positions[position] = mesh.Positions[position].GetSystemVector3();
				min = NVector3.Min( min, positions[position] );
				max = NVector3.Max( max, positions[position] );
			}
			var faces = new int[faceCount * 3];
			var normals = new NVector3[faceCount];
			var normalIndices = new int[faceCount];
			var block = new NVector3[mesh.CornerNormals.Length + mesh.FaceNormals.Length];
			for ( var corner = 0; corner < mesh.CornerNormals.Length; corner++ )
				block[corner] = mesh.CornerNormals[corner].GetSystemVector3();
			for ( var face = 0; face < mesh.FaceNormals.Length; face++ )
				block[mesh.CornerNormals.Length + face] = mesh.FaceNormals[face].GetSystemVector3();
			var corners = mesh.CornerPositionIndices;
			for ( var face = 0; face < faceCount; face++ )
			{
				for ( var corner = 0; corner < 3; corner++ )
				{
					var value = mesh.Indices[face * 3 + corner];
					faces[face * 3 + corner] = value < corners.Length ? corners[value] : 0;
				}
				var normal = face < mesh.FaceNormalIndices.Length ? mesh.FaceNormalIndices[face] & 0x7fff : -1;
				normalIndices[face] = normal;
				normals[face] = normal < 0 ? default
					: normal < mesh.CornerNormals.Length ? mesh.CornerNormals[normal].GetSystemVector3()
					: normal - mesh.CornerNormals.Length < mesh.FaceNormals.Length ? mesh.FaceNormals[normal - mesh.CornerNormals.Length].GetSystemVector3()
					: default;
			}
			result[index] = new Mesh { Positions = positions, Faces = faces, FaceNormals = normals, Order = FaceOrder( mesh, faceCount ), NormalBlock = block, NormalIndices = normalIndices };
		}
		return result;
	}

	/// <summary>
	/// The normal refresh of one instance (<c>0x473c30</c>): not yet run until the instance's first model test, then the root node's face normals as the refresh left them.
	/// </summary>
	public sealed class NormalRefresh
	{
		public bool Done { get; set; }
		public NVector3[]? RootFaceNormals { get; set; }
	}

	/// <summary>
	/// The face normals of a mesh after <c>0x473a40</c> over the node-local <paramref name="positions"/>: per face in storage order the unit normal of (corner 3 - corner 2) x (corner 1 - corner 2)
	/// written to the face's normal index, so a shared index keeps the last writer's; a face is then read back through its index. A face whose index lies outside the mesh's normal block is left
	/// out (the original would write beyond it, which no shipped model does, ObjectRayPickTests).
	/// </summary>
	internal static NVector3[] RefreshedFaceNormals( ModelFile model, int meshIndex, IReadOnlyList<NVector3>? positions )
	{
		var mesh = MeshesOf( model )[meshIndex]!;
		return RefreshedFaceNormals( mesh.NormalBlock, mesh.NormalIndices, mesh.Faces,
			position => positions != null ? position < positions.Count ? positions[position] : default : mesh.Positions[position] );
	}

	/// <summary>The refresh over plain arrays: the normal block, each face's normal index and each face's three position indices (corner words 1 to 3), and the positions.</summary>
	internal static NVector3[] RefreshedFaceNormals( NVector3[] normalBlock, int[] normalIndices, int[] faces, Func<int, NVector3> position )
	{
		var block = (NVector3[])normalBlock.Clone();
		for ( var face = 0; face < normalIndices.Length; face++ )
		{
			var index = normalIndices[face];
			if ( index < 0 || index >= block.Length )
				continue;
			block[index] = RefreshedNormal( position( faces[face * 3] ), position( faces[face * 3 + 1] ), position( faces[face * 3 + 2] ) );
		}
		var result = new NVector3[normalIndices.Length];
		for ( var face = 0; face < result.Length; face++ )
		{
			var index = normalIndices[face];
			result[face] = index >= 0 && index < block.Length ? block[index] : default;
		}
		return result;
	}

	/// <summary>One face's refreshed normal from its corner words 1 to 3 (<paramref name="first"/>, <paramref name="second"/>, <paramref name="third"/>), as <c>0x473a40</c> computes it.</summary>
	internal static NVector3 RefreshedNormal( NVector3 first, NVector3 second, NVector3 third )
	{
		// the base is the second corner word; e1 runs to the first, e2 to the third
		var e1 = first - second;
		var e2 = third - second;
		var x = e1.Z * e2.Y - e1.Y * e2.Z;
		var y = e1.X * e2.Z - e1.Z * e2.X;
		var z = e1.Y * e2.X - e1.X * e2.Y;
		var length = MathF.Sqrt( z * z + y * y + x * x );
		var normal = new NVector3( x / length, y / length, z / length );
		return float.IsNaN( normal.X ) ? new NVector3( 0, -1, 0 ) : normal;
	}

	/// <summary>
	/// The faces material by material through each material's range (start <c>a</c>, count <c>b</c>), as the original walks them: a face that two ranges share
	/// twice and a face that no range covers never; a range is cut at the mesh's last face, so a malformed one reads no other mesh's records.
	/// </summary>
	internal static int[] FaceOrder( ModelFile.Mesh mesh, int faceCount )
	{
		var order = new List<int>( faceCount );
		foreach ( var material in mesh.Materials )
			for ( var face = material.a; face < material.a + material.b && face < faceCount; face++ )
				order.Add( face );
		return order.ToArray();
	}

	/// <summary>
	/// The engine matrix of a node of an object: its MD2 node transform at the current pose, the MD2 axes turned to the engine's and the object's placement, so
	/// a stored position maps with <c>Transform(position, matrix)</c> (the matrix that OriginalObject draws the node's vertices with, after ObjectAssets.ConvertAxes).
	/// </summary>
	private static Matrix4x4 NodeToEngine( Matrix4x4 node, Matrix4x4 placement ) =>
		node * new Matrix4x4( 1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1 ) * placement;

	/// <summary>The first face of the model at the pose that the line meets inside it, in the original's node and face order, and the point.</summary>
	public static bool TryHitLine( ModelFile model, IReadOnlyList<Matrix4x4> nodes, Func<int, bool> hidden, Func<int, IReadOnlyList<NVector3>?> vertexPositions,
		Matrix4x4 placement, NVector3 origin, NVector3 direction, out NVector3 hit, NormalRefresh? refresh = null )
	{
		hit = default;
		var root = model.RootNodeIndex;
		if ( root < 0 || root >= model.Nodes.Count )
			return false;
		var built = MeshesOf( model );
		if ( refresh is { Done: false } )
		{
			// 0x473c30 at the instance's first model test: the root node's faces take the normals of the positions they then have
			refresh.Done = true;
			var rootMesh = model.Nodes[root].MeshIndex;
			if ( rootMesh >= 0 && rootMesh < built.Length && built[rootMesh] != null )
				refresh.RootFaceNormals = RefreshedFaceNormals( model, rootMesh, vertexPositions( rootMesh ) );
		}
		return TryHitSubtree( model, built, root, nodes, hidden, vertexPositions, placement, origin, direction, 0, out hit, root, refresh?.RootFaceNormals );
	}

	/// <summary>A node's own faces, then its children from the first along the siblings, depth first (a malformed tree is cut at the node count's depth).</summary>
	private static bool TryHitSubtree( ModelFile model, Mesh?[] built, int node, IReadOnlyList<Matrix4x4> nodes, Func<int, bool> hidden, Func<int, IReadOnlyList<NVector3>?> vertexPositions,
		Matrix4x4 placement, NVector3 origin, NVector3 direction, int depth, out NVector3 hit, int root, NVector3[]? rootNormals )
	{
		hit = default;
		if ( node < 0 || node >= model.Nodes.Count || depth > model.Nodes.Count )
			return false;
		if ( node < nodes.Count && TryHitNode( model, built, node, nodes[node], hidden, vertexPositions, placement, origin, direction, out hit, node == root ? rootNormals : null ) )
			return true;
		var siblings = 0;
		for ( var child = model.Nodes[node].FirstChildIndex; child >= 0 && child < model.Nodes.Count && siblings++ <= model.Nodes.Count; child = model.Nodes[child].NextSiblingIndex )
			if ( TryHitSubtree( model, built, child, nodes, hidden, vertexPositions, placement, origin, direction, depth + 1, out hit, root, rootNormals ) )
				return true;
		return false;
	}

	private static bool TryHitNode( ModelFile model, Mesh?[] built, int node, Matrix4x4 transform, Func<int, bool> hidden, Func<int, IReadOnlyList<NVector3>?> vertexPositions,
		Matrix4x4 placement, NVector3 origin, NVector3 direction, out NVector3 hit, NVector3[]? refreshedNormals )
	{
		hit = default;
		var index = model.Nodes[node].MeshIndex;
		if ( index < 0 || index >= built.Length || built[index] is not { } mesh || hidden( node ) )
			return false;
		var matrix = NodeToEngine( transform, placement );
		var animated = vertexPositions( index );
		// each position turned into the engine once for the node's faces
		var count = animated?.Count ?? mesh.Positions.Length;
		var positions = new NVector3[count];
		for ( var position = 0; position < count; position++ )
			positions[position] = NVector3.Transform( animated != null ? animated[position] : mesh.Positions[position], matrix );
		var faces = mesh.Faces;
		foreach ( var face in mesh.Order )
		{
			var a = positions[faces[face * 3]];
			var b = positions[faces[face * 3 + 1]];
			var c = positions[faces[face * 3 + 2]];
			var normal = NVector3.TransformNormal( refreshedNormals != null ? refreshedNormals[face] : mesh.FaceNormals[face], matrix );
			if ( ObjectRayPick.TryHitFace( origin, direction, a, b, c, normal, out hit ) )
				return true;
		}
		return false;
	}
}
