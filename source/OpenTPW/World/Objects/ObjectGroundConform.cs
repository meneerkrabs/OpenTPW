using NVector3 = System.Numerics.Vector3;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace OpenTPW;

/// <summary>
/// The edge conform of a placed object (docs/OBJECTS.md, "Floors"): every ground vertex that the type loader listed (<see cref="ObjectSkirt.ConformList"/>) is moved onto the terrain's nearest
/// grid corner, its ground coordinates onto the grid lines and its height onto that corner's height, whatever the procedural texturing option says. A copy of the node's positions is
/// changed; the type's own positions stay. Presentation only: no simulation state reads these positions.
/// </summary>
// [BIN:TPW-EXE:0x00469940 ground conform] the instance constructor 0x00463060 (creation flag 8, every non-fixed object) builds the placement matrix of the instance root (0x0046F150) and walks the type's nodes with 0x00469A80 in its apply mode, then the nodes with their own list (+0x98, flag 0x40) a second time with tolerances 0.1 and 100; a node with flag 0x200 is skipped (not its children), a node without flag 0x1000 changes nothing
internal static class ObjectGroundConform
{
	/// <summary>The positions each mesh of the model has after the conform (MD2 axes, node-local, indexed like <see cref="ModelFile.Mesh.Positions"/>); meshes whose vertices did not move are absent.</summary>
	/// <param name="Positions">The conformed positions by mesh index.</param>
	/// <param name="Processed">The listed vertices the walk reached, as (mesh index, position index) in the order of the list; fewer than the list when a node ended at the terrain's edge.</param>
	internal sealed record Result( IReadOnlyDictionary<int, NVector3[]> Positions, IReadOnlyList<(int Mesh, int Vertex)> Processed );

	/// <summary>
	/// The placement matrix of the instance root in the model's axes (MD2 X, Y up, Z), in model units on the terrain grid (a cell is 10 units): the rigid turn about the first cell, the anchor
	/// cell and the base height. Row vectors, as the original's matrices.
	/// </summary>
	internal static Matrix4x4 RootPlacement( ObjectPlacement placement ) =>
		ObjectPlacement.SwapAxes( ObjectPlacement.CellRotation( placement.Rotation )
			* Matrix4x4.CreateTranslation( placement.X * 10, placement.Y * 10, placement.BaseHeight / ObjectPlacement.ModelScale ) );

	/// <summary>
	/// The conform of <paramref name="skirt"/>'s listed vertices for an instance whose root has the matrix <paramref name="root"/> over the terrain grid <paramref name="terrain"/> whose corner heights
	/// <paramref name="cornerHeight"/> returns (model units, column and row of the corner).
	/// </summary>
	internal static Result Apply( ModelFile model, ObjectSkirt skirt, HoardingTerrain terrain, Func<int, int, float> cornerHeight, Matrix4x4 root )
	{
		var changed = new Dictionary<int, NVector3[]>();
		var processed = new List<(int, int)>();
		if ( skirt.ConformList.Length == 0 || model.RootNodeIndex < 0 )
			return new Result( changed, processed );
		// the model's nodes in pre-order, each with its stored matrix, the flags the walk reads (0x200 skip; 0x1000 listed; the drape bit 0x20000 as the model has it) and its stored positions
		var tree = new List<TreeNode>();
		var owners = new List<int>();
		void Collect( int nodeIndex, int parent )
		{
			var node = model.Nodes[nodeIndex];
			var flags = (int)(node.Type & 0x20200);
			if ( nodeIndex < skirt.ConformNodes.Length && skirt.ConformNodes[nodeIndex] )
				flags |= 0x1000;
			var hasMesh = node.MeshIndex >= 0 && node.MeshIndex < model.Meshes.Count;
			tree.Add( new TreeNode( parent, flags, ObjectFloorMath.ToArray( node.Transform ),
				hasMesh ? model.Meshes[node.MeshIndex].Positions.Select( stored => new NVector3( stored.X, stored.Y, stored.Z ) ).ToArray() : [] ) );
			owners.Add( hasMesh ? node.MeshIndex : -1 );
			var index = tree.Count - 1;
			for ( var child = node.FirstChildIndex; child >= 0; child = model.Nodes[child].NextSiblingIndex )
				Collect( child, index );
		}
		Collect( model.RootNodeIndex, -1 );
		var taken = Walk( tree, skirt.ConformList, terrain, cornerHeight, ObjectFloorMath.ToArray( root ) );
		foreach ( var (node, vertex) in taken )
		{
			processed.Add( (owners[node], vertex) );
			changed[owners[node]] = tree[node].Result;
		}
		return new Result( changed, processed );
	}

	/// <summary>One node of the instance's tree for the conform walk: its parent in the pre-order list (-1 for the root), its flags (0x1000 takes the listed vertices, 0x200 none, 0x20000 a drape), its stored matrix and its vertices; <see cref="Result"/> is the instance's copy after the walk.</summary>
	internal sealed class TreeNode( int parent, int flags, float[] matrix, NVector3[] source )
	{
		public int Parent { get; } = parent;
		public int Flags { get; } = flags;
		public float[] Matrix { get; } = matrix;
		public NVector3[] Source { get; } = source;
		public NVector3[] Result { get; } = (NVector3[])source.Clone();
		/// <summary>The bounds, minimum x y z then maximum x y z; the walk widens them by every conformed vertex.</summary>
		public float[] Bounds { get; set; } = new float[6];
	}

