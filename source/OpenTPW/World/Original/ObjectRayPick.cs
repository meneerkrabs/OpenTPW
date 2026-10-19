using System;
using System.Collections.Generic;
using NVector3 = System.Numerics.Vector3;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW;

/// <summary>
/// A placed thing whose model the cursor ray can hit: what the original's cell walk tests for the owner of a cell (tp.exe <c>0x45cdc0</c> → <c>0x45d090</c>).
/// </summary>
public interface IObjectPickShape
{
	/// <summary>The lowest and highest engine height of the shape's drawn faces at its current pose; false when it draws none.</summary>
	bool TryGetPickHeightRange( out float bottom, out float top );

	/// <summary>
	/// The first face, in the model's node order, that the line through <paramref name="origin"/> along <paramref name="direction"/> (engine space, both directions)
	/// meets inside it, and that point.
	/// </summary>
	bool TryHitPickLine( NVector3 origin, NVector3 direction, out NVector3 hit );
}

/// <summary>
/// The object half of the original's cursor pick: the ray walks the park cells from the camera outwards and tests the model of each cell's owner; the first
/// model it meets takes the cursor point when that point is nearer the camera than the terrain hit (<see cref="TerrainRayPick"/>).
/// </summary>
/// <remarks>
/// [BIN:TPW-EXE:0x45c94b object walk] after the terrain walk, 0x45bf90 starts at the camera (0x790aa0) and steps one cell (10 MD2 units, 0x41200000 or its
/// negative by the sign of the larger ground axis, 0x45c983-0x45c9d1) along that axis per pass, the other ground axis and the height by the ray's slope
/// (0x45c9a6-0x45c9f5, 0x45ca73-0x45cac4); each pass tests the cell of the point (both coordinates × 0.1, 0x6fe664, truncated by ftol, 0x45ca1d-0x45ca61,
/// 0x45cdc0) and, when the other axis crossed into another cell, the point 1.01 (0x6fe668) of the way to that crossing from the previous point (only while
/// both coordinates are not negative, 0x45cabd-0x45ccaf); the walk ends at the first owner hit (0x45ca6d, 0x45ccbb) or once the height is below the
/// terrain hit's height less one pass's drop, or below 0 when the terrain was missed (0x45c9ed-0x45ca17, 0x45ccbd-0x45ccc8)
/// [BIN:TPW-EXE:0x45cdc9 cell owner] a cell coordinate below 0 or past the grid (0x7a0314, 0x7a0374) is clamped to the edge cell (0x45cdd4-0x45ce10) before
/// the cell lookup 0x452ae0 names the owner, so a walk that starts off the grid tests the edge cells
/// [BIN:TPW-EXE:0x45ccf7 nearest hit] the owner's hit point replaces the terrain point when the terrain was missed or the owner's point is nearer the camera
/// (0x470940 distances, 0x45cd04-0x45cd35), and it is written to both outputs, the hover point 0x7a1c70 that 0x45d560 turns into the hover cell 0x7b05cc
/// (the frame loop 0x54e2b0) and the mouse point 0x7a1c90 (0x45cd44-0x45cd7b)
/// [APPROX:RIDES-021] the height window, the cell's object and its sub-instances: the original tests a cell's owner only when the ray's height over the
/// pass reaches down to the cell's height from the cell lookup 0x452ae0 (the owner's height map) and up to the model's word +0x84 (0x45cf47-0x45cfbd); the
/// port's window is the owner's drawn height range; 0x452ae0 also yields a track tile's second-layer piece instance before the owner, and an instance with
/// sub-instances (0x447790, 0x4477d0) tests each of them, which the port does not model (a coaster's or a water ride's track is not hit)
/// </remarks>
public static class ObjectRayPick
{
	/// <summary>The grid the walk steps over: the engine position of cell (0, 0)'s corner, the cell size in engine units and the cell counts.</summary>
	public readonly record struct Grid( NVector2 Origin, float CellSize, int Width, int Height );

	/// <summary>The walk's bound: past this many passes it stops even with the height still above the floor (a ray along the ground).</summary>
	private const int MaximumPasses = 4096;

