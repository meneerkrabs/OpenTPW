using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// ExitRide as tp.exe runs it (0x004E1410 → 0x005014E0): the ride update of a park turn reads VAR_LETMEOFF, puts the guest at UsageInfo.ExitCellAppearPos in the object's exit cell, finds the cell beyond
/// along the exit cell's link (reversed when the exit cell is the entrance cell) and walks the guest there in state 15 (0x00500900); a queue cell there or no route is a blocked exit, which closes the
/// ride (0x004DF150) and keeps the guest on it until a later ride update finds the way free. GPU- and data-free: a queued shop rig whose script takes a guest, waits 1000 ms and lets it off.
/// </summary>
[TestClass]
public class ExitRideTests
{
	private static readonly byte East = GuestPathGrid.LinkValue( GuestPathGrid.DirectionBetween( 0, 0, 1, 0 ) );

	private static QueueTests.Rig Rig()
	{
		var rig = new QueueTests.Rig( satisfies: GuestNeeds.Hunger, capacity: 1, takesGuests: true, kind: RideVisitorKind.Shop );
		rig.Ride.RideHandlesSprite = true;
		return rig;
	}

	/// <summary>Lays the object's own exit cell (3, 6) beside the entrance (2, 6), pointing east to (4, 6), and a path from (4, 6) up to the main path.</summary>
	private static void LayExit( QueueTests.Rig rig, bool queueBeyond = false )
	{
		var cells = rig.Grid.Cells;
		cells.SetType( 3, 6, ParkCellTypes.Entrance );
		cells.SetLinks( 3, 6, East );
		cells.SetQueueLink( 3, 6, East );
		for ( var y = 0; y <= 6; y++ )
			rig.Grid.SetPath( 4, y, true );
		if ( queueBeyond )
			cells.SetType( 4, 6, ParkCellType.Queue );
		rig.Ride.ExitAccessCell = (3, 6);
	}

	private static Guest Serve( QueueTests.Rig rig )
	{
		var guest = rig.Send();
		for ( var tick = 0; tick < 120 * 60 && guest.State != GuestState.Using; tick++ )
			rig.Step();
		Assert.AreEqual( GuestState.Using, guest.State, "the shop took the guest" );
		return guest;
	}

