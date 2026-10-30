using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] The staff state of every seat of a shared park (C4c-1), at the commands: each seat's pending hire reserves its candidate, a guest's hire or pick-up replaces
/// what its seat held in one record, the host's seat keeps today's transitions, an employee in another seat's hand is not dismissed, the drop back to where an employee was picked up is
/// every seat's cleanup, and a seat's release and its staff right taken back free what it held.
/// </summary>
[TestClass]
public class StaffSeatsTests
{
	private sealed class Fixture
	{
		public readonly ParkEconomy Economy = EconomyTestData.Park();
		public readonly Level Level;
		public readonly StaffHands Hands;

		public Fixture()
		{
			Level = CommandTestLevel.Create( Economy );
			Hands = new StaffHands( () => Economy, () => Level.Guests!.Grid.Cells, ( _, _ ) => false );
			typeof( Level ).GetField( "<StaffHands>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( Level, Hands );
		}

		/// <summary>Issues <paramref name="command"/> as seat <paramref name="seat"/>'s, as a record of that seat applies on every peer.</summary>
		public ParkCommandResult As( int seat, ParkCommand command )
		{
			var previous = Level.Commands.Player;
			Level.Commands.Player = seat;
			try
			{
				return Level.Commands.Issue( command );
			}
			finally
			{
				Level.Commands.Player = previous;
			}
		}

		/// <summary>The <paramref name="index"/>th candidate of the pool the park may hire.</summary>
		public StaffCandidate Candidate( int index = 0 ) => Economy.Staff.Candidates.Where( item => Economy.Staff.CanHire( item.Type ) ).ElementAt( index );

		/// <summary>An employee of <paramref name="type"/> standing idle on the grass cell (<paramref name="x"/>, <paramref name="y"/>), where it may be picked up.</summary>
		public StaffMember Standing( StaffType type, int x, int y )
		{
			var member = EconomyTestData.HireBest( Economy, type );
			StaffPlacement.PutDown( Economy.Staff, member, x, y );
			return member;
		}

		public (int Seat, int Candidate)[] PendingRows => Economy.Staff.PendingHires.Select( pair => (pair.Key, pair.Value) ).ToArray();
	}

	[TestMethod]
	public void EachSeatsPendingHireReservesItsCandidateAndAnotherSeatsBeginOrPlacementIsRefusedWithoutAChange()
	{
		var f = new Fixture();
		var (first, second) = (f.Candidate( 0 ), f.Candidate( 1 ));
		Assert.IsTrue( f.As( 1, new BeginStaffHireCommand( first.Id ) ).Accepted );
		var refused = f.As( 2, new BeginStaffHireCommand( first.Id ) );
		Assert.IsFalse( refused.Accepted );
		StringAssert.Contains( refused.Reason, "being placed by player 1" );
		Assert.IsFalse( f.As( 0, new BeginStaffHireCommand( first.Id ) ).Accepted, "the host's seat too" );
		CollectionAssert.AreEqual( new[] { (1, first.Id) }, f.PendingRows, "the refused seats hold nothing" );
		Assert.IsTrue( f.As( 2, new BeginStaffHireCommand( second.Id ) ).Accepted );
		Assert.IsFalse( f.As( 2, new PlaceStaffCommand( first.Id, 3, 3 ) ).Accepted, "another seat's candidate is not placed" );
		var hired = f.As( 1, new PlaceStaffCommand( first.Id, 3, 3 ) );
		Assert.IsTrue( hired.Accepted );
		Assert.AreEqual( first.Id, ((StaffMember)hired.Value!).Id );
		CollectionAssert.AreEqual( new[] { (2, second.Id) }, f.PendingRows );
		Assert.IsTrue( f.As( 2, new CancelStaffHireCommand() ).Accepted );
		Assert.AreEqual( 0, f.PendingRows.Length );
		Assert.AreEqual( 0, f.Economy.Staff.PendingPlacementId, "seat 0's is the single player's" );
	}

