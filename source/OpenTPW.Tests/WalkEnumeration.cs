using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTPW.Tests;

/// <summary>
/// Bounded enumeration of walks with OpenTPW's steering (<see cref="GuestNavigation"/>), the C# counterpart of
/// <c>walk_evidence.worst_walk</c> (docs/reverse/WALK-plan.md §9) plus the queue-route enumeration behind the M3 gate's
/// per-cell walk term (WALK-plan §11.7). A walk fails when it reroutes or exceeds the update limit.
/// </summary>
internal static class WalkEnumeration
{
	public const int One = Fixed16.One;

	/// <summary>
	/// Updates until the walk step reports arrival, or −1 when the walk hit the limit or, unless
	/// <paramref name="followReroutes"/>, rerouted (walk_evidence counts a reroute as a failure).
	/// </summary>
	public static int Walk( (int X, int Y) position, (int X, int Y) velocity, (int X, int Y) target, IReadOnlyList<(int X, int Y)> route,
		float speed, Func<int, int, bool> isOpen, bool opposeSeparation = false, int limit = 5000, bool followReroutes = false )
	{
		return Walk( position, velocity, target, route, speed, isOpen, out _, opposeSeparation, limit, followReroutes );
	}

	public static int Walk( (int X, int Y) position, (int X, int Y) velocity, (int X, int Y) target, IReadOnlyList<(int X, int Y)> route,
		float speed, Func<int, int, bool> isOpen, out int reroutes, bool opposeSeparation = false, int limit = 5000, bool followReroutes = false )
	{
		var (maxSpeed, maxForce) = GuestSpeed.Caps( speed );
		var navigation = new GuestNavigation { X = position.X, Y = position.Y, VelocityX = velocity.X, VelocityY = velocity.Y };
		navigation.SetDestination( target.X, target.Y, route );
		reroutes = 0;
		for ( var update = 1; update <= limit; update++ )
		{
			var arrived = navigation.Step( maxSpeed, maxForce, isOpen, opposeSeparation ? opposing : null );
			reroutes = navigation.Reroutes;
			if ( reroutes > 0 && !followReroutes )
				return -1;
			if ( arrived )
				return update;
		}
		return -1;
	}

	private static readonly Func<GuestNavigation, (int X, int Y)> opposing = OpposingNeighbour;
	[ThreadStatic] private static GuestNavigation[]? ahead;

	/// <summary>
	/// WALK-plan §9 variant "oppose": one neighbour 0.1 cell ahead on the way to follow_path's goal, so the traced
	/// separation (<see cref="GuestNavigation.Separation"/>) pushes straight against the walk with about ONE, which the
	/// step truncates to the max force: the largest separation force wherever the traced on/off rule leaves it on.
	/// </summary>
	public static (int X, int Y) OpposingNeighbour( GuestNavigation self )
	{
		var (goalX, goalY) = self.Goal;
		var (dx, dy) = (goalX - self.X, goalY - self.Y);
		var size = Fixed16.Length( dx, dy );
		if ( size == 0 )
			return (0, 0);
		ahead ??= [new GuestNavigation()];
		var scale = Fixed16.Div( One / 10, size );
		(ahead[0].X, ahead[0].Y) = (self.X + Fixed16.Mul( dx, scale ), self.Y + Fixed16.Mul( dy, scale ));
		return GuestNavigation.Separation( self, ahead );
	}

	public static (int X, int Y) CellOf( (int X, int Y) point ) => (point.X >> 16, point.Y >> 16);

	/// <summary>Zero, and 8 directions at full and half max speed (route setup keeps the old velocity).</summary>
	public static IEnumerable<(int X, int Y)> StaleVelocities( float speed )
	{
		var (maxSpeed, _) = GuestSpeed.Caps( speed );
		yield return (0, 0);
		foreach ( var size in new[] { maxSpeed, maxSpeed / 2 } )
			for ( var k = 0; k < 8; k++ )
				yield return ((int)(size * Math.Cos( k * Math.PI / 4 )), (int)(size * Math.Sin( k * Math.PI / 4 )));
	}

	// ---- walk_evidence's straight strip (WALK-plan §8) -------------------------------------------------------------

	public static readonly (int X, int Y) Entrance = (0, -1), Front = (0, 0), Behind = (0, 1);
	private static readonly HashSet<(int X, int Y)> Strip = new() { Entrance, Front, Behind };
	private static readonly int[] Laterals = { 114, 141 };

	public static bool InStrip( int x, int y ) => Strip.Contains( (x, y) );

	public static (int X, int Y) StandPoint() => GuestNavigation.BytePoint( Entrance, 127, 127 );

