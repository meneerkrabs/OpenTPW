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
	/// <summary>
	/// The engine height of the model's bounding-box minimum (MD2 header +0x84, its Y, × the engine scale): the walk's window tests it against the ray's highest
	/// point over the pass, as an absolute height (0x45cfaf-0x45cfbd reads the model's own word, not adding the instance's height).
	/// </summary>
	float PickBottom { get; }

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
/// [BIN:TPW-EXE:0x45cdc0 cell owner test] the cell lookup 0x452ae0 names the cell's instance: the second-layer piece instance first (0x53bf30), else the
/// instance (+0x20) of the class-3 thing that owns the cell (0x527e80), and only one with a height map (+0xcc): its height there is the map byte × 100/255
/// plus the instance's height (ObjectHeightMaps.Height); without a map no instance is named and the cell is not tested (0x452ae0's out-pointer is cleared);
/// the instance is tested when the ray's lowest height over the pass (the pass point's height less the share of the pass's drop to the cell's near edge
/// along the larger axis, 0x45cee6-0x45cf8f) is at most that height and its highest at least the model's bounds minimum (0x45cf8f-0x45cfbd)
/// [APPROX:RIDES-021] a cell whose second-layer piece instance answers (a kart or water ride's track tile) is not tested, as the original tests the piece's
/// model there, which the port does not hold; an instance with the group flag 0x80000 tests its group's members instead (0x447790, 0x4477d0), which the port
/// does not model; nor are the model walk's node state bits 0x20 and 0x8000 and the normal refresh 0x473c30 (ObjectPickModel); the hit instance 0x45bf90 hands to 0x469340 (0x45ccd5),
/// which the hover handler passes to 0x469180 (its normals copied, node flag 0x1000000 and model flag 0x400000 set: a hover highlight, not read further), is not used
/// </remarks>
public static class ObjectRayPick
{
	/// <summary>The grid the walk steps over: the engine position of cell (0, 0)'s corner, the cell size in engine units and the cell counts.</summary>
	public readonly record struct Grid( NVector2 Origin, float CellSize, int Width, int Height );

	/// <summary>The owner a cell names for the walk: its pick shape and the engine height the cell lookup gives it there (<c>0x452ae0</c>).</summary>
	public readonly record struct Owner( IObjectPickShape Shape, float Top );