	[TestMethod]
	public void AGuestsHireOrPickUpReplacesWhatItsSeatHeldInOneRecordInEitherOrder()
	{
		var f = new Fixture();
		var member = f.Standing( StaffType.Handyman, 3, 3 );
		var other = f.Standing( StaffType.Mechanic, 14, 2 );
		var candidate = f.Candidate();
		// a pick-up, then a hire: the hand drops back where it was picked up, and the hire is the seat's one state
		Assert.IsTrue( f.As( 1, new PickUpStaffCommand( member.Id ) ).Accepted );
		Assert.IsTrue( f.As( 1, new DropStaffCommand( member.Id, 4, 3 ) ).Accepted );
		Assert.IsTrue( f.As( 1, new PickUpStaffCommand( member.Id ) ).Accepted );
		Assert.IsTrue( f.As( 1, new BeginStaffHireCommand( candidate.Id ) ).Accepted );
		Assert.IsNull( f.Hands.Of( 1 ), "the hire emptied the seat's hand" );
		Assert.AreEqual( (StaffState.Idle, 4.5f, 3.5f), (member.State, member.CellX!.Value, member.CellY!.Value), "dropped back on the cell it was picked up from" );
		CollectionAssert.AreEqual( new[] { (1, candidate.Id) }, f.PendingRows );
		// a hire, then a pick-up: the placement ends, and the hand is the seat's one state
		Assert.IsTrue( f.As( 1, new PickUpStaffCommand( other.Id ) ).Accepted );
		Assert.AreEqual( 0, f.PendingRows.Length, "the pick-up ended the seat's placement" );
		Assert.AreEqual( other.Id, f.Hands.Of( 1 )!.Value.Staff );
		Assert.IsTrue( f.Economy.Staff.Candidates.Any( item => item.Id == candidate.Id ), "the candidate stays in the pool" );
		// a pick-up over a hand drops the first back
		Assert.IsTrue( f.As( 1, new PickUpStaffCommand( member.Id ) ).Accepted );
		Assert.AreEqual( (member.Id, StaffState.Idle), (f.Hands.Of( 1 )!.Value.Staff, other.State) );
		Assert.AreEqual( (14.5f, 2.5f), (other.CellX!.Value, other.CellY!.Value) );
		// a refused record changes nothing: another seat's candidate
		Assert.IsTrue( f.As( 2, new BeginStaffHireCommand( candidate.Id ) ).Accepted );
		Assert.IsFalse( f.As( 1, new BeginStaffHireCommand( candidate.Id ) ).Accepted );
		Assert.AreEqual( member.Id, f.Hands.Of( 1 )!.Value.Staff, "the refused hire left the hand" );
	}

	[TestMethod]
	public void TheHostsSeatKeepsTodaysTransitions()
	{
		var f = new Fixture();
		var member = f.Standing( StaffType.Handyman, 3, 3 );
		var other = f.Standing( StaffType.Guard, 6, 3 );
		var (first, second) = (f.Candidate( 0 ), f.Candidate( 1 ));
		Assert.IsTrue( f.As( 0, new PickUpStaffCommand( member.Id ) ).Accepted );
		Assert.IsTrue( f.As( 0, new BeginStaffHireCommand( first.Id ) ).Accepted );
		Assert.AreEqual( (member.Id, first.Id), (f.Hands.Of( 0 )!.Value.Staff, f.Economy.Staff.PendingPlacementId), "the host's record touches no hand: its HUD ends the tool first" );
		var holding = f.As( 0, new PickUpStaffCommand( other.Id ) );
		Assert.IsFalse( holding.Accepted );
		StringAssert.Contains( holding.Reason, "already holds" );
		Assert.IsTrue( f.As( 0, new BeginStaffHireCommand( second.Id ) ).Accepted );
		Assert.AreEqual( second.Id, f.Economy.Staff.PendingPlacementId, "a hire replaces the host's own pending hire" );
	}

	[TestMethod]
	public void ThePoolKeepsEverySeatsReservationAndTheStaleCandidateGoesOnlyWhenNoSeatPlacesIt()
	{
		var f = new Fixture();
		var staff = f.Economy.Staff;
		var (kept, stale, placed) = (f.Candidate( 0 ), f.Candidate( 1 ), f.Candidate( 2 ));
		Assert.IsTrue( f.As( 1, new BeginStaffHireCommand( kept.Id ) ).Accepted );
		// every candidate's time is up: the pool keeps the reserved one
		staff.UpdatePool( new WorldRng( 1 ), 1L << 40 );
		Assert.IsTrue( staff.Candidates.Any( item => item.Id == kept.Id ), "a seat's reservation does not expire" );

		// an imported save's on-pointer candidate that another seat reserves stays when a third candidate is placed
		var g = new Fixture();
		(kept, stale, placed) = (g.Candidate( 0 ), g.Candidate( 1 ), g.Candidate( 2 ));
		g.Economy.Staff.StaleOnPointerId = stale.Id;
		Assert.IsTrue( g.As( 2, new BeginStaffHireCommand( stale.Id ) ).Accepted );
		Assert.IsTrue( g.As( 1, new BeginStaffHireCommand( placed.Id ) ).Accepted );
		Assert.IsTrue( g.As( 1, new PlaceStaffCommand( placed.Id, 3, 3 ) ).Accepted );
		Assert.AreEqual( 0, g.Economy.Staff.StaleOnPointerId, "the flag goes at the tool's exit" );
		Assert.IsTrue( g.Economy.Staff.Candidates.Any( item => item.Id == stale.Id ), "seat 2 places it: its own end frees it" );
		CollectionAssert.AreEqual( new[] { (2, stale.Id) }, g.PendingRows );

		// seat 0 alone, as in single player: the placement removes the flagged candidate
		var h = new Fixture();
		(kept, stale, placed) = (h.Candidate( 0 ), h.Candidate( 1 ), h.Candidate( 2 ));
		h.Economy.Staff.StaleOnPointerId = stale.Id;
		Assert.IsTrue( h.As( 0, new BeginStaffHireCommand( placed.Id ) ).Accepted );
		Assert.IsTrue( h.As( 0, new PlaceStaffCommand( placed.Id, 3, 3 ) ).Accepted );
		Assert.IsFalse( h.Economy.Staff.Candidates.Any( item => item.Id == stale.Id ), "as today" );
	}