	public static (int X, int Y) SlotPoint( int slot, int lateral, bool reversedAxis = false )
	{
		var depth = new[] { 0, 63, 127, 191 }[slot];
		return GuestNavigation.BytePoint( Front, lateral, reversedAxis ? 255 - depth : depth );
	}

	public static List<(int X, int Y)> StripRoute( (int X, int Y) start, (int X, int Y) destination )
	{
		if ( start == destination )
			return [destination];
		var step = destination.Y > start.Y ? 1 : -1;
		var route = new List<(int X, int Y)>();
		for ( var y = start.Y; y != destination.Y + step; y += step )
			route.Add( (start.X, y) );
		return route;
	}

	/// <summary>Standing starts: within arrival radius + 2 × max speed of the origin, in the strip, a radius from its sides and ends.</summary>
	public static IEnumerable<(int X, int Y)> StandingStarts( (int X, int Y) origin, float speed, int grid )
	{
		var (maxSpeed, _) = GuestSpeed.Caps( speed );
		var reach = GuestNavigation.ArrivalRadius + 2 * maxSpeed;
		const int radius = GuestNavigation.Radius;
		for ( var i = 0; i < grid; i++ )
			for ( var j = 0; j < grid; j++ )
			{
				var dx = FloorDiv( -reach * (grid - 1) + 2 * reach * i, grid - 1 );
				var dy = FloorDiv( -reach * (grid - 1) + 2 * reach * j, grid - 1 );
				var (x, y) = (origin.X + dx, origin.Y + dy);
				if ( (long)dx * dx + (long)dy * dy <= (long)reach * reach && x >= 0 && x < One && x >= radius && x <= One - radius
					&& y >= -One + radius && y <= 2 * One - radius )
					yield return (x, y);
			}
	}

	private static int FloorDiv( int a, int b ) => (int)Math.Floor( a / (double)b );

	/// <summary><c>walk_evidence.worst_walk</c> with separation off, fences on and nearest rounding.</summary>
	public static (int Worst, int Failures) WorstStripWalk( string kind, float speed, int grid, bool reversedAxis = false )
	{
		int worst = 0, failures = 0;
		foreach ( var lateral in new[] { 114, 127, 141 } )
		{
			(int X, int Y)[] origins;
			(int X, int Y) target, destination;
			if ( kind == "w" )
				(origins, target, destination) = (new[] { SlotPoint( 0, lateral, reversedAxis ) }, StandPoint(), Entrance);
			else
			{
				origins = new[] { 1, 2, 3 }.SelectMany( slot => Laterals.Select( side => SlotPoint( slot, side, reversedAxis ) ) ).ToArray();
				(target, destination) = (SlotPoint( 0, lateral, reversedAxis ), Front);
			}
			foreach ( var origin in origins )
				foreach ( var start in StandingStarts( origin, speed, grid ) )
				{
					var route = StripRoute( CellOf( start ), destination );
					foreach ( var velocity in StaleVelocities( speed ) )
					{
						var updates = Walk( start, velocity, target, route, speed, InStrip );
						if ( updates < 0 )
							failures++;
						else
							worst = Math.Max( worst, updates );
					}
				}
		}
		return (worst, failures);
	}

	// ---- queue routes (the gate's per-cell walk term) --------------------------------------------------------------

	private static readonly (int DX, int DY)[] Directions = GuestPathGrid.Directions;

	/// <summary>
	/// A queue route of <paramref name="crossings"/> cell crossings from (0, 0) heading +X: each entry of <paramref name="turns"/> turns the
	/// next step left (−1), straight (0) or right (+1). The destination cell's front direction turns by
	/// <paramref name="exitTurn"/>; slot points lie along it (depth from the front edge, or from the far edge with
	/// <paramref name="reversedAxis"/>) with the lateral byte across.
	/// </summary>
	public static (List<(int X, int Y)> Route, int ExitDirection) QueueRoute( int crossings, int[] turns, int exitTurn )
	{
		var route = new List<(int X, int Y)> { (0, 0) };
		var direction = 1;
		for ( var step = 0; step < crossings; step++ )
		{
			if ( step > 0 )
				direction = (direction + turns[step - 1] + 4) & 3;
			var (x, y) = route[^1];
			route.Add( (x + Directions[direction].DX, y + Directions[direction].DY) );
		}
		return (route, (direction + exitTurn + 4) & 3);
	}

