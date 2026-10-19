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
// [APPROX:RIDES-021] the node state bits 0x20 (which prunes the children) and 0x8000 are not modelled
// [EXT:pick-face-order] a mesh whose several materials all start at face 0 (OpenTPW's own built models, ModelBuilder, leave every start word at 0; no
// shipped mesh does) or whose ranges run past its faces is walked in its stored face order, so all its faces can be hit
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
		public required NVector3 BoundsMin { get; init; }
		public required NVector3 BoundsMax { get; init; }
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
			var corners = mesh.CornerPositionIndices;
			for ( var face = 0; face < faceCount; face++ )
			{
				for ( var corner = 0; corner < 3; corner++ )
				{
					var value = mesh.Indices[face * 3 + corner];
					faces[face * 3 + corner] = value < corners.Length ? corners[value] : 0;
				}
				var normal = face < mesh.FaceNormalIndices.Length ? mesh.FaceNormalIndices[face] & 0x7fff : -1;
				normals[face] = normal < 0 ? default
					: normal < mesh.CornerNormals.Length ? mesh.CornerNormals[normal].GetSystemVector3()
					: normal - mesh.CornerNormals.Length < mesh.FaceNormals.Length ? mesh.FaceNormals[normal - mesh.CornerNormals.Length].GetSystemVector3()
					: default;
			}
			result[index] = new Mesh { Positions = positions, Faces = faces, FaceNormals = normals, Order = FaceOrder( mesh, faceCount ), BoundsMin = min, BoundsMax = max };
		}
		return result;
	}

	/// <summary>
	/// The faces material by material through each material's range (start <c>a</c>, count <c>b</c>), as the original walks them, a face that two ranges share
	/// twice and a face that no range covers never; the stored order for a mesh whose ranges run past its faces or whose several materials all start at face 0.
	/// </summary>
	internal static int[] FaceOrder( ModelFile.Mesh mesh, int faceCount )
	{
		var order = new List<int>( faceCount );
		var allAtZero = mesh.Materials.Length > 1;
		foreach ( var material in mesh.Materials )
		{
			if ( material.a + material.b > faceCount )
				return StoredOrder( faceCount );
			allAtZero &= material.a == 0;
			for ( var face = material.a; face < material.a + material.b; face++ )
				order.Add( face );
		}
		return allAtZero ? StoredOrder( faceCount ) : order.ToArray();
	}

	private static int[] StoredOrder( int faceCount )
	{
		var order = new int[faceCount];
		for ( var face = 0; face < faceCount; face++ )
			order[face] = face;
		return order;
	}

	/// <summary>
	/// The engine matrix of a node of an object: its MD2 node transform at the current pose, the MD2 axes turned to the engine's and the object's placement, so
	/// a stored position maps with <c>Transform(position, matrix)</c> (the matrix that OriginalObject draws the node's vertices with, after ObjectAssets.ConvertAxes).
	/// </summary>
	private static Matrix4x4 NodeToEngine( Matrix4x4 node, Matrix4x4 placement ) =>
		node * new Matrix4x4( 1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1 ) * placement;

	/// <summary>The drawn faces' engine height range of the model at the pose, or false when no node draws faces.</summary>
	public static bool TryGetHeightRange( ModelFile model, IReadOnlyList<Matrix4x4> nodes, Func<int, bool> hidden, Func<int, IReadOnlyList<NVector3>?> vertexPositions,
		Matrix4x4 placement, out float bottom, out float top )
	{
		bottom = float.PositiveInfinity;
		top = float.NegativeInfinity;
		var built = MeshesOf( model );
		for ( var node = 0; node < model.Nodes.Count && node < nodes.Count; node++ )
		{
			var index = model.Nodes[node].MeshIndex;
			if ( index < 0 || index >= built.Length || built[index] is not { } mesh || hidden( node ) )
				continue;
			var matrix = NodeToEngine( nodes[node], placement );
			if ( vertexPositions( index ) is { } animated )
			{
				foreach ( var position in animated )
				{
					var z = NVector3.Transform( position, matrix ).Z;
					bottom = MathF.Min( bottom, z );
					top = MathF.Max( top, z );
				}
				continue;
			}
			for ( var corner = 0; corner < 8; corner++ )
			{
				var local = new NVector3( (corner & 1) == 0 ? mesh.BoundsMin.X : mesh.BoundsMax.X, (corner & 2) == 0 ? mesh.BoundsMin.Y : mesh.BoundsMax.Y, (corner & 4) == 0 ? mesh.BoundsMin.Z : mesh.BoundsMax.Z );
				var z = NVector3.Transform( local, matrix ).Z;
				bottom = MathF.Min( bottom, z );
				top = MathF.Max( top, z );
			}
		}
		return bottom <= top;
	}

	/// <summary>The first face of the model at the pose that the line meets inside it, in the original's node and face order, and the point.</summary>
	public static bool TryHitLine( ModelFile model, IReadOnlyList<Matrix4x4> nodes, Func<int, bool> hidden, Func<int, IReadOnlyList<NVector3>?> vertexPositions,
		Matrix4x4 placement, NVector3 origin, NVector3 direction, out NVector3 hit )
	{
		hit = default;
		var root = model.RootNodeIndex;
		if ( root < 0 || root >= model.Nodes.Count )
			return false;
		var built = MeshesOf( model );
		// the depth-first walk: a node, then its children from the first along the siblings
		var stack = new Stack<int>();
		var visited = new HashSet<int>();
		stack.Push( root );
		while ( stack.Count > 0 )
		{
			var node = stack.Pop();
			if ( node < 0 || node >= model.Nodes.Count || !visited.Add( node ) )
				continue;
			if ( node < nodes.Count && TryHitNode( model, built, node, nodes[node], hidden, vertexPositions, placement, origin, direction, out hit ) )
				return true;
			// the children in reverse onto the stack, so the first child is walked first
			var children = new List<int>();
			for ( var child = model.Nodes[node].FirstChildIndex; child >= 0 && child < model.Nodes.Count && children.Count <= model.Nodes.Count; child = model.Nodes[child].NextSiblingIndex )
				children.Add( child );
			for ( var index = children.Count - 1; index >= 0; index-- )
				stack.Push( children[index] );
		}
		return false;
	}

	private static bool TryHitNode( ModelFile model, Mesh?[] built, int node, Matrix4x4 transform, Func<int, bool> hidden, Func<int, IReadOnlyList<NVector3>?> vertexPositions,
		Matrix4x4 placement, NVector3 origin, NVector3 direction, out NVector3 hit )
	{
		hit = default;
		var index = model.Nodes[node].MeshIndex;
		if ( index < 0 || index >= built.Length || built[index] is not { } mesh || hidden( node ) )
			return false;
		var matrix = NodeToEngine( transform, placement );
		var animated = vertexPositions( index );
		NVector3 Position( int position ) => NVector3.Transform( animated != null && position < animated.Count ? animated[position] : mesh.Positions[position], matrix );
		// a cheap rejection that cannot change the answer: a line that misses the mesh's box (a little larger) meets none of its faces
		if ( animated == null && !LineMeetsBox( origin, direction, mesh.BoundsMin, mesh.BoundsMax, matrix ) )
			return false;
		var faces = mesh.Faces;
		foreach ( var face in mesh.Order )
		{
			var a = Position( faces[face * 3] );
			var b = Position( faces[face * 3 + 1] );
			var c = Position( faces[face * 3 + 2] );
			var normal = NVector3.TransformNormal( mesh.FaceNormals[face], matrix );
			if ( ObjectRayPick.TryHitFace( origin, direction, a, b, c, normal, out hit ) )
				return true;
		}
		return false;
	}

	/// <summary>Whether the line meets the mesh box turned into the engine (its corners' axis box, grown by 1 % and a small margin).</summary>
	private static bool LineMeetsBox( NVector3 origin, NVector3 direction, NVector3 localMin, NVector3 localMax, Matrix4x4 matrix )
	{
		var min = new NVector3( float.PositiveInfinity );
		var max = new NVector3( float.NegativeInfinity );
		for ( var corner = 0; corner < 8; corner++ )
		{
			var local = new NVector3( (corner & 1) == 0 ? localMin.X : localMax.X, (corner & 2) == 0 ? localMin.Y : localMax.Y, (corner & 4) == 0 ? localMin.Z : localMax.Z );
			var world = NVector3.Transform( local, matrix );
			min = NVector3.Min( min, world );
			max = NVector3.Max( max, world );
		}
		var margin = (max - min) * 0.01f + new NVector3( 1e-3f );
		min -= margin;
		max += margin;
		double enter = double.NegativeInfinity, exit = double.PositiveInfinity;
		return Slab( origin.X, direction.X, min.X, max.X, ref enter, ref exit ) && Slab( origin.Y, direction.Y, min.Y, max.Y, ref enter, ref exit )
			&& Slab( origin.Z, direction.Z, min.Z, max.Z, ref enter, ref exit ) && enter <= exit;
	}

	private static bool Slab( double origin, double direction, double min, double max, ref double enter, ref double exit )
	{
		if ( direction == 0 )
			return origin >= min && origin <= max;
		var a = (min - origin) / direction;
		var b = (max - origin) / direction;
		enter = Math.Max( enter, Math.Min( a, b ) );
		exit = Math.Min( exit, Math.Max( a, b ) );
		return enter <= exit;
	}
}