	[TestMethod]
	public void AnEmployeeAnotherSeatCarriesIsNotDismissedAndOnesOwnLeavesTheHand()
	{
		var f = new Fixture();
		var member = f.Standing( StaffType.Handyman, 3, 3 );
		var own = f.Standing( StaffType.Guard, 6, 3 );
		Assert.IsTrue( f.As( 1, new PickUpStaffCommand( member.Id ) ).Accepted );
		foreach ( var seat in new[] { 0, 2 } )
		{
			var refused = f.As( seat, new FireStaffCommand( member.Id ) );
			Assert.IsFalse( refused.Accepted, $"seat {seat}" );
			Assert.AreEqual( FireStaffCommand.BeingMovedReason, refused.Reason );
		}
		Assert.IsTrue( f.Economy.Staff.Members.Contains( member ) );
		Assert.AreEqual( member.Id, f.Hands.Of( 1 )!.Value.Staff, "the pick-up's record first: the dismissal changed nothing" );
		Assert.IsTrue( f.As( 1, new FireStaffCommand( member.Id ) ).Accepted, "one's own carried employee" );
		Assert.IsNull( f.Hands.Of( 1 ), "the dismissal emptied the hand" );
		// the host's own carried employee, as a single player's
		Assert.IsTrue( f.As( 0, new PickUpStaffCommand( own.Id ) ).Accepted );
		Assert.IsTrue( f.As( 0, new FireStaffCommand( own.Id ) ).Accepted );
		Assert.IsNull( f.Hands.Of( 0 ) );
		Assert.IsFalse( f.As( 0, new DropStaffCommand( own.Id, 6, 3 ) ).Accepted, "nothing is left to put down" );
		// the dismissal's record first: the pick-up finds no employee and changes nothing
		var other = f.Standing( StaffType.Mechanic, 8, 3 );
		Assert.IsTrue( f.As( 0, new FireStaffCommand( other.Id ) ).Accepted );
		var late = f.As( 1, new PickUpStaffCommand( other.Id ) );
		Assert.IsFalse( late.Accepted );
		Assert.AreEqual( ((int?)null, false), (f.Hands.Of( 1 )?.Staff, f.Economy.Staff.Members.Contains( other )) );
	}