	public static (int X, int Y) QueueSlot( (int X, int Y) cell, int frontDirection, int depth, int lateral, bool reversedAxis )
	{
		var (dx, dy) = Directions[frontDirection];
		if ( reversedAxis )
			depth = 255 - depth;
		int bx, by;
		if ( dx != 0 )
			(bx, by) = (dx > 0 ? 255 - depth : depth, dx > 0 ? lateral : 255 - lateral);
		else
			(bx, by) = (dy < 0 ? lateral : 255 - lateral, dy > 0 ? 255 - depth : depth);
		return GuestNavigation.BytePoint( cell, bx, by );
	}

	/// <summary>Start points on a grid over the start cell, a radius from its edges (avoid_walls keeps a guest there).</summary>
	public static IEnumerable<(int X, int Y)> CellStarts( int grid )
	{
		const int low = GuestNavigation.Radius, high = One - GuestNavigation.Radius;
		for ( var i = 0; i < grid; i++ )
			for ( var j = 0; j < grid; j++ )
				yield return (low + (high - low) * i / (grid - 1), low + (high - low) * j / (grid - 1));
	}

	/// <summary>Every turn sequence of <paramref name="crossings"/> cell crossings (the first step is fixed).</summary>
	public static IEnumerable<int[]> TurnSequences( int crossings )
	{
		if ( crossings == 0 )
		{
			yield return [];
			yield break;
		}
		var count = (int)Math.Pow( 3, crossings - 1 );
		for ( var code = 0; code < count; code++ )
		{
			var turns = new int[crossings - 1];
			for ( int index = 0, value = code; index < turns.Length; index++, value /= 3 )
				turns[index] = value % 3 - 1;
			// A route never enters a cell twice.
			var (route, _) = QueueRoute( crossings, turns, 0 );
			if ( route.Distinct().Count() == route.Count )
				yield return turns;
		}
	}

	public sealed record QueueWalkResult( int Crossings, int Worst, int Failures, long Walks, int Rerouted, int WorstWithoutReroute );

	/// <summary>Worst queue walk over route shapes with <paramref name="crossings"/> cell crossings.</summary>
	public static QueueWalkResult WorstQueueWalk( int crossings, IEnumerable<int[]> shapes, float[] speeds, int grid, int velocityStride, int[] depths, int[] laterals, bool[]? axes = null )
	{
		axes ??= [false, true];
		int worst = 0, failures = 0, rerouted = 0, worstPlain = 0;
		long walks = 0;
		foreach ( var turns in shapes )
			foreach ( var exitTurn in new[] { -1, 0, 1 } )
			{
				var (route, exit) = QueueRoute( crossings, turns, exitTurn );
				// The queue continues beyond both ends: the cell ahead of the destination and the one behind the start.
				// For a one-cell walk the queue arrives from the side opposite the front unless it turns there.
				var behind = crossings == 0 && exitTurn == 0 ? (-1, 0) : crossings == 0 ? (route[0].X - Directions[exit].DX, route[0].Y - Directions[exit].DY) : (route[0].X - 1, route[0].Y);
				var open = new HashSet<(int X, int Y)>( route )
				{
					(route[^1].X + Directions[exit].DX, route[^1].Y + Directions[exit].DY),
					behind
				};
				bool IsOpen( int x, int y ) => open.Contains( (x, y) );
				foreach ( var speed in speeds )
				{
					var velocities = StaleVelocities( speed ).Where( ( _, index ) => index % velocityStride == 0 ).ToArray();
					foreach ( var reversed in axes )
						foreach ( var depth in depths )
							foreach ( var lateral in laterals )
							{
								var target = QueueSlot( route[^1], exit, depth, lateral, reversed );
								foreach ( var (sx, sy) in CellStarts( grid ) )
									foreach ( var velocity in velocities )
										foreach ( var oppose in new[] { false, true } )
										{
											walks++;
											var updates = Walk( (sx, sy), velocity, target, route, speed, IsOpen, out var reroutes, oppose, followReroutes: true );
											if ( reroutes > 0 )
												rerouted++;
											else if ( updates >= 0 )
												worstPlain = Math.Max( worstPlain, updates );
											if ( updates < 0 )
												failures++;
											else
												worst = Math.Max( worst, updates );
										}
							}
				}
			}
		return new QueueWalkResult( crossings, worst, failures, walks, rerouted, worstPlain );
	}

	// ---- the same queue routes over tp.exe's walk (the route search, edge test, commit and PC arithmetic of GuestNavigation.Navigate) ------------------

