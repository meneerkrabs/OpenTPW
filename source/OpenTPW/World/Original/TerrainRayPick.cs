using System;

namespace OpenTPW;

/// <summary>
/// The terrain half of the original's cursor pick: the ray from the camera through the mouse against the heightfield's cell triangles.
/// </summary>
/// <remarks>
/// [BIN:TPW-EXE:0x45bf90 cursor ray] the pick walks the heightfield cells the ray crosses and tests each cell's two triangle planes: the triangles are
/// {(x,z), (x+1,z), (x,z+1)} and {(x+1,z+1), (x,z+1), (x+1,z)} when the cell word has bit 0x2 clear (the anti-diagonal split, bit 0x4), else
/// {(x,z+1), (x+1,z+1), (x,z)} and {(x+1,z), (x,z), (x+1,z+1)}. Each plane is intersected with the ray and the hit counts when its (x, z) lies inside
/// the cell's square, not inside the triangle itself, and it is nearer (smaller |t|) than the hits found so far; the nearest one is the terrain hit
///. The test reads no hole bit, only bit 0x2, so a hole cell is picked like any other.
/// The ray here is the engine camera's (the original builds its own from the mouse at 0x45bf90), so only the terrain intersection is the original's.
/// </remarks>
public static class TerrainRayPick
{
	/// <summary>
	/// The nearest terrain point of the ray in engine space (<paramref name="origin"/> and <paramref name="direction"/> are engine vectors; the direction need not be unit length).
	/// </summary>
	public static bool TryPick( ModelHeightfield field, System.Numerics.Vector3 origin, System.Numerics.Vector3 direction, out System.Numerics.Vector3 hit )
	{
		hit = default;
		if ( !IsFinite( origin ) || !IsFinite( direction ) )
			return false;
		var park = OriginalParkPlacement.GetOrigin( field );
		const double scale = OriginalParkPlacement.ModelScale;
		// engine (x, y, z up) to MD2 (x, y up, z): the inverse of OriginalParkPlacement.ToEngine
		var o = new Vec( (origin.X + park.X) / scale, origin.Z / scale, (origin.Y + park.Y) / scale );
		var d = new Vec( direction.X / scale, direction.Z / scale, direction.Y / scale );
		if ( !TryPickModelSpace( field, o, d, out var t ) )
			return false;
		var point = new Vec( o.X + t * d.X, o.Y + t * d.Y, o.Z + t * d.Z );
		hit = new System.Numerics.Vector3( (float)(point.X * scale - park.X), (float)(point.Z * scale - park.Y), (float)(point.Y * scale) );
		return float.IsFinite( hit.X ) && float.IsFinite( hit.Y ) && float.IsFinite( hit.Z );
	}

	private readonly record struct Vec( double X, double Y, double Z );

	private static bool IsFinite( System.Numerics.Vector3 vector ) => float.IsFinite( vector.X ) && float.IsFinite( vector.Y ) && float.IsFinite( vector.Z );