	/// <summary>
	/// The cursor point of the ray from the camera <paramref name="eye"/> along <paramref name="direction"/>: the owner's hit when the walk finds one that is
	/// nearer than <paramref name="terrainHit"/> (or the terrain was missed), else false and <paramref name="hit"/> is unchanged.
	/// </summary>
	public static bool TryPick( NVector3 eye, NVector3 direction, NVector3? terrainHit, Grid grid, Func<int, int, Owner?> ownerAt, out NVector3 hit, out IObjectPickShape? owner )
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
	private static bool TryWalk( NVector3 eye, NVector3 direction, NVector3? terrainHit, Grid grid, Func<int, int, Owner?> ownerAt, out IObjectPickShape? owner, out NVector3 point )
	{
		owner = null;
		point = default;
		// the walk in cell units over the ground (u, v) and engine height, from the camera
		var size = grid.CellSize;
		double u = (eye.X - grid.Origin.X) / size, v = (eye.Y - grid.Origin.Y) / size, height = eye.Z;
		double du = direction.X / size, dv = direction.Y / size, dh = direction.Z;
		var cache = new Dictionary<IObjectPickShape, (bool Hit, NVector3 Point)>( ReferenceEqualityComparer.Instance );
		// the floor: the terrain hit's height less one pass's drop, 0 without a terrain hit (0x45c9ed-0x45ca08); the walk's only exit besides a hit is the height
		// falling below it. A ray straight down divides by a zero ground direction (0x45c9a9, 0x45c9db): with a terrain hit the floor is infinite and no cell is tested,
		// as here (without one the original would step forever; an exactly vertical ray over no terrain is a single pixel's case at most)
		if ( !(Math.Abs( du ) > 0 || Math.Abs( dv ) > 0) )
			return false;
		var alongV = Math.Abs( du ) <= Math.Abs( dv );
		var major = alongV ? dv : du;
		var step = major > 0 ? 1.0 : -1.0;
		var minorStep = (alongV ? du : dv) / major * step;
		var drop = dh / major * step;
		// [BIN:TPW-EXE:0x54e2c0 pick camera] the frame loop runs the picker only while the camera-mode byte 0x790ab0 has none of the eye bits 0x16, and with those bits
		// clear the camera matrix takes the zoom's pitch (0x42c6b6, 0x42ca04 → 0x42cc02-0x42cc15): (1 - t) × 0.7853982 + t × 1.134464 for t = (zoom - 70) / 60 over the zoom
		// clamp 70..130, or 40..300 with the mode flag 0x200 (0x42c621-0x42c6a1, 0x42c742-0x42c790), so at least 0.6109 rad (35 degrees), clamped to 0..1.4707963
		// (0x42c790-0x42c7c0); the lean that follows (0x46f870 mode 9, 'ZX' at 0x74da90) adds no pitch, its X turn 0x7908e4 being set to 0 whatever the velocity
		// (0x42c8d2-0x42c95d), only a roll of 0.005 sqrt(100 |v|) - 0.001 from the pan velocity v (0x7908e0, 0x42c850-0x42c8c8), which each frame is halved toward
		// 0.02 sqrt(zoom) times the pan input (0x42bc2a-0x42bc96; the edge input is (pointer share - 0.95) x 10, 0x42ba75-0x42ba9b), so the roll stays below the
		// 20 degrees that would lift a frustum corner for any pan input under 140; the picker's frustum
		// corners are (+-0.512, +-0.384, 0.768) in the camera's frame (0x45bf90's start: angle pi/2 x 0.885 / 2 at 0x74c9b8, 0x74d13c, turned by atan 0.75),
		// so its highest ray rises atan(0.5) = 26.57 degrees above the view direction and every ray falls (the picker's only caller is the park's frame
		// callback 0x54e2b0, registered at 0x54ecbc); OpenTPW's park camera keeps the same pitch line and
		// half height 0.5 (ParkCameraMode.PitchOf, OriginalProjection) and has no lean, so a ray that does not fall never comes from the park camera and there is no walk
		// to port for it (the original's loop would not end)
		if ( !(drop < 0) )
			return false;
		var stop = terrainHit is { } terrain ? terrain.Z + drop : 0.0;
		if ( !(stop <= height) )
			return false;
		bool Test( double cu, double cv, double passHeight, out IObjectPickShape? found, out NVector3 at )
		{
			found = null;
			at = default;
			var x = Math.Clamp( Truncate( cu ), 0, grid.Width - 1 );
			var y = Math.Clamp( Truncate( cv ), 0, grid.Height - 1 );
			if ( ownerAt( x, y ) is not { } named )
				return false;
			// the window: the ray over the pass, from the cell's edge behind the point along the larger axis to its edge ahead
			var within = Fraction( alongV ? cv : cu );
			var behind = step > 0 ? within : 1 - within;
			var first = passHeight - behind * drop;
			var last = (1 - behind) * drop + passHeight;
			if ( Math.Min( first, last ) > named.Top || Math.Max( first, last ) < named.Shape.PickBottom )
				return false;
			var shape = named.Shape;
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
		while ( true )
		{
			if ( Test( u, v, height, out owner, out point ) )
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
				if ( Test( lastU * (1 - share) + share * u, lastV * (1 - share) + share * v, lastHeight * (1 - share) + share * height, out owner, out point ) )
					return true;
			}
			if ( !(stop <= height) )
				return false;
		}
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
		var toA = NVector3.Normalize( a - point );
		var toB = NVector3.Normalize( b - point );
		var toC = NVector3.Normalize( c - point );
		float ab = NVector3.Dot( toA, toB ), bc = NVector3.Dot( toB, toC ), ca = NVector3.Dot( toC, toA );
		// a rejection that keeps the answer: acos(d) = 2 asin(sqrt((1 - d) / 2)) is at most pi sqrt((1 - d) / 2), so when those bounds add up to less than the
		// threshold (with room for the float rounding) the angles do too; it spares the arc cosines of the faces the line passes far from
		if ( MathF.PI * (MathF.Sqrt( MathF.Max( 0, (1 - ab) / 2 ) ) + MathF.Sqrt( MathF.Max( 0, (1 - bc) / 2 ) ) + MathF.Sqrt( MathF.Max( 0, (1 - ca) / 2 ) )) < InsideAngleSum - 1e-3f )
			return false;
		var sum = DetMathF.Acos( ab ) + DetMathF.Acos( bc ) + DetMathF.Acos( ca );
		if ( !(sum >= InsideAngleSum) )
			return false;
		hit = point;
		return true;
	}
}