	/// <summary>
	/// The cell map of a queue route (<see cref="QueueRoute"/> shifted by <paramref name="offset"/>): its cells are queue cells linked to the cell before and after them, each with its
	/// queue link toward the front; the cell ahead of the destination is a queue cell too (the next queue cell, or the front cell beside the ride entrance), the cell behind the
	/// start a path cell linked to the start, as the queue tool writes them (docs/reverse/WALK-plan.md §15, QueuePaths).
	/// </summary>
	internal static ParkCellMap QueueMap( List<(int X, int Y)> route, int exit, (int X, int Y) behind, (int X, int Y) offset )
	{
		var map = new ParkCellMap( 96, 96 );
		(int X, int Y) At( (int X, int Y) cell ) => (cell.X + offset.X, cell.Y + offset.Y);
		int Toward( (int X, int Y) from, (int X, int Y) to ) => GuestPathGrid.DirectionBetween( from.X, from.Y, to.X, to.Y );
		var ahead = At( (route[^1].X + Directions[exit].DX, route[^1].Y + Directions[exit].DY) );
		var chain = route.Select( At ).ToList();
		var all = new List<(int X, int Y)>( chain ) { ahead };
		foreach ( var (x, y) in all )
			map.SetType( x, y, ParkCellType.Queue );
		for ( var index = 0; index < all.Count; index++ )
		{
			var cell = all[index];
			byte links = 0;
			if ( index > 0 )
				links |= (byte)GuestPathGrid.LinkValue( Toward( cell, all[index - 1] ) );
			if ( index + 1 < all.Count )
			{
				links |= (byte)GuestPathGrid.LinkValue( Toward( cell, all[index + 1] ) );
				map.SetQueueLink( cell.X, cell.Y, (byte)GuestPathGrid.LinkValue( Toward( cell, all[index + 1] ) ) );
			}
			else
				map.SetQueueLink( cell.X, cell.Y, (byte)GuestPathGrid.LinkValue( exit ) );
			map.SetLinks( cell.X, cell.Y, links );
		}
		var back = At( behind );
		map.SetType( back.X, back.Y, ParkCellType.Path );
		var toStart = Toward( back, chain[0] );
		if ( toStart >= 0 )
		{
			map.SetLinks( back.X, back.Y, (byte)GuestPathGrid.LinkValue( toStart ) );
			map.SetLinks( chain[0].X, chain[0].Y, (byte)(map.LinksAt( chain[0].X, chain[0].Y ) | GuestPathGrid.LinkValue( (toStart + 2) & 3 )) );
		}
		return map;
	}

	/// <summary>
	/// Updates until tp.exe's walk (<see cref="GuestNavigation.Navigate"/> over <paramref name="map"/>, stepped by <see cref="GuestNavigation.Step(int, int, ParkCellMap, Func{int, int, bool}, Func{GuestNavigation, ValueTuple{int, int}}?)"/>)
	/// reports arrival, or −1 when its search fails, the step reports stuck or the limit is hit.
	/// </summary>
	public static int WalkOriginal( ParkCellMap map, (int X, int Y) position, (int X, int Y) velocity, (int X, int Y) target, float speed, out int reroutes, bool opposeSeparation = false, int limit = 5000, Func<GuestNavigation, (int X, int Y)>? separation = null )
	{
		var (maxSpeed, maxForce) = GuestSpeed.Caps( speed );
		var navigation = new GuestNavigation { X = position.X, Y = position.Y, VelocityX = velocity.X, VelocityY = velocity.Y };
		reroutes = 0;
		if ( !navigation.Navigate( map, target.X, target.Y ) )
			return -1;
		for ( var update = 1; update <= limit; update++ )
		{
			var result = navigation.Step( maxSpeed, maxForce, map, ( _, _ ) => true, separation ?? (opposeSeparation ? opposing : null) );
			reroutes = navigation.Reroutes;
			if ( result == WalkResult.Stuck )
				return -1;
			if ( result == WalkResult.Arrived )
				return update;
		}
		return -1;
	}