	[TestMethod]
	public void ALetOffGuestAppearsAtTheExitPointAndWalksToTheCellBeyond()
	{
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var rig = Rig();
			LayExit( rig );
			rig.Ride.ExitCellAppearPos = (0.5f, 0.9f);
			rig.Ride.Rotation = rotation;
			var guest = Serve( rig );
			var turnsUsing = 0;
			while ( guest.State == GuestState.Using && turnsUsing < 600 )
			{
				rig.Step();
				turnsUsing++;
			}
			Assert.AreEqual( GuestState.ExitingRide, guest.State, $"rotation {rotation}: let off" );
			var (bx, by) = GuestSimulation.StandBytes( (0.5f, 0.9f), rotation );
			// [BIN:TPW-EXE:0x004DEDF0 exit point] the exit cell with the bytes ftol(255 x f) rotated like the stand point; the walk has not stepped yet in the tick that let the guest off
			var point = GuestNavigation.BytePoint( (3, 6), bx, by );
			Assert.AreEqual( (3, 6), guest.Navigation.Cell, $"rotation {rotation}: in the exit cell" );
			Assert.IsTrue( System.Math.Abs( guest.Navigation.X - point.X ) < Fixed16.One / 4 && System.Math.Abs( guest.Navigation.Y - point.Y ) < Fixed16.One / 4,
				$"rotation {rotation}: at the exit point ({guest.Navigation.X - point.X}, {guest.Navigation.Y - point.Y})" );
			Assert.AreEqual( 0, guest.AttractionId, "the visit ended (GUEST-004)" );
			for ( var tick = 0; tick < 20 * 60 && guest.State == GuestState.ExitingRide; tick++ )
				rig.Step();
			Assert.AreEqual( (4, 6), (guest.CellX, guest.CellY), $"rotation {rotation}: it walked to the cell beyond the exit" );
			Assert.AreNotEqual( GuestState.ExitingRide, guest.State, "the arrival ends state 15 (0x00500900: state 6)" );
			Assert.AreEqual( 0, rig.Script[RideVariables.VAR_LETMEOFF] );
			Assert.AreEqual( 1, rig.Ride.ReleasedTotal );
		}
	}

	/// <summary>An exit cell that is the entrance cell has its link reversed (0x004D8C00): the entrance's link toward its queue points the guest away from it.</summary>
	[TestMethod]
	public void AnExitThatIsTheEntranceReversesItsLink()
	{
		var rig = Rig();
		rig.Ride.ExitAccessCell = GuestSimulation.StandCellOf( rig.Ride );
		Assert.AreEqual( (2, 6), rig.Ride.ExitAccessCell );
		Assert.AreEqual( GuestPathGrid.LinkValue( 0 ), rig.Grid.Cells.QueueLinkAt( 2, 6 ), "the entrance links toward its queue (2, 5)" );
		for ( var y = 7; y <= 9; y++ )
			rig.Grid.SetPath( 2, y, true );
		for ( var x = 2; x <= 9; x++ )
			rig.Grid.SetPath( x, 9, true );
		var guest = Serve( rig );
		for ( var tick = 0; tick < 30 * 60 && guest.State is GuestState.Using or GuestState.ExitingRide; tick++ )
			rig.Step();
		Assert.AreEqual( (2, 7), (guest.CellX, guest.CellY), "the reversed link leads to (2, 7), not back into the queue" );
		Assert.AreEqual( 0, rig.Ride.BlockedExitClosings );
	}

	/// <summary>A queue cell beyond the exit blocks it (0x00536320): the ride closes, the guest stays on it at the exit point with VAR_LETMEOFF set, and leaves once the cell is a path again.</summary>
	[TestMethod]
	public void ABlockedExitClosesTheRideAndKeepsTheGuestUntilTheWayIsFree()
	{
		var rig = Rig();
		LayExit( rig, queueBeyond: true );
		var calls = new List<int>();
		rig.Ride.ModelState = calls.Add;
		var guest = Serve( rig );
		var kept = rig.Simulation.KeptBodyPosition( guest );
		for ( var tick = 0; tick < 5 * 60; tick++ )
			rig.Step();
		Assert.AreEqual( GuestState.Using, guest.State, "the guest stays on the ride" );
		Assert.AreEqual( guest.Id, rig.Script[RideVariables.VAR_LETMEOFF], "VAR_LETMEOFF keeps the guest" );
		Assert.AreEqual( 1, rig.Script[RideVariables.VAR_RIDECLOSED], "the ride closed (0x004DF150)" );
		Assert.AreEqual( 1, rig.Ride.BlockedExitClosings, "an already closed ride does not close again" );
		CollectionAssert.AreEqual( new[] { ObjectHoardingState.CloseMode }, calls, "the close makes the model call 0x00454550(1) once" );
		Assert.AreEqual( (3, 6), guest.Navigation.Cell, "ExitRide put the guest at the exit point" );
		Assert.AreEqual( kept, rig.Simulation.KeptBodyPosition( guest ), "the kept body stays where it was (0x004FA087)" );
		Assert.IsTrue( rig.Simulation.ShowsKeptBody( guest ) );
		rig.Grid.SetPath( 4, 6, true );
		for ( var tick = 0; tick < 5 * 60 && guest.State == GuestState.Using; tick++ )
			rig.Step();
		Assert.AreEqual( GuestState.ExitingRide, guest.State, "a later ride update finds the way free" );
		Assert.AreEqual( 0, rig.Script[RideVariables.VAR_LETMEOFF] );
	}

	/// <summary>No route to the cell beyond is a blocked exit too (0x004FA530 fails).</summary>
	[TestMethod]
	public void NoRouteToTheCellBeyondIsABlockedExit()
	{
		var rig = Rig();
		LayExit( rig );
		for ( var y = 0; y <= 5; y++ )
			rig.Grid.SetPath( 4, y, false );
		rig.Grid.SetPath( 4, 6, true, 0 );
		rig.Grid.Cells.SetLinks( 3, 6, 0 );
		var guest = Serve( rig );
		for ( var tick = 0; tick < 5 * 60; tick++ )
			rig.Step();
		Assert.AreEqual( GuestState.Using, guest.State );
		Assert.AreEqual( 1, rig.Ride.BlockedExitClosings );
	}

	/// <summary>A use lasts the script's time plus up to one park turn: the let-off is read by the ride update of a turn (0x004E1410), not on every tick.</summary>
	[TestMethod]
	public void TheLetOffIsReadOncePerParkTurn()
	{
		var rig = Rig();
		LayExit( rig );
		var guest = Serve( rig );
		var released = false;
		for ( var tick = 0; tick < 5 * 60 && guest.State == GuestState.Using; tick++ )
		{
			var turn = rig.Simulation.ParkTurn;
			var before = rig.Script[RideVariables.VAR_LETMEOFF];
			rig.Step();
			if ( guest.State != GuestState.Using )
			{
				released = true;
				Assert.AreEqual( guest.Id, before, "the script had written VAR_LETMEOFF before the update that read it" );
				Assert.AreEqual( turn + 1, rig.Simulation.ParkTurn, "released in the tick a new park turn starts" );
			}
		}
		Assert.IsTrue( released );
	}
}