	/// <summary>
	/// The cursor point of the ray from the camera <paramref name="eye"/> along <paramref name="direction"/>: the owner's hit when the walk finds one that is
	/// nearer than <paramref name="terrainHit"/> (or the terrain was missed), else false and <paramref name="hit"/> is unchanged.
	/// </summary>
	public static bool TryPick( NVector3 eye, NVector3 direction, NVector3? terrainHit, Grid grid, Func<int, int, IObjectPickShape?> ownerAt, out NVector3 hit, out IObjectPickShape? owner )
	{
		hit = default;
		owner = null;
		if ( !IsFinite( eye ) || !IsFinite( direction ) || grid.Width <= 0 || grid.Height <= 0 || !(grid.CellSize > 0) )
			return false;
		if ( !TryWalk( eye, direction, terrainHit, grid, ownerAt, out var found, out var point ) )
			return false;
		// the owner's point wins without a terrain hit, or when it is nearer the camera
		if ( terrainHit is { } terrain && !(NVector3.Distance( point, eye ) < NVector3.Distance( terrain, eye )) )
			return false;
		hit = point;
		owner = found;
		return true;
	}

	/// <summary>The walk from the camera: the first tested owner whose model the line meets, and that point.</summary>
	private static bool TryWalk( NVector3 eye, NVector3 direction, NVector3? terrainHit, Grid grid, Func<int, int, IObjectPickShape?> ownerAt, out IObjectPickShape? owner, out NVector3 point )
	{
		owner = null;
		point = default;
		// the walk in cell units over the ground (u, v) and engine height, from the camera
		var size = grid.CellSize;
		double u = (eye.X - grid.Origin.X) / size, v = (eye.Y - grid.Origin.Y) / size, height = eye.Z;
		double du = direction.X / size, dv = direction.Y / size, dh = direction.Z;
		var cache = new Dictionary<IObjectPickShape, (bool Hit, NVector3 Point)>( ReferenceEqualityComparer.Instance );
		bool Test( double cu, double cv, double low, double high, out IObjectPickShape? found, out NVector3 at )
		{
			found = null;
			at = default;
			var x = Math.Clamp( Truncate( cu ), 0, grid.Width - 1 );
			var y = Math.Clamp( Truncate( cv ), 0, grid.Height - 1 );
			if ( ownerAt( x, y ) is not { } shape || !shape.TryGetPickHeightRange( out var bottom, out var top ) )
				return false;
			// the height window: the ray's height over the pass must reach the owner's drawn range
			if ( low > top || high < bottom )
				return false;
			if ( !cache.TryGetValue( shape, out var result ) )
			{
				var hitLine = shape.TryHitPickLine( eye, direction, out var linePoint );
				result = (hitLine, linePoint);
				cache[shape] = result;
			}
			if ( !result.Hit )
				return false;
			found = shape;
			at = result.Point;
			return true;
		}
		// a ray with no ground direction looks straight down: only the cell under the camera
		// [EXT:vertical-cursor-ray] the original divides by the larger ground direction (0x45c9a9, 0x45c9db) and has no answer for a vertical ray; the park camera never looks straight down
		if ( du == 0 && dv == 0 )
		{
			if ( dh >= 0 )
				return false;
			var floor = terrainHit?.Z ?? 0;
			return Test( u, v, Math.Min( height, floor ), Math.Max( height, floor ), out owner, out point );
		}
		// the larger ground axis steps by one cell, the other and the height by the slope
		var alongV = Math.Abs( du ) <= Math.Abs( dv );
		var major = alongV ? dv : du;
		var step = major > 0 ? 1.0 : -1.0;
		var minorStep = (alongV ? du : dv) / major * step;
		var drop = dh / major * step;
		// the floor: the terrain hit's height less one pass's drop, 0 without a terrain hit
		var stop = terrainHit is { } terrain ? terrain.Z + drop : 0.0;
		if ( !(stop <= height) )
			return false;
		for ( var pass = 0; pass < MaximumPasses; pass++ )
		{
			// the pass covers the major cell of the point: the height window spans the ray over it
			var majorCoordinate = alongV ? v : u;
			var within = majorCoordinate - Math.Floor( majorCoordinate );
			var forward = step > 0 ? 1 - within : within;
			var backward = 1 - forward;
			var low = Math.Min( height - backward * drop, height + forward * drop );
			var high = Math.Max( height - backward * drop, height + forward * drop );
			if ( Test( u, v, low, high, out owner, out point ) )
				return true;
			var (lastU, lastV, lastHeight) = (u, v, height);
			if ( alongV )
			{
				u += minorStep;
				v += step;
			}
			else
			{
				u += step;
				v += minorStep;
			}
			height += drop;
			// the smaller axis crossed into another cell: the point just past the crossing
			double lastMinor = alongV ? lastU : lastV, minor = alongV ? u : v;
			if ( Truncate( minor ) != Truncate( lastMinor ) && u >= 0 && v >= 0 )
			{
				var share = minor <= lastMinor
					? Fraction( lastMinor ) / (lastMinor - minor)
					: 1 - Fraction( minor ) / (minor - lastMinor);
				share *= 1.01;
				var crossU = lastU * (1 - share) + share * u;
				var crossV = lastV * (1 - share) + share * v;
				var crossHeight = lastHeight * (1 - share) + share * height;
				if ( Test( crossU, crossV, Math.Min( crossHeight, crossHeight - drop ), Math.Max( crossHeight, crossHeight - drop ), out owner, out point ) )
					return true;
			}
			if ( !(stop <= height) )
				break;
			// [EXT:cursor-walk-bound] past the grid and moving away on the larger axis, every further pass only repeats the clamped edge cells already tested; the original has no such bound
			if ( alongV ? (step > 0 ? v > grid.Height + 1 : v < -1) : (step > 0 ? u > grid.Width + 1 : u < -1) )
				break;
		}
		return false;
	}

