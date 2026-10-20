using System.Collections.Generic;
using OpenTPW.Hud;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>A CPU reference for the preview's depth test: what a pixel shows is the nearest triangle covering it (depth interpolated over the triangle, smaller nearer).</summary>
internal static class PreviewDepthRaster
{
	/// <summary>The depth of <paramref name="triangle"/> at <paramref name="point"/>, or null where it does not cover it (edges included).</summary>
	public static double? DepthAt( PreviewIcon.ArrangedTriangle triangle, NVector2 point )
	{
		double ax = triangle.A.X, ay = triangle.A.Y, bx = triangle.B.X, by = triangle.B.Y, cx = triangle.C.X, cy = triangle.C.Y;
		var area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
		if ( area == 0 )
			return null;
		var w0 = ((bx - point.X) * (cy - point.Y) - (cx - point.X) * (by - point.Y)) / area;
		var w1 = ((cx - point.X) * (ay - point.Y) - (ax - point.X) * (cy - point.Y)) / area;
		var w2 = 1 - w0 - w1;
		const double edge = -1e-9;
		if ( w0 < edge || w1 < edge || w2 < edge )
			return null;
		return w0 * triangle.A.Z + w1 * triangle.B.Z + w2 * triangle.C.Z;
	}

	/// <summary>The nearest of <paramref name="triangles"/> at <paramref name="point"/> (the first of equals, as LESSEQUAL draws the last), or null.</summary>
	public static PreviewIcon.ArrangedTriangle? Nearest( IEnumerable<PreviewIcon.ArrangedTriangle> triangles, NVector2 point )
	{
		PreviewIcon.ArrangedTriangle? best = null;
		var bestDepth = double.MaxValue;
		foreach ( var triangle in triangles )
			if ( DepthAt( triangle, point ) is { } depth && depth <= bestDepth )
			{
				bestDepth = depth;
				best = triangle;
			}
		return best;
	}
}

/// <summary>A per-pixel depth-tested raster of triangles (pixel centres, edges included, LESSEQUAL: the last drawn of equal depths wins).</summary>
internal sealed class DepthRaster
{
	public readonly int Width, Height;
	public readonly int[] Winner;
	public readonly double[] Depth;

	public DepthRaster( int width, int height )
	{
		Width = width;
		Height = height;
		Winner = new int[width * height];
		Depth = new double[width * height];
		System.Array.Fill( Winner, -1 );
		System.Array.Fill( Depth, double.MaxValue );
	}

	/// <summary>
	/// Draws triangle <paramref name="id"/> with corners in this raster's pixels (y down) and depths; with <paramref name="single"/> the depth is interpolated and
	/// compared in 32-bit floats, as the depth buffer holds it, else in doubles; with <paramref name="test"/> false it is painted over whatever is there.
	/// </summary>
	public void Draw( int id, System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector3 c, bool single, bool test = true )
	{
		double area = (b.X - a.X) * (double)(c.Y - a.Y) - (c.X - a.X) * (double)(b.Y - a.Y);
		if ( area == 0 )
			return;
		var minX = System.Math.Max( 0, (int)System.Math.Floor( System.Math.Min( a.X, System.Math.Min( b.X, c.X ) ) ) );
		var maxX = System.Math.Min( Width - 1, (int)System.Math.Ceiling( System.Math.Max( a.X, System.Math.Max( b.X, c.X ) ) ) );
		var minY = System.Math.Max( 0, (int)System.Math.Floor( System.Math.Min( a.Y, System.Math.Min( b.Y, c.Y ) ) ) );
		var maxY = System.Math.Min( Height - 1, (int)System.Math.Ceiling( System.Math.Max( a.Y, System.Math.Max( b.Y, c.Y ) ) ) );
		for ( var y = minY; y <= maxY; y++ )
			for ( var x = minX; x <= maxX; x++ )
			{
				double px = x + 0.5, py = y + 0.5;
				var w0 = ((b.X - px) * (c.Y - py) - (c.X - px) * (b.Y - py)) / area;
				var w1 = ((c.X - px) * (a.Y - py) - (a.X - px) * (c.Y - py)) / area;
				var w2 = 1 - w0 - w1;
				if ( w0 < 0 || w1 < 0 || w2 < 0 )
					continue;
				var depth = single ? (double)((float)w0 * a.Z + (float)w1 * b.Z + (float)w2 * c.Z) : w0 * a.Z + w1 * b.Z + w2 * c.Z;
				var index = y * Width + x;
				if ( test && depth > Depth[index] )
					continue;
				Depth[index] = depth;
				Winner[index] = id;
			}
	}
}