	[TestMethod]
	public void TheDropBackToWhereAnEmployeeWasPickedUpIsEverySeatsCleanup()
	{
		var f = new Fixture();
		var member = f.Standing( StaffType.Handyman, 3, 3 );
		var rights = new CoopBuildRights( f.Level );
		// a hand held without the staff right, set up directly: the drop back is allowed, a drop anywhere else is not
		Assert.IsTrue( f.Hands.Apply( new PickUpStaffCommand( member.Id ) { Player = 1 } ).Accepted );
		Assert.AreEqual( CoopRights.None, f.Level.RightsOf( 1 ) );
		Assert.IsTrue( rights.Allows( 1, new DropStaffCommand( member.Id, 3, 3 ) { Player = 1 } ), "the origin" );
		Assert.IsFalse( rights.Allows( 1, new DropStaffCommand( member.Id, 4, 3 ) { Player = 1 } ), "another cell needs the right" );
		Assert.AreEqual( CoopBuildRights.NotPermittedReason, rights.RefusalReason( 1, new DropStaffCommand( member.Id, 4, 3 ) { Player = 1 } ) );
		Assert.IsFalse( rights.Allows( 2, new DropStaffCommand( member.Id, 3, 3 ) { Player = 2 } ), "another seat's hand is not its cleanup" );
		// the staff right taken back returns the hand in its own record: a late drop back is refused harmlessly
		f.Level.SetSeatRights( 1, CoopRights.Staff );
		Assert.IsTrue( f.Hands.Apply( new DropStaffCommand( member.Id, 3, 3 ) { Player = 1 } ).Accepted );
		Assert.IsTrue( f.Hands.Apply( new PickUpStaffCommand( member.Id ) { Player = 1 } ).Accepted );
		f.Level.SetSeatRights( 1, CoopRights.None );
		Assert.IsNull( f.Hands.Of( 1 ) );
		Assert.AreEqual( (StaffState.Idle, 3.5f, 3.5f), (member.State, member.CellX!.Value, member.CellY!.Value) );
		Assert.IsFalse( f.Hands.Apply( new DropStaffCommand( member.Id, 3, 3 ) { Player = 1 } ).Accepted, "no hand left: nothing changes" );
		Assert.AreEqual( (3.5f, 3.5f), (member.CellX!.Value, member.CellY!.Value) );

		// through the command funnel under a session's rules: the seat without the right drops its employee back, and a drop elsewhere is refused before it runs
		f.Level.Commands.Rights = rights;
		f.Level.Commands.SessionRules = true;
		Assert.IsTrue( f.Hands.Apply( new PickUpStaffCommand( member.Id ) { Player = 1 } ).Accepted );
		var elsewhere = f.As( 1, new DropStaffCommand( member.Id, 4, 3 ) );
		Assert.IsFalse( elsewhere.Accepted );
		Assert.IsTrue( elsewhere.RightsRefused, $"{elsewhere.Reason}" );
		Assert.AreEqual( member.Id, f.Hands.Of( 1 )!.Value.Staff, "still in hand" );
		Assert.IsTrue( f.As( 1, new DropStaffCommand( member.Id, 3, 3 ) ).Accepted, "the drop back needs no right" );
		Assert.AreEqual( ((int?)null, StaffState.Idle, 3.5f, 3.5f), (f.Hands.Of( 1 )?.Staff, member.State, member.CellX!.Value, member.CellY!.Value) );
	}

	[TestMethod]
	public void ASeatsReleaseAndItsStaffRightTakenBackFreeItsHireAndReturnItsHand()
	{
		var f = new Fixture();
		var member = f.Standing( StaffType.Handyman, 3, 3 );
		var candidate = f.Candidate();
		Assert.IsTrue( f.Hands.Apply( new PickUpStaffCommand( member.Id ) { Player = 1 } ).Accepted );
		Assert.IsTrue( f.Hands.Apply( new BeginStaffHireCommand( candidate.Id ) { Player = 2 } ).Accepted );
		// a seat holding only staff state is found by both seat queries, so its departure and the host's teardown release it
		CollectionAssert.AreEqual( new[] { 1, 2 }, f.Level.HoldingGuestSeats().ToArray() );
		Assert.IsTrue( f.Level.SeatHoldsAnything( 1 ) && f.Level.SeatHoldsAnything( 2 ) );
		var released = f.Level.Commands.Apply( new ReleaseSeatCommand( 1 ) );
		Assert.IsTrue( released.Accepted && (bool)released.Value! );
		Assert.IsNull( f.Hands.Of( 1 ) );
		Assert.AreEqual( (StaffState.Idle, 3.5f, 3.5f), (member.State, member.CellX!.Value, member.CellY!.Value), "dropped back where it was picked up" );
		// the staff right taken back frees the seat's pending hire in the same record
		f.Level.SetSeatRights( 2, CoopRights.Staff | CoopRights.Finance );
		f.Level.SetSeatRights( 2, CoopRights.Finance );
		Assert.AreEqual( 0, f.PendingRows.Length );
		Assert.AreEqual( (false, false), (f.Level.SeatHoldsAnything( 1 ), f.Level.HoldingGuestSeats().Contains( 1 )) );
		Assert.IsTrue( f.Economy.Staff.Candidates.Any( item => item.Id == candidate.Id ), "the candidate stays in the pool" );
		// a teardown's cleanup outside the commands frees every guest seat's state, never the host's
		var host = f.Standing( StaffType.Guard, 6, 3 );
		Assert.IsTrue( f.Hands.Apply( new PickUpStaffCommand( host.Id ) ) .Accepted );
		Assert.IsTrue( f.Hands.Apply( new PickUpStaffCommand( member.Id ) { Player = 3 } ).Accepted );
		f.Level.ClearGuestStaff();
		Assert.AreEqual( (host.Id, (int?)null), (f.Hands.Of( 0 )!.Value.Staff, f.Hands.Of( 3 )?.Staff) );
	}
}