	/// <summary>The ray parameter of the nearest hit in MD2 space (x, height, z), walking the cells from the camera outwards.</summary>
	private static bool TryPickModelSpace( ModelHeightfield field, Vec o, Vec d, out double t )
	{
		t = 0;
		double sizeX = field.CellSizeX, sizeZ = field.CellSizeZ;
		if ( sizeX <= 0 || sizeZ <= 0 || field.CellCountX <= 0 || field.CellCountZ <= 0 )
			return false;
		double width = field.CellCountX * sizeX, depth = field.CellCountZ * sizeZ;
		// the part of the ray (t >= 0) over the grid
		double enter = 0, exit = double.PositiveInfinity;
		if ( !Slab( o.X, d.X, width, ref enter, ref exit ) || !Slab( o.Z, d.Z, depth, ref enter, ref exit ) || enter > exit )
			return false;
		var start = Math.Min( enter, exit );
		var cellX = Math.Clamp( (int)Math.Floor( (o.X + start * d.X) / sizeX ), 0, field.CellCountX - 1 );
		var cellZ = Math.Clamp( (int)Math.Floor( (o.Z + start * d.Z) / sizeZ ), 0, field.CellCountZ - 1 );
		var stepX = d.X > 0 ? 1 : -1;
		var stepZ = d.Z > 0 ? 1 : -1;
		var nextX = d.X == 0 ? double.PositiveInfinity : ((d.X > 0 ? cellX + 1 : cellX) * sizeX - o.X) / d.X;
		var nextZ = d.Z == 0 ? double.PositiveInfinity : ((d.Z > 0 ? cellZ + 1 : cellZ) * sizeZ - o.Z) / d.Z;
		var deltaX = d.X == 0 ? double.PositiveInfinity : sizeX / Math.Abs( d.X );
		var deltaZ = d.Z == 0 ? double.PositiveInfinity : sizeZ / Math.Abs( d.Z );
		for ( var steps = field.CellCountX + field.CellCountZ + 2; steps > 0; steps-- )
		{
			if ( TryPickCell( field, cellX, cellZ, o, d, out t ) )
				return true;
			if ( nextX < nextZ )
			{
				if ( nextX > exit )
					break;
				cellX += stepX;
				nextX += deltaX;
			}
			else
			{
				if ( nextZ > exit )
					break;
				cellZ += stepZ;
				nextZ += deltaZ;
			}
			if ( cellX < 0 || cellZ < 0 || cellX >= field.CellCountX || cellZ >= field.CellCountZ )
				break;
		}
		return false;
	}

	/// <summary>Clips [enter, exit] to the slab 0..extent of one axis (the ray runs forward only).</summary>
	private static bool Slab( double origin, double direction, double extent, ref double enter, ref double exit )
	{
		if ( direction == 0 )
			return origin >= 0 && origin <= extent;
		var a = (0 - origin) / direction;
		var b = (extent - origin) / direction;
		enter = Math.Max( enter, Math.Min( a, b ) );
		exit = Math.Min( exit, Math.Max( a, b ) );
		return true;
	}

	/// <summary>The cell's two triangle planes, each accepted inside the cell square; the nearer one wins.</summary>
	private static bool TryPickCell( ModelHeightfield field, int x, int z, Vec o, Vec d, out double t )
	{
		t = double.PositiveInfinity;
		double sizeX = field.CellSizeX, sizeZ = field.CellSizeZ;
		double x0 = x * sizeX, x1 = x0 + sizeX, z0 = z * sizeZ, z1 = z0 + sizeZ;
		var h00 = (double)field.GetCornerHeight( x, z );
		var h10 = (double)field.GetCornerHeight( x + 1, z );
		var h01 = (double)field.GetCornerHeight( x, z + 1 );
		var h11 = (double)field.GetCornerHeight( x + 1, z + 1 );
		var p00 = new Vec( x0, h00, z0 );
		var p10 = new Vec( x1, h10, z0 );
		var p01 = new Vec( x0, h01, z1 );
		var p11 = new Vec( x1, h11, z1 );
		var diagonal = (field.GetCellWord( x, z ) & 2) != 0;
		(Vec, Vec, Vec)[] triangles = diagonal
			? [(p01, p11, p00), (p10, p00, p11)]
			: [(p00, p10, p01), (p11, p01, p10)];
		var found = false;
		foreach ( var (a, b, c) in triangles )
		{
			var u = new Vec( b.X - a.X, b.Y - a.Y, b.Z - a.Z );
			var v = new Vec( c.X - a.X, c.Y - a.Y, c.Z - a.Z );
			var n = new Vec( u.Y * v.Z - u.Z * v.Y, u.Z * v.X - u.X * v.Z, u.X * v.Y - u.Y * v.X );
			var denominator = n.X * d.X + n.Y * d.Y + n.Z * d.Z;
			if ( denominator == 0 )
				continue;
			var hit = (a.X - o.X) * n.X + (a.Y - o.Y) * n.Y + (a.Z - o.Z) * n.Z;
			var candidate = hit / denominator;
			if ( !double.IsFinite( candidate ) || candidate < 0 || candidate >= t )
				continue;
			var px = o.X + candidate * d.X;
			var pz = o.Z + candidate * d.Z;
			if ( px < x0 || px > x1 || pz < z0 || pz > z1 )
				continue;
			t = candidate;
			found = true;
		}
		return found;
	}
}