	/// <summary>The fractional part of a coordinate, as the original's modf (0x67b280) gives it: the sign of the coordinate.</summary>
	private static double Fraction( double value ) => value - Math.Truncate( value );

	/// <summary>The cell of a coordinate as the original's ftol takes it: truncated toward zero.</summary>
	private static int Truncate( double value ) => value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : (int)value;

	private static bool IsFinite( NVector3 vector ) => float.IsFinite( vector.X ) && float.IsFinite( vector.Y ) && float.IsFinite( vector.Z );

	/// <summary>The angle-sum threshold of the face test: the plane hit is inside the face when its three angles add up to at least this (2 pi less about 0.001).</summary>
	// [BIN:TPW-EXE:0x45d48f face test] the three angles between the normalised vectors from the plane hit to the corners (acos, 0x67b4e0) are summed and compared with the float 6.2821856 (0x6fe678; 0x45d484-0x45d49a)
	public const float InsideAngleSum = 6.2821856f;

	/// <summary>
	/// The face test of the original's model pick: the line meets the plane of face (<paramref name="a"/>, <paramref name="b"/>, <paramref name="c"/>) with
	/// <paramref name="normal"/> at a point whose angles to the three corners add up to <see cref="InsideAngleSum"/> or more.
	/// </summary>
	// [BIN:TPW-EXE:0x45d090 model face test] the plane of a face through its first corner with its stored normal turned by the instance (0x46fca0) and normalised (0x45d19e): t = (n · a − n · origin) / (n · direction)
	// with no sign test, so the whole line counts; the point is accepted by the angle sum, so a face is hit when the point lies in it (or on its edge)
	public static bool TryHitFace( NVector3 origin, NVector3 direction, NVector3 a, NVector3 b, NVector3 c, NVector3 normal, out NVector3 hit )
	{
		hit = default;
		var length = normal.Length();
		var n = normal / length;
		var t = -((-NVector3.Dot( a, n ) + NVector3.Dot( origin, n )) / NVector3.Dot( direction, n ));
		var point = direction * t + origin;
		if ( !float.IsFinite( point.X ) || !float.IsFinite( point.Y ) || !float.IsFinite( point.Z ) )
			return false;
		// a cheap rejection that cannot change the answer: a point well outside the face's box cannot reach the angle sum
		var min = NVector3.Min( a, NVector3.Min( b, c ) );
		var max = NVector3.Max( a, NVector3.Max( b, c ) );
		var margin = (max - min) * 0.01f + new NVector3( 1e-4f );
		if ( point.X < min.X - margin.X || point.Y < min.Y - margin.Y || point.Z < min.Z - margin.Z
			|| point.X > max.X + margin.X || point.Y > max.Y + margin.Y || point.Z > max.Z + margin.Z )
			return false;
		var toA = NVector3.Normalize( a - point );
		var toB = NVector3.Normalize( b - point );
		var toC = NVector3.Normalize( c - point );
		var sum = MathF.Acos( NVector3.Dot( toA, toB ) ) + MathF.Acos( NVector3.Dot( toB, toC ) ) + MathF.Acos( NVector3.Dot( toC, toA ) );
		if ( !(sum >= InsideAngleSum) )
			return false;
		hit = point;
		return true;
	}
}
