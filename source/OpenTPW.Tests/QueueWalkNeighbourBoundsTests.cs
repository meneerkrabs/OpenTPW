using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Bounded adversarial witnesses for GATE-006, not a proof over all crowd histories or native staff chains.
/// Unlike the older off/opposing-only grid, these feed side, diagonal and ordered multi-neighbour sums through
/// the production separation query. Synthetic neighbours follow the walker at fixed offsets, as the existing
/// opposing witness does; they do not claim to reproduce a complete crowd simulation.
/// </summary>
[TestClass]
public class QueueWalkNeighbourBoundsTests
{
	private static readonly (int X, int Y)[][] Patterns =
	[
		[(0, 25)], [(0, -25)], [(25, 25)], [(-25, 25)],
		[(25, 0), (-25, 0)],
		[(25, 0), (25, 5), (25, -5), (25, 10), (25, -10), (-25, 0)],
		[(-25, 0), (25, -10), (25, 10), (25, -5), (25, 5), (25, 0)],
		[(0, 0), (102, 0), (25, 0), (0, 25), (-25, 0), (0, -25)]
	];

	private static Func<GuestNavigation, (int X, int Y)> Neighbours( (int X, int Y)[] offsets )
	{
		var neighbours = offsets.Select( _ => new GuestNavigation() ).ToArray();
		return self =>
		{
			for ( var i = 0; i < offsets.Length; i++ )
			{
				neighbours[i].X = self.X + (offsets[i].X << 8);
				neighbours[i].Y = self.Y + (offsets[i].Y << 8);
			}
			return GuestNavigation.Separation( self, neighbours );
		};
	}

	[TestMethod]
	public void WitnessesExerciseSidewaysCancellationAndFiveCallbackOrder()
	{
		var self = new GuestNavigation { X = 40 << 16, Y = 40 << 16 };
		Assert.AreEqual( (0, -Fixed16.One), Neighbours( Patterns[0] )( self ) );
		Assert.AreEqual( (0, Fixed16.One), Neighbours( Patterns[1] )( self ) );
		Assert.AreEqual( (0, 0), Neighbours( Patterns[4] )( self ) );
		Assert.AreNotEqual( Neighbours( Patterns[5] )( self ), Neighbours( Patterns[6] )( self ), "The sixth eligible neighbour must not erase first-five order." );
		Assert.AreEqual( (0, 0), Neighbours( Patterns[7] )( self ), "Coincident and exact-radius neighbours are rejected before the callback budget." );
	}

	[TestMethod]
	public void ReducedQueueWalkBoundIncludesNonOpposingAndOrderedNeighbours()
	{
		var offset = (X: 40, Y: 40);
		(int Crossings, int[] Turns)[] cases = [ (1, Array.Empty<int>()), (3, new[] { 0, 0 }), (3, new[] { -1, 0 }), (3, new[] { 1, 0 }) ];
		foreach ( var (crossings, turns) in cases )
		{
			var (route, exit) = WalkEnumeration.QueueRoute( crossings, turns, 0 );
			var map = WalkEnumeration.QueueMap( route, exit, (-1, 0), offset );
			foreach ( var speed in new[] { 0.59f, 1.0f, 2.0f } )
			foreach ( var reversed in new[] { false, true } )
			foreach ( var depth in new[] { 0, 191 } )
			foreach ( var startByte in new[] { 64, 191 } )
			foreach ( var velocity in WalkEnumeration.StaleVelocities( speed ).Where( ( _, index ) => index is 0 or 1 or 5 ) )
			for ( var pattern = 0; pattern < Patterns.Length; pattern++ )
			{
				var slot = WalkEnumeration.QueueSlot( route[^1], exit, depth, 114, reversed );
				var target = (slot.X + (offset.X << 16), slot.Y + (offset.Y << 16));
				var start = ((offset.X << 16) + (startByte << 8), (offset.Y << 16) + (startByte << 8));
				var bound = (int)M3GateRun.QueueWalkTurns( crossings );
				var updates = WalkEnumeration.WalkOriginal( map, start, velocity, target, speed, out var reroutes,
					limit: bound + 1, separation: Neighbours( Patterns[pattern] ) );
				Assert.IsTrue( updates >= 0 && updates <= bound,
					$"crossings={crossings}, turns={string.Join( ',', turns )}, speed={speed}, reversed={reversed}, depth={depth}, start={startByte}, velocity={velocity}, pattern={pattern}: updates={updates}, reroutes={reroutes}, bound={bound}" );
			}
		}
	}
}
