using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

[TestClass]
public class MapCellAggregationReviewTests
{
	private static (int X, int Y, int Pleased, int Dissatisfied)[] Legacy( IEnumerable<Guest> guests, int width, int height ) =>
		guests.Where( guest => guest.IsVisible && guest.Thought is GuestThought.Pleased or GuestThought.Dissatisfied
			&& float.IsFinite( guest.X ) && float.IsFinite( guest.Y ) && guest.X >= 0 && guest.Y >= 0 && guest.X < width && guest.Y < height )
			.GroupBy( guest => ((int)MathF.Floor( guest.X ), (int)MathF.Floor( guest.Y )) )
			.Select( group => (group.Key.Item1, group.Key.Item2, group.Count( guest => guest.Thought == GuestThought.Pleased ), group.Count( guest => guest.Thought == GuestThought.Dissatisfied )) ).ToArray();

	[TestMethod]
	public void ScratchMatchesLegacyAcrossEveryStateThoughtAndFixedPointEdge()
	{
		var scratch = new ParkMapMetrics.OutcomeScratch();
		var guest = new Guest( 1, 0 );
		var guests = new[] { guest };
		var coordinates = new[] { int.MinValue, -Fixed16.One, 0, Fixed16.One / 2, 4 * Fixed16.One - 256, 4 * Fixed16.One, int.MaxValue };
		foreach ( var (width, height) in new[] { (4, 4), (0, 4), (4, -1), (int.MaxValue, int.MaxValue) } )
			foreach ( var state in Enum.GetValues<GuestState>() )
				foreach ( var thought in Enum.GetValues<GuestThought>() )
					foreach ( var x in coordinates )
						foreach ( var y in coordinates )
						{
							guest.State = state; guest.Thought = thought;
							guest.Navigation.X = x; guest.Navigation.Y = y;
							CollectionAssert.AreEqual( Legacy( guests, width, height ), scratch.Rebuild( guests, width, height, true ).ToArray(),
								$"{width}/{height}/{state}/{thought}/{x}/{y}" );
						}
	}

	private static Guest Observation( int id, int x, int y, GuestThought thought ) => new( id, 0 )
	{
		State = GuestState.WalkingAround,
		Thought = thought,
		Navigation = { X = x * Fixed16.One, Y = y * Fixed16.One }
	};

	[TestMethod]
	public void RebuildDropsOldSourceAndOrdersOnlyFirstObservedEncounters()
	{
		var scratch = new ParkMapMetrics.OutcomeScratch();
		var first = new[] {
			Observation( 1, 5, 5, GuestThought.Happy ),
			Observation( 2, 1, 1, GuestThought.Pleased ),
			Observation( 3, 5, 5, GuestThought.Dissatisfied ),
			Observation( 4, 1, 1, GuestThought.Pleased )
		};
		var borrowed = scratch.Rebuild( first, 32, 32, true );
		CollectionAssert.AreEqual( new[] { (1, 1, 2, 0), (5, 5, 0, 1) }, borrowed.ToArray() );
		Assert.AreEqual( 0, scratch.Rebuild( first, 32, 32, false ).Count );
		Assert.AreEqual( 0, borrowed.Count, "The reusable result expires at the next rebuild, including a disabled frame." );
		var replacement = new[] {
			Observation( 5, 31, 31, GuestThought.Dissatisfied ),
			Observation( 6, 0, 0, GuestThought.Pleased ),
			Observation( 7, 31, 31, GuestThought.Pleased ),
			Observation( 8, 0, 0, GuestThought.Happy )
		};
		CollectionAssert.AreEqual( new[] { (31, 31, 1, 1), (0, 0, 1, 0) }, scratch.Rebuild( replacement, 32, 32, true ).ToArray() );
		Array.Reverse( replacement );
		CollectionAssert.AreEqual( Legacy( replacement, 32, 32 ), scratch.Rebuild( replacement, 32, 32, true ).ToArray() );
		CollectionAssert.AreEqual( new[] { (0, 0, 1, 0) }, scratch.Rebuild( replacement, 1, 1, true ).ToArray() );
		Assert.AreEqual( 0, scratch.Rebuild( Array.Empty<Guest>(), 32, 32, true ).Count );
		CollectionAssert.AreEqual( Legacy( first, 32, 32 ), scratch.Rebuild( first, 32, 32, true ).ToArray() );
	}
}