	/// <summary>
	/// The apply walk (tp.exe <c>0x00469A80</c>) over a tree in pre-order from the root with <paramref name="incoming"/> as the chain's start: each listed vertex of a node with flag 0x1000 is conformed in
	/// <see cref="TreeNode.Result"/>. Returns the (node, vertex) pairs it took, in list order. A drape node (flag 0x20000) takes nothing here: queue and track drapes are the drape pass's.
	/// </summary>
	// [BIN:TPW-EXE:0x00469A80 apply mode] per listed position p of the instance's copy: w = p · M with M the node's matrix times its parents' down to the root placement (0x004702D0, 0x0046FC00); per ground axis t = float((w − origin) / cell size) raised to 0, its whole part i and fraction f; f ≥ 0.5 names corner i + 1 (the node ends when the corner count + 1 ≤ i + 1) else corner i (ends when count + 1 ≤ i); a fraction within 0.01 of 0 or 1 snaps w onto that grid line; w's height becomes the heights table's entry of the chosen corner (+0x28, row stride count + 1); p becomes w · M⁻¹ (0x00470040, an LU inverse in floats); the bounds widen by it; the list pointer advances once per vertex taken and reads as 0 past its end
	internal static List<(int Node, int Vertex)> Walk( IReadOnlyList<TreeNode> tree, IReadOnlyList<ushort> list, HoardingTerrain terrain, Func<int, int, float> cornerHeight, float[] incoming )
		=> Walk( tree, list.Select( entry => (int)entry ).ToArray(), terrain, cornerHeight, incoming );

	internal static List<(int Node, int Vertex)> Walk( IReadOnlyList<TreeNode> tree, int[] list, HoardingTerrain terrain, Func<int, int, float> cornerHeight, float[] incoming )
	{
		var taken = new List<(int, int)>();
		var pointer = 0;
		int NextEntry() => pointer < list.Length ? list[pointer] : 0;
		var children = new List<int>[tree.Count];
		for ( var index = 0; index < tree.Count; index++ )
			children[index] = new List<int>();
		for ( var index = 0; index < tree.Count; index++ )
			if ( tree[index].Parent >= 0 )
				children[tree[index].Parent].Add( index );

		void Visit( int index, float[] parent )
		{
			var node = tree[index];
			var matrix = ObjectFloorMath.Chain( node.Matrix, parent );
			if ( (node.Flags & 0x200) == 0 && (node.Flags & 0x20000) == 0 && (node.Flags & 0x1000) != 0 )
			{
				var inverse = ObjectFloorMath.Inverse( matrix );
				for ( var vertex = 0; vertex < node.Source.Length; vertex++ )
				{
					if ( vertex != NextEntry() )
						continue;
					// a corner past the last one ends this node and its subtree
					if ( Snap( node.Source[vertex], matrix, inverse, terrain, cornerHeight ) is not { } moved )
						return;
					node.Result[vertex] = moved;
					Widen( node, moved );
					taken.Add( (index, vertex) );
					pointer++;
				}
			}
			foreach ( var child in children[index] )
				Visit( child, matrix );
		}

		if ( tree.Count > 0 )
			Visit( 0, incoming );
		return taken;
	}

	private static void Widen( TreeNode node, NVector3 moved )
	{
		var bounds = node.Bounds;
		for ( var axis = 0; axis < 3; axis++ )
		{
			var value = axis == 0 ? moved.X : axis == 1 ? moved.Y : moved.Z;
			if ( value < bounds[axis] )
				bounds[axis] = value;
			else if ( value > bounds[3 + axis] )
				bounds[3 + axis] = value;
		}
	}

	/// <summary>Moves one listed vertex; null when its corner lies past the terrain's last corner (the walk then ends the node).</summary>
	private static NVector3? Snap( NVector3 stored, float[] matrix, float[] inverse, HoardingTerrain terrain, Func<int, int, float> cornerHeight )
	{
		var world = ObjectFloorMath.TransformPoint( matrix, stored );
		var corner = new int[2];
		for ( var axis = 0; axis < 2; axis++ )
		{
			var coordinate = axis == 0 ? world.X : world.Z;
			var origin = axis == 0 ? terrain.OriginX : terrain.OriginZ;
			var size = axis == 0 ? terrain.CellSizeX : terrain.CellSizeZ;
			var count = axis == 0 ? terrain.CountX : terrain.CountZ;
			var value = size != 0 ? ((double)coordinate - origin) / size : double.PositiveInfinity;
			var t = value < 0 ? 0f : (float)value;
			var whole = Math.Truncate( (double)t );
			var fraction = (float)(t - whole);
			var upper = fraction >= 0.5f;
			if ( upper )
			{
				if ( whole + 1.0 < 0 || !(whole + 1.0 < count + 1) )
					return null;
			}
			else if ( whole < 0 || !(whole < count + 1) )
				return null;
			corner[axis] = (int)whole + (upper ? 1 : 0);
			if ( Math.Abs( fraction ) < ConformTolerance || Math.Abs( (double)fraction - 1.0 ) < ConformTolerance )
			{
				var snapped = (float)((upper ? whole + 1.0 : whole) * size + origin);
				if ( axis == 0 )
					world.X = snapped;
				else
					world.Z = snapped;
			}
		}
		world.Y = cornerHeight( corner[0], corner[1] );
		return ObjectFloorMath.TransformPoint( inverse, world );
	}

	private static readonly float ConformTolerance = 0.01f;
}
