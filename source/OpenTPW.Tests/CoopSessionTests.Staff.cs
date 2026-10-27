using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] What a park holds of a guest seat's staff, around the session (C4c-1): a save writes a guest's employee in hand as its drop back leaves it while the live park
/// keeps it in hand, the host's teardown releases a guest seat's staff state in records before its last checkpoint (or, inside a tick, after it), and the host's and a single player's own
/// staff tool follows what the park holds: a dismissed carried employee ends the tool, and a hire another seat's reservation refuses keeps it.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>A handyman hired on <paramref name="park"/> and standing idle on a free cell near (<paramref name="x"/>, <paramref name="y"/>), where it may be picked up.</summary>
	private static StaffMember StandingHandyman( Park park, int x = 18, int y = 12 )
	{
		park.Enter();
		var member = EconomyTestData.HireBest( park.Economy, StaffType.Handyman );
		var placement = park.Level.StaffPlacement!;
		var cell = Enumerable.Range( 0, 12 ).SelectMany( dx => Enumerable.Range( 0, 6 ).Select( dy => (X: x + dx, Y: y + dy) ) ).First( cell => placement.IsValid( cell.X, cell.Y ) );
		StaffPlacement.PutDown( member, cell.X, cell.Y );
		return member;
	}

	/// <summary>The fields of <paramref name="member"/>'s walk a put-down writes, to compare the live member before and after a save.</summary>
	private static (float? X, float? Y, double Travel, object? Navigation, StaffState State, int SpeedBonus, StaffJob Job, long IdleStamp, object Leg, int LegCount) WalkOf( StaffMember member ) =>
		(member.CellX, member.CellY, member.TravelSeconds, member.Navigation, member.State, member.SpeedBonus, member.Job, member.IdleStamp, member.OffGridLeg, member.OffGridLeg.Count);

	[TestMethod]
	public void GuestSeatsEmployeesAreSavedDroppedBackWhereTheyWerePickedUpAndTheLiveParkKeepsThemInHand()
	{
		var park = NewPark();
		var members = new[] { StandingHandyman( park, 18, 12 ), StandingHandyman( park, 30, 12 ) };
		var origins = members.Select( member => StaffPlacementRules.CellOf( member )!.Value ).ToArray();
		var hands = park.Level.StaffHands!;
		// seats 1 and 2 pick one up each, set up directly (no guest holds staff before C4c-2's switch), and each employee then has a walk of its own: a leg off the grid and a travel phase
		for ( var index = 0; index < members.Length; index++ )
		{
			var (member, origin) = (members[index], origins[index]);
			Assert.IsTrue( hands.Apply( new PickUpStaffCommand( member.Id ) { Player = index + 1 } ).Accepted );
			(member.CellX, member.CellY, member.TravelSeconds, member.SpeedBonus, member.Job, member.IdleStamp) = (origin.X + 2.25f, origin.Y + 0.75f, 0.125, 3, StaffJob.PickedUp, 77 + index);
			member.OffGridLeg.Add( (1, 2, 3, index) );
		}
		var before = members.Select( WalkOf ).ToArray();
		var snapshot = park.Level.CaptureResumeSnapshot( null, "shared", park.Level.AutosaveAssets() );
		for ( var index = 0; index < members.Length; index++ )
		{
			var (member, origin) = (members[index], origins[index]);
			var saved = snapshot.Economy.Staff!.Members!.Single( item => item.Id == member.Id );
			Assert.AreEqual( (origin.X + 0.5f, origin.Y + 0.5f, StaffState.Idle), (saved.CellX, saved.CellY, saved.State), $"seat {index + 1}'s employee in the economy, dropped back" );
			Assert.IsTrue( saved.WorldState is null || (saved.WorldState.Navigation == null && saved.WorldState.Job == StaffJob.Idle && saved.WorldState.SpeedBonus == 0
				&& saved.WorldState.IdleStamp == 0), $"seat {index + 1}'s employee: no walk left" );
			var actor = snapshot.Staff!.Actors.Single( item => item.Id == member.Id );
			Assert.AreEqual( (origin.X + 0.5f, origin.Y + 0.5f, 0.0), (actor.CellX, actor.CellY, actor.TravelSeconds), $"seat {index + 1}'s employee: the staff world's actor agrees" );
		}
		// the file the save writes reads back
		ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) );
		CollectionAssert.AreEqual( before, members.Select( WalkOf ).ToArray(), "the live members as they were" );
		Assert.AreEqual( ((members[0].Id, origins[0].X, origins[0].Y), (members[1].Id, origins[1].X, origins[1].Y)), (hands.Of( 1 )!.Value, hands.Of( 2 )!.Value), "and still in hand" );
		Assert.IsTrue( members.All( member => member.State == StaffState.PickedUp ) );

		// a capture that throws after the put-down leaves the live members as they were too
		var particles = Level.ParticlesCaptured;
		Level.ParticlesCaptured = _ => throw new InvalidOperationException( "a capture that fails" );
		try
		{
			Assert.ThrowsException<InvalidOperationException>( () => park.Level.CaptureResumeSnapshot( null, "shared", park.Level.AutosaveAssets() ) );
		}
		finally
		{
			Level.ParticlesCaptured = particles;
		}
		CollectionAssert.AreEqual( before, members.Select( WalkOf ).ToArray(), "the failed capture gave every field back" );

		// a join carries the employees as they are, with the hands
		var join = park.Level.CaptureJoinSnapshot();
		foreach ( var member in members )
			Assert.AreEqual( (StaffState.PickedUp, member.CellX), (join.Economy.Staff!.Members!.Single( item => item.Id == member.Id ).State, join.Economy.Staff.Members!.Single( item => item.Id == member.Id ).CellX) );
	}

	[TestMethod]
	public void TheHostsTeardownReleasesAGuestSeatsStaffStateInRecordsBeforeItsLastCheckpoint()
	{
		var (host, guest, _, _) = Shared();
		Rounds( 20, host, guest );
		var seat = guest.Level.Commands.Player;
		var ids = new (int Member, int Candidate)[2];
		// the guest's seat holds an employee and the next seat a pending hire, set up alike on every peer (no guest holds staff before C4c-2's switch)
		AlikeIn( host, guest, park =>
		{
			var member = StandingHandyman( park );
			var candidate = park.Economy.Staff.Candidates.First( item => park.Economy.Staff.CanHire( item.Type ) );
			Assert.IsTrue( park.Level.StaffHands!.Apply( new PickUpStaffCommand( member.Id ) { Player = seat } ).Accepted );
			Assert.IsTrue( park.Level.StaffHands.Apply( new BeginStaffHireCommand( candidate.Id ) { Player = seat + 1 } ).Accepted );
			ids[park == host ? 0 : 1] = (member.Id, candidate.Id);
		} );
		CatchUpAndCompare( host, guest );
		host.Enter();
		var hostStream = host.Session!.HostStream!;
		host.Session.End( "The host closed the park." );
		var log = hostStream.SessionLog!;
		var released = log.Entries.Where( entry => entry.Command is ReleaseSeatCommand ).Select( entry => ((ReleaseSeatCommand)entry.Command).Seat ).ToArray();
		CollectionAssert.AreEqual( new[] { seat, seat + 1 }, released, "each seat holding staff state was released in a record" );
		Assert.IsTrue( log.Entries.Where( entry => entry.Command is ReleaseSeatCommand ).All( entry => entry.Tick <= log.Checkpoints[^1].Ticks ), "before the last checkpoint" );
		Assert.AreEqual( host.Level.ComputeStateHash(), log.Checkpoints[^1].Hash, "the last checkpoint is the park after the releases" );
		Assert.AreEqual( (0, 0), (host.Level.StaffHands!.All.Count(), host.Economy.Staff.PendingHires.Count), "nothing of the seats' staff is left" );
		var member = host.Economy.Staff.Members.Single( item => item.Id == ids[0].Member );
		Assert.AreEqual( StaffState.Idle, member.State, "the employee dropped back where it was picked up" );
		Assert.IsTrue( host.Economy.Staff.Candidates.Any( item => item.Id == ids[0].Candidate ), "the candidate stays in the pool" );
		guest.Enter();
		guest.Session!.End( "The guest left." );
		Assert.AreEqual( (0, 0), (guest.Level.StaffHands!.All.Count(), guest.Economy.Staff.PendingHires.Count), "the guest's park clears them on its own" );
	}

	[TestMethod]
	public void AnEndInsideTheHostsTickRecordsNoReleaseAndClearsTheGuestSeatsStaffAfterTheLog()
	{
		var (host, guest, _, _) = Shared();
		Rounds( 20, host, guest );
		var seat = guest.Level.Commands.Player;
		AlikeIn( host, guest, park =>
		{
			var member = StandingHandyman( park );
			Assert.IsTrue( park.Level.StaffHands!.Apply( new PickUpStaffCommand( member.Id ) { Player = seat } ).Accepted );
		} );
		CatchUpAndCompare( host, guest );
		var session = host.Session!;
		var hostStream = session.HostStream!;
		// no game path ends a session inside a tick: the test seam does, outside a strict run, which throws at a command applied there
		var strict = ParkCommands.Strict;
		ParkCommands.Strict = false;
		ulong? afterEnd = null;
		try
		{
			host.Level.TickCompleted = _ =>
			{
				host.Level.TickCompleted = null;
				session.End( "The host closed the park." );
				afterEnd = host.Level.ComputeStateHash();
			};
			for ( var round = 0; round < 600 && session.EndedWith == null; round++ )
				Rounds( 1, host, guest );
		}
		finally
		{
			ParkCommands.Strict = strict;
			host.Level.TickCompleted = null;
		}
		Assert.IsNotNull( session.EndedWith, "the session ended inside a tick" );
		var log = hostStream.SessionLog!;
		Assert.IsFalse( log.Entries.Any( entry => entry.Command is ReleaseSeatCommand ), "no release was recorded inside the tick" );
		Assert.AreEqual( 0, host.Level.StaffHands!.All.Count(), "the park's own end returned the hand" );
		// the return came after the log's last checkpoint, the end's last change of the park: the checkpoint is the park with the employee still in hand
		Assert.AreNotEqual( afterEnd, log.Checkpoints[^1].Hash, "the last checkpoint is the park before the employee went back" );
	}

	[TestMethod]
	public void AHostsPickUpThenDismissalOfItsCarriedEmployeeReplaysToItsCheckpoints()
	{
		var id = 0;
		var (host, guest, _, _) = Shared( ( park, isHost ) =>
		{
			var member = StandingHandyman( park );
			if ( isHost )
				id = member.Id;
		} );
		Rounds( 20, host, guest );
		host.Enter();
		var carried = host.Economy.Staff.Members.Single( item => item.Id == id );
		var origin = StaffPlacementRules.CellOf( carried )!.Value;
		Assert.IsTrue( host.Level.StaffPlacement!.PickUp( carried ) );
		Rounds( 5, host, guest );
		Assert.IsTrue( host.Issue( new FireStaffCommand( id ) ).Accepted, "the host dismisses the employee it carries" );
		Assert.IsNull( host.Level.StaffHands!.Of( 0 ), "and its hand empties with it" );
		// a log of before C4c dropped the dismissed employee's hand by the tool's next click: that drop finds no hand now and changes nothing
		Assert.IsFalse( host.Issue( new DropStaffCommand( id, origin.X, origin.Y ) ).Accepted );
		host.Level.StaffPlacement.FollowPark();
		Assert.AreEqual( StaffToolKind.None, host.Level.StaffPlacement.Kind );
		Rounds( 20, host, guest );
		CatchUpAndCompare( host, guest );
		host.Enter();
		var hostStream = host.Session!.HostStream!;
		host.Session.End( "The host closed the park." );
		var log = ParkCommandLogFile.Deserialize( hostStream.SessionLog!.Serialize() );
		Assert.IsTrue( log.Entries.Any( entry => entry.Command is FireStaffCommand ), "the dismissal is in the log" );
		Assert.IsNull( FirstReplayMismatch( log, park => StandingHandyman( park ) ), "the log replays to every checkpoint" );
	}

	[TestMethod]
	public void AHostsOrSinglePlayersCarryToolEndsWhenItsEmployeeIsDismissedAndAHireAnotherSeatReservesKeepsItsTool()
	{
		var park = NewPark();
		var member = StandingHandyman( park );
		var placement = park.Level.StaffPlacement!;
		Assert.IsTrue( placement.PickUp( member ) );
		Assert.AreEqual( StaffToolKind.Carry, placement.Kind );
		// a hire of a candidate another seat places is refused before the running tool ends
		var reserved = park.Economy.Staff.Candidates.First( item => park.Economy.Staff.CanHire( item.Type ) );
		Assert.IsTrue( park.Level.StaffHands!.Apply( new BeginStaffHireCommand( reserved.Id ) { Player = 2 } ).Accepted );
		Assert.IsFalse( placement.BeginHire( reserved.Id ) );
		Assert.AreEqual( (StaffToolKind.Carry, member.Id), (placement.Kind, park.Level.StaffHands.Of( 0 )!.Value.Staff), "the carry stays" );
		var other = park.Economy.Staff.Candidates.First( item => item.Id != reserved.Id && park.Economy.Staff.CanHire( item.Type ) );
		Assert.IsTrue( placement.BeginHire( other.Id ), "a free candidate: the carry drops back first" );
		Assert.IsFalse( placement.BeginHire( reserved.Id ) );
		Assert.AreEqual( (StaffToolKind.Place, other.Id), (placement.Kind, park.Economy.Staff.PendingPlacementId), "the pending hire stays" );
		placement.Cancel();

		// the carried employee dismissed from its window: the tool follows the park at the next frame, and a click places nothing
		Assert.IsTrue( placement.PickUp( member ) );
		Assert.IsTrue( park.Issue( new FireStaffCommand( member.Id ) ).Accepted );
		Assert.IsNull( park.Level.StaffHands.Of( 0 ), "the dismissal emptied the hand" );
		placement.FollowPark();
		Assert.AreEqual( StaffToolKind.None, placement.Kind );
		var cell = StaffPlacementRules.CellOf( park.Economy.Staff.Members.First() ) ?? (18, 12);
		Assert.AreEqual( StaffClickResult.None, placement.Click( cell.X, cell.Y ) );
		// a Place tool whose pending hire went (a load, a release) ends too
		Assert.IsTrue( placement.BeginHire( other.Id ) );
		park.Economy.Staff.PendingPlacementId = 0;
		placement.FollowPark();
		Assert.AreEqual( StaffToolKind.None, placement.Kind );
	}
}