	/// <summary><see cref="WorstQueueWalk"/> over tp.exe's walk: the same shapes, speeds, starts, stale velocities and slots, on a typed queue map.</summary>
	public static QueueWalkResult WorstQueueWalkOriginal( int crossings, IEnumerable<int[]> shapes, float[] speeds, int grid, int velocityStride, int[] depths, int[] laterals, bool[]? axes = null )
	{
		axes ??= [false, true];
		int worst = 0, failures = 0, rerouted = 0, worstPlain = 0;
		long walks = 0;
		var offset = (X: 40, Y: 40);
		foreach ( var turns in shapes )
			foreach ( var exitTurn in new[] { -1, 0, 1 } )
			{
				var (route, exit) = QueueRoute( crossings, turns, exitTurn );
				var behind = crossings == 0 && exitTurn == 0 ? (-1, 0) : crossings == 0 ? (route[0].X - Directions[exit].DX, route[0].Y - Directions[exit].DY) : (route[0].X - 1, route[0].Y);
				// a queue never runs into itself: its next cell and the path behind it are no cell of the route
				var next = (route[^1].X + Directions[exit].DX, route[^1].Y + Directions[exit].DY);
				if ( route.Contains( next ) || route.Contains( behind ) || next == behind )
					continue;
				var map = QueueMap( route, exit, behind, offset );
				foreach ( var speed in speeds )
				{
					var velocities = StaleVelocities( speed ).Where( ( _, index ) => index % velocityStride == 0 ).ToArray();
					foreach ( var reversed in axes )
						foreach ( var depth in depths )
							foreach ( var lateral in laterals )
							{
								var local = QueueSlot( route[^1], exit, depth, lateral, reversed );
								var target = (local.X + (offset.X << 16), local.Y + (offset.Y << 16));
								foreach ( var (sx, sy) in CellStarts( grid ) )
									foreach ( var velocity in velocities )
										foreach ( var oppose in new[] { false, true } )
										{
											walks++;
											var start = (sx + (offset.X << 16), sy + (offset.Y << 16));
											var updates = WalkOriginal( map, start, velocity, target, speed, out var reroutes, oppose );
											if ( reroutes > 0 )
												rerouted++;
											else if ( updates >= 0 )
												worstPlain = Math.Max( worstPlain, updates );
											if ( updates < 0 )
												failures++;
											else
												worst = Math.Max( worst, updates );
										}
							}
				}
			}
		return new QueueWalkResult( crossings, worst, failures, walks, rerouted, worstPlain );
	}

	/// <summary>
	/// The strip of <see cref="WorstStripWalk"/> as typed cells for tp.exe's walk: the entrance cell (type 9) ahead of the front queue cell, the front cell, and the queue cell behind
	/// it, linked in a line with the queue links toward the entrance.
	/// </summary>
	private static ParkCellMap StripMap( (int X, int Y) offset )
	{
		var map = new ParkCellMap( 96, 96 );
		var cells = new[] { Entrance, Front, Behind }.Select( cell => (X: cell.X + offset.X, Y: cell.Y + offset.Y) ).ToArray();
		map.SetType( cells[0].X, cells[0].Y, 9 );
		map.SetType( cells[1].X, cells[1].Y, ParkCellType.Queue );
		map.SetType( cells[2].X, cells[2].Y, ParkCellType.Queue );
		var down = (byte)GuestPathGrid.LinkValue( 2 );
		var up = (byte)GuestPathGrid.LinkValue( 0 );
		map.SetLinks( cells[0].X, cells[0].Y, down );
		map.SetLinks( cells[1].X, cells[1].Y, (byte)(up | down ) );
		map.SetLinks( cells[2].X, cells[2].Y, up );
		map.SetQueueLink( cells[0].X, cells[0].Y, down );
		map.SetQueueLink( cells[1].X, cells[1].Y, up );
		map.SetQueueLink( cells[2].X, cells[2].Y, up );
		return map;
	}

	/// <summary>
	/// <see cref="WorstStripWalk"/>'s move-up ("w2": slots 1 to 3 to slot 0) over tp.exe's walk on a typed strip, starts within reach of each slot as there.
	/// </summary>
	public static (int Worst, int Failures, int Reroutes) WorstStripMoveUpOriginal( float speed, int grid, bool reversedAxis = false )
	{
		var offset = (X: 40, Y: 40);
		var map = StripMap( offset );
		int worst = 0, failures = 0, rerouted = 0;
		foreach ( var lateral in new[] { 114, 127, 141 } )
		{
			var target = SlotPoint( 0, lateral, reversedAxis );
			foreach ( var origin in new[] { 1, 2, 3 }.SelectMany( slot => Laterals.Select( side => SlotPoint( slot, side, reversedAxis ) ) ) )
				foreach ( var start in StandingStarts( origin, speed, grid ) )
					foreach ( var velocity in StaleVelocities( speed ) )
					{
						var updates = WalkOriginal( map, (start.X + (offset.X << 16), start.Y + (offset.Y << 16)), velocity, (target.X + (offset.X << 16), target.Y + (offset.Y << 16)), speed, out var reroutes );
						if ( updates < 0 )
							failures++;
						else
							worst = Math.Max( worst, updates );
						if ( reroutes > 0 )
							rerouted++;
					}
		}
		return (worst, failures, rerouted);
	}
}
