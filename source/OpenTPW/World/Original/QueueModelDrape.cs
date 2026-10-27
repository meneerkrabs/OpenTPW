using Matrix4x4 = System.Numerics.Matrix4x4;

namespace OpenTPW;

/// <summary>
/// How a queue cell's model sits on the terrain (docs/PATHS.md): the model is placed like a one-cell object and every vertex of every mesh node is draped onto the landscape
/// (<see cref="ModelDrape"/>), so a queue on a slope follows the ground and neighbouring cells meet at the grid lines. Presentation only: no simulation state, world hash or save field reads
/// these vertices.
/// </summary>
// [BIN:TPW-EXE:0x00522900 queue models] the eight models load with 0x4629D0 flags 0x12423 (both quedead 0x112423): bit 0x2 becomes loader flag 0x40, which ORs 0x20000 into every mesh node (0x461F10 at 0x46262B) and sets the header bit 0x80
// [BIN:TPW-EXE:0x005229E0 queue model] the placement 0x463060 (flags 0x33A) puts the root at the cell (x, y), the yaw 360 - rotation and the instance height 0x466D70 returns for the one-cell footprint, then flag 8 calls 0x469940 → 0x469A80, which drapes every node with 0x20000
public static class QueueModelDrape
{
	/// <summary>
	/// The instance height of a queue cell's model: half the sum of the lowest and the highest of the cell's four corner heights (0x466D70 over the 1 x 1 footprint {1, 1} that 0x522900 stores
	/// at 0x816D68 and the all-cells flag 0x10 of the creation flags), in single precision. The vertex heights of the drape cancel it; it only decides the rounding.
	/// </summary>
	// [BIN:TPW-EXE:0x00466D70 base height] ((float10)lowest + (float10)highest) * 0.5 over the 4 grid vertices of each footprint cell; the vertex of a corner is ftol(lerp + 0.1) (0x454050), for one cell the cell's own four corners whatever the rotation
	public static float BaseHeight( ModelHeightfield field, int x, int y )
	{
		ArgumentNullException.ThrowIfNull( field );
		var (low, high) = (float.PositiveInfinity, float.NegativeInfinity);
		for ( var corner = 0; corner < 4; corner++ )
		{
			var height = field.GetCornerHeight( Math.Min( x + (corner & 1), field.CellCountX ), Math.Min( y + (corner >> 1), field.CellCountZ ) );
			low = MathF.Min( low, height );
			high = MathF.Max( high, height );
		}
		float sum = low + high;
		return sum * 0.5f;
	}

	/// <summary>
	/// The root node's matrix of the instance in the MD2 frame (x, y up, z): the root node's own matrix (<paramref name="root"/>, which the Space models' bends carry a quarter turn in) turned by the
	/// yaw with the sine table's cosine and sine (not exactly 0 and 1), and only its translation replaced: the cell's origin on the grid and the instance height. The cell is the one 0x466F90 advances for
	/// the quarter turns (90 a row, 180 a column and a row, 270 a column).
	/// </summary>
	// [BIN:TPW-EXE:0x00467030 instance placement] creation flag 0x20: the node's matrix is turned by 0x46F650 (table cosine and sine of the angle) and the words +0x40 / +0x44 / +0x48 are set to (cell x * size + origin, instance height, cell y * size + origin); the cell advances are 0x466F90's
	public static Matrix4x4 Placement( ModelHeightfield field, Matrix4x4 root, int x, int y, int yaw, float baseHeight )
	{
		ArgumentNullException.ThrowIfNull( field );
		var (sin, cos) = ModelDrape.YawSinCos( yaw );
		var placement = ModelDrape.RotateYaw( root, cos, sin );
		var (column, row) = (x + (yaw is 180 or 270 ? 1 : 0), y + (yaw is 90 or 180 ? 1 : 0));
		placement.M41 = column * field.CellSizeX;
		placement.M42 = baseHeight;
		placement.M43 = row * field.CellSizeZ;
		return placement;
	}

	/// <summary><see cref="Placement(ModelHeightfield, Matrix4x4, int, int, int, float)"/> with the model's own root node matrix.</summary>
	public static Matrix4x4 Placement( ModelHeightfield field, ModelFile model, int x, int y, int yaw, float baseHeight ) =>
		Placement( field, model.RootNodeIndex >= 0 && model.RootNodeIndex < model.Nodes.Count ? model.Nodes[model.RootNodeIndex].Transform : Matrix4x4.Identity, x, y, yaw, baseHeight );

	/// <summary>
	/// The chain matrix of every node of <paramref name="model"/> in the grid's frame (<see cref="ModelDrape.Chain"/>): the root's own matrix is replaced by <paramref name="placement"/>, as the
	/// instance's root is, and every other node's matrix is applied to its parent's chain. A node without a parent (or in a parent cycle) is a root.
	/// </summary>
	public static Matrix4x4[] Chains( ModelFile model, Matrix4x4 placement )
	{
		ArgumentNullException.ThrowIfNull( model );
		var chains = new Matrix4x4?[model.Nodes.Count];
		Matrix4x4 Resolve( int index, int depth )
		{
			if ( chains[index] is { } known )
				return known;
			var parent = model.Nodes[index].ParentIndex;
			var chain = parent < 0 || parent >= chains.Length || depth > chains.Length
				? ModelDrape.Chain( placement, Matrix4x4.Identity )
				: ModelDrape.Chain( model.Nodes[index].Transform, Resolve( parent, depth + 1 ) );
			chains[index] = chain;
			return chain;
		}
		var result = new Matrix4x4[chains.Length];
		for ( var index = 0; index < result.Length; index++ )
			result[index] = Resolve( index, 0 );
		return result;
	}

	/// <summary>
	/// A vertex of a mesh node on the cell, in the grid's frame (x, height, z) and the grid's units: through <paramref name="chain"/>, with its height replaced by the landscape under it
	/// (the drape's triangle, <see cref="LandscapeSampler.SampleDrape"/>) minus the instance height plus its own height.
	/// </summary>
	public static System.Numerics.Vector3 Vertex( ModelHeightfield field, Matrix4x4 chain, System.Numerics.Vector3 vertex, float baseHeight )
	{
		ArgumentNullException.ThrowIfNull( field );
		return ModelDrape.Drape( chain, vertex, baseHeight, ( x, z ) => LandscapeSampler.SampleDrape( field.CellCountX, field.CellCountZ, field.CellSizeX, field.CellSizeZ, field.GetCornerHeight, field.SplitsAlongAntiDiagonal, x, z ) );
	}
}
