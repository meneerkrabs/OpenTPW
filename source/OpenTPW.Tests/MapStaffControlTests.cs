using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

[TestClass]
public class MapStaffControlTests
{
	private static Guest At( int id, float x, float y, float happiness = 50, GuestState state = GuestState.WalkingAround )
	{
		var guest = new Guest( id, 0 ) { State = state, Happiness = happiness };
		guest.Navigation.X = (int)(x * Fixed16.One);
		guest.Navigation.Y = (int)(y * Fixed16.One);
		return guest;
	}

	[TestMethod]
	public void LocatorCyclesVisibleActualVisitorsAndHandlesRemoval()
	{
		var guests = new[] { At( 9, 2, 3 ), At( 2, 4, 5 ), At( 5, 1, 1, state: GuestState.Using ), At( 12, 1, 1, state: GuestState.Gone ) };
		Assert.AreEqual( 2, ParkVisitorLocator.Next( guests, 0 )!.Id );
		Assert.AreEqual( 9, ParkVisitorLocator.Next( guests, 2 )!.Id );
		Assert.AreEqual( 2, ParkVisitorLocator.Next( guests, 9 )!.Id );
		Assert.AreEqual( 9, ParkVisitorLocator.Next( guests.Where( guest => guest.Id != 2 ), 2 )!.Id );
		Assert.IsNull( ParkVisitorLocator.Next( System.Array.Empty<Guest>(), 0 ) );
	}

	[TestMethod]
	public void HappinessLayerAveragesOnlyVisibleVisitorsInsideActualGrid()
	{
		var cells = ParkMapGuestLayers.Cells( new[] { At( 1, 2.1f, 3.2f, 20 ), At( 2, 2.8f, 3.9f, 80 ),
			At( 3, 2, 3, 0, GuestState.Using ), At( 4, -1, 3 ), At( 5, 10, 3 ) }, 10, 10 ).ToArray();
		Assert.AreEqual( 1, cells.Length );
		Assert.AreEqual( 2, cells[0].X );
		Assert.AreEqual( 3, cells[0].Y );
		Assert.AreEqual( 50f, cells[0].Happiness );
	}
	[TestMethod]
	public void StaffLocatorPreservesSelectionAndUsesActualRolePositions()
	{
		var actors = new[] { new StaffWorldSnapshot( 7, StaffType.Mechanic, 12.25f, 8.75f, true, true ),
			new StaffWorldSnapshot( 2, StaffType.Guard, 3.5f, 5.5f, false, false ),
			new StaffWorldSnapshot( 9, StaffType.Mechanic, 4.5f, 6.5f, false, false ) };
		var selected = ParkStaffLocator.Next( actors, StaffType.Mechanic, 7, 9 )!.Value;
		Assert.AreEqual( 7, selected.Id );
		Assert.AreEqual( 12.25f, selected.CellX );
		Assert.AreEqual( 8.75f, selected.CellY );
		Assert.AreEqual( 9, ParkStaffLocator.Next( actors, StaffType.Mechanic, 2, 7 )!.Value.Id );
		Assert.AreEqual( 9, ParkStaffLocator.Next( actors.Where( actor => actor.Id != 7 ), StaffType.Mechanic, 7, 7 )!.Value.Id );
		Assert.IsNull( ParkStaffLocator.Next( actors, StaffType.Researcher, 7, 0 ) );
		Assert.IsNull( ParkStaffLocator.Next( System.Array.Empty<StaffWorldSnapshot>(), StaffType.Mechanic, 7, 0 ) );
	}
	[TestMethod]
	public void SatisfactionUnobservedThoughtsStayUnmeasuredAndRealOutcomesUpdate()
	{
		var guest = At( 1, 2.5f, 3.5f );
		guest.Thought = GuestThought.Happy;
		Assert.AreEqual( 0, ParkMapMetrics.Outcomes( new[] { guest }, 10, 10 ).Count() );
		Assert.AreEqual( 0, ParkMapMetrics.Outcomes( System.Array.Empty<Guest>(), 10, 10 ).Count() );
		guest.Thought = GuestThought.Pleased;
		Assert.AreEqual( 1, ParkMapMetrics.Outcomes( new[] { guest }, 10, 10 ).Single().Pleased );
		guest.Thought = GuestThought.Dissatisfied;
		var outcome = ParkMapMetrics.Outcomes( new[] { guest }, 10, 10 ).Single();
		Assert.AreEqual( 0, outcome.Pleased );
		Assert.AreEqual( 1, outcome.Dissatisfied );
	}

	[TestMethod]
	public void AttractionExcitementRatingChangesOverlayColor()
	{
		Assert.AreNotEqual( ParkMapMetrics.ExcitementColor( 20 ), ParkMapMetrics.ExcitementColor( 80 ) );
		Assert.AreEqual( ParkMapMetrics.ExcitementColor( 0 ), ParkMapMetrics.ExcitementColor( -5 ) );
		Assert.AreEqual( ParkMapMetrics.ExcitementColor( 100 ), ParkMapMetrics.ExcitementColor( 105 ) );
	}
}
