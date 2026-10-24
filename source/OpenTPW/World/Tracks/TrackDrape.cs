namespace OpenTPW;

/// <summary>
/// The terrain drape of the kart and water track pieces (docs/TRACKS.md "Rendering"): the original loads every piece with the drape load flag, so each mesh vertex of a placed
/// tile is moved up by the landscape under it minus the tile's instance height, and the vehicles' height sampler reads the draped vertices (mode 2) or the terrain (mode 1).
/// </summary>
// [BIN:TPW-EXE:0x00522AA0 kart kit loader] 0x4629D0 gets load flags 0x10422 (the water loader 0x522C94 the same): bit 0x2 becomes 0x40, which makes 0x461F10 OR 0x20000 into every mesh node and set header bit 0x80 (0x4625F4)
// [BIN:TPW-EXE:0x00522B90 kart instancer] the instance is created by 0x463060 with flags 0x33A; flag 8 calls 0x469940 → 0x469A80, which moves every vertex of a node with 0x20000 to its model height + the landscape under the vertex - the instance height (0x466D70's midrange); it never changes x or z
public static class TrackDrape
{
	/// <summary>The model-unit grid position of a point in a piece's own space once the tile is placed: turned counter-clockwise by <paramref name="rotation"/> about the piece's centre, then moved to the tile.</summary>
	public static (float X, float Y) GridPosition( int x, int y, int rotation, float localX, float localY )
	{
		var angle = rotation * MathF.PI / 180;
		var (dx, dy) = (localX - 10, localY - 10);
		var (cos, sin) = (MathF.Cos( angle ), MathF.Sin( angle )); // [DET:presentation] the drawn drape and the vehicle surface of the renderer, never saved or hashed
		return (dx * cos - dy * sin + 10 + x * 10, dx * sin + dy * cos + 10 + y * 10);
	}

	/// <summary>
	/// The height, in model units, a vertex at the piece-local point (x, y) moves up on the tile at grid cell (<paramref name="x"/>, <paramref name="y"/>): the landscape there minus the tile's instance
	/// height, both in engine units, divided by the model scale.
	/// </summary>
	public static Func<float, float, float> Lift( int x, int y, int rotation, float tileHeight, Func<float, float, float> terrainHeight ) =>
		( localX, localY ) =>
		{
			var grid = GridPosition( x, y, rotation, localX, localY );
			return (terrainHeight( grid.X, grid.Y ) - tileHeight) / ObjectPlacement.ModelScale;
		};

	/// <summary>The vertices with their heights lifted by <paramref name="lift"/> at their own piece-local position, or null when no vertex moves.</summary>
	public static Vertex[]? Apply( Vertex[] vertices, Func<float, float, float> lift )
	{
		Vertex[]? draped = null;
		for ( var index = 0; index < vertices.Length; index++ )
		{
			var position = vertices[index].Position;
			var delta = lift( position.X, position.Y );
			if ( delta == 0 )
				continue;
			draped ??= (Vertex[])vertices.Clone();
			draped[index].Position = new Vector3( position.X, position.Y, position.Z + delta );
		}
		return draped;
	}
}
