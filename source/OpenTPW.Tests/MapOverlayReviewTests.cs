using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Tests;

[TestClass]
public class MapOverlayReviewTests
{
	private static (int Pleased, int Dissatisfied) LegacyTotals( IEnumerable<Guest> guests, int width, int height )
	{
		// Keep the original predicate and grouping independent of the new shared helper.
		var cells = guests.Where( guest => guest.IsVisible && guest.Thought is GuestThought.Pleased or GuestThought.Dissatisfied
			&& float.IsFinite( guest.X ) && float.IsFinite( guest.Y ) && guest.X >= 0 && guest.Y >= 0 && guest.X < width && guest.Y < height )
			.GroupBy( guest => ((int)MathF.Floor( guest.X ), (int)MathF.Floor( guest.Y )) )
			.Select( group => (Pleased: group.Count( guest => guest.Thought == GuestThought.Pleased ), Dissatisfied: group.Count( guest => guest.Thought == GuestThought.Dissatisfied )) ).ToArray();
		return (cells.Sum( cell => cell.Pleased ), cells.Sum( cell => cell.Dissatisfied ));
	}

	[TestMethod]
	public void FreshTotalsMatchLegacyEligibilityAcrossStatesThoughtsAndGridEdges()
	{
		var guest = new Guest( 1, 0 );
		var guests = new[] { guest };
		var coordinates = new[] { int.MinValue, -Fixed16.One, 0, Fixed16.One / 2, 4 * Fixed16.One - 256, 4 * Fixed16.One, int.MaxValue };
		foreach ( var state in Enum.GetValues<GuestState>() )
			foreach ( var thought in Enum.GetValues<GuestThought>() )
				foreach ( var x in coordinates )
					foreach ( var y in coordinates )
					{
						guest.State = state; guest.Thought = thought; guest.Navigation.X = x; guest.Navigation.Y = y;
						var expected = LegacyTotals( guests, 4, 4 );
						Assert.AreEqual( expected, ParkMapMetrics.OutcomeTotals( guests, 4, 4 ), $"{state}/{thought}/{x}/{y}" );
					}
		guest.State = GuestState.WalkingAround; guest.Thought = GuestThought.Pleased;
		guest.Navigation.X = 3 * Fixed16.One; guest.Navigation.Y = Fixed16.One;
		Assert.AreEqual( (0, 0), ParkMapMetrics.OutcomeTotals( guests, 3, 4 ) );
		Assert.AreEqual( (1, 0), ParkMapMetrics.OutcomeTotals( guests, 4, 4 ), "same guest count with changed bounds must refresh" );
		Assert.AreEqual( (0, 0), ParkMapMetrics.OutcomeTotals( guests, 0, 4 ) );
		Assert.AreEqual( (0, 0), ParkMapMetrics.OutcomeTotals( guests, 4, -1 ) );
	}

	private static int ReactionVertices( MapOverlayAllocationTests.Rig rig ) => rig.Context.Batch.Draws
		.SelectMany( draw => draw.Vertices ).Count( vertex => vertex.Color == new RgbaByte( 35, 235, 35, 240 )
			|| vertex.Color == new RgbaByte( 235, 35, 35, 240 ) || vertex.Color == new RgbaByte( 255, 165, 35, 240 ) );
	private static void Matches( MapOverlayAllocationTests.Rig rig, int pleased, int dissatisfied, int drawnCells )
	{
		Assert.AreEqual( (pleased, dissatisfied), ParkMapMetrics.OutcomeTotals( rig.Guests.Guests, 32, 32 ) );
		var expected = rig.Strings.Extra( OpenTpwText.MapRecordedVisitorReactions ) + "\n" + (pleased + dissatisfied == 0
			? rig.Strings.Extra( OpenTpwText.MapNoObservations ) : string.Format( rig.Strings.Extra( OpenTpwText.MapReactionLegend ), pleased, dissatisfied ));
		Assert.AreEqual( expected, rig.Legend() );
		rig.Draw();
		Assert.AreEqual( drawnCells * 6, ReactionVertices( rig ), "the live overlay and legend must both refresh" );
	}

	[TestMethod]
	public void DrawAndLegendRefreshForHiddenRemovedReplacementAndSameCountMutations()
	{
		var rig = new MapOverlayAllocationTests.Rig( 2 );
		var first = rig.Guests.Guests[0]; var second = rig.Guests.Guests[1];
		Matches( rig, 1, 1, 2 );
		second.Navigation.X = first.Navigation.X; second.Navigation.Y = first.Navigation.Y;
		Matches( rig, 1, 1, 1 ); // One mixed cell, unchanged legend totals.
		first.State = GuestState.Using; Matches( rig, 0, 1, 1 );
		first.State = GuestState.WalkingAround; first.Thought = GuestThought.Dissatisfied; Matches( rig, 0, 2, 1 );
		first.Navigation.Y = 32 * Fixed16.One; Matches( rig, 0, 1, 1 );
		first.Navigation.Y = second.Navigation.Y; Matches( rig, 0, 2, 1 );
		first.State = GuestState.Gone;
		rig.Guests.TickTurn( 0 ); // Exercise actual list removal, not only a hidden state (the turn that frees the slot: destroys wait for it).
		Assert.AreEqual( 1, rig.Guests.Guests.Count );
		second.Thought = GuestThought.Dissatisfied;
		Matches( rig, 0, 1, 1 );
		var replacement = rig.Guests.SpawnInPark( 5, 5 ); replacement.Thought = GuestThought.Pleased;
		Assert.AreNotSame( first, replacement );
		Assert.AreEqual( first.Id, replacement.Id, "the pool gives the slot the leaving guest freed to the next guest" );
		Matches( rig, 1, 1, 2 );
		replacement.Thought = GuestThought.Happy; Matches( rig, 0, 1, 1 );
		replacement.Thought = GuestThought.Pleased; Matches( rig, 1, 1, 2 );
	}

	[TestMethod]
	public void LayerTogglesDoNotCacheOrCoupleLiveReactionTotals()
	{
		var rig = new MapOverlayAllocationTests.Rig( 1 );
		typeof( ParkHud ).GetField( "mapStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( rig.Hud, new HashSet<StaffType>() );
		var guest = rig.Guests.Guests.Single();
		Matches( rig, 1, 0, 1 );
		rig.Hud.ToggleMapVisitors(); rig.Hud.ToggleMapHappiness(); rig.Hud.ToggleMapStaff( StaffType.Guard );
		Assert.IsFalse( rig.Hud.MapVisitors ); Assert.IsFalse( rig.Hud.MapHappiness ); Assert.IsTrue( rig.Hud.MapStaffRole( StaffType.Guard ) );
		Matches( rig, 1, 0, 1 );
		rig.Hud.ToggleMapSatisfaction(); rig.Draw();
		Assert.AreEqual( "", rig.Legend() ); Assert.AreEqual( 0, ReactionVertices( rig ) );
		guest.Thought = GuestThought.Dissatisfied;
		rig.Hud.ToggleMapExcitement();
		Assert.IsTrue( rig.Hud.MapExcitement ); Assert.IsFalse( rig.Hud.MapSatisfaction );
		rig.Hud.ToggleMapExcitement(); rig.Hud.ToggleMapSatisfaction();
		Matches( rig, 0, 1, 1 );
	}
}
